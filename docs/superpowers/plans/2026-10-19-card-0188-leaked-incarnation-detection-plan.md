# CARD-0188: detect unowned historical standing incarnations

Date: 2026-10-19. Stage: Plan. Task: `b99543e0`.
Inspected start ref: `00c620f25747f879cdf0fad067c827576d001a7e`.
Assigned branch: `feat/card-task-b99543e0`. Landing target: **master only**.

An otherwise eligible live session must produce `SessionUnowned` when no
agent's current `PersistentSessionId` points to it, even if the session retains
a historical `StandingAgentId`. Extend the existing read-time Attention
projection. Do not stop, kill, release, reassign, or restart a session.

This plan follows both passes in
[the investigation](../../investigations/2026-10-19-card-0188-leaked-incarnation.md).
That document is already in the starting history and must land with this branch.
Preserve its historical results and costs. It establishes a deterministic
visibility gap, not the cause of the August replacement or a present live leak.
The desktop investigation found no current leaked standing incarnation.

The brief does not fold TestDesign into Plan. **Next: test-design** to append
the executable verification design and checkpoint manifest before Code. The
constraints and proposed selections below are inputs to that stage, not claims
of executed verification. Code has a **45-minute budget**, one red-first witness
and one narrow green invocation.

## Ground truth

Paths and symbols below were inspected at the start ref; line numbers are navigation
hints, not future source identity.

| Card assumption or question | What the code does | Design consequence |
|---|---|---|
| A standing stamp means someone still owns the session. | `server/Domain/Entities/AgentSession.cs:7` explicitly defines `StandingAgentId` as historical physical ownership, with no cascading FK. | Retain the stamp as history; never use it alone to suppress this warning. A missing historical agent must not prevent detection. |
| Current ownership is what should suppress the warning. | `AttentionService.Leaks.cs:128`, `BuildSessionUnownedItemsAsync`, first excludes all stamped rows. Only then does it parse every non-null `Agent.PersistentSessionId` into a GUID set. | Remove the stamp exclusion and retain the all-agent pointer check. Do not restrict it to the stamped agent, AlwaysOn agents, or a particular provider. |
| Every unpointed Running row is currently warned. | Candidates also require no card, no `EndedAt`, `StartedAt < now - 10 minutes`, a Starting/Running/Stopping status, and no Queued/Dispatched/Working/Blocked task for that session. | Preserve these existing eligibility rules. An open task or card remains ownership for this projection; a terminal task does not. |
| The warning proves that the process is Running. | `RemoteLeakConfirmed` at `AttentionService.Leaks.cs:232` returns true for local rows without inspecting local inventory; remote rows need a directory, no pending/unknown inventory, and membership in its live set. `AttentionService.cs:273` already obtains inventory for other projections. | This is the existing database-live warning locally, not new process census evidence. Preserve remote confirmation and do not claim a local Exited runner entry alone suppresses a DB-Running row. |
| The ten-minute rule measures time since pointer loss. | The cutoff uses `StartedAt`; no ownership-loss timestamp exists. | An already old session can warn on the next feed read after losing its pointer. Do not add a timer, stored episode or migration. |
| A separate attention kind or frontend is needed. | The existing item has Warning severity, `session-unowned:{id:N}` identity, a session ID, and only `OpenDrawer`. `client/src/features/attention/attentionVisuals.ts` already routes this kind to session inspection. | Reuse that contract. Historical identity goes in evidence, not in the DTO's current `AgentId`. |
| Each leak condition needs a separate row. | `BuildSessionLeakItemsAsync` groups by session; stop-stuck wins over pool-unreleased, then unowned, with other evidence attached. | Preserve priority and deduplication; a higher-priority row may carry the unowned evidence. |
| Tests already protect the intended ownership rule. | `AttentionServiceTests.C691.cs:245`, `a_live_session_an_agent_names_is_not_unowned`, currently treats setting only the standing stamp as ownership. Its four cases passed during the investigation. | Update that obsolete case and add a real A-running/B-current regression witness. Do not reinterpret the historical green receipt as proof of the new behavior. |
| An unclaimed session may be reclaimed automatically. | The session owner document says unclaimed never implies kill and stalls are detection states. The existing projection is read-only. | Assert no mutation and zero runner kill calls; leave reconciliation, release and supervision untouched. |

## Decisions

- **D-1 — Reuse `SessionUnowned`.** It already supplies severity, stable condition
  identity and session inspection. A dedicated leaked-incarnation enum would
  duplicate routing and deduplication for the same actionable condition. Reject
  a new kind, client change, endpoint, scheduler, migration or configuration knob.
- **D-2 — Current pointers override historical stamps.** Remove only
  `s.StandingAgentId == null` from candidate eligibility. Keep GUID parsing and
  the current-pointer set across all agents. Reject clearing stamps, inferring
  ownership from agent existence, or comparing only the historical agent's
  pointer: another current agent can legitimately own A.
- **D-3 — Preserve the existing warning population and thresholds.** Apply that
  removal within the existing Starting/Running/Stopping projection; Running A
  is the required regression. This consistently corrects the historical-field
  interpretation without inventing different ownership semantics for Starting
  and Stopping. Retain card, task, age and ended exclusions. Reject immediate
  warnings for new launches or a new ten-minute ownership-loss episode.
- **D-4 — Preserve liveness evidence boundaries.** Remote unknown, pending or
  absent inventory still withholds this warning. Local database-live behavior
  stays as implemented. Reject additional runner RPCs and process probes: they
  would change availability and false-positive behavior beyond the confirmed
  stamp defect. A locally Exited process with a stale Running database row is
  still eligible under the existing projection until reconciliation updates it.
- **D-5 — Keep the item truthful and inspectable.** Use the headline
  `Live session has no agent pointer, card or open task.` Retain the existing
  entity label, severity, action and condition key. When stamped, append
  `historicalStandingAgentId=<guid>` to evidence with an explicit statement
  that it is historical, not a current pointer. Leave `AgentId` null so the
  action targets A rather than G's current session B. Do not claim the process
  was leaked by supervision or that G still exists.
- **D-6 — Detection has no release authority.** No writes to session state,
  ownership, tasks, incidents or runner custody are part of this change. No
  automatic kill, stop, input, restart, fresh launch or pool release. Reject
  adding cleanup to reconciliation or treating the warning as permission to
  operate on A. The plan creates no new session or input wait; there is no new
  release deadline. With parking disabled, waiting sessions retain their seats
  without an automatic release deadline (CARD-1083).
- **D-7 — Keep verification bounded and publication master-only.** Use one
  committed red witness followed by one focused green selection. Do not run a
  full assembly, real provider, production session, native pty or browser test
  for this projection change. Master publication requires ordinary Review;
  deployment/RC activation is outside this brief. TestDesign remains a separate
  stage because folding was not authorized in the brief.

These are implementation decisions within the commissioned scope; no pending
product decision is needed before TestDesign.

## Implementation slices

### S1 — Commit and demonstrate the regression

Files:

- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs`: add
  `a_historical_standing_incarnation_without_a_current_pointer_is_unowned` and
  the small fixture access needed to retain its fake runner and list A and B.
  Reuse `CensusWorld`, its isolated PostgreSQL schema and `FakeTimeProvider`.
- `tests/Antiphon.Tests/Application/AttentionServiceTests.cs`: give the existing
  `FakeRunnerClient.KillAsync` a call counter before its refusal, so swallowed
  exceptions cannot hide a kill attempt. Retain its refusing behavior and all
  other fake-client behavior. Expose the retained fake to the witness.

The fixture must represent standing G rather than accidentally qualifying as a
pool leak: non-pool G, historical A stamped with G, a distinct current B, G's
pointer at B. A is database-Running, unended, older than ten minutes, cardless
and has no open task; the fake local runner lists A and B Running. B is current
and must not produce `SessionUnowned`. Use the public `AttentionService.GetAsync`
feed and select by A/B identity, not just an internal helper.

The decisive assertion is exactly one A `SessionUnowned` warning. With unchanged
production code it must fail because the historical-stamp predicate excludes A.
Commit and push S1 before the red run; retain the source-qualified receipt.
A compile error, fixture failure or zero tests is not the intended red.

The same witness should assert the stable session condition key, inspection-only
action, truthful historical evidence, repeat-read stability, unchanged G -> B
pointer and A/B state, and zero kill calls. Control phases reattach a current
pointer to A and later mark A terminal/ended; each removes A's unowned item.
The terminal control must change database state as well as fake runner state,
per D-4. These checks follow the initial regression assertion and become
reachable in the green run; TestDesign binds their precise assertions.

### S2 — Fix the projection and finish the narrow green

Files:

- `server/Application/Services/AttentionService.Leaks.cs`: remove the historical
  stamp exclusion, update the headline and append historical evidence when
  present. Keep all other candidate filters, pointer parsing, remote liveness,
  item identity/actions and leak precedence unchanged. Add a short explanation
  beside the predicate if needed to prevent restoring the mistaken guard.
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs`: change the
  existing `"standing"` case of
  `a_live_session_an_agent_names_is_not_unowned` to set both the historical
  stamp and a current pointer. Keep its `"agent"` noncanonical GUID spelling,
  queued-task and ended controls. In
  `remote_leak_rows_require_confirmed_liveness`, stamp the session before
  removing its historical owner so its unknown/pending/gone/confirmed checks
  also exercise the newly admitted population, including deleted G.
- `docs/ops-http.md`, `SessionUnowned` description: replace the standalone
  standing-owner exclusion with current-pointer ownership and describe the
  historical stamp as evidence only.
- `docs/session-runtime-invariants.md`, CARD-0691 attention paragraph: clarify
  current ownership and that the ten-minute age is session age. Preserve the
  existing no-kill invariant.

Commit and push S2 before the green checkpoint. Test the new witness together
with the three named existing methods below. Record actual counts and source
provenance, remove producer-owned alternate outputs, and keep generated
receipts gitignored. Do not edit the investigation's historical observations.

No changes are planned in `AgentControlService`, `AgentSupervisorService`,
`SessionReconciliationService`, `TerminalRunnerSeatReleaseService`, domain
entities, migrations, runner implementation, or client code.

## TestDesign handoff and checkpoint constraints

Read the concrete methods and fixtures before finalizing the manifest. Append
the required verification design to this file without rewriting these decisions.
Bind the decisive warning assertion, current-pointer and terminal controls,
zero-kill/state-preservation checks, stamp evidence, existing remote gates and
age boundary to named verification/regression cases. Design later mutation
controls separately under the stage contract; they are not extra Code runs.

The intended Code execution is two invocations, with a fresh isolated build for
each committed slice. TestDesign must state how the intentional red receipt is
recorded without treating it as a green gate. It must finalize the parser-valid
`### Checkpoints` table and exact method roster before handing off to Code.

| Proposed checkpoint | After | Lane | Selection | Expected result | Estimated minutes including build |
|---|---|---|---|---|---:|
| CP-1, red witness | S1 | Managed application integration; isolated PostgreSQL and fake runner; Linux or Windows | Exact method `AttentionServiceTests.a_historical_standing_incarnation_without_a_current_pointer_is_unowned` | One executed, one intended missing-warning assertion failure; no fixture/build failure | 6 |
| CP-2, narrow green | S2 | Same managed application integration lane | New witness plus `a_live_session_no_agent_names_is_unowned`, `a_live_session_an_agent_names_is_not_unowned`, `remote_leak_rows_require_confirmed_liveness` | Proposed roster: 1 + 2 + 4 + 3 = 10 executions, all pass, none skipped | 6 |

Use method-segment OR syntax for CP-2 as documented in
[testing-and-build](../../testing-and-build.md#combined-class-filters-card-0403),
not OR between full paths or an entire `AttentionServiceTests` class filter.
CP-1's exact selection is
`/*/*/AttentionServiceTests/a_historical_standing_incarnation_without_a_current_pointer_is_unowned`.
Both rows build `tests/Antiphon.Tests` into distinct, owned `bin-<name>/` outputs
with a forward slash. Set `TUNIT_MAX_PARALLEL_TESTS=1` for this fixture family.
The checkpoint driver supplies `UseAppHost=false` on Linux. No fakeclaude apphost
or real runner is needed by these methods. Use the checkpoint tool through the
documented host build-slot gate, preserve its unedited receipt lines, and await
each run to completion. Slot refusal is not permission for an unleased run.

Platform discovery performed on 2026-10-19 at 22:16 UTC:
`GET /api/runner-defaults` returned revision 2, a global preference and no
per-kind overrides. `GET /api/session-runners` showed that preferred runner
unavailable/draining, with an eligible Linux runner and an eligible Windows
runner elsewhere in the catalogue. These are observations, not placement pins.
Re-read both routes before dispatch. Omit `-Runner` and `-Platform`; this lane
requires neither a particular host nor a particular OS. If clearing an inherited
OS pin is necessary, `-Platform Any` unpins. Never dispatch to an ineligible host
or embed today's fleet location into the checkpoint commands.

Provisional Code budget: 20 minutes authoring, 12 minutes for the two build/test
invocations, 5 minutes for receipts/publication/output cleanup, and 8 minutes
reserve = **45 minutes**. These are estimates. The prior investigation's single
four-case method took 1m18.610s after a corrected build of 1m18.98s; it is a cost
reference only, not a receipt for this plan. TestDesign must reconcile its final
selection with this budget rather than add broad coverage silently.

## Acceptance and remaining limits

The implementation is acceptable when stamped unpointed A produces the existing
warning, current B does not, a real current pointer suppresses A's warning, the
terminal/ended control clears it, remote uncertain/absent inventory remains
excluded, and the two reads leave session/agent state and runner custody intact.
An overlapping higher-priority leak retains the existing merged-evidence rule.
The old stamp-only suppression assertion must no longer survive as a requirement.

This proves the attention projection against stored state and controlled runner
evidence. It does not recreate the missing August launch history, prove that
automatic Fresh caused it, discover DB-rowless processes, or certify a fleet
inventory. Projection reads are not an atomic ownership snapshot; transient
warnings during concurrent pointer changes remain possible and confer no action
authority. No waiting-session lifecycle is added or changed.

Plan-stage validation: source/fixture inspection and runner-default/catalogue GETs
only; no build, test, production-session operation or deployment is required.

--- next stage ---
next: test-design
handoff: Append the verification design and exact two-run checkpoint manifest for this detection-only SessionUnowned fix: one committed red A-running/B-current witness, then one narrow green within the 45-minute Code budget. Preserve current-pointer ownership, remote liveness gates, zero kill/state mutation, and master-only landing with the investigation.
artifact: docs/superpowers/plans/2026-10-19-card-0188-leaked-incarnation-detection-plan.md

## Verification design

TestDesign task `8c883b09`, inspected source
`8c831a4360f202ef153eadae7b48f107cecdd424`. This appendix completes the
historical TestDesign handoff above; D-1 through D-7 and S1/S2 remain unchanged.
No tests or builds ran in TestDesign. The only intended pre-fix execution is
Code's CP-1. Every case below is an implementation obligation, not an existing
passing receipt. Ordinary scope is Final/Full for this bounded change.

### Inspection

- `AttentionServiceTests.C691.cs`: all test bodies, `CensusWorld.CreateAsync`,
  `FeedAsync`, `RowsAsync`, `RemoveOwnerAsync`, disposal and `CensusDirectory`
  | stamp admission, ownership, age, remote inventory, overlap -> V-1–V-5,
  R-1–R-8. `RemoveOwnerAsync` deletes both the task and agent; the primary
  witness must **not** call it because G and its pointer to B must survive.
- `AttentionServiceTests.cs`: class attributes, `Running`, `BuildService`,
  `CreateContext`, all `FakeRunnerClient` methods and
  `Scenario.AddInProgressCardAsync` | runner observation and safe card seeding
  -> V-1, V-3, R-2, R-5, R-7. The Scenario helper uses the shared database:
  copy its minimal Project/Board/BoardColumn/Card seed shape into `w.Db`,
  never invoke that helper against the isolated fixture.
- `TestDbFixture.cs`: assembly hooks, context-options overloads,
  `CreateIsolatedSchemaAsync`, `IsolatedTestSchema.DisposeAsync` | fixture
  isolation -> all integration cases. Despite the historical type name, this
  creates a migrated **cloned database**, not a shared search-path schema.
- `AttentionService.Leaks.cs`: all leak builders, `RemoteLeakConfirmed`,
  `ParseSessionId`, `IsOpenLeakTask`; `AttentionService.cs`: constructor and
  public projection's inventory/leak assembly | query and DTO boundaries ->
  V-1–V-5, R-1–R-8. Other attention builders are outside this change.
- `AgentSession`, `SessionStatus`, `AgentIncident`, `Board`, `BoardColumn`,
  `Card` | historical stamp, live/terminal enum and FK-valid test inputs ->
  R-2, R-3, R-5, R-7. No entity/migration edits.
- `PlanTableImporter.ImportMarkdown`, `SplitRow`, `RosterTokens`,
  `AfterSelector` and the testing owner's manifest, CARD-0403, source
  qualification, build-slot and Mutation sections | exact two-row table,
  escaped OR, counts, red receipt handling and post-land obligations below.
  `docs/project-context.md`, orchestration stage/D-2 rules, the CARD-0691
  paragraphs in both documentation owners, and the investigation's preserved
  findings/costs were also inspected. No historical outcome is relabelled.

Method bindings (all belong to `Antiphon.Tests.Application.AttentionServiceTests`):

| Alias | Exact method | Executions in CP-2 | Status at inspected source |
|---|---|---:|---|
| H | `a_historical_standing_incarnation_without_a_current_pointer_is_unowned` | 1 | New S1 test, no arguments |
| U | `a_live_session_no_agent_names_is_unowned` | 2 | Existing `remote=false,true` |
| O | `a_live_session_an_agent_names_is_not_unowned` | 4 | Existing `agent,standing,task,ended` |
| L | `remote_leak_rows_require_confirmed_liveness` | 3 | Existing `unknown,pending,gone` |

Keep these argument rosters. Additional phases below are ordinary assertions
inside these methods, **not** extra TUnit executions. New H retains
`[NotInParallel("C691Census")]`; run the whole family with
`TUNIT_MAX_PARALLEL_TESTS=1`. The four methods total **10**, not the number of
internal phases or assertions. Fresh TRX must identify exactly these methods
and their argument rows; wildcard suffixes must not admit another method.

Fixture binding for S1/S2: retain a single fake runner for the lifetime of each
`CensusWorld`, let it list A and B, and preserve existing `RunnerLive` behavior
for all other callers. Count `KillAsync`, `SendInputAsync`, and `StartAsync`
**before** their existing `NotSupportedException`; do not turn refusals into
successes. A mutable backing list can satisfy the existing init-only `Sessions`
property. Allow `FeedAsync` to omit the directory for the L null-directory
phase, defaulting to today's directory. None of these helpers starts a runner.

H recipe: use `w.Session` as A, retain the fixture's terminal Failed task,
make G non-pool and AlwaysOn, stamp A with G, and insert distinct B with G's
current pointer at B. Both rows are Running, unended, an hour old and cardless;
the retained fake lists both Running. Keep `LastSeenAt=Now` except during the
explicit overlap phase. Scope every assertion to A/B IDs and relevant kinds.
Do not assert that the entire attention feed is empty: unrelated projections
such as RecentFailure may legitimately produce rows.

### Delivery inventory

**No new or changed asynchronous delivery path.** This is an awaited read-time
projection: persisted AgentSession/Agent/AgentTask rows and cached inventory ->
`AttentionService.GetAsync` caller -> returned `AttentionDto.Items`, joined by
`SessionId=A.Id` and `ConditionKey=session-unowned:{A.Id:N}`. Existing database
commits are the input boundary; the projection persists no output. A later
read recomputes the item, so there is no enqueue, recovery journal, recipient
queue or acknowledgement to certify.

Receipt for this scope is the caller inspecting A's actual returned DTO.
The fake local runner substitutes for process inventory; `CensusDirectory`
substitutes for cached remote liveness. Neither proves a process exists or
receives input. The public service test substitutes for HTTP/client rendering
and cannot prove browser receipt or routing. Those paths are unchanged.
Busy/already-eligible recipient and crash/enqueue recovery tests therefore do
not apply. There is no session input to match to a complete UserPrompt; zero
input calls is instead required. If Code adds any asynchronous notification or
input, this inventory is invalid: return to Plan for the real queue and complete
recipient-transcript seam, rather than accepting a request/event/Sent flag/ack.

### Proves it works now

All V/R entries below use the real service plus isolated PostgreSQL integration
layer; H/U/O/L expand to the exact methods above and CP-1/CP-2 below.

- V-1: historical A warns and current B does not | H, CP-1 then CP-2 | recipe
  H; variant local A-running/G->B; case initial read; oracle
  `feed.Items.Where(i => i.Kind == SessionUnowned && i.SessionId == A.Id)`
  has exactly one item, while the same selection for B is empty; reachability
  first outcome assertion, before metadata or control phases; classification
  missing-warning regression. CP-1 must fail **this** single-item assertion
  because the collection is empty; CP-2 must pass it.
- V-2: warning is truthful and inspectable | H, CP-2 | same recipe, first and
  second reads; assert Warning, `SessionId=A.Id`, `AgentId=null`,
  `TaskId=null`, `Actions=[OpenDrawer]`, exact condition key, unchanged entity
  label and ModelKind, headline `Live session has no agent pointer, card or open task.`,
  and `historicalStandingAgentId=<G.Id in D format>` plus explicit historical/
  non-current wording in evidence. Second read retains the key and one A row;
  reachability follows V-1; classification DTO-contract regression.
- V-3: detection cannot alter custody | H, CP-2 | before the first read,
  snapshot persisted scalar values of all A/B sessions, agents and tasks and
  the incident rows in the isolated database; after **each** read compare
  fresh `AsNoTracking` results from a separate context, sorted by ID. Include
  status, timestamps, stamp, pointer, pool fields and row membership, not just
  tracked objects or row counts. Assert each runner mutation counter is zero.
  Reachable on the two baseline reads before test-owned control writes;
  classification safety regression. Compare against a fresh baseline around
  any later control read too, so deliberate fixture writes are not blamed on
  the projection. Also require no Added/Modified/Deleted tracked entries left
  by a read; a transient write-and-restore between observations is outside the
  persisted-snapshot oracle and remains a source-review responsibility.
- V-4: owner disappearance and remote confirmation work together | L, CP-2 |
  stamp the session with its existing owner **before** `RemoveOwnerAsync`, save,
  then use the existing unknown/pending/gone argument phases. Missing owner
  must not cause an exception or suppress the final confirmed-live warning;
  receipt is exactly one stamped SessionUnowned row after confirmation, carrying
  the historical ID. Reachability follows the existing pool checks; classification
  newly admitted remote population regression.
- V-5: condition resolves by ownership/state on the next read | H/O, CP-2 |
  reattach G->A, assert no A unowned row, restore G->B and assert A returns;
  finally set A Stopped **and** EndedAt, update its fake runner entry to Exited,
  and assert no A unowned row on two reads. Keep G->B/B unchanged. Reachability
  follows the initial two reads; classification resolution regression, not a
  claim that local runner Exited alone filters a DB-Running session.

### Guards the regression

Reset each changed field to the eligible baseline and prove the A warning
returns between negative phases. This makes every exclusion independently
reachable and prevents a previous guard from masking a removed guard.

- R-1: stamp alone becomes ownership again | H recipe/initial case, V-1
  single-item assertion; CP-1 is the natural pre-fix red and CP-2 the fixed
  green. Classification: introduced regression witness.
- R-2: valid current owners are falsely warned | H/O | O's `standing` case
  sets both stamp and pointer; retain `agent`'s uppercase compact GUID. H
  additionally inserts a distinct non-pool, non-AlwaysOn agent of a different
  provider pointing to A while G still points to B, then removes that pointer.
  Oracle: A absent while either current owner names it and present on removal;
  B remains absent. Reachability after V-1; classification ownership safety.
- R-3: ended/status exclusions mask one another | H/O | retain O's Running
  plus non-null EndedAt case independently. H varies only Status with
  EndedAt null: Starting/Running/Stopping each warn (LastSeenAt fresh),
  Created/Stopped/Failed each do not. Then restore Running. Oracle selects
  A's unowned row, never runner status alone. Classification eligibility safety.
- R-4: open task status or session association lost | O `task` | loop
  Queued/Dispatched/Working/Blocked for the task linked to A, each suppresses;
  return to Failed after each and require the warning. A terminal task and an
  open task linked to a distinct session must not suppress A. Classification
  ownership safety; baseline warning first makes the branch reachable.
- R-5: card or session-age boundary lost | H/U | H seeds a valid local
  Project/Board/Backlog column/Card in `w.Db`, binds only A.CardId and requires
  absence, then unbinds and requires presence. In U retain both local/remote
  unstamped baselines and exact ten-minute equality exclusion; repeat the age
  phase with a historical stamp whose agent is absent. Test ages 9 minutes,
  exactly 10, and 10 minutes plus 1 millisecond: absent, absent, present.
  Use the fake clock and stored StartedAt, no sleep. Classification threshold/
  ownership safety. A's old age after pointer removal proves this is not a new
  ten-minute ownership-loss episode.
- R-6: remote uncertainty becomes liveness | L | existing three arguments
  have respectively unknown+live, pending+live, or known-not-live, with all
  other gates open. Each excludes stamped A; clearing only that condition
  admits it. After confirmation, omit the directory on one further feed read:
  A must be absent; restore it and require presence. Classification liveness
  safety. No `Resolve`/inventory RPC may be introduced (the fake refuses them).
- R-7: read acquires cleanup authority | H | V-3's independently asserted
  session, agent, task, incident snapshots and Kill/Input/Start counters.
  Baselines bracket test-owned edits. Classification custody safety; swallowed
  fake exceptions cannot hide attempts because counters increment first.
- R-8: newly admitted A duplicates or displaces a stop warning | H | after
  other controls, set A Stopping with LastSeenAt six minutes ago and no owner;
  select only A and the three leak kinds. Exactly one row must remain,
  `Kind=SessionStopStuck`, key `session-stop-stuck:{A.Id:N}`, with
  `Also SessionUnowned` and A's historical evidence. Restore Running/fresh
  LastSeenAt and require V-1 again before the final terminal phase.
  Classification aggregation regression. Pool-before-unowned is inspected
  unchanged; their stable ownership predicates are mutually exclusive here.

### Guard inventory

Every independently bypassable safety guard has a distinct PC. None is omitted
on the grounds that this is a read-only change. DTO wording/format and repeat
identity have ordinary V-2 assertions; they are not additional action-authority
guards. The overlap priority/evidence guards are explicitly included.

| Guard | Plan reference and safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-2: historical stamp cannot suppress an otherwise unowned row | PC-1 |
| G-2 | D-2: current-pointer GUID spelling/case is parsed | PC-2 |
| G-3 | D-2: current pointers from every agent, not only historical G | PC-3 |
| G-4 | D-3: CardId excludes a session | PC-4 |
| G-5 | D-3: StartedAt is strictly older than ten minutes | PC-5 |
| G-6 | D-3: EndedAt excludes even a Running session | PC-6 |
| G-7 | D-3: only Starting/Running/Stopping are eligible | PC-7 |
| G-8 | D-3: a Queued task for this session excludes it | PC-8 |
| G-9 | D-3: a Dispatched task for this session excludes it | PC-9 |
| G-10 | D-3: a Working task for this session excludes it | PC-10 |
| G-11 | D-3: a Blocked task for this session excludes it | PC-11 |
| G-12 | D-4: no remote directory means no confirmation | PC-12 |
| G-13 | D-4: pending remote inventory excludes | PC-13 |
| G-14 | D-4: unknown remote session excludes | PC-14 |
| G-15 | D-4: remote live-set membership is required | PC-15 |
| G-16 | D-6: zero runner kill attempts | PC-16 |
| G-17 | D-6: session state/membership unchanged | PC-17 |
| G-18 | D-6: agent state/pointers/membership unchanged | PC-18 |
| G-19 | D-6: tasks unchanged | PC-19 |
| G-20 | D-6: incidents unchanged | PC-20 |
| G-21 | D-6: zero runner input attempts | PC-21 |
| G-22 | D-6: zero runner launch/restart attempts | PC-22 |
| G-23 | D-5: warning targets session A | PC-23 |
| G-24 | D-5: historical G is not exposed as current AgentId | PC-24 |
| G-25 | D-5/D-6: only OpenDrawer is offered | PC-25 |
| G-26 | D-1/D-5: stop-stuck retains priority and one row | PC-26 |
| G-27 | D-5: lower-priority unowned evidence is retained | PC-27 |

### Positive controls

Use the H/U/O/L **exact method bindings** above, never a whole class. Mutation
runs each control after ordinary Review and master publication, on the exact
SourceLanding snapshot. Each cycle is baseline green -> compiling defect ->
named assertion red -> exact source restoration -> fresh-build green. Code runs
V/R, Review judges this design before land. All PCs below are mandatory; none
has been executed by this TestDesign task. The natural CP-1 red does not discharge
post-land PC-1 at the landed SHA.

All defects below are applied in `AttentionService.Leaks.cs`, never by weakening
tests. For PC-12–15 inline the equivalent remote expression **only at the
unowned filter's call site**, then change the named term; keep the shared helper
and pool call unchanged so L's earlier pool assertions do not intercept the
intended red. For no-write/call controls, inject after candidates are loaded,
scoped to the stamped candidate, without changing the returned DTO; use EF
`ExecuteUpdateAsync`/`SaveChangesAsync` for persistent writes. Do not change
fixture state or assertions to simulate the defect.

| PC | Compiling defect breaking its same-numbered G | Exact method binding and intended red oracle |
|---|---|---|
| PC-1 | Restore `s.StandingAgentId == null` in candidate eligibility | H: initial A single-item assertion sees zero |
| PC-2 | Match pointer strings only to canonical `Id.ToString()` instead of parsed GUIDs | O `agent`: final A absence assertion sees a row |
| PC-3 | Restrict the pointer query to `a.AlwaysOn` | H: distinct non-AlwaysOn owner's A absence assertion sees a row |
| PC-4 | Remove only `s.CardId == null` | H: card-bound A absence assertion sees a row |
| PC-5 | Change `StartedAt < cutoff` to `StartedAt <= cutoff` | U: exact-ten-minute absence assertion sees a row |
| PC-6 | Remove only `s.EndedAt == null` | O `ended`: A absence assertion sees a row while still Running |
| PC-7 | Extend the eligible status disjunction with `SessionStatus.Stopped` | H: Stopped/unended A absence assertion sees a row |
| PC-8 | Remove only Queued from the session task subquery | O `task`: Queued phase absence assertion sees a row |
| PC-9 | Remove only Dispatched from that subquery | O `task`: Dispatched phase absence assertion sees a row |
| PC-10 | Remove only Working from that subquery | O `task`: Working phase absence assertion sees a row |
| PC-11 | Remove only Blocked from that subquery | O `task`: Blocked phase absence assertion sees a row |
| PC-12 | Make null directory explicitly admit remote candidates before the remaining non-null conjunction | L: null-directory A absence assertion sees a row; no null dereference counts |
| PC-13 | Remove only the pending-inventory term at the unowned call site | L `pending`: stamped A absence assertion sees a row |
| PC-14 | Remove only the unknown-session term at that call site | L `unknown`: stamped A absence assertion sees a row |
| PC-15 | Remove only the remote live-set membership term at that call site | L `gone`: stamped A absence assertion sees a row |
| PC-16 | Attempt `_runnerClient.KillAsync(stamped.Id, ct)` and swallow only the fake's NotSupportedException | H: KillCalls equals zero fails (counter is nonzero) |
| PC-17 | Persist A.FailureReason=`pc-session-write` without changing eligibility | H: persisted session scalar snapshot equality fails |
| PC-18 | Persist G.PoolIdleSince=now, leaving G->B intact | H: persisted agent scalar snapshot equality fails |
| PC-19 | Persist the retained terminal task's Title=`pc-task-write` | H: persisted task scalar snapshot equality fails |
| PC-20 | Insert an AgentIncident with new Id, A's SessionId, Warning severity and `pc-incident-write` Message/CreatedAt=now | H: incident row-set snapshot equality fails |
| PC-21 | Attempt `_runnerClient.SendInputAsync(stamped.Id, "pc-input", ct)` and swallow only NotSupportedException | H: InputCalls equals zero fails |
| PC-22 | Attempt `_runnerClient.StartAsync(stamped.Id, null!, ct)` and swallow only NotSupportedException (fake rejects before reading spec) | H: StartCalls equals zero fails |
| PC-23 | Set the unowned DTO SessionId to null | H: initial A single-item assertion sees zero |
| PC-24 | Set that DTO AgentId to `s.StandingAgentId` | H: row.AgentId null assertion fails |
| PC-25 | Add OpenAgent beside OpenDrawer in that DTO | H: exact `[OpenDrawer]` actions assertion fails |
| PC-26 | Select `group.Last()` rather than `group.First()` as the merged leak row | H: overlap Kind equals SessionStopStuck assertion fails |
| PC-27 | Replace merged Evidence with `group.First().Evidence` | H: overlap `Also SessionUnowned` evidence assertion fails |

Bindings at Code handoff must include the exact source anchor, test assertion
line and a compiling payload for each PC, plus committed method/fixture changes.
These are **binding required** until Code supplies them; prose above must not
be converted to "executed" or "bound" merely because Code passed. Any unresolved
payload remains binding required after Code and in Review's pending inventory.
If a payload cannot compile at landed L, Mutation records invalid/not-run and
requests a separately commissioned repair; it cannot count a build/fixture error
or zero-test run as red. Preserve survivors and all prior outcomes.

Method filters for every baseline/red/restored-green phase are
`/*/*/AttentionServiceTests/<exact H method>` (Min=1), or
`/*/*/AttentionServiceTests/<exact U/O/L method>*` (Min=2/4/3 respectively).
Each phase must report its argument roster and named failing assertion. Use the
local inherited `run-checkpoint.ps1`/build-slot driver and separate owned outputs
and external evidence roots per phase, as the testing owner requires. No batching:
all mutations touch the same source file and share projection/test paths.

Inventory audit: **guards=27, mapped=27, missing=0, duplicate PC maps=0**.
Execution audit now: **0/27 run, 27 pending post-land**. Completion requires
**all PCs run** with intended red plus restored green, no survivors, and a
restoration receipt; ordinary land is explicitly not PC-clean.

### Out of scope

- August launch/replacement causation, production process operations and live
  fleet census: historical evidence is unavailable, and this fixture proves a
  deterministic visibility rule only. No kill/release authority follows.
- New delivery, browser/HTTP wiring, native pty, real providers, supervisor,
  reconciliation and pool release: their paths are unchanged; fake inventory
  is not evidence of any of those outcomes.
- Atomic ownership snapshots and transient concurrent pointer movement: the
  existing read projection has no transaction spanning all observations.
- Full `AttentionServiceTests`, Unit lane and full assembly: the commissioned
  two-run profile overrides the generic full-suite default. The inspected
  overlap method's four executions are not silently included; R-8 supplies
  its relevant stamped-session control within H. Zombie census, deferred-kill
  debt and other pool predicates remain existing coverage outside this change.
- Deployment, RC or activation: master-only publication includes the unchanged
  investigation document already in ancestry. Ordinary Review remains required.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0188-red/` | c0188-red-witness | `/*/*/AttentionServiceTests/a_historical_standing_incarnation_without_a_current_pointer_is_unowned` | V-1, R-1 | exactly 1 executed, 0 passed, 1 intended missing-A-warning assertion failure, 0 skipped; expected exit 1 | 1 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c0188-green/` | c0188-narrow-green | `/*/*/AttentionServiceTests/(a_historical_standing_incarnation_without_a_current_pointer_is_unowned*)\|(a_live_session_no_agent_names_is_unowned*)\|(a_live_session_an_agent_names_is_not_unowned*)\|(remote_leak_rows_require_confirmed_liveness*)` | V-1, V-2, V-3, V-4, V-5, R-1, R-2, R-3, R-4, R-5, R-6, R-7, R-8 | exactly 10 executed (H=1 U=2 O=4 L=3), 10 passed, 0 failed/skipped; exit 0 | 10 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

The escaped pipes above are Markdown escapes; the importer emits literal
method-segment OR operators. No OR between full paths. Each row performs its
own isolated build, then exactly one test invocation. The union of CP-2's V/R
IDs is the whole ordinary acceptance scope; CP-1 provides its negative witness.
No repeats, extra method discovery run or optional quick proof is required.

Run each row only after its slice is committed and pushed. Use a fresh owned
checkpoint-tool output for each slice, through the gate (bootstrap is tool
setup, not an extra application test checkpoint):

```powershell
$plan = 'docs/superpowers/plans/2026-10-19-card-0188-leaked-incarnation-detection-plan.md'
$sliceSha = git rev-parse HEAD
# S1 only; capture its exit rather than chaining S2 to exit zero.
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0188-s1-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c0188-tool-s1/
dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c0188-tool-s1/ -- run --plan $plan --rows CP-1 --expected-source-sha $sliceSha --max-wait 50s
# S2 only, after implementation commit; refresh sliceSha first.
$sliceSha = git rev-parse HEAD
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0188-s2-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c0188-tool-s2/
dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c0188-tool-s2/ -- run --plan $plan --rows CP-2 --expected-source-sha $sliceSha --max-wait 50s
```

If `run` returns 75, use the same built tool's `wait <run-id> --max-wait 50s`
with `dotnet run --no-build --project tools/Antiphon.Checkpoints` and the same
OutputPath; await terminal completion. These no-build tool launches are the
checkpoint coordinator, whose build/test row drivers lease their own slots.
Release the bootstrap lease before launching the coordinator; do not hold an
outer lease while its rows wait for another slot. Exit 4 is a reported slot
timeout, never an unleased retry.
Check both tool and row receipts; preserve unedited CHECKPOINT lines, build
completion, actual TRX roster, source binding, elapsed time and errors.

`Expect` is descriptive: the runner does **not** turn the intentional CP-1
failure green. Record CP-1's actual exit 1 and assertion text at S1; do not pass
it off as a clean validator receipt. No attempt to validate CP-1 as green, no
known-flaky retry, and no second red proof if it is the intended failure.
CP-2 must have dirty=0, matching S2 source, buildSource=verified and all ten
cases green; validate its report with `validate-checkpoint-receipt.ps1` and
`-Rows CP-2 -ExpectedSourceSha <S2 SHA>`. Failure to reach the CP-1 assertion
or a missing CP-2 case leaves verification incomplete. Repair-caused reruns
need an explicit reason/cost; never claim the two-run budget was met afterward.
Clean only the producer-owned alternate outputs, including tool outputs,
using the owner's cleanup rules, and retain generated evidence gitignored.

### Cost

All following numbers are **estimates**, not new measurements. The prior
investigation's four-case method cost (1m18.610s test + 1m18.98s corrected
build) is historical context; it does not certify this larger phased witness.

- Ordinary V/R floor (Code): CP-1 exact H **6 minutes** + CP-2 exact H/U/O/L
  OR filter **6 minutes** = **12 minutes**, including two isolated application
  builds. Tool setup **2 minutes** is separate. Authoring **20**, receipt/
  publication/output cleanup **5**, reserve **6**: **45-minute Code budget**.
- PC floor (Mutation): each of PC-1–PC-27 uses its table-bound method filter.
  Per PC allow **3 minutes baseline build+green + 3 minutes mutated build+red
  + 1 minute mutation/restoration + 3 minutes restored build+green = 10 minutes**.
  Thus **27 x 10 = 270 minutes**, plus **3 minutes** initial setup = **273**.
  A three-minute phase includes roughly 1.5 build + 1.5 test minutes; actual
  runner cost may exceed it. All 81 phase invocations are method-scoped;
  the 27 controls are not 27 Code runs. Commission sufficient post-land time.
- Combined verification floor: **2 + 12 + 3 + 270 = 287 minutes**. Combined
  Code budget plus Mutation execution estimate: **45 + 273 = 318 minutes**,
  excluding separate Review/landing and Mutation analysis/reporting time.
- Savings: CP-2 combines four method selections into one build/host, saving
  three builds (estimated **4.5 minutes**) versus four separately built green
  methods. Mutation batching savings are **0**: shared file/method dependencies
  forbid independent batching. Neither broad suite runs nor early stopping
  after the first killed mutant are budgeted. If the bound phases cannot fit,
  report the measured overrun and keep missing work incomplete; do not lower
  counts, omit guards or weaken assertions to meet an estimate.

--- next stage ---
next: code
handoff: Implement S1/S2 with the bound H/U/O/L phases and exactly CP-1 red then CP-2 green (1 then 10 executions), within 45 minutes. Preserve detection-only behavior and master-only landing with the investigation. Bind all 27 pending post-land PCs; commit/push slices and return ordinary evidence to Review, not Mutation.
artifact: docs/superpowers/plans/2026-10-19-card-0188-leaked-incarnation-detection-plan.md
