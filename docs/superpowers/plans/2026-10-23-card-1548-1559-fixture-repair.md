# CARD-1548/1549/1550/1559 fixture repair

Original Code/landing owner: 3fa840bf-f753-4f7f-8570-ddc66866711b.
Task base: 877b9ce108e6e2bb4ba81840a99c4dd788302ca4 (origin/master at assignment).
Scope: test fixtures only. The commissioning brief overrides the generic Final Unit
lane with the four named affected integration classes. No whole-suite run.

## Verification design

V-1: C1121 purge/reseed invalidates the receipt proof while removing only owned
action dependents before transcript rows. Receipt count, epoch and sequence
assertions stay exact. Retention fixtures first discharge their owned action
custody; production retention/FKs remain unchanged.
V-2: C1121 live committed-row accounting keeps an exact three-pass command budget
and receipt SELECT counts [1,0,0]. Inspect the complete SQL roster and classify
additional authority reads before changing the budget.
V-3: The absent-store fixture resolves no SessionStateStore and still performs
all three receipt scans; disabled-store remains a distinct condition.
V-4: CardCorrection terminal/reopen behavior and cleanup keep owned cleanup
endpoint/target/obligation deletion before revisions. Already repaired at base by
b6615e643e570be7af25ec946153b52f03d3ee79; use inherited diagnosis for historical red.
R-1: Full watermark class preserves all cache invalidation/unknown evidence cases.
R-2: Full warmup and inbound recovery classes preserve retention, replay identity,
store reseeding and adjacent custody safety.
R-3: Full CardCorrection class retains all existing behavior assertions.
R-4: Ordered RC picks apply to 5a126f6fc5ae4e16d803b628716b8f0485a37174 in a
scratch detached worktree and Antiphon.Tests compiles through a host build slot.
No Windows-only code changes. Native Windows qualification remains owed to the
separately commissioned desktop lane; this task must not use that lane.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | baseline | `tests/Antiphon.Tests -> bin-3fa-red/` | inherited-red | `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_AnyCommittedChangeReopensFullScan*\|/*/*/AgentTaskLandReceiptWatermarkTests/C1121_UnchangedTranscriptSkipsOnlyReceiptSelect*\|/*/*/AgentTaskLandReceiptWatermarkTests/C1121_UnknownEvidenceUsesExistingScan*\|/*/*/SessionStateWarmupTests/Retention_*\|/*/*/ChannelInboundRecoveryTests/C768_BrokerReplayAfterPayloadPurgeKeepsNativeIdentity*` | V-1,V-2,V-3 | diagnostic: inherited red required | 29 | 5 | true |
| CP-2 | repair | `tests/Antiphon.Tests -> bin-3fa-final/` | watermark | `/*/*/AgentTaskLandReceiptWatermarkTests/*` | V-1,V-2,V-3,R-1 | all listed, 0 failed | 36 | 5 | true |
| CP-3 | repair | CP-2 | warmup | `/*/*/SessionStateWarmupTests/*` | V-1,R-2 | all listed, 0 failed | 16 | 2 | true |
| CP-4 | repair | CP-2 | inbound-recovery | `/*/*/ChannelInboundRecoveryTests/*` | V-1,R-2 | all listed, 0 failed | 27 | 5 | true |
| CP-5 | repair | CP-2 | card-correction | `/*/*/CardCorrectionIntegrationTests/*` | V-4,R-3 | all listed, 0 failed | 43 | 3 | true |

### Cost

One red-witness build/run, then one final shared build with four class rows.
Hard run deadlines: red 15m, final 30m; row cap 15m. Estimates include build
startup, not a claim of measured class cost. Record actual per-class durations.
The only additional builds are checkpoint-tool bootstrap and required RC compile;
both use the host build-slot gate. No repetitions after green.

## Mutation handoff

No new product behavior or deliberate mutants. No new PC IDs. All existing C1121
PCs remain pending for SourceLanding Mutation; ordinary green does not discharge
them. Mutation also owns missing-control discovery for these fixture repairs.

## Baseline correction

CP-1 completed with 36 watermark cases only: 32 passed, 4 inherited failures.
Its historical filter used unsupported full-path OR syntax; it did not select
warmup/inbound. Use retained diagnosis for those base witnesses (cost exception
in the brief). No baseline pass is claimed for those classes. Final rows select
one class each and inspect every fresh TRX identity. Evidence and exact RC picks:
`docs/investigations/2026-10-23-card-1548-1559-fixture-repair.md`.
