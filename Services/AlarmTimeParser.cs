using System;
using System.Globalization;
using System.Linq;

namespace Reminder;

internal static class AlarmTimeParser
{
    public static bool HasAllowedCharacters(string text) => text.All(c => c is >= '0' and <= '9' or ':');

    public static bool TryParse(string text, out TimeOnly time)
    {
        time = default;
        text = text.Trim();
        if (text.Length == 0 || !HasAllowedCharacters(text)) return false;
        string hours, minutes;
        if (text.Contains(':'))
        {
            var parts = text.Split(':');
            if (parts.Length != 2 || parts[0].Length is < 1 or > 2 || parts[1].Length is < 1 or > 2) return false;
            hours = parts[0]; minutes = parts[1];
        }
        else
        {
            if (text.Length > 4) return false;
            hours = text.Length <= 2 ? text : text[..^2];
            minutes = text.Length <= 2 ? "00" : text[^2..];
        }
        if (!int.TryParse(hours, NumberStyles.None, CultureInfo.InvariantCulture, out int h) ||
            !int.TryParse(minutes, NumberStyles.None, CultureInfo.InvariantCulture, out int m) || h > 23 || m > 59) return false;
        time = new TimeOnly(h, m);
        return true;
    }
}
