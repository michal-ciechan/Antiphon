using System.Globalization;
using System.Text;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Review.Execution;

internal static class ReviewSummaryRenderer
{
    public const int MaxCustomBodyBytes = 48 * 1024;
    private const int MaxLegacyBodyCharacters = 65536;
    private const string Heading = "**Antiphon review**, round ";
    private const string TruncationNotice = "_Some findings were omitted from this summary due to the size limit. View all findings in Antiphon._";

    internal sealed record RenderedSummary(string Body, int SummaryOnlyCount);

    public static RenderedSummary Render(ReviewWalk walk, WorkflowNode node, ReviewPublishConfiguration? configuration, ReviewPublishInputs? inputs,
        IReadOnlyList<ReviewFinding> inline, IReadOnlyList<ReviewFinding> summaryOnly, int belowBar, VerifyOutput? verify, bool verifySkipped,
        string? runLink = null)
    {
        var authored = node.Params.TryGetValue(ReviewPublishConfiguration.TemplateField, out var raw);
        var newline = authored ? "\n" : Environment.NewLine;
        var heading = $"{Heading}{walk.Run.Round} (iteration {walk.Iteration.Iteration}){newline}{newline}";
        var standardHeader = runLink is null ? heading : $"{heading}{runLink}{newline}{newline}";
        var footer = Footer(belowBar, verify, verifySkipped, newline);
        if (!authored)
        {
            var legacy = standardHeader + Findings(inline, summaryOnly, literal: false, newline).Body + footer;
            if (legacy.Length <= MaxLegacyBodyCharacters)
                return new(legacy, summaryOnly.Count);
            walk.Warnings.Add("Oversized Publish summary; used the bounded default summary.");
        }
        else
        {
            if (ReviewSummaryTemplate.TryParse(raw, configuration, out var template)
                && template!.TryRender(Section, MaxCustomBodyBytes, out var rendered))
            {
                var final = runLink is null ? rendered : WithRunLink(rendered, runLink, newline);
                if (Encoding.UTF8.GetByteCount(final) <= MaxCustomBodyBytes)
                    return new(final, summaryOnly.Count);
            }
            walk.Warnings.Add("Invalid or oversized Publish summary template; used the default summary.");
        }

        // Reserve whole Markdown blocks before accepting any finding records.
        var notice = $"{newline}{TruncationNotice}{newline}";
        var budget = MaxCustomBodyBytes - Encoding.UTF8.GetByteCount(standardHeader + footer + notice);
        if (budget < 0)
        {
            standardHeader = heading;
            budget = MaxCustomBodyBytes - Encoding.UTF8.GetByteCount(standardHeader + footer + notice);
        }

        var fallback = Findings(inline, summaryOnly, literal: true, newline, budget);
        return new(standardHeader + fallback.Body + footer + (fallback.Truncated ? notice : ""), fallback.SummaryOnlyCount);

        string Section(string name) => name switch
        {
            "run.title" => Literal(walk.Run.Subject.Title),
            "run.subject" => Literal(walk.Run.Subject.PrNumber is { } number
                ? $"{walk.Run.Subject.Repo} #{number}" : walk.Run.Subject.Repo),
            "round" => walk.Run.Round.ToString(CultureInfo.InvariantCulture),
            "iteration" => walk.Iteration.Iteration.ToString(CultureInfo.InvariantCulture),
            "defaultBody" => (heading + Findings(inline, summaryOnly, literal: true, newline).Body + footer).TrimEnd(),
            "findings" => Findings(inline, summaryOnly, literal: true, newline).Body.TrimEnd(),
            "belowBar" => BelowBar(belowBar) ?? "",
            "verification" => Verification(verify, verifySkipped) ?? "",
            "categorisation" => Categorisation(inputs?.Categorisation),
            "blastRadius" => BlastRadius(walk.Run),
            "categories" => $"**Categories across all iterations** · {Categories(walk.Run.Categories)}",
            "selectedChecks" => SelectedChecks(inputs?.Selection),
            _ => throw new InvalidOperationException("Unrecognised Publish summary slot."),
        };
    }

    /// <summary>Add the server-generated run link after rendering, outside template slots.</summary>
    private static string WithRunLink(string body, string runLink, string newline)
    {
        if (body.StartsWith(Heading, StringComparison.Ordinal) && body.IndexOf(newline, StringComparison.Ordinal) is var end && end >= 0)
            return body.Insert(end + newline.Length, $"{newline}{runLink}{newline}");

        return $"{body.TrimEnd()}{newline}{newline}{runLink}{newline}";
    }

    private static string Footer(int belowBar, VerifyOutput? verify, bool verifySkipped, string newline)
    {
        var text = new StringBuilder();
        if (BelowBar(belowBar) is { } below)
            text.Append(newline).Append(below).Append(newline);
        if (Verification(verify, verifySkipped) is { } verification)
            text.Append(newline).Append(verification).Append(newline);
        return text.ToString();
    }

    private static (string Body, int SummaryOnlyCount, bool Truncated) Findings(IReadOnlyList<ReviewFinding> inline,
        IReadOnlyList<ReviewFinding> summaryOnly, bool literal, string newline, int? budget = null)
    {
        var text = new StringBuilder();
        var inlineCount = AppendGroup(inline, $"Findings:{newline}", includeLine: false);
        var summaryOnlyCount = AppendGroup(summaryOnly, $"{newline}Listed here only:{newline}", includeLine: true);
        return (text.ToString(), summaryOnlyCount, inlineCount < inline.Count || summaryOnlyCount < summaryOnly.Count);

        int AppendGroup(IReadOnlyList<ReviewFinding> findings, string label, bool includeLine)
        {
            var count = 0;
            foreach (var finding in findings)
            {
                var key = Display(finding.Key);
                var severity = finding.Severity.ToString().ToLowerInvariant();
                var path = includeLine ? $"{finding.FilePath}:{finding.LineStart}" : finding.FilePath;
                var location = literal ? Literal(path) : "\x60" + path + "\x60";
                var prefix = includeLine ? $"- {key}" : $"- **{key}**";
                var record = $"{(count == 0 ? label : "")}{prefix} {severity} · {location}: {Display(finding.Title)}{newline}";
                if (budget is int remaining)
                {
                    var bytes = Encoding.UTF8.GetByteCount(record);
                    if (bytes > remaining)
                        break;
                    budget = remaining - bytes;
                }

                text.Append(record);
                count++;
            }
            return count;
        }

        string Display(string value) => literal ? Literal(value) : value;
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
        : $"**Categorisation this iteration** · {Literal(outcome.ChangeKind)}\n"
          + $"{Scores(outcome.Reach, outcome.ExternalContract, outcome.Data, outcome.RollbackAtRelease, outcome.ReversalAfterMonths, outcome.Exposure)}"
          + $"\n**Categories this iteration** · {Categories(outcome.Categories)}";

    private static string BlastRadius(ReviewRun run) =>
        $"**Blast radius across all iterations**\n"
        + Scores(run.BlastReach, run.BlastExternalContract, run.BlastData, run.BlastRollbackAtRelease, run.BlastReversalAfterMonths, run.BlastExposure);

    private static string Scores(int? reach, int? externalContract, int? data, int? rollback, int? reversal, int? exposure) =>
        string.Join("\n", new[]
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
        return checks.Count == 0 ? heading + " · None" : heading + "\n" + string.Join("\n", checks);
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
