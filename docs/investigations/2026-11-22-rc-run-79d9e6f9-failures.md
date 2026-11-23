# Windows Full run `79d9e6f9` failures

## Group 134 · C1323 marker and policy row

Nightly S16 (`b5ed36161f01d59cf7f2172aaf49ffa914675b13`) moved the capture-shell checks into `QualificationTimingTests` and removed the old `PosixHarnessToolTests` test and marker.
The CARD-1323 Windows exclusion still named both the deleted capture test and the surviving cutover test, so the marker and policy method sets differed.
S16's recorded verification covered the ported and edited classes and the derived index. The A5 method was outside those short subject selections.
Use the existing `linux-only-policy` removal trigger for `[NativeLinuxOnly(` lines and execute its selected `ReleaseGatePlatformTests/LinuxOnlyWindowsExclusionPolicy` row on the combined tree.

## Group 134 · C1210 roster closure

The same S16 commit removed the capture test's runtime gate. The baseline TRX had 252 source identities and 253 roster entries, with only `Capture_identity_resolves_git_sh_instead_of_hard_coded_bin_sh` on the roster side.
S16's short verification omitted the portable closure method. The existing `native-linux-gate-roster` path trigger does not include `PosixHarnessToolTests.cs`.
Route additions and removals of `NativeLinuxGate.Require` or `C1008HostFixture.RequireNativeLinux` in `tests/Antiphon.Tests/**/*.cs` to `WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates`.
The [g132 retrospective](2026-11-10-why-missed-g132-windowsunitplatformadmission.md) describes the same coverage gap for gate additions.

## Group 137 · Merge-subset kind lists

The [read-only Git fixture sweep](2026-11-22-why-missed-git-fixture-readonly-delete.md) (Debug `62b60ff2`) landed as `fd1a0c831415b5d76b42bb949311ecf75849cdf9` and added `test-fixture-cleanup` for `tests/**/*.cs`. Both historical ranges match that glob.
The sweep's short pre-land scope centered on fixture teardown and its guard. Merge runs the subject's narrow witness rows, which left the Unit `MergeHighRadiusSubsetTests` outside the selection until Windows Full.
When `tests/impacted-classes.json` changes, run the two historical range methods in `MergeHighRadiusSubsetTests` and the two historical kind-list methods in `ImpactedClassesSelectorTests`. The same coverage gap caused the [group 135 failure](2026-11-16-why-missed-g135-merge-subset-kinds.md).

## Group 145 · Git fixture cleanup

Nightly S19 (`cad15e980841e1713f645d19f354227832303761`) landed after the fixture cleanup sweep and added a raw recursive delete of the fixture's `.git` directory in `NightlyRunTests.C487_G002`.
Short Merge replays the subject's own witnesses, which left the fixture cleanup guard outside S19's selection on the combined tree.
Apply the existing `test-fixture-cleanup` trigger for `tests/**/*.cs` during short Merge and run `/*/*/TestFixtureCleanupGuardTests/*` on the combined tree before landing matching changes.

## Group 146 · Rolling recycle Git audit fixture

CARD-2043 (`c3781c45acef1e7a15b8e0e6bd3ed41b0afb285b`) raised `GIT_CONFIG_COUNT` from 2 to 7 to disable hooks, fsmonitor, user attributes, external diffs, and submodule recursion during retirement audits.
Short Merge selected the CARD-2043 dry-run preview method. The 40 cases of `C1594_Antiphon_evidence_gitdir` were outside that selection and shared a fixture that still expected 2.
Include this method in the short witness selection when changing the audit helper's Git environment.

## Group 138 · Live-gate disposition

`LiveLaneInventoryTests.EveryLiveGateHasDisposition` reported one unmapped gate after the Nightly S15 port (`75d96a8c7`). The scanner sees the `ANTIPHON_HEADED_TESTS` literal in `NightlyTestsRunTests.C487_G044`. The test supplies the value in a fixture environment and checks that the launcher clears it before starting the child. It runs without a headed opt-in, so the scanner hit is a false positive.
The data-only repair adds the generator's `diagnostic` disposition to `tests/live-lanes.json`. Short verification for changes that introduce a recognized gate shape should run `EveryLiveGateHasDisposition` to catch catalog drift before Windows Full.
Scanner changes from `65aae6b4` and `2920c586` are deferred. Follow-up scanner coverage needs to recognize a real lookup with the `target` named argument first and ignore environment lookup text printed as output.

## Group 90 · C778 Grok ready-ordering deadline race

Seeded Windows Full run `557cdb63` (rc/2026-12-03 at `ecc9bfaa5`) failed `GrokStartupReadyOrderingTests.Work_waits_for_ready_rules_ack_and_complete_prompt` with `Status = Failed`, rules `Pending`, `writes=0`. No product change was involved: the CARD-2047 readiness commits were already in the previous seed `39ac64c90`.
The test calls `ReleaseReady()` only after it has seen two starting-screen snapshots and flushed the session queue. The scripted adapter's 1500 ms `GrokReadyMaxWaitMs` started counting at the first snapshot, so a slow DB flush on a loaded host let the deadline expire first. A 1.6 s pause before `ReleaseReady()` reproduces the exact message on Linux.
Fast Linux hosts always finished within 1.5 s, which is why short runs never caught it. Repair `48c1f442d`: the happy path uses a 60 s deadline, so `ReleaseReady()` alone ends the wait and the 25 s test token bounds a hang. `Unready_launch_cleans_up_and_keeps_work_pending` still covers expiry with a 180 ms deadline.
In a test, a product deadline should be shorter than a test-controlled gate only when expiry is what the test asserts.

## Group 97 · Scope-hold launch count (run `557cdb63`)

`DelegationScopeHoldTests.a_worktree_task_dispatches_over_a_shared_holder_with_one_warning` asserted that the recording launch sink held exactly one session. The dispatcher tick dispatches every Queued row in the shared fixture database, so the sink also recorded launches for Queued tasks left or written by other suites. In the seeded Windows Full order that was three launches. On Linux, with the method alone, the result was green 3/3. One foreign Queued row seeded on purpose reproduces the failure deterministically (2 launches, `e71acf78e`).
No product change in `39ac64c90..ecc9bfaa5` touches the dispatcher. The failure depends on which suites share the database at the time of the tick, so a new seed exposed it and short Merge could not.
The repair (`e0512e864`) records each launch's `Cwd` and asserts one launch under the test's own workspace. This applies to this test and `two_worktree_tasks_in_one_area_both_run`. A dispatcher test that counts launches, events or rows against the shared database has to scope that count to rows it created.

## Master build · Resilience linked fixture

`1beb89e8c9fcb1048e425dbce6bf4df7b2c0c62a` added `BridgeQueueHarness.WarmModel()` to `TestDbFixture.cs`, which Resilience compiles without the bridge harness.
The commit records verification of `ChannelOutboundUnifiedPathTests` in `Antiphon.Tests`, where the call resolves. The original land's build selection is unverified.
The existing CARD-1996 Merge rule covers this gap. For changes under `tests/Antiphon.Tests/TestHelpers`, build `Antiphon.sln` or each linking project and run `LinkedTestHelperClosureTests` before publishing.

## Group 139 · C487 forced-Windows read-only witness (run `557cdb63`)

`C487ReadOnlyCleanupWitnessTests.C487_WindowsReadOnlyWitness` failed with exit 1: `Join-Path` could not bind an empty `Path` at line 7 of `scripts/lib/c487-harness.ps1`. W2-nightly S17 (`1c04c5878`, after the previous seed `39ac64c90`) added `. (Join-Path $PSScriptRoot 'c487-nightly-tests.ps1')` to the harness. `scripts/fixtures/c487-readonly-windows-witness.ps1` loads that harness as text through `[scriptblock]::Create`, and in that case `$PSScriptRoot` is empty.
This failure is deterministic on every platform, not specific to Windows. It was red on Linux at master `50d47f9b6` (1/1 failed, same message). S17's short verification did not select this `Integration` witness, and the witness has no direct-execution check of the harness file, so the first run that covered it was Windows Full.
The repair (`fdef148a6`) replaces `$PSScriptRoot` in the text-loaded source with the harness's own `scripts/lib` directory. The test is green 3/3 and its class 1/1 on Linux, and the witness passes with `-SourceRef` at master and at `1c04c5878~1`.
A change that makes a library dot-source a file through `$PSScriptRoot` should run every caller that loads the library as text (`git grep 'scriptblock]::Create'` next to the library name) in its short verification.

## Group 140 · C487_G109 sha-mismatch row (run `557cdb63`)

`NightlyHealthTests.C487_G109` failed with `["G109 sha mismatch :: "]`. G109 is not a file-hash pin: it checks that `NightlyHealth.Evaluate` reports a Windmill job SHA that differs from the native state SHA. That check runs only for today's scheduled job, and the row's job had no `LocalDueDate`, so `sha-mismatch` never fired. The row asserted bare `!Healthy` at `DateTime.UtcNow`, so it passed only on an unrelated reason: `overdue-start` after 01:00 London, or `missing-expected-run` after 08:00.
Between 00:00 and 01:00 London the verdict is healthy and the row fails. Group 140 of the long Windows run reached that window. Base `50d47f9b6` was red 3/3 on Linux at 00:27Z, so the failure is not specific to Windows or to line endings. Daytime runs could never hit it.
Repair (test only, red witness `af8f35dd5`): the job is the due day's (`LocalDueDate`). The row asserts the `sha-mismatch` reason at five fixed London times with a matching-SHA control, and every evaluator row now puts its verdict (`healthy`, `pending`, `reasons`) in the failure detail. A health-verdict test should assert the reason it is about, at a pinned clock, never `!Healthy` at the wall clock.

## Group 5 · SessionRunner interpreter guard audit (run `557cdb63`)

`WindowsShellGuardAuditTests.Every_command_interpreter_literal_is_behind_a_windows_guard` flagged `C0824WindowsSlotHolderTests.cs:63`. The `cmd.exe` literal comes from `35a880de5` (the 2026-11-22 exited-process census witness). That commit landed after the previous seed, and the audit is a static source scan, so it was red on Linux at master `9d2f1cc6d` too (3/3). It is not a Windows-only problem.
The method calls `NativeWindowsGate.Require` on its first line. The scanner only recognises `OperatingSystem.IsWindows()`, so it does not see this gate. The repair (test-only) adds the site to `Reviewed` with that reason, which matches the `C1022BackendLaunchTests` precedent.
Short verification ran only the subject's `C0824WindowsSlotHolderTests` rows. No `tests/impacted-classes.json` trigger connects `cmd.exe` edits in `tests/Antiphon.SessionRunner.Tests/**/*.cs` to the audit. Any change that adds an interpreter literal there must run `/*/*/WindowsShellGuardAuditTests/*` (3 tests, under 1 s) before landing. A follow-up could add that trigger.

## Final guards · Release-selection fixture cleanup (seeded run on `8d64518a9`)

`TestFixtureCleanupGuardTests` flagged a raw `DirectoryInfo.Delete(true)` at `ReleaseSelectionFixtures.cs:69`. It came from `794aa6f1f` (unit 8 of the remaining-ps1 plan, 2026-11-23), which landed after the previous seed. The guard is a Roslyn source scan, so master `8d64518a9` was red on Linux too. It is not Windows-only.
An operator session pushed the unit straight to master and ran only its ported classes. No Merge ran, so the existing `test-fixture-cleanup` trigger never selected the guard.
Repair (test-only): `ReadOnlyDirectory.Delete`. Before any direct push that adds or ports a Git fixture, run the Final-guards filter. Detail: [release-selection fixture cleanup](2026-11-23-release-selection-fixture-cleanup.md).
