using System.Drawing;
using DesktopAssistant.Core;
using Forms = System.Windows.Forms;

namespace DesktopAssistant.UI;

/// <summary>The icon next to the clock — the app keeps running there in the background.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(AppHost host)
    {
        _icon = new Forms.NotifyIcon { Text = "העוזר האישי", Icon = LoadIcon(), Visible = false };
        var menu = new Forms.ContextMenuStrip { RightToLeft = Forms.RightToLeft.Yes, ShowImageMargin = false };
        void Item(string text, Action action) => menu.Items.Add(text, null, (_, _) => AppLog.Guard("tray menu", action));
        Item("✦ פתח את העוזר", () => host.Main.ShowPanel());
        Item("🎙 דבר עם העוזר", () => _ = host.Main.ToggleVoiceAsync());
        Item("◎ הקף וחפש", () => _ = host.StartCircleSearchAsync());
        Item("🌐 תרגם את המסך", () => _ = host.StartTranslationAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        Item("⚙ הגדרות", host.OpenSettings);
        Item("📄 פתח את תיקיית הלוגים", () => { Directory.CreateDirectory(AppLog.LogDirectory); Desktop.AppLauncher.ShellOpen(AppLog.LogDirectory, null); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        Item("יציאה", host.Exit);
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) AppLog.Guard("tray click", () => host.Main.Toggle()); };
        _icon.Visible = true;
    }

    private static Icon LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            if (resource is not null) return new Icon(resource.Stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex) { AppLog.Warn("Tray icon resource missing: " + ex.Message); }
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(file)) return new Icon(file);
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
