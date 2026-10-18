using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopAssistant.Core;
using static DesktopAssistant.Core.Native;

namespace DesktopAssistant.Desktop;

public sealed record WindowInfo(nint Handle, string Title, string ProcessName, bool Minimized, Native.RECT Bounds);

public static class WindowManager
{
    public static IReadOnlyList<WindowInfo> List()
    {
        var own = Environment.ProcessId;
        var result = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != 0) return true;
                if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0) return true;
                if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                var title = GetWindowTitle(hwnd);
                if (string.IsNullOrWhiteSpace(title) || title is "Program Manager") return true;
                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == own) return true;
                var name = "";
                try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }
                GetWindowRect(hwnd, out var rect);
                result.Add(new WindowInfo(hwnd, title, name, IsIconic(hwnd), rect));
            }
            catch { }
            return true;
        }, 0);
        return result;
    }

    /// <summary>Finds a window by (partial) title or process name. Empty query = current foreground window.</summary>
    public static WindowInfo? Find(string? query)
    {
        var windows = List();
        if (string.IsNullOrWhiteSpace(query))
        {
            var fg = GetForegroundWindow();
            return windows.FirstOrDefault(w => w.Handle == fg) ?? windows.FirstOrDefault();
        }
        var q = query.Trim();
        var aliases = AppLauncher.ProcessAliases(q);
        return windows.FirstOrDefault(w => w.Title.Equals(q, StringComparison.OrdinalIgnoreCase))
            ?? windows.FirstOrDefault(w => w.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
            ?? windows.FirstOrDefault(w => w.ProcessName.Equals(q, StringComparison.OrdinalIgnoreCase) || aliases.Contains(w.ProcessName, StringComparer.OrdinalIgnoreCase))
            ?? windows.FirstOrDefault(w => q.Contains(w.ProcessName, StringComparison.OrdinalIgnoreCase) && w.ProcessName.Length > 2);
    }

    public static bool Focus(nint hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        var foreground = GetForegroundWindow();
        if (foreground == hwnd) return true;
        var currentThread = GetCurrentThreadId();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            // A synthetic ALT tap lifts the foreground lock Windows puts on background apps.
            keybd_event(0x12, 0, 0, OwnInjectionMarker);
            keybd_event(0x12, 0, KEYEVENTF_KEYUP, OwnInjectionMarker);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
        Thread.Sleep(120);
        return GetForegroundWindow() == hwnd;
    }

    public static void Minimize(nint hwnd) => ShowWindow(hwnd, SW_MINIMIZE);
    public static void Maximize(nint hwnd) => ShowWindow(hwnd, SW_MAXIMIZE);
    public static void Restore(nint hwnd) => ShowWindow(hwnd, SW_RESTORE);
    public static void Close(nint hwnd) => PostMessage(hwnd, WM_CLOSE, 0, 0);

    /// <summary>Waits until a window matching the query appears (used right after launching an app).</summary>
    public static async Task<WindowInfo?> WaitForAsync(string query, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var found = Find(query);
            if (found is not null) return found;
            await Task.Delay(300, cancellationToken);
        }
        return null;
    }
}
