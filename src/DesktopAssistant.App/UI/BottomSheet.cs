using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopAssistant.UI;

/// <summary>The Google-style result card shown after circling something.</summary>
public sealed class BottomSheet : Border
{
    private readonly TextBlock _answer = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 23, Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)) };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3, Margin = new Thickness(0, 4, 0, 10), Foreground = new SolidColorBrush(Theme.AccentColor), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly TextBlock _status = new() { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)) };
    private readonly TextBox _question = new() { FontSize = 14, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0) };
    private readonly TextBlock _placeholder = new() { Text = "שאל על מה שסימנת…", Foreground = new SolidColorBrush(Color.FromRgb(128, 134, 139)), FontSize = 14, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
    private readonly Button _settingsButton;
    private readonly ScrollViewer _scroll;
    private Action? _onSettings;

    public event Action? SearchClicked, LensClicked, TranslateClicked, CopyClicked;
    public event Action<string>? AskAssistantClicked, FollowUp;

    public BottomSheet()
    {
        Background = Brushes.White;
        CornerRadius = new CornerRadius(24);
        Padding = new Thickness(20, 14, 20, 16);
        Width = 600;
        MaxHeight = 520;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, 28);
        Effect = Theme.Shadow(40, 0.45);
        Cursor = Cursors.Arrow;
        TextElement.SetFontFamily(this, Theme.Font);

        var handle = new Border { Width = 40, Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromRgb(218, 220, 224)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };

        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        Button Chip(string text, Action action) { var b = Theme.Chip(text); b.Click += (_, _) => action(); chips.Children.Add(b); return b; }
        Chip("🔍  חיפוש בגוגל", () => SearchClicked?.Invoke());
        Chip("📷  Google Lens", () => LensClicked?.Invoke());
        Chip("🌐  תרגום", () => TranslateClicked?.Invoke());
        Chip("📋  העתק טקסט", () => CopyClicked?.Invoke());
        Chip("✨  המשך עם העוזר", () => AskAssistantClicked?.Invoke(_question.Text.Trim()));
        _settingsButton = Chip("⚙  הגדרות", () => _onSettings?.Invoke());
        _settingsButton.Visibility = Visibility.Collapsed;

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300, Content = new StackPanel { Children = { _status, _progress, _answer } } };

        var send = new Button { Content = "➤", Style = (Style)Application.Current.Resources["AccentButton"], Width = 38, Height = 38, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0) };
        send.Click += (_, _) => Submit();
        _question.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Submit(); } };
        _question.TextChanged += (_, _) => _placeholder.Visibility = _question.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var inputGrid = new Grid();
        inputGrid.Children.Add(_question);
        inputGrid.Children.Add(_placeholder);
        var input = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(send, Dock.Left);
        input.Children.Add(send);
        input.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(241, 243, 244)), CornerRadius = new CornerRadius(20), Padding = new Thickness(10, 6, 10, 6), Child = inputGrid });

        Child = new StackPanel { Children = { handle, chips, _scroll, input } };
    }

    private void Submit()
    {
        var text = _question.Text.Trim();
        if (text.Length == 0) return;
        _question.Clear();
        FollowUp?.Invoke(text);
    }

    public void ShowFor(Rect selection, double screenHeight)
    {
        // Keep the card from covering what was circled: put it at the top when the selection is low on the screen.
        var low = selection.Bottom > screenHeight - 360 && selection.Top > 380;
        VerticalAlignment = low ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        Margin = low ? new Thickness(0, 90, 0, 0) : new Thickness(0, 0, 0, 28);
        Visibility = Visibility.Visible;
        var slide = new TranslateTransform(0, low ? -30 : 30);
        RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(low ? -30 : 30, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    public void ShowLoading(string text)
    {
        _settingsButton.Visibility = Visibility.Collapsed;
        _status.Text = text;
        _status.Visibility = Visibility.Visible;
        _progress.Visibility = Visibility.Visible;
        _answer.Text = "";
    }

    public void ShowAnswer(string text, string searchQuery)
    {
        _status.Visibility = string.IsNullOrWhiteSpace(searchQuery) ? Visibility.Collapsed : Visibility.Visible;
        _status.Text = string.IsNullOrWhiteSpace(searchQuery) ? "" : $"🔍 {searchQuery}";
        _progress.Visibility = Visibility.Collapsed;
        _answer.Text = text;
        _answer.FlowDirection = Theme.ContainsHebrew(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _scroll.ScrollToTop();
    }

    public void ShowMessage(string text, bool showSettings = false, Action? onSettings = null)
    {
        _onSettings = onSettings;
        _settingsButton.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        _status.Visibility = Visibility.Collapsed;
        _progress.Visibility = Visibility.Collapsed;
        _answer.Text = text;
        _answer.FlowDirection = FlowDirection.RightToLeft;
    }
}
