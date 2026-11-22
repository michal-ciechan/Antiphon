namespace Antiphon.Server.Application.Settings;

/// <summary>
/// The review policy (design section 5) until <c>.antiphon/review/policy.yaml</c> is read: section
/// <c>Review:Policy</c>, with the design's defaults in code. List settings are null until
/// configured, so a configured list replaces the default instead of being appended to it; read the
/// <c>Effective*</c> members.
/// </summary>
public sealed class ReviewPolicySettings
{
    public const string SectionName = "Review:Policy";

    /// <summary><c>rounds.humanGateAfterBotOnlyRounds</c>: bot-only rounds before a human review opens.</summary>
    public int HumanGateAfterBotOnlyRounds { get; set; } = 3;

    /// <summary><c>rounds.post</c>: the posting bar per round; the last row applies to every later round.</summary>
    public List<ReviewRoundBarSettings>? Post { get; set; }

    /// <summary><c>rounds.bypassCategories</c> (D-35): posted in any round, in their own reviewer group.</summary>
    public List<string>? BypassCategories { get; set; }

    /// <summary><c>approval.humanRequiredWhen.categories</c>, matched against the run's categories.</summary>
    public List<string>? HumanRequiredCategories { get; set; }

    /// <summary><c>approval.humanRequiredWhen.blastRadius.external_contract</c> on the 1-5 scale (p 0.6 maps to 3; 4 is p &gt;= 0.625).</summary>
    public int HumanRequiredExternalContractAtLeast { get; set; } = 4;

    /// <summary><c>approval.humanRequiredWhen.blastRadius.reversal_after_months</c>, 1-5.</summary>
    public int HumanRequiredReversalAtLeast { get; set; } = 4;

    /// <summary><c>approval.humanRequiredWhen.size</c>: these size buckets are large.</summary>
    public List<string>? LargeSizes { get; set; }

    /// <summary><c>approval.humanRequiredWhen.concededDisputes</c>.</summary>
    public int HumanRequiredConcededDisputesAtLeast { get; set; } = 2;

    /// <summary><c>approval.humanRequiredWhen.unverifiedHighSeverity</c>.</summary>
    public bool HumanRequiredWhenUnverifiedHighSeverity { get; set; } = true;

    /// <summary><c>baseDrift.affectedThreshold</c> (D-18): p(yes) at or above it is affected.</summary>
    public double BaseDriftAffectedThreshold { get; set; } = 0.35;

    /// <summary><c>afterApproval.keepUpToDate</c> (D-37 option B): compare the merge base with the base tip.</summary>
    public bool KeepUpToDate { get; set; } = true;

    /// <summary>Base commits touching these paths overlap a PR whatever it changed (design 4.13 step 1).</summary>
    public List<string>? ContractGlobs { get; set; }

    /// <summary>A candidate check is selected when p(applies) is at or above this.</summary>
    public double SelectThreshold { get; set; } = 0.5;

    /// <summary>The verifier drops a finding when p(holds) is below this (never a bypass finding alone).</summary>
    public double VerifyDropBelow { get; set; } = 0.5;

    /// <summary>The group suffix budget: the selected checks' <c>@model</c> sections, in estimated tokens.</summary>
    public int GroupTokenBudget { get; set; } = 6000;

    /// <summary>The diff's share of the pack prefix, in estimated tokens (four characters each).</summary>
    public int PackDiffTokenBudget { get; set; } = 24_000;

    public IReadOnlyList<ReviewRoundBarSettings> EffectivePost => Post is { Count: > 0 } post
        ? post.OrderBy(p => p.Round).ToList()
        :
        [
            new() { Round = 1, MinSeverity = "nit", MinConfidence = 0.5, MaxNits = 5, NitsInline = false },
            new() { Round = 2, MinSeverity = "minor", MinConfidence = 0.65 },
            new() { Round = 3, MinSeverity = "major", MinConfidence = 0.8 },
        ];

    public IReadOnlyList<string> EffectiveBypassCategories => BypassCategories ?? ["security", "data-loss"];

    public IReadOnlyList<string> EffectiveHumanRequiredCategories =>
        HumanRequiredCategories ?? ["migration", "auth", "security", "public-api", "infra", "session-runtime"];

    public IReadOnlyList<string> EffectiveLargeSizes => LargeSizes ?? ["L", "XL"];

    public IReadOnlyList<string> EffectiveContractGlobs => ContractGlobs
        ?? ["**/*.proto", "**/openapi*.*", "**/*.graphql", "**/Migrations/**", "**/migrations/**", "**/*.sql"];
}

/// <summary>One row of <c>rounds.post</c>: the bar from <see cref="Round"/> on, until the next row.</summary>
public sealed class ReviewRoundBarSettings
{
    public int Round { get; set; } = 1;

    /// <summary><c>nit</c>, <c>minor</c>, <c>major</c> or <c>blocker</c>.</summary>
    public string MinSeverity { get; set; } = "nit";

    public double MinConfidence { get; set; }

    /// <summary>At most this many nits are posted (in the summary unless <see cref="NitsInline"/>); null is no cap.</summary>
    public int? MaxNits { get; set; }

    public bool NitsInline { get; set; } = true;
}
