namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-1320. One keep-six toggle for one standing orchestrator agent.
/// A missing row means off, target <see cref="DefaultTarget"/>, revision 0, and a read does not create it.
/// </summary>
public class KeepSixOrchestrator
{
    public const int DefaultTarget = 6;
    public const int MinTarget = 1;
    public const int MaxTarget = 24;
    public const int ReasonMaxLength = 400;
    public const int HoldReasonMaxLength = 200;
    public const int HoldMaxDays = 7;

    public Guid AgentId { get; set; }
    public bool Enabled { get; set; }
    public int Target { get; set; } = DefaultTarget;
    public string Provenance { get; set; } = "";
    public string Reason { get; set; } = "";
    public long Revision { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? EnabledAt { get; set; }
    public string? HoldReason { get; set; }
    public DateTime? HoldUntil { get; set; }
    public DateTime? LastReminderEnqueuedAt { get; set; }
    public Guid? OutstandingMessageId { get; set; }
    public DateTime? LastAcknowledgedAt { get; set; }

    public Agent Agent { get; set; } = null!;
}
