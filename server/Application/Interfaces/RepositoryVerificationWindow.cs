namespace Antiphon.Server.Application.Interfaces;

/// <summary>A bounded repository operation, distinct from an advisory lease owner tag.</summary>
public sealed record RepositoryPreflight
{
    private RepositoryPreflight(Guid identity, string? sha, string? path, string? branch)
        => (Identity, StartRefSha, WorktreePath, Branch) = (identity, sha, path, branch);
    public Guid Identity { get; }
    public string? StartRefSha { get; }
    public string? WorktreePath { get; }
    public string? Branch { get; }
    public static RepositoryPreflight Fetch(Guid invocation, string sha) => new(invocation, sha, null, null);
    public static RepositoryPreflight Worktree(Guid task, string path, string branch) => new(task, null, path, branch);
}

public sealed record RepositoryVerificationIdentity(Guid TaskId, Guid RequestId, int Attempt, Guid OperationId,
    string CandidateSha, string WorktreePath, string SourcePath, string SourceRef, string TargetRef);

/// <summary>Live coordinator capability; disk metadata alone never grants admission.</summary>
public abstract class RepositoryVerificationWindow : IAsyncDisposable
{
    public abstract Guid Id { get; }
    public abstract ValueTask DisposeAsync();
}
