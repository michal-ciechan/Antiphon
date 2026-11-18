# CARD-1981 owned temp producers: Final review round 1

Review task `8ff0e185`. Subject: Code task / landing owner `6af33109-b5ef-426f-960f-83619dc327a1`,
branch `feat/card-task-6af33109`, tip `192c8d63b62c8d73e1f1093a3c4c15338b1fb1fe`.
Base and master: `997f9b8d9ffc4ba870d2fd56caf7c03781591624`. Range reviewed: base..tip (4 commits).
Author: Grok. Reviewer: Claude Opus.

Verdict: **two blockers** (D-1, D-2). Everything else passes.

## Runs at the reviewed SHA

Every build and test ran on server2 through `scripts/build-slot.ps1`, one lease per command, in
the native default output with `UseAppHost=false` and `TEMP=/tmp`. These were direct runs, not
checkpoint-tool runs, so there are no SHA-validated receipts.

| Row | Selection | Result |
|---|---|---|
| build | `dotnet build tests/Antiphon.Tests` | 0 errors, 2m45s |
| CP-1 + touched + guards | `(OwnedTempLeakTests*)\|(TaskWorktreeRetirementTests*)\|(RemoteWorkspacePreparerTests*)\|(AgentTaskLandStageOutcomeTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)` | 144/144. OwnedTempLeak 8, TaskWorktreeRetirement 40, RemoteWorkspacePreparer 27, AgentTaskLandStageOutcome 16, TestClassificationPolicy 42, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3 |
| touched (large) | `(DelegationWorktreeTests*)\|(DelegationDispatchRecoveryBoundaryTests*)` | 440/440. DelegationWorktree 58, DelegationDispatchRecoveryBoundary 382 (this includes the c1150 producer) |
| rh producer | `(RcHistoricalEvidenceTests*)` | 3/3; the `/tmp/rh-*` count stayed at 14 |
| build | `dotnet build tests/Antiphon.SessionRunner.Tests` | 0 errors |
| CP-2 + sweep | `(C1677Temp*)` in SessionRunner.Tests | 52/52. FamilyRegistry 3, TempSweep 31, TempSweepDispatch 11, TempSweepNative 7 |
| CP-3 | `audit-land-verification-graph.ps1 -Check` (Release LandGraphAudit built first) | exit 0, `approved` |
| evidence | `check-evidence-diff.ps1` base..tip | commits=4 entries=0 violations=0 |
| merge-tree | `git merge-tree --write-tree origin/master HEAD` | clean, tree `2e6c25b8259920e66274daf9904241b8256b4565` |

Red-first: the claim that S1 `d97ed45e` had 2 failures was not re-executed. It is consistent
with the S1 source. At S1, `RetirementScratch.Release` returns without deleting, and the bridge
delete ran after the cleanup returned, not in `finally`.

The nightly pins were not recomputed. No nightly policy or selector input is in the diff.

The SessionRunner `C1677Temp*` run started at a 1-minute load of 24.7. The load check was folded
into the same command instead of gating it. The run took 10 s.

`TempSweep` stays disabled: `server/appsettings.json` keeps `TempSweep:Enabled=false` and
`Execute=false`, and the diff does not touch either key. The eight new families take the
`DefaultEnabled=true` default. They would sweep as soon as the master switch is turned on.

## Blockers

### D-1: `remove-review-scratch.ps1` deletes outside the container and deletes dirty nested worktrees

- **Where:** `scripts/remove-review-scratch.ps1:130-141`, at the child loop and the final
  `Remove-Item -Recurse -Force`.
- **Failure:** this was reproduced in the review scratchpad with a throwaway repository.
  1. *Symlinked child.* The container `antiphon-review-0123abcd` held the marker and a child
     `link -> <a clean linked worktree outside the container>`. The script exited 0. The foreign
     worktree directory was deleted and unregistered from its repository.
     `[IO.Directory]::GetDirectories` lists the link, `Join-Path $child '.git'` resolves through
     it, and `git worktree remove` acts on the target.
  2. *Nested worktree.* The container held `sub/wt`, a linked worktree with an uncommitted file.
     The script exited 0. The uncommitted file was deleted with the rest of the container, and
     the repository was left with a `prunable` registration. Only direct children are checked for
     `.git`. Anything deeper falls to `Remove-Item -Recurse -Force`, which is stronger than
     `git worktree remove --force`.
- **Why:** this is a delete path on server2's shared `/tmp`, where other tasks' clean mirrors
  and worktrees live. A reviewer who links their own mirror into the container for convenience
  can lose a clean mirror that belongs to the same repository. The documented contract ("never
  `--force`", "a failed worktree remove leaves the container") does not hold.
- **Fix:** do not follow links, and refuse on doubt:
  - Refuse when the container is a reparse point or symlink. Also refuse when any entry under it
    is one; use `LinkTarget` or `Attributes.ReparsePoint` and do not recurse into links.
  - Scan the whole container without following links. Refuse if a `.git` appears anywhere except
    directly under a top-level child.
  - For each child, require that `git worktree list --porcelain` from its common directory names
    exactly that child's path before calling `git worktree remove`.
  - Add `OwnedTempLeakTests` witnesses for a symlinked child that points at an outside worktree
    (the outside worktree must survive) and for a dirty nested worktree (the container must be
    kept). Both must be red at `192c8d63`.

### D-2: `OwnedTempLeakTests` starts child processes with no spawn limiter

- **Where:** `tests/Antiphon.Tests/TestHelpers/OwnedTempLeakTests.cs:507-509`, the class
  attributes. `RunScriptRaw` starts `pwsh`, and the scripts start `git`.
- **Failure:** three methods each spawn one or two `pwsh` children, plus `git`, with no
  `ParallelLimiter`. In the full `Antiphon.Tests` run they compete outside every lane. Each
  child has a 40-second `WaitForExit`, and the class `[Timeout(45_000)]` on loaded server2.
- **Why:** AGENTS.md (Tests and builds) and `docs/testing-and-build.md` both say a new class that
  starts a child must take one of the assembly-local limiters. The roster tests are floors, so no
  guard catches it.
- **Fix:** add `[ParallelLimiter<ProcessSpawnLimit>]`. The strict lane is right here because the
  children run pwsh scripts from the shared checkout.

## Advisories (stand; file cards; do not hold the land)

- **A-1:** `remove-review-scratch.ps1` can never remove a container that holds a standalone
  clone. `git worktree remove` refuses with "is a main working tree", which was reproduced, and
  the container is kept. `new-review-scratch.ps1` says "Clone into a child", so the documented
  use always fails closed. Only empty containers and linked-worktree children work.
  `docs/docker-stack.md` should say so, or clones should get their own audited removal.
- **A-2:** the script requires the marker to exist but never compares its owner text with the
  caller. Any task can remove any other task's marked container. Consider `-TaskId` or
  `-OwnerWorktree` and a byte match with the marker.
- **A-3:** `Hook_removes_a_root_the_test_left_behind` (V-3, second half) proves nothing new. Its
  own `[After(Test)]` method calls `OwnedTempRoot.Release` and asserts it, which duplicates
  `Owned_root_release_removes_the_directory`. It never exercises
  `OwnedTempRootAssemblyHook`/`[After(Assembly)]`. The `ReleaseAll` comment ("after every test")
  is wrong: the hook runs after the assembly.
- **A-4:** the RemoteWorkspacePreparer worktree base `antiphon-remote-prep-<32hex>` is deleted on
  dispose but is not a registered family. A killed host leaks it with no sweep handle.
- **A-5:** the card's slice 2 asked for a guard that records `/tmp` before and after a class.
  That guard is not delivered. The witnesses cover individual producers only.

## The six-character directories: the honest limit

This change does not stop that debris. A read-only count on server2 `/tmp` during the review
(2026-11-18, about 19:42Z) found the following top-level `[A-Za-z0-9]{6}` directories, all empty
and all owned by `app`:

| Window | New directories |
|---|---|
| last 60 minutes | 62 |
| last 6 hours | 102 |
| last 24 hours | 369 |
| last 48 hours | 1,651 |
| total present | 2,454 |

Eight more appeared during the review's own 8-minute build and test window. Seven tasks share
the host, so they cannot be attributed. They are still being produced, and roughly one appears
every one to five minutes. The two repository `Directory.CreateTempSubdirectory()` calls this
change removed deleted their directory in the constructor, so they were not the producer. The
plan says the same.

What would confirm it:

1. Find the producer: inotify or strace on `mkdir` in `/tmp` in a quiet throwaway container
   during one test shard and one build.
2. Then compare the hourly rate of new six-character directories on server2 before and after
   that fix reaches rc. This is read-only: `find /tmp -maxdepth 1 -type d -regextype
   posix-extended -regex '/tmp/[A-Za-z0-9]{6}' -mmin -60 | wc -l`.

Landing this change is not expected to move that rate. **CARD-1981 must stay open** for slice 1
(the unobserved producer), the slice-2 class guard, and the existing debris. That debris is
10,146 `antiphon-bridge-queue-*`, 2,718 `c459-*`, 2,631 `c487-tw-*` and 627
`antiphon-remote-prep*` today, and it waits on the disabled sweep.

c1150, the rh script and RemoteWorkspacePreparerTests were not executed by Code. This review ran
all three: DelegationDispatchRecoveryBoundary 382/382, RcHistoricalEvidence 3/3 and
RemoteWorkspacePreparer 27/27. They are green, so that gap no longer matters.
