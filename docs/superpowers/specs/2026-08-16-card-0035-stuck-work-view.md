# CARD-0035 — A diagnostic view for work that is stuck

- **Status**: Planned (this document is the plan; nothing here is implemented)
- **Card**: CARD-0035 (`43635fab-de31-4f12-87b8-df2f9af21bd5`) — "UX: a diagnostic view for work
  that is stuck", P0, story S5
- **Date**: 2026-08-16
- **Siblings**: CARD-0031 ("what is happening right now", P0, unbuilt), CARD-0002 (Tasks section on
  the home rail, designed in `docs/features/010-home-tasks-section/proposal.md`, unbuilt),
  CARD-0033 ("answer a blocked delegate question in place" — the reply path already exists in
  `TaskDrawer.tsx`; this view deep-links to it)
- **Concurrent work warning**: two agents are changing `AgentSessionService`,
  `RunnerClaudeAdapter`, `SessionReconciliationService` and `SupervisionSettings` (CARD-0056 et
  al.). **This plan touches none of those files.** See §5 for the collision map.

## 0. What exists today (verified against the code, 2026-08-16)

The card is right that the signal now exists and the screen does not. Verified inventory:

### Task-side signal (all on `AgentTask` / exposed in `AgentTaskSummaryDto`)

| Signal | Where | Notes |
|---|---|---|
| `ExpectedDurationMinutes` (default 10, always set) | `server/Domain/Entities/AgentTask.cs:135` | A hint, never a deadline — nothing fails on it |
| `NextCheckAt` / `CheckCount` | `AgentTask.cs:143-146` | Armed only when `ReplyTo == Session`; final check nulls `NextCheckAt` when `checkNumber >= CheckMaxCount` (`AgentTaskDispatcher.cs:588`, default 10, `DelegationSettings.cs:322`) |
| Status machine | `AgentTaskEnums.cs:53` | `Queued/Dispatched/Working/Blocked/Succeeded/Failed/Canceled`. `Working` is only ever set by `AgentTaskReplyService.cs:226` (after a blocked reply) — most live tasks read `Dispatched` forever, which is the card's complaint |
| Cost | `AgentTaskSummaryDto.SubtreeCostUsd`, `CostPricingVersion` | Rolled-up spend per subtree already computed and shipped to the client |
| Timeline | `AgentTaskEvent`, type `Check` (13) | Check events store the **digest** (truncated at 900 chars, `AgentTaskCheckService.cs:35`) + an interpreter cost line — **not** the interpreter's reading |
| Watchdogs | `AgentTaskDispatcher.cs:99/104` | `AutoEscalateStalledAsync` (progress-gated tier bump), `FailNeverStartedAsync` (zero transcript entries past `DeliveryFailTimeoutMinutes` ⇒ task Failed with reason) |

### Check-ins (CARD-0047, shipped)

`DelegateCheckProbe.GatherAsync` (`server/Application/Services/DelegateCheckProbe.cs:142`) already
computes, deterministically, per task: session status + the shared `IsWorkingAsync` verdict,
transcript tail, git commits/working-tree counts, pending queue messages, last 5 incidents.
`AgentTaskCheckService.RunCheckAsync` delivers `[check <id> #n]` notes to the **caller session's
queue** and records a `Check` event. The interpreter's 3–5-line reading lives only in (a) the
delivered note body and (b) the interpretation task's own `Result` row (`Role = Check`, hidden from
the board by default, correlated to the checked task **only by title text**
`CheckInterpretation.BuildTitle` — no FK).

### Incidents / alerts

- `AgentIncident` (`server/Domain/Entities/AgentIncident.cs`) is **append-only**: kind, severity,
  message, `AgentId` (required), `SessionId?`. **No open/resolved/acknowledged state exists.**
  Kinds 0–19 include `DeliveryVerificationFailed` (11), `DeliveryTransportFailed` (13),
  `TranscriptBindFailed` (15), `DelegateReportUncorrelated` (17), `DelegateFinalMessageMissing`
  (18), `DelegateSubagentsNeverReported` (19).
- API surface: **per-agent only** — `GET /api/agents/{id}/incidents?take=` (`AgentEndpoints.cs:96`,
  client hook `useAgentIncidents`). There is **no cross-agent incidents listing**.
- `Alert` rows are written (`AlertService.cs:46`), routed to channels and broadcast over SignalR
  (`AlertRaised` → toast in `useAlertToasts.ts`), pruned — but **never listable via any endpoint**.

### Queue / parking (CARD-0055, shipped)

`SessionQueuedMessage.DeliveryAttempts` + `LastDeliveryBaselineSequence` exist. Parking is **not a
status**: a message is parked when `Status == Pending && DeliveryAttempts >= MaxDeliveryAttempts`
(`SupervisionSettings.cs:159`, default 3); every automatic path excludes it
(`SessionMessageQueueService.cs:300,480,1102`). Parking raises a Critical incident when the agent
is channel-bound. **`QueuedMessageDto` (`SessionQueueDtos.cs:15`) does not expose
`DeliveryAttempts` or origin — the queue UI cannot currently tell a parked message from a pending
one.** Manual paths still work on a parked message: `POST …/messages/{id}/send-now` and
`DELETE …/messages/{id}` (`SessionEndpoints.cs`).

### Sessions

`AgentSession.Status` (`Created/Starting/Running/Stopping/Stopped/Failed`). CARD-0056's spec
(planned, not built) establishes: `SessionReconciliationService` today sees only DB-live-vs-runner
(its two passes query `LiveStatuses`) — **DB-Failed-while-runner-Running is invisible**, and
unclaimed runner sessions have no surface at all. There is **no sessions list endpoint**; sessions
reach the client only through the agent list. `POST /api/sessions/{id}/kill` exists.

### Actions already served

`POST /api/agent-tasks/{id}/cancel|retry|escalate|reply` (`AgentTaskEndpoints.cs:50-75`) with
client hooks in `client/src/api/agentTasks.ts`; reply-to-Blocked UI already in
`TaskDrawer.tsx:209-231`. Session kill, message cancel, message send-now — all exist.

### Client surfaces

- Home (`HomePage.tsx`, feature 008): agent rail + files + chat/tasks dock;
  `ProjectTasksPanel.tsx` is the flat list CARD-0002/010 replaces.
- Delegations board: `/orchestrator?tab=delegations` (`DelegationsBoard.tsx`), lanes
  Queued/Working/Blocked/Done (`taskVisuals.ts:32`), `TaskDrawer` opens via `?task=<id>`.
- Test conventions: `renderWithProviders` from `client/src/test/utils.ts` (never a raw
  `MantineProvider`), Vitest + Testing Library, co-located test files.

## 1. Design decisions

### D1. "Stuck" is nine named, computable conditions

One server projection computes them; every condition names its derivation. A session that is
`Working` with a fresh transcript is **never** stuck ("genuinely slow — leave it alone" is an
explicit non-member, which is what keeps the view trustworthy).

| # | Kind | Definition (all computable server-side) | Data exists? |
|---|---|---|---|
| 1 | `BlockedQuestion` | Task `Status == Blocked` | Yes |
| 2 | `ParkedMessage` | Queued message `Pending && DeliveryAttempts >= MaxDeliveryAttempts`; severity Critical when the session's agent is channel-bound | Yes (DB); **not exposed in any DTO** |
| 3 | `DeadSession` | Task Dispatched/Working AND (session row missing, or `Status ∈ {Stopped, Failed}`, or `EndedAt != null`) | Yes |
| 4 | `NeverStarted` | Task Dispatched, `DispatchedAt` older than 2 min, session has **zero** `TranscriptEntries` (the `FailNeverStartedAsync` predicate, surfaced while inside its window; after the window the watchdog turns it into a `RecentFailure`) | Yes |
| 5 | `UncorrelatedReport` | Task Dispatched/Working AND a `DelegateReportUncorrelated` (17) incident on its session since `DispatchedAt` | Yes |
| 6 | `PastExpectedIdle` | Task Dispatched/Working, elapsed > `max(2 × ExpectedDurationMinutes, ExpectedDurationMinutes + 30m)`, AND `IsWorkingAsync == false` (idle mid-flight = the "finished but not reported" shape; a working session is excluded) | Yes |
| 7 | `ChecksSpent` | Task Dispatched/Working, `CheckCount > 0`, `NextCheckAt == null` — the check budget ran out with the task still open; nobody is watching it any more | Yes |
| 8 | `SessionDisagreement` | Runner reports `Running` for a session the DB calls `Failed`/`Stopped`, or runner session with **no DB row** (leaked/unclaimed) | Needs one **read-only** runner `ListAsync` diff inside the projection (new code, no shared files); CARD-0056 slice 4 will later shrink this set via re-adoption — this view stays the safety net that makes the remainder visible |
| 9 | `RecentCriticalIncident` | Incident `Severity >= Error` in the last 24 h **not already attached** as evidence to a row above (e.g. `TranscriptBindFailed` on a channel-bound agent), grouped per agent | Yes (DB); **no cross-agent endpoint — the projection queries the table directly** |
| — | `RecentFailure` (context group, collapsed) | Task `Failed` with `CompletedAt` in the last 24 h — failures otherwise hide among successes in the Done lane | Yes |

**No ack model in v1.** Incidents are append-only and nothing marks them handled; conditions 1–8
self-clear when the underlying state changes (task settles, message unparked/cancelled, session
killed/re-adopted), which is the right lifecycle for a diagnostic list. Condition 9 and the
`RecentFailure` group use a fixed 24 h recency window instead of an ack — an ack/`resolvedAt`
model is explicitly **v2** (§D5).

Each item carries: kind, severity (`Critical`/`Error`/`Warning`), subject refs (`taskId?`,
`sessionId?`, `agentId?`, `messageId?`), `sinceUtc` (when the condition began, best-effort), a
one-line server-computed `headline`, an `evidence` string (see D4), `subtreeCostUsd` when
task-scoped (the card's cost requirement — spend is on the row, not hidden in a report), and an
`actions` list naming which of the D3 verbs apply.

### D2. Where it lives: one projection, one diagnostic tab; the siblings embed the same data

- **The projection** is the shared spine: `GET /api/attention` (new
  `AttentionService` + `AttentionEndpoints`). It is deliberately **fleet-global** (stuckness is
  not per-project; a parked Telegram reply matters wherever you are looking).
- **The full view** is a new tab on the existing Orchestrator page —
  `/orchestrator?tab=attention` — beside the delegations board. Rationale: every action and the
  task drawer already live there; a stuck row's natural click-through is `?tab=delegations&task=…`
  which is a sibling tab, and the page is already the "operations" surface. Not a new page (no
  reason to grow the nav for a list that is empty on a healthy day), not the home rail (240 px
  cannot carry evidence text), not a board panel (tasks, not cards, dominate the stuck set).
- **Relation to the siblings — one spine, three presentations, built in this order**:
  CARD-0035 (this) builds the projection + the diagnostic tab. CARD-0002/010's rail then renders
  `attention.items` where its proposal already reserves the "needs a human decision" group — it
  consumes the endpoint, it does not recompute. CARD-0031's status view takes the same items as
  its block 1 ("what needs me") and adds blocks 2–5 from other sources. They are **three surfaces
  over one projection**, not one surface: the home rail answers "what needs me *here*", CARD-0031
  answers "what is the state of *this project*", this card answers "across everything, what is
  stuck *and why*" — the why (evidence, per-kind actions) is what neither sibling has room for.
  The only home change in this card is a `Needs attention (N)` badge in the header linking to the
  tab (slice 6), so the signal is reachable from the landing screen before 010 lands.

### D3. What a human does from it (all verbs exist server-side today)

| Condition | Primary action | Secondary |
|---|---|---|
| BlockedQuestion | **Reply in place** (reuse `useReplyToAgentTask`; row expands to the same textarea `TaskDrawer` has — this *is* CARD-0033's ask, delivered here) | Cancel, Escalate |
| ParkedMessage | **Send now** (`POST …/send-now` — the manual path deliberately bypasses parking) | **Cancel message** (`DELETE …/messages/{id}`) |
| DeadSession / NeverStarted / UncorrelatedReport | **Retry** (`/retry`) | Cancel; Escalate |
| PastExpectedIdle / ChecksSpent | **Open drawer** (read the check digest first — the correct response may be "leave it") | Cancel; Retry; Escalate |
| SessionDisagreement (unclaimed/leaked) | **Kill session** (`POST /api/sessions/{id}/kill`) with a confirm dialog naming the session and its cwd | none |
| RecentCriticalIncident | **Open agent** (link to `/agents` incident drawer) | none |
| RecentFailure | **Retry** | Open drawer (read `FailureReason`) |

No new server verbs in v1. The one server-side gap the actions expose: the per-session queue DTO
must say which message is parked (slice 4 adds `deliveryAttempts`, `origin`, `parked` to
`QueuedMessageDto` — additive, no behavior change).

### D4. The explanation column: digest now, interpretation with one small addition

The check-interpreter's 3–5-line reading **is** the right human-facing explanation — it was built
for exactly this altitude — but today it is not retrievable per checked task (it lives in the
caller's note and on an uncorrelated `Role=Check` task row; the `Check` event keeps only the
digest + a cost line, `AgentTaskCheckService.cs:137`). So:

- **v1 (no new plumbing)**: the row's evidence is the tail of the latest `Check` event's stored
  digest (status/elapsed/commits lines) — deterministic, always present once a check has run —
  plus, per kind, the incident message / `FailureReason` / parked-message excerpt.
- **Slice 5 (small, safe)**: `AgentTaskCheckService.RunCheckAsync` writes the interpretation text
  into the `Check` event detail above the digest (same site that already composes the cost line;
  this file is **not** in the concurrent-change set). From then on the view shows the specialist's
  reading verbatim, and it is retroactively absent only for pre-slice checks. No new table, no FK.

### D5. Not in v1

- **No ack/resolve model for incidents** and no "dismiss" on rows — conditions self-clear; a
  dismissed-but-still-true row is how the 2026-08-11 nine-hour Dispatched happened.
- **No alerts listing endpoint** — `Alert` rows stay toast + channel + prune; incidents carry the
  durable record this view needs.
- **No cost anomaly detection / ceilings UI** — spend is shown per row (`SubtreeCostUsd`), not
  judged.
- **No auto-remediation** — every verb is a human click; the watchdogs already own the automatic
  half.
- **No new SignalR events** — reuse `AgentTaskChanged`/`AgentChanged`/`AlertRaised` invalidation +
  a 15 s poll, matching `useAgentTasks`.
- **No per-project scoping/filtering** — global list first; scoping arrives when CARD-0031
  consumes the projection.
- **No history/trends**, no "stuck duration" analytics.
- **Not CARD-0056's re-adoption** — this view only *shows* disagreements; fixing them is that
  card's reconciliation pass.

## 2. Server design

New files only:

- `server/Application/Services/AttentionService.cs` — computes §D1. Inputs: `AppDbContext`
  (read-only, `AsNoTracking` throughout), `ISessionRunnerClient` (the existing client;
  `ListAsync` only), `IOptions<SupervisionSettings>` (reads `Verification.MaxDeliveryAttempts` —
  **reads**; adds nothing to that file), `IOptions<DelegationSettings>`, `TimeProvider`. Reuses
  `SessionMessageQueueService.IsWorkingAsync` (static, already shared) for condition 6. Runner
  unreachable ⇒ `RunnerConsulted = false` on the response and condition 8 omitted — degrade,
  never fail the whole list.
- `server/Application/Dtos/AttentionDtos.cs` — `AttentionDto(GeneratedAt, RunnerConsulted,
  IReadOnlyList<AttentionItemDto> Items)`; `AttentionItemDto(Kind, Severity, TaskId?, SessionId?,
  AgentId?, MessageId?, Title, Headline, Evidence, SinceUtc?, SubtreeCostUsd?, Actions)` with
  `AttentionKind` and `AttentionAction` enums.
- `server/Api/Endpoints/AttentionEndpoints.cs` — `GET /api/attention`.

Ordering: severity desc, then `SinceUtc` asc (oldest stuck first). Item cap: none expected to be
needed (active tasks are tens, not thousands); `RecentFailure` group capped at 20.

## 3. Client design

New feature folder `client/src/features/attention/`:

- `client/src/api/attention.ts` — types mirroring the DTOs, `useAttention()` (15 s
  `refetchInterval`, SignalR invalidation on `AgentTaskChanged`/`AgentChanged` alongside the
  existing wiring).
- `AttentionPanel.tsx` — the tab body: severity-grouped rows ("Needs you now" / "Broken" /
  "Suspect" / collapsed "Recent failures"), each row = kind badge (semantic palette, reusing
  `STATUS_COLOR` conventions from `taskVisuals.ts`), title, age (`formatDuration`), cost
  (`formatCost`), evidence excerpt, kebab with the D3 verbs. Row click → existing drawer
  (`navigate('/orchestrator?tab=delegations&task=…')`) or agent page for incident rows. Empty
  state: "Nothing is stuck." — the view must earn trust by being empty on a good day.
- `attentionVisuals.ts` — kind → label/color/icon/severity-group mapping (pure, testable).
- `BlockedReplyRow.tsx` — inline reply expansion reusing `useReplyToAgentTask`.
- Orchestrator page: add the tab in `OrchestratorPage.tsx` (`?tab=attention`), with a count badge
  on the tab label.
- Home: `Needs attention (N)` badge-link in the `HomePage.tsx` header row (slice 6).

## 4. Slices (each independently landable and testable)

**Slice 1 — projection service + endpoint, DB-only conditions (1–7, 9, failures).**
Files: `AttentionService.cs`, `AttentionDtos.cs`, `AttentionEndpoints.cs`, `Program.cs`
registration. Tests: new `tests/Antiphon.Tests/Application/AttentionServiceTests.cs` (TUnit, via
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-attention/` while daemons
run) — one test per condition proving the predicate on seeded rows, **every assertion scoped to
rows the test created** (shared-Postgres rule), plus: a Working-session task past expected is NOT
listed; a parked message on a channel-bound agent reads Critical; runner-down still returns
DB-only items.

**Slice 2 — runner disagreement pass (condition 8).**
Files: `AttentionService.cs` only. Tests: same file, faking `ISessionRunnerClient` — DB-Failed +
runner-Running listed; runner session with no DB row listed as unclaimed; runner unreachable ⇒
`RunnerConsulted=false`, no throw. *Deliberately duplicates the diff CARD-0056 slice 4 will do in
`SessionReconciliationService` — read-only here, and this view remains correct whether or not
re-adoption lands.*

**Slice 3 — client: attention API + Orchestrator tab.**
Files: `client/src/api/attention.ts`, `features/attention/AttentionPanel.tsx`,
`attentionVisuals.ts`, `OrchestratorPage.tsx` (add tab). Tests (all via `renderWithProviders`,
MSW): `AttentionPanel.test.tsx` — groups render in severity order; empty state; evidence excerpt
shown; row click navigates to the drawer URL; `attentionVisuals.test.ts` — kind mapping total
(every `AttentionKind` has a visual); `OrchestratorPanel.test.tsx` — tab appears with count.

**Slice 4 — actions.**
Files: `AttentionPanel.tsx`, `BlockedReplyRow.tsx`; server: `SessionQueueDtos.cs` +
`SessionMessageQueueService` mapping (additive `deliveryAttempts`/`origin`/`parked` on
`QueuedMessageDto`) and `SessionMessageQueue.tsx` parked chip. Tests: `BlockedReplyRow.test.tsx`
(reply posts and clears); `AttentionPanel.test.tsx` — parked row offers send-now + cancel and
calls the right endpoints (MSW spies); kill-session shows confirm naming the session; server
`SessionQueueDto` mapping test asserting a parked message serialises `parked: true`.

**Slice 5 — interpretation into the Check event.**
Files: `AgentTaskCheckService.cs` (detail composition only). Tests: extend
`tests/Antiphon.Tests/**/AgentTaskCheck*` — event detail carries the interpretation above the
digest when one ran, digest-only when degraded; 900-char cap still holds; client
`AttentionPanel.test.tsx` — evidence prefers the interpretation line when present.

**Slice 6 — home badge.**
Files: `HomePage.tsx`. Tests: `HomePage.test.tsx` — badge renders with count when items exist,
absent when zero, links to `/orchestrator?tab=attention`.

## 5. Collision map (files this plan will NOT touch)

- `AgentSessionService.cs`, `RunnerClaudeAdapter.cs` — untouched (CARD-0056 slices 1–3 own them).
- `SessionReconciliationService.cs` — untouched; slice 2 duplicates its future diff read-only in
  new code. When CARD-0056 D4 lands with `AgentIncidentKind.SessionReAdopted`, add it to the
  incident-kind display map (one-line client follow-up).
- `SupervisionSettings.cs` — **read via IOptions only**; any new settings this feature ever needs
  go in a new `AttentionSettings.cs`. (v1 needs none: thresholds are the two constants in D1.6
  and the 24 h window, kept as consts in `AttentionService` until someone asks to tune them.)
- `SessionMessageQueueService.cs` is in slice 4 (DTO mapping only). CARD-0055 shipped and the
  named concurrent agents are not listed against it, but **re-check `git log` on that file before
  starting slice 4** — it borders their delivery work.

## 6. What I could not determine, and what settles it

1. **Whether `PastExpectedIdle`'s threshold (`max(2×expected, expected+30m)`) matches operator
   instinct.** Evidence: run the slice-1 projection against the live DB and eyeball the list with
   the operator; the constants are trivially movable.
2. **How noisy `RecentCriticalIncident` is on a real day** (RC churn could dominate). Evidence:
   `SELECT kind, count(*) FROM "AgentIncidents" WHERE "Severity" >= 2 AND "CreatedAt" > now() -
   interval '24 hours' GROUP BY kind;` before building slice 1's grouping; if RC kinds dominate,
   collapse per-agent per-kind to one row (the plan already groups per agent).
3. **Whether the runner's `ListAsync` includes sessions the DB never knew** (needed for
   "unclaimed" rows) — CARD-0056's spec asserts it; confirm against
   `ISessionRunnerClient.ListAsync` and the runner's `/sessions` handler when slice 2 starts, and
   against whatever shape CARD-0056 slice 4 has landed by then.
4. **Whether CARD-0002's rail wants counts or full items** from `/api/attention` — settled when
   010 is built; the DTO already serves both.
5. **`SinceUtc` fidelity** for `BlockedQuestion` (needs the latest `Blocked` event's timestamp —
   one extra query; verify the event is written on every blocked transition, including the
   settlement-path question detector).
