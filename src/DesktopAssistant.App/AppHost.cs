using System.Windows;
using System.Windows.Media.Imaging;
using DesktopAssistant.Agent;
using DesktopAssistant.Core;
using DesktopAssistant.Input;
using DesktopAssistant.Offline;
using DesktopAssistant.Overlay;
using DesktopAssistant.Settings;
using DesktopAssistant.UI;
using DesktopAssistant.Voice;

namespace DesktopAssistant;

/// <summary>Composition root: owns every long-lived piece and wires shortcuts to features.</summary>
public sealed class AppHost : IDisposable
{
    private HotkeyManager? _hotkeys;
    private LongPressDetector? _longPress;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private WakeWordListener? _wake;

    public AssistantController Controller { get; }
    public OfflineManager Offline => Controller.Offline;
    public SpeechService Speech { get; }
    public MainWindow Main { get; }
    public bool IsExiting { get; private set; }

    public AppHost()
    {
        var settings = SettingsStore.Load();
        Controller = new AssistantController(settings);
        Speech = new SpeechService(Controller, Controller.Offline);
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
        _wake = new WakeWordListener(
            (wav, hint, token) => Speech.TranscribeForWakeWordAsync(wav, hint, token),
            () => Main.IsRecording || Main.IsBusy || Main.IsSpeaking || CircleSearchSession.IsOpen,
            () => !Controller.Settings.WakeOnlyWhenCharging || IsOnAcPower(),
            command => Application.Current.Dispatcher.BeginInvoke(() => OnWakeWord(command)));
        ApplyWakeWord();
        AutoStart.Apply(settings.StartWithWindows);

        if (firstRunUi && !settings.OnboardingDone)
        {
            Main.ShowPanel();
            var welcome = new WelcomeWindow(this) { Owner = Main };
            welcome.ShowDialog();
            settings.OnboardingDone = true;
            SettingsStore.Save(settings);
            ApplyWakeWord(); // a key may have just been added
            Main.RefreshProvider();
        }
        else if (!startHidden) Main.ShowPanel();
        else Toast.Show($"{settings.AssistantName} פועלת ברקע ✦", WakeWordActive ? $"אמרו \"{settings.WakePhrase}\" או {settings.AssistantHotkey}" : $"{settings.AssistantHotkey} לפתיחה · {settings.DictationHotkey} להכתבה", 5);
    }

    public bool WakeWordActive => _wake?.IsRunning == true;

    /// <summary>Turns wake-word listening on/off. It needs the offline speech pack — it never uses the internet or an API quota.</summary>
    public void ApplyWakeWord()
    {
        if (_wake is null) return;
        var s = Controller.Settings;
        _wake.Phrase = s.WakePhrase;
        var shouldRun = s.WakeWordEnabled && Offline.SpeechReady;
        if (shouldRun && !_wake.IsRunning) _wake.Start();
        else if (!shouldRun && _wake.IsRunning) _wake.Stop();
        Offline.KeepSpeechLoaded(shouldRun);
        Main.RefreshProvider();
    }

    public string WakeWordStatus => _wake?.IsRunning == true ? _wake.Status : "כבוי";

    private static bool IsOnAcPower()
    {
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        return power.PowerLineStatus != System.Windows.Forms.PowerLineStatus.Offline; // desktops report Online/Unknown
    }

    private void OnWakeWord(string command)
    {
        Main.ShowPanel();
        Chime.Play();
        if (command.Trim().Length >= 2) _ = Main.SubmitAsync(command.Trim()); // "היי שיר, פתח וורד" — do it right away
        else _ = Main.ToggleVoiceAsync();                                     // "היי שיר" — then listen for the request
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
            (s.DictationHotkey, () => _ = Main.DictateAsync()),
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
        catch (Exception ex)
        {
            AppLog.Error("Overlay failed", ex);
            Toast.Show("הקף וחפש", "לא הצלחתי להקפיא את המסך: " + ex.Message);
        }
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
        ApplyWakeWord();
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
        try { _wake?.Dispose(); } catch { }
        try { Offline.Dispose(); } catch { }
        try { _longPress?.Dispose(); } catch { }
        try { _hotkeys?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        _wake = null; _longPress = null; _hotkeys = null; _tray = null;
    }
}
