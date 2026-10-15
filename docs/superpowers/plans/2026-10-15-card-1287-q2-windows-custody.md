# CARD-1287 Q2 Windows custody repair

Code/landing owner: `7a7b92ba-6819-4460-9776-a07787de775d`.
Task base: `067b47e2dc903247f1a7c8aa539032cc4266e4ad`.
Branch: `feat/card-task-7a7b92ba`.

Reproduce the existing twelve custody tests on Windows before changing source.
Determine whether their virtual filesystem or production cleanup is responsible.
Preserve every assertion and deadline, sibling-tool and unowned-run retention,
and the separation between generic creation custody and sealed tool authority.

The brief's narrow narrative excludes Unit, but its appended Final profile and
the Code stage contract require the whole Unit lane. CP-4 records that required
selection explicitly. No full assembly is commissioned. Linux execution is
required follow-up: this delegate cannot use a bound server2 checkpoint.

## Verification design

- V-1: reproduce every original custody case and retain each exact failure.
- V-2: all twelve custody cases pass after a minimal repair; no assertions weakened.
- R-1: full HostCleanup*, OutputCleanup* and CheckpointOwned* classes pass.
- R-2: whole Unit lane, to detect shared fixture and cross-class regressions.
- R-3: same custody class on Linux; unavailable to this Windows delegate and must
  remain unverified until a caller commissions the execution.
- V-3: manual source review confirms native custody/deletion safeguards unchanged
  for a fixture-only repair; report any production defect as an RC blocker.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | Diagnosis | `tests/Antiphon.Tests -> bin-q2-red/` | original-windows-red | `/*/*/HostCleanupCheckpointCustodyTests/*` | V-1 | all 12 original cases; diagnostic red expected | 12 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | Repair | `tests/Antiphon.Tests -> bin-q2-green/` | custody-windows | `/*/*/HostCleanupCheckpointCustodyTests/*` | V-2 | all 12 cases, 0 failed/skipped | 12 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | Repair | CP-2 | cleanup-regression | `/*/*/(HostCleanup*)\|(OutputCleanup*)\|(CheckpointOwned*)/*` | R-1 | all named full classes, 0 failed/skipped | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | Repair | CP-2 | final-unit | `/*/*/*/*[Category=Unit]` | R-2 | whole Unit lane, 0 failed | 1992 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled` |

### Cost

Estimated ordinary Windows cost: 30 minutes including two isolated builds.
Checkpoint CLI bootstrap is a declared additional slot-gated build. Run the
checkpoint tool via the build-slot wrapper, pass committed HEAD as expected SHA,
and wait for completion. Inspect fresh TRX methods/counts and clean receipts.
Any missing-token fallback clears ANTIPHON_TASK_ID only in the child environment.
No source edits while a run is active. No repeat proof after green is required.

All inherited CARD-0987 PC-1..413 and variants remain pending SourceLanding
Mutation. No deliberate mutants are part of this Code task. Mutation also owns
missing-control discovery for the fixture repair. Restart: none if fixture-only.
