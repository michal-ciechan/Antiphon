# CARD-1066 Code report

Implementation is pushed and all 12 targeted cases pass. Final verification is incomplete: the full script class timed out at the run's 15-minute total cap, and the Unit lane never started. This task is not ready for Review.

Original Code task / landing owner: d73d91aa-099e-44ae-a5db-5e3c4ce3d84d.
Branch: feat/card-task-d73d91aa.
Worktree: /work/worktrees/task-d73d91aa.
Task base: 0de12dac930ad52a235604c566ee643e71b21daa.
Test-only slice: aadc1195d5ce4bc5843b2785e97edfc18037ae63.
Implementation and actual tested SHA: b69f74ef3dba09e1bac07d8e5a151b9ae79f551b.
The later report-only commit does not change that tested SHA.

Plan: docs/superpowers/plans/2026-10-05-card-1066-cache-fixture-nounset-plan.md.
Changed implementation: scripts/c590-remote.sh.
Changed tests: tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs.

## Change and proof

Split every variable in the three affected declarations into its own local statement. A full-file audit found exactly three dependencies among 208 original local statements: tree reads fault, npm output reads project, warm output reads kind. The repaired 219 statements contain none of those same-statement dependencies.

The tests extract the actual functions from the script, execute under set -euo pipefail with the dependent names unset, and assert observed results. Tree tests use real temporary filesystem mutations and the real validator; npm/warm tests replace only Docker with deterministic replies and check the actual argument-derived log file.

Baseline CP-1 ran the new tests with the production script unchanged. Fresh TRX showed 11 assertion failures and the existing donor test passing. Captured stderr named fault, project, or kind as unbound, with exit 1 instead of the intended result. Fixed CP-1 ran all 12 cases successfully; its fresh TRX contains every intended method and argument variant. No deliberate Mutation cycle was run.

## Ordinary IDs and checkpoint outcomes

| ID | Actual outcome |
|---|---|
| V-1 | 6/6 tree variants passed after 6/6 failed on the old script. Exact exit 2 and diagnosis; donor metadata retained. |
| V-2 | 2/2 npm variants passed after 2/2 failed on the old script. Exact status/diagnosis and log contents. |
| V-3 | 3/3 warm variants passed after 3/3 failed on the old script. Exact status/diagnosis and log contents. |
| V-4 | Pending trusted desktop, reviewed/landed-SHA Fixture acceptance. Not run from this mirror. |
| R-1 | Existing narrow donor test passed in both CP-1 runs. Full RemoteScriptContractTests is incomplete: CP-2 ran about 13m24s before total timeout, with no final TRX. |
| R-2 | Not run: CP-3 Unit was still queued at total timeout. |
| R-3 | Passed: CP-4 bash syntax exit 0 and whole-file declaration audit. |

The brief's narrow/no-Unit instruction conflicts with the stage's mandatory Final profile. The manifest records the required Final class/Unit rows. No whole assembly was run. The estimates were insufficient for the full class; do not mark that row or Unit passed.

The full-class console observed Nested_lane_never_uses_sudo_or_python failing on the existing c1008 line:
canonical="$(sudo -n readlink -e -- "$source" 2>/dev/null)" || c1008_refuse RecycleContainerStateUnknown

A separate detached checkout at the exact task base reran only that method through the checkpoint tool. Its fresh TRX executed one test and reproduced the same failure and offending line. This failure is inherited, not repaired or weakened here. Baseline worktree/evidence remains at /work/worktrees/task-d73d91aa-baseline/.antiphon/checkpoints/CP-BASE-20261005-111427-4f86/.

## Source qualification limitation

Every actual test/build used the committed full expected SHA. CP-1's line and row receipt show dirty=0, sourceState=clean, buildSource=verified, matching the inspected fresh TRX. However, validating the final aggregate report for CP-1/CP-4 returned exit 2, row_heading_disagreement: the aggregate buildSource is unknown after CP-2 timeout while CP-1's row says verified. The receipt is retained unchanged. This is not a clean final certificate; obtain fresh ordinary evidence in the continuation. No assertion or timeout was loosened.

## Unedited checkpoint lines

```text
CHECKPOINT CP-BASE commit=0de12dac930ad52a235604c566ee643e71b21daa build=ok filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-d73d91aa-baseline/.antiphon/checkpoints/CP-BASE-20261005-111427-4f86/run.trx slot=granted waited=0s dirty=0 source=0de12dac930ad52a235604c566ee643e71b21daa sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=b69f74ef3dba09e1bac07d8e5a151b9ae79f551b build=n/a filter=bash -n scripts/c590-remote.sh executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=b69f74ef3dba09e1bac07d8e5a151b9ae79f551b sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=b69f74ef3dba09e1bac07d8e5a151b9ae79f551b build=ok filter=/*/*/RemoteScriptContractTests/(C1066*)|(C849_Seed_refuses_invalid_donors_and_partial_payloads) executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-d73d91aa/.antiphon/checkpoints/20261005-111019-dc4e/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=b69f74ef3dba09e1bac07d8e5a151b9ae79f551b sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=b69f74ef3dba09e1bac07d8e5a151b9ae79f551b build=n/a filter=/*/*/RemoteScriptContractTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=total slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-3 commit=b69f74ef3dba09e1bac07d8e5a151b9ae79f551b build=n/a filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-1 commit=aadc1195d5ce4bc5843b2785e97edfc18037ae63 build=ok filter=/*/*/RemoteScriptContractTests/(C1066*)|(C849_Seed_refuses_invalid_donors_and_partial_payloads) executed=12 passed=1 failed=11 skipped=0 trx=/work/worktrees/task-d73d91aa/.antiphon/checkpoints/20261005-110702-8439/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=aadc1195d5ce4bc5843b2785e97edfc18037ae63 sourceState=clean buildSource=verified
```

The terminal CP-2 timeout line says slot=skipped; the executor log records its actual granted lease 4ac62c4e-5b83-4640-af7e-34f0dce260f9 at 11:12:06 UTC, waited=0s, released at 11:25:30 UTC. This discrepancy is preserved, not edited away. CP-3 genuinely never acquired a slot or ran. All executed build/test drivers acquired slots with waited=0s; no bypass was used.

Evidence:
- .antiphon/checkpoints/20261005-110702-8439/report.md and rows/CP-1/run.trx (baseline regression proof).
- .antiphon/checkpoints/20261005-111019-dc4e/report.md, report.json, executor.log, rows/CP-1/run.trx, rows/CP-2/console.log (fixed run).
- .antiphon/c1066-base-check.log and detached-base TRX above (inherited guard proof).

Explained unlisted build/test work: one checkpoint-tool bootstrap build under scripts/build-slot.ps1, and CP-BASE's isolated build plus one exact method to verify inherited red. An initial launcher refused an abbreviated SHA before any driver; the retry supplied the full SHA. No repeat proof after green, no test repairs, and no deliberate mutants.

The finished checkpoint run cleaned its shadow executor; its exact bin-c1066 outputs were removed through checkpoint clean. Baseline bin-c1066-base and bootstrap bin-c1066-tool outputs were removed separately from their producer-owned locations. Generated TRX/JSON/logs remain ignored. Both GET /api/runner-defaults and GET /api/session-runners were read; no runner/platform pin was added.

## Pending and handoff

Continue Code to complete R-1/full CP-2 and R-2/CP-3 and obtain valid source-qualified receipts, including CP-1. Use the original Code task as landing owner. Review follows completion of ordinary verification. The full class already exceeded 13 minutes; budget its continuation explicitly.

All task Mutation controls remain pending: PC-1 host/metadata/reference/symlink/hardlink/special; PC-2 npm success/miss; PC-3 packages/scratch/home. Existing operational Fixture PC-01 through PC-32 (47 variants) also remain pending. Mutation owns deliberate method-scoped red/restore/green and missing-control discovery.

After ordinary Review and caller landing, the trusted desktop owner runs scripts/verify-card0849-caches.ps1 -Case Fixture -Sha <reviewed-landed-sha> and commissions SourceLanding Mutation. Expected operational receipt: groups=9 controls=32 expectedRed=32 variants=47 expectedRedVariants=47 inventories=2 failures=0 productionMutations=0. No live deploy, rollout phase, Reset, Prune, or Seed was performed.

Restart: none. Owner: caller/orchestrator for subsequent operational acceptance.

