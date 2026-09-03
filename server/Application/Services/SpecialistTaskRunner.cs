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
}

public sealed record SpecialistRun(
    SpecialistRunOutcome Outcome,
    string? Result,
    decimal CostUsd,
    int WaitMs,
    Guid? RunTaskId);

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

    public SpecialistTaskRunner(
        AppDbContext db,
        TimeProvider timeProvider,
        ILogger logger,
        IAlertService? alerts = null)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
        _alerts = alerts;
    }

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
            await RaiseUnavailableAsync(spec, specialist, "the interpretation failed", ct);
            return new SpecialistRun(
                SpecialistRunOutcome.Failed, settled.Result, settled.CostUsd, waitMs, settled.Id);
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

    private int WaitMs(DateTimeOffset started) =>
        (int)Math.Max(0, (_timeProvider.GetUtcNow() - started).TotalMilliseconds);

    private async Task<AgentTask> CreateRunTaskAsync(
        SpecialistSpec spec,
        Agent specialist,
        string title,
        string goal,
        string? createdDetail,
        CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
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
            ModelLevel = AgentModelLevel.Low,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = specialist.WorkingDirectory,
            AgentId = specialist.Id,
            AgentName = specialist.Name,
            Ephemeral = false,
            ReplyTo = AgentTaskReplyTo.None,
            Status = AgentTaskStatus.Queued,
            CreatedAt = now,
        };
        _db.AgentTasks.Add(row);
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.Created,
            ModelLevel = AgentModelLevel.Low,
            Detail = createdDetail ?? $"{spec.DisplayName} run.",
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

    private async Task RaiseUnavailableAsync(
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
