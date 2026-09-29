using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ReelWalk.Services;
internal static class ExifDates
{
    // Date the photo was taken, from the EXIF header.
    // path is a file. Returns null for videos and files with no date tag.
    internal static DateTime? TryRead(string path)
    {
        if (string.IsNullOrEmpty(path) || MediaTypes.IsVideo(path))
            return null;
        try
        {
            using (var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan))
            {
                if (stream.Length < 8)
                    return null;
                int b0 = stream.ReadByte();
                int b1 = stream.ReadByte();
                if (b0 == 0xFF && b1 == 0xD8)
                    return ReadJpeg(stream);
                if ((b0 == 'I' && b1 == 'I') || (b0 == 'M' && b1 == 'M'))
                {
                    stream.Position = 0;
                    var data = ReadPrefix(stream, 256 * 1024);
                    return ReadTiffDate(data, 0);
                }
            }
        }
        catch
        {
        }
        return null;
    }

    // DateTimeOriginal inside a JPEG APP1 segment.
    // stream is positioned after the SOI marker. Returns null when that segment is missing.
    private static DateTime? ReadJpeg(FileStream stream)
    {
        var pair = new byte[2];
        while (stream.Position + 4 < stream.Length && stream.Position < 1024 * 1024)
        {
            if (stream.Read(pair, 0, 2) < 2 || pair[0] != 0xFF)
                return null;
            int marker = pair[1];
            while (marker == 0xFF)
            {
                int extra = stream.ReadByte();
                if (extra < 0)
                    return null;
                marker = extra;
            }
            if (marker == 0xD9 || marker == 0xDA)
                return null;
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                continue;
            if (stream.Read(pair, 0, 2) < 2)
                return null;
            int len = (pair[0] << 8) | pair[1];
            if (len < 2)
                return null;
            int dataLen = len - 2;
            if (marker == 0xE1 && dataLen >= 14 && dataLen <= 1024 * 1024)
            {
                var data = new byte[dataLen];
                if (ReadFull(stream, data) != dataLen)
                    return null;
                if (data[0] == (byte)'E' && data[1] == (byte)'x' && data[2] == (byte)'i' &&
                    data[3] == (byte)'f' && data[4] == 0 && data[5] == 0)
                {
                    var taken = ReadTiffDate(data, 6);
                    if (taken.HasValue)
                        return taken;
                }
                continue;
            }
            stream.Seek(dataLen, SeekOrigin.Current);
        }
        return null;
    }

    // Best EXIF date in a TIFF block.
    // tiffStart is the byte-order mark. Returns null when the block has no date.
    private static DateTime? ReadTiffDate(byte[] data, int tiffStart)
    {
        if (data == null || tiffStart < 0 || tiffStart + 8 > data.Length)
            return null;
        bool le = data[tiffStart] == (byte)'I' && data[tiffStart + 1] == (byte)'I';
        bool be = data[tiffStart] == (byte)'M' && data[tiffStart + 1] == (byte)'M';
        if (!le && !be)
            return null;
        if (U16(data, tiffStart + 2, le) != 42)
            return null;
        int ifd = (int)U32(data, tiffStart + 4, le);
        int exifIfd;
        DateTime? ifd0 = WalkIfd(data, tiffStart, ifd, le, true, out exifIfd);
        if (exifIfd < 0)
            return ifd0;
        int ignored;
        DateTime? exif = WalkIfd(data, tiffStart, exifIfd, le, false, out ignored);
        return exif ?? ifd0;
    }

    // Walks one IFD.
    // followExif is true on IFD0 so exifIfd receives the Exif pointer. Returns the best date in that IFD, or null.
    private static DateTime? WalkIfd(byte[] data, int tiffStart, int ifdOffset, bool le, bool followExif, out int exifIfd)
    {
        exifIfd = -1;
        int pos = tiffStart + ifdOffset;
        if (ifdOffset < 0 || pos < 0 || pos + 2 > data.Length)
            return null;
        int count = U16(data, pos, le);
        pos += 2;
        DateTime? best = null;
        int rank = 0;
        for (int i = 0; i < count; i++)
        {
            int entry = pos + i * 12;
            if (entry + 12 > data.Length)
                break;
            int tag = U16(data, entry, le);
            int type = U16(data, entry + 2, le);
            int n = (int)U32(data, entry + 4, le);
            if (followExif && tag == 0x8769)
                exifIfd = (int)U32(data, entry + 8, le);
            int tagRank = tag == 0x9003 ? 3 : tag == 0x9004 ? 2 : tag == 0x0132 ? 1 : 0;
            if (tagRank == 0 || tagRank <= rank || type != 2 || n < 19)
                continue;
            var text = ReadAscii(data, tiffStart, entry + 8, n, le);
            var parsed = ParseExifDate(text);
            if (!parsed.HasValue)
                continue;
            best = parsed;
            rank = tagRank;
        }
        return best;
    }

    // Reads an ASCII TIFF field.
    // count includes the trailing null. Returns null when the offset is outside data.
    private static string ReadAscii(byte[] data, int tiffStart, int valueField, int count, bool le)
    {
        int offset = count <= 4 ? valueField : tiffStart + (int)U32(data, valueField, le);
        if (offset < 0 || offset >= data.Length)
            return null;
        int len = Math.Min(count, data.Length - offset);
        int end = offset;
        int max = offset + len;
        while (end < max && data[end] != 0)
            end++;
        if (end == offset)
            return null;
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    // Parses yyyy:MM:dd HH:mm:ss.
    // text is the EXIF string. Returns null when it is not that form, or the year is before 1970.
    private static DateTime? ParseExifDate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        DateTime parsed;
        if (!DateTime.TryParseExact(
            text.Trim(),
            "yyyy:MM:dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed))
            return null;
        if (parsed.Year < 1970)
            return null;
        return parsed;
    }

    // Reads up to max bytes from the current position.
    // Returns a shorter array when the stream ends first.
    private static byte[] ReadPrefix(Stream stream, int max)
    {
        int size = (int)Math.Min(max, stream.Length - stream.Position);
        if (size <= 0)
            return new byte[0];
        var data = new byte[size];
        ReadFull(stream, data);
        return data;
    }

    // Fills buffer from stream.
    // Returns how many bytes were read.
    private static int ReadFull(Stream stream, byte[] buffer)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            int n = stream.Read(buffer, got, buffer.Length - got);
            if (n <= 0)
                break;
            got += n;
        }
        return got;
    }

    // Unsigned 16-bit value.
    // le selects byte order. Returns 0 when offset is outside data.
    private static int U16(byte[] data, int offset, bool le)
    {
        if (offset < 0 || offset + 2 > data.Length)
            return 0;
        if (le)
            return data[offset] | (data[offset + 1] << 8);
        return (data[offset] << 8) | data[offset + 1];
    }

    // Unsigned 32-bit value.
    // le selects byte order. Returns 0 when offset is outside data.
    private static uint U32(byte[] data, int offset, bool le)
    {
        if (offset < 0 || offset + 4 > data.Length)
            return 0;
        if (le)
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
    }
}
