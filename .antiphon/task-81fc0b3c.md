# CARD-1222 code report (task 81fc0b3c)

S1-S2 is complete. Off Windows, `ZombieCensus:Enabled` true fails startup with "Zombie census is a supported omission off Windows. Set ZombieCensus:Enabled to false." The WMI census registers only on Windows. The unsupported snapshot throws that sentence. Linux compose flags stay false. Desktop `server/appsettings.json` stays enabled. CP-1 and CP-2 are green at the fix SHA. Windows native registration and PC-1..PC-8 stay pending.

Branch `feat/card-task-81fc0b3c`. Worktree `/work/worktrees/task-81fc0b3c`. Code task `81fc0b3c-48ad-485e-a6ff-bba65c7ac770`.

- Red: `1bfcbf3e194ed68e5d72593845d1881e9a7131ca`. Unlisted class run before the fix, because a red checkpoint row fails the tool: 4 failed, 1 passed, 0 skipped.
- Fix and tested: `b30340f87315596aa2df0fdf23d335330b5f07ad`.
- `tests/test-execution-policy.json` is unchanged. No policyHash or PinnedInputDigest refresh.

## Checkpoints

Run `20261110-211424-2bfd`. Host Debian GNU/Linux 12 (bookworm), cores=24. Source clean. buildSource=verified. Verdict GREEN, exit 0. Evidence `/work/worktrees/task-81fc0b3c/.antiphon/checkpoints/20261110-211424-2bfd/report.md`. The tool deleted `bin-c1222/`. Touched classes are these two rows; they were not run a second time.

- CP-1 `/*/*/ZombieCensusPlatformTests*/*` executed=8 passed=8 failed=0 skipped=1. slot=granted waited=420s. build=ok. wall=430.787s. phases slotWait=795s build=1127.0120881s.
- CP-2 `/*/*/ProductionRunnerGuardTests*/*` executed=2 passed=2 failed=0 skipped=0. build=reused. waited=435s. wall=445.684s.
- BUDGET wall=2134.797s execution=1335.223s admissionOnly=799.574s. unlisted inside the tool: none. wall 35m35s.

## V / R

- V-1 `Enabled_true_off_windows_is_rejected` passed.
- V-2 `Enabled_false_off_windows_is_accepted` passed.
- V-3 `Linux_registration_is_not_the_wmi_census` passed.
- V-4 `Forced_non_windows_branch_registers_the_omission` passed.
- V-5 `Forced_windows_branch_registers_the_wmi_type` passed.
- V-6 `Omission_snapshot_throws_and_is_not_empty` passed.
- V-7 `Bootstrap_records_the_supported_omission` passed.
- V-8 `Docker_stack_records_the_supported_omission` passed.
- V-9 `Windows_registration_uses_the_wmi_census` skipped (`NotExecuted`). Owed on a Windows host.
- R-1 `Every_program_boot_in_this_assembly_is_pointed_at_a_dead_runner` passed. The class also passed `The_refusing_client_records_and_refuses_a_launch`.

## Guards

Build lease `c1ca3840-7896-4d39-a9dc-e51ac93d245f`, waited=0s, held=165s, `UseAppHost=false`, native output, 0 errors. Run lease `18776dfe-586a-43df-a8bb-e949cd0806b3`, waited=0s, held=103s, `--no-build` with the same property, `TEMP=/tmp`.

Filter (nine-class batch in `docs/testing-and-build.md`, which includes the brief's eight plus `TestLaneCategoryGuardTests`): `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/*`.

total 78, failed 0, succeeded 78, skipped 0.

## OS witnesses

`scripts/check-os-witnesses.ps1` exit 0. Declaration `.antiphon/os-witness-card-1222.json`. Base `84bc17576287fb43cf2dc04395970a8bc52e2d76`. Head the fix SHA.

- Linux validator and public registration: `nativeObligation` recorded against `.antiphon/checkpoints/20261110-211424-2bfd/rows/CP-1/run.trx`.
- Forced bool branches: `not-required`.
- Windows public registration: pending.
- `restartImpact.required` is true because `server/Program.cs` is an additional startup path. The plan said false. The checker disagreed, so the inventory was kept and the Windows native obligation stays pending. Restart: none.

The checker and `scripts/check-evidence-diff.ps1` are unlisted. Their SHAs are the S2 tip.

## Evidence and landability

Repository is not shallow.

- `ab28a6ca73be3e39cabc98cce3c78926367a5625..HEAD` commits=2 violations=0, exit 0.
- `84bc17576287fb43cf2dc04395970a8bc52e2d76..HEAD` commits=3 violations=0, exit 0.

`git fetch origin master` resolved `ef7afd44927206f6c03547670a5add84130bdce9`. `git merge-tree --write-tree origin/master HEAD` wrote `f465ea76babfe655d0fda3877d32a02811d7e600`, exit 0. The branch was not merged.

## Host flags

- `server/appsettings.json` keeps `ZombieCensus:Enabled` true.
- `docker-compose.yml:45`, `docker-compose.server2-server.yml:90`, and `docker-compose.server2-staging-server.yml:54` set `ZombieCensus__Enabled` false.
- `scripts/deploy-server2-server.ps1` and `scripts/deploy-server2-staging-server.ps1` pin that false string.
- No other `appsettings*.json` sets the key.

## Owed

Run `Windows_registration_uses_the_wmi_census` on a Windows host and confirm parameterless registration resolves `WindowsZombieProcessCensus` while desktop Enabled true still validates. PC-1 through PC-8 stay pending for method-scoped SourceLanding Mutation and were not executed.

Restart: none.
