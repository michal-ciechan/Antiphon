# CARD-1981 owned temp producers

Code task / landing owner: `6af33109-b5ef-426f-960f-83619dc327a1`.
Base: `997f9b8d9ffc4ba870d2fd56caf7c03781591624`.
Branch: `feat/card-task-6af33109`.

## Implementation

S1 commits the two leak witnesses against the old cleanup. At that SHA,
`RetirementScratch.Release` is a no-op and bridge cleanup deletes only after
the database cleanup returns, so a throw leaves the directory. S2 deletes what
each owned producer creates and registers eight temp-sweep families. The sweep
stays off. Nothing on server2 is deleted.

The six-character shape was the two `Directory.CreateTempSubdirectory()` calls
with no prefix in `DelegationWorktreeTests` and `AgentTaskLandStageOutcomeTests`.
Both deleted that directory in the constructor. Those calls are gone. The census
still calls the live process that left 21,579 such directories unobserved.
`c487-tw`, `c487-reg` and `antiphon-c448` already delete on the normal path;
S2 only registers them. `c1150` is tracked and released when the test assembly
ends. A killed host can still leak; the registered prefix is the later sweep's
handle.

## Performance budget

CP-1 and CP-2 are one class each, serial, under 10 minutes of tests. The first
row's 15 minutes is the cold-build budget. No repeat block after green. No
whole slow class. The guard union stays out: classification, selector and pin
inputs were not edited (operator 2026-11-18). No positive controls. Cost floor
is the sum of `EstimatedMinutes` (28).

## Unlisted runs

- Red witness run of the two S1 tests, before this plan existed: red-first proof.
- Red run of CP-4 and CP-5 at `5398ff1867635444544e392d7e4e5ed225213221`, before
  the remover refused links and nested git. Both failed: the foreign README and
  the uncommitted file were deleted. The other eight class methods passed.
- One green re-run of `OwnedTempLeakTests` before the S2 commit: the class hook
  had been rewritten and CP-1 repeats that selection on a cold output.
- Builds of `tests/Antiphon.Resilience.Tests`, `tests/Antiphon.GitInProcess.Tests`,
  `tests/Antiphon.Observability.Tests`, `tests/Antiphon.SessionRunner.Tests` and
  `tests/Antiphon.Checkpoints.LifecycleHost`: CARD-1996, because
  `tests/Antiphon.Tests/TestHelpers` changed. None of those projects compiles
  `OwnedTempRoot.cs` or `BridgeQueueHarness.cs`.
- `scripts/regenerate-derived-impact-index.ps1` twice, after its driver build.
  The json is the last commit only when the first run prints changed.
- Native Release build of `tools/Antiphon.LandGraphAudit` so CP-3 can `-Check`.
- Evidence diff, merge-tree, OS-witness lint and the three nightly pin prints.
  They are not builds or test filters. Results are reported with the checkpoint
  lines.

## Verification design

- V-1: `c459` release removes its scratch directory.
- V-2: the bridge temp root is removed when cleanup throws, and kept when the
  database is preserved.
- V-3: an owned root is removed by `Release`, and by the class hook when the
  test leaves it.
- V-4: `remove-review-scratch.ps1` deletes one marked container, keeps a
  container with no marker, and keeps the container when `git worktree remove`
  fails. Never `--force`. It refuses a symlink or other reparse point, including
  a child that links a foreign worktree, and it refuses a `.git` below the
  top-level children, including a dirty nested worktree. The foreign worktree
  and the uncommitted file stay. A standalone clone child is refused and the
  container stays; this script does not delete that clone.
- R-1: the family table has 20 families, including `c459`, `c487-tw`,
  `c487-reg`, `antiphon-c448`, `antiphon-c1150`, `antiphon-remote-prep-ws`,
  `rh` and `antiphon-owned`. Each new pattern is pinned to a producer literal.
  `TempSweep` is not enabled.
- R-2: land-graph `-Check` exits 0. If the 5-second clock hits, that command
  is rerun once alone (CARD-1991).
- R-3: evidence diff over the full task base..HEAD, a conflict-free merge-tree
  against fetched `origin/master`, OS-witness inventory, and the three nightly
  pins recomputed and unchanged.

No PC variants. Mutation is waived for this Code task (operator 2026-11-12).
Every PC stays pending for method-scoped SourceLanding Mutation. Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S2 | tests/Antiphon.Tests -> bin-c1981-proof/ | leak-witnesses | `/*/*/OwnedTempLeakTests/*` | V-1, V-2, V-3, V-4 | 10 methods, 0 failed/skipped | 10 | 15 | true |
| CP-2 | S1-S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1981-families/ | family-registry | `/*/*/C1677TempFamilyRegistryTests/*` | R-1 | 3 methods, 0 failed/skipped | 3 | 8 | true |
| CP-3 | S2 | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll` | R-2 | exit 0 | n/a | 5 | true |
| CP-4 | repair | tests/Antiphon.Tests -> bin-c1981-proof/ | symlink-witness | `/*/*/OwnedTempLeakTests/Review_scratch_remove_refuses_a_symlinked_foreign_worktree` | V-4 | 1 method, 0 failed/skipped | 1 | 5 | true |
| CP-5 | repair | tests/Antiphon.Tests -> bin-c1981-proof/ | nested-witness | `/*/*/OwnedTempLeakTests/Review_scratch_remove_refuses_a_dirty_nested_worktree` | V-4 | 1 method, 0 failed/skipped | 1 | 5 | true |
