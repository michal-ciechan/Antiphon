# Review d19080aa (round 1 of 3): C448 V36 bounded retry for the set-aside tree's delete

Subject: Code task 9f577891 (branch feat/card-task-9f577891). I reviewed the tested fix
37105c2e4e498efb7800ade0cc821267e394678d. The report commit abbda246b sits on top and changes only
`.antiphon/task-9f577891.md`. Base: master db19cbbfc.

Verdict: **one defect, D-1 (missing witness).** The production code is sound on every safety point in the
brief. One brief-mandated mutant, "drop the 'already gone after pause' check", survives the whole new
suite. The fix is test-only.

## Defect

**D-1: no witness pins the "already gone after the pause" check.**

- **Where:** `server/Infrastructure/Git/WorktreeNoFollowDelete.cs:67`, the line
  `if (IsAbsent(path)) return;` after `r.Pause(...)`. The witnesses are in
  `tests/Antiphon.Tests/Infrastructure/LandingRemovalPolicyControlTests.cs` (the C448_V36 block, ~:734-931).
- **Failure:** delete that line (mutant M7) and all 69 of 69 `C448_V36_*` cases still pass. The Code report's
  mutant table names no witness for this check.
  - On real Windows the mutant breaks the case the check exists for. A delete-pending tree that finishes
    during the 100 ms pause is walked again. `OpenNoFollow` on the missing root raises raw code 2 or 3, which
    is not transient, so the result is a spurious `worktree_removal_incomplete` with
    `set-aside: <aside>; delete_error_3` (or `_2`) instead of clean.
  - That is the same false-refusal class as the flake this task fixes.
  - On Linux `DeletePortable` treats a missing path as a no-op (`FileInfo.Attributes == -1` includes
    ReparsePoint, so it calls `File.Delete`). The fixture's seam never removes the tree during a pause. So no
    witness can see the mutant.
- **Why it matters:** the brief requires every listed mutant to go red on a named witness. The Code report
  claims this check as behaviour: "a pending delete that finished during the pause is therefore a success".
  Nothing tests that claim.
- **Fix (test-only):** add a Windows=true witness in which the tree disappears during the pause.
  1. Make the fixture's Pause hook (or a seam fault) really delete the set-aside tree after the first refusal.
  2. Make the seam's later calls throw `new DirectoryNotFoundException()` when the path is missing. This is
     what Windows raises.
  3. Assert: clean; `DeleteCalls == 1`; one pause; record cleared; branch deleted.

  Confirm it is red under the M7 mutant and green at the fix.

## Brief checks (1)-(7)

1. **Fail-closed and idempotent: sound.**
   - At attempt 5 the catch filter `attempt < SharingAttempts` is false, so the last exception reaches the
     caller's existing catch. The caller keeps the same reason (`worktree_removal_incomplete`), the set-aside
     tree and the record. Only the `; delete_error_<code>` suffix is new.
   - A delete that returned but left the tree visible returns at attempt 5. The caller's own
     `IsAbsent(aside)` re-check then gives the bare Detail. Nothing is forced.
   - Each attempt calls the unchanged `WorktreeNoFollowDelete.Delete`. That re-runs `Find` (the nested
     census, which skips reparse points at :17/:40) and the no-follow walk.
   - Every entry, including the root, is opened with `FILE_FLAG_OPEN_REPARSE_POINT`. A reparse point is never
     enumerated, and `MarkDeleted` removes the link itself.
   - So a junction inserted, or swapped in for the root, between attempts is deleted as a link only. This is
     exactly what a later resume pass (site 1) already did. The retry adds no new exposure, and the walk's
     reparse handling is unchanged.
   - One difference from a later resume pass: that pass re-runs `AuthorityAsync`, while the in-loop retry
     does not. Authority was proven immediately before the move, and the retry only extends the same
     committed walk by at most about 2.4 s. This is not a decision change.
2. **Nested repository: sound.**
   - The pre-move census and the classify gates are unchanged.
   - In the delete, `Find` throws `NestedRepositoryPreservedException` first on every attempt. It is an
     IOException with COR_E_IO (0x80131620), so `Win32Code` is not in {5, 32, 33, 145}, and the type clause
     excludes it too. It is never retried; it goes straight to `Preserve`.
   - M6 (make it transient) is killed by `C448_V36_PreservedNestedRepositoryIsNeverRetried`.
   - M8 (drop only the type clause) survives. It is equivalent, as the Code report says.
3. **"Already gone" check: sound, but see D-1.**
   - It tests only `IsAbsent(set-aside path)` and returns normally.
   - Both callers re-verify: site 1 with `!IsAbsent(aside)`, site 2 with `directoryGone && IsAbsent(aside)`.
     It cannot turn a present tree into success.
   - If `GetAttributes` throws a non-NotFound exception, that exception propagates as a refusal (fail
     closed).
4. **Detail consumers: no parser.**
   - `set-aside:` is produced and read only inside `GuardedWorktreeRemoval`.
   - The S4a policy (:343-346), `TaskWorktreeRetirementService` and `WorktreeResidueSweepService` record only
     `Residue`.
   - `AgentTaskLandingProtocol:477` passes Detail into the land outcome as free text (`AppendDetail` and
     `FormatOutcome`, cut at 4000 per CARD-1710).
   - The client and scripts have no match. The suffix breaks nothing.
5. **Timing: acceptable, stated.**
   - Worst cases per removal:
     - a lasting code-32 hold: +2.0 s (2.4 s, against 0.4 s before);
     - a lasting 5, 33 or 145, or a tree still visible: +0.4 s.
   - S4a with `MaxReleasesPerTick` 25: worst 60 s per tick if every candidate hits a lasting hold (10 s
     before), or 240 s at the maximum setting of 100. I judge this acceptable because a lasting hold on every
     candidate is pathological. A transient hold now clears in one tick.
   - Land cleanup: `failedAt` is stamped after `DeleteSettled` returns, so the 10 s extra-work budget is
     untouched. A retry pass is nominated only for `worktree_remove_failed`, so it never doubles.
   - Held during the pause:
     - The pause is `Thread.Sleep`, synchronous and not cancellable, the same as the MoveAside and Record
       retries.
     - The caller's in-process repository lease is held. `RepositoryMutationLease` has no TTL, and
       `AuthorityAsync` checks `leases.Owns`.
     - No DB transaction is held. `TryRetireAsync` runs after `ReleaseCoreAsync` commits (:216), and the
       land/S4a/sweep paths open none around the removal.
6. **Linux unchanged: confirmed.**
   - `IsTransientDelete` requires `Windows`, and the visible wait is gated by `!r.Windows`.
   - M1 is killed by `C448_V36_DeleteRefusalIsOnlyTransientOnWindows` x3; M2 by
     `C448_V36_SetAsideTreeLastingVisibleAfterDeleteStillRefuses(False)`.
7. **Runs.** All ran on server2 through build-slot, one lease each, native output, `UseAppHost=false`,
   `TEMP=/tmp`:

| run | SHA | result |
|---|---|---|
| `C448_V36_*` (red worktree; one lease build+run) | 8297fb9a2 | 69 total, **23 failed**, 46 passed, matching the claimed W1-W6 set and the first nested control |
| build `tests/Antiphon.Tests` | 37105c2e4 | 0 `error CS`, held 261 s |
| `C448_V36_*` | 37105c2e4 | 69/69 |
| affected group 1 (14 classes: the brief's 8 plus AgentTaskLandAdoption{,Concurrency}, Monitoring, Request, TargetRace{,Fake}) | 37105c2e4 | 530 total, 519 passed, 0 failed, 11 skipped |
| affected group 2 (14 classes: AbandonedLandingReconciler … AgentTaskWorktreeLockOutcome) | 37105c2e4 | 279 total, 278 passed, 0 failed, 1 skipped |
| guards (TestClassificationPolicy/Guard, SlowTestRegistryMerge, SlowTestTripwire) | 37105c2e4 | 47/47 |

The affected classes total 809, with 797 passed and 12 skipped (Windows-only). This matches the Code
claim.

### Mutants

Each mutant ran at 37105c2e4 with the `C448_V36_*` filter, one lease per build+run.

| mutant | result | killing witness |
|---|---|---|
| M1 drop `Windows &&` in IsTransientDelete | red 3 | DeleteRefusalIsOnlyTransientOnWindows (145/32/5) |
| M2 drop `!r.Windows \|\|` in the visible wait | red 1 | SetAsideTreeLastingVisibleAfterDeleteStillRefuses(False) |
| M3 retry every error | red 8 | LastingDeleteErrorIsNeverRetried x4, PreservedNestedRepositoryIsNeverRetried, OnlyTransientOnWindows x3 |
| M4 bound 5 to 50 (finite form of dropping the bound; an unbounded loop would grow `Pauses` without limit) | red 6 | LastingRefusal…NextPass x4, ResumedSetAsideDeleteIsRetriedToo(MaxValue), LastingVisible(True) |
| M5 drop the still-visible wait | red 2 | SetAsideTreeStillVisibleAfterDeleteIsWaitedOut, LastingVisible(True) |
| M6 a nested repository is transient | red 1 | PreservedNestedRepositoryIsNeverRetried |
| **M7 drop the "already gone after pause" check** | **survives 69/69** | **none (D-1)** |
| M8 drop only the `is not NestedRepositoryPreservedException` clause | survives | equivalent (COR_E_IO is not in the code set) |

### Other checks

- `scripts/check-evidence-diff.ps1 -BaseRef db19cbbfc -HeadRef 37105c2e4`: commits=3, violations=0.
- The same check to abbda246b: commits=4, entries=1, violations=0.
- `git merge-tree --write-tree origin/master <tip>` with master at f205fdcec: no conflicts, for both
  37105c2e4 and abbda246b.
- Master's changes since db19cbbfc do not touch the changed files.

## Advisories (no rerun needed)

- A-1: `Win32Code` of an exception that is neither raw nor 0x8007xxxx prints a negative number, for example
  `delete_error_-2146232800`. This is cosmetic.
- A-2: no witness exercises a real partial delete followed by a re-walk. The seam faults never delete. The
  re-walk's safety rests on the unchanged no-follow `Delete`, and that is already covered by the CARD-0665
  tests.

## Not run

- The whole Unit lane did not run (about 3 h on loaded server2). The operator accepted Interim scope, so
  `ordinaryScopeCompleted` is Interim.
- CP-25 is owed on the desktop lane: the Windows-only real-sharing tests and the `delete_error_32`
  expectations at `LandingRemovalPolicyControlTests.cs:467` and `WorktreeGuardedCleanupTests.cs:409`.
- PCs stay pending.

Cleanup: the scratch red worktree has been removed (`git worktree remove`), and this worktree's bin/obj
outputs have been cleaned (`git clean -X` limited to server, src, tests and tools).
