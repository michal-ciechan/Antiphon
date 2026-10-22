using System.Collections.Immutable;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-1446 D-4 (S4a): copies each evidence-class ignored file to the retained evidence root before
/// guarded removal deletes the tree. S4a-test skeleton: still refuses like <see cref="RefusingEvidenceRetention"/>.
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

    public Task<WorktreeEvidenceRetentionResult> RetainAsync(AgentTask task, string worktreePath,
        ImmutableArray<string> relativePaths, Guid? attemptId, CancellationToken ct) =>
        Task.FromResult(relativePaths.IsDefaultOrEmpty ? WorktreeEvidenceRetentionResult.Nothing
            : new WorktreeEvidenceRetentionResult(null, [], "evidence_retention_unavailable"));
}
