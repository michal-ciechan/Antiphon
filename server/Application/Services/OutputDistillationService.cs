using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Output-distiller worker (CARD-0330 S3). Distils a settled report; Shadow records only,
/// Apply replaces a still-pending completion note after the gates pass.
/// </summary>
public sealed class OutputDistillationService
{
    private readonly AppDbContext _db;
    private readonly OutputDistillerProvisioner _provisioner;
    private readonly DelegationSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutputDistillationService> _logger;
    private readonly SpecialistTaskRunner _runner;
    private readonly SessionMessageQueueService? _messageQueue;
    private readonly SpecialistFailureQueue? _failures;

    public OutputDistillationService(
        AppDbContext db,
        OutputDistillerProvisioner provisioner,
        IOptions<DelegationSettings> settings,
        TimeProvider timeProvider,
        ILogger<OutputDistillationService> logger,
        IAlertService? alerts = null,
        SpecialistTaskRunner? runner = null,
        IModelAvailability? modelAvailability = null,
        SessionMessageQueueService? messageQueue = null,
        SpecialistFailureQueue? failures = null)
    {
        _db = db;
        _provisioner = provisioner;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _runner = runner ?? new SpecialistTaskRunner(db, timeProvider, logger, alerts, modelAvailability);
        _messageQueue = messageQueue;
        _failures = failures;
    }

    /// <summary>
    /// True when settlement should post a distill request for this task. Length skips still
    /// post so the ledger records SkippedShort/SkippedLong; disabled never posts.
    /// </summary>
    public static bool ShouldRequest(AgentTask task, DelegationSettings settings)
    {
        if (!settings.OutputDistillerEnabled)
            return false;
        return AgentReportPolicy.IsTarget(task);
    }

    // Compatibility front door for direct callers: admission is now, not after provisioning.
    public Task RequestAsync(Guid taskId, Guid? queuedMessageId, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        return RequestAsync(new DistillRequest(taskId, queuedMessageId, now,
            now.AddSeconds(Math.Max(1, _settings.OutputDistillerWaitSeconds)), _settings.OutputDistillerMode), ct);
    }

    public Task RejectAdmissionAsync(DistillRequest request, string reason, CancellationToken ct) =>
        RequestCoreAsync(request, reason, ct);

    public Task RequestAsync(DistillRequest request, CancellationToken ct) => RequestCoreAsync(request, null, ct);

    private async Task RequestCoreAsync(DistillRequest request, string? admissionFailure, CancellationToken ct)
    {
        var dequeued = _timeProvider.GetUtcNow();
        AgentTask? source = null;
        SpecialistRun? run = null;
        var distilled = "";
        string? missing = null;
        var reason = admissionFailure;
        string? expiryPhase = null;
        var outcome = DistillationOutcome.DegradedUnavailable;
        var writeLedger = true;
        // Expired/failed admission still gets bounded bookkeeping and preserves length skips.
        var initialDeadline = request.DeadlineAt > dequeued && admissionFailure is null
            ? request.DeadlineAt : dequeued.AddSeconds(2);
        using var execution = new ExecutionBudget(initialDeadline, _timeProvider, ct);
        try
        {
            var token = execution.Token;
            source = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == request.TaskId, token);
            if (source is null || !ShouldRequest(source, _settings)) { writeLedger = false; return; }
            var report = source.Result ?? source.FailureReason ?? "";
            if (report.Length < _settings.DistillMinChars) { outcome = DistillationOutcome.SkippedShort; return; }
            if (report.Length > _settings.DistillMaxRawChars) { outcome = DistillationOutcome.SkippedLong; return; }
            var digest = DelegationNoteDigest.Compute(report);
            if (source.LastPolledResultHash == digest) { writeLedger = false; return; }
            if (admissionFailure is not null) { outcome = DistillationOutcome.DegradedBusy; return; }
            if (_timeProvider.GetUtcNow() >= request.DeadlineAt)
            { outcome = DistillationOutcome.DegradedExpired; expiryPhase = "request-queue"; reason = "deadline"; return; }

            run = await _runner.RunWithPolicyAsync(OutputDistillerProvisioner.Spec(_settings),
                OutputDistillation.BuildTitle(source), OutputDistillation.BuildGoal(source, report),
                _settings.OutputDistillerMaxBacklog, _provisioner.EnsureAsync,
                new SpecialistExecutionPolicy(request.DeadlineAt), ct);
            outcome = run.Outcome switch
            {
                SpecialistRunOutcome.Held => DistillationOutcome.DegradedHeld,
                SpecialistRunOutcome.Expired => DistillationOutcome.DegradedExpired,
                SpecialistRunOutcome.Timeout => DistillationOutcome.DegradedTimeout,
                SpecialistRunOutcome.Busy => DistillationOutcome.DegradedBusy,
                SpecialistRunOutcome.Failed => DistillationOutcome.DegradedFailed,
                SpecialistRunOutcome.Empty => DistillationOutcome.DegradedEmpty,
                SpecialistRunOutcome.Succeeded => DistillationOutcome.Applied,
                _ => DistillationOutcome.DegradedUnavailable,
            };
            reason = run.Reason;
            expiryPhase = run.ExpiryPhase;
            if (outcome != DistillationOutcome.Applied) return;
            if (_timeProvider.GetUtcNow() >= request.DeadlineAt)
            { outcome = DistillationOutcome.DegradedTimeout; expiryPhase = "await-settlement"; reason = "deadline"; return; }
            distilled = OutputDistillation.Scrub(run.Result);
            var gate = OutputDistillationGate.Evaluate(report, distilled,
                _settings.DistilledMaxChars, _settings.DistilledMaxRatio);
            await StampDistilledAsync(source, distilled, run, token);
            if (!gate.Passed) { outcome = gate.ToOutcome(); missing = gate.MissingAnchorsJson; return; }
            if (request.Mode == OutputDistillerMode.Shadow) { outcome = DistillationOutcome.Shadowed; return; }
            expiryPhase = "apply";
            var applied = _messageQueue is not null
                ? await _messageQueue.TryApplyDistillationAsync(request, digest, distilled, ct)
                : "queue-unavailable";
            outcome = applied is null ? DistillationOutcome.Applied : DistillationOutcome.AppliedLate;
            reason = applied;
            if (applied != "deadline") expiryPhase = null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && execution.Token.IsCancellationRequested)
        {
            outcome = run?.Outcome == SpecialistRunOutcome.Succeeded
                ? DistillationOutcome.AppliedLate : DistillationOutcome.DegradedExpired;
            reason = "deadline";
            expiryPhase ??= run is null ? "request-queue" : "await-settlement";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Distillation failed for {TaskId}; raw note stands", request.TaskId);
            outcome = DistillationOutcome.DegradedUnavailable;
            reason = "request-failed";
        }
        finally
        {
            // Shutdown propagates; never leave a background DbContext operation unobserved.
            if (!ct.IsCancellationRequested)
            {
                var decision = _timeProvider.GetUtcNow();
                using var cleanupPhase = new RuntimePhase(_logger, _timeProvider,
                    source?.ParentSessionId ?? Guid.Empty, "distiller.cleanup", request.TaskId);
                _db.ChangeTracker.Clear();
                var cleanupDeadline = request.DeadlineAt <= dequeued || admissionFailure is not null
                    ? dequeued.AddSeconds(2) : decision.AddSeconds(2);
                using var cleanup = new ExecutionBudget(cleanupDeadline, _timeProvider, ct);
                try
                {
                    if (run?.RunTaskId is Guid runId && outcome is DistillationOutcome.DegradedExpired
                        or DistillationOutcome.DegradedTimeout or DistillationOutcome.DegradedHeld
                        or DistillationOutcome.DegradedUnavailable)
                    {
                        var canceled = await _runner.CancelQueuedAsync(runId,
                            "Optional work expired before execution.", cleanup.Token);
                        if (!canceled && outcome is (DistillationOutcome.DegradedExpired or DistillationOutcome.DegradedTimeout))
                        {
                            var current = await _db.AgentTasks.AsNoTracking().Where(t => t.Id == runId)
                                .Select(t => new { t.Status, t.DispatchedAt, t.FailureReason }).SingleOrDefaultAsync(cleanup.Token);
                            if (current?.Status == AgentTaskStatus.Canceled
                                && current.FailureReason == "Optional work expired before execution.")
                            { outcome = DistillationOutcome.DegradedExpired; expiryPhase = "dispatch-queue"; }
                            else if (outcome == DistillationOutcome.DegradedExpired && current?.DispatchedAt is not null)
                            { outcome = DistillationOutcome.DegradedTimeout; expiryPhase = "await-settlement"; }
                        }
                    }
                    await ReleaseHoldAsync(request.QueuedMessageId, cleanup.Token);
                    if (writeLedger && source is not null)
                    {
                        _db.OutputDistillations.Add(new OutputDistillationRecord
                        {
                            Id = Guid.NewGuid(), TaskId = source.Id, DistillTaskId = run?.RunTaskId,
                            QueuedMessageId = request.QueuedMessageId,
                            BundleStamp = InstructionBundles.Get(InstructionBundles.OutputDistiller).Stamp,
                            Mode = request.Mode, RawChars = (source.Result ?? source.FailureReason ?? "").Length,
                            DistilledChars = distilled.Length, WaitMs = run?.WaitMs ?? 0,
                            CostUsd = run?.CostUsd ?? 0, Outcome = outcome, MissingAnchors = missing,
                            CreatedAt = decision.UtcDateTime, RequestedAt = request.RequestedAt.UtcDateTime,
                            DeadlineAt = request.DeadlineAt.UtcDateTime, DequeuedAt = dequeued.UtcDateTime,
                            RunCreatedAt = run?.RunCreatedAt, DecisionAt = decision.UtcDateTime,
                            QueueWaitMs = (int)Math.Max(0, (dequeued - request.RequestedAt).TotalMilliseconds),
                            SpecialistWaitMs = run?.WaitMs,
                            CleanupMs = (int)Math.Max(0, (_timeProvider.GetUtcNow() - decision).TotalMilliseconds),
                            Reason = reason, ExpiryPhase = expiryPhase, AvailabilityAlias = run?.AvailabilityAlias,
                            AvailabilityKind = run?.AvailabilityKind, AvailabilityObservedAt = run?.AvailabilityObservedAt,
                        });
                        await _db.SaveChangesAsync(cleanup.Token);
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Distiller cleanup incomplete for {TaskId}; finite hold and dispatch expiry remain authoritative", request.TaskId);
                }
                if (writeLedger && source is not null && outcome is (DistillationOutcome.DegradedTimeout
                    or DistillationOutcome.DegradedFailed or DistillationOutcome.DegradedUnavailable))
                    _failures?.TryEnqueue(new SpecialistFailure(OutputDistillerProvisioner.Spec(_settings), reason ?? outcome.ToString()));
            }
        }
    }

    /// <summary>Clear HoldUntil so a held completion note can flush. Safe if the row is gone or already sent.</summary>
    public async Task ReleaseHoldAsync(Guid? queuedMessageId, CancellationToken ct)
    {
        if (queuedMessageId is not Guid id)
            return;
        try
        {
            await _db.SessionQueuedMessages
                .Where(m => m.Id == id && m.HoldUntil != null && m.SourceLandNotificationId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.HoldUntil, (DateTime?)null), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not clear HoldUntil on queued message {QueuedId}", id);
        }
    }

    public async Task RecordFeedbackAsync(
        Guid taskId, DistillationFeedback verdict, string? note, string? by, CancellationToken ct)
    {
        var row = await _db.OutputDistillations
            .Where(d => d.TaskId == taskId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new ConflictException("This task has no distillation to flag.");

        row.Feedback = verdict;
        row.FeedbackNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        row.FeedbackBy = string.IsNullOrWhiteSpace(by) ? null : by.Trim();
        row.FeedbackAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DistillationDto>> ListAsync(
        DateTime? since,
        DistillationOutcome? outcome,
        DistillationFeedback? feedback,
        int? limit,
        CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = _db.OutputDistillations.AsNoTracking().AsQueryable();
        if (since is DateTime sinceUtc)
        {
            var start = DateTime.SpecifyKind(sinceUtc.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(d => d.CreatedAt >= start);
        }

        if (outcome is DistillationOutcome o)
            query = query.Where(d => d.Outcome == o);
        if (feedback is DistillationFeedback f)
            query = query.Where(d => d.Feedback == f);

        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        var taskIds = rows.Select(d => d.TaskId).Distinct().ToList();
        var tasks = taskIds.Count == 0
            ? new Dictionary<Guid, AgentTask>()
            : await _db.AgentTasks.AsNoTracking()
                .Where(t => taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, ct);

        return rows.Select(d =>
        {
            tasks.TryGetValue(d.TaskId, out var task);
            return new DistillationDto(
                d.Id,
                d.TaskId,
                DelegationReportFormatter.Short(d.TaskId),
                d.DistillTaskId,
                d.QueuedMessageId,
                d.BundleStamp,
                d.Mode,
                d.RawChars,
                d.DistilledChars,
                d.WaitMs,
                d.CostUsd,
                d.Outcome,
                d.MissingAnchors,
                d.CreatedAt,
                d.Feedback,
                d.FeedbackNote,
                d.FeedbackBy,
                d.FeedbackAt,
                d.FullReadAt,
                task?.Result,
                task?.DistilledResult)
            {
                RequestedAt = d.RequestedAt, DeadlineAt = d.DeadlineAt, DequeuedAt = d.DequeuedAt,
                RunCreatedAt = d.RunCreatedAt, DecisionAt = d.DecisionAt, QueueWaitMs = d.QueueWaitMs,
                SpecialistWaitMs = d.SpecialistWaitMs, CleanupMs = d.CleanupMs, Reason = d.Reason,
                ExpiryPhase = d.ExpiryPhase, AvailabilityAlias = d.AvailabilityAlias,
                AvailabilityKind = d.AvailabilityKind, AvailabilityObservedAt = d.AvailabilityObservedAt,
            };
        }).ToList();
    }

    public async Task<DistillationStatsDto> StatsAsync(DateTime? since, CancellationToken ct)
    {
        var query = _db.OutputDistillations.AsNoTracking().AsQueryable();
        DateTime? start = null;
        if (since is DateTime sinceUtc)
        {
            start = DateTime.SpecifyKind(sinceUtc.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(d => d.CreatedAt >= start);
        }

        var rows = await query.ToListAsync(ct);
        var byOutcome = rows
            .GroupBy(d => d.Outcome.ToString())
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var byFeedback = rows
            .GroupBy(d => d.Feedback.ToString())
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var byStamp = rows
            .Where(d => !string.IsNullOrWhiteSpace(d.BundleStamp))
            .GroupBy(d => d.BundleStamp!)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var ratios = rows
            .Where(d => d.RawChars > 0 && d.DistilledChars > 0)
            .Select(d => (double)d.DistilledChars / d.RawChars)
            .OrderBy(r => r)
            .ToList();
        double? median = ratios.Count == 0 ? null : Percentile(ratios, 0.5);
        double? p90 = ratios.Count == 0 ? null : Percentile(ratios, 0.9);

        var missingClasses = rows
            .Where(d => !string.IsNullOrWhiteSpace(d.MissingAnchors))
            .SelectMany(d => ParseMissingClasses(d.MissingAnchors!))
            .GroupBy(c => c, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Take(12)
            .Select(g => $"{g.Key}:{g.Count()}")
            .ToList();

        var applied = rows.Where(d => d.Outcome == DistillationOutcome.Applied).ToList();
        double? fullReadRate = applied.Count == 0
            ? null
            : (double)applied.Count(d => d.FullReadAt is not null) / applied.Count;

        return new DistillationStatsDto(
            start,
            rows.Count,
            byOutcome,
            byFeedback,
            byStamp,
            median,
            p90,
            missingClasses,
            fullReadRate,
            rows.Sum(d => d.CostUsd));
    }

    public async Task MarkFullReadAsync(Guid taskId, DateTime now, CancellationToken ct)
    {
        await _db.OutputDistillations
            .Where(d => d.TaskId == taskId
                && d.Outcome == DistillationOutcome.Applied
                && d.FullReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.FullReadAt, now), ct);
    }

    private async Task StampDistilledAsync(
        AgentTask source, string distilled, SpecialistRun run, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        source.DistilledResult = distilled;
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = source.Id,
            Type = AgentTaskEventType.Distilled,
            ModelLevel = AgentModelLevel.Low,
            Detail = run.CostUsd > 0
                ? $"distiller: task {DelegationReportFormatter.Short(run.RunTaskId ?? Guid.Empty)} ${run.CostUsd:0.0000}"
                : $"distiller: task {DelegationReportFormatter.Short(run.RunTaskId ?? Guid.Empty)}",
            At = now,
        });
        await _db.SaveChangesAsync(ct);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 1)
            return sorted[0];
        var idx = (sorted.Count - 1) * p;
        var lo = (int)Math.Floor(idx);
        var hi = (int)Math.Ceiling(idx);
        if (lo == hi)
            return sorted[lo];
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
    }

    private static IEnumerable<string> ParseMissingClasses(string json)
    {
        string[] items;
        try
        {
            items = System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }

        return items.Select(item =>
        {
            var colon = item.IndexOf(':');
            return colon <= 0 ? item : item[..colon];
        });
    }
}
