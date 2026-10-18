using System.Windows;
using System.Windows.Media.Imaging;
using DesktopAssistant.Agent;
using DesktopAssistant.Core;
using DesktopAssistant.Input;
using DesktopAssistant.Overlay;
using DesktopAssistant.Settings;
using DesktopAssistant.UI;

namespace DesktopAssistant;

/// <summary>Composition root: owns every long-lived piece and wires shortcuts to features.</summary>
public sealed class AppHost : IDisposable
{
    private HotkeyManager? _hotkeys;
    private LongPressDetector? _longPress;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    public AssistantController Controller { get; }
    public MainWindow Main { get; }
    public bool IsExiting { get; private set; }

    public AppHost()
    {
        var settings = SettingsStore.Load();
        Controller = new AssistantController(settings);
        Main = new MainWindow(this);
    }

    public void Start(bool startHidden, bool firstRunUi = true)
    {
        var settings = Controller.Settings;
        _tray = new TrayIcon(this);
        _hotkeys = new HotkeyManager();
        ApplyShortcuts(showWarnings: true);
        _longPress = new LongPressDetector(
            () => Application.Current.Dispatcher.BeginInvoke(() => _ = StartCircleSearchAsync()),
            () => CircleSearchSession.IsOpen || Main.IsBusy || ForegroundIsOurs());
        ApplyLongPress();
        AutoStart.Apply(settings.StartWithWindows);

        if (firstRunUi && !settings.OnboardingDone)
        {
            Main.ShowPanel();
            var welcome = new WelcomeWindow(this) { Owner = Main };
            welcome.ShowDialog();
            settings.OnboardingDone = true;
            SettingsStore.Save(settings);
            Main.RefreshProvider();
        }
        else if (!startHidden) Main.ShowPanel();
        else Toast.Show("העוזר פועל ברקע ✦", $"{settings.AssistantHotkey} לפתיחה · {settings.CircleSearchHotkey} להקף וחפש", 5);
    }

    /// <summary>Thread-safe (pure Win32): true when one of this app's windows has focus.</summary>
    private static bool ForegroundIsOurs()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
        return pid == Environment.ProcessId;
    }

    public void ApplyShortcuts(bool showWarnings)
    {
        if (_hotkeys is null) return;
        var s = Controller.Settings;
        var list = new List<(string, Action)>
        {
            (s.AssistantHotkey, () => Main.Toggle()),
            (s.VoiceHotkey, () => _ = Main.ToggleVoiceAsync()),
            (s.CircleSearchHotkey, () => _ = StartCircleSearchAsync()),
            (s.TranslateHotkey, () => _ = StartTranslationAsync()),
        };
        if (s.UseCopilotKey) list.Add(("Win+Shift+F23", () => Main.Toggle()));
        var failed = _hotkeys.RegisterAll(list).Where(f => f != "Win+Shift+F23").ToList();
        if (showWarnings && failed.Count > 0)
            Toast.Show("קיצור מקשים תפוס", $"{string.Join(", ", failed)} כבר בשימוש של תוכנה אחרת. אפשר לבחור קיצור אחר בהגדרות.", 10);
    }

    private void ApplyLongPress()
    {
        if (_longPress is null) return;
        _longPress.ThresholdMs = Controller.Settings.LongPressMilliseconds;
        if (Controller.Settings.TouchpadLongPressEnabled) _longPress.Start(); else _longPress.Stop();
    }

    public Task StartCircleSearchAsync() => RunOverlayAsync(OverlayStartMode.CircleToSearch);
    public Task StartTranslationAsync() => RunOverlayAsync(OverlayStartMode.TranslateScreen);

    private async Task RunOverlayAsync(OverlayStartMode mode)
    {
        if (CircleSearchSession.IsOpen) return;
        var wasVisible = Main.IsVisible;
        if (wasVisible) { Main.Hide(); await Task.Delay(200); }
        var session = new CircleSearchSession
        {
            GetModel = Controller.CreateModel,
            TranslationTarget = Controller.Settings.TranslationTarget,
            AskAssistant = AskAboutImage,
            OpenSettings = OpenSettings,
        };
        try { await session.RunAsync(mode); }
        catch (Exception ex) { Toast.Show("הקף וחפש", "לא הצלחתי להקפיא את המסך: " + ex.Message); }
    }

    private void AskAboutImage(BitmapSource image, string question)
    {
        Main.ShowPanel();
        Main.Attach(image);
        _ = Main.SubmitAsync(string.IsNullOrWhiteSpace(question) ? "מה זה? הסבר לי על מה שסימנתי." : question);
    }

    public void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Called by the settings window after saving.</summary>
    public void OnSettingsChanged()
    {
        SettingsStore.Save(Controller.Settings);
        ApplyShortcuts(showWarnings: true);
        ApplyLongPress();
        AutoStart.Apply(Controller.Settings.StartWithWindows);
        Main.RefreshProvider();
    }

    public void Exit()
    {
        IsExiting = true;
        Dispose();
        Application.Current.Shutdown(0);
    }

    public void Dispose()
    {
        try { _longPress?.Dispose(); } catch { }
        try { _hotkeys?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        _longPress = null; _hotkeys = null; _tray = null;
    }
}
