using System;
using Avalonia;
using Avalonia.Media;

namespace ReelWalk.Services;
internal sealed class Palette
{
    internal readonly string Id;
    internal readonly Color Ink;
    internal readonly Color Surface;
    internal readonly Color Lift;
    internal readonly Color Accent;

    internal Palette(string id, string ink, string surface, string lift, string accent)
    {
        Id = id;
        Ink = Color.Parse(ink);
        Surface = Color.Parse(surface);
        Lift = Color.Parse(lift);
        Accent = Color.Parse(accent);
    }
}

internal static class ThemeCatalog
{
    internal const string HarborBlue = "HarborBlue";
    internal const string DarkSlate = "DarkSlate";
    internal const string JungleGreen = "JungleGreen";
    internal const string MidnightSky = "MidnightSky";
    internal const string PastelDreams = "PastelDreams";

    private static readonly Color DarkText = Color.Parse("#141018");

    private static readonly Palette[] All =
    {
        new Palette(HarborBlue, "#141414", "#152238", "#5B94F5", "#2F7CF0"),
        new Palette(DarkSlate, "#2E3440", "#3B4252", "#434C5E", "#4C566A"),
        new Palette(JungleGreen, "#073B3A", "#0B6E4F", "#08A045", "#6BBF59"),
        new Palette(MidnightSky, "#02010A", "#04052E", "#140152", "#22007C"),
        new Palette(PastelDreams, "#6E44FF", "#B892FF", "#FFC2E2", "#FF90B3")
    };

    // Known id, or Harbor Blue when the toml value is missing or unknown.
    // id is the saved theme name.
    internal static string Normalize(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return HarborBlue;
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase))
                return All[i].Id;
        }
        return HarborBlue;
    }

    // Paints the shared brushes and the accent colors used by controls.
    // id is a theme name. Unknown names use Harbor Blue. Returns nothing.
    internal static void Apply(string id)
    {
        var theme = Find(Normalize(id));
        var accent = theme.Accent;
        var hover = Luminance(theme.Lift) > Luminance(accent) ? theme.Lift : Lighten(accent, 0.18);
        var press = Darken(accent, 0.18);
        var onAccent = Luminance(accent) > 0.32 ? DarkText : Colors.White;
        var fieldText = Luminance(theme.Surface) > 0.35 ? DarkText : Colors.White;
        var caret = Luminance(theme.Lift) > 0.72 ? DarkText : theme.Lift;

        Paint("ThemePanelBrush", WithAlpha(theme.Ink, 0xCC));
        Paint("ThemeSurfaceBrush", theme.Surface);
        Paint("ThemeAccentBrush", accent);
        Paint("ThemeAccentHoverBrush", hover);
        Paint("ThemeAccentPressBrush", press);
        Paint("ThemeAccentWashBrush", WithAlpha(accent, 0x44));
        Paint("ThemeAccentSoftBrush", WithAlpha(accent, 0x55));
        Paint("ThemeAccentMidBrush", WithAlpha(accent, 0x66));
        Paint("ThemeAccentStrongBrush", WithAlpha(accent, 0x88));
        Paint("ThemeSpinBrush", WithAlpha(accent, 0x33));
        Paint("ThemeCaretBrush", caret);
        Paint("ThemeLinkBrush", caret);
        Paint("ThemeOnAccentBrush", onAccent);
        Paint("ThemeFieldTextBrush", fieldText);

        var app = Application.Current;
        if (app == null)
            return;

        app.Resources["SystemAccentColor"] = accent;
        app.Resources["SystemAccentColorDark1"] = press;
        app.Resources["SystemAccentColorDark2"] = Darken(accent, 0.32);
        app.Resources["SystemAccentColorDark3"] = Darken(accent, 0.5);
        app.Resources["SystemAccentColorLight1"] = hover;
        app.Resources["SystemAccentColorLight2"] = Lighten(accent, 0.35);
        app.Resources["SystemAccentColorLight3"] = Lighten(accent, 0.55);
        Paint("SystemControlHighlightAccentBrush", accent);
        Paint("SystemControlHighlightAltAccentBrush", accent);
        Paint("SystemControlHighlightListAccentHighBrush", accent);
        Paint("SystemControlHighlightListAccentLowBrush", WithAlpha(accent, 0x55));
        Paint("SystemControlHighlightListAccentMediumBrush", WithAlpha(accent, 0x88));
        Paint("SystemControlHighlightAccentListAccentHighBrush", accent);
        Paint("SystemControlForegroundAccentBrush", accent);
        Paint("AccentButtonBackground", accent);
        Paint("AccentButtonBackgroundPointerOver", hover);
        Paint("AccentButtonBackgroundPressed", press);
        Paint("AccentButtonBorderBrush", accent);
        Paint("AccentButtonBorderBrushPointerOver", hover);
        Paint("AccentButtonBorderBrushPressed", press);
        Paint("TextControlBorderBrushFocused", accent);
        Paint("TextControlElevationBorderFocusedBrush", accent);
        Paint("ComboBoxBorderBrushFocused", accent);
        Paint("FocusStrokeColorOuter", accent);
        Paint("FocusStrokeColorInner", hover);
        Paint("SystemControlHighlightListLowBrush", WithAlpha(accent, 0x44));
        Paint("SystemControlHighlightListMediumBrush", WithAlpha(accent, 0x66));
        Paint("SystemControlHighlightListAccentLowSelectedBrush", accent);
        Paint("ScrollBarThumbFill", WithAlpha(accent, 0x66));
        Paint("ScrollBarThumbFillPointerOver", WithAlpha(accent, 0xAA));
        Paint("ScrollBarThumbFillPressed", accent);
    }

    // Looks up a palette by its saved id.
    // id is already normalized. Returns Harbor Blue when missing.
    private static Palette Find(string id)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].Id == id)
                return All[i];
        }
        return All[0];
    }

    // Updates one brush, or adds it when the key is new.
    // key is a resource name. Returns nothing.
    private static void Paint(string key, Color color)
    {
        var app = Application.Current;
        if (app == null)
            return;

        object existing;
        if (app.Resources.TryGetValue(key, out existing))
        {
            var brush = existing as SolidColorBrush;
            if (brush != null)
            {
                brush.Color = color;
                return;
            }
        }

        app.Resources[key] = new SolidColorBrush(color);
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Color Lighten(Color color, double amount)
    {
        return Mix(color, Colors.White, amount);
    }

    private static Color Darken(Color color, double amount)
    {
        return Mix(color, Colors.Black, amount);
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        if (amount < 0) amount = 0;
        if (amount > 1) amount = 1;
        byte channel(byte a, byte b)
        {
            return (byte)Math.Round(a + (b - a) * amount);
        }
        return Color.FromArgb(255, channel(from.R, to.R), channel(from.G, to.G), channel(from.B, to.B));
    }

    // Relative luminance, 0 dark through 1 white.
    // color is an opaque swatch.
    private static double Luminance(Color color)
    {
        double channel(byte value)
        {
            double v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * channel(color.R) + 0.7152 * channel(color.G) + 0.0722 * channel(color.B);
    }
}
