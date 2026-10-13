# CARD-1205: recover the TurnEnd snapshot-to-subscription gap

Date: 2026-10-12. Stage: Plan; next stage: TestDesign. Inspected checkout:
`d8188da496b2369e9ee688f8977c4888510fbb9f`. This dispatch changes this document
only. All tests, fixes, checkpoint counts and positive controls below are proposed;
none has been executed by Plan. TestDesign is a separate, required stage.

Close the local runner's snapshot-to-live-stream coverage gap, then independently
recheck Working sessions on a bounded schedule. Recover delivery through committed
transcript evidence and the existing queue. A timeout must never manufacture a
TurnEnd, certify idle, type through Working, or terminate a session.

The first Code slice is a deterministic reproduction. Its expected red must show
the missing boundary in runner custody, absent from PostgreSQL despite a healthy
stream, and recovered by the same pull primitive used by delivery grace. Do not
claim an incident-equivalent reproduction until that assertion fails as designed.

## Evidence and ground truth

Read CARD-1205 on the Antiphon board, id
`29eaf667-5a31-42c1-9248-c4d1cf758795`, board
`8988ca03-7414-47ad-b0b6-51556c701703`. Its title still says “about an hour” and
“[regression]”. Recommend the title **“Missed TurnEnd after restart leaves caller
Working and delays five completion notes by 4h19m–5h39m [latent race]”**. The caller
should revise the board card; this task does not edit generated `docs/cards/` files.

The two investigation artifacts are not assumed to be on master. Their immutable
provenance is:

- Source/DB: `origin/feat/card-task-ef0bb4ed`, commit
  `e5a159ce85b2f741e439408300108ddf84532806`,
  `docs/investigations/2026-10-12-card-1205-task-completion-notes-not-delivered-to-the-orchestrator-after-an-apphost-restart-five-tasks-silent-for-about-an-hour.md`.
- Desktop: `origin/feat/card-task-d5091e82`, commit
  `9a283479f759f0f96d4f448d690c31b41b9cf79e`,
  `docs/investigations/2026-10-12-card-1205-desktop-evidence.md`.

Both were fetched and read with `git show origin/<branch>:<path>`. The artifacts
establish the withholding mechanism and recovery pull. Original publication and
subscription timestamps are not retained; the gap remains the strongest incident
attribution, not a proven first-bad change. Their investigation-stage recommendations
are superseded only by this brief's instruction to put controlled reproduction
first, not by new forensic evidence.

| Card assumption / question | What the evidence or inspected code actually does | Design consequence |
|---|---|---|
| Completion obligations were lost on restart. | All five notification and queue rows were promptly committed and eventually confirmed by complete caller UserPrompts. Completion-to-receipt delays were 4h19m26s–5h38m42s. | Preserve durable outbox/queue semantics; repair transcript coverage, not notification creation. |
| The caller was busy for the whole wait. | Its 11:19:24.074Z TurnEnd was stored at 17:07:38.350141Z. SQL and cache reconstructed Working while it was absent. | A persisted Working verdict can be stale even with a healthy runner and connection. |
| Runner capture was delayed until 17:07. | Runner sequences 15934–15936 precede compact sequence 15937, received by the server at 12:14:20Z. The runner retained the old tail. | The test must distinguish runner custody from server persistence. |
| Catch-up covers restart. | `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:44` awaits all session syncs before enumerating SSE at line 46. Caller catch-up was stored at 11:19:16Z; fleet catch-up ended at 11:19:49.825Z. | Publish between snapshot and subscription; test quiet and active stream recovery. |
| A newly opened SSE stream replays old events. | `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:4283` subscribes a new channel; `Publish` at line 4319 sends only to current subscribers. | A fake that buffers events before subscription would conceal the defect. Use the real hub or a faithful non-replaying substitute. |
| Response headers / `: connected` prove subscription. | `src/Antiphon.SessionRunner/Program.cs:395` flushes `: connected` before `runtime.Subscribe` at line 401. A subsequent `: keepalive` is written inside the subscribed loop. | Never trigger the repair pull from headers, iterator construction or `: connected`. |
| The stream watchdog bounds missing transcript evidence. | `SessionRunnerHttpClient.cs:865–924` resets its 90-second idle timer on every line, including keepalives, but emits only parsed events. | Connection health and transcript coverage need separate clocks. |
| There is no completion backstop. | `server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs:36,69,149` has boot/15-minute recovery and flush workers; a flush still obeys Working. | More queue retries alone cannot resolve this incident. |
| Existing state warmup pulls the runner. | `server/Infrastructure/Supervision/SessionStateWarmupService.cs` warms/pins committed state and explicitly never polls transcripts. | Do not present cache warming as evidence recovery. |
| Runner sequence is an enduring identity. | `AgentSessionRuntime.cs:988–1062` deduplicates `(Uuid, Kind)`, uses sequence only for null UUID, and rebases stored sequences across tailer lifetimes. AssistantText and TurnEnd can share a UUID. | Reject sequence-only dedupe and UUID-only dedupe. Preserve existing serialization and producer timestamps. |
| Any sync completion certifies freshness. | `AgentSessionRuntime.SyncTranscriptAsync` swallows pull failures; persistence can return `NeedsReload` and retain failure evidence. `CatchUpForReceiptAsync` already distinguishes several unknown results. | The new watchdog needs a checked result, not a void best-effort call or a successful Task. |
| Disabling cached reads removes all session serialization. | `server/Program.cs:677–684` registers SessionStateStore unconditionally; `PersistTranscriptAsync` takes its write lease even when `Enabled=false`. | Reuse that lease in both cache modes. An absent store in a custom service graph is Unknown for the new recovery operation, not permission to append without a gate. |
| Backfilling an old end is always safe while newer activity exists. | The SQL and cache use stored end sequence plus producer timestamps; inserting a previously missing old end above an already stored new prompt is not proof that the new turn ended. | Before watchdog persistence/flush, inspect the authoritative runner tail and the concurrent durable frontier. Never let an old boundary certify idle for newer work. |
| The 17:07 recovery was another restart. | Desktop logs identify a settings delivery's 30-second verification timeout and grace pull. `CatchUpTranscriptAsync` fetches/persists without re-entering queue callbacks. | Use that persistence path for the baseline recovery control. Keep watchdog pulls outside queue locks. |
| A recent activated change caused the race. | The investigations find no relevant delivery/pump change in the activation range; catch-up-before-stream predates it. | Treat this as a latent race. Do not label an August provenance commit “first bad”; no bisect is justified yet. |
| All callers were observed affected. | The bounded lag query found three delayed rows, all on this caller. Local sessions share source exposure; phone-home has a different pump. | Scope the gap repair to direct HTTP SSE. Do not claim a fleet-wide incident census or change phone-home recovery. |

## Decisions

These are explicit proposed implementation defaults and operator recommendations.
They permit a concrete plan now; TestDesign audits them before Code. If the caller
changes a default, revise the corresponding slice and checkpoint before dispatch.

- **D-1 — Reproduction is a stop gate, not a regression claim.** Retain a positive
  desired-behavior test that is red on the unmodified production path. Commit its
  test-only slice and expected-red report; do not land that slice alone. If the
  missing-row signature cannot be produced, stop the fix sequence and return
  `next: investigate`, measuring snapshot capture, subscription registration,
  publication, storage and grace recovery at explicit barriers.
- **D-2 — Prefer a server-only post-subscription catch-up.** Keep the initial
  catch-up for downtime ordering. The HTTP iterator emits one internal readiness
  marker per connection, immediately before its first parsed runner event, or
  on its first exact `: keepalive` comment. Both originate after subscription in
  the current runner. The pump handles this marker by awaiting a second catch-up
  before dispatching any live event from that connection. If that list times out,
  the already parsed event gets one bounded preservation offer before the iterator
  is disposed and the existing reconnect follows. Host cancellation also offers
  it once, then stops waiting on a pending consumer. A
  throwing list does not reconnect; the common preservation exit offers the event.
  Snapshot coverage is not claimed on any of those three. If a real event proved
  readiness, retain and emit that very event after the marker. This closes the
  gap without upgrading the runner protocol. Reject moving the first catch-up
  below a lazily constructed iterator: construction opens no subscription.
  Reject response-header readiness: the current endpoint flushes too early.
  Subscribe-before-catch-up with an explicit acknowledged subscription is a sound
  alternative, but requires more interface/runner work than this bounded repair.
- **D-3 — Preserve identity, ordering and failure semantics.** Reuse the runtime's
  `(Uuid, Kind)` upsert and session commit gate. Do not introduce a sequence
  high-water mark that discards low numbers from a resumed tailer. Keep producer
  timestamps; use injected `TimeProvider.GetTimestamp/GetElapsedTime` for new
  deadlines, due times and durations, and UTC only for human log timestamps.
  Recovery failure must not clear Working or claim complete coverage.
- **D-4 — Independent stale-Working safety net, enabled by default.** Proposed
  `StaleWorkingTranscript` settings: `Enabled=true`, `IntervalSeconds=120`,
  `PollSeconds=15`, `ProbeTimeoutSeconds=10`, `MaxConcurrentProbes=4`,
  `PageSize=128`. Interval is the maximum desired age of a successful authoritative
  check, not a maximum allowed turn length. Unknown freshness after server startup
  or a generation change is immediately due. Keepalives, cache reads, new queue
  attempts and unrelated transcript events never reset this check clock. Reject
  using `Timestamp`, `LastSeenAt`, or wall-clock subtraction as the elapsed timer.
- **D-5 — Scope and load bound.** Cover every locally owned Running session
  whose durable verdict is Working, even without a queued completion. Use keyset
  pages over DB ownership, not only warm cache entries or a capped runner list.
  Four owned probe workers, one probe per session generation, and a fair due queue
  prevent an unavailable session from monopolizing recovery. Do not add remote
  phone-home polling under this card. Re-pull the existing full transcript endpoint;
  “tail” describes the evidence needed, not a new endpoint or a constant-size payload
  claim. Reject polling every transcript on each queue-flush attempt.
- **D-6 — Log every firing; do not add a new attention schema.** Information logs
  report a due probe and its result; Warning logs report unknown, timeout,
  overdue/deferred capacity and discovery failure. Include session/generation,
  server epoch, reason, elapsed age, pre/post stored sequence, added count and
  duration, without transcript bodies. Recommend structured logs first; a persistent
  attention projection is a separate operator choice and follow-up scope. Disabling
  the feature is an explicit rollback, with a startup Warning stating that the
  periodic recovery bound is disabled. Zero/negative intervals are invalid, not
  a silent disable convention.
- **D-7 — Checked recovery never guesses idle.** Require a nonempty,
  session-matching, generation-matching runner snapshot that covers the known
  durable frontier. Missing generation evidence is Unknown for the new watchdog;
  it does not change ordinary legacy live ingestion. A runner snapshot still
  showing newer activity is a successful Working observation, not permission to
  append its old end out of order. Unknown results retain the old verdict and due
  debt. See the guarded recovery algorithm below. Reject “age exceeded, set idle”
  and automatic interrupt/stop/nudge policies.
- **D-8 — Portable verification lane.** Read `GET /api/runner-defaults` and
  `GET /api/session-runners` on 2026-10-12 at approximately 17:42Z. Revision 2
  supplied a global default with no kind overrides; its placement was eligible,
  and both Linux and Windows eligible capacity were advertised. All planned
  checkpoints use **Any / isolated PostgreSQL / controlled HTTP and provider**.
  Re-read placement immediately before dispatch. Omit `-Runner` and `-Platform`;
  use `-Platform Any` only to clear an inherited pin. No fleet location is embedded
  in this plan. An SSE defect on a desktop transport does not itself require a
  Windows test host.
- **D-9 — Restart proof uses fresh services and durable identities.** Dispose and
  reconstruct the provider, runtime/state cache, pump, queue, notifier, watchdog
  and in-memory wakeup queues while retaining the same isolated PostgreSQL schema
  and simulated runner/provider. This proves server service restart survival,
  not AppHost deployment or native PTY behavior. Retain the actual TaskId,
  SourceEventId, notification Id, queue Id, destination, wire digest and receipt
  sequence across the cuts.
- **D-10 — Keep release policy unchanged.** This card adds evidence recovery,
  not an input-wait session or a release mechanism. A newly recovered TurnEnd
  permits the ordinary idle queue gate; it does not release a seat. With parking
  disabled there is **no automatic release deadline** for a session waiting for
  input (CARD-1083). Working is never killed or parked by this watchdog. Fixture
  tasks, streams and subscriptions must be canceled, awaited and disposed at
  teardown even on the deliberately red reproduction.

## Implementation design

### Two catch-ups with a proven subscription between them

Add an additive, internal-only readiness flag/factory to
`server/Application/Dtos/SessionRunnerDtos.cs`'s `SessionRunnerEvent`. It is not a
wire event, session record, transcript row or SignalR publication. The HTTP client
sets it once within each iterator invocation, on exact keepalive or a successfully
parsed supported event. `: connected`, arbitrary comments, empty frames and
iterator construction are not proof. The stream-idle watchdog reconnects only after
genuine silence. Its deadline resets on every received line, including unrecognized
comments and unsupported frames, so that traffic keeps the connection alive. It does
not emit a readiness marker and it is not a readiness deadline. A connection that never
emits a qualifying frame is not reconnected merely because readiness is absent. No
coverage claim is made for a stream that never proves subscription. Cancellation disposes
response, reader and stream. Reset readiness on every reconnect.

In `SessionRunnerEventPump`, handle readiness before session-specific phase/event
dispatch. Await catch-up, then resume the same enumerator. On a list timeout,
offer the already parsed event once before the iterator is disposed, then use the
existing reconnect path. On host cancellation, offer it once and then stop waiting
on a pending consumer. On a throwing list, leave the exception inside catch-up, do not reconnect,
and offer the event from the common preservation exit. Do not wait for another stream
read and do not extend the list deadline. Completed held offers
and following live events preserve order within that connection. An abandoned
callback may settle later; no ordering across that uncertainty or reconnect is
promised. Overlapping transcript
rows dedupe using the existing runtime. Output/exit/fault/bind/adoption/status events
must also survive being the first event. Do not synthesize readiness in the routing
facade or phone-home pump. Use injected time for new timing and pump phases. No
new polling loop runs on that serial event-consumer task.

Repair round 2 binds the responsive held-event rows at
`/*/*/SessionRunnerStreamReadinessTests/C1205_Held_event_survives_failed_second_catchup*`
(success, timeout, throw, cancellation). Repair round 3 adds T48-T50 in
`SessionRunnerHeldEventDeliveryTests`. Repair 4 adds the 48-row T51 exit matrix
and two T52 iterator-fault controls in `SessionRunnerHeldEventExitMatrixTests`;
Repair 5 adds T53's three exception/late-fault rows. Repair 6 adds T54-T56's twenty
callback-fault/completed/deadline-race rows. Repair 7 adds T57-T60's 104 literal
classification/Review-control rows; CP-2 is now exactly 211 executions.
Its committed groups and exact filters are in
[the current repair checkpoint manifest](2026-10-12-card-1205-s2-repair7-checkpoints.md).

Every marker-held payload uses one finally-owned offer through the normal dispatcher
with a linked deadline/host
signal. `SessionRunner:HeldEventDeliveryTimeoutSeconds` defaults to 3 and must be
positive; its timer uses the injected TimeProvider. An already-stopping host gets
one initial callback offer before the host link is applied, preserving responsive
non-replayable output. A consumer still pending at cancellation/deadline is explicitly
warned once with an abandonment count; its attempt is abandoned, with no duplicate
retry of an uncertain output publication. An in-flight catch-up exception retains its original type, message and stack even
when preservation or its unwind fails. Secondary failures are logged without
replacing that exception. With successful catch-up, callback faults (including
unrelated OperationCanceledException under host cancellation) retain their original
object, type and stack via ExceptionDispatchInfo. The helper distinguishes completed,
callback-faulted and abandoned; only abandonment logs an abandonment warning and
uses the caller's existing deadline-only reconnect timeout or host shutdown signal.
If a callback is settled when the bounded wait observes cancellation, its outcome
wins; if still pending, abandonment wins and a later fault is only observed/logged.
Only a callback still pending when the bounded wait observes cancellation counts as
abandonment. A settled delivery-token OCE retains its original object and stack.
One classification table selects the surfaced exception and warning for the handler
and late observer. Catch-up X retains precedence and logs settled callback E.
Independent/tokenless OCE is a warning fault. A settled delivery/host-token OCE
under host cancellation reaches the normal pump shutdown catch; the same owned
cancellation arriving late during shutdown is observed without a fault warning.
Other late exceptions are warned with their original object, without changing the
already-selected timeout/catch-up result.
Its service scope remains owned until
the callback task settles, and late faults are observed. The iterator still disposes
and shutdown/reconnect proceeds. Durable transcript events can be recovered by the
existing next catch-up; no output replay queue exists. This bounds the asynchronous
wait, not arbitrary synchronous callback code or later ordinary live callbacks.
The successful-catch-up held callback has the same bound. The pump suppresses the
iterator's following copy after a completed offer; abandonment unwinds it without
replaying an uncertain attempt. Cancellation during any catch-up exit reaches this
same finally, including the swallowed list-error handler and final pull. The C2
list wait also uses the existing ListTimeoutSeconds with monotonic injected time,
independently of a non-cooperative request. Its retained scope/late task remain
observed until completion; the bound does not force request termination.

Coverage argument: let S be subscription registration, P the first qualifying
frame, and C2 the second snapshot. The producer appends a transcript row to its
snapshot before publication. A row published before S is available to C2; a row
published at/after S is in the existing subscriber stream and may also be in C2.
P proves S already occurred, and the pump completes C2 before consuming the held
live event. Overlap is idempotent. This argument is conditional on successful
snapshot/persistence and a surviving stream. When that list times out, the held
live event receives the bounded offer before reconnect, and snapshot coverage is not
claimed. Host cancellation offers it and then stops waiting on a pending consumer.
A throwing list does not
reconnect; the common exit offers the event and snapshot coverage is not
claimed. Timeout failures retain normal reconnect recovery and independent watchdog
debt. Logs must distinguish “pass attempted”
from “all transcripts verified”; the existing void sync cannot certify the latter.

Quiet-stream recovery normally starts on the first runner keepalive (default 15
seconds), not immediately on response headers. The setting can differ; the safety
net has its own bound. Do not advertise a fixed 15-second end-to-end delivery SLA.
The existing unbounded runner subscription and serial downstream callbacks remain;
CARD-0432's callback obstruction is not redesigned here.

### Checked Working recovery

Add a narrow result-returning runtime entry point, provisionally
`ProbeWorkingTranscriptAsync`, and a small `TranscriptRecoveryResult` DTO. Follow
the existing `PullAndPersistAsync` / `CatchUpForReceiptAsync` distinction between
failed pull, uncertain persistence and known committed state. Do not change the
receipt cache's semantics or make the void `SyncTranscriptAsync` a freshness proof.

1. Read the current Running/local ownership, accepted generation, and durable
   transcript frontier under the existing session serialization discipline. Capture
   a revision/frontier stamp, release the gate, and fetch the runner snapshot with
   the probe's cancellation budget. There is no queue lock held during this pull.
2. Validate session and accepted generation; an empty/truncated snapshot that does
   not cover the durable current-turn activity frontier is Unknown. Server-only
   queue annotations are not runner records and must not be required in its snapshot.
   Compare UUID plus kind where available; a raw sequence maximum is not an identity
   check. A legacy null-UUID activity frontier needs its existing sequence/record
   identity checked conservatively. Keep the whole committed revision as the
   concurrent-change stamp, separate from this runner-coverage check.
3. Classify the complete runner snapshot in its own source order using the existing
   end/activity semantics. If the authoritative tail is still Working, reacquire
   the gate and validate the captured stamp, then report CheckedWorking without
   backfilling an old missing end above newer stored activity. This deliberate
   deferral protects the current turn; complete historical backfill is not the
   watchdog's purpose. Do not change SQL/cache classification rules in this card.
4. For an idle runner tail, reacquire the session gate and revalidate generation
   and durable frontier before using the existing persistence core. A concurrent
   commit or ownership change makes this attempt Unknown and due again. Perform
   the check and persistence under the same gate; release it before callbacks.
   Cache-disabled mode retains the registered store's write gate and uses the
   durable SQL reader for its verdict, not a “missing cache means idle” fallback.
   If the store is absent, return Unknown; do not introduce a second runtime-only
   lock that fails to serialize with existing ingestion.
5. Only a complete successful persist plus a fresh committed idle verdict yields
   RecoveredIdle. `NeedsReload`, retained persist failures, failed state reads,
   cancellation and mismatched identity are Unknown. Report actual committed
   rows even if a partial failure occurred, but do not stamp freshness or schedule
   a new delivery on that result. Never use a speculative in-memory end row.
6. On RecoveredIdle, enqueue the session into the existing
   `CompletionNoteFlushQueue`; its worker rechecks the current ordinary idle gate.
   The probe does not type, settle a task, call SendNow, or invoke a queue callback
   while holding the commit gate. A recovered session with no pending note is a
   harmless flush. The stream catch-up continues to use Sync's existing settlement
   and boundary actions; the watchdog is an evidence/idle-flush backstop.

TestDesign must pin the exact frontier revalidation seam and cache-disabled gate
before Code. In particular, it must reject a proposed implementation that appends
an old end while ignoring a newer prompt, or merely checks revision after the
unsafe append. If that cannot be implemented within the existing commit gate,
split S3 before widening production scope; do not drop the guard.

### Bounded scheduling and observability

Create a concrete `StaleWorkingTranscriptRecovery` service with process-local
state keyed by `(SessionId, AcceptedGeneration)` and a hosted driver independent
of SSE consumption. Store monotonic successful-check and attempt timestamps
separately. Successful CheckedWorking resets evidence age; receipt of unrelated
stream data does not. Unknown keeps the original debt and retries no faster than
the poll interval. Remove terminal/foreign ownership entries after a fresh DB
read; uncertainty never counts as removal. Reconstruct all state on boot from
durable local Running sessions; do not persist monotonic timestamps across epochs.

Discovery uses one ordered page of at most PageSize per poll, with a retained
keyset cursor that wraps. Use the existing local-routing ownership rules, not a
hard-coded runner name. Due entries go through a fair, deduplicated queue with
at most MaxConcurrentProbes active; the discovery/logging loop never awaits one
session's transcript or queue delivery. Cap tracked generations and pending unique
work at 1024 entries. At that cap, retain discovery progress for a fair retry and
emit an explicit overflow/deferred Warning, rather than dropping coverage silently.
Reconcile tracked ownership in bounded pages too, so terminal entries eventually
leave the directory without an unbounded cleanup scan. The hosted driver also
honors `SessionRunner:Enabled=false`; a disabled transport cannot be certified by
this watchdog and receives the same explicit disabled-bound startup log.

Validate positive settings at startup: interval 30–3600 seconds, poll 1–30 and
no greater than interval, timeout 1–30 seconds, concurrency 1–4, page size 1–128.
Use relative TimeProvider timers and monotonic elapsed comparisons. The 10-second
probe budget must include admitted HTTP retries/body reads; an inner 100/120-second
client/resilience timeout does not override the caller token. Do not add retries
around SaveChanges. Source: `docs/resilience.md`.

For an already tracked session with a free worker, the nominal allowance is
I + P + D + B (145 seconds plus pass/commit delay with defaults). Here I is
IntervalSeconds, P is PollSeconds, D bounds the duration of one complete scheduler
pass, and B is ProbeTimeoutSeconds plus a bounded extra commit/join delay C.
Workers are replenished only once per pass, followed by the host's P-second delay.
For stable admitted N <= 1024 sessions with no generation churn, overflow or
non-cooperative owned operations, let K = ceil(N / MaxConcurrentProbes).
The conservative backlog completion allowance after its first admission is
(K-1)*(B+P+D)+B, not K*ProbeTimeoutSeconds. With defaults and D=C=0 this is
(K-1)*25+10 seconds. Discovery of L local Running candidates (including idle
candidates) adds M*(P+D), where M=ceil(L/PageSize) from an empty startup cursor,
or ceil(L/PageSize)+1 for a full arbitrary-cursor cycle. A conservative combined
allowance is I + M*(P+D) + (K-1)*(B+P+D) + B; cold startup need not wait I.
These formulas assume bounded successful scans and cooperative probes with a
bounded commit delay. Without finite D/C, or above directory capacity without a
free slot, there is no finite completion bound: retain and log visible debt.
T62's real hosted fake clock checks the poll-refill term using the actual defaults;
it does not certify arbitrary I/O latency or complete recipient delivery.

Once admitted, age crossing the interval must be
logged within one poll even when its worker is occupied. Discovery failure,
overflow and an overdue active operation are separately logged by that independent
loop. A non-cooperative DB operation can exceed its cancellation budget: retain
ownership, log it as overdue, and join it before disposal; never abandon it and
launch a second write for that generation. Test this condition explicitly.

Thus the guarantee is a bounded **check or visible recovery debt**, not forced
idle during a runner/DB outage. A truly Working or unreachable session can remain
Working indefinitely, with recurring bounded evidence attempts or visible debt.

## Implementation slices and collision ownership

Run these sequentially on the same evolving task branch. Each estimate includes
authoring and its ordinary checkpoint rows and is a separate 30–60 minute Code
dispatch. The canonical table below partitions the closed list by `After`; give
each dispatch only its named rows. Do not run S1's baseline-red checkpoint against
the final fixed source or count it as a remaining final failure.

| Slice / estimate | Owned files | Work and stopping point | Closed checkpoint rows |
|---|---|---|---|
| S1 — controlled reproduction / 45 min | New `tests/Antiphon.Tests/TestHelpers/TurnEndGapHarness.cs`; new `tests/Antiphon.Tests/Application/TurnEndGapReproductionTests.cs` | Real runtime/SQL/cache and HTTP iterator/pump; controlled snapshot and non-replaying hub; two barrier-driven tests. No production edit. Commit/push before the deliberate baseline run. Preserve the exact failure and grace-recovery witnesses. | CP-1; expected baseline red, never land alone. |
| S2 — close the gap / 50 min | `server/Application/Dtos/SessionRunnerDtos.cs`; `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`; `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs`; new `tests/Antiphon.Tests/Application/SessionRunnerStreamReadinessTests.cs`; S1's helper only as needed | One readiness marker and one post-subscription catch-up per connection; preserve first event, identity and cancellation. Inject TimeProvider where new timers/phases need it. The unchanged desired-behavior S1 test becomes green. | CP-2. |
| S3 — checked recovery / 60 min | `server/Application/Services/AgentSessionRuntime.cs`; new `server/Application/Dtos/TranscriptRecoveryResult.cs`; new `tests/Antiphon.Tests/Application/WorkingTranscriptRecoveryTests.cs`; S1's helper | Add the checked recovery operation with generation/frontier validation, conservative busy-tail handling, cache-on/off behavior and fail-closed result. Reuse existing persistence, not a second ingestion pipeline. | CP-3. |
| S4 — independent safety net / 50 min | New `server/Application/Services/StaleWorkingTranscriptRecovery.cs`; new `server/Application/Settings/StaleWorkingTranscriptSettings.cs`; new `server/Infrastructure/Supervision/StaleWorkingTranscriptHostedService.cs`; `server/Program.cs`; `server/appsettings.json`; new `tests/Antiphon.Tests/Application/StaleWorkingTranscriptWatchdogTests.cs`; `docs/session-runtime-invariants.md` | Monotonic scheduler, fair bounded discovery/work, validated settings, DI registration, boot reconstruction, logs and idle wakeups. Document interval/debt and no-release contract. | CP-4. |
| S5 — restart and recipient witness / 60 min | New `tests/Antiphon.Tests/TestHelpers/TurnEndGapDeliveryHarness.cs`; new `tests/Antiphon.Tests/Application/TurnEndGapRestartDeliveryTests.cs`; S1 helper; this plan's executed-name/count notes if TestDesign needs corrections | Compose C544World/Bridge recipes, the real completion producer, queue/notifier and new pump/watchdog across fresh providers. Five distinct task obligations reach complete submitted prompts once; negative busy/uncertain cases stay held. | CP-5, CP-6. |

The collision area is the **session/transcript delivery path**, especially
AgentSessionRuntime, SessionRunnerEventPump/HttpClient, SessionStateStore's gate,
SessionMessageQueueService and shared delivery fixtures. No edit to
SessionStateStore or SessionMessageQueueService is pre-authorized by the file list;
if necessary, name the guard and amend S3/S4 ownership before proceeding. Prefer
card-specific fixtures over changing ScriptedSessionRunnerClient's global behavior.

At the scoped read, Code tasks for CARD-1032, CARD-1033, CARD-1192 and CARD-1199 were
in flight, plus TestDesign for CARD-0987. That listing did not supply useful path
scopes, so it is not proof of no collision. The caller must inspect current task
scopes/effective pipeline limits before each Code dispatch, and defer any overlap
with this delivery path. Do not parallelize these five slices or edit shared
runtime files while another Code task owns them.

## Verification design

This is proposed verification for the separate TestDesign stage. It is not a
waiver of that audit and not a green report. Proposed methods are exact names;
TestDesign must bind assertions, compiling mutations, fixture cuts and actual
argument-expanded roster counts before Code. All new tests use explicit barriers
and fake monotonic time, with no sleeps to establish an ordering or an absence.
Real-time cancellation is allowed only as an outer test-hang guard, never the
success oracle. Independently move UTC and monotonic time in clock-jump tests.

### Deterministic reproduction protocol

Compose the actual SessionRunnerHttpClient and SessionRunnerEventPump with an
isolated DB/runtime and a controlled HttpMessageHandler/stream. Use the actual
public SessionRunnerEventHub for publication/subscription; a controlled in-memory
snapshot is the provider/tailer substitute. Keep timestamps and UUID/kind stable.
Do not use ScriptedSessionRunnerClient.Produce's always-present channel as the
hub: it incorrectly retains pre-subscription events for this case.

1. Store a prior end and a subsequent qualifying ToolCall; warm the state and
   assert SQL and cache both Working. The first caller snapshot excludes the new
   ToolResult/AssistantText/TurnEnd. Hold another session's catch-up or the events
   request so the test knows the caller pull completed before any subscription.
2. Append the missing tail to the fake runner snapshot, then publish it to the
   real hub while there are no subscribers. Record publication and snapshot
   custody independently. Release fleet catch-up and open the controlled stream.
3. Write `: connected` while subscription is still held. Assert this alone causes
   no second pull. Register the hub subscriber, then emit an exact keepalive.
   Consume/acknowledge that frame with a barrier; do not infer consumption from
   a delay. S2 must cause the second snapshot and durable store before a held
   subsequent live event is processed. Current production does not.
4. On unmodified production, advance fake time through hours and send several
   acknowledged keepalives; a bounded end-of-turn observation still sees no
   missing UUID/kind and Working true. Call the real CatchUpTranscriptAsync
   primitive explicitly, and require exactly one saved end and an idle verdict.
   This identifies the recovery primitive, not a test of the queue's 30-second
   grace policy itself.
5. In the desired-behavior method, capture the pre-grace outcome, perform the
   grace-pull control/teardown on both branches, then assert that pre-grace storage
   already contained the end. The final assertion must fail on the baseline.
   The companion method holds subscription throughout and asserts recovery by
   the explicit grace pull; it remains green after S2. Log barrier order and row
   identities outside checkpoint payload commits. No skipped/quarantined test,
   inverted final expectation or baseline-detection switch replaces the red.

Record the production baseline SHA. The assigned base is the inspected baseline;
before S1, compare the relevant production files to the then-current master. If
they changed, reproduce on that immutable master SHA in a separate authorized
test worktree/dispatch without rebasing this Plan branch. Do not say “red on
current master” based solely on these source reads.

### Delivery inventory and restart cuts

| Handoff | Producer / durable identity | Recovery and required recipient evidence |
|---|---|---|
| Turn tail -> runner custody | Controlled tail append -> real hub publication; SessionId, accepted generation, source sequence, UUID plus kind | Snapshot retains the end independently of the subscription; the pre-subscription event has no replay. |
| Runner -> PostgreSQL/cache | Actual HTTP iterator and pump -> runtime persistence; stored row identity and rebased sequence | Post-readiness pull or watchdog persists the end. Fresh SQL and cache agree; overlap inserts it once. |
| Settlement -> completion obligation | Real AgentTaskReplyService settlement via C544World; TaskId -> SourceEventId -> TaskCompletion notification | Five genuine immutable obligations, not directly seeded notification rows. Destination and snapshot survive provider reconstruction. |
| Obligation -> pending queue | Real AgentTaskLandNotificationService -> SourceLandNotificationId/QueueMessageId | Busy caller has five Pending, zero-attempt rows. Rebuild services without deleting the rows or creating replacement identities. |
| Idle evidence -> delivery | Checked transcript recovery / Sync -> normal queue worker and idle check | No SendNow or manual “mark idle” fallback. Observe each actual submitted body in the fake provider callback. |
| Submission -> confirmation | Actual submitted bytes -> one complete destination UserPrompt above the attempt floor -> real notifier | ConfirmedAt and ConfirmingPromptSequence must come from the notifier. Sent, enqueue, counter changes and head-only prompts are insufficient. |
| Commit -> restart | Separate cuts after queue admission, transcript commit before wakeup, and receipt creation before confirmation save | Retain DB and runner; discard all process-local services/channels. Boot recovery yields the same rows and receipts with no retyping. |

Use C544World's configurable service graph and restart support and Bridge's fake
adapter recipes; do not change C544DeliveryRig's shared private configuration just
to add another runner. For S5, OnSubmitted appends the **actual** submitted bytes
to the controlled runner snapshot/stream as UserPrompt plus an ordinary end, so
the production ingestion path stores the receipt. It must not directly insert an
expected prompt into the DB. Keep test-time provider timers advancing through
explicit barriers. Existing C544World uses a wall-offset clock, so the card-specific
composition must override it for the new monotonic-clock tests.

In the five-note witness, give tasks distinct roots so batching cannot satisfy
five obligations with an unexamined single prompt. Disable distillation in the
fixture. Match full wire content/digest and destination, then reconcile twice and
assert exactly five successful submissions and unchanged durable keys. Recreate
the lost-wakeup case with a fresh CompletionNoteFlushQueue; recovering that cut
may use the existing stranded-queue boot sweep, which must be invoked/observed
as the actual producer of the retry, not replaced with a direct fixture flush.

These are application and HTTP-framing witnesses. Fake provider/tailer input does
not prove native terminal framing, JSONL tailing, a real model or a live AppHost
restart. No test may point at the production runner or emit live messaging traffic.
Use isolated schemas; any helper that spawns Git/pwsh takes the assembly-local
ParallelLimiter<ProcessSpawnLimit>, and the tests join all owned work on teardown.

### V/R matrix: case -> mutation -> red

Counts are TUnit executions, not internal assertions. Methods below belong to the
class shown; all are new unless the row explicitly says existing.

| ID | Class.Method / executions | Case and decisive oracle | Mutation -> required red |
|---|---|---|---|
| V-1 / R-1 | `TurnEndGapReproductionTests.C1205_Gap_end_is_stored_before_any_grace_pull` / 1 | Controlled missing end, healthy keepalives, then explicit grace control. Pre-grace UUID/kind exists once, idle before any manual recovery after S2. Baseline must fail this assertion. | PC-1 removes the post-readiness catch-up: pre-grace stored-end assertion fails; grace control still succeeds. |
| V-2 | `TurnEndGapReproductionTests.C1205_Grace_pull_recovers_the_unsubscribed_tail` / 1 | Keep subscription held; row is absent until actual CatchUpTranscriptAsync. Full snapshot then saves the end exactly once and changes Working. | PC-2 suppresses the fetch/persist half of CatchUpTranscriptAsync: recovered-end assertion fails. |
| V-3 | `SessionRunnerStreamReadinessTests.C1205_Connected_comment_is_not_readiness` / 1; `C1205_Unknown_comment_is_not_readiness` / 1 | Headers/connected/other comments arrive while subscription is held. No readiness marker or second catch-up. | PC-3 treats connected or every comment as ready: zero-premature-markers assertion fails in the corresponding method. |
| V-4 | `SessionRunnerStreamReadinessTests.C1205_Keepalive_emits_readiness_once` / 1 | Exact keepalive after subscription emits one internal marker, never a transcript row; later keepalives emit none. | PC-4 emits on every keepalive: exactly-one-marker assertion fails. |
| V-5 | `SessionRunnerStreamReadinessTests.C1205_First_event_follows_readiness_without_loss` / 2 Arguments: transcript, exit | First event proves subscription; marker precedes the identical event, which is delivered once. | PC-5 consumes the first event to make the marker but never yields it: first-event identity/count assertion fails. |
| V-6 | `SessionRunnerStreamReadinessTests.C1205_Reconnect_rearms_readiness` / 1 | Drop the connection, publish during the new gap, reconnect and recover on that connection's proof. | PC-6 retains ready state across iterator instances: second-connection marker assertion fails. |
| V-7 | `SessionRunnerStreamReadinessTests.C1205_Stream_failure_and_cancellation_release_owned_resources` / 2 Arguments: read-fault, cancellation | Pending stream read is released; response/reader/subscription are disposed and tasks joined. | PC-7 removes owned stream disposal: retained-open resource oracle fails, not a hang/build failure. |
| V-8 / R-2 | `WorkingTranscriptRecoveryTests.C1205_Complete_idle_snapshot_recovers_once` / 2 Arguments: cache enabled, disabled | Matching full idle snapshot, unchanged frontier; one committed end, SQL/cache idle where enabled, RecoveredIdle result. A repeated pull inserts no duplicate. S4 owns the wakeup consumer. | PC-8 skips the recovery persist: end/idle witness fails. |
| V-9 | `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` / 6 Arguments: pull-fault, persist-fault, state-read-fault, empty, wrong-generation, missing-generation | Fault reaches its intended boundary; Unknown and no idle certification. Assert pre-commit faults append nothing; partial persistence is disclosed but never returned as success. | PC-9 treats unknown/failed persistence as success: result/idle-certification assertions fail in the bound variants. |
| V-10 | `WorkingTranscriptRecoveryTests.C1205_Newer_runner_activity_keeps_working` / 1 | Missing old end followed by a newer prompt already in DB and runner. CheckedWorking; no out-of-order end append and no wakeup. | PC-10 appends old end before checking runner-tail Working: no-new-end / still-Working assertion fails. |
| V-11 | `WorkingTranscriptRecoveryTests.C1205_Concurrent_commit_invalidates_snapshot` / 1 | Hold pull, commit newer activity, release old idle snapshot. Unknown and no unsafe end append/wakeup. | PC-11 removes frontier revalidation under the gate: protected-new-turn assertion fails. |
| V-12 | `WorkingTranscriptRecoveryTests.C1205_Overlap_preserves_uuid_kind_and_rebased_sequence` / 1 | Overlap snapshot/live rows; shared UUID across text/end; new generation's low source sequence. Unique rows, both kinds, monotonic stored sequence and unchanged producer time. | PC-12 changes dedupe to UUID-only: missing-text-or-end assertion fails. Sequence-only variant is separately bound by TestDesign if needed. |
| V-13 / R-3 | `StaleWorkingTranscriptWatchdogTests.C1205_Interval_boundary_controls_probe_and_log` / 1 | After first success, interval-minus-one-tick gives no probe; exact interval gives one due log/probe. Successful Working tail may remain Working. | PC-13 disables due admission: probe/log assertion at the boundary fails. |
| V-14 | `StaleWorkingTranscriptWatchdogTests.C1205_Wall_clock_jumps_do_not_change_due_time` / 2 Arguments: forward, backward | UTC jumps hours; monotonic time alone controls admission. | PC-14 computes age from UTC: before/at-monotonic-deadline assertion fails. |
| V-15 | `StaleWorkingTranscriptWatchdogTests.C1205_Healthy_keepalives_do_not_reset_probe_debt` / 1 | Stream keeps receiving lines while missing old end; probe still fires and recovers it. | PC-15 refreshes successful-check time on keepalive: bounded-probe assertion fails. |
| V-16 | `StaleWorkingTranscriptWatchdogTests.C1205_Fair_pages_and_single_flight_bound_work` / 1 | More than one discovery page; one blocked session, other due sessions, queue-capacity pressure, foreign/terminal rows. Cursor wraps, every eligible row is attempted or explicitly deferred, no generation has two probes, active <= configured cap. | PC-16 resets cursor each tick or drops overflow silently: later-page coverage/deferred-log assertion fails. TestDesign binds each variant separately. |
| V-17 | `StaleWorkingTranscriptWatchdogTests.C1205_Failed_probe_retains_debt_and_retries` / 1; `C1205_Overdue_operation_is_logged_and_joined` / 1 | Unknown does not stamp success or wake the queue; retry waits poll interval. A later RecoveredIdle schedules one coalesced wakeup. A non-cooperative held operation produces an overdue Warning while discovery continues and remains owned until release/teardown. | PC-17 stamps freshness on failure: retry/debt assertion fails. Removing overdue reporting is an additional guard for TestDesign to inventory. |
| V-18 / R-4 | `StaleWorkingTranscriptWatchdogTests.C1205_Hosted_restart_discovers_working_sessions_immediately` / 1 | New provider/host with empty scheduler state discovers durable Working at startup. Resolve the real production registration using a guarded test host or shared composition registration. | PC-18 removes startup discovery: due-on-first-pass assertion fails without advancing 120 seconds. |
| V-19 | `StaleWorkingTranscriptWatchdogTests.C1205_Disabled_setting_logs_the_missing_bound` / 1; `C1205_Invalid_settings_are_rejected` / 4 Arguments: negative interval, poll > interval, zero timeout, excessive concurrency | Disabled has no probe plus explicit warning; invalid positive-bound settings fail startup. | Configuration guards remain in TestDesign's PC expansion inventory; no claim of mutation coverage yet. |
| V-20 / R-5 | `TurnEndGapRestartDeliveryTests.C1205_Restart_delivers_five_gap_held_completions_once` / 2 Arguments: cache enabled, disabled | Real five-note producer chain; server service restart; gap tail; real queue and ingested complete UserPrompts; actual notifier confirmations preserve all keys. Busy phase has zero attempts. | PC-1 restores original gap, with watchdog held before due in this witness: no five confirmed receipts within the barrier schedule. |
| V-21 | `TurnEndGapRestartDeliveryTests.C1205_Watchdog_delivers_while_event_stream_remains_healthy` / 1 | After initial readiness, deliberately drop a later end publication while keeping it in the runner snapshot; keepalives continue. Only watchdog recovery starts delivery. | PC-13 disables watchdog admission: five receipts are absent after advancing the exact due schedule. |
| V-22 | `TurnEndGapRestartDeliveryTests.C1205_Restart_after_transcript_commit_recovers_lost_wakeup` / 1 | End committed; crash cut before wakeup; rebuild channels/services. Real existing stranded sweep delivers the retained queue rows once. | PC-19 disables the existing stranded retry for this destination: recipient receipt assertion fails. |
| V-23 | `TurnEndGapRestartDeliveryTests.C1205_Receipt_restart_confirms_without_retyping` / 1 | Submitted prompt committed, notification unconfirmed at fault; restart and reconcile twice. Same confirming sequence, no additional submission. | PC-20 makes reconciler ignore matching stored completion receipts: final Confirmed/sequence assertion fails. |
| V-24 | `TurnEndGapRestartDeliveryTests.C1205_Busy_tail_keeps_completion_obligations_pending` / 1 | Authoritative newer turn remains Working; queue/outbox persist across restart and elapsed time with zero attempts and no fabricated receipt. | PC-21 bypasses the queue's Working gate: zero-submissions assertion fails. |
| R-6 | Existing `SessionRunnerEventPumpTests` / 5 | Four S0 callback-obstruction characterizations plus generation-safe exit test. Preserve first-event dispatch behavior. | Ordinary compatibility scope; do not invert the known obstruction characterizations. |
| R-7 | Existing `SessionStateCommitTests` / 8; `TranscriptWorkingStateQueryTests` / 12 | Commit/publication serialization, partial failures, SQL end/activity rules, timestamp and sequence behavior. | Ordinary compatibility scope; new recovery tests cover the additional frontier guards. |
| R-8 | Existing `VerificationRoundDeliveryTests.C544_CompletionReceipt`, `C544_CompletionReceiptWholeWire`, `C544_StampIsNotReceipt` / 3 | Existing completion receipt composition, whole-wire matching and stamp-not-receipt guard. | Retain these; the new restart witness does not replace their receipt semantics. |

### Pending positive controls

All **PC-1 through PC-21 are pending**. The V/R table states the defect and expected
red. Proposed production targets: PC-1 EventPump's readiness catch-up; PC-2 runtime
CatchUpTranscriptAsync; PC-3–7 HttpClient readiness/event/disposal code; PC-8–12
runtime's checked recovery and persistence guard; PC-13–18 watchdog timing,
admission, discovery and debt bookkeeping; PC-19 existing stranded queue retry;
PC-20 AgentTaskLandNotificationService's completion receipt reconciliation;
PC-21 SessionMessageQueueService.FlushIfIdleAsync's Working gate.

Bind every PC to the **one method** named in its V row with
`/*/*/<Class>/<ExactMethod>`; arguments may expand that one method, but never use a
class/suite as a mutation filter. PC-3, PC-9 and PC-16 have variants: TestDesign must
split and price them and name the particular assertion expected from each. Also
audit the listed configuration, overdue-log and sequence-only controls for missing
guards. The list is a minimum proposed inventory, not a claim of a complete bound
pack. A compiling mutant must reach the intended assertion; zero tests, fixture
failure, timeout or build failure is not red. Restore and run the same method green.

S1's deliberate baseline-red observation is not post-land Mutation. Code runs
ordinary V/R and returns Review; Review is separate; after Land, commission the
pending controls against the actual landed SHA under the SourceLanding contract.
**Post-land Mutation was NOT run by this Plan dispatch.**

### Checkpoints

This single importable table is the closed manifest. Its `After` partitions are
the closed checkpoint tables for the five Code slices; do not duplicate competing
tables (the importer reads only the first `### Checkpoints`). Every row has one
isolated build and one exact filter. Group prefix `any-pg` names the lane: Any,
isolated PostgreSQL, controlled HTTP/provider, no desktop-only dependency. Counts
are proposed and must be checked by TestDesign against compiled names. All ordinary
rows require every listed method and zero skipped results; CP-1 is the explicitly
expected-red reproduction exception, reported as failed, never relabeled green.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1205-s1/` | any-pg-reproduction | `/*/*/TurnEndGapReproductionTests/*` | V-1, V-2, R-1 | 2 executed; exactly V-1 assertion fails on baseline; V-2 passes; 0 skipped | 2 | 6 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1205-s2/` | any-pg-subscription | `/*/*/(TurnEndGapReproductionTests*)\|(SessionRunnerStreamReadinessTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerHeldEventDeliveryTests*)\|(SessionRunnerHeldEventExitMatrixTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6, V-7, R-1, R-6, T47..T56 | all 107 executions, 0 failed/skipped | 107 | 10 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1205-s3/` | any-pg-checked-recovery | `/*/*/(WorkingTranscriptRecoveryTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | V-8, V-9, V-10, V-11, V-12, R-2, R-7 | all 31 executions, 0 failed/skipped | 31 | 8 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1205-s4/` | any-pg-watchdog | `/*/*/StaleWorkingTranscriptWatchdogTests/*` | V-13, V-14, V-15, V-16, V-17, V-18, V-19, R-3, R-4 | all 13 executions, 0 failed/skipped | 13 | 7 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1205-s5/` | any-pg-restart-delivery | `/*/*/TurnEndGapRestartDeliveryTests/*` | V-20, V-21, V-22, V-23, V-24, R-5 | all 6 executions, 0 failed/skipped | 6 | 10 |
| CP-6 | S5 | `tests/Antiphon.Tests -> bin-c1205-receipts/` | any-pg-receipt-compatibility | `/*/*/VerificationRoundDeliveryTests/(C544_CompletionReceipt*)\|(C544_StampIsNotReceipt)` | R-8 | all 3 named executions, 0 failed/skipped | 3 | 7 |

Commit and push each slice before its run. Use the checkpoint tool, one invocation
per committed `After` group, and retain its unedited CHECKPOINT lines, tested SHA,
counts, source/build provenance, slot result and all expected/unexpected failures.
The importer does not convert CP-1 into a green run: exit 1 with the exact desired
assertion is the reproduction result. A build/fixture error or another red is not
the required failure signature. CP-2 supplies the unchanged test's fixed green.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1205-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj --property:OutputPath=bin-c1205-tool/ -nodeReuse:false
dotnet tools/Antiphon.Checkpoints/bin-c1205-tool/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-12-card-1205-turnend-gap-plan.md --after S2
```

Substitute the slice's `After`; use `wait` on its run id until exit is not 75.
The first line is an explicitly disclosed tool-bootstrap build when no current
tool binary is available; it adds no verification evidence and its lease ends
before the second line. Resolve the emitted DLL path from that build if SDK output
layout differs. The second line invokes the same checkpoint `run` verb as
`dotnet run --project tools/Antiphon.Checkpoints -- run`, without an implicit build
or an outer lease held while the rows wait for their own slots. Checkpoint rows
acquire their own host slots; never run an unleased build/test driver. Include
bootstrap cost separately in the report. Wait in foreground, do not edit source
under an active run, and do not end a delegate turn with an owned run outstanding.
Slot timeout is not-run/blocked, never permission to bypass. Unexpected red is
reproduced method-scoped at the base before assigning blame. Record any unlisted
build/test and why it was necessary. Do not run a whole Antiphon.Tests assembly.

Generated TRX/JSON/logs remain ignored. Code and Review run
`scripts/check-evidence-diff.ps1` across their full base-to-pushed-SHA range.
Remove only the task-owned alternate `bin-c1205-*` outputs after the tool's normal
cleanup, preserving failed-run evidence through the documented checkpoint cleanup
path. Never force-add evidence directories.

## Cost, release checks and handoff

Proposed Code authoring floor: 219 minutes; ordinary checkpoint floor: 46 minutes;
total five-slice allocation: 265 minutes. S1 intentionally consumes one red run.
The separate Mutation provisional floor is 150 minutes for the 21 proposed PCs,
their variant review, red/restored-green cycles and reporting; TestDesign must
expand and recalculate it after binding every variant. Combined provisional Code
plus Mutation floor: 415 minutes. These are dispatch planning estimates, not
elapsed measurements or guaranteed runtimes.

After implementation review/land, the caller activates the server through the
canonical restart owner and verifies `/api/version` against the landed SHA.
No runner upgrade is required for D-2's current keepalive contract. Observe a
metadata-only subscription-proof/second-catch-up trace and watchdog due/result
logs; no live caller fault injection is authorized by this plan. Check actual
snapshot duration/size and probe/deferred counts before tuning the proposed
interval. A busy session's continuing Working verdict is expected when supported
by a fresh complete runner tail. Failed/legacy-generation probes must remain
visible and must not be reported as recovered.

TestDesign should first challenge the subscription proof, the old-end/new-prompt
guard, the cache-disabled commit gate, independent watchdog ownership/deadlines,
and producer-to-recipient restart cuts; then bind the PC pack and compile-level
checkpoint roster. The operator choices are D-1's title correction, D-2's
server-only repair, D-4's defaults, D-5's local-only scope and D-6's logs-first
visibility. All are recommendations recorded for review, not settings changed by
this dispatch. No production code, tests, configuration, board metadata or live
session was changed by Plan.

--- next stage ---
next: test-design
handoff: Audit the deterministic baseline-red gap reproduction, post-subscription proof, checked recovery/frontier guards and bounded watchdog; bind exact tests and all pending PC variants before sequential S1-S5 Code dispatches. Preserve latent-race attribution and real five-note restart/receipt evidence.
artifact: docs/superpowers/plans/2026-10-12-card-1205-turnend-gap-plan.md

## S4 F1/F2 repair (2026-10-13)

The successful generation-checked operation owns exactly one coalesced wakeup;
directory removal controls future scheduling only. T61 gates real SQL commit,
removes the directory entry before probe return, and requires the wake plus one
held adapter submission. Neighbouring before-commit, after-return and overlapping
probe-admission attempts are separate rows. T62 checks the hosted poll/backlog
allowance above. TestDesign's T61/T62 and the S4 checkpoint manifest supersede
this plan's original proposed S4 execution census. S5 remains out of scope.