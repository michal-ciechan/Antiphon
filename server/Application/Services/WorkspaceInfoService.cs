using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// TTL-cached git identity for the home screen's workspace switcher: which repo each agent
/// directory belongs to (batch), and a repo's full worktree list. The client polls these on
/// the agent-list cadence, so hits inside the TTL cost nothing; a git failure or missing
/// directory reads as "not a repository", never an error.
/// </summary>
public sealed class WorkspaceInfoService : IResettableCache
{
    // Long enough to absorb polling, short enough that a `git switch` shows up promptly.
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(20);
    // A batch is "every distinct agent/task directory on screen" — cap it so a hostile or
    // buggy caller can't fan out unbounded git processes.
    private const int MaxPathsPerRequest = 64;

    private readonly GitWorkspaceService _git;
    private readonly ConcurrentDictionary<string, (DateTime At, WorkspaceGitInfoDto Info)> _infoCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DateTime At, WorkspaceWorktreesDto List)> _worktreeCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<WorkspaceGitInfoDto>>> _infoInFlight =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<WorkspaceWorktreesDto>>> _worktreeInFlight =
        new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceInfoService(GitWorkspaceService git) => _git = git;

    public async Task<IReadOnlyList<WorkspaceGitInfoDto>> GetWorkspacesAsync(
        IReadOnlyList<string> paths, CancellationToken ct)
    {
        var distinct = paths
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .DistinctBy(Normalize)
            .Take(MaxPathsPerRequest)
            .ToList();

        var results = await Task.WhenAll(distinct.Select(p => GetWorkspaceAsync(p, ct)));
        return results;
    }

    public async Task<WorkspaceGitInfoDto> GetWorkspaceAsync(string path, CancellationToken ct)
    {
        var key = Normalize(path);
        if (_infoCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < Ttl)
            return hit.Info;

        var operation = _infoInFlight.GetOrAdd(
            key,
            _ => new Lazy<Task<WorkspaceGitInfoDto>>(
                () => ComputeWorkspaceAsync(path), LazyThreadSafetyMode.ExecutionAndPublication));
        var task = operation.Value;
        try
        {
            return await task.WaitAsync(ct);
        }
        finally
        {
            if (task.IsCompleted)
                ((ICollection<KeyValuePair<string, Lazy<Task<WorkspaceGitInfoDto>>>>)_infoInFlight)
                    .Remove(new(key, operation));
        }
    }

    public async Task<WorkspaceWorktreesDto> GetWorktreesAsync(string path, CancellationToken ct)
    {
        var key = Normalize(path);
        if (_worktreeCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < Ttl)
            return hit.List;

        var operation = _worktreeInFlight.GetOrAdd(
            key,
            _ => new Lazy<Task<WorkspaceWorktreesDto>>(
                () => ComputeWorktreesAsync(path), LazyThreadSafetyMode.ExecutionAndPublication));
        var task = operation.Value;
        try
        {
            return await task.WaitAsync(ct);
        }
        finally
        {
            if (task.IsCompleted)
                ((ICollection<KeyValuePair<string, Lazy<Task<WorkspaceWorktreesDto>>>>)_worktreeInFlight)
                    .Remove(new(key, operation));
        }
    }

    public void Clear()
    {
        _infoCache.Clear();
        _worktreeCache.Clear();
        _infoInFlight.Clear();
        _worktreeInFlight.Clear();
    }

    private async Task<WorkspaceGitInfoDto> ComputeWorkspaceAsync(string path)
    {
        var info = Directory.Exists(path)
            ? await _git.GetWorkspaceInfoAsync(path, CancellationToken.None)
            : new WorkspaceGitInfoDto(path, false, null, null, false);
        _infoCache[Normalize(path)] = (DateTime.UtcNow, info);
        return info;
    }

    private async Task<WorkspaceWorktreesDto> ComputeWorktreesAsync(string path)
    {
        var info = await GetWorkspaceAsync(path, CancellationToken.None);
        IReadOnlyList<WorktreeEntryDto> worktrees = info.IsGitRepository
            ? await _git.ListWorktreesAsync(path, CancellationToken.None)
            : [];
        var list = new WorkspaceWorktreesDto(path, info.IsGitRepository, info.RepoRoot, worktrees);
        _worktreeCache[Normalize(path)] = (DateTime.UtcNow, list);
        return list;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\', '/');
        }
        catch
        {
            // Unparseable input still deserves a stable cache identity.
            return path.Trim();
        }
    }
}
