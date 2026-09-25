using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>The managed worktree root, resolved one way for WorktreeManager and the land worktree (CARD-0688 D-3).</summary>
public static class WorktreeRoots
{
    public static string Resolve(GitSettings settings, bool create = false)
    {
        if (string.IsNullOrWhiteSpace(settings.WorktreeBasePath))
            throw new ValidationException("Git:WorktreeBasePath", "Worktree base path must be configured.");

        var root = Path.IsPathRooted(settings.WorktreeBasePath)
            ? settings.WorktreeBasePath
            : Path.Combine(AppContext.BaseDirectory, settings.WorktreeBasePath);
        root = Path.GetFullPath(root);

        if (create)
            Directory.CreateDirectory(root);

        return root;
    }
}
