using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Reminder;

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
                definition.RegistrationInfo.Description = "Reminder — personal task widget";
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
