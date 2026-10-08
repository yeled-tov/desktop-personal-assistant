using System.Text.RegularExpressions;
using DesktopAssistant.Configuration;

namespace DesktopAssistant.Automation;

public sealed record CommandResult(string Message, bool RequiresConfirmation = false, string ConfirmationText = "");

public sealed class CommandRouter(ApplicationLauncher launcher, ActionSafety safety)
{
    public Task<CommandResult> HandleAsync(string request)
    {
        if (LocalMemory.TryHandle(request, DesktopAssistant.App.Services.Settings, out var memoryMessage)) return Task.FromResult(new CommandResult(memoryMessage));
        if (safety.Classify(request) == ActionRisk.ConfirmationRequired)
            return Task.FromResult(new CommandResult("", true, $"הבקשה עשויה לבצע פעולה רגישה:\n\n{request}\n\nהאם להמשיך?"));

        if (ApplicationLauncher.TryCalculate(request, out var answer)) return Task.FromResult(new CommandResult($"התוצאה היא {answer}."));
        if (!DesktopAssistant.App.Services.Settings.EnableDesktopControl && Regex.IsMatch(request, @"(?:פתח|תפתח|חפש|תחפש|סגור|open|search|close|click|type)", RegexOptions.IgnoreCase))
            return Task.FromResult(new CommandResult("שליטה במחשב כבויה בהגדרות."));
        var youtube = request.Contains("יוטיוב", StringComparison.OrdinalIgnoreCase) || request.Contains("youtube", StringComparison.OrdinalIgnoreCase);
        var search = Regex.Match(request, @"(?:חפש|תחפש|חפשי|search)\s+(?:לי\s+)?(?:בגוגל|ביוטיוב|ב-google|ב-youtube)?\s*(.+)$", RegexOptions.IgnoreCase);
        if (search.Success) return Task.FromResult(new CommandResult(launcher.Search(search.Groups[1].Value.Trim(), youtube)));
        var close = Regex.Match(request, @"(?:סגור|סגרי|close)\s+(?:את\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (close.Success) return Task.FromResult(new CommandResult(WindowActions.TryCloseWindow(close.Groups[1].Value.Trim()) ? "ביקשתי מהחלון להיסגר." : "לא מצאתי חלון מתאים."));
        var open = Regex.Match(request, @"(?:תפתח|פתח|פתחי|open)\s+(?:לי\s+)?(?:את\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (open.Success) return Task.FromResult(new CommandResult(launcher.OpenFriendly(open.Groups[1].Value)));
        return Task.FromResult(new CommandResult("כדי לקבל תשובות AI צריך להפעיל ספק AI בענן בהגדרות ולהוסיף מפתח Gemini. כרגע זמינות פעולות מקומיות: פתיחת אפליקציות ואתרים, חיפוש וחישוב."));
    }
}
