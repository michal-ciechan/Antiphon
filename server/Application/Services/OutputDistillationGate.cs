using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public enum DistillationGateVerdict
{
    Pass = 0,
    RejectedOverCompressed = 1,
    RejectedUnderCompressed = 2,
    DegradedEmpty = 3,
}

public readonly record struct DistillationGateResult(
    DistillationGateVerdict Verdict,
    IReadOnlyList<string> MissingAnchors)
{
    public bool Passed => Verdict == DistillationGateVerdict.Pass;

    public DistillationOutcome ToOutcome() => Verdict switch
    {
        DistillationGateVerdict.Pass => DistillationOutcome.Applied,
        DistillationGateVerdict.RejectedOverCompressed => DistillationOutcome.RejectedOverCompressed,
        DistillationGateVerdict.RejectedUnderCompressed => DistillationOutcome.RejectedUnderCompressed,
        _ => DistillationOutcome.DegradedEmpty,
    };

    public string? MissingAnchorsJson =>
        MissingAnchors.Count == 0 ? null : JsonSerializer.Serialize(MissingAnchors);
}

/// <summary>
/// Pure static gates over a raw report and a distillation (CARD-0330 D6). A failure withholds
/// the improvement; it never withholds the report. CARD-0146: a distillation that drops
/// <c>next:</c> or <c>handoff:</c> from a present <c>--- next stage ---</c> block fails.
/// </summary>
public static class OutputDistillationGate
{
    public const int DistilledMinChars = 120;

    public static DistillationGateResult Evaluate(
        string? raw,
        string? distilled,
        int distilledMaxChars = 1_500,
        double distilledMaxRatio = 0.6)
    {
        if (string.IsNullOrWhiteSpace(distilled))
            return new DistillationGateResult(DistillationGateVerdict.DegradedEmpty, []);

        var distilledText = distilled.Trim();
        if (distilledText.Length < DistilledMinChars)
            return new DistillationGateResult(DistillationGateVerdict.DegradedEmpty, []);

        var rawText = raw ?? "";
        var max = Math.Min(
            Math.Max(0, distilledMaxChars),
            (int)Math.Floor(Math.Max(0, distilledMaxRatio) * Math.Max(1, rawText.Length)));
        if (distilledText.Length > max)
            return new DistillationGateResult(DistillationGateVerdict.RejectedUnderCompressed, []);

        var missing = new List<string>();
        CollectRequiredMisses(rawText, distilledText, missing);
        CollectPathMisses(rawText, distilledText, missing);
        CollectHandoffMisses(rawText, distilledText, missing);

        if (missing.Count > 0)
            return new DistillationGateResult(DistillationGateVerdict.RejectedOverCompressed, missing);

        return new DistillationGateResult(DistillationGateVerdict.Pass, []);
    }

    private static void CollectRequiredMisses(string raw, string distilled, List<string> missing)
    {
        foreach (Match match in ShaPattern.Matches(raw))
        {
            foreach (Capture sha in match.Groups["sha"].Captures)
                Require(distilled, sha.Value, missing, "sha:");
        }
        foreach (Match match in CardPattern.Matches(raw))
            Require(distilled, match.Value, missing, "card:");
        foreach (Match match in UrlPattern.Matches(raw))
            Require(distilled, match.Value.TrimEnd(").,;".ToCharArray()), missing, "url:");
        foreach (Match match in AttachPattern.Matches(raw))
            Require(distilled, match.Value, missing, "attach:");
        foreach (Match match in AmountPattern.Matches(raw))
            Require(distilled, match.Value.TrimEnd('.'), missing, "amount:");
        foreach (Match match in CountPattern.Matches(raw))
            Require(distilled, match.Value, missing, "count:");
    }

    private static void CollectPathMisses(string raw, string distilled, List<string> missing)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var artifact = PipelineHandoff.TryParse(raw).ArtifactPath;
        foreach (Match match in PathPattern.Matches(raw))
        {
            var path = match.Value.TrimEnd(").,;[]".ToCharArray());
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;
            if (artifact is not null && string.Equals(path, artifact, StringComparison.Ordinal))
                continue;
            if (seen.Add(path))
                paths.Add(path);
        }

        if (paths.Count == 0)
            return;

        if (paths.Count <= 10)
        {
            foreach (var path in paths)
                Require(distilled, path, missing, "path:");
            return;
        }

        var need = (int)Math.Ceiling(paths.Count * 0.6);
        var hits = paths.Count(p => distilled.Contains(p, StringComparison.Ordinal));
        if (hits >= need)
            return;

        foreach (var path in paths)
        {
            if (!distilled.Contains(path, StringComparison.Ordinal))
                missing.Add("path:" + path);
        }
    }

    private static void CollectHandoffMisses(string raw, string distilled, List<string> missing)
    {
        var parsed = PipelineHandoff.TryParse(raw);
        if (!parsed.Found)
            return;

        if (!string.IsNullOrWhiteSpace(parsed.RawToken))
        {
            if (!ContainsHandoffKey(distilled, "next:")
                || !distilled.Contains(parsed.RawToken, StringComparison.Ordinal))
                missing.Add("next:" + parsed.RawToken.Trim());
        }

        if (!string.IsNullOrWhiteSpace(parsed.Handoff))
        {
            if (!ContainsHandoffKey(distilled, "handoff:")
                || !distilled.Contains(parsed.Handoff, StringComparison.Ordinal))
                missing.Add("handoff:");
        }
    }

    private static bool ContainsHandoffKey(string distilled, string key) =>
        distilled.Contains(key, StringComparison.OrdinalIgnoreCase);

    private static void Require(string distilled, string token, List<string> missing, string prefix)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;
        if (!distilled.Contains(token, StringComparison.Ordinal))
            missing.Add(prefix + token);
    }

    // CARD-0431: short hex runs alone are ambiguous (notably task/report IDs). Require
    // Git context within 40 characters on the same line, even for backticked citations;
    // full SHA-1s remain anchors without a label. Capture only the SHA so the
    // distillation may paraphrase the prose, and keep every member of lists and ranges.
    // Permit revision-labelled continuations (", R2 in"), not arbitrary intervening prose.
    // Word/hyphen boundaries prevent extracting a SHA from an identifier or UUID.
    private static readonly Regex ShaPattern = new(
        @"(?:\b(?:sha(?:-?1)?|commits?|revision|head|landed|pushed|merged|committed
                     |shipped|reverted|rebased|cherry-picked|tagged)\b
             |\bgit[ \t]+(?:show|revert|cherry-pick|checkout|diff)\b
             |\bfixed[ \t]+in\b
             |\borigin/[\w./-]+[ \t]+is[ \t]+now\b)
          [^\r\n]{0,40}?(?<![\w-])(?<sha>[0-9a-f]{7,40})(?![\w-])[`""']?
          (?:[ \t]*(?:,(?:[ \t]*and\b)?|and\b|\.{2,3})[ \t]*(?:R\d+[ \t]+in[ \t]+)?
             [`""']?(?<![\w-])(?<sha>[0-9a-f]{7,40})(?![\w-])[`""']?)*
          |(?<![\w-])(?<sha>[0-9a-f]{40})(?![\w-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
        | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex CardPattern = new(
        @"CARD-\d{4}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UrlPattern = new(
        @"https?://[^\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AttachPattern = new(
        @"\[\[attach:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AmountPattern = new(
        @"\$\d[\d,]*(?:\.\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CountPattern = new(
        @"\b\d+\s*(passed|failed|skipped|tests?|files?|warnings?|errors?)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // CARD-0431: require a path signal, not just a segment count: an explicit root,
    // extension, known repository directory plus two segments, trailing slash or line.
    // Limit client to its source/public subtrees so client/server/db stays ordinary prose.
    // Other extension-less directories can be made explicit with ./ or a trailing slash.
    // Boundaries prevent suffix matches inside slash phrases and URLs.
    private static readonly Regex PathPattern = new(
        @"(?<![\w./\\:])(?:
            (?:[A-Za-z]:[\\/]|~[\\/]|\.{1,2}[\\/]|[\\/])[^\s`""'<>|]+
            |[\w.-]+(?:[\\/][\w.-]+)*[\\/][\w.-]+\.[A-Za-z0-9]+(?::\d+)?
            |(?i:(?:server|src|docs|tests|scripts|Application|Domain|Infrastructure|Api)[\\/][\w.-]+
                  |client[\\/](?:src|public))(?:[\\/][\w.-]+)+[\\/]?(?::\d+)?
            |[\w.-]+(?:[\\/][\w.-]+)+(?:[\\/]|:\d+)
          )(?![\w/\\])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace);
}
