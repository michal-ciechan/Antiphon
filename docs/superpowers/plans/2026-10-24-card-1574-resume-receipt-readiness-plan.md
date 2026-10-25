# CARD-1574: Keep resumed launches alive while receipt ingestion catches up

Date: 2026-10-24. Stage: Plan, with verification design folded in by the brief's
explicit `next=code` instruction. Source inspected:
`67860d9e35ccaac745f1be37923420c0d2214f56`. Code budget: 30–60 minutes, including
the focused checkpoints below. This document changes no running service.

## Outcome and scope

A standing Claude resume must not be killed because its accepted restart note is
still beyond the transcript reader's frontier. Give that note one durable queue
identity, establish receipt readiness before typing, and require fresh readiness
before interpreting a missing receipt after the existing verification and grace
windows. Behind or unknown evidence defers the same obligation; it proves neither
success nor failed delivery.

The investigation is
[`2026-10-24-card-1574-standing-resume-kills-accepted-restart-notes-during-transcript-lag.md`](../../investigations/2026-10-24-card-1574-standing-resume-kills-accepted-restart-notes-during-transcript-lag.md),
commit `465d51f965b6c7843b74ad6eb9299d8fbb9a1ce3` on
`feat/card-task-b491703f`. Its publication was in progress when this task started;
read with `git show` if the file is absent from the Code checkout. Its deployed
source was `5a126f6fc5ae4e16d803b628716b8f0485a37174`, not this plan's baseline.

Acceptance: an eligible resumed launch finishes boot without waiting minutes for
replay; its restart note eventually has one complete matching persisted UserPrompt;
lag causes no recovery kill, duplicate note, extra delivery attempt, or continuity
  failure charge. A genuinely missing receipt on a caught-up, idle AlwaysOn session
still reaches the existing generation-conditional recovery after the normal grace.

## Ground truth

| Card assumption / question | What the inspected code and investigation establish | Design consequence |
|---|---|---|
| Five resumed launches failed | One original delivery failure and four resume launches; all four restart-note UserPrompts exist, persisted 691–1363 seconds late | Reproduce the resume-note failure; do not claim the original Check was accepted |
| The launch has no grace | `SessionMessageQueueService.EnqueueCoreAsync` calls `TryGraceConfirmModeNowAsync`; settings default to 30-second confirmation and 20-second post-failure grace | Preserve both windows and the unrelated Mode.Now path |
| Pulling a transcript proves the reader reached its tail | `AgentSessionRuntime.CatchUpTranscriptAsync` returns whether any new row was stored. `ITranscriptTailer.Snapshot` means everything parsed **so far** | Add an explicit readiness observation; neither a successful GET nor zero new rows establishes readiness |
| A large file can simply be tailed from EOF | `TranscriptTailer.RunAsync` starts at zero for stable sequences, reads 1 MiB, then sleeps 300 ms; UUID deduplication and full recovery rely on replay | Keep replay, ordering, sequence and UUID semantics in this slice |
| Existing EOF evidence is suitable for live sessions | `TerminalTranscriptCompletion` requires child exit and settle; `TerminalSeatReleaseObservation` is a separate bounded native reread with release semantics | Introduce live ingestion progress, without changing either release/compaction protocol |
| Restart-note delivery has durable ownership | `AgentSessionService.DeliverLaunchNoteAsync` normally uses Now with no row; its catch inserts a new WhenIdle fallback | Persist the resumed note once before input; never manufacture an unattempted fallback copy after uncertain input |
| A pending retry is harmless | Queue recovery can re-press Enter/retype; attempt generation, baseline and timestamp already exist | Check receipt and readiness before those recovery writes; retain all original attempt metadata |
| A schema change is required | `SessionQueuedMessage.RuntimeActionKey` already has a unique `(AgentSessionId, RuntimeActionKey)` index; enqueue deduplicates it | Use a typed internal key for the launch obligation; no migration |
| An idle projection justifies a kill | Failure handling checks Working and a captured generation, but lag without a persist exception bypasses the persistence-blindness brake | Readiness is an additional prerequisite for a negative receipt verdict, never independent kill authority |
| Fresh fallback should fix this incident | CARD-0510's reservation is in this source, absent from the investigated running build; producer-bound/attempted input can also refuse transfer | Activation belongs to CARD-0510; do not change thresholds, transfer policy or fresh reservation |
| The 106.5-second replay floor explains all lag | It explains why three launches died before reaching their receipt chunks, not the remaining 11–23 minute delay | Per-hop latency attribution and throughput improvements remain CARD-1259 |

## Decisions

- **D-1 — Evidence gate, not a larger timer.** Use `Ready`, `Behind`, and `Unknown`
  outcomes. Only Ready plus successful persistence permits an absence decision.
  A size-scaled or fixed multi-minute grace was rejected: file size cannot bound
  transport/DB backlog, and the measured delay greatly exceeds the replay floor.
- **D-2 — Additive snapshot observation.** Append an optional readiness record to
  `RunnerTranscriptDto` and `SessionRunnerTranscriptDto`. Include an opaque native
  binding identity, parsed-through byte offset, freshly observed file length,
  and readiness/reason. The existing `AcceptedStartedAt` carries launch identity.
  Null means unsupported/unknown. HTTP and phone-home must preserve the record.
  Do not add a polling endpoint, feature negotiation round trip, or event kind.
- **D-3 — Preserve the reader.** Publish progress only after processing a complete
  chunk, under the snapshot's synchronization boundary. Invalidate readiness while
  a read is in flight and on bind/fork/revocation/reset/fault. Snapshot readiness
  checks current physical-file identity and current length, not a remembered EOF.
  Reuse `TerminalSeatReleaseObservation.IsCurrentFile` for the identity check;
  do not invoke its full-file observation or release qualification. Pending partial
  JSONL, malformed input, a shortened/replaced file or unavailable binding is not
  Ready. A valid ignored JSON record is not a parser error. Do not expose native
  paths as the opaque binding ID. Seeking past history and removing replay pacing
  were rejected for this card because they alter recovery/ordering or load control.
- **D-4 — Scope through a durable resume-note key.** Only the standing Claude path
  with non-null resume mode and a nonempty ResumeBody creates a System WhenIdle
  row with an internal key `standing-resume-note:<normalized StartedAt ticks>`.
  Scope also requires the session's actual standing owner; body text is never the
  discriminator. Use existing key uniqueness and immutable body on replay. Fresh
  launches, Grok, card launches and ordinary unkeyed Now sends retain their paths.
  Keep the Check-seat no-note exclusion. No new DB column or global setting.
- **D-5 — Do not hold the launch open.** Enqueue with `deliverIfIdle:false`; the
  launch's existing final flush and existing recovery sweeps attempt delivery.
  Return the existing "has a real input obligation" signal so boot does not add a
  synthetic probe. Resume-note deferral must not throw into launch cleanup or
  create a second fallback row. Preserve channel priority/FIFO: an owed channel
  stays ahead of the note, and receives its normal turn when the fence opens.
- **D-6 — One launch receipt fence.** While the current resumed launch has an
  unresolved keyed note, automated queue input first pulls/persists a fresh
  readiness snapshot. Behind/Unknown returns a temporary deferral before any
  typing, attempt claim, Esc, Enter or retry. This also gates automatic recovery
  and automated Now/SendNow paths that could otherwise bypass that queue fence.
  Explicit authenticated terminal input retains its existing operator contract.
  Reconcile already-attempted notes before testing Working or selecting new input.
  The fence is gone once that note has a complete correlated receipt; this is not
  a permanent extra pull on every message in a long-lived resumed session.
- **D-7 — After a submit, preserve uncertainty.** Keep the normal confirmation
  window and row-based grace. On this keyed path, a screen-only Delivered result
  still needs the complete UserPrompt receipt; route it through the same bounded
  transcript confirmation/grace without another input write. If
  NoTranscriptRecord/NoSubmitOutput remains, take
  a fresh readiness observation and recheck the receipt before failure handling.
  Behind/Unknown retains the same Pending row, charged attempt, generation,
  original timestamp/baseline and failure-verdict evidence; no attempt refund,
  kill, retyping, Enter-only recovery or second note. A later sweep pulls and
  late-confirms first. Ready with no receipt may re-enter the existing failure
  policy once; it cannot itself mark Sent. NoComposerEvidence and Truncated keep
  their existing policies after an admitted attempt; this is not a general
  exemption for a broken composer.
- **D-8 — Fail closed across persistence and identity changes.** A new runtime
  observation method returns Ready only for the requested session and accepted
  generation, structurally complete snapshot, unchanged runner route/store and
  launch ownership, and successful persistence with no NeedsReload/persist fault.
  Use existing generation normalization and state-lease patterns; do not hold a
  state lease across a call that reacquires it. Recheck current generation after
  asynchronous work. Runner sequences and stored sequences are different spaces;
  continue UUID-aware ingestion and capture the delivery baseline only **after**
  historical replay is persisted. Already-stored snapshots can be Ready even if
  catch-up stores zero new rows. A stale historical identical note cannot receipt
  the new attempt.
- **D-9 — Bounded work, no destructive expiry.** One readiness pull/persist cycle
  has a provisional five-second linked cancellation budget. Timeout is Unknown,
  not failed delivery; the existing deferred-action/stranded-queue machinery owns
  future checks (the stranded age default is 60 seconds). Release locks between
  checks; no minute-long task on the launch thread. Record reason, session,
  generation, offsets and elapsed time, not payloads. Reuse existing boot-stall
  attention instead of creating a new alert stream or timer-based kill.
  Distinguish the observation's own timeout from caller cancellation; its timeout
  must not escape as launch cancellation and trigger launch cleanup.
- **D-10 — Compatibility and activation.** New server + old runner receives null
  readiness and retains the resumed obligation without automatic input/recovery;
  new runner + old server remains wire compatible but has the old false-kill risk.
  Readiness is not active protection until both matching product components are
  deployed. Reused/adopted older pty-hosts need explicit qualification of the new
  observation. Never substitute a capability advertisement for the actual DTO.
  Activation/rollout is a caller-owned follow-up, not part of this Code slice.

These are implementation decisions within the commissioned scope, not pending
operator defaults. No human decision is needed to begin Code.

## State and recovery contract

| Boundary | Durable fact / recovery | Allowed result |
|---|---|---|
| Resume note created, input not yet attempted | Unique key + body + destination committed; repeated enqueue reads that row | Launch may finish; pending note waits for readiness and normal queue admission |
| Reader behind or unknown before input | No attempt metadata is added; same key remains visible | No input or kill; finite check returns and later sweep tries observation again |
| Ready snapshot persisted | Capture current stored transcript baseline, then claim ordinary queue attempt before writing | Existing guarded body + separate Enter and verification |
| Provider accepts but reader/transport/DB falls behind | Preserve actual attempt metadata and the one row | Complete persisted UserPrompt can confirm at any later sweep, including after service restart |
| Server stops after claim or after input | Existing Sent-with-null-verdict recovery is also fenced, before Enter/retype | Pull and receipt reconciliation first; absence during unread history cannot cause retry |
| Readiness returns, receipt missing | Recheck receipt, current generation, live state and Working; retain existing grace and conditional kill policy | Idle genuine failure remains recoverable; Working remains owned |
| Enqueue response/event fails after commit | Re-enter the same key; do not issue fallback Now or another row | One durable note survives |
| Generation changes during a check | Observation rejected; no writes/kill against either generation under that stale check | Existing generation-change recovery applies only after a fresh full catch-up; never transfer attempted notes |

Complete matching UserPrompt is this note's acceptance oracle. Screen redraw,
composer departure, readiness, a queue status, a queued-command attachment and a
model acknowledgement are not substitute receipts. Use the note-specific matcher
on this narrow path without changing other origins' existing receipt contracts.

### Waiting-session release checklist

This creates an ingestion wait, not a new AwaitingInput state. The session keeps
its seat and the launch releases its own worker/locks promptly. Readiness plus
receipt/ordinary delivery resolves the wait; an operator can use existing explicit
controls, and a process can exit naturally. Elapsed wait never releases the seat.
If the session also waits for human input, `BlockedTaskParking:Enabled=false`
means **no automatic release deadline** (CARD-1083). Working is never stopped by
this gate. CARD-0079's independently qualified Check auto-compaction exception is
unchanged and cannot be invoked using this weaker progress observation.

## Slices

1. **S1-red — Reader/transport witness.** Add the optional contract shape with
   unknown defaults and new `tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs`.
   Read the real tailer with small multi-chunk JSONL and a deterministic poll barrier,
   not a 373 MB fixture. Commit/push the compiling failing witness before the fix.
2. **S1 — Runner observation and transport.** Change
   `src/Antiphon.SessionRunner/TranscriptTailer.cs`,
   `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`,
   `server/Application/Dtos/SessionRunnerDtos.cs`,
   `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`, and
   `server/Infrastructure/Agents/SessionRunner/RunnerContractMapper.cs`.
   A small `src/Antiphon.SessionRunner/TranscriptReadiness.cs` may hold synchronized
   progress if that keeps the tailer readable. `SessionRunnerRuntime.RunnerSession.GetTranscript`
   must retain both the readiness and existing accepted-generation stamp; touch
   it only if necessary. No ITranscriptTailer-wide implementation requirement for
   other providers. Commit/push, then CP-2.
3. **S2-red — Launch/queue witnesses.** New
   `tests/Antiphon.Tests/Application/StandingResumeReceiptReadinessTests.cs`, based
   on `BridgeQueueHarness`, `ClaudeDialogFixture`, and the real launch chain in
   `AgentSystemPromptLaunchTests`. Extend the harness's `EmptyRunnerClient` only
   with bound snapshot/fault/barrier controls; do not make all legacy fake runners
   magically Ready. Add optional unknown-returning runtime observation scaffolding
   if compilation requires it. Commit/push and record CP-3 red before the fix.
4. **S2 — Durable note and readiness integration.** Change
   `server/Application/Services/AgentSessionService.cs`,
   `server/Application/Services/AgentSessionRuntime.cs`, and
   `server/Application/Services/SessionMessageQueueService.cs`; use a narrowly
   named helper/partial file for the shared readiness fence if needed. Wire first
   delivery, interrupted claim recovery, late-confirm and negative-verdict paths
   to the same rule. Do not duplicate a new scheduler or mutate supervisor counters.
   Update the existing launch test's fake readiness explicitly where it actually
   exercises a resume. Add the contract paragraph to
   `docs/session-runtime-invariants.md`. Commit/push; run CP-4 and CP-5.

No production edits while checkpoints run. If a material implementation change
cannot fit the 60-minute budget, preserve completed commits and report the exact
remaining slice; do not replace this plan with an unmeasured longer timeout.

## Verification design

### Inspection

Bodies read: `TranscriptTailer.RunAsync/Snapshot/ObserveCompactionSilenceAsync`,
`ITranscriptTailer`, `TerminalTranscriptCompletion`,
`TerminalSeatReleaseObservation.Bind/IsCurrentFile`, runner and server transcript
DTOs and both transport mappings; `AgentSessionRuntime.PullAndPersistAsync`,
`ProbeWorkingTranscriptAsync`, `CatchUpForReceiptAsync`; launch-note selection and
launch completion/cleanup; queue Now grace, keyed enqueue, attempt claim, late
confirmation, interrupted recovery, failure handling, Working/generation brakes;
`StandingRestartEscalationPolicy.CanTransfer`.

Test bodies/helpers read: `TranscriptTailerObservationTests.BoundWorld`,
`AgentSystemPromptLaunchTests` (real control/launch queue, resume, channel yield,
failed-note fallback, registering adapter), `BridgeQueueHarness` (runner snapshot
control, submission callback, DB and clock),
`SessionMessageQueueDeliveryVerificationTests` (grace, stale prompt, Working,
generation and repeated-attempt outcomes), and
`TurnEndGapRestartDeliveryTests` (durable recipient receipts across service rebuild).
Do not use the latter's process-heavy fixture for this narrow change.

### Delivery inventory

Producer is the real standing `AgentSessionService` resume; destination is the
same owned session/generation's Claude composer. The existing queue transaction
commits the keyed obligation, and its claim commits attempt/baseline before input.
The runner snapshot travels over either existing transport; normal ingestion
commits UserPrompt before confirmation. Queue sweeps/deferred actions recover by
the same row/key, not a recreated payload. Exercise busy channel recipient and
already-eligible recipient, failed enqueue acknowledgement after commit, restart
after claim, and restart after native acceptance but before persisted receipt.
Faults before an insert commit create no accepted obligation and must be reported
as enqueue failure, never asserted as delivered.

Fakes substitute for provider process and network scheduling only. They must
drive real launch, queue, ingestion and DB state transitions. They cannot prove
native Claude acceptance, deployed binary activation or the source of the incident's
extra latency; the investigation supplies the native-acceptance evidence. No live
provider call, synthetic input to a real agent, or production runner is needed.

### Proves it works now

All names below are new unless qualified as existing. Each method has one TUnit
execution; scenario matrices inside methods must assert each named outcome and
remain visible in diagnostics. Do not count internal scenarios as test executions.

| ID | Method / layer | Required observable outcome |
|---|---|---|
| V-1 | `ResumeReceiptReadinessTests.C1574_Reader_progress_tracks_complete_current_file` | Real tailer: behind at an intermediate chunk; Ready only after its complete tail is in the snapshot. Appending a complete receipt immediately invalidates old EOF; re-read returns Ready with that receipt. Stable replay sequence/UUIDs |
| V-2 | `ResumeReceiptReadinessTests.C1574_Invalid_binding_or_partial_tail_is_unknown` | Individually exercise partial JSONL, malformed JSON, claim revocation, fork, same-length file replacement and truncation. Each independently refuses Ready; normal complete file recovers where supported |
| V-3 | `StandingResumeReceiptReadinessTests.C1574_Transport_and_persistence_fence_readiness` | HTTP DTO and real phone-home mapper carry readiness; legacy JSON stays unknown. Real runtime probe distinguishes stored-zero-new Ready from behind, mismatched session/generation/route, failed pull and failed commit; no Ready while commit is held |
| V-4 | `StandingResumeReceiptReadinessTests.C1574_Resume_note_waits_then_receipts_once` | Real Start/launch creates one keyed row and completes boot while reader is behind. Old identical restart note does not receipt it. With eligible and busy/channel-recipient cases, catch-up then ordinary delivery yields exactly one complete stored new UserPrompt, one attempt, no kill/fresh/continuity charge, correct channel-before-note order |
| V-5 | `StandingResumeReceiptReadinessTests.C1574_Accepted_note_outlives_grace_without_retyping` | After actual fake-provider acceptance, keep receipt absent beyond 23 virtual minutes with Behind then Unknown snapshots. Flush, automated Now and SendNow produce no second body/Enter, kill or attempt increment. Release matching UserPrompt through real ingestion; one original row becomes confirmed without more input |
| V-6 | `StandingResumeReceiptReadinessTests.C1574_Recreated_services_recover_the_original_note` | Rebuild service graph at each cut: insert committed/response failed; claim committed/input uncertain; accepted/native receipt not persisted. Same key/row/body/floor/attempt survives. Receipt recovery through real queue produces one stored complete UserPrompt and no fallback duplicate; joining all fixture tasks is mandatory |
| V-7 | `StandingResumeReceiptReadinessTests.C1574_Ready_absence_keeps_existing_recovery_rules` | Ready + genuine missing receipt waits original grace then makes one conditional kill for idle AlwaysOn. Independently Working, non-AlwaysOn, changed generation, or a receipt arriving at the final check prevent that kill. NoComposerEvidence and Truncated follow their existing policy; no screen/QueuedUserPrompt substitute confirms this note |

### Guards the regression

- **R-1:** V-1/V-2 distinguish a reader prefix from current-file readiness. A
  successful HTTP response, large LastSequence, or remembered EOF cannot pass.
- **R-2:** V-3 distinguishes transport readiness from committed, same-launch
  receipt visibility, including an all-duplicate successful pull.
- **R-3:** V-4 uses the actual resume producer and recipient transcript; merely
  creating a Pending row cannot satisfy it. Busy and eligible cases are both required.
- **R-4:** V-5/V-6 reproduce the destructive deadline and duplicate-fallback
  windows, including process-independent reconstruction of attempt ownership.
- **R-5:** V-7 and CP-5 retain genuine-wedge recovery, old Now grace, channel yield
  and normal fresh bootstrap. An unconditional "never kill" fix fails acceptance.

### Guard inventory

| Guard | Safety-critical boundary | Positive control |
|---|---|---|
| G-1 | D-3 processing completes before publishing progress | PC-1 |
| G-2 | D-3 fresh file-length comparison | PC-2 |
| G-3 | D-3 revoked claim invalidates progress | PC-3 |
| G-4 | D-3 fork resets progress | PC-4 |
| G-5 | D-3 physical-file identity comparison | PC-5 |
| G-6 | D-3 partial JSONL refuses readiness | PC-6 |
| G-7 | D-3 malformed JSON refuses readiness | PC-7 |
| G-8 | D-8 snapshot session identity | PC-8 |
| G-9 | D-8 accepted/current generation identity | PC-9 |
| G-10 | D-8 unchanged runner route | PC-10 |
| G-11 | D-8 unchanged runner store identity | PC-11 |
| G-12 | D-8 persistence completes before Ready | PC-12 |
| G-13 | D-8 persistence failure/NeedsReload refuses Ready | PC-13 |
| G-14 | D-4/D-5 durable keyed row exists before input | PC-14 |
| G-15 | D-5 re-entry cannot mint a fallback copy | PC-15 |
| G-16 | D-6 flush admission readiness fence | PC-16 |
| G-17 | D-6 automated Now readiness fence | PC-17 |
| G-18 | D-6 SendNow readiness fence | PC-18 |
| G-19 | D-6 interrupted-claim recovery readiness fence | PC-19 |
| G-20 | D-7 negative-verdict readiness fence, including null legacy evidence | PC-20 |
| G-21 | D-7 receipt must contain the complete body | PC-21 |
| G-22 | D-8 receipt must be above the original attempt floor | PC-22 |
| G-23 | D-7 UserPrompt kind; no screen/queued-command substitute | PC-23 |
| G-24 | D-7 receipt reconciliation precedes retry | PC-24 |
| G-25 | D-7 attempt metadata retained on deferral/reconstruction | PC-25 |
| G-26 | D-9 observation timeout is a deferral, not launch cancellation | PC-26 |
| G-27 | D-3 an observed shortened binding stays invalid through regrowth | PC-27 |
| G-28 | D-3 malformed UTF-8 cannot be hidden by replacement decoding | PC-28 |

Existing Working, non-AlwaysOn and conditional-kill generation guards are retained,
not weakened or replaced; V-7 exercises them and their pre-existing tests remain
owned by CARD-0055/CARD-0679. No new authority is added to CARD-0079. Each newly
introduced, independently bypassable predicate has a separate PC below.

### Positive controls

Mutation executes these after land on a bound pack, one exact method at a time;
Code runs ordinary red witnesses and V/R. No PC is claimed executed by this Plan.
Recipes are source-bound during Code; preserve the concrete statement locations
and variant names in its report. Never accept a build error or zero tests as red.

| PC | Compiling defect / variant | Exact method and decisive red oracle |
|---|---|---|
| PC-1 | Publish progress before processing the chunk | V-1 method: Ready remains false at the held processing boundary |
| PC-2 | Use remembered length after an append | V-1 method: old EOF is not Ready |
| PC-3 | Bypass revoked-claim check | V-2 method: revoked binding refuses Ready |
| PC-4 | Preserve progress across fork | V-2 method: new binding refuses old readiness |
| PC-5 | Bypass physical-file comparison | V-2 method: same-length replacement refuses Ready |
| PC-6 | Ignore pending bytes | V-2 method: partial line refuses Ready |
| PC-7 | Ignore parse failure | V-2 method: malformed line refuses Ready |
| PC-8 | Accept wrong snapshot session | V-3 method: wrong-session observation is Unknown |
| PC-9 | Accept wrong accepted/current generation | V-3 method: changed-generation observation is Unknown |
| PC-10 | Ignore a changed runner route | V-3 method: route-race observation is Unknown |
| PC-11 | Ignore a changed runner store | V-3 method: store-race observation is Unknown |
| PC-12 | Return Ready before awaiting persistence | V-3 method: held commit keeps observation incomplete |
| PC-13 | Treat failed/NeedsReload persistence as success | V-3 method: faulted commit cannot return Ready |
| PC-14 | Use direct Now for resumed note | V-4 method: durable keyed row exists before first input |
| PC-15 | Mint a fallback key after commit/response failure | V-6 method: exactly one original row and recipient prompt |
| PC-16 | Bypass flush readiness gate | V-5 method: no additional body/attempt while Behind |
| PC-17 | Bypass automated Now readiness gate | V-5 method: blocked automated Now writes no input |
| PC-18 | Bypass SendNow readiness gate | V-5 method: blocked SendNow writes no input |
| PC-19 | Bypass interrupted recovery readiness gate | V-6 method: no Enter or body before readiness/receipt |
| PC-20 | Allow destructive failure on non-Ready evidence | V-5 method: zero conditional kills beyond the virtual deadline |
| PC-21 | Accept a clipped body as complete | V-7 method: clipped receipt never confirms note |
| PC-22 | Ignore attempt floor | V-4 method: old identical restart note cannot confirm new row |
| PC-23 | Accept queued-command/screen evidence as receipt | V-7 method: neither surrogate confirms note |
| PC-24 | Retry before checking late receipt | V-5 method: exactly one submitted body after catch-up |
| PC-25 | Refund/clear the attempt record on defer | V-6 method: original attempt fields and row ID remain equal |
| PC-26 | Propagate observation timeout into launch cleanup | V-4 method: timed-out pull leaves completed live launch and retained note |

| PC-27 | Forget observed truncation when the same file regrows | `C1574_Truncation_regrowth_and_invalid_utf8_stay_unknown`: truncation-regrowth is never Ready without replay |
| PC-28 | Remove strict byte validation before replacement decoding | `C1574_Truncation_regrowth_and_invalid_utf8_stay_unknown`: invalid-utf8 refuses Ready |

Inventory: guards=28, mapped=28, missing=0, duplicate PC maps=0. If implementation
shares a gate, each entry point still needs its named reachability witness. Ordinary
red proofs cover each changed behavior before its fix; post-land controls remain
pending until their own witnessed red/restore/green runs.

### Out of scope

CARD-1259 latency attribution, event-pump concurrency and replay throughput; seek
indexes and pruning; CARD-0510 deployment/fresh policy; backfilling/canceling old
live queue rows; changes to parking, terminal release, compaction continuation,
provider dialogs, CLI arguments or messaging envelopes. No broad suite or native
provider canary is justified by this portable observation/queue change. There is
no new OS branch; existing physical-file identity helpers remain unchanged. If
Code must change those helpers, native Windows qualification becomes additional
required scope with a revised checkpoint/budget, not inferred from Linux results.

### Checkpoints

Lane for all rows: **portable .NET runner/server, no provider processes**. Placement
uses live runner defaults: read `GET /api/runner-defaults` and
`GET /api/session-runners` before dispatch. Both reads succeeded during Plan
(default revision 2; eligible Windows and Linux capacity was advertised). These
are observations, not host pins. Omit `-Runner` and `-Platform`; never embed a fleet
location in commands. Use the checkpoint tool and host build-slot gate.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.SessionRunner.Tests -> bin-c1574-reader-red/` | portable-reader-red | `/*/*/ResumeReceiptReadinessTests/*` | V-1,V-2,R-1 | 2 executed; V-1 has an assertion failure on absent Ready (not compile/setup); record actual V-2 outcome | 2 | 2 |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1574-reader/` | portable-reader-green | `/*/*/ResumeReceiptReadinessTests/*` | V-1,V-2,R-1 | 3 passed, 0 failed/skipped; every named matrix plus the truncation-regrowth/invalid-utf8 extension checked | 3 | 2 |
| CP-3a | S2a-red | `tests/Antiphon.Tests -> bin-c1574-observe-red/` | portable-observe-red | `/*/*/StandingResumeReceiptReadinessTests/C1574_Transport_and_persistence_fence_readiness` | V-3,R-2 | 1 executed; assertion red on absent runtime Ready | 1 | 4 |
| CP-4a | S2a | `tests/Antiphon.Tests -> bin-c1574-observe/` | portable-observe-green | `/*/*/StandingResumeReceiptReadinessTests/C1574_Transport_and_persistence_fence_readiness` | V-3,R-2 | 1 passed, 0 failed/skipped; all runtime/transport scenarios checked | 1 | 4 |
| CP-4b | S2a | `tests/Antiphon.Tests -> bin-c1574-observe/` | portable-observe-adjacent | `/*/*/WorkingTranscriptRecoveryTests/C1205_Generation_changed_after_validation_must_not_append_old_end` | R-2 | 1 passed, 0 failed/skipped; existing local recovery rejects stale generation before append | 1 | 1 |
| CP-31 | S2-red | `tests/Antiphon.Tests -> bin-c1574-queue-red/` | queue-1 | `/*/*/StandingResumeReceiptReadinessTests/C1574_Resume_note_waits_then_receipts_once` | V-4 | 1 executed; record assertion witness | 1 | 3 |
| CP-32 | S2-red | `tests/Antiphon.Tests -> bin-c1574-queue-red/` | queue-2 | `/*/*/StandingResumeReceiptReadinessTests/C1574_Accepted_note_outlives_grace_without_retyping` | V-5 | 1 executed; record assertion witness | 1 | 3 |
| CP-33 | S2-red | `tests/Antiphon.Tests -> bin-c1574-queue-red/` | queue-3 | `/*/*/StandingResumeReceiptReadinessTests/C1574_Recreated_services_recover_the_original_note` | V-6 | 1 executed; record assertion witness | 1 | 3 |
| CP-34 | S2-red | `tests/Antiphon.Tests -> bin-c1574-queue-red/` | queue-4 | `/*/*/StandingResumeReceiptReadinessTests/C1574_Ready_absence_keeps_existing_recovery_rules` | V-7 | 1 executed; record assertion witness | 1 | 3 |
| CP-41 | S2 | `tests/Antiphon.Tests -> bin-c1574-queue/` | queue-1 | `/*/*/StandingResumeReceiptReadinessTests/C1574_Resume_note_waits_then_receipts_once` | V-4 | 1 passed, 0 failed/skipped | 1 | 3 |
| CP-42 | S2 | `tests/Antiphon.Tests -> bin-c1574-queue/` | queue-2 | `/*/*/StandingResumeReceiptReadinessTests/C1574_Accepted_note_outlives_grace_without_retyping` | V-5 | 1 passed, 0 failed/skipped | 1 | 3 |
| CP-43 | S2 | `tests/Antiphon.Tests -> bin-c1574-queue/` | queue-3 | `/*/*/StandingResumeReceiptReadinessTests/C1574_Recreated_services_recover_the_original_note` | V-6 | 1 passed, 0 failed/skipped | 1 | 3 |
| CP-44 | S2 | `tests/Antiphon.Tests -> bin-c1574-queue/` | queue-4 | `/*/*/StandingResumeReceiptReadinessTests/C1574_Ready_absence_keeps_existing_recovery_rules` | V-7 | 1 passed, 0 failed/skipped | 1 | 3 |
| CP-35 | S2-scope-red | `tests/Antiphon.Tests -> bin-c1574-queue-red/` | queue-body-scope | `/*/*/StandingResumeReceiptReadinessTests/C1574_Ordinary_repeated_note_body_keeps_direct_send_contract` | D-4,R-5 | 1 executed; exact compatibility witness | 1 | 2 |
| CP-45 | S2-scope | `tests/Antiphon.Tests -> bin-c1574-queue/` | queue-body-scope | `/*/*/StandingResumeReceiptReadinessTests/C1574_Ordinary_repeated_note_body_keeps_direct_send_contract` | D-4,R-5 | 1 executed; exact compatibility witness | 1 | 2 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1574-queue/` | portable-existing-contracts | `/*/*/SessionMessageQueueDeliveryVerificationTests/Card0164_ModeNow_*` | R-5 | 3 passed, 0 failed/skipped; existing direct-send grace remains intact | 3 | 2 |

CP-1/CP-3 deliberately exit with failed assertions and are historical diagnostic
red receipts, never green qualification. Compile scaffolding must default Unknown;
do not implement the gate to make a red-only build compile. CP-4 includes calls
through the real fresh-launch and channel-yield fixture paths within V-4/V-7;
its new tests assert those unchanged behaviors. Avoid a whole launch/queue class
rerun merely to include those paths. The ordinary list is closed: no full suite.

Run one checkpoint invocation per committed slice group with the plan and exact
source SHA; select only the rows due for that group. Use the tool's `run`, then
`wait` until the result is not 75; own and join every execution before settlement.
Selections are `--rows CP-1`, `--rows CP-2`, `--rows CP-3`, then
`--rows CP-4,CP-5 --serial`. Supply `--expected-source-sha <full-committed-sha>`
on each run, `--max-wait 00:00:45` on run/wait, and retain the printed run path
for `wait --run <path>`. Override importer limits with `--row-timeout 4m` for
CP-1/2 and `--row-timeout 8m` for CP-3; use the imported manifest with individual
limits of 8 and 4 minutes for CP-4/5. Intended red rows retain their original
receipts; CP-2/4 are the distinct narrow green records after the fixes.
Use `scripts/build-slot.ps1` if compiling/invoking the checkpoint CLI via `dotnet
run`; each checkpoint driver acquires its own slot. Do not double-wrap
`scripts/run-checkpoint.ps1`. Report admission wait separately. Preserve unedited
CHECKPOINT lines, counts, source/build provenance and expected-red assertion names.
Slot timeout means not run. Clean final qualification uses CP-2 at S1 and CP-4/5
at S2; rerun the reader only if S2 changes its production code or contract semantics.
Delete task-owned alternate `bin-c1574-*` outputs before settlement, retaining
the ignored receipts and timing evidence.

### Cost

Ordinary checkpoint floor: **14 minutes estimated** (2+2+4+4+2), including
isolated builds. Authoring/inspection/integration: **25–40 minutes estimated**;
total **39–54 minutes**, reserve up to **60 minutes**. Slot wait is separate and
bounded. No tests were executed by Plan and none of these costs is a measurement.
Compared with the documented approximately 25.5-minute Antiphon.Tests full run,
the 10-minute server portion avoids at least 15.5 minutes of unrelated execution,
before accounting for full-run repeats; comparisons are estimates across different
fixture mixes, not measured savings.

Post-land Mutation: **28 controls**, budget **3 minutes
per method-scoped red/restore/green cycle** including amortized build/setup:
**84 minutes total provisional**, split into four independently joined
batches of at most seven controls / 21 minutes each. Only PCs require that work;
do not run them during Code. If a batch/run is expected or observed above 30
minutes, caller commissions Debug with source, selection, host/load and timings
before repetition. No extra unchanged repetitions are required after green.

## Performance budget

There is no measured p95/sample for these new tests. CARD-1574 Code collects the
first per-method and phase samples once through the listed rows; CARD-1404 and
CARD-1514 own broader cost derivation/instrumentation. Provisional execution
ceilings: reader method 15 seconds, server method 60 seconds, reader row 4 minutes,
server row 8 minutes, CP-5 4 minutes, excluding separately bounded slot admission.
Set explicit execution limits in the imported manifest rather than accepting the
legacy 15-minute importer floor. Use first comparable results to document about
2x observed p95; one sample remains provisional and never justifies calling it a
measured p95. A five-second live readiness cycle is likewise provisional, bounded
and safe to cancel to Unknown; record the first read/persist durations before
changing it. Never widen existing confirmation or grace settings to get green.

Reader tests use small temporary JSONL files, owned barriers and real snapshot
code; no large sparse file, live Claude, process spawn, or wall-clock sleep for
23 minutes. Server tests use the assembly's shared TestDbFixture initialization
once per run with per-test isolated schemas and the shared template, not repeated
container/migration startup. Recreate only the service graph at the explicit
recovery cuts; that recreation is the subject under test. Inject controlled time
and await asynchronous fixture work rather than fire-and-forget receipt writes.

Per-row phase allocation: reader build/setup up to 90s, body/teardown up to 30s;
server build/shared DB readiness up to 150s, preparation/body/teardown up to 90s.
Record build and slot wait separately from execution; retain per-test durations,
scenario membership, DB readiness/schema clone, native file I/O, HTTP/mapping,
pull, persist, body, graph reconstruction and teardown timing. Mark unavailable
phases unknown and unexplained time remainder, not zero. Generated logs, TRX,
source receipts and timing JSON stay ignored under `.antiphon/c1574-checkpoints/`.

Categories/dependencies: reader tests are Claude runner + shared runner contracts;
server tests are server/session messaging, through the runner contract/client into
runtime ingestion and the queue/launch services. Capture direct ProjectReference
and transitive affected dependencies in Code's run manifest. The existing native
file-identity helpers are reused, not reimplemented. No slow-component extraction
is needed; the new small test class gives the affected component its own selection.
Review checks shared setup, clock/barrier ownership, actual counts and durations,
missing phase evidence, unknown dependency inputs and any >30-minute Debug duty.
These scoped ordinary receipts do not claim full rc qualification (CARD-1545).

## Handoff and dependencies

Next: Code using this plan's Checkpoints as its closed ordinary list. Preserve the
investigation/deployed-source distinction. CARD-1259 remains the owner of the
unattributed transport/persistence latency; CARD-0510 activation remains separate.
Publish the exact Code tip for ordinary Review before landing. A master land alone
does not activate either runner readiness or the server gate; caller must qualify
both running versions on the reviewed rc line before claiming the incident fixed
in production. No setting, runner pin or live queue action is requested here.

## Code checkpoint amendment (2026-10-25)

Reader inspection added a separate red/green witness for two D-3 holes: observed truncation followed by same-inode/same-extent regrowth, and malformed UTF-8 hidden by replacement decoding before JSON validation. CP-1 and the first two CP-2 receipts remain historical two-method selections. Subsequent CP-2 uses the three-method full reader class with the new exact method `C1574_Truncation_regrowth_and_invalid_utf8_stay_unknown`. Each new compiling guard has its own pending PC-27/PC-28; no Mutation is executed by Code. The first three-method CP-2 is intentionally red before the reader repair. The source changes and new assertion justify that changed proof selection; no unchanged green repetition is requested. S2 and Final scope remain incomplete in this budgeted checkpoint.

## Continuation 109f308b: bounded S2a checkpoint slice

The continuation brief overrides the generic Final sweep: one ordinary red and one
narrow green per behavior; no Unit lane or broad integration selection. CP-3a and
CP-4a isolate V-3/R-2 runtime/transport readiness before launch/queue integration.
They compile the shared server test project once per committed group and execute
only the exact V-3 method. Original CP-3/CP-4 remain due for V-4..V-7 once those
methods exist; CP-5 remains due with the queue changes. This split does not claim
those IDs or the whole S2 complete. PCs remain pending for Mutation.

CP-4b reuses the CP-4a build for one exact adjacent recovery-race smoke, because
the SQL fence helper is shared with checked Working recovery. It does not run the
whole class. Both S2a green rows run serially; no provider process is launched.

Continuation ad9a35c6: operator requires exact methods, one red/green per behavior and one build per slice. CP-31..34 replace broad CP-3; CP-41..44 replace broad CP-4. CP-5 shares their build. V-3 is already qualified and is not repeated. PCs remain pending.

Scope follow-up: CP-35 witnesses an ordinary identical-body Now send after the keyed note settled. Expanded V-6 also checks local-command Esc and stranded discovery after 124 minutes. Run CP-33,CP-35 red, then the affected CP-41..45 and CP-5 once after the repair, sharing one build per slice. No broad lane.
