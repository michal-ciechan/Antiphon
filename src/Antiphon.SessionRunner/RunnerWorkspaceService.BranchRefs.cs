using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0824 S2 (V-40). Before the server deletes a landed delegate branch on origin, the runner deletes its own
/// <c>refs/heads/&lt;b&gt;</c> and <c>refs/remotes/origin/&lt;b&gt;</c> in the primary repository, so a later CARD-1008 recycle
/// audit finds no ref whose tip origin no longer advertises. Every check runs before the first delete:
/// a delegate branch name only, the expected commit present (unknown is never absent), the branch checked out in no
/// worktree, and each ref at or behind the expected SHA. Each delete carries its old value.
/// </summary>
public sealed partial class RunnerWorkspaceService
{
    /// <summary>CARD-0824 S2 test seam: runs after every check and before each <c>update-ref -d</c>, with the ref name.</summary>
    internal Func<string, Task>? BeforeBranchRefDelete { get; set; }

    public async Task<PhoneHomeWorkspaceBranchRefsDeleteResponse> DeleteBranchRefsAsync(
        PhoneHomeWorkspaceBranchRefsDeleteRequest request, CancellationToken ct)
    {
        if (request.Version != 1 || !ShaPattern.IsMatch(request.ExpectedSha ?? ""))
            return Refused(WorkspaceBranchRefsCodes.InvalidRequest, [], []);
        if (request.Branch is null || request.Branch.StartsWith("refs/", StringComparison.Ordinal)
            || !DelegateBranchName.TryParse(request.Branch, out var parsed))
            return Refused(WorkspaceBranchRefsCodes.ProtectedBranch, [], []);
        string[] names = ["refs/heads/" + parsed.Branch, "refs/remotes/origin/" + parsed.Branch];
        if (!Directory.Exists(_repository))
            return new(WorkspaceBranchRefsCodes.Deleted, null, [], names, "repository absent");
        var gitDir = await GitAsync(_repository, ct, "rev-parse", "--git-dir");
        if (gitDir.ExitCode != 0)
            return Refused(WorkspaceBranchRefsCodes.RepositoryUnavailable, [], []);

        var commit = await GitAsync(_repository, ct, "cat-file", "-e", request.ExpectedSha + "^{commit}");
        if (commit.ExitCode != 0)
            return Refused(WorkspaceBranchRefsCodes.ExpectedObjectMissing, [], []);

        var worktrees = await GitAsync(_repository, ct, "worktree", "list", "--porcelain");
        if (worktrees.ExitCode != 0)
            return Refused(WorkspaceBranchRefsCodes.RefUnreadable, [], [], "worktree list failed");
        if (worktrees.Stdout.Split('\n').Any(line => line.TrimEnd('\r') == "branch " + names[0]))
            return Refused(WorkspaceBranchRefsCodes.BranchCheckedOut, [], []);

        var present = new List<(string Name, string Sha)>();
        var absent = new List<string>();
        foreach (var name in names)
        {
            var listed = await GitAsync(_repository, ct, "for-each-ref", "--format=%(refname) %(objectname)", name);
            if (listed.ExitCode != 0)
                return Refused(WorkspaceBranchRefsCodes.RefUnreadable, [], []);
            var match = listed.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r').Split(' '))
                .FirstOrDefault(fields => fields.Length == 2 && fields[0] == name);
            if (match is null) { absent.Add(name); continue; }
            if (!ShaPattern.IsMatch(match[1]))
                return Refused(WorkspaceBranchRefsCodes.RefUnreadable, [], []);
            if (match[1] != request.ExpectedSha)
            {
                var ancestry = await GitAsync(_repository, ct, "merge-base", "--is-ancestor", match[1], request.ExpectedSha!);
                if (ancestry.ExitCode == 1)
                    return Refused(WorkspaceBranchRefsCodes.UnpublishedLocalRef, [], [], name);
                if (ancestry.ExitCode != 0)
                    return Refused(WorkspaceBranchRefsCodes.RefUnreadable, [], [], name);
            }
            present.Add((name, match[1]));
        }

        var deleted = new List<string>();
        foreach (var (name, sha) in present)
        {
            if (BeforeBranchRefDelete is { } seam) await seam(name);
            var delete = await GitAsync(_repository, ct, "update-ref", "-d", name, sha);
            if (delete.ExitCode != 0)
                return Refused(WorkspaceBranchRefsCodes.RefMoved, deleted, absent, name);
            deleted.Add(name);
        }
        return new(WorkspaceBranchRefsCodes.Deleted, null, deleted, absent);
    }

    private static PhoneHomeWorkspaceBranchRefsDeleteResponse Refused(string code, IReadOnlyList<string> deleted,
        IReadOnlyList<string> absent, string? detail = null) =>
        new(WorkspaceBranchRefsCodes.Refused, code, deleted, absent, detail);
}
