using System.Text.Json.Nodes;
using DesktopAssistant.AI;
using DesktopAssistant.Desktop;
using DesktopAssistant.Settings;

namespace DesktopAssistant.Agent;

public sealed record ToolOutput(string Text, ImagePart? Image = null, bool IsError = false)
{
    public static ToolOutput Error(string text) => new(text, null, true);
}

/// <summary>Everything a tool may need from the rest of the app.</summary>
public sealed class AgentContext
{
    public required AppSettings Settings { get; init; }
    /// <summary>Asks the user to approve a sensitive action. (title, details) → approved?</summary>
    public required Func<string, string, Task<bool>> Confirm { get; init; }
    /// <summary>Short live status for the UI ("מקליד ב-Word…").</summary>
    public required Action<string> Activity { get; init; }
    /// <summary>Gets the assistant's own windows out of the way before touching other apps.</summary>
    public required Func<Task> PrepareForDesktopAction { get; init; }
    /// <summary>Hides/shows the floating status pill around screenshots.</summary>
    public required Func<bool, Task> SetOverlayVisible { get; init; }
    public required Func<Task> OpenScreenTranslation { get; init; }
    public required Action<string, string> Notify { get; init; }
    public required Func<Func<object?>, Task<object?>> RunOnUiThread { get; init; }
    public UiAutomationHelper Ui { get; } = new();
    public AgentScreenshot? LastScreenshot { get; set; }
}

public sealed class AgentTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonObject Schema { get; init; }
    /// <summary>Returns a confirmation text when this call needs the user's approval, otherwise null.</summary>
    public Func<JsonObject, AgentContext, string?>? NeedsConfirmation { get; init; }
    /// <summary>True for tools that operate other windows (the assistant hides itself first).</summary>
    public bool TouchesDesktop { get; init; }
    public required Func<JsonObject, AgentContext, CancellationToken, Task<ToolOutput>> Run { get; init; }
    public string ActivityText { get; init; } = "עובד…";

    public ToolSpec Spec => new(Name, Description, Schema);
}

/// <summary>Tiny helpers to declare JSON schemas and read arguments.</summary>
public static class S
{
    public static JsonObject Obj(params (string Name, JsonObject Schema, bool Required)[] props)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema, isRequired) in props)
        {
            properties[name] = schema;
            if (isRequired) required.Add(name);
        }
        var result = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0) result["required"] = required;
        return result;
    }

    public static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };
    public static JsonObject Int(string description) => new() { ["type"] = "integer", ["description"] = description };
    public static JsonObject Num(string description) => new() { ["type"] = "number", ["description"] = description };
    public static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };
    public static JsonObject Enum(string description, params string[] values)
    {
        var list = new JsonArray();
        foreach (var v in values) list.Add(v);
        return new JsonObject { ["type"] = "string", ["description"] = description, ["enum"] = list };
    }

    public static string GetString(this JsonObject args, string name, string fallback = "")
    {
        var node = args[name];
        if (node is null) return fallback;
        try { return node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node.ToString(); } catch { return fallback; }
    }

    public static string? GetOptionalString(this JsonObject args, string name)
    {
        var s = args.GetString(name);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    public static double GetNumber(this JsonObject args, string name, double fallback = 0)
    {
        var node = args[name];
        if (node is not JsonValue v) return fallback;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
        return fallback;
    }

    public static int GetInt(this JsonObject args, string name, int fallback = 0) => (int)Math.Round(args.GetNumber(name, fallback));

    public static bool GetBool(this JsonObject args, string name, bool fallback = false)
    {
        var node = args[name];
        if (node is not JsonValue v) return fallback;
        if (v.TryGetValue<bool>(out var b)) return b;
        if (v.TryGetValue<string>(out var s)) return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        return fallback;
    }
}
