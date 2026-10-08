using DesktopAssistant.Automation;
using DesktopAssistant.Configuration;
using DesktopAssistant.Localization;
using DesktopAssistant.ScreenTranslation;
using DesktopAssistant.AI;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var checks = new (string Name, Action Run)[]
{
    ("classification safe open", () => Assert(new ActionSafety().Classify("פתח יוטיוב") == ActionRisk.Safe)),
    ("classification send requires confirmation", () => Assert(new ActionSafety().Classify("שלח הודעה") == ActionRisk.ConfirmationRequired)),
    ("multiplication", () => { Assert(ApplicationLauncher.TryCalculate("147 כפול 38", out var answer)); Assert(answer == "5586"); }),
    ("addition", () => { Assert(ApplicationLauncher.TryCalculate("12 ועוד 5", out var answer)); Assert(answer == "17"); }),
    ("localization Hebrew", () => Assert(UiText.Get("title") == "עוזר אישי למחשב")),
    ("localization English", () => Assert(UiText.Get("title", "en") == "Desktop Personal Assistant")),
    ("Hebrew RTL", () => Assert(UiText.IsRightToLeft("he"))),
    ("English LTR", () => Assert(!UiText.IsRightToLeft("en"))),
    ("settings defaults privacy and Gemini", () => { var s = new UserSettings(); Assert(!s.AllowScreenshotAnalysis && !s.WakeWordEnabled && s.AiMode == "GeminiOnline" && s.TranslationMode == "Gemini"); }),
    ("settings JSON round trip", () => { var settings = new UserSettings { LocalMemory = "prefers Hebrew", GeminiModel = "gemini-test" }; var json = JsonSerializer.Serialize(settings); var copy = JsonSerializer.Deserialize<UserSettings>(json)!; Assert(copy.LocalMemory == settings.LocalMemory && copy.GeminiModel == settings.GeminiModel); }),
    ("local memory is editable and rejects secrets", () => { var settings = new UserSettings(); var persisted = 0; Assert(LocalMemory.TryHandle("תזכור שאני משתמש בכרום", settings, out _, _ => persisted++)); Assert(settings.LocalMemory.Contains("כרום") && persisted == 1); Assert(LocalMemory.TryHandle("תזכור את הסיסמה שלי", settings, out var reply, _ => persisted++)); Assert(reply.Contains("לא שומר") && persisted == 1); }),
    ("OCR box model keeps coordinates", () => { var b = new OcrTextBlock("sample", new System.Windows.Rect(1, 2, 3, 4), "en", .9); Assert(b.Bounds.Width == 3 && b.Confidence == .9); }),
    ("translation model retains placement", () => { var x = new TranslationOverlayItem("hello", "שלום", new System.Windows.Rect(2, 4, 10, 8)); Assert(x.Translation == "שלום" && x.Bounds.X == 2); })
};
var failed = 0;
foreach (var (name, run) in checks) { try { run(); Console.WriteLine($"PASS {name}"); } catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); } }
Console.WriteLine($"{checks.Length - failed}/{checks.Length} checks passed");
try { await AgentLoopRunsSafeToolAsync(); Console.WriteLine("PASS bounded agent tool round trip"); } catch (Exception e) { failed++; Console.WriteLine($"FAIL bounded agent tool round trip: {e.Message}"); }
try { await AgentLoopRequiresConfirmationAsync(); Console.WriteLine("PASS agent confirmation blocks action"); } catch (Exception e) { failed++; Console.WriteLine($"FAIL agent confirmation blocks action: {e.Message}"); }
try { AgentEnterRequiresConfirmation(); Console.WriteLine("PASS Enter submission shortcut requires confirmation"); } catch (Exception e) { failed++; Console.WriteLine($"FAIL Enter submission shortcut confirmation: {e.Message}"); }
try { await MockGeminiTranslationPipelineAsync(); Console.WriteLine("PASS mocked Gemini image translation and coordinates"); } catch (Exception e) { failed++; Console.WriteLine($"FAIL mocked Gemini image translation and coordinates: {e.Message}"); }
Console.WriteLine($"{checks.Length + 4 - failed}/{checks.Length + 4} total checks passed");
return failed == 0 ? 0 : 1;
static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }

static async Task AgentLoopRunsSafeToolAsync()
{
    var invoked = false;
    var tool = new AgentTool<EchoArgs>(new AgentToolDeclaration("echo_tool", "Echo", new JsonObject { ["type"]="OBJECT", ["properties"]=new JsonObject { ["value"]=new JsonObject { ["type"]="STRING" } }, ["required"]=new JsonArray("value") }), _ => ActionRisk.Safe,
        (input, _) => { invoked = true; return Task.FromResult(new AgentToolResult(input.Value)); });
    var loop = new GeminiAgentLoop(new MockAgentModel("echo_tool", "{\"value\":\"ok\"}", "סיימתי."), new AgentToolRegistry([tool]));
    var result = await loop.RunAsync("עשה משהו", _ => Task.CompletedTask, (_, _) => Task.FromResult(false));
    Assert(invoked && result.Text == "סיימתי." && result.Steps == 2);
}

static async Task AgentLoopRequiresConfirmationAsync()
{
    var invoked = false;
    var tool = new AgentTool<EchoArgs>(new AgentToolDeclaration("send_message", "Send", new JsonObject { ["type"]="OBJECT", ["properties"]=new JsonObject { ["value"]=new JsonObject { ["type"]="STRING" } } }), _ => ActionRisk.ConfirmationRequired,
        (input, _) => { invoked = true; return Task.FromResult(new AgentToolResult("sent")); });
    var loop = new GeminiAgentLoop(new MockAgentModel("send_message", "{\"value\":\"draft\"}", "ביטלתי."), new AgentToolRegistry([tool]));
    var result = await loop.RunAsync("שלח הודעה", _ => Task.CompletedTask, (_, _) => Task.FromResult(false));
    Assert(!invoked && result.Text == "ביטלתי.");
}

static void AgentEnterRequiresConfirmation()
{
    var settings = new UserSettings();
    var gemini = new GeminiAiProvider(() => settings);
    var registry = DesktopAgentTools.Create(new ApplicationLauncher(), new DesktopUiAutomation(), gemini);
    Assert(registry.TryGet("press_key", out var pressKey));
    using var enter = JsonDocument.Parse("{\"key\":\"ENTER\"}");
    using var shortcut = JsonDocument.Parse("{\"key\":\"CTRL+ENTER\"}");
    using var safe = JsonDocument.Parse("{\"key\":\"CTRL+L\"}");
    Assert(pressKey.GetRisk(enter.RootElement) == ActionRisk.ConfirmationRequired);
    Assert(pressKey.GetRisk(shortcut.RootElement) == ActionRisk.ConfirmationRequired);
    Assert(pressKey.GetRisk(safe.RootElement) == ActionRisk.Safe);
}

static async Task MockGeminiTranslationPipelineAsync()
{
    var pixels = new byte[4 * 4 * 4];
    var image = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16); image.Freeze();
    var frame = new ScreenFrame(image, new System.Windows.Rect(0, 0, 4, 4));
    var fake = new MockVisionProvider();
    var output = await fake.TranslateScreenshotAsync(frame, "he");
    var valid = TranslationResultValidator.ValidateRegions(output.Regions);
    Assert(valid.Count == 2 && valid[0].TranslatedText == "הגדרות" && valid[1].OriginalText == "设置");
    Assert(valid.All(region => region.Left >= 0 && region.Right <= 1));
}

sealed record EchoArgs(string Value);

sealed class MockAgentModel(string tool, string args, string finalText) : IAgentModel
{
    private int _calls;
    public Task<AgentModelTurn> GenerateAgentTurnAsync(JsonArray contents, IReadOnlyList<AgentToolDeclaration> tools, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _calls) == 1)
        {
            using var doc = JsonDocument.Parse(args);
            var part = new JsonObject { ["functionCall"]=new JsonObject { ["name"]=tool, ["args"]=JsonNode.Parse(args), ["id"]="mock-1" } };
            return Task.FromResult(new AgentModelTurn("", new JsonObject { ["role"]="model", ["parts"]=new JsonArray(part) }, [new AgentToolCall(tool, doc.RootElement.Clone(), "mock-1")]));
        }
        return Task.FromResult(new AgentModelTurn(finalText, new JsonObject { ["role"]="model", ["parts"]=new JsonArray(new JsonObject { ["text"]=finalText }) }, []));
    }
}

sealed class MockVisionProvider : IGeminiVisionProvider
{
    public Task<ScreenTranslationResult> TranslateScreenshotAsync(ScreenFrame frame, string targetLanguage, CancellationToken cancellationToken = default) => Task.FromResult(new ScreenTranslationResult("en+zh", targetLanguage,
    [new("Settings", "הגדרות", .1, .1, .4, .2), new("设置", "הגדרות", .5, .1, .9, .2), new("bad box", "לא מוצג", -.1, .2, .2, .3)]));
    public Task<string> AnalyzeScreenshotAsync(ScreenFrame frame, string question, CancellationToken cancellationToken = default) => Task.FromResult("ממשק בדיקה עם טקסט באנגלית ובסינית.");
}
