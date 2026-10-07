using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class AbsentLaunchPolicyTests
{
    [Test]
    [Arguments("unknown-evidence")]
    [Arguments("unknown-columns")]
    [Arguments("null-columns")]
    [Arguments("working-task")]
    [Arguments("missing-dispatch")]
    [Arguments("empty-task-id")]
    [Arguments("empty-session-id")]
    [Arguments("empty-goal")]
    [Arguments("wrong-reason")]
    [Arguments("missing-reason")]
    [Arguments("missing-messages")]
    [Arguments("missing-brief")]
    [Arguments("F2-other-message")]
    [Arguments("F2-prior-working")]
    [Arguments("null-working")]
    [Arguments("F2-any-transcript")]
    [Arguments("null-transcript")]
    [Arguments("D1-native-entry")]
    [Arguments("null-native")]
    [Arguments("listed-runner")]
    [Arguments("unknown-runner")]
    [Arguments("empty-message-id")]
    [Arguments("wrong-session")]
    [Arguments("wrong-origin")]
    [Arguments("not-pending")]
    [Arguments("not-first-brief")]
    [Arguments("old-brief")]
    [Arguments("empty-body")]
    [Arguments("unknown-task-identity")]
    [Arguments("conflicting-task-identity")]
    [Arguments("F1-source-task-delivered")]
    [Arguments("legacy-marker-missing")]
    [Arguments("row.SentAt")]
    [Arguments("row.CanceledAt")]
    [Arguments("row.DeliveryAttempts")]
    [Arguments("row.LastDeliveryStartedAt")]
    [Arguments("row.LastDeliveryGeneration")]
    [Arguments("row.LastDeliveryBaselineSequence")]
    [Arguments("row.DeliveryVerdict")]
    [Arguments("row.DeliveryVerdictAt")]
    [Arguments("row.ConversationKey")]
    [Arguments("row.SourceLandNotificationId")]
    [Arguments("row.SourceScheduleId")]
    [Arguments("row.ContentDigest")]
    [Arguments("row.NoteHeader")]
    [Arguments("row.HoldUntil")]
    [Arguments("row.PinRefreshKey")]
    [Arguments("row.PinRequestedRevision")]
    [Arguments("row.PinRequestedHash")]
    [Arguments("row.PinRequestedLocationGeneration")]
    [Arguments("row.RulesRefreshKey")]
    [Arguments("row.RulesReceiptJson")]
    [Arguments("row.RulesDeadlineAt")]
    [Arguments("row.RulesAcknowledgedAt")]
    [Arguments("row.RulesFailure")]
    [Arguments("row.RulesPromptSequence")]
    [Arguments("row.RulesTurnEndSequence")]
    [Arguments("row.RulesCoveredByMessageId")]
    [Arguments("row.RulesChainId")]
    [Arguments("row.RulesFollowOnCount")]
    [Arguments("row.RulesBoundarySequence")]
    [Arguments("row.SourceChannelInboundId")]
    [Arguments("row.ChannelOutboundDeliveryId")]
    [Arguments("row.ChannelReplyDiscoveryClosedAt")]
    [Arguments("row.ChannelReplySettledAt")]
    [Arguments("row.CapacityRecoveryActionKey")]
    [Arguments("row.CapacityWaitId")]
    [Arguments("row.CapacityWaitVersion")]
    [Arguments("row.MaintenanceKind")]
    [Arguments("row.MaintenanceAcceptedStartedAt")]
    [Arguments("row.MaintenanceResult")]
    [Arguments("row.MaintenanceResultAt")]
    [Arguments("row.MaintenanceEvidence")]
    [Arguments("row.SubmissionStartedAt")]
    [Arguments("row.MaintenanceSlotActive")]
    [Arguments("row.DeferredFromRunAttemptId")]
    public void C1149_Whitelist_requires_every_fact(string condition)
    {
        var task = Guid.NewGuid();
        var session = Guid.NewGuid();
        var at = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        var row = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = session,
            ExecutionTaskId = task, Body = DelegationReportFormatter.TaskMarker(task) + "\nretained brief",
            Sequence = 1, CreatedAt = at, Origin = QueuedMessageOrigin.Delegation };
        AbsentLaunchEvidence? evidence = new(task, session, AgentTaskStatus.Dispatched, at, "retained goal",
            SessionReconciliationService.RunnerUnknownSessionReason, [row], true, true, true, true, true);
        // Independent positive companion ensures this is not an always-refuse implementation.
        AbsentLaunchPolicy.IsNeverAttempted(evidence).ShouldBeTrue("pristine original brief");
        if (condition.StartsWith("row.", StringComparison.Ordinal))
        {
            var property = typeof(SessionQueuedMessage).GetProperty(condition[4..])!;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object value = type == typeof(string) ? "evidence" : type == typeof(Guid) ? Guid.NewGuid()
                : type == typeof(DateTime) ? at : type == typeof(bool) ? true
                : type.IsEnum ? Enum.ToObject(type, 1) : Convert.ChangeType(1, type);
            property.SetValue(row, value);
        }
        else switch (condition)
        {
            case "unknown-evidence": evidence = null; break;
            case "unknown-columns": evidence = evidence with { KnownMessageColumns = false }; break;
            case "null-columns": evidence = evidence with { KnownMessageColumns = null }; break;
            case "working-task": evidence = evidence with { Status = AgentTaskStatus.Working }; break;
            case "missing-dispatch": evidence = evidence with { DispatchedAt = null }; break;
            case "empty-task-id": evidence = evidence with { TaskId = Guid.Empty }; row.ExecutionTaskId = Guid.Empty; break;
            case "empty-session-id": evidence = evidence with { SessionId = Guid.Empty }; row.AgentSessionId = Guid.Empty; break;
            case "empty-goal": evidence = evidence with { Goal = " " }; break;
            case "wrong-reason": evidence = evidence with { FailureReason = "different reason" }; break;
            case "missing-reason": evidence = evidence with { FailureReason = null }; break;
            case "missing-messages": evidence = evidence with { Messages = null }; break;
            case "missing-brief": evidence = evidence with { Messages = [] }; break;
            case "F2-other-message": evidence = evidence with { Messages = [row, new SessionQueuedMessage()] }; break;
            case "F2-prior-working": evidence = evidence with { NeverWorking = false }; break;
            case "null-working": evidence = evidence with { NeverWorking = null }; break;
            case "F2-any-transcript": evidence = evidence with { EmptyTranscript = false }; break;
            case "null-transcript": evidence = evidence with { EmptyTranscript = null }; break;
            case "D1-native-entry": evidence = evidence with { EmptyNativeTranscript = false }; break;
            case "null-native": evidence = evidence with { EmptyNativeTranscript = null }; break;
            case "listed-runner": evidence = evidence with { RunnerAbsent = false }; break;
            case "unknown-runner": evidence = evidence with { RunnerAbsent = null }; break;
            case "empty-message-id": row.Id = Guid.Empty; break;
            case "wrong-session": row.AgentSessionId = Guid.NewGuid(); break;
            case "wrong-origin": row.Origin = QueuedMessageOrigin.Ui; break;
            case "not-pending": row.Status = QueuedMessageStatus.Sent; break;
            case "not-first-brief": row.Sequence = 2; break;
            case "old-brief": row.CreatedAt = at.AddTicks(-1); break;
            case "empty-body": row.Body = " "; break;
            case "unknown-task-identity": row.ExecutionTaskId = null; break;
            case "conflicting-task-identity": row.SourceTaskId = Guid.NewGuid(); break;
            case "F1-source-task-delivered":
                row.ExecutionTaskId = null; row.SourceTaskId = task;
                AbsentLaunchPolicy.IsNeverAttempted(evidence).ShouldBeTrue("pristine legacy rules brief");
                row.DeliveryVerdict = DeliveryVerdict.Delivered; break;
            case "legacy-marker-missing":
                row.ExecutionTaskId = null; row.SourceTaskId = task;
                AbsentLaunchPolicy.IsNeverAttempted(evidence).ShouldBeTrue("pristine legacy rules brief");
                row.Body = "unattributed legacy message"; break;
            default: throw new ArgumentOutOfRangeException(nameof(condition));
        }
        AbsentLaunchPolicy.IsNeverAttempted(evidence).ShouldBeFalse(condition);
    }

    [Test]
    public void C1149_Queue_columns_are_accounted_for()
    {
        // A newly mapped scalar cannot be silently accepted by the whitelist.
        typeof(SessionQueuedMessage).GetProperties().Where(p => p.Name != nameof(SessionQueuedMessage.AgentSession))
            .Select(p => p.Name).Order().ShouldBe(AbsentLaunchPolicy.MessageColumns.Split(' ').Order());
    }
}
