# CARD-1205 S1/S2 repair 6 — callback exception identity

Base production: 744d68d57614d345d492ffddcb753433f3d0c5f7. Landing owner:
cbce1a3f-5d59-47d2-a8a3-859ca571c16f, including inherited S1 34df9193c.
S3-S5 remain out of scope. Explicit brief excludes Unit. Restart: none.

## Verification design

T54 adds sixteen literal rows: successful/failed catch-up, InvalidOperationException/
independent OperationCanceledException, asynchronous/synchronous/host-cancelled/
deadline-racing callback. The real HTTP iterator, runtime and production readiness
handler are composed; direct handler observation exposes OCE even when the outer
BackgroundService shutdown catch would consume it. Faults assert original object,
stack provenance, one offer, zero acceptance, iterator disposal and zero abandonment
warnings. Failed catch-up retains its original object and logs the callback object.

T55 adds two completed-offer controls. T56 adds two deadline-before-late-fault
controls, with fake monotonic time, one abandonment warning and a joined observer.
T01/T51/T52/T53 assertions and timeouts remain unchanged. No sleeps.

Race rule: a callback already settled at the bounded wait's observation wins over
host/deadline cancellation; unrelated callback OCE remains its own fault. A pending
callback at that observation is abandoned; a later fault is only observed/logged.
An OCE caused by the cancelled delivery token is abandonment. Catch-up exception
always wins over secondary delivery failure. The helper returns a three-way outcome
without synthesizing an exception; the existing deadline-only reconnect signal in
the caller remains required by unchanged T53. No timeout replaces a callback fault.

RED-1 executes all twenty rows against unchanged base production: fault identity or
zero-abandonment assertions fail on all sixteen T54 rows; four controls pass.
All PC-1..182 and variants remain pending for method-scoped SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| RED-1 | RepairTests | `tests/Antiphon.Tests -> bin-c1205-r6-red/` | callback-baseline | `/*/*/SessionRunnerHeldEventDeliveryTests*/(C1205_Callback_fault_preserves_original_identity*)\|(C1205_Completed_callback_has_no_abandonment*)\|(C1205_Deadline_before_late_fault_retains_abandonment*)` | T54..T56,F1 | Exactly 20 execute; 16 fault rows assertion-red, 4 controls green, 0 skipped | 20 | 6 |
| CP-1 | RepairFix | `tests/Antiphon.Tests -> bin-c1205-r6-green/` | gap-baseline | `/*/Antiphon.Tests.Application/TurnEndGapReproductionTests*/*` | V-1,V-2,R-1 | Exactly 2 green; T01 unchanged | 2 | 6 |
| CP-2 | RepairFix | `CP-1` | subscription | `/*/Antiphon.Tests.Application/(TurnEndGapReproductionTests*)\|(SessionRunnerStreamReadinessTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerHeldEventDeliveryTests*)\|(SessionRunnerHeldEventExitMatrixTests*)/*` | V-1..7,R-1,R-6,T47..T56 | Exactly 107 green, 0 skipped | 107 | 10 |
| ADJ-1 | RepairFix | `CP-1` | adjacent | `/*/*/(SessionRunnerHttpClientHostStatsTests*)\|(SessionRunnerGenerationWireTests*)\|(SessionRunnerSettingsTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | R-7 adjacent compatibility | Exactly 33 green, 0 skipped | 33 | 3 |

### Cost

Baseline six minutes; ordinary nineteen; optional slot-gated tool bootstrap four.
One isolated build per committed group; UseAppHost=false, forward slash OutputPath.
Run --after RepairTests then --after RepairFix, --serial, full committed
--expected-source-sha. Wait to final exit; inspect fresh TRX per method/argument.
No repeats required. No manual acceptance for S1/S2. V-8..40 and R-2..5/R-8/R-9
are deferred to S3-S5; R-7 does not discharge CP-3. Post-land Mutation NOT run.

### Executed baseline

Production diff from 744d68d57 to e76a094f897cd39e2dd3a216884d5f3174e3c429 is empty.
Fresh TRX: twenty executions; sixteen intended assertion failures, four green controls.
Successful catch-up fault rows lose identity (TimeoutException or host OCE); failed
catch-up rows report one false abandonment warning instead of zero. No fixture/build
errors, skips or timeouts. Ordinary fixed-source checkpoints pending at this commit.

CHECKPOINT RED-1 commit=e76a094f897cd39e2dd3a216884d5f3174e3c429 build=ok filter=/*/*/SessionRunnerHeldEventDeliveryTests*/(C1205_Callback_fault_preserves_original_identity*)|(C1205_Completed_callback_has_no_abandonment*)|(C1205_Deadline_before_late_fault_retains_abandonment*) executed=20 passed=4 failed=16 skipped=0 trx=/work/worktrees/task-cbce1a3f/.antiphon/checkpoints/20261013-001445-ddb7/rows/RED-1/run.trx slot=granted waited=75s dirty=0 source=e76a094f897cd39e2dd3a216884d5f3174e3c429 sourceState=clean buildSource=verified
