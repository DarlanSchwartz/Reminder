using Xunit;

namespace Reminder.Tests;

public sealed class RecurrenceTests : LocalizedTest
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static DateTimeOffset At(int y, int mo, int d, int h = 9, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    [Fact]
    public void Monthly_ClampsToShortMonths_AndRestoresTheAnchorDay()
    {
        var monthly = new Recurrence { Unit = RepeatUnit.Month, Start = new DateTime(2027, 1, 31, 9, 0, 0) };
        var february = monthly.Next(At(2027, 1, 31), Utc);
        Assert.Equal(At(2027, 2, 28), february);
        Assert.Equal(At(2027, 3, 31), monthly.Next(february!.Value, Utc));
        Assert.Equal(At(2028, 2, 29), monthly.Next(At(2028, 1, 31), Utc));
    }

    [Fact]
    public void Monthly_RollsOverTheYear_AndFindsLaterDaysInTheSameMonth()
    {
        var day10 = new Recurrence { Unit = RepeatUnit.Month, Start = new DateTime(2026, 1, 10, 9, 0, 0) };
        Assert.Equal(At(2027, 1, 10), day10.Next(At(2026, 12, 11), Utc));
        Assert.Equal(At(2026, 9, 10), day10.Next(At(2026, 9, 1), Utc));
    }

    [Fact]
    public void Monthly_WithInterval_SkipsMonths()
    {
        var quarterly = new Recurrence { Unit = RepeatUnit.Month, Interval = 3, Start = new DateTime(2027, 1, 31, 9, 0, 0) };
        Assert.Equal(At(2027, 4, 30), quarterly.Next(At(2027, 2, 1), Utc));
    }

    [Fact]
    public void Daily_StartsAtTheAnchor_AndEndsAfterUntil()
    {
        var daily = new Recurrence { Unit = RepeatUnit.Day, Start = new DateTime(2027, 3, 1, 8, 30, 0), Until = new DateTime(2027, 3, 3) };
        Assert.Equal(At(2027, 3, 1, 8, 30), daily.Next(At(2027, 2, 1), Utc));
        Assert.Equal(At(2027, 3, 2, 8, 30), daily.Next(At(2027, 3, 1, 8, 30), Utc));
        Assert.Equal(At(2027, 3, 3, 8, 30), daily.Next(At(2027, 3, 3, 8, 0), Utc));
        Assert.Null(daily.Next(At(2027, 3, 3, 8, 30), Utc));
    }

    [Fact]
    public void Daily_WithInterval_SkipsDays()
    {
        var everyOther = new Recurrence { Unit = RepeatUnit.Day, Interval = 2, Start = new DateTime(2027, 3, 1, 8, 30, 0) };
        Assert.Equal(At(2027, 3, 3, 8, 30), everyOther.Next(At(2027, 3, 1, 9), Utc));
    }

    [Fact]
    public void Next_IsStrictlyAfterTheGivenTime()
    {
        var daily = new Recurrence { Unit = RepeatUnit.Day, Start = new DateTime(2027, 3, 1, 8, 30, 0) };
        Assert.Equal(At(2027, 3, 2, 8, 30), daily.Next(At(2027, 3, 1, 8, 30), Utc));
        Assert.Equal(At(2027, 3, 1, 8, 30), daily.Next(At(2027, 3, 1, 8, 30).AddTicks(-1), Utc));
    }

    [Fact]
    public void Weekly_VisitsEachChosenDay()
    {
        // 2027-03-01 is a Monday.
        var weekly = new Recurrence { Unit = RepeatUnit.Week, Start = new DateTime(2027, 3, 1, 7, 0, 0), Days = WeekDays.Monday | WeekDays.Friday };
        Assert.Equal(At(2027, 3, 5, 7), weekly.Next(At(2027, 3, 1, 7), Utc));
        Assert.Equal(At(2027, 3, 8, 7), weekly.Next(At(2027, 3, 5, 7), Utc));
    }

    [Fact]
    public void Weekly_WithInterval_SkipsWeeks()
    {
        var biweekly = new Recurrence { Unit = RepeatUnit.Week, Interval = 2, Start = new DateTime(2027, 3, 1, 7, 0, 0), Days = WeekDays.Monday | WeekDays.Friday };
        Assert.Equal(At(2027, 3, 15, 7), biweekly.Next(At(2027, 3, 5, 7), Utc));
        Assert.Equal(At(2027, 4, 26, 7), biweekly.Next(At(2027, 4, 20), Utc));
    }

    [Fact]
    public void Weekly_WithoutDays_UsesTheStartWeekday()
    {
        var weekly = new Recurrence { Unit = RepeatUnit.Week, Start = new DateTime(2027, 3, 1, 7, 0, 0) };
        Assert.Equal(At(2027, 3, 8, 7), weekly.Next(At(2027, 3, 1, 7), Utc));
    }

    [Fact]
    public void Weekly_NeverRingsBeforeTheStartDate()
    {
        // Start on a Wednesday with Monday also chosen: the Monday of that first week is skipped.
        var weekly = new Recurrence { Unit = RepeatUnit.Week, Start = new DateTime(2027, 3, 3, 7, 0, 0), Days = WeekDays.Monday | WeekDays.Wednesday };
        Assert.Equal(At(2027, 3, 3, 7), weekly.Next(At(2027, 2, 1), Utc));
    }

    [Fact]
    public void Hourly_KeepsTheMinuteOfTheAnchor()
    {
        var hourly = new Recurrence { Unit = RepeatUnit.Hour, Interval = 6, Start = new DateTime(2027, 3, 1, 1, 15, 0) };
        Assert.Equal(At(2027, 3, 2, 7, 15), hourly.Next(At(2027, 3, 2, 5), Utc));
    }

    [Fact]
    public void Yearly_OnFebruary29_UsesFebruary28InCommonYears()
    {
        var yearly = new Recurrence { Unit = RepeatUnit.Year, Start = new DateTime(2028, 2, 29, 10, 0, 0) };
        Assert.Equal(At(2029, 2, 28, 10), yearly.Next(At(2028, 3, 1), Utc));
        Assert.Equal(At(2032, 2, 29, 10), yearly.Next(At(2031, 3, 1), Utc));
    }

    [Fact]
    public void Until_EndsTheSeriesBeforeTheFirstOccurrence()
    {
        var ended = new Recurrence { Unit = RepeatUnit.Day, Start = new DateTime(2027, 3, 5, 9, 0, 0), Until = new DateTime(2027, 3, 4) };
        Assert.Null(ended.Next(At(2027, 1, 1), Utc));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Next_RejectsANonPositiveInterval(int interval)
    {
        var broken = new Recurrence { Unit = RepeatUnit.Day, Interval = interval, Start = new DateTime(2027, 3, 1, 9, 0, 0) };
        Assert.Throws<ArgumentOutOfRangeException>(() => broken.Next(At(2027, 3, 1), Utc));
    }

    [Fact]
    public void SpringForward_MovesAMissingTimeToTheFirstValidMinute()
    {
        // In New York, 2027-03-14 02:00 jumps to 03:00, so 02:30 doesn't exist that day.
        var daily = new Recurrence { Unit = RepeatUnit.Day, Start = new DateTime(2027, 3, 13, 2, 30, 0) };
        var next = daily.Next(new DateTimeOffset(2027, 3, 13, 2, 30, 0, TimeSpan.FromHours(-5)), NewYork);
        Assert.Equal(new DateTimeOffset(2027, 3, 14, 3, 0, 0, TimeSpan.FromHours(-4)), next);
    }

    [Fact]
    public void FallBack_RingsOnceForARepeatedTime()
    {
        // In New York, 2027-11-07 01:00-02:00 happens twice. The alarm rings once that day.
        var daily = new Recurrence { Unit = RepeatUnit.Day, Start = new DateTime(2027, 11, 6, 1, 30, 0) };
        var first = daily.Next(new DateTimeOffset(2027, 11, 6, 1, 30, 0, TimeSpan.FromHours(-4)), NewYork)!.Value;
        Assert.Equal(new DateTime(2027, 11, 7, 1, 30, 0), first.DateTime);
        var after = daily.Next(first, NewYork)!.Value;
        Assert.Equal(new DateTime(2027, 11, 8, 1, 30, 0), after.DateTime);
        Assert.Equal(TimeSpan.FromHours(-5), after.Offset);
    }

    [Fact]
    public void Next_KeepsTheWallClockTimeAcrossDaylightSaving()
    {
        var daily = new Recurrence { Unit = RepeatUnit.Day, Start = new DateTime(2027, 3, 13, 9, 0, 0) };
        var beforeChange = daily.Next(new DateTimeOffset(2027, 3, 13, 0, 0, 0, TimeSpan.FromHours(-5)), NewYork)!.Value;
        var afterChange = daily.Next(beforeChange, NewYork)!.Value;
        Assert.Equal(9, beforeChange.Hour);
        Assert.Equal(9, afterChange.Hour);
        Assert.Equal(TimeSpan.FromHours(-5), beforeChange.Offset);
        Assert.Equal(TimeSpan.FromHours(-4), afterChange.Offset);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Next_TreatsStartAsWallClockTime_WhateverItsKind(DateTimeKind kind)
    {
        // Used to throw ArgumentException when the kind's zone differed from `zone`.
        var daily = new Recurrence { Unit = RepeatUnit.Day, Start = DateTime.SpecifyKind(new DateTime(2027, 6, 1, 9, 0, 0), kind) };
        var next = daily.Next(new DateTimeOffset(2027, 6, 1, 10, 0, 0, TimeSpan.FromHours(-4)), NewYork);
        Assert.Equal(new DateTimeOffset(2027, 6, 2, 9, 0, 0, TimeSpan.FromHours(-4)), next);
    }

    [Fact]
    public void Labels_DescribeTheSeriesInTheCurrentLanguage()
    {
        var weekly = new Recurrence { Unit = RepeatUnit.Week, Interval = 2, Start = new DateTime(2027, 3, 1, 7, 0, 0), Days = WeekDays.Monday | WeekDays.Friday, Until = new DateTime(2027, 6, 30) };
        Assert.Equal("Every 2 weeks · Mon, Fri · until 06/30", weekly.ShortLabel);
        Assert.Equal("Every 2 weeks (Mon, Fri) at 07:00, until 06/30/2027.", weekly.Describe());

        var weekdays = weekly with { Days = (WeekDays)62, Until = null };
        Assert.Equal(Loc.F("EveryWeeks", 2) + " · " + Loc.T("MonToFri"), weekdays.ShortLabel);
        Assert.EndsWith(Loc.T("NoEnd"), weekdays.Describe());

        Loc.Set("pt");
        Assert.Contains("seg, sex", weekly.ShortLabel);
    }

    [Fact]
    public void Advance_MovesARepeatingTaskToItsNextOccurrence()
    {
        var now = DateTimeOffset.Now;
        var task = new TaskItem { AlarmAt = now.AddMinutes(-1), AlertShown = true, Completed = true, Repeat = new Recurrence { Unit = RepeatUnit.Day, Start = DateTime.Today.AddDays(-3).AddHours(9) } };
        var next = Recurring.Advance(task, now);
        Assert.True(next.AlarmAt > now);
        Assert.False(next.AlertShown);
        Assert.False(next.Completed);
    }

    [Fact]
    public void Advance_MarksAOneTimeOrEndedTaskAsDelivered()
    {
        var now = DateTimeOffset.Now;
        var once = new TaskItem { AlarmAt = now.AddMinutes(-1) };
        Assert.True(Recurring.Advance(once, now).AlertShown);

        var ended = once with { Repeat = new Recurrence { Unit = RepeatUnit.Day, Start = DateTime.Today.AddDays(-5).AddHours(9), Until = DateTime.Today.AddDays(-1) } };
        var result = Recurring.Advance(ended, now);
        Assert.True(result.AlertShown);
        Assert.Equal(ended.AlarmAt, result.AlarmAt);
    }
}
