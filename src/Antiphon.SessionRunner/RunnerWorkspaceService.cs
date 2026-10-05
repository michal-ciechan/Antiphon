using System.ComponentModel;
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
public sealed partial class RunnerWorkspaceService
{
    // A mirror name is the dispatcher's own task-<short> form and nothing else: it becomes a
    // directory name the runner creates, so an unconstrained name is a path-traversal primitive.
    private static readonly Regex NamePattern = new(@"^task-[0-9a-f]{8}$", RegexOptions.Compiled);
    private static readonly Regex ShaPattern = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    private static readonly Regex BranchPattern = new(@"^[A-Za-z0-9._/-]{1,200}$", RegexOptions.Compiled);

    private readonly string _repository;
    private readonly RunnerRepositoryPolicy _policy;
    private readonly string _worktreeRoot;
    // CARD-0604 D-19: schema-2 creation metadata lives beside the worktree root, never inside a
    // snapshot -- the snapshot is the thing being removed, and its own record cannot go with it.
    private readonly string _verificationMetadataRoot;
    private readonly TimeSpan _timeout;
    // CARD-0604 D-19: the published branch a verification sha must be reachable from. Production
    // is master; it is a parameter only so the runner-side tests can stand up a scratch repo.
    private readonly string _publishedBranch;
    // CARD-0631 D-6: runner-owned, never request-controlled. Anonymous HTTPS reads work without the
    // deploy key, so a fresh volume is provisioned by the first mirror rather than at boot.
    internal const string DefaultCloneSource = "https://github.com/michal-ciechan/Antiphon.git";
    private readonly string _cloneSource;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public RunnerWorkspaceService(string repository, string allowedCwd, TimeSpan? timeout = null,
        string publishedBranch = "master")
        : this(repository, allowedCwd, DefaultCloneSource, Process.Start, timeout, publishedBranch)
    {
    }

    /// <summary>
    /// CARD-0631 D-6/D-8 test seam: a local bare origin stands in for GitHub, and the process-start
    /// delegate lets a test fail or hold a git start without touching PATH or the installed git.
    /// </summary>
    internal RunnerWorkspaceService(string repository, string allowedCwd, string cloneSource,
        Func<ProcessStartInfo, Process?> startProcess, TimeSpan? timeout = null, string publishedBranch = "master")
        : this(new RunnerRepositoryPolicy(repository, cloneSource,
            Path.GetDirectoryName(repository) ?? repository, [cloneSource], true),
            allowedCwd, startProcess, timeout, publishedBranch)
    {
    }

    public RunnerWorkspaceService(RunnerRepositoryPolicy policy, string allowedCwd, TimeSpan? timeout = null,
        string publishedBranch = "master")
        : this(policy, allowedCwd, Process.Start, timeout, publishedBranch)
    {
    }

    internal RunnerWorkspaceService(RunnerRepositoryPolicy policy, string allowedCwd,
        Func<ProcessStartInfo, Process?> startProcess, TimeSpan? timeout = null, string publishedBranch = "master")
    {
        _policy = policy;
        _cloneSource = policy.PrimaryCloneSource;
        _startProcess = startProcess;
        _repository = policy.PrimaryPath;
        _worktreeRoot = allowedCwd.TrimEnd('/') + "/worktrees";
        _verificationMetadataRoot = allowedCwd.TrimEnd('/') + "/verification-creations";
        _timeout = timeout ?? TimeSpan.FromMinutes(10);
        _publishedBranch = publishedBranch;
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

        var (repository, cloneSource, named, secondary) = ResolveRepository(request.Repository);
        var path = _worktreeRoot + "/" + request.Name;
        if (named && (Directory.Exists(Path.Combine(repository, ".git"))
                      || File.Exists(Path.Combine(repository, ".git"))))
            await VerifyOriginAsync(repository, cloneSource, ct);
        if (Directory.Exists(path))
        {
            // Idempotent for a redispatch of the same task: the mirror is only reused when it is
            // already on the exact commit asked for, never adopted at some other tip.
            var existing = await GitAsync(path, ct, "rev-parse", "HEAD");
            if (existing.ExitCode == 0 && existing.Stdout.Trim() == request.Sha)
            {
                if (!named || PathsEqual(await OwningRepositoryAsync(path, ct), repository))
                    return new PhoneHomeWorkspaceMirrorResponse(path);
            }
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget,
                $"A mirror already exists at {path} at a different commit.",
                409);
        }

        await EnsureRepositoryAsync(repository, cloneSource, ct);
        if (named)
            await VerifyOriginAsync(repository, cloneSource, ct);
        if (secondary && _policy.ProbeSecondaryPushAccess)
            await ProbePushAccessAsync(repository, cloneSource, ct);

        var fetch = await GitAsync(repository, ct, "fetch", "origin", request.Branch!);
        if (fetch.ExitCode != 0)
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Mirror fetch failed: " + Tail(fetch.Stderr), 409);

        // G-27: the fetched tip must be exactly the sha the desktop pushed. A branch that moved
        // between the push and the mirror would silently run the session on someone else's commit.
        var tip = await GitAsync(repository, ct, "rev-parse", "FETCH_HEAD");
        if (tip.ExitCode != 0 || tip.Stdout.Trim() != request.Sha)
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget,
                $"Fetched tip of {request.Branch} is not {request.Sha}.",
                409);

        Filesystem("create the worktree root", () => Directory.CreateDirectory(_worktreeRoot));
        var add = await GitAsync(repository, ct, "worktree", "add", "-B", request.Branch!, path, request.Sha!);
        if (add.ExitCode != 0)
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Mirror creation failed: " + Tail(add.Stderr), 409);

        var head = await GitAsync(path, ct, "rev-parse", "HEAD");
        if (head.ExitCode != 0 || head.Stdout.Trim() != request.Sha)
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Mirror HEAD is not the requested sha.", 409);

        return new PhoneHomeWorkspaceMirrorResponse(path);
    }

    /// <summary>
    /// CARD-0631 D-6/D-7. A fresh runner volume has no repository at all, so the first mirror
    /// provisions it with an anonymous blobless clone of the runner-owned source. Anything already
    /// holding a <c>.git</c> (directory or gitfile) is the repository and git validates it on fetch;
    /// it is never recloned because HEAD moved. Only an absent or empty destination is cloned into:
    /// a file, or a directory with someone else's content in it, is refused and left exactly as
    /// found. The dispatcher's mutation lock already serializes mirrors, so no second lock here.
    /// </summary>
    private (string Path, string Source, bool Named, bool Secondary) ResolveRepository(string? requested)
    {
        if (requested is null)
            return (_repository, _cloneSource, false, false);
        if (!RepositoryCloneSource.TryNormalize(requested, out var identity)
            || !RepositoryCloneSource.TryNormalize(_cloneSource, out var primary)
            || !RepositoryCloneSource.IsAdmitted(identity, _policy.AllowedCloneSources, primary))
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.RepositoryNotAdmitted,
                "Repository is not admitted by this runner.", 409);
        if (identity == primary)
            return (_repository, primary, true, false);
        if (!RepositoryCloneSource.TryDeriveName(identity, out var name))
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.RepositoryNotAdmitted,
                "Repository has no admitted checkout name.", 409);
        return (Path.Combine(_policy.RepositoriesRoot, name), identity, true, true);
    }

    private async Task VerifyOriginAsync(string repository, string requested, CancellationToken ct)
    {
        var origin = await GitAsync(repository, ct, "remote", "get-url", "origin");
        var found = "";
        var hasIdentity = origin.ExitCode == 0
            && RepositoryCloneSource.TryNormalize(origin.Stdout, out found);
        if (hasIdentity && found == requested)
            return;
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.RepositoryMismatch,
            $"Runner checkout {repository} has origin '{(hasIdentity ? found : "<invalid origin>")}', expected '{requested}'.", 409);
    }

    private async Task ProbePushAccessAsync(string repository, string identity, CancellationToken ct)
    {
        var pushUrl = await GitAsync(repository, ct, "remote", "get-url", "--push", "origin");
        var category = PushProbeCategory.Unknown;
        if (pushUrl.ExitCode == 0 && pushUrl.Stdout.Trim().Length > 0)
        {
            // Use receive-pack to check write access. A read through the push URL also succeeds
            // for public repositories when the deploy key has no permission to push.
            // The random destination cannot overlap a task branch, and -- keeps the URL positional.
            var probeRef = "HEAD:refs/heads/antiphon-push-access-probe-" + Guid.NewGuid().ToString("N");
            var probe = await GitAsync(repository, ct, "push", "--dry-run", "--no-verify", "--porcelain", "--",
                pushUrl.Stdout.Trim(), probeRef);
            if (probe.ExitCode == 0)
                return;
            category = PushProbeOutcome.Classify(probe.ExitCode, probe.Stderr);
        }
        Serilog.Log.Warning("Runner cannot push to {RepositoryIdentity} ({PushProbeCategory})", identity, category);
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.RepositoryPushUnauthorized,
            $"Runner cannot push to {identity} ({category}): {PushProbeOutcome.Remedy(category)}", 409);
    }

    private async Task EnsureRepositoryAsync(string repository, string cloneSource, CancellationToken ct)
    {
        var dotGit = Path.Combine(repository, ".git");
        if (Directory.Exists(dotGit) || File.Exists(dotGit))
            return;
        if (File.Exists(repository))
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                $"Runner repository {repository} is a file, not a checkout; it was left untouched.", 409);
        if (Directory.Exists(repository)
            && Filesystem("inspect the runner repository", () => Directory.EnumerateFileSystemEntries(repository).Any()))
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                $"Runner repository {repository} is not empty and has no .git; it was left untouched.", 409);

        var parent = Path.GetDirectoryName(Path.GetFullPath(repository))
            ?? throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                $"Runner repository {repository} has no parent directory to clone from.", 409);
        Filesystem("create the runner repository parent", () => Directory.CreateDirectory(parent));

        var clone = await GitAsync(parent, ct, "clone", "--filter=blob:none", "--no-checkout", cloneSource, repository);
        if (clone.ExitCode != 0)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                "Runner repository clone failed: " + Tail(clone.Stderr), 409);
    }

    public async Task<PhoneHomeWorkspaceRemoveResponse> RemoveAsync(
        PhoneHomeWorkspaceRemoveRequest request, CancellationToken ct)
    {
        var path = request.Path ?? "";
        if (!IsUnderRoot(path))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Only a mirror under the worktree root may be removed.", 409);
        if (!Directory.Exists(path))
        {
            await PruneRepositoriesAsync(ct);
            return new PhoneHomeWorkspaceRemoveResponse(true, null);
        }

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
            if (request.PublishedSha is { } published)
            {
                if (!ShaPattern.IsMatch(published))
                    throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                        "Published sha must be 40 lowercase hex characters.", 409);
                var tip = await GitAsync(path, ct, "rev-parse", "HEAD");
                if (tip.ExitCode != 0 || !ShaPattern.IsMatch(tip.Stdout.Trim()))
                    throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnpublishedWork,
                        $"Mirror {path} tip could not be checked against published {published}.", 409);
                var ancestor = await GitAsync(path, ct, "merge-base", "--is-ancestor", tip.Stdout.Trim(), published);
                if (ancestor.ExitCode != 0)
                    throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnpublishedWork,
                        $"Mirror {path} has unpublished tip {tip.Stdout.Trim()} (published {published}).", 409);
            }
        }

        var repository = await OwningRepositoryAsync(path, ct);
        var remove = await GitAsync(repository, ct, "worktree", "remove", "--force", path);
        if (remove.ExitCode != 0 && Directory.Exists(path))
            return new PhoneHomeWorkspaceRemoveResponse(false, path);
        await GitAsync(repository, ct, "worktree", "prune");
        return new PhoneHomeWorkspaceRemoveResponse(!Directory.Exists(path), Directory.Exists(path) ? path : null);
    }

    /// <summary>Publish only this mirror's own branch when its tip fast-forwards the dispatch base.</summary>
    public async Task<PhoneHomeWorkspacePublishResponse> PublishAsync(
        PhoneHomeWorkspacePublishRequest request, CancellationToken ct)
    {
        var path = request.Path ?? "";
        var name = Path.GetFileName(path.TrimEnd('/', '\\'));
        if (!IsUnderRoot(path) || !NamePattern.IsMatch(name)
            || !string.Equals(request.Branch, "feat/card-task-" + name[5..], StringComparison.Ordinal)
            || !ShaPattern.IsMatch(request.BaselineSha ?? "")
            || (request.RemoteSha is not null && !ShaPattern.IsMatch(request.RemoteSha)))
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                "Publish target must be this task's mirror, branch and full commit ids.", 409);
        var resolved = TryResolveFinal(path);
        var root = TryResolveFinal(_worktreeRoot);
        if (resolved is null || root is null || !IsInside(resolved, root)
            || !string.Equals(Path.GetDirectoryName(resolved), root, StringComparison.Ordinal)
            || !Directory.Exists(resolved))
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                "Publish target is not a runner mirror.", 409);
        await OwningRepositoryAsync(path, ct);
        var symbolic = await GitAsync(path, ct, "symbolic-ref", "-q", "HEAD");
        if (symbolic.ExitCode != 0 || symbolic.Stdout.Trim() != "refs/heads/" + request.Branch)
            return new(null, "unknown", null, false, false, "not_on_branch");
        var gitDir = await GitAsync(path, ct, "rev-parse", "--path-format=absolute", "--git-dir");
        if (gitDir.ExitCode != 0)
            return new(null, "unknown", null, false, false, "inspection_unavailable");
        var directory = gitDir.Stdout.Trim();
        if (Directory.Exists(Path.Combine(directory, "rebase-merge"))
            || Directory.Exists(Path.Combine(directory, "rebase-apply"))
            || File.Exists(Path.Combine(directory, "MERGE_HEAD"))
            || File.Exists(Path.Combine(directory, "CHERRY_PICK_HEAD"))
            || File.Exists(Path.Combine(directory, "REVERT_HEAD")))
            return new(null, "unknown", null, false, false, "sequencer_active");

        var head = await GitAsync(path, ct, "rev-parse", "HEAD");
        var status = await GitAsync(path, ct, "status", "--porcelain");
        if (head.ExitCode != 0 || !ShaPattern.IsMatch(head.Stdout.Trim()) || status.ExitCode != 0)
            return new(null, "unknown", null, false, false, "inspection_unavailable");
        var tip = head.Stdout.Trim();
        var dirty = status.Stdout.Length > 0;
        var baseAncestor = await GitAsync(path, ct, "merge-base", "--is-ancestor", request.BaselineSha, tip);
        bool? descendsFromBaseline = baseAncestor.ExitCode switch { 0 => true, 1 => false, _ => null };
        var remote = request.RemoteSha ?? request.BaselineSha;
        string relation;
        if (tip == remote) relation = "equal";
        else if (descendsFromBaseline == false) relation = "diverged";
        else
        {
            var forward = await GitAsync(path, ct, "merge-base", "--is-ancestor", remote!, tip);
            if (forward.ExitCode == 0) relation = "descends";
            else if (forward.ExitCode != 1) relation = "unknown";
            else
            {
                var reverse = await GitAsync(path, ct, "merge-base", "--is-ancestor", tip, remote!);
                relation = reverse.ExitCode switch { 0 => "behind", 1 => "diverged", _ => "unknown" };
            }
        }
        if (!request.Publish || relation != "descends")
            return new(tip, relation, descendsFromBaseline, dirty, false, null);
        using var pushBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        pushBudget.CancelAfter(TimeSpan.FromSeconds(60));
        (int ExitCode, string Stdout, string Stderr) push;
        try
        {
            push = await GitAsync(path, pushBudget.Token, "push", "origin",
                "refs/heads/" + request.Branch + ":refs/heads/" + request.Branch);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(tip, relation, descendsFromBaseline, dirty, false, "push_rejected");
        }
        // Git stderr can contain a credential-bearing remote URL; keep only a stable refusal.
        return push.ExitCode == 0
            ? new(tip, relation, descendsFromBaseline, dirty, true, null)
            : new(tip, relation, descendsFromBaseline, dirty, false, "push_rejected");
    }

    internal async Task<string> OwningRepositoryAsync(string path, CancellationToken ct)
    {
        var common = await GitAsync(path, ct, "rev-parse", "--path-format=absolute", "--git-common-dir");
        if (common.ExitCode == 0 && IsOwnedCommonDirectory(common.Stdout.Trim()))
            return Path.GetDirectoryName(common.Stdout.Trim())!;
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
            $"Mirror {path} does not belong to a runner repository.", 409);
    }

    internal bool IsOwnedCommonDirectory(string common)
    {
        var directory = Path.GetDirectoryName(common);
        return directory is not null && Path.GetFileName(common) == ".git"
            && (PathsEqual(directory, _repository)
                || (PathsEqual(Path.GetDirectoryName(directory) ?? "", _policy.RepositoriesRoot)
                    && Directory.Exists(Path.Combine(directory, ".git"))));
    }

    private async Task PruneRepositoriesAsync(CancellationToken ct)
    {
        var repositories = new List<string> { _repository };
        try
        {
            if (Directory.Exists(_policy.RepositoriesRoot))
                repositories.AddRange(Directory.EnumerateDirectories(_policy.RepositoriesRoot)
                    .Where(path => Directory.Exists(Path.Combine(path, ".git"))));
        }
        catch (IOException) { /* The mirror is already absent; pruning is best effort. */ }
        catch (UnauthorizedAccessException) { /* Same: do not invent residue. */ }
        foreach (var repository in repositories.Distinct(StringComparer.Ordinal))
        {
            if (Directory.Exists(Path.Combine(repository, ".git")))
            {
                try { await GitAsync(repository, ct, "worktree", "prune"); }
                catch (PhoneHomeAdmissionException) { /* Continue other checkouts. */ }
            }
        }
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
        if (spill.MessageId is { } messageId
            && !string.Equals(relative, $".antiphon/inbox/{messageId:D}.md", StringComparison.Ordinal))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Spill path does not match its queue identity.", 409);
        // An ABSOLUTE path is refused rather than quietly rebased under the cwd: "/etc/passwd"
        // silently becoming "<mirror>/etc/passwd" would be safe but is not what the caller asked
        // for, and a spill pointer the agent is told to read must name the file that exists.
        if (relative.Length == 0
            || relative[0] == '/'
            || (relative.Length > 1 && relative[1] == ':')
            || relative.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Spill relative path is not admitted.", 409);

        // Lexical admission above does not see a symlink. The bytes have to land inside the
        // resolved mirror, or a directory link planted in that mirror writes somewhere else.
        var mirror = TryResolveFinal(runnerCwd);
        if (mirror is null || !ResolvedMirrorIsAdmitted(mirror)
            || !TryResolveContained(mirror, relative, out var full))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Spill path escapes the mirror worktree.", 409);

        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(full, spill.Body ?? "", new UTF8Encoding(false), ct);
    }

    private bool ResolvedMirrorIsAdmitted(string resolvedMirror)
    {
        var worktrees = TryResolveFinal(_worktreeRoot) ?? Path.GetFullPath(_worktreeRoot);
        if (IsInside(resolvedMirror, worktrees))
        {
            var rest = Path.GetRelativePath(worktrees, resolvedMirror);
            return rest.Length > 0
                && rest is not "."
                && !rest.Contains(Path.DirectorySeparatorChar)
                && !rest.Contains(Path.AltDirectorySeparatorChar);
        }

        var parent = Path.GetDirectoryName(_worktreeRoot);
        if (string.IsNullOrEmpty(parent))
            return false;
        var resolvedParent = TryResolveFinal(parent) ?? Path.GetFullPath(parent);
        return PathsEqual(resolvedMirror, resolvedParent);
    }

    private static bool TryResolveContained(string mirrorRoot, string relative, out string full)
    {
        var current = mirrorRoot;
        var parts = relative.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            var next = Path.Combine(current, parts[i]);
            // Exists follows a link and is false when the target is missing, so a dangling
            // symlink looks like a new file. LinkTarget sees the link itself. Writing through
            // it would create the target outside this mirror.
            if (IsSymbolicLink(next) || Directory.Exists(next) || File.Exists(next))
            {
                next = TryResolveFinal(next) ?? "";
                if (next.Length == 0 || !IsInside(next, mirrorRoot))
                {
                    full = "";
                    return false;
                }
            }
            else
            {
                next = Path.GetFullPath(next);
                if (!IsInside(next, mirrorRoot))
                {
                    full = "";
                    return false;
                }
            }

            if (i == parts.Length - 1)
            {
                full = next;
                return true;
            }

            current = next;
        }

        full = "";
        return false;
    }

    /// <summary>Follow directory and file links. Null when a link cannot be resolved.</summary>
    internal static string? TryResolveFinal(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
            return full;
        var segments = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var i = 0; i < segments.Length; i++)
        {
            var candidate = Path.Combine(current, segments[i]);
            if (IsSymbolicLink(candidate))
            {
                if (!TryResolveLinkTarget(candidate, out var resolved))
                    return null;
                current = resolved;
                continue;
            }

            FileSystemInfo? info = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : File.Exists(candidate) ? new FileInfo(candidate) : null;
            if (info is null)
                return Path.GetFullPath(Path.Combine(current, Path.Combine(segments[i..])));
            current = Path.GetFullPath(info.FullName);
        }

        return Path.GetFullPath(current);
    }

    /// <summary>
    /// True when <paramref name="path"/> is a symlink or junction. <see cref="File.Exists"/> is
    /// false for a dangling link, so it cannot answer this.
    /// </summary>
    private static bool IsSymbolicLink(string path) =>
        LinkTargetOf(new FileInfo(path)) is not null
        || LinkTargetOf(new DirectoryInfo(path)) is not null;

    private static string? LinkTargetOf(FileSystemInfo info)
    {
        try
        {
            return info.LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Follow the link to a target that exists. An unresolved link, including a dangling one,
    /// returns false so the caller refuses the write.
    /// </summary>
    private static bool TryResolveLinkTarget(string path, out string resolved)
    {
        resolved = "";
        FileSystemInfo? final = null;
        foreach (FileSystemInfo info in new FileSystemInfo[] { new DirectoryInfo(path), new FileInfo(path) })
        {
            try
            {
                var found = info.ResolveLinkTarget(returnFinalTarget: true);
                if (found is not null)
                {
                    final = found;
                    break;
                }
            }
            catch (IOException)
            {
                // A file/directory mismatch throws. The other shape may still resolve the same link.
            }
        }

        if (final is null)
            return false;
        var full = Path.GetFullPath(final.FullName);
        // The target path, not the link. Exists is false when the final target was never created.
        if (!Directory.Exists(full) && !File.Exists(full))
            return false;
        resolved = full;
        return true;
    }

    internal static bool IsInside(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(prefix, comparison);
    }

    internal static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }

    internal bool IsUnderRoot(string path)
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

    internal async Task<(int ExitCode, string Stdout, string Stderr)> GitAsync(
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

        // CARD-0631 D-8: a git that cannot start (missing binary, missing or unreadable cwd, access
        // denial, no process at all) is a named workspace admission the server can answer.
        var stage = "git " + args[0];
        Process? started;
        try
        {
            started = _startProcess(psi);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException
                                       or InvalidOperationException)
        {
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                $"{stage} could not start: {Cause(ex)}", 409);
        }
        using var process = started
            ?? throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                $"{stage} could not start: no process was created.", 409);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout or caller cancellation: either way this operation's own git child is killed
            // and reaped, and its redirected readers observed, before the operation lets go of it.
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            try { await process.WaitForExitAsync(CancellationToken.None); } catch { /* reaped or gone */ }
            try { await Task.WhenAll(stdout, stderr); } catch { /* cancelled with the operation */ }
            if (ct.IsCancellationRequested)
                throw;
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "git " + string.Join(' ', args) + " timed out.", 409);
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    /// <summary>
    /// CARD-0631 D-8: a filesystem failure while preparing the workspace is an admission naming the
    /// stage, not an escaped fault.
    /// </summary>
    private static T Filesystem<T>(string stage, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                $"Could not {stage}: {Cause(ex)}", 409);
        }
    }

    // The type always, and a bounded message: never a stack trace, environment or payload.
    private static string Cause(Exception ex)
    {
        const int MaxMessage = 200;
        var message = ex.Message.Replace('\r', ' ').Replace('\n', ' ');
        if (message.Length > MaxMessage)
            message = message[..MaxMessage] + "...";
        return ex.GetType().Name + ": " + message;
    }

    private static string Tail(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(line => line.Trim().Length > 0).ToList();
        return lines.Count == 0 ? "" : lines[^1];
    }
}
