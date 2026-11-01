using Antiphon.HostCleanup.Checkpoints;
using System.ComponentModel;
using System.Globalization;

namespace Antiphon.Checkpoints;

public enum DeleteDisposition
{
    Deleted,
    DryRun,
    Skipped,
    Refused,
}

public sealed class DeleteResult
{
    public DeleteDisposition Disposition { get; init; }
    public string Path { get; init; } = "";
    public string? Reason { get; init; }
}

public sealed class DeleteOwnership
{
    public string IsolatedRoot { get; init; } = "";
    public IReadOnlyCollection<string>? CreatedPaths { get; init; }
    public bool RequireInventory { get; init; } = true;
    public bool RequireBinName { get; init; } = true;
    public bool DryRun { get; init; }
    public bool AllowOutsideRepository { get; init; }
    public bool StampFile { get; init; }
    public bool FileOnly { get; init; }
    public string? GitExecutable { get; init; }
    public int GitTimeoutMilliseconds { get; init; } = 15_000;
}

public static class SafeDelete
{
    public static DeleteResult Apply(string path, DeleteOwnership ownership, Action? beforeDelete = null, Func<string?>? inventoryCheck = null) =>
        new CheckpointDelete(CheckpointCleanupDefaults.Context).Apply(path, ownership, beforeDelete, inventoryCheck);
    internal static DeleteResult ApplyToolCopy(string path, DeleteOwnership ownership, ToolCopyAuthority authority,
        Action? beforeDelete = null) =>
        new CheckpointDelete(CheckpointCleanupDefaults.Context).ApplyToolCopy(path, ownership, authority, beforeDelete);
    public static string? Refusal(string path, DeleteOwnership ownership) =>
        new CheckpointDelete(CheckpointCleanupDefaults.Context).Refusal(path, ownership);
    public static DeleteResult DeleteCreatedFile(string path) =>
        new CheckpointDelete(CheckpointCleanupDefaults.Context).DeleteCreatedFile(path);
    public static DeleteResult DeleteCreatedTree(string path) =>
        new CheckpointDelete(CheckpointCleanupDefaults.Context).DeleteCreatedTree(path);
    public static DeleteOwnership Stamp(string repositoryRoot, string? gitExecutable = null) =>
        new CheckpointDelete(CheckpointCleanupDefaults.Context).Stamp(repositoryRoot, gitExecutable);
    public static string? ExistingPathRefusal(string path, string? gitExecutable = null, int? timeoutMs = null) =>
        new CheckpointDelete(CheckpointCleanupDefaults.Context).ExistingPathRefusal(path, gitExecutable, timeoutMs);
    public static string? PreflightRoots(string repository, IReadOnlyCollection<string> roots, string? gitExecutable = null, int? timeoutMs = null, int? commandLineBudget = null) =>
        new CheckpointDelete(CheckpointCleanupDefaults.Context).PreflightRoots(repository, roots, gitExecutable, timeoutMs, commandLineBudget);
}

public sealed class CheckpointDelete(CheckpointCleanupContext context)
{
    private ICheckpointCleanupIo Io => context.Io;
    private CheckpointWrite SafeWrite => new(context);
    private CheckpointOutputLedger OwnedOutputLedger => new(context);
    private const int GitTimeoutMs = 15_000;
    private const int MaxEntries = 10_000;

    public DeleteResult Apply(string path, DeleteOwnership ownership, Action? beforeDelete = null, Func<string?>? inventoryCheck = null)
        => ApplyCore(path, ownership, beforeDelete, inventoryCheck, null);

    internal DeleteResult ApplyToolCopy(string path, DeleteOwnership ownership, ToolCopyAuthority authority,
        Action? beforeDelete = null) =>
        ApplyCore(path, ownership, beforeDelete, authority.Check, authority);

    private DeleteResult ApplyCore(string path, DeleteOwnership ownership, Action? beforeDelete,
        Func<string?>? inventoryCheck, ToolCopyAuthority? toolAuthority)
    {
        var full = Canonical(path);
        var reason = full is null ? "path-unsafe" : Evaluate(full, ownership);
        if (reason is not null)
            return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = full ?? path, Reason = reason };
        if (!Exists(full!))
            return new DeleteResult { Disposition = DeleteDisposition.Skipped, Path = full!, Reason = null };

        beforeDelete?.Invoke();
        reason = Evaluate(full!, ownership) ?? inventoryCheck?.Invoke();
        if (reason is not null)
            return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = full!, Reason = reason };
        if (ownership.DryRun)
            return new DeleteResult { Disposition = DeleteDisposition.DryRun, Path = full!, Reason = null };

        if (ownership.FileOnly)
        {
            if (!SafeWrite.MatchesCreation(full!))
                return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = full!, Reason = "left in place: " + full + "; current-process creation record missing or changed" };
            if (context.Registry.Identity(full!) is not { } expected)
                return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = full!, Reason = "left in place: " + full + "; current-process creation record missing or changed" };
            Io.DeleteAnchored(full!, expected, directory: false);
        }
        else
        {
            var custody = toolAuthority is null ? OwnedOutputLedger.Remove(full!) : toolAuthority.Remove(full!);
            if (custody is not null) return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = full!, Reason = custody };
        }
        return new DeleteResult { Disposition = DeleteDisposition.Deleted, Path = full!, Reason = null };
    }

    public string? Refusal(string path, DeleteOwnership ownership)
    {
        var full = Canonical(path);
        return full is null ? "path-unsafe" : Evaluate(full, ownership);
    }

    public DeleteResult DeleteCreatedFile(string path)
    {
        var full = Canonical(path);
        var parent = full is null ? null : Path.GetDirectoryName(full);
        if (full is null || parent is null)
            return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = path, Reason = "path-unsafe" };
        return Apply(full, Private(parent, full, fileOnly: true));
    }

    public DeleteResult DeleteCreatedTree(string path)
    {
        var full = Canonical(path);
        var parent = full is null ? null : Path.GetDirectoryName(full);
        if (full is null || parent is null)
            return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = path, Reason = "path-unsafe" };
        return Apply(full, Private(parent, full, fileOnly: false));
    }

    public DeleteOwnership Stamp(string repositoryRoot, string? gitExecutable = null) => new()
    {
        IsolatedRoot = repositoryRoot,
        RequireInventory = false,
        RequireBinName = false,
        AllowOutsideRepository = true,
        StampFile = true,
        FileOnly = true,
        GitExecutable = gitExecutable,
    };

    private DeleteOwnership Private(string root, string path, bool fileOnly) => new()
    {
        IsolatedRoot = root,
        CreatedPaths = [path],
        RequireInventory = true,
        RequireBinName = false,
        AllowOutsideRepository = true,
        FileOnly = fileOnly,
    };

    private string? Evaluate(string full, DeleteOwnership ownership)
    {
        var reparse = ReparseReason(full, ownership.IsolatedRoot);
        if (reparse is not null)
            return reparse;
        if (!string.IsNullOrWhiteSpace(ownership.IsolatedRoot) && !context.IsStrictlyInside(ownership.IsolatedRoot, full))
            return "outside-isolated-root";
        if (ownership.StampFile)
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(full) ?? "");
            if (!string.Equals(Path.GetFileName(full), "checkpoint-build-source.json", StringComparison.Ordinal)
                || !CheckpointOutputNames.IsIsolatedOutputId(parent))
                return "not-isolated-output";
        }
        else if (ownership.RequireBinName && !CheckpointOutputNames.IsIsolatedOutputId(Path.GetFileName(full)))
            return "not-isolated-output";
        if (ownership.RequireInventory)
        {
            if (ownership.CreatedPaths is null)
                return "inventory-missing";
            if (!Listed(ownership.CreatedPaths, full))
                return "not-created-by-run";
        }

        var git = GitRefusal(full, ownership.AllowOutsideRepository, ownership.GitExecutable, ownership.GitTimeoutMilliseconds);
        if (git is not null)
            return git;
        if (ownership.FileOnly || !Io.DirectoryExists(full))
            return null;
        return TreeReason(full);
    }

    private string? ReparseReason(string full, string? isolatedRoot)
    {
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (!IsReparse(current))
                continue;
            var target = LinkTargetOf(current);
            if (target is not null && !string.IsNullOrWhiteSpace(isolatedRoot))
            {
                var resolved = ResolveTarget(current, target);
                if (resolved is null || !context.IsStrictlyInside(isolatedRoot, resolved))
                    return "reparse-outside-root";
            }

            return "reparse-point";
        }

        return null;
    }

    // An existing path may not be replaced until the same tracked-file and link checks a delete uses
    // have succeeded. A missing path is a create, not an overwrite.
    public string? ExistingPathRefusal(string path, string? gitExecutable = null, int? timeoutMs = null)
    {
        if (!Io.FileExists(path) && !Io.DirectoryExists(path))
            return null;
        var full = Canonical(path);
        if (full is null)
            return "path-unsafe";
        var reparse = ReparseReason(full, null);
        if (reparse is not null)
            return reparse;
        return GitRefusal(full, allowOutsideRepository: true, gitExecutable, timeoutMs ?? GitTimeoutMs);
    }

    private string? TreeReason(string root)
    {
        if (IsGitMetadataName(root))
            return "nested_repository";
        var pending = new Stack<string>();
        pending.Push(root);
        var seen = 0;
        while (pending.Count > 0)
        {
            var path = pending.Pop();
            if (++seen > MaxEntries)
                return "unsafe-tree";
            if (IsReparse(path))
                return "reparse-point";
            if (IsGitMetadataName(path))
                return "nested_repository";
            try
            {
                if ((Io.Attributes(path) & FileAttributes.Directory) == 0)
                    continue;
                foreach (var child in Io.Enumerate(path))
                {
                    // Name check before push: do not follow a link, and do not enter .git.
                    if (IsGitMetadataName(child))
                        return "nested_repository";
                    pending.Push(child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "unsafe-tree";
            }
        }

        return null;
    }

    private bool IsGitMetadataName(string path) =>
        string.Equals(Path.GetFileName(path), ".git", StringComparison.Ordinal);

    // A git answer is required. No metadata is "not a repository", and that is a refusal
    // unless the caller proved this file sits outside any repository.
    private string? GitRefusal(string full, bool allowOutsideRepository, string? gitExecutable, int timeoutMs)
    {
        var start = Io.DirectoryExists(full) && !IsReparse(full) ? full : Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(start))
            return "path-unsafe";
        var owner = FindOwner(start);
        if (owner is null)
            return allowOutsideRepository ? null : "not-a-repository";

        var git = string.IsNullOrWhiteSpace(gitExecutable) ? "git" : gitExecutable;
        var top = RunGit(git, owner, ["-C", owner, "rev-parse", "--show-toplevel"], timeoutMs);
        if (top.Failure is not null)
            return top.Failure;
        if (top.ExitCode != 0)
            return top.Stderr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                ? "not-a-repository"
                : "git-failed";
        var toplevel = top.Stdout.Trim();
        if (toplevel.Length == 0)
            return "git-failed";

        var listed = RunGit(git, toplevel, ["-C", toplevel, "ls-files", "-z", "--", full], timeoutMs);
        if (listed.Failure is not null)
            return listed.Failure;
        if (listed.ExitCode != 0)
            return listed.Stderr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                ? "not-a-repository"
                : "git-failed";
        return listed.Stdout.Length == 0 ? null : "tracked";
    }

    // CreateProcess rejects a command line at 32,767 characters, and cmd.exe at 8,191.
    // 8,000 leaves room for the runner's fixed Git hardening prefix and Windows quoting.
    internal const int DefaultCommandLineBudget = 8_000;

    internal static int ArgumentCharacters(IReadOnlyList<string> arguments)
    {
        long total = 0;
        foreach (var argument in arguments)
        {
            if (total != 0) total++;
            total += argument.Length;
            if (total >= int.MaxValue) return int.MaxValue;
        }

        return (int)total;
    }

    private static string FormatStartFailure(Win32Exception ex, IReadOnlyList<string> arguments) =>
        "git-start-failed: Win32Exception win32="
        + ex.NativeErrorCode.ToString(CultureInfo.InvariantCulture)
        + " " + ex.Message
        + " argumentCharacters="
        + ArgumentCharacters(arguments).ToString(CultureInfo.InvariantCulture);

    // One ls-files of every write root. A git failure refuses; a missing repository has nothing tracked.
    // Paths that fit the command-line budget stay one invocation, in the same order.
    // A longer list is split. Any batch failure fails the call. Emptiness is the union.
    public string? PreflightRoots(string repository, IReadOnlyCollection<string> roots, string? gitExecutable = null, int? timeoutMs = null, int? commandLineBudget = null)
    {
        if (roots.Count == 0)
            return "git-failed";
        var budget = commandLineBudget ?? DefaultCommandLineBudget;
        if (budget < 1)
            return "git-start-failed: argumentCharacters=0 budget=" + budget.ToString(CultureInfo.InvariantCulture);
        var start = Canonical(repository);
        if (start is null)
            return "path-unsafe";
        var owner = FindOwner(Io.DirectoryExists(start) ? start : Path.GetDirectoryName(start) ?? start);
        if (owner is null)
            return string.IsNullOrWhiteSpace(gitExecutable) ? null : "git-failed";
        var timeout = timeoutMs ?? GitTimeoutMs;
        var git = string.IsNullOrWhiteSpace(gitExecutable) ? "git" : gitExecutable;
        var paths = new List<string>(roots.Count);
        foreach (var root in roots)
        {
            var full = Canonical(root);
            if (full is null)
                return "path-unsafe";
            paths.Add(full);
        }

        var prefix = new[] { "-C", owner, "ls-files", "-z", "--" };
        var batch = new List<string>(prefix);
        var matched = false;
        foreach (var path in paths)
        {
            if (batch.Count > prefix.Length)
            {
                var trial = (long)ArgumentCharacters(batch) + 1 + path.Length;
                if (trial > budget)
                {
                    var flushed = Consume(batch, ref matched);
                    if (flushed is not null)
                        return flushed;
                    batch = new List<string>(prefix);
                }
            }

            batch.Add(path);
            if (ArgumentCharacters(batch) > budget)
                return "git-start-failed: argumentCharacters="
                    + ArgumentCharacters(batch).ToString(CultureInfo.InvariantCulture)
                    + " budget=" + budget.ToString(CultureInfo.InvariantCulture)
                    + " one path exceeds the command-line budget";
        }

        var last = Consume(batch, ref matched);
        if (last is not null)
            return last;
        return matched ? "tracked" : null;

        string? Consume(List<string> arguments, ref bool any)
        {
            var listed = RunGit(git, owner, arguments, timeout);
            if (listed.Failure is not null)
                return listed.Failure;
            if (listed.ExitCode != 0)
                return "git-failed";
            if (listed.Stdout.Length != 0)
                any = true;
            return null;
        }
    }

    private string? FindOwner(string start)
    {
        var current = start;
        while (!string.IsNullOrEmpty(current))
        {
            var git = Path.Combine(current, ".git");
            if (Io.DirectoryExists(git) || Io.FileExists(git))
                return current;
            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
                break;
            current = parent;
        }

        return null;
    }

    private readonly record struct GitRun(int ExitCode, string Stdout, string Stderr, string? Failure);

    // The shared CheckpointProcessRunner owns one deadline for Git config inspection, process
    // exit and both output drains. Expiry kills the owned Linux process group (Windows job), so an
    // exited child's descendant cannot keep a pipe open, and Git runs with fsmonitor, hooks,
    // optional locks, global/system config and named filter/textconv drivers disabled.
    // A missing executable is git-unavailable. ETXTBSY, EAGAIN, and a settle race are retried
    // a few times inside that deadline when the executable is present, then still reported as
    // git-unavailable. Any other Win32 start failure names the native code, the OS message,
    // and argumentCharacters. A child that was created and then does not finish is git-timeout,
    // including a custody failure after that start.
    private GitRun RunGit(string git, string workingDirectory, IReadOnlyList<string> args, int timeoutMs)
    {
        if (timeoutMs < 1)
            timeoutMs = GitTimeoutMs;
        var started = context.Time.GetTimestamp();
        for (var attempt = 1; ; attempt++)
        {
            var remaining = timeoutMs - (int)Math.Min(context.Time.GetElapsedTime(started).TotalMilliseconds, timeoutMs);
            if (remaining < 1)
                return new GitRun(-1, "", "", "git-unavailable");
            CheckpointGitResult result;
            try
            {
                result = context.Git.Run(new(git, workingDirectory, args, remaining));
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException)
            {
                if (ex is Win32Exception win && ExecutableExists(git) && !IsTransientSpawn(ex, git))
                    return new GitRun(-1, "", "", FormatStartFailure(win, args));
                if (attempt >= SpawnAttempts || !IsTransientSpawn(ex, git))
                    return new GitRun(-1, "", "", "git-unavailable");
                context.Delay(TimeSpan.FromMilliseconds(Math.Min(25 << (attempt - 1), Math.Max(0, timeoutMs - context.Time.GetElapsedTime(started).TotalMilliseconds))));
                continue;
            }
            catch (InvalidOperationException)
            {
                return new GitRun(-1, "", "", "git-timeout");
            }

            if (result.TimedOut)
                return new GitRun(-1, "", "", "git-timeout");
            // Linux starts Git through setsid; its exec failure is a start failure, not a Git answer.
            if (StartFailed(result))
            {
                if (attempt >= SpawnAttempts || !ExecutableExists(git))
                    return new GitRun(-1, "", "", "git-unavailable");
                context.Delay(TimeSpan.FromMilliseconds(Math.Min(25 << (attempt - 1), Math.Max(0, timeoutMs - context.Time.GetElapsedTime(started).TotalMilliseconds))));
                continue;
            }

            return new GitRun(result.Exit, result.Stdout, result.Stderr, null);
        }
    }

    private const int SpawnAttempts = 6;

    private bool StartFailed(CheckpointGitResult result) =>
        Io.Platform == CheckpointPlatform.Linux && result.Exit is (126 or 127)
        && result.Stderr.StartsWith("setsid: failed to execute ", StringComparison.Ordinal);

    private bool IsTransientSpawn(Exception ex, string git)
    {
        if (ex is not Win32Exception win || !ExecutableExists(git))
            return false;
        var code = win.NativeErrorCode;
        if (Io.Platform == CheckpointPlatform.Windows)
            return code is 8 or 1450;
        return code is 4 or 11 or 12 or 26 or 2 or 13;
    }

    private bool ExecutableExists(string git)
    {
        if (Io.FileExists(git))
            return true;
        if (git.Contains('/') || git.Contains('\\'))
            return false;
        var path = Io.EnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return false;
        foreach (var dir in path.Split(Io.Platform == CheckpointPlatform.Windows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Io.FileExists(Path.Combine(dir, git)))
                return true;
            if (Io.Platform == CheckpointPlatform.Windows && Io.FileExists(Path.Combine(dir, git + ".exe")))
                return true;
        }

        return false;
    }

    private bool Listed(IReadOnlyCollection<string> created, string full)
    {
        var comparison = Io.Platform == CheckpointPlatform.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var candidate in created)
        {
            var other = Canonical(candidate);
            if (other is not null && string.Equals(other, full, comparison))
                return true;
        }

        return false;
    }

    private string? Canonical(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private bool Exists(string full) => Io.FileExists(full) || Io.DirectoryExists(full);

    private bool IsReparse(string path) => LinkTargetOf(path) is not null || HasReparseAttribute(path);

    private string? LinkTargetOf(string path)
    {
        try
        {
            return Io.LinkTarget(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private bool HasReparseAttribute(string path)
    {
        try
        {
            return (Io.Attributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private string? ResolveTarget(string link, string target)
    {
        try
        {
            var combined = Path.IsPathRooted(target)
                ? target
                : Path.Combine(Path.GetDirectoryName(link) ?? link, target);
            return Path.GetFullPath(combined);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
