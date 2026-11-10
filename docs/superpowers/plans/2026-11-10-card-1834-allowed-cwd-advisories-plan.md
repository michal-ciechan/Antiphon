# CARD-1834 AllowedCwd advisories

CARD-1834 arrived with no checkpoint table. This file exists so
`scripts/check-os-witnesses.ps1` can resolve checkpoint ids. It is not a
checkpoint-tool manifest. Do not run `tools/Antiphon.Checkpoints` for CP-1.
Ordinary proof is the leased `StandingRunnerCwdTests` run and the nine-class
guard batch already recorded on the Code task.

No new `OperatingSystem` branch was added. The two `OperatingSystem.IsWindows()`
calls in `PhoneHomeCommandDispatcher` are the pre-existing admission gates.
They shifted down four lines when the standing-link recheck was inserted, and
the call text is unchanged. The Windows leaf rule is the existing `windows`
flag inside `RunnerWorkspaceAdmission`, forced by
`Windows_mixed_case_standing_leaf_is_refused`. A-4, the native Windows junction
and `C:/` refusal, stays pending and is not a passed skip.

## Verification design

### Checkpoints

| ID | Slice | Build | Filter | Command | Expected | Result | Slot | Minutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | os-witness | n/a | n/a | not a checkpoint-tool row; OS-witness id anchor only | checker exit 0 | recorded by Code outside the checkpoint tool | n/a | n/a |
