# CARD-0326 — `schedule.ps1 -Card` accepts CARD-nnnn, not only a raw guid

**Date:** 2026-09-03 (Plan pass, task 1fb9eaa7 — design only; no production code changed)
**Card:** CARD-0326 "`schedule.ps1 -Card` requires a raw guid, unlike every other card-addressing surface" (InProgress, Normal/Normal, rank 10)
**Repro (from the card, live 2026-09-02):** `schedule.ps1 new -Card CARD-0325 -To InProgress ...` dies at JSON binding (`The JSON value could not be converted to ...CreateScheduleRequest. Path: $.cardId`). The same call with guid `bcae3d3e-a0c5-42b8-9a3b-84f97b28afce` succeeds.

**Sources (verified this pass):** CARD-0326, `scripts/{card,schedule,delegate}.ps1`, `server/Application/Dtos/{ScheduleDtos,AgentTaskDtos,RoutingPinDtos}.cs`, `server/Application/Services/{ScheduleService,CardService,CardIdentifierScope,StandingAgentResolver,AgentTaskCardBinder,RoutingPinService}.cs`, `server/Api/Endpoints/{ScheduleEndpoints,CardEndpoints}.cs`, `tests/Antiphon.Tests/Application/{ScheduleEndpointsTests,ScheduleCardActionTests,CardIdentifierResolutionTests}.cs`, `client/src/api/schedules.ts`, `docs/{antiphon-api,ops-http,orchestration-loop}.md`, `server/Bundles/board-api.md`.

---

## Verdict up front

**Fix it on the server: `CreateScheduleRequest.CardId` becomes `string?` and `ScheduleService.ApplyCardFieldsAsync` resolves it through `CardService.ResolveCardIdAsync`.** Do not add a client-side identifier→guid GET in `schedule.ps1`. That is not how `card.ps1` works, and it would be a third resolver next to the two the repo already has.

`schedule.ps1 preview`/`new -Card` already send the `-Card` value as `cardId` with no rewrite. The CLI is not the bug. The bug is `Guid?` JSON binding rejecting `"CARD-0325"` before the service runs.

One slice, Shared workspace, ~2–3 h.

---

## Ground truth

### How `card.ps1` actually addresses a card

It does **not** look the guid up first. `Get-CardOrFail` URL-encodes the caller's string and GETs `/api/cards/{id}` with optional `?boardId=` / `?cwd=`:

```264:271:scripts/card.ps1
function Get-CardOrFail {
    if ([string]::IsNullOrWhiteSpace($Card)) {
        Write-Error "Which card? Pass it as the first argument: card.ps1 $Verb CARD-0051 ..."
        exit 1
    }
    return Invoke-Antiphon -Method GET -Path (
        "/api/cards/{0}{1}" -f [uri]::EscapeDataString($Card.Trim()), (Get-CardScopeQuery))
}
```

The header states the contract: `CARD-0051`, `card-51`, `#51`, `51`, or the guid — "there is no id to look up first."

### How `/api/cards/{id}` resolves

Every card route takes `{id}` as a **string**, not `{id:guid}`, and calls `CardService.ResolveCardIdAsync` (`CardEndpoints.cs:10-12, 268-269`). That method:

1. Guid → exact match (404 if missing).
2. Identifier-shaped (`CARD-0051` / `card-51` / `#51` / `51`) → `CardIdentifierScope` walk, unique inside the scope that answers; collision is `409 card_identifier_ambiguous`.
3. Foreign tracker key (`ANT-12`) → exact identifier / external-key match.
4. Anything else → **422**, naming the accepted forms.

`docs/antiphon-api.md:77`: "Anywhere a card id appears in a **route** it is a string, resolved by `CardService.ResolveCardIdAsync`." Body fields were left as a per-DTO choice.

### How this repo already solves the *body-field* version of the same problem

| Surface | Input type | Resolver |
|---|---|---|
| `CreateScheduleRequest.Agent` | `string?` | `StandingAgentResolver` (guid / slug / name → 422) |
| `CreateAgentTaskRequest.Card` (`delegate.ps1 -Card`) | `string?` | `AgentTaskCardBinder.BindExplicitAsync` → `CardIdentifierScope` (explicit miss is 422) |
| `PutRoutingPinRequest.Card` | `string?` | `RoutingPinService.ResolveCardAsync` → `TryCanonicalIdentifier` + `CardIdentifierScope` (miss/garbage 422) |
| `CreateScheduleRequest.CardId` | **`Guid?`** | JSON deserializer. `"CARD-0325"` never reaches the service. |

`schedule.ps1` already treats `-Agent` as an opaque string and lets the server resolve it. `-Card` is the one field that still has to be a guid for the JSON to parse.

### What `schedule.ps1` does today

- `preview` / `new`: `$body.cardId = $Card` as-is (`scripts/schedule.ps1:164`). No client resolution. This is why the CARD-0325 canary failed at `/api/schedules/preview`.
- `list`: only appends `cardId=` when the value matches `^[0-9a-fA-F-]{36}$` (`:254-256`). A friendly identifier is **silently dropped**, so `list -Card CARD-0325` returns every schedule.

`client/src/api/schedules.ts` already types `cardId?: string` on `CreateScheduleRequest`. The web client is not the constraint.

---

## Decision

### D1. Server-side string + `ResolveCardIdAsync`. Not a CLI pre-lookup.

The card offers two locations. The repo already picked one:

- **Rejected: client-side GET in `schedule.ps1`.** `card.ps1` does not do this. A pre-flight `GET /api/cards/{id}` then stuffing the guid into `cardId` would make the HTTP API still guid-only, so a curl / the web client / any other caller of `POST /api/schedules` would still 400 on `"CARD-0325"`. It also duplicates 404/409/422 handling the CLI would have to reimplement (and `schedule.ps1` currently does not print `candidates`).
- **Rejected: a new JsonConverter on `Guid?`.** That keeps the C# type lying about what JSON accepts, and invents a third mechanism beside `ResolveCardIdAsync`.
- **Rejected: copying `RoutingPinService.ResolveCardAsync` into `ScheduleService`.** Same walk, third copy. Call the method that already exists.
- **Chosen:** `CreateScheduleRequest.CardId: Guid?` → `string?`. `ApplyCardFieldsAsync` resolves through `CardService.ResolveCardIdAsync(raw, ct)` (the public overload, `CardScopeContext.None`). Persist `row.CardId = resolved` as today. JSON property name stays `cardId`.

This is the same shape as `CreateScheduleRequest.Agent` on the same DTO, and the same resolver the card routes and `delegate.ps1 -Card` already share.

### D2. Status codes for a *body field*, not a route id

`ResolveCardIdAsync` throws 404 / 409 / 422 as if the card *were* the resource. On `POST /api/schedules` the resource is the schedule; `cardId` is an input field. Match Agent / routing-pin / today's guid-miss:

| Input | Result |
|---|---|
| Guid, `CARD-nnnn`, `card-51`, `#51`, `51`, foreign key | resolve, then existing card-kind validation |
| Missing / whitespace on `kind: Card` | 422 `CardId` "CardId is required…" (today's message) |
| Garbage (`limits`, `hello`) | 422 `CardId`, remap `ValidationException` so the errors key is `CardId` not `idOrIdentifier` |
| Unknown guid / unknown identifier | 422 `CardId` wrapping the `NotFoundException` message (today's guid miss is already 422; do not flip it to 404) |
| Ambiguous identifier (two boards hold it) | **409 `card_identifier_ambiguous`** — let `ConflictException` bubble. CARD-0218's contract. Do not pick the first row. |

Catch, remap, rethrow in one private helper on `ScheduleService` so `ApplyCardFieldsAsync` stays linear.

### D3. `CardScopeContext.None` — no `-Board` / `cwd` on this card

The repro identifier (`CARD-0325`) is unique. `None` still walks to "everywhere" and 409s on a collision, which is the safe default. Threading `HttpContext` / `cwd` / `boardId` onto `POST /api/schedules` would be CARD-0218's full fence, and `PutRoutingPinRequest.Card` already resolves with `None`. Out of scope. An operator hitting a 409 passes the guid (or we add `-Board` later).

### D4. List filter uses the same resolver

`GET /api/schedules?cardId=` is `Guid?` today, so `?cardId=CARD-0325` is a 400 from model binding, and the CLI's guid-regex gate hides that by sending nothing. Change the query parameter to `string?`, resolve, then call `ListAsync` with the guid. A missing identifier is 422, not "here is every schedule."

`ListAsync`'s `Guid? cardId` stays a guid — resolution belongs in the endpoint (or a one-line resolve-then-call in the service). Pick endpoint resolve so `ListAsync` does not grow a `CardService` dependency it does not otherwise need.

### D5. Inject `CardService` into `ScheduleService`. Do not hang resolve off `IScheduledCardActions`.

`IScheduledCardActions` is the fire-path (move / release / spawn). `ScheduleCardActionTests.RecordingScheduledCardActions` fakes it. Identifier resolution is a create/preview concern and needs the real `Cards` table. `World` already registers `CardService` even when the fire-path fake is in place (`ScheduleCardActionTests.cs:330-334`). Add a constructor argument; no new test-harness graph.

The two existing `CreateScheduleRequest(..., CardId: card.Id, ...)` call sites become `card.Id.ToString()`. `ScheduleDto.CardId` stays `Guid?` — that is the stored FK.

---

## Slice (one)

**S1 — bind, resolve, list, CLI list, docs, tests (~2–3 h)**

1. `server/Application/Dtos/ScheduleDtos.cs` — `Guid? CardId` → `string? CardId` on `CreateScheduleRequest` only.
2. `ScheduleService` — inject `CardService`; replace the `is not Guid` check in `ApplyCardFieldsAsync` with the helper in D2; keep the subsequent `Cards` include-query on the resolved guid (preview still needs the card graph).
3. `ScheduleEndpoints` — `MapGet("/"` `Guid? cardId` → `string? cardId`; when set, resolve then `ListAsync`.
4. `scripts/schedule.ps1`
   - Header: `-Card` takes `CARD-nnnn` / `card-51` / `#51` / `51` / guid, same sentence as `card.ps1`.
   - `list`: drop the guid-regex gate; `cardId={0}` with `[uri]::EscapeDataString($Card.Trim())`.
   - Error path: print `candidates` the way `card.ps1` does, so a 409 is readable.
   - `preview` / `new`: no change to the body. They already forward `-Card`.
5. Docs, one sentence each: `docs/antiphon-api.md` Schedules block (`cardId` on create/preview/list is identifier-or-guid, resolved by `ResolveCardIdAsync`); `scripts/schedule.ps1` header as above. No `board-api.md` change (that bundle is card-routes only).
6. Tests — see matrix. Compile-fix the two `CardId: card.Id` sites.

No migration. No client change. No fire-path change.

---

## Test matrix

Pin the **binding** at HTTP, not only the service. A service-level `CreateScheduleRequest(CardId: "CARD-9326")` would have been green on today's `Guid?` only because C# never went through JSON.

Add to `ScheduleEndpointsTests` (existing WAF, isolated schema):

- Seed a card whose identifier is **globally unused** (`CARD-{4000..9999}` the way `CardIdentifierResolutionTests.NextUnusedIdentifierAsync` does — never `CARD-0001`).
- `POST /api/schedules/preview` with `cardId` equal to each of: the canonical identifier, `card-N`, `#N`, `N`, the guid. Each is 200 and `target.cardId` is the seeded guid. **This is the CARD-0325 canary.**
- `POST /api/schedules/preview` with `cardId: "not-a-card"` → 422, errors key `CardId`.
- `POST /api/schedules/preview` with an unknown guid string → 422 (not 400, not 404).
- Existing `start_spawn_without_accept_spend_is_422_with_the_preview` still passes when `cardId` is a guid (anonymous object already serialises guid as a JSON string).

`ScheduleCardActionTests`: `CardId: card.Id.ToString()` at the two `CreateScheduleRequest` sites. No new fire-path cases.

Optional, same class as the HTTP tests: `GET /api/schedules?cardId=CARD-N` returns only that card's schedules after one is created.

No new `ScheduleCliE2ETests` case required. The CLI already forwards the string; the failure was binding. The HTTP preview test is the lock.

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0326/ --treenode-filter "/*/*/ScheduleEndpointsTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0326/ --treenode-filter "/*/*/ScheduleCardActionTests/*"
```

Delete the `bin-card0326` directories afterwards. Do not run the Application namespace.

---

## What this card does not do

- Client-side identifier→guid lookup in `schedule.ps1`.
- `-Board` / `cwd` / token-scoped resolution on schedule create (CARD-0218 fence; 409 is enough).
- Changing `ScheduleDto.CardId` or the `Schedules.CardId` column.
- The `list -Agent` guid-regex gate (same shape, different resource, not this bug).
- Renaming the JSON property to `card` to match `CreateAgentTaskRequest`.
- A JsonConverter on `Guid?`.
- Teaching the web Schedules tab to type `CARD-nnnn` (it already sends guids as strings).

---

## Execute notes

Shared workspace. ASCII-only in `schedule.ps1` (Windows PowerShell 5.1). Do not add `CardService` to `IScheduledCardActions`. Do not widen timeouts or loosen the spend-ack test while compiling the `CardId` type change.
