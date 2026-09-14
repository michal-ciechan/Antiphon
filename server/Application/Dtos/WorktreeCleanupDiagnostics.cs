using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Dtos;

public enum WorktreeLockStatus { OwnersObserved, NoOwnersObserved, Partial, Unavailable, TimedOut, Failed, PathGone }

public sealed record WorktreeLockOwner(string Name, int ProcessId, string RelativePath,
    long? ProcessStartTicks = null, int? ParentProcessId = null, long? ParentStartTicks = null,
    string Attribution = "unknown");

public sealed record WorktreeLockSnapshot(WorktreeLockStatus Status, string Reason, DateTime At,
    IReadOnlyList<WorktreeLockOwner> Owners, int OmittedOwners = 0,
    string? ToolVersion = null, string? ToolIdentity = null, bool FileControl = false,
    bool DirectoryControl = false, long DurationMilliseconds = 0);

public sealed record WorktreeNativeObservation(string Operation, string RelativePath, DateTime At,
    bool Succeeded, int? NativeErrorCode, bool IdentityVerified);

public sealed record WorktreeNativeSnapshot(WorktreeLockStatus Status, string Reason,
    IReadOnlyList<WorktreeNativeObservation> Observations, int Candidates = 0)
{
    public bool HasSharingConflict => Observations.Any(o => o.Operation == "DeleteAccessOpen"
        && o.IdentityVerified && !o.Succeeded && o.NativeErrorCode is 32 or 33);
}

public sealed record WorktreeGitOutcome(string Operation, int? ExitCode, string GeneratedCode,
    string? ExceptionType, DateTime At, bool NormallyExited);

public sealed record WorktreeCleanupCapture(Guid Id, Guid RequestId, Guid OperationId, Guid TaskId,
    DateTime At, WorktreeGitOutcome GitFailure, WorktreeLockSnapshot Handles,
    WorktreeNativeSnapshot Native, int Omitted = 0);

/// <summary>Journal correlation, never removal authority.</summary>
public sealed record WorktreeCleanupContext(Guid AttemptId, Guid RequestId, Guid OperationId, Guid TaskId);

public sealed record WorktreeCleanupIdentity(Guid RequestId, Guid OperationId, Guid TaskId,
    string RepositoryPath, string WorktreePath, string CommonDirectory, string GitDirectory,
    string SourceFullRef, string TargetFullRef, string SourceSha, string TargetSha);

public sealed record WorktreeCleanupReference(Guid AttemptId, Guid RequestId, Guid OperationId,
    DateTime? At, WorktreeCleanupCaptureState State, string? Summary, string? CaptureJson,
    bool PriorAttempt = false);
