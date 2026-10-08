using System.Windows;
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
        Services = new AppServices();
        base.OnStartup(e);
    }
}
