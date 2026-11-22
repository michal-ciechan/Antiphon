using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review;

/// <summary>Where a new finding goes when it is published (design 4.9).</summary>
public enum ReviewPlacement
{
    /// <summary>Clears the round's bar: an inline comment in the PR review.</summary>
    Inline,

    /// <summary>Clears the bar but is listed in the summary comment only: a round-1 nit, or a line outside the diff.</summary>
    SummaryOnly,

    /// <summary>Below the bar: stored, not posted.</summary>
    BelowBar,
}

/// <summary>
/// The rounds policy (design 5) as pure rules: the posting bar per round, bypass categories (D-35),
/// what blocks a clean round, and the route after a round. Defaults come from
/// <see cref="ReviewPolicySettings"/> until <c>policy.yaml</c> is read.
/// </summary>
public static class ReviewRoundsPolicy
{
    public const string RouteFindings = "findings";
    public const string RouteClean = "clean";
    public const string RouteBotRoundLimit = "bot-round-limit";

    /// <summary>Statuses a finding is still open in: it can block a round.</summary>
    public static readonly IReadOnlyList<ReviewFindingStatus> OpenStatuses =
        [ReviewFindingStatus.Open, ReviewFindingStatus.WillFix, ReviewFindingStatus.Disputed];

    /// <summary>The bar of the last <c>rounds.post</c> row at or before <paramref name="round"/>; the first row before it.</summary>
    public static ReviewRoundBarSettings BarFor(ReviewPolicySettings policy, int round)
    {
        var rows = policy.EffectivePost;
        return rows.LastOrDefault(r => r.Round <= round) ?? rows[0];
    }

    public static ReviewSeverity MinSeverity(ReviewRoundBarSettings bar) =>
        Enum.TryParse<ReviewSeverity>(bar.MinSeverity, ignoreCase: true, out var severity) ? severity : ReviewSeverity.Nit;

    /// <summary>True when any flag is a bypass category.</summary>
    public static bool IsBypass(IEnumerable<string> flags, ReviewPolicySettings policy) =>
        flags.Any(f => policy.EffectiveBypassCategories.Contains(f, StringComparer.OrdinalIgnoreCase));

    /// <summary>Clears the round's bar: a bypass finding always does (D-35).</summary>
    public static bool AboveBar(ReviewSeverity severity, double confidence, bool bypass, ReviewRoundBarSettings bar) =>
        bypass || (severity >= MinSeverity(bar) && confidence >= bar.MinConfidence);

    /// <summary>
    /// Whether an open finding keeps the round from being clean: an open blocker always does (D-21),
    /// a bypass finding always does, and any other open finding does when it clears this round's bar.
    /// A round-1 nit left open is below the bar from round 2 on.
    /// </summary>
    public static bool IsBlocking(ReviewFinding finding, int round, ReviewPolicySettings policy) =>
        OpenStatuses.Contains(finding.Status)
        && (finding.Severity == ReviewSeverity.Blocker || finding.BypassesRoundBar
            || AboveBar(finding.Severity, finding.Confidence, bypass: false, BarFor(policy, round)));

    /// <summary>
    /// Inline, summary-only or below the bar. A nit that clears the bar goes in the summary when the
    /// bar says <c>nitsInline: false</c>, and only <c>maxNits</c> of them are posted at all; a finding
    /// whose line is not in the diff cannot be an inline comment.
    /// </summary>
    public static ReviewPlacement Place(ReviewSeverity severity, double confidence, bool bypass, bool lineInDiff, ReviewRoundBarSettings bar,
        ref int nitsPosted)
    {
        if (!AboveBar(severity, confidence, bypass, bar))
            return ReviewPlacement.BelowBar;
        if (severity == ReviewSeverity.Nit && !bypass)
        {
            if (bar.MaxNits is { } max && nitsPosted >= max)
                return ReviewPlacement.BelowBar;
            nitsPosted++;
            if (!bar.NitsInline)
                return ReviewPlacement.SummaryOnly;
        }

        return lineInDiff ? ReviewPlacement.Inline : ReviewPlacement.SummaryOnly;
    }

    /// <summary>The route after a round (design 3.3 <c>route</c>): findings wait for fixes unless the bot-only limit is reached.</summary>
    public static string Route(int blocking, int botOnlyRounds, ReviewPolicySettings policy) =>
        blocking == 0 ? RouteClean
        : botOnlyRounds >= Math.Max(1, policy.HumanGateAfterBotOnlyRounds) ? RouteBotRoundLimit
        : RouteFindings;
}
