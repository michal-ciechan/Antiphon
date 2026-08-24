using System.Collections.Concurrent;

namespace Antiphon.FakeLlmApi;

/// <summary>What the stub returns for a matched provider endpoint.</summary>
public abstract record StubResponse;

/// <summary>HTTP error with optional JSON body. Default body is a minimal error object.</summary>
public sealed record ScriptedError(int StatusCode, string? JsonBody = null) : StubResponse;

/// <summary>One complete single-text-block turn (no tool_use).</summary>
public sealed record ScriptedTextTurn(string Text) : StubResponse;

/// <summary>
/// Per-endpoint FIFO of scripted responses. Missing scripts fall back to a default text turn
/// ("ok") so a hello/models probe can answer without the test queueing anything.
/// </summary>
public sealed class StubScript
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<StubResponse>> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StubResponse> _defaults = new(StringComparer.OrdinalIgnoreCase);

    public void Enqueue(string endpointKey, StubResponse response)
        => QueueFor(endpointKey).Enqueue(response);

    public void SetDefault(string endpointKey, StubResponse response)
        => _defaults[endpointKey] = response;

    public void Clear(string endpointKey)
    {
        if (_queues.TryGetValue(endpointKey, out var q))
            while (q.TryDequeue(out _)) { }
        _defaults.TryRemove(endpointKey, out _);
    }

    public void Reset()
    {
        _queues.Clear();
        _defaults.Clear();
    }

    /// <summary>Dequeues the next scripted response, or the default, or a ScriptedTextTurn("ok").</summary>
    public StubResponse Next(string endpointKey)
    {
        if (_queues.TryGetValue(endpointKey, out var q) && q.TryDequeue(out var next))
            return next;
        if (_defaults.TryGetValue(endpointKey, out var def))
            return def;
        return new ScriptedTextTurn("ok");
    }

    private ConcurrentQueue<StubResponse> QueueFor(string endpointKey)
        => _queues.GetOrAdd(endpointKey, _ => new ConcurrentQueue<StubResponse>());
}

/// <summary>Canonical endpoint keys used by the stub route handlers.</summary>
public static class StubEndpointKeys
{
    public const string ClaudeHello = "claude.hello";
    public const string ClaudeMessages = "claude.messages";
    public const string GrokModels = "grok.models";
    public const string GrokSettings = "grok.settings";
    public const string GrokApiKey = "grok.api-key";
    public const string GrokResponses = "grok.responses";
    public const string CodexModels = "codex.models";
    public const string CodexResponses = "codex.responses";
}
