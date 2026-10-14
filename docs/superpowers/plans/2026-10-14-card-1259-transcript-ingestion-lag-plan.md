# CARD-1259: keep transcript persistence ahead of delivery confirmation

Documentation plan with test design folded in. Original task `4a96220f`; repaired
by task `3403ceee`, 2026-10-14, against review `f2b1c61a`.
No production change or verification result is claimed by this document.

## Outcome and evidence boundary

Deliver three slices, in order: **S1 decouple persistence from transcript side
effects; S2 resume a recoverable native cursor; S3 use ingestion evidence before
another Enter or a negative delivery verdict.** S1 is independently useful and
should ship first. It removes the reproduced wait dependency responsible for the
roughly 30-second post-runner delay. S2 alone does not address that dependency;
S3 alone could mask it. Removal of the exact 07:02 Family delay remains an
acceptance observation, because the morning handler was not traced.

Sources read:

- [Desktop evidence](../../investigations/2026-10-14-card-1259-live-transcript-ingestion-lags-the-cli-by-28-s-delivery-verifier-presses-enter-again-and-fails-at-30-s-desktop-evidence.md), read from fetched `origin/master` at `efec74fd6dabf7b0672880a52f5d1c19b5a9f9aa` (not yet present in this branch's base).
- [Runner investigation](../../investigations/2026-10-14-card-1259-live-transcript-ingestion-lags-the-cli-by-28-s-delivery-verifier-presses-enter-again-and-fails-at-30-s.md), `origin/feat/card-task-baa53a4a` at `389f1f1330ef539d3fe3e01dacb3a6d90d5093bd`.
- Owners: [runtime invariants](../../session-runtime-invariants.md), [build/checkpoint contract](../../testing-and-build.md#checkpoint-manifest-card-0585), [project conventions](../../project-context.md), [HTTP operations](../../ops-http.md), [orchestration](../../orchestration-loop.md).

Code anchors below refer to assigned source
`389f1f1330ef539d3fe3e01dacb3a6d90d5093bd`. The tailer, runtime, queue service and
HTTP pump have no diff against the fetched master above. Recheck these anchors
at Code admission. Repair anchors were opened at `d48d4221f3e3b3f9ac15551100fe09898c072cce`.
The review's proposed line shifts differ from this checkout: the zero assignment is
`TranscriptTailer.cs:366` and pump registration is `server/Program.cs:907`; retain these
verified positions. UUID selection and sequence rebasing now point at executable
statements (`AgentSessionRuntime.cs:1155`, `:1200`), rather than comment starts.
Entity mapping belongs in `AppDbContext.OnModelCreating`, at
`server/Infrastructure/Data/AppDbContext.cs:136`. The investigation branch adds its report and
`tests/Antiphon.SessionRunner.Tests/TranscriptIngestionLatencyProbeTests.cs`;
both are already inherited by this task branch. Reuse those probes and the
existing `SessionRunnerEventPumpTests.S0_blocked_hosted_pump_probe`, rather than
copying their fixtures into a second reproduction suite. S0's historical test
counts passed, but its checkpoint receipt failed cleanup; it is not qualified
green evidence for this change.

## Ground truth

| Card assumption | What the code and retained evidence establish | Design consequence |
|---|---|---|
| A file watcher accounts for the observed 28–30 seconds. | `src/Antiphon.SessionRunner/TranscriptTailer.cs:366`, `:416`, `:440` start at zero, read at most 1 MiB, then delay 300 ms. The 96 MiB probe took 30.36 seconds. There is no watcher debounce in this path. | Remove intentional inter-chunk waiting while catching up, and resume a durable cursor. Do not assign the incident to this hop. |
| Family was still draining its file. | Bind was 07:00:50Z, 86.815 seconds before the prompt. Current length is 25,646,334 bytes; equal-size Windows history drained in 7.65 seconds. Historical bind length/frontier is missing. | S2 is a separate defect fix; record bind length and read frontier for future attribution. |
| Runner delivery implies prompt persistence. | Passive observation received a prompt after 0.470 seconds; CreatedAt followed receipt by 31.914 seconds. The observer used a different socket from the server. CreatedAt precedes database commit. | Separate runner availability, server receipt, and committed storage in diagnostics. |
| A confirmation waiter can consume its own confirming event. | `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:73`, `:204`, `:420` await runtime handlers. `server/Application/Services/AgentSessionRuntime.cs:372` awaits SignalR before persistence; `:402` → `:649` awaits queue work. `server/Application/Services/SessionMessageQueueService.cs:1561`, `:1570`, `:3950` hold the queue lock and wait for stored evidence. | Keep the queue lock, but remove these side effects from the ingestion continuation. Same-session persistence matters as well as cross-session persistence. |
| Only the desktop HTTP path is affected. | `server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs:359`, `:390` also await `ObserveTranscriptAsync` serially. | Exercise both transports through the shared persistence/effects split. |
| Increasing 30 seconds fixes the dependency. | Ordinary observable delivery does not pull until grace (`SessionMessageQueueService.cs:4083`, `:4243`); an awaited handler still occupies the reader during a longer wait. | S1 precedes timeout policy. Use evidence-based extension, not a larger unconditional timeout. |
| Extra Enter is known to duplicate the incident prompt. | The retained incident has one accepted UserPrompt despite three Enters. Empty-composer assumptions do not describe human input or a picker. | Reduce uncertain re-presses without claiming a measured duplicate. Retain Enter-only retry, composer ownership and modal guards. |
| The sidecar already has a resume cursor or bind size. | `src/Antiphon.SessionRunner/TranscriptSidecar.cs:19` stores binding metadata; `SessionRunnerRuntime.cs:3319` records path/how only. `SessionRunnerContracts.cs:909` has a three-field bind event. | Add a versioned companion cursor store and additive progress metadata. |
| Native EOF is sufficient evidence of delivery. | `TranscriptTailer.cs:189` exposes the normalized snapshot; the server matcher reads durable rows with baseline/body/completeness checks (`SessionMessageQueueService.cs:4335`). | EOF permits another evidence check; only a qualifying transcript record confirms an observable delivery. |
| No inserted row means no owed effect. | SyncTranscriptAsync calls refinement confirmation, Grok rules reconciliation and CARD-0288 settlement even after a duplicate snapshot (`AgentSessionRuntime.cs:967`, `:972`, `:981`). The unseen-boundary read at `:518` also admits on query failure. | Model sync reconciliation separately from row-insert work; retain pre-insert boundary admission (V-17/V-19). |
| A live tail handles a file shortened below its read offset. | `TranscriptTailer.cs:408` only reads when length exceeds offset; there is no live truncation branch. | Reset binding/cursor/partial state on detected shrink (V-20). |
| UUID dedup covers an ID-less native line. | `AgentSessionRuntime.cs:1169` selects null-ID incoming sequences; `:1195` skips a sequence already present before rebasing. | Restore journal sequences unchanged and retain/document the server collision rule (V-21). |
| An omitted progress field is a measured zero or explicit Unknown. | The existing runner DTOs have no progress member, and legacy confirmation has the configured 7-second/three-Enter policy. | Preserve legacy behavior on absence; only a capable observation changes retry policy (V-10/V-15). |

## Decisions

- **D-1 — adopted:** S1 uses an owned, durable transcript-effects work queue after
  persistence. A queue per session that still awaits confirmation in ingestion
  retains the same-session cycle. Untracked tasks lose shutdown/error ownership;
  an unbounded in-memory queue adds a memory failure mode. A small durable work
  record is the extra state needed to recover a crash after commit and before
  effects finish. This is a transcript-specific mechanism, not a general event bus.
- **D-2 — adopted:** resume at the last complete-line cursor whose normalized
  output is durably recoverable in a runner-owned journal. Keep the v1 sidecar
  format; use a separate versioned companion. Reject EOF skipping without a
  recoverable prefix, and reject cursor-after-publish with only an in-memory hub:
  either would omit evidence after a runner crash. An acknowledged server cursor
  would require a new coordinated server/runner write protocol; the local journal
  avoids that rollout dependency.
- **D-3 — adopted under caller standing authority; operator may overrule:** keep
  the normal 30-second allowance and spend up to 20 seconds from the existing
  grace on observation, with a hard 50-second total. Progress does not reset the
  cap (V-14). Freshness is two seconds; each progress/pull request gets at most
  min(two seconds, remaining allowance). Reject an added grace after that cap.
  Explicit Behind/Unknown from a capable runner suppresses speculative extra
  Enter; fresh CaughtUp plus a successful pull permits the 7-second/three-Enter
  policy. These are policy defaults, not latency percentiles.
- **D-4 — adopted under caller standing authority; operator may overrule:** a
  missing progress field selects today's legacy 7-second/three-Enter policy and
  existing verdict/grace rules; missing is not zero lag (V-10/V-15). Suppression
  for uncertainty requires explicit Unknown from a progress-capable runner;
  expired, unreadable or mismatched present evidence from a capable runner is
  classified as Unknown locally. An omitted value selects legacy fallback,
  including after a prior capable response. Behind remains a suppression reason.
  Held-message Enter-only recovery uses existing same-generation, whole-head
  composer evidence without a CaughtUp prerequisite (V-14/V-16). Deploy S3-capable
  runners before the S3 server. Reject treating missing as Unknown: that would
  strand a swallowed first Enter on legacy runners, including human-origin rows
  outside the stranded sweep. Other providers keep their existing policy.
- **D-5 — adopted:** this documentation repair goes to a short plan Review, then
  Code on acceptance. Verification design is included; no policy answer is
  outstanding. This repair does not implement or execute the proposed tests.
- **D-6 — adopted:** isolate confirmation and queue-lock waits in a separate
  four-worker lane. Four general workers with one batch per session alone can
  each spend 50 seconds confirming, leaving a fifth session's publication/reply
  queued for that interval. A per-session cap does not address four different
  blocked sessions. Keep four publication/routing workers plus four waiting
  workers, with per-session admission in each lane and explicit ordering edges
  below. This bounds concurrent waits, not arbitrary network/DB duration (V-18).

## Slices and implementation design

### S1 — persist while confirmation or transcript publication is pending

**Failure removed:** an inline transcript side effect holds the reader needed by
its confirmation, or delays a second session's transcript behind it.

Modify `server/Application/Services/AgentSessionRuntime.cs:369`, `:947`, `:1078`
and the pump call sites above. Split observation into an ingestion operation and
an effects operation. The ingestion operation performs ordered idempotent storage,
publishes the committed session-state snapshot, records owed effects, and returns.
It does not await SignalR, queue confirmation, reply routing, refinement confirmation
or compaction recovery. Apply the split to live HTTP, subscription catch-up,
phone-home live/catch-up and explicit sync. Keep the persistence-only
`CatchUpTranscriptAsync` at `:715` free of queue callbacks.

Add `server/Domain/Entities/TranscriptEffectWork.cs`; map it in
`AppDbContext.OnModelCreating` (`server/Infrastructure/Data/AppDbContext.cs:136`),
and add a CLI-generated migration under
`server/Migrations/`. The row records session, accepted generation, committed
source-row/sequence identity, effect kind, completion/retry state and claim owner.
Create owed-work rows in the transcript transaction, including the per-row fallback
at `AgentSessionRuntime.cs:1291`; do not schedule effects for a failed insert.
Uniqueness on source identity/effect kind handles duplicate live/catch-up arrivals.
SignalR payloads come from committed rows, avoiding a second body copy in the work
record. Persisting a prompt is sufficient for the confirmation loop to see it;
an effects worker is not on that read path.

Add `server/Application/Services/TranscriptEffectDispatcher.cs` and
`server/Infrastructure/Agents/SessionRunner/TranscriptEffectsHostedService.cs`,
registered beside the pump in `server/Program.cs:907`. Use the two owned worker
lanes in D-6, at most one active batch per session per lane, fresh service scopes,
host cancellation and joined worker tasks. Publication/routing workers return
after recording a dependent queue operation; they do not await its confirmation.
Queue flush, refinement confirmation (which can wait for the queue lock), and
compaction actions that call queue delivery use the waiting lane. A confirmation
gets at most the configured normal-plus-grace window (50 seconds by default);
queue-lock admission and external callbacks can add latency, so this is not a
wall-clock upper bound for the entire work item. Requeue after a batch for fairness.
The general lane continues when the four waiting workers are occupied (V-18).
Use a bounded/coalesced session wake hint; a full hint
queue leaves the durable record owed and does not block ingestion. A clock-driven
one-second recovery sweep finds owed work at startup and after a dropped hint.
Claim work with a renewable owner token; use skip-locked admission/short transactions,
and hold no database lock or session-state write lease during a callback.
An abandoned worker leaves work replayable after its claim expires. Errors keep
the work owed with bounded retry backoff and observable diagnostics.
Use the existing hosted-worker ownership conventions; an expired claim alone is
not proof that its external action failed. Source-keyed downstream idempotency
remains necessary for the callback-completed/ack-lost crash cut in V-3.

Within each session/effect stream retain source order and explicit dependencies: channel/
review/task routing precedes its queue flush (`AgentSessionRuntime.cs:619`);
compaction recovery precedes its narrow flush. Refinement confirmation precedes
the corresponding sync settlement. Persist those edges with the work. Live SignalR
publication is independent of the waiting lane, so a later committed prompt can
publish while an earlier queue confirmation is waiting. Do not hold a general
worker waiting for a dependency; defer that work and serve another session.
Use captured source identities
and completed turn windows, not a newer unrelated reply. Reuse existing correlation
claims and attempt/baseline checks for at-least-once recovery. Replaying a boundary
rechecks current working state before input. Do not replay capacity/compaction actions
against a changed generation. End the small transaction before network publication.

**Effects without a newly inserted row:** successful sync records a durable,
coalesced reconciliation request keyed by session/accepted generation, including
when its snapshot is empty or deduped. Track requested/completed revision and the
committed frontier; a request arriving during a claim stays owed for a later pass.
Persist that request after successful storage in the same commit when rows exist,
or a short transaction for the no-new-row case. Failed persistence creates no actionable request over uncommitted rows; log the
failure and let a later successful sync request reconciliation. The
persistence-only CatchUpTranscriptAsync used inside confirmation does not request
reconciliation, avoiding a feedback loop. Classify the existing paths explicitly:

| Existing path | Placement and reason | Test |
|---|---|---|
| CARD-0288 TryCatchUpSettlementAsync at `AgentSessionRuntime.cs:981` | Durable reconciliation in the routing lane, independent of AddedTurnBoundary. Recheck the open task and captured completed turn; preserve its separate failure boundary so another callback failure does not suppress settlement. Repeated sync can repair a previously stored boundary whose effect was missed. | V-17; AgentTaskCatchUpSettlementTests |
| ConfirmRefinementDeliveriesAsync at `AgentSessionRuntime.cs:967` and live UserPrompt `:394` | Durable waiting-lane work; successful sync requests it even with zero inserts. Recheck Sent refinement/null verdict and stored complete UserPrompt. Repeat is idempotent through the queue's state/lock checks. Route dependent sync settlement only after this work finishes. | V-17; AgentTaskMidTurnRefineTests |
| GrokRulesRefreshService.ReconcileAsync at `AgentSessionRuntime.cs:972` | Durable reconciliation request on successful sync, including duplicate snapshots. Preserve the refinement → rules → settlement dependency and the service's state checks. Any queue-waiting part belongs in the waiting lane. | V-17; GrokRulesCompactionRecoveryTests |
| IsUnseenTurnBoundaryAsync at `AgentSessionRuntime.cs:518` | Keep the pre-insert read on ingestion: UUID/kind replay and ApiCallId sibling TurnEnd suppress the boundary action. Preserve fail-open admission for null identity, unknown session or query failure, but create a durable boundary nudge only after a stored row/valid session is available. A query-failure admission can thus request idempotent work even when insertion dedups. Capture admission before inserting, rather than rechecking against the newly inserted row. | V-19; SessionFinishedDuplicateTests |

**Unchanged:** UUID/kind deduplication (`AgentSessionRuntime.cs:1155`), session sequence rebasing (`:1200`),
the state-store commit/publication lease, the queue's per-session composer lock,
subscription-held-event ownership, phone-home owner/epoch checks, and the matcher's
delivery verdict. Raw output and lifecycle handling are not broadly redesigned.
Their independent waits remain a limitation; S1's bounded claim covers the named
transcript publication/confirmation handlers, not arbitrary database or runner stalls.

**Tests/files:** extend
`tests/Antiphon.Tests/Application/SessionRunnerEventPumpTests.cs` and
`PhoneHomeEventPumpTests.cs`; add
`tests/Antiphon.Tests/Application/TranscriptEffectsTests.cs`. Update the existing
S0 method's oracle to persistence while held; preserve its historical name and four
cases. Remove its five-second negative wait and writing inside the compiler output
tree (`SessionRunnerEventPumpTests.cs:111`, `:134`). Diagnostics go to test output or
owned ignored evidence outside the build tree. V-1 through V-5 and
V-17/V-18/V-19 below close S1. The test-harness
migration and affected-class rows below are part of this slice.

### S2 — resume a recoverable cursor and drain backlog without polling gaps

**Failure removed:** replaying a large native prefix through 300-ms gaps after
restart/rebind. Also prevent cursor optimization from dropping an unread suffix.

Modify `src/Antiphon.SessionRunner/TranscriptTailer.cs:354`, `:483`, `:504`,
`SessionRunnerRuntime.cs:3309` and the tailer construction sites. Add
`src/Antiphon.SessionRunner/TranscriptCursorJournal.cs` with tests in
`tests/Antiphon.SessionRunner.Tests/TranscriptCursorJournalTests.cs`.
Keep `TranscriptSidecar.cs` schema 1 and its ownership/adoption rules.
The companion lives under the existing session-log root, keyed by session and
native binding generation, with restrictive permissions matching transcript logs.

The cursor is a **committed complete-line frontier C**, not the most recent read
offset. A journal frame contains format/normalizer version, file identity, C,
next runner sequence, normalized records and parser/completion state needed to
reconstruct the snapshot. Use length framing and checksum/commit footer, with
durable flush before publishing its records or adopting C. Restore the last valid
complete frame; ignore an incomplete trailing frame. The cursor and its recoverable
records are one commit unit. Checkpoints may compact the journal using temp-file
flush plus atomic replace, retaining the previous valid generation until replacement
finishes. Test the file operations on Linux and Windows.

Exact resume rules:

1. Validate session/provider, positively claimed native path, native file identity,
   format/normalizer version, and `0 <= C <= observedLength`. Use native stable
   file identity on the two supported OSes plus a bounded prefix/boundary fingerprint;
   path and length alone are insufficient. If identity evidence is unavailable,
   decline the cursor and replay from zero. Arbitrary in-place history edits away
   from the sampled fingerprints are outside the append-only provider assumption.
   Reuse/extract the native identity capture already in
   `src/Antiphon.SessionRunner/TerminalSeatReleaseObservation.cs:296`; preserve
   that observer's guards and qualify the extracted primitive with V-8/W-2.
2. Restore normalized history and next sequence from the journal, including records
   published before a crash but not yet stored by the server. The existing snapshot
   endpoint keeps that history available for reconnect catch-up. Then read native
   bytes `[C, current EOF)` in order, including bytes appended while the runner was
   stopped. EOF is a valid start only when C already equals that EOF.
3. After processing a complete-line batch, durably commit its records and new C,
   then update in-memory snapshot/publish. If the process dies before that commit,
   reread from the previous C; if it dies after commit but before publish, restore
   the records for snapshot/live replay. If it dies after persisting C but before
   the next read, the next start still reads the suffix after C. Keep partial UTF-8
   and partial JSONL bytes outside C and reread them on restart.
4. A missing, corrupt or unsupported journal, truncation, changed file identity,
   claim switch or fork starts that binding at zero with a reason diagnostic.
   Keep server deduplication as the replay defence. Preserve prior binding history
   and the session sequence floor when switching forks; isolate the new native cursor.
5. During a live tail, compare the current offset and committed C to fresh file
   length/identity before reading. If length drops below either, invalidate the
   cursor and terminal-completion evidence, discard partial bytes/parser state,
   and start a new binding generation at zero with reason LiveTruncation. Retain
   prior normalized history and the session sequence floor, as for a fork. A
   same-path replacement follows the identity-change rule. Do not wait for the
   file to grow back to the old offset (V-20).
6. Keep the 1-MiB allocation cap, but continue reading while unread bytes remain.
   After four chunks yield cooperatively and check cancellation/claim validity;
   apply the 300-ms delay only when caught up or after transient read failure.
   Journal IO failure leaves the durable frontier unchanged, reports the fault,
   and continues the existing volatile ingestion path; rebind then replays from
   the last valid durable frontier. Do not advance a resumable cursor on that path.
   Disable further journal advancement for that binding after the failure, so a
   later successful write does not jump past the unjournaled batch. Rebinding
   restores the valid prefix and rereads the omitted suffix before checkpointing.

**Null-ID replay rule:** server entries with null UUID still dedupe on incoming
sequence against stored session sequences (`AgentSessionRuntime.cs:1169`), before
rebasing at `:1200`. A journal restores the original normalized record sequences and
next-sequence value together; do not renumber its replayed prefix. V-21 covers
restart → snapshot → server persistence with a null-ID prefix and unread suffix,
and separately pins the existing collision rule after a rebased generation. The
current protocol can drop a distinct null-ID record whose incoming sequence
collides with a stored sequence; UUID records do not use that fallback. Repairing
that identity protocol is outside S2, so do not claim lossless null-ID replay
across arbitrary server rebases or cursor-loss fallback. Live truncation/fork keeps
the runner sequence floor but does not discover a higher server-only floor.

**Unchanged:** positive binding evidence, native normalization, partial-line handling,
snapshot completeness, UUID-based server deduplication and compaction/terminal
observation guards. Restore parse-failure state; a cursor is not terminal-completion
authority. Codex/Grok tailers are outside this cursor change. No server ack endpoint
or database native-offset column is introduced.

**Tests/files:** reuse and strengthen
`TranscriptIngestionLatencyProbeTests.Cold_tail_records_latency_to_a_prompt_after_existing_history`
and retain `Appended_prompts_reach_the_event_hub_under_concurrent_file_load` for warm
coverage; extend `TranscriptTailerCompactionTests.cs` only if fixture seams need it.
Add `tests/Antiphon.Tests/Application/TranscriptCursorPersistenceTests.cs` for
V-21's real journal-to-server seam. V-6 through V-8, V-20/V-21 and R-1/R-2 close
S2. Cursors are not written by this Plan task.

### S3 — make observation uncertainty control Enter and verification timing

**Failure removed:** treating absence in a lagging transcript as a swallowed Enter
or a completed negative verification; missing bind-size/frontier evidence.

Add an optional version-1 `TranscriptProgress` value to existing
`RunnerSessionDto` (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:219`)
and `RunnerTranscriptDto` (`:361`), their application DTOs
(`server/Application/Dtos/SessionRunnerDtos.cs:5`, `:96`), and the HTTP/phone-home
mappers (`server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:597`,
`RunnerContractMapper.cs:58`, `PhoneHomeRunnerClient.cs:335`). Also map session
progress in `SessionRunnerHttpClient.cs:1255` and `RunnerContractMapper.cs:33`,
reached by GetAsync at `SessionRunnerHttpClient.cs:571` and
`PhoneHomeRunnerClient.cs:180`. Obtain lightweight progress through existing
`ISessionRunnerClient.GetAsync`; no new route or transport message is required.
`ITranscriptTailer.cs` gets a default unsupported/null implementation, preserving
other providers. `SessionRunnerRuntime.cs:3786` carries the tailer's status.

Progress contains binding identity/generation, accepted session generation,
size-at-bind, starting durable offset, observed file length, consumed complete-line
offset, partial-byte count, last normalized runner sequence, sample age and reason.
An observation is `Behind` when observed bytes remain or a partial line awaits
completion; it is `CaughtUp` only with current identity, a successful read/stat and
no parse/read uncertainty. A missing value is LegacyUnsupported, distinct from
the explicit
`Unknown` state carried by S3-capable Claude tailers. A capable runner emits an
Unknown value before binding and during read/parse failure; it does not omit the
field for those states. Once a progress value has identified a capable runner
connection/epoch, a
failed read or inconsistent generation becomes Unknown locally. A subsequent
omitted value returns to legacy fallback; reconnect resets the capability
observation. A legacy connection without capability stays on D-4 when GetAsync
fails. Missing fields therefore select legacy fallback rather than CaughtUp or
explicit Unknown. Expose age calculated by the runner's monotonic clock; the
server adds
elapsed time since response receipt, avoiding cross-host wall-clock subtraction.
A sample older than two seconds is Unknown (D-3 policy default).

Record one structured Information diagnostic on each successful bind, including
exact and sidecar binds (`TranscriptTailer.cs:654`, `:689`), with binding identity,
size, initial cursor, backlog bytes, method and cursor-resume/fallback reason.
Do not turn exact binds into heuristic-bind incidents. Keep progress updates cheap
and independent of queued side-effect work. Include before/after values and durations
for journal restore and backlog drain; do not log prompt text. Correlate server
receipt, persist start/commit completion, effects start/end, verification deadline
and Enter-suppression reason by session/generation/UUID. Existing RuntimePhase Debug
traces alone were insufficient for the morning reconstruction.

Modify `SessionMessageQueueService.cs:3922`, `:4025`, `:4083`, `:4094` and
`SupervisionSettings.cs:355`. For transcript-observable Claude delivery:

1. Preserve first body submission: LF normalization, bracketed paste, separate
   Enter, attempt/generation/baseline recording and the queue lock.
2. Poll durable matching evidence first. A complete match wins even when lag is
   Behind; an incomplete identity remains Truncated. Progress itself is not a
   positive delivery verdict.
3. At most once per second obtain progress outside the event stream. Before a
   scheduled extra Enter, perform `CatchUpTranscriptAsync`, inspect its actual
   pull/persistence success, then rerun the matcher. Expose the Pulled/NeedsReload
   facts from `PullAndPersistAsync` (`AgentSessionRuntime.cs:720`); the public
   CatchUpTranscriptAsync bool at `AgentSessionRuntime.cs:715` only says that new rows were added,
   leaving empty success and failure indistinguishable.
   Bound each progress/pull request by the smaller of two seconds and the remaining
   verification allowance, using a linked token and the injected clock. Propagate
   that token through snapshot IO and persistence reads/writes; V-14 injects a
   delayed pull to check cancellation and the remaining-budget calculation. The
   admitted runner-read retry budget (up to 120 seconds) must not outlive this
   caller deadline. On a capable connection an expired request becomes Unknown;
   a legacy connection retains legacy policy. Observe/join an owned
   pending operation rather than launching overlapping polls.
4. With a missing field on a legacy connection, use the existing 7-second/three-
   Enter schedule, verdicts and grace; missing contributes no zero-lag evidence
   and selects no observation extension. With explicit Behind or Unknown, withhold
   the extra Enter without consuming an Enter attempt.
   Fresh CaughtUp plus a successful pull through its runner frontier and an unchanged
   generation permits the existing retry interval/cap, subject to composer/modal
   gates. Reobserve after a rebind; compare runner frontier inside its binding,
   not to the server's rebased sequence. Recheck immediately before input to narrow
   the race; concurrent human input remains a limitation of the composer contract.
5. Use monotonic elapsed time: the ordinary allowance remains 30 seconds; an
   observation-delayed attempt may consume up to the existing 20-second grace
   before emitting a negative result. Continue evidence pulls during the extension.
   A match at 31 seconds is Confirmed without an interim failure. Progress does
   not reset the absolute 50-second cap. For the new observation policy the cap
   is min(50 seconds, configured confirmation plus grace); the compressed test
   harness retains its shorter window. Charge this extension against grace,
   rather than adding another grace loop afterwards. Preserve a stricter specialist
   first-input deadline; cancellation also ends the wait.
   If observation stayed fresh/caught-up throughout, keep the existing ordinary
   30-second verdict and its remaining post-failure grace; perform the bounded
   final pull before that verdict. A prior Behind/Unknown observation selects the
   pre-verdict extension once, rather than switching modes repeatedly. If progress
   is CaughtUp at the extended cap and the final pull still finds no match, use
   the existing negative verdict with zero remaining grace. A later missing field
   changes retry admission to legacy policy without restarting elapsed time.
6. If the cap expires while observation is Behind/Unknown, append
   `DeliveryVerdict.TranscriptObservationPending = 11` in
   `server/Domain/Enums/DeliveryVerdict.cs`, preserve attempt metadata and hold the
   typed row Pending. No failure-triggered kill, retype or attempt refund follows
   solely from that result. Extend existing held-composer admission and interrupted
   delivery reconciliation to recognize it. Recovery first checks stored/pulled
   matching evidence for late confirmation.
   Without a match, same-generation, whole-head composer evidence can resume the
   existing Enter-only path even while progress is Behind, Unknown or absent.
   Retain modal/ownership gates; an unreadable or not-whole-visible body keeps the
   hold without retyping. Recovery is a distinct attempt to submit a positively
   identified held body, not speculative retry during the initial observation.
   A generation change uses the existing new-generation
   rules. Mode.Now returns an explicit uncertain/conflict receipt and retains the
   same session hold used by direct/watchdog delivery. The in-process queue lock
   is released after the bounded wait; the durable hold protects later input.
   Keep this classifier in the shared confirmation path so direct CARD-0650
   delivery obtains the same uncertainty result and retains its own durable hold.

**Unchanged:** transcript body identity/completeness and baseline rules; the limited
pre-first-turn and non-Claude fallback paths; CARD-0650 watchdog composer hold;
late confirmation before redelivery; picker/herdr gates; parking and session release
authority. Update the runtime owner doc with the narrowed Claude retry policy.

**Tests/files:** extend
`tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs`
by adding `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.C1259.cs`;
use the existing harness/test clock. Add progress
cases to `RunnerContractMapperTranscriptEventTests.cs` and runner observation
tests in `TranscriptTailerObservationTests.cs`. V-9 through V-16 and R-3 through R-6 close S3.

## Compatibility, risk and release

- Ship S1 server changes first; runner binaries need no S1 update. Verify same-session
  and second-session storage before confirmation finishes. Preserve failed-work
  diagnostics and effects backlog age; an effects backlog is a separate health signal.
- S2's v1 companion is independent of the existing sidecar. Old runners ignore it
  and use zero-based replay. New runners validate it after an old-runner interval;
  appended native bytes remain eligible. Unsupported versions fall back to replay.
  Keep normalized history available for an old server's snapshot pull. A disk-loss
  recovery depends on retained native history; simultaneous loss of journal and
  native history is not covered by the crash tests.
- Deploy S3-capable runners before the S3 server, using each deployment's own
  drain/activation checks; do not infer runner build identity from server version.
  Supported pairings are explicit in the matrix below.
  Server rollback after writing verdict 11 needs held rows reconciled or a rollback
  build that understands the new hold; an older server might otherwise retry them.
- Outbox growth and journal disk IO are costs. Page work reads and retention of
  completed work, scope journal retention to session-log lifecycle, and instrument
  bytes/flush duration. Preserve unfinished work and replayable transcript history
  during cleanup. New read queries must follow [resilience ownership](../../resilience.md).
- S1 removes waits for the named transcript side effects. Database admission,
  provider flush, discovery scans and unrelated event handlers can still delay
  ingestion. The seven-day CreatedAt distribution mixes live and historical rows
  and is shaped by grace pulls; it is not a service-level latency target.
- S3 reduces speculative Enter; the retained incident did not show double submission.
  Preserve CARD-0650 and CARD-0501 ownership: a readable empty composer, matching
  transcript, explicit cancellation under existing rules, or a new generation
  can resolve the corresponding hold. Raw human input can race an observation.
  A real-provider canary is outside the ordinary budget and is not replaced by a fake.
- **Input-wait release checklist:** the new observation wait releases its queue
  lock at the bounded cap (default 50 seconds, or an earlier existing deadline).
  Its durable input hold has evidence-based release conditions above, not a timer.
  It creates no automatic process stop. With `BlockedTaskParking:Enabled=false`,
  a session waiting for input retains its seat with **no automatic release deadline**
  (CARD-1083). Enabling parking is outside this plan.

| Server | Runner | Supported behavior / qualification |
|---|---|---|
| Legacy | Legacy | Existing 30-second confirmation, 20-second grace and 7-second/three-Enter policy. Baseline regression classes. |
| Legacy | S3-capable | Supported rollout first step; old DTO readers ignore optional progress. Snapshot/normalization remains available. V-10. |
| S3 | Legacy / progress field absent | Supported compatibility fallback: legacy retry/verdict/grace, no inferred CaughtUp or lag extension. V-10/V-15. |
| S3 | S3-capable | Explicit CaughtUp/Behind/Unknown policy, 50-second total cap, durable observation hold and composer-based recovery. V-11/V-13/V-14/V-16. |
| Server rollback lacking verdict 11 | Either | Requires prior held-row reconciliation or a rollback build that understands the hold; unqualified rollback with held rows is outside the supported sequence. |

**Stranded-message risk:** a swallowed Enter can leave a human-origin message on
an ordinary session unsubmitted beyond the observation cap; the stranded sweep
covers machine-origin rows on standing sessions, not that population. Missing-field
legacy fallback retains its existing automatic re-press, while capable-runner held
recovery does not demand CaughtUp before an evidence-backed Enter. These choices
remove the new missing-field/caught-up-gate strand from the earlier plan. They do
not create a new sweep for human rows: unreadable composer evidence still needs
an existing retry trigger or operator intervention, with no timed seat release.
V-15/V-16 cover these policy branches; a human-input race remains outside them.

Acceptance after implementation: use metadata-only passive observations, without
sending diagnostic chat input. Capture bind size/frontier and a correlated
runner-receipt → server-receipt → commit → effect timeline for a queued prompt.
Report counts, percentiles, generation and binary SHAs separately by hop; compare
against the cited 31.914-second post-runner episode. A recurrence needs a phase
trace identifying its actual waiter. Exact Family morning-handler attribution is
not covered by a test or reconstructable from the retained records.

## Verification design

### Harness defaults and affected callers

A source search for ObserveTranscriptAsync or SyncTranscriptAsync under tests
finds 105 call sites in 31 files at the anchor SHA. This includes two partials
of AgentSessionRuntimeTests, two of the delivery-verification class, and the
shared harness; it is not a count of independent test classes.

S1 adds test-only `tests/Antiphon.Tests/TestHelpers/TranscriptEffectsTestDriver.cs`
and wires it through `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`.
The default helper observes/syncs through production ingestion, then explicitly
awaits a deterministic drain of the durable effects owed by that call. Migrate
existing assertion-after-observe/sync call sites to that helper, including local
fixtures that construct AgentSessionRuntime directly. Keep concurrency tests on
raw ingestion with the real hosted dispatcher/barriers; do not add a production
switch that makes ObserveTranscriptAsync await effects. A drain includes dependent
work and propagates failure; it is not an arbitrary delay. Host-based fixtures
start/stop and join their dispatcher. In AgentTaskMidTurnRefineTests, move the
existing C1188 lock-wait assertion to the refinement effect while checking that
raw ingestion has already returned; preserve its positive queue-lock control.
Test teardown checks owed-work/worker cleanup.

S3 changes the shared harness's EmptyRunnerClient and relevant standalone
`tests/Antiphon.Tests/TestHelpers/FakeSessionRunnerClient.cs` fixtures to return
fresh, same-generation CaughtUp progress by default for Claude, with an empty
successful snapshot through the declared frontier. Sample age follows the injected
clock; adding fixture records updates its frontier. Preserve an explicitly supplied
SessionResponse and allow per-test missing, Unknown, Behind, stale and failed-pull
responses. Other provider fakes remain unsupported. The existing harness's compressed
3-second confirmation/3-second grace/1-second re-Enter settings stay in ordinary
regressions. C1259 timing methods explicitly use production 30/20/7/3 values with
controlled time. A fake default is fixture setup, not delivery evidence: matching
committed records still own the verdict.

CP-30..40 cover the portable affected classes, with the two methods in
AgentTaskReplyIntegrationTests.ChannelOutboundRuntime.cs selected in CP-39 instead
of the broader 224-method class. CP-40 runs the 119 existing delivery-verification
methods after S1; CP-41 reruns that class after S3, including the new cases. Source
counting yields 123 baseline argument-expanded cases. Inspect fresh TRX for the
119 baseline method names as well as the result floor, including
`A_record_carrying_a_stale_body_is_rejected_and_the_enter_is_re_pressed` and
`Stranded_watchdog_redelivers_pending_messages_on_idle_always_on_sessions`.
The pre-existing spelling of these method names is retained.

The native GrokDelegateEndToEndTests and SessionMessageQueueGrokPtyIntegrationTests
call sites use the same drain/host lifecycle adaptation; CP-42/43 run their bounded
fake-provider Windows classes after S1. Build/stage FakeGrok using their existing
project prerequisites and keep the process-spawn limiter; no production runner.
GrokRulesCompactionAcceptanceTests has two explicit, live-auth, Windows endurance
arms with 120-minute ceilings each. Update its calls for compilation, but its live
run is outside this ordinary list and needs separate commissioning. This excludes
the live endurance behavior from the claimed regression coverage.

### Test design and red-first contract

These names are implementation obligations, not executed tests. Commit the failing
test slice before its fix. Use the exact row filter below for red and then green,
with the failed assertion and source SHA recorded. New seams/DTOs may need a
compile-only scaffold in the red commit; a build error or zero tests is not the
required red. For behavior already correct before this card, retain the regression
and show assertion sensitivity with the named PC rather than fabricating a baseline
failure. Changes to probe assertions are part of the red test commit.

Use controlled task-completion barriers and injected TimeProvider. For V-1/V-2,
wait for the handler-entered barrier, emit the confirming/B event through the real
scripted stream, then observe the database commit while the handler-release barrier
is still closed. Advance the test clock to terminate a broken implementation;
avoid real-time negative sleeps. Query PostgreSQL using a second scoped context,
so a mocked notification or uncommitted tracked row is insufficient. The confirmation
case must store the prompt through the pump, not by the fake adapter inserting it.
Then release/join the handler and host in finally. Use an isolated schema per case.

| Behaviour | Named test | What it asserts |
|---|---|---|
| V-1: pending confirmation permits its own prompt persistence | `SessionRunnerEventPumpTests.C1259_confirming_prompt_persists_while_confirmation_is_pending` | Real queue enters confirmation; stream supplies matching prompt; a separate DB context sees it before releasing the handler and before advancing to timeout. Queue confirms with one submitted body/Enter and no grace pull. Current inline path is red. |
| V-2: held transcript handler permits another session | `SessionRunnerEventPumpTests.S0_blocked_hosted_pump_probe` | Reworked four existing cases (SignalR, confirmation, reconnect confirmation, throwing diagnostics) assert B's UUID/body and ordered prompt/text/end commit while A is held; B's task settles after effects release. Current oracle is inverted in the red commit. |
| V-3: owed effects survive a crash and retain ordering | `TranscriptEffectsTests.C1259_committed_effects_resume_after_restart_in_session_order` | Three fault cuts: rollback before commit leaves no work; crash after commit before wake replays work; crash after callback before completion replays without duplicate external correlation/typed body. Channel/review/task precede queue, distinct scopes are disposed, shutdown joins workers. |
| V-4: phone-home uses the same persistence split | `PhoneHomeEventPumpTests.C1259_pending_confirmation_does_not_block_owned_transcripts` | Owned same-session confirming UUID and B UUID commit while handler waits; foreign owner/epoch frame stays absent; credits release after durable ingestion. Current awaited runtime is red. |
| V-5: live/catch-up ordering and idempotence | `TranscriptEffectsTests.C1259_live_and_catchup_commit_order_without_duplicate_effects` | Duplicate UUID/kind yields one durable row and one effects obligation; persisted order and generation guards survive held subscription/reconnect. Two transport cases. |
| V-6: cursor reads suffix and survives crash cuts | `TranscriptCursorJournalTests.C1259_resume_reads_only_uncommitted_native_bytes` | Five cuts: before frame commit, after commit/before publish, after cursor commit/before next read, partial UTF-8 line, failed journal flush. Instrumented native reads exclude the valid committed prefix; snapshot plus new events contains exact marker UUIDs/bodies in order, including the crash suffix. |
| V-7: large-file rebind reaches the tail in a bounded probe | `TranscriptIngestionLatencyProbeTests.Cold_tail_records_latency_to_a_prompt_after_existing_history` | Extend existing method to three sizes: 0, 25,646,334 and 100,663,296 bytes. First bind drains without 300-ms backlog waits; rebind uses its committed cursor and emits a new marker. Assert at most one idle poll of scheduler delay and completion within 20 seconds after fixture creation in this synthetic probe. Existing 96-MiB behavior is red. Check identities/counts, not timing alone. Physical IO outside that bound fails qualification; do not widen it to hide red. |
| V-8: incompatible cursor falls back without skipping records | `TranscriptCursorJournalTests.C1259_invalid_cursor_replays_from_zero` | Six cases: v1-sidecar/no journal, future journal version, torn/checksum-invalid frame, truncation, same-path replacement, fork/claim change. Explicit fallback reason, retained/new marker identities, sequence floor and partial-line completeness. |
| V-9: bind and progress evidence describe the current file | `TranscriptTailerObservationTests.C1259_bind_progress_records_size_cursor_and_backlog` | Four binds: exact, sidecar, discovery, fork. Diagnostic size/cursor values equal fixture bytes; Behind clears only after complete parsing; read failure/stale generation reports Unknown. Exact/sidecar binds create no heuristic incident. |
| V-10: optional progress survives transports and missing fields | `RunnerContractMapperTranscriptEventTests.C1259_progress_contract_supports_mixed_versions` | Six cases: HTTP and phone-home with/without progress, a frozen old DTO reader ignoring extras, and a capable unbound tailer returning explicit Unknown. Missing fields select LegacyUnsupported; full values survive both session and transcript mappings; accepted generation stays bound. |
| V-11: lag suppresses Enter and extends observation | `SessionMessageQueueDeliveryVerificationTests.C1259_behind_tailer_confirms_after_normal_deadline_without_extra_enter` | Test clock advances past 7, 14 and 30 seconds while Behind; one body and one Enter, no failure/kill. Pump/pull stores matching prompt at 31 seconds; result is transcript-confirmed before the 50-second cap. |
| V-12: caught-up missing confirmation permits Enter | `SessionMessageQueueDeliveryVerificationTests.A_swallowed_first_enter_is_re_pressed_and_the_delivery_confirms` | Reuse existing method with a fresh CaughtUp sample and successful pull. First Enter is swallowed, second submits exact body once, then matching transcript confirms. No body rewrite; interval and cap remain active. |
| V-13: uncertain progress is not zero lag | `SessionMessageQueueDeliveryVerificationTests.C1259_unknown_progress_withholds_reenter` | Six capable-runner cases: expired age, generation mismatch, failed pull, parse/read fault, unbound explicit Unknown, and inconsistent frontier. No additional Enter or screen-only success for observable Claude; held uncertain outcome at the bounded cap. Missing on a fresh legacy connection is V-15, a different policy. |
| V-14: total budget ends without unsafe redelivery | `SessionMessageQueueDeliveryVerificationTests.C1259_observation_cap_preserves_hold_until_evidence_recovers` | Two entry modes (queued/Now): increasing progress does not extend 50 seconds; no second grace, retype, kill or refund; durable hold survives service recreation. Later complete evidence releases by late-confirm with zero writes; a distinct whole-visible held-composer branch permits Enter-only recovery while Behind. Body not whole-visible plus Behind retains the hold with zero retype. Earlier specialist deadline and cancellation retain precedence. |
| V-15: absent field preserves legacy recovery | `SessionMessageQueueDeliveryVerificationTests.C1259_legacy_missing_progress_keeps_retry_policy` | Three cases: successful Get with omitted field, legacy Get unsupported, and an omitted field after a prior capable response. Swallowed first Enter recovers on the existing interval/cap; missing selects no zero-lag evidence or new observation hold. Missing-only exhaustion uses the ordinary verdict/grace, with total 50 seconds at production settings. |
| V-16: held recovery needs composer evidence, not CaughtUp | `SessionMessageQueueDeliveryVerificationTests.C1259_held_body_recovers_without_caught_up_progress` | Six cases: Behind/Unknown/missing crossed with explicit queued retry and eligible stranded sweep. Seed verdict 11 and same-generation whole-head composer; late-confirm checks precede Enter, exactly one Enter submits with zero body retype, and a subsequent complete UserPrompt confirms. A non-swept human row stays pending until explicit retry; V-14 covers not-whole-visible refusal. |
| V-17: successful sync owes row-independent reconciliation | `TranscriptEffectsTests.C1259_sync_without_new_rows_reconciles_owed_state` | Four cases: CARD-0288 previously stored boundary settles an open task; duplicate prompt confirms a Sent refinement; duplicate snapshot reconciles Grok rules; a sync request arriving during a claimed revision remains owed. Repeat drains leave one completion and preserve refinement/rules/settlement ordering and isolated callback failure handling. |
| V-18: confirmation pool saturation permits publication/routing | `TranscriptEffectsTests.C1259_four_confirmation_waits_leave_publication_and_replies_runnable` | HTTP/phone-home cases hold four different sessions in real queue confirmation. A fifth session commits, publishes its live transcript and dispatches its correlated reply before releasing those barriers or advancing the clock to 50 seconds. Assert four waiting workers, bounded per-session admission, ordered routing-before-flush and shutdown joins. A single shared four-worker pool is red. |
| V-19: pre-insert boundary admission retains duplicate semantics | `TranscriptEffectsTests.C1259_boundary_admission_preserves_replay_and_fail_open_rules` | Five cases: UUID/kind replay; ApiCallId sibling; null UUID; query failure with successful persistence; unknown session. First two suppress extra turn flush; null identity/query failure admit idempotent durable nudges after persistence, including an existing row on dedup; unknown session stores no work and does not throw. |
| V-20: live truncation resets the reader | `TranscriptCursorJournalTests.C1259_live_truncation_rebinds_from_zero` | Two cases: complete-line frontier and pending partial UTF-8 bytes. Truncate the same open binding below offset, append a new marker below the old offset, and observe it without regrowing the old file; assert LiveTruncation reason, cleared completion/partial state, new cursor generation and retained sequence floor. |
| V-21: null-ID journal replay keeps the existing server key | `TranscriptCursorPersistenceTests.C1259_restart_preserves_null_id_sequence_dedup` | Two runner-journal → server cases: valid journal restart replays a stored null-ID prefix with unchanged sequences and inserts the unread suffix once; a deliberately rebased server sequence collides with a distinct null-ID entry and retains today's sequence suppression. Assert durable rows, sequences and UUID-kind sibling handling, documenting that second case as a compatibility limitation rather than lossless recovery. |
| R-1: warm tailing stays complete | `TranscriptIngestionLatencyProbeTests.Appended_prompts_reach_the_event_hub_under_concurrent_file_load` | Existing 1/16-session cases retain 32/512 exact prompt identities and snapshot counts. Latency remains diagnostic, not a fleet promise. |
| R-2: fork following remains intact | `TranscriptTailerCompactionTests.Tailer_follows_a_mid_session_fork_such_as_clear` | Existing fork fixture emits the new conversation and keeps its source order. |
| R-3: CARD-0650 uncertain body retains input ownership | `ExpectationDirectDeliveryTests.C650_Uncertain_watchdog_body_blocks_later_ordinary_input` | Existing hold blocks later queued input until its release evidence. |
| R-4: observable redraw does not confirm | `SessionMessageQueueDeliveryVerificationTests.Screen_output_advancing_without_a_record_is_no_longer_delivered` | Existing redraw-only fixture does not produce a transcript-confirmed receipt. |
| R-5: truncation remains terminal for that body | `SessionMessageQueueDeliveryVerificationTests.A_clipped_prefix_parks_as_truncated_not_sent` | Existing incomplete body is Truncated rather than Sent; no repair by Enter. |
| R-6: input framing stays intact | `SessionMessageQueueDeliveryVerificationTests.C514_Multiline_release_keeps_LF_paste_and_separate_Enter` | Existing multiline fixture asserts LF/paste/body and separate Enter after modal release. |
| W-1: Windows cursor file operations | `TranscriptCursorJournalTests.C1259_resume_reads_only_uncommitted_native_bytes` | Repeat V-6 on Windows to exercise file sharing, durable replacement and partial-write recovery; five cases. |
| W-2: Windows file identity/fallback | `TranscriptCursorJournalTests.C1259_invalid_cursor_replays_from_zero` | Repeat V-8 on Windows, including same-path replacement; six cases. |
| Morning handler, real Claude picker/human races, sustained fleet latency and power-loss filesystem behavior | not covered by a test | Retain these limits; passive rollout observation supplies hop timing, not a synthetic acceptance claim. |

### Mutation candidates

Method-scoped red/restored-green cycles only. PC rows are a separate post-land
Mutation obligation; ordinary Code does not run them. No source edits during a run.

| PC | Mutation | Exact method to run | Required red |
|---|---|---|---|
| PC-1 | Await effects from transcript ingestion | `SessionRunnerEventPumpTests.C1259_confirming_prompt_persists_while_confirmation_is_pending` | Prompt missing while the handler is held. |
| PC-2 | Put SignalR before storage again | `SessionRunnerEventPumpTests.S0_blocked_hosted_pump_probe` | Held publication blocks B persistence. |
| PC-3 | Mark effects complete before callback / omit durable restart scan | `TranscriptEffectsTests.C1259_committed_effects_resume_after_restart_in_session_order` | Owed effect missing after fault cut. |
| PC-4 | Save cursor without its recoverable records | `TranscriptCursorJournalTests.C1259_resume_reads_only_uncommitted_native_bytes` | Crash suffix or snapshot marker missing. |
| PC-5 | Resume at current EOF instead of committed C | `TranscriptCursorJournalTests.C1259_resume_reads_only_uncommitted_native_bytes` | Appended-during-downtime marker missing. |
| PC-6 | Restore 300-ms delay after backlog chunks | `TranscriptIngestionLatencyProbeTests.Cold_tail_records_latency_to_a_prompt_after_existing_history` | Scheduler or 96-MiB bound fails. |
| PC-7 | Accept changed file identity or future schema | `TranscriptCursorJournalTests.C1259_invalid_cursor_replays_from_zero` | Replacement/fallback marker omitted. |
| PC-8 | Ignore Behind in Enter admission | `SessionMessageQueueDeliveryVerificationTests.C1259_behind_tailer_confirms_after_normal_deadline_without_extra_enter` | Enter count exceeds one. |
| PC-9 | Map explicit stale/Unknown progress to CaughtUp | `SessionMessageQueueDeliveryVerificationTests.C1259_unknown_progress_withholds_reenter` | Extra Enter or wrong outcome. |
| PC-10 | Suppress Enter even after fresh caught-up evidence | `SessionMessageQueueDeliveryVerificationTests.A_swallowed_first_enter_is_re_pressed_and_the_delivery_confirms` | Expected second Enter/submission/confirmation absent. |
| PC-11 | Reset the absolute cap on progress or route uncertain verdict into failure kill | `SessionMessageQueueDeliveryVerificationTests.C1259_observation_cap_preserves_hold_until_evidence_recovers` | Wait exceeds cap, hold disappears or kill/input count changes. |
| PC-12 | Bypass the CARD-0650 hold for new observation state | `ExpectationDirectDeliveryTests.C650_Uncertain_watchdog_body_blocks_later_ordinary_input` | Later input is written while ownership remains held. |
| PC-13 | Classify a missing legacy field as explicit Unknown | `SessionMessageQueueDeliveryVerificationTests.C1259_legacy_missing_progress_keeps_retry_policy` | Legacy swallowed-Enter recovery fails or incorrectly produces verdict 11. |
| PC-14 | Require CaughtUp for held-composer Enter-only recovery | `SessionMessageQueueDeliveryVerificationTests.C1259_held_body_recovers_without_caught_up_progress` | The evidence-backed recovery Enter is missing. |
| PC-15 | Gate sync reconciliation on a newly inserted row | `TranscriptEffectsTests.C1259_sync_without_new_rows_reconciles_owed_state` | Existing-boundary settlement/refinement/rules obligation is missing. |
| PC-16 | Share four workers between confirmation and publication/routing | `TranscriptEffectsTests.C1259_four_confirmation_waits_leave_publication_and_replies_runnable` | Fifth session's publication/reply remains blocked at the barrier. |
| PC-17 | Retain old offset on live truncation | `TranscriptCursorJournalTests.C1259_live_truncation_rebinds_from_zero` | New marker below old offset stays absent. |
| PC-18 | Renumber the restored null-ID journal prefix | `TranscriptCursorPersistenceTests.C1259_restart_preserves_null_id_sequence_dedup` | Durable duplicate/count or sequence oracle fails. |
| PC-19 | Move the unseen-boundary read after insertion | `TranscriptEffectsTests.C1259_boundary_admission_preserves_replay_and_fail_open_rules` | New boundary's admitted work is absent. |

### Lanes, execution and cost

Read `GET /api/runner-defaults` and `GET /api/session-runners` at dispatch. The repair
read on 2026-10-14 returned HTTP 200, defaults revision 2, with one eligible Linux and
one eligible Windows runner; an additional runner was stale/ineligible. This is
an availability observation, not a host pin. Portable rows omit `-Runner` and
`-Platform`; use `-Platform Any` only to remove an inherited OS pin. W-1/W-2 use
the Windows lane because native file identity/sharing/replacement is OS-dependent;
omit `-Runner` there too. Do not embed a fleet address in commands.

Group prefixes below name lanes: `integration-*` uses isolated PostgreSQL and
scripted adapters; `runner-*` uses generated native files without a provider;
`contract-*` is DTO mapping; `windows-*` runs native journal and FakeGrok
checks on Windows. CP-42/43 require the Windows lane after S1; CP-21/22/44
require it after S2.
No whole Unit, namespace, assembly, browser, real-provider or fleet-load run
is in this ordinary list. Serialize the rows on the loaded host. Reuse each slice's
isolated output for its other exact methods, at the same committed SHA. W rows
are a separate Windows task, not part of an unpinned run.

Run one checkpoint invocation per committed slice group, selecting CP-1..5 for
S1 plus CP-23..25 and CP-30..40; CP-6..10/26/27 for S2; CP-11..20/28/29/41
for S3; CP-42/43 for Windows S1 and CP-21/22/44 for Windows S2.
Use the checkpoint tool's `run --plan <this-path> --rows <ids> --serial
--expected-source-sha <full-sha> --total-timeout <group-budget>`; continue
`wait` on exit 75 until completion. Bootstrap a missing tool through
`scripts/build-slot.ps1` into an owned `bin-c1259-tool/`, then invoke the built tool
DLL so the row drivers acquire their own slots; do not nest a wrapper lease around
the checkpoint executor. Report bootstrap separately. Slot timeout is not-run,
not permission for an unleased retry. Delete owned alternate outputs through the
documented provenance checks after their consumers finish.

The table estimates **251 minutes portable** and **47 minutes
Windows** for one green pass through the repaired plan (**298 minutes combined**),
including repeated S1/S3 class regression and the build estimates in producer rows.
These are loaded-host planning allowances, not measured test durations. Slot waits
and authoring are additional. EstimatedMinutes is expected row cost; importer
rowMinutes is max(15, 3 × EstimatedMinutes). Preserve that derived timeout instead
of overriding the delivery class to 15 minutes (CP-40/41 each estimates 25 minutes,
with a 75-minute row timeout). Report timeouts as incomplete coverage.

Total-run caps: S1 portable 240 minutes; S2 portable 90; S3 portable 120;
Windows S1 90; Windows S2 45. Those caps include room for build/slot variation and
are not summed test estimates. If a caller has a smaller foreground budget,
commission slice-specific runs and await them; do not replace rows by a whole Unit
lane. When splitting a group, include its producer CP in each separate invocation
and budget the repeated build explicitly. Within a run reuse only its owned output
at the same committed SHA. Allow another 60 minutes for method-scoped red runs,
30 for targeted review reruns after changes, and 95 for the separate 19-PC mutation
round. The 45-minute repair brief covers documentation, not execution of this list.
A row over budget is reported with counts/reason and triaged at the failing method.
The S1 EF migration-generation driver is a declared authoring operation outside
the test rows: run it through the host build-slot wrapper and report its command
and outcome. Do not add a broad verification run to generate the migration.

Plan validation is documentation-only: check paths, source anchors, table schema,
exact method roster and wording. This plan does not run future tests. Code/Review
must validate fresh TRX and source receipts and run
`scripts/check-evidence-diff.ps1` across their full base..HEAD history. Evidence stays
ignored; store the unedited CHECKPOINT lines, source SHA, counts and cleanup outcome
in the task report. The table is the closed list; proposed methods must exist before
Code claims a row complete.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1259-s1/` | integration-confirming-prompt | `/*/*/SessionRunnerEventPumpTests/C1259_confirming_prompt_persists_while_confirmation_is_pending` | V-1 | 1 executed, 0 failed/skipped | 1 | 15 |
| CP-2 | S1 | CP-1 | integration-second-session | `/*/*/SessionRunnerEventPumpTests/S0_blocked_hosted_pump_probe` | V-2 | 4 executed, 0 failed/skipped | 4 | 2 |
| CP-3 | S1 | CP-1 | integration-effects-recovery | `/*/*/TranscriptEffectsTests/C1259_committed_effects_resume_after_restart_in_session_order` | V-3 | 3 executed, 0 failed/skipped | 3 | 2 |
| CP-4 | S1 | CP-1 | integration-phone-home | `/*/*/PhoneHomeEventPumpTests/C1259_pending_confirmation_does_not_block_owned_transcripts` | V-4 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-5 | S1 | CP-1 | integration-order | `/*/*/TranscriptEffectsTests/C1259_live_and_catchup_commit_order_without_duplicate_effects` | V-5 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-6 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1259-s2/` | runner-cursor-crash | `/*/*/TranscriptCursorJournalTests/C1259_resume_reads_only_uncommitted_native_bytes` | V-6 | 5 executed, 0 failed/skipped | 5 | 8 |
| CP-7 | S2 | CP-6 | runner-large-rebind | `/*/*/TranscriptIngestionLatencyProbeTests/Cold_tail_records_latency_to_a_prompt_after_existing_history` | V-7 | 3 executed, 0 failed/skipped | 3 | 2 |
| CP-8 | S2 | CP-6 | runner-cursor-fallback | `/*/*/TranscriptCursorJournalTests/C1259_invalid_cursor_replays_from_zero` | V-8 | 6 executed, 0 failed/skipped | 6 | 2 |
| CP-9 | S2 | CP-6 | runner-warm-appends | `/*/*/TranscriptIngestionLatencyProbeTests/Appended_prompts_reach_the_event_hub_under_concurrent_file_load` | R-1 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-10 | S2 | CP-6 | runner-fork | `/*/*/TranscriptTailerCompactionTests/Tailer_follows_a_mid_session_fork_such_as_clear` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-11 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1259-s3-runner/` | runner-bind-progress | `/*/*/TranscriptTailerObservationTests/C1259_bind_progress_records_size_cursor_and_backlog` | V-9 | 4 executed, 0 failed/skipped | 4 | 8 |
| CP-12 | S3 | `tests/Antiphon.Tests -> bin-c1259-s3-server/` | contract-mixed-versions | `/*/*/RunnerContractMapperTranscriptEventTests/C1259_progress_contract_supports_mixed_versions` | V-10 | 6 executed, 0 failed/skipped | 6 | 12 |
| CP-13 | S3 | CP-12 | integration-behind | `/*/*/SessionMessageQueueDeliveryVerificationTests/C1259_behind_tailer_confirms_after_normal_deadline_without_extra_enter` | V-11 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-14 | S3 | CP-12 | integration-caught-up | `/*/*/SessionMessageQueueDeliveryVerificationTests/A_swallowed_first_enter_is_re_pressed_and_the_delivery_confirms` | V-12 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-15 | S3 | CP-12 | integration-unknown-progress | `/*/*/SessionMessageQueueDeliveryVerificationTests/C1259_unknown_progress_withholds_reenter` | V-13 | 6 executed, 0 failed/skipped | 6 | 3 |
| CP-16 | S3 | CP-12 | integration-observation-cap | `/*/*/SessionMessageQueueDeliveryVerificationTests/C1259_observation_cap_preserves_hold_until_evidence_recovers` | V-14 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-17 | S3 | CP-12 | integration-composer-hold | `/*/*/ExpectationDirectDeliveryTests/C650_Uncertain_watchdog_body_blocks_later_ordinary_input` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-18 | S3 | CP-12 | integration-redraw | `/*/*/SessionMessageQueueDeliveryVerificationTests/Screen_output_advancing_without_a_record_is_no_longer_delivered` | R-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-19 | S3 | CP-12 | integration-truncation | `/*/*/SessionMessageQueueDeliveryVerificationTests/A_clipped_prefix_parks_as_truncated_not_sent` | R-5 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-20 | S3 | CP-12 | integration-framing | `/*/*/SessionMessageQueueDeliveryVerificationTests/C514_Multiline_release_keeps_LF_paste_and_separate_Enter` | R-6 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-21 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1259-windows/` | windows-cursor-crash | `/*/*/TranscriptCursorJournalTests/C1259_resume_reads_only_uncommitted_native_bytes` | W-1 | 5 executed, 0 failed/skipped | 5 | 10 |
| CP-22 | S2 | CP-21 | windows-cursor-fallback | `/*/*/TranscriptCursorJournalTests/C1259_invalid_cursor_replays_from_zero` | W-2 | 6 executed, 0 failed/skipped | 6 | 2 |
| CP-23 | S1 | CP-1 | integration-sync-reconciliation | `/*/*/TranscriptEffectsTests/C1259_sync_without_new_rows_reconciles_owed_state` | V-17 | >=4 executed, 0 failed/skipped | 4 | 4 |
| CP-24 | S1 | CP-1 | integration-waiting-pool | `/*/*/TranscriptEffectsTests/C1259_four_confirmation_waits_leave_publication_and_replies_runnable` | V-18 | >=2 executed, 0 failed/skipped | 2 | 4 |
| CP-25 | S1 | CP-1 | integration-boundary-admission | `/*/*/TranscriptEffectsTests/C1259_boundary_admission_preserves_replay_and_fail_open_rules` | V-19 | >=5 executed, 0 failed/skipped | 5 | 3 |
| CP-26 | S2 | CP-6 | runner-live-truncation | `/*/*/TranscriptCursorJournalTests/C1259_live_truncation_rebinds_from_zero` | V-20 | >=2 executed, 0 failed/skipped | 2 | 2 |
| CP-27 | S2 | `tests/Antiphon.Tests -> bin-c1259-s2-server/` | integration-null-id-restart | `/*/*/TranscriptCursorPersistenceTests/C1259_restart_preserves_null_id_sequence_dedup` | V-21 | >=2 executed, 0 failed/skipped | 2 | 12 |
| CP-28 | S3 | CP-12 | integration-legacy-progress | `/*/*/SessionMessageQueueDeliveryVerificationTests/C1259_legacy_missing_progress_keeps_retry_policy` | V-15 | >=3 executed, 0 failed/skipped | 3 | 2 |
| CP-29 | S3 | CP-12 | integration-held-recovery | `/*/*/SessionMessageQueueDeliveryVerificationTests/C1259_held_body_recovers_without_caught_up_progress` | V-16 | >=6 executed, 0 failed/skipped | 6 | 4 |
| CP-30 | S1 | CP-1 | integration-runtime-regressions | `/*/Antiphon.Tests.Application/(AgentSessionRuntimeTests*)\|(SessionFinishedDuplicateTests*)\|(SessionContextUsagePersistenceTests*)/*` | S1 affected callers / harness drain | >=49 executed, 0 failed/skipped | 49 | 8 |
| CP-31 | S1 | CP-1 | integration-settlement-refinement | `/*/Antiphon.Tests.Application/(AgentTaskCatchUpSettlementTests*)\|(AgentTaskMidTurnRefineTests*)\|(CapacityRecoveryPersistenceTests*)/*` | S1 affected callers / harness drain | >=55 executed, 0 failed/skipped | 55 | 12 |
| CP-32 | S1 | CP-1 | integration-compaction-rules | `/*/Antiphon.Tests.Application/(CompactionRecoveryTests*)\|(GrokRulesCompactionRecoveryTests*)\|(GrokStartupReadyOrderingTests*)/*` | S1 affected callers / harness drain | >=21 executed, 0 failed/skipped | 21 | 8 |
| CP-33 | S1 | CP-1 | integration-channel-inbound | `/*/Antiphon.Tests.Application/(ChannelBridgeTests*)\|(ChannelInboundRecoveryTests*)\|(ChannelPromptCorrelationTests*)/*` | S1 affected callers / harness drain | >=105 executed, 0 failed/skipped | 105 | 15 |
| CP-34 | S1 | CP-1 | integration-channel-replies | `/*/Antiphon.Tests.Application/(ChannelReplyDurabilityTests*)\|(ChannelReplyLateConfirmDispatchTests*)/*` | S1 affected callers / harness drain | >=45 executed, 0 failed/skipped | 45 | 10 |
| CP-35 | S1 | CP-1 | integration-channel-outbound | `/*/Antiphon.Tests.Application/(ChannelOutboundDeliveryTests*)\|(ChannelOutboundRecoveryTests*)/*` | S1 affected callers / harness drain | >=56 executed, 0 failed/skipped | 56 | 12 |
| CP-36 | S1 | CP-1 | integration-phone-receipts | `/*/Antiphon.Tests.Application/(PhoneHomeQueuedTurnTests*)\|(ReceiptFailureDeliveryTests*)/*` | S1 affected callers / harness drain | >=38 executed, 0 failed/skipped | 38 | 10 |
| CP-37 | S1 | CP-1 | integration-dispatch-recovery | `/*/Antiphon.Tests.Application/(AgentTaskStandingAgentDispatchTests*)\|(AgentTaskLandReceiptWatermarkSafetyTests*)\|(WallRerouteDispatchTests*)\|(SessionMessageQueueDeliveredSpillRecoveryTests*)/*` | S1 affected callers / harness drain | >=48 executed, 0 failed/skipped | 48 | 12 |
| CP-38 | S1 | CP-1 | integration-retention | `/*/Antiphon.Tests.Application/(DataRetentionServiceTests*)/*` | S1 affected callers / harness drain | >=73 executed, 0 failed/skipped | 73 | 8 |
| CP-39 | S1 | CP-1 | integration-conversion-runtime | `/*/*/AgentTaskReplyIntegrationTests/(Internal_conversion_settles_with_usage_but_without_sources_or_follow_up*)\|(Deferred_is_durable_and_releases_runtime*)` | S1 affected conversion partial | >=2 executed, 0 failed/skipped | 2 | 6 |
| CP-40 | S1 | CP-1 | integration-delivery-after-effects | `/*/Antiphon.Tests.Application/(SessionMessageQueueDeliveryVerificationTests*)/*` | S1 delivery baseline, 119 methods | >=123 executed, 0 failed/skipped | 123 | 25 |
| CP-41 | S3 | CP-12 | integration-delivery-after-progress | `/*/Antiphon.Tests.Application/(SessionMessageQueueDeliveryVerificationTests*)/*` | S3 delivery baseline + V-11..16; stale-body and stranded recovery | >=141 executed, 0 failed/skipped | 141 | 25 |
| CP-42 | S1 | `tests/Antiphon.Tests -> bin-c1259-s1-windows/` | windows-grok-delegation | `/*/Antiphon.Tests.Application/(GrokDelegateEndToEndTests*)/*` | S1 affected native delegation/drain | >=6 executed, 0 failed/skipped | 6 | 20 |
| CP-43 | S1 | CP-42 | windows-grok-queue | `/*/Antiphon.Tests.Application/(SessionMessageQueueGrokPtyIntegrationTests*)/*` | S1 affected native queue/drain | >=5 executed, 0 failed/skipped | 5 | 12 |
| CP-44 | S2 | CP-21 | windows-live-truncation | `/*/*/TranscriptCursorJournalTests/C1259_live_truncation_rebinds_from_zero` | V-20 Windows | >=2 executed, 0 failed/skipped | 2 | 3 |
