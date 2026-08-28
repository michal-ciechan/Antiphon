# CARD-0218 — A card is addressed the way it is named, even when two boards name one alike

**Date:** 2026-08-28
**Status:** planned (design only — nothing here is implemented)
**Card:** CARD-0218 (`a3bba599-1987-4df6-a683-9a13b12a41ae`), board Antiphon
**Scope:** one build slice. A shared scope walk on the server, one optional argument on
`ResolveCardIdAsync`, a 409 that lists its candidates, two query parameters read by the card
routes, and `card.ps1` sending what it already knows. No schema change, no migration.
**Model followed:** `docs/superpowers/plans/2026-08-28-card-0212-remote-control-capability-gate-plan.md`
(shape only).

## Verdict, in one screen

| Fact (verified 2026-08-28 at `19937d5`, live API on 17202) | Consequence for the design |
|---|---|
| `Identifier` is unique per board by index (`IX_Cards_BoardId_Identifier`, `AppDbContext.cs:887`) and allocated per board as highest-seen + 1 (`CardIdentifierAllocator.ForBoardAsync`). `CardService.ResolveCardIdAsync` (`CardService.cs:219-270`) searches **every board** and turns 2+ matches into `409 conflict "matches more than one card — use the card's guid"` with no other information. | The resolver has no scope to narrow by. Everything else on the server that resolves an identifier already narrows — see the next row — so the fix is to give this resolver the same scope, not to invent a new one. |
| `AgentTaskCardBinder.ResolveAsync` (`AgentTaskCardBinder.cs:160-193`, CARD-0040) already solves this exact problem for `delegate.ps1 -Card`: caller boards (the task's/session's card's board, the standing agent's board) → repository boards (projects whose `LocalRepositoryPath` contains the working directory, via `DelegationWorkspaceResolver.IsWithinRoot`) → everywhere, uniqueness demanded inside the scope that answers. It is static over `AppDbContext`. | **D1: one scope walk, two callers.** Lift `CallerBoardsAsync` / `RepositoryBoardsAsync` / `MatchesAsync` into a static `CardIdentifierScope` used by both the binder and `ResolveCardIdAsync`. The binder's doc already states the reason: "two spellings of *which card is CARD-51* would eventually disagree." |
| Live collision today: 47 boards, **two** hold cards — Antiphon (228) and Gym Stat (21) — so `CARD-0001`…`CARD-0021` are all ambiguous right now. Every one of the 45 empty boards was made by project setup (CARD-0032) or a card-task worktree, and each will collide from its first card. Three projects point at `C:\src\Antiphon` (`Antiphon`, `antiphon`, `Antiphon (2)`), one at `C:/src/gym-stat`. | Repository scope alone resolves both live collisions: from a checkout under `C:\src\Antiphon` the three projects' boards contain exactly one `CARD-0011`; from `C:/src/gym-stat`, exactly one. Nothing needs renumbering. |
| `card.ps1` builds only two routes from the raw `$Card` (`scripts/card.ps1:184` `GET /api/cards/{card}`, `:251` `GET /api/cards/{card}/revisions`); every write goes to `$theCard.id`, the guid it just read. It already sends `X-Antiphon-Task-Token` when `ANTIPHON_TASK_TOKEN` is set (`:113`) and already has a `-Board` parameter, used only by `new` (`:55-58`). | **D2: the script changes are two route sites and one parameter widening.** `-Board` applies to every verb; the checkout root goes along as `cwd`; the token is already there. Writes need nothing. |
| No other script resolves an identifier: `delegate.ps1 -Card` is resolved server-side by the binder (already scoped); `checkpoint-task.ps1:161` only regex-extracts `CARD-\d+` from a title for a commit subject; `project.ps1` never touches `/api/cards`; `prune-test-data.ps1:210` reads by guid. The client (`client/src/api/boards.ts`) addresses cards by guid only; `cardIdentifier.ts` is a prefix search over one board's already-loaded cards. | Consistency is one server resolver + one script. There is no second copy of the pattern to keep in step. |
| Today's 409 as the human sees it: `card.ps1` prints the raw problem-details JSON (`\u2014`, `\u0027` unescaped), carries no board names, no guids, no titles. The scripted caller gets `code: "conflict"`, indistinguishable from a stale concurrency token. | **D3: (c) is built too, because (a) cannot cover a caller with no context** — a human in `C:\Users\lndco`, a Windmill job, a delegate whose task is bound to no card. The 409 becomes `code: card_identifier_ambiguous` with a `candidates` extension (board, guid, title, status) and a message that says the same, and `card.ps1` prints `detail` plus one line per candidate instead of JSON. |
| Option (b), global uniqueness, would have to renumber Gym Stat's 21 cards (or every future board's), and identifiers are cited outside the database — commit subjects, docs, other cards' reasons, GitHub sync labels (CARD-0175). A per-board prefix (`GYM-0011`) is how trackers actually solve this, but `CARD-` is hard-coded in the allocator, `AgentTaskCardBinder.TitleIdentifier`, `TryCanonicalIdentifier`, `WorktreeManager.ValidateCardId`, `cardIdentifier.ts`, `checkpoint-task.ps1`, `prune-test-data.ps1`'s name patterns and the `#N is CARD-000N and nothing else` rule (CARD-0170/0175). | **(b) rejected for this card**: a migration plus ~8 hard-coded sites to buy something (a)+(c) already deliver for the callers that exist. Recorded in §5 as a decision the operator can overturn; if wanted it is its own card and it does not replace (a)+(c), because a scoped resolver is still the right shape once prefixes exist (a prefix is just an explicit board). |

## 1. What exists today (verified 2026-08-28)

### 1.1 The resolver and its fifteen callers

- `CardService.ResolveCardIdAsync(string idOrIdentifier, CancellationToken ct)` — guid → existence
  check; else `TryCanonicalIdentifier` (`CARD-0051` / `card-51` / `#51` / `51` → `CARD-0051`) or the
  foreign `PREFIX-123` shape (422 otherwise); then one query over **all** `Cards` by `Identifier`
  (plus `ExternalIssueRef.ExternalKey` for the foreign shape), `Take(2)`: 0 → 404, 1 → id, 2 →
  `ConflictException` with the default `conflict` code.
- Every `/api/cards/{id}…` route (15 of them, `CardEndpoints.cs:53-203`) calls it with the raw
  route segment and nothing else. `GET /api/cards?boardId=` already exists (`:37-50`) — the query
  name `boardId` is established.
- Tests: `CardIdentifierResolutionTests` (10 tests) pins every form, the 422, the 404, and
  `The_same_identifier_on_two_boards_is_a_409_naming_the_way_out` — which asserts only that the
  message contains the identifier and the word "guid".

### 1.2 The binder's scope walk (what gets shared)

`AgentTaskCardBinder` (`server/Application/Services/AgentTaskCardBinder.cs`):

| Step | Source | Rule |
|---|---|---|
| Scope A, caller boards (`:212-245`) | inherited card's board; calling session's card's board; the standing agent whose `PersistentSessionId` is the calling session | unique → bind; otherwise **fall through** (A is a hint, not a fence) |
| Scope B, repository boards (`:252-275`) | every project whose `LocalRepositoryPath` contains `RepoPath ?? WorkingDirectory` (`IsWithinRoot`, separator- and case-insensitive) → its boards | unique → bind; 2+ → ambiguous; 0 → fall through |
| Everywhere (`:181-189`) | all boards | unique → bind; 0 → "matches no card"; 2+ → ambiguous |

`Ambiguous` (`:191-195`) already names the boards ("exists on 2 boards (Antiphon, Gym Stat); pass
-Card with the card's guid") but not the guids. `AgentTaskCardBindingTests` (14) pins the walk.

### 1.3 Who the caller is

`AgentTaskService.AuthenticateAsync` (`AgentTaskService.cs:70-95`) turns `X-Antiphon-Task-Token`
into `Caller(Task, SessionId, WorkingDirectory)` — a task token yields the task (with `CardId`,
`AgentSessionId`, `WorktreePath ?? WorkingDirectory`), a session token yields the standing
session and its `Cwd`. `AgentTaskEndpoints.ResolvePollingCallerAsync` (`:160-170`, `internal
static`) is the tolerant form: a missing or stale token is `null`, never a 403 — the shape a
card read must use, because a human's shell with a stale `ANTIPHON_TASK_TOKEN` in it must still
be able to `card.ps1 get`.

### 1.4 `card.ps1`

- `-Board` (`:55-58`): "Only 'new' needs it — every other verb finds the board from the card."
  Name (case-insensitive) or guid; name resolution with a not-found and an ambiguous-name error
  lives inline in the `new` arm (`:273-290`).
- `Get-CardOrFail` (`:178-185`) and `history` (`:251`) are the two places the raw `$Card` reaches
  a URL. Everything after a read uses `$theCard.id`.
- `Invoke-Antiphon`'s catch (`:130-137`) prints `$_.ErrorDetails.Message` — the whole JSON body.
- `CardCliE2ETests` (4 tests, `tests/Antiphon.E2E/CardCliE2ETests.cs`) runs the real script against
  the E2E fixture's Kestrel and Postgres; it is where "the server's message survives to the
  caller" is pinned.

### 1.5 The problem-details vocabulary

`HttpException` carries `Code` and an optional `Extensions` dictionary merged into the RFC 9457
document (`HttpException.cs:15`, `ExceptionMiddleware.cs:64-70`); CARD-0136's `quota` extension on
`subscription_quota_low` is the precedent. `docs/antiphon-api.md` §"Card identifiers" (`:75-80`)
documents the forms and says nothing about ambiguity.

## 2. The three options, compared

| | (a) scope to a board context | (b) globally unique identifiers | (c) a 409 that lists its candidates |
|---|---|---|---|
| Fixes the scripted caller (a delegate moving "its" card) | **Yes** — the token and the checkout already identify the board | Yes | **No** — a script cannot act on a list |
| Fixes the interactive caller | Yes when in a checkout or holding `-Board`; otherwise falls to (c) | Yes | Yes, at the cost of one retry with the guid or `-Board` |
| Data migration | None | Renumber every colliding card on younger boards, or introduce a prefix — and every external citation of a renumbered card goes stale | None |
| Code surface | `CardIdentifierScope` (lifted, not written), one optional parameter, 15 one-line call sites through a helper, two `card.ps1` route sites | Allocator, index, `TryCanonicalIdentifier`, binder regex, `ValidateCardId`, client parser, two scripts' regexes, the CARD-0175 rule, tracker sync labels | Message + extension in one `switch` arm, a formatter shared with the binder, `card.ps1`'s catch |
| Consistency with what exists | Same walk `delegate.ps1 -Card` has used since CARD-0040 | Contradicts the per-board index the binder and its tests are built on | Same problem-details shape as CARD-0136 |
| Risk of addressing the wrong card | The walk demands uniqueness inside the scope that answers; a wrong scope can only produce a 404 or a 409, never a silent other card | None | None (it refuses) |

**Decision: (a) and (c) together, one slice.** (c) alone is insufficient for the reason the brief
gives — `card.ps1` is a delegate's write path, and a delegate that gets a list instead of a move
has to fall back to a guid it may not have. (a) alone leaves the context-free caller with today's
message. Together, the scripted caller never sees the 409 in practice and the human who does sees
everything needed to answer it in the same round trip.

## 3. Design

### 3.1 `CardIdentifierScope` — the walk, lifted

New static class `server/Application/Services/CardIdentifierScope.cs`, code moved (not rewritten)
from `AgentTaskCardBinder`:

```csharp
internal sealed record CardScopeContext(
    Guid? ExplicitBoardId,      // ?boardId= — a fence, not a hint
    Guid? InheritedCardId,      // binder: parent/follow-up card; card API: the caller task's own CardId
    Guid? CallerSessionId,      // from the token
    string? Directory);         // ?cwd=, else the caller's WorktreePath ?? WorkingDirectory ?? Cwd

internal sealed record CardMatch(Guid Id, string Identifier, string Title, CardStatus Status,
                                 Guid BoardId, string BoardName);

internal sealed record CardScopeResult(CardMatch? Match, IReadOnlyList<CardMatch> Candidates,
                                       string ScopeName);   // "board", "caller", "repository", "all"

static Task<CardScopeResult> ResolveAsync(AppDbContext db, string canonical, CardScopeContext ctx, CancellationToken ct);
static string DescribeCandidates(string canonical, IReadOnlyList<CardMatch> candidates);
```

`ResolveAsync` keeps the binder's semantics byte for byte for scopes A/B/everywhere (unique →
match; A ambiguous → fall through; B or everywhere ambiguous → `Candidates`, `Match = null`; 0 →
next / empty). One new step in front:

- **Explicit board** (`ExplicitBoardId` set): search that board only. 1 → match. 0 → `Candidates`
  = the matches everywhere else (so the 404 can say "not on Gym Stat; Antiphon has it"), `Match =
  null`, `ScopeName = "board"`. 2 is impossible by index. **Never falls through** — a caller who
  named the board is not guessing.

`CardMatch` grows `Title`, `Status`, `BoardId` over the binder's private record; the join already
touches `Boards` so the cost is two projected columns. `DescribeCandidates` produces the one
sentence both callers print (§3.3).

`AgentTaskCardBinder.ResolveAsync` becomes a call into `CardIdentifierScope.ResolveAsync` with
`ExplicitBoardId = null` and `Directory = RepoPath ?? WorkingDirectory`; `Ambiguous` uses
`DescribeCandidates`. Its 14 tests must stay green with one assertion widened (§4).

### 3.2 `ResolveCardIdAsync` gains a scope

```csharp
public Task<Guid> ResolveCardIdAsync(string idOrIdentifier, CancellationToken ct)
    => ResolveCardIdAsync(idOrIdentifier, CardScopeContext.None, ct);   // existing callers untouched

public async Task<Guid> ResolveCardIdAsync(string idOrIdentifier, CardScopeContext scope, CancellationToken ct)
```

| Input | Behaviour |
|---|---|
| guid | As today: existence check, scope **ignored**. A guid is exact; `-Board` beside a guid is not a contradiction worth refusing. |
| our identifier (`CARD-0051` and friends) | `CardIdentifierScope.ResolveAsync`. Match → id. No match, no candidates → 404 (as today). No match, candidates, `ScopeName == "board"` → **404** `"No CARD-0011 on board 'Gym Stat'. It exists on: Antiphon (a3bba599-…)."`. No match, candidates otherwise → **409** (§3.3). |
| foreign key (`ANT-12`) | As today, plus the explicit-board fence when set. Foreign keys are unique per tracker and the union query stays; scope A/B are not applied (an `ExternalKey` match is already exact). |

### 3.3 The 409, made answerable

```
409  code: card_identifier_ambiguous
detail: Card identifier 'CARD-0011' matches 2 cards:
  Antiphon  a3bba599-1987-4df6-a683-9a13b12a41ae  InProgress  "card.ps1 identifier lookup is global…"
  Gym Stat  0f2c…                                Backlog     "Import workouts from CSV"
Pass -Board <name|guid>, use the card's guid, or run from the project's checkout.
extensions.candidates: [{ id, identifier, title, status, boardId, boardName }, …]
```

Titles are truncated at 60 characters in the message (the extension carries them whole). The
message is `DescribeCandidates` verbatim so the binder's `Warning` event and the card API say the
same thing. Ordering: board name, then identifier. The `code` is new and specific so a script can
tell it from a stale concurrency token without parsing prose — the CARD-0136 rule.

### 3.4 Endpoint plumbing — one helper, fifteen one-line edits

In `CardEndpoints`:

```csharp
private static async Task<Guid> ResolveAsync(
    HttpContext http, string id, CardService cards, AgentTaskService tasks, CancellationToken ct)
{
    var query = http.Request.Query;
    Guid? boardId = Guid.TryParse(query["boardId"], out var b) ? b : null;   // non-guid → 422 naming the parameter
    var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, tasks, ct);   // tolerant: stale token ⇒ null
    var scope = new CardScopeContext(
        boardId,
        caller?.Task?.CardId,
        caller?.SessionId,
        query["cwd"].FirstOrDefault() is { Length: > 0 } cwd ? cwd : caller?.WorkingDirectory);
    return await cards.ResolveCardIdAsync(id, scope, ct);
}
```

Every `service.ResolveCardIdAsync(id, cancellationToken)` in the file becomes
`ResolveAsync(http, id, service, tasks, cancellationToken)`; each lambda gains `HttpContext http,
AgentTaskService tasks` parameters. `ResolvePollingCallerAsync` is already `internal static` in
the same assembly.

Two facts about `cwd` that the code comment must carry: it is a **disambiguation hint, never an
authorisation** — the only thing it can change is *which* of several cards the caller could
already address by guid answers to a short name; and it is read on writes as well as reads
(`PATCH /api/cards/CARD-0011?boardId=…&cwd=…`), because the script's writes are guid-addressed
but a curl-by-hand move should have the same door.

### 3.5 `card.ps1`

1. `-Board` help text: "Board name (case-insensitive) or guid. Required by `new`; on every other
   verb it scopes the identifier when the same number exists on more than one board."
2. Lift the `new` arm's name→guid resolution (`:273-290`) into `Resolve-BoardId` and call it once
   up front when `-Board` is set, so `move CARD-0011 -Board 'Gym Stat'` and `new -Board 'Gym Stat'`
   resolve a name the same way and fail the same way ("names 2 boards — pass the guid").
3. `Get-CardScopeQuery`: builds `?boardId=<guid>&cwd=<escaped>` where `cwd` is
   `git rev-parse --show-toplevel` from `$PWD` (swallow the error outside a repo) else `$PWD`.
   Appended at `:184` and `:251` only.
4. `Invoke-Antiphon`'s catch: parse the body as JSON when it is; print `detail`, then `code`, then
   one line per `candidates` entry (`  <board>  <guid>  <status>  <title>`); fall back to the raw
   body when it is not JSON. This is what makes (c) legible — today's output is the JSON with its
   `\u2014` escapes intact.
5. Header comment: one paragraph under "A card is addressed the way it is NAMED" saying what
   happens when two boards name one alike — the checkout and the delegation token decide, `-Board`
   overrides, and the 409 lists every candidate.

Back-compat both ways: an old script against the new server sends no scope and gets today's
walk-everywhere behaviour plus the better message; the new script against an old server sends
query parameters the old routes ignore.

### 3.6 Docs

- `docs/antiphon-api.md` §"Card identifiers": the walk (explicit `boardId` → caller boards →
  repository boards by `cwd` → everywhere), the 404 on a fenced board, the
  `card_identifier_ambiguous` 409 and its `candidates` extension. The route table gains
  `?boardId=&cwd=` on the `{id}` rows' description line.
- `AGENTS.md` gotcha, under the CARD-0175 `#N` bullet:

  > **`CARD-nnnn` is unique per BOARD, and every card route resolves it through the same scope walk
  > `delegate.ps1 -Card` uses** (CARD-0218): explicit `?boardId=` (`card.ps1 -Board <name|guid>`,
  > now on every verb), else the caller's own card's board and standing agent's board (from
  > `X-Antiphon-Task-Token`), else the boards of every project whose `LocalRepositoryPath` contains
  > `?cwd=` (`card.ps1` sends `git rev-parse --show-toplevel`), else everywhere — uniqueness
  > demanded inside the scope that answers, never a silent first row. A collision that survives all
  > of that is `409 card_identifier_ambiguous` listing every candidate (board, guid, status, title)
  > in `detail` and in a `candidates` extension; `-Board` on a card the board does not hold is a 404
  > naming where it does live. Two boards hold cards today (Antiphon, Gym Stat — `CARD-0001…0021`
  > collide) and every project setup adds a board that will collide from its first card, so never
  > resolve an identifier with a bare global query; go through `CardIdentifierScope`.

- `card.ps1` header (§3.5 item 5).

## 4. Tests

All named tests are new unless marked *(exists)*. Run with the alternate output path and delete
the `bin-card0218` directories afterwards:

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0218/ --treenode-filter "/*/*/CardIdentifierResolutionTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0218/ --treenode-filter "/*/*/AgentTaskCardBindingTests/*"
dotnet run --project tests/Antiphon.E2E   --property:OutputPath=bin-card0218/ --treenode-filter "/*/*/CardCliE2ETests/*"
```

**`CardIdentifierResolutionTests`** (server, shared Postgres — every assertion scoped to the rows
the test seeded, per the AGENTS.md rule):

| Test | Pins |
|---|---|
| `The_same_identifier_on_two_boards_is_a_409_naming_every_candidate` *(exists as `…_naming_the_way_out`; extend)* | message contains both board names, both guids, both titles; `Code == "card_identifier_ambiguous"`; over HTTP the problem document's `candidates` has 2 entries with `id`/`boardName`/`status`/`title` |
| `An_explicit_board_scopes_the_identifier_to_that_board` | boards A and B share N; `boardId = B` → B's card; `boardId = A` → A's |
| `An_explicit_board_that_lacks_the_identifier_is_a_404_that_says_where_it_lives` | `boardId = C` (no such card) → `NotFoundException`, message names board C and lists A/B as holders — red today (today it is a 409) |
| `A_delegates_token_resolves_the_identifier_on_its_own_cards_board` | task with `CardId` on A, header set → A's card, no 409 — HTTP-level, through the token |
| `A_checkout_under_a_projects_repository_resolves_the_identifier_on_that_projects_boards` | project with `LocalRepositoryPath` = a temp dir; `?cwd=` a subdirectory of it (mixed separators, mixed case) → that project's board's card |
| `A_stale_token_does_not_turn_a_card_read_into_a_403` | garbage token header + unambiguous identifier → 200 |
| `A_guid_resolves_regardless_of_scope` | guid on A with `boardId = B` → A's id |
| `The_card_api_and_the_task_binder_answer_the_same_card` | same seed, same context through `ResolveCardIdAsync` and `AgentTaskCardBinder.BindAsync` → same id; the guard on D1 |

**`AgentTaskCardBindingTests`**: `an_identifier_on_two_boards_with_no_scope_binds_nothing_and_says_so`
*(exists)* — assertion widened to expect the guids in the warning; the other 13 unchanged and must
stay green through the lift.

**`CardCliE2ETests`** (real script, fixture Kestrel):

| Test | Pins |
|---|---|
| `The_cli_resolves_a_colliding_identifier_from_the_checkout_and_from_minus_Board` | two boards on two projects with real temp-dir `LocalRepositoryPath`s (each `git init`-ed so `--show-toplevel` answers); `card.ps1 get CARD-000N` run with `WorkingDirectory` under project 1 prints board 1's guid; `-Board <name of board 2>` prints board 2's; `-Board '<name>'` on a board that lacks it exits 1 with the 404 text |
| `An_ambiguous_identifier_prints_every_candidate_and_exits_1` | run from a directory outside both projects: stderr contains both board names and both guids, no `\u2014`, `code card_identifier_ambiguous`, exit code 1 read from the process, not a pipeline |

Both E2E tests set `ANTIPHON_TASK_TOKEN` to empty in the child's environment so an operator's
shell token cannot leak a scope into the test.

## 5. Operator decisions

1. **(a)+(c) now, (b) not at all** — recommended. (b) buys nothing for any caller that exists
   today and costs a renumbering plus eight hard-coded `CARD-` sites; it stays available as a
   separate card if per-board prefixes are ever wanted for prose, and the scoped resolver would
   still be the right shape underneath it. Overturning this changes the slice from "one" to a
   migration card.
2. **A fenced `-Board` that lacks the card is a 404, never a fall-through** — recommended. The
   alternative ("the board you named does not have it, but Antiphon does, so here is Antiphon's")
   is the silent-wrong-card shape the binder's doc calls "worse than binding none".
3. **The caller's own directory counts when no `cwd` is sent** — recommended (it is what the
   binder already does for `delegate.ps1`). The alternative treats a token caller and a shell
   caller differently for no reason the caller can see.
4. **`card.ps1` prints `detail` + candidates instead of the raw JSON on every error, not only
   this 409** — recommended, same slice; it is four lines in one catch and every server message
   (the length ceilings, the concurrency 409) reads better for it. Say no if a consumer somewhere
   parses the script's stderr as JSON — I found none.

## 6. Not planned

- Any change to how identifiers are allocated, the `IX_Cards_BoardId_Identifier` index, or the
  `#N is CARD-000N` rule (CARD-0170/0175).
- A `-Board` on `delegate.ps1` — its `-Card` already takes a guid and its title/inherited scopes
  already narrow; if a delegate ever needs to name a board it is a one-line follow-up on the same
  `CardScopeContext`.
- An `ANTIPHON_BOARD` environment variable. The token and the checkout already say which board a
  session works; a third, hand-set source is one more thing to be stale.
- Client changes: the UI addresses cards by guid and searches within one board.
- Pruning the 45 empty boards, or stopping project setup from creating one per project — the
  collision is inherent to per-board numbering, not to the count of boards.
