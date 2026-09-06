namespace Antiphon.Server.Application.Interfaces;

/// <summary>Contained card-file filesystem and literal Git operations (CARD-0408).</summary>
public interface ICardFileRepository
{
    void ValidatePath(string root, string path);
    Task<IReadOnlyList<string>> GetManagedGitPathsAsync(string root, string directory, CancellationToken ct);
    Task<CardFileCommitResult> CommitAsync(string root, string directory,
        IReadOnlyDictionary<string, string?> expected, string subject, CancellationToken ct);
    Task<CardFileRepositoryState> InspectAsync(string root, string directory, CancellationToken ct);
    Task<string> HashAsync(string root, string relativePath, string content, CancellationToken ct);
    Task UnstageAsync(string root, string directory, IReadOnlyList<string> paths, CancellationToken ct);
    Task<bool> IsIgnoredAsync(string root, IReadOnlyList<string> paths, CancellationToken ct);
    Task<bool> HasIgnoreProtectionAsync(string root, IReadOnlyList<string> enabledSlugs, CancellationToken ct);
    Task InstallIgnoreAsync(string root, CancellationToken ct);
    Task<string?> ReadAsync(string root, string path, CancellationToken ct);
    void Delete(string root, string path);
    Task WriteAsync(string root, string path, string content, Guid boardId, CancellationToken ct);
    void RemoveTemporaryFiles(string root, string directory, Guid boardId);
}

public sealed record CardFileCommitResult(string? Sha, string? SkipReason, string? Error);
public sealed record CardFileRepositoryState(IReadOnlyList<string> WorkingPaths,
    IReadOnlyDictionary<string, string> Index, IReadOnlyDictionary<string, string> Head);
