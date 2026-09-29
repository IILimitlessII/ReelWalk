using System;
using System.Collections.Generic;
using System.IO;
using ReelWalk.Models;

namespace ReelWalk.Services;
internal sealed partial class ImageLibrary
{
    private readonly Dictionary<string, FolderNode> folderNodes =
        new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);

    private sealed class FolderNode
    {
        internal int Direct;
        internal int Total;
        internal Dictionary<string, FolderNode> Kids;
    }

    // Configured library roots that exist on disk, for the top of the explorer.
    // roots are the configured paths. totalFiles is unknown here (0); play scans the folder.
    internal List<FolderChoice> ListRoots(IList<string> roots, out int totalFiles)
    {
        totalFiles = 0;
        var result = new List<FolderChoice>();
        if (roots == null)
            return result;

        string here = CurrentFolderNorm();
        for (int i = 0; i < roots.Count; i++)
        {
            var full = roots[i];
            if (string.IsNullOrWhiteSpace(full))
                continue;
            var norm = Paths.Normalize(full) ?? full.TrimEnd('\\', '/');
            try
            {
                if (!Directory.Exists(norm))
                    continue;
            }
            catch
            {
                continue;
            }

            bool hereMatch = here != null && Paths.IsInside(here, norm, true);
            result.Add(new FolderChoice
            {
                Name = SegmentName(full, true),
                FullPath = norm,
                FileCount = 0,
                HasChildren = HasSubfolders(norm),
                IsHere = hereMatch,
                IsSelected = hereMatch
            });
        }

        EnsureOneSelected(result);
        return result;
    }

    // Immediate child folders on disk under directory.
    // directory is the open folder. directFiles stays 0; play scans when chosen.
    internal List<FolderChoice> ListChildFolders(string directory, out int totalFiles, out int directFiles)
    {
        totalFiles = 0;
        directFiles = 0;
        var result = new List<FolderChoice>();
        if (string.IsNullOrWhiteSpace(directory))
            return result;

        var dirNorm = Paths.Normalize(directory);
        if (dirNorm == null)
            return result;

        try
        {
            if (!Directory.Exists(dirNorm))
                return result;
        }
        catch
        {
            return result;
        }

        string here = CurrentFolderNorm();
        var names = new List<string>();
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dirNorm))
            {
                var name = Paths.Leaf(sub);
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
        }
        catch
        {
            return result;
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var full = Paths.Combine(dirNorm, name);
            if (string.IsNullOrEmpty(full))
                continue;
            bool hereMatch = here != null && Paths.IsInside(here, full, true);
            result.Add(new FolderChoice
            {
                Name = name,
                FullPath = full,
                FileCount = 0,
                HasChildren = HasSubfolders(full),
                IsHere = hereMatch,
                IsSelected = hereMatch
            });
        }

        EnsureOneSelected(result);
        return result;
    }

    // True when folder has at least one readable subdirectory.
    // Returns false when the folder cannot be listed.
    private static bool HasSubfolders(string folder)
    {
        try
        {
            using (var e = Directory.EnumerateDirectories(folder).GetEnumerator())
                return e.MoveNext();
        }
        catch
        {
            return false;
        }
    }

    // Path segments from the drive down to directory.
    // Returns an empty list when directory is blank.
    internal List<FolderChoice> GetBrowseCrumbs(string directory)
    {
        var result = new List<FolderChoice>();
        if (string.IsNullOrWhiteSpace(directory))
            return result;

        string dir = directory.TrimEnd('\\', '/');
        var chain = new List<string>();
        while (!string.IsNullOrEmpty(dir))
        {
            chain.Add(dir);
            var parent = Paths.Parent(dir);
            if (parent == null)
                break;
            dir = parent;
        }
        chain.Reverse();

        for (int i = 0; i < chain.Count; i++)
        {
            var full = chain[i];
            result.Add(new FolderChoice
            {
                Name = SegmentName(full, i == 0),
                FullPath = full,
                ShowSeparator = i > 0,
                IsSelected = i == chain.Count - 1
            });
        }

        return result;
    }

    // Folder of the file on screen, normalized.
    // Returns null when nothing is showing.
    private string CurrentFolderNorm()
    {
        var current = GetCurrentImagePath();
        if (string.IsNullOrEmpty(current))
            return null;
        return Paths.Normalize(Paths.Parent(current));
    }

    // Highlights the first row when none is marked.
    // result may be empty. Returns nothing.
    private static void EnsureOneSelected(List<FolderChoice> result)
    {
        if (result == null || result.Count == 0)
            return;
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].IsSelected)
                return;
        }
        result[0].IsSelected = true;
    }

    // Display name of one path segment.
    // isFirst keeps a UNC share as a whole name. Returns the segment.
    private static string SegmentName(string fullPath, bool isFirst)
    {
        if (string.IsNullOrEmpty(fullPath))
            return fullPath;

        var trimmed = fullPath.TrimEnd('\\', '/');
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (isFirst)
                return trimmed;
        }

        var name = Path.GetFileName(trimmed);
        if (!string.IsNullOrEmpty(name))
            return name;
        return trimmed;
    }

    // How many playable files sit under folderNorm.
    // folderNorm has no trailing slash. Returns 0 when the index is empty.
    private int CountUnder(string folderNorm)
    {
        if (string.IsNullOrEmpty(folderNorm))
            return 0;
        FolderNode node;
        if (folderNodes.TryGetValue(folderNorm, out node))
            return node.Total;
        return 0;
    }

    // Rebuilds folder counts from the current library and media filter.
    // Returns nothing.
    private void RebuildFolderIndex()
    {
        folderNodes.Clear();
        if (allImagePaths == null)
            return;
        for (int i = 0; i < allImagePaths.Count; i++)
            NoteFolderFile(allImagePaths[i]);
    }

    // Counts one playable file under its folders.
    // originalPath is the library path. Returns nothing when the media filter skips it.
    private void NoteFolderFile(string originalPath)
    {
        if (!MediaTypes.Allows(originalPath, mediaShow))
            return;
        var norm = Paths.Normalize(originalPath) ?? originalPath;
        var dir = Paths.Parent(norm);
        if (string.IsNullOrEmpty(dir))
            return;
        AddFileUnder(dir);
    }

    // Removes one playable file from the folder counts.
    // originalPath is the library path. Returns nothing when it was not counted.
    private void ForgetFolderFile(string originalPath)
    {
        if (!MediaTypes.Allows(originalPath, mediaShow))
            return;
        var norm = Paths.Normalize(originalPath) ?? originalPath;
        var dir = Paths.Parent(norm);
        if (string.IsNullOrEmpty(dir))
            return;
        RemoveFileUnder(dir);
    }

    // Adds one file that sits in directory, then counts it on each parent.
    // directory is the file's folder. Returns nothing.
    private void AddFileUnder(string directory)
    {
        var node = GetFolderNode(directory);
        node.Direct++;
        node.Total++;
        string childName = Paths.Leaf(directory);
        string parent = Paths.Parent(directory);
        var current = node;
        while (!string.IsNullOrEmpty(parent))
        {
            var up = GetFolderNode(parent);
            up.Total++;
            if (up.Kids == null)
                up.Kids = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
            up.Kids[childName] = current;
            childName = Paths.Leaf(parent);
            current = up;
            parent = Paths.Parent(parent);
        }
    }

    // Drops one file from directory and its parents, and removes empty folders.
    // directory is the file's folder. Returns nothing.
    private void RemoveFileUnder(string directory)
    {
        string dir = directory;
        bool direct = true;
        while (!string.IsNullOrEmpty(dir))
        {
            FolderNode node;
            if (!folderNodes.TryGetValue(dir, out node))
                return;
            if (direct && node.Direct > 0)
                node.Direct--;
            if (node.Total > 0)
                node.Total--;
            direct = false;
            if (node.Total > 0)
            {
                dir = Paths.Parent(dir);
                continue;
            }

            string name = Paths.Leaf(dir);
            string parent = Paths.Parent(dir);
            folderNodes.Remove(dir);
            if (!string.IsNullOrEmpty(parent))
            {
                FolderNode up;
                if (folderNodes.TryGetValue(parent, out up) && up.Kids != null)
                    up.Kids.Remove(name);
            }
            dir = parent;
        }
    }

    // The node for directory, creating it when missing.
    // directory is a normalized folder. Returns the node.
    private FolderNode GetFolderNode(string directory)
    {
        FolderNode node;
        if (!folderNodes.TryGetValue(directory, out node))
        {
            node = new FolderNode();
            folderNodes[directory] = node;
        }
        return node;
    }

}
