using System.Text.Json;
using Xunit;

namespace Reminder.Tests;

public sealed class CliTests : LocalizedTest
{
    private readonly TempDirectory folder = new();
    private readonly TaskService service;

    public CliTests() => service = new TaskService(new TaskStore(folder.Path), new FakeScheduler());
    public override void Dispose() { folder.Dispose(); base.Dispose(); }

    private (int Code, string Output) Run(params string[] args) => Cli.Execute(service, args, folder.File("sounds"));

    private TaskItem Only => Assert.Single(service.State.Tasks);
    private string ShortId => Only.Id.ToString("N")[..8];
    private static string Future(int days = 30, string time = "09:00") => DateTime.Today.AddDays(days).ToString("yyyy-MM-dd") + " " + time;

    [Theory]
    [InlineData("add", true)]
    [InlineData("LIST", true)]
    [InlineData("--help", true)]
    [InlineData("--alarm", false)]
    [InlineData("open", false)]
    public void IsCommand_RecognisesCliCommands(string first, bool expected)
    {
        Assert.Equal(expected, Cli.IsCommand([first]));
        Assert.False(Cli.IsCommand([]));
    }

    [Fact]
    public void Help_ListsTheCommands()
    {
        var (code, output) = Run("help");
        Assert.Equal(0, code);
        Assert.Contains("add <title>", output);
        Assert.Equal(0, Run("help", "add").Code);
    }

    [Fact]
    public void Add_CreatesATask()
    {
        var (code, output) = Run("add", "  Pay rent  ", "--desc", "Flat 3", "--due", "2030-01-15");
        Assert.Equal(0, code);
        Assert.StartsWith("Created: ", output);
        Assert.Equal("Pay rent", Only.Title);
        Assert.Equal("Flat 3", Only.Description);
        Assert.Equal(new DateTime(2030, 1, 15), Only.DueDate);
        Assert.Null(Only.AlarmAt);
    }

    [Fact]
    public void Add_WithAnUnquotedTitle_IsAUsageError()
    {
        var (code, output) = Run("add", "Pay", "rent");
        Assert.Equal(2, code);
        Assert.Contains("\"rent\"", output);
        Assert.Empty(service.State.Tasks);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("add", "   ")]
    [InlineData("add", "X", "--at")]
    [InlineData("add", "X", "--colour", "red")]
    [InlineData("add", "X", "--at", "2000-01-01 09:00")]
    [InlineData("add", "X", "--at", "25:00")]
    [InlineData("add", "X", "--at", "+99999999d")]
    [InlineData("add", "X", "--due", "+99999999d")]
    [InlineData("add", "X", "--due", "31/02/2030")]
    [InlineData("add", "X", "--every", "day")]
    [InlineData("add", "X", "--at", "+1h", "--every", "fortnight")]
    [InlineData("add", "X", "--at", "+1h", "--every", "day", "--interval", "0")]
    [InlineData("add", "X", "--at", "+1h", "--every", "day", "--interval", "1000")]
    [InlineData("add", "X", "--at", "+1h", "--every", "day", "--days", "mon")]
    [InlineData("add", "X", "--at", "+1h", "--every", "week", "--days", "funday")]
    [InlineData("add", "X", "--at", "+1h", "--interval", "2")]
    [InlineData("add", "X", "--at", "+1h", "--no-alarm")]
    [InlineData("add", "X", "--music", "missing.mp3")]
    [InlineData("add", "X", "--music-folder", "missing-folder")]
    [InlineData("list", "extra")]
    [InlineData("show")]
    [InlineData("show", "abc")]
    [InlineData("show", "00000000")]
    [InlineData("frobnicate")]
    public void BadInput_IsAUsageError_AndChangesNothing(params string[] args)
    {
        var (code, output) = Run(args);
        Assert.Equal(2, code);
        Assert.StartsWith("Error: ", output);
        Assert.Empty(service.State.Tasks);
    }

    [Fact]
    public void Add_WithAnAlarmInThePast_SaysItMustBeInTheFuture()
    {
        var (_, output) = Run("add", "X", "--at", "2000-01-01 09:00");
        Assert.Contains("future", output);
    }

    [Fact]
    public void Add_WithAnAlarm_SetsItAndNotesTheIntegrationIsOff()
    {
        var (code, output) = Run("add", "Call", "--at", Future());
        Assert.Equal(0, code);
        Assert.Equal(DateTime.Today.AddDays(30).AddHours(9), Only.AlarmAt!.Value.LocalDateTime);
        Assert.Contains("Task Scheduler integration is off", output);
    }

    [Fact]
    public void Add_RepeatingTask_StoresTheAnchorAsWallClockTime()
    {
        // Relative times like "tomorrow" are computed from DateTime.Now, which has a Local kind.
        Assert.Equal(0, Run("add", "Gym", "--at", "tomorrow 09:00", "--every", "week", "--days", "mon,wed,fri", "--until", "2099-12-31").Code);
        var repeat = Only.Repeat!;
        Assert.Equal(RepeatUnit.Week, repeat.Unit);
        Assert.Equal(WeekDays.Monday | WeekDays.Wednesday | WeekDays.Friday, repeat.Days);
        Assert.Equal(new DateTime(2099, 12, 31), repeat.Until);
        // A Local kind would be saved with today's UTC offset and drift after a time zone change.
        Assert.Equal(DateTimeKind.Unspecified, repeat.Start.Kind);
        var saved = File.ReadAllText(folder.File("tasks.json"));
        Assert.Contains($"\"Start\": \"{DateTime.Today.AddDays(1):yyyy-MM-dd}T09:00:00\"", saved);
    }

    [Fact]
    public void Add_Weekdays_PresetsMondayToFriday()
    {
        Assert.Equal(0, Run("add", "Work", "--at", "+1h", "--every", "weekday").Code);
        Assert.Equal((WeekDays)62, Only.Repeat!.Days);
    }

    [Fact]
    public void Add_WeeklyWithoutDays_UsesTheAlarmWeekday()
    {
        Assert.Equal(0, Run("add", "Weekly", "--at", Future(), "--every", "week").Code);
        Assert.Equal(Recurrence.Flag(DateTime.Today.AddDays(30).DayOfWeek), Only.Repeat!.Days);
    }

    [Fact]
    public void Add_WithMusic_ImportsTheFile()
    {
        string song = folder.File("Song.mp3");
        File.WriteAllBytes(song, [1, 2, 3]);
        Assert.Equal(0, Run("add", "Wake", "--music", song).Code);
        Assert.Equal("Song.mp3", Only.SoundName);
        Assert.StartsWith(folder.File("sounds"), Only.SoundPath);
    }

    [Fact]
    public void List_ShowsPendingTasksByDefault()
    {
        Run("add", "Pending");
        Run("add", "Finished");
        var finished = service.State.Tasks.Single(t => t.Title == "Finished");
        Run("done", finished.Id.ToString("N")[..8]);

        Assert.Contains("Pending", Run("list").Output);
        Assert.DoesNotContain("Finished", Run("list").Output);
        Assert.Contains("Finished", Run("list", "--done").Output);
        Assert.Contains("Finished", Run("list", "--all").Output);
    }

    [Fact]
    public void List_WhenEmpty_SaysSo()
    {
        Assert.Equal((0, "No tasks.\n"), Run("list"));
    }

    [Fact]
    public void List_Json_IsMachineReadable()
    {
        Run("add", "Gym", "--at", Future(), "--every", "week", "--days", "mon,fri");
        var (code, output) = Run("list", "--json");
        Assert.Equal(0, code);
        var item = Assert.Single(JsonDocument.Parse(output).RootElement.EnumerateArray().ToList());
        Assert.Equal("Gym", item.GetProperty("title").GetString());
        Assert.Equal(ShortId, item.GetProperty("shortId").GetString());
        Assert.Equal("week", item.GetProperty("repeat").GetProperty("every").GetString());
        Assert.Equal(["mon", "fri"], item.GetProperty("repeat").GetProperty("days").EnumerateArray().Select(d => d.GetString()));
        Assert.True(DateTimeOffset.TryParse(item.GetProperty("alarm").GetString(), out _));
    }

    [Fact]
    public void Show_AcceptsShortAndFullIds()
    {
        Run("add", "Find me");
        Assert.Contains("Find me", Run("show", ShortId[..4]).Output);
        Assert.Contains("Find me", Run("show", Only.Id.ToString("D").ToUpperInvariant()).Output);
    }

    [Fact]
    public void Show_RequiresFourCharactersNotCountingDashes()
    {
        Run("add", "Find me");
        string id = Only.Id.ToString("N");
        Assert.Equal(2, Run("show", id[..2] + "-" + id[2]).Code);
    }

    [Fact]
    public void Done_CompletesAOneTimeTask_AndReopenUndoesIt()
    {
        Run("add", "Once", "--at", Future());
        Assert.StartsWith("Completed: ", Run("done", ShortId).Output);
        Assert.True(Only.Completed);
        Assert.Equal("Already completed.\n", Run("done", ShortId).Output);

        Assert.Equal(0, Run("reopen", ShortId).Code);
        Assert.False(Only.Completed);
    }

    [Fact]
    public void Done_OnARepeatingTask_MovesToTheNextOccurrence()
    {
        Run("add", "Daily", "--at", Future(1), "--every", "day");
        var first = Only.AlarmAt;
        Assert.StartsWith("Occurrence completed, next: ", Run("done", ShortId).Output);
        Assert.Equal(first!.Value.AddDays(1), Only.AlarmAt);
        Assert.False(Only.Completed);
    }

    [Fact]
    public void Done_Series_EndsTheWholeSeries()
    {
        Run("add", "Daily", "--at", Future(1), "--every", "day");
        Run("done", ShortId, "--series");
        Assert.True(Only.Completed);
        Assert.Null(Only.AlarmAt);
    }

    [Fact]
    public void Skip_OnlyWorksOnRepeatingTasks()
    {
        Run("add", "Once", "--at", Future());
        Assert.Equal(2, Run("skip", ShortId).Code);
    }

    [Fact]
    public void Edit_ChangesOnlyTheGivenFields()
    {
        Run("add", "Old", "--desc", "Keep me", "--at", Future(), "--every", "month");
        var before = Only;
        Assert.Equal(0, Run("edit", ShortId, "--title", "New").Code);
        Assert.Equal(before with { Title = "New" }, Only);
    }

    [Fact]
    public void Edit_NoAlarm_ClearsTheAlarmAndRepeat()
    {
        Run("add", "Daily", "--at", Future(), "--every", "day");
        Assert.Equal(0, Run("edit", ShortId, "--no-alarm").Code);
        Assert.Null(Only.AlarmAt);
        Assert.Null(Only.Repeat);
    }

    [Fact]
    public void Edit_Interval_KeepsTheSeriesAnchor()
    {
        Run("add", "Daily", "--at", Future(), "--every", "day");
        var start = Only.Repeat!.Start;
        Assert.Equal(0, Run("edit", ShortId, "--interval", "3").Code);
        Assert.Equal(3, Only.Repeat!.Interval);
        Assert.Equal(start, Only.Repeat.Start);
    }

    [Fact]
    public void Edit_WithAnEmptyTitle_IsAUsageError()
    {
        Run("add", "Keep");
        Assert.Equal(2, Run("edit", ShortId, "--title", " ").Code);
        Assert.Equal("Keep", Only.Title);
    }

    [Fact]
    public void Edit_UntilBeforeTheNextAlarm_IsAUsageError()
    {
        Run("add", "Daily", "--at", Future(10), "--every", "day");
        Assert.Equal(2, Run("edit", ShortId, "--until", DateTime.Today.AddDays(5).ToString("yyyy-MM-dd")).Code);
    }

    [Fact]
    public void Remove_DeletesTheTask()
    {
        Run("add", "Bye");
        var id = Only.Id;
        var (code, output) = Run("remove", ShortId, "--json");
        Assert.Equal(0, code);
        Assert.Equal(id, JsonDocument.Parse(output).RootElement.GetProperty("removed").GetGuid());
        Assert.Empty(service.State.Tasks);
    }

    [Fact]
    public void AmbiguousIds_AreRejected()
    {
        // Two ids that share their first eight characters.
        service.Put(new TaskItem { Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), Title = "A" });
        service.Put(new TaskItem { Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), Title = "B" });
        var (code, output) = Run("show", "aaaaaaaa");
        Assert.Equal(2, code);
        Assert.Contains("ambiguous", output);
    }

    // ParseDate / ParseWhen with a fixed "now": Thursday 2026-11-05 10:20:30.
    private static readonly DateTime Now = new(2026, 11, 5, 10, 20, 30);

    [Theory]
    [InlineData("today", "2026-11-05")]
    [InlineData("Hoje", "2026-11-05")]
    [InlineData("tomorrow", "2026-11-06")]
    [InlineData("mañana", "2026-11-06")]
    [InlineData("+3d", "2026-11-08")]
    [InlineData("2026-11-30", "2026-11-30")]
    [InlineData("30/11/2026", "2026-11-30")]
    [InlineData("1/2/2027", "2027-02-01")]
    [InlineData("30/11", "2026-11-30")]
    [InlineData("01/03", "2027-03-01")] // Already past this year: next year.
    [InlineData("05/11", "2026-11-05")] // Today stays today.
    public void ParseDate_UnderstandsDates(string input, string expected)
    {
        Assert.Equal(DateTime.Parse(expected), Cli.ParseDate(input, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("2026-13-01")]
    [InlineData("+d")]
    [InlineData("+-3d")]
    [InlineData("+36501d")]
    public void ParseDate_RejectsInvalidDates(string input)
    {
        Assert.ThrowsAny<Exception>(() => Cli.ParseDate(input, Now));
    }

    [Theory]
    [InlineData("18:30", "2026-11-05 18:30")]
    [InlineData("1830", "2026-11-05 18:30")]
    [InlineData("09:00", "2026-11-06 09:00")] // Already past today: tomorrow.
    [InlineData("now", "2026-11-05 10:21:30")]
    [InlineData("+30m", "2026-11-05 10:50")]
    [InlineData("+2h", "2026-11-05 12:20")]
    [InlineData("+1d", "2026-11-06 10:20")]
    [InlineData("+1w", "2026-11-12 10:20")]
    [InlineData("today", "2026-11-05 09:00")]
    [InlineData("tomorrow 08:00", "2026-11-06 08:00")]
    [InlineData("2026-11-05 09:00", "2026-11-05 09:00")]
    [InlineData("2026-11-05T21:15", "2026-11-05 21:15")]
    [InlineData("05/11/2026 9:30", "2026-11-05 09:30")]
    [InlineData("+3d 07:00", "2026-11-08 07:00")]
    public void ParseWhen_UnderstandsTimes(string input, string expected)
    {
        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Cli.ParseWhen(input, Now));
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("tomorrow 8pm")]
    [InlineData("+5y")]
    [InlineData("+99999999m")]
    [InlineData("+2147483647w")]
    [InlineData("2026-11-05 09:00 extra")]
    public void ParseWhen_RejectsInvalidTimes(string input)
    {
        var error = Assert.ThrowsAny<Exception>(() => Cli.ParseWhen(input, Now));
        Assert.IsNotType<ArgumentOutOfRangeException>(error);
    }
}
