using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopAssistant.Core;

namespace DesktopAssistant.Settings;

public enum ProviderKind { Gemini, OpenAICompatible, Anthropic }

/// <summary>Where the "brain" runs.</summary>
public enum AiMode
{
    /// <summary>The selected API provider (Gemini, OpenAI…).</summary>
    Cloud,
    /// <summary>Everything on this computer (needs the offline pack).</summary>
    Offline,
    /// <summary>Cloud, and automatically the offline brain when there is no internet or the quota ran out.</summary>
    Auto
}

/// <summary>Which speech-to-text engine to use.</summary>
public enum SpeechEngine { Auto, Offline, Cloud }

public sealed class ProviderConfig
{
    public string Id { get; set; } = "";
    public ProviderKind Kind { get; set; }
    public string DisplayName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>Speech-to-text model for voice input. Empty = use the chat model (Gemini) or not supported.</summary>
    public string TranscriptionModel { get; set; } = "";
    public bool RequiresKey { get; set; } = true;

    public ProviderConfig Clone() => (ProviderConfig)MemberwiseClone();
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 3;
    public string UiLanguage { get; set; } = "he";
    public string ActiveProviderId { get; set; } = "gemini";
    public AiMode AiMode { get; set; } = AiMode.Auto;
    public SpeechEngine SpeechEngine { get; set; } = SpeechEngine.Auto;
    /// <summary>Simple commands (open, search, time, volume, timers…) run instantly on the PC without using the AI quota.</summary>
    public bool QuickCommands { get; set; } = true;
    /// <summary>Selected offline packs (ids from OfflineCatalog).</summary>
    public string OfflineSpeechModel { get; set; } = "";
    public string OfflineBrainModel { get; set; } = "";
    public List<ProviderConfig> Providers { get; set; } = ProviderPresets.All.Select(p => p.Clone()).ToList();

    public string AssistantHotkey { get; set; } = "Ctrl+Alt+Space";
    public string VoiceHotkey { get; set; } = "Ctrl+Alt+V";
    public string DictationHotkey { get; set; } = "Ctrl+Alt+D";
    public string CircleSearchHotkey { get; set; } = "Ctrl+Alt+S";
    public string TranslateHotkey { get; set; } = "Ctrl+Alt+T";
    /// <summary>The Copilot key on new keyboards sends Win+Shift+F23.</summary>
    public bool UseCopilotKey { get; set; } = true;

    public bool TouchpadLongPressEnabled { get; set; } = true;
    public int LongPressMilliseconds { get; set; } = 800;

    public bool SpeakReplies { get; set; }
    /// <summary>The assistant's name (used in the wake phrase, greetings and the AI's persona).</summary>
    public string AssistantName { get; set; } = "שיר";
    /// <summary>Say this and the assistant opens by itself. Anything said right after it is run as a command.</summary>
    public string WakePhrase { get; set; } = "היי שיר";
    /// <summary>Off by default. Works only with the offline speech pack (nothing is sent to the internet).</summary>
    public bool WakeWordEnabled { get; set; }
    /// <summary>Pause listening for the wake word while the laptop runs on battery.</summary>
    public bool WakeOnlyWhenCharging { get; set; } = true;
    public bool AutoSendVoice { get; set; } = true;
    public bool ConfirmSensitiveActions { get; set; } = true;
    public int MaxAgentSteps { get; set; } = 30;
    public string TranslationTarget { get; set; } = "עברית";
    public bool StartWithWindows { get; set; } = true;
    public bool StartHidden { get; set; } = true;
    public bool OnboardingDone { get; set; }
    public List<string> Memory { get; set; } = [];

    [JsonIgnore] public ProviderConfig ActiveProvider => Providers.FirstOrDefault(p => p.Id == ActiveProviderId) ?? Providers.First();

    /// <summary>Adds presets that were introduced after the user's settings file was written.</summary>
    public void Normalize()
    {
        // v2.0.13 had the wake word on by default and sent speech to the cloud provider — turn it off once.
        if (SchemaVersion < 3) { WakeWordEnabled = false; SchemaVersion = 3; }
        Providers ??= [];
        foreach (var preset in ProviderPresets.All)
            if (Providers.All(p => p.Id != preset.Id)) Providers.Add(preset.Clone());
        if (Providers.All(p => p.Id != ActiveProviderId)) ActiveProviderId = Providers[0].Id;
        LongPressMilliseconds = Math.Clamp(LongPressMilliseconds, 400, 3000);
        MaxAgentSteps = Math.Clamp(MaxAgentSteps, 5, 80);
        Memory ??= [];
        if (string.IsNullOrWhiteSpace(TranslationTarget)) TranslationTarget = "עברית";
        if (string.IsNullOrWhiteSpace(AssistantName)) AssistantName = "שיר";
        if (string.IsNullOrWhiteSpace(WakePhrase)) WakePhrase = "היי " + AssistantName;
    }
}

public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(AppLog.DataDirectory, "settings.v2.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static readonly object Gate = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings file unreadable; starting with defaults", ex);
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
        }
        var fresh = new AppSettings();
        fresh.Normalize();
        return fresh;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppLog.DataDirectory);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
                File.Move(temp, FilePath, true);
            }
        }
        catch (Exception ex) { AppLog.Error("Saving settings failed", ex); }
    }
}
