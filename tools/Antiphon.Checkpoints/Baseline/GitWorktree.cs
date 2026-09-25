namespace Antiphon.Checkpoints;

/// <summary>Detached worktree commands issued by <see cref="BaselineComparer"/>.</summary>
public static class GitWorktree
{
    public static DriverRequest Add(string worktree, string path, string sha) =>
        new("git", ["worktree", "add", "--detach", path, sha], worktree);

    public static DriverRequest Remove(string worktree, string path) =>
        new("git", ["worktree", "remove", "--force", path], worktree);
}
