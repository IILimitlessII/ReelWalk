using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ReelWalk.Tests.HeadlessApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ReelWalk.Tests;
public class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}

public class TestApplication : Application
{
    public override void Initialize()
    {
        Resources["ThemePanelBrush"] = new SolidColorBrush(Color.Parse("#CC141414"));
        Resources["ThemeSurfaceBrush"] = new SolidColorBrush(Color.Parse("#152238"));
        Resources["ThemeAccentBrush"] = new SolidColorBrush(Color.Parse("#2F7CF0"));
        Resources["ThemeAccentHoverBrush"] = new SolidColorBrush(Color.Parse("#5B94F5"));
        Resources["ThemeAccentPressBrush"] = new SolidColorBrush(Color.Parse("#2563EB"));
        Resources["ThemeFieldTextBrush"] = new SolidColorBrush(Colors.White);
        Resources["ThemeOnAccentBrush"] = new SolidColorBrush(Colors.White);
        Resources["ThemeCaretBrush"] = new SolidColorBrush(Color.Parse("#5B94F5"));
        Resources["ThemeLinkBrush"] = new SolidColorBrush(Color.Parse("#5B94F5"));
    }
}
