using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// What the diagnose seat IS, as code (CARD-0352 S2).
///
/// <para>The specialist's standing instructions live in <see cref="Agent.SystemPromptAppend"/>,
/// which <c>AgentControlService</c> renders into <c>--append-system-prompt</c> on EVERY launch —
/// fresh and resume alike — so the contract survives compaction and re-arms on every restart. That
/// makes the agent row a PROJECTION of the constant below, not the source of truth:
/// <c>DiagnoseProvisioner.EnsureAsync</c> reconciles the row against it on every call, so editing
/// the bundle in a PR updates the live agent, and a hand-edit in the UI is overwritten.</para>
///
/// <para>The instructions are not the enforcement. The specialist needs zero tools, so it gets zero:
/// the provisioner also writes a deny-all <c>PreToolUse</c> hook into its scratch working directory
/// (<see cref="DenyAllToolsSettingsJson"/>). Prose asks; the hook refuses.</para>
///
/// <para>Parsers here are the gates in D6: code decides what a diagnosis is allowed to change.
/// A title the model likes that fails a gate is not applied. Jobs 1 and 2 (S3/S4) call these;
/// this file does not enqueue, write, or spend.</para>
/// </summary>
public static class Diagnosis
{
    /// <summary>
    /// Bumped whenever the contract changes meaningfully. It rides IN the contract text so an
    /// operator reading the agent row can see which version that agent is running without diffing
    /// prose, and so a stale row is obvious at a glance. Held together with the literal
    /// <c>contract v1</c> in <c>server/Bundles/diagnose.md</c> by <c>DiagnoseProvisionerTests</c>
    /// and <c>InstructionBundleTests</c>.
    /// </summary>
    public const string ContractVersion = "1";

    /// <summary>
    /// The standing contract. A FORWARD to bundle <c>diagnose</c>: the text lives in
    /// <c>server/Bundles/diagnose.md</c> and is composed like any other bundle, while every call
    /// site here keeps reading one constant. The forward is the bundle's TEXT and not its rendered
    /// form — no <c>[bundle:…]</c> header — so the reconciled agent row is the contract alone.
    /// </summary>
    public static string Contract => InstructionBundles.TextOf(InstructionBundles.Diagnose);

    /// <summary>The one-line TITLE reminder that rides every title brief, so the shape survives compaction.</summary>
    public const string TitleFormatReminder =
        "Answer with a title of 2 to 8 words, at most 80 characters, that says what the task will "
        + "do or find. Not a sentence, no full stop, no quotes. Close with the Diagnose task's "
        + "`done` token after the one line; never `blocked`.";

    /// <summary>The one-line LABELS reminder that rides every labels brief.</summary>
    public const string LabelsFormatReminder =
        "Answer exactly: complexity=hard|medium|easy ui=yes|no. If the card is a question with no "
        + "work described, or the description is empty, answer exactly: unclear. Close with the "
        + "Diagnose task's `done` token after the one line; never `blocked`.";

    /// <summary>
    /// Stderr the deny-all hook feeds back when a tool is refused. The JSON wrapper lives on
    /// <see cref="SpecialistSpec"/> so Check / Distill / Diagnose seats share the same shape.
    /// </summary>
    public const string DenyHookStderr =
        "This session is the Antiphon diagnose agent: it titles a task or labels a card. It has no tools. Answer from the request alone.";

    /// <summary>
    /// A deny-all <c>PreToolUse</c> hook — the hard half of "use no tools". Same mechanism as
    /// the check interpreter: matcher <c>*</c>, exit 2 so Claude Code feeds the stderr line back.
    /// </summary>
    public static string DenyAllToolsSettingsJson =>
        SpecialistSpec.BuildDenyAllToolsSettingsJson(DenyHookStderr);

    /// <summary>Where the hook file goes, relative to the specialist's working directory.</summary>
    public const string DenyHookRelativePath = SpecialistSpec.DenyHookRelativePath;

    public const int DefaultMaxInputChars = 12_000;
    public const int TitleMaxChars = 80;
    public const int TitleMaxCharsAfterCardPrefix = 100;
    public const int TitleMinWords = 2;
    public const int TitleMaxWords = 10;

    /// <summary>The diagnose task's title — names the target so the link survives on the board.</summary>
    public static string BuildTitleTaskTitle(AgentTask task) =>
        $"title for task {DelegationReportFormatter.Short(task.Id)}";

    /// <summary>The diagnose task's title for a card-label request.</summary>
    public static string BuildLabelsTaskTitle(Card card) =>
        $"labels for {card.Identifier}";

    /// <summary>
    /// The per-request TITLE brief: the goal, and the reminder of what to do with it. Task markers
    /// are scrubbed HERE because a delegate's Goal often opens with its own
    /// <c>[antiphon-task:xxxxxxxx]</c>, and a live-looking marker riding into the specialist's
    /// session would correlate its turn to somebody else's task.
    /// </summary>
    public static string BuildTitleGoal(AgentTask task, string? cardIdentifier = null)
    {
        var bound = string.IsNullOrWhiteSpace(cardIdentifier)
            ? ""
            : $" bound to {cardIdentifier.Trim()}";
        return $"""
            TITLE for task {DelegationReportFormatter.Short(task.Id)}{bound}.

            {Scrub(task.Goal)}

            {TitleFormatReminder}
            """.ReplaceLineEndings("\n");
    }

    /// <summary>
    /// The per-request LABELS brief: identifier, title, status, clamped description, format reminder.
    /// </summary>
    public static string BuildLabelsGoal(Card card, int maxInputChars = DefaultMaxInputChars)
    {
        var title = card.Title.Replace("\"", "'");
        return $"""
            LABELS for {card.Identifier} "{title}" ({card.Status})

            {ClampInput(Scrub(card.Description), maxInputChars)}

            {LabelsFormatReminder}
            """.ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Head + tail of <paramref name="text"/> fitting in <paramref name="maxChars"/>, with
    /// <c>[… n chars elided …]</c> naming how many characters were dropped. Under the budget the
    /// input is returned unchanged.
    /// </summary>
    public static string ClampInput(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";
        if (maxChars < 0)
            maxChars = 0;
        if (text.Length <= maxChars)
            return text;

        static string Marker(int n) => $"[… {n} chars elided …]";

        var elidedGuess = Math.Max(1, text.Length);
        var marker = Marker(elidedGuess);
        if (maxChars <= marker.Length)
            return text[..maxChars];

        var budget = maxChars - marker.Length;
        var head = budget / 2;
        var tail = budget - head;
        var elided = text.Length - head - tail;
        marker = Marker(elided);

        while (head + marker.Length + tail > maxChars && head + tail > 0)
        {
            if (head >= tail && head > 0) head--;
            else if (tail > 0) tail--;
            else break;
            elided = text.Length - head - tail;
            marker = Marker(elided);
        }

        if (head + tail == 0)
            return text[..maxChars];

        return string.Concat(text.AsSpan(0, head), marker, text.AsSpan(text.Length - tail));
    }

    /// <summary>
    /// Accept a TITLE answer: one line, 2–10 words, at most 80 characters (100 after an added
    /// CARD prefix). Rejects empty, multi-line, too long, too few/many words, markers, the
    /// fallback itself, and answers that start with TITLE / Title:.
    /// </summary>
    public static bool TryParseTitle(
        string? answer,
        string fallback,
        string? cardIdentifier,
        [NotNullWhen(true)] out string? title,
        [NotNullWhen(false)] out string? reason)
    {
        title = null;
        reason = null;

        if (string.IsNullOrWhiteSpace(answer))
        {
            reason = "empty";
            return false;
        }

        var raw = answer.ReplaceLineEndings("\n").Trim();
        var lineCount = CountPhysicalLines(raw);
        if (lineCount > 1)
        {
            reason = $"{lineCount} lines";
            return false;
        }

        var cleaned = StripTrailingStop(StripOneSurroundingPair(raw).Trim()).Trim();
        if (cleaned.Length == 0)
        {
            reason = "empty";
            return false;
        }

        if (cleaned.Contains('\n') || cleaned.Contains('\r'))
        {
            reason = $"{CountPhysicalLines(cleaned)} lines";
            return false;
        }

        if (ContainsMarker(cleaned))
        {
            reason = "contains marker";
            return false;
        }

        if (StartsWithTitleKeyword(cleaned))
        {
            reason = "starts with TITLE";
            return false;
        }

        if (string.Equals(cleaned, fallback.Trim(), StringComparison.Ordinal))
        {
            reason = "equals fallback";
            return false;
        }

        if (cleaned.Length > TitleMaxChars)
        {
            reason = $"{cleaned.Length} chars";
            return false;
        }

        var words = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < TitleMinWords || words.Length > TitleMaxWords)
        {
            reason = $"{words.Length} words";
            return false;
        }

        var result = cleaned;
        if (!string.IsNullOrWhiteSpace(cardIdentifier)
            && result.IndexOf(cardIdentifier.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
        {
            result = cardIdentifier.Trim() + " " + result;
        }

        if (result.Length > TitleMaxCharsAfterCardPrefix)
        {
            reason = $"{result.Length} chars";
            return false;
        }

        title = result;
        return true;
    }

    /// <summary>
    /// Accept a LABELS answer: exactly <c>complexity=hard|medium|easy ui=yes|no</c>
    /// (case-insensitive, whitespace-tolerant, one line), or <c>unclear</c>.
    /// </summary>
    public static LabelsParseResult TryParseLabels(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return LabelsParseResult.Rejected("empty");

        var raw = answer.ReplaceLineEndings("\n").Trim();
        var lineCount = CountPhysicalLines(raw);
        if (lineCount > 1)
            return LabelsParseResult.Rejected($"{lineCount} lines");

        if (string.Equals(raw, "unclear", StringComparison.OrdinalIgnoreCase))
            return LabelsParseResult.UnclearAnswer();

        var match = LabelsPattern.Match(raw);
        if (!match.Success)
            return LabelsParseResult.Rejected("unparseable");

        var complexity = match.Groups[1].Value.ToLowerInvariant() switch
        {
            "hard" => TaskComplexity.Hard,
            "medium" => TaskComplexity.Medium,
            _ => TaskComplexity.Easy,
        };
        var ui = match.Groups[2].Value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        return LabelsParseResult.Accepted(complexity, ui);
    }

    internal static string Scrub(string? body)
    {
        var text = AgentTaskCheckService.ScrubTaskMarkers(body ?? "");
        return ReportMarkerPattern.Replace(text, "[report-marker removed]");
    }

    private static readonly Regex ReportMarkerPattern = new(
        @"\[antiphon-report:[^\]]+\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LabelsPattern = new(
        @"^complexity\s*=\s*(hard|medium|easy)\s+ui\s*=\s*(yes|no)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static int CountPhysicalLines(string text)
    {
        if (text.Length == 0)
            return 0;
        var count = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
                count++;
        }

        return count;
    }

    private static string StripOneSurroundingPair(string value)
    {
        if (value.Length < 2)
            return value;
        var a = value[0];
        var b = value[^1];
        if (a == b && a is '"' or '\'' or '`')
            return value[1..^1];
        return value;
    }

    private static string StripTrailingStop(string value) =>
        value.Length > 0 && value[^1] == '.' ? value[..^1] : value;

    private static bool ContainsMarker(string value) =>
        value.Contains("antiphon-task", StringComparison.OrdinalIgnoreCase)
        || value.Contains("antiphon-report", StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithTitleKeyword(string value)
    {
        if (!value.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
            return false;
        return value.Length == 5
            || char.IsWhiteSpace(value[5])
            || value[5] == ':';
    }
}

/// <summary>Outcome of <see cref="Diagnosis.TryParseLabels"/>.</summary>
public readonly record struct LabelsParseResult(
    bool Ok,
    bool Unclear,
    TaskComplexity? Complexity,
    bool? Ui,
    string? Reason)
{
    public static LabelsParseResult Accepted(TaskComplexity complexity, bool ui) =>
        new(true, false, complexity, ui, null);

    public static LabelsParseResult UnclearAnswer() =>
        new(false, true, null, null, "unclear");

    public static LabelsParseResult Rejected(string reason) =>
        new(false, false, null, null, reason);
}
