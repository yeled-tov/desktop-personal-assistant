using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopAssistant.Agent;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;
using DesktopAssistant.Voice;

namespace DesktopAssistant.UI;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly StatusPill _pill = new();
    private readonly SpeechOutput _speech = new();
    private readonly List<(ImagePart Part, BitmapSource Preview)> _attachments = [];
    private CancellationTokenSource? _run;
    private CancellationTokenSource? _recordCancel;
    private CancellationTokenSource? _recordStop;
    private bool _hiddenForAction;
    private bool _positioned;
    private nint _previousWindow; // the app the user was in when they called the assistant

    public string PreviousWindowTitle => _previousWindow != 0 ? Native.GetWindowTitle(_previousWindow) : "";

    public bool IsBusy => _run is not null;
    public bool IsRecording => _recordCancel is not null;
    public bool IsSpeaking => _speech.IsSpeaking;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); _positioned = true; } catch { } } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (IsRecording) _recordCancel?.Cancel();
                else if (IsBusy) _run?.Cancel();
                else Hide();
            }
        };
        _pill.StopRequested += () => _run?.Cancel();
        Loaded += (_, _) => { RefreshProvider(); ShowWelcome(); };
    }

    // ------------- showing / hiding -------------

    public void ShowPanel(bool activate = true)
    {
        var foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out var pid);
        if (foreground != 0 && pid != Environment.ProcessId) _previousWindow = foreground;
        if (!_positioned)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 4;
            Top = area.Bottom - Height - 4;
            _positioned = true;
        }
        ShowActivated = activate;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (activate)
        {
            Activate();
            Topmost = true;
            Input.Focus();
            Keyboard.Focus(Input);
        }
        ShowActivated = true;
    }

    public void Toggle()
    {
        if (IsVisible && IsActive) Hide();
        else ShowPanel();
    }

    public void RefreshProvider()
    {
        var c = _host.Controller;
        TitleText.Text = c.Settings.AssistantName;
        Title = c.Settings.AssistantName;
        var wake = _host.WakeWordActive ? $"  ·  👂 \"{c.Settings.WakePhrase}\"" : "";
        ProviderText.Text = c.IsConfigured ? $"{c.Provider.DisplayName} · {c.Provider.Model}{wake}" : "לא מחובר ל-AI — לחץ ⚙ כדי לחבר";
    }

    // ------------- input -------------

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var caret = Input.CaretIndex;
            Input.Text = Input.Text.Insert(caret, Environment.NewLine);
            Input.CaretIndex = caret + Environment.NewLine.Length;
        }
        else _ = SendAsync();
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e) => Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    private void Send_Click(object sender, RoutedEventArgs e) => _ = SendAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) => _run?.Cancel();
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
    private void Settings_Click(object sender, RoutedEventArgs e) => _host.OpenSettings();
    private void Circle_Click(object sender, RoutedEventArgs e) => _ = _host.StartCircleSearchAsync();
    private void Mic_Click(object sender, RoutedEventArgs e) => _ = ToggleVoiceAsync();

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        _run?.Cancel();
        _host.Controller.NewConversation();
        Messages.Children.Clear();
        ClearAttachments();
        ShowWelcome();
    }

    private async void Screen_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        await Task.Delay(220);
        try
        {
            var shot = ScreenCapture.CaptureMonitors();
            var cursor = System.Windows.Forms.Cursor.Position;
            var monitor = shot.FirstOrDefault(s => cursor.X >= s.Bounds.X && cursor.X < s.Bounds.X + s.Bounds.Width && cursor.Y >= s.Bounds.Y && cursor.Y < s.Bounds.Y + s.Bounds.Height) ?? shot[0];
            Attach(monitor.Image);
        }
        catch (Exception ex) { AppLog.Error("Screen attach failed", ex); }
        ShowPanel();
    }

    public void Attach(BitmapSource image)
    {
        _attachments.Clear();
        _attachments.Add((new ImagePart(ScreenCapture.EncodeForModel(image)), image));
        AttachmentImage.Source = image;
        AttachmentChip.Visibility = Visibility.Visible;
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e) => ClearAttachments();

    private void ClearAttachments()
    {
        _attachments.Clear();
        AttachmentImage.Source = null;
        AttachmentChip.Visibility = Visibility.Collapsed;
    }

    /// <summary>Entry point used by voice, the overlay ("continue with the assistant") and suggestion chips.</summary>
    public Task SubmitAsync(string text)
    {
        Input.Text = text;
        return SendAsync();
    }

    // ------------- the main flow -------------

    private async Task SendAsync()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0 && _attachments.Count == 0) return;
        if (IsBusy) return;
        if (text.Length == 0) text = "מה רואים כאן? הסבר בקצרה.";
        Input.Clear();
        _speech.Stop();

        var images = _attachments.Select(a => a.Part).ToList();
        AddUserBubble(text, _attachments.FirstOrDefault().Preview);
        ClearAttachments();

        _run = new CancellationTokenSource();
        var token = _run.Token;
        SetBusy(true, "חושב…");
        var context = new AgentContext
        {
            Settings = _host.Controller.Settings,
            PreviousWindowTitle = PreviousWindowTitle,
            Confirm = (title, details) => Dispatcher.InvokeAsync(() => ConfirmWindow.Ask(title, details)).Task,
            Activity = status => Dispatcher.BeginInvoke(() => { ActivityText.Text = status; _pill.SetText(status); }),
            PrepareForDesktopAction = () => Dispatcher.InvokeAsync(() =>
            {
                if (!IsVisible) return;
                _hiddenForAction = true;
                Hide();
                _pill.SetText(ActivityText.Text);
                _pill.Show();
            }).Task.ContinueWith(async _ =>
            {
                // Give focus back to the app the user was working in (e.g. Word), so typing lands there.
                if (_previousWindow != 0 && Native.IsWindowVisible(_previousWindow)) await Task.Run(() => WindowManager.Focus(_previousWindow));
                await Task.Delay(150);
            }).Unwrap(),
            SetOverlayVisible = visible => Dispatcher.InvokeAsync(() => _pill.Opacity = visible ? 1 : 0).Task,
            OpenScreenTranslation = () => Dispatcher.InvokeAsync(() => { _ = _host.StartTranslationAsync(); }).Task,
            Notify = (title, message) => Toast.Show(title, message, 30),
            RunOnUiThread = work => Dispatcher.InvokeAsync(work).Task,
        };

        try
        {
            var result = await Task.Run(() => _host.Controller.AskAsync(text, images, context, token), token);
            AddAssistantMessage(result.Text, result.Actions);
            if (_host.Controller.Settings.SpeakReplies) _ = _speech.SpeakAsync(result.Text, CancellationToken.None);
        }
        catch (OperationCanceledException) { AddInfo("⏹ נעצר."); }
        catch (AiException ex) { AddAssistantMessage(ex.Message, [], isError: true); }
        catch (Exception ex)
        {
            AppLog.Error("Request failed", ex);
            AddAssistantMessage("משהו השתבש: " + ex.Message, [], isError: true);
        }
        finally
        {
            _run.Dispose();
            _run = null;
            SetBusy(false, "");
            _pill.Hide();
            if (_hiddenForAction)
            {
                _hiddenForAction = false;
                ShowPanel(activate: false); // show the answer without stealing focus from the app it just worked in
            }
        }
    }

    private void SetBusy(bool busy, string status)
    {
        ActivityBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActivityText.Text = status;
        SendButton.IsEnabled = !busy;
    }

    // ------------- voice -------------

    public async Task ToggleVoiceAsync()
    {
        if (IsRecording) { _recordStop?.Cancel(); return; }
        if (IsBusy) return;
        ShowPanel();
        _speech.Stop();
        _recordCancel = new CancellationTokenSource();
        _recordStop = new CancellationTokenSource();
        ListeningPanel.Visibility = Visibility.Visible;
        ListeningText.Text = "מקשיב… דבר עכשיו";
        MicButton.Foreground = new SolidColorBrush(Color.FromRgb(234, 67, 53));
        byte[] wav;
        var recorder = new AudioRecorder();
        recorder.LevelChanged += level => Dispatcher.BeginInvoke(() => LevelBar.Width = 4 + level * 140);
        try
        {
            wav = await Task.Run(() => recorder.RecordAsync(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(1.4), _recordCancel.Token, _recordStop.Token));
            if (wav.Length == 0) { AddInfo("לא שמעתי כלום 🙂 נסה שוב."); return; }
            ListeningText.Text = "מתמלל…";
            LevelBar.Width = 4;
            var text = await Task.Run(() => _host.Controller.TranscribeAsync(wav, _recordCancel.Token));
            if (string.IsNullOrWhiteSpace(text)) { AddInfo("לא הצלחתי להבין, נסה שוב."); return; }
            ListeningPanel.Visibility = Visibility.Collapsed;
            if (_host.Controller.Settings.AutoSendVoice) await SubmitAsync(text);
            else { Input.Text = text; Input.CaretIndex = text.Length; Input.Focus(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AddAssistantMessage(ex.Message, [], isError: true); }
        finally
        {
            ListeningPanel.Visibility = Visibility.Collapsed;
            MicButton.ClearValue(ForegroundProperty);
            _recordCancel?.Dispose(); _recordCancel = null;
            _recordStop?.Dispose(); _recordStop = null;
        }
    }

    // ------------- rendering -------------

    private void ShowWelcome()
    {
        if (Messages.Children.Count > 0) return;
        var panel = new StackPanel { Margin = new Thickness(4, 18, 4, 8), Tag = "welcome" };
        var hello = new TextBlock { FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Theme.Res("GeminiGradient"), Text = Greeting() };
        panel.Children.Add(hello);
        panel.Children.Add(new TextBlock { Text = "אני יכול לפתוח תוכנות, לכתוב בשבילך, ללחוץ, לחפש, לתרגם את המסך ולענות על כל שאלה.", TextWrapping = TextWrapping.Wrap, Foreground = Theme.Res("TextSecondary"), FontSize = 14, Margin = new Thickness(0, 6, 0, 16) });
        var chips = new WrapPanel();
        foreach (var (label, prompt) in new[]
        {
            ("📝 פתח וורד וכתוב מכתב תודה", "פתח וורד ותכתוב מכתב תודה קצר ויפה למורה"),
            ("👀 מה יש לי על המסך?", "מה יש לי עכשיו על המסך? תסביר בקצרה"),
            ("🌐 תרגם את המסך", "תרגם לי את המסך"),
            ("🎵 שיר רגוע ביוטיוב", "חפש ביוטיוב מוזיקה רגועה לעבודה"),
            ("⏰ תזכיר לי בעוד 10 דקות", "תזכיר לי בעוד 10 דקות לקום להתמתח"),
            ("📂 מה הורדתי לאחרונה?", "מה הקבצים האחרונים בתיקיית ההורדות?"),
        })
        {
            var chip = new Button { Content = label, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 7, 12, 7), FontSize = 13 };
            chip.Click += (_, _) => _ = SubmitAsync(prompt);
            chips.Children.Add(chip);
        }
        panel.Children.Add(chips);
        var keys = _host.Controller.Settings;
        panel.Children.Add(new TextBlock
        {
            Text = $"{(keys.WakeWordEnabled ? $"\"{keys.WakePhrase}\" או " : "")}{keys.AssistantHotkey} — פתיחה  ·  {keys.VoiceHotkey} — דיבור\n{keys.CircleSearchHotkey} או לחיצה ארוכה על משטח המגע — הקף וחפש",
            Foreground = Theme.Res("TextSecondary"), FontSize = 11.5, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap, Opacity = 0.8
        });
        Messages.Children.Add(panel);
    }

    private static string Greeting()
    {
        var h = DateTime.Now.Hour;
        var part = h < 5 ? "לילה טוב" : h < 12 ? "בוקר טוב" : h < 17 ? "צהריים טובים" : h < 21 ? "ערב טוב" : "לילה טוב";
        return $"{part}! במה לעזור?";
    }

    private void RemoveWelcome()
    {
        if (Messages.Children.Count > 0 && Messages.Children[0] is FrameworkElement { Tag: "welcome" } welcome) Messages.Children.Remove(welcome);
    }

    private void AddUserBubble(string text, BitmapSource? image)
    {
        RemoveWelcome();
        var stack = new StackPanel();
        if (image is not null) stack.Children.Add(new Image { Source = image, FlowDirection = FlowDirection.LeftToRight, MaxHeight = 120, MaxWidth = 240, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Right });
        stack.Children.Add(Selectable(text, Brushes.White, 14.5));
        Messages.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(46, 61, 99)), CornerRadius = new CornerRadius(18, 18, 18, 4), Padding = new Thickness(14, 9, 14, 9),
            Margin = new Thickness(50, 8, 0, 4), HorizontalAlignment = HorizontalAlignment.Right, Child = stack
        });
        ScrollToEnd();
    }

    private void AddAssistantMessage(string text, IReadOnlyList<string> actions, bool isError = false)
    {
        RemoveWelcome();
        var stack = new StackPanel { Margin = new Thickness(0, 8, 8, 6) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(new TextBlock { Text = isError ? "⚠" : "✦", FontSize = 15, Foreground = isError ? new SolidColorBrush(Color.FromRgb(242, 139, 130)) : Theme.Res("GeminiGradient") });
        stack.Children.Add(head);
        if (actions.Count > 0)
            stack.Children.Add(new TextBlock { Text = string.Join("   ", actions.Distinct().Take(10)), FontSize = 11.5, Foreground = Theme.Res("TextSecondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Opacity = 0.85 });
        stack.Children.Add(Selectable(text, isError ? new SolidColorBrush(Color.FromRgb(242, 139, 130)) : Theme.Res("TextPrimary"), 15));
        var copy = new Button { Content = "⧉ העתק", FontSize = 11.5, Padding = new Thickness(8, 2, 8, 2), Background = Brushes.Transparent, Foreground = Theme.Res("TextSecondary"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        copy.Click += (_, _) => { try { Clipboard.SetText(text); } catch { } };
        var speak = new Button { Content = "🔊", FontSize = 11.5, Padding = new Thickness(8, 2, 8, 2), Background = Brushes.Transparent, Foreground = Theme.Res("TextSecondary"), ToolTip = "הקרא" };
        speak.Click += (_, _) => _ = _speech.SpeakAsync(text, CancellationToken.None);
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Children = { copy, speak } });
        Messages.Children.Add(stack);
        ScrollToEnd();
    }

    private void AddInfo(string text)
    {
        Messages.Children.Add(new TextBlock { Text = text, Foreground = Theme.Res("TextSecondary"), FontSize = 13, Margin = new Thickness(8, 6, 8, 6), TextWrapping = TextWrapping.Wrap });
        ScrollToEnd();
    }

    private static TextBox Selectable(string text, Brush foreground, double size) => new()
    {
        Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        Foreground = foreground, FontSize = size, Padding = new Thickness(0), Cursor = Cursors.IBeam,
        FlowDirection = Theme.ContainsHebrew(text) || !text.Any(char.IsLetter) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
    };

    private void ScrollToEnd() => Dispatcher.BeginInvoke(() => Scroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_host.IsExiting) { e.Cancel = true; Hide(); return; }
        _speech.Dispose();
        try { _pill.Close(); } catch { }
        base.OnClosing(e);
    }
}
