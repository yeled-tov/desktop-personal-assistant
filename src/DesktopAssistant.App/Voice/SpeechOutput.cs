using System.Media;
using System.Text.RegularExpressions;
using DesktopAssistant.Core;
using Windows.Media.SpeechSynthesis;

namespace DesktopAssistant.Voice;

/// <summary>Reads replies aloud with the Windows voices (Hebrew voice "Asaf" if the Hebrew speech pack is installed).</summary>
public sealed class SpeechOutput : IDisposable
{
    private SoundPlayer? _player;
    private DateTime _speakingUntil = DateTime.MinValue;

    /// <summary>True while a reply is being read aloud (so the wake-word listener doesn't hear the assistant itself).</summary>
    public bool IsSpeaking => DateTime.UtcNow < _speakingUntil;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        Stop();
        var clean = Regex.Replace(text, @"[*_#`>\[\]]", "").Trim();
        if (clean.Length == 0) return;
        if (clean.Length > 1200) clean = clean[..1200];
        try
        {
            using var synth = new SpeechSynthesizer();
            var hebrew = clean.Any(c => c >= '֐' && c <= '׿');
            var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith(hebrew ? "he" : "en", StringComparison.OrdinalIgnoreCase));
            if (voice is not null) synth.Voice = voice;
            else if (hebrew) AppLog.Warn("No Hebrew TTS voice installed (Settings > Time & language > Speech > add Hebrew).");
            using var stream = await synth.SynthesizeTextToStreamAsync(clean);
            var memory = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            var bytesPerSecond = memory.Length > 32 ? BitConverter.ToInt32(memory.GetBuffer(), 28) : 0;
            if (bytesPerSecond > 0) _speakingUntil = DateTime.UtcNow.AddSeconds(memory.Length / (double)bytesPerSecond + 0.5);
            _player = new SoundPlayer(memory);
            _player.Play();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Error("Text to speech failed", ex); }
    }

    public void Stop()
    {
        try { _player?.Stop(); _player?.Dispose(); } catch { }
        _player = null;
        _speakingUntil = DateTime.MinValue;
    }

    public void Dispose() => Stop();
}
