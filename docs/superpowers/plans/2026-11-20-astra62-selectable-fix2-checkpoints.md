# Astra 6.2 selectable fix round 3 checkpoints

Code task `5361412b`. Subject tip `e00891f6961b10a4cbda2bb9a03f60faf45b0df9`. Red witnesses `070af154c1964a0aabc55208b1465f29732ef862`. Green fix `e0c12dcc71a2f2f5a55b86873363a60eaf03c172`.

## Verification design

R2-D-1: in the pinned pool branch of `TryReuseWarmAgentAsync`, the alias decline sits after the busy check, beside `ClaudeEffortMatchesPool`. A Running pinned pool delegate returns `WaitForAgent`. A free pin returns `SpawnFresh` when the claimed task has an alias or `PoolDelegateRelease.WithRequestedAlias` finds any task on that row. The shop and the desktop warm query call that same helper.

- V-1: `a_busy_pinned_alias_task_waits_for_its_pool_delegate` returns `WaitForAgent` and leaves the Running session in place. `a_ladder_task_pinned_to_alias_history_spawns_fresh` returns `SpawnFresh` for a Frontier Review pinned to an Idle row whose earlier task stored `gpt-6.2-astra`, and does not take that session.
- R-1: `AgentTaskPoolTests` (52 before these two witnesses, 54 after), `CodexDelegateDispatchTests` (30), and `MatrixDefaultComplexityCreateTests` + `ComplexityDispatcherTests` + `HeldStandingAgentChainTests` (59).
- R-2: the nine-class Linux guard batch. `C1729LayeringGuardTests` prints `VIOLATIONS 0 LISTED 162`. `WindowsUnitPlatformAdmissionTests` is not a row. On Linux its skip is not a pass. Windows behaviour of this change is unobserved.
- R-3: `DerivedImpactIndexTests` alone. The driver project is built before this table, through `scripts/build-slot.ps1`, because `regenerate_script_reports_changed_then_unchanged` runs `scripts/regenerate-derived-impact-index.ps1 -NoBuild`. Building `tests/Antiphon.Tests` does not produce that driver. That build is unlisted for that reason. The index commit, when the script prints changed, is the last commit before this run.
- R-4: `scripts/check-evidence-diff.ps1` over `e00891f6961b10a4cbda2bb9a03f60faf45b0df9..HEAD` exits 0.
- R-5: on this tree, `-PrintInputDigest` equals the stated `PinnedInputDigest` `c0d2a723edfe8f12b63f7eafa7ed360c6a8bc132377c1705d2fa9dd9dce6cd84`, and `Read-NightlyExecutionPolicy` equals stated `policyHash` `8c376b03c4e012e3700de15e8b99bdbe851187bce4d3c5defe2b6b6df1f782e2`. No pinned input was edited, so this branch does not reseal. `origin/master` `c124449e3b7d3c1ba0508edfb9a3c2274356f427` states the same policy hash and digest `15137e48291f1b0c70668a7e967965ed1391f64812cddea271387d48ce1a9365`.

The red confirmation at `070af154c` is not a row. It ran before the fix. The closed table is the green run.

Not in this closed list, under the short-test bound (operator 2026-11-18): alias display classes, the ladder table, the migration, and the land-graph audit. Those rows were closed on the parent fix. No positive controls. Mutation is waived (operator 2026-11-12). PCs stay pending for SourceLanding Mutation; this plan has none to execute.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-astra62fix2/` | witnesses | `/*/*/AgentTaskPoolTests/(a_busy_pinned_alias_task_waits_for_its_pool_delegate*)\|(a_ladder_task_pinned_to_alias_history_spawns_fresh*)` | V-1 | all listed, 0 failed | 2 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | all | CP-1 | pool | `/*/*/AgentTaskPoolTests/*` | R-1 | all listed, 0 failed | 54 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | all | CP-1 | dispatch | `/*/*/CodexDelegateDispatchTests/*` | R-1 | all listed, 0 failed | 30 | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | all | CP-1 | matrix | `/*/*/(MatrixDefaultComplexityCreateTests*)\|(ComplexityDispatcherTests*)\|(HeldStandingAgentChainTests*)/*` | R-1 | all listed, 0 failed | 59 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | all | CP-1 | linux-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | all listed, 0 failed | 81 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | all | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-3 | all listed, 0 failed | 27 | 12 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef e00891f6961b10a4cbda2bb9a03f60faf45b0df9 -HeadRef HEAD` | R-4 | exit 0 | n/a | 3 | true | n/a |
| CP-8 | all | n/a | pins | `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest > /tmp/astra62fix2-pin.txt && computed=$(tr -d '[:space:]' < /tmp/astra62fix2-pin.txt) && test "$computed" = c0d2a723edfe8f12b63f7eafa7ed360c6a8bc132377c1705d2fa9dd9dce6cd84 && rm /tmp/astra62fix2-pin.txt && pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; if ($p.Hash -ne "8c376b03c4e012e3700de15e8b99bdbe851187bce4d3c5defe2b6b6df1f782e2") { exit 1 }' && echo pin-ok` | R-5 | exit 0; stated digest and policy hash equal computed | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
