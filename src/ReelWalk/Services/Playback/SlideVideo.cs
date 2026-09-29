using System;
using Avalonia;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace ReelWalk.Services;
internal sealed class SlideVideo : IDisposable
{
    private readonly LibVLC libVlc;
    private readonly MediaPlayer player;
    private int session;
    private bool eventsArmed;
    private Media current;
    private readonly LibVLCSharp.Avalonia.VideoView view;
    private bool cover;
    private bool surfaceOn;
    private bool obscured;
    private bool released;

    internal event EventHandler Ended;
    internal event EventHandler Failed;
    internal event EventHandler Opened;

    // Starts LibVLC and the player shown in view.
    // view is the Avalonia video surface. Returns nothing.
    internal SlideVideo(LibVLCSharp.Avalonia.VideoView view)
    {
        this.view = view;
        var options = new System.Collections.Generic.List<string>
        {
            "--no-video-title-show",
            "--no-osd",
            "--file-caching=300",
            "--network-caching=1000",
            "--input-fast-seek"
        };
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows))
            options.Add("--avcodec-hw=d3d11va");
        libVlc = new LibVLC(options.ToArray());
        player = new MediaPlayer(libVlc);
        view.MediaPlayer = player;
        player.EndReached += OnEndReached;
        player.EncounteredError += OnError;
        player.Playing += OnPlaying;
    }

    internal MediaPlayer Player { get { return player; } }

    internal bool HasMedia { get { return player.Media != null; } }

    internal bool HasLength { get { return player.Length > 0; } }

    internal TimeSpan Position
    {
        get { return TimeSpan.FromMilliseconds(Math.Max(0, player.Time)); }
    }

    internal bool IsSeekable
    {
        get { return player.IsSeekable; }
    }

    // Jumps to position without a long pause.
    // Returns nothing when the player cannot seek yet.
    internal void SeekTo(TimeSpan position)
    {
        if (released || player.Media == null)
            return;

        long ms = (long)Math.Max(0, position.TotalMilliseconds);
        long length = player.Length;
        if (length > 0 && ms > length)
            ms = length;

        try
        {
            if (!player.IsSeekable)
                return;

            bool playing = player.IsPlaying;
            if (playing)
                player.SetPause(true);

            player.Time = ms;
            if (length > 0)
                player.Position = (float)(ms / (double)length);

            if (playing)
                player.SetPause(false);
        }
        catch { }
    }

    internal TimeSpan Length
    {
        get { return TimeSpan.FromMilliseconds(Math.Max(0, player.Length)); }
    }

    internal double Volume
    {
        get { return player.Volume / 100.0; }
        set
        {
            int scaled = (int)Math.Round(value * 100);
            if (scaled < 0) scaled = 0;
            if (scaled > 100) scaled = 100;
            player.Volume = scaled;
        }
    }

    // Letterboxes or fills the frame.
    // cover true fills the screen. Returns nothing.
    internal void SetCover(bool cover)
    {
        this.cover = cover;
        player.AspectRatio = cover ? "" : null;
        player.Scale = 0;
    }

    // Listens for open, end, and failure.
    // Returns nothing.
    internal void Hook()
    {
        eventsArmed = true;
    }

    // Ignores end and failure from a stop or a source change.
    // Returns nothing.
    internal void Unhook()
    {
        eventsArmed = false;
        session++;
    }

    // Hides the native video so Avalonia panels can draw on top.
    // obscured true is help, info, or another overlay. Returns nothing.
    internal void SetObscured(bool obscured)
    {
        this.obscured = obscured;
        ApplySurface();
    }

    // Plays path from the start.
    // Returns nothing.
    internal void Play(string path)
    {
        var media = new Media(libVlc, path, FromType.FromPath);
        media.AddOption(":file-caching=300");
        media.AddOption(":network-caching=1000");
        media.AddOption(":input-fast-seek");
        surfaceOn = true;
        ApplySurface();
        player.Play(media);
        if (current != null)
            current.Dispose();
        current = media;
    }

    // Pauses the current file.
    // Returns nothing.
    internal void Pause()
    {
        player.SetPause(true);
    }

    // Continues the current file.
    // Returns nothing.
    internal void Resume()
    {
        player.SetPause(false);
    }

    // Stops playback and drops the media.
    // Returns nothing. Call Unhook first so the stop is not a real ending.
    internal void Stop()
    {
        if (released)
            return;
        surfaceOn = false;
        try { ApplySurface(); }
        catch { }
        try { player.Stop(); }
        catch { }
    }

    // Shows the video, leaving a strip for the seek bar, unless an overlay is open.
    // Returns nothing.
    private void ApplySurface()
    {
        if (released)
            return;
        bool show = surfaceOn && !obscured;
        view.IsVisible = show;
        view.Margin = new Thickness(0, 0, 0, show ? 48 : 0);
    }

    // Detaches the video from the window, then releases LibVLC.
    // Safe to call more than once. Returns nothing.
    public void Dispose()
    {
        if (released)
            return;
        released = true;
        surfaceOn = false;
        Unhook();
        try { player.EndReached -= OnEndReached; } catch { }
        try { player.EncounteredError -= OnError; } catch { }
        try { player.Playing -= OnPlaying; } catch { }
        try { view.MediaPlayer = null; } catch { }
        try { player.Stop(); } catch { }
        if (current != null)
        {
            try { current.Dispose(); } catch { }
            current = null;
        }
        try { player.Dispose(); } catch { }
        try { libVlc.Dispose(); } catch { }
    }

    // Forwards the end event after it leaves the LibVLC thread.
    // Returns nothing.
    private void OnEndReached(object sender, EventArgs e)
    {
        Post(Ended);
    }

    // Forwards a playback failure.
    // Returns nothing.
    private void OnError(object sender, EventArgs e)
    {
        Post(Failed);
    }

    // Forwards the moment the file is actually playing.
    // Returns nothing.
    private void OnPlaying(object sender, EventArgs e)
    {
        if (cover)
            player.Scale = 0;
        Post(Opened);
    }

    // Raises handler on the UI thread when events are still armed.
    // Returns nothing.
    private void Post(EventHandler handler)
    {
        int mine = session;
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!eventsArmed || mine != session || handler == null)
                    return;
                handler(this, EventArgs.Empty);
            });
        }
        catch (InvalidOperationException)
        {
        }
    }
}
