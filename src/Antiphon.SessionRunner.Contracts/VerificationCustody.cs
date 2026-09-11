using System.Text.Json.Serialization;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>Immutable application reservation, carried unchanged through the runner and host.</summary>
public sealed record VerificationExecutionBinding(
    [property: JsonRequired] Guid ExecutionId,
    [property: JsonRequired] VerificationSourceIdentity Source,
    [property: JsonRequired] VerificationSessionGeneration Generation,
    [property: JsonRequired] VerificationCreationCoordinates Creation,
    [property: JsonRequired] int CustodyContractVersion = 1,
    [property: JsonRequired] string Backend = "windows-job-v1",
    [property: JsonRequired] Guid RunnerStoreId = default);

public sealed record VerificationSourceIdentity([property: JsonRequired] Guid TaskId,
    [property: JsonRequired] Guid SourceOperationId, [property: JsonRequired] string LandedSha);
public sealed record VerificationSessionGeneration([property: JsonRequired] Guid SessionId,
    [property: JsonRequired] DateTime AcceptedStartedAt);
public sealed record VerificationCreationCoordinates(
    [property: JsonRequired] string RepositoryPath, [property: JsonRequired] string CommonGitDirectory,
    [property: JsonRequired] string WorktreePath, [property: JsonRequired] string WorktreeGitDirectory,
    [property: JsonRequired] string Branch, [property: JsonRequired] Guid CreationId);

[JsonConverter(typeof(JsonStringEnumConverter<VerificationCustodyState>))]
public enum VerificationCustodyState
{
    Starting, Tracking, Draining, Exited, NeverStarted, Unknown, UnsupportedBackend,
}

/// <summary>Identity of the original observer and its dedicated unnamed job; never a PID oracle.</summary>
public sealed record VerificationHostIdentity(
    [property: JsonRequired] Guid RunnerStoreId, [property: JsonRequired] Guid HostInstanceId,
    [property: JsonRequired] Guid ContainerId, [property: JsonRequired] int HostPid,
    [property: JsonRequired] DateTime HostStartTimeUtc);

/// <summary>Terminal evidence. Session exit, kill acknowledgements and worker reports cannot create this.</summary>
public sealed record VerificationCustodyReceipt(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] VerificationExecutionBinding Binding,
    [property: JsonRequired] VerificationHostIdentity Host,
    [property: JsonRequired] long StateRevision,
    [property: JsonRequired] DateTime SealedAtUtc,
    [property: JsonRequired] DateTime ObservedAtUtc,
    [property: JsonRequired] string ObservationMethod,
    [property: JsonRequired] uint? ActiveProcesses,
    [property: JsonRequired] bool OutputDrained,
    [property: JsonRequired] VerificationCustodyState Disposition,
    [property: JsonRequired] int? RootPid,
    [property: JsonRequired] DateTime? RootStartTimeUtc,
    bool? TerminationSucceeded = null,
    int? TerminationError = null);

/// <summary>Receipt is the exact durable UTF-8 document, not a reconstructed state projection.</summary>
public sealed record VerificationCustodyStatus(
    VerificationExecutionBinding Binding, VerificationCustodyState State, string? Reason,
    VerificationHostIdentity? Host = null, byte[]? Receipt = null);

public sealed class VerificationCustodyException(string code, Exception? inner = null)
    : InvalidOperationException(code, inner)
{
    public string Code { get; } = code;
}
