using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Reminder;

// The picture or GIF behind the alarm screen. WPF's Image shows only a GIF's first
// frame, so animated GIFs are composed frame by frame and played with a timer.
public static class AlarmBackdrop
{
    public const string Filter = "*.png;*.jpg;*.jpeg;*.gif;*.bmp";
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "data", "backgrounds");

    // Copies the file into the data folder, so moving or deleting the original
    // doesn't break the alarm. Returns the copy's path.
    public static string Import(string source)
    {
        Directory.CreateDirectory(Folder);
        string target = Path.Combine(Folder, "alarm" + Path.GetExtension(source).ToLowerInvariant());
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return target;
        foreach (var old in Directory.GetFiles(Folder, "alarm.*")) File.Delete(old);
        File.Copy(source, target);
        return target;
    }
    public static void Remove(string? path)
    {
        if (path != null && File.Exists(path) && string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase)) File.Delete(path);
    }

    // An image that fills the screen, or null when the file is missing or unreadable.
    public static Image? Create(string? path, double screenWidth)
    {
        if (path == null || !File.Exists(path)) return null;
        try {
            var image = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase) && Frames(path) is { Count: > 1 } frames) Play(image, frames);
            else {
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.UriSource = new Uri(path); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; bitmap.DecodePixelWidth = (int)Math.Min(screenWidth * 1.5, 3840);
                bitmap.EndInit(); bitmap.Freeze();
                image.Source = bitmap;
            }
            return image;
        } catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static void Play(Image image, List<(BitmapSource Frame, TimeSpan Delay)> frames)
    {
        int index = 0;
        image.Source = frames[0].Frame;
        var timer = new DispatcherTimer { Interval = frames[0].Delay };
        timer.Tick += (_, _) => { index = (index + 1) % frames.Count; image.Source = frames[index].Frame; timer.Interval = frames[index].Delay; };
        image.Loaded += (_, _) => timer.Start();
        image.Unloaded += (_, _) => timer.Stop();
    }

    // Each GIF frame may cover only part of the picture and says what happens to it
    // afterwards (its disposal), so frames are drawn onto the previous result.
    private static List<(BitmapSource Frame, TimeSpan Delay)> Frames(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count < 2) return [];
        int width = Query(decoder.Metadata, "/logscrdesc/Width", decoder.Frames[0].PixelWidth);
        int height = Query(decoder.Metadata, "/logscrdesc/Height", decoder.Frames[0].PixelHeight);
        // Keep all frames under about 400 MB by shrinking large, long GIFs.
        double scale = Math.Min(1, Math.Sqrt(400e6 / (4.0 * width * height * decoder.Frames.Count)));
        int outWidth = Math.Max(1, (int)(width * scale)), outHeight = Math.Max(1, (int)(height * scale));
        var full = new Rect(0, 0, width, height);
        var frames = new List<(BitmapSource, TimeSpan)>();
        BitmapSource? below = null;
        foreach (var frame in decoder.Frames)
        {
            var meta = frame.Metadata as BitmapMetadata;
            var area = new Rect(Query(meta, "/imgdesc/Left", 0), Query(meta, "/imgdesc/Top", 0), frame.PixelWidth, frame.PixelHeight);
            int delay = Query(meta, "/grctlext/Delay", 10) * 10, disposal = Query(meta, "/grctlext/Disposal", 0);
            var drawn = Render(outWidth, outHeight, scale, dc => { if (below != null) dc.DrawImage(below, full); dc.DrawImage(frame, area); });
            frames.Add((drawn, TimeSpan.FromMilliseconds(delay < 20 ? 100 : delay)));
            below = disposal switch {
                2 => Render(outWidth, outHeight, scale, dc => { dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(area))); dc.DrawImage(drawn, full); dc.Pop(); }),
                3 => below,
                _ => drawn,
            };
        }
        return frames;
    }
    private static BitmapSource Render(int width, int height, double scale, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) { dc.PushTransform(new ScaleTransform(scale, scale)); draw(dc); dc.Pop(); }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }
    private static int Query(ImageMetadata? metadata, string query, int fallback)
    {
        try { return metadata is BitmapMetadata meta && meta.GetQuery(query) is { } value ? Convert.ToInt32(value) : fallback; }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException) { return fallback; }
    }
}
