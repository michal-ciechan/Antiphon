using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

internal static class LandNotificationPayload
{
    public static AgentTaskLandNotification Create(AgentTaskLandRequest request, AgentTaskEvent source, LandNotificationKind kind)
    {
        var id = Guid.NewGuid();
        var header = $"[land {id:N} request={request.Id:N} task={request.TaskId:N} outcome={source.Type}]";
        var approval = $"expected={request.ExpectedSourceSha ?? "null"}; local={request.LocalBeforeSha ?? "null"}; remote={request.RemoteSourceSha ?? "null"}; candidate={request.CandidateSourceSha ?? "null"}";
        var body = $"{header}\npublication={source.LandingPublication?.ToString() ?? "Unconfirmed"}; cleanup={source.LandingCleanup?.ToString() ?? "NotStarted"}\n{approval}\n{source.Detail}";
        return new AgentTaskLandNotification
        {
            Id = id, RequestId = request.Id, TaskId = request.TaskId, SourceEventId = source.Id,
            LandingOperationId = source.LandingOperationId, Kind = kind, ReplyTo = request.ReplyTo,
            ParentSessionId = request.ParentSessionId, Body = body,
            ContentDigest = DelegationNoteDigest.Compute($"{request.ReplyTo}:{request.ParentSessionId:N}\n{body}"),
            CreatedAt = source.At, NextAttemptAt = source.At,
            State = request.ReplyTo == AgentTaskReplyTo.None ? LandNotificationState.NotRequired
                : request.ParentSessionId is null ? LandNotificationState.DestinationUnavailable : LandNotificationState.Queued,
        };
    }
}
