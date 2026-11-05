using Xunit;

namespace Reminder.Tests;

public sealed class TaskServiceTests : LocalizedTest
{
    private readonly TempDirectory folder = new();
    private readonly FakeScheduler scheduler = new();
    private readonly TaskStore store;
    private readonly TaskService service;

    public TaskServiceTests()
    {
        store = new TaskStore(folder.Path);
        service = new TaskService(store, scheduler);
    }
    public override void Dispose() { folder.Dispose(); base.Dispose(); }

    private TaskItem Saved(Guid id) => store.Load().Tasks.Single(t => t.Id == id);
    private static Recurrence Daily(DateTime start, DateTime? until = null) => new() { Unit = RepeatUnit.Day, Start = start, Until = until };

    [Fact]
    public void Put_SavesAndReplacesByIdAndRaisesChanged()
    {
        int changes = 0;
        service.Changed += () => changes++;
        var task = new TaskItem { Title = "One" };
        service.Put(task);
        service.Put(task with { Title = "Renamed" });

        Assert.Equal("Renamed", Assert.Single(store.Load().Tasks).Title);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Put_WithoutConsent_DoesNotTouchTheScheduler()
    {
        service.Put(new TaskItem { Title = "Alarm", AlarmAt = DateTimeOffset.Now.AddHours(1) });
        Assert.Equal(0, scheduler.SetCalls);
    }

    [Fact]
    public void Put_WithConsent_RegistersTheAlarm()
    {
        service.EnableIntegration();
        var task = new TaskItem { Title = "Alarm", AlarmAt = DateTimeOffset.Now.AddHours(1) };
        service.Put(task);
        Assert.True(scheduler.Registered.ContainsKey(task.Id));
        Assert.True(store.Load().SchedulerConsent);
    }

    [Fact]
    public void Put_WhenTheSchedulerFails_RollsBackTheSavedEdit()
    {
        service.EnableIntegration();
        var task = new TaskItem { Title = "Original", AlarmAt = DateTimeOffset.Now.AddHours(1) };
        service.Put(task);
        scheduler.Fail = true;

        Assert.Throws<InvalidOperationException>(() => service.Put(task with { Title = "Edited" }));
        Assert.Equal("Original", Saved(task.Id).Title);
        Assert.Equal("Original", service.State.Tasks.Single().Title);
    }

    [Fact]
    public void Put_WhenTheRollbackSaveAlsoFails_ReportsTheSchedulerError()
    {
        service.EnableIntegration();
        var task = new TaskItem { Title = "Original", AlarmAt = DateTimeOffset.Now.AddHours(1) };
        service.Put(task);
        scheduler.Fail = true;
        // A folder where the temporary file goes makes every later save fail.
        scheduler.BeforeSet = () => Directory.CreateDirectory(folder.File("tasks.json.tmp"));

        Assert.Throws<InvalidOperationException>(() => service.Put(task with { Title = "Edited" }));
        Assert.Equal("Original", service.State.Tasks.Single().Title);
    }

    [Fact]
    public void Put_WhenSavingFails_KeepsTheStateInMemory()
    {
        var task = new TaskItem { Title = "Original" };
        service.Put(task);
        Directory.CreateDirectory(folder.File("tasks.json.tmp"));

        Assert.ThrowsAny<Exception>(() => service.Put(task with { Title = "Edited" }));
        Assert.Equal("Original", service.State.Tasks.Single().Title);
    }

    [Fact]
    public void Delete_RemovesTheTaskAndItsAlarm()
    {
        service.EnableIntegration();
        var task = new TaskItem { Title = "Alarm", AlarmAt = DateTimeOffset.Now.AddHours(1) };
        service.Put(task);
        service.Delete(task);

        Assert.Empty(store.Load().Tasks);
        Assert.Empty(scheduler.Registered);
    }

    [Fact]
    public void Delete_WhenTheSchedulerFails_KeepsTheTask()
    {
        service.EnableIntegration();
        var task = new TaskItem { Title = "Alarm", AlarmAt = DateTimeOffset.Now.AddHours(1) };
        service.Put(task);
        scheduler.Fail = true;

        Assert.Throws<InvalidOperationException>(() => service.Delete(task));
        Assert.Single(service.State.Tasks);
        Assert.Single(store.Load().Tasks);
    }

    [Fact]
    public void Delete_WhenSavingFails_RestoresTheTaskAndReportsTheSaveError()
    {
        service.EnableIntegration();
        var task = new TaskItem { Title = "Alarm", AlarmAt = DateTimeOffset.Now.AddHours(1) };
        service.Put(task);
        Directory.CreateDirectory(folder.File("tasks.json.tmp"));
        // Re-registering during the rollback fails too; the save error must still surface.
        scheduler.BeforeSet = () => scheduler.Fail = true;

        var error = Record.Exception(() => service.Delete(task));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.GetType().Name);
        Assert.Single(service.State.Tasks);
    }

    [Fact]
    public void Complete_TogglesAOneTimeTask()
    {
        var task = new TaskItem { Title = "Once", AlarmAt = DateTimeOffset.Now.AddHours(1) };
        service.Put(task);
        service.Complete(task);
        var done = Saved(task.Id);
        Assert.True(done.Completed);
        Assert.Null(done.AlarmAt);

        service.Complete(done);
        Assert.False(Saved(task.Id).Completed);
    }

    [Fact]
    public void Complete_OnARepeatingTask_MovesToTheNextOccurrence()
    {
        var task = new TaskItem { Title = "Daily", AlarmAt = DateTimeOffset.Now.AddMinutes(30), Repeat = Daily(DateTime.Now.AddMinutes(30)) };
        service.Put(task);
        service.Complete(task);

        var next = Saved(task.Id);
        Assert.False(next.Completed);
        Assert.True(next.AlarmAt > task.AlarmAt);
    }

    [Fact]
    public void Complete_OnTheLastOccurrence_FinishesTheSeries()
    {
        var start = DateTime.Now.AddMinutes(-1);
        var task = new TaskItem { Title = "Ending", AlarmAt = DateTimeOffset.Now.AddMinutes(-1), Repeat = Daily(start, until: start.Date) };
        service.Put(task);
        service.Complete(task);

        var done = Saved(task.Id);
        Assert.True(done.Completed);
        Assert.Null(done.AlarmAt);
    }

    [Fact]
    public void Acknowledge_ThenCompleteAlert_DoesNotSkipAnOccurrence()
    {
        var task = new TaskItem { Title = "Monthly", AlarmAt = DateTimeOffset.Now.AddMinutes(-1), Repeat = new Recurrence { Unit = RepeatUnit.Month, Start = new DateTime(2026, 1, 31, 9, 0, 0) } };
        service.Put(task);
        service.Acknowledge(task);
        var next = Saved(task.Id);
        Assert.True(next.AlarmAt > DateTimeOffset.Now);
        Assert.False(next.AlertShown);

        service.CompleteAlert(task);
        Assert.Equal(next.AlarmAt, Saved(task.Id).AlarmAt);
    }

    [Fact]
    public void CompleteAlert_OnAOneTimeTask_CompletesIt()
    {
        var task = new TaskItem { Title = "Once", AlarmAt = DateTimeOffset.Now.AddMinutes(-1) };
        service.Put(task);
        service.Acknowledge(task);
        service.CompleteAlert(task);

        var done = Saved(task.Id);
        Assert.True(done.Completed);
        Assert.True(done.AlertShown);
        Assert.Null(done.AlarmAt);
    }

    [Fact]
    public void CompleteAlert_AfterTheLastOccurrence_FinishesTheSeries()
    {
        var start = DateTime.Now.AddMinutes(-1);
        var task = new TaskItem { Title = "Ending", AlarmAt = DateTimeOffset.Now.AddMinutes(-1), Repeat = Daily(start, until: start.Date) };
        service.Put(task);
        service.Acknowledge(task);
        Assert.True(Saved(task.Id).AlertShown);

        service.CompleteAlert(task);
        Assert.True(Saved(task.Id).Completed);
    }

    [Fact]
    public void CompleteAlert_ForADeletedTask_DoesNothing()
    {
        service.CompleteAlert(new TaskItem { Title = "Gone" });
        Assert.Empty(service.State.Tasks);
    }

    [Fact]
    public void Snooze_KeepsTheSeries_AndAcknowledgingReturnsToTheSchedule()
    {
        var task = new TaskItem { Title = "Monthly", AlarmAt = DateTimeOffset.Now.AddMinutes(-1), Repeat = new Recurrence { Unit = RepeatUnit.Month, Start = new DateTime(2026, 1, 31, 9, 0, 0) } };
        service.Put(task);
        service.Acknowledge(task);
        var scheduled = Saved(task.Id).AlarmAt;

        service.Snooze(task.Id, 10);
        var snoozed = Saved(task.Id);
        Assert.Equal(task.Repeat, snoozed.Repeat);
        Assert.InRange(snoozed.AlarmAt!.Value, DateTimeOffset.Now.AddMinutes(9), DateTimeOffset.Now.AddMinutes(11));

        service.Acknowledge(snoozed);
        Assert.Equal(scheduled, Saved(task.Id).AlarmAt);
    }

    [Fact]
    public void Snooze_ReopensACompletedTask()
    {
        var task = new TaskItem { Title = "Once", Completed = true, AlertShown = true };
        service.Put(task);
        service.Snooze(task.Id, 5);
        var snoozed = Saved(task.Id);
        Assert.False(snoozed.Completed);
        Assert.True(snoozed.HasAlarm);
    }

    [Fact]
    public void SkipToday_MovesToTheFirstAlarmAfterToday_Once()
    {
        // Rings at 23:00 every hour; skipping today moves it to 00:00 tomorrow.
        var task = new TaskItem { Title = "Hourly", AlarmAt = new DateTimeOffset(DateTime.Today.AddHours(23)), Repeat = new Recurrence { Unit = RepeatUnit.Hour, Start = DateTime.Today.AddDays(-1) } };
        service.Put(task);
        service.SkipToday(task.Id);
        Assert.Equal(new DateTimeOffset(DateTime.Today.AddDays(1)), Saved(task.Id).AlarmAt);

        service.SkipToday(task.Id);
        Assert.Equal(new DateTimeOffset(DateTime.Today.AddDays(1)), Saved(task.Id).AlarmAt);
    }

    [Fact]
    public void SkipToday_CompletesASeriesThatEndsToday()
    {
        var task = new TaskItem { Title = "Hourly", AlarmAt = new DateTimeOffset(DateTime.Today.AddHours(23)), Repeat = new Recurrence { Unit = RepeatUnit.Hour, Start = DateTime.Today.AddDays(-1), Until = DateTime.Today } };
        service.Put(task);
        service.SkipToday(task.Id);
        Assert.True(Saved(task.Id).Completed);
    }

    [Fact]
    public void SkipToday_IgnoresOneTimeTasks()
    {
        var task = new TaskItem { Title = "Once", AlarmAt = DateTimeOffset.Now.AddHours(1) };
        service.Put(task);
        service.SkipToday(task.Id);
        Assert.Equal(task, Saved(task.Id));
    }

    [Fact]
    public void DisableIntegration_RemovesEveryRegistration()
    {
        service.EnableIntegration();
        service.SetStartup(true);
        service.Put(new TaskItem { Title = "A", AlarmAt = DateTimeOffset.Now.AddHours(1) });
        service.Put(new TaskItem { Title = "B", AlarmAt = DateTimeOffset.Now.AddHours(2) });

        service.DisableIntegration();
        Assert.Empty(scheduler.Registered);
        Assert.False(scheduler.Startup);
        var saved = store.Load();
        Assert.False(saved.SchedulerConsent);
        Assert.False(saved.StartupEnabled);
    }

    [Fact]
    public void Constructor_LoadsTheSavedState()
    {
        service.Put(new TaskItem { Title = "Persisted" });
        var reopened = new TaskService(new TaskStore(folder.Path), new FakeScheduler());
        Assert.Equal("Persisted", Assert.Single(reopened.State.Tasks).Title);
    }
}
