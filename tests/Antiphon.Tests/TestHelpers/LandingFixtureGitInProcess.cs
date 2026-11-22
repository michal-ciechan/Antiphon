namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The land fixtures' most common setup commands done with libgit2 in the test process instead of a git child:
/// `rev-parse HEAD`, `add .` and `commit [--allow-empty] -m &lt;message&gt;` at a checkout's root, run through
/// <see cref="LandingGitFixture.RequiredAsync"/> only (fixture setup; every command the land code under test runs
/// still starts git through the same <see cref="LandingGitFixture.FixtureGit"/>). Each is recorded in the fixture
/// git's Trace and Commands (and the optional C448 evidence) as if it had run, so a test that reads them sees what it
/// saw before. libgit2 here reads no user or system config (the assembly hooks apply LibGit2ConfigIsolation): the
/// view the fixture's git children have (GIT_CONFIG_NOSYSTEM, an empty GIT_CONFIG_GLOBAL, a HOME of their own), with
/// the same identity for commits and reflogs and, like `commit -m`, the message tidied (LibGit2Sharp's
/// PrettifyMessage). On Windows every git process costs about 0.6 s on a laptop; these three
/// are over half of a typical land test's fixture git processes.
/// </summary>
internal static class LandingFixtureGitInProcess
{
    // Null (git runs it, as before) when a test watches or rewrites commands (a FixtureGit hook, a hooks path, a
    // subclass), for any other shape, for anything git would refuse or treat specially (an unborn HEAD, a path that is
    // not a checkout root, a bare repository, a nested repository or link where `add .` would reach, a user ignore or
    // attributes file under XDG_CONFIG_HOME, nothing to commit, an operation or sequencer state in progress, an empty
    // message), and for any failure before the command took effect.
    public static string? TryRun(LandingGitFixture.FixtureGit git, string checkout, string[] args)
    {
        if (git.Observed) return null;
        var applied = false;
        string? headBefore = null;
        try
        {
            switch (args)
            {
                case ["rev-parse", "HEAD"]:
                    using (var repo = Open(checkout))
                    {
                        if (repo?.Head.Tip is not { } tip) return null;
                        git.RecordInProcess(checkout, args);
                        return tip.Sha + "\n";
                    }
                case ["add", "."]:
                    using (var repo = Open(checkout))
                    {
                        if (repo is null || repo.Info.IsBare || UserFilesApply() || Reaches(repo, checkout)) return null;
                        LibGit2Sharp.Commands.Stage(repo, "*");
                        applied = true;
                    }
                    git.RecordInProcess(checkout, args);
                    return "";
                case ["commit", "-m", _] or ["commit", "--allow-empty", "-m", _] when !string.IsNullOrWhiteSpace(args[^1]):
                    using (var repo = Open(checkout))
                    {
                        var message = args[^1];
                        if (repo is null || repo.Info.IsBare || repo.Info.CurrentOperation != LibGit2Sharp.CurrentOperation.None
                            || InProgress(repo))
                            return null;
                        var allowEmpty = args[1] == "--allow-empty";
                        // git refuses an empty first commit without --allow-empty ("nothing to commit"): git answers.
                        if (repo.Info.IsHeadUnborn && repo.Index.Count == 0 && !allowEmpty) return null;
                        var who = new LibGit2Sharp.Signature(LandingGitFixture.FixtureGit.Name,
                            LandingGitFixture.FixtureGit.Email, DateTimeOffset.Now);
                        headBefore = repo.Head.Tip?.Sha ?? "";
                        // Without --allow-empty an unchanged tree throws (EmptyCommitException): git answers that too.
                        repo.Commit(message, who, who, new LibGit2Sharp.CommitOptions { AllowEmptyCommit = allowEmpty });
                        applied = true;
                    }
                    git.RecordInProcess(checkout, args);
                    return "";
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            // The command took effect (a failure after it, such as on dispose, or a commit that moved HEAD before
            // failing): done, git must not repeat it.
            if (!applied && !(headBefore is not null && HeadMoved(checkout, headBefore))) return null;
            git.RecordInProcess(checkout, args);
            return "";
        }
    }

    // The repository whose working directory (or, bare, whose directory) is exactly `checkout`: libgit2 opens no
    // subdirectory, and git's answer from one (paths relative to it) differs.
    // The fixture's identity for the reflog entries a commit writes (as the git children's GIT_COMMITTER_* do).
    private static LibGit2Sharp.Repository? Open(string checkout)
    {
        if (!Directory.Exists(checkout)) return null;
        LibGit2Sharp.Repository? repo = null;
        try
        {
            repo = new LibGit2Sharp.Repository(checkout, new LibGit2Sharp.RepositoryOptions
            {
                Identity = new LibGit2Sharp.Identity(LandingGitFixture.FixtureGit.Name, LandingGitFixture.FixtureGit.Email),
            });
            var root = repo.Info.IsBare ? repo.Info.Path : repo.Info.WorkingDirectory;
            if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(checkout)), NameComparison))
                return repo;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
        }
        repo?.Dispose();
        return null;
    }

    // State a commit must keep that libgit2's commit clears (git_repository_state_cleanup) or git would act on: a
    // sequencer, a merge, cherry-pick, revert, rebase or bisect in progress, in this checkout's own git directory.
    private static bool InProgress(LibGit2Sharp.Repository repo) =>
        new[] { "sequencer", "MERGE_HEAD", "MERGE_MSG", "MERGE_MODE", "SQUASH_MSG", "CHERRY_PICK_HEAD", "REVERT_HEAD",
                "BISECT_LOG", "BISECT_START", "rebase-merge", "rebase-apply", "AUTO_MERGE" }
            .Any(name => File.Exists(Path.Combine(repo.Info.Path, name)) || Directory.Exists(Path.Combine(repo.Info.Path, name)));

    // A user ignore or attributes file git children would read ($XDG_CONFIG_HOME/git/...; their HOME is the
    // fixture's own, with none) and libgit2 here does not: git answers `add .` then.
    private static bool UserFilesApply()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return !string.IsNullOrEmpty(xdg)
            && (File.Exists(Path.Combine(xdg, "git", "ignore")) || File.Exists(Path.Combine(xdg, "git", "attributes")));
    }

    private static bool HeadMoved(string checkout, string before)
    {
        using var repo = Open(checkout);
        return repo?.Head.Tip?.Sha is { } now && now != before;
    }

    // The default file systems of Windows and macOS ignore case in names; Linux's do not.
    private static StringComparison NameComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // A `.git` (directory or file) other than the checkout's own, or any link (a symlink, a junction), in a directory
    // `add .` would reach (one the repository does not ignore): git adds a nested repository as a gitlink, and a link
    // as core.symlinks says, so git answers. The ignore rules are the repository's own and its info/exclude (no user
    // excludes file, as the fixture's git children have none).
    private static bool Reaches(LibGit2Sharp.Repository repo, string checkout)
    {
        var own = Path.GetFullPath(Path.Combine(checkout, ".git"));
        var pending = new Stack<string>();
        pending.Push(checkout);
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (string.Equals(Path.GetFileName(entry), ".git", NameComparison))
                {
                    if (!string.Equals(Path.GetFullPath(entry), own, NameComparison)) return true;
                    continue;
                }
                var relative = Path.GetRelativePath(checkout, entry).Replace('\\', '/');
                var isDirectory = Directory.Exists(entry);
                if (repo.Ignore.IsPathIgnored(isDirectory ? relative + "/" : relative)) continue;
                if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint)) return true;
                if (isDirectory) pending.Push(entry);
            }
        }
        return false;
    }
}
