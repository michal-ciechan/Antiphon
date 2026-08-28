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
    string? ArchivedBy = null);

/// <summary>
/// Archive is what "delete" means for a project: the row stays, so boards and agents never dangle.
/// Projects have no concurrency token (unlike cards); the reason is the whole request.
/// </summary>
public sealed record ArchiveProjectRequest(string Reason, string? ArchivedBy = null);

/// <summary>Undoing a project archive — same reason contract; mistakes need correcting too.</summary>
public sealed record UnarchiveProjectRequest(string Reason, string? UnarchivedBy = null);
