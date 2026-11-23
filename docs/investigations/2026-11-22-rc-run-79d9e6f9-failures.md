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

## Group 139 · C487 forced-Windows read-only witness (run `557cdb63`)

`C487ReadOnlyCleanupWitnessTests.C487_WindowsReadOnlyWitness` failed with exit 1: `Join-Path` could not bind an empty `Path` at line 7 of `scripts/lib/c487-harness.ps1`. W2-nightly S17 (`1c04c5878`, after the previous seed `39ac64c90`) added `. (Join-Path $PSScriptRoot 'c487-nightly-tests.ps1')` to the harness. `scripts/fixtures/c487-readonly-windows-witness.ps1` loads that harness as text through `[scriptblock]::Create`, and in that case `$PSScriptRoot` is empty.
This failure is deterministic on every platform, not specific to Windows. It was red on Linux at master `50d47f9b6` (1/1 failed, same message). S17's short verification did not select this `Integration` witness, and the witness has no direct-execution check of the harness file, so the first run that covered it was Windows Full.
The repair (`fdef148a6`) replaces `$PSScriptRoot` in the text-loaded source with the harness's own `scripts/lib` directory. The test is green 3/3 and its class 1/1 on Linux, and the witness passes with `-SourceRef` at master and at `1c04c5878~1`.
A change that makes a library dot-source a file through `$PSScriptRoot` should run every caller that loads the library as text (`git grep 'scriptblock]::Create'` next to the library name) in its short verification.
