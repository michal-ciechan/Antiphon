# CARD-1834 Code report

Full detail for task ef2fee3f. The caller message names the branch tip. Checker and merge-tree below were run at `51f5b934fe5954ba5af60097fc590d2b684d8788` before this file was committed. This file is the only change after that commit.

CARD-1834 is implemented and green on `feat/card-task-ef2fee3f`. Tested code is `8928f01711bb83963cf10830c7f04c2d4769f013`. Worktree `/work/worktrees/task-ef2fee3f`. Code task ef2fee3f. Restart none. Nothing deployed.

## Behaviour

A-1. `PhoneHomeSettings.Validate` refuses an AllowedCwd that is whitespace, a filesystem root, a `.` or `..` segment, or a lexical collapse to a filesystem root, with the existing text `PhoneHome:AllowedCwd must not be empty or a filesystem root.` `IsFilesystemRoot` is unchanged, so ChildCodexHome is unchanged. `/work`, `/work/agents`, and `C:/Antiphon/runner/work` still validate. `/.`, `/./`, `/../`, `/work/..`, `/./work`, `C:/.`, `C:\.`, `C:/Antiphon/..`, `C:\Antiphon\..`, `C:/./`, `C:/..`, and `//?/C:/` are refused. `\\?\C:\` is now refused with that same root message.

A-2. On the windows flag only, a standing leaf must equal its `ToLowerInvariant` form. Linux still admits `Foo` and `foo` as different directories. `/WORK/agents/foo` stays admitted on the windows flag. `/WORK/agents/Foo`, `Z`, `Slides-Server2`, and uppercase `COM10`/`COM`/`LPT`/`PRINTER` are refused on that flag. Lowercase `z`, `console`, `com10`, `com`, `lpt`, and `printer` stay admitted. Worktrees are not case-canonicalized. AgentService is unchanged.

A-3. `LaunchAsync` calls `RejectLinkedStandingCheckout` again after the capacity check and before the generation record, `RecordLaunch`, and `StartAsync`. A symlink swapped during the sign-in probe is refused and does not consume the generation.

Residual. A replacement after that second check and before the pty host chdir can still start. No O_NOFOLLOW branch was added.

A-4 stays pending on Windows: a junction at the leaf and at `agents` refused with a message containing `symlink`, a real directory left alone, and `C:/` plus `C:\` refused. This Linux run does not pass that witness.

## Evidence

Red `62f08cd78a0b84415416361fcf3e15652747c096`, lease `ba6ddb81-a12d-423e-adf1-7bde7d2afbb3` waited=0s held=4s: StandingRunnerCwdTests 24 total, 3 failed, 21 passed. The three new witnesses failed. Fix `85fcee97fd9bd07608b87eef10141f68138c0c21`. The class run at that SHA failed 1 (`/work/agents/Z` on windows was refused; the old assertion expected admitted), lease `420651d4-b588-476f-9f00-b53173371306` waited=0s held=3s. That row is not green. Follow-up `8928f01711bb83963cf10830c7f04c2d4769f013` keeps the refusal: the allow-list sample is `z`, and `Z` is admitted on Linux and refused on Windows. No new-witness assertion was weakened.

Green at `8928f01711bb83963cf10830c7f04c2d4769f013`. Build lease `6a35e502-55a0-4eec-985d-bf605d27d09a` waited=0s held=39s, UseAppHost=false, 0 errors. Class lease `31cc0d83-6a19-4958-a052-3f1ef0ca80ef` waited=0s held=3s: 24 passed, 0 failed, 0 skipped, including `Root_normalising_allowed_cwd_spellings_are_refused`, `Windows_mixed_case_standing_leaf_is_refused`, and `Standing_symlink_swapped_before_start_is_refused`.

Guards, same tested SHA. Build lease `170cc7b5-93c4-445c-bbb1-453006d79aea` waited=0s held=177s. Run lease `588cd0bc-3ecd-4f85-80c1-55ee458489e4` waited=0s held=126s: 78 passed, 0 failed, 0 skipped. Policy 41, classification guard 1, slow registry 5, tripwire 2, NativeLinuxPolicyWitness 3, mechanical 11, TaskPlatformGuidance 5, C1729 9, lane category 1. Filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/*`.

The card had no checkpoint table. `docs/superpowers/plans/2026-11-10-card-1834-allowed-cwd-advisories-plan.md` is an OS-witness anchor only (CP-1), not a checkpoint-tool row. Checker exit 0 at `51f5b934fe5954ba5af60097fc590d2b684d8788`. The two `OperatingSystem.IsWindows()` calls (base 1083 and 1093, head 1087 and 1097) are unchanged gates and are dispositioned non-behavior. Evidence `/tmp/c1834-os-witnesses.json`. `tests/test-execution-policy.json` is unchanged. Evidence-diff exit 0, 4 commits, 0 violations, base `8cae27e99ac1ebfaf5e062c1f8f4c4339e8dfe0c`. `git merge-tree --write-tree origin/master HEAD` exit 0, tree `8e88399288cbec87d0041210f77e41b126dc549c`.

## Positive controls

Pending, not executed.

- PC-1: remove the collapse and dot-segment rejection so `Root_normalising_allowed_cwd_spellings_are_refused` goes red.
- PC-2: remove the lowercase leaf check so `Windows_mixed_case_standing_leaf_is_refused` goes red.
- PC-3: remove the second `RejectLinkedStandingCheckout` so `Standing_symlink_swapped_before_start_is_refused` goes red.

## Files

- `src/Antiphon.SessionRunner.Contracts/RunnerPathForm.cs`
- `src/Antiphon.SessionRunner.Contracts/RunnerWorkspaceAdmission.cs`
- `src/Antiphon.SessionRunner/PhoneHomeSettings.cs`
- `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`
- `tests/Antiphon.SessionRunner.Tests/StandingRunnerCwdTests.cs`
- `docs/superpowers/plans/2026-11-10-card-1834-allowed-cwd-advisories-plan.md`
