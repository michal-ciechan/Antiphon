using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public enum TerminalRunnerSeatDecision
{
    Disabled, IncompleteAttempt, IncompleteReport, IdentityUnknown, Owned, StandingOwner,
    AlwaysOnOwner, BoardOwner, SpecialistOwner, WarmPool, VerificationOwner,
    SettlementTooYoung, PendingDelivery, Working, Unknown, Unsupported, Waiting,
    Reserved, AlreadyReserved, StaleAttempt
}

/// <summary>Pure decisions shared by the coordinator and its individual guard witnesses.</summary>
public sealed class TerminalRunnerSeatReleasePolicy
{
    public TerminalRunnerSeatDecision? TerminalAttempt(AgentTask? task)
    {
        if (task?.CompletedAt is null || task.Status is not (AgentTaskStatus.Succeeded
            or AgentTaskStatus.Failed or AgentTaskStatus.Canceled or AgentTaskStatus.Blocked))
            return TerminalRunnerSeatDecision.IncompleteAttempt;
        if (task.Status == AgentTaskStatus.Blocked && (string.IsNullOrWhiteSpace(task.Result)
            || task.ReportEvidence is not (AgentTaskReportEvidence.Marked
                or AgentTaskReportEvidence.UnmarkedAfterNudge or AgentTaskReportEvidence.QuestionHeuristic
                or AgentTaskReportEvidence.UnmarkedWaiting)))
            return TerminalRunnerSeatDecision.IncompleteReport;
        return null;
    }

    public TerminalRunnerSeatDecision? Custody(AgentTask task, Agent? agent)
    {
        if (task.SourceLandingOperationId is not null || task.VerificationCustodyContractVersion is not null)
            return TerminalRunnerSeatDecision.VerificationOwner;
        if (task.Role is AgentTaskRole.Check or AgentTaskRole.Distill or AgentTaskRole.Diagnose
            || agent?.StandingSpecialistOwnerId is not null || agent?.StandingSpecialistRole is not null)
            return TerminalRunnerSeatDecision.SpecialistOwner;
        if (agent is not null && !agent.IsPoolDelegate) return TerminalRunnerSeatDecision.StandingOwner;
        if (agent?.AlwaysOn == true) return TerminalRunnerSeatDecision.AlwaysOnOwner;
        if (agent?.BoardId is not null) return TerminalRunnerSeatDecision.BoardOwner;
        if (agent?.IsPoolDelegate == true && task.Workspace == WorkspaceMode.Shared
            && agent.PoolIdleSince is not null) return TerminalRunnerSeatDecision.WarmPool;
        return null;
    }

    public TerminalRunnerSeatDecision? SettlementAge(AgentTask task, DateTime now) =>
        task.CompletedAt is not DateTime completed || now - completed < TimeSpan.FromSeconds(120)
            ? TerminalRunnerSeatDecision.SettlementTooYoung : null;
}
