using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Security.Cryptography;
using System.Windows.Threading;

namespace Reminder;

public sealed class SoundLibrary
{
    private readonly string directory;
    public SoundLibrary(string directory) => this.directory = directory;
    public string Import(string source)
    {
        using var input = File.OpenRead(source);
        string hash = Convert.ToHexString(SHA256.HashData(input));
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, hash + Path.GetExtension(source).ToLowerInvariant());
        if (!File.Exists(target)) { input.Position = 0; using var output = File.Create(target); input.CopyTo(output); }
        return target;
    }
    // A random MP3/WAV from a folder, or null when there is none to play.
    public static string? PickRandom(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        try {
            var files = Directory.EnumerateFiles(folder).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".mp3" or ".wav").ToArray();
            return files.Length == 0 ? null : files[Random.Shared.Next(files.Length)];
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}

public sealed class AlarmAudio : IDisposable
{
    private readonly NativeAudioPlayer player = new();
    private readonly DispatcherTimer fallback = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool disposed;
    public string? LastError { get; private set; }
    public event Action? PlaybackFailed;
    public AlarmAudio()
    {
        fallback.Tick += (_, _) => SystemSounds.Exclamation.Play();
    }
    public void Play(string? path)
    {
        if (disposed) throw new ObjectDisposedException(nameof(AlarmAudio));
        fallback.Stop(); player.Stop(); LastError = null;
        if (string.IsNullOrWhiteSpace(path)) { UseFallback(false); return; }
        try { player.Play(path); }
        catch (Exception ex) { LastError = ex.Message; UseFallback(true); }
    }
    private void UseFallback(bool failed)
    {
        if (disposed || fallback.IsEnabled) return;
        player.Stop(); SystemSounds.Exclamation.Play(); fallback.Start();
        if (failed) PlaybackFailed?.Invoke();
    }
    public void Dispose() { if (disposed) return; disposed = true; fallback.Stop(); player.Dispose(); }
}
