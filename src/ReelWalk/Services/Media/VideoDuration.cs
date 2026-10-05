using System;
using System.Collections.Generic;
using System.IO;
using LibVLCSharp.Shared;

namespace ReelWalk.Services;
internal static class VideoDuration
{
    private sealed class Stamp
    {
        internal long Length;
        internal long WriteTicks;
        internal long Milliseconds;
    }

    private sealed class Mp4Clock
    {
        internal uint Scale;
        internal ulong Movie;
        internal ulong Fragment;
        internal ulong Tkhd;
        internal long VideoMs;
        internal long AnyMs;
        internal long PendingMs;
        internal bool PendingVideo;
    }

    private const uint Mvhd = 0x6D766864;
    private const uint Moov = 0x6D6F6F76;
    private const uint Trak = 0x7472616B;
    private const uint Mdia = 0x6D646961;
    private const uint Mdhd = 0x6D646864;
    private const uint Mvex = 0x6D766578;
    private const uint Mehd = 0x6D656864;
    private const uint Tkhd = 0x746B6864;
    private const uint Hdlr = 0x68646C72;
    private const uint Vide = 0x76696465;

    private const ulong EbmlSegment = 0x18538067;
    private const ulong EbmlInfo = 0x1549A966;
    private const ulong EbmlSeekHead = 0x114D9B74;
    private const ulong EbmlSeek = 0x4DBB;
    private const ulong EbmlSeekId = 0x53AB;
    private const ulong EbmlSeekPosition = 0x53AC;
    private const ulong EbmlTimecodeScale = 0x2AD7B1;
    private const ulong EbmlDuration = 0x4489;
    private const ulong EbmlCluster = 0x1F43B675;

    private static readonly byte[] AsfHeader =
    {
        0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
        0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
    };

    private static readonly byte[] AsfFileProps =
    {
        0xA1, 0xDC, 0xAB, 0x8C, 0xA9, 0x47, 0xCF, 0x11,
        0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
    };

    private static readonly Dictionary<string, Stamp> cache =
        new Dictionary<string, Stamp>(StringComparer.OrdinalIgnoreCase);
    private static readonly object gate = new object();
    private static LibVLC probe;

    // Length of a video in milliseconds.
    // path is a file. Returns -1 for photos and when the length cannot be read.
    internal static long Milliseconds(string path)
    {
        if (!MediaTypes.IsVideo(path))
            return -1;

        long length;
        long write;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 16)
                return -1;
            length = info.Length;
            write = info.LastWriteTimeUtc.Ticks;
        }
        catch
        {
            return -1;
        }

        lock (gate)
        {
            Stamp hit;
            if (cache.TryGetValue(path, out hit) && hit.Length == length && hit.WriteTicks == write)
                return hit.Milliseconds;
        }

        long ms = ReadContainer(path);
        if (ms <= 0)
            ms = Probe(path);
        if (ms <= 0)
            return -1;

        lock (gate)
        {
            cache[path] = new Stamp
            {
                Length = length,
                WriteTicks = write,
                Milliseconds = ms
            };
        }
        return ms;
    }

    // Reads a duration stored in the file header.
    // path is a video. Returns -1 when the container has no duration this reader knows.
    private static long ReadContainer(string path)
    {
        try
        {
            string ext = Path.GetExtension(path);
            if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".m4v", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mov", StringComparison.OrdinalIgnoreCase))
                return ReadMp4(path);
            if (ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".webm", StringComparison.OrdinalIgnoreCase))
                return ReadMkv(path);
            if (ext.Equals(".avi", StringComparison.OrdinalIgnoreCase))
                return ReadAvi(path);
            if (ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase))
                return ReadWmv(path);
        }
        catch
        {
            return -1;
        }
        return -1;
    }

    // Movie duration, then the video track when the movie header is empty.
    // path is an MP4, M4V, or MOV. Returns -1 when no positive duration is stored.
    private static long ReadMp4(string path)
    {
        var clock = new Mp4Clock();
        using (var stream = Open(path))
            WalkMp4(stream, 0, stream.Length, 0, clock);

        long movie = ToMs(clock.Movie, clock.Scale);
        if (movie > 0)
            return movie;
        long fragment = ToMs(clock.Fragment, clock.Scale);
        if (fragment > 0)
            return fragment;
        if (clock.VideoMs > 0)
            return clock.VideoMs;
        if (clock.AnyMs > 0)
            return clock.AnyMs;
        return ToMs(clock.Tkhd, clock.Scale);
    }

    // Walks ISO boxes and fills clock.
    // depth stops a damaged file from looping. Returns nothing.
    private static void WalkMp4(Stream stream, long start, long end, int depth, Mp4Clock clock)
    {
        if (depth > 8 || start >= end)
            return;
        long pos = start;
        while (pos + 8 <= end)
        {
            uint type;
            long data;
            long next;
            if (!TryBox(stream, pos, end, out type, out data, out next))
                return;

            if (type == Mvhd)
                ReadMvhd(stream, data, next, clock);
            else if (type == Mdhd)
                clock.PendingMs = MediaHeaderMs(stream, data, next);
            else if (type == Mehd)
                clock.Fragment = HeaderDuration(stream, data, next);
            else if (type == Tkhd)
            {
                ulong ticks = TkhdDuration(stream, data, next);
                if (ticks > clock.Tkhd)
                    clock.Tkhd = ticks;
            }
            else if (type == Hdlr)
                clock.PendingVideo = IsVideoHandler(stream, data, next);
            else if (type == Mdia)
            {
                clock.PendingMs = -1;
                clock.PendingVideo = false;
                WalkMp4(stream, data, next, depth + 1, clock);
                if (clock.PendingMs > clock.AnyMs)
                    clock.AnyMs = clock.PendingMs;
                if (clock.PendingVideo && clock.PendingMs > clock.VideoMs)
                    clock.VideoMs = clock.PendingMs;
            }
            else if (type == Moov || type == Trak || type == Mvex)
                WalkMp4(stream, data, next, depth + 1, clock);

            if (next <= pos)
                return;
            pos = next;
        }
    }

    // Timescale and duration from mvhd.
    // data is the first byte of the box body. Returns nothing.
    private static void ReadMvhd(Stream stream, long data, long end, Mp4Clock clock)
    {
        if (data + 20 > end)
            return;
        stream.Position = data;
        int version = stream.ReadByte();
        stream.Position = data + (version == 1 ? 20 : 12);
        uint scale = ReadU32(stream);
        ulong duration = version == 1 ? ReadU64(stream) : ReadU32(stream);
        if (scale > 0)
            clock.Scale = scale;
        if (duration > clock.Movie)
            clock.Movie = duration;
    }

    // Duration of mdhd, in milliseconds, using that header's own timescale.
    // data is the first byte of the box body. Returns -1 when the box is short.
    private static long MediaHeaderMs(Stream stream, long data, long end)
    {
        if (data + 20 > end)
            return -1;
        stream.Position = data;
        int version = stream.ReadByte();
        long scaleAt = data + (version == 1 ? 20 : 12);
        if (scaleAt + (version == 1 ? 12 : 8) > end)
            return -1;
        stream.Position = scaleAt;
        uint scale = ReadU32(stream);
        ulong duration = version == 1 ? ReadU64(stream) : ReadU32(stream);
        return ToMs(duration, scale);
    }

    // Duration field of mehd, in movie timescale units.
    // data is the first byte of the box body. Returns 0 when the box is short.
    private static ulong HeaderDuration(Stream stream, long data, long end)
    {
        if (data + 8 > end)
            return 0;
        stream.Position = data;
        int version = stream.ReadByte();
        stream.Position = data + 4;
        if (version == 1)
        {
            if (data + 12 > end)
                return 0;
            return ReadU64(stream);
        }
        return ReadU32(stream);
    }

    // Track duration from tkhd, in movie timescale units.
    // data is the first byte of the box body. Returns 0 when the box is short.
    private static ulong TkhdDuration(Stream stream, long data, long end)
    {
        if (data + 4 > end)
            return 0;
        stream.Position = data;
        int version = stream.ReadByte();
        long at = data + (version == 1 ? 28 : 20);
        if (version == 1)
        {
            if (at + 8 > end)
                return 0;
            stream.Position = at;
            return ReadU64(stream);
        }
        if (at + 4 > end)
            return 0;
        stream.Position = at;
        return ReadU32(stream);
    }

    // True when hdlr names a video track.
    // data is the first byte of the box body. Returns false when the box is short.
    private static bool IsVideoHandler(Stream stream, long data, long end)
    {
        if (data + 12 > end)
            return false;
        stream.Position = data + 8;
        return ReadU32(stream) == Vide;
    }

    // Reads one ISO box.
    // pos is the first byte of the header. Returns false when the box does not fit.
    private static bool TryBox(Stream stream, long pos, long limit, out uint type, out long data, out long next)
    {
        type = 0;
        data = 0;
        next = 0;
        if (pos + 8 > limit)
            return false;
        stream.Position = pos;
        uint size32 = ReadU32(stream);
        type = ReadU32(stream);
        long header = 8;
        long size = size32;
        if (size32 == 1)
        {
            if (pos + 16 > limit)
                return false;
            size = (long)ReadU64(stream);
            header = 16;
        }
        else if (size32 == 0)
        {
            size = limit - pos;
        }
        if (size < header)
            return false;
        data = pos + header;
        next = pos + size;
        if (next < data)
            return false;
        if (next > limit)
            next = limit;
        return data <= limit;
    }

    // Matroska or WebM duration, in milliseconds.
    // path is an MKV or WEBM. Returns -1 when Info has no duration.
    private static long ReadMkv(string path)
    {
        using (var stream = Open(path))
        {
            long end = stream.Length;
            while (stream.Position + 2 <= end)
            {
                ulong id;
                long size;
                long data;
                if (!TryEbml(stream, end, out id, out size, out data))
                    return -1;
                if (id == EbmlSegment)
                {
                    long segmentEnd = size < 0 ? end : Math.Min(end, data + size);
                    return ReadSegment(stream, data, segmentEnd);
                }
                if (size < 0)
                    return -1;
                stream.Position = data + size;
            }
        }
        return -1;
    }

    // Info element inside a Matroska segment.
    // start is the first payload byte. Returns -1 when Duration is missing.
    private static long ReadSegment(Stream stream, long start, long end)
    {
        long infoAt = -1;
        bool jumped = false;
        stream.Position = start;
        int steps = 0;
        while (stream.Position + 2 <= end && steps++ < 100000)
        {
            ulong id;
            long size;
            long data;
            if (!TryEbml(stream, end, out id, out size, out data))
                return -1;
            if (id == EbmlInfo)
            {
                long ms = ReadInfo(stream, data, size < 0 ? end : Math.Min(end, data + size));
                if (ms > 0)
                    return ms;
            }
            else if (id == EbmlSeekHead)
            {
                long found = FindInfoSeek(stream, data, size < 0 ? end : Math.Min(end, data + size));
                if (found >= 0)
                    infoAt = start + found;
            }
            else if (id == EbmlCluster)
            {
                if (!jumped && infoAt >= 0 && infoAt < end)
                {
                    jumped = true;
                    stream.Position = infoAt;
                    continue;
                }
                if (size < 0)
                    return -1;
            }

            if (size < 0)
                return -1;
            long next = data + size;
            if (next < stream.Position)
                return -1;
            stream.Position = next;
        }
        return -1;
    }

    // TimecodeScale and Duration from an Info element.
    // start is the first payload byte. Returns -1 when Duration is missing.
    private static long ReadInfo(Stream stream, long start, long end)
    {
        ulong scale = 1000000;
        double duration = -1;
        stream.Position = start;
        while (stream.Position + 2 <= end)
        {
            ulong id;
            long size;
            long data;
            if (!TryEbml(stream, end, out id, out size, out data))
                break;
            if (size < 0)
                break;
            if (id == EbmlTimecodeScale)
                scale = ReadUint(stream, size);
            else if (id == EbmlDuration)
                duration = ReadFloat(stream, size);
            stream.Position = data + size;
        }
        if (duration <= 0 || scale == 0)
            return -1;
        double ms = duration * scale / 1000000d;
        if (double.IsNaN(ms) || ms <= 0 || ms > 1000d * 60 * 60 * 24 * 365)
            return -1;
        return (long)Math.Round(ms);
    }

    // SeekHead entry that points at Info, relative to the segment payload.
    // start is the first payload byte. Returns -1 when Info is not listed.
    private static long FindInfoSeek(Stream stream, long start, long end)
    {
        stream.Position = start;
        while (stream.Position + 2 <= end)
        {
            ulong id;
            long size;
            long data;
            if (!TryEbml(stream, end, out id, out size, out data))
                return -1;
            if (size < 0)
                return -1;
            if (id == EbmlSeek)
            {
                long at = ReadSeek(stream, data, data + size);
                if (at >= 0)
                    return at;
            }
            stream.Position = data + size;
        }
        return -1;
    }

    // One Seek entry.
    // start is the first payload byte. Returns -1 when the entry is not Info.
    private static long ReadSeek(Stream stream, long start, long end)
    {
        bool info = false;
        long position = -1;
        stream.Position = start;
        while (stream.Position + 2 <= end)
        {
            ulong id;
            long size;
            long data;
            if (!TryEbml(stream, end, out id, out size, out data))
                return -1;
            if (size < 0)
                return -1;
            if (id == EbmlSeekId)
            {
                ulong target = ReadUint(stream, size);
                info = target == EbmlInfo;
            }
            else if (id == EbmlSeekPosition)
            {
                position = (long)ReadUint(stream, size);
            }
            stream.Position = data + size;
        }
        if (!info || position < 0)
            return -1;
        return position;
    }

    // AVI length from avih, or from a video stream header when the frame count is empty.
    // path is an AVI. Returns -1 when neither header has a duration.
    private static long ReadAvi(string path)
    {
        using (var stream = Open(path))
        {
            long avih = -1;
            long video = -1;
            long any = -1;
            WalkAvi(stream, 12, Math.Min(stream.Length, 1024 * 1024), ref avih, ref video, ref any);
            if (avih > 0)
                return avih;
            if (video > 0)
                return video;
            return any;
        }
    }

    // Walks RIFF chunks, including LIST.
    // start is the first chunk. Returns nothing.
    private static void WalkAvi(Stream stream, long start, long end, ref long avih, ref long video, ref long any)
    {
        var kind = new byte[4];
        long pos = start;
        while (pos + 8 <= end)
        {
            stream.Position = pos;
            if (stream.Read(kind, 0, 4) != 4)
                return;
            uint chunk = ReadU32Le(stream);
            long next = pos + 8L + chunk + (chunk & 1);
            if (next <= pos)
                return;
            if (IsFour(kind, (byte)'a', (byte)'v', (byte)'i', (byte)'h') && chunk >= 20 && avih <= 0)
            {
                uint micro = ReadU32Le(stream);
                stream.Position += 12;
                uint frames = ReadU32Le(stream);
                if (micro > 0 && frames > 0)
                    avih = (long)micro * frames / 1000L;
            }
            else if (IsFour(kind, (byte)'s', (byte)'t', (byte)'r', (byte)'h') && chunk >= 36)
            {
                long ms = StrhMs(stream);
                if (ms > any)
                    any = ms;
                stream.Position = pos + 8;
                if (IsFourAt(stream, (byte)'v', (byte)'i', (byte)'d', (byte)'s') && ms > video)
                    video = ms;
            }
            else if (IsFour(kind, (byte)'L', (byte)'I', (byte)'S', (byte)'T') && chunk >= 4)
            {
                WalkAvi(stream, pos + 12, Math.Min(next, end), ref avih, ref video, ref any);
            }
            pos = next;
        }
    }

    // dwLength * dwScale / dwRate for one stream header, in milliseconds.
    // The stream sits on fccType. Returns -1 when the rate is 0.
    private static long StrhMs(Stream stream)
    {
        stream.Position += 20;
        uint scale = ReadU32Le(stream);
        uint rate = ReadU32Le(stream);
        stream.Position += 4;
        uint length = ReadU32Le(stream);
        if (rate == 0 || length == 0 || scale == 0)
            return -1;
        return (long)length * scale * 1000L / rate;
    }

    // ASF play duration, in milliseconds.
    // path is a WMV. Returns -1 when the file properties object is missing.
    private static long ReadWmv(string path)
    {
        using (var stream = Open(path))
        {
            var guid = new byte[16];
            if (stream.Read(guid, 0, 16) != 16 || !Same(guid, AsfHeader))
                return -1;
            long size = (long)ReadU64Le(stream);
            if (size < 30)
                return -1;
            uint count = ReadU32Le(stream);
            stream.Position += 2;
            long end = Math.Min(stream.Length, size);
            for (uint i = 0; i < count; i++)
            {
                long obj = stream.Position;
                if (obj + 24 > end)
                    return -1;
                if (stream.Read(guid, 0, 16) != 16)
                    return -1;
                long objSize = (long)ReadU64Le(stream);
                if (objSize < 24 || obj + objSize < obj)
                    return -1;
                if (Same(guid, AsfFileProps))
                    return FilePropsMs(stream);
                stream.Position = obj + objSize;
            }
        }
        return -1;
    }

    // Play duration of an ASF file properties object, in milliseconds.
    // The stream sits on the file id. Returns -1 when both durations are 0.
    private static long FilePropsMs(Stream stream)
    {
        stream.Position += 40;
        ulong play = ReadU64Le(stream);
        ulong send = ReadU64Le(stream);
        ulong ticks = play > 0 ? play : send;
        if (ticks == 0)
            return -1;
        return (long)(ticks / 10000UL);
    }

    // Asks LibVLC when the header reader does not know the file.
    // path is a video. Returns -1 when LibVLC cannot open it.
    private static long Probe(string path)
    {
        try
        {
            lock (gate)
            {
                if (probe == null)
                    probe = new LibVLC("--quiet");
                using (var media = new Media(probe, path, FromType.FromPath))
                {
                    MediaParsedStatus status = media.Parse(MediaParseOptions.ParseLocal, 4000).GetAwaiter().GetResult();
                    if (status != MediaParsedStatus.Done || media.Duration <= 0)
                        return -1;
                    return media.Duration;
                }
            }
        }
        catch
        {
            return -1;
        }
    }

    // Opens a file without blocking a player that already has it.
    // path is a file. Returns the stream.
    private static FileStream Open(string path)
    {
        return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    // Converts a timescale duration to milliseconds.
    // scale is the clock ticks per second. Returns -1 when either value is 0.
    private static long ToMs(ulong duration, uint scale)
    {
        if (duration == 0 || scale == 0)
            return -1;
        return (long)(duration * 1000UL / scale);
    }

    // One EBML element.
    // limit is the first byte that must not be read. Returns false at the end of the parent.
    private static bool TryEbml(Stream stream, long limit, out ulong id, out long size, out long data)
    {
        id = 0;
        size = 0;
        data = 0;
        ulong rawSize;
        if (!ReadVint(stream, limit, false, out id))
            return false;
        if (!ReadVint(stream, limit, true, out rawSize))
            return false;
        data = stream.Position;
        if (rawSize == ulong.MaxValue)
        {
            size = -1;
            return true;
        }
        size = (long)rawSize;
        return true;
    }

    // Reads an EBML vint.
    // mask strips the length marker, which sizes use and ids do not. Returns false when the vint is cut off.
    private static bool ReadVint(Stream stream, long limit, bool mask, out ulong value)
    {
        value = 0;
        if (stream.Position >= limit)
            return false;
        int first = stream.ReadByte();
        if (first <= 0)
            return false;
        int length = 1;
        int marker = 0x80;
        while (length < 8 && (first & marker) == 0)
        {
            marker >>= 1;
            length++;
        }
        if ((first & marker) == 0)
            return false;
        if (stream.Position + length - 1 > limit)
            return false;
        ulong raw = (ulong)first;
        for (int i = 1; i < length; i++)
        {
            int b = stream.ReadByte();
            if (b < 0)
                return false;
            raw = (raw << 8) | (uint)b;
        }
        if (!mask)
        {
            value = raw;
            return true;
        }
        int dataBits = 7 * length;
        ulong dataMask = dataBits >= 64 ? ulong.MaxValue : (1UL << dataBits) - 1;
        ulong data = raw & dataMask;
        if (data == dataMask)
        {
            value = ulong.MaxValue;
            return true;
        }
        value = data;
        return true;
    }

    // Big-endian unsigned integer of size bytes.
    // size is 1 to 8. Returns 0 when a byte is missing.
    private static ulong ReadUint(Stream stream, long size)
    {
        ulong value = 0;
        long count = size > 8 ? 8 : size;
        for (long i = 0; i < count; i++)
        {
            int b = stream.ReadByte();
            if (b < 0)
                return 0;
            value = (value << 8) | (uint)b;
        }
        return value;
    }

    // Big-endian float of size 4 or 8.
    // size is the element width. Returns -1 for any other width.
    private static double ReadFloat(Stream stream, long size)
    {
        if (size != 4 && size != 8)
            return -1;
        var buf = new byte[size];
        if (stream.Read(buf, 0, (int)size) != size)
            return -1;
        if (BitConverter.IsLittleEndian)
            Array.Reverse(buf);
        if (size == 4)
            return BitConverter.ToSingle(buf, 0);
        return BitConverter.ToDouble(buf, 0);
    }

    // Big-endian 32-bit value.
    // Returns 0 when the stream ends early.
    private static uint ReadU32(Stream stream)
    {
        int a = stream.ReadByte();
        int b = stream.ReadByte();
        int c = stream.ReadByte();
        int d = stream.ReadByte();
        if (d < 0)
            return 0;
        return (uint)((a << 24) | (b << 16) | (c << 8) | d);
    }

    // Big-endian 64-bit value.
    // Returns 0 when the stream ends early.
    private static ulong ReadU64(Stream stream)
    {
        ulong hi = ReadU32(stream);
        ulong lo = ReadU32(stream);
        return (hi << 32) | lo;
    }

    // Little-endian 32-bit value.
    // Returns 0 when the stream ends early.
    private static uint ReadU32Le(Stream stream)
    {
        int a = stream.ReadByte();
        int b = stream.ReadByte();
        int c = stream.ReadByte();
        int d = stream.ReadByte();
        if (d < 0)
            return 0;
        return (uint)(a | (b << 8) | (c << 16) | (d << 24));
    }

    // Little-endian 64-bit value.
    // Returns 0 when the stream ends early.
    private static ulong ReadU64Le(Stream stream)
    {
        ulong lo = ReadU32Le(stream);
        ulong hi = ReadU32Le(stream);
        return lo | (hi << 32);
    }

    // True when kind is the four characters a, b, c, d.
    // kind is 4 bytes. Returns false when it is shorter.
    private static bool IsFour(byte[] kind, byte a, byte b, byte c, byte d)
    {
        return kind != null &&
               kind.Length >= 4 &&
               kind[0] == a && kind[1] == b && kind[2] == c && kind[3] == d;
    }

    // True when the next four bytes are a, b, c, d.
    // Returns false when the stream ends early.
    private static bool IsFourAt(Stream stream, byte a, byte b, byte c, byte d)
    {
        return stream.ReadByte() == a &&
               stream.ReadByte() == b &&
               stream.ReadByte() == c &&
               stream.ReadByte() == d;
    }

    // True when both arrays hold the same bytes.
    // Returns false when either array is missing.
    private static bool Same(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }
        return true;
    }
}
