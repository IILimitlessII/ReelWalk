using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ReelWalk.Models;
using ReelWalk.Services.Controls;

namespace ReelWalk.Services;
internal static partial class ConfigService
{
    internal const string FileName = "ReelWalk.toml";

    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    private static readonly string[] AllTransitions = {
        "Crossfade", "MorphZoom", "SoftWipe", "ParallaxReveal", "ScaleDissolve"
    };

    internal static bool NeedsSetup { get; private set; }

    internal static string FilePath
    {
        get
        {
            return Path.Combine(DataDirectory(), FileName);
        }
    }

    // Folder for ReelWalk.toml and ReelWalk.library.
    // REELWALK_DATA_DIR overrides this (used by the Debug launch configs).
    // Otherwise the folder beside the program, or BaseDirectory when hosted by `dotnet`.
    internal static string DataDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("REELWALK_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            try
            {
                Directory.CreateDirectory(overrideDir);
            }
            catch { }
            return Path.GetFullPath(overrideDir.Trim());
        }

        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    return dir;
            }
        }
        return AppContext.BaseDirectory;
    }

    // Creates ReelWalk.toml beside the exe when it is missing.
    // Returns false when a new empty config was just written.
    internal static bool Prepare()
    {
        NeedsSetup = false;
        var toml = FilePath;
        if (File.Exists(toml))
            return true;

        CreateDefaultConfig(toml);
        NeedsSetup = true;
        return false;
    }

    // Writes a toml with no folders.
    // path is the destination. Returns nothing.
    private static void CreateDefaultConfig(string path)
    {
        File.WriteAllText(path, BuildToml(new SlideshowConfig(), true), new UTF8Encoding(false));
    }

    // Reads toml.
    // path is the config file. Returns the settings.
    internal static SlideshowConfig LoadConfig(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new SlideshowConfig();

        return LoadToml(path);
    }

    // Rewrites the toml by writing a temp file and replacing the original.
    // path is ReelWalk.toml. Returns nothing.
    internal static void SaveConfig(string path, SlideshowConfig config)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, BuildToml(config, false), new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(tmp, path);
    }

    // Renders the config as toml text, including the comments.
    // isDefault adds the first-run note. Returns the file text.
    private static string BuildToml(SlideshowConfig config, bool isDefault)
    {
        var lines = new List<string>(80);
        lines.Add("# ReelWalk");
        lines.Add("# Folders are scanned recursively. Local paths and UNC paths both work.");
        lines.Add("# ReelWalk rewrites this file when it exits and keeps the settings below.");
        lines.Add("");

        lines.Add("# One folder per line.");
        if (isDefault || config.ImagePaths == null || config.ImagePaths.Count == 0)
        {
            lines.Add("# paths = [");
            lines.Add("#     \"C:\\\\Photos\",");
            lines.Add("#     \"D:\\\\Pictures\",");
            lines.Add("# ]");
            lines.Add("paths = []");
        }
        else
        {
            lines.Add("paths = [");
            foreach (var p in config.ImagePaths)
                lines.Add("    " + Quote(p) + ",");
            lines.Add("]");
        }
        lines.Add("");

        lines.Add("# Skip this folder and everything inside it.");
        if (isDefault || config.IgnorePaths == null || config.IgnorePaths.Count == 0)
        {
            lines.Add("# ignore = [");
            lines.Add("#     \"C:\\\\Photos\\\\Skip\",");
            lines.Add("# ]");
            lines.Add("ignore = []");
        }
        else
        {
            lines.Add("ignore = [");
            foreach (var p in config.IgnorePaths)
                lines.Add("    " + Quote(p) + ",");
            lines.Add("]");
        }
        lines.Add("");

        lines.Add("[display]");
        lines.Add("# Seconds each photo stays on screen. Videos play through.");
        lines.Add(string.Format(CI, "duration = {0:F1}", ClampDuration(config.DisplayDuration)));
        lines.Add("# Transition length as a percent of duration.");
        lines.Add(string.Format(CI, "transition_percent = {0:F1}", ClampPercent(config.TransitionDurationPercent)));
        lines.Add("# Contain = whole picture visible. Cover = fill the screen.");
        lines.Add("fit = " + Quote(string.IsNullOrEmpty(config.ImageFit) ? "Contain" : config.ImageFit));
        lines.Add("# HarborBlue | DarkSlate | JungleGreen | MidnightSky | PastelDreams");
        lines.Add("theme = " + Quote(ThemeCatalog.Normalize(config.Theme)));
        lines.Add("# 0.0 mute, 1.0 full.");
        lines.Add(string.Format(CI, "video_volume = {0:F2}", ClampVolume(config.VideoVolume)));
        lines.Add("");

        lines.Add("[playback]");
        lines.Add("# Random | NewestFirst | OldestFirst | Sequential");
        lines.Add("mode = " + Quote(string.IsNullOrEmpty(config.PlaybackMode) ? "Random" : config.PlaybackMode));
        lines.Add("# Both | Images | Videos");
        lines.Add("show = " + Quote(MediaTypes.NormalizeShow(config.MediaShow)));
        lines.Add("# Random | Sequential — starting choice in the folder menu");
        lines.Add("folder_mode = " + Quote(NormalizeFolderPlayMode(
            string.IsNullOrEmpty(config.FolderPlayMode) ? "Random" : config.FolderPlayMode)));
        lines.Add("# true plays folders inside the chosen folder as well.");
        lines.Add("folder_subfolders = " + (config.FolderIncludeSubfolders ? "true" : "false"));
        lines.Add("# How many files Back remembers in Random. Forward then returns along them.");
        lines.Add("history = " + ClampInt(config.BackHistory, 10, 1, 500));
        lines.Add("# Written on exit. Used to reopen the last file.");
        lines.Add(string.Format(CI, "last_index = {0}", config.LastImageIndex));
        lines.Add("last_path = " + Quote(config.LastImagePath ?? ""));
        lines.Add("");

        lines.Add("[transitions]");
        lines.Add("# One of these is chosen at random. Remove a name to turn it off.");
        lines.Add("# Comment out every name for hard cuts.");
        var active = new HashSet<string>(
            config.ActiveTransitions ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        lines.Add("enabled = [");
        foreach (var t in AllTransitions)
        {
            if (active.Contains(t))
                lines.Add("    " + Quote(t) + ",");
        }
        lines.Add("]");

        bool ken = config.EnableKenBurns || active.Contains("KenBurns");
        lines.Add("# Slow pan and zoom on still images.");
        lines.Add("ken_burns = " + (ken ? "true" : "false"));
        bool durationDefault = Math.Abs(config.KenBurnsDuration - 10.0) < 0.01;
        bool zoomDefault = Math.Abs(config.KenBurnsMaxZoom - 1.3) < 0.01;
        if (!durationDefault)
            lines.Add(string.Format(CI, "ken_burns_duration = {0:F1}", config.KenBurnsDuration));
        else
            lines.Add("# ken_burns_duration = 10.0");
        if (!zoomDefault)
            lines.Add(string.Format(CI, "ken_burns_max_zoom = {0:F1}", config.KenBurnsMaxZoom));
        else
            lines.Add("# ken_burns_max_zoom = 1.3");
        lines.Add("");

        lines.Add("[controls]");
        lines.Add("# Key names match WPF: Left, Right, PageUp, Space, Delete, F1.");
        lines.Add("# Prefix with Ctrl+, Shift+, or Alt+. Mouse wheel, right-click, and middle-click stay as they are.");
        lines.Add("# How many files Page Up and Page Down jump. In Random, Page Down walks the back trail.");
        lines.Add("skip = " + ClampInt(config.SkipCount, 10, 1, 500));
        lines.Add("# Seconds for a short video jump, and for a long one.");
        lines.Add(string.Format(CI, "seek_seconds = {0:0}", ClampSeconds(config.SeekSeconds, 5)));
        lines.Add(string.Format(CI, "seek_fast_seconds = {0:0}", ClampSeconds(config.SeekFastSeconds, 30)));
        for (int i = 0; i < ControlCatalog.Defaults.Length; i++)
        {
            var name = ControlCatalog.Defaults[i][0];
            lines.Add(name + " = " + Quote(config.Key(name)));
        }
        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    // Parses a toml file into settings.
    // path is ReelWalk.toml. Returns the settings.
    private static SlideshowConfig LoadToml(string path)
    {
        var config = new SlideshowConfig();
        var doc = ParseToml(File.ReadAllLines(path));

        List<string> paths;
        if (doc.Arrays.TryGetValue("paths", out paths))
            config.ImagePaths = CleanPaths(paths);

        List<string> ignore;
        if (doc.Arrays.TryGetValue("ignore", out ignore))
            config.IgnorePaths = CleanPaths(ignore);

        config.DisplayDuration = ClampDuration(doc.Number("display.duration", config.DisplayDuration));
        config.TransitionDurationPercent = ClampPercent(doc.Number("display.transition_percent", config.TransitionDurationPercent));
        config.ImageFit = doc.Text("display.fit", config.ImageFit);
        config.Theme = ThemeCatalog.Normalize(doc.Text("display.theme", config.Theme));
        config.VideoVolume = ClampVolume(doc.Number("display.video_volume", config.VideoVolume));

        config.PlaybackMode = doc.Text("playback.mode", config.PlaybackMode);
        config.MediaShow = MediaTypes.NormalizeShow(doc.Text("playback.show", config.MediaShow));
        config.FolderPlayMode = NormalizeFolderPlayMode(doc.Text("playback.folder_mode", "Random"));
        config.FolderIncludeSubfolders = doc.Bool("playback.folder_subfolders", true);
        config.BackHistory = ClampInt(doc.Number("playback.history", 10), 10, 1, 500);
        config.LastImageIndex = (int)doc.Number("playback.last_index", config.LastImageIndex);
        config.LastImagePath = doc.Text("playback.last_path", config.LastImagePath ?? "");

        List<string> enabled;
        if (doc.Arrays.TryGetValue("transitions.enabled", out enabled))
        {
            config.ActiveTransitions = new List<string>();
            config.EnableKenBurns = false;
            if (enabled != null)
            {
                foreach (var name in enabled)
                {
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    if (name.Equals("KenBurns", StringComparison.OrdinalIgnoreCase))
                    {
                        config.EnableKenBurns = true;
                        config.ActiveTransitions.Add("KenBurns");
                    }
                    else if (Array.Exists(AllTransitions,
                        x => x.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        config.ActiveTransitions.Add(name.Trim());
                    }
                }
            }
        }

        if (doc.Has("transitions.ken_burns"))
        {
            config.EnableKenBurns = doc.Bool("transitions.ken_burns", config.EnableKenBurns);
            if (config.EnableKenBurns)
            {
                if (!config.ActiveTransitions.Exists(x => x.Equals("KenBurns", StringComparison.OrdinalIgnoreCase)))
                    config.ActiveTransitions.Add("KenBurns");
            }
            else
            {
                config.ActiveTransitions.RemoveAll(x => x.Equals("KenBurns", StringComparison.OrdinalIgnoreCase));
            }
        }

        config.KenBurnsDuration = doc.Number("transitions.ken_burns_duration", config.KenBurnsDuration);
        config.KenBurnsMaxZoom = doc.Number("transitions.ken_burns_max_zoom", config.KenBurnsMaxZoom);

        config.SkipCount = ClampInt(doc.Number("controls.skip", 10), 10, 1, 500);
        config.SeekSeconds = ClampSeconds(doc.Number("controls.seek_seconds", 5), 5);
        config.SeekFastSeconds = ClampSeconds(doc.Number("controls.seek_fast_seconds", 30), 30);
        if (config.Keys == null)
            config.Keys = ControlCatalog.Create();
        for (int i = 0; i < ControlCatalog.Defaults.Length; i++)
        {
            var name = ControlCatalog.Defaults[i][0];
            if (!doc.Has("controls." + name))
                continue;
            var text = doc.Text("controls." + name, "");
            if (!string.IsNullOrWhiteSpace(text))
                config.Keys[name] = text.Trim();
        }

        return config;
    }

    // Trims quotes and blank entries from a path list.
    // raw may be null. Returns the kept paths.
    private static List<string> CleanPaths(List<string> raw)
    {
        var list = new List<string>();
        if (raw == null)
            return list;
        foreach (var item in raw)
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;
            list.Add(item.Trim().Trim('"'));
        }
        return list;
    }

    // Toml double-quoted string.
    // value is escaped. Returns the quoted text.
    private static string Quote(string value)
    {
        if (value == null)
            value = "";
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\') sb.Append("\\\\");
            else if (c == '"') sb.Append("\\\"");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') continue;
            else if (c == '\t') sb.Append("\\t");
            else sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }

    // Maps the folder order to Random or Sequential.
    // val is the raw setting. Returns Random for anything that is not sequential.
    private static string NormalizeFolderPlayMode(string val)
    {
        if (!string.IsNullOrEmpty(val) &&
            (val.Equals("Sequential", StringComparison.OrdinalIgnoreCase) ||
             val.Equals("Ordered", StringComparison.OrdinalIgnoreCase)))
            return "Sequential";
        return "Random";
    }

    // Clamps a volume into 0 to 1.
    // v above 1 is treated as a percent. Returns the fraction.
    private static double ClampVolume(double v)
    {
        if (v > 1.0) v = v / 100.0;
        if (v < 0) v = 0;
        if (v > 1) v = 1;
        return v;
    }

    // Whole number inside min and max.
    // A blank or out-of-range value returns fallback.
    private static int ClampInt(double value, int fallback, int min, int max)
    {
        if (double.IsNaN(value) || value < min)
            return fallback;
        if (value > max)
            return max;
        return (int)value;
    }

    // Photo time on screen, kept between 1 and 60 seconds.
    // A missing or invalid number returns 8. Returns the seconds.
    private static double ClampDuration(double value)
    {
        if (double.IsNaN(value))
            return 8;
        if (value < 1)
            return 1;
        if (value > 60)
            return 60;
        return value;
    }

    // Transition length as a percent of the photo time, kept between 0 and 100.
    // A missing or invalid number returns 20. Returns the percent.
    private static double ClampPercent(double value)
    {
        if (double.IsNaN(value))
            return 20;
        if (value < 0)
            return 0;
        if (value > 100)
            return 100;
        return value;
    }

    // A positive number of seconds, or fallback when it is missing or absurd.
    // value is the toml number. Returns the seconds.
    private static double ClampSeconds(double value, double fallback)
    {
        if (double.IsNaN(value) || value < 0.5 || value > 600)
            return fallback;
        return value;
    }


}
