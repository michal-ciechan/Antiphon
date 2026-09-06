using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public record CreateProjectRequest(
    string Name,
    string GitRepositoryUrl,
    string? ConstitutionPath,
    bool GitHubIntegrationEnabled,
    bool NotificationsEnabled,
    string? LocalRepositoryPath,
    string? BaseBranch,
    IReadOnlyDictionary<string, string>? DefaultLaunchEnv = null,
    [property: JsonConverter(typeof(RepositoryVisibilityConverter))] RepositoryVisibility? RepositoryVisibility = null);
