using System.Collections.Immutable;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record LandingGitResult(int ExitCode, string Output, string Diagnostic)
{
    public bool Succeeded => ExitCode == 0;
    /// <summary>HEAD observed at successful rebase completion, before returning to its caller.</summary>
    public string? RebaseHeadSha { get; init; }
}

/// <summary>CARD-0543. A probe failure sets <see cref="Reason"/>; the caller treats that as held.</summary>
public sealed record LandingIndexLockObservation(
    string Path,
    bool Present,
    DateTime? LastWriteUtc,
    long? Length,
    IReadOnlyList<(int Pid, DateTime? StartUtc)> CandidateHolders,
    string? Reason);

public sealed record LandSourceCoordinates(Guid TaskId, string RepositoryPath, string WorktreePath,
    string SourceFullRef, string TargetFullRef);

public sealed record LandSourceSnapshot(LandSourceCoordinates Coordinates, string CommonDirectory,
    string RegisteredPath, string GitDirectory, string SymbolicHead, string HeadSha,
    string BranchSha, string Status, ImmutableArray<string> IgnoredPaths);

public sealed record LandInspectionDiagnostic(string? Command, int? ExitCode, string? Code, string? ExceptionType);

public sealed record LandSourceInspection(LandSourceSnapshot? Snapshot, string? Reason,
    LandInspectionDiagnostic? Diagnostic = null)
{
    public bool Accepted => Snapshot is not null && Reason is null;
}

public sealed class LandingGitCommandException : IOException
{
    public string Command { get; }
    public int ExitCode { get; }
    public string DiagnosticCode { get; }

    public LandingGitCommandException(string command, int exitCode, string diagnosticCode)
        : base(diagnosticCode)
    {
        Command = command;
        ExitCode = exitCode;
        DiagnosticCode = diagnosticCode;
    }
}

public sealed class LandSourceResolutionConflictException : InvalidOperationException
{
    public LandSourceResolutionConflictException() : base("source_resolution_state_changed") { }
}

public sealed class LandFailurePersistenceException : Exception
{
    public Guid DiagnosticId { get; }
    public string PersistenceErrorType { get; }

    public LandFailurePersistenceException(Guid diagnosticId, Exception inner)
        : base("Could not persist land failure", inner)
    {
        DiagnosticId = diagnosticId;
        PersistenceErrorType = inner.GetType().Name;
    }
}

public sealed record LandingRegistration(string Path, string? Branch, string? Head,
    bool Locked, bool Prunable);

public sealed record LandingDestination(string RemoteName, string FullRef, string Fingerprint);

public sealed record LandingRemoteObservation(string? Sha, bool ContainsSource, string? Reason);

public sealed record LandingSourceObservation(string? Sha, string? ObservationRef, string? Fingerprint, string? Reason)
{
    public bool Missing => Reason == "source_remote_missing";
    public bool Accepted => Sha is not null && Reason is null && ObservationRef is not null && Fingerprint is { Length: 64 };
}

public sealed record LandingSourceGraph(LandSourceRelationship Relationship, string? Reason);

/// <summary>CARD-0642 D-6 / CARD-0688 D-8: one `ls-remote` of the source branch; no fetch and no pin ref.</summary>
public sealed record LandingSourceRecheck(string? Sha, string? Fingerprint, string? Reason)
{
    public bool Accepted => Reason is null && Sha is not null && Fingerprint is { Length: 64 };
}

public sealed record LandingVerification(bool Passed, string Description);

/// <summary>CARD-0642 D-5. Only guarded cleanup reads <see cref="LandSourceSnapshot.IgnoredPaths"/>;
/// the resolver and protocol ask for identity and the dirty check only.</summary>
public enum LandInspectionScope { Full, IdentityAndStatus }

/// <summary>CARD-0642 D-7: git I/O spent inside one land operation scope. Thread-safe.</summary>
public sealed class LandingGitProfile
{
    private long _processes, _worktreeLists, _registrationHits, _canonicalHits, _inspections, _remote, _gitTicks;

    public long Processes => Interlocked.Read(ref _processes);
    public long WorktreeLists => Interlocked.Read(ref _worktreeLists);
    public long RegistrationHits => Interlocked.Read(ref _registrationHits);
    public long CanonicalHits => Interlocked.Read(ref _canonicalHits);
    public long Inspections => Interlocked.Read(ref _inspections);
    public long RemoteRoundTrips => Interlocked.Read(ref _remote);
    public double GitSeconds => TimeSpan.FromTicks(Interlocked.Read(ref _gitTicks)).TotalSeconds;

    public void Record(IReadOnlyList<string> arguments, TimeSpan elapsed)
    {
        Interlocked.Increment(ref _processes);
        Interlocked.Add(ref _gitTicks, elapsed.Ticks);
        var index = SubcommandIndex(arguments);
        var command = index < arguments.Count ? arguments[index] : "";
        if (command == "worktree" && index + 1 < arguments.Count && arguments[index + 1] == "list") Interlocked.Increment(ref _worktreeLists);
        if (command is "ls-remote" or "fetch" or "push") Interlocked.Increment(ref _remote);
    }

    /// <summary>Index of the git subcommand after leading global options such as <c>-c key=value</c> or <c>-C path</c>.</summary>
    public static int SubcommandIndex(IReadOnlyList<string> arguments)
    {
        var index = 0;
        while (index < arguments.Count && arguments[index].StartsWith('-'))
            index += arguments[index] is "-c" or "-C" or "--git-dir" or "--work-tree" or "--namespace" ? 2 : 1;
        return index;
    }

    public void RegistrationHit() => Interlocked.Increment(ref _registrationHits);
    public void CanonicalHit() => Interlocked.Increment(ref _canonicalHits);
    public void Inspection() => Interlocked.Increment(ref _inspections);

    public string Describe() => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"processes={Processes} worktreeList={WorktreeLists} registrationHits={RegistrationHits} canonicalHits={CanonicalHits} inspections={Inspections} remote={RemoteRoundTrips} gitSeconds={GitSeconds:F2}");
}
