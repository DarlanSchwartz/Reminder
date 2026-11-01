using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Reminder;

public sealed record TaskItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTime? DueDate { get; init; }
    public DateTimeOffset? AlarmAt { get; init; }
    public Recurrence? Repeat { get; init; }
    // Legacy monthly fields, read from older tasks.json files and converted on load.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MonthlyDay { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TimeOnly? MonthlyTime { get; init; }
    public string? SoundPath { get; init; }
    public string? SoundName { get; init; }
    // When set, each alarm plays a random MP3/WAV from this folder (falls back to SoundPath).
    public string? SoundFolder { get; init; }
    public bool Completed { get; init; }
    public bool AlertShown { get; init; }
    [JsonIgnore] public string DueLabel => DueDate is not { } d ? "" : d.Date == DateTime.Today ? "Hoje" : d.Date == DateTime.Today.AddDays(1) ? "Amanhã" : d.Date == DateTime.Today.AddDays(-1) ? "Ontem" : d.ToString("dd MMM");
    [JsonIgnore] public bool HasDue => DueDate != null;
    [JsonIgnore] public bool Overdue => !Completed && DueDate?.Date < DateTime.Today;
    [JsonIgnore] public bool HasAlarm => AlarmAt != null && !AlertShown && !Completed;
    [JsonIgnore] public string AlarmLabel => AlarmAt is not { } a ? "" : a.LocalDateTime.Date == DateTime.Today ? $"Hoje {a.LocalDateTime:HH:mm}" : a.LocalDateTime.Date == DateTime.Today.AddDays(1) ? $"Amanhã {a.LocalDateTime:HH:mm}" : a.LocalDateTime.ToString("ddd dd/MM HH:mm");
    [JsonIgnore] public bool IsRepeating => Repeat != null && !Completed;
    [JsonIgnore] public string RepeatLabel => Repeat?.ShortLabel ?? "";
}

public sealed class AppState
{
    public List<TaskItem> Tasks { get; set; } = new();
    public bool SchedulerConsent { get; set; }
    public bool StartupEnabled { get; set; }
    public bool Pinned { get; set; } = true;
    public string? MusicFolder { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
}
