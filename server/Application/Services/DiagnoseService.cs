using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Diagnose-seat worker (CARD-0352). Job 1 replaces a long Goal-fallback title in place.
/// Job 2 labels an open card with <c>complexity:*</c> / <c>ui:*</c>.
/// </summary>
public sealed class DiagnoseService
{
    private readonly AppDbContext _db;
    private readonly DiagnoseProvisioner _provisioner;
    private readonly DelegationSettings _settings;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DiagnoseService> _logger;
    private readonly IModelAvailability? _availability;
    private readonly SpecialistTaskRunner _runner;

    public DiagnoseService(
        AppDbContext db,
        DiagnoseProvisioner provisioner,
        IOptions<DelegationSettings> settings,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<DiagnoseService> logger,
        IModelAvailability? availability = null,
        IAlertService? alerts = null,
        SpecialistTaskRunner? runner = null)
    {
        _db = db;
        _provisioner = provisioner;
        _settings = settings.Value;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
        _availability = availability;
        _runner = runner ?? new SpecialistTaskRunner(db, timeProvider, logger, alerts);
    }

    public Task RunAsync(DiagnoseRequest request, CancellationToken ct) =>
        request.Kind switch
        {
            DiagnosisKind.Title when request.TaskId is Guid taskId =>
                RunTitleAsync(taskId, ct),
            DiagnosisKind.Labels when request.CardId is Guid cardId =>
                RunCardAsync(cardId, request.Forced, ct),
            _ => Task.CompletedTask,
        };

    /// <summary>
    /// Title a task whose create stored the Goal-first-line fallback. Never throws into the
    /// drainer for a rejected or degraded answer — those write a ledger row and leave the title.
    /// </summary>
    public async Task RunTitleAsync(Guid taskId, CancellationToken ct)
    {
        var started = _timeProvider.GetUtcNow();

        if (!_settings.DiagnoseEnabled || !_settings.DiagnoseTitleEnabled)
            return;

        var task = await _db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return;

        var fallback = AgentTaskService.FallbackTitle(task.Goal);
        if (!string.Equals(task.Title, fallback, StringComparison.Ordinal))
        {
            await WriteLedgerAsync(
                DiagnosisKind.Title,
                taskId,
                cardId: task.CardId,
                diagnoseTaskId: null,
                DiagnosisOutcome.SkippedAlreadyTitled,
                answer: null,
                applied: null,
                reason: "title is no longer the Goal fallback",
                costUsd: 0m,
                WaitMs(started),
                forced: false,
                ct);
            return;
        }

        var specialist = await _provisioner.EnsureAsync(ct);
        if (specialist is null)
            return;

        var alias = ResolveSeatAlias(specialist);
        if (_availability is not null
            && await _availability.IsHeldAsync(AgentKind.ClaudeCode, alias, ct))
        {
            await WriteLedgerAsync(
                DiagnosisKind.Title, taskId, task.CardId, null,
                DiagnosisOutcome.DegradedHeld, null, null,
                $"alias '{alias}' is held", 0m, WaitMs(started), false, ct);
            return;
        }

        if (await DailySpendUsdAsync(ct) >= _settings.DiagnoseDailyBudgetUsd)
        {
            await WriteLedgerAsync(
                DiagnosisKind.Title, taskId, task.CardId, null,
                DiagnosisOutcome.DegradedBudget, null, null,
                $"daily budget ${_settings.DiagnoseDailyBudgetUsd:0.00} spent",
                0m, WaitMs(started), false, ct);
            return;
        }

        var cardIdentifier = task.CardId is Guid cardId
            ? await _db.Cards.AsNoTracking()
                .Where(c => c.Id == cardId)
                .Select(c => c.Identifier)
                .FirstOrDefaultAsync(ct)
            : null;

        var spec = DiagnoseProvisioner.Spec(_settings);
        var wait = TimeSpan.FromSeconds(Math.Max(1, _settings.DiagnoseWaitSeconds));
        var run = await _runner.RunAsync(
            spec,
            Diagnosis.BuildTitleTaskTitle(task),
            Diagnosis.BuildTitleGoal(task, cardIdentifier),
            wait,
            _settings.DiagnoseMaxBacklog,
            _provisioner.EnsureAsync,
            ct,
            createdDetail: $"Title diagnosis of task {DelegationReportFormatter.Short(task.Id)}.");

        var outcome = run.Outcome switch
        {
            SpecialistRunOutcome.Busy => DiagnosisOutcome.DegradedBusy,
            SpecialistRunOutcome.Timeout => DiagnosisOutcome.DegradedTimeout,
            SpecialistRunOutcome.Failed => DiagnosisOutcome.DegradedFailed,
            SpecialistRunOutcome.Empty => DiagnosisOutcome.DegradedEmpty,
            SpecialistRunOutcome.ProvisionFailed or SpecialistRunOutcome.QueueFailed
                or SpecialistRunOutcome.Disabled => DiagnosisOutcome.DegradedUnavailable,
            SpecialistRunOutcome.Succeeded => DiagnosisOutcome.Applied,
            _ => DiagnosisOutcome.DegradedUnavailable,
        };

        if (outcome != DiagnosisOutcome.Applied)
        {
            await WriteLedgerAsync(
                DiagnosisKind.Title, taskId, task.CardId, run.RunTaskId,
                outcome, run.Result, null, outcome.ToString(),
                run.CostUsd, run.WaitMs, false, ct);
            return;
        }

        if (!Diagnosis.TryParseTitle(
                run.Result, fallback, cardIdentifier, out var title, out var reason))
        {
            await WriteLedgerAsync(
                DiagnosisKind.Title, taskId, task.CardId, run.RunTaskId,
                DiagnosisOutcome.RejectedGate, run.Result, null, reason,
                run.CostUsd, run.WaitMs, false, ct);
            return;
        }

        var applied = await ApplyTitleAsync(taskId, fallback, title, run, ct);
        if (!applied)
        {
            await WriteLedgerAsync(
                DiagnosisKind.Title, taskId, task.CardId, run.RunTaskId,
                DiagnosisOutcome.SkippedAlreadyTitled, run.Result, null,
                "title is no longer the Goal fallback",
                run.CostUsd, run.WaitMs, false, ct);
            return;
        }

        await WriteLedgerAsync(
            DiagnosisKind.Title, taskId, task.CardId, run.RunTaskId,
            DiagnosisOutcome.Applied, run.Result, title, null,
            run.CostUsd, run.WaitMs, false, ct);
    }

    /// <summary>
    /// Label a card. Never throws into the drainer for a rejected or degraded answer — those write
    /// a ledger row and leave the labels. <paramref name="forced"/> is the on-demand path: skip
    /// backoff-style already-labelled short-circuit and replace diagnosis labels.
    /// </summary>
    public async Task RunCardAsync(Guid cardId, bool forced, CancellationToken ct)
    {
        var started = _timeProvider.GetUtcNow();

        if (!_settings.DiagnoseEnabled)
            return;

        var card = await _db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null)
            return;

        var existingLabels = BoardService.ParseLabels(card.LabelsJson);
        if (!forced && CardDiagnosisLabels.HasBothFamilies(existingLabels))
        {
            await WriteLedgerAsync(
                DiagnosisKind.Labels, null, cardId, null,
                DiagnosisOutcome.SkippedAlreadyLabelled, null, null,
                "card already has complexity: and ui: labels",
                0m, WaitMs(started), forced, ct);
            return;
        }

        var specialist = await _provisioner.EnsureAsync(ct);
        if (specialist is null)
            return;

        var alias = ResolveSeatAlias(specialist);
        if (_availability is not null
            && await _availability.IsHeldAsync(AgentKind.ClaudeCode, alias, ct))
        {
            await WriteLedgerAsync(
                DiagnosisKind.Labels, null, cardId, null,
                DiagnosisOutcome.DegradedHeld, null, null,
                $"alias '{alias}' is held", 0m, WaitMs(started), forced, ct);
            return;
        }

        if (await DailySpendUsdAsync(ct) >= _settings.DiagnoseDailyBudgetUsd)
        {
            await WriteLedgerAsync(
                DiagnosisKind.Labels, null, cardId, null,
                DiagnosisOutcome.DegradedBudget, null, null,
                $"daily budget ${_settings.DiagnoseDailyBudgetUsd:0.00} spent",
                0m, WaitMs(started), forced, ct);
            return;
        }

        var spec = DiagnoseProvisioner.Spec(_settings);
        var wait = TimeSpan.FromSeconds(Math.Max(1, _settings.DiagnoseWaitSeconds));
        var run = await _runner.RunAsync(
            spec,
            Diagnosis.BuildLabelsTaskTitle(card),
            Diagnosis.BuildLabelsGoal(card, _settings.DiagnoseMaxInputChars),
            wait,
            _settings.DiagnoseMaxBacklog,
            _provisioner.EnsureAsync,
            ct,
            createdDetail: $"Label diagnosis of {card.Identifier}.");

        var outcome = run.Outcome switch
        {
            SpecialistRunOutcome.Busy => DiagnosisOutcome.DegradedBusy,
            SpecialistRunOutcome.Timeout => DiagnosisOutcome.DegradedTimeout,
            SpecialistRunOutcome.Failed => DiagnosisOutcome.DegradedFailed,
            SpecialistRunOutcome.Empty => DiagnosisOutcome.DegradedEmpty,
            SpecialistRunOutcome.ProvisionFailed or SpecialistRunOutcome.QueueFailed
                or SpecialistRunOutcome.Disabled => DiagnosisOutcome.DegradedUnavailable,
            SpecialistRunOutcome.Succeeded => DiagnosisOutcome.Applied,
            _ => DiagnosisOutcome.DegradedUnavailable,
        };

        if (outcome != DiagnosisOutcome.Applied)
        {
            await WriteLedgerAsync(
                DiagnosisKind.Labels, null, cardId, run.RunTaskId,
                outcome, run.Result, null, outcome.ToString(),
                run.CostUsd, run.WaitMs, forced, ct);
            return;
        }

        var parsed = Diagnosis.TryParseLabels(run.Result);
        if (parsed.Unclear)
        {
            await WriteLedgerAsync(
                DiagnosisKind.Labels, null, cardId, run.RunTaskId,
                DiagnosisOutcome.Unclear, run.Result, null, "unclear",
                run.CostUsd, run.WaitMs, forced, ct);
            return;
        }

        if (!parsed.Ok || parsed.Complexity is null || parsed.Ui is null)
        {
            await WriteLedgerAsync(
                DiagnosisKind.Labels, null, cardId, run.RunTaskId,
                DiagnosisOutcome.RejectedUnparseable, run.Result, null, parsed.Reason ?? "unparseable",
                run.CostUsd, run.WaitMs, forced, ct);
            return;
        }

        var appliedText = CardDiagnosisLabels.AppliedText(parsed.Complexity.Value, parsed.Ui.Value);
        if (_settings.DiagnoseLabelMode == DiagnoseLabelMode.Shadow)
        {
            await WriteLedgerAsync(
                DiagnosisKind.Labels, null, cardId, run.RunTaskId,
                DiagnosisOutcome.Shadowed, run.Result, appliedText, "shadow",
                run.CostUsd, run.WaitMs, forced, ct);
            return;
        }

        var shortId = run.RunTaskId is Guid id
            ? DelegationReportFormatter.Short(id)
            : "none";
        var reason =
            $"antiphon-diagnose: {appliedText} (diagnose task {shortId}, ${run.CostUsd:0.0000})";

        CardDiagnosisApplyResult apply;
        try
        {
            apply = await ApplyLabelsAsync(
                cardId, parsed.Complexity.Value, parsed.Ui.Value, reason, forced, ct);
        }
        catch (ConflictException)
        {
            _db.ChangeTracker.Clear();
            await WriteLedgerAsync(
                DiagnosisKind.Labels, null, cardId, run.RunTaskId,
                DiagnosisOutcome.RejectedConflict, run.Result, null,
                "card was modified by another operation",
                run.CostUsd, run.WaitMs, forced, ct);
            return;
        }

        if (apply.AlreadyLabelled)
        {
            await WriteLedgerAsync(
                DiagnosisKind.Labels, null, cardId, run.RunTaskId,
                DiagnosisOutcome.SkippedAlreadyLabelled, run.Result, apply.Applied,
                "card already has complexity: and ui: labels",
                run.CostUsd, run.WaitMs, forced, ct);
            return;
        }

        await WriteLedgerAsync(
            DiagnosisKind.Labels, null, cardId, run.RunTaskId,
            DiagnosisOutcome.Applied, run.Result, apply.Applied, null,
            run.CostUsd, run.WaitMs, forced, ct);
    }

    /// <summary>
    /// Read-side of the Diagnoses ledger (CARD-0352 D7). Newest first. Joined with the card
    /// identifier / task short id so a caller does not have to.
    /// </summary>
    public async Task<IReadOnlyList<DiagnosisDto>> ListAsync(
        Guid? cardId,
        Guid? taskId,
        DateTime? since,
        DiagnosisOutcome? outcome,
        DiagnosisKind? kind,
        int? limit,
        CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = _db.Diagnoses.AsNoTracking().AsQueryable();
        if (cardId is Guid cid)
            query = query.Where(d => d.CardId == cid);
        if (taskId is Guid tid)
            query = query.Where(d => d.TaskId == tid);
        if (since is DateTime sinceUtc)
        {
            var start = DateTime.SpecifyKind(sinceUtc.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(d => d.CreatedAt >= start);
        }

        if (outcome is DiagnosisOutcome o)
            query = query.Where(d => d.Outcome == o);
        if (kind is DiagnosisKind k)
            query = query.Where(d => d.Kind == k);

        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        var cardIds = rows.Where(d => d.CardId is not null).Select(d => d.CardId!.Value).Distinct().ToList();
        var taskIds = rows.Where(d => d.TaskId is not null).Select(d => d.TaskId!.Value).Distinct().ToList();
        var cards = cardIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Cards.AsNoTracking()
                .Where(c => cardIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Identifier, ct);
        var tasks = taskIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await _db.AgentTasks.AsNoTracking()
                .Where(t => taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Id, ct);

        return rows.Select(d => new DiagnosisDto(
            d.Id,
            d.Kind,
            d.Outcome,
            d.CardId,
            d.CardId is Guid c && cards.TryGetValue(c, out var ident) ? ident : null,
            d.TaskId,
            d.TaskId is Guid t && tasks.ContainsKey(t) ? DelegationReportFormatter.Short(t) : null,
            d.DiagnoseTaskId,
            d.Answer,
            d.Applied,
            d.Reason,
            d.BundleStamp,
            d.CostUsd,
            d.WaitMs,
            d.Forced,
            d.CreatedAt)).ToList();
    }

    /// <summary>Kind × outcome counts, wait percentiles, spend, and Applied label distribution.</summary>
    public async Task<DiagnosisStatsDto> StatsAsync(DateTime? since, CancellationToken ct)
    {
        var query = _db.Diagnoses.AsNoTracking().AsQueryable();
        DateTime? start = null;
        if (since is DateTime sinceUtc)
        {
            start = DateTime.SpecifyKind(sinceUtc.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(d => d.CreatedAt >= start);
        }

        var rows = await query.ToListAsync(ct);
        var counts = rows
            .GroupBy(d => (d.Kind, d.Outcome))
            .OrderBy(g => g.Key.Kind)
            .ThenBy(g => g.Key.Outcome)
            .Select(g => new DiagnosisOutcomeCountDto(g.Key.Kind, g.Key.Outcome, g.Count()))
            .ToList();

        var waits = rows.Select(d => d.WaitMs).OrderBy(w => w).ToArray();
        var distribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(d =>
                     d.Kind == DiagnosisKind.Labels
                     && d.Outcome == DiagnosisOutcome.Applied
                     && !string.IsNullOrWhiteSpace(d.Applied)))
        {
            var parsed = Diagnosis.TryParseLabels(row.Applied);
            if (!parsed.Ok || parsed.Complexity is null || parsed.Ui is null)
                continue;
            AddCount(distribution, CardDiagnosisLabels.ComplexityLabel(parsed.Complexity.Value));
            AddCount(distribution, CardDiagnosisLabels.UiLabel(parsed.Ui.Value));
        }

        return new DiagnosisStatsDto(
            start,
            rows.Count,
            rows.Sum(d => d.CostUsd),
            Percentile(waits, 0.50),
            Percentile(waits, 0.90),
            counts,
            distribution);
    }

    internal static string BundleStamp()
    {
        var bundle = InstructionBundles.Get(InstructionBundles.Diagnose);
        return $"{bundle.Key} v{Diagnosis.ContractVersion} {bundle.Version}";
    }

    private async Task<bool> ApplyTitleAsync(
        Guid taskId,
        string fallback,
        string title,
        SpecialistRun run,
        CancellationToken ct)
    {
        var tracked = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (tracked is null)
            return false;
        if (!string.Equals(tracked.Title, fallback, StringComparison.Ordinal)
            && !string.Equals(tracked.Title, AgentTaskService.FallbackTitle(tracked.Goal), StringComparison.Ordinal))
            return false;

        var excerpt = fallback.Length <= 60 ? fallback : fallback[..60] + "…";
        var shortId = run.RunTaskId is Guid id
            ? DelegationReportFormatter.Short(id)
            : "none";
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        tracked.Title = title;
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = AgentTaskEventType.Diagnosed,
            Detail =
                $"Title set by antiphon-diagnose from \"{excerpt}\" (diagnose task {shortId}, ${run.CostUsd:0.0000})",
            At = now,
        });
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged",
            new { taskId, rootId = tracked.RootTaskId },
            ct);
        return true;
    }

    private async Task<CardDiagnosisApplyResult> ApplyLabelsAsync(
        Guid cardId,
        TaskComplexity complexity,
        bool ui,
        string reason,
        bool forced,
        CancellationToken ct)
    {
        var tracked = await _db.Cards.FirstOrDefaultAsync(c => c.Id == cardId, ct)
            ?? throw new NotFoundException(nameof(Card), cardId);

        var existing = BoardService.ParseLabels(tracked.LabelsJson);
        var merge = CardDiagnosisLabels.Merge(existing, complexity, ui, forced);
        var labelsJson = BoardService.SerializeLabels(merge.Labels);
        if (!merge.Wrote)
        {
            return new CardDiagnosisApplyResult(
                false, merge.AlreadyLabelled, merge.Applied, tracked.LabelsJson);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        CardRevisionLog.AppendContentEdit(tracked, reason, CardDiagnosisLabels.DiagnoseActor, now);
        tracked.LabelsJson = labelsJson;
        tracked.UpdatedAt = now;
        tracked.ConcurrencyToken = Guid.NewGuid();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(
                $"Card '{tracked.Identifier}' was modified by another operation.", ex);
        }
        catch (DbUpdateException ex) when (IsDuplicateRevisionNumber(ex))
        {
            throw new ConflictException(
                $"Card '{tracked.Identifier}' was modified by another operation "
                + $"({AgentService.DescribeDbFailure(ex)}).",
                ex);
        }

        await _eventBus.PublishToAllAsync(
            "CardChanged", new { boardId = tracked.BoardId, cardId = tracked.Id }, ct);
        return new CardDiagnosisApplyResult(true, false, merge.Applied, labelsJson);
    }

    private static bool IsDuplicateRevisionNumber(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_CardRevisions_CardId_RevisionNumber"
        };

    private static void AddCount(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;

    private static int Percentile(int[] sortedAscending, double p)
    {
        if (sortedAscending.Length == 0)
            return 0;
        var index = (int)Math.Round((sortedAscending.Length - 1) * p, MidpointRounding.AwayFromZero);
        return sortedAscending[Math.Clamp(index, 0, sortedAscending.Length - 1)];
    }

    private async Task<decimal> DailySpendUsdAsync(CancellationToken ct)
    {
        var startOfDay = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var start = DateTime.SpecifyKind(startOfDay, DateTimeKind.Utc);
        var end = start.AddDays(1);
        return await _db.AgentTasks
            .Where(t => t.Role == AgentTaskRole.Diagnose && t.CreatedAt >= start && t.CreatedAt < end)
            .SumAsync(t => (decimal?)t.CostUsd, ct) ?? 0m;
    }

    private static string ResolveSeatAlias(Agent specialist)
    {
        var fromId = ModelAlias.Normalize(AgentKind.ClaudeCode, specialist.ModelId);
        return fromId ?? ModelLevelAliases.For(AgentKind.ClaudeCode, AgentModelLevel.Low);
    }

    private int WaitMs(DateTimeOffset started) =>
        (int)Math.Max(0, (_timeProvider.GetUtcNow() - started).TotalMilliseconds);

    private async Task WriteLedgerAsync(
        DiagnosisKind kind,
        Guid? taskId,
        Guid? cardId,
        Guid? diagnoseTaskId,
        DiagnosisOutcome outcome,
        string? answer,
        string? applied,
        string? reason,
        decimal costUsd,
        int waitMs,
        bool forced,
        CancellationToken ct)
    {
        _db.Diagnoses.Add(new DiagnosisRecord
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            TaskId = taskId,
            CardId = cardId,
            DiagnoseTaskId = diagnoseTaskId,
            Outcome = outcome,
            Answer = answer,
            Applied = applied,
            Reason = reason,
            BundleStamp = BundleStamp(),
            CostUsd = costUsd,
            WaitMs = waitMs,
            Forced = forced,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync(ct);
    }
}
