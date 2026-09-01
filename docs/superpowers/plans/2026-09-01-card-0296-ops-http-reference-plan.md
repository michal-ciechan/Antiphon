# CARD-0296 — Document the agents/boards/sessions HTTP surface orchestrators actually use

**Date:** 2026-09-01 (Plan pass, task 69e1cde7 — design only; no code changed)
**Card:** CARD-0296 "No documented agents/boards/sessions API reference — orchestrator resorted to grepping controller source"
**Diagnosis:** done on the card (session `fdf1dd3d`: 404 on `/api/board` and `/api/sessions`, 400 on `/api/cards?limit=1`). This pass verified the live routes against `AgentEndpoints`, `BoardEndpoints`, `CardEndpoints`, `SessionEndpoints`, and `src/Antiphon.SessionRunner/Program.cs`.

**Sources (verified this pass):** CARD-0296, `scripts/card.ps1` header, `server/Bundles/board-api.md`, `server/Bundles/orchestrator.md`, `server/Bundles/README.md`, `AGENTS.md` (CARD-0254 core), `docs/orchestration-loop.md` §7, `docs/bootstrap.md` ports, `InstructionBundleTests` catalog pin, `project.ps1` (exists; no `agents.ps1`).

---

## Decision

**A short living doc is the product. No `scripts/agents.ps1` on this card.**

`card.ps1` exists because cards are addressed as `CARD-nnnn`, every write rotates a concurrency token, and bodies must come from a file. The wanted surface is almost all **GET**, ids are guids (or a list you filter by `name`/`slug`), and the write that actually bites — session input — must go through the **message queue**, not a raw pty POST. A wrapper that mirrored `POST :17204/sessions/{id}/input` would violate the LF + bracketed-paste + separate Enter contract. The card itself says the reference alone would have prevented the detour.

Shape, matching `board-api.md` ("shapes that bite", not an OpenAPI dump):

| Layer | What |
|---|---|
| Canonical | New `docs/ops-http.md` — ports, the six day-to-day calls, the 404/400 traps this session hit, archive vs delete, runner vs server |
| Index | `AGENTS.md` "Read before changing" **one row** + one Essential front-door bullet (CARD-0254: AGENTS.md is the index, not the essay) |
| Prompt | 4–6 lines at the end of `server/Bundles/orchestrator.md` pointing at that doc. Orchestrators already pay for this bundle; they will see "do not grep MapGet" without a new catalog key |
| Loop | One sentence in `docs/orchestration-loop.md` §7 next to the `card.ps1` paragraph |

**No new instruction bundle.** A second `ops-api.md` would need `InstructionBundleTests` catalog update, an orchestrator-preset attachment, and duplicate the doc in every launch. `board-api.md` stays cards-only.

**No wrapper.** Follow-up only if name-to-guid resolution for agents keeps hurting after the doc exists.

---

## Ground truth (routes, 2026-09-01)

Two processes. Mixing them is how this session got 404s.

| Process | Port | Prefix |
|---|---|---|
| Antiphon server (Aspire) | **17202** | `/api/...` |
| Session-runner (production) | **17204** | `/sessions/...` — **no `/api`** |

`$env:ANTIPHON_API` (default `http://localhost:17202`) + optional `$env:ANTIPHON_TASK_TOKEN` as `X-Antiphon-Task-Token` — same as `card.ps1`.

### Server (17202) — use these

| Need | Method | Path | Bite |
|---|---|---|---|
| List agents | GET | `/api/agents` | Plural. Includes `liveSession` when running. Id on other routes is **guid only** (no slug resolver). |
| One agent | GET | `/api/agents/{id:guid}` | |
| Start / stop | POST | `/api/agents/{id}/start`, `.../stop` | Stop is the named-agent kill. Empty `{}` start inherits flags. |
| Delete agent | DELETE | `/api/agents/{id}` | Hard delete; no archive. CARD-0295: no Running/AlwaysOn guard. |
| List boards | GET | `/api/boards` | Plural. `?includeArchived=true`. **No `/api/board`.** |
| One board | GET | `/api/boards/{id:guid}` | `?view=summary`, `?includeArchived=` |
| Board columns | GET | `/api/boards/{id}/columns` | Name → column id without the full payload |
| Archive board | POST | `/api/boards/{id}/archive` | Reason required; 409 if agents attached. Not DELETE. |
| Hard-delete board | DELETE | `/api/boards/{id}` | Detaches agents, does not delete them |
| List a board's cards | GET | `/api/cards?boardId={guid}` | **At least one of `boardId`, `status`, `updatedSince` is required** — otherwise **400**. There is no `limit`/`pageSize`. That is the `?limit=1` 400. |
| One card | GET | `/api/cards/{id}` | `CARD-nnnn` **does** resolve (use `card.ps1`) |
| Session buffer | GET | `/api/sessions/{id:guid}/buffer` | Server. **No list** `GET /api/sessions`. **No snapshot** on the server. |
| Transcript | GET | `/api/sessions/{id}/transcript?since=` | |
| Typed input | POST | `/api/sessions/{id}/messages` | Queue: LF + bracketed paste + Enter. This is the delivery contract. |
| Raw input | POST | `/api/sessions/{id}/input` | Bypass. Do not use for work bodies. |
| Kill session | POST | `/api/sessions/{id}/kill` | Records `OperatorRequest`. Prefer this or `agents/{id}/stop` over hitting the runner. |

### Runner (17204) — inspection / last resort

| Need | Method | Path |
|---|---|---|
| List **live** runner sessions | GET | `http://localhost:17204/sessions` |
| One live session | GET | `/sessions/{id}` |
| Rendered screen | GET | `/sessions/{id}/snapshot` |
| Raw input | POST | `/sessions/{id}/input` |
| Kill (runner only) | POST | `/sessions/{id}/kill` |
| Kill all | POST | `/sessions/kill-all` |

E2E owns a **random** runner port; 17204 is production. Do not send fake-gateway traffic at the live broker (existing AGENTS.md rule). Snapshot is runner-only; that is why grepping server `SessionEndpoints` never found it.

---

## Slices

### S1 — `docs/ops-http.md`

New living owner. Tone of `board-api.md`: standing rules, no "today's incident" narrative except as a named trap.

Sections, in order:

1. Two ports, two prefixes. `$env:ANTIPHON_API`. Token header.
2. The six jobs the card named, as a table plus one `Invoke-RestMethod` example each (PowerShell, ASCII).
3. Traps: `/api/board`, `/api/sessions` list, `/api/cards` without a filter, `pageSize`/`limit`, grepping `MapDelete.*boards` and missing `POST .../archive`.
4. Input: messages vs input; pointer to session-runtime-invariants (do not restate the paste contract).
5. Kill: `agents/stop` vs `sessions/kill` vs runner kill. CARD-0221 three-state reminder: kill, pool, or owned — one sentence + link.
6. Out of scope: OpenAPI, every Review/Files/Tracker route, `delegate.ps1` (already documented).

Re-read the four endpoint files at execute time; if a path moved, the doc follows the code.

### S2 — Findable without grepping

- `AGENTS.md` table: row `Inspecting agents, boards, and live sessions` → `docs/ops-http.md`. Keep it one line.
- `AGENTS.md` Essential front doors: one bullet — list/inspect via that doc; `card.ps1` remains the card write path; do not grep `MapGet`.
- `docs/orchestration-loop.md` §7: after the `card.ps1` paragraph, one sentence pointing at `ops-http.md` for agents/boards/live sessions.
- `server/Bundles/orchestrator.md`: 4–6 lines at the **end** (standing rule, not a heading-heavy essay):

  > Inspecting agents, boards, and live sessions: read `docs/ops-http.md`. Do not grep `MapGet`/`Program.cs` for routes. Server is `:17202` `/api/...`; the session-runner is `:17204` `/sessions/...` (no `/api`). There is no `GET /api/sessions` and no `GET /api/board`. Typed input goes to `POST /api/sessions/{id}/messages`, not the runner's `/input`.

  `InstructionBundleTests` already pins phrases in `OrchestratorContract`; those Stay. Adding a tail must not drop the opening "You are an orchestrator." Catalog keys stay the same (no new bundle file).

### S3 — Pins

- `InstructionBundleTests.the_catalog_holds_exactly_the_bundles_that_ship` still lists the current eight keys.
- Existing `OrchestratorContract.ShouldContain(...)` still green.
- Optional: one new assert `OrchestratorContract.ShouldContain("docs/ops-http.md")` so the pointer cannot be deleted silently.
- No HTTP/E2E test. This is a doc card.

---

## What this card does not do

- `scripts/agents.ps1` / `boards.ps1` / `sessions.ps1`.
- New bundle `ops-api`.
- Expanding `board-api.md` into a general HTTP dump.
- Documenting every endpoint (review files, tracker, workflows, TUI profiles).
- Changing routes, adding `GET /api/sessions` list, or slug-resolving `GET /api/agents/{name}`.
- CARD-0295 cleanup semantics beyond "archive is POST, delete is DELETE, agent delete is hard".

---

## Test matrix

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0296/ -- --treenode-filter "/*/*/InstructionBundleTests/*"
```

Forward slash. Delete `bin-card0296*` after. No client tests.

---

## Sequencing and risks

**Order: S1 doc (must exist before pointers), S2 indexes, S3 pin.**

| Risk | Disposition |
|---|---|
| AGENTS.md growth | One table row + one bullet. CARD-0254 budget. |
| Orchestrator prompt tokens | 4–6 lines, not the full table. Doc is read on demand. |
| Doc drifts from endpoints | Opening line: code in `*Endpoints.cs` / session-runner `Program.cs` wins; this page is the operator map. Execute re-reads those files. |
| Wrapper demand returns | New card. Do not sneak `agents.ps1` into this one. |
| Standing orchestrator has a stale bundle until next launch | Same as any bundle edit; AlwaysOn supervision relaunches. The AGENTS.md row helps the next session even before relaunch if it reads the table. |

---

## Execution notes

- Do not paste OpenAPI. If a route is not one of the six jobs or a named trap, omit it.
- Example `Invoke-RestMethod` calls must use `$env:ANTIPHON_API` defaulting to `http://localhost:17202`, never a hardcoded hostname.
- Do not tell orchestrators to POST `:17204/sessions/{id}/input` for work.
