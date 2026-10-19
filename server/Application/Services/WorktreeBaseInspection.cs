using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;

namespace Antiphon.Server.Application.Services;

public sealed class WorktreeBaseInspectionLimitException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>One monotonic deadline covers discovery, gate admission and every Git observation.</summary>
public sealed class WorktreeBaseInspection : IDisposable
{
    private readonly ILandingGit git;
    private readonly string repository;
    private readonly CancellationToken caller;
    private readonly TimeProvider _clock;
    private readonly GitProcessGate _gate;
    private readonly long _started;
    private readonly TimeSpan _limit;
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationTokenSource _budget;
    private readonly int _maxCommands;
    private readonly Dictionary<string, string> _commons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _commits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _checkouts = new(StringComparer.Ordinal);
    private bool _worktreesListed;
    public int Commands { get; private set; }
    public int MaxCandidates { get; }
    public int MaxInventoryRows { get; }
    public CancellationToken Token => _budget.Token;
    public TimeSpan Elapsed => _clock.GetElapsedTime(_started);
    public TimeSpan Remaining => _limit - Elapsed;

    public async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> read)
    {
        CheckDeadline();
        try
        {
            var value = await read(Token);
            CheckDeadline();
            return value;
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        { throw new WorktreeBaseInspectionLimitException("inspection_timeout"); }
    }

    public WorktreeBaseInspection(ILandingGit git, string repository, GitSettings settings,
        TimeProvider clock, GitProcessGate gate, CancellationToken caller)
    {
        this.git = git;
        this.repository = repository;
        this.caller = caller;
        _clock = clock;
        _gate = gate;
        _started = clock.GetTimestamp();
        _limit = TimeSpan.FromSeconds(Math.Max(1, settings.WorktreeBaseInspectionTimeoutSeconds));
        _deadline = new CancellationTokenSource(_limit, clock);
        _budget = CancellationTokenSource.CreateLinkedTokenSource(caller, _deadline.Token);
        _maxCommands = Math.Max(1, settings.WorktreeBaseMaxGitCommands);
        MaxCandidates = Math.Max(1, settings.WorktreeBaseMaxCandidates);
        MaxInventoryRows = Math.Max(MaxCandidates, settings.WorktreeBaseMaxInventoryRows);
    }

    public void CheckDeadline()
    {
        caller.ThrowIfCancellationRequested();
        if (_clock.GetElapsedTime(_started) >= _limit || _deadline.IsCancellationRequested)
            throw new WorktreeBaseInspectionLimitException("inspection_timeout");
    }

    public async Task<LandingGitResult> RunAsync(string repo, params string[] args)
    {
        CheckDeadline();
        if (Commands >= _maxCommands) throw new WorktreeBaseInspectionLimitException("git_command_limit");
        try
        {
            using var admission = await _gate.EnterAsync(_budget.Token);
            CheckDeadline();
            Commands++;
            var result = await git.RunAsync(repo, args, _budget.Token);
            CheckDeadline();
            return result;
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        { throw new WorktreeBaseInspectionLimitException("inspection_timeout"); }
    }

    public async Task<string> CommonAsync(string repo)
    {
        if (_commons.TryGetValue(repo, out var cached)) return cached;
        var result = await RunAsync(repo, "rev-parse", "--path-format=absolute", "--git-common-dir");
        if (!result.Succeeded) throw new IOException("git_common_directory_unavailable");
        string canonical;
        try
        {
            canonical = await git.CanonicalDirectoryAsync(result.Output.Trim(), _budget.Token);
            CheckDeadline();
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        { throw new WorktreeBaseInspectionLimitException("inspection_timeout"); }
        return _commons[repo] = canonical;
    }

    public async Task<string?> CommitAsync(string reference)
    {
        if (_commits.TryGetValue(reference, out var cached)) return cached;
        var result = await RunAsync(repository, "rev-parse", "--verify", "--quiet", $"{reference}^{{commit}}");
        if (!result.Succeeded && result.ExitCode != 1)
            throw new IOException("git_commit_probe_failed");
        var sha = result.Succeeded ? result.Output.Trim() : null;
        return _commits[reference] = sha is { Length: 40 or 64 }
            && sha.All(Uri.IsHexDigit) ? sha.ToLowerInvariant() : null;
    }

    public Task<Dictionary<string, string>> BranchCommitsAsync(string[] branches) =>
        ReadBranchesAsync(branches, null);

    public Task<Dictionary<string, string>> MergedBranchesAsync(string target, string[] branches) =>
        ReadBranchesAsync(branches, target);

    private async Task<Dictionary<string, string>> ReadBranchesAsync(string[] branches, string? target)
    {
        var expected = branches.Select(b => "refs/heads/" + b).ToHashSet(StringComparer.Ordinal);
        var commits = new Dictionary<string, string>(StringComparer.Ordinal);
        if (expected.Count == 0) return commits;
        var args = new List<string> { "for-each-ref", "--format=%(objectname) %(refname)" };
        if (target is not null) args.Add("--merged=" + target);
        args.AddRange(expected.Order(StringComparer.Ordinal));
        var result = await RunAsync(repository, args.ToArray());
        if (!result.Succeeded) throw new IOException("git_branch_inventory_unavailable");
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', 2);
            if (parts.Length != 2) throw new IOException("git_branch_inventory_invalid");
            if (!expected.Contains(parts[1])) continue;
            if (!GitObjectId.IsFull(parts[0])) throw new IOException("git_branch_object_invalid");
            var branch = parts[1][11..];
            var sha = parts[0].ToLowerInvariant();
            if (commits.TryGetValue(branch, out var prior) && prior != sha)
                throw new IOException("git_branch_inventory_inconsistent");
            commits[branch] = sha;
        }
        return commits;
    }

    public async Task<bool> AncestorAsync(string ancestor, string descendant)
    {
        var result = await RunAsync(repository, "merge-base", "--is-ancestor", ancestor, descendant);
        if (result.ExitCode is not (0 or 1)) throw new WorktreeBaseInspectionLimitException("git_inspection_error");
        return result.Succeeded;
    }

    public async Task<DelegationWorktreeService.CommitContainment> ContainsAsync(string source, string target)
    {
        var ancestor = await RunAsync(repository, "merge-base", "--is-ancestor", source, target);
        if (ancestor.Succeeded) return DelegationWorktreeService.CommitContainment.Contained;
        if (ancestor.ExitCode != 1) return DelegationWorktreeService.CommitContainment.Unknown;
        return await ContainsUnmergedAsync(source, target);
    }

    public async Task<DelegationWorktreeService.CommitContainment> ContainsUnmergedAsync(string source, string target)
    {
        var merges = await RunAsync(repository, "rev-list", "--max-count=1", "--min-parents=2", $"{target}..{source}");
        if (!merges.Succeeded || !string.IsNullOrWhiteSpace(merges.Output))
            return DelegationWorktreeService.CommitContainment.Unknown;
        var cherry = await RunAsync(repository, "cherry", target, source);
        if (!cherry.Succeeded) return DelegationWorktreeService.CommitContainment.Unknown;
        var lines = cherry.Output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Any(static line => !line.StartsWith('+') && !line.StartsWith('-')))
            return DelegationWorktreeService.CommitContainment.Unknown;
        return lines.Any(static line => line.StartsWith('+'))
            ? DelegationWorktreeService.CommitContainment.NotContained
            : DelegationWorktreeService.CommitContainment.Contained;
    }

    public async Task<bool> CheckoutSafeAsync(string branch)
    {
        if (!_worktreesListed)
        {
            var listing = await RunAsync(repository, "worktree", "list", "--porcelain");
            if (!listing.Succeeded) throw new IOException("git_worktree_listing_failed");
            string? current = null;
            foreach (var line in listing.Output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (line.StartsWith("worktree ", StringComparison.Ordinal)) current = line[9..];
                if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal))
                    _checkouts[line[18..]] = current;
            }
            _worktreesListed = true;
        }
        var path = _checkouts.GetValueOrDefault(branch);
        if (path is null) return true;
        if (!Directory.Exists(path)) return false;
        var status = await RunAsync(path, "status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none");
        if (!status.Succeeded) throw new IOException("git_checkout_status_failed");
        if (status.Output.Length != 0) return false;
        foreach (var state in new[] { "MERGE_HEAD", "rebase-merge", "rebase-apply" })
        {
            var location = await RunAsync(path, "rev-parse", "--git-path", state);
            if (!location.Succeeded) throw new IOException("git_checkout_operation_probe_failed");
            var gitPath = location.Output.Trim();
            if (File.Exists(gitPath) || Directory.Exists(gitPath)) return false;
        }
        return true;
    }

    public void Dispose()
    {
        _budget.Dispose();
        _deadline.Dispose();
    }
}
