using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Issue / rotate / revoke audit for a <see cref="DelegationCapability"/>. Detail names the
/// capability and its roots — never the raw token and never the hash.
/// </summary>
public class DelegationCapabilityEvent
{
    public const int DetailMaxLength = 4000;

    public Guid Id { get; set; }
    public Guid CapabilityId { get; set; }
    public DelegationCapabilityEventType Type { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime At { get; set; }

    public DelegationCapability Capability { get; set; } = null!;
}
