using DesktopAssistant.Agent;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Offline;
using DesktopAssistant.Settings;

namespace DesktopAssistant.Voice;

/// <summary>Chooses where speech becomes text: on this PC (free, private) or the cloud provider.</summary>
public sealed class SpeechService(AssistantController controller, OfflineManager offline)
{
    private AppSettings Settings => controller.Settings;
    private string Language => Settings.UiLanguage == "en" ? "en" : "he";

    public bool CanTranscribe => offline.SpeechReady || (Settings.SpeechEngine != SpeechEngine.Offline && controller.CanTranscribe);

    public string EngineLabel => Settings.SpeechEngine switch
    {
        SpeechEngine.Offline => offline.SpeechReady ? "במחשב (אופליין)" : "אופליין — לא מותקן",
        SpeechEngine.Cloud => "בענן (ספק ה-AI)",
        _ => offline.SpeechReady ? "במחשב (אופליין)" : "בענן (ספק ה-AI)",
    };

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellationToken, string? hint = null)
    {
        var useOffline = Settings.SpeechEngine == SpeechEngine.Offline || (Settings.SpeechEngine == SpeechEngine.Auto && offline.SpeechReady);
        if (useOffline)
        {
            if (!offline.SpeechReady) throw new AiException("בחרת זיהוי דיבור אופליין, אבל החבילה עוד לא הורדה. הגדרות ← אופליין.");
            return await offline.TranscribeAsync(wav, Language, hint, cancellationToken);
        }
        try { return await controller.TranscribeAsync(wav, cancellationToken, hint); }
        catch (AiException ex) when (ex.CanFallBackOffline && offline.SpeechReady)
        {
            AppLog.Info("Cloud transcription failed, using offline: " + ex.Message);
            return await offline.TranscribeAsync(wav, Language, hint, cancellationToken);
        }
    }

    /// <summary>The wake word never uses the cloud: always the local engine.</summary>
    public Task<string> TranscribeForWakeWordAsync(byte[] wav, string hint, CancellationToken cancellationToken) =>
        offline.TranscribeAsync(wav, Language, hint, cancellationToken);
}
