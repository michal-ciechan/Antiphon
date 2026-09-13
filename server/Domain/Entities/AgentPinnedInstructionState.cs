namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Lazy one-row-per-agent pin store metadata. Created on first capture. Exact no-ops do not
/// rotate <see cref="Revision"/> or <see cref="ContentHash"/>.
/// </summary>
public class AgentPinnedInstructionState
{
    public Guid AgentId { get; set; }
    public int Revision { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTime FirstUsedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Agent Agent { get; set; } = null!;
}
