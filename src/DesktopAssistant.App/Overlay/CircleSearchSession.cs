using System.Windows;
using System.Windows.Media.Imaging;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;

namespace DesktopAssistant.Overlay;

public enum OverlayStartMode { CircleToSearch, TranslateScreen }

/// <summary>
/// "Circle to Search" for Windows: freezes every monitor, lets the user circle anything, then searches,
/// explains, translates or copies it. Also hosts full-screen in-place translation.
/// </summary>
public sealed class CircleSearchSession
{
    private readonly List<CircleSearchWindow> _windows = [];
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static bool IsOpen { get; private set; }

    public required Func<IChatModel?> GetModel { get; init; }
    public required string TranslationTarget { get; init; }
    /// <summary>Sends the circled image to the main assistant chat with an optional question.</summary>
    public required Action<BitmapSource, string> AskAssistant { get; init; }
    public required Action OpenSettings { get; init; }

    public async Task RunAsync(OverlayStartMode mode)
    {
        if (IsOpen) return;
        IsOpen = true;
        try
        {
            var shots = await Task.Run(ScreenCapture.CaptureMonitors);
            AppLog.Info($"Overlay: captured {shots.Count} monitor(s), mode {mode}");
            foreach (var shot in shots)
            {
                var window = new CircleSearchWindow(this, shot);
                _windows.Add(window);
                window.Closed += (_, _) => CloseAll();
                window.Show();
            }
            var cursor = System.Windows.Forms.Cursor.Position;
            var active = _windows.FirstOrDefault(w => w.Contains(cursor.X, cursor.Y)) ?? _windows.First();
            active.Activate();
            var handle = new System.Windows.Interop.WindowInteropHelper(active).Handle;
            if (Native.GetForegroundWindow() != handle) await Task.Run(() => WindowManager.Focus(handle));
            active.Activate();
            active.Focus();
            if (mode == OverlayStartMode.TranslateScreen)
                foreach (var w in _windows) _ = w.TranslateWholeScreenAsync();
            await _closed.Task;
        }
        catch (Exception ex)
        {
            AppLog.Error("Circle to Search failed", ex);
            CloseAll();
            throw;
        }
        finally { IsOpen = false; }
    }

    public void CloseAll()
    {
        foreach (var window in _windows.ToList())
        {
            _windows.Remove(window);
            try { if (window.IsLoaded) window.Close(); } catch { }
        }
        _closed.TrySetResult();
    }

    /// <summary>Called by a window when the user starts selecting there, so other monitors drop old results.</summary>
    internal void OnSelectionStarted(CircleSearchWindow source)
    {
        foreach (var window in _windows.Where(w => w != source)) window.ClearSelection();
    }
}
