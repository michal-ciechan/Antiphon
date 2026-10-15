# CARD-1230 — C1017_TerminalTaskRequired: the reset cleanup is not clean

**Date:** 2026-10-15 · **Stage:** Code (tests first) · **Branch:** `feat/card-task-5a3d03f6`
**Task base:** `78e4001788e99ebc04f0590a84db18442f6324c6`

## Problem

`CardDoneWorktreeCleanupTests.C1017_TerminalTaskRequired` failed at its line-105 assertion
`(await f.CleanupAsync()).IsClean.ShouldBeTrue()` after the task is set back to Succeeded.
The assertion did not print the residue. Recorded reds:

| Run | Checkout | Selection | Result |
|---|---|---|---|
| 20261013-194516-9bed (CP-4, `b43e21ec`) | `/work/worktrees/task-560fbb63` | three retirement classes | 114 executed, 1 failed (this method) |
| 20261013-202658-1887 (CP-B1, `b5653d58`) | `/tmp/antiphon-review-560fbb63/baseline` | this method alone | 1 executed, 1 failed |

## Measured cause

1. `Residue.ShouldBeNull` is the same check as `IsClean` (`WorktreeRemoval.IsClean => Residue is null`,
   `server/Application/Dtos/WorktreeRemoval.cs`) and prints the residue. At `35aaeed6`, from
   `/work/worktrees/task-5a3d03f6`, the method alone passed (1/1, `build-slot.ps1` label
   `c1230-diag-run`).
2. `AgentReportStore.IsPersistent` refuses a report root under `Path.GetTempPath()`
   (`server/Infrastructure/Files/AgentReportStore.cs`; documented in `docs/orchestration-loop.md`,
   report storage: "temporary directories and worktrees are refused"). `CompletedCardCleanupFixture`
   keeps its repository, so its report root, under `<checkout>/.antiphon/test-output/c1017/`. From a
   checkout under the temp directory `StoreAsync` has no durable root, `CardDoneArtifactPreservation`
   returns `artifact_unpreserved`, and the admitted cleanup is not clean. The two refusals before it
   (`terminal_required`) return before report storage, so the failure is at the reset cleanup.
3. Reproduced at `35aaeed6` with the same build and `TMPDIR` set to the fixture parent
   (`TMPDIR=<checkout>/.antiphon/test-output/c1017/`, label `c1230-tmp-repro`): failed at line 105,
   `should be null but was "artifact_unpreserved"`, additional info `Queued`.

The product rule is intended, so the fixture is the part to change. The CP-4 red from
`/work/worktrees/task-560fbb63` is not explained by this: its checkout is outside `/tmp`
(`echo $TMPDIR` prints `/tmp` on this runner) and its store reason was not recorded. `StoreAsync` runs under a
fixed 2-second budget (`budget.CancelAfter(TimeSpan.FromSeconds(2))`) that covers several Git
subprocesses; an expiry also ends `artifact_unpreserved` (`io-budget-expired`). That is a
candidate, not a measured cause. The line-105 message now carries the store's warning, so a
recurrence names its reason.

## Design

- S1 (`35aaeed6`): line 105 asserts `Residue.ShouldBeNull` with the loop state.
- S1 (`b509cb8c`, red): `C1230_TempCheckoutIsRefusedByName` and a deliberately unimplemented
  `CompletedCardCleanupFixture.TempCheckoutRefusal` returning null.
- S2 (`61d6609b`): `TempCheckoutRefusal(root, temp)` returns `c1017_checkout_under_temp` when the
  fixture root equals or sits below the temp root (path-separator bounded, ordinal off Windows).
  The fixture constructor throws `InvalidOperationException` with that reason before it creates a
  repository or schema. `ReportStoreLog` captures `AgentReportStore` warnings through the
  harness logging (`ILoggerProvider`); line 105 appends them to the assertion message.
- No product file, timeout or assertion expectation changes; no skip.

## Verification design

| ID | Test | Guards |
|---|---|---|
| V-1 | `CardDoneWorktreeCleanupTests.C1230_TempCheckoutIsRefusedByName` | Literal `c1017_checkout_under_temp` for a root under `/tmp/` and `/tmp`; null for `/work/worktrees/...` and the sibling prefix `/tmpx/...`. Red at `b509cb8c` (line 114, first literal; label `c1230-red-run`). |
| V-2 | `CardDoneWorktreeCleanupTests.C1017_TerminalTaskRequired` | Each of the four states refuses `terminal_required`, then the Succeeded reset cleanup has a null residue and removes the tree. |
| R-1 | CARD-1213 CP-4 selection, whole: `CardDoneWorktreeCleanupTests`, `TaskWorktreeRetirementTests`, `WorktreeRetirementRaceTests` | The method runs with its class neighbours; the exclusion is removed from the CARD-1213 plan. |
| R-2 | Other `CompletedCardCleanupFixture` consumers: `CardWorktreeCleanupRecoveryTests`, `CardWorktreeMirrorCleanupTests`, `CompletedCardWorktreeRemovalTests` | The constructor refusal and the logger provider leave them unchanged from a non-temp checkout. |
| M-1 | Manual, `build-slot.ps1` label `c1230-tmp-guard`, at `61d6609b` | With `TMPDIR` at the fixture parent, V-2 fails at fixture creation with `c1017_checkout_under_temp: <root> is under <temp>` instead of line 105; V-1 passes. |

### Positive controls (pending for SourceLanding Mutation)

| PC | Mutation | Expected red |
|---|---|---|
| PC-1 | `TempCheckoutRefusal` returns null | V-1, first literal |
| PC-2 | Drop the `+ Path.DirectorySeparatorChar` bound (plain prefix match) | V-1, the `/tmpx/...` literal |
| PC-3 | Drop `Path.TrimEndingDirectorySeparator` on the temp root | V-1, first literal (`/tmp/` becomes the prefix `/tmp//`) |
| PC-4 | Remove the constructor call to `TempCheckoutRefusal` | No in-process V covers it (needs `TMPDIR`); M-1 is the evidence. Disclosed gap. |
| PC-5 | `ReportStoreLog` filters the wrong category | No V covers it (diagnostic message only). Disclosed gap. |

### Checkpoints

One run through the checkpoint tool (`dotnet run --project tools/Antiphon.Checkpoints -- run`,
manifest `rowMinutes: 40`, `--total-timeout 150m`, expected source SHA = committed HEAD). The tool
bootstrap build is the one unlisted, slot-gated build. The diagnostic and red/green runs above went
through `scripts/build-slot.ps1` with the labels named.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1230-cp/` | target | `/*/*/CardDoneWorktreeCleanupTests/(C1230_TempCheckoutIsRefusedByName*)\|(C1017_TerminalTaskRequired*)` | V-1, V-2 | 2 executed; 0 failed; 0 skipped | 2 | 3 | false |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1230-cp/` | retirement | `/*/*/(CardDoneWorktreeCleanupTests*)\|(TaskWorktreeRetirementTests*)\|(WorktreeRetirementRaceTests*)/*` | R-1 | 0 failed; 0 skipped; run 20261013-194516-9bed executed 114 at `b43e21ec`, plus V-1 | 115 | 12 | false |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1230-cp/` | fixture-consumers | `/*/*/(CardWorktreeCleanupRecoveryTests*)\|(CardWorktreeMirrorCleanupTests*)\|(CompletedCardWorktreeRemovalTests*)/*` | R-2 | 0 failed; 0 skipped; `[Test]` census in source 6 + 21 + 6 + 36 | 69 | 12 | false |
