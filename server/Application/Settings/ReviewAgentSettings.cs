using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// The reviewer agents (review engine design 4.7), section <c>Review:Agents</c>. Each reviewer group is
/// one read-only delegate task: <see cref="Role"/> (Review), a fresh worktree cut at the reviewed head,
/// <see cref="Kind"/> and the level of the group's tier, on <see cref="Runner"/> when set. A subject's
/// repo maps to an Antiphon project by <see cref="Repos"/>, else by the project's
/// <c>GitRepositoryUrl</c> (GitHub) or <c>LocalRepositoryPath</c> (local git).
/// </summary>
public sealed class ReviewAgentSettings
{
    public const string SectionName = "Review:Agents";

    /// <summary>The agent program every reviewer runs. Only the delegatable kinds (ClaudeCode, Codex, Grok) are accepted.</summary>
    public AgentKind Kind { get; set; } = AgentKind.ClaudeCode;

    /// <summary>The level per group tier (<c>deep</c>, <c>standard</c>); a tier with no row takes <see cref="DefaultLevel"/>.</summary>
    public Dictionary<string, AgentModelLevel> Levels { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["deep"] = AgentModelLevel.High,
        ["standard"] = AgentModelLevel.Medium,
    };

    public AgentModelLevel DefaultLevel { get; set; } = AgentModelLevel.Medium;

    /// <summary>The task role. Review by default; any non-specialist role is accepted.</summary>
    public AgentTaskRole Role { get; set; } = AgentTaskRole.Review;

    /// <summary>
    /// The session runner id (<c>delegate.ps1 -Runner</c>). Absent is automatic: the delegation default
    /// runner when it is eligible, else the desktop. <c>local</c> pins the desktop.
    /// </summary>
    public string? Runner { get; set; }

    /// <summary>The duration hint the task is created with (it schedules the first check-in, nothing else).</summary>
    public int ExpectedMinutes { get; set; } = 20;

    /// <summary>A reviewer task not finished this long after it was created is cancelled and its node fails.</summary>
    public int TimeoutMinutes { get; set; } = 90;

    /// <summary>
    /// A brief whose pack fits under this many characters carries the pack inline; a larger pack is
    /// read over HTTP from <c>GET /api/review-runs/{id}/packs/{iteration}/{group}</c>. The task goal
    /// limit is 20,000 characters.
    /// </summary>
    public int InlinePackMaxChars { get; set; } = 16_000;

    /// <summary>Repos mapped to a project explicitly, ahead of the project URL and path match.</summary>
    public List<ReviewAgentRepo> Repos { get; set; } = [];

    public ReviewAgentRepo? FindRepo(string repo) =>
        Repos.FirstOrDefault(r => string.Equals(r.Repo.Trim(), repo, StringComparison.OrdinalIgnoreCase));

    public AgentModelLevel LevelFor(string? tier) =>
        tier is not null && Levels.TryGetValue(tier, out var level) ? level : DefaultLevel;
}

/// <summary>One repo's project: by id or by name, with the local checkout to branch from when the project has none.</summary>
public sealed class ReviewAgentRepo
{
    /// <summary><c>owner/name</c>, as the review subject names it.</summary>
    public string Repo { get; set; } = "";

    public Guid? ProjectId { get; set; }

    /// <summary>The project name (case-insensitive), when no id is given.</summary>
    public string? Project { get; set; }

    /// <summary>The git checkout the reviewer's worktree is cut from; absent, the project's <c>LocalRepositoryPath</c>.</summary>
    public string? LocalPath { get; set; }
}
