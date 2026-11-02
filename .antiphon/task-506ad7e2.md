# CARD-1654 Code report (task 506ad7e2)

Branch `feat/card-task-506ad7e2`, base `5376ac529987b9e7b2019773cb4d784cba8007bf` (master), runner mirror `/work/worktrees/task-506ad7e2`.

## Commits

- `6b021d03a7a2a7851dc78a71a7b53fdf2bc83063` test: red witnesses + scaffold (settings knobs, result `Passes`/`TaskPrune`, `DataRetentionTaskCursor`; no behaviour change)
- `3e657e5f411a2a11e5326dc117dcf40c1bf0405c` test: assert behaviour before progress fields (base red names the defect); real exception chain
- `b9855bed7a0a6f6a96079fd5cbcdb802bf483690` fix: pass isolation + bounded batched task prune + per-sweep cap and cursor

## Mechanism (from code)

- `Program.cs` registers `AppDbContext` with `UseNpgsql(connectionString, o => { MigrationsAssembly; SetPostgresVersion(16,0) })`: no `CommandTimeout`, and the `DefaultConnection` string has no `Command Timeout=`. Every EF command runs under the Npgsql default `CommandTimeout` of 30 s.
- `DataRetentionService.PruneTasksAsync` ran one eligibility query over the whole `AgentTasks` table: `GROUP BY RootTaskId HAVING max(COALESCE(CompletedAt, CreatedAt)) < cutoff`. Each row gets about 12 correlated `NOT EXISTS` subqueries over `AgentTasks` members. CARD-1610 (`2bf104be4`, also on rc/2026-11-01) adds the `TaskSupervisionNotices` clause. One statement covers every tree, so its cost grows with the table.
- On a timeout, Npgsql throws `NpgsqlException("Exception while reading from stream")` with an inner `TimeoutException("Timeout during reading attempt")`. EF's default Npgsql execution strategy wraps it in `InvalidOperationException("An exception has been raised that is likely due to a transient failure...")`. The witness test reproduced this chain.
- `RunOnceAsync` called its passes in sequence and caught nothing, so the exception left after pass 8 (Tasks). `DataRetentionHostedService.SweepAsync` caught it and logged `LogWarning(ex, "Data retention sweep failed")`, so usage samples, residue rows and the audit archive were skipped every sweep. That matches the evidence: 7 passes ran 36 times while Tasks and everything after it ran once. pg_stat_statements does not record a cancelled statement.
- Not confirmed on the desktop. LoadOutboundTaskIdsAsync, which runs before the eligibility query, could also be slow. The log stack names the statement that failed.

## What the operator should look for (desktop server log; docs/logs.md)

- Before the fix: a `WRN` line `Data retention sweep failed` with the exception `System.InvalidOperationException: An exception has been raised that is likely due to a transient failure`, then `---> Npgsql.NpgsqlException: Exception while reading from stream`, then `---> System.TimeoutException: Timeout during reading attempt`. The stack goes through `DataRetentionService.PruneTasksAsync` (or `LoadOutboundTaskIdsAsync`). Expect one about every 6 h (`SweepHours`) since about 2026-10-26. The Postgres log may also show `canceling statement due to user request`, because Npgsql cancels the statement on timeout.
- After the fix: `Data retention sweep pass Tasks failed: System.InvalidOperationException > Npgsql.NpgsqlException > System.TimeoutException` if a batch still times out, plus `Data retention sweep completed with failed pass(es): Tasks`. The summary line now has `task prune N batch(es), scan complete True/False`.

## Change

- `RunOnceAsync`: each of 11 named passes runs under `RunPassAsync`. The passes are CheckCompactionRecoveries, ConfirmedPublicationIdentities, ChannelInboundPayloads, Sessions, Transcripts, QueuedMessages, ResolvedModalEpisodes, Tasks, SubscriptionUsageSamples, WorktreeResidueHeldRows and AuditArchive. A failure is logged as `Data retention sweep pass {Pass} failed: {ExceptionType}` with the type chain only, never the message, so no row data is logged. The pass is recorded as Failed, the change tracker is cleared, and the later passes run. A disabled pass is recorded as Skipped. Shutdown cancellation still propagates.
- `PruneTasksAsync` / `PruneTaskBatchesAsync`:
  - Candidate roots are `DISTINCT RootTaskId` with `COALESCE(CompletedAt, CreatedAt) < cutoff`, taken after the cursor in RootTaskId order, `TaskPruneBatchSize` at a time (default 100). Every row of an eligible tree meets this condition, so the candidate set contains every eligible tree.
  - Each batch runs the unchanged eligibility rules plus `batch.Contains(RootTaskId)`, then deletes children-first in its own transaction. Each statement runs under `TaskPruneBatchCommandTimeoutSeconds` (default 60), and the previous timeout is restored afterwards.
  - At most `TaskPruneMaxBatchesPerSweep` batches run per sweep (default 50). The singleton `DataRetentionTaskCursor` resumes the scan next sweep and resets when the scan ends.
  - Progress is reported in `DataRetentionSweepResult.TaskPrune`.
- Cutoffs and keep rules are unchanged. There is no new deletion scope and no migration. The diff does not touch AgentTaskDispatcher.cs, AgentTask.cs, CreateAgentTaskRequest or the slot pool.

## Witnesses (Antiphon.Tests, DataRetentionServiceTests, isolated schemas)

- `C1654_FailingPassLeavesLaterPassesRunning`: an interceptor throws Npgsql's timeout exception on the task eligibility query. Later passes must run, the result must record per-pass outcomes, and the log must name the pass and type without row data.
- `C1654_TaskBacklogPrunesAcrossBoundedBatches`: 7 stale trees plus a live tree and a fresh tree, batch size 3. No statement may name more than 3 roots, there must be at least 3 delete batches, every batch statement must use timeout 45, and the live and fresh trees must survive.
- `C1654_SweepCapStopsAndNextSweepResumes`: batch 2, cap 2. The first sweep deletes exactly the first 4 roots and stops with the cursor at roots[3]. The second sweep finishes and resets the cursor.

Each witness was red at `3e657e5f4`, where production code equals base plus unused scaffold (3/3 failed):
- The sweep aborted with InvalidOperationException.
- A statement named more than 3 roots.
- All 7 roots were deleted in one sweep.

At `b9855bed7`:
- DataRetentionServiceTests: 88/88 passed, 0 skipped. This includes the C1654 witnesses, the existing task-tree keep/delete tests and the CARD-1610 supervision retention cases.
- 45/45 passed, 0 skipped, across the retention-caller methods in ChannelOutboundRetentionTests, WorktreeResidueCandidateRowTests, CheckCompactionRecoveryPersistenceTests, SessionStateWarmupTests, SessionRuntimeAction{,Recovery,Deferral}Tests, AgentTaskLandReceiptTests, PostLandMutationDeliveryTests, WorkingTranscriptRecoveryTests and ChannelInboundRecoveryTests.

The test assertion edit in `b9855bed7` counts seeded roots per Guid list. The per-batch task-id list also carries each root row's own id, so this counts roots, not rows. Base still fails the edited form, because its delete names 7 or more roots.

## Gates

- Land graph `-Check`: approved, count 69, digest `D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE`. I built the evaluator to `bin-land-audit` and deleted it afterwards.
- `check-evidence-diff` base..HEAD: commits=3, violations=0.
- `merge-tree` onto origin/master `1859a1273`: clean.
- Onto origin/rc/2026-11-01 `10a826b9d`: the whole-branch merge-tree is clean. A per-commit cherry-pick chain of the 3 commits is also clean, and the result differs from rc only in the 5 touched files.

## Not done or pending

- The mutation PCs below are pending SourceLanding Mutation. The base red covers removing isolation and removing batching, but no dedicated mutant was run on the fix tip.
  - PC-1: `RunPassAsync` rethrows, so `C1654_FailingPass...` goes red.
  - PC-2: batch size ignored (whole table), so `C1654_TaskBacklog...` goes red.
  - PC-3: cap ignored, or the cursor not carried over, so `C1654_SweepCap...` goes red.
  - PC-4: the candidate prefilter is tightened, e.g. to `CreatedAt` only. Existing tree tests should catch this. Mutation should confirm.
- I did not run the whole Unit lane or full integration classes; the operator brief said minimal testing.
- Not confirmed on desktop data (no desktop log or EXPLAIN read).
- Decision for the operator: none required. Defaults are 100 roots / 60 s / 50 batches per sweep.
