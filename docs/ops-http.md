# Inspecting agents, boards and live sessions over HTTP

The operator's map of the surface an orchestrator actually reaches for: which agents exist, what
they are running, which boards and cards are open, and what a live session is doing. It is
deliberately short and it is not the route map — [antiphon-api.md](antiphon-api.md) is that, and
`server/Api/Endpoints/*.cs` plus `src/Antiphon.SessionRunner/Program.cs` are the authority over
both. Cards are worked through `scripts/card.ps1` (`server/Bundles/board-api.md` for the raw card
API); nothing here replaces that.

**Do not grep `MapGet` to find a route.** The one route this page cannot give you is a route this
page says does not exist.

Landing timeline events carry nullable `landingOperationId`, `landingPublication`,
`landingCleanup` and `landingMode` snapshots. `LandingCleanup` updates an existing publication
without counting another one. Legacy events have null snapshots and grant no cleanup authority.

## Two processes, two prefixes

Mixing them is how sessions get 404s that look like a broken server.

| Process | Base | Prefix |
|---|---|---|
| Antiphon server (Aspire) | `http://localhost:17202` | `/api/...` |
| Session-runner (production) | `http://localhost:17204` | `/sessions/...` — **no `/api`** |

Simple mode serves the server on `17281`; an E2E run owns its own **random** runner port, never
17204. Resolve the base the way the scripts do — `$env:ANTIPHON_API`, falling back to
`http://localhost:17202` — and send `$env:ANTIPHON_TASK_TOKEN` as the `X-Antiphon-Task-Token`
header when it is set. Inside a running agent session both are already in the environment.

### ChatGPT / Codex as an external orchestrator (CARD-0398)

Run `delegate.ps1 -Capability <name> …` from the approved checkout (add `-Kind Codex` while Claude is held). Do not read capability files, do not dump env looking for tokens, do not edit `Delegation:AllowedRoots`. Reports land on the bound card; poll `delegate.ps1 -Status <id>` or the card thread.

The operator issues the capability with `scripts/capability.ps1 issue -Name <n> -Roots <checkout>`. That script writes a DPAPI blob and prints the name and store path — never the token. `-Orchestrator -Kind Codex` stays 422. Codex remote control stays `409 remote_control_refused`. A Claude usage hold still `409 model_disabled` unless the caller passed `-Kind Codex`; quota-exhausted is `409 subscription_quota_low`. The complementary fallback is a named Codex AlwaysOn plus `POST /api/sessions/{id}/messages` — still do not widen `AllowedRoots`.

### Claude held

While Claude aliases are on a usage hold, a capability caller that wants to keep dispatching must pass `-Kind Codex` (Worker or stage role, not Orchestrator). Default kind is still ClaudeCode and still 409s. The complementary path is the named Codex AlwaysOn session token (kind-blind `MayDelegate`). Neither path edits `Delegation:AllowedRoots`.

## The jobs you have

CARD-0415 adds `GET`/`PUT /api/agents/{id}/specialist-routing` and
`POST /api/agents/{id}/specialist-routing/revalidate` for the configured standing Check owner.
`scripts/specialist-routing.ps1 inspect|set|revalidate -Agent <guid>` uses these shapes and reads a
fresh revision before writes. `set -Candidates 'ClaudeCode/High,Codex/Low'` declares ordered pairs;
the first pair must match the actual primary. `set -Disable` retains an existing list.
These endpoints expose declared pairs, retained candidate readiness/refusal reasons and durable
logical health. Reconciliation creates separate typed Claude alternate seats. Revalidate rotates
one bounded qualification authorization; the worker still requires verified CLI capability and
an idle authorized generation before its two semantic probes. A successful write does not mean
ready or activated. The production capability catalog is currently empty and authenticated
acceptance remains pending. Codex rows report PendingDependency CARD-0167. See the
[continuation evidence](investigations/2026-09-09-card-0415-continuation-evidence.md) before claiming
the chain is available. Durable `StandingSpecialistHealth` Attention survives incident pruning;
readiness or qualification alone cannot resolve a real-service outage.

| Need | Method | Path |
|---|---|---|
| Every agent, with its live session | GET | `/api/agents` |
| One agent | GET | `/api/agents/{id:guid}` |
| Start / stop an agent | POST | `/api/agents/{id}/start`, `/api/agents/{id}/stop` — named herdr pin (`herdrTabLabel`) 409s occupancy **before** enqueue; a later runner 409 is an async Failed row. |
| Runner named-tab preflight | POST | `:17204/herdr/placement/check` `{ sessionId, herdr }` — read-only; 200 `create`/`relaunch`/`adopt` or 409 with the launch codes. |
| Inspect one leftover Herdr pane | POST | `/api/herdr/pane-disposals/preview` `{ paneId, expectedSessionId?, expectedNativeSessionId? }`; one exact pane and at least one full UUID. Two-minute, single-consumer preview with sanitized ownership/process evidence; standing owner must be stopped. |
| Explicitly dispose a reviewed pane | POST | `/api/herdr/pane-disposals` `{ operationId, previewId, reason }`; 200 Closed/AlreadyAbsent, 409 refusal, 503 unavailable/Unknown. Best-effort fresh Antiphon checks precede unconditional `pane.close`; external changes after that check can race close. Read status after uncertainty; never replay destruction. |
| Read disposal receipt | GET | `/api/herdr/pane-disposals/{operationId}`; no session row required. Runner equivalents use `/herdr/pane-disposals`. `scripts/herdr-pane.ps1 inspect\|dispose\|status` uses server routes; dispose is dry-run unless `-Execute`. |
| Manually refresh an agent's policy (CARD-0334) | POST | `/api/agents/{id}/refresh-policy` (`{ force?: bool }`) — idle-gated like the sweep: kill+resume, or a `Notify`-lane message, without suspending supervision. `force` skips only the idle-minutes floor and the cooldown; a working session is always 409 `session_working`, and a Codex/unbound-transcript agent is 409 `not_resumable`. Returns `{ refreshed, notified, agent }`; a Notify-lane 200 is `refreshed: false, notified: true`. |
| Delete an agent | DELETE | `/api/agents/{id}` |
| Boards | GET | `/api/boards` (`?includeArchived=true`) |
| One board | GET | `/api/boards/{id}` (`?view=summary`, `?includeArchived=`) |
| A board's columns, name to id | GET | `/api/boards/{id}/columns` |
| Card-file policy and cleanup state | GET | `/api/boards/{id}/card-files/status` |
| Explicit card-file policy update | PUT | `/api/boards/{id}/card-files/settings` (both policy and expected-policy booleans) |
| Reconcile permitted card files and revoked exports | POST | `/api/boards/{id}/card-files/sync` (`?dryRun=true`; HTTP 200 may describe a refusal) |
| A board's cards | GET | `/api/cards?boardId={guid}` |
| One card | GET | `/api/cards/{id}` — `CARD-0296` resolves; prefer `card.ps1 get` |
| Queue a card diagnosis (CARD-0352) | POST | `/api/cards/{id}/diagnose` — 202 `{ queued: true }`; `card.ps1 diagnose CARD-nnnn` (`-NoWait` skips the 120 s poll). 409 `diagnose_disabled` when the seat is off. Shipped `DiagnoseLabelMode` is **Shadow** (ledger only) until flipped to Apply. |
| Diagnoses ledger / stats | GET | `/api/diagnoses?cardId=` (newest first), `/api/diagnoses/stats?since=` |
| Distillations ledger / stats (CARD-0330) | GET | `/api/distillations?since=&outcome=&feedback=&limit=`, `/api/distillations/stats?since=` — `scripts/distiller.ps1 -Stats` / `-List [-Flagged]` |
| Distillation feedback (CARD-0330) | POST | `/api/agent-tasks/{id}/distillation/feedback` `{ verdict: Good\|Lost\|Noisy, note? }` — 409 if the task has no distillation. `delegate.ps1 -Flag <id> -Verdict Lost\|Noisy\|Good [-Note]` |
| Home Tasks rail (cards + unbound delegations) | GET | `/api/home/tasks` |
| What needs a human (fleet-global) | GET | `/api/attention` |
| Issue / list / rotate / revoke a Delegation Capability (CARD-0398) | POST / GET / POST rotate / POST revoke | `/api/delegation-capabilities`, `/api/delegation-capabilities/{id}`, `…/rotate`, `…/revoke` — `scripts/capability.ps1`. GET never returns the token. |
| A session's screen | GET | `/api/sessions/{id}/buffer` |
| A session's transcript | GET | `/api/sessions/{id}/transcript?since={sequence}` |
| Type work into a session | POST | `/api/sessions/{id}/messages` |
| Schedules for an agent / card | GET | `/api/schedules?agentId=` / `?cardId=` (`scripts/schedule.ps1`) |
| Kill a session | POST | `/api/sessions/{id}/kill` |
| Land a succeeded Worktree task | POST | `/api/agent-tasks/{id}/land` (`{ verify?: string }`) — 202 `{ status: "queued" \| "requeued" }`. 409 means a land is running in this server now. Read `Landed` / `AlreadyPresent` / `LandedWithResidue` / `LandRefused` and the structured `landing` evidence on the task. `GET /api/agent-tasks/{id}` exposes `landRequestedAt`, `landStartedAt`, `landAttempt`. Re-POST resumes recorded publication or retries guarded cleanup. `LandRefused` does not imply the local target stayed unchanged. |
| Record/override a stage finding (CARD-0272) | POST | `/api/agent-tasks/{id}/finding` (`RecordStageFindingRequest`: `stage` name, `found` bool, `detail?`). Writes a `Source=Orchestrator` `StageOutcome` row that supersedes the latest for that (task, stage); `delegate.ps1 -Finding <id> -Stage … -Found "…"` / `-Clean`. |
| Per-stage hit rate vs. cost (CARD-0272) | GET | `/api/stage-outcomes` (`since`, `until`, `stage`, `cardId`, `latestOnly` default true) — rows plus a per-stage summary (runs, found/clean/skipped/failed/unreported, hit %, USD spent, USD per finding, server seconds). `scripts/stage-value-report.ps1` prints it as a table. |
| Live runner sessions / rendered screen | GET | `:17204/sessions`, `:17204/sessions/{id}/snapshot` |

```powershell
$api = if ($env:ANTIPHON_API) { $env:ANTIPHON_API } else { 'http://localhost:17202' }
$h = @{}
if ($env:ANTIPHON_TASK_TOKEN) { $h['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }

# who is running what -- liveSession is null when the agent is not up
Invoke-RestMethod "$api/api/agents" -Headers $h |
    Select-Object name, status, @{n='session';e={$_.liveSession.id}}

# a board's id from its name, then its cards
$board = (Invoke-RestMethod "$api/api/boards" -Headers $h | Where-Object name -eq 'Antiphon')
Invoke-RestMethod "$api/api/cards?boardId=$($board.id)" -Headers $h | Select-Object identifier, title, status

# column name -> column id, without pulling the whole board
Invoke-RestMethod "$api/api/boards/$($board.id)/columns" -Headers $h

# what a session has actually said, newest tail
Invoke-RestMethod "$api/api/sessions/$sessionId/transcript?since=0" -Headers $h

# give a running session work (queued, not typed raw -- see below)
Invoke-RestMethod "$api/api/sessions/$sessionId/messages" -Method Post -Headers $h `
    -ContentType 'application/json' -Body '{"body":"status please","mode":"WhenIdle"}'

# start an agent fresh; the body is required even when it is empty
Invoke-RestMethod "$api/api/agents/$agentId/start" -Method Post -Headers $h `
    -ContentType 'application/json' -Body '{"fresh":true}'
```

## Shapes that bite

- **PLURAL, OR 404.** `/api/boards`, `/api/agents`, `/api/cards`. There is no `/api/board`, and a
  404 from it says nothing about whether the board exists.

- **THERE IS NO `GET /api/sessions`.** The server exposes sessions only by id. To find one, read
  `liveSession` off `GET /api/agents`, or ask the runner: `GET http://localhost:17204/sessions`
  lists every session the runner still knows about, live and exited.

- **`GET /api/cards` REFUSES AN UNFILTERED READ.** At least one of `boardId`, `status` or
  `updatedSince` is required; without one it is a **400** whose detail says exactly that. There is
  no `limit` or `pageSize` — a `?limit=1` probe is the 400, not a paging failure. Filter, then take
  what you need client-side.

- **SNAPSHOT IS RUNNER-ONLY.** `GET :17204/sessions/{id}/snapshot` renders the screen; the server
  has `buffer` and `transcript` and no snapshot at all. Grepping `SessionEndpoints.cs` for it finds
  nothing because it was never there.

- **AGENT, BOARD AND SESSION IDS ARE GUIDS, WITH A ROUTE CONSTRAINT.** Only cards resolve a
  friendly identifier. There is no `GET /api/agents/{name}` and no slug resolver: list, filter by
  `name` or `slug`, then use the guid. A non-guid segment does not 400 with a helpful message — it
  simply does not match the route.

- **`working` IS TRANSCRIPT-DERIVED, NEVER CHANNEL SILENCE.** `GET /api/agents` `working` is
  `IsWorkingAsync`. A channel-bound agent idle between Antiphon notes is waiting; read `working`
  and the transcript, never the chat's silence. Catalog `lastMessageAt` is inbound only;
  `lastReplyAt` is the last outbound reply.

- **`POST /api/agents/{id}/start` REQUIRES A JSON BODY.** `{}` is the minimum and inherits the
  agent's persisted settings; no body at all is a **400** from model binding before the request
  ever reaches the agent. `{"fresh":true}` forces a brand-new conversation — the default resumes
  the agent's previous session so the terminal picks up where it left off. `remoteControl` overrides
  the persisted flag for this launch only. A start can refuse **409** `subscription_quota_low` or
  `model_disabled`; both are refusals, not warnings on a launch that happened.

- **HERDR RETRY HOLD REQUIRES AN EXPLICIT ACKNOWLEDGEMENT (CARD-0388).** A held dead-agent
  Start returns 409 `herdr_supervision_held` before other launch guards or named-placement
  preflight. Read `supervision.herdrConsecutiveFailures`, `herdrFailureHeldAt` and
  `lastHerdrFailureKind` on the agent; Attention remains until acknowledged, even after incident
  pruning or AlwaysOn off. After inspecting and repairing the cause, POST the same start route
  with `{"resetHerdrFailureHold":true}`; `fresh:true` is optional. The UI's **Retry and resume**
  sends this flag. Acknowledgement commits before normal validation, so a later model, rules,
  provider or placement refusal can leave the hold cleared without launching. A live Start is
  idempotent and preserves the hold even with the flag. Stop, attach and automatic callers do
  not clear it. The hold neither kills panes nor reroutes channel input; held inbound uses existing
  drop reporting without a chat notice. See [Herdr supervision and standing ownership](herdr-sessions.md#supervision-hold-and-explicit-recovery-card-0388)
  before repairing or handing over a channel-bound seat.

- **STOP IS THE NAMED-AGENT KILL, AND IT SUSPENDS SUPERVISION.** `POST /api/agents/{id}/stop` kills
  the live session and, on an `alwaysOn` agent, suspends its supervision until a manual start —
  deliberate, so restart supervision never fights a human. An always-on agent that "won't come back"
  was usually stopped.

- **ARCHIVE IS `POST`; `DELETE` MEANS DELETE.** `POST /api/boards/{id}/archive` (reason required) is
  the reversible hide; `DELETE /api/boards/{id}` really removes the board and detaches its agents.
  `DELETE /api/agents/{id}` is a **hard delete** with no archive and no running/always-on guard — it
  releases the agent's cards and drops its workflow runs, and there is nothing to unarchive
  afterwards. Grepping for `MapDelete` and stopping there is how an archive gets done as a delete.

## Typed input goes through the queue

`POST /api/sessions/{id}/messages` with `{"body":"...","mode":"Now"|"WhenIdle"}` (default
`WhenIdle`, which holds until the agent finishes its turn). That queue owns the delivery contract —
LF, bracketed paste, and a separate Enter — and the delivery verification that goes with it.

`POST /api/sessions/{id}/input` (`{"input":"..."}`) is a raw keystroke bypass, and the runner's
`POST :17204/sessions/{id}/input` is a further bypass beneath that. Neither is for work bodies:
they skip the paste contract and nothing records whether the prompt landed. See
[session-runtime-invariants.md](session-runtime-invariants.md) for why, and treat
transcript-confirmed `UserPrompt` evidence — not a screen redraw — as the delivery verdict.

## Killing

`POST /api/agents/{id}/stop` is the front door for a named agent; `POST /api/sessions/{id}/kill`
kills one session and records `OperatorRequest` as the termination source. The session summary
now carries `terminationSource`; `Unknown` on a row closed after CARD-0316 ships is a bug to
file, not a state. The runner's
`POST :17204/sessions/{id}/kill` and `POST :17204/sessions/kill-all` bypass all of that
bookkeeping — last resort only, and `kill-all` is scorched earth across every session on the box.

A released process must end in one of three states — killed, pooled warm, or owned by a standing
agent (CARD-0221). A stalled session is a detection and decision state, never an automatic kill.

## Not here

Review/files, tracker sync, workflows, gates, channels, settings and delegation routes are in
[antiphon-api.md](antiphon-api.md), which also explains why there is no OpenAPI document.
Delegation is `scripts/delegate.ps1` ([orchestration-loop.md](orchestration-loop.md)); card writes
are `scripts/card.ps1`. There is deliberately **no `scripts/agents.ps1` wrapper** (CARD-0296): this
surface is almost all GET, and the one write that bites must go through the message queue rather
than a script that could quietly reimplement it. If turning an agent name into a guid keeps
hurting, that is a new card, not a helper smuggled in here.

## Card-file publication (CARD-0408)

[Card-file privacy](card-file-privacy.md) owns opt-in settings/status/sync, the
explicit private-notes boundary, private snapshots, revocation and cleanup.
Boards default off and Unknown repository visibility blocks publication. Private
notes do not appear in ordinary DTOs or generated markdown. Outcome and archive
reasons remain public fields on eligible cards. Cleanup pending is independent of
card/session state; disabling the feature freezes existing exports.

Inspect the agent/session and Stop active work before selecting history. Read
`GET /api/agents/{id}/sessions?take=25&before={session-guid}` for bounded metadata history;
`nextBefore` is the next cursor. GET never adopts ownership and Start revalidates it.
POST `/api/agents/{id}/start` with exactly one of `resumeSessionId`, `retryContinuity:true`,
or `fresh:true`. The first selects an owned Antiphon session GUID; retry uses the current
target after repair; Fresh explicitly creates a new ID and keeps the old row. Acceptance
means queued, not proven recovered. These options do not reset Herdr's independent hold.

Historical ownership is the immutable physical standing-agent ID, or unambiguous legacy
current-pointer, task execution or Crash/RestartScheduled/Recovered incident evidence.
ParentSessionId, cwd, name and knowledge of an ID never prove ownership. Missing or
contradictory evidence refuses `standing_resume_owner_unproven`; a different stamped
owner refuses `standing_resume_not_owned`. Deleting/recreating a name does not transfer
ownership. This cannot reconstruct native history overwritten by the former same-ID fallback.

Selecting older history refuses live/queued sessions, pending card work and open execution
assignments. Only never-attempted pending non-rules input moves, appended in source order
after the target queue. Any delivery baseline, timestamp, verdict or settlement evidence
refuses the switch (including manual Fresh) with `standing_resume_delivery_pending`.
Resolve that input using existing queue controls. Transcript and task history stay separate.

Land detail also includes `landRequest`: request/hold/progress clocks, holder, attempts,
reconciliation disagreement and notification states with destination, queue ID and
confirmed prompt sequence. POST land returns additive `requestId` and `notification`.
Repeated inactive pending requests preserve age and identity. `delegate.ps1 -Status`
prints delegate, land, publication/cleanup and receipt separately. The Attention view
projects held and aged requests and unresolved receipts independently of task openness.
