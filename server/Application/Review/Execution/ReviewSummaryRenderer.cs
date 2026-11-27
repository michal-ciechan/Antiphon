using System.Globalization;
using System.Text;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Review.Execution;

internal static class ReviewSummaryRenderer
{
    public const int MaxCustomBodyBytes = 48 * 1024;
    private const string Heading = "**Antiphon review**, round ";

    /// <summary>Add the server-generated run link after rendering and keep it outside template slots.</summary>
    public static string WithRunLink(string body, string runLink)
    {
        var newline = Environment.NewLine;
        if (body.StartsWith(Heading, StringComparison.Ordinal) && body.IndexOf(newline, StringComparison.Ordinal) is var end && end >= 0)
            return body.Insert(end + newline.Length, $"{newline}{runLink}{newline}");

        return $"{body.TrimEnd()}{newline}{newline}{runLink}{newline}";
    }

    public static string Render(ReviewWalk walk, WorkflowNode node, ReviewPublishConfiguration? configuration, ReviewPublishInputs? inputs,
        IReadOnlyList<ReviewFinding> inline, IReadOnlyList<ReviewFinding> summaryOnly, int belowBar, VerifyOutput? verify, bool verifySkipped)
    {
        if (!node.Params.TryGetValue(ReviewPublishConfiguration.TemplateField, out var raw))
            return DefaultBody(walk, inline, summaryOnly, belowBar, verify, verifySkipped, literal: false);

        if (ReviewSummaryTemplate.TryParse(raw, configuration, out var template)
            && template!.TryRender(Section, MaxCustomBodyBytes, out var rendered))
            return rendered;

        walk.Warnings.Add("Invalid or oversized Publish summary template; used the default summary.");
        return DefaultBody(walk, inline, summaryOnly, belowBar, verify, verifySkipped, literal: false);

        string Section(string name) => name switch
        {
            "run.title" => Literal(walk.Run.Subject.Title),
            "run.subject" => Literal(walk.Run.Subject.PrNumber is { } number
                ? $"{walk.Run.Subject.Repo} #{number}" : walk.Run.Subject.Repo),
            "round" => walk.Run.Round.ToString(CultureInfo.InvariantCulture),
            "iteration" => walk.Iteration.Iteration.ToString(CultureInfo.InvariantCulture),
            "defaultBody" => DefaultBody(walk, inline, summaryOnly, belowBar, verify, verifySkipped, literal: true).TrimEnd(),
            "findings" => Findings(inline, summaryOnly, literal: true).TrimEnd(),
            "belowBar" => BelowBar(belowBar) ?? "",
            "verification" => Verification(verify, verifySkipped) ?? "",
            "categorisation" => Categorisation(inputs?.Categorisation),
            "blastRadius" => BlastRadius(walk.Run),
            "categories" => $"**Categories across all iterations** · {Categories(walk.Run.Categories)}",
            "selectedChecks" => SelectedChecks(inputs?.Selection),
            _ => throw new InvalidOperationException("Unrecognised Publish summary slot."),
        };
    }

    private static string DefaultBody(ReviewWalk walk, IReadOnlyList<ReviewFinding> inline, IReadOnlyList<ReviewFinding> summaryOnly,
        int belowBar, VerifyOutput? verify, bool verifySkipped, bool literal)
    {
        var text = new StringBuilder()
            .Append(Heading).Append(walk.Run.Round).Append(" (iteration ").Append(walk.Iteration.Iteration).AppendLine(")")
            .AppendLine()
            .Append(Findings(inline, summaryOnly, literal));
        if (BelowBar(belowBar) is { } below)
            text.AppendLine().AppendLine(below);
        if (Verification(verify, verifySkipped) is { } verification)
            text.AppendLine().AppendLine(verification);
        return text.ToString();
    }

    private static string Findings(IReadOnlyList<ReviewFinding> inline, IReadOnlyList<ReviewFinding> summaryOnly, bool literal)
    {
        var text = new StringBuilder();
        if (inline.Count > 0)
        {
            text.AppendLine("Findings:");
            foreach (var finding in inline)
                text.Append("- **").Append(Display(finding.Key)).Append("** ").Append(finding.Severity.ToString().ToLowerInvariant())
                    .Append(" · ").Append(Path(finding, includeLine: false)).Append(": ").AppendLine(Display(finding.Title));
        }

        if (summaryOnly.Count > 0)
        {
            text.AppendLine().AppendLine("Listed here only:");
            foreach (var finding in summaryOnly)
                text.Append("- ").Append(Display(finding.Key)).Append(' ').Append(finding.Severity.ToString().ToLowerInvariant())
                    .Append(" · ").Append(Path(finding, includeLine: true)).Append(": ").AppendLine(Display(finding.Title));
        }
        return text.ToString();

        string Display(string value) => literal ? Literal(value) : value;
        string Path(ReviewFinding finding, bool includeLine)
        {
            var path = includeLine ? $"{finding.FilePath}:{finding.LineStart}" : finding.FilePath;
            return literal ? Literal(path) : "\x60" + path + "\x60";
        }
    }

    private static string? BelowBar(int count) => count > 0 ? $"{count} more below this round's bar are kept in Antiphon." : null;

    private static string? Verification(VerifyOutput? verify, bool skipped) => (skipped, verify) switch
    {
        (true, _) => "_These findings were not verified: no verifier is configured._",
        (_, { Dropped.Count: > 0 }) => $"{verify.Dropped.Count} findings were disproved by the verifier and dropped.",
        _ => null,
    };

    private static string Categorisation(CategorisationOutcome? outcome) => outcome is null
        ? "**Categorisation this iteration** · Unavailable"
        : $"**Categorisation this iteration** · {Literal(outcome.ChangeKind)}{Environment.NewLine}"
          + $"{Scores(outcome.Reach, outcome.ExternalContract, outcome.Data, outcome.RollbackAtRelease, outcome.ReversalAfterMonths, outcome.Exposure)}"
          + $"{Environment.NewLine}**Categories this iteration** · {Categories(outcome.Categories)}";

    private static string BlastRadius(ReviewRun run) =>
        $"**Blast radius across all iterations**{Environment.NewLine}"
        + Scores(run.BlastReach, run.BlastExternalContract, run.BlastData, run.BlastRollbackAtRelease, run.BlastReversalAfterMonths, run.BlastExposure);

    private static string Scores(int? reach, int? externalContract, int? data, int? rollback, int? reversal, int? exposure) =>
        string.Join(Environment.NewLine, new[]
        {
            Score("Reach", reach, ["One place", "A few", "A module", "Many modules", "System-wide"]),
            Score("External contract", externalContract, ["None", "Unlikely", "Possible", "Likely", "Certain"]),
            Score("Persistent data", data, ["None", "Unlikely", "Possible", "Likely", "Certain"]),
            Score("Rollback at release", rollback, ["Trivial", "Easy", "Moderate", "Hard", "Very hard"]),
            Score("Reversal after months", reversal, ["Trivial", "Easy", "Moderate", "Hard", "One-way door"]),
            Score("Exposure", exposure, ["Internal only", "Low", "Medium", "High", "Critical path"]),
        });

    private static string Score(string label, int? score, string[] levels) => score is >= 1 and <= 5
        ? $"- {label}: {score} · {levels[score.Value - 1]}"
        : $"- {label}: Unavailable";

    private static string Categories(IReadOnlyList<string> categories) => categories.Count == 0
        ? "Unavailable"
        : string.Join(", ", categories.Select(Literal));

    private static string SelectedChecks(SelectOutput? selection)
    {
        const string heading = "**Selected checks**";
        if (selection is null)
            return heading + " · Unavailable";
        var checks = selection.Candidates.Where(c => c.Selected).OrderBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => $"- {Literal(c.Id)} · {Literal(c.Title)}").ToList();
        return checks.Count == 0 ? heading + " · None" : heading + Environment.NewLine + string.Join(Environment.NewLine, checks);
    }

    private static string Literal(string? value)
    {
        var text = new StringBuilder();
        foreach (var rune in (value ?? "").EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                if (text.Length == 0 || text[^1] != ' ')
                    text.Append(' ');
            }
            else if (rune.IsAscii && !char.IsAsciiLetterOrDigit((char)rune.Value))
            {
                // Entities keep dynamic punctuation out of Markdown syntax. A zero-width space also
                // breaks mentions, issue references and common autolinks in rendered text.
                text.Append("&#").Append(rune.Value).Append(';');
                if (rune.Value is '@' or '#' or '.' or ':')
                    text.Append("&#8203;");
            }
            else
            {
                text.Append(rune.ToString());
            }
        }

        return text.ToString().Trim();
    }
}
