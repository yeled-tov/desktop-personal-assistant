using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;

namespace DesktopAssistant.Overlay;

public sealed record TranslatedRegion(string Original, string Translated, double Left, double Top, double Right, double Bottom);
public sealed record SelectionInsight(string Answer, string SearchQuery, string Text);

/// <summary>Vision features for the screen overlay: in-place translation, "what is this", text extraction.</summary>
public static class ScreenVision
{
    public static async Task<IReadOnlyList<TranslatedRegion>> TranslateAsync(IChatModel model, BitmapSource image, string targetLanguage, CancellationToken cancellationToken)
    {
        // Offline (and as a cheaper path): Windows reads the text locally, the model only translates text.
        if (model.Config.Id == "offline")
        {
            var viaOcr = await TranslateViaOcrAsync(model, image, targetLanguage, cancellationToken);
            if (viaOcr is not null) return viaOcr;
        }
        var prompt = $$"""
            Find every piece of readable text in this screenshot and translate it to {{targetLanguage}}.
            Return JSON only: {"regions":[{"o":"original text","t":"translation","b":[left,top,right,bottom]}]}
            - b = bounding box normalized 0..1 relative to the whole image (left/top/right/bottom).
            - One region per line or per UI label/paragraph; keep separate labels separate.
            - Skip text that is already in {{targetLanguage}}, and skip numbers-only text.
            - Translate naturally and concisely (UI labels stay short). Do not invent text.
            """;
        var reply = await model.CompleteAsync("", [ChatMessage.User(prompt, new ImagePart(ScreenCapture.EncodeForModel(image)))], [], new ModelOptions(JsonOutput: true, MaxOutputTokens: 16000), cancellationToken);
        var root = ParseJson(reply.Text);
        var regions = new List<TranslatedRegion>();
        foreach (var item in (root?["regions"] as JsonArray ?? []).OfType<JsonObject>().Take(400))
        {
            var box = item["b"] as JsonArray;
            if (box is null || box.Count != 4) continue;
            double V(int i) { try { return box[i]!.GetValue<double>(); } catch { return double.NaN; } }
            double l = V(0), t = V(1), r = V(2), b = V(3);
            // Some models answer in 0..1000 instead of 0..1.
            if (r > 1.5 || b > 1.5) { l /= 1000; t /= 1000; r /= 1000; b /= 1000; }
            if (!(double.IsFinite(l) && double.IsFinite(t) && double.IsFinite(r) && double.IsFinite(b)) || r <= l || b <= t) continue;
            var translated = item["t"]?.ToString().Trim() ?? "";
            if (translated.Length == 0) continue;
            regions.Add(new TranslatedRegion(item["o"]?.ToString() ?? "", translated, Math.Clamp(l, 0, 1), Math.Clamp(t, 0, 1), Math.Clamp(r, 0, 1), Math.Clamp(b, 0, 1)));
        }
        return regions;
    }

    public static async Task<SelectionInsight> DescribeAsync(IChatModel model, BitmapSource selection, BitmapSource? fullScreen, string? question, CancellationToken cancellationToken)
    {
        var task = string.IsNullOrWhiteSpace(question)
            ? "The user circled this part of their screen (like Google's Circle to Search). Identify what it is and give the most useful short answer: what it is, key facts, and if it is text — what it says/means. If it is a product, place, person in the news, plant, error message, code, math, etc. — explain it."
            : $"The user circled this part of their screen and asks: \"{question}\". Answer that.";
        var prompt = $$"""
            {{task}}
            Reply in Hebrew (unless the question is in another language), concise (max ~120 words), plain text.
            Return JSON only: {"answer":"...","search_query":"best Google search query for this, in the most useful language","text":"all text visible in the selection, verbatim, or empty"}
            """;
        var parts = new List<ImagePart> { new(ScreenCapture.EncodeForModel(selection, 1600)) };
        if (fullScreen is not null) parts.Add(new ImagePart(ScreenCapture.EncodeForModel(fullScreen, 1280)));
        var text = fullScreen is null ? prompt : prompt + "\n(The first image is the selection; the second is the whole screen, for context only.)";
        var reply = await model.CompleteAsync("", [ChatMessage.User(text, [.. parts])], [], new ModelOptions(JsonOutput: true, MaxOutputTokens: 4000), cancellationToken);
        var root = ParseJson(reply.Text);
        if (root is null) return new SelectionInsight(reply.Text, "", "");
        return new SelectionInsight(root["answer"]?.ToString() ?? "", root["search_query"]?.ToString() ?? "", root["text"]?.ToString() ?? "");
    }

    public static async Task<string> FollowUpAsync(IChatModel model, BitmapSource selection, string question, CancellationToken cancellationToken)
    {
        var reply = await model.CompleteAsync("Answer about the image the user circled on their screen. Reply in the user's language, concise, plain text.",
            [ChatMessage.User(question, new ImagePart(ScreenCapture.EncodeForModel(selection, 1600)))], [], new ModelOptions(MaxOutputTokens: 4000), cancellationToken);
        return reply.Text;
    }

    /// <summary>
    /// Windows OCR finds the lines and their positions on the PC; the model translates only the text.
    /// Returns null when Windows has no OCR language installed (then the image path is used).
    /// </summary>
    public static async Task<IReadOnlyList<TranslatedRegion>?> TranslateViaOcrAsync(IChatModel model, BitmapSource image, string targetLanguage, CancellationToken cancellationToken)
    {
        var lines = await OcrLinesAsync(image);
        if (lines is null) return null;
        // Only what needs translating: skip lines already in the target script, symbols/numbers, and duplicates.
        var targetIsHebrew = targetLanguage.Contains("עברית") || targetLanguage.StartsWith("he", StringComparison.OrdinalIgnoreCase);
        lines = lines.Where(l => l.Text.Count(char.IsLetter) >= 2 && !(targetIsHebrew && l.Text.Any(c => c >= 'א' && c <= 'ת'))).Take(120).ToList();
        if (lines.Count == 0) return [];
        var results = new List<TranslatedRegion>();
        var cache = new Dictionary<string, string>();
        foreach (var chunk in lines.DistinctBy(l => l.Text).Chunk(20))
        {
            var input = new System.Text.Json.Nodes.JsonArray(chunk.Select(l => (System.Text.Json.Nodes.JsonNode?)l.Text).ToArray());
            var prompt = $$"""
                Translate each string in this JSON array to {{targetLanguage}}. Keep the same order and count. UI labels stay short.
                If a string is already in {{targetLanguage}} or is only numbers/symbols, copy it unchanged.
                Return JSON only: {"t":["...", "..."]}
                {{input.ToJsonString()}}
                """;
            // A tight output budget keeps a small local model from rambling (and the screen from waiting).
            var budget = 80 + chunk.Sum(l => l.Text.Length) * 2;
            var reply = await model.CompleteAsync("", [ChatMessage.User(prompt)], [], new ModelOptions(JsonOutput: true, MaxOutputTokens: Math.Min(3000, budget)), cancellationToken);
            var translated = ParseJson(reply.Text)?["t"] as JsonArray;
            for (var i = 0; i < chunk.Length; i++)
            {
                var text = translated is not null && i < translated.Count ? translated[i]?.ToString() ?? "" : "";
                if (text.Length > 0 && text != chunk[i].Text) cache[chunk[i].Text] = text;
            }
        }
        foreach (var line in lines)
            if (cache.TryGetValue(line.Text, out var text)) results.Add(new TranslatedRegion(line.Text, text, line.Left, line.Top, line.Right, line.Bottom));
        return results;
    }

    private sealed record OcrLine(string Text, double Left, double Top, double Right, double Bottom);

    private static async Task<List<OcrLine>?> OcrLinesAsync(BitmapSource image)
    {
        try
        {
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                         ?? Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            if (engine is null) return null;
            var png = ScreenCapture.EncodePng(image);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            double w = image.PixelWidth, h = image.PixelHeight;
            var lines = new List<OcrLine>();
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text)) continue;
                var l = line.Words.Min(x => x.BoundingRect.Left); var t = line.Words.Min(x => x.BoundingRect.Top);
                var r = line.Words.Max(x => x.BoundingRect.Right); var b = line.Words.Max(x => x.BoundingRect.Bottom);
                lines.Add(new OcrLine(line.Text, l / w, t / h, r / w, b / h));
            }
            return lines;
        }
        catch (Exception ex) { AppLog.Warn("OCR lines failed: " + ex.Message); return null; }
    }

    /// <summary>Local Windows OCR (no internet). Returns null when no OCR language is installed.</summary>
    public static async Task<string?> OcrAsync(BitmapSource image)
    {
        try
        {
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null) return null;
            var png = ScreenCapture.EncodePng(image);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            return string.Join("\n", result.Lines.Select(l => l.Text));
        }
        catch (Exception ex) { AppLog.Warn("Windows OCR failed: " + ex.Message); return null; }
    }

    private static JsonNode? ParseJson(string text)
    {
        var t = ChatModelBase.StripCodeFence(text);
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try { return JsonNode.Parse(t[start..(end + 1)]); }
        catch (JsonException) { return null; }
    }
}
