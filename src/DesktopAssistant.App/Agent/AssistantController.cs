using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Offline;
using DesktopAssistant.Settings;

namespace DesktopAssistant.Agent;

/// <summary>The assistant's brain: picks cloud/offline model, runs quick commands, the agent, and keeps the conversation.</summary>
public sealed class AssistantController
{
    private readonly List<ChatMessage> _history = [];

    public AppSettings Settings { get; }
    public OfflineManager Offline { get; }

    public AssistantController(AppSettings settings)
    {
        Settings = settings;
        Offline = new OfflineManager(settings);
    }

    public ProviderConfig Provider => Settings.ActiveProvider;

    /// <summary>The selected cloud provider has what it needs (a key, or none required).</summary>
    public bool IsConfigured => !Provider.RequiresKey || SecretStore.Has(Provider.Id);

    public bool HasAnyBrain => CreateModel() is not null;

    public string StatusLabel => Settings.AiMode switch
    {
        AiMode.Offline => Offline.BrainReady ? "מוח מקומי · אופליין" : "אופליין — לא הותקן",
        AiMode.Auto when !IsConfigured && Offline.BrainReady => "מוח מקומי · אופליין",
        _ when IsConfigured => $"{Provider.DisplayName} · {Provider.Model}{(Settings.AiMode == AiMode.Auto && Offline.BrainReady ? " (+אופליין)" : "")}",
        _ => "לא מחובר ל-AI — ⚙ כדי לחבר",
    };

    /// <summary>The model to use right now, or null when nothing is available.</summary>
    public IChatModel? CreateModel() => Settings.AiMode switch
    {
        AiMode.Offline => Offline.BrainReady ? Offline.CreateBrainModel() : null,
        AiMode.Auto => CloudModel() ?? (Offline.BrainReady ? Offline.CreateBrainModel() : null),
        _ => CloudModel(),
    };

    private IChatModel? CloudModel() => IsConfigured ? ChatModelFactory.Create(Provider) : FirstConfiguredModel(_ => true);

    private IChatModel? FirstConfiguredModel(Func<IChatModel, bool> predicate)
    {
        foreach (var provider in Settings.Providers)
        {
            if (provider.Id == "custom" && provider.BaseUrl is "https://" or "") continue;
            if (!provider.RequiresKey || !SecretStore.Has(provider.Id)) continue; // never silently fall back to a local server that may not be running
            var model = ChatModelFactory.Create(provider);
            if (predicate(model)) return model;
        }
        return null;
    }

    public void NewConversation() => _history.Clear();

    public async Task<AgentResult> AskAsync(string text, IReadOnlyList<ImagePart> images, AgentContext context, CancellationToken cancellationToken)
    {
        // 1) Everyday commands: instant, free, offline.
        if (Settings.QuickCommands && images.Count == 0)
        {
            var quick = await QuickCommands.TryHandleAsync(text, context, cancellationToken);
            if (quick is not null)
            {
                Remember(text, quick, []);
                return new AgentResult(quick, 0, []);
            }
        }

        // 2) The AI agent.
        var model = CreateModel();
        if (model is null)
            return new AgentResult(Settings.AiMode == AiMode.Offline
                ? "המוח האופליין עוד לא הותקן. ⚙ הגדרות ← אופליין ← הורדה."
                : "כדי לענות על זה אני צריכה \"מוח\" 🙂\n⚙ הגדרות ← בינה מלאכותית: הדביקו מפתח (Gemini חינמי), או ⚙ ← אופליין כדי לעבוד בלי אינטרנט בכלל.\nבינתיים אני יודעת לפתוח תוכנות, לחפש, להקליד, טיימרים, ווליום ועוד — ראו \"?\".", 0, []);

        AgentResult result;
        try { result = await RunAgentAsync(model, text, images, context, cancellationToken); }
        catch (AiException ex) when (Settings.AiMode == AiMode.Auto && ex.CanFallBackOffline && Offline.BrainReady && model.Config.Id != "offline")
        {
            AppLog.Info("Cloud failed, switching to the offline brain: " + ex.Message);
            context.Activity(ex.Kind == AiErrorKind.Quota ? "המכסה בענן נגמרה — עוברת למוח המקומי…" : "אין חיבור — עוברת למוח המקומי…");
            result = await RunAgentAsync(Offline.CreateBrainModel(), text, images, context, cancellationToken);
            result = result with { Text = result.Text + (ex.Kind == AiErrorKind.Quota ? "\n(ענה המוח המקומי — המכסה של ספק הענן נגמרה)" : "\n(ענה המוח המקומי — אין חיבור לענן)") };
        }
        Remember(images.Count > 0 ? text + " [צורפה תמונה]" : text, result.Text, result.Actions);
        return result;
    }

    private Task<AgentResult> RunAgentAsync(IChatModel model, string text, IReadOnlyList<ImagePart> images, AgentContext context, CancellationToken cancellationToken)
    {
        var offline = model.Config.Id == "offline";
        if (offline) context.Activity("המוח המקומי חושב… (בפעם הראשונה הטעינה לוקחת כחצי דקה)");
        var runner = new AgentRunner(model, context, offline ? DesktopTools.Essential : DesktopTools.All);
        // Small local models do better with a short history.
        return runner.RunAsync(_history.TakeLast(offline ? 6 : 20).ToList(), ChatMessage.User(text, [.. images]), cancellationToken);
    }

    /// <summary>Long-term history keeps text only (screenshots and tool chatter stay out).</summary>
    private void Remember(string user, string answer, IReadOnlyList<string> actions)
    {
        _history.Add(ChatMessage.User(user));
        var summary = actions.Count > 0 ? $"\n[פעולות שבוצעו: {string.Join(", ", actions.Take(12))}]" : "";
        _history.Add(ChatMessage.AssistantText(answer + summary));
        if (_history.Count > 60) _history.RemoveRange(0, _history.Count - 60);
    }

    /// <summary>True when some configured cloud provider can turn speech into text.</summary>
    public bool CanTranscribe => (IsConfigured && ChatModelFactory.Create(Provider).CanTranscribe) || FirstConfiguredModel(m => m.CanTranscribe) is not null;

    /// <summary>Cloud speech-to-text (the SpeechService decides between this and the offline engine).</summary>
    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellationToken, string? hint = null)
    {
        IChatModel? model = null;
        if (IsConfigured)
        {
            var active = ChatModelFactory.Create(Provider);
            if (active.CanTranscribe) model = active;
        }
        model ??= FirstConfiguredModel(m => m.CanTranscribe);
        if (model is null) throw new AiException("לזיהוי דיבור צריך מפתח של Gemini, OpenAI או Groq — או להוריד זיהוי דיבור אופליין (⚙ ← אופליין, בלי אינטרנט ובלי מכסה).");
        var language = Settings.UiLanguage == "en" ? "en" : "he";
        return await model.TranscribeAsync(wav, language, cancellationToken, hint);
    }
}
