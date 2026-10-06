# CARD-1076 S4 Final Review (task a2e3ae38)

Outcome: clean. No defect found. The S4 slice (D-6 queue reasons `repositoryLease` and `remotePrep`) reran green at the branch tip, the Code report's CP-5 lines match the plan's `### Checkpoints` row and the 70-to-66 floor explanation is verified, and an unknown `queueReason` cannot throw or break any client view, list or count while S5 (D-7) has not landed.

subjectTaskId `be6f17af-0abd-4ca6-b5a7-0975cd520da1` (Code owner, carried as the landing owner). Ref `refs/heads/feat/card-task-be6f17af` at `b6c34475a7e65b81231ef8565a1255e5210b6e41`; tested source `776fa4464d0b8f76ecfce20d83b84ff380e50c6c` differs from the tip only by `.antiphon/task-be6f17af.md` (the Code report), so the rerun was done at the tip. Base `23dc94ea54d547eb23a8e0d4ba6400995d1bd8f5`. Review worktree `/work/worktrees/task-a2e3ae38`, branch `feat/card-task-a2e3ae38`, clean tree (`git status --porcelain` empty) before and after the runs. Plan `docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md`.

## Fresh runs (one build, four separate drivers, serial)

One isolated build through `scripts/run-checkpoint.ps1` (slot `6919fa54-157d-4639-96d1-0acbc1d61778`, waited 0s, held 181s, `-maxcpucount:6`) to `bin-c1076-review/`, then three `-NoBuild` rows on that output, each under its own slot (waited 0s). All 28 `bin-c1076-review` directories were deleted afterwards (confined to the worktree root, 0 remaining).

```
CHECKPOINT CP-5 commit=b6c34475a7e65b81231ef8565a1255e5210b6e41 build=ok filter=/*/*/AgentTaskPipelineStatusTests/* executed=66 passed=66 failed=0 skipped=0 trx=/work/worktrees/task-a2e3ae38/.antiphon/c1076-s4-review/CP-5-20261006-102132-6cd0/run.trx slot=granted waited=0s dirty=0 source=b6c34475a7e65b81231ef8565a1255e5210b6e41 sourceState=clean buildSource=verified
CHECKPOINT C1076-S4-ENDPOINT commit=b6c34475a7e65b81231ef8565a1255e5210b6e41 build=reused filter=/*/*/AgentTaskPipelineEndpointTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-a2e3ae38/.antiphon/c1076-s4-review/C1076-S4-ENDPOINT-20261006-102546-5f56/run.trx slot=granted waited=0s dirty=0 source=b6c34475a7e65b81231ef8565a1255e5210b6e41 sourceState=clean buildSource=verified
CHECKPOINT C1076-S4-LEDGER commit=b6c34475a7e65b81231ef8565a1255e5210b6e41 build=reused filter=/*/*/DispatchHoldLedgerTests/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-a2e3ae38/.antiphon/c1076-s4-review/C1076-S4-LEDGER-20261006-102640-29ea/run.trx slot=granted waited=0s dirty=0 source=b6c34475a7e65b81231ef8565a1255e5210b6e41 sourceState=clean buildSource=verified
CHECKPOINT C1076-S4-GUARD commit=b6c34475a7e65b81231ef8565a1255e5210b6e41 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-a2e3ae38/.antiphon/c1076-s4-review/C1076-S4-GUARD-20261006-102647-9f74/run.trx slot=granted waited=0s dirty=0 source=b6c34475a7e65b81231ef8565a1255e5210b6e41 sourceState=clean buildSource=verified
```

Executed rosters (from the drivers' EXECUTED lines): CP-5 contains `C1076_a_dispatcher_lease_hold_is_the_queued_reason_and_names_the_owner` x4 (V-9), `C1076_remote_preparation_in_flight_or_backing_off_is_the_queued_reason` x3 (V-10), `C1076_a_task_waiting_for_its_push_turn_names_the_pushing_task` x1 (V-11) and the 58 existing results including `queued_work_is_awaiting_dispatch_unless_a_live_lease_holds_it`, `a_lease_hold_outranks_the_concurrency_cap`, `queued_work_is_concurrency_cap_when_in_flight_fills_the_cap`, `a_dated_pin_is_the_queued_reason_when_nothing_else_holds_the_task` (R-4). The ledger run contains `C1076_LeaseOwnerTaskId_parses_only_the_owner_sentence` (V-12) plus the 22 existing (R-6). The guard run is `Registry_matches_compiled_metadata`, `unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not`, `allowlist_file_exists_and_has_spawn_lane_entries`.

## Code report against the plan's Checkpoints table

- CP-5 row present; filter `/*/*/AgentTaskPipelineStatusTests/*`, floor exact 66 / min 66, V-9, V-10, V-11, R-4. Code's receipt at `776fa4464` is 66/66 with `slot=granted`, `sourceState=clean`, `buildSource=verified`. Matches.
- Floor change 70 to 66: verified. `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs` declares `public class AgentTaskPipelineEndpointTests` at line 948 beside the partial `AgentTaskPipelineStatusTests`; that class has exactly 4 `[Test]` methods, the CP-5 class filter cannot select it, and the separate endpoint run executed exactly 4 (66 + 4 = 70, the old count). The R-4 census edit (62 to 58 existing) is the same four. The plan edit is confined to those two rows.
- Ledger (23) and registry guard (3) are not plan rows for this slice; the Code report states the reason (same build, `-NoBuild`, the brief asked for them) and both carry SHA-validated receipts. The checkpoint-tool bootstrap build is the one unlisted build and is explained with its slot id. The red-first runs are declared dirty-tree runs, not receipts. Every receipted driver shows `slot=granted`.
- New tests can go red: each asserts a specific `QueueReason` constant and `HeldBy` task-id set or title; the Code report's red-first run shows 6 of 8 CP-5 arms failing before the implementation, and two CP-5 receipts record the push-turn test failing on a null `BehindTaskId` before the fix in `776fa4464`.
- Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef 23dc94ea5... -HeadRef b6c34475a...` printed `EVIDENCE result commits=4 entries=1 violations=0`. (The script takes `-BaseRef`/`-HeadRef`; `-ManualBase`/`-ManualHead` are only for the event-path form and return `ref_input` otherwise.)

## Code review (read-only)

- (a) Client under an unknown `queueReason`: no reachable throw. `client/src/api/agentTasks.ts:468` is a five-member union; `pipelineStageModel.ts:198` `compactQueueReason` and `homeTasksModel.ts:273` `queueReasonLine` switch on it with no default, so `repositoryLease`/`remotePrep` return `undefined` at runtime. The only consumers render that value as JSX text: `PipelineStagesPanel.tsx:177` `{row.right}` and `TaskCard.tsx:277` `{queue.line}`; React renders `undefined` as nothing. Counts use `stage.queued.length` (`pipelineStageModel.ts:100,116,133`); `pipelineRowFor`/`queueReasonFor` use `find` by task id; `QUEUE_REASON_LABEL` is indexed in production only by the literal `awaitingDispatch` (`homeTasksModel.ts:300`); no sort, filter, aria label or Record lookup keys on the reason. Server-emitted `hostBudget` already takes this exact path today, and the plan's D-7 records it.
- (b) Current stint only: `LoadQueuedDispatchHoldsAsync` takes the floor as the greatest `Dispatched.At` for the task and picks the latest `Held` with `At > floor` (then `Id` descending), the same rule as `AgentTaskDispatcher.LoadQueuedHoldIndexAsync` (`AgentTaskDispatcher.cs:1235-1252`). A lease Held row older than a later `Dispatched` row is ignored (V-9 `previous-stint` arm passes).
- (c) `heldBy` content: the existing `AgentTaskPipelineHolderDto` (task id, short id, title). Titles come from one `AgentTasks` lookup over the collected holder ids; the lease sentence itself (purpose, acquired instant) is never emitted. No path, token or secret; the endpoint already exposes task titles fleet-wide.
- (d) Additive: `AgentTaskPipelineQueuedDto` is unchanged (`QueueReason` is a string), no DTO file in the diff; the new branch runs only when the row is still `awaitingDispatch` after shared checkout, sibling land and dated pin, and before host budget / concurrency cap, exactly the D-6 precedence; the existing reason tests still pass inside CP-5 and the endpoint contract test passes.
- (e) No Git and bounded cost: the service injects no `ILandingGit`; the new load is three no-tracking queries per `GetAsync` (Held+Dispatched events for the queued ids, pending Running land requests joined to their tasks, holder titles), independent of the number of queued rows; `ScopeResolver.KeyFor` is a string selection; `RemoteWorkspacePreparer.Progress` is a `ConcurrentDictionary` read.

## Disclosures (not defects under the verdict policy)

- Until S5 lands, a `repositoryLease` or `remotePrep` row shows an empty right cell in the pipeline panel and an empty queue line on the Up-next task card, the same as `hostBudget` today. A task that previously read `concurrencyCap`/`hostBudget` while also behind a lease or remote prep now reads the new reason (planned precedence), so its cell goes blank in the browser until D-7. S5 (D-7) is the planned fix; no new card filed because it would duplicate the S5 slice.
- `remotePrep` `heldBy` is the preparer's enqueue-time `BehindTaskId` (CARD-1093): with three or more waiters it can name a finished pusher or be empty. The API doc, the `Progress` doc and the test say so. Already tracked by CARD-1093.
- `ToQueued` calls `Progress(task.Id)` a second time after the title lookup; if the gate holder changes between the two calls the title falls back to an empty string. Cosmetic, no throw.
- `TraceHeldAsync` dedupes on the latest Held text across stints (`AgentTaskDispatcher.cs:1242-1247`, floor-free `LastHeld`), so a task re-queued and held again for the identical sentence gets no new row in the new stint and the projection falls back to `awaitingDispatch` (the pre-S4 reading). Dispatcher semantics shared with the ledger; an under-report, not a regression.

## Scope not covered, per the brief

CP-4, CP-6, CP-7, V-14, V-15, R-9, R-12, the client rendering (D-7) and the whole Unit lane belong to S5 or the final SHA and were not run. Every PC (PC-1..PC-12) stays pending for method-scoped SourceLanding Mutation. `restart: none` (projection, not activated).

--- review evidence ---
subjectTaskId: be6f17af-0abd-4ca6-b5a7-0975cd520da1
reviewedSourceSha: b6c34475a7e65b81231ef8565a1255e5210b6e41
reviewedSourceClean: true
ordinaryScopeCompleted: Full

--- next stage ---
next: land
handoff: Land CARD-1076 S4 Code owner be6f17af-0abd-4ca6-b5a7-0975cd520da1 (refs/heads/feat/card-task-be6f17af) with -ExpectedSourceSha b6c34475a7e65b81231ef8565a1255e5210b6e41; Final Review clean at that SHA (CP-5 66/66, endpoint 4/4, ledger 23/23, guard 3/3); unknown queueReason renders blank in the client until S5 (D-7); every PC stays pending for Mutation.
artifact: docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md
