namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A project configuration pointing at a git repository with feature flags.
/// Projects are the top-level container for workflows (FR43, FR44, FR45, FR46).
/// </summary>
public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GitRepositoryUrl { get; set; } = string.Empty;
    public string? LocalRepositoryPath { get; set; }
    public string BaseBranch { get; set; } = "master";
    public string ConstitutionPath { get; set; } = "AGENTS.md;CLAUDE.md;README.md";
    public bool GitHubIntegrationEnabled { get; set; }
    public bool NotificationsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Set when the project is archived. Archive is what "delete" means here — the row stays so
    /// boards, agents and history never dangle, and the list endpoints can hide residue without
    /// destroying it.
    /// </summary>
    public DateTime? ArchivedAt { get; set; }
    public string? ArchivedReason { get; set; }
    public string? ArchivedBy { get; set; }

    /// <summary>
    /// Default launch environment inherited by every agent and pool delegate under this
    /// project unless the agent's own <c>LaunchEnvJson</c> (or a launch-time override) sets
    /// the same variable (CARD-0106 gap 2). JSON object, default <c>{}</c>. Values may
    /// reference stored API keys as <c>{{key:NAME}}</c>; reserved <c>ANTIPHON_*</c> names
    /// are refused at write time.
    /// </summary>
    public string DefaultLaunchEnvJson { get; set; } = "{}";

    public ICollection<Board> Boards { get; set; } = new List<Board>();

    /// <summary>
    /// Project-scoped API keys (CARD-0106). Cascade-deleted with the project; an agent still
    /// referencing a deleted key fails its next launch loudly rather than launching without it.
    /// </summary>
    public ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
}
