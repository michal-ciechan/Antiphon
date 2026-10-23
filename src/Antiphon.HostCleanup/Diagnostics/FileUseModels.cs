namespace Antiphon.HostCleanup.Diagnostics;

public enum FileUseStatus { Unavailable, Observed, NoOwnersObserved, PathGone, Partial, TimedOut, Busy, NotTriggered }
public enum FileUseCoverage { None, Partial, Complete }
public enum FileUseOwnerState { Observed, ExitedDuringCapture, IdentityChanged, MetadataUnavailable }
public enum FileUseReason { UnsupportedPlatform, AccessDenied, PathGone, ExitedDuringCapture, IdentityChanged, NamespaceLimited, Budget, ResourceLimit, ProcessLimit, FdLimit, InputLimit, OwnerLimit, AncestorLimit, TextLimit, EvidenceUnavailable, RedactionUnavailable, ProviderFailure, Suppressed }
public sealed record FileUseGeneration(int Pid, string NativeStart);
public sealed record FileUseAncestor(int Pid, string NativeStart, string Name, string? CommandLine);
public sealed record FileUseOwner(int Pid, string Name, string NativeStart, DateTimeOffset? StartUtc,
    string? CommandLine, int? ParentPid, IReadOnlyList<FileUseAncestor> Ancestors,
    bool? AntiphonOwned, string Attribution, string MatchKind, string Resource)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> ProvenModules { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProvenExecutable { get; init; }
    public FileUseOwnerState State { get; init; } = FileUseOwnerState.Observed;
}
public sealed record FileUseObservation(FileUseStatus Status, FileUseCoverage Coverage,
    IReadOnlyList<FileUseOwner> Owners, IReadOnlyList<FileUseReason> Reasons, int Omitted = 0)
{
    public string? PathKind { get; init; }
    public string? ResourceIdentity { get; init; }
}
public sealed record FileUseRequest(string Path, Exception Error, string EvidenceRoot)
{
    public DateTimeOffset FailureUtc { get; init; } = DateTimeOffset.UtcNow;
    public long FailureTimestamp { get; init; } = System.Diagnostics.Stopwatch.GetTimestamp();
    public string OperationId { get; init; } = Guid.NewGuid().ToString("N");
    public string? SourceSha { get; init; }
    public string? TaskId { get; init; }
    public string? RunId { get; init; }
    public string? SessionId { get; init; }
    public string? FailedCommandText { get; init; }
    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(5);
    public IReadOnlyList<FileUseGeneration> OwnedGenerations { get; init; } = [];
    public IReadOnlyList<FileUseGeneration> ForeignGenerations { get; init; } = [];
    public IReadOnlyList<string> AdditionalPaths { get; init; } = [];
    public IReadOnlyList<string> SecretValues { get; init; } = [];
    public IReadOnlyList<string> ProjectDirectories { get; init; } = [];
    public FileUseOperation Operation { get; init; } = new();
}
// The caller owns the operation lifetime; the capture service keeps no unbounded operation cache.
public sealed class FileUseOperation
{
    internal readonly Dictionary<string, FileUseSnapshot> Captures = new(StringComparer.Ordinal);
}
public sealed record FileUseSnapshot(string CaptureId, FileUseStatus Status, FileUseCoverage Coverage,
    string Path, string FailureType, int NativeCode, string FailureText, DateTimeOffset FailureUtc,
    DateTimeOffset ObservationUtc, IReadOnlyList<FileUseOwner> Owners, IReadOnlyList<FileUseReason> Reasons)
{
    public int SchemaVersion => 1;
    public string? EvidencePath { get; init; }
    public string Persistence { get; init; } = "Unavailable";
    public int Omitted { get; init; }
    public string? OperationId { get; init; }
    public string? SourceSha { get; init; }
    public string? TaskId { get; init; }
    public string? RunId { get; init; }
    public string? SessionId { get; init; }
    public string Backend { get; init; } = "none";
    public double DurationMilliseconds { get; init; }
    public long FailureTimestamp { get; init; }
    public string? PathIdentity { get; init; }
    public string? PathKind { get; init; }
    public string? ResourceIdentity { get; init; }
    public DateTimeOffset? ObservationEndUtc { get; init; }
    public string Summary
    {
        get
        {
            var text = $"file-use capture={CaptureId} status={Status}; " + string.Join("; ", Owners.Select(o => $"holder pid={o.Pid} name={o.Name} start={o.NativeStart} state={o.State} parent={o.ParentPid} commandLine={o.CommandLine}"));
            return text.Length <= 2048 ? text : text[..2033] + "...[truncated]";
        }
    }
}
public interface IFileUseProvider { FileUseObservation Observe(FileUseRequest request, CancellationToken cancellationToken); }
public interface IFileUseEvidenceWriter { string? Write(FileUseSnapshot snapshot, string root, CancellationToken cancellationToken); }
