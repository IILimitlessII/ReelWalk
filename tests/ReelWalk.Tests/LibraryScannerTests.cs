using System.Collections.Generic;
using System.IO;
using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class LibraryScannerTests
{
    [Fact]
    public void Scan_SkipsNestedIgnore_AndUnsupportedFiles()
    {
        var root = NewRoot();
        try
        {
            var keep = Path.Combine(root, "keep");
            var skip = Path.Combine(root, "skip");
            SampleFiles.WritePng(Path.Combine(keep, "a.png"));
            SampleFiles.WritePng(Path.Combine(skip, "nested", "b.png"));
            File.WriteAllText(Path.Combine(keep, "notes.txt"), "no");

            var found = LibraryScanner.Scan(new[] { root }, new[] { skip }, null, null);

            Assert.Single(found);
            Assert.EndsWith("a.png", found[0]);
            Assert.True(LibraryScanner.IsIgnored(Path.Combine(skip, "nested", "b.png"), new[] { skip }));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_EmptyRoots_ReturnsAnEmptyList()
    {
        var found = LibraryScanner.Scan(new List<string>(), null, null, null);
        Assert.NotNull(found);
        Assert.Empty(found);
        Assert.Empty(LibraryScanner.Scan(null, null, null, null));
    }

    [Fact]
    public void Scan_WhileOneIsRunning_ReturnsNull()
    {
        var root = NewRoot();
        try
        {
            SampleFiles.WritePng(Path.Combine(root, "a.png"));
            List<string> nested = new List<string>();
            var found = LibraryScanner.Scan(new[] { root }, null, null, batch =>
            {
                nested = LibraryScanner.Scan(new[] { root }, null, null, null);
            });

            Assert.Null(nested);
            Assert.Single(found);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FilterIgnored_DropsChildren_AndCopiesWhenNothingIsIgnored()
    {
        var files = new List<string> { @"C:\lib\a.png", @"C:\lib\skip\b.png", @"C:\lib\skipExtra\c.png" };
        var kept = LibraryScanner.FilterIgnored(files, new[] { @"C:\lib\skip" });
        Assert.Equal(2, kept.Count);
        Assert.DoesNotContain(kept, path => path.Contains(@"skip\b"));

        var same = LibraryScanner.FilterIgnored(files, null);
        Assert.Same(files, same);
        Assert.Empty(LibraryScanner.FilterIgnored(null, new[] { @"C:\lib" }));
    }

    private static string NewRoot()
    {
        return Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "reelwalk-scan-" + Path.GetRandomFileName())).FullName;
    }
}
