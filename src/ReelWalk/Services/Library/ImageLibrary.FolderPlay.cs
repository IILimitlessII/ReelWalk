using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReelWalk.Models;
using ReelWalk.Services.Playback;

namespace ReelWalk.Services;
internal sealed partial class ImageLibrary : IDisposable
{
    // Plays folderPath, including subfolders unless that choice is off.
    // mode is Random or Sequential. Returns false when fewer than two files can play.
    internal bool EnterFolderPlay(string folderPath, string mode)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        try
        {
            if (!Directory.Exists(folderPath))
                return false;
        }
        catch
        {
            return false;
        }

        var folderFiles = CollectFolderFiles(folderPath);
        if (folderFiles.Count <= 1)
            return false;

        mode = NormalizeFolderMode(mode);
        OrderFolderFiles(folderFiles, mode);

        var current = GetCurrentImagePath();
        int start = FindPath(folderFiles, current);
        if (start < 0) start = 0;

        if (!folderPlayActive)
        {
            savedPlaybackOrder = playbackOrder;
            savedIndex = currentIndex;
            savedMode = currentMode ?? "Random";
            folderPlayActive = true;
        }

        folderPlayMode = mode;
        folderPlayPath = folderPath;
        currentMode = "Folder";
        playbackOrder = folderFiles;
        currentIndex = start;
        folderRemaining = playbackOrder.Count - 1;

        history.Clear();
        PrepareShown();
        return true;
    }

    // Reorders the folder that is already playing.
    // mode is Random or Sequential. Returns nothing when folder play is off.
    internal void ApplyFolderPlayMode(string mode)
    {
        if (!folderPlayActive || playbackOrder == null || playbackOrder.Count == 0)
            return;

        mode = NormalizeFolderMode(mode);
        folderPlayMode = mode;
        var current = GetCurrentImagePath();
        OrderFolderFiles(playbackOrder, mode);

        int start = FindPath(playbackOrder, current);
        if (start < 0) start = 0;
        currentIndex = start;
        folderRemaining = playbackOrder.Count - 1;
        history.Clear();
        PrepareShown();
    }

    // Restores the playlist from before folder play.
    // Returns nothing.
    internal void ExitFolderPlay()
    {
        if (!folderPlayActive) return;
        RestoreSavedPlaylist(GetCurrentImagePath());
    }

    // Maps mode to Random or Sequential.
    // Returns Random for anything that is not sequential.
    private static string NormalizeFolderMode(string mode)
    {
        if (!string.IsNullOrEmpty(mode) &&
            (mode.Equals("Random", StringComparison.OrdinalIgnoreCase) ||
             mode.Equals("Shuffle", StringComparison.OrdinalIgnoreCase)))
            return "Random";
        return "Sequential";
    }

    // Replaces the indexed paths and their normalized copies.
    // paths may be null. Returns nothing.
    private void SetLibraryFiles(List<string> paths)
    {
        allImagePaths = paths ?? new List<string>();
        normalizedAll = new List<string>(allImagePaths.Count);
        for (int i = 0; i < allImagePaths.Count; i++)
            normalizedAll.Add(Paths.Normalize(allImagePaths[i]) ?? allImagePaths[i]);
        RebuildFolderIndex();
    }

    // Adds paths to the index.
    // added is the new files. Returns nothing.
    private void AppendLibraryFiles(List<string> added)
    {
        if (allImagePaths == null)
            allImagePaths = new List<string>();
        if (normalizedAll == null || normalizedAll.Count != allImagePaths.Count)
            SetLibraryFiles(allImagePaths);

        allImagePaths.AddRange(added);
        for (int i = 0; i < added.Count; i++)
        {
            normalizedAll.Add(Paths.Normalize(added[i]) ?? added[i]);
            NoteFolderFile(added[i]);
        }
    }

    // Removes one path from the index.
    // path is the file. Returns nothing when it is absent.
    private void RemoveLibraryFile(string path)
    {
        if (allImagePaths == null)
            return;
        int at = -1;
        for (int i = 0; i < allImagePaths.Count; i++)
        {
            if (string.Equals(allImagePaths[i], path, StringComparison.OrdinalIgnoreCase))
            {
                at = i;
                break;
            }
        }
        if (at < 0)
            return;
        ForgetFolderFile(allImagePaths[at]);
        allImagePaths.RemoveAt(at);
        if (normalizedAll != null && at < normalizedAll.Count)
            normalizedAll.RemoveAt(at);
    }

    // Playable files for folder play.
    // folderPath is the chosen folder. Subfolders are included unless that choice is off.
    private List<string> CollectFolderFiles(string folderPath)
    {
        var files = new List<string>();
        var folder = Paths.Normalize(folderPath);
        if (folder == null)
            return files;

        var prefix = Paths.FolderPrefix(folder);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (allImagePaths != null && normalizedAll != null &&
            normalizedAll.Count == allImagePaths.Count)
        {
            for (int i = 0; i < normalizedAll.Count; i++)
            {
                var n = normalizedAll[i];
                if (n != null &&
                    IncludeFolderFile(n, prefix) &&
                    MediaTypes.Allows(allImagePaths[i], mediaShow) &&
                    seen.Add(allImagePaths[i]))
                    files.Add(allImagePaths[i]);
            }
        }
        else if (allImagePaths != null)
        {
            for (int i = 0; i < allImagePaths.Count; i++)
            {
                var p = allImagePaths[i];
                if (BelongsInFolderPlay(p, folder) &&
                    MediaTypes.Allows(p, mediaShow) &&
                    seen.Add(p))
                    files.Add(p);
            }
        }

        var current = GetCurrentImagePath();
        if (!string.IsNullOrEmpty(current) &&
            BelongsInFolderPlay(current, folder) &&
            seen.Add(current))
            files.Add(current);

        if (files.Count <= 1)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(folderPath))
                {
                    if (MediaTypes.Allows(f, mediaShow) && seen.Add(f))
                        files.Add(f);
                }
            }
            catch { }
        }

        return files;
    }

    // Sets whether folder play includes subfolders.
    // include false keeps direct files. Returns true when a folder already playing was rebuilt.
    internal bool SetFolderIncludeSubfolders(bool include)
    {
        folderIncludeSubfolders = include;
        if (!folderPlayActive || string.IsNullOrEmpty(folderPlayPath))
            return false;

        var folderFiles = CollectFolderFiles(folderPlayPath);
        if (folderFiles.Count == 0)
        {
            ExitFolderPlay();
            return true;
        }

        OrderFolderFiles(folderFiles, folderPlayMode);

        playbackOrder = folderFiles;
        PlaceOn(GetCurrentImagePath());
        folderRemaining = Math.Max(0, playbackOrder.Count - 1);
        history.Clear();
        PrepareShown();
        return true;
    }

    // True when normalizedFile belongs under prefix.
    // prefix includes the trailing slash. Direct files only when subfolders are off.
    private bool IncludeFolderFile(string normalizedFile, string prefix)
    {
        if (string.IsNullOrEmpty(normalizedFile) ||
            !normalizedFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (folderIncludeSubfolders)
            return true;
        var rest = normalizedFile.Substring(prefix.Length);
        return rest.IndexOf('\\') < 0 && rest.IndexOf('/') < 0;
    }

    // True when file belongs in the folder currently being played.
    // folderNorm has no trailing slash. Returns false for subfolders when that choice is off.
    private bool BelongsInFolderPlay(string file, string folderNorm)
    {
        if (folderIncludeSubfolders)
            return Paths.IsInside(file, folderNorm, false);
        var n = Paths.Normalize(file);
        if (n == null)
            return false;
        return Paths.Same(Paths.Parent(n), folderNorm);
    }

    // Ends folder play and lands on keepPath when it is still in the library.
    // Returns nothing.
    private void RestoreSavedPlaylist(string keepPath)
    {
        folderPlayActive = false;
        folderPlayPath = null;
        folderRemaining = 0;
        currentMode = string.IsNullOrEmpty(savedMode) ? "Random" : savedMode;

        if (savedPlaybackOrder != null && savedPlaybackOrder.Count > 0)
            playbackOrder = savedPlaybackOrder;
        else
            playbackOrder = BuildPlaybackOrder(allImagePaths, currentMode);

        int idx = -1;
        if (!string.IsNullOrEmpty(keepPath) && playbackOrder != null)
        {
            idx = FindPath(playbackOrder, keepPath);
        }
        if (idx >= 0)
            currentIndex = idx;
        else
            currentIndex = Math.Max(0, Math.Min(savedIndex, playbackOrder.Count - 1));

        savedPlaybackOrder = null;
        history.Clear();
        PrepareShown();
    }
}
