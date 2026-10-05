using System;
using System.IO;
using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class VideoDurationTests
{
    [Fact]
    public void Mp4_ReadsMvhdDuration()
    {
        var path = Path.Combine(Path.GetTempPath(), "reelwalk-dur-" + Path.GetRandomFileName() + ".mp4");
        try
        {
            File.WriteAllBytes(path, Box("moov", Box("mvhd", Mvhd(1000, 2500))));
            Assert.Equal(2500, VideoDuration.Milliseconds(path));
            Assert.Equal(-1, VideoDuration.Milliseconds(Path.ChangeExtension(path, ".png")));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Mp4_UsesTheVideoTrackWhenTheMovieHeaderIsEmpty()
    {
        var path = Temp(".mp4");
        try
        {
            var hdlr = new byte[12];
            hdlr[8] = (byte)'v';
            hdlr[9] = (byte)'i';
            hdlr[10] = (byte)'d';
            hdlr[11] = (byte)'e';
            var mdia = Box("mdia", Concat(Box("mdhd", Mvhd(1000, 4500)), Box("hdlr", hdlr)));
            var moov = Box("moov", Concat(Box("mvhd", Mvhd(1000, 0)), Box("trak", mdia)));
            var mdat = Box("mdat", new byte[32]);
            File.WriteAllBytes(path, Concat(mdat, moov));
            Assert.Equal(4500, VideoDuration.Milliseconds(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Mkv_ReadsInfoDuration()
    {
        var path = Temp(".mkv");
        try
        {
            File.WriteAllBytes(path, Mkv(4000f, 1000000));
            Assert.Equal(4000, VideoDuration.Milliseconds(path));
            Assert.Equal(4000, VideoDuration.Milliseconds(path));
            File.WriteAllBytes(path, MkvSeekingPastCluster(4000f, 1000000));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
            Assert.Equal(4000, VideoDuration.Milliseconds(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Avi_ReadsStreamLengthWhenTheFrameCountIsEmpty()
    {
        var path = Temp(".avi");
        try
        {
            File.WriteAllBytes(path, Avi(scale: 1, rate: 25, length: 50));
            Assert.Equal(2000, VideoDuration.Milliseconds(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Wmv_ReadsPlayDuration()
    {
        var path = Temp(".wmv");
        try
        {
            File.WriteAllBytes(path, Wmv(TimeSpan.FromSeconds(2)));
            Assert.Equal(2000, VideoDuration.Milliseconds(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void FolderOrder_KeepsKnownNames()
    {
        Assert.Equal("Random", FolderOrder.Normalize("nope"));
        Assert.Equal("Sequential", FolderOrder.Normalize("Ordered"));
        Assert.Equal("SizeAsc", FolderOrder.Normalize("Smallest"));
        Assert.Equal("SizeDesc", FolderOrder.Normalize("largest"));
        Assert.Equal("LengthAsc", FolderOrder.Normalize("Shortest"));
        Assert.Equal("LengthDesc", FolderOrder.Normalize("Longest"));
        Assert.Equal("Shortest", FolderOrder.Label("LengthAsc"));
    }

    private static byte[] Mvhd(uint timescale, uint durationMs)
    {
        var body = new byte[20];
        WriteBe(body, 12, timescale);
        WriteBe(body, 16, durationMs);
        return body;
    }

    private static byte[] Box(string name, byte[] body)
    {
        var box = new byte[8 + body.Length];
        WriteBe(box, 0, (uint)box.Length);
        box[4] = (byte)name[0];
        box[5] = (byte)name[1];
        box[6] = (byte)name[2];
        box[7] = (byte)name[3];
        System.Buffer.BlockCopy(body, 0, box, 8, body.Length);
        return box;
    }

    private static void WriteBe(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static byte[] Concat(byte[] left, byte[] right)
    {
        var all = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, all, 0, left.Length);
        Buffer.BlockCopy(right, 0, all, left.Length, right.Length);
        return all;
    }

    private static byte[] Mkv(float duration, uint scale)
    {
        var segment = Element(new byte[] { 0x18, 0x53, 0x80, 0x67 }, MkvInfo(duration, scale));
        var header = Element(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, new byte[4]);
        return Concat(header, segment);
    }

    private static byte[] MkvSeekingPastCluster(float duration, uint scale)
    {
        var info = MkvInfo(duration, scale);
        var cluster = Element(new byte[] { 0x1F, 0x43, 0xB6, 0x75 }, new byte[8]);
        var seekBody = Concat(
            Element(new byte[] { 0x53, 0xAB }, new byte[] { 0x15, 0x49, 0xA9, 0x66 }),
            Element(new byte[] { 0x53, 0xAC }, new byte[] { 0 }));
        var seekHead = Element(new byte[] { 0x11, 0x4D, 0x9B, 0x74 }, Element(new byte[] { 0x4D, 0xBB }, seekBody));
        int position = seekHead.Length + cluster.Length;
        seekBody = Concat(
            Element(new byte[] { 0x53, 0xAB }, new byte[] { 0x15, 0x49, 0xA9, 0x66 }),
            Element(new byte[] { 0x53, 0xAC }, new byte[] { (byte)position }));
        seekHead = Element(new byte[] { 0x11, 0x4D, 0x9B, 0x74 }, Element(new byte[] { 0x4D, 0xBB }, seekBody));
        if (seekHead.Length + cluster.Length != position)
            throw new InvalidOperationException("Seek position no longer matches the header.");
        var segment = Element(new byte[] { 0x18, 0x53, 0x80, 0x67 }, Concat(Concat(seekHead, cluster), info));
        return Concat(Element(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, new byte[4]), segment);
    }

    private static byte[] MkvInfo(float duration, uint scale)
    {
        var scaleBits = BitConverter.GetBytes(scale);
        var durationBits = BitConverter.GetBytes(duration);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(scaleBits);
            Array.Reverse(durationBits);
        }
        return Element(new byte[] { 0x15, 0x49, 0xA9, 0x66 }, Concat(
            Element(new byte[] { 0x2A, 0xD7, 0xB1 }, scaleBits),
            Element(new byte[] { 0x44, 0x89 }, durationBits)));
    }

    private static byte[] Element(byte[] id, byte[] payload)
    {
        var all = new byte[id.Length + 1 + payload.Length];
        Buffer.BlockCopy(id, 0, all, 0, id.Length);
        all[id.Length] = (byte)(0x80 | payload.Length);
        Buffer.BlockCopy(payload, 0, all, id.Length + 1, payload.Length);
        return all;
    }

    private static byte[] Avi(uint scale, uint rate, uint length)
    {
        var strh = new byte[8 + 48];
        strh[0] = (byte)'s';
        strh[1] = (byte)'t';
        strh[2] = (byte)'r';
        strh[3] = (byte)'h';
        WriteLe(strh, 4, 48);
        strh[8] = (byte)'v';
        strh[9] = (byte)'i';
        strh[10] = (byte)'d';
        strh[11] = (byte)'s';
        WriteLe(strh, 8 + 20, scale);
        WriteLe(strh, 8 + 24, rate);
        WriteLe(strh, 8 + 32, length);

        var body = strh;
        var file = new byte[12 + body.Length];
        file[0] = (byte)'R';
        file[1] = (byte)'I';
        file[2] = (byte)'F';
        file[3] = (byte)'F';
        WriteLe(file, 4, (uint)(file.Length - 8));
        file[8] = (byte)'A';
        file[9] = (byte)'V';
        file[10] = (byte)'I';
        file[11] = (byte)' ';
        Buffer.BlockCopy(body, 0, file, 12, body.Length);
        return file;
    }

    private static byte[] Wmv(TimeSpan play)
    {
        var props = new byte[104];
        Buffer.BlockCopy(new byte[]
        {
            0xA1, 0xDC, 0xAB, 0x8C, 0xA9, 0x47, 0xCF, 0x11,
            0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
        }, 0, props, 0, 16);
        WriteLe64(props, 16, (ulong)props.Length);
        WriteLe64(props, 64, (ulong)play.Ticks);

        var header = new byte[30 + props.Length];
        Buffer.BlockCopy(new byte[]
        {
            0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
            0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
        }, 0, header, 0, 16);
        WriteLe64(header, 16, (ulong)header.Length);
        WriteLe(header, 24, 1);
        Buffer.BlockCopy(props, 0, header, 30, props.Length);
        return header;
    }

    private static void WriteLe(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
        buf[offset + 2] = (byte)(value >> 16);
        buf[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteLe64(byte[] buf, int offset, ulong value)
    {
        for (int i = 0; i < 8; i++)
            buf[offset + i] = (byte)(value >> (8 * i));
    }

    private static string Temp(string extension)
    {
        return Path.Combine(Path.GetTempPath(), "reelwalk-dur-" + Path.GetRandomFileName() + extension);
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
