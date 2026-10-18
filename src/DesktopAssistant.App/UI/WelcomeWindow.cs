using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopAssistant.AI;
using DesktopAssistant.Desktop;
using DesktopAssistant.Settings;

namespace DesktopAssistant.UI;

/// <summary>First run: connect a brain in under a minute.</summary>
public sealed class WelcomeWindow : Window
{
    private readonly AppHost _host;
    private readonly ComboBox _provider = new() { DisplayMemberPath = nameof(ProviderConfig.DisplayName), Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(6) };
    private readonly PasswordBox _key = new() { Padding = new Thickness(6), Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontSize = 13 };

    public WelcomeWindow(AppHost host)
    {
        _host = host;
        Title = "ברוכים הבאים";
        Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; FlowDirection = FlowDirection.RightToLeft; FontFamily = Theme.Font; FontSize = 14;
        Background = Brushes.White; Topmost = true;

        var settings = host.Controller.Settings;
        _provider.ItemsSource = settings.Providers.Where(p => p.Id != "custom").ToList();
        _provider.SelectedItem = settings.ActiveProvider;

        var link = new TextBlock { Margin = new Thickness(0, 0, 0, 4) };
        void UpdateLink()
        {
            link.Inlines.Clear();
            if (_provider.SelectedItem is ProviderConfig p && ProviderPresets.KeyUrl(p.Id) is { } url)
            {
                var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(p.Id == "gemini" ? "קבל מפתח Gemini חינם (דקה אחת) ←" : "איפה משיגים מפתח? ←"));
                h.Click += (_, _) => AppLauncher.OpenUrl(url);
                link.Inlines.Add(h);
            }
        }
        _provider.SelectionChanged += (_, _) => UpdateLink();
        UpdateLink();

        var connect = new Button { Content = "חבר והתחל ✦", Style = (Style)Application.Current.Resources["AccentButton"], MinWidth = 140 };
        var skip = new Button { Content = "אחר כך", MinWidth = 90 };
        connect.Click += async (_, _) => await ConnectAsync(connect);
        skip.Click += (_, _) => Close();

        var s = settings;
        Content = new StackPanel
        {
            Margin = new Thickness(26, 22, 26, 22),
            Children =
            {
                new TextBlock { Text = "✦ שלום! אני העוזר האישי שלך", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)) },
                new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 14), Foreground = Brushes.DimGray, Text =
                    $"• {s.AssistantHotkey} — לפתוח אותי מכל מקום (או ללחוץ על הסמל ליד השעון)\n• {s.VoiceHotkey} — לדבר איתי\n• {s.CircleSearchHotkey} או לחיצה ארוכה על משטח המגע — להקפיא את המסך ולהקיף משהו לחיפוש/תרגום\n\nכדי שאהיה חכם צריך לחבר מודל AI. בחר ספק והדבק מפתח API:" },
                new TextBlock { Text = "ספק", FontWeight = FontWeights.SemiBold },
                _provider,
                new TextBlock { Text = "מפתח API", FontWeight = FontWeights.SemiBold },
                _key,
                link,
                _status,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0), Children = { connect, skip } }
            }
        };
    }

    private async Task ConnectAsync(Button button)
    {
        if (_provider.SelectedItem is not ProviderConfig provider) return;
        var key = _key.Password.Trim();
        if (provider.RequiresKey && key.Length == 0 && !SecretStore.Has(provider.Id)) { _status.Foreground = Brushes.Firebrick; _status.Text = "הדבק מפתח API (או לחץ 'אחר כך')."; return; }
        button.IsEnabled = false;
        _status.Foreground = Brushes.DimGray;
        _status.Text = "בודק את החיבור…";
        try
        {
            if (key.Length > 0) SecretStore.Save(provider.Id, key);
            var reply = await ChatModelFactory.Create(provider).CompleteAsync("", [ChatMessage.User("ענה במילה אחת: שלום")], [], new ModelOptions(MaxOutputTokens: 200), CancellationToken.None);
            _host.Controller.Settings.ActiveProviderId = provider.Id;
            SettingsStore.Save(_host.Controller.Settings);
            _status.Foreground = new SolidColorBrush(Color.FromRgb(30, 142, 62));
            _status.Text = "✓ מחובר! " + reply.Text;
            await Task.Delay(700);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = "⚠ " + ex.Message;
            button.IsEnabled = true;
        }
    }
}
