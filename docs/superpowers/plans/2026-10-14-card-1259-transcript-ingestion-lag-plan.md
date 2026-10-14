# CARD-1259: keep transcript persistence ahead of delivery confirmation

Documentation plan with test design folded in. Task `4a96220f`, 2026-10-14.
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
at Code admission. The investigation branch adds its report and
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
| A confirmation waiter can consume its own confirming event. | `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:73`, `:204`, `:420` await runtime handlers. `server/Application/Services/AgentSessionRuntime.cs:371` awaits SignalR before persistence; `:402` → `:649` awaits queue work. `server/Application/Services/SessionMessageQueueService.cs:1561`, `:1570`, `:3950` hold the queue lock and wait for stored evidence. | Keep the queue lock, but remove these side effects from the ingestion continuation. Same-session persistence matters as well as cross-session persistence. |
| Only the desktop HTTP path is affected. | `server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs:359`, `:390` also await `ObserveTranscriptAsync` serially. | Exercise both transports through the shared persistence/effects split. |
| Increasing 30 seconds fixes the dependency. | Ordinary observable delivery does not pull until grace (`SessionMessageQueueService.cs:4083`, `:4243`); an awaited handler still occupies the reader during a longer wait. | S1 precedes timeout policy. Use evidence-based extension, not a larger unconditional timeout. |
| Extra Enter is known to duplicate the incident prompt. | The retained incident has one accepted UserPrompt despite three Enters. Empty-composer assumptions do not describe human input or a picker. | Reduce uncertain re-presses without claiming a measured duplicate. Retain Enter-only retry, composer ownership and modal guards. |
| The sidecar already has a resume cursor or bind size. | `src/Antiphon.SessionRunner/TranscriptSidecar.cs:19` stores binding metadata; `SessionRunnerRuntime.cs:3319` records path/how only. `SessionRunnerContracts.cs:909` has a three-field bind event. | Add a versioned companion cursor store and additive progress metadata. |
| Native EOF is sufficient evidence of delivery. | `TranscriptTailer.cs:189` exposes the normalized snapshot; the server matcher reads durable rows with baseline/body/completeness checks (`SessionMessageQueueService.cs:4335`). | EOF permits another evidence check; only a qualifying transcript record confirms an observable delivery. |

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
- **D-3 — recommended policy default for operator review:** retain 30 seconds as
  the normal confirmation allowance and use the existing 20-second grace as the
  maximum observation extension, for a 50-second total under current settings.
  Behind or Unknown suppresses extra Enter. A fresh caught-up sample plus a
  successful persistence-only pull permits the existing 7-second/three-Enter
  policy. A hard cap still applies while progress continues. These are bounded
  policy defaults, not percentiles inferred from the incident distribution.
- **D-4 — recommended compatibility default for operator review:** absent,
  expired, unreadable or generation-mismatched progress is Unknown. An old runner
  remains ingestible and transcript-confirmable, but gets no speculative extra
  Enter from the new Claude lag policy. Accept the temporary reduction in automatic
  swallowed-Enter recovery while that runner awaits upgrade. Do not treat a
  missing field as zero lag. Other providers retain their existing policy.
- **D-5 — adopted:** review this document before implementation, as commissioned.
  Test design is included below. If review accepts D-3/D-4 and the durable-store
  choices, proceed to Code; a separate TestDesign dispatch is needed only for a
  concrete review finding. No operator answer is required to publish this plan.

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

Add `server/Domain/Entities/TranscriptEffectWork.cs`, its mapping under
`server/Infrastructure/Data/Configurations/`, and a CLI-generated migration under
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
registered beside the pump in `server/Program.cs:907`. Four owned workers by
default, at most one effects batch per session, fresh service scopes, host cancellation
and joined worker tasks. Use a bounded/coalesced session wake hint; a full hint
queue leaves the durable record owed and does not block ingestion. A clock-driven
one-second recovery sweep finds owed work at startup and after a dropped hint.
Claim work with a renewable owner token; use skip-locked admission/short transactions,
and hold no database lock or session-state write lease during a callback.
An abandoned worker leaves work replayable after its claim expires. Errors keep
the work owed with bounded retry backoff and observable diagnostics.
Use the existing hosted-worker ownership conventions; an expired claim alone is
not proof that its external action failed. Source-keyed downstream idempotency
remains necessary for the callback-completed/ack-lost crash cut in V-3.

Within one session retain source order and the existing channel/review/task routing
before queue delivery (`AgentSessionRuntime.cs:619`). Use captured source identities
and completed turn windows, not a newer unrelated reply. Reuse existing correlation
claims and attempt/baseline checks for at-least-once recovery. Replaying a boundary
rechecks current working state before input. Do not replay capacity/compaction actions
against a changed generation. End the small transaction before network publication.

**Unchanged:** UUID/kind deduplication (`:1148`), session sequence rebasing (`:1178`),
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
owned ignored evidence outside the build tree. V-1 through V-5 below close S1.

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
   `src/Antiphon.SessionRunner/TerminalSeatReleaseObservation.cs:298`; preserve
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
5. Keep the 1-MiB allocation cap, but continue reading while unread bytes remain.
   After four chunks yield cooperatively and check cancellation/claim validity;
   apply the 300-ms delay only when caught up or after transient read failure.
   Journal IO failure leaves the durable frontier unchanged, reports the fault,
   and continues the existing volatile ingestion path; rebind then replays from
   the last valid durable frontier. Do not advance a resumable cursor on that path.
   Disable further journal advancement for that binding after the failure, so a
   later successful write does not jump past the unjournaled batch. Rebinding
   restores the valid prefix and rereads the omitted suffix before checkpointing.

**Unchanged:** positive binding evidence, native normalization, partial-line handling,
snapshot completeness, UUID-based server deduplication and compaction/terminal
observation guards. Restore parse-failure state; a cursor is not terminal-completion
authority. Codex/Grok tailers are outside this cursor change. No server ack endpoint
or database native-offset column is introduced.

**Tests/files:** reuse and strengthen
`TranscriptIngestionLatencyProbeTests.Cold_tail_records_latency_to_a_prompt_after_existing_history`
and retain `Appended_prompts_reach_the_event_hub_under_concurrent_file_load` for warm
coverage; extend `TranscriptTailerCompactionTests.cs` only if fixture seams need it.
V-6 through V-8 and R-1/R-2 close S2. Cursors are not written by this Plan task.

### S3 — make observation uncertainty control Enter and verification timing

**Failure removed:** treating absence in a lagging transcript as a swallowed Enter
or a completed negative verification; missing bind-size/frontier evidence.

Add an optional version-1 `TranscriptProgress` value to existing
`RunnerSessionDto` (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:219`)
and `RunnerTranscriptDto` (`:361`), their application DTOs
(`server/Application/Dtos/SessionRunnerDtos.cs:5`, `:96`), and the HTTP/phone-home
mappers (`SessionRunnerHttpClient.cs:597`, `RunnerContractMapper.cs:58`,
`PhoneHomeRunnerClient.cs:335`). Obtain lightweight progress through existing
`ISessionRunnerClient.GetAsync`; no new route or transport message is required.
`ITranscriptTailer.cs` gets a default unsupported/null implementation, preserving
other providers. `SessionRunnerRuntime.cs:3786` carries the tailer's status.

Progress contains binding identity/generation, accepted session generation,
size-at-bind, starting durable offset, observed file length, consumed complete-line
offset, partial-byte count, last normalized runner sequence, sample age and reason.
An observation is `Behind` when observed bytes remain or a partial line awaits
completion; it is `CaughtUp` only with current identity, a successful read/stat and
no parse/read uncertainty. Unsupported, unavailable or inconsistent evidence is
`Unknown`. Expose age calculated by the runner's monotonic clock; the server adds
elapsed time since response receipt, avoiding cross-host wall-clock subtraction.
A sample older than two seconds is Unknown (D-3 policy default).

Record one structured Information diagnostic on each successful bind, including
exact and sidecar binds (`TranscriptTailer.cs:654`, `:691`), with binding identity,
size, initial cursor, backlog bytes, method and cursor-resume/fallback reason.
Do not turn exact binds into heuristic-bind incidents. Keep progress updates cheap
and independent of queued side-effect work. Include before/after values and durations
for journal restore and backlog drain; do not log prompt text. Correlate server
receipt, persist start/commit completion, effects start/end, verification deadline
and Enter-suppression reason by session/generation/UUID. Existing RuntimePhase Debug
traces alone were insufficient for the morning reconstruction.

Modify `SessionMessageQueueService.cs:3922`, `:4025`, `:4083`, `:4094` and
`SupervisionSettings.cs:348`. For transcript-observable Claude delivery:

1. Preserve first body submission: LF normalization, bracketed paste, separate
   Enter, attempt/generation/baseline recording and the queue lock.
2. Poll durable matching evidence first. A complete match wins even when lag is
   Behind; an incomplete identity remains Truncated. Progress itself is not a
   positive delivery verdict.
3. At most once per second obtain progress outside the event stream. Before a
   scheduled extra Enter, perform `CatchUpTranscriptAsync`, inspect its actual
   pull/persistence success, then rerun the matcher. Expose the success facts from
   `PullAndPersistAsync` (`AgentSessionRuntime.cs:720`); its current bool only says
   that new rows were added, leaving empty success and failure indistinguishable.
   Bound each progress/pull request by the smaller of two seconds and the remaining
   verification allowance, using a linked token and the injected clock. The
   admitted runner-read retry budget (up to 120 seconds) must not outlive this
   caller deadline. Treat an expired request as Unknown; observe/join an owned
   pending operation rather than launching overlapping polls.
4. Behind or Unknown withholds the extra Enter without consuming an Enter attempt.
   Fresh CaughtUp plus a successful pull through its runner frontier and an unchanged
   generation permits the existing retry interval/cap, subject to composer/modal
   gates. Reobserve after a rebind; compare runner frontier inside its binding,
   not to the server's rebased sequence. Recheck immediately before input to narrow
   the race; concurrent human input remains a limitation of the composer contract.
5. Use monotonic elapsed time: the ordinary allowance remains 30 seconds; an
   observation-delayed attempt may consume up to the existing 20-second grace
   before emitting a negative result. Continue evidence pulls during the extension.
   A match at 31 seconds is Confirmed without an interim failure. Progress does
   not reset the absolute 50-second cap. Charge this extension against grace,
   rather than adding another grace loop afterwards. Preserve a stricter specialist
   first-input deadline; cancellation also ends the wait.
   If observation stayed fresh/caught-up throughout, keep the existing ordinary
   30-second verdict and its remaining post-failure grace; perform the bounded
   final pull before that verdict. A prior Behind/Unknown observation selects the
   pre-verdict extension once, rather than switching modes repeatedly.
6. If the cap expires while observation is Behind/Unknown, append
   `DeliveryVerdict.TranscriptObservationPending = 11` in
   `server/Domain/Enums/DeliveryVerdict.cs`, preserve attempt metadata and hold the
   typed row Pending. No failure-triggered kill, retype or attempt refund follows
   solely from that result. Extend existing held-composer admission and interrupted
   delivery reconciliation to recognize it. A later fresh caught-up pull first
   late-confirms; otherwise same-generation composer recovery can resume the
   existing Enter-only path. A generation change uses the existing new-generation
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
using a `.C1259.cs` partial; use the existing harness/test clock. Add progress
cases to `RunnerContractMapperTranscriptEventTests.cs` and runner observation
tests in `TranscriptTailerObservationTests.cs`. V-9 through V-14 and R-3 through
R-6 close S3.

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
- S3 adds optional fields: new server/old runner means Unknown, old server/new runner
  ignores them. Restart the two runner deployments independently after their own
  drain/activation checks; do not infer runner build identity from server version.
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

Acceptance after implementation: use metadata-only passive observations, without
sending diagnostic chat input. Capture bind size/frontier and a correlated
runner-receipt → server-receipt → commit → effect timeline for a queued prompt.
Report counts, percentiles, generation and binary SHAs separately by hop; compare
against the cited 31.914-second post-runner episode. A recurrence needs a phase
trace identifying its actual waiter. Exact Family morning-handler attribution is
not covered by a test or reconstructable from the retained records.

## Verification design

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
| V-10: optional progress survives transports and missing fields | `RunnerContractMapperTranscriptEventTests.C1259_progress_contract_supports_mixed_versions` | Four serialization/mapping cases: HTTP and phone-home with/without progress. Missing fields map to Unknown; full values survive; frozen old DTO reader ignores extras; accepted generation stays bound. |
| V-11: lag suppresses Enter and extends observation | `SessionMessageQueueDeliveryVerificationTests.C1259_behind_tailer_confirms_after_normal_deadline_without_extra_enter` | Test clock advances past 7, 14 and 30 seconds while Behind; one body and one Enter, no failure/kill. Pump/pull stores matching prompt at 31 seconds; result is transcript-confirmed before the 50-second cap. |
| V-12: caught-up missing confirmation permits Enter | `SessionMessageQueueDeliveryVerificationTests.A_swallowed_first_enter_is_re_pressed_and_the_delivery_confirms` | Reuse existing method with a fresh CaughtUp sample and successful pull. First Enter is swallowed, second submits exact body once, then matching transcript confirms. No body rewrite; interval and cap remain active. |
| V-13: uncertain progress is not zero lag | `SessionMessageQueueDeliveryVerificationTests.C1259_unknown_progress_withholds_reenter` | Five cases: absent legacy field, expired age, generation change, failed pull, parse/read fault. No additional Enter or screen-only success for observable Claude; returns the held uncertain outcome at the bounded cap. |
| V-14: total budget ends without unsafe redelivery | `SessionMessageQueueDeliveryVerificationTests.C1259_observation_cap_preserves_hold_until_evidence_recovers` | Two entry modes (queued/Now): increasing progress does not extend 50 seconds; no second grace, retype, kill or refund; durable hold survives service recreation. Later complete evidence releases by late-confirm with zero writes; a distinct caught-up/held-composer branch permits only existing Enter recovery. Earlier specialist deadline and cancellation retain precedence. |
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
| PC-9 | Map missing/stale progress to CaughtUp | `SessionMessageQueueDeliveryVerificationTests.C1259_unknown_progress_withholds_reenter` | Extra Enter or wrong outcome. |
| PC-10 | Suppress Enter even after fresh caught-up evidence | `SessionMessageQueueDeliveryVerificationTests.A_swallowed_first_enter_is_re_pressed_and_the_delivery_confirms` | Expected second Enter/submission/confirmation absent. |
| PC-11 | Reset the absolute cap on progress or route uncertain verdict into failure kill | `SessionMessageQueueDeliveryVerificationTests.C1259_observation_cap_preserves_hold_until_evidence_recovers` | Wait exceeds cap, hold disappears or kill/input count changes. |
| PC-12 | Bypass the CARD-0650 hold for new observation state | `ExpectationDirectDeliveryTests.C650_Uncertain_watchdog_body_blocks_later_ordinary_input` | Later input is written while ownership remains held. |

### Lanes, execution and cost

Read `GET /api/runner-defaults` and `GET /api/session-runners` at dispatch. This
planning read returned HTTP 200, defaults revision 2, with one eligible Linux and
one eligible Windows runner; an additional runner was stale/ineligible. This is
an availability observation, not a host pin. Portable rows omit `-Runner` and
`-Platform`; use `-Platform Any` only to remove an inherited OS pin. W-1/W-2 use
the Windows lane because native file identity/sharing/replacement is OS-dependent;
omit `-Runner` there too. Do not embed a fleet address in commands.

Group prefixes below name lanes: `integration-*` uses isolated PostgreSQL and
scripted adapters; `runner-*` uses generated native files without a provider;
`contract-*` is DTO mapping; `windows-*` repeats native journal checks on Windows.
No whole Unit, namespace, assembly, browser, real-provider or load-saturation run
is in this ordinary list. Serialize the rows on the loaded host. Reuse each slice's
isolated output for its other exact methods, at the same committed SHA. W rows
are a separate Windows task, not part of an unpinned run.

Run one checkpoint invocation per committed slice group, selecting CP-1..5 for
S1, CP-6..10 for S2, CP-11..20 for S3, and CP-21..22 for the Windows piece.
Use the checkpoint tool's `run --plan <this-path> --rows <ids> --serial
--expected-source-sha <full-sha> --row-timeout 15m --total-timeout 60m`; continue
`wait` on exit 75 until completion. Bootstrap a missing tool through
`scripts/build-slot.ps1` into an owned `bin-c1259-tool/`, then invoke the built tool
DLL so the row drivers acquire their own slots; do not nest a wrapper lease around
the checkpoint executor. Report bootstrap separately. Slot timeout is not-run,
not permission for an unleased retry. Delete owned alternate outputs through the
documented provenance checks after their consumers finish.

The table estimates total **49 minutes** for one green portable pass and
**7 minutes** for Windows (56 minutes combined), excluding slot waits and authoring.
Allow an additional 30 minutes for targeted red runs across the three slice groups,
15 minutes for code-review reruns if source changes, and 45 minutes for the separate
12-PC mutation round. These are planning budgets, not measured durations. Split
commissioning by slice if the caller's foreground/task budget is smaller; do not
silently replace the table with a wider or reduced lane. A row over budget is
reported with counts/reason and triaged using the failing method at the base.
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
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1259-s1/` | integration-confirming-prompt | `/*/*/SessionRunnerEventPumpTests/C1259_confirming_prompt_persists_while_confirmation_is_pending` | V-1 | 1 executed, 0 failed/skipped | 1 | 9 |
| CP-2 | S1 | CP-1 | integration-second-session | `/*/*/SessionRunnerEventPumpTests/S0_blocked_hosted_pump_probe` | V-2 | 4 executed, 0 failed/skipped | 4 | 2 |
| CP-3 | S1 | CP-1 | integration-effects-recovery | `/*/*/TranscriptEffectsTests/C1259_committed_effects_resume_after_restart_in_session_order` | V-3 | 3 executed, 0 failed/skipped | 3 | 2 |
| CP-4 | S1 | CP-1 | integration-phone-home | `/*/*/PhoneHomeEventPumpTests/C1259_pending_confirmation_does_not_block_owned_transcripts` | V-4 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-5 | S1 | CP-1 | integration-order | `/*/*/TranscriptEffectsTests/C1259_live_and_catchup_commit_order_without_duplicate_effects` | V-5 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-6 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1259-s2/` | runner-cursor-crash | `/*/*/TranscriptCursorJournalTests/C1259_resume_reads_only_uncommitted_native_bytes` | V-6 | 5 executed, 0 failed/skipped | 5 | 4 |
| CP-7 | S2 | CP-6 | runner-large-rebind | `/*/*/TranscriptIngestionLatencyProbeTests/Cold_tail_records_latency_to_a_prompt_after_existing_history` | V-7 | 3 executed, 0 failed/skipped | 3 | 2 |
| CP-8 | S2 | CP-6 | runner-cursor-fallback | `/*/*/TranscriptCursorJournalTests/C1259_invalid_cursor_replays_from_zero` | V-8 | 6 executed, 0 failed/skipped | 6 | 2 |
| CP-9 | S2 | CP-6 | runner-warm-appends | `/*/*/TranscriptIngestionLatencyProbeTests/Appended_prompts_reach_the_event_hub_under_concurrent_file_load` | R-1 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-10 | S2 | CP-6 | runner-fork | `/*/*/TranscriptTailerCompactionTests/Tailer_follows_a_mid_session_fork_such_as_clear` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-11 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1259-s3-runner/` | runner-bind-progress | `/*/*/TranscriptTailerObservationTests/C1259_bind_progress_records_size_cursor_and_backlog` | V-9 | 4 executed, 0 failed/skipped | 4 | 3 |
| CP-12 | S3 | `tests/Antiphon.Tests -> bin-c1259-s3-server/` | contract-mixed-versions | `/*/*/RunnerContractMapperTranscriptEventTests/C1259_progress_contract_supports_mixed_versions` | V-10 | 4 executed, 0 failed/skipped | 4 | 8 |
| CP-13 | S3 | CP-12 | integration-behind | `/*/*/SessionMessageQueueDeliveryVerificationTests/C1259_behind_tailer_confirms_after_normal_deadline_without_extra_enter` | V-11 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-14 | S3 | CP-12 | integration-caught-up | `/*/*/SessionMessageQueueDeliveryVerificationTests/A_swallowed_first_enter_is_re_pressed_and_the_delivery_confirms` | V-12 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-15 | S3 | CP-12 | integration-unknown-progress | `/*/*/SessionMessageQueueDeliveryVerificationTests/C1259_unknown_progress_withholds_reenter` | V-13 | 5 executed, 0 failed/skipped | 5 | 2 |
| CP-16 | S3 | CP-12 | integration-observation-cap | `/*/*/SessionMessageQueueDeliveryVerificationTests/C1259_observation_cap_preserves_hold_until_evidence_recovers` | V-14 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-17 | S3 | CP-12 | integration-composer-hold | `/*/*/ExpectationDirectDeliveryTests/C650_Uncertain_watchdog_body_blocks_later_ordinary_input` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-18 | S3 | CP-12 | integration-redraw | `/*/*/SessionMessageQueueDeliveryVerificationTests/Screen_output_advancing_without_a_record_is_no_longer_delivered` | R-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-19 | S3 | CP-12 | integration-truncation | `/*/*/SessionMessageQueueDeliveryVerificationTests/A_clipped_prefix_parks_as_truncated_not_sent` | R-5 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-20 | S3 | CP-12 | integration-framing | `/*/*/SessionMessageQueueDeliveryVerificationTests/C514_Multiline_release_keeps_LF_paste_and_separate_Enter` | R-6 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-21 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1259-windows/` | windows-cursor-crash | `/*/*/TranscriptCursorJournalTests/C1259_resume_reads_only_uncommitted_native_bytes` | W-1 | 5 executed, 0 failed/skipped | 5 | 5 |
| CP-22 | S2 | CP-21 | windows-cursor-fallback | `/*/*/TranscriptCursorJournalTests/C1259_invalid_cursor_replays_from_zero` | W-2 | 6 executed, 0 failed/skipped | 6 | 2 |
