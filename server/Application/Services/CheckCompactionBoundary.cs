namespace Antiphon.Server.Application.Services;

/// <summary>
/// Observation seam at the real CARD-0079 handoffs. Production installs no probe,
/// so every hit is a completed no-op. Tests replace <see cref="Probe"/> for one scope.
/// </summary>
public class CheckCompactionBoundary
{
    public static readonly string[] Points =
    [
        "observed",
        "before-stop-commit",
        "stop-committed",
        "before-stop-rpc",
        "stopped-committed",
        "resume-committed",
        "before-resume-enqueue",
        "before-resume-spawn",
        "launch-accepted",
        "before-fence-write",
        "retirement-committed",
        "failure-committed",
        "before-note-enqueue",
        "note-committed",
        "attempt-committed",
        "prompt-accepted",
        "before-receipt-commit",
        "audit-committed",
        "before-audit-publish",
        "legacy-capture-committed",
        "legacy-run-link-committed",
        "legacy-outcome-selected",
        "legacy-before-produce-commit",
        "legacy-note-produced",
        "legacy-captured-scan",
    ];

    public virtual Task ReachedAsync(string boundary, Guid operationId, CancellationToken ct) =>
        Task.CompletedTask;
}
