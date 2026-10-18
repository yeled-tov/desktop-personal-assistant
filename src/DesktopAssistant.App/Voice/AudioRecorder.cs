using System.Runtime.InteropServices;
using DesktopAssistant.Core;

namespace DesktopAssistant.Voice;

/// <summary>
/// Microphone capture (16 kHz mono PCM) through the classic waveIn API — no extra packages.
/// Stops automatically after the speaker goes quiet.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private const int SampleRate = 16000, BufferMs = 50, BufferCount = 8;
    private const int BufferBytes = SampleRate * 2 * BufferMs / 1000;

    public event Action<double>? LevelChanged;

    public async Task<byte[]> RecordAsync(TimeSpan maxDuration, TimeSpan silenceToStop, CancellationToken cancellationToken, CancellationToken stopToken)
    {
        var format = new WaveFormat { wFormatTag = 1, nChannels = 1, nSamplesPerSec = SampleRate, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = SampleRate * 2, cbSize = 0 };
        var result = waveInOpen(out var device, WAVE_MAPPER, ref format, 0, 0, 0);
        if (result != 0) throw new InvalidOperationException(result == 2 ? "לא נמצא מיקרופון. חבר מיקרופון ונסה שוב." : $"לא ניתן לפתוח את המיקרופון (שגיאה {result}). בדוק בהגדרות הפרטיות של Windows שהגישה למיקרופון מותרת לאפליקציות שולחן עבודה.");

        var headerSize = Marshal.SizeOf<WaveHeader>();
        var flagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.dwFlags));
        var recordedOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.dwBytesRecorded));
        var headers = new nint[BufferCount];
        var data = new nint[BufferCount];
        var pcm = new MemoryStream();
        try
        {
            for (var i = 0; i < BufferCount; i++)
            {
                data[i] = Marshal.AllocHGlobal(BufferBytes);
                headers[i] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader { lpData = data[i], dwBufferLength = BufferBytes }, headers[i], false);
                Check(waveInPrepareHeader(device, headers[i], headerSize));
                Check(waveInAddBuffer(device, headers[i], headerSize));
            }
            Check(waveInStart(device));

            var started = DateTime.UtcNow;
            var lastVoice = DateTime.UtcNow;
            var heardVoice = false;
            var noiseFloor = 0.0;
            var next = 0;
            var buffer = new byte[BufferBytes];
            while (!cancellationToken.IsCancellationRequested && !stopToken.IsCancellationRequested)
            {
                if ((Marshal.ReadInt32(headers[next], flagsOffset) & WHDR_DONE) == 0) { await Task.Delay(10, CancellationToken.None); continue; }
                var recorded = Marshal.ReadInt32(headers[next], recordedOffset);
                Marshal.Copy(data[next], buffer, 0, recorded);
                pcm.Write(buffer, 0, recorded);

                var level = Rms(buffer, recorded);
                LevelChanged?.Invoke(Math.Min(1, level * 6));
                var elapsed = DateTime.UtcNow - started;
                if (elapsed < TimeSpan.FromMilliseconds(300)) noiseFloor = Math.Max(noiseFloor, level);
                var threshold = Math.Max(0.012, noiseFloor * 2.2);
                if (level > threshold) { heardVoice = true; lastVoice = DateTime.UtcNow; }

                Check(waveInAddBuffer(device, headers[next], headerSize));
                next = (next + 1) % BufferCount;

                if (elapsed > maxDuration) break;
                if (heardVoice && DateTime.UtcNow - lastVoice > silenceToStop) break;
                if (!heardVoice && elapsed > TimeSpan.FromSeconds(8)) break; // nobody spoke
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!heardVoice) return [];
        }
        finally
        {
            waveInStop(device);
            waveInReset(device);
            for (var i = 0; i < BufferCount; i++)
            {
                if (headers[i] != 0) { waveInUnprepareHeader(device, headers[i], headerSize); Marshal.FreeHGlobal(headers[i]); }
                if (data[i] != 0) Marshal.FreeHGlobal(data[i]);
            }
            waveInClose(device);
        }
        return ToWav(pcm.ToArray());
    }

    private static double Rms(byte[] buffer, int count)
    {
        double sum = 0; var samples = count / 2;
        if (samples == 0) return 0;
        for (var i = 0; i + 1 < count; i += 2) { var s = BitConverter.ToInt16(buffer, i) / 32768.0; sum += s * s; }
        return Math.Sqrt(sum / samples);
    }

    public static byte[] ToWav(byte[] pcm)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + pcm.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(pcm.Length); w.Write(pcm);
        w.Flush();
        return stream.ToArray();
    }

    private static void Check(int result) { if (result != 0) throw new InvalidOperationException($"שגיאת מיקרופון ({result})."); }

    public void Dispose() { }

    private const uint WAVE_MAPPER = unchecked((uint)-1);
    private const int WHDR_DONE = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat { public ushort wFormatTag; public ushort nChannels; public int nSamplesPerSec; public int nAvgBytesPerSec; public ushort nBlockAlign; public ushort wBitsPerSample; public ushort cbSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader { public nint lpData; public int dwBufferLength; public int dwBytesRecorded; public nint dwUser; public int dwFlags; public int dwLoops; public nint lpNext; public nint reserved; }

    [DllImport("winmm.dll")] private static extern int waveInOpen(out nint device, uint deviceId, ref WaveFormat format, nint callback, nint instance, int flags);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(nint device, nint header, int size);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(nint device, nint header, int size);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(nint device, nint header, int size);
    [DllImport("winmm.dll")] private static extern int waveInStart(nint device);
    [DllImport("winmm.dll")] private static extern int waveInStop(nint device);
    [DllImport("winmm.dll")] private static extern int waveInReset(nint device);
    [DllImport("winmm.dll")] private static extern int waveInClose(nint device);
}
