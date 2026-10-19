# CARD-0907: Codex 0.160.0 parser drift

Date: 2026-10-19. Stage: Plan. Target: **master only**.
Plan source: `c8bc95276ad1dfa22fcfcd0d1a2b3b2c6a1d80b0`.
Assigned plan branch: `feat/card-task-046b1e67`.

Restore positive working/queue recognition from a submitted 0.160.0 TUI turn,
while preserving transcript authority and the existing positive readiness gate.
Capture the missing frame before choosing either replacement pattern. Ship three
small behavior/compatibility slices after the capture slice. No RC changes,
deployment, history cleanup, provider upgrade, or model-tier change belongs here.

Verification is a **separate TestDesign stage** in this dispatch. The witness
requirements and checkpoint allocation below are its inputs, not a claim of
completed verification design or permission to skip TestDesign. Next: test-design.

## Evidence and placement

Read CARD-0907 with `pwsh -NoProfile -File scripts/card.ps1 get CARD-0907 -Board Antiphon`.
Its initial "no captures exist" wording predates the investigations below.

| Source | Immutable reference | Use and limit |
|---|---|---|
| `docs/investigations/2026-10-18-codex-0160-parsers.md` | `feat/card-task-359b4ff7`, `c5543c29839e9c9d6d19e012daedb927551a78af` | Installed-binary literal census, exec rollout, idle/trust frames; submitted TUI frame was **not** obtained. |
| `docs/investigations/2026-10-18-codex-0160-ready.md` and its adjacent captures | `feat/card-task-13a42530`, `87fad6edb6e8bc80ff27c25419b869f7d84e2aac` | Ready footer and historical 51.47 s cold paint / 1.18 s warm paint; not today's cold-start measurement. |
| `docs/investigations/2026-10-01-card-0904-codex-0160-trust-screen.md` | Present at plan source | Earlier 120x40 image capture; contains `2. Quit` and `Press enter to continue`, unlike the later installed binary. Preserve both proven shapes with their provenance. |
| Dispatch brief, 2026-10-19 | Task `046b1e67` | Reports about 3.5 s cold startup after history removal and live server `CodexReadyMaxWaitMs=120000`. These are supplied operational observations, not measurements repeated by this Plan. |

The two investigation commits were fetched and read directly; they need not be
merged into a Code branch to recover their documents (`git show <sha>:<path>`).
The parser investigation committed only its report, not the exec JSONL. Do not
claim that the original rollout is recoverable from that commit.

On 2026-10-19, `GET /api/runner-defaults` returned revision 2 with global preference
`server2` and no kind overrides. `GET /api/session-runners` returned eligible,
accepting Linux and Windows runners reporting 0.160.0; the Linux version observation
was stale on the follow-up read. The spare runner was unavailable. Re-read both
routes at dispatch and verify the actual CLI locally during capture. Availability
is not a reservation or a fresh binary measurement.

The **capture lane** is Linux PTY, on the brief's specifically requested server2
runner, uid 1654. Pin that one capture if dispatching it separately. The **parser
lanes** are portable, offline .NET tests on the currently eligible default runner;
omit `-Runner` and `-Platform` for those stages. Use `-Platform Any` only to clear an
existing OS pin. Do not copy an IP address, container ID, or deployment path into
dispatch commands. No Windows/ConPTY qualification is implied by Linux fixtures.

## Ground truth

| Card/brief assumption | What this checkout and evidence actually do | Planning consequence |
|---|---|---|
| A running Codex turn has a recognizable working line. | `src/Antiphon.Agents.Pty/CodexDetectors.cs`, `CodexWorkingIndicator`, requires `Working (` and `esc to interrupt)` on one row. Neither literal exists in the investigated installed binary. | Confirmed defect. Capture the replacement before retargeting. Binary words `Ready`, `Working`, `Thinking` alone do not specify a parser. |
| Queue mode blocks startup input. | `CodexStartupScreen.Classify` in `CodexStartupReadiness.cs` checks `tab to queue message`, after the working check. That literal is absent; the current running footer is unknown. | Capture it and design an independent queue witness so the working branch cannot mask a broken queue matcher. |
| Trust option 2 is `Quit`. | The later installed TUI says `Back to Agent Command Center`. The detector matches the question and option 1, then requires the accept highlight. It never requires option 2's text. | Preserve logic; update explanatory comments and cover both highlighted options with the new fixture. |
| Missing `Press enter to continue` breaks update/sign-in detection. | Sign-in has its own chooser phrases and runs first. Blocking update also recognizes `Update available` plus `Skip until next version`; Escape is limited to that measured picker. | Do not invent a replacement for the missing phrase. Preserve legacy support; verify the current chooser/picker and static-notice negative control. |
| `codex exec` emits only flat rollout records. | The normalizer's class comment says dialect depends on launch mode, not version. The measured exec rollout has `originator=codex_exec`, thread items and `task_complete`; the existing dialect latch handles it. | Correct the comment and add fixture coverage. No switch rewrite, no stdout parser. |
| Done can be inferred from quiet or a `Ready` word. | `CodexTurnScreenTracker` requires working seen, working gone, quiet output mark, and visible raw output. `RunnerCodexAdapter` prefers a transcript TurnEnd. | Preserve these invariants; prove the new line drives the existing lifecycle. |
| Codex ready text/model footer needs a parser fix. | Current `TryReadFooterOnlyModel` accepts `GPT-… default/xhigh · <cwd>`, warning chip included; splash without the footer remains Unknown. | Add current fixtures; keep readiness rules and settle time. |
| The timeout change is part of this code fix. | The brief reports live 120000 ms. `server/Application/Settings/AgentRegistrySettings.cs:63` and the in-process detector still default to 60000 ms at this source. | Distinguish deployed configuration from source defaults. Neither is changed by this parser plan. |
| The full 0.160.0 deploy gate is proven by existing reports. | No submitted TUI frame in the parser report; update, live quota, compaction/continue and current interactive rollout were not all observed. `v0160-status-panel.txt` is a vendor-derived status panel, not a quota refusal. | Retain an explicit acceptance ledger; this plan does not certify all of CARD-0907 or authorize CARD-0904 deployment. |

## Decisions

**D-1 — Capture before retargeting.** S0 is a hard dependency of the working and
queue fixes. Capture current frames continuously, not by waiting for today's broken
matcher to fire. Reject guessing a regex from binary strings or accepting any
occurrence of `Working`/`Thinking`: model prose, a draft, or help text could match.

**D-2 — Match measured current screen structure.** Keep the legacy working shape
and add only observed 0.160.0 status-row structure, with its distinguishing
delimiters/position/context. Inspect a state token as part of that structure; never
match the OSC title, an arbitrary transcript occurrence, or bare elapsed silence.
Do not version-gate by banner: two reports labelled 0.160.0 already differ. Record
binary fingerprint and capture context instead. TestDesign must identify the exact
row grammar after S0, including whether `Thinking` was actually observed.

**D-3 — Queue gets its own evidence.** Prefer a small, pure queue-footer predicate
in `CodexStartupReadiness.cs`, matching only the observed active composer/footer
context. Preserve the existing legacy needle. Retain classifier precedence:
modals/MCP, then working, then queue, then positive ready layout. Reject using a
help-tip sentence as evidence that a queue footer exists. If no distinct queue
footer is painted, record that result and return the queue portion for investigation
instead of fabricating a passing fixture or deleting the old guard.

**D-4 — Preserve working parser and modal compatibility.** Do not delete old idle
hints, trust wording, the bare-continue update arm, or flat rollouts merely because
the installed binary no longer emits them. Existing supported captures still use
them. Trust comments must describe accept selection rather than a guaranteed Quit
destination. Update/sign-in comments must distinguish legacy footer wording from
current picker/chooser recognition. No new automatic keystroke is introduced.

**D-5 — Transcript remains authoritative.** Keep the normalizer's first-dialect
latch, duplicate suppression, turn identity, usage deltas and synthesized `end_turn`.
`token_usage_record` stays ignored while the measured `token_count` supplies usage.
Do not parse `codex exec --json` stdout as rollout JSONL. The comment repair records
the 0.147 historical samples and the 0.160 exec thread-item observation separately.

**D-6 — Bound scope by evidence.** No timeout widening, history deletion, credential
repair, launch/submit protocol change, rollout-directory change, or version-policy
change. Ready timing is already explained by separate evidence. Unsupported modal
or missing running-frame evidence leaves that acceptance row incomplete; it does
not justify weakening a detector. Wider CARD-0907 acceptance remains visible to
the caller rather than silently becoming part of a parser rewrite.

## Slices

### S0 — Capture one submitted turn and curate reproducible inputs

This is the first execution step. It can land as fixtures with passing fixture
integrity/replay tests, independently of product changes. TestDesign specifies the
capture harness and final replay assertions before Build starts it; the observed
grammar is then recorded before S1/S2 edits. If S0 contradicts the detector premise,
stop that dependent slice and report `next: investigate` with the missing measurement.

1. Use the investigation's foreground interactive PTY approach as **uid 1654,
   never root**. Verify uid and `codex --version`, resolve and hash the executable,
   record OS/backend, UTC time, argv and geometry. Use 120 columns x 30 rows,
   `TERM=xterm-256color`, `--no-alt-screen`, the measured production permission
   flags, `-m gpt-6.1-sol`, low reasoning, `disable_paste_burst=true`, and disabled
   startup update checking for the turn capture.
2. Allocate a fresh scratch cwd and scratch `CODEX_HOME`; do not reuse or wipe the
   service home/history. Use only the already sanctioned credential-copy approach
   from the investigation, under the same uid with owner-only permissions; never
   print/read credentials into the report. Do not initiate login or copy the whole
   service home. Preserve the inherited home variable and give a child-only override.
   Note the temporary-home warning if it appears; do not reinterpret it as a modal.
3. Start capturing before submit, keep stdin open, and reconstruct every paint
   through `TerminalScreen(120, 30)` or replay the captured stream through it.
   Do not strip CUP/CUF sequences with sed and call the result a rendered frame.
   Wait for the positive settled composer, then send one trivial prompt, e.g.
   `Reply with the single word pong. Do not call tools. Do not read or write files.`
   Preserve the LF/bracketed-paste body protocol and send Enter as a separate write.
   A piped newline stranded the prior probe in its composer.
4. Save pre-submit idle, an in-progress status line with its entire composer/footer,
   and post-turn idle/done frames. Record byte offsets/times and the associated
   rollout's matching native UserPrompt, final answer and task_complete/TurnEnd.
   A draft on screen is not a submit receipt. Do not retype the body or silently
   submit another paid turn if capture misses the active frame. Record the missing
   evidence and propose a specifically scoped follow-up measurement.
5. Capture current trust and signed-out screens in separate fresh scratch homes
   without model turns; use an isolated untrusted scratch git repository for trust.
   Exercise option 2 by moving the highlight without pressing Enter on it. Capture
   the update picker using the existing CARD-0777 scratch `version.json` technique
   with update checks enabled only for that probe. Never accept `Update now`.
   If a current picker cannot be elicited, retain the old measured regression and
   label current-version acceptance unobserved; do not synthesize a live capture.

Files:

- `tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-{idle,running,done,trust-prompt,trust-back-selected,signed-out,update-modal}.txt`, for frames actually captured.
- A minimal curated raw replay fixture in the same directory, e.g. `v0160-running.raw.txt`, and `v0160-provenance.md` describing source fingerprint, frame offsets, hashes, transformations and any missing scenes.
- `tests/Antiphon.Agents.Pty.Tests/CodexStartupFixtures.cs`: shared accessors that remove any fixture metadata header before passing payload to production parsers. This file and its fixtures are already linked into `Antiphon.Tests`; no duplicate fixture set is needed.
- New `tests/Antiphon.Agents.Pty.Tests/CodexV0160FixtureTests.cs`: `C907_raw_capture_replays_the_running_frame` and `C907_fixture_payloads_match_recorded_provenance`. Neither asserts the unfixed working parser yet.
- Curated interactive rollout input `tests/Antiphon.SessionRunner.Tests/Fixtures/codex-v0160-tui-turn.jsonl` for S3. Preserve parser-consumed fields and disclose removal of base instructions, paths or unrelated private material.

Full raw PTY streams, unredacted rollouts, probe scripts/output, receipts and logs
stay ignored under `.antiphon/card-0907-capture/`. Only deliberately reviewed,
minimal test inputs consumed by tests belong under `tests/.../Fixtures`; relocating
bulk generated evidence there is prohibited. The current fixture globs already
copy these inputs to test output. Do not rewrite existing provenance manifests.

Probe ownership/release: the foreground harness owns the child and its PTY until
exit. Bound startup at 120 s, the submitted-turn observation at 120 s, and cleanup
at 5 s; on success, cancellation or deadline, stop/reap only that owned scratch
child and remove its credential copy. Await cleanup before returning. This probe
deadline does not add a production stall-kill policy. For a production session
waiting for input, parking is disabled by default and **there is no automatic
release deadline** (CARD-1083); input/explicit owner action or the separately
enabled conditional parking path governs its continuation/release.

### S1 — Restore the working signal and completion fallback

Files: `src/Antiphon.Agents.Pty/CodexDetectors.cs`,
`tests/Antiphon.Tests/Agents/CodexWorkingIndicatorTests.cs`,
`tests/Antiphon.Tests/Agents/RunnerCodexAdapterTurnCompleteTests.cs`, and, only for
fixture injection, `tests/Antiphon.Tests/Agents/ScriptedCodexRunnerClient.cs`.

Commit and push `CodexWorkingIndicatorTests.C907_submitted_frame_is_working`
before changing production code. Run its exact method and retain an assertion-red
result against S0 plus that test. A compile failure, missing fixture or zero tests
is not the witness. Implement the measured additional grammar and rerun it green.

Add `C907_captured_lifecycle_requires_seen_gone_and_settle` with deterministic time
and output marks, and `C907_status_words_outside_the_status_row_are_not_working`.
Include idle/Ready, ordinary answer prose containing Working/Thinking, unsubmitted
draft text, startup/MCP spinner, a malformed/partial row, and raw-history-only
controls. If only one active spelling was captured, mark other spellings derived
or unobserved; do not label them captures.

Add `RunnerCodexAdapterTurnCompleteTests.C907_captured_lifecycle_completes_screen_fallback`
using the actual running/done fixture pair with transcripts unavailable. Keep the
existing transcript-first and never-working tests. Avoid a new adapter algorithm:
the shared `CodexTurnScreenTracker` should work after the predicate repair.

Retain public legacy Prefix/Suffix constants while adding the new shape. Inspect
`tests/Antiphon.Agents.Pty.Tests/CodexDoneDetectionCanaryTests.cs`, whose diagnostic
line extraction still uses Prefix. If updating its diagnostic selection/comment,
include that exact file in the slice and distinguish it from the measured Linux
probe. Its opt-in ModernConPTY real-account canary is not an ordinary Linux row.

S1 lands with its tests green; a tests-only red commit is evidence, not a landable
endpoint. S1 depends on S0, not on queue or transcript-comment changes.

### S2 — Retarget queue blocking and pin modal compatibility

Files: `src/Antiphon.Agents.Pty/CodexStartupReadiness.cs`, trust comments in
`src/Antiphon.Agents.Pty/CodexDetectors.cs`, new
`tests/Antiphon.Agents.Pty.Tests/CodexV0160StartupTests.cs`, and the S0 fixture helper.

Create `C907_running_queue_footer_blocks_ready` before the queue predicate change.
Use a labelled **derived** frame that places the captured queue footer in an
otherwise positively ready layout, removes the working row and excludes all
earlier blockers. Assert NotReady with reason QueueMode, not merely NotReady.
This must fail for the old queue guard and pass for the replacement; a full running
frame classified Working cannot prove the queue arm. Confirm an ordinary answer
quoting the queue wording does not become an active footer.

The same class supplies `C907_running_frame_blocks_startup`,
`C907_idle_frame_requires_full_settle_after_working`,
`C907_trust_accept_requires_current_accept_highlight`,
`C907_signed_out_chooser_is_sign_in`,
`C907_update_picker_is_blocking_but_static_notice_is_not`, and
`C907_update_escape_requires_a_fresh_settled_frame`.
Use FakeTimeProvider/current-frame snapshots for settle and write behavior. Verify
zero Enter writes for the back option/sign-in/history-only trust, one Escape for
the measured update picker, and the original total wait budget. Existing legacy
tests remain unchanged except precise version-labelled comments.

Trust/sign-in/update behavior is expected green before product edits. Do not
manufacture a red-first "fix" for an unchanged parser. If a captured current modal
actually fails, commit its exact assertion witness first, then make a separately
identified minimal fix with a method-scoped red/green run. TestDesign must add that
row before execution. Missing current modal capture remains missing acceptance.

S2 depends on S0 and S1 for the full working-to-ready lifecycle; its queue guard
can be developed independently with the isolated queue witness. Land only its
completed, green scope, recording unresolved capture evidence separately.

### S3 — Correct dialect documentation and smoke the current rollout

Files: `src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs` (class comment),
`tests/Antiphon.SessionRunner.Tests/CodexTranscriptNormalizerTests.cs`, and the S0
interactive rollout fixture/provenance. No normalizer production logic change is
expected. Existing `codex-*.jsonl` copying already includes the new fixture.

Add `C907_v0160_rollout_preserves_prompt_reply_turn_identity_and_usage` against the
captured TUI rows. Assert exactly one UserPrompt, the expected assistant reply,
one end_turn, shared final-answer/TurnEnd turn id, and measured usage numbers;
preserve relevant token_count and task_complete rows. Verify that additional
token_usage_record and duplicate response_item rows produce no extra prompt or
usage. Re-tail deterministically. Never substitute exec stdout events.

Add `C907_thread_item_dialect_is_not_selected_by_originator` using a clearly
labelled derived copy of the same thread-item input with session_meta originator
changed to codex_exec. This proves mode-independent acceptance; the October 18
report supplies the historical live exec observation. It is not a second live
exec capture. Retain the old actual flat exec fixture and its assertions.

These tests should pass before the comment edit. A failure is new evidence to
isolate, not authorization for an unexplained normalizer rewrite. S3 is independently
landable after S0 and does not require S1/S2.

## TestDesign handoff

Turn the following obligations into the final `## Verification design`, including
method-scoped positive controls, exact counts, build provenance and any conditional
capture outcomes. Do not forward to Code merely because this Plan has a table.
TestDesign must spell out how the S0 capture gate freezes the grammar and updates
the execution selection before parser edits, without guessed live fixture text.

| ID | Obligation / detecting test | Expected baseline |
|---|---|---|
| V-1 | `CodexV0160FixtureTests.C907_raw_capture_replays_the_running_frame`, `C907_fixture_payloads_match_recorded_provenance` | Green on S0; raw/rendered mismatch is capture failure. |
| V-2 | `CodexWorkingIndicatorTests.C907_submitted_frame_is_working` | Assertion red before S1, green after. |
| V-3 | `CodexWorkingIndicatorTests.C907_captured_lifecycle_requires_seen_gone_and_settle`, `RunnerCodexAdapterTurnCompleteTests.C907_captured_lifecycle_completes_screen_fallback` | Current running frame must enable the existing lifecycle, not bypass it. |
| V-4 | `CodexV0160StartupTests.C907_running_queue_footer_blocks_ready` | Assertion red before S2 if an independent current queue footer is observed, green after. |
| V-5 | `CodexV0160StartupTests.C907_running_frame_blocks_startup`, `C907_idle_frame_requires_full_settle_after_working` | Non-ready while active; full positive settle after transition. |
| V-6 | The four trust/sign-in/update methods named in S2 | Green compatibility evidence unless a newly observed defect is isolated. |
| V-7 | The two `CodexTranscriptNormalizerTests.C907_*` methods named in S3 | Green current rollout and derived originator coverage; retain legacy flat input. |
| R-1 | `CodexWorkingIndicatorTests` and `RunnerCodexAdapterTurnCompleteTests` | Legacy lifecycle, null/empty output, transcript baseline and transcript-first authority remain. |
| R-2 | `CodexStartupReadinessTests`, `CodexReadyTrackerTests`, `CodexReadyWaitTests`, `CodexV0156StartupPromptTests` | Legacy blockers, positive settle, original deadline and restricted modal writes remain. |
| R-3 | `CodexTranscriptNormalizerTests` | Flat/thread-item latch, deduplication, usage, errors, aborted turns and re-tail remain. |

For each changed parser the red commit precedes its fix commit, and both are
pushed. CP-2 and CP-4 below are rerun on the respective red and green SHAs; preserve
the initial failure and costs. Subsequent Mutation controls, if commissioned,
revert one predicate at a time and use the same exact V-2/V-4 method filters, never
a class as the red detector. Comment-only changes do not need invented red tests.

The card-wide acceptance ledger must separately name unobserved update/sign-in
scenes, usage-limit refusal, compaction/continue, live interactive originator and
Windows geometry/backend qualification. S0 resolves interactive identity and
120x30 Linux evidence. Existing vendor status text is not a usage-limit error
capture. A vendor-derived quota/compaction fixture must say so and cite immutable
source; no live quota exhaustion or long compaction run is authorized here.
Unproven broader card rows stay incomplete for the caller's gate decision.

### Execution rules and cost

All proposed rows are **portable offline, narrow parser/adapter lanes**; no headed
CLI, production runner session, database or broad full-assembly run is selected.
The dispatch's narrow-filter scope takes precedence over the generic full-suite
recipe. Run the two main test assemblies sequentially. The capture is the sole
Linux PTY lane and is not hidden inside an ordinary unit run.

TestDesign confirms the roster and replaces the conservative execution floors
below if necessary; `Min` counts test results, not internal assertions. Planned
ordinary floor is 24 minutes (3+8+2+4+1+2+4), plus red passes (12 minutes), capture
and curation (20-40 minutes), authoring and host-slot wait. No paid replay beyond
the one capture turn is included.

Use the checkpoint tool's `run --plan <this-plan> --expected-source-sha <full-sha>`
for each committed slice group with its row selection, then keep awaiting completion
if wait returns 75. Bootstrap/build drivers go through `scripts/build-slot.ps1`;
checkpoint rows acquire their own slot. A slot timeout is not permission to run
unleased. Reuse output only within the same After/source group; after a fix use a
fresh verified build. Keep source frozen while a run is active.

Preserve unedited CHECKPOINT lines and source receipts in ignored evidence, with
executed/pass/fail/skip counts per CP. Report expected red separately from green.
Code and Review run `scripts/check-evidence-diff.ps1 -BaseRef <base> -HeadRef HEAD`.
Remove owned alternate `bin-c907-*/` outputs through the documented cleanup path.
At landability check fetch master and run `git merge-tree --write-tree origin/master HEAD`;
report conflicts without merging/rebasing the assigned task branch. Landing and
deployment remain caller actions; no RC branch is touched.

### Checkpoints

Proposed allocation for TestDesign to finalize. CP-2/CP-4 begin as expected
assertion-red runs on their tests-only commits; their acceptance Expect below is
the final green result. Every row's Group names its lane.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S0 | `tests/Antiphon.Agents.Pty.Tests -> bin-c907-fixtures/` | offline-fixture-replay | `/*/*/CodexV0160FixtureTests/*` | V-1 | all listed, 0 failed | 2 | 3 | true |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c907-working/` | offline-working-witness | `/*/*/CodexWorkingIndicatorTests/C907_submitted_frame_is_working` | V-2 | all listed, 0 failed | 1 | 8 | true |
| CP-3 | S1 | CP-2 | offline-working-adapter | `/*/Antiphon.Tests.Agents/(CodexWorkingIndicatorTests*)\|(RunnerCodexAdapterTurnCompleteTests*)/*` | V-3, R-1 | all listed, 0 failed | 14 | 2 | true |
| CP-4 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c907-startup/` | offline-queue-witness | `/*/*/CodexV0160StartupTests/C907_running_queue_footer_blocks_ready` | V-4 | all listed, 0 failed | 1 | 4 | true |
| CP-5 | S2 | CP-4 | offline-startup-frames | `/*/Antiphon.Agents.Pty.Tests/(CodexV0160StartupTests*)\|(CodexStartupReadinessTests*)\|(CodexReadyTrackerTests*)/*` | V-5, V-6, R-2 | all listed, 0 failed | 30 | 1 | true |
| CP-6 | S2 | CP-4 | offline-startup-waits | `/*/Antiphon.Agents.Pty.Tests/(CodexReadyWaitTests*)\|(CodexV0156StartupPromptTests*)/*` | R-2 | all listed, 0 failed | 2 | 2 | true |
| CP-7 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c907-rollout/` | offline-rollout-normalizer | `/*/*/CodexTranscriptNormalizerTests/*` | V-7, R-3 | all listed, 0 failed | 19 | 4 | true |
