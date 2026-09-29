using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using ReelWalk.Services;

namespace ReelWalk.Views;
public partial class MainWindow
{
    // Shows the seek time again and restarts its hide timer.
    // Returns nothing.
    private void RestartToast()
    {
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // Length of the current video.
    // Returns null when it is unknown.
    private TimeSpan? VideoDuration()
    {
        if (_slideVideo != null && _slideVideo.HasLength)
            return _slideVideo.Length;
        return null;
    }

    // Updates the seek bar from the player.
    // Returns nothing.
    private void RefreshVideoProgress()
    {
        var playback = _viewModel.Playback;
        bool playing = playback != null && playback.IsVideoPlaying;
        var position = playing ? playback.VideoDisplayPosition : TimeSpan.Zero;
        _viewModel.UpdateVideoProgress(playing, position, VideoDuration());

        if (_videoProgressTimer == null)
            return;
        if (playing)
            _videoProgressTimer.Start();
        else
            _videoProgressTimer.Stop();
    }

    // Starts a video scrub at the pointer.
    // Returns nothing.
    private void VideoProgress_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (_viewModel.Playback == null || !_viewModel.Playback.IsVideoPlaying)
            return;
        if (!e.GetCurrentPoint(VideoProgressOverlay).Properties.IsLeftButtonPressed)
            return;

        _scrubbing = true;
        e.Pointer.Capture(VideoProgressOverlay);
        _viewModel.BeginScrub();
        SeekFromPoint(e.GetPosition(VideoProgressOverlay).X);
        e.Handled = true;
    }

    // Moves the scrub with the pointer.
    // Returns nothing.
    private void VideoProgress_PointerMoved(object sender, PointerEventArgs e)
    {
        if (!_scrubbing)
            return;
        SeekFromPoint(e.GetPosition(VideoProgressOverlay).X);
    }

    // Lands the scrub where the pointer was released.
    // Returns nothing.
    private void VideoProgress_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_scrubbing)
            return;
        _scrubbing = false;
        if (e.Pointer.Captured == VideoProgressOverlay)
            e.Pointer.Capture(null);
        SeekFromPoint(e.GetPosition(VideoProgressOverlay).X);
        _viewModel.EndScrub();
        e.Handled = true;
    }

    // Lands the scrub if the pointer capture is lost.
    // Returns nothing.
    private void VideoProgress_PointerCaptureLost(object sender, PointerCaptureLostEventArgs e)
    {
        if (!_scrubbing)
            return;
        _scrubbing = false;
        _viewModel.EndScrub();
    }

    // Maps a pointer x on the bar to a video fraction.
    // Returns nothing.
    private void SeekFromPoint(double x)
    {
        double width = VideoProgressOverlay.Bounds.Width;
        if (width <= 1)
            return;
        _viewModel.ScrubTo(x / width, VideoDuration());
    }

    // Opens ReelWalk.toml with the system editor.
    // Returns nothing.
    private static void OpenConfig()
    {
        var path = ConfigService.FilePath;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }

    // Deletes the current file.
    // confirm true asks first. Returns nothing.
    private async void DeleteImage(bool confirm)
    {
        var path = _viewModel.CurrentPath();
        if (path == null) return;

        if (confirm)
        {
            bool yes = await Notice.Confirm(this, "ReelWalk - Delete Image", "Delete this image?\n\n" + path);
            if (!yes)
                return;
        }

        _viewModel.DeleteCurrent();
    }

    // Saves settings before the window closes.
    // Returns nothing.
    private void MainWindow_Closing(object sender, WindowClosingEventArgs e)
    {
        _viewModel.Save();
        if (_slideVideo != null)
        {
            _slideVideo.Dispose();
            _slideVideo = null;
        }
    }

    // Stops timers and removes the tray icon.
    // Returns nothing.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_videoProgressTimer != null) _videoProgressTimer.Stop();
        if (_toastTimer != null) _toastTimer.Stop();
        if (_hintTimer != null) _hintTimer.Stop();
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
            TrayIcon.SetIcons(Avalonia.Application.Current, null);
        }
        if (_slideVideo != null)
        {
            _slideVideo.Dispose();
            _slideVideo = null;
        }
        _viewModel.DisposePlayback();
    }
}
