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
        var paired = history.Where(item => item.Binding == expected).ToArray();
        if (paired.Length != 2)
            return new("unknown", paired.Length < 2 ? "broker-pair-missing" : "broker-pair-ambiguous", null);
        var grant = Array.Find(paired, item => item.Kind == "grant");
        var release = Array.Find(paired, item => item.Kind == "release");
        if (grant is null || release is null || release.At < grant.At)
            return new("unknown", "broker-pair-unverified", null);
        return new("known", null, (release.At - grant.At).TotalSeconds);
    }

    internal static async Task<BuildObservationRead> ReadAsync(string ownedRunDirectory, CancellationToken cancellationToken)
    {
        var path = CheckpointBuildObservation.JournalRoot(ownedRunDirectory);
        try
        {
            if (!Directory.Exists(path))
                return new([], "observation-missing");
            await TimingJournal.Open(path).ImportSpoolAsync(cancellationToken).ConfigureAwait(false);
            var read = TimingJournal.Read(path);
            var events = read.Events.Where(item => item.BuildPhase is { Version: "antiphon-timing-phase-v1" }).ToArray();
            if (read.CorruptLines.Count > 0 || read.TornTails > 0)
                return new(events, "observation-integrity");
            if (read.Events.Any(item => item.UnavailableReason == "observation-truncated"))
                return new(events, "observation-truncated");
            return new(events, events.Length == 0 ? "observation-missing" : null);
        }
        catch (TimingIntegrityException)
        {
            return new([], "event-conflict");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimingRootException)
        {
            return new([], "observation-unreachable");
        }
    }

    internal static BuildAvailabilityResult Classify(ObservedBuild producer, BuildAvailabilityRequest request,
        BrokerCoverage broker, BuildCustodyEvidence? custody)
    {
        if (broker.Status != "known")
            return new("unknown", broker.Reason);
        return BuildAvailability.Classify(request, producer with { LeaseVerified = true }, custody);
    }
}
