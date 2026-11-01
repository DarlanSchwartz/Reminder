using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Reminder;

public sealed class AlarmWindow : Window
{
    private readonly AlarmAudio audio = new();
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private static readonly FontFamily Icons = new("Segoe Fluent Icons");
    public AlarmWindow(TaskItem task, TaskService service, bool preview = false)
    {
        Title = Loc.T("AlarmTitle"); Language = Loc.Xml;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowInTaskbar = true;
        var area = SystemParameters.WorkArea;
        Left = area.Left; Top = area.Top; Width = area.Width; Height = area.Height;
        Background = new RadialGradientBrush(Color.FromRgb(20, 84, 74), Color.FromRgb(7, 18, 22)) { Center = new Point(0.5, 0.4), GradientOrigin = new Point(0.5, 0.4), RadiusX = 0.85, RadiusY = 0.85 };
        Foreground = Brushes.White;
        var mint = new SolidColorBrush(Color.FromRgb(126, 226, 196));
        var soft = new SolidColorBrush(Color.FromRgb(190, 212, 205));
        var glass = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));

        var root = new Grid();
        // Live clock in the top-left corner.
        var clockText = new TextBlock { FontSize = 46, FontWeight = FontWeights.Light };
        var dateText = new TextBlock { FontSize = 15, Foreground = soft };
        var corner = new StackPanel { Margin = new Thickness(48, 36, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        corner.Children.Add(clockText); corner.Children.Add(dateText);
        void Tick() { clockText.Text = DateTime.Now.ToString("HH:mm"); string d = DateTime.Now.ToString(Loc.T("fmt.Clock"), Loc.Culture); dateText.Text = char.ToUpper(d[0]) + d[1..]; }
        Tick(); clock.Tick += (_, _) => Tick();
        root.Children.Add(corner);

        var panel = new StackPanel { MaxWidth = 820, Margin = new Thickness(50), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        // Bell inside softly pulsing rings.
        var badge = new Grid { Width = 180, Height = 180, HorizontalAlignment = HorizontalAlignment.Center };
        var rings = new[] { NewRing(mint, 120), NewRing(mint, 120) };
        foreach (var ring in rings) badge.Children.Add(ring);
        badge.Children.Add(new Ellipse { Width = 104, Height = 104, Fill = new SolidColorBrush(Color.FromArgb(50, 126, 226, 196)), Effect = new DropShadowEffect { Color = Color.FromRgb(126, 226, 196), BlurRadius = 40, ShadowDepth = 0, Opacity = 0.5 } });
        var bell = new TextBlock { Text = "", FontFamily = Icons, FontSize = 44, Foreground = mint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.1), RenderTransform = new RotateTransform() };
        badge.Children.Add(bell);
        panel.Children.Add(badge);

        var when = task.AlarmAt?.LocalDateTime ?? DateTime.Now;
        panel.Children.Add(new TextBlock { Text = Loc.T(preview ? "AlarmPreview" : "ReminderCaps") + "  ·  " + when.ToString("HH:mm"), Foreground = mint, FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(new TextBlock { Text = task.Title, FontSize = 60, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 12), TextWrapping = TextWrapping.Wrap, MaxHeight = 260, TextTrimming = TextTrimming.CharacterEllipsis });
        if (task.Description.Length > 0)
            panel.Children.Add(new TextBlock { Text = task.Description, FontSize = 20, Foreground = soft, TextAlignment = TextAlignment.Center, MaxHeight = 110, TextTrimming = TextTrimming.CharacterEllipsis });

        string? song = task.SoundFolder != null ? SoundLibrary.PickRandom(task.SoundFolder) ?? task.SoundPath : task.SoundPath;
        var pills = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
        if (task.Repeat is { } repeat)
        {
            var next = repeat.Next(task.AlarmAt > DateTimeOffset.Now ? task.AlarmAt.Value : DateTimeOffset.Now);
            pills.Children.Add(Pill("", repeat.ShortLabel + (next is { } n ? Loc.F("NextOccurrence", n.LocalDateTime.ToString(Loc.T("fmt.Short"), Loc.Culture)) : Loc.T("LastAlert")), glass, soft));
        }
        var musicPill = Pill("", song == null ? Loc.T("DefaultSound") : task.SoundFolder != null || task.SoundName == null ? System.IO.Path.GetFileNameWithoutExtension(song) : System.IO.Path.GetFileNameWithoutExtension(task.SoundName), glass, soft);
        pills.Children.Add(musicPill);
        panel.Children.Add(pills);

        var done = new Button { Content = Loc.T(preview ? "ClosePreview" : task.Repeat != null ? "CompleteOccurrence" : "CompleteTask"), Style = (Style)FindResource("Primary"), Background = new SolidColorBrush(Color.FromRgb(22, 160, 133)), Padding = new Thickness(36, 18, 36, 18), Margin = new Thickness(0, 40, 0, 0), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 300 };
        done.Click += (_, _) => Execute(() => { if (!preview) service.CompleteAlert(task); Close(); });
        panel.Children.Add(done);
        if (!preview) {
            var snoozeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            snoozeRow.Children.Add(new TextBlock { Text = Loc.T("Snooze"), Foreground = soft, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            foreach (int minutes in new[] { 5, 10, 30 })
            {
                var snooze = new Button { Content = Loc.F("Minutes", minutes), Background = glass, Foreground = Brushes.White, Padding = new Thickness(18, 11, 18, 11), Margin = new Thickness(4, 0, 4, 0), FontSize = 14 };
                snooze.Click += (_, _) => Execute(() => { service.Snooze(task.Id, minutes); Close(); });
                snoozeRow.Children.Add(snooze);
            }
            panel.Children.Add(snoozeRow);
        }
        var dismiss = new Button { Content = Loc.T("Dismiss"), HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent, Foreground = soft, Margin = new Thickness(0, 14, 0, 0) };
        dismiss.Click += (_, _) => Close(); panel.Children.Add(dismiss);
        var audioStatus = new TextBlock { Foreground = soft, TextAlignment = TextAlignment.Center, FontSize = 13, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(audioStatus);
        var scroller = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroller);
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        audio.PlaybackFailed += () => audioStatus.Text = Loc.T("SongFailed");
        Loaded += (_, _) => {
            NativeWindow.Activate(this); done.Focus(); audio.Play(song); clock.Start();
            Motion.Zoom(scroller, 0.86, ms: 520); Motion.Enter(corner, x: -20, y: 0, ms: 420, delay: 150);
            for (int i = 0; i < pills.Children.Count; i++) Motion.Enter(pills.Children[i], y: 10, ms: 320, delay: 280 + i * 80);
            if (!SystemParameters.ClientAreaAnimation) return;
            // Shake the whole card every 3 seconds for as long as the alarm is ringing.
            var movement = new TranslateTransform(); panel.RenderTransform = movement;
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3), RepeatBehavior = RepeatBehavior.Forever };
            foreach (var (ms, x) in new[] { (0, 0), (70, -14), (140, 14), (220, -11), (300, 11), (400, -6), (500, 6), (600, 0), (3000, 0) })
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            movement.BeginAnimation(TranslateTransform.XProperty, shake);
            var ringing = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3), RepeatBehavior = RepeatBehavior.Forever };
            foreach (var (ms, angle) in new[] { (0, 0), (80, 16), (160, -14), (240, 12), (320, -9), (400, 6), (480, -3), (560, 0), (3000, 0) })
                ringing.KeyFrames.Add(new LinearDoubleKeyFrame(angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            ((RotateTransform)bell.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, ringing);
            // Two rings expand and fade, offset by half a cycle.
            for (int i = 0; i < rings.Length; i++)
            {
                var begin = TimeSpan.FromSeconds(1.5 * i);
                var grow = new DoubleAnimation(1, 1.5, TimeSpan.FromSeconds(3)) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var fade = new DoubleAnimation(0.7, 0, TimeSpan.FromSeconds(3)) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever };
                var scale = (ScaleTransform)rings[i].RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                rings[i].BeginAnimation(OpacityProperty, fade);
            }
        };
        Closed += (_, _) => { clock.Stop(); audio.Dispose(); };
    }
    private static Ellipse NewRing(Brush stroke, double size) => new() { Width = size, Height = size, Stroke = stroke, StrokeThickness = 2, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform() };
    private static Border Pill(string glyph, string text, Brush background, Brush foreground)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 12, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
        row.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = foreground, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420, TextWrapping = TextWrapping.NoWrap });
        return new Border { Background = background, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(4), Child = row };
    }
    private void Execute(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, Loc.T("ChangeFailed") + "\n" + ex.Message, "Reminder"); } }
}
