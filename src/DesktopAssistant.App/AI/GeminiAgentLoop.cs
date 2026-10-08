using System.Text.Json.Nodes;
using DesktopAssistant.Automation;

namespace DesktopAssistant.AI;

public sealed record AgentRunResult(string Text, bool Cancelled = false, int Steps = 0);

/// <summary>Bounded, cancellable Gemini tool loop. It has a hard step cap and per-tool timeout.</summary>
public sealed class GeminiAgentLoop(IAgentModel model, AgentToolRegistry tools)
{
    private const int MaximumSteps = 8;
    private static readonly TimeSpan TaskTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(20);

    public async Task<AgentRunResult> RunAsync(string userText, Func<string, Task> reportStatus, Func<string, string, Task<bool>> confirm, CancellationToken cancellationToken = default, IReadOnlyList<ConversationMessage>? history = null)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TaskTimeout);
        var contents = new JsonArray();
        foreach (var item in history is null ? Enumerable.Empty<ConversationMessage>() : history.TakeLast(12)) contents.Add(new JsonObject { ["role"]=item.Role == "assistant" ? "model" : "user", ["parts"]=new JsonArray(new JsonObject { ["text"]=item.Content }) });
        contents.Add(new JsonObject { ["role"]="user", ["parts"]=new JsonArray(new JsonObject { ["text"]=userText }) });
        for (var step = 1; step <= MaximumSteps; step++)
        {
            bounded.Token.ThrowIfCancellationRequested();
            await reportStatus("מתכנן את הפעולה הבאה…");
            AgentModelTurn turn;
            try { turn = await model.GenerateAgentTurnAsync(contents, tools.Declarations, bounded.Token); }
            catch (InvalidOperationException ex) when (step <= 2 && ex.Message.StartsWith("לא ניתן להתחבר", StringComparison.Ordinal)) { await reportStatus("החיבור ל-Gemini נכשל; מנסה שוב…"); turn = await model.GenerateAgentTurnAsync(contents, tools.Declarations, bounded.Token); }
            if (turn.ToolCalls.Count == 0) return new AgentRunResult(turn.Text, Steps: step);

            contents.Add(turn.ModelContent.DeepClone());
            var responseParts = new JsonArray();
            foreach (var call in turn.ToolCalls.Take(4))
            {
                bounded.Token.ThrowIfCancellationRequested();
                if (!tools.TryGet(call.Name, out var tool))
                {
                    responseParts.Add(FunctionResponse(call, new AgentToolResult("הכלי המבוקש אינו זמין.", false)));
                    continue;
                }

                var summary = tool.GetConfirmationText(call.Arguments);
                if (string.IsNullOrWhiteSpace(summary)) summary = Summarize(call.Name, call.Arguments);
                AgentToolResult result;
                try
                {
                    if (tool.GetRisk(call.Arguments) == ActionRisk.ConfirmationRequired && !await confirm(call.Name, summary))
                    {
                        responseParts.Add(FunctionResponse(call, new AgentToolResult("המשתמש ביטל את הפעולה.", false)));
                        continue;
                    }
                    if (tool.GetRisk(call.Arguments) == ActionRisk.ConfirmationRequired) tool.ConfirmAccepted();
                    await reportStatus(StatusFor(call.Name));
                    using var toolCts = CancellationTokenSource.CreateLinkedTokenSource(bounded.Token);
                    toolCts.CancelAfter(ToolTimeout);
                    result = await tool.ExecuteAsync(call.Arguments, toolCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { result = new AgentToolResult("הפעולה חרגה ממגבלת הזמן.", false); }
                catch (Exception ex) { result = new AgentToolResult($"הפעולה נכשלה: {ex.Message}", false); }
                responseParts.Add(FunctionResponse(call, result));
            }
            contents.Add(new JsonObject { ["role"]="user", ["parts"]=responseParts });
        }
        return new AgentRunResult("עצרתי אחרי שמונה שלבים כדי למנוע רצף פעולות בלתי מוגבל. אפשר לבקש ממני להמשיך.", Steps: MaximumSteps);
    }

    private static JsonObject FunctionResponse(AgentToolCall call, AgentToolResult result)
    {
        var response = new JsonObject { ["name"]=call.Name, ["response"]=new JsonObject { ["result"]=result.Result, ["succeeded"]=result.Succeeded } };
        if (!string.IsNullOrWhiteSpace(call.Id)) response["id"] = call.Id;
        return new JsonObject { ["functionResponse"]=response };
    }

    private static string StatusFor(string name) => name switch
    {
        "open_application" or "open_url" => "פותח את האפליקציה או האתר…",
        "search_web" or "search_youtube" => "מחפש…",
        "close_window" => "סוגר את החלון…",
        "calculate" => "מחשב…",
        "inspect_ui" or "read_active_window" or "read_screen" => "בודק את התוכן הגלוי בחלון…",
        "click_ui_element" => "מפעיל את הרכיב בחלון…",
        "type_text" => "מקליד בחלון…",
        "analyze_screen" => "מנתח צילום מסך לאחר אישור…",
        "wait" => "ממתין לטעינה…",
        _ => "מבצע פעולה…"
    };

    private static string Summarize(string name, System.Text.Json.JsonElement args)
    {
        if (name == "analyze_screen") return "צילום המסך הנוכחי יישלח ל-Gemini לצורך ניתוח. לאשר את שליחת התמונה?";
        var values = string.Join("\n", args.EnumerateObject().Select(p => $"{p.Name}: {p.Value.ToString()}"));
        return $"הפעולה {name} עומדת להתבצע עם הפרטים הבאים:\n{values}\n\nלבצע?";
    }
}
