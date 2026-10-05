using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class ImageLibraryFolderTests
{
    [AvaloniaFact]
    public void MissingFolder_ReturnsFalse()
    {
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var missing = Path.Combine(Path.GetTempPath(), "reelwalk-missing-" + Path.GetRandomFileName());
            Assert.False(lib.EnterFolderPlay(missing, "Sequential", null, null));
            Assert.False(lib.EnterFolderPlay("  ", "Sequential", null, null));
            Assert.False(lib.IsFolderPlay);
        }
    }

    [AvaloniaFact]
    public async Task IndexedOrderedFolder_StartsOnTheFirstName()
    {
        using (var dir = new FolderDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var folder = dir.Folder("album");
            var files = new[]
            {
                dir.Png(folder, "c.png"),
                dir.Png(folder, "a.png"),
                dir.Png(folder, "b.png")
            };
            lib.ApplyLibrary(files.ToList(), true, ImageLibrary.LibraryStageScan);

            bool? ready = null;
            Assert.True(lib.EnterFolderPlay(folder, "Ordered", null, ok => ready = ok));
            Assert.True(await Wait(() => ready.HasValue));
            Assert.True(ready);
            Assert.True(lib.IsFolderPlay);
            Assert.EndsWith("a.png", lib.GetCurrentImagePath(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, lib.TotalImages);
        }
    }

    [AvaloniaFact]
    public async Task SizeDesc_StartsOnTheLargestFile()
    {
        using (var dir = new FolderDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var folder = dir.Folder("album");
            var small = dir.Bytes(folder, "small.png", 20);
            var large = dir.Bytes(folder, "large.png", 400);
            var mid = dir.Bytes(folder, "mid.png", 80);
            lib.ApplyLibrary(new List<string> { small, large, mid }, true, ImageLibrary.LibraryStageScan);

            bool? ready = null;
            Assert.True(lib.EnterFolderPlay(folder, "Largest", null, ok => ready = ok));
            Assert.True(await Wait(() => ready == true));
            var order = Playlist(lib).Select(Path.GetFileName).ToArray();
            Assert.Equal(new[] { "large.png", "mid.png", "small.png" }, order);
        }
    }

    [AvaloniaFact]
    public async Task LengthAsc_PutsTheShortestVideoFirst()
    {
        using (var dir = new FolderDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var folder = dir.Folder("album");
            var photo = dir.Png(folder, "photo.png");
            var shortClip = dir.Mp4(folder, "short.mp4", 1500);
            var longClip = dir.Mp4(folder, "long.mp4", 8000);
            lib.ApplyLibrary(new List<string> { photo, longClip, shortClip }, true, ImageLibrary.LibraryStageScan);

            bool? ready = null;
            Assert.True(lib.EnterFolderPlay(folder, "Shortest", null, ok => ready = ok));
            Assert.True(await Wait(() => ready == true));
            var order = Playlist(lib).Select(Path.GetFileName).ToArray();
            Assert.Equal(new[] { "short.mp4", "long.mp4", "photo.png" }, order);
        }
    }

    [AvaloniaFact]
    public async Task DiskWalk_LeavesOutIgnoredChildren()
    {
        using (var dir = new FolderDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var folder = dir.Folder("album");
            var skip = dir.Folder("album", "skip");
            dir.Png(folder, "b.png");
            dir.Png(folder, "a.png");
            var hidden = dir.Png(skip, "c.png");

            bool? ready = null;
            Assert.True(lib.EnterFolderPlay(folder, "Sequential", new[] { skip }, ok => ready = ok));
            Assert.True(await Wait(() => ready == true));
            var order = Playlist(lib);
            Assert.Equal(2, order.Count);
            Assert.DoesNotContain(order, path => Paths.Same(path, hidden));
            Assert.EndsWith("a.png", order[0], StringComparison.OrdinalIgnoreCase);
        }
    }

    [AvaloniaFact]
    public async Task SubfoldersOff_DropsNestedFiles()
    {
        using (var dir = new FolderDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var folder = dir.Folder("album");
            var nested = dir.Png(dir.Folder("album", "inner"), "c.png");
            var files = new List<string>
            {
                dir.Png(folder, "a.png"),
                dir.Png(folder, "b.png"),
                nested
            };
            lib.ApplyLibrary(files, true, ImageLibrary.LibraryStageScan);

            bool? ready = null;
            Assert.True(lib.EnterFolderPlay(folder, "Sequential", null, ok => ready = ok));
            Assert.True(await Wait(() => ready == true));
            Assert.Equal(3, lib.TotalImages);

            Assert.True(lib.SetFolderIncludeSubfolders(false));
            var order = Playlist(lib);
            Assert.Equal(2, order.Count);
            Assert.DoesNotContain(order, path => Paths.Same(path, nested));
        }
    }

    [AvaloniaFact]
    public async Task Exit_RestoresThePreviousPlaylist()
    {
        using (var dir = new FolderDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var folder = dir.Folder("album");
            var files = new List<string>
            {
                dir.Png(dir.Root, "outside.png"),
                dir.Png(folder, "a.png"),
                dir.Png(folder, "b.png")
            };
            lib.ApplyLibrary(files, true, ImageLibrary.LibraryStageScan);
            lib.SetMode("Sequential");

            bool? ready = null;
            Assert.True(lib.EnterFolderPlay(folder, "Sequential", null, ok => ready = ok));
            Assert.True(await Wait(() => ready == true));
            Assert.Equal(2, lib.TotalImages);

            lib.ExitFolderPlay();
            Assert.False(lib.IsFolderPlay);
            Assert.Equal("Sequential", lib.CurrentMode);
            Assert.Equal(3, lib.SnapshotPaths().Count);
            Assert.Equal(3, lib.TotalImages);
        }
    }

    [AvaloniaFact]
    public async Task SecondEnter_CancelsTheFirstGeneration()
    {
        using (var dir = new FolderDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var slow = dir.Folder("slow");
            var fast = dir.Folder("fast");
            var slowFile = dir.Png(slow, "slow.png");
            var fastFiles = new List<string>
            {
                dir.Png(fast, "b.png"),
                dir.Png(fast, "a.png")
            };
            lib.ApplyLibrary(fastFiles, true, ImageLibrary.LibraryStageScan);

            bool? slowReady = null;
            bool? fastReady = null;
            Assert.True(lib.EnterFolderPlay(slow, "Sequential", null, ok => slowReady = ok));
            Assert.True(lib.EnterFolderPlay(fast, "Sequential", null, ok => fastReady = ok));
            Assert.True(await Wait(() => fastReady == true));
            await Task.Delay(150);

            Assert.True(lib.IsFolderPlay);
            Assert.EndsWith("a.png", lib.GetCurrentImagePath(), StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(slowFile, lib.GetCurrentImagePath());
            Assert.Equal(2, lib.TotalImages);
            Assert.False(slowReady == true && Paths.Same(lib.GetCurrentImagePath(), slowFile));
        }
    }

    [Fact]
    public void FolderIndex_ListsRealFoldersAndCrumbs()
    {
        using (var dir = new FolderDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var root = dir.Folder("library");
            dir.Folder("library", "beta");
            var alpha = dir.Folder("library", "alpha");
            dir.Folder("library", "alpha", "nested");
            var missing = Path.Combine(dir.Root, "gone");

            int total;
            var roots = lib.ListRoots(new[] { root, missing, "  " }, out total);
            Assert.Equal(0, total);
            Assert.Single(roots);
            Assert.Equal(root, roots[0].FullPath);
            Assert.True(roots[0].HasChildren);
            Assert.Equal(0, roots[0].FileCount);

            int direct;
            var children = lib.ListChildFolders(root, out total, out direct);
            Assert.Equal(new[] { "alpha", "beta" }, children.Select(item => item.Name).ToArray());
            Assert.True(children[0].HasChildren);
            Assert.False(children[1].HasChildren);

            var crumbs = lib.GetBrowseCrumbs(alpha);
            Assert.True(crumbs.Count >= 2);
            Assert.Equal("alpha", crumbs[crumbs.Count - 1].Name);
            Assert.True(crumbs[crumbs.Count - 1].IsSelected);
            Assert.Empty(lib.GetBrowseCrumbs("  "));
        }
    }

    private static List<string> Playlist(ImageLibrary lib)
    {
        var order = new List<string>();
        for (int i = 0; i < lib.TotalImages; i++)
        {
            lib.JumpTo(i);
            order.Add(lib.GetCurrentImagePath());
        }
        return order;
    }

    private static async Task<bool> Wait(Func<bool> done)
    {
        var start = DateTime.UtcNow;
        while (!done() && DateTime.UtcNow - start < TimeSpan.FromSeconds(8))
            await Task.Delay(20);
        return done();
    }

    private sealed class FolderDir : IDisposable
    {
        internal readonly string Root;

        internal FolderDir()
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "reelwalk-folder-" + Path.GetRandomFileName())).FullName;
        }

        internal string Folder(params string[] parts)
        {
            var path = Root;
            for (int i = 0; i < parts.Length; i++)
                path = Path.Combine(path, parts[i]);
            Directory.CreateDirectory(path);
            return path;
        }

        internal string Png(string folder, string name)
        {
            var path = Path.Combine(folder, name);
            SampleFiles.WritePng(path);
            return path;
        }

        internal string Bytes(string folder, string name, int length)
        {
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, new byte[length]);
            return path;
        }

        internal string Mp4(string folder, string name, int durationMs)
        {
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, SampleMp4(durationMs));
            return path;
        }

        private static byte[] SampleMp4(int durationMs)
        {
            var mvhd = new byte[32];
            WriteBe(mvhd, 0, (uint)mvhd.Length);
            mvhd[4] = (byte)'m';
            mvhd[5] = (byte)'v';
            mvhd[6] = (byte)'h';
            mvhd[7] = (byte)'d';
            WriteBe(mvhd, 20, 1000);
            WriteBe(mvhd, 24, (uint)durationMs);

            var moov = new byte[8 + mvhd.Length];
            WriteBe(moov, 0, (uint)moov.Length);
            moov[4] = (byte)'m';
            moov[5] = (byte)'o';
            moov[6] = (byte)'o';
            moov[7] = (byte)'v';
            Buffer.BlockCopy(mvhd, 0, moov, 8, mvhd.Length);
            return moov;
        }

        private static void WriteBe(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }

        public void Dispose()
        {
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    if (Directory.Exists(Root))
                        Directory.Delete(Root, true);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
