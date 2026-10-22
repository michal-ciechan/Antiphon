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
    [property: JsonConverter(typeof(RepositoryVisibilityConverter))] RepositoryVisibility? RepositoryVisibility = null,
    string? DefaultWorkerWorkspace = null,
    // CARD-0167 S2. Null/omitted = none. Same validation as the update request.
    IReadOnlyDictionary<string, ProviderEndpointDto?>? ProviderEndpoints = null);
