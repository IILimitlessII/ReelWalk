using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using ReelWalk.Models;
using ReelWalk.Services;
using ReelWalk.Services.Controls;

namespace ReelWalk.ViewModels;
internal sealed partial class MainViewModel
{
    // Moves the highlight in the folder list.
    // delta is the row step. Returns nothing at either end.
    internal void MoveExplorerSelection(int delta)
    {
        if (ExplorerFolders == null || ExplorerFolders.Count == 0 || delta == 0)
            return;

        int current = 0;
        for (int i = 0; i < ExplorerFolders.Count; i++)
        {
            if (ReferenceEquals(ExplorerFolders[i], SelectedExplorer) || ExplorerFolders[i].IsSelected)
            {
                current = i;
                break;
            }
        }

        int next = current + delta;
        if (next < 0) next = 0;
        if (next >= ExplorerFolders.Count) next = ExplorerFolders.Count - 1;
        SetExplorerSelection(ExplorerFolders[next]);
    }

    // Adds text to the folder find, starting over after a short pause.
    // text is the typed characters. Returns nothing.
    internal void TypeExplorer(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var now = DateTime.UtcNow;
        if ((now - _queryAt).TotalMilliseconds > 900)
            _query = "";
        _queryAt = now;
        _query += text;
        ApplyQuery(null);
    }

    // Deletes the last typed find character.
    // Returns false when the find box is already empty.
    internal bool BackspaceExplorer()
    {
        if (string.IsNullOrEmpty(_query))
            return false;
        _query = _query.Substring(0, _query.Length - 1);
        _queryAt = DateTime.UtcNow;
        ApplyQuery(null);
        return true;
    }

    // Opens the highlighted folder, or plays it when it has no folders inside.
    // Returns nothing when nothing is highlighted.
    internal void OpenSelectedExplorer()
    {
        if (SelectedExplorer == null || string.IsNullOrEmpty(SelectedExplorer.FullPath))
            return;
        if (!SelectedExplorer.HasChildren)
        {
            PlayFolderChoice(SelectedExplorer);
            return;
        }
        _browsePath = SelectedExplorer.FullPath;
        _selectPath = null;
        RefreshExplorer();
    }

    // Plays the highlighted folder, or the folder currently open when none is highlighted.
    // Returns nothing.
    internal void PlaySelectedExplorer()
    {
        if (SelectedExplorer != null && !string.IsNullOrEmpty(SelectedExplorer.FullPath))
        {
            PlayFolderChoice(SelectedExplorer);
            return;
        }
        PlayBrowseFolder();
    }

    // Moves the explorer to the parent folder.
    // From a library root it returns to the library list. Returns nothing.
    internal void GoExplorerUp()
    {
        if (_playback == null || string.IsNullOrEmpty(_browsePath))
            return;

        string leaving = _browsePath;
        if (_playback.IsLibraryRoot(_browsePath))
        {
            _browsePath = null;
        }
        else
            _browsePath = Paths.Parent(_browsePath);

        _selectPath = leaving;
        RefreshExplorer();
    }

    internal Func<string, Task> CopyText;

    // Copies the current file path through CopyText.
    // Returns nothing.
    internal void CopyCurrentPath()
    {
        var path = CurrentPath();
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            if (CopyText == null)
            {
                ShowToast("Could not copy the path", false);
                return;
            }
            CopyText(path).GetAwaiter().GetResult();
            ShowToast("Path copied", false);
        }
        catch
        {
            ShowToast("Could not copy the path", false);
        }
    }

    // Switches between the whole picture and filling the screen.
    // Returns the new fit name, or null when playback is missing.
    internal string ToggleFit()
    {
        if (_playback == null)
            return null;
        var fit = _playback.ToggleImageFit();
        ShowToast(string.Equals(fit, "Cover", StringComparison.OrdinalIgnoreCase)
            ? "Fill the screen"
            : "Show the whole picture", false);
        return fit;
    }

    // Mutes or restores the video volume.
    // Returns nothing.
    internal void ToggleMute()
    {
        if (_playback == null)
            return;
        _playback.ToggleMute();
        ShowToast(_playback.CurrentVideoVolume <= 0.001
            ? "Muted"
            : string.Format("Volume {0:0}%", _playback.CurrentVideoVolume * 100), false);
    }

    // Shows the folder that was clicked in the path or the Open button.
    // parameter is a FolderChoice. An empty path returns to the library list.
    [RelayCommand]
    private void OpenExplorerFolder(object parameter)
    {
        var choice = parameter as FolderChoice;
        if (choice == null)
            return;
        if (string.IsNullOrEmpty(choice.FullPath))
            _browsePath = null;
        else
            _browsePath = choice.FullPath;
        _selectPath = null;
        RefreshExplorer();
    }

    // Plays the folder the explorer is showing.
    // Returns nothing at the library list.
    [RelayCommand]
    private void PlayBrowseFolder()
    {
        if (string.IsNullOrEmpty(_browsePath))
            return;
        PlayFolderChoice(new FolderChoice { Name = Paths.Leaf(_browsePath), FullPath = _browsePath });
    }

    // Reloads the path, the folder list, and the title for the open location.
    // Returns nothing.
    private void RefreshExplorer()
    {
        if (_playback == null)
            return;

        int totalFiles;
        int directFiles;
        var folders = _playback.ListExplorer(_browsePath, out totalFiles, out directFiles)
            ?? new List<FolderChoice>();
        _explorerAll = folders;
        _query = "";

        FolderChoice pick = null;
        if (!string.IsNullOrEmpty(_selectPath))
        {
            for (int i = 0; i < folders.Count; i++)
            {
                if (Paths.Same((folders[i].FullPath ?? "").TrimEnd('\\', '/'), _selectPath.TrimEnd('\\', '/')))
                {
                    pick = folders[i];
                    break;
                }
            }
        }
        if (pick == null)
        {
            for (int i = 0; i < folders.Count; i++)
            {
                if (folders[i].IsHere || folders[i].IsSelected)
                {
                    pick = folders[i];
                    break;
                }
            }
        }
        if (pick == null && folders.Count > 0)
            pick = folders[0];

        string title = string.IsNullOrEmpty(_browsePath) ? "Library" : Paths.Leaf(_browsePath);
        ExplorerTitle = title;
        if (string.IsNullOrEmpty(_browsePath))
            ExplorerDetail = folders.Count == 1 ? "1 folder" : folders.Count.ToString("N0") + " folders";
        else if (folders.Count == 0)
            ExplorerDetail = FilePhrase(totalFiles);
        else
        {
            string folderPhrase = folders.Count == 1 ? "1 folder" : folders.Count.ToString("N0") + " folders";
            string files = FilePhrase(totalFiles);
            if (directFiles > 0)
                files += directFiles == 1 ? ", 1 in this folder" : ", " + directFiles.ToString("N0") + " in this folder";
            ExplorerDetail = folderPhrase + "  ·  " + files;
        }
        PlayBrowseLabel = title.Length > 42 ? "Play this folder" : "Play " + title;
        CanPlayBrowse = !string.IsNullOrEmpty(_browsePath);

        var crumbs = new List<FolderChoice>();
        crumbs.Add(new FolderChoice
        {
            Name = "Library",
            FullPath = "",
            ShowSeparator = false,
            IsSelected = string.IsNullOrEmpty(_browsePath)
        });
        var chain = _playback.BrowseCrumbs(_browsePath);
        if (chain != null)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                chain[i].ShowSeparator = true;
                chain[i].IsSelected = !string.IsNullOrEmpty(_browsePath) && i == chain.Count - 1;
                crumbs.Add(chain[i]);
            }
        }
        FolderChoices = crumbs;
        ApplyQuery(pick);
    }

    // Filters the folder list by the typed find and highlights a row.
    // prefer is kept when the find is empty. Returns nothing.
    private void ApplyQuery(FolderChoice prefer)
    {
        ExplorerQueryLabel = string.IsNullOrEmpty(_query) ? "" : "Find  " + _query;
        HasExplorerQuery = !string.IsNullOrEmpty(_query);

        var source = _explorerAll ?? new List<FolderChoice>();
        List<FolderChoice> shown;
        if (string.IsNullOrEmpty(_query))
        {
            shown = source;
        }
        else
        {
            var prefix = new List<FolderChoice>();
            var contains = new List<FolderChoice>();
            for (int i = 0; i < source.Count; i++)
            {
                var name = source[i].Name ?? "";
                if (name.StartsWith(_query, StringComparison.OrdinalIgnoreCase))
                    prefix.Add(source[i]);
                else if (name.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)
                    contains.Add(source[i]);
            }
            shown = prefix.Count > 0 ? prefix : contains;
        }

        FolderChoice pick = null;
        if (shown.Count > 0)
        {
            if (!string.IsNullOrEmpty(_query))
                pick = shown[0];
            else if (prefer != null)
            {
                for (int i = 0; i < shown.Count; i++)
                {
                    if (ReferenceEquals(shown[i], prefer))
                    {
                        pick = shown[i];
                        break;
                    }
                }
            }
            else
            {
                for (int i = 0; i < shown.Count; i++)
                {
                    if (ReferenceEquals(shown[i], SelectedExplorer))
                    {
                        pick = shown[i];
                        break;
                    }
                }
            }
            if (pick == null)
                pick = shown[0];
        }

        ExplorerFolders = shown;
        SetExplorerSelection(pick);
        HasExplorerFolders = shown.Count > 0;
        ExplorerIsEmpty = shown.Count == 0;
        if (!string.IsNullOrEmpty(_query) && shown.Count == 0)
            ExplorerEmptyText = "No folder matches \"" + _query + "\".";
        else if (string.IsNullOrEmpty(_browsePath))
            ExplorerEmptyText = "No library folders to open.";
        else
            ExplorerEmptyText = "No folders inside. Play " + ExplorerTitle + " plays the files here.";
    }

    // Count as words.
    // count is a file total. Returns the phrase.
    private static string FilePhrase(int count)
    {
        if (count == 1)
            return "1 file";
        return count.ToString("N0") + " files";
    }

    // Highlights choice and clears the highlight on the other rows.
    // choice may be null. Returns nothing.
    private void SetExplorerSelection(FolderChoice choice)
    {
        if (ExplorerFolders != null)
        {
            for (int i = 0; i < ExplorerFolders.Count; i++)
                ExplorerFolders[i].IsSelected = ReferenceEquals(ExplorerFolders[i], choice);
        }
        SelectedExplorer = choice;
    }

    // Plays the chosen folder, then closes the explorer.
    // parameter is a FolderChoice. Returns nothing when it has no path.
    [RelayCommand]
    private void PlayFolderChoice(object parameter)
    {
        var choice = parameter as FolderChoice;
        if (choice == null || string.IsNullOrEmpty(choice.FullPath))
            return;

        IsFolderMenuVisible = false;
        SyncUiHold();
        if (_playback == null)
            return;

        bool ok = _playback.PlayFolder(choice.FullPath);
        string how = FolderPlayIsRandom ? "random" : "ordered";
        if (ok)
            ShowToast(string.Format("Playing {0}   ·   {1:N0}   ·   {2}",
                choice.Name, _playback.ImageCount, how), false);
        else
            ShowToast("No other files in " + choice.Name, false);

        IsFolderPlaying = _playback.IsFolderPlay;
        RefreshInfo();
        RefreshPath();
    }

    // Plays the open folder in name order.
    // Returns nothing.
    [RelayCommand]
    private void SetFolderPlayOrdered()
    {
        SetFolderPlayMode(false);
    }

    // Plays the open folder in random order.
    // Returns nothing.
    [RelayCommand]
    private void SetFolderPlayRandom()
    {
        SetFolderPlayMode(true);
    }

    // Limits folder play to the selected folder.
    // Returns nothing.
    [RelayCommand]
    private void SetFolderThisOnly()
    {
        SetFolderDepth(false);
    }

    // Includes folders inside the selected folder.
    // Returns nothing.
    [RelayCommand]
    private void SetFolderSubfolders()
    {
        SetFolderDepth(true);
    }

    // Sets random or ordered folder play.
    // random false is path order. Returns nothing.
    private void SetFolderPlayMode(bool random)
    {
        if (_playback == null)
            return;
        _playback.SetFolderPlayMode(random ? "Random" : "Sequential");
        FolderPlayIsRandom = random;
        if (_playback.IsFolderPlay)
            ShowToast((random ? "Random" : "Ordered") + " folder play", false);
        RefreshInfo();
    }

    // Plays nested folders or only the chosen folder.
    // includeSubfolders false keeps direct files. Returns nothing.
    private void SetFolderDepth(bool includeSubfolders)
    {
        if (_playback == null)
            return;
        _playback.SetFolderIncludeSubfolders(includeSubfolders);
        FolderIncludesSubfolders = includeSubfolders;
        IsFolderPlaying = _playback.IsFolderPlay;
        if (_playback.IsFolderPlay)
            ShowToast(includeSubfolders ? "Including subfolders" : "This folder only", false);
        RefreshInfo();
        RefreshPath();
    }

    // Returns to the library playlist and closes the explorer.
    // Returns nothing.
    [RelayCommand]
    private void StopFolderPlay()
    {
        if (_playback == null)
            return;
        _playback.StopFolderPlay();
        IsFolderMenuVisible = false;
        SyncUiHold();
        IsFolderPlaying = false;
        ShowToast("Resumed library", false);
        RefreshInfo();
        RefreshPath();
    }

    // Path of the file on screen.
    // Returns null when nothing is playing.
    internal string CurrentPath()
    {
        return _playback == null ? null : _playback.GetCurrentImagePath();
    }

    // Deletes the file on screen after the window has already confirmed.
    // Returns nothing.
    internal void DeleteCurrent()
    {
        if (_playback == null) return;
        if (_playback.DeleteCurrentImage() != null)
            RefreshInfo();
    }

    // Holds the video seek while the pointer is down.
    // Returns nothing.
    internal void BeginScrub()
    {
        if (_playback != null)
            _playback.BeginVideoScrub();
    }

    // Moves the pending seek to fraction of the video.
    // duration is unused by the player and kept for the label. Returns nothing.
    internal void ScrubTo(double fraction, TimeSpan? duration)
    {
        if (_playback == null) return;
        _playback.ScrubVideoToFraction(fraction);
        if (_playback.IsVideoPlaying)
            UpdateVideoProgress(true, _playback.VideoDisplayPosition, duration);
    }

    // Lands the video seek where the pointer was released.
    // Returns nothing.
    internal void EndScrub()
    {
        if (_playback != null)
            _playback.EndVideoScrub();
    }

    // Writes config and the current index.
    // Returns nothing.
    internal void Save()
    {
        if (_playback != null)
            _playback.SaveState();
    }

    // Stops timers, watchers, and the video.
    // Returns nothing.
    internal void DisposePlayback()
    {
        if (_playback != null)
            _playback.Dispose();
    }

}
