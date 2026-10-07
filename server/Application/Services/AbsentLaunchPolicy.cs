using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Only complete positive evidence can admit the absent-launch exception.</summary>
internal sealed record AbsentLaunchEvidence(
    Guid TaskId, Guid SessionId, AgentTaskStatus Status, DateTime? DispatchedAt, string? Goal,
    string? FailureReason, IReadOnlyList<SessionQueuedMessage>? Messages,
    bool? KnownMessageColumns, bool? NeverWorking, bool? EmptyTranscript,
    bool? EmptyNativeTranscript, bool? RunnerAbsent);

internal static class AbsentLaunchPolicy
{
    // A new mapped column is unknown until its meaning is classified here and in the predicate.
    // No database query is needed to compare this census with the EF model.
    internal const string MessageColumns =
        "Id AgentSessionId Body RemoteSpillBody RemoteSpillRelativePath Status Sequence Origin " +
        "ExecutionTaskId SourceTaskId ExecutionDeadlineAt SpecialistInputPolicyJson " +
        "ConversationKey SourceLandNotificationId SourceScheduleId ContentDigest NoteHeader HoldUntil " +
        "CreatedAt SentAt CanceledAt DeliveryAttempts LastDeliveryStartedAt LastDeliveryGeneration " +
        "DeliveryVerdict DeliveryVerdictAt LastDeliveryBaselineSequence " +
        "PinRefreshKey PinRequestedRevision PinRequestedHash PinRequestedLocationGeneration " +
        "RulesRefreshKey RulesReceiptJson RulesDeadlineAt RulesAcknowledgedAt RulesFailure " +
        "RulesPromptSequence RulesTurnEndSequence RulesCoveredByMessageId RulesChainId " +
        "RulesFollowOnCount RulesBoundarySequence SourceChannelInboundId ChannelOutboundDeliveryId " +
        "ChannelReplyDiscoveryClosedAt ChannelReplySettledAt CapacityRecoveryActionKey CapacityWaitId " +
        "CapacityWaitVersion MaintenanceKind MaintenanceAcceptedStartedAt MaintenanceResult " +
        "MaintenanceResultAt MaintenanceEvidence SubmissionStartedAt MaintenanceSlotActive DeferredFromRunAttemptId";

    /// <summary>
    /// Exactly one original Pending brief, with every delivery/control/correlation field pristine;
    /// no other related message; no historical Working, database or native/sidecar entry; positive
    /// runner absence; and the exact runner-unknown failure. Null/unknown evidence never qualifies.
    /// Current Working/listed-runner safety withholds are enforced by the caller before this decision.
    /// </summary>
    internal static bool IsNeverAttempted(AbsentLaunchEvidence? evidence) =>
        evidence is
        {
            Status: AgentTaskStatus.Dispatched, DispatchedAt: not null,
            KnownMessageColumns: true, NeverWorking: true, EmptyTranscript: true,
            EmptyNativeTranscript: true, RunnerAbsent: true, Messages.Count: 1,
        }
        && evidence.TaskId != Guid.Empty && evidence.SessionId != Guid.Empty
        && !string.IsNullOrWhiteSpace(evidence.Goal)
        && evidence.FailureReason == SessionReconciliationService.RunnerUnknownSessionReason
        && evidence.Messages[0] is
        {
            Origin: QueuedMessageOrigin.Delegation, Status: QueuedMessageStatus.Pending, Sequence: 1,
            SentAt: null, CanceledAt: null, DeliveryAttempts: 0, LastDeliveryStartedAt: null,
            LastDeliveryGeneration: null, LastDeliveryBaselineSequence: null,
            DeliveryVerdict: null, DeliveryVerdictAt: null,
            ConversationKey: null, SourceLandNotificationId: null, SourceScheduleId: null,
            ContentDigest: null, NoteHeader: null, HoldUntil: null,
            PinRefreshKey: null, PinRequestedRevision: null, PinRequestedHash: null,
            PinRequestedLocationGeneration: null, RulesRefreshKey: null, RulesReceiptJson: null,
            RulesDeadlineAt: null, RulesAcknowledgedAt: null, RulesFailure: null,
            RulesPromptSequence: null, RulesTurnEndSequence: null, RulesCoveredByMessageId: null,
            RulesChainId: null, RulesFollowOnCount: 0, RulesBoundarySequence: null,
            SourceChannelInboundId: null, ChannelOutboundDeliveryId: null,
            ChannelReplyDiscoveryClosedAt: null, ChannelReplySettledAt: null,
            CapacityRecoveryActionKey: null, CapacityWaitId: null, CapacityWaitVersion: null,
            MaintenanceKind: RemoteControlMaintenanceKind.None, MaintenanceAcceptedStartedAt: null,
            MaintenanceResult: null, MaintenanceResultAt: null, MaintenanceEvidence: null,
            SubmissionStartedAt: null, MaintenanceSlotActive: false, DeferredFromRunAttemptId: null,
        } row
        && row.Id != Guid.Empty && row.AgentSessionId == evidence.SessionId
        && row.CreatedAt >= evidence.DispatchedAt && !string.IsNullOrWhiteSpace(row.Body)
        && ((row.ExecutionTaskId == evidence.TaskId && row.SourceTaskId is null)
            || (row.ExecutionTaskId is null && row.SourceTaskId == evidence.TaskId
                && row.Body.Contains(DelegationReportFormatter.TaskMarker(evidence.TaskId), StringComparison.Ordinal)));
}
