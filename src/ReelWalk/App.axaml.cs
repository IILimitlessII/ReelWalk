using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ReelWalk.Services;
using ReelWalk.Views;

namespace ReelWalk;
public partial class App : Application
{
    private static Mutex singleInstanceMutex;

    // Loads the styles.
    // Returns nothing.
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ForceBlueAccent();
    }

    // Pins every SystemAccent* color to ReelWalk blue so OS yellow cannot leak in.
    // Returns nothing.
    private void ForceBlueAccent()
    {
        var accent = Color.Parse("#2F7CF0");
        var dark1 = Color.Parse("#2563EB");
        var dark2 = Color.Parse("#1D4ED8");
        var dark3 = Color.Parse("#1E3A8A");
        var light1 = Color.Parse("#5B94F5");
        var light2 = Color.Parse("#93B8F8");
        var light3 = Color.Parse("#BFDBFE");
        Resources["SystemAccentColor"] = accent;
        Resources["SystemAccentColorDark1"] = dark1;
        Resources["SystemAccentColorDark2"] = dark2;
        Resources["SystemAccentColorDark3"] = dark3;
        Resources["SystemAccentColorLight1"] = light1;
        Resources["SystemAccentColorLight2"] = light2;
        Resources["SystemAccentColorLight3"] = light3;
    }

    // Keeps a single instance, then opens the slideshow or the first-launch note.
    // Returns nothing.
    public override void OnFrameworkInitializationCompleted()
    {
        bool createdNew = false;
        try
        {
            singleInstanceMutex = new Mutex(true, "Global\\ReelWalk_SingleInstance_B7E3A1D9", out createdNew);
        }
        catch
        {
            singleInstanceMutex = new Mutex(true, "ReelWalk_SingleInstance_B7E3A1D9", out createdNew);
        }

        var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (desktop == null)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.Exit += OnExit;
        if (!createdNew)
        {
            desktop.Shutdown();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        Dispatcher.UIThread.UnhandledException += App_DispatcherUnhandledException;

        try
        {
            if (!ConfigService.Prepare())
            {
                desktop.MainWindow = Notice.Create(
                    "ReelWalk - First Launch",
                    "A config file ReelWalk.toml has been created next to ReelWalk.\n\nAdd your folder paths, then start ReelWalk again.");
            }
            else
                desktop.MainWindow = new MainWindow();
        }
        catch (Exception ex)
        {
            desktop.MainWindow = Notice.Create(
                "ReelWalk - Error",
                "Error during startup:\n\n" + ex.Message);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Releases the single-instance lock.
    // e is the exit args. Returns nothing.
    private static void OnExit(object sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (singleInstanceMutex == null)
            return;
        try
        {
            singleInstanceMutex.ReleaseMutex();
        }
        catch { }
        singleInstanceMutex.Dispose();
        singleInstanceMutex = null;
    }

    // Records a crash outside the UI thread.
    // e holds the exception. Returns nothing.
    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        System.Diagnostics.Trace.WriteLine(ex != null ? ex.ToString() : "Unknown error");
    }

    // Keeps the process alive after a UI exception.
    // e holds the exception. Returns nothing.
    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Trace.WriteLine(e.Exception);
        e.Handled = true;
    }
}
