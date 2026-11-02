# task-d670e3ae: C1149/C1150 statement budgets one over (split 8, run a074ff9a)

Outcome: a product defect, not a stale budget. Each of the six red cases issues one extra
statement, CARD-1619's terminal exit enrichment SELECT, even when the runner lists no Exited
session. It now skips that read when the set is empty. The frozen budgets are unchanged, and the
test names the read so the failure says which statement it is.

## Base reproduction (Linux, rc/2026-11-01 = ead93feb00fcaf8bf31018599437f17bee8e934c)

`C1149_C1150_Statement_budgets`: 14 total, 6 failed, 8 passed. These are the same six cases as
the Windows run, with the same counts: inside-grace-absent-scan 4->5, runner-unknown-close-scan
5->6, running-recovery-existing-brief 70->71, running-recovery-missing-brief 65->66,
repeated-discovery-after-delivery 6->7, empty-healthy-scan 5->6.

## The extra statement

The full SQL was dumped for all 14 cases. Statement 2 of every reconciler scan is:

    SELECT <AgentSessions columns> FROM "AgentSessions" AS a
    WHERE a."Id" = ANY (@__exitedIds_0) AND a."Status" IN (4, 5) AND a."RunnerId" IS NULL

- Table: AgentSessions. Predicate: Id in the runner's Exited snapshot ids, Status Stopped/Failed,
  and the owner runner matches.
- Issuer: `SessionReconciliationService.ReconcileSessionsAsync`, the terminal exit enrichment
  pass, at server/Application/Services/SessionReconciliationService.cs:200-206.
- Origin: CARD-1619, added in 69533f50e ("carry nullable exit evidence into server storage and
  projections") and reshaped in 16ccb04ae. `git log -S exitedIds.Contains` finds it.
- Count: this pattern appears exactly once in each of the six red cases. It appears zero times in
  the eight green cases, which are dispatcher ticks that do not run this pass.
- Why it is a defect: no fixture's runner lists an Exited session, so `exitedIds` is empty and the
  read cannot return a row. It is a guaranteed-empty round trip added to every scan. It is not a
  new read that has to happen, so the budget does not go up.
- It is not CARD-1610's supervision backfill. No AgentTasks query appears in these scans.

### The "four identical selects"

They are AgentSessions selects, not AgentTasks. They share a column list, but their WHERE clauses
differ:

1. Live select: `Status IN (1,2,3) AND RunnerId IS NULL`.
2. Terminal enrichment: the read above. This is the one removed.
3. Row lock: `(SELECT * ... WHERE "Id" = @p0 FOR UPDATE) LIMIT 2`.
4. Tracked reload: `Entry(session).ReloadAsync`, `WHERE Id = @p LIMIT 1`.

The fifth statement is the Agents `Status = 2` select (ReconcileAgentsAsync). Rows after it in
the dump are the test's own verify reads, which run after the count is taken.

Statements 3 and 4 could in principle be merged into one tracked FOR UPDATE query that refreshes
the entity. That is a locking design change outside this repair, so I did not make it. It is
reported here only.

## Commits on feat/card-task-d670e3ae

1. b0b37c14758f1459a90395ea8309653eaeb39f35 (test). `C1149_C1150_Statement_budgets` now asserts
   that no counted command matches `"Id" = ANY \(@\w*exitedIds\w*\)`, the terminal enrichment
   read, before it checks the total. Red against the unfixed product: all 6 cases fail with
   "terminal exit enrichment read on an empty Exited set", 8 pass.
2. 4357ac1b08bad84b278d7b8ba8bd64a007d7dda6 (fix). `var terminal = exitedIds.Length == 0 ? [] :
   await terminalQuery.ToListAsync(ct);`. The behaviour with a non-empty Exited set is unchanged.

## Verification at 4357ac1b

- `DelegationDispatchRecoveryBoundaryTests/*` (the whole class): 382 total, 382 passed, 0 failed,
  0 skipped. Duration 1m35s.
- Neighbours, filter `(Card1619ExitPersistenceTests*)|(SessionReconciliationServiceTests*)|(StandingBootStatementBudgetTests*)/*`:
  91 total, 91 passed, 0 failed. These classes cover CARD-1619 terminal enrichment with a real
  Exited snapshot, the reconciler, and the other statement-budget class.
- Mutant M1, a duplicated read (`_ = await liveSessionsQuery.AsNoTracking().ToListAsync(ct)`
  added after the live select): the same 6 cases go red, each +1 (4/5, 5/6, 5/6, 70/71, 65/66,
  6/7). Restored.
- Mutant M2, the guard removed and the named-statement assertion removed: the 6 cases are still
  red on the budget count alone, so the budgets guard on their own. Restored.
- Land graph `-Check` (evaluator built with `-c Release` through build-slot, passed as
  `-ToolPath`): approved, count=69, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE.
  `-Update` was not run.
- check-evidence-diff from ead93feb0 to HEAD: commits=2, violations=0.
- `merge-tree --write-tree` is clean onto origin/rc/2026-11-01 (ead93feb0) and onto origin/master
  (4518ce64b). Master's SessionReconciliationService.cs is identical to the base, so the same
  defect is on master.

All builds and runs used build-slot, the native output, UseAppHost=false and TEMP=/tmp. No
landing was done.

--- next stage ---
next: Review
handoff: diff-only Review of b0b37c14..4357ac1b (2 files, +11/-1). The budget numbers are a guard and stay unchanged; the fix only skips an empty-set read. Then a Merge-role fast-forward of rc/2026-11-01 and master. The next Windows run re-tests split 8.
artifact: .antiphon/task-d670e3ae.md
