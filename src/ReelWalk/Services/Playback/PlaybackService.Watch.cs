using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReelWalk.Models;

namespace ReelWalk.Services;
internal sealed partial class PlaybackService
{
    // Starts the folder watchers and the periodic scan once the library is authoritative.
    // Returns nothing.
    private void EnsureAutoRefresh()
    {
        if (autoRefreshStarted || video == null)
            return;
        autoRefreshStarted = true;

        var dispatcher = Dispatcher.UIThread;
        StartWatchers(dispatcher);

        watchFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        watchFlushTimer.Tick += (s, e) => FlushWatch();

        libraryRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        libraryRefreshTimer.Tick += (s, e) => RefreshLibraryQuietly(dispatcher);
        libraryRefreshTimer.Start();
    }

    // Watches each library root for new and deleted files.
    // dispatcher marshals the updates. Returns nothing.
    private void StartWatchers(Dispatcher dispatcher)
    {
        if (config.ImagePaths == null)
            return;

        for (int i = 0; i < config.ImagePaths.Count; i++)
        {
            var root = config.ImagePaths[i];
            if (string.IsNullOrWhiteSpace(root))
                continue;
            try
            {
                if (!Directory.Exists(root))
                    continue;
                var watcher = new FileSystemWatcher(root);
                watcher.IncludeSubdirectories = true;
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;
                watcher.InternalBufferSize = 64 * 1024;
                watcher.Created += (s, e) => NoteWatch(dispatcher, e.FullPath, false);
                watcher.Renamed += (s, e) =>
                {
                    NoteWatch(dispatcher, e.OldFullPath, true);
                    NoteWatch(dispatcher, e.FullPath, false);
                };
                watcher.Deleted += (s, e) => NoteWatch(dispatcher, e.FullPath, true);
                watcher.Error += (s, e) =>
                {
                    dispatcher.Post(() => RefreshLibraryQuietly(dispatcher));
                };
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch { }
        }
    }

    // Records one watcher event to apply after a short pause.
    // removed true deletes path. Returns nothing.
    private void NoteWatch(Dispatcher dispatcher, string path, bool removed)
    {
        if (string.IsNullOrEmpty(path))
            return;
        if (!removed && !MediaTypes.IsSupported(path))
            return;
        if (LibraryScanner.IsIgnored(path, config.IgnorePaths))
            return;

        lock (watchGate)
        {
            if (removed)
                watchRemoved.Add(path);
            else
                watchAdded.Add(path);
        }

        dispatcher.Post(() =>
        {
            if (watchFlushTimer != null && !watchFlushTimer.IsEnabled)
                watchFlushTimer.Start();
        });
    }

    // Applies the paused watcher events to the library.
    // Returns nothing.
    private void FlushWatch()
    {
        if (watchFlushTimer != null)
            watchFlushTimer.Stop();

        List<string> added = null;
        List<string> removed = null;
        lock (watchGate)
        {
            if (watchAdded.Count > 0)
            {
                added = new List<string>(watchAdded);
                watchAdded.Clear();
            }
            if (watchRemoved.Count > 0)
            {
                removed = new List<string>(watchRemoved);
                watchRemoved.Clear();
            }
        }

        int grew = 0;
        if (added != null)
        {
            int before = imageManager.SnapshotPaths().Count;
            imageManager.AbsorbFiles(added, false);
            grew = imageManager.SnapshotPaths().Count - before;
        }

        bool lostCurrent = false;
        if (removed != null)
            lostCurrent = imageManager.RemoveUnavailable(removed);

        if (grew > 0 || (removed != null && removed.Count > 0))
            SaveLibrarySnapshot();

        if (lostCurrent)
        {
            ShowCurrentImage();
            ResetTimer();
        }

        if (grew > 0)
        {
            var handler = FilesAdded;
            if (handler != null)
                handler(grew);
        }
    }

    // Scans for new files only. A missing drive does not wipe the library.
    // dispatcher shows the toast. Returns nothing.
    private void RefreshLibraryQuietly(Dispatcher dispatcher)
    {
        if (Interlocked.CompareExchange(ref refreshRunning, 1, 0) != 0)
            return;

        var roots = config.ImagePaths;
        var ignore = config.IgnorePaths;
        Task.Run(() =>
        {
            List<string> found = null;
            try
            {
                if (RootExists(roots))
                    found = LibraryScanner.Scan(roots, ignore, null, null);
            }
            catch { }

            dispatcher.Post(() =>
            {
                try
                {
                    if (found == null || found.Count == 0)
                        return;
                    int before = imageManager.SnapshotPaths().Count;
                    imageManager.AbsorbFiles(found, false);
                    int grew = imageManager.SnapshotPaths().Count - before;
                    if (grew > 0)
                    {
                        SaveLibrarySnapshot();
                        var handler = FilesAdded;
                        if (handler != null)
                            handler(grew);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref refreshRunning, 0);
                }
            });
        });
    }

    // Rewrites the library cache on a background thread.
    // Returns nothing.
    private void SaveLibrarySnapshot()
    {
        var copy = imageManager.SnapshotPaths();
        var path = libraryCachePath;
        if (string.IsNullOrEmpty(path) || copy.Count == 0)
            return;
        Task.Run(() => LibraryCache.Save(path, copy));
    }

    // True when at least one library root is reachable.
    // roots are the configured folders.
    private static bool RootExists(IList<string> roots)
    {
        if (roots == null)
            return false;
        for (int i = 0; i < roots.Count; i++)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(roots[i]) && Directory.Exists(roots[i]))
                    return true;
            }
            catch { }
        }
        return false;
    }

    // Stops the watchers and the refresh timers.
    // Returns nothing.
    private void StopAutoRefresh()
    {
        if (libraryRefreshTimer != null)
            libraryRefreshTimer.Stop();
        if (watchFlushTimer != null)
            watchFlushTimer.Stop();
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
    }

    // Stops playback, the video, and the watchers.
    // Returns nothing.
    public void Dispose()
    {
        StopAutoRefresh();
        if (videoSeek != null) videoSeek.Cancel();
        if (timer != null) timer.Stop();
        UnhookVideo();
        if (transitionEngine != null)
        {
            try
            {
                transitionEngine.StopAllAnimations();
                transitionEngine.StopVideo();
            }
            catch { }
        }
        if (imageManager != null) imageManager.Dispose();
    }

    // Turns enabled transition names into the animation pool.
    // names may include KenBurns, which is not a transition. Returns the pool.
    private static List<TransitionType> ParseTransitionPool(List<string> names)
    {
        var pool = new List<TransitionType>();
        if (names == null) return pool;
        foreach (var name in names)
        {
            if (name.Equals("KenBurns", StringComparison.OrdinalIgnoreCase))
                continue;

            TransitionType t;
            if (Enum.TryParse(name, true, out t) && t != TransitionType.None)
                pool.Add(t);
        }
        return pool;
    }
}
