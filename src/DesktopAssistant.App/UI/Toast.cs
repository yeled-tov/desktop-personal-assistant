using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopAssistant.Core;

namespace DesktopAssistant.UI;

/// <summary>Small notification bubble at the bottom-right that never steals focus.</summary>
public static class Toast
{
    public static void Show(string title, string text, int seconds = 6)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => Show(title, text, seconds)); return; }
        try
        {
            var area = SystemParameters.WorkArea;
            var window = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
                ShowActivated = false, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, FlowDirection = FlowDirection.RightToLeft
            };
            var content = new Border
            {
                Background = Theme.Res("Surface"), CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(16),
                BorderBrush = Theme.Res("GeminiGradient"), BorderThickness = new Thickness(1.2), Effect = Theme.Shadow(), MaxWidth = 380,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14.5, Foreground = Theme.Res("TextPrimary"), FontFamily = Theme.Font },
                        new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 4, 0, 0), Foreground = Theme.Res("TextSecondary"), FontFamily = Theme.Font }
                    }
                }
            };
            window.Content = content;
            window.MouseLeftButtonUp += (_, _) => window.Close();
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new nint(style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW));
            };
            window.Loaded += (_, _) =>
            {
                window.Left = area.Right - window.ActualWidth - 8;
                window.Top = area.Bottom - window.ActualHeight - 8;
                window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            };
            window.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) => { timer.Stop(); try { window.Close(); } catch { } };
            timer.Start();
        }
        catch (Exception ex) { AppLog.Error("Toast failed", ex); }
    }
}
