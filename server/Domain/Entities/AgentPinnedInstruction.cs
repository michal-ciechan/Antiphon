using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One immutable standing instruction for a named agent (CARD-0262). Text and provenance never
/// change; replacement revokes this row and inserts another in the same transaction.
/// </summary>
public class AgentPinnedInstruction
{
    public const int MaxTextLength = 500;
    public const int MaxSourceNamespaceLength = 64;
    public const int MaxSourceKeyLength = 200;
    public const int MaxSourceRefLength = 200;
    public const int MaxActivePerAgent = 20;

    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string Text { get; set; } = string.Empty;
    public PinInstructionSource Source { get; set; }
    public string? SourceNamespace { get; set; }
    public string? SourceKey { get; set; }
    public string? SourceRef { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? CreatedBySessionId { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public Guid? RevokedBySessionId { get; set; }
    public Guid? SupersedesPinId { get; set; }

    public Agent Agent { get; set; } = null!;
}
