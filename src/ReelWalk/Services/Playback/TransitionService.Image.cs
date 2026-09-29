using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ReelWalk.Models;

namespace ReelWalk.Services;
internal sealed partial class TransitionService
{
    // Moves from the current picture or video to newImage.
    // transitionDur is the animation length. displayDuration feeds Ken Burns. Returns nothing.
    internal void TransitionToImage(Bitmap newImage, double transitionDur, double displayDuration)
    {
        if (newImage == null) return;

        CancelTransition();
        pendingVideoPath = null;

        if (videoIsActive)
            HideVideoImmediate();

        var outImg = ActiveImage();
        var outSc = ActiveScale();
        var outTr = ActiveTranslate();
        var inImg = StandbyImage();
        var inSc = StandbyScale();
        var inTr = StandbyTranslate();

        StopMotion();
        inSc.ScaleX = 1; inSc.ScaleY = 1;
        inTr.X = 0; inTr.Y = 0;
        inImg.Opacity = 0;
        inImg.Source = newImage;

        var type = PickTransition();
        LastTransition = type;

        if (type == TransitionType.None || transitionDur < 0.05)
        {
            ClearAll();
            outImg.Opacity = 0; outImg.Source = null;
            outSc.ScaleX = 1; outSc.ScaleY = 1; outTr.X = 0; outTr.Y = 0;
            inImg.Opacity = 1;
            image1IsActive = !image1IsActive;
            if (EnableKenBurns) StartKenBurns(inSc, inTr, displayDuration);
            return;
        }

        var dur = TimeSpan.FromSeconds(transitionDur);
        double sw = image1.Bounds.Width;
        if (sw < 1) sw = 1600;

        switch (type)
        {
            case TransitionType.MorphZoom:
                Run(inImg, Visual.OpacityProperty, 0, 1, dur, CubicInOut);
                Run(inSc, ScaleTransform.ScaleXProperty, 1.15, 1.0, dur, CubicInOut);
                Run(inSc, ScaleTransform.ScaleYProperty, 1.15, 1.0, dur, CubicInOut);
                FadeOut(outImg, dur, CubicInOut);
                Run(outSc, ScaleTransform.ScaleXProperty, outSc.ScaleX, 0.88, dur, CubicInOut);
                Run(outSc, ScaleTransform.ScaleYProperty, outSc.ScaleY, 0.88, dur, CubicInOut);
                break;

            case TransitionType.SoftWipe:
                Run(inImg, Visual.OpacityProperty, 0, 1, dur, CubicInOut);
                Run(inTr, TranslateTransform.XProperty, sw * 0.25, 0, dur, CubicInOut);
                FadeOut(outImg, dur, CubicInOut);
                Run(outTr, TranslateTransform.XProperty, outTr.X, -sw * 0.15, dur, CubicInOut);
                break;

            case TransitionType.ParallaxReveal:
                double drift = 60;
                bool goRight = random.Next(2) == 0;
                Run(inImg, Visual.OpacityProperty, 0, 1, TimeSpan.FromSeconds(transitionDur * 0.4), null);
                Run(inTr, TranslateTransform.XProperty, goRight ? -drift : drift, 0, dur, EaseInOut);
                FadeOut(outImg, dur, CubicInOut);
                Run(outSc, ScaleTransform.ScaleXProperty, outSc.ScaleX, 1.1, dur, CubicInOut);
                Run(outSc, ScaleTransform.ScaleYProperty, outSc.ScaleY, 1.1, dur, CubicInOut);
                Run(outTr, TranslateTransform.XProperty, outTr.X, goRight ? 120 : -120, dur, CubicInOut);
                break;

            case TransitionType.ScaleDissolve:
                Run(inImg, Visual.OpacityProperty, 0, 1, dur, EaseInOut);
                FadeOut(outImg, dur, EaseInOut);
                Run(outSc, ScaleTransform.ScaleXProperty, outSc.ScaleX, 1.25, dur, EaseInOut);
                Run(outSc, ScaleTransform.ScaleYProperty, outSc.ScaleY, 1.25, dur, EaseInOut);
                break;

            default:
                Run(inImg, Visual.OpacityProperty, 0, 1, dur, EaseInOut);
                FadeOut(outImg, dur, EaseInOut);
                break;
        }

        transitionInProgress = true;
        pendingInImg = inImg; pendingInSc = inSc; pendingInTr = inTr;
        pendingOutImg = outImg; pendingOutSc = outSc; pendingOutTr = outTr;
        pendingDisplayDur = displayDuration;
        completionTimer.Interval = TimeSpan.FromSeconds(transitionDur + 0.02);
        completionTimer.Start();
    }

    // Resets the outgoing layer after the animation and starts Ken Burns or the queued video.
    // Returns nothing.
    private void OnTransitionCompleted()
    {
        if (!transitionInProgress) return;
        transitionInProgress = false;

        if (pendingVideoPath != null)
        {
            string path = pendingVideoPath;
            pendingVideoPath = null;
            if (pendingOutImg != null)
            {
                pendingOutImg.Opacity = 0;
                pendingOutImg.Source = null;
            }
            video.Play(path);
            return;
        }

        if (pendingInImg == null)
        {
            if (pendingOutImg != null)
            {
                pendingOutImg.Opacity = 0;
                pendingOutImg.Source = null;
                pendingOutSc.ScaleX = 1; pendingOutSc.ScaleY = 1;
                pendingOutTr.X = 0; pendingOutTr.Y = 0;
            }
            return;
        }

        var capInImg = pendingInImg;
        var capInSc = pendingInSc;
        var capInTr = pendingInTr;
        var capOutImg = pendingOutImg;
        var capOutSc = pendingOutSc;
        var capOutTr = pendingOutTr;
        double capDisplay = pendingDisplayDur;

        capOutImg.Opacity = 0;
        capOutImg.Source = null;
        capOutSc.ScaleX = 1; capOutSc.ScaleY = 1;
        capOutTr.X = 0; capOutTr.Y = 0;
        capInImg.Opacity = 1;
        image1IsActive = !image1IsActive;

        if (EnableKenBurns)
            StartKenBurns(capInSc, capInTr, capDisplay);
        else
        {
            capInSc.ScaleX = 1; capInSc.ScaleY = 1;
            capInTr.X = 0; capInTr.Y = 0;
        }
    }

    // Slow pan and zoom on the visible still.
    // duration is the photo length; KenBurnsDuration overrides when set. Returns nothing.
    private void StartKenBurns(ScaleTransform scale, TranslateTransform translate, double duration)
    {
        double move = KenBurnsDuration > 0.5 ? KenBurnsDuration : duration;
        double dur = Math.Max(move, 2.0);
        var ts = TimeSpan.FromSeconds(dur);
        bool zoomIn = random.Next(2) == 0;
        double s1 = zoomIn ? KenBurnsMaxZoom : 1.0;
        double maxPan = 40;
        double x1 = 0, y1 = 0;
        switch (random.Next(5))
        {
            case 0: x1 = -maxPan; break;
            case 1: x1 = maxPan; break;
            case 2: y1 = -maxPan; break;
            case 3: y1 = maxPan; break;
        }

        Run(scale, ScaleTransform.ScaleXProperty, scale.ScaleX, s1, ts, EaseInOut);
        Run(scale, ScaleTransform.ScaleYProperty, scale.ScaleY, s1, ts, EaseInOut);
        Run(translate, TranslateTransform.XProperty, translate.X, x1, ts, EaseInOut);
        Run(translate, TranslateTransform.YProperty, translate.Y, y1, ts, EaseInOut);
    }

    // Picks one enabled transition at random.
    // Returns None when the pool is empty.
    private TransitionType PickTransition()
    {
        if (TransitionPool == null || TransitionPool.Count == 0)
            return TransitionType.None;
        return TransitionPool[random.Next(TransitionPool.Count)];
    }

    // Stops the completion timer.
    // Returns nothing.
    private void CancelTransition()
    {
        completionTimer.Stop();
        transitionInProgress = false;
    }

    // Stops animations and hides both image layers.
    // Returns nothing.
    private void ClearAll()
    {
        StopMotion();
        image1.Opacity = image1IsActive ? image1.Opacity : 0;
        image2.Opacity = image1IsActive ? 0 : image2.Opacity;
    }

    // Stops Ken Burns and any transition in progress.
    // Returns nothing.
    internal void StopAllAnimations()
    {
        CancelTransition();
        StopMotion();
        scale1.ScaleX = 1; scale1.ScaleY = 1; translate1.X = 0; translate1.Y = 0;
        scale2.ScaleX = 1; scale2.ScaleY = 1; translate2.X = 0; translate2.Y = 0;
        HideVideoImmediate();
    }

    // The image layer currently on screen.
    // Returns image1 or image2.
    private Image ActiveImage() { return image1IsActive ? image1 : image2; }

    // The image layer waiting for the next picture.
    // Returns the layer that is not active.
    private Image StandbyImage() { return image1IsActive ? image2 : image1; }

    // Scale transform of the visible image.
    // Returns the matching transform.
    private ScaleTransform ActiveScale() { return image1IsActive ? scale1 : scale2; }

    // Scale transform of the incoming image.
    // Returns the matching transform.
    private ScaleTransform StandbyScale() { return image1IsActive ? scale2 : scale1; }

    // Translate transform of the visible image.
    // Returns the matching transform.
    private TranslateTransform ActiveTranslate() { return image1IsActive ? translate1 : translate2; }

    // Translate transform of the incoming image.
    // Returns the matching transform.
    private TranslateTransform StandbyTranslate() { return image1IsActive ? translate2 : translate1; }

    // Cancels running animations.
    // Returns nothing.
    private void StopMotion()
    {
        motion.Cancel();
        motion.Dispose();
        motion = new CancellationTokenSource();
    }

    // Starts an animation on a double property.
    // ease may be null. Returns nothing.
    private void Run(Animatable target, AvaloniaProperty<double> prop, double from, double to, TimeSpan dur, Easing ease)
    {
        var animation = new Animation
        {
            Duration = dur,
            FillMode = FillMode.Forward,
            Easing = ease ?? new LinearEasing(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(prop, from) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(prop, to) }
                }
            }
        };
        Start(animation, target, motion.Token);
    }

    // Runs animation and ignores a cancel.
    // token stops it. Returns nothing.
    private static async void Start(Animation animation, Animatable target, CancellationToken token)
    {
        try
        {
            await animation.RunAsync(target, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Fades target to invisible.
    // dur is the length. ease may be null. Returns nothing.
    private void FadeOut(Visual target, TimeSpan dur, Easing ease)
    {
        Run(target, Visual.OpacityProperty, target.Opacity, 0, dur, ease);
    }
}
