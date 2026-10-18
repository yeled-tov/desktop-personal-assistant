using DesktopAssistant.Core;
using Microsoft.Win32;

namespace DesktopAssistant.Settings;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopAssistant";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            var exe = Environment.ProcessPath;
            if (enabled && !string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\" --minimized");
            else if (!enabled) key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { AppLog.Error("Updating auto-start failed", ex); }
    }
}
