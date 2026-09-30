using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReelWalk.Services;
internal static class LibraryCache
{
    private const string Header = "reelwalk-library-v1";

    // True when a saved library has at least one path.
    // path is ReelWalk.library. Does not read the whole file.
    internal static bool HasEntries(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            using (var reader = new StreamReader(path))
            {
                if (reader.ReadLine() != Header)
                    return false;
                var line = reader.ReadLine();
                return line != null && line.Length > 0;
            }
        }
        catch
        {
            return false;
        }
    }

    // Reads the saved file list.
    // path is ReelWalk.library. Returns null when the file is missing or not this format.
    internal static List<string> Load(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2 || lines[0] != Header)
                return null;

            var files = new List<string>(lines.Length - 1);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                    files.Add(lines[i]);
            }
            return files.Count > 0 ? files : null;
        }
        catch
        {
            return null;
        }
    }

    // Writes the file list, replacing the previous cache.
    // path is the cache file. files is the full list. Returns nothing.
    internal static void Save(string path, List<string> files)
    {
        if (string.IsNullOrEmpty(path) || files == null)
            return;

        try
        {
            var tmp = path + ".tmp";
            using (var writer = new StreamWriter(tmp, false, new UTF8Encoding(false)))
            {
                writer.WriteLine(Header);
                for (int i = 0; i < files.Count; i++)
                    writer.WriteLine(files[i]);
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch
        {
            try
            {
                var tmp = path + ".tmp";
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch { }
        }
    }

    // Keeps the existing index and appends paths the scan found that were not already saved.
    // cached is the previous ReelWalk.library. found is the latest full scan.
    // Returns null when nothing new was found (caller should not rewrite the file).
    internal static List<string> MergeNew(List<string> cached, List<string> found)
    {
        if (found == null || found.Count == 0)
            return null;
        if (cached == null || cached.Count == 0)
            return found;

        var known = new HashSet<string>(cached.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < cached.Count; i++)
        {
            if (!string.IsNullOrEmpty(cached[i]))
                known.Add(cached[i]);
        }

        var merged = new List<string>(cached.Count + 64);
        merged.AddRange(cached);
        bool grew = false;
        for (int i = 0; i < found.Count; i++)
        {
            var path = found[i];
            if (string.IsNullOrEmpty(path))
                continue;
            if (!known.Add(path))
                continue;
            merged.Add(path);
            grew = true;
        }

        return grew ? merged : null;
    }
}
