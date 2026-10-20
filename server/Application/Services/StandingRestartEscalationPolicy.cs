using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>The bounded exception to strict standing resume; reservation rechecks this under locks.</summary>
public sealed class StandingRestartEscalationPolicy(SupervisionSettings settings, DelegationSettings delegation)
{
    public bool CanReserve(Agent agent, AgentSupervisionState state, AgentSession? current, AgentKind kind) =>
        settings.Enabled && settings.AutomaticFreshAfterResumeFailures > 0
        && state.AutomaticResumeFailures >= settings.AutomaticFreshAfterResumeFailures
        && !state.AutomaticFreshConsumed && !state.Suspended && state.LivenessLatchedAt is null
        && state.HerdrFailureHeldAt is null && state.ActiveCompactionRecoveryId is null
        && agent.AlwaysOn && !agent.IsPoolDelegate && agent.StandingSpecialistRole is null
        && !StandingSpecialistSeatPolicy.IsCheck(agent, delegation)
        && kind is AgentKind.ClaudeCode or AgentKind.Grok
        && current is not null && current.AgentKind == kind
        && current.CardId is null && current.WorktreeId is null
        && current.Status is SessionStatus.Stopped or SessionStatus.Failed
        && current.RestartFailureKind != RestartFailureKind.ContinuityUnavailable
        && agent.PersistentSessionId == current.Id.ToString("D")
        && state.LastAutomaticResumeSessionId == current.Id
        && state.LastAutomaticResumeStartedAt == current.StartedAt
        && (state.ContinuityHeldAt is null
            || state.ContinuityReason == StandingContinuityReason.RepeatedResumeFailure
                && state.ContinuitySessionId == current.Id);

    // Producer-owned destinations need their own transfer protocol. S2 admits only ordinary
    // untouched input; it never rewrites a report, channel, maintenance or receipt binding.
    public bool CanTransfer(SessionQueuedMessage message) =>
        StandingQueueSwitchPolicy.NeverAttempted(message)
        && message.Origin is QueuedMessageOrigin.Ui or QueuedMessageOrigin.System
        && message.SourceTaskId is null && message.SourceLandNotificationId is null
        && message.SourceChannelInboundId is null && message.ChannelOutboundDeliveryId is null
        && message.ConversationKey is null && message.SourceScheduleId is null
        && message.ReviewThreadId is null && message.RuntimeActionKey is null
        && message.ExecutionTaskId is null && message.SpecialistInputPolicyJson is null
        && message.PinRefreshKey is null && message.CapacityRecoveryActionKey is null
        && message.CapacityWaitId is null && message.MaintenanceKind == RemoteControlMaintenanceKind.None
        && message.RemoteSpillBody is null && message.RemoteSpillRelativePath is null
        && message.ContentDigest is null && message.NoteHeader is null
        && message.DirectReplyProvider is null && message.DirectReplyConversationId is null
        && message.DirectReplyHandle is null && message.DirectReplyPromptSequence is null
        && message.DirectReplyThroughSequence is null && message.DirectReplyPendingThroughSequence is null
        && message.SubmissionStartedAt is null && message.LastDeliveryGeneration is null;
}
