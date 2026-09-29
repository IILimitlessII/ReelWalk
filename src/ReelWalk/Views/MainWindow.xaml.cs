using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReelWalk.Services;
using ReelWalk.Services.Controls;
using ReelWalk.ViewModels;

namespace ReelWalk.Views;
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private DispatcherTimer _videoProgressTimer;
    private DispatcherTimer _toastTimer;
    private DispatcherTimer _hintTimer;
    private TrayIcon _trayIcon;
    private SlideVideo _slideVideo;
    private bool _cover;
    private bool _started;
    private bool _scrubbing;
    private int _skipCount = 10;
    private double _seekSeconds = 5;
    private double _seekFastSeconds = 30;
    private KeyModifiers _mods;
    private readonly Dictionary<string, ControlChord> _keys =
        new Dictionary<string, ControlChord>(StringComparer.OrdinalIgnoreCase);

    // Creates the window state, timers, and the view model.
    // Returns nothing.
    public MainWindow()
    {
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        PointerPressed += MainWindow_PointerPressed;
        Opened += (s, e) => ClaimKeyboardFocus();
        Activated += (s, e) => ClaimKeyboardFocus();
        Deactivated += (s, e) => _viewModel.EndScrub();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.ControlsReloaded += ReloadControlsFromConfig;
        _viewModel.SettingsFitApplied += ApplyImageFit;
        _viewModel.SkipSeekChanged += ReloadSkipSeek;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            _viewModel.HideToast();
        };

        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _hintTimer.Tick += (s, e) =>
        {
            _hintTimer.Stop();
            _viewModel.HideHint();
        };
    }

    // Scrolls the highlighted folder into view after the list changes.
    // e names the property. Returns nothing.
    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "SelectedExplorer" || e.PropertyName == "IsFolderMenuVisible")
            Dispatcher.UIThread.Post(ScrollExplorerIntoView, DispatcherPriority.Loaded);
        if (e.PropertyName == "IsHelpVisible" || e.PropertyName == "IsInfoVisible" ||
            e.PropertyName == "IsFolderMenuVisible" || e.PropertyName == "IsSettingsVisible" ||
            e.PropertyName == "IsToastVisible" ||
            e.PropertyName == "IsPausedVisible" || e.PropertyName == "IsLoadingVisible" ||
            e.PropertyName == "IsHintVisible")
            SyncVideoChrome();
    }

    // Rebuilds key chords from the live config after a remap.
    // Returns nothing.
    private void ReloadControlsFromConfig()
    {
        if (_viewModel.Playback == null || _viewModel.Playback.Config == null)
            return;
        LoadControls(_viewModel.Playback.Config);
    }

    // Copies skip and seek lengths from the live config.
    // Returns nothing.
    private void ReloadSkipSeek()
    {
        if (_viewModel.Playback == null || _viewModel.Playback.Config == null)
            return;
        var config = _viewModel.Playback.Config;
        _skipCount = config.SkipCount;
        _seekSeconds = config.SeekSeconds;
        _seekFastSeconds = config.SeekFastSeconds;
    }

    // Hides the video only while a large menu covers it.
    // Info stays in its own window so the picture remains. Returns nothing.
    private void SyncVideoChrome()
    {
        if (_slideVideo == null)
            return;
        bool obscured = _viewModel.IsHelpVisible ||
            _viewModel.IsFolderMenuVisible ||
            _viewModel.IsSettingsVisible;
        _slideVideo.SetObscured(obscured);
    }

    // Scrolls the highlighted explorer row into view.
    // Returns nothing when the list is hidden.
    private void ScrollExplorerIntoView()
    {
        FolderExplorer.ScrollSelectionIntoView();
    }

    // Sets stills and video to letterbox or fill.
    // fit Cover or Fill fills the screen. Returns nothing.
    private void ApplyImageFit(string fit)
    {
        _cover = !string.IsNullOrEmpty(fit) &&
            (fit.Equals("Cover", StringComparison.OrdinalIgnoreCase) ||
             fit.Equals("Fill", StringComparison.OrdinalIgnoreCase));
        var stretch = _cover ? Stretch.UniformToFill : Stretch.Uniform;
        Image1.Stretch = stretch;
        Image2.Stretch = stretch;
        if (_slideVideo != null)
            _slideVideo.SetCover(_cover);
    }

    // Handles pause, the folder explorer, and a click on the picture.
    // Returns nothing.
    private void MainWindow_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        ClaimKeyboardFocus();

        if (IsDescendantOf(e.Source, VideoProgressOverlay) ||
            IsDescendantOf(e.Source, FolderExplorer) ||
            IsDescendantOf(e.Source, SettingsPanel) ||
            (PathButton != null && IsDescendantOf(e.Source, PathButton)))
        {
            e.Handled = true;
            return;
        }

        if (_viewModel.IsSettingsVisible)
        {
            _viewModel.HideSettings();
            e.Handled = true;
            return;
        }

        if (_viewModel.IsHelpVisible)
        {
            _viewModel.HideHelp();
            e.Handled = true;
            return;
        }

        if (_viewModel.IsFolderMenuVisible)
        {
            _viewModel.HideFolderMenu();
            e.Handled = true;
            return;
        }

        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.RightButtonPressed)
        {
            _viewModel.ToggleFolderMenu();
            e.Handled = true;
            return;
        }

        if (kind == PointerUpdateKind.MiddleButtonPressed)
        {
            _viewModel.TogglePause();
            e.Handled = true;
            return;
        }

        e.Handled = true;
    }

    // Puts keyboard focus on this window so Linux (and video) keep receiving keys.
    // Returns nothing.
    private void ClaimKeyboardFocus()
    {
        if (!IsVisible)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible)
                return;
            Activate();
            Focus();
        }, DispatcherPriority.Input);
    }

    // Next or previous file, or volume when Ctrl is held.
    // Ignored while a menu is open. Returns nothing.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_viewModel.Playback == null || _viewModel.IsFolderMenuVisible ||
            _viewModel.IsHelpVisible || _viewModel.IsSettingsVisible)
            return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl)
        {
            _viewModel.ChangeVolume(e.Delta.Y > 0 ? 0.05 : -0.05);
            RestartToast();
        }
        else if (e.Delta.Y > 0)
            _viewModel.Previous();
        else if (e.Delta.Y < 0)
            _viewModel.Next();
        e.Handled = true;
    }

    // True when child is parent or inside it.
    // Returns false when either is null.
    private static bool IsDescendantOf(object child, Visual parent)
    {
        var visual = child as Visual;
        while (visual != null)
        {
            if (visual == parent)
                return true;
            visual = visual.GetVisualParent();
        }
        return false;
    }

    // Scale transform on image.
    // image is one of the two still layers. Returns its scale.
    private static ScaleTransform ScaleOf(Image image)
    {
        return ((TransformGroup)image.RenderTransform).Children[0] as ScaleTransform;
    }

    // Translate transform on image.
    // image is one of the two still layers. Returns its offset.
    private static TranslateTransform TranslateOf(Image image)
    {
        return ((TransformGroup)image.RenderTransform).Children[1] as TranslateTransform;
    }
}
