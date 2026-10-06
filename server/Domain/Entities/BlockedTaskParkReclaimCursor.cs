namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Singleton traversal cursor for the legacy Blocked sweep. Losing it would restart at the
/// first page; persisting it is what makes a restart continue instead of skipping or pinning.
/// </summary>
public sealed class BlockedTaskParkReclaimCursor
{
    public int Id { get; set; }
    public Guid? AfterTaskId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
