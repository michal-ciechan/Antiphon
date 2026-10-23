# CARD-1510: installed PowerShell for the PATH probe

Owner: Code `5019ea31-13ce-48dd-bdc7-4017c6529e1f`, branch
`feat/card-task-5019ea31`, base `ef121b23f3d78bbeb31e09886dcf8448e4a633e5`.
Fixture/test-only. Preserve every existing probe assertion and deadline.
The group-5 Debug report identifies a WindowsApps launcher prefix ahead of the
otherwise exact bounded PATH. Reuse `ScriptHarnessProcessFixture.ResolveInstalledPowerShell`;
do not discount another child PATH prefix. That existing helper is shared by the
Windows script fixtures. The CARD-1515/1516 OwnedPty sources do not contain a
separate installed-PowerShell resolver at the assigned master or their pushed tip.

S1: extract the existing WindowsApps path predicate and probe executable selection
without changing behavior, add portable regression tests, commit/push, run red.
S2: make the probe's Windows branch use the shared installed resolver; reject both
separator spellings of a WindowsApps component and assert the chosen path is not
an alias. Keep Linux launch behavior. Commit evidence/candidate before final green.

## Verification design

| ID | Method | Required witness |
|---|---|---|
| V-1 / R-1 | `RunnerProbePowerShellFixtureTests.WindowsApps_component_is_classified_independently_of_host` | Windows-shaped backslash, slash, mixed-case/mixed-separator aliases; real install and near-component negatives. Red is an actual classifier mismatch, not a constant/self-comparison. |
| V-2 / R-2 | `RunnerProbePowerShellFixtureTests.Probe_selects_regular_install_or_refuses_absence` | Real temporary installed file and earlier alias; alias-only, missing pwsh on nonempty PATH, empty PATH, and real symlink/reparse-point before regular install. The actual FileAttributes value witnesses rejection, not a hand-coded platform type. Red must be wrong selected executable or missing refusal. No host PATH mutation. |
| V-3 / R-3 | `RunnerProcessProbeTests.Inherited_path_over_the_ceiling_is_bounded_on_an_entry_boundary_and_the_probe_runs` | Native Windows original group-5 method: successful probe, exact ordered entries, 4000-character bound and only existing PSHOME adjustment; selected executable asserted alias-free. Red/green OWED Windows, never counted as Linux coverage. |
| R-4 | `RunnerProcessProbeTests.Inherited_path_over_the_ceiling_drops_relative_entries` | Adjacent real Linux probe keeps original relative-entry refusal and bounded PATH assertions. |

The explicit brief overrides generic Final scope: no Unit lane, full affected
classes or whole-suite run. CP-5/6 are OWED to a separately commissioned Windows
lane, not runnable against the protected desktop qualification (PID 209072) or
release directories. No Final/Full native claim. No repeats after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1510-red/` | classify-red | `/*/*/RunnerProbePowerShellFixtureTests/WindowsApps_component_is_classified_independently_of_host` | V-1, R-1 | 10 executed, expected classification assertion failures, 0 skipped | 10 | 5 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1510-green/` | classify-green | `/*/*/RunnerProbePowerShellFixtureTests/WindowsApps_component_is_classified_independently_of_host` | V-1, R-1 | 10 passed, 0 failed/skipped | 10 | 5 |
| CP-3 | S1 | CP-1 | select-red | `/*/*/RunnerProbePowerShellFixtureTests/Probe_selects_regular_install_or_refuses_absence` | V-2, R-2 | 5 executed, expected selection/refusal assertion failures, 0 skipped | 5 | 1 |
| CP-4 | S2 | CP-2 | select-green | `/*/*/RunnerProbePowerShellFixtureTests/Probe_selects_regular_install_or_refuses_absence` | V-2, R-2 | 5 passed, 0 failed/skipped | 5 | 1 |
| CP-5 | S1 | `tests/Antiphon.Tests -> bin-c1510-windows-red/` | windows-owed-red | `/*/*/RunnerProcessProbeTests/Inherited_path_over_the_ceiling_is_bounded_on_an_entry_boundary_and_the_probe_runs` | V-3, R-3 | 1 executed, alias-prefix assertion red on affected Windows host, 0 skipped | 1 | 5 |
| CP-6 | S2 | `tests/Antiphon.Tests -> bin-c1510-windows-green/` | windows-owed-green | `/*/*/RunnerProcessProbeTests/Inherited_path_over_the_ceiling_is_bounded_on_an_entry_boundary_and_the_probe_runs` | V-3, R-3 | 1 passed, 0 failed/skipped | 1 | 5 |
| CP-7 | S2 | CP-2 | relative-smoke | `/*/*/RunnerProcessProbeTests/Inherited_path_over_the_ceiling_drops_relative_entries` | R-4 | 1 passed, 0 failed/skipped | 1 | 1 |

Run checkpoint tool with `--rows CP-1,CP-3` for S1 and
`--rows CP-2,CP-4,CP-7` for S2; exact committed `--expected-source-sha`,
`--serial --row-timeout 10m --total-timeout 15m --max-wait 50s`.
Wait to terminal status; validate fresh TRX and receipts. The tool bootstrap is
one explained infrastructure build through build-slot.ps1, isolated output.
Portable estimated cost 13 minutes plus authoring; native rows 10 minutes owed.

### Mutation obligations

PC-1 (backslash/slash/case/component-boundary variants): bypass alias classification.
PC-2 (alias-first, alias-only, no-pwsh, empty-path, reparse-first variants): restore
alias/bare-name fallback or accept the reparse point. All PENDING SourceLanding
Mutation, method-scoped; deliberate mutants and missing-control discovery belong
there. No ordinary green discharges a PC.

Restart: none. Next: Review with Windows gate incomplete; original Code task owns landing.
