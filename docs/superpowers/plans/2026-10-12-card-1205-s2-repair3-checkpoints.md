# CARD-1205 S1/S2 repair 3

This bounded repair continues ff56110324863c70fe4f85bfde21c7f28b8a6c7c.
S3-S5 remain outside scope. The explicit repair brief overrides the generic
Final Unit profile: no broad Unit lane. T01's assertion stays unchanged.

## Verification design

T48 (two rows) proves host cancellation stops waiting on a non-cooperative held
consumer before fixture release. T49 proves the three-second monotonic deadline
without host cancellation. T50 proves final-pull cancellation accepts one output
through the real HTTP client, runtime and pump. Every test releases and joins its
callback on green and baseline-red teardown. The fake-clock advance/cancellation
is the stimulus; real-time waits are outer hang guards only, with no sleeps.

The first committed group adds tests against unchanged ff561103 production; all
four new rows must be assertion-red. The second group fixes production and runs
the complete affected classes and adjacent smoke once. Existing success/throw/
timeout/cancellation T47 controls remain in CP-2. No deliberate mutants run here;
PC-1..PC-119 stay pending for method-scoped SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| RED-1 | RepairTests | `tests/Antiphon.Tests -> bin-c1205-e07-red/` | repair-baseline | `/*/*/SessionRunnerHeldEventDeliveryTests*/*` | T48,T49,T50 | Exactly 4 assertion failures on unchanged production, 0 skipped | 4 | 5 |
| CP-1 | RepairFix | `tests/Antiphon.Tests -> bin-c1205-e07-green/` | gap-baseline | `/*/Antiphon.Tests.Application/TurnEndGapReproductionTests*/*` | V-1,V-2,R-1 | Exactly 2 green; preGraceEndCount.ShouldBe(1) unchanged | 2 | 6 |
| CP-2 | RepairFix | `CP-1` | subscription | `/*/Antiphon.Tests.Application/(TurnEndGapReproductionTests*)\|(SessionRunnerStreamReadinessTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerHeldEventDeliveryTests*)/*` | V-1..7,R-1,R-6,T47..T50 | Exactly 34 green, 0 skipped | 34 | 10 |
| ADJ-1 | RepairFix | `CP-1` | adjacent | `/*/*/(SessionRunnerHttpClientHostStatsTests*)\|(SessionRunnerGenerationWireTests*)\|(SessionRunnerSettingsTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | R-7 adjacent compatibility only | Exactly 33 green, 0 skipped | 33 | 3 |

### Cost

RED-1 five minutes; ordinary repair floor nineteen minutes; authoring plus runs
budget sixty minutes. One isolated build per committed group, UseAppHost=false,
forward-slash OutputPath. Tool bootstrap is a separately disclosed leased setup
build. Run with --after RepairTests or --after RepairFix, --serial and the exact
--expected-source-sha. Await the tool until exit is not 75; preserve fresh TRX and
unedited CHECKPOINT lines. Delete only outputs produced by these runs.

No required manual acceptance for S1/S2. S3-S5 V/R are deferred to those slices;
adjacent R-7 green does not discharge CP-3. Restart: none.
