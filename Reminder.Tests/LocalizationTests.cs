using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Reminder.Tests;

public sealed class LocalizationTests : LocalizedTest
{
    // Embedded the same way as in the app: lang.<code>.json.
    private static Dictionary<string, Dictionary<string, string>> AppTables() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith("lang.", StringComparison.Ordinal))
            .ToDictionary(n => n[5..^5], n =>
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(n)!;
                return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
            });

    private static Dictionary<string, Dictionary<string, string>> InstallerTables() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "InstallerLocalization"), "*.json")
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f))!);

    public static TheoryData<string> Sets => new() { "app", "installer" };
    private static Dictionary<string, Dictionary<string, string>> Tables(string set) => set == "app" ? AppTables() : InstallerTables();

    private static string[] Placeholders(string text) =>
        Regex.Matches(text.Replace("{{", "").Replace("}}", ""), @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().Order().ToArray();

    [Fact]
    public void TheAppShipsEnglishSpanishAndPortuguese()
    {
        Assert.Equal(["en", "es", "pt"], AppTables().Keys.Order());
        Assert.Equal(["en", "es", "pt"], InstallerTables().Keys.Order());
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void Translations_UseTheSamePlaceholdersAsEnglish(string set)
    {
        // string.Format throws at runtime when a translation uses {1} and only one value is given.
        var tables = Tables(set);
        var problems = from table in tables
                       from entry in table.Value
                       where tables["en"].ContainsKey(entry.Key)
                       let expected = Placeholders(tables["en"][entry.Key])
                       where !Placeholders(entry.Value).SequenceEqual(expected)
                       select $"{table.Key}.json {entry.Key}: \"{entry.Value}\"";
        Assert.Empty(problems);
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void Translations_OnlyUseKeysThatExistInEnglish(string set)
    {
        // A misspelt key is never shown; the English text appears instead.
        var tables = Tables(set);
        Assert.Empty(tables.SelectMany(t => t.Value.Keys.Except(tables["en"].Keys).Select(k => $"{t.Key}.json {k}")));
    }

    [Fact]
    public void EveryLanguage_HasANameAndAValidCulture()
    {
        foreach (var (code, table) in AppTables())
        {
            Assert.False(string.IsNullOrWhiteSpace(table["_language"]), code);
            Assert.NotNull(CultureInfo.GetCultureInfo(table["_culture"]));
        }
    }

    [Fact]
    public void EveryLanguage_NamesSevenDays()
    {
        // Indexed by DayOfWeek in the editor and the repeat labels.
        foreach (var code in AppTables().Keys)
        {
            Loc.Set(code);
            Assert.Equal(7, Loc.T("DayInitials").Length);
            Assert.Equal(7, Loc.T("DayAbbreviations").Split(',').Length);
        }
    }

    [Fact]
    public void EveryLanguage_HasValidDateFormats()
    {
        var sample = new DateTime(2027, 12, 31, 23, 59, 0);
        foreach (var code in AppTables().Keys)
        {
            Loc.Set(code);
            foreach (var key in new[] { "fmt.DayMonth", "fmt.Date", "fmt.MonthDay", "fmt.Due", "fmt.Short", "fmt.Long", "fmt.Clock" })
                Assert.False(string.IsNullOrWhiteSpace(sample.ToString(Loc.T(key), Loc.Culture)), $"{code} {key}");
        }
    }

    [Fact]
    public void Set_SwitchesTextAndCulture_AndNotifies()
    {
        int changes = 0;
        Action count = () => changes++;
        Loc.Changed += count;
        try
        {
            Loc.Set("pt");
            Assert.Equal("pt", Loc.Language);
            Assert.Equal("pt-BR", Loc.Culture.Name);
            Assert.Equal("pt-BR", CultureInfo.CurrentCulture.Name);
            Assert.Equal(AppTables()["pt"]["Today"], Loc.T("Today"));
            Assert.Equal(1, changes);
        }
        finally { Loc.Changed -= count; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xx")]
    [InlineData("PT")]
    public void UnknownLanguages_FallBackToEnglish(string? code)
    {
        Assert.Equal("en", Loc.Normalize(code));
        Loc.Set(code);
        Assert.Equal("en", Loc.Language);
    }

    [Fact]
    public void MissingKeys_ShowTheKey()
    {
        Assert.Equal("NoSuchKey", Loc.T("NoSuchKey"));
    }

    [Fact]
    public void Languages_ListsEnglishFirst()
    {
        Assert.Equal("en", Loc.Languages[0].Code);
        Assert.Equal(AppTables().Count, Loc.Languages.Length);
    }
}
