using System;
using ReelWalk.Services.Controls;

namespace ReelWalk.ViewModels;
internal sealed partial class MainViewModel
{
    // Keyboard help, using the keys from the toml.
    // Returns the overlay text.
    private string BuildHelpText()
    {
        int history = _controls == null ? 10 : _controls.BackHistory;
        int skip = _controls == null ? 10 : _controls.SkipCount;
        double seek = _controls == null ? 5 : _controls.SeekSeconds;
        double seekFast = _controls == null ? 30 : _controls.SeekFastSeconds;
        return
            KeyName("previous") + " / " + KeyName("next") + ", or the mouse wheel — previous or next file\n" +
            "Random remembers the last " + history + " files. Forward returns along them.\n" +
            "Ctrl+wheel — volume\n" +
            KeyName("seek_back") + " / " + KeyName("seek_forward") + " — jump " + seek.ToString("0") + " seconds in a video\n" +
            KeyName("seek_back_fast") + " / " + KeyName("seek_forward_fast") + " — jump " + seekFast.ToString("0") + " seconds\n" +
            "Quick jumps land once, on the time you stop at\n" +
            KeyName("first") + " / " + KeyName("last") + " — first or last file\n" +
            KeyName("skip_forward") + " / " + KeyName("skip_back") + " — skip " + skip + " files\n" +
            "\n" +
            "Drag the bar at the bottom to move through a video.\n" +
            "It plays from the spot where you release.\n" +
            KeyName("volume_up") + " / " + KeyName("volume_down") + " — volume     " + KeyName("mute") + " — mute\n" +
            "\n" +
            KeyName("pause") + ", or middle-click — pause or resume\n" +
            KeyName("duration_up") + " / " + KeyName("duration_down") + " — how long each photo stays\n" +
            KeyName("random") + "  " + KeyName("newest") + "  " + KeyName("sequential") + " — random, newest by date taken, or by name\n" +
            KeyName("show") + " — photos, videos, or both\n" +
            KeyName("folder") + " or right-click — folder explorer.\n" +
            "It opens on the folders around the current one.\n" +
            "Click a folder, or " + KeyName("explorer_play") + ", to play it. " + KeyName("explorer_open") + " opens it.\n" +
            "Subfolders includes folders inside it. This folder does not.\n" +
            "Smallest and largest order by file size. Shortest and longest order videos by length.\n" +
            "Type to jump to a name. " + KeyName("explorer_back") + " goes back.\n" +
            "A photo that will not open is skipped.\n" +
            "New files are picked up on their own.\n" +
            "\n" +
            KeyName("info") + " — file info     " + KeyName("help") + " — this help     " + KeyName("settings") + " — settings\n" +
            KeyName("ken_burns") + " — Ken Burns\n" +
            KeyName("fit") + " — whole picture or fill the screen\n" +
            KeyName("copy") + " — copy the file path     " + KeyName("open") + " — show it in Explorer\n" +
            KeyName("delete") + " — delete, asks first     " + KeyName("delete_now") + " — delete now\n" +
            KeyName("close") + " — quit";
    }

    // Key text from the toml, or the built-in default.
    // name is a control name. Returns the label.
    private string KeyName(string name)
    {
        if (_controls == null)
            return ControlCatalog.Default(name);
        return _controls.Key(name);
    }

    // Pauses the video and photo advances while help or the explorer is open.
    // Returns nothing.
    private void SyncUiHold()
    {
        if (_playback != null)
            _playback.SetUiHold(IsFolderMenuVisible || IsHelpVisible || IsSettingsVisible);
    }

    // Formats a video clock.
    // t is clamped at zero. Returns h:mm:ss or m:ss.
    private static string FormatVideoTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours >= 1)
            return string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        return string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
    }
}
