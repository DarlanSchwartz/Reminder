using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace Reminder;

public enum RepeatUnit { Hour, Day, Week, Month, Year }

[Flags]
public enum WeekDays { None = 0, Sunday = 1, Monday = 2, Tuesday = 4, Wednesday = 8, Thursday = 16, Friday = 32, Saturday = 64 }

public sealed record Recurrence
{
    public RepeatUnit Unit { get; init; } = RepeatUnit.Day;
    public int Interval { get; init; } = 1;
    // Weekly only. Empty means the weekday of Start.
    public WeekDays Days { get; init; }
    // Local wall-clock anchor: the first occurrence. Its time of day, day of month
    // and month are kept, so Jan 31 -> Feb 28 -> Mar 31 and Feb 29 -> Feb 28 -> Feb 29.
    public DateTime Start { get; init; }
    // Last day (inclusive) on which an occurrence may happen.
    public DateTime? Until { get; init; }

    public static WeekDays Flag(DayOfWeek day) => (WeekDays)(1 << (int)day);

    [JsonIgnore] private WeekDays EffectiveDays => Days == WeekDays.None ? Flag(Start.DayOfWeek) : Days;

    // Next occurrence strictly after `after`, or null when the series has ended.
    public DateTimeOffset? Next(DateTimeOffset after, TimeZoneInfo? zone = null)
    {
        if (Interval < 1) throw new ArgumentOutOfRangeException(nameof(Interval));
        zone ??= TimeZoneInfo.Local;
        var from = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        foreach (var local in Candidates(from < Start ? Start : from))
        {
            if (Until is { } until && local.Date > until.Date) return null;
            var date = local;
            while (zone.IsInvalidTime(date)) date = date.AddMinutes(1);
            var candidate = new DateTimeOffset(date, zone.GetUtcOffset(date));
            if (candidate > after) return candidate;
        }
        throw new InvalidOperationException("Couldn't compute the next occurrence.");
    }

    // Occurrences in order, beginning at or slightly before `from`.
    private IEnumerable<DateTime> Candidates(DateTime from)
    {
        var time = Start.TimeOfDay;
        switch (Unit)
        {
            case RepeatUnit.Hour:
            {
                long step = TimeSpan.FromHours(Interval).Ticks;
                long k = Math.Max(0, (from - Start).Ticks / step);
                for (int i = 0; i < 4; i++) yield return Start.AddTicks((k + i) * step);
                break;
            }
            case RepeatUnit.Day:
            {
                long k = Math.Max(0, (from.Date - Start.Date).Days / Interval);
                for (int i = 0; i < 4; i++) yield return Start.Date.AddDays((k + i) * Interval) + time;
                break;
            }
            case RepeatUnit.Week:
            {
                var days = EffectiveDays;
                var firstWeek = WeekStart(Start.Date);
                long k = Math.Max(0, (WeekStart(from.Date) - firstWeek).Days / 7 / Interval);
                for (int i = 0; i < 3; i++)
                {
                    var week = firstWeek.AddDays((k + i) * Interval * 7);
                    for (int d = 0; d < 7; d++)
                    {
                        var date = week.AddDays(d);
                        if (date >= Start.Date && (days & Flag(date.DayOfWeek)) != 0) yield return date + time;
                    }
                }
                break;
            }
            case RepeatUnit.Month:
            {
                int months = (from.Year - Start.Year) * 12 + from.Month - Start.Month;
                long k = Math.Max(0, months / Interval);
                for (int i = 0; i < 3; i++) yield return Clamp(Start.Year, Start.Month, (k + i) * Interval) + time;
                break;
            }
            case RepeatUnit.Year:
            {
                long k = Math.Max(0, (from.Year - Start.Year) / Interval);
                for (int i = 0; i < 3; i++) yield return Clamp(Start.Year, Start.Month, (k + i) * Interval * 12) + time;
                break;
            }
        }
    }

    private DateTime Clamp(int year, int month, long addMonths)
    {
        var first = new DateTime(year, month, 1).AddMonths((int)addMonths);
        return first.AddDays(Math.Min(Start.Day, DateTime.DaysInMonth(first.Year, first.Month)) - 1);
    }

    private static DateTime WeekStart(DateTime date) => date.AddDays(-(int)date.DayOfWeek);

    // "Every week" vs "Every 3 weeks": the singular key, or the plural key formatted with Interval.
    private string Every(string one, string many) => Interval == 1 ? Loc.T(one) : Loc.F(many, Interval);
    private string Date(string format) => Start.ToString(Loc.T(format), Loc.Culture);

    [JsonIgnore] public string ShortLabel => Unit switch
    {
        RepeatUnit.Hour => Every("EveryHour", "EveryHoursShort"),
        RepeatUnit.Day => Every("EveryDayShort", "EveryDays"),
        RepeatUnit.Week => Every("EveryWeek", "EveryWeeks") + " · " + DayList(),
        RepeatUnit.Month => Every("EveryMonth", "EveryMonths") + " · " + Loc.F("DayOfMonth", Start.Day),
        _ => Every("EveryYear", "EveryYears") + " · " + Date("fmt.DayMonth"),
    } + (Until is { } u ? Loc.F("UntilShort", u.ToString(Loc.T("fmt.DayMonth"), Loc.Culture)) : "");

    public string Describe()
    {
        string at = Start.ToString("HH:mm", CultureInfo.InvariantCulture);
        string body = Unit switch
        {
            RepeatUnit.Hour => Loc.F("HourlyFrom", Every("EveryHour", "EveryHours"), Date("fmt.DayMonth"), at),
            RepeatUnit.Day => Loc.F("AtTime", Every("EveryDay", "EveryDays"), at),
            RepeatUnit.Week => Loc.F("WeeklyAt", Every("EveryWeek", "EveryWeeks"), DayList(), at),
            RepeatUnit.Month => Loc.F("MonthlyAt", Every("EveryMonth", "EveryMonths"), Start.Day, at) + (Start.Day > 28 ? Loc.T("ShortMonths") : ""),
            _ => Loc.F("YearlyAt", Every("EveryYear", "EveryYears"), Date("fmt.MonthDay"), at),
        };
        return body + (Until is { } u ? Loc.F("UntilLong", u.ToString(Loc.T("fmt.Date"), Loc.Culture)) : Loc.T("NoEnd"));
    }

    private string DayList()
    {
        var days = EffectiveDays;
        if (days == (WeekDays)127) return Loc.T("AllWeek");
        if (days == (WeekDays)62) return Loc.T("MonToFri");
        var names = Loc.T("DayAbbreviations").Split(',');
        // Monday first, as people usually read a week.
        return string.Join(", ", new[] { 1, 2, 3, 4, 5, 6, 0 }.Where(d => (days & Flag((DayOfWeek)d)) != 0).Select(d => names[d]));
    }
}

public static class Recurring
{
    public static TaskItem Advance(TaskItem task, DateTimeOffset now)
    {
        if (task.Repeat is not { } repeat) return task with { AlertShown = true };
        var after = task.AlarmAt > now ? task.AlarmAt.Value : now;
        // When the series has ended, behave like a delivered one-time alarm.
        return repeat.Next(after) is { } next ? task with { AlarmAt = next, AlertShown = false, Completed = false } : task with { AlertShown = true };
    }

    // Converts the old "MonthlyDay/MonthlyTime" fields to a monthly Recurrence.
    public static TaskItem Migrate(TaskItem task)
    {
        if (task.MonthlyDay is not { } day || task.Repeat != null) return task with { MonthlyDay = null, MonthlyTime = null };
        var time = task.MonthlyTime ?? (task.AlarmAt is { } a ? TimeOnly.FromDateTime(a.LocalDateTime) : new TimeOnly(9, 0));
        var month = (task.AlarmAt?.LocalDateTime ?? DateTime.Today).Date;
        month = new DateTime(month.Year, month.Month, 1);
        while (DateTime.DaysInMonth(month.Year, month.Month) < day) month = month.AddMonths(-1);
        var start = month.AddDays(day - 1).Add(time.ToTimeSpan());
        return task with { Repeat = new Recurrence { Unit = RepeatUnit.Month, Start = start }, MonthlyDay = null, MonthlyTime = null };
    }
}
