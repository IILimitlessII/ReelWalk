using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Media.Imaging;
using System.Threading.Tasks;
using ReelWalk.Models;
using ReelWalk.Services.Playback;

namespace ReelWalk.Services;
internal enum StillFetch
{
    Ready,
    Waiting,
    Missing
}

internal sealed partial class ImageLibrary : IDisposable
{
    private List<string> allImagePaths;
    private List<string> playbackOrder;
    private readonly Dictionary<string, int> playbackIndex =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> knownPaths;
    private int currentIndex;
    private readonly WatchHistory history = new WatchHistory();
    private string currentMode;
    private string mediaShow = "Both";
    private readonly Random random = new Random();

    private bool folderPlayActive;
    private List<string> savedPlaybackOrder;
    private int savedIndex;
    private string savedMode;
    private int folderRemaining;
    private int folderPlayGeneration;
    private bool folderListFilling;
    private string folderPlayMode = "Random";
    private string folderPlayPath;
    private bool folderIncludeSubfolders = true;
    private List<string> normalizedAll;
    private int upcomingRandom = -1;
    private readonly LinkedList<string> recentOrder = new LinkedList<string>();
    private readonly HashSet<string> recentSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<int, Bitmap> imageCache =
        new ConcurrentDictionary<int, Bitmap>();
    private readonly ConcurrentDictionary<string, byte> decodeInflight =
        new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> failedStills =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Bitmap> retired = new List<Bitmap>();
    private const int PRELOAD_COUNT = 4;
    private bool libraryReleased;

    internal event Action StillDecoded;

    private readonly int decodePixelWidth;
    private readonly ImageDecoder imageDecoder;
    private readonly object randomGate = new object();

    private bool showing;
    private bool provisional = true;
    private int appliedStage;
    private string resumePath;

    internal const int LibraryStageResume = 1;
    internal const int LibraryStageCache = 2;
    internal const int LibraryStageScan = 3;

    private long cachedFileSize;
    private DateTime cachedFileDate;
    private string cachedFilePath;

    // True when the file on screen is a video.
    // Returns false when nothing is showing.
    internal bool IsCurrentVideo()
    {
        return MediaTypes.IsVideo(GetCurrentImagePath());
    }

    internal int TotalImages { get { return playbackOrder != null ? playbackOrder.Count : 0; } }
    internal bool HasFiles { get { return allImagePaths != null && allImagePaths.Count > 0; } }
    internal string MediaShow { get { return mediaShow; } }
    internal int CurrentIndex { get { return currentIndex; } }
    internal string CurrentMode { get { return currentMode; } }
    internal bool IsFolderPlay { get { return folderPlayActive; } }
    internal string FolderPlayMode { get { return folderPlayMode; } }
    internal string FolderResumeMode { get { return folderPlayActive ? savedMode : currentMode; } }

    // Prepares an empty library. It does not scan.
    // mode is the playlist order. startIndex is restored later. kenBurnsMaxZoom widens the decode.
    internal ImageLibrary(string mode, int startIndex, double kenBurnsMaxZoom)
    {
        double zoom = Math.Max(kenBurnsMaxZoom, 1.0);
        decodePixelWidth = (int)(ScreenWidth() * zoom);
        imageDecoder = new ImageDecoder(decodePixelWidth);

        currentMode = mode;
        currentIndex = Math.Max(0, startIndex);
        allImagePaths = new List<string>();
        playbackOrder = new List<string>();
        knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    // How many files Back can return through in Random.
    // limit below 1 becomes 10. Returns nothing.
    internal void SetBackHistory(int limit)
    {
        history.SetLimit(limit);
    }

    // Loads the saved library, then scans for anything new.
    // progress receives the walked file count and whether a saved library already exists.
    // publish runs on this background thread. The stage must not replace a newer scan. Returns the file count.
    internal Task<int> InitAsync(
        List<string> imagePaths,
        IList<string> ignorePaths,
        string cacheFile,
        string resumePath,
        Action<int, bool> progressCallback,
        Action<List<string>, bool, int> publish)
    {
        this.resumePath = resumePath;
        if (LibraryScanner.IsIgnored(resumePath, ignorePaths))
            this.resumePath = null;

        return Task.Run(() =>
        {
            bool hasSavedLibrary = LibraryCache.HasEntries(cacheFile);
            if (progressCallback != null)
                progressCallback(0, hasSavedLibrary);

            if (!string.IsNullOrEmpty(this.resumePath) &&
                IsQuickLocalFile(this.resumePath) && publish != null)
                publish(new List<string> { this.resumePath }, false, LibraryStageResume);

            var cached = LibraryScanner.FilterIgnored(LibraryCache.Load(cacheFile), ignorePaths);
            var cacheTask = Task.Run(() =>
            {
                if (cached == null || cached.Count == 0 || publish == null)
                    return;

                const int slice = 200;
                for (int i = 0; i < cached.Count; i += slice)
                {
                    int n = Math.Min(slice, cached.Count - i);
                    var batch = new List<string>(n);
                    for (int j = 0; j < n; j++)
                        batch.Add(cached[i + j]);
                    publish(batch, false, LibraryStageCache);
                    if (i + n < cached.Count)
                        Thread.Sleep(25);
                }
            });

            var found = LibraryScanner.Scan(imagePaths, ignorePaths, count =>
            {
                if (progressCallback != null)
                    progressCallback(count, hasSavedLibrary);
            }, batch =>
            {
                if (publish != null)
                    publish(batch, false, LibraryStageScan);
            });

            try { cacheTask.Wait(); }
            catch { }

            if (publish != null)
                publish(null, true, LibraryStageScan);

            // Keep the indexed list. Only rewrite when the scan found paths that were not cached.
            if (AnyRootExists(imagePaths))
            {
                var toSave = LibraryCache.MergeNew(cached, found);
                if (toSave != null)
                    LibraryCache.Save(cacheFile, toSave);
            }

            if (found != null && found.Count > 0)
                return found.Count;
            return cached == null ? 0 : cached.Count;
        });
    }

    // Replaces the playlist.
    // authoritative false must not wipe a larger library. stage drops older results. Returns whether the UI should refresh.
    internal LibraryUpdate ApplyLibrary(List<string> orderedFiles, bool authoritative, int stage)
    {
        if (orderedFiles == null)
            orderedFiles = new List<string>();

        if (stage < appliedStage)
        {
            bool scanLockedIn = appliedStage >= LibraryStageScan && !provisional;
            if (scanLockedIn || allImagePaths == null || orderedFiles.Count <= allImagePaths.Count)
                return LibraryUpdate.Unchanged;
        }

        if (!authoritative && showing && allImagePaths != null)
        {
            if (allImagePaths.Count > orderedFiles.Count)
                return LibraryUpdate.Unchanged;
            string keepNow = GetCurrentImagePath();
            if (!string.IsNullOrEmpty(keepNow) && FindPath(orderedFiles, keepNow) < 0)
                return LibraryUpdate.Unchanged;
        }

        if (authoritative && !provisional && showing && SameFileSet(allImagePaths, orderedFiles))
        {
            appliedStage = stage;
            return LibraryUpdate.Unchanged;
        }

        string keep = showing ? GetCurrentImagePath() : null;
        SetLibraryFiles(new List<string>(orderedFiles));
        playbackOrder = OnlyAllowed(orderedFiles);

        if (currentMode == "Random" && playbackOrder.Count > 1)
            ShuffleInPlace(playbackOrder);
        RebuildPlaybackIndex();

        LibraryUpdate result = LibraryUpdate.Updated;
        if (!string.IsNullOrEmpty(keep))
        {
            int idx = FindPath(playbackOrder, keep);
            if (idx >= 0)
                currentIndex = idx;
            else if (playbackOrder.Count == 0)
            {
                currentIndex = 0;
                result = LibraryUpdate.NeedsRedisplay;
            }
            else
            {
                currentIndex = PickRandomIndex(-1);
                result = LibraryUpdate.NeedsRedisplay;
            }
        }
        else if (playbackOrder.Count == 0)
        {
            currentIndex = 0;
        }
        else
        {
            int resume = FindPath(playbackOrder, resumePath);
            if (resume >= 0)
                currentIndex = resume;
            else
                currentIndex = PickRandomIndex(-1);
        }

        showing = playbackOrder.Count > 0;
        provisional = !authoritative;
        appliedStage = stage;
        knownPaths = new HashSet<string>(allImagePaths, StringComparer.OrdinalIgnoreCase);

        history.Clear();
        PrepareShown();
        return result;
    }

    // Adds newly found files. Random can play what is already in.
    // complete true is the end of a scan. Returns whether the UI should refresh.
    internal LibraryUpdate AbsorbFiles(IList<string> files, bool complete)
    {
        var added = new List<string>();
        if (files != null)
        {
            if (knownPaths == null)
                knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Count; i++)
            {
                var path = files[i];
                if (string.IsNullOrEmpty(path))
                    continue;
                if (knownPaths.Add(path))
                    added.Add(path);
            }
        }

        if (added.Count > 0)
        {
            AppendLibraryFiles(added);

            var playable = OnlyAllowed(added);
            if (folderPlayActive)
            {
                if (savedPlaybackOrder != null && playable.Count > 0)
                {
                    if (string.Equals(savedMode, "Random", StringComparison.OrdinalIgnoreCase))
                        ShuffleInPlace(playable);
                    savedPlaybackOrder.AddRange(playable);
                }

                var live = new List<string>();
                var folderNorm = Paths.Normalize(folderPlayPath);
                for (int i = 0; i < playable.Count; i++)
                {
                    if (folderNorm != null && BelongsInFolderPlay(playable[i], folderNorm))
                        live.Add(playable[i]);
                }
                if (live.Count > 0 && playbackOrder != null)
                {
                    int from = playbackOrder.Count;
                    playbackOrder.AddRange(live);
                    NotePlaybackAdded(from);
                    folderRemaining += live.Count;
                }
            }
            else if (string.Equals(currentMode, "Random", StringComparison.OrdinalIgnoreCase))
            {
                int from = playbackOrder.Count;
                playbackOrder.AddRange(playable);
                NotePlaybackAdded(from);
            }
            else if (complete)
            {
                var keep = GetCurrentImagePath();
                playbackOrder = BuildPlaybackOrder(allImagePaths, currentMode);
                RebuildPlaybackIndex();
                history.Clear();
                int idx = FindPath(playbackOrder, keep);
                currentIndex = idx >= 0 ? idx : Math.Max(0, Math.Min(currentIndex, playbackOrder.Count - 1));
            }
            else
            {
                int from = playbackOrder.Count;
                playbackOrder.AddRange(playable);
                NotePlaybackAdded(from);
            }
        }

        if (complete)
        {
            provisional = false;
            appliedStage = LibraryStageScan;
            if (!folderPlayActive &&
                added.Count == 0 &&
                allImagePaths.Count > 0 &&
                !string.Equals(currentMode, "Random", StringComparison.OrdinalIgnoreCase))
            {
                var keep = GetCurrentImagePath();
                playbackOrder = BuildPlaybackOrder(allImagePaths, currentMode);
                RebuildPlaybackIndex();
                history.Clear();
                int idx = FindPath(playbackOrder, keep);
                currentIndex = idx >= 0 ? idx : Math.Max(0, Math.Min(currentIndex, playbackOrder.Count - 1));
            }
        }

        showing = playbackOrder != null && playbackOrder.Count > 0;
        if (added.Count == 0 && !complete)
            return LibraryUpdate.Unchanged;

        return LibraryUpdate.Updated;
    }

    // Files that match the photos, videos, or both filter.
    // source is the full list. Returns the kept paths.
    private List<string> OnlyAllowed(IList<string> source)
    {
        var list = new List<string>();
        if (source == null)
            return list;
        for (int i = 0; i < source.Count; i++)
        {
            if (MediaTypes.Allows(source[i], mediaShow))
                list.Add(source[i]);
        }
        return list;
    }

    // Limits playback to photos, videos, or both.
    // show is the choice. Returns false when that choice has no files.
    internal bool TrySetMediaShow(string show)
    {
        show = MediaTypes.NormalizeShow(show);
        if (allImagePaths != null && allImagePaths.Count > 0)
        {
            string previous = mediaShow;
            mediaShow = show;
            if (OnlyAllowed(allImagePaths).Count == 0)
            {
                mediaShow = previous;
                return false;
            }
        }
        else
        {
            mediaShow = show;
            RebuildFolderIndex();
            return true;
        }

        var keep = GetCurrentImagePath();
        if (!MediaTypes.Allows(keep, mediaShow))
            keep = null;

        if (folderPlayActive)
        {
            string resumeMode = string.IsNullOrEmpty(savedMode) ? "Random" : savedMode;
            savedPlaybackOrder = BuildPlaybackOrder(allImagePaths, resumeMode);
            var folderFiles = CollectFolderFiles(folderPlayPath);
            if (folderFiles.Count == 0)
            {
                ExitFolderPlay();
            }
            else
            {
                OrderFolderFiles(folderFiles, folderPlayMode);
                playbackOrder = folderFiles;
                RebuildPlaybackIndex();
                PlaceOn(keep);
                folderRemaining = Math.Max(0, playbackOrder.Count - 1);
            }
        }
        else
        {
            playbackOrder = BuildPlaybackOrder(allImagePaths, currentMode);
            RebuildPlaybackIndex();
            PlaceOn(keep);
        }

        showing = playbackOrder != null && playbackOrder.Count > 0;
        upcomingRandom = -1;
        history.Clear();
        RebuildFolderIndex();
        PrepareShown();
        return true;
    }



}
