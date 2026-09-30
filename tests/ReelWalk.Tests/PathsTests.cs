using System.IO;
using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class PathsTests
{
    [Fact]
    public void Normalize_DropsQuotesAndTrailingSlash()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "reelwalk-paths-" + Path.GetRandomFileName())).FullName;
        try
        {
            var quoted = "\"" + folder + Path.DirectorySeparatorChar + "\"";
            Assert.Equal(folder.TrimEnd('\\', '/'), Paths.Normalize(quoted));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankIsNull(string path)
    {
        Assert.Null(Paths.Normalize(path));
    }

    [Fact]
    public void Same_IgnoresCaseAndRejectsBlank()
    {
        Assert.True(Paths.Same(@"C:\Photos\A.jpg", @"c:\photos\a.jpg"));
        Assert.False(Paths.Same("", @"C:\Photos"));
        Assert.False(Paths.Same(null, null));
    }

    [Fact]
    public void Parent_OfDriveRootIsNull()
    {
        Assert.Null(Paths.Parent(@"C:\"));
        Assert.Null(Paths.Parent(""));
    }

    [Fact]
    public void Parent_AndLeaf_SplitAFile()
    {
        Assert.Equal("shot.jpg", Paths.Leaf(@"C:\Photos\shot.jpg"));
        Assert.EndsWith("Photos", Paths.Parent(@"C:\Photos\shot.jpg"));
    }

    [Fact]
    public void IsInside_DoesNotTreatAPrefixNameAsAChild()
    {
        Assert.False(Paths.IsInside(@"C:\foobar\a.jpg", @"C:\foo", false));
        Assert.True(Paths.IsInside(@"C:\foo\bar\a.jpg", @"C:\foo", false));
        Assert.True(Paths.IsInside(@"C:\foo", @"C:\foo", true));
        Assert.False(Paths.IsInside(@"C:\foo", @"C:\foo", false));
        Assert.False(Paths.IsInside("", @"C:\foo", true));
    }

    [Fact]
    public void IsInside_AcceptsMixedSeparators()
    {
        Assert.True(Paths.IsInside(@"C:\foo/bar\a.jpg", @"C:/foo", false));
    }

    [Fact]
    public void Combine_JoinsAndRejectsBlank()
    {
        var combined = Paths.Combine(@"C:\Photos\", "Trip");
        Assert.EndsWith(Path.Combine("Photos", "Trip"), combined);
        Assert.Null(Paths.Combine(@"C:\Photos", ""));
        Assert.Null(Paths.Combine("", "Trip"));
    }

    [Fact]
    public void FolderPrefix_AddsASeparator()
    {
        var prefix = Paths.FolderPrefix(@"C:\Photos\");
        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), prefix);
        Assert.Null(Paths.FolderPrefix(""));
    }
}
