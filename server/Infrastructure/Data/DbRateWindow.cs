namespace Antiphon.Server.Infrastructure.Data;

/// <summary>
/// Pure minute ring for <c>pg_stat_statements</c> deltas. The first observation is a baseline.
/// A stats reset, a dealloc growth, a missing id, or a backwards counter is a gap: the new
/// snapshot becomes the next baseline and no negative rate is kept. Labels are redacted before
/// they are stored. The ring keeps per-minute totals plus a bounded top of statements and call
/// sites so 1,440 minutes stay near the 3 MB budget.
/// </summary>
public sealed class DbRateWindow
{
    public const int RingMinutes = 1440;
    public const int MaxLabels = 2000;
    public const int MaxStatementsPerMinute = 15;
    public const int MaxOperationsPerMinute = 32;
    public const int MaxSitesPerMinute = 16;
    public const string GapUnavailable = "unavailable";
    public const string GapExtension = "extension-missing";
    public const string GapReset = "stats-reset";
    public const string GapDealloc = "dealloc";

    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Queue<Minute> _minutes = new();
    private readonly Dictionary<long, string> _labels = new();
    private PgssSnapshot? _previous;
    private DateTimeOffset _previousAt;
    private ProcessSample? _previousProcess;

    public DbRateWindow(int retentionMinutes = RingMinutes)
    {
        _capacity = retentionMinutes is >= 1 and <= RingMinutes ? retentionMinutes : RingMinutes;
    }

    public int LabelCount { get { lock (_gate) return _labels.Count; } }
    public int MinuteCount { get { lock (_gate) return _minutes.Count; } }

    internal bool KnowsLabel(long id) { lock (_gate) return _labels.ContainsKey(id); }

    internal string? CachedLabel(long id)
    {
        lock (_gate) return _labels.TryGetValue(id, out var label) ? label : null;
    }

    public void RecordGap(DateTimeOffset at, string reason)
    {
        lock (_gate)
            Add(new Minute { At = at, IsGap = true, Reason = string.IsNullOrEmpty(reason) ? GapUnavailable : reason });
    }

    public void Observe(DateTimeOffset at, PgssSnapshot snapshot, IReadOnlyDictionary<long, string>? texts = null,
        ProcessSample? process = null)
    {
        lock (_gate)
        {
            Remember(texts);
            if (_previous is null)
            {
                _previous = snapshot;
                _previousAt = at;
                if (process is not null) _previousProcess = process;
                return;
            }

            var elapsed = (at - _previousAt).TotalSeconds;
            var reason = GapReason(elapsed, _previous, snapshot);
            var minute = new Minute { At = at, ElapsedSeconds = elapsed <= 0 ? 0 : elapsed, IsGap = reason is not null, Reason = reason ?? "" };
            if (reason is null)
                Fill(minute, _previous, snapshot, texts, process);
            Add(minute);
            _previous = snapshot;
            _previousAt = at;
            if (process is not null) _previousProcess = process;
        }
    }

    public DbRateReport Report(int minutes, DateTimeOffset now)
    {
        if (minutes < 1) minutes = 1;
        if (minutes > RingMinutes) minutes = RingMinutes;
        lock (_gate)
        {
            var rollups = new Rollup[4];
            var index = 0;
            foreach (var window in new[] { 1, 5, 15, 60 })
                rollups[index++] = Roll(window, now);
            var selected = Select(minutes, now);
            var elapsed = Elapsed(selected);
            var series = new List<MinuteRate>(selected.Count);
            foreach (var minute in selected)
            {
                if (minute.IsGap || minute.ElapsedSeconds <= 0) continue;
                series.Add(new MinuteRate(minute.At,
                    Rate(minute.Calls, minute.ElapsedSeconds),
                    Rate(minute.TotalMs, minute.ElapsedSeconds),
                    Rate(minute.Rows, minute.ElapsedSeconds)));
            }
            return new DbRateReport(
                rollups,
                series,
                Tops(selected, elapsed),
                Operations(selected, elapsed),
                selected.Where(m => m.IsGap).Select(m => new RateGap(m.At, m.Reason)).ToArray(),
                new CommandRate(Rate(selected.Sum(m => m.CmdTotal), elapsed), Rate(selected.Sum(m => m.CmdUntagged), elapsed)),
                Sites(selected),
                new TransactionRate(
                    Rate(selected.Sum(m => m.Begin), elapsed),
                    Rate(selected.Sum(m => m.Commit), elapsed),
                    Rate(selected.Sum(m => m.Rollback), elapsed),
                    Rate(selected.Sum(m => m.Savepoint), elapsed),
                    Rate(selected.Sum(m => m.Release), elapsed)));
        }
    }

    private void Remember(IReadOnlyDictionary<long, string>? texts)
    {
        if (texts is null || texts.Count == 0) return;
        foreach (var id in texts.Keys.OrderBy(id => id))
        {
            if (_labels.ContainsKey(id)) continue;
            if (_labels.Count >= MaxLabels) break;
            _labels[id] = PgStatLabel.FromQuery(texts[id]);
        }
    }

    private static string? GapReason(double elapsed, PgssSnapshot previous, PgssSnapshot next)
    {
        if (elapsed <= 0) return GapUnavailable;
        if (!string.Equals(previous.StatsReset, next.StatsReset, StringComparison.Ordinal)) return GapReset;
        if (next.Dealloc > previous.Dealloc) return GapDealloc;
        if (next.Dealloc < previous.Dealloc) return GapReset;
        var nextById = next.Rows.GroupBy(r => r.QueryId).ToDictionary(g => g.Key, g => g.Last());
        foreach (var row in previous.Rows)
        {
            if (!nextById.TryGetValue(row.QueryId, out var after)) return GapReset;
            if (after.Calls < row.Calls || after.TotalExecMs < row.TotalExecMs || after.Rows < row.Rows)
                return GapReset;
        }
        return null;
    }

    private void Fill(Minute minute, PgssSnapshot previous, PgssSnapshot next,
        IReadOnlyDictionary<long, string>? texts, ProcessSample? process)
    {
        var before = previous.Rows.GroupBy(r => r.QueryId).ToDictionary(g => g.Key, g => g.Last());
        var statements = new List<Stmt>(next.Rows.Count);
        var ops = new Dictionary<string, Acc>(StringComparer.Ordinal);
        foreach (var row in next.Rows)
        {
            before.TryGetValue(row.QueryId, out var prior);
            var calls = row.Calls - (prior?.Calls ?? 0);
            var ms = row.TotalExecMs - (prior?.TotalExecMs ?? 0);
            var rows = row.Rows - (prior?.Rows ?? 0);
            if (calls <= 0 && ms <= 0 && rows <= 0) continue;
            if (calls < 0) calls = 0;
            if (ms < 0) ms = 0;
            if (rows < 0) rows = 0;
            var label = LabelFor(row.QueryId, texts);
            minute.Calls += calls;
            minute.TotalMs += ms;
            minute.Rows += rows;
            statements.Add(new Stmt { Id = row.QueryId, Calls = calls, TotalMs = ms, Rows = rows, Label = label });
            var op = PgStatLabel.Operation(label);
            if (!ops.TryGetValue(op, out var acc)) ops[op] = acc = new Acc();
            acc.Calls += calls;
            acc.Ms += ms;
            acc.Rows += rows;
        }
        statements.Sort(static (a, b) => b.Calls != a.Calls ? b.Calls.CompareTo(a.Calls) : string.CompareOrdinal(a.Label, b.Label));
        if (statements.Count > MaxStatementsPerMinute)
            statements.RemoveRange(MaxStatementsPerMinute, statements.Count - MaxStatementsPerMinute);
        minute.Statements = statements;
        minute.Operations = Fold(ops);
        FillProcess(minute, process);
    }

    private Dictionary<string, Acc> Fold(Dictionary<string, Acc> ops)
    {
        if (ops.Count <= MaxOperationsPerMinute) return ops;
        var ordered = ops.OrderByDescending(p => p.Value.Calls).ToList();
        var kept = new Dictionary<string, Acc>(StringComparer.Ordinal);
        if (!ops.TryGetValue("untagged", out var untagged)) untagged = new Acc();
        kept["untagged"] = untagged;
        var room = MaxOperationsPerMinute - 1;
        foreach (var pair in ordered)
        {
            if (pair.Key == "untagged") continue;
            if (room > 0)
            {
                kept[pair.Key] = pair.Value;
                room--;
            }
            else
            {
                untagged.Calls += pair.Value.Calls;
                untagged.Ms += pair.Value.Ms;
                untagged.Rows += pair.Value.Rows;
            }
        }
        return kept;
    }

    private void FillProcess(Minute minute, ProcessSample? process)
    {
        if (process is not { } next || _previousProcess is not { } prev) return;
        minute.CmdTotal = NonNeg(next.CommandTotal - prev.CommandTotal);
        minute.CmdUntagged = NonNeg(next.CommandUntagged - prev.CommandUntagged);
        minute.Begin = NonNeg(next.Begin - prev.Begin);
        minute.Commit = NonNeg(next.Commit - prev.Commit);
        minute.Rollback = NonNeg(next.Rollback - prev.Rollback);
        minute.Savepoint = NonNeg(next.Savepoint - prev.Savepoint);
        minute.Release = NonNeg(next.Release - prev.Release);
        minute.Unknown = NonNeg(next.Unknown - prev.Unknown);
        var overflow = NonNeg(next.Overflow - prev.Overflow);
        var deltas = new List<SiteDelta>();
        long siteSum = 0;
        foreach (var pair in next.Sites)
        {
            prev.Sites.TryGetValue(pair.Key, out var old);
            var delta = NonNeg(pair.Value - old);
            if (delta <= 0) continue;
            siteSum += delta;
            deltas.Add(new SiteDelta(pair.Key, delta));
        }
        if (overflow > 0) deltas.Add(new SiteDelta("overflow", overflow));
        deltas.Sort(static (a, b) => b.Samples != a.Samples ? b.Samples.CompareTo(a.Samples) : string.CompareOrdinal(a.Site, b.Site));
        if (deltas.Count > MaxSitesPerMinute)
            deltas.RemoveRange(MaxSitesPerMinute, deltas.Count - MaxSitesPerMinute);
        minute.Sites = deltas;
        minute.Sampled = siteSum + minute.Unknown + overflow;
    }

    private string LabelFor(long id, IReadOnlyDictionary<long, string>? texts)
    {
        if (_labels.TryGetValue(id, out var cached)) return cached;
        if (texts is not null && texts.TryGetValue(id, out var raw)) return PgStatLabel.FromQuery(raw);
        return "untagged";
    }

    private void Add(Minute minute)
    {
        _minutes.Enqueue(minute);
        while (_minutes.Count > _capacity) _minutes.Dequeue();
    }

    private List<Minute> Select(int minutes, DateTimeOffset now)
    {
        var start = now.AddMinutes(-minutes);
        var selected = new List<Minute>();
        foreach (var minute in _minutes)
            if (minute.At > start && minute.At <= now) selected.Add(minute);
        return selected;
    }

    private Rollup Roll(int minutes, DateTimeOffset now)
    {
        var selected = Select(minutes, now);
        var elapsed = Elapsed(selected);
        long calls = 0, rows = 0;
        double ms = 0;
        foreach (var minute in selected)
        {
            if (minute.IsGap) continue;
            calls += minute.Calls;
            ms += minute.TotalMs;
            rows += minute.Rows;
        }
        return new Rollup(minutes, Rate(calls, elapsed), Rate(ms, elapsed), Rate(rows, elapsed));
    }

    private static double Elapsed(List<Minute> minutes)
    {
        double elapsed = 0;
        foreach (var minute in minutes)
            if (!minute.IsGap && minute.ElapsedSeconds > 0) elapsed += minute.ElapsedSeconds;
        return elapsed;
    }

    private static TopStatements Tops(List<Minute> minutes, double elapsed)
    {
        var byId = new Dictionary<long, Agg>();
        foreach (var minute in minutes)
        {
            if (minute.IsGap) continue;
            foreach (var stmt in minute.Statements)
            {
                if (!byId.TryGetValue(stmt.Id, out var agg)) byId[stmt.Id] = agg = new Agg { Label = stmt.Label };
                agg.Calls += stmt.Calls;
                agg.Ms += stmt.TotalMs;
                agg.Rows += stmt.Rows;
                if (agg.Label.Length == 0) agg.Label = stmt.Label;
            }
        }
        var rates = byId.Values.Select(a => new StatementRate(a.Label, a.Calls, Rate(a.Calls, elapsed),
            Rate(a.Ms, elapsed), Rate(a.Rows, elapsed), a.Calls > 0 ? a.Ms / a.Calls : 0)).ToList();
        var byCalls = rates.OrderByDescending(r => r.Calls).ThenBy(r => r.Label, StringComparer.Ordinal).Take(10).ToArray();
        var byTime = rates.OrderByDescending(r => r.DbMsPerSecond).ThenBy(r => r.Label, StringComparer.Ordinal).Take(5).ToArray();
        return new TopStatements(byCalls, byTime);
    }

    private static OperationRate[] Operations(List<Minute> minutes, double elapsed)
    {
        var ops = new Dictionary<string, Acc>(StringComparer.Ordinal);
        foreach (var minute in minutes)
        {
            if (minute.IsGap) continue;
            foreach (var pair in minute.Operations)
            {
                if (!ops.TryGetValue(pair.Key, out var acc)) ops[pair.Key] = acc = new Acc();
                acc.Calls += pair.Value.Calls;
                acc.Ms += pair.Value.Ms;
                acc.Rows += pair.Value.Rows;
            }
        }
        return ops.Select(p => new OperationRate(p.Key, Rate(p.Value.Calls, elapsed), Rate(p.Value.Ms, elapsed),
                Rate(p.Value.Rows, elapsed), p.Value.Calls, p.Value.Calls > 0 ? p.Value.Ms / p.Value.Calls : 0))
            .OrderByDescending(r => r.Calls).ThenBy(r => r.Operation, StringComparer.Ordinal).ToArray();
    }

    private static CallSiteReport Sites(List<Minute> minutes)
    {
        long sampled = 0, unknown = 0;
        var sites = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var minute in minutes)
        {
            if (minute.IsGap) continue;
            sampled += minute.Sampled;
            unknown += minute.Unknown;
            foreach (var site in minute.Sites)
                sites[site.Site] = sites.TryGetValue(site.Site, out var n) ? n + site.Samples : site.Samples;
        }
        var top = sites.Select(p => new CallSiteShare(p.Key, p.Value, sampled > 0 ? (double)p.Value / sampled : 0))
            .OrderByDescending(s => s.Samples).ThenBy(s => s.Site, StringComparer.Ordinal).Take(30).ToArray();
        return new CallSiteReport(sampled > 0 ? (double)unknown / sampled : 0, top);
    }

    private static double Rate(double amount, double elapsed) => elapsed > 0 ? amount / elapsed : 0;
    private static long NonNeg(long value) => value < 0 ? 0 : value;

    private sealed class Minute
    {
        public DateTimeOffset At;
        public double ElapsedSeconds;
        public bool IsGap;
        public string Reason = "";
        public long Calls;
        public double TotalMs;
        public long Rows;
        public List<Stmt> Statements = [];
        public Dictionary<string, Acc> Operations = new(StringComparer.Ordinal);
        public long CmdTotal, CmdUntagged;
        public long Begin, Commit, Rollback, Savepoint, Release;
        public long Sampled, Unknown;
        public List<SiteDelta> Sites = [];
    }

    private sealed class Stmt
    {
        public long Id;
        public long Calls;
        public double TotalMs;
        public long Rows;
        public string Label = "";
    }

    private sealed class Acc
    {
        public long Calls;
        public double Ms;
        public long Rows;
    }

    private sealed class Agg
    {
        public string Label = "";
        public long Calls;
        public double Ms;
        public long Rows;
    }

    private readonly record struct SiteDelta(string Site, long Samples);
}
