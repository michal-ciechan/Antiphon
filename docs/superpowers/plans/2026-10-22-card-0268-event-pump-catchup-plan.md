# CARD-0268: catch-up/subscription gap disposition

Date: 2026-10-22. Stage: Plan. Inspected source:
`3f54023d6b715fe2653014dc82187d358c0193b6`.
Documentation owner: task `3c1a181b`, branch `feat/card-task-3c1a181b`.

**Outcome: the original defect is already fixed in this checkout by CARD-1205.
Recommend closing CARD-0268 as superseded. No additional production fix or new
regression test is justified by the card's existing evidence.** This document is
the requested plan artifact and records why the proposed implementation slices
are unnecessary. It does not certify which binaries are currently activated.

Read from the Antiphon board: CARD-0268
(`1e30d814-3630-4bf4-9e25-38cdc26ce3f8`), its Done parent CARD-0264
(`9a74730f-3baa-4227-b3fa-5d0022b71372`), and Done CARD-1205
(`29eaf667-5a31-42c1-9248-c4d1cf758795`). The complete
[CARD-0264 investigation](2026-08-31-card-0264-completion-note-strand-plan.md)
was read, including secondary finding 3. Its original timestamp evidence remains
historical evidence; the approximate gap duration is not a timeout specification.

## Ground truth

References below are source locations at the inspected SHA.

| What the card assumes | What the code does now | Consequence |
|---|---|---|
| A record published after boot catch-up and before subscription can remain missing until an unrelated pull. | `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:50` retains the first catch-up; lines 63-67 recognize readiness, and `HandleSubscriptionReadyAsync` at line 118 awaits another catch-up with `subscriptionProof: true`. | The original one-catch-up implementation is gone. Successful post-subscription catch-up recovers the gap. |
| Opening the event stream establishes subscription. | `src/Antiphon.SessionRunner/Program.cs:398` writes and flushes `: connected` before `runtime.Subscribe` at line 403. | Headers and `: connected` remain insufficient proof. |
| Stream events replay the missed record. | `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:4487` registers subscribers; `Publish` at line 4502 snapshots only current subscribers. | The publication window still exists physically; recovery closes its persistence consequence. |
| A second pull might still run before registration. | `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:896` creates a per-enumeration readiness latch. Only exact `: keepalive` or a successfully parsed supported event emits readiness. Both originate inside the runner's subscribed loop. | The second pull is causally after subscription, on startup and each reconnect. |
| Using the first event as proof could discard or overtake it. | The marker retains that event's payload (`server/Application/Dtos/SessionRunnerDtos.cs:165`). The pump awaits catch-up, offers the held event through its common `finally`, then suppresses the iterator's duplicate copy. | Existing ordering and held-event failure handling already cover this concern. |
| Avoiding backfill requires changing stored sequences. | `server/Application/Services/AgentSessionRuntime.cs:1356` retains per-session monotonic rebasing and `(Uuid, Kind)` identity; `SyncTranscriptAsync` uses existing persistence/action admission. | Keep rebasing, timestamps, deduplication and the CARD-0264 working-state fix. |
| This still needs a new red witness. | `TurnEndGapReproductionTests.C1205_Gap_end_is_stored_before_any_grace_pull` already constructs this exact gap using the real hub and production HTTP iterator/pump. Its original missing-row assertion remains. | Reuse the existing witness; do not add a duplicate or make current production red deliberately. |

## Reachability and fix provenance

The original schedule was reachable: capture caller snapshot C1; pause a different
session's initial pull; append a caller record to runner custody and publish it
with zero subscribers; finish initial catch-up; register the stream. The hub does
not replay that publication. The CARD-1205 harness implements those barriers in
`tests/Antiphon.Tests/TestHelpers/TurnEndGapHarness.cs`, without timing sleeps.

Current processing adds subscription proof P and snapshot C2. A retained record
published before registration is available to C2. A record published after
registration is available through the stream, C2, or both, with existing identity
deduplication handling overlap. C2 is awaited before ordinary live dispatch.
A quiet stream supplies proof via keepalive (runner default 15 seconds); there
is no new immediate-delivery or maximum recovery-time guarantee.

The implementation commit is
`764488f85bbd84a4b84866aadee3eff64bb1b89f`, **fix(CARD-1205): catch up after the
stream proves subscription**. `git merge-base --is-ancestor` confirms it is an
ancestor of the inspected source. Subsequent held-event repairs are also present,
including `a608d30e5d5b7af4e5d37e7ea62150e13cc79655`.

The witness was introduced in
`c2ae7c038b5b50d184fba37d24399d35ee4dab8f`; readiness cases followed in
`d2e4aa89ea3d2b142e7e75c9f239d1c42a7119c0`. The
[CARD-1205 test design](2026-10-12-card-1205-test-design.md#checkpoints)
records S1's expected red and S2's green T01. Comments in the test that say
“unmodified production has no second catch-up” describe that historical baseline,
not this checkout. These are existing evidence records, not fresh test results.

CARD-1205's board closure explicitly reports the readiness marker and second
catch-up shipped, plus later checked recovery, watchdog and delivery acceptance.
It also discloses pending post-land Mutation and missing native Windows
qualification. This plan neither removes those limitations nor claims that all
transcript-loss mechanisms are solved. Catch-up List/pull/persistence failures
remain distinct from the successful-read subscription race.

## Decisions

- **D-1 — Recommend closure as superseded by CARD-1205.** Both source ancestry and
  the current control flow establish the repair. Reject implementing a second
  version of the same mechanism or treating the historical card as a new defect.
- **D-2 — Preserve the existing first and post-proof catch-ups.** The first keeps
  downtime ordering; the second covers the registration gap. Reject moving a
  pull after merely constructing a lazy iterator, or using headers/`: connected`
  as readiness. An acknowledged subscribe-before-snapshot protocol could work,
  but adds runner/interface scope without a demonstrated remaining defect.
- **D-3 — Keep the closure bounded to the direct HTTP SSE race.** Do not change
  phone-home recovery, queue delivery, transcript rebasing or idle rules. The
  historical parent and current CARD-1205 ownership already distinguish them.
- **D-4 — No fresh builds, test runs or mutations for this documentation-only
  disposition.** Existing tests are identified below and were inspected, not
  executed here. A red witness against current code is not an honest requirement
  when the fix is already present. Reject reverting production just to recreate
  the historical red in this Plan task.
- **D-5 — Route a contradicted premise to a narrow investigation, not Code.**
  Recommend closure now from the source evidence. If the caller needs deployment
  confirmation or has a current missing record, measure the facts below before
  commissioning another implementation. No human product decision is needed.

## Slices and ownership

| Slice | Files / action | Tests and completion condition |
|---|---|---|
| S1 — source disposition | Add only `docs/superpowers/plans/2026-10-22-card-0268-event-pump-catchup-plan.md`; commit and push on the assigned branch. | Inspect the existing reproduction/readiness tests listed below; pass CP-1 and CP-2. No production/test edit. |
| S2 — owner landing and card disposition | Caller lands this documentation through task owner `3c1a181b` and records the superseding CARD-1205 on CARD-0268. Never edit generated `docs/cards/` files. | No build/test slice. Recommended card outcome: Done, already fixed by CARD-1205. |

No session is added or changed to wait for input, so a session-release checklist
is inapplicable. This artifact creates no parking or release deadline.

## Verification design

This is verification of a closure recommendation, not a new Code execution plan.
There are zero newly implemented behaviours and therefore no new red/green pair.
The brief's already-fixed disposition applies. The existing smallest pair is
CARD-1205 T01's historical missing-end red and its repaired green; neither is
claimed as executed in this task.

Existing test files and their concrete oracles:

| File / exact method | What makes it a useful witness |
|---|---|
| `tests/Antiphon.Tests/Application/TurnEndGapReproductionTests.cs` — `C1205_Gap_end_is_stored_before_any_grace_pull` | Publish with zero hub subscribers, consume subscribed keepalive, capture DB count and SQL/cache working state **before** explicit grace pull. Require the missing UUID/kind once and idle. Removing post-proof catch-up yields count 0; the grace control still recovers it. |
| Same file — `C1205_Grace_pull_recovers_the_unsubscribed_tail` | Keep subscription absent; require missing row before and exactly one after the explicit recovery pull. Preserves the load-bearing backfill control. |
| `tests/Antiphon.Tests/Application/SessionRunnerStreamReadinessTests.cs` — `C1205_Connected_comment_is_not_readiness` | Headers and `: connected` cause neither readiness nor a second snapshot. |
| Same file — `C1205_Reconnect_rearms_readiness` | A second connection gets its own marker and recovers a gap record once. |
| Same file — `C1205_Catchup_finishes_before_live_dispatch` | Hold C2's save; require no live callback until that save completes. |

The harness invokes the production HTTP iterator and pump with a controlled
handler and real event hub. It does not invoke the runner's actual `/events`
route; that route's subscribe/write ordering was inspected directly. This is
not native provider, real-network or deployment qualification.

Placement discovery was performed through `GET /api/runner-defaults` and
`GET /api/session-runners`. Defaults revision 2 was available and eligible Linux
and Windows lanes were reported. This artifact pins neither a host nor an OS.
Any follow-up dispatch resolves the current defaults again and omits `-Runner`
and `-Platform` unless it actually requires a pin.

### Checkpoints

Closed list for S1: **two static documentation/source-provenance checks, zero
builds, zero TUnit executions, no full-suite run**. Group names identify the lane.
These commands run directly; neither invokes a build/test driver or needs a build
slot. No checkpoint output is represented as a test receipt.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | n/a | docs-static | `git diff --check 3f54023d6b715fe2653014dc82187d358c0193b6 HEAD` | V-1 artifact whitespace | Exit 0, no diagnostics | n/a | 1 |
| CP-2 | S1 | n/a | source-provenance-static | `git merge-base --is-ancestor 764488f85bbd84a4b84866aadee3eff64bb1b89f HEAD` | V-2 fix inherited by this plan | Exit 0 | n/a | 1 |

## Follow-up measurement only if closure needs more evidence

The stage handoff is `investigate` because the card's unfixed-source premise is
obsolete. It is not authorization for another fix or an open-ended test sweep.
The caller can close the card using the source evidence above. If activation is
part of the remaining question, read the affected deployment's `/api/version`
SHA and establish whether it contains the fix; source ancestry does not prove
activation. If a fresh miss exists on fixed source, correlate session/generation,
record UUID/kind, C1 snapshot, hub registration/publication, first readiness
proof, C2 List/pull result and durable insert. Classify missing custody, missing
subscription proof, failed catch-up and failed persistence separately. Do not
infer success from keepalive traffic or an empty queue, and do not restart a
service merely to obtain this evidence.

--- next stage ---
next: investigate
handoff: CARD-0268 is fixed in source by CARD-1205; recommend closure and owner landing of this docs artifact. If closure needs activation evidence, verify the affected deployment SHA contains 764488f85bbd84a4b84866aadee3eff64bb1b89f; investigate snapshot/subscription/post-proof-pull UUID coverage only for a fresh miss. No Code dispatch is indicated.
artifact: docs/superpowers/plans/2026-10-22-card-0268-event-pump-catchup-plan.md
