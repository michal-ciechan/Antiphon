# Review 2a2cd323: skip the empty terminal exit enrichment read

Clean. The empty-set skip does not change enrichment. Frozen budgets stay frozen. The named guard still fails the six reconciler scans when the read is put back.

Reviewed branch `feat/card-task-d670e3ae`. Executed tree `4357ac1b08bad84b278d7b8ba8bd64a007d7dda6` (fix). Branch tip `65862ec4b603786ff5eb79befdb00ca05455f570` adds only `.antiphon/task-d670e3ae.md`. Product and test blobs match. Base `ead93feb00fcaf8bf31018599437f17bee8e934c`.

## Equivalence

`exitedIds` is the key array of `runnerSessions` whose `Status` equals `"Exited"` (ordinal). The query is that same array, plus `Status` Stopped/Failed (4, 5), plus the owner `RunnerId` arm. An empty array cannot return a row. The length guard and the id predicate are the same set. The status and runner filters only shrink a non-empty set, so a non-empty Exited snapshot still queries.

`ToListAsync` of zero rows returns an empty `List<AgentSession>` and attaches nothing. `[]` in that ternary is the same empty list. `terminal` is not null and is only enumerated. `exitedById` is built before the read and is not indexed when the loop does not run. Later live-session handling builds its own dictionary from the runner list and does not read `terminal`.

A runner status that is Exited only under ignore-case is absent from this id set. The old query would not have returned it either. The live path's ignore-case Exited handling is unchanged.

## Overlap with 41da14612

`git merge-tree --write-tree --merge-base ead93feb00fcaf8bf31018599437f17bee8e934c` in both orders writes `213348529d4eb787de897ffd9c201f4f031a24be` (exit 0). The merged service has the skip and `CommitPassAsync` (empty-live, closed-nothing, and the helper). Those hunks do not touch the enrichment query. `CommitPassAsync` saves tracker changes. An empty enrichment read attaches nothing, so skipping it leaves the same tracker state. A non-empty set still queries, and `SessionExitEvidence.Apply` still mutates before the commit.

## Verification

Host flag is `--timeout 45m` (this test host has no `--total-timeout`). Builds and runs used `scripts/build-slot.ps1`, native output, `UseAppHost=false`, `TEMP=/tmp`.

- Base `ead93feb0`, `C1149_C1150_Statement_budgets`: 14 total, 6 failed, 8 passed. Each failure is +1: inside-grace-absent-scan 4/5, runner-unknown-close-scan 5/6, empty-healthy-scan 5/6, repeated-discovery-after-delivery 6/7, running-recovery-existing-brief 70/71, running-recovery-missing-brief 65/66. The two recovery SQL dumps contain `Id = ANY (@__exitedIds_0) AND Status IN (4, 5) AND RunnerId IS NULL`.
- Tip class `DelegationDispatchRecoveryBoundaryTests`: 382 passed, 0 failed, 0 skipped, 1m58s. The same recovery dumps contain no `exitedIds`.
- Neighbours `Card1619ExitPersistenceTests` 9, `SessionReconciliationServiceTests` 71, `StandingBootStatementBudgetTests` 11: 91 passed, 0 failed.
- Mutant, skip removed and the named assertion kept, then restored: the same 6 cases fail with `terminal exit enrichment read on an empty Exited set`. 8 passed. Source restored; worktree clean.

## Land

`scripts/audit-land-verification-graph.ps1 -Check` approved. Digest `D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE`, count 69. `-Update` was not run. Evaluator built `-c Release` through build-slot to the native output and passed as `-ToolPath`.

`check-evidence-diff.ps1` base..`65862ec4b`: commits=3, entries=1, violations=0.

`merge-tree --write-tree` exit 0 onto `origin/rc/2026-11-01` (`ead93feb0`, fetched) and `origin/master` (`4518ce64b70c13a4c03a375d9630152342ee732c`, fetched). Master's `SessionReconciliationService.cs` blob matches the base. rc merge of the fix equals the fix tree `fa3881ddfd65753a054d16bbf83f47c142aa6d52`. rc merge of the tip equals `2119bedea165ab95cda745ff860e05922ffbac8b`. Master merge of the fix is `9082c403137e2f581f4c0109458abc45f5beb6a1` and contains the skip.

No OS-selected branch. Linux reproduced the same six +1 deltas. Lock plus reload of the reconciled row stays two statements; merging them would change the locking design and is out of scope. `due-absent-hold` returns before the named regex and measures a dispatcher sweep; it passed at the base. No positive controls in this repair.

--- review evidence ---
subjectTaskId: d670e3ae-df77-410e-8a10-19c70c4719ba
reviewedSourceSha: 65862ec4b603786ff5eb79befdb00ca05455f570
reviewedSourceClean: true
ordinaryScopeCompleted: Full
