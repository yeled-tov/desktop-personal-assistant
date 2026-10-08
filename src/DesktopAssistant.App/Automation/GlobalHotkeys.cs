using System.Runtime.InteropServices;
using System.Windows.Interop;
using Window = System.Windows.Window;

namespace DesktopAssistant.Automation;

public sealed class GlobalHotkeys(Window window, Action activate, Action translate) : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int AssistantId = 0xA101, TranslateId = 0xA102;
    private HwndSource? _source;
    public string RegistrationMessage { get; private set; } = "";

    public void Register()
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowProc);
        var assistantRegistered = Native.RegisterHotKey(handle, AssistantId, 0x0002 | 0x0001 | 0x0004, 0x41); // Ctrl+Alt+Shift+A
        var translateRegistered = Native.RegisterHotKey(handle, TranslateId, 0x0002 | 0x0001 | 0x0004, 0x54); // Ctrl+Alt+Shift+T
        if (!assistantRegistered || !translateRegistered)
        {
            var unavailable = new List<string>();
            if (!assistantRegistered) unavailable.Add("פתיחת העוזר");
            if (!translateRegistered) unavailable.Add("תרגום מסך");
            RegistrationMessage = $"לא ניתן לרשום קיצור עבור {string.Join(" ו", unavailable)}. ייתכן שהוא תפוס; אפשר לפתוח דרך סמל המגש.";
        }
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey)
        {
            if (wParam.ToInt32() == AssistantId) activate();
            if (wParam.ToInt32() == TranslateId) translate();
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        var handle = new WindowInteropHelper(window).Handle;
        Native.UnregisterHotKey(handle, AssistantId); Native.UnregisterHotKey(handle, TranslateId);
        _source?.RemoveHook(WindowProc);
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnregisterHotKey(nint hWnd, int id);
    }
}
