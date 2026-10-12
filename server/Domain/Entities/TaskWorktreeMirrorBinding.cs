namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-1032. One ordinary mirror creation. Append-only: a later runner assignment does not
/// replace this row, and a migration must not invent one for a historical path.
/// </summary>
public sealed class TaskWorktreeMirrorBinding
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public int Attempt { get; set; }
    public Guid PreparationId { get; set; }
    public string RunnerId { get; set; } = "";
    public string RunnerStoreId { get; set; } = "";
    public Guid BindingId { get; set; }
    public string RepositoryIdentity { get; set; } = "";
    public string CommonDirectory { get; set; } = "";
    public string GitDirectory { get; set; } = "";
    public string WorktreePath { get; set; } = "";
    public string SourceFullRef { get; set; } = "";
    public string CreationBaselineSha { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
