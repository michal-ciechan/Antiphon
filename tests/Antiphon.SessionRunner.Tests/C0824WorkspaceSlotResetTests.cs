using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C0824SlotScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0824 S1 CP-1. Lease to the exact tree, the warm keep-list, and the Free slot shape.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(30_000)]
public sealed class C0824WorkspaceSlotResetTests
{
    [Test]
    public async Task C0824_LeaseResetsToExactShaFromUnrelatedState(CancellationToken ct)
    {
        // V-1. A's lease leaves its own branch, a pushed commit and ignored residue; B's lease must be
        // exactly B. (A tracked or untracked dirty tree never reaches a lease: V-4 and V-6b quarantine it.)
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        Plant(a.Path, "a2.txt", "pushed by A");
        Git(a.Path, "add", "a2.txt");
        Git(a.Path, "commit", "-m", "A work");
        Git(a.Path, "push", "origin", BranchA);
        Plant(a.Path, "notes.log", "ignored residue");
        (await Release(pool, a, TaskA)).Outcome.ShouldBe("released");

        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        b.Path.ShouldBe(scratch.Slot("ws-01"));
        Git(b.Path, "rev-parse", "HEAD").Trim().ShouldBe(scratch.ShaB);
        Git(b.Path, "symbolic-ref", "HEAD").Trim().ShouldBe("refs/heads/" + BranchB);
        Git(b.Path, "status", "--porcelain=v1", "-z", "--untracked-files=all").ShouldBeEmpty();
        File.Exists(Path.Combine(b.Path, "a2.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(b.Path, "notes.log")).ShouldBeFalse();
    }

    [Test]
    public async Task C0824_VerifyQuarantinesSilentCheckout(CancellationToken ct)
    {
        // V-1b. A checkout that exits 0 and does nothing must never be handed out.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        await Release(pool, a, TaskA);
        scratch.Faults.Add(new GitFault((dir, args) => dir == a.Path && args is ["checkout", "--force", "-B", ..], "", 1, 0));

        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        b.Path.ShouldNotBe(a.Path, "returned directory != quarantined one");
        Git(b.Path, "rev-parse", "HEAD").Trim().ShouldBe(scratch.ShaB);
        var quarantined = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem();
        quarantined.Path.ShouldBe(a.Path);
        Directory.Exists(a.Path).ShouldBeTrue();
    }

    [Test]
    public async Task C0824_WarmResetWipesEveryNonKeepListClass(CancellationToken ct)
    {
        // V-2. Every wiped class planted by A is gone for B; the accepted Q-3 residue survives.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        var gitDir = GitDir(a.Path);
        Plant(a.Path, ".claude/settings.local.json", "{\"deny\":true}");
        File.AppendAllText(Path.Combine(scratch.Clone, ".git", "info", "exclude"), "\n.claude/settings.local.json\n");
        Plant(a.Path, ".antiphon/inbox/m.md", "inbox");
        Plant(a.Path, ".antiphon/checkpoints/x/report.json", "{}");
        Plant(a.Path, "bin-c1/x", "alt output");
        Plant(a.Path, "src/P/bin-ws/y", "alt output");
        Directory.CreateDirectory(Path.Combine(a.Path, "nested"));
        Git(Path.Combine(a.Path, "nested"), "init", "-q");
        Plant(a.Path, "nested/n.txt", "nested repo");
        Git(scratch.Clone, "config", "extensions.worktreeConfig", "true");
        Git(a.Path, "config", "--worktree", "core.fileMode", "false");
        File.Exists(Path.Combine(gitDir, "config.worktree")).ShouldBeTrue();
        // An empty cherry-pick leaves CHERRY_PICK_HEAD on a clean tree.
        GitExit(a.Path, "cherry-pick", scratch.ShaA).Exit.ShouldNotBe(0);
        File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")).ShouldBeTrue();
        Git(a.Path, "update-ref", "refs/worktree/x", "HEAD");
        Git(a.Path, "update-ref", "refs/bisect/bad", "HEAD");
        Plant(a.Path, ".playwright/state", "pw");
        Plant(a.Path, "TestResults/t.trx", "trx");
        Plant(a.Path, "run.log", "log");
        Plant(a.Path, "src/P/bin/notes.txt", "inside the keep-list");
        (await Release(pool, a, TaskA)).Outcome.ShouldBe("released");

        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        b.Path.ShouldBe(a.Path);
        foreach (var gone in new[] { ".claude/settings.local.json", ".antiphon", "bin-c1", "src/P/bin-ws", "nested", ".playwright", "TestResults", "run.log" })
            Path.Exists(Path.Combine(b.Path, gone)).ShouldBeFalse(gone + " absent");
        File.Exists(Path.Combine(GitDir(b.Path), "config.worktree")).ShouldBeFalse("config.worktree absent");
        File.Exists(Path.Combine(GitDir(b.Path), "CHERRY_PICK_HEAD")).ShouldBeFalse("CHERRY_PICK_HEAD absent");
        GitExit(b.Path, "rev-parse", "-q", "--verify", "refs/worktree/x").Exit.ShouldNotBe(0, "refs/worktree/x absent");
        GitExit(b.Path, "rev-parse", "-q", "--verify", "refs/bisect/bad").Exit.ShouldNotBe(0, "refs/bisect/bad absent");
        File.ReadAllText(Path.Combine(b.Path, "src/P/bin/notes.txt")).ShouldBe("inside the keep-list", "Q-3 accepted residue");
    }

    [Test]
    public async Task C0824_WarmResetKeepsNativeOutputByteAndMtime(CancellationToken ct)
    {
        // V-27. Native output survives a warm reset byte for byte with its mtime; no OutputPath anywhere.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        var kept = new[] { "src/P/bin/Debug/a.dll", "src/P/obj/project.assets.json", "node_modules/x/i", "client/dist/i.js" };
        foreach (var path in kept)
            Plant(a.Path, path, "native " + path, OldMtime);
        var before = Tree(a.Path);
        await Release(pool, a, TaskA);
        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        var after = Tree(b.Path);
        foreach (var path in kept)
            after.GetValueOrDefault(path).ShouldBe(before[path], path);
        lock (scratch.Argv)
        {
            scratch.Argv.SelectMany(argv => argv.Skip(1))
                .Any(arg => arg.Contains("OutputPath", StringComparison.OrdinalIgnoreCase) || arg == "-o" || arg.Contains("bin-ws", StringComparison.Ordinal))
                .ShouldBeFalse();
        }
    }

    [Test]
    public async Task C0824_FreeSlotDetachedAtPublishedShaWithNoTaskRefs(CancellationToken ct)
    {
        // V-11. A Free slot is detached at the published tip with no reflog and no task refs.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        await Release(pool, a, TaskA);
        GitExit(a.Path, "symbolic-ref", "-q", "HEAD").Exit.ShouldBe(1);
        Git(a.Path, "rev-parse", "HEAD").Trim().ShouldBe(scratch.Base);
        GitExit(a.Path, "merge-base", "--is-ancestor", "HEAD", "refs/remotes/origin/main").Exit.ShouldBe(0);
        GitExit(a.Path, "reflog", "show", "--format=%H", "HEAD").Stdout.Trim().ShouldBeEmpty();
        GitExit(a.Path, "rev-parse", "-q", "--verify", "refs/heads/" + BranchA).Exit.ShouldNotBe(0);
        GitExit(a.Path, "rev-parse", "-q", "--verify", "refs/remotes/origin/" + BranchA).Exit.ShouldNotBe(0);
        var admin = Directory.EnumerateFiles(GitDir(a.Path)).Select(Path.GetFileName).ToHashSet();
        admin.ShouldContain(RunnerWorkspaceBindingStore.OwnerStampFileName);
        admin.Except(["HEAD", "ORIG_HEAD", "commondir", "gitdir", "index", RunnerWorkspaceBindingStore.OwnerStampFileName]).ShouldBeEmpty();
        var slot = pool.Ledger().Slot("ws-01").ShouldNotBeNull();
        slot.State.ShouldBe(WorkspaceSlotStates.Free);
    }

    [Test]
    public async Task C0824_EvidenceRetainedBeforeWipe(CancellationToken ct)
    {
        // V-38. Evidence classes are copied byte-identically before the clean, within the cap.
        using var scratch = Create();
        var pool = scratch.Pool(Settings(settings => settings.EvidenceCapBytes = 1024));
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        Plant(a.Path, ".antiphon/reports/r.md", "report");
        Plant(a.Path, ".antiphon/checkpoints/c1/report.json", "{\"ok\":true}");
        Plant(a.Path, ".antiphon/inbox/big.bin", new string('b', 4096));
        Plant(a.Path, ".antiphon/reports/large.md", new string('L', 2048));
        var retained = pool.RetainedRoot + "/" + TaskA.ToString("N") + "/" + a.Generation;
        var seenBeforeClean = false;
        pool.PhaseCut = phase =>
        {
            if (phase == "before-clean")
                seenBeforeClean = File.Exists(retained + "/.antiphon/reports/r.md")
                                  && File.Exists(retained + "/.antiphon/checkpoints/c1/report.json");
        };
        await Release(pool, a, TaskA);
        seenBeforeClean.ShouldBeTrue("retained before the clean");
        File.ReadAllText(retained + "/.antiphon/reports/r.md").ShouldBe("report");
        File.ReadAllText(retained + "/.antiphon/checkpoints/c1/report.json").ShouldBe("{\"ok\":true}");
        File.Exists(retained + "/.antiphon/inbox/big.bin").ShouldBeFalse();
        File.Exists(retained + "/.antiphon/reports/large.md").ShouldBeFalse();
        pool.Ledger().Attention.ShouldContain(item => item.Key == WorkspaceSlotCodes.EvidenceTruncated);
    }

    [Test]
    public async Task C0824_ResetNeverTouchesOutsideSlot(CancellationToken ct)
    {
        // V-44. Siblings of the slot (another slot, a legacy mirror, a temp root) are untouched.
        using var scratch = Create();
        var pool = scratch.Pool();
        Plant(scratch.Worktrees + "/ws-02", "keep.txt", "other slot");
        Plant(scratch.Worktrees + "/task-deadbeef", "x", "legacy");
        Plant(scratch.Work + "/tmp-c723-x", "y", "temp root");
        var before = Tree(scratch.Work + "/worktrees/ws-02").Concat(Tree(scratch.Work + "/worktrees/task-deadbeef")).Concat(Tree(scratch.Work + "/tmp-c723-x")).ToList();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        a.Path.ShouldBe(scratch.Slot("ws-01"));
        await Release(pool, a, TaskA);
        await pool.LeaseAsync(scratch.RequestB(), ct);
        var after = Tree(scratch.Work + "/worktrees/ws-02").Concat(Tree(scratch.Work + "/worktrees/task-deadbeef")).Concat(Tree(scratch.Work + "/tmp-c723-x")).ToList();
        after.ShouldBe(before);
    }

    [Test]
    public async Task C0824_PoolBoundedAcrossSequentialAndConcurrentLeases(CancellationToken ct)
    {
        // V-45. Capacity 2 + headroom 2: ten sequential cycles and four concurrent leases use at most four directories.
        using var scratch = Create();
        var pool = scratch.Pool();
        for (var i = 0; i < 10; i++)
        {
            var lease = await pool.LeaseAsync(i % 2 == 0 ? scratch.RequestA() : scratch.RequestB(), ct);
            (await Release(pool, lease, i % 2 == 0 ? TaskA : TaskB)).Outcome.ShouldBe("released");
        }

        var tasks = new[] { (TaskA, BranchA, scratch.ShaA), (TaskB, BranchB, scratch.ShaB), (TaskC, BranchC, scratch.ShaC) }
            .Select(item => pool.LeaseAsync(Request(item.Item1, item.Item2, item.Item3), ct)).ToList();
        Git(scratch.Origin, "branch", "feat/card-task-dddddddd", scratch.Base);
        tasks.Add(pool.LeaseAsync(Request(Guid.Parse("dddddddd-0000-4000-8000-000000000004"), "feat/card-task-dddddddd", scratch.Base), ct));
        var leases = await Task.WhenAll(tasks);
        leases.Select(lease => lease.SlotId).Distinct().Count().ShouldBe(4);
        var directories = Directory.EnumerateDirectories(scratch.Worktrees).Select(Path.GetFileName).ToList();
        directories.Count(name => name!.StartsWith("ws-", StringComparison.Ordinal)).ShouldBeLessThanOrEqualTo(4);
        directories.Count(name => name!.StartsWith("task-", StringComparison.Ordinal)).ShouldBe(0);
    }

    [Test]
    public async Task C0824_ReviewLeaseIsExactTreeNeverSubjectBranch(CancellationToken ct)
    {
        // V-46. A Review task leases its own branch at the subject sha; naming the leased subject branch is refused.
        using var scratch = Create();
        var pool = scratch.Pool();
        var subject = await pool.LeaseAsync(scratch.RequestA(), ct);
        Git(scratch.Origin, "branch", "feat/card-task-eeeeeeee", scratch.ShaA);
        var reviewTask = Guid.Parse("eeeeeeee-0000-4000-8000-000000000005");
        var review = await pool.LeaseAsync(Request(reviewTask, "feat/card-task-eeeeeeee", scratch.ShaA), ct);
        review.Path.ShouldNotBe(subject.Path);
        Git(review.Path, "rev-parse", "HEAD").Trim().ShouldBe(scratch.ShaA);
        Git(review.Path, "symbolic-ref", "HEAD").Trim().ShouldBe("refs/heads/feat/card-task-eeeeeeee");
        Git(review.Path, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignored=matching").ShouldBeEmpty();
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(() =>
            pool.LeaseAsync(Request(reviewTask, BranchA, scratch.ShaA), ct));
        refusal.Message.ShouldContain(WorkspaceSlotCodes.BranchCheckedOut);
    }

    [Test]
    public async Task C0824_FlagOffRunnerKeepsTaskMirror(CancellationToken ct)
    {
        // V-12r. The flag defaults off; with it off a mirror request is the legacy task-<id> worktree add -B.
        new PhoneHomeWorkspaceSlotSettings().Enabled.ShouldBeFalse();
        new PhoneHomeSettings().WorkspaceSlots.Enabled.ShouldBeFalse();
        using var scratch = Create();
        var service = scratch.Service();
        var result = await service.MirrorAsync(new PhoneHomeWorkspaceMirrorRequest(BranchA, scratch.ShaA, "task-aaaaaaaa"), ct);
        result.Path.ShouldBe(scratch.Worktrees + "/task-aaaaaaaa");
        result.SlotId.ShouldBeNull();
        lock (scratch.Argv)
            scratch.Argv.ShouldContain(argv => argv.Skip(1).SequenceEqual(new[] { "worktree", "add", "-B", BranchA, scratch.Worktrees + "/task-aaaaaaaa", scratch.ShaA }));
        Directory.Exists(scratch.Work + "/workspace-slots").ShouldBeFalse();
        var slotRefused = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest(BranchB, scratch.ShaB, "task-bbbbbbbb",
                Binding: new PhoneHomeWorkspaceMirrorBinding(TaskB, 1, Guid.NewGuid(), "runner-a"), Slot: true), ct));
        slotRefused.Message.ShouldBe(WorkspaceSlotCodes.Disabled);
        Directory.Exists(scratch.Work + "/workspace-slots").ShouldBeFalse();
    }
}
