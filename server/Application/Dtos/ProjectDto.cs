namespace Antiphon.Server.Application.Dtos;

public record ProjectDto(
    Guid Id,
    string Name,
    string GitRepositoryUrl,
    string? LocalRepositoryPath,
    string BaseBranch,
    string ConstitutionPath,
    bool GitHubIntegrationEnabled,
    bool NotificationsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyDictionary<string, string> DefaultLaunchEnv,
    DateTime? ArchivedAt = null,
    string? ArchivedReason = null,
    string? ArchivedBy = null)
{
    public Antiphon.Server.Domain.Enums.RepositoryVisibility RepositoryVisibility { get; init; }
    public string[] CardFileWarnings { get; init; } = [];
    /// <summary>On, Off, or null (inherit the global default).</summary>
    public string? CommitOnSettle { get; init; }
    /// <summary>Resolved project-or-global switch. True means settlement will try to commit.</summary>
    public bool EffectiveCommitOnSettle { get; init; }
    /// <summary>Configured override; null inherits the global setting.</summary>
    public string? DefaultWorkerWorkspace { get; init; }
    /// <summary>Global setting used when the project override is cleared.</summary>
    public string GlobalDefaultWorkerWorkspace { get; init; } = "Worktree";
    /// <summary>Configured project-or-global preference. Dispatch starts honoring it in CARD-0458 S3.</summary>
    public string EffectiveWorkerWorkspace { get; init; } = "Worktree";
    /// <summary>False while CARD-0458 S1/S2 only stores and reports the preference.</summary>
    public bool DispatchHonorsWorkspaceDefault { get; init; }
    public Guid? DefaultPipelineDefinitionId { get; init; }
    public PipelineResolutionDto? Pipeline { get; init; }
}

/// <summary>
/// Archive is what "delete" means for a project: the row stays, so boards and agents never dangle.
/// Projects have no concurrency token (unlike cards); the reason is the whole request.
/// </summary>
public sealed record ArchiveProjectRequest(string Reason, string? ArchivedBy = null);

/// <summary>Undoing a project archive — same reason contract; mistakes need correcting too.</summary>
public sealed record UnarchiveProjectRequest(string Reason, string? UnarchivedBy = null);
