using System.Runtime.InteropServices;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;
using static DesktopAssistant.Core.Native;

namespace DesktopAssistant.Input;

/// <summary>
/// "Press and hold" on the touchpad (or mouse) without moving => opens Circle-to-Search, like a long press on the phone's nav bar.
///
/// How it stays safe for normal clicking: the button-down is held back for a moment. If the button is released
/// or the pointer moves before the threshold, the held-back press is replayed instantly, so clicks, double-clicks,
/// drags and text selection keep working. Only a still press longer than the threshold is consumed.
/// The hook runs on its own thread so a busy UI can never make Windows drop it.
/// </summary>
public sealed class LongPressDetector : IDisposable
{
    private const int MoveTolerance = 7;
    private const long TouchSignatureMask = 0xFFFFFF00, TouchSignature = 0xFF515700;

    private readonly Action _onLongPress;
    private readonly Func<bool> _isSuspended;
    private readonly object _gate = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private LowLevelMouseProc? _proc; // keep the delegate alive
    private Timer? _timer;

    private enum State { Idle, Pending, Triggered }
    private State _state = State.Idle;
    private POINT _downPoint;
    private int _generation;

    public int ThresholdMs { get; set; } = 800;
    public bool IsRunning => _hook != 0;

    public LongPressDetector(Action onLongPress, Func<bool> isSuspended)
    {
        _onLongPress = onLongPress;
        _isSuspended = isSuspended;
    }

    public void Start()
    {
        if (_thread is not null) return;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _proc = HookProc;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == 0) AppLog.Error($"Mouse hook failed (error {Marshal.GetLastWin32Error()})");
            ready.Set();
            while (GetMessage(out _, 0, 0, 0) > 0) { }
            if (_hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
        }) { IsBackground = true, Name = "Long-press hook", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait(3000);
    }

    public void Stop()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(2000);
        _thread = null;
        _timer?.Dispose();
        _timer = null;
        lock (_gate) _state = State.Idle;
    }

    private nint HookProc(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0 && Handle((int)wParam, Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam))) return 1;
        }
        catch (Exception ex) { AppLog.Error("Long-press hook", ex); lock (_gate) _state = State.Idle; }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <returns>true to swallow the event.</returns>
    private bool Handle(int message, MSLLHOOKSTRUCT data)
    {
        if ((data.flags & LLMHF_INJECTED) != 0) return false;                            // our own or other software's injected input
        if ((data.dwExtraInfo.ToInt64() & TouchSignatureMask) == TouchSignature) return false; // touchscreen / pen: Windows already maps hold => right click

        lock (_gate)
        {
            switch (message)
            {
                case WM_LBUTTONDOWN:
                    if (_state != State.Idle || _isSuspended() || AnyModifierDown()) return false;
                    _state = State.Pending;
                    _downPoint = data.pt;
                    var generation = ++_generation;
                    _timer?.Dispose();
                    _timer = new Timer(_ => OnTimer(generation), null, ThresholdMs, Timeout.Infinite);
                    return true; // hold the press back

                case WM_MOUSEMOVE when _state == State.Pending:
                    if (Math.Abs(data.pt.X - _downPoint.X) <= MoveTolerance && Math.Abs(data.pt.Y - _downPoint.Y) <= MoveTolerance) return false;
                    _state = State.Idle; _generation++;
                    // Replay the press where it happened, then continue the drag from here.
                    InputSimulator.SendLeftDownThenMove(_downPoint.X, _downPoint.Y, data.pt.X, data.pt.Y);
                    return true;

                case WM_LBUTTONUP when _state == State.Pending:
                    _state = State.Idle; _generation++;
                    InputSimulator.SendLeftClickAt(_downPoint.X, _downPoint.Y);
                    return true;

                case WM_LBUTTONUP when _state == State.Triggered:
                    _state = State.Idle;
                    return true; // the app never saw the press, so it must not see the release either

                case WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_MOUSEWHEEL when _state == State.Pending:
                    _state = State.Idle; _generation++;
                    InputSimulator.SendLeftDownAt(_downPoint.X, _downPoint.Y);
                    return false;
            }
        }
        return false;
    }

    private void OnTimer(int generation)
    {
        lock (_gate)
        {
            if (_state != State.Pending || generation != _generation) return;
            _state = State.Triggered;
        }
        AppLog.Info("Long press detected");
        AppLog.Guard("long-press action", _onLongPress);
        // Safety net: if the release never arrives (e.g. focus change), don't stay stuck.
        _ = Task.Delay(15000).ContinueWith(_ => { lock (_gate) { if (_state == State.Triggered && _generation == generation) _state = State.Idle; } });
    }

    private static bool AnyModifierDown() =>
        (GetAsyncKeyState(0x10) & 0x8000) != 0 || (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x12) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

    public void Dispose() => Stop();
}
