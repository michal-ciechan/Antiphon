namespace Antiphon.Server.Application.Dtos;

public record ProjectDto(
    Guid Id,
    string Name,
    string GitRepositoryUrl,
    string ConstitutionPath,
    bool GitHubIntegrationEnabled,
    bool NotificationsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt);
