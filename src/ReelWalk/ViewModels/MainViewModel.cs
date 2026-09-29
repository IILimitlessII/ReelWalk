using System;
using System.Collections.Generic;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ReelWalk.Models;
using ReelWalk.Services;
using ReelWalk.Services.Controls;

namespace ReelWalk.ViewModels;
internal sealed partial class MainViewModel : ObservableObject
{
    private PlaybackService _playback;
    private SlideshowConfig _controls;
    private string _browsePath;
    private string _selectPath;
    private List<FolderChoice> _explorerAll;
    private string _query = "";
    private DateTime _queryAt;
    private readonly DispatcherTimer _toastHide;

    [ObservableProperty] private string _hintText = "H  controls     F or right-click  folders     wheel  next     Space  pause";
    [ObservableProperty] private string _loadingText = "";
    [ObservableProperty] private bool _isLoadingVisible;
    [ObservableProperty] private string _indexStatus = "";
    [ObservableProperty] private bool _isIndexVisible;
    [ObservableProperty] private string _infoText = "";
    [ObservableProperty] private bool _isInfoVisible;
    [ObservableProperty] private string _helpText = "";
    [ObservableProperty] private bool _isHelpVisible;
    [ObservableProperty] private bool _isFolderMenuVisible;
    [ObservableProperty] private IList<FolderChoice> _folderChoices;
    [ObservableProperty] private IList<FolderChoice> _explorerFolders;
    [ObservableProperty] private FolderChoice _selectedExplorer;
    [ObservableProperty] private string _explorerTitle = "Library";
    [ObservableProperty] private string _explorerDetail = "";
    [ObservableProperty] private string _playBrowseLabel = "Play this folder";
    [ObservableProperty] private string _explorerQueryLabel = "";
    [ObservableProperty] private bool _hasExplorerQuery;
    [ObservableProperty] private bool _hasExplorerFolders;
    [ObservableProperty] private bool _explorerIsEmpty;
    [ObservableProperty] private bool _canPlayBrowse;
    [ObservableProperty] private string _explorerEmptyText = "";
    [ObservableProperty] private bool _folderPlayIsRandom;
    [ObservableProperty] private bool _folderIncludesSubfolders = true;
    [ObservableProperty] private bool _isFolderPlaying;
    [ObservableProperty] private string _pathText = "";
    [ObservableProperty] private bool _isPathVisible;
    [ObservableProperty] private string _toastText = "";
    [ObservableProperty] private double _toastFontSize = 16;
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private bool _isPausedVisible;
    [ObservableProperty] private bool _isHintVisible;
    [ObservableProperty] private string _videoTimeText = "";
    [ObservableProperty] private double _videoProgress;
    [ObservableProperty] private bool _isVideoProgressVisible;

    // Wires the toast timer.
    // Returns nothing.
    internal MainViewModel()
    {
        _toastHide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        _toastHide.Tick += (s, e) =>
        {
            _toastHide.Stop();
            HideToast();
        };
    }

    internal PlaybackService Playback
    {
        get { return _playback; }
    }

    // Connects the window to the running slideshow.
    // playback may replace an older one. Returns nothing.
    internal void AttachPlayback(PlaybackService playback)
    {
        _playback = playback;
    }

    // Stores the keys shown in the hint and the help overlay.
    // config is the loaded toml. Returns nothing.
    internal void ApplyControls(SlideshowConfig config)
    {
        _controls = config;
        HintText = KeyName("help") + "  controls     " +
            KeyName("folder") + " or right-click  folders     wheel  next     " +
            KeyName("pause") + "  pause";
    }

    // Shows the startup message.
    // text is the message. Returns nothing.
    internal void ShowLoading(string text)
    {
        LoadingText = text;
        IsLoadingVisible = true;
    }

    // Hides the startup message.
    // Returns nothing.
    internal void HideLoading()
    {
        IsLoadingVisible = false;
    }

    // Shows the scan progress line.
    // text is the status. Returns nothing.
    internal void ShowIndex(string text)
    {
        IndexStatus = text;
        IsIndexVisible = true;
    }

    // Hides the scan progress line.
    // Returns nothing.
    internal void HideIndex()
    {
        IsIndexVisible = false;
    }

    // Shows the bottom control hint.
    // Returns nothing.
    internal void ShowHint()
    {
        IsHintVisible = true;
    }

    // Hides the bottom control hint.
    // Returns nothing.
    internal void HideHint()
    {
        IsHintVisible = false;
    }

    // Shows a short message.
    // large uses a bigger font. Returns nothing.
    internal void ShowToast(string text, bool large)
    {
        ToastFontSize = large ? 34 : 16;
        ToastText = text;
        IsToastVisible = true;
        _toastHide.Stop();
        _toastHide.Start();
    }

    // Hides the short message.
    // Returns nothing.
    internal void HideToast()
    {
        IsToastVisible = false;
    }

    // Closes the help overlay and lets the photo timer run.
    // Returns nothing.
    internal void HideHelp()
    {
        IsHelpVisible = false;
        SyncUiHold();
    }

    // Closes the folder explorer and lets the photo timer run.
    // Returns nothing.
    internal void HideFolderMenu()
    {
        IsFolderMenuVisible = false;
        SyncUiHold();
    }


}
