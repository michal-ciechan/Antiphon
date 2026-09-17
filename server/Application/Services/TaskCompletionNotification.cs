using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0544 D-9. Composes the durable caller-completion obligation for a profile-v1 Code/Review
/// settlement on the CARD-0481 <see cref="AgentTaskLandNotification"/> outbox. Not a transport and
/// not a worker: the reply settlement adds the row to its own transaction, and the existing
/// <see cref="AgentTaskLandNotificationService"/> / hosted scanner deliver and confirm it.
///
/// <para>Two JSON documents ride the row. <see cref="Snapshot"/> is immutable per settlement: the
/// exact raw result and its SHA-256, the normalized digest, the commissioned profile, the frozen
/// note header and raw fallback body, and the distillation deadline decided at settlement. Recovery
/// renders only from it — never from the current task Result, card policy, HEAD or a later
/// outcome. <see cref="Delivery"/> is written with the first typed attempt and freezes the exact
/// wire text (and spill identity) that receipt must find in the caller transcript.</para>
/// </summary>
public static class TaskCompletionNotification
{
    public const int SnapshotVersion = 1;
    public const string KeyPrefix = "task:";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// G-119: only profile-v1 terminal reported settlements that reply into a session. Legacy
    /// (null-profile), non-session and Blocked/question settlements keep today's direct note.
    /// </summary>
    public static bool Applies(AgentTask task) =>
        task.VerificationProfileVersion is not null
        && task.VerificationRound is not null
        && task.ReplyTo == AgentTaskReplyTo.Session
        && task.ParentSessionId is not null
        && task.Status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled;

    public sealed record Snapshot(
        int Version,
        Guid TaskId,
        Guid RootTaskId,
        Guid SourceEventId,
        Guid? StageOutcomeId,
        Guid ParentSessionId,
        AgentTaskStatus Status,
        string RawResult,
        string RawSha256,
        string NoteDigest,
        int ProfileVersion,
        VerificationRound Round,
        VerificationScope? CompletedScope,
        Guid? SubjectTaskId,
        Guid? BaselineOutcomeId,
        VerificationSelectionReference? Selection,
        bool PendingFinalReview,
        string? NextStage,
        string? Handoff,
        string NoteHeader,
        string RawBody,
        string? ReportFilePath,
        string? DeliverablePath,
        string? DeliverableRef,
        string? RepoPath,
        string WorkingDirectory,
        string? WorktreePath,
        bool DistillRequested,
        OutputDistillerMode DistillMode,
        DateTime? DistillRequestedAt,
        DateTime? DistillDeadlineAt);

    /// <summary>What the queue actually typed on the first committed attempt, frozen for replay and receipt.</summary>
    public sealed record Delivery(
        int Version,
        string RenderingKind,
        string LogicalNote,
        string WireText,
        string WireSha256,
        IReadOnlyList<Guid> MemberQueueIds,
        string? SpillPath,
        string? SpillSha256,
        DateTime CommittedAt);

    /// <summary>
    /// CARD-0544 D-9 on the unified <see cref="LandNotificationKind.TaskCompletion"/> kind: a row
    /// carrying a snapshot is this obligation (renderable, frozen wire receipt). A snapshot-less
    /// TaskCompletion is CARD-0527's legacy commit-outcome note and keeps the immutable-Body rule.
    /// Keep the EF query seams (<c>Kind == TaskCompletion &amp;&amp; CompletionSnapshotJson != null</c>) in step.
    /// </summary>
    public static bool IsProfiled(AgentTaskLandNotification notification) =>
        notification.Kind == LandNotificationKind.TaskCompletion && notification.CompletionSnapshotJson is not null;

    public static AgentTaskLandNotification Create(AgentTask task, AgentTaskEvent settlementEvent, Snapshot snapshot, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        TaskId = task.Id,
        SourceEventId = settlementEvent.Id,
        Kind = LandNotificationKind.TaskCompletion,
        ReplyTo = task.ReplyTo,
        ParentSessionId = task.ParentSessionId,
        Body = snapshot.RawBody,
        ContentDigest = snapshot.NoteDigest,
        CreatedAt = now,
        NextAttemptAt = now,
        State = LandNotificationState.Queued,
        CompletionSnapshotJson = JsonSerializer.Serialize(snapshot, Json),
    };

    public static string ConversationKey(Guid rootTaskId) => $"{KeyPrefix}{rootTaskId:N}";

    public static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(text))).ToLowerInvariant();

    public static Snapshot? TryReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(json, Json);
            return snapshot is { Version: SnapshotVersion } && Sha256(snapshot.RawResult) == snapshot.RawSha256
                ? snapshot : null;
        }
        catch (JsonException) { return null; }
    }

    public static string SerializeDelivery(Delivery delivery) => JsonSerializer.Serialize(delivery, Json);

    public static Delivery? TryReadDelivery(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var delivery = JsonSerializer.Deserialize<Delivery>(json, Json);
            return delivery is { Version: SnapshotVersion } && Sha256(delivery.WireText) == delivery.WireSha256
                ? delivery : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The verification header bit every rendering keeps: commissioned round, completed scope
    /// (Review only) and whether a Final Review is still owed.
    /// </summary>
    public static string? HeaderBit(AgentTask task, VerificationScope? completedScope)
    {
        if (task.VerificationProfileVersion is null || task.VerificationRound is not { } round)
            return null;
        var bits = $"verification={round}";
        if (task.Role == AgentTaskRole.Review)
            bits += $"; scope={(completedScope?.ToString() ?? "Unknown")}";
        bits += round == VerificationRound.Interim ? "; final-review=pending" : "; final-review=none";
        return bits;
    }

    /// <summary>A rendering is authorized only while it still carries the snapshot's exact header.</summary>
    public static bool RenderingKeepsHeader(Snapshot snapshot, string logicalNote) =>
        logicalNote.ReplaceLineEndings("\n").StartsWith(snapshot.NoteHeader, StringComparison.Ordinal);
}
