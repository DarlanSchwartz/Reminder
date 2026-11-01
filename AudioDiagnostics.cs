using System;
using System.IO;

namespace Reminder;

internal static class AudioDiagnostics
{
    public static int Run(string path)
    {
        using var player = new NativeAudioPlayer();
        string report;
        int exitCode = 1;
        try {
            player.Play(path, 0);
            System.Threading.Thread.Sleep(600);
            string mode = player.Mode, position = player.Position;
            player.Stop();
            report = $"Mode={mode}; Position={position}; AfterStop={player.Mode}";
            exitCode = mode == "playing" && int.TryParse(position, out int milliseconds) && milliseconds > 0 && player.Mode == "closed" ? 0 : 1;
        }
        catch (Exception ex) { report = ex.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "audio-diagnostic.txt"), report);
        return exitCode;
    }
}
