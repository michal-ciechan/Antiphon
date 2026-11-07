using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public interface IHostLoadHistorySource
{
    HostLoadHistoryPage ReadHistory(string? after, int limit);
}

/// <summary>
/// Flushed JSONL chunks and an atomically replaced acknowledgement ledger. Only the trusted
/// recipient's committed cursor acknowledges a page. All filesystem work belongs to the append
/// worker/read surface, never the live sampler. Reopening does not change stored sample identity.
/// </summary>
public sealed class HostLoadSpool : IHostLoadHistorySource
{
    private sealed record Entry(long Ordinal, HostLoadSample Sample);
    private sealed record Cursor(Guid Store, long From, long Through);
    private sealed record Ledger(Guid Store, string Secret, long Acknowledged, long Next,
        List<HostLoadGap> Gaps, bool Incomplete);
    private readonly object _gate = new();
    private readonly string _root;
    private readonly HostStatsSettings _settings;
    private readonly TimeProvider _time;
    private readonly List<(string Path, List<Entry> Entries)> _chunks = [];
    private Ledger _ledger;
    private long _sequence;
    private readonly Guid _boot;
    private readonly Action<string, HostLoadSource>? _appendObservation;
    public Guid RunnerStoreId => _ledger.Store;
    public Guid BootId => _boot;
    public long LostSamples { get { lock (_gate) return _ledger.Gaps.Sum(g => g.ToSequence - g.FromSequence + 1); } }
    public long Bytes { get { lock (_gate) return Files().Sum(f => f.Length); } }
    public HostLoadSource NextSource() => new(_settings.RunnerId, RunnerStoreId, _boot, Interlocked.Increment(ref _sequence));

    public HostLoadSpool(string path, HostStatsSettings settings, TimeProvider time, Guid? boot = null,
        Action<string, HostLoadSource>? appendObservation = null)
    {
        _root = Path.GetFullPath(path);
        _settings = settings;
        _time = time;
        _boot = boot ?? Guid.NewGuid();
        _appendObservation = appendObservation;
        Directory.CreateDirectory(_root);
        if ((File.GetAttributes(_root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("host_load_spool_link");
        var state = Path.Combine(_root, "cursor.json");
        _ledger = File.Exists(state)
            ? JsonSerializer.Deserialize<Ledger>(File.ReadAllBytes(state), PhoneHomeFraming.Json)
                ?? throw new IOException("host_load_spool_ledger")
            : new(Guid.NewGuid(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), 0, 0, [], false);
        if (_ledger.Store == Guid.Empty || _ledger.Acknowledged < 0 || _ledger.Next < _ledger.Acknowledged)
            throw new IOException("host_load_spool_ledger");
        foreach (var file in Directory.GetFiles(_root, "chunk-*.jsonl").Order(StringComparer.Ordinal))
            Recover(file);
        _ledger = _ledger with { Next = Math.Max(_ledger.Next, _chunks.SelectMany(c => c.Entries).Select(e => e.Ordinal).DefaultIfEmpty().Max()) };
        Persist();
        EnforceCaps();
    }

    public void Append(HostLoadSample sample)
    {
        lock (_gate)
        {
            Validate(sample);
            var prior = _chunks.SelectMany(c => c.Entries).FirstOrDefault(e => e.Sample.Source == sample.Source);
            if (prior is not null)
            {
                if (!JsonSerializer.SerializeToUtf8Bytes(prior.Sample, PhoneHomeFraming.Json)
                    .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(sample, PhoneHomeFraming.Json)))
                    throw new InvalidOperationException("host_load_identity_conflict");
                return;
            }
            var entry = new Entry(checked(_ledger.Next + 1), sample);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, PhoneHomeFraming.Json);
            if (bytes.Length > 960 * 1024) throw new ArgumentException("host_load_record_too_large");
            var last = _chunks.LastOrDefault();
            if (last.Entries is null || last.Entries.Count >= 120
                || new FileInfo(last.Path).Length + bytes.Length + 1 > Math.Min(1024 * 1024, _settings.SpoolMaxBytes / 4))
            {
                last = (Path.Combine(_root, "chunk-" + entry.Ordinal.ToString("D20") + ".jsonl"), []);
                _chunks.Add(last);
            }
            using (var stream = new FileStream(last.Path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes);
                stream.WriteByte((byte)'\n');
                _appendObservation?.Invoke("before-flush", sample.Source);
                stream.Flush(flushToDisk: true);
                _appendObservation?.Invoke("after-flush", sample.Source);
            }
            last.Entries.Add(entry); // Never advertise a record before its durable append boundary.
            _ledger = _ledger with { Next = entry.Ordinal };
            Persist();
            EnforceCaps();
        }
    }

    public void RecordLoss(HostLoadSource source, string reason = "gap")
    {
        lock (_gate) { AddGap(source, reason); Persist(); EnforceCaps(); }
    }

    public HostLoadHistoryPage ReadHistory(string? after, int limit)
    {
        if (limit is < 1 or > 120) throw new ArgumentException("invalid_host_load_query");
        lock (_gate)
        {
            EnforceCaps();
            if (after is not null)
            {
                var cursor = Decode(after);
                if (cursor.Store != RunnerStoreId || cursor.From > _ledger.Acknowledged
                    || cursor.Through < cursor.From || cursor.Through > _ledger.Next)
                    throw new ArgumentException("invalid_host_load_cursor");
                if (cursor.Through > _ledger.Acknowledged)
                {
                    // Ledger replacement must finish before any acknowledged chunk is removed.
                    _ledger = _ledger with { Acknowledged = cursor.Through };
                    Persist();
                    RemoveAcknowledged();
                }
            }
            var pending = _chunks.SelectMany(c => c.Entries).Where(e => e.Ordinal > _ledger.Acknowledged).ToArray();
            var selected = new List<Entry>();
            var next = (string?)null;
            foreach (var entry in pending.Take(limit))
            {
                selected.Add(entry);
                var candidate = Encode(new(RunnerStoreId, _ledger.Acknowledged, entry.Ordinal));
                var page = Page(selected, candidate, selected.Count < pending.Length);
                if (JsonSerializer.SerializeToUtf8Bytes(page, PhoneHomeFraming.Json).Length > 1024 * 1024)
                { selected.RemoveAt(selected.Count - 1); break; }
                next = candidate;
            }
            return Page(selected, next, selected.Count < pending.Length);
        }
    }

    private HostLoadHistoryPage Page(List<Entry> selected, string? cursor, bool more) =>
        new(1, new(_settings.RunnerId, RunnerStoreId, _boot, _sequence),
            selected.Select(e => e.Sample).ToArray(), _ledger.Gaps.ToArray(), cursor, more,
            !_ledger.Incomplete && _ledger.Gaps.Count == 0);

    private void Recover(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("host_load_spool_link");
        var bytes = File.ReadAllBytes(path);
        var entries = new List<Entry>();
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != '\n') continue;
            try
            {
                var entry = JsonSerializer.Deserialize<Entry>(bytes.AsSpan(start, i - start), PhoneHomeFraming.Json)
                    ?? throw new JsonException();
                Validate(entry.Sample);
                if (entry.Ordinal <= 0) throw new JsonException();
                entries.Add(entry);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            { _ledger = _ledger with { Incomplete = true }; }
            start = i + 1;
        }
        if (start != bytes.Length)
        {
            var reader = new Utf8JsonReader(bytes.AsSpan(start), isFinalBlock: false, state: default);
            try
            {
                while (reader.Read())
                    if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("source"))
                    {
                        reader.Read();
                        var source = JsonSerializer.Deserialize<HostLoadSource>(ref reader, PhoneHomeFraming.Json);
                        if (source is not null) AddGap(source, "gap");
                        break;
                    }
            }
            catch (JsonException) { }
            _ledger = _ledger with { Incomplete = true };
            File.Move(path, path + ".quarantine-" + Guid.NewGuid().ToString("N"));
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            foreach (var entry in entries)
            {
                output.Write(JsonSerializer.SerializeToUtf8Bytes(entry, PhoneHomeFraming.Json));
                output.WriteByte((byte)'\n');
            }
            output.Flush(true);
        }
        _chunks.Add((path, entries));
    }

    private void RemoveAcknowledged()
    {
        foreach (var chunk in _chunks.Where(c => c.Entries.Count == 0 || c.Entries.All(e => e.Ordinal <= _ledger.Acknowledged)).ToArray())
        { File.Delete(chunk.Path); _chunks.Remove(chunk); }
    }

    private void EnforceCaps()
    {
        RemoveAcknowledged();
        var cutoff = _time.GetUtcNow() - TimeSpan.FromHours(_settings.SpoolMaxHours);
        while (_chunks.Count > 0 && (Bytes > _settings.SpoolMaxBytes || _chunks[0].Entries.Any(e => e.Sample.SampledAt < cutoff)))
        {
            var chunk = _chunks[0];
            foreach (var entry in chunk.Entries.Where(e => e.Ordinal > _ledger.Acknowledged)) AddGap(entry.Sample.Source, "gap");
            // Loss is explicit and durable before eviction. Cursor may cross only these declared losses.
            var through = chunk.Entries.Select(e => e.Ordinal).DefaultIfEmpty(_ledger.Acknowledged).Max();
            _ledger = _ledger with { Acknowledged = Math.Max(_ledger.Acknowledged, through), Incomplete = true };
            Persist();
            File.Delete(chunk.Path);
            _chunks.RemoveAt(0);
        }
        foreach (var quarantine in Files().Where(f => f.Name.Contains(".quarantine-", StringComparison.Ordinal)).OrderBy(f => f.LastWriteTimeUtc))
        {
            if (Bytes <= _settings.SpoolMaxBytes) break;
            _ledger = _ledger with { Incomplete = true }; Persist(); File.Delete(quarantine.FullName);
        }
    }

    private FileInfo[] Files() => new DirectoryInfo(_root).GetFiles();
    private void AddGap(HostLoadSource source, string reason)
    {
        var existing = _ledger.Gaps.FindIndex(g => g.RunnerStoreId == source.RunnerStoreId && g.BootId == source.BootId
            && g.Reason == reason && source.Sequence >= g.FromSequence - 1 && source.Sequence <= g.ToSequence + 1);
        if (existing >= 0)
        {
            var gap = _ledger.Gaps[existing];
            _ledger.Gaps[existing] = gap with { FromSequence = Math.Min(gap.FromSequence, source.Sequence), ToSequence = Math.Max(gap.ToSequence, source.Sequence) };
        }
        else _ledger.Gaps.Add(new(source.RunnerStoreId, source.BootId, source.Sequence, source.Sequence, reason));
        // The ledger itself is bounded; discarding old range detail remains visibly incomplete.
        if (_ledger.Gaps.Count > 256) _ledger.Gaps.RemoveAt(0);
        _ledger = _ledger with { Incomplete = true };
    }

    private void Persist()
    {
        var temporary = Path.Combine(_root, "cursor.tmp");
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { stream.Write(JsonSerializer.SerializeToUtf8Bytes(_ledger, PhoneHomeFraming.Json)); stream.Flush(true); }
        File.Move(temporary, Path.Combine(_root, "cursor.json"), overwrite: true);
    }

    private string Encode(Cursor cursor)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(cursor, PhoneHomeFraming.Json);
        var mac = HMACSHA256.HashData(Convert.FromHexString(_ledger.Secret), payload);
        return Convert.ToBase64String(payload) + "." + Convert.ToHexString(mac);
    }
    private Cursor Decode(string value)
    {
        try
        {
            if (value.Length > 1024) throw new FormatException();
            var parts = value.Split('.');
            if (parts.Length != 2) throw new FormatException();
            var payload = Convert.FromBase64String(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[1]), HMACSHA256.HashData(Convert.FromHexString(_ledger.Secret), payload)))
                throw new FormatException();
            return JsonSerializer.Deserialize<Cursor>(payload, PhoneHomeFraming.Json) ?? throw new FormatException();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        { throw new ArgumentException("invalid_host_load_cursor"); }
    }
    public static void Validate(HostLoadSample sample)
    {
        if (sample.SchemaVersion != 1 || sample.Source.RunnerStoreId == Guid.Empty || sample.Source.BootId == Guid.Empty
            || sample.Source.Sequence <= 0 || sample.IntervalSeconds != 5 || sample.SampledAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("invalid_host_load_sample");
        // Serialization also rejects every nonfinite nested number before a record becomes durable.
        _ = JsonSerializer.SerializeToUtf8Bytes(sample, PhoneHomeFraming.Json);
        if (sample.CpuPercent is < 0 or > 100) throw new ArgumentException("invalid_host_load_sample");
    }
}
