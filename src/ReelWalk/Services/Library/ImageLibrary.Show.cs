using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReelWalk.Models;
using ReelWalk.Services.Playback;

namespace ReelWalk.Services;
internal sealed partial class ImageLibrary
{
    // Advances one file. Folder play returns to the library when the folder ends.
    // Returns nothing.
    internal void Next()
    {
        if (playbackOrder.Count == 0) return;

        if (folderPlayActive)
        {
            if (folderRemaining <= 0)
            {
                RestoreSavedPlaylist(GetCurrentImagePath());
                if (playbackOrder.Count == 0) return;
            }
            else
            {
                folderRemaining--;
                currentIndex = (currentIndex + 1) % playbackOrder.Count;
                AdvanceShown();
                return;
            }
        }

        if (IsRandomMode() && history.TryAdvance(ref currentIndex))
        {
            AdvanceShown();
            return;
        }

        JumpRandomOrSequential(1);

        AdvanceShown();
    }

    // Goes back one file. Random walks the files just shown, up to the history limit.
    // Returns nothing when Random has nothing left to return to.
    internal void Previous()
    {
        if (playbackOrder.Count == 0) return;

        if (IsRandomMode())
        {
            if (!history.TryRetreat(ref currentIndex))
                return;
        }
        else
            JumpRandomOrSequential(-1);

        AdvanceShown();
    }

    // Moves to index and preloads.
    // index is clamped. Returns nothing.
    internal void JumpTo(int index)
    {
        if (playbackOrder.Count == 0) return;

        currentIndex = Math.Max(0, Math.Min(index, playbackOrder.Count - 1));
        AdvanceShown();
    }

    // Jumps count files. Random forward picks one unseen file. Random back walks the trail.
    // count may be negative. Returns nothing.
    internal void Skip(int count)
    {
        if (playbackOrder.Count == 0) return;

        if (IsRandomMode())
        {
            if (count < 0)
            {
                if (history.Retreat(ref currentIndex, -count) == 0)
                    return;
            }
            else
                JumpRandomOrSequential(1);
        }
        else
        {
            currentIndex = (currentIndex + count) % playbackOrder.Count;
            if (currentIndex < 0)
                currentIndex += playbackOrder.Count;
        }

        AdvanceShown();
    }

    // Removes the current file from the playlist after it was deleted.
    // Returns nothing.
    internal void RemoveCurrent()
    {
        if (playbackOrder.Count == 0) return;

        var path = playbackOrder[currentIndex];

        RemoveLibraryFile(path);
        if (knownPaths != null)
            knownPaths.Remove(path);
        playbackOrder.RemoveAt(currentIndex);
        history.Shift(currentIndex);

        DropCache();

        if (playbackOrder.Count == 0)
        {
            currentIndex = 0;
            if (folderPlayActive)
                RestoreSavedPlaylist(null);
            return;
        }

        if (currentIndex >= playbackOrder.Count)
            currentIndex = 0;

        PreloadImages();
    }

    // Rebuilds the playlist in mode and keeps the current file when it remains.
    // Returns nothing.
    internal void SetMode(string mode)
    {
        if (folderPlayActive)
        {
            folderPlayActive = false;
            folderPlayPath = null;
            folderRemaining = 0;
            savedPlaybackOrder = null;
        }
        else if (currentMode == mode)
        {
            return;
        }

        var currentPath = GetCurrentImagePath();
        currentMode = mode;
        playbackOrder = BuildPlaybackOrder(allImagePaths, currentMode);

        if (currentPath != null)
        {
            var newIndex = FindPath(playbackOrder, currentPath);
            currentIndex = newIndex >= 0 ? newIndex : 0;
        }

        history.Clear();
        PrepareShown();
    }

    // Decodes the file on screen and a few around it, off the UI thread.
    // Returns nothing.
    private void PreloadImages()
    {
        if (playbackOrder == null || playbackOrder.Count == 0)
            return;

        int baseIndex = currentIndex;
        int count = playbackOrder.Count;
        bool randomPicks = IsRandomMode() && count > 1;
        int planned = -1;
        int backIndex = history.RecentBack;
        int forwardIndex = history.RecentForward;
        if (randomPicks)
        {
            planned = PickRandomIndex(baseIndex);
            upcomingRandom = planned;
        }

        if (randomPicks)
        {
            QueueDecode(PathAt(baseIndex));
            if (planned >= 0 && planned != baseIndex)
                QueueDecode(PathAt(planned));
            if (backIndex >= 0 && backIndex < count && backIndex != baseIndex && backIndex != planned)
                QueueDecode(PathAt(backIndex));
            if (forwardIndex >= 0 && forwardIndex < count &&
                forwardIndex != baseIndex && forwardIndex != planned && forwardIndex != backIndex)
                QueueDecode(PathAt(forwardIndex));
            return;
        }

        for (int i = 0; i <= PRELOAD_COUNT; i++)
            QueueDecode(PathAt((baseIndex + i) % count));
    }

    // Path at index, or null when the playlist does not have it.
    // index is a playback index. Returns the path.
    private string PathAt(int index)
    {
        if (playbackOrder == null || index < 0 || index >= playbackOrder.Count)
            return null;
        return playbackOrder[index];
    }

    // Starts a background decode when path is not cached or already running.
    // path is a still. Returns nothing.
    private void QueueDecode(string path)
    {
        if (libraryReleased || string.IsNullOrEmpty(path) || MediaTypes.IsVideo(path))
            return;
        if (failedStills.Contains(path))
            return;
        int existing = FindPath(playbackOrder, path);
        if (existing >= 0 && imageCache.ContainsKey(existing))
            return;
        if (!decodeInflight.TryAdd(path, 0))
            return;

        Task.Run(() =>
        {
            Bitmap image = null;
            try { image = imageDecoder.Load(path); }
            catch { }
            var ready = image;
            try
            {
                Dispatcher.UIThread.Post(() => FinishDecode(path, ready));
            }
            catch (InvalidOperationException)
            {
                if (ready != null)
                    ready.Dispose();
            }
        });
    }

    // Stores a finished decode and shows it when it is still the current file.
    // path is the file that was decoded. image is null when decoding failed. Returns nothing.
    private void FinishDecode(string path, Bitmap image)
    {
        byte ignored;
        decodeInflight.TryRemove(path, out ignored);
        if (libraryReleased)
        {
            if (image != null)
                image.Dispose();
            return;
        }

        if (image == null)
            failedStills.Add(path);
        else
            StoreStill(path, image);

        if (!string.Equals(GetCurrentImagePath(), path, StringComparison.OrdinalIgnoreCase))
            return;
        var handler = StillDecoded;
        if (handler != null)
            handler();
    }

    // Keeps image for path's place in the playlist.
    // image is retired when path is no longer listed. Returns nothing.
    private void StoreStill(string path, Bitmap image)
    {
        int idx = FindPath(playbackOrder, path);
        if (idx < 0 || !imageCache.TryAdd(idx, image))
            Retire(image);
    }

    // Cached still for the file on screen, or a background decode.
    // bitmap is set only when Ready. Returns Waiting while that decode is running.
    internal StillFetch FetchCurrentStill(out Bitmap bitmap)
    {
        bitmap = null;
        if (playbackOrder == null || playbackOrder.Count == 0)
            return StillFetch.Missing;

        var path = playbackOrder[currentIndex];
        if (string.IsNullOrEmpty(path) || MediaTypes.IsVideo(path))
            return StillFetch.Missing;

        Bitmap cached;
        if (imageCache.TryGetValue(currentIndex, out cached) && cached != null)
        {
            bitmap = cached;
            return StillFetch.Ready;
        }

        if (failedStills.Contains(path))
            return StillFetch.Missing;

        QueueDecode(path);
        return StillFetch.Waiting;
    }

    // Drops decoded stills that are far from the current index.
    // Returns nothing.
    private void CleanupCache()
    {
        int count = playbackOrder.Count;
        if (count == 0) return;

        if (IsRandomMode())
        {
            int backIndex = history.RecentBack;
            int forwardIndex = history.RecentForward;
            foreach (var key in imageCache.Keys)
            {
                if (key != currentIndex && key != upcomingRandom &&
                    key != backIndex && key != forwardIndex)
                {
                    Bitmap removed;
                    if (imageCache.TryRemove(key, out removed))
                        Retire(removed);
                }
            }
            return;
        }

        foreach (var key in imageCache.Keys)
        {
            bool keep = false;
            for (int i = 0; i <= PRELOAD_COUNT; i++)
            {
                if (key == (currentIndex + i) % count)
                {
                    keep = true;
                    break;
                }
            }
            if (!keep)
            {
                Bitmap removed;
                if (imageCache.TryRemove(key, out removed))
                    Retire(removed);
            }
        }
    }

    // Clears decoded stills and prepares the file now on screen.
    // Returns nothing.
    private void PrepareShown()
    {
        DropCache();
        PreloadImages();
    }

    // Releases every cached still after the outgoing frame can finish.
    // Returns nothing.
    private void DropCache()
    {
        foreach (var pair in imageCache)
            Retire(pair.Value);
        imageCache.Clear();
    }

    // Updates the file on screen and drops stills that are far away.
    // Returns nothing.
    private void AdvanceShown()
    {
        CleanupCache();
        PreloadImages();
    }

    // Remembers the size and date of the current file.
    // Returns nothing.
    private void CacheFileInfo()
    {
        cachedFileSize = 0;
        cachedFileDate = DateTime.MinValue;
        cachedFilePath = null;

        if (playbackOrder.Count == 0) return;

        var path = playbackOrder[currentIndex];
        cachedFilePath = path;

        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists)
            {
                cachedFileSize = fi.Length;
                cachedFileDate = fi.LastWriteTime;
            }
        }
        catch { }
    }

    // Text for the info overlay.
    // Returns a short message when the library is empty.
    internal string GetImageInfo()
    {
        if (playbackOrder.Count == 0)
            return "No images found";

        CacheFileInfo();
        var path = cachedFilePath ?? GetCurrentImagePath();
        var sb = new StringBuilder(256);

        sb.Append(Path.GetFileName(path));
        string label = MediaTypes.IsVideo(path) ? "Video" : "Image";
        sb.AppendFormat("\n{0} {1} of {2}", label, currentIndex + 1, playbackOrder.Count);

        if (cachedFileSize > 0)
        {
            if (cachedFileSize >= 1024 * 1024)
                sb.AppendFormat("\nSize: {0:F1} MB", cachedFileSize / (1024.0 * 1024.0));
            else
                sb.AppendFormat("\nSize: {0:F0} KB", cachedFileSize / 1024.0);
        }

        if (cachedFileDate != DateTime.MinValue)
            sb.AppendFormat("\nDate: {0:yyyy-MM-dd HH:mm}", cachedFileDate);

        Bitmap cached;
        if (imageCache.TryGetValue(currentIndex, out cached) && cached != null)
            sb.AppendFormat("\nDimensions: {0} x {1}", cached.PixelSize.Width, cached.PixelSize.Height);

        sb.AppendFormat("\nPath: {0}", Path.GetDirectoryName(path));
        if (provisional)
            sb.Append("\nLibrary: still updating");

        return sb.ToString();
    }

    // Releases decoded stills.
    // Returns nothing.
    public void Dispose()
    {
        libraryReleased = true;
        DropCache();
        while (retired.Count > 0)
        {
            retired[0].Dispose();
            retired.RemoveAt(0);
        }
    }
}
