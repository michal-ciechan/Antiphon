# CARD-0466: preserve standing-session continuity through infrastructure failures

Status: Plan complete; separate TestDesign required before Code.
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

## Completion and landing

Plan is complete under D-1 through D-8; no operator answer is required to author it.
Next stage is TestDesign. The caller must land this plan task through
`scripts/delegate.ps1 -Land 81da7ca2` before dispatching TestDesign in a new worktree,
so that worktree contains the committed artifact. Implementation should receive the
resulting plan plus verification section; interrupted-startup durability stays a
separate follow-up.
