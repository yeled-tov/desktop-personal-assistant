using System.Runtime.InteropServices;
using DesktopAssistant.Core;
using static DesktopAssistant.Core.Native;

namespace DesktopAssistant.Desktop;

/// <summary>Keyboard and mouse injection through SendInput (works with Hebrew text, any app).</summary>
public static class InputSimulator
{
    private static readonly Dictionary<string, ushort> Keys = BuildKeyMap();
    private static readonly HashSet<ushort> ExtendedKeys = [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0x6F, 0x90, 0xA3, 0xA5, 0x2C,
        0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3, 0xA6, 0xA7, 0xA8, 0xAC];

    private static Dictionary<string, ushort> BuildKeyMap()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["CTRL"] = 0x11, ["CONTROL"] = 0x11, ["SHIFT"] = 0x10, ["ALT"] = 0x12, ["WIN"] = 0x5B, ["WINDOWS"] = 0x5B, ["CMD"] = 0x5B,
            ["ENTER"] = 0x0D, ["RETURN"] = 0x0D, ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B, ["TAB"] = 0x09, ["SPACE"] = 0x20, ["BACKSPACE"] = 0x08, ["BACK"] = 0x08,
            ["DELETE"] = 0x2E, ["DEL"] = 0x2E, ["INSERT"] = 0x2D, ["INS"] = 0x2D, ["HOME"] = 0x24, ["END"] = 0x23, ["PAGEUP"] = 0x21, ["PGUP"] = 0x21, ["PAGEDOWN"] = 0x22, ["PGDN"] = 0x22,
            ["LEFT"] = 0x25, ["UP"] = 0x26, ["RIGHT"] = 0x27, ["DOWN"] = 0x28, ["CAPSLOCK"] = 0x14, ["PRINTSCREEN"] = 0x2C, ["PRTSC"] = 0x2C, ["APPS"] = 0x5D, ["MENU"] = 0x5D,
            ["VOLUME_UP"] = 0xAF, ["VOLUMEUP"] = 0xAF, ["VOLUME_DOWN"] = 0xAE, ["VOLUMEDOWN"] = 0xAE, ["VOLUME_MUTE"] = 0xAD, ["MUTE"] = 0xAD,
            ["MEDIA_PLAY_PAUSE"] = 0xB3, ["PLAYPAUSE"] = 0xB3, ["MEDIA_NEXT"] = 0xB0, ["NEXTTRACK"] = 0xB0, ["MEDIA_PREV"] = 0xB1, ["PREVTRACK"] = 0xB1, ["MEDIA_STOP"] = 0xB2,
            ["BROWSER_BACK"] = 0xA6, ["BROWSER_FORWARD"] = 0xA7, ["BROWSER_REFRESH"] = 0xA8,
            ["PLUS"] = 0xBB, ["MINUS"] = 0xBD, ["COMMA"] = 0xBC, ["PERIOD"] = 0xBE, ["SLASH"] = 0xBF, ["SEMICOLON"] = 0xBA, ["QUOTE"] = 0xDE, ["BACKSLASH"] = 0xDC,
            ["+"] = 0xBB, ["-"] = 0xBD, [","] = 0xBC, ["."] = 0xBE, ["/"] = 0xBF, [";"] = 0xBA, ["'"] = 0xDE, ["["] = 0xDB, ["]"] = 0xDD, ["`"] = 0xC0, ["="] = 0xBB, ["\\"] = 0xDC,
        };
        for (var c = 'A'; c <= 'Z'; c++) map[c.ToString()] = c;
        for (var d = '0'; d <= '9'; d++) map[d.ToString()] = d;
        for (var f = 1; f <= 24; f++) map["F" + f] = (ushort)(0x70 + f - 1);
        for (var n = 0; n <= 9; n++) map["NUM" + n] = (ushort)(0x60 + n);
        return map;
    }

    public static bool TryParseKey(string name, out ushort vk) => Keys.TryGetValue(name.Trim(), out vk);

    /// <summary>Presses a combination like "CTRL+SHIFT+S", "ALT+F4", "WIN+D", "ENTER".</summary>
    public static void PressCombo(string combo, int repeat = 1)
    {
        var tokens = combo.Replace(" ", "").Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (combo.EndsWith("++", StringComparison.Ordinal)) tokens = [.. tokens, "PLUS"];
        if (tokens.Length == 0) throw new ArgumentException("לא צוין מקש.");
        var codes = new List<ushort>();
        foreach (var token in tokens)
        {
            if (!TryParseKey(token, out var vk)) throw new ArgumentException($"מקש לא מוכר: {token}");
            codes.Add(vk);
        }
        var modifiers = codes.Take(codes.Count - 1).ToList();
        var key = codes[^1];
        for (var i = 0; i < Math.Clamp(repeat, 1, 50); i++)
        {
            var inputs = new List<INPUT>();
            foreach (var m in modifiers) inputs.Add(Key(m, false));
            inputs.Add(Key(key, false));
            inputs.Add(Key(key, true));
            for (var j = modifiers.Count - 1; j >= 0; j--) inputs.Add(Key(modifiers[j], true));
            Send(inputs);
            if (repeat > 1) Thread.Sleep(25);
        }
    }

    /// <summary>Types any Unicode text into the focused control.</summary>
    public static void TypeText(string text, CancellationToken cancellationToken = default)
    {
        var normalized = text.Replace("\r\n", "\n");
        var batch = new List<INPUT>();
        foreach (var ch in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ch == '\n') { batch.Add(Key(0x0D, false)); batch.Add(Key(0x0D, true)); }
            else if (ch == '\t') { batch.Add(Key(0x09, false)); batch.Add(Key(0x09, true)); }
            else { batch.Add(Unicode(ch, false)); batch.Add(Unicode(ch, true)); }
            if (batch.Count >= 40) { Send(batch); batch.Clear(); Thread.Sleep(8); }
        }
        if (batch.Count > 0) Send(batch);
    }

    public static void MoveMouse(int x, int y) => SetCursorPos(x, y);

    public static void Click(int x, int y, string button = "left", int clicks = 1)
    {
        SetCursorPos(x, y);
        Thread.Sleep(40);
        var (down, up) = button.ToLowerInvariant() switch
        {
            "right" => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            "middle" => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
        };
        for (var i = 0; i < Math.Clamp(clicks, 1, 3); i++)
        {
            Send([Mouse(down), Mouse(up)]);
            Thread.Sleep(60);
        }
    }

    public static void Drag(int fromX, int fromY, int toX, int toY)
    {
        SetCursorPos(fromX, fromY);
        Thread.Sleep(40);
        Send([Mouse(MOUSEEVENTF_LEFTDOWN)]);
        for (var step = 1; step <= 12; step++)
        {
            SetCursorPos(fromX + (toX - fromX) * step / 12, fromY + (toY - fromY) * step / 12);
            Thread.Sleep(15);
        }
        Send([Mouse(MOUSEEVENTF_LEFTUP)]);
    }

    public static void Scroll(int x, int y, int notches, bool horizontal = false)
    {
        SetCursorPos(x, y);
        Thread.Sleep(30);
        var input = Mouse(horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL);
        input.U.mi.mouseData = notches * 120;
        Send([input]);
    }

    /// <summary>Re-injects a left button event (used by the long-press detector), tagged so our hook ignores it.</summary>
    internal static void SendLeftClickAt(int x, int y) =>
        Send([AbsoluteMouse(x, y, MOUSEEVENTF_LEFTDOWN), AbsoluteMouse(x, y, MOUSEEVENTF_LEFTUP)]);

    internal static void SendLeftDownAt(int x, int y) => Send([AbsoluteMouse(x, y, MOUSEEVENTF_LEFTDOWN)]);

    /// <summary>Press at the original point, then move to where the pointer is now (keeps drags exact).</summary>
    internal static void SendLeftDownThenMove(int fromX, int fromY, int toX, int toY) =>
        Send([AbsoluteMouse(fromX, fromY, MOUSEEVENTF_LEFTDOWN), AbsoluteMouse(toX, toY, 0)]);

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    private static INPUT AbsoluteMouse(int x, int y, uint flags)
    {
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77), width = Math.Max(2, GetSystemMetrics(78)), height = Math.Max(2, GetSystemMetrics(79));
        var input = Mouse(flags | MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);
        input.U.mi.dx = (int)Math.Round((x - left) * 65535.0 / (width - 1));
        input.U.mi.dy = (int)Math.Round((y - top) * 65535.0 / (height - 1));
        return input;
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (ExtendedKeys.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0), dwExtraInfo = OwnInjectionMarker } }
    };

    private static INPUT Unicode(char ch, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), dwExtraInfo = OwnInjectionMarker } }
    };

    private static INPUT Mouse(uint flags) => new() { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, dwExtraInfo = OwnInjectionMarker } } };

    private static void Send(IList<INPUT> inputs)
    {
        var array = inputs as INPUT[] ?? inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        if (sent != array.Length) AppLog.Warn($"SendInput injected {sent}/{array.Length} events (error {Marshal.GetLastWin32Error()}). A window running as administrator blocks input from normal apps.");
    }

    /// <summary>Releases modifier keys the user may still be holding (e.g. after pressing the hotkey).</summary>
    public static void ReleaseModifiers()
    {
        var inputs = new List<INPUT>();
        foreach (var vk in new ushort[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(vk) & 0x8000) != 0) inputs.Add(Key(vk, true));
        if (inputs.Count > 0) Send(inputs);
    }
}
