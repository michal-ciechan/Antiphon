# CARD-1199 repair 12c Code report

Outcome: COMPLETE. The inherited detect-and-report-only redesign and fixture repairs passed all nine closed Final checkpoints in one uninterrupted run: 165 executed, 165 passed, zero failed or skipped. No additional production/test repair was necessary. Next stage: Review.

Landing owner/original Code task: 1baae4ee-3a02-495f-925b-345b0630c01c, superseding predecessor Code 6c9fe082-cfe7-48bc-9d40-a9a58918da4f under the repair brief. Branch feat/card-task-1baae4ee; worktree /work/worktrees/task-1baae4ee. Assigned base and actual tested source: 8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3. Plan redesign base: 6e8b9cc231ea90981888e5ccdab4d519b4451ad2. Full candidate base: cdc2cd3112bb146787248c28a3e7b63bd3b13beb.

Deliverables: docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md updates landing ownership and records this result; .antiphon/task-1baae4ee.md is this report. These are documentation-only changes after the run. Production, scripts and test sources are unchanged from the tested SHA. The final pushed report/ownership commit is identified in the stored completion report; these receipts are not relabeled as that later commit. Review must qualify its exact candidate SHA.

## Ordinary Final evidence

The brief and plan explicitly override the generic whole-Unit profile: CP-1..CP-9 are the closed scope, no Unit lane and no full assembly run. Every named full class and ordinary V/R obligation was covered. No unbounded class selection. Estimated ordinary floor 31 minutes; actual run 25m30s. Source was frozen for the entire run; no stop, restart, repair, retry, known-flaky rerun or extra ordinary selection occurred. --serial ensured CP-1, CP-7 and CP-8 executed in order.

Run: .antiphon/checkpoints/20261013-010217-465b/report.md and report.json. Every fresh rows/CP-n/run.trx was inspected; all intended classes/methods have nonzero results. Detailed roster inspection: .antiphon/c1199r12c/trx-final.txt. All nine rows report slot=granted, dirty=0, sourceState=clean and buildSource=verified. The source snapshot's start/end SHA and fingerprint match; fingerprint 8fdcca9104493c97ffcc5b1c7768d27e7727bffe333d51e90790684bcfe0444d. scripts/validate-checkpoint-receipt.ps1 returned exit 0: CHECKPOINT SOURCE VALID source=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 rows=9.

| CP | Executed | Passed | Failed | Skipped | Slot | Waited |
|---|---:|---:|---:|---:|---|---|
| CP-1 | 125 | 125 | 0 | 0 | granted | 0s |
| CP-2 | 1 | 1 | 0 | 0 | granted | 30s |
| CP-3 | 1 | 1 | 0 | 0 | granted | 15s |
| CP-4 | 1 | 1 | 0 | 0 | granted | 30s |
| CP-5 | 17 | 17 | 0 | 0 | granted | 15s |
| CP-6 | 5 | 5 | 0 | 0 | granted | 0s |
| CP-7 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-8 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-9 | 13 | 13 | 0 | 0 | granted | 0s |

CP-1 class counts: BaselineComparerTests 7; CheckpointAppTests 3; CheckpointManifestTests 7; CheckpointNamespaceCensusUsageTests 1; CheckpointOutputConfinementTests 5; CheckpointTaskOwnershipTests 24; CheckpointToolCopyCleanupTests 16; CheckpointTrackedBaselineTests 7; OutputCleanupTests 29; RunSchedulerTests 15; RunStateStoreTests 1; WaitCommandTests 10. CP-5 covers all 16 shape variants plus missing_receipt_is_not_negative_success. CP-9 covers the full 13 RowRunnerTests cases, clearing the predecessor's nine reds.

## Invariant outcomes

- V-1 PASSED: CP-2/3/4 and all CP-5 variants retain build-result bytes, deletions, links and index, list tampered paths and manual commands, refuse tampering and retain clean-build behavior through both entry points.
- V-2 PASSED: CP-5 existing stamp, compiler file/directory and linked output collisions refuse before the build with user bytes retained; confinement cases pass in CP-1.
- V-3 PASSED: CP-5 checks actual bound invalid receipts and refuses missing receipts; the complete RowRunnerTests class passes in CP-9.
- V-4 PASSED: all five CP-6 ledger cleanup cases pass, including extra/changed files, directories and links.
- R-1 PASSED: every named full class, tracked snapshot membership and compiled namespace census passed. The independent compiled-case census reconfirmed the literal 488; no census change was needed.
- R-2 DISCLOSED/PENDING NATIVE QUALIFICATION: Linux only. Windows junction behavior was not executed or marked passed.
- R-3 PASSED: three consecutive ordinary executions of hanging_git_is_refused_and_killed_within_the_bound: CP-1 15.2342678s; CP-7 15.8425863s; CP-8 15.3813825s, each Passed in its fresh native TRX. No deadline or assertion change. No further repetitions after green.

## Historical red and repair disposition

The predecessor's tests-first commit 223b1325e058aef21c33f03da22f3dd95886e7bf changed tests only; git diff from 6e8b9cc23 confirms unchanged production tooling/scripts. Its retained CP-RED TRX was independently read, not rerun: 15 executed, 7 passed, 8 failed, zero skipped. F1 regular-user-move fails bytes-changed on both entry points; F2 existing-stamp and compiler-collision fail pre-existing-user-output-lost on both; F3 missing_receipt_is_not_negative_success fails its outcome assertion. They all pass in this task's CP-5. Historical inspection: .antiphon/c1199r12c/historical-red-inspection.txt; original TRX /work/worktrees/task-6c9fe082/.antiphon/checkpoints/20261012-234819-d7cc/rows/CP-RED/run.trx. The previous full report .antiphon/task-6c9fe082.md describes the original red/green redesign and obsolete recovery assertions removed there.

This round removed no assertions or tests, added no retries, and changed no deadlines. The previously unrerun fixture fixes are now verified: mocked project parents, ownership/output ledgers, fresh confinement output names and prior bin-* exclusion. Neither deliberate mutants nor positive-control cycles were run by this Code task.

## Build, runner and execution provenance

One isolated Antiphon.Tests build, build id bin-c1199r12 (not tests), OutputPath bin-c1199r12/ with forward slash/trailing slash, UseAppHost=false, --keep-outputs. Build slot eb3cd407-d9a5-4c43-afd6-eb0f3825ae71, slot=granted waited=0s; compiler build passed, 0 errors, 691 warnings. The real build used absolute confined OutputPath and ArtifactsPath as the tool requires. Fixture mini-project builds are part of the selected tests, not additional ordinary selections.

The only unlisted build was the necessary small tool bootstrap through scripts/build-slot.ps1: tools/Antiphon.Checkpoints, OutputPath bin-c1199r12c-tool/, UseAppHost=false; passed, zero errors and one existing CS8602 warning in TaskOwnerGuard.cs. Bootstrap slot 744a8545-5338-4b49-b6c1-ad773816f5a2, granted waited=0s, held=6s. Log .antiphon/c1199r12c/bootstrap.log. No build/test driver ran outside the checkpoint tool or slot gate.

The run and every foreground wait were awaited until final exit 0 (earlier bounded waits returned 75). Executor owner reads logged two transient HTTP 502 responses, recovered without changing scope or stopping the run; no owner refusal remained. No owned test/build process remains. No server/runner restart or deployment. Restart: none.

GET /api/runner-defaults and /api/session-runners succeeded: defaults revision 2, empty kindDefaults, runner catalogue observed. No Runner/Platform pin was supplied; no fleet location was embedded in source.

## Unedited checkpoint lines

```text
CHECKPOINT CP-1 commit=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 build=ok filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*|BaselineComparerTests*|RunSchedulerTests*|CheckpointManifestTests*|CheckpointTaskOwnershipTests*|CheckpointToolCopyCleanupTests*|WaitCommandTests*|RunStateStoreTests*|CheckpointAppTests*|CheckpointNamespaceCensusUsageTests*|CheckpointOutputConfinementTests*|CheckpointTrackedBaselineTests*)/* executed=125 passed=125 failed=0 skipped=0 trx=/work/worktrees/task-1baae4ee/.antiphon/checkpoints/20261013-010217-465b/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_untracked_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1baae4ee/.antiphon/checkpoints/20261013-010217-465b/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_ignored_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1baae4ee/.antiphon/checkpoints/20261013-010217-465b/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 build=reused filter=/*/*/CheckpointTrackedBaselineTests/index_added_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1baae4ee/.antiphon/checkpoints/20261013-010217-465b/rows/CP-4/run.trx slot=granted waited=30s dirty=0 source=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 build=reused filter=/*/*/CheckpointShapeConflictTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-1baae4ee/.antiphon/checkpoints/20261013-010217-465b/rows/CP-5/run.trx slot=granted waited=15s dirty=0 source=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 build=reused filter=/*/*/CheckpointOwnedOutputTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-1baae4ee/.antiphon/checkpoints/20261013-010217-465b/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 build=reused filter=/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1baae4ee/.antiphon/checkpoints/20261013-010217-465b/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 build=reused filter=/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1baae4ee/.antiphon/checkpoints/20261013-010217-465b/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 build=reused filter=/*/*/RowRunnerTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-1baae4ee/.antiphon/checkpoints/20261013-010217-465b/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 sourceState=clean buildSource=verified
```

## Mutation and Review handoff

Post-land SourceLanding Mutation was NOT run. The plan enumerates zero PC-n rows; every PC/variant and missing-control discovery remain pending method-scoped SourceLanding Mutation. No ordinary green or nightly result discharges those controls. Candidate documentation root docs/superpowers/mutations/ remains unbound. After ordinary Review, the caller lands the original Code task 1baae4ee-3a02-495f-925b-345b0630c01c and commissions SourceLanding Mutation. R-2 native Windows/junction qualification remains pending; no ordinary scope is silently deferred to a later Final round.

Rerun from the candidate checkout after this task's outputs are reclaimed:

```powershell
$candidateSha = (git rev-parse HEAD).Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1199-review-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1199-review-tool/ --property:UseAppHost=false --nologo
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1199-review-tool/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md --expected-source-sha $candidateSha --keep-outputs --serial --max-wait 50s
# If exit 75, keep calling wait with the explicit run id until completion.
```

## Cleanup and publication

Both exact task-owned alternate output directories were removed. The run's createdOutputPaths listed only tests/Antiphon.Tests/bin-c1199r12. After confirmed executor exit, two independent exact ledger inventories/hash checks verified 107,914 entries and 39,265,566,008 payload bytes (excluding the ledger file itself), with no symlinks/reparse points, changed/unlisted entries or tracked files. A fresh git ls-files check immediately preceded deletion. Cleanup exit 0; output absent. Logs and record: .antiphon/c1199r12c/cleanup.log and cleanup.json. The original build binding was retained ignored at .antiphon/c1199r12c/build-binding-before-cleanup.json before deletion.

The fresh bootstrap directory tools/Antiphon.Checkpoints/bin-c1199r12c-tool was absent before bootstrap and belonged to this task. Two matching inventories checked 48 entries and 19,595,595 bytes, with no linked ancestors/entries or tracked files, before removal. Inventory/log .antiphon/c1199r12c/bootstrap-output-inventory.json and bootstrap-cleanup.log. The run's tool shadow copy was already removed by the completed wait. No alternate output directory created by this task remains. Normal bin/obj and unrelated outputs were not reclaimed. Generated TRX, JSON, logs, receipts and checkpoint payloads remain ignored; only this individual Markdown report and the plan amendment are committed.

Ownership/result documentation slice 488d9d4d9140177e6aec274e54c9804ee6f61acd was committed and pushed independently. The later full-report commit and final remote SHA are named in the stored completion report. No rebase, amend, reset or force-push. The exact tested source remains 8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3; git diff against it for tools/Antiphon.Checkpoints, scripts and tests is empty. Documentation was added after ordinary verification and is not claimed as a separately tested SHA.

Merge-tree against freshly fetched origin/master e19186dc8e156216a2875c8c87237b94b7a857f8 returned exit 0 after the ownership slice. Final merge-tree, scripts/check-evidence-diff.ps1 over full task base..HEAD, git ls-files count, worktree status and git ls-remote publication checks are performed after this report commit and their actual outcomes are in the stored completion report.
