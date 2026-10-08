using System.Text.Json;

namespace DesktopAssistant.Configuration;

public sealed class UserSettings
{
    public string UiLanguage { get; set; } = "he";
    public string AssistantHotkey { get; set; } = "Ctrl+Alt+Space";
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool WakeWordEnabled { get; set; }
    public bool SpeakResponses { get; set; }
    public bool AllowScreenshotAnalysis { get; set; }
    public bool EnableDesktopControl { get; set; } = true;
    public bool KeepDiagnostics { get; set; } = true;
    public string TargetLanguage { get; set; } = "he";
    public string LocalMemory { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-3.8-flash";
    public string GeminiVisionModel { get; set; } = "gemini-3.8-flash";
    public bool AllowCloudAi { get; set; }
    public bool AllowCloudScreenTranslation { get; set; }
    public bool AllowGeminiVoice { get; set; }
    public bool AllowLocalMemory { get; set; } = true;
    public bool OnboardingCompleted { get; set; }
    public string AiMode { get; set; } = "GeminiOnline";
    public string TranslationMode { get; set; } = "Gemini";
    public string TranslationQuality { get; set; } = "Balanced";
}

public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAssistant", "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static UserSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { return new(); }
    }
    public static void Save(UserSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, FilePath, true);
    }
}
