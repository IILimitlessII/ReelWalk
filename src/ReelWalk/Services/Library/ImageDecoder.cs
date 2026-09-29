using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ReelWalk.Services;
internal sealed class ImageDecoder
{
    private readonly int decodePixelWidth;

    // Stores the widest pixel width to decode.
    // decodePixelWidth is clamped to at least 1. Returns nothing.
    internal ImageDecoder(int decodePixelWidth)
    {
        this.decodePixelWidth = Math.Max(1, decodePixelWidth);
    }

    // Decodes path, scaled down only when wider than the screen.
    // Returns null when the file cannot be read.
    internal Bitmap Load(string path)
    {
        try
        {
            using (var codec = SKCodec.Create(path))
            {
                if (codec == null)
                    return null;

                var info = codec.Info;
                int width = info.Width;
                int height = info.Height;
                if (width <= 0 || height <= 0)
                    return null;

                if (width > decodePixelWidth)
                {
                    height = Math.Max(1, height * decodePixelWidth / width);
                    width = decodePixelWidth;
                }

                var desired = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var raw = SKBitmap.Decode(codec, desired))
                {
                    if (raw == null)
                        return null;

                    var origin = codec.EncodedOrigin;
                    if (origin == SKEncodedOrigin.TopLeft || origin == SKEncodedOrigin.Default)
                        return ToAvalonia(raw);

                    using (var oriented = Orient(raw, origin))
                        return ToAvalonia(oriented);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    // Copies pixels into an Avalonia bitmap.
    // source is Bgra8888. Returns the bitmap.
    private static Bitmap ToAvalonia(SKBitmap source)
    {
        int width = source.Width;
        int height = source.Height;
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        int row = width * 4;
        var buffer = new byte[row];
        using (var frame = bitmap.Lock())
        {
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(source.GetPixels(), y * source.RowBytes), buffer, 0, row);
                Marshal.Copy(buffer, 0, IntPtr.Add(frame.Address, y * frame.RowBytes), row);
            }
        }
        return bitmap;
    }

    // Rotates or flips source to match the encoded origin.
    // origin is the codec orientation. Returns a new bitmap.
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        bool swap = origin == SKEncodedOrigin.LeftTop
            || origin == SKEncodedOrigin.RightTop
            || origin == SKEncodedOrigin.RightBottom
            || origin == SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using (var canvas = new SKCanvas(result))
        {
            switch (origin)
            {
                case SKEncodedOrigin.TopRight:
                    canvas.Scale(-1, 1, source.Width / 2f, 0);
                    break;
                case SKEncodedOrigin.BottomRight:
                    canvas.RotateDegrees(180, source.Width / 2f, source.Height / 2f);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Scale(1, -1, 0, source.Height / 2f);
                    break;
                case SKEncodedOrigin.LeftTop:
                    canvas.Translate(0, source.Width);
                    canvas.RotateDegrees(-90);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(source.Height, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.RightBottom:
                    canvas.Translate(source.Height, 0);
                    canvas.RotateDegrees(90);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, source.Width);
                    canvas.RotateDegrees(-90);
                    break;
            }
            canvas.DrawBitmap(source, 0, 0);
        }
        return result;
    }
}
