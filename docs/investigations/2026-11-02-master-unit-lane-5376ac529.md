# Master Unit lane backstop at 5376ac529 (task a8bd6706, 2026-11-02)

Verdict: **no real regression from the master-only lands.** Every red in the Antiphon.Tests Unit lane,
SessionRunner.Tests and the client lane also fails at the older master 3a49b6013, or passes when run
alone. Antiphon.Agents.Pty.Tests is green.

- Subject: master `5376ac529987b9e7b2019773cb4d784cba8007bf`. Base for comparison: `3a49b60133327f4441f06698ef3369b1d277d049`, in a detached scratch worktree.
- Host: server2 (Linux, 24 cores), load average 8 to 56 during the run. Builds were in the native default output with `-p:UseAppHost=false`. Every build and run had its own `scripts/build-slot.ps1` lease.
- Runs used `dotnet run --project tests/<P> --no-build -p:UseAppHost=false -- --treenode-filter ... --timeout 45m --report-trx`.
- Evidence (gitignored, runner mirror): `.antiphon/a8bd-unit/*.log|*.trx`, and the TRX union at `.antiphon/a8bd-trx-unit/`.

## Deviation from the brief

The brief asked for 4 to 6 sequential shards. Earlier measurements show that a sequential lane cannot fit
a 150-minute budget. `RemoteScriptContractTests` alone takes about 67 minutes serial, and Rolling* and
Ch* each take 25 to 45 minutes. I ran 3 concurrent lanes of sequential shards, 19 shards in all, each with
its own lease and a 45m timeout. A fourth lane re-ran a capped shard. Pty.Tests ran alone, after every
Antiphon.Tests shard had finished.

Two shards hit the 45m cap with no failures: `rolling` (87 done, 6 failed) and `misc` (786 done, 0
failed). Neither counts as a pass. Both were re-run in smaller shards. Wall time was about 3h20m, past the
150-minute budget. Slot waits accounted for 0 to 12 minutes per shard.

## Completeness

A System.Reflection.Metadata census of `Antiphon.Tests.dll` found 4,108 `[Test]` methods with
`Category("Unit")` on the class or the method. The union of the 20 shard TRX files covers 4,107 of
them, with no extras. The one missing method is `LiveLaneIsolationTests.StartupArgvMatchesProductionApplyOff`,
which is `[Explicit]`. The union has 6,514 results: 6,449 passed, 21 failed, 44 skipped.

## Per-shard counts (Antiphon.Tests, `[Category=Unit]`)

| Shard | Filter (class segment) | Executed | Passed | Failed | Skipped | Test duration |
|---|---|---|---|---|---|---|
| rsc-c110 | RemoteScriptContractTests / C110* | 217 | 217 | 0 | 0 | 16m |
| rsc-c116-9 | RSC / C116,C118,C119 | 50 | 50 | 0 | 0 | 7m |
| rsc-c10 | RSC / C099,C10* | 31 | 31 | 0 | 0 | 34m |
| rsc-c12-13 | RSC / C12*,C13* | 83 | 83 | 0 | 0 | 2m |
| rsc-rest | RSC / non-C1 | 130 | 130 | 0 | 0 | 7m |
| ch | Ch* | 577 | 576 | 0 | 1 | 32m |
| ret | Ret* | 38 | 38 | 0 | 0 | 17m |
| r-other | other R (23 prefixes) | 501 | 501 | 0 | 0 | 11m |
| adw | A*,D*,W* | 1388 | 1378 | 0 | 10 | 2m |
| rolling | Roll* | 87 (ABORTED at 45m, no TRX) | 81 | 6 | 0 | 45m |
| rolling2-mount-census | RollingProductionMount, RollingVolumeRecycleCensusChurn | 13 | 7 | 6 | 0 | 1m |
| rolling2-script-c1008 | RollingVolumeRecycleScriptTests / C1008* | 19 | 19 | 0 | 0 | 26m |
| rolling2-script-rest | RollingVolumeRecycleScriptTests / C1087,C13*,C1594 | 58 | 58 | 0 | 0 | 14m |
| c-other | C0,C1,C4,C5,C9,Ca,Cl,Co,Cr,Cu | 386 | 381 | 2 | 3 | 10m |
| st | S*,T* | 775 | 767 | 1 | 7 | 4m |
| op | O*,P* | 788 | 780 | 0 | 8 | 6m |
| ghi | G*,H*,I* | 654 | 630 | 12 | 12 | 6m |
| misc | B,E,F,J,K,L,M,N,Q,U,V,Z | 786 (ABORTED at 45m, no TRX) | 783 | 0 | 3 | 45m |
| misc-a | misc minus VolumeRetirement* | 752 | 749 | 0 | 3 | 11m |
| misc-v1 | VolumeRetirementApplyGate, Content | 28 | 28 | 0 | 0 | 13m |
| misc-v2 | VolumeRetirementHostProof | 9 | 9 | 0 | 0 | 1m |
| misc-v3 | VolumeRetirementReceipt, Wrapper, D*, F* | 17 | 17 | 0 | 0 | 30m |

Other lanes:

| Lane | Executed | Passed | Failed | Skipped | Duration |
|---|---|---|---|---|---|
| Antiphon.SessionRunner.Tests (whole) | 1888 | 1661 | 145 | 82 | 10m |
| Antiphon.Agents.Pty.Tests (whole, run alone) | 755 | 522 | 0 | 233 | 49s |
| Client (`scripts/test-client.ps1`, lint+typecheck+vitest) | 1841 | 1838 | 2 | 1 | 332s vitest |

## Real regressions

None.

## Failure table

| # | Test | Assertion | Class | Evidence |
|---|---|---|---|---|
| 1-6 | `Scripts.RollingProductionMountTests`: C1105_Previous_generation_roster_is_derived_from_its_compose, C994_Secrets_match_read_only_files, C994_Named_volume_identity_and_topology_are_exact, C994_Compose_model_preserves_service_specific_mounts, C994_Tmpfs_configuration_survives_exit, C994_Bind_sources_are_canonical_and_typed | `actual[key].Ok should be True but was False`, `antiphon-runner-temp:valid diagnosis=RecycleContainerStateUnknown` (RollingProductionMountTests.cs:95). `c1008_owned_mounts` runs `sudo -n readlink/stat`, and server2 has no sudo | (d) pre-existing; host-dependent | Same 6/12 red at 3a49b6013. `scripts/c590-remote.sh` and the test are unchanged in 3a49b6013..5376ac529 |
| 7 | `Scripts.C1364BoundLandWaitProofTests.C1364_Host_recycle_admission_reports_waited` | `seed.Exit should be 77 but was 2`; `DIAGNOSIS=RecycleGitAuditUnknown reason=preview-required` (C1364BoundLandWaitProofTests.cs:346) | (d) pre-existing | Red alone at the tip and red at 3a49b6013, same assertion |
| 8 | `Scripts.C1364BoundLandWaitProofTests.C1364_Host_recycle_post_removal_reports_waited` | `boundary["record"]["phase"] should be "containersRemoved" but was "preflight"` | (d) pre-existing | Red alone at the tip and red at 3a49b6013 |
| 9 | `Checkpoints.TimeoutTests.total_deadline_kills_running_and_skips_queued` | rows `["skipped","skipped"]`, expected `["execution-budget-exceeded","skipped"]` (TimeoutTests.cs:135) | (b) load flake | Passes alone at the tip (1/1). Known 200 ms wall-clock TotalTimeout flake |
| 10-21 | `Scripts.HostJqPrerequisiteScriptTests` (12): C1025_Transport_is_bounded_and_reaped, _Receipt_persistence_is_required, _Check_rejects_canonical_leaf_symlink, _Receipt_rejects_canonical_lookup_with_unapproved_target, _Check_has_no_install_effects, _Provision_requires_canonical_source, _Receipt_requires_complete_current_proof, _Explicit_tool_phases_never_deploy, _Transport_preserves_secret_custody, _Check_qualifies_deployment_shell, C1058_Check_rejects_canonical_hardlink, C1058_Changed_leaf_cannot_emit_success_proof | Exit 1, expected 0/2: `deploy-server2.ps1: The term '/tmp/c1025-*/scripts/c1609-host-proof.ps1' is not recognized`. The fixture (HostJqPrerequisiteScriptTests.cs:1282) copies `c1008-host-proof.ps1` but not the `c1609-host-proof.ps1` that it dot-sources | (d) pre-existing fixture defect (not a master-only land) | The identical 12/39 are red at 3a49b6013. The dot-source came in with 85776c1c8, an ancestor of 3a49b6013 |
| SR | `Antiphon.SessionRunner.Tests`: 145 failures in 22 classes (RunnerRestartHealthTests 55, RunnerRestartPreflightSafetyTests 26, CodexWindowsLaunchPolicyTests 19, RunnerRestartScriptCompatibilityTests 8, CodexCliVersionWindowsTests 4, DaemonLogRotationTests 4, CompactionContinuationStopTests 4, PtyHostAdoptionTests 4, HerdrGkpLaunchGuardTests 3, SessionCpuWatchdogTests 3, and 12 more with 1-2 each) | Mostly `pwsh.exe`/`cmd.exe` start failures (Win32Exception), `OperatingSystem.IsWindows() should be True`, and Windows launch-policy `should throw` | (a) Windows-dependent and (d) pre-existing | Whole project at 3a49b6013: 145 failed, 1583 passed, 82 skipped of 1810. No failing name at the tip is absent at base. Includes the known CARD-1650 watchdog/compaction seven (CompactionContinuationStopTests x4, SessionCpuWatchdogTests x3) and the cmd.exe `TranscriptAdoptionSafetyTests.ToDto_reports_unbound_while_locating_exact_after_bind_and_sidecar_after_readopt` |
| CL | client `SessionTerminal.mouse.test.tsx` > CARD-0148 mouse policy > `protocol 'ANY' stays local` and `reset and instances remain independent` | `Test timed out in 20000ms` | (b)/(d) timing, pre-existing | File alone at the tip: 1/14 red (reset...). File alone at 3a49b6013: 1/14 red, same test. `client/src/features/board/` is unchanged in the range |

The other known Linux reds from the brief are outside the Antiphon.Tests Unit lane:

- `Agent_files_review_threads_and_content_contracts` (CARD-1648) is in E2E.
- `C508_GuardRefMismatchWarnedOnce` (AgentTaskDispatchBaseGuardTests) is not `Category("Unit")`.

They were not re-run.

## Recommended follow-ups (not regressions)

- HostJqPrerequisiteScriptTests fixture: copy `scripts/c1609-host-proof.ps1` beside `c1008-host-proof.ps1`. This is red on Linux since 85776c1c8, and Windows skips the class.
- RollingProductionMountTests and C1364BoundLandWaitProofTests are red on server2 at both commits (sudo/git-audit host dependence). These need a Debug to decide between fixture and NativeLinux policy.
- SessionRunner.Tests has about 135 Windows-only tests that do not skip themselves on Linux.
