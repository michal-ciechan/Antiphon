using System.Text.Json.Serialization;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>Immutable application reservation, carried unchanged through the runner and host.</summary>
public sealed record VerificationExecutionBinding(
    [property: JsonRequired] Guid ExecutionId,
    [property: JsonRequired] VerificationSourceIdentity Source,
    [property: JsonRequired] VerificationSessionGeneration Generation,
    [property: JsonRequired] VerificationCreationCoordinates Creation,
    // CARD-0604 D-19: no default. A binding that did not say which custody mechanism produced it
    // is not a binding -- the whole point of the second backend is that "the field was absent"
    // must never silently mean "Windows job object".
    [property: JsonRequired] string Backend,
    [property: JsonRequired] Guid RunnerStoreId,
    [property: JsonRequired] int CustodyContractVersion = 1);

/// <summary>
/// CARD-0604 D-19. The closed set of custody mechanisms and the observation method each one can
/// honestly produce. Before this existed, six call sites pinned the literal "windows-job-v1",
/// which is exactly the shape that makes a second backend arrive either as a fabricated Windows
/// receipt or as a flag that skips validation. Membership and equality are different questions
/// and both are asked: a binding's backend must be one of these AND must equal the backend the
/// runner that will execute it advertised when the execution was reserved.
/// </summary>
public static class VerificationCustodyBackends
{
    public const string WindowsJob = "windows-job-v1";
    public const string LinuxCgroup = "linux-cgroup-v1";

    /// <summary>Windows: the job object's own accounting. Linux: the cgroup's own procs file.</summary>
    public const string JobObjectAccounting = "JobObjectBasicAccountingInformation";
    public const string CgroupProcsEmpty = "CgroupProcsEmpty";

    /// <summary>Backend-independent: sealed before any native start intent was recorded.</summary>
    public const string NeverStartedMethod = "sealed-before-native-start-intent";

    public static bool IsSupported(string? backend) => backend is WindowsJob or LinuxCgroup;

    public static string ObservationMethodFor(string? backend) => backend switch
    {
        WindowsJob => JobObjectAccounting,
        LinuxCgroup => CgroupProcsEmpty,
        _ => throw new VerificationCustodyException("verification_custody_unsupported_backend"),
    };
}

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
