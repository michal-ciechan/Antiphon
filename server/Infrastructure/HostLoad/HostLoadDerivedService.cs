using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.HostLoad;

/// <summary>Durable per-sample work markers are committed with summaries; the ingest cursor is never a derived watermark.</summary>
public sealed class HostLoadDerivedService(AppDbContext db, HostLoadTaskWindowObserver windows,
    IOptions<HostLoadHistorySettings> settings, TimeProvider time)
{
    public async Task<int> DeriveOnceAsync(CancellationToken ct)
    {
        await windows.ReconcileAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"host-load:derived"}))", ct);
        var records = await db.HostLoadSamples.Where(r => r.DerivedAt == null).OrderBy(r => r.Id).Take(120).ToListAsync(ct);
        var primary = records.Where(r => r is { IsPrimary: true, PhysicalHostKey: not null, SourceEpoch: not null }).ToArray();
        foreach (var group in primary.GroupBy(r => (r.PhysicalHostKey, r.SourceEpoch, Hour: HostLoadMetrics.Hour(r.SampledAt))))
        {
            var rollups = await db.HostLoadHourlyRollups.Where(r => r.PhysicalHostKey == group.Key.PhysicalHostKey
                && r.SourceEpoch == group.Key.SourceEpoch && r.HourUtc == group.Key.Hour).ToListAsync(ct);
            var held = rollups.ToDictionary(r => r.Metric, r => Restore(r.HistogramJson), StringComparer.Ordinal);
            foreach (var record in group)
            {
                var sample = JsonSerializer.Deserialize<HostLoadSample>(record.PayloadJson, PhoneHomeFraming.Json)!;
                foreach (var (metric, value) in HostLoadMetrics.Values(sample))
                {
                    if (value is null || !double.IsFinite(value.Value) || value < 0) continue;
                    if (!held.TryGetValue(metric, out var histogram)) held[metric] = histogram = new(HostLoadMetrics.BinWidth(metric));
                    histogram.Add(value.Value);
                }
            }
            var includedIds = group.Select(r => r.Id).ToArray(); var hourEnd = group.Key.Hour.AddHours(1);
            var raw = await db.HostLoadSamples.AsNoTracking().Where(r => r.IsPrimary && r.PhysicalHostKey == group.Key.PhysicalHostKey
                && r.SourceEpoch == group.Key.SourceEpoch && r.SampledAt >= group.Key.Hour && r.SampledAt < hourEnd
                && (r.DerivedAt != null || includedIds.Contains(r.Id))).Take(2000).ToListAsync(ct);
            var decoded = raw.Select(r => (Record: r, Values: HostLoadMetrics.Values(JsonSerializer.Deserialize<HostLoadSample>(r.PayloadJson, PhoneHomeFraming.Json)!))).ToArray();
            foreach (var (metric, histogram) in held)
            {
                var row = rollups.FirstOrDefault(r => r.Metric == metric);
                if (row is null)
                {
                    row = new() { PhysicalHostKey = group.Key.PhysicalHostKey!, SourceEpoch = group.Key.SourceEpoch!.Value,
                        HourUtc = group.Key.Hour, Metric = metric }; db.HostLoadHourlyRollups.Add(row);
                }
                row.HistogramJson = JsonSerializer.Serialize(histogram.State(), PhoneHomeFraming.Json);
                var exact = new HostLoadAggregation().Exact(decoded.Select(r => new HostLoadMetricPoint(r.Record.Id.ToString(),
                    r.Record.SampledAt, r.Values.GetValueOrDefault(metric))), group.Key.Hour, hourEnd);
                var approximate = histogram.Summary(720);
                row.StatisticsJson = JsonSerializer.Serialize(exact.ValidCount == approximate.ValidCount ? exact : approximate, PhoneHomeFraming.Json);
            }
        }
        await windows.AddSamplesAsync(primary, ct);
        foreach (var record in records) record.DerivedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return records.Count;
    }
    public async Task RetainOnceAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        // Never erase raw input whose rollup/window update has not committed.
        var raw = await db.HostLoadSamples.Where(r => r.SampledAt < now.AddDays(-settings.Value.RawRetentionDays)
            && r.DerivedAt != null).OrderBy(r => r.Id).Take(500).ToListAsync(ct);
        db.HostLoadSamples.RemoveRange(raw);
        var hours = await db.HostLoadHourlyRollups.Where(r => r.HourUtc < now.AddDays(-settings.Value.AggregateRetentionDays))
            .OrderBy(r => r.Id).Take(500).ToListAsync(ct); db.HostLoadHourlyRollups.RemoveRange(hours);
        var terminal = await db.AgentTaskLoadWindows.Where(r => r.EndedAt != null && r.EndedAt < now.AddDays(-settings.Value.TaskRetentionDays))
            .OrderBy(r => r.EndedAt).Take(500).ToListAsync(ct); db.AgentTaskLoadWindows.RemoveRange(terminal);
        var identities = await db.HostLoadSampleIdentities.Where(r => r.IngestedAt < now.AddDays(-90))
            .OrderBy(r => r.IngestedAt).Take(500).ToListAsync(ct); db.HostLoadSampleIdentities.RemoveRange(identities);
        await db.SaveChangesAsync(ct);
    }
    internal static HostLoadHistogram Restore(string json) => new(JsonSerializer.Deserialize<HostLoadHistogramState>(json, PhoneHomeFraming.Json)!);
}

public sealed class HostLoadDerivedWorker(IServiceScopeFactory scopes, IOptions<HostLoadHistorySettings> settings,
    TimeProvider time, ILogger<HostLoadDerivedWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.Enabled) return;
        await Task.Yield(); var lastRetention = time.GetUtcNow();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), time);
        do
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, deadline.Token);
                using var scope = scopes.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<HostLoadDerivedService>();
                await service.DeriveOnceAsync(linked.Token);
                if (time.GetUtcNow() - lastRetention >= TimeSpan.FromHours(1))
                { await service.RetainOnceAsync(linked.Token); lastRetention = time.GetUtcNow(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("Host load derived work unavailable; durable work remains pending"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
