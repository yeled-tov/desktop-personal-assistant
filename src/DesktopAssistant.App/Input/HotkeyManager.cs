using System.Windows.Interop;
using DesktopAssistant.Core;
using static DesktopAssistant.Core.Native;

namespace DesktopAssistant.Input;

/// <summary>System-wide shortcuts, registered on a hidden message-only window (independent of the chat window).</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 0xB000;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("DesktopAssistantHotkeys") { ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0 }; // HWND_MESSAGE
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers every shortcut. Returns the ones Windows refused (already taken by another app).</summary>
    public IReadOnlyList<string> RegisterAll(IEnumerable<(string Text, Action Action)> shortcuts)
    {
        UnregisterAll();
        var failed = new List<string>();
        foreach (var (text, action) in shortcuts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!Hotkey.TryParse(text, out var hotkey)) { failed.Add(text); continue; }
            var id = _nextId++;
            if (RegisterHotKey(_source.Handle, id, hotkey.Modifiers | MOD_NOREPEAT, hotkey.Key)) _actions[id] = action;
            else { failed.Add(text); AppLog.Warn($"Hotkey {text} is taken (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})"); }
        }
        return failed;
    }

    private void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            // Run outside the Win32 callback; never let an exception cross back into user32.
            _source.Dispatcher.BeginInvoke(() => AppLog.Guard("hotkey", action));
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
