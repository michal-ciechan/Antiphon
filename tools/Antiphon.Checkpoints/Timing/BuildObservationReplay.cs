namespace Antiphon.Checkpoints;

// A source supplies these records only after binding a retained transition to the broker boot
// and the producer. Checkpoint-side acquire/release logs and aggregate counters cannot supply it.
internal sealed record ProducerLeaseBinding(
    string Authority, string Generation, Guid LeaseId, string TaskId, string RunId, string BuildId);

internal sealed record ProducerLeaseTransition(ProducerLeaseBinding Binding, string Kind, DateTimeOffset At);
internal sealed record BrokerCoverage(string Status, string? Reason, double? HeldSeconds);
internal sealed record BuildObservationRead(IReadOnlyList<TimingEvent> Events, string? CoverageReason);

internal static class BuildObservationReplay
{
    internal static BrokerCoverage MeasureProducerHold(ProducerLeaseBinding expected,
        IReadOnlyList<ProducerLeaseTransition>? history)
    {
        if (history is null)
            return new("unknown", "broker-telemetry-unreachable", null);
        if (string.IsNullOrWhiteSpace(expected.Authority) || string.IsNullOrWhiteSpace(expected.Generation)
            || expected.LeaseId == Guid.Empty)
            return new("unknown", "broker-binding-unverified", null);
        var paired = history.Where(item => item.Binding == expected).ToArray();
        if (paired.Length != 2)
            return new("unknown", paired.Length < 2 ? "broker-pair-missing" : "broker-pair-ambiguous", null);
        var grant = Array.Find(paired, item => item.Kind == "grant");
        var release = Array.Find(paired, item => item.Kind == "release");
        if (grant is null || release is null || release.At < grant.At)
            return new("unknown", "broker-pair-unverified", null);
        return new("known", null, (release.At - grant.At).TotalSeconds);
    }

    internal const int TaskShardLimit = 16;
    private const int TaskByteLimit = TaskShardLimit * CheckpointBuildObservation.ByteLimit;
    private const int TaskEntryLimit = TaskShardLimit * (2 * CheckpointBuildObservation.EventLimit + 8);
    internal const string BatchFile = "batch.pending";

    internal static Task<FileStream> LockTaskAsync(string runDirectory, CancellationToken cancellationToken) =>
        TimingJournal.LockAsync(Path.Combine(TaskRoot(runDirectory), ".build-observation.lock"), cancellationToken);

    internal static async Task<BuildObservationRead> ReadAsync(string ownedRunDirectory, CancellationToken cancellationToken)
    {
        var path = CheckpointBuildObservation.JournalRoot(ownedRunDirectory);
        if (!Directory.Exists(path))
            return new([], "observation-missing");
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(2));
            using var gate = await LockTaskAsync(ownedRunDirectory, budget.Token).ConfigureAwait(false);
            if (!WithinTaskBudget(ownedRunDirectory, 0, 0, budget.Token))
                return new([], "observation-read-bound-exceeded");
            var (read, failure) = await TimingJournal.Open(path, CheckpointBuildObservation.ByteLimit)
                .RecoverAndReadAsync(read: true, budget.Token).ConfigureAwait(false);
            var events = read.Events.Where(item => item.BuildPhase is { Version: "antiphon-timing-phase-v1" }).ToArray();
            var issue = failure ?? read.Warnings.FirstOrDefault(warning => warning.StartsWith("observation-", StringComparison.Ordinal));
            if (issue is not null)
                return new(events, issue);
            if (read.CorruptLines.Count > 0 || read.TornTails > 0)
                return new(events, "observation-integrity");
            var marker = Path.Combine(path, BatchFile);
            if (File.Exists(marker))
                return new(events, "observation-batch-incomplete");
            if (read.Events.Any(item => item.UnavailableReason == "observation-truncated"))
                return new(events, "observation-truncated");
            return new(events, events.Length == 0 ? "observation-missing" : null);
        }
        catch (TimingReadBoundException)
        {
            return new([], "observation-read-bound-exceeded");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new([], "observation-replay-timeout");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimingRootException)
        {
            return new([], "observation-unreachable");
        }
    }

    // The results root owns the task's runs. Count the whole root, including
    // staged bytes and unacknowledged duplicates, under the same cross-process
    // lock used by observers and their replay readers.
    internal static bool WithinTaskBudget(string runDirectory, int additionalShards, int additionalBytes,
        CancellationToken cancellationToken)
    {
        var task = TaskRoot(runDirectory);
        var entries = 0;
        var shards = additionalShards;
        long bytes = additionalBytes;
        foreach (var run in Directory.EnumerateFileSystemEntries(task))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entries > TaskEntryLimit)
                return false;
            var runAttributes = File.GetAttributes(run);
            if (!runAttributes.HasFlag(FileAttributes.Directory) || runAttributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            var journal = CheckpointBuildObservation.JournalRoot(run);
            if (!Directory.Exists(journal))
                continue;
            var runShards = 0;
            var paths = new Stack<(string Path, int Depth)>();
            paths.Push((journal, 0));
            while (paths.TryPop(out var directory))
            {
                if (File.GetAttributes(directory.Path).HasFlag(FileAttributes.ReparsePoint))
                    return false;
                foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++entries > TaskEntryLimit)
                        return false;
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(path); }
                    catch (FileNotFoundException) { continue; }
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                        return false;
                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        if (directory.Depth >= 4)
                            return false;
                        paths.Push((path, directory.Depth + 1));
                        continue;
                    }
                    if (Path.GetFileName(path) == "events.jsonl")
                        runShards++;
                    bytes += new FileInfo(path).Length;
                    if (bytes > TaskByteLimit)
                        return false;
                }
            }
            shards += Math.Max(1, runShards);
            if (shards > TaskShardLimit)
                return false;
        }
        return true;
    }

    private static string TaskRoot(string runDirectory) =>
        Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(runDirectory)))!;

    internal static BuildAvailabilityResult Classify(ObservedBuild producer, BuildAvailabilityRequest request,
        BrokerCoverage broker, BuildCustodyEvidence? custody)
    {
        if (broker.Status != "known")
            return new("unknown", broker.Reason);
        return BuildAvailability.Classify(request, producer with { LeaseVerified = true }, custody);
    }
}
