using System;
using System.IO;
using System.Text;

namespace Reminder;

// The app is a Windows (GUI) executable: write to redirected handles when an agent
// captures the output, or attach to the calling console when run interactively.
internal static class ConsoleOutput
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    public static void Write(string text, bool error)
    {
        var handle = GetStdHandle(error ? -12 : -11);
        bool attached = (handle == IntPtr.Zero || handle == new IntPtr(-1)) && AttachConsole(-1);
        using var writer = new StreamWriter(error ? Console.OpenStandardError() : Console.OpenStandardOutput(), attached ? Console.OutputEncoding : new UTF8Encoding(false));
        writer.Write(attached ? text.Replace("\n", Environment.NewLine) : text);
    }
}
