using System;
using System.Collections.Generic;
using System.IO;

namespace ReelWalk.Services;
internal static class MediaTypes
{
    private static readonly HashSet<string> ImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff",
            ".wdp", ".jxr", ".ico", ".webp", ".heic", ".heif", ".avif"
        };

    private static readonly HashSet<string> VideoExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".wmv", ".avi", ".mov", ".m4v", ".mkv", ".webm"
        };

    private static readonly HashSet<string> AllExtensions;

    // Builds the combined set of image and video extensions.
    // Returns nothing.
    static MediaTypes()
    {
        AllExtensions = new HashSet<string>(ImageExtensions, StringComparer.OrdinalIgnoreCase);
        foreach (var ext in VideoExtensions)
            AllExtensions.Add(ext);
    }

    // True when path uses a video extension.
    // path is a file path. Returns false when path is empty.
    internal static bool IsVideo(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return VideoExtensions.Contains(Path.GetExtension(path));
    }

    // True when path uses a still-image extension.
    // path is a file path. Returns false when path is empty.
    private static bool IsImage(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return ImageExtensions.Contains(Path.GetExtension(path));
    }

    // True when path is an image or a video this app can open.
    // path is a file path. Returns false when path is empty.
    internal static bool IsSupported(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return AllExtensions.Contains(Path.GetExtension(path));
    }

    // Maps a show setting to Both, Images, or Videos.
    // value is the raw setting. Returns Both when it is blank or unknown.
    internal static string NormalizeShow(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Both";
        var v = value.Trim();
        if (v.Equals("Images", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Photos", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Photo", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Pictures", StringComparison.OrdinalIgnoreCase))
            return "Images";
        if (v.Equals("Videos", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Video", StringComparison.OrdinalIgnoreCase))
            return "Videos";
        return "Both";
    }

    // True when path should play under the current show filter.
    // show is Both, Images, or Videos. Returns false for unsupported files.
    internal static bool Allows(string path, string show)
    {
        if (!IsSupported(path))
            return false;
        show = NormalizeShow(show);
        if (show == "Videos")
            return IsVideo(path);
        if (show == "Images")
            return IsImage(path);
        return true;
    }

    // Short label for the show filter.
    // show is Both, Images, or Videos. Returns the words shown in the toast.
    internal static string ShowLabel(string show)
    {
        show = NormalizeShow(show);
        if (show == "Images")
            return "Photos only";
        if (show == "Videos")
            return "Videos only";
        return "Photos and videos";
    }
}
