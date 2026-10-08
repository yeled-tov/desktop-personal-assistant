using System.Text.RegularExpressions;

namespace DesktopAssistant.Configuration;

public static class LocalMemory
{
    private static readonly string[] SecretTerms = ["סיסמה", "password", "api key", "מפתח api", "token", "טוקן"];
    public static bool TryHandle(string request, UserSettings settings, out string response, Action<UserSettings>? persist = null)
    {
        response = "";
        if (request.Contains("תשכח", StringComparison.OrdinalIgnoreCase) || request.Contains("שכח את הזיכרון", StringComparison.OrdinalIgnoreCase) || request.Contains("forget memory", StringComparison.OrdinalIgnoreCase))
        {
            settings.LocalMemory = ""; Persist(settings, persist); response = "מחקתי את הזיכרון המקומי."; return true;
        }
        var match = Regex.Match(request, @"(?:תזכור|זכור|remember)\s+(?:ש)?(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        var item = match.Groups[1].Value.Trim();
        if (SecretTerms.Any(term => item.Contains(term, StringComparison.OrdinalIgnoreCase))) { response = "אני לא שומר סיסמאות, מפתחות API או אסימוני גישה בזיכרון."; return true; }
        if (!settings.AllowLocalMemory) { response = "הזיכרון המקומי כבוי. אפשר להפעיל אותו בהגדרות."; return true; }
        settings.LocalMemory = string.Join(Environment.NewLine, new[] { settings.LocalMemory, item }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
        Persist(settings, persist); response = "שמרתי את זה בזיכרון המקומי במחשב הזה."; return true;
    }
    private static void Persist(UserSettings settings, Action<UserSettings>? persist) { if (persist is null) SettingsStore.Save(settings); else persist(settings); }
}
