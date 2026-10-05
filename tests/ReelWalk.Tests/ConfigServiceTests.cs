using System;
using System.IO;
using ReelWalk.Models;
using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class ConfigServiceTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "reelwalk-missing-" + Path.GetRandomFileName() + ".toml");
        var config = ConfigService.LoadConfig(path);
        Assert.Empty(config.ImagePaths);
        Assert.Equal(8.0, config.DisplayDuration);
        Assert.Equal("Random", config.PlaybackMode);
        Assert.Equal("Both", config.MediaShow);
        Assert.Equal("HarborBlue", config.Theme);
        Assert.Equal("Right", config.Key("next"));
    }

    [Fact]
    public void SaveLoad_KeepsPathsThemeKeysTransitionsAndShow()
    {
        var path = TempToml();
        try
        {
            var config = new SlideshowConfig();
            config.ImagePaths = new System.Collections.Generic.List<string> { @"C:\Photos", @"D:\Trips" };
            config.IgnorePaths = new System.Collections.Generic.List<string> { @"C:\Photos\Skip" };
            config.DisplayDuration = 12.5;
            config.TransitionDurationPercent = 35;
            config.ImageFit = "Cover";
            config.Theme = "junglegreen";
            config.VideoVolume = 0.4;
            config.PlaybackMode = "NewestFirst";
            config.MediaShow = "Pictures";
            config.FolderPlayMode = "Ordered";
            config.FolderIncludeSubfolders = false;
            config.BackHistory = 25;
            config.LastImageIndex = 4;
            config.LastImagePath = @"C:\Photos\a.jpg";
            config.EnableKenBurns = false;
            config.KenBurnsDuration = 6;
            config.KenBurnsMaxZoom = 1.8;
            config.SkipCount = 15;
            config.SeekSeconds = 8;
            config.SeekFastSeconds = 45;
            config.ActiveTransitions = new System.Collections.Generic.List<string> { "Crossfade" };
            config.Keys["next"] = "Ctrl+N";

            ConfigService.SaveConfig(path, config);
            var loaded = ConfigService.LoadConfig(path);

            Assert.Equal(new[] { @"C:\Photos", @"D:\Trips" }, loaded.ImagePaths);
            Assert.Equal(new[] { @"C:\Photos\Skip" }, loaded.IgnorePaths);
            Assert.Equal(12.5, loaded.DisplayDuration);
            Assert.Equal(35, loaded.TransitionDurationPercent);
            Assert.Equal("Cover", loaded.ImageFit);
            Assert.Equal("JungleGreen", loaded.Theme);
            Assert.Equal(0.4, loaded.VideoVolume, 2);
            Assert.Equal("NewestFirst", loaded.PlaybackMode);
            Assert.Equal("Images", loaded.MediaShow);
            Assert.Equal("Sequential", loaded.FolderPlayMode);
            config.FolderPlayMode = "Largest";
            ConfigService.SaveConfig(path, config);
            Assert.Equal("SizeDesc", ConfigService.LoadConfig(path).FolderPlayMode);
            Assert.False(loaded.FolderIncludeSubfolders);
            Assert.Equal(25, loaded.BackHistory);
            Assert.Equal(4, loaded.LastImageIndex);
            Assert.Equal(@"C:\Photos\a.jpg", loaded.LastImagePath);
            Assert.False(loaded.EnableKenBurns);
            Assert.Equal(6, loaded.KenBurnsDuration);
            Assert.Equal(1.8, loaded.KenBurnsMaxZoom);
            Assert.Equal(15, loaded.SkipCount);
            Assert.Equal(8, loaded.SeekSeconds);
            Assert.Equal(45, loaded.SeekFastSeconds);
            Assert.Contains("Crossfade", loaded.ActiveTransitions);
            Assert.DoesNotContain(loaded.ActiveTransitions, name => name.Equals("MorphZoom", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Ctrl+N", loaded.Key("next"));
            Assert.Equal("Left", loaded.Key("previous"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Load_UnknownThemeAndBadNumbers_FallBack()
    {
        var path = TempToml();
        try
        {
            File.WriteAllText(path,
                "paths = []\n" +
                "ignore = []\n" +
                "[display]\n" +
                "theme = \"Nope\"\n" +
                "video_volume = 250\n" +
                "duration = nope\n" +
                "[playback]\n" +
                "history = -5\n" +
                "show = \"nope\"\n" +
                "[controls]\n" +
                "skip = 9000\n" +
                "seek_seconds = 0.1\n" +
                "seek_fast_seconds = 10000\n" +
                "next = \"  \"\n");

            var loaded = ConfigService.LoadConfig(path);
            Assert.Equal("HarborBlue", loaded.Theme);
            Assert.Equal(1, loaded.VideoVolume, 2);
            Assert.Equal(8.0, loaded.DisplayDuration);
            Assert.Equal(10, loaded.BackHistory);
            Assert.Equal("Both", loaded.MediaShow);
            Assert.Equal(500, loaded.SkipCount);
            Assert.Equal(5, loaded.SeekSeconds);
            Assert.Equal(30, loaded.SeekFastSeconds);
            Assert.Equal("Right", loaded.Key("next"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Load_HistoryAboveTheCap_ClampsTo500_AndVolumePercentScales()
    {
        var path = TempToml();
        try
        {
            File.WriteAllText(path,
                "[playback]\nhistory = 9000\n[display]\nvideo_volume = 40\n");
            var loaded = ConfigService.LoadConfig(path);
            Assert.Equal(500, loaded.BackHistory);
            Assert.Equal(0.4, loaded.VideoVolume, 2);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Prepare_WritesAFirstRunFile_ThenFindsIt()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "reelwalk-data-" + Path.GetRandomFileName())).FullName;
        var previous = Environment.GetEnvironmentVariable("REELWALK_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("REELWALK_DATA_DIR", dir);
            Assert.False(ConfigService.Prepare());
            Assert.True(ConfigService.NeedsSetup);
            Assert.True(File.Exists(Path.Combine(dir, "ReelWalk.toml")));

            Assert.True(ConfigService.Prepare());
            Assert.False(ConfigService.NeedsSetup);
        }
        finally
        {
            Environment.SetEnvironmentVariable("REELWALK_DATA_DIR", previous);
            Directory.Delete(dir, true);
        }
    }

    private static string TempToml()
    {
        return Path.Combine(Path.GetTempPath(), "reelwalk-config-" + Path.GetRandomFileName() + ".toml");
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        var tmp = path + ".tmp";
        if (File.Exists(tmp))
            File.Delete(tmp);
    }
}
