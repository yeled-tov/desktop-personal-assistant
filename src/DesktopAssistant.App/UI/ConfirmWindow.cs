using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopAssistant.UI;

/// <summary>Approval prompt for sensitive actions (works even while the assistant panel is hidden).</summary>
public sealed class ConfirmWindow : Window
{
    private ConfirmWindow(string title, string details)
    {
        Background = Theme.Res("Surface"); Topmost = true; ShowInTaskbar = true;
        WindowStyling.MakeBorderless(this);
        SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = Theme.Font; Title = title;

        var yes = new Button { Content = "אישור", Style = (Style)Application.Current.Resources["AccentButton"], MinWidth = 96, IsDefault = true };
        var no = new Button { Content = "ביטול", MinWidth = 96, IsCancel = true };
        yes.Click += (_, _) => { DialogResult = true; };
        no.Click += (_, _) => { DialogResult = false; };

        Content = new Border
        {
            Padding = new Thickness(22, 18, 22, 16), MaxWidth = 520, BorderBrush = Theme.Res("GeminiGradient"), BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "✦  " + title, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Theme.Res("TextPrimary") },
                    new ScrollViewer
                    {
                        MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 16),
                        Content = new TextBox
                        {
                            Text = details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                            Foreground = Theme.Res("TextSecondary"), FontSize = 14,
                            FlowDirection = Theme.ContainsHebrew(details) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
                        }
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Children = { yes, no } }
                }
            }
        };
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        Loaded += (_, _) => { Activate(); yes.Focus(); };
    }

    public static bool Ask(string title, string details) => new ConfirmWindow(title, details).ShowDialog() == true;
}
