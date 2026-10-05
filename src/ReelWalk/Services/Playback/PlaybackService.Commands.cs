using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ReelWalk.Models;

namespace ReelWalk.Services;
internal sealed partial class PlaybackService
{
    // Plays folderPath, then returns to the previous playlist when it finishes.
    // Returns false when fewer than two files can play.
    internal bool PlayFolder(string folderPath, Action<bool> ready)
    {
        if (imageManager.EnterFolderPlay(folderPath, config.FolderPlayMode, config.IgnorePaths, ok =>
        {
            if (ok)
            {
                firstImage = true;
                ShowCurrentImage();
                var handler = ImageChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
            if (ready != null)
                ready(ok);
        }))
            return true;

        if (ready != null)
            ready(false);
        return false;
    }

    // Restores the playlist from before folder play.
    // Returns nothing when a folder is not playing.
    internal void StopFolderPlay()
    {
        if (!imageManager.IsFolderPlay)
            return;
        imageManager.ExitFolderPlay();
        var handler = ImageChanged;
        if (handler != null)
            handler(this, EventArgs.Empty);
    }

    // Sets how a folder is ordered.
    // mode is Random, Sequential, SizeAsc, SizeDesc, LengthAsc, or LengthDesc. Returns nothing.
    internal void SetFolderPlayMode(string mode)
    {
        config.FolderPlayMode = FolderOrder.Normalize(mode);
        imageManager.ApplyFolderPlayMode(config.FolderPlayMode);
    }

    // Plays nested folders, or only the chosen folder.
    // include false keeps direct files. Rebuilds a folder that is already playing.
    internal void SetFolderIncludeSubfolders(bool include)
    {
        if (config != null)
            config.FolderIncludeSubfolders = include;
        if (imageManager == null)
            return;
        if (!imageManager.SetFolderIncludeSubfolders(include))
            return;
        var handler = ImageChanged;
        if (handler != null)
            handler(this, EventArgs.Empty);
    }

    internal bool FolderIncludeSubfolders
    {
        get { return config == null || config.FolderIncludeSubfolders; }
    }

    // Folder of the file on screen.
    // Returns null when no file is showing.
    internal string CurrentDirectory()
    {
        if (imageManager == null)
            return null;
        var path = imageManager.GetCurrentImagePath();
        return Paths.Parent(path);
    }

    // True when path is one of the configured library folders.
    // path is compared without a trailing slash.
    internal bool IsLibraryRoot(string path)
    {
        if (config == null || config.ImagePaths == null || string.IsNullOrEmpty(path))
            return false;
        var norm = path.TrimEnd('\\', '/');
        for (int i = 0; i < config.ImagePaths.Count; i++)
        {
            var root = config.ImagePaths[i];
            if (string.IsNullOrEmpty(root))
                continue;
            if (Paths.Same(norm, root.TrimEnd('\\', '/')))
                return true;
        }
        return false;
    }

    // Child folders for the explorer.
    // directory null lists the library roots. totalFiles and directFiles count the files there.
    internal List<FolderChoice> ListExplorer(string directory, out int totalFiles, out int directFiles)
    {
        totalFiles = 0;
        directFiles = 0;
        if (imageManager == null)
            return new List<FolderChoice>();
        List<FolderChoice> list;
        if (string.IsNullOrEmpty(directory))
            list = imageManager.ListRoots(config == null ? null : config.ImagePaths, out totalFiles);
        else
            list = imageManager.ListChildFolders(directory, out totalFiles, out directFiles);
        MarkIgnoredFolders(list);
        return list;
    }

    // Sets IsIgnored on each row from the configured ignore list.
    // list may be null. Returns nothing.
    private void MarkIgnoredFolders(List<FolderChoice> list)
    {
        if (list == null)
            return;
        for (int i = 0; i < list.Count; i++)
            list[i].IsIgnored = IsPathIgnored(list[i].FullPath);
    }

    // Path segments from the drive down to directory.
    // directory null returns an empty list.
    internal List<FolderChoice> BrowseCrumbs(string directory)
    {
        if (imageManager == null)
            return new List<FolderChoice>();
        return imageManager.GetBrowseCrumbs(directory);
    }

    internal bool IsFolderPlay
    {
        get { return imageManager != null && imageManager.IsFolderPlay; }
    }

    internal string FolderPlayMode
    {
        get
        {
            return config == null || string.IsNullOrEmpty(config.FolderPlayMode)
                ? "Random"
                : config.FolderPlayMode;
        }
    }

    // Rebuilds the playlist in mode and shows the current file again if it remains.
    // Returns nothing.
    internal void SetPlaybackMode(string mode)
    {
        imageManager.SetMode(mode);
        config.PlaybackMode = mode;
        firstImage = true;
        ShowCurrentImage();
    }

    // Cycles photos and videos, photos only, then videos only.
    // Returns the label, or null when that choice has no files.
    internal string CycleMediaShow()
    {
        string next = NextMediaShow(config.MediaShow);
        var before = imageManager.GetCurrentImagePath();
        if (!imageManager.TrySetMediaShow(next))
            return null;

        config.MediaShow = imageManager.MediaShow;
        var after = imageManager.GetCurrentImagePath();
        if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
        {
            firstImage = true;
            ShowCurrentImage();
            ResetTimer();
        }
        return MediaTypes.ShowLabel(config.MediaShow);
    }

    // The show filter after current.
    // current is Both, Images, or Videos. Returns the next one.
    private static string NextMediaShow(string current)
    {
        current = MediaTypes.NormalizeShow(current);
        if (current == "Both")
            return "Images";
        if (current == "Images")
            return "Videos";
        return "Both";
    }

    // Turns Ken Burns on or off and stores it.
    // Returns nothing.
    internal void ToggleKenBurns()
    {
        transitionEngine.EnableKenBurns = !transitionEngine.EnableKenBurns;
        config.EnableKenBurns = transitionEngine.EnableKenBurns;
    }

    // File name, index, size, date, and playback state for the info overlay.
    // Returns the overlay text.
    internal string GetInfoText()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder(512);

        sb.Append(imageManager.GetImageInfo());

        string status = isPaused ? "PAUSED" : (videoPlaying ? "VIDEO" : "PLAYING");
        string mode = imageManager.IsFolderPlay
            ? "Folder (" + FolderOrder.Label(imageManager.FolderPlayMode) + ") → " + imageManager.FolderResumeMode
            : imageManager.CurrentMode;
        sb.AppendFormat(ci, "\n\nStatus: {0}  |  Mode: {1}",
            status, mode);
        sb.Append("\nShowing: " + MediaTypes.ShowLabel(config.MediaShow));
        sb.AppendFormat(ci, "\nDisplay: {0:F1}s  |  Transition: {1:F1}s  |  Volume: {2:P0}",
            displayDuration, displayDuration * transitionDurationPercent / 100.0,
            config.VideoVolume);

        var last = transitionEngine.LastTransition;
        sb.AppendFormat(ci, "\nLast transition: {0}",
            last == TransitionType.None ? "None (instant)" : last.ToString());

        sb.Append("\nEffects: ");
        bool any = false;
        if (transitionEngine.TransitionPool != null)
        {
            foreach (var t in transitionEngine.TransitionPool)
            {
                if (any) sb.Append(", ");
                sb.Append(t.ToString());
                any = true;
            }
        }
        if (transitionEngine.EnableKenBurns)
        {
            if (any) sb.Append(", ");
            sb.Append("KenBurns");
            any = true;
        }
        if (!any)
            sb.Append("None (instant cuts)");

        return sb.ToString();
    }

    // Stores the current index, path, and settings back into the toml.
    // Returns nothing.
    internal void SaveState()
    {
        if (imageManager.IsFolderPlay)
            imageManager.ExitFolderPlay();
        config.LastImageIndex = imageManager.CurrentIndex;
        config.LastImagePath = imageManager.GetCurrentImagePath();
        config.EnableKenBurns = transitionEngine.EnableKenBurns;
        config.VideoVolume = transitionEngine.VideoVolume;
        if (transitionEngine.TransitionPool != null)
        {
            config.ActiveTransitions = transitionEngine.TransitionPool
                .Select(t => t.ToString()).ToList();
            if (transitionEngine.EnableKenBurns)
                config.ActiveTransitions.Add("KenBurns");
        }
        ConfigService.SaveConfig(configPath, config);
    }
}
