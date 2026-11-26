# CARD-2084 placement ordering verification

Task base `c7221e8568087a7683cb4c619c81184fe71ea5da`. See the [CARD-2080 timing investigation](../../investigations/2026-11-26-card-2080.md) and Review `d2c7cfba`.

- V-1 requires a successful release of another session's existing slot while a tracked Linux placement is held at the runtime boundary. The red witness lets the original placement budget expire and measures the queue time. A release of the launching session still waits.
- V-2 preserves capacity and generation admission and ordering between launch, mirror, slot custody and retention. With the first placement held, race the last seat, resend the same generation, try a newer generation for the same session, kill the launching generation, and dispatch a mirror request. Assert each waits, then check the typed outcomes, runtime start count and surviving session generation. Check the CARD-1677 two-phase retention contract and the `workspace-mutation-lock` impact row.
- V-3 requires refusal before GO for a valid PLACED sent at the absolute cutoff while the timer callback is held. The same witness accepts the timely path and independently checks that timer expiry closes a silent peer.
- V-4 requires all native stage and lifecycle cases on a Linux v1 custody fixture with the stage installed. Run the test host as a child of init. A native skip or an unsupported installation leaves this gate open.
- V-5 requires the guard union, live-gate inventory companion, Linux marker/pin checks in the relevant assemblies, impact resolution, evidence-diff and a conflict check against fetched master. The 20-sample collection stays with the timing follow-up.

R-1 checks that the launch completion fence opens on failure as well as success. Inject a placement failure at the real runtime's provider boundary after it registers the session. Verify the table entry disappears and the waiting Launch reaches the provider with capacity one. The existing unrelated-release test also bounds both failure-path joins. R-2 checks retention keeps recent/live trees and keeps phase A ordered, while phase B and Input remain independent. R-3 checks the native stage receives the deadline and the lifecycle still proves release, including after host loss. No migration.

## Performance budget

The bounded selections cover phone-home ordering, Linux placement, custody and workspace slot mutations. Policy, layering and inventory guards scan broad source inputs within their selected classes and methods. The nightly and rc runs own whole-lane qualification. Round 2 budgets 22 minutes for CP-2 through CP-9 including cold builds, plus build-slot waits. Review `a579caef` run `20261126-171213-2b81` supplies the previous row timings. The new interleavings use in-process gates and isolated runtime state with no shared service startup. Five seconds, matching the existing dispatcher join bound, is a provisional diagnostic ceiling for a stuck gate or reply. The timing follow-up owns the larger sample set. Checkpoint reports retain slot wait, build and execution phases and the TRX retains per-test durations.

### Checkpoints

Supply `ANTIPHON_PLACEMENT_FIXTURE_STAGE` in the executor environment for the source-matched unprivileged stage. The native custody row needs the installed v1 stage, namespace and helpers. `CP-1` records the historical red on the witness-only SHA. `CP-2` includes the Round 2 dispatcher and runtime cleanup witnesses. `CP-8` covers the Windows exclusion policy affected by the Linux runtime cleanup witness. `CP-9` covers the named `nightly-live-lane` integration classes affected by that policy and its live-lane digest.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | red | `tests/Antiphon.SessionRunner.Tests -> bin-c2084-red/` | release-witness | `/*/*/PhoneHomeCommandDispatcherTests/Unrelated_slot_release_does_not_wait_for_stuck_linux_placement` | V-1 | One expected red on witness-only SHA | 1 | 3 | true |
| CP-2 | final | `tests/Antiphon.SessionRunner.Tests -> bin-c2084-r2-runner/` | dispatch-and-custody | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(PhoneHomePendingPlacementCleanupTests*)\|(C1677RetentionDispatchTests*)\|(C0824WorkspaceSlotCustodyTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | V-1, V-2, V-5, R-1, R-2 | All selected classes, zero failures or skips | 88 | 5 | true |
| CP-3 | final | CP-2 | native-custody | `/*/*/LinuxRunnerCustodyLifecycleTests/*` | V-4, R-3 | All lifecycle methods, zero failures or skips | 6 | 2 | true |
| CP-4 | final | `tests/Antiphon.PtyHost.Tests -> bin-c2084-host/` | handshake | `/*/*/(Card2060PlacementHandshakeTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | V-3, V-4, V-5, R-3 | 17 handshake cases and three marker guards, zero failures or skips | 20 | 4 | true |
| CP-5 | final | `tests/Antiphon.Tests -> bin-c2084-guards/` | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | V-5 | All guard classes, zero failures or skips | 59 | 7 | true |
| CP-6 | final | CP-5 | live-inventory | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | V-5 | Both methods, zero failures or skips | 2 | 1 | true |
| CP-7 | final | `tests/Antiphon.Agents.Pty.Tests -> bin-c2084-pty/` | pty-policy | `/*/*/NativeLinuxPolicyWitnessTests/Policy_rows_require_pinned_attribute` | V-5 | One method, zero failures or skips | 1 | 1 | true |
| CP-8 | final | CP-5 | linux-exclusion-policy | `/*/*/ReleaseGatePlatformTests/(LinuxOnlyWindowsExclusionPolicy*)\|(C1686_LinuxOnlyCensus*)` | V-5 | Both impacted methods, zero failures or skips | 2 | 1 | true |
| CP-9 | final | CP-5 | nightly-live-lane | `/*/*/(LiveLaneRunnerTests*)\|(NightlyCoreGoldenTests*)/*` | V-5 | Both impacted classes and the live-lane digest, zero failures or skips | 2 | 1 | true |
