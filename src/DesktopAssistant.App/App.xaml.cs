using System.Windows;
using System.Windows.Threading;
using DesktopAssistant.Infrastructure;

namespace DesktopAssistant;

public partial class App : System.Windows.Application
{
    public static AppServices Services { get; private set; } = null!;
    public static bool IsExiting { get; private set; }

    public static void ExitApplication()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var dlog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAssistant", "diag.log");
        Directory.CreateDirectory(Path.GetDirectoryName(dlog)!);
        void L(string m) { try { File.AppendAllText(dlog, DateTime.Now.ToString("HH:mm:ss.fff ") + m + Environment.NewLine); } catch { } }
        AppDomain.CurrentDomain.FirstChanceException += (_, a) => L("FIRST-CHANCE: " + a.Exception.GetType().FullName + ": " + a.Exception.Message + "\n" + Environment.StackTrace);
        AppDomain.CurrentDomain.UnhandledException += (_, a) => L("UNHANDLED: " + a.ExceptionObject);
        L("OnStartup");
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            L("services"); Services = new AppServices(); L("window");
            var window = new MainWindow();
            MainWindow = window;
            window.Show(); L("shown");
        }
        catch (Exception ex)
        {
            ShowStartupError(ex);
            Shutdown(-1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowStartupError(e.Exception);
        e.Handled = true;
        Current.Shutdown(-1);
    }

    private static void ShowStartupError(Exception exception)
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAssistant", "startup.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] Startup failure{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }

        try
        {
            MessageBox.Show($"עוזר שולחן העבודה לא הצליח להיפתח.\n\nנשמר דוח אבחון כאן:\n{logPath}\n\nפרטים: {exception.Message}",
                "שגיאה בהפעלת העוזר", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK,
                MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
        }
        catch { }
    }
}
