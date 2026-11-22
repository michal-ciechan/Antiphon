using System.Text.Json.Nodes;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review.Abf;

/// <summary>A legacy block (spec 6.6). Fields hold only declared keys, in the declared spelling.</summary>
public sealed record AbfLegacyRecord(string Type, int Line, bool Standalone, IReadOnlyList<KeyValuePair<string, List<string>>> Fields)
{
    public string? Last(string key) =>
        Fields.Where(f => f.Key == key).Select(f => f.Value[^1]).LastOrDefault();

    public IReadOnlyList<string> All(string key) =>
        Fields.Where(f => f.Key == key).SelectMany(f => f.Value).ToList();
}

/// <summary>The <c>next-stage</c> values (spec 6.4).</summary>
public sealed record AbfNextStageValues(bool Found, PipelineHandoffKind? Kind, string? RawToken, string? Handoff, string? ArtifactPath);

/// <summary>The <c>review-evidence</c> values (spec 6.5).</summary>
public sealed record AbfReviewEvidenceValues(
    bool Found,
    bool Usable,
    Guid? SubjectTaskId,
    string? ReviewedSourceSha,
    string? Warning,
    VerificationScope Scope,
    bool? ReviewedSourceClean);

/// <summary>The legacy document of a report (spec 6.6) and its typed values.</summary>
public sealed class AbfLegacyDocument
{
    public required IReadOnlyList<AbfLegacyRecord> Records { get; init; }
    public required AbfNextStageValues NextStage { get; init; }
    public required AbfReviewEvidenceValues ReviewEvidence { get; init; }

    public JsonObject ToJson()
    {
        var records = new JsonArray();
        foreach (var record in Records)
        {
            var fields = new JsonObject();
            foreach (var (key, values) in record.Fields)
            {
                fields[key] = values.Count == 1
                    ? JsonValue.Create(values[0])
                    : new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
            }

            records.Add(new JsonObject
            {
                ["type"] = record.Type,
                ["id"] = null,
                ["line"] = record.Line,
                ["standalone"] = record.Standalone,
                ["fields"] = fields,
                ["rows"] = new JsonArray(),
            });
        }

        return new JsonObject
        {
            ["abf"] = 1,
            ["type"] = "legacy",
            ["version"] = null,
            ["records"] = records,
            ["diagnostics"] = new JsonArray(),
        };
    }

    public JsonObject ValuesJson() => new()
    {
        ["next-stage"] = new JsonObject
        {
            ["found"] = NextStage.Found,
            ["kind"] = NextStage.Kind is { } kind ? PipelineHandoff.Token(kind) : null,
            ["rawToken"] = NextStage.RawToken,
            ["handoff"] = NextStage.Handoff,
            ["artifactPath"] = NextStage.ArtifactPath,
        },
        ["review-evidence"] = new JsonObject
        {
            ["found"] = ReviewEvidence.Found,
            ["usable"] = ReviewEvidence.Usable,
            ["subjectTaskId"] = ReviewEvidence.SubjectTaskId?.ToString("D"),
            ["reviewedSourceSha"] = ReviewEvidence.ReviewedSourceSha,
            ["warning"] = ReviewEvidence.Warning,
            ["scope"] = ReviewEvidence.Scope.ToString(),
            ["reviewedSourceClean"] = ReviewEvidence.ReviewedSourceClean,
        },
    };
}

/// <summary>
/// Legacy report blocks (spec section 6): <c>--- next stage ---</c>, <c>--- review evidence ---</c>
/// and any other <c>--- name ---</c>, read outside ABF documents with one profile per block type, so
/// every report without an ABF document reads as <see cref="PipelineHandoff"/> and
/// <see cref="ReviewEvidence"/> read it.
/// </summary>
public static class AbfLegacy
{
    private const string NextStageHeading = "--- next stage ---";
    private const string ReviewEvidenceHeading = "--- review evidence ---";
    private const int MaxHandoffChars = PipelineHandoff.MaxHandoffChars;

    private static readonly string[] NextStageKeys = ["next", "handoff", "artifact"];

    private static readonly string[] ReviewEvidenceKeys =
        ["subjectTaskId", "reviewedSourceSha", "ordinaryScopeCompleted", "reviewedSourceClean", "reviewRound", "reviewedRange"];

    private enum Profile
    {
        NextStage,
        ReviewEvidence,
        Generic,
    }

    /// <summary>Reads a delegate report with the report envelope (6.1) and the scan outside ABF documents (6.2).</summary>
    public static AbfLegacyDocument Read(string? report)
    {
        var lines = AbfLines.Prepare(report, reportEnvelope: true);
        var inSpan = new bool[lines.Length];
        foreach (var document in AbfScanner.FindHeaderDocuments(lines))
        {
            for (var k = document.SpanStart; k <= document.SpanEnd && k < lines.Length; k++)
                inSpan[k] = true;
        }

        var protectedLines = GenericFenceProtection(lines, inSpan);
        var headings = new List<(int Index, Profile Profile, bool Standalone, string Type)>();
        var evidenceFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (inSpan[i])
                continue;
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                evidenceFence = !evidenceFence;
                continue;
            }

            if (line.Equals(NextStageHeading, StringComparison.OrdinalIgnoreCase))
            {
                headings.Add((i, Profile.NextStage, true, "next-stage"));
                continue;
            }

            if (IsEvidenceHeadingAfterPrefix(line))
            {
                var standalone = !evidenceFence && line.Length == line.TrimStart().Length
                    && trimmed.Equals(ReviewEvidenceHeading, StringComparison.OrdinalIgnoreCase);
                headings.Add((i, Profile.ReviewEvidence, standalone, "review-evidence"));
                continue;
            }

            if (!protectedLines[i] && AbfLines.TryLegacyHeading(line, out var words)
                && !words.Equals("next stage", StringComparison.OrdinalIgnoreCase)
                && !words.Equals("review evidence", StringComparison.OrdinalIgnoreCase))
            {
                headings.Add((i, Profile.Generic, true, words.ToLowerInvariant().Replace(' ', '-').Replace('_', '-')));
            }
        }

        var records = headings
            .Select(h => new AbfLegacyRecord(h.Type, h.Index + 1, h.Standalone,
                h.Standalone ? ReadFields(lines, inSpan, h.Index, h.Profile) : []))
            .ToList();
        return new AbfLegacyDocument
        {
            Records = records,
            NextStage = NextStageValues(records),
            ReviewEvidence = ReviewEvidenceValues(records),
        };
    }

    private static List<KeyValuePair<string, List<string>>> ReadFields(string[] lines, bool[] inSpan, int heading, Profile profile)
    {
        var fields = new List<KeyValuePair<string, List<string>>>();
        for (var j = heading + 1; j < lines.Length && !inSpan[j]; j++)
        {
            var raw = lines[j];
            var line = raw.TrimEnd();
            if (line.Length == 0)
                continue;
            if (line.Trim().StartsWith("---", StringComparison.Ordinal))
                break;

            string key;
            string value;
            if (profile == Profile.Generic)
            {
                if (!AbfLines.TryField(raw, out key, out value))
                    continue;
                Add(fields, key, value);
                continue;
            }

            if (profile == Profile.ReviewEvidence && line.Length != line.TrimStart().Length)
                continue;
            var trimmed = line.Trim();
            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
                continue;
            key = trimmed[..colon].Trim();
            value = trimmed[(colon + 1)..].Trim();
            var declared = (profile == Profile.NextStage ? NextStageKeys : ReviewEvidenceKeys)
                .FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (declared is not null)
                Add(fields, declared, value);
        }

        return fields;
    }

    private static void Add(List<KeyValuePair<string, List<string>>> fields, string key, string value)
    {
        var index = fields.FindIndex(f => f.Key == key);
        if (index < 0)
            fields.Add(new(key, [value]));
        else
            fields[index].Value.Add(value);
    }

    private static AbfNextStageValues NextStageValues(List<AbfLegacyRecord> records)
    {
        var last = records.LastOrDefault(r => r.Type == "next-stage" && r.Standalone);
        if (last is null)
            return new AbfNextStageValues(false, null, null, null, null);

        var next = last.Last("next");
        PipelineHandoffKind? kind = PipelineHandoff.TryParseKind(next, out var parsed) ? parsed : null;
        var handoff = last.Last("handoff");
        if (handoff is { Length: > MaxHandoffChars })
            handoff = handoff[..MaxHandoffChars];
        string? artifactPath = null;
        if (last.Last("artifact") is { } artifact && !string.IsNullOrWhiteSpace(artifact))
        {
            var match = PipelineHandoff.DeliverablePathPattern.Match(artifact.Trim());
            if (match.Success && match.Length == artifact.Trim().Length)
                artifactPath = match.Groups["path"].Value;
        }

        return new AbfNextStageValues(true, kind, next, string.IsNullOrWhiteSpace(handoff) ? null : handoff, artifactPath);
    }

    private static AbfReviewEvidenceValues ReviewEvidenceValues(List<AbfLegacyRecord> records)
    {
        var evidence = records.Where(r => r.Type == "review-evidence").ToList();
        if (evidence.Count == 0)
            return new(false, false, null, null, null, VerificationScope.Unknown, null);
        var standalone = evidence.Where(r => r.Standalone).ToList();
        if (standalone.Count == 0)
            return new(true, false, null, null, ReviewEvidence.NotStandaloneWarning, VerificationScope.Unknown, null);
        if (standalone.Count > 1)
            return new(true, false, null, null, "review_evidence_duplicate", VerificationScope.Unknown, null);

        var block = standalone[0];
        var lastNextStage = records.LastOrDefault(r => r.Type == "next-stage");
        if (lastNextStage is not null && block.Line > lastNextStage.Line)
            return new(true, false, null, null, "review_evidence_after_next_stage", VerificationScope.Unknown, null);

        var scopes = block.All("ordinaryScopeCompleted");
        var scope = scopes.Count != 1 ? VerificationScope.Unknown : scopes[0] switch
        {
            var v when v.Equals("Full", StringComparison.OrdinalIgnoreCase) => VerificationScope.Full,
            var v when v.Equals("Interim", StringComparison.OrdinalIgnoreCase) => VerificationScope.Interim,
            var v when v.Equals("None", StringComparison.OrdinalIgnoreCase) => VerificationScope.None,
            _ => VerificationScope.Unknown,
        };
        var cleans = block.All("reviewedSourceClean");
        bool? clean = cleans.Count == 1 ? cleans[0] switch { "true" => true, "false" => false, _ => null } : null;

        var subject = block.Last("subjectTaskId");
        if (string.IsNullOrWhiteSpace(subject) || !Guid.TryParse(subject, out var subjectId) || subjectId == Guid.Empty)
            return new(true, false, null, null, "review_evidence_subject_invalid", scope, clean);
        if (!GitObjectId.TryNormalize(block.Last("reviewedSourceSha"), out var sha))
            return new(true, false, subjectId, null, "review_evidence_sha_invalid", scope, clean);
        return new(true, true, subjectId, sha, null, scope, clean);
    }

    /// <summary>Lines inside a closed fence (3.6), over the lines outside ABF documents, with no <c>=== end</c> bound.</summary>
    private static bool[] GenericFenceProtection(string[] lines, bool[] inSpan)
    {
        var outside = Enumerable.Range(0, lines.Length).Where(i => !inSpan[i]).ToList();
        var protectedLines = new bool[lines.Length];
        for (var p = 0; p < outside.Count; p++)
        {
            if (!AbfLines.TryFenceOpen(lines[outside[p]], out var fence))
                continue;
            var close = -1;
            for (var q = p + 1; q < outside.Count; q++)
            {
                if (AbfLines.IsFenceClose(lines[outside[q]], fence))
                {
                    close = q;
                    break;
                }
            }

            if (close < 0)
                continue;
            for (var q = p + 1; q < close; q++)
                protectedLines[outside[q]] = true;
            p = close;
        }

        return protectedLines;
    }

    private static bool IsEvidenceHeadingAfterPrefix(string line)
    {
        var remaining = line.AsSpan();
        while (true)
        {
            while (!remaining.IsEmpty && remaining[0] is ' ' or '\t')
                remaining = remaining[1..];
            if (remaining.IsEmpty || remaining[0] != '>')
                break;
            remaining = remaining[1..];
        }

        return remaining.Trim().Equals(ReviewEvidenceHeading.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }
}
