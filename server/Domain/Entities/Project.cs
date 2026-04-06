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
}
