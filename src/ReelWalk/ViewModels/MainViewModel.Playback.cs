using System;
using CommunityToolkit.Mvvm.Input;
using ReelWalk.Services;

namespace ReelWalk.ViewModels;
internal sealed partial class MainViewModel
{
    // Opens or closes the keyboard help.
    // Returns nothing.
    [RelayCommand]
    internal void ToggleHelp()
    {
        if (IsHelpVisible)
        {
            HideHelp();
            return;
        }

        HideFolderMenu();
        HideSettings();
        HelpText = BuildHelpText();
        IsHelpVisible = true;
        IsHintVisible = false;
        SyncUiHold();
    }

    // Opens the explorer on the folders around the current file, or closes it.
    // A folder of only files opens on its parent. Returns nothing.
    [RelayCommand]
    internal void ToggleFolderMenu()
    {
        if (IsFolderMenuVisible)
        {
            IsFolderMenuVisible = false;
            SyncUiHold();
            return;
        }

        if (_playback == null)
            return;

        HideHelp();
        HideSettings();
        var here = _playback.CurrentDirectory();
        int ignoredTotal;
        int ignoredDirect;
        var inside = string.IsNullOrEmpty(here)
            ? null
            : _playback.ListExplorer(here, out ignoredTotal, out ignoredDirect);
        string parent = Paths.Parent(here);
        if ((inside == null || inside.Count == 0) &&
            !string.IsNullOrEmpty(parent) &&
            !_playback.IsLibraryRoot(here))
        {
            _browsePath = parent;
            _selectPath = here;
        }
        else
        {
            _browsePath = here;
            _selectPath = null;
        }
        _query = "";
        _queryAt = DateTime.MinValue;
        RefreshExplorer();
        SyncFolderOrderFlags();
        FolderIncludesSubfolders = _playback.FolderIncludeSubfolders;
        IsFolderPlaying = _playback.IsFolderPlay;
        RefreshExplorerLibraryLists();
        IsFolderMenuVisible = true;
        IsHintVisible = false;
        SyncUiHold();
    }

    // Opens or closes the file info overlay.
    // Returns nothing.
    internal void ToggleInfo()
    {
        IsInfoVisible = !IsInfoVisible;
        if (IsInfoVisible)
            RefreshInfo();
    }

    // Rewrites the file info from the current file.
    // Returns nothing when the overlay is closed.
    internal void RefreshInfo()
    {
        if (!IsInfoVisible || _playback == null)
            return;
        InfoText = _playback.GetInfoText();
    }

    // Updates the path button and whether a folder is playing.
    // Returns nothing.
    internal void RefreshPath()
    {
        var path = CurrentPath();
        PathText = path ?? "";
        IsPathVisible = !string.IsNullOrEmpty(PathText);
        if (_playback != null)
            IsFolderPlaying = _playback.IsFolderPlay;
    }

    // Shows or hides the paused label.
    // Returns nothing.
    private void RefreshPaused()
    {
        IsPausedVisible = _playback != null && _playback.IsPaused;
    }

    // Updates the seek bar while a video plays.
    // duration is null when the length is unknown. Returns nothing.
    internal void UpdateVideoProgress(bool playing, TimeSpan position, TimeSpan? duration)
    {
        IsVideoProgressVisible = playing;
        if (!playing)
        {
            VideoProgress = 0;
            VideoTimeText = "";
            return;
        }

        if (!duration.HasValue)
        {
            VideoProgress = 0;
            VideoTimeText = FormatVideoTime(position) + " / --:--";
            return;
        }

        double total = duration.Value.TotalSeconds;
        double frac = total > 0 ? position.TotalSeconds / total : 0;
        if (frac < 0) frac = 0;
        if (frac > 1) frac = 1;
        VideoProgress = frac;
        VideoTimeText = FormatVideoTime(position) + " / " + FormatVideoTime(duration.Value);
    }

    // Shows the time the video will jump to.
    // duration is the video length, or null. Returns nothing.
    internal void ShowSeekPreview(TimeSpan position, TimeSpan? duration)
    {
        string text = duration.HasValue
            ? FormatVideoTime(position) + "   /   " + FormatVideoTime(duration.Value)
            : FormatVideoTime(position);
        ShowToast(text, true);
    }

    // Pauses or resumes.
    // Returns nothing.
    [RelayCommand]
    internal void TogglePause()
    {
        if (_playback == null) return;
        _playback.TogglePause();
        RefreshPaused();
    }

    // Shows the next file.
    // Returns nothing.
    [RelayCommand]
    internal void Next()
    {
        if (_playback == null) return;
        _playback.NextImage();
    }

    // Shows the previous file.
    // Returns nothing.
    [RelayCommand]
    internal void Previous()
    {
        if (_playback == null) return;
        _playback.PreviousImage();
    }

    // Shows the first file in the current order.
    // Returns nothing.
    internal void First()
    {
        if (_playback == null) return;
        _playback.FirstImage();
    }

    // Shows the last file in the current order.
    // Returns nothing.
    internal void Last()
    {
        if (_playback == null) return;
        _playback.LastImage();
    }

    // Jumps count files.
    // count may be negative. Returns nothing.
    internal void Skip(int count)
    {
        if (_playback == null) return;
        if (count >= 0)
            _playback.SkipForward(count);
        else
            _playback.SkipBackward(-count);
    }

    // Queues a video jump of seconds.
    // seconds may be negative. Returns nothing.
    internal void Seek(double seconds)
    {
        if (_playback == null) return;
        if (!_playback.SeekVideo(seconds))
            ShowToast("This file is a photo", false);
    }

    // Changes the video volume by delta.
    // delta is a fraction of full volume. Returns nothing.
    internal void ChangeVolume(double delta)
    {
        if (_playback == null) return;
        _playback.AdjustVideoVolume(delta);
        ShowToast(string.Format("Volume {0:0}%", _playback.CurrentVideoVolume * 100), false);
        RefreshInfo();
    }

    // Changes how long each photo stays, by deltaSeconds.
    // Returns nothing.
    internal void ChangeDuration(double deltaSeconds)
    {
        if (_playback == null) return;
        if (deltaSeconds >= 0)
            _playback.IncreaseDisplayDuration();
        else
            _playback.DecreaseDisplayDuration();
        ShowToast(string.Format("{0:0} seconds per photo", _playback.DisplayDurationSeconds), false);
        RefreshInfo();
    }

    // Sets the library order.
    // mode is Random, NewestFirst, OldestFirst, or Sequential. Returns nothing.
    internal void SetMode(string mode)
    {
        if (_playback == null) return;
        _playback.SetPlaybackMode(mode);
        RefreshInfo();
    }

    // Switches photos and videos, photos only, then videos only.
    // Stays put when the next choice has no files. Returns nothing.
    internal void CycleMedia()
    {
        if (_playback == null)
            return;
        var label = _playback.CycleMediaShow();
        if (string.IsNullOrEmpty(label))
            ShowToast("No files for that choice", false);
        else
            ShowToast(label, false);
        RefreshInfo();
    }

    // Turns the slow pan and zoom on or off.
    // Returns nothing.
    internal void ToggleKenBurns()
    {
        if (_playback == null) return;
        _playback.ToggleKenBurns();
        RefreshInfo();
    }

    // Opens or closes the folder explorer.
    // Returns nothing.
    internal void PlayFolder()
    {
        ToggleFolderMenu();
    }
}
