using System.Text.Json;

namespace Antiphon.SessionRunner.Agent2Agent;

/// <summary>One line of <c>codex exec --json</c> output.</summary>
public abstract record CodexExecEvent
{
    public sealed record ThreadStarted(string ThreadId) : CodexExecEvent;
    public sealed record TurnStarted : CodexExecEvent;
    public sealed record ItemCompleted(string ItemType, string? Text, JsonElement Item) : CodexExecEvent;
    public sealed record TurnCompleted(JsonElement? Usage) : CodexExecEvent;
    public sealed record TurnFailed(string Message) : CodexExecEvent;
    public sealed record StreamError(string Message) : CodexExecEvent;
    /// <summary>A well-formed event this runner does not act on (item.started, item.updated, future types).</summary>
    public sealed record Other(string Type) : CodexExecEvent;
}

public static class CodexExecEventParser
{
    /// <summary>Parses one JSON line. Blank or non-JSON lines return null; Codex prints only JSON on stdout with --json.</summary>
    public static CodexExecEvent? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(line);
            root = document.RootElement.Clone();
        }
        catch (JsonException) { return null; }
        if (root.ValueKind != JsonValueKind.Object || !TryString(root, "type", out var type)) return null;

        switch (type)
        {
            case "thread.started":
                return TryString(root, "thread_id", out var threadId) ? new CodexExecEvent.ThreadStarted(threadId) : null;
            case "turn.started":
                return new CodexExecEvent.TurnStarted();
            case "item.completed":
                if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object
                    || !TryString(item, "type", out var itemType))
                    return null;
                return new CodexExecEvent.ItemCompleted(itemType, TryString(item, "text", out var text) ? text : null, item);
            case "turn.completed":
                return new CodexExecEvent.TurnCompleted(root.TryGetProperty("usage", out var usage) ? usage : null);
            case "turn.failed":
                return new CodexExecEvent.TurnFailed(
                    root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                    && TryString(error, "message", out var failure) ? failure : "Codex reported turn.failed.");
            case "error":
                return new CodexExecEvent.StreamError(TryString(root, "message", out var message) ? message : "Codex reported an error.");
            default:
                return new CodexExecEvent.Other(type);
        }
    }

    /// <summary>A one-line progress summary for a completed non-message item, or null for items not worth reporting.</summary>
    public static string? Summarize(CodexExecEvent.ItemCompleted item)
    {
        var body = item.Item;
        switch (item.ItemType)
        {
            case "agent_message":
                return item.Text;
            case "command_execution":
                var command = TryString(body, "command", out var c) ? c : "(command)";
                return body.TryGetProperty("exit_code", out var exit) && exit.ValueKind == JsonValueKind.Number
                    ? $"ran: {command} (exit {exit.GetInt32()})" : $"ran: {command}";
            case "file_change":
                if (!body.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                    return "changed files";
                var paths = changes.EnumerateArray()
                    .Select(change => TryString(change, "path", out var path)
                        ? (TryString(change, "kind", out var kind) ? $"{kind} {path}" : path) : null)
                    .Where(path => path is not null);
                return "changed: " + string.Join(", ", paths);
            case "mcp_tool_call":
                return $"tool: {(TryString(body, "server", out var server) ? server : "?")}.{(TryString(body, "tool", out var tool) ? tool : "?")}";
            case "web_search":
                return TryString(body, "query", out var query) ? $"searched: {query}" : "searched the web";
            case "error":
                return TryString(body, "message", out var warning) ? $"warning: {warning}" : null;
            default:
                return null;
        }
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String && property.GetString() is { } text)
        {
            value = text;
            return true;
        }
        value = "";
        return false;
    }
}
