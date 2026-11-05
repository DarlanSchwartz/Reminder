using Xunit;

namespace Reminder.Tests;

public sealed class TaskStoreTests : IDisposable
{
    private readonly TempDirectory folder = new();
    public void Dispose() => folder.Dispose();

    [Fact]
    public void Load_WithoutAFile_ReturnsDefaults()
    {
        var state = new TaskStore(folder.Path).Load();
        Assert.Empty(state.Tasks);
        Assert.Equal("en", state.Language);
        Assert.True(state.Pinned);
    }

    [Fact]
    public void Constructor_CreatesTheFolder()
    {
        string nested = folder.File("a/b");
        new TaskStore(nested);
        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public void Save_RoundTripsUnicodeAndOptionalFields()
    {
        var store = new TaskStore(folder.Path);
        var task = new TaskItem
        {
            Title = "Ação & <foco> 🎯", Description = "Linha 1\nLinha 2", DueDate = new DateTime(2027, 5, 1),
            AlarmAt = new DateTimeOffset(2027, 5, 1, 9, 0, 0, TimeSpan.FromHours(-3)),
            Repeat = new Recurrence { Unit = RepeatUnit.Week, Interval = 2, Days = WeekDays.Monday | WeekDays.Friday, Start = new DateTime(2027, 5, 1, 9, 0, 0), Until = new DateTime(2027, 12, 31) },
            SoundName = "song.mp3", SoundPath = "C:/x/song.mp3",
        };
        store.Save(new AppState { Tasks = { task }, Language = "pt", Theme = "ocean", Left = 10.5 });

        var loaded = store.Load();
        Assert.Equal(task, Assert.Single(loaded.Tasks));
        Assert.Equal(TimeSpan.FromHours(-3), loaded.Tasks[0].AlarmAt!.Value.Offset);
        Assert.Equal("pt", loaded.Language);
        Assert.Equal("ocean", loaded.Theme);
        Assert.Equal(10.5, loaded.Left);
    }

    [Fact]
    public void Save_KeepsABackup_AndLeavesNoTemporaryFile()
    {
        var store = new TaskStore(folder.Path);
        store.Save(new AppState { Tasks = { new TaskItem { Title = "First" } } });
        store.Save(new AppState { Tasks = { new TaskItem { Title = "Second" } } });

        Assert.Contains("First", File.ReadAllText(folder.File("tasks.json.bak")));
        Assert.Contains("Second", File.ReadAllText(folder.File("tasks.json")));
        Assert.False(File.Exists(folder.File("tasks.json.tmp")));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Load_RejectsAnUnreadableFile_AndKeepsIt(string content)
    {
        File.WriteAllText(folder.File("tasks.json"), content);
        Assert.Throws<InvalidDataException>(() => new TaskStore(folder.Path).Load());
        Assert.Equal(content, File.ReadAllText(folder.File("tasks.json")));
    }

    [Fact]
    public void Load_TreatsNullTasksAsEmpty()
    {
        File.WriteAllText(folder.File("tasks.json"), """{ "Tasks": null }""");
        Assert.Empty(new TaskStore(folder.Path).Load().Tasks);
    }

    [Fact]
    public void Load_SkipsNullTaskEntries()
    {
        File.WriteAllText(folder.File("tasks.json"), """{ "Tasks": [ null, { "Title": "Kept" }, null ] }""");
        Assert.Equal("Kept", Assert.Single(new TaskStore(folder.Path).Load().Tasks).Title);
    }

    [Fact]
    public void Load_IgnoresUnknownProperties()
    {
        File.WriteAllText(folder.File("tasks.json"), """{ "Future": 1, "Tasks": [ { "Title": "A", "Later": true } ] }""");
        Assert.Equal("A", Assert.Single(new TaskStore(folder.Path).Load().Tasks).Title);
    }
}
