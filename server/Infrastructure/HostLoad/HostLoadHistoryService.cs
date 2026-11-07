using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.HostLoad;

public sealed class HostLoadHistoryService(AppDbContext db, ISessionRunnerDirectory directory,
    IOptions<HostLoadHistorySettings> settings, IHostLoadCapacityReader capacity, HostLoadRecommendation recommendation,
    HostLoadShadowCache shadows, TimeProvider time)
{
    private sealed record Cursor(string Query, string Snapshot, long MaxId, DateTimeOffset At, long Id);
    public async Task<HostLoadCurrentDto> CurrentAsync(string hostId, Guid? projectId, AgentTaskRole? role, CancellationToken ct)
    {
        var runner = Known(hostId); var now = time.GetUtcNow(); var binding = Binding(runner, now);
        var host = binding?.PhysicalHostKey;
        var latestRow = host is null ? null : await db.HostLoadSamples.AsNoTracking().Where(s => s.PhysicalHostKey == host && s.IsPrimary)
            .OrderByDescending(s => s.SampledAt).ThenByDescending(s => s.Id).FirstOrDefaultAsync(ct);
        var current = latestRow is null ? null : Decode(latestRow);
        var raw = host is null ? [] : await db.HostLoadSamples.AsNoTracking().Where(s => s.PhysicalHostKey == host && s.IsPrimary
            && s.SampledAt >= now.AddHours(-1) && s.SampledAt < now).OrderBy(s => s.SampledAt).Take(2000).ToListAsync(ct);
        var decoded = raw.Select(r => (Row: r, Values: HostLoadMetrics.Values(Decode(r)))).ToArray();
        var names = decoded.SelectMany(r => r.Values.Keys).Append("cpuPercent").Distinct(StringComparer.Ordinal).ToArray();
        var lastHour = names.ToDictionary(name => name, name => new HostLoadAggregation().Exact(decoded.Select(s =>
            new HostLoadMetricPoint(s.Row.Id.ToString(CultureInfo.InvariantCulture), s.Row.SampledAt, s.Values.GetValueOrDefault(name))), now.AddHours(-1), now));
        var fifteen = new HostLoadAggregation().Exact(decoded.Select(s => new HostLoadMetricPoint(s.Row.Id.ToString(), s.Row.SampledAt, s.Values.GetValueOrDefault("cpuPercent"))), now.AddSeconds(-15), now);
        var sixty = new HostLoadAggregation().Exact(decoded.Select(s => new HostLoadMetricPoint(s.Row.Id.ToString(), s.Row.SampledAt, s.Values.GetValueOrDefault("cpuPercent"))), now.AddSeconds(-60), now);
        HostLoadCapacityInput limits;
        try { limits = await capacity.ReadAsync(runner, projectId, role, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { limits = new(null, null, null, null, null); }
        var open = host is null ? [] : await db.AgentTaskLoadWindows.AsNoTracking().Where(w => w.PhysicalHostKey == host && w.EndedAt == null).Take(2000).ToListAsync(ct);
        var launches = open.Select(w => new HostLoadLaunchObservation(w.TaskId, w.Attempt, w.WindowOrdinal, w.StartedAt,
            current is not null && current.SampledAt >= w.StartedAt.AddSeconds(15) && sixty.ValidCount == 12, false)).ToArray();
        var advice = recommendation.Evaluate(new(current?.PhysicalHostKey, host, current?.SampledAt, current?.CpuPercent,
            fifteen.Avg, sixty.Avg, (int)fifteen.ValidCount, (int)fifteen.ExpectedCount, (int)sixty.ValidCount, (int)sixty.ExpectedCount,
            binding is not null && latestRow?.SourceEpoch == binding.SourceEpoch), limits, launches, now, projectId is not null, role is not null);
        if (projectId is null && role is null) shadows.Set(runner, advice);
        var authority = current?.Authority ?? HostAdmissionSnapshot.Unknown(host);
        var age = current is null || current.SampledAt > now.AddSeconds(1) ? (double?)null : Math.Max(0, (now - current.SampledAt).TotalSeconds);
        return new(1, runner, host, now, current, age, lastHour, lastHour["cpuPercent"].Coverage,
            new(authority.Mode, authority.Revision), authority, advice.Decision, advice.Recommendation);
    }

    public async Task<HostLoadHistoryDto> HistoryAsync(string hostId, string? fromText, string? toText,
        string? resolution, string? limitText, string? cursorText, CancellationToken ct)
    {
        var runner = Known(hostId); var now = time.GetUtcNow(); var binding = Binding(runner, now);
        resolution ??= "raw";
        var from = ParseTime(fromText, now.AddHours(-1)); var to = ParseTime(toText, now);
        var limit = 720;
        if (limitText is not null && (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit <= 0)) throw InvalidQuery();
        limit = Math.Min(limit, 2000);
        if (resolution is not ("raw" or "hour") || to <= from || to - from > TimeSpan.FromDays(resolution == "raw" ? 7 : 90)) throw InvalidQuery();
        var query = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runner + "|" + binding?.PhysicalHostKey + "|" + binding?.SourceEpoch
            + "|" + binding?.Revision + "|" + from.ToString("O") + "|" + to.ToString("O") + "|" + resolution)));
        Cursor? cursor = cursorText is null ? null : DecodeCursor(cursorText, query);
        if (binding is null) return new(1, runner, from, to, resolution, [], null, false, ["binding-unknown"]);
        // Capture actual transaction membership as well as a keyset frontier. A sequence allocated
        // before page one but committed afterwards is excluded. CreatedTransactionId is immutable.
        var snapshot = cursor?.Snapshot ?? await db.Database.SqlQueryRaw<string>("SELECT pg_current_snapshot()::text AS \"Value\"").SingleAsync(ct);
        if (snapshot.Length > 2048) throw InvalidCursor();
        var host = binding.PhysicalHostKey;
        var items = new List<object>(); string? next = null; bool more; bool covered;
        if (resolution == "raw")
        {
            var rows = db.HostLoadSamples.FromSqlInterpolated($"SELECT * FROM \"HostLoadSamples\" WHERE pg_visible_in_snapshot(\"CreatedTransactionId\"::text::xid8, {snapshot}::pg_snapshot)")
                .AsNoTracking().Where(s => s.IsPrimary && s.PhysicalHostKey == host && s.SampledAt >= from && s.SampledAt < to);
            var maximum = cursor?.MaxId ?? await rows.Select(s => (long?)s.Id).MaxAsync(ct) ?? 0;
            rows = rows.Where(s => s.Id <= maximum);
            var distinctTimes = await rows.Select(s => s.SampledAt).Distinct().LongCountAsync(ct);
            covered = distinctTimes >= HostLoadAggregation.ExpectedCount(from, to);
            if (cursor is not null) rows = rows.Where(s => s.SampledAt > cursor.At || (s.SampledAt == cursor.At && s.Id > cursor.Id));
            var page = await rows.OrderBy(s => s.SampledAt).ThenBy(s => s.Id).Take(limit + 1).ToListAsync(ct);
            more = page.Count > limit; page = page.Take(limit).ToList();
            items.AddRange(page.Select(Stored));
            if (more) { var last = page[^1]; next = EncodeCursor(new(query, snapshot, maximum, last.SampledAt, last.Id)); }
        }
        else
        {
            var rows = db.HostLoadHourlyRollups.FromSqlInterpolated($"SELECT * FROM \"HostLoadHourlyRollups\" WHERE pg_visible_in_snapshot(\"CreatedTransactionId\"::text::xid8, {snapshot}::pg_snapshot)")
                .AsNoTracking().Where(s => s.PhysicalHostKey == host && s.HourUtc >= from && s.HourUtc < to);
            var maximum = cursor?.MaxId ?? await rows.Select(s => (long?)s.Id).MaxAsync(ct) ?? 0;
            rows = rows.Where(s => s.Id <= maximum);
            if (cursor is not null) rows = rows.Where(s => s.HourUtc > cursor.At || (s.HourUtc == cursor.At && s.Id > cursor.Id));
            var page = await rows.OrderBy(s => s.HourUtc).ThenBy(s => s.Id).Take(limit + 1).ToListAsync(ct);
            more = page.Count > limit; page = page.Take(limit).ToList();
            items.AddRange(page.Select(s => (object)new { s.PhysicalHostKey, s.SourceEpoch, s.HourUtc, s.Metric,
                Summary = JsonSerializer.Deserialize<HostLoadMetricSummary>(s.StatisticsJson, PhoneHomeFraming.Json) }));
            // A paged summary is explicitly partial; do not claim one sparse bucket covers
            // a whole requested interval merely because the spool had no known losses.
            covered = cursor is null && page.Where(s => s.Metric == "cpuPercent").Sum(s =>
                JsonSerializer.Deserialize<HostLoadMetricSummary>(s.StatisticsJson, PhoneHomeFraming.Json)!.ValidCount)
                >= HostLoadAggregation.ExpectedCount(from, to);
            if (more) { var last = page[^1]; next = EncodeCursor(new(query, snapshot, maximum, last.HourUtc, last.Id)); }
        }
        var cursors = await db.HostLoadIngestCursors.AsNoTracking().Where(c => c.RunnerId == runner).ToListAsync(ct);
        var complete = items.Count > 0 && covered && cursors is [{ Complete: true }] && !more;
        return new(1, runner, from, to, resolution, items, next, complete,
            complete ? [] : items.Count == 0 ? ["missing"] : ["partial"]);
    }

    public async Task<HostLoadTaskDto> TaskAsync(Guid taskId, int? requestedAttempt, CancellationToken ct)
    {
        var task = await db.AgentTasks.AsNoTracking().Where(t => t.Id == taskId).Select(t => new { t.Attempt }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Agent task", taskId.ToString());
        var attempt = requestedAttempt ?? task.Attempt;
        if (attempt < 1 || attempt > task.Attempt) throw new ValidationException("attempt", "Invalid task attempt.");
        var rows = await db.AgentTaskLoadWindows.AsNoTracking().Where(w => w.TaskId == taskId && w.Attempt == attempt)
            .OrderBy(w => w.WindowOrdinal).ToListAsync(ct);
        var ids = rows.Select(w => w.Id).ToArray();
        var buckets = await db.HostLoadTaskMetricBuckets.AsNoTracking().Where(b => ids.Contains(b.WindowId)).ToListAsync(ct);
        var result = new List<HostLoadTaskWindowDto>();
        foreach (var row in rows)
        {
            var details = HostLoadTaskWindowObserver.Read(row);
            var end = row.EndedAt ?? time.GetUtcNow(); var expected = HostLoadAggregation.ExpectedCount(row.StartedAt.AddTicks(1), end);
            var metrics = new Dictionary<string, HostLoadMetricSummary>(StringComparer.Ordinal);
            foreach (var group in buckets.Where(b => b.WindowId == row.Id).GroupBy(b => b.Metric))
            {
                var merged = new HostLoadHistogram(HostLoadMetrics.BinWidth(group.Key));
                foreach (var bucket in group) merged.Merge(HostLoadDerivedService.Restore(bucket.HistogramJson));
                metrics[group.Key] = merged.Summary(expected);
            }
            metrics.TryAdd("cpuPercent", new(null, null, null, null, null, 0, expected, 0, false, false, null));
            // A raw-backed short task is exact. Long-task histograms keep their declared error.
            if (row.StartedAt >= time.GetUtcNow().AddDays(-7))
            {
                var raw = await db.HostLoadSamples.AsNoTracking().Where(s => s.IsPrimary && s.PhysicalHostKey == row.PhysicalHostKey
                    && s.BootId == row.RunnerBootId && s.SampledAt > row.StartedAt && s.SampledAt < end).Take(121000).ToListAsync(ct);
                var decoded = raw.Select(r => (r.SampledAt, r.Id, Values: HostLoadMetrics.Values(Decode(r)))).ToArray();
                foreach (var metric in metrics.Keys.ToArray())
                {
                    var exact = new HostLoadAggregation().Exact(decoded.Select(r => new HostLoadMetricPoint(r.Id.ToString(), r.SampledAt, r.Values.GetValueOrDefault(metric))), row.StartedAt.AddTicks(1), end);
                    if (exact.ValidCount == metrics[metric].ValidCount) metrics[metric] = exact;
                }
            }
            var overlap = await db.AgentTaskLoadWindows.AsNoTracking().CountAsync(w => w.PhysicalHostKey == row.PhysicalHostKey
                && w.StartedAt < end && (w.EndedAt == null || w.EndedAt > row.StartedAt), ct);
            result.Add(details with { WindowOrdinal = row.WindowOrdinal, RunnerBootId = row.RunnerBootId, During = metrics, OverlapCount = overlap });
        }
        var complete = rows.Count > 0 && rows.All(w => w.Complete);
        return new(1, taskId, attempt, result, complete, complete ? [] : ["event-or-sample-gap"]);
    }

    private string Known(string host)
    {
        var canonical = HostLoadCounterCache.Canonical(host);
        if (!directory.KnownRunnerIds.Select(HostLoadCounterCache.Canonical).Contains(canonical, StringComparer.Ordinal)) throw new NotFoundException("Host", host);
        return canonical;
    }
    private HostLoadBinding? Binding(string runner, DateTimeOffset at)
    {
        var bindings = settings.Value.Bindings.Where(b => HostLoadCounterCache.Canonical(b.RunnerId) == runner
            && at >= b.From && (b.To is null || at < b.To)).ToArray();
        return bindings.Length == 1 ? bindings[0] : null;
    }
    private static HostLoadSample Decode(Domain.Entities.HostLoadSampleRecord row) =>
        JsonSerializer.Deserialize<HostLoadSample>(row.PayloadJson, PhoneHomeFraming.Json)! with { PhysicalHostKey = row.PhysicalHostKey, SourceEpoch = row.SourceEpoch };
    private static object Stored(Domain.Entities.HostLoadSampleRecord row)
    {
        var node = JsonSerializer.SerializeToNode(Decode(row), PhoneHomeFraming.Json)!.AsObject();
        node["ingestedAt"] = JsonValue.Create(row.IngestedAt);
        node["counters"] = row.CountersJson is null ? null : JsonNode.Parse(row.CountersJson);
        return node;
    }
    private static DateTimeOffset ParseTime(string? text, DateTimeOffset fallback)
    {
        if (text is null) return fallback;
        if (!Regex.IsMatch(text, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)) throw InvalidQuery();
        return result.ToUniversalTime();
    }
    private static Cursor DecodeCursor(string text, string query)
    {
        try
        {
            if (text.Length > 8192) throw InvalidCursor();
            var value = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(text), PhoneHomeFraming.Json) ?? throw InvalidCursor();
            if (value.Query != query || value.MaxId < 0 || value.Id < 0 || !Regex.IsMatch(value.Snapshot, @"\A[0-9]+:[0-9]+:(?:[0-9]+(?:,[0-9]+)*)?\z", RegexOptions.CultureInvariant)) throw InvalidCursor();
            return value;
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { throw InvalidCursor(); }
    }
    private static string EncodeCursor(Cursor cursor) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, PhoneHomeFraming.Json));
    private static ValidationException InvalidQuery() => new("history", "Invalid host load history window, resolution or limit.");
    private static ValidationException InvalidCursor() => new("cursor", "Invalid host load history cursor.");
}
