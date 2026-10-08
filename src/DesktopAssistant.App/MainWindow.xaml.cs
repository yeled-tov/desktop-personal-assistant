using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopAssistant.Automation;
using DesktopAssistant.ScreenTranslation;
using DesktopAssistant.Localization;
using DesktopAssistant.AI;
using DesktopAssistant.Infrastructure;
using MessageBox = System.Windows.MessageBox;

namespace DesktopAssistant;

public partial class MainWindow : Window
{
    private readonly CommandRouter _router = new(App.Services.Launcher, App.Services.Safety);
    private readonly TrayController _tray;
    private readonly GlobalHotkeys _hotkeys;
    private CancellationTokenSource? _activeTask;
    private readonly List<ConversationMessage> _conversation = [];

    public MainWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        _tray = new TrayController(this, () => _ = StartTranslationAsync());
        _hotkeys = new GlobalHotkeys(this, () => ActivateAssistant(), () => _ = StartTranslationAsync());
        Loaded += MainWindow_Loaded;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        Closing += (_, e) => { if (!App.IsExiting) { e.Cancel = true; Hide(); } else _hotkeys.Dispose(); };
    }

    private void ApplyLocalization()
    {
        var language = App.Services.Settings.UiLanguage;
        FlowDirection = UiText.IsRightToLeft(language) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Title = UiText.Get("title", language); TitleText.Text = UiText.Get("title", language);
        SettingsButton.Content = UiText.Get("settings", language); SendButton.Content = UiText.Get("send", language); TranslateButton.Content = UiText.Get("translate", language);
        OfflineNote.Text = language == "en" ? "Local actions work without an AI connection." : "פעולות מקומיות פועלות גם ללא חיבור AI.";
        StatusText.Text = $"{UiText.Get("ready", language)} · Ctrl + Alt + Space";
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _tray.Show(); _hotkeys.Register();
        AddAssistant("שלום! אני יכול לפתוח אפליקציות ואתרים, לחפש ברשת ולחשב. אפשר לכתוב בעברית או באנגלית.");
        var settings = App.Services.Settings;
        if (!settings.OnboardingCompleted)
        {
            var welcome = new WelcomeWindow { Owner = this };
            welcome.ShowDialog();
            settings.OnboardingCompleted = true; Configuration.SettingsStore.Save(settings);
            if (welcome.ConnectGemini) new SettingsWindow { Owner = this }.ShowDialog();
        }
        if (Environment.GetCommandLineArgs().Contains("--minimized", StringComparer.OrdinalIgnoreCase) && settings.StartMinimized) Hide();
    }

    public void ActivateAssistant()
    {
        Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); InputBox.Focus(); });
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();
    private async void Input_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await SendAsync(); } }

    private async Task SendAsync()
    {
        var text = InputBox.Text.Trim();
        if (text.Length == 0) return;
        if (text.Equals("עצור", StringComparison.OrdinalIgnoreCase) || text.Equals("בטל", StringComparison.OrdinalIgnoreCase) || text.Equals("stop", StringComparison.OrdinalIgnoreCase)) { StopActiveTask(); AddAssistant("עצרתי את הפעולה הנוכחית."); return; }
        if (_activeTask is not null) { AddAssistant("יש משימה פעילה. לחץ על עצור לפני התחלת משימה חדשה."); return; }
        var priorHistory = _conversation.TakeLast(12).ToArray();
        InputBox.Clear(); AddUser(text); StatusText.Text = "מבצע פעולה…";
        _activeTask?.Dispose(); _activeTask = new CancellationTokenSource();
        try
        {
            CommandResult result;
            if (IsTranslationRequest(text)) result = new CommandResult(await StartTranslationAsync());
            else if (LocalMemory.TryHandle(text, App.Services.Settings, out var memoryResponse)) result = new CommandResult(memoryResponse);
            else if (IsScreenQuestion(text))
            {
                result = new CommandResult(await AnalyzeScreenAsync(text, _activeTask.Token));
            }
            else if (App.Services.Settings.AiMode != "Local" && App.Services.Settings.AllowCloudAi && App.Services.Settings.EnableDesktopControl && CredentialStore.TryGetApiKey(out _))
            {
                var run = await App.Services.Agent.RunAsync(text,
                    status => Dispatcher.InvokeAsync(() => StatusText.Text = status).Task,
                    (tool, description) => Dispatcher.InvokeAsync(() => MessageBox.Show(this, description, "נדרשת הסכמה", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading) == MessageBoxResult.Yes).Task,
                    _activeTask.Token, priorHistory);
                result = new CommandResult(run.Text);
            }
            else if (App.Services.Settings.AiMode != "Local" && App.Services.Settings.AllowCloudAi && CredentialStore.TryGetApiKey(out _) && !IsComputerActionRequest(text))
            {
                var reply = await App.Services.AiProvider.ReplyAsync(priorHistory.Append(new ConversationMessage("user", text)).ToArray(), _activeTask.Token);
                result = new CommandResult(reply.Text);
            }
            else result = await _router.HandleAsync(text);
            if (result.RequiresConfirmation)
            {
                var choice = MessageBox.Show(this, result.ConfirmationText, "נדרשת הסכמה", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
                if (choice != MessageBoxResult.Yes) { AddAssistant("הפעולה בוטלה."); return; }
                AddAssistant("פעולות רגישות אינן זמינות עדיין בגרסה זו."); return;
            }
            AddAssistant(result.Message);
        }
        catch (OperationCanceledException) { AddAssistant("הפעולה נעצרה."); }
        catch (Exception ex) { AddAssistant($"הפעולה נכשלה: {ex.Message}"); }
        finally { _activeTask?.Dispose(); _activeTask = null; StatusText.Text = "מוכן · Ctrl + Alt + Space"; }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopActiveTask();
    private void StopActiveTask() { try { _activeTask?.Cancel(); } catch (ObjectDisposedException) { } }

    private void AddUser(string message) { _conversation.Add(new ConversationMessage("user", message)); AddBubble(message, true); }
    private void AddAssistant(string message) { _conversation.Add(new ConversationMessage("assistant", message)); AddBubble(message, false); }
    private void AddBubble(string message, bool user)
    {
        var border = new Border { Background = new System.Windows.Media.SolidColorBrush(user ? System.Windows.Media.Color.FromRgb(99,91,255) : System.Windows.Media.Color.FromRgb(243,244,247)), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(user ? 55 : 0, 4, user ? 0 : 55, 4), HorizontalAlignment = user ? HorizontalAlignment.Left : HorizontalAlignment.Right };
        border.Child = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = new System.Windows.Media.SolidColorBrush(user ? System.Windows.Media.Colors.White : System.Windows.Media.Color.FromRgb(32,35,42)) };
        History.Children.Add(border); HistoryScroll.ScrollToEnd();
    }

    private async void Translate_Click(object sender, RoutedEventArgs e) => await StartTranslationAsync();
    private async Task<string> StartTranslationAsync()
    {
        var settings = App.Services.Settings;
        var useGemini = false;
        if (settings.TranslationMode != "Local")
        {
            var hasKey = CredentialStore.TryGetApiKey(out _);
            if (!hasKey && settings.TranslationMode == "Gemini") { MessageBox.Show(this, "כדי לתרגם עם Gemini, שמור מפתח API בהגדרות. לא נשלח צילום מסך.", "תרגום מסך", MessageBoxButton.OK, MessageBoxImage.Information); return "לא נשלח צילום המסך."; }
            if (hasKey && !settings.AllowCloudScreenTranslation)
            {
                var consent = MessageBox.Show(this, "צילום המסך יישלח ל-Gemini לצורך זיהוי ותרגום הטקסט. לאשר ולאפשר זאת בהגדרות?", "פרטיות · תרגום מסך", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
                if (consent == MessageBoxResult.Yes) { settings.AllowCloudScreenTranslation = true; Configuration.SettingsStore.Save(settings); }
                else if (settings.TranslationMode == "Gemini") return "התרגום בוטל.";
            }
            useGemini = hasKey && settings.AllowCloudScreenTranslation;
        }
        try
        {
            Hide(); await Task.Delay(180, _activeTask?.Token ?? CancellationToken.None); await new TranslationOverlay((IGeminiVisionProvider)App.Services.AiProvider, useGemini, settings.TargetLanguage, _activeTask?.Token ?? CancellationToken.None).ShowAsync(this);
            return useGemini ? "תרגום המסך הסתיים." : "מצב תרגום מקומי עדיין אינו זמין.";
        }
        catch (OperationCanceledException) { return "תרגום המסך נעצר."; }
        catch (Exception ex) { MessageBox.Show(this, "לא ניתן להפעיל תרגום מסך כרגע. " + ex.Message, "תרגום מסך", MessageBoxButton.OK, MessageBoxImage.Information); return "לא ניתן לתרגם כרגע."; }
        finally { Show(); WindowState = WindowState.Normal; Activate(); }
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
    private void Voice_Click(object sender, RoutedEventArgs e) => AddAssistant("זיהוי דיבור בעברית עדיין אינו מוגדר. אפשר להשתמש בקלט הטקסט; מצב האזנה ברקע כבוי כברירת מחדל.");

    private static bool IsScreenQuestion(string request) => request.Contains("מה כתוב במסך", StringComparison.OrdinalIgnoreCase) || request.Contains("מה אני רואה", StringComparison.OrdinalIgnoreCase) || request.Contains("מה כתוב כאן", StringComparison.OrdinalIgnoreCase) || request.Contains("מה כתוב פה", StringComparison.OrdinalIgnoreCase) || request.Contains("read this screen", StringComparison.OrdinalIgnoreCase);
    private static bool IsTranslationRequest(string request) => request.Contains("תרגם מסך", StringComparison.OrdinalIgnoreCase) || request.Contains("תרגם לי את המסך", StringComparison.OrdinalIgnoreCase) || request.Contains("תרגם את המסך", StringComparison.OrdinalIgnoreCase) || request.Contains("תרגם את מה שאני רואה", StringComparison.OrdinalIgnoreCase) || request.Contains("translate this screen", StringComparison.OrdinalIgnoreCase);
    private static bool IsComputerActionRequest(string request) => new[] { "פתח", "תפתח", "חפש", "תחפש", "סגור", "לחץ", "תלחץ", "תקליד", "open", "search", "close", "click", "type", "switch window", "minimize", "maximize" }.Any(term => request.Contains(term, StringComparison.OrdinalIgnoreCase));

    private async Task<string> AnalyzeScreenAsync(string question, CancellationToken cancellationToken)
    {
        var settings = App.Services.Settings;
        if (settings.AiMode == "Local" || !CredentialStore.TryGetApiKey(out _)) return "ניתוח מסך דורש מפתח Gemini ומצב Gemini Online. צילום המסך לא נשלח.";
        if (!settings.AllowScreenshotAnalysis)
        {
            var consent = MessageBox.Show(this, "צילום המסך יישלח ל-Gemini לצורך ניתוח. לאשר ולהפעיל זאת בהגדרות?", "פרטיות · ניתוח מסך", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
            if (consent != MessageBoxResult.Yes) return "לא שלחתי את צילום המסך.";
            settings.AllowScreenshotAnalysis = true; Configuration.SettingsStore.Save(settings);
        }
        Hide(); await Task.Delay(180, cancellationToken);
        try
        {
            StatusText.Text = "מנתח את המסך…";
            var frame = await new ScreenCaptureProvider().CaptureWithoutAssistantAsync(cancellationToken);
            return await ((IGeminiVisionProvider)App.Services.AiProvider).AnalyzeScreenshotAsync(frame, question, cancellationToken);
        }
        finally { Show(); Activate(); }
    }
}
