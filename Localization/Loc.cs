using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace Reminder;

// UI text in English, Spanish and Portuguese. XAML binds through {local:Tr Key},
// so switching the language updates open views without reopening them.
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public static readonly (string Code, string Name)[] Languages = [("en", "English"), ("es", "Español"), ("pt", "Português")];
    public static string Language { get; private set; } = "en";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");
    public static XmlLanguage Xml => XmlLanguage.GetLanguage(Culture.IetfLanguageTag);
    public static event Action? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => T(key);

    public static string T(string key) => Strings.Table.TryGetValue(key, out var text) ? text[Index] : key;
    public static string F(string key, params object[] args) => string.Format(Culture, T(key), args);
    private static int Index => Language switch { "es" => 1, "pt" => 2, _ => 0 };

    public static string Normalize(string? code) => code is "es" or "pt" ? code : "en";

    public static void Set(string? code)
    {
        Language = Normalize(code);
        Culture = CultureInfo.GetCultureInfo(Language switch { "es" => "es-ES", "pt" => "pt-BR", _ => "en-US" });
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
