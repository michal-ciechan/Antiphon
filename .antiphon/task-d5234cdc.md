# CARD-0667 S3h continuation: fixture repaired; Final verification incomplete

The real phone-home inventory recovery fix works: CP-21 passed all three intended
methods. The commissioned Final profile is not complete/green: Unit timed out
without a TRX, and the full affected-class run has one failure. This is not ready
for Review. Next stage is Code for the remaining ordinary verification.

## Identity and changes

- Original Code task / landing owner: `bfc79905-468d-4c41-91f7-88871f55e568`.
- Continuation: `d5234cdc-4d41-46a4-ac2f-e6267d0e4253`.
- Branch: `feat/card-task-d5234cdc`; worktree: `/work/worktrees/task-d5234cdc`.
- Base: `3610e45ffdd2e90be8c617c932555e5d876ec2e8`.
- Actual tested source: `c882207c46ad5a0073e3b97f27121bda4fc4473e`.
- Repair commits: `7d3fcf1612f3b3b815896f963af145e7e5ce4a2c` and
  `c882207c46ad5a0073e3b97f27121bda4fc4473e`, both pushed immediately. The first
  checkpoint began only after the second commit; no failing compile was run for
  the interim DTO member spelling. This report is a later documentation-only
  commit, whose final pushed SHA is in the caller summary, not a relabelled test SHA.
- Implementation plan: `docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`.
  The brief omitted `task-` from that filename; the existing owner plan was used.
- Final continuation manifest: `docs/superpowers/plans/2026-10-05-card-0667-s3h-continuation-verification.md`.

`tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs` is the only changed
C# file. `LiveSeat.StartTransportAsync` now performs a real `ListAsync` through
the production phone-home client/dispatcher, checks the single session and its
accepted generation, verifies the directory still owns that exact connection,
then marks it recovered. This path also runs after both server and runner
transport recreation. The added assertions retain the production admission gate.
`PhoneHomeTestHost` has no recovery pump; production `PhoneHomeRecoveryPump` already
performs inventory catch-up before `MarkRecovered`. No production defect was
demonstrated or production code changed by this continuation.

No timeout or assertion was loosened. `AutomaticEnabled=false` remains the default.
No migration, discovery hookup, native-provider launch, deployment or activation.
The task token was present. Authenticated GET `/api/runner-defaults` (revision 2)
and `/api/session-runners` succeeded, with Linux/Windows catalogue entries. No
runner/platform pin or fleet configuration was written.

## Ordinary verification and provenance

Main run: `.antiphon/checkpoints/20261005-161218-7de3/`; expected/actual source was
the clean committed `c882207c46ad5a0073e3b97f27121bda4fc4473e` throughout. The run
finished with exit 5 after all three rows finished. Its isolated build passed
(615 warnings, zero errors). Every build/test slot was granted with `waited=0s`.
Rows were serial; CP-22/23 reused the same source-certified build.

| Row | Actual outcome |
|---|---|
| CP-21 | 3 executed, 3 passed, 0 failed, 0 skipped. All exact method identities confirmed in fresh TRX. |
| CP-22 | Whole Unit selection reached its existing 15-minute timeout; no TRX, so total/executed/pass/fail/skip counts are unavailable. Console records three failures and platform skips, not a completed lane. |
| CP-23 | 29 executed, 28 passed, 1 failed, 0 skipped. Fresh TRX: `RunnerSeatOrphanSweepTests` 8/8; `TerminalRunnerSeatReleaseTests` 20/21. |

CP-21's three methods, also green in the required full-class row:

- `Discovery_request_uses_runner_owned_delivery_evidence`
- `Server_restart_reacquires_runner_delivery_evidence`
- `Evidence_missing_or_peer_unsupported_defers_discovery`

The earlier valid assertion-red production baseline is retained in
`.antiphon/task-bfc79905.md` (run `20261005-155334-14be`, source
`3087478c04e68fd445e8c64366769f7fb2a58ab4`). The predecessor's final recovery-gate
failure was source `ca9ff7f3dc03653a4eefc2a29c51c03356a6982b`. This continuation
added no test methods or deliberate mutants. CP-21 needed no failure-driven
repair after its first execution here. Its second execution was required full
affected-class coverage, not a discretionary green repetition. No loaded repeats.

Receipt validation for CP-21 alone returned exit 0, `CHECKPOINT SOURCE VALID`.
Validation of CP-21/22/23 returned exit 2, `reason=row_failed`. Source state and
build provenance are clean/verified; there is no eligible green Final receipt.
The Unit timeout is not classified as inherited and was not rerun with a wider
deadline. CP-22 used `TUNIT_MAX_PARALLEL_TESTS=1`; its runtime cause remains unknown.

## Failure classification and additional runs

The Final profile in the brief required Unit and the full affected classes,
despite the original slice plan's narrower CP-21-only selection. The companion
manifest explicitly adds CP-22/23, preserving CP-21's exact filter. No namespace
or full-assembly run was performed; affected integration classes are bounded to
the two consumers of the changed fixture.

Declared setup: gated checkpoint-tool bootstrap to `bin-c667-tool/`,
`UseAppHost=false`, succeeded with one existing nullable warning and zero errors;
`slot=granted waited=0s`, held 6 seconds. No tool rebuild followed.

Additional diagnostic `BASE-UNIT` used only the three failing methods at the
assigned base in detached `/tmp/card0667-base-d5234cdc`, through the checkpoint
tool's self-leasing `row` command and isolated `bin-c667-base/`. It built and
executed 3 results: 0 passed, 3 failed, 0 skipped, with clean source and verified
build provenance. Fresh TRX confirms the same assertions:

- `SpecialistRoleContractTests.no_Check_comparison_survives_outside_the_allowlist`:
  `TerminalRunnerSeatReleasePolicy.cs:34` directly compares `AgentTaskRole.Check`
  outside the guard's allowlist.
- `C1008PlatformContractTests.C1050_Windows_remote_outcomes_are_exact` and
  `C1050_Windows_rolling_outcomes_are_exact`: native Windows required, Linux host.

Those three failures are inherited; no unrelated repair or assertion weakening
was attempted. BASE-UNIT evidence is under
`.antiphon/checkpoints/base-unit/BASE-UNIT-20261005-162715-1c7f/`.

CP-23's failure is
`TerminalRunnerSeatReleaseTests.Unsupported_server_transport_never_falls_back_to_force`:
expected `Unsupported`, received `Released` at line 681. `BASE-CLASS` reproduced
that exact assertion at the assigned base: 1 executed, 0 passed, 1 failed, 0 skipped,
clean source and verified build provenance, `slot=granted waited=0s`. Its fresh TRX
is `.antiphon/checkpoints/base-class/BASE-CLASS-20261005-163347-1e7d/run.trx`.
This fourth failure is therefore inherited too. That additional isolated build
and method-scoped rerun were necessary because the full affected-class row exposed
it after BASE-UNIT finished. Both diagnostic runs were awaited; no broad baseline
run or deliberate mutation was performed. Baseline builds used a separate frozen
checkout while the main run owned its own frozen source; no source changed under
either run.

## V/R, manual acceptance, Mutation and next work

| ID | Outcome in this continuation |
|---|---|
| V-1 | Not selected; full runner/cross-platform qualification belongs to S4c CP-1/5. No pass claim. |
| V-2 | Existing affected class executed 21 results: 20 passed, 1 failed. Remaining future B methods and Windows qualification are not certified. |
| V-3 | S3h subset passed 3/3; all 8 currently implemented C methods passed in CP-23. Future S3e/S4a/S4b C methods and Windows rowless witness are not certified. |
| R-1 | Not run; deferred to S4c CP-1/5. |
| R-2 | Not run; deferred to S4c CP-2. |
| R-3 | Not run; deferred to S4c CP-4. |
| R-4 | Not run; deferred to separately commissioned Windows S4c CP-5/6. |

Future-slice checkpoints CP-16/17/18 and whole-feature Final CP-1 through CP-6
remain pending, never passed by inference. No live manual activation acceptance
was run; the plan reserves it for a separate commission after dormant S4c. The
required read-only runner-defaults/catalogue inspection and dormant-default
source check were completed.

Every PC-1 through PC-104 remains pending for post-land SourceLanding Mutation.
Relevant S3h variants: PC-100 has HTTP/phone-home × Claude/Grok/Codex, old-generation
refusal and Enter retry; PC-101 has both transports, preserved runner across server
restart, lost capture across runner restart, and persisted-token refusal; PC-102
has both transports, release-only/evidence-only capabilities, missing/invalid
capture, malformed/lost response, unavailable/stale/adopting/store-mismatched
peers and valid control. Ordinary green discharges none. Mutation owns deliberate
mutants, method-scoped red/restore/green and missing-control discovery.

Next Code must finish ordinary Final verification: diagnose CP-22's timeout and
handle the reproduced inherited Unit failures in appropriate scope; repair or
otherwise resolve CP-23's transport-refusal failure, then rerun the affected red
selection without loosening assertions or widening timeouts. Preserve this
continuation's small fixture patch for the caller's port after S3f/S3g publication.
Original landing ownership stays with `bfc79905-468d-4c41-91f7-88871f55e568`.

Restart: **none**. Any eventual server activation belongs to the caller's separate
post-publication commission; no runner restart is required by this fixture-only fix.

## Final receipts and cleanup

The main checkpoint executor and row drivers were awaited; its tool shadow was
removed by `wait`. Generated TRX, JSON, logs and receipts stay ignored under
`.antiphon/checkpoints/`. Only this Markdown summary and the companion manifest
are tracked evidence. The full task base..HEAD evidence-diff check passed before
this report and is repeated over the final report commit; final result appears in
the caller summary. The exact CHECKPOINT lines and final cleanup follow.

All 28 main `bin-c667-s3h` output directories were removed through checkpoint
cleanup. The separately created detached baseline checkout and all of its owned
outputs were removed after each completed diagnostic run; generated evidence was
written directly to the assigned worktree's ignored checkpoint root. The setup
`tools/Antiphon.Checkpoints/bin-c667-tool` output was removed after the last command.
No build/test driver or checkpoint executor remains owned by this task.

Rerun the incomplete/red ordinary rows after the next committed repair (bootstrap
the checkpoint tool through `scripts/build-slot.ps1` as described in the owner plan):

```powershell
$c667Sha = git rev-parse HEAD
dotnet run --no-build --no-restore --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-05-card-0667-s3h-continuation-verification.md --rows CP-22,CP-23 --expected-source-sha $c667Sha --max-wait 50s
```

Wait until exit is not 75 and inspect fresh TRX for both full class names and Unit
counts. This command is a future handoff, not evidence already run. CP-21 itself
does not need an unchanged green repeat.

## Unedited CHECKPOINT lines
CHECKPOINT CP-21 commit=c882207c46ad5a0073e3b97f27121bda4fc4473e build=ok filter=/*/*/RunnerSeatOrphanSweepTests*/(Discovery_request_uses_runner_owned_delivery_evidence*)|(Server_restart_reacquires_runner_delivery_evidence*)|(Evidence_missing_or_peer_unsupported_defers_discovery*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d5234cdc/.antiphon/checkpoints/20261005-161218-7de3/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=c882207c46ad5a0073e3b97f27121bda4fc4473e sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=c882207c46ad5a0073e3b97f27121bda4fc4473e build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=15m slot=granted waited=0s dirty=0 source=c882207c46ad5a0073e3b97f27121bda4fc4473e sourceState=clean buildSource=verified
CHECKPOINT CP-23 commit=c882207c46ad5a0073e3b97f27121bda4fc4473e build=reused filter=/*/*/(RunnerSeatOrphanSweepTests*)|(TerminalRunnerSeatReleaseTests*)/* executed=29 passed=28 failed=1 skipped=0 trx=/work/worktrees/task-d5234cdc/.antiphon/checkpoints/20261005-161218-7de3/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=c882207c46ad5a0073e3b97f27121bda4fc4473e sourceState=clean buildSource=verified
CHECKPOINT BASE-UNIT commit=3610e45ffdd2e90be8c617c932555e5d876ec2e8 build=ok filter=/*/*/(SpecialistRoleContractTests*)|(C1008PlatformContractTests*)/(no_Check_comparison_survives_outside_the_allowlist*)|(C1050_Windows_rolling_outcomes_are_exact*)|(C1050_Windows_remote_outcomes_are_exact*) executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-d5234cdc/.antiphon/checkpoints/base-unit/BASE-UNIT-20261005-162715-1c7f/run.trx slot=granted waited=0s dirty=0 source=3610e45ffdd2e90be8c617c932555e5d876ec2e8 sourceState=clean buildSource=verified
CHECKPOINT BASE-CLASS commit=3610e45ffdd2e90be8c617c932555e5d876ec2e8 build=ok filter=/*/*/TerminalRunnerSeatReleaseTests*/Unsupported_server_transport_never_falls_back_to_force* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-d5234cdc/.antiphon/checkpoints/base-class/BASE-CLASS-20261005-163347-1e7d/run.trx slot=granted waited=0s dirty=0 source=3610e45ffdd2e90be8c617c932555e5d876ec2e8 sourceState=clean buildSource=verified
