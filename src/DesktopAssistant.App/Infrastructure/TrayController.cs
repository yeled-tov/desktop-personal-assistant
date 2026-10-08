using Forms = System.Windows.Forms;
using Window = System.Windows.Window;

namespace DesktopAssistant.Infrastructure;

public sealed class TrayController(Window window, Action translate)
{
    private readonly Forms.NotifyIcon _icon = new() { Text = "עוזר אישי למחשב", Icon = LoadIcon(), Visible = false };
    private static System.Drawing.Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "app.ico");
        return File.Exists(path) ? new System.Drawing.Icon(path) : System.Drawing.SystemIcons.Information;
    }
    public void Show()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("פתח עוזר", null, (_, _) => { window.Show(); window.WindowState = System.Windows.WindowState.Normal; window.Activate(); });
        menu.Items.Add("תרגם מסך", null, (_, _) => window.Dispatcher.Invoke(translate));
        menu.Items.Add("יציאה", null, (_, _) => { _icon.Visible = false; App.ExitApplication(); });
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => { window.Show(); window.WindowState = System.Windows.WindowState.Normal; window.Activate(); };
        _icon.Visible = true;
    }
}
