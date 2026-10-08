namespace DesktopAssistant.Localization;

/// <summary>Centralized labels for runtime-selected UI language.</summary>
public static class UiText
{
    private static readonly IReadOnlyDictionary<string, (string He, string En)> Labels = new Dictionary<string, (string, string)>
    {
        ["title"] = ("עוזר אישי למחשב", "Desktop Personal Assistant"),
        ["settings"] = ("הגדרות", "Settings"),
        ["ready"] = ("מוכן", "Ready"),
        ["send"] = ("שלח", "Send"),
        ["translate"] = ("תרגם מסך", "Translate screen"),
        ["cancel"] = ("ביטול", "Cancel")
    };
    public static string Get(string key, string language = "he") => Labels.TryGetValue(key, out var value) ? (language.Equals("en", StringComparison.OrdinalIgnoreCase) ? value.En : value.He) : key;
    public static bool IsRightToLeft(string language) => language.Equals("he", StringComparison.OrdinalIgnoreCase) || language.Equals("ar", StringComparison.OrdinalIgnoreCase);
}
