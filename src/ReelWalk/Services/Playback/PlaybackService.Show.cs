using System;
using System.IO;

namespace ReelWalk.Services;
internal sealed partial class PlaybackService
{
    // Shows the file after move, and restarts the photo timer.
    // move changes the playlist index. Returns nothing.
    private void ShowAfter(Action move)
    {
        move();
        ShowCurrentImage();
        ResetTimer();
    }

    // Shows the next file.
    // Returns nothing.
    internal void NextImage()
    {
        ShowAfter(() => imageManager.Next());
    }

    // Shows the previous file.
    // Returns nothing.
    internal void PreviousImage()
    {
        ShowAfter(() => imageManager.Previous());
    }

    // Shows the first file.
    // Returns nothing.
    internal void FirstImage()
    {
        ShowAfter(() => imageManager.JumpTo(0));
    }

    // Shows the last file.
    // Returns nothing.
    internal void LastImage()
    {
        ShowAfter(() => imageManager.JumpTo(imageManager.TotalImages - 1));
    }

    // Jumps forward count files and shows the result.
    // Returns nothing.
    internal void SkipForward(int count)
    {
        ShowAfter(() => imageManager.Skip(count));
    }

    // Jumps back count files and shows the result.
    // Returns nothing.
    internal void SkipBackward(int count)
    {
        ShowAfter(() => imageManager.Skip(-count));
    }

    // Deletes the file on screen and removes it from the playlist.
    // Returns the path, or null when nothing was deleted.
    internal string DeleteCurrentImage()
    {
        var path = imageManager.GetCurrentImagePath();
        if (path == null) return null;

        try
        {
            imageManager.RemoveCurrent();

            if (File.Exists(path))
                File.Delete(path);

            if (imageManager.TotalImages > 0)
            {
                firstImage = true;
                ShowCurrentImage();
                ResetTimer();
            }

            return path;
        }
        catch
        {
            return null;
        }
    }

    // Path of the file on screen.
    // Returns null when the library is empty.
    internal string GetCurrentImagePath()
    {
        return imageManager.GetCurrentImagePath();
    }

    // Pauses or resumes the photo timer and the video.
    // Returns nothing.
    internal void TogglePause()
    {
        isPaused = !isPaused;
        if (videoPlaying && video != null)
        {
            if (isPaused)
                video.Pause();
            else
                video.Resume();
        }
    }

    // Queues a jump of seconds on the current video.
    // Returns false when the current file is not a video.
    internal bool SeekVideo(double seconds)
    {
        if (!videoPlaying || videoSeek == null)
            return false;
        videoSeek.Nudge(seconds);
        return true;
    }

    // Holds the video position until the pointer is released.
    // Returns nothing.
    internal void BeginVideoScrub()
    {
        if (videoSeek != null)
            videoSeek.SetScrubHeld(true);
    }

    // Moves the pending video position.
    // fraction is 0 to 1. Returns false when this is not a video.
    internal bool ScrubVideoToFraction(double fraction)
    {
        if (!videoPlaying || videoSeek == null)
            return false;
        videoSeek.MoveToFraction(fraction);
        return true;
    }

    // Writes the position chosen by the pointer.
    // Returns nothing.
    internal void EndVideoScrub()
    {
        if (videoSeek != null)
            videoSeek.SetScrubHeld(false);
    }

    // Adds one second to how long each photo stays.
    // Returns nothing.
    internal void IncreaseDisplayDuration()
    {
        displayDuration = Math.Min(displayDuration + 1.0, 60.0);
        UpdateTimerInterval();
        config.DisplayDuration = displayDuration;
    }

    // Subtracts one second, not below one second.
    // Returns nothing.
    internal void DecreaseDisplayDuration()
    {
        displayDuration = Math.Max(displayDuration - 1.0, 1.0);
        UpdateTimerInterval();
        config.DisplayDuration = displayDuration;
    }

    // Changes the video volume by delta and stores it.
    // delta is a fraction. Returns nothing.
    internal void AdjustVideoVolume(double delta)
    {
        SetVideoVolume(config.VideoVolume + delta);
    }

    // Mutes, or restores the volume from before the mute.
    // Returns nothing.
    internal void ToggleMute()
    {
        if (config.VideoVolume > 0.001)
        {
            volumeBeforeMute = config.VideoVolume;
            SetVideoVolume(0);
        }
        else
        {
            double restore = volumeBeforeMute > 0.001 ? volumeBeforeMute : 1;
            SetVideoVolume(restore);
        }
    }

    // Switches Contain and Cover and stores the choice.
    // Returns the new fit name.
    internal string ToggleImageFit()
    {
        bool cover = !string.IsNullOrEmpty(config.ImageFit) &&
            (config.ImageFit.Equals("Cover", StringComparison.OrdinalIgnoreCase) ||
             config.ImageFit.Equals("Fill", StringComparison.OrdinalIgnoreCase));
        config.ImageFit = cover ? "Contain" : "Cover";
        return config.ImageFit;
    }

    // Applies v to the player and the config, clamped to 0..1.
    // Returns nothing.
    private void SetVideoVolume(double v)
    {
        if (v < 0) v = 0;
        if (v > 1) v = 1;
        config.VideoVolume = v;
        transitionEngine.VideoVolume = v;
        if (video != null)
            video.Volume = v;
    }

    // Sets the photo timer from the current duration.
    // Returns nothing.
    private void UpdateTimerInterval()
    {
        timer.Interval = TimeSpan.FromSeconds(displayDuration);
    }

    // Restarts the photo timer from a full interval.
    // Returns nothing.
    private void ResetTimer()
    {
        timer.Stop();
        if (!videoPlaying)
            timer.Start();
    }
}
