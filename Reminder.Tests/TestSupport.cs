using Xunit;

// Loc.Set changes the process-wide culture, and several tests read the clock and local
// time zone, so the suite runs sequentially. It is small and fast either way.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Reminder.Tests;

// A fresh folder per test, deleted afterwards.
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "reminder-tests-" + Guid.NewGuid().ToString("N"));
    public TempDirectory() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

// Records registrations instead of touching the Windows Task Scheduler.
public sealed class FakeScheduler : IAlarmScheduler
{
    public bool Fail { get; set; }
    // Runs before a Set call, e.g. to make the store fail during a rollback.
    public Action? BeforeSet { get; set; }
    public Dictionary<Guid, TaskItem> Registered { get; } = new();
    public bool Startup { get; private set; }
    public int SetCalls { get; private set; }

    public bool IsStartupEnabled() => Startup;
    public void Set(TaskItem task)
    {
        SetCalls++;
        BeforeSet?.Invoke();
        if (Fail) throw new InvalidOperationException("Simulated scheduler failure");
        if (task.AlarmAt == null || task.Completed || task.AlertShown) Registered.Remove(task.Id);
        else Registered[task.Id] = task;
    }
    public void Remove(Guid id)
    {
        if (Fail) throw new InvalidOperationException("Simulated scheduler failure");
        Registered.Remove(id);
    }
    public void SetStartup(bool enabled)
    {
        if (Fail) throw new InvalidOperationException("Simulated scheduler failure");
        Startup = enabled;
    }
}

// Every test starts in English and goes back to it, whatever it switched to.
public abstract class LocalizedTest : IDisposable
{
    protected LocalizedTest() => Loc.Set("en");
    public virtual void Dispose() => Loc.Set("en");
}
