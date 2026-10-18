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
                Background = Theme.Res("Surface"), Topmost = true, ShowInTaskbar = false,
                SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, FlowDirection = FlowDirection.RightToLeft
            };
            var content = new Border
            {
                Padding = new Thickness(18, 14, 18, 14), BorderBrush = Theme.Res("GeminiGradient"), BorderThickness = new Thickness(1), MaxWidth = 380,
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
            WindowStyling.MakeBorderless(window);
            WindowStyling.MakeNoActivate(window);
            window.Loaded += (_, _) =>
            {
                window.Left = area.Right - window.ActualWidth - 16;
                window.Top = area.Bottom - window.ActualHeight - 16;
            };
            window.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) => { timer.Stop(); try { window.Close(); } catch { } };
            timer.Start();
        }
        catch (Exception ex) { AppLog.Error("Toast failed", ex); }
    }
}
