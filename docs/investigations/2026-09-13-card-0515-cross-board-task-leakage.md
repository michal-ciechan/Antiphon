# CARD-0515 cross-board task leakage investigation

Date: 2026-09-13. Investigate task `cc9000c7`.

## Verdict

Confirmed. `GET /api/agent-tasks` is a fleet-wide list with no board or project
predicate. A caller using it to answer an Antiphon-board question therefore receives
other repositories' tasks, while the list DTO does not carry a board or project label
to make the scope mismatch visible.

## Reproduction from the running store

At 2026-09-13, against `http://localhost:17202`, the following two requests were
made with the same status query:

```text
GET /api/agent-tasks?status=Queued,Dispatched,Working,Blocked
GET /api/agent-tasks?status=Queued,Dispatched,Working,Blocked&boardId=00000000-0000-0000-0000-000000000000&projectId=00000000-0000-0000-0000-000000000000
```

Both returned 12 rows, in the same ID order. The second response still contained
these foreign-repository rows:

| Task ID | Status | Card | Working directory |
|---|---|---|---|
| `58b504c4-8840-4930-a5c0-a24052e9a8af` | Blocked | null | `C:\\src\\gym-stat` |
| `58c746cc-6950-4a04-adb2-80332b5349b3` | Blocked | null | `C:\\src\\gym-stat` |
| `6a1fd14f-128d-4116-b5de-1caea80da24e` | Blocked | null | `C:\\src\\gym-stat` |
| `92952c11-d88c-43ef-ac61-97df48289c0b` | Blocked | `CARD-0039` | `C:\\src\\markdown-package` |
| `4aa3b3a3-72b1-41d2-84d7-307c0f758b4e` | Dispatched | `CARD-0005` | `C:\\src\\slides` |

The same response also included Antiphon rows, including this investigation
(`cc9000c7`, `CARD-0515`, `C:\\src\\Antiphon`). This reconstructs the reported
mixed-board "what is in flight" result from current stored rows rather than an
inference from titles.

## Endpoint and response evidence

- The published route contract lists only `rootId`, `status`, `includeChecks`, and
  `since`, and explicitly says omitting filters returns the full table:
  [docs/antiphon-api.md](../antiphon-api.md:245).
- The route handler binds exactly those four inputs and passes no board/project
  value to the service: [AgentTaskEndpoints.cs](../../server/Api/Endpoints/AgentTaskEndpoints.cs:32).
- `ListAsync` starts from all `AgentTasks`; its only predicates are root, status,
  specialist visibility, and the settled-row time window:
  [AgentTaskService.cs](../../server/Application/Services/AgentTaskService.cs:1386).
- The returned summary has `CardId` and a denormalised `CardIdentifier`, but no
  `BoardId`, board name, `ProjectId`, or project name:
  [AgentTaskDtos.cs](../../server/Application/Dtos/AgentTaskDtos.cs:165) and
  [AgentTaskService.cs](../../server/Application/Services/AgentTaskService.cs:2418).

Thus an unrecognised `boardId`/`projectId` query key is not a supported server-side
filter, and callers cannot reliably identify a card-number collision across boards
from the list response alone.

## Call-site census

Production callers of the list route are centralised in
`client/src/api/agentTasks.ts`. `queryForAgentTasks` constructs only `includeChecks`,
`since`, and `status` ([agentTasks.ts](../../client/src/api/agentTasks.ts:580));
`useAgentTasks` performs that GET ([agentTasks.ts](../../client/src/api/agentTasks.ts:608)).

| Surface | Call shape and risk |
|---|---|
| Delegations board | Calls `useAgentTasks(false, { since: 'active' })` ([DelegationsBoard.tsx](../../client/src/features/delegations/DelegationsBoard.tsx:35)). It intentionally shows the fleet but its “working” header combines every board's rows and fleet summary, so it is unsafe if represented as an Antiphon-board-only answer. |
| Delegations history | Calls the same hook with window/status only ([DelegationsHistory.tsx](../../client/src/features/delegations/DelegationsHistory.tsx:44)); it is likewise fleet-wide and has no board/project label. |
| Orchestrator page | Hosts both of those surfaces under a page described as “what is the fleet doing right now” ([OrchestratorPage.tsx](../../client/src/features/orchestrator/OrchestratorPage.tsx:18)). The wording correctly says fleet, but no scope marker distinguishes rows. |
| `delegate.ps1` | No production `GET /api/agent-tasks` list call exists. Its GETs are task detail (`/{id}`) and areas; `-Status` is detail-only ([delegate.ps1](../../scripts/delegate.ps1:492)). |
| `card.ps1` | No `GET /api/agent-tasks` call exists; card history uses `/api/cards/{id}/revisions` ([card.ps1](../../scripts/card.ps1:529)). |
| Check interpreter | No HTTP/tool call exists by contract; it reads only the supplied bundle ([check-interpreter.md](../../server/Bundles/check-interpreter.md:33)). |

The request-string search found no other production direct caller; test handlers and
fixtures are excluded from the census because they do not invoke the server route.

## Other fleet projections

- `GET /api/agent-tasks/summary` is fleet-wide by implementation: it begins from all
  non-specialist tasks and emits aggregate counts/cost ([AgentTaskService.cs](../../server/Application/Services/AgentTaskService.cs:1417)). Its API documentation labels it “fleet-wide counters”
  ([docs/antiphon-api.md](../antiphon-api.md:264)); it is not silently presented as a board
  filter, but the Delegations surfaces combine it with unlabelled list rows.
- `GET /api/agent-tasks/pipeline` is deliberately a fleet projection in both handler
  comment ([AgentTaskEndpoints.cs](../../server/Api/Endpoints/AgentTaskEndpoints.cs:48)) and
  service query, which selects all open non-specialist tasks
  ([AgentTaskPipelineStatusService.cs](../../server/Application/Services/AgentTaskPipelineStatusService.cs:57)). Its UI explicitly labels itself “Fleet-wide stage glance”
  ([PipelineStagesPanel.tsx](../../client/src/features/orchestrator/PipelineStagesPanel.tsx:17)).
- `GET /api/attention` is also fleet-global by documented service contract
  ([AttentionService.cs](../../server/Application/Services/AttentionService.cs:15)) and reads all
  open/blocked tasks ([AttentionService.cs](../../server/Application/Services/AttentionService.cs:158)). This is intentional for a human-attention queue, but has the same absence of an endpoint scope parameter if a caller needs board-local attention.

## Remaining uncertainty

This investigation did not recover the original orchestrator transcript, so it does
not establish which non-product caller issued the unfiltered request on the reported
session. The endpoint behaviour, live rows, and all in-repository production callers
are confirmed. The live store can change after this capture; the task IDs above are the
durable reproduction anchors.

## Not done, noted

No fix was designed or implemented; a Plan should decide the compatible board/project filter contract, response scope labels, tests, and which intentionally fleet-wide UI/documentation remains explicitly labelled.
