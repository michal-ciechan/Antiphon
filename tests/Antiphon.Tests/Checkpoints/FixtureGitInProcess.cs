namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// Fixture repository setup done with libgit2 in the test process instead of a git child, for the setup commands
/// the checkpoint fixtures run most. Used by <see cref="CheckpointFixtureGitEnvironment"/> (whose git children read
/// no user or system config) and by OutputCleanupTests and CheckpointConfinementLedgerTests (whose git children read
/// the machine's config). libgit2 here reads no user or system config and no global attributes file (the assembly
/// hook LibGit2ConfigIsolationAssemblyHook applies LibGit2ConfigIsolation before any test): that is the view the
/// checkpoint code under test hardens its own git to (GIT_CONFIG_NOSYSTEM, an empty GIT_CONFIG_GLOBAL,
/// core.attributesFile on the null device: CheckpointProcessRunner.HardenGit) and the view the Linux runs have, so a
/// repository set up in process stores its bytes as that code expects. A git child that reads a machine's line-ending
/// config can store them differently (legacy_hostile_config_produces_hardened_status_differential).
/// </summary>
internal static class FixtureGitInProcess
{
    // The fixture setup commands the checkpoint classes run most, done with libgit2 in this process (no git child):
    // `init [-q] -b <branch>`, `add -A`, `add [-f] -- <paths>`, the fixture commit (`-c user.email=.. -c user.name=..
    // -c commit.gpgsign=false commit -m <message> --no-verify`) and `rev-parse HEAD`. Fixture setup is not what
    // these tests prove, and the tool and script under test still read the repository their own way. Null for any
    // other shape, any shape git would refuse or treat specially (an ignored path named without -f, a nested
    // repository), or any failure before the command took effect: git runs it, as before, from where it started.
    // Output is git's only for rev-parse; the other commands' callers do not read theirs.
    public static string? TryRun(string repository, string[] args)
    {
        var dotGit = Path.Combine(repository, ".git");
        var created = false;
        var applied = false;
        try
        {
            switch (args)
            {
                // A fresh directory only (not a re-init, not a .git file), and a branch name git would accept.
                case ["init", ..] when InitBranch(args) is { } branch && !Directory.Exists(dotGit) && !File.Exists(dotGit):
                    created = true;
                    LibGit2Sharp.Repository.Init(repository);
                    File.WriteAllText(Path.Combine(dotGit, "HEAD"), "ref: refs/heads/" + branch + "\n");
                    return "";
                // git adds a nested repository as a gitlink: git answers that.
                case ["add", "-A"]:
                    using (var repo = new LibGit2Sharp.Repository(repository))
                    {
                        if (HasNestedRepository(repo, repository, repository, enterIgnored: false)) return null;
                        LibGit2Sharp.Commands.Stage(repo, "*");
                        applied = true;
                    }
                    return "";
                // Paths that exist below the repository root, named literally (git refuses a path that matches nothing:
                // git answers that). A directory stages what is under it; with -f ignored files included, without -f
                // they are left out, as git does. The root itself, a path outside it, a pattern character, (off
                // Windows) a backslash in a name, which libgit2's pathspec would read differently, a path in or above
                // a nested repository, and without -f an ignored path (git refuses it), go to git.
                case ["add", "-f", "--", ..] or ["add", "--", ..] when args.Length > (args[1] == "-f" ? 3 : 2):
                    var force = args[1] == "-f";
                    var paths = new List<string>();
                    using (var repo = new LibGit2Sharp.Repository(repository))
                    {
                        foreach (var raw in args.Skip(force ? 3 : 2))
                        {
                            // Named literally: a path that normalising changes (a "." or ".." segment, a doubled
                            // separator, on Windows a trailing dot or space) goes to git, whose answer for those forms
                            // differs by platform. So does a trailing separator on anything but a directory.
                            var literal = Path.IsPathRooted(raw) ? raw : Path.Combine(repository, raw);
                            if (OperatingSystem.IsWindows()) literal = literal.Replace('/', '\\');
                            var named = Path.GetFullPath(literal);
                            if (!string.Equals(named, literal, StringComparison.Ordinal))
                                return null;
                            var full = Path.TrimEndingDirectorySeparator(named);
                            var isDirectory = Directory.Exists(full);
                            if (full.Length != named.Length && !isDirectory)
                                return null;
                            var relative = Path.GetRelativePath(repository, full);
                            if ((!File.Exists(full) && !isDirectory) || relative == "." || relative.StartsWith("..", StringComparison.Ordinal)
                                || Path.IsPathRooted(relative) || relative.IndexOfAny(['*', '?', '[', ']']) >= 0
                                || (!OperatingSystem.IsWindows() && relative.Contains('\\'))
                                || InNestedRepository(repository, full) || (isDirectory && HasNestedRepository(repo, repository, full, enterIgnored: force)))
                                return null;
                            relative = relative.Replace('\\', '/');
                            if (!force && repo.Ignore.IsPathIgnored(isDirectory ? relative + "/" : relative))
                                return null;
                            paths.Add(relative);
                        }
                        LibGit2Sharp.Commands.Stage(repo, paths, new LibGit2Sharp.StageOptions { IncludeIgnored = force });
                        applied = true;
                    }
                    return "";
                case ["-c", var email, "-c", var name, "-c", "commit.gpgsign=false", "commit", ..] when
                    email.StartsWith("user.email=", StringComparison.Ordinal) && name.StartsWith("user.name=", StringComparison.Ordinal)
                    && CommitMessage(args.Skip(7).ToArray()) is { } message && !IdentityFromEnvironment():
                    using (var repo = new LibGit2Sharp.Repository(repository))
                    {
                        // git refuses an empty first commit ("nothing to commit"): git answers that.
                        if (repo.Info.IsHeadUnborn && repo.Index.Count == 0) return null;
                        var who = new LibGit2Sharp.Signature(name["user.name=".Length..], email["user.email=".Length..], DateTimeOffset.Now);
                        repo.Commit(message, who, who, new LibGit2Sharp.CommitOptions());
                        applied = true;
                    }
                    return "";
                case ["rev-parse", "HEAD"]:
                    using (var repo = new LibGit2Sharp.Repository(repository))
                        return repo.Head.Tip is { } tip ? tip.Sha + "\n" : null;
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            // The command took effect (a failure after it, such as on dispose): done, git must not repeat it.
            if (applied) return "";
            // A half-made repository is removed so git starts where the command did; if it cannot be, the setup
            // fails here (git would re-init it and keep libgit2's branch).
            if (created && (Directory.Exists(dotGit) || File.Exists(dotGit)))
            {
                try { Directory.Delete(dotGit, recursive: true); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    throw new IOException("fixture init of " + repository + " failed in process and its .git could not be removed", ex);
                }
            }
            return null;
        }
    }

    // The branch of `init -b <branch>`, `init -q -b <branch>` or `init -b <branch> -q`, when it is a plain branch name
    // libgit2 accepts. Anything else (HEAD, a leading '-', a name libgit2 refuses) goes to git, which decides.
    private static string? InitBranch(string[] args)
    {
        var branch = args switch
        {
            ["init", "-b", var name] => name,
            ["init", "-q", "-b", var name] => name,
            ["init", "-b", var name, "-q"] => name,
            _ => null,
        };
        return branch is { Length: > 0 } && branch != "HEAD" && !branch.StartsWith('-')
               && LibGit2Sharp.Reference.IsValidName("refs/heads/" + branch) ? branch : null;
    }

    // The default file systems of Windows and macOS ignore case in names; Linux's do not.
    private static StringComparison NameComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // A `.git` (directory or file) below `top`, other than the repository's own, in a directory add would reach: a
    // nested repository. Without -f, directories the repository ignores are not entered (git add does not reach them);
    // with -f (enterIgnored) every directory is. The ignore rules are the repository's own (no user excludes file):
    // where a machine's excludes file would make git skip more, the walk is wider than git's, which only sends more
    // commands to git.
    private static bool HasNestedRepository(LibGit2Sharp.Repository repo, string repository, string top, bool enterIgnored)
    {
        var own = Path.GetFullPath(Path.Combine(repository, ".git"));
        var pending = new Stack<string>();
        pending.Push(top);
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (string.Equals(Path.GetFileName(entry), ".git", NameComparison))
                {
                    if (!string.Equals(Path.GetFullPath(entry), own, NameComparison)) return true;
                    continue;
                }
                if (Directory.Exists(entry) && !File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint)
                    && (enterIgnored || !repo.Ignore.IsPathIgnored(Path.GetRelativePath(repository, entry).Replace('\\', '/') + "/")))
                    pending.Push(entry);
            }
        }
        return false;
    }

    // A `.git` in a directory between the repository root (exclusive) and `path`'s parent (inclusive).
    private static bool InNestedRepository(string repository, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(path));
             directory is not null && directory.Length > root.Length;
             directory = Path.GetDirectoryName(directory))
        {
            var marker = Path.Combine(directory, ".git");
            if (Directory.Exists(marker) || File.Exists(marker)) return true;
        }
        return false;
    }

    // The message of `commit -m <message> --no-verify` with those two options in either order; null for any other
    // option list, and for an empty message (git aborts that commit).
    private static string? CommitMessage(string[] options) => options switch
    {
        ["-m", var message, "--no-verify"] when !string.IsNullOrWhiteSpace(message) => message,
        ["--no-verify", "-m", var message] when !string.IsNullOrWhiteSpace(message) => message,
        _ => null,
    };

    // An author or committer the environment names (git prefers it over -c user.*): git answers that commit.
    private static bool IdentityFromEnvironment() =>
        new[] { "GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_AUTHOR_DATE", "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL", "GIT_COMMITTER_DATE" }
            .Any(name => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)));
}
