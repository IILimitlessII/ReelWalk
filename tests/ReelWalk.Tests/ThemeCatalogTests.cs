using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using ReelWalk.Services;
using Xunit;

namespace ReelWalk.Tests;
public class ThemeCatalogTests
{
    [Theory]
    [InlineData(null, "HarborBlue")]
    [InlineData("", "HarborBlue")]
    [InlineData("  ", "HarborBlue")]
    [InlineData("nope", "HarborBlue")]
    [InlineData("harborblue", "HarborBlue")]
    [InlineData(" DarkSlate ", "DarkSlate")]
    [InlineData("junglegreen", "JungleGreen")]
    [InlineData("MIDNIGHTSKY", "MidnightSky")]
    [InlineData("pasteldreams", "PastelDreams")]
    public void Normalize_KnownIds_AndUnknownFallsBack(string raw, string expected)
    {
        Assert.Equal(expected, ThemeCatalog.Normalize(raw));
    }

    [AvaloniaFact]
    public void Apply_PaintsEachAccent_AndPastelUsesDarkFieldText()
    {
        var accents = new[]
        {
            ThemeCatalog.HarborBlue,
            ThemeCatalog.DarkSlate,
            ThemeCatalog.JungleGreen,
            ThemeCatalog.MidnightSky,
            ThemeCatalog.PastelDreams
        };
        Color previous = default;
        for (int i = 0; i < accents.Length; i++)
        {
            ThemeCatalog.Apply(accents[i]);
            var accent = Brush("ThemeAccentBrush");
            if (i > 0)
                Assert.NotEqual(previous, accent);
            previous = accent;
        }

        ThemeCatalog.Apply(ThemeCatalog.HarborBlue);
        Assert.Equal(Color.Parse("#2F7CF0"), Brush("ThemeAccentBrush"));

        ThemeCatalog.Apply(ThemeCatalog.DarkSlate);
        Assert.Equal(Color.Parse("#4C566A"), Brush("ThemeAccentBrush"));

        ThemeCatalog.Apply(ThemeCatalog.JungleGreen);
        Assert.Equal(Color.Parse("#6BBF59"), Brush("ThemeAccentBrush"));

        ThemeCatalog.Apply(ThemeCatalog.MidnightSky);
        Assert.Equal(Color.Parse("#22007C"), Brush("ThemeAccentBrush"));

        ThemeCatalog.Apply(ThemeCatalog.PastelDreams);
        Assert.Equal(Color.Parse("#FF90B3"), Brush("ThemeAccentBrush"));
        Assert.Equal(Color.Parse("#141018"), Brush("ThemeFieldTextBrush"));
    }

    private static Color Brush(string key)
    {
        var brush = Application.Current.Resources[key] as SolidColorBrush;
        Assert.NotNull(brush);
        return brush.Color;
    }
}
