namespace Antiphon.Checkpoints;

internal sealed record BuildInputDescriptor(
    string Repository, string SourceSha, string Fingerprint, string Project,
    string PropertiesDigest, string? GraphDigest, string ToolchainDigest,
    string Runner, string OperatingSystem, string Architecture);

internal sealed record BuildAvailabilityRequest(
    string TaskId, DateTimeOffset RequestedAt, BuildInputDescriptor? Inputs, bool ClockAligned = true);

internal sealed record ObservedBuild(
    string TaskId, string BuildId, BuildInputDescriptor? Inputs,
    DateTimeOffset StartedAt, DateTimeOffset? SettledAt, string? Outcome,
    string OutputGeneration, bool LeaseVerified);

internal sealed record OutputCustody(
    string Runner, string BuildId, string OutputGeneration, bool Verified,
    DateTimeOffset From, DateTimeOffset Until, DateTimeOffset? RevokedAt,
    DateTimeOffset PresentFrom, DateTimeOffset PresentUntil, DateTimeOffset? RemovedAt);

internal sealed record BuildAvailabilityResult(string Status, string? Reason = null, string? BuildId = null);

internal static class BuildAvailability
{
    public static BuildAvailabilityResult Classify(
        BuildAvailabilityRequest request, ObservedBuild producer, OutputCustody? custody)
        => new("unknown", "classification-unavailable");
}
