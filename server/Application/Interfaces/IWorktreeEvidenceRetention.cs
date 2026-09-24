using System.Collections.Immutable;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0665 D-3/D-4: copies a source worktree's evidence-class ignored files to the retained
/// report root, byte-verified, before guarded removal deletes the tree, and refuses when a task
/// artifact pointer inside the tree would not survive. A non-null <see cref="WorktreeEvidenceRetentionResult.Reason"/>
/// is a refusal and the tree is kept.
/// </summary>
public interface IWorktreeEvidenceRetention
{
    Task<WorktreeEvidenceRetentionResult> RetainAsync(AgentTask task, string worktreePath,
        ImmutableArray<string> relativePaths, Guid? attemptId, CancellationToken ct);
}

public sealed record WorktreeEvidenceRetentionResult(string? Root, ImmutableArray<RetainedWorktreeFile> Files, string? Reason)
{
    public static WorktreeEvidenceRetentionResult Nothing { get; } = new(null, [], null);
}

public sealed record RetainedWorktreeFile(string Relative, string RetainedPath, long Bytes, string Sha256);

/// <summary>
/// CARD-0665 S2 seam until the copying implementation is registered: a tree with no evidence
/// proceeds, a tree with evidence is refused rather than deleted uncopied.
/// </summary>
public sealed class RefusingEvidenceRetention : IWorktreeEvidenceRetention
{
    public Task<WorktreeEvidenceRetentionResult> RetainAsync(AgentTask task, string worktreePath,
        ImmutableArray<string> relativePaths, Guid? attemptId, CancellationToken ct) =>
        Task.FromResult(relativePaths.IsDefaultOrEmpty ? WorktreeEvidenceRetentionResult.Nothing
            : new WorktreeEvidenceRetentionResult(null, [], "evidence_retention_unavailable"));
}
