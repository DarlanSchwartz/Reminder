using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Reminder;

// Puts the app folder on the user PATH so terminals, scripts and agents can run `reminder`.
public static class CommandPath
{
    private static string Folder => AppContext.BaseDirectory.TrimEnd('\\');

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        return Parts(key?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string).Any(IsFolder);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Environment");
        var kind = key.GetValueNames().Contains("Path") ? key.GetValueKind("Path") : RegistryValueKind.ExpandString;
        var current = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        var parts = Parts(current).Where(p => !IsFolder(p)).ToList();
        if (enabled) parts.Add(Folder);
        key.SetValue("Path", string.Join(";", parts), kind);
        // Tells Explorer, and the terminals it starts afterwards, that PATH changed.
        SendMessageTimeout(new IntPtr(0xFFFF), 0x1A, IntPtr.Zero, "Environment", 2, 3000, out _);
    }

    private static string[] Parts(string? path) => (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
    private static bool IsFolder(string part) => string.Equals(Environment.ExpandEnvironmentVariables(part).TrimEnd('\\'), Folder, StringComparison.OrdinalIgnoreCase);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);
}
