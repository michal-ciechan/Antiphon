using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Fail-closed identity of one Dispatched task's current brief. Only a positive
/// outcome below inserts or reuses; everything else keeps the existing row.
/// </summary>
internal enum DispatchBriefKind
{
    Absent,
    Reuse,
    Received,
    AttemptOwned,
    Uncertain,
    Unavailable,
    Superseded,
}

internal sealed record DispatchBriefDecision(DispatchBriefKind Kind, bool Hold, string? Reason);

internal sealed record DispatchBriefEnsureRequest(
    Guid TaskId,
    int Attempt,
    Guid SessionId,
    DateTime DispatchedAt,
    DateTime SessionStartedAt,
    bool Refocus = false);

internal sealed record DispatchBriefTaskSnapshot(
    AgentTaskStatus Status,
    int Attempt,
    Guid? AgentSessionId,
    DateTime? DispatchedAt,
    DateTime? SessionStartedAt,
    string? Goal);

internal sealed record DispatchBriefRowEvidence(
    Guid Id,
    Guid AgentSessionId,
    QueuedMessageOrigin Origin,
    QueuedMessageStatus Status,
    DateTime CreatedAt,
    Guid? ExecutionTaskId,
    Guid? SourceTaskId,
    Guid? SourceLandNotificationId,
    string? ConversationKey,
    string? ContentDigest,
    string Body,
    string? RemoteSpillBody,
    string? RemoteSpillRelativePath,
    Guid? RulesCoveredByMessageId,
    int DeliveryAttempts,
    DeliveryVerdict? DeliveryVerdict,
    DateTime? LastDeliveryStartedAt,
    DateTime? SentAt,
    DateTime? CanceledAt,
    DateTime? LastDeliveryGeneration);

internal sealed record DispatchBriefPromptEvidence(
    Guid AgentSessionId,
    string Kind,
    string? Text,
    DateTime? Timestamp);

/// <summary>
/// What the ensure observed about one task's current brief, each a positive observation. Row
/// facts are false unless exactly one current row is recognized as the brief.
/// </summary>
internal readonly record struct DispatchBriefFacts(
    bool TaskOpen,
    bool CurrentAttempt,
    int RecognizedRows,
    bool OtherCurrentEvidence,
    bool CompleteReceipt,
    bool SpillPointer,
    bool PayloadIntact,
    bool Canceled,
    bool Unattempted,
    bool Attempted);

internal sealed record DispatchBriefRule(string Name, Func<DispatchBriefFacts, bool> When, DispatchBriefDecision Then);

internal static class DispatchBriefEvidence
{
    public const string InputUnavailableReason =
        "dispatch_brief_input_unavailable: retained brief spill is missing or conflicts; the original row was not replaced.";

    public const string EvidenceUncertainReason =
        "dispatch_brief_evidence_uncertain: current brief evidence is not a complete unattempted row or a complete receipt.";

    public const string AttemptOwnedReason =
        "dispatch_brief_attempt_owned: the current brief was already attempted; queue recovery keeps that row.";

    private static readonly DispatchBriefDecision UncertainHold =
        new(DispatchBriefKind.Uncertain, Hold: true, EvidenceUncertainReason);

    /// <summary>
    /// CARD-1150. The whole verdict, in evidence order: the task and its attempt, then which rows
    /// are the brief, then a complete receipt, then the retained payload, then the row's delivery
    /// shape. The first rule that matches decides; the last holds whatever no rule admitted.
    /// A receipt comes before the payload because the queue releases a spill's bytes after a
    /// complete UserPrompt (F5), and only a queue-written spill pointer has a payload to lose (F4).
    /// </summary>
    internal static readonly IReadOnlyList<DispatchBriefRule> Table =
    [
        new("task-not-open", f => !f.TaskOpen, new(DispatchBriefKind.Uncertain, Hold: false, Reason: null)),
        new("superseded", f => !f.CurrentAttempt, new(DispatchBriefKind.Superseded, Hold: false, Reason: null)),
        new("several-briefs", f => f.RecognizedRows > 1, UncertainHold),
        new("unrecognized-evidence", f => f.RecognizedRows == 0 && f.OtherCurrentEvidence, UncertainHold),
        new("absent", f => f.RecognizedRows == 0, new(DispatchBriefKind.Absent, Hold: false, Reason: null)),
        new("received", f => f.CompleteReceipt, new(DispatchBriefKind.Received, Hold: false, Reason: null)),
        new("spill-unavailable", f => f.SpillPointer && !f.PayloadIntact,
            new(DispatchBriefKind.Unavailable, Hold: true, InputUnavailableReason)),
        new("canceled", f => f.Canceled, UncertainHold),
        new("unattempted", f => f.Unattempted, new(DispatchBriefKind.Reuse, Hold: false, Reason: null)),
        new("attempted", f => f.Attempted, new(DispatchBriefKind.AttemptOwned, Hold: false, AttemptOwnedReason)),
        new("unknown", _ => true, UncertainHold),
    ];

    public static DispatchBriefDecision Decide(DispatchBriefFacts facts) =>
        Table.First(rule => rule.When(facts)).Then;

    public static DispatchBriefDecision Classify(
        DispatchBriefEnsureRequest request,
        DispatchBriefTaskSnapshot task,
        IReadOnlyList<DispatchBriefRowEvidence> rows,
        IReadOnlyList<DispatchBriefPromptEvidence> prompts,
        Func<string, string?>? readAbsoluteFile = null) =>
        Decide(Observe(request, task, rows, prompts, readAbsoluteFile));

    internal static DispatchBriefFacts Observe(
        DispatchBriefEnsureRequest request,
        DispatchBriefTaskSnapshot task,
        IReadOnlyList<DispatchBriefRowEvidence> rows,
        IReadOnlyList<DispatchBriefPromptEvidence> prompts,
        Func<string, string?>? readAbsoluteFile = null)
    {
        var open = task.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working;
        var currentAttempt = task.Attempt == request.Attempt
            && task.AgentSessionId == request.SessionId
            && task.DispatchedAt is DateTime dispatchedAt
            && task.SessionStartedAt is DateTime startedAt
            && SameGeneration(dispatchedAt, request.DispatchedAt)
            && SameGeneration(startedAt, request.SessionStartedAt);
        if (!open || !currentAttempt)
            return new DispatchBriefFacts(open, currentAttempt, 0, false, false, false, false, false, false, false);

        var marker = DelegationReportFormatter.TaskMarker(request.TaskId);
        var current = new List<DispatchBriefRowEvidence>();
        foreach (var row in rows)
        {
            if (row.AgentSessionId == request.SessionId && InWindow(row.CreatedAt, request.DispatchedAt))
                current.Add(row);
        }

        var recognized = new List<DispatchBriefRowEvidence>();
        foreach (var row in current)
        {
            if (IsRecognized(row, request.TaskId, marker))
                recognized.Add(row);
        }

        var other = current.Count > recognized.Count || HasCurrentMarkerPrompt(prompts, request, marker);
        if (recognized.Count != 1)
            return new DispatchBriefFacts(true, true, recognized.Count, other, false, false, false, false, false, false);

        var brief = recognized[0];
        var payload = ReadPayload(brief, marker, readAbsoluteFile);
        return new DispatchBriefFacts(
            TaskOpen: true,
            CurrentAttempt: true,
            RecognizedRows: 1,
            OtherCurrentEvidence: other,
            CompleteReceipt: HasCompleteReceipt(brief, payload.Text, marker, prompts, request),
            SpillPointer: payload.Spill,
            PayloadIntact: payload.IsIntact(marker, task.Goal),
            Canceled: brief.Status == QueuedMessageStatus.Canceled || brief.CanceledAt is not null,
            Unattempted: IsUnattempted(brief),
            Attempted: IsAttempted(brief));
    }

    private static bool IsRecognized(DispatchBriefRowEvidence row, Guid taskId, string marker)
    {
        if (row.Origin != QueuedMessageOrigin.Delegation || row.SourceLandNotificationId is not null)
            return false;
        if (row.ConversationKey is not null
            && !row.ConversationKey.StartsWith("released-seat-answer:", StringComparison.Ordinal))
            return false;
        if (!ContainsMarker(row, marker))
            return false;
        if (row.ExecutionTaskId == taskId)
            return true;
        return row.SourceTaskId == taskId && row.ExecutionTaskId is null && row.ConversationKey is null;
    }

    private static bool ContainsMarker(DispatchBriefRowEvidence row, string marker) =>
        row.Body.Contains(marker, StringComparison.Ordinal)
        || (row.RemoteSpillBody?.Contains(marker, StringComparison.Ordinal) ?? false);

    private static bool IsUnattempted(DispatchBriefRowEvidence row) =>
        row.Status == QueuedMessageStatus.Pending
        && row.DeliveryAttempts == 0
        && row.DeliveryVerdict is null
        && row.LastDeliveryStartedAt is null
        && row.SentAt is null
        && row.CanceledAt is null;

    private static bool IsAttempted(DispatchBriefRowEvidence row) =>
        row.DeliveryAttempts > 0
        || row.Status == QueuedMessageStatus.Sent
        || row.DeliveryVerdict is not null
        || row.LastDeliveryStartedAt is not null
        || row.SentAt is not null;

    /// <summary>
    /// The text the agent needs: the retained or spilled payload of a spill claim, otherwise the
    /// body itself.
    /// </summary>
    private static SpillPayload ReadPayload(
        DispatchBriefRowEvidence row, string marker, Func<string, string?>? readAbsoluteFile)
    {
        var claimsSpill = !string.IsNullOrEmpty(row.RemoteSpillRelativePath)
            || SpillTokenIndex(row.Body, 0) >= 0;
        if (!claimsSpill)
            return new SpillPayload(false, row.Body, Conflict: false);

        var file = ReadSpill(AbsoluteSpillPath(row.Body), readAbsoluteFile);
        var retained = string.IsNullOrEmpty(row.RemoteSpillBody) ? null : row.RemoteSpillBody;
        var conflict = retained is not null && file is not null && !string.Equals(file, retained, StringComparison.Ordinal);
        return new SpillPayload(true, retained ?? file, conflict);
    }

    private readonly record struct SpillPayload(bool Spill, string? Text, bool Conflict)
    {
        public bool IsIntact(string marker, string? goal)
        {
            if (Conflict || string.IsNullOrEmpty(Text) || !Text.Contains(marker, StringComparison.Ordinal))
                return false;
            var expectedGoal = goal?.Trim();
            return string.IsNullOrEmpty(expectedGoal) || Text.Contains(expectedGoal, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A complete UserPrompt of the typed body, or of the payload it carried. The queue compares
    /// the typed wire text, so a spill pointer is received when its pointer is.
    /// </summary>
    private static bool HasCompleteReceipt(
        DispatchBriefRowEvidence row,
        string? payload,
        string marker,
        IReadOnlyList<DispatchBriefPromptEvidence> prompts,
        DispatchBriefEnsureRequest request)
    {
        foreach (var prompt in prompts)
        {
            if (!PromptInWindow(prompt, request))
                continue;
            if (!string.Equals(prompt.Kind, TranscriptKinds.UserPrompt, StringComparison.Ordinal))
                continue;
            if (string.IsNullOrEmpty(prompt.Text) || !prompt.Text.Contains(marker, StringComparison.Ordinal))
                continue;
            if (CompleteIn(row.Body, prompt.Text) || CompleteIn(payload, prompt.Text))
                return true;
        }

        return false;
    }

    private static bool CompleteIn(string? body, string text) =>
        PromptSubmissionMatch.RequiresTextMatch(body) && PromptSubmissionMatch.IsCompleteIn(body, text);

    private static bool HasCurrentMarkerPrompt(
        IReadOnlyList<DispatchBriefPromptEvidence> prompts,
        DispatchBriefEnsureRequest request,
        string marker)
    {
        foreach (var prompt in prompts)
        {
            if (!PromptInWindow(prompt, request))
                continue;
            if (prompt.Text?.Contains(marker, StringComparison.Ordinal) == true)
                return true;
        }

        return false;
    }

    private static bool PromptInWindow(DispatchBriefPromptEvidence prompt, DispatchBriefEnsureRequest request) =>
        prompt.AgentSessionId == request.SessionId
        && (prompt.Timestamp is null || InWindow(prompt.Timestamp.Value, request.DispatchedAt));

    /// <summary>
    /// The local producer writes its spill with <see cref="Path.Combine(string, string, string)"/>,
    /// so a Windows pointer names <c>.antiphon\</c>. Both separators are a spill claim. A rooted
    /// path of either platform is returned verbatim; one this host cannot read stays unproven.
    /// </summary>
    internal static string? AbsoluteSpillPath(string body)
    {
        var index = 0;
        while ((index = SpillTokenIndex(body, index)) >= 0)
        {
            var tokenEnd = index + SpillToken.Length + 1;
            if (QuotedPathAround(body, index, tokenEnd) is { } quoted && IsRootedOnAnyPlatform(quoted))
                return quoted;
            var start = index;
            while (start > 0 && !char.IsWhiteSpace(body[start - 1]))
                start--;
            var end = tokenEnd;
            while (end < body.Length && !char.IsWhiteSpace(body[end]))
                end++;
            var path = body[start..end].Trim('`', '"', '\'', ',', '.', ')', '(');
            if (IsRootedOnAnyPlatform(path))
                return path;
            index = end;
        }

        return null;
    }

    private const string SpillToken = ".antiphon";

    private static int SpillTokenIndex(string body, int from)
    {
        var index = from;
        while ((index = body.IndexOf(SpillToken, index, StringComparison.Ordinal)) >= 0)
        {
            var next = index + SpillToken.Length;
            if (next < body.Length && body[next] is '/' or '\\')
                return index;
            index = next;
        }

        return -1;
    }

    private static string? QuotedPathAround(string body, int tokenStart, int tokenEnd)
    {
        var open = tokenStart - 1;
        while (open >= 0 && body[open] is not ('\'' or '"' or '`' or '\n' or '\r'))
            open--;
        if (open < 0 || body[open] is '\n' or '\r')
            return null;
        var close = body.IndexOf(body[open], tokenEnd);
        var line = body.IndexOf('\n', tokenEnd);
        if (close < 0 || (line >= 0 && line < close))
            return null;
        return body[(open + 1)..close];
    }

    private static bool IsRootedOnAnyPlatform(string path) =>
        Path.IsPathRooted(path)
        || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/')
        || path.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>A reader that throws proves nothing: the payload is unavailable, never present.</summary>
    private static string? ReadSpill(string? path, Func<string, string?>? readAbsoluteFile)
    {
        if (path is null || readAbsoluteFile is null)
            return null;
        try
        {
            return readAbsoluteFile(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static bool InWindow(DateTime created, DateTime dispatched) =>
        SameGeneration(created, dispatched) || AsUtc(created) >= AsUtc(dispatched);

    /// <summary>
    /// PostgreSQL timestamptz rounds to microseconds. A sub-microsecond gap is the
    /// same generation; <see cref="SessionGeneration.Next"/> moves by a whole microsecond.
    /// </summary>
    private static bool SameGeneration(DateTime left, DateTime right)
    {
        var delta = AsUtc(left).Ticks - AsUtc(right).Ticks;
        if (delta < 0)
            delta = -delta;
        if (delta < SessionGeneration.MicrosecondTicks)
            return true;
        return SessionGeneration.Normalize(AsUtc(left)) == SessionGeneration.Normalize(AsUtc(right));
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
