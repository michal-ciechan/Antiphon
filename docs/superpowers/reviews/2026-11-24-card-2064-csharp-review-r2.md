# CARD-2064 Review round 2 (Final/Full, C# stack checks): Clean

- Subject: `origin/feat/card-task-cae8bb4f` tip `650e778cd12cdad34d424978ea045b70ab07c37e` (Code task cae8bb4f, Codex High).
- Range reviewed: `a8982a509d823ffe1d8639838747ea6dc3f42bc6..650e778cd12cdad34d424978ea045b70ab07c37e` (3 commits, unchanged since round 1).
- Reviewer: Review task b6941f8f (Claude Opus 5.5), server2 runner mirror, Linux. Nothing ran on the desktop runner.
- Round 1: task 69d20d94, note `docs/superpowers/reviews/2026-11-24-card-2064-csharp-review-r1.md` on `feat/card-task-69d20d94`.
  That round was Clean but Interim. This round runs the Final scope that round 1 skipped.
- Verdict: **Clean**. No blockers and no regressions. ordinaryScopeCompleted: Full.

## Scope run (checkpoint tool, all rows at 650e778cd)

The plan extracts are under the ignored `.antiphon/r2064/` folder in the runner mirror: `review-plan.md` (run A),
`review-plan-b.md` (run B) and `review-plan-c.md` (run C). Every row ran through
`tools/Antiphon.Checkpoints` with `slot=granted waited=0s dirty=0`.

| Row | Selection | Result at 650e778cd |
|---|---|---|
| author | `BootstrapCheckTests`, `VerifyDevStackSmokeTests` | 36/36 passed |
| guard union (10 classes) | TestClassificationPolicy, TestClassificationGuard, SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitness, MechanicalPcContract, TaskPlatformGuidance, C1729LayeringGuard, TestLaneCategoryGuard, TestFixtureCleanupGuard | 85 executed, 84 passed. The 1 failure is inherited (I-1 below). |
| brief-named guards | `NativeLinuxPolicyWitnessMutationTests`, `WindowsUnitPlatformAdmissionTests` | 18 passed, 0 failed, 1 roster-listed Windows skip |
| Merge guard rows | `DerivedImpactIndexTests`; `C1514LandingSplitBudgetTests/C1584_E2EExpandedBudget` | 25/27 passed, with 2 inherited failures (I-2 below); 1/1 passed |
| impacted, `--merge-subset` | AppDbContextModel, CheckpointReceiptSafety, HostCleanupCheckpointReceipt, LiveLaneRunner, NightlyCoreGolden, ReceiptScanDestinationIntegration, TestFixtureCleanupGuard, TestLaneCategoryGuard, TestTimingOperationLabel | 82 executed, 81 passed. The 1 failure is I-1. |
| impacted land-1 | AgentTaskLandHeldNotification, …ParkFollowUp, …Persistence, …ReconcileLoad | 34/34 passed |
| impacted land-2 | AgentTaskLandNotificationRecovery, …Rescan, AgentTaskLandReceiptWatermarkSafety, …Watermark | 104/104 passed |
| impacted methods | `ReleaseGatePlatformTests/C1686_LinuxOnlyCensus`, `/LinuxOnlyWindowsExclusionPolicy` | 2/2 passed |
| impacted E2E | `tests/Antiphon.E2E` `ContractSnapshotTests` | 5/5 passed |

The impacted selection came from `scripts/antiphon impacted-classes --merge-subset --range a8982a509..650e778cd --json`.
Its diff-derived classes are BootstrapCheckTests, VerifyDevStackSmokeTests, LiveLaneRunnerTests, NightlyCoreGoldenTests,
TestFixtureCleanupGuardTests and TestLaneCategoryGuardTests. All of them are covered by the rows above.

Runs:

- Run A, `20261124-142615-acc2`: every row ran. Its verdict is RED only because of I-1 and I-2.
- Run B, `20261124-144845-5e5d`: TestFixtureCleanupGuardTests excluded and the E2E row added. Every row was green except
  DerivedImpactIndexTests (I-2).
- Run C, `20261124-150607-e5d4`: both inherited-red classes excluded, for the receipt. It was GREEN, exit 0, with 9
  rows and 365 tests executed, 0 failed. Its `report.json` gives `CHECKPOINT SOURCE VALID
  source=650e778cd12cdad34d424978ea045b70ab07c37e rows=9` under `scripts/validate-checkpoint-receipt.ps1`.
  - The tool `validate` verb gives rows=8 when the skip row is left out. It refuses CP-3 only for its one roster-listed
    Windows-placement skip, `C1210_Windows_linux_only_outcomes_are_exact`.
  - Every build has `buildSource` verified.

## Inherited reds (not caused by this subject)

Both reproduce identically at current master `0ed95b1d08a2ff0e253eb80245b80670adf69014`. That run was a detached scratch
checkout with a build-slot build: 28 tests, 25 passed, the same 3 failed.

- **I-1.** `TestFixtureCleanupGuardTests.Git_fixtures_use_read_only_safe_directory_cleanup` flags
  `tests/Antiphon.Tests/Review/ReviewMirrorCheckoutTests.cs:67` (`Directory.Delete(root, recursive: true)`).
  - The violation came in with a3d539025, "feat(review-engine): run the review engine in Docker", 2026-11-24. That commit
    is already in the subject's base.
  - The subject touches neither that file nor the guard.
  - A board search for `ReviewMirrorCheckoutTests` or `TestFixtureCleanupGuardTests` found no card. This needs a Debug
    card, which this read-only review did not file.
- **I-2.** `DerivedImpactIndexTests.committed_index_matches_the_builder` and
  `regenerate_command_reports_changed_then_unchanged` fail because the committed `tests/derived-impact-index.json` lags
  master. This is the drift the I-5 procedure expects between regeneration waves.
  - Regenerating on scratch copies gives byte-identical output at base a8982a509 and at tip 650e778cd. The command was
    `scripts/antiphon regenerate-derived-impact-index --no-build --repo <scratch>`, using the driver built at master.
  - So the subject adds nothing to the index, and this branch needs no regeneration.
  - Master needs one regeneration wave: `scripts/antiphon regenerate-derived-impact-index` and then
    `DerivedImpactIndexTests`. The server land regeneration (`DerivedImpactIndexRegen`) also covers it.

## Other checks

- `git merge-tree --write-tree origin/master(0ed95b1d0) 650e778cd`: tree `d891fb2558c7d80ce2b6cb8bd91f5de1a1e32727`, no
  conflicts. None of the 10 master commits since the base touch the subject's files, `scripts/lib/Antiphon.Nightly` or
  `tests/Antiphon.Tests/Scripts`.
- `scripts/antiphon evidence-diff --base a8982a509 --head 650e778cd`: commits=3, entries=0, violations=0.
- Diff re-read for blockers.
  - With the flag unset, the original docker/volume/pg_isready/17280 logic is unchanged and still nested under `dockerUp`.
  - SKIP rows count as neither a pass nor a failure.
  - `DevPostgresCheck.Probe` fails closed on an unusable endpoint, a parse error, a file error or a closed port.
  - `nightly-core.ps1` already builds with `-p:CopyLocalLockFileAssemblies=true` and runs `Add-Type` on every dll, so
    Npgsql and Microsoft.Extensions.Configuration.Json ship beside the core.
- Round-1 advisories A-1 to A-3 still stand. They are not blocking. A-3 is the desktop acceptance: run
  `verify-dev-stack.ps1 -SkipBrowser` and `bootstrap-check.ps1` with the flag set and unset. It stays pending for an
  operator or a desktop lane, because the desktop slot is held.
