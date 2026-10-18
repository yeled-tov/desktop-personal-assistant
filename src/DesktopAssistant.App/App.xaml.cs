using System.Windows;
using System.Windows.Threading;
using DesktopAssistant.Agent;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;
using DesktopAssistant.Input;
using DesktopAssistant.Overlay;
using DesktopAssistant.Settings;
using DesktopAssistant.UI;

namespace DesktopAssistant;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Prune();
        AppLog.Info($"Starting v{typeof(App).Assembly.GetName().Version} on Windows {Environment.OSVersion.Version}, args: {string.Join(' ', e.Args)}");
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => AppLog.Error("Unhandled exception (fatal)", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { AppLog.Error("Unobserved task exception", a.Exception); a.SetObserved(); };

        var smoke = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        var minimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        if (!smoke)
        {
            _instance = new SingleInstance();
            if (!_instance.IsFirstInstance)
            {
                AppLog.Info("Already running — asking the running copy to show itself.");
                _instance.SignalFirstInstance();
                Shutdown(0);
                return;
            }
        }

        try
        {
            _host = new AppHost();
            if (smoke) { _ = RunSmokeTestAsync(_host); return; }
            _instance!.ListenForActivation(() => Dispatcher.BeginInvoke(() => _host.Main.ShowPanel()));
            _host.Start(startHidden: minimized && _host.Controller.Settings.StartHidden);
            AppLog.Info("Started.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Startup failed", ex);
            MessageBox.Show($"העוזר לא הצליח להיפתח.\n\n{ex.Message}\n\nפרטים נשמרו בקובץ:\n{AppLog.CurrentLogFile}", "העוזר האישי", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("UI exception", e.Exception);
        e.Handled = true; // keep running — one failed action must not kill the assistant
        try { Toast.Show("משהו השתבש", e.Exception.Message); } catch { }
    }

    /// <summary>
    /// Used by CI on a real Windows machine: starts every subsystem, opens every window, then exits.
    /// Exit code 0 means the app can actually launch and run — the check the previous version never had.
    /// </summary>
    private async Task RunSmokeTestAsync(AppHost host)
    {
        var marker = Path.Combine(AppLog.DataDirectory, "smoke-result.txt");
        var steps = new List<string>();
        void Step(string name) { steps.Add(name); AppLog.Info("SMOKE step: " + name); }
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        watchdog.Tick += (_, _) => { File.WriteAllText(marker, "TIMEOUT after: " + string.Join(" > ", steps)); Shutdown(2); };
        watchdog.Start();
        try
        {
            Directory.CreateDirectory(AppLog.DataDirectory);
            host.Start(startHidden: false, firstRunUi: false); Step("host started (tray, hotkeys, mouse hook)");
            host.Main.ShowPanel(); await Task.Delay(400); Step("assistant panel shown");

            foreach (var tool in DesktopTools.All) GeminiModel.ConvertSchema(tool.Schema);
            Step($"{DesktopTools.All.Count} tool schemas valid");
            foreach (var provider in host.Controller.Settings.Providers) ChatModelFactory.Create(provider);
            Step("all providers constructible");
            if (!Hotkey.TryParse("Ctrl+Alt+Space", out _)) throw new InvalidOperationException("hotkey parser");
            Step("hotkey parser");

            var welcome = new WelcomeWindow(host); welcome.Show(); await Task.Delay(300); welcome.Close(); Step("welcome window");
            host.OpenSettings(); await Task.Delay(500);
            foreach (var w in Windows.OfType<SettingsWindow>().ToList()) w.Close();
            Step("settings window");

            var apps = await AppLauncher.GetInstalledAppsAsync();
            Step($"installed apps enumerated ({apps.Count})");
            var windows = WindowManager.List(); Step($"windows enumerated ({windows.Count})");
            var shot = ScreenCapture.CaptureForAgent(); Step($"screenshot {shot.Width}x{shot.Height}");

            var session = host.Main.Dispatcher.InvokeAsync(() => host.StartCircleSearchAsync()).Task.Unwrap();
            await Task.Delay(1500);
            var overlays = Windows.OfType<Window>().Count(w => w.GetType().Name == "CircleSearchWindow");
            if (overlays == 0) throw new InvalidOperationException("Circle-to-Search overlay did not open");
            foreach (var w in Windows.OfType<Window>().Where(w => w.GetType().Name == "CircleSearchWindow").ToList()) w.Close();
            await session.WaitAsync(TimeSpan.FromSeconds(5));
            Step($"circle-to-search overlay ({overlays} monitor(s))");

            var pill = new StatusPill(); pill.SetText("בדיקה"); pill.Show(); await Task.Delay(200); pill.Close(); Step("status pill");
            Toast.Show("בדיקה", "smoke test", 1); Step("toast");

            File.WriteAllText(marker, "SMOKE OK\n" + string.Join("\n", steps));
            AppLog.Info("SMOKE OK");
            host.Exit();
        }
        catch (Exception ex)
        {
            AppLog.Error("SMOKE FAILED", ex);
            File.WriteAllText(marker, $"SMOKE FAILED at step {steps.Count + 1}: {ex}\nDone: {string.Join(" > ", steps)}");
            host.Dispose();
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _host?.Dispose(); } catch { }
        _instance?.Dispose();
        AppLog.Info($"Exit ({e.ApplicationExitCode}).");
        base.OnExit(e);
    }
}
