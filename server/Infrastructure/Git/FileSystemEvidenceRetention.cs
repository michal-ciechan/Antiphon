using System.Collections.Immutable;
using System.Security.Cryptography;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-1446 D-4 (S4a): copies each evidence-class ignored file to
/// <c>&lt;WorktreeCleanup:RetainedEvidenceRoot&gt;/&lt;taskId:N&gt;/&lt;attemptId:N&gt;/&lt;relative&gt;</c> before guarded
/// removal deletes the tree, hashing the source while copying and the destination after. It is a copy,
/// never a move: the gate's second reading requires the evidence set unchanged. Refusals (a non-null
/// <see cref="WorktreeEvidenceRetentionResult.Reason"/>) keep the tree: <c>evidence_retention_budget_files</c>
/// and <c>evidence_retention_budget_bytes</c> before any copy, <c>evidence_retention_conflict</c> for an existing
/// destination with different bytes, <c>evidence_retention_verify_failed</c> for a copy whose bytes differ,
/// <c>evidence_retention_source_missing</c> for a listed path that is gone. Retained copies are never deleted by code.
/// </summary>
public sealed class FileSystemEvidenceRetention : IWorktreeEvidenceRetention
{
    private readonly WorktreeCleanupSettings _cleanup;
    private readonly GitSettings _git;
    private readonly ILogger<FileSystemEvidenceRetention> _logger;

    public FileSystemEvidenceRetention(IOptions<WorktreeCleanupSettings> cleanup, IOptions<GitSettings> git,
        ILogger<FileSystemEvidenceRetention> logger)
    {
        _cleanup = cleanup.Value;
        _git = git.Value;
        _logger = logger;
    }

    public string Root => string.IsNullOrWhiteSpace(_cleanup.RetainedEvidenceRoot)
        ? Path.Combine(_git.WorktreeBasePath, ".retained") : _cleanup.RetainedEvidenceRoot;

    public async Task<WorktreeEvidenceRetentionResult> RetainAsync(AgentTask task, string worktreePath,
        ImmutableArray<string> relativePaths, Guid? attemptId, CancellationToken ct)
    {
        if (relativePaths.IsDefaultOrEmpty) return WorktreeEvidenceRetentionResult.Nothing;
        if (relativePaths.Length > _cleanup.MaxRetainedEvidenceFiles) return Refuse("evidence_retention_budget_files");

        // Budget first, so a refusal copies nothing; the classifier already rejected rooted or escaping paths.
        var sources = new List<(string Relative, FileInfo Info)>(relativePaths.Length);
        long total = 0;
        foreach (var relative in relativePaths)
        {
            var normalized = WorktreeIgnoredContentClassifier.Normalize(relative);
            if (WorktreeIgnoredContentClassifier.IsRootedOrEscaping(normalized)) return Refuse("evidence_retention_path_escapes");
            var info = new FileInfo(Path.Combine(worktreePath, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!info.Exists) return Refuse("evidence_retention_source_missing");
            total += info.Length;
            if (total > _cleanup.MaxRetainedEvidenceBytes) return Refuse("evidence_retention_budget_bytes");
            sources.Add((normalized, info));
        }

        var destinationRoot = Path.Combine(Root, task.Id.ToString("N"), (attemptId ?? Guid.Empty).ToString("N"));
        var files = ImmutableArray.CreateBuilder<RetainedWorktreeFile>(sources.Count);
        foreach (var (relative, info) in sources)
        {
            ct.ThrowIfCancellationRequested();
            var destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string sourceHash;
            if (File.Exists(destination))
            {
                // An earlier pass copied this path: identical bytes are accepted, anything else is a conflict.
                sourceHash = await HashAsync(info.FullName, ct);
                if (!string.Equals(await HashAsync(destination, ct), sourceHash, StringComparison.Ordinal))
                    return Refuse("evidence_retention_conflict");
            }
            else
            {
                sourceHash = await CopyHashingAsync(info.FullName, destination, ct);
                if (!string.Equals(await HashAsync(destination, ct), sourceHash, StringComparison.Ordinal))
                {
                    TryDelete(destination);
                    return Refuse("evidence_retention_verify_failed");
                }
            }
            files.Add(new RetainedWorktreeFile(relative, destination, info.Length, sourceHash));
        }

        _logger.LogInformation("Worktree evidence retained task={TaskId} attempt={AttemptId} files={Files} bytes={Bytes} root={Root}",
            task.Id, attemptId, files.Count, total, destinationRoot);
        return new WorktreeEvidenceRetentionResult(destinationRoot, files.ToImmutable(), null);
    }

    private static WorktreeEvidenceRetentionResult Refuse(string reason) => new(null, [], reason);

    private static async Task<string> CopyHashingAsync(string source, string destination, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        await output.FlushAsync(ct);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
