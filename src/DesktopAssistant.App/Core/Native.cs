using System.Runtime.InteropServices;

namespace DesktopAssistant.Core;

/// <summary>All Win32 interop in one place.</summary>
public static class Native
{
    // ---------- input ----------
    public const int INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
    public const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004, MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010,
        MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;
    /// <summary>Marker placed in dwExtraInfo of input this app injects, so our own hook ignores it.</summary>
    public static readonly nint OwnInjectionMarker = unchecked((nint)0x0DA55157);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public int type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx; public int dy; public int mouseData; public uint dwFlags; public uint time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);

    // ---------- windows ----------
    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(nint hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hWnd, int cmd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(nint hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, nint extraInfo);

    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_MAXIMIZE = 3, SW_SHOW = 5, SW_MINIMIZE = 6, SW_RESTORE = 9;
    public const uint WM_CLOSE = 0x0010, GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000, DWMWA_CLOAKED = 14;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    public static readonly nint HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    // ---------- hotkeys ----------
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(nint hWnd, int id);

    // ---------- low level mouse hook ----------
    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_MOUSEWHEEL = 0x020A;
    public const uint LLMHF_INJECTED = 0x1;
    public delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public nint dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc fn, nint hMod, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int nCode, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);

    [StructLayout(LayoutKind.Sequential)] public struct MSG { public nint hwnd; public uint message; public nint wParam; public nint lParam; public uint time; public POINT pt; }
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, nint hWnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);
    public const uint WM_QUIT = 0x0012;

    // ---------- misc ----------
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint hObject);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    public static string GetWindowTitle(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var builder = new StringBuilder(length + 1);
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    public static string GetWindowClass(nint hwnd)
    {
        var builder = new StringBuilder(256);
        GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }
}
