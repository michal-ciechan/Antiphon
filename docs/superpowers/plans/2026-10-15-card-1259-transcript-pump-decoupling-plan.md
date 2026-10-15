# CARD-1259: persist transcripts independently of delivery confirmation

Plan task `8b2dbcd8`, 2026-10-15. Inspected source:
`8ffe3633357e6383b7e408bd078465acf9f5fd79`. This is a design, not an implementation
or a passing verification report. **Next: TestDesign**, separately dispatched;
the brief did not fold that stage into Plan. No production changes, builds,
tests, provider input, or external messages were made in this task.

The change must remove the dependency in which the runner event reader awaits
delivery confirmation whose UserPrompt is still waiting to be read. Keep the
reader ordered and move transcript-triggered effects to owned, bounded workers.
Persist their work with the transcript, so a full worker queue cannot recreate
the dependency or silently discard a turn boundary.

## Evidence and ground truth

Read the live Antiphon card and the complete `result` of Investigate task
`ab022b12` through the task API. The desktop report referenced by that result is
[the retained evidence](../../investigations/2026-10-14-card-1259-live-transcript-ingestion-lags-the-cli-by-28-s-delivery-verifier-presses-enter-again-and-fails-at-30-s-desktop-evidence.md).
The [earlier investigation](../../investigations/2026-10-14-card-1259-live-transcript-ingestion-lags-the-cli-by-28-s-delivery-verifier-presses-enter-again-and-fails-at-30-s.md)
and [CARD-0432 D-5/S3](2026-09-07-card-0432-distiller-timeouts-plan.md) also matter:
S3 was explicitly deferred, not implemented when that card closed. This plan
supersedes its pump slice for this card; it does not reopen its distillation work.

| Card/brief assumption | What this checkout and evidence actually show | Consequence |
|---|---|---|
| The ingest path can wait for its own evidence. | `SessionRunnerEventPump.cs:53,73,420` awaits handlers serially; `AgentSessionRuntime.cs:382,402,649` stores a boundary then awaits `OnTurnEndAsync`; `SessionMessageQueueService.cs:1561,1570,3950` holds the queue lock while awaiting a stored prompt. | Confirmed defect; remove the wait dependency. |
| Confirmation periodically catches up an ordinary observable session. | The pull gate at queue service `:4083` is `fullInline || !observable`; ordinary observable confirmation waits on stored evidence. The failure grace pulls at `:4243`. | A longer 30-second timeout would preserve the dependency. Keep the verifier unchanged. |
| The morning Family incident's exact handler was measured. | It was not. The passive capture proves 31.913968 seconds from independent runner receipt to DB stamp, following runner receipt in 0.469977 seconds. The server's own handler entry/stack was not captured. | Qualify the dependency, not a claim to have reconstructed every original hop. `CreatedAt` is assigned before save, not a commit timestamp. |
| Near-30-second delays recur. | The latest Investigate result sampled October 8–15: 6,797 desktop prompts, p95 30.666 seconds, 376 in 29–35 seconds. The older artifact's different October 7–14 window has 361. | Preserve both observations and their windows; neither is a timeout-sizing measurement. |
| S0 is an acceptance test. | `SessionRunnerEventPumpTests.S0_blocked_hosted_pump_probe` has four cases and asserts B **does not** persist during a hold. Its queue arm holds `OnSubmitted`, then inserts rows directly into the DB. | Flip its blocking oracle and add a real pump-to-verifier regression; S0 alone cannot prove natural confirmation. |
| Only the last queue callback can block. | Runtime also awaits refinement confirmation, manual-compaction flushing, status-unblock flushing, compaction enqueue, channel/review/task dispatch. Task routing can enqueue into a parent session. `ConfirmSentRefinementsAsync:2737` has an unlocked absence check (CARD-1188), but still waits on the queue lock when a refinement exists. | Offloading only `queue.OnTurnEndAsync` is insufficient. Audit the whole transcript-effect call graph, including cross-session destinations. |
| Reconnect is independent of delivery. | Pump `:199–204` calls `SyncTranscriptAsync` sequentially; runtime `:945–996` still runs settlement and queue effects inline. | Apply the same boundary to initial catch-up, subscription-ready catch-up, and held transcript payloads. |
| Transcript writes already have the needed serialization. | `PersistTranscriptAsync:1084` takes `SessionStateStore.BeginWriteAsync`; `PersistTranscriptCoreAsync` dedups `(Uuid,Kind)`, rebases sequences, saves, and publishes committed state under that lease. The unseen-boundary read is currently outside it, and persistence currently uses `CancellationToken.None`. | Reuse the store lease, move action eligibility inside it, and preserve batch/fallback publication. Do not add parallel transcript writers. |
| A bounded in-memory channel alone is safe backpressure. | A waiting channel writer can block ingress before the confirming prompt; dropping loses effects. `CompletionNoteFlushQueue` has a durable **completion-note-specific** recovery source, not arbitrary runtime actions. | A channel is a nonblocking hint; pending transcript effects need their own durable recovery source. Do not repurpose the completion-note worker. |
| Deferred callbacks can safely ask for the newest turn. | Channel, review and task dispatchers contain latest-turn selection. Ingestion will now advance while they wait. Review correlations currently live in `ReviewReplyDispatcher._pending`, although the queue already stores `review:{threadId}` and the body. | Bind deferred routing to stored turn identity; recover review targets from authorized queue rows, never from model-written tags alone. |

Paths abbreviated in this table are under `server/Application/Services/` except
the pump under `server/Infrastructure/Agents/SessionRunner/`. Read owners:
`docs/project-context.md`, the relevant delivery/ordering/recovery sections of
`docs/session-runtime-invariants.md` and `docs/herdr-sessions.md`, and the stage
and checkpoint contracts in `docs/orchestration-loop.md` and
`docs/testing-and-build.md`.

## Decisions

### D-1. Keep ingestion serial; defer transcript effects after a durable commit

Split runtime ingestion from runtime effects. Live observation and pump catch-up
await only transcript storage, state-store publication, and durable action
admission. A hosted worker executes the former post-persistence callbacks,
including transcript SignalR publication. No worker completion, queue semaphore,
transport submission, channel send, review publication, or task settlement is
awaited by those ingress paths.

This is the smallest complete boundary identified in the current call graph.
Changing one await leaves nested queue waits; parallelizing whole handlers lets
one session's rows reorder; adding a second subscriber duplicates ingestion and
does not resolve action ownership. Unowned `Task.Run`, one task per event, and a
bounded `WriteAsync` on ingress are rejected. Native tailer offsets, verifier
polling, the 30-second window and Enter-retry policy are outside this fix.

Keep raw terminal-output, typed exit, bind/fault and adoption handling on their
current paths. They have separate I/O latency risks, but do not invoke the
confirmed queue-confirm chain. This card does not promise that arbitrary database,
raw-output SignalR or network stalls cannot delay the stream. The AgentStatus
blocked-exit branch must record a narrow flush request rather than await it.

### D-2. Use durable actions, not an unbounded heap of callbacks

Add `SessionRuntimeAction` and its EF mapping/migration. Each action carries:
session ID, accepted session generation when available, stable stored trigger
sequence/entry identity, trigger kind, next effect phase, creation/due times,
attempt count and bounded failure reason. Store no duplicate transcript body or
serialized DI callback. Unique action identity is `(session, stored sequence,
trigger kind)`; synthetic catch-up/status requests use distinct trigger kinds
and the current stored frontier. A new request for an existing active key wakes
it without rewinding an already completed phase.
Retain the producer sequence separately from the stored sequence where needed
to preserve the existing SignalR DTO. A delayed `SessionTranscript` remains a
display event, never a new committed-state or delivery-receipt authority.

Insert transcript-triggered actions in the **same database transaction** as the
corresponding new transcript rows. The batch fallback must preserve that property
for every individually committed row or safe stub. A duplicate/replayed source
must not create a fresh turn-end effect. Classify sibling TurnEnds by ApiCallId
inside the storage lease, including siblings in the same batch. Only committed,
eligible rows create effects; a persistence failure grants no delivery authority.

Keep one active action per stored transcript entry for ordered transcript UI
publication, with meaningful later phases only for relevant kinds. Delete an
action after its final acknowledged phase; completed transcript identities still
prevent replay from recreating it. Do not backfill every historical transcript
as fresh actions on upgrade. Existing owed-work recovery remains responsible for
pre-upgrade obligations; pump sync also admits a coalesced catch-up reconciliation
request even when no new transcript row was added, preserving CARD-0288.

Retention must exclude sessions with unfinished actions and recheck under the
existing transcript mutation lease. Protect the whole needed turn, not only its
boundary row. No expiry may delete unfinished work. This bounds heap use, not
the durable backlog during an indefinitely unavailable downstream service.

### D-3. Preserve transcript order and separate lock lifetimes

Use the existing singleton `SessionStateStore` write lease for stream, snapshot,
synthetic boundary and fallback writes. Include dedup, action eligibility,
sequence allocation, database commit and committed-state publication within it.
Production must not silently fall back to an unserialized writer when that
registration is absent; relevant harnesses must register the real store too.

Order remains queue lock (when an existing queue caller holds it) -> transcript
lease -> any existing session-row commit lock. The action worker takes neither
transcript lease nor a database transaction across effects. A session worker's
ownership token is not acquired by ingress or by queue verification. No shared
DbContext spans workers. `CatchUpTranscriptAsync` remains persistence-only from
its caller's perspective: it may record actions, but never waits for them or
re-enters a queue callback under the queue lock.

Thread cancellation through storage acquisition and database I/O. If cancellation
arrives after commit, reconcile/publish the committed store state before releasing
the lease (or invalidate it for a mandatory reload); do not pretend the committed
rows rolled back. Wakeup failure after commit leaves discoverable action debt.
Snapshot order-before-live and the held marker's one-offer/skip-copy contract stay
intact. A slow side effect must not extend subscription catch-up.

### D-4. Bound execution and make overflow recoverable

Use a dedicated concrete action coordinator plus hosted worker, with **four
workers, 128 queued session-ID hints and 128-row keyset discovery pages**. These
match the scale of existing completion work without sharing its delivery scope.
One worker owns a session at a time, even if a hint and a sweep race. A worker
loads at most one page, in stored sequence order. A busy session yields at page
boundaries; retrying failures release their worker between attempts.

Ingress uses `TryWrite`; full/closed hints never block a successful commit. The
worker scans pending actions at startup, after overflow, and at a one-second
backstop using injected `TimeProvider`. Retry failed effects with 1/2/4/8/16/30
second capped delay, retaining phase and identity; there is no automatic
dead-letter deletion or success promotion. Page cursors and indexes must prevent
one failing session from hiding later sessions. Do not retain an unbounded
session-ID dictionary as an overflow mechanism.

All four workers may wait on delivery while ingress continues persisting their
confirmations. Under saturation another session's effects can wait, but its
transcript cannot wait on worker capacity. When a worker is available, another
session's task/report must also make progress. Log action age, pending count,
saturation, retry phase, worker start/end and stored frontier without body text;
diagnostics must not determine success or throw into the ingestion contract.

### D-5. Route the triggering turn before considering the next delivery

Retain the logical phase order and durable acknowledgement between phases:

| Trigger | Effects, in order |
|---|---|
| Every committed transcript row | Ordered `SessionTranscript` UI publication; then applicable phases below. A publication failure is retried without undoing persistence. |
| UserPrompt / QueuedUserPrompt / TurnEnd | Capacity recovery at that stored sequence; UserPrompt refinement confirmation when applicable. |
| Eligible end/cancel/error/interrupt boundary | Channel reply -> review reply -> task settlement -> turn-end queue request -> CARD-1247 late-confirm dispatch for the returned exact source IDs. |
| AssistantText | Channel/review/task routing for that text's owning turn, including text after an early TurnEnd. |
| CompactBoundary | Recovery at the stored sequence; **manual only** requests narrow idle flush after recovery. No manual-compaction report settlement or SessionFinished. |
| Catch-up reconciliation | Refinement confirmation -> rules reconciliation -> owed task settlement, plus actions for newly committed rows. No historical blanket resend. |
| AgentStatus leaves blocked | Existing display invalidation and a narrow current-state idle-flush request. Status grants no receipt, stop, or state override. |

Add explicit sequence/turn-bound entry points to `ChannelReplyDispatcher`,
`ReviewReplyDispatcher`, and `AgentTaskReplyService`; their existing sweep/API
entry points retain their contracts. The worker carries the triggering frontier,
owning prompt and boundary identities. Extraction may include later text only
within that same prompt window/ApiCallId; never substitute a newer completed
turn. Preserve UserPrompt precedence over in-turn QueuedUserPrompt, error and
housekeeping exclusions, answered-Blocked watermark checks and follow-up text
watermarks. An early boundary with no text leaves the reply correlation owed;
the later AssistantText action supplies the retry trigger.

Queue requests are coalescible wakeups, not permissions to type. Before executing
one, finish routing actions through the session's committed frontier; if that
frontier advanced, process those routing actions first. Then call the existing
queue entry point, which rechecks current idle/blocked/rules/attempt eligibility
under its own lock. Do not type because an old action says the session was idle.
Keep turn-end versus narrow-flush intent distinct; a narrow request cannot create
a finished notification. Worker actions arriving during a flush remain pending.
Check current session generation and task attempt at the existing authoritative
gates before any input/settlement. Old actions cannot turn a new generation idle
or settle a newer attempt. Historical routing is allowed only for its still-owed,
exactly identified correlation; a stale input request is superseded without
typing rather than rebound to the current generation.

Internal callbacks must expose completed/deferred/retry outcomes rather than
letting a swallowed exception acknowledge an unfinished phase. Reuse existing
durable queue verdicts and publication/settlement claims. Commit-before-wakeup,
send-before-action-ack and concurrent sweeper races must not duplicate input.
Do not strengthen claims about the existing direct channel producer's uncertain
crash window; CARD-1247's publication ownership remains authoritative.

### D-6. Carry review authority through the new asynchronous boundary

The in-memory review map cannot be the recovery source for durable actions.
Use the queue row created by `ReviewThreadService.DispatchAsync`
(`Origin=Channel`, exact `review:{threadId}`, stored body and session) as the
authorized correlation. Add a nullable `ReviewThreadId` captured by that service
and persisted atomically with enqueue, plus `ReviewReplyConsumedAt`. A transcript
`[Review ...]` tag alone is insufficient; neither a worker nor a migration invents
the new target field by parsing historical model text.
Resolve its owning prompt with the same complete-body and turn-window guards.
Atomically append the review comment/update the thread and consume the exact
queue correlation in one transaction, using the dedicated review consume stamp
on `SessionQueuedMessage` to avoid changing channel publication ownership.
Remove map-only authority for this dispatch path. Retain API-error withholding
and TTL semantics. The migration leaves the new fields null on historical rows;
it neither fabricates consumption nor replays historical reviews. New dispatches
carry durable authority. A pre-upgrade row without the new target is legacy
unknown: exclude it from automatic recovery, retain the thread/queue evidence,
and log the affected thread/session identity for explicit re-dispatch if still
owed. Do not infer a current obligation from a thread's latest status alone.

This is a necessary dependency of durable deferred review dispatch, already
identified in CARD-0432's S3 amendment. It is not permission to redesign review
threads or channel delivery. The existing process restart already loses the map;
this plan does not claim to recover authority that was never stored. TestDesign
must pin the legacy-unknown classification and prove new durable correlations
survive the same restart.

### D-7. Shutdown owns its work; delivery evidence stays unchanged

On shutdown stop admission of new execution, stop the pump, cancel active workers
through the host token and await all owned tasks/scopes. Pending actions remain
durable. Cancellation is not a completed effect and never grants Delivered.
Preserve queue cancellation/interrupted-Sent recovery: restart checks the stored
prompt before any retry. Do not release an action/session execution owner merely
because a timed wait expired while its underlying operation still runs. If an
external operation ignores cancellation, report that shutdown is still joining;
the existing host shutdown deadline is not permission to start a second owner.
Within-process reconnect does not create another worker cohort.
This retains the documented single-server-writer deployment model of
`SessionStateStore`; it is not a distributed multi-server action executor.

One complete matching UserPrompt past the attempt floor is still the ordinary
observable delivery verdict. Preserve LF, bracketed paste and separate Enter,
body completeness, generation/time floors, Enter-only retries, blocked deferral,
truncation handling and late-confirm-before-retype. A queue insert, action ack,
screen change, Sent flag or UI publication is not a receipt. Do not widen or
disable the 30-second confirmation window to make any test pass.

This changes server work scheduling, not a session's input-wait lifecycle.
**Parking remains disabled by default; a session waiting for input has no
automatic release deadline (CARD-1083).** No worker timeout stops a session,
releases its seat, sends synthetic input, or changes the stall policy.

## Implementation slices

All paths below are repository-relative. Commit/push the red test slice before
the fix; commit each implementation slice before its checkpoint group. No
unrelated distiller, native runner, UI, fleet setting or provider changes.

| Slice | Files and concrete result | Tests |
|---|---|---|
| S1: red regression | `tests/Antiphon.Tests/Application/SessionRunnerEventPumpTests.cs`; `tests/Antiphon.Tests/TestHelpers/ScriptedSessionRunnerClient.cs`; extend `BridgeQueueHarness.cs` only for owned pump/worker lifecycle and stream barriers. Flip four S0 cases, retaining phase assertions and throwing-diagnostics case. Add a three-case natural confirmation method using current public seams so it compiles before production changes. | V-1/V-2; CP-1 is intentionally red on the original implementation. |
| S2: durable admission and worker | New `server/Domain/Entities/SessionRuntimeAction.cs`, `server/Application/Services/SessionRuntimeActionCoordinator.cs`, `server/Infrastructure/Agents/SessionRunner/SessionRuntimeActionHostedService.cs`; `server/Infrastructure/Data/AppDbContext.cs`, CLI-generated `server/Migrations/*`, `server/Program.cs`, `server/Application/Services/DataRetentionService.cs`. Implement transactional admission, bounded hints/discovery, single-session ownership, retries and cleanup. | New `tests/Antiphon.Tests/Application/SessionRuntimeActionTests.cs`: V-3/V-4/V-8/V-9/V-10. |
| S3: ingress cutover and bound effects | `server/Application/Services/AgentSessionRuntime.cs`, `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs`, `ChannelReplyDispatcher.cs`, `ReviewReplyDispatcher.cs`, `AgentTaskReplyService.cs`, `TranscriptTurnWindow.cs`; `ReviewThreadService.cs`, `server/Domain/Entities/SessionQueuedMessage.cs` and EF/CLI migration for the review stamp. Queue service changes limited to outcome/intent seams needed to retain existing semantics; no verifier algorithm changes. | New `tests/Antiphon.Tests/Application/SessionRuntimeActionRoutingTests.cs`: V-5/V-6/V-7/V-11/V-12. Adapt affected runtime fixtures to await observable action completion rather than synchronous method return. |
| S4: failure and lifecycle closure | Complete tests in the three named classes; `AgentSessionRuntimeTests.cs`/`.Persist.cs` and existing queue/manual-compaction tests only where asynchronous completion needs awaiting. Update `docs/session-runtime-invariants.md` with actual commit/effect ordering, overflow, recovery and shutdown; `docs/herdr-sessions.md` only for changed unblock scheduling. | V-1–V-12 and named R-1–R-4 below; CP-2–CP-8. Run full ordinary Review (D-8 category) before landing. |

## Verification requirements for the separate TestDesign stage

These are the acceptance design and executable method names to bind, not a claim
that tests or positive controls have run. TestDesign must add its required
`## Verification design`, inspect the referenced fixtures/bodies, map every
independent safety guard to a method-scoped PC, and finalize the one checkpoint
table below. It must reject any seam that proves only that work was enqueued.

Use `BridgeQueueHarness`, real runtime/queue/state store and isolated PostgreSQL,
`ScriptedSessionRunnerClient`, controlled clock and a recording fake transport.
No real provider, external chat, production runner, live broker or paid call.
Fresh DI scopes and DB reads observe receipts independently of the producer.
Fakes replace transport/clock only; they cannot certify real native Enter behavior
or latency under actual desktop CPU/disk load. Existing native submission policy
is preserved rather than requalified here.

| ID / method | Decisive evidence and red condition |
|---|---|
| V-1 `SessionRunnerEventPumpTests.S0_blocked_hosted_pump_probe` (4 existing arguments) | While A's effect is held, B is read and stored **before** release; with a free worker B's marked task settles. Preserve held SignalR and queue-submit cases, reconnect and throwing diagnostics. Remove the old `Received.ShouldNotContain` and false persistence oracle. Original code must fail the positive progress assertion. |
| V-2 `SessionRunnerEventPumpTests.C1259_confirming_prompt_and_other_session_progress_before_confirmation` (live/startup/subscription-ready, 3 arguments) | Ordinary observable WhenIdle message, full-inline-specialist path off, verification settings retain production 30s/7s limits. First submission immediately returns and emits A's exact complete UserPrompt **through the scripted event stream**, never directly into DB. Hold the verifier's next evidence read with an interceptor/barrier, prove A's prompt and B's ordered rows committed while the delivery task is incomplete, then release the read/advance one normal poll. Assert Delivered (not LateConfirmed), one body write, one submitting Enter, one matching prompt, one delivery attempt, no timeout/grace/recovery pull after submission. Initial/reconnect snapshot pulls are separately counted and allowed. Original pump must fail the before-release persistence assertion. |
| V-3 `SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic` | Inject batch and per-row save failure, dedup replay and safe-stub fallback. No effect for an uncommitted row; every successfully committed eligible row has its action. Throw between commit and wake; restart discovers it once. Mutating the transaction/wakeup-only assumption must fail DB assertions. |
| V-4 `SessionRuntimeActionTests.C1259_stream_pull_and_replay_preserve_order` | Interleave stream, snapshot and a second snapshot under the actual lease, with low restarted runner sequences, same UUID/different kinds, repeated ApiCallId and assistant text after stop. Assert exact UUID/kind/stored-sequence roster, one eligible boundary effect, no interleaved duplicates. A gate/remap/dedup bypass must change the roster. |
| V-5 `SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush` | Hold A's routing, ingest two completed turns and trailing text, then release. Distinct fake channel destinations and marked task IDs must receive the correct complete outputs, in trigger order; older action cannot select the newer turn. A newly queued prompt cannot overtake owed routing. |
| V-6 `SessionRuntimeActionRoutingTests.C1259_new_working_or_blocked_state_defers_old_idle_action` | After durable idle action, change to Working, Herdr blocked or rules-held before execution. Zero input/attempt charged; later eligible state permits one submission with matching UserPrompt. Mutation bypasses final current-state/queue gates separately. |
| V-7 `SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion` | Hold the real queue lock/confirmation with a Sent refinement outstanding; stream matching prompt and B. Also exercise auto/manual compaction and blocked-exit. Transcript progresses; manual recovery precedes narrow flush; no manual report/finished signal; status never confirms delivery. Removing any remaining inline queue await makes its specific case red. |
| V-8 `SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow` | Occupy all four workers; fill 128 distinct ID hints plus overflow, using small injected fixture limits for the exhaustive matrix if needed. Same-session confirming prompts and B still persist. Assert max worker count, one owner per session and bounded hints/discovery; release workers and recover every durable action without new runner events. Include a failing first page with later eligible sessions. `WriteAsync`, dropped action, duplicate owner or unbounded spawn must fail separate assertions. |
| V-9 `SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission` | Stop during queue wait, after persistence before wake, and after effect before action ack. Assert scopes are not disposed while used, no task continues after a reported clean stop, pending phase survives, and restart sees the existing complete prompt without another body/Enter. For an uncooperative fake, Stop remains incomplete until controlled release; no second worker owner. |
| V-10 `SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn` | Fault routing/save independently; prior successful phase does not repeat, failed phase is not acknowledged, retries respect due time and a later session progresses. Retention cannot delete any required turn while pending, including discovery/ingest races; after completion normal eligibility returns. |
| V-11 `SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once` | Restart with an authorized review queue row and pending action; exactly one correct thread comment/consume transaction. Model text naming another thread cannot redirect it. Crash before/after comment commit, duplicate action and historical/unresolvable migration rows neither duplicate nor falsely certify recovery. |
| V-12 `SessionRuntimeActionRoutingTests.C1259_missing_or_truncated_prompt_never_confirms` | Screen/output advance, wrong body, old prompt or identity-only prefix do not become Delivered as workers run; only complete matching receipt succeeds. Leave timeout and existing negative-verdict behavior unchanged. |

Do not let V-2's barrier block database writes or hold the transcript lease. Use
timeouts only as test failure bounds; positive barriers establish the happens-
before relation. A five-second sleep and eventual success after release are not
proof. A fake that inserts confirmation directly into PostgreSQL would bypass
the defect. TestDesign must explicitly instrument the real confirm-read seam,
prove it is reached and distinguish one body submission from extra Enter calls.

R-1 is stored sequence/dedup (`AgentSessionRuntimeTests` exact existing methods
below). R-2 is LF/paste/Enter, manual narrow flush and truncation. R-3 is CARD-1247
exact late-confirm ownership. R-4 is typed stale-exit forwarding. Preserve
existing historical failure records: the earlier S0 cases passed their old
blocking assertions, but their checkpoint failed output-ledger/receipt validation;
that is not a qualified green acceptance run.

### Platform and lanes

Read `GET /api/runner-defaults` and `GET /api/session-runners` on 2026-10-15
16:31 UTC. Revision 2 supplied a global preference with no kind overrides;
the catalogue had eligible Linux and Windows lanes and an unavailable draining
entry. These are observations, not placement pins. Re-read at dispatch; omit
`-Runner` and `-Platform`. The tests are **Integration lane, Linux-capable** and
require isolated PostgreSQL, not Windows or a native provider. No fleet location
is embedded in this plan. Do not run a whole Unit lane or assembly.

### Checkpoints

This is the proposed closed list for Code, to be bound by TestDesign before Code
dispatch. All groups are in the Integration lane. CP-1 intentionally records
assertion red against S1 (four flipped S0 cases and three V-2 arguments); a build,
fixture, zero-test or cleanup failure is not the required red. CP-2 re-runs that
exact selection green after the fix. New V-3–V-12 method floors are conservative
one execution each; TestDesign must enumerate argument expansion/rosters and
raise them as appropriate without adding a broad lane run. Reused builds are
permitted only within the same `After` group and unchanged source.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1259-red/ | ingress-red-integration | `/*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/(S0_blocked_hosted_pump_probe*)\|(C1259_confirming_prompt_and_other_session_progress_before_confirmation*)` | V-1,V-2 baseline | 7 executed; expected progress assertions fail; record intentional red, never green | 7 | 5 |
| CP-2 | S2-S4 | tests/Antiphon.Tests -> bin-c1259-green/ | ingress-green-integration | `/*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/(S0_blocked_hosted_pump_probe*)\|(C1259_confirming_prompt_and_other_session_progress_before_confirmation*)` | V-1,V-2 | all listed, 0 failed | 7 | 5 |
| CP-3 | S2-S4 | CP-2 | action-lifecycle-integration | `/*/Antiphon.Tests.Application/SessionRuntimeActionTests/C1259_*` | V-3,V-4,V-8,V-9,V-10 | all listed, 0 failed | 5 | 3 |
| CP-4 | S2-S4 | CP-2 | action-routing-integration | `/*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/C1259_*` | V-5,V-6,V-7,V-11,V-12 | all listed, 0 failed | 5 | 3 |
| CP-5 | S2-S4 | CP-2 | transcript-order-integration | `/*/Antiphon.Tests.Application/AgentSessionRuntimeTests/(C698_same_uuid_different_kinds_survive_replay_across_sessions_and_generations*)\|(C698_1025_uuid_keys_are_bounded_and_replays_preserve_every_pair_and_sequence*)\|(Error_turn_end_is_acted_on_once_replay_dedups*)` | R-1 | all listed, 0 failed | 3 | 2 |
| CP-6 | S2-S4 | CP-2 | delivery-contract-integration | `/*/Antiphon.Tests.Application/SessionMessageQueueDeliveryVerificationTests/(C514_Multiline_release_keeps_LF_paste_and_separate_Enter*)\|(Manual_boundary_flushes_a_message_stranded_before_the_compaction*)\|(Manual_boundary_with_an_empty_queue_publishes_no_finished_event*)\|(A_clipped_prefix_parks_as_truncated_not_sent*)\|(Late_confirm_does_not_promote_a_truncated_body_to_sent*)` | R-2 | all listed, 0 failed | 5 | 2 |
| CP-7 | S2-S4 | CP-2 | late-confirm-integration | `/*/Antiphon.Tests.Application/ChannelReplyLateConfirmDispatchTests/(Grace_dispatch_keeps_its_confirmed_turn_when_a_newer_turn_completes*)\|(Grace_and_runtime_redispatch_share_one_atomic_publication_owner*)\|(Caller_cancellation_does_not_release_a_still_publishing_direct_reply*)` | R-3 | all listed, 0 failed | 8 | 2 |
| CP-8 | S2-S4 | CP-2 | stale-exit-integration | `/*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition` | R-4 | all listed, 0 failed | 1 | 1 |

Estimated ordinary execution floor: **23 minutes**, including two isolated
builds and intentional red; final green floor 18 minutes. Estimates exclude slot
wait, authoring and the separately designed post-land mutation work. Counts are
execution floors, not minutes. No native/whole-suite repetition is requested.
All new tests must declare the Integration category and use the existing schema
isolation/process limiter conventions; do not loosen timeouts to hide failures.

Run the committed manifest with `Antiphon.Checkpoints run --plan <this-path>
--after S1` then `--after S2-S4`, with exact committed source SHA. Await each run
to a terminal exit (75 means still running); do not leave a child at settlement.
Checkpoint rows acquire host build slots themselves. If the tool needs an
isolated bootstrap build, use `scripts/build-slot.ps1` and report that named
prerequisite rather than holding a second lease around the checkpoint process.
Keep generated receipts/TRX/logs ignored, preserve unedited CHECKPOINT/provenance
lines, clean owned alternate outputs, and run `scripts/check-evidence-diff.ps1`
over Code's recorded base..pushed HEAD. Full ordinary Review must inspect ordering,
locking, recovery and every listed receipt; positive controls execute later in
the separate SourceLanding Mutation stage, not in this Plan task.

## Completion and scope limits

Build is ready only after TestDesign has bound the natural-loop fixture,
migration/upgrade review treatment, phase-result semantics, exact case rosters,
and independent guard/PC inventory. These are implementation verification work,
not a request to enable parking, change a fleet budget, or weaken delivery proof.
If that stage finds this durable boundary cannot preserve a named effect, return
to Plan with the precise failed contract rather than replacing it with arbitrary
concurrent latest-turn dispatch.

Acceptance is the hosted pump/queue proof: A's confirming prompt and B's ordered
transcript commit while A's delivery remains pending; the same delivery then
confirms with exactly one submission and no timeout recovery. Saturation,
restart, exception and shutdown cases must preserve that custody and the existing
negative delivery outcomes. No claim is made about eliminating all native-history
latency, certifying every historical Family delay, or making external publication
exactly-once across an uncertain transport crash.

Land/deploy is a later authorized stage; this task does not restart a shared
server. A later live observation may compare producer/runner/server-persist phase
timings without sending real chat messages, using the same measurement caveats
as Investigate. It is not a substitute for the deterministic acceptance proof.
