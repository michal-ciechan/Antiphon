# Merge batch 151 report (task 0677b5ed)

Outcome: both subjects landed on master. 4a600e9af54c297d8527f472e47fdbc1e9b6f652 -> da4f168de5a771adf5dab22336826d19b4ec613b (plain fast-forward, first try). Nothing was excluded and every pick was clean. No migration, no land-graph refresh, no re-pin.

## Per-commit table (cherry-pick -x, linear)

| Subject | Source | Landed | patch-id |
|---|---|---|---|
| group-3 fix (Debug 1db3f61f) | 179039c499882776ed3d08cacb932b729a23e694 | 54b0a1ced | eff6df55477c match |
| CARD-1784 plan | 0130776b73c5d3c38a767e449b7732f5723f0381 | 8536980bd | 8a1452cbdd17 match |
| CARD-1784 red | 3fc4389a0c99de1b192d162a3e2b12ac75522185 | 549cce24f | 14a5a7eb4151 match |
| CARD-1784 impl | 247ea2c69ed1c5efba535ec45b39030c1844c41e | b0d0378d4 | 165034f9c660 match |
| CARD-1784 census order | 6a97ddc201118b42d665afa8de588cf582ef8cb4 | 1d88796e6 | beddabe5fda9 match |
| CARD-1784 r1 red | 60e24bbfb5c6b8bdb42b76add9aa1b613ce40d41 | e5ca48a2d | 35f6873c6c12 match |
| CARD-1784 r1 fix | 05a47779cb2f64567d526d01131e3efd336c6674 | 12706fc26 | 8e79ef573499 match |
| CARD-1784 r1 fix | 6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647 | f03ace285 | e5a6ca14dade match |
| CARD-1784 r2 red | ee4258bbb91e10dd04b550bb05ee4d86bbeea578 | cfcb250fa | 3af352740a8a match |
| CARD-1784 r2 fix | 287fc145771e1850a22d681a1c49e44712d2017f | da4f168de5a771adf5dab22336826d19b4ec613b | 98233adf0b9b match |

Skipped: 6fa1d709e (report-only, .antiphon/task-d705ed31.md).

## Final-tree checks (all at da4f168de, server2 Linux)

- Solution build: `dotnet build Antiphon.sln -p:UseAppHost=false` (parallel, maxcpucount 6), 0 errors, 2m57s.
- One batched Antiphon.Tests run of 22 class patterns: guards without the policy classes (MechanicalPcContract, TaskPlatformGuidance, C1729LayeringGuard, TestLaneCategoryGuard), ProcessSpawnLimit, C0824WorkspaceSlotRoleGate, PostLandEarlyWarning, RunnerCodexAdapter*, CodexAdapter*, and the 13 fixed merge-subset classes. 343 total, 0 failed, 335 passed, 8 skipped, 7m17s. The skips are Windows-only ConPTY: CodexAdapterLocalShellTests 7 and CodexAdapterIntegrationTests 1. PostLandEarlyWarningTests 20/20. RunnerCodexAdapterTurnCompleteTests 9/9, including C1322_Cancelled_verdict_cannot_become_screen_success. Ready 12/12, SubmitConfirm 8/8, RawResponse 3/3. TestLaneCategoryGuard 1/1, ProcessSpawnLimit 55/55.
- C1729LayeringGuardTests: `VIOLATIONS 0 LISTED 168`.
- Policy guard batch, in its own process: 52/52, 0 failed, 1m19s.
- A5 pair (C1686_LinuxOnlyCensus, LinuxOnlyWindowsExclusionPolicy): 2/2.
- E2E ContractSnapshotTests: 5/5.
- land-graph `-Check` (Debug LandGraphAudit dll): `approved`, exit 0. No refresh needed.
- check-evidence-diff over 4a600e9af..da4f168de: commits=10, violations=0, exit 0. No unshallow needed.
- check-os-witnesses: exit 0. The gitignored declaration is in the scratchpad (not committed). Inventory is the 5 non-doc paths. Witness W-1784-linux-gate: the `$linuxHost = $IsLinux` gate in the entry script, test Non_linux_run_refuses_before_a_slot, CP-1, portable-forced. Its Windows native obligation is pending. restartImpact is required for `scripts/lib/post-land-early-warning.ps1` by the scripts/lib census, also pending on Windows. AppHost does not load that library.
- import --plan: the documented Merge rows (orchestration-loop.md lines 705-711, without the migrations-only messaging row) imported 5 rows, 0 warnings. The CARD-1784 plan imported 6 rows. checkpoints.yaml was deleted after each import.
- impacted-classes -MergeSubset -Range: no migrations. The only kind is spawn-limit-rosters (ProcessSpawnLimitTests), and that class was in the union.

## Pins

The values the brief quotes (50c33c11 / 8510514a) are stale template carry-over again. Computed equals stored before the push and again right before it:
- policyHash fdc043758617a5e8c21735126539c559faf9dad95abe5f1122a6d5780ee9df4d (unchanged)
- PinnedInputDigest fb0af076ce8dcaec52af83234cc9fe5d642047028194f866c1c19ac6290e98a3 (unchanged)

## Owed

- A Windows native run of PostLandEarlyWarningTests.Non_linux_run_refuses_before_a_slot (OS-witness obligation pending).
- Windows restart-impact: none functionally, because AppHost does not dot-source scripts/lib/post-land-early-warning.ps1. It is recorded as pending only because of the census.
- 8 Windows-only Codex skips. The group-3 fix itself is a Windows load race, so its real proof is the next Windows rc run.
- No rc/* was cut or pushed. Porting to rc is a separate decision.
- Not in this batch: CARD-1819, CARD-1820 S1, CARD-1823 S1.

--- next stage ---
next: done
handoff: master da4f168de5a771adf5dab22336826d19b4ec613b carries batch 151
artifact: .antiphon/task-0677b5ed.md
