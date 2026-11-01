using System;
using System.Linq;

namespace Reminder;

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
    // No more alarms today: a repeating task moves to its first alarm after today.
    // If the series ends before then, the task is completed.
    public void SkipToday(Guid id)
    {
        var current = State.Tasks.FirstOrDefault(t => t.Id == id);
        if (current?.Repeat is not { } repeat || current.Completed) return;
        var endOfToday = TaskItem.EndOfToday;
        if (current.AlarmAt > endOfToday && !current.AlertShown) return;
        Put(repeat.Next(endOfToday) is { } next ? current with { AlarmAt = next, AlertShown = false } : current with { Completed = true, AlarmAt = null, AlertShown = true });
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
