using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DesktopAssistant.AI;
using Window = System.Windows.Window;
using Panel = System.Windows.Controls.Panel;

namespace DesktopAssistant;

public sealed class SettingsWindow : Window
{
    private readonly CheckBox _startWithWindows = new() { Content = "הפעלה עם Windows" };
    private readonly CheckBox _startMinimized = new() { Content = "התחלה ממוזערת למגש" };
    private readonly CheckBox _wakeWord = new() { Content = "האזנה למילת הפעלה" };
    private readonly CheckBox _tts = new() { Content = "הקראת תשובות בקול" };
    private readonly CheckBox _vision = new() { Content = "אפשר ל-Gemini לנתח צילומי מסך" };
    private readonly CheckBox _automation = new() { Content = "לאפשר פתיחת אפליקציות ואתרים" };
    private readonly ComboBox _language = new() { Width = 150 };
    private readonly ComboBox _aiMode = new() { Width = 260 };
    private readonly ComboBox _translationMode = new() { Width = 260 };
    private readonly ComboBox _quality = new() { Width = 180 };
    private readonly CheckBox _allowCloudAi = new() { Content = "אפשר לשלוח הודעות ל-Gemini" };
    private readonly CheckBox _allowCloudTranslation = new() { Content = "אפשר ניתוח מסך בענן לתרגום" };
    private readonly CheckBox _allowGeminiVoice = new() { Content = "אפשר Gemini לקול (לא זמין עדיין)" };
    private readonly CheckBox _allowMemory = new() { Content = "אפשר זיכרון מקומי" };
    private readonly TextBox _model = new() { Text = "gemini-3.8-flash", Width = 240 };
    private readonly TextBox _visionModel = new() { Text = "gemini-3.8-flash", Width = 240 };
    private readonly PasswordBox _apiKey = new() { Width = 300 };
    private readonly TextBlock _geminiStatus = new() { Text = "Gemini לא מחובר", TextWrapping = TextWrapping.Wrap };
    private bool _geminiVerified;
    private readonly TextBox _memory = new() { AcceptsReturn = true, Height = 76, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { Foreground = System.Windows.Media.Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _about = new() { TextWrapping = TextWrapping.Wrap };

    public SettingsWindow()
    {
        Title = "הגדרות · עוזר אישי למחשב"; Width = 480; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner; FlowDirection = FlowDirection.RightToLeft; Background = System.Windows.Media.Brushes.White;
        var settings = App.Services.Settings;
        _startWithWindows.IsChecked = settings.StartWithWindows; _startMinimized.IsChecked = settings.StartMinimized;
        _wakeWord.IsChecked = false; _wakeWord.IsEnabled = false; _wakeWord.ToolTip = "ספק זיהוי דיבור ומילת הפעלה עדיין לא מחוברים.";
        _tts.IsChecked = settings.SpeakResponses; _tts.IsEnabled = false; _tts.ToolTip = "ספק דיבור עדיין לא מחובר.";
        _vision.IsChecked = settings.AllowScreenshotAnalysis; _vision.IsEnabled = true;
        _automation.IsChecked = settings.EnableDesktopControl; _memory.Text = settings.LocalMemory;
        _allowCloudAi.IsChecked = settings.AllowCloudAi; _allowCloudTranslation.IsChecked = settings.AllowCloudScreenTranslation;
        _allowGeminiVoice.IsChecked = settings.AllowGeminiVoice; _allowGeminiVoice.IsEnabled = false;
        _allowMemory.IsChecked = settings.AllowLocalMemory; _model.Text = settings.GeminiModel; _visionModel.Text = settings.GeminiVisionModel;
        RefreshAbout(settings);
        _language.Items.Add(new ComboBoxItem { Content = "עברית", Tag = "he" }); _language.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        _language.SelectedIndex = settings.UiLanguage == "en" ? 1 : 0;
        AddOption(_aiMode, "Gemini Online — מומלץ", "GeminiOnline"); AddOption(_aiMode, "Local / Offline", "Local"); AddOption(_aiMode, "Automatic", "Automatic"); SelectOption(_aiMode, settings.AiMode);
        AddOption(_translationMode, "Gemini", "Gemini"); AddOption(_translationMode, "Local", "Local"); AddOption(_translationMode, "Automatic", "Automatic"); SelectOption(_translationMode, settings.TranslationMode);
        AddOption(_quality, "Fast", "Fast"); AddOption(_quality, "Balanced", "Balanced"); AddOption(_quality, "Accurate", "Accurate"); SelectOption(_quality, settings.TranslationQuality);
        UpdateGeminiStatus();

        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock { Text = "הגדרות", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,8) });
        Section(stack, "כללי", new TextBlock { Text = "שפת ממשק" }, _language, _startWithWindows, _startMinimized, new TextBlock { Text = "קיצור מקשים: Ctrl + Alt + Space  |  תרגום מסך: Win + Shift + T", Margin = new Thickness(0,4,0,0), TextWrapping = TextWrapping.Wrap });
        Section(stack, "קול", _wakeWord, _tts, new TextBlock { Text = "זיהוי דיבור והקראה אינם זמינים עד לחיבור ספק מתאים.", Foreground = System.Windows.Media.Brushes.DimGray });
        Section(stack, "AI", _aiMode, new TextBlock { Text = "Gemini Online: מהיר, חכם, ללא הורדת מודלים גדולים; דורש אינטרנט ומפתח Gemini.\nLocal: פועל מקומית, אך אין רכיבים מקומיים בגרסה זו ולא יורדו מודלים.\nAutomatic: משתמש ב-Gemini כשזמין וביכולות מקומיות אחרת.", TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = "Google Gemini · מפתח API" }, _apiKey, new WrapPanel { Children = { KeyButton("שמור מפתח", SaveKey_Click), KeyButton("בדוק חיבור", TestConnection_Click), KeyButton("מחק מפתח", DeleteKey_Click) } }, new TextBlock { Text = "צריך מפתח Gemini? לחץ כאן כדי ליצור מפתח API.", Margin = new Thickness(0,4,0,0) }, KeyButton("פתח Google AI Studio", OpenAiStudio_Click), new TextBlock { Text = "1. התחבר ל-Google  2. לחץ Create API key  3. העתק  4. הדבק כאן  5. לחץ בדוק חיבור", TextWrapping = TextWrapping.Wrap }, _geminiStatus, new TextBlock { Text = "מודל לשיחה ולסוכן" }, _model, new TextBlock { Text = "מודל לניתוח תמונה ותרגום" }, _visionModel, _allowCloudAi, new TextBlock { Text = "הודעות הטקסט יישלחו ל-Gemini רק כשהאפשרות הזו מסומנת. צילומי מסך דורשים אישור נפרד.", TextWrapping = TextWrapping.Wrap }, _vision, _allowGeminiVoice);
        Section(stack, "שליטה במחשב", _automation, new TextBlock { Text = "פעולות רגישות כגון שליחה או מחיקה דורשות אישור. ביצוען עדיין חסום בגרסה זו.", TextWrapping = TextWrapping.Wrap });
        Section(stack, "זיכרון מקומי", _allowMemory, new TextBlock { Text = "העדפות אישיות נשמרות רק במחשב הזה:", Margin = new Thickness(0,0,0,4) }, _memory);
        Section(stack, "תרגום מסך", _translationMode, new TextBlock { Text = "צילום המסך נשלח ל-Gemini רק כאשר Gemini נבחר ומותר ניתוח מסך. אחרת אין ניתוח ענן. Local OCR/translation לא הותקנו ולא יורדו אוטומטית.", TextWrapping = TextWrapping.Wrap }, _allowCloudTranslation, _quality, new TextBlock { Text = "שפת יעד: עברית" });
        Section(stack, "אודות", new TextBlock { Text = "עוזר אישי למחשב\nעוזר חכם למחשב שלך", TextWrapping = TextWrapping.Wrap }, _about);
        stack.Children.Add(_status);
        var save = new Button { Content = "שמור", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,12,0,0), Padding = new Thickness(24,8,24,8) };
        save.Click += Save_Click; stack.Children.Add(save);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static void Section(Panel panel, string title, params FrameworkElement[] controls)
    {
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0,10,0,4) });
        foreach (var item in controls) { item.Margin = new Thickness(0,3,0,3); panel.Children.Add(item); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = App.Services.Settings;
            s.StartWithWindows = _startWithWindows.IsChecked == true; s.StartMinimized = _startMinimized.IsChecked == true;
            s.UiLanguage = (_language.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "he";
            s.AllowScreenshotAnalysis = _vision.IsChecked == true; s.AllowCloudScreenTranslation = _allowCloudTranslation.IsChecked == true;
            s.AllowCloudAi = _allowCloudAi.IsChecked == true; s.AllowGeminiVoice = _allowGeminiVoice.IsChecked == true;
            s.GeminiModel = string.IsNullOrWhiteSpace(_model.Text) ? "gemini-3.8-flash" : _model.Text.Trim(); s.GeminiVisionModel = string.IsNullOrWhiteSpace(_visionModel.Text) ? s.GeminiModel : _visionModel.Text.Trim();
            s.AiMode = SelectedTag(_aiMode, "GeminiOnline"); s.TranslationMode = SelectedTag(_translationMode, "Gemini"); s.TranslationQuality = SelectedTag(_quality, "Balanced");
            s.AllowLocalMemory = _allowMemory.IsChecked == true; s.EnableDesktopControl = _automation.IsChecked == true; s.LocalMemory = s.AllowLocalMemory ? _memory.Text.Trim() : "";
            Configuration.SettingsStore.Save(s);
            StartupRegistration.SetEnabled(s.StartWithWindows);
            RefreshAbout(s);
            _status.Text = "ההגדרות נשמרו במחשב.";
        }
        catch (Exception ex) { _status.Text = "שמירת ההגדרות נכשלה: " + ex.Message; }
    }

    private static void AddOption(ComboBox combo, string label, string tag) => combo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
    private static void SelectOption(ComboBox combo, string tag) => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, tag)) ?? (object)combo.Items[0];
    private static string SelectedTag(ComboBox combo, string fallback) => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
    private static Button KeyButton(string label, RoutedEventHandler handler) { var button = new Button { Content = label, Margin = new Thickness(3) }; button.Click += handler; return button; }
    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_apiKey.Password)) { _geminiStatus.Text = "הדבק מפתח API לפני השמירה."; return; }
            CredentialStore.SaveApiKey(_apiKey.Password); _apiKey.Clear(); _geminiVerified = false; UpdateGeminiStatus("המפתח נשמר בצורה מאובטחת. לחץ בדוק חיבור.");
        }
        catch (Exception ex) { _apiKey.Clear(); _geminiStatus.Text = "שמירת מפתח Gemini נכשלה: " + ex.Message; }
    }
    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        _geminiStatus.Text = "בודק חיבור ל-Gemini…";
        try
        {
            App.Services.Settings.GeminiModel = string.IsNullOrWhiteSpace(_model.Text) ? "gemini-3.8-flash" : _model.Text.Trim();
            _geminiStatus.Text = await ((GeminiAiProvider)App.Services.AiProvider).TestConnectionAsync(); _geminiVerified = true;
        }
        catch (Exception ex) { _geminiVerified = false; _geminiStatus.Text = "✕ לא ניתן להתחבר ל-Gemini. " + ex.Message; }
    }
    private void DeleteKey_Click(object sender, RoutedEventArgs e)
    {
        try { CredentialStore.DeleteApiKey(); _geminiVerified = false; UpdateGeminiStatus("מפתח Gemini נמחק."); }
        catch (Exception ex) { _geminiStatus.Text = "מחיקת מפתח Gemini נכשלה: " + ex.Message; }
    }
    private void OpenAiStudio_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://aistudio.google.com/app/apikey") { UseShellExecute = true }); }
        catch (Exception ex) { _geminiStatus.Text = "לא ניתן לפתוח את הדפדפן: " + ex.Message; }
    }
    private void UpdateGeminiStatus(string? message = null) => _geminiStatus.Text = message ?? (_geminiVerified ? "● Gemini מחובר" : CredentialStore.TryGetApiKey(out _) ? "○ מפתח Gemini שמור · בדוק חיבור" : "○ Gemini לא מחובר");
    private void RefreshAbout(Configuration.UserSettings settings)
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        var ai = CredentialStore.TryGetApiKey(out _) ? "Google Gemini" : "לא מחובר";
        var translation = settings.TranslationMode == "Local" ? "OCR מקומי לא זמין" : "Gemini Vision";
        var privacy = $"צ'אט לענן: {(settings.AllowCloudAi ? "מופעל" : "כבוי")} · ניתוח מסך: {(settings.AllowScreenshotAnalysis ? "מופעל" : "כבוי")} · תרגום בענן: {(settings.AllowCloudScreenTranslation ? "מופעל" : "כבוי")} · מיקרופון: לא בשימוש";
        _about.Text = $"גרסה {version}\nAI: {ai} · דיבור: לא זמין · תרגום: {translation}\nפרטיות: {privacy}";
    }
}

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue("DesktopAssistant", $"\"{Environment.ProcessPath}\" --minimized");
        else key.DeleteValue("DesktopAssistant", throwOnMissingValue: false);
    }
}
