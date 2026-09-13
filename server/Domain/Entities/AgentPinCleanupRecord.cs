using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Hard-delete cleanup intent that survives the agent FK cascade (CARD-0262). OriginalAgentId is
/// audit data, not a foreign key.
/// </summary>
public class AgentPinCleanupRecord
{
    public Guid Id { get; set; }
    public Guid OriginalAgentId { get; set; }
    public string CanonicalHost { get; set; } = string.Empty;
    public string CanonicalCwd { get; set; } = string.Empty;
    public string TargetRelativePath { get; set; } = string.Empty;
    public string TargetAbsolutePath { get; set; } = string.Empty;
    public int PathSchemaVersion { get; set; } = 1;
    public PinCleanupStatus Status { get; set; } = PinCleanupStatus.Pending;
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
