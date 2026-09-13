using System.Text.Json;
using System.Text.Json.Serialization;

namespace Antiphon.Server.Application.Dtos;

public enum ProgressRemoteState
{
    Present = 0,
    Missing = 1,
    NotConfigured = 2,
    Unavailable = 3,
}

public enum CompletionProgressAssessment
{
    ProgressObserved = 0,
    NoAttributedProgress = 1,
    Indeterminate = 2,
}

public enum ProgressOrigin
{
    Primary = 0,
    PrimaryRemote = 1,
    RepairSource = 2,
    RepairSourceRemote = 3,
}

public sealed record ProgressBaselineSnapshot(
    int SchemaVersion,
    DateTime CapturedAt,
    DateTime FileProbeCutoff,
    ProgressSourceBaseline Primary,
    ProgressSourceBaseline? RepairSource);

public sealed record ProgressSourceBaseline(
    string CanonicalRepository,
    string CanonicalCommonDirectory,
    Guid? OwnerTaskId,
    string? RegisteredCheckout,
    string FullRef,
    string LocalSha,
    ProgressRemoteBaseline Remote);

public sealed record ProgressRemoteBaseline(
    ProgressRemoteState State,
    string? Sha = null,
    string? EndpointFingerprint = null,
    string? Reason = null);

public sealed record CompletionProgressEvidence(
    int SchemaVersion,
    CompletionProgressAssessment Assessment,
    string? Reason = null,
    string? ClaimedSha = null,
    string? ClaimWarning = null,
    IReadOnlyList<CompletionProgressSource>? Sources = null);

public sealed record CompletionProgressSource(
    ProgressOrigin Origin,
    CompletionProgressAssessment Assessment,
    Guid? OwnerTaskId = null,
    string? ClaimedSha = null,
    string? VerifiedSha = null,
    string? LocalObserved = null,
    string? RemoteObserved = null,
    string? RegisteredPath = null,
    string? Reason = null,
    bool Complete = true);

public sealed record ProgressEvidenceDto(
    CompletionProgressAssessment Assessment,
    string? Reason = null,
    IReadOnlyList<ProgressEvidenceSourceDto>? Sources = null);

public sealed record ProgressEvidenceSourceDto(
    ProgressOrigin Origin,
    Guid? OwnerTaskId = null,
    string? Commit = null,
    string? LocalObserved = null,
    string? RemoteObserved = null,
    string? RegisteredPath = null,
    string? Reason = null);

public sealed record ProgressRevParse(bool Succeeded, string? Sha, string? Reason);
public sealed record ProgressSymbolicHead(bool Succeeded, string? FullRef, string? Reason);
public sealed record ProgressRemoteObservation(
    ProgressRemoteState State,
    string? Sha = null,
    string? EndpointFingerprint = null,
    string? Reason = null,
    string? ObservationRef = null);
public sealed record ProgressPinResult(bool Succeeded, string? RefName = null, string? Reason = null);

public sealed record ProgressClaimParse(
    string? Sha,
    string? Warning);

/// <summary>CARD-0499 stored JSON vocabulary. CamelCase properties, PascalCase enum names.</summary>
public static class TaskProgressJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string SerializeBaseline(ProgressBaselineSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static ProgressBaselineSnapshot? TryReadBaseline(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ProgressBaselineSnapshot>(json, Options); }
        catch (JsonException) { return null; }
    }

    public static string SerializeEvidence(CompletionProgressEvidence evidence) =>
        JsonSerializer.Serialize(evidence, Options);

    public static CompletionProgressEvidence? TryReadEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CompletionProgressEvidence>(json, Options); }
        catch (JsonException) { return null; }
    }

    public static ProgressEvidenceDto? ToDto(CompletionProgressEvidence? evidence)
    {
        if (evidence is null) return null;
        return new ProgressEvidenceDto(
            evidence.Assessment,
            evidence.Reason,
            evidence.Sources?
                .OrderBy(s => s.Assessment == CompletionProgressAssessment.ProgressObserved ? 0 : 1)
                .Select(s => new ProgressEvidenceSourceDto(
                    s.Origin,
                    s.OwnerTaskId,
                    s.VerifiedSha ?? s.ClaimedSha,
                    s.LocalObserved,
                    s.RemoteObserved,
                    s.RegisteredPath,
                    s.Reason)).ToArray());
    }
}
