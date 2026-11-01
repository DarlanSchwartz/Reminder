using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace Reminder;

// UI text, one JSON file per language in Localization/ (embedded in the app).
// "_language" is the name shown in the menu and "_culture" sets date formats;
// missing keys fall back to English. XAML binds through {local:Tr Key}, so
// switching the language updates open views without reopening them.
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    private static readonly Dictionary<string, Dictionary<string, string>> Tables = LoadTables();
    public static readonly (string Code, string Name)[] Languages = Tables.Select(t => (t.Key, t.Value["_language"])).OrderBy(l => l.Key != "en").ThenBy(l => l.Key).ToArray();
    public static string Language { get; private set; } = "en";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");
    public static XmlLanguage Xml => XmlLanguage.GetLanguage(Culture.IetfLanguageTag);
    public static event Action? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => T(key);

    public static string T(string key) => Tables[Language].TryGetValue(key, out var text) || Tables["en"].TryGetValue(key, out text) ? text : key;
    public static string F(string key, params object[] args) => string.Format(Culture, T(key), args);

    public static string Normalize(string? code) => code != null && Tables.ContainsKey(code) ? code : "en";

    private static Dictionary<string, Dictionary<string, string>> LoadTables()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var tables = new Dictionary<string, Dictionary<string, string>>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("lang.") && n.EndsWith(".json")))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            tables[name[5..^5]] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        }
        return tables;
    }

    public static void Set(string? code)
    {
        Language = Normalize(code);
        Culture = CultureInfo.GetCultureInfo(Tables[Language].GetValueOrDefault("_culture", "en-US"));
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Culture;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(Binding.IndexerName));
        Changed?.Invoke();
    }
}

// {local:Tr Key} binds a property to the current translation of Key.
public sealed class Tr(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider services) =>
        new Binding($"[{key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(services);
}
