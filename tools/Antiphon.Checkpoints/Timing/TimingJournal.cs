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

    public string Root { get; }
    public TimingFault Fault { get; set; }
    public Func<Task>? FlushGate { get; set; }
    public TaskCompletionSource<bool>? FlushEntered { get; set; }
    public Func<Task>? AfterSequenceAssigned { get; set; }
    public Func<string, Task>? AfterPartialStage { get; set; }

    private readonly int? byteLimit;

    private TimingJournal(string root, int? byteLimit)
    {
        Root = root;
        this.byteLimit = byteLimit;
    }

    public static TimingJournal Open(string root) => Open(root, null);

    internal static TimingJournal Open(string root, int? byteLimit)
    {
        RequireComponent(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), allowNested: true);
        return new TimingJournal(Path.GetFullPath(root), byteLimit);
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

        Directory.CreateDirectory(Root);
        using var gate = await LockAsync(Path.Combine(Root, ".journal.lock"), cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(SpoolDirectory());
        var spool = SpoolPath(ev.EventId);
        if (File.Exists(spool))
        {
            var prior = JsonSerializer.Deserialize<TimingEvent>(ReadText(spool, byteLimit), Json);
            if (prior?.PayloadIdentity != ev.PayloadIdentity)
                throw new TimingIntegrityException("event-conflict");
        }
        else
        {
            await StageAsync(spool, ev, cancellationToken).ConfigureAwait(false);
        }

        if (Fault == TimingFault.AfterSpoolBeforeAppend)
            throw new TimingPersistenceException("append-not-started");
        return await AppendStagedAsync(ev, spool, recovering: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task StageAsync(string spool, TimingEvent ev, CancellationToken cancellationToken)
    {
        var temp = spool + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ev, Json);
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                if (AfterPartialStage is not null)
                {
                    await stream.WriteAsync(bytes.AsMemory(0, bytes.Length / 2), cancellationToken).ConfigureAwait(false);
                    stream.Flush(true);
                    await AfterPartialStage(temp).ConfigureAwait(false);
                    await stream.WriteAsync(bytes.AsMemory(bytes.Length / 2), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, spool);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private async Task<TimingAppendResult> AppendStagedAsync(TimingEvent ev, string spool, bool recovering,
        CancellationToken cancellationToken, Dictionary<string, TimingReadResult>? shards = null)
    {
        var path = ShardPath(ev);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (shards is null || !shards.TryGetValue(path, out var existing))
        {
            existing = ReadShard(path, byteLimit);
            shards?.Add(path, existing);
        }
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

        // Preserve the torn bytes for diagnosis and never join a new JSON object to a damaged line.
        if (existing.TornTails > 0 || existing.CorruptLines.Count > 0)
            throw new TimingIntegrityException("malformed-journal");
        ev.Sequence = existing.Events.Count == 0 ? 1 : checked(existing.Events.Max(item => item.Sequence) + 1);
        if (AfterSequenceAssigned is not null)
            await AfterSequenceAssigned().ConfigureAwait(false);

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev, Json) + "\n");
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        if (byteLimit is int limit && (stream.Length + bytes.Length > limit
            || existing.Events.Count >= CheckpointBuildObservation.EventLimit))
            throw new TimingReadBoundException();
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        FlushEntered?.TrySetResult(true);
        if (FlushGate is not null)
            await FlushGate().ConfigureAwait(false);
        stream.Flush(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (!recovering && Fault == TimingFault.AfterFlushBeforeAck)
            throw new TimingPersistenceException("ack-not-returned");
        existing.Events.Add(ev);
        TryDelete(spool);
        return new TimingAppendResult { Acknowledged = true, Sequence = ev.Sequence };
    }

    public async Task ImportSpoolAsync(CancellationToken cancellationToken = default)
    {
        await RecoverAndReadAsync(read: false, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<(TimingReadResult Read, string? Failure)> RecoverAndReadAsync(bool read,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Root))
            return (new(), null);
        using var gate = await LockAsync(Path.Combine(Root, ".journal.lock"), cancellationToken).ConfigureAwait(false);
        string? failure = null;
        var shards = read ? new Dictionary<string, TimingReadResult>(StringComparer.Ordinal) : null;
        var dir = SpoolDirectory();
        if (Directory.Exists(dir))
        {
            // Observation replay has already checked the bounded task inventory. Bound
            // this enumeration as well, so additions after that scan cannot expand it.
            var files = Directory.EnumerateFiles(dir).Take(byteLimit.HasValue ? 2 * CheckpointBuildObservation.EventLimit + 1 : int.MaxValue)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (byteLimit.HasValue && files.Length > 2 * CheckpointBuildObservation.EventLimit)
                throw new TimingReadBoundException();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.EndsWith(".tmp", StringComparison.Ordinal))
                {
                    failure ??= "observation-torn-stage:" + Path.GetFileName(file);
                    continue;
                }
                if (!file.EndsWith(".json", StringComparison.Ordinal))
                    continue;
                try
                {
                    var ev = JsonSerializer.Deserialize<TimingEvent>(ReadText(file, byteLimit), Json)
                        ?? throw new JsonException();
                    RequireComponent(ev.EventId);
                    RequireComponent(ev.RunId);
                    if (Path.GetFileNameWithoutExtension(file) != ev.EventId || ev.Attempt <= 0)
                        throw new JsonException();
                    await AppendStagedAsync(ev, file, recovering: true, cancellationToken, shards).ConfigureAwait(false);
                }
                catch (FileNotFoundException)
                {
                    continue;
                }
                catch (Exception ex) when (read && ex is JsonException or TimingRootException or TimingIntegrityException or OverflowException or DecoderFallbackException)
                {
                    failure ??= "observation-invalid-stage:" + Path.GetFileName(file);
                }
            }
        }
        return (read ? ReadRoots([Root], byteLimit, shards) : new(), failure);
    }

    // FileShare.None is an OS-backed exclusion on the supported hosts. Keep the
    // lock file in place so waiters always open the same file after a process exits.
    internal static async Task<FileStream> LockAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new TimingRootException("linked-journal-lock");
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (File.Exists(path))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static TimingReadResult Read(params string[] roots) => ReadRoots(roots, null, null);

    private static TimingReadResult ReadRoots(string[] roots, int? byteLimit, Dictionary<string, TimingReadResult>? shards)
    {
        var events = new List<TimingEvent>();
        var warnings = new List<string>();
        var corrupt = new List<string>();
        var torn = 0;
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root)))
        {
            var files = Directory.EnumerateFiles(root, "events.jsonl", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = byteLimit.HasValue ? FileAttributes.ReparsePoint : 0,
                MaxRecursionDepth = byteLimit.HasValue ? 4 : int.MaxValue,
            }).Take(byteLimit.HasValue ? BuildObservationReplay.TaskShardLimit + 1 : int.MaxValue)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (byteLimit.HasValue && files.Length > BuildObservationReplay.TaskShardLimit)
                throw new TimingReadBoundException();
            foreach (var file in files)
            {
                TimingReadResult shard;
                try { shard = shards?.GetValueOrDefault(file) ?? ReadShard(file, byteLimit); }
                catch (DecoderFallbackException) when (byteLimit.HasValue)
                {
                    warnings.Add("observation-invalid-shard:" + Path.GetRelativePath(root, file));
                    continue;
                }
                if (byteLimit.HasValue && (shard.CorruptLines.Count > 0 || shard.TornTails > 0))
                    warnings.Add("observation-invalid-shard:" + Path.GetRelativePath(root, file));
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

    private static TimingReadResult ReadShard(string path, int? byteLimit)
    {
        if (!File.Exists(path))
            return new TimingReadResult();
        var text = ReadText(path, byteLimit);
        if (text.Length == 0)
            return new TimingReadResult();
        var torn = !text.EndsWith('\n');
        var lines = text.Split('\n');
        if (byteLimit.HasValue && lines.Length > CheckpointBuildObservation.EventLimit + 1)
            throw new TimingReadBoundException();
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
                var ev = JsonSerializer.Deserialize<TimingEvent>(lines[i], Json) ?? throw new JsonException("empty");
                if (byteLimit.HasValue && (string.IsNullOrWhiteSpace(ev.EventId) || ev.Sequence <= 0))
                    throw new JsonException("timing identity or sequence missing");
                events.Add(ev);
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

    internal static string ReadText(string path, int? byteLimit)
    {
        if (byteLimit is not int limit)
            return File.ReadAllText(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > limit)
            throw new TimingReadBoundException();
        var buffer = new byte[limit + 1];
        var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (count > limit)
            throw new TimingReadBoundException();
        return new UTF8Encoding(false, true).GetString(buffer, 0, count);
    }

    private string ShardPath(TimingEvent ev)
    {
        var date = (ev.StartedAt ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        RequireComponent(date);
        return Path.Combine(Root, "runs", date, ev.RunId, ev.Attempt.ToString(CultureInfo.InvariantCulture), "events.jsonl");
    }

    private string SpoolDirectory() => Path.Combine(Root, "spool");

    private string SpoolPath(string eventId) => Path.Combine(SpoolDirectory(), eventId + ".json");

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

internal sealed class TimingReadBoundException : IOException;
