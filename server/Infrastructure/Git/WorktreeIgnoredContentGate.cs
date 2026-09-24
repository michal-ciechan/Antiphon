using System.Collections.Immutable;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-0665 D-5: the ignored-content rule both readings of guarded removal apply. Classification
/// is pure; retention reads the task row in its own scope so its artifact pointers are current.
/// </summary>
public sealed class WorktreeIgnoredContentGate(WorktreeIgnoredContentClassifier classifier,
    IWorktreeEvidenceRetention retention, IWorktreeRemovalEvidence evidence)
{
    public WorktreeIgnoredContent Classify(LandSourceSnapshot snapshot) => classifier.Classify(snapshot.IgnoredPaths);

    public async Task<WorktreeEvidenceRetentionResult> RetainAsync(WorktreeRemovalRequest request,
        ImmutableArray<string> evidencePaths, CancellationToken ct)
    {
        var task = await evidence.ReadTaskAsync(request.Source.TaskId, ct);
        if (task is null || task.Id != request.Source.TaskId)
            return new(null, [], "artifact_pointers_unavailable");
        return await retention.RetainAsync(task, request.Source.WorktreePath, evidencePaths,
            request.CleanupContext?.AttemptId, ct);
    }

    /// <summary>D-8: at most five paths, the rest counted, clipped to 400 characters.</summary>
    public static string ProtectedDetail(ImmutableArray<string> paths)
    {
        const int shown = 5;
        var text = "protected: " + string.Join(", ", paths.Take(shown))
            + (paths.Length > shown ? $" (+{paths.Length - shown})" : "");
        return WorktreeCleanupPresentation.Clip(text, 400);
    }

    public static string? RetainedDetail(WorktreeEvidenceRetentionResult retained) =>
        retained.Files.IsDefaultOrEmpty ? null
            : WorktreeCleanupPresentation.Clip($"retained={retained.Files.Length} files at {retained.Root}", 400);
}
