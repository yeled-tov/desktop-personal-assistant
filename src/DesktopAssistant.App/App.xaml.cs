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
            if (smoke) { _ = RunSmokeTestAsync(_host, e.Args); return; }
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
    private async Task RunSmokeTestAsync(AppHost host, string[] args)
    {
        var marker = Path.Combine(AppLog.DataDirectory, "smoke-result.txt");
        var steps = new List<string>();
        void Step(string name) { steps.Add(name); AppLog.Info("SMOKE step: " + name); }
        var ask = args.FirstOrDefault(a => a.StartsWith("--smoke-ask=", StringComparison.Ordinal))?["--smoke-ask=".Length..];
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ask is null ? 90 : 200) };
        watchdog.Tick += (_, _) => { File.WriteAllText(marker, "TIMEOUT after: " + string.Join(" > ", steps)); Shutdown(2); };
        watchdog.Start();
        try
        {
            Directory.CreateDirectory(AppLog.DataDirectory);
            host.Start(startHidden: false, firstRunUi: false); Step("host started (tray, hotkeys, mouse hook)");
            host.Main.ShowPanel(); await Task.Delay(400); Snap(host.Main, "main"); Step("assistant panel shown");

            foreach (var tool in DesktopTools.All) GeminiModel.ConvertSchema(tool.Schema);
            Step($"{DesktopTools.All.Count} tool schemas valid");
            foreach (var provider in host.Controller.Settings.Providers) ChatModelFactory.Create(provider);
            Step("all providers constructible");
            if (!Hotkey.TryParse("Ctrl+Alt+Space", out _)) throw new InvalidOperationException("hotkey parser");
            Step("hotkey parser");

            var welcome = new WelcomeWindow(host); welcome.Show(); await Task.Delay(300); Snap(welcome, "welcome"); welcome.Close(); Step("welcome window");
            host.OpenSettings(); await Task.Delay(500);
            foreach (var w in Windows.OfType<SettingsWindow>().ToList()) { Snap(w, "settings"); w.Close(); }
            Step("settings window");

            var apps = await AppLauncher.GetInstalledAppsAsync();
            Step($"installed apps enumerated ({apps.Count})");
            var windows = WindowManager.List(); Step($"windows enumerated ({windows.Count})");
            var shot = ScreenCapture.CaptureForAgent(); Step($"screenshot {shot.Width}x{shot.Height}");

            var session = host.Main.Dispatcher.InvokeAsync(() => host.StartCircleSearchAsync()).Task.Unwrap();
            var overlays = 0;
            for (var i = 0; i < 40 && overlays == 0; i++)
            {
                await Task.Delay(250);
                overlays = Windows.OfType<Window>().Count(w => w.GetType().Name == "CircleSearchWindow");
                if (session.IsFaulted) throw session.Exception!.GetBaseException();
            }
            if (overlays == 0) throw new InvalidOperationException("Circle-to-Search overlay did not open");
            var overlay = Windows.OfType<CircleSearchWindow>().First();
            await Task.Delay(400);
            overlay.DemoSelection(); await Task.Delay(600); Snap(overlay, "overlay-selection");
            overlay.DemoTranslation(); await Task.Delay(300); Snap(overlay, "overlay-translation");
            foreach (var w in Windows.OfType<CircleSearchWindow>().ToList()) w.Close();
            await session.WaitAsync(TimeSpan.FromSeconds(5));
            Step($"circle-to-search overlay ({overlays} monitor(s))");

            var pill = new StatusPill(); pill.SetText("בדיקה"); pill.Show(); await Task.Delay(200); pill.Close(); Step("status pill");
            Toast.Show("בדיקה", "smoke test", 1); Step("toast");

            if (ask is not null)
            {
                host.Main.ShowPanel();
                await host.Main.SubmitAsync(ask);
                await Task.Delay(500);
                Snap(host.Main, "main-after-ask");
                Step("agent request: " + ask);
            }

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

    /// <summary>Saves a picture of a window next to the logs (lets CI and developers see the UI).</summary>
    private static void Snap(Window window, string name)
    {
        try
        {
            if (window.Content is not System.Windows.Media.Visual visual || window.ActualWidth < 1) return;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            System.Windows.Media.Imaging.BitmapSource image = bitmap;
            if (window.FlowDirection == FlowDirection.RightToLeft)
            {
                // Rendering an RTL root comes out mirrored; flip the pixels back.
                var stride = bitmap.PixelWidth * 4;
                var pixels = new byte[stride * bitmap.PixelHeight];
                bitmap.CopyPixels(pixels, stride, 0);
                var flipped = new byte[pixels.Length];
                for (var y = 0; y < bitmap.PixelHeight; y++)
                    for (var x = 0; x < bitmap.PixelWidth; x++)
                        Buffer.BlockCopy(pixels, y * stride + x * 4, flipped, y * stride + (bitmap.PixelWidth - 1 - x) * 4, 4);
                image = System.Windows.Media.Imaging.BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, flipped, stride);
            }
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(AppLog.DataDirectory, $"smoke-{name}.png"));
            encoder.Save(file);
        }
        catch (Exception ex) { AppLog.Warn($"Snapshot {name} failed: {ex.Message}"); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _host?.Dispose(); } catch { }
        _instance?.Dispose();
        AppLog.Info($"Exit ({e.ApplicationExitCode}).");
        base.OnExit(e);
    }
}
