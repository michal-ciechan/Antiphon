# CARD-1767 Review, ordinary round 1 (Final), task f43511cc

Verdict: CLEAN. No blockers. Three advisories, none a landing condition.

Subject: `origin/feat/card-task-2b28a21a` tip `7f580e0bd598348df8717b42f5a689e373dbd708`
(Grok Code task `2b28a21a-d8cf-4a2b-9e6a-501675446482`). Base `74a8004f405bc54666fea33889839f0ff4d62ec7`.
Red commit `40556d7807ad6bc90935dfc4f10e921858a0a7fa`. Master at review time
`6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa`. Diff base..tip: 3 files, +214/-4
(`server/Infrastructure/Data/WorkspaceReservationJournal.cs`,
`tests/Antiphon.Tests/Application/C1767ReflessOverlapTests.cs` new,
`tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.cs` pin 45 -> 44).
`tests/test-execution-policy.json` unchanged. No plan document and no `### Checkpoints`
table exist for this card (advisory follow-up from review 9758fc71 A-1, no Plan stage), so the
Code report's named runs are the checkpoint source.

## Blocker checks

### B-1 The early return is dead logic for ref-less keys: PASS (derived from code)

Pre-change predicate per row (`WorkspaceReservationJournal.cs:269-274` at base):

    Same(r, n) || (requestingRefOwner || rowOwner in refOwners)
                  && n.SourceFullRef.Length != 0
                  && NormalizeRef(r.SourceFullRef) == n.SourceFullRef
                  && PathsEqual(NormalizeRepository(r.CommonDirectory, r.CanonicalPath), n.CommonDirectory)

with `n = key.Normalized()`. When `n.SourceFullRef.Length == 0` the second disjunct is false for
every row whatever `refOwners`, `requestingRefOwner`, or the requester's `DesktopWorkspaceKind`
contain, so the result is exactly `rows.Where(r => Same(r, n))`. The skipped owner SELECT is
`AsNoTracking`, has no side effects, and feeds only `refOwners`/`requestingRefOwner`, which only
appear inside the guarded disjunct. The new return sits before any use of `taskId`.

- RefOnly meaning: `DesktopWorkspaceKind { Checkout = 0, RefOnly = 1 }` on `AgentTasks`. The
  CARD-0824 fence (commit 43d819da71) is "when either owner is recorded RefOnly, also fence an
  exact nonempty full ref" in the same repository. By construction it never fences by path, by
  repository alone, or by kind alone; the kind only widens matching to an exact equal nonempty ref.
- Overlap by path for a ref-less requester is `Same()`: `RefsCompatible` treats an empty ref on
  either side as compatible, so a ref-less key already overlaps every row on the same canonical
  path, RefOnly rows included; that is unchanged. A different-path RefOnly row never overlapped a
  ref-less key before (exact nonempty ref equality required), so nothing is lost.
- Whitespace: `Normalized()` -> `For()` -> `NormalizeRef` returns "" for null/whitespace after
  `Trim()`, before the `refs/` test; the pre-change guard used the same normalized value. Order is
  trim/normalize then compare in both versions.
- All four callers go through the one private `OverlapAsync`: `ReadActiveAsync(key)`,
  `ReadActiveAsync(key, liveOwnersOnly)` (both pass `taskId: null`), `TryAdmitConsumerAsync`
  (`command.TaskId`) and `TryClaimRetirementAsync` (`command.TaskId`). Retirement claims build
  their key from `retirement.SourceFullRef` (`TaskWorktreeRetirementService.cs:693`), normally
  nonempty, so they stay on the unchanged ref-bearing path; an empty retirement ref would have
  been `Same()`-only before as well.
- Production ref-less producers: the dispatcher's `ForTask(WorktreePath, WorkingDirectory,
  WorktreeBranch, RepoPath)` (`AgentTaskDispatcher.cs:5828`) with no branch for Shared/ReadOnly
  tasks, and `AgentSessionService.cs:3797` where `branch` starts as "". That is the
  initial-refused-dispatch fixture shape, and CP-2 measured 44 at the tip.

### B-2 Same() rows returned identically: PASS

`rows.Where(r => Same(r, normalized)).ToList()`: same predicate, same `normalized` value, same
`Where` ordering, same `List<WorkspaceUseReservation>` type as the pre-change expression with a
false second disjunct.

### B-3 Witnesses real: PASS

- W1 `Refless_key_skips_the_owner_select_and_keeps_same_path_overlap`: loops "", "   ", "\t";
  asserts the normalized ref is empty; for each, both `ReadActiveAsync` overloads return exactly
  the same-path row and 0 owner reads, `TryAdmitConsumerAsync` is refused `workspace_reserved`
  with 0 owner reads, `TryClaimRetirementAsync` is refused `workspace_in_use` with 0 owner reads;
  then an empty-row admit with the requester (a RefOnly task) in the id set is accepted with 0
  owner reads. Owner reads are counted as SQL containing `DesktopWorkspaceKind` through
  `FullCommandCounter`; in these paths only the owner SELECT carries that column
  (`FindOrphanedLaunchRowsAsync` projects Id/Status/LandRequestedAt and the rows are Retirement,
  so it issues nothing). Outcome assertions, not self-compares.
- W2 `Ref_bearing_key_still_reads_owners_and_fences_refonly`: cross-path read returns only the
  RefOnly owner's row with 1 owner read; a RefOnly requester is fenced by a Checkout row on the
  same ref (`workspace_in_use`, 1 read); a Checkout requester against a Checkout row on another
  path is accepted (1 read).
- Red at base: at `40556d7807` the SELECT runs on every call, so W1 line 55 fails with 1 vs 0 and
  the budget case `(initial-refused-dispatch, 44)` measures 45. Code reports exactly that (overlap
  2 run / 1 failed, slot ec14ed45; budget 14 run / 1 failed, slot e37a88bb). I derived the red
  from the code and did not rebuild the red commit (a second cold build was 22 min at load 34-62).
- Mutants M1 (drop the return -> W1:55 reads 1), M2 (return empty -> W1:54 ids), M3
  (unconditional skip -> W2:124 fence empty) match the code; plausible kills. PC-1..PC-3 stay
  pending for method-scoped SourceLanding Mutation (filters below).

### B-4 Pin 44 accurate, no other row changed: PASS

Only `[Arguments("initial-refused-dispatch", 45)]` -> `44` changed; the other 13 rows are
byte-identical in the diff. CP-2 ran all 14 cases green at the tip, including
`C1149_C1150_Statement_budgets(initial-refused-dispatch, 44)`.

## Rerun (one checkpoint-tool run, run `20261108-031828-f114`)

Plan extract `.antiphon/c1767-review-r1-plan.md` (ignored), one build `bin-c1767-rv1/`, three
serial rows. Tool bootstrap build under its own lease (19ceb712, 28 s). Receipt validated:
`CHECKPOINT SOURCE VALID source=7f580e0bd598348df8717b42f5a689e373dbd708 rows=3`.

    source: 7f580e0bd598348df8717b42f5a689e373dbd708 state=clean buildSource=verified
    CHECKPOINT CP-1 commit=7f580e0bd598348df8717b42f5a689e373dbd708 build=ok filter=/*/*/C1767ReflessOverlapTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-f43511cc/.antiphon/checkpoints/20261108-031828-f114/rows/CP-1/run.trx slot=granted waited=60s dirty=0 source=7f580e0bd598348df8717b42f5a689e373dbd708 sourceState=clean buildSource=verified
    CHECKPOINT CP-2 commit=7f580e0bd598348df8717b42f5a689e373dbd708 build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-f43511cc/.antiphon/checkpoints/20261108-031828-f114/rows/CP-2/run.trx slot=granted waited=60s dirty=0 source=7f580e0bd598348df8717b42f5a689e373dbd708 sourceState=clean buildSource=verified
    CHECKPOINT CP-3 commit=7f580e0bd598348df8717b42f5a689e373dbd708 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=75 passed=75 failed=0 skipped=0 trx=/work/worktrees/task-f43511cc/.antiphon/checkpoints/20261108-031828-f114/rows/CP-3/run.trx slot=granted waited=270s dirty=0 source=7f580e0bd598348df8717b42f5a689e373dbd708 sourceState=clean buildSource=verified
    PHASES CP-1 slotWait=60s build=1336.3575974s startup=30.6174198s testsWall=4.9457572s teardown=7.5294116s hostWall=43.0927482s
    SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 150s tests=39 (CP-3)
    unlisted: none (the tool ran no other build or test command)
    wall: 35m18s  sequential-equivalent: 11m01s  builds: 1  rows: 3 green 0 red 0 skipped
    verdict: GREEN exit=0

CP-1 TRX names: `Refless_key_skips_the_owner_select_and_keeps_same_path_overlap`,
`Ref_bearing_key_still_reads_owners_and_fences_refonly`. CP-2 TRX lists all 14 `[Arguments]`
cases. Totals: 91 executed, 91 passed, 0 failed, 0 skipped.

## Code report audit

- Runs named with leases: red overlap (ec14ed45), red budget (e37a88bb), build (ad585b2f), green
  overlap (570e73bc), green budget (f49eb451), restore rebuild (6bbddba4), guards (11f2ac4a,
  75/75, same eight classes as CP-3). Unlisted runs: none beyond A-1.
- `scripts/check-evidence-diff.ps1 -BaseRef 74a8004f4 -HeadRef 7f580e0bd`: commits=2
  entries=0 violations=0, exit 0.
- Landable: `git merge-tree --write-tree origin/master HEAD` (origin/master 6ccf8896e) clean,
  tree `38e2e9fa6f6c6863752328dcad65e7f975b89b3d`, matching the Code report.
- No OS branch in the change (no platform conditionals; `PathsEqual` casing is pre-existing).
  Performance: W1/W2 run in about 5 s of test wall inside a 120 s class timeout; the budget
  method is pre-existing; no new Slow class.

## Advisories (not landing conditions)

- A-1 The Code report gives no build-slot lease ids for the M1-M3 mutant builds/runs; every
  other run names one. Mutants are not evidence for this round (PCs pending), so this is a
  reporting gap only.
- A-2 `WorkspaceReservationJournal.cs:271` keeps `normalized.SourceFullRef.Length != 0` after
  the early return makes it always true. Harmless, defensive redundancy; a later tidy may drop
  it together with a test that keeps the ref-bearing fence.
- A-3 Budget: the brief allowed 60 min; the review took about 48 min wall, 35m18s of it the
  checkpoint run whose cold `Antiphon.Tests` build was 22.3 min at host load 34-62.

## Scope statement

`ordinaryScopeCompleted: Full` for the selection the brief commissioned (overlap class, budget
method with its 14 cases, eight-class guard batch; 91 tests). The whole `Antiphon.Tests` Unit
lane was not part of that selection and did not run here (Code did not run it either; it is
about three hours on server2). The caller may downgrade to Interim if the Unit lane is required.

## Pending method-scoped PCs for SourceLanding Mutation

- PC-1 `--treenode-filter "/*/*/C1767ReflessOverlapTests/Refless_key_skips_the_owner_select_and_keeps_same_path_overlap"`:
  M1 remove the early return (expect line 55, reads 1 vs 0); M2 return an empty list (line 54).
- PC-2 `--treenode-filter "/*/*/C1767ReflessOverlapTests/Ref_bearing_key_still_reads_owners_and_fences_refonly"`:
  M3 make the skip unconditional (line 124, cross-path RefOnly row missing).
- PC-3 `--treenode-filter "/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets"`:
  M1 (expect `(initial-refused-dispatch, 44)` measures 45).
