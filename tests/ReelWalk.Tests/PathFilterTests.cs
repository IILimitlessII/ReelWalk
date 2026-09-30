using System.Collections.Generic;
using System.IO;
using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class PathFilterTests
{
    [Fact]
    public void Empty_BlocksNothing()
    {
        var filter = PathFilter.From(null);
        Assert.True(filter.IsEmpty);
        Assert.False(filter.Blocks(@"C:\Photos\a.jpg"));
        Assert.False(PathFilter.From(new List<string>()).Blocks(@"C:\Photos"));
    }

    [Fact]
    public void Blocks_TheFolderItselfAndChildren_NotAPrefixSibling()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "reelwalk-filter-" + Path.GetRandomFileName())).FullName;
        var skip = Path.Combine(root, "Skip");
        var sibling = Path.Combine(root, "SkipExtra");
        Directory.CreateDirectory(skip);
        Directory.CreateDirectory(sibling);
        try
        {
            var filter = PathFilter.From(new[] { skip + Path.DirectorySeparatorChar });
            Assert.True(filter.Blocks(skip));
            Assert.True(filter.Blocks(Path.Combine(skip, "a.jpg")));
            Assert.False(filter.Blocks(Path.Combine(sibling, "a.jpg")));
            Assert.False(filter.Blocks(Path.Combine(root, "keep.jpg")));
            Assert.False(filter.Blocks(""));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void From_DropsBlankAndDuplicateRoots()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "reelwalk-filter2-" + Path.GetRandomFileName())).FullName;
        try
        {
            var filter = PathFilter.From(new[] { root, root.ToUpperInvariant(), "  ", null });
            Assert.False(filter.IsEmpty);
            Assert.True(filter.Blocks(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
