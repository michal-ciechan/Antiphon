# CARD-0846: card search and complete list pagination

Plan, 2026-09-30. Inspected source: `24cf55c5bb36800a65d6dfebbb992d4901b2b5a8`.
Card: `85d0b6c4-1fb3-4331-b5b7-b3c523d3ab55`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`. Read the full live card with
`pwsh -NoProfile -File scripts/card.ps1 get CARD-0846 -Json`.

Outcome: add server-side substring search and resumable pages without changing card
ranking, existing filter meanings, or the description preview contract. Shell commands
must either return the complete enumeration or fail explicitly. This is a Plan-stage
artifact; implementation and executed verification remain for subsequent stages.

## Ground truth

| Brief/card premise | Inspected implementation | Consequence |
|---|---|---|
| List is capped at 500 without a cursor | `CardEndpoints.MapCardEndpoints` calls `CardService.GetSummaryAsync`; service uses `Max(1, Cards.MaxListResults)`, `Take(limit + 1)`, removes the lookahead, returns `CardListDto(Cards, Truncated)` | Retain bounded pages and the existing envelope; add a continuation |
| Listing is ordered by rank | Actual flat list order is `UpdatedAt DESC, Id ASC`. Rank is calculated in `BoardService.ToCardDto`; board/column ranking is a different surface | Preserve the actual order; use updated timestamp and UUID as the keyset |
| Existing list filters | At least one of `boardId`, single `status`, `updatedSince` is required (400 otherwise). Supplied filters intersect; timestamp comparison is inclusive `>=`. Service always excludes archived cards | Keep these defaults and intersections; add an explicit archive opt-in |
| List descriptions are previews | `BoardService.ToSummaryDto` clips BOTH description and terminal reason using `Cards.SummaryPreviewChars` (200), empties sessions, and sets `hasMore` when either text was clipped | Search stored text before summarizing; get one card for full text |
| Query/repository layer | `CardService` uses `AppDbContext` directly. No card repository abstraction exists. The card-file repository is unrelated | Keep the existing service/DbContext pattern; isolate provider SQL in a small Infrastructure helper |
| Searchable persistence | Identifier/title/nullable alias, description and nullable terminal reason are existing columns; `LabelsJson` is `jsonb` serialized from a string array. Private notes are separate | Match decoded label elements, not serialized JSON punctuation or escape sequences; exclude private notes |
| Existing indexes | `AppDbContext` defines PK `Id`, `BoardId`, `BoardColumnId`, unique `(BoardId, Identifier)`, `(BoardId, Status)`, and agent/session/workflow indexes. There is no text-search index or `(UpdatedAt, Id)` index | No schema/index migration is needed for correctness; explain scan costs explicitly |
| Shell surface | `scripts/card.ps1` has neither list nor search; `Invoke-Antiphon` supplies `ANTIPHON_API` and task header, encodes bodies, and exits on HTTP failure. Board names resolve through `/api/boards`; the second positional argument is `$Card` | Reuse transport and board resolution; add collection-specific argument handling before card lookup |
| Instructions | Root AGENTS.md now has a short card front-door bullet, not a `Working cards from a shell` section. `board-api.md` still refers to that old heading. ops-http says there is no limit/pageSize | Restore a small synopsis at that heading and correct the stale references; do not recreate the former long instructions |
| Existing tests | `CardCorrectionApiTests` has 12 unparameterized tests, including unfiltered refusal, status, updatedSince, previews and full views. `CardAliasApiTests` has 8. `CardDiagnoseScriptTests` has 2 loopback CLI tests | Preserve these as focused regression coverage; new tests exercise real PostgreSQL and the actual script |

Other bodies inspected: `Card`, `CardsSettings`, `BoardDtos`, card endpoint routes,
`CardPrivateNotesApiTests` public-response checks, `CardFilePrivacyScriptTests`,
`AntiphonWebAppFactory`, `TestDbFixture`, and the checkpoint table importer. The web
factory owns a cloned test database, disables background execution, and replaces the
runner with `RefusingSessionRunnerClient`. Keep those guards.

## Decisions

### D-1. One bounded query contract, two read endpoints

Extend `GET /api/cards` with `limit`, `pageToken`, `includeArchived=false`.
Keep `boardId`, `status`, `updatedSince` and the current unfiltered-read refusal.
Neither `limit`, a token nor `includeArchived=true` counts as a filter.
Response: `{ cards, truncated, nextPageToken }`, with an explicit null final token.
Add an optional trailing `NextPageToken = null` to `CardListDto` so existing two-argument
construction and readers remain compatible.

Add literal `GET /api/cards/search` before the `/{id}` registration for readability;
it takes `q`, optional `boardId`, single `status`, `limit`, `pageToken`, and
`includeArchived=false`. A nonblank `q` is sufficient scope; absent board/status means
all boards/all statuses, including Done and Canceled. Return
`{ cards, total, truncated, nextPageToken }` via a separate `CardSearchDto` in BoardDtos.
`total` is a 64-bit count of ALL matching rows before the cursor and page limit, repeated
on every accepted page. An empty result has `cards: []`, `total: 0`, `truncated: false`,
`nextPageToken: null`. Search never routes through card-identifier resolution.

For either endpoint, omitted limit uses `Max(1, Cards.MaxListResults)`; positive limits
are clamped to that ceiling. Zero/negative limits are 422 `ValidationException` on
`limit`. Preserve framework 400 binding failures for invalid enum, GUID, timestamp or
integer syntax/overflow. Search q is trimmed, required, and at most 500 characters
(`CardService.MaxSearchQueryLength`); missing/blank/oversize q is 422 on `q`.
Do not truncate a search term. Unknown board GUID naturally returns an empty list.

`includeArchived` concerns card archival, as on board detail. Do not add implicit
board/project archival filters absent from the existing flat list. A default read
excludes archived cards; an opted-in read includes both archived and live cards.

### D-2. Literal full-field matching; no migration or new index

Match the trimmed q as one case-insensitive literal substring in ANY of identifier,
alias, title, FULL description, any decoded label, or FULL terminal reason. Words in q
remain a phrase; no stemming, relevance ranking, regex or query language. Identifier
matching uses its stored text; shorthand resolution remains the detail route's job.
Null alias/reason is simply a nonmatch. Search returns the same summary cards as list,
so the matched words need not appear in the preview. Private notes, archive reasons,
comments, revisions and session transcripts are not searched.

Use PostgreSQL `ILIKE` with an explicit escape character; escape backslash, percent
and underscore before adding the surrounding percent wildcards. Parameterize q and
every other user value. Use `EXISTS (SELECT ... FROM jsonb_array_elements_text(...))`
for labels; casting the JSON array to text is wrong for quotes, backslashes and Unicode.
Do not fetch 500 rows and filter in memory, or deserialize every card's description to
find a match. The complete predicate executes in PostgreSQL before limit/projection.

Place provider-specific, composable `FromSqlInterpolated` query construction in
`Infrastructure/Data/CardReadQuery.cs`; it returns an `IQueryable<Card>` over the
existing DbContext, not a repository or a new DI service. SQL fragments and identifiers
are fixed source text. CardService owns validation, transactions and DTO assembly.
Retain `AsNoTracking`, the existing assigned-agent/workflow/external-issue projection,
and `BoardService.ToSummaryDto`; do not load sessions or private-note DTOs.

**Migration decision: none. Index decision: none in CARD-0846.** Existing text/jsonb
columns can satisfy literal matching. A tsvector would change phrase/substring
semantics; trigram indexing adds extension/deployment/write costs without measured
need. Board/status indexes narrow scoped reads, but leading-wildcard search and the
consistency check below can still scan the matching scope. This is an explicit cost,
not a claim of indexed full-text performance. Capture elapsed time and query plans
against the large test fixture; if unacceptable, return evidence to Plan before adding
an index/migration. An index proposal is a separate, measured footprint change.

Provider references: [PostgreSQL JSON array expansion](https://www.postgresql.org/docs/16/functions-json.html)
and [EF Core parameterized, composable SQL](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries).

### D-3. Keyset order and exact page boundaries

Keep `ORDER BY UpdatedAt DESC, Id ASC`. Continue strictly after the last RETURNED row:

```sql
UpdatedAt < @afterUpdatedAt
OR (UpdatedAt = @afterUpdatedAt AND Id > @afterId)
```

Use PostgreSQL's native timestamp/UUID comparisons for the predicate and sorting;
do not compare UUID strings or depend on .NET Guid ordering. Store the database-read
UTC timestamp without truncating its precision. Fetch `effectiveLimit + 1`; the extra
row proves another page exists but is not returned or used as the cursor boundary.
`truncated` means more matching rows remain after this page, not that it happens to
contain exactly the limit. Thus exactly 500 total rows produce no token; 501 produce
500/1 pages. Lower limits behave identically. No OFFSET and no count-based skip.

Use versioned base64url JSON via `Application/Services/CardPageToken.cs`. Carry endpoint
kind, normalized board/status/updatedSince/q/archive filters, effective limit,
last timestamp/id, and the scope fingerprint from D-4. Validate format/version,
required fields, UTC timestamp, nonempty UUID, fingerprint shape and 4096-character
input bound. Changed filters, endpoint or effective limit invalidate the token with
422 `validation_failed` on `pageToken`. Continuations repeat their original query
parameters; a token never supplies or broadens request scope. Keep tokens opaque in
docs; they are continuation data, not authorization credentials. No key store, cache,
TTL, stored cursor, or deployment-specific signing secret is needed.

### D-4. Concurrent writes cannot silently turn a scan into an incomplete result

Both rank and UpdatedAt are mutable. A plain keyset, including one fenced by a
first-page timestamp, can omit an unseen card edited across the boundary. Returning
such a scan as complete would retain the duplicate-check bug.

Choose an optimistic, restart-on-change enumeration. In each request open a short
read-only-use RepeatableRead transaction. Stream only `(Id, UpdatedAt)` from the full
matching query BEFORE applying the cursor or limit, in database Id order. Compute
a SHA-256 digest with a versioned, fixed-width canonical encoding of UUID bytes and
UTC ticks; count that stream for search total. Use incremental hashing, not a list of
all Card entities. The first page places this digest in its token. Each continuation
compares a newly computed digest before querying its page. A mismatch throws 409
`card_page_changed`: "Matching cards changed during pagination; restart without
pageToken." The metadata scan and page query MUST share the same transaction snapshot;
commit/dispose before returning. Forward RequestAborted throughout.

This guarantees a successfully completed scan enumerates an unchanged matching
membership/order exactly once. A concurrent insert, deletion, archival, filter-membership
change or timestamp movement either changes the next request's fingerprint and refuses
continuation, or occurs after that request's snapshot and belongs to a later refresh.
Changes wholly outside the matching set do not interrupt the scan. It is not a historical
snapshot of every DTO field across HTTP requests, nor a promise of progress under
continuous writes. Clients buffer results until success and restart explicitly on 409.
No automatic retry loop is added; existing card reads remain outside the admitted
resilience operations in `docs/resilience.md`.

The tradeoff is O(matching rows) narrow metadata work per page, with bounded memory
and bounded full-card hydration. At the card's reported hundreds of rows this avoids
a durable snapshot table, unlimited in-memory cursor cache, or transactions held over
client think time. Count/max-timestamp alone is rejected: it misses deletions and
movements below the maximum. Skipping changed rows or silently de-duplicating them in
the client is rejected because neither proves completeness. If uninterrupted progress
under arbitrary writes is later required, it needs a retained snapshot design and a
separate scope decision; do not advertise that stronger guarantee here.

The within-request consistency relies on [PostgreSQL Repeatable Read](https://www.postgresql.org/docs/16/transaction-iso.html#XACT-REPEATABLE-READ).

### D-5. Shell collection commands always exhaust pages

Add:

```powershell
pwsh -NoProfile -File scripts/card.ps1 search 'checkpoint receipt' -Board Antiphon -All
pwsh -NoProfile -File scripts/card.ps1 search 'CARD-0469' -Board Antiphon -Json
pwsh -NoProfile -File scripts/card.ps1 list -Board Antiphon -Status Done -Json
pwsh -NoProfile -File scripts/card.ps1 list -UpdatedSince '2026-09-30T00:00:00Z'
```

Add `list` and `search` to Verb; reuse the second positional argument as search text
only for search. Add `-Status`, `-UpdatedSince` (list only), `-All`, and `-Limit` (page
size hint, not a total result cap); `-Json` already exists. Validate collection-only
parameters and misplaced card arguments before HTTP. List needs Board, Status, or
UpdatedSince, matching the existing API guard; `list`/`list -All` alone fail locally
with those choices. Search without Board/Status is explicitly fleet-wide. No inferred
cwd board or hard-coded board GUID. `-All` means include archived; it never controls
whether pagination is followed. A named Board with `-All` resolves using
`/api/boards?includeArchived=true`; retain current board resolution for other verbs.

Build a collection query independently of `Get-CardScopeQuery`, encoding every value
with `EscapeDataString` (including q, timestamps and tokens). Reuse `Invoke-Antiphon`
for every page so the configured base and task header are preserved. Do not call
`Get-CardOrFail`, fetch all board-detail columns, or search local previews.

Accumulate summaries, preserving server order, until `truncated=false` and token null.
On each nonfinal page write a clear continuation/cap note to stderr, including returned
count; stdout remains machine-readable with `-Json`. Fail nonzero on a truncated
page without a token (old server), token cycles/reuse, empty continuing page,
contradictory truncated/token fields, duplicate card IDs, a changed search total,
final count unequal to total, or any subsequent HTTP failure. Do not discard duplicates
and declare success. Buffering means a failed enumeration emits no successful cards
or JSON envelope. The existing transport already reports 409 code/detail and exits;
retain that behavior for `card_page_changed`, with a restart instruction from the API.
Never fall back from search 404 to local title/preview matching.

On success `-Json` emits ONE object: `{ cards, total, truncated: false,
nextPageToken: null }`. For search total is the verified server total; for list total is
the accumulated count. Empty results retain an array, not null, and one-card results
retain an array, not a scalar. Human output starts with count and scope, uses existing
card-line formatting plus board ID when unscoped, and states that text is previewed;
use `card.ps1 get` to inspect a hit. Keep the script ASCII-only/PowerShell 5.1-compatible.

### D-6. Document the safe duplicate-check workflow

Update ops-http's routes, board example and "Shapes that bite"; remove its obsolete
no-limit claim. Update the antiphon-api route map with search, pagination, errors,
total, archive semantics and ordering. Update board-api's verb list and duplicate-check
instruction. Add a compact `### Working cards from a shell` synopsis to AGENTS.md
adjacent to its essential front doors, replacing its existing bare card-script bullet
with the link/examples rather than duplicating a long guide.

Canonical example: `card.ps1 search '<distinctive phrase>' -Board <name> -All`, then
`card.ps1 get <hit> -Board <name>` for the full description/verdict. State explicitly
that no match in a capped list or preview is evidence of absence. Explain that a failed
or changed enumeration cannot establish "no duplicate". The detailed CLI header and
ops-http own the long contract; bundle and AGENTS synopsis link to them. Existing
write, private-note and task-header rules remain applicable.

## Slices and exact implementation footprint

Only this plan is changed by the Plan task. The following is the closed expected
Code footprint for collision checking; new paths are explicitly marked. No migrations,
model snapshot, domain entities, ranking code, client UI, Program registration,
session runtime, generated `docs/cards/` files or live configuration are in this footprint.
If TestDesign needs an extra fixture or Code discovers another caller, record the
additional exact path in the plan before implementation/dispatch scope is expanded.

| Slice | Files | Work and closure |
|---|---|---|
| S1 | `server/Api/Endpoints/CardEndpoints.cs`; `server/Application/Services/CardService.cs`; `server/Application/Dtos/BoardDtos.cs`; **new** `server/Application/Services/CardPageToken.cs`; **new** `server/Infrastructure/Data/CardReadQuery.cs`; **new** `tests/Antiphon.Tests/TestHelpers/CardReadApiFixture.cs`; **new** `tests/Antiphon.Tests/Application/CardListPaginationApiTests.cs` | List parameters, keyset/token/fingerprint, summary compatibility and real PostgreSQL list tests; CP-1 |
| S2 | `server/Api/Endpoints/CardEndpoints.cs`; `server/Application/Services/CardService.cs`; `server/Application/Dtos/BoardDtos.cs`; `server/Infrastructure/Data/CardReadQuery.cs`; `tests/Antiphon.Tests/TestHelpers/CardReadApiFixture.cs`; **new** `tests/Antiphon.Tests/Application/CardSearchApiTests.cs` | Search predicate, total, full-field/archive coverage using S1 continuation machinery; CP-2 |
| S3 | `scripts/card.ps1`; **new** `tests/Antiphon.Tests/Scripts/CardListSearchScriptTests.cs`; `docs/ops-http.md`; `docs/antiphon-api.md`; `server/Bundles/board-api.md`; `AGENTS.md` | Complete enumeration, diagnostics, real script against loopback API, concise instructions; CP-3 and manual doc/ASCII review |

S2 depends on S1; S3 depends on both. Commit/push each slice before its checkpoint
run, then commit/push any corrections. Existing regression-test files are read/run,
not planned edits. This artifact may be appended by TestDesign:
`docs/superpowers/plans/2026-09-30-card-0846-card-search-pagination-plan.md`.

## Verification requirements for TestDesign

The separate TestDesign stage must finalize fixture mechanics, V/R and guard inventories
without weakening D-1 through D-6. The named roster and checkpoint counts below are
planned execution counts, not claims of tests already written or passed. Use one
unparameterized method per row below; internal boundary loops do not inflate counts.

Use `CardReadApiFixture : AntiphonWebAppFactory`, shared per test session by the two new
API classes, preserving the refusing runner and cloned database. Create owned projects/
boards, seed through DbContext in a batch, and clean only owned rows. The default fixture
has at least 605 nonarchived cards plus 3 archived cards, several tied UpdatedAt values
straddling page boundaries, distinct UUIDs, all status types, nullable fields and
an additional foreign board. Search markers are unique per test. The old card that
must be found is deliberately beyond page 1 in actual UpdatedAt order, not merely
low-numbered. Do not lower MaxListResults to pretend the >500 case was exercised.

For within-request concurrency, use a test-owned EF command interceptor/barrier in
the new fixture: after the metadata read and before page SQL, commit an owned-card
change through a second DbContext. No sleeps and no production test hook. Assert the
first response is internally consistent and the next continuation detects the change.
TestDesign must bind the interceptor to that test's board/request and show it fired.

| ID / class | Exact new method | Decisive ordinary assertion |
|---|---|---|
| V-1 / CardListPaginationApiTests | `More_than_500_cards_are_returned_exactly_once` | Concatenated pages equal the fixture's complete expected ID sequence and distinct count, including oldest card |
| V-2 / same | `Page_boundaries_use_lookahead` | Owned sets of 0, 1, 499, 500, 501 and 1000; exact counts and token/truncated truth table, with no unnecessary extra page |
| V-3 / same | `Equal_timestamps_use_database_uuid_order` | Page size 2 across deliberately shuffled tied UUIDs; exact DB order and no repeats/gaps |
| V-4 / same | `Existing_filters_intersect_and_unfiltered_reads_fail` | Board/status/since intersections; inclusive timestamp; no filter and archive/token-only requests are 400 |
| V-5 / same | `Archived_cards_require_opt_in` | Default excludes only archived cards; opt-in includes them through later pages |
| V-6 / same | `Limits_and_invalid_tokens_are_validated` | Zero/negative limits 422, large positive clamp, malformed/oversize/unsupported tokens 422 |
| V-7 / same | `Tokens_cannot_change_the_query` | Independent board/status/since/archive/limit/endpoint changes reject continuation; unchanged request succeeds |
| V-8 / same | `Moving_an_unseen_card_refuses_incomplete_continuation` | After page 1, update unseen row across cursor; next response is 409 card_page_changed; fresh scan includes it |
| V-9 / same | `Membership_changes_refuse_continuation` | Insert, archive, delete, status departure and arrival each invalidate; equal-count swap is also detected |
| V-10 / same | `Writes_outside_the_matching_scope_do_not_break_paging` | Foreign-board change still permits exact complete scoped enumeration |
| V-11 / same | `Fingerprint_and_page_share_a_database_snapshot` | Barrier-controlled intervening write cannot mix metadata and page snapshots; next request refuses drift |
| V-12 / same | `Summary_previews_and_detail_remain_distinct` | Both long fields clipped and hasMore true; empty sessions; detail preserves full text; no privateNotes payload |
| V-13 / CardSearchApiTests | `Search_finds_an_old_card_beyond_the_first_500` | Target missing from first list page is returned by its unique stored marker |
| V-14 / same | `Every_public_search_field_matches_independently` | Separate marker-only identifier, alias, title, description, label and terminal reason rows are each found, case-insensitively |
| V-15 / same | `Description_and_terminal_body_matches_precede_previewing` | Marker beyond 200 chars matches both fields while returned preview omits it; get returns it |
| V-16 / same | `Totals_and_search_pages_cover_all_matches` | >500 matching rows; total stays full pre-cursor count; final union exact and no matches has total zero |
| V-17 / same | `Search_filters_include_closed_and_optional_archived_cards` | Done/Canceled found by default; archive opt-in, board/status intersection and unscoped q behavior correct |
| V-18 / same | `Literal_patterns_and_decoded_labels_do_not_overmatch` | Percent, underscore, backslash, quote, Unicode and SQL-looking input match only literal markers; multiword q is a phrase |
| V-19 / same | `Invalid_search_and_changed_search_tokens_are_rejected` | q missing/blank/>500 gives 422; changed q and list token reject; search literal route is never an identifier lookup |
| V-20 / same | `Private_notes_comments_and_revisions_are_not_search_sources` | Synthetic exclusive markers in excluded fields produce zero hits and do not leak in responses |
| V-21 / same | `Search_membership_change_invalidates_the_next_page` | Edit previously nonmatching description to match; next token gets 409, fresh total and results include it |
| V-22 / same | `Nondefault_page_limit_and_null_fields_work` | Nullable alias/reason and empty labels are safe; limit 2 yields full exact enumeration |
| V-23 / CardListSearchScriptTests | `List_exhausts_pages_and_keeps_json_clean` | Real pwsh gets 500+ pages; stdout parses as one envelope; stderr contains cap/continuation note |
| V-24 / same | `Search_uses_search_endpoint_and_preserves_encoded_query` | Special characters/Unicode, Board and Status decoded identically on every request; all matches returned |
| V-25 / same | `All_includes_archives_and_resolves_archived_board_names` | -All sends archive opt-in for board-name resolution and every card page; absence sends neither |
| V-26 / same | `Paging_is_automatic_without_All` | Both verbs request the final page without -All and without a total-result cap |
| V-27 / same | `Old_or_inconsistent_pagination_fails_without_results` | Missing token, contradictory flag/token, empty continuing page, repeated token and token cycle each fail nonzero with empty result stdout |
| V-28 / same | `Later_http_failure_and_changed_scope_do_not_emit_partial_json` | Page 2 500/409 fail; 409 names restart; search 404 has no local fallback |
| V-29 / same | `Duplicate_ids_and_inconsistent_totals_are_errors` | Duplicates, changed total and final total mismatch each fail instead of reporting complete |
| V-30 / same | `Collection_argument_errors_are_local` | Missing q/list scope, invalid status/limit, illegal UpdatedSince on search, collection flags on get rejected before any request |
| V-31 / same | `Empty_and_single_results_keep_array_shapes` | Both verbs, JSON empty/single arrays; human output count/scope and preview/get guidance |
| V-32 / same | `Every_page_preserves_base_and_task_header` | Loopback API verifies a synthetic task token and configured base on all requests; no real token inherited or printed |
| V-33 / same | `List_updated_since_is_encoded_and_preserved` | Every page retains exact UTC timestamp; status-only list works without Board |

CLI tests follow CardDiagnoseScriptTests' real `ProcessStartInfo.ArgumentList` and
`EphemeralHttpListener` pattern with separate stdout/stderr, timeout disposal and
`[ParallelLimiter<ProcessSpawnLimit>]`. They exercise actual script execution, not
source-text searches. No production ports, browser, live board seeding or agent launch.
Tag new classes Integration. API assertions always scope owned fixtures.

R-1 is all 12 existing CardCorrectionApiTests; R-2 all 8 CardAliasApiTests; R-3 both
CardDiagnoseScriptTests. Their purpose is to catch list/summary/detail and existing
CLI dispatch regressions from the shared edits. Documentation review (R-4) checks
all four instruction surfaces against D-6 and ASCII preservation; it needs no mirrored
source-string test. Capture query timing/plans as diagnostics within the API fixture,
without flaky wall-clock pass thresholds or an extra ad hoc build/test run.

### Positive controls to retain and refine

Each variant must compile and fail the named observable assertion. Code runs ordinary
V/R. Mutation, after reviewed land, runs exact-method green/break/red/restore/fresh-build/
green cycles at the published source. A build failure, zero tests or broken fixture is
not a positive control. TestDesign must finish the exhaustive guard-to-PC map.

| PC | Compiling defect in the production path | Exact red method / assertion |
|---|---|---|
| PC-1 | Drop UUID tie comparison in keyset continuation | V-3 `Equal_timestamps_use_database_uuid_order`: complete ordered ID sequence differs |
| PC-2 | Use the lookahead row as the cursor boundary | V-1 `More_than_500_cards_are_returned_exactly_once`: boundary ID is missing |
| PC-3 | Set truncated when count equals limit without proving lookahead | V-2 `Page_boundaries_use_lookahead`: exactly-500 final token/flag wrong |
| PC-4 | Bypass scope fingerprint mismatch refusal | V-8 `Moving_an_unseen_card_refuses_incomplete_continuation`: expected 409 becomes incomplete 200 |
| PC-5 | Use ReadCommitted instead of RepeatableRead | V-11 `Fingerprint_and_page_share_a_database_snapshot`: forced interleaving changes the returned page |
| PC-6 | Search only the first SummaryPreviewChars of description | V-15 `Description_and_terminal_body_matches_precede_previewing`: body-only hit missing |
| PC-7 | Include PrivateNotes in the OR predicate | V-20 `Private_notes_comments_and_revisions_are_not_search_sources`: excluded-field hit appears |
| PC-8 | Remove archive exclusion in the default base predicate | V-5 `Archived_cards_require_opt_in`: archived ID leaks into default enumeration |
| PC-9 | Stop the CLI page loop after its first success | V-26 `Paging_is_automatic_without_All`: request roster/returned count misses final page |
| PC-10 | Treat truncated-without-token as complete in CLI | V-27 `Old_or_inconsistent_pagination_fails_without_results`: expected nonzero/empty stdout becomes false success |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c846-list/` | card-list | `/*/Antiphon.Tests.Application/(CardListPaginationApiTests*)\|(CardCorrectionApiTests*)/*` | V-1 through V-12, R-1 | 24 executed: 12 new + 12 existing; both classes present; 0 failed/skipped | 24 | 12 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c846-search/` | card-search | `/*/Antiphon.Tests.Application/(CardSearchApiTests*)\|(CardAliasApiTests*)\|(CardListPaginationApiTests*)/*` | V-1 through V-22, R-2 | 30 executed: 10 search + 8 alias + 12 list; all three classes present; 0 failed/skipped | 30 | 12 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c846-cli/` | card-cli | `/*/Antiphon.Tests.Scripts/(CardListSearchScriptTests*)\|(CardDiagnoseScriptTests*)/*` | V-23 through V-33, R-3; R-4 manual review | 13 executed: 11 new + 2 existing; both classes present; 0 failed/skipped | 13 | 8 |

CP-2 deliberately reruns the list class: S2 changes its shared base predicate and
continuation machinery. CP-3 changes no server code. If it does, revise its scope and
coverage before execution. Each row has one isolated build and one exact filter.
The total ordinary budget is 67 executed results (33 distinct new tests and the
specified regressions, with 12 list tests intentionally repeated), zero skipped.

Lane: portable .NET/PostgreSQL and pwsh, `Platform Any`; no headed/Windows-only work.
Read `/api/runner-defaults` and `/api/session-runners` at dispatch; do not pin a fleet
location. Inspection on this date found an eligible Linux lane and an eligible Windows
lane. The checkpoint tool handles non-Windows `UseAppHost=false`. Docker/test Postgres,
pwsh and the host build-slot broker are required; never substitute the dev database or
an unleased build after a slot timeout.

Bootstrap the checkpoint tool once under a build slot if no current tool binary is
available. This is the one explicitly listed setup build outside the CP rows: it
builds the driver, not the product/test host. Release its lease before starting a
checkpoint run so the orchestration process cannot occupy a slot needed by its rows.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c846-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c846-tool/ --property:UseAppHost=false -nodeReuse:false
```

After each committed slice run the already-built checkpoint tool with the
corresponding `--rows`:

```powershell
dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c846-tool/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-09-30-card-0846-card-search-pagination-plan.md --rows CP-1
```

Substitute CP-2/CP-3 for the next groups. The checkpoint drivers acquire their own
row leases; the already-built coordinator/wait process needs no build lease. Keep waiting on the recorded
run ID when the tool returns 75; use waits of at most 60 seconds when the calling tool
needs progress messages. Report each CHECKPOINT line with commit, actual counters,
TRX and reruns. New test names/count changes require a committed table update before
Code dispatch. No full suite, client build or browser E2E is justified by this scope.

### Cost

Estimated, not measured: ordinary Code verification floor 12 + 12 + 8 = **32 minutes**,
including the three isolated product/test builds. Driver bootstrap is an additional
estimated 2 minutes when needed. Authoring/fixture work approximately 90 minutes;
Code dispatch budget approximately 124 minutes including bootstrap, plus observed
build-slot queue wait.
Ten preliminary positive controls at 8 minutes each for the compiling red/restored
green builds/runs give a separate Mutation floor of **80 minutes**, plus 10 minutes
for setup/discovery/evidence = 90 minutes. Combined ordinary/PC execution floor is
112 minutes; including bootstrap, authoring and mutation overhead, approximately 214 minutes.
TestDesign must revise these estimates if its final PC inventory grows. Avoiding a
schema migration and browser fixture saves setup, but no unsupported numeric saving
is claimed.

## Handoff and acceptance

Next: TestDesign, then Code, ordinary Review, land, and post-land Mutation under the
repository workflow. No runtime behavior changed or tests executed by this Plan task.
TestDesign should scrutinize the D-4 barrier and failure semantics particularly; the
chosen guarantee is complete unchanged membership/order or explicit restart, not an
unbounded snapshot service. It must finalize the test/guard inventory and keep exactly
one authoritative Checkpoints table in this artifact.

After activation, the orchestrator verifies the loaded server SHA from the canonical
checkout and performs read-only searches for known old/body-only cards and a full
board enumeration. Use actual live data only for observation; synthetic >500-card
fixtures belong exclusively in tests. A 409 during a changing board is a retryable
operator workflow, never evidence that no duplicate exists. Existing clients may
continue reading one summary page; this card supplies continuation and fixes the
operator/agent duplicate-check front door, without redesigning their UI.
