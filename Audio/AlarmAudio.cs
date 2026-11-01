using System;
using System.Media;
using System.Windows.Threading;

namespace Reminder;

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
