using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Job 1 of the diagnose seat (CARD-0352 S3): replace a long Goal-fallback title in place.
/// Job 2 (<see cref="RunCardAsync"/>) is a dispatch stub until S4.
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

    /// <summary>S4 fills this in. S3 keeps the dispatch switch compiling and the drainer crash-free.</summary>
    public Task RunCardAsync(Guid cardId, bool forced, CancellationToken ct)
    {
        _logger.LogDebug("Card diagnosis for {CardId} (forced={Forced}) lands in S4; skipping", cardId, forced);
        return Task.CompletedTask;
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
