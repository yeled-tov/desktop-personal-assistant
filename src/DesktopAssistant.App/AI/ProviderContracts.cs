namespace DesktopAssistant.AI;

public sealed record ConversationMessage(string Role, string Content);
public sealed record AiReply(string Text);
public interface IAiProvider { string Name { get; } Task<AiReply> ReplyAsync(IReadOnlyList<ConversationMessage> history, CancellationToken cancellationToken = default); }
public sealed record AgentToolCall(string Name, System.Text.Json.JsonElement Arguments, string? Id);
public sealed record AgentModelTurn(string Text, System.Text.Json.Nodes.JsonObject ModelContent, IReadOnlyList<AgentToolCall> ToolCalls);
public interface IAgentModel { Task<AgentModelTurn> GenerateAgentTurnAsync(System.Text.Json.Nodes.JsonArray contents, IReadOnlyList<AgentToolDeclaration> tools, CancellationToken cancellationToken = default); }
public interface ISpeechToTextProvider { string Name { get; } Task<string> RecognizeAsync(Stream audio, string? language = null, CancellationToken cancellationToken = default); }
public interface IWakeWordProvider { bool IsAvailable { get; } Task StartAsync(Func<Task> onWake, CancellationToken cancellationToken = default); Task StopAsync(); }
public interface ITextToSpeechProvider { string Name { get; } Task SpeakAsync(string text, CancellationToken cancellationToken = default); }
public interface IEmailProvider { Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken cancellationToken = default); Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default); }
public interface ICalendarProvider { Task<IReadOnlyList<string>> GetUpcomingAsync(CancellationToken cancellationToken = default); }

public sealed class UnavailableAiProvider : IAiProvider
{
    public string Name => "לא מוגדר";
    public Task<AiReply> ReplyAsync(IReadOnlyList<ConversationMessage> history, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiReply("לא הוגדר ספק AI. הפעולות המקומיות ממשיכות לעבוד."));
}

public sealed class GeminiAiProvider(Func<DesktopAssistant.Configuration.UserSettings> getSettings) : IAiProvider, IAgentModel, DesktopAssistant.ScreenTranslation.IGeminiVisionProvider
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(35) };
    private const string ApiRoot = "https://generativelanguage.googleapis.com/v1beta/models/";
    public string Name => "Google Gemini";

    public async Task<AiReply> ReplyAsync(IReadOnlyList<ConversationMessage> history, CancellationToken cancellationToken = default)
    {
        var settings = getSettings();
        if (!CredentialStore.TryGetApiKey(out var apiKey)) throw new InvalidOperationException("לא נשמר מפתח Gemini במנהל האישורים של Windows.");
        var contents = new System.Text.Json.Nodes.JsonArray();
        foreach (var item in history.TakeLast(14)) contents.Add(new System.Text.Json.Nodes.JsonObject { ["role"] = item.Role == "assistant" ? "model" : "user", ["parts"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["text"] = item.Content }) });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { systemInstruction = new { parts = new[] { new { text = "אתה עוזר אישי למחשב. ענה בעברית כברירת מחדל. אל תטען שביצעת פעולות שלא נמסר לך שבוצעו." } } }, contents });
        using var request = CreateRequest(settings.GeminiModel, apiKey, payload);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw UserFacingError(response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var text = ExtractText(document.RootElement);
        return new AiReply(string.IsNullOrWhiteSpace(text) ? "לא התקבלה תשובה מ-Gemini." : text);
    }

    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = getSettings();
        if (!CredentialStore.TryGetApiKey(out var apiKey)) throw new InvalidOperationException("לא נשמר מפתח Gemini. שמור מפתח API לפני בדיקת החיבור.");
        var payload = System.Text.Json.JsonSerializer.Serialize(new { contents = new[] { new { role = "user", parts = new[] { new { text = "Reply with OK." } } } }, generationConfig = new { maxOutputTokens = 8 } });
        using var request = CreateRequest(settings.GeminiModel, apiKey, payload);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw UserFacingError(response.StatusCode);
        return "✓ Gemini מחובר בהצלחה";
    }

    public async Task<AgentModelTurn> GenerateAgentTurnAsync(System.Text.Json.Nodes.JsonArray contents, IReadOnlyList<AgentToolDeclaration> tools, CancellationToken cancellationToken = default)
    {
        var settings = getSettings();
        if (!CredentialStore.TryGetApiKey(out var apiKey)) throw new InvalidOperationException("לא נשמר מפתח Gemini במנהל האישורים של Windows.");
        var declarations = tools.Select(tool => new { name = tool.Name, description = tool.Description, parameters = tool.Parameters }).ToArray();
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = "אתה סוכן מחשב למשתמש. השתמש בכלים המוצהרים לביצוע פעולות אמיתיות. הצג למשתמש רק עדכוני סטטוס קצרים ותשובה סופית, ללא נימוק פנימי. בצע רק פעולה שהמשתמש ביקש. בקשות שליחה, מחיקה, רכישה או שינוי מערכת חייבות לעבור אישור מפורש לפני ביצוע. ענה בעברית כברירת מחדל." } } },
            contents,
            tools = new[] { new { functionDeclarations = declarations } },
            toolConfig = new { functionCallingConfig = new { mode = "AUTO" } }
        });
        using var request = CreateRequest(settings.GeminiModel, apiKey, payload);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw UserFacingError(response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var content = document.RootElement.GetProperty("candidates")[0].GetProperty("content");
        var contentNode = System.Text.Json.Nodes.JsonNode.Parse(content.GetRawText()) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
        var calls = new List<AgentToolCall>(); var texts = new List<string>();
        foreach (var part in content.GetProperty("parts").EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text)) texts.Add(text.GetString() ?? "");
            if (part.TryGetProperty("functionCall", out var function))
                calls.Add(new AgentToolCall(function.GetProperty("name").GetString() ?? "", function.TryGetProperty("args", out var args) ? args.Clone() : System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(), function.TryGetProperty("id", out var id) ? id.GetString() : null));
        }
        return new AgentModelTurn(string.Join("\n", texts).Trim(), contentNode, calls);
    }

    public async Task<DesktopAssistant.ScreenTranslation.ScreenTranslationResult> TranslateScreenshotAsync(DesktopAssistant.ScreenTranslation.ScreenFrame frame, string targetLanguage, CancellationToken cancellationToken = default)
    {
        var settings = getSettings();
        if (!CredentialStore.TryGetApiKey(out var apiKey)) throw new InvalidOperationException("לא נשמר מפתח Gemini במנהל האישורים של Windows.");
        var imageBytes = EncodeScreenshot(frame.Image);
        if (imageBytes.Length > 19 * 1024 * 1024) throw new InvalidOperationException("צילום המסך גדול מדי לשליחה. צמצם את שטח המסך או את מספר המסכים ונסה שוב.");
        var schema = new
        {
            type = "OBJECT", required = new[] { "sourceLanguage", "targetLanguage", "regions" },
            properties = new
            {
                sourceLanguage = new { type = "STRING" }, targetLanguage = new { type = "STRING" },
                regions = new { type = "ARRAY", items = new { type = "OBJECT", required = new[] { "originalText", "translatedText", "box" }, properties = new
                    {
                        originalText = new { type = "STRING" }, translatedText = new { type = "STRING" },
                        box = new { type = "OBJECT", required = new[] { "left", "top", "right", "bottom" }, properties = new { left = new { type = "NUMBER" }, top = new { type = "NUMBER" }, right = new { type = "NUMBER" }, bottom = new { type = "NUMBER" } } }
                    } } }
            }
        };
        var qualityGuidance = settings.TranslationQuality switch { "Fast" => "Prioritize short common UI labels and keep regions compact.", "Accurate" => "Carefully preserve punctuation, numbers, meaning, and mixed-language boundaries.", _ => "Balance translation quality with speed; preserve meaning and concise UI phrasing." };
        var prompt = $"Read all visible text in this screenshot, including mixed languages, and translate every text region to {targetLanguage}. {qualityGuidance} Return only structured JSON matching the schema. For each region, box.left/top/right/bottom must be normalized image coordinates from 0 to 1, relative to the full image. Keep separate nearby labels separate, preserve line grouping where practical, and do not invent text.";
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            contents = new[] { new { role = "user", parts = new object[] { new { text = prompt }, new { inlineData = new { mimeType = "image/jpeg", data = Convert.ToBase64String(imageBytes) } } } },
            generationConfig = new { responseMimeType = "application/json", responseSchema = schema, maxOutputTokens = settings.TranslationQuality switch { "Fast" => 6000, "Accurate" => 16000, _ => 12000 } }
        });
        using var request = CreateRequest(settings.GeminiVisionModel, apiKey, payload);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw UserFacingError(response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var json = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts").EnumerateArray().First(part => part.TryGetProperty("text", out _)).GetProperty("text").GetString() ?? "{}";
        using var result = System.Text.Json.JsonDocument.Parse(json);
        var root = result.RootElement;
        var regions = new List<DesktopAssistant.ScreenTranslation.NormalizedScreenRegion>();
        foreach (var item in root.GetProperty("regions").EnumerateArray().Take(250))
        {
            var original = item.GetProperty("originalText").GetString()?.Trim() ?? "";
            var translated = item.GetProperty("translatedText").GetString()?.Trim() ?? "";
            var box = item.GetProperty("box");
            var left = box.GetProperty("left").GetDouble(); var top = box.GetProperty("top").GetDouble();
            var right = box.GetProperty("right").GetDouble(); var bottom = box.GetProperty("bottom").GetDouble();
            regions.Add(new(original, translated, left, top, right, bottom));
        }
        return new DesktopAssistant.ScreenTranslation.ScreenTranslationResult(root.TryGetProperty("sourceLanguage", out var source) ? source.GetString() ?? "auto" : "auto", targetLanguage, DesktopAssistant.ScreenTranslation.TranslationResultValidator.ValidateRegions(regions));
    }

    public async Task<string> AnalyzeScreenshotAsync(DesktopAssistant.ScreenTranslation.ScreenFrame frame, string question, CancellationToken cancellationToken = default)
    {
        var settings = getSettings();
        if (!CredentialStore.TryGetApiKey(out var apiKey)) throw new InvalidOperationException("לא נשמר מפתח Gemini במנהל האישורים של Windows.");
        var bytes = EncodeScreenshot(frame.Image);
        var prompt = "Answer the user's question about this screenshot in Hebrew. Be concise, describe only visible facts, and do not pretend to click anything. Question: " + question;
        var payload = System.Text.Json.JsonSerializer.Serialize(new { contents = new[] { new { role="user", parts = new object[] { new { text=prompt }, new { inlineData = new { mimeType="image/jpeg", data=Convert.ToBase64String(bytes) } } } } });
        using var request = CreateRequest(settings.GeminiVisionModel, apiKey, payload);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw UserFacingError(response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        return ExtractText(doc.RootElement) ?? "לא התקבלה תשובה מ-Gemini.";
    }

    private static byte[] EncodeScreenshot(System.Windows.Media.Imaging.BitmapSource source)
    {
        var image = source;
        if (source.PixelWidth > 3200 || source.PixelHeight > 3200)
        {
            var scale = Math.Min(3200d / source.PixelWidth, 3200d / source.PixelHeight);
            image = new System.Windows.Media.Imaging.TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
            image.Freeze();
        }
        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 82 };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    private static HttpRequestMessage CreateRequest(string model, string apiKey, string payload)
    {
        var safeModel = Uri.EscapeDataString(string.IsNullOrWhiteSpace(model) ? "gemini-3.8-flash" : model.Trim());
        var request = new HttpRequestMessage(HttpMethod.Post, ApiRoot + safeModel + ":generateContent") { Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Add("x-goog-api-key", apiKey);
        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        try { return await Client.SendAsync(request, token); }
        catch (HttpRequestException) { throw new InvalidOperationException("לא ניתן להתחבר כרגע ל-Gemini. בדוק את חיבור האינטרנט."); }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("הבקשה ל-Gemini ארכה מדי. נסה שוב בעוד רגע."); }
    }

    private static string? ExtractText(System.Text.Json.JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0) return null;
            if (!candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) return null;
            return parts.EnumerateArray().Where(part => part.TryGetProperty("text", out _)).Select(part => part.GetProperty("text").GetString()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static InvalidOperationException UserFacingError(System.Net.HttpStatusCode code) => code switch
    {
        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => new("מפתח Gemini אינו תקין או שאין לו הרשאה. בדוק את מפתח ה-API."),
        System.Net.HttpStatusCode.TooManyRequests => new("מכסת Gemini או קצב הבקשות הוגבלו. נסה שוב מאוחר יותר."),
        System.Net.HttpStatusCode.NotFound => new("מודל Gemini לא נמצא. בדוק את שם המודל בהגדרות."),
        System.Net.HttpStatusCode.BadRequest => new("הבקשה ל-Gemini נדחתה. בדוק את מפתח ה-API, שם המודל או מבנה הבקשה."),
        _ => new($"לא ניתן להתחבר כרגע ל-Gemini (שגיאה {(int)code}). בדוק את חיבור האינטרנט או את שם המודל.")
    };
}
