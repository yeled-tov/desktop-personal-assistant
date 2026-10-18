using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;
using DesktopAssistant.Input;
using DesktopAssistant.Settings;

namespace DesktopAssistant.UI;

public sealed class SettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly AppSettings _settings;
    private readonly List<ProviderConfig> _providers;

    private readonly ComboBox _provider = new() { MinWidth = 280, DisplayMemberPath = nameof(ProviderConfig.DisplayName) };
    private readonly PasswordBox _key = new() { MinWidth = 280, Padding = new Thickness(4) };
    private readonly TextBlock _keyStatus = new() { Margin = new Thickness(0, 4, 0, 0), FontSize = 12.5 };
    private readonly TextBlock _keyLink = new() { Margin = new Thickness(0, 4, 0, 0), FontSize = 12.5 };
    private readonly TextBox _baseUrl = new() { MinWidth = 280, Padding = new Thickness(4), FlowDirection = FlowDirection.LeftToRight };
    private readonly ComboBox _model = new() { MinWidth = 280, IsEditable = true, FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox _transcription = new() { MinWidth = 280, Padding = new Thickness(4), FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBlock _testResult = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MaxWidth = 480 };

    private readonly TextBox _hkAssistant = HotkeyBox(), _hkVoice = HotkeyBox(), _hkCircle = HotkeyBox(), _hkTranslate = HotkeyBox();
    private readonly CheckBox _copilot = new() { Content = "גם מקש ה-Copilot במקלדת פותח את העוזר" };
    private readonly CheckBox _longPress = new() { Content = "לחיצה ארוכה על משטח המגע / העכבר (בלי להזיז) פותחת \"הקף וחפש\"" };
    private readonly Slider _longPressMs = new() { Minimum = 500, Maximum = 2000, TickFrequency = 100, IsSnapToTickEnabled = true, Width = 220 };
    private readonly TextBlock _longPressLabel = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    private readonly CheckBox _speak = new() { Content = "הקרא את התשובות בקול" };
    private readonly CheckBox _wakeEnabled = new() { Content = "האזנה למילת הפעלה — לקרוא לעוזר בקול והוא נפתח לבד" };
    private readonly TextBox _assistantName = new() { MinWidth = 200, Padding = new Thickness(4) };
    private readonly TextBox _wakePhrase = new() { MinWidth = 200, Padding = new Thickness(4) };
    private readonly TextBlock _wakeStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 8) };
    private readonly CheckBox _autoSend = new() { Content = "שלח אוטומטית אחרי שסיימתי לדבר" };
    private readonly ComboBox _target = new() { IsEditable = true, MinWidth = 200, ItemsSource = new[] { "עברית", "English", "العربية", "Русский", "Français", "Español", "Deutsch", "አማርኛ", "Українська" } };

    private readonly CheckBox _autostart = new() { Content = "הפעל עם Windows" };
    private readonly CheckBox _startHidden = new() { Content = "כשנפתח עם Windows — התחל מוסתר (רק סמל ליד השעון)" };
    private readonly CheckBox _confirm = new() { Content = "בקש אישור לפני פעולות רגישות (שיחה, פקודת מערכת, שליחה/קנייה/מחיקה)" };
    private readonly Slider _steps = new() { Minimum = 5, Maximum = 80, TickFrequency = 5, IsSnapToTickEnabled = true, Width = 220 };
    private readonly TextBlock _stepsLabel = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ListBox _memory = new() { Height = 140, MinWidth = 420 };

    private ProviderConfig? _current;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        _settings = host.Controller.Settings;
        _providers = _settings.Providers.Select(p => p.Clone()).ToList();
        Title = "הגדרות — העוזר האישי";
        Width = 640; Height = 680; MinWidth = 560; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FlowDirection = FlowDirection.RightToLeft;
        FontFamily = Theme.Font; FontSize = 13.5;
        Background = new SolidColorBrush(Color.FromRgb(248, 249, 251));
        Topmost = true;
        Icon = Theme.AppIcon;

        var tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0), Padding = new Thickness(16) };
        tabs.Items.Add(new TabItem { Header = "  בינה מלאכותית  ", Content = Scroll(BuildAiTab()) });
        tabs.Items.Add(new TabItem { Header = "  קיצורים ומחוות  ", Content = Scroll(BuildShortcutsTab()) });
        tabs.Items.Add(new TabItem { Header = "  קול ומילת הפעלה  ", Content = Scroll(BuildVoiceTab()) });
        tabs.Items.Add(new TabItem { Header = "  כללי וזיכרון  ", Content = Scroll(BuildGeneralTab()) });

        var save = new Button { Content = "שמור", Style = (Style)Application.Current.Resources["AccentButton"], MinWidth = 110 };
        var cancel = new Button { Content = "ביטול", MinWidth = 90 };
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 10, 14, 14), Children = { save, cancel } };

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;
        LoadValues();
    }

    // ---------------- tabs ----------------

    private UIElement BuildAiTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(Note("בחר את ה\"מוח\" של העוזר. כל ספק עובד: Gemini (יש מסלול חינמי), OpenAI, Claude, Groq, OpenRouter, DeepSeek, או מודל מקומי עם Ollama. המפתח נשמר מוצפן במנהל האישורים של Windows."));
        panel.Children.Add(Row("ספק", _provider));
        var keyStack = new StackPanel { Children = { _key, _keyStatus, _keyLink } };
        var deleteKey = new Button { Content = "מחק מפתח", Padding = new Thickness(10, 4, 10, 4), VerticalAlignment = VerticalAlignment.Top };
        deleteKey.Click += (_, _) => { if (_current is null) return; try { SecretStore.Delete(_current.Id); } catch { } UpdateKeyStatus(); };
        panel.Children.Add(Row("מפתח API", new DockPanel { Children = { keyStack } }, deleteKey));
        panel.Children.Add(Row("כתובת שרת", _baseUrl));
        var loadModels = new Button { Content = "טען מודלים", Padding = new Thickness(10, 4, 10, 4) };
        loadModels.Click += async (_, _) => await LoadModelsAsync();
        panel.Children.Add(Row("מודל", _model, loadModels));
        panel.Children.Add(Row("מודל תמלול (דיבור)", _transcription));
        panel.Children.Add(Note("למשל whisper-large-v3-turbo ב-Groq או gpt-transcribe ב-OpenAI. ב-Gemini אין צורך — הוא מתמלל בעצמו."));
        var test = new Button { Content = "🔌 בדוק חיבור", Style = (Style)Application.Current.Resources["AccentButton"], HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        test.Click += async (_, _) => await TestAsync();
        panel.Children.Add(test);
        panel.Children.Add(_testResult);

        _provider.ItemsSource = _providers;
        _provider.SelectionChanged += (_, _) => { StoreCurrent(); ShowProvider(_provider.SelectedItem as ProviderConfig); };
        return panel;
    }

    private UIElement BuildShortcutsTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(Note("לחץ בתוך תיבה ואז הקש את צירוף המקשים החדש. Backspace מנקה."));
        panel.Children.Add(Row("פתיחת העוזר", _hkAssistant));
        panel.Children.Add(Row("דיבור", _hkVoice));
        panel.Children.Add(Row("הקף וחפש", _hkCircle));
        panel.Children.Add(Row("תרגום מסך", _hkTranslate));
        panel.Children.Add(Spaced(_copilot));
        panel.Children.Add(Spaced(_longPress, 18));
        _longPressMs.ValueChanged += (_, _) => _longPressLabel.Text = $"{_longPressMs.Value / 1000:0.0} שניות";
        panel.Children.Add(Row("משך הלחיצה", new StackPanel { Orientation = Orientation.Horizontal, Children = { _longPressMs, _longPressLabel } }));
        panel.Children.Add(Note("לחיצה רגילה, לחיצה כפולה וגרירה ממשיכות לעבוד כרגיל — רק לחיצה ארוכה ושקטה מפעילה את החיפוש."));
        return panel;
    }

    private UIElement BuildVoiceTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "קריאה בקול", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(Spaced(_wakeEnabled, 4));
        panel.Children.Add(Row("שם העוזר", _assistantName));
        panel.Children.Add(Row("מילת הפעלה", _wakePhrase));
        panel.Children.Add(_wakeStatus);
        panel.Children.Add(Note("אומרים את מילת ההפעלה והעוזר נפתח ומקשיב. אפשר גם להמשיך באותו משפט: \"היי שיר, פתח וורד\" — והפעולה מתבצעת מיד.\n" +
            "פרטיות: המיקרופון מנותח במחשב. רק כשמישהו אומר משפט קצר (עד 6 שניות) הקטע נשלח לתמלול אצל ספק ה-AI כדי לבדוק אם נאמרה מילת ההפעלה; שיחות ארוכות לא נשלחות, ושום הקלטה לא נשמרת. אפשר לכבות גם מהסמל ליד השעון."));
        string? lastAutoPhrase = null;
        _assistantName.TextChanged += (_, _) =>
        {
            // Keep "היי <name>" in sync while the user hasn't typed a custom phrase.
            var auto = "היי " + _assistantName.Text.Trim();
            if (lastAutoPhrase is null || _wakePhrase.Text == lastAutoPhrase || _wakePhrase.Text.Length == 0) _wakePhrase.Text = auto;
            lastAutoPhrase = auto;
        };
        panel.Children.Add(new TextBlock { Text = "דיבור והקראה", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 14, 0, 4) });
        panel.Children.Add(Spaced(_autoSend));
        panel.Children.Add(Spaced(_speak));
        panel.Children.Add(Note("כדי שההקראה תהיה בעברית: הגדרות Windows ← זמן ושפה ← דיבור ← הוסף קול עברי."));
        panel.Children.Add(Row("לתרגם מסך לשפה", _target));
        return panel;
    }

    private UIElement BuildGeneralTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(Spaced(_autostart));
        panel.Children.Add(Spaced(_startHidden));
        panel.Children.Add(Spaced(_confirm));
        _steps.ValueChanged += (_, _) => _stepsLabel.Text = $"{_steps.Value:0} צעדים";
        panel.Children.Add(Row("מקסימום צעדים למשימה", new StackPanel { Orientation = Orientation.Horizontal, Children = { _steps, _stepsLabel } }));
        panel.Children.Add(new TextBlock { Text = "מה העוזר זוכר עליך", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) });
        panel.Children.Add(_memory);
        var remove = new Button { Content = "מחק את הנבחר", Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Right };
        remove.Click += (_, _) => { if (_memory.SelectedItem is string item) _memory.Items.Remove(item); };
        var logs = new Button { Content = "📄 פתח לוגים", Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Right };
        logs.Click += (_, _) => { Directory.CreateDirectory(AppLog.LogDirectory); AppLauncher.ShellOpen(AppLog.LogDirectory, null); };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { remove, logs } });
        panel.Children.Add(Note($"גרסה {typeof(App).Assembly.GetName().Version?.ToString(3)} · הגדרות ולוגים: {AppLog.DataDirectory}"));
        return panel;
    }

    // ---------------- data ----------------

    private void LoadValues()
    {
        _provider.SelectedItem = _providers.FirstOrDefault(p => p.Id == _settings.ActiveProviderId) ?? _providers[0];
        _hkAssistant.Text = _settings.AssistantHotkey; _hkVoice.Text = _settings.VoiceHotkey; _hkCircle.Text = _settings.CircleSearchHotkey; _hkTranslate.Text = _settings.TranslateHotkey;
        _copilot.IsChecked = _settings.UseCopilotKey;
        _longPress.IsChecked = _settings.TouchpadLongPressEnabled;
        _longPressMs.Value = _settings.LongPressMilliseconds;
        _speak.IsChecked = _settings.SpeakReplies;
        _wakeEnabled.IsChecked = _settings.WakeWordEnabled;
        _assistantName.Text = _settings.AssistantName;
        _wakePhrase.Text = _settings.WakePhrase;
        var canHear = _host.Controller.CanTranscribe;
        _wakeStatus.Text = !canHear ? "⚠ כדי שהעוזר ישמע אותך צריך מפתח של Gemini, OpenAI או Groq (לשונית 'בינה מלאכותית')."
            : _host.WakeWordActive ? "✓ מאזין עכשיו." : "כבוי כרגע.";
        _wakeStatus.Foreground = !canHear ? new SolidColorBrush(Color.FromRgb(197, 34, 31)) : Brushes.DimGray;
        _autoSend.IsChecked = _settings.AutoSendVoice;
        _target.Text = _settings.TranslationTarget;
        _autostart.IsChecked = _settings.StartWithWindows;
        _startHidden.IsChecked = _settings.StartHidden;
        _confirm.IsChecked = _settings.ConfirmSensitiveActions;
        _steps.Value = _settings.MaxAgentSteps;
        foreach (var item in _settings.Memory) _memory.Items.Add(item);
    }

    private void ShowProvider(ProviderConfig? provider)
    {
        _current = provider;
        if (provider is null) return;
        _key.Password = "";
        _baseUrl.Text = provider.BaseUrl;
        _model.ItemsSource = null;
        _model.Text = provider.Model;
        _transcription.Text = provider.TranscriptionModel;
        _transcription.IsEnabled = provider.Kind == ProviderKind.OpenAICompatible;
        _testResult.Text = "";
        UpdateKeyStatus();
        var url = ProviderPresets.KeyUrl(provider.Id);
        _keyLink.Inlines.Clear();
        if (url is not null)
        {
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("איך משיגים מפתח? לחץ כאן"));
            link.Click += (_, _) => AppLauncher.OpenUrl(url);
            _keyLink.Inlines.Add(link);
        }
    }

    private void UpdateKeyStatus()
    {
        if (_current is null) return;
        if (!_current.RequiresKey) { _keyStatus.Text = "לא נדרש מפתח לספק הזה."; _keyStatus.Foreground = Brushes.Gray; return; }
        var has = SecretStore.Has(_current.Id);
        _keyStatus.Text = has ? "✓ מפתח שמור (השאר ריק כדי לא לשנות)" : "אין מפתח שמור";
        _keyStatus.Foreground = has ? new SolidColorBrush(Color.FromRgb(30, 142, 62)) : new SolidColorBrush(Color.FromRgb(197, 34, 31));
    }

    private void StoreCurrent()
    {
        if (_current is null) return;
        _current.BaseUrl = _baseUrl.Text.Trim();
        _current.Model = _model.Text.Trim();
        _current.TranscriptionModel = _transcription.Text.Trim();
        if (_key.Password.Trim().Length > 0)
        {
            try { SecretStore.Save(_current.Id, _key.Password.Trim()); _key.Password = ""; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "שמירת מפתח"); }
        }
    }

    private async Task LoadModelsAsync()
    {
        StoreCurrent();
        if (_current is null) return;
        _testResult.Text = "טוען רשימת מודלים…";
        try
        {
            var models = await ChatModelFactory.Create(_current).ListModelsAsync(CancellationToken.None);
            var text = _model.Text;
            _model.ItemsSource = models;
            _model.Text = text;
            _testResult.Text = models.Count == 0 ? "הספק לא החזיר מודלים." : $"נמצאו {models.Count} מודלים — פתח את הרשימה ובחר.";
            _model.IsDropDownOpen = models.Count > 0;
        }
        catch (Exception ex) { _testResult.Text = "⚠ " + ex.Message; }
        UpdateKeyStatus();
    }

    private async Task TestAsync()
    {
        StoreCurrent();
        UpdateKeyStatus();
        if (_current is null) return;
        _testResult.Foreground = Brushes.Black;
        _testResult.Text = "בודק…";
        try
        {
            var model = ChatModelFactory.Create(_current);
            var reply = await model.CompleteAsync("", [ChatMessage.User("ענה במילה אחת: שלום")], [], new ModelOptions(MaxOutputTokens: 200), CancellationToken.None);
            _testResult.Foreground = new SolidColorBrush(Color.FromRgb(30, 142, 62));
            _testResult.Text = $"✓ מחובר! המודל ענה: {reply.Text}";
        }
        catch (Exception ex)
        {
            _testResult.Foreground = new SolidColorBrush(Color.FromRgb(197, 34, 31));
            _testResult.Text = "⚠ " + ex.Message;
        }
    }

    private void Save()
    {
        StoreCurrent();
        foreach (var box in new[] { _hkAssistant, _hkVoice, _hkCircle, _hkTranslate })
            if (box.Text.Length > 0 && !Hotkey.TryParse(box.Text, out _)) { MessageBox.Show(this, $"הקיצור '{box.Text}' לא תקין.", "הגדרות"); return; }

        if (_wakeEnabled.IsChecked == true && _wakePhrase.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            MessageBox.Show(this, "מילת הפעלה צריכה להיות לפחות שתי מילים (למשל \"היי שיר\"), אחרת העוזר ייפתח בטעות באמצע שיחות.", "מילת הפעלה");
            return;
        }
        _settings.Providers = _providers;
        if (_provider.SelectedItem is ProviderConfig selected) _settings.ActiveProviderId = selected.Id;
        _settings.AssistantHotkey = _hkAssistant.Text; _settings.VoiceHotkey = _hkVoice.Text; _settings.CircleSearchHotkey = _hkCircle.Text; _settings.TranslateHotkey = _hkTranslate.Text;
        _settings.UseCopilotKey = _copilot.IsChecked == true;
        _settings.TouchpadLongPressEnabled = _longPress.IsChecked == true;
        _settings.LongPressMilliseconds = (int)_longPressMs.Value;
        _settings.SpeakReplies = _speak.IsChecked == true;
        _settings.WakeWordEnabled = _wakeEnabled.IsChecked == true;
        _settings.AssistantName = _assistantName.Text.Trim();
        _settings.WakePhrase = _wakePhrase.Text.Trim();
        _settings.AutoSendVoice = _autoSend.IsChecked == true;
        _settings.TranslationTarget = string.IsNullOrWhiteSpace(_target.Text) ? "עברית" : _target.Text.Trim();
        _settings.StartWithWindows = _autostart.IsChecked == true;
        _settings.StartHidden = _startHidden.IsChecked == true;
        _settings.ConfirmSensitiveActions = _confirm.IsChecked == true;
        _settings.MaxAgentSteps = (int)_steps.Value;
        _settings.Memory = _memory.Items.OfType<string>().ToList();
        _settings.Normalize();
        _host.OnSettingsChanged();
        Close();
    }

    // ---------------- helpers ----------------

    private static TextBox HotkeyBox()
    {
        var box = new TextBox { MinWidth = 200, IsReadOnly = true, Padding = new Thickness(4), FlowDirection = FlowDirection.LeftToRight, Cursor = Cursors.Hand, IsReadOnlyCaretVisible = false };
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Back or Key.Delete) { box.Text = ""; return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab) return;
            var mods = Keyboard.Modifiers;
            var name = key switch
            {
                Key.Space => "Space", Key.Enter => "Enter", Key.Escape => "Esc",
                >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
                >= Key.A and <= Key.Z => key.ToString(),
                >= Key.F1 and <= Key.F24 => key.ToString(),
                Key.OemPlus => "PLUS", Key.OemMinus => "MINUS", Key.OemPeriod => "PERIOD", Key.OemComma => "COMMA",
                _ => key.ToString().ToUpperInvariant()
            };
            var win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
            if (mods == ModifierKeys.None && !win && !(key >= Key.F1 && key <= Key.F24)) return; // a bare letter would block typing everywhere
            box.Text = Hotkey.Format(mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt), mods.HasFlag(ModifierKeys.Shift), win, name);
        };
        return box;
    }

    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4) };

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 12) };

    private static FrameworkElement Spaced(FrameworkElement element, double top = 10) { element.Margin = new Thickness(0, top, 0, 4); return element; }

    private static UIElement Row(string label, UIElement control, UIElement? extra = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        if (extra is not null) { Grid.SetColumn(extra, 2); grid.Children.Add(extra); }
        return grid;
    }
}
