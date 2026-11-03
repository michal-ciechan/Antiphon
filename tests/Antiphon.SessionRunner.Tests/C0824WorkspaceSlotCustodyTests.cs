using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C0824SlotScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0824 S1 CP-2. Unpushed work and dirt are preserved, crashes recover, slots are exclusive.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
public sealed class C0824WorkspaceSlotCustodyTests
{
    [Test]
    public async Task C0824_UnpushedTipQuarantines(CancellationToken ct)
    {
        // V-3a. An unpushed commit on the task branch keeps the directory and re-homes the slot.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        Plant(a.Path, "unpushed.txt", "only copy");
        Git(a.Path, "add", "unpushed.txt");
        Git(a.Path, "commit", "-m", "unpushed");
        var head = Git(a.Path, "rev-parse", "HEAD").Trim();
        var before = Tree(a.Path);

        (await Release(pool, a, TaskA)).Outcome.ShouldBe("quarantined");
        Tree(a.Path).ShouldBe(before);
        Git(a.Path, "rev-parse", "HEAD").Trim().ShouldBe(head);
        var ledger = pool.Ledger();
        var entry = ledger.QuarantinedDirectories.ShouldHaveSingleItem();
        entry.Class.ShouldBe("custody");
        entry.Reason.ShouldBe("unpushed");
        entry.Path.ShouldBe(a.Path);
        var slot = ledger.Slot("ws-01").ShouldNotBeNull();
        slot.Path.ShouldBe(scratch.Slot("ws-01-d2"));
        slot.State.ShouldBe(WorkspaceSlotStates.Free);
    }

    [Test]
    public async Task C0824_UnpushedReflogOnlyCommitQuarantines(CancellationToken ct)
    {
        // V-3b. A commit reached only through the HEAD reflog (a deleted side branch) is unpushed work.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        Git(a.Path, "checkout", "-q", "-b", "side");
        Plant(a.Path, "side.txt", "side work");
        Git(a.Path, "add", "side.txt");
        Git(a.Path, "commit", "-m", "side");
        Git(a.Path, "checkout", "-q", BranchA);
        Git(a.Path, "branch", "-D", "side");
        Git(a.Path, "push", "origin", BranchA);

        (await Release(pool, a, TaskA)).Outcome.ShouldBe("quarantined");
        pool.Ledger().QuarantinedDirectories.Count(entry => entry.Reason == "unpushed").ShouldBe(1, "quarantined count 1");
        Directory.Exists(a.Path).ShouldBeTrue();
    }

    [Test]
    public async Task C0824_DirtQuarantinesPreservesBytesAndRehomes(CancellationToken ct)
    {
        // V-4. Dirt is preserved and locked in place; the next lease gets a clean ws-01-d2 under the same id.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        File.WriteAllText(Path.Combine(a.Path, "a.txt"), "modified");
        Plant(a.Path, "untracked.txt", "new");
        var before = Tree(a.Path);

        (await Release(pool, a, TaskA)).Outcome.ShouldBe("quarantined");
        Tree(a.Path).ShouldBe(before, "old Tree() unchanged");
        var list = Git(scratch.Clone, "worktree", "list", "--porcelain");
        list.ShouldContain("worktree " + a.Path + "\n");
        list.ShouldContain("locked antiphon quarantine ws-01 custody dirty");

        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        b.Path.ShouldBe(scratch.Slot("ws-01-d2"));
        b.SlotId.ShouldBe("ws-01");
        Git(b.Path, "rev-parse", "HEAD").Trim().ShouldBe(scratch.ShaB);
        Git(b.Path, "status", "--porcelain=v1", "-z", "--untracked-files=all").ShouldBeEmpty();
    }

    [Test]
    [Arguments("Leasing")]
    [Arguments("Leased")]
    [Arguments("Releasing")]
    public async Task C0824_CrashAtEachPhaseRecoversNeverDirty(string phase, CancellationToken ct)
    {
        // V-6. A crash after each journal intent recovers to Free or a quarantined directory, never a dirty lease.
        using var scratch = Create();
        var pool = scratch.Pool();
        var first = await pool.LeaseAsync(scratch.RequestA(), ct);
        if (phase == "Releasing")
        {
            Plant(first.Path, "dirty.txt", "left by the crashed task");
            pool.PhaseCut = cut => { if (cut == "releasing") throw new InvalidOperationException("crash"); };
            await Should.ThrowAsync<InvalidOperationException>(() => Release(pool, first, TaskA));
        }
        else
        {
            await Release(pool, first, TaskA);
            pool.PhaseCut = cut => { if (cut == phase.ToLowerInvariant()) throw new InvalidOperationException("crash"); };
            await Should.ThrowAsync<InvalidOperationException>(() => pool.LeaseAsync(scratch.RequestB(), ct));
        }

        pool.Ledger().Slot("ws-01")!.State.ShouldBe(phase);
        var restarted = scratch.Pool();
        await restarted.RecoverAsync([], ct);
        var ledger = restarted.Ledger();
        var slot = ledger.Slot("ws-01").ShouldNotBeNull();
        slot.State.ShouldBe(WorkspaceSlotStates.Free);
        if (phase == "Releasing")
        {
            ledger.QuarantinedDirectories.ShouldHaveSingleItem().Path.ShouldBe(first.Path);
            File.Exists(Path.Combine(first.Path, "dirty.txt")).ShouldBeTrue();
        }
        else
        {
            ledger.QuarantinedDirectories.ShouldBeEmpty();
        }

        var next = await restarted.LeaseAsync(scratch.RequestC(), ct);
        File.Exists(Path.Combine(next.Path, "dirty.txt")).ShouldBeFalse();
        Git(next.Path, "status", "--porcelain=v1", "-z", "--untracked-files=all").ShouldBeEmpty();
    }

    [Test]
    public async Task C0824_LeaseReverifiesFreeInvariant(CancellationToken ct)
    {
        // V-6b. A late writer in a Free slot: the lease quarantines that directory and returns another one.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        await Release(pool, a, TaskA);
        Plant(a.Path, "x.txt", "late writer");
        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        b.Path.ShouldNotBe(a.Path, "returned slot != planted one");
        File.Exists(Path.Combine(a.Path, "x.txt")).ShouldBeTrue();
        pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem().Path.ShouldBe(a.Path);
    }

    [Test]
    public async Task C0824_LeaseQuarantinesFreeSlotWithHolder(CancellationToken ct)
    {
        // CARD-0824 S1 mutation survivor X17. A process entered a Free slot after its release: the lease-time free
        // check sees the holder, quarantines that directory and re-homes, and the lease gets another directory.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        (await Release(pool, a, TaskA)).Outcome.ShouldBe("released");
        scratch.Processes.Holders = path => path == a.Path ? [new WorkspaceSlotHolder(4242, "/usr/bin/dotnet", "cwd")] : [];
        var before = Tree(a.Path);

        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        b.Path.ShouldNotBe(a.Path, "the held directory is never handed to a second task");
        Directory.Exists(a.Path).ShouldBeTrue();
        Tree(a.Path).ShouldBe(before, "the held directory is untouched");
        var entry = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem();
        entry.Path.ShouldBe(a.Path);
        entry.Class.ShouldBe("custody");
        entry.Reason.ShouldBe("lingering");
        entry.Error.ShouldContain("pid=4242");
        pool.Ledger().Slot(b.SlotId)!.TaskId.ShouldBe(TaskB);
    }

    [Test]
    public async Task C0824_ReleaseOfLeasingSlotRefused(CancellationToken ct)
    {
        // CARD-0824 S1 mutation survivor X06. Only a Leased slot is settled: a release whose generation and task match
        // a slot still in Leasing (the lease is preparing its tree) is refused and leaves the slot to the lease.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        (await Release(pool, a, TaskA)).Outcome.ShouldBe("released");
        PhoneHomeAdmissionException? refusal = null;
        string? settled = null, stateAtCut = null, stateAfterRelease = null;
        var cuts = 0;
        pool.PhaseCut = phase =>
        {
            if (phase != "leasing" || Interlocked.Exchange(ref cuts, 1) == 1)
                return;
            var leasing = pool.Ledger().Slot(a.SlotId)!;
            stateAtCut = leasing.State;
            try
            {
                settled = pool.ReleaseAsync(new PhoneHomeWorkspaceSlotReleaseRequest(leasing.Id, leasing.LeaseGeneration, TaskB),
                    CancellationToken.None).GetAwaiter().GetResult().Outcome;
            }
            catch (PhoneHomeAdmissionException ex)
            {
                refusal = ex;
            }

            stateAfterRelease = pool.Ledger().Slot(a.SlotId)!.State;
        };

        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        stateAtCut.ShouldBe(WorkspaceSlotStates.Leasing);
        settled.ShouldBeNull("the release must not settle a Leasing slot");
        refusal.ShouldNotBeNull().Message.ShouldBe("Slot " + a.SlotId + " is " + WorkspaceSlotStates.Leasing + ", not leased.");
        stateAfterRelease.ShouldBe(WorkspaceSlotStates.Leasing);
        b.SlotId.ShouldBe(a.SlotId);
        var slot = pool.Ledger().Slot(a.SlotId)!;
        slot.State.ShouldBe(WorkspaceSlotStates.Leased);
        slot.TaskId.ShouldBe(TaskB);
        slot.ReleaseRequested.ShouldBeFalse();
        Git(b.Path, "rev-parse", "HEAD").Trim().ShouldBe(scratch.ShaB);
    }

    [Test]
    public async Task C0824_ConcurrentLeasesNeverShareSlot(CancellationToken ct)
    {
        // V-7a. The first lease is held right after its pick; a second lease must pick another slot.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        await Release(pool, a, TaskA);
        var secondEntered = new ManualResetEventSlim();
        var firstHeld = 0;
        pool.PhaseCut = phase =>
        {
            if (phase == "enter" && Volatile.Read(ref firstHeld) == 1)
                secondEntered.Set();
            if (phase == "picked" && Interlocked.CompareExchange(ref firstHeld, 1, 0) == 0)
            {
                secondEntered.Wait(TimeSpan.FromSeconds(5));
                Thread.Sleep(300);
            }
        };
        var first = Task.Run(() => pool.LeaseAsync(scratch.RequestB(), ct), ct);
        var second = Task.Run(async () =>
        {
            while (Volatile.Read(ref firstHeld) == 0)
                await Task.Yield();
            return await pool.LeaseAsync(scratch.RequestC(), ct);
        }, ct);
        var leases = await Task.WhenAll(first, second);
        leases[0].SlotId.ShouldNotBe(leases[1].SlotId, "distinct slot ids");
        pool.Ledger().Slots.Count(slot => slot.State == WorkspaceSlotStates.Leased).ShouldBe(2);
    }

    [Test]
    public async Task C0824_BranchCheckedOutInAnotherSlotRefused(CancellationToken ct)
    {
        // V-7b. The same branch never sits in two slots.
        using var scratch = Create();
        var pool = scratch.Pool();
        await pool.LeaseAsync(scratch.RequestA(), ct);
        var before = JsonSerializer.Serialize(pool.Ledger());
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(() =>
            pool.LeaseAsync(Request(TaskC, BranchA, scratch.ShaA), ct));
        refusal.Message.ShouldContain(WorkspaceSlotCodes.BranchCheckedOut);
        JsonSerializer.Serialize(pool.Ledger()).ShouldBe(before);
        Directory.EnumerateDirectories(scratch.Worktrees).Count().ShouldBe(1);
    }

    [Test]
    [Arguments("ws-01")]
    [Arguments("ws-01-d2")]
    public async Task C0824_RemoveAsyncRefusesSlotManaged(string leaf, CancellationToken ct)
    {
        // V-8. The legacy removal path never removes a slot directory, whatever the flag.
        using var scratch = Create();
        var service = scratch.Service();
        var path = scratch.Slot(leaf);
        Directory.CreateDirectory(scratch.Worktrees);
        Git(scratch.Clone, "worktree", "add", "--detach", path, scratch.Base);
        Plant(path, "lessee.txt", "lessee work");
        var before = Tree(path);
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(() =>
            service.RemoveAsync(new PhoneHomeWorkspaceRemoveRequest(path, Force: true), ct));
        refusal.Message.ShouldBe(WorkspaceSlotCodes.SlotManaged);
        Tree(path).ShouldBe(before);
    }

    [Test]
    public async Task C0824_StaleGenerationReleaseRefused(CancellationToken ct)
    {
        // V-9. A stale generation is refused; a repeated current release is an idempotent success.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        await Release(pool, a, TaskA);
        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        b.SlotId.ShouldBe(a.SlotId);
        b.Generation.ShouldBe(a.Generation + 1);
        Plant(b.Path, "b-work.log", "lessee");
        var before = Tree(b.Path);
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => Release(pool, a, TaskA));
        refusal.Message.ShouldContain(WorkspaceSlotCodes.GenerationMismatch);
        Tree(b.Path).ShouldBe(before);
        pool.Ledger().Slot("ws-01")!.State.ShouldBe(WorkspaceSlotStates.Leased);

        (await Release(pool, b, TaskB)).Outcome.ShouldBe("released");
        int calls;
        lock (scratch.Argv)
            calls = scratch.Argv.Count;
        var tree = Tree(b.Path);
        (await Release(pool, b, TaskB)).Outcome.ShouldBe("already_free");
        Tree(b.Path).ShouldBe(tree);
        lock (scratch.Argv)
            scratch.Argv.Skip(calls).ShouldNotContain(argv => argv.Length > 1 && (argv[1] == "clean" || argv[1] == "checkout"));
    }

    [Test]
    public async Task C0824_LeasedSlotNeverResetByForeignRelease(CancellationToken ct)
    {
        // V-47. The current generation with another task's id is refused.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        Plant(a.Path, "a-work.log", "lessee");
        var before = Tree(a.Path);
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => Release(pool, a, TaskB));
        refusal.Message.ShouldContain(WorkspaceSlotCodes.TaskMismatch);
        Tree(a.Path).ShouldBe(before);
        pool.Ledger().Slot("ws-01")!.State.ShouldBe(WorkspaceSlotStates.Leased);
    }

    [Test]
    public async Task C0824_PublishResolvesSlotByLedger(CancellationToken ct)
    {
        // V-48. Publish from a slot is admitted for its lessee only.
        using var scratch = Create();
        var pool = scratch.Pool();
        var service = scratch.Service();
        service.SlotPool = pool;
        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        await Should.ThrowAsync<PhoneHomeAdmissionException>(() =>
            service.PublishAsync(new PhoneHomeWorkspacePublishRequest(b.Path, BranchA, scratch.Base, null, false), ct));
        var accepted = await service.PublishAsync(new PhoneHomeWorkspacePublishRequest(b.Path, BranchB, scratch.Base, null, false), ct);
        accepted.Tip.ShouldBe(scratch.ShaB);
        accepted.Refusal.ShouldBeNull();
    }

    [Test]
    public async Task C0824_LeaseThroughDispatcherFrame(CancellationToken ct)
    {
        // V-49. One WorkspaceMirror frame with the slot fields through the real dispatcher decode.
        using var scratch = Create();
        var service = scratch.Service();
        scratch.Pool(service: service);
        var storeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var storePath = scratch.Root + "/store-id";
        File.WriteAllText(storePath, storeId.ToString("D"));
        var dispatcher = new PhoneHomeCommandDispatcher(new C0824StoreRuntime(storeId), new PhoneHomeSettings
        {
            Enabled = true,
            RunnerId = "runner-a",
            AllowedCwd = scratch.Work,
            RunnerRepository = scratch.Clone.Replace('\\', '/'),
            Capacity = 2,
            CapacityStatePath = scratch.Root.Replace('\\', '/') + "/capacity",
            StoreIdPath = storePath,
            WorkspaceSlots = Settings(),
        });
        dispatcher.UseWorkspace(service);
        var request = new PhoneHomeWorkspaceMirrorRequest(BranchA, scratch.ShaA, "task-aaaaaaaa",
            Binding: new PhoneHomeWorkspaceMirrorBinding(TaskA, 1, Guid.NewGuid(), "runner-a", storeId.ToString("D")),
            Slot: true, Cold: false);
        var reply = await dispatcher.DispatchAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.WorkspaceMirror,
            JsonSerializer.SerializeToElement(request, PhoneHomeFraming.Json)), ct);
        reply.Kind.ShouldBe(PhoneHomeFrameKind.Result, reply.ErrorDetail);
        var body = reply.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorResponse>(PhoneHomeFraming.Json)!;
        body.SlotId.ShouldBe("ws-01");
        body.Generation.ShouldBe(1);
        body.Path.ShouldStartWith(scratch.Worktrees + "/ws-");
        PhoneHomeWorkspaceMirrorContract.ValidateResponse(reply.Payload!.Value.GetRawText()).IsValid.ShouldBeTrue();
    }

    [Test]
    public async Task C0824_LeaseWaitsForTheDispatcherMutationLock(CancellationToken ct)
    {
        // CARD-0824 S1 mutation survivor PC-74 (G-74). A slot lease is a workspace mutation: while an Input holds the
        // dispatcher's mutation lock, a WorkspaceMirror frame does not enter the pool (no Leasing slot is written) and
        // its reply arrives only after the lock is released.
        using var scratch = Create();
        var service = scratch.Service();
        var pool = scratch.Pool(service: service);
        var storeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var runtime = new C0824StoreRuntime(storeId)
        {
            InputGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var dispatcher = SlotDispatcher(scratch, service, runtime, storeId);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.PhaseCut = phase => { if (phase == "enter") entered.TrySetResult(); };
        var input = dispatcher.DispatchAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Input,
            JsonSerializer.SerializeToElement(new { sessionId = Guid.NewGuid(), input = "x" }, PhoneHomeFraming.Json)), ct);
        await runtime.InputStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        var request = new PhoneHomeWorkspaceMirrorRequest(BranchA, scratch.ShaA, "task-aaaaaaaa",
            Binding: new PhoneHomeWorkspaceMirrorBinding(TaskA, 1, Guid.NewGuid(), "runner-a", storeId.ToString("D")),
            Slot: true, Cold: false);
        var mirror = dispatcher.DispatchAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.WorkspaceMirror,
            JsonSerializer.SerializeToElement(request, PhoneHomeFraming.Json)), ct);
        // Unlocked, the pool is entered within milliseconds (empty ledger); 2 s is the observation window.
        await Task.WhenAny(entered.Task, mirror, Task.Delay(TimeSpan.FromSeconds(2), ct));
        entered.Task.IsCompleted.ShouldBeFalse("the lease entered the pool while the mutation lock was held");
        mirror.IsCompleted.ShouldBeFalse("the lease replied while the mutation lock was held");
        pool.Ledger().Slots.ShouldBeEmpty("no slot is Leasing before the lock is released");
        input.IsCompleted.ShouldBeFalse();

        runtime.InputGate.SetResult();
        (await input.WaitAsync(TimeSpan.FromSeconds(5), ct)).Kind.ShouldBe(PhoneHomeFrameKind.Result);
        var reply = await mirror.WaitAsync(TimeSpan.FromSeconds(8), ct);
        reply.Kind.ShouldBe(PhoneHomeFrameKind.Result, reply.ErrorDetail);
        reply.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorResponse>(PhoneHomeFraming.Json)!.SlotId.ShouldBe("ws-01");
        pool.Ledger().Slot("ws-01")!.State.ShouldBe(WorkspaceSlotStates.Leased);
    }

    private static PhoneHomeCommandDispatcher SlotDispatcher(C0824SlotScratch scratch, RunnerWorkspaceService service,
        IPhoneHomeRuntimeSurface runtime, Guid storeId)
    {
        var storePath = scratch.Root + "/store-id";
        File.WriteAllText(storePath, storeId.ToString("D"));
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            RunnerId = "runner-a",
            AllowedCwd = scratch.Work,
            RunnerRepository = scratch.Clone.Replace('\\', '/'),
            Capacity = 2,
            CapacityStatePath = scratch.Root.Replace('\\', '/') + "/capacity",
            StoreIdPath = storePath,
            WorkspaceSlots = Settings(),
        });
        dispatcher.UseWorkspace(service);
        return dispatcher;
    }

    [Test]
    public async Task C0824_RestartKeepsLeasedSlotForTheServersRelease(CancellationToken ct)
    {
        // D-3 (review 3a354688) recovery action. A plain restart (RecoverAsync(null)) cannot tell a terminal task
        // from a queued one, so it KEEPS the lease, untouched; the server's later release (reconciler or drain
        // re-arm) settles it on this same runner after the restart.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        Plant(a.Path, "src/P/bin/Debug/a.dll", "native");
        var before = Tree(a.Path);

        var restarted = scratch.Pool();
        await restarted.StartAsync(ct);
        var kept = restarted.Ledger().Slot(a.SlotId).ShouldNotBeNull();
        kept.State.ShouldBe(WorkspaceSlotStates.Leased);
        kept.TaskId.ShouldBe(TaskA);
        kept.LeaseGeneration.ShouldBe(a.Generation);
        Tree(a.Path).ShouldBe(before);

        (await Release(restarted, a, TaskA)).Outcome.ShouldBe("released");
        restarted.Ledger().Slot(a.SlotId)!.State.ShouldBe(WorkspaceSlotStates.Free);
    }
}

internal sealed class C0824StoreRuntime(Guid storeId) : IPhoneHomeRuntimeSurface
{
    public Guid RunnerStoreId { get; } = storeId;
    public int OwnedSessionCount => 0;
    public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "test", false);
    public string Health() => "Healthy";
    public IReadOnlyList<RunnerSessionDto> List() => [];
    public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
    public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
    public RunnerBufferDto GetBuffer(Guid sessionId) => throw new NotSupportedException();
    public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
    public RunnerTranscriptDto GetTranscript(Guid sessionId) => throw new NotSupportedException();
    public TaskCompletionSource? InputGate { get; init; }
    public TaskCompletionSource InputStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        if (InputGate is not { } gate)
            throw new NotSupportedException();
        InputStarted.TrySetResult();
        await gate.Task.WaitAsync(ct);
    }

    public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
    public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) => throw new NotSupportedException();
}
