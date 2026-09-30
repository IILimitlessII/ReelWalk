using System;
using System.IO;
using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class ExifDatesTests
{
    [Fact]
    public void Jpeg_ReadsDateTimeOriginal()
    {
        var path = Path.Combine(Path.GetTempPath(), "reelwalk-exif-" + Path.GetRandomFileName() + ".jpg");
        try
        {
            ExifSamples.Write(path, ExifSamples.JpegWithDate("2020:01:02 03:04:05"));
            var taken = ExifDates.TryRead(path);
            Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5), taken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Tiff_ReadsLittleEndianDate()
    {
        var path = Path.Combine(Path.GetTempPath(), "reelwalk-exif-" + Path.GetRandomFileName() + ".tif");
        try
        {
            ExifSamples.Write(path, ExifSamples.TiffWithDate("2019:12:31 23:59:58"));
            Assert.Equal(new DateTime(2019, 12, 31, 23, 59, 58), ExifDates.TryRead(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void JpegWithoutADate_Video_AndTruncated_ReturnNull()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "reelwalk-exif-" + Path.GetRandomFileName())).FullName;
        try
        {
            var plain = Path.Combine(dir, "plain.jpg");
            File.WriteAllBytes(plain, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
            Assert.Null(ExifDates.TryRead(plain));

            var clip = Path.Combine(dir, "clip.mp4");
            File.WriteAllBytes(clip, new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 });
            Assert.Null(ExifDates.TryRead(clip));

            var cut = Path.Combine(dir, "cut.jpg");
            File.WriteAllBytes(cut, new byte[] { 0xFF, 0xD8, 0x00 });
            Assert.Null(ExifDates.TryRead(cut));
            Assert.Null(ExifDates.TryRead(""));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void YearBefore1970_IsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "reelwalk-exif-" + Path.GetRandomFileName() + ".jpg");
        try
        {
            ExifSamples.Write(path, ExifSamples.JpegWithDate("1969:12:31 23:59:59"));
            Assert.Null(ExifDates.TryRead(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
