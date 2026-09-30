using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReelWalk.Models;
using ReelWalk.Services;
using ReelWalk.Services.Controls;

namespace ReelWalk.ViewModels;
internal sealed partial class MainViewModel
{
    private bool _settingsReady;
    private string _capturingAction;

    [ObservableProperty] private bool _isSettingsVisible;
    [ObservableProperty] private double _settingsDuration = 8;
    [ObservableProperty] private double _settingsTransitionPercent = 20;
    [ObservableProperty] private string _settingsFit = "Contain";
    [ObservableProperty] private string _settingsTheme = "HarborBlue";
    [ObservableProperty] private double _settingsVolume = 1;
    [ObservableProperty] private string _settingsPlaybackMode = "Random";
    [ObservableProperty] private string _settingsMediaShow = "Both";
    [ObservableProperty] private string _settingsFolderMode = "Random";
    [ObservableProperty] private bool _settingsFolderSubfolders = true;
    [ObservableProperty] private double _settingsHistory = 10;
    [ObservableProperty] private bool _settingsFxCrossfade = true;
    [ObservableProperty] private bool _settingsFxMorphZoom = true;
    [ObservableProperty] private bool _settingsFxSoftWipe = true;
    [ObservableProperty] private bool _settingsFxParallaxReveal = true;
    [ObservableProperty] private bool _settingsFxScaleDissolve = true;
    [ObservableProperty] private bool _settingsKenBurns = true;
    [ObservableProperty] private double _settingsKenBurnsDuration = 10;
    [ObservableProperty] private double _settingsKenBurnsMaxZoom = 1.3;
    [ObservableProperty] private double _settingsSkip = 10;
    [ObservableProperty] private double _settingsSeekSeconds = 5;
    [ObservableProperty] private double _settingsSeekFastSeconds = 30;
    [ObservableProperty] private ObservableCollection<KeybindItem> _keybinds =
        new ObservableCollection<KeybindItem>();
    [ObservableProperty] private bool _isCapturingKeybind;

    // True when Contain is the selected fit in Settings.
    internal bool SettingsFitIsContain
    {
        get { return string.Equals(SettingsFit, "Contain", StringComparison.OrdinalIgnoreCase); }
    }

    // True when Cover is the selected fit in Settings.
    internal bool SettingsFitIsCover
    {
        get { return string.Equals(SettingsFit, "Cover", StringComparison.OrdinalIgnoreCase); }
    }

    internal bool SettingsThemeIsHarbor
    {
        get { return ThemeIs(ThemeCatalog.HarborBlue); }
    }

    internal bool SettingsThemeIsDarkSlate
    {
        get { return ThemeIs(ThemeCatalog.DarkSlate); }
    }

    internal bool SettingsThemeIsJungle
    {
        get { return ThemeIs(ThemeCatalog.JungleGreen); }
    }

    internal bool SettingsThemeIsMidnight
    {
        get { return ThemeIs(ThemeCatalog.MidnightSky); }
    }

    internal bool SettingsThemeIsPastel
    {
        get { return ThemeIs(ThemeCatalog.PastelDreams); }
    }

    internal bool SettingsModeIsRandom
    {
        get { return string.Equals(SettingsPlaybackMode, "Random", StringComparison.OrdinalIgnoreCase); }
    }

    internal bool SettingsModeIsNewest
    {
        get { return string.Equals(SettingsPlaybackMode, "NewestFirst", StringComparison.OrdinalIgnoreCase); }
    }

    internal bool SettingsModeIsOldest
    {
        get { return string.Equals(SettingsPlaybackMode, "OldestFirst", StringComparison.OrdinalIgnoreCase); }
    }

    internal bool SettingsModeIsSequential
    {
        get { return string.Equals(SettingsPlaybackMode, "Sequential", StringComparison.OrdinalIgnoreCase); }
    }

    internal bool SettingsShowIsBoth
    {
        get { return string.Equals(SettingsMediaShow, "Both", StringComparison.OrdinalIgnoreCase); }
    }

    internal bool SettingsShowIsImages
    {
        get { return string.Equals(SettingsMediaShow, "Images", StringComparison.OrdinalIgnoreCase); }
    }

    internal bool SettingsShowIsVideos
    {
        get { return string.Equals(SettingsMediaShow, "Videos", StringComparison.OrdinalIgnoreCase); }
    }

    internal bool SettingsFolderIsRandom
    {
        get { return string.Equals(SettingsFolderMode, "Random", StringComparison.OrdinalIgnoreCase); }
    }

    internal bool SettingsFolderIsOrdered
    {
        get { return !SettingsFolderIsRandom; }
    }

    // Opens or closes the settings panel.
    // Returns nothing.
    [RelayCommand]
    internal void ToggleSettings()
    {
        if (IsSettingsVisible)
        {
            HideSettings();
            return;
        }

        HideHelp();
        HideFolderMenu();
        LoadSettingsFromConfig();
        IsSettingsVisible = true;
        IsHintVisible = false;
        SyncUiHold();
    }

    // Closes settings, cancels capture, and writes the toml.
    // Returns nothing.
    internal void HideSettings()
    {
        CancelKeyCapture();
        if (!IsSettingsVisible)
            return;
        IsSettingsVisible = false;
        SyncUiHold();
        if (_playback != null)
            _playback.SaveState();
        if (_controls != null)
            ApplyControls(_controls);
    }

    // Starts listening for a new chord for item.
    // item is a keybind row. Returns nothing.
    [RelayCommand]
    internal void BeginKeyCapture(KeybindItem item)
    {
        if (item == null)
            return;
        CancelKeyCapture();
        _capturingAction = item.Name;
        item.IsCapturing = true;
        IsCapturingKeybind = true;
    }

    // Cancels an in-progress key capture without changing the binding.
    // Returns nothing.
    internal void CancelKeyCapture()
    {
        if (!IsCapturingKeybind)
            return;
        for (int i = 0; i < Keybinds.Count; i++)
            Keybinds[i].IsCapturing = false;
        _capturingAction = null;
        IsCapturingKeybind = false;
    }

    // Applies a captured chord, or cancels when Escape is pressed alone.
    // Returns true when Settings consumed the key.
    internal bool TryCaptureKey(Key key, KeyModifiers mods)
    {
        if (!IsCapturingKeybind || string.IsNullOrEmpty(_capturingAction))
            return false;

        var mask = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt;
        var cleaned = mods & mask;

        if (key == Key.Escape && cleaned == KeyModifiers.None)
        {
            CancelKeyCapture();
            return true;
        }

        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LWin || key == Key.RWin ||
            key == Key.None)
            return true;

        var text = ControlChord.Format(key, cleaned);
        if (string.IsNullOrEmpty(text))
            return true;

        if (_playback != null)
            _playback.ApplyKey(_capturingAction, text);
        if (_controls != null)
        {
            if (_controls.Keys == null)
                _controls.Keys = ControlCatalog.Create();
            _controls.Keys[_capturingAction] = text;
        }

        for (int i = 0; i < Keybinds.Count; i++)
        {
            if (string.Equals(Keybinds[i].Name, _capturingAction, StringComparison.OrdinalIgnoreCase))
                Keybinds[i].Chord = text;
            Keybinds[i].IsCapturing = false;
        }

        _capturingAction = null;
        IsCapturingKeybind = false;
        ApplyControls(_controls);
        ControlsReloaded?.Invoke();
        return true;
    }

    // Raised when keybinds change so the window can rebuild its chord map.
    internal event Action ControlsReloaded;

    // Copies live config into the settings fields.
    // Returns nothing.
    private void LoadSettingsFromConfig()
    {
        _settingsReady = false;
        var cfg = _playback != null ? _playback.Config : _controls;
        if (cfg == null)
        {
            _settingsReady = true;
            return;
        }

        SettingsDuration = cfg.DisplayDuration;
        SettingsTransitionPercent = cfg.TransitionDurationPercent;
        SettingsFit = string.IsNullOrEmpty(cfg.ImageFit) ? "Contain" : cfg.ImageFit;
        SettingsTheme = ThemeCatalog.Normalize(cfg.Theme);
        SettingsVolume = cfg.VideoVolume;
        SettingsPlaybackMode = string.IsNullOrEmpty(cfg.PlaybackMode) ? "Random" : cfg.PlaybackMode;
        SettingsMediaShow = string.IsNullOrEmpty(cfg.MediaShow) ? "Both" : cfg.MediaShow;
        SettingsFolderMode = string.IsNullOrEmpty(cfg.FolderPlayMode) ? "Random" : cfg.FolderPlayMode;
        SettingsFolderSubfolders = cfg.FolderIncludeSubfolders;
        SettingsHistory = cfg.BackHistory;
        SettingsKenBurns = cfg.EnableKenBurns;
        SettingsKenBurnsDuration = cfg.KenBurnsDuration;
        SettingsKenBurnsMaxZoom = cfg.KenBurnsMaxZoom;
        SettingsSkip = cfg.SkipCount;
        SettingsSeekSeconds = cfg.SeekSeconds;
        SettingsSeekFastSeconds = cfg.SeekFastSeconds;

        var active = cfg.ActiveTransitions ?? new List<string>();
        SettingsFxCrossfade = HasFx(active, "Crossfade");
        SettingsFxMorphZoom = HasFx(active, "MorphZoom");
        SettingsFxSoftWipe = HasFx(active, "SoftWipe");
        SettingsFxParallaxReveal = HasFx(active, "ParallaxReveal");
        SettingsFxScaleDissolve = HasFx(active, "ScaleDissolve");

        Keybinds.Clear();
        for (int i = 0; i < ControlCatalog.Defaults.Length; i++)
        {
            var name = ControlCatalog.Defaults[i][0];
            Keybinds.Add(new KeybindItem(name, TitleForAction(name), cfg.Key(name)));
        }

        NotifySettingsModeFlags();
        _settingsReady = true;
    }

    partial void OnSettingsDurationChanged(double value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.ApplyDisplayDuration(value);
    }

    partial void OnSettingsTransitionPercentChanged(double value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.ApplyTransitionPercent(value);
    }

    partial void OnSettingsThemeChanged(string value)
    {
        var id = ThemeCatalog.Normalize(value);
        ThemeCatalog.Apply(id);
        NotifySettingsModeFlags();
        if (!_settingsReady || _playback == null)
            return;
        _playback.Config.Theme = id;
    }

    partial void OnSettingsFitChanged(string value)
    {
        if (!_settingsReady || _playback == null) return;
        var fit = _playback.ApplyImageFit(value);
        SettingsFitApplied?.Invoke(fit);
        NotifySettingsModeFlags();
    }

    partial void OnSettingsVolumeChanged(double value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.ApplyVideoVolume(value);
    }

    partial void OnSettingsPlaybackModeChanged(string value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.SetPlaybackMode(value);
        NotifySettingsModeFlags();
    }

    partial void OnSettingsMediaShowChanged(string value)
    {
        if (!_settingsReady || _playback == null) return;
        var label = _playback.ApplyMediaShow(value);
        if (label == null)
        {
            ShowToast("No files for that choice", false);
            _settingsReady = false;
            SettingsMediaShow = _playback.Config.MediaShow;
            _settingsReady = true;
        }
        NotifySettingsModeFlags();
    }

    partial void OnSettingsFolderModeChanged(string value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.SetFolderPlayMode(value);
        FolderPlayIsRandom = string.Equals(value, "Random", StringComparison.OrdinalIgnoreCase);
        NotifySettingsModeFlags();
    }

    partial void OnSettingsFolderSubfoldersChanged(bool value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.SetFolderIncludeSubfolders(value);
        FolderIncludesSubfolders = value;
    }

    partial void OnSettingsHistoryChanged(double value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.ApplyBackHistory((int)Math.Round(value));
    }

    partial void OnSettingsFxCrossfadeChanged(bool value) { ApplyFxPool(); }
    partial void OnSettingsFxMorphZoomChanged(bool value) { ApplyFxPool(); }
    partial void OnSettingsFxSoftWipeChanged(bool value) { ApplyFxPool(); }
    partial void OnSettingsFxParallaxRevealChanged(bool value) { ApplyFxPool(); }
    partial void OnSettingsFxScaleDissolveChanged(bool value) { ApplyFxPool(); }

    partial void OnSettingsKenBurnsChanged(bool value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.ApplyKenBurns(value);
        ApplyFxPool();
    }

    partial void OnSettingsKenBurnsDurationChanged(double value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.ApplyKenBurnsDuration(value);
    }

    partial void OnSettingsKenBurnsMaxZoomChanged(double value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.ApplyKenBurnsMaxZoom(value);
    }

    partial void OnSettingsSkipChanged(double value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.ApplySkipCount((int)Math.Round(value));
        SkipSeekChanged?.Invoke();
    }

    partial void OnSettingsSeekSecondsChanged(double value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.ApplySeekSeconds(value);
        SkipSeekChanged?.Invoke();
    }

    partial void OnSettingsSeekFastSecondsChanged(double value)
    {
        if (!_settingsReady || _playback == null) return;
        _playback.ApplySeekFastSeconds(value);
        SkipSeekChanged?.Invoke();
    }

    // Raised when fit changes from Settings so the window can restretch images.
    internal event Action<string> SettingsFitApplied;

    // Raised when skip/seek values change so the window copies them.
    internal event Action SkipSeekChanged;

    [RelayCommand]
    private void SetSettingsFit(string fit)
    {
        SettingsFit = fit;
    }

    [RelayCommand]
    private void SetSettingsTheme(string theme)
    {
        SettingsTheme = ThemeCatalog.Normalize(theme);
    }

    [RelayCommand]
    private void SetSettingsPlaybackMode(string mode)
    {
        SettingsPlaybackMode = mode;
    }

    [RelayCommand]
    private void SetSettingsMediaShow(string show)
    {
        SettingsMediaShow = show;
    }

    [RelayCommand]
    private void SetSettingsFolderMode(string mode)
    {
        SettingsFolderMode = mode;
    }

    [RelayCommand]
    private void ToggleSettingsFolderSubfolders()
    {
        SettingsFolderSubfolders = !SettingsFolderSubfolders;
    }

    [RelayCommand]
    private void ToggleSettingsFxCrossfade()
    {
        SettingsFxCrossfade = !SettingsFxCrossfade;
    }

    [RelayCommand]
    private void ToggleSettingsFxMorphZoom()
    {
        SettingsFxMorphZoom = !SettingsFxMorphZoom;
    }

    [RelayCommand]
    private void ToggleSettingsFxSoftWipe()
    {
        SettingsFxSoftWipe = !SettingsFxSoftWipe;
    }

    [RelayCommand]
    private void ToggleSettingsFxParallaxReveal()
    {
        SettingsFxParallaxReveal = !SettingsFxParallaxReveal;
    }

    [RelayCommand]
    private void ToggleSettingsFxScaleDissolve()
    {
        SettingsFxScaleDissolve = !SettingsFxScaleDissolve;
    }

    [RelayCommand]
    private void ToggleSettingsKenBurns()
    {
        SettingsKenBurns = !SettingsKenBurns;
    }

    // Rebuilds the transition pool from the Settings checkboxes.
    // Returns nothing.
    private void ApplyFxPool()
    {
        if (!_settingsReady || _playback == null)
            return;
        var names = new List<string>();
        if (SettingsFxCrossfade) names.Add("Crossfade");
        if (SettingsFxMorphZoom) names.Add("MorphZoom");
        if (SettingsFxSoftWipe) names.Add("SoftWipe");
        if (SettingsFxParallaxReveal) names.Add("ParallaxReveal");
        if (SettingsFxScaleDissolve) names.Add("ScaleDissolve");
        if (SettingsKenBurns) names.Add("KenBurns");
        _playback.ApplyTransitionPool(names);
    }

    // True when names lists effect.
    // names is the active transition list.
    private static bool HasFx(List<string> names, string effect)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i].Equals(effect, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Human label for a control catalog action.
    // name is the toml key. Returns a short title.
    private static string TitleForAction(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";
        var parts = name.Split('_');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                continue;
            parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
        }
        return string.Join(" ", parts);
    }

    // True when SettingsTheme is id.
    // id is a ThemeCatalog name.
    private bool ThemeIs(string id)
    {
        return string.Equals(SettingsTheme, id, StringComparison.OrdinalIgnoreCase);
    }

    // Refreshes boolean flags used by mode buttons.
    // Returns nothing.
    private void NotifySettingsModeFlags()
    {
        OnPropertyChanged(nameof(SettingsFitIsContain));
        OnPropertyChanged(nameof(SettingsFitIsCover));
        OnPropertyChanged(nameof(SettingsThemeIsHarbor));
        OnPropertyChanged(nameof(SettingsThemeIsDarkSlate));
        OnPropertyChanged(nameof(SettingsThemeIsJungle));
        OnPropertyChanged(nameof(SettingsThemeIsMidnight));
        OnPropertyChanged(nameof(SettingsThemeIsPastel));
        OnPropertyChanged(nameof(SettingsModeIsRandom));
        OnPropertyChanged(nameof(SettingsModeIsNewest));
        OnPropertyChanged(nameof(SettingsModeIsOldest));
        OnPropertyChanged(nameof(SettingsModeIsSequential));
        OnPropertyChanged(nameof(SettingsShowIsBoth));
        OnPropertyChanged(nameof(SettingsShowIsImages));
        OnPropertyChanged(nameof(SettingsShowIsVideos));
        OnPropertyChanged(nameof(SettingsFolderIsRandom));
        OnPropertyChanged(nameof(SettingsFolderIsOrdered));
    }
}
