using CommunityToolkit.Mvvm.ComponentModel;

namespace ReelWalk.Models;
internal sealed partial class KeybindItem : ObservableObject
{
    internal KeybindItem(string name, string title, string chord)
    {
        Name = name;
        Title = title;
        Chord = chord;
    }

    internal string Name { get; }
    internal string Title { get; }

    [ObservableProperty] private string _chord;
    [ObservableProperty] private bool _isCapturing;

    // Text shown on the remap button.
    // Returns a capture prompt while listening.
    internal string ButtonLabel
    {
        get { return IsCapturing ? "Press a key…" : Chord; }
    }

    partial void OnChordChanged(string value)
    {
        OnPropertyChanged(nameof(ButtonLabel));
    }

    partial void OnIsCapturingChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonLabel));
    }
}
