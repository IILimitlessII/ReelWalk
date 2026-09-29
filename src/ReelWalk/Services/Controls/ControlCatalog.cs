using System;
using System.Collections.Generic;

namespace ReelWalk.Services.Controls;
internal static class ControlCatalog
{
    internal static readonly string[][] Defaults = new string[][]
    {
        new[] { "next", "Right" },
        new[] { "previous", "Left" },
        new[] { "pause", "Space" },
        new[] { "first", "Home" },
        new[] { "last", "End" },
        new[] { "skip_forward", "PageUp" },
        new[] { "skip_back", "PageDown" },
        new[] { "duration_up", "Up" },
        new[] { "duration_down", "Down" },
        new[] { "volume_up", "Ctrl+Up" },
        new[] { "volume_down", "Ctrl+Down" },
        new[] { "mute", "M" },
        new[] { "seek_forward", "Ctrl+Right" },
        new[] { "seek_back", "Ctrl+Left" },
        new[] { "seek_forward_fast", "Ctrl+Shift+Right" },
        new[] { "seek_back_fast", "Ctrl+Shift+Left" },
        new[] { "random", "R" },
        new[] { "newest", "N" },
        new[] { "sequential", "S" },
        new[] { "show", "V" },
        new[] { "folder", "F" },
        new[] { "ken_burns", "K" },
        new[] { "fit", "W" },
        new[] { "copy", "C" },
        new[] { "info", "I" },
        new[] { "help", "H" },
        new[] { "help_alt", "F1" },
        new[] { "settings", "P" },
        new[] { "open", "O" },
        new[] { "delete", "Delete" },
        new[] { "delete_now", "Ctrl+Delete" },
        new[] { "close", "Escape" },
        new[] { "explorer_up", "Up" },
        new[] { "explorer_down", "Down" },
        new[] { "explorer_home", "Home" },
        new[] { "explorer_end", "End" },
        new[] { "explorer_back", "Left" },
        new[] { "explorer_open", "Right" },
        new[] { "explorer_play", "Enter" },
        new[] { "explorer_backspace", "Back" }
    };

    // A fresh map of every action to its default key.
    // Returns the map.
    internal static Dictionary<string, string> Create()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Defaults.Length; i++)
            map[Defaults[i][0]] = Defaults[i][1];
        return map;
    }

    // Default key text for an action.
    // name is the toml key. Returns an empty string when name is unknown.
    internal static string Default(string name)
    {
        for (int i = 0; i < Defaults.Length; i++)
        {
            if (string.Equals(Defaults[i][0], name, StringComparison.OrdinalIgnoreCase))
                return Defaults[i][1];
        }
        return "";
    }
}
