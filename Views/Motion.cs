using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Reminder;

// Small, consistent motion vocabulary. Everything is skipped when Windows
// animations are turned off (Settings → Accessibility → Visual effects).
internal static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;
    private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };

    // Holds `from` until `delay`, then eases to `to`, so staggered items stay hidden while waiting.
    private static DoubleAnimationUsingKeyFrames Tween(double from, double to, int ms, int delay = 0, IEasingFunction? ease = null)
    {
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delay > 0) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + ms)), ease ?? Out));
        return a;
    }

    // Fades in while sliding from (x, y) to its place.
    public static void Enter(UIElement e, double x = 0, double y = 12, int ms = 260, int delay = 0)
    {
        if (!Enabled) return;
        var move = new TranslateTransform(); e.RenderTransform = move;
        e.BeginAnimation(UIElement.OpacityProperty, Tween(0, 1, ms, delay));
        if (x != 0) move.BeginAnimation(TranslateTransform.XProperty, Tween(x, 0, ms, delay));
        if (y != 0) move.BeginAnimation(TranslateTransform.YProperty, Tween(y, 0, ms, delay));
    }

    // Fades out while sliding to (x, y), then runs `done`.
    public static void Exit(UIElement e, Action done, double x = 0, double y = 10, int ms = 170)
    {
        if (!Enabled) { done(); return; }
        var move = new TranslateTransform(); e.RenderTransform = move;
        var fade = Tween(e.Opacity, 0, ms, ease: new CubicEase { EasingMode = EasingMode.EaseIn });
        fade.Completed += (_, _) => done();
        if (x != 0) move.BeginAnimation(TranslateTransform.XProperty, Tween(0, x, ms));
        if (y != 0) move.BeginAnimation(TranslateTransform.YProperty, Tween(0, y, ms));
        e.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // Grows from `from` to full size around `origin` with a slight overshoot.
    public static void Zoom(UIElement e, double from = 0.94, Point? origin = null, int ms = 320)
    {
        if (!Enabled) return;
        var scale = new ScaleTransform(); e.RenderTransform = scale;
        e.RenderTransformOrigin = origin ?? new Point(0.5, 0.5);
        e.BeginAnimation(UIElement.OpacityProperty, Tween(0, 1, ms * 2 / 3));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Tween(from, 1, ms, ease: Spring));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Tween(from, 1, ms, ease: Spring));
    }

    // Collapses an element's height to zero, then runs `done`.
    public static void Collapse(FrameworkElement e, Action done, int ms = 160)
    {
        if (!Enabled) { done(); return; }
        var shrink = Tween(e.ActualHeight, 0, ms);
        shrink.Completed += (_, _) => done();
        e.BeginAnimation(FrameworkElement.HeightProperty, shrink);
    }

    // A quick horizontal "no" shake for validation errors.
    public static void Shake(UIElement e)
    {
        if (!Enabled) return;
        var move = new TranslateTransform(); e.RenderTransform = move;
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
        foreach (var (ms, x) in new[] { (0, 0), (60, -7), (120, 6), (180, -4), (240, 3), (300, -1), (360, 0) })
            a.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        move.BeginAnimation(TranslateTransform.XProperty, a);
    }

    // Animates a progress bar segment filling from the left.
    public static void Fill(UIElement e, int delay = 0)
    {
        if (!Enabled) return;
        var scale = new ScaleTransform(0, 1); e.RenderTransform = scale; e.RenderTransformOrigin = new Point(0, 0.5);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Tween(0, 1, 320, delay));
    }

    // Slides/resizes an element (e.g. a tab highlight) to a new spot.
    public static void Glide(FrameworkElement e, double x, double width, bool animate)
    {
        var move = e.RenderTransform as TranslateTransform ?? (TranslateTransform)(e.RenderTransform = new TranslateTransform());
        if (!animate || !Enabled)
        {
            move.BeginAnimation(TranslateTransform.XProperty, null); e.BeginAnimation(FrameworkElement.WidthProperty, null);
            move.X = x; e.Width = width; return;
        }
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(260)) { EasingFunction = Spring });
        e.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(260)) { EasingFunction = Out });
    }

    // Every button gives a little under the pointer and springs back on release.
    public static void RegisterPressFeedback()
    {
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, _) => Press((UIElement)s, 0.95)), true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((s, _) => Press((UIElement)s, 1)), true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseLeaveEvent, new MouseEventHandler((s, _) => Press((UIElement)s, 1)), true);
    }
    private static void Press(UIElement e, double to)
    {
        if (!Enabled) return;
        if (e.RenderTransform is not ScaleTransform scale)
        {
            if (to == 1) return;
            e.RenderTransform = scale = new ScaleTransform(); e.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(to < 1 ? 90 : 220)) { EasingFunction = to < 1 ? Out : Spring };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }
}
