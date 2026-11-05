using Xunit;

namespace Reminder.Tests;

public sealed class AlarmTimeParserTests
{
    [Theory]
    [InlineData("0", "00:00")]
    [InlineData("9", "09:00")]
    [InlineData("930", "09:30")]
    [InlineData("0930", "09:30")]
    [InlineData("9:3", "09:03")]
    [InlineData("23:59", "23:59")]
    [InlineData(" 930 ", "09:30")]
    [InlineData("00:00", "00:00")]
    public void TryParse_NormalizesValidTimes(string input, string expected)
    {
        Assert.True(AlarmTimeParser.TryParse(input, out var time));
        Assert.Equal(expected, time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("24:00")]
    [InlineData("2360")]
    [InlineData("9:99")]
    [InlineData("-1:00")]
    [InlineData("12::30")]
    [InlineData("ab:cd")]
    [InlineData("9:")]
    [InlineData(":30")]
    [InlineData("12345")]
    [InlineData("1:2:3")]
    [InlineData("٠٩:٣٠")] // Non-ASCII digits
    public void TryParse_RejectsInvalidTimes(string input)
    {
        Assert.False(AlarmTimeParser.TryParse(input, out _));
    }

    [Theory]
    [InlineData("0123456789:", true)]
    [InlineData("12a", false)]
    [InlineData("12.30", false)]
    [InlineData("", true)]
    public void HasAllowedCharacters_AcceptsOnlyDigitsAndColons(string input, bool expected)
    {
        Assert.Equal(expected, AlarmTimeParser.HasAllowedCharacters(input));
    }
}
