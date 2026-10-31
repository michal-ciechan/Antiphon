using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.TestSupport.LiveClaude;

internal sealed record DeliveryBinding(string Session, string Generation, string NativeBinding);
internal sealed record DeliveryRow(DeliveryBinding Binding, long Sequence, string Kind, string? Text,
    string Id, string? Parent, string? Api, bool End = false, bool Error = false, string? ErrorType = null, string? Request = null);
internal sealed record DeliveryRequest(DeliveryBinding Binding, long Floor, string Body);
internal sealed record DeliveryBridge(int Pid, string Started, string Session, string? Id, bool Readable = true);
internal enum DeliveryReadiness { Ready, Busy, Permission, Question, Unknown }

/// <summary>Diagnostic-only verdicts. Screen text is deliberately absent from the decision inputs.</summary>
internal static class LiveClaudeDeliveryEvidence
{
    public static DeliveryRow RequirePrompt(DeliveryRequest request, IReadOnlyList<DeliveryRow> rows)
    {
        if (!PromptSubmissionMatch.RequiresTextMatch(request.Body)) throw new LiveClaudeRefusal("prompt-too-short");
        var prompts = rows.Where(r => r.Kind == "UserPrompt"
            && r.Binding.Session == request.Binding.Session
            && r.Binding.Generation == request.Binding.Generation
            && r.Binding.NativeBinding == request.Binding.NativeBinding
            && r.Sequence > request.Floor
            && PromptSubmissionMatch.IsCompleteIn(request.Body, r.Text)).ToArray();
        if (prompts.Length != 1 || string.IsNullOrWhiteSpace(prompts[0].Id))
            throw new LiveClaudeRefusal("prompt-unconfirmed");
        return prompts[0];
    }

    public static void RequireSubmit(DeliveryRequest request, IReadOnlyList<DeliveryRow> rows)
    {
        var prompt = RequirePrompt(request, rows);
        var following = rows.Where(r => r.Binding == request.Binding).OrderBy(r => r.Sequence).ToArray();
        foreach (var response in following.Where(r => r.Kind == "AssistantText" && !string.IsNullOrWhiteSpace(r.Text)))
        {
            if (response.Sequence <= prompt.Sequence) continue;
            if (string.IsNullOrWhiteSpace(response.Api) || !BelongsTo(response, prompt, following)) continue;
            if (following.Any(r => r.Kind is "UserPrompt" or "QueuedUserPrompt"
                && r.Sequence > prompt.Sequence && r.Sequence < response.Sequence)) continue;
            if (response.Error) throw new LiveClaudeRefusal("provider-error");
            var end = following.FirstOrDefault(r => r.End && r.Kind == "AssistantText"
                && r.Api == response.Api && r.Id == response.Id && r.Sequence >= response.Sequence);
            if (end is null) continue;
            if (following.Any(r => r.Kind is "UserPrompt" or "QueuedUserPrompt"
                && r.Sequence > prompt.Sequence && r.Sequence <= end.Sequence)) continue;
            if (end.Error) throw new LiveClaudeRefusal("provider-error");
            return;
        }
        throw new LiveClaudeRefusal("turn-unconfirmed");
    }

    private static bool BelongsTo(DeliveryRow response, DeliveryRow prompt, IReadOnlyList<DeliveryRow> rows)
    {
        var parent = response.Parent;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ceiling = response.Sequence;
        while (!string.IsNullOrEmpty(parent) && seen.Add(parent))
        {
            if (parent == prompt.Id) return true;
            var previous = rows.LastOrDefault(r => r.Id == parent && r.Sequence < ceiling);
            if (previous is null || previous.Kind is "UserPrompt" or "QueuedUserPrompt") return false;
            ceiling = previous.Sequence;
            parent = previous.Parent;
        }
        return false;
    }

    public static void RequireMenu(DeliveryRequest request, IReadOnlyList<DeliveryRow> rows,
        DeliveryBridge owner, DeliveryBridge? beforeEsc, DeliveryBridge? afterEsc, DeliveryBridge? afterSubmit)
    {
        _ = RequirePrompt(request, rows);
        RequireSameBridge(owner, beforeEsc, "before-Esc");
        RequireSameBridge(owner, afterEsc, "after-Esc");
        RequireSameBridge(owner, afterSubmit, "after-submit");
    }

    private static void RequireSameBridge(DeliveryBridge owner, DeliveryBridge? observed, string phase)
    {
        if (observed is not { Readable: true } || string.IsNullOrWhiteSpace(owner.Id)
            || owner.Pid <= 0 || string.IsNullOrWhiteSpace(owner.Started)
            || string.IsNullOrWhiteSpace(owner.Session)
            || observed.Pid != owner.Pid || observed.Started != owner.Started
            || observed.Session != owner.Session || observed.Id != owner.Id)
            throw new LiveClaudeRefusal("bridge-changed-" + phase);
    }

    // Narrow native JSONL reader for the two direct-input canaries. Sequence is the persisted
    // line ordinal. Generation/native binding come from the independently checked owned process,
    // never an untrusted JSON field or the screen. No transcript payload enters diagnostics.
    public static IReadOnlyList<DeliveryRow> ReadJsonl(string jsonl, DeliveryBinding observed)
    {
        if (jsonl.Length > 0 && !jsonl.EndsWith('\n')) throw new LiveClaudeRefusal("transcript-partial");
        var rows = new List<DeliveryRow>();
        long sequence = 0;
        try
        {
            foreach (var line in jsonl.Split('\n').SkipLast(1))
            {
                sequence++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) continue;
                var session = Get(root, "sessionId") ?? Get(root, "session_id") ?? "";
                var binding = observed with { Session = session };
                var type = Get(root, "type");
                var id = Get(root, "uuid") ?? "";
                var parent = Get(root, "parentUuid");
                if (!root.TryGetProperty("message", out var message)) continue;
                var text = Content(message, "text");
                if (type == "user") rows.Add(new(binding, sequence, "UserPrompt", text, id, parent, null, Request: id));
                else if (type == "assistant")
                {
                    var error = root.TryGetProperty("isApiErrorMessage", out var e) && e.ValueKind == JsonValueKind.True
                        || root.TryGetProperty("error", out var errorValue) && errorValue.ValueKind is not (JsonValueKind.Null or JsonValueKind.False);
                    var errorType = error ? OutageType(Get(root, "error"), Status(root)) : null;
                    var kind = string.IsNullOrWhiteSpace(text) ? "Thinking" : "AssistantText";
                    rows.Add(new(binding, sequence, kind, text, id, parent, Get(message, "id"), Get(message, "stop_reason") == "end_turn", error, errorType, parent));
                }
                else rows.Add(new(binding, sequence, type == "attachment" ? "QueuedUserPrompt" : "Screen", text, id, parent, null));
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        { throw new LiveClaudeRefusal("transcript-unreadable"); }
        return rows;
    }

    internal static string? OutageType(string? error, int? status)
    {
        if (error is "Overloaded" or "ServiceUnavailable" or "RateLimited") return error;
        if (error is "rate_limit" || status == 429) return "RateLimited";
        if (error is "overloaded" || status == 529) return "Overloaded";
        if (error is "server_error" or "unavailable" || status == 503) return "ServiceUnavailable";
        return null;
    }

    private static int? Status(JsonElement root)
        => root.TryGetProperty("apiErrorStatus", out var item) && item.TryGetInt32(out var status) ? status : null;

    private static string? Get(JsonElement value, string name)
        => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static string? Content(JsonElement message, string kind)
    {
        if (!message.TryGetProperty("content", out var content)) return null;
        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind != JsonValueKind.Array) return null;
        return string.Join("\n", content.EnumerateArray().Where(c => Get(c, "type") == kind).Select(c => Get(c, kind)));
    }

    public static async Task<IReadOnlyList<DeliveryRow>> SubmitOnceAsync(DeliveryRequest request,
        Func<Task> send, Func<Task<IReadOnlyList<DeliveryRow>>> observe, bool completion)
    {
        await send();
        var rows = await observe();
        if (completion) RequireSubmit(request, rows);
        else _ = RequirePrompt(request, rows);
        return rows;
    }

    public static async Task SendOnceAsync(string body, Func<DeliveryReadiness> readiness,
        Func<Task> waitUntilNextRead, Func<string, Task> write, Func<string, Task<bool>> echo)
    {
        if (!PromptSubmissionMatch.RequiresTextMatch(body)) throw new LiveClaudeRefusal("prompt-too-short");
        while (true)
        {
            var state = readiness();
            if (state == DeliveryReadiness.Ready) break;
            if (state != DeliveryReadiness.Busy) throw new LiveClaudeRefusal("recipient-blocked");
            await waitUntilNextRead();
        }
        var normalized = body.ReplaceLineEndings("\n");
        await write("\x1b[200~" + normalized + "\x1b[201~");
        if (!await echo(normalized)) throw new LiveClaudeRefusal("composer-unobserved");
        if (readiness() != DeliveryReadiness.Ready) throw new LiveClaudeRefusal("recipient-blocked");
        await write("\r");
        // Intentionally no retry: ambiguous writes/read failures require independent receipt evidence.
    }
}
