# D-1 test-only repair

D-1 is fixed: the real two-run chain is green and kills M13 and M9. The inherited missing-digest fail-open is confirmed and remains unresolved under this explicitly test-only commission.

Base: `e805b7848ae1cc1fe00c9d6f7ccb6e21b0ababc3`. Tested source: `a52cdc53712d5a143fe0903cb22046737e4c17b1`. Branch: `feat/card-task-8d4a4d7b`.

## Changes

- `scripts/test-nightly-failed-first.ps1`: CP-6 runs a small first attempt with exactly one failed chunk, exports that attempt's real summary through `Export-NightlyPriorFailures`, and passes the exported history into the public second execution. It asserts the real producer's digest, the failed TRX observation and its resolved member, `previous-run-order`, failed-first/passed-last launch order, exactly one launch of every current chunk, and a complete green second verdict.
- A second consumer swaps class membership while retaining all four chunk keys. It must report `previous-run-plan-mismatch`, execute the canonical order exactly once, and finish green. Retaining keys ensures the roster guard cannot substitute for the digest guard.
- `tests/Antiphon.Tests/Scripts/RcFailedFirstExecutionTests.cs`: names all three new assertions and raises CP-6's harness assertion count from 13 to 16. Existing deadlines are unchanged. No production, policy, project, build-definition or land-graph definition file changed. No plan cost or timeout was widened.

## Verification

Checkpoint run `20261031-094112-3b40`, Linux, TEMP=/tmp, UseAppHost=false. The tool imported four rows and ran CP-5 through CP-8 serially from ONE fresh isolated build, `bin-d1verify1/`. The build took 244.566 seconds and waited 15 seconds for admission. Every row acquired its slot without waiting. Full run wall: 909.230 seconds; charged execution: 894.236 seconds. No run exceeded 30 minutes.

| Row | Executed | Passed | Failed | Skipped | Row wall | Test body |
|---|---:|---:|---:|---:|---:|---:|
| CP-5 CapFitsAdmittedPlannedCost | 1 | 1 | 0 | 0 | 10.474 s | 5.819 s |
| CP-6 ActualLoopFollowsSchedule | 1 | 1 | 0 | 0 | 397.695 s | 393.281 s |
| CP-7 JournalCustodySurvivesInterleaving | 1 | 1 | 0 | 0 | 88.937 s | 82.944 s |
| CP-8 LedgerAndReceiptBytesAreUnchanged | 1 | 1 | 0 | 0 | 73.223 s | 67.556 s |

CP-6 is 16/16 internal assertions; CP-7 remains 27/27. CP-6 stayed below its existing 1,080-second harness bound and 1,140-second TUnit timeout. Its three new chain assertions took 12.550 seconds in this full run (9.749 seconds in the small green probe). These are n=1 observations, not measured p95. Retained performance JSON records assertion durations, fixture/planner/archive phases, host/load, source and platform. Docker/database/AppHost are not used; unclassified time remains explicitly unattributed.

Receipt validation returned:

```text
CHECKPOINT SOURCE VALID source=a52cdc53712d5a143fe0903cb22046737e4c17b1 rows=4
```

The tool's exact CHECKPOINT lines are appended below. Evidence: `/work/worktrees/task-8d4a4d7b/.antiphon/checkpoints/20261031-094112-3b40/report.md` and `report.json`. Generated evidence is ignored, not committed.

## Red/green controls and historical outcomes

All controls used the exact `ActualLoopFollowsSchedule -SmallLoopProbe` harness case, through the build-slot gate. Scratch source was a local archive of this task branch; the assigned checkout's production files were never mutated. These are regression witnesses, not SourceLanding Mutation credit.

| Control | Passed | Failed | Result |
|---|---:|---:|---|
| Final source, small probe | 16 | 0 | green |
| M13 remove coordinator `-PreviousRun $DispatchPreviousRun` | 14 | 2 | red: chainPreviousOrder and chainRegrouped |
| M9 replace plan-digest guard with `if($false)` | 15 | 1 | red: chainRegrouped |
| Restored scratch after both mutants | 16 | 0 | green |
| Remove digest from real summary, re-export, consume | 15 | 1 | red: chainRegrouped; inherited A-2 |
| Same digestless probe with base production libraries | 15 | 1 | same red; inherited confirmed |

Restored `nightly-dispatch.ps1` SHA256: `69C5DFC988AD2F856B9C581EEC79A72C34DECFF1F2E5F2532282BFDF2C65C084`, matching the original. Scratch harness was restored to the committed source after the digestless probe. Logs are retained under `/work/worktrees/task-8d4a4d7b/.antiphon/d1/`; each probe has its own results directory and performance evidence.

Authoring outcomes are preserved: `ad1309bab2683a037f4fab98cd3672f3121ebbfc` small probe was 15 passed/1 failed because the first producer assertion required complete export membership. `a4060ecb69dca0c0ab5b262e8134193014957a40` was 13 passed/3 failed: enabling exclusions caused legitimate repacking and invalidated the intended four-chunk fixture. The final test uses the original small loop path and explicitly verifies that the real failed TRX member resolves. No product change or deadline change made it green. The first checkpoint launch refused an abbreviated expected SHA before any build; the corrected full-SHA launch is the sole checkpoint run.

The additional, explicitly commissioned harness controls and gated tool bootstrap/audit builds are outside the checkpoint tool's own `unlisted: none` statement. There was one checkpoint-tool bootstrap build and one land-audit bootstrap build, each with its own fresh isolated output; no other suite was run.

## Inherited findings and limits

- **A-2 is a real fail-open.** Removing `dispatchProgress.canonicalPlanDigest` from a real run's summary, then re-exporting its evidence, is accepted at ingress. The regrouped consumer selects `previous-run-order` and stale failed-chunk order instead of canonical fallback. The same probe fails with base production libraries. A record without a digest should fall back; this task has not repaired that behavior because the brief explicitly forbids product behavior changes. Keep this finding open for a separate behavior repair; do not describe it as safe or fixed.
- **A-4:** the seam's suite-wide supplemental discovery still produces the previously reviewed `short-trx` unresolved diagnostics. The committed chain nevertheless verifies exactly one failed member with a real failed TRX observation and resolves that member to `antiphon/Priority`. It does not claim complete historical membership.
- **M5/M14:** retained as equivalent/defense-in-depth controls, following Final review. M5 removes a guard behind a partition that is a permutation by construction; M14 removes a second set-equality guard behind schedule construction's guard. No artificial assertion was added merely to kill them. Existing M2/M6 and M10/M11 witnesses cover the actual bad permutations.
- Native Windows CP-15/16 remain OWED. This report is the commissioned Linux CP-5..CP-8 verification, not full qualification or a fresh Final Review. The master port needs its own review.

## Land graph, branch and cleanup

The real gated `scripts/audit-land-verification-graph.ps1 -Check` ran both evaluation passes and approved `digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69`, with the same five known not-built projects. No `-Update` ran; the audit removed its canary.

`scripts/check-evidence-diff.ps1` over base..tested-source: commits=3, entries=0, violations=0.

After fetching origin, merge-tree onto rc `53835c8c2033618a76e6cb512f386e31c11e4ed7` was clean (tested-source tree `585887c4f0b6bce0aee4feb2e2f220b2cc1d9366`). Master `96715b56623d57a16c6788838bd18281a40e768d` still has 48 conflicts. No merge, rebase, tag, publish, desktop access, activation or live qualification operation was performed.

The checkpoint tool deleted `tests/Antiphon.Tests/bin-d1verify1/`. After the last tool use, these exact owned paths were removed individually: `src/Antiphon.HostCleanup/bin-d1tool1`, `tools/Antiphon.Checkpoints/bin-d1tool1`, `tools/Antiphon.LandGraphAudit.Core/bin-d1audit1`, and `tools/Antiphon.LandGraphAudit/bin-d1audit1`. Nothing outside owned scratch/output was deleted.

Source commits, each pushed immediately:

- `ad1309bab2683a037f4fab98cd3672f3121ebbfc`: committed new chain before verification.
- `a4060ecb69dca0c0ab5b262e8134193014957a40`: failed fixture adjustment; retained history.
- `a52cdc53712d5a143fe0903cb22046737e4c17b1`: final green test-only implementation.

To rerun, bootstrap the checkpoint tool through `scripts/build-slot.ps1` with a fresh alternate OutputPath and UseAppHost=false. Import/run the table from `.antiphon/d1/plan.md`, substituting a fresh bin name; CP-5 owns the single `tests/Antiphon.Tests` build, CP-6/7/8 reuse it. Exact selections are preserved in the CHECKPOINT lines below. Use TEMP=/tmp, wait until exit is not 75, then validate report.json against the full tested source SHA.

## Exact checkpoint provenance

```text
CHECKPOINT CP-5 commit=a52cdc53712d5a143fe0903cb22046737e4c17b1 build=ok filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogTests/CapFitsAdmittedPlannedCost executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8d4a4d7b/.antiphon/checkpoints/20261031-094112-3b40/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=a52cdc53712d5a143fe0903cb22046737e4c17b1 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=a52cdc53712d5a143fe0903cb22046737e4c17b1 build=reused filter=/*/Antiphon.Tests.Scripts/RcFailedFirstExecutionTests/ActualLoopFollowsSchedule executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8d4a4d7b/.antiphon/checkpoints/20261031-094112-3b40/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=a52cdc53712d5a143fe0903cb22046737e4c17b1 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=a52cdc53712d5a143fe0903cb22046737e4c17b1 build=reused filter=/*/Antiphon.Tests.Scripts/RcFailedFirstExecutionTests/JournalCustodySurvivesInterleaving executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8d4a4d7b/.antiphon/checkpoints/20261031-094112-3b40/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=a52cdc53712d5a143fe0903cb22046737e4c17b1 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=a52cdc53712d5a143fe0903cb22046737e4c17b1 build=reused filter=/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/LedgerAndReceiptBytesAreUnchanged executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8d4a4d7b/.antiphon/checkpoints/20261031-094112-3b40/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=a52cdc53712d5a143fe0903cb22046737e4c17b1 sourceState=clean buildSource=verified
```

--- next stage ---
next: merge to rc
handoff: D-1 test-only repair verified; preserve inherited A-2 for a separate behavior repair. Windows CP-15/16 remain owed. The master port gets its own review.
artifact: /work/worktrees/task-8d4a4d7b/.antiphon/task-8d4a4d7b.md
