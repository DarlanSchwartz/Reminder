using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Reminder;

// MCI uses the native media device directly, without the optional WMP component
// required by WPF MediaPlayer. One device is open only while a sound is playing.
internal sealed class NativeAudioPlayer : IDisposable
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint mciSendString(string command, StringBuilder? result, int length, IntPtr callback);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorString(uint error, StringBuilder text, int length);
    private readonly string alias = "reminder" + Guid.NewGuid().ToString("N");
    private bool opened;
    private bool disposed;
    public void Play(string path, int volume = 800)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Stop();
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Arquivo de áudio não encontrado.", fullPath);
        if (fullPath.Contains('"')) throw new ArgumentException("Caminho de áudio inválido.");
        Send($"open \"{fullPath}\" type mpegvideo alias {alias}");
        opened = true;
        try { Send($"setaudio {alias} volume to {Math.Clamp(volume, 0, 1000)}"); Send($"play {alias} repeat"); }
        catch { Stop(); throw; }
    }
    public string Mode => opened ? Send($"status {alias} mode") : "closed";
    public string Position => opened ? Send($"status {alias} position") : "0";
    private static string Send(string command)
    {
        var result = new StringBuilder(512);
        uint error = mciSendString(command, result, result.Capacity, IntPtr.Zero);
        if (error != 0) { var message = new StringBuilder(512); mciGetErrorString(error, message, message.Capacity); throw new InvalidOperationException($"Áudio do Windows ({error}): {message}"); }
        return result.ToString();
    }
    public void Stop()
    {
        if (!opened) return;
        mciSendString($"close {alias}", null, 0, IntPtr.Zero);
        opened = false;
    }
    public void Dispose() { if (disposed) return; Stop(); disposed = true; }
}
