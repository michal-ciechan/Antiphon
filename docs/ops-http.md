# Inspecting agents, boards and live sessions over HTTP

The operator's map of the surface an orchestrator actually reaches for: which agents exist, what
they are running, which boards and cards are open, and what a live session is doing. It is
deliberately short and it is not the route map — [antiphon-api.md](antiphon-api.md) is that, and
`server/Api/Endpoints/*.cs` plus `src/Antiphon.SessionRunner/Program.cs` are the authority over
both. Cards are worked through `scripts/card.ps1` (`server/Bundles/board-api.md` for the raw card
API); nothing here replaces that.

**Do not grep `MapGet` to find a route.** The one route this page cannot give you is a route this
page says does not exist.

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

## The jobs you have

| Need | Method | Path |
|---|---|---|
| Every agent, with its live session | GET | `/api/agents` |
| One agent | GET | `/api/agents/{id:guid}` |
| Start / stop an agent | POST | `/api/agents/{id}/start`, `/api/agents/{id}/stop` |
| Delete an agent | DELETE | `/api/agents/{id}` |
| Boards | GET | `/api/boards` (`?includeArchived=true`) |
| One board | GET | `/api/boards/{id}` (`?view=summary`, `?includeArchived=`) |
| A board's columns, name to id | GET | `/api/boards/{id}/columns` |
| A board's cards | GET | `/api/cards?boardId={guid}` |
| One card | GET | `/api/cards/{id}` — `CARD-0296` resolves; prefer `card.ps1 get` |
| A session's screen | GET | `/api/sessions/{id}/buffer` |
| A session's transcript | GET | `/api/sessions/{id}/transcript?since={sequence}` |
| Type work into a session | POST | `/api/sessions/{id}/messages` |
| Kill a session | POST | `/api/sessions/{id}/kill` |
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

- **`POST /api/agents/{id}/start` REQUIRES A JSON BODY.** `{}` is the minimum and inherits the
  agent's persisted settings; no body at all is a **400** from model binding before the request
  ever reaches the agent. `{"fresh":true}` forces a brand-new conversation — the default resumes
  the agent's previous session so the terminal picks up where it left off. `remoteControl` overrides
  the persisted flag for this launch only. A start can refuse **409** `subscription_quota_low` or
  `model_disabled`; both are refusals, not warnings on a launch that happened.

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
kills one session and records `OperatorRequest` as the termination source. The runner's
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
