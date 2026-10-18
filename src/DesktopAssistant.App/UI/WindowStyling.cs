using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using DesktopAssistant.Core;

namespace DesktopAssistant.UI;

/// <summary>
/// Borderless windows WITHOUT AllowsTransparency. Layered (transparent) WPF windows are drawn in software
/// and were the main reason the previous version felt slow; this keeps GPU rendering and lets
/// Windows 11 draw the rounded corners and the shadow natively.
/// </summary>
public static class WindowStyling
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;

    public static void MakeBorderless(Window window, bool resizable = false, bool smallCorners = false)
    {
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = false;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = resizable ? new Thickness(6) : new Thickness(0),
            GlassFrameThickness = new Thickness(0, 0, 0, 1), // keeps the native DWM shadow
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var corner = smallCorners ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
            var dark = 1;
            try
            {
                Native.DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int)); // Windows 11; ignored on 10
                Native.DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch { }
        };
    }

    /// <summary>Never take focus and never show in Alt+Tab (status pill, toasts).</summary>
    public static void MakeNoActivate(Window window)
    {
        window.ShowActivated = false;
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new nint(style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW));
        };
    }

    /// <summary>Work area (in WPF units) of the monitor under the mouse.</summary>
    public static Rect WorkAreaUnderCursor()
    {
        try
        {
            var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            var wa = screen.WorkingArea;
            var dpi = 1.0;
            if (Native.GetDpiForMonitor(Native.MonitorFromPoint(new Native.POINT { X = wa.Left + 1, Y = wa.Top + 1 }, 2), 0, out var dx, out _) == 0 && dx > 0) dpi = dx / 96.0;
            return new Rect(wa.Left / dpi, wa.Top / dpi, wa.Width / dpi, wa.Height / dpi);
        }
        catch { return SystemParameters.WorkArea; }
    }
}
