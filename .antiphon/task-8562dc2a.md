# Task 8562dc2a — child tree-kill fix round 1

D-1 through D-4 are fixed on `feat/card-task-8562dc2a`. The validating receipt is run `20261031-175553-b428` at `d44e36398f73ff5bd19cbf083440ccd27fd867ac`: `CHECKPOINT SOURCE VALID source=d44e36398f73ff5bd19cbf083440ccd27fd867ac rows=10`, 10 green, 0 red, 0 skipped. CP-2 is 28 executed, 28 passed, 0 failed, 0 skipped.

Snapshot failure kills only the owned root handle (`entireProcessTree: false`, log token `fallback=owned_root_only`). This round does not fall back to `Kill(entireProcessTree: true)`. An unguarded walk after a failed snapshot would signal whatever now carries the root pid, which is the Linux failure in D-3.

Review subject: Opus Final review 877889c5 at `4a4e3c01ad86986e672b35c3ee93af70c3677f84` on `feat/card-task-877889c5`. Verdict there was defects. This branch was not merged.

## Commits

Fast-forward of `24415a6bb8576cb27b640ea3837d20705fa1aa45`:

- `47c28a2dd4bed8b509f4bd9e9fa44edd8f0f5680` fix: refuse a reused pid when killing an owned git tree
- `1f069c291433b60e2d595dc9e17c0a0b169168f1` test: keep a late descendant visible to a kill-time re-walk
- `6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64` docs: checkpoint the tree-kill fix round
- `d44e36398f73ff5bd19cbf083440ccd27fd867ac` docs: omit the two Windows lease skips from the checkpoint

The product change is `47c28a2dd4bed8b509f4bd9e9fa44edd8f0f5680`. `1f069c291433b60e2d595dc9e17c0a0b169168f1` only changes the sealed-snapshot witness so a kill-time re-walk can see the late pid. The two docs commits are the checkpoint plan.

## D-1 — reused parent pid

Identity is `RepositoryChildJournal.TryStartTicks` (`Process.StartTime.ToUniversalTime().Ticks`), the same value the child journal stores at spawn. `LandingGit.IsProcessAliveAsync` uses that helper. CARD-1619 `LinuxProcessIdentity` (boot id plus `/proc` stat field 22) stays unused: it is Linux-only, it lives in SessionRunner, and the server does not reference it. A boot id does not separate two processes that reused a pid in the same boot. Advisory A-3 says the boot id is unnecessary for this snapshot and asks for the existing tick helper.

`OwnedProcessTree.Plan` accepts a child edge only when the child's start ticks are non-null and `childTicks >= parentTicks`. A null tick is omitted and is not walked past. A child that started before its parent is omitted with its subtree. The root's captured start ticks are compared with the table before the walk. A table row for the root pid whose ticks differ is `root_identity_mismatch`: the walk is blocked and only the owned handle is attempted. A missing or unreadable root row is walked with the captured ticks, because a Windows orphan keeps the dead parent pid. The kill step re-reads each sealed pid's ticks and signals only an exact match (`reason=reused` otherwise). One `Process` object is used for that re-read and the kill.

Server pid and owner pid are excluded and are not walked through. Production passes `Environment.ProcessId` for both. A root pid that is the server or the owner is blocked and is not killed.

## D-2 — planner seam and witnesses

`Plan(table, root identity, serverPid, ownerPid)` is pure. `Execute` takes injected `readTable`, `readStartTicks`, `kill`, `killOwnedRoot`, `rootAlreadyExited`, and a log action. Up to four snapshot passes run before any kill (`MaxSnapshotPasses = 4`). The list is then sealed. Tree bound is `MaxDescendants = 64` including the root. Candidate tick reads stop at `MaxCandidateRows = 256`.

Planner witnesses in `OwnedProcessTreeTests` (10 methods, all green on the validating run):

- `C448_ReusedPidWithLaterStartTicksIsNotSignalled`
- `C448_StaleParentPidExcludesTheChildAndItsSubtree`
- `C448_ServerAndOwnerPidsAreExcluded`
- `C448_UnreadableStartTimeStopsTheWalk`
- `C448_SnapshotIsSealedBeforeTheFirstKill`
- `C448_TreeSizeIsBounded`
- `C448_RootIdentityMismatchWalksNothing`
- `C448_LinuxExitedRootIsNotWalked`
- `C448_SnapshotFailureKillsOnlyTheOwnedRoot`
- `C448_KillOrderSignalsTheRootFirst`

`1f069c291433b60e2d595dc9e17c0a0b169168f1` records the late pid 99 once and reads ticks from the last table row. The first mutant cycle left M3a green because the witness appended pid 99 on every kill and the single-row lookup threw before a signal. The assertion was not loosened. After that commit, M3a went red and the restored source went green.

Journal and live-process witnesses kept:

- `C448_JournalStaysWhileTheRootLives` — `LivingRootGit` overrides both `KillOwnedTree` and `KillOwnedRoot`. The grace-path `TryKill(entireProcessTree: false)` was killing the root while the tree kill was a no-op. `Exited()` still throws `owned_child_still_running` when the handle has not exited.
- `C448_ParentDeathGrandchildDoesNotKeepTheJournal` — grandchild alive before cancel and dead after; journal empty. A legitimately detached stdout holder is still not killed (`C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder`, `C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits`).

## D-3 — exited root

On Linux, `HasExited` before the walk returns `root_gone` and does not walk. Witness `C448_LinuxExitedRootIsNotWalked`: the root pid in the fake table belongs to a different start-tick process, and the log contains `reason=root_gone`.

## D-4 — snapshot and kill logs

Failure lines:

- `repository_child_tree_kill snapshot_failed pid={pid} reason={reason} fallback=owned_root_only event=repository_child_tree_kill_snapshot_failed`
- `repository_child_tree_kill kill_failed pid={pid} reason={reason}`
- success: `repository_child_tree_kill root={pid} descendants={count} killed={signalled pids}`

Windows toolhelp failures use `Marshal.GetLastWin32Error()` immediately after the failed call: `invalid_handle:win32_{code}` and `process32_first_failed:win32_{code}` (`OwnedProcessTree.cs` at the snapshot-open sites). A per-pid stat error is skipped. It is not a snapshot failure.

`LandingGit.AbandonedRootExitBound` is 30 seconds (`A-7`). If the owned root is still alive, the handoff logs `repository_child_journal_retained_running_root pid={pid} reason=exit_wait_bound` and leaves the journal.

How a native Windows log reads, on a checkout that contains `47c28a2dd4bed8b509f4bd9e9fa44edd8f0f5680` (validated source `d44e36398f73ff5bd19cbf083440ccd27fd867ac`):

- `descendants=0` and the json remains: the snapshot missed the tree. Enumeration failure also writes a `snapshot_failed` line with `fallback=owned_root_only`.
- `descendants>0` and the sleeper still alive: the terminate missed it. `kill_failed` carries `reason=reused` for a tick mismatch, or the access-denied / exception reason.
- `descendants>0`, sleeper dead, json remains: drain or delete. Read `CleanupFailure`.

Those two native runs cannot witness D-1. D-1 is the planner seam.

Owed, still not run here (this host is Linux; no native Windows run):

- `RepositoryMutationLeaseTests.C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion`
- `LandingGitTests.C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal`

Keep stderr.

## Advisories A-1..A-8

- A-1 applied. The sealed list signals the root first, then descendants. Witness `C448_KillOrderSignalsTheRootFirst`.
- A-2 applied for one `Process` per pid (tick re-read and kill). Parent re-check at kill time declined: root-first reparents Linux children, and a parent re-check would skip the snapshotted grandchild that `PR_SET_PDEATHSIG` must still reach.
- A-3 declined `LinuxProcessIdentity`. Reused `TryStartTicks`. `IsProcessAliveAsync` calls it.
- A-4 applied cheaply. `kill_failed` includes pid and reason. The success line lists signalled pids. A full tick dump was not added.
- A-5 daemon behaviour declined: a captured descendant is still killed, same as before the review. The tree-size bound was applied anyway (`MaxDescendants = 64`) because the brief requires a bound.
- A-6 declined. `KillHolder` still kills from the pid file with no stored ticks. The review calls reuse within seconds negligible, and rewriting every holder pid file is outside the cheap list.
- A-7 applied. 30-second `WaitForExit` bound, then the retained-root log above.
- A-8 no code change. The Windows skip on the parent-death test and the living-root test already names the two owed native rows. The owed interpretation above replaces the review's wording, which was written against `c70d88af9` and said a missed terminate was not distinguishable in the log.

## Mutants

Each mutant was applied, observed red, restored, and observed green. Source was restored after the cycle. Nothing in this list was committed as a mutant.

```
M2 red exit=2 total=1 succeeded=0 failed=1
M2 green exit=0 total=1 succeeded=1 failed=0
BASE green exit=0 total=1 succeeded=1 failed=0
M3a red exit=2 total=1 succeeded=0 failed=1
M3a green exit=0 total=1 succeeded=1 failed=0
M3b red exit=2 total=1 succeeded=0 failed=1
M3b green exit=0 total=1 succeeded=1 failed=0
M4 red exit=2 total=1 succeeded=0 failed=1
M4 green exit=0 total=1 succeeded=1 failed=0
M7 red exit=2 total=1 succeeded=0 failed=1
M7 green exit=0 total=1 succeeded=1 failed=0
M8 red exit=2 total=1 succeeded=0 failed=1
M8 green exit=0 total=1 succeeded=1 failed=0
M1 red exit=2 total=1 succeeded=0 failed=1
M1 green exit=0 total=1 succeeded=1 failed=0
M6 red exit=2 total=1 succeeded=0 failed=1
M6 green exit=0 total=1 succeeded=1 failed=0
M5 red exit=2 total=2 succeeded=0 failed=2
M5 green exit=0 total=2 succeeded=2 failed=0
M9 red exit=2 total=1 succeeded=0 failed=1
M9 green exit=0 total=1 succeeded=1 failed=0
ALL_MUTANTS_RED_THEN_GREEN
```

M5's two failures are the two detached-holder journal witnesses. M3b, M4, and M7 ran on the pure planner only, so a bad plan never signalled a live stranger.

## Checkpoints

Plan: `docs/superpowers/plans/2026-10-31-task-8562dc2a-tree-kill-checkpoints.md`. One Antiphon.Tests build (`bin-tk8562cp/`), later rows reuse it. Policy rows for SessionRunner, PtyHost, and Agents.Pty each build once. CP-10 is `audit-land-verification-graph.ps1 -Check` only.

Counts on the validating run: OwnedProcessTreeTests 10/10, RepositoryMutationLeaseTests 28/28/0 skipped, LandingGitTests 65/65, child-journal group 33/33, roster plus census (C1404, C1577, C1584, C1410, G2865) 8/8, NativeLinuxPolicyWitness 3/3 in each of Antiphon.Tests, SessionRunner.Tests, PtyHost.Tests, and Agents.Pty.Tests. Land graph exit 0.

Land-graph console on that run: build 0 warnings, 0 errors, elapsed `00:00:19.74`; `digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69`; `approved`. Not-built projects were the card-0462 reproduce project, the c1375 and c487 nightly probes, `tests/Antiphon.TimeoutAudit.Tests`, and `tools/Antiphon.NativeLinuxMigrationAudit`. The canary worktree named in that console is gone.

### Run 1 — tool green, receipt invalid

`20261031-165356-3c92` at `6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64` exited 0 with verdict GREEN (10 green, wall 40m17s). `validate` then exited 2: `CHECKPOINT SOURCE INVALID reason=row_failed`. CP-2 selected `/*/*/RepositoryMutationLeaseTests/*` and recorded `executed=28 passed=28 failed=0 skipped=2`. The two skips are the Windows-only methods (`C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput` and `C448_V13_WindowsJunctionAndOtherProcessShareTheLease`). ReportValidator treats a non-zero skip on a non-command row as `row_failed`. This receipt is not source-valid. The unedited tool report is appended below.

### Run 2 — stopped, no receipt

`20261031-173646-994f` at `d44e36398f73ff5bd19cbf083440ccd27fd867ac` was stopped. `state.json` records `slotReason: refused: tampered` on the Antiphon.Tests build. CP-10's Release land-graph build rewrote `tools/Antiphon.LandGraphAudit` and `tools/Antiphon.LandGraphAudit.Core` `obj/Release` and `bin-tk8562land` files that already existed from run 1 and were therefore inside the confinement ledger. Creating those paths fresh is allowed; rewriting a ledger-stamped ignored file outside the build's write roots is `refused: tampered`. The executor was stopped. There is no `report.json`. Do not validate this run.

The third run deleted those obj and bin trees first, so the parallel land-graph command created them during the slot. CP-1 of that run went green.

### Run 3 — source-valid

`20261031-175553-b428`, tool exit 0, verdict GREEN, source clean, `buildSource=verified`.

`dotnet exec .../Antiphon.Checkpoints.dll validate --evidence .../20261031-175553-b428/report.json --expected-source-sha d44e36398f73ff5bd19cbf083440ccd27fd867ac` printed:

`CHECKPOINT SOURCE VALID source=d44e36398f73ff5bd19cbf083440ccd27fd867ac rows=10`

CP-2 on this run is `skipped=0`. Wall 35m31s, sequential-equivalent 31m25s, builds 4. `BUDGET wall=2130.897s execution=1388.876s admissionOnly=742.021s`. `unlisted: none`. The unedited tool report is appended below.

## Evidence and publication

`scripts/check-evidence-diff.ps1 -BaseRef 24415a6bb8576cb27b640ea3837d20705fa1aa45 -HeadRef HEAD` at the validated tip:

`EVIDENCE result commits=4 entries=0 violations=0 base=24415a6bb8576cb27b640ea3837d20705fa1aa45 head=d44e36398f73ff5bd19cbf083440ccd27fd867ac` (exit 0).

Fetched this turn:

- `origin/master` = `2051d327123a5e98524b4bd1ecf31c6e516bdb4e`. `git merge-tree --write-tree origin/master HEAD` exits 1. Conflicts include `server/Infrastructure/Git/LandingGit.cs` and `tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs` among many files that this branch does not touch. That is the standing rc/master divergence. Not merged.
- `origin/rc/2026-10-27` = `e0d49d221513b467f8c853f47587e688e04255e2`. It is not an ancestor of this branch, and `24415a6bb8576cb27b640ea3837d20705fa1aa45` is not an ancestor of that rc tip. The seven commits from `a909cdd16` through `d44e36398` are absent from rc. `git merge-tree --write-tree origin/rc/2026-10-27 HEAD` exits 0 with no conflict lines. Clean tree `db37f0a1fd85af068dc55ce57da753d64c4a33ba`. Not merged.

No tag, no publish, no rebase, no force-push.

## Owed native rows

On a Windows checkout of this branch at or after `47c28a2dd4bed8b509f4bd9e9fa44edd8f0f5680`, rerun the two tests named under D-4 and read stderr with the three-way interpretation in that section.

--- next stage ---
next: a short Final review of D-1..D-4, then merge to rc top-up 52
handoff: review the TryStartTicks identity, the owned-root-only snapshot fallback, and receipt 20261031-175553-b428
artifact: .antiphon/task-8562dc2a.md

## Unedited tool report — validating run 20261031-175553-b428

```
--- checkpoint report ---
run: 20261031-175553-b428   manifest: /work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/manifest.resolved.yaml
commit: d44e36398f73ff5bd19cbf083440ccd27fd867ac  branch: feat/card-task-8562dc2a  worktree: /work/worktrees/task-8562dc2a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: d44e36398f73ff5bd19cbf083440ccd27fd867ac state=clean buildSource=verified
CHECKPOINT CP-10 commit=d44e36398f73ff5bd19cbf083440ccd27fd867ac build=n/a filter=dotnet build tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj -c Release --property:OutputPath=bin-tk8562land/ --property:AppendTargetFrameworkToOutputPath=false --property:UseAppHost=false --nologo -v q -maxcpucount:6 && test -f tools/Antiphon.LandGraphAudit/bin-tk8562land/Antiphon.LandGraphAudit.dll && pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath /work/worktrees/task-8562dc2a/tools/Antiphon.LandGraphAudit/bin-tk8562land/Antiphon.LandGraphAudit.dll executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=210s dirty=0 source=d44e36398f73ff5bd19cbf083440ccd27fd867ac sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-10 waited=210s executed=49.294s wall=259.444s
CHECKPOINT CP-1 commit=d44e36398f73ff5bd19cbf083440ccd27fd867ac build=ok filter=/*/*/OwnedProcessTreeTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/rows/CP-1/run.trx slot=granted waited=210s dirty=0 source=d44e36398f73ff5bd19cbf083440ccd27fd867ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=210s executed=4.848s wall=214.927s
PHASES CP-1 slotWait=405s build=788.4918727s startup=3.2762763s testsWall=0.0513954s teardown=0.6497938s hostWall=3.9774696s
CHECKPOINT CP-2 commit=d44e36398f73ff5bd19cbf083440ccd27fd867ac build=reused filter=/*/*/RepositoryMutationLeaseTests/(C448_C24_UnreadableJournalStateCannotAdmitAWriter*)|(C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion*)|(C448_JournalRemovalRetriesTransientDeleteFailuresBeforeRelease*)|(C448_JournalRemovalFailureKeepsFenceAndRecordsReason*)|(C448_C24_StandingJournalFencesAdmissionByStartIdentity*)|(C661_AlreadyExitedChildIsJournalledInsteadOfThrowing*)|(C661_FastExitingOwnedChildLeavesTheCallersChildStateUnknown*)|(C661_FastExitingOwnedChildPersistsUnknownIdentityAndRecoveryRefuses*)|(C448_C24_KilledWorkerLeavesLiveGitChildFenced*)|(C666_JournaledGitThatCannotStartLeavesNoStandingFence*)|(C666_AmbiguousGitStartFailureKeepsTheStandingFence*)|(C448_V13_LeaseUsesCommonRepositoryIdentity*)|(C1076_LiveTaggedPrepPushChildDoesNotFenceButDeadReusedOrUnknownStillDoes*) executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/rows/CP-2/run.trx slot=granted waited=225s dirty=0 source=d44e36398f73ff5bd19cbf083440ccd27fd867ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=225s executed=81.983s wall=307.115s
PHASES CP-2 slotWait=225s build=0s startup=46.5251443s testsWall=31.3344908s teardown=3.2879989s hostWall=81.1476343s
CHECKPOINT CP-3 commit=d44e36398f73ff5bd19cbf083440ccd27fd867ac build=reused filter=/*/*/LandingGitTests/* executed=65 passed=65 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/rows/CP-3/run.trx slot=granted waited=120s dirty=0 source=d44e36398f73ff5bd19cbf083440ccd27fd867ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=120s executed=31.636s wall=151.713s
PHASES CP-3 slotWait=120s build=0s startup=8.8719501s testsWall=20.6051927s teardown=0.5160925s hostWall=29.9932383s
SLOW CLASS Antiphon.Tests.Infrastructure.LandingGitTests 70s tests=65 (CP-3)
CHECKPOINT CP-4 commit=d44e36398f73ff5bd19cbf083440ccd27fd867ac build=reused filter=/*/*/(RepositoryMutationLeaseDescribeTests*)|(RepositoryMutationLeaseOwnerTests*)|(RepositoryChildJournalInspectorTests*)|(RepositoryChildRecoveryTests*)|(RepositoryFenceObserverTests*)/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/rows/CP-4/run.trx slot=granted waited=150s dirty=0 source=d44e36398f73ff5bd19cbf083440ccd27fd867ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=150s executed=110.456s wall=260.634s
PHASES CP-4 slotWait=150s build=0s startup=26.5764224s testsWall=73.8273108s teardown=8.5130786s hostWall=108.9168157s
SLOW CLASS Antiphon.Tests.Infrastructure.RepositoryChildRecoveryTests 66s tests=16 (CP-4)
CHECKPOINT CP-5 commit=d44e36398f73ff5bd19cbf083440ccd27fd867ac build=reused filter=/*/*/(RcRosterRepairTests*)|(C1514LandingSplitBudgetTests*)/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/rows/CP-5/run.trx slot=granted waited=135s dirty=0 source=d44e36398f73ff5bd19cbf083440ccd27fd867ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=135s executed=108.959s wall=244.194s
PHASES CP-5 slotWait=135s build=0s startup=6.5778059s testsWall=100.096357s teardown=0.9734661s hostWall=107.6476294s
SLOW CLASS Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests 81s tests=5 (CP-5)
CHECKPOINT CP-6 commit=d44e36398f73ff5bd19cbf083440ccd27fd867ac build=reused filter=/*/*/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/rows/CP-6/run.trx slot=granted waited=105s dirty=0 source=d44e36398f73ff5bd19cbf083440ccd27fd867ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=105s executed=7.858s wall=112.917s
PHASES CP-6 slotWait=105s build=0s startup=2.7376552s testsWall=4.0758978s teardown=0.4392518s hostWall=7.2528035s
CHECKPOINT CP-7 commit=d44e36398f73ff5bd19cbf083440ccd27fd867ac build=ok filter=/*/*/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/rows/CP-7/run.trx slot=granted waited=90s dirty=0 source=d44e36398f73ff5bd19cbf083440ccd27fd867ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=90s executed=9.678s wall=99.739s
PHASES CP-7 slotWait=300s build=132.3742234s startup=5.7319537s testsWall=1.2352727s teardown=1.3811768s hostWall=8.3483719s
CHECKPOINT CP-8 commit=d44e36398f73ff5bd19cbf083440ccd27fd867ac build=ok filter=/*/*/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/rows/CP-8/run.trx slot=granted waited=105s dirty=0 source=d44e36398f73ff5bd19cbf083440ccd27fd867ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=105s executed=3.601s wall=108.812s
PHASES CP-8 slotWait=240s build=40.710209s startup=2.4015964s testsWall=0.2182979s teardown=0.4262691s hostWall=3.0461629s
CHECKPOINT CP-9 commit=d44e36398f73ff5bd19cbf083440ccd27fd867ac build=ok filter=/*/*/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/rows/CP-9/run.trx slot=granted waited=120s dirty=0 source=d44e36398f73ff5bd19cbf083440ccd27fd867ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=120s executed=5.474s wall=125.558s
PHASES CP-9 slotWait=180s build=50.6119627s startup=3.3825363s testsWall=0.2236834s teardown=0.3541292s hostWall=3.9603487s
BUDGET wall=2130.897s execution=1388.876s admissionOnly=742.021s
unlisted: none (the tool ran no other build or test command)
wall: 35m31s  sequential-equivalent: 31m25s  builds: 4  max-concurrent-builds: 1  rows: 10 green 0 red 0 skipped
outputs: deleted bin-tk8562cp/, bin-tk8562run/, bin-tk8562pty/, bin-tk8562agt/
evidence: /work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-175553-b428/report.md
verdict: GREEN exit=0

```

## Unedited tool report — run 20261031-165356-3c92 (tool GREEN, validate row_failed)

```
--- checkpoint report ---
run: 20261031-165356-3c92   manifest: /work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/manifest.resolved.yaml
commit: 6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64  branch: feat/card-task-8562dc2a  worktree: /work/worktrees/task-8562dc2a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 state=clean buildSource=verified
CHECKPOINT CP-10 commit=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 build=n/a filter=dotnet build tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj -c Release --property:OutputPath=bin-tk8562land/ --property:AppendTargetFrameworkToOutputPath=false --property:UseAppHost=false --nologo -v q -maxcpucount:6 && test -f tools/Antiphon.LandGraphAudit/bin-tk8562land/Antiphon.LandGraphAudit.dll && pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath /work/worktrees/task-8562dc2a/tools/Antiphon.LandGraphAudit/bin-tk8562land/Antiphon.LandGraphAudit.dll executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=510s dirty=0 source=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-10 waited=510s executed=62.450s wall=572.698s
CHECKPOINT CP-1 commit=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 build=ok filter=/*/*/OwnedProcessTreeTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/rows/CP-1/run.trx slot=granted waited=330s dirty=0 source=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=330s executed=5.146s wall=335.301s
PHASES CP-1 slotWait=570s build=334.0751498s startup=3.5557765s testsWall=0.0842258s teardown=0.7048095s hostWall=4.344808s
CHECKPOINT CP-2 commit=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 build=reused filter=/*/*/RepositoryMutationLeaseTests/* executed=28 passed=28 failed=0 skipped=2 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/rows/CP-2/run.trx slot=granted waited=345s dirty=0 source=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=345s executed=140.836s wall=486.018s
PHASES CP-2 slotWait=345s build=0s startup=87.6305556s testsWall=45.3958574s teardown=4.8305614s hostWall=137.8569724s
CHECKPOINT CP-3 commit=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 build=reused filter=/*/*/LandingGitTests/* executed=65 passed=65 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/rows/CP-3/run.trx slot=granted waited=390s dirty=0 source=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=390s executed=35.555s wall=425.859s
PHASES CP-3 slotWait=390s build=0s startup=7.27233s testsWall=24.8580291s teardown=0.5935361s hostWall=32.7238952s
SLOW CLASS Antiphon.Tests.Infrastructure.LandingGitTests 86s tests=65 (CP-3)
CHECKPOINT CP-4 commit=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 build=reused filter=/*/*/(RepositoryMutationLeaseDescribeTests*)|(RepositoryMutationLeaseOwnerTests*)|(RepositoryChildJournalInspectorTests*)|(RepositoryChildRecoveryTests*)|(RepositoryFenceObserverTests*)/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/rows/CP-4/run.trx slot=granted waited=315s dirty=0 source=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=315s executed=115.834s wall=431.191s
PHASES CP-4 slotWait=315s build=0s startup=35.4632874s testsWall=76.216001s teardown=3.4100059s hostWall=115.0892985s
SLOW CLASS Antiphon.Tests.Infrastructure.RepositoryChildRecoveryTests 69s tests=16 (CP-4)
CHECKPOINT CP-5 commit=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 build=reused filter=/*/*/(RcRosterRepairTests*)|(C1514LandingSplitBudgetTests*)/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/rows/CP-5/run.trx slot=granted waited=435s dirty=0 source=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=435s executed=80.465s wall=515.701s
PHASES CP-5 slotWait=435s build=0s startup=5.3301408s testsWall=73.7487357s teardown=0.5739848s hostWall=79.6528618s
SLOW CLASS Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests 60s tests=5 (CP-5)
CHECKPOINT CP-6 commit=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 build=reused filter=/*/*/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/rows/CP-6/run.trx slot=granted waited=375s dirty=0 source=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=375s executed=13.309s wall=388.409s
PHASES CP-6 slotWait=375s build=0s startup=4.5866331s testsWall=7.2925843s teardown=0.6421892s hostWall=12.5214068s
CHECKPOINT CP-7 commit=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 build=ok filter=/*/*/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/rows/CP-7/run.trx slot=granted waited=75s dirty=0 source=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=75s executed=8.396s wall=83.455s
PHASES CP-7 slotWait=300s build=108.3165469s startup=5.0896491s testsWall=1.0375643s teardown=1.3379398s hostWall=7.4651532s
CHECKPOINT CP-8 commit=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 build=ok filter=/*/*/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/rows/CP-8/run.trx slot=granted waited=105s dirty=0 source=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=105s executed=3.809s wall=108.867s
PHASES CP-8 slotWait=495s build=53.1788853s startup=2.2954334s testsWall=0.3384492s teardown=0.5667898s hostWall=3.2006723s
CHECKPOINT CP-9 commit=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 build=ok filter=/*/*/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/rows/CP-9/run.trx slot=granted waited=90s dirty=0 source=6a65b3b20bd9c3daf6b8e1c36b342cebb424ef64 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=90s executed=5.569s wall=95.620s
PHASES CP-9 slotWait=510s build=56.5045852s startup=3.4303593s testsWall=0.5541528s teardown=0.927983s hostWall=4.9124953s
BUDGET wall=2417.619s execution=1215.169s admissionOnly=1202.450s
unlisted: none (the tool ran no other build or test command)
wall: 40m17s  sequential-equivalent: 57m23s  builds: 4  max-concurrent-builds: 1  rows: 10 green 0 red 0 skipped
outputs: deleted bin-tk8562cp/, bin-tk8562run/, bin-tk8562pty/, bin-tk8562agt/
evidence: /work/worktrees/task-8562dc2a/.antiphon/checkpoints/20261031-165356-3c92/report.md
verdict: GREEN exit=0

```
