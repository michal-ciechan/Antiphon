using System.Text;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Review;

/// <summary>What categorise concluded: the change kind, the blast radius on a 1-5 scale each, and the categories.</summary>
public sealed record CategorisationOutcome(
    string ChangeKind,
    int Reach,
    int ExternalContract,
    int Data,
    int RollbackAtRelease,
    int ReversalAfterMonths,
    int Exposure,
    IReadOnlyList<string> Categories);

/// <summary>
/// The categorise node (design 4.3): one batched decision call over a state built from the title,
/// description, changed files and a diff excerpt. The per-risk-area questions and code-only
/// evidence of design 4.3 come later.
/// </summary>
public static class ReviewCategorisation
{
    public const string DefaultChain = "decisions-default";
    public const int MaxDescriptionChars = 4_000;
    public const int MaxStateFiles = 300;
    public const int MaxDiffChars = 24_000;

    /// <summary>A yes/no answer at or above this probability of yes adds its category.</summary>
    public const double CategoryThreshold = 0.5;

    public static readonly IReadOnlyList<string> ChangeKinds = ["feature", "bugfix", "refactor", "test", "docs", "infra", "dependency"];

    /// <summary>The design 4.3 question set, in its order.</summary>
    public static readonly IReadOnlyList<DecisionQuestion> Questions =
    [
        new("change_kind", DecisionQuestionKind.Choice, "What kind of change is this?", ChangeKinds.Select(k => new DecisionOption(k)).ToList()),
        DecisionQuestion.Noul("external_contract",
            "Does this change an API, message schema, public type, CLI flag or config key that other teams, systems or users rely on?"),
        DecisionQuestion.Noul("persistent_data",
            "Does this change database schema, migrations, stored data, or a persisted file or message format?"),
        Score("reach", "How many components, callers or services does this affect?",
            "One place", "A few", "A module", "Many modules", "System-wide"),
        Score("rollback_at_release", "If this breaks on release, how hard is it to roll back?",
            "Trivial", "Easy", "Moderate", "Hard", "Very hard"),
        Score("reversal_after_months", "After months in production, how hard is it to undo this design?",
            "Trivial", "Easy", "Moderate", "Hard", "One-way door"),
        Score("exposure", "How user-facing and high-traffic is the changed behaviour?",
            "Internal only", "Low", "Medium", "High", "Critical path"),
    ];

    /// <summary>The decision state: title, description, scope, the file list with stats, and the diff up to <see cref="MaxDiffChars"/>.</summary>
    public static string BuildState(string title, string? description, string scope, IReadOnlyList<CodeHostChangedFile> files, string? diff,
        string? diffNote)
    {
        var additions = files.Sum(f => f.Additions);
        var deletions = files.Sum(f => f.Deletions);
        var state = new StringBuilder()
            .Append("Title: ").AppendLine(title)
            .AppendLine()
            .AppendLine("Description:")
            .AppendLine(string.IsNullOrWhiteSpace(description) ? "(none)" : Truncate(description.Trim(), MaxDescriptionChars))
            .AppendLine()
            .Append("Scope: ").AppendLine(scope)
            .Append("Changed files: ").Append(files.Count).Append(", +").Append(additions).Append(" -").Append(deletions)
            .Append(", size ").AppendLine(ReviewIntake.SizeBucket(additions + deletions));
        foreach (var file in files.Take(MaxStateFiles))
            state.AppendLine(ReviewIntake.FileLine(file));
        if (files.Count > MaxStateFiles)
            state.Append("... and ").Append(files.Count - MaxStateFiles).AppendLine(" more files");
        state.AppendLine();
        if (diffNote is not null)
            state.AppendLine(diffNote);
        if (!string.IsNullOrEmpty(diff))
        {
            state.AppendLine(diff.Length > MaxDiffChars ? $"Diff (first {MaxDiffChars} characters):" : "Diff:")
                .AppendLine(Truncate(diff, MaxDiffChars));
        }

        return state.ToString();
    }

    /// <summary>
    /// The outcome, or null with the questions that have no answer: refused by every entry of the
    /// chain. A yes/no probability maps to 1-5 as <c>1 + round(4p)</c>; a score is its level's position.
    /// </summary>
    public static CategorisationOutcome? Read(IReadOnlyDictionary<string, DecisionAnswer> answers, out IReadOnlyList<string> unanswered)
    {
        var missing = new List<string>();
        string? Choice(string name) => Answer(name) is { Value: { } value } ? value : Missing(name);
        int Probability(string name) => Answer(name)?.Probabilities is { } p && p.TryGetValue("yes", out var yes)
            ? 1 + (int)Math.Round(Math.Clamp(yes, 0, 1) * 4, MidpointRounding.AwayFromZero)
            : MissingScore(name);
        int Level(string name)
        {
            var question = Questions.Single(q => q.Name == name);
            var index = Answer(name) is { Value: { } value } ? question.Options.ToList().FindIndex(o => o.Value == value) : -1;
            return index >= 0 ? index + 1 : MissingScore(name);
        }

        DecisionAnswer? Answer(string name) => answers.TryGetValue(name, out var a) && !a.Refused ? a : null;
        string? Missing(string name)
        {
            missing.Add(name);
            return null;
        }

        int MissingScore(string name)
        {
            missing.Add(name);
            return 0;
        }

        var kind = Choice("change_kind");
        var contract = Probability("external_contract");
        var data = Probability("persistent_data");
        var outcome = new CategorisationOutcome(kind ?? "", Level("reach"), contract, data, Level("rollback_at_release"),
            Level("reversal_after_months"), Level("exposure"), []);
        unanswered = missing;
        if (missing.Count > 0)
            return null;

        var categories = new List<string> { kind! };
        if (YesProbability(answers["external_contract"]) >= CategoryThreshold)
            categories.Add("external-contract");
        if (YesProbability(answers["persistent_data"]) >= CategoryThreshold)
            categories.Add("persistent-data");
        return outcome with { Categories = categories };
    }

    public static string Describe(CategorisationOutcome o) =>
        $"{o.ChangeKind}; reach {o.Reach}, contract {o.ExternalContract}, data {o.Data}, rollback {o.RollbackAtRelease}, "
        + $"reversal {o.ReversalAfterMonths}, exposure {o.Exposure} (1-5); categories {string.Join(", ", o.Categories)}";

    private static double YesProbability(DecisionAnswer answer) =>
        answer.Probabilities is { } p && p.TryGetValue("yes", out var yes) ? yes : 0;

    private static DecisionQuestion Score(string name, string instructions, params string[] levels) =>
        new(name, DecisionQuestionKind.Score, instructions, levels.Select(l => new DecisionOption(l)).ToList());

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
