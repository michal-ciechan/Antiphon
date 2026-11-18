# CARD-1893 S1 checkpoints (with CARD-1985)

Final verification for the S1 slice: the standing server2 runner's durable host-load history
(`HistoryPath: /state/host-load` plus the literal binding identity), the temp `HistoryEnabled: "false"`
override, `state-init`'s private `/runner-state/host-load`, the section 7.0 hold script
`scripts/c1893-runner-hold.sh`, and the CARD-1985 retirement of the S0 base `HistoryEnabled` line, its
witness `C1893S0RunnerHoldTests`, and the c1906/c346 fixture allowance.
One serial run on server2, no `--row-timeout`. CP-1 is the cold `Antiphon.Tests` build.

Deviations from the S1/S2 TestDesign (`2026-11-17-card-1893-s1-s2-testdesign.md`):
- The hold fixtures borrow the checkout's object store through `objects/info/alternates` instead of a
  depth-1 fetch of `9cf3e4917`. Same objects, no network, ~50 ms per method instead of 3-8 s.
- CARD-1985 adds four methods to `DockerStackContractTests`: `C1893_S1_Standing_runner_does_not_disable_history`
  (A-1a), `C1893_S1_Hold_stays_when_the_base_still_disables_history` (A-1c),
  `C1893_S1_Hold_keeps_the_s0_release_only_while_temp_leaves_history_alone` (keeps the S0 clause of
  section 7.0 releasable for S0-only rc SHAs; A-2), and `C1893_S1_Hold_exits_nonzero_when_the_collector_object_is_absent` (G-21's
  second half). The unresolved-argument method asserts the script's own exit 2, because a missing
  script (127) also exits non-zero with empty stdout and was green at base.
- `C1893S0RunnerHoldTests` is deleted (A-1b, A-3).
- The c1906/c346 base-equivalence fixtures drop the S0 allowance; they now assert that the base has no
  `HistoryEnabled` and pin the five S1 keys before comparing with the historical render.

Deferred to Merge/nightly (slow derived readers of the compose files and `init-state.sh`, per the
TestDesign "Existing tests affected"): `RemoteScriptContractTests`, `HostJqPrerequisiteScriptTests`,
`Rolling*`, `VolumeRetirement*`, `C0824BranchDeletionRecycleAuditTests`, `C1008*`, `C1364BoundLandWaitProofTests`,
`CheckpointImportTests`, `RetiredTempContainer*`, and the SessionRunner.Tests readers
`CustodyNamespaceBootstrapTests`/`HostStatsProbeParseTests`. None is marked passed here.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1893s1/ | s1-contract | `/*/*/DockerStackContractTests/C1893_S1_*` | V-1 | all listed, 0 failed | 15 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | S1 | CP-1 | s1-docs | `/*/*/DockerStackDocumentationTests/C1893_S1_Docs_name_the_hold_and_the_durable_path` | V-1 | all listed, 0 failed | 1 | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | S1 | CP-1 | s1-green | `/*/*/(DockerStackContractTests*)\|(DockerStackDocumentationTests*)/*` | V-1, R-1 | all listed, 0 failed | 152 | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | S1 | CP-1 | s1-compose-readers | `/*/*/(CodexRunnerImageContractTests*)\|(DindRunnerContractTests*)\|(Server2StagingServerContractTests*)\|(VerifyDindRunnerScriptTests*)/*` | R-1 | all listed, 0 failed | 60 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | S1 | CP-1 | s1-c1906 | `/*/*/RunnerLinuxCompilerPolicyTests/Saved_values_render_and_survive_each_named_phase_independently` | V-1 | all listed, 0 failed | 1 | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | S1 | n/a | s1-c346 | `node scripts/fixtures/c346-workspace-slot-compose.mjs` | V-1 | exit 0, PASS matrices=16 base-equivalence=1 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | S1 | CP-1 | s1-spawn | `/*/*/ProcessSpawnLimitTests/*` | R-1 | all listed, 0 failed | 30 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | S1 | n/a | s1-driver | `ANTIPHON_LINUX_COMPILER_POLICY=0 dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-1} --nologo` | R-2 | exit 0, native output | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | S1 | CP-1 | s1-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-2 | all listed, 0 failed; C1210_Windows_linux_only_outcomes_are_exact skips on Linux | 90 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

`Min` for CP-3 is 117 + 15 S1 contract methods + 19 + 1 S1 doc method = 152 (the S0 class is
deleted, so nothing else changes). CP-9 expects the one documented Linux skip; if the receipt
refuses on `skipped>0`, CP-9 is reported from its own run with the skip named.

### Verification

- V-1: the S1 witnesses (CP-1, CP-2) were committed red at `58af1549588c958df23af6642fb20110593f5468`
  and `f85780c0d92755016e0a2e531568e76aa7e1526f` and failed at their named assertions (missing compose
  lines, missing init entry, missing doc section, missing script exit 127); the c1906/c346 fixtures failed
  on `CARD-1985: the S0 HistoryEnabled stop-gap is retired`. Green at the S1 production commit.
- R-1: existing DockerStack, compose-reader and spawn-limit pins stay green.
- R-2: the guard union stays green; `tests/derived-impact-index.json` is regenerated with the driver
  (two runs, the second `unchanged`) and committed last only when the first run printed `changed`.

PCs (pending for method-scoped SourceLanding Mutation; mutation is waived by the operator 2026-11-12):
TestDesign PC-1, PC-2, PC-13 to PC-16, PC-19 to PC-21, plus PC-22 (drop the `! has "$base" ...HistoryEnabled:`
clause: `C1893_S1_Hold_stays_when_the_base_still_disables_history` red), PC-23 (drop the temp
`History` clause of the S0 branch: `C1893_S1_Hold_keeps_the_s0_release_only_while_temp_leaves_history_alone`
red), PC-24 (restore the S0 base line: `C1893_S1_Standing_runner_does_not_disable_history` red).
