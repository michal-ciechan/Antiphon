# CARD-2085 checkpoint held-handle audit

The Code brief supplies the acceptance scope for this follow-up to the landed
[Windows warm-build repair](../../investigations/2026-11-26-why-missed-group-113-warm-build-overlap.md).
The original [CARD-2054 plan](2026-11-24-card-2054-checkpoint-cache-seal-plan.md)
retains its independent qualification debt. The task base is
`5b385e77e17ec147ae58107c0bcc40f1e0ab6127`.

## File handle audit

| Path | Handle lifetime and custody check |
|---|---|
| `OpenCreate` via cache `CreateBlank`, `PublishNewBytes` and `WriteSeal` | An exclusive creator binds the identity from its opened handle. An exception during bookkeeping releases the stream before it reaches the caller. A blank lock is closed before a subsequent acquisition. Props and seal staging streams close before pathname rechecks and rename. |
| `OpenGate` via `TerminalPublicationGate` | Capture identity from the retained exclusive lock handle. Tool-copy and test-root gates share the same primitive. A separate attempt to acquire a held gate remains a contender. |
| `OpenAppend` via `CheckpointUsageEvent.Write` | Capture identity from the retained exclusive append handle. Preserve one complete record per append and the existing contention retry. |
| `OpenAppend` via `FileExecutorLogSink` and `ProcessDriver` | Use the same held-handle recording with the caller's read/write/delete sharing. Preparation logging precedes the long-lived driver stream. Scheduler admission and settlement logging occur outside that stream's lifetime. Wait and other observers read through their own share-compatible handles. |
| Cache `OpenExisting` for `run.lock` and a seed seal | Validate the opened handle while it is held. Initial path checks run before acquisition. The overlap and replay tests intentionally attempt an independent acquisition while the first owner holds `run.lock`. On Windows, that contender can spend the bounded custody retry before it falls back or refuses. Seal preparation closes its validation handle before entering publication. |
| `TimingJournal.AppendAsync` and receipt/state publication | The journal's scan ends before it opens the append stream. Receipt and state writes use closed files for pathname checks and publication. |

## Verification

| ID | Acceptance |
|---|---|
| V-1 | Two portable variants for exclusive gate and append. Observe zero path identity probes, bound identity and creation registration. The Windows branch verifies a live second open fails with sharing violation 32. Gate bytes stay intact and append preserves prior bytes. |
| V-2 | A real exclusive create throws from held-handle inspection. The original exception reaches the caller, the native handle closes, no stream is handed off, and another exclusive open can succeed. |
| V-3 | The previous OpenCreate witness directly asserts creation registration and a bound identity. M2 remains pending for Mutation. |
| V-4 | The overlap preserves the first row's warm binding, sends the second build cold, refuses the independent contender, drains both rows and reacquires the sealed partition. Collect five watchdog-window samples with phase timestamps. |
| V-5 | All named impacted classes, the Final guard batch, and the live-lane guard pair have fresh nonzero TRX evidence. The six policy guards and the plan class selection guard are identifiable in the same validated receipt. |
| R-1 | Query both runner routes. Native Windows confirmation remains pending under the active qualification hold. Run `CheckpointHeldHandleTests.gate_and_append_record_identity_without_reopening` (both arguments) and `CheckpointWarmBuildTests.overlapping_build_keeps_first_rows_partition` at the reviewed SHA once a native slot is available. Budget one minute for each test host invocation, excluding build and slot wait. Preserve native TRX and timed phases. |
| R-2 | Preserve the separate CARD-2054 R4 and native Windows qualification debt, including cross-process contention, temporary close/replacement and reacquisition after publication failure. |
| R-3 | Run evidence-diff from the full task base and a merge-tree check against fetched master. |
| R-4 | Check OS witness declarations at the committed SHA. Linux placement skips for the native recovery, anchored delete and custody cases selected by impact remain pending on Windows. |

## Performance budget

The overlap fixture uses its own small Git tree and file I/O. Seed, gate open,
first build, first row, second build slot wait, independent gate contention,
scheduler completion, release and seal verification are recorded in its TRX
output. Phase capture also uses the opt-in test timing recorder. There is no
web host, database, container or live broker in the fabricated overlap.
The watchdog covers gate opening through completion of both scheduled rows.
The five portable observations use three method-only runs and two runs with
other warm-build fixture cases. Report host load, slot wait and test time
separately.

The Linux build at `f9096cd25fdcff6946769186061411f9cb875912` supplied
watchdog windows of 493.4, 569.8 and 426.6 ms in method-only execution and
963.9 and 159.6 ms with the publication and admission cases selected alongside
it. The provisional nearest-rank p95 for those five quieter runs was 963.9 ms.
Review `cd5da02e` at `6d94c939301bcb9f6029357ea476f415835236a6` added
six Linux observations at server2 load 31–48. One CP-1 run alongside CP-2
measured 159.6 ms. Five method-only runs measured 831.5, 349.1, 367.7,
771.6 and 1436.3 ms. The combined 11-sample nearest-rank p95 is a
provisional 1436.3 ms. Linux returns to the original 5 s watchdog while
loaded calibration is pending. Windows retains its provisional 5 s watchdog
pending native calibration. Retain these samples separately from final
qualification of the repair.

R5 follow-up (CARD-2085) — collect 20 watchdog-window samples on a loaded
Linux host and 20 natively on Windows, then reassess each budget against
twice its measured p95.

The main test project shares one isolated Debug build across five rows.
Expect 5 to 8 minutes for the main build, up to 2 minutes each for the affected
class and guard rows, and up to 1 minute for the live-lane pair. Allow up to
3 minutes for each of the two derived impact rows. The two
process-roster mirror projects each have two methods and a separate Debug
build, provisionally 1 to 3 minutes each. The checkpoint run is serial across
rows to keep the Pty and main test executions separate. Capture queue times,
build, host execution and TRX independently. The closed rows bound the affected
classes in each project. The nightly and rc lanes own full-assembly execution.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c2085-check/` | held-handles | `/*/*/(CheckpointWarmBuildTests*)\|(CheckpointHeldHandleTests*)\|(CheckpointTerminalReceiptTests*)\|(CheckpointExecutorLogTests*)\|(ProcessDriverTests*)\|(CheckpointOwnedOutputTests*)\|(CheckpointToolCopyCleanupTests*)\|(CheckpointTempScopeTests*)\|(CheckpointTempUsageTests*)\|(HostCleanupCheckpointCustodyTests*)/*` | V-1 to V-5 | Each class present and zero failures | 35 | 8 |
| CP-2 | S1 | CP-1 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)\|(ProcessSpawnLimitTests*)\|(TestTimingOperationLabelTests*)/*` | V-5 | Each class present and zero failures | 60 | 2 |
| CP-3 | S1 | CP-1 | live-lane-inventory | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | V-5 | Both methods pass | 2 | 1 |
| CP-4 | S1 | `tests/Antiphon.Agents.Pty.Tests -> bin-c2085-pty/` | pty-roster | `/*/*/(ProcessSpawnLimitTests*)/*` | V-5 | Both methods pass | 2 | 3 |
| CP-5 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c2085-runner/` | runner-roster | `/*/*/(ProcessSpawnLimitTests*)/*` | V-5 | Both methods pass | 2 | 3 |
| CP-6 | S1 | CP-1 | impacted-execution | `/*/*/(CheckpointAdmissionContractTests*)\|(CheckpointBuildBindingPathTests*)\|(CheckpointImportTests*)\|(CheckpointLargeStampTests*)\|(CheckpointOrFilterTests*)\|(CheckpointProcessIdentityTests*)\|(CheckpointSelectedOutputTests*)\|(CheckpointTaskOwnershipTests*)\|(OuterRowBuildStampTests*)\|(PhaseJournalKillDiagnosticTests*)\|(RowRunnerTests*)\|(WaitCommandTests*)/*` | V-5 | Each class present and zero failures | 60 | 3 |
| CP-7 | S1 | CP-1 | impacted-custody | `/*/*/(AnchoredPathDeleteWitnessTests*)\|(CheckpointRecoveryWindowsTests*)\|(CheckpointWindowsAnchoredDeleteTests*)\|(CheckpointWindowsAnchoredPathDeleteWitnessTests*)\|(EvidenceFolderTests*)\|(HostCleanupCheckpointAuthorityTests*)\|(HostCleanupCheckpointCompatibilityTests*)\|(HostCleanupCheckpointExtractionTests*)\|(OutputCleanupTests*)\|(UnsafeDeleteRecoveryTests*)/*` | V-5 and R-4 | Each class present and zero failures. Windows-only methods are eligible for pinned roster skips on Linux. | 60 | 3 |
