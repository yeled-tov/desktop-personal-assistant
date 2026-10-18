namespace DesktopAssistant.Core;

/// <summary>Small, thread-safe file logger. Never throws.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAssistant");
    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");
    public static string CurrentLogFile => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(CurrentLogFile, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }

    /// <summary>Deletes log files older than two weeks.</summary>
    public static void Prune()
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "app-*.log"))
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-14)) File.Delete(file);
        }
        catch { }
    }

    /// <summary>Runs a callback coming from native code. An exception escaping a Win32 callback
    /// terminates the process on recent Windows builds, so every native entry point goes through here.</summary>
    public static void Guard(string where, Action action)
    {
        try { action(); }
        catch (Exception ex) { Error($"Exception in {where}", ex); }
    }
}
