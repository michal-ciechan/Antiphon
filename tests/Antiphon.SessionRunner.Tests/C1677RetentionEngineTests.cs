using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C1677RetentionScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1677 S1 CP-1 (portable). The runner retention engine over real git: a scratch bare origin, a runner
/// repository and <c>task-&lt;8 hex&gt;</c> mirrors under a temp worktree root. Every refusal case violates
/// exactly one condition and asserts the exact reason, the untouched tree, its registration, an empty
/// retiring root and no git mutation (F-1 <see cref="C1677RetentionScratch.Kept"/>).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(15_000)]
public sealed class C1677RetentionEngineTests
{
    private const string Execute = WorkspaceRetainModes.Execute;

    [Test]
    public async Task Clean_pushed_old_mirror_is_removed_without_force_or_prune(CancellationToken ct)
    {
        // V-1 (G-23, G-32, G-33). The positive path: moved aside, removed without force, nothing pruned.
        using var s = Create();
        var stale = s.StaleRegistration("a16770ff");
        var m = s.Mirror();
        var request = Request(m);
        var response = await s.Engine().RetainAsync(request, ct);

        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Removed, response.Detail);
        Directory.Exists(m.Path).ShouldBeFalse();
        s.Registered(m.Path).ShouldBeFalse();
        s.RetiringEntries().ShouldBeEmpty();
        var moved = s.Retiring + "/task-a1677001." + request.RequestId.ToString("N")[..8];
        var move = s.Calls("worktree", "move").ShouldHaveSingleItem();
        move.Cwd.ShouldBe(s.Repository);
        move.Args.ShouldBe(["worktree", "move", m.Path, moved]);
        var remove = s.Calls("worktree", "remove").ShouldHaveSingleItem();
        remove.Args.ShouldBe(["worktree", "remove", moved]);
        remove.Args.ShouldNotContain("--force");
        remove.Args.ShouldNotContain("-f");
        s.Calls("worktree", "prune").ShouldBeEmpty();
        s.Calls("fetch").ShouldBeEmpty();
        s.Calls("branch").ShouldBeEmpty();
        s.Calls("rm").ShouldBeEmpty();
        s.Registered(stale).ShouldBeTrue("no prune ran, so the stale registration is still listed");
        response.HeadSha.ShouldBe(m.Head);
        response.OriginTip.ShouldBe(m.Head);
        response.PhaseAMs.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(0);
        response.PhaseBMs.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(0);
        s.Calls("ls-remote").ShouldAllBe(call => call.Args.SequenceEqual(new[]
            { "ls-remote", "--refs", "--", s.OriginGit, "refs/heads/feat/card-task-a1677001" }));
        s.Calls("ls-remote").Count.ShouldBe(2, "the dry pass and the locked recheck each read live origin");
    }

    [Test]
    [Arguments("outside-root")]
    [Arguments("nested-under-root")]
    [Arguments("symlinked-leaf")]
    [Arguments("leaf-id-mismatch")]
    public async Task Confinement_refuses_outside_scope(string shape, CancellationToken ct)
    {
        // V-9 (G-1, G-2, G-3). Every other fact is eligible: clean, pushed, branch matches, no holder.
        using var s = Create();
        MirrorTree m;
        PhoneHomeWorkspaceRetainRequest request;
        string? link = null;
        switch (shape)
        {
            case "outside-root":
                m = s.Mirror(path: s.Work + "/elsewhere/task-a1677001");
                request = Request(m);
                break;
            case "nested-under-root":
                m = s.Mirror(path: s.Worktrees + "/sub/task-a1677001");
                request = Request(m);
                break;
            case "symlinked-leaf":
                m = s.Mirror(path: s.Work + "/real/task-a1677001");
                link = s.Worktrees + "/task-a1677001";
                File.CreateSymbolicLink(link, m.Path);
                request = Request(m, path: link);
                break;
            default:
                m = s.Mirror("b1677002");
                request = Request(m, taskId: TaskOf("a1677001"), branch: "feat/card-task-b1677002");
                break;
        }

        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(request, ct);
        s.Kept(response, WorkspaceRetentionCodes.OutsideScope, m, before);
        if (link is not null)
            new FileInfo(link).LinkTarget.ShouldNotBeNull("the link itself is untouched");
    }

    [Test]
    [Arguments("ws-leaf")]
    [Arguments("ledger-path")]
    public async Task Slot_paths_are_refused(string shape, CancellationToken ct)
    {
        // V-10 (G-5 by reason, G-6 on survival: only the ledger names this task-<id8> path).
        using var s = Create();
        MirrorTree m;
        if (shape == "ws-leaf")
            m = s.Mirror(path: s.Worktrees + "/ws-03", branch: "feat/card-task-a1677001");
        else
        {
            m = s.Mirror();
            new RunnerWorkspaceSlotLedger(s.Work + "/workspace-slots").Write(new WorkspaceSlotLedgerState
            {
                Slots = [new WorkspaceSlotRecord { Id = "ws-01", Number = 1, Path = m.Path, State = WorkspaceSlotStates.Free }],
            });
        }

        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(Request(m, taskId: TaskOf("a1677001")), ct);
        s.Kept(response, WorkspaceRetentionCodes.SlotManaged, m, before);
    }

    [Test]
    public async Task Unreadable_slot_ledger_keeps_a_removable_mirror(CancellationToken ct)
    {
        // CARD-0824 S1 mutation survivor X47 (CARD-1677 guard 3b). The mirror is otherwise removable (clean, pushed,
        // its own task-<id8> leaf), but an unreadable slot ledger cannot prove the path is not a slot: kept, slot_managed.
        using var s = Create();
        var m = s.Mirror();
        var ledger = new RunnerWorkspaceSlotLedger(s.Work + "/workspace-slots");
        Directory.CreateDirectory(ledger.StateRoot);
        File.WriteAllText(ledger.LedgerPath, "{ \"slots\": [ not json");
        var before = Tree(m.Path);

        var response = await s.Engine().RetainAsync(Request(m), ct);
        s.Kept(response, WorkspaceRetentionCodes.SlotManaged, m, before);
        response.Detail.ShouldBe("slot ledger unreadable");
    }

    [Test]
    [Arguments("use-owner")]
    [Arguments("removal-journal")]
    [Arguments("locked")]
    public async Task Ownership_gates_refuse(string shape, CancellationToken ct)
    {
        // V-11 (G-7 where the gate is installed, M-13; G-8 by reason, M-3).
        using var s = Create();
        var m = s.Mirror();
        var expected = WorkspaceRetentionCodes.Locked;
        if (shape == "locked")
            Git(s.Repository, "worktree", "lock", m.Path);
        else
        {
            var journal = new RunnerWorkspaceRemovalJournal(s.Work + "/removal-journal", "store-c1677");
            var gate = new RunnerWorkspaceUseGate(journal);
            await using (await gate.AcquireCensusAsync(ct))
            {
            }
            gate.CompleteCensus();
            s.Service().WorkspaceUseGate = gate;
            if (shape == "use-owner")
            {
                gate.RecordOwner(m.Path, Guid.NewGuid(), () => true);
                expected = WorkspaceRetentionCodes.WorkspaceInUse;
            }
            else
            {
                // An unreadable journal is "retiring or unknown": the gate holds every removal.
                File.WriteAllText(journal.JournalPath, "{ not json");
                expected = WorkspaceRetentionCodes.WorkspaceRetiringOrUnknown;
            }
        }

        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(Request(m), ct);
        s.Kept(response, expected, m, before);
    }

    [Test]
    [Arguments("foreign-repository")]
    [Arguments("other-branch")]
    [Arguments("branch-id-not-leaf")]
    [Arguments("detached")]
    public async Task Repository_and_branch_must_match(string shape, CancellationToken ct)
    {
        // V-24 (G-9, G-10, G-11).
        using var s = Create();
        MirrorTree m;
        PhoneHomeWorkspaceRetainRequest request;
        var expected = WorkspaceRetentionCodes.BranchMismatch;
        switch (shape)
        {
            case "foreign-repository":
            {
                var foreign = s.Root + "/foreign";
                Git(s.Root, "clone", "-q", s.OriginGit, foreign);
                var reference = s.Mirror("a1677009"); // pushes and fetches a branch the foreign clone can see
                Git(foreign, "fetch", "-q", "origin");
                var path = s.Worktrees + "/task-a1677001";
                Git(foreign, "worktree", "add", "-b", "feat/card-task-a1677001", path, "origin/" + reference.Branch);
                Git(foreign, "push", "-q", "origin", "feat/card-task-a1677001");
                m = new MirrorTree(path, "feat/card-task-a1677001", TaskOf("a1677001"), Git(path, "rev-parse", "HEAD").Trim());
                request = Request(m);
                expected = WorkspaceRetentionCodes.NotRunnerRepository;
                var before0 = Tree(m.Path);
                var refused = await s.Engine().RetainAsync(request, ct);
                refused.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept);
                refused.Reason.ShouldBe(expected);
                Tree(m.Path).ShouldBe(before0);
                Git(foreign, "worktree", "list", "--porcelain").ShouldContain("worktree " + path);
                s.RetiringEntries().ShouldBeEmpty();
                s.MutatingVerbs().ShouldBeEmpty();
                return;
            }
            case "other-branch":
                m = s.Mirror(branch: "feat/card-task-a1677001-x");
                request = Request(m, branch: "feat/card-task-a1677001");
                break;
            case "branch-id-not-leaf":
                m = s.Mirror(path: s.Worktrees + "/task-a1677001", branch: "feat/card-task-b1677002");
                request = Request(m, taskId: TaskOf("a1677001"), branch: "feat/card-task-b1677002");
                break;
            default:
                m = s.Mirror();
                Git(m.Path, "checkout", "-q", "--detach", m.Head);
                request = Request(m);
                break;
        }

        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(request, ct);
        s.Kept(response, expected, m, before);
    }

    [Test]
    [Arguments("tracked")]
    [Arguments("untracked-nested-file")]
    [Arguments("non-ignored-nested-repo")]
    [Arguments("status-unavailable")]
    public async Task Dirty_tree_is_kept(string shape, CancellationToken ct)
    {
        // V-2, V-3, V-16b (G-12, G-13, G-14). Git would also refuse phase B, but the data must never move (M-4).
        using var s = Create();
        var m = s.Mirror();
        var expected = WorkspaceRetentionCodes.Dirty;
        switch (shape)
        {
            case "tracked":
                File.WriteAllText(m.Path + "/a", "modified");
                break;
            case "untracked-nested-file":
                Plant(m.Path, "d/f");
                break;
            case "non-ignored-nested-repo":
                Directory.CreateDirectory(m.Path + "/seedrepo");
                Git(m.Path + "/seedrepo", "init", "-q");
                Git(m.Path + "/seedrepo", "-c", "user.email=x@y", "-c", "user.name=x", "commit", "-q", "--allow-empty", "-m", "nested");
                break;
            default:
                s.Faults.Add(RetentionFault.In(m.Path, "status", "boom", remaining: 1));
                expected = WorkspaceRetentionCodes.StatusUnavailable;
                break;
        }

        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(Request(m), ct);
        s.Kept(response, expected, m, before);
        s.Calls("status").ShouldNotBeEmpty();
        s.Calls("status").ShouldAllBe(call => call.Args.SequenceEqual(new[]
            { "status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none" }));
    }

    [Test]
    [Arguments("local-commit")]
    [Arguments("pushed-by-url-tracking-stale")]
    public async Task Unpushed_commit_is_kept(string shape, CancellationToken ct)
    {
        // V-4, V-4b (G-15). In the second case live origin equals HEAD, so only rev-list refuses (M-5).
        using var s = Create();
        var m = s.Mirror();
        if (shape == "local-commit")
            s.CommitIn(m.Path, "local.txt");
        else
            s.PushByUrl(m);

        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(Request(m), ct);
        s.Kept(response, WorkspaceRetentionCodes.Unpushed, m, before);
    }

    [Test]
    [Arguments("deleted-on-origin")]
    [Arguments("decoy-tail-match")]
    [Arguments("origin-unavailable")]
    [Arguments("origin-reset-behind")]
    [Arguments("origin-advanced-unknown-tip")]
    public async Task Live_origin_must_hold_the_branch(string shape, CancellationToken ct)
    {
        // V-5, V-5b, V-5c, V-6, V-6b (G-16..G-21). The tracking ref still contains HEAD in every case.
        using var s = Create();
        var m = s.Mirror();
        string expected;
        switch (shape)
        {
            case "deleted-on-origin":
                s.DeleteOriginBranch(m);
                expected = WorkspaceRetentionCodes.BranchAbsentOnOrigin;
                break;
            case "decoy-tail-match":
                s.DeleteOriginBranch(m);
                s.Decoy(m);
                Git(s.Root, "ls-remote", "--refs", "--", s.OriginGit, "refs/heads/" + m.Branch)
                    .ShouldContain("refs/heads/x/refs/heads/" + m.Branch, Case.Sensitive, "ls-remote tail-matches the decoy");
                expected = WorkspaceRetentionCodes.BranchAbsentOnOrigin;
                break;
            case "origin-unavailable":
                s.Faults.Add(RetentionFault.In(m.Path, "ls-remote", "fatal: unreachable", exitCode: 128));
                expected = WorkspaceRetentionCodes.OriginUnavailable;
                break;
            case "origin-reset-behind":
                s.ResetOriginBehind(m);
                expected = WorkspaceRetentionCodes.OriginDoesNotContainHead;
                break;
            default:
                s.AdvanceOriginFromSide(m);
                expected = WorkspaceRetentionCodes.OriginTipUnknown;
                break;
        }

        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(Request(m), ct);
        s.Kept(response, expected, m, before);
        s.Calls("fetch").ShouldBeEmpty();
    }

    [Test]
    public async Task Ls_remote_is_bounded(CancellationToken ct)
    {
        // V-25 (G-22). A stalled ls-remote is cut at its budget and keeps the tree.
        using var s = Create();
        var m = s.Mirror();
        s.Faults.Add(RetentionFault.Sleep(m.Path, "ls-remote", 8));
        var engine = s.Engine();
        engine.LsRemoteBudget = TimeSpan.FromSeconds(1);
        var before = Tree(m.Path);
        var watch = Stopwatch.StartNew();
        var response = await engine.RetainAsync(Request(m), ct);
        watch.Stop();
        s.Kept(response, WorkspaceRetentionCodes.OriginUnavailable, m, before);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    [Arguments("holder")]
    [Arguments("scan-unknown")]
    public async Task Holder_keeps_the_tree(string shape, CancellationToken ct)
    {
        // V-7, V-8 (G-24, G-25).
        using var s = Create();
        var m = s.Mirror();
        s.Holders.Next = shape == "holder"
            ? _ => new HolderScan(true, [new WorkspaceSlotHolder(4242, "sleep", "cwd")])
            : _ => new HolderScan(false, []);
        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(Request(m), ct);
        if (shape == "holder")
        {
            s.Kept(response, WorkspaceRetentionCodes.LiveProcess, m, before);
            response.Holders.ShouldNotBeNull().ShouldBe([new WorkspaceRetainHolder(4242, "sleep", "cwd")]);
        }
        else
            s.Kept(response, WorkspaceRetentionCodes.HolderScanUnknown, m, before);
    }

    [Test]
    public async Task Dry_run_reports_would_remove_and_mutates_nothing(CancellationToken ct)
    {
        // V-12 (G-27).
        using var s = Create();
        var m = s.Mirror();
        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(Request(m, WorkspaceRetainModes.DryRun), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.WouldRemove, response.Detail);
        response.HeadSha.ShouldBe(m.Head);
        response.OriginTip.ShouldBe(m.Head);
        Tree(m.Path).ShouldBe(before);
        s.Registered(m.Path).ShouldBeTrue();
        s.RetiringEntries().ShouldBeEmpty();
        s.MutatingVerbs().ShouldBeEmpty();
        s.Holders.Calls.ShouldBe(1);
    }

    [Test]
    [Arguments("holder-appears")]
    [Arguments("file-appears")]
    public async Task Execute_rechecks_under_the_lock_before_the_move(string change, CancellationToken ct)
    {
        // V-26 (G-28). The dry pass passed; the change lands at phase A entry, before its recheck.
        using var s = Create();
        var m = s.Mirror();
        var engine = s.Engine();
        engine.PhaseCut = phase =>
        {
            if (phase != "locked")
                return;
            if (change == "holder-appears")
                s.Holders.Next = _ => new HolderScan(true, [new WorkspaceSlotHolder(4343, "sleep", "cwd")]);
            else
                Plant(m.Path, "late.txt");
        };
        var response = await engine.RetainAsync(Request(m), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept);
        response.Reason.ShouldBe(change == "holder-appears" ? WorkspaceRetentionCodes.LiveProcess : WorkspaceRetentionCodes.Dirty);
        Directory.Exists(m.Path).ShouldBeTrue();
        s.Registered(m.Path).ShouldBeTrue();
        s.Calls("worktree", "move").ShouldBeEmpty();
        s.RetiringEntries().ShouldBeEmpty();
        if (change == "holder-appears")
            s.Holders.Calls.ShouldBe(2);
    }

    [Test]
    public async Task Phase_b_refusal_keeps_the_moved_tree_registered(CancellationToken ct)
    {
        // V-13 (G-32). A file written into the moved tree after phase A: git refuses, nothing is forced.
        using var s = Create();
        var m = s.Mirror();
        var engine = s.Engine();
        string? moved = null;
        engine.PhaseCut = phase =>
        {
            if (phase != "moved")
                return;
            moved = s.RetiringEntries().ShouldHaveSingleItem();
            Plant(moved, "late.txt");
        };
        var response = await engine.RetainAsync(Request(m), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.RetiringRefused);
        response.Detail.ShouldNotBeNull().ShouldContain("contains modified or untracked files");
        moved.ShouldNotBeNull();
        Directory.Exists(moved).ShouldBeTrue();
        s.Registered(moved).ShouldBeTrue();
        Directory.Exists(m.Path).ShouldBeFalse();
        var remove = s.Calls("worktree", "remove").ShouldHaveSingleItem();
        remove.Args.ShouldNotContain("--force");
        remove.Args.ShouldNotContain("-f");
        s.Calls("rm").ShouldBeEmpty();
        s.Calls("worktree", "prune").ShouldBeEmpty();
    }

    [Test]
    public async Task Move_failure_leaves_the_tree_in_place(CancellationToken ct)
    {
        // V-27 (G-31). A failed move never reaches phase B.
        using var s = Create();
        var m = s.Mirror();
        s.Faults.Add(RetentionFault.InSub(s.Repository, "worktree", "move", "fatal: move"));
        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(Request(m), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept);
        response.Reason.ShouldBe(WorkspaceRetentionCodes.RetiringMoveFailed);
        Tree(m.Path).ShouldBe(before);
        s.Registered(m.Path).ShouldBeTrue();
        s.RetiringEntries().ShouldBeEmpty();
        s.Calls("worktree", "remove").ShouldBeEmpty();
    }

    [Test]
    public async Task Retiring_residue_is_retried_by_phase_b_only(CancellationToken ct)
    {
        // V-28 (G-34, G-35, S-5).
        using var s = Create();
        var m = s.Mirror();
        var engine = s.Engine();
        string? moved = null;
        engine.PhaseCut = phase =>
        {
            if (phase != "moved")
                return;
            moved = s.RetiringEntries().ShouldHaveSingleItem();
            Plant(moved, "late.txt");
        };
        (await engine.RetainAsync(Request(m), ct)).Verdict.ShouldBe(WorkspaceRetentionCodes.RetiringRefused);
        File.Delete(moved! + "/late.txt");
        var other = s.Mirror("c1677003");
        var otherResidue = s.Retiring + "/task-c1677003.0badf00d";
        Git(s.Repository, "worktree", "move", other.Path, otherResidue);
        engine.PhaseCut = null;
        s.Argv.Clear();

        var response = await engine.RetainAsync(Request(m), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Removed, response.Detail);
        response.RetiringResidue.ShouldBeTrue();
        s.Calls("worktree", "move").ShouldBeEmpty("a residue is never moved again");
        s.Calls("worktree", "remove").ShouldHaveSingleItem().Args.ShouldBe(["worktree", "remove", moved!]);
        Directory.Exists(moved).ShouldBeFalse();
        Directory.Exists(otherResidue).ShouldBeTrue("another task's residue is untouched");
        s.Registered(otherResidue).ShouldBeTrue();
    }

    [Test]
    [Arguments(WorkspaceRetainModes.DryRun)]
    [Arguments(WorkspaceRetainModes.Execute)]
    public async Task Residue_retry_keeps_a_residue_a_live_process_holds(string mode, CancellationToken ct)
    {
        // CARD-1677 D-3 (c) (review c238f9ca, S-5). The holder scan comes first on the residue retry too: a
        // live process with its cwd in the moved tree keeps it, and `git worktree remove` (which does not
        // look at processes) never runs.
        using var s = Create();
        var m = s.Mirror();
        var residue = s.Retiring + "/task-a1677001.0badf00d";
        Directory.CreateDirectory(s.Retiring);
        Git(s.Repository, "worktree", "move", m.Path, residue);
        s.Holders.Next = _ => new HolderScan(true, [new WorkspaceSlotHolder(4242, "sleep", "cwd")]);
        var before = Tree(residue);
        s.Argv.Clear();

        var response = await s.Engine().RetainAsync(Request(m, mode), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, response.Detail);
        response.Reason.ShouldBe(WorkspaceRetentionCodes.LiveProcess);
        response.RetiringResidue.ShouldBeTrue();
        response.Holders.ShouldNotBeNull().ShouldBe([new WorkspaceRetainHolder(4242, "sleep", "cwd")]);
        s.Holders.Calls.ShouldBe(1, "the residue itself was scanned");
        s.Calls("worktree", "remove").ShouldBeEmpty();
        s.MutatingVerbs().ShouldBeEmpty();
        Tree(residue).ShouldBe(before);
        s.Registered(residue).ShouldBeTrue();
    }

    [Test]
    public async Task Dry_run_on_a_clean_unheld_residue_removes_nothing(CancellationToken ct)
    {
        // CARD-1684 R25 (guard (d), S-5). The leaf is gone and its retiring residue is clean and no process holds
        // it: a DryRun answers would_remove for the residue, hands the dispatcher no moved step, and never runs
        // phase B's `git worktree remove`. The residue and its registration stay.
        using var s = Create();
        var m = s.Mirror();
        var residue = s.Retiring + "/task-a1677001.0badf00d";
        Directory.CreateDirectory(s.Retiring);
        Git(s.Repository, "worktree", "move", m.Path, residue);
        var before = Tree(residue);
        s.Argv.Clear();
        var engine = s.Engine();

        var step = await engine.CheckAndMoveAsync(Request(m, WorkspaceRetainModes.DryRun), ct);
        step.Moved.ShouldBeFalse("a DryRun never hands phase B a path");
        var response = await engine.RetainAsync(Request(m, WorkspaceRetainModes.DryRun), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.WouldRemove, response.Detail);
        response.RetiringResidue.ShouldBeTrue();
        response.MovedPath.ShouldBe(residue);
        s.Holders.Calls.ShouldBe(2, "the residue itself was scanned on each pass");
        s.Calls("worktree", "remove").ShouldBeEmpty();
        s.MutatingVerbs().ShouldBeEmpty();
        Directory.Exists(residue).ShouldBeTrue();
        Tree(residue).ShouldBe(before);
        s.Registered(residue).ShouldBeTrue();
    }

    [Test]
    [Arguments("with-own-residue")]
    [Arguments("no-own-residue")]
    public async Task Residue_retry_never_takes_a_foreign_residue_that_sorts_first(string shape, CancellationToken ct)
    {
        // CARD-1684 R33 (PC-35). Another task's clean, unheld residue task-01677003.* sorts before this task's
        // task-a1677001.*, so "the first residue by ordinal" would pick it. Only this leaf's own residue is
        // retried; with none, the answer is already_absent. The foreign residue is never removed.
        using var s = Create();
        var foreign = s.Mirror("01677003");
        Directory.CreateDirectory(s.Retiring);
        var foreignResidue = s.Retiring + "/task-01677003.0badf00d";
        Git(s.Repository, "worktree", "move", foreign.Path, foreignResidue);
        string? own = null;
        PhoneHomeWorkspaceRetainRequest request;
        if (shape == "with-own-residue")
        {
            var m = s.Mirror();
            own = s.Retiring + "/task-a1677001.0badf00d";
            Git(s.Repository, "worktree", "move", m.Path, own);
            request = Request(m);
        }
        else
            request = new PhoneHomeWorkspaceRetainRequest(TaskOf("a1677001"), s.Worktrees + "/task-a1677001",
                "feat/card-task-a1677001", Execute, Guid.NewGuid());
        string.CompareOrdinal(Path.GetFileName(foreignResidue), "task-a1677001.0badf00d")
            .ShouldBeLessThan(0, "the foreign residue sorts first (the fixture's precondition)");
        var foreignBefore = Tree(foreignResidue);
        s.Argv.Clear();

        var response = await s.Engine().RetainAsync(request, ct);
        if (own is null)
        {
            response.Verdict.ShouldBe(WorkspaceRetentionCodes.AlreadyAbsent, response.Detail);
            response.RetiringResidue.ShouldBeFalse();
            s.MutatingVerbs().ShouldBeEmpty();
        }
        else
        {
            response.Verdict.ShouldBe(WorkspaceRetentionCodes.Removed, response.Detail);
            response.RetiringResidue.ShouldBeTrue();
            response.MovedPath.ShouldBe(own);
            s.Calls("worktree", "remove").ShouldHaveSingleItem().Args.ShouldBe(["worktree", "remove", own]);
            Directory.Exists(own).ShouldBeFalse();
        }
        Directory.Exists(foreignResidue).ShouldBeTrue("another task's residue is untouched");
        Tree(foreignResidue).ShouldBe(foreignBefore);
        s.Registered(foreignResidue).ShouldBeTrue();
    }

    [Test]
    [Arguments(WorkspaceRetainModes.Execute)]
    [Arguments(WorkspaceRetainModes.DryRun)]
    public async Task Residue_retry_keeps_a_legitimate_residue_beside_a_stray_entry(string mode, CancellationToken ct)
    {
        // CARD-1677 S1 hardening mutation XR (task cde14651). This leaf's real, clean, registered, unheld residue
        // task-a1677001.0badf00d sits beside a stray task-a1677001.0badf00d-stray that sorts after it. The stray is
        // a doubt about this leaf's residues, so the residue is kept and reported: taking the first exact name
        // before the rest of the folder is read would remove it.
        using var s = Create();
        var m = s.Mirror();
        var residue = s.Retiring + "/task-a1677001.0badf00d";
        var stray = residue + "-stray";
        Directory.CreateDirectory(s.Retiring);
        Git(s.Repository, "worktree", "move", m.Path, residue);
        Directory.CreateDirectory(stray);
        string.CompareOrdinal(Path.GetFileName(residue), Path.GetFileName(stray))
            .ShouldBeLessThan(0, "the stray sorts after the residue (the fixture's precondition)");
        var before = Tree(residue);
        s.Argv.Clear();

        var response = await s.Engine().RetainAsync(Request(m, mode), ct);

        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, response.Detail);
        response.Reason.ShouldBe(WorkspaceRetentionCodes.WorkspaceRetiringOrUnknown, response.Detail);
        response.RetiringResidue.ShouldBeTrue();
        Directory.Exists(residue).ShouldBeTrue("the residue stays");
        Tree(residue).ShouldBe(before);
        s.Registered(residue).ShouldBeTrue("the residue registration stays");
        Directory.Exists(stray).ShouldBeTrue("the stray stays");
        s.Calls("worktree", "remove").ShouldBeEmpty();
        s.MutatingVerbs().ShouldBeEmpty("no git mutation ran");
        s.Holders.Calls.ShouldBe(0, "a doubted residue is never scanned as a candidate");
    }

    [Test]
    [Arguments(WorkspaceRetainModes.Execute)]
    [Arguments(WorkspaceRetainModes.DryRun)]
    public async Task Residue_retry_keeps_an_upper_case_hex_residue(string mode, CancellationToken ct)
    {
        // CARD-1677 S1 hardening mutation PC-C (task cde14651). A real, clean, registered, unheld worktree named
        // task-a1677001.0BADF00D holds no control character, so only the not-<leaf>.<8 hex> doubt refuses it:
        // without that doubt it falls through as this leaf's residue and is removed.
        using var s = Create();
        var m = s.Mirror();
        var residue = s.Retiring + "/task-a1677001.0BADF00D";
        Directory.CreateDirectory(s.Retiring);
        Git(s.Repository, "worktree", "move", m.Path, residue);
        var before = Tree(residue);
        s.Argv.Clear();

        var response = await s.Engine().RetainAsync(Request(m, mode), ct);

        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, response.Detail);
        response.Reason.ShouldBe(WorkspaceRetentionCodes.WorkspaceRetiringOrUnknown, response.Detail);
        response.RetiringResidue.ShouldBeTrue();
        Directory.Exists(residue).ShouldBeTrue("the residue stays");
        Tree(residue).ShouldBe(before);
        s.Registered(residue).ShouldBeTrue("the residue registration stays");
        s.Calls("worktree", "remove").ShouldBeEmpty();
        s.MutatingVerbs().ShouldBeEmpty("no git mutation ran");
        s.Holders.Calls.ShouldBe(0, "a doubted residue is never scanned as a candidate");
    }

    [Test]
    [Arguments("execute")]
    [Arguments("dryrun")]
    [Arguments("Bogus")]
    [Arguments("")]
    public async Task Unrecognised_mode_is_kept_before_any_git(string mode, CancellationToken ct)
    {
        // CARD-1684 R26 (guard (d)). Only the two exact constants are modes; anything else (a case variant, an
        // unknown word, empty) is refused before any git, so it can never execute.
        using var s = Create();
        var m = s.Mirror();
        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(Request(m, mode), ct);
        s.Kept(response, WorkspaceRetentionCodes.OutsideScope, m, before);
        response.Detail.ShouldBe("mode must be DryRun or Execute");
        s.Argv.ShouldBeEmpty("the mode is refused before any git runs");
        s.Holders.Calls.ShouldBe(0);
    }

    [Test]
    [Arguments(WorkspaceRetainModes.DryRun)]
    [Arguments(WorkspaceRetainModes.Execute)]
    public async Task Tree_not_registered_at_its_path_is_kept(string mode, CancellationToken ct)
    {
        // CARD-1677 D-3 (b) (review c238f9ca, step 3). A runner-repository worktree moved into the leaf with a
        // plain mv: its .git still names the runner repository, but `git worktree list` lists the old path, so
        // it is not a registered worktree at the request path. Kept in DryRun too (git's own move refusal
        // would only mask the check in Execute).
        using var s = Create();
        var registered = s.Mirror(path: s.Worktrees + "/elsewhere-a1677001");
        var path = s.Worktrees + "/task-a1677001";
        Directory.Move(registered.Path, path);
        var m = registered with { Path = path };
        var before = Tree(path);

        var response = await s.Engine().RetainAsync(Request(m, mode), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, response.Detail);
        response.Reason.ShouldBe(WorkspaceRetentionCodes.NotRunnerRepository, response.Detail);
        Directory.Exists(path).ShouldBeTrue();
        Tree(path).ShouldBe(before);
        s.Registered(registered.Path).ShouldBeTrue("the registration still names the old path");
        s.RetiringEntries().ShouldBeEmpty();
        s.MutatingVerbs().ShouldBeEmpty();
    }

    [Test]
    public async Task Cross_device_retiring_root_moves_nothing(CancellationToken ct)
    {
        // V-15 (G-29).
        using var s = Create();
        var m = s.Mirror();
        s.Device = path => path.Replace('\\', '/').TrimEnd('/') == s.Retiring ? 2UL : 1UL;
        var before = Tree(m.Path);
        var response = await s.Engine().RetainAsync(Request(m), ct);
        s.Kept(response, WorkspaceRetentionCodes.RetiringRootCrossDevice, m, before);
    }

    [Test]
    public async Task Retiring_root_inside_the_worktree_root_is_refused(CancellationToken ct)
    {
        // V-29 (G-30).
        using var s = Create();
        var m = s.Mirror();
        var before = Tree(m.Path);
        var response = await s.Engine(retiringRoot: s.Worktrees + "/retiring").RetainAsync(Request(m), ct);
        s.Kept(response, WorkspaceRetentionCodes.RetiringRootInvalid, m, before);
        Directory.Exists(s.Worktrees + "/retiring").ShouldBeFalse();
    }

    [Test]
    public async Task Ignored_nested_repo_goes_with_the_tree(CancellationToken ct)
    {
        // V-16. A nested repository under ignored .antiphon/ is CARD-1590 evidence, not a refusal.
        using var s = Create();
        var m = s.Mirror();
        Directory.CreateDirectory(m.Path + "/.antiphon/seed");
        Git(m.Path + "/.antiphon/seed", "init", "-q");
        Git(m.Path + "/.antiphon/seed", "-c", "user.email=x@y", "-c", "user.name=x", "commit", "-q", "--allow-empty", "-m", "seed");
        var response = await s.Engine().RetainAsync(Request(m), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Removed, response.Detail);
        response.NestedIgnoredRepos.ShouldBe(1);
        Directory.Exists(m.Path).ShouldBeFalse();
    }

    [Test]
    public async Task Absent_path_is_already_absent_without_prune(CancellationToken ct)
    {
        // V-17 (G-39).
        using var s = Create();
        var stale = s.StaleRegistration("a16770ff");
        var request = new PhoneHomeWorkspaceRetainRequest(TaskOf("a1677001"), s.Worktrees + "/task-a1677001",
            "feat/card-task-a1677001", Execute, Guid.NewGuid());
        var response = await s.Engine().RetainAsync(request, ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.AlreadyAbsent);
        response.RetiringResidue.ShouldBeFalse();
        s.Calls("worktree", "prune").ShouldBeEmpty();
        s.MutatingVerbs().ShouldBeEmpty();
        s.Registered(stale).ShouldBeTrue();
    }

    [Test]
    [Arguments(".antiphon/inbox/brief.md", "\n")]
    [Arguments(".antiphon/inbox/brief.md", "\r")]
    [Arguments(".antiphon/inbox/brief.md", "\u2028")]
    [Arguments(".antiphon/inbox/brief.md", " ")]
    [Arguments(".antiphon/task-1234-brief.md", "\n")]
    [Arguments("bin/cache.dat", "\n")]
    [Arguments("src/obj/cache.dat", "\t")]
    [Arguments("obj/project.assets.json", "\t")]
    public async Task Ignored_content_with_an_end_suffix_is_never_disposable(string path, string suffix)
    {
        // CARD-1677 S1 hardening (review d0b57b9c A-3). The completed-card classifier's `^(?:...)$` matched
        // `.antiphon/inbox/brief.md` plus a final newline as a disposable inbox brief, so a protected-by-default
        // file went with the tree. A suffixed name is no disposable pattern's match: it stays protected.
        // Control: the bare name, under both the default and an explicit policy, is still disposable.
        var classifiers = new[]
        {
            new RunnerWorkspaceIgnoredContent(),
            new RunnerWorkspaceIgnoredContent(["**/bin/**", "**/obj/**", ".antiphon/inbox/*.md", ".antiphon/task-*-brief.md"]),
        };
        foreach (var classifier in classifiers)
        {
            classifier.Classify(path).ShouldBe(RunnerIgnoredContentKind.Disposable, "control: " + path);
            classifier.Classify(path + suffix).ShouldBe(RunnerIgnoredContentKind.Protected, path + " + U+" + ((int)suffix[0]).ToString("X4"));
        }
        await Task.CompletedTask;
    }

    [Test]
    [Arguments(".antiphon/inbox/br", "ief.md", "\n")]
    [Arguments(".antiphon/inbox/br", "ief.md", "\u2028")]
    [Arguments("bin/ca", "che.dat", "\t")]
    public async Task Ignored_content_with_an_embedded_control_character_is_never_disposable(string head, string tail, string control)
    {
        // CARD-1677 S1 hardening mutation XC (task cde14651). The control character sits inside the name, not at
        // its end: `*` (`[^/]*`) matches a newline or U+2028 and `**` (`.*`) a TAB, so a check of the last
        // character alone would make each disposable. Control: the name without it is still disposable.
        var classifiers = new[]
        {
            new RunnerWorkspaceIgnoredContent(),
            new RunnerWorkspaceIgnoredContent(["**/bin/**", "**/obj/**", ".antiphon/inbox/*.md", ".antiphon/task-*-brief.md"]),
        };
        foreach (var classifier in classifiers)
        {
            classifier.Classify(head + tail).ShouldBe(RunnerIgnoredContentKind.Disposable, "control: " + head + tail);
            classifier.Classify(head + control + tail).ShouldBe(RunnerIgnoredContentKind.Protected,
                head + " + U+" + ((int)control[0]).ToString("X4") + " + " + tail);
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task Ignored_secret_files_survive_admission_on_dry_run_and_execute(CancellationToken ct)
    {
        // CARD-1718 A-D3 round 2 (review 47d31366 B-1). A clean pushed mirror whose ignored
        // bin/.env, node_modules/.env and dist/credentials/token are invisible to status porcelain.
        // Admission must keep the tree, its registration and every secret byte. DryRun must not
        // answer would_remove.
        using var s = Create();
        var m = IgnoredSecrets(s);
        var paths = new[] { "bin/.env", "node_modules/.env", "dist/credentials/token" };
        var bytes = paths.ToDictionary(path => path, path => File.ReadAllBytes(m.Path + "/" + path));

        var dry = await s.Engine().RetainAsync(Request(m, WorkspaceRetainModes.DryRun), ct);
        dry.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, dry.Detail);
        dry.Reason.ShouldBe("ignored_secret", dry.Detail);
        dry.Detail.ShouldBe("bin/.env");

        var response = await s.Engine().RetainAsync(Request(m), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, response.Detail);
        response.Reason.ShouldBe("ignored_secret", response.Detail);
        response.Detail.ShouldBe("bin/.env");
        Directory.Exists(m.Path).ShouldBeTrue();
        s.Registered(m.Path).ShouldBeTrue();
        s.RetiringEntries().ShouldBeEmpty();
        s.Calls("worktree", "remove").ShouldBeEmpty();
        s.Calls("worktree", "move").ShouldBeEmpty();
        foreach (var path in paths)
            File.ReadAllBytes(m.Path + "/" + path).ShouldBe(bytes[path]);
    }

    [Test]
    public async Task Phase_b_keeps_an_ignored_secret_planted_after_the_move(CancellationToken ct)
    {
        // B-1 removal. The tree was eligible at admission. A secret that appears in the moved
        // tree must survive phase B: no `git worktree remove`, registration kept, bytes kept.
        using var s = Create();
        var m = s.Mirror();
        var engine = s.Engine();
        string? moved = null;
        engine.PhaseBGate = path =>
        {
            moved = path;
            Plant(path, "bin/.env", "late-secret");
            return Task.CompletedTask;
        };

        var response = await engine.RetainAsync(Request(m), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, response.Detail);
        response.Reason.ShouldBe("ignored_secret", response.Detail);
        response.Detail.ShouldBe("bin/.env");
        Directory.Exists(moved).ShouldBeTrue();
        File.ReadAllText(moved + "/bin/.env").ShouldBe("late-secret");
        s.Registered(moved!).ShouldBeTrue();
        s.Calls("worktree", "remove").ShouldBeEmpty();
    }

    [Test]
    [Arguments(WorkspaceRetainModes.Execute)]
    [Arguments(WorkspaceRetainModes.DryRun)]
    public async Task Residue_retry_keeps_an_ignored_secret(string mode, CancellationToken ct)
    {
        // B-1 recovery. The leaf is gone and its retiring residue holds an ignored secret.
        // DryRun never reaches phase B, so the refusal has to happen on the residue itself.
        // Execute keeps it here too, before `git worktree remove`.
        using var s = Create();
        var m = s.Mirror();
        var residue = s.Retiring + "/task-a1677001.0badf00d";
        Directory.CreateDirectory(s.Retiring);
        Git(s.Repository, "worktree", "move", m.Path, residue);
        Plant(residue, "bin/.env", "residue-secret");

        var response = await s.Engine().RetainAsync(Request(m, mode), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, response.Detail);
        response.Reason.ShouldBe("ignored_secret", response.Detail);
        response.Detail.ShouldBe("bin/.env");
        response.RetiringResidue.ShouldBeTrue();
        File.ReadAllText(residue + "/bin/.env").ShouldBe("residue-secret");
        s.Registered(residue).ShouldBeTrue();
        s.Calls("worktree", "remove").ShouldBeEmpty();
    }

    [Test]
    public async Task Unavailable_ignored_list_keeps_the_tree(CancellationToken ct)
    {
        // B-1. A failed ignored-file listing is a doubt. The tree stays; it is not treated as empty.
        using var s = Create();
        var m = s.Mirror();
        Plant(m.Path, "bin/.env", "still-here");
        s.Faults.Add(RetentionFault.In(m.Path, "ls-files", "ls-files failed"));

        var response = await s.Engine().RetainAsync(Request(m), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, response.Detail);
        response.Reason.ShouldBe(WorkspaceRetentionCodes.InspectionFailed, response.Detail);
        Directory.Exists(m.Path).ShouldBeTrue();
        File.ReadAllText(m.Path + "/bin/.env").ShouldBe("still-here");
        s.Registered(m.Path).ShouldBeTrue();
        s.Calls("worktree", "remove").ShouldBeEmpty();
    }

    /// <summary>A clean pushed mirror plus the three ignored secrets the retention path must keep.</summary>
    private static MirrorTree IgnoredSecrets(C1677RetentionScratch s)
    {
        var m = s.Mirror();
        File.AppendAllText(m.Path + "/.gitignore", "node_modules/\ndist/\n");
        Git(m.Path, "add", ".gitignore");
        Git(m.Path, "commit", "-q", "-m", "ignore disposable trees");
        Git(m.Path, "push", "-q", s.OriginGit, "HEAD:refs/heads/" + m.Branch);
        // rev-list counts commits absent from origin's remote-tracking refs, not the bare push URL.
        Git(s.Repository, "fetch", "origin", "refs/heads/" + m.Branch + ":refs/remotes/origin/" + m.Branch);
        Plant(m.Path, "bin/.env", "bin-env-secret");
        Plant(m.Path, "node_modules/.env", "nm-env-secret");
        Plant(m.Path, "dist/credentials/token", "dist-token-secret");
        Git(m.Path, "status", "--porcelain", "--untracked-files=all").Trim().ShouldBeEmpty();
        return m with { Head = Git(m.Path, "rev-parse", "HEAD").Trim() };
    }
}
