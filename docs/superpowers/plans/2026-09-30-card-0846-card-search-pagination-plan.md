# CARD-0846: card search and complete list pagination

Plan, 2026-09-30. Inspected source: `24cf55c5bb36800a65d6dfebbb992d4901b2b5a8`.
Card: `85d0b6c4-1fb3-4331-b5b7-b3c523d3ab55`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`. Read the full live card with
`pwsh -NoProfile -File scripts/card.ps1 get CARD-0846 -Json`.

Outcome: add server-side substring search and resumable pages without changing card
ranking, existing filter meanings, or the description preview contract. Shell commands
must either return the complete enumeration or fail explicitly. The fix design is
preserved; TestDesign below is finalized.
Implementation and executed verification remain for subsequent stages.

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
that a lack of matches in a capped list or preview is not evidence of absence.
Explain that a failed or changed enumeration cannot establish "no duplicate". The detailed CLI header and
ops-http own the long contract; bundle and AGENTS synopsis link to them. Existing
write, private-note and task-header rules remain applicable.

## Slices and exact implementation footprint

Only this plan is changed by the Plan and TestDesign tasks. The following is the
closed expected
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

## Verification design

TestDesign finalized 2026-09-30 against plan source
`85daf142cce144f88347f9f26cfa91ef1021f7ee` and the full live CARD-0846.
D-1 through D-6 and the exact S1/S2/S3 file footprint remain authoritative.
This is a static design, not evidence of executed tests, builds or mutations.

### Inspection

| Bodies read | Boundary and coverage |
|---|---|
| `CardService.GetSummaryAsync`, card list/detail endpoints, `CardListDto`, `CardsSettings`, `BoardService.ToCardDto/ToSummaryDto/Preview` | Actual UpdatedAt/UUID ordering, archived predicate, filters, lookahead and preview semantics -> V-1 through V-12, R-1 |
| `Card`, `CardRevision`, `CardComment`, their AppDbContext mappings and public-note API assertions | jsonb labels, nullable fields, public projection versus excluded search sources -> V-13 through V-22 |
| Entire `CardCorrectionApiTests`, `CardAliasApiTests`, `CardDiagnoseScriptTests`; relevant `CardPrivateNotesApiTests` and `CardFilePrivacyScriptTests` bodies | Real HTTP/database fixture, error assertions, real pwsh transport and synthetic notes -> R-1 through R-3 and new API/CLI classes |
| `AntiphonWebAppFactory`, `TestDbFixture`, `IsolatedTestSchema`, `CountingCommandInterceptor`, `OutputDistillationDispatchTests.ClaimProbe`, `EphemeralHttpListener` | Cloned PostgreSQL database, ApplyTestOverrides, second-context writes, parameter-scoped interception, owned loopback process -> all V cases |
| `scripts/card.ps1` parameters, Invoke-Antiphon, Resolve-BoardId, Get-CardScopeQuery, Write-CardLine and verb dispatch | Local validation precedes board lookup; collection queries bypass cwd/card resolution; output buffering -> V-23 through V-33 |
| `ExceptionMiddleware`, `ValidationException`, `ConflictException`; `PlanTableImporter` and testing/build owner | 400 binding versus 422 field errors versus coded 409; importer, roster and count contract -> V-4/V-6/V-7/V-19, CP-1 through CP-3 |

### Delivery inventory

No new asynchronous queue, session input, durable outbox or background delivery path.
The producer is the HTTP card read; the destination is the requesting client, then
the shell's stdout. Each request reads one database snapshot, with no persisted cursor.
The opaque token binds successive reads; receipt is a parsed final envelope and its
complete ordered ID sequence. A failed request produces no successful shell result;
recovery is an explicit fresh enumeration. V-11/V-21 exercise database interleaving;
V-23 through V-33 use a real pwsh child and loopback HTTP. That stub proves the shell
protocol, not PostgreSQL semantics; the real-host API tests provide the latter.
No session transcript or queue-delivery substitute is claimed.

### Fixture and assertion mechanics

All new helper types, including interceptors, stay inside the three new test files
and `CardReadApiFixture.cs` already listed in S1-S3. No production hook, new shared
fixture file, Program edit or test-project edit is required.

- Use `CardReadApiFixture : AntiphonWebAppFactory`, shared
  `PerTestSession` by both new API classes, each `[Category("Integration")]`
  and `[NotInParallel]` without a group. Call base ResetAsync; disarm the
  interceptor and restore any changed settings in finally. Keep the factory's cloned
  database, dead runner URL, refusing runner and background-service guards.
- Add the factory-owned interceptor through
  `ApplyTestOverrides(services)` / `services.AddDbContext<AppDbContext>(o =>
  o.AddInterceptors(probe))`; retain the base provider/connection options. Pin this
  fixture's CardsSettings to MaxListResults=500 and SummaryPreviewChars=200 via
  services.Configure, leaving the separate legacy factory untouched. A helper
  creates the writer/oracle context with
  `new AppDbContext(TestDbFixture.CreateDbContextOptions(ConnectionString))`,
  without the interceptor. Never use the default shared-store connection.
- Seed an owned project and BoardService-created boards/columns, then batch-insert
  Cards using the real column IDs, unique per-board identifiers, valid status/column
  pairs and explicit UTC timestamps. The large world has 605 live plus 3 archived
  cards and a foreign board. Smaller boundary worlds are separate owned boards.
  Keep the ceiling at 500 for large-world assertions. Cleanup deletes only owned
  comments/revisions/cards, board workflow definitions, columns, boards and project,
  in FK-safe order; additional navigation fixtures are tracked and removed too.
- Capture expected IDs independently with a simple PostgreSQL query over known seed
  membership, ordered by UpdatedAt DESC, Id ASC; never use CardReadQuery, the token
  codec or the API response as the oracle. Read timestamps back after SaveChanges
  to account for database precision. The oldest hit must actually fall after row
  500 in this oracle. V-3 seeds shuffled fixed UUIDs plus UTC microsecond differences
  within the same millisecond; ties straddle pages of size 2. Compare sequences,
  not sorted sets alone, and separately assert total distinct IDs.
- New API page helpers use JsonDocument as well as typed DTOs: assert actual
  `cards` array, boolean `truncated`, and explicit `nextPageToken: null`
  on a final page. Guard helpers against cycles and excessive requests so a faulty
  endpoint fails rather than hanging. Compare each page with the expected oracle
  slice immediately, before the helper's cycle/request-budget checks, so PC-1/2/11/12
  fail on wrong returned IDs rather than a later loop timeout. Search reads `total`
  with GetInt64.
  422 asserts `code=validation_failed` plus the exact lowercase
  `errors.limit`, `errors.q` or `errors.pageToken`;
  409 asserts `code=card_page_changed` and restart detail. Binding 400 does
  not require a Problem Details body that the framework need not provide.
- Preview fixtures use spaced words, for example 60 repetitions of `"abcd "`
  followed by a unique marker, independently in description and terminal reason.
  An unbroken 300-character word is NOT a valid clipping fixture: the existing
  Preview helper preserves it. Assert the known 199-character word boundary plus
  ellipsis, absence of the late marker, hasMore, empty sessions and full detail text.
  Seed one AgentSession with the owned CardId, a synthetic DefinitionName/Cwd,
  explicit UTC CreatedAt/StartedAt/LastSeenAt and Status=Exited; assert detail includes
  its ID and summaries have empty sessions. No process is launched; delete this owned
  row before card cleanup. Public JSON may expose `hasPrivateNotes`, never the
  `privateNotes` property or its unique contents.

#### Deterministic database snapshot barrier (V-11, repeated for search in V-21)

1. Seed four matching live rows A/B/C/D with distinct decreasing timestamps; take
   the pre-write database oracle. Request limit=2. Arm one probe for the owned BoardId and one request.
   All other commands, including setup, oracle reads and the separate writer, bypass it.
2. In ReaderExecutedAsync identify the scoped Cards metadata reader by its two
   result columns Id/UpdatedAt and the typed board parameter; record its EF ContextId
   and transaction reference. This callback means the reader opened, NOT that the
   metadata stream finished. Do not mutate or block in this callback.
3. Gate ReaderExecutingAsync for the subsequent bounded Cards page SELECT on that
   same ContextId/BoardId. Verify the metadata reader has closed (retain its reference
   and inspect IsClosed), and record the page transaction. Signal a
   RunContinuationsAsynchronously TaskCompletionSource, then await a release TCS
   with cancellation and a bounded failure deadline. No sleeps or injected SQL tags
   required in production. A missing gate or unexpected query sequence is fixture
   failure, never a PC red.
4. While that page command has NOT executed, the test uses the separate context to
   move C ahead of A and commit. Assert one row changed; a fresh independent read must
   see C first before releasing the gate. In finally always release/disarm and await
   the HTTP task, even if writer/assertions fail.
5. Assert first HTTP 200, exact IDs [A,B], truncated true and a nonempty token, and
   one gate hit. Search additionally asserts the pre-write total of four. Then assert
   a continuation is 409 and a fresh enumeration equals [C,A,B,D]. Check metadata
   and page shared a nonnull transaction as supporting evidence, AFTER the decisive
   data assertion. PC-5 changes isolation only: ReadCommitted returns [C,A] and
   fails the ordered response assertion, not a timeout or an isolation enum check.

For S1 V-11 uses list only; S2 V-21 adds the same search interleaving after its
between-request membership cases. PostgreSQL establishes Repeatable Read's snapshot
at the first non-transaction-control statement; this forced write separates that
snapshot from the page command. See [PostgreSQL isolation](https://www.postgresql.org/docs/16/transaction-iso.html#XACT-REPEATABLE-READ)
and the [EF interception seam](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors).
This is not a historical snapshot of all DTO values over multiple HTTP requests.

#### Search and query boundary cases

V-14 uses one marker-exclusive row per identifier, alias, title, description, label
and terminal reason, with mixed ASCII case; nonmatching sentinel cards must stay out.
Identifiers/aliases obey existing lengths. V-17 covers all six CardStatus values,
including Done/Canceled, board+status intersection, and fleet-wide q with unique
markers on two owned boards. V-5/V-17 include a live card under an archived
board/project: D-1 adds no implicit parent archival filter.

V-18 seeds labels via JsonSerializer.Serialize(string[]), never hand-built JSON.
For each of `%`, `_`, backslash, double quote, non-ASCII `雪` and a SQL-looking
literal, use separate unique markers for the label-only and text-field subcases
(one expected hit per query).
Include near-miss decoys (`rateXdone` versus `rate%done`, `partXname` versus
`part_name`, missing/doubled backslashes). Also query a decoded embedded quote,
a JSON-escape-shaped backslash+quote that is NOT in that decoded label, and a phrase
spanning two different label elements: the last two have zero hits. Duplicate the
same matching label within one card and assert one card/one total. Add a multiword
phrase, reversed/separated words and trimmed q. Assert exact IDs AND total in each
subcase, not merely that a target appears. Decoded elements are required by
[PostgreSQL jsonb_array_elements_text](https://www.postgresql.org/docs/16/functions-json.html).

V-20 seeds unique exclusive markers in current PrivateNotes, revision PrivateNotes,
revision public text, CardComment.Body and ArchivedReason (query with archive opt-in).
Every such query has zero cards/total. A separate public marker returns a notes-bearing
card so response-property and private-marker absence assertions cannot pass vacuously.
Comments/revisions use the inspected entities and real FK/revision-number constraints;
session transcripts are excluded by the unchanged query dependencies, not by launching
a session to invent transcript content.

V-6 loops omitted/1/2/500/501/int.MaxValue limits, zero/negative, noninteger and integer
overflow; V-4 covers malformed GUID, status name, timestamp and boolean binding.
Check the configured ceiling floor by temporarily setting the fixture's IOptions value
to 0 and -1 (restore in finally), expecting a one-card page, not an unbounded read.
V-19 also changes only the endpoint-kind member of an issued search token while
leaving q and every other member intact, so endpoint validation cannot be masked by
q validation. V-19 also pairs the 501-character refusal with successful queries of
exactly 500 characters and a trimmed nonblank term. Bind nullable q at the endpoint
so its missing case reaches the specified service 422 instead of framework 400.
V-6 mutates JSON decoded from a REAL issued token, re-encodes base64url and changes
one property per case: bad version, missing required member, non-UTC time, empty UUID,
bad fingerprint shape, malformed base64/JSON and >4096 input characters. The oversize
case adds legal JSON whitespace around a valid issued payload before base64url
encoding; all other fields remain valid so another parser guard cannot mask the size
guard. Do not call the production encoder to build the expected token.
V-7/V-19 change each request binding
independently; valid unchanged continuations are the paired positive cases.
Requests using an omitted limit and an explicit value clamped to the same effective
ceiling are equivalent; whitespace-trimmed equivalent q succeeds. Tokens are not
authorization credentials and arbitrary tamper resistance is outside D-3.

V-8 moves an unseen row across the cursor both above the maximum and, in a separate
world, below an unchanged maximum; V-9 covers insert, hard delete of an unreferenced
card, archive/unarchive, status entry/exit, and equal-count replacement preserving the
timestamp multiset (all timestamps equal in that world). Also delete a far-unseen row
beyond both page one/lookahead in UpdatedAt order AND the first effectiveLimit rows
in metadata Id order. Delete an ALREADY RETURNED row in another subcase: the full-scope
fingerprint must still reject. V-10 updates an excluded archived row and a foreign
board without changing the scoped enumeration. V-21 covers both search entry and exit
by changing description through the separate DbContext while preserving UpdatedAt
(and then its within-request barrier case). Compare a fresh
oracle after every change; never reuse another subcase's token.

Capture scoped metadata/page commands for V-1/V-16 diagnostics, plus
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) using copied test-only parameter values on
the independent connection, after the measured enumeration. Emit timing/query plans
to test diagnostics without timing thresholds. Review confirms the metadata projection
is narrow/streamed, full-card hydration bounded to limit+1, predicate parameterized
before the limit, no OFFSET or materialized all-card list, and cancellation forwarded
through transaction, stream and page. No extra benchmark process or checkpoint.

### Proves it works now

The roster below is closed: one unparameterized [Test] method per V-n, internal loops
only. A loop's subcases are named in assertion messages and count as ONE execution.
These are 33 planned tests, not claims that new source already exists or passes.

| ID / class | Exact new method | Decisive ordinary assertion |
|---|---|---|
| V-1 / CardListPaginationApiTests | `More_than_500_cards_are_returned_exactly_once` | Concatenated pages equal the fixture's complete expected ID sequence and distinct count, including oldest card |
| V-2 / same | `Page_boundaries_use_lookahead` | Owned sets of 0, 1, 499, 500, 501 and 1000; exact counts and token/truncated truth table, with no unnecessary extra page |
| V-3 / same | `Equal_timestamps_use_database_uuid_order` | Page size 2 across shuffled ties and microseconds within one millisecond; exact independent DB order and no repeats/gaps |
| V-4 / same | `Existing_filters_intersect_and_unfiltered_reads_fail` | Board/status/since intersections; inclusive timestamp; no filter and archive/token-only requests are 400 |
| V-5 / same | `Archived_cards_require_opt_in` | Default excludes only archived cards; opt-in includes them through later pages |
| V-6 / same | `Limits_and_invalid_tokens_are_validated` | Zero/negative limits 422, large positive clamp, malformed/oversize/unsupported tokens 422 |
| V-7 / same | `Tokens_cannot_change_the_query` | Independent board/status/since/archive/limit changes reject continuation; unchanged/effective-equivalent request succeeds; endpoint/q cases are in S2 V-19 |
| V-8 / same | `Moving_an_unseen_card_refuses_incomplete_continuation` | After page 1, update unseen row across cursor; next response is 409 card_page_changed; fresh scan includes it |
| V-9 / same | `Membership_changes_refuse_continuation` | Insert, archive/unarchive, returned and far-unseen deletion, status departure/arrival and equal-count/equal-timestamp ID replacement each invalidate |
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
| V-20 / same | `Private_notes_comments_and_revisions_are_not_search_sources` | Exclusive private/revision/comment/archive-reason markers give zero hits; a positive public hit also excludes note property/content |
| V-21 / same | `Search_membership_change_invalidates_the_next_page` | Description-only membership entry/exit preserving UpdatedAt gets 409; fresh total/IDs correct; repeat V-11 barrier for search |
| V-22 / same | `Nondefault_page_limit_and_null_fields_work` | Nullable alias/reason and empty labels are safe; limit 2 yields full exact enumeration |
| V-23 / CardListSearchScriptTests | `List_exhausts_pages_and_keeps_json_clean` | Real pwsh gets 605 cards across 500/105 pages; stdout parses as one envelope; stderr contains cap/continuation note |
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


#### Real-shell harness and failure matrix

Nest the loopback stub/runner in CardListSearchScriptTests. Reuse
EphemeralHttpListener and ProcessStartInfo.ArgumentList, but keep stdout and stderr
SEPARATE (CardDiagnoseScriptTests currently concatenates them). Redirect/drain both
concurrently, set a 60-second child deadline, kill only that owned child tree on
timeout, await exit/drains, and dispose the listener/pump in finally. Never credit a
timeout, launch failure or stub exception as the expected rejection. Assert the script
exited normally with a nonzero code and the specific error on stderr.

Set `ANTIPHON_API` to the owned loopback base, optionally
with a `/fixture` prefix for V-32; replace inherited `ANTIPHON_TASK_TOKEN` with an
empty value or V-32's synthetic token. Capture method/path/decoded query/headers in
request order without logging credentials. Every unplanned request is recorded and
answered with a terminal error; fail on that roster, not only the script exit. Tests
carry `[Category("Integration")]` and `[ParallelLimiter<ProcessSpawnLimit>]`.

- V-23 serves 500 then 105 DISTINCT summaries, expects exactly two card requests,
  one JSON object with all 605 ordered IDs, total 605, false/null final fields, and
  a stderr continuation note containing 500. There is no third empty request.
  V-26 loops both verbs, with and without -All and with -Limit 2, and requires
  every scripted page: -All affects archives only, -Limit never caps aggregate count.
- V-24/V-33 compare decoded q/board/status/limit/since on EVERY page and the exact
  previous token supplied as pageToken. Include spaces, plus, ampersand, percent,
  hash, quote, slash, backslash and Unicode in q and opaque synthetic tokens.
  No cwd, board-detail GET or card-resolution request is permitted. Named board
  resolution is the only allowed preflight; GUID boards require none.
- V-25 tests both verbs. A name resolves only through
  `/api/boards?includeArchived=true` with -All, and every page carries archive
  opt-in. Without -All the query omits it or is false; the default board request
  omits archive opt-in. Unknown/ambiguous names fail before any card request.
- V-27 gives each bad case a fresh child/server and otherwise coherent cards:
  truncated true with missing/null/blank token; truncated false with a nonempty
  token; an empty page with true/new token; immediate repeated token A,A; nonadjacent
  cycle A,B,A. Run bad first-page and bad later-page cases where applicable, both
  verbs. A valid final page may be empty only when it satisfies the other contracts.
  Feed DISTINCT card IDs and consistent search totals so another guard cannot mask
  a removed token/empty-page guard. After a forbidden transition, the stub offers
  a coherent final response to an extra request, recording it as unexpected; a
  removed guard must fail a normal-exit/request-roster assertion, not just time out.
  Tokens A and a are different opaque values: a valid sequence containing both
  must succeed (avoid PowerShell's default case-insensitive set/comparison trap).
- V-28 serves one valid continuing page then HTTP 500 or coded 409
  card_page_changed. Require nonzero exit, whitespace-only stdout and error code/
  restart text for 409, with no retry. A first-page search 404 requires nonzero exit
  and exactly the search request, no fallback to list/board/detail/local previews.
- V-29 independently tests duplicate IDs within a page and across pages (including
  equivalent GUID text casing); changing search total; final count less than total;
  final count greater than total. Arrange other fields consistently for each case.
  Require nonzero exit, whitespace-only stdout and the relevant duplicate/total
  diagnostic. These are separate guards, not a generic malformed-response smoke test.
- V-30 supplies a named Board even with invalid arguments to prove validation happens
  BEFORE board resolution: missing/blank q, list without scope including -All alone,
  misplaced positional card, bad status, zero/negative/noninteger/overflow limit,
  invalid timestamp, UpdatedSince on search, and each collection-only option on get.
  Require zero HTTP requests, nonzero exit and useful stderr.
- V-31 checks empty/one/multiple JSON cards with JsonValueKind.Array and exactly one
  top-level object. Human output checks count, scope (fleet plus board IDs when
  unscoped), Write-CardLine fields and preview/get guidance. Failure cases also run
  without -Json to ensure no successful human card lines precede failure.
- V-32 requires the synthetic task header on board lookup and all pages, including a
  later failure, and proves the configured base path is preserved. Never inspect
  the real environment token in diagnostics.

### Guards the regression

R-1 runs all 12 CardCorrectionApiTests: particularly
`Cards_list_requires_a_filter_and_filters_needs_decision_across_boards` (400 plus
exact decision ID), `Cards_list_updated_since_uses_the_card_update_timestamp_not_creation_time`
(updated row present), `Summary_view_previews_at_a_word_boundary_and_leaves_short_text_whole`
(exact preview/hasMore/sessions), and `Full_board_views_remain_wire_identical_and_do_not_emit_summary_metadata`
(raw full-view equality). The other eight methods retain edit/archive/reopen/move/
limits and error-route behavior.

R-2 runs all eight CardAliasApiTests: exact stored/projected alias, normalized
creation, null/omission/clear distinctions, history and existing validation errors.
R-3 runs both CardDiagnoseScriptTests: POST route and ledger output, and NoWait's
202 without polling, guarding existing CLI dispatch.

R-4 is manual review of AGENTS.md, ops-http, antiphon-api, board-api and the CLI
header against D-6; inspect ASCII bytes and PowerShell 5.1-compatible syntax in the
script. These are not additional test executions or permission for an unlisted build.
No new mirrored source-string tests. Existing regression files stay unchanged.

### Guard inventory

Safety-critical here means a guard against incomplete enumeration being accepted,
scope drift, false search completeness, SQL interpretation of a literal, or private
content entering a public search/result. Ordinary display, framework binding and
nonsecurity token syntax cases also have V/R coverage; no new asynchronous delivery
or authorization guard exists. Independently bypassable refusal branches below have
distinct PCs, including separate token bindings and shell failure conditions.

| Guard | Plan invariant | Positive control |
|---|---|---|
| G-1 | D-3: UUID tie continuation | PC-1 |
| G-2 | D-3: cursor uses last returned row | PC-2 |
| G-3 | D-3: lookahead proves continuation | PC-3 |
| G-4 | D-4: changed fingerprint refuses | PC-4 |
| G-5 | D-4: one snapshot within a request | PC-5 |
| G-6 | D-2: search full description | PC-6 |
| G-7 | D-2: private notes excluded from matching | PC-7 |
| G-8 | D-1: default excludes archived cards | PC-8 |
| G-9 | D-5: automatic page exhaustion | PC-9 |
| G-10 | D-5: truncated requires token | PC-10 |
| G-11 | D-3: strict timestamp boundary | PC-11 |
| G-12 | D-3: timestamp precision retained | PC-12 |
| G-13 | D-4: fingerprint includes IDs | PC-13 |
| G-14 | D-4: fingerprint includes timestamps | PC-14 |
| G-15 | D-4: fingerprint covers the full matching set | PC-15 |
| G-16 | D-3: token board binding | PC-16 |
| G-17 | D-3: token status binding | PC-17 |
| G-18 | D-3: token since binding | PC-18 |
| G-19 | D-3: token archive binding | PC-19 |
| G-20 | D-3: token effective-limit binding | PC-20 |
| G-21 | D-3: token endpoint binding | PC-21 |
| G-22 | D-3: token q binding | PC-22 |
| G-23 | D-1: unfiltered list refusal | PC-23 |
| G-24 | D-1/D-4: total precedes cursor and limit | PC-24 |
| G-25 | D-2: search full terminal reason | PC-25 |
| G-26 | D-2: labels decoded before matching | PC-26 |
| G-27 | D-2: percent is literal | PC-27 |
| G-28 | D-2: underscore is literal | PC-28 |
| G-29 | D-2: backslash is literal | PC-29 |
| G-30 | D-2: q remains a SQL parameter | PC-30 |
| G-31 | D-5: final flag and token agree | PC-31 |
| G-32 | D-5: continuing pages are nonempty | PC-32 |
| G-33 | D-5: immediate token reuse refused | PC-33 |
| G-34 | D-5: nonadjacent token cycles refused | PC-34 |
| G-35 | D-5: duplicate card IDs refused | PC-35 |
| G-36 | D-5: search total stays constant | PC-36 |
| G-37 | D-5: final count equals server total | PC-37 |
| G-38 | D-5: later HTTP failure aborts | PC-38 |
| G-39 | D-5: search 404 has no false empty fallback | PC-39 |
| G-40 | D-5: output waits for complete success | PC-40 |
| G-41 | D-5: archive opt-in preserved on every page | PC-41 |
| G-42 | D-5: literal q preserved on every page | PC-42 |
| G-43 | D-5: updatedSince preserved on every page | PC-43 |
| G-44 | D-5: transport preserves task header | PC-44 |
| G-45 | D-1: page hydration stays bounded | PC-45 |
| G-46 | D-1: empty search cannot silently enumerate | PC-46 |
| G-47 | D-1: oversize search is refused | PC-47 |
| G-48 | D-3: token input stays bounded | PC-48 |
| G-49 | D-2: public result does not leak notes | PC-49 |
| G-50 | D-4: search fingerprint uses search membership | PC-50 |

### Positive controls

The original PC-1 through PC-10 retain their identity. Every row below is separate;
looped subcases do not authorize batching interacting mutations. Mutation runs the
exact method filter `/*/*/Class/Method` with Min=1 and a Class.Method Expect token
for baseline/red/restored-green in separate evidence directories. Code runs ordinary
V/R only. A production HTTP 500 caused by unsafe SQL is an observable status failure;
a compile error, timeout, stub exception or missing barrier is not a PC red.

| PC / guard | Compiling defect in production | Exact method | Required observable failing assertion |
|---|---|---|---|
| PC-1 / G-1 | Drop the equal-timestamp UUID comparison from the keyset predicate | `CardListPaginationApiTests.Equal_timestamps_use_database_uuid_order` (V-3) | complete ordered IDs differ at the tied boundary |
| PC-2 / G-2 | Encode the lookahead row as the cursor boundary | `CardListPaginationApiTests.More_than_500_cards_are_returned_exactly_once` (V-1) | the known boundary ID is missing from the final ordered sequence |
| PC-3 / G-3 | Set truncated when returned count equals limit without lookahead | `CardListPaginationApiTests.Page_boundaries_use_lookahead` (V-2) | the exactly-500 response must have truncated=false and nextPageToken=null |
| PC-4 / G-4 | Skip the fingerprint mismatch ConflictException | `CardListPaginationApiTests.Moving_an_unseen_card_refuses_incomplete_continuation` (V-8) | the continuation must be HTTP 409 with code card_page_changed, but returns 200 |
| PC-5 / G-5 | Use ReadCommitted instead of RepeatableRead for the read transaction | `CardListPaginationApiTests.Fingerprint_and_page_share_a_database_snapshot` (V-11) | after the committed barrier write the first page must be [A,B], but is [C,A] |
| PC-6 / G-6 | Apply the description match to LEFT(Description, SummaryPreviewChars) | `CardSearchApiTests.Description_and_terminal_body_matches_precede_previewing` (V-15) | the description-only late-marker query must return its exact card ID |
| PC-7 / G-7 | Add current PrivateNotes as an OR search term | `CardSearchApiTests.Private_notes_comments_and_revisions_are_not_search_sources` (V-20) | the private-only marker must have zero cards and total=0 |
| PC-8 / G-8 | Remove default ArchivedAt IS NULL from the shared base predicate | `CardListPaginationApiTests.Archived_cards_require_opt_in` (V-5) | the default enumeration must exclude the seeded archived IDs |
| PC-9 / G-9 | Break the shell page loop after the first successful page | `CardListSearchScriptTests.Paging_is_automatic_without_All` (V-26) | the complete ordered IDs and exact final-page request roster must match |
| PC-10 / G-10 | Treat true/missing-token as a successful final page | `CardListSearchScriptTests.Old_or_inconsistent_pagination_fails_without_results` (V-27) | the missing-token subcase must exit nonzero with whitespace-only stdout |
| PC-11 / G-11 | Change UpdatedAt < after to UpdatedAt <= after | `CardListPaginationApiTests.Equal_timestamps_use_database_uuid_order` (V-3) | the full ordered sequence must have no repeated tied-boundary IDs |
| PC-12 / G-12 | Truncate the token timestamp to whole milliseconds | `CardListPaginationApiTests.Equal_timestamps_use_database_uuid_order` (V-3) | the microsecond-boundary subcase must contain every expected ID in order |
| PC-13 / G-13 | Omit UUID bytes from the digest, retaining count/timestamps | `CardListPaginationApiTests.Membership_changes_refuse_continuation` (V-9) | equal-count/equal-timestamp replacement must return 409, not 200 |
| PC-14 / G-14 | Omit timestamp bytes from the digest, retaining UUIDs | `CardListPaginationApiTests.Moving_an_unseen_card_refuses_incomplete_continuation` (V-8) | movement below an unchanged maximum must still return 409 |
| PC-15 / G-15 | Cap the metadata stream at effectiveLimit instead of scanning all matching rows | `CardListPaginationApiTests.Membership_changes_refuse_continuation` (V-9) | deleting a far-unseen row beyond page one/lookahead must return 409, not 200 |
| PC-16 / G-16 | Exclude boardId from normalized token/request comparison | `CardListPaginationApiTests.Tokens_cannot_change_the_query` (V-7) | only-board-changed request must be 422 with errors.pageToken |
| PC-17 / G-17 | Exclude status from normalized token/request comparison | `CardListPaginationApiTests.Tokens_cannot_change_the_query` (V-7) | only-status-changed request must be 422 with errors.pageToken |
| PC-18 / G-18 | Exclude updatedSince from normalized token/request comparison | `CardListPaginationApiTests.Tokens_cannot_change_the_query` (V-7) | only-since-changed request must be 422 with errors.pageToken |
| PC-19 / G-19 | Exclude includeArchived from normalized token/request comparison | `CardListPaginationApiTests.Tokens_cannot_change_the_query` (V-7) | only-archive-changed request must be 422 with errors.pageToken |
| PC-20 / G-20 | Exclude effectiveLimit from normalized token/request comparison | `CardListPaginationApiTests.Tokens_cannot_change_the_query` (V-7) | limit 2 changed to 3 must be 422 with errors.pageToken |
| PC-21 / G-21 | Exclude endpoint kind from normalized token/request comparison | `CardSearchApiTests.Invalid_search_and_changed_search_tokens_are_rejected` (V-19) | an otherwise-unchanged search token with only endpoint kind replaced by list must be 422 with errors.pageToken |
| PC-22 / G-22 | Exclude normalized q from token/request comparison | `CardSearchApiTests.Invalid_search_and_changed_search_tokens_are_rejected` (V-19) | a changed-q request must be 422 with errors.pageToken |
| PC-23 / G-23 | Remove the endpoint's no-board/status/since guard | `CardListPaginationApiTests.Existing_filters_intersect_and_unfiltered_reads_fail` (V-4) | no-filter and token/archive-only requests must be HTTP 400 |
| PC-24 / G-24 | Return the current page count as search total | `CardSearchApiTests.Totals_and_search_pages_cover_all_matches` (V-16) | page one and every continuation must report the full matching Int64 count (605) |
| PC-25 / G-25 | Apply the terminal-reason match to LEFT(TerminalReason, SummaryPreviewChars) | `CardSearchApiTests.Description_and_terminal_body_matches_precede_previewing` (V-15) | the terminal-only late-marker query must return its exact card ID |
| PC-26 / G-26 | Replace decoded-element EXISTS with ILIKE on LabelsJson::text | `CardSearchApiTests.Literal_patterns_and_decoded_labels_do_not_overmatch` (V-18) | embedded-quote/escape and cross-element cases must have the exact IDs and total |
| PC-27 / G-27 | Remove percent escaping from the LIKE pattern builder | `CardSearchApiTests.Literal_patterns_and_decoded_labels_do_not_overmatch` (V-18) | the percent near-miss decoy must not appear; total must be one |
| PC-28 / G-28 | Remove underscore escaping from the LIKE pattern builder | `CardSearchApiTests.Literal_patterns_and_decoded_labels_do_not_overmatch` (V-18) | the underscore near-miss decoy must not appear; total must be one |
| PC-29 / G-29 | Remove backslash escaping while retaining the explicit escape character | `CardSearchApiTests.Literal_patterns_and_decoded_labels_do_not_overmatch` (V-18) | the stored single-backslash marker must return its one exact ID |
| PC-30 / G-30 | Embed q in a SQL string literal before FromSqlRaw, keeping the remaining query valid for ordinary text | `CardSearchApiTests.Literal_patterns_and_decoded_labels_do_not_overmatch` (V-18) | the quote/SQL-looking literal request must be HTTP 200 with its exact single hit, not SQL error or broadened results |
| PC-31 / G-31 | Ignore a nonempty token when truncated=false | `CardListSearchScriptTests.Old_or_inconsistent_pagination_fails_without_results` (V-27) | the false/nonempty-token subcase must exit nonzero with whitespace-only stdout |
| PC-32 / G-32 | Remove the empty-continuing-page refusal | `CardListSearchScriptTests.Old_or_inconsistent_pagination_fails_without_results` (V-27) | the empty/true/new-token subcase must exit nonzero before requesting its offered final page |
| PC-33 / G-33 | Skip visited-token refusal only when the new token equals the previous token | `CardListSearchScriptTests.Old_or_inconsistent_pagination_fails_without_results` (V-27) | the A,A subcase must exit nonzero with no third card request |
| PC-34 / G-34 | Compare only with the previous token instead of the visited-token set | `CardListSearchScriptTests.Old_or_inconsistent_pagination_fails_without_results` (V-27) | the A,B,A subcase must exit nonzero before following A again |
| PC-35 / G-35 | Remove the seen-ID rejection and append duplicates | `CardListSearchScriptTests.Duplicate_ids_and_inconsistent_totals_are_errors` (V-29) | the cross-page duplicate subcase must exit nonzero with whitespace-only stdout |
| PC-36 / G-36 | Remove the cross-page total equality guard | `CardListSearchScriptTests.Duplicate_ids_and_inconsistent_totals_are_errors` (V-29) | a changed second-page total (with final count matching that new total) must still exit nonzero |
| PC-37 / G-37 | Remove the final accumulated-count equality guard | `CardListSearchScriptTests.Duplicate_ids_and_inconsistent_totals_are_errors` (V-29) | both final short/long-count subcases must exit nonzero |
| PC-38 / G-38 | In Invoke-Antiphon swallow a later list HTTP error and return an empty final envelope | `CardListSearchScriptTests.Later_http_failure_and_changed_scope_do_not_emit_partial_json` (V-28) | the page-two 500 case must exit nonzero, not emit buffered page-one cards |
| PC-39 / G-39 | Handle first-search 404 as an empty successful search envelope | `CardListSearchScriptTests.Later_http_failure_and_changed_scope_do_not_emit_partial_json` (V-28) | the search-404 subcase must exit nonzero with whitespace-only stdout |
| PC-40 / G-40 | Write page cards to stdout inside the loop before final validation | `CardListSearchScriptTests.Later_http_failure_and_changed_scope_do_not_emit_partial_json` (V-28) | the later-409 case must have whitespace-only stdout despite a successful first page |
| PC-41 / G-41 | Send includeArchived only on page one of the shell loop | `CardListSearchScriptTests.All_includes_archives_and_resolves_archived_board_names` (V-25) | decoded includeArchived must be true on the second and final card requests |
| PC-42 / G-42 | Append raw q instead of EscapeDataString(q) to the collection URL | `CardListSearchScriptTests.Search_uses_search_endpoint_and_preserves_encoded_query` (V-24) | the decoded q containing plus/ampersand/hash must equal the input on every request |
| PC-43 / G-43 | Omit updatedSince on continuation requests while retaining status scope | `CardListSearchScriptTests.List_updated_since_is_encoded_and_preserved` (V-33) | the second request's decoded timestamp must equal the original UTC timestamp |
| PC-44 / G-44 | Use an empty header dictionary for continuation reads | `CardListSearchScriptTests.Every_page_preserves_base_and_task_header` (V-32) | the second card request must carry the exact synthetic X-Antiphon-Task-Token |
| PC-45 / G-45 | Use the positive requested limit without clamping to Cards.MaxListResults | `CardListPaginationApiTests.Limits_and_invalid_tokens_are_validated` (V-6) | limit=501/int.MaxValue must still return only 500 cards with continuation |
| PC-46 / G-46 | Normalize missing/blank q to empty and bypass its required guard | `CardSearchApiTests.Invalid_search_and_changed_search_tokens_are_rejected` (V-19) | missing and whitespace q must be 422 with errors.q, not 200 |
| PC-47 / G-47 | Remove the 500-character q ceiling check without truncating q | `CardSearchApiTests.Invalid_search_and_changed_search_tokens_are_rejected` (V-19) | 501-character q must be 422 with errors.q |
| PC-48 / G-48 | Remove the 4096-character token input check | `CardListPaginationApiTests.Limits_and_invalid_tokens_are_validated` (V-6) | a >4096-character otherwise-valid JSON/base64url token must be 422 with errors.pageToken |
| PC-49 / G-49 | At summary assembly copy PrivateNotes into the public Description for a notes-bearing card | `CardSearchApiTests.Private_notes_comments_and_revisions_are_not_search_sources` (V-20) | the positive public-hit JSON must not contain its unique private marker |
| PC-50 / G-50 | For search only hash board/status/archive metadata without q, while retaining the correct q-filtered total | `CardSearchApiTests.Search_membership_change_invalidates_the_next_page` (V-21) | changing only a nonmatching Description to match while retaining UpdatedAt must make continuation 409 |

### Out of scope

No ranking change, new schema/index, client UI, browser E2E, full suite, real-board
seeding, provider/runner launch, durable snapshot service, token signing or automatic
read retries. Syntactic token corruption is covered by V-6; the tokens grant no
authority. Request cancellation forwarding and narrow/streamed hydration are inspected
within the named API checks, not by adding a global frozen clock or timing-sensitive
load test. Unicode search is literal PostgreSQL behavior; accent folding and Unicode
normalization equivalence are not promised. Raw malformed LabelsJson/nonarray storage
is excluded: current writes serialize string arrays. Windows PowerShell 5.1 receives
static syntax/ASCII review; the executed shell lane is pwsh on either admitted platform.

### Static checkpoint census

Source census at `85daf142cce144f88347f9f26cfa91ef1021f7ee` found exactly 12, 8 and 2
[Test] methods in CardCorrectionApiTests, CardAliasApiTests and CardDiagnoseScriptTests.
All are unparameterized, with no Arguments/Matrix/Repeat/Skip or inherited test base.
The two API class-data sources each supply one factory; the CLI class has no data
source. A repository-wide class-name census
found no other class with any of those prefixes; new classes must use only the three
exact names below. Keep the trailing class wildcards required by the pinned TUnit
combined-filter hint extractor. PlanTableImporter strips the escaped table pipes and
extracts all named class roster expectations from those parenthesized operands.

The new static roster is 12 list + 10 search + 11 CLI = 33 distinct methods. CP-1
is 12+12=24, CP-2 is 10+8+12=30, CP-3 is 11+2=13. Total is 67 results (55 distinct
methods, with the 12 new list methods repeated). No test-discovery command, build,
TUnit run or mutation was used for this census. Code still must verify the actual
fresh TRX counts/classes; Min is a floor, not permission for omitted or extra classes.

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
location. The Plan-stage inspection found an eligible Linux lane and an eligible Windows
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

All figures are estimates, not measurements. Ordinary Code verification remains
CP-1 12 + CP-2 12 + CP-3 8 = **32 minutes**, including three isolated builds.
The allowed driver bootstrap adds **2 minutes** when needed. Finalized fixture and
test authoring is estimated at **180 minutes**; Code dispatch estimate is therefore
**214 minutes**, plus observed build-slot queue wait.

The guard audit expands the preliminary 10 controls to **50**, with no extra ordinary
test methods or footprint expansion. Mutation uses each PC's exact Class.Method
filter, Min=1, never a class-wide cycle. Estimate **12 minutes per PC**: 4 for baseline
build/run, 4 for compiling red build/run, 4 for restored fresh-build/green. Thus the
separate Mutation execution floor is **600 minutes**, plus **20 minutes** setup and
evidence, for **620 minutes**. These estimates include all three phases; a failed
build or fixture/timeout never counts as red. Schedule enough time or split the
commissioned battery into explicit stage work, without dropping guards.

Combined ordinary/PC execution floor: **632 minutes**. Including bootstrap, authoring
and Mutation setup/evidence: **834 minutes**, plus queues and actual repairs.
No numeric batching saving is claimed: most controls share CardService/CardReadQuery/
CardPageToken or card.ps1, so run interfering mutations separately. The 12 list
regressions deliberately repeated in CP-2 protect S2's shared machinery.
No schema, browser or full-suite setup is included or required.

## Handoff and acceptance

TestDesign is complete: **50 guards, 50 distinct mapped PCs, missing=0, duplicate
PC mappings=0**. Each PC names a compiling production change and an observable
HTTP/data/exit/stream assertion in one exact ordinary method. The barrier, decoded
label fixtures, non-vacuous privacy response and independently coherent shell failure
cases are specified above. Implement all 33 V methods with internal subcase loops.
No tests, builds or mutations were run in this stage.

Next: Code, ordinary Review, confirmed land, then post-land Mutation under the
repository workflow. D-1 through D-6, all exact implementation paths and the single
Checkpoints table remain closed. Code must commit/push each slice, execute its CP
via the checkpoint tool, and report actual counts/receipts. New method/count/path
changes require a committed plan revision before broadening implementation or checks.
Any fixture seam that cannot be realized as specified returns evidence to Plan;
do not replace the real database/script assertions with source-text tests.

After activation, the orchestrator verifies the loaded server SHA from the canonical
checkout and performs read-only searches for known old/body-only cards and a full
board enumeration. Use actual live data only for observation; synthetic >500-card
fixtures belong exclusively in tests. A 409 during a changing board calls for a fresh
enumeration and cannot establish absence of a duplicate. Existing clients may
continue reading one summary page; this card supplies continuation and fixes the
operator/agent duplicate-check front door, without redesigning their UI.
