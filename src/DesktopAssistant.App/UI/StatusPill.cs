using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopAssistant.Core;

namespace DesktopAssistant.UI;

/// <summary>A small floating "working on it…" bar shown while the agent operates other apps. Never takes focus.</summary>
public sealed class StatusPill : Window
{
    private readonly TextBlock _text = new() { Foreground = Brushes.White, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420, TextTrimming = TextTrimming.CharacterEllipsis };

    public event Action? StopRequested;

    public StatusPill()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; FlowDirection = FlowDirection.RightToLeft; FontFamily = Theme.Font;
        var stop = new Button { Content = "■ עצור", Padding = new Thickness(12, 4, 12, 4), Background = new SolidColorBrush(Color.FromRgb(197, 69, 59)), Foreground = Brushes.White, Margin = new Thickness(12, 0, 0, 0) };
        stop.Click += (_, _) => StopRequested?.Invoke();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "✦", FontSize = 16, Foreground = Theme.Res("GeminiGradient"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_text);
        row.Children.Add(stop);
        Content = new Border
        {
            Margin = new Thickness(12), Padding = new Thickness(16, 7, 8, 7), CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(235, 30, 31, 34)), BorderBrush = Theme.Res("GeminiGradient"), BorderThickness = new Thickness(1.2),
            Effect = Theme.Shadow(20, 0.4), Child = row
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new nint(style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW));
        };
        SizeChanged += (_, _) => Reposition();
    }

    public void SetText(string text) => _text.Text = text;

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + 6;
    }
}
