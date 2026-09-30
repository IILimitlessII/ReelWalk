using System;
using System.IO;
using System.Text;

namespace ReelWalk.Tests;
internal static class ExifSamples
{
    internal static byte[] JpegWithDate(string date)
    {
        var tiff = TiffWithDate(date);
        var payload = new byte[6 + tiff.Length];
        Encoding.ASCII.GetBytes("Exif").CopyTo(payload, 0);
        tiff.CopyTo(payload, 6);
        int length = payload.Length + 2;
        var jpeg = new byte[4 + 2 + payload.Length];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        jpeg[2] = 0xFF;
        jpeg[3] = 0xE1;
        jpeg[4] = (byte)(length >> 8);
        jpeg[5] = (byte)length;
        payload.CopyTo(jpeg, 6);
        return jpeg;
    }

    internal static byte[] TiffWithDate(string date)
    {
        var text = Encoding.ASCII.GetBytes(date + "\0");
        if (text.Length != 20)
            throw new InvalidOperationException("EXIF date must be 19 characters.");

        int ifd0 = 8;
        int exifIfd = 26;
        int textAt = 44;
        var data = new byte[textAt + text.Length];
        data[0] = (byte)'I';
        data[1] = (byte)'I';
        data[2] = 42;
        data[4] = (byte)ifd0;

        data[ifd0] = 1;
        WriteU16(data, ifd0 + 2, 0x8769);
        WriteU16(data, ifd0 + 4, 4);
        WriteU32(data, ifd0 + 6, 1);
        WriteU32(data, ifd0 + 10, (uint)exifIfd);

        data[exifIfd] = 1;
        WriteU16(data, exifIfd + 2, 0x9003);
        WriteU16(data, exifIfd + 4, 2);
        WriteU32(data, exifIfd + 6, 20);
        WriteU32(data, exifIfd + 10, (uint)textAt);
        text.CopyTo(data, textAt);
        return data;
    }

    internal static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteU16(byte[] data, int offset, int value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
