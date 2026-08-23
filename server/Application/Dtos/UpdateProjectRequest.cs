namespace Antiphon.Server.Application.Dtos;

public record UpdateProjectRequest(
    string Name,
    string GitRepositoryUrl,
    string? ConstitutionPath,
    bool GitHubIntegrationEnabled,
    bool NotificationsEnabled,
    string? LocalRepositoryPath,
    string? BaseBranch,
    // Null = leave unchanged (an older UI build PUTting a project must not wipe a default env
    // somebody configured). An empty dictionary is the explicit clear. ANTIPHON_* refused 422.
    IReadOnlyDictionary<string, string>? DefaultLaunchEnv = null);
