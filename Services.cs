using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace Reminder;

public sealed class TaskStore
{
    private readonly string path;
    public TaskStore(string directory) { Directory.CreateDirectory(directory); path = Path.Combine(directory, "tasks.json"); }
    public AppState Load()
    {
        if (!File.Exists(path)) return new();
        try {
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path)) ?? throw new InvalidDataException("Arquivo de tarefas vazio.");
            state.Tasks = state.Tasks.Select(Recurring.Migrate).ToList();
            return state;
        }
        catch (JsonException ex) { throw new InvalidDataException("Não foi possível ler tasks.json. O arquivo foi preservado; restaure tasks.json.bak antes de abrir novamente.", ex); }
    }
    public void Save(AppState state)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }
}

public interface IAlarmScheduler
{
    bool IsStartupEnabled();
    void Set(TaskItem task);
    void Remove(Guid id);
    void SetStartup(bool enabled);
}

public sealed class WindowsAlarmScheduler : IAlarmScheduler
{
    private readonly string executable;
    private readonly string prefix;
    public WindowsAlarmScheduler(string executable, string instance) { this.executable = executable; prefix = "Reminder-" + instance + "-"; }
    public bool IsStartupEnabled()
    {
        bool enabled = false;
        WithService((service, root) => {
            try { dynamic task = root.GetTask(prefix + "Startup"); enabled = task.Enabled; Marshal.FinalReleaseComObject(task); }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { }
        });
        return enabled;
    }
    private void WithService(Action<dynamic, dynamic> action)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        dynamic? root = null;
        try { service.Connect(); root = service.GetFolder("\\"); action(service, root); }
        finally { if (root != null) Marshal.FinalReleaseComObject(root); Marshal.FinalReleaseComObject(service); }
    }
    public void Set(TaskItem task)
    {
        if (task.AlarmAt == null || task.Completed || task.AlertShown) { Remove(task.Id); return; }
        Register(task.Id.ToString("N"), $"--alarm {task.Id:D}", task.AlarmAt);
    }
    private void Register(string name, string arguments, DateTimeOffset? time)
    {
        WithService((service, root) => {
            dynamic definition = service.NewTask(0);
            try {
                string sid = WindowsIdentity.GetCurrent().User!.Value;
                definition.RegistrationInfo.Description = "Reminder — widget pessoal de tarefas";
                definition.Principal.UserId = sid;
                definition.Principal.LogonType = 3;
                definition.Principal.RunLevel = 0;
                definition.Settings.Enabled = true;
                definition.Settings.StartWhenAvailable = true;
                definition.Settings.DisallowStartIfOnBatteries = false;
                definition.Settings.StopIfGoingOnBatteries = false;
                definition.Settings.ExecutionTimeLimit = "PT0S";
                definition.Settings.MultipleInstances = 2;
                definition.Settings.WakeToRun = time != null;
                dynamic trigger = definition.Triggers.Create(time == null ? 9 : 1);
                if (time == null) trigger.UserId = sid;
                else {
                    trigger.StartBoundary = time.Value.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
                    trigger.EndBoundary = time.Value.AddYears(1).ToString("yyyy-MM-dd'T'HH:mm:sszzz");
                }
                dynamic action = definition.Actions.Create(0);
                action.Path = executable;
                action.Arguments = arguments;
                action.WorkingDirectory = Path.GetDirectoryName(executable)!;
                dynamic registered = root.RegisterTaskDefinition(prefix + name, definition, 6, sid, null, 3, null);
                Marshal.FinalReleaseComObject(registered);
                Marshal.FinalReleaseComObject(action);
                Marshal.FinalReleaseComObject(trigger);
            } finally { Marshal.FinalReleaseComObject(definition); }
        });
    }
    public void Remove(Guid id) => WithService((service, root) => {
        try { root.DeleteTask(prefix + id.ToString("N"), 0); }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { }
    });
    public void SetStartup(bool enabled)
    {
        if (enabled) Register("Startup", "", null);
        else WithService((service, root) => { try { root.DeleteTask(prefix + "Startup", 0); } catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { } });
    }
}

public sealed class TaskService
{
    public AppState State { get; }
    private readonly TaskStore store;
    private readonly IAlarmScheduler scheduler;
    public event Action? Changed;
    public TaskService(TaskStore store, IAlarmScheduler scheduler) { this.store = store; this.scheduler = scheduler; State = store.Load(); }
    public void SaveSettings() => store.Save(State);
    public void RefreshStartup() => State.StartupEnabled = scheduler.IsStartupEnabled();
    public void Acknowledge(TaskItem task) => Put(Recurring.Advance(task, DateTimeOffset.Now));
    public void Complete(TaskItem task)
    {
        if (task.Repeat != null && !task.Completed)
        {
            // Completing early skips to the next occurrence; the last one finishes the task.
            var next = Recurring.Advance(task, DateTimeOffset.Now);
            Put(next.AlertShown ? next with { Completed = true, AlarmAt = null } : next);
        }
        else Put(task with { Completed = !task.Completed, AlarmAt = null, AlertShown = false });
    }
    public void Snooze(Guid id, int minutes = 10)
    {
        var current = State.Tasks.FirstOrDefault(t => t.Id == id);
        if (current != null) Put(current with { AlarmAt = DateTimeOffset.Now.AddMinutes(minutes), AlertShown = false, Completed = false });
    }
    public void CompleteAlert(TaskItem delivered)
    {
        var current = State.Tasks.FirstOrDefault(t => t.Id == delivered.Id);
        if (current == null) return;
        if (current.Repeat != null)
        {
            // Usually delivery has already rearmed the next occurrence. Retry only if
            // that write failed, without advancing the next occurrence twice.
            if (current.AlertShown) Put(current with { Completed = true, AlarmAt = null });
            else if (current.AlarmAt <= delivered.AlarmAt) Acknowledge(current);
        }
        else Put(current with { Completed = true, AlertShown = true, AlarmAt = null });
    }
    public void Put(TaskItem task)
    {
        var before = State.Tasks.ToList();
        State.Tasks.RemoveAll(t => t.Id == task.Id);
        State.Tasks.Add(task);
        try { store.Save(State); }
        catch { State.Tasks = before; throw; }
        try { if (State.SchedulerConsent) scheduler.Set(task); }
        catch {
            State.Tasks = before; store.Save(State);
            // Restore the previous registration when replacing an existing alarm.
            var previous = before.Find(t => t.Id == task.Id);
            try { if (previous != null) scheduler.Set(previous); else scheduler.Remove(task.Id); } catch { }
            throw;
        }
        Changed?.Invoke();
    }
    public void Delete(TaskItem task)
    {
        if (State.SchedulerConsent) scheduler.Remove(task.Id);
        var before = State.Tasks.ToList();
        State.Tasks.RemoveAll(t => t.Id == task.Id);
        try { store.Save(State); }
        catch { State.Tasks = before; if (State.SchedulerConsent) scheduler.Set(task); throw; }
        Changed?.Invoke();
    }
    public void EnableIntegration()
    {
        State.SchedulerConsent = true;
        SaveSettings();
    }
    public void SetStartup(bool enabled)
    {
        scheduler.SetStartup(enabled);
        State.StartupEnabled = enabled;
        SaveSettings();
    }
    public void DisableIntegration()
    {
        foreach (var task in State.Tasks) scheduler.Remove(task.Id);
        scheduler.SetStartup(false);
        State.StartupEnabled = false;
        State.SchedulerConsent = false;
        SaveSettings();
    }
}

internal static class NativeWindow
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    public static void Round(Window window) { int value = 2; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 33, ref value, 4); }
    public static void Activate(Window window) { window.Show(); window.WindowState = WindowState.Normal; window.Activate(); SetForegroundWindow(new WindowInteropHelper(window).Handle); }
}
