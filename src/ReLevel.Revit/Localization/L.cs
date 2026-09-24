using System.Globalization;
using System.Text.Json;

namespace ReLevel.Revit;

internal static class L
{
    private static readonly Dictionary<string, string> English = Load();
    public static bool UseEnglish { get; set; } = true;
    public static string Get(string russian) => UseEnglish && English.TryGetValue(russian, out var value) ? value : russian;
    public static string Format(FormattableString text) => string.Format(
        CultureInfo.GetCultureInfo(UseEnglish ? "en-US" : "ru-RU"), Get(text.Format), text.GetArguments());

    // Only used for plugin-owned static UI labels, never model names or user input.
    public static string TranslateLabel(string text)
    {
        if (English.ContainsKey(text)) return Get(text);
        foreach (var pair in English)
            if (pair.Value == text) return Get(pair.Key);
        return text;
    }

    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("ReLevel.Revit.Localization.English.json")
            ?? throw new InvalidOperationException("The English translation resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("The English translation resource is empty.");
    }
}
