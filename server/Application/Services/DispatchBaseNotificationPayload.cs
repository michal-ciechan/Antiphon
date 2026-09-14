using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>One frozen dispatch-base warning draft captured with a successful claim.</summary>
internal sealed record DispatchWarningDraft(string WarningKey, string Detail);

internal sealed record DispatchBaseCapturedPayload(
    Guid WarningEventId,
    Guid NotificationId,
    Guid TaskId,
    Guid DispatchEventId,
    int Attempt,
    string WarningKey,
    AgentTaskReplyTo ReplyTo,
    Guid? ParentSessionId,
    string Detail,
    string Body,
    string ContentDigest,
    DateTime CreatedAt,
    LandNotificationState InitialState);

/// <summary>CARD-0508 S2b. Immutable dispatch-base warning body, header, digest, and initial state.</summary>
internal static class DispatchBaseNotificationPayload
{
    public const string SiblingKeyPrefix = "sibling:";
    public const string MismatchKey = "base-observation-stale";
    public const string DefaultUnresolvedKey = "default-unresolved";

    public static string Header(Guid notificationId, Guid taskId, Guid warningEventId) =>
        $"[dispatch-base {notificationId:N} task={taskId:N} warning={warningEventId:N}]";

    public static string Body(Guid notificationId, Guid taskId, Guid warningEventId, string detail) =>
        $"{Header(notificationId, taskId, warningEventId)}\n{detail}";

    public static string Digest(AgentTaskReplyTo replyTo, Guid? parentSessionId, string body) =>
        DelegationNoteDigest.Compute($"{replyTo}:{parentSessionId:N}\n{body}");

    public static LandNotificationState InitialState(AgentTaskReplyTo replyTo, Guid? parentSessionId) =>
        replyTo == AgentTaskReplyTo.None ? LandNotificationState.NotRequired
            : parentSessionId is null ? LandNotificationState.DestinationUnavailable
            : LandNotificationState.Queued;

    public static DispatchBaseCapturedPayload Capture(
        Guid warningEventId,
        Guid notificationId,
        Guid taskId,
        Guid dispatchEventId,
        int attempt,
        string warningKey,
        AgentTaskReplyTo replyTo,
        Guid? parentSessionId,
        string detail,
        DateTime createdAt)
    {
        var body = Body(notificationId, taskId, warningEventId, detail);
        return new DispatchBaseCapturedPayload(
            warningEventId, notificationId, taskId, dispatchEventId, attempt, warningKey,
            replyTo, parentSessionId, detail, body, Digest(replyTo, parentSessionId, body),
            createdAt, InitialState(replyTo, parentSessionId));
    }

    public static AgentTaskLandNotification Materialize(AgentTaskDispatchWarningIntent intent)
    {
        return new AgentTaskLandNotification
        {
            Id = intent.NotificationId,
            RequestId = null,
            TaskId = intent.TaskId,
            LandingOperationId = null,
            SourceEventId = intent.Id,
            Kind = LandNotificationKind.DispatchBase,
            ReplyTo = intent.ReplyTo,
            ParentSessionId = intent.ParentSessionId,
            Body = intent.Body,
            ContentDigest = intent.ContentDigest,
            CreatedAt = intent.CreatedAt,
            NextAttemptAt = intent.CreatedAt,
            State = intent.InitialState,
            ConcurrencyToken = Guid.NewGuid(),
        };
    }

    public static AgentTaskEvent WarningEvent(AgentTaskDispatchWarningIntent intent) => new()
    {
        Id = intent.Id,
        AgentTaskId = intent.TaskId,
        Type = AgentTaskEventType.Warning,
        Detail = intent.Detail,
        At = intent.CreatedAt,
    };

    public static string MismatchDetail(string observed, string actual, WorktreeBaseSource source) =>
        $"sibling containment was evaluated against {observed}; the worktree was cut from {actual} ({source}). Re-check before relying on the warnings above.";

    public static string DefaultUnresolvedDetail(string failedRef) =>
        $"configured default branch '{failedRef}' did not resolve; falling back to HEAD.";

    public static string SiblingKey(Guid siblingTaskId) => $"{SiblingKeyPrefix}{siblingTaskId:N}";
}
