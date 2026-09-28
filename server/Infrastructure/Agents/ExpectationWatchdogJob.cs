using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>Minute sweep. Every directive uses an independent scope and bounded observation.</summary>
public sealed class ExpectationWatchdogJob(IServiceScopeFactory scopes,
    IOptions<ExpectationWatchdogSettings> options, TimeProvider time)
{
    public const string RecurringJobId = "antiphon:expectation-watchdog";

    public static bool ShouldRun(HangfireSettings hangfire, ExpectationWatchdogSettings watchdog) =>
        hangfire.ServerEnabled && watchdog.Enabled;

    [Queue("expectations")]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;
        using var pass = CancellationTokenSource.CreateLinkedTokenSource(ct);
        pass.CancelAfter(TimeSpan.FromSeconds(50));
        var directives = settings.Directives
            .Where(d => ExpectationDirectiveActivity.HasEffects(settings, d, time.GetUtcNow()))
            .ToList();

        // Operator debt gets its own first pass; a slow transcript/probe cannot defer a due page.
        foreach (var directive in directives)
        {
            if (pass.IsCancellationRequested) break;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ExpectationOperatorDeliveryService>()
                    .PublishDueAsync(directive, pass.Token);
            }
            catch (OperationCanceledException) when (pass.IsCancellationRequested) { break; }
            catch { /* Durable debt stays due for the next sweep. */ }
        }

        // A persistently slow or partially observed directive moves behind its peers next pass.
        var scanOrder = directives;
        try
        {
            await using var orderingScope = scopes.CreateAsyncScope();
            var ids = directives.Select(d => d.Id).ToList();
            var cursors = await orderingScope.ServiceProvider.GetRequiredService<AppDbContext>()
                .ExpectationWatchStates.AsNoTracking().Where(s => ids.Contains(s.DirectiveId))
                .ToDictionaryAsync(s => s.DirectiveId, s => s.UpdatedAt, pass.Token);
            scanOrder = directives.OrderBy(d => cursors.TryGetValue(d.Id, out var at)
                    ? at : DateTime.MinValue)
                .ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
        }
        catch (OperationCanceledException) when (pass.IsCancellationRequested) { return; }
        catch { /* Per-directive scopes below still isolate a failed ordering read. */ }

        foreach (var directive in scanOrder)
        {
            if (pass.IsCancellationRequested) break;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var provider = scope.ServiceProvider;
                var db = provider.GetRequiredService<AppDbContext>();
                await provider.GetRequiredService<ExpectationResponseService>()
                    .ReconcileAsync(directive, pass.Token);
                var adapter = provider.GetRequiredService<ExpectationObservationAdapter>();
                var catalog = await adapter.ReferencesAsync(directive, pass.Token);
                var faults = ExpectationDirectiveReferences.Evaluate(directive, catalog);
                if (faults.Any(f => f.Code is not "channel_missing" and not "channel_disabled"))
                {
                    await provider.GetRequiredService<ExpectationLedger>().RecordObservationAsync(
                        directive.Id, await ExpectationConfigIdentity.ResolveAsync(db, directive, settings.Timing, pass.Token),
                        time.GetUtcNow().UtcDateTime, false,
                        string.Join(", ", faults.Select(f => f.Code)), pass.Token);
                    continue;
                }

                using var probe = CancellationTokenSource.CreateLinkedTokenSource(pass.Token);
                probe.CancelAfter(TimeSpan.FromSeconds(5));
                var observation = await adapter.ObserveAsync(directive, probe.Token);
                var scanned = await provider.GetRequiredService<ExpectationWatchdogService>()
                    .ScanAsync(directive, observation, pass.Token);
                var pending = await db.ExpectationNudges.AsNoTracking()
                    .Where(n => n.DirectiveId == directive.Id && n.AttemptState == ExpectationAttemptState.None)
                    .OrderBy(n => n.CreatedAt).Select(n => n.Id).Take(10).ToListAsync(pass.Token);
                foreach (var nudgeId in pending)
                {
                    using var send = CancellationTokenSource.CreateLinkedTokenSource(pass.Token);
                    send.CancelAfter(TimeSpan.FromSeconds(20));
                    await provider.GetRequiredService<ExpectationNudgeDeliveryService>()
                        .DeliverAsync(directive, nudgeId, send.Token);
                }
                await provider.GetRequiredService<ExpectationResponseService>()
                    .ReconcileAsync(directive, pass.Token);
                await provider.GetRequiredService<ExpectationOperatorDeliveryService>()
                    .PublishDueAsync(directive, pass.Token);
            }
            catch (OperationCanceledException) when (pass.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                await using var scope = scopes.CreateAsyncScope();
                try
                {
                    var errorDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await scope.ServiceProvider.GetRequiredService<ExpectationLedger>().RecordObservationAsync(
                        directive.Id, await ExpectationConfigIdentity.ResolveAsync(errorDb, directive,
                            settings.Timing, CancellationToken.None),
                        time.GetUtcNow().UtcDateTime, false, "scan failed: " + ex.GetType().Name,
                        CancellationToken.None);
                }
                catch { /* A failed fault audit must not prevent the next directive's due page. */ }
            }
        }
    }
}
