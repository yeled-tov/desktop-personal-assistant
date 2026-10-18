using System.Diagnostics;
using DesktopAssistant.Core;

namespace DesktopAssistant.Desktop;

public sealed record InstalledApp(string Name, string Target, bool IsShellApp);

/// <summary>Opens applications by friendly name (Hebrew or English), websites, files and URIs.</summary>
public static class AppLauncher
{
    private sealed record Alias(string[] Names, string Command, string[] Processes);

    private static readonly Alias[] Aliases =
    [
        new(["word", "וורד", "ורד", "מייקרוסופט וורד", "microsoft word"], "winword", ["WINWORD"]),
        new(["excel", "אקסל", "אקסעל"], "excel", ["EXCEL"]),
        new(["powerpoint", "פאוורפוינט", "פאורפוינט", "מצגת"], "powerpnt", ["POWERPNT"]),
        new(["outlook", "אאוטלוק", "אוטלוק"], "outlook", ["OUTLOOK", "olk"]),
        new(["onenote", "וואן נוט"], "onenote", ["ONENOTE"]),
        new(["notepad", "פנקס רשימות", "נוטפד", "פנקס"], "notepad", ["Notepad"]),
        new(["calculator", "calc", "מחשבון"], "calc", ["CalculatorApp", "Calculator"]),
        new(["paint", "צייר", "פיינט"], "mspaint", ["mspaint"]),
        new(["file explorer", "explorer", "סייר הקבצים", "סייר", "קבצים", "תיקיות"], "explorer", ["explorer"]),
        new(["command prompt", "cmd", "שורת הפקודה"], "cmd", ["cmd"]),
        new(["powershell", "פאוורשל"], "powershell", ["powershell", "pwsh"]),
        new(["terminal", "טרמינל"], "wt", ["WindowsTerminal"]),
        new(["chrome", "google chrome", "כרום", "גוגל כרום"], "chrome", ["chrome"]),
        new(["edge", "microsoft edge", "אדג'", "אדג"], "msedge", ["msedge"]),
        new(["firefox", "פיירפוקס"], "firefox", ["firefox"]),
        new(["settings", "הגדרות", "הגדרות windows", "הגדרות המחשב"], "ms-settings:", ["SystemSettings"]),
        new(["wifi", "וויפיי", "רשת"], "ms-settings:network-wifi", ["SystemSettings"]),
        new(["bluetooth", "בלוטוס", "בלוטות'"], "ms-settings:bluetooth", ["SystemSettings"]),
        new(["display settings", "הגדרות תצוגה", "מסך"], "ms-settings:display", ["SystemSettings"]),
        new(["sound settings", "הגדרות קול", "שמע"], "ms-settings:sound", ["SystemSettings"]),
        new(["windows update", "עדכונים"], "ms-settings:windowsupdate", ["SystemSettings"]),
        new(["whatsapp", "וואטסאפ", "ווטסאפ", "ווצאפ", "וואצאפ"], "whatsapp:", ["WhatsApp", "WhatsApp.Root"]),
        new(["phone link", "קישור לטלפון", "טלפון", "your phone"], "ms-phone:", ["PhoneExperienceHost"]),
        new(["spotify", "ספוטיפיי"], "spotify:", ["Spotify"]),
        new(["camera", "מצלמה"], "microsoft.windows.camera:", ["WindowsCamera"]),
        new(["microsoft store", "store", "חנות"], "ms-windows-store:", ["WinStore.App"]),
        new(["teams", "טימס"], "msteams:", ["ms-teams", "Teams"]),
        new(["vscode", "vs code", "visual studio code", "קוד"], "code", ["Code"]),
        new(["task manager", "מנהל המשימות", "מנהל משימות"], "taskmgr", ["Taskmgr"]),
        new(["control panel", "לוח הבקרה"], "control", ["control"]),
        new(["snipping tool", "צילום מסך", "כלי החיתוך"], "ms-screenclip:", ["SnippingTool"]),
        new(["clock", "alarms", "שעון", "שעון מעורר", "טיימר"], "ms-clock:", ["Time"]),
        new(["calendar", "יומן"], "outlookcal:", ["olk", "HxCalendarAppImm"]),
        new(["photos", "תמונות"], "ms-photos:", ["Photos"]),
        new(["mail", "דואר"], "outlookmail:", ["olk", "HxOutlook"]),
        new(["weather", "מזג אוויר"], "msnweather:", ["Microsoft.Msn.Weather"]),
        new(["maps", "מפות"], "https://www.google.com/maps", ["chrome", "msedge"]),
        new(["youtube", "יוטיוב"], "https://www.youtube.com", ["chrome", "msedge"]),
        new(["gmail", "ג'ימייל", "גימייל", "ג׳ימייל"], "https://mail.google.com", ["chrome", "msedge"]),
        new(["google", "גוגל"], "https://www.google.com", ["chrome", "msedge"]),
        new(["google translate", "גוגל טרנסלייט", "מתרגם"], "https://translate.google.com", ["chrome", "msedge"]),
        new(["chatgpt", "צ'אט ג'יפיטי"], "https://chatgpt.com", ["chrome", "msedge"]),
        new(["google drive", "דרייב"], "https://drive.google.com", ["chrome", "msedge"]),
        new(["google docs", "גוגל דוקס"], "https://docs.google.com/document/create", ["chrome", "msedge"]),
        new(["whatsapp web", "וואטסאפ ווב"], "https://web.whatsapp.com", ["chrome", "msedge"]),
    ];

    private static IReadOnlyList<InstalledApp>? _installed;
    private static DateTime _installedAt;

    public static string[] ProcessAliases(string name)
    {
        var n = Normalize(name);
        return Aliases.Where(a => a.Names.Any(x => Normalize(x) == n)).SelectMany(a => a.Processes).ToArray();
    }

    public static async Task<string> OpenAsync(string name, string? arguments = null, CancellationToken cancellationToken = default)
    {
        var raw = name.Trim().Trim('"', '\'', '.', '!', '?');
        if (raw.Length == 0) throw new ArgumentException("לא צוין מה לפתוח.");
        var n = Normalize(raw);

        if (LooksLikeUrl(raw)) { OpenUrl(raw.Contains("://") ? raw : "https://" + raw); return $"נפתח האתר {raw}."; }
        if ((raw.Contains(":\\") || raw.StartsWith("\\\\")) && (File.Exists(raw) || Directory.Exists(raw))) { ShellOpen(raw, arguments); return $"נפתח: {raw}"; }

        var alias = Aliases.FirstOrDefault(a => a.Names.Any(x => Normalize(x) == n)) ?? Aliases.FirstOrDefault(a => a.Names.Any(x => x.Length > 3 && n.Contains(Normalize(x))));
        if (alias is not null)
        {
            try { ShellOpen(alias.Command, arguments); return $"פתחתי את {raw}."; }
            catch (Exception ex) { AppLog.Warn($"Alias launch failed for {alias.Command}: {ex.Message}"); }
        }

        var installed = await GetInstalledAppsAsync(cancellationToken);
        var match = BestMatch(installed, n);
        if (match is not null)
        {
            if (match.IsShellApp) ShellOpen("explorer.exe", $"shell:AppsFolder\\{match.Target}");
            else ShellOpen(match.Target, arguments);
            return $"פתחתי את {match.Name}.";
        }

        // Last resort: let Windows resolve it (App Paths, PATH).
        try { ShellOpen(raw, arguments); return $"ביקשתי מ-Windows לפתוח את {raw}."; }
        catch { }
        var suggestions = installed.Where(a => a.Name.Contains(raw.Split(' ')[0], StringComparison.OrdinalIgnoreCase)).Take(5).Select(a => a.Name).ToList();
        throw new InvalidOperationException(suggestions.Count > 0
            ? $"לא מצאתי בדיוק את '{raw}'. אולי התכוונת ל: {string.Join(", ", suggestions)}"
            : $"לא מצאתי אפליקציה בשם '{raw}' במחשב. אפשר להשתמש ב-list_installed_apps כדי לראות מה מותקן.");
    }

    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new ArgumentException($"כתובת לא תקינה: {url}");
        ShellOpen(uri.AbsoluteUri, null);
    }

    public static string Search(string query, string engine = "google")
    {
        var q = Uri.EscapeDataString(query.Trim());
        var url = engine.ToLowerInvariant() switch
        {
            "youtube" => $"https://www.youtube.com/results?search_query={q}",
            "maps" => $"https://www.google.com/maps/search/{q}",
            "images" => $"https://www.google.com/search?tbm=isch&q={q}",
            "bing" => $"https://www.bing.com/search?q={q}",
            "wikipedia" => $"https://he.wikipedia.org/w/index.php?search={q}",
            "translate" => $"https://translate.google.com/?sl=auto&tl=iw&text={q}",
            _ => $"https://www.google.com/search?q={q}"
        };
        OpenUrl(url);
        return $"חיפשתי: {query}";
    }

    public static void ShellOpen(string target, string? arguments)
    {
        var info = new ProcessStartInfo(target) { UseShellExecute = true };
        if (!string.IsNullOrWhiteSpace(arguments)) info.Arguments = arguments;
        Process.Start(info);
    }

    public static string ResolveKnownFolder(string path)
    {
        var p = path.Trim();
        string? Known(string key) => Normalize(key) switch
        {
            "desktop" or "שולחן העבודה" or "שולחן עבודה" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "documents" or "מסמכים" or "המסמכים" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "downloads" or "הורדות" or "ההורדות" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "pictures" or "תמונות" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" or "מוזיקה" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" or "סרטונים" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "home" or "בית" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            _ => null
        };
        var direct = Known(p);
        if (direct is not null) return direct;
        var slash = p.IndexOfAny(['\\', '/']);
        if (slash > 0 && Known(p[..slash]) is { } root) return Path.Combine(root, p[(slash + 1)..]);
        return Environment.ExpandEnvironmentVariables(p);
    }

    public static async Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(CancellationToken cancellationToken = default)
    {
        if (_installed is not null && DateTime.UtcNow - _installedAt < TimeSpan.FromMinutes(10)) return _installed;
        var apps = new List<InstalledApp>();
        try
        {
            // Shell AppsFolder = exactly what the Start menu "All apps" shows: desktop + Store apps.
            var shellApps = await StaRunner.RunAsync(() =>
            {
                var list = new List<InstalledApp>();
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type is null) return list;
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                dynamic items = folder.Items();
                int count = items.Count;
                for (var i = 0; i < count; i++)
                {
                    dynamic item = items.Item(i);
                    string itemName = item.Name; string itemPath = item.Path;
                    if (!string.IsNullOrWhiteSpace(itemName) && !string.IsNullOrWhiteSpace(itemPath)) list.Add(new InstalledApp(itemName, itemPath, true));
                }
                return list;
            }, cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            apps.AddRange(shellApps);
        }
        catch (Exception ex) { AppLog.Warn("AppsFolder enumeration failed: " + ex.Message); }

        if (apps.Count == 0)
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) })
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase)))
                        apps.Add(new InstalledApp(Path.GetFileNameWithoutExtension(file), file, false));
                }
                catch { }
            }
        }
        _installed = apps.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(a => a.Name).ToList();
        _installedAt = DateTime.UtcNow;
        return _installed;
    }

    private static InstalledApp? BestMatch(IReadOnlyList<InstalledApp> apps, string normalizedQuery)
    {
        var bad = new[] { "uninstall", "הסר", "readme", "help", "documentation", "website" };
        var candidates = apps.Where(a => !bad.Any(b => a.Name.Contains(b, StringComparison.OrdinalIgnoreCase))).ToList();
        return candidates.FirstOrDefault(a => Normalize(a.Name) == normalizedQuery)
            ?? candidates.Where(a => Normalize(a.Name).StartsWith(normalizedQuery)).OrderBy(a => a.Name.Length).FirstOrDefault()
            ?? candidates.Where(a => Normalize(a.Name).Contains(normalizedQuery)).OrderBy(a => a.Name.Length).FirstOrDefault()
            ?? candidates.Where(a => normalizedQuery.Length > 3 && normalizedQuery.Contains(Normalize(a.Name)) && a.Name.Length > 3).OrderByDescending(a => a.Name.Length).FirstOrDefault();
    }

    private static bool LooksLikeUrl(string text) =>
        text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        (!text.Contains(' ') && System.Text.RegularExpressions.Regex.IsMatch(text, @"^[\w-]+(\.[\w-]+)*\.(com|net|org|io|co|il|co\.il|gov|edu|app|dev|ai|me|tv)(/.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    public static string Normalize(string text)
    {
        var t = text.Trim().ToLowerInvariant().Replace("׳", "'").Replace("״", "\"");
        foreach (var prefix in new[] { "את ", "אפליקציית ", "אפליקציה ", "תוכנת ", "תוכנה ", "האפליקציה ", "התוכנה " })
            if (t.StartsWith(prefix, StringComparison.Ordinal)) t = t[prefix.Length..];
        // "הוורד" -> "וורד", "המחשבון" -> "מחשבון" (only when that yields a known name)
        if (t.Length > 3 && t[0] == 'ה' && Aliases.Any(a => a.Names.Contains(t[1..]))) t = t[1..];
        return t.Trim();
    }
}
