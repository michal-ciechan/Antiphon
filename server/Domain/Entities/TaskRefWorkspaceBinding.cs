using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>Durable ownership of one task ref. This record grants no filesystem custody.</summary>
public sealed class TaskRefWorkspaceBinding
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string RepositoryPath { get; set; } = "";
    public string CommonDirectory { get; set; } = "";
    public string IdentityPath { get; set; } = "";
    public string Branch { get; set; } = "";
    public string FullRef { get; set; } = "";
    public string SelectedBaseSha { get; set; } = "";
    public string SelectedBaseRef { get; set; } = "";
    public WorktreeBaseSource SelectedBaseSource { get; set; }
    public Guid? SelectedBaseTaskId { get; set; }
    public TaskRefWorkspacePreparationState PreparationState { get; set; }
    public long Revision { get; set; }
}
