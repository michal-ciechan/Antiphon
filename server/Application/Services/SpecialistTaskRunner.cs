using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public enum SpecialistRunOutcome
{
    Disabled = 0,
    Busy = 1,
    Succeeded = 2,
    Timeout = 3,
    Failed = 4,
    Empty = 5,
    ProvisionFailed = 6,
    QueueFailed = 7,
    Held = 8,
    Expired = 9,
    AvailabilityFailed = 10,
    IdentityMismatch = 11,
}

public sealed record SpecialistRun(
    SpecialistRunOutcome Outcome,
    string? Result,
    decimal CostUsd,
    int WaitMs,
    Guid? RunTaskId,
    string? Reason = null,
    string? ExpiryPhase = null,
    string? AvailabilityAlias = null,
    AgentKind? AvailabilityKind = null,
    DateTime? AvailabilityObservedAt = null,
    DateTime? RunCreatedAt = null,
    Guid? RequestId = null);

/// <summary>Explicit opt-in; Check and Diagnose retain the original RunAsync contract.</summary>
public sealed record SpecialistExecutionPolicy(DateTimeOffset DeadlineAt);

/// <summary>
/// Ensure → backlog gate → pinned Low-tier row → poll until settled → cancel-if-still-Queued
/// → per-minute-deduped unavailable incident (CARD-0352 S1). Extracted from
/// <c>AgentTaskCheckService.InterpretAsync</c>; Check / Distill / Diagnose each pass a spec.
/// </summary>
public sealed class SpecialistTaskRunner
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan UnavailableDedupWindow = TimeSpan.FromMinutes(1);

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly IAlertService? _alerts;
    private readonly IModelAvailability? _availability;

    public SpecialistTaskRunner(
        AppDbContext db,
        TimeProvider timeProvider,
        ILogger logger,
        IAlertService? alerts = null,
        IModelAvailability? modelAvailability = null)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
        _alerts = alerts;
        _availability = modelAvailability;
    }

    public async Task<SpecialistRun> RunWithPolicyAsync(
        SpecialistSpec spec, string title, string goal, int maxBacklog,
        Func<CancellationToken, Task<Agent?>> ensure, SpecialistExecutionPolicy policy,
        CancellationToken ct)
    {
        var started = _timeProvider.GetTimestamp();
        using var budget = new ExecutionBudget(policy.DeadlineAt, _timeProvider, ct);
        var token = budget.Token;
        AgentTask? run = null;
        string? alias = null;
        AgentKind? kind = null;
        DateTime? observed = null;
        var phase = "provision";
        var began = false;

        SpecialistRun Result(SpecialistRunOutcome outcome, string? reason = null) =>
            new(outcome, null, 0m, (int)Math.Max(0, _timeProvider.GetElapsedTime(started).TotalMilliseconds), run?.Id, reason,
                outcome is SpecialistRunOutcome.Expired or SpecialistRunOutcome.Timeout ? phase : null,
                alias, kind, observed, run?.CreatedAt);

        async Task<bool> Held(Agent? seat, AgentKind? executionKind)
        {
            kind = executionKind ?? seat?.Kind ?? AgentKind.ClaudeCode;
            alias = run?.SpecialistModelAlias
                ?? DispatchModelAlias.Resolve(kind.Value, seat?.ModelLevel ?? AgentModelLevel.Low, seat?.ModelId);
            if (_availability is null)
                throw new InvalidOperationException("Model availability reader is not registered.");
            var held = await _availability.IsHeldAsync(kind.Value, alias, token);
            token.ThrowIfCancellationRequested();
            observed = _timeProvider.GetUtcNow().UtcDateTime;
            return held;
        }

        async Task<SpecialistRun?> CheckHold(Agent? seat, AgentKind? executionKind = null)
        {
            try { return await Held(seat, executionKind) ? Result(SpecialistRunOutcome.Held, "model-held") : null; }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Distiller availability check failed for {Kind}/{Alias}", kind, alias);
                return Result(SpecialistRunOutcome.AvailabilityFailed, "availability-check");
            }
        }

        try
        {
            token.ThrowIfCancellationRequested();
            var seat = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Slug == spec.Slug, token);
            var held = await CheckHold(seat);
            if (held is not null) return held;
            seat = await ensure(token);
            token.ThrowIfCancellationRequested();
            if (_timeProvider.GetUtcNow() >= policy.DeadlineAt) return Result(SpecialistRunOutcome.Expired, "deadline");
            if (seat is null) return Result(SpecialistRunOutcome.Disabled, "disabled");
            held = await CheckHold(seat);
            if (held is not null) return held;
            var backlog = await _db.AgentTasks.CountAsync(t => t.AgentId == seat.Id && t.Role == spec.Role
                && (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Dispatched
                    || t.Status == AgentTaskStatus.Working), token);
            token.ThrowIfCancellationRequested();
            if (backlog >= Math.Max(1, maxBacklog)) return Result(SpecialistRunOutcome.Busy, "run-backlog");
            run = await CreateRunTaskAsync(spec, seat, title, goal, null, token, policy.DeadlineAt.UtcDateTime);
            phase = "dispatch-queue";
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var row = await _db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == run.Id, token);
                if (row is null) return Result(SpecialistRunOutcome.Failed, "run-missing");
                began |= row.DispatchedAt is not null || row.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working;
                phase = began ? "await-settlement" : "dispatch-queue";
                // A terminal row observed late is still late, regardless of producer timestamps.
                if (_timeProvider.GetUtcNow() >= policy.DeadlineAt)
                    return Result(began ? SpecialistRunOutcome.Timeout : SpecialistRunOutcome.Expired, "deadline");
                if (AgentTaskService.IsSettled(row.Status) || row.Status == AgentTaskStatus.Blocked)
                {
                    var outcome = row.Status is AgentTaskStatus.Failed or AgentTaskStatus.Canceled
                        ? (row.FailureCode == AgentTaskFailureCode.SpecialistIdentityMismatch
                            ? SpecialistRunOutcome.IdentityMismatch : row.FailureReason == "Optional work expired before execution."
                            ? SpecialistRunOutcome.Expired : SpecialistRunOutcome.Failed)
                        : string.IsNullOrWhiteSpace(row.Result) ? SpecialistRunOutcome.Empty : SpecialistRunOutcome.Succeeded;
                    return Result(outcome, outcome == SpecialistRunOutcome.Expired ? "deadline" : FailureDetail(row))
                        with { Result = row.Result, CostUsd = row.CostUsd };
                }
                if (row.Status == AgentTaskStatus.Queued)
                {
                    seat = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == row.AgentId, token);
                    held = await CheckHold(seat, row.AgentKind);
                    if (held is not null)
                    {
                        // Dispatch may have won during the availability read. Never cancel active work.
                        var canceled = await CancelQueuedAsync(row.Id, held.Reason!, token);
                        if (canceled) return held;
                        var current = await _db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == row.Id, token);
                        began |= current?.DispatchedAt is not null;
                        if (began) phase = "await-settlement";
                    }
                }
                var remaining = policy.DeadlineAt - _timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                    return Result(began ? SpecialistRunOutcome.Timeout : SpecialistRunOutcome.Expired, "deadline");
                using (new RuntimePhase(_logger, _timeProvider, row.AgentSessionId ?? Guid.Empty,
                    "specialist.poll-wait", row.Id))
                    await Task.Delay(remaining < PollInterval ? remaining : PollInterval, _timeProvider, token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && token.IsCancellationRequested)
        {
            return Result(began ? SpecialistRunOutcome.Timeout : SpecialistRunOutcome.Expired, "deadline");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Distiller request failed in {Phase}", phase);
            return Result(run is null ? SpecialistRunOutcome.ProvisionFailed : SpecialistRunOutcome.Failed, phase);
        }
    }

    public async Task<bool> CancelQueuedAsync(Guid runId, string reason, CancellationToken ct) =>
        await _db.AgentTasks.Where(t => t.Id == runId && t.Status == AgentTaskStatus.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled)
                .SetProperty(t => t.CompletedAt, _timeProvider.GetUtcNow().UtcDateTime)
                .SetProperty(t => t.FailureReason, reason)
                .SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()), ct) > 0;

    public static string UnavailableDedupKey(AgentIncidentKind kind, Guid agentId) =>
        $"delegation:{kind}:{agentId}";

    /// <param name="ensure">
    /// The seat's facade <c>EnsureAsync</c>. Passing the facade (not the inner provisioner)
    /// preserves feature-switch and throw-on-dead-context behaviour.
    /// </param>
    public async Task<SpecialistRun> RunAsync(
        SpecialistSpec spec,
        string title,
        string goal,
        TimeSpan waitBudget,
        int maxBacklog,
        Func<CancellationToken, Task<Agent?>> ensure,
        CancellationToken ct,
        string? createdDetail = null)
    {
        var started = _timeProvider.GetUtcNow();

        Agent? specialist;
        try
        {
            specialist = await ensure(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not provision the {DisplayName}", spec.DisplayName);
            await RaiseUnavailableAsync(spec, specialist: null, "could not be provisioned", ct);
            return Finish(SpecialistRunOutcome.ProvisionFailed, started);
        }

        if (specialist is null)
            return Finish(SpecialistRunOutcome.Disabled, started);

        var backlog = await _db.AgentTasks.CountAsync(
            t => t.AgentId == specialist.Id
                && t.Role == spec.Role
                && (t.Status == AgentTaskStatus.Queued
                    || t.Status == AgentTaskStatus.Dispatched
                    || t.Status == AgentTaskStatus.Working),
            ct);
        if (backlog >= Math.Max(1, maxBacklog))
        {
            _logger.LogInformation(
                "{DisplayName} degraded: {Backlog} run(s) already pending",
                spec.DisplayName, backlog);
            return Finish(SpecialistRunOutcome.Busy, started);
        }

        AgentTask run;
        try
        {
            run = await CreateRunTaskAsync(spec, specialist, title, goal, createdDetail, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not queue a {DisplayName} run", spec.DisplayName);
            await RaiseUnavailableAsync(
                spec, specialist, "the interpretation could not be queued", ct);
            return Finish(SpecialistRunOutcome.QueueFailed, started);
        }

        var settled = await WaitForRunAsync(run.Id, waitBudget, ct);
        if (settled is null)
        {
            await CancelIfStillQueuedAsync(run.Id, ct);
            var timeoutReason = $"no reading within {(int)waitBudget.TotalSeconds}s";
            await RaiseUnavailableAsync(spec, specialist, timeoutReason, ct);
            return Finish(SpecialistRunOutcome.Timeout, started, runTaskId: run.Id);
        }

        var waitMs = WaitMs(started);
        if (settled.Status is AgentTaskStatus.Failed or AgentTaskStatus.Canceled)
        {
            var reason = FailureDetail(settled) ?? "the interpretation failed";
            await RaiseUnavailableAsync(spec, specialist, reason, ct);
            return new SpecialistRun(
                settled.FailureCode == AgentTaskFailureCode.SpecialistIdentityMismatch
                    ? SpecialistRunOutcome.IdentityMismatch : SpecialistRunOutcome.Failed,
                settled.Result, settled.CostUsd, waitMs, settled.Id, reason);
        }

        if (string.IsNullOrWhiteSpace(settled.Result))
        {
            await RaiseUnavailableAsync(spec, specialist, "the interpretation was empty", ct);
            return new SpecialistRun(
                SpecialistRunOutcome.Empty, settled.Result, settled.CostUsd, waitMs, settled.Id);
        }

        return new SpecialistRun(
            SpecialistRunOutcome.Succeeded, settled.Result, settled.CostUsd, waitMs, settled.Id);
    }

    private SpecialistRun Finish(
        SpecialistRunOutcome outcome, DateTimeOffset started, Guid? runTaskId = null) =>
        new(outcome, null, 0m, WaitMs(started), runTaskId);

    private static string? FailureDetail(AgentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.FailureReason)) return null;
        var reason = AgentTaskCheckService.ScrubTaskMarkers(task.FailureReason).ReplaceLineEndings(" ").Trim();
        return reason[..Math.Min(reason.Length, 800)];
    }

    private int WaitMs(DateTimeOffset started) =>
        (int)Math.Max(0, (_timeProvider.GetUtcNow() - started).TotalMilliseconds);

    private async Task<AgentTask> CreateRunTaskAsync(
        SpecialistSpec spec,
        Agent specialist,
        string title,
        string goal,
        string? createdDetail,
        CancellationToken ct, DateTime? executionDeadlineAt = null)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        var selectedSession = Guid.TryParse(specialist.PersistentSessionId, out var sessionId)
            ? await _db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            : null;
        var row = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentTaskId = null,
            ParentSessionId = null,
            Depth = 0,
            Title = title,
            Goal = goal,
            Kind = AgentTaskKind.Worker,
            Role = spec.Role,
            AgentKind = specialist.Kind,
            ModelLevel = specialist.ModelLevel,
            SpecialistModelAlias = DispatchModelAlias.Resolve(specialist.Kind, specialist.ModelLevel, specialist.ModelId),
            SpecialistModelId = specialist.ModelId,
            SpecialistEffectiveModelId = selectedSession?.EffectiveModelId,
            SpecialistSessionId = selectedSession?.Id,
            SpecialistSessionStartedAt = selectedSession?.StartedAt,
            SpecialistProfileRevisionId = selectedSession?.TuiProfileRevisionId,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = specialist.WorkingDirectory,
            AgentId = specialist.Id,
            AgentName = specialist.Name,
            Ephemeral = false,
            ReplyTo = AgentTaskReplyTo.None,
            Status = AgentTaskStatus.Queued,
            CreatedAt = now,
            ExecutionDeadlineAt = executionDeadlineAt,
        };
        _db.AgentTasks.Add(row);
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.Created,
            ModelLevel = specialist.ModelLevel,
            Detail = (createdDetail ?? $"{spec.DisplayName} run.")
                + $" Execution: {row.AgentKind}/{row.ModelLevel}/{row.SpecialistModelAlias}."
                + $" Session: {row.SpecialistSessionId:D} at {row.SpecialistSessionStartedAt:O}.",
            At = now,
        });
        await _db.SaveChangesAsync(ct);
        return row;
    }

    private async Task<AgentTask?> WaitForRunAsync(
        Guid runId, TimeSpan waitBudget, CancellationToken ct)
    {
        var deadline = _timeProvider.GetUtcNow() + (waitBudget < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1)
            : waitBudget);

        while (true)
        {
            var row = await _db.AgentTasks.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == runId, ct);
            if (row is null)
                return null;
            if (AgentTaskService.IsSettled(row.Status) || row.Status == AgentTaskStatus.Blocked)
                return row;

            var remaining = deadline - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                return null;

            await Task.Delay(remaining < PollInterval ? remaining : PollInterval, _timeProvider, ct);
        }
    }

    private async Task CancelIfStillQueuedAsync(Guid runId, CancellationToken ct)
    {
        try
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var rows = await _db.AgentTasks
                .Where(t => t.Id == runId && t.Status == AgentTaskStatus.Queued)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled)
                          .SetProperty(t => t.CompletedAt, now)
                          .SetProperty(t => t.FailureReason, "The caller that asked for it stopped waiting.")
                          .SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()),
                    ct);
            if (rows > 0)
            {
                _logger.LogDebug(
                    "Specialist run {ShortId} cancelled — it never left the queue",
                    DelegationReportFormatter.Short(runId));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not cancel the timed-out specialist run {ShortId}",
                DelegationReportFormatter.Short(runId));
        }
    }

    internal async Task RaiseUnavailableAsync(
        SpecialistSpec spec, Agent? specialist, string reason, CancellationToken ct)
    {
        try
        {
            var agent = specialist;
            if (agent is null)
            {
                agent = await _db.Agents.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Slug == spec.Slug, ct);
            }

            if (agent is null)
                return;

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var windowStart = now - UnavailableDedupWindow;
            var kind = spec.UnavailableIncidentKind;
            var already = await _db.AgentIncidents.AnyAsync(
                i => i.AgentId == agent.Id
                    && i.Kind == kind
                    && i.CreatedAt >= windowStart,
                ct);
            if (already)
                return;

            Guid? sessionId = Guid.TryParse(agent.PersistentSessionId, out var parsed)
                ? parsed
                : null;
            var message =
                $"{spec.DisplayName} '{agent.Slug}' could not complete a run ({reason}).";

            _db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agent.Id,
                SessionId = sessionId,
                Kind = kind,
                Severity = AlertSeverity.Warning,
                Message = message,
                CreatedAt = now,
            });
            await _db.SaveChangesAsync(ct);

            if (_alerts is null)
                return;

            await _alerts.RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Warning,
                    Source: "delegation",
                    Title: $"{spec.DisplayName} unavailable ({agent.Slug})",
                    Detail: message,
                    DedupKey: UnavailableDedupKey(kind, agent.Id),
                    AgentId: agent.Id,
                    SessionId: sessionId),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not record a {DisplayName}-unavailable incident", spec.DisplayName);
        }
    }
}
