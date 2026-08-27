# CARD-0091 — A sweep that discards parked messages whose work is already over

**Date:** 2026-08-27
**Status:** planned (design only — nothing here is implemented)
**Card:** CARD-0091 (`6bd1a367-0e66-4efc-806e-91243b663a3a`), board Antiphon
**Scope:** the automated drain — a periodic sweep that moves a parked message to a terminal state once
the work it was queued for has ended. The "stop the pile at the source" half of the card turns out to
be **already shipped** (§1.4); this plan pins it with a test and builds nothing new for it.
**Builds on:** CARD-0055 (parking: `Pending` + `DeliveryAttempts >= MaxDeliveryAttempts`, excluded
from every automatic redelivery path), CARD-0082 (Supervision-origin cancel-not-park), CARD-0074/0132
(the superseded-check-note sweep — the nearest existing "cancel a queued row from evidence" path),
CARD-0117 (pool-reuse compact gated on the provider contract), CARD-0040 (the sweep + hosted-service
shape this copies), the Grok Debug findings on the card (task 4a670762, 2026-08-19).
**Model followed:** `docs/superpowers/plans/2026-08-27-card-0040-automatic-column-transitions-plan.md`.

## Verdict, in one screen

| Finding | Evidence | Consequence |
|---|---|---|
| **The pile is 34 rows, and it has been 34 for six days.** Newest parked row is `2026-08-21 21:09Z`; the newest parked `/compact` is `2026-08-20 01:36Z`. The "~1.6/day" growth was real between 08-19 and 08-21 and has stopped. | §1.3 (live query, 2026-08-27) | The sweep is a **drain**, and a guard against the next source; it is not racing an open tap. |
| **The source is already closed** — CARD-0117 (`6d39c98`, 2026-08-23) gates the reuse `/compact` on `ProviderContractCatalog.For(kind).RefocusCompact.State == Supported`, and Grok's is `Unknown` with `Command: null`. No Grok has been sent a reuse compact since. | §1.4 | Problem 2 needs a **regression pin**, not a fix. One test, one slice, Codex tier. |
| **"No open task on the session" covers 34 of 34.** Every row is `Origin = Delegation`, none is a completion note (`SourceTaskId` null) or channel-keyed; every owning task is terminal (31 Succeeded, 2 Canceled, 1 Failed); every session is `Stopped`/`Failed`; zero sessions have a Dispatched/Working/Blocked task. | §1.3 | The rule in §2.1 is exact for the live pile and needs no per-row task attribution to be safe. |
| **The manual dismiss path is one method**: Drop message → `DELETE /api/sessions/{id}/messages/{id}` → `SessionMessageQueueService.CancelAsync` → `Canceled`. Two sibling cancel paths already exist beside it with the same lock-and-recheck shape. | §1.1 | Add a third thin entry point over a shared core; the sweep never writes a row itself. |
| **`Canceled` is invisible once written** — the queue DTO, the attention feed and the stranded watchdog all filter to `Pending`; retention deletes `Canceled` after 30 days and never deletes `Pending`. | §1.2, §1.5 | Reuse `Canceled`. A reason column would decorate rows nothing displays; provenance goes in the log. The sweep is what finally hands these rows to retention. |
| The exact hosted-service shape exists (`CardWorkTransitionService` + `CardWorkTransitionHostedService`, 60 s `PeriodicTimer`). The two other candidate homes are wrong for this: the stranded watchdog is the *retry* path and is gated behind `RcWatch.Enabled`; the dispatcher tick is 5 s and task-domain. | §1.6, §2.3 | `ParkedMessageSweepService` + `ParkedMessageSweepHostedService` + `ParkedMessageSweepSettings`, registered beside CARD-0040's. |

---

## 1. What exists today (verified against the code and the live database, 2026-08-27)

### 1.1 The manual dismiss path, and its two automated siblings

- **Client.** `AttentionPanel.tsx:304` labels `CancelMessage` as `'Drop message'`; `:356` runs
  `cancelQueuedMessage(item.sessionId!, item.messageId!)`. `MobileHomePage.tsx:226/:290` is the same
  mutation under the same label. `SessionMessageQueue.tsx:166` (the per-session queue panel) calls
  it too. All three go through `client/src/api/sessions.ts:143`.
- **Endpoint.** `SessionEndpoints.cs:99` — `sessions.MapDelete("/{id:guid}/messages/{messageId:guid}")`
  → `queue.CancelAsync(id, messageId, ct)`.
- **Service.** `SessionMessageQueueService.CancelAsync` (`:355-380`): take the per-session
  `SemaphoreSlim` (`GetLock`), open a scoped `AppDbContext`, load the row (404 if missing), and only if
  `Status == Pending` set `Canceled` + `CanceledAt`; release; `PublishQueueChangedAsync` the rebuilt
  queue DTO. It does **not** look at `DeliveryAttempts` — a parked row is just a Pending row to it, which
  is why Drop already works on the pile.
- **Sibling 1** — `CancelPendingIfUntypedAsync` (`:448-475`, CARD-0074/0132): same lock, same scoped
  context, same re-check-under-lock, but additionally requires `DeliveryAttempts == 0` and returns
  `bool` instead of throwing. Called from `AgentTaskDispatcher.ReconcileSupersededChecksAsync`
  (`:1518-1571`), which is the closest existing thing to "a sweep that cancels queued rows from task
  evidence": it reads `AgentTasks` to decide, then hands the write to the queue service.
- **Sibling 2** — `CancelPendingSupervisionLockedAsync` (`:790-809`) and the CARD-0082
  cancel-not-park arm inside `HandleDeliveryFailureAsync` (`:2451-2456`): Supervision-origin rows are
  canceled instead of parked at the attempts cap, with a log line as the only record.
- The **precondition differs per caller and the core is the same**. Three copies of the
  lock/load/re-check/stamp/publish sequence already exist; a fourth copy is the thing to avoid (§2.2).

### 1.2 Where `Canceled` goes, and who reads it

- `QueuedMessageStatus` (`server/Domain/Enums/QueuedMessageStatus.cs`) is `Pending = 0`, `Sent = 1`,
  `Canceled = 2`. `Canceled`'s own doc comment already reads "user canceled, or the session ended" — it
  was never a single-source status.
- Six writers set `Canceled` today (`SessionMessageQueueService.cs:328, :369, :462, :800, :895, :2453`):
  the human Drop, the untyped-check cancel, CARD-0082's two arms, the superseded-check arm inside the
  flush, and the enqueue-time cancel of a Supervision compact into a working session. **No column
  records which.**
- **Nothing displays a `Canceled` row.** `BuildQueueDtoAsync` (`:2679`) filters
  `Status == Pending`; `AttentionService.BuildParkedMessageItemsAsync` (`:687-741`) filters
  `Status == Pending && DeliveryAttempts >= maxAttempts`; `FlushStrandedQueuesAsync` (`:612`) filters
  `Pending && DeliveryAttempts < maxAttempts`. A `Canceled` row is reachable only by SQL.
- **Retention.** `DataRetentionService.PruneQueuedMessagesAsync` (`:165-184`) deletes `Sent`/`Canceled`
  rows older than `Retention:QueuedMessageRetentionDays` (30, `server/appsettings.json:126`) and, by
  design, "never a Pending row (parked messages stay Pending by design)". That sentence is the whole
  reason the pile is permanent: parking is a Pending state, and Pending is exempt from retention.

### 1.3 The live pile, re-counted (2026-08-27, `antiphon-postgres`)

Query shape: parked rows joined to their session, with the owning task by `LEFT JOIN LATERAL` — the
newest `AgentTask` on the same `AgentSessionId` whose `DispatchedAt <= CreatedAt + 5 s` — and a
correlated count of open tasks (`Status IN (Dispatched, Working, Blocked)`) on the session.

| Slice | n |
|---|---|
| Parked (`Status = Pending`, `DeliveryAttempts >= 3`) | **34** |
| `Origin = Delegation` | 34 (no Ui / Channel / System / Check / Supervision) |
| `SourceTaskId` null (not a completion note) and `ConversationKey` null | 34 |
| Body is a reuse `/compact This session is being handed NEW, unrelated work…` | 27 (26 on Grok sessions, 1 on a Claude session) |
| Body is an actual brief (`[antiphon-task:…] role=…`) | 6 |
| Body is a `REFINEMENT` follow-up | 1 |
| Owning task `Succeeded` / `Canceled` / `Failed` | 31 / 2 / 1 — **0 open** |
| Sessions with any Dispatched/Working/Blocked task | **0** |
| Session `Failed` / `Stopped` / `Running` | 30 / 4 / **0** (the 6 "on idle pool Groks" from 08-19 have since been retired) |
| Oldest / newest `CreatedAt` | `08-17 17:18Z` / `08-21 21:09Z` |

Two things changed since the 08-19 debug pass and both matter to the design:

1. **The seven non-compact rows.** Five briefs parked between `08-20 05:18Z` and `08:38Z` — every one
   *before* CARD-0103 (`754d02f`, 2026-08-20 14:14) stopped charging attempts against a TUI that is
   still waking (its "deaf for 48–200 s" measurement is that same morning). Their tasks all reached a
   terminal state anyway (four Succeeded, one Failed), so the brief got in by some other path or the
   task was re-dispatched. The sixth is the CARD-0065 brief from 08-17 (task Canceled) and the seventh
   a 08-21 refinement (task Succeeded). None is a completion note. **The rule must not be
   "compact-shaped bodies only"** — it would leave these seven on the home screen.
2. **Nothing new since 08-21.** Whatever produced the 08-20 briefs (CARD-0103's shape) and the compacts
   (§1.4) is closed. The sweep's first pass discards 34 and every later pass will, on current evidence,
   discard nothing.

Non-parked Pending rows, for the record: 5 Ui-origin and 9 Delegation-origin at 0 attempts, 5
Delegation at 1 attempt. All outside the rule by construction (`DeliveryAttempts < max`).

### 1.4 The source is already closed — CARD-0117, 2026-08-23

`AgentTaskDispatcher.DeliverReuseMessagesAsync` (`:2611-2661`) looks the session's `AgentKind` up
**before** the compact and enqueues one only when
`ProviderContractCatalog.For(kind).RefocusCompact is { State: Supported, Command: not null }`
(`:2641-2644`; the comment cites CARD-0084 S1 / CARD-0117 S3). The catalog (`ProviderContractCatalog.cs`)
declares `RefocusCompact` **Supported with `"/compact"` for Claude only** (`:89-92`); Grok (`:154-157`)
is `Unknown` — "Never probed… Unknown behaves as Unsupported for enabling machinery" — with
`Command: null`; Codex, OpenCode and Raw likewise `Command: null`. When the compact is skipped the brief
itself is rendered with `refocus: true` (`FitBriefForTyping`, CARD-0117 D2). That commit landed
2026-08-23 11:32; the newest parked compact was enqueued 2026-08-20 01:36. The card's second problem
is therefore solved in production and only needs a pin (S2) so a catalog edit cannot quietly reopen it.

The one Claude-session compact (`fe4817c2`, 08-17, task `49f11348` Canceled) is a different shape —
Claude *does* support `/compact`; that row parked because the session died — and it is covered by the
same rule as everything else.

### 1.5 Where the rows render

Verified still accurate: `GET /api/attention` returns each as `AttentionKind.ParkedMessage`,
`AlertSeverity.Error` (Critical only when the owning agent is channel-bound — none are), `taskId`
null, actions `[SendNow, CancelMessage]` (`AttentionService.cs:721-738`). `MobileHomePage.tsx:188`
renders every item in Band 1 "Needs you · N"; `HomePage.tsx:361` renders only the count badge
("Needs attention (N)", nothing at zero); the full list is `OrchestratorPage.tsx:59` / `AttentionPanel`.
The client polls `/attention` every 15 s (`client/src/api/attention.ts:122`) and the queue panel
listens to `PublishQueueChangedAsync`. **No client change is needed**: a row leaves the feed the moment
its status leaves `Pending`.

### 1.6 The sweep pattern to copy, and the two homes not to use

- **Copy:** `CardWorkTransitionService` (scoped, `ScanAsync(ct)` public for tests, per-item
  try/catch, returns the count acted on) + `CardWorkTransitionHostedService`
  (`server/Infrastructure/Orchestration/`, `BackgroundService`, `PeriodicTimer`, `Enabled` short-circuit,
  scope per tick, `OperationCanceledException when stoppingToken.IsCancellationRequested`) +
  `CardWorkTransitionSettings` bound from a named section (`Program.cs:149`), scoped registration
  (`:320`), hosted registration beside `SessionReconciliationHostedService` (`:469-470`), section in
  `server/appsettings.json:115-119`.
- **Not `SessionHealthHostedService`** (`:31-34`): it returns immediately when `RcWatch.Enabled` is
  false, so the drain would silently inherit an unrelated switch; and `FlushStrandedQueuesAsync` is the
  automatic *retry* path — the sweep is its exact complement and should not share a doc comment with it.
- **Not the dispatcher tick** (`AgentTaskDispatcher.RunSweepAsync`, 5 s): CARD-0072 put its retry
  there because it needed that clock; this needs a minute, and it is queue-domain, not task-domain.
- **Not inside `SessionMessageQueueService` as policy.** The queue service owns the *write primitive*
  (the lock and the row); "which rows are stale" reads `AgentTasks` and `AgentSessions`, which is the
  same split `ReconcileSupersededChecksAsync` ↔ `CancelPendingIfUntypedAsync` already draws.

### 1.7 What does not exist (the build list)

1. A cancel entry point whose precondition is "still Pending **and still parked**" (the two existing
   automated ones require `DeliveryAttempts == 0`; the human one requires nothing).
2. A staleness rule over `AgentTasks`/`AgentSessions` for a queued row that has no task FK.
3. A hosted service to run it, its settings, and the config section.
4. A test that pins "a Grok pool reuse enqueues no compact".

---

## 2. Design

### 2.1 The rule

A parked row is **stale** — nobody needs to decide anything about it — when all of the following hold:

| # | Condition | Why |
|---|---|---|
| a | `Status == Pending && DeliveryAttempts >= MaxDeliveryAttempts` | Parked, by CARD-0055's definition; read from `SupervisionSettings.DeliveryVerification` exactly as `AttentionService` does (`:692`), so the sweep and the feed can never disagree about what parked means. |
| b | `Origin ∈ { Delegation, System, Check, Supervision }` | **A machine enqueued it.** `Ui` and `Channel` are a person's words (`DelegateCheckProbe.cs:590` already classifies exactly these two as `"human"`); parking exists for them. |
| c | `SourceTaskId == null && ConversationKey == null` | Not a delegation **completion note** and not channel/check-keyed. A parked completion note is the one Delegation row whose content a human is owed *precisely because* its task succeeded — CARD-0067's shape in the queue's own direction. Today's pile has none; the rule must still refuse them. |
| d | No `AgentTask` on the same `AgentSessionId` with `Status ∈ { Dispatched, Working, Blocked }` | Nothing is still running on that session that the row could have been for. The same `OpenStatuses` set `CardWorkTransitionService` uses (`:34`); `Queued` is not open there either. Vacuously true for a session that never had a task (a parked System-origin auto-continue on an always-on agent is housekeeping too). |
| e | `max(LastDeliveryStartedAt, CreatedAt) <= now − MinParkedMinutes` | Race hygiene, not a waiting period: `HandleDeliveryFailureAsync`'s grace pull and `LateConfirmAttemptedMessagesAsync` can still turn a fresh park into `Sent`, and a human who is looking at the row right now gets the same window. Default **10 min**. |

Session status is deliberately **not** a condition. On the live pile it agrees with (d) 34/34, but it is
the weaker signal: a `Failed` row can be a CARD-0056 false negative on a healthy session, and an idle
pool session is `Running` with a stale compact on it (the 08-19 shape). (d) is the durable fact in both
cases. The sweep reads **no** session or transcript liveness — CARD-0040's rule, for the same reasons.

What the rule refuses, on purpose: a parked brief whose task is still `Dispatched` (the orchestrator sees
the row and either Send-nows it or cancels the task, after which the next pass drops it); every
Ui/Channel row; every completion note. These stay parked for a human, as CARD-0055 intended.

### 2.2 The write primitive — one core, three entry points

Add to `SessionMessageQueueService`:

```csharp
/// CARD-0091. Cancels a message only if it is still Pending AND still parked at the moment the
/// lock is held — a Send-now or a late-confirm that marked it Sent in between wins, and a human
/// Drop that already Canceled it returns false rather than writing twice.
public Task<bool> CancelParkedIfStaleAsync(Guid sessionId, Guid messageId, CancellationToken ct)
    => CancelUnderLockAsync(sessionId, messageId,
        m => m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts >= MaxAttempts, ct);
```

…and refactor `CancelAsync` (`:355`) and `CancelPendingIfUntypedAsync` (`:448`) onto the same private
`CancelUnderLockAsync(sessionId, messageId, Func<SessionQueuedMessage, bool> when, ct)`: take
`GetLock(sessionId)`, scoped `AppDbContext`, load, evaluate `when`, stamp `Canceled` + `CanceledAt`,
save, release, `PublishQueueChangedAsync` only when a row changed. `CancelAsync` keeps its 404 and its
DTO return (the endpoint contract is untouched); the other two keep `bool`. The recheck under the lock is
the entire concurrency story: `SendNowAsync`, `FlushAsync` and `HandleDeliveryFailureAsync` all hold the
same semaphore, so the sweep can neither cancel a row mid-type nor cancel one that just confirmed.

The status is **`Canceled`, unchanged.** Reasons, in order of weight: no surface renders a `Canceled` row
(§1.2), so a `CanceledReason` column would be readable only by SQL; the card's own goal is "off the
screen, and eventually out of the table", which `Canceled` → retention already delivers; and the human
Drop, CARD-0082 and this sweep all mean the same thing operationally — "nothing will type this". The
distinction that matters is kept where it can be read: one structured `Information` line per row
(`MessageId`, `SessionId`, `Origin`, `DeliveryAttempts`, `ParkedSinceUtc`, `OwningTaskId`, `OwningTaskStatus`,
body head) under the `Antiphon` logger namespace, which is pinned at Information, plus one summary line
per pass that discarded anything. A per-row `AgentTaskEvent` on the owning task was considered and
rejected: the task is terminal, its timeline is closed, and a "we tidied a compact you never got" row on
a Succeeded task is noise.

### 2.3 `ParkedMessageSweepService` + hosted service + settings

`server/Application/Services/ParkedMessageSweepService.cs` (scoped):

```csharp
public async Task<int> ScanAsync(CancellationToken ct)
{
    if (!_settings.Enabled) return 0;
    var max = Math.Max(1, _supervision.DeliveryVerification.MaxDeliveryAttempts);
    var floor = UtcNow() - TimeSpan.FromMinutes(Math.Max(0, _settings.MinParkedMinutes));
    var candidates = await _db.SessionQueuedMessages.AsNoTracking()
        .Where(m => m.Status == QueuedMessageStatus.Pending
            && m.DeliveryAttempts >= max
            && m.SourceTaskId == null && m.ConversationKey == null
            && (m.Origin == Delegation || m.Origin == System || m.Origin == Check || m.Origin == Supervision)
            && (m.LastDeliveryStartedAt ?? m.CreatedAt) <= floor
            && !_db.AgentTasks.Any(t => t.AgentSessionId == m.AgentSessionId
                && (t.Status == Dispatched || t.Status == Working || t.Status == Blocked)))
        .Select(m => new { m.Id, m.AgentSessionId, m.Origin, m.DeliveryAttempts, m.CreatedAt, m.LastDeliveryStartedAt, Head = m.Body.Substring(0, 80) })
        .ToListAsync(ct);
    // per row: resolve the owning task for the LOG ONLY (LATERAL: newest task on the session with
    // DispatchedAt <= CreatedAt + 5 s), then
    //   if (_settings.DryRun) log "would discard" and continue;
    //   if (await _queue.CancelParkedIfStaleAsync(row.AgentSessionId, row.Id, ct)) { log; discarded++; }
    // a throw on one row is logged and the loop continues (SessionReconciliationService's contract).
}
```

The candidate query is the attention feed's own parked predicate (`AttentionService.cs:695-696`) plus
(b)–(e), so it costs what `/attention` already costs every 15 s. The task lookup per row is for the log
line and is **not a gate** — (d) is the gate.

`server/Infrastructure/Orchestration/ParkedMessageSweepHostedService.cs`: byte-for-byte the
`CardWorkTransitionHostedService` shape (`Enabled` short-circuit, `PeriodicTimer(max(5, IntervalSeconds))`,
scope per tick, resolve the service, `ScanAsync`, OCE-when-stopping breaks, anything else logs Warning
and continues).

`server/Application/Settings/ParkedMessageSweepSettings.cs`, section **`ParkedMessages`**:

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Off = the feature does not exist; nothing else changes. |
| `IntervalSeconds` | `60` | Same cadence as `CardTransitions`; the table is tiny and the query is the feed's. |
| `MinParkedMinutes` | `10` | Condition (e). |
| `DryRun` | `false` | Log "would discard" per row, write nothing. The brake for a future operator who wants to watch a pass before it acts; §1.3 is why it ships off (§5, decision 2). |

Registration: `Configure<ParkedMessageSweepSettings>(GetSection("ParkedMessages"))` beside
`Program.cs:149`, `AddScoped<ParkedMessageSweepService>()` beside `:320`,
`AddHostedService<…ParkedMessageSweepHostedService>()` immediately after `:470`, and the section in
`server/appsettings.json` after `CardTransitions`. `ProductionRunnerGuard` needs nothing: the sweep
launches nothing and touches no runner.

### 2.4 What the first deploy does

The first tick after `scripts/restart-apphost.ps1` discards **34** rows (every row in §1.3 satisfies
(a)–(e); the youngest is six days past (e)), logs 34 lines and one summary, and within 15 s the mobile
home's "Needs you" band drops from 41 items to the 7 `RecentFailure` rows (which are a different
condition and age out on their own). 30 days later `PruneQueuedMessagesAsync` deletes the 34 rows.
Verification is the §1.3 query returning 0 rows and `GET /api/attention` reporting 0 `ParkedMessage`
items.

### 2.5 The source pin (S2)

`tests/Antiphon.Tests/Application/AgentTaskReuseEnqueueTests.cs` already pins the two neighbours —
Claude enqueues compact-then-brief (`both_reuse_messages_enqueue_when_nothing_throws`, `:167`) and
**Codex** enqueues no compact (`a_codex_reuse_enqueue_throw_still_raises_its_own_incident`, `:185`,
CARD-0117 S3) — but has **no Grok case**, and Grok is the kind that built the pile. Add one to that
class: `SeedReuseDispatchAsync(AgentKind.Grok)` for unrelated work enqueues **exactly one** Delegation
row, the brief, and **no** row beginning with `/compact`, through the same `ReuseEnqueueOverride` seam
(`AgentTaskDispatcher.cs:2670`, CARD-0077) the class already uses. This is a guard on the catalog (`ProviderContractCatalog.cs:154`), not on the
dispatcher — flipping Grok's `RefocusCompact` to `Supported` without a measured command reopens the
pile, and this test is what says so.

### 2.6 Docs (S3)

- `AGENTS.md` Gotchas: one bullet — *A parked message whose session has no open task is discarded by a
  sweep, never retried* (CARD-0091): the rule in one sentence, `Canceled` reused on purpose, what stays
  parked (Ui/Channel rows, completion notes, rows on sessions with open tasks), `ParkedMessages:DryRun`.
- `docs/orchestration-loop.md`, wherever it tells the orchestrator what a `ParkedMessage` attention row
  means: add that a row on a finished task clears itself within ~10 minutes and the ones that remain are
  the ones that need a decision.

---

## 3. What this costs its neighbours

- **CARD-0055's contract is untouched.** Parking still means "no automatic *redelivery*"; the sweep
  never types, never re-Enters, never marks `Sent`. Late-confirm keeps precedence because (e) and the
  under-lock recheck let it run first.
- **CARD-0082 is subsumed, not changed.** A Supervision row that parked before CARD-0082 landed is now
  caught by (b); the cancel-not-park arm keeps handling new ones at the attempts cap.
- **CARD-0074/0132's untyped-check sweep is unchanged.** It acts at `DeliveryAttempts == 0` with a
  banner; this one acts at `>= max` with a log line. A Check note that was typed three times and parked
  is only ever reached here, and only once its task has no open sibling.
- **CARD-0067** stays whole: `Channel` rows are outside (b), so a lost reply is still the Critical
  `ChannelReplyLost` incident and never a quiet cancel.
- **Retention** gains its missing feeder: `Canceled` rows age out; `Pending` ones still never do.
- **The attention feed** loses 34 rows and gains no code.

## 4. Non-goals

- Retrying, Send-now-ing, or amending any parked row (the card is explicit: typing a stale `/compact`
  into today's session is the harm, not the fix).
- A new `QueuedMessageStatus` value, a `CanceledReason`/`CanceledBy` column, or a migration of any kind.
- Ui-origin rows on dead sessions, Channel rows, and completion notes (`SourceTaskId != null`). Each is
  a person's content; each stays parked for the existing Drop action. If the Ui-on-dead-session shape
  ever piles up, it is its own card with its own rule.
- Auto-clearing `RecentFailure` attention items (a different condition, with its own 24 h window).
- A "parked messages" list UI, a bulk-drop button, or any client change.
- Touching `ProviderContractCatalog` — Grok's `RefocusCompact` stays `Unknown` until someone measures it.

## 5. Decisions that are the operator's — each with a recommendation

1. **Reuse `Canceled` or add provenance?** Recommend **reuse, no column** (§2.2). Nothing renders
   `Canceled`; the log carries the distinction; retention already knows the status. Revisit only if a
   queue-history view is ever built.
2. **Ship `DryRun=false`?** Recommend **yes**. The candidate set has been enumerated by hand today
   (§1.3): 34 rows, every task terminal, every session without open work, none human-origin, none a
   completion note. A dry-run first deploy would print the same 34 ids and cost a second restart. The
   flag exists for the next operator, not this deploy.
3. **`MinParkedMinutes`.** Recommend **10**. It is race hygiene; the grace and late-confirm paths finish
   in seconds. Anything over an hour only keeps dead rows on the home screen longer.
4. **Session status as a condition?** Recommend **no** (§2.1) — "no open task" is the durable fact;
   session status is the one that has lied before (CARD-0056).
5. **Include Ui-origin rows on `Stopped`/`Failed` sessions?** Recommend **not in v1**. Zero exist today;
   a human typed them; the Drop button is one tap.

## 6. Slices, tiers, tests

| Slice | What | Tier | Tests (scoped to rows the test created — the shared-Postgres rule) |
|---|---|---|---|
| **S1** the sweep | `CancelUnderLockAsync` core + `CancelParkedIfStaleAsync`; `ParkedMessageSweepService`; hosted service; settings + `Program.cs` + `appsettings.json` | Grok | `ParkedMessageSweepServiceTests` (integration, `[NotInParallel]` with **no** key, own migrated schema per test — the `CardWorkTransitionServiceTests.World` shape): parked Delegation row, task Succeeded, session Failed → `Canceled` + `CanceledAt`, returns 1; parked compact on a `Running` session with no open task → Canceled; sibling task `Working` → untouched, then settle it → next pass cancels; `Blocked` counts as open; `DeliveryAttempts = max − 1` → untouched; Ui and Channel origin → untouched; `SourceTaskId` set → untouched; `ConversationKey` set → untouched; younger than `MinParkedMinutes` → untouched, older → Canceled; `DryRun` → 0 writes, returns 0; `Enabled=false` → 0; one row marked `Sent` after the candidate read (call the primitive directly on a Sent row) → false, no write; a human-Canceled row → false. `SessionMessageQueueServiceTests`: `CancelAsync` and `CancelPendingIfUntypedAsync` behave exactly as before through the shared core (existing tests stay green, one new test per entry point on the precondition). `DataRetentionServiceTests`: a swept row (Canceled, attempts ≥ max) past the window is pruned |
| **S2** source pin | §2.5 | Codex terra | `AgentTaskReuseEnqueueTests`: new Grok case — one row, the brief, no `/compact` (Claude and Codex cases already exist) |
| **S3** docs | §2.6 | Codex luna | none |

S1 and S2 are independent; S3 last. Build to `--property:OutputPath=bin-<name>/`; the S1 suite is
`--treenode-filter "/*/Antiphon.Tests.Application/ParkedMessageSweepServiceTests/*"` plus
`SessionMessageQueueServiceTests` and `DataRetentionServiceTests`. Deploy S1 with
`pwsh -File scripts/restart-apphost.ps1`, then the §2.4 checks. Estimate: S1 ≈ 2–3 h, S2 ≈ 30 min,
S3 ≈ 20 min.
