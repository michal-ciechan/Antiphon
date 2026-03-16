namespace Antiphon.Server.Application.Dtos;

public record CreateProjectRequest(
    string Name,
    string GitRepositoryUrl,
    string? ConstitutionPath,
    bool GitHubIntegrationEnabled,
    bool NotificationsEnabled);
