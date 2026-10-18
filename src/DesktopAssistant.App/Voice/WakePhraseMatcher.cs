using System.Text.RegularExpressions;

namespace DesktopAssistant.Voice;

/// <summary>
/// Decides whether a transcript starts with the wake phrase ("היי שיר"), tolerating the small
/// variations speech recognition produces: punctuation, niqqud, "הי"/"hey"/"hi", one-letter slips.
/// </summary>
public static class WakePhraseMatcher
{
    private static readonly string[] Greetings = ["היי", "הי", "הייי", "היא", "hey", "hi", "hay", "hei", "אהלן", "שלום", "yo"];
    private static readonly string[] Fillers = ["אה", "אהה", "אמ", "אממ", "אוקיי", "טוב", "נו", "um", "uh", "ok", "okay", "so"];

    /// <returns>true when the phrase was said; <paramref name="command"/> receives whatever followed it.</returns>
    public static bool TryMatch(string transcript, string phrase, out string command)
    {
        command = "";
        var spoken = Tokens(transcript);
        var wanted = Tokens(phrase);
        if (spoken.Length == 0 || wanted.Length == 0) return false;

        // Allow one filler word before the phrase ("אה, היי שיר…"), but not mid-sentence mentions.
        for (var start = 0; start <= Math.Min(1, spoken.Length - wanted.Length); start++)
        {
            if (start == 1 && !Fillers.Contains(spoken[0])) break;
            var ok = true;
            for (var i = 0; i < wanted.Length && ok; i++) ok = TokenMatches(spoken[start + i], wanted[i]);
            if (!ok) continue;
            command = string.Join(' ', spoken.Skip(start + wanted.Length));
            return true;
        }
        return false;
    }

    private static bool TokenMatches(string heard, string wanted)
    {
        if (heard == wanted) return true;
        if (Greetings.Contains(heard) && Greetings.Contains(wanted)) return true;
        // The recognizer sometimes writes a Hebrew name in Latin letters ("Shir").
        if (IsHebrew(wanted) && !IsHebrew(heard) && heard.Length >= 2) return Levenshtein(heard, Transliterate(wanted)) <= (wanted.Length >= 4 ? 2 : 1);
        // Hebrew prefix letters glued to the name by the recognizer ("ושיר", "השיר") are not the name.
        if (wanted.Length < 4) return false;
        return Levenshtein(heard, wanted) <= 1;
    }

    private static bool IsHebrew(string s) => s.Any(c => c >= 'א' && c <= 'ת');

    private static string Transliterate(string hebrew)
    {
        var map = new Dictionary<char, string>
        {
            ['א'] = "a", ['ב'] = "b", ['ג'] = "g", ['ד'] = "d", ['ה'] = "h", ['ו'] = "o", ['ז'] = "z", ['ח'] = "ch", ['ט'] = "t", ['י'] = "i",
            ['כ'] = "k", ['ך'] = "ch", ['ל'] = "l", ['מ'] = "m", ['ם'] = "m", ['נ'] = "n", ['ן'] = "n", ['ס'] = "s", ['ע'] = "a", ['פ'] = "p",
            ['ף'] = "f", ['צ'] = "tz", ['ץ'] = "tz", ['ק'] = "k", ['ר'] = "r", ['ש'] = "sh", ['ת'] = "t", ['\''] = ""
        };
        return string.Concat(hebrew.Select(c => map.TryGetValue(c, out var v) ? v : c.ToString()));
    }

    private static string[] Tokens(string text)
    {
        var t = text.ToLowerInvariant();
        t = Regex.Replace(t, "[֑-ׇ]", "");            // niqqud / cantillation
        t = t.Replace("׳", "'").Replace("״", "\"");
        t = Regex.Replace(t, @"[^\p{L}\p{N}' ]+", " ");          // punctuation
        return t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
