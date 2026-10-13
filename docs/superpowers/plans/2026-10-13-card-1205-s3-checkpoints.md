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
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1205-s3/` | checked-recovery | `/*/Antiphon.Tests.Application/(WorkingTranscriptRecoveryTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | V-8..12,V-25,V-26,R-2,R-7 | Exactly 62 executed; baseline expected guarded assertion reds; implementation all green | 62 | 11 |
