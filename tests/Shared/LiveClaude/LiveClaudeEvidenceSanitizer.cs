using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Antiphon.TestSupport.LiveClaude;

/// <summary>Synthetic capture sentinels. Projection never treats these as a strip list.</summary>
public static class LiveClaudeEvidenceSentinels
{
    public const string Link = "https://claude.ai/code/session/SENTINEL-LINK-3a7c132f";
    public const string Screen = "SENTINEL-SCREEN-3a7c132f";
    public const string Secret = "SENTINEL-TOKEN-3a7c132f";
    public const string Device = "SENTINEL-DEVICE-3a7c132f";
}

/// <summary>One raw capture. Screen, link, secret and device text never leave <see cref="LiveClaudeEvidenceSanitizer.Project"/>.</summary>
public sealed record EvidenceCapture(
    string CaseId,
    string TestId,
    string Kind,
    string? RunId,
    string? Link,
    string? Screen,
    string? Secret,
    string? Device,
    IReadOnlyDictionary<string, string>? Fields);

public readonly record struct SanitizedEvidence(string Marker, string Text);

/// <summary>The only projection used by live-lane sinks.</summary>
public static class LiveClaudeEvidenceSanitizer
{
    public static SanitizedEvidence Project(EvidenceCapture capture)
    {
        if (capture is null) throw new InvalidOperationException("C1545 evidence/capture");
        if (!IsToken(capture.CaseId) || !IsToken(capture.TestId) || !IsToken(capture.Kind))
            throw new InvalidOperationException("C1545 evidence/identity");
        var run = IsToken(capture.RunId) ? capture.RunId! : "rejected";
        var marker = "C1545-EVIDENCE case=" + capture.CaseId + " test=" + capture.TestId
            + " kind=" + capture.Kind + " run=" + run;
        var text = new StringBuilder(marker);
        text.Append('\n').Append("link=").Append(Classify(capture.Link));
        text.Append('\n').Append("screen=").Append(Classify(capture.Screen));
        text.Append('\n').Append("secret=").Append(Classify(capture.Secret));
        text.Append('\n').Append("device=").Append(Classify(capture.Device));
        if (capture.Fields is not null)
        {
            foreach (var pair in capture.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!IsToken(pair.Key) || !IsToken(pair.Value)) continue;
                text.Append('\n').Append(pair.Key).Append('=').Append(pair.Value);
            }
        }
        return new SanitizedEvidence(marker, text.ToString());
    }

    public static bool IsToken(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 80) return false;
        foreach (var character in value)
        {
            if (character is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or ':' or '-'))
                return false;
        }
        return true;
    }

    public static bool ContainsSentinel(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Contains(LiveClaudeEvidenceSentinels.Link, StringComparison.Ordinal)
            || text.Contains(LiveClaudeEvidenceSentinels.Screen, StringComparison.Ordinal)
            || text.Contains(LiveClaudeEvidenceSentinels.Secret, StringComparison.Ordinal)
            || text.Contains(LiveClaudeEvidenceSentinels.Device, StringComparison.Ordinal);
    }

    private static string Classify(string? value) => string.IsNullOrEmpty(value) ? "absent" : "present";
}

public static class LiveClaudeEvidence
{
    public static string Success(string caseId, string testId, string? link, string? screen, string? secret, string? device,
        IReadOnlyDictionary<string, string>? fields = null)
        => Emit(caseId, testId, "success", null, link, screen, secret, device, fields);

    public static string Assertion(string caseId, string testId, string? link, string? screen, string? secret, string? device)
        => Emit(caseId, testId, "assertion", null, link, screen, secret, device, null);

    public static string Skip(string caseId, string testId, string? link, string? screen, string? secret, string? device)
        => Emit(caseId, testId, "skip", null, link, screen, secret, device, null);

    public static string Projected(string caseId, string testId, string kind, string? runId, string? link, string? screen,
        string? secret, string? device)
    {
        var safe = LiveClaudeEvidenceSanitizer.Project(new EvidenceCapture(
            caseId, testId, kind, runId, link, screen, secret, device, null));
        Console.WriteLine(safe.Text);
        return safe.Text;
    }

    public static void WriteDiagnostic(string path, string caseId, string? raw)
    {
        var safe = LiveClaudeEvidenceSanitizer.Project(new EvidenceCapture(
            caseId, "C680_LiveNudgeRefusesPermissionAndQuestionDialogs", "diagnostic", null,
            null, raw, null, null, null));
        File.WriteAllText(path, safe.Text);
    }

    public static void AppendDiagnostic(string path, string caseId, string? raw)
    {
        var safe = LiveClaudeEvidenceSanitizer.Project(new EvidenceCapture(
            caseId, "C680_LiveNudgeRefusesPermissionAndQuestionDialogs", "diagnostic", null,
            null, raw, null, null, null));
        File.AppendAllText(path, safe.Text + "\n");
    }

    public static string SerializeReceipt(IReadOnlyList<SanitizedEvidence> events, string? runId)
        => Serialize("receipt", events, runId);

    public static string SerializeSummary(IReadOnlyList<SanitizedEvidence> events, string? runId)
        => Serialize("summary", events, runId);

    public static bool MarkerLine(string? line)
    {
        if (string.IsNullOrEmpty(line) || line.Contains('\n') || line.Contains('\r')) return false;
        var parts = line.Split(' ');
        if (parts.Length != 5 || parts[0] != "C1545-EVIDENCE") return false;
        return parts[1].StartsWith("case=", StringComparison.Ordinal) && LiveClaudeEvidenceSanitizer.IsToken(parts[1][5..])
            && parts[2].StartsWith("test=", StringComparison.Ordinal) && LiveClaudeEvidenceSanitizer.IsToken(parts[2][5..])
            && parts[3].StartsWith("kind=", StringComparison.Ordinal) && LiveClaudeEvidenceSanitizer.IsToken(parts[3][5..])
            && parts[4].StartsWith("run=", StringComparison.Ordinal) && LiveClaudeEvidenceSanitizer.IsToken(parts[4][4..]);
    }

    private static string Emit(string caseId, string testId, string kind, string? runId, string? link, string? screen,
        string? secret, string? device, IReadOnlyDictionary<string, string>? fields)
    {
        var safe = LiveClaudeEvidenceSanitizer.Project(new EvidenceCapture(
            caseId, testId, kind, runId, link, screen, secret, device, fields));
        Console.WriteLine(safe.Text);
        return safe.Text;
    }

    private static string Serialize(string kind, IReadOnlyList<SanitizedEvidence> events, string? runId)
    {
        var run = LiveClaudeEvidenceSanitizer.IsToken(runId) ? runId! : "rejected";
        var lines = new List<string>
        {
            "C1545-EVIDENCE-DOCUMENT kind=" + kind + " run=" + run + " count=" + events.Count.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var evidence in events)
        {
            if (!MarkerLine(evidence.Marker)) continue;
            lines.Add(evidence.Marker);
        }
        return string.Join('\n', lines) + "\n";
    }
}
