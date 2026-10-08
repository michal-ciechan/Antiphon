# CARD-1143: back off a Held park whose episode cannot be loaded

Date: 2026-10-08. Plan task: `7bbbb07b-ffc7-4208-96f0-f44029358287`.
Board: Antiphon. Card: CARD-1143 (`a09492c9-293d-431f-98c7-12150c7af440`).
Inspected `origin/master`: **`394229df96f7901a9d60c75b1a62a64c259e1e2b`**, fetched before
inspection and fast-forwarded from the assigned branch base
`b5e78700ae9a76430c13d75cc03399055dd82e59`; `65745cfa` is an ancestor. All source
citations below refer to that inspected SHA, not the older plans' line numbers.

Status: implementation and verification design complete; **next: code**. This dispatch
supplies the requested negative controls, mutation design and executable checkpoint manifest
in this artifact. No production change is made by the Plan task. The source-derived SQL census
was subsequently checked by an isolated PostgreSQL diagnostic: seven command-count checks and
three existing budget-method invocations passed. See the dated measurement amendment below.
The proposed fix has not run; Code must execute its count pins before calling it verified.

## Outcome and scope

Keep an unloadable park Held and record `park_episode_changed` with the existing configured
Held backoff. Retain the park snapshot already read during preparation so this costs no new
SELECT. Apply one conditional UPDATE only to a due Held row at the observed revision. Do not
retire, release, publish, confirm, stop, fail, resume or settle anything on this path.

Production scope is `server/Application/Services/TaskParkPublicationService.cs` and
`server/Application/Services/BlockedTaskParkingService.cs`. Test scope is the existing
`BlockedTaskParkReclaimTests` class (a new partial file is permitted) and
`BlockedTaskParkProjectionTests`. Owner prose is `docs/session-runtime-invariants.md`.
No schema, enum, configuration, runner, dispatcher, API, client or AGENTS.md change is needed.

Platform observations, 2026-10-08 07:51 UTC: both `GET /api/runner-defaults` and
`GET /api/session-runners` succeeded. Defaults revision 2 has a global preference and no
kind overrides; the catalogue contains Windows and Linux lanes, including a draining Linux
runner and an accepting Linux runner. These are observations, not placement instructions.
All checkpoints below are **portable server/PostgreSQL/Git** or **portable metadata/doc**
lanes. Resolve effective placement again at dispatch; omit `-Runner` and `-Platform`.
No physical host or fleet path is embedded in a checkpoint. No Windows-only qualification
or runner deployment is required for this change.

## Ground truth

| Card/brief assumption | What current master does; verified file:line | Consequence |
|---|---|---|
| A due Held park can be selected forever without a new stamp. | `TerminalRunnerSeatReleaseService.cs:79-88` registers then reads the park and admits a due Held row; `:99-109` runs ambiguity detection and `PrepareAsync`. `TaskParkPublicationService.cs:94-95` returns `park_episode_changed` when loading fails, without writing. | Confirmed residual; no release evidence is obtained. |
| Registration would replace a park whose task concurrency token changed. | `BlockedTaskParkingService.cs:27-37` verifies the *current* task token passed by the caller, then returns an existing `(TaskId, Attempt, BlockEventId)` row without comparing its captured token. `TaskParkPublicationService.cs:366-372` later compares the captured token in `SameEpisode`. | Same attempt/event plus changed token is the smallest stable reproduction. Do not alter registration identity to fix pacing. |
| Every candidate-load failure means a deleted episode. | `TaskParkPublicationService.cs:326-342` also rejects missing coordinates, missing/invalid baseline, missing session, runner/store/start/path mismatch, a different newest Blocked event and handoff digest mismatch. | Preserve the existing outward reason; it is not proof of permanent deletion. |
| Deleted/pruned/cross-board rows all cause the same hot sweep. | `AgentTaskPark.cs:8-11` retains historical coordinates without cascading FKs; `AppDbContext.cs:135-158` defines no park FK or board filter. `NextLegacyPageAsync` selects current Blocked **tasks** (`BlockedTaskParkingService.cs:129-142`). A deleted task is not visited. With no remaining Blocked event, registration returns null (`:124-126`, `:30-31`). A newer Blocked event creates/reuses a different park key. `LoadAsync` and `SameEpisode` have no BoardId/CardId predicate. | Do not invent cross-board rejection or an orphan-retirement rule. Deleted session/baseline/digest can keep the current task hot; deleted task/event and a board move need distinct controls. |
| CARD-1135 already restamps all refusals. | Only `HoldAsync` (`TaskParkPublicationService.cs:354-363`) and the explicit binding/ambiguity arms (`TerminalRunnerSeatReleaseService.cs:89-106`) stamp. Direct returns at publication `:31-33`, `:93-102`, `:118`, `:141`, `:153-158` do not. | Cover only unloadable preparation/capture candidates; narrow the overbroad doc sentence. Preserve transient busy, intent-CAS and receipt refusal behavior. |
| Re-reading the park is necessary to stamp it. | The first read of `LoadAsync` already holds `AgentTaskPark` including Id, State, Revision and NextAttemptAt (`TaskParkPublicationService.cs:328-330`). | Retain this snapshot locally; never add a fallback SELECT on a successful or unsuccessful load. |
| Held to Held is a state transition. | `Allowed` omits it (`BlockedTaskParkingService.cs:216-224`). `StampHeldAttemptAsync` updates only NextAttemptAt, ReasonCode and UpdatedAt, with `(Id, Revision, State == Held)` (`:164-175`). | Add a due guard for the new stamp; preserve State, Revision, HeldFromState and every evidence field. |
| CARD-1082 provides an hourly retirement precedent. | Its final F3d explicitly withdrew retirement. `SettlementSyncRecoveryService.cs:26-27`, `:78-85` restamps Held debt hourly without Git or task outcome changes. The 2026-10-07 CARD-1082 follow-ups plan's F3d supersedes its earlier path-based retirement design. | Copy the conservative principle, not the separate debt service's 60-minute cadence or transaction graph. |
| Reclaim runs on every dispatcher tick. | Pool-release calls `ReclaimScheduledAsync` (`AgentTaskDispatcher.cs:7856-7862`); `TerminalRunnerSeatReleaseService.cs:204-229` gates overlap and interval, then calls `(32,3)`. Disabled exits before any query. Raw `ReclaimLegacyAsync` is deliberately ungated. | Measure the raw visit separately from the scheduled tick; neither cadence nor page/cursor behavior changes. |
| Held attention must gain a warning row. | Slot projection copies the current owner's park `ReasonCode` (`SeatDesktopJoin.cs:116-132`). Release attention is built from existing release ledgers (`AttentionService.Leaks.cs:16-57`); a Held park with no release id does not itself create that attention item. | Persist the reason for existing projections. Do not promise new attention, append events or manufacture release ledgers. |
| Parking supplies a timeout for a waiting session. | `BlockedTaskParkingOptions.cs:6-15` defaults Enabled/ReclaimExisting off, interval 120 and Held backoff 600. `TerminalRunnerSeatReleaseOptions` defaults automatic release off (`TerminalRunnerSeatReleaseService.cs:14-18`). Owner sentences at `docs/session-runtime-invariants.md:68-79` say no automatic release deadline while dormant. | Default-off remains inert. A backoff is not a seat-release deadline. |
| The desktop hot-path budgets can increase to accommodate this fix. | `DelegationDispatchRecoveryBoundaryTests.cs:25-45` pins held-dispatched tick 18, working-live tick 18 and inside-grace absent scan 4 with `FullCommandCounter`. | No increase; run this exact method. No new dispatcher or reconciliation query. |
| Known limits still name CARD-1129 and CARD-1135. | On this master, `docs/session-runtime-invariants.md:121-126` separately describes their landed behavior; Known limits names CARD-1097 item 2, CARD-1104, CARD-1143 and CARD-1154. `BlockedTaskParkProjectionTests.cs:91-95` pins it. | Preserve the two landed guarantees and remove only CARD-1143's residual clause once implemented. Do not restore old plan wording. |

Paths in abbreviated source citations above are under `server/Application/Services/`, except
entities under `server/Domain/Entities/`, settings under `server/Application/Settings/`,
`AppDbContext.cs` under `server/Infrastructure/Data/`, and named tests under
`tests/Antiphon.Tests/Application/`.

### Reproduction and statement census

Use the existing real-PostgreSQL `RunnerSeatReleaseFixture` with parking, reclaim and automatic
release enabled only in the fixture; real scratch Git and fake bound runner transport. Make one
blocked task's source dirty, invoke reclaim until its park is Held `park_dirty`, and prime the
cursor to that task id. Keep its Attempt, latest Blocked event, Result and session fixed. Update
only the task's ConcurrencyToken in a separate context. Advance the fake clock to NextAttemptAt.
Registration returns the same park; `SameEpisode` fails, `LoadAsync` returns null, `PrepareAsync`
returns Held/`park_episode_changed`, and neither Git inspection nor the release coordinator is
reached. At master, both NextAttemptAt and `park_dirty` remain stale. Another visit repeats.

This reproduction also ran against an isolated test database (measurement amendment below),
never a live session or production DB. The census counts executing DbCommands, including `ExecuteSql` task locks and UPDATEs; transaction
begin/commit messages are not DbCommands. It deliberately differs from
`CountingCommandInterceptor`, which records only readers. Use `FullCommandCounter`
(`tests/Antiphon.Tests/TestHelpers/FullCommandCounter.cs:7-9,40-82`) for executed validation.

| Boundary, one existing report-backed park with nonempty worktree path | Master census | Planned census | Why |
|---|---:|---:|---|
| `PrepareAsync` or `CaptureSourceIdentityAsync`, token mismatch | 4 reads | 4 reads + 1 UPDATE = 5 | Park, task, session, newest Blocked event; Result digest needs no transcript read; retain park snapshot. |
| `TryHandleTaskAsync`, due token mismatch | 11 reads + 1 task lock = 12 | 11 reads + 1 task lock + 1 UPDATE = 13 | Task + settlement event (2); Register task/block/park + lock (4); park (1); ambiguity (1); load (4). |
| `TryHandleTaskAsync`, next visit inside Held backoff | 12 again for the broken row | 6 reads + 1 task lock = 7 | Returns at the existing Held gate before ambiguity and candidate loading. |
| Raw `ReclaimLegacyAsync(32,1)`, one Blocked task, warmed cursor after that id, due mismatch | 22 reads + 2 locks + cursor UPDATE = 25 | 26 | Count + cursor + forward page + wrap (4); RegisterLegacy (6); Handle (12/13); task + release query (2); cursor UPDATE (1). |
| Same raw sweep, next visit inside backoff | 25 again | 20 | Registration/page/cursor overhead remains; saves ambiguity + four candidate reads. |
| Scheduled call before interval / parking disabled | 0 reclaim commands | 0 | Existing early gates remain. |
| Existing dispatcher budget method, three arguments | 18 / 18 / 4 | 18 / 18 / 4 | No production change on these paths. |

With default 120-second sweep and 600-second backoff, five visits to this one row cost 125
commands at master versus 106 after the fix (26 + 4 x 20). This is a derived command saving,
not a CPU-time measurement. Page wrap adds one SELECT; an empty initial cursor subtracts one
from the raw-sweep numbers. A missing/invalid baseline ends Load after two reads; a missing park
ends after one; transcript-backed digest evaluation can add a reader. Do not universalize 25
to those shapes, the three-row C1135 fixture, or the dispatcher tick.

V-4 must log measured totals and a SQL roster on failure. It must verify each command's role,
not just assert a lower total. If an unrelated landed change alters a fixture count, explain
and remeasure at that base; do not silently raise 18/18/4 or swap an exact count for a ceiling.

## Decisions

These are evidence-backed implementation decisions; no operator choice is outstanding.

### D-1. Reuse the Held backoff; retain the unresolved row

Use `ReclaimHeldBackoffSeconds` (default 600; zero/negative leave NextAttemptAt null) and
`park_episode_changed`. Do not introduce `Unresolvable` or `Superseded`. A failed load cannot
prove permanent loss, and this change must preserve which episodes can later publish/release.
State, Revision, HeldFromState, captured task/session/generation/source/report coordinates,
publication and release receipts, sync state and accepted-answer coordinates stay unchanged.
The bounded reason is visible through the existing park projection; no new warning is required.

Rejected: 60 minutes copied from settlement debt (unnecessary second cadence overriding the park
setting); a new terminal state (changes recovery/admission semantics and enum consumers);
repairing the old token or captured digest (would authorize different evidence); deleting parks
or excluding them from paging (changes traversal and historical custody). No migration is needed:
ReasonCode, NextAttemptAt and UpdatedAt and the relevant index already exist.

### D-2. Separate preparation loading from read-only proof loading

Split the existing loader internally so its first park SELECT can be retained by a preparation
wrapper. One reasonable shape is an overload `LoadAsync(AgentTaskPark? park, ct)` containing the
current remaining checks, the existing `LoadAsync(Guid, ct)` as a read-only wrapper, and a new
`LoadForPreparationAsync(Guid, ct)` which reads the park once, calls the same checks, and stamps
only a null candidate from a due Held snapshot. No DI or public result DTO change.

Use the new preparation wrapper in all three existing preparation reads: Capture entry, Prepare
entry, and Prepare's reload after successful identity capture. Each call keeps exactly the old
SELECT sequence and short-circuit order. Keep `VerifyAsync`, `ReadEvidenceAsync`, `AcceptAsync`,
`LockAndLoadAsync` and `PersistIntentAsync` on read-only loading. A successful capture normally
transitions to Requested; if the subsequent candidate disappears, the Held-only stamp writes
nothing and Prepare still returns `park_episode_changed`. Never turn a Requested/Published row
into Held on a null-candidate path.

Rejected: a write inside generic `LoadAsync` (would mutate during receipt verification and inside
transactions); a second lookup after Load returns null (extra SQL and a newer, unbound snapshot);
stamp every returned Held result (changes busy/intent/receipt outcomes); skipping `SameEpisode`
or producing a Candidate from partial evidence (fail-open).

### D-3. A due-guarded metadata CAS makes repeat calls harmless

Add a narrow internal `StampUnloadedHeldAttemptAsync(Guid parkId, long revision,
CancellationToken ct)` in `BlockedTaskParkingService`, using the same no-ambient-transaction /
no-dirty-tracker guard as the existing stamp plus Enabled. Capture the clock for the due test;
compute the ordinary Held backoff for `park_episode_changed`. Its single ExecuteUpdate predicate
is `(Id, Revision, State == Held, NextAttemptAt == null || NextAttemptAt <= observedNow)`.
It writes only NextAttemptAt, ReasonCode and UpdatedAt. Reuse private field-setting code if useful,
but **do not change the existing stamp's other callers or refusal cadence**. Also skip a known
future-due snapshot before attempting the UPDATE. No new transaction, lock, SELECT or entity
attachment is needed.

Concurrent stale snapshots cannot postpone an already-restamped positive backoff: the database
due predicate rejects the second writer even though Revision is deliberately unchanged. A
different state/revision or a deleted park matches zero rows. Zero/negative backoff deliberately
permits repeated visits, as it does today; do not claim idempotence of UpdatedAt in that disabled
backoff mode. Exactly at the due instant is eligible. Restore a loadable episode and it retries
through the original policy at the next due visit.

The stamp affects historical park metadata only, even if the task disappeared or became Working
after the snapshot. It never mutates that task or its session and confers no publication authority.
Caller result remains Held/`park_episode_changed` even if the CAS loses. Exceptions retain today's
exception/cancellation handling; they are not converted into successful evidence or a permanent
absence verdict. A failed write can therefore remain hot until the DB recovers, as other failed
stamps do today.

Rejected: bumping Revision (a scheduling stamp is not an episode transition); broadening
`PersistStateAsync` to admit Held-to-Held (would change HeldFromState); omitting the due predicate
(allows duplicate callers to keep moving the deadline); task/session status writes to retire debt.

### D-4. No telemetry or rollback side effects

Use the persisted park reason; add no `AgentTaskEvent`, `AgentIncident`, caller message, new
attention kind or per-tick warning. Existing log/event infrastructure remains best effort and must
not change the disposition. This fix uses AsNoTracking reads and ExecuteUpdate only, so it adds
no tracked entity to detach and no multi-write transaction to roll back. V-10 injects an UPDATE
failure and performs a later unrelated save in the same context to prove no park write leaks.
If implementation introduces tracking or a transaction despite this design, every rollback path
must explicitly detach its own changed entities and prove the same later-save assertion; do not
clear a caller's unrelated tracker state as recovery.

### D-5. Preserve the waiting-session and release contract

This plan changes the retry pacing of a session that can wait for input. What releases it and
when: **nothing automatically releases an input-waiting seat while parking is disabled**
(CARD-1083). When all existing gates are explicitly enabled, a Blocked seat can release only
after current publication evidence, the conditional runner proof and confirmed ledger result;
the legacy proof includes the existing 120-second server-anchored idle window. There is no
unconditional release deadline. Neither 120-second scheduling nor 600-second backoff promises
release. Working sessions are never released, stopped or failed by this change; CARD-0079's
separate, explicit Check-compaction recovery is untouched.

### D-6. Avoid active source intersections; activate server code once landed

| In-flight work named in the brief | Intersection and coordination |
|---|---|
| CARD-1151 S1-S3, Code `4adef3e9`: AgentTaskDispatcher boot-stall tail and BootStall partial | No production intersection. Read/run the existing budget method but do not edit dispatcher or BootStall files. Do not adopt the older prompt-only Working characterization as a new requirement here. |
| CARD-1149/1150 S2 repair 2: DispatchBriefEvidence.cs, SessionMessageQueueService.DispatchBrief.cs, AgentSessionService | No production intersection. `DelegationDispatchRecoveryBoundaryTests` is a regression-only dependency; do not change its assertions or expected budgets. |
| CARD-1153 runner files; S4 gated on AgentTaskDispatcher.cs | No runner or dispatcher edits. Reuse the landed baseline tests as read-only regression coverage. No runner restart or S4 activation is part of this card. |
| Park follow-ups landing concurrently | `docs/session-runtime-invariants.md`, its `BlockedTaskParkProjectionTests` pins, and possibly the reclaim class are shared. Re-read these at Code start; preserve other cards' newer sentences and tests. Serialize overlapping edits. |

S1 changes server code and requires the canonical AppHost restart after review/land, under the
owner restart runbook and activation policy; confirm `/api/version` equals the landed SHA.
S2 and S3 are tests/docs and need no restart. Code/Plan restart nothing. No setting is enabled
for rollout. Reverting the two service changes restores old pacing; it does not undo receipts
or require a down migration. Existing stamped deadlines can expire normally after rollback.

## Implementation slices

Run S1 -> S2 -> S3 on the task branch. Each slice is 30-60 minutes including its listed checks;
commit and push before executing the group. No source edits while a group runs. A failure-driven
fix is committed before rerunning the affected row; no assertion is weakened or deleted.

| Slice | Files and changes | Named tests/checkpoints | Budget / restart |
|---|---|---|---|
| S1 | `server/Application/Services/TaskParkPublicationService.cs`: retain snapshot and the preparation wrapper (D-2). `server/Application/Services/BlockedTaskParkingService.cs`: dedicated due CAS (D-3). Make `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.cs` partial; put new methods in `BlockedTaskParkReclaimTests.Unloadable.cs`, retaining class attributes and existing helpers. | V-1..V-4; CP-1..CP-4. Primary reproduction, both entry points, exact CAS/time boundary and SQL census. | 35 author + 19 checks = 54 min. Server restart after land. |
| S2 | `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.Unloadable.cs`: negative/control methods. Use existing fixture's `configureDb`, fake clock, scope and wire; no shared fixture default changes. Any safety correction discovered stays confined to S1's two services. | V-5..V-10; CP-5..CP-10. Run missing/changed evidence, default-off/busy/unknown, Working and warning absence, read-only proof and write-failure controls. | 33 author + 17 checks = 50 min. No restart unless a production repair is necessary. |
| S3 | `docs/session-runtime-invariants.md` and `tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs`: exact sentences below and pins. Record measured S1 costs in this plan by a dated amendment if needed, without rewriting historical evidence. | V-11, R-1..R-8; CP-11..CP-19. All named park, terminal, admission, budget, doc and registry regression coverage at final committed source. | 15 author + 40 checks = 55 min. No restart. |

No new test class means no new Slow allowlist or classification entry. The partial retains
Integration/Slow, `NotInParallel("MessageQueue")` and `ParallelLimiter<ProcessSpawnLimit>`
from the existing reclaim class.
If fixture additions become unavoidable, scope them to an optional argument with today's default;
do not change timeout, clock, runner or PostgreSQL defaults for other tests.

## Documentation sentences with pins

Edit only the park paragraphs around `docs/session-runtime-invariants.md:124-126`. Preserve
CARD-1129/1147 confirmation counts, the server-anchored window and all default-off sentences.
Use `BlockedTaskParkProjectionTests.C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` for exact
case-sensitive text pins; retain all other checks. Changing a prose pin to the corrected
sentence is part of this specified documentation change, not permission to weaken behavior tests.

| Pin | Exact sentence to add/replace |
|---|---|
| `c1135-held-restamp` (replace its overbroad sentence) | A refusal routed through HoldAsync on a Held row re-stamps NextAttemptAt from the same backoff and records the refusal reason (CARD-1135). |
| `c1143-unloadable-held` (new) | When PrepareAsync or CaptureSourceIdentityAsync loads a due Held park but cannot load its episode, it records park_episode_changed and the same configured Held backoff without changing the park state, revision, publication receipt or release ledger (CARD-1143). |
| `c1143-no-repeat` (new) | With a positive Held backoff, a repeated or concurrent unloadable-episode refusal does not move an already future NextAttemptAt; zero or negative backoff still disables the delay. |
| `c1143-refusal-scope` (new) | Disabled or busy entry, intent-CAS and receipt refusals keep their existing behavior; proof verification stays read-only on an unloadable episode. |
| `c1141-known-limits` (replace current full sentence, preserving any newly landed unrelated clauses) | Known limits stay on CARD-1097 item 2 (resume reads the desktop checkout, not the runner mirror), CARD-1104 for a remote parent with RunnerCwd, and CARD-1154 (the local 409 can name Reply for a confirmed published park whose release identity no longer matches). |

Keep existing `c1108-held-backoff`, default-off/Working/input-wait pins. No new promise that
every Held outcome is stamped, that an orphan receives a new attention row, or that any wait
ends after the backoff. Do not rewrite the older CARD-1108/1124 or CARD-1082 plan decisions:
this plan and its owner sentences are the correction to CARD-1135's remaining scope.

## Verification design

### Inspection

Read both referenced 2026-10-07 follow-up plans, especially CARD-1108/1124 D-1 (the in-memory
Requested reset after intent persistence) and CARD-1082 F3d (withdrawn retirement), before Code.
Current source inspected for this plan: the complete publication and park-storage services;
terminal coordinator entry, reclaim/page/gate, release lookup, settlement-event and ambiguity
paths; dispatcher pool-release hook; park mapping/entity/options; the owner park paragraphs;
the C1108/C1135 reclaim tests and fixture seams; publication and identity fixtures; release
attention/projection; the three dispatcher budget rows and FullCommandCounter. These are the
authorities behind the ground-truth and census tables, not the old plans' source assumptions.

Tests must drive production code with isolated real PostgreSQL and scratch Git/fake runner
transport. Scope every counter around the operation only; seed, arrange, cursor priming and
assertion reads occur outside it. Normalize fake timestamps to PostgreSQL microseconds. No
wall-clock sleep, provider CLI, production runner or production database is needed.

### Proves it works now

All new behavior tests below belong to `BlockedTaskParkReclaimTests` in the new partial file.
Each method has one result unless its arguments are explicitly enumerated. Internal subcases
are assertion arms, not additional result counts. Capture original task/session, receipts,
source tip and wire/Git counters before every negative arm.

| V | Method and results | Required assertions and negative control |
|---|---|---|
| V-1 | `BlockedTaskParkReclaimTests.C1143_UnloadableHeldEpisodeRestampsAndSkipsNextSweep` (5 arguments: task-token, runner-store, accepted-start, handoff-digest, baseline-missing) | Create a real dirty Held row, change the named bound fact only, then run due reclaim. Same park id/State/HeldFromState/Revision; reason becomes park_episode_changed, UpdatedAt equals due visit, NextAttemptAt equals now+600; no receipt or release created; no Git/wire publication/stop. Next sweep at +120 leaves metadata unchanged and issues no candidate-load reads. A later due sweep advances by 600 again. Working neighbor and scratch source retained. |
| V-2 | `BlockedTaskParkReclaimTests.C1143_UnloadablePreparationEntryPointsRestamp` (2 arguments: prepare, capture) | Call each public service entry directly against a Held stale-token row in a fresh scope; no second park lookup, correct outward Held reason and durable stamp. Future-due repeat preserves all metadata. Include missing park -> same Held return, no UPDATE. For Prepare also inject a task-token change immediately after a successful intent commit during capture; its second load fails with a Requested row, which remains Requested and unstamped. This arm pins the second wrapper call's non-Held behavior. |
| V-3 | `BlockedTaskParkReclaimTests.C1143_RestampCasIsDueBoundedAndIdempotent` (4 arguments: revision-race, state-race, already-restamped, deleted-row) | Exercise the dedicated metadata CAS with an observed old snapshot, then alter the database in another scope before it runs. Losing writer changes zero metadata and never inserts. In already-restamped arm, same Revision plus future deadline is protected. Include just-before due, exact due and null due at a fixed fake clock; zero and -1 backoff retain null and no new transition. Equality is due, future is not; changing only Id cannot stamp a neighbor. |
| V-4 | `BlockedTaskParkReclaimTests.C1143_UnloadableSweepStatementBudget` (1) | Reproduce the precise one-row/warmed-cursor census. FullCommandCounter records direct preparation 5, due Handle 13, backed-off Handle 7, due raw sweep 26, backed-off raw sweep 20. Independently arrange each measurement so earlier calls do not consume the deadline. Match SQL roles, one park UPDATE on due, none on follow-up; Git inspections and wire publication/release/force counters stay zero. Five default-interval visits total 106. Print counts/roster, not source/report text. |
| V-5 | `BlockedTaskParkReclaimTests.C1143_LoadableEpisodeAndOtherRefusalsKeepTheirBehaviour` (1) | Loadable dirty park still returns park_dirty through normal inspection/re-request; remove dirt and advance to due to exercise original publication and conditional release gates. Another loadable row refused as park_other_writer retains that reason/backoff; workspace reservation retains immediate retry. Assert expected revision transition after PersistIntent (the C1135 Requested reset), not just a Held result. Existing C1108/C1135 tests also remain unchanged. |
| V-6 | `BlockedTaskParkReclaimTests.C1143_MissingAndReplacedEpisodesStayFailClosed` (1) | Distinguish missing task (not paged), no Blocked event (not registered), missing session (existing Held metadata may restamp), newer Blocked event (new key, old historical park untouched by normal sweep), and move the card/agent board without changing episode coordinates (still loadable). Direct preparation of an orphaned Held row is a refusal, never authority. Assert no FK/cascade assumptions and no cross-board selection introduced. |
| V-7 | `BlockedTaskParkReclaimTests.C1143_DisabledBusyAndUnknownKeepExistingBehaviour` (1) | Flip Enabled off after seeding Held; preparation/capture return park_disabled_or_busy with zero commands and raw/scheduled reclaim return None. Automatic release off retains its existing no-release behavior. Separately test ambient transaction and dirty tracked caller: same early refusal and unchanged caller entries. Inject a load DbException/cancellation: no stamp, no reclassification as durable absence, same exception/cancellation behavior. A loadable unknown runner/inspection response retains the existing Unknown outcome/reason and HoldAsync policy. |
| V-8 | `BlockedTaskParkReclaimTests.C1143_WorkingSessionAndWarningsAreUntouched` (1) | Change the task to Working after it acquired a Held park; raw reclaim excludes it. Direct stale-park preparation may only stamp historical metadata: task status/token/failure/completion, session status/generation, queue and all release records are byte-equivalent; no stop/kill/force, no new child/attempt. Repeated due and backed-off refusals add zero Warning events, incidents or caller notes and no release attention duplicates. Read slot projection after a normal Blocked restamp and assert the stored park reason is visible. |
| V-9 | `BlockedTaskParkReclaimTests.C1143_ReadOnlyProofReadersNeverRestamp` (1) | With a due Held stale episode, call VerifyAsync, ReadEvidenceAsync and AcceptAsync with retained otherwise-valid evidence. Each refuses as today with zero UPDATE/INSERT, unchanged park and zero new Git/wire release calls. Repeat under a transaction: proof reads cannot acquire the new write path. |
| V-10 | `BlockedTaskParkReclaimTests.C1143_RestampFailureLeavesNoTrackedWrite` (1) | A DbCommandInterceptor throws before the due park UPDATE. Caller retains the existing refusal/exception handling, no release or warning row is invented. Remove the fault, save an unrelated sentinel in the same DbContext and reload from a fresh one: park remains unchanged and no added/modified park entity exists. A subsequent fresh successful due visit stamps normally. Check any explicit rollback paths detach their own entities. |
| V-11 | `BlockedTaskParkProjectionTests.C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` (existing 1) | Exact owner sentences/pins above, no old CARD-1143 residual text, unchanged default-off, Working and waiting-session pins; the other projection method remains green. |

### Guards the regression

| R | Existing class/method scope and invariant |
|---|---|
| R-1 | Whole `BlockedTaskParkReclaimTests`: idle proof, cursor fairness, gating, Held retries, this-run confirmation accounting, rollback accounting and single-verify behavior. 12 existing results + 18 new = 30. |
| R-2 | Whole `TaskParkPublicationTests` (3) and `TaskParkRunnerIdentityTests` (2): source mode, identity capture, policy refusal, intent and receipt CAS, no accidental side effects in proof verification. |
| R-3 | Whole `TerminalRunnerSeatReleaseTests` (39): terminal admission, exact release receipts, caller delivery, Working refusal, reply and warning/attention behavior. |
| R-4 | Whole `RemotePoolFollowUpAdmissionTests` (3): existing 422/409 and local/explicit-worktree admission. No AgentTaskService edits. |
| R-5 | Whole `BlockedTaskParkReleaseTests` (3), `BlockedTaskParkResumeTests` (7) and `BlockedTaskParkDeliveryTests` (6): release, source sync and exact continuation/caller receipt behavior; park-resume dedupe remains independent. |
| R-6 | Whole park doc class `BlockedTaskParkProjectionTests` (2) and `RunnerBranchContractDocumentationTests` (7), including CARD-1082 Held debt prose; registry `TestClassificationGuardTests.Registry_matches_compiled_metadata` (1) plus `SlowTestTripwireTests` (2). No whole-Unit selection. |
| R-7 | Exact `DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets` (3 inspected arguments): 18, 18, 4. Do not alter its production paths, expected values or incoming tests from CARD-1149/1150/1153. |
| R-8 | Whole `DispatcherSweepLifetimeRegistrationTests` (2): production wiring still receives both independent sync sweeps and keeps shared state. This boots the guarded test host, never the production runner. |

Counts above were counted from current source, not discovery output. At Code start re-read the
selected classes; if concurrent work added arguments/methods, amend the roster explicitly and
retain every named baseline result. Require the actual executed names as well as Min; zero
tests, skipped Pending placeholders, a fixture error or a missing method is not coverage.

### Guard inventory

| G | Production guard / decision | Detectors | Mutation |
|---|---|---|---|
| G-1 | Null candidate in either preparation entry routes through retained-snapshot restamp, D-2 | V-1, V-2 | PC-1, PC-2 |
| G-2 | Fixed refusal reason and existing backoff, no episode transition, D-1/D-3 | V-1, V-3 | PC-3, PC-4 |
| G-3 | Id/revision/Held/due CAS, D-3 | V-3 | PC-5..PC-8 |
| G-4 | No extra lookup and existing Held early gate, D-2/D-3 | V-4 | PC-9, PC-10 |
| G-5 | Existing policy and successful-candidate behavior, D-2 | V-5, R-1, R-2 | PC-11 |
| G-6 | Disabled/busy/unknown and read-only proof paths, D-2/D-4 | V-7, V-9 | PC-12, PC-13 |
| G-7 | Historical metadata only; Working custody and no warning flood, D-4/D-5 | V-8 | PC-14, PC-15 |
| G-8 | No tracked write leak after failure, D-4 | V-10 | PC-16 |
| G-9 | Accurate owner sentences, D-5 | V-11 | PC-17 |

### Positive controls

Post-land SourceLanding Mutation only: each row is an independent compiling defect, baseline /
expected assertion red / restored green, one **method**, never a class or suite. The PC filters
deliberately name exact methods without wildcard, as required for mutation. Ordinary checkpoints
below use trailing wildcards for pinned-runner discovery. Shared-file controls run sequentially.
Fixture/build/zero-count/timeout failures do not count as red. Restore every mutation and keep
per-PC external evidence; do not commit from a SourceLanding snapshot.

| PC | Single defect | Exact detecting filter | Expected assertion failure |
|---|---|---|---|
| PC-1 | Prepare entry uses old read-only loader, bypassing its null-candidate stamp. | `/*/*/BlockedTaskParkReclaimTests/C1143_UnloadableHeldEpisodeRestampsAndSkipsNextSweep` | Due row retains park_dirty / past deadline. |
| PC-2 | Capture entry uses old read-only loader. | `/*/*/BlockedTaskParkReclaimTests/C1143_UnloadablePreparationEntryPointsRestamp` | Capture arm retains stale reason/deadline. |
| PC-3 | New UPDATE omits ReasonCode. | `/*/*/BlockedTaskParkReclaimTests/C1143_UnloadableHeldEpisodeRestampsAndSkipsNextSweep` | Reason stays park_dirty. |
| PC-4 | New stamp uses 3600 instead of the configured 600 seconds. | `/*/*/BlockedTaskParkReclaimTests/C1143_UnloadableHeldEpisodeRestampsAndSkipsNextSweep` | NextAttemptAt differs from exact now+600. |
| PC-5 | Remove Revision from new CAS. | `/*/*/BlockedTaskParkReclaimTests/C1143_RestampCasIsDueBoundedAndIdempotent` | Revision-race arm's newer row metadata changes. |
| PC-6 | Remove Held from new CAS. | `/*/*/BlockedTaskParkReclaimTests/C1143_RestampCasIsDueBoundedAndIdempotent` | State-race arm's Requested row is stamped. |
| PC-7 | Remove database due predicate, leaving snapshot check. | `/*/*/BlockedTaskParkReclaimTests/C1143_RestampCasIsDueBoundedAndIdempotent` | Already-restamped concurrent row's future deadline/UpdatedAt moves. |
| PC-8 | Use `< observedNow` rather than `<= observedNow` in due CAS. | `/*/*/BlockedTaskParkReclaimTests/C1143_RestampCasIsDueBoundedAndIdempotent` | Exact-due arm is left stale. |
| PC-9 | Add one park SELECT after null candidate to recover the snapshot. | `/*/*/BlockedTaskParkReclaimTests/C1143_UnloadableSweepStatementBudget` | Direct preparation 6 instead of 5; due sweep 27 instead of 26. |
| PC-10 | Bypass TryHandleTaskAsync's existing Held future-deadline early return. | `/*/*/BlockedTaskParkReclaimTests/C1143_UnloadableSweepStatementBudget` | Follow-up has ambiguity/load reads and exceeds 20. |
| PC-11 | Remove in-memory `park.State = Requested` after PersistIntent. | `/*/*/BlockedTaskParkReclaimTests/C1143_LoadableEpisodeAndOtherRefusalsKeepTheirBehaviour` | Loadable dirty retry remains Requested without its ordinary new hold. |
| PC-12 | Admit preparation when Enabled is false (remove the Enabled check in both entry gates and the new stamp gate as one disabled-policy defect). | `/*/*/BlockedTaskParkReclaimTests/C1143_DisabledBusyAndUnknownKeepExistingBehaviour` | Disabled arm issues queries/stamps instead of zero-command park_disabled_or_busy. |
| PC-13 | Make read-only LoadAsync delegate to the preparation wrapper, preserving the shared read core so it does not recurse. | `/*/*/BlockedTaskParkReclaimTests/C1143_ReadOnlyProofReadersNeverRestamp` | Proof reader emits UPDATE and changes historical metadata. |
| PC-14 | New null-candidate handler updates the task to Failed or session to Stopped. | `/*/*/BlockedTaskParkReclaimTests/C1143_WorkingSessionAndWarningsAreUntouched` | Working task/session snapshot changes (no provider process needed). |
| PC-15 | Append and save a Warning event for each park_episode_changed result. | `/*/*/BlockedTaskParkReclaimTests/C1143_WorkingSessionAndWarningsAreUntouched` | Event delta is nonzero / repeated warnings. |
| PC-16 | Attach a changed park entity before ExecuteUpdate and leave it attached when that command fails. | `/*/*/BlockedTaskParkReclaimTests/C1143_RestampFailureLeavesNoTrackedWrite` | Later unrelated SaveChanges leaks reason/deadline into park. |
| PC-17 | Restore the old broad c1135 sentence and old CARD-1143 Known-limits clause in the owner doc. | `/*/*/BlockedTaskParkProjectionTests/C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` | c1135-held-restamp / c1141-known-limits / new scope pins reject stale prose. |

### Out of scope

Orphan cleanup/retirement, new attention infrastructure, repairing episode coordinates, changing
which parks are published/released/confirmed, new wait deadlines, enabling parking, any Working
stall recovery, CARD-0079, CARD-1148 debt registration identity, CARD-1154 local reply guidance,
and CARD-1151/1149/1150/1153 implementation. The earlier cards' mutation obligations remain theirs.
No full assembly or whole-Unit run is authorized by this plan.

### Cost

Ordinary V/R estimate: S1 19 + S2 17 + S3 40 = **76 minutes**, sum of the rows below.
Authoring estimate 83 minutes; total three slices about **159 minutes** plus a one-time
5-minute checkpoint-driver bootstrap allowance. These are estimates, not measured runtimes.
Post-land Mutation: 17 sequential method-scoped controls, allow about 90-120 minutes.
Do not turn either cost into a test-count floor or reduce scope to fit a dispatch timeout.

### Checkpoints

Closed ordinary Code list. Lane is encoded in Group: `pg-*` means portable isolated PostgreSQL
and scratch Git/fake transport, `meta-*` portable docs/metadata. Every row is serial, including
all PostgreSQL classes; no other row or build runs in flight. TUnit's existing process-spawn
limiter remains in place within the reclaim class. One build per After group, then one narrow
filter per behavior/regression row. Reuse only within that same After group and committed SHA.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1143-s1/` | pg-unloadable | `/*/*/BlockedTaskParkReclaimTests/C1143_UnloadableHeldEpisodeRestampsAndSkipsNextSweep*` | V-1 | exact 5, 0 failed/skipped | 5 | 8 | true |
| CP-2 | S1 | CP-1 | pg-entry-points | `/*/*/BlockedTaskParkReclaimTests/C1143_UnloadablePreparationEntryPointsRestamp*` | V-2 | exact 2, 0 failed/skipped | 2 | 3 | true |
| CP-3 | S1 | CP-1 | pg-due-cas | `/*/*/BlockedTaskParkReclaimTests/C1143_RestampCasIsDueBoundedAndIdempotent*` | V-3 | exact 4, 0 failed/skipped | 4 | 4 | true |
| CP-4 | S1 | CP-1 | pg-park-budget | `/*/*/BlockedTaskParkReclaimTests/C1143_UnloadableSweepStatementBudget*` | V-4 | exact 1, 0 failed/skipped | 1 | 4 | true |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1143-s2/` | pg-other-refusals | `/*/*/BlockedTaskParkReclaimTests/C1143_LoadableEpisodeAndOtherRefusalsKeepTheirBehaviour*` | V-5 | exact 1, 0 failed/skipped | 1 | 7 | true |
| CP-6 | S2 | CP-5 | pg-missing-replaced | `/*/*/BlockedTaskParkReclaimTests/C1143_MissingAndReplacedEpisodesStayFailClosed*` | V-6 | exact 1, 0 failed/skipped | 1 | 2 | true |
| CP-7 | S2 | CP-5 | pg-gates-unknown | `/*/*/BlockedTaskParkReclaimTests/C1143_DisabledBusyAndUnknownKeepExistingBehaviour*` | V-7 | exact 1, 0 failed/skipped | 1 | 2 | true |
| CP-8 | S2 | CP-5 | pg-working-warnings | `/*/*/BlockedTaskParkReclaimTests/C1143_WorkingSessionAndWarningsAreUntouched*` | V-8 | exact 1, 0 failed/skipped | 1 | 2 | true |
| CP-9 | S2 | CP-5 | pg-proof-readers | `/*/*/BlockedTaskParkReclaimTests/C1143_ReadOnlyProofReadersNeverRestamp*` | V-9 | exact 1, 0 failed/skipped | 1 | 2 | true |
| CP-10 | S2 | CP-5 | pg-write-failure | `/*/*/BlockedTaskParkReclaimTests/C1143_RestampFailureLeavesNoTrackedWrite*` | V-10 | exact 1, 0 failed/skipped | 1 | 2 | true |
| CP-11 | S3 | `tests/Antiphon.Tests -> bin-c1143-final/` | meta-park-docs | `/*/*/(BlockedTaskParkProjectionTests*)\|(RunnerBranchContractDocumentationTests*)/*` | V-11, R-6 | exact 9, 0 failed/skipped | 9 | 5 | true |
| CP-12 | S3 | CP-11 | pg-reclaim-regression | `/*/*/BlockedTaskParkReclaimTests/*` | R-1, V-1..V-10 | exact 30, 0 failed/skipped | 30 | 10 | true |
| CP-13 | S3 | CP-11 | pg-publication-regression | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)/*` | R-2 | exact 5, 0 failed/skipped | 5 | 3 | true |
| CP-14 | S3 | CP-11 | pg-terminal-regression | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-3 | exact 39, 0 failed/skipped | 39 | 7 | true |
| CP-15 | S3 | CP-11 | pg-followup-regression | `/*/*/RemotePoolFollowUpAdmissionTests/*` | R-4 | exact 3, 0 failed/skipped | 3 | 2 | true |
| CP-16 | S3 | CP-11 | pg-park-release-resume | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-5 | exact 16, 0 failed/skipped | 16 | 6 | true |
| CP-17 | S3 | CP-11 | pg-desktop-budget | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | R-7 | exact 3, 0 failed/skipped; totals 18/18/4 | 3 | 3 | true |
| CP-18 | S3 | CP-11 | pg-sweep-registration | `/*/*/DispatcherSweepLifetimeRegistrationTests/*` | R-8 | exact 2, 0 failed/skipped | 2 | 3 | true |
| CP-19 | S3 | CP-11 | meta-registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | R-6 | exact 3, 0 failed/skipped | 3 | 1 | true |

Execution recipe (no host pin):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1143-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1143-driver/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1143-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-08-card-1143-held-park-unloadable-episode-plan.md --after S1 --expected-source-sha "$(git rev-parse HEAD)"
```

S1/S2/S3 are literal After tokens; use `--after S2` and `--after S3` on their committed slices.
Run once per group, then own and await the run (`wait` while exit 75) until final completion.
Exit 4 means slot timeout/not run, never bypass the lease. Preserve unedited CHECKPOINT lines,
SHA, actual counts, dirty/sourceState/buildSource and run provenance in the stored report.
No additional build/test driver without a stated failure-driven reason. PCs remain post-land;
ordinary Code does not run broad mutation loops. Verify inherited failures by the failing method
at the base, not the whole assembly; never widen timeouts or weaken assertions.
Code and Review run `scripts/check-evidence-diff.ps1` over the complete task range. Generated
TRX/JSON/logs stay ignored. Remove only this task's producer-owned `bin-c1143-*` outputs.

## Plan validation and handoff

Plan inspected the card, current source, referenced owner docs and both follow-up plans; read
live runner defaults/catalogue; counted the exact narrow regression rosters and SQL call paths.
The checkpoint tool was built through `scripts/build-slot.ps1` at the inspected source SHA:
build succeeded, 0 errors, 1 existing CS8602 warning in `TaskOwnerGuard.cs:170`. Its importer
accepted all 19 rows (exit 0). Generated YAML remains ignored. Ordinary implementation acceptance
is reserved for Code's manifest. No CPU-time benchmark is claimed.

### Measurement amendment, 2026-10-08 08:09 UTC

After the initial plan commit, a foreground, inherited diagnostic executable used the existing
`RunnerSeatReleaseFixture` through reflection, `FullCommandCounter`, real scratch Git and the
fixture's fake bound runner. It invoked the two public publication entries, `HandleAsync` and
`ReclaimResultAsync`, and then the three existing budget arguments directly. PostgreSQL was a
fixture-owned container with isolated cloned databases, explicitly disposed at completion.
The runner/check-interpreter/diagnose/distiller/Hangfire environment was pinned to the existing
test guard's disabled/dead-runner values. No real Program host or provider process was started.

Source at both build and execution: `06163abfa61b4be78915c34405d982f289cbae58`, the initial
plan-only commit. `git diff 394229df96f7901a9d60c75b1a62a64c259e1e2b..06163abfa61b4be78915c34405d982f289cbae58 -- server tests`
is empty. Source stayed unchanged through the run. The ignored diagnostic is not a production
fix, a new committed test, a TUnit/Checkpoint receipt or a claim that CP-1..CP-19 ran.

Commands (the diagnostic artifacts/logs are intentionally ignored):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1143-cost-probe-build -- dotnet build .antiphon/c1143-probe/Probe.csproj --property:OutputPath=bin-c1143-probe/ --property:UseAppHost=false --nologo
SessionRunner__BaseUrl=http://127.0.0.1:1 Delegation__CheckInterpreterEnabled=false Delegation__DiagnoseEnabled=false Delegation__OutputDistillerEnabled=false Hangfire__ServerEnabled=false pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1143-cost-probe-run -- dotnet .antiphon/c1143-probe/bin-c1143-probe/Probe.dll
```

Build: exit 0, 0 errors, 645 warnings across the referenced test/project graph, 142 seconds
holding its granted build slot. Run: exit 0, seven exact command-count checks plus three budget
argument invocations, zero failures, 48 seconds holding its granted slot. These are diagnostic
counts, not TUnit executed-result counts. The future-deadline controls were arranged with a
direct fixture-only UPDATE after proving the stale row stayed unchanged; they test the existing
gate, not the unimplemented restamp. The planned due totals of 5/13/26 remain the measured
4/12/25 plus the proposed single UPDATE.

Diagnostic artifact SHA-256 provenance (payloads remain ignored):

| Artifact under `.antiphon/c1143-probe/` | SHA-256 |
|---|---|
| `Program.cs` | `0bbd8e8343c6423fcb932e91630ae3a56332fa566326bc6f525d824bc24f7c8a` |
| `Probe.csproj` | `30037a0abbc6aaf966e7ecd6e3968769870092f97492db0e75ab920a98007648` |
| `build.log` | `5802c8a8d5aa3980b52027ab852acf61a9db56b041a6a0270fbea8f3688781d8` |
| `run.log` | `2fd800ff10dd63ad3f4d1fd23bf636675410f2e8f0047c9332228af37bec4cb2` |

Essential unedited output:

```text
BUILD SLOT granted lease=b8dfe352-1563-47ec-911b-4f7c90f4b97e waited=0s maxcpucount=6
C1143-MEASURE prepare-stale-token commands=4 expected=4
C1143-MEASURE capture-stale-token commands=4 expected=4
C1143-MEASURE handle-due commands=12 expected=12
C1143-MEASURE sweep-due commands=25 expected=25
C1143-MEASURE sweep-next-still-hot commands=25 expected=25
C1143-RESIDUAL reproduced unchanged-due=true unchanged-revision=true stale-reason=park_dirty release-ledgers=0
C1143-MEASURE handle-future-manual-control commands=7 expected=7
C1143-MEASURE sweep-future-manual-control commands=20 expected=20
C1149-BUDGET held-dispatched-tick total=18
C1149-BUDGET working-live-tick total=18
C1149-BUDGET inside-grace-absent-scan total=4
C1143-DIAGNOSTIC completed command-checks=7 existing-budget-arguments=3 failures=0
BUILD SLOT released lease=b8dfe352-1563-47ec-911b-4f7c90f4b97e held=48s
```

Code starts with S1 on current landed source, preserving these decisions and rechecking any
changed cited file. Bind the dispatch to this artifact's pushed plan commit and section
`### Checkpoints`. Review checks ordinary evidence and the pending method-scoped mutation
design; only after review and confirmed land should the caller commission SourceLanding
Mutation for PC-1..PC-17. Activate the server slice through the canonical restart owner, keeping
parking/automatic-release defaults off.

### Code amendment, 2026-10-08 (task `cab80ac2`)

S1 measured the planned census exactly at `91f43ceb35e25bd42eb7bb8aa59770616a71942c`
(CP-4 stdout): `prepare-due 5`, `capture-due 5`, `handle-due 13`, `handle-backed-off 7`,
`sweep-due 26`, four `sweep-backed-off` visits of 20, five default-interval visits 106. The due
raw sweep reads the park four times (RegisterLegacy, TryHandle's Register, the Held gate and the
preparation load); the backed-off sweep three. One implementation detail differs from D-3's
sketch: `StampUnloadedHeldAttemptAsync(parkId, revision, observedNextAttemptAt, ct)` takes the
retained snapshot's deadline so the known-future skip happens inside the same method that
captures the clock; the database predicate is unchanged. No other decision changed.
