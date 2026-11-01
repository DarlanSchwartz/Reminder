using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace ReminderSetup;

// Per-user install: files under %LocalAppData%\Programs\Reminder, shortcuts,
// an Apps & Features entry and, optionally, the folder on the user PATH.
internal static class Setup
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Reminder";
    public const string UninstallerName = "uninstall.exe";
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Reminder");
    private static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Reminder.lnk");
    private static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Reminder.lnk");

    public static string InstalledFolder()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key?.GetValue("InstallLocation") as string is { Length: > 0 } folder && Directory.Exists(folder) ? folder : null;
    }

    public static bool HasRuntime()
    {
        string programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string shared = Path.Combine(programFiles, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        return Directory.Exists(shared) && Directory.GetDirectories(shared).Any(d => Path.GetFileName(d).StartsWith("10.", StringComparison.Ordinal));
    }

    // Closes copies of Reminder running from the folder. Returns true if one was running.
    public static bool StopApp(string folder)
    {
        bool stopped = false;
        foreach (var process in Process.GetProcessesByName("Reminder"))
        {
            try
            {
                if (!process.MainModule.FileName.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                process.Kill(); process.WaitForExit(5000); stopped = true;
            }
            catch (Exception) { /* Another user's process, or already gone. */ }
            finally { process.Dispose(); }
        }
        return stopped;
    }

    public static void Install(string folder, bool desktop, bool addToPath, Action<double, string> report)
    {
        report(0.02, Text.T("Preparing"));
        Directory.CreateDirectory(folder);
        string root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            var entries = zip.Entries.Where(e => !e.FullName.StartsWith("data/", StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                Retry(() => entry.ExtractToFile(target, true));
                report(0.05 + 0.8 * (i + 1) / entries.Count, entry.FullName);
            }
        }
        string self = Assembly.GetExecutingAssembly().Location;
        string uninstaller = Path.Combine(folder, UninstallerName);
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase)) Retry(() => File.Copy(self, uninstaller, true));

        report(0.9, Text.T("Shortcuts"));
        string exe = Path.Combine(folder, "Reminder.exe");
        CreateShortcut(StartMenuShortcut, exe, folder);
        if (desktop) CreateShortcut(DesktopShortcut, exe, folder); else File.Delete(DesktopShortcut);
        SetOnPath(folder, addToPath);

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "Reminder");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "DarlanSchwartz");
            key.SetValue("DisplayIcon", exe);
            key.SetValue("InstallLocation", folder);
            key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall");
            key.SetValue("URLInfoAbout", "https://github.com/DarlanSchwartz/Reminder");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024), RegistryValueKind.DWord);
        }
        report(1, "");
    }

    public static void Uninstall(string folder, bool keepData, Action<double, string> report)
    {
        report(0.05, Text.T("Alarms"));
        StopApp(folder);
        string exe = Path.Combine(folder, "Reminder.exe");
        if (File.Exists(exe))
        {
            try { using var cleanup = Process.Start(new ProcessStartInfo(exe, "--remove-integration") { UseShellExecute = false }); cleanup.WaitForExit(20000); }
            catch (Exception) { /* Without the .NET runtime nothing was ever scheduled. */ }
        }
        report(0.35, Text.T("Files"));
        File.Delete(StartMenuShortcut);
        File.Delete(DesktopShortcut);
        SetOnPath(folder, false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

        string self = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
        var items = new DirectoryInfo(folder).GetFileSystemInfos().Where(i => !(keepData && i.Name.Equals("data", StringComparison.OrdinalIgnoreCase)) && !i.FullName.Equals(self, StringComparison.OrdinalIgnoreCase)).ToList();
        for (int i = 0; i < items.Count; i++)
        {
            try { if (items[i] is DirectoryInfo d) d.Delete(true); else items[i].Delete(); } catch (Exception) { }
            report(0.4 + 0.6 * (i + 1) / items.Count, items[i].Name);
        }
        report(1, "");
    }

    // The running uninstaller can't delete itself, so a short-lived cmd removes it after exit.
    public static void DeleteAfterExit(string folder, bool keepData)
    {
        string self = Assembly.GetExecutingAssembly().Location;
        string command = keepData ? $"del /f /q \"{self}\"" : $"rmdir /s /q \"{folder}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & {command}") { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }

    private static void CreateShortcut(string path, string target, string folder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType);
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = target; link.WorkingDirectory = folder; link.IconLocation = target + ",0"; link.Description = "Reminder";
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static void SetOnPath(string folder, bool present)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Environment");
        var kind = key.GetValueNames().Contains("Path") ? key.GetValueKind("Path") : RegistryValueKind.ExpandString;
        string current = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        var parts = current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Where(p => !p.TrimEnd('\\').Equals(folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
        if (present) parts.Add(folder);
        string updated = string.Join(";", parts);
        if (updated == current) return;
        key.SetValue("Path", updated, kind);
        SendMessageTimeout(new IntPtr(0xFFFF), 0x1A, IntPtr.Zero, "Environment", 2, 3000, out _);
    }

    private static void Retry(Action action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { action(); return; }
            catch (IOException) when (attempt < 10) { Thread.Sleep(300); }
            catch (UnauthorizedAccessException) when (attempt < 10) { Thread.Sleep(300); }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);
}
