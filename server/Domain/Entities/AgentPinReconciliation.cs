using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Latest desired pin revision for an agent and whether projection/notification work is still
/// outstanding. Pin mutation and this dirty revision commit together. An API delivery receipt
/// never clears pending file work.
/// </summary>
public class AgentPinReconciliation
{
    public Guid AgentId { get; set; }
    public int DesiredRevision { get; set; }
    public string DesiredHash { get; set; } = string.Empty;
    public PinProjectionStatus Status { get; set; } = PinProjectionStatus.Pending;
    public string? Error { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Agent Agent { get; set; } = null!;
}
