namespace Antiphon.HostCleanup.Diagnostics;

public enum FileUseStatus { Unavailable, Observed, NoOwnersObserved, PathGone, Partial, TimedOut, Busy, NotTriggered }
public enum FileUseCoverage { None, Partial, Complete }
public enum FileUseReason { UnsupportedPlatform, AccessDenied, PathGone, ExitedDuringCapture, IdentityChanged, NamespaceLimited, Budget, ResourceLimit, ProcessLimit, FdLimit, InputLimit, OwnerLimit, AncestorLimit, TextLimit, EvidenceUnavailable, RedactionUnavailable, ProviderFailure }
public sealed record FileUseGeneration(int Pid, string NativeStart);
public sealed record FileUseAncestor(int Pid, string NativeStart, string Name, string? CommandLine);
public sealed record FileUseOwner(int Pid, string Name, string NativeStart, DateTimeOffset? StartUtc,
    string? CommandLine, int? ParentPid, IReadOnlyList<FileUseAncestor> Ancestors,
    bool? AntiphonOwned, string Attribution, string MatchKind, string Resource);
public sealed record FileUseObservation(FileUseStatus Status, FileUseCoverage Coverage,
    IReadOnlyList<FileUseOwner> Owners, IReadOnlyList<FileUseReason> Reasons, int Omitted = 0);
public sealed record FileUseRequest(string Path, Exception Error, string EvidenceRoot)
{
    public DateTimeOffset FailureUtc { get; init; } = DateTimeOffset.UtcNow;
    public long FailureTimestamp { get; init; } = System.Diagnostics.Stopwatch.GetTimestamp();
    public string OperationId { get; init; } = Guid.NewGuid().ToString("N");
    public string? SourceSha { get; init; }
    public string? TaskId { get; init; }
    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(5);
    public IReadOnlyList<FileUseGeneration> OwnedGenerations { get; init; } = [];
    public IReadOnlyList<string> SecretValues { get; init; } = [];
    public IReadOnlyList<string> ProjectDirectories { get; init; } = [];
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
    public string Summary => $"file-use capture={CaptureId} status={Status}; " + string.Join("; ", Owners.Select(o => $"holder pid={o.Pid} name={o.Name} start={o.NativeStart} parent={o.ParentPid} commandLine={o.CommandLine}"));
}
public interface IFileUseProvider { FileUseObservation Observe(FileUseRequest request, CancellationToken cancellationToken); }
public interface IFileUseEvidenceWriter { string? Write(FileUseSnapshot snapshot, string root, CancellationToken cancellationToken); }
