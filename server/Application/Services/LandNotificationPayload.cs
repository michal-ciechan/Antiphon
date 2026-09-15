using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

internal static class LandNotificationPayload
{
    private const int EnvelopeBytes = 1024;
    // Leaves room for a sibling count even with four distinct 64-character SHAs.
    private const int DiagnosticReserveBytes = 448;

    public static AgentTaskLandNotification Create(AgentTaskLandRequest request, AgentTaskEvent source, LandNotificationKind kind,
        WorktreeCleanupReference? cleanupCapture = null, IReadOnlyList<string>? unlandedSiblings = null,
        string? cleanupReason = null)
    {
        var id = Guid.NewGuid();
        var header = $"[land {id:N} request={request.Id:N} task={request.TaskId:N} outcome={source.Type}]";
        var approval = $"expected={request.ExpectedSourceSha ?? "null"}; local={request.LocalBeforeSha ?? "null"}; remote={request.RemoteSourceSha ?? "null"}; candidate={request.CandidateSourceSha ?? "null"}";
        var body = $"{header}\npublication={source.LandingPublication?.ToString() ?? "Unconfirmed"}; cleanup={source.LandingCleanup?.ToString() ?? "NotStarted"}\n{approval}\n{source.Detail}";
        if (cleanupCapture is not null)
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
            // Reserve diagnostics FIRST. Sibling names cannot borrow this space even when
            // the capture is small; optional diagnostic detail can use any leftover bytes.
            var remaining = EnvelopeBytes - Encoding.UTF8.GetByteCount(prefix);
            var (required, optional) = new WorktreeCleanupPresentation().NotificationParts(
                cleanupCapture, cleanupReason ?? "cleanup complete", DiagnosticReserveBytes);
            var siblings = SummarizeSiblings(unlandedSiblings, remaining - DiagnosticReserveBytes - 1);
            body = prefix + required + (siblings is null ? "" : "\n" + siblings);
            remaining = EnvelopeBytes - Encoding.UTF8.GetByteCount(body) - 1;
            if (remaining > 0) body += "\n" + WorktreeCleanupPresentation.ClipUtf8(optional, remaining);
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

    private static string? SummarizeSiblings(IReadOnlyList<string>? siblings, int byteLimit)
    {
        if (siblings is null || siblings.Count == 0) return null;
        const string marker = "unlanded-sibling=";
        var full = marker + string.Join(",", siblings);
        if (Encoding.UTF8.GetByteCount(full) <= byteLimit) return full;

        var shown = new List<string>();
        var summary = $"{marker}{siblings.Count} siblings, showing first 0";
        foreach (var sibling in siblings)
        {
            var candidate = $"{marker}{siblings.Count} siblings, showing first {shown.Count + 1}: "
                + string.Join(",", shown.Append(sibling));
            if (Encoding.UTF8.GetByteCount(candidate) > byteLimit) break;
            shown.Add(sibling);
            summary = candidate;
        }
        return summary;
    }
}
