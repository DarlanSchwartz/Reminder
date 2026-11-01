using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Reminder;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private bool ownsMutex;
    private CancellationTokenSource? lifetime;
    private Forms.NotifyIcon? tray;
    private TaskService? service;
    private MainWindow? widget;
    private readonly DispatcherTimer alarmTimer = new();
    private readonly HashSet<Guid> visibleAlarms = new();
    private string pipeName = "";
    // Not async: culture set inside an async method is undone when it returns,
    // which would put dates back in the Windows language.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Loc.Set("en");
        if (e.Args.Contains("--self-test")) { Shutdown(SelfTests.Run()); return; }
        if (e.Args.Contains("--scheduler-test")) { Shutdown(SelfTests.RunWindowsScheduler()); return; }
        try {
            string root = AppContext.BaseDirectory;
            string instance = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant() + Environment.UserName)))[..16];
            pipeName = "Reminder-" + instance;
            instanceMutex = new Mutex(true, "Local\\" + pipeName, out ownsMutex);
            // Used by the uninstaller: removes this copy's Task Scheduler entries.
            if (e.Args.Contains("--remove-integration")) {
                new TaskService(new TaskStore(Path.Combine(root, "data")), new WindowsAlarmScheduler(Path.Combine(root, "Reminder.exe"), instance)).DisableIntegration();
                Shutdown(0); return;
            }
            if (Cli.IsCommand(e.Args)) { _ = ShutdownAfter(RunCliAsync(e.Args, root, instance)); return; }
            if (!ownsMutex) {
                _ = ShutdownAfter(SendAsync(string.Join(" ", e.Args))); return;
            }
            service = new TaskService(new TaskStore(Path.Combine(root, "data")), new WindowsAlarmScheduler(Path.Combine(root, "Reminder.exe"), instance));
            try { service.RefreshStartup(); service.SaveSettings(); } catch (System.Runtime.InteropServices.COMException) { }
            Loc.Set(service.State.Language);
            Palette.Apply(service.State.Theme);
            Motion.RegisterPressFeedback();
            widget = new MainWindow(service); MainWindow = widget;
            widget.Show();
            tray = new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Path.Combine(root, "Reminder.exe")), Text = Loc.T("TrayTip"), Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(Loc.T("TrayOpen"), null, (_, _) => Dispatcher.Invoke(() => NativeWindow.Activate(widget)));
            menu.Items.Add(Loc.T("NewTask"), null, (_, _) => Dispatcher.Invoke(() => { NativeWindow.Activate(widget); widget.OpenEditor(null); }));
            menu.Items.Add(Loc.T("TrayQuit"), null, (_, _) => Dispatcher.Invoke(Shutdown));
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => NativeWindow.Activate(widget));
            Loc.Changed += () => { tray.Text = Loc.T("TrayTip"); menu.Items[0].Text = Loc.T("TrayOpen"); menu.Items[1].Text = Loc.T("NewTask"); menu.Items[2].Text = Loc.T("TrayQuit"); };
            lifetime = new CancellationTokenSource();
            _ = ListenAsync(lifetime.Token);
            service.Changed += ScheduleNext;
            alarmTimer.Tick += (_, _) => { alarmTimer.Stop(); CheckDue(); };
            CheckDue();
        } catch (Exception ex) { MessageBox.Show(Loc.T("OpenFailed") + "\n\n" + ex.Message, "Reminder", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    private async Task ShutdownAfter(Task work)
    {
        try { await work; Shutdown(work is Task<int> code ? code.Result : 0); }
        catch (Exception ex) { MessageBox.Show(Loc.T("OpenFailed") + "\n\n" + ex.Message, "Reminder", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    // CLI commands run inside the open app when there is one, so the widget and its
    // in-memory state stay in sync; otherwise they work on the data folder directly.
    private async Task<int> RunCliAsync(string[] args, string root, string instance)
    {
        int code; string output;
        try {
            if (ownsMutex) {
                var cli = new TaskService(new TaskStore(Path.Combine(root, "data")), new WindowsAlarmScheduler(Path.Combine(root, "Reminder.exe"), instance));
                Loc.Set(cli.State.Language);
                (code, output) = Cli.Execute(cli, args, Path.Combine(root, "data", "sounds"));
            } else {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(6000);
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
                    await writer.WriteLineAsync("--cli " + JsonSerializer.Serialize(args));
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                code = int.TryParse(await reader.ReadLineAsync(), out int c) ? c : 1;
                output = await reader.ReadToEndAsync();
            }
        } catch (Exception ex) { code = 1; output = "Error: " + ex.Message + "\n"; }
        ConsoleOutput.Write(output, code != 0);
        return code;
    }
    private async Task SendAsync(string message)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(6000);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        await writer.WriteLineAsync(message);
    }
    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested) {
            try {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                string? message = await reader.ReadLineAsync(token);
                if (message?.StartsWith("--cli ", StringComparison.Ordinal) == true && service != null) {
                    var args = JsonSerializer.Deserialize<string[]>(message[6..]) ?? [];
                    var (code, output) = await Dispatcher.InvokeAsync(() => Cli.Execute(service, args, Path.Combine(AppContext.BaseDirectory, "data", "sounds")));
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true);
                    await writer.WriteAsync(code + "\n" + output);
                    continue;
                }
                await Dispatcher.InvokeAsync(() => { if (message?.StartsWith("--alarm ", StringComparison.Ordinal) == true) CheckDue(); else if (widget != null) NativeWindow.Activate(widget); });
            } catch (OperationCanceledException) { break; }
            catch (IOException) { /* A caller may close before sending. Keep accepting the next launch. */ }
        }
    }
    private void ScheduleNext()
    {
        alarmTimer.Stop();
        if (service == null) return;
        var next = service.State.Tasks.Where(t => !t.Completed && !t.AlertShown && t.AlarmAt != null).Select(t => t.AlarmAt!.Value).OrderBy(t => t).FirstOrDefault();
        // One wake-up at the next alarm, with a minute cap only while an alarm is pending,
        // to recover gracefully from sleep or a system clock change. Idle lists use no timer.
        if (next != default) { alarmTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp((next - DateTimeOffset.Now).TotalMilliseconds, 250, 60000)); alarmTimer.Start(); }
    }
    private void CheckDue()
    {
        if (service == null) return;
        if (visibleAlarms.Count > 0) return;
        widget?.Refresh();
        foreach (var task in service.State.Tasks.Where(t => !t.Completed && !t.AlertShown && t.AlarmAt <= DateTimeOffset.Now).ToList()) {
            if (!visibleAlarms.Add(task.Id)) continue;
            try {
                var alert = new AlarmWindow(task, service);
                alert.Closed += (_, _) => { visibleAlarms.Remove(task.Id); Dispatcher.BeginInvoke(CheckDue); };
                alert.Show();
                service.Acknowledge(task);
                break;
            } catch (Exception ex) { MessageBox.Show(Loc.T("AlarmStateFailed") + "\n" + ex.Message, "Reminder"); }
        }
        ScheduleNext();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        lifetime?.Cancel(); alarmTimer.Stop(); tray?.Dispose();
        if (ownsMutex) instanceMutex?.ReleaseMutex(); instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
