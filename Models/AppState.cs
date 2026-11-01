using System.Collections.Generic;

namespace Reminder;

public sealed class AppState
{
    public List<TaskItem> Tasks { get; set; } = new();
    public bool SchedulerConsent { get; set; }
    public bool StartupEnabled { get; set; }
    public bool Pinned { get; set; } = true;
    public string? MusicFolder { get; set; }
    // "en", "es" or "pt".
    public string Language { get; set; } = "en";
    // A Palette theme key.
    public string Theme { get; set; } = "teal";
    // A picture or GIF copied into data/backgrounds, shown behind the alarm.
    public string? AlarmBackground { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
}
