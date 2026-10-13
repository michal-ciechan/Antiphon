# CARD-1205 S1/S2 repair 7 — one exception classification table

Base production: a9256bed2cedde2204766d5d7a08145215b3141b.
Landing owner: efe40115-531a-4664-86d5-c0a87bc899fc (this Code task), inheriting
cbce1a3f-5d59-47d2-a8a3-859ca571c16f and S1 34df9193c.
S3-S5 remain out of scope; explicit brief excludes Unit. Restart: none.

## Verification design

T57 has 42 literal settled rows: success/failure catch-up x host/deadline/no
cancellation x invalid/independent OCE/host OCE/delivery OCE/aggregate/custom/
tokenless OCE. T58 has 32 literal pending-then-late rows: success/failure catch-up
x host/deadline x those seven exceptions or completed. T59 adds four completed
host/deadline race controls; T55 retains the two completed/no-cancellation cells.
These include every one of Review dab30ee3's 26 diagnostic checks (six failing,
twenty controls) and the previously uncovered table cells. No sleeps; actual HTTP
iterator, runtime and readiness handler; fake monotonic clock; explicit callback
gates; late observer scope join before inspecting log absence.

Hand-written expected outcome categories per argument row: settled fault surfaces
the original E object/stack (or original X if catch-up failed); independent and
tokenless OCE are warning faults, as are all secondary E under X. Settled host/
delivery OCE retains E and the pump's normal outer host-shutdown catch consumes it
when host cancelled. Pending callback surfaces genuine deadline TimeoutException
or normal host shutdown signal unless X is already in flight. Late faults cannot
change that primary; warn with the original E, except an owned delivery/host OCE
during host shutdown. Completed callbacks produce no fault or abandonment.

RED-1 runs all 78 new rows against unchanged base production. The six diagnostic
failures must fail object identity or original-object logging, with all twenty
diagnostic controls green. Additional independent/tokenless logging cells may be
red too; no build/fixture failure counts as defect proof. CP-2 preserves all 107
previous cases plus these 78. T01 and T53 assertions/timeouts unchanged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| RED-1 | RepairTests | `tests/Antiphon.Tests -> bin-c1205-r7-red/` | classification-baseline | `/*/*/SessionRunnerHeldEventDeliveryTests*/(C1205_Settled_callback_classification_table*)\|(C1205_Late_callback_classification_table*)\|(C1205_Completed_callback_wins_cancelled_wait*)` | T57..T59,F1,F2 | Exactly 78 execute; diagnostic six assertion-red, twenty controls green; 0 skipped | 78 | 6 |
| CP-1 | RepairFix | `tests/Antiphon.Tests -> bin-c1205-r7-green/` | gap-baseline | `/*/Antiphon.Tests.Application/TurnEndGapReproductionTests*/*` | V-1,V-2,R-1 | Exactly 2 green; unchanged T01 | 2 | 6 |
| CP-2 | RepairFix | `CP-1` | subscription | `/*/Antiphon.Tests.Application/(TurnEndGapReproductionTests*)\|(SessionRunnerStreamReadinessTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerHeldEventDeliveryTests*)\|(SessionRunnerHeldEventExitMatrixTests*)/*` | V-1..7,R-1,R-6,T47..T59 | Exactly 185 green, 0 skipped | 185 | 10 |
| ADJ-1 | RepairFix | `CP-1` | adjacent | `/*/*/(SessionRunnerHttpClientHostStatsTests*)\|(SessionRunnerGenerationWireTests*)\|(SessionRunnerSettingsTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | R-7 adjacent compatibility | Exactly 33 green, 0 skipped | 33 | 3 |

### Cost and deferred scope

One isolated build per committed group, UseAppHost=false, forward slash OutputPath.
No build id named tests. Run --after RepairTests then --after RepairFix, --serial,
full committed --expected-source-sha; await final exit; inspect fresh TRX per row.
Tool bootstrap is the only separately disclosed slot-gated build (no existing tool).
No repeats required after green; no full assembly run. No required S1/S2 manual work.
V-8..40, R-2..5/R-8/R-9 and CP-3..6 deferred to S3-S5; R-7 does not discharge CP-3.
PC-1..190 and every variant pending for method-scoped SourceLanding Mutation.
Post-land Mutation NOT run.

