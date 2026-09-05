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
            Require(distilled, match.Value, missing, "sha:");
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

    // Hex runs of 7–40 chars bounded by non-hex — commit shas, not CARD digits or amounts.
    private static readonly Regex ShaPattern = new(
        @"(?<![0-9a-fA-F])[0-9a-fA-F]{7,40}(?![0-9a-fA-F])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    // Drive, rooted, ./relative, or segment/segment. Lookbehind so /Application inside
    // server/Application is not a separate path.
    private static readonly Regex PathPattern = new(
        @"(?<=^|\s)(?:[A-Za-z]:[\\/][^\s]+|(?:~|\.)?/[^\s]+|[\w.-]+(?:/[\w.-]+)+(?::\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
