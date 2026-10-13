# CARD-1205 S3 checked recovery

Repair Code/landing owner: 080e0111-220a-4633-bc3e-efa40d4016ff.
Original S3 Code task: 3e736c72-542c-472a-b6c9-1dcd367c7a36.
Repair base: 742e3404c26df504e30727a6178f66076cb41485.
Scope: S3 only; frozen T10..T16, V-8..12,V-25,V-26,R-2,R-7.
The explicit slice brief and frozen design exclude a broad Unit lane.
S4/S5 own scheduling, flush callbacks and recipient receipts. No receipt claim in S3.

Baseline uses a temporary test-only extension that calls the existing real
CatchUpForReceiptAsync path. It derives observations from actual committed state;
the implementation commit removes the extension and calls the checked API.
Rows protecting existing persistence semantics can be baseline green controls;
missing guards must fail outcome/no-append assertions, not fixture/build errors.
All PC-11..40 and variants remain pending for method-scoped SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| F1-BASE | Tests | `tests/Antiphon.Tests -> bin-c1205-f1-base/` | late-generation-baseline | `/*/Antiphon.Tests.Application/WorkingTranscriptRecoveryTests*/C1205_Generation_changed_after_validation_must_not_append_old_end*` | F1 | One guarded assertion red on unchanged 742e3404 production | 1 | 6 |
| CP-1 | Repair | `tests/Antiphon.Tests -> bin-c1205-f1-final/` | gap-baseline | `/*/Antiphon.Tests.Application/TurnEndGapReproductionTests*/*` | V-1,V-2,R-1 | Exactly 2 green | 2 | 6 |
| CP-2 | Repair | `tests/Antiphon.Tests -> bin-c1205-f1-final/` | subscription | `/*/Antiphon.Tests.Application/(TurnEndGapReproductionTests*)\|(SessionRunnerStreamReadinessTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerHeldEventDeliveryTests*)\|(SessionRunnerHeldEventExitMatrixTests*)/*` | V-1..7,R-1,R-6 | Exactly 211 green | 211 | 10 |
| ADJ-1 | Repair | `tests/Antiphon.Tests -> bin-c1205-f1-final/` | adjacent | `/*/*/(SessionRunnerHttpClientHostStatsTests*)\|(SessionRunnerGenerationWireTests*)\|(SessionRunnerSettingsTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | wire and working-state compatibility | Exactly 33 green | 33 | 3 |
| CP-3 | Repair | `tests/Antiphon.Tests -> bin-c1205-f1-final/` | checked-recovery | `/*/Antiphon.Tests.Application/(WorkingTranscriptRecoveryTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | V-8..12,V-25,V-26,R-2,R-7,F1,V-F1..6 | Exactly 74 green: original 62 plus 12 repair rows | 74 | 11 |

## Test-first baseline

At 459f3c500566679d0a85aa4095d9e5303e9cb85a, unchanged pre-S3 production
ran 62 executions: 33 assertion reds, 29 controls green, zero skipped or fixture
errors. T10..T16 use real HTTP mapping, persistence and state readers. The nine
new baseline-green variants protect already-landed persistence/rejection behavior;
they are compatibility controls, with mutant proof still pending. Two earlier
diagnostic runs found fixture errors in incomplete remote bindings; the valid
all-or-none binding fixes those. The final API has no baseline adapter.

The result method returns after its using-owned lease disposes (T13/T15).
S4 owns scheduling and ordinary completion flush work; S5 owns recipient witnesses.
No delivery receipt, operational restart, bounded payload size or self-consistent
truncated-history detection is claimed by this slice.

Implementation verification: run CP-3 with the exact committed HEAD as expected
source SHA. Preserve receipts and all expanded method/argument counts in
.antiphon/task-3e736c72.md.
