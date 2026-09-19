using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>Persisted workspace-use fence shared by retirement and every managed reuse/launch path.</summary>
public sealed class WorkspaceUseReservation
{
    public Guid Id { get; set; }
    public int Generation { get; set; } = 1;
    public string CanonicalPath { get; set; } = "";
    public string SourceFullRef { get; set; } = "";
    public string CommonDirectory { get; set; } = "";
    public Guid? TaskId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? RetirementId { get; set; }
    public WorkspaceReservationKind Kind { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public bool Active { get; set; } = true;
}
