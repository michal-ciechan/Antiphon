using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0146 S2. Parses the <c>--- next stage ---</c> block that a stage-role report places
/// immediately above the closing <c>[antiphon-report:…]</c> token. Enrichment, never a
/// settlement gate: a missing block still settles (<c>next=unmarked</c> on stage roles).
/// </summary>
public static class PipelineHandoff
{
    public const int MaxHandoffChars = 400;

    public const string Heading = "--- next stage ---";

    /// <summary>
    /// Same pattern <see cref="AgentTaskReplyService"/> uses to find a markdown deliverable.
    /// An <c>artifact:</c> value must match this in full (optional surrounding backticks).
    /// </summary>
    public static readonly Regex DeliverablePathPattern = new(
        "`?(?<path>docs/[\\w./-]+\\.md)`?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, PipelineHandoffKind> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["investigate"] = PipelineHandoffKind.Investigate,
            ["plan"] = PipelineHandoffKind.Plan,
            ["design"] = PipelineHandoffKind.Plan,
            ["test-design"] = PipelineHandoffKind.TestDesign,
            ["testdesign"] = PipelineHandoffKind.TestDesign,
            ["test design"] = PipelineHandoffKind.TestDesign,
            ["code"] = PipelineHandoffKind.Code,
            ["build"] = PipelineHandoffKind.Code,
            ["execute"] = PipelineHandoffKind.Code,
            ["review"] = PipelineHandoffKind.Review,
            ["verify"] = PipelineHandoffKind.Review,
            ["land"] = PipelineHandoffKind.Land,
            ["merge"] = PipelineHandoffKind.Land,
            ["cleanup"] = PipelineHandoffKind.Land,
            ["decide"] = PipelineHandoffKind.Decide,
            ["none"] = PipelineHandoffKind.None,
        };

    public readonly record struct Result(
        bool Found,
        PipelineHandoffKind? Kind,
        string? Handoff,
        string? ArtifactPath,
        string? RawToken);

    /// <summary>
    /// Canonical header token for a parsed kind (<c>test-design</c>, not <c>TestDesign</c>).
    /// </summary>
    public static string Token(PipelineHandoffKind kind) => kind switch
    {
        PipelineHandoffKind.Investigate => "investigate",
        PipelineHandoffKind.Plan => "plan",
        PipelineHandoffKind.TestDesign => "test-design",
        PipelineHandoffKind.Code => "code",
        PipelineHandoffKind.Review => "review",
        PipelineHandoffKind.Land => "land",
        PipelineHandoffKind.Decide => "decide",
        PipelineHandoffKind.None => "none",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Maps a <c>next:</c> kind onto the pipeline stage it is ready for.
    /// <see cref="PipelineHandoffKind.Land"/>, <see cref="PipelineHandoffKind.Decide"/> and
    /// <see cref="PipelineHandoffKind.None"/> produce no ready row.
    /// </summary>
    public static bool TryToStageRole(PipelineHandoffKind kind, out AgentTaskRole role)
    {
        switch (kind)
        {
            case PipelineHandoffKind.Investigate:
                role = AgentTaskRole.Investigate;
                return true;
            case PipelineHandoffKind.Plan:
                role = AgentTaskRole.Plan;
                return true;
            case PipelineHandoffKind.TestDesign:
                role = AgentTaskRole.TestDesign;
                return true;
            case PipelineHandoffKind.Code:
                role = AgentTaskRole.Code;
                return true;
            case PipelineHandoffKind.Review:
                role = AgentTaskRole.Review;
                return true;
            default:
                role = default;
                return false;
        }
    }

    /// <summary>
    /// The <c>next=</c> header value, or null when the bit must not appear (a non-stage role
    /// with no block). Stage role, no block → <c>unmarked</c>; unparseable token →
    /// <c>unrecognised:</c> plus the first 24 characters of the raw token.
    /// </summary>
    public static string? HeaderBit(AgentTaskRole role, Result parsed)
    {
        if (parsed.Kind is { } kind)
            return Token(kind);
        if (parsed.Found && !string.IsNullOrWhiteSpace(parsed.RawToken))
        {
            var raw = parsed.RawToken.Trim();
            return "unrecognised:" + (raw.Length <= 24 ? raw : raw[..24]);
        }

        return AgentTaskRoles.IsStage(role) ? "unmarked" : null;
    }

    /// <summary>
    /// Finds the last <c>--- next stage ---</c> block before the closing report token.
    /// Anything after the token is ignored. Missing block → <see cref="Result.Found"/> false.
    /// </summary>
    public static Result TryParse(string? report)
    {
        if (string.IsNullOrWhiteSpace(report))
            return default;

        var normalized = report.ReplaceLineEndings("\n");
        var searchable = TextBeforeClosingReportToken(normalized);
        var headingAt = LastWholeLineIndex(searchable, Heading);
        if (headingAt < 0)
            return default;

        var afterHeading = searchable[(headingAt + Heading.Length)..].TrimStart('\n');
        string? next = null;
        string? handoff = null;
        string? artifact = null;
        foreach (var rawLine in afterHeading.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
                continue;
            var trimmed = line.Trim();
            if (trimmed.StartsWith("---", StringComparison.Ordinal))
                break;

            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            if (key.Equals("next", StringComparison.OrdinalIgnoreCase))
                next = value;
            else if (key.Equals("handoff", StringComparison.OrdinalIgnoreCase))
                handoff = value;
            else if (key.Equals("artifact", StringComparison.OrdinalIgnoreCase))
                artifact = value;
        }

        PipelineHandoffKind? kind = null;
        if (!string.IsNullOrWhiteSpace(next) && Aliases.TryGetValue(next.Trim(), out var mapped))
            kind = mapped;

        if (handoff is { Length: > MaxHandoffChars })
            handoff = handoff[..MaxHandoffChars];

        string? artifactPath = null;
        if (!string.IsNullOrWhiteSpace(artifact))
        {
            var match = DeliverablePathPattern.Match(artifact.Trim());
            if (match.Success && match.Length == artifact.Trim().Length)
                artifactPath = match.Groups["path"].Value;
        }

        return new Result(true, kind, string.IsNullOrWhiteSpace(handoff) ? null : handoff, artifactPath, next);
    }

    private static string TextBeforeClosingReportToken(string normalized)
    {
        var lines = normalized.Split('\n');
        var tokenLine = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("[antiphon-report:", StringComparison.OrdinalIgnoreCase)
                && line.EndsWith(']'))
            {
                tokenLine = i;
            }
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
