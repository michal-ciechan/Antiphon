using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One mandatory projection location for a named agent, keyed by canonical execution host and cwd.
/// Different AgentIds in the same cwd never share a target.
/// </summary>
public class AgentPinProjection
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string CanonicalHost { get; set; } = string.Empty;
    public string CanonicalCwd { get; set; } = string.Empty;
    public int PathSchemaVersion { get; set; } = 1;
    public string TargetRelativePath { get; set; } = string.Empty;
    public string TargetAbsolutePath { get; set; } = string.Empty;
    public int LocationGeneration { get; set; } = 1;
    public int DesiredRevision { get; set; }
    public int? ProjectedRevision { get; set; }
    public string? LastWrittenByteHash { get; set; }
    public int MarkerVersion { get; set; } = 1;
    public PinProjectionStatus Status { get; set; } = PinProjectionStatus.Pending;
    public string? Error { get; set; }
    public PinImportStatus ImportStatus { get; set; } = PinImportStatus.None;
    public PinClaudeImportMode ImportMode { get; set; } = PinClaudeImportMode.Unverified;
    public string? ImportTarget { get; set; }
    public int? ImportOwnedStart { get; set; }
    public int? ImportOwnedLength { get; set; }
    public string? IntendedBeforeHash { get; set; }
    public string? IntendedAfterHash { get; set; }
    public bool HasConfiguredConsumer { get; set; }
    public bool HasLiveSessionConsumer { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Agent Agent { get; set; } = null!;
}
