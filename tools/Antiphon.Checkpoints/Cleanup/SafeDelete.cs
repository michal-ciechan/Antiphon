using System.ComponentModel;
using System.Diagnostics;

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
    private const int GitTimeoutMs = 15_000;
    private const int MaxEntries = 10_000;

    public static DeleteResult Apply(string path, DeleteOwnership ownership, Action? beforeDelete = null, Func<string?>? inventoryCheck = null)
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
            if (!SafeWrite.MatchesCreation(full!)) return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = full!, Reason = "left in place: " + full + "; current-process creation record missing or changed" };
            File.Delete(full!);
        }
        else
        {
            var custody = OwnedOutputLedger.Remove(full!);
            if (custody is not null) return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = full!, Reason = custody };
        }
        return new DeleteResult { Disposition = DeleteDisposition.Deleted, Path = full!, Reason = null };
    }

    public static string? Refusal(string path, DeleteOwnership ownership)
    {
        var full = Canonical(path);
        return full is null ? "path-unsafe" : Evaluate(full, ownership);
    }

    public static DeleteResult DeleteCreatedFile(string path)
    {
        var full = Canonical(path);
        var parent = full is null ? null : Path.GetDirectoryName(full);
        if (full is null || parent is null)
            return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = path, Reason = "path-unsafe" };
        return Apply(full, Private(parent, full, fileOnly: true));
    }

    public static DeleteResult DeleteCreatedTree(string path)
    {
        var full = Canonical(path);
        var parent = full is null ? null : Path.GetDirectoryName(full);
        if (full is null || parent is null)
            return new DeleteResult { Disposition = DeleteDisposition.Refused, Path = path, Reason = "path-unsafe" };
        return Apply(full, Private(parent, full, fileOnly: false));
    }

    public static DeleteOwnership Stamp(string repositoryRoot, string? gitExecutable = null) => new()
    {
        IsolatedRoot = repositoryRoot,
        RequireInventory = false,
        RequireBinName = false,
        AllowOutsideRepository = true,
        StampFile = true,
        FileOnly = true,
        GitExecutable = gitExecutable,
    };

    private static DeleteOwnership Private(string root, string path, bool fileOnly) => new()
    {
        IsolatedRoot = root,
        CreatedPaths = [path],
        RequireInventory = true,
        RequireBinName = false,
        AllowOutsideRepository = true,
        FileOnly = fileOnly,
    };

    private static string? Evaluate(string full, DeleteOwnership ownership)
    {
        var reparse = ReparseReason(full, ownership.IsolatedRoot);
        if (reparse is not null)
            return reparse;
        if (!string.IsNullOrWhiteSpace(ownership.IsolatedRoot) && !OutputCleanup.IsStrictlyInside(ownership.IsolatedRoot, full))
            return "outside-isolated-root";
        if (ownership.StampFile)
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(full) ?? "");
            if (!string.Equals(Path.GetFileName(full), "checkpoint-build-source.json", StringComparison.Ordinal)
                || !OutputCleanup.IsIsolatedOutputId(parent))
                return "not-isolated-output";
        }
        else if (ownership.RequireBinName && !OutputCleanup.IsIsolatedOutputId(Path.GetFileName(full)))
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
        if (ownership.FileOnly || !Directory.Exists(full))
            return null;
        return TreeReason(full);
    }

    private static string? ReparseReason(string full, string? isolatedRoot)
    {
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (!IsReparse(current))
                continue;
            var target = LinkTargetOf(current);
            if (target is not null && !string.IsNullOrWhiteSpace(isolatedRoot))
            {
                var resolved = ResolveTarget(current, target);
                if (resolved is null || !OutputCleanup.IsStrictlyInside(isolatedRoot, resolved))
                    return "reparse-outside-root";
            }

            return "reparse-point";
        }

        return null;
    }

    // An existing path may not be replaced until the same tracked-file and link checks a delete uses
    // have succeeded. A missing path is a create, not an overwrite.
    public static string? ExistingPathRefusal(string path, string? gitExecutable = null, int? timeoutMs = null)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return null;
        var full = Canonical(path);
        if (full is null)
            return "path-unsafe";
        var reparse = ReparseReason(full, null);
        if (reparse is not null)
            return reparse;
        return GitRefusal(full, allowOutsideRepository: true, gitExecutable, timeoutMs ?? GitTimeoutMs);
    }

    private static string? TreeReason(string root)
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
                if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
                    continue;
                foreach (var child in Directory.EnumerateFileSystemEntries(path))
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

    private static bool IsGitMetadataName(string path) =>
        string.Equals(Path.GetFileName(path), ".git", StringComparison.Ordinal);

    // A git answer is required. No metadata is "not a repository", and that is a refusal
    // unless the caller proved this file sits outside any repository.
    private static string? GitRefusal(string full, bool allowOutsideRepository, string? gitExecutable, int timeoutMs)
    {
        var start = Directory.Exists(full) && !IsReparse(full) ? full : Path.GetDirectoryName(full);
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

    // One ls-files of every write root. A git failure refuses; a missing repository has nothing tracked.
    public static string? PreflightRoots(string repository, IReadOnlyCollection<string> roots, string? gitExecutable = null, int? timeoutMs = null)
    {
        if (roots.Count == 0)
            return "git-failed";
        var start = Canonical(repository);
        if (start is null)
            return "path-unsafe";
        var owner = FindOwner(Directory.Exists(start) ? start : Path.GetDirectoryName(start) ?? start);
        if (owner is null)
            return string.IsNullOrWhiteSpace(gitExecutable) ? null : "git-failed";
        var timeout = timeoutMs ?? GitTimeoutMs;
        var git = string.IsNullOrWhiteSpace(gitExecutable) ? "git" : gitExecutable;
        var args = new List<string> { "-C", owner, "ls-files", "-z", "--" };
        foreach (var root in roots)
        {
            var full = Canonical(root);
            if (full is null)
                return "path-unsafe";
            args.Add(full);
        }

        var listed = RunGit(git, owner, args, timeout);
        if (listed.Failure is not null)
            return listed.Failure;
        if (listed.ExitCode != 0)
            return listed.Stderr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                ? "git-failed"
                : "git-failed";
        return listed.Stdout.Length == 0 ? null : "tracked";
    }

    private static string? FindOwner(string start)
    {
        var current = start;
        while (!string.IsNullOrEmpty(current))
        {
            var git = Path.Combine(current, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return current;
            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
                break;
            current = parent;
        }

        return null;
    }

    private readonly record struct GitRun(int ExitCode, string Stdout, string Stderr, string? Failure);

    // A start that throws before a child exists is git-unavailable. ETXTBSY, EAGAIN, and a
    // settle race are retried a few times when the executable is present, then still
    // reported as git-unavailable. A child that was created and then does not finish is
    // git-timeout, including a wait or kill exception after that start.
    private static GitRun RunGit(string git, string workingDirectory, IReadOnlyList<string> args, int timeoutMs)
    {
        if (timeoutMs < 1)
            timeoutMs = GitTimeoutMs;
        Process? process = null;
        var startedChild = false;
        try
        {
            var start = new ProcessStartInfo(git)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.Environment.Remove("GIT_DIR");
            start.Environment.Remove("GIT_WORK_TREE");
            start.Environment.Remove("GIT_INDEX_FILE");
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            process = StartGit(git, start);
            if (process is null)
                return new GitRun(-1, "", "", "git-unavailable");
            startedChild = true;
            // Read both pipes before waiting. A synchronous read ahead of the timeout never reaches WaitForExit
            // when git leaves stdout open.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                KillTree(process);
                Drain(stdoutTask, stderrTask, 2_000);
                return new GitRun(-1, "", "", "git-timeout");
            }

            if (!Drain(stdoutTask, stderrTask, timeoutMs))
            {
                KillTree(process);
                return new GitRun(-1, "", "", "git-timeout");
            }

            return new GitRun(process.ExitCode, stdoutTask.Result, stderrTask.Result, null);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException)
        {
            if (startedChild && process is not null)
                KillTree(process);
            return new GitRun(-1, "", "", startedChild ? "git-timeout" : "git-unavailable");
        }
        finally
        {
            process?.Dispose();
        }
    }

    private const int SpawnAttempts = 6;

    private static Process? StartGit(string git, ProcessStartInfo start)
    {
        for (var attempt = 1; attempt <= SpawnAttempts; attempt++)
        {
            try
            {
                var process = Process.Start(start);
                if (process is not null || attempt >= SpawnAttempts || !ExecutableExists(git))
                    return process;
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException)
            {
                if (attempt >= SpawnAttempts || !IsTransientSpawn(ex, git))
                    throw;
            }

            Thread.Sleep(25 << (attempt - 1));
        }

        return null;
    }

    private static bool IsTransientSpawn(Exception ex, string git)
    {
        if (ex is not Win32Exception win || !ExecutableExists(git))
            return false;
        var code = win.NativeErrorCode;
        if (OperatingSystem.IsWindows())
            return code is 8 or 1450;
        return code is 4 or 11 or 12 or 26 or 2 or 13;
    }

    private static bool ExecutableExists(string git)
    {
        if (File.Exists(git))
            return true;
        if (git.Contains('/') || git.Contains('\\'))
            return false;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return false;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(dir, git)))
                return true;
            if (OperatingSystem.IsWindows() && File.Exists(Path.Combine(dir, git + ".exe")))
                return true;
        }

        return false;
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }

        try
        {
            if (!process.HasExited)
                process.WaitForExit(2_000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or SystemException)
        {
        }
    }

    private static bool Drain(Task<string> stdout, Task<string> stderr, int timeoutMs)
    {
        try
        {
            return Task.WhenAll(stdout, stderr).Wait(timeoutMs);
        }
        catch (AggregateException)
        {
            return false;
        }
    }

    private static bool Listed(IReadOnlyCollection<string> created, string full)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var candidate in created)
        {
            var other = Canonical(candidate);
            if (other is not null && string.Equals(other, full, comparison))
                return true;
        }

        return false;
    }

    private static string? Canonical(string path)
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

    private static bool Exists(string full) => File.Exists(full) || Directory.Exists(full);

    private static bool IsReparse(string path) => LinkTargetOf(path) is not null || HasReparseAttribute(path);

    private static string? LinkTargetOf(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            if (!string.IsNullOrEmpty(directory.LinkTarget))
                return directory.LinkTarget;
            var file = new FileInfo(path);
            return string.IsNullOrEmpty(file.LinkTarget) ? null : file.LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool HasReparseAttribute(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string? ResolveTarget(string link, string target)
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
