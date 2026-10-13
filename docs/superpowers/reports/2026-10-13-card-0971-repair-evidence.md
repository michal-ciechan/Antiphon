# CARD-0971 repair round 2 evidence

Landing owner: 520a9bf3-d0ab-427b-a036-88323a621750.
Original Code task: bc957ed9-9a6a-4503-a99d-fce2f677d4ec.
Branch: feat/card-task-520a9bf3. Worktree: /work/worktrees/task-520a9bf3.
Repair base: 46fe27d2f85d996e5de6c57ea84da55ab74731a0.
Full candidate base for evidence policy: 02275c3ba8ec6969a6c7bbec8dbe48609367d045.
Plan: docs/superpowers/plans/2026-10-12-card-0971-ignored-nested-repo.md.
Final actual checkpoint outcomes/receipts and publication SHA are recorded in
/work/worktrees/task-520a9bf3/.antiphon/task-520a9bf3.md after verification.
This tracked report is frozen before final source qualification; final outcomes
are pending at its commit, not presumed green.

## Repair and preservation policy

The newly admitted ignored independent repository previously reached guarded
publication cleanup, where disposable obj/** classification hid the private
repository and managed recursive deletion removed it. The repair adds a
filesystem-only census at both cleanup readings and a guard at managed deletion,
including previously recorded set-aside trees. Any own .git entry protects a
directory, including a directory, gitdir file, or reparse marker. The census
never enters a recognized private repository or runs Git/config/filters there.
Unknown filesystem evidence fails closed. Root checkout administration is excluded
from the nested census; common-directory administrative deletion keeps its prior
narrow scope. No assertion or timeout was relaxed.

The existing plan policy is preserved: publication succeeds; intentional residue
reports cleanup Refused with ignored_content_preserved and protected paths. An
already-visible repository retains the original worktree, registration and branch.
A repository inserted after unregistering survives in the recorded set-aside tree,
whose path is reported; its branch remains and later deletion resumes preserve it.
The strict half-reset proof still refuses .git files; ordinary reviewed adoption
is independently tested for pointer preservation after publication.

## Every subsequent deletion path audited

| Path | Behaviour for nested repository |
|---|---|
| AgentTaskLandSourceResolver -> reset --hard at the owner | Existing collision/byte/index proof refuses obstructions; reset leaves admitted unrelated ignored repositories alone. No owner git clean call exists. |
| AgentTaskLandingProtocol -> WorktreeManager -> GuardedWorktreeRemoval, publication | Both filesystem censuses override ignored disposable/evidence globs. Visible .git keeps the whole original owner tree, registration and branch; ignored_content_preserved is reported. |
| GuardedWorktreeRemoval, LocalMerge / SettledTask retirement / completed-card publication | Same classification and deletion guards; retirement cannot borrow permission to delete a private repository from obj/**. |
| GuardedWorktreeRemoval -> WorktreeNoFollowDelete, initial and recorded set-aside resumes | Protected preflight before deletion, plus marker rechecks during no-follow walk. No recognized private repository is traversed/deleted; late discovery retains record/tree/branch and reports the set-aside path. |
| LandingGit.UnregisterWorktreeAsync / WorktreeSetAside.TryClear -> WorktreeNoFollowDelete | Exact common-directory administrative entry only; does not follow gitdir pointers or delete owner checkout bytes. Existing admin deletion remains distinct from guarded checkout deletion. |
| LandWorkspace.EnsureAsync -> git clean -fdx | Runs only in the operation's separate detached landing workspace, never the accepted owner checkout. Single-force Git clean retains recognized independent repositories; no double-force clean exists. |
| GuardedVerificationRemoval | Separate verification authority, complete exact-file inventory and frozen output hashes; private administrative subtree fails inventory. No publication recovery path routes here. Uses exact file deletion, empty ancestor removal and nonforcing Git worktree removal. |
| WorktreeManager.RollbackFailedAddAsync | Creation rollback only, requires empty status including ignored paths, no force remove. An ignored nested repository prevents rollback. |
| WorktreeManager.TryHealStaleRegistrationAsync | Reconstructs a missing creation-owned checkout; no recursive deletion. |
| WorktreeDiagnosticControls disposal | Deletes only its own separately created diagnostic control scratch root, never the owner worktree. |

## Authentic pre-fix proof

Production in both test-only commits is byte-identical to repair base 46fe27d2.
Fresh TRX inspected for actual class/method/parameter rows and nonzero execution.
Initial CP-10: 7 executed, 3 passed, 4 failed. Three preservation rows reproduced
missing .git after confirmed publication; one new refusal assertion confused the
strict proof reason with its documented queue-level source_dirty mapping.
The test-only correction asserts both the exact strict proof reason and the exact
mapped queue reason, strengthening the check without changing production.

Corrected CP-10: 8 executed, 4 passed, 4 failed. All four refusal/cleanup controls
pass. The vulnerable obj, deeper obj, gitdir-file publication and late set-aside
rows fail at actual missing .git assertions, not build/fixture errors. The ordinary
ignored/nested row already preserves on base and cannot honestly be called red.
The late deletion test checks initial and resumed deletion after the repair.

CHECKPOINT CP-10 commit=987092a64fce4825baa73e3956e3357b105e8d69 build=ok filter=/*/*/AgentTaskLandNestedRepositoryCleanupTests/* executed=7 passed=3 failed=4 skipped=0 trx=/work/worktrees/task-520a9bf3/.antiphon/checkpoints/20261013-010801-0dd5/rows/CP-10/run.trx slot=granted waited=105s dirty=0 source=987092a64fce4825baa73e3956e3357b105e8d69 sourceState=clean buildSource=verified
PHASES CP-10 slotWait=135s build=192.5492641s startup=62.098018s testsWall=40.872742s teardown=1.8816686s hostWall=104.8524334s
CHECKPOINT CP-10 commit=59b02a7d1bb6f0183f85d6e04bc24982a654f756 build=ok filter=/*/*/AgentTaskLandNestedRepositoryCleanupTests/* executed=8 passed=4 failed=4 skipped=0 trx=/work/worktrees/task-520a9bf3/.antiphon/checkpoints/20261013-011656-2dd4/rows/CP-10/run.trx slot=granted waited=45s dirty=0 source=59b02a7d1bb6f0183f85d6e04bc24982a654f756 sourceState=clean buildSource=verified
PHASES CP-10 slotWait=75s build=102.9291141s startup=37.0265746s testsWall=26.4180777s teardown=1.3891383s hostWall=64.8338067s

Baseline build slots: granted, waited=30s each. Row waits: 105s then 45s.
Clean receipts carry matching tested SHA and buildSource=verified. Actual builds
are bin-c971-repair-base/; the other declared manifest builds are unused.
Infrastructure bootstrap: build-slot.ps1, isolated bin-c971-repair-tool/,
UseAppHost=false, granted waited=0s, zero warnings/errors in retained bootstrap log.
A launcher initially omitted UseAppHost=false and failed before any checkpoint;
an incorrectly copied expected SHA was also refused before any driver. Neither
is test execution. The corrected bootstrap/run properties and actual receipts
are retained in the task's ignored evidence. No unleased or NoSlot execution.

Final closed list: CP-2..6, CP-11..16, one actual bin-c971-final/ test build reused
by every row, explicit UseAppHost=false on the tool and automatic Linux default on
manifest drivers. Complete named classes bind the impact; no full assembly run or
unbounded class is proposed. Unit/R-5/CP-7..9 is expressly excluded by this repair
brief (CARD-1209/CARD-1210), never marked passed or silently repaired.
No deliberate Mutation cycle ran. PC-1..11 and all variants remain pending for
method-scoped SourceLanding Mutation after Code, Final Review and caller landing.
No green repetitions are required. Restart: none. Next: Review after ordinary
verification completes; Code if any ordinary repair/verification remains.
