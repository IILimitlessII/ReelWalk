using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ReelWalk.Models;

namespace ReelWalk.Services;
internal sealed partial class TransitionService
{
    private readonly Image image1;
    private readonly Image image2;
    private readonly ScaleTransform scale1, scale2;
    private readonly TranslateTransform translate1, translate2;
    private readonly SlideVideo video;

    private bool image1IsActive = true;
    private readonly Random random = new Random();
    private bool videoIsActive;
    private bool transitionInProgress;
    private Image pendingInImg, pendingOutImg;
    private ScaleTransform pendingInSc, pendingOutSc;
    private TranslateTransform pendingInTr, pendingOutTr;
    private double pendingDisplayDur;
    private string pendingVideoPath;
    private readonly DispatcherTimer completionTimer;
    private CancellationTokenSource motion = new CancellationTokenSource();

    private static readonly Easing EaseInOut = new QuadraticEaseInOut();
    private static readonly Easing CubicInOut = new CubicEaseInOut();

    internal bool EnableKenBurns { get; set; }
    internal double KenBurnsMaxZoom { get; set; }
    internal double KenBurnsDuration { get; set; }
    internal double VideoVolume { get; set; }
    internal List<TransitionType> TransitionPool { get; set; }
    internal TransitionType LastTransition { get; private set; }
    internal bool VideoIsActive { get { return videoIsActive; } }

    // Binds the two image layers, the video player, and one reused completion timer.
    // Returns nothing.
    internal TransitionService(
        Image img1, Image img2,
        ScaleTransform s1, ScaleTransform s2,
        TranslateTransform t1, TranslateTransform t2,
        SlideVideo video)
    {
        image1 = img1;
        image2 = img2;
        scale1 = s1;
        scale2 = s2;
        translate1 = t1;
        translate2 = t2;
        this.video = video;

        EnableKenBurns = true;
        KenBurnsMaxZoom = 1.3;
        KenBurnsDuration = 10.0;
        VideoVolume = 1.0;
        TransitionPool = new List<TransitionType> {
            TransitionType.Crossfade,
            TransitionType.MorphZoom,
            TransitionType.SoftWipe,
            TransitionType.ParallaxReveal,
            TransitionType.ScaleDissolve
        };

        completionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        completionTimer.Tick += CompletionTimer_Tick;
    }

    // Finishes the transition that the timer was armed for.
    // Returns nothing.
    private void CompletionTimer_Tick(object sender, EventArgs e)
    {
        completionTimer.Stop();
        OnTransitionCompleted();
    }

    // Shows bitmap with no transition and starts Ken Burns when that effect is on.
    // displayDuration is how long the still stays. Returns nothing.
    internal void ShowFirstImage(Bitmap bitmap, double displayDuration)
    {
        if (bitmap == null) return;

        CancelTransition();
        ClearAll();
        HideVideoImmediate();
        LastTransition = TransitionType.None;

        var img = ActiveImage();
        var sc = ActiveScale();
        var tr = ActiveTranslate();

        img.Source = bitmap;
        img.Opacity = 1;

        var other = StandbyImage();
        other.Opacity = 0;
        other.Source = null;

        if (EnableKenBurns)
            StartKenBurns(sc, tr, displayDuration);
    }

    // Starts path at once, with both image layers hidden.
    // Returns nothing.
    internal void ShowFirstVideo(string path)
    {
        CancelTransition();
        ClearAll();
        LastTransition = TransitionType.None;
        pendingVideoPath = null;

        image1.Opacity = 0; image1.Source = null;
        image2.Opacity = 0; image2.Source = null;

        video.Volume = VideoVolume;
        video.Play(path);
        videoIsActive = true;
    }

    // Stops the video before a still is shown.
    // Returns nothing. Callers detach media events first so the stop is not a real ending.
    internal void HideVideoImmediate()
    {
        if (video == null) return;
        video.Stop();
        videoIsActive = false;
        pendingVideoPath = null;
    }

    // Stops playback for shutdown.
    // Returns nothing.
    internal void StopVideo()
    {
        if (video == null) return;
        video.Stop();
    }
}
