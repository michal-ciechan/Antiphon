# The Antiphon HTTP API

Everything Antiphon's own UI does, it does over this API — and so can a script, a scheduled job,
or an agent. This document is the orientation and the map: the conventions every endpoint follows,
the complete route surface grouped by area, the real-time channel, and the front doors that already
wrap the common calls.

It is **not** a generated schema reference, and it deliberately does not restate every request DTO.
Where a body's exact shape matters, the DTO record in `server/Application/Dtos/` is the authority
and is named here.

## 0. Read this first: there is no authentication

`CurrentUserMiddleware` resolves **every** request to a hardcoded seeded admin
(`a0000000-0000-0000-0000-000000000001`, `admin`). There is no `UseAuthentication`, no
`UseAuthorization`, and no `RequireAuthorization` anywhere in `server/Program.cs`.

Some endpoints are correspondingly powerful: `GET /api/filesystem/browse` enumerates arbitrary
directories on the host, and its own source comment says so — *"intended for the single-user
localhost dev tool only. If Antiphon ever becomes multi-user, gate this behind auth."*

**Do not expose this port beyond localhost or the operator's tailnet.**

## 1. Base URL and conventions

| | |
|---|---|
| Base URL | `http://localhost:17202` (Aspire stack). Simple mode is `17281`. Scripts read `$env:ANTIPHON_API` and fall back to `http://localhost:17202`. |
| Content type | `application/json` |
| Property casing | camelCase |
| Enums | **names, not integers.** `JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)` — `"High"`, not `2`. A numeric token is a 400, not a silent parse. |
| Errors | RFC 9457 `application/problem+json` |
| Health | `GET /health` (includes a PostgreSQL check) |
| Real-time | SignalR at `/hubs/antiphon` |

### Error shape

```json
{
  "type":    "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title":   "Not Found",
  "status":  404,
  "detail":  "Card 41 was not found.",
  "traceId": "0HN7…",
  "code":    "herdr_refused",
  "errors":  { "agentKind": ["OpenCode is not a delegate kind. …"] }
}
```

- `code` is present when the exception carried a **stable machine-readable code**. `conflict` and
  `validation_failed` are the generic ones; the specific ones worth branching on are
  `herdr_refused`, `subscription_quota_low`, `channel_disabled`, `profile_not_found`,
  `profile_resolution_unavailable`, `profile_revision_conflict`.
- `errors` is present on validation failures (422), keyed by field.
- Additional keys may be spliced in from an exception's `Extensions`.
- `traceId` is the request's trace identifier; it is also on the correlated log lines.
- Body-bind failures (an unknown enum name, a numeric enum token) are mapped to **400 with the
  JSON error's own message** — they used to surface as an opaque 500.

Statuses the middleware maps deliberately: **400** bad request, **403** forbidden, **404** not
found, **409** conflict (concurrency, or a refusal carrying a `code`), **422** validation
(`errors` present), **503** unavailable. Anything else is a 500 with the detail flattened to
*"An unexpected error occurred."* and the real exception in the server log against the same
`traceId`.

### Concurrency tokens

Card and board writes are compare-and-swap: the request carries the entity's current
`concurrencyToken` (a guid), and the server rotates it on every write. A stale token is a 409.

Two truly concurrent writers also collide on the database's unique `(CardId, RevisionNumber)`
index, and every content write has been revision-logged since CARD-0019 — so a clobber is readable
and reversible from `GET /api/cards/{id}/revisions`.

### Card identifiers

Anywhere a card id appears in a route it is a **string**, resolved by `CardService.ResolveCardIdAsync`.
`CARD-0051`, `card-51`, `#51`, `51`, and the guid all address the same card. There is no separate
lookup step.

Every card carries a `CARD-nnnn` identifier — including cards a tracker sync imported (CARD-0175).
A foreign tracker's own key (`ANT-12`) additionally resolves through the card's
`ExternalIssueRef.ExternalKey`, and the key and its link are on `CardDto.externalIssue`. `#N` is
**not** routed to a tracker number: it is the canonical entry form of `CARD-000N` and nothing else.

## 2. The surface

Twenty-two endpoint files under `server/Api/Endpoints/`, mapped in `server/Program.cs`. Each row
below is a route group; the file named is the authority for its exact bodies.

### Work items — cards, boards, projects

`CardEndpoints.cs`, `BoardEndpoints.cs`, `ProjectEndpoints.cs`

```
GET    /api/cards/limits                     title/description/reason/actor length ceilings
GET    /api/cards/{id}                       one card
GET    /api/cards/{id}/thread                card + its plans, tasks and commits (read-only projection)
PATCH  /api/cards/{id}                       move (column + concurrencyToken + reason, optional spawn)
PATCH  /api/cards/{id}/content               title/description edit — revision-logged
GET    /api/cards/{id}/revisions             every content write, with actor and reason
POST   /api/cards/{id}/spawn                 start an agent on this card
POST   /api/cards/{id}/archive | /unarchive | /reopen
GET    /api/cards/{id}/diff                  the card's branch diff
POST   /api/cards/{id}/comments
GET    /api/cards/{id}/discussion  POST /api/cards/{id}/discussion
POST   /api/cards/{id}/pr                    open a pull request for the card

GET    /api/boards  |  /api/boards/{id}  |  /api/boards/{id}/columns
POST   /api/boards            DELETE /api/boards/{id}
POST   /api/boards/{id}/cards                create a card on this board
GET    /api/boards/{id}/workflow   PUT /api/boards/{id}/workflow    the board's workflow YAML

GET    /api/projects  |  /api/projects/{id}
POST   /api/projects   PUT /api/projects/{id}   DELETE /api/projects/{id}
GET    /api/projects/{id}/deletion-impact
POST   /api/projects/test-connectivity
```

> A move into an active column **does not start an agent** unless `spawn: true` (CARD-0051), and
> the orchestrator tick will not pick that card up either (CARD-0087).

### Agents and their sessions

`AgentEndpoints.cs`, `SessionEndpoints.cs`

```
GET    /api/agents  |  /api/agents/{id}
GET    /api/agents/definitions               the configured Agents:Definitions catalogue
GET    /api/agents/preamble-preset?provider=  telegram | slack (404 for anything else)
GET    /api/agents/bundles                   attachable instruction bundles (read-only; the catalog is code)
POST   /api/agents        POST /api/agents/draft        PATCH /api/agents/{id}    DELETE /api/agents/{id}
GET    /api/agents/{id}/incidents
POST   /api/agents/{id}/start  |  /stop
POST   /api/agents/{id}/queue   PATCH /api/agents/{id}/queue   DELETE /api/agents/{id}/queue/{cardId}

POST   /api/sessions                         launch an interactive session
GET    /api/sessions/{id}/buffer             the screen mirror
GET    /api/sessions/{id}/transcript         normalized transcript entries
POST   /api/sessions/{id}/input              type into the session
GET    /api/sessions/{id}/commands           the slash-command catalogue for this session
GET    /api/sessions/{id}/messages           the delivery queue
POST   /api/sessions/{id}/messages           enqueue (Now / WhenIdle)
DELETE /api/sessions/{id}/messages/{messageId}
POST   /api/sessions/{id}/messages/{messageId}/send-now
POST   /api/sessions/{id}/resize  |  /resume  |  /kill
```

`PATCH /api/agents/{id}` is where `alwaysOn`, `kind`, `tuiProfileId`, `modelId`, `launchEnv`,
`bundleKeys` and `sessionBackend` are set — and where the herdr pairing gate fires
(`409 herdr_refused`). See [herdr-sessions.md](herdr-sessions.md).

### Delegation

`AgentTaskEndpoints.cs` — the API behind `scripts/delegate.ps1`.

```
POST   /api/agent-tasks                      create (CreateAgentTaskRequest)
GET    /api/agent-tasks  |  /api/agent-tasks/{id}      {id} accepts the 8-char short id
POST   /api/agent-tasks/{id}/cancel  |  /retry  |  /escalate
POST   /api/agent-tasks/{id}/reply           answer a Blocked delegate's question
POST   /api/agent-tasks/{id}/refine          steer a running delegate without cancelling it
```

`CreateAgentTaskRequest` (`server/Application/Dtos/AgentTaskDtos.cs`) is the biggest body in the
API and is fully commented in place. The fields that change behaviour most: `role`, `kind`
(`Worker` / `Orchestrator`), `modelLevel`, `agentKind` (ClaudeCode / Grok / Codex — see
[agent-kinds.md](agent-kinds.md)), `workspace`, `workingDirectory`, `scopeGlob`, `followUpOnTask`,
`expectedMinutes`, `envOverride`, `ignoreSubscriptionQuota`.

> `POST /api/agent-tasks` can refuse with **409 `subscription_quota_low`** (CARD-0136). That is a
> launch refusal, not a warning attached to a launch that already happened. Retry with
> `ignoreSubscriptionQuota: true`, or pick another `agentKind`/agent. The dispatcher never refuses;
> it only records an informational warning.

### Runner profiles and credentials

`AgentTuiEndpoints.cs`, `ApiKeyEndpoints.cs` — see
[ai-agent-tui-configuration.md](ai-agent-tui-configuration.md) and
[agent-credentials.md](agent-credentials.md).

```
GET    /api/agent-tui/runner-types                           per-kind capability catalogue
GET    /api/agent-tui/profiles     POST /api/agent-tui/profiles
GET    /api/agent-tui/profiles/{id}   PATCH …   DELETE …   POST …/duplicate
PUT    /api/agent-tui/profiles/{id}/secrets/{environmentName}    write-only
DELETE /api/agent-tui/profiles/{id}/secrets/{environmentName}
GET    /api/agent-tui/profiles/{id}/models    POST …/models/refresh
GET    /api/agent-tui/profiles/{id}/capabilities
POST   /api/agent-tui/profiles/{id}/validate  GET /api/agent-tui/validation-runs/{runId}
GET    /metrics/agent-tui                                    (root, not under /api)

GET    /api/api-keys   GET /api/api-keys/global
PUT    /api/api-keys/{name}          DELETE /api/api-keys/{id}
GET    /api/projects/{projectId}/api-keys    PUT /api/projects/{projectId}/api-keys/{name}
```

Secret and key **values are write-only** — nothing reads one back out over HTTP.

### Channels

`ChannelEndpoints.cs`

```
GET    /api/channels                         the catalog (rows appear on first inbound message)
PATCH  /api/channels/{id}                    bind/unbind an agent, preamble, enable/disable
POST   /api/channels/{id}/send               proactive send — {"text": "..."}
```

`POST …/send` is a **proactive** push (a scheduled job or an operator script), not a reply to
anything inbound: it bypasses the alert throttle/digest path entirely and produces a `ChannelReply`
straight onto `channels.outbound`. A disabled channel is `409 channel_disabled`. It was landed as
pre-design groundwork on CARD-0171 and is unratified — treat it as present but provisional.

The whole inbound/outbound model is [messaging/build-your-own-gateway.md](messaging/build-your-own-gateway.md).

### Orchestration and workflows

`OrchestratorEndpoints.cs`, `WorkflowEndpoints.cs`, `GateEndpoints.cs`, `CascadeEndpoints.cs`,
`ArtifactEndpoints.cs`

```
GET    /api/orchestrator/state    POST /api/orchestrator/pause | /resume | /tick

GET    /api/workflows  |  /api/workflows/{id}     POST /api/workflows
POST   /api/workflows/{id}/pause | /resume | /abandon | /visit | /close
DELETE /api/workflows/{id}        GET /api/workflows/{id}/delete-info
GET    /api/workflows/{id}/branch-diff  |  /file-content
GET    /api/projects/{projectId}/feature-status/{featureName}

POST   /api/workflows/{id}/gates/approve | /reject | /prompt | /comment | /go-back
POST   /api/workflows/{id}/cascade        GET /api/workflows/{id}/cascade/affected

GET    /api/workflows/{id}/artifacts
GET    /api/workflows/{id}/artifacts/{stageId}
GET    /api/workflows/{id}/artifacts/{stageExecutionId}/section-reviews
POST   /api/workflows/{id}/artifacts/{stageExecutionId}/section-reviews
DELETE /api/workflows/{id}/artifacts/{stageExecutionId}/section-reviews/{**sectionPath}
```

### Review surface

`ReviewEndpoints.cs` — the agent Files review UI: workspace file listing (git ∪ agent activity),
content/diff reads, viewed/reviewed marks, and inline comment threads with agent dispatch.

```
GET    /api/agents/{agentId}/files  |  /files/commits  |  /files/tree  |  /files/content  |  /files/diff
POST   /api/agents/{agentId}/files/ignore/preview  |  /files/ignore  |  /files/review
GET    /api/agents/{agentId}/review/sections   POST …/review/sections   POST …/review/checkpoint
GET    /api/agents/{agentId}/review/threads    POST …/review/threads
POST   /api/review/threads/{threadId}/comments | /dispatch | /resolve
```

### Issue-tracker sync

`TrackerSyncEndpoints.cs` — see [workflow-tracker-block.md](workflow-tracker-block.md).

```
POST   /api/boards/{id}/tracker/sync         bidirectional push for one board
POST   /api/tracker-sync/run                 (root route) every activated board
```

Reads happen on the orchestrator tick (`Orchestrator:TrackerSyncIntervalMinutes`, default 30).
**Writes never run from the tick** — only from these two routes, the board's "Sync tracker now"
button, `scripts/github-sync.ps1`, or the Windmill job.

### Everything else

```
GET    /api/attention                        the one "what needs a human" projection — fleet-global and unfiltered
GET    /api/plans  |  /api/plans/content     read-only projection over plan markdown in the repo (git is the store; there is no write path and there will not be one)
GET    /api/audit  |  /cost-summary  |  /cost-ledger  |  /conversation    DELETE /api/audit/archive
GET    /api/github/status  |  /repos  |  /repos/{owner}/{repo}/branches   POST /api/github/repos/refresh
GET    /api/filesystem/browse  |  /workspaces  |  /worktrees              ⚠ enumerates host directories
GET/POST/PUT/DELETE /api/settings/templates | /template-groups | /providers
GET    /api/settings/templates/{id}/stages
POST   /api/settings/providers/{id}/test
GET/POST /api/settings/providers/{id}/model-routing   PUT|DELETE /api/settings/model-routing/{routingId}
GET    /health                               liveness + PostgreSQL
```

## 3. Real-time (SignalR)

Hub: `/hubs/antiphon` (`AntiphonHub`). Client-callable methods: `JoinGroup`, `LeaveGroup`,
`JoinCard`, `LeaveCard`, `SendAsync`, `DelegateCard`.

Groups are how you scope a subscription. The two naming schemes in use:

| Group | Carries |
|---|---|
| `session-{sessionId}` | `AgentTextDelta`, `AgentToolCall`, `AgentToolResult`, `AgentActivityUpdate`, `AgentPromptReceived`, `UserPromptSent`, `SessionStarted`, `SessionExited`, `SessionResumed`, `SessionAdopted`, `SessionTranscript`, `SessionQueueChanged` |
| `workflow-{workflowId}` | `WorkflowStatusChanged`, `WorkflowCompleted`, `StageStarted`, `StageCompleted`, `GateReady`, `GateActioned`, `GateApproved`, `GateRejected`, `CascadeTriggered`, `CascadeCompleted` |

Broadcast to everyone (no group): `AgentChanged`, `AgentTaskChanged`, `BoardChanged`,
`CardChanged`, `ChannelChanged`, `OrchestratorTick`, `RunAttemptChanged`, `ReviewThreadChanged`,
`SessionFinished`.

The client's event → query-invalidation mapping is in
[project-context.md](project-context.md#signalr---query-invalidation-mapping); the authority for
the event names is the `IEventBus` call sites in `server/Application/Services/`.

## 4. The session-runner's own API (internal)

The session-runner is a **separate process** on `http://localhost:17204` with its own small HTTP
surface. The server is its only client; nothing external should call it, and it is documented here
so nobody mistakes it for the public API.

```
GET  /capabilities        pty backend decision, transcript formats, runner build, session backends
GET  /sessions  |  /sessions/{id}
POST /sessions            RunnerLaunchRequest
GET  /sessions/{id}/buffer | /snapshot | /transcript
POST /sessions/{id}/input | /clear-live-buffer | /resize | /kill
POST /sessions/kill-all   scorched earth
GET  /events              SSE
```

`GET /capabilities` is worth knowing about even from outside: it is how you check which pty backend
is actually serving (`InboxConhost` vs `ModernConPty`) and whether the runner advertises `herdr`.

## 5. Front doors you probably want instead

For the common operations, use the scripts — they handle base-URL resolution, card-id forms,
concurrency tokens and PowerShell's quoting hazards.

| Script | For |
|---|---|
| `scripts/card.ps1` | `get`, `history`, `new`, `edit`, `move`, `close`, `reopen`, `archive`, `unarchive`, `-Limits`. Long text via `-DescriptionFile` / `-ReasonFile`, never inline. |
| `scripts/delegate.ps1` | create / `-Status` / `-Reply` / `-Refine` a delegated task |
| `scripts/github-sync.ps1` | a bidirectional tracker push |
| `scripts/checkpoint-task.ps1` | WIP-commit a stalled task's work without killing it |
| `verify-dev-stack.ps1` | health check across the whole stack |

Both `card.ps1` and `delegate.ps1` read `$env:ANTIPHON_API`, defaulting to
`http://localhost:17202`. Inside a running agent session, `ANTIPHON_API` is already set.

## 6. Why there is no OpenAPI document (yet)

There is no Swagger/OpenAPI generation wired up: `server/Program.cs` has no `AddOpenApi()`,
`MapOpenApi()`, or Swashbuckle. This was reviewed on CARD-0172 and **hand-written documentation was
chosen over generating a spec**, for a specific reason rather than inertia:

Every endpoint returns an **untyped `IResult`** (`Results.Ok(await service.…())`) and not one
carries a `Produces<T>()` annotation. .NET's OpenAPI generator can infer *paths, methods and
request bodies* from that, but **no response schemas at all**. The document it produced today would
be authoritative-looking and half-empty — which is worse than a page that states its own limits and
names the DTO files.

Every group already carries `.WithTags(...)`, so the groundwork exists. Making generation genuinely
worth it is a real piece of work, not two lines:

1. add `Microsoft.AspNetCore.OpenApi`, `AddOpenApi()` / `MapOpenApi()`;
2. annotate the endpoints with `Produces<T>` / `TypedResults` so response schemas exist;
3. decide whether an unauthenticated schema endpoint should be exposed at all, given §0.

If you want that, file it as a card. Until then this page plus `server/Application/Dtos/` is the
reference.

## See also

- [agent-kinds.md](agent-kinds.md) · [agent-credentials.md](agent-credentials.md) ·
  [herdr-sessions.md](herdr-sessions.md)
- [messaging/build-your-own-gateway.md](messaging/build-your-own-gateway.md) — the Kafka side,
  which is a contract rather than an HTTP API.
- [orchestration-loop.md](orchestration-loop.md) — what to call, in what order, to get work done.
- [project-context.md](project-context.md) — API naming conventions and layer boundaries.
