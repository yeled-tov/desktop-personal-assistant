using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopAssistant.Settings;

namespace DesktopAssistant.AI;

/// <summary>OpenAI chat-completions protocol: OpenAI, Groq, OpenRouter, DeepSeek, Ollama, LM Studio and others.</summary>
public sealed class OpenAiCompatibleModel(ProviderConfig config) : ChatModelBase(config)
{
    public const string Tag = "openai";

    public override bool CanTranscribe => !string.IsNullOrWhiteSpace(Config.TranscriptionModel);

    public override async Task<ModelReply> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, ModelOptions options, CancellationToken cancellationToken)
    {
        var wire = new JsonArray();
        if (!string.IsNullOrWhiteSpace(system)) wire.Add(new JsonObject { ["role"] = "system", ["content"] = system });
        foreach (var message in messages) foreach (var item in ToWire(message)) wire.Add(item);

        var body = new JsonObject { ["model"] = Model(options), ["messages"] = wire };
        if (tools.Count > 0)
        {
            var list = new JsonArray();
            foreach (var tool in tools)
                list.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = tool.Schema.DeepClone() } });
            body["tools"] = list;
        }
        if (options.JsonOutput) body["response_format"] = new JsonObject { ["type"] = "json_object" };
        if (Config.Id == "openrouter") body["max_tokens"] = options.MaxOutputTokens;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions") { Content = JsonContent(body) };
        Authorize(request);
        var response = await SendJsonAsync(request, cancellationToken);
        var message0 = response["choices"]?[0]?["message"] as JsonObject ?? throw new AiException($"{Config.DisplayName} לא החזיר תשובה.");
        var text = message0["content"] switch
        {
            JsonValue v => v.GetValue<string>(),
            JsonArray a => string.Join("", a.OfType<JsonObject>().Select(p => p["text"]?.GetValue<string>() ?? "")),
            _ => ""
        };
        var calls = new List<ToolCallPart>();
        if (message0["tool_calls"] is JsonArray toolCalls)
        {
            foreach (var call in toolCalls.OfType<JsonObject>())
            {
                var function = call["function"] as JsonObject;
                if (function is null) continue;
                JsonObject args;
                try { args = JsonNode.Parse(function["arguments"]?.GetValue<string>() ?? "{}") as JsonObject ?? new JsonObject(); }
                catch (JsonException) { args = new JsonObject(); }
                calls.Add(new ToolCallPart(call["id"]?.GetValue<string>() ?? $"call_{Guid.NewGuid():N}", function["name"]?.GetValue<string>() ?? "", args));
            }
        }
        return new ModelReply(text.Trim(), calls, null, Tag);
    }

    private static IEnumerable<JsonObject> ToWire(ChatMessage message)
    {
        if (message.Role == ChatRole.Assistant)
        {
            var assistant = new JsonObject { ["role"] = "assistant", ["content"] = message.TextContent };
            var calls = message.Parts.OfType<ToolCallPart>().ToList();
            if (calls.Count > 0)
            {
                var list = new JsonArray();
                foreach (var call in calls)
                    list.Add(new JsonObject { ["id"] = call.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments.ToJsonString() } });
                assistant["tool_calls"] = list;
            }
            yield return assistant;
            yield break;
        }

        var results = message.Parts.OfType<ToolResultPart>().ToList();
        foreach (var result in results)
            yield return new JsonObject { ["role"] = "tool", ["tool_call_id"] = result.Id, ["content"] = result.IsError ? "ERROR: " + result.Content : result.Content };

        // Images can't be attached to tool messages, so they follow in a user message.
        var content = new JsonArray();
        foreach (var image in results.Where(r => r.Image is not null).Select(r => r.Image!).Concat(message.Parts.OfType<ImagePart>()))
            content.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{image.MimeType};base64,{Convert.ToBase64String(image.Data)}" } });
        foreach (var text in message.Parts.OfType<TextPart>().Where(t => !string.IsNullOrEmpty(t.Text)))
            content.Add(new JsonObject { ["type"] = "text", ["text"] = text.Text });
        if (content.Count == 0) yield break;
        if (content.Count == 1 && content[0]?["type"]?.GetValue<string>() == "text")
            yield return new JsonObject { ["role"] = "user", ["content"] = content[0]!["text"]!.GetValue<string>() };
        else
            yield return new JsonObject { ["role"] = "user", ["content"] = content };
    }

    public override async Task<string> TranscribeAsync(byte[] wav, string languageCode, CancellationToken cancellationToken)
    {
        if (!CanTranscribe) throw new AiException($"לא הוגדר מודל זיהוי דיבור עבור {Config.DisplayName}.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/audio/transcriptions") { Content = WavForm(wav, Config.TranscriptionModel.Trim(), languageCode) };
        Authorize(request);
        var response = await SendJsonAsync(request, cancellationToken);
        return response["text"]?.GetValue<string>()?.Trim() ?? "";
    }

    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        Authorize(request);
        var response = await SendJsonAsync(request, cancellationToken);
        return (response["data"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(m => m["id"]?.GetValue<string>() ?? "")
            .Where(id => id.Length > 0 && !id.Contains("embedding") && !id.Contains("tts") && !id.StartsWith("dall-e") && !id.Contains("moderation"))
            .OrderBy(id => id)
            .ToList();
    }

    private void Authorize(HttpRequestMessage request)
    {
        var key = ApiKey();
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (Config.Id == "openrouter")
        {
            request.Headers.Add("HTTP-Referer", "https://github.com/yeled-tov/desktop-personal-assistant");
            request.Headers.Add("X-Title", "Desktop Personal Assistant");
        }
    }
}
