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

internal static class DispatchBriefEvidence
{
    public const string InputUnavailableReason =
        "dispatch_brief_input_unavailable: retained brief spill is missing or conflicts; the original row was not replaced.";

    public const string EvidenceUncertainReason =
        "dispatch_brief_evidence_uncertain: current brief evidence is not a complete unattempted row or a complete receipt.";

    public const string AttemptOwnedReason =
        "dispatch_brief_attempt_owned: the current brief was already attempted; queue recovery keeps that row.";

    public static DispatchBriefDecision Classify(
        DispatchBriefEnsureRequest request,
        DispatchBriefTaskSnapshot task,
        IReadOnlyList<DispatchBriefRowEvidence> rows,
        IReadOnlyList<DispatchBriefPromptEvidence> prompts,
        Func<string, string?>? readAbsoluteFile = null)
    {
        if (task.Status is not (AgentTaskStatus.Dispatched or AgentTaskStatus.Working))
            return new DispatchBriefDecision(DispatchBriefKind.Uncertain, Hold: false, Reason: null);

        if (task.Attempt != request.Attempt
            || task.AgentSessionId != request.SessionId
            || task.DispatchedAt is not DateTime dispatchedAt
            || task.SessionStartedAt is not DateTime startedAt
            || !SameGeneration(dispatchedAt, request.DispatchedAt)
            || !SameGeneration(startedAt, request.SessionStartedAt))
            return new DispatchBriefDecision(DispatchBriefKind.Superseded, Hold: false, Reason: null);

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

        if (recognized.Count > 1)
            return Uncertain();

        if (recognized.Count == 1)
            return ClassifyRecognized(recognized[0], request, marker, task.Goal, prompts, readAbsoluteFile);

        if (current.Count > 0 || HasCurrentMarkerPrompt(prompts, request, marker))
            return Uncertain();

        return new DispatchBriefDecision(DispatchBriefKind.Absent, Hold: false, Reason: null);
    }

    private static DispatchBriefDecision ClassifyRecognized(
        DispatchBriefRowEvidence row,
        DispatchBriefEnsureRequest request,
        string marker,
        string? goal,
        IReadOnlyList<DispatchBriefPromptEvidence> prompts,
        Func<string, string?>? readAbsoluteFile)
    {
        if (row.Status == QueuedMessageStatus.Canceled || row.CanceledAt is not null)
            return Uncertain();

        if (IsDamaged(row, marker, goal, readAbsoluteFile))
            return new DispatchBriefDecision(DispatchBriefKind.Unavailable, Hold: true, InputUnavailableReason);

        if (HasCompleteReceipt(row, marker, prompts, request, readAbsoluteFile))
            return new DispatchBriefDecision(DispatchBriefKind.Received, Hold: false, Reason: null);

        if (IsUnattempted(row))
            return new DispatchBriefDecision(DispatchBriefKind.Reuse, Hold: false, Reason: null);

        if (IsAttempted(row))
            return new DispatchBriefDecision(DispatchBriefKind.AttemptOwned, Hold: false, AttemptOwnedReason);

        return Uncertain();
    }

    private static DispatchBriefDecision Uncertain() =>
        new(DispatchBriefKind.Uncertain, Hold: true, EvidenceUncertainReason);

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

    private static bool IsDamaged(
        DispatchBriefRowEvidence row,
        string marker,
        string? goal,
        Func<string, string?>? readAbsoluteFile)
    {
        var claimsSpill = !string.IsNullOrEmpty(row.RemoteSpillRelativePath)
            || row.Body.Contains(".antiphon/", StringComparison.Ordinal);
        if (!claimsSpill)
            return false;

        var path = AbsoluteSpillPath(row.Body);
        string? file = path is null ? null : readAbsoluteFile?.Invoke(path);
        if (!string.IsNullOrEmpty(row.RemoteSpillBody)
            && file is not null
            && !string.Equals(file, row.RemoteSpillBody, StringComparison.Ordinal))
            return true;

        var authoritative = !string.IsNullOrEmpty(row.RemoteSpillBody) ? row.RemoteSpillBody : file;
        if (string.IsNullOrEmpty(authoritative))
            return true;
        if (!authoritative.Contains(marker, StringComparison.Ordinal))
            return true;
        var expectedGoal = goal?.Trim();
        if (!string.IsNullOrEmpty(expectedGoal)
            && !authoritative.Contains(expectedGoal, StringComparison.Ordinal))
            return true;
        return false;
    }

    private static bool HasCompleteReceipt(
        DispatchBriefRowEvidence row,
        string marker,
        IReadOnlyList<DispatchBriefPromptEvidence> prompts,
        DispatchBriefEnsureRequest request,
        Func<string, string?>? readAbsoluteFile)
    {
        var body = AuthoritativeBody(row, readAbsoluteFile);
        if (!PromptSubmissionMatch.RequiresTextMatch(body))
            return false;
        foreach (var prompt in prompts)
        {
            if (!PromptInWindow(prompt, request))
                continue;
            if (!string.Equals(prompt.Kind, TranscriptKinds.UserPrompt, StringComparison.Ordinal))
                continue;
            if (string.IsNullOrEmpty(prompt.Text) || !prompt.Text.Contains(marker, StringComparison.Ordinal))
                continue;
            if (PromptSubmissionMatch.IsCompleteIn(body, prompt.Text))
                return true;
        }

        return false;
    }

    private static string? AuthoritativeBody(DispatchBriefRowEvidence row, Func<string, string?>? readAbsoluteFile)
    {
        if (!string.IsNullOrEmpty(row.RemoteSpillBody))
            return row.RemoteSpillBody;
        var path = AbsoluteSpillPath(row.Body);
        if (path is not null && readAbsoluteFile?.Invoke(path) is { Length: > 0 } file)
            return file;
        return row.Body;
    }

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

    internal static string? AbsoluteSpillPath(string body)
    {
        const string token = ".antiphon/";
        var index = 0;
        while ((index = body.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            var start = index;
            while (start > 0 && !char.IsWhiteSpace(body[start - 1]))
                start--;
            var end = index + token.Length;
            while (end < body.Length && !char.IsWhiteSpace(body[end]))
                end++;
            var path = body[start..end].Trim('`', '"', '\'', ',', '.', ')', '(');
            if (Path.IsPathRooted(path))
                return path;
            index = end;
        }

        return null;
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
