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

    /// <summary>
    /// CARD-0613 D-7. Post-baseline work proved in the task's OWN registered checkout, but not on
    /// the expected ref with descendant history: an off-branch or detached HEAD carrying a valid
    /// task-scoped claim, a task branch reset into a divergent lineage, or uncommitted files in
    /// either of those states. It prevents a false no-progress failure and NOTHING else — it is
    /// deliberately absent from <see cref="Services.TaskCompletionProgressService.AllowsAutomaticWorkspaceMutation"/>,
    /// so landing still goes through the explicit path. Appended, never renumbered: old stored
    /// evidence keeps its meaning.
    /// </summary>
    PrimaryAlternate = 4,
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
    IReadOnlyList<CompletionProgressSource>? Sources = null,
    /// <summary>
    /// CARD-0657 D-4. What the pre-attribution runner sync observed for this evaluation. Additive:
    /// absent on local tasks and on evidence written before this card (still schema version 1).
    /// </summary>
    RemoteSyncEvidence? RemoteSync = null);

/// <summary>
/// CARD-0657 D-4. The durable sync facts of one completion evaluation. Never reused as fresh
/// evidence: every attempt prepares again.
/// </summary>
public sealed record RemoteSyncEvidence(
    int Attempt,
    RemoteSettlementSyncState State,
    string? FullRef = null,
    string? ObservedSha = null,
    string? ConfirmedSha = null,
    string? Reason = null)
{
    public static RemoteSyncEvidence From(int attempt, RemoteSettlementSyncResult result) =>
        new(attempt, result.State, result.FullRef, result.RemoteSha,
            result.Confirmed ? result.DesktopAfterSha : null, result.Reason);
}

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
    bool Complete = true,
    /// <summary>
    /// CARD-0613 D-7. The symbolic ref the registered checkout was actually on when observed.
    /// Null alongside an alternate reason denotes a detached HEAD. Additive: absent on evidence
    /// written before this card, which still round-trips at schema version 1.
    /// </summary>
    string? ObservedRef = null);

public sealed record ProgressEvidenceDto(
    CompletionProgressAssessment Assessment,
    string? Reason = null,
    IReadOnlyList<ProgressEvidenceSourceDto>? Sources = null,
    RemoteSyncEvidence? RemoteSync = null);

public sealed record ProgressEvidenceSourceDto(
    ProgressOrigin Origin,
    Guid? OwnerTaskId = null,
    string? Commit = null,
    string? LocalObserved = null,
    string? RemoteObserved = null,
    string? RegisteredPath = null,
    string? Reason = null,
    string? ObservedRef = null);

public sealed record ProgressRevParse(bool Succeeded, string? Sha, string? Reason);
public sealed record ProgressSymbolicHead(bool Succeeded, string? FullRef, string? Reason);
public sealed record ProgressRemoteObservation(
    ProgressRemoteState State,
    string? Sha = null,
    string? EndpointFingerprint = null,
    string? Reason = null,
    string? ObservationRef = null);
public sealed record ProgressPinResult(bool Succeeded, string? RefName = null, string? Reason = null);

/// <summary>
/// CARD-0613 D-6. The committer timestamp of ONE exact commit object, in UTC. An unreadable,
/// missing, malformed or out-of-range answer is <see cref="Available"/> false — never 'now', and
/// never an absence that could be read as a complete negative.
/// </summary>
public sealed record ProgressCommitTime(bool Available, DateTime? CommitterUtc, string? Reason);

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
                    s.Reason,
                    s.ObservedRef)).ToArray(),
            evidence.RemoteSync);
    }
}
