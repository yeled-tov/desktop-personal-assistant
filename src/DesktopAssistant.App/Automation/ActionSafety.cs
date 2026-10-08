namespace DesktopAssistant.Automation;

public enum ActionRisk { Safe, ConfirmationRequired }

public sealed class ActionSafety
{
    private static readonly string[] Risky = ["שלח", "מחק", "קנה", "רכוש", "התקן", "הסר התקנה", "submit", "send", "delete", "purchase", "buy", "checkout", "install", "uninstall", "powershell", "פקודת מערכת", "security settings"];
    public ActionRisk Classify(string request) => Risky.Any(token => request.Contains(token, StringComparison.OrdinalIgnoreCase)) ? ActionRisk.ConfirmationRequired : ActionRisk.Safe;
}
