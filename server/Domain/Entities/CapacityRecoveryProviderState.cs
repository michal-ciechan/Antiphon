using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Per-execution-kind admission clock and sole outstanding grant (CARD-0412).
/// Supervisor reconciliation alone writes the grant; queue/start/dispatch redeem it.
/// </summary>
public class CapacityRecoveryProviderState
{
    public AgentKind Kind { get; set; }

    public DateTime? NextAdmissionAt { get; set; }
    public string? LastActionKey { get; set; }
    public DateTime? LastActionAt { get; set; }
    public int WaveRevision { get; set; }

    public Guid? GrantedWaitId { get; set; }
    public string? GrantedActionKey { get; set; }
    public DateTime? GrantedAt { get; set; }
    public int GrantVersion { get; set; }

    public int ExpectedWaitVersion { get; set; }
    public int ExpectedWaveRevision { get; set; }
    public Guid? ExpectedOwnerId { get; set; }

    public DateTime UpdatedAt { get; set; }
}
