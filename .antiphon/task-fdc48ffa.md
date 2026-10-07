# CARD-1082 follow-up F1 Final Review (task fdc48ffa)

Verdict: clean. No regression of existing behaviour, no new reachable fail-open, no new crash vector in the settlement path. The whole ordinary scope for F1 was rerun at the reviewed SHA as one checkpoint-tool run: 9 rows, 101 results, 0 failed, 0 skipped, one build, every slot granted, source clean, build provenance verified. All four plan controls (PC-1..PC-4) went red under a method-scoped mutation and green again on the restored source. No Backlog card filed.

## Identity

- subjectTaskId (Code owner): `22c90b2b-31d3-4b76-b682-4e30ac8caeef`, ref `refs/heads/feat/card-task-22c90b2b`
- Reviewed SHA: `319236fefba040315fad7e0ca6d9152e895dc5d6` (remote ref equals it; this review branch `feat/card-task-fdc48ffa` started at the same SHA)
- Base: `cc7b7101a2034c40499e4e1e56206bdde78e7164` = merge-base with origin/master = origin/master at review time (it has not moved). Dispatch base `c0448cf8f84f3f3a981ed939bf26ed2c9f2a50f3`.
- Range `cc7b7101a..319236fef`: **1 commit, 4 files** (+101/-13): `SettlementSyncDebtPolicy.cs`, `SettlementSyncDebtPolicyTests.cs`, `RunnerBranchContractDocumentationTests.cs` (+1 sentence), `docs/orchestration-loop.md` (+1 sentence). The brief's "2 commits, 6 files" counts from the dispatch base and includes master's own `cc7b7101a` (the CARD-1108/1124 plan commit); the F1 change is the single commit above. `AgentTaskReplyService.cs` is untouched (F2 owns it).
- `git merge-tree --write-tree origin/master HEAD`: exit 0, tree `d8cb0042877424a3a6acbe93efa115d91233fa08`, zero conflicting hunks (trivial: HEAD is a fast-forward of origin/master). The CARD-1108/1124 S1 Code task edits parking files; this slice touches none of them.
- Platform read: `GET /api/runner-defaults` globalRunnerId=server2; `GET /api/session-runners` lists desktop (windows), server2 (linux), server2-temp (linux). Run here on the Linux mirror; no `-Runner`/`-Platform` pin needed for the land or Mutation.
- Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef c0448cf8f -HeadRef HEAD` -> `EVIDENCE result commits=2 entries=1 violations=0`; `-BaseRef cc7b7101a` -> `commits=1 entries=0 violations=0`.

## Checkpoint rerun (one checkpoint-tool run, hand-made manifest, `--serial`)

Tool built via `scripts/build-slot.ps1` (lease 51d41c5d, 6 s) into `bin-c1082furv-drv/`. Manifest in the scratchpad (outside the repo): CP-1 is the plan's CP-1 (same project and filter); CP-2..CP-9 are the regression classes and the registry guard named in the brief. `import --plan` validated 9 rows. `start --plan ... --after F1 --serial --expected-source-sha 319236fef --keep-outputs --total-timeout 120m`, run `20261007-071307-b53d`. `validate --evidence report.json --expected-source-sha 319236fef` -> `CHECKPOINT SOURCE VALID source=319236fefba040315fad7e0ca6d9152e895dc5d6 rows=9`. Wall 8m42s, build 123 s.

```
CHECKPOINT CP-1 commit=319236fefba040315fad7e0ca6d9152e895dc5d6 build=ok filter=/*/*/SettlementSyncDebtPolicyTests/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-fdc48ffa/.antiphon/checkpoints/20261007-071307-b53d/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=319236fefba040315fad7e0ca6d9152e895dc5d6 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=319236fefba040315fad7e0ca6d9152e895dc5d6 build=reused filter=/*/*/RunnerTaskSettlementTests/* executed=31 passed=31 failed=0 skipped=0 trx=/work/worktrees/task-fdc48ffa/.antiphon/checkpoints/20261007-071307-b53d/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=319236fefba040315fad7e0ca6d9152e895dc5d6 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=319236fefba040315fad7e0ca6d9152e895dc5d6 build=reused filter=/*/*/RunnerSettlementSyncTests/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/task-fdc48ffa/.antiphon/checkpoints/20261007-071307-b53d/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=319236fefba040315fad7e0ca6d9152e895dc5d6 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=319236fefba040315fad7e0ca6d9152e895dc5d6 build=reused filter=/*/*/SettlementSyncRecoveryTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-fdc48ffa/.antiphon/checkpoints/20261007-071307-b53d/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=319236fefba040315fad7e0ca6d9152e895dc5d6 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=319236fefba040315fad7e0ca6d9152e895dc5d6 build=reused filter=/*/*/ReviewEvidenceResettlementTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-fdc48ffa/.antiphon/checkpoints/20261007-071307-b53d/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=319236fefba040315fad7e0ca6d9152e895dc5d6 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=319236fefba040315fad7e0ca6d9152e895dc5d6 build=reused filter=/*/*/BlockedTaskSyncRecoveryTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-fdc48ffa/.antiphon/checkpoints/20261007-071307-b53d/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=319236fefba040315fad7e0ca6d9152e895dc5d6 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=319236fefba040315fad7e0ca6d9152e895dc5d6 build=reused filter=/*/*/RunnerCompletionProgressTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-fdc48ffa/.antiphon/checkpoints/20261007-071307-b53d/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=319236fefba040315fad7e0ca6d9152e895dc5d6 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=319236fefba040315fad7e0ca6d9152e895dc5d6 build=reused filter=/*/*/RunnerBranchContractDocumentationTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-fdc48ffa/.antiphon/checkpoints/20261007-071307-b53d/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=319236fefba040315fad7e0ca6d9152e895dc5d6 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=319236fefba040315fad7e0ca6d9152e895dc5d6 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-fdc48ffa/.antiphon/checkpoints/20261007-071307-b53d/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=319236fefba040315fad7e0ca6d9152e895dc5d6 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
```

TRX rosters equal the expected sets exactly. CP-1 = 30: the 21 existing results (AttentionWarns 4, BlockReasonForPending 4, DisabledSetting 1, LeaseBusyWithObservedTip 1, MissingOrShortTip 2, OtherReasonsAndStates 7, PendingEvidenceRecordsObserved 1, PendingWarningSaysSyncedLater 1) plus the 9 new (ForeignFullRef 3, PendingCodeWithoutEvidence 2, PendingTextHelpersRefuse 2, PendingCodeIndeterminateCarriesReason 2). CP-2 = 31 results over 27 methods. CP-8 includes `C1082_settlement_sync_debt_is_documented`. CP-9 = `Registry_matches_compiled_metadata` (1) + SlowTestTripwireTests (2).

Statement counts, re-measured through the production path (the `DbCommandInterceptor` on the services' `AppDbContext`), read from the CP-2 TRX stdout: `path=synchronized statements=30 debtSql=0`, `path=pending-review statements=40 debtSql=1`, `path=kill-switch statements=31 debtSql=0`. Same figures as the S5 record and the Code report. F1 adds no SQL (the diff contains no DbContext access).

## Mutation probes (check d; method-scoped, each restored; PCs stay pending for Mutation)

Each: sed edit on `SettlementSyncDebtPolicy.cs`, `scripts/build-slot.ps1 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1082furv-mut/ --property:UseAppHost=false`, `scripts/build-slot.ps1 -- dotnet <dll> --treenode-filter <method*> --report-trx`, `git checkout -- <file>`, `git diff --quiet`.

| PC | Mutation applied | Filter | Result |
|---|---|---|---|
| PC-1 | dropped the `FullRef` predicate from `Eligible` | `C1113_ForeignFullRefIsNeverPending*` | total 3, failed 3 (lease c7e89387 / d9b98815) |
| PC-2 | `evidence is null` arm returns null | `C1113_PendingCodeWithoutEvidenceBlocksWithLeaseReason*` | total 2, failed 1 (Code arm), passed 1 (Review arm, expected) (4252d2e5 / afe30421) |
| PC-3 | Indeterminate arm returns the constant lease reason | `C1119_PendingCodeIndeterminateCarriesEvaluatedReason*` | total 2, failed 1 (reason-present arm), passed 1 (null-reason arm, expected) (27996cfa / d8cd8b54) |
| PC-4 | removed both `RequirePending(result)` calls | `C1113_PendingTextHelpersRefuseANonPendingResult*` | total 2, failed 2 (0954296a / 80828647) |

Restored source rebuilt (lease 024c57ee) and `(C1113_*)|(C1119_*)|(C1082_BlockReasonForPendingDependsOnRoleAndProgress*)` ran 13/13 passed (lease 8ad7fff8). `git status --porcelain` empty at `319236fef` afterwards. PC-2's second detector in the plan, `RunnerTaskSettlementTests.C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked`, does not exist at this SHA: it is F2's deliverable, so the F1 Mutation stage can run PC-2 only with the policy detector until F2 lands (zero executions on the F2 filter is not red).

## Code report audit (owner 22c90b2b)

- Plan `### Checkpoints` has one F1 row, CP-1 (`/*/*/SettlementSyncDebtPolicyTests/*`, exact 30, Min 30). The Code's CP-1 line: executed=30 passed=30, build=ok, slot=granted, sourceState=clean, buildSource=verified, run `20261007-064624-ad8b` exit 0 with `--rows CP-1`. Matches the table. The Code also noted its earlier `--after F1` run selected the After=all rows too and discarded it as a certificate: correct.
- Unlisted runs, each with a reason: the eight regression classes (brief-required, leased build `bin-c1082fu-reg/` then `dotnet <dll>` serially), `RunnerBranchContractDocumentationTests` 5/5 (the F1 doc pin), the two statement-count tests (re-measure, `/tmp/c1082fu-sql/sql.trx`, leased), and the four PC probes (red then restored, `cmp` against the good file). No driver reported outside the slot gate.
- The full Code report `.antiphon/task-22c90b2b.md` was never committed (the branch has 4 files; `.antiphon/` is ignored); it still exists on this mirror at `/work/worktrees/task-22c90b2b/.antiphon/task-22c90b2b.md` and will vanish with that mirror. Nothing in this review depends on it: every count above was re-executed.
- New tests can go red: every one asserts a concrete return value, a `ShouldBeSameAs` identity, or a thrown `ArgumentException`; none is a self-compare or a constant check, and the probes above prove each detects its own guard. No assertion was weakened or deleted: the single changed existing assertion is row 4 of `C1082_BlockReasonForPendingDependsOnRoleAndProgress`, whose expected value moves from the constant lease reason to the evidence reason the fixture already passes (`"progress_read_other"`), which is strictly more specific (plan V-3). The fixture `Task()` id change (`Guid.Empty` -> `abcd1234-...`) is the plan's required F1 fixture; `C1082_AttentionWarnsOnHeldAndStalePendingDebt` sets its own id and is unaffected (4/4 green).

## Adversarial checks (a)-(e)

(a) Pending is never Confirmed: `RemoteSettlementSyncResult.Confirmed` (`RemoteSettlementSyncDtos.cs:62`) still excludes Pending and is not in the diff. Land, review-evidence binding, the CARD-1065 parking invariants, the S4b sweep and the S5 settlement wiring are untouched: the diff is the policy file, one doc sentence and tests; `AgentTaskReplyService.cs`, `SettlementSyncRecoveryService.cs`, `BlockedTaskSyncRecovery*`, `TerminalRunnerSeatReleaseService.cs`, `AgentTaskLand*` have no hunks. The normal path is unchanged by construction: `Classify` on a real lease-busy result (own ref, full SHA) still mints Pending (V-1 green; CP-2's `C1082_ReviewLeaseBusySettlesSucceededWithPendingSyncDebt`, `C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending`, `C1082_CodeLeaseBusyEqualTipFailsOnItsOwnVerdict`, `C1082_PendingDebtRecoversThroughTheDispatcherSweep` green, 40 statements, 1 debt insert).

(b) Fail-closed, enumerated at the policy and traced to settlement:
- Pending + Code + null evidence: `BlockReason` now returns `runner_sync_lease_busy`; `SettleAsync` sets `remoteBlock`, status Blocked, NextStage Decide, handoff "Runner sync blocked (runner_sync_lease_busy)"; the debt insert (`:1152`) and both Pending text sites require `remoteBlock is null`, so no row, no "synced later", no `desktop-sync=pending`. That is the pre-CARD-1082 shape (a lease-busy Unavailable result blocked the same way). The only producer of null evidence under Pending is `TryClassifyCompletedWithoutProgressAsync` returning null when `TaskCompletionProgressService` is absent from DI (`:3499-3511`), the fail-open CARD-1113 item 2 named; the settlement-level pin (V-6) is F2's.
- Pending + Code + Indeterminate: blocked, now with `evidence.Reason ?? runner_sync_lease_busy`. The unfetched-Code case (`SourceDescends` null or no local object) yields `Unknown(LeaseBusy)` (`TaskCompletionProgressService.cs:233`), so its handoff text is unchanged; `baseline_lineage_broken` / `primary_log_unavailable` now name the real cause. Nothing keys on the string: no reader in `scripts/`, `client/src`, parking or seat release matches `runner_sync_lease_busy` (grep).
- Debt cases still create exactly one row: owned-ref Pending for a Code task with ProgressObserved or NoAttributedProgress evidence (Succeeded or Failed on its own verdict), and owned-ref Pending for any non-Code role (Review/Plan/etc., evidence or not). Both go through the unchanged `remoteBlock is null && Pending && Succeeded|Failed` guard with the unique `(TaskId, Attempt)` index. Indeterminate was never a debt case (it blocked before F1 too).
- The `FullRef` pin refuses: another task's `refs/heads/feat/card-task-xxxxxxxx`, any `refs/tags/...` or `refs/remotes/...`, a bare branch name without `refs/heads/`, and null. No real task can carry one of these on a lease-busy result: both lease-busy producers (`RemoteWorkspaceService.cs:417` observe-busy, `:465` acquire-busy) pass the `fullRef` that `SyncCoreAsync` derives from `OwnedBranch(task.Id)` (`:220-221`), after refusing `BranchMismatch` when `task.WorktreeBranch` or the baseline's `FullRef` differs (`:222-223`, `:231-232`); `PrepareAsync` (`TaskCompletionProgressService.cs:116-144`) derives the same ref for its own Unavailable fallbacks and never returns LeaseBusy; the review-evidence repair path runs the same `SyncCoreAsync`. A task dispatched on a non-owned branch is Refused/branch_mismatch before and after F1, so no legitimate lease-busy settlement is lost.

(c) The throw-unless-Pending helpers have exactly two production callers, both in `AgentTaskReplyService.SettleAsync`, and both guard first: `remoteBlock is null && remote.Result is { State: Pending } pendingSync && Status is Succeeded or Failed` at `:967-975` and the same shape at `:1040-1043`. `Classify` (`:1998`) and `BlockReason` (`:2050`) do not throw. The throw is therefore unreachable. Were it reached: no `SaveChangesAsync` runs between the settle entry (`:840`), `ClassifyReportAsync` (`:3574-3673`, no save) and the first helper call; the only persistence is `PersistDeliverThenReleaseAsync` (`:2284`/`:2293`) after both sites. An exception propagates to `OnTurnEndLockedAsync`'s catch (`:120-125`), which logs and requests a recovery sweep; the task row stays Working for re-settlement. No half-settled task is possible.

(d) See the mutation table: each new test fails under a mutation of the production line it names, including the null-evidence block (PC-2) and the owned-ref check (PC-1).

(e) Statement counts 30/40/31 re-measured in CP-2 through the production path; no new per-settlement statement. Docs: the added sentence "The policy accepts only the task's own owned ref as the observed tip, and a Code report with no progress evidence under Pending stays Blocked." is true at this SHA (pin-wise at the policy; F2 adds the settlement-level pin) and is pinned by `LoopSentences` (CP-8 green). The F2 sentence "The Blocked warning says Runner sync pending and then reply." is still present and still true until F2.

## Disclosures (no Backlog card: nothing outside the plan's own sequencing)

- PC-2's second detector is F2's test; the F1 Mutation stage can only run the policy detector until F2 lands.
- The Code report's full detail file is uncommitted on a retiring mirror (same shape the S5 review disclosed); the brief's "2 commits, 6 files" is the dispatch-base count.
- Under Pending + Code + Indeterminate with a non-lease reason, the Blocked warning (state word from `remote.Result.State`, `:945`) now reads "Runner sync pending: baseline_lineage_broken ..." until F2 (CARD-1133) switches the word to "unavailable"; the handoff and verdict are unchanged.

## Cleanup

`clean --run 20261007-071307-b53d` deleted every kept `bin-c1082furv/`; 27 `bin-c1082furv-mut` directories and `tools/Antiphon.Checkpoints/bin-c1082furv-drv` removed by root-confined loops; `find -name 'bin-c1082furv*'` empty; all four mutations reverted; tree clean at `319236fef`. Evidence (TRX, report.md/json, executor.log) stays ignored under `.antiphon/checkpoints/20261007-071307-b53d/`.

--- review evidence ---
subjectTaskId: 22c90b2b-31d3-4b76-b682-4e30ac8caeef
reviewedSourceSha: 319236fefba040315fad7e0ca6d9152e895dc5d6
reviewedSourceClean: true
ordinaryScopeCompleted: Full
