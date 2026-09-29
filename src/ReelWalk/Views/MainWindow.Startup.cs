using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SkiaSharp;
using ReelWalk.Services;

namespace ReelWalk.Views;
public partial class MainWindow
{
    // Opens fullscreen, loads the library, and starts playback.
    // Returns nothing.
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ConfigService.NeedsSetup)
            {
                Close();
                return;
            }

            Activate();
            ClaimKeyboardFocus();
            _viewModel.CopyText = text =>
            {
                var clipboard = Clipboard;
                if (clipboard == null)
                    return System.Threading.Tasks.Task.CompletedTask;
                return clipboard.SetTextAsync(text);
            };

            var configPath = ConfigService.FilePath;
            var config = ConfigService.LoadConfig(configPath);
            ApplyImageFit(config.ImageFit);
            _slideVideo = new SlideVideo(VideoPlayer);
            _slideVideo.SetCover(_cover);
            _slideVideo.Volume = config.VideoVolume;
            _slideVideo.SurfaceShown += (s, e2) => ClaimKeyboardFocus();
            _slideVideo.Opened += (s, e2) => ClaimKeyboardFocus();
            LoadControls(config);

            if (config.ImagePaths.Count == 0)
            {
                await Notice.Show(this,
                    "ReelWalk - Configuration Error",
                    "No image paths configured.\n\nPlease edit ReelWalk.toml and add at least one folder path.");
                Close();
                return;
            }

            var playback = new PlaybackService(
                Image1, Image2,
                ScaleOf(Image1), ScaleOf(Image2),
                TranslateOf(Image1), TranslateOf(Image2),
                _slideVideo,
                config);
            _viewModel.AttachPlayback(playback);

            playback.FilesAdded += count =>
            {
                _viewModel.ShowToast(count == 1 ? "1 new file" : string.Format("{0:N0} new files", count), false);
            };
            playback.ImageChanged += (s2, e2) =>
            {
                _viewModel.RefreshInfo();
                _viewModel.RefreshPath();
                RefreshVideoProgress();
            };
            playback.SeekPreviewChanged += (s2, e2) =>
            {
                var position = playback.VideoDisplayPosition;
                var duration = VideoDuration();
                _viewModel.ShowSeekPreview(position, duration);
                _viewModel.UpdateVideoProgress(true, position, duration);
                _toastTimer.Stop();
                _toastTimer.Start();
            };

            _viewModel.ShowLoading("Starting…");

            int imageCount = await playback.InitAsync(
                count =>
                {
                    if (!_started)
                    {
                        _viewModel.ShowLoading(count > 0
                            ? string.Format("Scanning… {0:N0}", count)
                            : "Scanning…");
                    }
                    else if (count > 0)
                    {
                        _viewModel.ShowIndex(string.Format("Updating library… {0:N0}", count));
                    }
                },
                BeginPlayback);

            _viewModel.HideLoading();
            _viewModel.HideIndex();

            if (!_started)
            {
                await Notice.Show(this,
                    "ReelWalk",
                    "Nothing to play for " + MediaTypes.ShowLabel(config.MediaShow) + ".\n\n" +
                    "Please check that:\n" +
                    "1. Paths in ReelWalk.toml are correct\n" +
                    "2. show is Both, Images, or Videos\n" +
                    "3. You have permission to access the folders");
                Close();
                return;
            }

            if (imageCount == 0)
            {
                await Notice.Show(this,
                    "ReelWalk",
                    "Nothing to play for " + MediaTypes.ShowLabel(config.MediaShow) + ".");
                Close();
            }
        }
        catch (Exception ex)
        {
            await Notice.Show(this,
                "ReelWalk - Error",
                "Error initializing slideshow:\n\n" + ex.Message);
            Close();
        }
    }

    // Starts the timer after the first file is on screen.
    // Returns nothing.
    private void BeginPlayback()
    {
        if (_started || _viewModel.Playback == null)
            return;

        _started = true;
        _viewModel.HideLoading();
        _viewModel.ShowIndex("Checking for new files…");

        SetupTrayIcon();
        _viewModel.Playback.Start();
        ClaimKeyboardFocus();

        _videoProgressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _videoProgressTimer.Tick += (s, args) => RefreshVideoProgress();
        RefreshVideoProgress();

        _viewModel.ShowHint();
        _hintTimer.Start();
    }

    // Adds the tray menu.
    // Returns nothing.
    private void SetupTrayIcon()
    {
        var menu = new NativeMenu();
        menu.Add(MenuItem("Pause / Resume", () => _viewModel.TogglePauseCommand.Execute(null)));
        menu.Add(MenuItem("Next", () => _viewModel.NextCommand.Execute(null)));
        menu.Add(MenuItem("Previous", () => _viewModel.PreviousCommand.Execute(null)));
        menu.Add(MenuItem("Folders", () => _viewModel.ToggleFolderMenu()));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(MenuItem("Edit config", OpenConfig));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(MenuItem("Exit", Close));

        _trayIcon = new TrayIcon
        {
            Icon = CreateTrayIcon(),
            ToolTipText = "ReelWalk v1.0",
            Menu = menu,
            IsVisible = true
        };
        var icons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(Avalonia.Application.Current, icons);
    }

    // Builds one tray menu row.
    // action runs on click. Returns the item.
    private static NativeMenuItem MenuItem(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (s, e) =>
        {
            if (action != null)
                action();
        };
        return item;
    }

    // Builds a small tray icon.
    // Returns the icon.
    private static WindowIcon CreateTrayIcon()
    {
        var info = new SKImageInfo(16, 16);
        using (var surface = SKSurface.Create(info))
        {
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(30, 30, 30));
            using (var paint = new SKPaint { Color = new SKColor(100, 180, 255), IsAntialias = true })
                canvas.DrawRect(2, 2, 12, 12, paint);
            using (var paint = new SKPaint { Color = new SKColor(50, 120, 50), IsAntialias = true })
            {
                using (var path = new SKPath())
                {
                    path.MoveTo(2, 14);
                    path.LineTo(8, 6);
                    path.LineTo(14, 14);
                    path.Close();
                    canvas.DrawPath(path, paint);
                }
            }
            using (var paint = new SKPaint { Color = new SKColor(255, 220, 60), IsAntialias = true })
                canvas.DrawOval(11, 5, 2, 2, paint);
            using (var image = surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                var stream = new MemoryStream(data.ToArray());
                return new WindowIcon(stream);
            }
        }
    }
}
