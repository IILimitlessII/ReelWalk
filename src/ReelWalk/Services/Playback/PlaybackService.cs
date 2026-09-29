using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReelWalk.Models;

namespace ReelWalk.Services;
internal sealed partial class PlaybackService : IDisposable
{
    private readonly ImageLibrary imageManager;
    private readonly TransitionService transitionEngine;
    private readonly SlideVideo video;
    private readonly SlideshowConfig config;
    private readonly DispatcherTimer timer;
    private readonly string configPath;

    private double displayDuration;
    private double transitionDurationPercent;
    private bool isPaused;
    private bool firstImage = true;
    private bool videoPlaying;
    private bool playbackStarted;
    private bool uiHold;
    private bool advanceAfterHold;
    private bool videoHeld;
    private double volumeBeforeMute = 1;
    private readonly VideoSeekService videoSeek;
    private readonly Queue<LibraryPublish> pendingPublish = new Queue<LibraryPublish>();
    private int publishScheduled;
    private long lastProgressUi;
    private readonly object publishGate = new object();
    private string libraryCachePath;
    private bool autoRefreshStarted;
    private int refreshRunning;
    private DispatcherTimer libraryRefreshTimer;
    private DispatcherTimer watchFlushTimer;
    private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
    private readonly List<string> watchAdded = new List<string>();
    private readonly List<string> watchRemoved = new List<string>();
    private readonly object watchGate = new object();
    private int videoToken;
    private int videoOpenedToken = -1;
    private int armedVideoToken = -1;
    private string armedVideoPath;
    private bool videoEventsHooked;

    internal event Action<int> FilesAdded;

    internal bool IsPaused { get { return isPaused; } }
    internal bool IsVideoPlaying { get { return videoPlaying; } }
    internal int ImageCount { get { return imageManager != null ? imageManager.TotalImages : 0; } }

    internal event EventHandler ImageChanged;

    internal event EventHandler SeekPreviewChanged;

    internal TimeSpan VideoDisplayPosition
    {
        get { return videoSeek != null ? videoSeek.DisplayPosition : TimeSpan.Zero; }
    }

    internal double DisplayDurationSeconds { get { return displayDuration; } }
    internal double CurrentVideoVolume { get { return config.VideoVolume; } }

    // Connects the library, transitions, video, and the photo timer.
    // cfg is the loaded toml. Returns nothing.
    internal PlaybackService(
        Image image1, Image image2,
        ScaleTransform scale1, ScaleTransform scale2,
        TranslateTransform translate1, TranslateTransform translate2,
        SlideVideo player,
        SlideshowConfig cfg)
    {
        config = cfg;
        displayDuration = config.DisplayDuration;
        transitionDurationPercent = config.TransitionDurationPercent;
        video = player;

        configPath = ConfigService.FilePath;

        imageManager = new ImageLibrary(
            config.PlaybackMode,
            config.LastImageIndex,
            config.KenBurnsMaxZoom);
        imageManager.TrySetMediaShow(config.MediaShow);
        imageManager.SetFolderIncludeSubfolders(config.FolderIncludeSubfolders);
        imageManager.SetBackHistory(config.BackHistory);
        imageManager.StillDecoded += ShowCurrentImage;

        transitionEngine = new TransitionService(
            image1, image2,
            scale1, scale2,
            translate1, translate2,
            player)
        {
            EnableKenBurns = config.EnableKenBurns,
            KenBurnsMaxZoom = config.KenBurnsMaxZoom,
            VideoVolume = config.VideoVolume,
            TransitionPool = ParseTransitionPool(config.ActiveTransitions)
        };

        timer = new DispatcherTimer();
        timer.Interval = TimeSpan.FromSeconds(displayDuration);
        timer.Tick += Timer_Tick;

        videoSeek = new VideoSeekService(video, () => isPaused);
        videoSeek.PreviewChanged += OnSeekPreview;
    }

    // Raises the seek preview so the time label can follow the pointer.
    // position is the pending time. Returns nothing.
    private void OnSeekPreview(TimeSpan position)
    {
        var handler = SeekPreviewChanged;
        if (handler != null)
            handler(this, EventArgs.Empty);
    }

    // Shows the last file first, then finishes the folder scan.
    // ready runs on the UI thread the first time there is something to play. Returns the file count.
    internal Task<int> InitAsync(Action<int> progressCallback, Action ready)
    {
        var dispatcher = Dispatcher.UIThread;
        libraryCachePath = Path.Combine(
            Path.GetDirectoryName(configPath),
            "ReelWalk.library");
        string cacheFile = libraryCachePath;

        return imageManager.InitAsync(
            config.ImagePaths,
            config.IgnorePaths,
            cacheFile,
            config.LastImagePath,
            count =>
            {
                long now = Environment.TickCount64;
                if (count > 0 && now - lastProgressUi < 1000)
                    return;
                lastProgressUi = now;
                if (progressCallback == null) return;
                try { dispatcher.Post(() => progressCallback(count), DispatcherPriority.Background); }
                catch (InvalidOperationException) { }
            },
            (files, authoritative, stage) =>
            {
                EnqueuePublish(dispatcher, ready, files, authoritative, stage);
            });
    }

    // Queues a library update so the scan cannot block the UI.
    // authoritative true is a finished cache or scan. stage drops older results. Returns nothing.
    private void EnqueuePublish(
        Dispatcher dispatcher,
        Action ready,
        List<string> files,
        bool authoritative,
        int stage)
    {
        bool start;
        lock (publishGate)
        {
            pendingPublish.Enqueue(new LibraryPublish
            {
                Files = files,
                Authoritative = authoritative,
                Stage = stage,
                Ready = ready
            });
            start = publishScheduled == 0;
            publishScheduled = 1;
        }

        if (start)
        {
            try { dispatcher.Post(() => DrainPublish(dispatcher), DispatcherPriority.Background); }
            catch (InvalidOperationException) { publishScheduled = 0; }
        }
    }

    // Applies a few queued library updates, then continues on a background turn.
    // dispatcher is the UI thread. Returns nothing.
    private void DrainPublish(Dispatcher dispatcher)
    {
        LibraryPublish item;
        lock (publishGate)
        {
            if (pendingPublish.Count == 0)
            {
                publishScheduled = 0;
                return;
            }
            item = pendingPublish.Dequeue();
        }

        ApplyPublish(item);

        try { dispatcher.Post(() => DrainPublish(dispatcher), DispatcherPriority.Background); }
        catch (InvalidOperationException) { }
    }

    // Installs one queued file list and shows a picture when this is the first list.
    // item is that update. Returns nothing.
    private void ApplyPublish(LibraryPublish item)
    {
        LibraryUpdate update;
        if (!imageManager.HasFiles && item.Files != null && item.Files.Count > 0)
            update = imageManager.ApplyLibrary(item.Files, item.Authoritative, item.Stage);
        else
            update = imageManager.AbsorbFiles(item.Files, item.Authoritative);

        if (!playbackStarted && imageManager.TotalImages > 0)
        {
            playbackStarted = true;
            if (item.Ready != null)
                item.Ready();
        }
        else if (playbackStarted && update == LibraryUpdate.NeedsRedisplay)
        {
            ShowCurrentImage();
        }

        if (item.Authoritative && item.Stage >= ImageLibrary.LibraryStageScan)
            EnsureAutoRefresh();
    }

    private struct LibraryPublish
    {
        internal List<string> Files;
        internal bool Authoritative;
        internal int Stage;
        internal Action Ready;
    }

    // Shows the first file and starts the photo timer.
    // Returns nothing.
    internal void Start()
    {
        ShowCurrentImage();
        timer.Start();
    }

    // Advances to the next file when a photo's time is up.
    // Returns nothing while paused, held, or a video is playing.
    private void Timer_Tick(object sender, EventArgs e)
    {
        if (isPaused || uiHold)
            return;
        imageManager.Next();
        ShowCurrentImage();
        ResetTimer();
    }

    // Advances after the video that is actually playing.
    // Ignores the end event raised by stopping a video. Returns nothing.
    private void VideoPlayer_MediaEnded(object sender, EventArgs e)
    {
        if (!videoPlaying || armedVideoToken < 0)
            return;
        if (isPaused)
            return;
        if (uiHold)
        {
            advanceAfterHold = true;
            return;
        }
        imageManager.Next();
        ShowCurrentImage();
        ResetTimer();
    }

    // Pauses the photo timer and the current video while a menu is open.
    // hold false resumes that video, or moves on if it ended during the hold. Returns nothing.
    internal void SetUiHold(bool hold)
    {
        uiHold = hold;
        if (hold)
        {
            timer.Stop();
            if (videoPlaying && video != null && !isPaused)
            {
                video.Pause();
                videoHeld = true;
            }
            return;
        }

        if (advanceAfterHold && !isPaused)
        {
            advanceAfterHold = false;
            videoHeld = false;
            imageManager.Next();
            ShowCurrentImage();
            ResetTimer();
            return;
        }

        if (videoHeld)
        {
            videoHeld = false;
                if (videoPlaying && video != null && !isPaused)
                    video.Resume();
        }

        if (!isPaused && !videoPlaying && playbackStarted)
            timer.Start();
    }

    private int showDepth;

    // Shows the current file, and bails out if show calls re-enter.
    // Returns nothing.
    private void ShowCurrentImage()
    {
        if (showDepth > 12)
            return;
        showDepth++;
        try
        {
            ShowCurrentImageCore();
        }
        finally
        {
            showDepth--;
        }
    }

    // Skips a still that will not decode, or presents the current file.
    // Returns nothing.
    private void ShowCurrentImageCore()
    {
        int budget = imageManager.TotalImages;
        if (budget <= 0)
            return;

        for (int i = 0; i < budget; i++)
        {
            if (imageManager.TotalImages <= 0)
                return;

            if (imageManager.IsCurrentVideo())
            {
                PresentCurrent(null);
                return;
            }

            var image = imageManager.FetchCurrentStill(out Bitmap still);
            if (image == StillFetch.Ready)
            {
                PresentCurrent(still);
                return;
            }
            if (image == StillFetch.Waiting)
                return;

            if (!imageManager.DropUnplayableCurrent())
                return;
        }
    }

    // Shows image, or the current video when image is null.
    // Returns nothing.
    private void PresentCurrent(Bitmap image)
    {
        if (videoSeek != null)
            videoSeek.Cancel();

        bool isVideo = image == null && imageManager.IsCurrentVideo();
        if (!isVideo)
            QuietVideo();
        else
            UnhookVideo();

        if (isVideo)
        {
            var path = imageManager.GetCurrentImagePath();
            if (path == null) return;

            videoPlaying = true;
            armedVideoToken = ++videoToken;
            armedVideoPath = path;
            videoOpenedToken = -1;
            timer.Stop();
            firstImage = false;
            HookVideo();
            transitionEngine.ShowFirstVideo(path);
            if (uiHold && !isPaused)
            {
                video.Pause();
                videoHeld = true;
            }
        }
        else
        {
            if (image == null)
                return;

            if (firstImage)
            {
                firstImage = false;
                transitionEngine.ShowFirstImage(image, displayDuration);
            }
            else
            {
                double transitionDur = displayDuration * transitionDurationPercent / 100.0;
                transitionEngine.TransitionToImage(image, transitionDur, displayDuration);
            }
        }

        var handler = ImageChanged;
        if (handler != null)
            handler(this, EventArgs.Empty);
    }

    // Detaches video events and stops the player.
    // Returns nothing. A stop then cannot be read as the video finishing or failing.
    private void QuietVideo()
    {
        UnhookVideo();
        videoPlaying = false;
        videoHeld = false;
        armedVideoToken = -1;
        armedVideoPath = null;
        videoOpenedToken = -1;
        if (transitionEngine != null)
            transitionEngine.HideVideoImmediate();
    }

    // Listens for open, end, and failure on the video that is about to start.
    // Returns nothing.
    private void HookVideo()
    {
        if (video == null || videoEventsHooked)
            return;
        video.Ended += VideoPlayer_MediaEnded;
        video.Failed += VideoPlayer_MediaFailed;
        video.Opened += VideoPlayer_MediaOpened;
        video.Hook();
        videoEventsHooked = true;
    }

    // Stops listening so a stop or a source change is ignored.
    // Returns nothing.
    private void UnhookVideo()
    {
        if (video == null || !videoEventsHooked)
            return;
        video.Unhook();
        video.Ended -= VideoPlayer_MediaEnded;
        video.Failed -= VideoPlayer_MediaFailed;
        video.Opened -= VideoPlayer_MediaOpened;
        videoEventsHooked = false;
    }

    // Marks the armed video as opened so a late failure is ignored.
    // Returns nothing.
    private void VideoPlayer_MediaOpened(object sender, EventArgs e)
    {
        if (!videoPlaying || armedVideoToken < 0)
            return;
        videoOpenedToken = armedVideoToken;
    }

    // Drops the video that failed, and ignores a failure for a file that already moved on.
    // Returns nothing.
    private void VideoPlayer_MediaFailed(object sender, EventArgs e)
    {
        if (!videoPlaying || armedVideoToken < 0 || armedVideoToken != videoToken)
            return;
        if (videoOpenedToken == armedVideoToken)
            return;
        if (!Paths.Same(armedVideoPath, imageManager.GetCurrentImagePath()))
            return;

        armedVideoToken = -1;
        if (!imageManager.DropUnplayableCurrent())
            return;
        ShowCurrentImage();
        ResetTimer();
    }



}
