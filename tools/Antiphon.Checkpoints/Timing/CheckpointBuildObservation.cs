using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public sealed record CheckpointBuildPhase(
    string Version, string Invocation, string? TaskId, string? AttemptId, string BuildId, string? Project,
    string? SourceSha, string? SourceFingerprint, string? RequestedPropertiesDigest, string? LeaseId,
    DateTimeOffset RequestedAt, DateTimeOffset? LaunchedAt, DateTimeOffset? ExitedAt, DateTimeOffset SettledAt,
    double ElapsedSeconds, string Outcome, string BuildSource, string? OperatingSystem, string Architecture,
    double? TestHostSeconds, DateTimeOffset? CleanupStartedAt, DateTimeOffset? CleanupEndedAt, bool? OutputsCleaned)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BrokerAuthority { get; init; }

    public string BrokerCoverageReason { get; init; } = "broker-telemetry-unreachable";
    public string CustodyCoverageReason { get; init; } = "custody-record-unreachable";
}

// Collect scalar facts during execution and hand them to the observation worker.
internal sealed class CheckpointBuildObservation(string runDirectory, string runId, string? taskId,
    string? attemptId, TimeProvider time, int eventLimit = CheckpointBuildObservation.EventLimit,
    int byteLimit = CheckpointBuildObservation.ByteLimit)
{
    internal const int EventLimit = 128;
    internal const int ByteLimit = 64 * 1024;
    private const int DiagnosticReserve = 2048;
    private const string Producer = "checkpoint-build";
    private const string EventKind = "build-observed";
    private const string Schema = "antiphon-timing-phase-v1";

    private readonly List<TimingEvent> _records = [];
    private int _recordBytes;
    private bool _truncated;

    internal static string JournalRoot(string ownedRunDirectory) => Path.Combine(ownedRunDirectory, "build-observation");

    internal sealed class Attempt(string invocation, string id, string? project, SourceObservation source,
        DateTimeOffset requestedAt, long startedTick)
    {
        internal string Invocation { get; } = invocation;
        internal string Id { get; } = id;
        internal string? Project { get; } = project;
        internal SourceObservation Source { get; } = source;
        internal DateTimeOffset RequestedAt { get; } = requestedAt;
        internal long StartedTick { get; } = startedTick;
        internal string? PropertyDigest { get; set; }
        internal string? LeaseId { get; private set; }
        internal string? BrokerEndpoint { get; private set; }
        internal DateTimeOffset? LaunchedAt { get; set; }
        internal DateTimeOffset? ExitedAt { get; set; }

        internal void RecordLease(SlotLease lease)
        {
            LeaseId = lease.LeaseId;
            BrokerEndpoint = lease.BrokerEndpoint;
        }

        internal void RecordProperties(IReadOnlyList<string> properties)
        {
            if (properties.Count <= 64 && properties.Sum(property => (long)property.Length) <= 4096)
                PropertyDigest = Digest(JsonSerializer.SerializeToUtf8Bytes(properties));
        }
    }

    internal Attempt Begin(string invocation, string id, string? project, SourceObservation source)
    {
        var relative = project?.Replace('\\', '/');
        if (relative is { Length: > 240 } || relative?.StartsWith('/') == true || relative?.Contains(':') == true
            || relative?.Split('/').Contains("..", StringComparer.Ordinal) == true)
            relative = null;
        return new(invocation, id, relative, source, time.GetUtcNow(), time.GetTimestamp());
    }

    internal void Complete(Attempt attempt, string outcome, string buildSource, double? testHostSeconds = null)
    {
        if (_records.Count >= eventLimit - 1 || attempt.Id.Length > 128)
        {
            _truncated = true;
            return;
        }
        var clean = attempt.Source is { CaptureStatus: "known", DirtyFiles: 0 };
        var phase = new CheckpointBuildPhase(Schema, attempt.Invocation, GuidId(taskId), GuidId(attemptId),
            attempt.Id, attempt.Project, clean ? Hex(attempt.Source.Commit, 40, 64) : null,
            clean ? Hex(attempt.Source.Fingerprint, 64, 64) : null, attempt.PropertyDigest, GuidId(attempt.LeaseId),
            attempt.RequestedAt, attempt.LaunchedAt, attempt.ExitedAt, time.GetUtcNow(),
            time.GetElapsedTime(attempt.StartedTick).TotalSeconds, outcome, buildSource,
            OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : null,
            RuntimeInformation.ProcessArchitecture.ToString(), testHostSeconds, null, null, null)
        {
            BrokerAuthority = BrokerAuthority(attempt.BrokerEndpoint),
        };
        var record = Event(EventKind, attempt.Id, attempt.RequestedAt);
        record.BuildPhase = phase;
        // Reserve room for the sequence, newline and terminal cleanup timestamps.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, TimingJournal.Json).Length + 112;
        if (_recordBytes + bytes > byteLimit - DiagnosticReserve)
        {
            _truncated = true;
            return;
        }
        _recordBytes += bytes;
        _records.Add(record);
    }

    internal async Task PublishAsync(Func<string, TimingJournal>? open, Action<string> diagnostic,
        DateTimeOffset? cleanupStartedAt = null, DateTimeOffset? cleanupEndedAt = null, bool? outputsCleaned = null,
        CancellationToken cancellationToken = default)
    {
        if (_records.Count == 0 && !_truncated)
            return;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var taskLock = await BuildObservationReplay.LockTaskAsync(runDirectory, budget.Token).ConfigureAwait(false);
            var records = new List<TimingEvent>(_records);
            if (_truncated)
            {
                var marker = Event("build-observation-coverage", "", _records.FirstOrDefault()?.StartedAt ?? time.GetUtcNow());
                marker.UnavailableReason = "observation-truncated";
                records.Insert(0, marker);
            }
            var shards = records.Select(record => record.StartedAt!.Value.UtcDateTime.Date).Distinct().Count();
            var stagingBytes = records.Max(record => JsonSerializer.SerializeToUtf8Bytes(record, TimingJournal.Json).Length);
            if (!BuildObservationReplay.WithinTaskBudget(runDirectory, shards,
                    _recordBytes + DiagnosticReserve + stagingBytes, budget.Token))
                throw new TimingReadBoundException();

            var path = JournalRoot(runDirectory);
            budget.Token.ThrowIfCancellationRequested();
            // The checkpoint already owns the run and the task lock checked its ancestors.
            // An exclusive mkdir cannot replace an existing path.
            SafeWrite.Require(ExclusiveDirectory.Create(path));
            var pending = Path.Combine(path, BuildObservationReplay.BatchFile);
            // The freshly created private directory and task lock already provide custody.
            using (var batch = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                batch.Write("observation-batch-incomplete"u8);
                batch.Flush(true);
            }
            var journal = open?.Invoke(path) ?? TimingJournal.Open(path, ByteLimit);
            foreach (var record in records)
            {
                if (record.BuildPhase is { } phase)
                    record.BuildPhase = phase with
                    {
                        CleanupStartedAt = cleanupStartedAt, CleanupEndedAt = cleanupEndedAt, OutputsCleaned = outputsCleaned,
                    };
                await journal.AppendAsync(record, budget.Token).ConfigureAwait(false);
            }
            budget.Token.ThrowIfCancellationRequested();
            File.Delete(pending);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            try
            {
                var reason = ex is OperationCanceledException ? "publication-timeout" : "journal-" + ex.GetType().Name;
                diagnostic("BUILD OBSERVATION coverage=unknown reason=" + reason);
            }
            catch (Exception logError) when (logError is IOException or InvalidOperationException or UnauthorizedAccessException) { }
        }
    }

    private TimingEvent Event(string kind, string buildId, DateTimeOffset started) => new()
    {
        EventId = Digest(JsonSerializer.SerializeToUtf8Bytes(new[] { Producer, taskId, attemptId, runId, buildId, kind })),
        EventKind = kind,
        RunId = runId,
        Producer = Producer,
        ProducerVersion = Schema,
        SetKind = "build-observation",
        SetId = buildId,
        ProducingBuildId = buildId,
        StartedAt = started,
        CoverageStatus = "unknown",
        UnavailableReason = "effective-inputs-unavailable",
    };

    // The endpoint can carry credentials or deployment-specific path components.
    internal static string? BrokerAuthority(string? endpoint) =>
        endpoint is { Length: > 0 and <= 4096 } ? Digest(Encoding.UTF8.GetBytes(endpoint.TrimEnd('/'))) : null;

    private static string? GuidId(string? value) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty ? parsed.ToString("D") : null;

    private static string? Hex(string? value, int min, int max) => value is not null &&
        (value.Length == min || value.Length == max) && value.All(Uri.IsHexDigit) ? value : null;

    internal static string? ProjectDigest(string project) => project.Length <= 4096
        ? Digest(Encoding.UTF8.GetBytes(project.Replace('\\', '/')))
        : null;

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
