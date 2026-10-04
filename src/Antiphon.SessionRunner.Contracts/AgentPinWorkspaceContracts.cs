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
