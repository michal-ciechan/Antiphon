using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class WorktreeManager : IWorktreeManager
{
    private const string BranchPrefix = "feat/card-";
    private const string DirectoryPrefix = "card-";
    private const string MetadataDirectoryName = ".antiphon";
    private const string WorktreeMetadataDirectoryName = "worktrees";
    private static readonly Regex CardIdPattern = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly GitSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorktreeManager> _logger;

    public WorktreeManager(
        IOptions<GitSettings> settings,
        TimeProvider timeProvider,
        ILogger<WorktreeManager> logger)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
    {
        var repoFullPath = ResolveExistingDirectory(repoPath, nameof(repoPath));
        await EnsureGitRepositoryAsync(repoFullPath, ct);
        ValidateBaseRef(baseRef);

        var validatedCardId = ValidateCardId(cardId);
        var branch = BuildBranchName(validatedCardId);
        var worktreeRoot = ResolveWorktreeRoot(create: true);
        var worktreePath = Path.GetFullPath(Path.Combine(worktreeRoot, BuildDirectoryName(validatedCardId)));
        EnsurePathUnderRoot(worktreePath, worktreeRoot, nameof(worktreePath));

        var healed = await TryHealStaleRegistrationAsync(repoFullPath, worktreePath, ct);

        if (Directory.Exists(worktreePath))
            throw new ConflictException($"Worktree path already exists: {worktreePath}");

        var branchExistedBefore = await BranchExistsAsync(repoFullPath, branch, ct);
        if (branchExistedBefore && !healed)
            throw new ConflictException($"Worktree branch already exists: {branch}");

        await EnsureRefExistsAsync(repoFullPath, baseRef, ct);

        try
        {
            if (branchExistedBefore)
            {
                // Re-attach the existing branch (CARD-0220): whatever a previous attempt committed
                // is preserved. Same rule as DelegationWorktreeService's adopt arm.
                await RunGitAsync(repoFullPath, ["worktree", "add", worktreePath, branch], ct);
            }
            else
            {
                await RunGitAsync(repoFullPath, ["worktree", "add", "-b", branch, worktreePath, baseRef], ct);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            await RollbackFailedAddAsync(repoFullPath, worktreePath, branch, branchExistedBefore);
            throw;
        }

        if (healed)
            await DeleteMetadataForPathAsync(worktreeRoot, worktreePath, ct);

        var now = _timeProvider.GetUtcNow();
        var info = new WorktreeInfo(
            validatedCardId,
            repoFullPath,
            worktreePath,
            branch,
            baseRef,
            now,
            now);

        await SaveMetadataAsync(ToMetadata(info), ct);
        return info;
    }

    public async Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct)
    {
        var repoFullPath = ResolveExistingDirectory(repoPath, nameof(repoPath));
        await EnsureGitRepositoryAsync(repoFullPath, ct);

        var worktreeRoot = ResolveWorktreeRoot(create: true);
        var metadataByPath = await LoadMetadataByPathAsync(worktreeRoot, ct);
        var result = await RunGitAsync(repoFullPath, ["worktree", "list", "--porcelain"], ct);
        var entries = ParseWorktreeList(result.Stdout);
        var worktrees = new List<WorktreeInfo>();

        foreach (var entry in entries)
        {
            var path = Path.GetFullPath(entry.Path);
            if (!IsPathUnderRoot(path, worktreeRoot))
                continue;

            var branch = NormalizeBranchName(entry.Branch);
            if (!IsAntiphonBranch(branch))
                continue;

            if (metadataByPath.TryGetValue(NormalizePathForComparison(path), out var metadata))
            {
                worktrees.Add(ToInfo(metadata));
                continue;
            }

            var cardId = branch[BranchPrefix.Length..];
            var createdAt = Directory.Exists(path)
                ? new DateTimeOffset(Directory.GetCreationTimeUtc(path), TimeSpan.Zero)
                : _timeProvider.GetUtcNow();
            var lastTouchedAt = Directory.Exists(path)
                ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero)
                : createdAt;

            worktrees.Add(new WorktreeInfo(
                cardId,
                repoFullPath,
                path,
                branch,
                string.Empty,
                createdAt,
                lastTouchedAt));
        }

        return worktrees;
    }

    public async Task<IReadOnlyList<DelegateWorktreeScanEntry>> ScanDelegateWorktreesAsync(
        string repoPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            return [];

        var repoFullPath = Path.GetFullPath(repoPath);
        var list = await RunGitAsync(repoFullPath, ["worktree", "list", "--porcelain"], ct, throwOnError: false);
        if (list.ExitCode != 0)
        {
            _logger.LogWarning(
                "Worktree health scan skipped {RepoPath}: git worktree list failed ({ExitCode}): {StdErr}",
                repoFullPath,
                list.ExitCode,
                list.Stderr.Trim());
            return [];
        }

        var result = new List<DelegateWorktreeScanEntry>();
        var registeredBranches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ParseWorktreeList(list.Stdout))
        {
            var branch = NormalizeBranchName(entry.Branch);
            if (!TryParseDelegateTaskBranch(branch, out var shortId))
                continue;

            registeredBranches.Add(branch);
            var path = Path.GetFullPath(entry.Path);
            var directoryExists = Directory.Exists(path);
            var gitMarker = Path.Combine(path, ".git");
            result.Add(new DelegateWorktreeScanEntry(
                repoFullPath,
                path,
                branch,
                shortId,
                Registered: true,
                Locked: entry.Locked,
                LockReason: entry.LockReason,
                DirectoryExists: directoryExists,
                GitFileExists: File.Exists(gitMarker) || Directory.Exists(gitMarker)));
        }

        var branches = await RunGitAsync(
            repoFullPath, ["branch", "--list", "feat/card-task-*"], ct, throwOnError: false);
        if (branches.ExitCode == 0)
        {
            foreach (var raw in branches.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal)
                         .Replace('\r', '\n')
                         .Split('\n'))
            {
                var name = raw.Trim().TrimStart('*').Trim();
                if (!TryParseDelegateTaskBranch(name, out var shortId))
                    continue;
                if (registeredBranches.Contains(name))
                    continue;

                result.Add(new DelegateWorktreeScanEntry(
                    repoFullPath,
                    Path: string.Empty,
                    Branch: name,
                    ShortId: shortId,
                    Registered: false,
                    Locked: false,
                    LockReason: null,
                    DirectoryExists: false,
                    GitFileExists: false));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<string>> ListKnownDelegateRepoPathsAsync(CancellationToken ct)
    {
        var worktreeRoot = ResolveWorktreeRoot(create: false);
        if (!Directory.Exists(worktreeRoot))
            return [];

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var repos = new HashSet<string>(comparer);
        foreach (var record in await LoadMetadataRecordsAsync(worktreeRoot, ct))
        {
            if (!string.IsNullOrWhiteSpace(record.Metadata.RepoPath)
                && Directory.Exists(record.Metadata.RepoPath))
            {
                repos.Add(Path.GetFullPath(record.Metadata.RepoPath));
            }
        }

        return repos.ToList();
    }

    public async Task<IReadOnlyList<WorktreeResidueScanEntry>> ScanResidueCandidatesAsync(
        IReadOnlyList<string> extraRepoPaths, CancellationToken ct)
    {
        var worktreeRoot = ResolveWorktreeRoot(create: false);
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var byPath = new Dictionary<string, WorktreeResidueScanEntry>(comparer);
        var dangling = new List<WorktreeResidueScanEntry>();

        var repos = new HashSet<string>(comparer);
        foreach (var known in await ListKnownDelegateRepoPathsAsync(ct))
            AddRepoIfPresent(repos, known);
        foreach (var extra in extraRepoPaths)
            AddRepoIfPresent(repos, extra);

        foreach (var repo in repos)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<DelegateWorktreeScanEntry> scan;
            try
            {
                scan = await ScanDelegateWorktreesAsync(repo, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Worktree residue scan skipped for {RepoPath}", repo);
                continue;
            }

            foreach (var entry in scan)
            {
                if (entry.Registered)
                {
                    if (!Directory.Exists(worktreeRoot) || !IsPathUnderRoot(entry.Path, worktreeRoot))
                        continue;
                    var path = Path.GetFullPath(entry.Path);
                    byPath[NormalizePathForComparison(path)] = new WorktreeResidueScanEntry(
                        path,
                        entry.Branch,
                        repo,
                        Registered: true,
                        DirectoryExists: entry.DirectoryExists);
                    continue;
                }

                var expected = Directory.Exists(worktreeRoot) && TryParseDelegateTaskBranch(entry.Branch, out var shortId)
                    ? Path.GetFullPath(Path.Combine(worktreeRoot, $"card-task-{shortId}"))
                    : string.Empty;
                if (expected.Length > 0 && byPath.ContainsKey(NormalizePathForComparison(expected)))
                    continue;
                dangling.Add(new WorktreeResidueScanEntry(
                    expected,
                    entry.Branch,
                    repo,
                    Registered: false,
                    DirectoryExists: expected.Length > 0 && Directory.Exists(expected)));
            }
        }

        if (Directory.Exists(worktreeRoot))
        {
            var metadataByPath = await LoadMetadataByPathAsync(worktreeRoot, ct);
            foreach (var dir in Directory.EnumerateDirectories(worktreeRoot))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)
                    || !name.StartsWith("card-task-", StringComparison.OrdinalIgnoreCase))
                    continue;

                var path = Path.GetFullPath(dir);
                var key = NormalizePathForComparison(path);
                if (byPath.ContainsKey(key))
                    continue;
                if (dangling.Any(d => d.Path.Length > 0 && PathsEqual(d.Path, path)))
                    continue;

                var branch = InferAntiphonBranchFromDirectory(path) ?? $"feat/{name}";
                string? repo = null;
                if (metadataByPath.TryGetValue(key, out var metadata)
                    && !string.IsNullOrWhiteSpace(metadata.RepoPath))
                {
                    repo = metadata.RepoPath;
                    branch = string.IsNullOrWhiteSpace(metadata.Branch) ? branch : metadata.Branch;
                }
                else
                {
                    repo = TryRepoFromGitFile(path);
                }

                byPath[key] = new WorktreeResidueScanEntry(
                    path,
                    branch,
                    repo,
                    Registered: false,
                    DirectoryExists: true);
            }
        }

        foreach (var entry in dangling)
        {
            if (entry.Path.Length > 0 && byPath.ContainsKey(NormalizePathForComparison(entry.Path)))
                continue;
            if (entry.Path.Length > 0)
                byPath[NormalizePathForComparison(entry.Path)] = entry;
            else
                byPath[$"branch:{entry.RepoPath}:{entry.Branch}"] = entry;
        }

        return byPath.Values.ToList();
    }

    public async Task<WorktreeResidueGitState> InspectResidueAsync(
        string? repoPath, string? worktreePath, string? branch, string targetRef, CancellationToken ct)
    {
        var branchExists = false;
        var isAncestor = true;
        var ahead = 0;
        var tracked = false;
        var untracked = false;

        var repo = !string.IsNullOrWhiteSpace(repoPath) && Directory.Exists(repoPath)
            ? Path.GetFullPath(repoPath)
            : null;
        var normalizedBranch = string.IsNullOrWhiteSpace(branch) ? null : NormalizeBranchName(branch);

        if (repo is not null && normalizedBranch is not null)
        {
            branchExists = await BranchExistsAsync(repo, normalizedBranch, ct);
            if (branchExists && !string.IsNullOrWhiteSpace(targetRef))
            {
                var ancestor = await RunGitAsync(
                    repo,
                    ["merge-base", "--is-ancestor", normalizedBranch, targetRef],
                    ct,
                    throwOnError: false);
                isAncestor = ancestor.ExitCode == 0;
                var count = await RunGitAsync(
                    repo,
                    ["rev-list", "--count", $"{targetRef}..{normalizedBranch}"],
                    ct,
                    throwOnError: false);
                if (count.ExitCode == 0)
                    _ = int.TryParse(count.Stdout.Trim(), out ahead);
            }
        }

        var tree = !string.IsNullOrWhiteSpace(worktreePath) && Directory.Exists(worktreePath)
            ? Path.GetFullPath(worktreePath)
            : null;
        if (tree is not null)
        {
            var status = await RunGitAsync(
                tree,
                ["status", "--porcelain", "-z", "--untracked-files=all"],
                ct,
                throwOnError: false);
            if (status.ExitCode == 0)
                (tracked, untracked) = ParsePorcelainDirtiness(status.Stdout);
        }

        return new WorktreeResidueGitState(branchExists, isAncestor, ahead, tracked, untracked);
    }

    internal static (bool Tracked, bool Untracked) ParsePorcelainDirtiness(string stdout)
    {
        var tracked = false;
        var untracked = false;
        var records = stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length < 2)
                continue;
            if (record[0] == '?' || (record.Length > 1 && record[1] == '?'))
                untracked = true;
            else
                tracked = true;
            if (record[0] is 'R' or 'C')
                i++;
        }

        return (tracked, untracked);
    }

    internal static bool TryParseDelegateTaskBranch(string? branch, out string shortId)
    {
        shortId = string.Empty;
        if (string.IsNullOrWhiteSpace(branch))
            return false;

        const string prefix = "feat/card-task-";
        var name = NormalizeBranchName(branch);
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = name[prefix.Length..];
        if (rest.Length != 8)
            return false;
        foreach (var c in rest)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }

        shortId = rest.ToLowerInvariant();
        return true;
    }

    public async Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct)
    {
        var result = await TryRemoveAsync(repoPath, worktreePath, mergedInto: null, ct);
        if (!result.IsClean)
            throw new InvalidOperationException(result.Residue ?? "Worktree removal left residue.");
    }

    public async Task<WorktreeRemoval> TryRemoveAsync(
        string repoPath, string worktreePath, string? mergedInto, CancellationToken ct)
    {
        var repoFullPath = ResolveExistingDirectory(repoPath, nameof(repoPath));
        await EnsureGitRepositoryAsync(repoFullPath, ct);

        var worktreeRoot = ResolveWorktreeRoot(create: true);
        var worktreeFullPath = Path.GetFullPath(worktreePath);
        EnsurePathUnderRoot(worktreeFullPath, worktreeRoot, nameof(worktreePath));

        var metadata = await FindMetadataByPathAsync(worktreeRoot, worktreeFullPath, ct);
        var branch = metadata?.Branch;
        if (Directory.Exists(worktreeFullPath))
            branch = await TryGetCurrentBranchAsync(worktreeFullPath, ct) ?? branch;
        branch ??= InferAntiphonBranchFromDirectory(worktreeFullPath);

        if (!IsAntiphonBranch(branch))
            throw new ValidationException(nameof(worktreePath), "Worktree is not an Antiphon-managed feat/card-* worktree.");

        string? gitRemoveError = null;
        var removeFailedOrTimedOut = false;
        var locked = false;

        try
        {
            var remove = await RunGitAsync(
                repoFullPath,
                ["worktree", "remove", "--force", worktreeFullPath],
                ct,
                throwOnError: false);

            if (remove.ExitCode == 0)
            {
                // Unregistered (and usually deleted). Directory leftovers are step 2's job only
                // when this command failed or timed out.
            }
            else if (IsAlreadyUnregisteredWorktreeError(remove.Stderr))
            {
                _logger.LogWarning(
                    "git worktree remove --force reported {Path} is not a working tree (already unregistered); deleting leftover directory. stderr: {StdErr}",
                    worktreeFullPath,
                    remove.Stderr);
                var leftover = TryDeleteDirectory(worktreeFullPath);
                if (leftover is not null)
                    gitRemoveError = leftover;
            }
            else
            {
                removeFailedOrTimedOut = true;
                gitRemoveError = FirstLine(remove.Stderr);
                locked = IsLockedWorktreeError(remove.Stderr);
                _logger.LogWarning(
                    "git worktree remove --force failed (exit {ExitCode}): {StdErr}",
                    remove.ExitCode,
                    remove.Stderr);
            }
        }
        catch (TimeoutException ex)
        {
            removeFailedOrTimedOut = true;
            gitRemoveError = ex.Message;
            _logger.LogWarning(ex, "git worktree remove --force timed out for {Path}", worktreeFullPath);
        }

        string? directoryDeleteError = null;
        if (removeFailedOrTimedOut && !locked)
        {
            directoryDeleteError = TryDeleteDirectory(worktreeFullPath);
            await RunGitAsync(repoFullPath, ["worktree", "prune"], ct, throwOnError: false);
        }

        var unregistered = !await IsRegisteredAsync(repoFullPath, worktreeFullPath, ct);
        var directoryGone = !Directory.Exists(worktreeFullPath);

        var (branchDeleted, branchResidue) = await TryDeleteBranchAsync(
            repoFullPath, branch!, mergedInto, ct);

        var directoryReason = !directoryGone
            ? DirectoryResidueReason(gitRemoveError, directoryDeleteError)
            : null;
        var residue = ComposeResidue(
            unregistered, directoryGone, branchDeleted,
            worktreeFullPath, directoryReason, branchResidue);

        if (residue is null)
        {
            await DeleteMetadataForPathAsync(worktreeRoot, worktreeFullPath, ct);
        }
        else if (metadata is not null)
        {
            await SaveMetadataAsync(
                metadata with { ResidueSince = metadata.ResidueSince ?? _timeProvider.GetUtcNow() },
                ct);
        }

        return new WorktreeRemoval(unregistered, directoryGone, branchDeleted, residue);
    }

    public async Task TouchAsync(string worktreePath, CancellationToken ct)
    {
        var worktreeRoot = ResolveWorktreeRoot(create: true);
        var worktreeFullPath = Path.GetFullPath(worktreePath);
        EnsurePathUnderRoot(worktreeFullPath, worktreeRoot, nameof(worktreePath));

        var metadata = await FindMetadataByPathAsync(worktreeRoot, worktreeFullPath, ct);
        if (metadata is null)
            throw new NotFoundException("Worktree", worktreeFullPath);

        await SaveMetadataAsync(metadata with { LastTouchedAt = _timeProvider.GetUtcNow() }, ct);
    }

    public async Task<int> PruneStaleAsync(CancellationToken ct)
    {
        var worktreeRoot = ResolveWorktreeRoot(create: true);
        var staleAfter = TimeSpan.FromDays(Math.Max(1, _settings.WorktreeStaleAfterDays));
        var cutoff = _timeProvider.GetUtcNow() - staleAfter;
        var records = await LoadMetadataRecordsAsync(worktreeRoot, ct);
        var pruned = 0;

        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();

            var metadata = record.Metadata;
            if (metadata.ResidueSince is null && metadata.LastTouchedAt > cutoff)
                continue;

            var worktreePath = Path.GetFullPath(metadata.Path);
            if (!IsPathUnderRoot(worktreePath, worktreeRoot))
            {
                _logger.LogWarning("Skipping stale worktree metadata outside root: {Path}", metadata.Path);
                continue;
            }

            if (!IsAntiphonBranch(metadata.Branch))
            {
                _logger.LogWarning("Skipping stale worktree metadata for non-Antiphon branch: {Branch}", metadata.Branch);
                continue;
            }

            if (!Directory.Exists(metadata.RepoPath))
            {
                _logger.LogWarning("Skipping stale worktree metadata because repo path does not exist: {RepoPath}", metadata.RepoPath);
                continue;
            }

            try
            {
                var removal = await TryRemoveAsync(metadata.RepoPath, worktreePath, mergedInto: null, ct);
                if (removal.IsClean)
                    pruned++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to prune stale worktree {Path}", metadata.Path);
            }
        }

        return pruned;
    }

    internal static string ValidateCardId(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            throw new ValidationException(nameof(cardId), "Card id must not be empty.");

        if (cardId != cardId.Trim())
            throw new ValidationException(nameof(cardId), "Card id must not contain leading or trailing whitespace.");

        if (cardId.Contains("..", StringComparison.Ordinal))
            throw new ValidationException(nameof(cardId), "Card id must not contain path traversal segments.");

        if (!CardIdPattern.IsMatch(cardId))
            throw new ValidationException(nameof(cardId), "Card id may only contain letters, numbers, dots, underscores, and hyphens.");

        return cardId;
    }

    internal static string BuildBranchName(string cardId) => $"{BranchPrefix}{ValidateCardId(cardId)}";

    internal static string BuildDirectoryName(string cardId) => $"{DirectoryPrefix}{ValidateCardId(cardId)}";

    internal static bool IsPathUnderRoot(string path, string root)
    {
        var fullPath = NormalizePathForComparison(path);
        var fullRoot = NormalizePathForComparison(root);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!Path.EndsInDirectorySeparator(fullRoot))
            fullRoot += Path.DirectorySeparatorChar;

        return fullPath.StartsWith(fullRoot, comparison);
    }

    internal static IReadOnlyList<WorktreePorcelainEntry> ParseWorktreeList(string stdout)
    {
        var entries = new List<WorktreePorcelainEntry>();
        string? path = null;
        string? branch = null;
        var locked = false;
        string? lockReason = null;
        var prunable = false;
        string? prunableReason = null;

        foreach (var rawLine in stdout.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                AddEntry();
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
                path = line["worktree ".Length..];
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
                branch = line["branch ".Length..];
            else if (line.Equals("locked", StringComparison.Ordinal)
                     || line.StartsWith("locked ", StringComparison.Ordinal))
            {
                locked = true;
                lockReason = line.Equals("locked", StringComparison.Ordinal)
                    ? string.Empty
                    : line["locked ".Length..];
            }
            else if (line.Equals("prunable", StringComparison.Ordinal)
                     || line.StartsWith("prunable ", StringComparison.Ordinal))
            {
                prunable = true;
                prunableReason = line.Equals("prunable", StringComparison.Ordinal)
                    ? string.Empty
                    : line["prunable ".Length..];
            }
        }

        AddEntry();
        return entries;

        void AddEntry()
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                entries.Add(new WorktreePorcelainEntry(
                    path,
                    branch ?? string.Empty,
                    locked,
                    lockReason,
                    prunable,
                    prunableReason));
            }

            path = null;
            branch = null;
            locked = false;
            lockReason = null;
            prunable = false;
            prunableReason = null;
        }
    }

    private string ResolveWorktreeRoot(bool create)
    {
        if (string.IsNullOrWhiteSpace(_settings.WorktreeBasePath))
            throw new ValidationException("Git:WorktreeBasePath", "Worktree base path must be configured.");

        var root = Path.IsPathRooted(_settings.WorktreeBasePath)
            ? _settings.WorktreeBasePath
            : Path.Combine(AppContext.BaseDirectory, _settings.WorktreeBasePath);
        root = Path.GetFullPath(root);

        if (create)
            Directory.CreateDirectory(root);

        return root;
    }

    private static string ResolveExistingDirectory(string path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ValidationException(fieldName, "Path must not be empty.");

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new NotFoundException("Directory", fullPath);

        return fullPath;
    }

    private static void ValidateBaseRef(string baseRef)
    {
        if (string.IsNullOrWhiteSpace(baseRef))
            throw new ValidationException(nameof(baseRef), "Base ref must not be empty.");

        if (baseRef != baseRef.Trim())
            throw new ValidationException(nameof(baseRef), "Base ref must not contain leading or trailing whitespace.");

        if (baseRef[0] == '-' || baseRef.Any(char.IsControl))
            throw new ValidationException(nameof(baseRef), "Base ref contains invalid characters.");
    }

    private static void EnsurePathUnderRoot(string path, string root, string fieldName)
    {
        if (!IsPathUnderRoot(path, root))
            throw new ValidationException(fieldName, "Resolved worktree path must stay under Git:WorktreeBasePath.");
    }

    private async Task EnsureGitRepositoryAsync(string repoPath, CancellationToken ct)
    {
        var result = await RunGitAsync(repoPath, ["rev-parse", "--is-inside-work-tree"], ct, throwOnError: false);
        if (result.ExitCode != 0 || !result.Stdout.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException(nameof(repoPath), "Path must be a git working tree.");
    }

    private async Task EnsureRefExistsAsync(string repoPath, string baseRef, CancellationToken ct)
    {
        var result = await RunGitAsync(repoPath, ["rev-parse", "--verify", "--quiet", $"{baseRef}^{{commit}}"], ct, throwOnError: false);
        if (result.ExitCode != 0)
            throw new ValidationException(nameof(baseRef), $"Base ref '{baseRef}' does not resolve to a commit.");
    }

    private async Task<bool> BranchExistsAsync(string repoPath, string branch, CancellationToken ct)
    {
        var result = await RunGitAsync(repoPath, ["show-ref", "--verify", "--quiet", $"refs/heads/{branch}"], ct, throwOnError: false);
        return result.ExitCode == 0;
    }

    private async Task<string?> TryGetCurrentBranchAsync(string worktreePath, CancellationToken ct)
    {
        var result = await RunGitAsync(worktreePath, ["branch", "--show-current"], ct, throwOnError: false);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout)
            ? result.Stdout.Trim()
            : null;
    }

    private async Task<Dictionary<string, WorktreeMetadata>> LoadMetadataByPathAsync(string worktreeRoot, CancellationToken ct)
    {
        var records = await LoadMetadataRecordsAsync(worktreeRoot, ct);
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var byPath = new Dictionary<string, WorktreeMetadata>(comparer);

        foreach (var record in records)
        {
            byPath[NormalizePathForComparison(record.Metadata.Path)] = record.Metadata;
        }

        return byPath;
    }

    private async Task<WorktreeMetadata?> FindMetadataByPathAsync(string worktreeRoot, string worktreePath, CancellationToken ct)
    {
        var byPath = await LoadMetadataByPathAsync(worktreeRoot, ct);
        return byPath.TryGetValue(NormalizePathForComparison(worktreePath), out var metadata)
            ? metadata
            : null;
    }

    private async Task<IReadOnlyList<WorktreeMetadataRecord>> LoadMetadataRecordsAsync(string worktreeRoot, CancellationToken ct)
    {
        var metadataDirectory = GetMetadataDirectory(worktreeRoot);
        if (!Directory.Exists(metadataDirectory))
            return [];

        var records = new List<WorktreeMetadataRecord>();
        foreach (var filePath in Directory.EnumerateFiles(metadataDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(filePath, ct);
                var metadata = JsonSerializer.Deserialize<WorktreeMetadata>(json, JsonOptions);
                if (metadata is not null)
                    records.Add(new WorktreeMetadataRecord(filePath, metadata));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Skipping malformed worktree metadata file {Path}", filePath);
            }
        }

        return records;
    }

    private async Task SaveMetadataAsync(WorktreeMetadata metadata, CancellationToken ct)
    {
        var worktreeRoot = ResolveWorktreeRoot(create: true);
        EnsurePathUnderRoot(metadata.Path, worktreeRoot, nameof(metadata.Path));

        var metadataDirectory = GetMetadataDirectory(worktreeRoot);
        Directory.CreateDirectory(metadataDirectory);
        var filePath = GetMetadataFilePath(metadataDirectory, metadata.Path);
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    private async Task DeleteMetadataForPathAsync(string worktreeRoot, string worktreePath, CancellationToken ct)
    {
        var metadataDirectory = GetMetadataDirectory(worktreeRoot);
        if (!Directory.Exists(metadataDirectory))
            return;

        var expectedFile = GetMetadataFilePath(metadataDirectory, worktreePath);
        if (File.Exists(expectedFile))
        {
            await DeleteMetadataFileAsync(expectedFile, ct);
            return;
        }

        var records = await LoadMetadataRecordsAsync(worktreeRoot, ct);
        foreach (var record in records.Where(record =>
                     PathsEqual(record.Metadata.Path, worktreePath)))
        {
            await DeleteMetadataFileAsync(record.FilePath, ct);
        }
    }

    private static Task DeleteMetadataFileAsync(string filePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }

    private static string GetMetadataDirectory(string worktreeRoot) =>
        Path.Combine(worktreeRoot, MetadataDirectoryName, WorktreeMetadataDirectoryName);

    private static string GetMetadataFilePath(string metadataDirectory, string worktreePath) =>
        Path.Combine(metadataDirectory, $"{HashPath(worktreePath)}.json");

    private static string HashPath(string path)
    {
        var normalized = NormalizePathForComparison(path);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return NormalizePathForComparison(left).Equals(NormalizePathForComparison(right), comparison);
    }

    private static string NormalizePathForComparison(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string NormalizeBranchName(string branch)
    {
        const string refsHeadsPrefix = "refs/heads/";
        return branch.StartsWith(refsHeadsPrefix, StringComparison.Ordinal)
            ? branch[refsHeadsPrefix.Length..]
            : branch;
    }

    private static bool IsAntiphonBranch(string? branch) =>
        !string.IsNullOrWhiteSpace(branch)
        && branch.StartsWith(BranchPrefix, StringComparison.Ordinal)
        && branch.Length > BranchPrefix.Length;

    /// <summary>
    /// git 2.50: <c>fatal: '&lt;path&gt;' is not a working tree</c> (exit 128) when the directory
    /// is still on disk but git's registration under <c>.git/worktrees/</c> is already gone
    /// (CARD-0229 / CARD-0220 S4). Distinct from a still-registered tree whose <c>.git</c> is
    /// missing (<c>validation failed … /.git does not exist</c>).
    /// </summary>
    internal static bool IsAlreadyUnregisteredWorktreeError(string stderr) =>
        stderr.Contains("is not a working tree", StringComparison.Ordinal);

    internal static bool IsLockedWorktreeError(string stderr) =>
        stderr.Contains("locked working tree", StringComparison.OrdinalIgnoreCase)
        || stderr.Contains("cannot remove a locked", StringComparison.OrdinalIgnoreCase);

    private static string? InferAntiphonBranchFromDirectory(string worktreePath)
    {
        var name = Path.GetFileName(NormalizePathForComparison(worktreePath));
        if (string.IsNullOrEmpty(name)
            || !name.StartsWith(DirectoryPrefix, StringComparison.Ordinal)
            || name.Length <= DirectoryPrefix.Length)
            return null;

        var branch = "feat/" + name;
        return IsAntiphonBranch(branch) ? branch : null;
    }

    private static void AddRepoIfPresent(HashSet<string> repos, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        repos.Add(Path.GetFullPath(path));
    }

    /// <summary>
    /// A leftover worktree's <c>.git</c> file is <c>gitdir: &lt;repo&gt;/.git/worktrees/&lt;name&gt;</c>.
    /// </summary>
    internal static string? TryRepoFromGitFile(string worktreePath)
    {
        var gitFile = Path.Combine(worktreePath, ".git");
        if (!File.Exists(gitFile))
            return null;

        try
        {
            var text = File.ReadAllText(gitFile).Trim();
            const string prefix = "gitdir:";
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            var gitdir = text[prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(gitdir))
                return null;
            if (!Path.IsPathRooted(gitdir))
                gitdir = Path.GetFullPath(Path.Combine(worktreePath, gitdir));
            var worktreesDir = Path.GetDirectoryName(gitdir);
            if (worktreesDir is null
                || !string.Equals(Path.GetFileName(worktreesDir), "worktrees", StringComparison.OrdinalIgnoreCase))
                return null;
            var gitDir = Path.GetDirectoryName(worktreesDir);
            var repo = gitDir is null ? null : Path.GetDirectoryName(gitDir);
            return repo is not null && Directory.Exists(repo) ? Path.GetFullPath(repo) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<(bool Deleted, string? Residue)> TryDeleteBranchAsync(
        string repoPath, string branch, string? mergedInto, CancellationToken ct)
    {
        if (!await BranchExistsAsync(repoPath, branch, ct))
            return (true, null);

        if (mergedInto is not null)
        {
            var ancestor = await RunGitAsync(
                repoPath, ["merge-base", "--is-ancestor", branch, mergedInto], ct, throwOnError: false);
            if (ancestor.ExitCode != 0)
            {
                var count = await RunGitAsync(
                    repoPath, ["rev-list", "--count", $"{mergedInto}..{branch}"], ct, throwOnError: false);
                var ahead = count.ExitCode == 0 && int.TryParse(count.Stdout.Trim(), out var n) ? n : 0;
                return (false, $"branch kept: {ahead} commit(s) not on {mergedInto}");
            }
        }

        var deleted = await RunGitAsync(repoPath, ["branch", "-D", branch], ct, throwOnError: false);
        if (deleted.ExitCode == 0)
            return (true, null);

        _logger.LogWarning(
            "Failed to delete worktree branch {Branch} in {RepoPath}: {StdErr}",
            branch,
            repoPath,
            deleted.Stderr);
        var reason = FirstLine(deleted.Stderr);
        return (false, string.IsNullOrEmpty(reason)
            ? $"branch {branch} kept"
            : $"branch {branch} kept ({reason})");
    }

    private static string DirectoryResidueReason(string? gitRemoveError, string? directoryDeleteError)
    {
        if (!string.IsNullOrWhiteSpace(directoryDeleteError))
            return directoryDeleteError;
        if (!string.IsNullOrWhiteSpace(gitRemoveError))
            return gitRemoveError.StartsWith("git:", StringComparison.Ordinal)
                ? gitRemoveError
                : $"git: {gitRemoveError}";
        return "could not delete";
    }

    private static string? ComposeResidue(
        bool unregistered,
        bool directoryGone,
        bool branchDeleted,
        string path,
        string? directoryReason,
        string? branchResidue)
    {
        var parts = new List<string>();
        if (!directoryGone)
            parts.Add($"directory {path} still exists ({directoryReason ?? "could not delete"})");
        else if (!unregistered)
            parts.Add($"worktree registration for {path} remains");

        if (!branchDeleted)
            parts.Add(branchResidue ?? "branch kept");
        else if (parts.Count > 0)
            parts.Add("branch deleted");

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static string FirstLine(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return string.Empty;
        var newline = trimmed.IndexOfAny(['\r', '\n']);
        return newline < 0 ? trimmed : trimmed[..newline];
    }

    private static WorktreeMetadata ToMetadata(WorktreeInfo info) => new(
        SchemaVersion: 1,
        CardId: info.CardId,
        RepoPath: info.RepoPath,
        Path: info.Path,
        Branch: info.Branch,
        BaseRef: info.BaseRef,
        CreatedAt: info.CreatedAt,
        LastTouchedAt: info.LastTouchedAt,
        ResidueSince: null);

    private static WorktreeInfo ToInfo(WorktreeMetadata metadata) => new(
        metadata.CardId,
        metadata.RepoPath,
        metadata.Path,
        metadata.Branch,
        metadata.BaseRef,
        metadata.CreatedAt,
        metadata.LastTouchedAt);

    private TimeSpan TimeoutFor(IReadOnlyList<string> arguments)
    {
        if (arguments.Count >= 2
            && arguments[0].Equals("worktree", StringComparison.Ordinal)
            && arguments[1].Equals("add", StringComparison.Ordinal))
        {
            var seconds = _settings.WorktreeAddTimeoutSeconds > 0
                ? _settings.WorktreeAddTimeoutSeconds
                : 180;
            return TimeSpan.FromSeconds(seconds);
        }

        if (arguments.Count >= 2
            && arguments[0].Equals("worktree", StringComparison.Ordinal)
            && arguments[1].Equals("remove", StringComparison.Ordinal))
        {
            var seconds = _settings.WorktreeRemoveTimeoutSeconds > 0
                ? _settings.WorktreeRemoveTimeoutSeconds
                : 300;
            return TimeSpan.FromSeconds(seconds);
        }

        return GitTimeout;
    }

    /// <summary>
    /// A registered worktree whose directory is gone is a dead end for every future dispatch of
    /// that task id (CARD-0220). Heal it before the path/branch conflict checks so create can
    /// proceed: delete any leftover directory, <c>remove --force --force</c>, then prune.
    /// The branch is re-attached by the caller, never deleted here.
    /// </summary>
    private async Task<bool> TryHealStaleRegistrationAsync(
        string repoPath, string worktreePath, CancellationToken ct)
    {
        var list = await RunGitAsync(repoPath, ["worktree", "list", "--porcelain"], ct, throwOnError: false);
        if (list.ExitCode != 0)
            return false;

        var stale = ParseWorktreeList(list.Stdout)
            .FirstOrDefault(entry => PathsEqual(entry.Path, worktreePath));
        if (stale is null || Directory.Exists(worktreePath))
            return false;

        var commands = new List<string>();
        try
        {
            TryDeleteDirectory(worktreePath);
            commands.Add("delete directory");

            var remove = await RunGitAsync(
                repoPath,
                ["worktree", "remove", "--force", "--force", worktreePath],
                CancellationToken.None,
                throwOnError: false);
            commands.Add(DescribeGitStep("worktree remove --force --force", remove));
            if (remove.ExitCode != 0)
                throw new InvalidOperationException(remove.Stderr.Trim());

            var prune = await RunGitAsync(
                repoPath,
                ["worktree", "prune"],
                CancellationToken.None,
                throwOnError: false);
            commands.Add(DescribeGitStep("worktree prune", prune));
            if (prune.ExitCode != 0)
                throw new InvalidOperationException(prune.Stderr.Trim());

            var lockReason = stale.Locked
                ? (string.IsNullOrEmpty(stale.LockReason) ? "(no reason)" : stale.LockReason)
                : "unlocked";
            var killedAdd = string.Equals(stale.LockReason, "initializing", StringComparison.Ordinal)
                ? " — a killed add"
                : string.Empty;
            _logger.LogWarning(
                "Healed stale worktree registration at {Path} (lock: {LockReason}{KilledAdd}); ran: {Commands}",
                worktreePath,
                lockReason,
                killedAdd,
                string.Join(" → ", commands));
            return true;
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            var diagnosis = $"Worktree '{worktreePath}' is registered"
                + (stale.Locked
                    ? $" and locked ({(string.IsNullOrEmpty(stale.LockReason) ? "no reason" : stale.LockReason)})"
                    : "")
                + " but its directory is gone";
            throw new ConflictException(
                $"{diagnosis}. Heal attempted: {string.Join("; ", commands)}. {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Full rollback of a failed <c>worktree add</c>, on a fresh token so a cancelled caller cannot
    /// abort cleanup halfway. Each step is logged; a rollback failure is Warning and the original
    /// exception still propagates.
    /// </summary>
    private async Task RollbackFailedAddAsync(
        string repoPath,
        string worktreePath,
        string branch,
        bool branchExistedBefore)
    {
        var ct = CancellationToken.None;
        _logger.LogInformation(
            "Rolling back failed worktree add at {Path} (branch {Branch}, existedBefore={ExistedBefore})",
            worktreePath,
            branch,
            branchExistedBefore);

        TryDeleteDirectory(worktreePath);

        try
        {
            if (await IsRegisteredAsync(repoPath, worktreePath, ct))
            {
                var remove = await RunGitAsync(
                    repoPath,
                    ["worktree", "remove", "--force", "--force", worktreePath],
                    ct,
                    throwOnError: false);
                if (remove.ExitCode != 0)
                {
                    _logger.LogWarning(
                        "Rollback worktree remove --force --force failed for {Path}: {StdErr}",
                        worktreePath,
                        remove.Stderr);
                }
                else
                {
                    _logger.LogInformation("Rollback removed worktree registration {Path}", worktreePath);
                }
            }

            var prune = await RunGitAsync(repoPath, ["worktree", "prune"], ct, throwOnError: false);
            if (prune.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Rollback worktree prune failed in {RepoPath}: {StdErr}",
                    repoPath,
                    prune.Stderr);
            }
            else
            {
                _logger.LogInformation("Rollback pruned worktree registrations in {RepoPath}", repoPath);
            }

            if (!branchExistedBefore && await BranchExistsAsync(repoPath, branch, ct))
            {
                var deleteBranch = await RunGitAsync(repoPath, ["branch", "-D", branch], ct, throwOnError: false);
                if (deleteBranch.ExitCode != 0)
                {
                    _logger.LogWarning(
                        "Rollback branch -D {Branch} failed: {StdErr}",
                        branch,
                        deleteBranch.Stderr);
                }
                else
                {
                    _logger.LogInformation("Rollback deleted branch {Branch}", branch);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Rollback of failed worktree add at {Path} failed; the original exception still propagates",
                worktreePath);
        }
    }

    private async Task<bool> IsRegisteredAsync(string repoPath, string worktreePath, CancellationToken ct)
    {
        var result = await RunGitAsync(repoPath, ["worktree", "list", "--porcelain"], ct, throwOnError: false);
        if (result.ExitCode != 0)
            return false;
        return ParseWorktreeList(result.Stdout).Any(entry => PathsEqual(entry.Path, worktreePath));
    }

    private string? TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return null;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch (Exception) { /* best-effort; Delete will report if it still cannot */ }
            }

            Directory.Delete(path, recursive: true);
            _logger.LogInformation("Deleted leftover worktree directory {Path}", path);
            return Directory.Exists(path) ? $"directory {path} still exists after delete" : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete worktree directory {Path}", path);
            return ex.Message;
        }
    }

    private static string DescribeGitStep(string command, GitCommandResult result)
    {
        var stderr = result.Stderr.Trim();
        return string.IsNullOrEmpty(stderr)
            ? $"{command} (exit {result.ExitCode})"
            : $"{command} (exit {result.ExitCode}): {stderr}";
    }

    private async Task<GitCommandResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken ct,
        bool throwOnError = true)
    {
        var budget = TimeoutFor(arguments);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        _logger.LogDebug(
            "Running git {Arguments} in {WorkingDirectory} (timeout {TimeoutSeconds}s)",
            string.Join(" ", arguments),
            workingDirectory,
            budget.TotalSeconds);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
            throw new TimeoutException(
                $"git {string.Join(" ", arguments)} timed out after {budget.TotalSeconds:0}s in {workingDirectory}");
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var result = new GitCommandResult(process.ExitCode, stdout, stderr);

        if (throwOnError && result.ExitCode != 0)
        {
            _logger.LogError(
                "git {Arguments} failed (exit {ExitCode}): {StdErr}",
                string.Join(" ", arguments),
                result.ExitCode,
                result.Stderr);
            throw new InvalidOperationException(
                $"git {string.Join(" ", arguments)} failed with exit code {result.ExitCode}: {result.Stderr}");
        }

        return result;
    }

    internal sealed record WorktreePorcelainEntry(
        string Path,
        string Branch,
        bool Locked = false,
        string? LockReason = null,
        bool Prunable = false,
        string? PrunableReason = null);

    private sealed record WorktreeMetadata(
        int SchemaVersion,
        string CardId,
        string RepoPath,
        string Path,
        string Branch,
        string BaseRef,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastTouchedAt,
        DateTimeOffset? ResidueSince = null);

    private sealed record WorktreeMetadataRecord(string FilePath, WorktreeMetadata Metadata);

    private sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr);
}
