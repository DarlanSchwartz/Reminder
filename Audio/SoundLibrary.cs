using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

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
        if (File.Exists(target)) return target;
        // Copy under a temporary name first: the hash name is trusted as complete, so
        // an interrupted copy must never be left behind under it.
        string temp = target + ".tmp";
        input.Position = 0;
        using (var output = File.Create(temp)) input.CopyTo(output);
        File.Move(temp, target, overwrite: true);
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
