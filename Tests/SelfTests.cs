using System;
using System.IO;

namespace Reminder;

internal static class SelfTests
{
    public static int RunWindowsScheduler()
    {
        var scheduler = new WindowsAlarmScheduler(Path.Combine(AppContext.BaseDirectory, "Reminder.exe"), "Verification-" + Guid.NewGuid().ToString("N"));
        var task = new TaskItem { Title = "Temporary registration verification", AlarmAt = DateTimeOffset.Now.AddHours(1) };
        try {
            scheduler.Set(task);
            scheduler.Set(task with { AlarmAt = DateTimeOffset.Now.AddHours(2) });
            scheduler.Remove(task.Id);
            scheduler.SetStartup(true);
            Assert(scheduler.IsStartupEnabled(), "startup registration can be queried");
            scheduler.SetStartup(false);
            Assert(!scheduler.IsStartupEnabled(), "startup registration removed");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "scheduler-test-results.txt"), "PASS: Windows COM alarm creation, replacement, deletion, startup registration and removal.");
            return 0;
        } catch (Exception ex) {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "scheduler-test-results.txt"), "FAIL: " + ex); return 1;
        } finally { scheduler.Remove(task.Id); scheduler.SetStartup(false); }
    }
    private sealed class FakeScheduler : IAlarmScheduler
    {
        public bool Fail { get; set; }
        public int Registrations { get; private set; }
        public bool IsStartupEnabled() => false;
        public void Set(TaskItem task) { if (Fail) throw new InvalidOperationException("Simulated scheduler failure"); Registrations++; }
        public void Remove(Guid id) { if (Fail) throw new InvalidOperationException("Simulated scheduler failure"); }
        public void SetStartup(bool enabled) { }
    }
    public static int Run()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "test-output", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string resultPath = Path.Combine(AppContext.BaseDirectory, "test-results.txt");
        try {
            var utc = TimeZoneInfo.Utc;
            DateTimeOffset At(int y, int mo, int d, int h = 9, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);
            var monthly = new Recurrence { Unit = RepeatUnit.Month, Start = new DateTime(2027, 1, 31, 9, 0, 0) };
            var february = monthly.Next(At(2027, 1, 31), utc)!.Value;
            Assert(february == At(2027, 2, 28), "short month uses last day");
            Assert(monthly.Next(february, utc)!.Value.Day == 31, "day 31 restored in March");
            Assert(monthly.Next(At(2028, 1, 31), utc)!.Value.Day == 29, "leap February");
            var day10 = monthly with { Start = new DateTime(2026, 1, 10, 9, 0, 0) };
            Assert(day10.Next(At(2026, 12, 11), utc)!.Value.Year == 2027, "year rollover");
            Assert(day10.Next(At(2026, 9, 1), utc)!.Value.Month == 9, "upcoming occurrence in same month");
            Assert((monthly with { Interval = 3 }).Next(At(2027, 2, 1), utc) == At(2027, 4, 30), "every 3 months");
            var daily = new Recurrence { Unit = RepeatUnit.Day, Start = new DateTime(2027, 3, 1, 8, 30, 0), Until = new DateTime(2027, 3, 3) };
            Assert(daily.Next(At(2027, 2, 1), utc) == At(2027, 3, 1, 8, 30), "starts at anchor");
            Assert(daily.Next(At(2027, 3, 1, 8, 30), utc) == At(2027, 3, 2, 8, 30), "next day");
            Assert(daily.Next(At(2027, 3, 3, 8, 0), utc) == At(2027, 3, 3, 8, 30), "until date is inclusive");
            Assert(daily.Next(At(2027, 3, 3, 8, 30), utc) == null, "series ends after until");
            Assert((daily with { Interval = 2, Until = null }).Next(At(2027, 3, 1, 9), utc) == At(2027, 3, 3, 8, 30), "every 2 days");
            // 2027-03-01 is a Monday.
            var weekly = new Recurrence { Unit = RepeatUnit.Week, Start = new DateTime(2027, 3, 1, 7, 0, 0), Days = WeekDays.Monday | WeekDays.Friday };
            Assert(weekly.Next(At(2027, 3, 1, 7), utc) == At(2027, 3, 5, 7), "weekly Friday after Monday");
            Assert(weekly.Next(At(2027, 3, 5, 7), utc) == At(2027, 3, 8, 7), "weekly wraps to next Monday");
            var biweekly = weekly with { Interval = 2 };
            Assert(biweekly.Next(At(2027, 3, 5, 7), utc) == At(2027, 3, 15, 7), "every 2 weeks skips a week");
            Assert(biweekly.Next(At(2027, 4, 20), utc) == At(2027, 4, 26, 7), "every 2 weeks far ahead");
            Assert((weekly with { Days = WeekDays.None }).Next(At(2027, 3, 1, 7), utc) == At(2027, 3, 8, 7), "weekly defaults to start weekday");
            var hourly = new Recurrence { Unit = RepeatUnit.Hour, Interval = 6, Start = new DateTime(2027, 3, 1, 1, 15, 0) };
            Assert(hourly.Next(At(2027, 3, 2, 5), utc) == At(2027, 3, 2, 7, 15), "every 6 hours");
            var yearly = new Recurrence { Unit = RepeatUnit.Year, Start = new DateTime(2028, 2, 29, 10, 0, 0) };
            Assert(yearly.Next(At(2028, 3, 1), utc) == At(2029, 2, 28, 10), "leap date in common year");
            Assert(yearly.Next(At(2031, 3, 1), utc) == At(2032, 2, 29, 10), "leap date restored");
            string audioSource = Path.Combine(directory, "sample.wav");
            File.WriteAllBytes(audioSource, new byte[] { 1, 2, 3, 4 });
            var library = new SoundLibrary(Path.Combine(directory, "sounds"));
            string importedAudio = library.Import(audioSource);
            Assert(importedAudio != audioSource && File.ReadAllBytes(importedAudio).Length == 4, "audio stored in app library");
            Assert(library.Import(audioSource) == importedAudio, "audio deduplicated");
            foreach (var (input, expected) in new[] { ("0", "00:00"), ("9", "09:00"), ("930", "09:30"), ("0930", "09:30"), ("9:3", "09:03"), ("23:59", "23:59"), (" 930 ", "09:30") })
                Assert(AlarmTimeParser.TryParse(input, out var parsed) && parsed.ToString("HH:mm") == expected, "time normalization: " + input);
            foreach (var invalid in new[] { "", "24:00", "2360", "9:99", "-1:00", "12::30", "ab:cd", "9:", "12345" })
                Assert(!AlarmTimeParser.TryParse(invalid, out _), "invalid time rejected: " + invalid);
            var scheduler = new FakeScheduler();
            var store = new TaskStore(directory);
            var service = new TaskService(store, scheduler);
            Assert(service.State.Tasks.Count == 0, "starts empty");
            var task = new TaskItem { Title = "Ação & <foco> 🎯", Description = "Linha 1\nLinha 2", DueDate = DateTime.Today };
            service.Put(task);
            Assert(store.Load().Tasks[0] == task, "Unicode and optional fields round trip");
            Assert(scheduler.Registrations == 0, "does not touch scheduler without consent");
            service.EnableIntegration();
            var alarm = task with { AlarmAt = DateTimeOffset.Now.AddHours(2) };
            service.Put(alarm);
            Assert(store.Load().Tasks[0].AlarmAt == alarm.AlarmAt, "alarm offset persists");
            scheduler.Fail = true;
            try { service.Put(alarm with { Title = "Should roll back" }); throw new Exception("failure was not surfaced"); } catch (InvalidOperationException) { }
            Assert(store.Load().Tasks[0].Title == task.Title, "scheduler failure rolls back saved edit");
            try { service.Delete(alarm); throw new Exception("delete failure was not surfaced"); } catch (InvalidOperationException) { }
            Assert(service.State.Tasks.Count == 1, "failed cancellation keeps task");
            scheduler.Fail = false;
            service.Put(alarm with { Completed = true, AlarmAt = null });
            Assert(store.Load().Tasks[0].Completed, "completion persists");
            service.Delete(service.State.Tasks[0]);
            Assert(store.Load().Tasks.Count == 0, "deletion persists");
            var recurring = new TaskItem { Title = "Monthly", AlarmAt = DateTimeOffset.Now.AddMinutes(-1), Repeat = new Recurrence { Unit = RepeatUnit.Month, Start = new DateTime(2026, 1, 31, 9, 0, 0) }, SoundPath = importedAudio, SoundName = "sample.wav" };
            service.Put(recurring);
            service.Acknowledge(recurring);
            var nextOccurrence = store.Load().Tasks[0];
            Assert(nextOccurrence.AlarmAt > DateTimeOffset.Now && !nextOccurrence.AlertShown && !nextOccurrence.Completed, "delivery rearms monthly task");
            service.CompleteAlert(recurring);
            Assert(store.Load().Tasks[0].AlarmAt == nextOccurrence.AlarmAt, "completing delivered month does not skip next month");
            Assert(nextOccurrence.SoundPath == importedAudio && nextOccurrence.Repeat?.Start.Day == 31, "music and recurrence persist");
            service.Snooze(recurring.Id);
            var snoozed = store.Load().Tasks[0];
            Assert(snoozed.Repeat == recurring.Repeat && snoozed.AlarmAt < DateTimeOffset.Now.AddMinutes(11), "snooze preserves monthly anchor");
            service.Acknowledge(snoozed);
            Assert(store.Load().Tasks[0].AlarmAt == nextOccurrence.AlarmAt, "snooze returns to monthly schedule");
            service.Complete(store.Load().Tasks[0]);
            Assert(store.Load().Tasks[0].AlarmAt > nextOccurrence.AlarmAt, "early completion advances one occurrence");
            service.Delete(store.Load().Tasks[0]);
            var ending = new TaskItem { Title = "Ending", AlarmAt = DateTimeOffset.Now.AddMinutes(-1), Repeat = new Recurrence { Unit = RepeatUnit.Day, Start = DateTime.Now.AddMinutes(-1), Until = DateTime.Today } };
            service.Put(ending);
            service.Acknowledge(ending);
            Assert(store.Load().Tasks[0].AlertShown, "last occurrence behaves like a one-time alarm");
            service.CompleteAlert(ending);
            Assert(store.Load().Tasks[0].Completed, "completing the last occurrence finishes the task");
            service.Delete(store.Load().Tasks[0]);
            Assert(File.Exists(Path.Combine(directory, "tasks.json.bak")), "backup exists");
            File.WriteAllText(Path.Combine(directory, "tasks.json"), "{ broken");
            try { store.Load(); throw new Exception("corrupt file accepted"); } catch (InvalidDataException) { }
            Assert(File.ReadAllText(Path.Combine(directory, "tasks.json")) == "{ broken", "corrupt input preserved");
            File.WriteAllText(resultPath, "PASS: persistence, rollback, hourly/daily/weekly/monthly/yearly recurrence, end dates, leap years, snooze, sound import and 16 time parsing cases. No Windows scheduler registrations performed."); return 0;
        } catch (Exception ex) { File.WriteAllText(resultPath, "FAIL: " + ex); return 1; }
    }
    private static void Assert(bool value, string name) { if (!value) throw new Exception(name); }
}
