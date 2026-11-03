using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C0824SlotScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0824 S1 CP-8. Re-homing on injected filesystem failures, the loop guard and quarantine exits.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
[NativeLinuxOnly("CARD-0824 Linux-only by construction: S1 workspace slots are Linux/server2 only and the git fault seam runs /bin/sh")]
public sealed class C0824WorkspaceSlotRehomeTests
{
    private const string Eacces = "warning: failed to remove src/a: Permission denied";

    [Test]
    [Arguments("EACCES")]
    [Arguments("ENAMETOOLONG")]
    [Arguments("EBUSY")]
    public async Task C0824_InjectedFsFailureQuarantinesAndRehomes(string errno, CancellationToken ct)
    {
        // V-34. The old directory is kept, locked and reported; the slot moves to ws-01-d2 under the same id.
        var stderr = errno switch
        {
            "EACCES" => Eacces,
            "ENAMETOOLONG" => "fatal: cannot create directory at 'src/very/long': File name too long",
            _ => "warning: failed to remove bin/x.dll: Device or resource busy",
        };
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        scratch.Faults.Add(GitFault.In(a.Path, "clean", stderr));
        (await Release(pool, a, TaskA)).Outcome.ShouldBe("quarantined");

        Directory.Exists(a.Path).ShouldBeTrue("old directory exists");
        var list = Git(scratch.Clone, "worktree", "list", "--porcelain");
        list.ShouldContain("worktree " + a.Path + "\n");
        list.ShouldContain("locked antiphon quarantine ws-01 filesystem");
        var ledger = pool.Ledger();
        var entry = ledger.QuarantinedDirectories.ShouldHaveSingleItem();
        entry.Path.ShouldBe(a.Path);
        entry.Class.ShouldBe("filesystem");
        entry.Error.ShouldContain(stderr);
        entry.Error.Length.ShouldBeLessThanOrEqualTo(4096);
        ledger.Attention.ShouldContain(item => item.Key == WorkspaceSlotCodes.Rehomed && item.Path == a.Path);
        var slot = ledger.Slot("ws-01").ShouldNotBeNull("slot id ws-01");
        slot.Path.ShouldBe(scratch.Slot("ws-01-d2"), "path ws-01-d2");
        Git(scratch.Slot("ws-01-d2"), "rev-parse", "HEAD").Trim().ShouldBe(scratch.Base);

        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        b.SlotId.ShouldBe("ws-01");
        b.Path.ShouldBe(scratch.Slot("ws-01-d2"));
        b.Generation.ShouldBe(a.Generation + 1);
    }

    [Test]
    public async Task C0824_IdentityBrokenDirectoryRehomes(CancellationToken ct)
    {
        // V-34b. A slot whose .git file vanished (CARD-1448 shape) is quarantined at lease time.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        await Release(pool, a, TaskA);
        File.Delete(Path.Combine(a.Path, ".git"));
        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        b.Path.ShouldNotBe(a.Path);
        var entry = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem("quarantined");
        entry.Path.ShouldBe(a.Path);
        entry.Class.ShouldBe("filesystem");
        Directory.Exists(a.Path).ShouldBeTrue();
    }

    [Test]
    public async Task C0824_SharingViolationViaFileSystemSeam(CancellationToken ct)
    {
        // V-34c. A sharing violation on the evidence copy re-homes the slot; the source stays in place.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        Plant(a.Path, ".antiphon/reports/r.md", "evidence");
        scratch.FileSystem.CopyFault = path => path.EndsWith(".antiphon/reports/r.md", StringComparison.Ordinal)
            ? new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020))
            : null;
        (await Release(pool, a, TaskA)).Outcome.ShouldBe("quarantined");
        pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem().Class.ShouldBe("filesystem");
        File.ReadAllText(Path.Combine(a.Path, ".antiphon/reports/r.md")).ShouldBe("evidence");
        pool.Ledger().Slot("ws-01")!.Path.ShouldBe(scratch.Slot("ws-01-d2"));
    }

    [Test]
    public async Task C0824_ReplacementFailureMarksOnlySlotFailed(CancellationToken ct)
    {
        // V-35a. Both replacement directories fail: only ws-01 is Failed; a base slot and an overflow still lease.
        using var scratch = Create();
        var pool = scratch.Pool(Settings(settings => settings.Headroom = 0), capacity: 1);
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        scratch.Faults.Add(GitFault.In(a.Path, "clean", Eacces));
        foreach (var leaf in new[] { "ws-01-d2", "ws-01-d3" })
        {
            var target = scratch.Slot(leaf);
            scratch.Faults.Add(new GitFault((_, args) => args is ["worktree", "add", ..] && args.Contains(target),
                "fatal: could not create directory of '" + target + "': Permission denied"));
        }

        (await Release(pool, a, TaskA)).Outcome.ShouldBe("quarantined");
        var ledger = pool.Ledger();
        ledger.Slot("ws-01")!.State.ShouldBe(WorkspaceSlotStates.Failed);
        ledger.Attention.ShouldContain(item => item.Key == WorkspaceSlotCodes.RehomeFailed && item.SlotId == "ws-01");
        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        b.SlotId.ShouldBe("ws-02", "ws-02 leases");
        var c = await pool.LeaseAsync(scratch.RequestC(), ct);
        c.SlotId.ShouldBe("ws-03");
        pool.Ledger().Slot("ws-03")!.CreatedAs.ShouldBe("overflow");
    }

    [Test]
    public async Task C0824_RehomeLoopGuardRetiresSlot(CancellationToken ct)
    {
        // V-35b. More than MaxRehomesPerSlotPerDay re-homes in 24 h retire the slot id.
        using var scratch = Create();
        var pool = scratch.Pool();
        for (var i = 0; i < 4; i++)
        {
            var lease = await pool.LeaseAsync(scratch.RequestA(), ct);
            lease.SlotId.ShouldBe("ws-01");
            scratch.Faults.Add(GitFault.In(lease.Path, "clean", Eacces));
            (await Release(pool, lease, TaskA)).Outcome.ShouldBe("quarantined");
            scratch.Clock.Advance(TimeSpan.FromHours(1));
        }

        var ledger = pool.Ledger();
        ledger.Slot("ws-01")!.State.ShouldBe(WorkspaceSlotStates.Retired, "the slot retired");
        ledger.Attention.ShouldContain(item => item.Key == WorkspaceSlotCodes.Retired && item.SlotId == "ws-01");
        var next = await pool.LeaseAsync(scratch.RequestB(), ct);
        next.SlotId.ShouldNotBe("ws-01");
    }

    [Test]
    public async Task C0824_QuarantinedDirectoryBoundHoldsPool(CancellationToken ct)
    {
        // V-51. Five open quarantined directories (more than four) hold the pool.
        using var scratch = Create();
        var pool = scratch.Pool(Settings(settings => settings.MaxRehomesPerSlotPerDay = 10));
        for (var i = 0; i < 5; i++)
        {
            var lease = await pool.LeaseAsync(scratch.RequestA(), ct);
            scratch.Faults.Add(GitFault.In(lease.Path, "clean", Eacces));
            (await Release(pool, lease, TaskA)).Outcome.ShouldBe("quarantined");
        }

        pool.Ledger().QuarantinedDirectories.Count(entry => entry.IsOpen).ShouldBe(5);
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => pool.LeaseAsync(scratch.RequestB(), ct));
        refusal.Message.ShouldBe(WorkspaceSlotCodes.PoolDegraded);
    }

    [Test]
    [Arguments("Pushed")]
    [Arguments("AcceptLoss")]
    [Arguments("Removed")]
    public async Task C0824_QuarantineReleaseDispositions(string disposition, CancellationToken ct)
    {
        // V-52. Pushed needs the work published; AcceptLoss retains the diff first; Removed retries the removal.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        switch (disposition)
        {
            case "Pushed":
                Plant(a.Path, "unpushed.txt", "work");
                Git(a.Path, "add", "unpushed.txt");
                Git(a.Path, "commit", "-m", "unpushed");
                break;
            case "AcceptLoss":
                File.WriteAllText(Path.Combine(a.Path, "a.txt"), "modified by A");
                Plant(a.Path, "untracked.txt", "new");
                break;
            default:
                scratch.Faults.Add(GitFault.In(a.Path, "clean", Eacces));
                break;
        }

        (await Release(pool, a, TaskA)).Outcome.ShouldBe("quarantined");
        var dirId = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem().DirId;
        PhoneHomeWorkspaceSlotQuarantineReleaseRequest Ask() => new(dirId, disposition, "operator checked");
        if (disposition == "Pushed")
        {
            (await pool.ReleaseQuarantineAsync(Ask(), ct)).Outcome.ShouldBe("refused");
            Directory.Exists(a.Path).ShouldBeTrue();
            Git(a.Path, "push", "origin", BranchA);
        }

        var retained = pool.RetainedRoot + "/quarantine/" + dirId;
        var patchBeforeRemoval = false;
        pool.PhaseCut = phase =>
        {
            if (phase == "before-quarantine-removal")
                patchBeforeRemoval = File.Exists(retained + "/diff.patch") && Directory.Exists(a.Path);
        };
        if (disposition == "Removed")
        {
            scratch.Faults.Add(new GitFault((_, args) => args is ["worktree", "remove", ..], "error: failed to delete: Permission denied"));
            (await pool.ReleaseQuarantineAsync(Ask(), ct)).Outcome.ShouldBe("removal_failed");
            pool.Ledger().QuarantinedDirectories.Single().IsOpen.ShouldBeTrue();
        }

        (await pool.ReleaseQuarantineAsync(Ask(), ct)).Outcome.ShouldBe("removed");
        Directory.Exists(a.Path).ShouldBeFalse();
        var entry = pool.Ledger().QuarantinedDirectories.Single();
        entry.IsOpen.ShouldBeFalse();
        entry.Disposition.ShouldBe(disposition);
        if (disposition == "AcceptLoss")
        {
            patchBeforeRemoval.ShouldBeTrue("the patch exists before the removal");
            File.ReadAllText(retained + "/diff.patch").ShouldContain("modified by A");
            File.ReadAllText(retained + "/untracked.txt").ShouldContain("untracked.txt");
        }
    }

    [Test]
    [Arguments("pushed")]
    [Arguments("Bogus")]
    [Arguments("")]
    public async Task C0824_QuarantineReleaseUnknownDispositionRefused(string disposition, CancellationToken ct)
    {
        // CARD-0824 S1 mutation survivor X31. Quarantine never deletes without a known disposition: a wrongly cased or
        // unknown one is refused before any git runs, and the entry, its lock and the directory's bytes all stay.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        Plant(a.Path, "dirty.txt", "only copy");
        (await Release(pool, a, TaskA)).Outcome.ShouldBe("quarantined");
        var dirId = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem().DirId;
        var before = Tree(a.Path);
        int mark;
        lock (scratch.Argv)
            mark = scratch.Argv.Count;

        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(() =>
            pool.ReleaseQuarantineAsync(new PhoneHomeWorkspaceSlotQuarantineReleaseRequest(dirId, disposition, "operator typo"), ct));
        refusal.Message.ShouldBe("Disposition must be Pushed, AcceptLoss or Removed.");
        lock (scratch.Argv)
            scratch.Argv.Skip(mark).ShouldBeEmpty("no git ran for a refused disposition");
        Directory.Exists(a.Path).ShouldBeTrue();
        Tree(a.Path).ShouldBe(before);
        var entry = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem();
        entry.IsOpen.ShouldBeTrue();
        entry.Disposition.ShouldBeNull();
        var list = Git(scratch.Clone, "worktree", "list", "--porcelain");
        list.ShouldContain("worktree " + a.Path + "\n");
        list.ShouldContain("locked antiphon quarantine ws-01 custody dirty", Case.Sensitive, "the quarantine lock stays");
    }

    [Test]
    public async Task C0824_UnpushedCheckFailureQuarantinesNotResets(CancellationToken ct)
    {
        // CARD-0824 S1 mutation survivor X11. The unpushed check fails closed: when `rev-list ... --not --remotes=origin`
        // fails, the release cannot prove the lease's commits are on origin, so it quarantines instead of resetting
        // away the task branch ref and the HEAD reflog that hold an unpushed commit.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        Plant(a.Path, "unpushed.txt", "only copy");
        Git(a.Path, "add", "unpushed.txt");
        Git(a.Path, "commit", "-q", "-m", "unpushed");
        var head = Git(a.Path, "rev-parse", "HEAD").Trim();
        var before = Tree(a.Path);
        var fault = GitFault.In(a.Path, "rev-list", "fatal: unable to read refs/remotes/origin");
        scratch.Faults.Add(fault);

        (await Release(pool, a, TaskA)).Outcome.ShouldBe("quarantined");
        fault.Hits.ShouldBe(1);
        var entry = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem();
        entry.Path.ShouldBe(a.Path);
        entry.Class.ShouldBe("custody");
        entry.Reason.ShouldBe("unpushed");
        entry.Error.ShouldStartWith("unpushed check failed: ");
        Tree(a.Path).ShouldBe(before);
        Git(a.Path, "rev-parse", "HEAD").Trim().ShouldBe(head);
        Git(a.Path, "rev-parse", "refs/heads/" + BranchA).Trim().ShouldBe(head, "the task branch ref is kept");
        Git(a.Path, "reflog", "show", "--format=%H", "HEAD").ShouldContain(head, Case.Sensitive, "the HEAD reflog is kept");
    }

    [Test]
    [Arguments("primary")]
    [Arguments("sibling")]
    [Arguments("symlink")]
    public async Task C0824_ReleaseProvesOwnershipBeforeAnyDestructiveGit(string pointer, CancellationToken ct)
    {
        // D-1 (review 3a354688). A released slot whose .git names the PRIMARY .git or a SIBLING slot's registration,
        // or whose directory is a link to a sibling, is quarantined before any checkout, clean, update-ref or file
        // delete: the other repository's HEAD, reflog, refs and files are byte-identical afterwards.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.Extra(1), ct);
        var victimTree = scratch.Clone;
        var victimGitDir = scratch.Clone + "/.git";
        WorkspaceSlotLease? b = null;
        if (pointer != "primary")
        {
            b = await pool.LeaseAsync(scratch.Extra(2), ct);
            victimTree = b.Path;
            victimGitDir = GitDir(b.Path);
        }

        var slotTree = a.Path;
        if (pointer == "symlink")
        {
            slotTree = scratch.Root + "/aside-ws-01";
            Directory.Move(a.Path, slotTree);
            Directory.CreateSymbolicLink(a.Path, b!.Path);
        }
        else
        {
            File.WriteAllText(a.Path + "/.git", "gitdir: " + victimGitDir + "\n");
        }

        var dotGit = File.ReadAllText(slotTree + "/.git");
        var victim = Snapshot(scratch, victimGitDir, victimTree);
        var slotBefore = Tree(slotTree);

        (await Release(pool, a, ExtraTask(1))).Outcome.ShouldBe("quarantined");

        Snapshot(scratch, victimGitDir, victimTree).ShouldBe(victim, "the " + pointer + " repository is untouched");
        Tree(slotTree).ShouldBe(slotBefore, "the slot's own bytes are untouched");
        File.ReadAllText(slotTree + "/.git").ShouldBe(dotGit);
        var entry = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem();
        entry.Path.ShouldBe(a.Path);
        entry.Class.ShouldBe("filesystem");
        entry.Reason.ShouldBe("identity");
        if (b is not null)
            pool.Ledger().Slot(b.SlotId)!.State.ShouldBe(WorkspaceSlotStates.Leased, "the sibling's lease is untouched");
    }

    /// <summary>HEAD, the HEAD reflog, every ref of the shared repository and the working files of one git directory.</summary>
    private static string Snapshot(C0824SlotScratch scratch, string gitDir, string tree)
    {
        static string Bytes(string path) => File.Exists(path) ? Convert.ToHexString(File.ReadAllBytes(path)) : "<absent>";
        return string.Join("\n",
            "HEAD " + Bytes(gitDir + "/HEAD"),
            "reflog " + Bytes(gitDir + "/logs/HEAD"),
            "refs " + Git(scratch.Clone, "for-each-ref", "--format=%(refname) %(objectname)"),
            "files " + string.Join(";", Tree(tree).Select(item => item.Key + "=" + item.Value)));
    }
}
