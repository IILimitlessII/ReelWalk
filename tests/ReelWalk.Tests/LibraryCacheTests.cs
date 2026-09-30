using System.Collections.Generic;
using System.IO;
using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class LibraryCacheTests
{
    [Fact]
    public void SaveLoad_RoundTripsPaths()
    {
        var path = TempFile();
        try
        {
            LibraryCache.Save(path, new List<string> { @"C:\a.png", @"D:\b.mp4" });
            var loaded = LibraryCache.Load(path);
            Assert.Equal(new[] { @"C:\a.png", @"D:\b.mp4" }, loaded);
            Assert.True(LibraryCache.HasEntries(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void HasEntries_IsTrueFromTheHeaderAndOnePath()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "reelwalk-library-v1\r\nC:\\one.png\r\n");
            Assert.True(LibraryCache.HasEntries(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Load_SkipsBlankLines_AndRejectsABadHeader()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "reelwalk-library-v1\n\nC:\\a.png\n\n");
            Assert.Equal(new[] { @"C:\a.png" }, LibraryCache.Load(path));

            File.WriteAllText(path, "not-a-library\nC:\\a.png\n");
            Assert.Null(LibraryCache.Load(path));
            Assert.False(LibraryCache.HasEntries(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void MissingFile_IsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "reelwalk-missing-" + Path.GetRandomFileName());
        Assert.Null(LibraryCache.Load(path));
        Assert.False(LibraryCache.HasEntries(path));
        Assert.False(LibraryCache.HasEntries(""));
        LibraryCache.Save("", new List<string> { "a" });
        LibraryCache.Save(path, null);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void HeaderOnly_HasNoEntries()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "reelwalk-library-v1\n");
            Assert.False(LibraryCache.HasEntries(path));
            Assert.Null(LibraryCache.Load(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void MergeNew_AppendsUnknownPaths_AndSkipsDuplicates()
    {
        var cached = new List<string> { @"C:\a.png" };
        Assert.Null(LibraryCache.MergeNew(cached, new List<string> { @"c:\a.png" }));
        Assert.Null(LibraryCache.MergeNew(cached, new List<string>()));
        Assert.Null(LibraryCache.MergeNew(cached, null));

        var merged = LibraryCache.MergeNew(cached, new List<string> { @"C:\a.png", @"C:\b.png" });
        Assert.Equal(new[] { @"C:\a.png", @"C:\b.png" }, merged);
        Assert.Equal(new[] { @"C:\only.png" }, LibraryCache.MergeNew(null, new List<string> { @"C:\only.png" }));
    }

    private static string TempFile()
    {
        return Path.Combine(Path.GetTempPath(), "reelwalk-cache-" + Path.GetRandomFileName());
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        var tmp = path + ".tmp";
        if (File.Exists(tmp))
            File.Delete(tmp);
    }
}
