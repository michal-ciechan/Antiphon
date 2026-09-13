using System.Collections.Immutable;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record LandingGitResult(int ExitCode, string Output, string Diagnostic)
{
    public bool Succeeded => ExitCode == 0;
    /// <summary>HEAD observed at successful rebase completion, before returning to its caller.</summary>
    public string? RebaseHeadSha { get; init; }
}

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

public sealed record LandingVerification(bool Passed, string Description);
