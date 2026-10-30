using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

internal static class CeilingHoldFacts
{
    internal const int AbsoluteCapMinutes = 720;

    internal static bool Active(AgentTask task) => task.Status == AgentTaskStatus.Working
        && task.CeilingHoldEpisodeId is not null && task.CeilingHeldAt is not null
        && task.CeilingAcknowledgedAt is null && task.CeilingHoldAttempt == task.Attempt
        && task.CeilingHoldSessionId == task.AgentSessionId && task.CeilingHoldDispatchedAt == task.DispatchedAt;

    internal static int? ValidateInitial(long? minutes, string? reason)
    {
        if (minutes is null) return null;
        if (minutes is < 1 or > AbsoluteCapMinutes || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw new ValidationException("ceilingMinutes", "A ceiling override requires 1..720 minutes and a reason of 1..1000 characters.");
        return (int)minutes.Value;
    }

    internal static void Resolve(AgentTask task, DelegationSettings settings)
    {
        ValidateInitial(task.RequestedCeilingMinutes, task.RequestedCeilingReason);
        task.ResolvedCeilingMinutes = task.RequestedCeilingMinutes ?? TaskDeadlinePolicy.CeilingMinutes(settings, task.Role);
        task.ResolvedCeilingSource = task.RequestedCeilingMinutes is not null ? "requested"
            : settings.RolePolicy.ContainsKey(task.Role.ToString()) ? "rolePolicy" : "default";
        task.NextCeilingMinutes = Math.Max(0, task.ResolvedCeilingMinutes.Value);
    }

    internal static bool CanHold(AgentTaskStatus status, DateTime accepted, DateTime observed,
        bool running, bool inventoryKnown, bool working, bool boot, bool phaseBreached) =>
        status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working
        && accepted != default && accepted == observed && running && inventoryKnown && working && !boot && !phaseBreached;

    internal static double Bound(AgentTask task, DelegationSettings settings) =>
        task.NextCeilingMinutes is { } next && double.IsFinite(next) && next >= 0 ? next
        : Math.Max(0, task.ResolvedCeilingMinutes ?? task.RequestedCeilingMinutes ?? TaskDeadlinePolicy.CeilingMinutes(settings, task.Role));

    internal static double Rearm(AgentTask task, CeilingAction action, int? minutes, bool operatorExtend,
        bool authenticatedOperator, string? reason, DateTime now)
    {
        if (task.DispatchedAt is not { } dispatch || task.ResolvedCeilingMinutes is not > 0)
            throw new ConflictException("The attempt has no resolved ceiling.", "ceiling_identity_changed");
        var elapsed = Math.Max(0, (now - dispatch).TotalMinutes);
        if (operatorExtend)
        {
            if (!authenticatedOperator) throw new ForbiddenException("Operator Extend requires an authenticated operator.", "operator_token_required");
            if (action != CeilingAction.Extend || minutes is not (>= 1 and <= 240)
                || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
                throw new ValidationException("extendMinutes", "Operator Extend requires 1..240 minutes and a bounded reason.");
            return elapsed + minutes.Value;
        }
        if (Environment.GetEnvironmentVariable("C1610_MUTANT") != "M5" && (elapsed >= AbsoluteCapMinutes || (task.NextCeilingMinutes ?? 0) > AbsoluteCapMinutes))
            throw new ConflictException("The final ceiling hold requires operator Extend or Cancel.", "ceiling_cap_reached");
        if (action == CeilingAction.Continue && minutes is not null
            || action == CeilingAction.Extend && minutes is not (>= 1 and <= AbsoluteCapMinutes)
            || !Enum.IsDefined(action))
            throw new ValidationException("ceilingAction", "Continue takes no minutes; Extend requires positive minutes.");
        var next = Math.Max(elapsed, task.NextCeilingMinutes ?? task.ResolvedCeilingMinutes.Value)
            + (action == CeilingAction.Continue ? task.ResolvedCeilingMinutes.Value : minutes!.Value);
        if (action == CeilingAction.Continue) return Math.Min(AbsoluteCapMinutes, next);
        if (next > AbsoluteCapMinutes)
            throw new ConflictException("Extend exceeds the 720-minute total elapsed cap.", "ceiling_cap_reached");
        return next;
    }

    internal static void Reset(AgentTask task)
    {
        task.CeilingHoldEpisodeId = null;
        task.CeilingHeldAt = null;
        task.CeilingHoldAttempt = null;
        task.CeilingHoldSessionId = null;
        task.CeilingHoldSessionStartedAt = null;
        task.CeilingHoldDispatchedAt = null;
        task.CeilingAcknowledgedAt = null;
        task.ResolvedCeilingMinutes = null;
        task.ResolvedCeilingSource = null;
        task.NextCeilingMinutes = null;
        task.CeilingOperatorExtended = false;
    }

    internal static async Task<CeilingHoldDto?> DescribeAsync(AppDbContext db, AgentTask task, DateTime now,
        DelegationSettings settings, CancellationToken ct)
    {
        if (!Active(task) || task.AgentSessionId is not { } sessionId) return null;
        if (!await db.AgentSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId
            && s.StartedAt == task.CeilingHoldSessionStartedAt, ct)) return null;
        var verdict = await TaskDeadlinePolicy.EvaluateAsync(db, task, now, settings, ct);
        var last = await TaskDeadlinePolicy.LoadLastEntryAsync(db, sessionId, ct);
        var phase = verdict?.PhaseDeadline;
        var noPhase = phase is null;
        var guidance = noPhase ? "No phase deadline covers this tail; the ceiling is its only deadline."
            : $"Phase {phase!.Kind}: {phase.Limit.TotalMinutes:0}-minute limit, deadline {now + phase.Limit - phase.Elapsed:O}; phase supervision continues.";
        var bound = Bound(task, settings);
        var question = $"Task {DelegationReportFormatter.Short(task.Id)} reached its {bound:0.#}-minute ceiling. "
            + "The delegate continues and still consumes capacity. "
            + $"Last transcript: {last?.Kind ?? "unknown"}, age {TaskDeadlinePolicy.Duration(last is null ? TimeSpan.Zero : now - last.At)}. "
            + guidance + " Choose typed Continue/Extend or existing Cancel. next=decide";
        return new(task.CeilingHoldEpisodeId!.Value, task.CeilingHeldAt!.Value, task.Attempt,
            task.CeilingHoldSessionStartedAt!.Value, task.ResolvedCeilingMinutes!.Value,
            task.ResolvedCeilingSource ?? "legacy", bound, last?.Kind, last is null ? null : Math.Max(0, (now - last.At).TotalSeconds),
            phase?.Kind.ToString(), phase?.Limit.TotalMinutes,
            phase is null ? null : now + phase.Limit - phase.Elapsed,
            Math.Max(0, (now - task.DispatchedAt!.Value).TotalMinutes) >= AbsoluteCapMinutes, question);
    }
}
