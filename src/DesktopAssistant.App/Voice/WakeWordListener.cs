using DesktopAssistant.Core;

namespace DesktopAssistant.Voice;

/// <summary>
/// "היי שיר" — listening for the wake phrase, 100% on this computer.
///
/// A wake word can only be heard with the microphone open — that is how every voice assistant works.
/// To keep it light: the audio is checked locally for "is someone speaking" (a few hundred calculations
/// per second, practically free), and only short utterances (up to ~6 s) go to the LOCAL speech engine to
/// check for the phrase. Nothing is ever sent to the internet or uses an API quota. It pauses by itself on
/// battery power (configurable), and stops completely when turned off.
/// </summary>
public sealed class WakeWordListener : IDisposable
{
    // Frames are 100 ms.
    private const int PreRollFrames = 3;          // keep 300 ms before speech starts
    private const int StartFrames = 2;            // 200 ms of voice to start a segment
    private const int EndSilenceFrames = 7;       // 700 ms of quiet ends it
    private const int MinSpeechFrames = 3;        // ignore clicks/coughs shorter than 300 ms
    private const int MaxSegmentFrames = 60;      // 6 s — longer speech is conversation, not a wake word
    private const int MaxChecksPerMinute = 20;    // CPU guard in a noisy room

    private readonly Func<byte[], string, CancellationToken, Task<string>> _transcribe;
    private readonly Func<bool> _isPaused;
    private readonly Func<bool> _shouldListen;
    private readonly Action<string> _onWake;
    private readonly Queue<DateTime> _recentChecks = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _checking;
    private DateTime _cooldownUntil = DateTime.MinValue;

    public string Phrase { get; set; } = "היי שיר";
    public bool IsRunning => _loop is { IsCompleted: false };
    public string Status { get; private set; } = "כבוי";

    /// <param name="transcribe">(wav, hint, token) → text</param>
    /// <param name="isPaused">true while the assistant is already listening/speaking/busy</param>
    /// <param name="onWake">called with the command said after the phrase (may be empty)</param>
    /// <param name="shouldListen">false = close the microphone completely (e.g. on battery); re-checked every few seconds</param>
    public WakeWordListener(Func<byte[], string, CancellationToken, Task<string>> transcribe, Func<bool> isPaused, Func<bool> shouldListen, Action<string> onWake)
    {
        _transcribe = transcribe;
        _isPaused = isPaused;
        _shouldListen = shouldListen;
        _onWake = onWake;
    }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunAsync(token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(1500); } catch { }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        Status = "כבוי";
    }

    private async Task RunAsync(CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_shouldListen())
                {
                    Status = "מושהה (סוללה)";
                    await Task.Delay(TimeSpan.FromSeconds(15), token);
                    continue;
                }
                Status = "מאזין";
                AppLog.Info($"Wake word listener: microphone open (\"{Phrase}\")");
                await ListenAsync(token);
                AppLog.Info("Wake word listener: microphone closed");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                failures++;
                Status = "אין מיקרופון";
                if (failures <= 3) AppLog.Warn("Wake word listener: " + ex.Message);
                // Microphone missing/busy/unplugged: retry quietly, slower and slower (max every 2 minutes).
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(120, 5 * failures)), token); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private Task ListenAsync(CancellationToken token)
    {
        var preRoll = new Queue<byte[]>();
        var segment = new MemoryStream();
        var inSpeech = false;
        int loudRun = 0, quietRun = 0, speechFrames = 0, segmentFrames = 0;
        var noiseFloor = 0.01;
        var tooLong = false;
        var lastPolicyCheck = DateTime.UtcNow;

        return AudioRecorder.CaptureAsync((buffer, count, level) =>
        {
            if (DateTime.UtcNow - lastPolicyCheck > TimeSpan.FromSeconds(10))
            {
                lastPolicyCheck = DateTime.UtcNow;
                if (!_shouldListen()) return false; // close the mic (e.g. unplugged from power)
            }
            var frame = buffer.AsSpan(0, count).ToArray();
            if (_isPaused() || DateTime.UtcNow < _cooldownUntil)
            {
                inSpeech = false; segment.SetLength(0); preRoll.Clear(); loudRun = 0;
                return true;
            }

            // Slowly track background noise so a fan or AC doesn't count as speech.
            if (!inSpeech) noiseFloor = level < noiseFloor ? noiseFloor * 0.9 + level * 0.1 : noiseFloor * 0.99 + level * 0.01;
            var threshold = Math.Max(0.012, noiseFloor * 3.0);
            var loud = level > threshold;

            if (!inSpeech)
            {
                preRoll.Enqueue(frame);
                if (preRoll.Count > PreRollFrames) preRoll.Dequeue();
                loudRun = loud ? loudRun + 1 : 0;
                if (loudRun >= StartFrames)
                {
                    inSpeech = true; tooLong = false;
                    segment.SetLength(0);
                    foreach (var f in preRoll) segment.Write(f);
                    segmentFrames = preRoll.Count; speechFrames = loudRun; quietRun = 0;
                    preRoll.Clear();
                }
                return true;
            }

            segment.Write(frame);
            segmentFrames++;
            if (loud) { speechFrames++; quietRun = 0; } else quietRun++;
            if (segmentFrames > MaxSegmentFrames) tooLong = true; // keep consuming until quiet, then drop

            if (quietRun >= EndSilenceFrames)
            {
                inSpeech = false; loudRun = 0;
                if (!tooLong && speechFrames >= MinSpeechFrames) Check(segment.ToArray());
                segment.SetLength(0);
            }
            return true;
        }, token);
    }

    private void Check(byte[] pcm)
    {
        if (_checking) return; // one clip at a time; the next utterance will be checked
        var now = DateTime.UtcNow;
        while (_recentChecks.Count > 0 && now - _recentChecks.Peek() > TimeSpan.FromMinutes(1)) _recentChecks.Dequeue();
        if (_recentChecks.Count >= MaxChecksPerMinute) return;
        _recentChecks.Enqueue(now);
        _checking = true;
        var phrase = Phrase;
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                var text = await _transcribe(AudioRecorder.ToWav(pcm), $"The speaker may say the wake phrase \"{phrase}\" followed by a request.", timeout.Token);
                if (!WakePhraseMatcher.TryMatch(text, phrase, out var command)) return;
                AppLog.Info($"Wake phrase detected{(command.Length > 0 ? " with a command" : "")}");
                _cooldownUntil = DateTime.UtcNow.AddSeconds(3);
                _onWake(command);
            }
            catch (Exception ex) { AppLog.Warn("Wake word check failed: " + ex.Message); }
            finally { _checking = false; }
        });
    }

    public void Dispose() => Stop();
}
