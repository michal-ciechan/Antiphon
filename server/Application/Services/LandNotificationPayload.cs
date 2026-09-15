using System.Text;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

internal static class LandNotificationPayload
{
    public static AgentTaskLandNotification Create(AgentTaskLandRequest request, AgentTaskEvent source, LandNotificationKind kind,
        string? cleanupDetail = null, string? unlandedSiblingMarker = null)
    {
        var id = Guid.NewGuid();
        var header = $"[land {id:N} request={request.Id:N} task={request.TaskId:N} outcome={source.Type}]";
        var approval = $"expected={request.ExpectedSourceSha ?? "null"}; local={request.LocalBeforeSha ?? "null"}; remote={request.RemoteSourceSha ?? "null"}; candidate={request.CandidateSourceSha ?? "null"}";
        var body = $"{header}\npublication={source.LandingPublication?.ToString() ?? "Unconfirmed"}; cleanup={source.LandingCleanup?.ToString() ?? "NotStarted"}\n{approval}\n{source.Detail}";
        if (cleanupDetail is not null)
        {
            // Keep the detailed publication narrative on the source event. The immutable
            // diagnostic envelope must also fit the supported inbox-conhost write contract.
            // Equal approval coordinates can name the preceding field without losing a SHA.
            var coordinates = new[] { ("expected", request.ExpectedSourceSha), ("local", request.LocalBeforeSha),
                ("remote", request.RemoteSourceSha), ("candidate", request.CandidateSourceSha) };
            var values = new Dictionary<string, string>();
            var fields = new List<string>();
            foreach (var (name, sha) in coordinates)
            {
                var value = sha ?? "null";
                fields.Add($"{name}={values.GetValueOrDefault(value, value)}");
                values.TryAdd(value, name);
            }
            var prefix = $"{header}\npublication={source.LandingPublication?.ToString() ?? "Unconfirmed"}; cleanup={source.LandingCleanup?.ToString() ?? "NotStarted"}\n{string.Join("; ", fields)}\n";
            // The sibling warning is actionable publication evidence. Carry the producer's
            // exact marker ahead of optional diagnostic display text without parsing prose.
            var detail = unlandedSiblingMarker is null ? cleanupDetail : $"{unlandedSiblingMarker}\n{cleanupDetail}";
            body = prefix + WorktreeCleanupPresentation.ClipUtf8(detail, 1024 - Encoding.UTF8.GetByteCount(prefix));
        }
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
