using System;
using Avalonia.Threading;

namespace ReelWalk.Services;
internal sealed class VideoSeekService
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(70);

    private readonly SlideVideo media;
    private readonly Func<bool> userPaused;
    private readonly DispatcherTimer quietTimer;

    private TimeSpan? desired;
    private TimeSpan? landed;
    private DateTime landedAt;
    private bool scrubHeld;

    internal event Action<TimeSpan> PreviewChanged;

    // Coalesces seeks and applies them with a short jump, not a long freeze.
    // userPaused keeps a user pause after a seek. Returns nothing.
    internal VideoSeekService(SlideVideo media, Func<bool> userPaused)
    {
        this.media = media;
        this.userPaused = userPaused;
        quietTimer = new DispatcherTimer { Interval = QuietPeriod };
        quietTimer.Tick += OnQuiet;
    }

    internal bool IsAdjusting
    {
        get { return desired.HasValue || scrubHeld; }
    }

    internal TimeSpan DisplayPosition
    {
        get
        {
            if (desired.HasValue)
                return desired.Value;
            if (landed.HasValue && media != null)
            {
                double delta = Math.Abs((media.Position - landed.Value).TotalSeconds);
                bool stillSettling = (DateTime.UtcNow - landedAt).TotalSeconds < 1.2;
                if (delta > 0.35 && stillSettling)
                    return landed.Value;
                landed = null;
            }
            return media != null ? media.Position : TimeSpan.Zero;
        }
    }

    // Queues a jump of seconds from the position on screen.
    // seconds may be negative. Returns nothing.
    internal void Nudge(double seconds)
    {
        if (media == null || !media.HasMedia)
            return;

        var origin = desired ?? (landed ?? media.Position);
        Arm(origin + TimeSpan.FromSeconds(seconds), false);
    }

    // Queues a jump to a fraction of the video length.
    // fraction is 0 to 1. Returns nothing when the length is unknown.
    internal void MoveToFraction(double fraction)
    {
        if (media == null || !media.HasMedia || !media.HasLength)
            return;

        if (fraction < 0) fraction = 0;
        if (fraction > 1) fraction = 1;

        var dur = media.Length;
        Arm(TimeSpan.FromTicks((long)(dur.Ticks * fraction)), true);
    }

    // Defers writing the position while the pointer is down.
    // held true starts a drag. Returns nothing.
    internal void SetScrubHeld(bool held)
    {
        scrubHeld = held;
        if (!held && desired.HasValue)
            ApplyDesired();
    }

    // Drops a pending seek.
    // Returns nothing.
    internal void Cancel()
    {
        quietTimer.Stop();
        desired = null;
        landed = null;
        scrubHeld = false;
    }

    // Stores the next target and applies it after a short quiet, or right away on scrub release.
    // fromScrub true is a drag on the seek bar. Returns nothing.
    private void Arm(TimeSpan next, bool fromScrub)
    {
        next = Clamp(next);
        desired = next;

        var handler = PreviewChanged;
        if (handler != null)
            handler(next);

        if (fromScrub && scrubHeld)
        {
            quietTimer.Stop();
            return;
        }

        quietTimer.Stop();
        quietTimer.Start();
    }

    // Applies the pending seek after the quiet period.
    // Returns nothing.
    private void OnQuiet(object sender, EventArgs e)
    {
        quietTimer.Stop();
        if (scrubHeld)
            return;
        ApplyDesired();
    }

    // Writes the pending time to the player.
    // Returns nothing.
    private void ApplyDesired()
    {
        quietTimer.Stop();
        if (media == null || !desired.HasValue)
            return;

        var dest = Clamp(desired.Value);
        desired = null;
        landed = dest;
        landedAt = DateTime.UtcNow;
        media.SeekTo(dest);

        if (userPaused != null && userPaused())
        {
            try { media.Pause(); }
            catch { }
        }
    }

    // Keeps value inside the current video.
    // Returns TimeSpan.Zero when the value is negative.
    private TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;
        if (media != null && media.HasLength && value > media.Length)
            return media.Length;
        return value;
    }
}
