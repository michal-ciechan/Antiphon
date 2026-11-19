# CARD-2004 A-1 scratch-repo caller checkpoints

`DelegationScratchRepo` copies `scripts/delegate.ps1` into a throwaway git repo for the headed canaries `DelegationPipelineE2ETests` and `DelegationSequencingE2ETests`. That script dot-sources `scripts/lib/antiphon-caller.ps1` from its own directory. The lone copy exited 1 with the helper path not recognized, before any HTTP call.

The scratch repo now copies each `lib/` file the script dot-sources (`(Join-Path $PSScriptRoot 'lib/...')`). No other test copies `delegate.ps1`, `card.ps1`, `status-snapshot.ps1`, or `checkpoint-task.ps1` into a temp or scratch directory. The other hits run the checkout copy, which already has `scripts/lib` beside it.

`DelegationScratchCallerTests` builds that scratch repo and runs the copied `delegate.ps1 -Status` against a loopback stub. The stub must record `X-Antiphon-Caller: delegate.ps1` and the script must exit 0. No browser and no headed run. Windows is unobserved. `WindowsUnitPlatformAdmissionTests` is not a row: `C1210_Windows_linux_only_outcomes_are_exact` skips on Linux, and a skip is not a clean receipt.

No migration. No positive-control row. Mutation execution is waived (operator 2026-11-12) and is not discharged. Short tests only (operator 2026-11-18).

The same filter then passed once, unlisted, on the helper-copy commit `e1b4074ce49103edaebdc3e6eb15c993e1874520` (build slot `c2004-scratch-green-build` lease `988bb64b-5fa6-4584-9cec-d6cf411c6687` waited=15s, run slot `c2004-scratch-green-run` lease `b5b3f243-3500-47ff-9684-fde6d268de13` waited=0s; load 14.23 then 15.39): executed 1, failed 0, passed 1, skipped 0. The red witness ran once, unlisted, at `6e2f55605637c9f3253e5e5a9a006612842750ae` (filter `/*/*/DelegationScratchCallerTests/*`, build slot `c2004-scratch-red-build` lease `cf2bb3cb-0760-4abc-9124-698e2086dbe3` waited=0s, run slot `c2004-scratch-red-run` lease `3dd586a6-ca7e-496e-b64e-802f528ce24b` waited=0s). Load was 12.04 when the build started and 13.43 when the run started. Result: executed 1, failed 1, passed 0, skipped 0. Stderr: `scripts/lib/antiphon-caller.ps1` is not recognized. This table is the green closed list.

## Verification design

- V-1. `Copied_delegate_loads_the_caller_helper_before_the_server_call`: the scratch copy of `delegate.ps1 -Status` reaches the loopback stub with `X-Antiphon-Caller: delegate.ps1` and exits 0.
- V-2. `RegisteredCallerScriptTests`: the checkout copies of the four scripts still send their registered labels, and an unregistered label is refused.
- R-1. Policy sweep.
- R-2. Guard, tripwire, native Linux, mechanical contract, platform guidance, layering, and lane. `WindowsUnitPlatformAdmissionTests` stays off this Linux receipt. The floor is 34: `TaskPlatformGuidanceTests` has five tests. `RunnerDefaultGuidanceTests` shares that file and is outside this filter.
- R-3. Slow-registry merge.
- R-4. Derived-impact index, including the new witness class on the scratch-repo edge.
- R-5. Land graph, evidence diff, pin digest, and merge-tree.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.E2E -> bin-c2004scr-a/ | c2004scr-witness | `/*/*/DelegationScratchCallerTests/*` | V-1 | 0 failed, 0 skipped, executed 1 | 1 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | S1 | tests/Antiphon.Tests -> bin-c2004scr-b/ | c2004scr-caller | `/*/*/RegisteredCallerScriptTests/*` | V-2 | 0 failed, 0 skipped, executed 5 | 5 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | S1 | CP-2 | c2004scr-policy | `/*/*/TestClassificationPolicyTests/*` | R-1 | 0 failed, 0 skipped, executed at least 42 | 42 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | S1 | CP-2 | c2004scr-guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | 0 failed, 0 skipped, executed at least 34 | 34 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | S1 | CP-2 | c2004scr-slow | `/*/*/SlowTestRegistryMergeTests/*` | R-3 | 0 failed, 0 skipped, executed 5 | 5 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | S1 | CP-2 | c2004scr-index | `/*/*/DerivedImpactIndexTests/*` | R-4 | 0 failed, 0 skipped, executed at least 27 | 27 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | S1 | n/a | c2004scr-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-5 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | S1 | n/a | c2004scr-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 5f0804e425384f8b03f88b833fbdad0e93f966d3 -HeadRef HEAD` | R-5 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | S1 | n/a | c2004scr-pins | `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` | R-5 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | S1 | n/a | c2004scr-merge | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-5 | exit 0, no conflict | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 81 minutes (sum of EstimatedMinutes). Derived total timeout is 172 minutes.
No row's `3 * EstimatedMinutes` exceeds the 45-minute advisory. CP-1, CP-2, and CP-6 deadlines are 45 minutes.
