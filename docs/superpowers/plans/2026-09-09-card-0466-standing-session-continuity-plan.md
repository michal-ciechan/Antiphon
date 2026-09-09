# CARD-0466: preserve standing-session continuity through infrastructure failures

Status: Plan and TestDesign complete; ready for Code.
Date: 2026-09-09. Task: `81da7ca2-944c-442e-80ab-f21b1c72f8f8`.
Source checkout: `4e0ae76a` (`feat/card-task-81da7ca2`).

## Outcome and scope

Separate retry pacing from conversation identity. An infrastructure failure, including
Postgres `57P03` during Start/resume, must neither increase `ConsecutiveFailures` nor
authorize Fresh. Preserve capped exponential recovery. When continuity cannot be
resumed, preserve the session and surface a durable decision; only an explicit Fresh
request abandons it. Add an ownership-validated `resumeSessionId` to standing-agent
Start so a prior conversation remains selectable after `PersistentSessionId` changed.

This applies to ordinary standing cardless Claude/Grok sessions, regardless of agent
name, channel binding or hosting backend. Explicit recovery is available to non-pool
standing agents even when AlwaysOn is disabled. Supervisor policy applies to AlwaysOn.
Keep specialist, capacity, quota, authentication, Herdr placement and human-stop guards.

**Excluded:** changing `ResumeInterruptedLaunchAsync`'s task-backed eligibility,
persisting/replaying launch notes, remote-control options or initial prompts after a
server restart, introducing a durable launch job, native Codex/OpenCode resume support,
card-session resume redesign, automatic ownership adoption, and live production recovery.
Attempt outcome evidence below is for classification, not interrupted-launch replay.

Owners: [runtime](../../session-runtime-invariants.md),
[agent kinds](../../agent-kinds.md), [HTTP](../../ops-http.md),
[card lifecycle](../../agent-card-lifecycle.md),
[testing](../../testing-and-build.md), [conventions](../../project-context.md).

## Ground truth

| Assumption or requirement | Code at this checkout | Design implication |
|---|---|---|
| The resume-failure limit measures broken conversations. | `AgentSupervisorService.SuperviseAsync` chooses `Fresh` from `ConsecutiveFailures >= FreshAfterResumeFailures` (default 2). The counter grows in a generic Start catch and when a prior supervised start is no longer live. | Removing only a Postgres catch is insufficient. Remove the counter-to-Fresh relationship altogether. |
| Start success means the process resumed. | `AgentControlService` pre-creates/restamps a row and enqueues work; `AgentSessionLaunchQueue` runs `LaunchInteractiveAsync` later in another scope. | Cover synchronous failures and durable asynchronous outcomes, not just exceptions from Start. |
| Runner availability proves the infrastructure is ready. | Tick probes runner `ListAsync`; database reads, composition and saves occur afterward. | A reachable runner says nothing about Postgres. A failed observation cannot prove conversation loss. |
| Fresh is chosen only by the supervisor. | `FindResumableSessionAsync` returns null for an ineligible/missing current pointer and Start creates a new row. `LaunchInteractiveAsync` catches `ResumeTargetMissingException` and starts fresh under the **same ID**. | Close both cardless implicit fallbacks; a stable row ID alone does not prove continuity. |
| Grok missing native history is a definitive filesystem observation. | `EffectiveResumeMode` turns resume into create when `GrokNativeSessionStore.Exists` is false. Its locator also returns null on I/O/access errors. | Absence and unavailable storage must not authorize create. Standing strict resume must preserve the requested mode. |
| A prior standing session can already be resumed explicitly. | `AgentSessionService.ResumeAsync` requires a card/worktree. Standing Start looks only at `Agent.PersistentSessionId`, requires stopped/failed, same Claude/Grok kind and canonical cwd. | Add selection to standing Start and reuse its composer/queue/process path; leave the card-only endpoint intact. |
| A session row durably identifies its standing owner. | `AgentSession` has no standing-agent FK/owner ID. Current ownership is a mutable pointer. Tasks and selected lifecycle incidents can retain historical evidence. | Add immutable historical ownership, plus conservative legacy resolution; cwd, names and knowledge of an ID are insufficient. |
| The agent lock already serializes the entire Start. | `LockAgentAsync` issues `FOR UPDATE`, but Start itself does not enclose selection and mutation in a transaction. Herdr evidence has its own short transaction. | Explicit selection needs an actual atomic reservation and revalidation, not reliance on the method name. |
| Switching sessions has no work-routing effect. | The fresh-row branch moves pending non-rules messages and remaps Dispatched/Working tasks to the new ID; the resume branch does neither. | Define source-to-target behavior explicitly. Never replay a previously attempted message across conversations. |
| Failures must retry forever at high frequency. | Existing backoff is `min(5s * 2^n, 30 days)`; healthy uptime resets it; Herdr has a separate durable hold. | Preserve bounded cadence and escalation. Continuity holds are distinct from transient backoff and existing holds. |
| Interrupted-launch recovery is this fix. | `ResumeInterruptedLaunchAsync` requires a Dispatched task and an attachable non-Herdr adapter; otherwise it fails for clean relaunch. | Leave that separate durability problem unchanged. |

Relevant existing tests: `AgentSupervisionTests`, `AgentControlServiceIntegrationTests`,
`GrokNativeSessionResumeTests`, `HerdrSupervisionBackoffTests`,
`CapacityRecoverySupervisionTests`, `HerdrAlwaysOnChannelParityTests`.
Some tests intentionally assert the old automatic Fresh behavior and must change.

## Decisions

### D-1: Fresh is an explicit identity decision

Delete automatic Fresh selection from the generic failure ladder. Supervised restarts
request resume/default Start, never `Fresh: true` because of a count, timeout, process
exit, transport error, model hold, startup failure or unknown outcome.

For a resumable standing kind with an existing conversation, default Start is strict:
resume that conversation or explain why it cannot proceed. Missing row, malformed
nonempty pointer, kind/cwd mismatch or uncertain ownership is a refusal/decision, not
permission to create. A genuinely new agent with no previous conversation may start
its first session. Unsupported native-resume kinds retain their existing ordinary
new-session behavior, with an explicit `ResumeUnsupported` audit reason; an explicit
`resumeSessionId` for such a kind is refused. Do not infer that a kind change erased
the previous conversation: an existing supported conversation changed to another
kind requires an explicit Fresh request.

Retain `FreshAfterResumeFailures` as a deprecated, ignored configuration property for
compatibility in this change; emit one startup warning when explicitly configured.
Remove its operative reads and update current owner docs/settings examples. Do not
rewrite historical plans. Test that extreme values cannot restore automatic Fresh.

Reason: a repeated inability to launch does not demonstrate irrecoverable history.
Rejected: exempting only SQLSTATE 57P03, adding a larger threshold, or renaming the
same generic counter to ResumeFailures. All still infer history loss from failures.

### D-2: Pace every failed attempt, classify continuity independently

Add `RestartBackoffFailures` to `AgentSupervisionState`. It is the exponent input and
the count reported by backoff escalation. Keep `ConsecutiveFailures` for confirmed
non-infrastructure launch/process failures only; neither counter chooses Fresh.
Add a provider-independent outcome kind: `Infrastructure`, `LaunchOrProcessFailure`,
`ContinuityUnavailable`, `Unknown`. Existing capacity/Herdr outcomes retain their own
policies and take precedence where they already own recovery.

| Observation | Ordinary failure count | Backoff/recovery | Identity |
|---|---|---|---|
| Transient database failure, connection refusal, runner outage, non-caller timeout | Unchanged | Increment backoff once for an actual failed attempt; retry at capped due time | Preserve target |
| Caller/host cancellation | Unchanged | Propagate; do not record shutdown as a failed attempt | Preserve target |
| Confirmed launch/configuration/process failure unrelated to infrastructure | Increment once | Existing capped ladder / qualifying Herdr hold | Preserve target |
| Unknown or interrupted launch outcome | Unchanged | Back off when a terminal outcome is observed; do not fabricate a resume failure | Preserve target |
| Positive provider evidence that the selected conversation is unavailable | Unchanged | Durable continuity hold; no automatic create | Operator decides |
| Capacity/model hold or provider-sign-in refusal | No new continuity count | Existing wait/hold/refusal policy | No silent reroute or Fresh |
| Running for configured healthy interval | Reset ordinary and backoff counters and escalation tier | Existing health recovery | Same target |

Use a concrete application failure policy consuming BCL exception types and typed
launch/runner exceptions. Walk wrapper chains and aggregate leaves; `DbException`
transience, transport errors and non-caller cancellation/timeouts are infrastructure
evidence. Test the actual Npgsql `PostgresException("57P03")`, including EF/wrapper
forms, without adding Npgsql dependencies to Domain. If a provider-specific test
requires normalization beyond `DbException.IsTransient`, normalize it in Infrastructure
to an application exception. Do not introduce interfaces for pure policy helpers.
Nontransient database/config errors still cannot imply lost history. Infrastructure
evidence takes precedence over stale screen text containing a missing-session phrase.

Change cancellation filters in the touched supervisor/interactive-launch paths to
distinguish `ct.IsCancellationRequested` from an HttpClient timeout. Retain cleanup
ownership and never convert an ambiguous runner response into another fresh launch.

### D-3: Persist asynchronous evidence and consume one incarnation once

Store `AgentSession.RestartFailureKind` and `InteractiveLaunchCompletedAt` on the
session incarnation (reset them on each new/restamped `Starting` row). Use the
existing `(SessionId, persisted StartedAt)` generation pattern from Herdr supervision.
Add `LastObservedRestartSessionId` and `LastObservedRestartStartedAt` to supervision
state. A terminal incarnation is charged once, even across ticks,
manual retries, scope reconstruction and server restarts. Mere `LastAttemptAt != null`
is no longer sufficient to increase `ConsecutiveFailures`.

The queue worker records the original exception's classification before rethrowing;
the later exit caused by cleanup must not overwrite it with a generic process crash.
Stamp successful launch completion only after interactive boot work has completed;
an ordinary subsequent runtime crash can then be classified by the runtime observer.
No completion/evidence after a failed database save means Unknown, not proof of a
bad conversation. Persisted `Running` alone is insufficient: it is set before the
remaining boot work currently finishes.

The synchronous Start catch accounts for its failed attempt once and schedules from
`RestartBackoffFailures`. If it has already restamped a session, mark that incarnation
consumed when recording its outcome so the next dead-session sweep does not charge
the same attempt again. Otherwise retain the previously consumed terminal generation.
Do not charge an old death again merely because composition failed before enqueue.

Keep the agent/evidence lock short; commit before runner or composition I/O. If the
database is unavailable even for failure bookkeeping, let the hosted service log and
retry with a fresh scope at its bounded tick cadence. Do not repeatedly save a failed
transaction/context, launch again in that catch, or infer a terminal outcome from
the missing write. Resume the durable due-time ladder when storage recovers.
This does not promise durable retry accounting while the database itself is down.

Migration: initialize `RestartBackoffFailures` from the old `ConsecutiveFailures` and
retain `NextRestartAt`/escalation state. Reset the old mixed count to zero, because
historical rows cannot distinguish infrastructure failures. Do not migrate any count
into a continuity hold. Existing human, liveness, capacity and Herdr state is preserved.

### D-4: Unavailable continuity becomes durable Attention

Add a separate durable continuity hold on supervision state: `ContinuityHeldAt`,
`ContinuitySessionId`, `ContinuityReason` and bounded `ContinuityEvidence`. Reasons:
`NativeSessionMissing`,
`TargetMissing`, `TargetIncompatible`, `OwnershipUnproven`. This is not `Suspended`,
a card state, an alert acknowledgement or a new board column.

Append `StandingContinuityDecision` to Attention and append timeline incident kinds
for hold, explicit Fresh and selected resume. Derive Attention from the durable hold,
so incident pruning, AlwaysOn being switched off and server restart do not erase it.
Show agent, target, cause, and actions to open the agent/session. Record decisions
with `raiseAlert:false`; avoid a duplicate incident-derived Attention row. Existing
infrastructure failure alerts/backoff escalation remain operational signals.

Start accepts `retryContinuity: true` to retry the same target after repair. Ordinary
Start while held returns 409 `standing_continuity_held` without clearing any latch.
An explicit, validated `resumeSessionId` acknowledges this hold by selecting a target;
explicit `fresh:true` acknowledges abandoning it. Clear the continuity hold only as
part of an accepted launch reservation; rejection by quota/auth/configuration leaves
it visible. A later failed resume restores/updates the hold. None of these flags
implicitly clears a Herdr hold: its existing explicit reset remains required.

For ordinary standing cardless launches, remove the `ResumeTargetMissingException`
same-ID fresh retry. Persist the missing-target outcome and hold, then fail that
attempt visibly. The failed adapter still receives normal cleanup. Never run a second
adapter in create mode inside the same exception handler.

The strict standing Grok path must not use `EffectiveResumeMode`'s Boolean
Exists-to-create downgrade. Preserve `--resume` and use authoritative runner/provider
refusal. Where a native-store probe supplies missing evidence, distinguish Found,
Missing and Unavailable; an I/O/access error is not Missing. Preserve the current
card-session policy by explicitly scoping this change to standing cardless launches.
Test inherited PtyHost and Herdr paths, including `GrokNativeSessionMissing` and
rules-migration refusals. Missing target is the only currently supported positive
native-loss classification; generic readiness failure is not a corruption detector.

An operator may deliberately choose Fresh even when history might still be recoverable;
that is an intentional discard, not an automatically diagnosed fallback. Always create
a **new session row/ID**, record old/new IDs and the explicit reason, and retain the old
row and ownership. Successful resume and accepted enqueue are separate timeline facts.

### D-5: Historical ownership survives a pointer change

Add nullable immutable `AgentSession.StandingAgentId`, indexed with `CreatedAt` for
history lookup. It identifies the physical standing agent, not a specialist's logical
owner. Store this historical GUID without cascade/reassignment on agent deletion;
do not allow another agent with the same name to inherit it. Write it on ordinary
standing interactive creation and validated standing Herdr attach, before enqueue.
Resume preserves it. Pool/card sessions do not acquire it. Do not expose a generic
PATCH that sets this field or accepts a caller's ownership claim.

A conservative legacy resolver is necessary for recovery of conversations already
orphaned by the old fallback. For a cardless, worktree-free row with no stamped owner:

1. Gather existing server-authored ownership evidence: current persistent-pointer
   references, task execution links `(AgentTask.AgentId, AgentSessionId)` to a non-pool
   standing agent, and supervisor lifecycle incidents `Crash`, `RestartScheduled`,
   or `Recovered` with both agent and session IDs. A task's **ParentSessionId** is a
   reply destination, not an ownership link.
2. Require at least one positive source, exactly one distinct agent across those
   sources, and no competing current pointer or existing owner. Never use cwd,
   title, slug, model, recency, transcript proximity or the supplied GUID as ownership.
3. Backfill only these unambiguous rows in the CLI-created migration. Recheck legacy
   evidence under the selection transaction before stamping any still-null row.
   GET history may derive eligibility but must not mutate ownership.
4. Missing/pruned/contradictory evidence yields `standing_resume_owner_unproven`;
   another stamped owner yields `standing_resume_not_owned`. Do not offer a bypass.

Test legacy historical-only ownership with the current pointer already on a different
session: this is a release requirement, not optional future compatibility. Also test
contradictory historical links. Successful ownership validation proves who may attempt
resume; only the native resume outcome proves the conversation remains available.
Native history already overwritten by the old **same-ID** create fallback cannot be
reconstructed by this feature. No promise to restore history that no longer exists.

### D-6: Select through Start, with strict validation and current composition

Extend `StartAgentRequest` by appending `Guid? ResumeSessionId = null` (wire
`resumeSessionId`) and `bool RetryContinuity = false`. `fresh:true` together with either
resume option is 422; `resumeSessionId` plus `retryContinuity` is also 422. The selected
ID is the Antiphon session ID, never a path or arbitrary native-provider token.

Add read-only `GET /api/agents/{id}/sessions?take=...&before=...` returning bounded,
stable history with ID, created/ended time, kind, cwd, status, ownership-evidence kind,
eligibility and refusal code. Include uniquely proven legacy sessions, and never show
another owner's history. Session metadata and links only; no environment, credentials,
delegation hashes or transcript content. This endpoint discovers candidates; Start
always revalidates. The UI provides Resume previous conversation and a distinct Start
fresh action, showing the selected session and the consequence before submission.

Explicit selection requires all of the following before mutation/queue submission:

- Target exists; `CardId == null`, `WorktreeId == null`; owner resolves to this non-pool
  standing agent. Reject delegated pool/card/worktree sessions even with matching cwd.
- Target is Stopped or Failed. No worker owns it, no live runner process owns it, and
  no other agent points to it. Database liveness alone is not enough; runner uncertainty
  yields a retriable refusal. Reuse existing native transcript ownership rules.
- Current effective launch kind supports resume and matches target kind; canonical cwd
  matches using the existing OS comparison. Recompose current bundles, profile/model,
  environment resolver, backend/placement, remote-control policy, Grok rules and launch
  notes through `StartInteractiveSessionAsync`; do not reuse historical argv/tokens.
- No current live/Starting/Stopping/queued launch on another session. Return 409
  `standing_resume_current_active`; do not stop it as a hidden effect of selection.
  An idempotent repeat selecting the same current live/queued target is a no-op and
  must not repeat the initial prompt or decision incident.
- `ResolveStartCardAsync` finds no spawnable current/queued card. Refuse explicit
  selection with 409 `standing_resume_card_work_pending`; do not spawn or steal a card.
- Existing specialist authorization, provider quota/model/authentication, human-start,
  capacity and Herdr-reset rules are respected. Automatic/capacity callers cannot use
  `resumeSessionId`, `retryContinuity`, or Fresh to bypass these guards.

Use stable HttpException codes for validation/409 refusals and the established 404 for
a missing target. Return the ordinary AgentDetailDto after acceptance; the API response
means **queued**, not recovered. Asynchronous missing/failed resume remains visible.

Reason: Start owns standing launch composition already. Rejected: broadening the
card-only `/sessions/{id}/resume` path, accepting cwd as ownership, rewriting
PersistentSessionId via PATCH, choosing the latest matching transcript, or killing a
current conversation on selection.

### D-7: Reserve atomically; route only safe work across a deliberate switch

Refactor standing selection and existing launch composition into shared helpers, so
normal Start, retry and historical selection use one resume branch. Preflight/compose
outside the transaction. Then take a short explicit transaction: lock agent first,
lock involved session rows in deterministic ID order, reload/revalidate pointer,
generation, live/queued state, spawnable-card state and ownership. Atomically reserve
the target as Starting, restamp the normal resume fields, set PersistentSessionId and
Agent.Status, record selection, and update permitted work references. Commit before
enqueue. Persist owner/current pointer before any worker can run, for every standing
resume/create, not only the existing special Check branch.

Use in-process launch ownership plus the persisted generation to prevent duplicate
enqueue. Make standing-worker ownership checks cover a selected ordinary agent as
well as Check: before spawn and before post-ready typing, verify the expected pointer,
generation and accepted intent. A concurrent Stop/new Start that supersedes it must
leave a late-created process killed or legitimately owned, never orphaned. Serialize
Stop's reservation/invalidation with the same agent lock; do not hold a transaction
while waiting for kill/ready/runner RPCs. Revalidate after awaited preflight I/O.
This requires concurrency tests; a bare autocommit `FOR UPDATE` is not the solution.

For a historical switch from current session B back to owned A:

- Refuse with `standing_resume_work_in_flight` if either involved session has an open
  task execution assignment; settle/reconcile that work first. Do not silently remap
  Dispatched/Working/Blocked tasks into older context. Child tasks whose ParentSessionId
  points at A are not execution assignments and do not prevent recovering their parent.
- Move only Pending, non-rules messages from B that were **never attempted**: zero
  attempts, no delivery-start timestamp, no verdict/baseline/evidence. Preserve their
  IDs, origin, scheduling/hold times, ordering, correlations and park limits.
- If B has ambiguous previously attempted pending messages, refuse the switch with
  `standing_resume_delivery_pending` and expose their queue links for existing
  operator resolution. Leave all such evidence untouched; never clear baselines or
  retype them on A. Do not silently drop them. A's existing messages keep their own
  late-confirm/parking semantics because they remain on their original conversation.
- Sent/canceled messages, transcript rows, rules-refresh messages, task reply routing
  and historical task session links remain on their original sessions. Restoring A is
  not a merge of two transcripts. Prompt-on-start, if provided, uses the current queue
  ordering after launch notes and interrupted-turn continuation, exactly once.

Apply the same no-ambiguous-replay rule to the newly surfaced manual Fresh transition;
do not preserve the unsafe assumption that Pending means never submitted. Preserve
unrelated specialist task-migration behavior behind its existing policy; explicit
historical selection refuses an occupied specialist seat instead of remapping work.

### D-8: Recovery and adoption limits are deliberate

No automatic historical search in the supervisor, no automatic conversation fork,
no retry budget that becomes consent, and no new ownership-adoption endpoint. Old
counts/settings do not create a latent Fresh permission after upgrade. A manual Fresh
request is consumed by that one accepted launch; its failures subsequently retry that
new identity without authorizing another new conversation.

An agent with repairable configuration or unproven ownership can remain held. The UI
must distinguish that from a provider-confirmed missing target; neither a refusal nor
a hold says history was deleted. No change to transcript claiming, bracketed paste,
Enter timing, matching UserPrompt delivery verdicts or native rollout files is allowed.

## Implementation slices

| Slice | Files / changes | Required test targets |
|---|---|---|
| S1: outcome and ownership model | `server/Domain/Entities/AgentSession.cs`, `AgentSupervisionState.cs`, new small enums; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated migration. Add backoff/outcome/generation fields, immutable historical owner, continuity hold, conservative legacy backfill. | New ownership/backfill tests; outcome transition tests; migration defaults on pre-change state, including orphaned historical-only ownership and conflicting evidence. |
| S2: identity-safe supervision | `server/Application/Services/AgentSupervisorService.cs`, new concrete failure policy, `AgentSessionService.cs`, `AgentSessionRuntime.cs`, settings and validator/composition-root warning. Account sync/async failures once, distinguish cancellation, use backoff count, remove threshold Fresh. | Extend `AgentSupervisionTests`; new focused classifier tests with actual 57P03 and wrappers; `CapacityRecoverySupervisionTests`, `HerdrSupervisionBackoffTests`. Replace `Supervised_fresh_threshold_restart_carries_TabLabel_and_previous_pane_hint` with repeated-resume assertion and a separate explicit-Fresh placement case. |
| S3: strict standing resume and decision | `AgentSessionService.cs`, `AgentSessionLaunchQueue.cs`, typed runner failure mapping/native-store probe if needed; new continuity-state helper; `AgentIncidentKind.cs`. Remove standing same-ID create fallback and Grok downgrade, preserve card-only behavior. | `AgentControlServiceIntegrationTests` missing-target tests change to hold/no-create; `GrokNativeSessionResumeTests` distinguish strict standing from existing card/native behavior; `HerdrAlwaysOnChannelParityTests` cover real queue/runner adapter with fake provider. |
| S4: selected owned session through Start | `AgentControlService.cs`, `AgentService.cs`, `AgentDtos.cs`, `AgentEndpoints.cs`; focused standing-ownership/selection helper. Atomic reservation, history query, request flags, preflights, safe message handling and worker-generation checks. Stamp ownership on standing `AttachHerdrAsync` too. | Extend `AgentControlServiceIntegrationTests` and add focused standing-recovery tests: overwritten pointer, foreign/legacy owner, pending card/tasks/messages, same-target idempotency, Start/Stop/supervisor races, rejected request has zero side effects. |
| S5: surfaced decision and operator UX | `AttentionDtos.cs`, `AttentionService.cs`, Agent supervision DTO mapping; `client/src/api/agents.ts`, attention API types, `features/agents/AgentsPage.tsx` (extract focused history/recovery component), `features/attention/attentionVisuals.ts`. | `AttentionServiceTests`, new continuity Attention tests, `AgentsPage.test.tsx` or focused recovery component tests, `attentionVisuals.test.ts`: held reason survives pruning, exact request payloads, no automatic fallback on 409, queued vs running display. |
| S6: owner docs and regression | Update `docs/session-runtime-invariants.md`, `docs/ops-http.md`, `docs/antiphon-api.md`, `docs/agent-kinds.md` and applicable supervision/settings documentation. Document inspect/Stop/select/retry/Fresh and legacy refusal, deprecate threshold. | Run targeted backend classes sequentially with any Pty assembly run; client affected tests/build. Retain card-only resume, native identity, Herdr placement/holds, capacity pacing, launch notes/current stamps and prompt-delivery regression evidence. |

## TestDesign handoff

Write the separate verification design into this plan before Code. Define V/R rows
with test methods, state assertions and exact regression filters, and a small set of
meaningful positive controls. At minimum, bind these independent defects:

1. Several real/wrapped Postgres 57P03 failures from synchronous resume composition,
   then recovery: unchanged ordinary count/target, increasing capped backoff, no create.
2. An asynchronous launch failure after API success produces the same result; cleanup
   exit and repeated/reconstructed ticks cannot double-charge or override evidence.
3. Failure of outcome persistence itself leaves Unknown and no Fresh authority;
   successful recovery subsequently uses a fresh scope and the original identity.
4. Non-caller TaskCanceledException backs off; requested cancellation propagates.
   Genuine independent process failures still grow the ordinary/backoff ladder and
   healthy uptime still resets it. Existing Herdr/capacity holds retain precedence.
5. Positive missing target holds with exactly one resume process attempt, no new row
   and no same-ID create. Infrastructure/stale output and Grok I/O errors are negative
   controls. Include post-upgrade large old counters and configured threshold 0/1.
6. A -> explicit/legacy fresh B -> Stop -> `resumeSessionId:A` launches with A's native
   resume identity and current composed config, updates pointer, and preserves both
   histories. Include an old A owned only by unambiguous historical evidence.
7. Foreign/unproven owner, same cwd/name, pool/card/worktree, wrong kind/cwd, live/pending
   target/current, pending card work, and occupied execution tasks refuse with no
   pointer/latch/message/queue changes. No request flag bypasses other launch gates.
8. Concurrent accepted/rejected Start/Stop/supervisor operations launch at most one
   authorized generation and clean up any superseded process. Same-target repeated
   requests enqueue neither a second launch nor a second prompt.
9. Only never-attempted messages move atomically on a permitted switch. Ambiguous
   attempted input refuses; original baselines, task references and transcript records
   remain intact. Test the source pointer changes between preflight and reservation.
10. Durable Attention outlives incident pruning/restart; repaired retry, selected resume
    and manual Fresh have distinct audit records. Only a valid accepted request clears
    the hold. API acceptance is never asserted as proof of successful native resume.

Do not call a test green merely because the request returned 200 or `PersistentSessionId`
did not change: assert adapter/native argv, number of starts, stored outcome and queue
behavior. Use the established fake adapters and isolated fake runner; no real model
turns or production runner. Global-supervisor suites use ungrouped `[NotInParallel]`
and row-scoped assertions; queue clocks must advance over real time or use the approved
auto-advancing fake. Positive controls follow the owner doc's exact-method red/green
procedure, including fresh executed-test counts.

No tests/builds ran during Plan; this artifact changes no runtime code. Code must use
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c466/ --
--treenode-filter <TestDesign filter>` and `pwsh -File scripts/test-client.ps1 <filter>`
as applicable. TestDesign owns the precise filters and PC list. Do not start the shared
stack, migrate the live database or perform actual orchestrator recovery for acceptance.

## Verification design

Authored by TestDesign task `7b848110` against Plan commit `569ce66f`. The V rows
below are requirements for Code, not executed-test evidence. New class/method names
are prescribed targets; update this mapping if implementation needs to rename them.
All new backend classes below live in `tests/Antiphon.Tests/Application/` and use
namespace `Antiphon.Tests.Application`. Keep D-1 through D-8 unchanged.

### Evidence and fixtures

- Use real PostgreSQL through `TestDbFixture`, fresh service scopes for each tick or
  competing request, and fresh `AsNoTracking` reads for assertions. Reuse
  `AgentSupervisionTests.BuildHarness` and the control/Herdr parity fixtures, extending
  their adapter and runner seams. Whole-supervisor sweeps require ungrouped
  `[NotInParallel]`; all assertions and cleanup identify fixture-owned agents/rows.
  Migration tests use `TestDbFixture.CreateIsolatedSchemaAsync`, never downgrade the
  shared schema. Tag pure classifiers Unit and database/worker/HTTP tests Integration.
- Inject a real `Npgsql.PostgresException` with SQLSTATE `57P03` at the intended
  composition/read/save seam; do not stop PostgreSQL to produce it. EF interceptors
  must identify the operation and fixture row, not fail every command or the nth
  unrelated save. Gate worker start/ready and runner calls with bounded
  `TaskCompletionSource` barriers. Verify each injection/barrier was reached, release
  barriers in `finally`, and await every worker before disposing its fixture.
- Record adapter construction, start specs (including native identity flags), kills,
  disposal and typed input. A preflight failure can correctly have zero process
  attempts; an asynchronous missing-target failure must have exactly one resume
  attempt and zero create attempts. Check both, rather than merely counting DB rows.
  Use only synthetic environment values in captured specs and assertions.
- For each attempt record the persisted `(SessionId, StartedAt)` before observation,
  `RestartFailureKind`, `InteractiveLaunchCompletedAt`, both failure counters,
  consumed generation, `NextRestartAt`, escalation and continuity hold. Compare the
  database-rounded generation, not an unpersisted timestamp. Advance enough to create
  distinct generations on the same row. Rebuild the service provider for durability
  cases so in-memory deduplication cannot make them pass.
- Supervisor-only arithmetic may use a controlled clock. Any clock reaching queue
  polling must advance over real time or use the approved auto-advancing fake. Assert
  due-time deltas within a small stated tolerance for that clock; do not sleep through
  real backoff. At count `n`, expect `min(5 seconds * 2^n, 30 days)` under default
  settings. An initial observation with no failed attempt schedules at count zero.
- A rejected selection compares pre/post pointer, status, owner, session generation,
  continuity/human/liveness latches, queue rows, execution links, transcripts and
  accepted-decision incidents; expect no mutation, no enqueue, no spawn, no kill and
  no typing. Test an independently requested Herdr reset under its existing policy;
  continuity flags themselves never acknowledge it.
- Queue success requires a matching owning `UserPrompt` after the recorded baseline,
  plus the expected number/order of writes. API 200, Running before boot completion,
  a stable session GUID, a changed bundle stamp or a screen redraw alone cannot pass
  a recovery test. Fake-native history markers and argv establish identity in these
  tests; they do not claim live-model policy adoption or restore overwritten history.

### V rows: classification, pacing and durable outcomes

| ID / decision | Exact test target | Setup and required verdict |
|---|---|---|
| V-01 / D-2 | `RestartFailureClassificationTests.Infrastructure_wrappers_and_cancellation_are_classified_by_evidence` | Parameterize actual 57P03, `DbUpdateException` and ordinary wrappers around it, nested/multi-leaf `AggregateException`, transient `DbException`, `HttpRequestException` with connection-refused inner exception, timeout, and unrequested `TaskCanceledException`/`OperationCanceledException`. Expect Infrastructure through all wrappers. Repeat with stale missing-session screen text: it cannot win. Requested caller/host cancellation propagates in service tests and is not a failed attempt. Nontransient database/configuration failure and a message merely containing `57P03` or a missing-session phrase are negative controls for positive native-loss evidence. Typed native-missing evidence is ContinuityUnavailable; unclassified evidence is Unknown. |
| V-02 / D-1, D-2 | `StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff` | Seed stopped owned Claude A with ordinary count 7, an already-consumed terminal generation and a due retry. Fail three successive synchronous composition attempts with wrapped 57P03 before restamping/enqueue; recover on the fourth. Ordinary count stays 7, backoff advances once per failed attempt, no continuity hold and no new session. Ticks before due do not call Start; due retry finally starts exactly `--resume A`, never create. Repeat settings `FreshAfterResumeFailures=0,1,2,int.MaxValue`; no value restores Fresh. |
| V-03 / D-3 | `StandingRestartAccountingTests.Async_infrastructure_failure_is_consumed_once_across_recreation` | Start returns while the worker is gated. Fail after enqueue, including after the early Running save but before boot completion. Original Infrastructure outcome persists; completion remains null; cleanup kills/disposes its adapter. Deliver the cleanup exit, repeat ticks, reconstruct all services, then tick again. Exactly one backoff charge and zero ordinary charges for that generation; exit must not overwrite the cause. Retry restamps the same row, clears old evidence/completion, and a second failed generation can be charged exactly once. An owned worker has no terminal charge while still classifying its catch. |
| V-04 / D-3 | `StandingRestartAccountingTests.Sync_failure_after_restamp_is_not_charged_again_by_terminal_observation` | Inject synchronous failure after a reservation/restamp has committed but before enqueue. Accounting records and consumes that generation once. A later dead-row observation does not add a second charge. Contrast failure before restamp with an older consumed death: retain the old consume key and do not charge the old death again. Use separate contexts for the catch and observer. |
| V-05 / D-2, D-3 | `StandingRestartAccountingTests.Failed_outcome_save_recovers_as_unknown_without_fresh_authority` | Let a worker fail, then fail its outcome save and subsequent bookkeeping while storage is unavailable. Observe bounded hosted-tick retries/logging, no loop saving the same failed context, no catch-path second launch, and no invented outcome/completion. Restore storage, reconstruct scopes, and observe a terminal incarnation through reconciliation. Charge Unknown at most once only when terminal evidence exists, keep ordinary count unchanged, and eventually resume A. Do not require an exact durable failure count during the database outage; D-3 explicitly excludes that promise. |
| V-06 / D-2 | `StandingRestartAccountingTests.Timeout_retries_but_requested_cancellation_does_not_charge` | Exercise runner probe, synchronous Start and worker timeout with active request tokens; each loop remains usable and the failed-attempt path backs off without ordinary increments. Cancel the supplied caller/host token at the equivalent gates: cancellation propagates and records no failed-attempt charge/continuity hold. If a process already started, assert cleanup/ownership. A failed observation-only runner probe authorizes no launch and invents no failed incarnation. |
| V-07 / D-2, D-3 | `StandingRestartAccountingTests.Real_failures_cap_escalate_and_reset_only_after_healthy_completion` | Complete boot, then deliver independent process exits on distinct persisted generations; also inject a confirmed non-infrastructure launch failure. Both counters increase once per failure, repeated observations do not. Exercise exponential boundaries, hourly/daily escalation once per tier, and the 30-day cap with large counts (no overflow). Successful enqueue/Starting and early Running before boot completion do not reset; just before healthy uptime does not reset; completed boot plus healthy interval resets both counters, due time and escalation once. Resume identity remains A throughout. |
| V-08 / D-1, D-8 | `StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity` | A genuinely new agent can create its first row. Missing/malformed nonempty pointer, incompatible prior kind/cwd, and unresolved prior ownership refuse/hold without create. Native-unsupported kinds retain their ordinary behavior with `ResumeUnsupported` audit, but explicit selection is refused; changing an existing supported conversation to an unsupported kind requires explicit Fresh. No lookup of the newest historical session substitutes for a user selection. |

### V rows: explicit decisions and strict native resume

| ID / decision | Exact test target | Setup and required verdict |
|---|---|---|
| V-09 / D-1, D-4 | `StandingContinuityRecoveryTests.Missing_native_target_holds_after_one_resume_without_create` | Seed owned cardless A with native identity and history marker. Raise typed `ResumeTargetMissingException` after its adapter starts. Exactly one resume spec, no second adapter/create, no new row and no rewritten native history; normal cleanup completes. Persist NativeSessionMissing hold for A, no ordinary increment, and no automatic launches across repeated due ticks/service recreation. Retain TabLabel and the proper resume placement hints. |
| V-10 / D-2, D-4 | `StandingContinuityRecoveryTests.Grok_strict_resume_distinguishes_missing_from_unavailable_storage` | Standing Grok cases cover Found, authoritative Missing, and I/O/access Unavailable. Found emits only `--resume A`; Missing holds without `--session-id A`; Unavailable takes Infrastructure recovery without a continuity-loss verdict or create. Include the runner's `GrokNativeSessionMissing` refusal and rules-migration refusal: the latter keeps its existing explicit-migration policy and never becomes native-missing or permission to Fresh. Bind effective launch home rather than the test process home. Preserve explicitly card-scoped legacy behavior in R-03. |
| V-11 / D-4, D-8 | `StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions` | While held, ordinary Start yields 409 `standing_continuity_held`. Repair A then accept `retryContinuity:true`: one resume A and one retry decision. Select owned C: one selected-resume decision with old/new targets. Accept explicit Fresh: one new B row and explicit discard reason, preserve A/owner/history, and clear continuity hold only with accepted reservation. Fail B afterward: subsequent automatic attempts resume B and do not create C. Failed retry restores/updates the hold; queue acceptance and completed resume are separate facts. |
| V-12 / D-4, D-6 | `StandingSessionSelectionTests.Recovery_options_cannot_bypass_existing_start_guards` | Parameterize retry, selection and Fresh against applicable quota, sign-in, model/capacity, configuration, specialist and Herdr holds; test automatic/capacity callers separately from manual callers. Existing allowed supervisor quota policy remains intact. Rejected recovery retains continuity hold and has the rejection snapshot above. Continuity flags do not clear human/liveness intent before validation or reset Herdr; a valid manual start may perform its established latch clear at acceptance. Cover both AlwaysOn values. |
| V-13 / D-4 | `StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts` | Persist each hold reason, prune its incident, recreate services and disable AlwaysOn. Exactly one row-scoped StandingContinuityDecision remains with agent/target/cause/open links. Stop and alert acknowledgement do not resolve it. Hold/selection/Fresh incidents use `raiseAlert:false`, create no duplicate incident-derived Attention and preserve unrelated infrastructure alerts. Only accepted retry/selection/Fresh resolves the durable row; a rejected attempt does not. Evidence is bounded and uses synthetic metadata, with no transcript/environment payload. |

### V rows: historical ownership and Start selection

| ID / decision | Exact test target | Setup and required verdict |
|---|---|---|
| V-14 / D-5, S1 | `StandingSessionOwnershipTests.Upgrade_backfills_only_unambiguous_owners_and_preserves_recovery_state` | In an isolated schema migrate to the predecessor, seed legacy data using the old schema, then apply the actual CLI-generated migration. Counts 0/2/large move to RestartBackoffFailures, ordinary counts reset to zero, due/escalation and human/liveness/capacity/Herdr state remain. No invented completion/outcome/continuity hold. Backfill current-pointer, historical execution-only and allowed lifecycle-incident-only ownership when unique, including A whose pointer moved to B. Multiple agreeing sources are allowed; contradictory owners, ParentSessionId-only, unrelated incident kinds, pool/card/worktree and no evidence remain unstamped. Assert index/model alignment and no pending model changes. |
| V-15 / D-5 | `StandingSessionOwnershipTests.Stamped_owner_survives_pointer_change_attach_and_agent_recreation` | Normal standing creation and validated Herdr attach stamp the physical agent before enqueue; resume preserves it. Moving pointer to B preserves A's owner. Delete the agent through the existing flow and create the same name/cwd with a new GUID: history is neither cascaded nor reassigned and the new agent cannot select A. Pool/card creation never gains a standing owner; conflicting stamped ownership cannot be overwritten through attach, Start or generic PATCH. |
| V-16 / D-5, D-6 | `StandingSessionSelectionTests.Legacy_historical_owner_can_resume_after_pointer_moved` | Release-critical sequence: seed legacy A with null owner and only an allowed historical execution link (repeat allowed incident-only), current pointer on stopped B. GET history discovers A without writing its owner. POST Start with `resumeSessionId:A` revalidates evidence, stamps owner atomically, queues the shared resume path and ultimately starts native A with current composition. Preserve A/B transcripts and task history; a child task pointing to A only as ParentSessionId neither proves ownership nor blocks otherwise-proven parent recovery. |
| V-17 / D-5 | `StandingSessionSelectionTests.Foreign_conflicting_or_unproven_ownership_refuses_without_side_effects` | Foreign stamped owner returns `standing_resume_not_owned`; absent/pruned/contradictory legacy evidence returns `standing_resume_owner_unproven`. Test current pointer to A by another agent, competing historical execution/lifecycle links, same cwd/name, and caller knowledge of A's GUID. No bypass or history adoption; enforce the full rejection snapshot. Add conflicting evidence between GET/preflight and reservation to prove Start rechecks. |
| V-18 / D-6 | `StandingSessionSelectionTests.Invalid_or_busy_targets_refuse_before_reservation` | Data cases: missing A (404); nonexclusive option pairs (422); unsupported/wrong kind or canonical cwd mismatch; pool/card/worktree target; target Created/Starting/Running/Stopping, owned worker, or DB-dead but runner-live; unavailable runner probe; other current live/queued session (409 `standing_resume_current_active`); spawnable current/queued card (409 `standing_resume_card_work_pending`). Assert stable Problem Details codes and the full rejection snapshot. Equivalent canonical paths accepted under existing OS comparison are the positive control. |
| V-19 / D-6, D-7 | `StandingSessionSelectionTests.Owned_history_uses_current_composition_and_native_resume_identity` | A -> explicit Fresh B -> Stop -> select A, for Claude/Grok and AlwaysOn on/off. Between A and selection change bundles/profile/model/synthetic environment, notes and backend placement. Assert fresh current stamps/specs, A's native resume flag exactly once, A as persistent pointer before worker execution, B retained, and no historical argv/environment reused. Grok uses current receipt/rules barrier; remote-control and interrupted-turn continuation retain existing order. A second identical request cannot duplicate the launch/prompt/accepted-decision incident. |
| V-20 / D-6 | `StandingSessionRecoveryHttpTests.History_and_start_preserve_wire_contract_and_revalidate_eligibility` | Use a guarded real Program test host with fake/refusing runner. Exercise camelCase flags and invalid combinations through HTTP, not only direct service calls. History is bounded/stably paged (including equal CreatedAt values and cursor boundaries), read-only and excludes foreign history; expose only prescribed metadata/eligibility/refusal codes. No secrets, hashes, environment or transcript content. Change eligibility after GET: Start refuses rather than trusting the response. Accepted Start returns normal AgentDetailDto as queued; force later worker failure and observe its durable hold. |

### V rows: concurrency and safe queued work

| ID / decision | Exact test target | Setup and required verdict |
|---|---|---|
| V-21 / D-7 | `StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation` | Independent PostgreSQL contexts contend: two selections of A; selection A versus default/supervised Start; selection A versus Fresh. Gate both after preflight, then race reservation. One accepted generation/enqueue/prompt/decision; same-target repeats are no-ops, incompatible loser refuses/revalidates. Reload committed pointer, owner and generation. Pause the fake runner/composer RPC and prove another context can acquire the agent lock within a bounded timeout: no transaction spans external I/O. |
| V-22 / D-7 | `StandingSessionSwitchConcurrencyTests.Stop_supersedes_launch_before_spawn_and_before_typing` | Stop at gates before worker spawn, while start RPC is pending, and after ready before boot writes. Release the obsolete worker: before-spawn case launches nothing; late-created process is killed/disposed; no obsolete notes/RC/prompt and no overwrite of stopped status/latch. Repeat with a newly accepted B generation after Stop: only B is authorized/owned and A's late completion cannot erase it. Verify actual process/fake-adapter ownership, not only final DB status. |
| V-23 / D-7 | `StandingSessionSwitchConcurrencyTests.Reservation_rechecks_source_pointer_work_and_delivery_evidence` | Gate selection after preflight and independently change B's pointer/generation, add an execution assignment/spawnable card, add a competing ownership claim, or start delivery of B's pending message before reservation. Request refuses or revalidates from the new state; it must never move stale B messages or launch using stale authority. Include the reverse order: reservation wins, stale queue delivery cannot type the moved row on B. Gate rollback during reservation save: pointer/owner/hold/queue/decision all roll back and no worker is enqueued. |
| V-24 / D-7 | `StandingSessionQueueSwitchTests.Only_unattempted_messages_move_atomically_and_keep_order_and_routing` | B has never-attempted Pending Ui/Channel/completion/scheduled messages, future holds, Sent/Canceled rows and rules-refresh rows; A already has its own queue and transcript. Accepted selection moves exactly the safe non-rules set with original IDs/body/origin/correlations/hold times/parking policy. Preserve each source's FIFO order and keep existing A work ahead of appended B work; allocate collision-free target Sequence values where needed, without changing A's existing sequences. Sent/Canceled/rules/history/task reply links stay put. After readiness/notes/continuation, flush ordinary input in the resulting order, initial prompt once, and confirm matching owning UserPrompts. Future-held messages retain their existing eligibility semantics. |
| V-25 / D-7 | `StandingSessionQueueSwitchTests.Any_prior_delivery_evidence_refuses_switch_and_fresh` | For selection and manual Fresh, vary one B Pending field at a time: DeliveryAttempts > 0, LastDeliveryStartedAt, LastDeliveryBaselineSequence (including zero), DeliveryVerdict, DeliveryVerdictAt, residual SentAt/settlement evidence; include attempted-at-limit parked input and a mixed safe/unsafe queue. Return `standing_resume_delivery_pending` with queue links; no partial safe-row move, no new Fresh row, no reset/cancel/retype of ambiguous input. Byte-for-byte evidence and pointer/latches remain. A Sent row with null verdict remains on B for existing interrupted-attempt recovery, never copied as fresh input. |
| V-26 / D-7 | `StandingSessionQueueSwitchTests.Target_late_confirmation_and_open_task_guards_remain_intact` | A has attempted Pending input with stored baseline and a later matching UserPrompt. Selecting A retains that evidence; late-confirm marks it without typing again, and an unconfirmed parked A row remains parked. Open execution assignments Dispatched/Working/Blocked on either A or B refuse selection with `standing_resume_work_in_flight`, without task remapping. Completed historical executions and child ParentSessionId references do not block. Keep existing specialist Fresh migration behavior scoped to its established policy. |
| V-27 / D-4, D-6 | `client/src/features/agents/StandingSessionRecovery.test.tsx` (new) and `attentionVisuals.test.ts` | Test named user interactions: `retry sends only retryContinuity`, `history selection sends the Antiphon session id`, `fresh shows the discard consequence and sends only fresh`, `refusal retains the hold and never retries as fresh`, `queued start is not shown as recovered`, and `missing and unproven history have distinct explanations`. Verify selected target display, disabled ineligible choices/open links, loading/double-click dedupe and query refresh after a decision. Exercise AgentsPage wiring too; merely opening history/Attention performs reads and no Start/mutation calls. |
| V-28 / D-1, D-4, D-6 | `HerdrAlwaysOnChannelParityTests.Standing_history_recovery_preserves_native_identity_and_queued_reply` | Small integration matrix: Claude/Grok across PtyHost and Herdr using isolated fake provider/runner lanes (extend the existing parity harness; its PtyHost stub alone is not native-wire evidence). Run missing-target/no-create and owned A -> B -> Stop -> A recovery, including preserved native-history marker and current safe queued channel input/reply. Assert actual runner launch request/provider argv, one authorized process, native A history, matched input and one fake-gateway reply. Herdr tab/last-pane behavior and rules barriers remain valid. Never point fake traffic at the live broker. |

V-24 makes relative queue ordering explicit: moved B rows append after A's existing
queue, preserving B order; per-session numeric Sequence values are not portable IDs.
The accepted-reservation rollback and delivery race in V-23 must exercise the same
database/queue synchronization that production uses, not a sequential mock substitute.

### Positive controls

Run these seven defect mutations red then restored green during Code. They target
independent safety failures; do not substitute broad baseline regressions. Each row
names exactly one method. With the common command below, replace `<Class>/<Method>`
with that row's target and record the mutation, executed cases, assertion and result.

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c466/ -- --treenode-filter "/*/*/<Class>/<Method>" --report-trx --report-trx-filename <unique-pc-phase>.trx
```

| ID | Mutation | Exact class/method and required red assertion |
|---|---|---|
| PC-1 | Classify wrapped actual 57P03 as LaunchOrProcessFailure at the policy boundary. | `StandingRestartAccountingTests/Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff`: ordinary count changes from 7, failing the unchanged-count assertion. |
| PC-2 | Bypass consumed `(SessionId, StartedAt)` comparison for terminal Infrastructure evidence. | `StandingRestartAccountingTests/Async_infrastructure_failure_is_consumed_once_across_recreation`: repeated/reconstructed observation charges the same incarnation twice. |
| PC-3 | Restore the standing `ResumeTargetMissingException` same-ID create fallback. | `StandingContinuityRecoveryTests/Missing_native_target_holds_after_one_resume_without_create`: a second/create spec or missing hold fails, even if the session ID is unchanged. |
| PC-4 | Accept historical ownership without checking distinct conflicting owners. | `StandingSessionSelectionTests/Foreign_conflicting_or_unproven_ownership_refuses_without_side_effects`: contradictory historical-link case accepts/stamps/enqueues instead of refusing. |
| PC-5 | Skip the worker's post-ready pointer/generation/intent check. | `StandingSessionSwitchConcurrencyTests/Stop_supersedes_launch_before_spawn_and_before_typing`: the post-ready Stop case types stale work or revives an obsolete generation. |
| PC-6 | Treat Pending plus zero attempts alone as safe, ignoring baseline/timestamp/verdict evidence. | `StandingSessionQueueSwitchTests/Any_prior_delivery_evidence_refuses_switch_and_fresh`: zero-attempt baseline-only case moves or starts instead of refusing. |
| PC-7 | Clear the continuity hold before normal Start guards accept the reservation. | `StandingSessionSelectionTests/Recovery_options_cannot_bypass_existing_start_guards`: rejected model/auth/config case loses the hold. |

Follow [testing and build: Code-stage positive-control execution](../../testing-and-build.md#code-stage-positive-control-execution-card-0451):
exact-method filters for both phases, unique fresh TRX, nonzero executed counts and
expected assertion failures. Build/fixture errors, zero tests or list-tests output
are not red evidence. Batch only mutations in independent files/methods; PC-5 and
PC-7 may overlap the worker/control refactor and must be checked before batching.
Restore every mutation, refresh source timestamps and rebuild so green executes the
fixed DLL. Do not edit files while their test/build runs. Finish with regression on
the unmutated implementation; report every PC separately.

### R rows and exact execution filters

Each class below is an exact regression target, not a namespace-wide recommendation.
Run the commands sequentially, checking exit status and fresh executed counts per
class; stop to diagnose a failure. New-class names must match the V mapping.

```powershell
$card466Classes = @(
    'RestartFailureClassificationTests',
    'StandingRestartAccountingTests',
    'StandingContinuityRecoveryTests',
    'StandingContinuityAttentionTests',
    'StandingSessionOwnershipTests',
    'StandingSessionSelectionTests',
    'StandingSessionRecoveryHttpTests',
    'StandingSessionSwitchConcurrencyTests',
    'StandingSessionQueueSwitchTests',
    'AttentionServiceTests',
    'AgentSupervisionTests',
    'AgentControlServiceIntegrationTests',
    'GrokNativeSessionResumeTests',
    'GrokRulesResumeMigrationTests',
    'AgentSessionLaunchFailureTests',
    'AgentSessionRuntimeTests',
    'SessionReconciliationServiceTests',
    'HerdrSupervisionBackoffTests',
    'HerdrSupervisionAttentionTests',
    'CapacityRecoverySupervisionTests',
    'PolicyRefreshServiceTests',
    'GrokRulesReadyOrderingTests',
    'SessionMessageQueueDeliveryVerificationTests',
    'SessionMessageQueueInterruptedAttemptTests',
    'SessionMessageQueueSupervisionTests',
    'HerdrAlwaysOnChannelParityTests'
)
foreach ($card466Class in $card466Classes) {
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c466/ -- --treenode-filter "/*/*/$card466Class/*" --report-trx --report-trx-filename "c466-$card466Class.trx"
    if ($LASTEXITCODE -ne 0) { throw "CARD-0466 failed: $card466Class (exit $LASTEXITCODE)" }
}
```

Use a new evidence directory/run identity or unique suffix when rerunning; do not
reuse an old TRX as evidence. `AttentionServiceTests` covers shared feed behavior
alongside the focused continuity cases. Do not widen to all Application.

| Regression | Targets from the command / additional exact filter | What must remain true |
|---|---|---|
| R-01: ordinary supervision and Start | `AgentSupervisionTests`, `AgentControlServiceIntegrationTests` | First launch, intentional Stop/liveness latch, current composition/notes/RC, explicit Fresh and placement. Replace the old automatic-threshold and two missing-target-fallback assertions with strict resume/hold cases; retain a separate explicit-Fresh TabLabel/pane-hint case. Rewrite counter-only crash fixtures to seed actual terminal generation evidence. |
| R-02: lifecycle and cleanup | `AgentSessionLaunchFailureTests`, `AgentSessionRuntimeTests`, `SessionReconciliationServiceTests` | Failure cleanup preserves original classification/termination source. Unclaimed live sessions are not killed. Reconciliation and ordinary runtime exits remain correct. Do not change/retest launch-extra replay as a new feature. |
| R-03: Grok policy boundary | `GrokNativeSessionResumeTests`, `GrokRulesResumeMigrationTests`, `GrokRulesReadyOrderingTests` | Existing missing-directory downgrade tests must explicitly exercise the retained card policy; new standing strict cases must pass through real callers. Native ID flag sanitation, launch-home precedence, explicit rules migration and queue barriers remain. |
| R-04: independent holds | `AttentionServiceTests`, `HerdrSupervisionBackoffTests`, `HerdrSupervisionAttentionTests`, `CapacityRecoverySupervisionTests` | Shared feed filtering/deduplication and existing hold precedence, once-per-incarnation accounting, human reset, capacity grants/pacing and no silent reroute. Continuity acknowledgement is not Herdr acknowledgement. |
| R-05: refresh and delivery | `PolicyRefreshServiceTests`, `SessionMessageQueueDeliveryVerificationTests`, `SessionMessageQueueInterruptedAttemptTests`, `SessionMessageQueueSupervisionTests` | Current composition on resume, matching UserPrompt evidence, Enter-only/late-confirm semantics, attempts/parking and working-session guards survive switching changes. Existing interrupted-message verification is distinct from excluded interrupted-startup durability. |
| R-06: backend parity | `HerdrAlwaysOnChannelParityTests` | Rename/update `Named_AlwaysOn_agent_lands_on_its_labelled_tab_across_crash_restart_and_fresh_threshold` to repeated same-conversation resume, with manual Fresh separate. Existing held-shell, placement and channel behavior plus V-28's native-wire assertions pass. |
| R-07: runner/native seam, when S3 touches it | `Antiphon.SessionRunner.Tests`: `/*/*/HerdrGrokResumeGuardTests/*`, `/*/*/GrokRulesRunnerRefusalTests/*`, `/*/*/GrokRulesStoreFailureTests/*`, `/*/*/PromptSubmissionMatchTests/*` | Invoke each with `dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c466/ -- --treenode-filter "<filter>"` and fresh TRX. Missing/unavailable distinction and argv refusals survive the HTTP boundary; no production runner or native home. Add only other directly touched class filters. |
| R-08: client | Commands below | Recovery payloads, Attention presentation, AgentsPage integration and type/build compatibility. Run `AgentsPage.test.tsx` for page wiring/shared start behavior as well as the focused component cases. |

```powershell
pwsh -File scripts/test-client.ps1 StandingSessionRecovery.test
pwsh -File scripts/test-client.ps1 attentionVisuals.test
pwsh -File scripts/test-client.ps1 AgentsPage.test
npm --prefix client run build
```

For an adapter/native-store change in `Antiphon.Agents.Pty`, add the exact changed
class filter using `dotnet run --project tests/Antiphon.Agents.Pty.Tests
--property:OutputPath=bin-c466/ -- --treenode-filter "/*/*/<TouchedClass>/*"`.
Run after Antiphon.Tests, never concurrently. Process-spawning fixtures require their
assembly-local `[ParallelLimiter<ProcessSpawnLimit>]`; no headed/live-model canary is
required by this card. Use the existing production-runner guard for real Program
hosts; V-28 owns isolated fake lanes and temporary stores. No AppHost restart, live
migration, broker traffic or real standing-session recovery is an acceptance step.

### Completion evidence for Code and Review

Provide the implementation commit, V-to-method mapping (including parameter cases),
per-class executed/passed/failed/skipped counts, fresh result paths, seven PC red/green
verdicts, and client/build results. Explicitly account for every required row; an
unexecuted native-wire arm or legacy historical-only recovery case is an acceptance
gap, not a passing claim. Review verifies no operative FreshAfterResumeFailures read
remains, explicit configuration emits only the planned startup warning, migration
preserves old recovery state, and current owner docs describe strict Start/history.
Unsupported-kind compatibility and card-only behavior remain explicit boundaries.

Interrupted-startup replay, persisted launch extras/durable launch jobs, automatic
ownership adoption and live production recovery remain excluded. Service recreation
in V-03/V-05/V-13 proves stored evidence/holds survive; it does not authorize resuming
an interrupted named launch or claim its lost boot inputs became durable.

TestDesign changed only this plan. No tests/builds were run at this stage; document
structure, target names and whitespace are checked before handoff.

## Completion and landing

Plan and TestDesign are complete under D-1 through D-8; no operator answer is required.
Next stage is Code. The TestDesign worktree initially lacked the artifact, so its
branch was fast-forwarded to the existing Plan commit `569ce66f` before editing;
the TestDesign branch contains both stages. The caller must land this task through
`scripts/delegate.ps1 -Land 7b848110` before dispatching Code in a new worktree, and
confirm that dispatch base contains the combined artifact. Interrupted-startup
durability stays a separate follow-up.
