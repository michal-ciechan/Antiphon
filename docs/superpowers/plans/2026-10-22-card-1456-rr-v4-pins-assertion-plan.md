# CARD-1456 RR_V4 pins assertion: an entry refusal leaves no recovery pin

Date: 2026-10-22. Code task 70570ea4 (Final round, minimal testing: no Unit lane). Test-only change;
no product code moves.

`AgentTaskLandRefusedRetryTests.Scenario.RefuseAsync` asserted `_pins.ShouldNotBeEmpty()` (line 516)
for every refusal. `RR_V4_RemoteReadFailureRetriesUnchangedSource` injects `source_remote_unreadable`
at the protocol entry recheck (`AgentTaskLandingProtocol.cs:211`), before the Inspected block writes the
`source`/`target-before` recovery pins (`:220-225`). The only ref under the operation prefix is the
factory's `remote-observed` observation pin (`LandOperationFactory.cs:46`), which CARD-1432 S2 deletes on
the terminal land (`LandRefRetention.IsDeletable`). Final Review 7e34f413 of CARD-1449 proved the test
first fails at 6cd9317a0 (S2) and passes whole with only line 516 bypassed.

## Slice S1 (one commit group)

1. `tests/Antiphon.Tests/Application/AgentTaskLandRefusedRetryTests.cs`: `RefuseAsync` takes
   `recoveryPinned` (default `true`, every other caller unchanged). `RR_V4_RemoteReadFailure` passes
   `false`; that branch asserts the pin set is empty, `SourcePinned`/`TargetPinned`/`PreparedPinned`
   are false, and `<prefix>/remote-observed` no longer resolves (S2). Every retry assertion of the test
   (ls-remote, fetch of B's recovery prefix, merge-base, target status, `AssertReplacementAsync`
   with `Mode == Fresh`, `AssertSuccessAsync`) is kept; `AssertPinsAsync` compares the empty set.

## Verification design

Red witness (one unlisted narrow run, base source `673a8c6d759b379294469985111e49ae5c8ede2b`, isolated
`bin-c1456-red/` build through `scripts/build-slot.ps1`): filter
`/*/*/AgentTaskLandRefusedRetryTests/RR_V4_RemoteReadFailureRetriesUnchangedSource` executes 1 and
fails 1 at `Scenario.RefuseAsync` line 516 (`_pins`). The Code report records the leases.

| ID | Invariant | Evidence |
|---|---|---|
| V-1 | After a `source_remote_unreadable` refusal at entry, the operation prefix holds no ref at all (no recovery pin was written; the observation pin is deleted by S2), the operation records no pin, and the retry from the unchanged source still replaces A with a Fresh B and lands. | CP-1 (`RR_V4_RemoteReadFailureRetriesUnchangedSource` past line 516) |
| R-1 | Every other `AgentTaskLandRefusedRetryTests` case keeps its outcome; refusals that reach the Inspected block still keep their recovery pins (`_pins.ShouldNotBeEmpty()` is unchanged for them). | CP-1 (whole class) |

Known reds at the task base (CARD-1449, not landed at `673a8c6d7`; investigation
`docs/investigations/2026-10-22-card-1449-land-protocol-base-reds.md`): the eight RR cases that pass
through the verification window fail at `b.Mode.ShouldBe(LandOperationMode.Fresh)` (`ResumePublication`
relabel on the ticketed resume): `RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation`,
`RR_V3_TargetOnlyAdvanceRequiresFreshSelectedVerification`, `RR_V4_RemoteReadFailureRetriesUnchangedSource`,
`RR_V4_TargetStatusFailureRetriesFromFreshEvidence(True)`, `RR_V6_PreparationFailurePreservesActiveRefusal`
(2 cases), `RR_V6_ReplacementSaveFailureRollsBackOldDeactivation` (2 cases). They are documented
non-receipt rows; CP-1's expected result is exactly those eight, with
`RR_V4_RemoteReadFailureRetriesUnchangedSource` now failing at the CARD-1449 `Mode` assertion instead of
line 516. Once CARD-1449 lands the same row is expected 15 passed, 0 failed/skipped (the Code report's
unlisted combined run on CARD-1449's reviewed tip plus S1 records that outcome).

Positive controls: none planned. The change is test-only; deliberate mutants and missing-control
discovery stay with SourceLanding Mutation (zero-PC plan).

Lane: portable .NET/PostgreSQL on the eligible default runner. No `-Runner`, no `-Platform`.
The class carries no platform skip, so the whole-class filter runs every case.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1456/` | c1456-refused-retry | `/*/*/AgentTaskLandRefusedRetryTests/*` | V-1, R-1 | 15 cases executed, 0 skipped; before CARD-1449 lands: 7 passed and exactly the 8 documented CARD-1449 `Mode` reds failed, none at line 516 (documented non-receipt rows, run exit 1); after CARD-1449 lands: 15 passed, 0 failed | 15 | 12 |

### Cost

Ordinary floor 12 minutes (CP-1) plus the unlisted red witness, the checkpoint tool bootstrap build,
the rc/2026-10-20 scratch check and the combined CARD-1449-tip diagnostic described in the Code report.

## rc/2026-10-20 check (Code report records the outcome)

CARD-1432 S2 (6cd9317a0) is not on `rc/2026-10-20`, so the observation pin survives there and line 516
is not red on rc; the test is expected red on rc only at the CARD-1449 `Mode` assertion. S1 must not be
cherry-picked onto rc without S2: on a tree without S2 its `_pins.ShouldBeEmpty()` is red by construction.
