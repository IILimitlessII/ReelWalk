using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReelWalk.Services;

namespace ReelWalk.Services;
internal sealed partial class PlaybackService
{
    // True when path is under a configured ignore folder.
    // path is a file or folder. Returns false when path is blank.
    internal bool IsPathIgnored(string path)
    {
        return LibraryScanner.IsIgnored(path, config.IgnorePaths);
    }

    // True when path is one of the library roots.
    // path is normalized for comparison.
    internal bool IsConfiguredLibraryRoot(string path)
    {
        if (config.ImagePaths == null || string.IsNullOrEmpty(path))
            return false;
        var norm = Paths.Normalize(path) ?? path.TrimEnd('\\', '/');
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

    // Adds a library root and rescans.
    // path is the folder. Returns false when it is missing or already listed.
    internal bool AddLibraryPath(string path)
    {
        path = Paths.Normalize(path);
        if (string.IsNullOrEmpty(path))
            return false;
        try
        {
            if (!Directory.Exists(path))
                return false;
        }
        catch
        {
            return false;
        }

        if (config.ImagePaths == null)
            config.ImagePaths = new List<string>();
        for (int i = 0; i < config.ImagePaths.Count; i++)
        {
            if (Paths.Same(config.ImagePaths[i], path))
                return false;
        }

        config.ImagePaths.Add(path);
        RescanAfterLibraryConfigChange();
        return true;
    }

    // Removes a library root and rescans.
    // path is the folder. Returns false when it was not listed.
    internal bool RemoveLibraryPath(string path)
    {
        if (config.ImagePaths == null || string.IsNullOrEmpty(path))
            return false;
        var norm = Paths.Normalize(path) ?? path;
        for (int i = 0; i < config.ImagePaths.Count; i++)
        {
            if (Paths.Same(config.ImagePaths[i], norm))
            {
                config.ImagePaths.RemoveAt(i);
                RescanAfterLibraryConfigChange();
                return true;
            }
        }
        return false;
    }

    // Adds or removes an ignore folder, then rescans.
    // path is the folder tree to skip. Returns true when ignore is now on.
    internal bool ToggleIgnorePath(string path)
    {
        path = Paths.Normalize(path);
        if (string.IsNullOrEmpty(path))
            return false;
        if (config.IgnorePaths == null)
            config.IgnorePaths = new List<string>();

        for (int i = 0; i < config.IgnorePaths.Count; i++)
        {
            if (Paths.Same(config.IgnorePaths[i], path))
            {
                config.IgnorePaths.RemoveAt(i);
                RescanAfterLibraryConfigChange();
                return false;
            }
        }

        config.IgnorePaths.Add(path);
        RescanAfterLibraryConfigChange();
        return true;
    }

    // Drops one ignore entry.
    // path is the folder. Returns false when it was not ignored.
    internal bool RemoveIgnorePath(string path)
    {
        if (config.IgnorePaths == null || string.IsNullOrEmpty(path))
            return false;
        var norm = Paths.Normalize(path) ?? path;
        for (int i = 0; i < config.IgnorePaths.Count; i++)
        {
            if (Paths.Same(config.IgnorePaths[i], norm))
            {
                config.IgnorePaths.RemoveAt(i);
                RescanAfterLibraryConfigChange();
                return true;
            }
        }
        return false;
    }

    // Full scan after paths or ignore changed.
    // Returns nothing.
    private void RescanAfterLibraryConfigChange()
    {
        if (Interlocked.CompareExchange(ref refreshRunning, 1, 0) != 0)
            return;

        var roots = config.ImagePaths == null ? new List<string>() : new List<string>(config.ImagePaths);
        var ignore = config.IgnorePaths == null ? new List<string>() : new List<string>(config.IgnorePaths);
        var dispatcher = Dispatcher.UIThread;

        Task.Run(() =>
        {
            List<string> found = new List<string>();
            try
            {
                if (RootExists(roots))
                    found = LibraryScanner.Scan(roots, ignore, null, null) ?? new List<string>();
            }
            catch { }

            dispatcher.Post(() =>
            {
                try
                {
                    imageManager.ApplyLibrary(found, true, ImageLibrary.LibraryStageScan);
                    SaveLibrarySnapshot();
                    SaveState();
                    RestartWatchers();
                    var handler = ImageChanged;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                }
                finally
                {
                    Interlocked.Exchange(ref refreshRunning, 0);
                }
            });
        });
    }

    // Rebuilds folder watchers after library roots change.
    // Returns nothing.
    private void RestartWatchers()
    {
        for (int i = 0; i < watchers.Count; i++)
        {
            try
            {
                watchers[i].EnableRaisingEvents = false;
                watchers[i].Dispose();
            }
            catch { }
        }
        watchers.Clear();
        StartWatchers(Dispatcher.UIThread);
    }
}
