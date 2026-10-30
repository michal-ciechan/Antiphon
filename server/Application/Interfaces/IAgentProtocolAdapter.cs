using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Result of <see cref="IAgentProtocolAdapter.KillGenerationAsync"/>.
/// <see cref="Cause"/> is authoritative only when this call observed it.
/// Unknown and null do not mean the kill produced the exit.
/// </summary>
public readonly record struct AdapterGenerationKill(bool Killed, AgentExitReason? Cause);

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
    /// <see cref="AdapterGenerationKill.Cause"/> is what this call observed (KilledByRequest versus
    /// an exit that had already happened). Null is not a cause: the exit watcher updates
    /// <see cref="ExitReason"/> later and must not authorise a launch-failure rewrite (CARD-1190).
    /// </summary>
    Task<AdapterGenerationKill> KillGenerationAsync(
        DateTime expectedAcceptedStartedAt, TimeSpan timeout, CancellationToken ct) =>
        Task.FromResult(new AdapterGenerationKill(false, null));
    Task<int> Exited { get; }
    // Old implementations report their existing code and unknown signal evidence.
    Task<AgentExitObservation> Completion => LegacyCompletionAsync(Exited);
    private static async Task<AgentExitObservation> LegacyCompletionAsync(Task<int> exited) => new(await exited);
    int? Pid { get; }
    AgentExitReason ExitReason { get; }

    Task SendPromptAsync(string prompt, CancellationToken ct);

    Task<bool> WaitForFirstPromptOutputAsync(TimeSpan timeout, CancellationToken ct);

    Task SendInputAsync(string input, CancellationToken ct);

    Task<ProviderDialogObservation?> ObserveDialogAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult<ProviderDialogObservation?>(null);

    // A finite RC maintenance action, separate from intentional interactive input.
    Task DismissRemoteControlAsync(CancellationToken ct) =>
        Task.FromException(new NotSupportedException("Guarded RC input is unsupported by this adapter."));

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
/// A cancelled StopReason denotes an interrupted turn, not a completion timeout.
/// Record so future fields (token usage) extend without breaking callers.
/// </summary>
public sealed record AgentTurnResult(
    bool TurnCompleted,
    string? ResponseText,
    bool IsAskingQuestion,
    string RawSnapshot,
    string? StopReason = null);
