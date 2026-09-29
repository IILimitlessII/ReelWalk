using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReelWalk.Models;
using ReelWalk.Services;

namespace ReelWalk.ViewModels;
internal sealed partial class MainViewModel
{
    [ObservableProperty] private bool _explorerAtLibraryRoot;
    [ObservableProperty] private bool _explorerBrowsePathIgnored;
    [ObservableProperty] private bool _canManageBrowsePath;
    [ObservableProperty] private string _explorerBrowsePathLabel = "";
    [ObservableProperty] private ObservableCollection<LibraryPathItem> _libraryPathItems =
        new ObservableCollection<LibraryPathItem>();
    [ObservableProperty] private ObservableCollection<IgnorePathItem> _ignorePathItems =
        new ObservableCollection<IgnorePathItem>();
    [ObservableProperty] private bool _hasLibraryPathItems;
    [ObservableProperty] private bool _hasIgnorePathItems;

    internal Func<Task<string>> PickFolderAsync;

    // Opens a folder picker and adds the choice to library paths.
    // Returns nothing.
    [RelayCommand]
    private async Task AddLibraryPathFromPicker()
    {
        if (_playback == null || PickFolderAsync == null)
        {
            ShowToast("Folder picker unavailable", false);
            return;
        }

        string path;
        try
        {
            path = await PickFolderAsync();
        }
        catch
        {
            ShowToast("Could not open folder picker", false);
            return;
        }

        if (string.IsNullOrEmpty(path))
            return;
        if (_playback.AddLibraryPath(path))
        {
            ShowToast("Added to library", false);
            RefreshExplorerLibraryLists();
            RefreshExplorer();
            Save();
        }
        else
            ShowToast("Could not add that folder", false);
    }

    // Opens a folder picker and navigates the explorer there.
    // Returns nothing.
    [RelayCommand]
    private async Task BrowseToPickedFolder()
    {
        if (PickFolderAsync == null)
        {
            ShowToast("Folder picker unavailable", false);
            return;
        }

        string path;
        try
        {
            path = await PickFolderAsync();
        }
        catch
        {
            ShowToast("Could not open folder picker", false);
            return;
        }

        if (string.IsNullOrEmpty(path))
            return;
        _browsePath = Paths.Normalize(path) ?? path;
        _selectPath = null;
        RefreshExplorer();
    }

    // Adds the open browse folder to library paths.
    // Returns nothing when not inside a folder.
    [RelayCommand]
    private void AddBrowsePathToLibrary()
    {
        if (_playback == null || string.IsNullOrEmpty(_browsePath))
            return;
        if (_playback.AddLibraryPath(_browsePath))
        {
            ShowToast("Added to library", false);
            RefreshExplorerLibraryLists();
            RefreshExplorer();
            Save();
        }
        else
            ShowToast("Already in library or unavailable", false);
    }

    // Toggles ignore on the folder the explorer is showing.
    // Returns nothing when not inside a folder.
    [RelayCommand]
    private void ToggleBrowsePathIgnore()
    {
        if (_playback == null || string.IsNullOrEmpty(_browsePath))
            return;
        bool nowIgnored = _playback.ToggleIgnorePath(_browsePath);
        ShowToast(nowIgnored ? "Folder ignored" : "Folder no longer ignored", false);
        RefreshExplorerLibraryLists();
        RefreshExplorer();
        Save();
    }

    // Removes one library root.
    // parameter is a LibraryPathItem or path string.
    [RelayCommand]
    private void RemoveLibraryPath(object parameter)
    {
        if (_playback == null)
            return;
        string path = PathFromParameter(parameter);
        if (string.IsNullOrEmpty(path))
            return;
        if (_playback.RemoveLibraryPath(path))
        {
            ShowToast("Removed from library", false);
            RefreshExplorerLibraryLists();
            RefreshExplorer();
            Save();
        }
    }

    // Removes one ignore entry.
    // parameter is an IgnorePathItem or path string.
    [RelayCommand]
    private void RemoveIgnorePath(object parameter)
    {
        if (_playback == null)
            return;
        string path = PathFromParameter(parameter);
        if (string.IsNullOrEmpty(path))
            return;
        if (_playback.RemoveIgnorePath(path))
        {
            ShowToast("Removed ignore", false);
            RefreshExplorerLibraryLists();
            RefreshExplorer();
            Save();
        }
    }

    // Opens the browse folder in the system file manager.
    // Returns nothing when no folder is open.
    [RelayCommand]
    private void OpenBrowsePathInExplorer()
    {
        if (string.IsNullOrEmpty(_browsePath))
            return;
        DesktopService.OpenFolder(_browsePath);
    }

    // Path from a command parameter.
    // parameter may be LibraryPathItem, IgnorePathItem, or a string.
    private static string PathFromParameter(object parameter)
    {
        if (parameter is LibraryPathItem lib)
            return lib.FullPath;
        if (parameter is IgnorePathItem ign)
            return ign.FullPath;
        return parameter as string;
    }

    // Reloads library and ignore lists for the explorer panel.
    // Returns nothing.
    private void RefreshExplorerLibraryLists()
    {
        LibraryPathItems.Clear();
        IgnorePathItems.Clear();
        if (_playback == null)
            return;

        var cfg = _playback.Config;
        if (cfg.ImagePaths != null)
        {
            for (int i = 0; i < cfg.ImagePaths.Count; i++)
            {
                var p = cfg.ImagePaths[i];
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                LibraryPathItems.Add(new LibraryPathItem
                {
                    FullPath = p,
                    Name = Paths.Leaf(p)
                });
            }
        }

        if (cfg.IgnorePaths != null)
        {
            for (int i = 0; i < cfg.IgnorePaths.Count; i++)
            {
                var p = cfg.IgnorePaths[i];
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                IgnorePathItems.Add(new IgnorePathItem
                {
                    FullPath = p,
                    Name = Paths.Leaf(p)
                });
            }
        }

        HasLibraryPathItems = LibraryPathItems.Count > 0;
        HasIgnorePathItems = IgnorePathItems.Count > 0;
    }

    // Updates browse-path flags after RefreshExplorer.
    // Returns nothing.
    private void SyncExplorerBrowseFlags()
    {
        ExplorerAtLibraryRoot = string.IsNullOrEmpty(_browsePath);
        CanManageBrowsePath = !string.IsNullOrEmpty(_browsePath);
        ExplorerBrowsePathLabel = string.IsNullOrEmpty(_browsePath)
            ? ""
            : _browsePath;
        ExplorerBrowsePathIgnored = _playback != null &&
            !string.IsNullOrEmpty(_browsePath) &&
            _playback.IsPathIgnored(_browsePath);
    }
}
