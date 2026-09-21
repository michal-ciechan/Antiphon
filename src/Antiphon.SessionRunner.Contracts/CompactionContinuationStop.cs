namespace Antiphon.SessionRunner.Contracts;

/// <summary>CARD-0079 capability token. Absence means the runner must not be asked to stop.</summary>
public static class CompactionContinuationStopCapability
{
    public const string Feature = "compactionContinuationStopV1";
}

public static class CompactionObservationStatuses
{
    public const string Success = "success";
    public const string Unsupported = "unsupported";
    public const string Unbound = "unbound";
    public const string Unavailable = "unavailable";
    public const string Partial = "partial";
    public const string Unparsed = "unparsed";
    public const string StaleTail = "stale-tail";
}

public static class CompactionStopOutcomes
{
    public const string Exited = "exited";
    public const string Refused = "refused";
    public const string Mismatch = "mismatch";
    public const string Missing = "missing";
    public const string Unsupported = "unsupported";
    public const string Duplicate = "duplicate";
}

public sealed record CompactionTailObservation(
    string Status,
    bool IsSuccessful,
    long? ConsumedBytes,
    string? BindingIdentity,
    long TranscriptRevision,
    long OutputRevision,
    string? NativeBoundaryId,
    string? NativeContinuationId)
{
    public static CompactionTailObservation Unsupported() =>
        new(CompactionObservationStatuses.Unsupported, false, null, null, 0, 0, null, null);

    public static CompactionTailObservation Unbound() =>
        new(CompactionObservationStatuses.Unbound, false, null, null, 0, 0, null, null);

    public static CompactionTailObservation Unavailable() =>
        new(CompactionObservationStatuses.Unavailable, false, null, null, 0, 0, null, null);

    public static CompactionTailObservation Partial(string? binding) =>
        new(CompactionObservationStatuses.Partial, false, null, binding, 0, 0, null, null);

    public static CompactionTailObservation Unparsed(string? binding) =>
        new(CompactionObservationStatuses.Unparsed, false, null, binding, 0, 0, null, null);

    public static CompactionTailObservation Stale(string? binding) =>
        new(CompactionObservationStatuses.StaleTail, false, null, binding, 0, 0, null, null);
}

public sealed record CompactionContinuationStopRequest(
    Guid AttemptId,
    DateTime ExpectedAcceptedStartedAt,
    string NativeBoundaryIdentity,
    string NativeContinuationIdentity,
    int ThresholdMinutes,
    string BindingIdentity,
    long TranscriptRevision,
    long OutputRevision);

public sealed record CompactionContinuationStopResult(
    Guid SessionId,
    Guid AttemptId,
    bool ConfirmsExit,
    string Outcome,
    DateTime? AcceptedStartedAt = null);
