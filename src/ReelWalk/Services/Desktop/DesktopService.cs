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
            if (string.IsNullOrEmpty(folder))
                return;
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
