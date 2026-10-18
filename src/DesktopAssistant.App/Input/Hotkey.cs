using DesktopAssistant.Desktop;
using static DesktopAssistant.Core.Native;

namespace DesktopAssistant.Input;

public sealed record Hotkey(uint Modifiers, uint Key)
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = new Hotkey(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint modifiers = 0; uint key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= MOD_CONTROL; break;
                case "ALT": modifiers |= MOD_ALT; break;
                case "SHIFT": modifiers |= MOD_SHIFT; break;
                case "WIN" or "WINDOWS": modifiers |= MOD_WIN; break;
                default:
                    if (key != 0 || !InputSimulator.TryParseKey(raw, out var vk)) return false;
                    key = vk; break;
            }
        }
        if (key == 0) return false;
        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    public static string Format(bool ctrl, bool alt, bool shift, bool win, string keyName)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(keyName);
        return string.Join("+", parts);
    }
}
