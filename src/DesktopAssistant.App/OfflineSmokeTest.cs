using System.Diagnostics;
using System.Windows;
using DesktopAssistant.Agent;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;
using DesktopAssistant.Offline;
using DesktopAssistant.Overlay;
using DesktopAssistant.Settings;

namespace DesktopAssistant;

/// <summary>
/// CI check of the whole offline stack on real Windows: downloads the light packs from their real URLs,
/// runs local speech-to-text on synthesized speech, the local brain (text, tool calling, vision) and OCR translation.
/// Run: DesktopAssistant.exe --smoke-offline   → writes %LOCALAPPDATA%\DesktopAssistant\offline-result.txt
/// </summary>
internal static class OfflineSmokeTest
{
    public static async Task RunAsync(AppHost host)
    {
        var marker = Path.Combine(AppLog.DataDirectory, "offline-result.txt");
        var log = new List<string>();
        var clock = Stopwatch.StartNew();
        void Step(string text) { var line = $"[{clock.Elapsed:mm\\:ss}] {text}"; log.Add(line); AppLog.Info("OFFLINE " + line); File.WriteAllText(marker, "RUNNING\n" + string.Join("\n", log)); }
        try
        {
            Directory.CreateDirectory(AppLog.DataDirectory);
            var settings = host.Controller.Settings;
            settings.AiMode = AiMode.Offline;
            settings.SpeechEngine = SpeechEngine.Offline;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(40));
            var lastLogged = -1;
            var progress = new Progress<DownloadProgress>(p => { var pct = (int)(100 * p.Done / Math.Max(1, p.Total)); if (pct / 20 != lastLogged) { lastLogged = pct / 20; AppLog.Info($"download {p.File} {pct}% {p.BytesPerSecond / 1e6:0.0}MB/s"); } });

            foreach (var id in new[] { "speech-small", "brain-2b" })
            {
                var pack = OfflineCatalog.Find(id)!;
                await host.Offline.InstallAsync(pack, progress, cts.Token);
                Step($"installed {id} ({OfflineCatalog.Size(pack.TotalBytes)}) — sizes and URLs verified");
            }

            // Speech: synthesize English with the Windows voice, resample to 16 kHz, transcribe locally.
            var wav = await SynthesizeAsync("Hello. This is a test of the offline speech engine on Windows.");
            var text = await host.Offline.TranscribeAsync(wav, "en", null, cts.Token);
            Step($"speech-to-text: \"{text}\"");
            if (!text.Contains("test", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("transcript doesn't contain the spoken words");

            // Brain: plain answer
            var model = host.Offline.CreateBrainModel();
            var reply = await model.CompleteAsync("", [ChatMessage.User("Reply with one word: OK")], [], new ModelOptions(MaxOutputTokens: 50), cts.Token);
            Step($"brain answered: \"{reply.Text}\"");
            if (string.IsNullOrWhiteSpace(reply.Text)) throw new InvalidOperationException("empty answer");

            // Brain: Hebrew
            reply = await model.CompleteAsync("", [ChatMessage.User("ענה במשפט אחד בעברית: מה הבירה של צרפת?")], [], new ModelOptions(MaxOutputTokens: 80), cts.Token);
            Step($"brain Hebrew: \"{reply.Text}\"");

            // Brain: tool calling through the real agent loop
            var opened = false;
            var context = new AgentContext
            {
                Settings = settings,
                Confirm = (_, _) => Task.FromResult(true),
                Activity = a => AppLog.Info("activity: " + a),
                PrepareForDesktopAction = () => Task.CompletedTask,
                SetOverlayVisible = _ => Task.CompletedTask,
                OpenScreenTranslation = () => Task.CompletedTask,
                Notify = (_, _) => { },
                RunOnUiThread = work => Application.Current.Dispatcher.InvokeAsync(work).Task,
                Help = () => AssistantHelp.Text(settings),
            };
            var runner = new AgentRunner(model, context, DesktopTools.Essential);
            var result = await runner.RunAsync([], ChatMessage.User("Open Notepad."), cts.Token);
            opened = result.Actions.Count > 0;
            Step($"agent: {result.Steps} step(s), actions [{string.Join(", ", result.Actions)}], answer \"{result.Text}\"");
            if (!opened) throw new InvalidOperationException("the offline brain did not call a tool");
            foreach (var w in WindowManager.List().Where(w => w.ProcessName.Contains("notepad", StringComparison.OrdinalIgnoreCase))) WindowManager.Close(w.Handle);

            // Brain: vision on a screenshot
            var shot = ScreenCapture.CaptureForAgent(maxSide: 768);
            reply = await model.CompleteAsync("", [ChatMessage.User("Describe this screenshot in one short sentence.", new ImagePart(shot.Jpeg))], [], new ModelOptions(MaxOutputTokens: 120), cts.Token);
            Step($"vision: \"{reply.Text}\"");
            if (string.IsNullOrWhiteSpace(reply.Text)) throw new InvalidOperationException("empty vision answer");

            // Text translation (the part of screen translation the brain does)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var words = await ScreenVision.TranslateLinesAsync(model, ["Open file", "Settings", "Close window", "Good morning"], "Hebrew", cts.Token);
            Step($"translate lines ({sw.Elapsed.TotalSeconds:0}s): " + string.Join(" | ", words.Select(kv => kv.Key + " → " + kv.Value)));
            if (words.Count < 2) throw new InvalidOperationException("line translation returned too little");

            // Offline screen translation (Windows OCR + local brain)
            var monitor = ScreenCapture.CaptureMonitors()[0];
            var regions = await ScreenVision.TranslateAsync(model, monitor.Image, "עברית", cts.Token);
            Step($"screen translation: {regions.Count} region(s){(regions.Count > 0 ? $", e.g. \"{regions[0].Original}\" → \"{regions[0].Translated}\"" : "")}");

            File.WriteAllText(marker, "OFFLINE OK\n" + string.Join("\n", log));
            host.Exit();
        }
        catch (Exception ex)
        {
            AppLog.Error("OFFLINE FAILED", ex);
            File.WriteAllText(marker, $"OFFLINE FAILED: {ex}\n" + string.Join("\n", log));
            host.Dispose();
            Application.Current.Shutdown(1);
        }
    }

    private static async Task<byte[]> SynthesizeAsync(string text)
    {
        using var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
        var voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (voice is not null) synth.Voice = voice;
        using var stream = await synth.SynthesizeTextToStreamAsync(text);
        using var memory = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(memory);
        return To16kMono(memory.ToArray());
    }

    /// <summary>Converts a PCM WAV of any rate/channels to 16 kHz mono 16-bit (what whisper expects).</summary>
    private static byte[] To16kMono(byte[] wav)
    {
        int channels = BitConverter.ToInt16(wav, 22), rate = BitConverter.ToInt32(wav, 24), bits = BitConverter.ToInt16(wav, 34);
        var pos = 12;
        while (pos + 8 < wav.Length && System.Text.Encoding.ASCII.GetString(wav, pos, 4) != "data") pos += 8 + BitConverter.ToInt32(wav, pos + 4);
        var dataLength = Math.Min(BitConverter.ToInt32(wav, pos + 4), wav.Length - pos - 8);
        var start = pos + 8;
        var frames = dataLength / (channels * bits / 8);
        var mono = new double[frames];
        for (var i = 0; i < frames; i++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++) sum += BitConverter.ToInt16(wav, start + (i * channels + c) * 2) / 32768.0;
            mono[i] = sum / channels;
        }
        var outFrames = (int)((long)frames * 16000 / rate);
        var pcm = new byte[outFrames * 2];
        for (var i = 0; i < outFrames; i++)
        {
            var src = (double)i * rate / 16000;
            var a = (int)src; var b = Math.Min(a + 1, frames - 1); var f = src - a;
            var v = (short)Math.Clamp((mono[a] * (1 - f) + mono[b] * f) * 32767, short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), v);
        }
        return Voice.AudioRecorder.ToWav(pcm);
    }
}
