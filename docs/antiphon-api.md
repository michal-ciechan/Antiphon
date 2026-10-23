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

**Do not expose this port beyond localhost or the operator's tailnet.** Operator-only surfaces
(runner force-release, the Hangfire dashboard) need the operator token whatever the client address:
every request reaches Kestrel as loopback (CARD-0653, CARD-0658).

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
  `herdr_refused`, `herdr_grok_native_session_missing`, `remote_control_refused`, `subscription_quota_low`, `provider_sign_in_required`, `concurrency_limit`, `card_identifier_ambiguous`, `channel_disabled`, `profile_not_found`,
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
lookup step. A foreign tracker's key (for example a Jira `ANT-12`) resolves through the card's
external-issue ref; `#N` is always `CARD-000N` and never a GitHub issue number.

### Importance, urgency, and rank (CARD-0039)

A card stores `importance` (`Low | Normal | High | Critical`, default `Normal`) and `urgency`
(`Normal | Soon | Now`, default `Normal`) plus an optional `dueAt`. Responses also carry the
derived triple: `effectiveUrgency` (stored urgency escalated by a due date within 14 days → Soon,
within 3 days or passed → Now), `quadrant` (`DoFirst | Schedule | Clear | Someday`), and `rank`
(lower sorts first). There is no `priority` field. `CreateCardRequest` and `UpdateCardContentRequest`
reject unknown JSON members, so a stale `priority` write is **400**, not a silent no-op.

`PATCH /api/cards/{id}/content` takes optional `importance`, `urgency`, `dueAt`, and `clearDueAt`
(the tri-state for the date; null `dueAt` means unchanged). `GET /api/cards/limits` includes
`importanceValues` and `urgencyValues` from `Enum.GetNames`.

CARD-0544: the same PATCH takes optional `codeVerificationPolicy` / `reviewVerificationPolicy`
(`FullOnly` | `AllowInterim`, default `FullOnly`; unknown strings 422). `CardDto` and revisions
expose both; a revision records the superseded values.

**Position (CARD-0098).** `CardDto.position` is a nullable dense `1..n` inside the card's
(column, rank cell). Null means never placed and sorts after every placed card in that cell.
`PATCH /api/cards/{id}/position` is relative placement: exactly one of `before` / `after` (a card
ref on the same board) or `placement: "Top" | "Bottom"` of the current cell, or both `before` and
`after` when they are adjacent. Optional `importance` / `urgency` overrides are for undo and
scripts. Neighbours' concurrency tokens are not rotated. **409 `card_order_stale`** when named
neighbours are no longer adjacent; **422 `card_position_unreachable`** when the moved card's
`dueAt` escalates it out of the target cell. A column or axis change clears `position`.

**Importance provenance (CARD-0327).** `CardDto.importanceProvenance` (`Auto | Human`) says who
last set `importance`. Setting `importance` on the content PATCH always flips it to `Human`; a
title-only edit (no `importance` in the body) leaves it unchanged. The optional
`importanceProvenance` field on that same PATCH hands it back explicitly — `"Auto"` lets the
tracker sync own the field again, the analogue of `DELETE /api/routing-pins/{id}`. The tracker sync
itself never overwrites a `Human` card's importance; it only ever writes `Auto`. On an import-origin
card (one with an `externalIssue`), `ExternalIssueDto` carries `author` (the tracker login, or null
if the tracker does not report one), `authorIsOperator` (`true | false | null` — null means the
board's `tracker.operator_logins` is unset and the author was never judged), and
`needsHumanReview` (`true` when the card is import-origin, `authorIsOperator == false`, still
`Auto`, still in Backlog, and not archived). See
[workflow-tracker-block.md](workflow-tracker-block.md) for the full authority rule and
[agent-card-lifecycle.md](agent-card-lifecycle.md) for the review marker.

`Identifier` is unique per **board**, not globally. Every `{id}` card route walks the same scope
`delegate.ps1 -Card` uses (CARD-0218), narrowed by two query parameters:

1. explicit `?boardId=` — a fence. One match on that board is the card; zero is a **404** naming
   the boards that *do* hold it. Never falls through to a different board.
2. else the caller's own card's board and standing agent's board (from `X-Antiphon-Task-Token`)
3. else the boards of every project whose `LocalRepositoryPath` contains `?cwd=`
   (`card.ps1` sends `git rev-parse --show-toplevel`)
4. else everywhere

Uniqueness is demanded inside the scope that answers. A collision that survives all of that is
**409 `card_identifier_ambiguous`**, with `detail` listing every candidate (board, guid, status,
title) and a `candidates` extension of `{ id, identifier, title, status, boardId, boardName }`.
A guid is exact and ignores scope. `cwd` is a disambiguation hint, never an authorisation, and
is read on writes as well as reads.

## 2. The surface

Endpoint files under `server/Api/Endpoints/`, mapped in `server/Program.cs`. Each row
below is a route group; the file named is the authority for its exact bodies.

### Work items — cards, boards, projects

`CardEndpoints.cs`, `DiagnosisEndpoints.cs`, `BoardEndpoints.cs`, `ProjectEndpoints.cs`, `HomeEndpoints.cs`

Card detail, card-list and board-detail cards (both full and `view=summary`) carry
`blockers` when the card has `BlockedBy` rows, including resolved rows, and
`openBlockerCount` when the unresolved count is positive. Done, Canceled or archived blockers
are resolved by the shared CardBlockers predicate
(`CardRelationshipApiTests.Card_and_list_views_carry_open_blocker_count_and_blockers_excluding_resolved`
and `CardRelationshipApiTests.Board_views_return_blockers_using_live_resolution_state`).
For a nonempty board result, the board loads these summaries in one query for all returned
cards (CARD-1255; `CardRelationshipApiTests.Board_loads_all_blocker_summaries_in_one_query`).
Cards with no `BlockedBy` rows, including cards that only block other cards, omit both members,
preserving their existing wire shape
(`CardRelationshipApiTests.Board_without_relationships_omits_blocker_fields` and
`CardRelationshipApiTests.Board_loads_all_blocker_summaries_in_one_query`).

```
GET    /api/home/tasks                       read-only home-rail projection of cards and unbound delegations (CARD-0002). Fleet-global; the client filters by project directory. Bound tasks nest as a card's Worker, never as their own item. No question field — that text is GET /api/attention.
GET    /api/cards                            list (?boardId=|status=|updatedSince=, at least one; ?limit=&pageToken=&includeArchived=) → { cards, truncated, nextPageToken }. Bounded summary pages in updatedAt DESC, id ASC order; default excludes archived cards. A continuation binds the scope and limit. Unfiltered is 400.
GET    /api/cards/search                     literal case-insensitive substring search (?q= required; ?boardId=&status=&limit=&pageToken=&includeArchived=) → { cards, total, truncated, nextPageToken }. Searches identifier, alias, title, full description, decoded labels and full terminal reason across all boards/statuses unless scoped. `total` counts all matches before cursor and limit. Private notes, comments, revisions and archive reason are excluded.
GET    /api/cards/limits                     title/description/reason/actor length ceilings plus importanceValues / urgencyValues (enum names)
GET    /api/cards/{id}                       one card  (?boardId=&cwd=)
GET    /api/cards/{id}/thread                card + its plans, tasks and commits (read-only projection)  (?boardId=&cwd=)
PATCH  /api/cards/{id}                       move (column + concurrencyToken + reason, optional spawn)  (?boardId=&cwd=)
PATCH  /api/cards/{id}/content               title/description edit — revision-logged  (?boardId=&cwd=)
PATCH  /api/cards/{id}/position              relative placement (before/after/top/bottom); 409 card_order_stale, 422 card_position_unreachable  (?boardId=&cwd=)
GET    /api/cards/{id}/revisions             every content write, with actor and reason  (?boardId=&cwd=)
POST   /api/cards/{id}/spawn                 start an agent on this card (409 `remote_control_refused` when `remoteControlName` is set on a kind whose catalog row is not Supported)  (?boardId=&cwd=)
POST   /api/cards/{id}/archive | /unarchive | /reopen  (?boardId=&cwd=)
GET    /api/cards/{id}/relationships         CARD-0100 → { blockedBy, blocks }; `related*` names the other card, `resolved` is computed on the blocker (Done, Canceled or archived)  (?boardId=&cwd=)
POST   /api/cards/{id}/relationships         { concurrencyToken, relatedCard, kind: "BlockedBy", relatedBoardId?, reason?, createdBy? } → 201 row + cardConcurrencyToken; 409 conflict (token, archived either side), card_relationship_exists, card_relationship_cycle; 422 validation_failed (self, kind, lengths); 404 unknown card  (?boardId=&cwd=)
POST   /api/cards/{id}/relationships/{relationshipId}/remove  { concurrencyToken, reason?, removedBy? } → 200 card; 404 when the row is not this card's  (?boardId=&cwd=)
GET    /api/cards/{id}/diff                  the card's branch diff  (?boardId=&cwd=)
POST   /api/cards/{id}/comments  (?boardId=&cwd=)
GET    /api/cards/{id}/discussion  POST /api/cards/{id}/discussion  (?boardId=&cwd=)
POST   /api/cards/{id}/pr                    open a pull request for the card  (?boardId=&cwd=)
POST   /api/cards/{id}/diagnose              queue a forced card-label diagnosis (202 `{ queued: true }`; 409 `diagnose_disabled` when `Delegation:DiagnoseEnabled` is false)  (?boardId=&cwd=)

GET    /api/diagnoses                        Diagnoses ledger, newest first (`?cardId=&taskId=&since=&outcome=&kind=&limit=`)
GET    /api/diagnoses/stats                  kind × outcome counts, p50/p90 wait, total cost, Applied label distribution (`?since=`)

GET    /api/boards  |  /api/boards/{id}  |  /api/boards/{id}/columns
POST   /api/boards            DELETE /api/boards/{id}
POST   /api/boards/{id}/cards                create a card on this board (importance/urgency names, optional dueAt; a `priority` field is 400)
POST   /api/boards/{id}/card-order           bulk relative order (listed cards first per rank cell; required reason; skippedHumanRated when Human-rated axes are left alone)
GET    /api/boards/{id}/workflow   PUT /api/boards/{id}/workflow    the board's workflow YAML
PUT    /api/boards/{id}/pipeline   select a shared pipeline definition; `{ "pipelineDefinitionId": null }` inherits the project default
POST   /api/boards/{id}/archive | /unarchive  hide/restore a board (reason body; not a delete)
GET    /api/boards/{id}/card-files/status    read-only policy and cleanup state
PUT    /api/boards/{id}/card-files/settings  explicit opt-in CAS; requires syncCardFiles + expectedSyncCardFiles
POST   /api/boards/{id}/card-files/sync      reconcile permitted public fields and revoked exports (?dryRun=; 200 may refuse writes; see card-file-privacy.md)

GET    /api/projects  |  /api/projects/{id}
POST   /api/projects   PUT /api/projects/{id}   DELETE /api/projects/{id}
PUT    /api/projects/{id}/pipeline   set or clear the project's default pipeline definition
POST   /api/projects/{id}/archive | /unarchive  hide/restore a project (reason body; not a delete)
GET    /api/projects/{id}/deletion-impact
POST   /api/projects/test-connectivity
GET    /api/projects/{id}/readiness                 ProjectReadinessDto
GET    /api/projects/setup-catalog                  ProjectSetupCatalogDto
POST   /api/projects/setup                          ProjectSetupResultDto
```

Board deletion and project deletion with `force=true` return 409
`project_delete_blocked_by_expectation_hold` when an audit nudge selected for deletion
is Attempting, Uncertain or Unconfirmed for an existing destination session's current
generation. The detail identifies those sessions and their
`POST /api/sessions/{id}/expectation-hold/release` routes. An initial check precedes
cascade writes; the nudge DELETE also checks its returned rows and rolls back with 409
if an inserted-and-claimed nudge raced that check. Operator release requires
`X-Antiphon-Operator-Token` and a reason; inspect and clear the composer before releasing.
The guard shares the composer gate's state-and-generation filter; it conservatively
blocks its candidates even if transcript or transient screen evidence would let the queue
proceed. Older-generation attempts and attempts whose destination session has been deleted
do not block audit deletion, as exercised by the generation cases below.

| Behaviour | Named test (ProjectCascadeForeignKeyTests) | What it asserts |
|---|---|---|
| Holding audit refuses board/project deletion | Holding_nudge_refuses_delete_preserves_composer_and_allows_audited_release | Three holding states return 409 with the release route before SQL writes; rows and queue hold survive; audited release permits deletion |
| Non-holding audit permits deletion | Nonholding_nudge_allows_delete_while_another_board_hold_survives | None, Confirmed, Refused, Submitted and Released delete successfully; an unrelated board's nudge and composer hold survive |
| Insert-and-claim race refuses deletion | Nudge_inserted_and_claimed_after_hold_check_refuses_delete_and_preserves_hold | A separately committed nudge claimed after the initial check returns 409; rollback retains its audit, card and composer hold for both delete routes |
| Stale or missing destination permits deletion | Holding_state_without_a_current_destination_generation_allows_delete | Three holding states x stale generation/missing session x board/project delete succeed; the release route has empty IDs or 404 before deletion |

Card list and search use `limit` as a positive page size (default and ceiling
`max(1, Cards:MaxListResults)`, normally 500); zero/negative is 422 on `limit`.
Search trims `q`, requires 1–500 characters, and treats `%`, `_` and backslash
literally. Invalid `q` is 422 on `q`. Invalid GUID, enum, timestamp or integer
binding remains framework 400. A page token is opaque continuation data bound to
the endpoint, filters and effective limit; changed or malformed tokens are 422 on
`pageToken`. Each continuation rechecks matching membership and order in a short
Repeatable Read snapshot. If either changed, it returns 409 `card_page_changed`
with a restart instruction. Final pages have `truncated: false` and
`nextPageToken: null`; search repeats its full 64-bit `total` on every page.
`includeArchived=true` includes archived cards regardless of parent board/project
archival state. Reads scan matching metadata per page, and leading-wildcard text
search may scan the scoped cards; no text index is claimed. Use
`scripts/card.ps1 list|search` for complete buffered enumeration.

> A move into an active column **does not start an agent** unless `spawn: true` (CARD-0051), and
> the orchestrator tick will not pick that card up either (CARD-0087).
>
> `GET /api/boards` and `GET /api/projects` hide archived rows unless `?includeArchived=true`
> (the same query name the board-detail endpoint already uses for archived cards). Archive is
> reversible hide, not delete; `scripts/prune-test-data.ps1` is the bulk front door.

Shared pipeline definitions are named, versioned orders of stage roles. The board pointer
overrides the project default, which falls back to the code-owned Standard pipeline. The
definition's active revision changes for future card runs; existing runs stay pinned.

```text
GET    /api/pipeline-definitions[?includeArchived=true]  |  /api/pipeline-definitions/{id}
POST   /api/pipeline-definitions                       create a Custom definition and revision 1
POST   /api/pipeline-definitions/{id}/clone            copy the active revision into a Custom definition
POST   /api/pipeline-definitions/{id}/revisions        append an immutable revision and activate it
POST   /api/pipeline-definitions/{id}/archive | /unarchive
```

Revision stages are ordered `{role, bundleKey, allowedNext}` rows. Roles are the six
`AgentTaskRole` stages; bundle keys name shipping `stage-*` bundles; allowed next tokens
must name a stage in the same revision or `land`, `decide`, or `none`. Built-in definitions
may be cloned but not edited or archived. Archived definitions remain resolvable by existing
pointers; new pointer writes to them return 409. `scripts/pipeline-definition.ps1` exposes
list, get, clone, revise, set-board, and set-project.

### Agents and their sessions

`AgentEndpoints.cs`, `SessionEndpoints.cs`

```
GET    /api/agents  |  /api/agents/{id}
GET    /api/agents/{id}/specialist-routing   declared Check routing, exact primary alias, retained candidate reasons; null revision means no explicit chain
PUT    /api/agents/{id}/specialist-routing   { concurrencyToken: guid|null, enabled: bool, candidates: [{ agentKind, modelLevel }] }; 1-3 unique complete ClaudeCode/Codex pairs, primary-matching head; stale revision 409 specialist_routing_stale, invalid declaration 422
POST   /api/agents/{id}/specialist-routing/revalidate  { concurrencyToken: guid }; rotates qualification authorization only, never stamps Qualified or starts a process; disabled feature/configuration 409 specialist_routing_disabled
GET    /api/agents/definitions               the configured Agents:Definitions catalogue
GET    /api/agents/preamble-preset?provider=  telegram | slack (404 for anything else)
GET    /api/agents/bundles                   attachable instruction bundles (read-only; the catalog is code)
POST   /api/agents        POST /api/agents/draft        PATCH /api/agents/{id}    DELETE /api/agents/{id}
GET    /api/agents/{id}/incidents            AgentIncidentDto (Kind, Severity, Message, FailureReason, CreatedAt). `HumanNotifiedAt` is the digest-pager stamp on the `AgentIncidents` row, not an acknowledgement and not on this DTO — a second Critical `ChannelReplyLost` an hour later is a second page.
POST   /api/agents/{id}/start  |  /stop      start accepts optional `resetHerdrFailureHold: bool` (default false). A held dead agent returns 409 `herdr_supervision_held` before other guards; explicit true acknowledges durably before normal validation (even if validation then refuses), preserving the consumed-attempt pair. Already-live Start is idempotent and does not acknowledge. Stop/attach do not clear the hold. Start also refuses 409 `remote_control_refused` when `remoteControl: true` on a kind whose catalog row is not Supported. Named herdr placement (`herdrTabLabel`) preflights `POST :17204/herdr/placement/check` and 409s with the runner code (`pane_occupied` / `herdr_tab_ambiguous` / `herdr_tab_invalid`) **before** enqueue; missing `herdr-named-tab-placement` is 409 `herdr_refused`. A post-preflight runner 409 fails the session row asynchronously.
POST   /api/agents/{id}/attach-herdr         bind a standing Herdr agent to an existing operator pane `{ "paneId": "w2:p3" }`. 409 `herdr_refused` / `session_active` / `herdr_kind_mismatch` / `herdr_pane_bound` / `herdr_native_id_unknown` / `herdr_transcript_not_found` / `herdr_pane_changed` / `session_id_taken`; 404 `herdr_pane_not_found`; 503 `herdr_unreachable`. Stop on an attached session detaches.
POST   /api/herdr/pane-disposals/preview    independent read-only inspection; requires exact paneId and at least one full expectedSessionId/expectedNativeSessionId UUID. Two-minute preview; guardMode=antiphon-best-effort, atomicClose=false. Ownership refusal has an empty previewId and requires fresh inspection after Stop.
POST   /api/herdr/pane-disposals            { operationId, previewId, reason }; explicit best-effort check-then-close. 200 Closed/AlreadyAbsent, 409 refusal, 503 unavailable/Unknown with operationId/receipt. External changes after the final check can race unconditional close.
GET    /api/herdr/pane-disposals/{operationId} durable receipt/reconciliation, or 404; Unknown is never destructively replayed. Runner equivalents are /herdr/pane-disposals (no /api), plus GET /herdr/pane-disposals/previews/{previewId} for immutable ownership lookup. Script: scripts/herdr-pane.ps1 inspect|dispose|status; dispose requires -Execute. Ordinary Stop still detaches.
POST   /api/agents/{id}/ensure-directory     create the agent's configured working directory (CARD-0214 readiness `create-directory` fix). Idempotent. 404 if the agent is missing; 422 if mkdir fails. Never takes a path from the caller.
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
POST   /api/sessions/{id}/expectation-hold/release  { "reason": "..." } (required); audited release of an expectation-watchdog composer hold (CARD-0650). Needs X-Antiphon-Operator-Token (403 operator_token_required otherwise); the audit names the authenticated operator. Types nothing; 422 without a reason, 404 unknown session, 200 with the released nudge ids (empty when nothing held).
GET    /api/expectation-watchdog?boardId=<guid>&skip=0&take=20  read-only CARD-0650 status: directive/config faults, scan freshness, lane counts, paged episodes and nudge receipt/answer/operator evidence. boardId is required; task and capability tokens are constrained to their board/project. No channel address, transcript body or send/ACK route is returned.
POST   /api/sessions/{id}/resize  |  /resume  |  /kill
```

`PATCH /api/agents/{id}` is where `alwaysOn`, `kind`, `tuiProfileId`, `modelId`, `launchEnv`,
`bundleKeys`, `replyStyle`, `systemPromptAppend`, `sessionBackend`, `herdrWorkspaceLabel` and
`herdrTabLabel` are set (null leaves a label unchanged; empty string clears it) — style keys on
`bundleKeys` are 422 — and where the herdr pairing gate fires
(`409 herdr_refused`) and where the remote-control capability gate fires
(`409 remote_control_refused` on a kind whose catalog row is not Supported — ClaudeCode only
today). See [herdr-sessions.md](herdr-sessions.md). `POST /api/agents/{id}/start` and
`POST /api/cards/{id}/spawn` refuse with the same `remote_control_refused` code when the caller
explicitly asks for remote control on a non-capable kind.

Attach an existing herdr pane (CARD-0213) — standing agents only, no rename, no launch note:

```
curl -s -X POST http://localhost:17202/api/agents/{id}/attach-herdr \
  -H "Content-Type: application/json" \
  -d "{\"paneId\":\"w2:p3\"}"
```

### Delegation

`AgentTaskEndpoints.cs` — the API behind `scripts/delegate.ps1`.

```
POST   /api/agent-tasks                      create (CreateAgentTaskRequest)
GET    /api/agent-tasks                      list envelope `{ scope, items, excluded }`. Query:
                                             rootId, status (comma list of AgentTaskStatus),
                                             includeChecks (bool, default false; hides Check,
                                             Distill, and Diagnose specialist rows), since (ISO
                                             instant), landPending (bool, default false; true
                                             requires LandRequestedAt or LandStartedAt non-null,
                                             composed with status/since/includeChecks and scope;
                                             exclusions count only filtered rows), projectId, boardId, unscoped
                                             (`exclude` default when a scope id is present,
                                             `include`, `only`). `since` keeps every non-settled
                                             row regardless of age and trims only settled rows by
                                             CompletedAt. Omitting projectId/boardId returns every
                                             board (`scope` is null, `excluded` is zeros). In-repo
                                             production list callers are the web client
                                             (`client/src/api/agentTasks.ts`),
                                             `scripts/checkpoint-task.ps1` (Working/Dispatched
                                             occupancy) and `scripts/prune-test-data.ps1`
                                             (open-task safety); `delegate.ps1` does not list.
                                             Status names are case-insensitive
                                             (`working` == `Working`); an unrecognised value,
                                             including an undefined number, is `422
                                             validation_failed` with `errors.status` naming it,
                                             and a comma list with one bad entry is refused whole
                                             (CARD-0546). An unrecognised query key is `400
                                             unknown_query_parameter` naming the key and listing
                                             the supported set. `unscoped` without a scope id is
                                             `422 validation_failed`. A known board whose project
                                             is not `projectId` is `422 scope_conflict`. Each row
                                             carries projectId/projectName/boardId/boardName/
                                             scopeSource (`Task`/`Card`/`None`). A PowerShell
                                             caller reads `.items` off the envelope.
GET    /api/agent-tasks/{id}                 {id} accepts the 8-char short id.
                                             `session` is read-time liveness: sessionId,
                                             status, working, lastSeenAt, endedAt,
                                             lastTranscriptAt. Null without a session row.
                                             Working is transcript-derived for Running sessions.
                                             Blocked tasks include `blocked`
                                             (`BlockedContextDto`): kind, round,
                                             isolated question, prior rounds,
                                             canAnswer, plus CARD-0294 `reason`,
                                             `authority`, `canContinue`.
                                             Detail also exposes `standingAuthority`
                                             and `autoContinueOnWait`, and CARD-0331
                                             `landRequestedAt`, `landStartedAt`,
                                             `landAttempt` (a pending land without
                                             reading events).
GET    /api/agent-tasks/summary              counters (active, blocked, runs, totalCostUsd,
                                             byStatus) with the same projectId/boardId/unscoped
                                             predicate as the list. Omitted scope is fleet-wide
                                             and independent of the list window. Specialists stay
                                             hidden.
POST   /api/agent-tasks/{id}/commit          gated commit of named paths (task-token caller).
                                             Body `{ paths, message }`. 200 `{ sha, files }`.
                                             Paths is a required nonempty array of explicit repository-relative
                                             file paths. Missing/null/empty or malformed selections are 422
                                             before lease admission or Git mutation; no whole-tree default.
                                             409 codes: ignored_path_staged, ignore_rules_changed,
                                             nothing_to_commit, repository_busy, commit_failed.
                                             403 `delegation_token_required` / `commit_on_settle_never`.
                                             503 `commit_inspection_pending` means the commit succeeded:
                                             `{ committed: true, operationId, sha? }`; files are unknown.
                                             Recover that operation before another POST.
GET    /api/agent-tasks/{id}/commit/{operationId} read-only recovery, same task token required.
                                             200 `{ sha, files }` for the exact task/operation trailers,
                                             even after HEAD advances or policy is disabled. 404 no match;
                                             503 inspection unavailable/ambiguous. Never commits or pushes.
POST   /api/agent-tasks/{id}/cancel  |  /retry  |  /escalate
                                             /retry takes an optional body `RetryAgentTaskRequest`
                                             `{ abandonCommitRecovery: false }` (absent body = false).
                                             Every requeue (retry, reroute, escalate) of a task holding an
                                             unresolved commit-recovery obligation is 409
                                             `commit_recovery_pending` with `obligationEventId`,
                                             `settlement`, `startedAt`, `holdExpiresAt` (CARD-0547);
                                             `abandonCommitRecovery: true` on /retry records a
                                             `CommitRecoveryAbandoned` event and requeues.
                                             /escalate, and /retry of a task not governed by a
                                             chain or pin, are 409 `model_disabled` (the create
                                             body) when the alias the dispatcher would launch is
                                             held (a pinned agent's ModelId or a specialist's
                                             alias wins the tier). Nothing is killed or changed
                                             (CARD-1490).
POST   /api/agent-tasks/{id}/reply           answer a Blocked delegate's question.
                                             Body `ReplyToAgentTaskRequest`:
                                             `message`, optional `round` (stale
                                             guard, 409 if the question moved on),
                                             optional `origin` (`Web` default;
                                             `delegate.ps1` sends `Cli`).
POST   /api/agent-tasks/{id}/continue        replay the standing authority given at
                                             dispatch as the answer (CARD-0294 S1).
                                             Body `{ origin?: "Web"|"Cli" }`
                                             (default Web). 409 `not_blocked` /
                                             `not_a_question` / `no_authority`.
                                             Returns `AgentTaskSummaryDto` like
                                             `/reply`.
POST   /api/agent-tasks/{id}/refine          steer a running delegate without cancelling it
POST   /api/agent-tasks/{id}/distillation/feedback
                                             CARD-0330: flag a distillation.
                                             Body `{ verdict: "Good"|"Lost"|"Noisy", note? }`.
                                             409 if the task has no distillation row.
                                             `delegate.ps1 -Flag <id> -Verdict Lost|Noisy|Good [-Note]`.
GET    /api/distillations                    CARD-0330 ledger. Query: since, outcome,
                                             feedback, limit (default 50, max 200).
                                             Joins raw and distilled texts from the task.
GET    /api/distillations/stats              CARD-0330 counts by outcome/feedback/stamp,
                                             ratio quantiles, top missing-anchor classes,
                                             FullReadAt rate. `scripts/distiller.ps1 -Stats`.
POST   /api/agent-tasks/{id}/handoff-actions  record an explicit caller observation (200).
GET    /api/agent-tasks/{id}/handoff-actions  read action snapshots and observation history (200).
POST   /api/agent-tasks/{id}/handoff-actions/{actionId}/child  keyed child request/recovery (200).
POST   /api/agent-tasks/{id}/review-evidence/corrections  attach validated execution to a Review (200).
POST   /api/agent-tasks/{id}/finding         CARD-0272: orchestrator override of a stage
                                             finding. Body `RecordStageFindingRequest`:
                                             `stage` (name, not ordinal — unknown is 422),
                                             `found` bool, optional `detail`, optional
                                             `reviewedSourceSha` (Review Clean only; 422
                                             otherwise). Writes a `Source=Orchestrator`
                                             `StageOutcome` row that supersedes the latest
                                             for that (task, stage). Optional nullable
                                             `reviewedSourceClean` requires an explicit
                                             `reviewedSourceSha` and never copies a prior
                                             assertion. `delegate.ps1 -Finding <id>
                                             -Stage … -Found "…"` / `-Clean`
                                             [`-ReviewedSourceSha`]
                                             [`-ReviewedSourceClean <bool>`]. Status and
                                             stage-outcome DTOs expose the nullable field.
POST   /api/agent-tasks/{id}/land            queue an explicit land of a Succeeded Worktree task
POST   /api/agent-tasks/{id}/land/v2         same handler as `/land` (CARD-0495). New CLI
                                             callers POST only `/land/v2` after `GET /api/version`
                                             advertises exact `land-v2`. An old process has
                                             no v2 route (404/405, never redirected to `/land`).
                                             Body `{ expectedSourceSha, reviewEvidenceId?,
                                             adoptFromTaskId?, recoverReviewedSource?, verify? }`.
                                             `adoptFromTaskId` lands a reviewed source through
                                             the original owner; `recoverReviewedSource` uses
                                             the owner's own reviewed branch. Fresh work requires a full
                                             40/64-hex SHA (422 without it). Optional
                                             review evidence must match subject/SHA/ref/repo
                                             and assert `reviewedSourceClean=true` (409
                                             `review_evidence_source_not_clean` otherwise).
                                             Pending identity is immutable. 202
                                             `{ status: "queued" | "requeued" }`; git
                                             runs in the background after remote-source
                                             freshness. Outcomes: `Landed`
                                             (publication confirmed, cleaned), `AlreadyPresent`
                                             (exact remote containment; cleanup separately
                                             reported), `LandedWithResidue`
                                             (publication confirmed, cleanup left residue;
                                             re-POST to retry cleanup), `LandingCleanup`
                                             (cleanup result for an existing publication;
                                             never a second publication), `LandRefused`
                                             (publication refused/unconfirmed; local target
                                             may have advanced). GET task detail includes
                                             structured `landing`/`landRequest`/`reviewEvidence`:
                                             approved original vs verified SHAs, remote source,
                                             publication, cleanup
                                             and reason. New landing events also carry
                                             nullable landingOperationId, landingPublication,
                                             landingCleanup and landingMode snapshots;
                                             legacy events have no inferred receipt.
                                             409 if a land is
                                             running in this server now (never for a
                                             request no process holds).
GET    /api/stage-outcomes                   CARD-0272 per-stage hit rate vs. cost. Query
                                             `since`, `until`, `stage` (name), `cardId`
                                             (guid), `latestOnly` (default true — one row
                                             per (task, stage), the latest superseding any
                                             orchestrator override). Returns
                                             `StageOutcomeListDto`: `rows` plus `summary`
                                             (one `StageOutcomeSummaryRowDto` per stage —
                                             runs, found, clean, skipped, failed,
                                             unreported, hitPercent = found/(found+clean),
                                             usdSpent, usdPerFinding, serverSecs). Stages
                                             are `Rebase`/`Verify`/`Cleanup`/`Review`/
                                             `FollowUp`/`Deploy`. `scripts/stage-value-report.ps1`
                                             wraps this as a table.
GET    /api/agent-tasks/areas?directory=     the repo's named areas (antiphon.areas.json)
GET    /api/agent-tasks/pipeline             fleet-wide across every board when no query is set. `?projectId=<guid>` and `?unscoped=true` are mutually exclusive and filter tasks, ready rows and the investigate backlog the same way; a scoped read still returns that scope's policy when it has no tasks. Unknown project is 404. A malformed GUID, `unscoped` other than `true`, or both parameters is 422. Host summaries stay fleet totals (`scope: fleet`, `hostSummaryScope: fleet`) and are not project admission. `concurrencyScopes` carries each represented project's effective policy, sources, revisions, and open/parallel/queued counts with nonnegative remaining and explicit overage; the null bucket is included on the fleet read. `recommendedInFlight` is the effective global role value on a fleet or null-bucket read, and that project's role value on a project read. `atOrAboveRecommendation` uses that same global role value on the fleet, and the project's combined parallel bound when the read is scoped. The GET does not seed settings, clear pins, or change tasks. Queued queueReason is one of sharedCheckoutLease, siblingLandInFlight, routingPinNotBefore, repositoryLease, remotePrep, projectParallelLimit, roleParallelLimit, hostBudget, concurrencyCap, awaitingDispatch. `projectParallelLimit` and `roleParallelLimit` are SeparateQueues dispatch holds and do not replace a lease, sibling land, or dated pin. Advisory in-flight / queued / blocked / ready snapshot. repositoryLease names heldBy from the current-stint lease sentence (the owner task, else the holder of a running land on the same repository). remotePrep names heldBy from the preparer's BehindTaskId, the current push-gate holder while the task waits for its push turn, read under the gate lock and copied by the pipeline status read at read time (CARD-1093, CARD-1100). It is null when that task is pushing or mirroring, and null while the holder is empty. The preparer push is `git push origin <branch>` with no `-u`, so the desktop task branch does not track origin. `maxConcurrentTasks` and `inFlightAgainstCap` describe the local host only; `hosts` gives each host's in-flight count and effective/configured/declared limit. In-flight, queued and blocked rows carry agentKind / modelLevel; in-flight and queued also carry workspace. Ready rows sit on the stage named by a settled stage-role task's `next:` handoff (`Investigate`/`Plan`/`TestDesign`/`Code`/`Review`) and carry `sourcePlanTaskId`, `sourceRole`, `deliverablePath`, `handoff`. A Succeeded Plan with `NextStage` unset and a verified `docs/superpowers/plans/` deliverable still yields a Code ready row (CARD-0146 S4 legacy). `land`/`decide`/`none` produce no ready row. CARD-0881: the response also carries `runners` (the `GET /api/session-runners` rows, fleet-wide on every read) and `runnerDefaults` (the `GET /api/runner-defaults` body; null before the first import), and each `hosts[]` row adds `remaining` = max(0, effectiveLimit − inFlight), null when the effective limit is unknown. With `/api/agent-tasks/pipeline?projectId=<guid>` this is the one effective-settings read before dispatch; it is a snapshot, not a reservation.

The same response has `investigateBacklog: { total, items }`: up to five fresh Backlog candidates across live boards, ranked by the dispatcher's full card key `(rank, position null last, dueAt null last, createdAt)`, then board/card GUID for exact ties. `total` counts all eligible cards before the five-item limit. Each item carries `cardId`, `boardId`, board-scoped `identifier`, `title`, derived `rank`, and nullable `position`. Cards already owned by a session, archived or terminal, marked `post-land-verification`, or bound to an open or succeeded pipeline-stage task are omitted. Failed/canceled history alone does not omit one. Formal `ready` rows remain separate and are ordered by the same card key, then ready age and board/card GUID; each exposes `rank`. The ordering is advisory: WIP, scope and human decisions still govern dispatch. Semantic coverage by a different landed card is an operator check against the card thread and closure evidence, not inferred by this response.
POST   /api/agent-tasks/worktree-residue/preview   CARD-0459 inventory only (projectId/boardId). Never deletes.
GET    /api/agent-tasks/worktree-residue/runs/{runId}  durable run/candidate page (`page`).
POST   /api/agent-tasks/{id}/worktree-retirement  record NoFurtherWorkspaceUse release. Does not delete.
DELETE /api/agent-tasks/{id}/worktree-retirement/{retirementId}  revoke only before claim/intent.
```

`CreateAgentTaskRequest` (`server/Application/Dtos/AgentTaskDtos.cs`) is the biggest body in the
API and is fully commented in place. The fields that change behaviour most: `role`, `kind`
(`Worker` / `Orchestrator`), `modelLevel`, `agentKind` (ClaudeCode / Grok / Codex — see
[agent-kinds.md](agent-kinds.md)), `workspace`, `workingDirectory`, `scope`, `followUpOnTask`,
`expectedMinutes`, `envOverride`, `ignoreSubscriptionQuota`, `ignoreModelDisabled`,
`ignoreRoutingPin`, `ignoreConcurrencyLimit` (CARD-0147; omits the create-time project/role cap for this request only), `commitOnSettle` (`Never`/`Always`/`Agent`; omitted inherits project then `Delegation:CommitOnSettle`; `-NoCommit` sends Never), `authority` (CARD-0294 standing authority, ≤ 2000 chars; `autoContinue`
without it is 422 `auto_continue_needs_authority`). `mergeTargetRef` is the landing target, never a
worktree base. Fresh card-bound Worktrees default to `requestedWorktreeBaseMode: Auto`:
creation returns `worktreeBase` with the observed decision, source task/branch/full SHA,
warnings and inspection counts. `worktreeBaseTask` selects a same-card source task;
`freshWorktree: true` requests the target base. The two overrides are exclusive. A
divergent automatic choice is 409 `worktree_base_ambiguous`; an invalid requested source
is 422 `worktree_base_source_invalid`. Detail records `worktreeBaseRequestedRef`,
`requestedWorktreeBaseMode`, `requestedWorktreeBaseTaskId`, `worktreeBasePreviewJson`,
`worktreeBaseRef`, `worktreeBaseSource`, `worktreeBaseTaskId`, `worktreeBaseBranch`, and
`worktreeBaseSha`. Dispatch rechecks the selection before creating a distinct task branch.
`AgentTaskLandNotifications` also carries `DispatchBase` notes with a null `RequestId`.

`role` is `AgentTaskRole`. Dispatchable: `Investigate`, `Plan`, `TestDesign`, `Code`, `Review`,
`Debug`, `Coverage`, `Docs`, `Commit`, `Test`, `Deploy`, `Merge`, `Custom`. Specialists `Check`,
`Distill`, `Diagnose` stay hidden unless `includeChecks`. Pipeline stages (CARD-0146) are
`Investigate`, `Plan`, `TestDesign`, `Code`, `Review`.

> `POST /api/agent-tasks` can refuse with **409 `subscription_quota_low`** (CARD-0136). That is a
> launch refusal, not a warning attached to a launch that already happened. Retry with
> `ignoreSubscriptionQuota: true`, or pick another `agentKind`/agent. The dispatcher never refuses;
> it only records an informational warning.
>
> It can also refuse with **409 `concurrency_limit`** (CARD-0147 / CARD-0366) when a new non-specialist task
> would push its project scope past `Delegation:MaxOpenTasks` (default 6) or that role within the project past
> `RolePolicy[role].RecommendedInFlight` (default 1; Code and Review default 2). Queued, Dispatched and Working count; Blocked
> does not; specialists and live follow-ups are exempt; tasks with no project scope form their own bucket.
> The `concurrency` extension names the axis, `projectId`, the occupants (same project only), and
> `override: "ignoreConcurrencyLimit"`. Retry with `ignoreConcurrencyLimit: true`
> if the user asked for parallel work this turn — that records a Warning and still does not raise
> `MaxConcurrentTasks`.
>
> It can also refuse with **409 `provider_sign_in_required`** (CARD-0324) for a registry-Grok
> create whose `GROK_HOME` has no usable `auth.json`. The problem-details extension carries
> `agentKind`, `grokHome`, and `remedy: "grok login"`. Retry with `allowUnauthenticatedProvider:
> true` (`delegate.ps1 -AllowUnauthenticatedProvider`) to queue anyway; the dispatcher still
> fails the task if the store is empty at launch. Never silently reroute. Profiles that
> authenticate with an API key (the standing `gkp` path) are not probed.
>
> It can also refuse with **409 `model_disabled`** (CARD-0022 / CARD-0309) when that kind/alias is
> on an active `ModelAvailabilityHold`. The detail lists remaining aliases (`available: opus,
> sonnet, …`); the `modelAvailability` problem-details extension carries `kind`, `modelAlias`,
> `disabledUntil`, `source`, and `available`. `ignoreModelDisabled: true` on **create only** queues
> the task; the dispatcher still skips it until the hold clears. Start never honours the flag.
> When a Required routing pin ends in this 409, the detail adds a coda that the list does not
> satisfy the pin — do not silently pick from `available`. Routing: see **The matrix governs an ordinary Worker dispatch; a pin is the exception** in
> [orchestration-loop.md](orchestration-loop.md#2-tiers).
>
> Create may also send **`complexity`** (`Hard`/`Medium`/`Easy`) and **`refuseIfExhausted`**
> (CARD-0090 / CARD-0332). Combined with explicit `agentKind`/`modelLevel` or with `ignoreModelDisabled` is
> 422. The walk reads the `(role, complexity)` cell, then the any-role row, then the config
> default. A Required pin with one effective candidate applies single-target 409 checks on
> exhaustion, including a forbid-only pin over a default row (C1250 V17 D1). Other exhausted
> chains insert the task **Blocked** (201 Created; status code not covered by a test) unless `refuseIfExhausted` is true,
> which is **409 `routing_exhausted`** with a `complexityRouting` extension. Auto-over-Human
> chain writes are **409 `complexity_chain_human`**. An Auto PUT to a role cell is the same 409
> when the any-role row for that complexity is Human (write it as Human, or clear the any-role
> row).
>
> With an explicit `complexity`, a forbid-only Required pin and one Codex row, an
> Orchestrator create is refused **409 `routing_exhausted`** and no task row is written
> (CARD-1271; `RequiredSingleCandidateRefusalTests` V-1).
>
> Routing pins (CARD-0305, CARD-0322) can also refuse create with **409 `routing_pin_conflict`**
> (explicit kind/level/agent disagrees with a Required pin; a kind/level conflict lists every
> candidate) or **409 `routing_pin_forbidden`** (resolved alias is on the stage pin's forbid list).
> **409 `routing_pin_human`** is a PUT refusal (Auto PUT onto an active Human row), not a create
> refusal. `ignoreRoutingPin: true` is one-shot
> and does not clear the pin; it is not `ignoreModelDisabled`. A walked multi-candidate pin that
> exhausts is **Blocked** (or 409 `routing_exhausted` with `refuseIfExhausted`).

```
GET    /api/runner-defaults
PUT    /api/runner-defaults                           expectedRevision, globalRunnerId, kindDefaults, reason, provenance
GET    /api/runner-defaults/revisions?beforeRevision=&limit=
GET    /api/dispatch-concurrency                      seed, overrides, effective, nullProject, supportedRoles, ranges, revision, occupancy
PUT    /api/dispatch-concurrency                      expectedRevision, overrides, reason, provenance; expectedGlobalRevision optional
GET    /api/dispatch-concurrency/revisions?beforeRevision=&limit=
GET    /api/projects/{projectId}/dispatch-concurrency  overrides, inherited, effective, revision (0 without a row), globalRevision, occupancy
PUT    /api/projects/{projectId}/dispatch-concurrency  expectedRevision, expectedGlobalRevision, overrides, reason, provenance
GET    /api/projects/{projectId}/dispatch-concurrency/revisions?beforeRevision=&limit=
GET    /api/session-runners                           desktop plus every configured runner
GET    /api/hosts                                     local and configured runner budgets, occupancy, availability and revision
PUT    /api/hosts/{hostId}/budget                     maxInFlight (0–512 or null to clear), reason (required)
GET    /api/session-runners/{runnerId}/capacity       declared capacity, server bound, effective limit and occupancy
PUT    /api/session-runners/{runnerId}/capacity       capacity, reason (required); live runner push
GET    /api/hosts/stats                               latest per-host sample, rollups, state, Antiphon counts
GET    /api/hosts/{hostId}/stats/series?metric=cpu&window=30m
GET    /api/hosts/{hostId}/occupancy-samples?from=&to=&limit=   read-only occupancy audit, newest first (CARD-1079)
```

Dispatch concurrency routes (CARD-0505; Settings → Concurrency UI, CARD-0506, in
[orchestration-loop.md](orchestration-loop.md#concurrency-settings-tab-card-0506)). Each row states
what the named `DispatchConcurrencyWireTests` method asserts:

| Behaviour | Test |
|---|---|
| Global GET returns revision 1 with seed `origin` `startupConfiguration`; PUT with `expectedRevision` 1 returns 200 and revision 2; a later GET returns revision 2 | `Global_get_put_roundtrip` |
| Project GET without a row returns `revision` 0 and `globalRevision` 1; PUT returns 200 and leaves another project at revision 0; PUT `overrides: {}` returns revision 2; unknown project GET and PUT are 404 and create no row | `Project_get_put_clear_roundtrip` |
| On a store already initialized by a global read: 19 listed bodies (missing, duplicate, unknown or wrong-type field; blank or 401-character reason; unknown provenance; `maxParallel: 0`; `mode` null or numeric; non-object `overrides`; malformed JSON) are 422 `validation_failed` and leave the settings rows and revision count unchanged; unknown project PUT is 404 with the same fingerprint | `Missing_fields_invalid_body_and_unknown_project_write_nothing` |
| A stale `expectedRevision` is 409 `dispatch_concurrency_revision_conflict` with `globalRevision`; of two concurrent PUTs at the same revision one is 200 and one 409; an `Auto` PUT over a `Human` revision is 409 `dispatch_concurrency_human` | `Revision_conflict_returns_current_revisions` |
| After initialization, that test's three 409s and two committed PUTs raise the `MockEventBus` published-event count by exactly 2 (event names not checked; not a recipient-delivery witness) | `Revision_conflict_returns_current_revisions` |
| `/revisions`: default page 50, `limit` 1–100 (0 and 101 are 422), `beforeRevision` cursor (-1 is 422); a GET on a cold store creates no row; GETs leave the fingerprint unchanged | `Revision_routes_are_readonly_and_bounded` |

Not covered by a test: a PUT with a rejected policy value (such as `maxParallel: 0`) on a store
with no global row. `PutAsync` initializes the store before it checks policy values.

Host stats are sampled by each session runner every five seconds and held in a 30-minute memory
ring. `GET /api/hosts/stats` gives `HostStatsDto[]` for desktop and configured runners. `state`
is `live`, `stale`, `offline`, or `unsupported`; stale retains the last known values, while a
missing value is null and must never be interpreted as zero. `current` includes CPU, load where
available, memory, swap, disks and process count; `rollups` carry average and maximum over
1/5/15/30 minutes. `antiphon` carries current tasks by stage and agent kind, queued/held tasks,
lands, sessions, seats, and build slots. `metric=cpu|load|memory` and
`window=1m|5m|15m|30m` are the only series queries; there is no tasks series. Windows
`swapUsedBytes`/`swapTotalBytes` describe page-file **commit charge / commit limit**, not
physical swap use/capacity. `HostStatsUpdated` publishes the same list to SignalR group `hosts`.
The server settings are `HostStats:PollIntervalMs`, `StaleAfterMs`, `RequestTimeoutMs`, and
`SeriesTimeoutMs`; runner settings are under `SessionRunner:HostStats`.

`GET /api/hosts/{hostId}/occupancy-samples` is the durable occupancy audit (CARD-1079).
It is read-only: there is no write route. An unknown host is 404 through the same
host budget lookup as `PUT /api/hosts/{hostId}/budget`. The default window is the
last 24 hours (`from` defaults to `to` minus 24 hours, `to` defaults to now, both
UTC). `limit` defaults to 500 and is clamped to 2000; the applied value is the
response `limit`. `limit=1` and `limit=2000` are applied as given. A `limit`
below 1 is 400 `{ "error": "limit_invalid" }`. A non-numeric or overflowing
`limit` fails model binding and `ExceptionMiddleware` returns 400 problem
details, not a 500 and not `limit_invalid` (CARD-1111). The occupancy
endpoint's malformed `limit` body does not depend on the hosting
environment (CARD-1123). A `to`
within 24 hours of `DateTime.MinValue` with `from` omitted is 400
`{ "error": "window_invalid" }` (CARD-1101): the default window would otherwise
underflow `AddHours(-24)` into a sanitized 500. A reversed window (`from`
after `to`) is 200 with `samples: []` and the requested bounds.
Rows are newest first and carry the stored counters (`inFlight`,
`dispatchedWorking`, `sessions`, `pendingLaunch`, `inFlightMirrors`, `idleSeats`,
`pooledWarmSeats`, `orphanSlots`, `effectiveLimit`, `declaredCapacity`,
`oldestIdleSince`, `inventoryState`, `inventoryReason`). `orphanSlots` counts occupying slots with no live desktop session or no owner task (Queued, Dispatched, Working or Blocked); samples stored before CARD-1124 activated also counted live Blocked owners. `inventoryReason` on
the wire is null or one of four exact categories: `unavailable`, `timeout`,
`error`, or `unavailable: phone-home`. The sampler stores those categories
and never a raw exception message. A runner that is not connected stores
`unavailable: phone-home` (the directory's fixed `phone-home runner unavailable`
token, not the exception text). The route publishes a stored reason only when
it is exactly one of those four; any other stored text, including a legacy
exception message still inside the retention window, is returned as `error`
and is not echoed (CARD-1111). The exception text stays in the server log.
`Attention:SeatWatchEnabled` defaults true; setting it false is the rollback
(the sampler writes nothing and `SeatIdle` (57), `OccupancyDivergence` (58) and
`SlotOrphan` (59) are omitted). Seat rows are phone-home hosts only. Nothing on
this route stops, kills, releases or dispatches.

Task detail includes `parkSync` when the current attempt's sync state is not
`NotRequired`. `GET /api/attention` adds `SessionDisagreement` keyed
`runner-seat-release:{releaseId}` for an open runner-seat release or pending or held
sync. The evidence line carries the park id, sync state, source SHA and reason, and
omits report and answer text. `BlockedTaskParking:Enabled` defaults to false.
`ReclaimExisting` defaults to false. `ReclaimIntervalSeconds` defaults to 120; 0 sweeps on every scheduled call and a negative value is treated as 0. `ReclaimHeldBackoffSeconds` defaults to 600; 0 disables the Held backoff and a negative value is treated as 0. `TerminalRunnerSeatRelease:AutomaticEnabled`
defaults to false. New publication and a Blocked release require both Enabled and
automatic release. Legacy discovery also requires ReclaimExisting. Turning the
switches off does not delete a publication receipt, abandon an accepted answer, or
stop release reconciliation and sync-debt recovery. Parking never stops a Working
session.

CARD-1082. Task detail includes syncDebt (id, state, sourceSha, confirmedSha, reasonCode, attempts, nextAttemptAt, sourceReadyAt) when this attempt has a settlement sync debt row. Pending is never Confirmed. Delegation:RunnerSyncDebtOnSettlement defaults to true. The debt row is created only for runner_sync_lease_busy on a complete report that settles. The workspace note contains desktop-sync=pending and the caller warning says synced later. GET /api/attention adds SessionDisagreement keyed settlement-sync-debt: for Held debt and for Pending debt older than the threshold. RunnerSyncDebtAttentionMinutes defaults to 30.

Host budgets are separate from the sampled host stats. A local budget overrides
`Delegation:MaxConcurrentTasks`; a runner's effective limit is the minimum of its configured
budget and declared capacity. A null budget uses the setting or declaration. A zero budget
holds every new task for that host without stopping existing sessions. Runner capacity PUT
requires a live compatible phone-home peer and returns 409 on refusal or timeout. Both writes
record an incident with the operator's reason.

`PUT /api/runner-defaults` is one complete snapshot. `409 runner_defaults_revision_conflict` and
`409 runner_defaults_human` write nothing. `Delegation:DefaultRunnerId` is imported once into
revision 1 and is not a live placement control after that. A task's frozen `requiredPlatform` and
`runnerId` are on the task summary; a card's `requiredPlatform` is its default for new tasks.

```
GET    /api/routing-pins?card=CARD-0304&role=Plan     active pins (card query includes stage-wide)
PUT    /api/routing-pins                              upsert the grain (Human cannot be overwritten by Auto)
DELETE /api/routing-pins/{id}                         clear (204 if already clear)
```

A pin is the standing instruction the **next** create reads; it does not rewrite Queued work.
Stage-wide: omit `card`. A `PUT` for Check, Distill or Diagnose is 422
(`RoutingPinServiceTests.A_specialist_role_has_no_stage_to_pin`). Script: `scripts/routing-pin.ps1 get|set|clear`.
`delegate.ps1 -Pin` writes Human Required from the resolved kind/level; `-Pin` without a card is
refused (that would be a stage-wide pin).

CARD-0322: `PUT` accepts `candidates: [{agentKind, modelLevel}, …]` (1..8, mutually exclusive
with the `agentKind`/`modelLevel` shorthand). `GET` returns `candidates[]` with
`availableNow`/`unavailableReason` plus `candidateCount`; `agentKind`/`modelLevel`/`modelAlias`
remain the **head**. A kind/level 409 `routing_pin_conflict` lists every candidate; the `routingPin`
extension carries `candidateCount`. Routing: see **The matrix governs an ordinary Worker dispatch; a pin is the exception** in
[orchestration-loop.md](orchestration-loop.md#2-tiers).

```
GET    /api/role-policy                               RolePolicy per routable role beside its active
                                                      stage-wide pin (CARD-0097)
```

Body: `{ defaultLevel, defaultTimeoutMinutes, roles[] }`; each row is `{ role, configured, level,
kind, alias, escalateTo, escalateAfterMinutes, timeoutMinutes, stagePin, effective }` and
`effective` is `{ source, agentKind, modelLevel, alias, strength }` with `source` `pin` or
`rolePolicy`: a projection of the stage pin's first candidate filled from the policy, or of the
policy; it is not a create result. Nullable: `defaultLevel`, `level`, `kind`, `escalateTo`,
`escalateAfterMinutes`, `stagePin` and `effective.agentKind`/`modelLevel`/`alias`/`strength`. A
`kind` is null when no kind is configured or the configured kind is undefined; with no stage pin
the effective target is null for an undefined kind (the two `kind` null rows below). A row with no
configured kind and an undefined level has the same `kind` null, `alias` `""` and null target (case
`policy-level-999`), so `alias` and `effective` do not identify the cause. Pin routes: the `/api/routing-pins` block above.
Configuration validation and create refusals for such values (CARD-1234): the last rows of the table.

`effective` alone is not the next create. The UI composes stage pins before choosing a default-tier
walk or a single target; stage forbids, Orchestrator kind filtering and quota can change the result.
A single held pin is refused. A Required forbid-only pin with one default-row candidate is
also refused when held, quota-limited or forbidden (C1250 V17 D1; V21). Other exhausted
row/list paths can become Blocked (V3, V15, V17 exhaustion controls). Missing floor, alias or
availability facts are disclosed as server dependencies. See CARD-1250 and its cited parity tests
in [orchestration-loop.md](orchestration-loop.md); the tests below bind this DTO.

For card-origin Hand back or a possible CARD-<digits> binding in the derived title, the modal
displays “unknown — a card pin may apply; routing depends on the server” with no target badge.
Identifier boundaries are exact for ASCII neighbours. A non-ASCII neighbour counts as a
possible boundary because browser and server Unicode tables can differ, so this can show
unknown when the server would not bind (C1250 V22 Unicode drift and ASCII controls).
Title derivation and explicit-card context are also covered by C1250 V22;
V23 covers Hand back, goal edits and the card-free control. Bare #N in a goal does not bind;
an explicit API card field can use #N. The Settings column assumes a card-free create.
The modal admission note names card pins, standing-agent details, quota, holds, provider sign-in,
concurrency, workspace and runner conditions as server dependencies (V24).

| Behaviour | Test (runner, file) | What it asserts |
|---|---|---|
| 14 rows in `RoutableRoles` order with the policy fields; Custom is unconfigured and reports the defaults | TUnit `RolePolicyEndpointTests.Get_lists_every_routable_role_in_order_with_policy_fields`, `tests/Antiphon.Tests/Api/RolePolicyEndpointTests.cs` | role list Investigate … Custom; Mutation `Low`/`haiku`, `kind` null, `escalateTo` `High`, `effective.source` `rolePolicy`; Custom `configured` false, `High`/`opus`, timeout 240 |
| Active stage-wide pin is `stagePin` and drives `effective` | TUnit `RolePolicyEndpointTests.Get_overlays_the_active_stage_pin_as_effective`, same file | Plan pin Codex/Frontier → `stagePin.id`, head `availableNow`, `effective` `pin`/Codex/Frontier/`gpt-6-astra`/`Required`; Code stays `rolePolicy` |
| Card pins and cleared pins are excluded | TUnit `RolePolicyEndpointTests.Get_ignores_card_pins_and_cleared_pins`, same file | live Code card pin and cleared Debug stage pin → both rows `stagePin` null, `effective.source` `rolePolicy` |
| Unsupported or undefined kind/level still answers 200 | TUnit `RolePolicyEndpointTests.Get_serialises_every_row_when_one_role_has_unsupported_values`, same file | 14 rows; Docs level 999 → `level` null, `alias` `""`; Commit `kind` `Raw`, `alias` `""`; Deploy kind 999 → `kind` null; each of the three has `effective` `agentKind`/`modelLevel`/`alias`/`strength` null |
| Per-case withholding of unsupported or undefined values | TUnit `RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values` (`[MethodDataSource]`, 15 cases), `tests/Antiphon.Tests/Application/RolePolicyReadServiceTests.cs` | e.g. `policy-level-999` → `level` null, `alias` `""`; `default-level-999` → `defaultLevel` null; `pin-head-kind-Raw` → `effective` target null, `strength` `Required` |
| `kind` null: no kind configured | TUnit `RolePolicyReadServiceTests.Compose_resolves_alias_from_kind`, `tests/Antiphon.Tests/Application/RolePolicyReadServiceTests.cs` | Code `kind` null → `alias` `fable`, `effective.agentKind` ClaudeCode |
| `kind` null: undefined configured kind | TUnit `RolePolicyEndpointTests.Get_serialises_every_row_when_one_role_has_unsupported_values`; `RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values` case `policy-kind-999` | Deploy kind 999 → `kind` null, `alias` `""`, `effective` `rolePolicy` with `agentKind`/`modelLevel`/`alias`/`strength` null; Docs kind 999 → `kind` null, `alias` `""` |
| Alias follows the configured kind | TUnit `RolePolicyReadServiceTests.Compose_resolves_alias_from_kind`, same file | Docs Grok → `grok-4.7`; Commit Codex → `gpt-6.1-sol`; Code `kind` null → `fable`, `effective.agentKind` ClaudeCode |
| Unconfigured role uses `DefaultLevel`/`DefaultTimeoutMinutes` | TUnit `RolePolicyReadServiceTests.Compose_lists_RoutableRoles_in_order_and_marks_Custom_unconfigured`, same file | DefaultLevel Medium, timeout 90 → Custom `Medium`/`sonnet`/90 |
| First candidate of a stage pin is `effective`; a card pin is not a stage pin | TUnit `RolePolicyReadServiceTests.Compose_effective_prefers_the_first_stage_pin_candidate_and_excludes_card_pins`, same file | Preferred Plan pin head Codex/Frontier is `effective`; Code card pin → `stagePin` null |
| A partial pin head fills from the policy | TUnit `RolePolicyReadServiceTests.Compose_fills_a_missing_pin_level_from_the_policy`, `…Compose_fills_a_missing_pin_kind_from_the_policy`, same file | Grok head without level → Medium/`grok-4.7`; level-only head → policy kind (ClaudeCode `opus`, or Grok `grok-4.7`) |
| A candidate-free pin names no target | TUnit `RolePolicyReadServiceTests.Compose_withholds_the_target_for_a_candidate_free_pin`, same file | `effective` `{pin, null, null, null, Required}` |
| `DelegationSettingsValidator` (configuration validation, not create) rejects a policy `Kind` outside ClaudeCode, Grok and Codex, or an undefined policy `Level`, `EscalateTo` or `DefaultLevel`, naming key and value | TUnit `RolePolicyConfigurationValidationTests.An_unlaunchable_kind_or_undefined_level_fails_validation_naming_role_and_value` (8 cases), `…Binding_unlaunchable_or_undefined_configuration_fails_the_options_read` (Unit), `tests/Antiphon.Tests/Application/RolePolicyConfigurationValidationTests.cs` | one literal failure per case, e.g. `Delegation:RolePolicy:Code:Kind is Raw, which delegation cannot launch. Use ClaudeCode, Grok or Codex, or leave it unset.`; `Test-EscalateTo-999` → `Delegation:RolePolicy:Test:EscalateTo is 999, which is not a model level. Use Frontier, High, Medium or Low.`; binding reads `Value` and throws `OptionsValidationException` with four literals |
| The validator accepts the C# defaults (`shipped` case), ClaudeCode/Grok/Codex/null kinds and defined levels | TUnit `RolePolicyConfigurationValidationTests.A_launchable_kind_and_defined_levels_validate` (12 cases), `…Binding_named_valid_values_and_absent_fields_validates`, same file | `Succeeded`; bound Docs Grok/Low, Plan `EscalateTo` Frontier |
| On a host without the validator, a Docs create with no explicit level or kind that resolves policy `Level` 999, `DefaultLevel` 999 (no Docs entry) or policy `Kind` Raw/OpenCode/999 is refused | TUnit `RolePolicyInvalidValuesCreateTests.post_agent_tasks_refuses_an_unusable_policy_value_with_a_named_422_and_writes_nothing` (5 cases), `tests/Antiphon.Tests/Application/RolePolicyInvalidValuesCreateTests.cs` | 422 problem+json; level cases `role_policy_level_invalid` (`errors.RolePolicy`), kind cases `role_policy_kind_unsupported` (`errors.AgentKind`); zero tasks and sessions; a Commit create on the same host then answers 201 at Medium |
| An explicit valid level overrides policy `Level` 999 | TUnit `RolePolicyInvalidValuesCreateTests.an_explicit_level_does_not_consult_an_undefined_policy_level`, same file | Docs create with `modelLevel` Low → 201, created `ModelLevel` Low |
| An orchestrator's policy `Level` 999 is refused before the orchestrator floor | TUnit `RolePolicyInvalidValuesCreateTests.an_orchestrator_refuses_an_undefined_policy_level_instead_of_clamping_it_to_the_floor`, same file | `ResolveLevel(Orchestrator, Docs, null)` throws `ValidationException` `role_policy_level_invalid`; Test resolves High |
| `EscalateTo` 999 on create | not covered by a test (`EscalateTo` has validator cases only) | — |
| No write verb on `/api/role-policy` | not covered by a test | — |

```
GET    /api/complexity-chains                          any-role ×3 first (Hard/Medium/Easy), then every
                                                       active role cell; + roles[], complexities[], defaultComplexity. Each
                                                       entry carries role (null = any) and resolvedFrom
                                                       (role | any | config | none).
GET    /api/complexity-chains?role=Plan                the three EFFECTIVE Plan cells, each with
                                                       resolvedFrom and effective
PUT    /api/complexity-chains/{role}/{complexity}      upsert the cell; role = any|Plan|Code|…;
                                                       optional body concurrencyToken (CARD-1275)
DELETE /api/complexity-chains/{role}/{complexity}      clear the cell (204 if already clear);
                                                       optional ?concurrencyToken= (CARD-1275)
PUT    /api/complexity-chains/{complexity}             alias of /any/{complexity}
DELETE /api/complexity-chains/{complexity}             alias of /any/{complexity}
POST   /api/agent-tasks/{id}/reroute                   { agentKind, modelLevel } — Blocked-for-routing or Queued
```

Config defaults for chains ship **empty**. Script: `scripts/complexity-chain.ps1 get|set|clear`
(`-Role` omitted or `Any` writes the any-role row). `delegate.ps1 -Complexity Hard|Medium|Easy`,
`-RefuseIfExhausted`, `-Reroute <id> -Kind … -Level …`. 422 for Check/Distill/Diagnose as a cell
role ("seat-pinned roles are not routed by chains") and for an unknown role.

The role view adds `effective: { source, candidates, stagePin, governsDefault, withheldReason }`.
Sources are `pin`, `pin+chain`, `role`, `any`, `config`, `rolePolicy` or `agent`; candidates retain
`agentKind`, `modelLevel`, `alias`, `availableNow` and `unavailableReason`. `effective` is null on
the unfiltered list and any-role rows. `defaultComplexity` is the configured ordinary Worker tier,
shipped as `Medium`.

**Concurrency token (CARD-1275).** Each entry carries `concurrencyToken`: the version of the cell's
own active row, rotated by each write and clear, including the lazy `notAfter` expiry sweep
(`ComplexityChainService.StageUpsertAsync`, `ClearAsync`; `ComplexityRoutingService.SweepExpiredAsync`).
It is null when the cell has no row; on the `?role=` view that includes a role cell answered by the
any-role row or by config (`GetEffectiveAsync`; `Effective_view_carries_the_cells_own_token_not_the_any_role_rows`),
so a writer sends the token of the row it would replace. A PUT body `concurrencyToken` or a DELETE
`?concurrencyToken=` is the version the writer read; `ThrowIfStaleAsync` compares it with the row
the request reads:

| Sent | Active row | Result | Source |
|---|---|---|---|
| omitted | — | write without a version check (callers that predate CARD-1275; `migrate-stage-pins-to-cells.ps1`): last writer wins; see below for a lost race | `ThrowIfStaleAsync` returns at once; `Put_twice_is_last_writer_wins_without_a_409` |
| `00000000-0000-0000-0000-000000000000` | absent | PUT creates the row; DELETE is a 204 no-op | `Stale_delete_is_409_and_keeps_the_row` (PUT); `ClearAsync` returns before the check (DELETE) |
| `00000000-0000-0000-0000-000000000000` | present | 409 `complexity_chain_stale` | `Create_when_absent_needs_no_version_and_the_empty_token_refuses_an_existing_row` |
| the row's token | present | write: a PUT is 200 with the new token, a DELETE 204 | `Stale_delete_is_409_and_keeps_the_row` (second PUT, fresh DELETE) |
| another value | present, or absent on PUT | 409 `complexity_chain_stale` | `Stale_put_is_409_with_the_current_row_and_leaves_it_unchanged`; `Create_when_absent_…` (token of a cleared row) |
| a value | absent on DELETE | 204 no-op (already clear) | `Stale_delete_is_409_and_keeps_the_row` (repeat DELETE) |

The 409 problem body adds `current`: the cell's row as GET returns it now (camelCase, enum names,
nulls kept; `StaleAsync`). The server reads the row, then saves; a write committed in between makes
the save fail, through the UPDATE's EF concurrency token or the unique active-row index
(`TrySaveAsync`, `IsLostRace`). A write that sent a token is then 409 `complexity_chain_stale` with
no second attempt (`RetriesLostRace`; `A_write_with_a_token_that_loses_a_race_is_stale_at_once_with_the_current_row`).
A write without a token reads the row again and re-applies its values, up to 3 attempts
(`ComplexityChainService.UnconditionalAttempts`), and only a race lost on each attempt is 409
`complexity_chain_stale` (`A_race_lost_on_every_attempt_exhausts_the_bound_and_is_stale`); a DELETE
without a token whose row was cleared meanwhile is the usual 204
(`A_delete_without_a_token_that_loses_a_race_is_retried_and_clears`).

The expiry sweep also re-reads after a lost race, up to 3 saves (`ComplexityRoutingService.SweepAttempts`).
After the third lost race its `DbUpdateConcurrencyException` leaves `FindActiveAsync`, and the
request's database context no longer tracks the rows the sweep cleared (CARD-1278;
`A_sweep_that_loses_every_attempt_lets_the_concurrency_error_propagate`,
`A_sweep_of_several_rows_that_loses_every_attempt_leaves_none_of_them_modified`). No test drives
this through HTTP. What the request then answers depends on whether its caller catches it:

| Request | Answer | Source |
|---|---|---|
| `GET /api/complexity-chains` (unfiltered), chain PUT, chain DELETE | 500 | `GetAsync`, `StageUpsertAsync` and `ClearAsync` call `FindActiveAsync`, and the list calls `ListLiveCellsAsync` (same sweep), with no catch; `ExceptionMiddleware.Classify` maps it to 500 |
| `GET /api/complexity-chains?role=` | 200, not 500: the cell falls back as for any failed read, and `effective.withheldReason` carries `chain read failed (DbUpdateConcurrencyException)`, or `chain metadata read failed (DbUpdateConcurrencyException)` when the cell still resolves from its own row | `GetEffectiveAsync` runs each chain read through `ReadPartAsync`, which catches it |
| `POST /api/agent-tasks` with an explicit `complexity` | 500, no task created | `AgentTaskService.CreateAsync` calls `WalkAsync` with no catch; `MatrixDefaultComplexityCreateTests.An_explicit_complexity_create_whose_expiry_sweep_loses_every_race_still_fails` (the service throws `DbUpdateConcurrencyException`; `ExceptionMiddleware.Classify` maps it to 500) |
| `POST /api/agent-tasks` that reads the chain at the default tier | 201: the task is created on today's path (RolePolicy) with the warning `Routing read failed (DbUpdateConcurrencyException); using today's path.` and no complexity stamp | `AgentTaskService.CreateAsync` default-tier `catch (Exception)`; the create endpoint answers `Results.Created` (`AgentTaskEndpoints`); `MatrixDefaultComplexityCreateTests.A_default_tier_create_whose_expiry_sweep_loses_every_race_is_created_with_the_warning` (service level: Queued, that warning, no modified chain row left in the shared context) |

On the `?role=` view a role cell's token, reason and expiry are read before its candidates, so a
cell created or re-created between the two reads is returned with the newer candidates and the older
token (or none), and a save with it is 409; a cell cleared between them is returned without its row
or token (`GetEffectiveAsync`;
`The_role_view_reads_the_token_before_the_candidates_so_a_change_between_them_makes_a_save_stale`).
The unfiltered list takes each entry's candidates and token from one row read (`GetAsync` and
`ListLiveCellsAsync`, then `ToRowDtoAsync`). `complexity-chain.ps1 set|clear` reads
`GET /api/complexity-chains` once and sends each cell's token from that read
(`Set_and_clear_send_each_cells_token_from_one_read_and_stop_on_stale`). The settings editor sends
the version it opened; on a 409 it shows the stored row beside the draft as "Changed since you
opened this: reload", and adopts the new version when the operator presses Reload
(`ComplexityChainCellEditor.test.tsx` `C1275 a stale Save shows the stored row…`). An editor whose
stored row or metadata failed to read sends no token once the operator confirms the overwrite; its
offline-queued note then says the write replaces the row stored when it is sent (`C1275 an unset
cell sends the empty token; an unread cell confirmed for overwrite sends none`, `C1275 D2 an offline
Save from an unread row says the queued write overwrites and sends no token`).

| Behaviour | Named test | What it asserts or does not cover |
|---|---|---|
| Role view includes the effective overlay and default complexity | `ComplexityEffectiveHttpTests.Get_role_view_carries_effective_and_default_complexity_and_card_pins_are_excluded` | Default Medium, role-view effective candidates, excluded card pins and null effective on the list view |
| Candidate ordering reflects Required/Preferred stage pins | `ComplexityEffectiveComposeTests.A_required_pin_replaces_the_cell_and_sources_pin`, `A_preferred_pin_prepends_and_sources_pin_plus_chain` | Exact candidate arrays and `pin` / `pin+chain` sources |
| A target can be withheld | `ComplexityEffectiveComposeTests.An_unlaunchable_policy_is_withheld_with_its_reason`; `ComplexityEffectiveHttpTests.Get_role_view_reports_an_agent_only_stage_pin_as_agent_bound` | Unsupported policy has an empty target and reason; agent-bound pin has `source: agent`, empty candidates and `governsDefault: false` |
| A later PUT without a token replaces the cell | `ComplexityEffectiveHttpTests.Put_twice_is_last_writer_wins_without_a_409` | Second candidates and reason returned by GET, updated timestamp and no 409 |
| A stale PUT or DELETE is refused with the current row | `ComplexityChainConcurrencyHttpTests.Stale_put_is_409_with_the_current_row_and_leaves_it_unchanged`, `Stale_delete_is_409_and_keeps_the_row` | 409 `complexity_chain_stale`, `current.concurrencyToken` equals the stored token, stored row unchanged; a fresh DELETE is 204 and a repeat is 204 |
| Create when absent, expect-absent and a cleared row | `ComplexityChainConcurrencyHttpTests.Create_when_absent_needs_no_version_and_the_empty_token_refuses_an_existing_row` | No-token create 200; empty token over a row 409; no-token overwrite 200; a token for a cleared row 409 with `current.source` `config` |
| Concurrent writers | `ComplexityChainConcurrencyHttpTests.Concurrent_writers_with_one_token_leave_exactly_one_winner` | Six parallel creates and six parallel updates from one version: one 200 and five 409 each; GET equals the winner |
| A lost race without a token is re-run | `ComplexityChainRaceTests.A_write_without_a_token_that_loses_a_race_is_retried_and_the_callers_values_win`, `A_delete_without_a_token_that_loses_a_race_is_retried_and_clears` | A competing commit between the read and the save: the no-token PUT (update and create) saves on the second attempt with the caller's values and a new token; the no-token DELETE clears, and returns at once after losing to a clear |
| A lost race with a token is stale at once | `ComplexityChainRaceTests.A_write_with_a_token_that_loses_a_race_is_stale_at_once_with_the_current_row` | One save attempt; 409 with `current` equal to the competitor's row for a token PUT, a token DELETE and an empty-token create; the old token sent again is 409 |
| The re-run is bounded | `ComplexityChainRaceTests.A_race_lost_on_every_attempt_exhausts_the_bound_and_is_stale` | A competing commit before each save: PUT and DELETE without a token make `UnconditionalAttempts` saves, then 409 |
| The expiry sweep's error after the third lost race propagates | `ComplexityChainRaceTests.A_sweep_that_loses_every_attempt_lets_the_concurrency_error_propagate`, `A_sweep_of_several_rows_that_loses_every_attempt_leaves_none_of_them_modified` | A writer rotates the expired row's token before each sweep save: 3 saves, then `DbUpdateConcurrencyException` out of `FindActiveAsync` or `ListLiveCellsAsync`; the rows stay active, no chain row stays tracked as modified and the context's next save writes 0 rows |
| A default-tier create survives that error; an explicit-complexity create does not | `MatrixDefaultComplexityCreateTests.A_default_tier_create_whose_expiry_sweep_loses_every_race_is_created_with_the_warning`, `An_explicit_complexity_create_whose_expiry_sweep_loses_every_race_still_fails` | Default tier: Queued on the RolePolicy pair with the `Routing read failed` warning, Complexity null; explicit `Medium`: `DbUpdateConcurrencyException`, no task row. Service level, not HTTP |
| The expiry sweep rotates the token and re-reads after a lost race | `ComplexityChainRaceTests.The_expiry_sweep_rotates_the_token_so_a_write_that_read_the_row_live_loses_its_save`, `A_sweep_that_loses_to_a_write_reads_again_and_returns_the_writers_row` | A token PUT that read the row before the sweep is 409 with no row; a no-token PUT recreates it; a sweep that lost to a write returns the writer's row |
| The `?role=` view reads the token before the candidates | `ComplexityChainRaceTests.The_role_view_reads_the_token_before_the_candidates_so_a_change_between_them_makes_a_save_stale` | Re-created between the reads: newer candidates, older token, a save with it 409; created between them: no token, a save with the empty GUID 409; cleared between them: no row, no token |
| A role cell's token is its own row's | `ComplexityChainConcurrencyHttpTests.Effective_view_carries_the_cells_own_token_not_the_any_role_rows` | `?role=Review` Hard carries the cell token; Medium (via any-role) carries null; the list's any-role Medium carries its token |
| Script sends the read token | `ComplexityChainScriptTests.Set_and_clear_send_each_cells_token_from_one_read_and_stop_on_stale` | One GET, then per-cell token or the empty GUID; a 409 stops `set -Complexity All` with `code complexity_chain_stale` |
| Ordinary Worker default-tier adoption | `MatrixDefaultComplexityCreateTests.A_plain_create_walks_the_role_row_at_the_default_tier`, `An_unset_row_keeps_the_role_policy_path` | Resolved Plan/Medium is stamped and walked; unset Plan uses RolePolicy |

```
GET    /api/model-availability                         active holds + remaining aliases
PUT    /api/model-availability/{kind}/{alias}          Manual hold (Source=Manual). alias may be *
DELETE /api/model-availability/{kind}/{alias}          clear (204 if already clear)
```

`kind` is the enum member name (`ClaudeCode`, `Grok`, `Codex`). `alias` is a `ModelLevelAliases`
value or `*` (kind-wide, OR'd with per-alias rows). PUT body: `{ disabledUntil?: utc, reason?:
string }`. Omitted `disabledUntil` is open-ended. Past UTC is 422. Script:
`scripts/model-availability.ps1 get|hold|clear`.

```
GET    /api/subscription-usage                        latest stored observation per (kind, profile)
```

`SubscriptionUsageEndpoints.cs` (CARD-0333 S1) is a **read-only** projection of
`SubscriptionUsageReader`'s stored samples. It never polls a provider, starts a monitoring sweep, or
flips `SubscriptionUsageMonitoringSettings.Enabled`; no samples is an empty array, not an error.
Each `SubscriptionUsageObservationDto` carries only display-safe fields: `provider` (`AgentKind`),
`planLabel`, `remainingPercent`, `resetsAt`, `observedAt`, `age` — nulls are preserved (a missing
percent/reset is JSON `null`, never a manufactured 0/100). It never returns raw command output,
subscription/profile keys, paths, credentials, or session identifiers, and the observation is
provider/profile-level, not per-model-alias — there is no aggregate fleet percentage and no
"usage remaining" field on a model-availability row. Treat an old `observedAt`/`age` as stale
evidence, not current capacity; monitoring being off or having no sample for a provider looks
identical to this endpoint (empty/absent entry) — the client renders both as "unknown," not "zero."

#### Caller handoff actions and Review evidence corrections (CARD-1303 R10)

The four routes above are implemented by `PipelineHandoffActionEndpoints.cs` and
`ReviewEvidenceCorrectionEndpoints.cs`. All route IDs here are full GUIDs; `{id}` is the
predecessor task for handoffs and the Review task for corrections. These surfaces require
`X-Antiphon-Operator-Token` or an authorized `X-Antiphon-Task-Token`; they do not use the
anonymous UI fallback. A non-operator must have delegation permission and matching project,
repository-root and applicable card/board scope. Handoffs additionally require the predecessor's
completion-recipient session. Scope denials are **403** `handoff_principal_denied`,
`handoff_project_denied`, `handoff_repository_denied`, `handoff_card_denied`,
`handoff_board_denied`, `handoff_recipient_denied`; correction denials use
`review_correction_principal_denied`, `review_correction_project_denied`,
`review_correction_repository_denied`, `review_correction_card_denied`,
`review_correction_board_denied`. Missing task/action is 404. JSON uses camelCase and enum names;
these request DTOs reject unknown members (400). Service validation is 422 with `errors`.

**Record/read a handoff.** `PipelineHandoffActionRequest` in
`server/Application/Dtos/PipelineHandoffActionDtos.cs` has required fields `attempt` (int >= 0),
`settlementEventId` (nonempty GUID), `nextStage` (`AgentTaskRole` name),
`expectedObservationSequence` (long >= 0), `reasonCode`, `detail` (1–2000 nonblank characters).
Optional nullable fields: `nextObservationAt`, `workflowRunId`, `pipelineDefinitionRevisionId`,
`currentStageId`, `workflowUpdatedAt`, `receipt`. The predecessor must still be Succeeded at that
attempt, with the matching Completed settlement event and authorized next stage. Workflow
coordinates must match the card's active run/revision/stage/update time; without a run the
other workflow fields must be null. `reasonCode` is `consumed`, `eligible`, or a hold:
`dependency`, `decision`, `capacity`, `overlap`, `priority`, `evidence`, `quota`, `placement`.
A hold requires a future `nextObservationAt`; supplied observation/workflow timestamps must
be UTC. Start at sequence 0 and use the latest observation sequence for each new write.
An identical last observation can be replayed with its original expected sequence and actor.

`receipt`, when supplied, is `PipelineHandoffReceiptReference`:
`{ notificationId, queueId, recipientSessionId, recipientGeneration, receiptSequence, wireDigest }`.
`recipientGeneration` is the session's UTC start timestamp. The service validates the confirmed
completion notification, queue/delivery generation, transcript UserPrompt and wire/spill digest.
The current API permits receipt-less observations, including `eligible`; such consumption is
caller accounting, not independent proof of transcript delivery. A bound receipt cannot change
or be omitted on a subsequent observation.

POST returns a `PipelineHandoffActionDto` object; GET returns an array ordered by action creation
time then ID. Each object has `{ action, observations, requestedWithinFiveMinutes,
unexplainedWaitRequiresInvestigation }`. `action` is a `PipelineHandoffAction` snapshot with:

| Group | Fields |
|---|---|
| Identity/fence | `id`, `schemaVersion`, `predecessorTaskId`, `attempt`, `settlementEventId`, `nextStage`, `cardId`, `projectId`, `workflowRunId`, `pipelineDefinitionRevisionId`, `currentStageId`, `workflowUpdatedAt` |
| Optional receipt | `notificationId`, `queueId`, `recipientSessionId`, `recipientGeneration` (round-trip timestamp string), `receiptSequence`, `wireDigest` |
| Clocks/state | `settledAt`, `notifiedAt`, `receivedAt`, `consumedAt`, `eligibleAt`, `childCreatedAt`, `childDispatchedAt`, `reasonCode`, `nextObservationAt`, `requestDigest`, `actor`, `childTaskId`, `createdAt` |

`observations` is sequence-ordered, append-only history of
`{ id, actionId, sequence, reasonCode, detail, actor, observedAt, nextObservationAt }`.
Child clocks are projected from the actual child. `requestedWithinFiveMinutes` is null until
both eligibility and child exist, then tests creation within [eligibleAt, eligibleAt + 5 minutes].
`unexplainedWaitRequiresInvestigation` is true when there is no child and the latest reason
is `eligible` with eligibility older than 15 minutes. GET creates no consumption or child.

**Request/recover a child.** `PipelineHandoffChildRequest` is
`{ task: CreateAgentTaskRequest, acceptSpend: true, trigger: "caller" }` (`acceptSpend` defaults
false; `trigger` defaults `caller`). It admits a fresh Worker, without a specialist role, pinned
agent or `followUpOnTask`. The nested task must match the action's card/project/next stage.
The caller pilot cannot set `ignoreConcurrencyLimit`, `ignoreSubscriptionQuota`,
`ignoreModelDisabled`, `ignoreRoutingPin` or `allowUnauthenticatedProvider`.
Success is **200**, with the ordinary `AgentTaskCreatedDto` (`id`, `shortId`, `status`,
`modelLevel`, `agentKind`, card/platform/runner fields and ordinary optional creation metadata;
authority: `server/Application/Dtos/AgentTaskDtos.cs`). Repeating the same action and original
request digest returns the existing child without another spend or launch-gate evaluation.
For an unbound action, a 409/422 admission refusal appends an observation with its code and
a five-minute next-observation time. Read the history after a lost answer before changing a request.

Handoff **409** codes from `PipelineHandoffActionService` and `AgentTaskService`:

| Codes | Meaning / recovery |
|---|---|
| `handoff_predecessor_changed`, `handoff_settlement_changed`, `handoff_workflow_changed`, `handoff_scope_changed` | Identity or admission fence changed; re-read predecessor, card/workflow and action before a fresh decision. |
| `handoff_observation_changed`, `handoff_already_requested`, `handoff_receipt_changed`, `handoff_foreign_receipt` | Stale sequence, already-bound child, or unproven/different receipt; read history and the retained receipt coordinates. |
| `handoff_request_changed`, `handoff_child_missing`, `handoff_child_mismatch` | Different request digest or invalid/missing child binding; recover the original keyed request and investigate the binding. |
| `handoff_trigger_held`, `handoff_spend_held`, `handoff_override_held`, `handoff_transaction_owned` | Only the explicit spend-accepted caller pilot is admitted, without overrides and with its own transaction. |
| `handoff_stage_held`, `handoff_decision_held`, `handoff_dependency_held`, `handoff_overlap_held`, `handoff_not_eligible`, `handoff_capacity_held` | Resolve the named stage, decision, blocker, writer overlap, recorded eligibility or host-capacity hold before reconsidering. |

Ordinary creation refusals also apply to a new child (including `concurrency_limit`,
`subscription_quota_low`, routing/model/provider refusals and 422 placement validation).
`handoff_requires_fresh_task` is **422**, not a 409. Card-bound implementation admission
(`Code`, `Custom`, `Debug`, `Docs`, `Merge`, `Commit`, `Deploy`) on ordinary create as well
as a new handoff child can return **409 `card_implementation_closed`** for Done, Canceled or
archived cards, or **409 `card_admission_changed`** when board, identifier, required platform,
or either verification policy changes between the pre-read and the locked decision, or when
status leaves the open columns (Backlog, In Progress, Review) for NeedsDecision. A
transition-sweep move among those open columns, or an active workflow run change, is re-read
under the card lock and admitted from that locked state; it is not `card_admission_changed`.
Re-read the card after a fence refusal; do not replay or bypass a closed-card refusal.

**Correct Review evidence.** `ReviewEvidenceCorrectionRequest` in
`server/Application/Dtos/ReviewEvidenceCorrectionDtos.cs` has required fields
`{ predecessorEvidenceId, expectedReportDigest, subjectTaskId, sourceSha, sourceRef,
repositoryPath, selection, producerIdentity, bundleDigest, reason }`.
`selection` is `{ path, commitSha, section, digest, rows }`: a committed repository-relative
`docs/**/*.md` path, full commit object ID, exact single-line section (<= 200 characters),
SHA-256 digest, and 1–100 unique `CP-n` row IDs. `sourceSha` is a full Git object ID;
`sourceRef` starts `refs/heads/`; report/bundle/selection digests are 64 lowercase hex characters;
`reason` is nonblank and <= 2000 characters. `expectedReportDigest` hashes the retained Review
result's UTF-8 bytes. `producerIdentity` selects an operator-pinned `CheckpointReceipts:Producers`
entry, never an arbitrary caller-supplied receipt path. `bundleDigest` pins its inventory.

This attaches validated execution to an existing Succeeded Final/profile-v1 Review of the same
Code/Worktree subject and repository. The retained report must contain the single typed
`--- correction assessment v1 ---` block with `Behavior` = `Satisfied` and matching subject/source
coordinates; the commissioned goal's `--- correction selection v1 ---` block must exactly match
the requested selection. It does not replace a substantive behavior Review or edit historical
outcomes/costs. Complete validation appends a linked Clean Final/Full successor and correction audit.

All dispositions return **200** `ReviewEvidenceCorrectionResponse`:
`{ disposition, correctionId, successorEvidenceId, missingRows }`.
`corrected` and replay `already-corrected` include both GUIDs and an empty `missingRows` array.
`incomplete` has null IDs and the missing row IDs; no successor is created. A bundle pin mismatch,
missing/failed required execution or failed validator can be incomplete rather than 409.
Restore exact commissioned execution evidence before retrying; incomplete is not land approval.

Correction **409** codes from `ReviewEvidenceCorrectionService` and `CheckpointReceiptValidator`:

| Codes | Meaning |
|---|---|
| `review_evidence_correction_review_ineligible`, `review_evidence_correction_predecessor_ineligible`, `review_evidence_correction_behavior_requires_review` | Wrong Review/predecessor eligibility or absent Satisfied behavior assessment; ordinary Review is required for behavior gaps. |
| `review_evidence_correction_report_changed`, `review_evidence_correction_prepared_report_changed`, `review_evidence_correction_assessment_coordinates_mismatch`, `review_evidence_correction_subject_changed`, `review_evidence_correction_predecessor_superseded`, `review_evidence_correction_correction_changed` | Retained report, subject/source coordinates, active predecessor or existing correction changed. Re-read the identities before a new decision. |
| `review_evidence_correction_commissioned_selection_mismatch`, `review_evidence_correction_selection_invalid`, `review_evidence_correction_selection_not_committed`, `review_evidence_correction_selection_digest_mismatch`, `review_evidence_correction_selection_section_ambiguous`, `review_evidence_correction_selection_table_missing`, `review_evidence_correction_selection_row_unsupported`, `review_evidence_correction_selection_rows_missing` | Selection differs from its commission or cannot be proved as a supported committed checkpoint table. |
| `review_evidence_correction_producer_untrusted`, `review_evidence_correction_producer_repository_mismatch`, `review_evidence_correction_inventory_digest_mismatch`, `review_evidence_correction_producer_mismatch`, `review_evidence_correction_receipt_coordinates_mismatch`, `review_evidence_correction_duplicate_rows`, `review_evidence_correction_manifest_digest_mismatch`, `review_evidence_correction_receipt_invalid`, `review_evidence_correction_storage_unconfigured`, `review_evidence_correction_retained_evidence_unavailable` | Operator producer pins, receipt provenance/integrity or retained storage cannot be validated. |
| `review_evidence_correction_link_missing`, `review_evidence_correction_link_mismatch`, `review_evidence_correction_link_report_changed`, `review_evidence_correction_link_validation_mismatch`, `review_evidence_correction_link_validation_invalid` | Replay/land validation cannot prove the append-only correction link or its saved validation. |

Invalid selection path/commit/section shape is **422 `verification_selection_invalid`**.
Corrected evidence remains dependent on the retained bundle under `CheckpointReceipts:StorageRoot`:
replay and land re-hash it and can refuse with `review_evidence_correction_retained_evidence_unavailable`.
For land this is **409**, with a detail naming the retained bundle digest and absolute inventory
path when its inventory or any referenced blob is missing, unreadable, or changed (CARD-1303 R12).
Restore the original bundle at that path, ensure the validating server can read the
`CheckpointReceipts:StorageRoot` mount, then retry land. If the exact retained bytes cannot be
restored, commission a new Review of the exact source and use its evidence. Refusal preserves
the correction/history and creates no landing request or queue entry; unverifiable evidence
never approves landing.
Preserve that bundle and make it available to the validating server; the correction ID alone is not proof.

### Schedules

`ScheduleEndpoints.cs` — the API behind `scripts/schedule.ps1` (CARD-0057: prompt delivery and card actions).

```
GET    /api/schedules?agentId=&cardId=&boardId=&enabled=
GET    /api/schedules/{id}                   last 10 fires included
POST   /api/schedules                        create (CreateScheduleRequest)
POST   /api/schedules/preview                same body, no write
GET    /api/schedules/{id}/preview
PATCH  /api/schedules/{id}                   concurrency token; recomputes NextFireAt
DELETE /api/schedules/{id}
POST   /api/schedules/{id}/fire-now          bypasses grace, does not advance recurrence
```

`CreateScheduleRequest` / `ScheduleDto` live in `server/Application/Dtos/ScheduleDtos.cs`. `kind: Prompt` enqueues WhenIdle onto a standing agent. `kind: Card` moves a card (`Start=None` bookkeeping, `Release` lifts the auto-dispatch hold, `Spawn` launches now). `Release`/`Spawn` without `acceptSpend: true` is 422 `spend_unacknowledged` with the preview embedded. Repeat is `Once` / `Interval` / `Daily` — no cron. Enums are names. Long prompt text belongs in `-PromptFile` on the script, never inline. Card create: `schedule.ps1 new -Card … -To … -Start … -AcceptSpend`. `cardId` on create/preview/list is identifier-or-guid (`CARD-nnnn`, `card-51`, `#51`, `51`, or the guid), resolved by `CardService.ResolveCardIdAsync`.

### Runner profiles and credentials

`AgentTuiEndpoints.cs`, `ApiKeyEndpoints.cs` — see
[ai-agent-tui-configuration.md](ai-agent-tui-configuration.md) and
[agent-credentials.md](agent-credentials.md).

CARD-0167 adds `providerEndpoints` to project create/PUT/read DTOs: a complete replacement map
keyed by `ClaudeCode`, `Grok` or `Codex`, each value `{ "baseUrl": "http://127.0.0.1:8088",
"apiKeyName": "proxy-key" }` (sentinel example). Null/omitted leaves the map unchanged; `{}` clears.
`PUT /api/projects/{id}` still requires the other project fields; retain them from a fresh GET.
Agent create/PATCH/read DTOs add `providerEndpointMode` (`Inherit`, `Endpoint`, `Direct`) and
`providerEndpoint` (the same URL/name shape). Endpoint mode requires an endpoint; Inherit/Direct
require no own endpoint. Null fields together leave an existing setting unchanged; set the mode
explicitly to clear an own endpoint. Direct does not remove generic env values.

Save validates HTTP(S) URL shape and key names; Codex requires a key name. Invalid input is 422
(`provider_endpoint_unsupported_kind`, `provider_endpoint_key_required` and field-specific URL
codes). A missing named key produces a `warnings` entry without rejecting save; launch refuses
`api_key_not_found`. GET runner-types adds a `providerEndpoint` descriptor with `supported`,
`keyRequired`, `environmentNames` and `argumentTemplate`. These are names/templates, never key
values. Runner-bound Claude/Codex credentials still refuse `phone_home_env_refused`. See
[provider endpoint operations](agent-credentials.md#provider-endpoint-operations-card-0167).

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

POST   /api/delegation-capabilities                  issue (201, body includes token + storePath hint)
GET    /api/delegation-capabilities                  list (no token)
GET    /api/delegation-capabilities/{id}             detail (no token)
POST   /api/delegation-capabilities/{id}/rotate      200, new token
POST   /api/delegation-capabilities/{id}/revoke      200, no token
```

Secret and key **values are write-only** — nothing reads one back out over HTTP. Capability GET
list/detail never include the raw token; issue/rotate return it once for `scripts/capability.ps1`.
`IssueDelegationCapabilityRequest` / `DelegationCapabilityDto` live in
`server/Application/Dtos/DelegationCapabilityDtos.cs`.

### Channels

`ChannelEndpoints.cs`

```
GET    /api/channels/consumer                effective inbound consumer identity (group/topic/enabled/broker host-port allowlist)
GET    /api/channels                         the catalog (rows appear on first inbound message)
GET    /api/channels/outbound-profiles       configured profile previews (project, worker, trigger, prompt revision, timeout)
POST   /api/channels/outbound-deliveries/{id}/retry  explicit uncertain-publication retry; body {"acknowledgePossibleDuplicate":true}
POST   /api/channels/outbound-deliveries/{id}/resume explicit Held retry after original channel binding is repaired
PATCH  /api/channels/{id}                    bind/unbind an agent, preamble, enable/disable, outboundAgentProfile / clearOutboundAgentProfile
```

`ChatChannelService.SendAsync` is an internal proactive send primitive, not an HTTP route. It
bypasses outbound conversion and produces a `ChannelReply` on `channels.outbound`; a disabled
channel is `409 channel_disabled`. There is deliberately no general `POST /{id}/send` route.

`outboundAgentProfile` is an explicit per-channel selection. The named profile must pin a
delegatable worker on the inbound agent's project and a prompt file inside its workspace. A
selected agent reply is accepted as a durable outbound delivery first; it appears on the same
`channels.outbound` topic only after optional conversion and validation. An omitted PATCH field
leaves the binding alone; `clearOutboundAgentProfile: true` clears it. The channel UI previews worker, trigger, deadline,
prompt revision and the automatic metered invocation before a binding is saved.

`ChatChannelDto` (`server/Application/Dtos/ChatChannelDtos.cs`): `lastMessageAt` / `lastAuthor` /
`lastMessagePreview` are inbound only. `lastReplyAt` / `lastReplyPreview` are the last outbound
reply (agent or server-composed). Do not read inbound silence as agent idleness.

`ChannelBridge:MachineTurnTextOrigins` (default `Delegation`, `Check`, `Scheduled`) is the
dial for delivering a machine-triggered turn's plain text as a follow-up; empty is attachments
only. `Channel` / `Ui` / `Supervision` are rejected. Exact `NO_REPLY` opts out. System stays
marker-only unless listed. `Digest:WakeOnIncidentKinds` (default `ChannelReplyLost`) is the
pager kinds for `IncidentPageNotifier`.

The whole inbound/outbound model is [messaging/build-your-own-gateway.md](messaging/build-your-own-gateway.md).

### Orchestration and workflows

`OrchestratorEndpoints.cs`, `WorkflowEndpoints.cs`, `GateEndpoints.cs`, `CascadeEndpoints.cs`,
`ArtifactEndpoints.cs`

```
GET    /api/orchestrator/state    POST /api/orchestrator/pause | /resume | /tick
                                  running = card-spawn sessions plus the current session of every
                                  Dispatched/Working non-Check task; Blocked and interactive
                                  sessions excluded.

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
GET    /api/attention                        the one "what needs a human" projection — fleet-global across every board; there is no board filter. Optional `?minSeverity=` (CARD-1085) is omitted for the previous unfiltered feed. The names Info, Warning, Error, and Critical match case-insensitively and drop rows below that floor. Empty, whitespace, numeric, and unknown values are 400 application/problem+json (title Bad Request, code bad_request). `kindCounts` is the unfiltered count per kind.
GET    /api/plans  |  /api/plans/content     read-only projection over plan markdown in the repo (git is the store; there is no write path and there will not be one)
GET    /api/audit  |  /cost-summary  |  /cost-ledger  |  /conversation    DELETE /api/audit/archive
GET    /api/github/status  |  /repos  |  /repos/{owner}/{repo}/branches   POST /api/github/repos/refresh
GET    /api/filesystem/browse  |  /workspaces  |  /worktrees              ⚠ enumerates host directories
GET/POST/PUT/DELETE /api/settings/templates | /template-groups | /providers
GET    /api/settings/templates/{id}/stages
POST   /api/settings/providers/{id}/test
GET/POST /api/settings/providers/{id}/model-routing   PUT|DELETE /api/settings/model-routing/{routingId}
GET    /health                               liveness + PostgreSQL
GET    /api/version                          build-time git SHA (CARD-0179) plus `capabilities` (CARD-0495 `land-v2`); /health stays the literal Healthy body
POST   /api/diagnostics/bundle               Report-bug zip (application/zip); best-effort members + errors.txt
GET    /api/diagnostics/session-state        CARD-0701 process/cache counters, live/unknown counts, sanitized pool flags (no transcript text)
POST   /api/operator/dashboard-sessions      one-time Hangfire dashboard login link; needs X-Antiphon-Operator-Token (CARD-0658)
GET    /api/operator/dashboard-login?nonce=  redeems the link once; sets the /hangfire session cookie, 302 to /hangfire
```

`GET /api/attention` includes `CardNeedsDecision` rows for cards currently in the Needs decision
state. Each row carries the card and board IDs, the move/reopen reason as its evidence, and the
`OpenCard` action. The dedicated human-decision surface is
`/orchestrator?tab=decisions`; it uses this same feed rather than a second decisions endpoint.

`AttentionKind.ImportedIssueNeedsReview = 28` (CARD-0327; appended, not renumbered — `27` is
`LivenessProbeFailed`) is a `Warning`, not a decision, so it does not count toward
`AttentionSummaryDto`'s decision total. One row per card with `needsHumanReview == true`: `Title`
is `CARD-nnnn — title`, `Headline` is `Raised on GitHub by <author> (not an operator) — nobody has
rated it.`, `Evidence` carries the issue key/URL, the first line of the body, and the ready-to-paste
`delegate.ps1` triage command (see [workflow-tracker-block.md](workflow-tracker-block.md)), and
`Actions` is `[OpenCard]`.

`AttentionKind.CommitRecoveryPending = 41` (CARD-0547; appended after 40, not renumbered) is an
`Error` row for a Dispatched/Working task whose oldest unresolved commit-recovery obligation is
older than two minutes: settlement made a gated commit but could not save its record, and the
overdue watchdog and dead-session reconciler hold the task for the re-hand. It is evaluated before
`DeadSession`. `SinceUtc` is the obligation start; `Evidence` (not excerpted) lists the obligation
id, the settlement digest, the `git log --all --reflog` recipe, the hold expiry and the
`/retry {"abandonCommitRecovery":true}` request; `Actions` is `[OpenDrawer, Cancel]`.

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
POST /sessions/{id}/absence-evidence/prepare   signed; records a fresh, never-used id (CARD-1153)
POST /sessions/{id}/absence-evidence           signed never-created certificate (CARD-1153)
GET  /events              SSE
```

`GET /capabilities` is worth knowing about even from outside: it is how you check which pty backend
is configured for new sessions (`ModernConPty`, deprecated `InboxConhost`, or `UnixPty`) and
whether the runner advertises `herdr`. CARD-1022 release A appends nullable
`ptyBackendDeprecated` after the four Codex CLI observation fields: true for legacy/fallback,
false for modern/Unix, null/absent for an older peer. `ptyBackendRequested` preserves raw input;
`ptyBackendFellBack` distinguishes a missing-pair fallback from an explicit legacy request.
HTTP and phone-home project the same runtime decision. These observations are not delivery
receipts or a new admission gate. Windows defaults to modern; Unix retains Porta.

**Runner absence evidence (CARD-1153).**
POST /sessions/{id}/absence-evidence/prepare and POST /sessions/{id}/absence-evidence are runner routes without /api; ordinary session and transcript GETs still return 404 for a never-created ID.
Pinned by `RunnerAbsenceEvidenceContractTests.C1153_Real_unknown_transcript_has_a_separate_certificate`
and `RunnerAbsenceEvidenceContractTests.C1153_Http_authentication_covers_request_and_response`.
Requests and responses are HMAC-SHA256 signed with the key in
[agent-credentials.md](agent-credentials.md#runner-absence-evidence-key-card-1153). A later start
or attach of a certified id is refused with `session_identity_closed` (phone-home
`phone_home_session_identity_closed`). Phone-home carries the same operations as
`PrepareAbsenceEvidence` (38) and `CertifyAbsence` (39). The wire contract is in `AbsenceEvidenceRoutes`
and `RunnerAbsenceEvidence`, pinned by `RunnerAbsenceEvidenceContractTests`.

**CARD-0478 runtime checkpoint, not a complete deployed feature:** this branch adds
`verificationCustodyV1`, `verificationCustodyBackend` and stable `runnerStoreId` to
capabilities, plus these internal routes:

```text
GET  /sessions/{id}/executions/{executionId}/custody?acceptedStartedAt=<UTC round-trip timestamp>
POST /sessions/{id}/executions/{executionId}/seal
```

POST takes the exact `VerificationExecutionBinding` as its body and durably
closes admission for that execution; it does not kill. GET observes without
sealing a running root. The binding includes the expected capability store ID
and accepted application generation at microsecond precision. A terminal result
carries the exact UTF-8 receipt bytes (JSON encodes byte arrays as base64), after
runner acceptance. Unknown/Unsupported results confer no exit authority.
Application task creation now accepts optional `repairSourceTaskId` (full owner GUID) and
optional `sourceLandingOperationId`; task detail exposes `progressEvidence` (assessment,
reason, sources with origin/ownerTaskId/commit), `repairSourceTaskId`, `sourceLandingSha`, `verificationCleanupResidue`, the cleanup seal id,
execution revision, removal facts, and typed `verificationExecutions` with
`custodyReason` and receipt digest. The explicit
`POST /api/agent-tasks/{id}/cleanup-verification` seals a terminal sourced task and
imports all-attempt custody before guarded removal. It never kills or publishes.
See [ops-http.md](ops-http.md#post-land-verification-card-0478) for the CLI and restrictions.
The full ordinary acceptance matrix remains incomplete; do not deploy this
[checkpoint](investigations/2026-09-11-card-0478-application-custody-checkpoint.md).

## 5. Front doors you probably want instead

For the common operations, use the scripts — they handle base-URL resolution, card-id forms,
concurrency tokens and PowerShell's quoting hazards.

| Script | For |
|---|---|
| `scripts/card.ps1` | `get`, `history`, `new`, `edit`, `move`, `close`, `reopen`, `archive`, `unarchive`, `-Limits`. Long text via `-DescriptionFile` / `-ReasonFile`, never inline. |
| `scripts/schedule.ps1` | `list`, `get`, `preview`, `new`, `enable`, `disable`, `remove`, `fire`. Long text via `-PromptFile`. Prints preview before create. |
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

## Card-file publication (CARD-0408)

[Card-file privacy](card-file-privacy.md) owns opt-in settings/status/sync, the
explicit private-notes boundary, private snapshots, revocation and cleanup.
Boards default off and Unknown repository visibility blocks publication. Private
notes do not appear in ordinary DTOs or generated markdown. Outcome and archive
reasons remain public fields on eligible cards. Cleanup pending is independent of
card/session state; disabling the feature freezes existing exports.

## Standing conversation recovery (CARD-0466)

`GET /api/agents/{id}/sessions?take=25&before={session-guid}` returns owned metadata history
with `items` and `nextBefore`. Start appends mutually exclusive `resumeSessionId`,
`retryContinuity` and existing `fresh`; POST acceptance means queued. See
[the operator flow](ops-http.md) for strict ownership, refusals and safe queued input.

### CARD-0680 additive runner input metadata

`GET /sessions/{id}/snapshot` includes optional `dialog` on runners advertising
`claudeDialogGuardV1`. `POST /sessions/{id}/conditional-input` adds
`requireClaudeDialogClear`, `expectedInputRevision`, `expectedRuntimeEpoch` and a
finite internal `dialogIntent`. HTTP and phone-home carry identical fields.
Ordinary automation uses the default intent; named startup/negative/RC intents
accept only their exact current layout and permitted key. The existing explicit
interactive input route is separate. Missing capability/metadata refuses guarded
writes. Runtime and server activation must both precede use; a land alone does
not activate the guard. Native Windows and Unix watchdog qualification is separate.
