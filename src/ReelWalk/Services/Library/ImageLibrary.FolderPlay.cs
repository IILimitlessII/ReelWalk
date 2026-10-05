using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReelWalk.Models;
using ReelWalk.Services.Playback;

namespace ReelWalk.Services;
internal sealed partial class ImageLibrary : IDisposable
{
    // Plays folderPath, including subfolders unless that choice is off.
    // mode is a folder order. ready runs on the UI thread with false when nothing can play.
    // Returns false when the folder does not exist.
    internal bool EnterFolderPlay(string folderPath, string mode, IList<string> ignorePaths, Action<bool> ready)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        var folder = Paths.Normalize(folderPath);
        try
        {
            if (folder == null || !Directory.Exists(folder))
                return false;
        }
        catch
        {
            return false;
        }

        folderPlayGeneration++;
        int gen = folderPlayGeneration;
        mode = FolderOrder.Normalize(mode);
        folderPlayMode = mode;
        folderPlayPath = folder;
        bool includeSub = folderIncludeSubfolders;
        string show = mediaShow;
        var ignore = PathFilter.From(ignorePaths);
        var paths = allImagePaths == null ? new List<string>() : new List<string>(allImagePaths);
        var norms = normalizedAll == null ? new List<string>() : new List<string>(normalizedAll);
        var dispatcher = Dispatcher.UIThread;

        Task.Run(() =>
        {
            var known = TakeFolderFiles(paths, norms, folder, includeSub, show);
            if (known.Count >= 2)
            {
                OrderFolderFiles(known, mode);
                dispatcher.Post(() => InstallFolderPlaylist(gen, known, 0, false, ready));
                return;
            }

            string starter = null;
            string randomPick = null;
            int seen = 0;
            var rng = new Random();
            var gathered = new List<string>();
            WalkFolderFiles(folder, includeSub, show, ignore, file =>
            {
                if (starter == null)
                    starter = file;
                seen++;
                if (rng.Next(seen) == 0)
                    randomPick = file;
                gathered.Add(file);
                if (gathered.Count == 1 || gathered.Count % 128 == 0)
                {
                    var batch = gathered;
                    gathered = new List<string>();
                    var open = starter;
                    dispatcher.Post(() => AppendFolderBatch(gen, batch, open, false, ready));
                }
            });

            if (gathered.Count > 0)
            {
                var last = gathered;
                var open = starter;
                var pick = IsRandom(mode) ? randomPick : null;
                dispatcher.Post(() =>
                {
                    AppendFolderBatch(gen, last, open, true, ready);
                    FinishFolderOrder(gen, pick, open);
                });
            }
            else
            {
                var pick = IsRandom(mode) ? randomPick : null;
                var open = starter;
                dispatcher.Post(() =>
                {
                    if (gen != folderPlayGeneration)
                        return;
                    if (starter == null)
                    {
                        if (ready != null)
                            ready(false);
                        return;
                    }
                    FinishFolderOrder(gen, pick, open);
                });
            }
        });
        return true;
    }

    // Reorders the folder that is already playing.
    // mode is a folder order. Returns nothing when folder play is off.
    internal void ApplyFolderPlayMode(string mode)
    {
        if (!folderPlayActive || playbackOrder == null || playbackOrder.Count == 0)
            return;

        mode = FolderOrder.Normalize(mode);
        folderPlayMode = mode;
        var current = GetCurrentImagePath();
        OrderFolderFiles(playbackOrder, mode);
        RebuildPlaybackIndex();

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

    // Puts an ordered folder list on screen.
    // ready runs when there is something to play. Returns nothing.
    private void InstallFolderPlaylist(int gen, List<string> files, int start, bool filling, Action<bool> ready)
    {
        if (gen != folderPlayGeneration)
            return;
        if (files == null || files.Count < 2)
        {
            if (ready != null)
                ready(false);
            return;
        }

        if (!folderPlayActive)
        {
            savedPlaybackOrder = playbackOrder;
            savedIndex = currentIndex;
            savedMode = currentMode ?? "Random";
            folderPlayActive = true;
        }

        currentMode = "Folder";
        folderListFilling = filling;
        playbackOrder = files;
        RebuildPlaybackIndex();
        if (start < 0 || start >= files.Count)
            start = 0;
        currentIndex = start;
        folderRemaining = Math.Max(0, files.Count - 1);
        history.Clear();
        PrepareShown();
        if (ready != null)
            ready(true);
    }

    // Adds one scanned batch. The first batch starts playback.
    // starter is the first file found. Returns nothing.
    private void AppendFolderBatch(int gen, List<string> batch, string starter, bool complete, Action<bool> ready)
    {
        if (gen != folderPlayGeneration)
            return;
        if (batch == null || batch.Count == 0)
        {
            if (complete && string.IsNullOrEmpty(starter) && ready != null)
                ready(false);
            return;
        }

        bool starting = !folderPlayActive;
        if (starting)
        {
            savedPlaybackOrder = playbackOrder;
            savedIndex = currentIndex;
            savedMode = currentMode ?? "Random";
            folderPlayActive = true;
            currentMode = "Folder";
            folderListFilling = true;
            playbackOrder = new List<string>();
            RebuildPlaybackIndex();
            history.Clear();
        }

        RememberInLibrary(batch);
        int from = playbackOrder.Count;
        for (int i = 0; i < batch.Count; i++)
        {
            var path = batch[i];
            if (string.IsNullOrEmpty(path) || playbackIndex.ContainsKey(path))
                continue;
            playbackOrder.Add(path);
        }
        NotePlaybackAdded(from);
        folderRemaining = Math.Max(folderRemaining, Math.Max(0, playbackOrder.Count - 1));

        if (!starting)
            return;

        currentIndex = 0;
        PrepareShown();
        if (ready != null)
            ready(playbackOrder.Count > 0);
    }

    // After a disk walk, order the folder and keep the file on screen unless it is still the opener.
    // randomPick is the uniform random file. starter is the first file shown. Returns nothing.
    private void FinishFolderOrder(int gen, string randomPick, string starter)
    {
        if (gen != folderPlayGeneration || !folderPlayActive || playbackOrder == null)
            return;

        folderListFilling = false;
        if (playbackOrder.Count < 2)
        {
            RestoreSavedPlaylist(GetCurrentImagePath());
            return;
        }

        var keep = GetCurrentImagePath();
        bool stillOpener = string.Equals(keep, starter, StringComparison.OrdinalIgnoreCase);
        OrderFolderFiles(playbackOrder, folderPlayMode);
        RebuildPlaybackIndex();

        int idx = FindPath(playbackOrder, keep);
        if (stillOpener && IsRandom(folderPlayMode) && !string.IsNullOrEmpty(randomPick))
        {
            int pick = FindPath(playbackOrder, randomPick);
            if (pick >= 0)
                idx = pick;
        }
        else if (stillOpener && !IsRandom(folderPlayMode))
        {
            idx = 0;
        }
        if (idx < 0)
            idx = 0;
        currentIndex = idx;
        folderRemaining = Math.Max(0, playbackOrder.Count - 1);
        PrepareShown();
    }

    // Adds paths the folder walk found that the library did not already know.
    // Returns nothing.
    private void RememberInLibrary(IList<string> files)
    {
        if (files == null)
            return;
        if (knownPaths == null)
            knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var added = new List<string>();
        for (int i = 0; i < files.Count; i++)
        {
            var path = files[i];
            if (string.IsNullOrEmpty(path))
                continue;
            if (knownPaths.Add(path))
                added.Add(path);
        }
        if (added.Count > 0)
            AppendLibraryFiles(added);
    }

    // Indexed files that sit in folder.
    // Returns the matching paths.
    private static List<string> TakeFolderFiles(
        List<string> paths,
        List<string> norms,
        string folder,
        bool includeSub,
        string show)
    {
        var files = new List<string>();
        if (paths == null || norms == null)
            return files;
        int n = paths.Count < norms.Count ? paths.Count : norms.Count;
        for (int i = 0; i < n; i++)
        {
            if (!InFolder(norms[i], folder, includeSub))
                continue;
            if (MediaTypes.Allows(paths[i], show))
                files.Add(paths[i]);
        }
        return files;
    }

    // True when normalized file sits in folder.
    // includeSub false keeps only files directly inside folder.
    private static bool InFolder(string file, string folder, bool includeSub)
    {
        if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(folder))
            return false;
        if (file.Length <= folder.Length)
            return false;
        if (!file.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            return false;
        char sep = file[folder.Length];
        if (sep != '\\' && sep != '/')
            return false;
        if (includeSub)
            return true;
        return file.IndexOf('\\', folder.Length + 1) < 0 &&
               file.IndexOf('/', folder.Length + 1) < 0;
    }

    // Walks folder on disk and calls accept for each playable file.
    // ignore skips those folders. A repeated directory is not walked again. Returns nothing.
    private static void WalkFolderFiles(
        string folder,
        bool includeSub,
        string show,
        PathFilter ignore,
        Action<string> accept)
    {
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!seenDirs.Add(folder) || (ignore != null && ignore.Blocks(folder)))
            return;

        var dirs = new Queue<string>();
        dirs.Enqueue(folder);
        while (dirs.Count > 0)
        {
            var dir = dirs.Dequeue();
            if (includeSub)
            {
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        var norm = Paths.Normalize(sub);
                        if (norm == null || !seenDirs.Add(norm))
                            continue;
                        if (ignore != null && ignore.Blocks(norm))
                            continue;
                        dirs.Enqueue(norm);
                    }
                }
                catch { }
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (!MediaTypes.Allows(file, show) || !seenFiles.Add(file))
                        continue;
                    if (ignore != null && ignore.Blocks(file))
                        continue;
                    if (accept != null)
                        accept(file);
                }
            }
            catch { }

            if (!includeSub)
                break;
        }
    }

    // Playable files for folder play.
    // Uses the indexed library. A disk walk runs only when the index has nothing there.
    private List<string> CollectFolderFiles(string folderPath)
    {
        var folder = Paths.Normalize(folderPath);
        if (folder == null)
            return new List<string>();

        var files = TakeFolderFiles(allImagePaths, normalizedAll, folder, folderIncludeSubfolders, mediaShow);
        if (files.Count > 0)
            return files;

        WalkFolderFiles(folder, folderIncludeSubfolders, mediaShow, null, files.Add);
        return files;
    }

    // Sets whether folder play includes subfolders.
    // include false keeps direct files. Returns true when a folder already playing was rebuilt.
    internal bool SetFolderIncludeSubfolders(bool include)
    {
        folderIncludeSubfolders = include;
        if (!folderPlayActive || string.IsNullOrEmpty(folderPlayPath))
            return false;

        folderPlayGeneration++;
        var folderFiles = CollectFolderFiles(folderPlayPath);
        if (folderFiles.Count == 0)
        {
            ExitFolderPlay();
            return true;
        }

        OrderFolderFiles(folderFiles, folderPlayMode);

        folderListFilling = false;
        playbackOrder = folderFiles;
        RebuildPlaybackIndex();
        PlaceOn(GetCurrentImagePath());
        folderRemaining = Math.Max(0, playbackOrder.Count - 1);
        history.Clear();
        PrepareShown();
        return true;
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
        folderPlayGeneration++;
        folderListFilling = false;
        folderPlayActive = false;
        folderPlayPath = null;
        folderRemaining = 0;
        currentMode = string.IsNullOrEmpty(savedMode) ? "Random" : savedMode;

        if (savedPlaybackOrder != null && savedPlaybackOrder.Count > 0)
            playbackOrder = savedPlaybackOrder;
        else
            playbackOrder = BuildPlaybackOrder(allImagePaths, currentMode);
        RebuildPlaybackIndex();

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
