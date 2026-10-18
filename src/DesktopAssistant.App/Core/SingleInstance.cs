namespace DesktopAssistant.Core;

/// <summary>
/// Makes sure only one copy runs. A second launch (desktop shortcut, Start menu, installer "run")
/// signals the running copy to show itself instead of silently starting a hidden duplicate.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\DesktopAssistant.SingleInstance.v2";
    private const string EventName = @"Local\DesktopAssistant.Activate.v2";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _registration;

    public bool IsFirstInstance { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
    }

    public void SignalFirstInstance()
    {
        try { Native.AllowSetForegroundWindow(-1); _activate.Set(); } catch (Exception ex) { AppLog.Error("Could not signal running instance", ex); }
    }

    public void ListenForActivation(Action onActivate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => AppLog.Guard("activation signal", onActivate), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        if (IsFirstInstance) { try { _mutex.ReleaseMutex(); } catch { } }
        _mutex.Dispose();
        _activate.Dispose();
    }
}
