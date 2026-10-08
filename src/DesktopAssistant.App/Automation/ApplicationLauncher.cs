using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopAssistant.Automation;

public sealed class ApplicationLauncher
{
    private static readonly Dictionary<string, string> Sites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"]="https://www.youtube.com", ["יוטיוב"]="https://www.youtube.com", ["gmail"]="https://mail.google.com", ["ג'ימייל"]="https://mail.google.com", ["גימייל"]="https://mail.google.com",
        ["google"]="https://www.google.com", ["גוגל"]="https://www.google.com", ["outlook"]="https://outlook.live.com", ["אאוטלוק"]="https://outlook.live.com", ["calendar"]="https://calendar.google.com", ["יומן"]="https://calendar.google.com"
    };

    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("אפשר לפתוח רק כתובות אינטרנט מסוג HTTP או HTTPS.");
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }

    public string OpenFriendly(string label)
    {
        var normalized = label.Trim().Trim('"', '\'', '.', ',', '!', '?');
        var mapped = Sites.FirstOrDefault(pair => normalized.Contains(pair.Key, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(mapped.Key)) { OpenUrl(mapped.Value); return $"פתחתי את {normalized}."; }
        if (normalized.Contains("מחשבון") || normalized.Contains("calculator", StringComparison.OrdinalIgnoreCase))
        { Process.Start(new ProcessStartInfo("calc.exe") { UseShellExecute = true }); return "פתחתי את המחשבון."; }
        if (normalized.Contains("כרום") || normalized.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        { Process.Start(new ProcessStartInfo(FindExecutable("chrome.exe", [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")])) { UseShellExecute = true }); return "פתחתי את Chrome."; }
        if (normalized.Contains("edge", StringComparison.OrdinalIgnoreCase) || normalized.Contains("אדג'"))
        { Process.Start(new ProcessStartInfo("msedge.exe") { UseShellExecute = true }); return "פתחתי את Microsoft Edge."; }
        if (normalized.Contains("firefox", StringComparison.OrdinalIgnoreCase) || normalized.Contains("פיירפוקס"))
        { Process.Start(new ProcessStartInfo("firefox.exe") { UseShellExecute = true }); return "פתחתי את Firefox."; }
        if (normalized.Contains("task manager", StringComparison.OrdinalIgnoreCase) || normalized.Contains("מנהל המשימות"))
        { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); return "פתחתי את מנהל המשימות."; }
        if (normalized.Contains("סייר הקבצים") || normalized.Contains("file explorer", StringComparison.OrdinalIgnoreCase))
        { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); return "פתחתי את סייר הקבצים."; }
        if (normalized.Contains("פתקים") || normalized.Contains("notepad", StringComparison.OrdinalIgnoreCase))
        { Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true }); return "פתחתי את פנקס הרשימות."; }
        if (normalized.Contains("הגדרות") || normalized.Contains("settings", StringComparison.OrdinalIgnoreCase))
        { Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }); return "פתחתי את הגדרות Windows."; }
        if (Regex.IsMatch(normalized, @"^https?://", RegexOptions.IgnoreCase)) { OpenUrl(normalized); return "פתחתי את האתר."; }
        throw new InvalidOperationException($"לא מצאתי אפליקציה או אתר בשם '{normalized}'. אפשר להוסיף אפליקציות מותאמות בהגדרות בגרסה הבאה.");
    }

    public string Search(string query, bool youtube = false)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("צריך לציין מה לחפש.");
        var baseUrl = youtube ? "https://www.youtube.com/results?search_query=" : "https://www.google.com/search?q=";
        OpenUrl(baseUrl + Uri.EscapeDataString(query));
        return youtube ? $"מחפש ביוטיוב: {query}" : $"מחפש בגוגל: {query}";
    }

    private static string FindExecutable(string fallback, IEnumerable<string> candidates) => candidates.FirstOrDefault(File.Exists) ?? fallback;

    public static bool TryCalculate(string expression, out string answer)
    {
        answer = "";
        var match = Regex.Match(expression, @"(-?\d+(?:[.,]\d+)?)\s*(?:כפול|פעמים|\*|×|x|X|ועוד|\+|פחות|−|-)\s*(-?\d+(?:[.,]\d+)?)");
        if (!match.Success) return false;
        var left = double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var right = double.Parse(match.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var op = match.Value[match.Groups[1].Length..].TrimStart();
        var value = op.StartsWith("כפול") || op.StartsWith("פעמים") || op.StartsWith("*") || op.StartsWith("×") || op.StartsWith("x", StringComparison.OrdinalIgnoreCase) ? left * right
            : op.StartsWith("ועוד") || op.StartsWith("+") ? left + right : left - right;
        answer = value.ToString("G", CultureInfo.InvariantCulture); return true;
    }
}
