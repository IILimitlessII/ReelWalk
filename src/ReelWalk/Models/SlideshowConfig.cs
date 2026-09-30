using System.Collections.Generic;
using ReelWalk.Services.Controls;

namespace ReelWalk.Models;
internal sealed class SlideshowConfig
{
    // Fills a new config with the built-in defaults.
    // Returns nothing.
    internal SlideshowConfig()
    {
        ImagePaths = [];
        IgnorePaths = [];
        DisplayDuration = 8.0;
        TransitionDurationPercent = 20.0;
        PlaybackMode = "Random";
        MediaShow = "Both";
        LastImageIndex = 0;
        LastImagePath = "";
        EnableKenBurns = true;
        KenBurnsDuration = 10.0;
        KenBurnsMaxZoom = 1.3;
        ImageFit = "Contain";
        Theme = "HarborBlue";
        VideoVolume = 1.0;
        FolderPlayMode = "Random";
        FolderIncludeSubfolders = true;
        BackHistory = 10;
        SkipCount = 10;
        SeekSeconds = 5;
        SeekFastSeconds = 30;
        Keys = ControlCatalog.Create();
        ActiveTransitions =
        [
            "Crossfade", "MorphZoom", "SoftWipe", "ParallaxReveal", "ScaleDissolve",
            "KenBurns"
        ];
    }

    internal List<string> ImagePaths { get; set; }
    internal List<string> IgnorePaths { get; set; }
    internal double DisplayDuration { get; set; }
    internal double TransitionDurationPercent { get; set; }
    internal string PlaybackMode { get; set; }
    internal string MediaShow { get; set; }
    internal int LastImageIndex { get; set; }
    internal string LastImagePath { get; set; }
    internal bool EnableKenBurns { get; set; }
    internal double KenBurnsDuration { get; set; }
    internal double KenBurnsMaxZoom { get; set; }
    internal string ImageFit { get; set; }
    internal string Theme { get; set; }
    internal double VideoVolume { get; set; }
    internal string FolderPlayMode { get; set; }
    internal bool FolderIncludeSubfolders { get; set; }
    internal int BackHistory { get; set; }
    internal int SkipCount { get; set; }
    internal double SeekSeconds { get; set; }
    internal double SeekFastSeconds { get; set; }
    internal Dictionary<string, string> Keys { get; set; }
    internal List<string> ActiveTransitions { get; set; }

    // Key text for an action, or the built-in default.
    // name is next, previous, folder, and the other control names. Returns the text.
    internal string Key(string name)
    {
        string value;
        if (Keys != null && Keys.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();
        var fallback = ControlCatalog.Default(name);
        return string.IsNullOrEmpty(fallback) ? name : fallback;
    }
}
