using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
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
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("pt-BR")));
        if (e.Args.Contains("--self-test")) { Shutdown(SelfTests.Run()); return; }
        if (e.Args.Contains("--scheduler-test")) { Shutdown(SelfTests.RunWindowsScheduler()); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--audio-diagnostic") { Shutdown(AudioDiagnostics.Run(e.Args[1])); return; }
        try {
            string root = AppContext.BaseDirectory;
            string instance = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant() + Environment.UserName)))[..16];
            pipeName = "Reminder-" + instance;
            instanceMutex = new Mutex(true, "Local\\" + pipeName, out ownsMutex);
            if (!ownsMutex) {
                await SendAsync(string.Join(" ", e.Args)); Shutdown(); return;
            }
            service = new TaskService(new TaskStore(Path.Combine(root, "data")), new WindowsAlarmScheduler(Path.Combine(root, "Reminder.exe"), instance));
            try { service.RefreshStartup(); service.SaveSettings(); } catch (System.Runtime.InteropServices.COMException) { }
            widget = new MainWindow(service); MainWindow = widget;
            widget.Show();
            tray = new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Path.Combine(root, "Reminder.exe")), Text = "Reminder · suas tarefas", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Abrir Reminder", null, (_, _) => Dispatcher.Invoke(() => NativeWindow.Activate(widget)));
            menu.Items.Add("Nova tarefa", null, (_, _) => Dispatcher.Invoke(() => { NativeWindow.Activate(widget); widget.OpenEditor(null); }));
            menu.Items.Add("Sair", null, (_, _) => Dispatcher.Invoke(Shutdown));
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => NativeWindow.Activate(widget));
            lifetime = new CancellationTokenSource();
            _ = ListenAsync(lifetime.Token);
            service.Changed += ScheduleNext;
            alarmTimer.Tick += (_, _) => { alarmTimer.Stop(); CheckDue(); };
            CheckDue();
        } catch (Exception ex) { MessageBox.Show("Não foi possível abrir o Reminder.\n\n" + ex.Message, "Reminder", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
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
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var reader = new StreamReader(pipe);
                string? message = await reader.ReadLineAsync(token);
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
            } catch (Exception ex) { MessageBox.Show("O alarme foi disparado, mas houve uma falha ao atualizar seu estado.\n" + ex.Message, "Reminder"); }
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
