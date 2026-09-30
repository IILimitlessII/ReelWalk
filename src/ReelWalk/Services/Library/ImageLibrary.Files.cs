using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using System.Threading.Tasks;
using ReelWalk.Models;
using ReelWalk.Services.Playback;

namespace ReelWalk.Services;
internal sealed partial class ImageLibrary : IDisposable
{
    // Drops the file on screen from the playlist without deleting it.
    // The index then points at the next file. Returns false when nothing is left.
    internal bool DropUnplayableCurrent()
    {
        if (playbackOrder == null || playbackOrder.Count == 0)
            return false;

        var path = playbackOrder[currentIndex];
        RemoveLibraryFile(path);
        if (knownPaths != null)
            knownPaths.Remove(path);
        playbackOrder.RemoveAt(currentIndex);
        NotePlaybackRemoved(path, currentIndex);
        history.Shift(currentIndex);
        if (savedPlaybackOrder != null)
        {
            for (int i = savedPlaybackOrder.Count - 1; i >= 0; i--)
            {
                if (string.Equals(savedPlaybackOrder[i], path, StringComparison.OrdinalIgnoreCase))
                    savedPlaybackOrder.RemoveAt(i);
            }
        }

        imageCache.Clear();
        upcomingRandom = -1;

        if (playbackOrder.Count == 0)
        {
            currentIndex = 0;
            if (folderPlayActive)
                RestoreSavedPlaylist(null);
            return playbackOrder != null && playbackOrder.Count > 0;
        }

        if (currentIndex >= playbackOrder.Count)
            currentIndex = 0;
        if (folderPlayActive && folderRemaining > 0)
            folderRemaining--;
        return true;
    }

    // Copy of every indexed path.
    // Returns an empty list when the library is empty.
    internal List<string> SnapshotPaths()
    {
        if (allImagePaths == null || allImagePaths.Count == 0)
            return new List<string>();
        return new List<string>(allImagePaths);
    }

    // Removes files that disappeared.
    // A directory path removes everything inside it. Returns whether the file on screen was removed.
    internal bool RemoveUnavailable(IList<string> paths)
    {
        if (paths == null || paths.Count == 0 || allImagePaths == null || allImagePaths.Count == 0)
            return false;

        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefixes = new List<string>();
        for (int i = 0; i < paths.Count; i++)
        {
            var n = Paths.Normalize(paths[i]);
            if (n == null)
                continue;
            exact.Add(n);
            prefixes.Add(Paths.FolderPrefix(n));
        }
        if (exact.Count == 0)
            return false;

        string current = GetCurrentImagePath();
        bool lostCurrent = false;
        var keepAll = new List<string>(allImagePaths.Count);
        var keepNorm = new List<string>(allImagePaths.Count);
        for (int i = 0; i < allImagePaths.Count; i++)
        {
            var original = allImagePaths[i];
            var n = normalizedAll != null && i < normalizedAll.Count
                ? normalizedAll[i]
                : Paths.Normalize(original);
            if (PathDropped(n, exact, prefixes))
            {
                if (knownPaths != null)
                    knownPaths.Remove(original);
                if (!lostCurrent && current != null &&
                    string.Equals(original, current, StringComparison.OrdinalIgnoreCase))
                    lostCurrent = true;
                continue;
            }
            keepAll.Add(original);
            keepNorm.Add(n);
        }

        if (keepAll.Count == allImagePaths.Count)
            return false;

        allImagePaths = keepAll;
        normalizedAll = keepNorm;
        FilterPlayList(ref playbackOrder, exact, prefixes, ref currentIndex);
        RebuildPlaybackIndex();
        if (savedPlaybackOrder != null)
        {
            int ignored = 0;
            FilterPlayList(ref savedPlaybackOrder, exact, prefixes, ref ignored);
        }
        if (folderPlayActive && playbackOrder != null)
            folderRemaining = Math.Max(0, playbackOrder.Count - 1);

        imageCache.Clear();
        upcomingRandom = -1;
        RebuildFolderIndex();
        return lostCurrent;
    }

    // True when normalized is an exact path or sits under a removed folder.
    // prefixes include the trailing slash.
    private static bool PathDropped(string normalized, HashSet<string> exact, List<string> prefixes)
    {
        if (string.IsNullOrEmpty(normalized))
            return false;
        if (exact.Contains(normalized))
            return true;
        for (int i = 0; i < prefixes.Count; i++)
        {
            var prefix = prefixes[i];
            if (string.IsNullOrEmpty(prefix))
                continue;
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
            // Older caches may have used the opposite separator.
            var alt = prefix.EndsWith("\\")
                ? prefix.TrimEnd('\\') + "/"
                : prefix.TrimEnd('/') + "\\";
            if (normalized.StartsWith(alt, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Removes dropped paths from order and fixes index.
    // exact and prefixes come from the removal. Returns nothing.
    private static void FilterPlayList(
        ref List<string> order,
        HashSet<string> exact,
        List<string> prefixes,
        ref int index)
    {
        if (order == null || order.Count == 0)
            return;

        int newIndex = index;
        var keep = new List<string>(order.Count);
        for (int i = 0; i < order.Count; i++)
        {
            var n = Paths.Normalize(order[i]);
            if (PathDropped(n, exact, prefixes))
            {
                if (i < index)
                    newIndex--;
                continue;
            }
            keep.Add(order[i]);
        }

        order = keep;
        if (order.Count == 0)
            index = 0;
        else if (newIndex >= order.Count)
            index = 0;
        else if (newIndex < 0)
            index = 0;
        else
            index = newIndex;
    }

    // Moves by step, using a preloaded random pick when one is ready.
    // step is usually 1 or -1. Returns nothing.
    private void JumpRandomOrSequential(int step)
    {
        if (playbackOrder.Count == 0) return;

        if (IsRandomMode() && playbackOrder.Count > 1)
        {
            RememberRecent(GetCurrentImagePath());
            history.NoteJump(currentIndex);
            if (step > 0 &&
                upcomingRandom >= 0 &&
                upcomingRandom < playbackOrder.Count &&
                upcomingRandom != currentIndex &&
                !recentSet.Contains(playbackOrder[upcomingRandom]))
                currentIndex = upcomingRandom;
            else
                currentIndex = PickRandomIndex(currentIndex);
            upcomingRandom = -1;
            return;
        }

        currentIndex = (currentIndex + step) % playbackOrder.Count;
        if (currentIndex < 0)
            currentIndex += playbackOrder.Count;
    }

    // Sorts by the date the photo was taken, then by the file time.
    // newestFirst reverses the order. Videos use the file time. Returns the ordered paths.
    private static List<string> SortByDate(List<string> source, bool newestFirst)
    {
        var withDates = new KeyValuePair<string, long>[source.Count];
        Parallel.For(0, source.Count, i =>
        {
            long ticks = 0;
            var taken = ExifDates.TryRead(source[i]);
            if (taken.HasValue)
                ticks = taken.Value.Ticks;
            else
            {
                try { ticks = File.GetLastWriteTime(source[i]).Ticks; }
                catch { }
            }
            withDates[i] = new KeyValuePair<string, long>(source[i], ticks);
        });

        if (newestFirst)
            Array.Sort(withDates, (a, b) => b.Value.CompareTo(a.Value));
        else
            Array.Sort(withDates, (a, b) => a.Value.CompareTo(b.Value));

        var result = new List<string>(withDates.Length);
        for (int i = 0; i < withDates.Length; i++)
            result.Add(withDates[i].Key);
        return result;
    }

    // True when a and b hold the same paths, ignoring order.
    // Returns false when either list is null.
    private static bool SameFileSet(List<string> a, List<string> b)
    {
        if (a == null || b == null || a.Count != b.Count)
            return false;

        var set = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        if (set.Count != b.Count)
            return false;
        for (int i = 0; i < b.Count; i++)
        {
            if (!set.Contains(b[i]))
                return false;
        }
        return true;
    }

    // Index of path in files.
    // The live playlist is a dictionary lookup. Any other list is scanned. Returns -1 when it is missing.
    private int FindPath(List<string> files, string path)
    {
        if (files == null || string.IsNullOrEmpty(path))
            return -1;
        if (ReferenceEquals(files, playbackOrder))
        {
            int idx;
            if (playbackIndex.TryGetValue(path, out idx) &&
                idx >= 0 && idx < playbackOrder.Count &&
                Paths.Same(playbackOrder[idx], path))
                return idx;
            return -1;
        }
        for (int i = 0; i < files.Count; i++)
        {
            if (Paths.Same(files[i], path))
                return i;
        }
        return -1;
    }

    // Fills the playlist index after the order is replaced or shuffled.
    // Returns nothing.
    private void RebuildPlaybackIndex()
    {
        playbackIndex.Clear();
        if (playbackOrder == null)
            return;
        for (int i = 0; i < playbackOrder.Count; i++)
            RememberPlaybackIndex(playbackOrder[i], i);
    }

    // Records paths appended to the playlist, starting at from.
    // Returns nothing.
    private void NotePlaybackAdded(int from)
    {
        if (playbackOrder == null)
            return;
        if (from < 0)
            from = 0;
        if (playbackIndex.Count == 0 && from > 0)
        {
            RebuildPlaybackIndex();
            return;
        }
        for (int i = from; i < playbackOrder.Count; i++)
            RememberPlaybackIndex(playbackOrder[i], i);
    }

    // Drops one playlist path and shifts later indexes down.
    // path is the file removed at removed. Returns nothing.
    private void NotePlaybackRemoved(string path, int removed)
    {
        if (playbackIndex.Count == 0)
        {
            RebuildPlaybackIndex();
            return;
        }

        int mapped;
        if (!string.IsNullOrEmpty(path) &&
            playbackIndex.TryGetValue(path, out mapped) &&
            mapped == removed)
            playbackIndex.Remove(path);

        if (playbackIndex.Count == 0)
            return;

        var later = new List<string>();
        foreach (var pair in playbackIndex)
        {
            if (pair.Value > removed)
                later.Add(pair.Key);
        }
        for (int i = 0; i < later.Count; i++)
            playbackIndex[later[i]] = playbackIndex[later[i]] - 1;
    }

    // Stores the first index for path.
    // Returns nothing.
    private void RememberPlaybackIndex(string path, int index)
    {
        if (string.IsNullOrEmpty(path))
            return;
        if (!playbackIndex.ContainsKey(path))
            playbackIndex.Add(path, index);
    }

    // True for a local drive path that can be shown before the scan.
    // path must look like a drive letter. Returns false for network paths.
    private static bool IsQuickLocalFile(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 3 || path[1] != ':')
            return false;
        try { return File.Exists(path); }
        catch { return false; }
    }

    // True when one scan root is reachable.
    // roots are the configured folders.
    private static bool AnyRootExists(List<string> roots)
    {
        if (roots == null) return false;
        for (int i = 0; i < roots.Count; i++)
        {
            try
            {
                if (!string.IsNullOrEmpty(roots[i]) && Directory.Exists(roots[i]))
                    return true;
            }
            catch { }
        }
        return false;
    }

    // Pixel width of the primary screen.
    // Returns 1920 when the window is not ready.
    private static int ScreenWidth()
    {
        try
        {
            var desktop = Avalonia.Application.Current == null
                ? null
                : Avalonia.Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var screen = desktop == null || desktop.MainWindow == null
                ? null
                : desktop.MainWindow.Screens.Primary;
            if (screen != null && screen.Bounds.Width > 0)
                return screen.Bounds.Width;
        }
        catch { }
        return 1920;
    }

    // Holds a bitmap that just left the cache so the outgoing frame can finish.
    // bitmap may be null. Returns nothing.
    private void Retire(Bitmap bitmap)
    {
        if (bitmap == null)
            return;
        retired.Add(bitmap);
        while (retired.Count > 8)
        {
            retired[0].Dispose();
            retired.RemoveAt(0);
        }
    }

    // Path of the file at the current index.
    // Returns null when the playlist is empty.
    internal string GetCurrentImagePath()
    {
        if (playbackOrder.Count == 0) return null;
        return playbackOrder[currentIndex];
    }

    // Cached still for the current file.
    // Returns null for a video, a cache miss, or a file that will not decode.
    internal Bitmap GetCurrentImage()
    {
        if (playbackOrder == null || playbackOrder.Count == 0)
            return null;

        Bitmap cached;
        if (imageCache.TryGetValue(currentIndex, out cached))
            return cached;
        return null;
    }
}
