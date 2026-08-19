# CARD-0050 — the .NET flake cast: diagnosis, first fixes, remaining slices

**Date**: 2026-08-19 · **Task**: d8a10896 · **Status**: Slices 1–5 implemented; S5 concurrent 3-green bar not met (see S5).

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

## Slice 1 — SHIPPED (`3f792ec`)

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

## Slice 2 — SHIPPED (`e091acc`)

`CodexAdapterLocalShellTests` wait-window inventory (this file only). Concurrent double-run
before this slice: **8 failures** (5 + 3), 3 of them this file, all empty snapshots in 1.74–3.06s
(QuietPeriod 750ms of zero output — MaxWait never ran). After: **4 failures** (1 + 3), **this
file 3 → 0**. The remaining 4 are S1 residual (`Probe_cancellation…`) and rotating S3 members.

| Window | Class | Action |
|---|---|---|
| `CodexReadyMaxWaitMs` / `CodexDoneMaxWaitMs` 15s → 60s | runaway | widened (success returns on quiet) |
| `CodexReadyQuietPeriodMs` / `CodexDoneQuietPeriodMs` 750ms | scenario-gated | kept — already the ConPTY-echo floor (250ms flaked). Stretching this only delays the same false ready |
| `WaitUntilSnapshotContainsAsync` 60s | runaway + gate | new. Empty+quiet and title-only both return ready before the body exists |
| `KillAsync(2s)` / `ShouldBeLessThan(2.5s)` / `Delay(300)` | runaway / scenario-gated / settle | untouched — kill test did not fail under load |

Measured: first ConPTY write under load was the cmd **title** at **2321ms**; batch body still
absent at **6549ms** (title→body gap >4.2s, CARD-0015 shape). Any-byte gate was not enough;
expected-text gates (`>` / `1. Yes, continue` / `READY_AFTER_TRUST`) wait for the body.

## Slice 3 — SHIPPED (`8d6e517`)

**Echo-gated submit helper**: `EchoGatedSubmit` writes the body, waits for
`ComposerDeliveryEvidence` on the rendered screen, then sends CR — mirrors production's
`VerifiedPromptSubmitter` (evidence-gated, not time-gated). Two-write FakeClaude / ClaudeSubmit
tests moved onto it; the one-write paste arm is untouched (time-based — a single write can only be
split, never merged). Ready-banner wait 15s→45s (runaway bound; launch measured >15s).
FakeGrok `updates.jsonl` now polls with `FileShare.ReadWrite` and a `.timing` sidecar (same trail
S1 gave FakeClaude), with retry-then-give-up on `IOException`. `ANTIPHON_FAKE_BURST_MS` left at
12ms — the [15,19]ms window between read jitter and writer spacing is too thin to tune. Concurrent
double-run 17→8; every named S3 member green. The remaining 8 rotated outside this slice (S2
CodexAdapter, AgentTui windows, two PtyInputChunking/large-write tests).

## Slice 4 — SHIPPED (`2721c6c`)

**`AgentChannelServiceIntegrationTests` instrumentation**: mechanism still did not reproduce (0/2
load runs, and 0/2 further runs during this slice). Static analysis confirmed the CLAUDE.md
global-count hypothesis does not apply here — the mention-target lookup is scoped to the
per-harness runtime's live session ids and the test's own board, assertions are per-adapter.
Since the failure won't reproduce on demand, the slice shipped instrumentation instead of a guess:
`MentionRouteDiagnostics` (256-entry ring buffer, append-only, wrapped so a diagnostics failure can
never affect routing) records every pipeline stage (`delta-observed` → `pending-scheduled`/
`pending-cleared` → `debounce-delay-started` → `debounce-fired`/`stale`/`cancelled`/`no-mentions`/
`not-ready` → `command-enqueued`/`dropped`/`skipped-duplicate` → `command-dequeued` →
`route-started` → `source-query-returned` → `target-query-returned` → `input-sent` →
`event-published` → `route-returned`/`route-failed`). On a `WaitUntilAsync` timeout the assertion
now prints the last stage reached, the full trail, and relevant session/event state, so the next
natural occurrence self-diagnoses instead of needing another investigation pass. `_diagnostics` is
nullable, defaults to `null`, and is not registered in production DI — true no-op outside tests.
Isolated `AgentChannelServiceIntegrationTests` 6/6; the target test passed in two further
concurrent double-runs (one other test in the same file, `Channel_delegate_claims_via_optimistic_concurrency`,
failed once under load — untouched by this slice, treated as a rotating member, not investigated
here).

## Slice 5 — IMPLEMENTED (sequential 3-green; concurrent bar not met)

**(a) + (b).** TUnit `[ParallelLimiter<ProcessSpawnLimit>]` with `Limit = 1` on every
process-spawning class in `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` (the limiter is
per-process). CLAUDE.md now says do not co-schedule those two projects: Limit=2, Limit=1, and
Limit=1 plus `--maximum-parallel-tests 4` / `2` on `Antiphon.Tests` all still rotated FakeClaude
under a concurrent double-run. Three consecutive **sequential** pair-runs (Antiphon.Tests, then
Pty.Tests) were green: 1682/1686 + 237/277 each time (4 + 40 skipped, headed).

**(c) not deleted, because it never lived in the repo.** The "known flaky list" is informal
delegate-brief prose, not a `server/Bundles/` artifact. The standing CLAUDE.md line that told
delegates some Antiphon.Tests are "PTY-timing flaky under full parallel load" is gone; a failure
in the lane is a real defect unless it also fails at the base commit. Do not restore a named
ignore-list.

The plan's own concurrent 3-green measure did **not** land. New rotating members seen only under
concurrent load, not chased: `FakeClaudeContractTests` (submit/clip windows),
`FakeGrokContractTests.Body_then_separate_CR_submits`,
`AgentStartRecoveryTests.Interactive_start_failure_marks_agent_failed_not_working` (not a
process-spawner — Start returned Failed before Running).

## Notes left for the orchestrator

- Reproduction is cheap and reliable: the concurrent double-run still rotates FakeClaude under
  saturation. Sequential pair-runs (Antiphon.Tests, then Pty.Tests) are the configuration S5
  proved green 3/3. Do not accept "passes in isolation" as evidence that concurrent is fixed.
- `bin-c50*` build dirs must be deleted after the work (`Get-ChildItem -Recurse -Depth 2
  -Directory -Filter bin-c50* | Remove-Item -Recurse -Force`) — and never with a trailing
  backslash on the OutputPath (see CLAUDE.md).
- The FakeClaudeContractTests transcript-row lead the card opens with (cold file cache on first
  JSON serialization) did not reproduce in either load run *with the sidecar armed* — if it recurs,
  the sidecar now captures the whole answer. Do not re-derive it from scratch.
