using Avalonia;
using LibVLCSharp.Shared;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ReelWalk;

internal static class Program
{
    // Starts the Avalonia desktop app.
    // args are the process arguments. Returns nothing.
    [STAThread]
    public static void Main(string[] args)
    {
        var libvlc = LibVlcDirectory();
        if (libvlc == null)
            Core.Initialize();
        else
            Core.Initialize(libvlc);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Configures Avalonia, the platform, and the font.
    // Returns the app builder.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }

    // Folder that contains libvlc.dll after a single-file publish unpacks.
    // Returns null on Linux, where LibVLCSharp loads the system library.
    private static string LibVlcDirectory()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        var arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";
        var beside = Path.Combine(AppContext.BaseDirectory, "libvlc", arch);
        if (File.Exists(Path.Combine(beside, "libvlc.dll")))
            return beside;

        var native = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string;
        if (string.IsNullOrEmpty(native))
            return null;

        var parts = native.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            var candidate = Path.Combine(parts[i].Trim(), "libvlc", arch);
            if (File.Exists(Path.Combine(candidate, "libvlc.dll")))
                return candidate;
        }

        return null;
    }
}
