# CARD-0907: Codex 0.160.0 parser verification addendum

Date: 2026-10-19. Stage: TestDesign. Target: **master only**.
Task: `9d7ac7b5`; source inspected: `f99f8bffb5887d43fafae61ae979ae9e11a36f80`.

This appends verification to the implementation plan at
`08bdb7087f3903fcc7f2980ff520a3a4997c250a`,
`docs/superpowers/plans/2026-10-19-card-0907-codex-0160-parser-plan.md`
on `feat/card-task-046b1e67`. Its D-1–D-6 and S0–S3 remain the fix design.
The checkpoint table here replaces the plan's explicitly provisional allocation.
No product implementation, test execution, build or live capture was performed by
TestDesign. New method names below are implementation obligations, not existing tests.

The source-reference search found three additional consumers of the working
predicate: `SubmitEvidence`, `CodexSubmitConfirmation`, and
`SessionMessageQueueService.WaitForTranscriptConfirmAsync`. The verification
scope therefore adds their narrow tests and one real-queue integration class.
This changes verification selection only. It does not authorize changes to delivery,
retry, receipt or recovery policy. In particular, existing screen-only degraded
success must not be reported as transcript-proven delivery.

The card was read with `scripts/card.ps1 get CARD-0907 -Board Antiphon`
(revision 24, card `f2c2e41a-54c3-4786-8e1e-e804ce450bd1`,
board `8988ca03-7414-47ad-b0b6-51556c701703`).
Investigations read at `c5543c29839e9c9d6d19e012daedb927551a78af` and
`87fad6edb6e8bc80ff27c25419b869f7d84e2aac`.
The brief's approximately 3.5 s cold start and AppHost user-secret
`CodexReadyMaxWaitMs=120000` are supplied operational observations.
The older 51.47 s measurement and source default 60000 ms remain historical/source
facts respectively; no timeout or configuration edit is selected.

## Verification design

### Inspection

Read the following bodies before selecting cases. Paths below the indicated
project directory are literal test scope; a new class uses the nearest fixture
listed alongside it.

| Bodies read | Boundary -> verification or exclusion |
|---|---|
| `src/Antiphon.Agents.Pty/CodexDetectors.cs`: working matcher, turn tracker/done wait and trust methods; `CodexStartupReadiness.cs`: classifier, layout readers, tracker, bounded wait/write | V-2–V-6, R-1/R-2; no replacement wording guessed |
| `tests/Antiphon.Tests/Agents/CodexWorkingIndicatorTests.cs`, `RunnerCodexAdapterTurnCompleteTests.cs`, `ScriptedCodexRunnerClient.cs` (snapshot, input and transcript bodies); production runner verdict/baseline methods | V-2/V-3, R-1; injectable current running/done pair required; fake legacy Working constant alone cannot cover this change |
| `tests/Antiphon.Agents.Pty.Tests/CodexStartupFixtures.cs`, `CodexStartupReadinessTests.cs`, `CodexReadyTrackerTests.cs`, `CodexReadyWaitTests.cs`, `CodexV0156StartupPromptTests.cs` including their clock/cleanup helpers | V-1/V-4–V-6, R-2; reuse completed-read barriers and FakeTimeProvider, not real sleeps to prove settle |
| `Fixtures/CodexStartup/captures.json` P1–P3 and provenance; v0156 trust/signed-out/ready/update; v0158 footer-only-ready/update; v0160-status-panel header/payload | Existing positives/negatives and metadata format; status panel explicitly excluded as quota-refusal evidence |
| `TerminalScreenTests.cs` chunk-boundary helper and CSI/control-string tests; `ClaudeEffortDismissalReplayTests.cs` fixture loading and chunking helpers | Nearest replay pattern for new `CodexV0160FixtureTests.cs`; V-1; no TerminalScreen production changes or broad emulator suite selected |
| `CodexDoneDetectionCanaryTests.cs` complete canary/probe bodies | Prefix-only diagnostic consumer noted; opt-in ModernConPTY real-account run excluded |
| `src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs` normalization, dialect, item, usage, identity and TurnEnd bodies; `tests/Antiphon.SessionRunner.Tests/CodexTranscriptNormalizerTests.cs` including fixture readers | V-7/R-3; comment repair only, preserve existing switch |
| `Fixtures/codex-tui-turn.jsonl`, `codex-exec-turn.jsonl`, `codex-tui-multi-turn.jsonl` parser-relevant records and metadata; bulky instructions/encrypted content excluded from parser-field inspection | Nearest rollout fixtures; historical flat/thread dialects, final/commentary attribution, cumulative usage, error and re-tail assertions |
| `src/Antiphon.Agents.Pty/SubmitEvidence.cs`, `SubmitEvidenceTests.cs`; production `CodexSubmitConfirmation.cs`; `RunnerCodexAdapterSubmitConfirmTests.cs` submit, fallback and baseline cases | V-8/R-4; working signal affects degraded submit as well as done/startup |
| `SessionMessageQueueService.cs` idle flush, pending/attempt commit, interrupted-Sent recovery, late confirmation, confirm loop and transcript queries; dispatcher `FailAsync` failure-outbox body | V-9/R-5; durable attempt/receipt boundaries are not interchangeable |
| `tests/Antiphon.Tests/Application/CodexStartupDeliveryTests.cs` busy/eligible, pending/restart, attempt/body/receipt cuts, insertion-failure/caller-receipt tests and their setup/interceptor helpers; `SessionMessageQueueDeliveryVerificationTests.cs` Codex working/degraded receipt cases | Nearest cases for new `CodexV0160QueueDeliveryTests.cs`; existing names promising “crash” or “recreation” do not by themselves prove an actual new service graph |
| `BridgeQueueHarness.cs` Create/attach, OnSubmitted transcript writer, transcript insert, pending seed, MarkWorking and disposal; `FakeAgentProtocolAdapter.cs` input, composer, snapshot and OnSubmitted bodies | V-9: real queue and database, scripted terminal recipient; isolated schema and fresh provider required for recovery |
| Three project files' fixture/linked-source include entries | Shared startup helper/fixtures already linked to Antiphon.Tests; codex-*.jsonl already copied by SessionRunner.Tests |

**Binding gate (S0, before any S1/S2 parser edit).** Code first commissions the
plan's one foreground server2 Linux PTY capture as uid **1654**, never root.
This TestDesign shell reported uid 1654; that is not a future capture-host
attestation. The operator of the capture records uid, UTC, actual version,
resolved executable SHA-256, argv, backend and 120x30 geometry again.

Use a fresh scratch cwd and scratch child-only CODEX_HOME, owner-only credential
copy by the already sanctioned investigation procedure; do not copy the whole
service home, log credentials, login, wipe history or change parent HOME/CODEX_HOME.
Use the S0 launch flags, gpt-6.1-sol, low reasoning and disabled update checking
for the single model turn. Keep stdin open. Capture from before submit at every
paint/byte-offset, independently of the broken detector. Once positive composer
readiness has settled, write the prompt once using LF/bracketed paste and a
separate Enter. Record the exact body and Enter offsets. Native matching complete
UserPrompt, final answer and task_complete in the scratch rollout are the submit
receipt. A visible draft, output advance, acknowledgement or successful write is
insufficient. No repeat paid turn is authorized if the active frame is missed.

The capture harness owns and awaits its child through exit/cleanup, including
failure: startup <=120 s, submitted-turn observation <=120 s, cleanup <=5 s.
Preserve the original main stream in ignored evidence; stop/reap only that child,
remove its credential copy, and record cleanup. Separate no-model-turn trust,
back-highlight, signed-out and scratch-version.json update scenes follow S0.
Never activate Update now or Enter on the back option. Failure to obtain a current
update modal keeps current-update acceptance unobserved; legacy coverage remains.

Reconstruct via the production TerminalScreen(120,30), never an ANSI-strip view.
Curate only replay-consumed bytes plus the required initial screen state.
The provenance document records raw offset range, initial-state recipe, binary
fingerprint, capture and payload hashes, LF/UTF-8 rules, redactions preserving
positions, and the exact rendered rows. Test metadata must be outside parser
payload (header marker `--- payload ---` or a separate provenance file).
Full streams, native rollouts and command/cleanup receipts remain ignored.

Before S1/S2, append an S0 binding record to this design and
`Fixtures/CodexStartup/v0160-provenance.md`:

| Binding | Required value / refusal |
|---|---|
| B-1 working | Exact observed row token(s), delimiters and position/context, including whether Thinking actually appeared; link offset and hash. Words found only in a binary are not a grammar. |
| B-2 queue | Exact distinct running composer/footer token and context, full frame and row offsets. Construct the queue-only witness and prove all earlier classifier arms false; removing its queue token must yield positive Ready. If this construction is impossible, return **next: plan** with the seam; do not change the readiness contract to manufacture the witness. |
| B-3 receipt/rollout | Native session/turn/item IDs, exact complete prompt/reply, row ordering/timestamps, four usage expectations and actual originator. Preserve parser fields during curation. |
| B-4 modals | Actual current trust accept/back and signed-out files; current update file if obtained. Record absent Press-enter wording and differing October-1 legacy 0.160 labels. Do not infer shape from version alone. |
| B-5 execution | Concrete test methods, literal assertion labels, row expansion counts and exact mutation statement locations. Keep every payload-dependent PC marked **binding required** until this record exists. Reconcile the manifest before affected rows run. |

A recipe can be specified now without pretending its payload exists. S0 is a
mandatory first Code slice with V-1 green before S1/S2. A missing running/receipt
capture returns to Plan/Investigate for that measurement; do not forward a partial
parser fix as complete. Missing current modal scenes remain named acceptance gaps,
never skipped tests counted as green. If a current modal contradicts the legacy
parser, stop that slice, add its exact red witness and bounded repair to Plan.

### Delivery inventory

No new queue, event or outbox is added. Retargeting the shared working predicate
changes these existing asynchronous paths, so they remain in ordinary verification.

| Path / producer -> recipient | Durable identity and persistence boundary | Recovery | Observable receipt / tests |
|---|---|---|---|
| P-1 caller EnqueueAsync -> Codex session through SessionMessageQueueService | Destination session/generation + queue row ID/sequence + immutable body; pending insert, then Sent/attempt/start/baseline committed before input | Busy waits for real OnTurnEnd; already eligible flushes; stranded scan reopens the same pending row after provider recreation | V-9 busy/eligible/recreation methods: one complete matching recipient UserPrompt above the stored baseline (or attempt timestamp floor), same row identity |
| P-2 queue/adapter submit -> Codex native composer/rollout | P-1 identity plus typed body and Enter; receipt later persists separately | After committed attempt before bytes: recover untyped; after body before Enter: Enter-only; after recipient UserPrompt before verdict: late-confirm without typing | V-8 submit consumer and V-9 attempt-cut method; native S0 rollout supplies the independent real-CLI evidence. Screen fallback and successful SubmitAsync are not receipt |
| P-3 durable Dispatched task whose brief insert fails -> original caller via DeliveryFailure outbox and real queue | task ID/dispatch attempt -> failure event -> notification ID -> SourceLandNotificationId on caller queue row -> caller session/baseline | FailNeverStartedAsync persists failure obligation; recreate producer; recovery enqueues note or reconciles already-enqueued/accepted note | V-9 enqueue-failure method, busy and eligible caller variants: complete immutable notification body in caller UserPrompt, original caller/session and notification identity retained |
| P-4 CLI rollout writer -> normalizer -> downstream transcript consumer | Native session/item/turn ID plus stable normalized Uuid/Kind; file append, then transcript persistence | Re-tail from zero deterministically; duplicate response records do not duplicate prompts | V-7 parses the matching captured prompt/reply/TurnEnd. This is serialization coverage, not proof of the live tailer's discovery or DB persistence |

**Real-queue recipe Q.** Add
`tests/Antiphon.Tests/Application/CodexV0160QueueDeliveryTests.cs`, using the
read BridgeQueueHarness/FakeAgentProtocolAdapter seams and an isolated
TestDbFixture schema. Set AgentKind=Codex explicitly. Reuse a real or scaled clock
over real time (never a frozen GetUtcNow with real timers). Every read/assertion
is scoped to that schema, destination session and queue/notification ID.
Use the actual captured frame payloads in post-Enter snapshots; label composer
substitutions derived. Keep the fake terminal's normal body accumulation, paste
decoding and separate Enter behavior; OnSubmitted writes the *received body* to
the destination transcript, never the expected test string. Record submitted-body
count as supporting evidence and assert the full persisted UserPrompt independently.

Use two provider instances for recreation, not two scopes over the same singleton.
Preserve the isolated database between them, attach the same agent/session, and
carry only the terminal/composer state needed by the cut. Release and await every
fault/hold in finally. A fault-injected/seeded durable boundary is a declared
substitute for process death; it proves recovery from that persisted state, not
native crash atomicity or remote PTY survival. No test may claim the latter.

| Case / variant | Recipe and boundary | Oracle and reachability | Classification |
|---|---|---|---|
| Q-1 `C907_busy_queue_delivers_whole_prompt_once` | MarkWorking, enqueue one unique multiline body, explicitly FlushIfIdle while busy, then real OnTurnEnd | Before release: zero writes and zero matching UserPrompt. After: exactly one whole recipient prompt, queue ID/sequence/body unchanged. Earlier blockers absent; running fixture does not stand in for MarkWorking. | Real queue integration; terminal substitute |
| Q-2 `C907_eligible_queue_delivers_whole_prompt_once` | Same body protocol, destination already Running/idle; EnqueueAsync delivers directly | One complete UserPrompt after captured attempt floor and one body write. Also record true transcript receipt; never infer from Sent. | Real queue integration |
| Q-3 `C907_queue_recreation_preserves_pending_identity` | Enqueue with deliverIfIdle=false; dispose graph preserving DB; attach new graph; FlushStrandedQueuesAsync | Same durable row/sequence/body, one complete recipient prompt after flush. Recipient is otherwise eligible so skipped scan cannot hide behind busy. | Service restart substitute |
| Q-4 `C907_committed_attempt_recovery_delivers_once` | Independent cases: attempt committed/no bytes; body accepted/no Enter; complete recipient prompt/verdict commit lost | BeforeInput observer must see persisted attempt/floor in a separate DbContext. Recreate graph at each cut. First cut types once; second only CR; third no input. Every case ends with exactly one matching whole UserPrompt and same queue row. Use cancellation/fault barriers and seeded terminal state, not a claimed real crash. | Persistence-cut integration |
| Q-5 `C907_receipt_requires_complete_current_session_prompt` | Separate fresh harnesses for prefix-only, other-session, old-sequence, and observable working-only transcript cases | No transcript confirmation until a matching whole UserPrompt after that attempt floor; then exactly one receipt. Keep output/composer evidence otherwise positive. Assert prefix identity true and completeness false separately in fixture preconditions. | Receipt negative/positive controls |
| Q-6 `C907_unobservable_working_remains_unverified_until_whole_receipt` | No initial rows; current running frame appears after Enter, including derived still-echoed body case. Use Mode.Now for returned receipt metadata; separate persisted WhenIdle case for recovery | At original deadline: ConfirmedBy=Screen/Degraded, DeliveryUnverified incident, zero matching UserPrompt. Subsequently publish the received complete body through the recipient and catch up; no retype, one receipt. Empty-composer-only fallback is disabled by keeping the body visible during the first phase. | Degraded path; deliberately not delivery proof until final transcript |
| Q-7 `C907_enqueue_failure_returns_whole_failure_note` | Adapt the read CodexStartupDeliveryTests insertion interceptor/dispatcher fixture. Busy and eligible caller crossed with: dispatch committed before brief insert; insert throws; failure outbox committed before enqueue; caller queue insert committed; caller UserPrompt committed before notification confirmation | Recreate graph after each persisted cut, recover through real dispatcher/notification service/queue. Assert the durable failure obligation before recovery, notification-to-queue identity, full caller UserPrompt above its attempt floor and no duplicate body after accepted cut. Seed only the state corresponding to each named cut; never label this OS crash evidence. | Durable failure-handoff integration |

For Q-6 the scripted recipient accepts the body once and retains those received
bytes behind a transcript-publication barrier. The initial screen may contain a
labelled ghost echo of that body to prevent the emptied-composer arm from masking
working recognition. Release the retained recipient record through runtime
catch-up after the degraded deadline; do not inject the expected string or send a
second body to create the positive receipt. This models delayed observation, not
a swallowed Enter that somehow produced a prompt without further input.

If Q-7 needs a service/fault seam that cannot be bound without production changes,
return **next: plan**; do not weaken it to “task Failed” or “note enqueued”.
Its new helper may be local to the test class or a precise extraction of the read
test-only fixture. Every new helper body must be reviewed before Code claims binding.

The direct matcher, replay and scripted runner are substitutes for live provider
behavior. They cannot prove real delivery latency, runner transport, busy native
Codex queuing, live tailer discovery, or Windows ConPTY. Native S0 receipt and
Q's durable receiver transcript are complementary evidence with those limits.
No request, insert, event, Sent/LateConfirmed flag or assistant acknowledgement is
used as the delivery verdict.

### Proves it works now

Every new method is one ordinary TUnit result unless explicitly expanded below.
Variants within a method are independent harness/tracker instances with named
assertion messages; loop rows are not extra executions. Each entry supplies recipe,
variant/case, oracle, reachability and classification.

- **V-1 — Captured fixture fidelity** | offline PTY project |
  `CodexV0160FixtureTests.C907_raw_capture_replays_the_running_frame` and
  `CodexV0160FixtureTests.C907_fixture_payloads_match_recorded_provenance`.
  Replay the curated raw prefix/initial state in whole, recorded-paint and
  incremental UTF-8 chunkings through TerminalScreen; compare the entire frame to
  the independently frozen rendered payload. Hash every curated payload against
  the recorded manifest; assert header exclusion. No working-parser assertion in
  this slice. **Captured**, expected green before product edits; missing fixtures
  or mismatched replay fail S0 and are never the S1/S2 red witness.

- **V-2 — Current and legacy working recognition** | offline Antiphon.Tests |
  `CodexWorkingIndicatorTests.C907_submitted_frame_is_working` asserts
  current-working true directly on B-1.
  `CodexWorkingIndicatorTests.C907_status_words_outside_the_status_row_are_not_working`
  independently supplies idle/Ready, answer prose, draft, help sentence, malformed
  and partial status row, MCP-only spinner, and OSC-title-only raw replay.
  Only the genuine active frame is captured; edits are **derived** and labelled.
  Neither the queue nor a downstream lifecycle can rescue this direct predicate
  witness. Commit/push test first, run CP-2 assertion-red with the old matcher,
  then implement and run CP-2 green.

- **V-3 — Existing lifecycle driven by the new frame** | tracker + runner adapter |
  `CodexWorkingIndicatorTests.C907_captured_lifecycle_requires_seen_gone_and_settle`
  uses independent trackers and explicit DateTime/output marks for never seen,
  still present after quiet, disappeared at settle-minus-one/full settle, and a
  repaint immediately before the old deadline. Assert each intermediate false and
  final true; do not stop at IndicatorSeen.
  `RunnerCodexAdapterTurnCompleteTests.C907_captured_lifecycle_completes_screen_fallback`
  injects running/done payloads into ScriptedCodexRunnerClient with transcript
  unavailable and visible raw output; asserts TurnCompleted true and a nonempty
  snapshot. Separately retain transcript-first/baseline and never-working controls.
  **Captured inputs with deterministic/simulated time**, not native delivery proof.

- **V-4 — Independent queue recognition** | offline PTY project |
  `CodexV0160StartupTests.C907_running_queue_footer_blocks_ready` uses B-2's
  **derived** queue-only frame. Assert Working=false, no modal/MCP/queued-follow-up,
  and a positive-ready frame when only the queue token is removed. Assert
  IsReady=false **and Reason=QueueMode** on the target. CP-4 is committed/pushed
  assertion-red before the new queue matcher and green after.
  `CodexV0160StartupTests.C907_queue_words_outside_footer_do_not_block_ready`
  puts the exact token in answer/help prose above an unchanged ready composer;
  Ready remains true. No invented queue token or legacy literal substituted for B-2.

- **V-5 — Startup ordering and positive readiness** | offline PTY project |
  `CodexV0160StartupTests.C907_running_frame_blocks_startup` asserts Working
  on a captured active frame and the labelled both-active variant.
  `CodexV0160StartupTests.C907_blocker_precedence_is_preserved` uses independent
  trust/sign-in/sandbox/update/MCP-incomplete/MCP-boot-plus-running derived cases and exact Reason values.
  `CodexV0160StartupTests.C907_idle_frame_requires_full_settle_after_working`
  covers ready -> working -> ready, 999/1000ms, zero-settle one-observation false,
  second observation true, and a held second snapshot.
  `CodexV0160StartupTests.C907_footer_only_ready_needs_complete_current_layout`
  asserts captured idle Ready (warning chip allowed), splash-without-footer Unknown,
  occupied composer Unknown, context/clipped footer Unknown, and invalid
  model/effort/path Unknown. For the generic/footer-only guard PCs isolate each
  with the other guard satisfied. **Captured + labelled derived**.
  No time budget change is permitted.

- **V-6 — Current modals and legacy compatibility** | offline PTY project |
  `CodexV0160StartupTests.C907_trust_accept_requires_current_accept_highlight`
  covers accept, Back highlighted, stale accept after one CR, pre-modal candidate,
  and post-action full settle; zero CR on Back, exactly one on accept.
  `CodexV0160StartupTests.C907_raw_history_cannot_authorize_modal_input`
  uses idle current frame plus trust/update only in raw history; zero writes.
  `CodexV0160StartupTests.C907_signed_out_chooser_is_sign_in` requires SignIn
  and zero Enter; a **derived** legacy continue-footer variant tests precedence
  without falsely claiming the current CLI emits it.
  `CodexV0160StartupTests.C907_update_picker_is_blocking_but_static_notice_is_not`
  checks current picker when captured, mandatory legacy picker/static notice, and
  independent title-only/skip-only derived controls.
  `CodexV0160StartupTests.C907_update_escape_requires_a_fresh_settled_frame`
  checks exactly one ESC, stale picker, a held post-ESC read, new ready at
  settle-minus-one/full settle; never Enter.
  `CodexV0160StartupTests.C907_modal_actions_share_the_original_deadline`
  uses FakeTimeProvider and read/write barriers for trust and update at 600ms,
  1000ms settle and 1500ms total; result false at original deadline.
  Current-update variants are **binding required** if unobserved; mandatory
  legacy variants execute without a skipped result. Report both separately.

- **V-7 — Current native rollout and originator-independent dialect** | offline
  SessionRunner project |
  `CodexTranscriptNormalizerTests.C907_v0160_rollout_preserves_prompt_reply_turn_identity_and_usage`
  reads B-3 from `Fixtures/codex-v0160-tui-turn.jsonl`, asserts exactly one
  complete UserPrompt, exact final reply, one end_turn, explicit native turn ID
  on final/TurnEnd, measured Input/Output/CacheRead/CacheCreation values, and
  deterministic re-tail tuples. A labelled derived duplicate response_item and
  token_usage_record inserted after token_count change none of those values.
  Pin expectations in the test/provenance; do not calculate expected results by
  calling the normalizer again.
  `CodexTranscriptNormalizerTests.C907_thread_item_dialect_is_not_selected_by_originator`
  changes only session_meta originator to codex_exec; rows stay thread items and
  results are identical. **Captured TUI + derived exec-originator control**;
  not a claim that the lost October-18 exec fixture was recovered.

- **V-8 — Shared submit consumers** | offline, both projects |
  `SubmitEvidenceTests.C907_current_working_is_positive_without_an_emptied_composer`
  feeds captured running, with a labelled still-visible-body variant so the
  emptied-composer OR arm cannot mask working failure; current status prose/draft
  negative keeps the body visible too.
  `RunnerCodexAdapterSubmitConfirmTests.C907_current_working_degrades_without_retyping`
  drives CodexSubmitConfirmation with unavailable transcript and the captured
  active frame, plus independent body-still-visible/no-working control. Assert
  one body write; the current working path returns degraded without the held-body
  exception, and the negative throws ComposerMayHoldBody. Test both first/second
  look using completed-clock barriers. Return success is **not** a delivery receipt.

- **V-9 — Complete recipient evidence and persisted recovery** | real queue +
  isolated database | all seven exact `CodexV0160QueueDeliveryTests.C907_*`
  methods in Q-1–Q-7. Recipes, variants, decisive assertions, reachability and
  substitution limits are in the delivery inventory. Final positives require a
  matching complete receiver UserPrompt. Each independent negative is followed
  by a real successful recipient submit, so a disconnected observer cannot make
  a refusal-only test pass.

  Exact V-9 method roster:
  `CodexV0160QueueDeliveryTests.C907_busy_queue_delivers_whole_prompt_once`,
  `CodexV0160QueueDeliveryTests.C907_eligible_queue_delivers_whole_prompt_once`,
  `CodexV0160QueueDeliveryTests.C907_queue_recreation_preserves_pending_identity`,
  `CodexV0160QueueDeliveryTests.C907_committed_attempt_recovery_delivers_once`,
  `CodexV0160QueueDeliveryTests.C907_receipt_requires_complete_current_session_prompt`,
  `CodexV0160QueueDeliveryTests.C907_unobservable_working_remains_unverified_until_whole_receipt`,
  `CodexV0160QueueDeliveryTests.C907_enqueue_failure_returns_whole_failure_note`.

### Guards the regression

- **R-1 — Legacy working, lifecycle and transcript authority.**
  Run all CodexWorkingIndicatorTests and RunnerCodexAdapterTurnCompleteTests.
  Existing source census: 7+7 methods/results; add 3+1 = **18 results**.
  Decisive assertions include empty raw false, never-working false, old TurnEnd
  false, preserved baseline after fetch miss, and transcript reply text preferred.
  The direct current witness is intentionally also repeated by the class selection.

- **R-2 — Legacy readiness/modal boundaries.**
  CodexStartupReadinessTests has 23 methods/**31 results**;
  CodexReadyTrackerTests 4/4; CodexReadyWaitTests 18/**19**;
  CodexV0156StartupPromptTests 8/8. Keep existing literal/variant assertions,
  including bare continue with no Escape, sign-in-before-update, Back/Quit
  no-Enter, fresh snapshots, cancellation/exit, and original deadlines.
  CodexV0160StartupTests adds **12** single-result methods. The unchanged
  cancellation, exited-process and diagnostic plumbing receives regression runs;
  this plan makes no new lifecycle/kill-policy claim about those branches.

- **R-3 — Normalizer compatibility.**
  All 17 existing CodexTranscriptNormalizerTests plus 2 new = **19 results**.
  Preserve actual flat exec fixture, independent dialect latches, response-item
  exclusion, strict activity/TurnEnd timestamps, final/commentary identity,
  aborted/cancelled boundary, error fields, cumulative usage and stable re-tail.

- **R-4 — Submit compatibility.**
  RunnerCodexAdapterSubmitConfirmTests: 8 existing plus 1 new = **9 results**;
  SubmitEvidenceTests: 8 existing plus 1 new = **9 results**.
  Preserve Enter-only retry, first-confirm no extra Enter, later-baseline floor,
  held-body exception and kind-specific predicates. Tests that return degraded
  retain that classification.

- **R-5 — Delivery invariants at the changed predicate's consumers.**
  Q-1–Q-7 are the closed integration scope; no whole MessageQueue class/suite.
  Receipt completeness/session/floor, same durable row, recovery without duplicate
  input, and caller failure obligation assertions are mandatory. Existing broader
  queue/dispatcher policy is not expanded or redesigned.

### Guard inventory

The inventory below is the safety-critical set relied on by this parser change,
including preserved guards in its end-to-end witnesses. Independent predicates
have distinct controls even when they use the same test method. Unchanged
cancellation/process-release policy and unrelated provider/queue features are
excluded explicitly below, rather than described as verified by this change.

| Guard | Plan reference + invariant | Positive control |
|---|---|---|
| G-1 | S0: Fixture metadata never reaches parsers. | PC-1 |
| G-2 | S0: Fixture payload bytes match independently recorded provenance. | PC-2 |
| G-3 | S0: Curated raw paints reproduce the recorded current frame at 120x30. | PC-3 |
| G-4 | D-2/S1: Measured current working row is recognized. | PC-4 |
| G-5 | D-2/S1: State words outside the measured status context cannot declare Working. | PC-5 |
| G-6 | D-4/S1: Legacy working recognition survives. | PC-6 |
| G-7 | D-5/S1: A never-working screen cannot complete a turn. | PC-7 |
| G-8 | D-5/S1: A working indicator must disappear before screen completion. | PC-8 |
| G-9 | D-5/S1: Completion waits the entire quiet interval. | PC-9 |
| G-10 | D-5/S1: A changed output mark restarts quiet time. | PC-10 |
| G-11 | D-5/S1: Empty/ANSI-only raw output cannot certify screen completion. | PC-11 |
| G-12 | D-5/S1: A transcript verdict and reply outrank the screen. | PC-12 |
| G-13 | D-5/S1: Old TurnEnd cannot complete the new prompt. | PC-13 |
| G-14 | D-5/S1: A missed subsequent baseline fetch preserves the last known floor. | PC-14 |
| G-15 | D-3/S2: The measured queue footer independently blocks startup. | PC-15 |
| G-16 | D-3/S2: Queue wording outside the active footer cannot block an idle composer. | PC-16 |
| G-17 | D-4/S2: Legacy queue matching survives. | PC-17 |
| G-18 | D-3/S2: Trust independently blocks readiness ahead of working/queue. | PC-18 |
| G-19 | D-3/S2: SignIn independently blocks readiness ahead of working/queue. | PC-19 |
| G-20 | D-3/S2: Sandbox independently blocks readiness ahead of working/queue. | PC-20 |
| G-21 | D-3/S2: BlockingUpdate independently blocks readiness ahead of working/queue. | PC-21 |
| G-22 | D-3/S2: McpIncomplete independently blocks readiness ahead of working/queue. | PC-22 |
| G-23 | D-3/S2: McpBoot independently blocks readiness ahead of working/queue. | PC-23 |
| G-24 | D-3/S2: Working takes precedence over queue when both are active. | PC-24 |
| G-25 | D-6/S2: Positive ready requires the Codex banner. | PC-25 |
| G-26 | D-6/S2: An explicitly empty model row is not repaired by the footer fallback. | PC-26 |
| G-27 | D-6/S2: An explicit loading model cannot become ready. | PC-27 |
| G-28 | D-6/S2: A historical composer above an unknown bottom is not the current composer. | PC-28 |
| G-29 | D-6/S2: Readiness requires idle composer content. | PC-29 |
| G-30 | D-6/S2: The generic footer gate rejects context-only or clipped footer text. | PC-30 |
| G-31 | D-6/S2: Footer-only readiness requires exactly a model token and an effort token. | PC-31 |
| G-32 | D-6/S2: Footer-only readiness requires a recognized GPT model prefix. | PC-32 |
| G-33 | D-6/S2: Footer-only readiness requires a supported effort. | PC-33 |
| G-34 | D-6/S2: Footer-only readiness requires a recognizable cwd path. | PC-34 |
| G-35 | D-6/S2: Positive readiness must settle for the full interval. | PC-35 |
| G-36 | D-6/S2: A blocker resets the positive-ready candidate. | PC-36 |
| G-37 | D-6/S2: A changed relevant ready region restarts settling. | PC-37 |
| G-38 | D-6/S2: Readiness needs a second completed current snapshot. | PC-38 |
| G-39 | D-4/S2: Raw history cannot authorize a modal keystroke. | PC-39 |
| G-40 | D-4/S2: Trust Enter requires the accept highlight. | PC-40 |
| G-41 | D-4/S2: Trust is accepted at most once per wait. | PC-41 |
| G-42 | D-4/S2: Sign-in wins over a shared continue footer. | PC-42 |
| G-43 | D-4/S2: Update picker requires its title independently. | PC-43 |
| G-44 | D-4/S2: Update picker requires its skip-version option independently. | PC-44 |
| G-45 | D-4/S2: Legacy bare continue remains a blocker. | PC-45 |
| G-46 | D-4/S2: A generic continue blocker cannot authorize Escape. | PC-46 |
| G-47 | D-4/S2: Update Escape is sent at most once. | PC-47 |
| G-48 | D-4/S2: A trust action discards an earlier ready candidate. | PC-48 |
| G-49 | D-4/S2: An update action cannot itself return ready without a new frame. | PC-49 |
| G-50 | D-4/D-6/S2: Modal handling never extends the original total deadline. | PC-50 |
| G-51 | D-5/S3: Thread-item user and assistant rows are accepted independent of originator. | PC-51 |
| G-52 | D-5/S3: User dialect latch suppresses duplicate content. | PC-52 |
| G-53 | D-5/S3: Assistant dialect latch suppresses duplicate content independently. | PC-53 |
| G-54 | D-5/S3: response_item duplicate prompt is never emitted as a UserPrompt. | PC-54 |
| G-55 | D-5/S3: Final answer shares its native turn identity with TurnEnd. | PC-55 |
| G-56 | D-5/S3: Commentary cannot borrow final-answer ownership. | PC-56 |
| G-57 | D-5/S3: task_complete synthesizes the exact end_turn boundary. | PC-57 |
| G-58 | D-5/S3: New token_usage_record does not replace or double count token_count usage. | PC-58 |
| G-59 | D-5/S3: Usage uses session cumulative deltas across turns. | PC-59 |
| G-60 | D-5/S3: Cached input is subtracted from billed input. | PC-60 |
| G-61 | D-5/S3: Re-tail produces stable dedup identities. | PC-61 |
| G-62 | S1 delivery consumer: Busy recipient retains its pending body until eligible. | PC-62 |
| G-63 | S1 delivery consumer: The attempted queue row is durable before input. | PC-63 |
| G-64 | S1 delivery consumer: Recovery of a still-held composer sends Enter only. | PC-64 |
| G-65 | S1 delivery consumer: A persisted matching receipt prevents replay after the verdict-write cut. | PC-65 |
| G-66 | S1 delivery consumer: Receipt requires the complete body, not just marker or prefix. | PC-66 |
| G-67 | S1 delivery consumer: Receipt belongs to the destination session. | PC-67 |
| G-68 | S1 delivery consumer: Receipt lies above this attempt's sequence floor. | PC-68 |
| G-69 | S1 delivery consumer: Screen-only success must stay distinguishable from transcript receipt. | PC-69 |
| G-70 | S1 delivery consumer: Committed pending rows survive service recreation under the same identity. | PC-70 |
| G-71 | S1 failure handoff: A missing brief insertion leaves a durable caller failure obligation. | PC-71 |
| G-72 | D-4/S2: Legacy trust wording remains recognized and accepted. | PC-72 |
| G-73 | S1 submit consumer: Submit confirmation types the body once, even on the degraded working path. | PC-73 |

### Positive controls

**All 73 controls must run**, including every independent case/variant within
their detecting methods. None is waived. Their execution belongs to **post-land
SourceLanding Mutation**, not TestDesign or Code. Current execution status:
**0/73 run, 73/73 pending**. Code runs all ordinary V/R; ordinary Review
judges the tests, bindings and pending controls before land. A new test that is
incapable of detecting its claimed defect is a stub.

Recipe M for every row: bind exact target statement and assertion at the S0/Code
tip; run the exact method green on landed L; introduce only the listed compiling
defect; rebuild and run the same exact method red; restore original bytes,
refresh timestamps/rebuild, and run the same method green. Keep separate baseline,
red and restored-green source/build receipts, nonzero execution counts and first
failing assertion. No build, fixture-loading, missing-test or timeout failure
counts as the intended red. Default expected execution count is 1; all named
detecting methods below are non-parameterized.

Each table row supplies the **recipe** M plus its listed defect, **variant/case**
(the exact method and assertion case), **oracle** (expected-red cell),
**reachability** (the distinguishing setup in that cell and V/Q recipe), and
**classification**: pending method-scoped mutation. Current status for all rows is
`reachability=unproven`; static design is not execution. New frame/rollout
payloads and exact new-arm statement locations remain **binding required**.
Code must retain that status if it cannot bind them; a generic “parser reverted”
mutation cannot stand in for the isolated working/queue controls.

Keep fixture integrity assertions in V-1. Later behavioral methods must not
fail first on a hash or helper assertion when the intended mutation only changes
parser code. Put independent state-machine cases in the stated order with stable
labels; if a mutant reaches an earlier assertion, report the actual finding and
repair/bind the design before declaring that PC clean. Do not mutate a test's
expected value to manufacture red. PC-1/PC-2/PC-3 deliberately test the fixture
loader/replay-input boundary, not a production matching branch.

Lanes: **P** = Antiphon.Agents.Pty.Tests; **T** = Antiphon.Tests offline tests;
**N** = Antiphon.SessionRunner.Tests; **Q** = Antiphon.Tests real-queue integration.
All are exact-method filters, never whole-class Mutation runs. No independent
batching is assumed; controls sharing source files/methods run serially.
SourceLanding never commits/pushes or changes its managed branch; retain evidence
and restoration records in its assigned external root. Only local inherited
children are allowed.

| PC / guard | Compiling defect (one control at a time) | Exact detecting filter | Expected assertion-red / reachability | Lane |
|---|---|---|---|
| PC-1 / G-1 | Return the complete header-bearing file from the new payload accessor. | `/*/*/CodexV0160FixtureTests/C907_fixture_payloads_match_recorded_provenance` | payload-header-excluded: loaded payload contains no metadata/header and equals the recorded UTF-8 payload hash. | P |
| PC-2 / G-2 | In the new payload accessor change one recorded printable model/status character after removing metadata. | `/*/*/CodexV0160FixtureTests/C907_fixture_payloads_match_recorded_provenance` | payload-hash: independent SHA-256 of the loaded payload equals its frozen manifest hash; header-exclusion precondition still passes. | P |
| PC-3 / G-3 | In the replay helper, omit the final recorded paint segment while retaining earlier bytes. | `/*/*/CodexV0160FixtureTests/C907_raw_capture_replays_the_running_frame` | running-frame-replay: GetScreenText() equals the independently recorded running frame in every chunking. | P |
| PC-4 / G-4 | Return false from only the additional current-row arm, retaining the legacy arm. | `/*/*/CodexWorkingIndicatorTests/C907_submitted_frame_is_working` | current-working: IsVisible(capturedRunning) is true. | T |
| PC-5 / G-5 | Replace only the new contextual predicate with a case-sensitive Contains of the observed active state token. | `/*/*/CodexWorkingIndicatorTests/C907_status_words_outside_the_status_row_are_not_working` | status-context-required: false for prose, draft, help, OSC-title-only replay and malformed/partial row cases. | T |
| PC-6 / G-6 | Disable the legacy Prefix/Suffix matching arm while retaining the new arm. | `/*/*/CodexWorkingIndicatorTests/The_measured_working_line_is_visible` | IsVisible on the measured 0.147 Working line is true. | T |
| PC-7 / G-7 | Initialize _indicatorSeen to true in CodexTurnScreenTracker. | `/*/*/CodexWorkingIndicatorTests/C907_captured_lifecycle_requires_seen_gone_and_settle` | never-seen-not-done: idle at t0 and t0+60s is false with nonempty raw and a stable mark. | T |
| PC-8 / G-8 | Remove only && _indicatorGone from the final Observe return. | `/*/*/CodexWorkingIndicatorTests/C907_captured_lifecycle_requires_seen_gone_and_settle` | still-working-not-done: captured running frame after the full quiet interval remains false. | T |
| PC-9 / G-9 | Replace the elapsed >= quietPeriod term with true. | `/*/*/CodexWorkingIndicatorTests/C907_captured_lifecycle_requires_seen_gone_and_settle` | done-quiet-minus-one: done at quietPeriod minus one millisecond is false. | T |
| PC-10 / G-10 | Delete _lastChangeUtc = nowUtc only inside the outputMark-change branch. | `/*/*/CodexWorkingIndicatorTests/C907_captured_lifecycle_requires_seen_gone_and_settle` | repaint-restarts-quiet: a repaint near the old deadline does not complete at that deadline. | T |
| PC-11 / G-11 | Remove only VisiblePtyOutput.HasVisibleOutput(rawOutput) from Observe's result. | `/*/*/CodexWorkingIndicatorTests/An_empty_snapshot_never_completes_even_with_the_full_lifecycle` | The second Observe with completed lifecycle and empty raw output is false. | T |
| PC-12 / G-12 | Suppress the TryBuildTranscriptVerdictAsync return in WaitForTurnCompleteAsync, leaving the screen loop. | `/*/*/RunnerCodexAdapterTurnCompleteTests/A_TurnEnd_row_past_the_prompt_baseline_completes_the_turn_with_transcript_reply_text` | turn.TurnCompleted is true with a never-working screen; failure must be the assertion, not a test timeout. | T |
| PC-13 / G-13 | Replace e.Sequence > _transcriptBaselineSequence with true in TryBuildTranscriptVerdictAsync. | `/*/*/RunnerCodexAdapterTurnCompleteTests/A_TurnEnd_at_or_below_the_baseline_does_not_complete_the_new_turn` | turn.TurnCompleted is false. | T |
| PC-14 / G-14 | In CaptureTranscriptBaseline replace Resolve(fetched, _lastKnownTranscriptSequence) with fetched ?? 0. | `/*/*/RunnerCodexAdapterTurnCompleteTests/A_transient_fetch_failure_on_a_later_turn_preserves_the_baseline_instead_of_replaying_the_previous_reply` | stale.TurnCompleted is false on the second turn before its new rows arrive. | T |
| PC-15 / G-15 | Disable only the added current queue-footer arm, retaining legacy matching. | `/*/*/CodexV0160StartupTests/C907_running_queue_footer_blocks_ready` | queue-only-reason: derived frame is NotReady with Reason == QueueMode; baseline assertion-red must be this same assertion. | P |
| PC-16 / G-16 | Replace the added footer-context predicate with whole-screen Contains of the captured queue token. | `/*/*/CodexV0160StartupTests/C907_queue_words_outside_footer_do_not_block_ready` | quoted-queue-is-ready: ready layout quoting the token above the composer remains Ready. | P |
| PC-17 / G-17 | Disable only the legacy tab-to-queue predicate. | `/*/*/CodexStartupReadinessTests/Queue_mode_footer_blocks_a_loaded_model` | Reason is QueueMode; Unknown is red even if IsReady remains false. | P |
| PC-18 / G-18 | Disable only the CodexTrustPromptDetector.IsVisibleOnCurrentScreen(text) classifier branch; retain all other branches. | `/*/*/CodexV0160StartupTests/C907_blocker_precedence_is_preserved` | trust-before-working: derived frame with this one blocker plus active working/queue has exact Reason Trust; no other earlier blocker is present. | P |
| PC-19 / G-19 | Disable only the ContainsSignIn(text) classifier branch; retain all other branches. | `/*/*/CodexV0160StartupTests/C907_blocker_precedence_is_preserved` | signin-before-working: derived frame with this one blocker plus active working/queue has exact Reason SignIn; no other earlier blocker is present. | P |
| PC-20 / G-20 | Disable only the ContainsSandboxOrInputDisabled(text) classifier branch; retain all other branches. | `/*/*/CodexV0160StartupTests/C907_blocker_precedence_is_preserved` | sandbox-before-working: derived frame with this one blocker plus active working/queue has exact Reason Sandbox; no other earlier blocker is present. | P |
| PC-21 / G-21 | Disable only the ContainsBlockingUpdate(text) classifier branch; retain all other branches. | `/*/*/CodexV0160StartupTests/C907_blocker_precedence_is_preserved` | update-before-working: derived frame with this one blocker plus active working/queue has exact Reason BlockingUpdate; no other earlier blocker is present. | P |
| PC-22 / G-22 | Disable only the text.Contains("MCP startup incomplete", StringComparison.OrdinalIgnoreCase) classifier branch; retain all other branches. | `/*/*/CodexV0160StartupTests/C907_blocker_precedence_is_preserved` | mcp-incomplete-before-working: derived frame with this one blocker plus active working/queue has exact Reason McpIncomplete; no other earlier blocker is present. | P |
| PC-23 / G-23 | Disable only the mcpVisible classifier branch; retain all other branches. | `/*/*/CodexV0160StartupTests/C907_blocker_precedence_is_preserved` | mcp-before-working: derived frame with this one blocker plus active working/queue has exact Reason McpBoot; no other earlier blocker is present. | P |
| PC-24 / G-24 | Move the queue check immediately before the working check. | `/*/*/CodexV0160StartupTests/C907_running_frame_blocks_startup` | working-before-queue: both active signals yield Reason Working. | P |
| PC-25 / G-25 | Disable only the !HasCodexBanner early return. | `/*/*/CodexStartupReadinessTests/Missing_codex_banner_is_unknown` | IsReady is false for P3 with the banner removed. | P |
| PC-26 / G-26 | Disable the !hasModelRow && modelRowSeen early return. | `/*/*/CodexStartupReadinessTests/Empty_model_row_is_unknown` | IsReady is false for P3 with only the model value erased. | P |
| PC-27 / G-27 | Disable only the loading early return. | `/*/*/CodexStartupReadinessTests/Loading_model_never_borrows_the_footer_model` | IsReady is false for P3 whose model is loading. | P |
| PC-28 / G-28 | In TryReadBottomComposer choose the first parseable composer and its following nonblank footer instead of the bottom pair. | `/*/*/CodexStartupReadinessTests/Historical_composer_above_an_unknown_bottom_is_not_ready` | IsReady is false for historical P3 with an unknown bottom; the old ready pair stays intact to make the bypass reachable. | P |
| PC-29 / G-29 | Make IsIdleComposer return true for any parsed content. | `/*/*/CodexV0160StartupTests/C907_footer_only_ready_needs_complete_current_layout` | occupied-composer-not-ready: replace only the current idle hint by a nonempty draft. | P |
| PC-30 / G-30 | Make IsModelEffortCwdFooter return true for any nonempty footer. | `/*/*/CodexV0160StartupTests/C907_footer_only_ready_needs_complete_current_layout` | generic-footer-required: a derived frame with a valid explicit model row and only a context-left footer is not ready; this isolates the generic gate from footer-only model parsing. | P |
| PC-31 / G-31 | Remove only parts.Length != 2 from TryReadFooterOnlyModel's initial rejection. | `/*/*/CodexV0160StartupTests/C907_footer_only_ready_needs_complete_current_layout` | footer-token-count-required: derived GPT-test low extra + valid cwd stays Unknown; all later guards are satisfied. | P |
| PC-32 / G-32 | Remove only !parts[0].StartsWith("GPT-", StringComparison.OrdinalIgnoreCase) from TryReadFooterOnlyModel's initial rejection. | `/*/*/CodexV0160StartupTests/C907_footer_only_ready_needs_complete_current_layout` | footer-model-required: derived Other-model low + valid cwd stays Unknown; generic footer/token count/effort/path checks pass. | P |
| PC-33 / G-33 | Disable only the supported-effort early return in TryReadFooterOnlyModel. | `/*/*/CodexV0160StartupTests/C907_footer_only_ready_needs_complete_current_layout` | footer-effort-required: derived GPT-test invented + valid cwd stays Unknown; prefix/count/path guards pass. | P |
| PC-34 / G-34 | Disable only the cwd path-shape early return in TryReadFooterOnlyModel. | `/*/*/CodexV0160StartupTests/C907_footer_only_ready_needs_complete_current_layout` | footer-cwd-required: derived GPT-test low + non-path cwd stays Unknown; count/prefix/effort/generic footer guards pass. | P |
| PC-35 / G-35 | Return true instead of comparing elapsed minus candidateSince with _settle on the final tracker return. | `/*/*/CodexReadyTrackerTests/Ready_requires_the_full_settle_interval` | The second observation at 999ms is false. | P |
| PC-36 / G-36 | In the !IsReadyCandidate branch return false without clearing candidate, region or count. | `/*/*/CodexV0160StartupTests/C907_idle_frame_requires_full_settle_after_working` | working-restarts-ready: ready at 0, working at 999, ready at 1000 remains false. | P |
| PC-37 / G-37 | Disable only the changed NormalizedRegion branch. | `/*/*/CodexReadyTrackerTests/Relevant_layout_changes_restart_settling` | R-20: false after the hint change at 1998ms (model-only change at 999ms still precedes the original deadline). | P |
| PC-38 / G-38 | In the candidateSince-is-null branch return _settle <= TimeSpan.Zero instead of false. | `/*/*/CodexV0160StartupTests/C907_idle_frame_requires_full_settle_after_working` | one-sample-zero-settle-not-ready: even Settle=0 requires a second positive observation. | P |
| PC-39 / G-39 | In CodexReadyWait assign screen from snapshot.RawOutput instead of RenderedScreen. | `/*/*/CodexV0160StartupTests/C907_raw_history_cannot_authorize_modal_input` | history-trust-no-enter: current idle with historical trust raw bytes produces zero writes. | P |
| PC-40 / G-40 | Return true on every MatchesV0156 trust screen in IsAcceptSelectedOnCurrentScreen. | `/*/*/CodexV0160StartupTests/C907_trust_accept_requires_current_accept_highlight` | back-selection-not-accepted: current Back to Agent Command Center selection is false; no Enter is sent. | P |
| PC-41 / G-41 | Remove only !acceptedTrust from the trust-action condition. | `/*/*/CodexV0160StartupTests/C907_trust_accept_requires_current_accept_highlight` | trust-one-enter: captured accept, stale accept, then ready results in exactly one CR. | P |
| PC-42 / G-42 | Move ContainsBlockingUpdate ahead of ContainsSignIn in Classify. | `/*/*/CodexV0160StartupTests/C907_signed_out_chooser_is_sign_in` | signin-before-continue: labelled derived signed-out plus legacy continue footer has Reason SignIn. | P |
| PC-43 / G-43 | In IsMeasuredUpdatePicker remove only the Update available conjunct. | `/*/*/CodexV0160StartupTests/C907_update_picker_is_blocking_but_static_notice_is_not` | picker-title-required: skip-option-only derived ready frame is Ready and not a measured picker. | P |
| PC-44 / G-44 | In IsMeasuredUpdatePicker remove only the Skip until next version conjunct. | `/*/*/CodexV0160StartupTests/C907_update_picker_is_blocking_but_static_notice_is_not` | static-update-ready: captured legacy static notice above ready remains Ready. | P |
| PC-45 / G-45 | Remove only the Press enter to continue arm of ContainsBlockingUpdate. | `/*/*/CodexStartupReadinessTests/Blocking_update_is_distinct_from_a_static_notice` | The derived picker IsReady is false; static notices still pass before it. | P |
| PC-46 / G-46 | Remove only IsMeasuredUpdatePicker(screen) from the update-action condition. | `/*/*/CodexReadyWaitTests/Bare_continue_prompt_does_not_send_escape` | writes.ShouldBeEmpty after the bare-continue wait. | P |
| PC-47 / G-47 | Remove only !dismissedUpdate from the update-action condition. | `/*/*/CodexV0160StartupTests/C907_update_escape_requires_a_fresh_settled_frame` | update-one-escape: picker, stale picker, ready produces exactly one ESC. | P |
| PC-48 / G-48 | Remove only tracker.Reset() after the successful trust write. | `/*/*/CodexV0160StartupTests/C907_trust_accept_requires_current_accept_highlight` | trust-resets-candidate: pre-modal ready candidate cannot shorten the post-trust settle. | P |
| PC-49 / G-49 | Replace the continue after successful Escape by return true. | `/*/*/CodexV0160StartupTests/C907_update_escape_requires_a_fresh_settled_frame` | update-needs-new-frame: hold the first post-Escape snapshot; gate is still incomplete. | P |
| PC-50 / G-50 | After a successful modal write reassign deadline = time.GetUtcNow() + options.MaxWait. | `/*/*/CodexV0160StartupTests/C907_modal_actions_share_the_original_deadline` | modal-original-budget: 600ms modal + 1000ms settle inside 1500ms total returns false at original deadline. | P |
| PC-51 / G-51 | When session_meta originator is codex_exec, preset both dialect latches to Flat. | `/*/*/CodexTranscriptNormalizerTests/C907_thread_item_dialect_is_not_selected_by_originator` | exec-originator-same-parts: derived codex_exec header yields the same UserPrompt, assistant reply and TurnEnd as the captured TUI. | N |
| PC-52 / G-52 | Remove only the TakeDialect check in FromFlatUser. | `/*/*/CodexTranscriptNormalizerTests/A_second_dialect_in_the_same_file_cannot_double_emit` | The post-flat UserPrompt count is 1 (the earlier count still passes). | N |
| PC-53 / G-53 | Remove only the TakeDialect check in FromFlatAgent. | `/*/*/CodexTranscriptNormalizerTests/A_second_dialect_in_the_same_file_cannot_double_emit` | The final AssistantText count is 1. | N |
| PC-54 / G-54 | Before the type!=event_msg guard, map a response_item user message to a UserPrompt using its content[].text. | `/*/*/CodexTranscriptNormalizerTests/C907_v0160_rollout_preserves_prompt_reply_turn_identity_and_usage` | native-prompt-count: baseline or injected duplicate-response input yields exactly one UserPrompt; S0 binding records the first applicable assertion in method order. | N |
| PC-55 / G-55 | In the AgentMessage mapping set ApiCallId to null. | `/*/*/CodexTranscriptNormalizerTests/C907_v0160_rollout_preserves_prompt_reply_turn_identity_and_usage` | final-turn-id: final assistant ApiCallId equals the nonempty captured task_complete turn_id. | N |
| PC-56 / G-56 | Use turnId for every AgentMessage phase. | `/*/*/CodexTranscriptNormalizerTests/TUI_final_answers_share_their_turn_identity_while_commentary_stays_unattributed` | texts[1].ApiCallId is null for commentary. | N |
| PC-57 / G-57 | Change only FromTaskComplete StopReason from end_turn to null. | `/*/*/CodexTranscriptNormalizerTests/C907_v0160_rollout_preserves_prompt_reply_turn_identity_and_usage` | task-complete-end-turn: exactly one TurnEnd has StopReason end_turn. | N |
| PC-58 / G-58 | For top-level token_usage_record set _cumulative and _turnBaseline to default and _sawUsageThisTurn=false before returning []. | `/*/*/CodexTranscriptNormalizerTests/C907_v0160_rollout_preserves_prompt_reply_turn_identity_and_usage` | token-usage-record-ignored: insert that derived record after token_count; all four TurnEnd usage fields equal the measured baseline. | N |
| PC-59 / G-59 | Replace delta = now - _turnBaseline by delta = now. | `/*/*/CodexTranscriptNormalizerTests/Per_turn_usage_is_the_cumulative_delta_with_cached_input_de_overlapped` | Turn 3 InputTokens equals (157058-18609)-(138240-11008). | N |
| PC-60 / G-60 | Set inTok = Math.Max(0, delta.Input) without subtracting delta.Cached. | `/*/*/CodexTranscriptNormalizerTests/Per_turn_usage_is_the_cumulative_delta_with_cached_input_de_overlapped` | Turn 2 InputTokens equals 18609-11008. | N |
| PC-61 / G-61 | Return Guid.NewGuid().ToString("N") from SynthesizeUuid. | `/*/*/CodexTranscriptNormalizerTests/A_re_tail_from_offset_zero_reproduces_identical_parts` | The second normalized tuple sequence equals the first, including TurnEnd Uuid. | N |
| PC-62 / G-62 | In FlushIfIdleAsync replace !await ReadWorkingAsync(db, sessionId, ct) with true; the test explicitly invokes this flush while busy. | `/*/*/CodexV0160QueueDeliveryTests/C907_busy_queue_delivers_whole_prompt_once` | busy-no-input: no body writes and no matching recipient UserPrompt before the real turn-end release. | Q |
| PC-63 / G-63 | Skip the ordinary non-completion SaveChangesAsync after stamping Sent/baseline/attempt and before DeliverAsync. | `/*/*/CodexV0160QueueDeliveryTests/C907_committed_attempt_recovery_delivers_once` | attempt-before-body: the recipient BeforeInput observer reads attempts==1, Sent and the frozen baseline from a separate DbContext. | Q |
| PC-64 / G-64 | In RecoverDeliveryRunLockedAsync disable the HeadFragmentIsVisibleWhole Enter-only return so the run reverts to Pending. | `/*/*/CodexV0160QueueDeliveryTests/C907_committed_attempt_recovery_delivers_once` | held-body-not-retyped: recovery writes contain only CR, and the matching complete recipient prompt occurs once. | Q |
| PC-65 / G-65 | Return LateConfirmCounts.Empty immediately from LateConfirmAttemptedMessagesAsync. | `/*/*/CodexV0160QueueDeliveryTests/C907_committed_attempt_recovery_delivers_once` | accepted-body-not-retyped: after provider recreation, zero further body/Enter writes and one matching complete UserPrompt. | Q |
| PC-66 / G-66 | In WaitForTranscriptConfirmAsync bypass only the !match.Complete rejection; preserve identity matching. | `/*/*/CodexV0160QueueDeliveryTests/C907_receipt_requires_complete_current_session_prompt` | truncated-not-confirmed: prefix-only recipient row must not produce transcript-confirmed delivery. | Q |
| PC-67 / G-67 | Remove only AgentSessionId == sessionId from TryFindConfirmingRecordAsync's transcript query. | `/*/*/CodexV0160QueueDeliveryTests/C907_receipt_requires_complete_current_session_prompt` | other-session-not-confirmed: complete body in a different session does not confirm the queue row. | Q |
| PC-68 / G-68 | Remove only Sequence > baselineSequence from TryFindConfirmingRecordAsync. | `/*/*/CodexV0160QueueDeliveryTests/C907_receipt_requires_complete_current_session_prompt` | old-sequence-not-confirmed: complete prior UserPrompt at the baseline does not confirm this attempt. | Q |
| PC-69 / G-69 | Change only Confirmed(DeliveryConfirmedBy.Screen) to Confirmed(DeliveryConfirmedBy.Transcript) in the unobservable deadline fallback. | `/*/*/CodexV0160QueueDeliveryTests/C907_unobservable_working_remains_unverified_until_whole_receipt` | screen-is-not-receipt: ConfirmedBy is Screen, DeliveryUnverified exists and complete recipient receipt count is zero. | Q |
| PC-70 / G-70 | Make FlushStrandedQueuesAsync return 0 before scanning persisted queued rows. | `/*/*/CodexV0160QueueDeliveryTests/C907_queue_recreation_preserves_pending_identity` | recreated-queue-receipt: after recreated provider flush, exactly one matching recipient UserPrompt and unchanged row ID/sequence/body. | Q |
| PC-71 / G-71 | Suppress the DeliveryFailure outbox Add in AgentTaskDispatcher.FailAsync when deliveryFailureNotificationId is supplied. | `/*/*/CodexV0160QueueDeliveryTests/C907_enqueue_failure_returns_whole_failure_note` | failure-obligation-durable: after failure settlement, the task-linked DeliveryFailure notification exists before caller receipt. | Q |
| PC-72 / G-72 | Make MatchesLegacy return false while preserving MatchesV0156. | `/*/*/CodexV0156StartupPromptTests/Legacy_trust_wording_is_still_recognised_and_accepted` | CodexTrustPromptDetector.IsVisible(LegacyTrustScreen) is true. | P |
| PC-73 / G-73 | Invoke sendLine(ct) twice at the start of CodexSubmitConfirmation.SubmitAsync. | `/*/*/RunnerCodexAdapterSubmitConfirmTests/C907_current_working_degrades_without_retyping` | submit-body-once: captured working/no transcript path returns with exactly one body write. | T |

Guard audit: **guards=73, mapped=73, missing=0, duplicate PC maps=0**.
This is a design census. The post-land completion gate additionally requires
**73/73 executed**, all variants classified and source restored; it has not
been satisfied by this document.

### Out of scope

- **Full CARD-0907 / CARD-0904 deploy acceptance is incomplete.** S0 is the only
  authorized paid turn in this work. Live quota exhaustion, compaction/continue,
  post-login onboarding, live MCP scenes not captured, Windows 120x30 ConPTY
  qualification, and a post-deployment Plan-task canary remain unobserved here.
  The existing vendor status panel proves neither a usage-limit refusal nor live
  quota recovery. Do not close those acceptance rows by running these filters.
- **Current update modal** remains unobserved unless scratch version.json
  actually elicits it. Legacy 0.156/0.158 picker coverage and labelled derived
  title/skip controls do not upgrade current-version acceptance.
- **Other current spellings/geometry** such as Thinking not observed in S0,
  wrapped/narrow layouts and different OS backends remain unobserved. A derived
  negative can reject prose; it cannot establish an unseen positive grammar.
- **Deployment/configuration**: master only; no RC changes, image rollout,
  AppHost/runner restart, timeout default/user-secret edit, history wipe, credential
  repair, provider upgrade or model ladder change.
- **Unchanged raw response/status fixes** already handled by the October-15
  CARD-0907 slice: no CodexOutputText, UsageLimitWallParser or SubscriptionUsageParser
  redesign/requalification here. Normalizer error/abort cases run as compatibility
  regression, not new current-CLI quota or interruption evidence.
- **Transport/native process guarantees**: no full suite, headed canary, actual
  OS crash/remote runner destruction, full tailer path-discovery test, process
  ownership/kill-policy mutation, or production messaging broker. Q is a real
  queue with a scripted receiver and persisted-state recovery; S0 supplies the
  native complete prompt independently. If new behavior requires a missing
  native seam, return to Plan instead of substituting an ack.
- **Unchanged guards outside the selected behavior**: cancellation/exit checks,
  Grok/Claude-specific delivery policies, queue retention/limits, permission
  state, and failure-notification internals outside the named handoffs receive
  no new safety claim. Existing selected regression methods may cover them,
  but their prior verification is not relabelled as this parser repair's PCs.

Acceptance ledger to carry forward unchanged until new evidence is attached:

| Acceptance | Now | Required to change status |
|---|---|---|
| Current submitted working/done and native prompt | binding required | S0 B-1/B-3 + V-1/V-2/V-3 |
| Independent current queue footer | binding required | S0 B-2 + assertion-red/green CP-4 |
| Current ready at Linux 120x30 | historic observation; new binding required | S0 current frame + V-5 |
| Current trust accept/back; signed-out | later trust wording observed historically; new fixture binding required | S0 B-4 + V-6; retain earlier Quit/continue shapes separately |
| Current update picker | unobserved | actual B-4 picker plus V-6 current variant |
| Interactive 0.160 originator/rollout/usage | binding required | B-3 + V-7; exec-originator derived test is not a live exec capture |
| Complete queue/caller receipt and handoff recovery | designed, not executed | V-9/Q-1–Q-7 recipient evidence |
| Usage-limit refusal; compaction/continue; post-login | unobserved/out of this execution | separately commissioned measurements; no quota exhaustion here |
| Windows/backend qualification and post-deploy Plan canary | unobserved/out of this execution | separately commissioned Windows/operational acceptance |

### Checkpoints

Closed ordinary Code selection after S0 binding. Only CP-2 and CP-4 have
intentional tests-first red runs; commit/push their tests before the production
fix, then commit/push each fix before the green run. Each row has one isolated
build or permitted same-After reuse and one exact filter. EstimatedMinutes includes
build time where built; Min counts TUnit results only. **No suite was run to derive
these floors**: existing methods/Arguments were counted from the read source.
New methods are single-result methods; do not convert their internal variants into
Arguments without updating Min and the mutation bindings.

The capture is an explicitly separate S0 measurement/curation prerequisite,
not an ordinary test-driver checkpoint or a hidden paid turn inside V-1. Its
20–40 minute allowance is costed below. If it needs a scratch renderer build, run
that one named S0 capture-renderer bootstrap through the build-slot gate and
report its source/command/time separately; do not run a suite to obtain a renderer.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S0 | `tests/Antiphon.Agents.Pty.Tests -> bin-c907-fixtures/` | fixture-replay | `/*/*/CodexV0160FixtureTests/*` | V-1 | all 2 methods, 0 failed/skipped | 2 | 3 | true |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c907-working/` | working-witness | `/*/*/CodexWorkingIndicatorTests/C907_submitted_frame_is_working` | V-2 | 1 executed, 0 failed/skipped after fix | 1 | 8 | true |
| CP-3 | S1 | CP-2 | working-lifecycle | `/*/Antiphon.Tests.Agents/(CodexWorkingIndicatorTests*)\|(RunnerCodexAdapterTurnCompleteTests*)/*` | V-2, V-3, R-1 | all 18 results, 0 failed/skipped | 18 | 2 | true |
| CP-4 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c907-startup/` | queue-witness | `/*/*/CodexV0160StartupTests/C907_running_queue_footer_blocks_ready` | V-4 | 1 executed, 0 failed/skipped after fix | 1 | 4 | true |
| CP-5 | S2 | CP-4 | startup-and-submit-frames | `/*/Antiphon.Agents.Pty.Tests/(CodexV0160StartupTests*)\|(CodexStartupReadinessTests*)\|(CodexReadyTrackerTests*)\|(SubmitEvidenceTests*)/*` | V-4, V-5, V-6, V-8, R-2, R-4 | all 56 results, 0 failed/skipped | 56 | 1 | true |
| CP-6 | S2 | CP-4 | startup-waits | `/*/Antiphon.Agents.Pty.Tests/(CodexReadyWaitTests*)\|(CodexV0156StartupPromptTests*)/*` | R-2 | all 27 results, 0 failed/skipped | 27 | 2 | true |
| CP-7 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c907-rollout/` | native-rollout | `/*/*/CodexTranscriptNormalizerTests/*` | V-7, R-3 | all 19 results, 0 failed/skipped | 19 | 4 | true |
| CP-8 | S1 | CP-2 | submit-confirmation | `/*/*/RunnerCodexAdapterSubmitConfirmTests/*` | V-8, R-4 | all 9 results, 0 failed/skipped | 9 | 1 | true |
| CP-9 | S1-S3 | `tests/Antiphon.Tests -> bin-c907-delivery/` | queue-recipient-recovery | `/*/*/CodexV0160QueueDeliveryTests/*` | V-9, R-5 | all 7 methods and Q variants, 0 failed/skipped | 7 | 18 | true |

CP-5's 56 = 12 new startup + 31 legacy classifier + 4 tracker + 9 submit predicate.
CP-6's 27 = 19 wait + 8 trust compatibility. CP-9's seven methods include internal
case matrices, not seven claimed crash events or one result per assertion.
The union covers V-1–V-9 and R-1–R-5; duplicate method executions are intentional
direct witness plus regression coverage. The ordered slice groups are S0, then
S1 (CP-2, CP-3, CP-8), S2 (CP-4–CP-6), S3 (CP-7), S1-S3 (CP-9).
Build reuse never crosses source commits or After groups.

Build the checkpoint tool once through
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c907-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c907-tool/ --nologo`.
Use its documented `run --plan <this-file> --rows <CP-list> --expected-source-sha <full-committed-sha>`
once per group; keep awaiting `wait` while exit is 75. Bootstrap/run drivers take
the host build-slot gate; no unleased retry after a slot timeout. On Linux retain
the checkpoint tool's UseAppHost=false handling. Serial rows prevent cross-assembly
PTY/FakeClaude concurrency. Q uses isolated schemas and the assembly-local
ProcessSpawnLimit attribute if it launches any child.

Preserve unedited CHECKPOINT lines and complete source/build/roster receipts
under ignored evidence. Validate them against their actual source SHA; never
relabel earlier source receipts after a commit. Check source/evidence with
`scripts/check-evidence-diff.ps1 -BaseRef <dispatch-base> -HeadRef <pushed-sha>`.
No unchecked skip, missing fixture or zero execution is green. Run static
plan-to-test coverage with the bound new files before final ordinary checkpoints;
unbound payloads/unsupported helper analysis stay visible, not silently marked
covered. Clean only producer-owned alternate outputs by the documented custody
path. Record every extra run's reason and cost; no broad diagnostic loop.

### Cost

All numbers below are **estimates**, not measured durations or a claim of green.

- **Ordinary V/R floor (Code): 43 minutes** =
  CP-1 3 + CP-2 8 + CP-3 2 + CP-4 4 + CP-5 1 + CP-6 2 + CP-7 4 +
  CP-8 1 + CP-9 18. Filters are exactly the table above. This includes all row
  builds (estimated 27 minutes) and ordinary execution (16 minutes), so do not
  add those builds a second time. Separate tool bootstrap: **2 minutes**.
- **Tests-first red evidence: 12 minutes** = exact CP-2 method 8 + exact CP-4
  method 4. Their green runs are already included in the 43-minute ordinary
  floor. Expected-red receipts and both SHAs remain historical evidence.
- **S0 capture/setup/curation: 20–40 minutes**, including one paid trivial
  gpt-6.1-sol/low turn, no-model-turn modal attempts, owned-child cleanup and,
  if needed, the single gated scratch renderer build. Model token cost is
  capture-dependent and must be recorded from B-3; it is not invented here.
- **PC floor (Mutation): 694.5 minutes**, separate from Code.
  The 40 P exact-method filters above each budget **2 baseline + 2 red +
  2 restored-green = 6 minutes** including fresh phase builds:
  40 x 6 = 240.
  The 12 T filters each budget **3 + 3 + 3 = 9 minutes**:
  12 x 9 = 108.
  The 11 N filters each budget **2 + 2 + 2 = 6 minutes**:
  11 x 6 = 66.
  The 10 Q filters each budget **8 + 8 + 8 = 24 minutes**, including the
  per-method queue matrix and fresh schema/provider setup:
  10 x 24 = 240.
  Phase execution/build total = **654 minutes**. Add **36.5 minutes**
  (0.5 per PC) for applying/checking/restoring mutations and **4 minutes** for
  Mutation bootstrap/receipt setup: **694.5 minutes**. All cases in each exact
  method run in every phase. Slot contention, new failure investigation and
  report authoring are additional, never omitted from measured actuals.
- **Total verification/setup allowance: 771.5–791.5 minutes** =
  2 bootstrap + 20–40 S0 + 12 intentional-red + 43 ordinary + 694.5 Mutation.
  Implementation/test authoring and ordinary Review effort are additional.
  The Code execution allowance is **77–97 minutes** before authoring; do not
  commission Code for the Mutation floor.
- The 24-minute provisional ordinary allocation becomes 43 (**+19 minutes**):
  explicit submit coverage adds 1 and queue/recipient recovery adds 18. This
  prevents the shared matcher change from escaping through an unverified
  asynchronous consumer. Same-source reuse on CP-3/CP-8/CP-5/CP-6 saves an
  estimated **24 build minutes** versus four fresh builds (8+8+4+4).
  Mutation build savings assumed **0**: distinct-source red/restore phases and
  shared mutation sites require fresh verified builds. No claimed full-suite
  saving uses an obsolete full-suite runtime.

Next stage: **code**, starting with S0 binding only; implementation of S1/S2 is
conditional on the recorded capture and independent queue witness being reachable.
Any unmet binding or untestable delivery seam returns to Plan with the precise
gap. All missing broader card acceptance and all post-land PCs remain owed.
