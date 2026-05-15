using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class BoardColumn
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public string StateKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ColumnOrder { get; set; }
    public CardStatus CardStatus { get; set; }
    public bool IsActive { get; set; }
    public bool IsTerminal { get; set; }
    public int? MaxConcurrentSessions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Board Board { get; set; } = null!;
    public ICollection<Card> Cards { get; set; } = new List<Card>();
}
