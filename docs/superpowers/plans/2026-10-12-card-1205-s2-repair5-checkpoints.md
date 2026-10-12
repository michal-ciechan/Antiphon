# CARD-1205 S1/S2 repair 5 — exception preservation

Base production: 5d451ca4c3315744a8cdde3a1f81f0018a4ba382. Landing owner:
d3cc8360-d502-4525-8f0b-b161e1adc4b4. S3-S5 remain outside scope. Explicit brief
excludes Unit. No assertion or timeout is weakened. Restart: none.

## Verification design

T53 has three literal rows: catch-up throws / callback blocked; catch-up throws /
callback completes; normal catch-up / callback blocked. Actual HTTP client, runtime,
pump and iterator are composed. The outer pump's catch receives the exception;
assert its exact type and message. Blocked rows use three fake monotonic seconds,
assert one warning and iterator disposal before release, then fault the retained
callback and await the observer's logged exception and scope disposal. Responsive
control asserts one offer and original output accepted once. No sleeps.

Baseline RED-1 must fail the throws/blocked row with expected
OperationCanceledException, actual TimeoutException. The other two controls must
pass on base production; requiring them to fail would invert their desired behavior.
T01, all 48 matrix rows and both iterator controls remain unchanged.
PC-172..175 and all PC-1..171/variants remain pending for SourceLanding Mutation.
F2 scratch diagnostic is separately priced: one isolated build and method-scoped
T50 run, changing only the common offer predicate to skip a cancelled host.
This requested diagnostic is not a discharged positive-control cycle.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| RED-1 | RepairTests | `tests/Antiphon.Tests -> bin-c1205-r5-red/` | exception-baseline | `/*/*/SessionRunnerHeldEventDeliveryTests*/C1205_Catchup_exception_survives_bounded_held_delivery*` | T53,F1 | Exactly 3 execute; throws/blocked assertion-red, 2 controls green, 0 skipped | 3 | 6 |
| CP-1 | RepairFix | `tests/Antiphon.Tests -> bin-c1205-r5-green/` | gap-baseline | `/*/Antiphon.Tests.Application/TurnEndGapReproductionTests*/*` | V-1,V-2,R-1 | Exactly 2 green; T01 unchanged | 2 | 6 |
| CP-2 | RepairFix | `CP-1` | subscription | `/*/Antiphon.Tests.Application/(TurnEndGapReproductionTests*)\|(SessionRunnerStreamReadinessTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerHeldEventDeliveryTests*)\|(SessionRunnerHeldEventExitMatrixTests*)/*` | V-1..7,R-1,R-6,T47..T53 | Exactly 87 green, 0 skipped | 87 | 10 |
| ADJ-1 | RepairFix | `CP-1` | adjacent | `/*/*/(SessionRunnerHttpClientHostStatsTests*)\|(SessionRunnerGenerationWireTests*)\|(SessionRunnerSettingsTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | R-7 adjacent compatibility | Exactly 33 green, 0 skipped | 33 | 3 |

### Cost

Baseline six minutes, ordinary nineteen, F2 scratch six; authoring and setup within
45-minute brief budget. One isolated build per committed group; UseAppHost=false,
forward slash OutputPath. Slot-gated tool bootstrap separately disclosed if needed.
Run --after RepairTests or RepairFix, --serial, full committed --expected-source-sha.
Wait to final exit, inspect fresh TRX and retain unedited CHECKPOINT lines.
No repeats required. No manual acceptance for S1/S2. V-8..40 and R-2..5/R-8/R-9
deferred to S3-S5; R-7 does not discharge CP-3.
