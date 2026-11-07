using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.HostLoad;

/// <summary>Observes durable event snapshots; notification loss and process restart do not lose work.</summary>
public sealed class HostLoadTaskWindowObserver(AppDbContext db, IOptions<HostLoadHistorySettings> settings, TimeProvider time)
{
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"host-load:events"}))", ct);
        var events = await db.AgentTaskEvents.Where(e => e.LoadAttempt != null
            && !db.HostLoadObservedEvents.Any(o => o.EventId == e.Id)).OrderBy(e => e.At).ThenBy(e => e.Id).Take(120).ToListAsync(ct);
        var taskIds = events.Select(e => e.AgentTaskId).Distinct().ToArray();
        var sessionIds = events.Where(e => e.LoadSessionId != null).Select(e => e.LoadSessionId!.Value).Distinct().ToArray();
        var sessions = await db.AgentSessions.AsNoTracking().Where(s => sessionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var existing = await db.AgentTaskLoadWindows.Where(w => taskIds.Contains(w.TaskId)).ToListAsync(ct);
        foreach (var ev in events)
        {
            var at = Utc(ev.At); var attempt = ev.LoadAttempt!.Value;
            var terminal = ev.LoadStatus is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled;
            foreach (var window in existing.Where(w => w.TaskId == ev.AgentTaskId && w.EndedAt == null
                && (w.Attempt != attempt || terminal || (ev.Type == AgentTaskEventType.Dispatched
                    && ev.LoadRunnerId is not null && ResolveHost(ev.LoadRunnerId, at)?.PhysicalHostKey != w.PhysicalHostKey))).ToArray())
                await CloseAsync(window, ev.Id, at, ct);
            if (ev.Type == AgentTaskEventType.Dispatched && ev.LoadSessionId is { } sid
                && sessions.TryGetValue(sid, out var session) && session.StartedAt != default
                && ev.LoadQueuedAt is { } queued && session.StartedAt >= queued)
            {
                var startAt = Utc(session.StartedAt);
                var binding = ResolveHost(ev.LoadRunnerId!, startAt);
                if (binding is not null && !existing.Any(w => w.StartEventId == ev.Id))
                {
                    var boundary = await BoundaryAsync(binding.PhysicalHostKey, ev.Id, startAt, ct);
                    var window = new AgentTaskLoadWindow
                    {
                        Id = Guid.NewGuid(), TaskId = ev.AgentTaskId, Attempt = attempt, PhysicalHostKey = binding.PhysicalHostKey,
                        RunnerBootId = boundary.SampleIdentity?.BootId ?? Guid.Empty,
                        WindowOrdinal = existing.Where(w => w.TaskId == ev.AgentTaskId && w.Attempt == attempt).Select(w => w.WindowOrdinal).DefaultIfEmpty().Max() + 1,
                        StartEventId = ev.Id, StartedAt = startAt, Complete = boundary.State == "known",
                        DetailsJson = JsonSerializer.Serialize(new HostLoadTaskWindowDto(ev.AgentTaskId, attempt, binding.PhysicalHostKey,
                            boundary.SampleIdentity?.BootId ?? Guid.Empty, 0, Utc(queued), ev.LoadDispatchedAt is { } dispatch ? Utc(dispatch) : null,
                            startAt, null, "open", boundary, null, new Dictionary<string, HostLoadMetricSummary>(), 1), PhoneHomeFraming.Json),
                    };
                    db.AgentTaskLoadWindows.Add(window); existing.Add(window);
                    // Backfill already-derived observations only. Pending raw records are handled by the separate derived frontier.
                    var backfill = await db.HostLoadSamples.AsNoTracking().Where(r => r.IsPrimary && r.DerivedAt != null
                        && r.PhysicalHostKey == window.PhysicalHostKey && r.BootId == window.RunnerBootId && r.SampledAt > window.StartedAt)
                        .OrderBy(r => r.Id).Take(2001).ToListAsync(ct);
                    if (backfill.Count > 2000) window.Complete = false;
                    else await AddToBucketsAsync([window], backfill, ct);
                }
            }
            db.HostLoadObservedEvents.Add(new() { EventId = ev.Id, ObservedAt = time.GetUtcNow() });
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return events.Count;
    }

    private HostLoadBinding? ResolveHost(string runner, DateTimeOffset at)
    {
        var matches = settings.Value.Bindings.Where(b => HostLoadCounterCache.Canonical(b.RunnerId) == HostLoadCounterCache.Canonical(runner)
            && b.Primary && at >= b.From && (b.To is null || at < b.To)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    public async Task AddSamplesAsync(IReadOnlyList<HostLoadSampleRecord> samples, CancellationToken ct)
    {
        if (samples.Count == 0) return;
        var from = samples.Min(s => s.SampledAt); var to = samples.Max(s => s.SampledAt);
        var hosts = samples.Select(s => s.PhysicalHostKey!).Distinct().ToArray();
        var windows = await db.AgentTaskLoadWindows.Where(w => hosts.Contains(w.PhysicalHostKey)
            && w.StartedAt <= to && (w.EndedAt == null || w.EndedAt >= from)).Take(2001).ToListAsync(ct);
        if (windows.Count > 2000) throw new InvalidOperationException("host_load_window_batch_bound");
        foreach (var window in windows.Where(w => w.RunnerBootId == Guid.Empty).ToArray())
        {
            var boundary = await BoundaryAsync(window.PhysicalHostKey, window.StartEventId, window.StartedAt, ct);
            if (boundary.State != "known") continue;
            var details = Read(window); window.RunnerBootId = boundary.SampleIdentity!.BootId;
            window.Complete = true; window.DetailsJson = JsonSerializer.Serialize(details with { Start = boundary, RunnerBootId = window.RunnerBootId }, PhoneHomeFraming.Json);
        }
        // A collector boot change splits an open ambient window. Late replay from an older boot
        // cannot reopen or move the current segment backwards.
        foreach (var sample in samples.OrderBy(s => s.SampledAt).ThenBy(s => s.Id))
        {
            foreach (var previous in windows.Where(w => w.EndedAt == null && w.PhysicalHostKey == sample.PhysicalHostKey
                && w.RunnerBootId != Guid.Empty && w.RunnerBootId != sample.BootId && sample.SampledAt > w.StartedAt).ToArray())
            {
                await CloseAsync(previous, sample.BootId, sample.SampledAt, ct);
                var details = Read(previous);
                var start = await BoundaryAsync(previous.PhysicalHostKey, sample.BootId, sample.SampledAt, ct);
                var next = new AgentTaskLoadWindow { Id = Guid.NewGuid(), TaskId = previous.TaskId, Attempt = previous.Attempt,
                    PhysicalHostKey = previous.PhysicalHostKey, RunnerBootId = sample.BootId,
                    WindowOrdinal = windows.Where(w => w.TaskId == previous.TaskId && w.Attempt == previous.Attempt).Max(w => w.WindowOrdinal) + 1,
                    StartEventId = sample.BootId, StartedAt = sample.SampledAt, Complete = start.State == "known",
                    DetailsJson = JsonSerializer.Serialize(details with { RunnerBootId = sample.BootId, Start = start,
                        StartedAt = sample.SampledAt, EndedAt = null, State = "open", End = null }, PhoneHomeFraming.Json) };
                db.AgentTaskLoadWindows.Add(next); windows.Add(next);
            }
        }
        await AddToBucketsAsync(windows, samples, ct);
    }
    private async Task AddToBucketsAsync(IReadOnlyList<AgentTaskLoadWindow> windows, IReadOnlyList<HostLoadSampleRecord> samples, CancellationToken ct)
    {
        if (samples.Count == 0 || windows.Count == 0) return;
        var ids = windows.Select(w => w.Id).ToArray(); var firstHour = HostLoadMetrics.Hour(samples.Min(s => s.SampledAt));
        var lastHour = HostLoadMetrics.Hour(samples.Max(s => s.SampledAt));
        var buckets = await db.HostLoadTaskMetricBuckets.Where(b => ids.Contains(b.WindowId) && b.HourUtc >= firstHour && b.HourUtc <= lastHour).ToListAsync(ct);
        var indexed = buckets.ToDictionary(b => (b.WindowId, b.HourUtc, b.Metric));
        var histograms = buckets.ToDictionary(b => (b.WindowId, b.HourUtc, b.Metric), b => HostLoadDerivedService.Restore(b.HistogramJson));
        foreach (var record in samples)
        {
            var sample = JsonSerializer.Deserialize<HostLoadSample>(record.PayloadJson, PhoneHomeFraming.Json)!;
            var hour = HostLoadMetrics.Hour(record.SampledAt);
            foreach (var window in windows.Where(w => w.PhysicalHostKey == record.PhysicalHostKey && w.RunnerBootId == record.BootId
                && record.SampledAt > w.StartedAt && (w.EndedAt == null || record.SampledAt < w.EndedAt)))
                foreach (var (metric, value) in HostLoadMetrics.Values(sample))
                {
                    if (value is null || !double.IsFinite(value.Value) || value < 0) continue;
                    var key = (window.Id, hour, metric);
                    if (!histograms.TryGetValue(key, out var histogram))
                    {
                        histograms[key] = histogram = new(HostLoadMetrics.BinWidth(metric));
                        var bucket = new HostLoadTaskMetricBucket { WindowId = window.Id, HourUtc = hour, Metric = metric };
                        indexed[key] = bucket; db.HostLoadTaskMetricBuckets.Add(bucket);
                    }
                    histogram.Add(value.Value);
                }
        }
        foreach (var (key, histogram) in histograms) indexed[key].HistogramJson = JsonSerializer.Serialize(histogram.State(), PhoneHomeFraming.Json);
    }
    private async Task CloseAsync(AgentTaskLoadWindow window, Guid eventId, DateTimeOffset at, CancellationToken ct)
    {
        if (at < window.StartedAt) { window.Complete = false; return; }
        window.EndedAt = at; window.EndEventId = eventId;
        var end = await BoundaryAsync(window.PhysicalHostKey, eventId, at, ct);
        var details = Read(window); window.Complete &= end.State == "known";
        window.DetailsJson = JsonSerializer.Serialize(details with { EndedAt = at, State = "closed", End = end }, PhoneHomeFraming.Json);
        var hour = HostLoadMetrics.Hour(at);
        var affected = await db.HostLoadTaskMetricBuckets.Where(b => b.WindowId == window.Id && b.HourUtc >= hour).ToListAsync(ct);
        db.HostLoadTaskMetricBuckets.RemoveRange(affected);
        if (time.GetUtcNow() - at > TimeSpan.FromDays(7)) { window.Complete = false; return; }
        var retained = await db.HostLoadSamples.AsNoTracking().Where(r => r.IsPrimary && r.DerivedAt != null
            && r.PhysicalHostKey == window.PhysicalHostKey && r.BootId == window.RunnerBootId
            && r.SampledAt >= hour && r.SampledAt > window.StartedAt && r.SampledAt < at).Take(2000).ToListAsync(ct);
        // Deleted tracked buckets are not eligible for reuse by this rebuilding pass.
        await db.SaveChangesAsync(ct);
        await AddToBucketsAsync([window], retained, ct);
    }
    public async Task<HostLoadBoundary> BoundaryAsync(string host, Guid eventId, DateTimeOffset at, CancellationToken ct)
    {
        var candidates = await db.HostLoadSamples.AsNoTracking().Where(s => s.IsPrimary && s.PhysicalHostKey == host
            && s.SampledAt >= at.AddSeconds(-15) && s.SampledAt <= at.AddSeconds(15)).OrderBy(s => s.SampledAt).Take(500).ToListAsync(ct);
        var nearest = candidates.OrderBy(s => (s.SampledAt - at).Duration()).ThenBy(s => s.SampledAt)
            .ThenBy(s => s.RunnerStoreId).ThenBy(s => s.BootId).ThenBy(s => s.Sequence).FirstOrDefault();
        return nearest is null ? new(eventId, at, null, null, null, "missing")
            : new(eventId, at, JsonSerializer.Deserialize<HostLoadSample>(nearest.PayloadJson, PhoneHomeFraming.Json)!.Source,
                nearest.SampledAt, (nearest.SampledAt - at).Duration().TotalSeconds, "known");
    }
    internal static HostLoadTaskWindowDto Read(AgentTaskLoadWindow window) => JsonSerializer.Deserialize<HostLoadTaskWindowDto>(window.DetailsJson, PhoneHomeFraming.Json)!;
    private static DateTimeOffset Utc(DateTime at) => new(DateTime.SpecifyKind(at, DateTimeKind.Utc));
}
