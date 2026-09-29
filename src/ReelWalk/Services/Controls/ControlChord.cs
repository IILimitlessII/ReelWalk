using System;
using System.Text;
using Avalonia.Input;

namespace ReelWalk.Services.Controls;
internal struct ControlChord
{
    internal Key Key;
    internal KeyModifiers Modifiers;

    // Reads a key such as Ctrl+Shift+Right.
    // text is the toml value. Returns an empty chord when text is blank.
    internal static ControlChord Parse(string text)
    {
        var chord = new ControlChord();
        if (string.IsNullOrWhiteSpace(text))
            return chord;

        var parts = text.Split('+');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (part.Length == 0)
                continue;
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                chord.Modifiers |= KeyModifiers.Control;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                chord.Modifiers |= KeyModifiers.Shift;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                chord.Modifiers |= KeyModifiers.Alt;
            else
                chord.Key = ParseKey(part);
        }
        return chord;
    }

    // Writes key and modifiers as toml text such as Ctrl+Shift+Right.
    // Returns an empty string when key is None.
    internal static string Format(Key key, KeyModifiers mods)
    {
        if (key == Key.None)
            return "";
        var sb = new StringBuilder(24);
        if ((mods & KeyModifiers.Control) != 0)
            sb.Append("Ctrl+");
        if ((mods & KeyModifiers.Shift) != 0)
            sb.Append("Shift+");
        if ((mods & KeyModifiers.Alt) != 0)
            sb.Append("Alt+");
        sb.Append(FormatKeyName(key));
        return sb.ToString();
    }

    // Short name for a key in toml chords.
    // key is an Avalonia key. Returns its label.
    private static string FormatKeyName(Key key)
    {
        if (key == Key.Escape)
            return "Escape";
        if (key == Key.Delete)
            return "Delete";
        if (key == Key.PageUp)
            return "PageUp";
        if (key == Key.PageDown)
            return "PageDown";
        if (key == Key.Back)
            return "Back";
        return key.ToString();
    }

    // True when key and the Ctrl, Shift, and Alt flags match this chord.
    // Extra mouse buttons are ignored.
    internal bool Matches(Key key, KeyModifiers mods)
    {
        if (Key == Key.None || key != Key)
            return false;
        var mask = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt;
        return (mods & mask) == Modifiers;
    }

    // Avalonia key name, plus a few short aliases.
    // part is one piece of the chord. Returns None when it is not a key.
    private static Key ParseKey(string part)
    {
        if (part.Equals("Esc", StringComparison.OrdinalIgnoreCase))
            return Key.Escape;
        if (part.Equals("Del", StringComparison.OrdinalIgnoreCase))
            return Key.Delete;
        if (part.Equals("PgUp", StringComparison.OrdinalIgnoreCase))
            return Key.PageUp;
        if (part.Equals("PgDn", StringComparison.OrdinalIgnoreCase))
            return Key.PageDown;
        Key key;
        if (Enum.TryParse(part, true, out key))
            return key;
        return Key.None;
    }
}
