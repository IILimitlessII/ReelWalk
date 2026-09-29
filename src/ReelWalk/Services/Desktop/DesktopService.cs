using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ReelWalk.Services;
internal static class DesktopService
{
    // Reveals path in the system file browser.
    // path is a file. Returns nothing when the file is missing.
    internal static void OpenInExplorer(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"",
                    UseShellExecute = true
                });
                return;
            }

            var folder = Path.GetDirectoryName(path);
            OpenFolder(folder);
        }
        catch { }
    }

    // Opens folder in the system file browser.
    // folder must exist. Returns nothing when missing.
    internal static void OpenFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder))
            return;
        try
        {
            if (!Directory.Exists(folder))
                return;
        }
        catch
        {
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + folder + "\"",
                    UseShellExecute = true
                });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = "\"" + folder + "\"",
                UseShellExecute = false
            });
        }
        catch { }
    }
}
