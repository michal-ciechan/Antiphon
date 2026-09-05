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

    public OutputDistillationService(
        AppDbContext db,
        OutputDistillerProvisioner provisioner,
        IOptions<DelegationSettings> settings,
        TimeProvider timeProvider,
        ILogger<OutputDistillationService> logger,
        IAlertService? alerts = null,
        SpecialistTaskRunner? runner = null)
    {
        _db = db;
        _provisioner = provisioner;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _runner = runner ?? new SpecialistTaskRunner(db, timeProvider, logger, alerts);
    }

    /// <summary>
    /// True when settlement should post a distill request for this task. Length skips still
    /// post so the ledger records SkippedShort/SkippedLong; disabled never posts.
    /// </summary>
    public static bool ShouldRequest(AgentTask task, DelegationSettings settings)
    {
        if (!settings.OutputDistillerEnabled)
            return false;
        if (task.ReplyTo != AgentTaskReplyTo.Session)
            return false;
        if (AgentTaskRoles.IsSpecialist(task.Role))
            return false;
        return task.Status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed;
    }

    public async Task RequestAsync(Guid taskId, Guid? queuedMessageId, CancellationToken ct)
    {
        var started = _timeProvider.GetUtcNow();

        if (!_settings.OutputDistillerEnabled)
        {
            await ReleaseHoldAsync(queuedMessageId, ct);
            return;
        }

        var source = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (source is null)
        {
            await ReleaseHoldAsync(queuedMessageId, ct);
            return;
        }

        var report = source.Result ?? source.FailureReason ?? "";
        var mode = _settings.OutputDistillerMode;
        var stamp = InstructionBundles.Get(InstructionBundles.OutputDistiller).Stamp;

        if (AgentTaskRoles.IsSpecialist(source.Role)
            || source.Status is not (AgentTaskStatus.Succeeded or AgentTaskStatus.Failed)
            || source.ReplyTo != AgentTaskReplyTo.Session)
        {
            await ReleaseHoldAsync(queuedMessageId, ct);
            return;
        }

        if (report.Length < _settings.DistillMinChars)
        {
            await WriteLedgerAsync(
                source, queuedMessageId, distillTaskId: null, stamp, mode,
                report.Length, 0, 0, 0m, DistillationOutcome.SkippedShort, null, ct);
            await ReleaseHoldAsync(queuedMessageId, ct);
            return;
        }

        if (report.Length > _settings.DistillMaxRawChars)
        {
            await WriteLedgerAsync(
                source, queuedMessageId, distillTaskId: null, stamp, mode,
                report.Length, 0, 0, 0m, DistillationOutcome.SkippedLong, null, ct);
            await ReleaseHoldAsync(queuedMessageId, ct);
            return;
        }

        var digest = DelegationNoteDigest.Compute(report);
        if (!string.IsNullOrEmpty(source.LastPolledResultHash)
            && string.Equals(source.LastPolledResultHash, digest, StringComparison.Ordinal))
        {
            await ReleaseHoldAsync(queuedMessageId, ct);
            return;
        }

        var spec = OutputDistillerProvisioner.Spec(_settings);
        var wait = TimeSpan.FromSeconds(Math.Max(1, _settings.OutputDistillerWaitSeconds));
        var run = await _runner.RunAsync(
            spec,
            OutputDistillation.BuildTitle(source),
            OutputDistillation.BuildGoal(source, report),
            wait,
            _settings.OutputDistillerMaxBacklog,
            _provisioner.EnsureAsync,
            ct,
            createdDetail: $"Distill of task {DelegationReportFormatter.Short(source.Id)}.");

        var outcome = run.Outcome switch
        {
            SpecialistRunOutcome.Busy => DistillationOutcome.DegradedBusy,
            SpecialistRunOutcome.Timeout => DistillationOutcome.DegradedTimeout,
            SpecialistRunOutcome.Failed => DistillationOutcome.DegradedFailed,
            SpecialistRunOutcome.Empty => DistillationOutcome.DegradedEmpty,
            SpecialistRunOutcome.ProvisionFailed or SpecialistRunOutcome.QueueFailed
                or SpecialistRunOutcome.Disabled => DistillationOutcome.DegradedUnavailable,
            SpecialistRunOutcome.Succeeded => DistillationOutcome.Applied,
            _ => DistillationOutcome.DegradedUnavailable,
        };

        var distilled = OutputDistillation.Scrub(run.Result);
        if (outcome != DistillationOutcome.Applied)
        {
            await WriteLedgerAsync(
                source, queuedMessageId, run.RunTaskId, stamp, mode,
                report.Length, distilled.Length, run.WaitMs, run.CostUsd, outcome, null, ct);
            await ReleaseHoldAsync(queuedMessageId, ct);
            return;
        }

        var gate = OutputDistillationGate.Evaluate(
            report, distilled, _settings.DistilledMaxChars, _settings.DistilledMaxRatio);
        if (!gate.Passed)
        {
            await StampDistilledAsync(source, distilled, run, ct);
            await WriteLedgerAsync(
                source, queuedMessageId, run.RunTaskId, stamp, mode,
                report.Length, distilled.Length, run.WaitMs, run.CostUsd,
                gate.ToOutcome(), gate.MissingAnchorsJson, ct);
            await ReleaseHoldAsync(queuedMessageId, ct);
            return;
        }

        await StampDistilledAsync(source, distilled, run, ct);

        if (mode == OutputDistillerMode.Shadow)
        {
            await WriteLedgerAsync(
                source, queuedMessageId, run.RunTaskId, stamp, mode,
                report.Length, distilled.Length, run.WaitMs, run.CostUsd,
                DistillationOutcome.Shadowed, null, ct);
            await ReleaseHoldAsync(queuedMessageId, ct);
            return;
        }

        var applied = await TryApplyAsync(source, queuedMessageId, distilled, digest, ct);
        await WriteLedgerAsync(
            source, queuedMessageId, run.RunTaskId, stamp, mode,
            report.Length, distilled.Length, run.WaitMs, run.CostUsd,
            applied ? DistillationOutcome.Applied : DistillationOutcome.AppliedLate, null, ct);
        if (!applied)
            await ReleaseHoldAsync(queuedMessageId, ct);
    }

    /// <summary>Clear HoldUntil so a held completion note can flush. Safe if the row is gone or already sent.</summary>
    public async Task ReleaseHoldAsync(Guid? queuedMessageId, CancellationToken ct)
    {
        if (queuedMessageId is not Guid id)
            return;
        try
        {
            await _db.SessionQueuedMessages
                .Where(m => m.Id == id && m.HoldUntil != null)
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
                task?.DistilledResult);
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

    private async Task<bool> TryApplyAsync(
        AgentTask source, Guid? queuedMessageId, string distilled, string digest, CancellationToken ct)
    {
        if (queuedMessageId is not Guid id)
            return false;

        var queued = await _db.SessionQueuedMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (queued is null)
            return false;
        if (queued.Status != QueuedMessageStatus.Pending || queued.DeliveryAttempts != 0)
            return false;
        if (!string.IsNullOrEmpty(source.LastPolledResultHash)
            && string.Equals(source.LastPolledResultHash, digest, StringComparison.Ordinal))
            return false;

        var header = string.IsNullOrWhiteSpace(queued.NoteHeader) ? "" : queued.NoteHeader.TrimEnd();
        queued.Body = $"{header}\n\n{distilled.Trim()}\n\n{OutputDistillation.PointerLine(source)}"
            .ReplaceLineEndings("\n");
        queued.HoldUntil = null;
        await _db.SaveChangesAsync(ct);
        return true;
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

    private async Task WriteLedgerAsync(
        AgentTask source,
        Guid? queuedMessageId,
        Guid? distillTaskId,
        string stamp,
        OutputDistillerMode mode,
        int rawChars,
        int distilledChars,
        int waitMs,
        decimal costUsd,
        DistillationOutcome outcome,
        string? missingAnchors,
        CancellationToken ct)
    {
        _db.OutputDistillations.Add(new OutputDistillationRecord
        {
            Id = Guid.NewGuid(),
            TaskId = source.Id,
            DistillTaskId = distillTaskId,
            QueuedMessageId = queuedMessageId,
            BundleStamp = stamp,
            Mode = mode,
            RawChars = rawChars,
            DistilledChars = distilledChars,
            WaitMs = waitMs,
            CostUsd = costUsd,
            Outcome = outcome,
            MissingAnchors = missingAnchors,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
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
