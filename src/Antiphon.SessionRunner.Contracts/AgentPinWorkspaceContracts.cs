namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// Read-only S3a.1 request. The host derives the only target from Cwd and the full
/// AgentId; this is not an arbitrary-file API. Transport admission remains dormant.
/// </summary>
public sealed record AgentPinInspectRequest(
    int SchemaVersion, Guid AgentId, Guid RunnerStoreId, string Cwd);

public enum AgentPinInspectionStatus
{
    Refused,
    Unavailable,
    MissingCwd,
    MissingFile,
    Observed
}

/// <summary>
/// A point-in-time native observation, NOT a publication/custody receipt. Observed
/// bytes can be foreign or edited; this result cannot authorize Ready or cleanup.
/// No pin content is returned in this metadata result.
/// </summary>
public sealed record AgentPinInspection(
    AgentPinInspectionStatus Status,
    string? Reason,
    Guid AgentId,
    Guid RunnerStoreId,
    string? Cwd = null,
    string? Path = null,
    string? Sha256 = null,
    long? ByteCount = null);

/// <summary>Dormant host primitive. The authenticated caller must have committed
/// its intent before calling. Fence increases across location retirement/reuse.
/// Null expected hash means absence; cleanup carries no content or new hash.</summary>
public sealed record AgentPinPublicationRequest(
    int SchemaVersion, Guid AgentId, Guid TargetOwnerId, Guid RunnerStoreId,
    string Cwd, Guid LocationGeneration, Guid OperationId, long Fence, long Revision,
    AgentPinFileAction Action, byte[]? Content, string? Sha256, string? ExpectedSha256);

public enum AgentPinFileAction { Publish, Cleanup }
public enum AgentPinPublicationStatus { Applied, Refused, Unavailable }

public sealed record AgentPinFileReceipt(
    int SchemaVersion, Guid AgentId, Guid RunnerStoreId, string Path,
    Guid LocationGeneration, Guid OperationId, long Fence, long Revision,
    AgentPinFileAction Action, string? Sha256, long ByteCount);

public sealed record AgentPinPublicationResult(
    AgentPinPublicationStatus Status, string? Reason, AgentPinFileReceipt? Receipt = null);
