using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Job 2 selection (CARD-0352 D4): open unlabelled cards on live boards, with a 24 h / 3-attempt
/// backoff recorded in the Diagnoses ledger. The hosted service enqueues; this class does not
/// run the seat.
/// </summary>
public sealed class CardDiagnosisSweep
{
    private static readonly CardStatus[] OpenStatuses =
    [
        CardStatus.Backlog,
        CardStatus.InProgress,
        CardStatus.Review,
        CardStatus.NeedsDecision,
    ];

    private readonly AppDbContext _db;
    private readonly DiagnoseQueue _queue;
    private readonly DiagnoseProvisioner _provisioner;
    private readonly DelegationSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly IModelAvailability? _availability;

    public CardDiagnosisSweep(
        AppDbContext db,
        DiagnoseQueue queue,
        DiagnoseProvisioner provisioner,
        IOptions<DelegationSettings> settings,
        TimeProvider timeProvider,
        IModelAvailability? availability = null)
    {
        _db = db;
        _queue = queue;
        _provisioner = provisioner;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _availability = availability;
    }

    /// <summary>
    /// Cards the next tick should diagnose, already capped at <see cref="DelegationSettings.DiagnoseSweepBatch"/>.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> SelectAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var retryAfter = now.AddHours(-Math.Max(0, _settings.DiagnoseRetryHours));
        var maxAttempts = Math.Max(1, _settings.DiagnoseMaxAttemptsPerCard);
        var batch = Math.Max(1, _settings.DiagnoseSweepBatch);

        // LabelsJson is jsonb — ILIKE/LIKE do not apply — so family presence is filtered in
        // memory. Open unlabelled cards are a few hundred at most; backoff stays in SQL.
        var candidates = await (
            from card in _db.Cards.AsNoTracking()
            join board in _db.Boards.AsNoTracking() on card.BoardId equals board.Id
            where board.ArchivedAt == null
                  && card.ArchivedAt == null
                  && OpenStatuses.Contains(card.Status)
                  && !_db.Diagnoses.Any(d =>
                      d.CardId == card.Id
                      && d.Kind == DiagnosisKind.Labels
                      && d.CreatedAt >= retryAfter)
                  && _db.Diagnoses.Count(d =>
                      d.CardId == card.Id
                      && d.Kind == DiagnosisKind.Labels
                      && d.Outcome != DiagnosisOutcome.Applied
                      && d.CreatedAt > card.UpdatedAt) < maxAttempts
            select card).ToListAsync(ct);

        return candidates
            .Where(c =>
            {
                var labels = BoardService.ParseLabels(c.LabelsJson);
                return !CardDiagnosisLabels.HasComplexity(labels) || !CardDiagnosisLabels.HasUi(labels);
            })
            .OrderBy(c => c.Status == CardStatus.Backlog ? 0
                : c.Status == CardStatus.NeedsDecision ? 1
                : c.Status == CardStatus.InProgress ? 2
                : 3)
            .ThenByDescending(c => c.Importance)
            .ThenByDescending(c => c.Urgency)
            .ThenByDescending(c => c.CreatedAt)
            .Take(batch)
            .Select(c => c.Id)
            .ToList();
    }

    /// <summary>
    /// One sweep tick: gates, then enqueue one <see cref="DiagnoseRequest.ForCard"/> per selected
    /// card. Held or budget-spent ticks enqueue nothing. Disabled ticks are a no-op.
    /// </summary>
    public async Task<int> TickAsync(CancellationToken ct)
    {
        if (!_settings.DiagnoseEnabled || !_settings.DiagnoseSweepEnabled)
            return 0;

        var specialist = await _provisioner.EnsureAsync(ct);
        if (specialist is null)
            return 0;

        var alias = ResolveSeatAlias(specialist);
        if (_availability is not null
            && await _availability.IsHeldAsync(AgentKind.ClaudeCode, alias, ct))
            return 0;

        if (await DailySpendUsdAsync(ct) >= _settings.DiagnoseDailyBudgetUsd)
            return 0;

        var ids = await SelectAsync(ct);
        var enqueued = 0;
        foreach (var id in ids)
        {
            if (_queue.TryEnqueue(DiagnoseRequest.ForCard(id)))
                enqueued++;
        }

        return enqueued;
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
}
