# CARD-1521: require complete Codex boot-pointer consumption

Date: 2026-10-22. Plan task `3791a90d`, assigned branch `feat/card-task-3791a90d`.
Code inspected at `c5da1d2387c08adf86e301a3874a5cbcfd4d6a98`. The inspected delivery files
are also unchanged at fetched master `e6bfcbc2c13f4a7431b1d314e2aef87f30de4ca7`.

The fix is a stricter consumption gate for queued Codex task-brief pointers, followed by
reconciliation of the original typed attempt. A fragment, repeated task marker, redraw or
elapsed settle interval cannot authorize Enter. A retry cannot append another pointer while
the first write may still occupy the composer or input buffer. Complete `UserPrompt` evidence
remains the delivery authority. Verification design is folded into this plan; next is Code.

## Evidence and ground truth

Read the live CARD-1521, CARD-0266 and CARD-0121 descriptions and the CARD-0264 investigation
required by CARD-0266. The Debug report is
`docs/investigations/2026-10-22-task-4e50d241-boot-wedge-debug.md`, read in full from Debug
commit `6b7fb59e3e4a065551230ba49be1c705918bbc19` on `feat/card-task-0f98ac20`.
That document was not yet on fetched master when inspected; its owner lands it separately.
This plan does not copy or change that owner's report.

The retained evidence root is `/state/session-runner/investigations/2026-10-22-0f98ac20`.
It is an investigation artifact location, not a runner-placement instruction. No production
launch, retry or load experiment was performed by this Plan task.

| Card assumption or tempting shortcut | What the inspected code/evidence actually does | Design consequence |
|---|---|---|
| The brief reached the composer before the failure. | The two `TerminalScreen(120, 30)` replays contain only 237/676 and 193/676 characters. No file path, closing marker or `UserPrompt` appeared. | Treat these as incomplete consumption, not proven submission failure or permanent TUI deadlock. |
| `IsVisible` proves body consumption. | `ComposerDeliveryEvidence.IsVisible` accepts any ten-character head **or** tail window; this pointer passes after ten initial characters. `SessionMessageQueueService.WaitForComposerEvidenceAsync` uses it. | Add a separate whole-pointer predicate; retain the generic rendering predicate for its existing callers. |
| Switching to `BodyConsumed` fixes it. | `BodyConsumed` uses the same window matcher on the tail. The final marker repeats at the head; this pointer passes after fourteen characters. Both captured replays pass both predicates. | No head/tail window, marker-presence, marker-count or placeholder shortcut for this gate. |
| Settling establishes completion. | `SettlePostEvidenceAsync` waits for quiet with a three-second absolute bound, then allows CR. It measures redraw settling, not which bytes arrived. | Keep the settle, but only after complete consumption, and recheck the current composer before CR. |
| A `UserPrompt` with a matching prefix is sufficient. | Queue `TranscriptConfirm.Classify` already combines identity with `PromptSubmissionMatch.IsCompleteIn`; incomplete identity is `Truncated`. Null-baseline confirmation has an attempt-time floor. | Reuse those exact complete-receipt and floor rules. Do not introduce a weaker matcher. |
| A failure can safely retry the write. | Pending recovery and interrupted-Sent recovery use `HeadFragmentIsVisibleWhole`; a visible first 40 normalized characters can authorize Enter while the pointer is still partial. If the head is absent, Pending recovery can retype. `SendNowAsync` also retypes an unconfirmed row. | The new gate must cover first delivery, both recovery paths and retry of the same row through Send-now. Changing only the initial wait is insufficient. |
| `NoComposerEvidence` is a harmless return. | `HandleDeliveryFailureAsync` can refund pre-first-turn attempts under CARD-0103, and other failure arms can kill. A refund to zero bypasses checks based only on `DeliveryAttempts > 0`. | Preserve the original typing stamp, generation, floor and spill; route this pointer's incomplete-consumption result to a non-destructive defer before generic refund/kill logic. Recognize legacy stamped rows with zero attempts. |
| CARD-0266 already proves an empty Codex composer. | CARD-0266's `CanceledTypingBlocksTheComposerAsync` and `TypedBodiesBlockComposerAsync` are present. The readable-box/two-empty-snapshot logic is Claude-only; other kinds fall back to a whole head on the screen. | Reuse the hold infrastructure and generation/transcript release rules, but do not treat a missing Codex head as a cleared input buffer. |
| CARD-0121 supplies a measured Codex clear. | Its plan's probe uses `ComposerInputProbe` and measured Claude behavior; D-5 excludes Codex/Grok clear-key adoption. Its proposed `BodyStandsWhole` means whole head **or** tail, not the entire body. Those additions are not in this checkout. | Do not duplicate its probe, placeholder field, migration or Claude canary; neither proposed helper proves this pointer complete. Integrate with its helpers if they land before Code. |
| A new composer parser is required from scratch. | `CodexStartupScreen` already parses prompt glyphs, the model/effort/cwd footer and idle layouts. Its private bottom-composer reader only accepts the one-line idle form. | Factor/reuse those layout primitives for a narrowly scoped wrapped-pointer reader; leave readiness semantics unchanged. |
| NoSubmitOutput proves the TUI stopped reading. | `TryHandleBootWedgeAsync` selects recovery from the verdict and cold-task conjunction. Incident text says “TUI stopped painting”; `AgentTaskDispatcher.BootWedgeFailedReason` says “TUI stopped reading after the brief rendered.” Neither follows from the observations. | Require a current-attempt complete-consumption fact before this pointer can reach boot-wedge recovery, and describe the bounded observations factually. |
| CPU saturation is the root cause. | 37 incident samples average 99.9768% CPU on eight cores; no responsible process or Codex/ConPTY mechanism was established. The buffers continue growing in tiny increments. | CPU is a plausible trigger. Fix evidence correctness independently; do not add a launch-load gate in this card. |

### Captured-input provenance

| Input | Source-file SHA-256 | Recorded size/sequence |
|---|---|---|
| `target-first-buffer.json` | `d91456ad3b7e1ea0c7739c6934a8cca8578add4745ecaa92f83ff18831db8a37` | buffer 12,399 characters; sequence 599 |
| `target-second-buffer.json` | `601768a946435cb9c72f18c7235cf835ab0ba5ff066cf8823d9fd9d8ba40d8ea` | buffer 15,134 characters; sequence 845 |
| `reconstructed-pointer.txt` | `9dfa6fc0379a16aa233a0a8ad0b81bd164055eed40c8551a62d3eadb91bf3b20` | 676 characters, repeated opening/closing task marker |

S1 creates **one intentional regression fixture**,
`.antiphon/fixtures/card-1521/partial-boot-pointers.approved.json`, with the exact pointer and
the two ANSI `buffer` strings plus their source hashes, dimensions and expected consumed-prefix
lengths. Its named consumer is `SessionMessageQueueCodexPointerTests`. Review the extracted
strings for secrets; these captures contain the task pointer/TUI, not credentials. Preserve
their escape bytes, unfinished frames and marker occurrences. Do not substitute stripped text
or a synthetic prefix for the two real captures. The fixture is an approved expected test
input under CARD-1036, individually staged; the original JSON envelopes, bundles, receipts
and logs remain ignored/external. Test loading must locate this fixture from the repository
or explicitly copy that one file into test output, with no absolute evidence-root dependency.

## Decisions

These are implementation decisions resolved from the brief and evidence, not pending product
defaults that require a Decide stage.

**D-1 — narrow scope.** Apply the stricter rule to a persisted Delegation-origin row for a
Codex session whose wire body is an outer task-brief pointer: opening task marker/header plus
the existing `DispatchBriefEvidence.TryReadPointerLocation` recognition, including the compact
pointer form. Use `TypedBodySpill.TryReadOpeningTaskMarker`; do not add a loose substring or
new regex for pointer recognition. Keep this scope on retries regardless of whether the task
is still Dispatched or its baseline is now observable. Keep that pointer as one immutable
delivery run; never batch later notes into its attempted wire body.

Pass this scope explicitly into the shared delivery seam from the persisted row. Do not infer
it from arbitrary text in an untracked Mode:Now request. Named/card-launch adapter submission,
inline non-pointer prompts, generic `VerifiedPromptSubmitter`, Claude/Grok paste handling,
provider/auth configuration and transport encoding are outside this repair. Their existing
contracts remain covered by narrow controls. Rejected: tighten `IsVisible` globally, because
its suffix/placeholder behavior exists for measured long and collapsed Claude input.

**D-2 — whole active-composer evidence.** Add `CodexPointerComposerEvidence` in the Pty library.
Read only the bottom active input region bounded by the Codex prompt glyph and recognized
model/effort/cwd footer, using the existing `CodexStartupScreen` normalization, glyph and
footer primitives. Support wrapped continuation rows shown in the retained 120-column
captures and an 80-column complete control. Unknown, clipped, ambiguous, modal or footerless
frames do not prove completion. Text in conversation history cannot count.

After extracting the region, compare the **entire** normalized body with its **entire**
normalized content, preserving character order and repeated occurrences. Reuse the existing
whitespace/wrapping normalization from `ComposerDeliveryEvidence` through a small whole-body
equality helper. Do not add a Contracts dependency to the Pty library. Extra substantive
composer content also refuses Enter, avoiding submission of a fused body. No ten-character
windows, “both ends visible,” `[Pasted text #N]` arm, or concatenation across successive
screens is sufficient. A pointer whose whole body cannot be observed stays unconfirmed.

Rejected: a new unique suffix/nonce on the wire. A suffix proves the tail reached a display,
not that intervening bytes arrived, and changes formatter/receipt contracts unnecessarily.
Rejected: raw ANSI containment, which can join stale frames or history. `TerminalScreen`
remains the renderer; the new predicate judges its current screen.

**D-3 — separate consumption, submission and receipt.** For this scope, replace the boolean
composer wait with a private result that distinguishes complete composer, complete transcript,
truncated transcript and unconfirmed consumption. During the existing bounded evidence wait,
poll snapshots and use the existing catch-up cadence and transcript helpers against the
original pre-write sequence/time floor. A complete `UserPrompt` wins even when the screen is
partial or unavailable: return transcript-confirmed delivery with **no extra Enter**. An
identified clipped receipt takes the existing truncation handler, never success or retyping.

Only a complete current composer authorizes the existing settle and separate Enter. Recheck
generation, working/receipt state and the complete current composer after settling; if proof
was lost, reconcile/defer instead of sending CR from a latched old snapshot. Missing snapshots
must not turn this scoped check into the existing blind path. A verification toggle is not
authority to bypass pointer integrity. Preserve the existing post-submit confirmation loop
and its bounded Enter-only retries. Any retained screen fallback is explicitly unverified
delivery and is reachable only after the whole pointer was observed; body visibility alone
never stamps Sent as confirmed.

Carry the complete-consumption fact in the private delivery outcome to the cold recovery
decision. `TryHandleBootWedgeAsync` must not infer it from `NoSubmitOutput` for a scoped
pointer. Default/missing evidence is false. After restart, re-observe completeness before any
new Enter; an old verdict alone supplies no consumption authority. Complete-pointer/no-submit
recovery retains its existing cold-task conjunction, limit and generation safeguards.

**D-4 — preserve one typed attempt, without a schema change.** Reuse
`DeliveryVerdict.NoComposerEvidence` for “required complete composer evidence not observed”
and give this scoped result an early non-destructive handler: revert Sent to Pending, clear
the provisional SentAt, retain DeliveryAttempts, LastDeliveryStartedAt, LastDeliveryGeneration,
LastDeliveryBaselineSequence and spill bytes, and publish the ordinary queue change. Do not
refund the original body write, cancel it, consume a boot-wedge relaunch or kill on this
result. Subsequent observation-only holds charge no attempt. Use one existing
`DeliveryVerificationFailed` Warning per row/generation with `pointer-consumption-unconfirmed`
and the release conditions; do not create an incident on every poll.

This avoids a new persisted state/DTO/migration and preserves crash recovery: the pre-write
stamp already survives. For this scope, a retained `LastDeliveryStartedAt` is attempted input
even if an older CARD-0103 refund left attempts at zero. Late-confirm and hold selection must
not skip that shape. Rejected: returning the old generic failure unchanged, because its refund
and kill branches would undo the safety change.

**D-5 — reconciliation before retry or clear.** Reuse the existing session lock, late-confirm,
`DeliveryGenerationChanged`, `EnterOnlyConfirmLockedAsync`, CARD-0266 canceled-row query and
`TypedBodiesBlockComposerAsync`; introduce no second hold store or new release endpoint.
Place the scoped decision ahead of the generic head/tail recovery paths, including any
CARD-0121 `BodyStandsWhole` helper that has landed by implementation time.

| Current evidence for the original typed pointer | Next action |
|---|---|
| Matching complete UserPrompt above its original floor | Confirm the original row; no input. |
| Matching incomplete receipt | Existing truncation handling; no body write or automatic resend. |
| Same accepted generation, complete pointer in the current composer, fresh idle check | Reuse Enter-only recovery; recheck after settling; never retype. |
| Same generation, partial/unknown/unavailable/empty screen, or Working | Hold. Empty screenshots do not prove the already-written FIFO input drained. No CR, probe, clear, body write or attempt charge. |
| Positively changed generation | The old composer is gone; normal first-write path may type once into the new generation, subject to existing admission and late-confirm checks. |

Apply this to Pending retry, interrupted-Sent recovery, held-back/capped rows, and Send-now
of the same pointer. A canceled scoped pointer must also hold later automatic queue/expectation
input, even when its head is no longer visible. Reuse CARD-0266's later-submitted-prompt release
for a canceled row (release only, **not** confirmation of that canceled pointer), and its
generation rule. A screen-only empty result cannot release this particular buffered-write
uncertainty. Keep the existing behavior for other kinds and other bodies.

An operator who needs to discard partial Codex input uses existing Stop then explicit Retry/
fresh generation. A queue Cancel by itself is bookkeeping and cannot clear it. Do not send
Ctrl+U, Ctrl+C, Esc-and-retype or CARD-0121's Claude input probe into this Codex body. Prevent the
reactive overlay recovery in `DeliverAsync` from retyping this already-written pointer without
the same safety proof. Raw human terminal input is outside the automatic delivery contract;
this change does not introduce a new keyboard-control API.

**D-6 — bounded observation and truthful wording.** Keep existing evidence, settle, submit,
re-Enter and delivery-watchdog settings. Do not widen a timeout to make the incident disappear.
Replace claims about internal TUI death with observed facts, for example: “Complete pointer
consumption was not observed within the evidence window; Enter withheld,” and, for an actual
complete-pointer submit failure, “No submission was confirmed within the submit window after
complete pointer consumption.” The exhausted-relaunch reason must not assert the TUI stopped
reading. Preserve historical incidents; only new messages change.

Log bounded milestone metadata at Debug: session/queue/generation identifiers, body length or
digest, write start/return, first matching prefix length, whole-composer observation, each Enter
and complete receipt. Log a prefix-length advance only when it increases; never the pointer,
spill content or raw screen. A transport write return means bytes handed off, not consumed.

**D-7 — CPU is a possible trigger, no launch-time load gate now.** A correct integrity check
must tolerate slow input at any CPU level. A load gate would add admission policy, thresholds,
hysteresis, queueing and starvation behavior without causal evidence. The bounded Windows
measurement below records the timing boundary and concurrent load; it does not establish a
failure rate or recreate production saturation. Consider a separately commissioned load-gate
investigation only if correctly classified launches remain operationally problematic. Measure
launch denominators and write-to-first-character, write-to-whole-body and Enter-to-receipt
latencies against CPU/load, responsible processes, CLI/backend versions and natural low/high
load windows before choosing a threshold. Do not deliberately saturate a shared host.

### Session-input release checklist

This repair can leave an existing cold session holding partial input while its queue defers.
The queue observation wait is bounded by `EvidenceTimeoutSeconds` (15 seconds by default);
that deadline releases the delivery call/lock, **not the session or runner seat**. Later flushes
reconcile the same attempt. Complete receipt resolves delivery; complete composer plus idle
permits Enter-only recovery; a new generation removes the old composer. Canceled-row release
uses the existing later-UserPrompt/generation evidence as described in D-5.

The queue hold has no timed release. `DeliveryFailTimeoutMinutes` (10 minutes by default) is
the separate existing never-started watchdog: its current guards may fail/clean up a genuinely
undelivered task, but that is not a guaranteed seat-release deadline and this card does not
change or bypass those guards. Explicit operator Stop, native process exit and existing
terminal cleanup remain separate lifecycle paths. If work is waiting on an operator or is
Blocked, parking is disabled by default (CARD-1083), so there is **no automatic parking release
deadline**. Do not claim the hold parks a session or releases a Working session.

## Slices

### S1 — executable red witnesses and controls

Add `tests/Antiphon.Tests/Application/SessionMessageQueueCodexPointerTests.cs` and the one
approved fixture above. Reuse `BridgeQueueHarness`, `FakeAgentProtocolAdapter`, their input
list/submitted-bodies/transcript hooks and injected clock. Tag Integration and
`NotInParallel("MessageQueue")`; no process spawn is needed. Add only the minimal fake seam
needed to switch rendered frames after the body write or record input times. Prefer existing
`RenderedScreenFrames`/`RenderedScreenOverride` and input callbacks.

The tests call the real enqueue/flush/recovery APIs, not a test copy of a proposed predicate.
For captures, feed the actual ANSI buffer through the existing `TerminalScreen(120, 30)`;
assert the known 237/193 prefix and missing suffix before supplying that rendered screen to
the queue. The old predicates may be recorded/asserted as characterization, but the regression
oracle is the production queue's CR/body writes, row state, complete receipt and recovery
effects. Thus S1 compiles on the unchanged product and fails on an actual unsafe action.

Use these four exact test methods and separately reported argument rows:

| Method | Argument rows | Count |
|---|---|---:|
| `C1521_Initial_consumption_is_complete` | `capture-first`, `capture-second`, `prefix-10`, `prefix-14`, `missing-last-byte`, `complete-120`, `complete-80`, `partial-then-complete`, `history-only`, `complete-swallowed-enter` | 10 |
| `C1521_Initial_transcript_is_authority` | `complete-current`, `clipped-current`, `complete-stale` | 3 |
| `C1521_Recovery_never_stacks_a_pointer` | `pending`, `interrupted-sent`, `send-now`, `canceled-before-next`, `capped-before-next`, `expectation-direct` | 6 |
| `C1521_Recovery_reconciles_before_input` | `complete-idle`, `working`, `unreadable`, `two-empty-screens`, `new-generation`, `late-complete-receipt`, `legacy-refunded-stamp`, `changed-generation-stale-receipt` | 8 |

Commit and push S1 before CP-1. Keep its intended red outcome as evidence; a build error,
fixture error, zero execution or failure outside the named behavior is not the red witness.

### S2 — scoped consumption and recovery, plus the measurement method

Files:

- `src/Antiphon.Agents.Pty/CodexPointerComposerEvidence.cs` (new): current wrapped composer
  extraction and complete comparison, using `CodexStartupReadiness.cs` primitives. Factor
  only the primitive visibility needed from that file; do not change its ready classifier.
- `src/Antiphon.Agents.Pty/ComposerDeliveryEvidence.cs`: the whole-body normalization/equality
  helper. Keep `IsVisible`, `BodyConsumed`, placeholder identity and CARD-0121 helpers intact.
- `server/Application/Services/SessionMessageQueueService.cs`: D-1 scope propagation; D-3
  composer/receipt result and milestone logs; D-4 non-destructive failure handling; D-5 retry,
  interrupted-Sent, late-confirm, held-back and same-row Send-now decisions; overlay-retype
  refusal; complete-consumption authority for the cold recovery branch.
- `server/Application/Services/SessionMessageQueueService.Expectations.cs`: reuse/extend the
  shared typed-body judgment for canceled scoped pointers, keeping the Claude two-read behavior.
  Canceled-row selection must include a retained typed stamp on the scoped legacy-zero shape.
- `server/Application/Services/AgentTaskDispatcher.cs`: factual exhausted-relaunch reason only;
  no scheduling, timeout or load-admission change.
- `tests/Antiphon.Tests/Application/SessionMessageQueueBootWedgeTests.cs`: update exact wording
  expectations while retaining the whole-body/no-submit and non-Codex recovery assertions.
- `tests/Antiphon.Tests/Agents/CodexBootWedgeProbeTests.cs`: add the single explicit Windows
  measurement method below, reusing its local stub and owned runtime helpers.
- `docs/session-runtime-invariants.md`: document scoped consumption, retained typed attempt,
  hold/release and factual BootWedged interpretation; correct the obsolete implication that
  NoComposerEvidence always means none of the body rendered.

No entity, migration, new setting, global matching change, load gate, or new hold subsystem is
needed. If CARD-0121 lands first, use its shared standing/probe helpers for their existing
scope while inserting this stricter pointer branch before them. Inspect concurrent changes
to these shared delivery files before commissioning Code; do not copy the old Plan branch over
later implementation. Commit/push the completed slice before CP-2/CP-3/CP-4, and freeze source
while any checkpoint is running.

## Verification design

### V-1: consumption before submission

The ten initial-consumption rows exercise the real queue. For partial/missing/history cases,
assert one body write, **zero submitting CRs**, zero SubmittedBodies/complete UserPrompts,
Pending with its original floor/generation/spill and charged original attempt, no BootWedged,
no kill and no relaunch. The two real captures must fail current code on CR or destructive
recovery, even though old `IsVisible` and `BodyConsumed` return true. Prefix-10/prefix-14 and
the missing last byte make the repeated-marker boundary explicit. History-only puts the
entire pointer above an empty current composer and cannot authorize input.

Complete-120 and complete-80 use actual whole pointers fed through `TerminalScreen`, including
mid-token wraps, and must yield exactly one submitted complete pointer. Partial-then-complete
holds at a deterministic barrier after the prefix, proves no CR crossed it, then releases the
remaining bytes inside the existing evidence budget and proves one submission. Do not rely on
wall-clock sleeps or make the fake claim a complete composer before the last byte arrives.
Complete-swallowed-enter proves complete input still reaches the existing bounded Enter-only
submit recovery, and cold boot-wedge handling remains available with factual wording when
submission never occurs. It must never type a second body.

The three transcript rows insert evidence through the harness's real ingestion path after the
original floor. Complete-current resolves with no extra CR even if the screen stays partial;
clipped-current is Truncated and is not confirmed/retyped; complete-stale cannot resolve the
new attempt. Verify completeness using `PromptSubmissionMatch.IsCompleteIn`, not a marker or
the 200-character identity window. Receipt evidence is scoped to session/generation/floor.

### V-2: retries and holds do not stack or submit partial input

Seed the original attempt as already typed in the current generation. Use a ten-character
captured/synthetic prefix for the six retry-route rows so neither a missing whole head nor a
cancel releases the uncertainty. Assert no second body write and no CR, unchanged attempt
floor/generation, no new attempt charge, and no automatic kill/relaunch. Interrupted-Sent must
survive a service reconstruction and a second flush; canceled/capped rows must prevent the
next unrelated automated note, including expectation-direct input.

The eight reconciliation rows prove the boundaries: complete-idle submits only the original
pointer with Enter; Working and unreadable states hold; two empty screenshots still cannot
prove buffered input drained; a confirmed new generation allows one fresh body; late complete
receipt resolves with no input; a zero-attempt legacy row with a typed stamp still holds; and
a previous-generation complete receipt cannot confirm a new attempt. Assert body/Enter counts
as well as database states, not only returned enum values. Exercise same-row Send-now before
it overwrites attempt stamps. Cancellation alone must never make the partial write disappear.

### R-1: small existing regression selection

Run only these five existing methods with the final portable build:

- `SessionMessageQueueBootWedgeTests.Codex_cold_first_delivery_NoSubmitOutput_relaunches_once`
- `SessionMessageQueueBootWedgeTests.a_non_codex_delivery_verified_kind_takes_the_same_recovery`
- `SessionMessageQueueWedgedHeadTests.C0266_Cleared_composer_releases_after_a_canceled_typed_row`
- `ExpectationDirectDeliveryTests.C650_Ghost_empty_composer_on_one_snapshot_keeps_holding_while_the_next_shows_the_body`
- `ExpectationDirectDeliveryTests.C650_Input_uses_normalized_paste_and_separate_enter`

These bound the unchanged generic recovery, CARD-0266/0650 Claude clear judgement and
LF/bracketed-paste/separate-Enter contract. No whole class, namespace or assembly run is part
of this dispatch. Attribute any unexpected existing failure by rerunning that exact failing
selection at the recorded base; record that conditional diagnostic run and its reason.

### V-3: one bounded cold Windows measurement

Add `CodexBootWedgeProbeTests.C1521_Cold_pointer_records_write_consume_enter_and_receipt`.
Reuse `RealCliStubGate`, `RealCliStubEnv.ForCodex`, the local `FakeLlmApiServer`, disposable cwd/
Codex home, owned `SessionRunnerRuntime` and production PtyHost configuration from the existing
probe. Use the current `CodexReadyWait` contract, not the old probe's quiet-only readiness.
Keep Explicit/Integration/RealCliStubProxy classification and its process-spawn limiter.
Never connect a test host to the production runner or use a live provider endpoint.

Launch **once**, not the existing 30-iteration P1. Use a generated repeated-marker pointer
whose harmless local spill file exists, and the production whole-pointer predicate and
settle/Enter order. Record UTC correlation plus monotonic elapsed times for ready, body-write
start/return, first prefix, increasing prefix lengths, whole body, each Enter and complete
UserPrompt. Include actual CLI, backend and source versions; capture concurrent host CPU/load
when available and label missing samples unknown. Preserve bounded raw output/snapshots as
ignored evidence (maximum 256 KiB), with no private provider-home capture.

Budgets: ready 60 s, consume the unchanged configured 15 s, submit the unchanged 30 s, teardown
10 s; outer method timeout 150 s. Assert no Enter before whole consumption and no second body
write, then one complete UserPrompt and a stub request containing the complete pointer.
Tear down the owned process in finally and verify its exit. A readiness/consumption/receipt
timeout or skipped/unavailable lane is **incomplete qualification**, not a passing measurement;
report the precise boundary and commission Debug if it fails. Do not silently retry under a
longer timeout. The queue wiring is proven by V-1/V-2; this row measures native consumption,
not fleet admission or a statistically meaningful wedge rate.

### Execution, lanes and costs

Read `GET /api/runner-defaults` and `GET /api/session-runners` again before commissioning Code.
The Plan read revision 2 on 2026-10-22: the global preference selected an eligible Linux runner,
an eligible Windows lane existed, and another registered lane was draining/unavailable.
These are observations, not placement constants. CP-1..CP-3 use the **portable queue lane**:
omit `-Runner` and `-Platform`. CP-4 uses the **Windows native PtyHost lane**: request
`-Platform windows`, omit `-Runner`. Do not pin a fleet host or embed its address in the plan.
If split across dispatches, CP-4 qualifies the exact committed S2 source, not an independently
edited copy; missing CP-4 remains an explicit verification obligation before Review/land.
CP-4 deliberately builds separately on Windows: CP-2's portable output cannot qualify the
native lane. This is the reason for any equivalent-build advisory from the plan importer.

Use the checkpoint tool, one run per committed group, through the host build-slot gate:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-1521-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-22-card-1521-complete-codex-pointer-plan.md --rows CP-1 --expected-source-sha <S1-sha>
# After S2, same command with --rows CP-2,CP-3 and the exact S2 SHA on the portable lane.
# On Windows, same command with --rows CP-4 and that exact S2 SHA.
```

Await the run; if it returns 75, keep its run id and call `wait --run <id>` until final exit.
Slot timeout 4 is not permission to run unleased. No source edits while a run is in flight.
Keep generated evidence ignored and preserve unedited CHECKPOINT lines, roster, counts and
source/build binding in the stored report. Run `scripts/check-evidence-diff.ps1` over the
Code task base..HEAD. Validate green receipts against the exact tested SHA. Remove only
task-owned alternate build outputs under the documented cleanup rules before settlement.

Ordinary execution floor: **20 minutes** (6 + 6 + 2 + 6), excluding slot waits and authoring;
27 new results red once and green once, five existing guards once, one Windows measurement.
CP-1 may contain both expected failures and passing controls; record the actual per-argument
outcomes. At minimum both captured-screen rows and the Pending retype witness must fail on
the named unsafe action at the base, or stop and repair the witness before product changes.

Pending post-land controls, if commissioned by the normal pipeline: PC-1 substitute the old
`IsVisible` result at the scoped complete-consumption decision and run only
`/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/C1521_Initial_consumption_is_complete`;
PC-2 bypass the scoped recovery decision before the existing Pending head/fall-through branch
and run only
`/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/C1521_Recovery_never_stacks_a_pointer`.
Each must compile, fail its intended CR/body-write assertion, be restored, and pass the same
selection. Budget eight minutes each including builds: PC floor 16 minutes; total ordinary
plus PC floor 36 minutes. These are separate post-land SourceLanding obligations, not extra
ordinary Code runs or permission to mutate this Plan checkout.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1521-red/` | portable-red | `/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/(C1521_Initial_consumption_is_complete)\|(C1521_Initial_transcript_is_authority)\|(C1521_Recovery_never_stacks_a_pointer)\|(C1521_Recovery_reconciles_before_input)` | V-1, V-2 red and controls | 27 executed, 0 skipped; both captures and Pending retry fail on premature CR or duplicate body; report every argument result | 27 | 6 | true | |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1521-green/` | portable-green | `/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/(C1521_Initial_consumption_is_complete)\|(C1521_Initial_transcript_is_authority)\|(C1521_Recovery_never_stacks_a_pointer)\|(C1521_Recovery_reconciles_before_input)` | V-1, V-2 | 27 passed, 0 failed/skipped | 27 | 6 | true | |
| CP-3 | S2 | CP-2 | portable-existing-guards | `/*/Antiphon.Tests.Application/(SessionMessageQueueBootWedgeTests)\|(SessionMessageQueueWedgedHeadTests)\|(ExpectationDirectDeliveryTests)/(Codex_cold_first_delivery_NoSubmitOutput_relaunches_once)\|(a_non_codex_delivery_verified_kind_takes_the_same_recovery)\|(C0266_Cleared_composer_releases_after_a_canceled_typed_row)\|(C650_Ghost_empty_composer_on_one_snapshot_keeps_holding_while_the_next_shows_the_body)\|(C650_Input_uses_normalized_paste_and_separate_enter)` | R-1 | 5 passed, 0 failed/skipped | 5 | 2 | true | |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1521-windows/` | windows-native-measurement | `/*/Antiphon.Tests.Agents/CodexBootWedgeProbeTests/C1521_Cold_pointer_records_write_consume_enter_and_receipt` | V-3 | 1 passed, 0 failed/skipped; timing, complete receipt and owned-process-exit evidence retained | 1 | 6 | true | `ANTIPHON_REAL_CLI_STUB_TESTS=1` |

## Completion and handoff

Plan acceptance is this committed/pushed artifact and its conflict-free merge-tree check;
it makes no claim that implementation tests or the Windows measurement have run. Code finishes
the closed ordinary checkpoints and returns next: review. Review/land and any activation or
post-land Mutation are separate. A master land alone does not activate the product hosts.

--- next stage ---
next: code
handoff: Implement the scoped complete Codex pointer gate and reconciliation-only retry using existing composer holds. Commit the captured-screen red witnesses first, then run CP-1..CP-4 on their named lanes. Preserve receipt authority and report CPU as a possible trigger; add no load gate.
artifact: docs/superpowers/plans/2026-10-22-card-1521-complete-codex-pointer-plan.md
