using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ReelWalk.Models;
using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class ImageLibraryTests
{
    [Fact]
    public void Sequential_SortsByName()
    {
        using (var dir = new LibraryDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var files = dir.Add("c.png", "a.png", "b.png");
            lib.ApplyLibrary(files, true, ImageLibrary.LibraryStageScan);

            Assert.Equal("a.png", Path.GetFileName(lib.GetCurrentImagePath()));
            Assert.Equal(new[] { "a.png", "b.png", "c.png" }, Playlist(lib).Select(Path.GetFileName).ToArray());
        }
    }

    [Fact]
    public void Random_IsAPermutationOfTheSameFiles()
    {
        using (var dir = new LibraryDir())
        using (var lib = new ImageLibrary("Random", 0, 1))
        {
            var files = dir.Add("a.png", "b.png", "c.png", "d.png");
            lib.ApplyLibrary(files, true, ImageLibrary.LibraryStageScan);

            var order = Playlist(lib);
            Assert.Equal(files.Count, order.Count);
            Assert.Equal(files.Count, order.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(files, path => Assert.Contains(order, item => Paths.Same(item, path)));
        }
    }

    [Fact]
    public void NewestAndOldest_FollowFileTimes()
    {
        using (var dir = new LibraryDir())
        using (var lib = new ImageLibrary("NewestFirst", 0, 1))
        {
            var old = dir.Add("old.png")[0];
            var mid = dir.Add("mid.png")[0];
            var newest = dir.Add("new.png")[0];
            File.SetLastWriteTime(old, new DateTime(2020, 1, 1));
            File.SetLastWriteTime(mid, new DateTime(2021, 6, 1));
            File.SetLastWriteTime(newest, new DateTime(2024, 3, 1));

            lib.ApplyLibrary(new List<string> { mid, old, newest }, true, ImageLibrary.LibraryStageScan);
            Assert.True(Paths.Same(newest, lib.GetCurrentImagePath()));
            Assert.Equal("new.png", Path.GetFileName(Playlist(lib)[0]));

            lib.JumpTo(0);
            lib.SetMode("OldestFirst");
            Assert.True(Paths.Same(newest, lib.GetCurrentImagePath()));
            Assert.Equal("old.png", Path.GetFileName(Playlist(lib)[0]));
        }
    }

    [Fact]
    public void ShowFilter_RefusesAChoiceWithNoFiles()
    {
        using (var dir = new LibraryDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            lib.ApplyLibrary(dir.Add("a.png", "b.png"), true, ImageLibrary.LibraryStageScan);
            Assert.False(lib.TrySetMediaShow("Videos"));
            Assert.Equal("Both", lib.MediaShow);

            var clip = dir.AddEmpty("clip.mp4")[0];
            lib.AbsorbFiles(new[] { clip }, true);
            Assert.True(lib.TrySetMediaShow("Videos"));
            Assert.True(MediaTypes.IsVideo(lib.GetCurrentImagePath()));
            Assert.True(lib.TrySetMediaShow("Images"));
            Assert.False(MediaTypes.IsVideo(lib.GetCurrentImagePath()));
        }
    }

    [Fact]
    public void Absorb_DoesNotDuplicatePaths()
    {
        using (var dir = new LibraryDir())
        using (var lib = new ImageLibrary("Random", 0, 1))
        {
            var files = dir.Add("a.png", "b.png");
            lib.ApplyLibrary(files, true, ImageLibrary.LibraryStageScan);
            Assert.Equal(LibraryUpdate.Unchanged, lib.AbsorbFiles(files, false));
            Assert.Equal(2, lib.SnapshotPaths().Count);

            var extra = dir.Add("c.png");
            Assert.Equal(LibraryUpdate.Updated, lib.AbsorbFiles(extra, false));
            Assert.Equal(3, lib.SnapshotPaths().Count);
            Assert.Equal(LibraryUpdate.Unchanged, lib.AbsorbFiles(extra, false));
        }
    }

    [Fact]
    public void RemoveCurrent_LandsOnTheFileThatFollowed()
    {
        using (var dir = new LibraryDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var files = dir.Add("a.png", "b.png", "c.png");
            lib.ApplyLibrary(files, true, ImageLibrary.LibraryStageScan);
            lib.SetMode("Sequential");
            var ordered = Playlist(lib);
            lib.JumpTo(1);
            var follower = ordered[2];
            lib.RemoveCurrent();
            Assert.True(Paths.Same(follower, lib.GetCurrentImagePath()));
            Assert.Equal(2, lib.TotalImages);
        }
    }

    [Fact]
    public void RandomBack_ReturnsToTheFileJustLeft()
    {
        using (var dir = new LibraryDir())
        using (var lib = new ImageLibrary("Random", 0, 1))
        {
            lib.ApplyLibrary(dir.Add("a.png", "b.png", "c.png", "d.png"), true, ImageLibrary.LibraryStageScan);
            lib.SetBackHistory(10);
            lib.SetMode("Random");
            var before = lib.GetCurrentImagePath();
            lib.Next();
            lib.Previous();
            Assert.True(Paths.Same(before, lib.GetCurrentImagePath()));
        }
    }

    [Fact]
    public void OlderStage_DoesNotReplaceAFinishedScan()
    {
        using (var dir = new LibraryDir())
        using (var lib = new ImageLibrary("Sequential", 0, 1))
        {
            var scanned = dir.Add("a.png", "b.png", "c.png");
            lib.ApplyLibrary(scanned, true, ImageLibrary.LibraryStageScan);
            var older = dir.Add("only.png");
            Assert.Equal(LibraryUpdate.Unchanged, lib.ApplyLibrary(older, true, ImageLibrary.LibraryStageResume));
            Assert.Equal(3, lib.SnapshotPaths().Count);
            Assert.Contains(lib.SnapshotPaths(), path => path.EndsWith("a.png"));
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

    private sealed class LibraryDir : IDisposable
    {
        private readonly string root;

        internal LibraryDir()
        {
            root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "reelwalk-lib-" + Path.GetRandomFileName())).FullName;
        }

        internal List<string> Add(params string[] names)
        {
            var paths = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                var path = Path.Combine(root, names[i]);
                SampleFiles.WritePng(path);
                paths.Add(path);
            }
            return paths;
        }

        internal List<string> AddEmpty(params string[] names)
        {
            var paths = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                var path = Path.Combine(root, names[i]);
                SampleFiles.WriteEmpty(path);
                paths.Add(path);
            }
            return paths;
        }

        public void Dispose()
        {
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                        Directory.Delete(root, true);
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
