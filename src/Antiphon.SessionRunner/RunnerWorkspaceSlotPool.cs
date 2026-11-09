using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>A lease request after the runner's admission (name, sha, branch, repository) passed.</summary>
public sealed record WorkspaceSlotLeaseRequest(Guid TaskId, Guid PreparationId, string Branch, string Sha, bool Cold = false);

public sealed record WorkspaceSlotLease(string SlotId, int Generation, string Path);

/// <summary>
/// CARD-0824 S1. The runner's bounded pool of reusable workspace slots (<c>&lt;allowed&gt;/worktrees/ws-NN</c>).
///
/// A lease resets a Free slot to the exact requested sha and branch with a clean tree; only native build
/// output survives a warm reset (§3.4). A release first proves nothing is lost: no lingering process, no
/// commit outside origin (branch tip and HEAD reflog), no dirt. Otherwise the DIRECTORY is quarantined in
/// place, locked and reported, and the slot is re-homed to <c>ws-NN-dK</c> (§3.5a). The pool grows bounded
/// overflow slots when every slot is leased (§3.2a) and cold-resets only on request, schedule or pre-rc
/// (§3.12), never while leased. The ledger journals every transition before the git work it announces.
/// Resets, re-homes and overflow creation are git and filesystem work only and take no build slot.
/// </summary>
public sealed class RunnerWorkspaceSlotPool
{
    private const long GiB = 1024L * 1024 * 1024;
    private const int ErrorLimit = 4096;
    private static readonly Regex SlotLeaf = new(@"^ws-(?<n>[0-9]{2,})(-d(?<k>[0-9]+))?$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions StampJson = new(JsonSerializerDefaults.Web);

    private readonly RunnerWorkspaceService _service;
    private readonly RunnerWorkspaceSlotLedger _ledger;
    private readonly TimeProvider _clock;
    private readonly IWorkspaceSlotFileSystem _fileSystem;
    private readonly IWorkspaceSlotProcessProbe _processes;
    private readonly IWorkspaceSlotDiskProbe _disk;
    // The ledger lock bounds every read-modify-write; git work runs outside it, fenced by slot state.
    private readonly SemaphoreSlim _lock = new(1, 1);
    // Repository-level git (fetch, worktree add/remove/lock) is serialized; per-slot git is not.
    private readonly SemaphoreSlim _repository = new(1, 1);
    private readonly RunnerRepositoryActivity? _activity;
    private TaskCompletionSource _freed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RunnerWorkspaceSlotPool(
        RunnerWorkspaceService service, PhoneHomeWorkspaceSlotSettings settings, int capacity,
        TimeProvider? clock = null, IWorkspaceSlotFileSystem? fileSystem = null,
        IWorkspaceSlotProcessProbe? processes = null, IWorkspaceSlotDiskProbe? disk = null,
        RunnerRepositoryActivity? activity = null)
    {
        _service = service;
        Settings = settings;
        Capacity = Math.Max(1, capacity);
        _clock = clock ?? TimeProvider.System;
        _fileSystem = fileSystem ?? new PhysicalWorkspaceSlotFileSystem();
        _processes = processes ?? new ProcWorkspaceSlotProcessProbe();
        _disk = disk ?? new PhysicalWorkspaceSlotDiskProbe();
        _activity = activity;
        _ledger = new RunnerWorkspaceSlotLedger(service.AllowedRoot + "/workspace-slots");
    }

    public PhoneHomeWorkspaceSlotSettings Settings { get; }
    public int Capacity { get; }
    public int Target => Capacity + Math.Max(0, Settings.Headroom);
    public string LedgerRoot => _ledger.StateRoot;
    public string RetainedRoot => _service.AllowedRoot + "/retained";

    /// <summary>Test seam: named phase cuts (enter, picked, leasing, leased, releasing, before-clean, free, ...).</summary>
    internal Action<string>? PhaseCut { get; set; }

    /// <summary>Test seam: invoked when a lease starts waiting for a Releasing slot (after its timer exists).</summary>
    internal Action? WaitingForRelease { get; set; }

    public WorkspaceSlotLedgerState Ledger() => _ledger.Read();

    /// <summary>True when a path names a slot directory (<c>ws-NN</c> or <c>ws-NN-dK</c>).</summary>
    public static bool IsSlotLeaf(string? leaf) => leaf is not null && SlotLeaf.IsMatch(leaf);

    internal WorkspaceSlotRecord? SlotByPath(string path)
    {
        var normalized = Normalize(path);
        return _ledger.Read().Slots.FirstOrDefault(slot => Normalize(slot.Path) == normalized);
    }

    // ---------------------------------------------------------------- lease

    public async Task<WorkspaceSlotLease> LeaseAsync(WorkspaceSlotLeaseRequest request, CancellationToken ct)
    {
        PhaseCut?.Invoke("enter");
        var current = _ledger.Read();
        // D-A replay: a lost reply resends the same preparation and gets the same slot back.
        var replay = current.Slots.FirstOrDefault(slot => slot.State == WorkspaceSlotStates.Leased
            && slot.TaskId == request.TaskId && slot.PreparationId == request.PreparationId);
        if (replay is not null && Directory.Exists(replay.Path))
            return new WorkspaceSlotLease(replay.Id, replay.LeaseGeneration, replay.Path);
        if (current.QuarantinedDirectories.Count(entry => entry.IsOpen) > Settings.MaxQuarantinedDirectories)
            throw Refuse(WorkspaceSlotCodes.PoolDegraded);
        if (BranchCheckedOut(request.Branch, except: null))
            throw Refuse(WorkspaceSlotCodes.BranchCheckedOut + ": " + request.Branch + " is checked out in another worktree.");
        await FetchExactAsync(request.Branch, request.Sha, ct);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (slot, created) = await PickAsync(request, ct);
            PhaseCut?.Invoke("leasing");
            if (created)
            {
                var add = await RepositoryGitAsync(ct, "worktree", "add", "--detach", slot.Path, request.Sha);
                if (add.ExitCode != 0 || !await StampDirectoryAsync(slot.Path, slot.Id, slot.DirectoryGeneration, ct))
                {
                    await MutateAsync(state => Fail(state, slot.Id, "creation failed: " + Bound(add.Stderr)));
                    continue;
                }
            }
            else if (await PreflightFreeAsync(slot, ct) is { } failure)
            {
                await QuarantineAndRehomeAsync(slot, failure.Class, failure.Reason, failure.Detail, ct);
                continue;
            }

            var cold = request.Cold || slot.ColdResetPending is not null;
            var reset = await ResetForLeaseAsync(slot, request, cold, ct);
            var verify = reset ?? await VerifyLeaseAsync(slot.Path, request, ct);
            if (verify is null && ProveOwnership(slot).Failure is { } broken)
                verify = broken.Detail;
            if (verify is not null)
            {
                await QuarantineAndRehomeAsync(slot, "filesystem", reset is null ? "verify" : "reset", verify, ct);
                continue;
            }

            StampLease(slot, request);
            await MutateAsync(state =>
            {
                var record = state.Slot(slot.Id)!;
                record.State = WorkspaceSlotStates.Leased;
                record.LeasedAt = _clock.GetUtcNow();
                record.LastResetKind = cold ? "cold" : "warm";
                if (cold)
                {
                    record.LastColdReset = record.ColdResetPending is { } pending
                        ? Stamp(pending)
                        : new WorkspaceSlotColdResetRecord { RequestId = "lease:" + request.PreparationId.ToString("D"), Requester = "dispatch", Reason = "cold lease", At = _clock.GetUtcNow() };
                    record.ColdResetPending = null;
                }
            });
            PhaseCut?.Invoke("leased");
            return new WorkspaceSlotLease(slot.Id, slot.LeaseGeneration, slot.Path);
        }

        throw Refuse(WorkspaceSlotCodes.Unavailable + ": no slot passed verification.");
    }

    private async Task<(WorkspaceSlotRecord Slot, bool Created)> PickAsync(WorkspaceSlotLeaseRequest request, CancellationToken ct)
    {
        var waited = false;
        while (true)
        {
            string? refusal = null;
            Task? freed = null;
            await _lock.WaitAsync(ct);
            try
            {
                var state = _ledger.Read();
                var created = false;
                var picked = state.Slots.Where(slot => slot.State == WorkspaceSlotStates.Free)
                    .OrderBy(slot => slot.Number).FirstOrDefault();
                if (picked is null)
                {
                    var active = state.Slots.Where(slot => WorkspaceSlotStates.IsActive(slot.State)).ToList();
                    if (!waited && active.Any(slot => slot.State is WorkspaceSlotStates.Releasing or WorkspaceSlotStates.ColdResetting))
                        freed = _freed.Task;
                    else if (active.Count < Target)
                        (picked, created) = (NewSlot(state, "base", request.TaskId), true);
                    else if (active.Count - Target >= Settings.MaxOverflowSlots)
                        refusal = WorkspaceSlotCodes.OverflowCap;
                    else if (!DiskAllowsOverflow(state))
                        refusal = WorkspaceSlotCodes.DiskBudget;
                    else
                    {
                        (picked, created) = (NewSlot(state, "overflow", request.TaskId), true);
                        Attention(state, WorkspaceSlotCodes.Overflow, picked.Id, picked.Path, "created for task " + request.TaskId.ToString("D"));
                    }
                }

                if (picked is not null)
                {
                    PhaseCut?.Invoke("picked");
                    picked.State = WorkspaceSlotStates.Leasing;
                    picked.TaskId = request.TaskId;
                    picked.PreparationId = request.PreparationId;
                    picked.Branch = request.Branch;
                    picked.Sha = request.Sha;
                    picked.Cold = request.Cold;
                    picked.LeaseGeneration++;
                    picked.LingerAttempts = 0;
                    picked.NextLingerAt = null;
                    picked.ReleaseRequested = false;
                    picked.Detail = null;
                    _ledger.Write(state);
                    return (Clone(picked), created);
                }
            }
            finally
            {
                _lock.Release();
            }

            if (refusal is not null)
                throw Refuse(WorkspaceSlotCodes.UnavailableDetail(refusal));
            waited = true;
            var delay = Task.Delay(TimeSpan.FromSeconds(Math.Max(0, Settings.OverflowWaitSeconds)), _clock, ct);
            WaitingForRelease?.Invoke();
            await Task.WhenAny(freed!, delay);
        }
    }

    private WorkspaceSlotRecord NewSlot(WorkspaceSlotLedgerState state, string createdAs, Guid task)
    {
        // A number is never reused while the ledger or any directory generation still knows it.
        var onDisk = Directory.Exists(_service.WorktreeRoot)
            ? Directory.EnumerateDirectories(_service.WorktreeRoot).Select(Path.GetFileName)
                .Select(leaf => SlotLeaf.Match(leaf ?? "")).Where(match => match.Success)
                .Select(match => int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture))
            : [];
        var number = state.Slots.Select(slot => slot.Number).Concat(onDisk).DefaultIfEmpty(0).Max() + 1;
        var id = "ws-" + number.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        var slot = new WorkspaceSlotRecord
        {
            Id = id,
            Number = number,
            Path = _service.WorktreeRoot + "/" + id,
            DirectoryGeneration = 1,
            CreatedAs = createdAs,
            CreatedByTask = task,
            CreatedAt = _clock.GetUtcNow(),
            IdleSince = _clock.GetUtcNow(),
        };
        state.Slots.Add(slot);
        return slot;
    }

    private bool DiskAllowsOverflow(WorkspaceSlotLedgerState state)
    {
        var estimate = Settings.SlotBytesEstimateGiB * GiB;
        var (free, total) = _disk.Volume(_service.WorktreeRoot);
        if (free < Math.Max(20 * GiB, total / 10) + 2 * estimate)
            return false;
        var pool = state.Slots.Where(slot => WorkspaceSlotStates.IsActive(slot.State)).Sum(slot => _disk.Measure(slot.Path))
                   + state.QuarantinedDirectories.Where(entry => entry.IsOpen).Sum(entry => _disk.Measure(entry.Path));
        return pool + estimate <= Settings.PoolDiskBudgetGiB * GiB;
    }

    private async Task<string?> ResetForLeaseAsync(WorkspaceSlotRecord slot, WorkspaceSlotLeaseRequest request, bool cold, CancellationToken ct)
    {
        // D-1: nothing destructive runs until this directory is proven to be this slot's own worktree.
        var (owned, broken) = ProveOwnership(slot);
        if (owned is null)
            return "identity: " + broken!.Detail;
        var path = owned.Path;
        await AbortSequencersAsync(path, ct);
        var checkout = await _service.GitAsync(path, ct, "checkout", "--force", "-B", request.Branch, request.Sha);
        if (checkout.ExitCode != 0)
            return "checkout: " + Bound(checkout.Stderr);
        var clean = await _service.GitAsync(path, ct, [.. WorkspaceSlotResetPolicy.CleanArguments(cold)]);
        if (clean.ExitCode != 0)
            return "clean: " + Bound(clean.Stderr);
        await ClearWorktreeStateAsync(owned, ct);
        return null;
    }

    private async Task<string?> VerifyLeaseAsync(string path, WorkspaceSlotLeaseRequest request, CancellationToken ct)
    {
        var head = await _service.GitAsync(path, ct, "rev-parse", "HEAD");
        if (head.ExitCode != 0 || head.Stdout.Trim() != request.Sha)
            return "HEAD is " + head.Stdout.Trim() + ", not " + request.Sha;
        var symbolic = await _service.GitAsync(path, ct, "symbolic-ref", "-q", "HEAD");
        if (symbolic.ExitCode != 0 || symbolic.Stdout.Trim() != "refs/heads/" + request.Branch)
            return "HEAD is not on refs/heads/" + request.Branch;
        var status = await _service.GitAsync(path, ct, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        if (status.ExitCode != 0 || status.Stdout.Length > 0)
            return "tree is not clean: " + Bound(status.Stdout.Replace('\0', ' '));
        if (await GitDirectoryAsync(path, ct) is not { } gitDir)
            return "git directory unreadable";
        if (MarkerIn(gitDir) is { } marker)
            return "sequencer or lock marker " + marker;
        return null;
    }

    private void StampLease(WorkspaceSlotRecord slot, WorkspaceSlotLeaseRequest request)
    {
        var owned = ProveOwnership(slot).Owned ?? throw Refuse("Slot " + slot.Id + " lost its ownership proof.");
        WriteStamp(owned.AdminDir, slot.Id, slot.DirectoryGeneration, slot.LeaseGeneration, request.TaskId, request.PreparationId);
    }

    /// <summary>D-1: a directory the pool just created carries its owner stamp before any later reset proves ownership.</summary>
    private async Task<bool> StampDirectoryAsync(string path, string slotId, int directoryGeneration, CancellationToken ct)
    {
        if (await GitDirectoryAsync(path, ct) is not { } gitDir)
            return false;
        try
        {
            WriteStamp(gitDir, slotId, directoryGeneration, 0, null, null);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void WriteStamp(string gitDir, string slotId, int directoryGeneration, int leaseGeneration, Guid? task, Guid? preparation)
    {
        // D-7: the existing owner stamp name keeps the CARD-1008 recycle audit allowlist unchanged.
        var path = RunnerWorkspaceBindingStore.OwnerStampPath(gitDir);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new
        {
            slotId,
            leaseGeneration,
            directoryGeneration,
            taskId = task,
            preparationId = preparation,
        }, StampJson));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    // ---------------------------------------------------------------- release

    public async Task<PhoneHomeWorkspaceSlotReleaseResponse> ReleaseAsync(
        PhoneHomeWorkspaceSlotReleaseRequest request, CancellationToken ct)
    {
        string? immediate = null;
        await _lock.WaitAsync(ct);
        try
        {
            var state = _ledger.Read();
            var slot = state.Slot(request.SlotId) ?? throw Refuse(WorkspaceSlotCodes.UnknownSlot);
            if (slot.State is WorkspaceSlotStates.Free or WorkspaceSlotStates.Retired or WorkspaceSlotStates.Failed
                && slot.LeaseGeneration == request.Generation && slot.LastTaskId == request.TaskId)
                return new PhoneHomeWorkspaceSlotReleaseResponse("already_free", slot.Id, slot.LeaseGeneration);
            if (slot.LeaseGeneration != request.Generation)
                throw Refuse(WorkspaceSlotCodes.GenerationMismatch);
            if (slot.TaskId != request.TaskId)
                throw Refuse(WorkspaceSlotCodes.TaskMismatch);
            if (slot.State == WorkspaceSlotStates.Releasing)
                return new PhoneHomeWorkspaceSlotReleaseResponse("deferred", slot.Id, slot.LeaseGeneration, "release in progress");
            if (slot.State != WorkspaceSlotStates.Leased)
                throw Refuse("Slot " + slot.Id + " is " + slot.State + ", not leased.");
            if (slot.ReleaseRequested && slot.NextLingerAt > _clock.GetUtcNow())
                immediate = slot.LingerAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture);
            else
            {
                slot.ReleaseRequested = true;
                _ledger.Write(state);
            }
        }
        finally
        {
            _lock.Release();
        }

        if (immediate is not null)
            return new PhoneHomeWorkspaceSlotReleaseResponse("deferred", request.SlotId, request.Generation, immediate);
        return await SettleAsync(request.SlotId, allowDefer: true, ct);
    }

    /// <summary>
    /// The release and crash-recovery check (§3.5). Only a slot with no holder, no unpushed commit and no
    /// dirt is reset; anything else is quarantined in place and the slot re-homes.
    /// </summary>
    private async Task<PhoneHomeWorkspaceSlotReleaseResponse> SettleAsync(string slotId, bool allowDefer, CancellationToken ct)
    {
        var slot = await MutateAsync(state =>
        {
            var record = state.Slot(slotId)!;
            record.State = WorkspaceSlotStates.Releasing;
            return Clone(record);
        });
        PhaseCut?.Invoke("releasing");
        var quarantined = new PhoneHomeWorkspaceSlotReleaseResponse("quarantined", slot.Id, slot.LeaseGeneration);

        // D-1: a directory that is not provably this slot's own worktree is quarantined before any git runs in it.
        if (ProveOwnership(slot).Failure is { } broken)
        {
            await QuarantineAndRehomeAsync(slot, broken.Class, broken.Reason, broken.Detail, ct);
            return quarantined;
        }

        var holders = _processes.Holders(slot.Path);
        if (holders.Count > 0)
        {
            var attempts = slot.LingerAttempts + 1;
            var detail = string.Join("; ", holders.Select(holder => $"pid={holder.Pid} argv0={holder.Argv0} kind={holder.Kind}"));
            if (!allowDefer || attempts > Settings.LingerRetries)
            {
                await QuarantineAndRehomeAsync(slot, "custody", "lingering", detail, ct);
                return quarantined;
            }

            await MutateAsync(state =>
            {
                var record = state.Slot(slotId)!;
                record.State = WorkspaceSlotStates.Leased;
                record.LingerAttempts = attempts;
                record.NextLingerAt = _clock.GetUtcNow() + Backoff(attempts);
                record.Detail = detail;
            });
            return new PhoneHomeWorkspaceSlotReleaseResponse("deferred", slot.Id, slot.LeaseGeneration,
                attempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (await UnpushedAsync(slot.Path, slot.Branch, ct) is { } unpushed)
        {
            await QuarantineAndRehomeAsync(slot, "custody", "unpushed", unpushed, ct);
            return quarantined;
        }

        if (await DirtAsync(slot.Path, ct) is { } dirt)
        {
            await QuarantineAndRehomeAsync(slot, "custody", "dirty", dirt, ct);
            return quarantined;
        }

        try
        {
            if (slot.TaskId is { } task)
                RetainEvidence(slot, task);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await QuarantineAndRehomeAsync(slot, "filesystem", "evidence", ex.GetType().Name + ": " + ex.Message, ct);
            return quarantined;
        }

        PhaseCut?.Invoke("before-clean");
        var cold = slot.ColdResetPending is not null;
        var published = await PublishedShaAsync(ct);
        var error = await ResetForFreeAsync(slot, published, cold, ct);
        if (error is null && await CheckFreeAsync(slot, ct) is not null)
        {
            // One retry for a residue the first pass left; a second failure is an undeletable residue.
            error = await ResetForFreeAsync(slot, published, cold, ct);
            if (error is null && await CheckFreeAsync(slot, ct) is { } residue)
                error = "residue: " + residue.Detail;
        }

        if (error is not null)
        {
            await QuarantineAndRehomeAsync(slot, "filesystem", "reset", error, ct);
            return quarantined;
        }

        PhaseCut?.Invoke("free");
        await MutateAsync(state =>
        {
            var record = state.Slot(slotId)!;
            MarkFree(record);
            record.LastResetKind = cold ? "cold" : "warm";
            if (cold && record.ColdResetPending is { } pending)
            {
                record.LastColdReset = Stamp(pending);
                record.ColdResetPending = null;
            }
        });
        SignalFreed();
        return new PhoneHomeWorkspaceSlotReleaseResponse("released", slot.Id, slot.LeaseGeneration);
    }

    private static TimeSpan Backoff(int attempt) => attempt switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(4),
        _ => TimeSpan.FromMinutes(5),
    };

    private void MarkFree(WorkspaceSlotRecord record)
    {
        record.State = WorkspaceSlotStates.Free;
        record.IdleSince = _clock.GetUtcNow();
        if (record.TaskId is { } task)
            record.LastTaskId = task;
        record.TaskId = null;
        record.PreparationId = null;
        record.Branch = null;
        record.Sha = null;
        record.Cold = false;
        record.LingerAttempts = 0;
        record.NextLingerAt = null;
        record.ReleaseRequested = false;
        record.Detail = null;
    }

    private async Task<string?> ResetForFreeAsync(WorkspaceSlotRecord slot, string published, bool cold, CancellationToken ct)
    {
        // D-1: re-proved here, immediately before the first destructive command, not only at the preflight.
        var (owned, broken) = ProveOwnership(slot);
        if (owned is null)
            return "identity: " + broken!.Detail;
        var path = owned.Path;
        var branch = slot.Branch;
        await AbortSequencersAsync(path, ct);
        var checkout = await _service.GitAsync(path, ct, "checkout", "--force", "--detach", published);
        if (checkout.ExitCode != 0)
            return "checkout: " + Bound(checkout.Stderr);
        var clean = await _service.GitAsync(path, ct, [.. WorkspaceSlotResetPolicy.CleanArguments(cold)]);
        if (clean.ExitCode != 0)
            return "clean: " + Bound(clean.Stderr);
        await ClearWorktreeStateAsync(owned, ct);
        if (branch is not null && branch != _service.PublishedBranch)
        {
            // Runner-local task refs go with the release; the unpushed check already ran.
            await _service.GitAsync(path, ct, "update-ref", "-d", "refs/heads/" + branch);
            await _service.GitAsync(path, ct, "update-ref", "-d", "refs/remotes/origin/" + branch);
        }

        DeleteFile(owned.AdminDir + "/logs/HEAD");
        return null;
    }

    private async Task AbortSequencersAsync(string path, CancellationToken ct)
    {
        foreach (var verb in new[] { "rebase", "cherry-pick", "revert", "merge" })
            await _service.GitAsync(path, ct, verb, "--abort");
        foreach (var verb in new[] { "rebase", "cherry-pick", "revert", "merge" })
            await _service.GitAsync(path, ct, verb, "--quit");
    }

    private async Task ClearWorktreeStateAsync(Owned owned, CancellationToken ct)
    {
        var refs = await _service.GitAsync(owned.Path, ct, "for-each-ref", "--format=%(refname)", "refs/worktree/", "refs/bisect/");
        if (refs.ExitCode == 0)
        {
            foreach (var name in refs.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                await _service.GitAsync(owned.Path, ct, "update-ref", "-d", name);
        }

        foreach (var name in new[] { "config.worktree", "ORIG_HEAD", "AUTO_MERGE", "FETCH_HEAD", "BISECT_LOG", "BISECT_START", "BISECT_NAMES", "BISECT_EXPECTED_REV", "BISECT_TERMS", "BISECT_ANCESTORS_OK" })
            DeleteFile(owned.AdminDir + "/" + name);
    }

    // ---------------------------------------------------------------- checks

    private sealed record Failure(string Class, string Reason, string Detail);

    /// <summary>A slot directory proven to be this slot's own worktree, and its registration (admin) directory.</summary>
    private sealed record Owned(string Path, string AdminDir);

    /// <summary>
    /// D-1 (review 3a354688). Read-only proof, required before any checkout, clean, update-ref or file delete,
    /// that the directory IS this slot's own worktree: it is <c>&lt;worktrees&gt;/&lt;this slot's leaf&gt;</c> by canonical
    /// path, with no link at the directory, its <c>.git</c> file or the registration; its <c>.git</c> file names a
    /// registration directly under <c>&lt;primary&gt;/.git/worktrees</c> (never the primary <c>.git</c>); that
    /// registration's <c>gitdir</c> back-pointer names this directory's <c>.git</c> (never a sibling's); and the
    /// pool's owner stamp there names this slot id and directory generation. Any doubt is a failure.
    /// </summary>
    private (Owned? Owned, Failure? Failure) ProveOwnership(WorkspaceSlotRecord slot)
    {
        static (Owned?, Failure?) Broken(string detail) => (null, new Failure("filesystem", "identity", detail));
        try
        {
            var path = Normalize(slot.Path);
            var leaf = Path.GetFileName(path);
            if (!IsSlotLeaf(leaf) || (leaf != slot.Id && !leaf.StartsWith(slot.Id + "-d", StringComparison.Ordinal))
                || !RunnerWorkspaceService.PathsEqual(Path.GetDirectoryName(path) ?? "", _service.WorktreeRoot))
                return Broken("slot path " + path + " is not " + slot.Id + " under the slot root");
            if (!Path.Exists(path))
                return Broken("slot directory is missing");
            if (IsLink(path) || !Directory.Exists(path)
                || RunnerWorkspaceService.TryResolveFinal(path) is not { } realPath
                || RunnerWorkspaceService.TryResolveFinal(_service.WorktreeRoot) is not { } realRoot
                || !RunnerWorkspaceService.PathsEqual(realPath, Path.Combine(realRoot, leaf)))
                return Broken("slot directory " + path + " is a link or resolves outside the slot root");
            var dotGit = path + "/.git";
            if (!Path.Exists(dotGit) || Directory.Exists(dotGit))
                return Broken(".git file is missing");
            if (IsLink(dotGit))
                return Broken(".git is a link");
            var pointer = File.ReadAllText(dotGit).Trim();
            if (!pointer.StartsWith("gitdir:", StringComparison.Ordinal))
                return Broken(".git is not a gitdir pointer");
            var target = pointer["gitdir:".Length..].Trim();
            var admin = Normalize(Path.IsPathRooted(target) ? target : Path.Combine(path, target));
            var worktrees = _service.PrimaryRepository + "/.git/worktrees";
            if (!Directory.Exists(admin) || IsLink(admin) || IsLink(_service.PrimaryRepository + "/.git") || IsLink(worktrees)
                || RunnerWorkspaceService.TryResolveFinal(admin) is not { } realAdmin
                || RunnerWorkspaceService.TryResolveFinal(worktrees) is not { } realWorktrees
                || !RunnerWorkspaceService.PathsEqual(Path.GetDirectoryName(realAdmin) ?? "", realWorktrees))
                return Broken("git directory " + admin + " is not a registration under " + Normalize(worktrees));
            var back = admin + "/gitdir";
            if (!File.Exists(back) || IsLink(back))
                return Broken("registration " + admin + " has no gitdir back-pointer");
            var backTarget = File.ReadAllText(back).Trim();
            if (RunnerWorkspaceService.TryResolveFinal(Path.IsPathRooted(backTarget) ? backTarget : Path.Combine(admin, backTarget)) is not { } realBack
                || RunnerWorkspaceService.TryResolveFinal(dotGit) is not { } realDotGit
                || !RunnerWorkspaceService.PathsEqual(realBack, realDotGit))
                return Broken("registration " + admin + " belongs to " + backTarget + ", not " + dotGit);
            var stamp = RunnerWorkspaceBindingStore.OwnerStampPath(admin);
            if (!File.Exists(stamp) || IsLink(stamp))
                return Broken("registration " + admin + " has no owner stamp");
            using var json = JsonDocument.Parse(File.ReadAllText(stamp));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("slotId", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != slot.Id
                || !root.TryGetProperty("directoryGeneration", out var generation) || generation.ValueKind != JsonValueKind.Number
                || !generation.TryGetInt32(out var stamped) || stamped != slot.DirectoryGeneration)
                return Broken("owner stamp " + stamp + " does not name " + slot.Id + " directory generation " + slot.DirectoryGeneration);
            return (new Owned(path, Normalize(realAdmin)), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return Broken("ownership unreadable: " + ex.GetType().Name + ": " + Bound(ex.Message));
        }
    }

    // A dangling or unreadable entry throws and the caller fails closed; LinkTarget alone can return null on EACCES.
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// D-1/D-2 read-only preflight before any destructive work on a Free slot (lease, cold reset, retirement):
    /// the ownership proof and free invariant, then no commit outside origin in the slot's HEAD reflog.
    /// </summary>
    private async Task<Failure?> PreflightFreeAsync(WorkspaceSlotRecord slot, CancellationToken ct)
    {
        if (await CheckFreeAsync(slot, ct) is { } failure)
            return failure;
        return await UnpushedAsync(slot.Path, null, ct, fetch: false) is { } unpushed
            ? new Failure("custody", "unpushed", unpushed)
            : null;
    }

    /// <summary>The free invariant (§3.4): ownership, detached at a published sha, only keep-list residue, no markers, no holder.</summary>
    private async Task<Failure?> CheckFreeAsync(WorkspaceSlotRecord slot, CancellationToken ct)
    {
        var path = slot.Path;
        var (owned, broken) = ProveOwnership(slot);
        if (owned is null)
            return broken;
        var gitDir = owned.AdminDir;
        if ((await _service.GitAsync(path, ct, "symbolic-ref", "-q", "HEAD")).ExitCode == 0)
            return new Failure("custody", "attached", "HEAD is on a branch");
        var contained = await _service.GitAsync(path, ct, "for-each-ref", "--count=1", "--contains", "HEAD", "--format=%(refname)", "refs/remotes/origin/");
        if (contained.ExitCode != 0 || contained.Stdout.Trim().Length == 0)
            return new Failure("custody", "unpublished_head", "HEAD is not contained in an origin head");
        var status = await _service.GitAsync(path, ct, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignored=matching");
        if (status.ExitCode != 0)
            return new Failure("filesystem", "status", Bound(status.Stderr));
        var foreign = StatusEntries(status.Stdout)
            .Where(entry => !(entry.Code == "!!" && WorkspaceSlotResetPolicy.IsKeepListPath(entry.Path))).ToList();
        if (foreign.Count > 0)
            return new Failure("custody", "dirty", "paths outside the keep-list: " + Bound(string.Join(", ", foreign.Select(entry => entry.Code + " " + entry.Path))));
        if (MarkerIn(gitDir) is { } marker)
            return new Failure("filesystem", "marker", marker);
        if (File.Exists(gitDir + "/config.worktree"))
            return new Failure("filesystem", "marker", "config.worktree");
        var holders = _processes.Holders(path);
        if (holders.Count > 0)
            return new Failure("custody", "lingering", string.Join("; ", holders.Select(holder => $"pid={holder.Pid} argv0={holder.Argv0} kind={holder.Kind}")));
        return null;
    }

    private static IEnumerable<(string Code, string Path)> StatusEntries(string porcelainZ)
    {
        var parts = porcelainZ.Split('\0');
        for (var i = 0; i < parts.Length; i++)
        {
            var entry = parts[i];
            if (entry.Length < 4)
                continue;
            var code = entry[..2];
            yield return (code, entry[3..]);
            if (code[0] is 'R' or 'C')
                i++;
        }
    }

    private static string? MarkerIn(string gitDir)
    {
        foreach (var marker in WorkspaceSlotResetPolicy.SequencerMarkers.Append("index.lock"))
        {
            if (File.Exists(gitDir + "/" + marker) || Directory.Exists(gitDir + "/" + marker))
                return marker;
        }

        return null;
    }

    /// <summary>
    /// §3.5. Every commit the lease could have produced (the HEAD reflog since the lease, HEAD and the task
    /// branch tip) must be reachable from an origin head. The reflog is expired at every release, so its
    /// entries all belong to this lease.
    /// </summary>
    private async Task<string?> UnpushedAsync(string path, string? branch, CancellationToken ct, bool fetch = true)
    {
        // Without a fetch the origin heads are older, never newer: the check can only grow stricter.
        if (fetch && branch is not null)
            await RepositoryGitAsync(ct, "fetch", "origin", "+refs/heads/" + branch + ":refs/remotes/origin/" + branch);
        if (fetch)
            await PublishedShaAsync(ct);
        var shas = new HashSet<string>(StringComparer.Ordinal);
        var reflog = await _service.GitAsync(path, ct, "reflog", "show", "--format=%H", "HEAD");
        if (reflog.ExitCode == 0)
            shas.UnionWith(reflog.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var head = await _service.GitAsync(path, ct, "rev-parse", "HEAD");
        if (head.ExitCode == 0)
            shas.Add(head.Stdout.Trim());
        if (branch is not null)
        {
            var tip = await _service.GitAsync(path, ct, "rev-parse", "-q", "--verify", "refs/heads/" + branch);
            if (tip.ExitCode == 0)
                shas.Add(tip.Stdout.Trim());
        }

        shas.RemoveWhere(sha => sha.Length == 0);
        if (shas.Count == 0)
            return null;
        var missing = await _service.GitAsync(path, ct, ["rev-list", "--max-count=1", .. shas.Order(StringComparer.Ordinal), "--not", "--remotes=origin"]);
        if (missing.ExitCode != 0)
            return "unpushed check failed: " + Bound(missing.Stderr);
        var commit = missing.Stdout.Trim();
        return commit.Length == 0 ? null : "commit " + commit + " is not on origin";
    }

    private async Task<string?> DirtAsync(string path, CancellationToken ct)
    {
        var status = await _service.GitAsync(path, ct, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        if (status.ExitCode != 0)
            return "status failed: " + Bound(status.Stderr);
        return status.Stdout.Length == 0 ? null : Bound(status.Stdout.Replace('\0', ' ').Trim());
    }

    private bool BranchCheckedOut(string branch, string? except)
    {
        var common = _service.PrimaryRepository + "/.git";
        var target = "ref: refs/heads/" + branch;
        var heads = new List<string> { common + "/HEAD" };
        if (Directory.Exists(common + "/worktrees"))
            heads.AddRange(Directory.EnumerateDirectories(common + "/worktrees").Select(directory => directory + "/HEAD"));
        foreach (var head in heads)
        {
            try
            {
                if (File.Exists(head) && File.ReadAllText(head).Trim() == target
                    && (except is null || Normalize(Path.GetDirectoryName(head)!) != Normalize(except)))
                    return true;
            }
            catch (IOException)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- evidence

    private void RetainEvidence(WorkspaceSlotRecord slot, Guid task)
    {
        var source = slot.Path + "/.antiphon";
        if (!Directory.Exists(source))
            return;
        var taskRoot = RetainedRoot + "/" + task.ToString("N");
        var destination = taskRoot + "/" + slot.LeaseGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var used = Directory.Exists(taskRoot)
            ? Directory.EnumerateFiles(taskRoot, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
            : 0;
        var truncated = false;
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(slot.Path, file).Replace('\\', '/'))
            .Where(relative => relative.EndsWith(".md", StringComparison.Ordinal)
                               || Regex.IsMatch(relative, @"^\.antiphon/checkpoints/[^/]+/report\.json$"))
            .Order(StringComparer.Ordinal);
        foreach (var relative in files)
        {
            var length = new FileInfo(slot.Path + "/" + relative).Length;
            if (used + length > Settings.EvidenceCapBytes)
            {
                truncated = true;
                continue;
            }

            _fileSystem.CopyVerified(slot.Path + "/" + relative, destination + "/" + relative);
            used += length;
        }

        if (truncated)
            MutateSync(state => Attention(state, WorkspaceSlotCodes.EvidenceTruncated, slot.Id, destination,
                "retention cap " + Settings.EvidenceCapBytes + " bytes reached"));
    }

    private void PruneRetained()
    {
        if (!Directory.Exists(RetainedRoot))
            return;
        var cutoff = _clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(Settings.EvidenceRetentionDays);
        foreach (var directory in Directory.EnumerateDirectories(RetainedRoot))
        {
            if (Path.GetFileName(directory) == "quarantine")
                continue;
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // ---------------------------------------------------------------- quarantine and re-home

    /// <summary>
    /// §3.5a. The directory is locked and recorded in place, never moved or deleted. The slot keeps its id and
    /// lease generation and moves to <c>ws-NN-dK</c>; two failed creations mark only this slot Failed.
    /// </summary>
    private async Task QuarantineAndRehomeAsync(WorkspaceSlotRecord slot, string cls, string reason, string error, CancellationToken ct)
    {
        await QuarantineDirectoryAsync(slot, cls, reason, error, ct);
        var retired = await MutateAsync(state =>
        {
            var record = state.Slot(slot.Id)!;
            var now = _clock.GetUtcNow();
            record.Rehomes.RemoveAll(at => at < now - TimeSpan.FromHours(24));
            record.Rehomes.Add(now);
            if (record.TaskId is { } task)
                record.LastTaskId = task;
            if (record.Rehomes.Count > Settings.MaxRehomesPerSlotPerDay)
            {
                record.State = WorkspaceSlotStates.Retired;
                record.Detail = "re-homed " + record.Rehomes.Count + " times in 24 h";
                Attention(state, WorkspaceSlotCodes.Retired, record.Id, record.Path, record.Detail);
                return true;
            }

            record.State = WorkspaceSlotStates.Rehoming;
            return false;
        });
        if (!retired)
            await RehomeAsync(slot.Id, ct);
    }

    private async Task QuarantineDirectoryAsync(WorkspaceSlotRecord slot, string cls, string reason, string error, CancellationToken ct)
    {
        string? head = null, branch = null;
        if (Directory.Exists(slot.Path) && File.Exists(slot.Path + "/.git"))
        {
            var rev = await _service.GitAsync(slot.Path, ct, "rev-parse", "HEAD");
            head = rev.ExitCode == 0 ? rev.Stdout.Trim() : null;
            var symbolic = await _service.GitAsync(slot.Path, ct, "symbolic-ref", "-q", "--short", "HEAD");
            branch = symbolic.ExitCode == 0 ? symbolic.Stdout.Trim() : null;
        }

        var lockReason = "antiphon quarantine " + slot.Id + " " + cls + " " + reason;
        // A linked slot path would resolve to (and lock) whatever worktree the link names.
        var linked = true;
        try { linked = IsLink(slot.Path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var locked = linked
            ? (ExitCode: 1, Stdout: "", Stderr: "slot path is a link or unreadable; not locked")
            : await RepositoryGitAsync(ct, "worktree", "lock", "--reason", lockReason, slot.Path);
        long bytes = 0;
        try { bytes = _disk.Measure(slot.Path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        await MutateAsync(state =>
        {
            state.QuarantinedDirectories.Add(new QuarantinedDirectoryRecord
            {
                DirId = Guid.NewGuid().ToString("N")[..12],
                Path = slot.Path,
                SlotId = slot.Id,
                DirectoryGeneration = slot.DirectoryGeneration,
                Class = cls,
                Reason = reason,
                Error = error.Length > ErrorLimit ? error[..ErrorLimit] : error,
                Branch = branch ?? slot.Branch,
                Head = head,
                TaskId = slot.TaskId,
                Bytes = bytes,
                Locked = locked.ExitCode == 0,
                LockError = locked.ExitCode == 0 ? null : Bound(locked.Stderr),
                At = _clock.GetUtcNow(),
            });
            if (cls == "custody")
                Attention(state, WorkspaceSlotCodes.Quarantined, slot.Id, slot.Path, reason);
            Attention(state, WorkspaceSlotCodes.Rehomed, slot.Id, slot.Path, cls + " " + reason);
        });
    }

    private async Task RehomeAsync(string slotId, CancellationToken ct)
    {
        var slot = _ledger.Read().Slot(slotId)!;
        string published;
        try
        {
            published = await PublishedShaAsync(ct);
        }
        catch (PhoneHomeAdmissionException ex)
        {
            await MutateAsync(state => Fail(state, slotId, "re-home failed: " + ex.Message));
            return;
        }

        var generation = slot.DirectoryGeneration;
        for (var tries = 0; tries < 2; tries++)
        {
            generation++;
            var path = _service.WorktreeRoot + "/" + slot.Id + "-d" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            while (Directory.Exists(path))
            {
                generation++;
                path = _service.WorktreeRoot + "/" + slot.Id + "-d" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var add = await RepositoryGitAsync(ct, "worktree", "add", "--detach", path, published);
            if (add.ExitCode != 0 || !await StampDirectoryAsync(path, slot.Id, generation, ct))
                continue;
            var created = generation;
            await MutateAsync(state =>
            {
                var record = state.Slot(slotId)!;
                MarkFree(record);
                record.Path = path;
                record.DirectoryGeneration = created;
                record.LastResetKind = "rehome";
            });
            SignalFreed();
            return;
        }

        await MutateAsync(state => Fail(state, slotId, "both replacement directories failed"));
    }

    private void Fail(WorkspaceSlotLedgerState state, string slotId, string detail)
    {
        var record = state.Slot(slotId)!;
        if (record.TaskId is { } task)
            record.LastTaskId = task;
        record.State = WorkspaceSlotStates.Failed;
        record.Detail = detail;
        Attention(state, WorkspaceSlotCodes.RehomeFailed, record.Id, record.Path, detail);
    }

    public async Task<PhoneHomeWorkspaceSlotQuarantineReleaseResponse> ReleaseQuarantineAsync(
        PhoneHomeWorkspaceSlotQuarantineReleaseRequest request, CancellationToken ct)
    {
        var entry = _ledger.Read().QuarantinedDirectories.FirstOrDefault(item => item.DirId == request.DirId && item.IsOpen)
                    ?? throw Refuse("Quarantined directory " + request.DirId + " is not open.");
        switch (request.Disposition)
        {
            case "Pushed":
                if (await UnpushedAsync(entry.Path, entry.Branch, ct) is { } unpushed)
                    return new(entry.DirId, "refused", unpushed);
                if (await DirtAsync(entry.Path, ct) is { } dirt)
                    return new(entry.DirId, "refused", "dirty: " + dirt);
                break;
            case "AcceptLoss":
                RetainLoss(entry, await LossAsync(entry, ct));
                break;
            case "Removed":
                break;
            default:
                throw Refuse("Disposition must be Pushed, AcceptLoss or Removed.");
        }

        PhaseCut?.Invoke("before-quarantine-removal");
        await RepositoryGitAsync(ct, "worktree", "unlock", entry.Path);
        var remove = await RepositoryGitAsync(ct, "worktree", "remove", "--force", "--force", entry.Path);
        if (remove.ExitCode != 0 || Directory.Exists(entry.Path))
        {
            await RepositoryGitAsync(ct, "worktree", "lock", "--reason", "antiphon quarantine " + entry.SlotId + " " + entry.Class + " " + entry.Reason, entry.Path);
            var detail = Bound(remove.Stderr);
            await MutateAsync(state => state.QuarantinedDirectories.First(item => item.DirId == entry.DirId).Outcome = "removal_failed: " + detail);
            return new(entry.DirId, "removal_failed", detail);
        }

        await RepositoryGitAsync(ct, "worktree", "prune");
        await MutateAsync(state =>
        {
            var record = state.QuarantinedDirectories.First(item => item.DirId == entry.DirId);
            record.Disposition = request.Disposition;
            record.ClosedAt = _clock.GetUtcNow();
            record.Outcome = "removed: " + request.Reason;
        });
        return new(entry.DirId, "removed");
    }

    private async Task<(string Diff, string Untracked, string Commits)> LossAsync(QuarantinedDirectoryRecord entry, CancellationToken ct)
    {
        var diff = await _service.GitAsync(entry.Path, ct, "diff", "HEAD", "--binary");
        var untracked = await _service.GitAsync(entry.Path, ct, "ls-files", "--others", "--exclude-standard");
        var commits = await _service.GitAsync(entry.Path, ct, "log", "-p", "--binary", "HEAD", "--not", "--remotes=origin");
        if (diff.ExitCode != 0 || untracked.ExitCode != 0)
            throw Refuse("The quarantined directory's diff could not be read; nothing was removed.");
        return (diff.Stdout, untracked.Stdout, commits.ExitCode == 0 ? commits.Stdout : "");
    }

    private void RetainLoss(QuarantinedDirectoryRecord entry, (string Diff, string Untracked, string Commits) loss)
    {
        var root = RetainedRoot + "/quarantine/" + entry.DirId;
        _fileSystem.CreateDirectory(root);
        File.WriteAllText(root + "/diff.patch", loss.Diff);
        File.WriteAllText(root + "/untracked.txt", loss.Untracked);
        File.WriteAllText(root + "/commits.patch", loss.Commits);
        File.WriteAllText(root + "/head.txt", (entry.Head ?? "") + "\n" + entry.Path + "\n");
        foreach (var name in loss.Untracked.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var source = entry.Path + "/" + name;
            if (File.Exists(source))
                _fileSystem.CopyVerified(source, root + "/untracked/" + name);
        }
    }

    // ---------------------------------------------------------------- cold reset

    public async Task<PhoneHomeWorkspaceSlotColdResetResponse> ColdResetAsync(
        PhoneHomeWorkspaceSlotColdResetRequest request, CancellationToken ct)
    {
        var outcomes = new List<PhoneHomeWorkspaceSlotColdResetOutcome>();
        var run = new List<WorkspaceSlotRecord>();
        var record = new WorkspaceSlotColdResetRecord
        {
            RequestId = request.RequestId.ToString("D"),
            Requester = request.Requester,
            Reason = request.Reason,
            At = _clock.GetUtcNow(),
        };
        await MutateAsync(state =>
        {
            var targets = request.All
                ? state.Slots.Where(slot => WorkspaceSlotStates.IsActive(slot.State)).Select(slot => slot.Id).ToList()
                : (request.Slots ?? []).ToList();
            if (state.ColdResetRequests.Contains(record.RequestId))
            {
                outcomes.AddRange(targets.Select(id => new PhoneHomeWorkspaceSlotColdResetOutcome(id, "duplicate")));
                return;
            }

            state.ColdResetRequests.Add(record.RequestId);
            foreach (var id in targets)
            {
                var slot = state.Slot(id);
                if (slot is null || !WorkspaceSlotStates.IsActive(slot.State))
                {
                    outcomes.Add(new(id, "unknown"));
                }
                else if (slot.State == WorkspaceSlotStates.Free)
                {
                    slot.State = WorkspaceSlotStates.ColdResetting;
                    run.Add(Clone(slot));
                    outcomes.Add(new(id, "cold_reset"));
                }
                else
                {
                    // Never while leased: it runs inside that slot's release, before the slot is Free.
                    slot.ColdResetPending ??= record;
                    outcomes.Add(new(id, "pending"));
                }
            }
        });

        foreach (var slot in run)
        {
            PhaseCut?.Invoke("cold-resetting");
            // D-2: the lease's own preflight (ownership, free invariant, unpushed reflog) runs BEFORE the clean, so
            // a cold reset deletes only what a lease would also reset away: keep-list native output. Anything a
            // lease would quarantine (an untracked or non-keep-list ignored file, an unpushed commit) is
            // quarantined here with its bytes in place.
            var failure = await PreflightFreeAsync(slot, ct);
            if (failure is null)
            {
                var owned = ProveOwnership(slot);
                var clean = owned.Owned is { } proven
                    ? await _service.GitAsync(proven.Path, ct, [.. WorkspaceSlotResetPolicy.CleanArguments(cold: true)])
                    : (ExitCode: 1, Stdout: "", Stderr: owned.Failure!.Detail);
                failure = clean.ExitCode != 0
                    ? new Failure("filesystem", "cold-reset", "clean: " + Bound(clean.Stderr))
                    : await CheckFreeAsync(slot, ct);
            }
            if (failure is not null)
            {
                await QuarantineAndRehomeAsync(slot, failure.Class, failure.Reason, failure.Detail, ct);
                var index = outcomes.FindIndex(item => item.SlotId == slot.Id);
                outcomes[index] = new(slot.Id, "quarantined");
                continue;
            }

            await MutateAsync(state =>
            {
                var current = state.Slot(slot.Id)!;
                current.State = WorkspaceSlotStates.Free;
                current.LastColdReset = record;
                current.LastResetKind = "cold";
                current.ColdResetPending = null;
            });
            SignalFreed();
        }

        return new PhoneHomeWorkspaceSlotColdResetResponse(request.RequestId, outcomes);
    }

    // ---------------------------------------------------------------- maintenance and recovery

    /// <summary>Pool maintenance (at start and every 15 min): schedule marks, deferred releases, idle retirement.</summary>
    public async Task MaintainAsync(CancellationToken ct)
    {
        await MarkScheduleAsync();
        var now = _clock.GetUtcNow();
        foreach (var slot in _ledger.Read().Slots.Where(slot => slot.State == WorkspaceSlotStates.Leased
                     && slot.ReleaseRequested && slot.NextLingerAt <= now).ToList())
            await SettleAsync(slot.Id, allowDefer: true, ct);
        await RetireIdleAsync(ct);
        PruneRetained();
    }

    private Task MarkScheduleAsync() => MutateAsync(state =>
    {
        if (LatestBoundary(_clock.GetUtcNow()) is not { } boundary)
            return;
        if (state.LastScheduledBoundary is null)
        {
            state.LastScheduledBoundary = boundary;
            return;
        }

        if (boundary <= state.LastScheduledBoundary)
            return;
        var record = new WorkspaceSlotColdResetRecord
        {
            RequestId = "schedule:" + boundary.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            Requester = "schedule",
            Reason = Settings.ColdResetSchedule + " cold reset",
            At = _clock.GetUtcNow(),
        };
        foreach (var slot in state.Slots.Where(slot => WorkspaceSlotStates.IsActive(slot.State)))
            slot.ColdResetPending ??= record;
        state.ScheduledMarks++;
        state.LastScheduledBoundary = boundary;
    });

    private DateTimeOffset? LatestBoundary(DateTimeOffset now)
    {
        var hour = Math.Clamp(Settings.ColdResetHourUtc, 0, 23);
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddHours(hour);
        switch (Settings.ColdResetSchedule)
        {
            case "Daily":
                return today <= now ? today : today.AddDays(-1);
            case "Weekly":
                var back = ((int)now.UtcDateTime.DayOfWeek - (int)Settings.ColdResetDayOfWeek + 7) % 7;
                var candidate = today.AddDays(-back);
                return candidate <= now ? candidate : candidate.AddDays(-7);
            default:
                return null;
        }
    }

    private async Task RetireIdleAsync(CancellationToken ct)
    {
        while (true)
        {
            var candidate = await MutateAsync(state =>
            {
                var active = state.Slots.Where(slot => WorkspaceSlotStates.IsActive(slot.State)).ToList();
                if (active.Count <= Target)
                    return null;
                var cutoff = _clock.GetUtcNow() - TimeSpan.FromHours(Settings.IdleRetireHours);
                var slot = active.Where(item => item.State == WorkspaceSlotStates.Free && item.IdleSince <= cutoff)
                    .OrderByDescending(item => item.Number).FirstOrDefault();
                if (slot is null)
                    return null;
                slot.State = WorkspaceSlotStates.Retiring;
                return Clone(slot);
            });
            if (candidate is null)
                return;

            var failure = await PreflightFreeAsync(candidate, ct);
            if (failure is null)
            {
                var remove = await RepositoryGitAsync(ct, "worktree", "remove", "--force", candidate.Path);
                if (remove.ExitCode != 0 || Directory.Exists(candidate.Path))
                    failure = new Failure("filesystem", "retire", "worktree remove: " + Bound(remove.Stderr));
            }

            if (failure is not null)
                await QuarantineDirectoryAsync(candidate, failure.Class, failure.Reason, failure.Detail, ct);
            await MutateAsync(state =>
            {
                var record = state.Slot(candidate.Id)!;
                record.State = WorkspaceSlotStates.Retired;
                record.Detail = failure is null ? "idle retirement" : "retired; directory quarantined";
            });
        }
    }

    /// <summary>
    /// §3.5 crash recovery. Every slot in a transitional state, and every Leased slot the server did not
    /// confirm (when <paramref name="confirmed"/> is given), is settled by the release check: reset to Free
    /// or quarantined and re-homed. Null (a plain restart) KEEPS every Leased slot: the runner cannot tell a
    /// terminal task from a queued one, and the server still owes the release. The task row keeps the runner
    /// id, slot id and generation with no release stamp, so the reconciler releases it once the task is
    /// terminal and a drain re-arm releases it on this runner before the task may move (D-3, review 3a354688).
    /// </summary>
    public async Task RecoverAsync(IReadOnlyCollection<(string SlotId, int Generation)>? confirmed, CancellationToken ct)
    {
        foreach (var slot in _ledger.Read().Slots.ToList())
        {
            switch (slot.State)
            {
                case WorkspaceSlotStates.Leasing or WorkspaceSlotStates.Releasing or WorkspaceSlotStates.ColdResetting:
                    await SettleAsync(slot.Id, allowDefer: false, ct);
                    break;
                case WorkspaceSlotStates.Leased when confirmed is not null && !confirmed.Contains((slot.Id, slot.LeaseGeneration)):
                    await SettleAsync(slot.Id, allowDefer: false, ct);
                    break;
                case WorkspaceSlotStates.Rehoming:
                    await RehomeAsync(slot.Id, ct);
                    break;
                case WorkspaceSlotStates.Retiring:
                    await MutateAsync(state => state.Slot(slot.Id)!.State = WorkspaceSlotStates.Free);
                    break;
            }
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await RecoverAsync(null, ct);
        await MaintainAsync(ct);
    }

    // ---------------------------------------------------------------- git and ledger plumbing

    /// <summary>CARD-1817. The repository lock is the runner's lease. Maintenance skips while it is held.</summary>
    internal async Task RunUnderRepositoryLockAsync(Func<Task> action, CancellationToken ct)
    {
        await _repository.WaitAsync(ct);
        using var lease = _activity?.EnterLease();
        try { await action(); }
        finally { _repository.Release(); }
    }

    private async Task FetchExactAsync(string branch, string sha, CancellationToken ct)
    {
        await _repository.WaitAsync(ct);
        using var lease = _activity?.EnterLease();
        try
        {
            var fetch = await _service.GitAsync(_service.PrimaryRepository, ct, "fetch", "origin",
                "+refs/heads/" + branch + ":refs/remotes/origin/" + branch);
            if (fetch.ExitCode != 0)
                throw Refuse("Mirror fetch failed: " + Bound(fetch.Stderr));
            // G-27: the fetched tip must be exactly the sha the desktop pushed.
            var tip = await _service.GitAsync(_service.PrimaryRepository, ct, "rev-parse", "refs/remotes/origin/" + branch);
            if (tip.ExitCode != 0 || tip.Stdout.Trim() != sha)
                throw Refuse($"Fetched tip of {branch} is not {sha}.");
        }
        finally
        {
            _repository.Release();
        }
    }

    /// <summary>D-9: Free slots park detached at the fetched published-branch tip.</summary>
    private async Task<string> PublishedShaAsync(CancellationToken ct)
    {
        var branch = _service.PublishedBranch;
        await RepositoryGitAsync(ct, "fetch", "origin", "+refs/heads/" + branch + ":refs/remotes/origin/" + branch);
        var tip = await _service.GitAsync(_service.PrimaryRepository, ct, "rev-parse", "refs/remotes/origin/" + branch);
        if (tip.ExitCode != 0 || tip.Stdout.Trim().Length != 40)
            throw Refuse("The published branch " + branch + " has no fetched tip.");
        return tip.Stdout.Trim();
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RepositoryGitAsync(CancellationToken ct, params string[] args)
    {
        await _repository.WaitAsync(ct);
        using var lease = _activity?.EnterLease();
        try
        {
            return await _service.GitAsync(_service.PrimaryRepository, ct, args);
        }
        finally
        {
            _repository.Release();
        }
    }

    private async Task<string?> GitDirectoryAsync(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path))
            return null;
        var gitDir = await _service.GitAsync(path, ct, "rev-parse", "--path-format=absolute", "--git-dir");
        return gitDir.ExitCode == 0 && gitDir.Stdout.Trim().Length > 0 ? Normalize(gitDir.Stdout.Trim()) : null;
    }

    private async Task MutateAsync(Action<WorkspaceSlotLedgerState> change)
    {
        await _lock.WaitAsync();
        try
        {
            var state = _ledger.Read();
            change(state);
            _ledger.Write(state);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<T> MutateAsync<T>(Func<WorkspaceSlotLedgerState, T> change)
    {
        await _lock.WaitAsync();
        try
        {
            var state = _ledger.Read();
            var result = change(state);
            _ledger.Write(state);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void MutateSync(Action<WorkspaceSlotLedgerState> change)
    {
        _lock.Wait();
        try
        {
            var state = _ledger.Read();
            change(state);
            _ledger.Write(state);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void Attention(WorkspaceSlotLedgerState state, string key, string slotId, string? path, string? detail) =>
        state.Attention.Add(new WorkspaceSlotAttention { Key = key, SlotId = slotId, Path = path, Detail = detail, At = _clock.GetUtcNow() });

    private WorkspaceSlotColdResetRecord Stamp(WorkspaceSlotColdResetRecord pending) => new()
    {
        RequestId = pending.RequestId,
        Requester = pending.Requester,
        Reason = pending.Reason,
        At = _clock.GetUtcNow(),
    };

    private void SignalFreed() =>
        Interlocked.Exchange(ref _freed, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    private static WorkspaceSlotRecord Clone(WorkspaceSlotRecord record) =>
        JsonSerializer.Deserialize<WorkspaceSlotRecord>(JsonSerializer.Serialize(record, StampJson), StampJson)!;

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Replace('\\', '/');

    private static string Bound(string text)
    {
        var trimmed = text.Replace("\r", "").Trim();
        return trimmed.Length > ErrorLimit ? trimmed[..ErrorLimit] : trimmed;
    }

    private static PhoneHomeAdmissionException Refuse(string message) =>
        new(PhoneHomeProblemTypes.UnsupportedTarget, message, 409);
}
