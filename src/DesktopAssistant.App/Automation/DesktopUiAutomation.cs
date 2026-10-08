using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

namespace DesktopAssistant.Automation;

/// <summary>Semantic Windows UI Automation first; never uses fixed screen coordinates.</summary>
public sealed class DesktopUiAutomation
{
    private static readonly ControlType[] UsefulTypes = [ControlType.Button, ControlType.Edit, ControlType.Text, ControlType.Hyperlink, ControlType.ListItem, ControlType.TabItem, ControlType.MenuItem, ControlType.CheckBox, ControlType.RadioButton, ControlType.ComboBox];

    public string Inspect(string? windowTitle = null, int maxItems = 80)
    {
        var root = FindWindow(windowTitle) ?? ForegroundElement();
        if (root is null) return "לא נמצא חלון פעיל.";
        var title = Safe(() => root.Current.Name, "");
        var elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        var output = new List<object>();
        var bound = Math.Clamp(maxItems, 1, 150);
        for (var i = 0; i < elements.Count && output.Count < bound; i++)
        {
            var item = elements[i];
            try
            {
                var type = item.Current.ControlType;
                var name = item.Current.Name;
                if (!UsefulTypes.Contains(type) || string.IsNullOrWhiteSpace(name) || item.Current.IsOffscreen) continue;
                var rect = item.Current.BoundingRectangle;
                output.Add(new { name, type = type.ProgrammaticName.Replace("ControlType.", ""), automationId = item.Current.AutomationId, enabled = item.Current.IsEnabled, password = item.Current.IsPassword, bounds = new { left = rect.Left, top = rect.Top, width = rect.Width, height = rect.Height } });
            }
            catch (ElementNotAvailableException) { }
        }
        return JsonSerializer.Serialize(new { window = title, elements = output });
    }

    public string ReadVisibleText(string? windowTitle = null)
    {
        var root = FindWindow(windowTitle) ?? ForegroundElement();
        if (root is null) return "לא נמצא חלון פעיל.";
        var parts = new List<string>(); var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        for (var i = 0; i < all.Count && parts.Count < 250; i++)
        {
            try
            {
                if (all[i].Current.IsOffscreen || all[i].Current.IsPassword) continue;
                var name = all[i].Current.Name?.Trim();
                if (!string.IsNullOrEmpty(name) && (parts.Count == 0 || !parts[^1].Equals(name, StringComparison.Ordinal))) parts.Add(name);
            }
            catch (ElementNotAvailableException) { }
        }
        return parts.Count == 0 ? "לא נמצא טקסט נגיש בחלון." : string.Join("\n", parts);
    }

    public string ReadVisibleFormDetails()
    {
        var root = ForegroundElement();
        if (root is null) return "";
        var details = new List<string>(); var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        for (var i = 0; i < all.Count && details.Count < 20; i++)
        {
            try
            {
                if (all[i].Current.ControlType != ControlType.Edit || all[i].Current.IsPassword || all[i].Current.IsOffscreen) continue;
                if (!all[i].TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)) continue;
                var value = ((ValuePattern)pattern).Current.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(value)) details.Add($"{all[i].Current.Name}: {value[..Math.Min(value.Length, 1500)]}");
            }
            catch (ElementNotAvailableException) { }
        }
        return string.Join("\n", details);
    }

    public string ClickByName(string name)
    {
        var element = FindByName(name);
        if (element is null) return $"לא מצאתי רכיב נגיש בשם '{name}'.";
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) ((InvokePattern)invoke).Invoke();
            else if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select)) ((SelectionItemPattern)select).Select();
            else if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle)) ((TogglePattern)toggle).Toggle();
            else { element.SetFocus(); return $"עברתי אל הרכיב '{name}'. לא נמצא דפוס לחיצה נגיש."; }
            return $"לחצתי על '{name}'.";
        }
        catch (Exception ex) { return $"לא ניתן להפעיל את '{name}': {ex.Message}"; }
    }

    public string TypeInto(string name, string text)
    {
        var element = FindByName(name);
        if (element is null) return $"לא מצאתי שדה נגיש בשם '{name}'.";
        try
        {
            if (element.Current.IsPassword) return "מטעמי אבטחה אי אפשר להקליד בכלי זה לשדה סיסמה.";
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern) && !((ValuePattern)valuePattern).Current.IsReadOnly)
            { ((ValuePattern)valuePattern).SetValue(text); return $"הקלדתי בשדה '{name}'."; }
            element.SetFocus(); SendUnicodeText(text); return $"הקלדתי בשדה '{name}'.";
        }
        catch (Exception ex) { return $"לא ניתן להקליד בשדה '{name}': {ex.Message}"; }
    }

    public string FocusWindow(string title, bool minimize = false, bool maximize = false)
    {
        var window = FindWindow(title);
        if (window is null) return $"לא מצאתי חלון בשם '{title}'.";
        var handle = new IntPtr(window.Current.NativeWindowHandle);
        if (handle == IntPtr.Zero) return "לחלון אין ידית Windows זמינה.";
        if (minimize) Native.ShowWindow(handle, 6); else if (maximize) Native.ShowWindow(handle, 3); else { Native.ShowWindow(handle, 9); Native.SetForegroundWindow(handle); }
        return minimize ? "מיזערתי את החלון." : maximize ? "הגדלתי את החלון." : "עברתי לחלון.";
    }

    public string PressKey(string key)
    {
        var parts = key.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) return "קיצור המקשים לא נתמך.";
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase) { ["CTRL"]=0x11, ["CONTROL"]=0x11, ["ALT"]=0x12, ["SHIFT"]=0x10, ["WIN"]=0x5B, ["ENTER"]=0x0D, ["RETURN"]=0x0D, ["ESC"]=0x1B, ["ESCAPE"]=0x1B, ["TAB"]=0x09, ["SPACE"]=0x20, ["BACKSPACE"]=0x08, ["DELETE"]=0x2E, ["F5"]=0x74, ["LEFT"]=0x25, ["RIGHT"]=0x27, ["UP"]=0x26, ["DOWN"]=0x28, ["PAGEUP"]=0x21, ["PAGEDOWN"]=0x22, ["HOME"]=0x24, ["END"]=0x23 };
        var keys = new List<ushort>();
        foreach (var part in parts)
        {
            if (map.TryGetValue(part, out var code)) keys.Add(code);
            else if (part.Length == 1 && char.IsLetterOrDigit(part[0])) keys.Add((ushort)char.ToUpperInvariant(part[0]));
            else return $"המקש '{part}' אינו נתמך.";
        }
        foreach (var code in keys) Native.keybd_event((byte)code, 0, 0, UIntPtr.Zero);
        foreach (var code in keys.AsEnumerable().Reverse()) Native.keybd_event((byte)code, 0, 2, UIntPtr.Zero);
        return $"שלחתי את קיצור המקשים {key}.";
    }

    private static AutomationElement? FindByName(string name)
    {
        var root = ForegroundElement() ?? AutomationElement.RootElement;
        var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        for (var i = 0; i < all.Count; i++)
        {
            try { if (all[i].Current.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return all[i]; }
            catch (ElementNotAvailableException) { }
        }
        for (var i = 0; i < all.Count; i++)
        {
            try { if (all[i].Current.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) return all[i]; }
            catch (ElementNotAvailableException) { }
        }
        return null;
    }

    private static AutomationElement? FindWindow(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var root = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
        for (var i = 0; i < root.Count; i++)
            try { if (root[i].Current.Name.Contains(title, StringComparison.OrdinalIgnoreCase)) return root[i]; } catch (ElementNotAvailableException) { }
        return null;
    }

    private static AutomationElement? ForegroundElement()
    {
        var hwnd = Native.GetForegroundWindow();
        return hwnd == IntPtr.Zero ? null : AutomationElement.FromHandle(hwnd);
    }

    private static void SendUnicodeText(string text)
    {
        foreach (var ch in text)
        {
            var down = new Native.INPUT { Type = 1, Union = new Native.INPUTUNION { Keyboard = new Native.KEYBDINPUT { Scan = ch, Flags = 0x0004 } } };
            var up = new Native.INPUT { Type = 1, Union = new Native.INPUTUNION { Keyboard = new Native.KEYBDINPUT { Scan = ch, Flags = 0x0004 | 0x0002 } } };
            Native.SendInput(1, ref down, Marshal.SizeOf<Native.INPUT>()); Native.SendInput(1, ref up, Marshal.SizeOf<Native.INPUT>());
        }
    }

    private static T Safe<T>(Func<T> get, T fallback) { try { return get(); } catch { return fallback; } }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint Type; public INPUTUNION Union; }
        [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT Mouse; [FieldOffset(0)] public KEYBDINPUT Keyboard; [FieldOffset(0)] public HARDWAREINPUT Hardware; }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int X, Y; public uint Data, Flags, Time; public UIntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] internal struct HARDWAREINPUT { public uint Message; public ushort ParamL, ParamH; }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(IntPtr handle, int command);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, ref INPUT input, int size);
        [DllImport("user32.dll")] internal static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    }
}
