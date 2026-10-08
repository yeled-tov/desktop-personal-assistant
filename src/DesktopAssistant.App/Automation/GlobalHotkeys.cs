using System.Runtime.InteropServices;
using System.Windows.Interop;
using Window = System.Windows.Window;

namespace DesktopAssistant.Automation;

public sealed class GlobalHotkeys(Window window, Action activate, Action translate) : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int AssistantId = 0xA101, TranslateId = 0xA102;
    private HwndSource? _source;

    public void Register()
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowProc);
        Native.RegisterHotKey(handle, AssistantId, 0x0002 | 0x0001, 0x20); // Ctrl+Alt+Space
        Native.RegisterHotKey(handle, TranslateId, 0x0008 | 0x0004, 0x54); // Win+Shift+T
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
