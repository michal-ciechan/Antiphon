# Master port of rc top-ups 44–51

Port of rc-only fixes from `origin/rc/2026-10-27` `e0d49d221513b467f8c853f47587e688e04255e2` onto `feat/card-task-a584c99e`, fast-forward from master `5aea2708fca4e196ae1915b6101c5287cb8c7cea`. Port tip `6799e6d40ac079842b59717c0a451714a07a845c` (126 commits). Pushed only to `refs/heads/feat/card-task-a584c99e`. Top-up 52 is not on that rc tip and was not ported. No rebase, amend, reset, force-push, tag, or publish. `_PtyHostNativeProps` is absent (`git grep` on `*.csproj`, `*.props`, `*.targets` is empty). Trees use `_PtyHostNativeDir`.

`git cherry -v origin/master origin/rc/2026-10-27`: 1123 rows, 978 patch-equivalent, 145 unmatched. Brief top-up 50 object `fc064a684` is not the tip; the object is `fc064a68247b0186918dfb1f04465e6be0475389`. Brief CARD-1610 SHA `4e7f3385` is not an object. CARD-1610 migration commit `7c6f96f62` is an ancestor of `origin/master`. Latest 1610 migration on master is `20261030214946_CloseTaskSupervisionReportWatches` via `e73aa7645`.

## Classification

| Series | Class | Port tip | Notes |
|---|---|---|---|
| Pre-44 Claude composer hints | (a) | `1f650acc5` | `eb284614b`, `4c4ad5126`, `1f650acc5` from `de9a8fba48`, `c90f8047b2`, `cdfd8366fa` |
| Pre-44 equivalents | (b) skip | — | `125c720b97`→`583a36b0b`, `3cb215cf3d`→`909e99ad4`, `9c271b7e1b`→`c2ebb2a47`, `80b3a59c40`→`698bf1b73`, `b226c226b5`→`cbe3e6b42` |
| Pre-44 rc-integration docs and cancelled pair `59deddd30a` + `97756db4ee` | (d) not ported | — | rc bookkeeping, not a master fix |
| CARD-1619 product + witnesses | (c) | `2dcb7e33d` | Migration recreated; see below |
| C448 lease / journal cleanup | (a) | through `60da5bf1e` | `f8ced3206` `a5fe39f84` `231761303` `7edc3135c` `60da5bf1e` |
| Release-gate credential-label scan | (a) | `7acbfa848` | `12ba4f389` `7acbfa848` |
| Top-up 44 record | (a) docs | `c52bcf0d5` | from `ac6bd1b5b` |
| Top-up 45 groups 104/105/124/126/128, policy rows, C1061 | (a) | `7a2d45c2e` | from `47a1cd5ed`. One hand conflict |
| Top-up 46 GitFixture / encoding witness | (a) | `78fee7464` | from `b37178b6b`. Land-graph input |
| Top-up 47 CARD-1514 manifest publication + remote assertion | (a) | `ddfb2d806` | from `3fb3c113e` |
| Top-up 48 C849 driver witness + C1022 join | (a) | `6fe770030` | from `13565c5a4` |
| Top-up 49 CARD-1619 policy rows + C1614 linger fixture | (a) | `eec5980ae` | from `53835c8c2`. One hand conflict |
| Top-up 50 Land receipt diagnostics + C488 selector | (a) | `e28223252` | from `fc064a682` |
| Top-up 51 C849 finalize-in-signal, C459 wait, C1022 retry, capture-identity | (a) | `6799e6d40` | from `e0d49d221` |
| CARD-1609 volume retirement | (d) not this port | — | Separate rc port. Not cherry-picked |
| Top-up 52 tree-kill / failed-first / seed builder | out of scope | — | Not on `e0d49d221` |

C849 custody residue that rc carried as `5ee70db1c` is already on master as `6898ab6f7` (class b). The later rc finalize-in-signal `744a9a3d8` was still ported as `a251c2ed8`.

## CARD-1619 migration

Cherry-pick `16ccb04ae` (from `749e1c7dc`) auto-merged the model snapshot (+13 ExitSignal hunks, identical). Follow-up `00ee6f4c3` renames the migration to `server/Migrations/20261031180000_AddSessionExitSignalEvidence.cs` so it sorts after CARD-1610. Designer is the merged snapshot. Up adds nullable integer `ExitSignal` on `RunAttempts`, `AgentSessions`, and `AgentIncidents`, and nullable `ExitStopIntentJson` `varchar(4096)` on `AgentSessions`. Down drops those four. No second migration. Schema tests in the Antiphon.Tests run included `TestSchemaSnapshotTests` and `Card1619ExitPersistenceTests` (`Migration_preserves_old_rows` included) and passed.

## Conflicts

Two hand conflicts, both `tests/test-execution-policy.json`, hash field only. Row bodies kept identical to rc, then the hash was recomputed.

1. `67f80cd7d` ← `db8a211f2` (top-up 45). Kept `Card1619ExitConsumerTests`, `Card1619ExitPersistenceTests`, `Card1619StopAttributionTests`. Hash `c65a202aa95b6623255f5dca94715843c70261b5fbb9c7add736e80fe3c799cc`.
2. `651eb516b` ← `1f3794102` (top-up 49). Kept `Card1619LinuxCustodyStopIntentTests`, `Card1619LinuxStopIntentTests`, `Card1619RunnerExitTests`, `Card1619UnixExitTests`, `Card1619LinuxHostExitTests`, `Card1619StopIntentTests`. Hash `9cd8d7281b04ee66287f1c2bf5104d1e385937cd4e34881032f9fbca3d438eea` (current file).

Everything else applied clean or git auto-merged with no conflict markers. Auto-merged files included `AgentControlService.cs`, `AgentSessionRuntime.cs`, `AgentSupervisorService.cs`, `CheckCompactionContinuationService.cs`, `AppDbContext.cs`, the EF snapshot on `16ccb04ae`, `docs/session-runtime-invariants.md`, `AgentTaskDispatcher.cs`, `AttentionService.cs`, and `docs/testing-and-build.md` on several 47–51 commits. No `_PtyHostNativeProps` restoration.

## Land graph

`scripts/audit-land-verification-graph.ps1 -Check` on the final tree. Exit 0, phase=pass2, 34.79s. Did not run `-Update`. Allow-list not widened.

- Previous recorded digest `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`, count 68.
- Approved digest `E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806`, count 69 (GitFixture, graph commit `78fee7464`).

Not-built probes, expected: `docs/investigations/evidence/card-0462/Reproduce.csproj`, `scripts/fixtures/nightly/c1375-probe/Probe.csproj`, `scripts/fixtures/nightly/c487-probe/Probe.csproj`, `tests/Antiphon.TimeoutAudit.Tests/Antiphon.TimeoutAudit.Tests.csproj`, `tools/Antiphon.NativeLinuxMigrationAudit/Antiphon.NativeLinuxMigrationAudit.csproj`.

## Tests (all on `6799e6d40`, slot builds, `UseAppHost=false`, `TEMP=/tmp`)

| Run | Result |
|---|---|
| Antiphon.Tests `RcRosterRepairTests` | 3 passed |
| Antiphon.Tests roster + `NativeLinuxPolicyWitnessTests` | 6 passed. `Policy_rows_require_pinned_attribute` passed |
| Antiphon.Tests series class filter | 106 passed, 0 failed, 16m 41s. Includes schema/migration, census C1404/C1577/C1584/C1410/G2865, `LandDeliveryWaitRoutingTests` (4) |
| Antiphon.Tests method filter (C849 and related) | 44 passed, 0 failed, 2m 19s |
| PtyHost.Tests series | 34 passed, 15 skipped (Windows), 0 failed. Policy witness passed. `C1614_Linger_fixture_is_admitted_before_native_start` passed. `C1614_Windows_tracked_linger_releases_job` skipped |
| SessionRunner bundle (stop-intent, custody, runner exit, C1022, policy witness) | First two runs: 27 passed, 1 failed, 5 skipped. Failure `Card1619RunnerExitTests.Exit_while_detached_survives_adoption` (`AdoptOrphanedHostsAsync` expected 1, was 0) at 969ms then 527ms. Same filter rerun: 28 passed, 5 skipped, 0 failed, 31s 697ms |
| SessionRunner isolated method | 1 passed |
| SessionRunner exit class only | 6 passed |
| SessionRunner stop-intent + custody + exit | 17 passed, 2 skipped |
| Agents.Pty.Tests Unix exit, Claude dialog, policy witness, Windows exit | 14 passed, 2 skipped, 0 failed, 1.2s. Windows methods skipped: `Requires native Windows ConPTY`. `Policy_rows_require_pinned_attribute` passed |
| E2E `LandDeliveryFixtureProbeTests` | 6 passed, 0 failed, 6s |

`git diff --stat e0d49d221 HEAD` is empty for `src/Antiphon.SessionRunner`, `src/Antiphon.PtyHost`, `src/Antiphon.PtyHost.Client`, `src/Antiphon.PtyHost.Protocol`, `src/Antiphon.SessionRunner.Contracts`, `src/Antiphon.Agents.Pty`, and `tests/Antiphon.SessionRunner.Tests/Card1619RunnerExitTests.cs`. The intermittent adoption failure is on rc-identical bits. In the failing order the method ran after `Kill_all_records_each_owned_generation`. Smaller filters that ran it earlier passed, and a later run of the original 33-test filter passed. No assertion was loosened and no product change was made for it.

Named and not re-run: `Custody_residue_counts_subdirectories_only` (brief’s known inherited red). The brief’s `Policy_rows` legacy-digest `8397984d` did not reproduce; that test passed in Antiphon.Tests, PtyHost.Tests, SessionRunner.Tests, and Agents.Pty.Tests.

Not run, Windows-only: `DirectoryLinkFixtureWindowsTests` (C1061 unit coverage is in the 106), `WorktreeRetirementDeliveryE2ETests` (`LandDeliveryFixture.InitializeAsync` asserts Windows), `Card1619WindowsExitTests` (skipped on Linux), C1022 modern-host proofs (skipped on Linux), custody tests that require the entrypoint namespace marker (skipped).

## merge-tree

After `git fetch origin master`: `origin/master` still `5aea2708fca4e196ae1915b6101c5287cb8c7cea`. `git merge-tree --write-tree origin/master HEAD` at `6799e6d40` exited 0, tree `3aa4a06e592cd81192d2eaa3756d0e97f473b02a`. Master was not merged into the task branch. CARD-1545 and CARD-1609 are not on `origin/master`.

Measured overlap with in-flight work, not a land conflict today:

- CARD-1609 round 14 `origin/feat/card-task-a9634687` `0efeaa25ac5cd4228da6f2d25ab7fe971361892d`: `docs/docker-stack.md`, `scripts/c590-remote.sh`, `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`, `tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs`.
- CARD-1545 union of remote tips whose subject contains 1545 and that are not ancestors of master: `docs/session-runtime-invariants.md`, `server/Application/Services/AgentTaskDispatcher.cs`, `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`, `tests/Antiphon.Tests/Antiphon.Tests.csproj`.

## Series lines

- Claude composer: ported, tip `1f650acc5`.
- CARD-1619: ported, migration `20261031180000`, tip `2dcb7e33d`.
- C448 + release-gate + top-up 44: tip `c52bcf0d5`.
- Top-up 45: tip `7a2d45c2e`, policy hash recomputed.
- Top-up 46: tip `78fee7464`, land graph approved.
- Top-up 47: tip `ddfb2d806`.
- Top-up 48: tip `6fe770030`.
- Top-up 49: tip `eec5980ae`, policy hash recomputed.
- Top-up 50: tip `e28223252` (rc object `fc064a682`, not the brief’s `fc064a684`).
- Top-up 51: tip `6799e6d40`.
