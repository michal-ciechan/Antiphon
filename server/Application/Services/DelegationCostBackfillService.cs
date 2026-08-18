using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Re-prices settled tasks that were costed before CARD-0023 (<c>CostPricingVersion &lt; </c>
/// <see cref="DelegationCost.PricingVersion"/>).
///
/// Leaving them alone was the cheaper option, but <c>AgentTaskDispatcher.RootIsOverBudgetAsync</c>
/// keeps summing <c>CostUsd</c> across a run — a stale row inflated ~10x goes on throttling its
/// root on spend that never happened. The stored transcripts are still there, so the honest figure
/// is recoverable: this recomputes it through the SAME code path a settle uses, so there is exactly
/// one pricing implementation to maintain.
///
/// A task priced at the rates in force when it RAN (its CompletedAt), not today's — history is not
/// repriced by a later table edit. Rows whose session or transcript is gone cannot be recomputed;
/// they keep their old figure and their version 0, which is what the UI labels.
///
/// Idempotent: a second run finds nothing to do. Bumping PricingVersion is what re-arms it.
/// </summary>
public sealed class DelegationCostBackfillService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DelegationSettings _settings;
    private readonly ILogger<DelegationCostBackfillService> _logger;

    public DelegationCostBackfillService(
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> settings,
        ILogger<DelegationCostBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Off the startup path — a DB scan must never delay the server coming up.
        await Task.Yield();

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var stale = await db.AgentTasks
                .Where(t => t.CostPricingVersion < DelegationCost.PricingVersion && t.AgentSessionId != null)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync(ct);
            if (stale.Count == 0)
                return;

            var repriced = 0;
            var unrecoverable = 0;
            decimal before = 0, after = 0;

            foreach (var task in stale)
            {
                ct.ThrowIfCancellationRequested();

                if (task.CompletedAt is not DateTime at)
                {
                    // Still in flight — nothing is costed until it settles, and the settle path
                    // will stamp it. Leaving it alone also keeps this run idempotent.
                    continue;
                }

                var spend = await DelegationUsageRollup.ForSessionAsync(
                    db, task.AgentSessionId!.Value, task.DispatchedAt, at, ct);
                if (spend == TokenSpend.Zero && task.TokensIn == 0 && task.TokensOut == 0)
                {
                    // Nothing was ever recorded (never dispatched, or transcript purged). A zero
                    // row is not a wrong row — stamp it so it stops being rescanned.
                    task.CostPricingVersion = DelegationCost.PricingVersion;
                    continue;
                }
                if (spend == TokenSpend.Zero)
                {
                    // It HAD spend once but the transcript is gone: the old figure is all we have.
                    // Leave version 0 so the UI keeps calling it a legacy estimate.
                    unrecoverable++;
                    continue;
                }

                before += task.CostUsd;
                task.TokensIn = spend.InputTokens;
                task.CacheReadTokens = spend.CacheReadTokens;
                task.CacheCreationTokens = spend.CacheCreationTokens;
                task.TokensOut = spend.OutputTokens;
                // Kind-aware for the same reason the settle path is. Every row this sweep can
                // reach predates AgentKind and therefore carries the column default, ClaudeCode —
                // so a re-price cannot move a historical figure by passing it.
                task.CostUsd = DelegationCost.Estimate(
                    _settings.Pricing, task.ModelLevel, spend, at, task.AgentKind);
                task.CostPricingVersion = DelegationCost.PricingVersion;
                after += task.CostUsd;
                repriced++;
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Re-priced {Repriced} delegated task(s) at pricing v{Version}: ${Before:0.00} -> ${After:0.00}"
                    + " ({Unrecoverable} left as legacy estimates — transcript gone)",
                repriced, DelegationCost.PricingVersion, before, after, unrecoverable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed backfill must not take the server down — the old figures stay, labelled.
            _logger.LogWarning(ex, "Delegation cost backfill failed; existing figures left untouched");
        }
    }
}
