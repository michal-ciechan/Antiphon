# CARD-1850 S3a Final Review — ea79a8f2

CLEAN. Portable Windows launch preparation at `369c5e3900a9f80ab6c5cf796b2c6f16e2c0412c` is ready to land. Native `CreateProcess` membership, empty-job join, and Windows execution stay with S3b. Positive controls stay pending. Original Code landing owner: `66a1f6d2-9287-4df4-b1cc-7e859737de32`.

Reviewed range `c38918255330caec88c35fda713dbddad221d5a1..369c5e3900a9f80ab6c5cf796b2c6f16e2c0412c`. Product files: `WindowsOwnedGitLaunch.cs`, `WindowsOwnedGitProcess.cs`, `OwnedGitProcessContractTests.cs`, `tests/derived-impact-index.json`, and the plan. The sixth commit is the Code report. No caller migration. `OwnedGitProcess.cs`, `LinuxOwnedGitProcess.cs`, and `LinuxOwnedGitMembers.cs` are unchanged. No scripts changed.

## Combined tree

Current `origin/master` is `b57583cee95b75c62edd496247e2361c7cc4e034`. Cherry-pick of the five product commits onto that tip applied cleanly in `/tmp/card-1850-s3a-review-ea79a8f2` (report commit skipped). The index commit auto-merged; regenerating `tests/derived-impact-index.json` left the bytes unchanged. Scratch tip `73420536de2c4edaffee931c0b9b343a9f183b58`.

Scratch receipts, source clean, build verified: CP-3 61/61, CP-S3-1 14/14, CP-S3-2 129/129, CP-S3-3 10/10. `ReleaseImpactEnvelopeTests` is 10 methods on current master and 4 on the branch tip; both floors are above Min 4. Extra row on the same build: `LinuxOwnedGitProcessTests` 35 and `ProcessSpawnLimitTests` 57, together 92/92. `git merge-tree --write-tree origin/master HEAD` at the reviewed tip exited 0, tree `a9c3b31c3e612f30fb768a3990a61bfe8ce6c87e`.

## Reviewed SHA

Checkpoint tool, slot-gated, `UseAppHost=false`, expected SHA `369c5e3900a9f80ab6c5cf796b2c6f16e2c0412c`, dirty=0, sourceState=clean, buildSource=verified. `validate-checkpoint-receipt.ps1` returned CHECKPOINT SOURCE VALID for every green receipt below.

| Row | Result | Receipt |
|---|---|---|
| CP-3 | 61/61/0/0 `OwnedGitProcessContractTests` | `20261113-132439-79f9` |
| CP-S3-1 | 14/14/0/0 (`GitIndexLockTests` 11, `ServerGitAssemblyTests` 3) | same run, build reused |
| CP-S3-2 | 129/129/0/0 after the driver build | `CP-S3-2-20261113-133542-20fb` |
| CP-S3-3 | 4/4/0/0 `ReleaseImpactEnvelopeTests` | `CP-S3-3-20261113-134331-fa3c` |
| named extra | 92/92/0/0 (`LinuxOwnedGitProcessTests` 35, `ProcessSpawnLimitTests` 57) | `CP-S3-extra-20261113-133949-ffb6` |

CP-S3-2 class split: `TestClassificationPolicyTests` 42, `DerivedImpactIndexTests` 27, `ServerGitProcessTests` 23, `MechanicalPcContractTests` 11, `C1729LayeringGuardTests` 9, `TaskPlatformGuidanceTests` 5, `SlowTestRegistryMergeTests` 5, `NativeLinuxPolicyWitnessTests` 3, `SlowTestTripwireTests` 2, `TestClassificationGuardTests` 1, `TestLaneCategoryGuardTests` 1.

The first guards run (`20261113-132632-4234`) executed 129 and failed 1: `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged` exits 1 when `Antiphon.DerivedImpactIndex.dll` is absent. That test invokes `regenerate-derived-impact-index.ps1 -NoBuild`. Building `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` through the slot, then rerunning the closed filter with the kept build, produced 129/129. CP-S3-3 in the first guards run was already 4/4; the standalone row is the validated receipt. A filter aimed at `Antiphon.Server.Git.Tests` executed 0 for `LinuxOwnedGitProcessTests` because that class lives in `Antiphon.Tests`; the corrected row is the 92 above.

Historical CP-S3-R (`20261113-121610-b8d7` on the Code worktree) has eight headings, all `Windows_arguments_preserve_empty_quotes_backslashes_and_unicode`, assertion `transport should not be null` with `S3a must encode argv before a native child can be created`. That is the expected pre-implementation red. The same class is 61/61 on the green tip. CP-S3-R was not re-run as a green claim.

## Safety

`WindowsOwnedGitLaunch.Prepare` creates an unnamed job (`CreateJobObjectW` with null security and a null name) and builds a two-attribute list: `PROC_THREAD_ATTRIBUTE_JOB_LIST` (`0x0002000D`) holding the job, and `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` (`0x00020002`) holding exactly the three child standard handles. `CreateJob` does not call `SetInformationJobObject`, so breakaway stays at the job default and `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` is absent. Explicit termination remains S3b's job. Parent pipe ends are made non-inheritable with `SetHandleInformation` mask 1 flags 0. Attribute buffers are freed after `DeleteProcThreadAttributeList`. A prepare failure disposes acquired handles and throws `owned_git_windows_prepare_failed`.

This slice does not call `CreateProcess`, `AssignProcessToJobObject`, `ResumeThread`, or `QueryInformationJobObject`. A child cannot run outside the job here because no child is created. S1 cleanup is unchanged: `Joined` is `RootExited && Members.State == Empty`, and an incomplete scan stays `Unknown`.

`Windows_prepared_transport` asserts the inherited list equals the three child handles and does not contain the job. `Windows_preparation_failure_closes_every_acquired_resource` covers failAt 1..6. Tests use `FakeWindowsNative`, compile and run on Linux, and contain no Windows skip that counts as a pass. P/Invoke uses Unicode `CharSet`, `SetLastError`, `SafeHandleZeroOrMinusOneIsInvalid`, and `uint` for `SearchPathW`. `nLength` is `Marshal.SizeOf` of the security-attributes struct.

Mutants, judged from source and the tests (operator waiver; production launch is not in this slice):

- Sharing the job handle with the child fails the inherit assertion above.
- Allowing breakaway would require a new `SetInformationJobObject` call. The shipped `CreateJob` does not make that call. A native limit witness belongs to S3b.
- Assign-after-resume is not implemented. The prepared path is creation-time `JOB_LIST`.
- Treating assignment failure as success fails the six prepare-failure cases. CreateProcess-time failure is S3b.
- Treating `ERROR_MORE_DATA` as Empty has no query to mutate. S3b owes a truncated or growing PID list as `Unknown`.

## Selector, evidence, platform

`impacted-classes.ps1 -Range` over the reviewed range: kind `server-git-start-sites`; classes `DerivedImpactIndexTests`, `GitIndexLockTests`, `OwnedGitProcessContractTests`, `ReleaseImpactEnvelopeTests`, `ServerGitAssemblyTests`, `ServerGitProcessTests`; owed empty; windowsFilters empty. Those six classes are inside CP-3, CP-S3-1, CP-S3-2, and CP-S3-3. Migration symbols in the three new/edited C# files: 0. Downgrade selector was not selected (red=0/green=0).

`check-evidence-diff.ps1` over the reviewed range: commits=6, entries=1, violations=0. The entry is the Code report. OS-witness lint for that range passed with one non-behavior candidate at `WindowsOwnedGitLaunch.cs:123` (credential fields rejected only when `OperatingSystem.IsWindows()`). Runner defaults and session runners were read; no runner and no platform were pinned. Default placement stays the configured Linux host.

Observed reviewed-SHA cost: CP-3 build 23.5s, tests 0.08s wall, executed 3.8s; CP-S3-1 tests 5.2s wall, executed 9.9s; guards build 218s, tests 186s wall, executed 207s; `DerivedImpactIndexTests` 139s for 27 tests; CP-S3-3 tests 22s wall, executed 30s. Slot waits on these rows were 0s. The plan's CP-10 advisory (48 minute derived row timeout) is for a row this review did not run.

## Hygiene and advisories

S3a tests use memory streams. They created no fixture directories.

`ReleaseImpactEnvelopeTests` on this tip (`MissingIndexIsIncomplete`, `StaleInputIsIncomplete`, `CandidateMismatchIsIncomplete`, `OrdinaryModeRemainsAdvisory`) still `git init`s under `.antiphon/selector-evidence/f1903e95/fixtures/<guid>` and has no dispose. This run left four such repositories; they were removed by exact path. Current master's copy deletes the fixture (`DisposeAsync`, `FixtureCleansUpAfterFailure`), and the cherry-pick onto `origin/master` left no nested git under `.antiphon`. The author's four GUIDs were the same pre-existing class. This is already repaired on master and is not an S3a regression.

`LinuxOwnedGitProcessTests` via `OwnedProcessContainmentFixture` retains non-git directories under `.antiphon/containment-fixtures` by comment. The same leftovers exist after the master run. The five directories from this run were removed by exact path.

Advisory for a card: `WindowsOwnedGitProcess.cs` (`WindowsOwnedGitStreams`) has no derived-impact edge. The committed index maps only `WindowsOwnedGitLaunch.cs` to `OwnedGitProcessContractTests`, and regeneration on the merged tree agreed. A later streams-only edit will not select that class until a test spells the type name.

Advisory, no land hold: `Prepare` resolves the executable before command and environment encoding. `SearchPath` acquires no handle. The invalid-transport cases assert `Acquired.Count == 0`.

S3b still owes: `CreateProcessW` with `EXTENDED_STARTUPINFO_PRESENT` and `bInheritHandles` true before `ReleaseChildHandles`; `cb` set to the extended startup size; creation-time `JOB_LIST` rather than assign-after-resume; assignment failure fail-closed; `ERROR_MORE_DATA` and a PID list that grows between query and use as `Unknown`; no breakaway flags; explicit termination because kill-on-close is intentionally absent.

No product file was edited. Mutation and every PC remain pending.
