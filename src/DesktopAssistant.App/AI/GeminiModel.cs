using System.Text.Json.Nodes;
using DesktopAssistant.Settings;

namespace DesktopAssistant.AI;

public sealed class GeminiModel(ProviderConfig config) : ChatModelBase(config)
{
    public const string Tag = "gemini";
    private static readonly HashSet<string> SchemaKeys = ["type", "description", "enum", "properties", "items", "required", "nullable", "format", "minimum", "maximum"];

    public override bool CanTranscribe => true;

    public override async Task<ModelReply> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, ModelOptions options, CancellationToken cancellationToken)
    {
        var contents = new JsonArray();
        foreach (var message in messages) contents.Add(ToContent(message));

        var body = new JsonObject { ["contents"] = contents };
        if (!string.IsNullOrWhiteSpace(system)) body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) };
        if (tools.Count > 0)
        {
            var declarations = new JsonArray();
            foreach (var tool in tools)
                declarations.Add(new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = ConvertSchema(tool.Schema) });
            body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = declarations });
            body["toolConfig"] = new JsonObject { ["functionCallingConfig"] = new JsonObject { ["mode"] = "AUTO" } };
        }
        var generation = new JsonObject { ["maxOutputTokens"] = options.MaxOutputTokens };
        if (options.JsonOutput) generation["responseMimeType"] = "application/json";
        body["generationConfig"] = generation;

        var response = await PostAsync(Model(options), body, cancellationToken);
        var candidates = response["candidates"] as JsonArray;
        if (candidates is null || candidates.Count == 0)
        {
            var reason = response["promptFeedback"]?["blockReason"]?.GetValue<string>();
            throw new AiException(reason is null ? "Gemini לא החזיר תשובה." : $"Gemini חסם את הבקשה ({reason}).");
        }
        var content = candidates[0]?["content"] as JsonObject;
        if (content is null)
        {
            var finish = candidates[0]?["finishReason"]?.GetValue<string>() ?? "";
            return new ModelReply(finish is "SAFETY" or "PROHIBITED_CONTENT" ? "Gemini סירב לענות על זה." : "", [], null, Tag);
        }

        var texts = new List<string>();
        var calls = new List<ToolCallPart>();
        if (content["parts"] is JsonArray parts)
        {
            foreach (var part in parts.OfType<JsonObject>())
            {
                if (part["thought"]?.GetValue<bool>() == true) continue;
                if (part["text"] is JsonValue text) texts.Add(text.GetValue<string>());
                if (part["functionCall"] is JsonObject call)
                {
                    var id = call["id"]?.GetValue<string>() ?? $"call_{Guid.NewGuid():N}"[..17];
                    var args = call["args"] as JsonObject ?? new JsonObject();
                    calls.Add(new ToolCallPart(id, call["name"]?.GetValue<string>() ?? "", (JsonObject)args.DeepClone()));
                }
            }
        }
        if (!content.ContainsKey("role")) content["role"] = "model";
        return new ModelReply(string.Join("", texts).Trim(), calls, content.DeepClone(), Tag);
    }

    private static JsonObject ToContent(ChatMessage message)
    {
        if (message.Role == ChatRole.Assistant && message.ProviderKindTag == Tag && message.ProviderRaw is JsonObject raw)
            return (JsonObject)raw.DeepClone();

        var parts = new JsonArray();
        var trailingImages = new List<ImagePart>();
        foreach (var part in message.Parts)
        {
            switch (part)
            {
                case TextPart text when !string.IsNullOrEmpty(text.Text): parts.Add(new JsonObject { ["text"] = text.Text }); break;
                case ImagePart image: parts.Add(InlineData(image.MimeType, image.Data)); break;
                case ToolCallPart call: parts.Add(new JsonObject { ["functionCall"] = new JsonObject { ["name"] = call.Name, ["args"] = call.Arguments.DeepClone() } }); break;
                case ToolResultPart result:
                    var response = new JsonObject { ["name"] = result.Name, ["response"] = new JsonObject { [result.IsError ? "error" : "result"] = result.Content } };
                    if (!result.Id.StartsWith("call_", StringComparison.Ordinal)) response["id"] = result.Id;
                    parts.Add(new JsonObject { ["functionResponse"] = response });
                    if (result.Image is not null) trailingImages.Add(result.Image);
                    break;
            }
        }
        foreach (var image in trailingImages) parts.Add(InlineData(image.MimeType, image.Data));
        if (parts.Count == 0) parts.Add(new JsonObject { ["text"] = " " });
        return new JsonObject { ["role"] = message.Role == ChatRole.Assistant ? "model" : "user", ["parts"] = parts };
    }

    private static JsonObject InlineData(string mime, byte[] data) => new() { ["inlineData"] = new JsonObject { ["mimeType"] = mime, ["data"] = Convert.ToBase64String(data) } };

    /// <summary>Gemini's function schema is an OpenAPI subset with upper-case type names.</summary>
    public static JsonObject ConvertSchema(JsonObject schema)
    {
        var result = new JsonObject();
        foreach (var (key, value) in schema)
        {
            if (!SchemaKeys.Contains(key) || value is null) continue;
            switch (key)
            {
                case "type": result["type"] = value.GetValue<string>().ToUpperInvariant(); break;
                case "properties" when value is JsonObject props:
                    var converted = new JsonObject();
                    foreach (var (name, child) in props) if (child is JsonObject childSchema) converted[name] = ConvertSchema(childSchema);
                    result["properties"] = converted; break;
                case "items" when value is JsonObject items: result["items"] = ConvertSchema(items); break;
                default: result[key] = value.DeepClone(); break;
            }
        }
        return result;
    }

    public override async Task<string> TranscribeAsync(byte[] wav, string languageCode, CancellationToken cancellationToken)
    {
        var language = languageCode == "he" ? "Hebrew (the speaker may mix in English words)" : languageCode;
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    InlineData("audio/wav", wav),
                    new JsonObject { ["text"] = $"Transcribe this recording exactly as spoken. Expected language: {language}. Output only the transcript text, no quotes, no comments. If there is no speech, output nothing." })
            }),
            ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = 2048 }
        };
        var model = string.IsNullOrWhiteSpace(Config.TranscriptionModel) ? Model() : Config.TranscriptionModel.Trim();
        var response = await PostAsync(model, body, cancellationToken);
        var parts = response["candidates"]?[0]?["content"]?["parts"] as JsonArray;
        return parts is null ? "" : string.Join("", parts.OfType<JsonObject>().Where(p => p["thought"]?.GetValue<bool>() != true).Select(p => p["text"]?.GetValue<string>() ?? "")).Trim();
    }

    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models?pageSize=1000");
        request.Headers.Add("x-goog-api-key", ApiKey());
        var response = await SendJsonAsync(request, cancellationToken);
        return (response["models"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(m => (m["supportedGenerationMethods"] as JsonArray)?.Any(x => x?.GetValue<string>() == "generateContent") ?? true)
            .Select(m => (m["name"]?.GetValue<string>() ?? "").Replace("models/", ""))
            .Where(n => n.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) && !n.Contains("tts") && !n.Contains("image") && !n.Contains("embedding"))
            .OrderByDescending(n => n)
            .ToList();
    }

    private async Task<JsonNode> PostAsync(string model, JsonObject body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/models/{Uri.EscapeDataString(model)}:generateContent") { Content = JsonContent(body) };
        request.Headers.Add("x-goog-api-key", ApiKey());
        return await SendJsonAsync(request, cancellationToken);
    }
}
