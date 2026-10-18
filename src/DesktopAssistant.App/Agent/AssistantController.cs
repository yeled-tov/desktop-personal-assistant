using System.Text.RegularExpressions;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;
using DesktopAssistant.Settings;

namespace DesktopAssistant.Agent;

/// <summary>The assistant's brain: settings, conversation memory, model selection, agent runs and voice.</summary>
public sealed class AssistantController
{
    private readonly List<ChatMessage> _history = [];

    public AppSettings Settings { get; private set; }

    public AssistantController(AppSettings settings) => Settings = settings;

    public void ReloadSettings(AppSettings settings) => Settings = settings;

    public ProviderConfig Provider => Settings.ActiveProvider;

    public bool IsConfigured => !Provider.RequiresKey || SecretStore.Has(Provider.Id);

    /// <summary>The model to use, or null when no key is configured.</summary>
    public IChatModel? CreateModel() => IsConfigured ? ChatModelFactory.Create(Provider) : FirstConfiguredModel(_ => true);

    private IChatModel? FirstConfiguredModel(Func<IChatModel, bool> predicate)
    {
        foreach (var provider in Settings.Providers)
        {
            if (provider.Id == "custom" && provider.BaseUrl is "https://" or "") continue;
            if (provider.RequiresKey && !SecretStore.Has(provider.Id)) continue;
            if (!provider.RequiresKey) continue; // don't silently fall back to a local server that may not be running
            var model = ChatModelFactory.Create(provider);
            if (predicate(model)) return model;
        }
        return null;
    }

    public void NewConversation() => _history.Clear();

    public async Task<AgentResult> AskAsync(string text, IReadOnlyList<ImagePart> images, AgentContext context, CancellationToken cancellationToken)
    {
        var model = CreateModel();
        if (model is null)
        {
            var offline = await TryOfflineAsync(text, cancellationToken);
            return new AgentResult(offline ?? "עוד לא חיברת מוח לעוזר 🙂\nפתח ⚙ הגדרות, בחר ספק (למשל Google Gemini — יש מסלול חינמי) והדבק מפתח API. בלי מפתח אני יודע רק לפתוח אפליקציות ולחפש ('פתח וורד', 'חפש מזג אוויר').", 0, []);
        }
        var user = ChatMessage.User(text, [.. images]);
        var runner = new AgentRunner(model, context);
        var result = await runner.RunAsync(_history.TakeLast(20).ToList(), user, cancellationToken);
        // Long-term history keeps text only (screenshots and tool chatter stay out).
        _history.Add(ChatMessage.User(images.Count > 0 ? text + " [צורפה תמונה]" : text));
        var summary = result.Actions.Count > 0 ? $"\n[פעולות שבוצעו: {string.Join(", ", result.Actions.Take(12))}]" : "";
        _history.Add(ChatMessage.AssistantText(result.Text + summary));
        return result;
    }

    /// <summary>Speech-to-text with the active provider, or any configured provider that can transcribe.</summary>
    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellationToken, string? hint = null)
    {
        IChatModel? model = null;
        if (IsConfigured)
        {
            var active = ChatModelFactory.Create(Provider);
            if (active.CanTranscribe) model = active;
        }
        model ??= FirstConfiguredModel(m => m.CanTranscribe);
        if (model is null) throw new AiException("כדי לדבר עם העוזר צריך מפתח של Gemini, OpenAI או Groq (הם יודעים לתמלל עברית). אפשר להוסיף בהגדרות.");
        var language = Settings.UiLanguage == "en" ? "en" : "he";
        return await model.TranscribeAsync(wav, language, cancellationToken, hint);
    }

    /// <summary>True when some configured provider can turn speech into text (needed for voice and the wake word).</summary>
    public bool CanTranscribe => (IsConfigured && ChatModelFactory.Create(Provider).CanTranscribe) || FirstConfiguredModel(m => m.CanTranscribe) is not null;

    /// <summary>Basic commands that work with no AI connected.</summary>
    private static async Task<string?> TryOfflineAsync(string text, CancellationToken cancellationToken)
    {
        var t = text.Trim();
        var open = Regex.Match(t, @"^(?:תפתח|פתח|פתחי|הפעל|open|launch|start)\s+(?:לי\s+)?(?:את\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (open.Success)
        {
            try { return await AppLauncher.OpenAsync(open.Groups[1].Value, null, cancellationToken); }
            catch (Exception ex) { return ex.Message; }
        }
        var search = Regex.Match(t, @"^(?:חפש|תחפש|חפשי|search|google)\s+(?:לי\s+)?(?:ב(?:גוגל|יוטיוב)\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (search.Success) return AppLauncher.Search(search.Groups[1].Value, t.Contains("יוטיוב") || t.Contains("youtube", StringComparison.OrdinalIgnoreCase) ? "youtube" : "google");
        return null;
    }
}
