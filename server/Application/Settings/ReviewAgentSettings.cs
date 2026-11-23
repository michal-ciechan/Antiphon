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

    /// <summary>
    /// How reviewers run: <see cref="ReviewAgentTransports.Delegation"/> (the default, a delegate task on a
    /// session runner) or <see cref="ReviewAgentTransports.Cli"/> (one-shot <c>claude -p</c> on this server,
    /// for an instance with no session runner). Read once at startup by Program.cs.
    /// </summary>
    public string Transport { get; set; } = ReviewAgentTransports.Delegation;

    /// <summary>The one-shot <c>claude -p</c> transport, section <c>Review:Agents:Cli</c>.</summary>
    public ReviewAgentCliSettings Cli { get; set; } = new();

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

/// <summary>The values of <see cref="ReviewAgentSettings.Transport"/>.</summary>
public static class ReviewAgentTransports
{
    public const string Delegation = "delegation";
    public const string Cli = "cli";

    /// <summary>True for <c>delegation</c>, <c>cli</c> or no value (case-insensitive). Program.cs validates it at startup.</summary>
    public static bool IsKnown(string? transport) =>
        string.IsNullOrWhiteSpace(transport)
        || transport.Trim().Equals(Delegation, StringComparison.OrdinalIgnoreCase)
        || transport.Trim().Equals(Cli, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for <c>cli</c>, false for <c>delegation</c> or no value; throws for anything else, so a typo never runs the wrong transport.</summary>
    public static bool IsCli(string? transport)
    {
        if (!IsKnown(transport))
            throw new InvalidOperationException(
                $"{ReviewAgentSettings.SectionName}:Transport is '{transport}'; it must be '{Delegation}' (the default) or '{Cli}'.");
        return !string.IsNullOrWhiteSpace(transport) && transport.Trim().Equals(Cli, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// One-shot <c>claude -p</c> (review engine design 7.2), section <c>Review:Agents:Cli</c>. The reviewer
/// transport uses all of it; the <c>claude-cli</c> completion and decision dialects use <see cref="Command"/>.
/// </summary>
public sealed class ReviewAgentCliSettings
{
    /// <summary>The program to run: <c>claude</c> on the PATH, or a full path to the executable.</summary>
    public string Command { get; set; } = "claude";

    /// <summary>The <c>--model</c> per group tier; a tier with no row takes <see cref="DefaultModel"/>.</summary>
    public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["deep"] = "opus",
        ["standard"] = "sonnet",
    };

    public string DefaultModel { get; set; } = "sonnet";

    /// <summary>At most this many reviewers run at once; the rest wait their turn.</summary>
    public int MaxParallel { get; set; } = 2;

    public string ModelFor(string? tier) =>
        tier is not null && Models.TryGetValue(tier, out var model) && !string.IsNullOrWhiteSpace(model) ? model.Trim() : DefaultModel;
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
