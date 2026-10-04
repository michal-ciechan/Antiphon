namespace Antiphon.Server.Domain.Entities;

/// <summary>A Done revision's durable inventory obligation; it never seals the set of task attempts.</summary>
public sealed class CardWorktreeCleanup
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public Guid DoneRevisionId { get; set; }
    public DateTime DoneAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastDiscoveredAt { get; set; }
    public Card Card { get; set; } = null!;
    public CardRevision DoneRevision { get; set; } = null!;
}
