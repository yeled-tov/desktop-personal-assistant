using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using DesktopAssistant.Core;

namespace DesktopAssistant.Offline;

/// <summary>
/// Runs a local engine (llama-server / whisper-server) as a hidden child process on 127.0.0.1.
/// The process is tied to a Windows Job object, so it can never outlive the assistant, and it is
/// stopped automatically after a period without use to give the memory back.
/// </summary>
public sealed class LocalServer : IDisposable
{
    private readonly string _name;
    private readonly string _exe;
    private readonly Func<int, string> _arguments;
    private readonly string _healthPath;
    private readonly TimeSpan _idleTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Timer? _idleTimer;
    private DateTime _lastUse;

    public int Port { get; private set; }
    public bool IsRunning => _process is { HasExited: false };
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    /// <summary>When true the server is never stopped for idleness (e.g. while the wake word is listening).</summary>
    public bool KeepAlive { get; set; }

    public LocalServer(string name, string exe, Func<int, string> arguments, string healthPath, TimeSpan idleTimeout)
    {
        _name = name; _exe = exe; _arguments = arguments; _healthPath = healthPath; _idleTimeout = idleTimeout;
    }

    public async Task EnsureStartedAsync(TimeSpan startupTimeout, CancellationToken cancellationToken)
    {
        _lastUse = DateTime.UtcNow;
        if (IsRunning) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning) return;
            if (!File.Exists(_exe)) throw new InvalidOperationException($"המנוע המקומי ({_name}) לא מותקן. הגדרות ← אופליין.");
            Port = FreePort();
            var info = new ProcessStartInfo(_exe, _arguments(Port))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(_exe)!,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            AppLog.Info($"Starting {_name}: {Path.GetFileName(_exe)} {info.Arguments}");
            var process = Process.Start(info) ?? throw new InvalidOperationException($"לא הצלחתי להפעיל את {_name}.");
            var tail = new Queue<string>();
            void Capture(string? line) { if (line is null) return; lock (tail) { tail.Enqueue(line); if (tail.Count > 30) tail.Dequeue(); } }
            process.OutputDataReceived += (_, e) => Capture(e.Data);
            process.ErrorDataReceived += (_, e) => Capture(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            JobObject.Assign(process);
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { } // keep the PC responsive while it thinks
            _process = process;

            var until = DateTime.UtcNow + startupTimeout;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    string log; lock (tail) log = string.Join("\n", tail.TakeLast(8));
                    AppLog.Error($"{_name} exited ({process.ExitCode}):\n{log}");
                    _process = null;
                    throw new InvalidOperationException($"המנוע המקומי ({_name}) נסגר בזמן הטעינה. ייתכן שאין מספיק זיכרון פנוי (RAM). פרטים בלוג.");
                }
                try
                {
                    using var response = await http.GetAsync(BaseUrl + _healthPath, cancellationToken);
                    if (response.IsSuccessStatusCode) break;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                if (DateTime.UtcNow > until) { Stop(); throw new InvalidOperationException($"המנוע המקומי ({_name}) לא עלה בזמן."); }
                await Task.Delay(400, cancellationToken);
            }
            AppLog.Info($"{_name} ready on port {Port}");
            _idleTimer ??= new Timer(_ => { if (!KeepAlive && IsRunning && DateTime.UtcNow - _lastUse > _idleTimeout) { AppLog.Info($"{_name} idle — stopping to free memory"); Stop(); } }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
        finally { _gate.Release(); }
    }

    public void Touch() => _lastUse = DateTime.UtcNow;

    public void Stop()
    {
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        _idleTimer?.Dispose();
        Stop();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>Kill-on-close job: child engines die together with the assistant, even if it crashes.</summary>
internal static class JobObject
{
    private static readonly nint Handle = Create();

    public static void Assign(Process process)
    {
        try { if (Handle != 0) AssignProcessToJobObject(Handle, process.Handle); } catch (Exception ex) { AppLog.Warn("Job object: " + ex.Message); }
    }

    private static nint Create()
    {
        try
        {
            var job = CreateJobObject(0, null);
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE
            var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                SetInformationJobObject(job, 9, ptr, (uint)size); // JobObjectExtendedLimitInformation
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return job;
        }
        catch { return 0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION { public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags; public nuint MinimumWorkingSetSize; public nuint MaximumWorkingSetSize; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass; public uint SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION { public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo; public nuint ProcessMemoryLimit; public nuint JobMemoryLimit; public nuint PeakProcessMemoryUsed; public nuint PeakJobMemoryUsed; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateJobObject(nint attributes, string? name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(nint job, int infoClass, nint info, uint length);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(nint job, nint process);
}
