using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0604 D-15. Creates and removes the runner-side MIRROR worktrees that remote ordinary tasks
/// run in. The desktop worktree stays canonical and the branch on origin is the unit of exchange,
/// so everything here operates on a commit that already exists remotely.
///
/// Every git process is a direct child of the runner process, started through
/// <see cref="ProcessStartInfo.ArgumentList"/> (no shell, no quoting), as the runner's own uid.
/// </summary>
public sealed class RunnerWorkspaceService
{
    // A mirror name is the dispatcher's own task-<short> form and nothing else: it becomes a
    // directory name the runner creates, so an unconstrained name is a path-traversal primitive.
    private static readonly Regex NamePattern = new(@"^task-[0-9a-f]{8}$", RegexOptions.Compiled);
    private static readonly Regex ShaPattern = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    private static readonly Regex BranchPattern = new(@"^[A-Za-z0-9._/-]{1,200}$", RegexOptions.Compiled);

    private readonly string _repository;
    private readonly string _worktreeRoot;
    private readonly TimeSpan _timeout;

    public RunnerWorkspaceService(string repository, string allowedCwd, TimeSpan? timeout = null)
    {
        _repository = repository;
        _worktreeRoot = allowedCwd.TrimEnd('/') + "/worktrees";
        _timeout = timeout ?? TimeSpan.FromMinutes(10);
    }

    public string WorktreeRoot => _worktreeRoot;

    public async Task<PhoneHomeWorkspaceMirrorResponse> MirrorAsync(
        PhoneHomeWorkspaceMirrorRequest request, CancellationToken ct)
    {
        if (!NamePattern.IsMatch(request.Name ?? ""))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Mirror name must be task-<8 hex>.", 409);
        if (!ShaPattern.IsMatch(request.Sha ?? ""))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Mirror sha must be 40 lowercase hex characters.", 409);
        if (!BranchPattern.IsMatch(request.Branch ?? "") || request.Branch!.Contains("..", StringComparison.Ordinal))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Mirror branch name is not admitted.", 409);

        var path = _worktreeRoot + "/" + request.Name;
        if (Directory.Exists(path))
        {
            // Idempotent for a redispatch of the same task: the mirror is only reused when it is
            // already on the exact commit asked for, never adopted at some other tip.
            var existing = await GitAsync(path, ct, "rev-parse", "HEAD");
            if (existing.ExitCode == 0 && existing.Stdout.Trim() == request.Sha)
                return new PhoneHomeWorkspaceMirrorResponse(path);
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget,
                $"A mirror already exists at {path} at a different commit.",
                409);
        }

        var fetch = await GitAsync(_repository, ct, "fetch", "origin", request.Branch!);
        if (fetch.ExitCode != 0)
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Mirror fetch failed: " + Tail(fetch.Stderr), 409);

        // G-27: the fetched tip must be exactly the sha the desktop pushed. A branch that moved
        // between the push and the mirror would silently run the session on someone else's commit.
        var tip = await GitAsync(_repository, ct, "rev-parse", "FETCH_HEAD");
        if (tip.ExitCode != 0 || tip.Stdout.Trim() != request.Sha)
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget,
                $"Fetched tip of {request.Branch} is not {request.Sha}.",
                409);

        Directory.CreateDirectory(_worktreeRoot);
        var add = await GitAsync(_repository, ct, "worktree", "add", "-B", request.Branch!, path, request.Sha!);
        if (add.ExitCode != 0)
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Mirror creation failed: " + Tail(add.Stderr), 409);

        var head = await GitAsync(path, ct, "rev-parse", "HEAD");
        if (head.ExitCode != 0 || head.Stdout.Trim() != request.Sha)
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Mirror HEAD is not the requested sha.", 409);

        return new PhoneHomeWorkspaceMirrorResponse(path);
    }

    public async Task<PhoneHomeWorkspaceRemoveResponse> RemoveAsync(
        PhoneHomeWorkspaceRemoveRequest request, CancellationToken ct)
    {
        var path = request.Path ?? "";
        if (!IsUnderRoot(path))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Only a mirror under the worktree root may be removed.", 409);
        if (!Directory.Exists(path))
            return new PhoneHomeWorkspaceRemoveResponse(true, null);

        if (!request.Force)
        {
            // A dirty mirror is residue the operator must see, not something to delete: the work
            // in it was never pushed, so removing it destroys the only copy.
            var status = await GitAsync(path, ct, "status", "--porcelain");
            if (status.ExitCode != 0)
                return new PhoneHomeWorkspaceRemoveResponse(false, path + " (status unavailable)");
            if (status.Stdout.Trim().Length > 0)
                throw new PhoneHomeAdmissionException(
                    PhoneHomeProblemTypes.UnsupportedTarget, "Mirror has uncommitted changes.", 409);
        }

        var remove = await GitAsync(_repository, ct, "worktree", "remove", "--force", path);
        if (remove.ExitCode != 0 && Directory.Exists(path))
            return new PhoneHomeWorkspaceRemoveResponse(false, path);
        await GitAsync(_repository, ct, "worktree", "prune");
        return new PhoneHomeWorkspaceRemoveResponse(!Directory.Exists(path), Directory.Exists(path) ? path : null);
    }

    /// <summary>
    /// CARD-0604 G-21. Writes a spilled body the Input operation carried, inside the session's own
    /// runner cwd. The relative path is the dispatcher's own and is still bounded here: the runner
    /// never writes outside the directory it was told to write in.
    /// </summary>
    public async Task WriteSpillAsync(string runnerCwd, PhoneHomeInputSpill spill, CancellationToken ct)
    {
        if (!IsUnderRoot(runnerCwd) && runnerCwd.TrimEnd('/') != Path.GetDirectoryName(_worktreeRoot)?.Replace('\\', '/'))
        {
            // The workspace root itself is admitted (a named agent's cwd); anything else must be
            // a mirror under the worktree root.
            var root = Path.GetDirectoryName(_worktreeRoot)?.Replace('\\', '/') ?? "";
            if (runnerCwd.TrimEnd('/') != root)
                throw new PhoneHomeAdmissionException(
                    PhoneHomeProblemTypes.UnsupportedTarget, "Spill target is outside the runner workspace.", 409);
        }

        var relative = (spill.RelativePath ?? "").Replace('\\', '/');
        // An ABSOLUTE path is refused rather than quietly rebased under the cwd: "/etc/passwd"
        // silently becoming "<mirror>/etc/passwd" would be safe but is not what the caller asked
        // for, and a spill pointer the agent is told to read must name the file that exists.
        if (relative.Length == 0
            || relative[0] == '/'
            || (relative.Length > 1 && relative[1] == ':')
            || relative.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Spill relative path is not admitted.", 409);

        var full = runnerCwd.TrimEnd('/') + "/" + relative;
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(full, spill.Body ?? "", new UTF8Encoding(false), ct);
    }

    private bool IsUnderRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var prefix = _worktreeRoot + "/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var rest = normalized[prefix.Length..];
        return rest.Length > 0 && !rest.Contains('/', StringComparison.Ordinal) && rest is not ("." or "..");
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> GitAsync(
        string workingDirectory, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        // No interactive credential prompt can ever block this process: the deploy key is the only
        // credential and it is BatchMode.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("git did not start");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "git " + string.Join(' ', args) + " timed out.", 409);
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    private static string Tail(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(line => line.Trim().Length > 0).ToList();
        return lines.Count == 0 ? "" : lines[^1];
    }
}
