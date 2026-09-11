using Antiphon.Server.Domain;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0488 D-2. Parses the standalone <c>--- review evidence ---</c> block a Review-stage
/// report places before the next-stage block. Settlement, not this parser, decides whether
/// the block is usable approval.
/// </summary>
public static class ReviewEvidence
{
    public const string Heading = "--- review evidence ---";

    public readonly record struct Result(
        bool Found,
        bool Usable,
        Guid? SubjectTaskId,
        string? ReviewedSourceSha,
        string? Warning);

    public static Result TryParse(string? report)
    {
        if (string.IsNullOrWhiteSpace(report))
            return default;

        var normalized = report.ReplaceLineEndings("\n");
        var searchable = TextBeforeClosingReportToken(normalized);
        var headings = StandaloneHeadingIndexes(searchable);
        if (headings.Count == 0)
            return default;
        if (headings.Count > 1)
            return new(true, false, null, null, "review_evidence_duplicate");

        var headingAt = headings[0];
        var nextStageAt = LastWholeLineIndex(searchable, PipelineHandoff.Heading);
        if (nextStageAt >= 0 && headingAt > nextStageAt)
            return new(true, false, null, null, "review_evidence_after_next_stage");

        var afterHeading = searchable[(headingAt + Heading.Length)..].TrimStart('\n');
        string? subject = null;
        string? sha = null;
        foreach (var rawLine in afterHeading.Split('\n'))
        {
            var trimmed = rawLine.TrimEnd();
            if (trimmed.Length == 0)
                continue;
            if (trimmed.TrimStart().StartsWith("---", StringComparison.Ordinal))
                break;
            if (trimmed.Length != trimmed.TrimStart().Length)
                continue;

            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            if (key.Equals("subjectTaskId", StringComparison.OrdinalIgnoreCase))
                subject = value;
            else if (key.Equals("reviewedSourceSha", StringComparison.OrdinalIgnoreCase))
                sha = value;
        }

        if (string.IsNullOrWhiteSpace(subject) || !Guid.TryParse(subject, out var subjectId) || subjectId == Guid.Empty)
            return new(true, false, null, null, "review_evidence_subject_invalid");
        if (!GitObjectId.TryNormalize(sha, out var normalizedSha))
            return new(true, false, subjectId, null, "review_evidence_sha_invalid");
        return new(true, true, subjectId, normalizedSha, null);
    }

    private static List<int> StandaloneHeadingIndexes(string text)
    {
        var found = new List<int>();
        var inFence = false;
        var offset = 0;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
                inFence = !inFence;
            else if (!inFence
                     && line.Length == line.TrimStart().Length
                     && !line.StartsWith('>')
                     && trimmed.Equals(Heading, StringComparison.OrdinalIgnoreCase))
                found.Add(offset);
            offset += line.Length + 1;
        }

        return found;
    }

    private static string TextBeforeClosingReportToken(string normalized)
    {
        var lines = normalized.Split('\n');
        var tokenLine = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("[antiphon-report:", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']'))
                tokenLine = i;
        }

        if (tokenLine < 0)
            return normalized;

        var cut = 0;
        for (var j = 0; j < tokenLine; j++)
            cut += lines[j].Length + 1;
        return normalized[..cut];
    }

    private static int LastWholeLineIndex(string text, string heading)
    {
        var last = -1;
        var searchFrom = 0;
        while (searchFrom <= text.Length - heading.Length)
        {
            var idx = text.IndexOf(heading, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                break;
            var startOk = idx == 0 || text[idx - 1] == '\n';
            var end = idx + heading.Length;
            var endOk = end == text.Length || text[end] == '\n';
            if (startOk && endOk)
                last = idx;
            searchFrom = idx + heading.Length;
        }

        return last;
    }
}
