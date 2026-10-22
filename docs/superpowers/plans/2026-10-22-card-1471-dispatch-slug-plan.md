# CARD-1471: persist the fresh pool agent only with the final claim

Source: `docs/investigations/2026-10-22-dispatch-save-failure.md` (Debug `c80c961f`). Live failure
2026-10-22 08:34:04.905Z: PostgreSQL 23505 on `IX_Agents_Slug`, key `task-f9acde3c`, at the
ready claim of a runner-bound Worktree task. Investigation base `a38e1e1bd`; the rc line
`rc/2026-10-20` (tip `ceb6e3d6b`, the live server build) carries the same code.

## Root cause

`AgentTaskDispatcher.ResolveAgentAsync` builds the fresh pool `Agent` (slug `task-<8hex>`) and
calls `_db.Agents.Add` before remote preparation. The preparation-only `SaveChangesAsync` + commit
(task still Queued, `AgentId` null, `HeldForRemotePrep`) therefore persists an unbound agent row.
When the mirror is ready the next claim finds `AgentId` null, builds a second agent with the same
slug and the final claim save fails with 23505. An ordinary tick runs the idle-pool sweep first and
deletes the unowned row, which masks the defect; a ready-waiter resume or a nested ready dispatch
does not sweep and fails.

## Slices

- **S1 (tests, committed first):** `tests/Antiphon.Tests/Application/PhoneHomeTaskDispatchProjectionTests.DispatchSave.cs`
  with V-1, V-2, V-3 and F-1 below. Red witness: CP-1 and CP-3 at the S1 commit through the
  checkpoint tool (`start --rows CP-1,CP-3`), expected red (the explicitly authorized extra run).
- **S2 (fix):** `ResolveAgentAsync` returns a fresh agent without tracking it; `DispatchOneAsync`
  adds that instance to the context beside the session row, after remote preparation has a mirror
  and before the task/session binding. Pinned agents keep their tracked identity. The generic
  dispatch catch appends `AgentService.DescribeDbFailure` plus the SQLSTATE to the persisted
  failure reason when a `DbUpdateException` wraps a `PostgresException` (constraint/table name
  only, never provider detail, parameters or connection strings).

Out of scope (per the investigation): any `DbUpdateException` retry, random slugs, deletion by
slug, weakening `IX_Agents_Slug`, production cleanup, settings or restarts.

## Verification design

- **V-1 `C_dispatch_save_preparation_has_no_unbound_agent`:** the fake peer records the mirror
  request and does not answer it; one real claim (`DispatchOneAsync` on a scoped dispatcher)
  returns `HeldForRemotePrep`; a separate `DbContext` sees the task Queued with its desktop
  worktree, a `TaskWorktreeMirrorBindings` preparation row, zero `Agents` rows, no
  `AgentId`/`AgentSessionId`, no session, no launch, no brief. The held reply is released and the
  preparer awaited in `finally`. Red at S1: one unbound pool agent row.
- **V-2 `C_dispatch_save_ready_claim_needs_no_pool_sweep`:** first claim, `preparer.WhenIdleAsync()`,
  then a fresh scope's dispatcher calls `DispatchOneAsync` directly (no `TickAsync`, no sweep).
  Asserts Dispatched, exactly one agent (bound, Running, `task-<short>`), one session on the
  requested runner/mirror, one launch, one brief, no Failed event. Red at S1: `DbUpdateException`
  with 23505 at the second claim.
- **V-3 `C_dispatch_save_repeated_pending_claim_stays_unbound`:** with the mirror still held, a
  second scope re-enters `DispatchOneAsync`; it returns `HeldForRemotePrep` and commits no agent,
  session or launch, and the task identity (`Id`, `Attempt`, null bindings) is unchanged. The
  mirror is then released, the preparer awaited and one claim succeeds; a further claim through the
  stale scope is `NotClaimed` and adds no second agent/session/launch. Red at S1: 23505 at the
  re-entry.
- **F-1 `C_dispatch_failure_reason_names_the_violated_constraint`:** a pre-existing non-pool agent
  already owns `task-<short>`; after preparation the claim fails on the unique index and the task's
  persisted `FailureReason` names `IX_Agents_Slug` and SQLSTATE 23505 and contains no connection
  string. Red at S1: the reason is only the generic EF message.
- **R-1..R-5:** the five existing exact methods of CP-2 (Claude/Grok runner projection, warm Codex
  reuse, card-bound dispatch, policy refusal before remote side effects) stay green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-dispatch-save-lifecycle/` | dispatch-agent-lifecycle | `/*/*/PhoneHomeTaskDispatchProjectionTests/C_dispatch_save_*` | V-1, V-2, V-3 | all listed, 0 failed | 3 | 8 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-dispatch-save-regression/` | dispatch-agent-regression | `/*/*/PhoneHomeTaskDispatchProjectionTests/(Runner_bound_claude_task_reaches_the_queue_projected*)\|(Runner_bound_grok_task_reaches_the_queue_projected*)\|(Signed_out_codex_runner_still_claims_and_reuses_a_warm_session*)\|(Card_bound_runner_worktree_task_dispatches*)\|(Refused_runner_task_leaves_no_pushed_branch_or_mirror*)` | R-1, R-2, R-3, R-4, R-5 | all listed, 0 failed | 5 | 8 |
| CP-3 | S1-S2 | CP-1 | dispatch-failure-reason | `/*/*/PhoneHomeTaskDispatchProjectionTests/C_dispatch_failure_reason_names_the_violated_constraint` | F-1 | all listed, 0 failed | 1 | 3 |

CP-1 is the card's closed row unchanged. CP-2 is the card's row with the same five methods; its
filter uses the `(Name*)` group form because TUnit's tree-node grammar runs zero tests for a
`(FullName)|(FullName)` group list (run `20261022-101841-f05b` executed 0 with the card's form; each
prefix is unique in the class, so the roster is still exactly five). CP-3 is added for the brief's failure-reason
requirement and reuses CP-1's build (same `After` group). No Unit lane, assembly or qualification
run; the brief asks for minimal testing.

### Cost and mutation

Ordinary green floor 19 minutes plus the S1 red pass (about 8 minutes). Post-land method-scoped
SourceLanding Mutation candidates, none executed here:

- **PC-1:** restore `_db.Agents.Add(agent)` inside `ResolveAgentAsync`; run only V-2; require the
  23505 red, restore, then V-2 green on the bound source.
- **PC-2:** drop the `DescribeDbFailure` suffix from the dispatch catch; run only F-1; require the
  `IX_Agents_Slug` assertion red, restore, then F-1 green.
