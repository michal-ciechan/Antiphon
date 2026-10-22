using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Enums;

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
    IReadOnlyDictionary<string, string>? DefaultLaunchEnv = null,
    [property: JsonConverter(typeof(RepositoryVisibilityConverter))] RepositoryVisibility? RepositoryVisibility = null,
    /// <summary>
    /// CARD-0527. On / Off / Inherit. Null leaves the stored value unchanged (the DefaultLaunchEnv
    /// rule). Inherit clears the column so the global default applies.
    /// </summary>
    string? CommitOnSettle = null,
    /// <summary>Shared, Worktree, or Inherit. Null leaves the stored value unchanged.</summary>
    string? DefaultWorkerWorkspace = null,
    /// <summary>
    /// CARD-0167 S2. Per-kind provider endpoints keyed by <c>AgentKind</c> name (ClaudeCode, Grok,
    /// Codex). Null leaves the stored map unchanged (the DefaultLaunchEnv rule); an empty map is the
    /// explicit clear. Validated by <c>ProviderEndpointSettings</c>: 422 on an unsupported kind, a
    /// bad URL, or a Codex endpoint without a key name.
    /// </summary>
    IReadOnlyDictionary<string, ProviderEndpointDto?>? ProviderEndpoints = null);
