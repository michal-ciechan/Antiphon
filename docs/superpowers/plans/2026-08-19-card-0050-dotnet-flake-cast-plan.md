# CARD-0050 — the .NET flake cast: diagnosis, first fixes, remaining slices

**Date**: 2026-08-19 · **Task**: d8a10896 · **Status**: Slices 1 and 3 shipped; slices 2, 4, 5 open.

## The scoping answer the card asked for

The cast is **not** four unrelated causes, and it is also not "one bug". It decomposes into:

1. **One dominant mechanism class** covering ~10 of the observed failures across five files:
   *process-spawn latency under saturated parallel load racing fixed real-time windows.* Under a
   full-suite run (both process-heavy projects concurrently on this 8-core machine), a cold `pwsh`
   start was **measured taking >6s** and a fakeclaude launch **>15s** — against windows of 250ms,
   1s, 5s and 15s. Every member of this class fails under load and passes in isolation by
   construction, because isolation removes the latency, not the defect.
2. **One genuine product race** the flake was correctly reporting (SessionRunnerRuntimeTests — see
   below). The card's instinct not to widen timeouts blindly is vindicated here: widening would
   have buried it.
3. **One teardown fragility** (a throwing `finally` failing a test whose assertions passed).
4. **One member with no established mechanism** (the DB channel test — did NOT reproduce in two
   load runs; its assertions turn out to be properly scoped, so the CLAUDE.md global-count
   hypothesis is *weakened*, not confirmed).

## Reproduction method (repeatable)

Build both test projects with `--property:OutputPath=bin-c50/` (daemons lock `bin/`), then run
`Antiphon.Tests.exe` and `Antiphon.Agents.Pty.Tests.exe` **concurrently**. One such run on
2026-08-19 reproduced **13 failures** in a single pass:

- `RunnerProcessProbeTests` — all six of CARD-0058's reported load-flakes:
  three "tree" tests (`Probe_timeout_kills_the_entire_process_tree`,
  `Unconfirmed_primary_cleanup…`, `Reaper_host_stop…`) failed with **the helper's pid file never
  written**: the probe's 1s timeout killed `pwsh` before the script ran at all
  (`WaitForFileAsync` → `File.Exists false`, wall 6.1–6.7s). `Invalid_utf8…` (5s timeout) timed
  out with empty output, so `OutputTruncated` read false. `Bounded_startup_that_exits_on_stdin_close`
  failed because `StopAfter=250ms` fired before pwsh reached its `ReadLine`.
  `Probe_cancellation…` **passed its assertions** and then threw
  `UnauthorizedAccessException: 'cversions.2.db'` from `Directory.Delete` in `finally` — a killed
  pwsh with the probe's bounded environment dropped shell-cache files into its scratch cwd.
- `SessionRunnerRuntimeTests.Session_id_can_be_relaunched_after_exit_but_not_while_running` —
  `InvalidOperationException: Host launch failed (alreadyLaunched): Session is Exited`. **A real
  race in the runner**, not a timing budget: the pty-host pipe name derives from the session id
  (`PtyHostProtocol.PipeNameFor`), the old host's Shutdown ack is fire-and-forget
  (`HandleExited` → `Task.Run(ShutdownHostAsync)`), so a prompt relaunch of the same id spawns a
  new host while the dying host still owns a pipe server instance with the identical name — the
  new client's connect reaches the OLD host, which rejects the launch. The same window exists in
  production `--resume` relaunches (kill → relaunch same id), where it would surface as a spurious
  launch failure plus a leaked fresh host.
- `FakeClaudeContractTests` (`Two_separate_turns_each_submit`,
  `Compact_after_turns_env_emits_compacted_after_nth_turn`),
  `ClaudeSubmitContractTests.Text_and_CR_in_one_write_does_not_submit(fakeclaude)` (ready banner
  missed at 15s), `Multi_line_paste_then_lone_cr_submits_whole_body_with_escaped_marker` — the
  last one's raw-output capture is the key evidence: **the composer held the full body and the
  lone CR never submitted**. Mechanism: the fake distinguishes Enter-vs-paste by arrival-time
  burst gap (12ms); the writer's 20ms body→CR spacing **compresses below 12ms at the reader**
  when ConPTY delivery of the body lags under load, so a legitimate two-write submit reads as one
  paste burst. The margin is structurally thin: ConPTY intra-write read jitter is ~14ms (measured,
  CARD-0028), writer spacing 20ms, threshold 12ms.
- `CodexAdapterLocalShellTests.Wait_for_ready_accepts_codex_directory_trust_prompt` — spawned
  child produced zero output inside the wait window. (Run 2 failed a *different* test in this
  file, `Question_detection_ignores_question_mark_in_prompt_echo`, the same way — this file is a
  two-time offender in two runs and the card already named it a cast member.)
- `FakeGrokContractTests.Session_id_writes_grok_session_files_under_GROK_HOME` —
  `updates.jsonl` not yet written when read. Same class.

After slice 1 (below), the **same concurrent double-run** went 13 failures → 2, all previous
members green, with two NEW rotating members failing (`PtyAgentRunnerTests.Stdin_round_trip_via_pwsh`,
`CodexAdapterLocalShellTests.Question_detection…`) — confirming both that the shipped fixes hold
under the reproduction load and that the cast rotates with saturation until the structural slice
lands.

## Slice 1 — SHIPPED (this commit)

1. **Runner relaunch race** (`SessionRunnerRuntime`): the relaunch arm of `StartAsync` now calls
   `EnsureExitedHostGoneAsync` before launching the new host — Shutdown-ack first, bounded wait
   for the old host process to exit, then a **verified** kill (pid must still be an
   `Antiphon.PtyHost`; pid-reuse counts as gone). Safe by construction: only ever runs with the
   child already exited, so no session survival is forfeited. The flaking test is the regression
   pin; it cannot pass while the pipe race exists under load.
2. **Probe tests**: default probe timeout 5s→30s (it is a runaway bound — success path returns on
   child exit, so this costs nothing); the three tree-timeout tests 1s→10s (the timeout must fire
   *after* the tree exists, and the only lever is outlasting the measured >6s cold start);
   `StopAfter` 250ms/1s→8s for the two bounded-startup tests (same constraint — cannot be gated
   on the pid file); scratch teardown is now retrying/best-effort (`DeleteScratchBestEffort`), so
   shell-cache droppings from a killed child can't fail a green test from `finally`. These are
   widened budgets **with the mechanism established**, which is exactly the case the card
   distinguishes from blind widening.
3. **fakeclaude evidence trail** (CARD-0050's original lead): every transcript append now stamps a
   `<path>.timing` sidecar (process-start marker, per-record wall clock, share-mode retry count,
   explicit `GAVE-UP` marker on the silent-drop path), and `WaitForTranscriptLinesAsync` fails
   *with the sidecar attached* on a deadline miss — so the next natural occurrence of the
   "one record short" shape self-diagnoses late-vs-lost-vs-starved instead of needing another
   investigation pass. The test poll also reads with `FileShare.ReadWrite` now, so polling can no
   longer starve the fake's writer into its 100-retry give-up (a real silent-loss path: after 100
   `IOException`s the record was dropped forever).

## Remaining slices

- **S2 — CodexAdapterLocalShellTests** (two distinct tests failed across two runs): establish this
  file's wait-window inventory the way slice 1 did for the probe file; separate "runaway bound"
  windows (widen freely) from "scenario needs the deadline" windows (gate or measure). Ship with
  a load-run before/after count.
- **S3 — SHIPPED (echo-gated submit helper)**: `EchoGatedSubmit` writes the body, waits for
  `ComposerDeliveryEvidence` on the rendered screen, then sends CR. Two-write FakeClaude /
  ClaudeSubmit tests moved onto it; the one-write paste arm is untouched (time-based — a single
  write can only be split). Ready-banner wait 15s→45s (runaway bound; launch measured >15s).
  FakeGrok `updates.jsonl` now polls with `FileShare.ReadWrite` and a `.timing` sidecar (S1's
  transcript-wait shape). `ANTIPHON_FAKE_BURST_MS` left at 12ms. Concurrent double-run 17→8;
  every named S3 member green. The remaining 8 rotate (S2 CodexAdapter, AgentTui windows, two
  PtyInputChunking/large-write tests).
- **S4 — AgentChannelServiceIntegrationTests**: mechanism still unestablished (0 reproductions in
  2 load runs). Static analysis this pass ruled OUT the CLAUDE.md global-count class: the mention
  target lookup is scoped to the per-harness runtime's live-session ids and the test's own board,
  and the assertions are per-adapter. Next step is instrumentation, not a guess: on `WaitUntilAsync`
  timeout, report which pipeline stage was reached (pending-mention debounce fired? route command
  dequeued? DB source/target queries returned? event published?) so the next natural failure names
  its stage. Suspect ranking: shared-testcontainer query latency under load inside the 5s window.
- **S5 — the structural end-state** (this is what actually stops the cast rotating): decide the
  concurrency lane for process-spawning tests. Concretely evaluate: (a) TUnit `ParallelLimiter`
  capping concurrent process-spawning tests per assembly (pty runners, probe, codex, session
  runtime); (b) full-suite runner guidance to not co-schedule the two process-heavy projects at
  full width; (c) whether the delegate-brief "known flaky list" can then be DELETED — the list is
  itself the rot CARD-0045 exists to stop. Measure: 3 consecutive concurrent double-runs green.

## For whoever picks up S2–S5

- Reproduction is cheap and reliable: the concurrent double-run above reproduced 13 failures in
  one 5-minute pass. Do not accept "passes in isolation" as evidence for this card.
- `bin-c50/` build dirs must be deleted after the work (`Get-ChildItem -Recurse -Depth 2
  -Directory -Filter bin-c50 | Remove-Item -Recurse -Force`) — and never with a trailing
  backslash on the OutputPath (see CLAUDE.md).
- The FakeClaudeContractTests transcript-row lead the card opens with (cold file cache on first
  JSON serialization) did not reproduce in either load run *with the sidecar armed* — if it recurs,
  the sidecar now captures the whole answer. Do not re-derive it from scratch.
