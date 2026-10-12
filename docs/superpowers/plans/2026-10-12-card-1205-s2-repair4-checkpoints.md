# CARD-1205 S1/S2 repair 4 — common held-event exit

Base production: 115b4fbef85cb493e06cc0e36dd2d711124c9dfa.
This repair supersedes repair 3's executable manifest only. S3-S5 remain outside
scope. The explicit brief excludes the broad Unit lane despite the generic Final
profile. T01 and every existing assertion/timeout remain unchanged.

## Verification design

T51 is a 48-execution literal-Arguments matrix in
SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome.
Rows: second list success/timeout/throws/never; cancellation none/before-list/
during-list/error-handler/final-pull/callback; responsive/blocked consumer.
Every responsive row expects accepted=1, attempts=1, warnings=0. Every blocked
row expects accepted=0, attempts=1, warnings=1 and disposed iterator before release.
The original output text is asserted. The callback intentionally ignores cancellation;
teardown releases and joins both retained scopes and the outstanding list request.
A fake monotonic clock drives the unchanged three-second bounds; two-second waits
are unchanged outer hang guards, never stimuli or sleeps.

Some Cartesian cells cannot enter the named phase: a successful list has no error
handler, and a failed or pending list has no final transcript pull. These cells
cancel at the final held catch-up boundary instead. This substitution is explicit
in the fixture and does not claim the unreachable phase executed. Success/final-pull
uses the actual runtime pull; throws/error-handler uses the actual caught-list-error
logger before its return (Review F1). Timeout and never/error-handler cancel at the
outer catch handler. Never is a non-cooperative HTTP request released only at teardown;
the pump must independently bound its second-list wait and observe its late fault.
No guarantee of terminating arbitrary consumer/request code is made.

T52 adds two real HTTP iterator read/disposal fault controls after the held offer:
accepted=1, attempts=1, disposed and no abandonment. They protect dispatch-before-
iterator-unwind, not a claim that a fault before parsing carries a held payload.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| RED-1 | RepairTests | `tests/Antiphon.Tests -> bin-c1205-r4-red/` | baseline-matrix | `/*/*/SessionRunnerHeldEventExitMatrixTests*/*` | T51,T52,F1 | All 50 execute; F1 and any other defect rows assertion-red on unchanged base production; 0 skipped | 50 | 7 |
| CP-1 | RepairFix | `tests/Antiphon.Tests -> bin-c1205-r4-green/` | gap-baseline | `/*/Antiphon.Tests.Application/TurnEndGapReproductionTests*/*` | V-1,V-2,R-1 | Exactly 2 green; unchanged T01 | 2 | 6 |
| CP-2 | RepairFix | `CP-1` | subscription | `/*/Antiphon.Tests.Application/(TurnEndGapReproductionTests*)\|(SessionRunnerStreamReadinessTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerHeldEventDeliveryTests*)\|(SessionRunnerHeldEventExitMatrixTests*)/*` | V-1..7,R-1,R-6,T47..T52 | Exactly 84 green, 0 skipped | 84 | 10 |
| ADJ-1 | RepairFix | `CP-1` | adjacent | `/*/*/(SessionRunnerHttpClientHostStatsTests*)\|(SessionRunnerGenerationWireTests*)\|(SessionRunnerSettingsTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | R-7 adjacent compatibility | Exactly 33 green, 0 skipped | 33 | 3 |

### Cost

Test-first baseline seven minutes; ordinary floor nineteen minutes; authoring/runs
budget sixty minutes. One isolated build per committed group, UseAppHost=false,
forward-slash OutputPath. Bootstrap separately disclosed through the slot gate.
Run --after RepairTests then --after RepairFix, --serial, full committed
--expected-source-sha. Wait to final exit and inspect fresh per-case TRX.
No repeat required after green. No required manual acceptance for S1/S2.
V-8..40, R-2..5/R-8/R-9 deferred to S3-S5; R-7 does not discharge CP-3.
Post-land Mutation NOT run; PC-1..171 and every variant pending. Restart: none.

