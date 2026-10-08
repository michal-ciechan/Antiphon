namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-1153 D-4: the only shapes an absence-evidence read can produce.</summary>
public enum SessionRunnerAbsenceEvidenceKind
{
    /// <summary>Authenticated, complete, fresh never-created certificate for the exact identity.</summary>
    Proven,
    /// <summary>The runner, transport or configuration cannot certify (old runner, no key, no capability).</summary>
    Unsupported,
    /// <summary>Anything else: refusal, malformed, unauthenticated, stale, timed out or unreachable.</summary>
    Unknown,
}

/// <summary>
/// A validated certificate. Only <see cref="Services.RunnerAbsenceEvidenceValidator"/> constructs
/// one; callers never set an "authenticated" bit. It expires five seconds after receipt.
/// </summary>
public sealed record SessionRunnerAbsenceEvidence(
    Guid SessionId,
    DateTime AcceptedStartedAt,
    Guid RunnerStoreId,
    Guid RuntimeEpoch,
    DateTimeOffset ReceivedAt,
    DateTimeOffset ExpiresAt,
    DateTime ObservedAtUtc)
{
    public bool IsFresh(DateTimeOffset now) => now >= ReceivedAt && now <= ExpiresAt;
}

public sealed record SessionRunnerAbsenceEvidenceResult(
    SessionRunnerAbsenceEvidenceKind Kind,
    SessionRunnerAbsenceEvidence? Evidence,
    string Reason)
{
    public bool IsProven => Kind == SessionRunnerAbsenceEvidenceKind.Proven && Evidence is not null;

    public static SessionRunnerAbsenceEvidenceResult Unsupported(string reason) =>
        new(SessionRunnerAbsenceEvidenceKind.Unsupported, null, reason);

    public static SessionRunnerAbsenceEvidenceResult Unknown(string reason) =>
        new(SessionRunnerAbsenceEvidenceKind.Unknown, null, reason);
}

/// <summary>Prepare acknowledgement. Never evidence; only says a record was written.</summary>
public sealed record SessionRunnerAbsencePrepareResult(bool Prepared, string Reason)
{
    public static SessionRunnerAbsencePrepareResult Unsupported(string reason) => new(false, reason);
}
