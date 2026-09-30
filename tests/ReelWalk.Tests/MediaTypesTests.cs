using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class MediaTypesTests
{
    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".bmp")]
    [InlineData(".gif")]
    [InlineData(".tif")]
    [InlineData(".tiff")]
    [InlineData(".wdp")]
    [InlineData(".jxr")]
    [InlineData(".ico")]
    [InlineData(".webp")]
    [InlineData(".heic")]
    [InlineData(".heif")]
    [InlineData(".avif")]
    public void Images_AreSupportedAndNotVideo(string ext)
    {
        var path = @"C:\p\shot" + ext.ToUpperInvariant();
        Assert.True(MediaTypes.IsSupported(path));
        Assert.False(MediaTypes.IsVideo(path));
        Assert.True(MediaTypes.Allows(path, "Images"));
        Assert.False(MediaTypes.Allows(path, "Videos"));
        Assert.True(MediaTypes.Allows(path, "Both"));
    }

    [Theory]
    [InlineData(".mp4")]
    [InlineData(".wmv")]
    [InlineData(".avi")]
    [InlineData(".mov")]
    [InlineData(".m4v")]
    [InlineData(".mkv")]
    [InlineData(".webm")]
    public void Videos_AreSupported(string ext)
    {
        var path = @"C:\p\clip" + ext;
        Assert.True(MediaTypes.IsVideo(path));
        Assert.True(MediaTypes.Allows(path, "Videos"));
        Assert.False(MediaTypes.Allows(path, "Images"));
        Assert.False(MediaTypes.Allows(path, "photos"));
        Assert.True(MediaTypes.Allows(path, "Both"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\p\notes.txt")]
    [InlineData(@"C:\p\archive.zip")]
    public void Unsupported_AndEmpty_AreRejected(string path)
    {
        Assert.False(MediaTypes.IsSupported(path));
        Assert.False(MediaTypes.IsVideo(path));
        Assert.False(MediaTypes.Allows(path, "Both"));
    }

    [Theory]
    [InlineData(null, "Both")]
    [InlineData("", "Both")]
    [InlineData("whatever", "Both")]
    [InlineData("Image", "Images")]
    [InlineData("Photos", "Images")]
    [InlineData("Photo", "Images")]
    [InlineData("Pictures", "Images")]
    [InlineData("Video", "Videos")]
    [InlineData("videos", "Videos")]
    public void NormalizeShow_MapsAliases(string raw, string expected)
    {
        Assert.Equal(expected, MediaTypes.NormalizeShow(raw));
    }

    [Fact]
    public void ShowLabel_UsesTheWordsFromTheToast()
    {
        Assert.Equal("Photos only", MediaTypes.ShowLabel("Pictures"));
        Assert.Equal("Videos only", MediaTypes.ShowLabel("Video"));
        Assert.Equal("Photos and videos", MediaTypes.ShowLabel("nope"));
    }
}
