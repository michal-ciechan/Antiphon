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

        if (Directory.Exists(worktreePath))
            throw new ConflictException($"Worktree path already exists: {worktreePath}");

        if (await BranchExistsAsync(repoFullPath, branch, ct))
            throw new ConflictException($"Worktree branch already exists: {branch}");

        await EnsureRefExistsAsync(repoFullPath, baseRef, ct);

        try
        {
            await RunGitAsync(repoFullPath, ["worktree", "add", "-b", branch, worktreePath, baseRef], ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
            if (Directory.Exists(worktreePath))
            {
                try
                {
                    Directory.Delete(worktreePath, recursive: true);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to clean up partially-created worktree {Path}", worktreePath);
                }
            }

            throw;
        }

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

    public async Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct)
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

        if (!IsAntiphonBranch(branch))
            throw new ValidationException(nameof(worktreePath), "Worktree is not an Antiphon-managed feat/card-* worktree.");

        if (Directory.Exists(worktreeFullPath))
            await RunGitAsync(repoFullPath, ["worktree", "remove", "--force", worktreeFullPath], ct);

        var deleteBranch = await RunGitAsync(repoFullPath, ["branch", "-D", branch!], ct, throwOnError: false);
        if (deleteBranch.ExitCode != 0)
        {
            _logger.LogWarning(
                "Failed to delete worktree branch {Branch} in {RepoPath}: {StdErr}",
                branch,
                repoFullPath,
                deleteBranch.Stderr);
        }

        await DeleteMetadataForPathAsync(worktreeRoot, worktreeFullPath, ct);
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
            if (metadata.LastTouchedAt > cutoff)
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
                if (Directory.Exists(worktreePath))
                {
                    await RemoveAsync(metadata.RepoPath, worktreePath, ct);
                }
                else
                {
                    await RunGitAsync(metadata.RepoPath, ["worktree", "prune"], ct, throwOnError: false);
                    await DeleteMetadataFileAsync(record.FilePath, ct);
                }

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

        foreach (var rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.None))
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
        }

        AddEntry();
        return entries;

        void AddEntry()
        {
            if (!string.IsNullOrWhiteSpace(path))
                entries.Add(new WorktreePorcelainEntry(path, branch ?? string.Empty));

            path = null;
            branch = null;
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

    private static WorktreeMetadata ToMetadata(WorktreeInfo info) => new(
        SchemaVersion: 1,
        CardId: info.CardId,
        RepoPath: info.RepoPath,
        Path: info.Path,
        Branch: info.Branch,
        BaseRef: info.BaseRef,
        CreatedAt: info.CreatedAt,
        LastTouchedAt: info.LastTouchedAt);

    private static WorktreeInfo ToInfo(WorktreeMetadata metadata) => new(
        metadata.CardId,
        metadata.RepoPath,
        metadata.Path,
        metadata.Branch,
        metadata.BaseRef,
        metadata.CreatedAt,
        metadata.LastTouchedAt);

    private async Task<GitCommandResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken ct,
        bool throwOnError = true)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(GitTimeout);

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

        _logger.LogDebug("Running git {Arguments} in {WorkingDirectory}", string.Join(" ", arguments), workingDirectory);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
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

    internal sealed record WorktreePorcelainEntry(string Path, string Branch);

    private sealed record WorktreeMetadata(
        int SchemaVersion,
        string CardId,
        string RepoPath,
        string Path,
        string Branch,
        string BaseRef,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastTouchedAt);

    private sealed record WorktreeMetadataRecord(string FilePath, WorktreeMetadata Metadata);

    private sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr);
}
