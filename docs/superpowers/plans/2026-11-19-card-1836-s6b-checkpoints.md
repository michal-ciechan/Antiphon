# CARD-1836 S6b shadow run evidence

Code/landing owner 5563c349; base ef331572957c36548dffed10a6f68a026268193f.
Owner: [activation plan](2026-11-12-card-1836-incremental-activation-plan.md), S6b, D-2/D-6, V-12 and CP-12.
Ready note: [remaining chain](../../investigations/2026-11-19-card-1836-remaining-chain-status.md).

S6b records source and input snapshots, build output seals, one discovery per suite, and immutable open per-chunk revisions, including during an explicit Full bootstrap. Recording grants no gate credit. `effectiveMode` stays Full. `ANTIPHON_RC_RUN_EVIDENCE=off` writes nothing. Carry cannot skip discovery. An interrupted writer leaves the previous open revision and marks the remainder not-run. A closed or credited pointer is refused. Native custody is unchanged.

## Verification design

V-12: all four RcRunEvidenceTests methods: BuildSourceAndLaunchOutputMustJoin; ChangedInputsDuringRunRefuse; InterruptedWriterLeavesNotRun; OneBuildAndDiscoveryPerSuite. The build receipt covers every staged dll, deps.json and runtimeconfig.json, not only the entry DLL. A launch from another directory with `--no-build` is foreign even when the bytes match. Changed inputs refuse. The off switch stays Full and writes nothing.

R-4: RcFullTimingContractTests, NightlyCoreTests, LiveLaneRunnerTests. No migrations, selector mode, tag, publish or promote path. Incremental is not added to a mode list. Get-ReleaseGateCreditVerdict remains the only credit writer.

PC-V12-join, PC-V12-inputs, PC-V12-interrupt and PC-V12-once remain pending for caller-commissioned SourceLanding Mutation. The operator waiver does not mark any PC passed.

## Performance budget

Provisional method 60s, child 45s plus 10s joined cleanup. The nightly core cache is warmed by the witness harness before CP-12, because the first load builds scripts/lib/Antiphon.Nightly. Serial rows, one build at a time. CP-12 EstimatedMinutes is 15. CP-90 keeps its own isolated output because After=all cannot reuse the S6b build. CP-91 and CP-92 are the impacted spawn-limit rosters in the other two test projects; they run serially and never overlap Antiphon.Tests. Min is the TUnit executed-test floor.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-12 | S6b | `tests/Antiphon.Tests -> bin-c1836-s6b/` | linux-run-evidence | `/*/*/(RcRunEvidenceTests*)\|(RcFullTimingContractTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)/*` | V-12 R-4 | all listed, 0 failed/skipped | 4 | 15 |
| CP-90 | all | `tests/Antiphon.Tests -> bin-c1836-s6b-guards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | policy, categories, layering, spawn census | all listed, 0 failed/skipped | 79 | 15 |
| CP-91 | S6b | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s6b-pty/` | linux-pty-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 4 |
| CP-92 | S6b | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s6b-runner/` | linux-runner-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 4 |

### S6b command contract and remaining native work

`scripts/lib/rc-run-evidence.ps1` is the PowerShell 5.1 entry. The compiled writer is `scripts/lib/Antiphon.Nightly/RcRunEvidence.cs`. Evidence lives outside the checkout under `revisions/<sequence>/`. The index is published only after the revision bytes are read back. `state` stays `open`, `gateCredit` stays false, and `bindingState` stays `diagnostic-only`. Fixture seams with `StartProcess` or `Git` set do not record unless `ANTIPHON_RC_RUN_EVIDENCE=record`. A non-strict failure returns unproven and does not change the Full exit.

Restart target for this task: none. `scripts/lib/**` is on the startup census, so a later rc pick owes the Windows startup probe. This task does not run that probe. S7d native launch and custody remain pending. Windows-only behavior is UNOBSERVED.
