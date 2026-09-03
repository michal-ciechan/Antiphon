# CARD-0095 — Orchestrator panel stats: combine card and delegation accounting

**Date:** 2026-09-03 (Plan pass; no production code changed)
**Card:** CARD-0095 — "Orchestrator panel stats (cost, retry, active runtime) read zero"
**Builds on:** CARD-0092 S1+S2, shipped on 2026-09-02

## Verdict

CARD-0092 already fixed the user's reported **"cards"** counter and **active runtime** as a
side effect of its deliberately narrow Running Sessions change. There is no literal `Cards` label
on `/orchestrator`: the relevant controls are the **Running** summary metric and the **Running
Sessions** table. They now union card-spawn sessions with the session of every active non-specialist
delegate task, and `ActiveRuntimeSeconds` is already calculated from that union. Do not reopen or
redesign that projection for this card.

The remaining defect is genuine: the **Cost** and **Tokens** metrics aggregate only
`RunAttempt.TokenUsage` (the card-spawn ledger), and **Retry Queue** only reads `RetrySchedule`
(the card-spawn automatic-retry queue). Delegate work instead accrues cumulative usage on
`AgentTask`, and its explicit retry/escalate operation requeues that same task row with an
incremented `Attempt`; it creates no `RetrySchedule`. Implement one mixed-source state projection
in `OrchestratorService`, then teach the existing retry table to render both sources. No migration,
new endpoint, session backfill, or change to the card auto-dispatch controls is needed.

## Ground truth and accounting contract

| Concern | Card-spawn path | Delegate-task path | State result |
|---|---|---|---|
| Historical tokens/cost | `RunAttempt.TokenUsage` | cumulative `AgentTask.TokensIn`, `TokensOut`, `CostUsd` | sum both ledgers once |
| Active runtime | active `AgentSession` | active task's `AgentSession` | already `running.Sum(RuntimeSeconds)` after CARD-0092 |
| Retry waiting to run | due, unexhausted `RetrySchedule` | `AgentTask` is `Queued` after a prior attempt | one mixed Retry Queue |

`AgentTask.CostUsd` is a task's own cumulative spend across its attempts, not its subtree roll-up;
sum task rows directly. Do not use `AgentTaskCostWalk`, which intentionally adds descendants and
would double-count a parent/child delegation family. Similarly, do not add `RunAttempt` usage to a
task's usage merely because a card-spawned agent later dispatched that task: those are separate
agent executions and separate spending records.

The existing Tokens metric means `TokensIn + TokensOut`. Preserve that API/UI contract for this
card by adding the task rows' same two fields; do not silently fold `CacheReadTokens` or
`CacheCreationTokens` into a display that has never included them. `CostUsd` already reflects the
task pricing model, including its cache accounting.

## Decisions

1. **Keep one state endpoint and one service-owned aggregation.** In
   `OrchestratorService.GetStateAsync`, introduce a small private accounting projection/helper that
   obtains no-tracking aggregate rows from scoped `RunAttempts` and scoped `AgentTasks`, then adds
   their zero-safe `TokensIn`, `TokensOut`, and `CostUsd` values into the existing
   `OrchestratorStateTotalsDto`. Do not publish two adjacent UI totals or extract a shared service:
   no other consumer needs this combined contract today.

2. **Add task scope with the same meaning as CARD-0092.** Add an `ApplyScope` overload for
   `IQueryable<AgentTask>`. With an internal-repository prefix, a card-bound task is included only
   when its card's board/project is in scope; a cardless task remains visible/global, matching the
   current `AgentSession` rule. The accounting query includes every task status and role: Cost is
   actual spend, so a settled task, a blocked task, or a system-specialist task must not disappear.
   The running and retry projections retain their operator-work exclusions separately.

3. **Define the delegate half of Retry Queue precisely.** Include a delegate row only when
   `Status == Queued`, `Attempt > 1`, and `AgentTaskRoles.NotSpecialist` holds. It represents an
   actual retry or escalation reattempt that is waiting for the task dispatcher. A fresh queued
   task (`Attempt == 1`) is ordinary pipeline backlog, not a retry; it remains on the Delegations
   tab. `Blocked`, `Dispatched`, and `Working` retries have respectively become a human-decision
   or active-work concern and leave this queue. Keep the current card half exactly as it is: only
   unexhausted retry schedules that are due now belong in this view.

4. **Make the retry DTO an explicit union, not a fake card.** Replace the card-required
   `OrchestratorRetryQueueItemDto` shape with a source discriminator (`Card` / `Delegation`) and
   nullable card/board references plus an optional compact task reference. Both sources retain
   `AttemptCount`, `MaxAttempts`, and `LastError`; card rows retain `NextRetryAt`/`LastAttemptAt`,
   while delegate rows identify the task and say that it is queued for the task dispatcher rather
   than inventing a retry time. A bound delegate shows its card; an unbound one shows its task title
   and short id and an em dash for board. This is the smallest honest representation of two retry
   mechanisms with different scheduling models.

5. **Do not change Running or runtime logic.** Preserve CARD-0092's active session/task join,
   task-family ordering, source counters, caption, pause/tick semantics, and
   `running.Sum(RuntimeSeconds)`. Add regression coverage to make the already-fixed runtime
   dependency explicit rather than adding a second runtime aggregate.

## Implementation slices

### S1 — Mixed-source server state projection

Change `server/Application/Services/OrchestratorService.cs` and
`server/Application/Dtos/OrchestratorDtos.cs`.

- Add the scoped `AgentTask` query and a private, zero-safe combined accounting result; aggregate
  `RunAttempt.TokenUsage` and every task row independently, then populate the existing totals DTO
  from their sum.
- Project due `RetrySchedule` rows and queued delegate reattempt rows into one ordered collection
  of the new discriminated retry DTO. Preserve card retry ordering by next retry time, then append
  task rows in deterministic task-dispatch order (oldest queue creation first), rather than relying
  on database incidental order.
- Reuse the task-card scope lookup pattern already used for Running Sessions; never join a
  delegate task through `AgentSession.CardId`, which remains null by design.
- Keep the state endpoint route and all running-session DTO fields compatible; only the retry-item
  contract changes. No EF model or migration changes.

### S2 — Types and truthful retry-table rendering

Change `client/src/api/orchestrator.ts`,
`client/src/features/orchestrator/OrchestratorPanel.tsx`, and
`client/src/features/orchestrator/OrchestratorPanel.test.tsx`.

- Mirror the discriminated retry type exactly. Render a source chip (`Card` / `Task`) in the Retry
  Queue table; render the existing card identifier/title/board layout for card rows and the task
  title plus short-id link to `?tab=delegations&task=...` for delegate rows.
- Change the time/status cell by source: card rows keep formatted `Next Retry`; task rows say
  `Queued for task dispatcher`. Preserve the card retry error display; use the retained task
  failure reason when present, otherwise an em dash.
- Keep the metric label **Retry Queue**, but make its count exactly the mixed table length. Do not
  add a `Cards` metric or alter **Running**, **Tokens**, **Cost**, or **Active Runtime** rendering.

### S3 — Focused regression coverage

Extend `tests/Antiphon.Tests/Application/OrchestratorStateProjectionTests.cs` and the existing
panel Vitest fixture/tests.

- Seed one card run attempt plus task rows in open, blocked, settled, and specialist states with
  distinct usage. Assert totals are the exact ledger sum, specialist/settled task spend is retained,
  and nested tasks are not rolled up twice.
- Under `InternalTrackerRepositoryPathPrefix`, seed an out-of-scope card run, an out-of-scope
  card-bound task, and an unbound task. Assert only the unbound task contributes from that set,
  matching the deployed running-session scope rule.
- Seed one due card `RetrySchedule`, one queued delegate task at attempt two, a first-attempt
  queued task, a working reattempt, and a Check reattempt. Assert only the first two appear, their
  source-specific fields are correct, and `RetryQueueLength == RetryQueue.Count`.
- Assert a mixed card/delegate running fixture's `ActiveRuntimeSeconds` equals the sum of the rows
  returned by the already-existing union, so a future accounting edit cannot regress CARD-0092's
  runtime fix.
- Update the panel fixture with both retry sources; assert the source chips, task-delegations link,
  source-specific status text, card compatibility, and combined metric count.

## Verification

Run the narrow server class, then the panel Vitest file:

```powershell
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/*/OrchestratorStateProjectionTests/*"
pwsh -File scripts/test-client.ps1 OrchestratorPanel.test
```

The server test must prove mixed totals, retry inclusion/exclusion, scope, and runtime; the client
test must prove both retry shapes render. No E2E rebuild is required for this targeted API/component
change unless an E2E test is added.

## Risks kept out of scope

- `RetrySchedule` is an automatic card-retry scheduler; an `AgentTask` retry is immediately
  dispatchable work. The discriminated row is required so the UI does not imply both are scheduled
  for a time.
- The total is historical spend within the existing scope, as it was for `RunAttempt` before this
  card. It is not a per-day budget, per-card subtree total, or a replacement for `/cost-summary`.
- The normal delegate pipeline queue and human-decision surfaces already live on Delegations/Home.
  Folding every fresh queued task into Retry Queue would duplicate and misname that work.
