# DW-1 checkpoints (Code task 7222353c)

`New-WindowsPhoneHomeRunnerEnv` sets `SessionRunner__HostStats__HistoryEnabled`
to `false`. Phase 1 (`scripts/start-windows-phone-home-runner.ps1`) and phase 2
(`Resolve-SessionRunnerAutostartPlan`) both return that map, so neither plan
output writes a second host-load history stream. This slice does not set
`HistoryPath` or the binding keys. AppHost launch-refusal text and the
fail-closed autostart paths stay byte-identical.

Base: `c93364585afe99a99c770d23ec8ff7d2dc23bfa0`. Branch
`feat/card-task-7222353c`. No migration. Restart none. No `-Runner` and no
`-Platform`. Mutation execution is waived (operator 2026-11-12) and is not
discharged. No PC row is in the DW-1 plan. A mutant that drops the
`HistoryEnabled` assignment stays pending for SourceLanding Mutation.

## Verification design

- V-1. `Invoke-EnvTable` requires `SessionRunner__HostStats__HistoryEnabled=false`
  in key order and value. At the DW-1-red commit the helper does not set it, so
  `env-table.keys` and `env-table.values` fail. The class has 10 `[Test]`
  methods and no `[Arguments]`: 1 failed, 9 passed, executed 10.
- V-2. After the helper line, the same class is green. The phase-2 autostart
  plan (`desktop-win`, port 17204, config path not explicit) carries the same
  value. The phase-1 map is the direct helper call (port 17214).
- R-1. Nine-class guard union, parenthesized, floor 85: policy 42, guard 1,
  merge 5, tripwire 2, native Linux 3, mechanical 11, TaskPlatformGuidance 9,
  C1729 11, lane 1. C1729 inventory stays `VIOLATIONS 0 LISTED 162`.
- R-2. `WindowsUnitPlatformAdmissionTests`. `C1210_Windows_linux_only_outcomes_are_exact`
  skips on Linux. That skip is not a pass. Floor 6 executed.
- R-3. `DerivedImpactIndexTests` alone, floor 27. The regenerate driver is
  built into the worktree default output before this row, because the test
  calls `scripts/regenerate-derived-impact-index.ps1 -NoBuild`.
- R-4. `scripts/audit-land-verification-graph.ps1 -Check`, nightly pin
  recompute (`-PrintInputDigest` plus `Read-NightlyExecutionPolicy`), 
  `scripts/check-evidence-diff.ps1` over the task base..HEAD, and
  `git merge-tree --write-tree origin/master HEAD`. No `-Update`.

`WindowsUnitPlatformAdmissionTests` is its own row so the skip does not enter
the zero-skip rows. Validate the green receipt with `--rows` that omit CP-4
when the full-report validator rejects that rostered skip.

### Checkpoints

Run through the checkpoint tool. Pass committed HEAD as `--expected-source-sha`.
`--serial`. The tool leases its own slot. Do not wrap it in `build-slot.ps1`.
`ANTIPHON_LINUX_COMPILER_POLICY=0`. Admit a run only when the 1-minute load is
below 16. CP-1 runs only at the DW-1-red commit. `--after DW-1` is the green
group. First row `EstimatedMinutes` is 15. Estimates are provisional, not a
measured p95. No green repetition.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | DW-1-red | tests/Antiphon.Tests -> bin-dw1-red/ | dw1-red | `/*/*/C1744G3PhoneHomeSupervisionTests/*` | V-1 | 1 failed, 9 passed, executed 10 | 10 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | DW-1 | tests/Antiphon.Tests -> bin-dw1/ | dw1 | `/*/*/C1744G3PhoneHomeSupervisionTests/*` | V-1, V-2 | 0 failed, 0 skipped, executed 10 | 10 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | DW-1 | CP-2 | dw1-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-1 | 0 failed, 0 skipped, executed at least 85 | 85 | 12 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | DW-1 | CP-2 | dw1-windows | `/*/*/WindowsUnitPlatformAdmissionTests/*` | R-2 | 6 executed, 1 rostered Linux skip | 6 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | DW-1 | CP-2 | dw1-index | `/*/*/DerivedImpactIndexTests/*` | R-3 | 0 failed, 0 skipped, executed at least 27 | 27 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | DW-1 | n/a | dw1-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | DW-1 | n/a | dw1-pins | `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` | R-4 | exit 0 | n/a | 5 | true | n/a |
| CP-8 | DW-1 | n/a | dw1-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef c93364585afe99a99c770d23ec8ff7d2dc23bfa0 -HeadRef HEAD` | R-4 | exit 0 | n/a | 5 | true | n/a |
| CP-9 | DW-1 | n/a | dw1-merge | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-4 | exit 0, no conflict | n/a | 5 | true | n/a |
