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

/// <summary>The small panel at the bottom of the screen (Gemini-style). Grows upwards as the conversation grows.</summary>
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
    private bool _userMoved;
    private nint _previousWindow; // the app the user was in when they called the assistant

    public string PreviousWindowTitle => _previousWindow != 0 ? Native.GetWindowTitle(_previousWindow) : "";
    public bool IsBusy => _run is not null;
    public bool IsRecording => _recordCancel is not null;
    public bool IsSpeaking => _speech.IsSpeaking;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        WindowStyling.MakeBorderless(this);
        Icon = Theme.AppIcon;
        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); _userMoved = true; } catch { } } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (IsRecording) _recordCancel?.Cancel();
            else if (IsBusy) _run?.Cancel();
            else Hide();
        };
        SizeChanged += (_, _) => Anchor();
        _pill.StopRequested += () => { _run?.Cancel(); _recordStop?.Cancel(); };
        Loaded += (_, _) => { RefreshProvider(); ResetGreeting(); };
    }

    // ------------- showing / hiding -------------

    public void ShowPanel(bool activate = true)
    {
        RememberPreviousWindow();
        ShowActivated = activate;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Anchor();
        if (activate)
        {
            Activate();
            Input.Focus();
            Keyboard.Focus(Input);
        }
        ShowActivated = true;
    }

    public void Toggle()
    {
        if (IsVisible && IsActive && WindowState != WindowState.Minimized) Hide();
        else ShowPanel();
    }

    private void RememberPreviousWindow()
    {
        var foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out var pid);
        if (foreground != 0 && pid != Environment.ProcessId) _previousWindow = foreground;
    }

    /// <summary>Bottom-centre of the screen, growing upwards — unless the user dragged it elsewhere.</summary>
    private void Anchor()
    {
        if (_userMoved || !IsVisible) return;
        var area = WindowStyling.WorkAreaUnderCursor();
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 14;
        Scroll.MaxHeight = Math.Max(200, area.Height * 0.62);
    }

    public void RefreshProvider()
    {
        var c = _host.Controller;
        TitleText.Text = c.Settings.AssistantName;
        Title = c.Settings.AssistantName;
        var parts = new List<string> { c.StatusLabel };
        if (_host.WakeWordActive) parts.Add($"👂 \"{c.Settings.WakePhrase}\"");
        StatusText.Text = string.Join("  ·  ", parts);
        StatusText.ToolTip = StatusText.Text;
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
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Settings_Click(object sender, RoutedEventArgs e) => _host.OpenSettings();
    private void Circle_Click(object sender, RoutedEventArgs e) => _ = _host.StartCircleSearchAsync();
    private void Mic_Click(object sender, RoutedEventArgs e) => _ = ToggleVoiceAsync();
    private void Dictate_Click(object sender, RoutedEventArgs e) => _ = DictateAsync();
    private void Help_Click(object sender, RoutedEventArgs e) => AddAssistantMessage(AssistantHelp.Text(_host.Controller.Settings, _host.Offline), []);

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        _run?.Cancel();
        _host.Controller.NewConversation();
        Messages.Children.Clear();
        ClearAttachments();
        ResetGreeting();
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

    /// <summary>Entry point used by voice, the wake word, the overlay and suggestion chips.</summary>
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
        SetBusy(true, "חושבת…");
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
            OpenCircleSearch = () => Dispatcher.InvokeAsync(() => { _ = _host.StartCircleSearchAsync(); }).Task,
            Notify = (title, message) => Toast.Show(title, message, 30),
            RunOnUiThread = work => Dispatcher.InvokeAsync(work).Task,
            Help = () => AssistantHelp.Text(_host.Controller.Settings, _host.Offline),
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
            RefreshProvider();
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
        ActivityProgress.IsIndeterminate = busy; // only animate while working (an idle animation costs CPU)
        ActivityText.Text = status;
        SendButton.IsEnabled = !busy;
    }

    // ------------- voice: talk to the assistant -------------

    public async Task ToggleVoiceAsync()
    {
        if (IsRecording) { _recordStop?.Cancel(); return; }
        if (IsBusy) return;
        ShowPanel();
        _speech.Stop();
        _recordCancel = new CancellationTokenSource();
        _recordStop = new CancellationTokenSource();
        ListeningPanel.Visibility = Visibility.Visible;
        ListeningText.Text = "מקשיבה… דברו עכשיו";
        MicButton.Foreground = new SolidColorBrush(Color.FromRgb(234, 67, 53));
        var recorder = new AudioRecorder();
        recorder.LevelChanged += level => Dispatcher.BeginInvoke(() => LevelBar.Width = 4 + level * 140);
        try
        {
            var wav = await Task.Run(() => recorder.RecordAsync(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(1.4), _recordCancel.Token, _recordStop.Token));
            if (wav.Length == 0) { AddInfo("לא שמעתי כלום 🙂 נסו שוב."); return; }
            ListeningText.Text = "מתמללת…";
            LevelBar.Width = 4;
            var text = await Task.Run(() => _host.Speech.TranscribeAsync(wav, _recordCancel.Token));
            if (string.IsNullOrWhiteSpace(text)) { AddInfo("לא הצלחתי להבין, נסו שוב."); return; }
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

    // ------------- dictation: speak, and it types where you are -------------

    /// <summary>
    /// Records one utterance and types it into the window the user was working in (Word, a browser, WhatsApp…).
    /// Only speech-to-text is used — no AI agent — so it is fast and costs almost nothing (or nothing, offline).
    /// </summary>
    public async Task DictateAsync()
    {
        if (IsRecording || IsBusy) { _recordStop?.Cancel(); return; }
        RememberPreviousWindow();
        var target = _previousWindow;
        if (IsVisible) { Hide(); await Task.Delay(120); }
        if (target != 0) await Task.Run(() => WindowManager.Focus(target));

        _recordCancel = new CancellationTokenSource();
        _recordStop = new CancellationTokenSource();
        _pill.SetText("🎙 הכתבה — דברו, ואקליד בשבילכם (לחיצה נוספת או עצור לסיום)");
        _pill.Show();
        Chime.Play();
        try
        {
            var recorder = new AudioRecorder();
            var dots = 0;
            recorder.LevelChanged += level => { if (level > 0.15 && ++dots % 6 == 0) Dispatcher.BeginInvoke(() => _pill.SetText("🎙 שומעת… " + new string('•', 1 + dots / 6 % 4))); };
            var wav = await Task.Run(() => recorder.RecordAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(2.2), _recordCancel.Token, _recordStop.Token));
            if (wav.Length == 0) { _pill.SetText("לא שמעתי כלום"); await Task.Delay(1200); return; }
            _pill.SetText("✍ מתמללת…");
            var text = (await Task.Run(() => _host.Speech.TranscribeAsync(wav, _recordCancel.Token))).Trim();
            if (text.Length == 0) { _pill.SetText("לא הצלחתי להבין"); await Task.Delay(1200); return; }
            if (target != 0) await Task.Run(() => WindowManager.Focus(target));
            await Task.Delay(80);
            await Task.Run(() => { InputSimulator.ReleaseModifiers(); InputSimulator.TypeText(text + " "); });
            AppLog.Info($"Dictation typed {text.Length} chars");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Toast.Show("הכתבה", ex.Message, 8); }
        finally
        {
            _pill.Hide();
            _recordCancel?.Dispose(); _recordCancel = null;
            _recordStop?.Dispose(); _recordStop = null;
        }
    }

    // ------------- rendering -------------

    private void ResetGreeting()
    {
        var s = _host.Controller.Settings;
        GreetingText.Text = $"היי, אני {s.AssistantName} 👋  איך אפשר לעזור?";
        Suggestions.Children.Clear();
        foreach (var (label, prompt) in new[]
        {
            ("📝 פתחי וורד ותכתבי מכתב תודה", "פתח וורד ותכתוב מכתב תודה קצר ויפה למורה"),
            ("👀 מה יש לי על המסך?", "מה יש לי עכשיו על המסך? תסביר בקצרה"),
            ("❓ מה את יודעת לעשות?", "מה את יודעת לעשות?"),
        })
        {
            var chip = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(11, 5, 11, 5), FontSize = 12.5 };
            chip.Click += (_, _) => _ = SubmitAsync(prompt);
            Suggestions.Children.Add(chip);
        }
        Greeting.Visibility = Visibility.Visible;
        Scroll.Visibility = Visibility.Collapsed;
    }

    private void EnsureConversationVisible()
    {
        Greeting.Visibility = Visibility.Collapsed;
        Scroll.Visibility = Visibility.Visible;
    }

    private void AddUserBubble(string text, BitmapSource? image)
    {
        EnsureConversationVisible();
        var stack = new StackPanel();
        if (image is not null) stack.Children.Add(new Image { Source = image, FlowDirection = FlowDirection.LeftToRight, MaxHeight = 110, MaxWidth = 220, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Right });
        stack.Children.Add(Selectable(text, Brushes.White, 14));
        Messages.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(46, 61, 99)), CornerRadius = new CornerRadius(16, 16, 16, 4), Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(50, 6, 0, 2), HorizontalAlignment = HorizontalAlignment.Right, Child = stack
        });
        ScrollToEnd();
    }

    private void AddAssistantMessage(string text, IReadOnlyList<string> actions, bool isError = false)
    {
        EnsureConversationVisible();
        var stack = new StackPanel { Margin = new Thickness(0, 6, 6, 4) };
        if (actions.Count > 0)
            stack.Children.Add(new TextBlock { Text = string.Join("   ", actions.Distinct().Take(10)), FontSize = 11, Foreground = Theme.Res("TextSecondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4), Opacity = 0.85 });
        var row = new DockPanel();
        var mark = new TextBlock { Text = isError ? "⚠" : "✦", FontSize = 14, Margin = new Thickness(0, 1, 0, 0), Foreground = isError ? new SolidColorBrush(Color.FromRgb(242, 139, 130)) : Theme.Res("GeminiGradient") };
        DockPanel.SetDock(mark, Dock.Right);
        row.Children.Add(mark);
        var body = Selectable(text, isError ? new SolidColorBrush(Color.FromRgb(242, 139, 130)) : Theme.Res("TextPrimary"), 14.5);
        body.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(body);
        stack.Children.Add(row);
        var copy = new Button { Content = "⧉ העתק", FontSize = 11, Padding = new Thickness(7, 1, 7, 1), Background = Brushes.Transparent, Foreground = Theme.Res("TextSecondary") };
        copy.Click += (_, _) => { try { Clipboard.SetText(text); } catch { } };
        var speak = new Button { Content = "🔊", FontSize = 11, Padding = new Thickness(7, 1, 7, 1), Background = Brushes.Transparent, Foreground = Theme.Res("TextSecondary"), ToolTip = "הקראה" };
        speak.Click += (_, _) => _ = _speech.SpeakAsync(text, CancellationToken.None);
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Children = { copy, speak } });
        Messages.Children.Add(stack);
        ScrollToEnd();
    }

    private void AddInfo(string text)
    {
        EnsureConversationVisible();
        Messages.Children.Add(new TextBlock { Text = text, Foreground = Theme.Res("TextSecondary"), FontSize = 12.5, Margin = new Thickness(8, 4, 8, 4), TextWrapping = TextWrapping.Wrap });
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
