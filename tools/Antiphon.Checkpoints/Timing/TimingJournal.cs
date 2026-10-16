using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Antiphon.Checkpoints;

public sealed class TimingRootRequest
{
    public string? Explicit { get; init; }
    public string? Environment { get; init; }
    public string? GitCommonDirectory { get; init; }
    public bool Hosted { get; init; }
    public string? Checkout { get; init; }
    public string? Output { get; init; }
    public string? Cleanup { get; init; }
    public string RunId { get; init; } = "run";
    public string Attempt { get; init; } = "1";
}

public sealed class TimingJournal
{
    public const string SchemaVersion = "antiphon-test-timing-v1";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    public string Root { get; }
    public TimingFault Fault { get; set; }
    public Func<Task>? FlushGate { get; set; }
    public TaskCompletionSource<bool>? FlushEntered { get; set; }
    public Func<Task>? AfterSequenceAssigned { get; set; }

    private TimingJournal(string root) => Root = root;

    public static TimingJournal Open(string root)
    {
        RequireComponent(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), allowNested: true);
        return new TimingJournal(Path.GetFullPath(root));
    }

    public static string Resolve(TimingRootRequest request)
    {
        RequireComponent(request.RunId);
        RequireComponent(request.Attempt);
        var chosen = First(request.Explicit, request.Environment, Fallback(request.GitCommonDirectory));
        if (string.IsNullOrWhiteSpace(chosen))
            throw new TimingRootException("timing-root-unconfigured");
        var full = Path.GetFullPath(chosen);
        if (request.Hosted)
        {
            foreach (var forbidden in new[] { request.Checkout, request.Output, request.Cleanup })
            {
                if (!string.IsNullOrWhiteSpace(forbidden) && IsUnder(full, Path.GetFullPath(forbidden)))
                    throw new TimingRootException("root-outside-custody");
            }
        }

        return full;
    }

    public static IReadOnlyList<string> DisposableOutputs(string outputDirectory, string timingRoot)
    {
        _ = timingRoot;
        return [Path.GetFullPath(outputDirectory)];
    }

    public async Task<TimingAppendResult> AppendAsync(TimingEvent ev, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ev.EventId))
            ev.EventId = Guid.NewGuid().ToString("N");
        RequireComponent(ev.EventId);
        RequireComponent(ev.RunId);
        RequireComponent(ev.Attempt.ToString(CultureInfo.InvariantCulture));
        ev.SchemaVersion = SchemaVersion;
        if (Fault == TimingFault.BeforeSpool)
            throw new TimingPersistenceException("spool-not-persisted");

        Directory.CreateDirectory(SpoolDirectory());
        var spool = SpoolPath(ev.EventId);
        var staged = JsonSerializer.Serialize(ev, Json);
        await File.WriteAllTextAsync(spool, staged, cancellationToken).ConfigureAwait(false);
        if (Fault == TimingFault.AfterSpoolBeforeAppend)
            throw new TimingPersistenceException("append-not-started");

        var gate = Gates.GetOrAdd(ShardKey(ev), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ShardPath(ev);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var existing = ReadShard(path);
            var match = existing.Events.FirstOrDefault(item => item.EventId == ev.EventId);
            if (match is not null)
            {
                if (!string.Equals(match.PayloadIdentity, ev.PayloadIdentity, StringComparison.Ordinal))
                    throw new TimingIntegrityException("event-conflict");
                TryDelete(spool);
                return new TimingAppendResult { Acknowledged = true, Idempotent = true, Sequence = match.Sequence };
            }

            if (ev.EventKind == "set-completed" && existing.Events.Any(item => item.EventKind == "set-completed"
                    && item.SetId == ev.SetId && item.SetKind == ev.SetKind && item.Producer != ev.Producer))
            {
                TryDelete(spool);
                return new TimingAppendResult { Acknowledged = true, SuppressedProducer = true, TimingPersistence = "complete" };
            }

            ev.Sequence = existing.Events.Count == 0 ? 1 : existing.Events.Max(item => item.Sequence) + 1;
            if (AfterSequenceAssigned is not null)
                await AfterSequenceAssigned().ConfigureAwait(false);

            var line = JsonSerializer.Serialize(ev, Json) + "\n";
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            FlushEntered?.TrySetResult(true);
            if (FlushGate is not null)
                await FlushGate().ConfigureAwait(false);
            stream.Flush(true);
            if (Fault == TimingFault.AfterFlushBeforeAck)
                throw new TimingPersistenceException("ack-not-returned");
            TryDelete(spool);
            return new TimingAppendResult { Acknowledged = true, Sequence = ev.Sequence };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ImportSpoolAsync(CancellationToken cancellationToken = default)
    {
        var dir = SpoolDirectory();
        if (!Directory.Exists(dir))
            return;
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            var ev = JsonSerializer.Deserialize<TimingEvent>(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), Json)
                ?? throw new TimingIntegrityException("event-conflict");
            var previous = Fault;
            Fault = TimingFault.None;
            try
            {
                await AppendAsync(ev, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Fault = previous;
            }
        }
    }

    public static TimingReadResult Read(params string[] roots)
    {
        var events = new List<TimingEvent>();
        var warnings = new List<string>();
        var corrupt = new List<string>();
        var torn = 0;
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root)))
        {
            foreach (var file in Directory.GetFiles(root, "events.jsonl", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            {
                var shard = ReadShard(file);
                torn += shard.TornTails;
                warnings.AddRange(shard.Warnings);
                corrupt.AddRange(shard.CorruptLines);
                foreach (var ev in shard.Events)
                {
                    if (seen.TryGetValue(ev.EventId, out var payload))
                    {
                        if (!string.Equals(payload, ev.PayloadIdentity, StringComparison.Ordinal))
                            corrupt.Add(ev.EventId + " event-conflict");
                        continue;
                    }

                    seen[ev.EventId] = ev.PayloadIdentity;
                    events.Add(ev);
                }
            }
        }

        return new TimingReadResult { Events = events, Warnings = warnings, CorruptLines = corrupt, TornTails = torn };
    }

    public static string AttemptStatus(IReadOnlyList<TimingEvent> events)
    {
        var started = events.Any(ev => ev.EventKind is "run-started" or "set-started");
        var finished = events.Any(ev => ev.EventKind is "set-completed" or "run-completed");
        if (started && !finished)
            return "unfinished";
        if (events.Any(ev => ev.Outcome == "passed" && ev.TimingPersistence == "complete" && ev.WallSeconds == 0 && ev.UnavailableReason is not null))
            return "unfinished";
        return finished ? "finished" : "absent";
    }

    public static string PayloadIdentity(TimingEvent ev)
    {
        var sequence = ev.Sequence;
        ev.Sequence = 0;
        try
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev, Json))));
        }
        finally
        {
            ev.Sequence = sequence;
        }
    }

    public static string? GitCommonTimingRoot(string? gitCommonDirectory) => Fallback(gitCommonDirectory);

    private static TimingReadResult ReadShard(string path)
    {
        if (!File.Exists(path))
            return new TimingReadResult();
        var text = File.ReadAllText(path);
        if (text.Length == 0)
            return new TimingReadResult();
        var torn = !text.EndsWith('\n');
        var lines = text.Split('\n');
        var events = new List<TimingEvent>();
        var warnings = new List<string>();
        var corrupt = new List<string>();
        var limit = lines.Length - 1;
        for (var i = 0; i < limit; i++)
        {
            if (lines[i].Length == 0)
                continue;
            try
            {
                events.Add(JsonSerializer.Deserialize<TimingEvent>(lines[i], Json) ?? throw new JsonException("empty"));
            }
            catch (JsonException)
            {
                corrupt.Add(lines[i]);
            }
        }

        if (torn && lines[^1].Length > 0)
            warnings.Add("torn-tail");
        return new TimingReadResult { Events = events, Warnings = warnings, CorruptLines = corrupt, TornTails = torn && lines[^1].Length > 0 ? 1 : 0 };
    }

    private string ShardPath(TimingEvent ev)
    {
        var date = (ev.StartedAt ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        RequireComponent(date);
        return Path.Combine(Root, "runs", date, ev.RunId, ev.Attempt.ToString(CultureInfo.InvariantCulture), "events.jsonl");
    }

    private string SpoolDirectory() => Path.Combine(Root, "spool");

    private string SpoolPath(string eventId) => Path.Combine(SpoolDirectory(), eventId + ".json");

    private static string ShardKey(TimingEvent ev) =>
        ev.RunId + "/" + ev.Attempt.ToString(CultureInfo.InvariantCulture);

    private static string? Fallback(string? gitCommon)
    {
        if (string.IsNullOrWhiteSpace(gitCommon))
            return null;
        return Path.Combine(Path.GetFullPath(gitCommon), "antiphon", "test-timings");
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    public static void RequireComponent(string value, bool allowNested = false)
    {
        if (allowNested)
            return;
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Contains("..", StringComparison.Ordinal)
            || value.IndexOfAny(['/', '\\', '\0']) >= 0
            || Path.IsPathRooted(value))
            throw new TimingRootException("path-component-refused");
    }

    private static bool IsUnder(string path, string parent)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(parent);
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
