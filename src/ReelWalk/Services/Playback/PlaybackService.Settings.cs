using System;
using System.Collections.Generic;
using System.Linq;
using ReelWalk.Models;
using ReelWalk.Services.Controls;

namespace ReelWalk.Services;
internal sealed partial class PlaybackService
{
    internal SlideshowConfig Config
    {
        get { return config; }
    }

    // Sets how long each photo stays, clamped to 1..60 seconds.
    // seconds is the new duration. Returns nothing.
    internal void ApplyDisplayDuration(double seconds)
    {
        if (double.IsNaN(seconds))
            return;
        if (seconds < 1) seconds = 1;
        if (seconds > 60) seconds = 60;
        displayDuration = seconds;
        config.DisplayDuration = seconds;
        UpdateTimerInterval();
    }

    // Sets transition length as a percent of display duration.
    // percent is clamped to 0..100. Returns nothing.
    internal void ApplyTransitionPercent(double percent)
    {
        if (double.IsNaN(percent))
            return;
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;
        transitionDurationPercent = percent;
        config.TransitionDurationPercent = percent;
    }

    // Sets Contain or Cover for stills and video.
    // fit is the toml value. Returns the applied fit.
    internal string ApplyImageFit(string fit)
    {
        bool cover = !string.IsNullOrEmpty(fit) &&
            (fit.Equals("Cover", StringComparison.OrdinalIgnoreCase) ||
             fit.Equals("Fill", StringComparison.OrdinalIgnoreCase));
        config.ImageFit = cover ? "Cover" : "Contain";
        return config.ImageFit;
    }

    // Sets video volume between 0 and 1.
    // volume may be a percent above 1. Returns nothing.
    internal void ApplyVideoVolume(double volume)
    {
        if (volume > 1.0)
            volume = volume / 100.0;
        SetVideoVolume(volume);
    }

    // Sets the media filter when that choice has files.
    // show is Both, Images, or Videos. Returns the label, or null when empty.
    internal string ApplyMediaShow(string show)
    {
        show = MediaTypes.NormalizeShow(show);
        var before = imageManager.GetCurrentImagePath();
        if (!imageManager.TrySetMediaShow(show))
            return null;
        config.MediaShow = imageManager.MediaShow;
        var after = imageManager.GetCurrentImagePath();
        if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
        {
            firstImage = true;
            ShowCurrentImage();
            ResetTimer();
        }
        return MediaTypes.ShowLabel(config.MediaShow);
    }

    // Turns Ken Burns on or off.
    // enabled is the new state. Returns nothing.
    internal void ApplyKenBurns(bool enabled)
    {
        transitionEngine.EnableKenBurns = enabled;
        config.EnableKenBurns = enabled;
    }

    // Sets how long the Ken Burns move lasts.
    // seconds below 2 uses 2. Returns nothing.
    internal void ApplyKenBurnsDuration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0.5)
            seconds = 10;
        if (seconds > 600)
            seconds = 600;
        config.KenBurnsDuration = seconds;
        transitionEngine.KenBurnsDuration = seconds;
    }

    // Sets the Ken Burns zoom ceiling.
    // zoom below 1 uses 1. Returns nothing.
    internal void ApplyKenBurnsMaxZoom(double zoom)
    {
        if (double.IsNaN(zoom) || zoom < 1)
            zoom = 1;
        if (zoom > 3)
            zoom = 3;
        config.KenBurnsMaxZoom = zoom;
        transitionEngine.KenBurnsMaxZoom = zoom;
    }

    // Replaces the transition pool from enabled effect names.
    // names may include KenBurns, which is handled separately. Returns nothing.
    internal void ApplyTransitionPool(IList<string> names)
    {
        var list = names == null ? new List<string>() : names.ToList();
        transitionEngine.TransitionPool = ParseTransitionPool(list);
        config.ActiveTransitions = list.ToList();
        if (config.EnableKenBurns &&
            !config.ActiveTransitions.Exists(x => x.Equals("KenBurns", StringComparison.OrdinalIgnoreCase)))
            config.ActiveTransitions.Add("KenBurns");
        if (!config.EnableKenBurns)
            config.ActiveTransitions.RemoveAll(x => x.Equals("KenBurns", StringComparison.OrdinalIgnoreCase));
    }

    // Sets how many Random back steps are remembered.
    // history is clamped to 1..500. Returns nothing.
    internal void ApplyBackHistory(int history)
    {
        if (history < 1)
            history = 1;
        if (history > 500)
            history = 500;
        config.BackHistory = history;
        imageManager.SetBackHistory(history);
    }

    // Sets how many files Page Up and Page Down jump.
    // skip is clamped to 1..500. Returns nothing.
    internal void ApplySkipCount(int skip)
    {
        if (skip < 1)
            skip = 1;
        if (skip > 500)
            skip = 500;
        config.SkipCount = skip;
    }

    // Sets the short video jump length.
    // seconds is clamped like the toml loader. Returns nothing.
    internal void ApplySeekSeconds(double seconds)
    {
        config.SeekSeconds = ClampSeek(seconds, 5);
    }

    // Sets the long video jump length.
    // seconds is clamped like the toml loader. Returns nothing.
    internal void ApplySeekFastSeconds(double seconds)
    {
        config.SeekFastSeconds = ClampSeek(seconds, 30);
    }

    // Stores a remapped key chord for action.
    // action is a ControlCatalog name. chord is toml text. Returns nothing.
    internal void ApplyKey(string action, string chord)
    {
        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(chord))
            return;
        if (config.Keys == null)
            config.Keys = ControlCatalog.Create();
        config.Keys[action] = chord.Trim();
    }

    // Clamps a seek length the same way as ConfigService.
    // value is the proposed seconds. Returns fallback when invalid.
    private static double ClampSeek(double value, double fallback)
    {
        if (double.IsNaN(value) || value < 0.5 || value > 600)
            return fallback;
        return value;
    }
}
