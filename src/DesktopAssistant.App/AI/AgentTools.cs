using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopAssistant.Automation;

namespace DesktopAssistant.AI;

public sealed record AgentToolDeclaration(string Name, string Description, JsonObject Parameters);
public sealed record AgentToolResult(string Result, bool Succeeded = true);

public interface IAgentTool
{
    AgentToolDeclaration Declaration { get; }
    ActionRisk GetRisk(JsonElement arguments);
    string GetConfirmationText(JsonElement arguments);
    Task<AgentToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
    void ConfirmAccepted();
}

/// <summary>A Gemini-callable tool with a concrete input type and a typed result contract.</summary>
public sealed class AgentTool<TInput>(AgentToolDeclaration declaration, Func<TInput, ActionRisk> risk, Func<TInput, CancellationToken, Task<AgentToolResult>> execute, Action? onConfirmed = null, Func<TInput, string>? confirmationText = null) : IAgentTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public AgentToolDeclaration Declaration { get; } = declaration;
    public ActionRisk GetRisk(JsonElement arguments) => risk(Deserialize(arguments));
    public string GetConfirmationText(JsonElement arguments) => confirmationText?.Invoke(Deserialize(arguments)) ?? "";
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken) => execute(Deserialize(arguments), cancellationToken);
    public void ConfirmAccepted() => onConfirmed?.Invoke();
    private static TInput Deserialize(JsonElement element) => element.Deserialize<TInput>(JsonOptions) ?? throw new InvalidOperationException("המודל שלח קלט כלי לא תקין.");
}

public sealed class AgentToolRegistry(IEnumerable<IAgentTool> tools)
{
    private readonly Dictionary<string, IAgentTool> _tools = tools.ToDictionary(tool => tool.Declaration.Name, StringComparer.Ordinal);
    public IReadOnlyList<AgentToolDeclaration> Declarations => _tools.Values.Select(tool => tool.Declaration).ToArray();
    public bool TryGet(string name, out IAgentTool tool) => _tools.TryGetValue(name, out tool!);
}

public sealed record OpenApplicationArgs(string Application);
public sealed record OpenUrlArgs(string Url);
public sealed record SearchArgs(string Query);
public sealed record CalculateArgs(string Expression);
public sealed record WindowNameArgs(string Title);
public sealed record WaitArgs(int Milliseconds);
public sealed record InspectUiArgs(string? WindowTitle = null, int MaxItems = 80);
public sealed record ClickUiArgs(string ElementName);
public sealed record TypeTextArgs(string ElementName, string Text);
public sealed record PressKeyArgs(string Key);
public sealed record FocusWindowArgs(string WindowTitle);
public sealed record AnalyzeScreenArgs(string Question);
public sealed record ScrollArgs(string Direction, int Amount = 1);
public sealed record ScreenshotArgs();

public static class DesktopAgentTools
{
    public static AgentToolRegistry Create(ApplicationLauncher launcher, DesktopUiAutomation ui, DesktopAssistant.AI.GeminiAiProvider gemini)
    {
        var safe = _ => ActionRisk.Safe;
        return new AgentToolRegistry([
            new AgentTool<OpenApplicationArgs>(Def("open_application", "Open one of the user's installed applications or a supported website by friendly name.", "application", "string"), safe,
                (input, ct) => Run(() => launcher.OpenFriendly(input.Application), ct)),
            new AgentTool<OpenUrlArgs>(Def("open_url", "Open a user-requested HTTP or HTTPS website.", "url", "string"), safe,
                (input, ct) => Run(() => { launcher.OpenUrl(input.Url); return "האתר נפתח."; }, ct)),
            new AgentTool<SearchArgs>(Def("search_web", "Search the web for the supplied query.", "query", "string"), safe,
                (input, ct) => Run(() => launcher.Search(input.Query), ct)),
            new AgentTool<SearchArgs>(Def("search_youtube", "Search YouTube for the supplied query.", "query", "string"), safe,
                (input, ct) => Run(() => launcher.Search(input.Query, youtube: true), ct)),
            new AgentTool<CalculateArgs>(Def("calculate", "Calculate a simple two-number expression.", "expression", "string"), safe,
                (input, ct) => Run(() => ApplicationLauncher.TryCalculate(input.Expression, out var answer) ? answer : throw new ArgumentException("הביטוי לא נתמך."), ct)),
            new AgentTool<WindowNameArgs>(Def("close_window", "Ask a visible window with the given title to close. Do not use for unsaved work unless the user asked.", "title", "string"), safe,
                (input, ct) => Run(() => WindowActions.TryCloseWindow(input.Title) ? "חלון היעד התבקש להיסגר." : "לא נמצא חלון בשם הזה.", ct)),
            new AgentTool<WaitArgs>(new AgentToolDeclaration("wait", "Wait a short time for a requested page or application to load.", new JsonObject { ["type"]="OBJECT", ["properties"]=new JsonObject { ["milliseconds"]=new JsonObject { ["type"]="INTEGER", ["minimum"]=0, ["maximum"]=10000 } }, ["required"]=new JsonArray("milliseconds") }), safe,
                async (input, ct) => { var ms = Math.Clamp(input.Milliseconds, 0, 10000); await Task.Delay(ms, ct); return new AgentToolResult($"המתנתי {ms} מילישניות."); }),
            new AgentTool<InspectUiArgs>(DefWithOptional("inspect_ui", "Inspect accessible controls in the current or named window. Use this before clicking.", new JsonObject { ["windowTitle"]=new JsonObject { ["type"]="STRING" }, ["maxItems"]=new JsonObject { ["type"]="INTEGER" } }), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.Inspect(input.WindowTitle, input.MaxItems)); }, ct)),
            new AgentTool<InspectUiArgs>(DefWithOptional("read_active_window", "Read visible accessible text in the active or named window.", new JsonObject { ["windowTitle"]=new JsonObject { ["type"]="STRING" } }), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.ReadVisibleText(input.WindowTitle)); }, ct)),
            new AgentTool<InspectUiArgs>(DefWithOptional("read_screen", "Read visible accessible text in the active or named window.", new JsonObject { ["windowTitle"]=new JsonObject { ["type"]="STRING" } }), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.ReadVisibleText(input.WindowTitle)); }, ct)),
            new AgentTool<ClickUiArgs>(Def("click_ui_element", "Click or activate a UI element by its accessible name. Never guess; inspect_ui first. Confirm action buttons like submit, continue, save, or send.", "elementName", "string"), input => IsSensitiveButton(input.ElementName) ? ActionRisk.ConfirmationRequired : new ActionSafety().Classify(input.ElementName),
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.ClickByName(input.ElementName)); }, ct), confirmationText: input =>
                {
                    var details = ui.ReadVisibleFormDetails();
                    return string.IsNullOrWhiteSpace(details) ? $"אני עומד ללחוץ על '{input.ElementName}'. לא זיהיתי פרטי טופס גלויים." : $"אני עומד ללחוץ על '{input.ElementName}' עם הפרטים הגלויים הבאים:\n{details}\n\nלבצע?";
                }),
            new AgentTool<TypeTextArgs>(DefMany("type_text", "Type text into a named accessible edit field; never use this to submit or send.", new JsonObject { ["elementName"]=new JsonObject { ["type"]="STRING" }, ["text"]=new JsonObject { ["type"]="STRING" } }, "elementName", "text"), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.TypeInto(input.ElementName, input.Text)); }, ct)),
            new AgentTool<PressKeyArgs>(Def("press_key", "Press an approved key or shortcut, for example ENTER, ESC, CTRL+L or TAB. Confirm ENTER when it may submit a form.", "key", "string"),
                input => MaySubmit(input.Key) ? ActionRisk.ConfirmationRequired : ActionRisk.Safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.PressKey(input.Key)); }, ct),
                confirmationText: input => DescribeKeyConfirmation(input.Key, ui)),
            new AgentTool<PressKeyArgs>(Def("press_hotkey", "Press a supported keyboard shortcut such as CTRL+L. Confirm ENTER shortcuts that may submit a form.", "key", "string"),
                input => MaySubmit(input.Key) ? ActionRisk.ConfirmationRequired : ActionRisk.Safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.PressKey(input.Key)); }, ct),
                confirmationText: input => DescribeKeyConfirmation(input.Key, ui)),
            new AgentTool<ScrollArgs>(DefMany("scroll", "Scroll the active window a small number of pages or lines.", new JsonObject { ["direction"]=new JsonObject { ["type"]="STRING", ["enum"]=new JsonArray("up", "down") }, ["amount"]=new JsonObject { ["type"]="INTEGER", ["minimum"]=1, ["maximum"]=8 } }, "direction"), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); var key = input.Direction.Equals("up", StringComparison.OrdinalIgnoreCase) ? "PAGEUP" : "PAGEDOWN"; var count = Math.Clamp(input.Amount, 1, 8); for (var i = 0; i < count; i++) ui.PressKey(key); return new AgentToolResult($"גללתי {input.Direction}."); }, ct)),
            new AgentTool<FocusWindowArgs>(Def("switch_window", "Focus a visible window by its title.", "windowTitle", "string"), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.FocusWindow(input.WindowTitle)); }, ct)),
            new AgentTool<FocusWindowArgs>(Def("focus_window", "Focus a visible window by its title.", "windowTitle", "string"), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.FocusWindow(input.WindowTitle)); }, ct)),
            new AgentTool<FocusWindowArgs>(Def("minimize_window", "Minimize a visible window by its title.", "windowTitle", "string"), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.FocusWindow(input.WindowTitle, minimize: true)); }, ct)),
            new AgentTool<FocusWindowArgs>(Def("maximize_window", "Maximize a visible window by its title.", "windowTitle", "string"), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.FocusWindow(input.WindowTitle, maximize: true)); }, ct)),
            new AgentTool<ScreenshotArgs>(DefWithOptional("take_screenshot", "Capture the current virtual desktop locally. This does not upload the image.", new JsonObject()), safe,
                async (_, ct) => { var frame = await new DesktopAssistant.ScreenTranslation.ScreenCaptureProvider().CaptureWithoutAssistantAsync(ct); return new AgentToolResult($"צילום המסך נשמר בזיכרון בלבד: {frame.Image.PixelWidth}×{frame.Image.PixelHeight}. הוא לא נשלח לענן."); }),
            new AgentTool<ScreenshotArgs>(DefWithOptional("copy", "Copy the current selection to the Windows clipboard.", new JsonObject()), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.PressKey("CTRL+C")); }, ct)),
            new AgentTool<ScreenshotArgs>(DefWithOptional("paste", "Paste the current clipboard content into the focused control.", new JsonObject()), safe,
                (input, ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return new AgentToolResult(ui.PressKey("CTRL+V")); }, ct)),
            new AgentTool<AnalyzeScreenArgs>(Def("analyze_screen", "Send a screenshot to Gemini Vision and answer a question about visible content. Use only when the user asks about the screen.", "question", "string"),
                _ => DesktopAssistant.App.Services.Settings.AllowScreenshotAnalysis ? ActionRisk.Safe : ActionRisk.ConfirmationRequired,
                async (input, ct) =>
                {
                    var frame = await new DesktopAssistant.ScreenTranslation.ScreenCaptureProvider().CaptureWithoutAssistantAsync(ct);
                    var answer = await gemini.AnalyzeScreenshotAsync(frame, input.Question, ct);
                    return new AgentToolResult(answer);
                },
                () => { DesktopAssistant.App.Services.Settings.AllowScreenshotAnalysis = true; DesktopAssistant.Configuration.SettingsStore.Save(DesktopAssistant.App.Services.Settings); })
        ]);
    }

    private static AgentToolDeclaration Def(string name, string description, string property, string type) => new(name, description,
        new JsonObject { ["type"]="OBJECT", ["properties"]=new JsonObject { [property]=new JsonObject { ["type"]=type } }, ["required"]=new JsonArray(property) });

    private static AgentToolDeclaration DefMany(string name, string description, JsonObject properties, params string[] required) => new(name, description,
        new JsonObject { ["type"]="OBJECT", ["properties"]=properties, ["required"]=new JsonArray(required.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()) });
    private static AgentToolDeclaration DefWithOptional(string name, string description, JsonObject properties) => new(name, description, new JsonObject { ["type"]="OBJECT", ["properties"]=properties });
    private static bool IsSensitiveButton(string name) => ["submit", "continue", "confirm", "save", "apply", "שלח", "מחק", "רכוש", "קנה", "המשך", "אישור", "שמור", "בצע"].Any(item => name.Contains(item, StringComparison.OrdinalIgnoreCase));
    private static bool MaySubmit(string key) => key.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(part => part.Equals("ENTER", StringComparison.OrdinalIgnoreCase) || part.Equals("RETURN", StringComparison.OrdinalIgnoreCase));
    private static string DescribeKeyConfirmation(string key, DesktopUiAutomation ui)
    {
        var details = ui.ReadVisibleFormDetails();
        return string.IsNullOrWhiteSpace(details)
            ? $"אני עומד ללחוץ על {key}. קיצור זה עלול לשלוח או לאשר טופס. לבצע?"
            : $"אני עומד ללחוץ על {key}. קיצור זה עלול לשלוח או לאשר טופס עם הפרטים הגלויים הבאים:\n{details}\n\nלבצע?";
    }

    private static Task<AgentToolResult> Run(Func<string> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { return Task.FromResult(new AgentToolResult(operation())); }
        catch (Exception ex) { return Task.FromResult(new AgentToolResult(ex.Message, false)); }
    }
}
