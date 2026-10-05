# CARD-0667 S3h continuation verification

Code task `d5234cdc-4d41-46a4-ac2f-e6267d0e4253` continues original Code/landing owner
`bfc79905-468d-4c41-91f7-88871f55e568` from `3610e45ffdd2e90be8c617c932555e5d876ec2e8`.
The implementation plan remains
`docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`.
This companion retains its exact CP-21 selection and adds the commissioned Final
profile's whole Unit lane and both full affected integration classes. Those extra
rows are explicitly required by this continuation's brief despite the original
slice manifest's narrower budget. No full assembly or namespace run is selected.

The fixture must read real inventory through the production phone-home client and
dispatcher before marking that exact connection recovered. It must do this on
initial creation, server transport recreation, and runner recreation. The current
seat identity and accepted generation must match; production recovery guards,
timeouts, release assertions and `AutomaticEnabled=false` remain unchanged.

V-3 S3h/G-100-102 is CP-21. Full affected classes are
`RunnerSeatOrphanSweepTests` (8 current results) and
`TerminalRunnerSeatReleaseTests` (21 current results), because both use the changed
fixture. CP-23 exercises their current V-2/V-3 lifecycle, reservation, persistence,
reply and restart witnesses. Future S3e/S4a/S4b methods and S4c cross-platform
CP-1-6 remain separate commissioned work and receive no pass claim here. No live
activation is part of this dormant fixture repair. PC-100-102, all their variants,
and all other original-plan PCs stay pending for post-land SourceLanding Mutation.

The predecessor's compiling assertion-red CP-21 is recorded in
`.antiphon/task-bfc79905.md`; this continuation introduces no new test methods or
deliberate mutants. At most two failure-driven repair rounds and 30 minutes total.

### Checkpoints

All rows run serially after the committed fixture fix. CP-22/23 reuse CP-21's
source-certified isolated output; every driver acquires a build slot. Inspect
fresh TRX counts and method names. Unit's minimum is only a nonempty-selection
guard, not a prediction of the evolving repository-wide Unit roster.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-21 | S3h | `tests/Antiphon.Tests -> bin-c667-s3h/` | linux-s3h-postgres | `/*/*/RunnerSeatOrphanSweepTests*/(Discovery_request_uses_runner_owned_delivery_evidence*)\|(Server_restart_reacquires_runner_delivery_evidence*)\|(Evidence_missing_or_peer_unsupported_defers_discovery*)` | V-3 | all 3 listed results; named assertion red then 0 failed/skipped green | 3 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c667-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-22 | S3h | CP-21 (-NoBuild) | final-unit | `/*/*/*/*[Category=Unit]` | Final Unit | whole Unit lane; report every outcome | 1 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c667-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-23 | S3h | CP-21 (-NoBuild) | final-affected-classes | `/*/*/(RunnerSeatOrphanSweepTests*)\|(TerminalRunnerSeatReleaseTests*)/*` | V-2, V-3 current affected classes | all 29 current results, 0 failed/skipped | 29 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c667-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

16 estimated minutes for the three ordinary rows, plus gated checkpoint-tool
bootstrap, authoring and failure diagnosis within the brief's 30-minute hard cap.
No green repeat is required. Any red row rerun must be reported with its actual
source SHA; unrelated red is only called inherited after a method-scoped base run.
