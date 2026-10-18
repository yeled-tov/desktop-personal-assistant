using System.Text.RegularExpressions;
using DesktopAssistant.Offline;
using DesktopAssistant.Settings;

namespace DesktopAssistant.Agent;

/// <summary>What the assistant can do and how — shown by the "?" button, answered locally to "מה את יודעת לעשות", and given to the AI.</summary>
public static class AssistantHelp
{
    private static readonly Regex HelpQuestion = new(@"^(מה (את|אתה) (יודעת|יודע|יכולה|יכול) (לעשות|לעזור)|מה אפשר לעשות (איתך|פה|כאן)|עזרה|help|what can you do|איך משתמשים( בך)?|מה היכולות שלך)\s*\??$", RegexOptions.IgnoreCase);

    public static bool IsHelpQuestion(string text) => HelpQuestion.IsMatch(text.Trim().TrimEnd('?', '!', '.'));

    public static string Text(AppSettings s, OfflineManager? offline = null)
    {
        var wake = s.WakeWordEnabled ? $"\"{s.WakePhrase}\" או " : "";
        var offlineLine = offline is null ? "" : offline.BrainReady && offline.SpeechReady ? "\n🔌 מצב אופליין מותקן — אני עובדת גם בלי אינטרנט." : "\n🔌 אפשר להוריד מצב אופליין (הגדרות ← אופליין) ואז אעבוד גם בלי אינטרנט ובלי מפתח API.";
        return $"""
            אני {s.AssistantName}, העוזרת שלך במחשב. הנה מה שאני יודעת לעשות:

            🗣 לדבר ולהקשיב — {s.VoiceHotkey} או 🎙, ואני מבצעת מה שתגידו.
            ✍ הכתבה — {s.DictationHotkey} או ✍: מדברים, ואני מקלידה את המילים בחלון שבו הייתם (Word, וואטסאפ, דפדפן…).
            🖥 להפעיל את המחשב — "פתחי וורד", "תסגרי את כרום", "תלחצי על שמור", "תעלי ווליום", "שיר הבא".
            📝 לכתוב בשבילך — "תכתבי ב-Word מכתב תודה למורה ותשמרי בשולחן העבודה".
            👀 לראות את המסך — "מה כתוב לי פה?", "תסבירי את השגיאה הזאת" (או ▣ לצרף את המסך).
            ◎ הקף וחפש — {s.CircleSearchHotkey} או לחיצה ארוכה על משטח המגע: המסך קופא, מקיפים משהו ואני מסבירה, מחפשת, מתרגמת או מעתיקה את הטקסט.
            🌐 תרגום מסך — {s.TranslateHotkey}: כל הטקסט במסך מתורגם במקום.
            📞 טלפון ווואטסאפ — "תתקשרי לאמא" (דרך 'קישור לטלפון'), "תשלחי בוואטסאפ ל-050… שאני בדרך".
            ⏰ תזכורות — "תזכירי לי בעוד 20 דקות להוציא כביסה".
            📂 קבצים — "מה הורדתי לאחרונה?", "תשמרי את זה כקובץ בשולחן העבודה".
            🧠 לזכור — "תזכרי שהמספר של אמא הוא…".
            ❓ סתם לשאול — כל שאלה, הסבר, ניסוח, תרגום או חישוב.

            לפתוח אותי: {wake}{s.AssistantHotkey}, או לחיצה על הסמל ליד השעון.
            כל הקיצורים ניתנים לשינוי בהגדרות ⚙ ← קיצורים.{offlineLine}
            """;
    }
}
