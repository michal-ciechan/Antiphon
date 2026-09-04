using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Read model, Merge-cost attachment, and orchestrator finding override for
/// <see cref="StageOutcome"/> (CARD-0272). Live land rows live on
/// <see cref="AgentTaskLandService"/>; live delegate rows live on
/// <see cref="AgentTaskReplyService"/>.
/// </summary>
public sealed class StageOutcomeService
{
    private readonly AppDbContext _db;

    public StageOutcomeService(AppDbContext db) => _db = db;

    public async Task<StageOutcomeListDto> ListAsync(
        DateTime? since,
        DateTime? until,
        string? stage,
        Guid? cardId,
        bool latestOnly,
        CancellationToken ct)
    {
        var parsedStage = ParseStage(stage);
        var query = _db.StageOutcomes.AsNoTracking().AsQueryable();
        if (since is not null)
            query = query.Where(o => o.RecordedAt >= since);
        if (until is not null)
            query = query.Where(o => o.RecordedAt <= until);
        if (parsedStage is not null)
            query = query.Where(o => o.Stage == parsedStage);
        if (cardId is not null)
            query = query.Where(o => o.CardId == cardId);

        var rows = await query.OrderBy(o => o.RecordedAt).ThenBy(o => o.Id).ToListAsync(ct);
        if (latestOnly)
            rows = LatestPerTaskStage(rows).ToList();

        return new StageOutcomeListDto(rows.Select(ToDto).ToList(), Summarise(rows));
    }

    /// <summary>
    /// Hit rate = Found / (Found + Clean). Skipped, Failed and Unreported stay in the run count
    /// so a stage whose Unreported share is high is visibly under-measured.
    /// </summary>
    public static IReadOnlyList<StageOutcomeSummaryRowDto> Summarise(IReadOnlyList<StageOutcome> rows)
    {
        return rows
            .GroupBy(o => o.Stage)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var found = g.Count(o => o.Outcome == StageOutcomeKind.Found);
                var clean = g.Count(o => o.Outcome == StageOutcomeKind.Clean);
                var skipped = g.Count(o => o.Outcome == StageOutcomeKind.Skipped);
                var failed = g.Count(o => o.Outcome == StageOutcomeKind.Failed);
                var unreported = g.Count(o => o.Outcome == StageOutcomeKind.Unreported);
                var denom = found + clean;
                decimal? hit = denom == 0 ? null : decimal.Round(100m * found / denom, 1);
                var usd = g.Sum(o => o.CostUsd ?? 0m) + g.Sum(o => o.ResolutionCostUsd ?? 0m);
                decimal? perFinding = found == 0 ? null : decimal.Round(usd / found, 2);
                var serverSecs = g
                    .Where(o => o.Source is StageOutcomeSource.Server or StageOutcomeSource.Backfill)
                    .Sum(o => o.DurationSeconds);
                return new StageOutcomeSummaryRowDto(
                    g.Key, g.Count(), found, clean, skipped, failed, unreported,
                    hit, decimal.Round(usd, 2), perFinding, serverSecs);
            })
            .ToList();
    }

    /// <summary>
    /// Latest row per (task, stage). Task is the subject when present, else the stage task — the
    /// same grain an orchestrator override supersedes.
    /// </summary>
    public static IReadOnlyList<StageOutcome> LatestPerTaskStage(IReadOnlyList<StageOutcome> rows) =>
        rows
            .GroupBy(o => (o.SubjectTaskId ?? o.StageTaskId, o.Stage))
            .Select(g => g.OrderByDescending(o => o.RecordedAt).ThenByDescending(o => o.Id).First())
            .OrderBy(o => o.RecordedAt)
            .ThenBy(o => o.Id)
            .ToList();

    /// <summary>
    /// A Merge delegate finishing is the cost of the Rebase finding it resolved. No row → nothing
    /// to attach (a hand-dispatched Merge with no parent finding is an ordinary delegate row, S2).
    /// Must run on the same <see cref="AppDbContext"/> as the settlement SaveChanges.
    /// </summary>
    public Task AttachMergeResolutionAsync(AgentTask merge, CancellationToken ct) =>
        AttachMergeResolutionAsync(_db, merge, ct);

    public static async Task AttachMergeResolutionAsync(AppDbContext db, AgentTask merge, CancellationToken ct)
    {
        if (merge.ParentTaskId is not Guid parentId)
            return;

        var finding = await db.StageOutcomes
            .Where(o => o.SubjectTaskId == parentId
                && o.Stage == OrchestrationStage.Rebase
                && o.Outcome == StageOutcomeKind.Found)
            .OrderByDescending(o => o.RecordedAt)
            .ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync(ct);
        if (finding is null)
            return;

        finding.ResolutionTaskId = merge.Id;
        finding.ResolutionCostUsd = merge.CostUsd;
    }

    public static StageOutcomeDto ToDto(StageOutcome o) => new(
        o.Id, o.Stage, o.Outcome, o.Source, o.SubjectTaskId, o.StageTaskId, o.CardId,
        o.CostUsd, o.TokensIn, o.TokensOut, o.DurationSeconds, o.ResolutionTaskId,
        o.ResolutionCostUsd, o.Detail, o.Ref, o.SupersedesId, o.RecordedAt);

    /// <summary>
    /// CARD-0272 S3. An orchestrator override of a stage run. Appends a row with
    /// <see cref="StageOutcomeSource.Orchestrator"/> pointing at the latest row for this
    /// (task, stage) when one exists, plus a <see cref="AgentTaskEventType.FindingRecorded"/>
    /// event so the drawer shows it. A task with no stage still gets a row at the given stage.
    /// </summary>
    public async Task<StageOutcomeDto> RecordFindingAsync(
        Guid taskId,
        RecordStageFindingRequest request,
        CancellationToken ct)
    {
        var stage = ParseRequiredStage(request.Stage);
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId);

        var now = DateTime.UtcNow;
        var existing = await _db.StageOutcomes
            .Where(o => o.Stage == stage && (o.StageTaskId == taskId || o.SubjectTaskId == taskId))
            .OrderByDescending(o => o.RecordedAt)
            .ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync(ct);

        var detail = (request.Detail ?? string.Empty).Trim();
        if (detail.Length > StageOutcome.DetailMaxLength)
            detail = detail[..StageOutcome.DetailMaxLength];

        var duration = 0;
        if (task.CompletedAt is DateTime completed && task.DispatchedAt is DateTime dispatched)
            duration = (int)Math.Clamp(Math.Round((completed - dispatched).TotalSeconds), 0, int.MaxValue);

        var row = new StageOutcome
        {
            Id = Guid.NewGuid(),
            Stage = stage,
            Outcome = request.Found ? StageOutcomeKind.Found : StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Orchestrator,
            SubjectTaskId = existing?.SubjectTaskId ?? task.FollowUpOfTaskId,
            StageTaskId = task.Id,
            CardId = existing?.CardId ?? task.CardId,
            CostUsd = existing?.CostUsd ?? (task.CostUsd == 0m ? null : task.CostUsd),
            TokensIn = existing?.TokensIn ?? (task.TokensIn == 0 ? null : task.TokensIn),
            TokensOut = existing?.TokensOut ?? (task.TokensOut == 0 ? null : task.TokensOut),
            DurationSeconds = existing?.DurationSeconds > 0 ? existing.DurationSeconds : duration,
            Detail = detail,
            SupersedesId = existing?.Id,
            RecordedAt = now,
        };
        _db.StageOutcomes.Add(row);

        var eventDetail = $"{stage} {(request.Found ? "Found" : "Clean")}"
            + (detail.Length > 0 ? $": {detail}" : string.Empty);
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.FindingRecorded,
            Detail = eventDetail.Length <= 4000 ? eventDetail : eventDetail[..4000],
            At = now,
        });
        await _db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public static OrchestrationStage ParseRequiredStage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(
                "stage",
                "stage is required. Use Rebase, Verify, Cleanup, Review, FollowUp, or Deploy.");
        }

        return ParseStage(value)
            ?? throw new ValidationException(
                "stage",
                $"'{value}' is not an orchestration stage. Use Rebase, Verify, Cleanup, Review, FollowUp, or Deploy.");
    }

    private static OrchestrationStage? ParseStage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Enum.TryParse<OrchestrationStage>(value, ignoreCase: true, out var stage)
            || !Enum.IsDefined(stage))
        {
            throw new ValidationException(
                "stage",
                $"'{value}' is not an orchestration stage. Use Rebase, Verify, Cleanup, Review, FollowUp, or Deploy.");
        }

        return stage;
    }
}
