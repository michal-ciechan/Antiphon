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
