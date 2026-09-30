using System.IO;
using Avalonia.Headless.XUnit;
using ReelWalk.Models;
using ReelWalk.Services;
using ReelWalk.ViewModels;
using Xunit;

namespace ReelWalk.Tests;
public class SettingsViewModelTests
{
    [AvaloniaFact]
    public void EachTheme_SetsOnlyItsOwnFlag()
    {
        var vm = new MainViewModel();
        vm.SettingsTheme = ThemeCatalog.HarborBlue;
        AssertOnly(vm, harbor: true);

        vm.SettingsTheme = ThemeCatalog.DarkSlate;
        AssertOnly(vm, slate: true);

        vm.SettingsTheme = ThemeCatalog.JungleGreen;
        AssertOnly(vm, jungle: true);

        vm.SettingsTheme = ThemeCatalog.MidnightSky;
        AssertOnly(vm, midnight: true);

        vm.SettingsTheme = ThemeCatalog.PastelDreams;
        AssertOnly(vm, pastel: true);
    }

    [AvaloniaFact]
    public void HelpText_IncludesTheConfiguredKeys()
    {
        var vm = new MainViewModel();
        var config = new SlideshowConfig();
        config.Keys["next"] = "Ctrl+N";
        config.Keys["previous"] = "Ctrl+B";
        config.BackHistory = 7;
        config.SeekSeconds = 9;
        config.SeekFastSeconds = 40;
        vm.ApplyControls(config);
        vm.ToggleHelp();

        Assert.Contains("Ctrl+N", vm.HelpText);
        Assert.Contains("Ctrl+B", vm.HelpText);
        Assert.Contains("last 7 files", vm.HelpText);
        Assert.Contains("jump 9 seconds", vm.HelpText);
        Assert.Contains("jump 40 seconds", vm.HelpText);
        Assert.True(vm.IsHelpVisible);
    }

    [AvaloniaFact]
    public void LoadedSettings_KeepHistoryAndSeekInsideTheirRanges()
    {
        var path = Path.Combine(Path.GetTempPath(), "reelwalk-settings-" + Path.GetRandomFileName() + ".toml");
        try
        {
            File.WriteAllText(path,
                "[display]\n" +
                "duration = 999\n" +
                "transition_percent = -5\n" +
                "video_volume = -4\n" +
                "[playback]\n" +
                "history = 0\n" +
                "[controls]\n" +
                "seek_seconds = 0.2\n" +
                "seek_fast_seconds = 900\n" +
                "skip = 0\n");

            var vm = new MainViewModel();
            vm.ApplyControls(ConfigService.LoadConfig(path));
            vm.ToggleSettings();

            Assert.Equal(60, vm.SettingsDuration);
            Assert.Equal(0, vm.SettingsTransitionPercent);
            Assert.Equal(0, vm.SettingsVolume);
            Assert.Equal(10, vm.SettingsHistory);
            Assert.Equal(5, vm.SettingsSeekSeconds);
            Assert.Equal(30, vm.SettingsSeekFastSeconds);
            Assert.Equal(10, vm.SettingsSkip);
            Assert.True(vm.IsSettingsVisible);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void AssertOnly(
        MainViewModel vm,
        bool harbor = false,
        bool slate = false,
        bool jungle = false,
        bool midnight = false,
        bool pastel = false)
    {
        Assert.Equal(harbor, vm.SettingsThemeIsHarbor);
        Assert.Equal(slate, vm.SettingsThemeIsDarkSlate);
        Assert.Equal(jungle, vm.SettingsThemeIsJungle);
        Assert.Equal(midnight, vm.SettingsThemeIsMidnight);
        Assert.Equal(pastel, vm.SettingsThemeIsPastel);
    }
}
