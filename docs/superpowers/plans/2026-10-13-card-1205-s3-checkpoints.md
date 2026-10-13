# CARD-1205 S3 checked recovery

Original Code/landing owner: 3e736c72-542c-472a-b6c9-1dcd367c7a36.
Base: a608d30e5d5b7af4e5d37e7ea62150e13cc79655.
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
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1205-s3/` | checked-recovery | `/*/Antiphon.Tests.Application/(WorkingTranscriptRecoveryTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | V-8..12,V-25,V-26,R-2,R-7 | Exactly 62 executed; baseline expected guarded assertion reds; implementation all green | 62 | 11 |

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
