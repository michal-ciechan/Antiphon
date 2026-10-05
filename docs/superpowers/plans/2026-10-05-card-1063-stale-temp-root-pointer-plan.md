# CARD-1063: stale `.checkpoint-temp-roots` pointer after a retained-root test

Date: 2026-10-05. Stage: Plan (verification design folded in). Base:
`f41748999c285a6ea00a7b8bf486ec51ee92fa16`. Branch: `feat/card-task-450924c6`.
Card: Antiphon `CARD-1063`, id `1a3f1f13-77ea-4bb7-b6d4-d91de894e08f`.
Related: CARD-0820 (Done, landed `1b30bbe`; its S1 Windows Review disclosed this),
CARD-0828 (Backlog, `CheckpointTaskOwnershipTests` lifetime leak).
Complexity: **small**. Next stage: **code**. No build or test was run in Plan.

## Outcome and boundary

Two `CheckpointTempScopeTests` methods deliberately make scope teardown retain a
root, then remove that root with a bare `Directory.Delete` and never drop the
root's `.checkpoint-temp-roots/<root>.json` pointer. Every run leaves one or two
index entries whose directory is gone. The fix routes that cleanup through the
scope's own teardown once the veto is cleared, asserts in both tests that the
retained root keeps its pointer while retained and loses it when released, and
adds the missing proof that the shared sweep drops a pointer whose directory is
absent. No production code under `tools/Antiphon.Checkpoints` changes, and the
two files linked into `Antiphon.Checkpoints.LifecycleHost` do not change.

Out of scope: the two older `c723-` directories from 2026-10-04 the Review saw
(dead-owner roots are the sweep's grace/owner path, unchanged here); CARD-0828's
unjoined execution; any change to sweep budgets, interval or lock order (CARD-0820
S4 just landed them); the pending CARD-0820 Mutation inventory.

## Ground truth

Paths are repository-relative; line numbers refer to the base commit.

| Card/brief assumption | What the code does at base | Design consequence |
|---|---|---|
| `teardown_preserves_failure_and_attempts_other_roots` deletes its denied root without `Unregister`. | Confirmed: `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs:148-149` runs `Directory.Delete(denied, true)` then writes a `root-delete` usage event by hand. The index pointer written by `TempDir()` (`CheckpointTestScope.cs:54-55`) is never removed. | Fix this method (S1). |
| Only that one test leaks. | `live_child_prevents_scope_deletion` (`CheckpointTempScopeTests.cs:113-117`) has the same shape: an always-`Unknown` probe makes teardown retain the root, then `finally` bare-deletes it. The card does not name it. | Fix both methods in S1 with the same pattern. |
| The teardown it exercises must Unregister when it removes a root. | It already does: `CheckpointTestScope.DisposeAsync` deletes then calls `new CheckpointTempRootSweep().Unregister(root)` and writes `root-delete` (`CheckpointTestScope.cs:142-145`). A retained root (veto, unfinished work, uncertain child, denied delete) correctly keeps its pointer because the directory still exists. | No change to `CheckpointTestScope`. The leak is purely the tests' manual delete. A second `DisposeAsync()` on the sealed scope removes only roots that still exist and is the established cleanup pattern (`unfinished_registered_work_retains_roots:45`, `CheckpointTaskOwnershipTests.cs:885`). |
| Stale pointers accumulate until someone compacts the index. | `CheckpointTempRootSweep.SweepOnce` already drops a pointer whose directory is gone (`CheckpointTempRootSweep.cs:139-140`, skip reason `absent`), but only when the shared five-minute interval has elapsed, the coordinator lock is free, and the cursor reaches the entry inside 512 entries / 2 s. Two Review runs inside one interval therefore saw two pointers. | Keep the gated sweep as the backstop. Do not add an ungated prune (D-2). |
| A proof asserts the index has no pointer to a missing directory after teardown. | Existing assertions cover the normal path only (`CheckpointTempScopeTests.cs:60,95-96`). No test asserts a pointer survives retention or disappears on release, and no test in `CheckpointTempRootSweepTests` asserts `Skips["absent"]` or that the sweep removes the pointer (grep over `tests/Antiphon.Tests/Checkpoints` finds no `absent` skip assertion). | Add labelled assertions in both scope tests (S1) and one new sweep method (S2). |
| Consider a startup/teardown sweep that drops absent pointers. | Startup already sweeps: `CheckpointTempSweepAssemblyHook.Sweep` (`CheckpointTempRootSweep.cs:388-393`) calls `SweepOnce` under the interval gate; every `TempDir()` does too (`CheckpointTestScope.cs:60`). `Register` writes temp-then-move without the gate (`:71-93`). | An ungated prune would race another host's registration window and would erase the only evidence of a root that vanished under a live owner, the leak class CARD-0828 chases. Rejected (D-2). |
| Coordinate with CARD-0820 S2-S4 and CARD-0828. | CARD-0820 is Done; its CP-2/CP-14 rows and PC inventory select `teardown_preserves_failure_and_attempts_other_roots` by name and depend on its existing assertions. CARD-0828 is Backlog and names only `CheckpointTaskOwnershipTests.cs`. | Keep both method names and every existing assertion (D-4). No path overlap with CARD-0828; the orchestrator still defers same-area co-scheduling under the Checkpoints tests area. |
| The hand-written `root-delete` events are needed by the usage observer. | `CheckpointUsageEvent.Write` (`tools/Antiphon.Checkpoints/Execution/CheckpointUsageEvent.cs`) is a no-op without `C804_ROOT_EVENTS`; the scope writes the same event itself on deletion (`CheckpointTestScope.cs:145`). | Remove the manual writes with the manual deletes so a release produces exactly one event. |
| Changing the scope tests needs a LifecycleHost build. | `CheckpointTestScope.cs` and `CheckpointTempRootSweep.cs` are linked into `tests/Antiphon.Checkpoints.LifecycleHost`, which `tests/Antiphon.Tests.csproj:71` builds (`ReferenceOutputAssembly="false"`). | Neither linked file changes under D-1; the `tests/Antiphon.Tests` build covers compilation anyway. |
| A new checkpoint test case is free. | `Get-NamespaceCensus` holds a literal `selected = 418` (`scripts/lib/checkpoint-usage.ps1:114`) and `CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases` fails when the compiled count drifts. | S2 bumps the literal to 419 with the new method and runs the census row (CP-3). |

## Decisions

- **D-1: release a retained root through a second scope disposal, not a bare delete.**
  After the first `DisposeAsync` has thrown and the assertions on retention have run,
  the test clears its veto (deny flag off; probe verdict to `Dead`) and awaits
  `scope.DisposeAsync()` again. The sealed scope skips roots that no longer exist,
  deletes the retained one, unregisters it, and writes the usage event (`:119-146`).
  Rejected: (a) a new `CheckpointTestScope.RemoveRetained(root)` helper, which adds
  API to a LifecycleHost-linked file and duplicates the teardown's delete/unregister
  ordering; (b) adding `Unregister` beside each manual delete, which fixes the symptom
  but keeps the bare-delete pattern available for the next test; (c) leaving it,
  which is the card's defect.
- **D-2: no ungated index prune at assembly start, assembly end or scope teardown.**
  The sweep already removes an absent pointer under the coordinator gate and interval
  and counts it as a receipt skip. An ungated prune races a concurrent host's
  temp-then-move registration and silently removes the trace of a root that vanished
  beneath a live owner (CARD-0828's shape). Rejected: `[After(Assembly)]` prune;
  `DisposeAsync` unregistering roots it finds already missing. S2 instead adds the
  missing proof of the `absent` path so the backstop is trusted rather than assumed.
- **D-3: the uncertain-child test gets a settable probe verdict.** The private
  `UnknownProbe` gains a `Verdict` field (initial `Unknown`, reason `identity-unknown`;
  the test sets `Dead`, reason `identity-dead`, before the second disposal). It must
  never answer `AliveSame` for the current process: that branch kills the pid
  (`CheckpointTestScope.cs:103-113`). Rejected: registering a real short-lived child,
  which needs the process-spawn limiter and a Windows-specific exit race for no extra
  proof.
- **D-4: method names and existing assertions stay.** CARD-0820's landed rows and its
  58-PC inventory cite these methods. New assertions carry labels; old ones are
  untouched.
- **D-5: one documentation sentence.** The temp custody section of
  `docs/testing-and-build.md` states that a test releasing a deliberately retained
  root does so through a second scope disposal, never a bare `Directory.Delete`.
- **D-6: lanes.** CP-1..3 are portable and carry no placement pin; the Code task runs
  them on its own lane (server2 by `GET /api/runner-defaults`). CP-4..5 repeat the
  two changed scope methods and the new sweep method on `-Platform Windows` with no
  `-Runner` pin, because the defect was observed on the Windows Review host and
  `File.Delete` of an index entry has Windows-only sharing semantics. Repeat 1. No
  whole-Unit, namespace or assembly run.
- **D-7: census bump travels with the new case.** `selected = 419` lands in the same S2
  commit as the sweep method, and CP-3 proves it.
- **D-8: no production change.** `tools/Antiphon.Checkpoints` is untouched; the sweep's
  lines under PC-4 are mutated only during Mutation.

## Implementation slices

### S1: release retained roots through teardown (30-40 minutes)

File: `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`.

`teardown_preserves_failure_and_attempts_other_roots`:
1. Replace the `root == denied` throw with `deny && root == denied`, `deny` a local
   `bool` starting `true`.
2. After the existing `Directory.Exists` assertions add
   `File.Exists(IndexPath(denied)).ShouldBeTrue("retained-root-stays-indexed")` and
   `File.Exists(IndexPath(other)).ShouldBeFalse("deleted-root-unregistered")`.
3. Set `deny = false`, `await scope.DisposeAsync()` (must not throw), then assert
   `Directory.Exists(denied).ShouldBeFalse("denied-root-released")` and
   `File.Exists(IndexPath(denied)).ShouldBeFalse("denied-root-unregistered")`.
4. Delete the manual `Directory.Delete(denied, true)` and
   `CheckpointUsageEvent.Write("root-delete", denied)` lines.

`live_child_prevents_scope_deletion`:
1. Give `UnknownProbe` a settable `Verdict` (D-3) and construct it as a local so the
   test can flip it.
2. After the existing `identity-unknown` and `Directory.Exists(root)` assertions add
   `File.Exists(IndexPath(root)).ShouldBeTrue("retained-root-stays-indexed")`.
3. In `finally`: set the verdict to `Dead`, `await scope.DisposeAsync()`, then assert
   `Directory.Exists(root).ShouldBeFalse("released-after-dead-verdict")` and
   `File.Exists(IndexPath(root)).ShouldBeFalse("released-root-unregistered")`.
4. Delete the manual `Directory.Delete(root, true)` and the hand-written usage event.

Move the `IndexPath` helper next to the other private helpers; keep its path shape.
Keep `[Category("Unit")]`. Commit with the outcome in the message and push.

Known pre-existing exposure (not widened): the pointer assertions share the shape
of `CheckpointTempScopeTests.cs:60,95` and could in principle observe a Windows
sharing violation if another host's sweep has the entry open for the microseconds
of `ReadAllText`. No retry or sleep is added; a red there is reported with the
base-commit comparison.

### S2: prove the sweep drops an absent pointer, bump census, document (20-30 minutes)

Files: `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`,
`scripts/lib/checkpoint-usage.ps1`, `docs/testing-and-build.md`.

1. New method `absent_root_pointer_is_dropped_by_the_sweep`: `var sandbox = TempDir();
   var root = Candidate(sandbox); Directory.Delete(root, recursive: true);` (index
   entry left in place), `var receipt = Sweep(sandbox).SweepOnce();` then assert
   `receipt.Skips["absent"].ShouldBe(1, "absent-pointer-counted")`,
   `receipt.CompletedRoots.ShouldBe(0, "absent-pointer-deletes-nothing")`, and
   `File.Exists(IndexPath(sandbox, root)).ShouldBeFalse("absent-pointer-dropped")`.
   Use the class's existing `Sweep`, `Candidate` and `IndexPath` helpers.
2. `Get-NamespaceCensus`: `selected = 418` becomes `419`.
3. `docs/testing-and-build.md`, "Checkpoint temp custody and usage", after
   "Disposal removes its index file; stale entries for absent roots are removed when
   visited.": add "A test that deliberately makes teardown retain a root releases it
   with a second scope disposal after clearing its veto, never a bare
   `Directory.Delete`, so the root's index pointer leaves with it (CARD-1063)."
4. Commit and push.

## Verification design

### Inspection

- `CheckpointTempScopeTests.cs` whole file; `CheckpointTestScope.cs` whole file,
  in particular `DisposeAsync` lines 86-151 (work join, child verdict branches,
  per-root delete/unregister/event, failure aggregation) and `TempDir` 35-70
  (marker, `Register`, roster add, event, `SweepOnce`).
- `CheckpointTempRootSweep.cs`: `Register`/`WriteIndexEntry`/`Unregister` 64-100,
  the `SweepOnce` entry loop 121-140 (`absent` branch), `IsCandidate` 267-276, the
  `[Before(Assembly)]` hook 386-393. `CheckpointTempRootSweepTests.cs` helpers
  `Sweep`, `Candidate`, `IndexPath` (360-397) and
  `disposed_roots_do_not_consume_the_next_sweep_budget` (325-345).
- `scripts/lib/checkpoint-usage.ps1` `Get-NamespaceCensus`;
  `CheckpointNamespaceCensusUsageTests` in `CheckpointTempUsageTests.cs:343-366`;
  `CheckpointRoster.Compiled.cs` (argument-expanded count).
- Owners read: `docs/testing-and-build.md` (Checkpoint manifest, Combined class
  filters, Mutation positive controls, Checkpoint temp custody),
  `docs/orchestration-loop.md` stage contract.

### Proves it works now

- V-1: a retained root keeps its pointer and loses it on release | fixture unit |
  `CheckpointTempScopeTests.teardown_preserves_failure_and_attempts_other_roots` |
  first disposal still reports `original-assertion` and `delete-denied`; denied root
  exists with its pointer; other root and pointer gone; second disposal returns
  normally; denied root and pointer gone.
- V-2: an uncertain-child retention releases cleanly once the child is dead |
  fixture unit | `CheckpointTempScopeTests.live_child_prevents_scope_deletion` |
  `identity-unknown` failure, root and pointer retained; verdict `Dead`; second
  disposal returns normally; root and pointer gone.
- V-3: the gated sweep drops a pointer whose directory is absent | sweep unit |
  `CheckpointTempRootSweepTests.absent_root_pointer_is_dropped_by_the_sweep` |
  `Skips["absent"] == 1`, `CompletedRoots == 0`, index file removed.
- V-4: census matches the compiled checkpoint cases | owned pwsh child |
  `CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases`
  | `selected == 419`, no unmatched OS skip.

### Guards the regression

- R-1: the normal path still unregisters on delete |
  `completed_faulted_work_reports_failure_and_deletes_roots`,
  `sealed_scope_awaits_registered_work` | both roots and pointers gone after an
  ordinary disposal.
- R-2: disposed and orphaned entries still cost the sweep nothing |
  `disposed_roots_do_not_consume_the_next_sweep_budget` | examined 1, completed 1.

### Positive controls

Pending inventory for the post-land SourceLanding Mutation. Each row names one
compiling defect in production-side test infrastructure, one exact detecting method
and the label of the assertion that must go red. Baseline, red, restore exact bytes,
fresh green, one method filter per phase. Never craft a variant that makes the
current pid observe `AliveSame` in `DisposeAsync`: that branch kills the test host.

| PC | Behaviour | Mutation (file:line at base) | Detecting method filter | Expected red |
|---|---|---|---|---|
| PC-1 | A retained root keeps its index pointer | `CheckpointTestScope.cs:142-144`: move `new CheckpointTempRootSweep().Unregister(root);` to before `_beforeDelete?.Invoke(root);` | `/*/*/CheckpointTempScopeTests/teardown_preserves_failure_and_attempts_other_roots*` | `retained-root-stays-indexed` |
| PC-2 | Releasing a root removes its pointer | `CheckpointTestScope.cs:144`: delete the `Unregister(root)` call | `/*/*/CheckpointTempScopeTests/live_child_prevents_scope_deletion*` | `released-root-unregistered` (the same mutant also reds `deleted-root-unregistered` in V-1; one cycle) |
| PC-3 | A dead child verdict no longer vetoes deletion | `CheckpointTestScope.cs:116`: `else if (observed.Verdict is ProcessVerdict.Unknown or ProcessVerdict.ReusedPid)` becomes `else if (observed.Verdict != ProcessVerdict.AliveSame)` | `/*/*/CheckpointTempScopeTests/live_child_prevents_scope_deletion*` | second `DisposeAsync` throws `IOException` containing `identity-dead`; `released-after-dead-verdict` never reached |
| PC-4 | The sweep drops an absent pointer | `CheckpointTempRootSweep.cs:140`: delete `Unregister(root);` from the `absent` branch | `/*/*/CheckpointTempRootSweepTests/absent_root_pointer_is_dropped_by_the_sweep*` | `absent-pointer-dropped` |

Guards=4, mapped=4, missing=0. The census row has no PC: adding the S2 method
without the literal bump is itself the red-first proof of V-4 and is reported as
such only if observed.

### Checkpoints

All rows are `tests/Antiphon.Tests` Unit selections. CP-1..3 are portable rows with
no placement pin and run in the Code task's own lane. CP-4..5 require
`-Platform Windows` and no `-Runner` pin, run once at the final committed SHA by a
Windows-placed dispatch (the Code task if it is placed there, otherwise a bounded
Windows verification task before Review closes). Invoke the checkpoint tool through
`scripts/build-slot.ps1`, one committed group per command, with the exact SHA,
`--row-timeout 5m --total-timeout 10m --serial`:

```powershell
$planPath = 'docs/superpowers/plans/2026-10-05-card-1063-stale-temp-root-pointer-plan.md'
$candidateSha = git rev-parse HEAD
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1063-cp1-3 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1063-tool/ -- run --plan $planPath --rows CP-1,CP-2,CP-3 --expected-source-sha $candidateSha --row-timeout 5m --total-timeout 10m --serial
```

Windows substitutes `--rows CP-4,CP-5`. A `wait` exit 75 is continued, never
abandoned. Slot exit 4 is reported, never run unleased. Remove only the task-owned
`bin-c1063*` outputs on completion. Code and Review run
`scripts/check-evidence-diff.ps1` over the full task range.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1063/` | scope-release | `/*/*/CheckpointTempScopeTests/(teardown_preserves_failure_and_attempts_other_roots*)\|(live_child_prevents_scope_deletion*)\|(completed_faulted_work_reports_failure_and_deletes_roots*)\|(sealed_scope_awaits_registered_work*)` | V-1, V-2, R-1 | all listed; 4 executed, 0 failed/skipped | 4 | 5 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1063-s2/` | sweep-absent | `/*/*/CheckpointTempRootSweepTests/(absent_root_pointer_is_dropped_by_the_sweep*)\|(disposed_roots_do_not_consume_the_next_sweep_budget*)` | V-3, R-2 | all listed; 2 executed, 0 failed/skipped | 2 | 5 |
| CP-3 | S1-S2 | `CP-2` | census | `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases*` | V-4 | all listed; 1 executed, 0 failed/skipped | 1 | 1 |
| CP-4 | all | `tests/Antiphon.Tests -> bin-c1063-win/` | win-scope-release | `/*/*/CheckpointTempScopeTests/(teardown_preserves_failure_and_attempts_other_roots*)\|(live_child_prevents_scope_deletion*)\|(completed_faulted_work_reports_failure_and_deletes_roots*)\|(sealed_scope_awaits_registered_work*)` | V-1, V-2, R-1 | all listed; 4 executed, 0 failed/skipped | 4 | 6 |
| CP-5 | all | `CP-4` | win-sweep-absent | `/*/*/CheckpointTempRootSweepTests/(absent_root_pointer_is_dropped_by_the_sweep*)\|(disposed_roots_do_not_consume_the_next_sweep_budget*)` | V-3, R-2 | all listed; 2 executed, 0 failed/skipped | 2 | 1 |

### Cost

All numbers are estimates.

- Ordinary V/R floor (Code): **18 minutes**, the sum of EstimatedMinutes
  (CP-1 5, CP-2 5, CP-3 1 on Linux; CP-4 6, CP-5 1 on Windows). Three isolated
  builds at about 4 m each, five filter runs at about 1 m each. Expected execution
  floor: 13 TUnit results.
- Authoring: S1 30-40 m, S2 20-30 m. Code `-ExpectAbout` is about 70-90 m
  including the Linux rows; the Windows pair is a separate 10-15 m dispatch when
  Code is not placed on Windows.
- Mutation PC floor: **44 minutes** on the SourceLanding lane: three distinct
  detecting methods get one baseline each (3 x 4 m), then four red/restore/green
  cycles at 8 m.

## Handoff

Code consumes this plan at its committed SHA:
`checkpoints: docs/superpowers/plans/2026-10-05-card-1063-stale-temp-root-pointer-plan.md@<plan commit> section "### Checkpoints"`.
Scope for the Code task: `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`,
`tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`,
`scripts/lib/checkpoint-usage.ps1`, `docs/testing-and-build.md`. Defer a Code dispatch
while a CARD-0828 Code task is in flight in the Checkpoints tests area.
