using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopAssistant.Settings;

namespace DesktopAssistant.AI;

public enum ChatRole { User, Assistant }

public abstract record ChatPart;
public sealed record TextPart(string Text) : ChatPart;
public sealed record ImagePart(byte[] Data, string MimeType = "image/jpeg") : ChatPart;
public sealed record ToolCallPart(string Id, string Name, JsonObject Arguments) : ChatPart;
public sealed record ToolResultPart(string Id, string Name, string Content, bool IsError = false, ImagePart? Image = null) : ChatPart;

public sealed class ChatMessage
{
    public ChatRole Role { get; init; }
    public List<ChatPart> Parts { get; init; } = [];
    /// <summary>The provider's own representation of a model turn (Gemini needs its thought signatures echoed back verbatim).</summary>
    public JsonNode? ProviderRaw { get; init; }
    public string? ProviderKindTag { get; init; }

    public static ChatMessage User(string text, params ImagePart[] images)
    {
        var parts = new List<ChatPart>();
        parts.AddRange(images);
        parts.Add(new TextPart(text));
        return new ChatMessage { Role = ChatRole.User, Parts = parts };
    }

    public static ChatMessage AssistantText(string text) => new() { Role = ChatRole.Assistant, Parts = [new TextPart(text)] };

    public string TextContent => string.Join("\n", Parts.OfType<TextPart>().Select(p => p.Text)).Trim();
}

public sealed record ToolSpec(string Name, string Description, JsonObject Schema);

public sealed record ModelReply(string Text, IReadOnlyList<ToolCallPart> ToolCalls, JsonNode? ProviderRaw, string ProviderKindTag);

public sealed record ModelOptions(bool JsonOutput = false, int MaxOutputTokens = 8192, string? ModelOverride = null);

public interface IChatModel
{
    ProviderConfig Config { get; }
    Task<ModelReply> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, ModelOptions options, CancellationToken cancellationToken);
    bool CanTranscribe { get; }
    Task<string> TranscribeAsync(byte[] wav, string languageCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
}

/// <summary>An error whose message is already written for the user (Hebrew).</summary>
public sealed class AiException(string message) : Exception(message);

public abstract class ChatModelBase(ProviderConfig config) : IChatModel
{
    protected static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    public ProviderConfig Config { get; } = config;

    public abstract Task<ModelReply> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, ModelOptions options, CancellationToken cancellationToken);
    public abstract bool CanTranscribe { get; }
    public abstract Task<string> TranscribeAsync(byte[] wav, string languageCode, CancellationToken cancellationToken);
    public abstract Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);

    protected string BaseUrl => Config.BaseUrl.TrimEnd('/');
    protected string Model(ModelOptions? options = null) => !string.IsNullOrWhiteSpace(options?.ModelOverride) ? options!.ModelOverride!.Trim()
        : !string.IsNullOrWhiteSpace(Config.Model) ? Config.Model.Trim() : throw new AiException($"לא נבחר מודל עבור {Config.DisplayName}. בחר מודל בהגדרות.");

    protected string ApiKey()
    {
        var key = SecretStore.Get(Config.Id);
        if (key is null && Config.RequiresKey) throw new AiException($"לא נשמר מפתח API עבור {Config.DisplayName}. פתח הגדרות ← בינה מלאכותית והדבק מפתח.");
        return key ?? "";
    }

    protected async Task<JsonNode> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try { response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken); }
        catch (HttpRequestException ex) { throw new AiException($"אין חיבור ל-{Config.DisplayName}. בדוק את האינטרנט ({ex.Message})."); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AiException($"{Config.DisplayName} לא ענה בזמן. נסה שוב."); }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw new AiException(DescribeError(response.StatusCode, body));
            try { return JsonNode.Parse(body) ?? new JsonObject(); }
            catch (JsonException) { throw new AiException($"{Config.DisplayName} החזיר תשובה לא תקינה."); }
        }
    }

    protected string DescribeError(HttpStatusCode code, string body)
    {
        var detail = ExtractErrorMessage(body);
        var hint = code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "מפתח ה-API שגוי או שאין לו הרשאה",
            HttpStatusCode.NotFound => $"המודל '{Config.Model}' לא נמצא — בחר מודל אחר בהגדרות (כפתור 'טען מודלים')",
            HttpStatusCode.TooManyRequests => "הגעת למגבלת הבקשות/מכסה של הספק. נסה שוב בעוד רגע",
            HttpStatusCode.BadRequest => "הבקשה נדחתה",
            HttpStatusCode.PaymentRequired => "נגמרה היתרה בחשבון אצל הספק",
            _ => $"שגיאה {(int)code}"
        };
        return string.IsNullOrWhiteSpace(detail) ? $"{Config.DisplayName}: {hint}." : $"{Config.DisplayName}: {hint}.\n({detail})";
    }

    private static string ExtractErrorMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            var message = node?["error"]?["message"]?.GetValue<string>() ?? node?["error"]?.GetValue<string>() ?? node?["message"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(message)) return message.Length > 300 ? message[..300] + "…" : message;
        }
        catch { }
        body = body.Trim();
        return body.Length > 200 ? body[..200] + "…" : body;
    }

    protected static StringContent JsonContent(JsonNode node) => new(node.ToJsonString(), Encoding.UTF8, "application/json");

    protected static MultipartFormDataContent WavForm(byte[] wav, string model, string languageCode)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "speech.wav");
        form.Add(new StringContent(model), "model");
        if (!string.IsNullOrWhiteSpace(languageCode)) form.Add(new StringContent(languageCode), "language");
        form.Add(new StringContent("json"), "response_format");
        return form;
    }

    /// <summary>Removes a ```json fence some models wrap around JSON output.</summary>
    public static string StripCodeFence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```")) return t;
        var firstNewLine = t.IndexOf('\n');
        if (firstNewLine < 0) return t.Trim('`');
        t = t[(firstNewLine + 1)..];
        var end = t.LastIndexOf("```", StringComparison.Ordinal);
        return (end >= 0 ? t[..end] : t).Trim();
    }
}

public static class ChatModelFactory
{
    public static IChatModel Create(ProviderConfig config) => config.Kind switch
    {
        ProviderKind.Gemini => new GeminiModel(config),
        ProviderKind.Anthropic => new AnthropicModel(config),
        _ => new OpenAiCompatibleModel(config)
    };
}
