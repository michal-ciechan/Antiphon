namespace Antiphon.Server.Application.Dtos;

public record UpdateProjectRequest(
    string Name,
    string GitRepositoryUrl,
    string? ConstitutionPath,
    bool GitHubIntegrationEnabled,
    bool NotificationsEnabled);
