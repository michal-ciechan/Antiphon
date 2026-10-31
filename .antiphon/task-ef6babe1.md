# Final Review ef6babe1: master port of rc top-ups 44-51

Subject: `feat/card-task-a584c99e` at `b1c6889fa7fb7ca420e71f0c7ac20a38f49a6477` (port tip `6799e6d40`, 127 commits on master `5aea2708fca4e196ae1915b6101c5287cb8c7cea`). rc reviewed against: `origin/rc/2026-10-27` `e0d49d221513b467f8c853f47587e688e04255e2`. Runner: server2 (Linux). Read-only on the subject branch.

**Verdict: one defect (D-1, migration id). Everything else is sound: completeness, policy hash, series tests, mutants, land graph, evidence diff and merge-tree.**

## Defects

### D-1: the re-created CARD-1619 migration id breaks every rc-migrated database (release-blocking)

- **Where:** `server/Migrations/20261031180000_AddSessionExitSignalEvidence.Designer.cs:15` (`[Migration("20261031180000_AddSessionExitSignalEvidence")]`) and the paired `.cs`, from `00ee6f4c31a7e4790e44c33af443081971aca382`. rc ships the same `Up`/`Down` (byte-identical `.cs`) as `20261030141651_AddSessionExitSignalEvidence`.
- **Failure:** take any database that has run an rc build at or after `749e1c7dc` (rc tip `e0d49d221` included). Its `__EFMigrationsHistory` holds `20261030141651_AddSessionExitSignalEvidence`, and its four columns already exist. When the new rc cut from master starts, `Program.cs:1088` `Database.Migrate()` treats `20261031180000` as pending. Its first `AddColumn` fails with `PostgresException 42701: column "ExitSignal" of relation "RunAttempts" already exists`. EF 9 rolls back the whole pending batch, so CARD-1610's three migrations are not applied either, and the server does not start.
  - **Proven** with a scratch probe (never committed). Unmutated port tree, isolated test schema: migrate to `20261028170715`, apply the rc `Up` SQL and the rc history row, then call `MigrateAsync()`. Result: `sqlstate=42701`, and the applied tail stays at `...170715, 20261030141651`.
  - An idempotent `Up` alone would not be enough. `scripts/deploy-local.ps1:133-141` would still report `database-only migrations: 20261030141651_AddSessionExitSignalEvidence` and print `DEPLOY VERDICT: failed`.
- **Live ledger today:**
  - The desktop server is at rc `8edda9996`, which predates `749e1c7dc`. Its DB tail is `20261028170715_AddAgentSessionCodexTokenCount`, and there are no `ExitSignal`/`ExitStopIntentJson` columns (read-only query).
  - server2 runs no Postgres.
  - So no standing DB is broken yet. But product hosts track the rc tip, so any AppHost restart onto `rc/2026-10-27` `e0d49d221` before the new cut arms the failure. Any qualification or other DB that ran rc ≥ `749e1c7dc` is already armed.
- **Why it matters:** this is the "fix that is wrong on master" case. The new rc would fail at startup, not just in tests. The rename was unnecessary: EF Core applies every unapplied migration in id order, whatever its position relative to already-applied ones. A master DB that already ran CARD-1610's `...214946` would still apply `...141651`.
- **Fix (Code, owner task a584c99e):**
  - Restore the rc id `20261030141651_AddSessionExitSignalEvidence` for both files. Keep the current merged-snapshot Designer body, which is the correct post-1610 target model. The `.cs` body is unchanged.
  - Add a migration test that covers both directions:
    - (a) an rc-ledger DB (rc chain through `...141651`) migrates to master head with no error, and the history equals the source set;
    - (b) a master-ledger DB (CARD-1610 through `...214946`, without `...141651`) applies `...141651` out of order and gets all four nullable columns.
  - Re-run `Card1619ExitPersistenceTests`, `TestSchemaSnapshotTests` and `dotnet ef migrations has-pending-model-changes`.
  - Do it before the port lands: no master DB has applied `20261031180000` yet, so this is the cheap moment.

## Verified, no defect

1. **Completeness.**
   - `git cherry -v origin/master origin/rc/2026-10-27` gives 1123 rows: 978 `-` and 145 `+`. Against the port tip, 22 remain unmatched, and every one is accounted for:
     - **Hand conflicts (2):** `db8a211f2` and `1f3794102`. Policy rows are identical to rc (see 3).
     - **Already on master in another form (5), each confirmed:**
       - `3cb215cf3d`→`909e99ad4`, `9c271b7e1b`→`c2ebb2a47`, `80b3a59c40`→`698bf1b73`: every rc +/- line is present in the master commit (the master commits are supersets).
       - `125c720b97`→`583a36b0b`: the extra rc lines are a plan doc, already on master byte-identical.
       - `b226c226b5`→`cbe3e6b42`: the extra rc lines are only the C1379 debug doc. `LandRestartReplayTests.cs` is byte-identical rc vs port.
       - Every code and test file those five touch is byte-identical rc vs port, except `AgentTaskDispatcher`, `AgentTaskService` and `AttentionService.Seats`, which master changed later.
     - **rc bookkeeping (8 + 2):** 8 `.antiphon/task-*.md` records, plus the cancelled pair `59deddd30`/`97756db4e`, whose net effect is nothing.
     - **rc-only investigation docs (5 commits, 4 files):** see A-1.
   - **Tree diff** `rc..port` over the 180 files the 145 rc-only commits touched:
     - Every code file's net change `master..port` equals, line for line, the sum of the ported rc commits' changes for that file (0 lines only in the port, 0 only in rc). This covers `AgentControlService`, `AgentSessionRuntime`, `AgentSupervisorService`, `AgentTaskDispatcher`, `AttentionService`, `CheckCompactionContinuationService`, `AppDbContext` and the EF snapshot.
     - The two docs (`session-runtime-invariants.md`, `testing-and-build.md`) differ only by moved line pairs that cancel out.
     - So every remaining rc-vs-port difference is master-only state, and no hunk is unexplained.
     - Files absent from the port: the rc migration pair (D-1), the 7 `.antiphon` records and the 4 investigation docs.
2. **Migration.**
   - The port's migration sorts after CARD-1610's `20261030214946`.
   - `dotnet ef migrations has-pending-model-changes`: "No changes have been made to the model since the last migration."
   - `TestSchemaSnapshotTests` 3/3 and `Card1619ExitPersistenceTests` (including `Migration_preserves_old_rows_and_nullable_contract`, a real Up from the previous migration on Postgres) pass.
   - The defect is the id, not the content.
3. **Policy hash.**
   - Owner tooling (`Read-NightlyExecutionPolicy` / `Get-NightlyPolicyHash`) recomputes `9cd8d7281b04ee66287f1c2bf5104d1e385937cd4e34881032f9fbca3d438eea`, equal to the stated hash.
   - The rows equal rc plus one master-only row (`test-qualification-graph`, from `0e2c171c1`, not on rc).
   - `Policy_rows_require_pinned_attribute` and `NativeLinuxPolicyWitnessTests` pass, not skipped, in all four suites (Antiphon.Tests, SessionRunner.Tests, PtyHost.Tests, Agents.Pty.Tests).
   - On `8397984d`: the earlier legacy-digest red was the policy json not matching its recorded digest. This port recomputes the hash after the row changes, so that red cannot show up at this tip. It is neither fixed nor hidden by this port: it is a different input, and it would come back only if the json changed without a hash recompute.
4. **Series tests.** All from one build per project, Linux.

   | Run | Total | Passed | Failed | Skipped | Time | Notes |
   |---|---|---|---|---|---|---|
   | Antiphon.Tests run1 (23 classes) | 187 | 185 | 0 | 2 | 22m18s | CARD-1619 consumer/persistence/policy/attribution, SessionGenerationExit, RepositoryMutationLease (C448), ReleaseGatePublication, RcManifest×3, RcRosterRepair, C1514LandingSplitBudget (C1404/C1577/C1584/C1410/G2865), TestSchemaSnapshot, LandDeliveryWaitRouting, PosixHarnessTool, QualificationCheckoutSource, VerifyDindRunner, CheckpointGitEncoding, CheckpointRepeatHost, PlanCoverageCensus, RunScheduler, NativeLinuxPolicyWitness(+Mutation). Skips are C448 V13/V28 (Windows) |
   | Antiphon.Tests run2 (series methods in big classes) | 48 | 48 | 0 | 0 | | RemoteScriptContract C849_*/C1192/Custody_*, AgentSessionLaunchFailure A_ready_gate_kill*, ScriptHarnessProcessContract C1061/Post_exit/Reader_fault/Timeout/Windows_resolution. **`Custody_residue_counts_subdirectories_only` passed** |
   | PtyHost.Tests | 52 | 37 | 0 | 15 | | Skips are Windows-only |
   | Agents.Pty.Tests | 16 | 14 | 0 | 2 | | Skips are Windows ConPTY |
   | SessionRunner bundle (Card1619LinuxCustodyStopIntent, Card1619LinuxStopIntent, Card1619RunnerExit, C1022BackendLaunch, NativeLinuxPolicyWitness), run 3× | 33 each | 27 / 28 / 28 | 1 / 0 / 0 | 5 each | | Run 1 failure: `Exit_while_detached_survives_adoption`, `AdoptOrphanedHostsAsync` expected 1, was 0 (`Card1619RunnerExitTests.cs:38`) |

   - **Adoption red:** a real intermittent failure, now 3 reds in 5 bundle runs between the worker and me. `git diff rc..port` is empty for SessionRunner, PtyHost(+Client/Protocol), Contracts and `tests/Antiphon.SessionRunner.Tests`, so it is equally present on rc and not introduced by the port. Zero adoptions means either the probe saw the detached host dead, or `AdoptAsync` threw and was swallowed as "treat as dead" (`SessionRunnerRuntime.cs:2096-2125`, NullLogger in the fixture). The worker's ordering note (it fails after `Kill_all_records_each_owned_generation`) suggests a cross-test kill or linger interaction. It is worth a Debug card (A-2). It does not block this port.
5. **Mutants.** Each went red and was restored green; restored-tree run 62 total, 60 passed, 0 failed, 2 Windows skips.

   | Mutant | Change | Red |
   |---|---|---|
   | M1a release-gate | `scripts/test-release-gate.ps1:1628` pattern reverted to `'token'` | `C599_NotesCredentialScan` 2/13 failed (identifier false positives) |
   | M1b release-gate | boundary class over-tightened to `[a-z0-9_]` | 4/13 failed (`GITHUB_TOKEN`/`access_token` no longer rejected) |
   | M2 C448 lease | `RepositoryChildJournal.Remove` retry disabled | `C448_JournalRemovalRetriesTransientDeleteFailuresBeforeRelease` ×4 and `C448_JournalRemovalFailureKeepsFenceAndRecordsReason` ×4 red |
   | M3 migration | `ExitStopIntentJson` `AddColumn` removed from Up | 8 `Card1619ExitPersistenceTests` red with 42703. `TestSchemaSnapshotTests` stayed green, as expected: it compares migrations to migrations |
   | M4 CARD-1619 chain | `SessionExitPolicy.IsCleanStop` treats an unrequested signal as clean | `Describe_exit_evidence` ×3 and `Native_exit_reaches_failure_and_attention_text` ×2 red |

6. **Land graph and merges.**
   - Land graph `-Check` (evaluator built through the slot): `approved`, count 69, digest `E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806`, phase=pass2. `-Update` was not run.
   - `check-evidence-diff` `5aea2708f..b1c6889fa`: commits=127, violations=0.
   - After `git fetch`, `origin/master` is still `5aea2708f` (it already includes CARD-1622). `merge-tree --write-tree origin/master b1c6889fa` is clean, tree `9a401d9c44faf929583a34dda1db0e6f7472c3b0`.
   - **CARD-1609** round 14 (`0efeaa25a`): merge-tree with the port is clean. Shared files: `docs/docker-stack.md`, `scripts/c590-remote.sh`, `RemoteScriptContractTests.cs`, `WindowsUnitSkipRoster.cs`.
   - **CARD-1545** tips: shared files are `SessionRunnerRuntime.cs` and `Antiphon.Tests.csproj`. The 1545 tips' own add/add plan conflicts already exist against master (base `cbe3e6b42`) and are not caused by this port.

## Advisories

- **A-1:** four rc-only investigation records are not on master:
  - `docs/investigations/2026-10-30-c1379-land-restart-replay-debug.md`
  - `docs/investigations/2026-10-30-c514-arm-unconfirmed-debug.md`
  - `docs/investigations/2026-10-30-reportbudget-posix-git-path-debug.md`
  - `docs/investigations/2026-10-30-reportbudget-posix-git-path-sweep.md`

  They are docs only, not linked from any tracked file on either side, and carry no behaviour. The port report's classification does not name them explicitly. Port them during the D-1 round if master should keep the evidence history; it is optional.
- **A-2:** file a Debug card for the intermittent `Card1619RunnerExitTests.Exit_while_detached_survives_adoption` (the same on rc and master).
- **A-3:** the release-gate harness case run directly with `-ResultsDirectory` prints 13 rows. Run without it, the harness exits 1 with a null-path error. That is a harness ergonomics issue, not the port.

## Process notes

- **Builds:** one slot lease per build, `UseAppHost=false`, `TEMP=/tmp`.
  - One deviation: the M3 mutant rebuild reused the mutant tree's `bin-rvmA` directory incrementally instead of a fresh name. The mutant witnesses are unaffected; restored green came from a fresh `bin-rvmC`.
  - `dotnet ef has-pending-model-changes` built into default `bin/` folders.
- **Cleanup:** all `bin-rv*`, `bin-land-audit` and `bin/` output and the mutant worktree were removed with literal paths.
- **Not run:**
  - Windows-only proofs (`Card1619WindowsExitTests`, `DirectoryLinkFixtureWindowsTests`, C1022 modern host, C448 V13/V28, `WorktreeRetirementDeliveryE2ETests`). These stay owed to the new rc's Windows run.
  - The E2E `LandDeliveryFixtureProbeTests` (worker 6/6) was not re-run.
  - The whole Unit lane was not run (brief-scoped short review), so `ordinaryScopeCompleted` is Interim.
- **PCs:** all PCs stay pending for SourceLanding Mutation.
