using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Repeat-safe pin mutation keyed by client RequestId. Reusing a RequestId with a different
/// fingerprint is 409 <c>pin_request_conflict</c>.
/// </summary>
public class AgentPinOperation
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Guid RequestId { get; set; }
    public PinOperationKind Kind { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public Guid? ResultPinId { get; set; }
    public Guid? ResultRevokedPinId { get; set; }
    public int ResultRevision { get; set; }
    public string ResultHash { get; set; } = string.Empty;
    public bool CreatedNewRow { get; set; }
    public DateTime CreatedAt { get; set; }

    public Agent Agent { get; set; } = null!;
}
