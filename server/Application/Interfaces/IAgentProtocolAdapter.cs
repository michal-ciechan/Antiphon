using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;

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

    /// <summary>
    /// CARD-0502: kill only if this adapter still represents <paramref name="expectedAcceptedStartedAt"/>.
    /// Default is a non-kill — never an unconditional <see cref="KillAsync"/> fallback.
    /// </summary>
    Task<bool> KillGenerationAsync(
        DateTime expectedAcceptedStartedAt, TimeSpan timeout, CancellationToken ct) =>
        Task.FromResult(false);
    Task<int> Exited { get; }
    int? Pid { get; }
    AgentExitReason ExitReason { get; }

    Task SendPromptAsync(string prompt, CancellationToken ct);

    Task<bool> WaitForFirstPromptOutputAsync(TimeSpan timeout, CancellationToken ct);

    Task SendInputAsync(string input, CancellationToken ct);

    /// <summary>
    /// CARD-0514: generation-and-sequence-conditional maintenance write. Default Unsupported —
    /// never a raw <see cref="SendInputAsync"/> fallback.
    /// </summary>
    Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        RunnerConditionalInputRequest request, CancellationToken ct) =>
        Task.FromResult(new RunnerConditionalInputResult(
            Guid.Empty, ConditionalInputOutcomes.Unsupported, null, null));

    Task ResizeAsync(int cols, int rows, CancellationToken ct);

    /// <summary>Raw PTY chunks. Subscribe to fan out via SignalR (FR-06).</summary>
    event Action<string>? OnTextDelta;

    Task<bool> WaitForReadyAsync(CancellationToken ct);

    /// <summary>
    /// Why the last <see cref="WaitForReadyAsync"/> returned false, when the adapter can name
    /// it (CARD-0324). Null keeps today's generic "Agent process did not become ready."
    /// Codex/Claude return null this card.
    /// </summary>
    AgentLaunchBlock? LaunchBlock => null;

    Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct);

    string SnapshotRawOutput();

    /// <summary>
    /// Cancellable raw-output snapshot. The remote-control arm wait must use this rather than
    /// <see cref="SnapshotRawOutput"/> so a hung runner HTTP call cannot outlive the setup budget.
    /// </summary>
    Task<string> SnapshotRawOutputAsync(CancellationToken ct);

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
