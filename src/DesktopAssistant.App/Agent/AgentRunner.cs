using System.Globalization;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Desktop;

namespace DesktopAssistant.Agent;

public sealed record AgentResult(string Text, int Steps, IReadOnlyList<string> Actions);

/// <summary>The think → act → observe loop.</summary>
public sealed class AgentRunner(IChatModel model, AgentContext context, IReadOnlyList<AgentTool> tools)
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(90);

    public async Task<AgentResult> RunAsync(IReadOnlyList<ChatMessage> history, ChatMessage userMessage, CancellationToken cancellationToken)
    {
        var specs = tools.Select(t => t.Spec).ToList();
        var messages = new List<ChatMessage>(history) { userMessage };
        var actions = new List<string>();
        var system = BuildSystemPrompt();
        var maxSteps = context.Settings.MaxAgentSteps;
        var hidden = false;

        for (var step = 1; step <= maxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Activity(step == 1 ? "חושב…" : "בודק מה הלאה…");
            var reply = await model.CompleteAsync(system, messages, specs, new ModelOptions(), cancellationToken);

            var assistantParts = new List<ChatPart>();
            if (reply.Text.Length > 0) assistantParts.Add(new TextPart(reply.Text));
            assistantParts.AddRange(reply.ToolCalls);
            messages.Add(new ChatMessage { Role = ChatRole.Assistant, Parts = assistantParts, ProviderRaw = reply.ProviderRaw, ProviderKindTag = reply.ProviderKindTag });

            if (reply.ToolCalls.Count == 0)
                return new AgentResult(reply.Text.Length > 0 ? reply.Text : (actions.Count > 0 ? "בוצע." : "לא התקבלה תשובה."), step, actions);

            var results = new List<ChatPart>();
            foreach (var call in reply.ToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tool = tools.FirstOrDefault(t => t.Name == call.Name);
                if (tool is null) { results.Add(new ToolResultPart(call.Id, call.Name, $"Unknown tool '{call.Name}'.", true)); continue; }

                var confirmation = tool.NeedsConfirmation?.Invoke(call.Arguments, context);
                if (confirmation is not null && context.Settings.ConfirmSensitiveActions)
                {
                    if (!await context.Confirm(ConfirmTitle(tool.Name), confirmation))
                    {
                        results.Add(new ToolResultPart(call.Id, call.Name, "The user declined this action. Do not retry it; tell the user it was not done.", true));
                        actions.Add($"✖ {tool.ActivityText.TrimEnd('…')} (בוטל)");
                        continue;
                    }
                }

                if (tool.TouchesDesktop && !hidden) { await context.PrepareForDesktopAction(); hidden = true; }
                context.Activity(tool.ActivityText);
                AppLog.Info($"Tool {call.Name} {Truncate(call.Arguments.ToJsonString(), 300)}");
                ToolOutput output;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(ToolTimeout);
                    output = await tool.Run(call.Arguments, context, timeout.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) { output = ToolOutput.Error("The action timed out."); }
                catch (Exception ex) { AppLog.Warn($"Tool {call.Name} failed: {ex.Message}"); output = ToolOutput.Error(ex.Message); }
                actions.Add($"{(output.IsError ? "⚠" : "✓")} {tool.ActivityText.TrimEnd('…')}");
                results.Add(new ToolResultPart(call.Id, call.Name, output.Text, output.IsError, output.Image));
            }
            messages.Add(new ChatMessage { Role = ChatRole.User, Parts = results });
            TrimOldScreenshots(messages);
        }
        return new AgentResult($"עצרתי אחרי {maxSteps} צעדים כדי לא להמשיך בלי סוף. אפשר לבקש ממני להמשיך.", maxSteps, actions);
    }

    /// <summary>Keeps only the latest two screenshots in the conversation (they are large).</summary>
    private static void TrimOldScreenshots(List<ChatMessage> messages)
    {
        var withImages = messages.Select((m, i) => (m, i)).Where(x => x.m.Role == ChatRole.User && x.m.Parts.OfType<ToolResultPart>().Any(p => p.Image is not null)).Select(x => x.i).ToList();
        foreach (var index in withImages.Take(Math.Max(0, withImages.Count - 2)))
        {
            var message = messages[index];
            var parts = message.Parts.Select(p => p is ToolResultPart r && r.Image is not null ? r with { Image = null, Content = r.Content + " (old screenshot removed)" } : p).ToList();
            messages[index] = new ChatMessage { Role = message.Role, Parts = parts };
        }
    }

    private static string ConfirmTitle(string tool) => tool switch
    {
        "make_call" => "לאשר שיחה?",
        "run_powershell" => "לאשר הרצת פקודה?",
        "write_text_file" => "לאשר שמירת קובץ?",
        _ => "לאשר את הפעולה?"
    };

    private string BuildSystemPrompt()
    {
        var he = new CultureInfo("he-IL");
        var now = DateTime.Now;
        var foreground = context.PreviousWindowTitle;
        var windows = string.Join("; ", WindowManager.List().Take(15).Select(w => $"\"{Truncate(w.Title, 60)}\" ({w.ProcessName})"));
        var memory = context.Settings.Memory.Count == 0 ? "(none yet)" : string.Join("\n", context.Settings.Memory.Select(m => "- " + m));
        return $"""
            Your name is "{context.Settings.AssistantName}" — a personal AI assistant that lives on the user's Windows PC, like Gemini/Google Assistant on a phone but for the computer.
            You can really operate the computer through your tools: open apps and sites, type (any language), press keys, click, read windows, see the screen, write in Word, make calls via Phone Link, open WhatsApp chats, manage files, run PowerShell, set timers.

            Context
            - Now: {now.ToString("dddd, d MMMM yyyy, HH:mm", he)} ({now:yyyy-MM-dd}). Windows user: {Environment.UserName}.
            - The user was working in this window when they called you: "{foreground}" (default target for typing / "here" / "this")
            - Open windows: {windows}
            - Remembered about the user:
            {memory}

            How to work
            - Act, don't explain. When the user asks for something on the computer, do it with tools right away; ask a question only if something essential is truly missing.
            - Prefer the most direct tool: open_app, web_search, word_write (for anything that should end up in Word), whatsapp_message, make_call, open_path.
            - To operate an app: focus it (window_action) → list_ui_elements → click_element / set_element_text. If an app shows no elements (games, canvas, some web content) use take_screenshot → click_at with coordinates from that screenshot.
            - Typing what the user dictates: type it exactly as given (fix only obvious speech-recognition slips). Hebrew is fine.
            - After an important step (sending, saving, filling a form) verify with read_window_text or take_screenshot before saying it is done. Never claim something happened unless a tool result shows it.
            - Sensitive actions (calls, PowerShell, overwriting files, pressing send/buy/delete) are automatically shown to the user for approval — just call the tool; do not ask for permission in text.
            - If a tool fails, try another way (e.g. different app name, keyboard shortcut, screenshot) before giving up.
            - Use 'remember' when the user tells you something lasting about themselves (names, numbers, preferences) or asks you to remember.
            - Questions that need no action (facts, advice, writing, math, translation): just answer from your knowledge.

            About yourself (use this when the user asks what you can do or how to use you — explain warmly and concretely, in their language)
            {context.Help()}

            Answer style
            - Reply in the user's language (Hebrew by default), short and natural — 1-3 sentences for completed actions. Plain text; at most simple "-" bullets. No tables, no headers.
            - In Hebrew, speak about yourself in the feminine form (את/אני עושה → "פתחתי", "אני יכולה").
            """;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
