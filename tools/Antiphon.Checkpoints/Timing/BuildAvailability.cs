namespace Antiphon.Checkpoints;

internal sealed record BuildInputDescriptor(
    string Repository, string SourceSha, string Fingerprint, string Project,
    string PropertiesDigest, string? GraphDigest, string ToolchainDigest,
    string Runner, string OperatingSystem, string Architecture)
{
    public int Version { get; init; } = 1;

    public bool IsComplete => Version == 1
        && SourceSha is { Length: 40 or 64 } && SourceSha.All(Uri.IsHexDigit)
        && !string.IsNullOrWhiteSpace(GraphDigest)
        && !string.IsNullOrWhiteSpace(Repository)
        && !string.IsNullOrWhiteSpace(Fingerprint)
        && !string.IsNullOrWhiteSpace(Project)
        && !string.IsNullOrWhiteSpace(PropertiesDigest)
        && !string.IsNullOrWhiteSpace(ToolchainDigest)
        && !string.IsNullOrWhiteSpace(Runner)
        && !string.IsNullOrWhiteSpace(OperatingSystem)
        && !string.IsNullOrWhiteSpace(Architecture);
}

internal sealed record BuildAvailabilityRequest(
    string TaskId, DateTimeOffset RequestedAt, BuildInputDescriptor? Inputs, bool ClockAligned = true);

internal sealed record ObservedBuild(
    string TaskId, string BuildId, BuildInputDescriptor? Inputs,
    DateTimeOffset StartedAt, DateTimeOffset? SettledAt, string? Outcome,
    string OutputGeneration, bool LeaseVerified);

internal sealed record BuildCustodyEvidence(
    string Runner, string BuildId, string OutputGeneration, bool Verified,
    DateTimeOffset From, DateTimeOffset Until, DateTimeOffset? RevokedAt,
    DateTimeOffset PresentFrom, DateTimeOffset PresentUntil, DateTimeOffset? RemovedAt);

internal sealed record BuildAvailabilityResult(string Status, string? Reason = null, string? BuildId = null);

internal static class BuildAvailability
{
    public static BuildAvailabilityResult Classify(
        BuildAvailabilityRequest request, ObservedBuild producer, BuildCustodyEvidence? custody)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId) || string.IsNullOrWhiteSpace(producer.TaskId))
            return new("unknown", "task-binding-missing");
        if (request.TaskId == producer.TaskId)
            return new("ineligible", "same-task");
        if (request.Inputs is not { IsComplete: true } || producer.Inputs is not { IsComplete: true })
            return new("unknown", "key-incomplete");
        if (request.Inputs != producer.Inputs)
            return new("ineligible", "key-mismatch");

        var at = request.RequestedAt;
        if (!request.ClockAligned || producer.SettledAt < producer.StartedAt)
            return new("unknown", "clock-unaligned");
        if (at < producer.StartedAt)
            return new("ineligible", "producer-not-started");
        if (producer.SettledAt <= at && producer.Outcome is "failed" or "canceled")
            return new("ineligible", "producer-failed");
        if (!producer.LeaseVerified)
            return new("unknown", "lease-unverified");
        if (producer.SettledAt is not DateTimeOffset settled || settled > at)
            return new("in-flight-candidate", BuildId: producer.BuildId);
        if (producer.Outcome != "ok")
            return new("unknown", "producer-outcome-unknown");

        if (custody is null)
            return new("unknown", "custody-record-unreachable");
        if (!custody.Verified)
            return new("unknown", "custody-unverified");
        if (custody.Runner != producer.Inputs.Runner || custody.BuildId != producer.BuildId
            || custody.OutputGeneration != producer.OutputGeneration)
            return new("unknown", "custody-binding-mismatch");
        if (custody.RevokedAt <= at)
            return new("ineligible", "custody-revoked");
        if (custody.Until <= at)
            return new("ineligible", "custody-expired");
        if (custody.From > at)
            return new("unknown", "custody-interval-unknown");
        if (custody.RemovedAt <= at)
            return new("ineligible", "output-absent");
        if (at < custody.PresentFrom || at >= custody.PresentUntil)
            return new("unknown", "output-presence-unknown");
        return new("eligible-ready", BuildId: producer.BuildId);
    }
}
