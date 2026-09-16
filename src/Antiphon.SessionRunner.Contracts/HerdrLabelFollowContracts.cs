namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// Immutable explicit placement intent from a standing launch. Missing metadata and unknown
/// versions disable following; effective project/allocator labels never supply missing intent.
/// </summary>
public sealed record HerdrLabelFollowIntent(
    int Version,
    Guid StandingAgentId,
    Guid PlacementEditToken,
    string? WorkspaceLabel,
    string? TabLabel);

/// <summary>
/// Replaceable, ordered result for one accepted launch generation. Only a completed validated
/// result with positive evidence in the current GET may authorize a configuration write.
/// Refusals advance the watermark without carrying candidate labels.
/// </summary>
public sealed record HerdrLabelObservation(
    int Version,
    Guid SessionId,
    DateTime AcceptedStartedAt,
    string WorkspaceId,
    string TabId,
    string PaneId,
    string Origin,
    HerdrLabelFollowIntent Intent,
    long Sequence,
    DateTime CompletedAtUtc,
    DateTime ExpiresAtUtc,
    string ResultCode,
    string? TabLabel = null,
    string? WorkspaceLabel = null,
    bool PositivelyVerified = false);
