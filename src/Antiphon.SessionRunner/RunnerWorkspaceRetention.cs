using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1677 S1. One retention request split at the dispatcher's mutation lock: <see cref="Final"/> is a
/// complete answer (kept, would_remove, already_absent or a phase-A refusal); otherwise the tree was moved
/// to <see cref="MovedPath"/> and phase B (<see cref="RunnerWorkspaceRetention.CompleteAsync"/>) removes it.
/// </summary>
public sealed record WorkspaceRetainStep(
    PhoneHomeWorkspaceRetainResponse? Final,
    string? MovedPath = null,
    string? Repository = null,
    PhoneHomeWorkspaceRetainResponse? Facts = null)
{
    public bool Moved => Final is null && MovedPath is not null;
}

/// <summary>
/// CARD-1677 S1 (plan §3.3). Runner-side recheck and removal of one FINISHED task's mirror. The server
/// decides that the task is finished; this class decides only whether the tree is safe to remove, and
/// every doubt (a git failure, an unparseable answer, an unreadable process table) keeps the tree.
///
/// The worktree root is the runner's own (<see cref="RunnerWorkspaceService.WorktreeRoot"/>), never a
/// request field; a request path must be its direct child <c>task-&lt;8 hex&gt;</c> equal to the task id.
/// It never runs <c>rm</c>, <c>git worktree prune</c>, <c>--force</c>, <c>git fetch</c> or a branch
/// deletion. Execute moves the tree into the retiring root (phase A, under the dispatcher's lock) and
/// then runs <c>git worktree remove</c> without force (phase B, outside it); Git repeats its own clean check.
/// </summary>
public sealed partial class RunnerWorkspaceRetention
{
    // \z, never $: in .NET $ also matches just before a final newline (review d0b57b9c A-3).
    private static readonly Regex LeafPattern = new(@"^task-[0-9a-f]{8}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ShaPattern = new(@"^[0-9a-f]{40}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BranchPattern = new(@"^[A-Za-z0-9._/-]{1,200}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly RunnerWorkspaceService _service;
    private readonly IWorkspaceHolderScan _holders;
    private readonly Func<string, ulong?> _deviceOf;

    public RunnerWorkspaceRetention(RunnerWorkspaceService service, IWorkspaceHolderScan holders,
        Func<string, ulong?>? deviceOf = null, string? retiringRoot = null, TimeProvider? clock = null)
    {
        _service = service;
        _holders = holders;
        _deviceOf = deviceOf ?? DeviceOf;
        _ = clock;
        // Runner-owned and never request-controlled: a sibling of the worktree root on the same volume.
        RetiringRoot = (retiringRoot ?? service.AllowedRoot + "/worktrees-retiring").Replace('\\', '/').TrimEnd('/');
    }

    public string WorktreeRoot => _service.WorktreeRoot;
    public string RetiringRoot { get; }

    /// <summary>Test seam: "checked" after the dry pass, "locked" at phase A entry before its recheck, "moved" after the move.</summary>
    internal Action<string>? PhaseCut { get; set; }

    /// <summary>Test seam: awaited with the moved path before phase B's <c>git worktree remove</c>.</summary>
    internal Func<string, Task>? PhaseBGate { get; set; }

    internal TimeSpan LsRemoteBudget { get; set; } = TimeSpan.FromSeconds(60);
    internal TimeSpan NestedCountBudget { get; set; } = TimeSpan.FromSeconds(10);
    internal TimeSpan HolderScanBudget { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Both phases back to back, without the dispatcher's lock (runner tests and tools).</summary>
    public async Task<PhoneHomeWorkspaceRetainResponse> RetainAsync(PhoneHomeWorkspaceRetainRequest request, CancellationToken ct)
    {
        var step = await CheckAndMoveAsync(request, ct);
        return step.Moved ? await CompleteAsync(step, ct) : step.Final!;
    }

    /// <summary>Steps 1-8 (DryRun) or steps 1-7 twice plus the move (Execute, phase A).</summary>
    public async Task<WorkspaceRetainStep> CheckAndMoveAsync(PhoneHomeWorkspaceRetainRequest request, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var execute = request.Mode == WorkspaceRetainModes.Execute;
        if (!execute && request.Mode != WorkspaceRetainModes.DryRun)
            return Final(Kept(WorkspaceRetentionCodes.OutsideScope, "mode must be DryRun or Execute"));
        if (!RetiringRootValid())
            return Final(Kept(WorkspaceRetentionCodes.RetiringRootInvalid, RetiringRoot));

        var path = (request.Path ?? "").Replace('\\', '/').TrimEnd('/');
        var scope = Confine(request, path);
        if (scope is not null)
            return Final(scope);

        if (!EntryExists(path))
            return await ResidueOrAbsentAsync(request, path, execute, started, ct);

        var first = await CheckAsync(request, path, countNested: true, ct);
        if (first.Final is not null || !execute)
            return first.Final is not null ? first : Final(first.Facts! with { Verdict = WorkspaceRetentionCodes.WouldRemove });
        PhaseCut?.Invoke("checked");

        // Phase A. The dispatcher holds its mutation lock around this call: the recheck below runs
        // immediately before the move, so nothing the first pass saw can have changed unobserved.
        PhaseCut?.Invoke("locked");
        var recheck = await CheckAsync(request, path, countNested: false, ct);
        if (recheck.Final is not null)
            return recheck;
        var facts = recheck.Facts! with { NestedIgnoredRepos = first.Facts!.NestedIgnoredRepos };

        try
        {
            Directory.CreateDirectory(RetiringRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Final(Kept(WorkspaceRetentionCodes.RetiringRootInvalid, ex.GetType().Name));
        }

        var rootDevice = _deviceOf(WorktreeRoot);
        var retiringDevice = _deviceOf(RetiringRoot);
        if (rootDevice is null || retiringDevice is null || rootDevice != retiringDevice)
            return Final(facts with
            {
                Verdict = WorkspaceRetentionCodes.Kept, Reason = WorkspaceRetentionCodes.RetiringRootCrossDevice,
                Detail = $"root={Hex(rootDevice)} retiring={Hex(retiringDevice)}",
            });

        var leaf = Path.GetFileName(path);
        var moved = RetiringRoot + "/" + leaf + "." + request.RequestId.ToString("N")[..8];
        if (EntryExists(moved))
            return Final(facts with { Verdict = WorkspaceRetentionCodes.Kept, Reason = WorkspaceRetentionCodes.RetiringMoveFailed, Detail = "retiring entry exists" });
        var repository = recheck.Repository!;
        var move = await Git(repository, ct, "worktree", "move", path, moved);
        if (move is null || move.Value.ExitCode != 0 || !Directory.Exists(moved) || EntryExists(path))
            return Final(facts with
            {
                Verdict = WorkspaceRetentionCodes.Kept, Reason = WorkspaceRetentionCodes.RetiringMoveFailed,
                Detail = move is null ? "git unavailable" : Tail(move.Value.Stderr),
            });
        PhaseCut?.Invoke("moved");
        var phaseA = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return new WorkspaceRetainStep(null, moved, repository, facts with { PhaseAMs = phaseA, MovedPath = moved });
    }

    /// <summary>Phase B, outside the dispatcher's lock: <c>git worktree remove</c> of the moved path, never forced.</summary>
    public async Task<PhoneHomeWorkspaceRetainResponse> CompleteAsync(WorkspaceRetainStep step, CancellationToken ct)
    {
        if (!step.Moved)
            return step.Final!;
        var started = Stopwatch.GetTimestamp();
        var moved = step.MovedPath!;
        var facts = step.Facts ?? new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept);
        if (PhaseBGate is { } gate)
            await gate(moved);
        // A secret planted after the move, or a residue handed here, must not reach `git worktree remove`.
        var ignored = await ReadIgnoredSecretAsync(moved, ct);
        if (ignored.Doubt || ignored.Secret is not null)
            return facts with
            {
                Verdict = WorkspaceRetentionCodes.Kept,
                Reason = ignored.Secret is not null ? WorkspaceRetentionCodes.IgnoredSecret : WorkspaceRetentionCodes.InspectionFailed,
                Detail = ignored.Secret is not null ? Bound(ignored.Secret) : "ignored list unavailable",
                PhaseBMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                MovedPath = moved,
            };
        var remove = await Git(step.Repository!, ct, "worktree", "remove", moved);
        var phaseB = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (remove is { ExitCode: 0 } && !EntryExists(moved))
            return facts with { Verdict = WorkspaceRetentionCodes.Removed, Reason = null, PhaseBMs = phaseB, MovedPath = moved };
        // The moved tree stays registered in the retiring root; the next request for this task retries
        // phase B for this exact path (S-5). Nothing is forced and nothing is moved back.
        return facts with
        {
            Verdict = WorkspaceRetentionCodes.RetiringRefused, Reason = WorkspaceRetentionCodes.RetiringRefused,
            PhaseBMs = phaseB, MovedPath = moved, Detail = remove is null ? "git unavailable" : Tail(remove.Value.Stderr),
        };
    }

    /// <summary>Plan §3.4. Read-only: one <c>git worktree list --porcelain</c> per runner repository, nothing else.</summary>
    public async Task<PhoneHomeWorkspaceRetentionInventoryResponse> InventoryAsync(CancellationToken ct)
    {
        var registered = new HashSet<string>(StringComparer.Ordinal);
        var complete = true;
        foreach (var repository in _service.RunnerRepositories())
        {
            var list = await Git(repository, ct, "worktree", "list", "--porcelain");
            if (list is not { ExitCode: 0 })
            {
                complete = false;
                continue;
            }
            foreach (var block in Blocks(list.Value.Stdout))
                registered.Add(block.Path);
        }

        var ledger = LedgerPaths(out var ledgerReadable);
        complete &= ledgerReadable;
        var entries = new List<WorkspaceRetentionEntry>();
        foreach (var (rootName, root) in new[] { ("worktrees", WorktreeRoot), ("retiring", RetiringRoot) })
        {
            List<string> names;
            try
            {
                names = Directory.Exists(root)
                    ? Directory.EnumerateFileSystemEntries(root).Select(entry => Path.GetFileName(entry)).OrderBy(n => n, StringComparer.Ordinal).ToList()
                    : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
                continue;
            }
            foreach (var name in names)
            {
                var full = root + "/" + name;
                var link = IsLink(full);
                DateTime? mtime = null;
                try { mtime = new FileInfo(full).LastWriteTimeUtc; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                entries.Add(new WorkspaceRetentionEntry(rootName, name, !link && Directory.Exists(full), link,
                    registered.Contains(Normalize(full)),
                    RunnerWorkspaceSlotPool.IsSlotLeaf(name) || ledger.Contains(Normalize(full)), mtime));
            }
        }

        return new PhoneHomeWorkspaceRetentionInventoryResponse(entries, complete);
    }

    // ---------------------------------------------------------------- checks

    /// <summary>Step 1 confinement, before any filesystem or git read of the tree.</summary>
    private PhoneHomeWorkspaceRetainResponse? Confine(PhoneHomeWorkspaceRetainRequest request, string path)
    {
        if (WorkspaceRetentionNames.HasControlCharacter(path))
            return Kept(WorkspaceRetentionCodes.NameControlCharacter, "path holds a control character: " + WorkspaceRetentionNames.Escape(path));
        if (!_service.IsUnderRoot(path))
            return Kept(WorkspaceRetentionCodes.OutsideScope, "not a direct child of the runner worktree root");
        var leaf = Path.GetFileName(path);
        // A slot directory is the pool's, whatever else is true of it (CARD-0824 S1 §3.6).
        if (RunnerWorkspaceSlotPool.IsSlotLeaf(leaf))
            return Kept(WorkspaceRetentionCodes.SlotManaged, leaf);
        if (!LeafPattern.IsMatch(leaf))
            return Kept(WorkspaceRetentionCodes.OutsideScope, "leaf is not task-<8 hex>");
        if (!string.Equals(leaf, "task-" + request.TaskId.ToString("N")[..8], StringComparison.Ordinal))
            return Kept(WorkspaceRetentionCodes.OutsideScope, "leaf does not name this task");
        if (!BranchPattern.IsMatch(request.Branch ?? "") || request.Branch!.Contains("..", StringComparison.Ordinal))
            return Kept(WorkspaceRetentionCodes.BranchMismatch, "branch name is not admitted");
        return null;
    }

    private async Task<WorkspaceRetainStep> CheckAsync(
        PhoneHomeWorkspaceRetainRequest request, string path, bool countNested, CancellationToken ct)
    {
        var leaf = Path.GetFileName(path);
        // Step 1: the leaf and every ancestor resolve without links to a directory directly under the root.
        var resolved = RunnerWorkspaceService.TryResolveFinal(path);
        var root = RunnerWorkspaceService.TryResolveFinal(WorktreeRoot);
        if (resolved is null || root is null || IsLink(path)
            || !RunnerWorkspaceService.PathsEqual(resolved, Path.Combine(root, leaf))
            || !Directory.Exists(resolved))
            return Final(Kept(WorkspaceRetentionCodes.OutsideScope, "the leaf is a link or not a directory under the root"));
        var ledger = LedgerPaths(out var ledgerReadable);
        if (!ledgerReadable || ledger.Contains(Normalize(path)))
            return Final(Kept(WorkspaceRetentionCodes.SlotManaged, ledgerReadable ? "ledger slot" : "slot ledger unreadable"));

        // Step 2: the optional use gate (installed by no production composition today) and its journal.
        IAsyncDisposable? lease = null;
        if (_service.WorkspaceUseGate is { } gate)
        {
            try
            {
                lease = await gate.AcquireRemovalAsync(path, path, ct);
            }
            catch (PhoneHomeAdmissionException ex)
            {
                return Final(Kept(ex.Code == WorkspaceRetentionCodes.WorkspaceInUse
                    ? WorkspaceRetentionCodes.WorkspaceInUse : WorkspaceRetentionCodes.WorkspaceRetiringOrUnknown, ex.Message));
            }
        }

        await using var held = lease;
        // Step 3: the owning repository is a runner repository and lists this exact path.
        string repository;
        try
        {
            repository = (await _service.OwningRepositoryAsync(path, ct)).Replace('\\', '/');
        }
        catch (PhoneHomeAdmissionException ex)
        {
            return Final(Kept(WorkspaceRetentionCodes.NotRunnerRepository, ex.Message));
        }
        var list = await Inspect(repository, ct, "worktree", "list", "--porcelain");
        if (list is not { ExitCode: 0 })
            return Final(Kept(WorkspaceRetentionCodes.LockStateUnknown, list is null ? "git unavailable" : Tail(list.Value.Stderr)));
        var block = Blocks(list.Value.Stdout).FirstOrDefault(b => b.Path == Normalize(path));
        if (block is null)
            return Final(Kept(WorkspaceRetentionCodes.NotRunnerRepository, "not registered in its runner repository"));
        if (block.Locked)
            return Final(Kept(WorkspaceRetentionCodes.Locked, path));

        // Step 3: HEAD is the request's branch, and that branch is this leaf's task branch.
        var symbolic = await Inspect(path, ct, "symbolic-ref", "-q", "HEAD");
        if (symbolic is not { ExitCode: 0 } || symbolic.Value.Stdout.Trim() != "refs/heads/" + request.Branch
            || !DelegateBranchName.TryParse(request.Branch, out var parsed) || parsed.Branch != request.Branch
            || !string.Equals(parsed.ShortId, leaf[5..], StringComparison.Ordinal))
            return Final(Kept(WorkspaceRetentionCodes.BranchMismatch, symbolic is { ExitCode: 0 } ? symbolic.Value.Stdout.Trim() : "detached or unreadable HEAD"));
        var head = await Inspect(path, ct, "rev-parse", "HEAD");
        if (head is not { ExitCode: 0 } || !ShaPattern.IsMatch(head.Value.Stdout.Trim()))
            return Final(Kept(WorkspaceRetentionCodes.InspectionFailed, "HEAD unreadable"));
        var headSha = head.Value.Stdout.Trim();
        var facts = new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, HeadSha: headSha);

        // Step 4: clean, untracked files included.
        var status = await Inspect(path, ct, "status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none");
        if (status is not { ExitCode: 0 })
            return Final(facts with { Reason = WorkspaceRetentionCodes.StatusUnavailable, Detail = status is null ? "git unavailable" : Tail(status.Value.Stderr) });
        if (status.Value.Stdout.Trim().Length > 0)
            return Final(facts with { Reason = WorkspaceRetentionCodes.Dirty, Detail = Bound(status.Value.Stdout) });

        // Step 5: nothing outside origin's remote-tracking refs.
        var unpushed = await Inspect(path, ct, "rev-list", "--count", "HEAD", "--not", "--remotes=origin");
        if (unpushed is not { ExitCode: 0 } || !int.TryParse(unpushed.Value.Stdout.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            return Final(facts with { Reason = WorkspaceRetentionCodes.UnpushedUnknown });
        if (count != 0)
            return Final(facts with { Reason = WorkspaceRetentionCodes.Unpushed, Detail = count.ToString(CultureInfo.InvariantCulture) });

        // Step 6: the branch on LIVE origin (not a stale tracking ref), known locally, containing HEAD.
        var endpoint = await _service.LiveOriginEndpointAsync(repository, ct);
        if (endpoint is null)
            return Final(facts with { Reason = WorkspaceRetentionCodes.OriginUnavailable, Detail = "no runner-owned origin endpoint" });
        var refName = "refs/heads/" + request.Branch;
        (int ExitCode, string Stdout, string Stderr)? remote;
        using (var budget = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            budget.CancelAfter(LsRemoteBudget);
            try
            {
                remote = await _service.InspectionGitAsync(path, budget.Token, "ls-remote", "--refs", "--", endpoint, refName);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Final(facts with { Reason = WorkspaceRetentionCodes.OriginUnavailable, Detail = "ls-remote exceeded its budget" });
            }
            catch (PhoneHomeAdmissionException ex)
            {
                return Final(facts with { Reason = WorkspaceRetentionCodes.OriginUnavailable, Detail = ex.Message });
            }
        }
        if (remote.Value.ExitCode != 0)
            return Final(facts with { Reason = WorkspaceRetentionCodes.OriginUnavailable, Detail = "ls-remote exit " + remote.Value.ExitCode.ToString(CultureInfo.InvariantCulture) });
        var tips = new List<string>();
        foreach (var line in remote.Value.Stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length != 2 || !ShaPattern.IsMatch(parts[0]))
                return Final(facts with { Reason = WorkspaceRetentionCodes.OriginUnavailable, Detail = "unparseable ls-remote line" });
            // ls-remote matches by tail: refs/heads/x/refs/heads/<b> is listed for <b>. Only the exact name counts.
            if (string.Equals(parts[1], refName, StringComparison.Ordinal))
                tips.Add(parts[0]);
        }
        if (tips.Count != 1)
            return Final(facts with { Reason = WorkspaceRetentionCodes.BranchAbsentOnOrigin });
        var tip = tips[0];
        facts = facts with { OriginTip = tip };
        var known = await Inspect(path, ct, "cat-file", "-e", tip + "^{commit}");
        if (known is not { ExitCode: 0 })
            return Final(facts with { Reason = WorkspaceRetentionCodes.OriginTipUnknown });
        var contains = await Inspect(path, ct, "merge-base", "--is-ancestor", headSha, tip);
        if (contains is not { ExitCode: 0 })
            return Final(facts with { Reason = WorkspaceRetentionCodes.OriginDoesNotContainHead });

        // Step 7: no live process with its cwd or an open descriptor under the tree; an incomplete scan keeps it.
        HolderScan scan;
        try
        {
            scan = await Task.Run(() => _holders.Scan(path), ct).WaitAsync(HolderScanBudget, ct);
        }
        catch (TimeoutException)
        {
            return Final(facts with { Reason = WorkspaceRetentionCodes.HolderScanUnknown, Detail = "holder scan exceeded its budget" });
        }
        var holders = scan.Holders.Select(h => new WorkspaceRetainHolder(h.Pid, h.Argv0, h.Kind)).ToList();
        if (holders.Count > 0)
            return Final(facts with { Reason = WorkspaceRetentionCodes.LiveProcess, Holders = holders });
        if (!scan.Complete)
            return Final(facts with { Reason = WorkspaceRetentionCodes.HolderScanUnknown });

        var ignored = await ReadIgnoredSecretAsync(path, ct);
        if (ignored.Doubt)
            return Final(facts with { Reason = WorkspaceRetentionCodes.InspectionFailed, Detail = "ignored list unavailable" });
        if (ignored.Secret is { } secret)
            return Final(facts with { Reason = WorkspaceRetentionCodes.IgnoredSecret, Detail = Bound(secret) });

        var nested = countNested ? CountNestedRepositories(path) : (int?)null;
        return new WorkspaceRetainStep(null, null, repository, facts with { Reason = null, Holders = [], NestedIgnoredRepos = nested });
    }

    /// <summary>S-5: the leaf is gone. A retiring entry of THIS leaf is retried by phase B only; else already_absent.</summary>
    private async Task<WorkspaceRetainStep> ResidueOrAbsentAsync(
        PhoneHomeWorkspaceRetainRequest request, string path, bool execute, long started, CancellationToken ct)
    {
        var leaf = Path.GetFileName(path);
        var (residue, doubt) = Residues(leaf);
        if (doubt is { } refused)
            return Final(new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, refused.Reason,
                RetiringResidue: true, Detail: refused.Detail));
        if (residue is null)
            return Final(new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.AlreadyAbsent, RetiringResidue: false));
        var facts = new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, RetiringResidue: true, MovedPath: residue);
        string repository;
        try
        {
            repository = (await _service.OwningRepositoryAsync(residue, ct)).Replace('\\', '/');
        }
        catch (PhoneHomeAdmissionException ex)
        {
            return Final(facts with { Reason = WorkspaceRetentionCodes.NotRunnerRepository, Detail = ex.Message });
        }
        HolderScan scan;
        try
        {
            scan = await Task.Run(() => _holders.Scan(residue), ct).WaitAsync(HolderScanBudget, ct);
        }
        catch (TimeoutException)
        {
            return Final(facts with { Reason = WorkspaceRetentionCodes.HolderScanUnknown });
        }
        if (scan.Holders.Count > 0)
            return Final(facts with
            {
                Reason = WorkspaceRetentionCodes.LiveProcess,
                Holders = scan.Holders.Select(h => new WorkspaceRetainHolder(h.Pid, h.Argv0, h.Kind)).ToList(),
            });
        if (!scan.Complete)
            return Final(facts with { Reason = WorkspaceRetentionCodes.HolderScanUnknown });
        var ignored = await ReadIgnoredSecretAsync(residue, ct);
        if (ignored.Doubt)
            return Final(facts with { Reason = WorkspaceRetentionCodes.InspectionFailed, Detail = "ignored list unavailable" });
        if (ignored.Secret is { } secret)
            return Final(facts with { Reason = WorkspaceRetentionCodes.IgnoredSecret, Detail = Bound(secret) });
        if (!execute)
            return Final(facts with { Verdict = WorkspaceRetentionCodes.WouldRemove });
        var phaseA = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return new WorkspaceRetainStep(null, residue, repository, facts with { PhaseAMs = phaseA });
    }

    /// <summary>
    /// The single retiring entry <c>&lt;leaf&gt;.&lt;8 hex&gt;</c> of this leaf (a real directory), or null. Any
    /// other entry that begins <c>&lt;leaf&gt;.</c> (a control character, a trailing space, upper-case hex) is a
    /// doubt, reported and kept rather than skipped: it is never taken as the residue nor read as its absence.
    /// </summary>
    private (string? Residue, (string Reason, string Detail)? Doubt) Residues(string leaf)
    {
        try
        {
            if (!Directory.Exists(RetiringRoot))
                return (null, null);
            var pattern = new Regex("^" + Regex.Escape(leaf) + @"\.[0-9a-f]{8}\z", RegexOptions.CultureInvariant);
            var names = Directory.EnumerateFileSystemEntries(RetiringRoot)
                .Select(entry => Path.GetFileName(entry))
                .Where(name => name.StartsWith(leaf + ".", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            foreach (var name in names)
            {
                if (WorkspaceRetentionNames.HasControlCharacter(name))
                    return (null, (WorkspaceRetentionCodes.NameControlCharacter,
                        "retiring entry holds a control character: " + WorkspaceRetentionNames.Escape(name)));
                if (!pattern.IsMatch(name))
                    return (null, (WorkspaceRetentionCodes.WorkspaceRetiringOrUnknown, "retiring entry is not " + leaf + ".<8 hex>: " + name));
            }
            return (names.Select(name => RetiringRoot + "/" + name)
                .FirstOrDefault(entry => !IsLink(entry) && Directory.Exists(entry)), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private bool RetiringRootValid()
    {
        if (!Path.IsPathFullyQualified(RetiringRoot) || !RetiringRoot.StartsWith('/') && !OperatingSystem.IsWindows())
            return false;
        var root = WorktreeRoot.TrimEnd('/');
        // Never the worktree root itself, never inside it, and never an ancestor of it.
        return !RunnerWorkspaceService.PathsEqual(RetiringRoot, root)
            && !RunnerWorkspaceService.IsInside(RetiringRoot, root)
            && !RunnerWorkspaceService.IsInside(root, RetiringRoot);
    }

    private HashSet<string> LedgerPaths(out bool readable)
    {
        try
        {
            var state = new RunnerWorkspaceSlotLedger(_service.AllowedRoot + "/workspace-slots").Read();
            readable = true;
            return state.Slots.Select(slot => Normalize(slot.Path)).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            readable = false;
            return [];
        }
    }

    /// <summary>
    /// CARD-1718 A-D3. Ignored paths are invisible to <c>status --porcelain</c>, and
    /// <c>git worktree remove</c> deletes them. The first ordinal secret-floor match is kept;
    /// an unreadable listing, a control character or an escaping path is a doubt and also keeps the tree.
    /// </summary>
    private async Task<(bool Doubt, string? Secret)> ReadIgnoredSecretAsync(string path, CancellationToken ct)
    {
        (int ExitCode, string Stdout, string Stderr)? listed;
        try
        {
            listed = await Inspect(path, ct, "ls-files", "--others", "--ignored", "--exclude-standard", "-z");
        }
        catch (RegexMatchTimeoutException)
        {
            return (true, null);
        }
        if (listed is not { ExitCode: 0 })
            return (true, null);
        string? secret = null;
        foreach (var raw in listed.Value.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var relative = IgnoredSecretFloor.Normalize(raw);
            if (relative.Length == 0)
                continue;
            if (WorkspaceRetentionNames.HasControlCharacter(relative) || IgnoredSecretFloor.Escapes(relative))
                return (true, null);
            try
            {
                if (IgnoredSecretFloor.Matches(relative) && (secret is null || string.CompareOrdinal(relative, secret) < 0))
                    secret = relative;
                // Git collapses an ignored nested repository to "dir/", hiding every file in it.
                // Inspect its actual descendants rather than treating that directory name as clean.
                if (relative.EndsWith('/'))
                {
                    var nested = ReadCollapsedIgnoredDirectory(path, relative, ct);
                    if (nested.Doubt)
                        return (true, null);
                    if (nested.Secret is { } found && (secret is null || string.CompareOrdinal(found, secret) < 0))
                        secret = found;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return (true, null);
            }
        }
        return (false, secret);
    }

    private static (bool Doubt, string? Secret) ReadCollapsedIgnoredDirectory(string root, string relative, CancellationToken ct)
    {
        string? secret = null;
        var started = Stopwatch.GetTimestamp();
        var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 };
        try
        {
            // Check every ancestor before entering the collapsed directory, including ignored parents.
            var directory = root;
            foreach (var segment in relative.TrimEnd('/').Split('/'))
            {
                directory = Path.Combine(directory, segment);
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    return (true, null);
            }
            var pending = new Stack<string>();
            pending.Push(directory);
            while (pending.TryPop(out directory))
            {
                ct.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(10))
                    return (true, null);
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(10))
                        return (true, null);
                    var name = IgnoredSecretFloor.Normalize(Path.GetRelativePath(root, entry));
                    if (WorkspaceRetentionNames.HasControlCharacter(name) || IgnoredSecretFloor.Escapes(name))
                        return (true, null);
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                        return (true, null);
                    if ((attributes & FileAttributes.Directory) != 0)
                        pending.Push(entry);
                    else if (IgnoredSecretFloor.Matches(name) && (secret is null || string.CompareOrdinal(name, secret) < 0))
                        secret = name;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
        {
            return (true, null);
        }
        return (false, secret);
    }

    /// <summary>CARD-1590 evidence, not a refusal: nested <c>.git</c> entries (under ignored paths, as status was clean).</summary>
    private int CountNestedRepositories(string path)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(NestedCountBudget.TotalSeconds * Stopwatch.Frequency);
        var count = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
            MatchType = MatchType.Simple,
        };
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, ".git", options))
            {
                var relative = Path.GetRelativePath(path, entry).Replace('\\', '/');
                if (relative != ".git" && !relative.Contains("/.git/", StringComparison.Ordinal) && !relative.StartsWith(".git/", StringComparison.Ordinal))
                    count++;
                if (Stopwatch.GetTimestamp() > deadline)
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return count;
    }

    // ---------------------------------------------------------------- helpers

    private Task<(int ExitCode, string Stdout, string Stderr)?> Inspect(string cwd, CancellationToken ct, params string[] args) =>
        Run(() => _service.InspectionGitAsync(cwd, ct, args));

    private Task<(int ExitCode, string Stdout, string Stderr)?> Git(string cwd, CancellationToken ct, params string[] args) =>
        Run(() => _service.GitAsync(cwd, ct, args));

    // A git that cannot start is a doubt, and a doubt keeps the tree: the caller maps null to a kept reason.
    private static async Task<(int ExitCode, string Stdout, string Stderr)?> Run(Func<Task<(int, string, string)>> git)
    {
        try
        {
            return await git();
        }
        catch (PhoneHomeAdmissionException)
        {
            return null;
        }
    }

    private sealed record WorktreeBlock(string Path, bool Locked);

    private static IEnumerable<WorktreeBlock> Blocks(string porcelain)
    {
        string? path = null;
        var locked = false;
        foreach (var raw in porcelain.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length == 0)
            {
                if (path is not null) yield return new WorktreeBlock(path, locked);
                path = null;
                locked = false;
                continue;
            }
            if (raw.StartsWith("worktree ", StringComparison.Ordinal))
                path = Normalize(raw["worktree ".Length..]);
            else if (raw == "locked" || raw.StartsWith("locked ", StringComparison.Ordinal))
                locked = true;
        }
        if (path is not null) yield return new WorktreeBlock(path, locked);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Replace('\\', '/');

    private static bool EntryExists(string path) => Directory.Exists(path) || File.Exists(path) || IsLink(path);

    private static bool IsLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable link metadata is a doubt: treat it as a link so the caller keeps the tree.
            return true;
        }
    }

    private static ulong? DeviceOf(string path)
    {
        try
        {
            var identity = RunnerWorkspaceNoFollowDelete.Identity(path);
            var device = identity.Split(':')[0];
            return ulong.TryParse(device, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Hex(ulong? value) => value is { } v ? v.ToString("x", CultureInfo.InvariantCulture) : "unknown";

    private static WorkspaceRetainStep Final(PhoneHomeWorkspaceRetainResponse response) => new(response);

    private static PhoneHomeWorkspaceRetainResponse Kept(string reason, string? detail = null) =>
        new(WorkspaceRetentionCodes.Kept, reason, Detail: detail);

    private static string Tail(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(line => line.Trim().Length > 0).ToList();
        return lines.Count == 0 ? "" : Bound(string.Join(" | ", lines.TakeLast(3)));
    }

    private static string Bound(string text) => text.Length <= 500 ? text.Trim() : text[..500].Trim() + "...";
}
