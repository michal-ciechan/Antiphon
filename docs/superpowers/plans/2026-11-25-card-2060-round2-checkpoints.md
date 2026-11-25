# CARD-2060 round 2 checkpoints

Review 6e6013b4 (round 1) found the placement implementation sound but its highest-risk decisions unwitnessed: mutants A1 (GO without PLACED validation), A2 (the stage skips the v1 membership proof) and B (a lost RELEASING treated as released) survived every row. This amendment adds the rows for the round-2 Code task. The [landed plan](2026-11-24-card-2060-custody-placement-shim-handshake-plan.md) and its [test design](2026-11-24-card-2060-custody-placement-shim-handshake-test-design.md) stay authoritative; CP-1 (the S1 red witness) is historical and is not repeated.

## New witnesses

All are `Card2060PlacementHandshakeTests` methods in `tests/Antiphon.PtyHost.Tests` and are Linux-only by construction.

| Method | Control | Decisive assertion, real → mutant |
|---|---|---|
| `HostChecksSpawnPeer` | PC-5 | first frame to a peer with the right UID and a distinct PID: none → PROVE |
| `MismatchedPlacedWithholdsGo` | Q2 host validator fault fixture (A1) | reply to a PLACED differing in challenge, namespace, PID, parent, start ticks, boot or controller mask: none → GO |
| `LostAckRetainsLaunchedChild` | PC-22 (B, PC-22/a) | exchange after GO without RELEASING completes and `Commit()` is null → throws, or returns a proof |
| `MixedFreezerCannotAttest` | PC-4 (A2) | stage reply with an extra freezer member: none → PLACED |
| `StageWaitsForGo` | PC-13 | stage holding after PLACED with GO withheld: no RELEASING, no exec → RELEASING and exec |
| `FailedReleaseSendSuppressesExec` | PC-14 | stage whose RELEASING send fails after a valid GO: exec calls 0 → 1 |
| `DropPreservesPayloadBoundary` | PC-15 | recorded exec argv equals the fixed `setpriv` prefix, separator and payload argv |

Host cases drive the real `LinuxPlacementAttempt` against an in-process Unix socket peer. The attempt's expected peer UID is root in production; the fixture names its own UID. Stage cases run a test-only build of the real stage source (`-DANTIPHON_PLACEMENT_FIXTURE`): private cgroup and namespace roots, the caller's UID as listener, and an exec recorder on stdout. The `session-testing` image installs it at `/opt/antiphon-tests/antiphon-custody-placement-fixture`; `ANTIPHON_PLACEMENT_FIXTURE_STAGE` names another build of the same source. `CustodyHelperContractTests.PlacementFixtureBuildStaysTestOnly` keeps the adapter out of the installed stage. Every positive control stays pending for SourceLanding Mutation.

The runner launch path (`SessionRunnerRuntime` placement feature requirement and launch retirement) has no green native witness in this round: its native methods in `LinuxRunnerCustodyLifecycleTests` are red at base and tip on CARD-2067 (runner/host start-time exact compare), so CP-9 selects only the probe methods and that obligation stays pending.

### Checkpoints

CP-2, CP-3, CP-8, CP-9 and CP-10 need the native Linux cgroup v1 lane and the matched `session-testing` image built from the tested SHA (privileged, host PID and cgroup namespaces, a fresh custody namespace). CP-4 to CP-7 run on any Linux runner. Require a unique TRX, the exact roster and zero skips.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-2 | r2 | tests/Antiphon.PtyHost.Tests -> bin-c2060r2-native/ | r2-native | `/*/*/LinuxCgroupCustodyTests/(C2060_Preexec_placement_identifies_the_first_root*)\|(C1614_Natural_descendant_exit_finalizes_without_release*)` | V-1, R-1 | both named methods, 0 failed or skipped | 2 | 4 | true |
| CP-3 | r2 | CP-2 | r2-handshake | `/*/*/Card2060PlacementHandshakeTests/*` | PC-4, PC-5, PC-13, PC-14, PC-15, PC-22, Q2 | 0 failed, executed at least 16 | 16 | 4 | true |
| CP-8 | r2 | CP-2 | r2-hangup | `/*/*/LinuxCgroupCustodyTests/PipeHangupWinsBeforeGo` | PC-19 host path | 0 failed, executed 1 | 1 | 4 | true |
| CP-9 | r2 | tests/Antiphon.SessionRunner.Tests -> bin-c2060r2-runner/ | r2-runner-probe | `/*/*/LinuxRunnerCustodyLifecycleTests/(C1614_Installed_helpers_advertise_custody*)\|(C1614_Container_namespaces_and_probes_do_not_collide*)` | S2 placement probe | 0 failed, executed 2 | 2 | 6 | true |
| CP-10 | r2 | CP-9 | r2-runner-ledger | `/*/*/(LinuxCustodyProbeTests*)\|(Card2060PlacementLedgerTests*)/*` | PC-31, PC-32, PC-33, PC-41 designs | 0 failed, executed at least 4 | 4 | 4 | true |
| CP-4 | r2 | tests/Antiphon.Tests -> bin-c2060r2-guards/ | r2-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestLaneCategoryGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestFixtureCleanupGuardTests*)/*` | guards | 0 failed, executed at least 85 | 85 | 12 | true |
| CP-5 | r2 | CP-4 | r2-contracts | `/*/*/(CustodyHelperContractTests*)\|(DockerStackContractTests*)\|(DockerStackDocumentationTests*)/*` | S2 packaging | 0 failed, executed at least 164 | 164 | 6 | true |
| CP-6 | r2 | CP-4 | r2-linuxonly | `/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` | policy census | 0 failed, executed 2 | 2 | 6 | true |
| CP-7 | r2 | CP-4 | r2-livelane | `/*/*/LiveLaneRunnerTests/WrapperOnlyPaidManifest` | live-lane pin | 0 failed, executed 1 | 1 | 6 | true |
