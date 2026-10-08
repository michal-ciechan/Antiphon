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
    /// The text the agent needs. A spill pointer is either the queue's own claim (its retained
    /// path column, named in the typed body) or a producer pointer whose slot names a spill file.
    /// Any other body is inline and is its own payload, whatever paths it mentions (F4).
    /// </summary>
    private static SpillPayload ReadPayload(
        DispatchBriefRowEvidence row, string marker, Func<string, string?>? readAbsoluteFile)
    {
        var location = TryReadPointerLocation(row.Body, marker, out var slot) && IsFileLocation(slot) ? slot : null;
        var queueClaim = !string.IsNullOrEmpty(row.RemoteSpillRelativePath)
            && row.Body.Contains(row.RemoteSpillRelativePath, StringComparison.Ordinal);
        if (location is null && !queueClaim)
            return new SpillPayload(false, row.Body, Conflict: false);

        var file = location is not null && IsRootedOnAnyPlatform(location) ? ReadSpill(location, readAbsoluteFile) : null;
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
    /// The rooted spill file a pointer names in its slot, or null. The local producer writes it
    /// with <see cref="Path.Combine(string, string, string)"/>, so either platform's root and
    /// separator can appear; one this host cannot read stays unproven.
    /// </summary>
    internal static string? AbsoluteSpillPath(string body) =>
        TryReadPointerLocation(body, TypedBodySpill.TryReadOpeningTaskMarker(body), out var location)
        && IsFileLocation(location) && IsRootedOnAnyPlatform(location)
            ? location
            : null;

    private const string BriefHeadline = "YOUR BRIEF IS NOT IN THIS MESSAGE. It is ";
    private const string MessageHeadline = TypedBodySpill.PointerHeadline + " It is ";
    private const string LengthUnit = " characters";
    private const string SlotOpen = "Read it in full before you do anything else:";
    private const string SlotClose = "Everything you need is there. Do not start from this summary.";
    private const string ReadOnlyLine = "Do NOT modify any files. This is a read-only task — report findings only.";
    private const string ReportHeading = "--- how to report back ---";
    private const string CompactOpen = "Read the complete task brief at ";
    private const string CompactClose = " before doing anything. Follow its reporting contract.";

    /// <summary>
    /// The location a pointer names, read only from the outer envelope its producer writes.
    /// <see cref="DelegationReportFormatter.BuildBriefPointer"/> is this task's header line, the
    /// title paragraph, then the headline paragraph with its length and location slot, then the
    /// read-only line or the report section; or the header and the compact second line. Since
    /// eb24e568f (2026-08-10) the headline is the third paragraph; 563568e60 (2026-09-24) put
    /// this task's marker in front of it, so a persisted row may carry either (F6). Both
    /// renderings, multi-line and the joined one line (ad258cd41), are read. A
    /// <see cref="TypedBodySpill"/> pointer opens with its own headline. A pointer quoted in a
    /// goal or a previous attempt's report is never in that position, so it is inline (F4, F7).
    /// </summary>
    internal static bool TryReadPointerLocation(string body, string? marker, out string location)
    {
        location = "";
        var text = body.ReplaceLineEndings("\n").Trim();
        var flat = !text.Contains('\n');
        int after;
        var brief = false;
        if (marker is not null && text.StartsWith(marker + " role=", StringComparison.Ordinal))
        {
            var compactTail = CompactClose + (flat ? " " : "\n") + marker;
            var compact = text.IndexOf(CompactOpen, StringComparison.Ordinal);
            var lineEnd = flat ? compact - 1 : text.IndexOf('\n');
            if (compact > 0 && lineEnd >= 0 && compact == lineEnd + 1
                && IsHeaderOnly(text[..lineEnd], marker) && text.EndsWith(compactTail, StringComparison.Ordinal))
            {
                return TrySlot(text, compact + CompactOpen.Length, text.Length - compactTail.Length, out location);
            }

            if (!TryReadBriefHeadline(text, marker, flat, out after))
                return false;
            brief = true;
        }
        else
        {
            var start = marker is not null && text.StartsWith(marker + " ", StringComparison.Ordinal)
                ? marker.Length + 1
                : 0;
            if (string.CompareOrdinal(text, start, MessageHeadline, 0, MessageHeadline.Length) != 0)
                return false;
            after = start + MessageHeadline.Length;
        }

        if (!TrySkipLength(text, after, out after))
            return false;
        var open = text.IndexOf(SlotOpen, after, StringComparison.Ordinal);
        if (open < 0 || text.AsSpan(after, open - after).Contains("\n\n", StringComparison.Ordinal))
            return false;
        var slot = open + SlotOpen.Length;
        var close = text.IndexOf(SlotClose, slot, StringComparison.Ordinal);
        if (close < 0 || (brief && !IsReportTail(text, close + SlotClose.Length)))
            return false;
        return TrySlot(text, slot, close, out location);
    }

    /// <summary>
    /// Where the full form's headline text starts. Multi-line: the header line, a blank line,
    /// one title paragraph, a blank line, then the headline, bare or after this task's marker.
    /// Joined: the first headline on the line, after a space, and after no other task's marker.
    /// </summary>
    private static bool TryReadBriefHeadline(string text, string marker, bool flat, out int after)
    {
        after = 0;
        int headline;
        if (flat)
        {
            headline = text.IndexOf(BriefHeadline, StringComparison.Ordinal);
            if (headline <= 0 || text[headline - 1] != ' ')
                return false;
            // A marker right before the headline is this task's (563568e60 onward), or none (F6).
            var before = text[..(headline - 1)];
            var token = before[(before.LastIndexOf(' ') + 1)..];
            if (token.StartsWith("[antiphon-task:", StringComparison.Ordinal) && token != marker)
                return false;
        }
        else
        {
            var headerEnd = text.IndexOf('\n');
            if (headerEnd < 0 || string.CompareOrdinal(text, headerEnd, "\n\n", 0, 2) != 0)
                return false;
            var titleEnd = text.IndexOf("\n\n", headerEnd + 2, StringComparison.Ordinal);
            if (titleEnd < 0 || string.IsNullOrWhiteSpace(text[(headerEnd + 2)..titleEnd]))
                return false;
            headline = titleEnd + 2;
            if (string.CompareOrdinal(text, headline, marker + " ", 0, marker.Length + 1) == 0)
                headline += marker.Length + 1;
            if (string.CompareOrdinal(text, headline, BriefHeadline, 0, BriefHeadline.Length) != 0)
                return false;
        }

        after = headline + BriefHeadline.Length;
        return true;
    }

    /// <summary>After the slot every producer version writes the read-only line or the report section.</summary>
    private static bool IsReportTail(string text, int start)
    {
        var rest = text.AsSpan(start).TrimStart();
        if (rest.StartsWith(ReadOnlyLine, StringComparison.Ordinal))
            rest = rest[ReadOnlyLine.Length..].TrimStart();
        return rest.StartsWith(ReportHeading, StringComparison.Ordinal);
    }

    private static bool IsHeaderOnly(string line, string marker)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || !string.Equals(tokens[0], marker, StringComparison.Ordinal))
            return false;
        for (var i = 1; i < tokens.Length; i++)
        {
            if (tokens[i].IndexOf('=') <= 0)
                return false;
        }

        return true;
    }

    /// <summary>The producer's <c>{length:N0} characters</c>; any culture's digit grouping.</summary>
    private static bool TrySkipLength(string text, int start, out int after)
    {
        after = start;
        var end = text.IndexOf(LengthUnit, start, StringComparison.Ordinal);
        if (end < 0 || end - start > 24)
            return false;
        var digits = 0;
        for (var i = start; i < end; i++)
        {
            if (char.IsAsciiDigit(text[i]))
                digits++;
            else if (text[i] == '\n' || char.IsLetter(text[i]))
                return false;
        }

        after = end + LengthUnit.Length;
        return digits > 0;
    }

    private static bool TrySlot(string text, int start, int end, out string location)
    {
        location = "";
        if (end < start)
            return false;
        var slot = text[start..end].Trim();
        if (slot.Length >= 2 && slot[0] == '\'' && slot[^1] == '\'')
            slot = slot[1..^1];
        if (slot.Length == 0 || slot.Contains('\n'))
            return false;
        location = slot;
        return true;
    }

    /// <summary>A spill file, not the API fallback the producer names when it could not write one.</summary>
    private static bool IsFileLocation(string location) =>
        location.Contains(".antiphon/", StringComparison.Ordinal)
        || location.Contains(".antiphon\\", StringComparison.Ordinal);

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
