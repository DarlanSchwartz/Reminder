using Xunit;

namespace Reminder.Tests;

public sealed class TaskItemTests : LocalizedTest
{
    [Fact]
    public void HasAlarm_OnlyForPendingUndeliveredAlarms()
    {
        var task = new TaskItem { AlarmAt = DateTimeOffset.Now.AddHours(1) };
        Assert.True(task.HasAlarm);
        Assert.False((task with { AlertShown = true }).HasAlarm);
        Assert.False((task with { Completed = true }).HasAlarm);
        Assert.False(new TaskItem().HasAlarm);
    }

    [Fact]
    public void Overdue_WhenPendingAndDueBeforeToday()
    {
        var task = new TaskItem { DueDate = DateTime.Today.AddDays(-1) };
        Assert.True(task.Overdue);
        Assert.False((task with { Completed = true }).Overdue);
        Assert.False((task with { DueDate = DateTime.Today }).Overdue);
        Assert.False(new TaskItem().Overdue);
    }

    [Fact]
    public void DueLabel_UsesRelativeWordsNearToday()
    {
        Assert.Equal("Today", new TaskItem { DueDate = DateTime.Today }.DueLabel);
        Assert.Equal("Tomorrow", new TaskItem { DueDate = DateTime.Today.AddDays(1) }.DueLabel);
        Assert.Equal("Yesterday", new TaskItem { DueDate = DateTime.Today.AddDays(-1) }.DueLabel);
        Assert.Equal("", new TaskItem().DueLabel);
    }

    [Fact]
    public void CanSkipToday_ForARepeatingAlarmDueToday()
    {
        var repeat = new Recurrence { Unit = RepeatUnit.Hour, Start = DateTime.Today };
        var today = new TaskItem { AlarmAt = TaskItem.EndOfToday.AddMinutes(-1), Repeat = repeat };
        Assert.True(today.CanSkipToday);
        Assert.False((today with { AlarmAt = TaskItem.EndOfToday.AddHours(1) }).CanSkipToday);
        Assert.False((today with { Repeat = null }).CanSkipToday);
    }
}
