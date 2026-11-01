using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Reminder;

// Color themes. Each theme names a few base colors and the rest are mixed from them.
// Styles read the brushes with DynamicResource, so Apply recolors open windows too.
public static class Palette
{
    private sealed record Theme(string Key, bool Dark, string Accent, string Ink, string Muted, string Canvas, string Surface, string Line, string Danger, string Violet);
    private static readonly Theme[] Themes =
    [
        new("teal", false, "#0E7C6B", "#1B2430", "#6B7785", "#F3F5F7", "#FFFFFF", "#E2E7EB", "#B4442B", "#5B4BC4"),
        new("ocean", false, "#2563C9", "#18202E", "#687385", "#F2F5F9", "#FFFFFF", "#E0E6EE", "#B4442B", "#7A4BC4"),
        new("berry", false, "#B0336B", "#2A1E26", "#7A6B74", "#F8F3F5", "#FFFFFF", "#EEE2E7", "#B4442B", "#5B4BC4"),
        new("amber", false, "#B0650A", "#2B2219", "#7D7266", "#F8F5F0", "#FFFFFF", "#ECE5DA", "#B4442B", "#5B4BC4"),
        new("midnight", true, "#1F9E86", "#E6EDF2", "#93A1AE", "#10171D", "#18222A", "#26333D", "#F07A5E", "#9B8CF0"),
        new("graphite", true, "#5F6BE0", "#ECECEE", "#9A9AA3", "#141416", "#1E1E21", "#2E2E33", "#F07A5E", "#C08CF0"),
    ];
    private static Dictionary<string, Color> colors = Build(Themes[0]);

    public static IEnumerable<(string Key, Color Accent, Color Canvas)> All => Themes.Select(t => (t.Key, Parse(t.Accent), Parse(t.Canvas)));
    public static string Current { get; private set; } = Themes[0].Key;
    public static Color Get(string key) => colors[key];

    public static void Apply(string? key)
    {
        var theme = Themes.FirstOrDefault(t => t.Key == key) ?? Themes[0];
        Current = theme.Key;
        colors = Build(theme);
        var resources = Application.Current.Resources;
        foreach (var (name, color) in colors) resources[name] = Frozen(new SolidColorBrush(color));
        resources["Header"] = Frozen(new LinearGradientBrush(colors["HeaderStart"], colors["AccentDeep"], new Point(0, 0), new Point(1, 1)));
        // Lets the controls we don't restyle (scroll bars, the calendar) follow light or dark.
#pragma warning disable WPF0001
        Application.Current.ThemeMode = theme.Dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
    }

    private static Dictionary<string, Color> Build(Theme t)
    {
        Color accent = Parse(t.Accent), ink = Parse(t.Ink), surface = Parse(t.Surface), canvas = Parse(t.Canvas), danger = Parse(t.Danger), violet = Parse(t.Violet);
        double soft = t.Dark ? 0.2 : 0.12;
        return new()
        {
            ["Accent"] = accent, ["Ink"] = ink, ["Muted"] = Parse(t.Muted), ["Canvas"] = canvas, ["Surface"] = surface, ["Line"] = Parse(t.Line),
            ["Danger"] = danger, ["Violet"] = violet,
            ["AccentSoft"] = Mix(surface, accent, soft), ["AccentLine"] = Mix(surface, accent, 0.35), ["Selection"] = Mix(surface, accent, 0.3),
            ["AccentDeep"] = Scale(accent, 0.76), ["HeaderStart"] = Scale(accent, 1.17),
            ["DangerSoft"] = Mix(surface, danger, soft), ["VioletSoft"] = Mix(surface, violet, soft),
            ["Control"] = Mix(canvas, ink, 0.05), ["Hover"] = Mix(surface, ink, 0.07), ["Pressed"] = Mix(surface, ink, 0.14),
            ["FieldLine"] = Mix(surface, ink, 0.22), ["FieldLineHover"] = Mix(surface, ink, 0.4),
            // The alarm screen is always dark: a deep tint of the accent with a bright glow.
            ["AlarmCenter"] = Scale(accent, 0.68), ["AlarmEdge"] = Scale(accent, 0.15), ["AlarmButton"] = Scale(accent, 1.3),
            ["AlarmGlow"] = Mix(Scale(accent, 1.8), Colors.White, 0.3), ["AlarmSoft"] = Mix(accent, Colors.White, 0.78),
        };
    }
    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(Channel(a.R + (b.R - a.R) * amount), Channel(a.G + (b.G - a.G) * amount), Channel(a.B + (b.B - a.B) * amount));
    private static Color Scale(Color c, double factor) => Color.FromRgb(Channel(c.R * factor), Channel(c.G * factor), Channel(c.B * factor));
    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
    private static T Frozen<T>(T brush) where T : Freezable { brush.Freeze(); return brush; }
}
