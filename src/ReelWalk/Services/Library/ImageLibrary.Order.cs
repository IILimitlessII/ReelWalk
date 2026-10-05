using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReelWalk.Services;
internal sealed partial class ImageLibrary
{
    // Moves the index to keep, or to a random file when keep is gone.
    // keep is a path. Returns nothing.
    private void PlaceOn(string keep)
    {
        if (playbackOrder == null || playbackOrder.Count == 0)
        {
            currentIndex = 0;
            return;
        }

        int idx = FindPath(playbackOrder, keep);
        if (idx >= 0)
            currentIndex = idx;
        else if (IsRandom(currentMode) || (folderPlayActive && IsRandom(folderPlayMode)))
            currentIndex = PickRandomIndex(-1);
        else
            currentIndex = 0;
    }

    // Orders source for mode, after the show filter.
    // mode is Random, NewestFirst, OldestFirst, or Sequential. Returns the playlist.
    private List<string> BuildPlaybackOrder(List<string> source, string mode)
    {
        source = OnlyAllowed(source);
        switch (mode)
        {
            case "Random":
                var shuffled = source.ToList();
                ShuffleInPlace(shuffled);
                return shuffled;

            case "NewestFirst":
                return SortByDate(source, true);

            case "OldestFirst":
                return SortByDate(source, false);

            case "Sequential":
            default:
                return source
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
    }

    // Shuffles list.
    // Returns nothing.
    private void ShuffleInPlace(List<string> list)
    {
        lock (randomGate)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }
    }

    // True when mode is Random.
    // mode is a playback name. Returns false for ordered and blank.
    private static bool IsRandom(string mode)
    {
        return string.Equals(mode, "Random", StringComparison.OrdinalIgnoreCase);
    }

    // Shuffles or sorts files for a folder mode.
    // mode is Random, Sequential, SizeAsc, SizeDesc, LengthAsc, or LengthDesc. Returns nothing.
    private void OrderFolderFiles(List<string> files, string mode)
    {
        if (files == null)
            return;
        mode = FolderOrder.Normalize(mode);
        if (mode == FolderOrder.Random)
        {
            ShuffleInPlace(files);
            return;
        }
        if (mode == FolderOrder.SizeAsc || mode == FolderOrder.SizeDesc)
        {
            SortByMeasure(files, mode == FolderOrder.SizeDesc, false, FileBytes);
            return;
        }
        if (mode == FolderOrder.LengthAsc || mode == FolderOrder.LengthDesc)
        {
            SortByMeasure(files, mode == FolderOrder.LengthDesc, true, VideoDuration.Milliseconds);
            return;
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
    }

    // Sorts files by a measurement taken once per file.
    // missingLast puts unknown lengths after every known length. Returns nothing.
    private static void SortByMeasure(List<string> files, bool descending, bool missingLast, Func<string, long> measure)
    {
        var values = new long[files.Count];
        for (int i = 0; i < files.Count; i++)
            values[i] = measure(files[i]);

        var order = new int[files.Count];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => CompareMeasured(values[a], values[b], files[a], files[b], descending, missingLast));

        var copy = new string[files.Count];
        files.CopyTo(copy);
        for (int i = 0; i < order.Length; i++)
            files[i] = copy[order[i]];
    }

    // Compares two measurements, then the file name.
    // missing lengths stay last in both directions. Returns the sort order.
    private static int CompareMeasured(long aValue, long bValue, string aPath, string bPath, bool descending, bool missingLast)
    {
        bool aMissing = missingLast && aValue < 0;
        bool bMissing = missingLast && bValue < 0;
        if (aMissing || bMissing)
        {
            if (aMissing && bMissing)
                return StringComparer.OrdinalIgnoreCase.Compare(aPath, bPath);
            return aMissing ? 1 : -1;
        }

        int cmp = aValue.CompareTo(bValue);
        if (cmp == 0)
            return StringComparer.OrdinalIgnoreCase.Compare(aPath, bPath);
        return descending ? -cmp : cmp;
    }

    // Byte length of a file.
    // path is a file. Returns 0 when it cannot be read.
    private static long FileBytes(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    // True for random library playback.
    // Returns false during folder play.
    private bool IsRandomMode()
    {
        return !folderPlayActive && IsRandom(currentMode);
    }

    // Picks a file that was not shown recently.
    // avoid is the current index. Returns an index.
    private int PickRandomIndex(int avoid)
    {
        int count = playbackOrder != null ? playbackOrder.Count : 0;
        if (count <= 0) return 0;
        if (count == 1) return 0;

        int next = 0;
        lock (randomGate)
        {
            for (int t = 0; t < 48; t++)
            {
                next = random.Next(count);
                if (next == avoid)
                    continue;
                var path = playbackOrder[next];
                if (!string.IsNullOrEmpty(path) && recentSet.Contains(path))
                    continue;
                return next;
            }

            next = random.Next(count);
            if (next == avoid)
                next = (next + 1) % count;
        }
        return next;
    }

    // Remembers path so random will not repeat it soon.
    // Returns nothing.
    private void RememberRecent(string path)
    {
        if (string.IsNullOrEmpty(path) || playbackOrder == null)
            return;

        if (recentSet.Add(path))
            recentOrder.AddFirst(path);
        else
        {
            var node = recentOrder.First;
            while (node != null)
            {
                if (string.Equals(node.Value, path, StringComparison.OrdinalIgnoreCase))
                {
                    recentOrder.Remove(node);
                    recentOrder.AddFirst(node);
                    break;
                }
                node = node.Next;
            }
        }

        int cap = RecentCap();
        while (recentOrder.Count > cap)
        {
            var last = recentOrder.Last;
            if (last == null)
                break;
            recentOrder.RemoveLast();
            recentSet.Remove(last.Value);
        }
    }

    // How many recent files random should avoid.
    // Returns 0 when the library is tiny.
    private int RecentCap()
    {
        int count = playbackOrder == null ? 0 : playbackOrder.Count;
        if (count <= 2)
            return 0;
        int cap = count / 4;
        if (cap < 12)
            cap = Math.Min(12, count - 1);
        if (cap > 48)
            cap = 48;
        if (cap >= count)
            cap = count - 1;
        return cap;
    }

}
