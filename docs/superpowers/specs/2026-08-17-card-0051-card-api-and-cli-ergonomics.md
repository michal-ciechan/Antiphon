# CARD-0051 — Card API + CLI ergonomics: single-card GET, identifier addressing, discoverable limits, opt-in spawn, `scripts/card.ps1`

**Date:** 2026-08-17 · **Card:** CARD-0051 · **Status:** planned, not implemented
**Author:** Plan task 7f779bfa (planning only — no code touched)

## Why (verified, not taken on faith)

Every claim on the card was checked against shipped code:

1. **No single-card GET.** `CardEndpoints.cs` maps PATCH `/{id:guid}`, PATCH `/{id:guid}/content`,
   GET `/{id:guid}/revisions`, POST spawn/archive/unarchive/comments/pr, GET `/{id:guid}/diff` —
   and no `GET /{id}`. The service method already exists and is public:
   `CardService.GetByIdAsync` (`server/Application/Services/CardService.cs:121`) — it is called
   internally by every write to build the response, and simply never mapped. Reading one card today
   means `GET /api/boards/{boardId}` (measured 252 KB for the current Antiphon board).
2. **GUID-only addressing.** All nine card routes constrain `{id:guid}`. The precedent and its
   rationale are in `server/Api/Endpoints/AgentTaskEndpoints.cs:39` — `{id}` is a string, resolved
   by `AgentTaskService.ResolveTaskIdAsync` (`AgentTaskService.cs:539`) with 0→404, 1→id,
   many→409 semantics. `client/src/shared/cardIdentifier.ts` parses `#51`, `51`, `CARD-0051`,
   `card-51` — client-side only.
3. **Token rotates on every write.** `ConcurrencyToken = Guid.NewGuid()` in `UpdateContentAsync`
   (`CardService.cs:200`), `ApplyColumnMove` (`:538`), `SaveArchiveChangeAsync` (`:300`). A fresh
   read immediately before every write is mandatory. Independent backstop: the
   `(CardId, RevisionNumber)` unique index turns truly concurrent writers into a 409 regardless
   (`SaveCardWriteAsync` remark, `CardService.cs:443-472`).
4. **Limits live only in source.** `CardService` constants: Title 300, Description 20 000,
   Reason 4 000, Actor 200 (`CardService.cs:15-44`). Since CARD-0019 an over-limit value is a 422
   naming limit and actual length (`RequireWithinLimit`, `:652`) — but a caller cannot ask
   *before* composing.
5. **No card CLI.** `scripts/` has `delegate.ps1` and ten ops scripts; nothing for cards.
6. **A move silently spawns.** `MoveAsync` calls `SpawnAsync` whenever the target column
   `IsActive` and `OwnerSessionId is null` (`CardService.cs:161-162`), discards the
   `SpawnCardResult`, and returns a plain `CardDto`. Nothing in the response says a session
   was started.

Identifier scope fact that shapes D1: `Identifier` is unique **per board**
(`IX_Cards_BoardId_Identifier`, `AppDbContext.cs:803`), not globally. Today all 63 cards in the
dev DB sit on the one Antiphon board (verified in Postgres: zero duplicate identifiers across 47
boards), but the day any other board files its first card it gets `CARD-0001` again — ambiguity
handling is required, not theoretical.

## Decisions

**D1 — `GET /api/cards/{id}`, accepting GUID or identifier.** New
`CardService.ResolveCardIdAsync(string idOrIdentifier, CancellationToken)` modeled directly on
`ResolveTaskIdAsync`: GUID parses → existence check → return. Otherwise normalize exactly the
forms `cardIdentifier.ts` accepts — trim, strip leading `#`, strip `card-`/`card ` prefix
(case-insensitive), strip leading zeros; all-digits remainder `n` → canonical `$"CARD-{n:0000}"`.
Match by canonical form OR exact case-insensitive raw match (so foreign-tracker identifiers like
`PROJ-12` resolve too). `Take(2)`: 0 → `NotFoundException` (404), 1 → id, 2 → `ConflictException`
(409, "matches cards on more than one board — use the GUID"). Non-GUID, non-identifier-shaped
input → `ValidationException` (422). Match is **exact**, not prefix — the client's prefix match
is for incremental typing in a search box; an API taking `5` to mean "CARD-0005 or maybe 0051"
would be a footgun.

**D2 — identifier addressing on every card route, not a resolve endpoint.** All nine routes in
`CardEndpoints.cs` change `{id:guid}` → `{id}` and call the resolver first. A separate
resolve-then-act step adds the round trip that is friction #1 in the first place, and the tasks
comment already argues the general form: the id a caller *sees* must be the id the API *takes*.
Cost: the `:guid` route constraint's automatic 404-on-garbage becomes the resolver's 422 —
equivalent protection, better message. `POST /api/boards/{id}/cards` (create) keeps `:guid`;
boards are not the problem.

**D3 — limits: an endpoint AND the existing self-describing 422s.** New `GET /api/cards/limits`
returning `{ maxTitleLength, maxDescriptionLength, maxReasonLength, maxActorLength }` straight
from the `CardService` constants. The 422 side is already done (CARD-0019 — messages carry limit
and actual length); no new problem-details extension is needed. Route note: literal segments
outrank route parameters in ASP.NET routing, so `/limits` never collides with `/{id}` — but the
resolver's 422 arm covers it anyway if registration order ever changes.

**D4 — a move that would spawn requires explicit opt-in, and the response says what happened.**
`MoveCardRequest` (`BoardDtos.cs:111`) gains `bool Spawn = false`. `MoveAsync` only calls
`SpawnAsync` when `request.Spawn` is true; the response becomes
`MoveCardResult(CardDto Card, Guid? SpawnedSessionId, bool SpawnSuppressed)` — `SpawnedSessionId`
from the `SpawnCardResult` currently discarded, `SpawnSuppressed` true when the target was active,
unowned, and `Spawn` was false (so a script learns it moved a card into an active column without
starting work, rather than discovering a dead session later — or nothing at all). The UI drag
passes `spawn: true`, preserving today's human UX unchanged; whether the UI should *confirm* first
is CARD-0040's scope, not this card's. Default-off is the right polarity: the measured accidents
were scripted PATCHes and the API's job is to make bookkeeping safe by default.

**D5 — the CLI defaults to read-then-write, with `-Token` for strict CAS, and says so.**
`card.ps1` fetches the card immediately before every write and uses that token (window:
milliseconds, vs. the seconds-to-minutes of the manual flow the token was rotting in). This can
in principle clobber an edit that landed inside that window — accepted, because (a) the
`(CardId, RevisionNumber)` unique index still 409s truly concurrent writers at the DB, and
(b) every content write is revision-logged, so a clobber is recoverable from history, which is
exactly what CARD-0019 built. For callers that composed an edit from an earlier read and want
true compare-and-swap, `-Token <guid>` passes that token verbatim and a stale one is the server's
409. The script's comment block states this tradeoff explicitly.

## Slices

Each slice is independently landable and testable. **No slice adds a migration or touches
`Program.cs`** — deliberate, see Collisions.

### Slice 1 — read a card the way everyone names it

*Files:*
- `server/Application/Services/CardService.cs` — add `ResolveCardIdAsync` (per D1; EF query:
  `c.Identifier == canonical || c.Identifier.ToLower() == raw.ToLower()`, `Select(c => c.Id).Take(2)`).
- `server/Api/Endpoints/CardEndpoints.cs` — map `GET /api/cards/{id}` →
  `ResolveCardIdAsync` + existing `GetByIdAsync`; change all nine existing routes
  `{id:guid}` → `{id}` + resolve.
- `server/Api/Endpoints/BoardEndpoints.cs` — add `GET /api/boards/{id:guid}/columns` returning
  the board's `BoardColumnDto`s **without cards** (`BoardColumnDto` already carries `Name`,
  `CardStatus`, `IsActive`, `IsTerminal` — `BoardDtos.cs:29-38`). This is what lets slice 4's
  `move -To <column name>` and `close` avoid the 252 KB board fetch.

*Tests:* new `tests/Antiphon.Tests/Application/CardIdentifierResolutionTests.cs` (service level,
`TestDbFixture` — scope every assertion to rows the test created, per the shared-Postgres rule):
GUID pass-through; `CARD-0051` / `#51` / `51` / `card-51` / `Card-0051` / `CARD-51` all resolve;
foreign-form exact match; unknown → 404; same identifier on two boards → 409; junk → 422.
Plus HTTP-level cases in `CardCorrectionApiTests.cs` (the wiring is what only full-stack catches):
`GET /api/cards/CARD-nnnn` returns the card; `PATCH /api/cards/CARD-nnnn/content` works;
`GET /api/boards/{id}/columns` returns columns and no cards.

### Slice 2 — ask what the limits are

*Files:*
- `server/Application/Dtos/BoardDtos.cs` — `CardLimitsDto(int MaxTitleLength,
  int MaxDescriptionLength, int MaxReasonLength, int MaxActorLength)`.
- `server/Api/Endpoints/CardEndpoints.cs` — `GET /api/cards/limits` (registered before `/{id}`),
  returning the DTO built from the `CardService` constants.

*Tests:* `CardCorrectionApiTests.cs` — response fields assert **equality against the constants
themselves** (`CardService.MaxTitleLength` etc.), so the endpoint cannot drift from the
enforcement; and `GET /api/cards/limits` must NOT be swallowed by the `/{id}` route (a regression
here would come back as a 422 "not an identifier").

### Slice 3 — a move that starts work says so, and only when asked

*Files:*
- `server/Application/Dtos/BoardDtos.cs` — `MoveCardRequest` gains `bool Spawn = false`; new
  `MoveCardResult(CardDto Card, Guid? SpawnedSessionId, bool SpawnSuppressed)`.
- `server/Application/Services/CardService.cs` — `MoveAsync` returns `MoveCardResult`; the
  auto-spawn at `:161-162` becomes `if (targetColumn.IsActive && card.OwnerSessionId is null)`
  → spawn only when `request.Spawn`, else set `SpawnSuppressed`.
- `server/Api/Endpoints/CardEndpoints.cs` — PATCH `/{id}` returns the new shape.
- `client/src/api/boards.ts` — `MoveCardRequest` interface (`:157`) gains `spawn`; `useMoveCard`
  (`:363-367`) retypes to `MoveCardResult` and unwraps `.card` where the `CardDto` was used.
- The drag call site (BoardPage / wherever `useMoveCard().mutate` fires on drop) passes
  `spawn: true`.

*Tests:* server — new cases beside the existing move coverage: move-to-active without `Spawn`
claims no session (`AgentSessions` scoped to the test's card stays empty) and reports
`SpawnSuppressed`; with `Spawn: true` reports the `SpawnedSessionId`. Client —
`BoardPage.test.tsx:291` MSW handler updated to the new response shape; assert the drag mutation
sends `spawn: true`. E2E —
`BoardE2ETests.Board_user_can_drag_backlog_card_to_in_progress_and_open_terminal_session` must
keep passing; **rebuild `client/dist` (`npm run build`) before trusting it** (the fixture
hard-fails on a stale bundle, but the reminder belongs in the slice).

*Compat:* server and client land in the same commit — an old client against the new server would
silently stop spawning on drag. Out-of-repo callers (the scratchpad scripts this card is about)
must now opt in; that is the intended behavior change and the response tells them.

### Slice 4 — `scripts/card.ps1`

Mirrors `delegate.ps1` exactly in its mechanics: ASCII-only (PowerShell 5.1 CP1252 rule),
`[CmdletBinding]` parameter sets, `$env:ANTIPHON_API` defaulting to `http://localhost:17202`,
`X-Antiphon-Task-Token` header when present, and the same `Invoke-Antiphon` error pattern that
surfaces the server's own problem-details message.

Verbs (first positional argument selects the parameter set):

```
card.ps1 get       CARD-0051 [-Json]
card.ps1 history   CARD-0051 [-Json]
card.ps1 new       -Board <name|guid> -Title <t> [-DescriptionFile p | -Description s]
                   [-Priority n] [-Labels a,b]
card.ps1 edit      CARD-0051 -Reason <r> [-Title t] [-DescriptionFile p | -Description s]
                   [-Priority n] [-Labels a,b] [-By name] [-Token g]
card.ps1 move      CARD-0051 -To <column name|guid> [-Reason r | -ReasonFile p] [-Spawn] [-Token g]
card.ps1 close     CARD-0051 -Reason r | -ReasonFile p        # move to the board's first
                                                              # IsTerminal column, by ColumnOrder
card.ps1 archive   CARD-0051 -Reason r [-By name]
card.ps1 unarchive CARD-0051 -Reason r [-By name]
card.ps1 -Limits
```

- **Long text only ever comes from a file** (`-DescriptionFile` / `-ReasonFile`,
  `Get-Content -Raw`): nothing has to survive shell quoting — the backtick/dollar mangling that
  produced fifteen throwaway Python scripts is designed out, not worked around.
- Before sending, lengths are pre-checked against `GET /api/cards/limits` (slice 2) so an
  over-long reason fails locally, deterministically, naming the ceiling.
- `move -To <name>` and `close` resolve the column via `GET /api/boards/{boardId}/columns`
  (slice 1), case-insensitive on `Name`; the card's `boardId` comes from the `get` that fetches
  the token anyway — two small requests total, no board-sized fetch.
- `move` without `-Spawn` prints a clear line when the response says `spawnSuppressed`
  ("moved to an active column; NO agent was started — re-run with -Spawn to start one").
- `-Board` on `new` resolves name→guid via the lightweight `GET /api/boards` list.
- Token behavior per D5, documented in the script's comment block.

*Tests:* the semantics all live behind the API and are covered by slices 1–3; what needs pinning
is the script's own plumbing (file reading, column-name resolution, error surfacing). Precedent:
`delegate.ps1` has no direct harness — it is exercised only through the delegation E2E suite.
Proposed: `tests/Antiphon.E2E/CardCliE2ETests.cs` invoking
`pwsh -NoProfile -File scripts/card.ps1` against the fixture's real Kestrel URL (no browser, no
agent session — cheap): `new` → `get` by identifier → `edit -DescriptionFile` with a body full of
backticks/`$(...)`/newlines round-trips byte-identical → `move` to a non-active column →
`-Limits` prints four numbers. See "Could not determine" for the one open question here.

### Slice 5 — paper trail

`AGENTS.md` (or wherever `delegate.ps1` is introduced to agents) gains the `card.ps1` synopsis,
and the CARD-0051 close-out cites this spec. Small, lands with or after slice 4.

## Collisions with in-flight work (checked 2026-08-17)

- **Task 0581d8aa — CARD-0035 slice 1** builds new `AttentionService/AttentionDtos/
  AttentionEndpoints` + `GET /api/attention`, registered in `Program.cs`. This plan touches no
  new `Program.cs` lines (all mapping happens inside the existing `MapCardEndpoints` /
  `MapBoardEndpoints` bodies) and none of their new files. No collision beyond ordinary
  same-file-different-region merges if they also edit `BoardDtos.cs` (nothing indicates they do).
- **Task ea26d91a — CARD-0058 slices 1+2** (instruction bundles: catalog, composer, launch
  composition) touches `AgentControlService` and adds new entities — i.e. **migrations**. Its spec
  (`2026-08-16-card-0058-0059-0060-instruction-bundles.md`) never mentions `CardEndpoints`,
  `CardService`, or `BoardDtos`. This plan deliberately needs **zero schema change and zero
  migration**, so migration-ordering conflicts cannot arise. `card.ps1` is a new file; no overlap
  with `delegate.ps1`.
- Both tasks were `Dispatched` at planning time; re-check `git log` on `server/Application/Dtos/`
  and `Program.cs` before starting slices 1–3.

## Could not determine

1. **Whether any out-of-repo caller depends on the move response being a bare `CardDto`.**
   In-repo, the only HTTP callers of `PATCH /api/cards/{id}` are `client/src/api/boards.ts` and
   test MSW handlers (grepped). The scratchpad Python scripts are ephemeral by nature. If some
   external tool (MCP server, Windmill job) reads the move response as a `CardDto`, slice 3's
   response-shape change breaks it — nothing in the repo suggests one exists, but absence of
   evidence is all this is.
2. **Whether `Antiphon.E2E` can invoke `pwsh` against its fixture cheaply.** The fixture exposes a
   real Kestrel URL and `AgentTuiSmokeScriptTests` proves TUnit→pwsh invocation works, but no
   existing E2E test runs a repo script without a Claude session in the loop. If it turns out
   heavy, fallback: a `[NotInParallel]` test in `Antiphon.Tests` that binds the API host to a real
   loopback port for the duration (Kestrel via `WebApplicationFactory.UseKestrel` is not available
   on the current factory — would need a small helper), or accept manual smoke + slice 1–3
   coverage.
3. **Whether the UI drag should confirm before spawning.** Deliberately left with CARD-0040;
   slice 3 keeps human UX bit-identical (`spawn: true` on drag).
4. **`AgentTaskEndpoints`' own non-GET routes still take `:guid` only** (`cancel`/`retry`/
   `escalate`, `AgentTaskEndpoints.cs:50-64`) — the same D2 argument applies there. Out of scope
   for this card; worth its own small card if the friction is ever felt.
