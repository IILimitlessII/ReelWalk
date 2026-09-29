using System;
using System.IO;

namespace ReelWalk.Services;
internal static class Paths
{
    // Full path with quotes and a trailing slash removed.
    // path is raw text. Returns null when path is blank.
    internal static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var p = path.Trim().Trim('"');
        try { p = Path.GetFullPath(p); }
        catch { }
        return p.TrimEnd('\\', '/');
    }

    // True when a and b name the same path.
    // Blank paths do not match.
    internal static bool Same(string a, string b)
    {
        return !string.IsNullOrEmpty(a) &&
               !string.IsNullOrEmpty(b) &&
               string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // Parent directory of a file or folder.
    // path is a file or folder. Returns null when there is no parent.
    internal static string Parent(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        try
        {
            var trimmed = path.TrimEnd('\\', '/');
            var parent = Path.GetDirectoryName(trimmed);
            if (string.IsNullOrEmpty(parent) ||
                parent.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                parent.Equals(path, StringComparison.OrdinalIgnoreCase))
                return null;
            return parent;
        }
        catch
        {
            return null;
        }
    }

    // Last folder or file name in path.
    // path is a file or folder. Returns path when it has no separator.
    internal static string Leaf(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        var name = path.TrimEnd('\\', '/');
        try
        {
            var leaf = Path.GetFileName(name);
            if (!string.IsNullOrEmpty(leaf))
                return leaf;
        }
        catch { }

        int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (slash >= 0 && slash < name.Length - 1)
            return name.Substring(slash + 1);
        return name;
    }

    // folder plus a trailing separator, for prefix checks.
    // path may already be normalized. Returns null when path is blank.
    internal static string FolderPrefix(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        return path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
    }

    // Joins a folder and a child name with the OS separator, then normalizes.
    // Returns null when either part is blank.
    internal static string Combine(string folder, string child)
    {
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(child))
            return null;
        try
        {
            return Normalize(Path.Combine(folder.TrimEnd('\\', '/'), child));
        }
        catch
        {
            return FolderPrefix(folder) + child.TrimEnd('\\', '/');
        }
    }

    // True when file is folder, or sits inside it.
    // includeSelf false rejects file that is folder itself. Returns false when either path is blank.
    internal static bool IsInside(string file, string folder, bool includeSelf)
    {
        var n = Normalize(file);
        var root = Normalize(folder);
        if (n == null || root == null)
            return false;
        if (Same(n, root))
            return includeSelf;
        return n.Length > root.Length &&
               n.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
               (n[root.Length] == '\\' || n[root.Length] == '/');
    }
}
