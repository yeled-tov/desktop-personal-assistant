using System.Runtime.InteropServices;
using System.Text;

namespace DesktopAssistant.Automation;

public static class WindowActions
{
    public static bool TryCloseWindow(string titlePart)
    {
        if (string.IsNullOrWhiteSpace(titlePart)) return false;
        var target = IntPtr.Zero;
        Native.EnumWindows((handle, _) =>
        {
            if (!Native.IsWindowVisible(handle)) return true;
            var buffer = new StringBuilder(512);
            _ = Native.GetWindowText(handle, buffer, buffer.Capacity);
            if (buffer.ToString().Contains(titlePart, StringComparison.OrdinalIgnoreCase)) { target = handle; return false; }
            return true;
        }, IntPtr.Zero);
        return target != IntPtr.Zero && Native.PostMessage(target, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
    }

    private static class Native
    {
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    }
}
