using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Reminder;

internal static class NativeWindow
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    public static void Round(Window window) { int value = 2; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 33, ref value, 4); }
    public static void Activate(Window window) { window.Show(); window.WindowState = WindowState.Normal; window.Activate(); SetForegroundWindow(new WindowInteropHelper(window).Handle); }
}
