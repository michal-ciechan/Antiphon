namespace Antiphon.Server.Domain.Entities;

/// <summary>Link between a capacity-wait episode and an observed hold generation (CARD-0412).</summary>
public class CapacityRecoveryWaitHold
{
    public Guid WaitId { get; set; }
    public Guid HoldId { get; set; }

    public int ObservedRevision { get; set; }
    public DateTime? ReleaseAcknowledgedAt { get; set; }

    public CapacityRecoveryWait Wait { get; set; } = null!;
    public ModelAvailabilityHold Hold { get; set; } = null!;
}
