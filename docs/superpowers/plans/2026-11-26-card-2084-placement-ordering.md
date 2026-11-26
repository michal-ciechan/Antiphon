# CARD-2084 placement ordering verification

Task base `c7221e8568087a7683cb4c619c81184fe71ea5da`. See the [CARD-2080 timing investigation](../../investigations/2026-11-26-card-2080.md) and Review `d2c7cfba`.

- V-1 requires a successful release of another session's existing slot while a tracked Linux placement is held at the runtime boundary. The red witness lets the original placement budget expire and measures the queue time. A release of the launching session still waits.
- V-2 preserves capacity and generation admission and ordering between launch, mirror, slot custody and retention. Check the dispatcher, the CARD-1677 two-phase retention contract, and the `workspace-mutation-lock` impact row. Preserve the existing workspace fence for repository operations during pending placement.
- V-3 requires refusal before GO for a valid PLACED sent at the absolute cutoff while the timer callback is held. The same witness accepts the timely path and independently checks that timer expiry closes a silent peer.
- V-4 requires all native stage and lifecycle cases on a Linux v1 custody fixture with the stage installed. Run the test host as a child of init. A native skip or an unsupported installation leaves this gate open.
- V-5 requires the guard union, live-gate inventory companion, Linux marker/pin checks in the relevant assemblies, impact resolution, evidence-diff and a conflict check against fetched master. The 20-sample collection stays with the timing follow-up.

R-1 checks that the launch completion fence opens on failure as well as success. R-2 checks retention keeps recent/live trees and keeps phase A ordered, while phase B and Input remain independent. R-3 checks the native stage receives the deadline and the lifecycle still proves release, including after host loss. No migration.

The bounded integration selections cover phone-home ordering, Linux placement, custody and workspace slot mutations. The policy, layering and inventory guards have broad source inputs, but the filter is restricted to their guard classes. Unit and other integration classes stay in nightly/rc. Total final estimated cost is 20 minutes including cold builds, plus build-slot waits.

### Checkpoints

Supply `ANTIPHON_PLACEMENT_FIXTURE_STAGE` in the executor environment for the source-matched unprivileged stage. The native custody row needs the installed v1 stage, namespace and helpers. `CP-1` repeats only to record the red-first comparison. `CP-2` is the final broader scoped dispatcher check.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | red | `tests/Antiphon.SessionRunner.Tests -> bin-c2084-red/` | release-witness | `/*/*/PhoneHomeCommandDispatcherTests/Unrelated_slot_release_does_not_wait_for_stuck_linux_placement` | V-1 | One expected red on witness-only SHA | 1 | 3 | true |
| CP-2 | final | `tests/Antiphon.SessionRunner.Tests -> bin-c2084-runner/` | dispatch-and-custody | `/*/*/(PhoneHomeCommandDispatcherTests)\|(C1677RetentionDispatchTests)\|(C0824WorkspaceSlotCustodyTests)\|(NativeLinuxPolicyWitnessTests)/*` | V-1, V-2, V-5, R-1, R-2 | All selected classes, zero failures or skips | 4 | 5 | true |
| CP-3 | final | CP-2 | native-custody | `/*/*/LinuxRunnerCustodyLifecycleTests/*` | V-4, R-3 | All lifecycle methods, zero failures or skips | 6 | 2 | true |
| CP-4 | final | `tests/Antiphon.PtyHost.Tests -> bin-c2084-host/` | handshake | `/*/*/(Card2060PlacementHandshakeTests)\|(NativeLinuxPolicyWitnessTests)/*` | V-3, V-4, V-5, R-3 | 17 handshake cases and marker check, zero failures or skips | 18 | 4 | true |
| CP-5 | final | `tests/Antiphon.Tests -> bin-c2084-guards/` | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | V-5 | All guard classes, zero failures or skips | 59 | 7 | true |
| CP-6 | final | CP-5 | live-inventory | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | V-5 | Both methods, zero failures or skips | 2 | 1 | true |
| CP-7 | final | `tests/Antiphon.Agents.Pty.Tests -> bin-c2084-pty/` | pty-policy | `/*/*/NativeLinuxPolicyWitnessTests/Policy_rows_require_pinned_attribute` | V-5 | One method, zero failures or skips | 1 | 1 | true |
