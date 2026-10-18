using System.Globalization;
using System.Text.RegularExpressions;
using DesktopAssistant.Desktop;
using DesktopAssistant.Settings;

namespace DesktopAssistant.Agent;

/// <summary>
/// Everyday commands answered instantly on the PC — no AI call, no quota, works offline.
/// Anything that isn't a clear simple command goes to the AI as usual.
/// </summary>
public static class QuickCommands
{
    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex Open = new(@"^(?:תפתח|תפתחי|תפתחו|פתח|פתחי|תפעיל|תפעילי|הפעל|הפעילי|open|launch|start)\s+(?:לי\s+)?(?:את\s+)?(?<what>.{2,40})$", O);
    private static readonly Regex Close = new(@"^(?:תסגור|תסגרי|סגור|סגרי|close)\s+(?:לי\s+)?(?:את\s+)?(?<what>.{2,40})$", O);
    private static readonly Regex Search = new(@"^(?:חפש|חפשי|תחפש|תחפשי|search|google)\s+(?:לי\s+)?(?:ב(?<where>גוגל|יוטיוב|מפות|ויקיפדיה|תמונות)\s+)?(?<q>.+)$", O);
    private static readonly Regex Play = new(@"^(?:תנגן|תנגני|נגן|נגני|תשמיע|תשמיעי|השמע)\s+(?:לי\s+)?(?:ביוטיוב\s+)?(?<q>.{2,})$", O);
    private static readonly Regex Type = new(@"^(?:תקליד|תקלידי|הקלד|הקלידי|תרשום|תרשמי|type)(?:\s+לי)?(?:\s+(?:כאן|פה))?\s*[:\-–]?\s*(?<text>.+)$", O | RegexOptions.Singleline);
    private static readonly Regex Timer = new(@"^(?:תזכיר|תזכירי|תזכירו)\s+לי\s+בעוד\s+(?<n>\d+(?:[.,]\d+)?|חצי|רבע|שעה|שעתיים|דקה|דקותיים)\s*(?<unit>דקות|דקה|שעות|שעה|שניות)?\s*(?:ל|ש|על|את)?\s*(?<what>.*)$", O);
    private static readonly Regex TimerShort = new(@"^(?:טיימר|תשים טיימר|שים טיימר|תשימי טיימר)\s+(?:ל|של\s+)?(?<n>\d+)\s*(?<unit>דקות|דקה|שעות|שעה|שניות)?$", O);
    private static readonly Regex Remember = new(@"^(?:תזכור|תזכרי|זכור|זכרי|remember)\s+(?:ש|that\s+)?(?<fact>.{3,})$", O);

    public static async Task<string?> TryHandleAsync(string input, AgentContext ctx, CancellationToken cancellationToken)
    {
        var t = Regex.Replace(input.Trim(), @"[\s]+", " ").TrimEnd('.', '!', '?');
        if (t.Length == 0) return null;
        var lower = t.ToLowerInvariant();

        if (AssistantHelp.IsHelpQuestion(t)) return ctx.Help();

        // ---- time & date ----
        if (Regex.IsMatch(lower, @"^(מה ה?שעה( עכשיו)?|כמה השעה|what time is it)$")) return $"השעה {DateTime.Now:HH:mm}.";
        if (Regex.IsMatch(lower, @"^(מה ה?תאריך( היום)?|איזה יום (היום|זה)|איזה תאריך היום|what('s| is) the date( today)?)$"))
        {
            var he = new CultureInfo("he-IL");
            var hebrewCalendar = new CultureInfo("he-IL");
            hebrewCalendar.DateTimeFormat.Calendar = new HebrewCalendar();
            string hebrewDate;
            try { hebrewDate = DateTime.Now.ToString("d MMMM yyyy", hebrewCalendar); } catch { hebrewDate = ""; }
            return $"היום {DateTime.Now.ToString("dddd, d בMMMM yyyy", he)}{(hebrewDate.Length > 0 ? $" ({hebrewDate})" : "")}.";
        }

        // ---- media & volume ----
        string? media = lower switch
        {
            _ when Regex.IsMatch(lower, @"^(תעלה|תעלי|הגבר|הגבירי|תגביר|תגבירי)\s+(את\s+)?(ה)?(ווליום|וליום|עוצמה|קול|סאונד)") => "VOLUME_UP",
            _ when Regex.IsMatch(lower, @"^(תוריד|תורידי|הנמך|הנמיכי|תנמיך|תנמיכי)\s+(את\s+)?(ה)?(ווליום|וליום|עוצמה|קול|סאונד)") => "VOLUME_DOWN",
            _ when Regex.IsMatch(lower, @"^(השתק|תשתיק|תשתיקי|השתיקי|mute)$") => "VOLUME_MUTE",
            _ when Regex.IsMatch(lower, @"^((ה)?שיר( ה)?הבא|הבא|next( song)?)$") => "MEDIA_NEXT",
            _ when Regex.IsMatch(lower, @"^((ה)?שיר( ה)?קודם|קודם|previous( song)?)$") => "MEDIA_PREV",
            _ when Regex.IsMatch(lower, @"^(עצור|עצרי|תעצור|תעצרי|המשך|תמשיך|תמשיכי)\s+(את\s+)?(ה)?(מוזיקה|שיר|נגינה|סרטון)$|^(pause|play|נגן|נגני)$") => "MEDIA_PLAY_PAUSE",
            _ => null
        };
        if (media is not null)
        {
            InputSimulator.PressCombo(media, media.StartsWith("VOLUME_U") || media.StartsWith("VOLUME_D") ? 5 : 1);
            return media switch { "VOLUME_UP" => "הגברתי 🔊", "VOLUME_DOWN" => "הנמכתי 🔉", "VOLUME_MUTE" => "השתקתי/ביטלתי השתקה 🔇", "MEDIA_NEXT" => "הבא ⏭", "MEDIA_PREV" => "קודם ⏮", _ => "⏯" };
        }

        // ---- screen features ----
        if (Regex.IsMatch(lower, @"^(תרגם|תרגמי|תתרגם|תתרגמי)\s+(לי\s+)?(את\s+)?(כל\s+)?(ה)?מסך$|^translate (the |my )?screen$")) { await ctx.OpenScreenTranslation(); return "פתחתי את תרגום המסך 🌐"; }
        if (Regex.IsMatch(lower, @"^(הקף וחפש|הקיפי וחפשי|חיפוש במסך|circle to search)$")) { await ctx.OpenCircleSearch(); return "המסך קפוא — הקיפו משהו 🔍"; }

        // ---- timers ----
        var timer = Timer.Match(t);
        if (!timer.Success) timer = TimerShort.Match(t);
        if (timer.Success && TryMinutes(timer.Groups["n"].Value, timer.Groups["unit"].Value, out var minutes))
        {
            var what = timer.Groups["what"].Success && timer.Groups["what"].Value.Trim().Length > 0 ? timer.Groups["what"].Value.Trim() : "⏰ הזמן עבר";
            _ = Task.Delay(TimeSpan.FromMinutes(minutes)).ContinueWith(_ => { ctx.Notify("⏰ תזכורת", what); System.Media.SystemSounds.Exclamation.Play(); });
            return $"אזכיר לך בעוד {FormatMinutes(minutes)} ({DateTime.Now.AddMinutes(minutes):HH:mm}).";
        }

        // ---- remember ----
        var remember = Remember.Match(t);
        if (remember.Success && !Regex.IsMatch(remember.Groups["fact"].Value, "סיסמ|password|אשראי|credit", O))
        {
            var fact = remember.Groups["fact"].Value.Trim();
            if (!ctx.Settings.Memory.Contains(fact)) { ctx.Settings.Memory.Add(fact); SettingsStore.Save(ctx.Settings); }
            return "זכרתי 👍";
        }

        // ---- dictation: "תקלידי: …" types into the window you were in ----
        var type = Type.Match(t);
        if (type.Success)
        {
            var text = type.Groups["text"].Value.Trim();
            await ctx.PrepareForDesktopAction();
            await Task.Run(() => { InputSimulator.ReleaseModifiers(); InputSimulator.TypeText(text); }, cancellationToken);
            return $"הקלדתי ✍ ({text.Length} תווים)";
        }

        // ---- search / play ----
        var play = Play.Match(t);
        if (play.Success) return AppLauncher.Search(play.Groups["q"].Value, "youtube");
        var search = Search.Match(t);
        if (search.Success)
        {
            var engine = search.Groups["where"].Value switch { "יוטיוב" => "youtube", "מפות" => "maps", "ויקיפדיה" => "wikipedia", "תמונות" => "images", _ => "google" };
            return AppLauncher.Search(search.Groups["q"].Value, engine);
        }

        // ---- open / close a single app (anything more complex goes to the AI) ----
        var open = Open.Match(t);
        if (open.Success && IsSimpleTarget(open.Groups["what"].Value))
        {
            try
            {
                var result = await AppLauncher.OpenAsync(open.Groups["what"].Value, null, cancellationToken);
                return result.Replace("פתחתי", "פתחתי ✓");
            }
            catch { return null; } // not an app name we know — let the AI figure it out
        }
        var close = Close.Match(t);
        if (close.Success && IsSimpleTarget(close.Groups["what"].Value) && WindowManager.Find(close.Groups["what"].Value) is { } window)
        {
            WindowManager.Close(window.Handle);
            return $"סגרתי את \"{window.Title}\".";
        }
        return null;
    }

    /// <summary>"וורד" yes; "וורד ותכתוב מכתב" no (that needs the AI).</summary>
    private static bool IsSimpleTarget(string what) =>
        what.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3 && !Regex.IsMatch(what, @"\sו\S|\sand\s|,|\bו(ת|ה|ל)", O);

    private static bool TryMinutes(string n, string unit, out double minutes)
    {
        minutes = n switch { "חצי" => 30, "רבע" => 15, "שעה" => 60, "שעתיים" => 120, "דקה" => 1, "דקותיים" => 2, _ => double.TryParse(n.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0 };
        if (n is "חצי" or "רבע" && unit is "" or "שעה") { } // "חצי שעה", "רבע שעה"
        else if (unit is "שעות" or "שעה" && n is not ("שעה" or "שעתיים")) minutes *= 60;
        else if (unit == "שניות") minutes /= 60;
        return minutes > 0 && minutes <= 24 * 60;
    }

    private static string FormatMinutes(double minutes) => minutes < 1 ? $"{minutes * 60:0} שניות" : minutes >= 60 && minutes % 60 == 0 ? (minutes == 60 ? "שעה" : $"{minutes / 60:0} שעות") : $"{minutes:0.#} דקות";
}
