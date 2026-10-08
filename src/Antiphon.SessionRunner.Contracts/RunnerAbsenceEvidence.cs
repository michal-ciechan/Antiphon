namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// CARD-1153: authenticated evidence that a freshly allocated session id was never created on
/// this runner. Version 1 only. Acceptance everywhere is a whitelist of positive facts; any
/// unknown, stale, mismatched or malformed state is a refusal, never absence.
/// </summary>
public static class RunnerAbsenceEvidence
{
    public const int Version = 1;
    public const string NeverCreated = "never_created";
    public const int NonceBytes = 32;

    /// <summary>Capability token advertised only when the evidence service is ready.</summary>
    public const string Feature = "sessionAbsenceEvidenceV1";

    /// <summary>A-3: one closed-identity problem type for HTTP (409) creation refusals.</summary>
    public const string ClosedIdentityProblemType = "session_identity_closed";

    /// <summary>A-3: the phone-home error frame type for the same refusal.</summary>
    public const string PhoneHomeClosedIdentityErrorType = "phone_home_session_identity_closed";

    /// <summary>The closed version 1 certificate member set (A-6).</summary>
    public static readonly IReadOnlyList<string> CertificateMembers =
    [
        "version", "outcome", "sessionId", "acceptedStartedAt", "runnerStoreId", "runtimeEpoch",
        "requestNonce", "complete", "creationObserved", "nativeTranscriptPresent",
        "sidecarTranscriptPresent", "processPresent", "identityClosed", "observedAtUtc",
    ];

    /// <summary>True only for base64 that decodes to exactly <see cref="NonceBytes"/> bytes.</summary>
    public static bool IsValidNonce(string? nonce)
    {
        if (string.IsNullOrEmpty(nonce) || nonce.Length > 64)
            return false;
        var buffer = new byte[48];
        return Convert.TryFromBase64String(nonce, buffer, out var written) && written == NonceBytes;
    }

    public static string NewNonce() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(NonceBytes));
}

/// <summary>Typed refusal codes. Status is the HTTP status the route answers with.</summary>
public static class RunnerAbsenceRefusalCodes
{
    /// <summary>404: never prepared; an arbitrary id is not certified.</summary>
    public const string NotPrepared = "absence_evidence_not_prepared";
    /// <summary>409: a runtime entry, attempt, artifact or watermark exists for the id.</summary>
    public const string IdentityKnown = "absence_evidence_identity_known";
    /// <summary>409: the record was written by an earlier runner epoch.</summary>
    public const string PriorEpoch = "absence_evidence_prior_epoch";
    /// <summary>409: generation or store does not equal the recorded binding.</summary>
    public const string BindingMismatch = "absence_evidence_binding_mismatch";
    /// <summary>503: adoption incomplete, storage latched, IO error, corrupt or unknown record.</summary>
    public const string Unavailable = "absence_evidence_unavailable";
    /// <summary>400: malformed request (version, nonce, ids, route/body disagreement).</summary>
    public const string InvalidRequest = "absence_evidence_invalid_request";
    /// <summary>401: missing or wrong authentication, or no key configured.</summary>
    public const string Unauthenticated = "absence_evidence_unauthenticated";
}

public sealed record RunnerAbsenceRefusal(string Code, int Status, string Reason);

/// <summary>Request body for both prepare and certify (same identity fields).</summary>
public sealed record RunnerAbsenceRequest(
    int Version,
    Guid SessionId,
    DateTime AcceptedStartedAt,
    Guid RunnerStoreId,
    string RequestNonce);

/// <summary>Prepare acknowledgement: the durable record's identity, never a certificate.</summary>
public sealed record RunnerAbsencePrepared(
    int Version,
    string State,
    Guid SessionId,
    DateTime AcceptedStartedAt,
    Guid RunnerStoreId,
    Guid RuntimeEpoch,
    string RequestNonce);

/// <summary>
/// The version 1 never-created certificate. Every member is nullable so an omitted value is
/// never mistaken for an observation. Producers always set every member.
/// </summary>
public sealed record RunnerAbsenceCertificate(
    int? Version,
    string? Outcome,
    Guid? SessionId,
    DateTime? AcceptedStartedAt,
    Guid? RunnerStoreId,
    Guid? RuntimeEpoch,
    string? RequestNonce,
    bool? Complete,
    bool? CreationObserved,
    bool? NativeTranscriptPresent,
    bool? SidecarTranscriptPresent,
    bool? ProcessPresent,
    bool? IdentityClosed,
    DateTime? ObservedAtUtc);

/// <summary>Outcome of a prepare or certify call: exactly one of value and refusal.</summary>
public sealed record RunnerAbsenceOutcome<T>(T? Value, RunnerAbsenceRefusal? Refusal) where T : class
{
    public static RunnerAbsenceOutcome<T> Ok(T value) => new(value, null);
    public static RunnerAbsenceOutcome<T> Refuse(string code, int status, string reason) =>
        new(null, new RunnerAbsenceRefusal(code, status, reason));
}

/// <summary>
/// A-3: a creation or attach refused because the id was certified never-created. Not a
/// retryable fault; the server maps it to the existing refused-launch path.
/// </summary>
public sealed class SessionIdentityClosedException(Guid sessionId)
    : InvalidOperationException($"Session '{sessionId:D}' identity is closed: it was certified never created.")
{
    public Guid SessionId { get; } = sessionId;
}
