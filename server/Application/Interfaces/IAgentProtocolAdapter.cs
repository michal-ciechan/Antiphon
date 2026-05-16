using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Hides protocol-specific behaviour (when ready, when turn complete, response shape)
/// behind one seam so Codex / Claude / Gemini / Aider / raw shells slot in
/// without hardcoded quirks at the call site.
///
/// Implementations own a private <c>PtyAgentRunner</c> for the lifetime of the adapter.
/// PTY library types never appear in this interface (NFR-01).
/// </summary>
public interface IAgentProtocolAdapter : IAsyncDisposable
{
    Task StartAsync(AgentLaunchSpec spec, CancellationToken ct);
    Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct);
    Task<int> Exited { get; }
    int? Pid { get; }
    AgentExitReason ExitReason { get; }

    Task SendPromptAsync(string prompt, CancellationToken ct);

    Task<bool> WaitForFirstPromptOutputAsync(TimeSpan timeout, CancellationToken ct);

    Task SendInputAsync(string input, CancellationToken ct);

    Task ResizeAsync(int cols, int rows, CancellationToken ct);

    /// <summary>Raw PTY chunks. Subscribe to fan out via SignalR (FR-06).</summary>
    event Action<string>? OnTextDelta;

    Task<bool> WaitForReadyAsync(CancellationToken ct);
    Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct);

    string SnapshotRawOutput();
    string SnapshotRenderedScreen();

    string? AuditDirectory { get; }
}

/// <summary>
/// Outcome of a single prompt → response cycle. Adapters fill what they know;
/// callers needing more reach for <see cref="RawSnapshot"/>.
/// Record so future fields (token usage, stop reason) extend without breaking callers.
/// </summary>
public sealed record AgentTurnResult(
    bool TurnCompleted,
    string? ResponseText,
    bool IsAskingQuestion,
    string RawSnapshot);
