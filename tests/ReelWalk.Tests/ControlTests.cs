using Avalonia.Input;
using ReelWalk.Models;
using ReelWalk.Services.Controls;
using Xunit;

namespace ReelWalk.Tests;
public class ControlTests
{
    [Fact]
    public void EveryDefaultChord_ParsesMatchesAndFormats()
    {
        var map = ControlCatalog.Create();
        Assert.Equal(ControlCatalog.Defaults.Length, map.Count);
        for (int i = 0; i < ControlCatalog.Defaults.Length; i++)
        {
            var name = ControlCatalog.Defaults[i][0];
            var text = ControlCatalog.Defaults[i][1];
            Assert.Equal(text, ControlCatalog.Default(name));
            var chord = ControlChord.Parse(text);
            Assert.NotEqual(Key.None, chord.Key);
            Assert.True(chord.Matches(chord.Key, chord.Modifiers));
            Assert.Equal(text, ControlChord.Format(chord.Key, chord.Modifiers));
        }
    }

    [Fact]
    public void Parse_BlankAndUnknown_DoNotMatch()
    {
        var blank = ControlChord.Parse("  ");
        Assert.Equal(Key.None, blank.Key);
        Assert.False(blank.Matches(Key.Space, KeyModifiers.None));
        Assert.Equal("", ControlChord.Format(Key.None, KeyModifiers.Control));
        Assert.Equal("", ControlCatalog.Default("not-a-command"));
    }

    [Fact]
    public void Parse_AcceptsAliases_AndIgnoresExtraModifiers()
    {
        var esc = ControlChord.Parse("Ctrl+Esc");
        Assert.True(esc.Matches(Key.Escape, KeyModifiers.Control | KeyModifiers.Meta));
        Assert.False(esc.Matches(Key.Escape, KeyModifiers.None));

        var del = ControlChord.Parse("Shift+Del");
        Assert.Equal("Shift+Delete", ControlChord.Format(del.Key, del.Modifiers));
        Assert.Equal(Key.PageUp, ControlChord.Parse("PgUp").Key);
        Assert.Equal(Key.PageDown, ControlChord.Parse("PgDn").Key);
    }

    [Fact]
    public void SlideshowConfig_Key_FallsBackWhenTheEntryIsBlank()
    {
        var config = new SlideshowConfig();
        config.Keys["next"] = "   ";
        Assert.Equal("Right", config.Key("next"));
        config.Keys["next"] = "Ctrl+N";
        Assert.Equal("Ctrl+N", config.Key("next"));
        Assert.Equal("missing", config.Key("missing"));
    }
}
