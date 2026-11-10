# CARD-1834 Final Review

Round 1, Final. No product code change. Subject `origin/feat/card-task-ef2fee3f` at `0e937620d416bdcdf980c45edbe068d28493ba9a`. Code owner `ef2fee3f-ec46-4266-bed7-c68952c0effa`. Verdict: behavior clean. `reviewedSourceClean` is false because the plan's only checkpoint row is not a checkpoint-tool row, so there is no `validate-checkpoint-receipt` receipt.

## Behaviour

A-1. `PhoneHomeSettings.Validate` refuses an AllowedCwd that is whitespace, a filesystem root, a `.` or `..` segment, or a lexical collapse to a drive root (`//?/C:/`, and `\\?\C:\` after slash folding), with the existing text `PhoneHome:AllowedCwd must not be empty or a filesystem root.` `IsFilesystemRoot` is unchanged. `/work`, `/work/agents`, `C:/Antiphon/runner/work`, and `C:/Antiphon/runner/work/` still validate. A name segment that merely contains a dot (`my.app`) is not a `.` segment and stays admitted. Relative `work` still fails the absolute-path message.

A-2. On the windows flag only, a standing leaf must equal its `ToLowerInvariant` form. Standing names are ASCII, so that is the case fold. Linux still admits `Foo` and `foo`. `/WORK/agents/foo` stays admitted. Worktrees are not lowercased. `Z`, `Slides-Server2`, and uppercase `COM10`/`COM`/`LPT`/`PRINTER` are refused on the windows flag; lowercase `z`, `console`, `com10`, `com`, `lpt`, and `printer` stay admitted.

A-3. `LaunchAsync` calls `RejectLinkedStandingCheckout` again inside the mutation lock, after the capacity check and before the generation record, `RecordLaunch`, and `StartAsync`. A symlink swapped during the sign-in probe is refused and does not start. Residual: a replacement after that second check and before the pty host chdir can still start. No `O_NOFOLLOW` branch was added. That window is acceptable: the card allowed a recheck instead of no-follow, and the recheck closes the probe-and-capacity gap. It is documented in `.antiphon/task-ef2fee3f.md` on the reviewed tip. The source comment states the closed gap and does not restate the leftover window. Advisory only; it does not hold the land.

A-4 stays pending on Windows. This Linux run does not pass a junction, a real directory, or `C:/` / `C:\`. Nothing marks those rows passed.

## Evidence

Reviewed tree was clean at `0e937620d416bdcdf980c45edbe068d28493ba9a` before this review file.

StandingRunnerCwdTests. Build lease `e3689cf3-bf76-486a-b737-e0d152d6e0e8` waited=15s held=76s, UseAppHost=false, 0 errors. Run lease `6cf09f60-fed1-4abe-bb61-9b4e37841943` waited=45s held=3s: 24 passed, 0 failed, 0 skipped.

Guards, same SHA. Build lease `6e97f550-1ff6-4fd8-8cca-2a8138820817` waited=60s held=258s, UseAppHost=false, 0 errors. Run lease `3b6e4318-b7c2-47d5-b304-9afec3925321` waited=0s held=142s: 78 passed, 0 failed, 0 skipped. Filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/*`.

CP-1 was not run through the checkpoint tool. The plan says that row is an OS-witness anchor only. `scripts/check-os-witnesses.ps1` exit 0 at base `8cae27e99ac1ebfaf5e062c1f8f4c4339e8dfe0c` head `0e937620d416bdcdf980c45edbe068d28493ba9a`. The two `OperatingSystem.IsWindows()` calls (base 1083 and 1093, head 1087 and 1097) are the pre-existing gates, dispositioned non-behavior.

`scripts/impacted-classes.ps1 -Range` selected 0 filters. The migration row did not match, so the downgrade selector was not scanned; count 0.

Evidence-diff on the reviewed range: exit 0, commits=5, entries=1, violations=0. `git merge-tree --write-tree origin/master 0e937620d416bdcdf980c45edbe068d28493ba9a` exit 0, tree `ae50b4bbfff18579878845c975b898ad80bdbef1`, master `29e6736ee88b8ee686fea28128fe13056d79828d`.

Historical red/green was read, not re-run. Red `62f08cd78a0b84415416361fcf3e15652747c096` (3 failed), follow-up `8928f01711bb83963cf10830c7f04c2d4769f013` keeps the `Z` refusal. PC-1, PC-2, and PC-3 stay pending.

## Advisory

The source comment above the second `RejectLinkedStandingCheckout` does not state that a swap after that call can still start. The Code report does. Not a land hold.
