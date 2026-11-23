using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Code host polling (review engine design 8.5), section <c>Review:Polling</c>. There is no public
/// webhook at home, so polling is the integration: each pass lists the open PRs of every configured
/// repo, starts a run for a new one, refreshes a run whose head moved or whose PR is no longer open,
/// and acts on <c>/antiphon review</c> and <c>/antiphon recheck</c> PR comments. Off by default;
/// <c>POST /api/review-polling/poll</c> runs a pass on demand either way.
/// </summary>
public sealed class ReviewPollingSettings
{
    public const string SectionName = "Review:Polling";
    public const int DefaultIntervalSeconds = 120;
    public const int MinimumIntervalSeconds = 30;
    public const string DefaultCommandPrefix = "/antiphon";

    /// <summary>Runs a pass every <see cref="IntervalSeconds"/>. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>Seconds between passes; at least <see cref="MinimumIntervalSeconds"/>.</summary>
    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;

    /// <summary>The comment command prefix: <c>/antiphon review [steer]</c>, <c>/antiphon recheck</c>.</summary>
    public string CommandPrefix { get; set; } = DefaultCommandPrefix;

    public List<ReviewPollingRepo> Repos { get; set; } = [];

    [JsonIgnore]
    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(MinimumIntervalSeconds, IntervalSeconds));

    [JsonIgnore]
    public string Prefix => string.IsNullOrWhiteSpace(CommandPrefix) ? DefaultCommandPrefix : CommandPrefix.Trim();
}

/// <summary>
/// One polled repo. <see cref="Host"/> is the host kind; the host id and its credential come from
/// <c>Review:CodeHosts:Repos</c> as for any intake. The filters choose which open PRs get a run
/// automatically; a comment command from a trusted user starts one regardless.
/// </summary>
public sealed class ReviewPollingRepo
{
    public const string DefaultSkipLabel = "antiphon:skip";

    /// <summary><c>owner/name</c>.</summary>
    public string Repo { get; set; } = "";

    public ReviewHost Host { get; set; } = ReviewHost.GitHub;

    /// <summary>The base branches whose PRs are reviewed; empty means every base branch.</summary>
    public List<string> BaseBranches { get; set; } = [];

    public bool IncludeDrafts { get; set; }

    /// <summary>A PR with any of these labels gets no run. Absent: <c>antiphon:skip</c>.</summary>
    public List<string>? SkipLabels { get; set; }

    /// <summary>When set, only PRs with this label get a run.</summary>
    public string? RequireLabel { get; set; }

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveSkipLabels => SkipLabels is { Count: > 0 } labels ? labels : [DefaultSkipLabel];
}
