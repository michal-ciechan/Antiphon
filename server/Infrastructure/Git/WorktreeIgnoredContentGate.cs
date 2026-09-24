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

    /// <summary>
    /// Git lists through a directory junction, so a removable path that is, or sits under, a
    /// symlink, junction or other reparse point names bytes outside the tree: evidence would be
    /// copied from there, and the listing would stand in for content the tree does not own. The
    /// deletion itself never follows a link (<see cref="WorktreeNoFollowDelete"/>). Returns the worktree-relative link paths, checking ancestors first and never
    /// reading through a link it has found. A path gone since the listing is disposable churn.
    /// </summary>
    public static ImmutableArray<string> ReparsePoints(string worktreePath, WorktreeIgnoredContent content)
    {
        var known = new Dictionary<string, bool>(StringComparer.Ordinal);
        var links = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var relative in content.Evidence.Concat(content.Disposable))
        {
            var prefix = "";
            foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                prefix = prefix.Length == 0 ? segment : prefix + "/" + segment;
                if (!known.TryGetValue(prefix, out var linked))
                    known[prefix] = linked = IsReparsePoint(Path.Combine(worktreePath, prefix));
                if (linked) { links.Add(prefix); break; }
            }
        }
        return [.. links];
    }

    /// <summary>D-8: at most five paths, the rest counted, clipped to 400 characters.</summary>
    public static string ProtectedDetail(ImmutableArray<string> paths) => PathDetail("protected", paths);

    public static string ReparseDetail(ImmutableArray<string> paths) => PathDetail("reparse", paths);

    private static string PathDetail(string label, ImmutableArray<string> paths)
    {
        const int shown = 5;
        var text = label + ": " + string.Join(", ", paths.Take(shown))
            + (paths.Length > shown ? $" (+{paths.Length - shown})" : "");
        return WorktreeCleanupPresentation.Clip(text, 400);
    }

    private static bool IsReparsePoint(string path)
    {
        // Attributes of the entry itself: a link is reported, not followed.
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public static string? RetainedDetail(WorktreeEvidenceRetentionResult retained) =>
        retained.Files.IsDefaultOrEmpty ? null
            : WorktreeCleanupPresentation.Clip($"retained={retained.Files.Length} files at {retained.Root}", 400);
}
