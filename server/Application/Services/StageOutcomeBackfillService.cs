using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One-shot derivation of <see cref="StageOutcome"/> rows from land events since 2026-09-01
/// (CARD-0272 S1). Idempotent: a second run finds each outcome event already stamped and writes
/// nothing. Delegate-run stages are not backfilled — there is no finding marker to read.
/// </summary>
public sealed class StageOutcomeBackfillService : BackgroundService
{
    internal static readonly DateTime LandHistoryStartUtc = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StageOutcomeBackfillService> _logger;

    public StageOutcomeBackfillService(
        IServiceScopeFactory scopeFactory,
        ILogger<StageOutcomeBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var written = await RunAsync(db, ct);
            if (written > 0)
            {
                _logger.LogInformation(
                    "Backfilled {Count} stage-outcome row(s) from land events since {Start:u}",
                    written, LandHistoryStartUtc);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Stage-outcome backfill failed; existing rows left untouched");
        }
    }

    internal static async Task<int> RunAsync(AppDbContext db, CancellationToken ct)
    {
        var events = await db.AgentTaskEvents
            .AsNoTracking()
            .Where(e => e.At >= LandHistoryStartUtc && (
                e.Type == AgentTaskEventType.LandRequested
                || e.Type == AgentTaskEventType.Landed
                || e.Type == AgentTaskEventType.LandRefused
                || e.Type == AgentTaskEventType.LandedWithResidue
                || e.Type == AgentTaskEventType.Conflicted))
            .OrderBy(e => e.At)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);
        if (events.Count == 0)
            return 0;

        var taskIds = events.Select(e => e.AgentTaskId).Distinct().ToList();
        var tasks = await db.AgentTasks
            .AsNoTracking()
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.CardId })
            .ToListAsync(ct);
        var cardByTask = tasks.ToDictionary(t => t.Id, t => t.CardId);

        var merges = await db.AgentTasks
            .AsNoTracking()
            .Where(t => t.Role == AgentTaskRole.Merge && t.ParentTaskId != null && taskIds.Contains(t.ParentTaskId.Value))
            .Select(t => new { t.Id, t.ParentTaskId, t.CostUsd, t.CreatedAt })
            .ToListAsync(ct);
        var mergeByParent = merges
            .GroupBy(m => m.ParentTaskId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.CreatedAt).First());

        var existingRefs = (await db.StageOutcomes
                .AsNoTracking()
                .Where(o => o.Source == StageOutcomeSource.Backfill && o.Ref != null)
                .Select(o => o.Ref!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var serverCovered = (await db.StageOutcomes
                .AsNoTracking()
                .Where(o => o.Source == StageOutcomeSource.Server && o.SubjectTaskId != null)
                .Select(o => new { o.SubjectTaskId, o.RecordedAt })
                .ToListAsync(ct))
            .GroupBy(o => o.SubjectTaskId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RecordedAt).ToList());

        var added = new List<StageOutcome>();
        foreach (var group in events.GroupBy(e => e.AgentTaskId))
        {
            var timeline = group.OrderBy(e => e.At).ThenBy(e => e.Id).ToList();
            foreach (var request in timeline.Where(e => e.Type == AgentTaskEventType.LandRequested))
            {
                var outcome = timeline.FirstOrDefault(e =>
                    e.At >= request.At
                    && e.Id != request.Id
                    && (e.Type == AgentTaskEventType.Landed
                        || e.Type == AgentTaskEventType.LandRefused
                        || e.Type == AgentTaskEventType.LandedWithResidue
                        || e.Type == AgentTaskEventType.Conflicted));
                if (outcome is null)
                    continue;

                var eventRef = outcome.Id.ToString("D");
                if (existingRefs.Contains(eventRef))
                    continue;
                if (serverCovered.TryGetValue(group.Key, out var recorded)
                    && recorded.Any(at => at >= request.At && at <= outcome.At.AddSeconds(2)))
                    continue;

                cardByTask.TryGetValue(group.Key, out var cardId);
                mergeByParent.TryGetValue(group.Key, out var merge);
                var duration = Math.Max(0, (int)Math.Round((outcome.At - request.At).TotalSeconds));
                added.AddRange(RowsFor(group.Key, cardId, outcome, eventRef, duration, merge?.Id, merge?.CostUsd));
                existingRefs.Add(eventRef);
            }
        }

        if (added.Count == 0)
            return 0;

        db.StageOutcomes.AddRange(added);
        await db.SaveChangesAsync(ct);
        return added.Count;
    }

    internal static IReadOnlyList<StageOutcome> RowsFor(
        Guid taskId,
        Guid? cardId,
        AgentTaskEvent outcome,
        string eventRef,
        int durationSeconds,
        Guid? mergeId,
        decimal? mergeCost)
    {
        var detailFlag = "duration=request-to-outcome";
        return outcome.Type switch
        {
            AgentTaskEventType.Conflicted =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Found, durationSeconds,
                    JoinDetail(outcome.Detail, detailFlag), eventRef, outcome.At, mergeId, mergeCost),
            ],
            AgentTaskEventType.Landed when ContainsInsensitive(outcome.Detail, "build skipped") =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Verify, StageOutcomeKind.Skipped, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Cleanup, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
            ],
            AgentTaskEventType.Landed when ContainsInsensitive(outcome.Detail, "build OK") =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Verify, StageOutcomeKind.Clean, durationSeconds,
                    JoinDetail(Head(outcome.Detail, "verify:"), detailFlag), eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Cleanup, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
            ],
            AgentTaskEventType.LandedWithResidue when ContainsInsensitive(outcome.Detail, "build skipped") =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Verify, StageOutcomeKind.Skipped, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Cleanup, StageOutcomeKind.Failed, durationSeconds,
                    JoinDetail(outcome.Detail, detailFlag), eventRef, outcome.At),
            ],
            AgentTaskEventType.LandedWithResidue when ContainsInsensitive(outcome.Detail, "build OK") =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Verify, StageOutcomeKind.Clean, durationSeconds,
                    JoinDetail(Head(outcome.Detail, "verify:"), detailFlag), eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Cleanup, StageOutcomeKind.Failed, durationSeconds,
                    JoinDetail(outcome.Detail, detailFlag), eventRef, outcome.At),
            ],
            AgentTaskEventType.LandedWithResidue =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Verify, StageOutcomeKind.Unreported, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Cleanup, StageOutcomeKind.Failed, durationSeconds,
                    JoinDetail(outcome.Detail, detailFlag), eventRef, outcome.At),
            ],
            AgentTaskEventType.LandRefused when ContainsInsensitive(outcome.Detail, "could not delete") =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Verify, StageOutcomeKind.Unreported, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Cleanup, StageOutcomeKind.Failed, durationSeconds,
                    JoinDetail(outcome.Detail, detailFlag), eventRef, outcome.At),
            ],
            AgentTaskEventType.LandRefused when ContainsInsensitive(outcome.Detail, "build failed")
                || ContainsInsensitive(outcome.Detail, "tests failed") =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Verify, StageOutcomeKind.Failed, durationSeconds,
                    JoinDetail(outcome.Detail, detailFlag), eventRef, outcome.At),
            ],
            AgentTaskEventType.LandRefused when ContainsInsensitive(outcome.Detail, "push")
                && ContainsInsensitive(outcome.Detail, "rejected") =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Verify, StageOutcomeKind.Unreported, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Cleanup, StageOutcomeKind.Failed, durationSeconds,
                    JoinDetail(outcome.Detail, detailFlag), eventRef, outcome.At),
            ],
            AgentTaskEventType.LandRefused =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Failed, durationSeconds,
                    JoinDetail(outcome.Detail, detailFlag), eventRef, outcome.At),
            ],
            AgentTaskEventType.Landed =>
            [
                Row(taskId, cardId, OrchestrationStage.Rebase, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Verify, StageOutcomeKind.Unreported, durationSeconds, detailFlag, eventRef, outcome.At),
                Row(taskId, cardId, OrchestrationStage.Cleanup, StageOutcomeKind.Clean, durationSeconds, detailFlag, eventRef, outcome.At),
            ],
            _ => [],
        };
    }

    private static StageOutcome Row(
        Guid taskId,
        Guid? cardId,
        OrchestrationStage stage,
        StageOutcomeKind outcome,
        int durationSeconds,
        string detail,
        string eventRef,
        DateTime at,
        Guid? resolutionTaskId = null,
        decimal? resolutionCostUsd = null) => new()
    {
        Id = Guid.NewGuid(),
        Stage = stage,
        Outcome = outcome,
        Source = StageOutcomeSource.Backfill,
        SubjectTaskId = taskId,
        CardId = cardId,
        DurationSeconds = durationSeconds,
        ResolutionTaskId = resolutionTaskId,
        ResolutionCostUsd = resolutionCostUsd,
        Detail = Clip(detail),
        Ref = eventRef,
        RecordedAt = at,
    };

    private static bool ContainsInsensitive(string text, string needle) =>
        text.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string JoinDetail(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
            return right;
        return $"{left.Trim()} ({right})";
    }

    private static string Head(string detail, string marker)
    {
        var at = detail.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? detail : detail[at..];
    }

    internal static string Clip(string detail) =>
        detail.Length <= StageOutcome.DetailMaxLength
            ? detail
            : detail[..StageOutcome.DetailMaxLength];
}
