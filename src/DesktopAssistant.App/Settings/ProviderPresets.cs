namespace DesktopAssistant.Settings;

public static class ProviderPresets
{
    public static readonly IReadOnlyList<ProviderConfig> All =
    [
        new() { Id = "gemini", Kind = ProviderKind.Gemini, DisplayName = "Google Gemini", BaseUrl = "https://generativelanguage.googleapis.com/v1beta", Model = "gemini-3.8-flash" },
        new() { Id = "openai", Kind = ProviderKind.OpenAICompatible, DisplayName = "OpenAI (ChatGPT)", BaseUrl = "https://api.openai.com/v1", Model = "gpt-6.1-sol", TranscriptionModel = "gpt-transcribe" },
        new() { Id = "anthropic", Kind = ProviderKind.Anthropic, DisplayName = "Anthropic Claude", BaseUrl = "https://api.anthropic.com/v1", Model = "claude-sonnet-5-5" },
        new() { Id = "groq", Kind = ProviderKind.OpenAICompatible, DisplayName = "Groq", BaseUrl = "https://api.groq.com/openai/v1", Model = "llama-3.3-70b-versatile", TranscriptionModel = "whisper-large-v3-turbo" },
        new() { Id = "openrouter", Kind = ProviderKind.OpenAICompatible, DisplayName = "OpenRouter (כל המודלים)", BaseUrl = "https://openrouter.ai/api/v1", Model = "openrouter/auto" },
        new() { Id = "deepseek", Kind = ProviderKind.OpenAICompatible, DisplayName = "DeepSeek", BaseUrl = "https://api.deepseek.com/v1", Model = "deepseek-chat" },
        new() { Id = "ollama", Kind = ProviderKind.OpenAICompatible, DisplayName = "Ollama (מקומי, ללא אינטרנט)", BaseUrl = "http://localhost:11434/v1", Model = "llama3.2", RequiresKey = false },
        new() { Id = "custom", Kind = ProviderKind.OpenAICompatible, DisplayName = "ספק מותאם (OpenAI-compatible)", BaseUrl = "https://", Model = "" },
    ];

    public static string? KeyUrl(string providerId) => providerId switch
    {
        "gemini" => "https://aistudio.google.com/apikey",
        "openai" => "https://platform.openai.com/api-keys",
        "anthropic" => "https://console.anthropic.com/settings/keys",
        "groq" => "https://console.groq.com/keys",
        "openrouter" => "https://openrouter.ai/keys",
        "deepseek" => "https://platform.deepseek.com/api_keys",
        _ => null
    };
}
