# CARD-1440 fixture repair: hot-path ingest wiring and C1205 replace-frontier

Date: 2026-10-22. Code task d3d1fdab (Final round, minimal testing: no Unit lane).
Investigation: [2026-10-22-card-1440-hotpath-and-c1205-reds.md](../../investigations/2026-10-22-card-1440-hotpath-and-c1205-reds.md)
(Investigate task 4ff055e6, commit 0c87a382107a8ecb3b7658e6cc0b9c78a3b5df94).

Test-only change; no product code moves. Both reds are stale fixtures from CARD-1259 S2
(`dd039a405`), which made `SessionStateStore` mandatory for transcript ingestion and made every
ingested row hold a Restrict FK from `SessionRuntimeActions`.

## Slice S1 (one commit group)

1. `tests/Antiphon.Tests/TestHelpers/TranscriptHotPathFixture.cs`: register logging,
   `TimeProvider.System`, `SessionStateSettings`, `SessionStateCommandMetrics`,
   `ISessionStateLoader -> SessionStateLoader`, `SessionStateStore` and
   `SessionRuntimeActionCoordinator` (the `BridgeQueueHarness` set), pass `states:` to
   `AgentSessionRuntime`, and replace `NullLogger` with a `RecordingLogger<AgentSessionRuntime>`
   whose logged exception chain is printed by the `MembershipAsync` failure assertion.
2. `tests/Antiphon.Tests/Application/WorkingTranscriptRecoveryTests.cs`
   `C1205_Concurrent_commit_invalidates_snapshot`, `replace-frontier` branch: delete the
   session's `SessionRuntimeActions` before its `TranscriptEntries` inside the same mutation.

## Verification design

Red witness (one unlisted run, base source `0c87a382107a8ecb3b7658e6cc0b9c78a3b5df94`, isolated
`bin-c1440-red/` build through `scripts/build-slot.ps1`): filter
`/*/*/(TranscriptHotPathQueryTests*)|(WorkingTranscriptRecoveryTests*)/(Uuid_membership_seeks_by_session_and_uuid*)|(C1205_Concurrent_commit_invalidates_snapshot*)`
executes 13 and fails 3: `Uuid_membership_seeks_by_session_and_uuid`,
`C1205_Concurrent_commit_invalidates_snapshot(replace-frontier, True|False)`. These are the
guarded defects: the same selection is green inside CP-1/CP-2 after S1.

| ID | Invariant | Evidence |
|---|---|---|
| V-1 | With the store and coordinator wired, every `TranscriptHotPathQueryTests` method passes and the plan/index assertions are unchanged. | CP-1 |
| R-1 | A future persist failure in `MembershipAsync` names the logged exception chain, never a bare type name. | CP-1 (assertion message path; exercised red in the witness run through the old `NullLogger`, now carries the message) |
| V-2 | The `replace-frontier` variant models a legal transcript replacement (actions removed before entries) and still invalidates the probe snapshot (`Unknown`, no rows appended). | CP-2 |
| R-2 | No other `WorkingTranscriptRecoveryTests` case changes outcome. | CP-2 (whole class) |

Positive controls: none planned. The change is test-only; deliberate mutants and missing-control
discovery stay with SourceLanding Mutation (zero-PC plan).

Lane: portable .NET/PostgreSQL on the eligible default runner. No `-Runner`, no `-Platform`.
Neither class carries a platform skip, so both whole-class filters run every case and the
receipt validates.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1440/` | hot-path-class | `/*/*/TranscriptHotPathQueryTests/*` | V-1, R-1 | all 4 methods, 0 failed/skipped | 4 | 9 |
| CP-2 | S1 | CP-1 | c1205-recovery-class | `/*/*/WorkingTranscriptRecoveryTests/*` | V-2, R-2 | all 13 methods (54 argument cases), 0 failed/skipped | 54 | 7 |

### Cost

Ordinary floor 16 minutes (CP-1 9 + CP-2 7) plus the unlisted red witness, the checkpoint tool
bootstrap build and the rc/2026-10-20 scratch check described in the Code report.

## rc/2026-10-20 check (Code report records the outcome)

Both classes are part of the rc qualification (CARD-1259 is on rc). In a discarded scratch
worktree: cherry-pick the S1 commits onto `origin/rc/2026-10-20` and report conflicts; at the
unmodified rc tip run one narrow unlisted run per class (the red-witness method filters) and
report whether the same reds exist there.
