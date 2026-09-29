using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ReelWalk.Services;
internal static class LibraryScanner
{
    private static int scanActive;
    private static int pace;

    // Walks roots one at a time and leaves gaps so playback can use the disk.
    // ignorePaths are skipped with their children. onNewFiles receives batches. Returns null when a scan is already running.
    internal static List<string> Scan(
        IList<string> roots,
        IList<string> ignorePaths,
        Action<int> progress,
        Action<List<string>> onNewFiles)
    {
        if (Interlocked.CompareExchange(ref scanActive, 1, 0) != 0)
            return null;

        var previous = Thread.CurrentThread.Priority;
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            pace = 0;
            return ScanNow(roots, ignorePaths, progress, onNewFiles);
        }
        finally
        {
            Thread.CurrentThread.Priority = previous;
            Interlocked.Exchange(ref scanActive, 0);
        }
    }

    // Walks roots and reports files as they are found.
    // ignorePaths are skipped with their children. onNewFiles receives batches. Returns every accepted path.
    private static List<string> ScanNow(
        IList<string> roots,
        IList<string> ignorePaths,
        Action<int> progress,
        Action<List<string>> onNewFiles)
    {
        var all = new List<string>();
        if (roots == null || roots.Count == 0)
            return all;

        var ignore = PathFilter.From(ignorePaths);
        var pending = new List<string>(64);
        int flushed = 0;

        Action<string> accept = file =>
        {
            if (ignore.Blocks(file))
                return;

            all.Add(file);
            pending.Add(file);
            int n = all.Count;
            List<string> batch = null;
            int threshold = flushed == 0 ? 16 : 128;
            if (pending.Count >= threshold)
            {
                batch = pending;
                pending = new List<string>(64);
                flushed += batch.Count;
            }

            if (progress != null && (n == 1 || (n % 400) == 0))
                progress(n);

            if (batch != null && onNewFiles != null)
                onNewFiles(batch);

            EaseOff();
        };

        if (roots != null)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                try
                {
                    if (!Directory.Exists(root) || ignore.Blocks(root))
                        continue;
                }
                catch
                {
                    continue;
                }

                Walk(root, ignore, accept);
            }
        }

        List<string> last = null;
        if (pending.Count > 0)
            last = pending;

        if (last != null && onNewFiles != null)
            onNewFiles(last);

        if (progress != null)
            progress(all.Count);

        return all;
    }

    // Pauses the walk every few hundred files so playback can read the disk.
    // Returns nothing.
    private static void EaseOff()
    {
        pace++;
        if (pace < 256)
            return;
        pace = 0;
        Thread.Sleep(8);
    }

    // Breadth-first walk of root.
    // accept is called for each supported file. Returns nothing.
    private static void Walk(string root, PathFilter ignore, Action<string> accept)
    {
        var dirs = new Queue<string>();
        dirs.Enqueue(root);

        while (dirs.Count > 0)
        {
            var dir = dirs.Dequeue();
            if (ignore.Blocks(dir))
                continue;

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (!ignore.Blocks(sub))
                        dirs.Enqueue(sub);
                }
            }
            catch { }

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (MediaTypes.IsSupported(file))
                        accept(file);
                }
            }
            catch { }
        }
    }

    // Drops files that sit under an ignore path.
    // Returns a new list, or a copy of files when nothing is ignored.
    internal static List<string> FilterIgnored(IList<string> files, IList<string> ignorePaths)
    {
        if (files == null || files.Count == 0)
            return files == null ? new List<string>() : new List<string>(files);

        var ignore = PathFilter.From(ignorePaths);
        if (ignore.IsEmpty)
            return files as List<string> ?? new List<string>(files);

        var kept = new List<string>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            if (!ignore.Blocks(files[i]))
                kept.Add(files[i]);
        }
        return kept;
    }

    // True when path is an ignore folder or inside one.
    // ignorePaths are the configured folders.
    internal static bool IsIgnored(string path, IList<string> ignorePaths)
    {
        return PathFilter.From(ignorePaths).Blocks(path);
    }
}

internal sealed class PathFilter
{
    private readonly string[] _roots;

    // Stores normalized ignore folders.
    // roots are full paths without a trailing slash. Returns nothing.
    private PathFilter(string[] roots)
    {
        _roots = roots;
    }

    internal bool IsEmpty { get { return _roots == null || _roots.Length == 0; } }

    // Builds a filter from the configured ignore list.
    // ignorePaths may be null. Returns an empty filter when there are none.
    internal static PathFilter From(IList<string> ignorePaths)
    {
        if (ignorePaths == null || ignorePaths.Count == 0)
            return new PathFilter(new string[0]);

        var list = new List<string>(ignorePaths.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ignorePaths.Count; i++)
        {
            var n = Paths.Normalize(ignorePaths[i]);
            if (n != null && seen.Add(n))
                list.Add(n);
        }
        return new PathFilter(list.ToArray());
    }

    // True when path equals an ignore folder or is inside one.
    // path is a file or folder. Returns false for an empty filter.
    internal bool Blocks(string path)
    {
        if (IsEmpty || string.IsNullOrEmpty(path))
            return false;

        var n = Paths.Normalize(path);
        if (n == null)
            return false;

        for (int i = 0; i < _roots.Length; i++)
        {
            if (Paths.IsInside(n, _roots[i], true))
                return true;
        }
        return false;
    }
}
