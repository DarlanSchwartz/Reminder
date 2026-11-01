using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization.Json;

namespace ReminderSetup;

// Setup text from Installer/Localization/<language>.json, chosen from the Windows display language.
// Missing languages and keys fall back to English.
internal static class Text
{
    private static readonly Dictionary<string, string> English = Load("en");
    private static readonly Dictionary<string, string> Current = Load(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? English;

    public static string T(string key) => Current.TryGetValue(key, out var text) || English.TryGetValue(key, out text) ? text : key;

    private static Dictionary<string, string> Load(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"lang.{code}.json");
        if (stream == null) return null;
        var json = new DataContractJsonSerializer(typeof(Dictionary<string, string>), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        return (Dictionary<string, string>)json.ReadObject(stream);
    }
}
