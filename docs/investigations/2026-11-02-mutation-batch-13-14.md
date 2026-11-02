# Mutation sweep: batches 13 and 14 (CARD-1649, 1654, 1488/1497, 1482, 1655, 1657)

Task 0be34a95, server2/Linux, 2026-11-02. Mutated source: master
`a00962fd6a055c513fa567c3a930fb875660dbef` (the task worktree, HEAD = that SHA, clean tracked
source and index before and after every mutant).

**Not CARD-0478 evidence.** The orchestrator waived SourceLanding for this run: these changes
landed by Merge-role fast-forward pushes, so there is no land-v2 operation O or
`O.VerifiedSourceSha`. This is an ordinary-worktree mutation sweep. Its results are supplemental
and the post-land verification companions stay pending.

## Method (the waiver's one-build-per-mutant method)

1. Apply one literal before/after replacement (expected match count 1; pre and mutant SHA-256
   recorded).
2. Build `tests/Antiphon.Tests` once in its own build-slot lease (native output,
   `UseAppHost=false`), strictly one build at a time.
3. Run only the witness method(s) with one `--treenode-filter`.
4. Restore with `git checkout -- <file>` and prove `git diff --exit-code` and
   `git diff --cached --quiet` clean.

The `delegate.ps1` mutants needed no build: the tests read the script from
`DelegateScriptRunner.RepoRoot`, which is the worktree.

- **Baseline at L, before any mutant:** 18/18 witnesses green, plus the C1649 witness 1/1 green.
- **Restored at L, after all mutants:** a fresh build (Server dll `61ca82d0…` became `7e255ed2…`;
  Tests dll `3e20cb0b…` became `191bf600…`), then 20/20 green, including the survivor's witnesses.
- **Survivor build was fresh:** the PC-4 build rebuilt `Antiphon.Server.dll` at 19:22:52 after
  the mutation was applied at 19:20:51.

## Survivors

### S-1. CARD-1654 PC-4: candidate filter narrowed (coverage survivor)

- **Where:** `server/Application/Services/DataRetentionService.cs:504`,
  `_db.AgentTasks.Where(t => (t.CompletedAt ?? t.CreatedAt) < cutoff)`.
- **Mutant:** `t.CompletedAt < cutoff`. Rows with a null `CompletedAt` never nominate their tree
  as a candidate.
- **Result:** GREEN 6/6 on `C1654_*` (3), `A_fully_terminal_stale_tree_loses_every_row_and_its_events`
  and `A_tree_*` (2).
- **Why it survives:** every stale terminal row the tests seed has a `CompletedAt`. The only
  null-`CompletedAt` seeds are live (Working) rows, and their trees are ineligible anyway.
- **Not equivalent.** Several terminal transitions do not stamp `CompletedAt`:
  - Blocked Check to Succeeded remap: `AgentTaskCheckService.cs:568`.
  - Land-resume Blocked to Succeeded: `AgentTaskLandService.cs:173`.
  - Conflicted-helper Blocked to Succeeded: `AgentTaskReplyService.cs:2587`.

  Only 2 of the 11 Blocked writers set `CompletedAt` nearby. So a stale tree whose rows all have a
  null `CompletedAt`, such as a standalone remapped Check task, is eligible under the unchanged
  COALESCE rule. It would never be pruned if the prefilter lost its `CreatedAt` fallback.
- **Severity:** low. The failure is a retention leak (rows kept forever), never the deletion of
  kept data.
- **Recommended witness:** in `DataRetentionServiceTests`, seed a terminal (Succeeded) tree with
  `CreatedAt = DaysAgo(200)` and `CompletedAt = null` on every row. Seed one single-row tree and
  one root+child tree. Run `PruneTasksAsync` (and `RunOnceAsync` with `TaskPruneBatchSize = 1`)
  and assert both trees are deleted. Keep a fresh null-`CompletedAt` tree that must survive.
  Under the mutant this goes red; at L it is green.

## Results

| PC | Mutated file:line (at L) | Witness | Outcome | Evidence |
|---|---|---|---|---|
| 1654 PC-1 pass isolation removed | DataRetentionService.cs:117 catch filter catches only non-shutdown OCE | C1654_FailingPassLeavesLaterPassesRunning | RED 1/1 | InvalidOperationException escaped RunOnceAsync from PruneTaskBatchAsync :559 |
| 1654 PC-2 batching removed | DataRetentionService.cs:492 `batchSize = int.MaxValue` | C1654_TaskBacklogPrunesAcrossBoundedBatches | RED 1/1 | statement named more than 3 seeded roots |
| 1654 PC-3 per-sweep cap removed | DataRetentionService.cs:502 `while (tally.Batches < int.MaxValue)` | C1654_SweepCapStopsAndNextSweepResumes | RED 1/1 | all roots deleted in the first sweep |
| 1654 PC-3b cursor not carried | DataRetentionService.cs:528 `_ = batch[^1]` | C1654_SweepCapStopsAndNextSweepResumes | RED 1/1 | `cursor.After` null, expected roots[3] |
| 1654 PC-4 candidate filter narrowed | DataRetentionService.cs:504 `t.CompletedAt < cutoff` | C1654_* x3, A_fully_terminal_stale_tree, A_tree_* x2 | **GREEN 6/6: survivor S-1** | see S-1 |
| 1654 keep check bypassed (Review) | DataRetentionService.cs:560-561 `batch.Contains(..) \|\| !live..` | A_tree_*, C1654_TaskBacklog* | RED 2/3 | live-member root deleted; live tree count != 2. Window-rule companion green |
| 1649 attempt check removed | WorkspaceSlotReleaseService.cs:107 StuckPrefix uses SlotText | C1649_RecordReleaseStampsOnlyTheStuckAttemptOfATaskThatIsNotLive | RED 1/1 | 200 OK, expected 409 ("a new attempt is a live lease") |
| 1649 live-status guard removed | WorkspaceSlotReleaseService.cs:178 `&& false` | C1649_RecordRelease… | RED 1/1 | 200 OK, expected 409 ("a Dispatched task holds its lease") |
| 1649 token check removed | SessionRunnerEndpoints.cs:196 `&& false` | C1649_RecordRelease… | RED 1/1 | 200 OK, expected 400 ("never the operator token") |
| 1488 PC-1 Clear removed | TerminalTaskResidue.cs:28 `_ = waiters` | Cancel_clears_its_settlement_waiter…, A_process_exit_failure_retires… | RED 2/2 | waiter still registered |
| 1488 PC-2 clear by repository | RepositoryLeaseWaiters.cs:133 Clear empties the task's repository | Cancel_keeps_another_open_tasks_waiter…, A_process_exit_failure… | RED 2/2 | other task's waiter dropped |
| 1488 PC-3 nudge/wait-note cleanup removed | TerminalTaskResidue.cs:29 `queue = null` | Cancel_retires_the_unsent_nudge…, A_process_exit_failure… | RED 2/2 | nudge Pending, expected Canceled |
| 1497 PC-4 FailAsync call removed | AgentTaskDispatcher.cs:8079 RetireAsync removed | A_process_exit_failure_retires… | RED 1/4 (exact) | nudge Pending; the 3 Cancel_* companions green |
| 1488 wait-note prefix widened (Review) | TerminalTaskResidue.cs:47-48 any `task-wait:` key | Cancel_retires_…_only_for_that_task | RED 1/2 | sibling wait note Canceled. A_process_exit green: it seeds no sibling note |
| 1657 no-id guard removed | scripts/delegate.ps1:873 `if ($false)` | C1657_SuccessWithoutRequestIdIsUncertain (2 cases) | RED 2/2 | exit 0, "Queued land request ." |
| 1657 LandApiStub ContentLength reverted | tests/…/LandApiStub.cs:138 line removed | C495_TransportFailureAfterPostIsUncertain | RED 1/1 | output lacks "did not complete" |
| 1655 single-line print removed | scripts/delegate.ps1:614-615 removed | a_bind_400_* (5) | RED 1/5 | unknown_served_build: "could not be converted" split by the 80-column wrap; 4 companions green |
| 1482 forced rebase | AgentTaskLandingProtocol.cs:270 other committer + `--force-rebase` | C1388_SourceTreeChangeLandsWithResidue | RED 1/1 | VerifiedSourceSha f38507a6…, expected world.OwnerSha c77b37aa… |

Totals: 18 mutants run, 17 RED and 1 survivor. No mutant failed to compile and no
infrastructure failures occurred.

## Notes

- **CARD-1655 is Linux-specific.** The red depends on redirected pwsh wrapping at 80 columns on
  Linux, so the witness may not catch the mutant on Windows.
- **CARD-1497:** the process-exit witness does not seed a sibling wait note. Only the cancel
  witness pins the per-task wait-note identity. Both arms share `TerminalTaskResidue`, so this is
  not a gap today.
- **Evidence:** logs, TRX files and `restoration.json` are under the task worktree's untracked
  `.antiphon/mut/` (gitignored).
