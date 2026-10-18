using System.Text.Json.Nodes;
using DesktopAssistant.Settings;

namespace DesktopAssistant.AI;

/// <summary>Anthropic Messages API (Claude).</summary>
public sealed class AnthropicModel(ProviderConfig config) : ChatModelBase(config)
{
    public const string Tag = "anthropic";

    public override bool CanTranscribe => false;

    public override async Task<ModelReply> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, ModelOptions options, CancellationToken cancellationToken)
    {
        var wire = new JsonArray();
        foreach (var message in messages)
        {
            var content = ToBlocks(message);
            if (content.Count == 0) continue;
            var role = message.Role == ChatRole.Assistant ? "assistant" : "user";
            // The API requires alternating roles; merge consecutive messages of the same role.
            if (wire.Count > 0 && wire[^1]?["role"]?.GetValue<string>() == role && wire[^1]?["content"] is JsonArray previous)
                foreach (var block in content.ToList()) { content.Remove(block); previous.Add(block); }
            else
                wire.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }

        var system0 = system;
        if (options.JsonOutput) system0 += "\n\nReply with a single valid JSON object only — no prose, no code fences.";
        var body = new JsonObject { ["model"] = Model(options), ["max_tokens"] = options.MaxOutputTokens, ["messages"] = wire };
        if (!string.IsNullOrWhiteSpace(system0)) body["system"] = system0;
        if (tools.Count > 0)
        {
            var list = new JsonArray();
            foreach (var tool in tools) list.Add(new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["input_schema"] = tool.Schema.DeepClone() });
            body["tools"] = list;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/messages") { Content = JsonContent(body) };
        Authorize(request);
        var response = await SendJsonAsync(request, cancellationToken);
        var texts = new List<string>();
        var calls = new List<ToolCallPart>();
        foreach (var block in (response["content"] as JsonArray ?? []).OfType<JsonObject>())
        {
            switch (block["type"]?.GetValue<string>())
            {
                case "text": texts.Add(block["text"]?.GetValue<string>() ?? ""); break;
                case "tool_use":
                    calls.Add(new ToolCallPart(block["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"), block["name"]?.GetValue<string>() ?? "", (block["input"] as JsonObject)?.DeepClone() as JsonObject ?? new JsonObject()));
                    break;
            }
        }
        var text = string.Join("", texts).Trim();
        if (options.JsonOutput) text = StripCodeFence(text);
        return new ModelReply(text, calls, null, Tag);
    }

    private static JsonArray ToBlocks(ChatMessage message)
    {
        var blocks = new JsonArray();
        foreach (var part in message.Parts)
        {
            switch (part)
            {
                case ImagePart image: blocks.Add(Image(image)); break;
                case TextPart text when !string.IsNullOrWhiteSpace(text.Text): blocks.Add(new JsonObject { ["type"] = "text", ["text"] = text.Text }); break;
                case ToolCallPart call: blocks.Add(new JsonObject { ["type"] = "tool_use", ["id"] = call.Id, ["name"] = call.Name, ["input"] = call.Arguments.DeepClone() }); break;
                case ToolResultPart result:
                    var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = string.IsNullOrEmpty(result.Content) ? "(empty)" : result.Content });
                    if (result.Image is not null) content.Add(Image(result.Image));
                    blocks.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = result.Id, ["content"] = content, ["is_error"] = result.IsError });
                    break;
            }
        }
        // tool_result blocks must come first in a user turn.
        var ordered = blocks.OrderBy(b => b?["type"]?.GetValue<string>() == "tool_result" ? 0 : 1).Select(b => b!.DeepClone()).ToArray();
        return new JsonArray(ordered);
    }

    private static JsonObject Image(ImagePart image) => new()
    {
        ["type"] = "image",
        ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MimeType, ["data"] = Convert.ToBase64String(image.Data) }
    };

    public override Task<string> TranscribeAsync(byte[] wav, string languageCode, CancellationToken cancellationToken, string? hint = null) =>
        throw new AiException("Claude לא תומך בזיהוי דיבור. כדי לדבר עם העוזר הוסף גם מפתח של Gemini, OpenAI או Groq בהגדרות (הם ישמשו רק לתמלול).");

    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models?limit=100");
        Authorize(request);
        var response = await SendJsonAsync(request, cancellationToken);
        return (response["data"] as JsonArray ?? []).OfType<JsonObject>().Select(m => m["id"]?.GetValue<string>() ?? "").Where(id => id.Length > 0).ToList();
    }

    private void Authorize(HttpRequestMessage request)
    {
        request.Headers.Add("x-api-key", ApiKey());
        request.Headers.Add("anthropic-version", "2023-06-01");
    }
}
