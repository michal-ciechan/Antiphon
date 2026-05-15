# E01 — PTY substrate + Windows headed-agent proof

> **Status:** `[x]` **Closed 2026-05-15.**
> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md) · **Test strategy:** [`tests/Antiphon.Agents.Pty.Tests/TEST-STRATEGY.md`](../../../../tests/Antiphon.Agents.Pty.Tests/TEST-STRATEGY.md)

---

## Goal

Stand up a permanent, library-grade PTY substrate (`Antiphon.Agents.Pty`) capable of:

1. Spawning arbitrary console processes (cmd, pwsh, claude, codex) under Windows ConPTY.
2. Streaming stdout/stderr as a byte stream into an in-memory ring buffer with structured reader access.
3. Accepting stdin (raw bytes + line-mode), supporting interactive TUIs.
4. Detecting **ready** (process settled into idle prompt) and **done** (response complete) for headed claude sessions.
5. Killing the child cleanly within 2s on demand, with no orphaned conhost / claude processes.
6. Surviving repeated spawn/dispose cycles without handle leaks.

**Why this isn't a throwaway spike:** every later epic (E02 agent abstraction, E05 lifecycle, E08 xterm.js streaming, E11 amux channels, E18 watchdog) sits on top of `PtyAgentRunner`. Bugs here cascade. We treat E01 as production code with the test-strategy coverage in `TEST-STRATEGY.md` (≥40 tests across Unit / Pty / PtyStress / Headed / HeadedLong categories).

**Covers requirements:** FR-04, FR-06 (output streaming substrate), FR-07 (stdin + resize substrate), FR-09 (kill), NFR-01 (PTY confined to one library), NFR-06 (ring buffer), NFR-08 (stall-timeout substrate via WaitForQuiet helpers).

**Architecture placement:** library `src/Antiphon.Agents.Pty/` — referenced later by `Antiphon.Server.Infrastructure` (NFR-01). No domain types leak in.

**Out of scope (this epic):** SignalR streaming, persistent disk spill (NFR-06 disk side), JobObject memory cap (NFR-05), worktree creation (E03), agent abstraction layer / `IAgentProtocolAdapter` (E02), watchdog rules (E11/F18).

---

## Architectural Decisions Captured

- **Library: Porta.Pty 1.0.7** — actively maintained, ConPTY-backed. Microsoft `Pty.Net` is unlisted (last published 2018). Decision rationale + fallback options in `TEST-STRATEGY.md` and findings doc (S08).
- **Test framework: xUnit** — matches existing `Antiphon.Tests` / `Antiphon.E2E`. Migration to TUnit + Shouldly tracked separately under E14, sequenced immediately after this epic.
- **No CLI driver project** — all manual scenarios are xUnit `[Fact]`s under `Category=Headed`. One way to invoke: `dotnet test`.
- **Single test project** (`Antiphon.Agents.Pty.Tests`) — categories (`Unit`, `Pty`, `PtyStress`, `Headed`, `HeadedLong`) gate cost. Default CI excludes Headed.

---

## Stories

### Foundational

- **E01-S01** `[x]` Stand up `Antiphon.Agents.Pty` library + `Antiphon.Agents.Pty.Tests` project. Spawn `cmd.exe /c <bat>` via Porta.Pty, capture exit code + stdout, kill in <2s.
  - Work items:
    - Add `Porta.Pty` PackageReference. Confirm ConPTY backend on Win10 1809+.
    - Implement `PtyAgentRunner.StartAsync(app, args[], cwd, env, cols, rows, ct)` returning `Task<int> Exited`.
    - Implement `RingBuffer<T>` (FIFO, fixed capacity, overwrite-oldest, snapshot copy, thread-safe).
    - Implement `KillAsync(timeout)` returning bool (true if exited within timeout).
    - *TDD:* `RingBuffer_overwrites_oldest_when_full`, `PtyAgentRunner_can_spawn_and_capture_known_exit_code`, `PtyAgentRunner_kill_terminates_within_2s`. ✅ all green.
    - Wire projects into `Antiphon.sln`. Default CI runs them.

### Streaming + I/O

- **E01-S02** `[x]` Structured stdout/stderr streaming with ring buffer + live tail.
  - Work items:
    - `PtyAgentRunner.OnData : event Action<string>` fires per chunk (bytes already decoded UTF-8).
    - `Output : RingBuffer<string>` keeps last N chunks for late-attach replay.
    - `SnapshotText()` returns concatenated live buffer; `ClearLiveBuffer()` resets between prompts.
    - *TDD:* `OnData_invokes_each_subscriber_for_every_chunk`, `Output_ringbuffer_keeps_last_N_chunks_under_high_volume`, `SnapshotText_returns_full_concat_of_chunks_in_order`, `ClearLiveBuffer_does_not_affect_ringbuffer`.

- **E01-S03** `[x]` stdin support: raw `WriteAsync(string)` + line mode `SendLineAsync(line)` with `\r` line ending.
  - Work items:
    - Validate large writes (64KB single call) round-trip via echo child.
    - Validate concurrent reads + writes do not interleave / corrupt.
    - *TDD:* `Stdin_round_trip_via_echo_child`, `Stdin_64KB_write_not_truncated`, `WriteAsync_before_StartAsync_throws_InvalidOperationException`.

- **E01-S04** `[x]` Terminal resize (`Resize(cols, rows)`).
  - Work items:
    - Wire `IPtyConnection.Resize`. Validate child receives `SIGWINCH`-equivalent (cmd: just no throw; claude TUI: re-renders to new width).
    - *TDD:* `Resize_after_start_does_not_throw`, `Resize_to_extreme_values_clamped_or_rejected_cleanly`.

### Lifecycle + safety

- **E01-S05** `[x]` Lifecycle hardening — IDisposable / IAsyncDisposable correctness.
  - Work items:
    - `DisposeAsync` cancels read loop, awaits read task, disposes connection, never deadlocks even mid-stream.
    - `Pid` exposed after Start, null before.
    - `StartAsync` second call on same instance throws.
    - `Exited` task completes exactly once with the exit code.
    - *TDD:* `Dispose_mid_read_returns_within_3s`, `StartAsync_called_twice_throws`, `Pid_null_before_start_set_after`, `Exited_task_completes_exactly_once`.

- **E01-S06** `[x]` Wait helpers: `WaitForOutputAsync(predicate, timeout)` + `WaitForQuietAsync(quietPeriod, maxWait)`.
  - Work items:
    - Predicate variant: poll snapshot every 50ms, returns true on match, false on timeout.
    - Quiet variant: returns true after `quietPeriod` of no buffer growth, false if `maxWait` elapses with continued output.
    - Both must honour caller `CancellationToken`.
    - *TDD:* `WaitForOutput_matches_within_budget`, `WaitForOutput_returns_false_on_timeout`, `WaitForQuiet_detects_quiet_after_burst`, `WaitForQuiet_returns_false_under_continuous_output`, `Wait_helpers_honour_cancellation_token`.

- **E01-S07** `[x]` Env + cwd propagation.
  - Work items:
    - Pass `IDictionary<string,string>` env to child; verify via `cmd /c set MY_VAR`.
    - Pass `cwd` distinct from current directory; verify via `cmd /c cd`.
    - *TDD:* `Env_vars_propagate_to_child`, `Cwd_honoured_by_child`.

### Stress + leak

- **E01-S08** `[x]` Spawn/dispose loop — no process or handle leak.
  - Work items:
    - Run spawn → wait exit → dispose × 50 sequentially.
    - Snapshot `Process.GetProcessesByName("conhost")` count before/after, diff < 5 (allow noise).
    - Snapshot handle count via `Process.GetCurrentProcess().HandleCount` before/after, diff bounded.
    - *TDD:* `Spawn_dispose_loop_does_not_leak_processes`, `Spawn_dispose_loop_handle_count_bounded`. Marked `[Trait("Category","PtyStress")]`.

- **E01-S09** `[x]` Rapid kill — kill before first read returns exit code cleanly.
  - Work items:
    - Spawn long-running cmd, kill within 50ms × 20 iterations.
    - All return non-null exit code, no hang.
    - *TDD:* `Rapid_kill_before_first_read_succeeds_x20`. `PtyStress` category.

- **E01-S10** `[x]` High-volume stdout — 1MB output captured without OOM.
  - Work items:
    - Spawn cmd that loops `echo` for ~1MB output.
    - Verify no exception, ring buffer respects capacity (last N entries only), live snapshot still readable.
    - *TDD:* `Child_emitting_1MB_does_not_OOM_runner`. `PtyStress` category.

### Headed claude

> All `[Trait("Category","Headed")]`. Skipped unless: (a) Windows, (b) `cl.bat` on PATH, (c) `ANTIPHON_HEADED_TESTS=1`. Each costs API tokens.

- **E01-S11** `[x]` `cl --version` smoke via PTY exits 0 with version string.
  - Work items:
    - Resolve `cl.bat` location.
    - Spawn with `--version` arg, await exit, parse output.
    - *TDD:* `Cl_version_via_pty_exits_zero_with_version_string`.

- **E01-S12** `[x]` `cl --print "say PONG"` non-interactive prompt round-trip.
  - Work items:
    - Spawn with `-p "say PONG"`, capture, await exit.
    - Output (ANSI-stripped) contains "PONG" case-insensitive.
    - *TDD:* `Cl_print_mode_returns_response`.

- **E01-S13** `[x]` Headed TUI ready detection.
  - Work items:
    - Spawn `cl` (no args, full TUI), wait quiet 1.5s within 30s budget.
    - `WaitForQuietAsync` returns true → considered ready.
    - *TDD:* `Cl_tui_reaches_ready_within_30s`.

- **E01-S14** `[x]` Single-prompt round-trip in headed session.
  - Work items:
    - Reach ready, `ClearLiveBuffer`, send "hi\\r", wait quiet 3s within 60s.
    - Snapshot contains response (lowercase greeting heuristic).
    - *TDD:* `Cl_headed_single_prompt_hi_returns_response`.

- **E01-S15** `[x]` Sequential two-prompt session: continuity within same PTY.
  - Work items:
    - "hi" → done, then "what is 2+2" → done, output contains "4".
    - Validates that claude maintains context across prompts in the same PTY (no `--resume` needed).
    - *TDD:* `Cl_headed_sequential_prompts_share_context`.

- **E01-S16** `[x]` Tool-using prompt: "run `systeminfo` via Bash and tell me the OS".
  - Work items:
    - Send prompt, wait done with longer budget (3min).
    - ANSI-stripped output contains "Windows".
    - *TDD:* `Cl_headed_tool_use_returns_systeminfo`.

- **E01-S17** `[x]` `/exit` clean shutdown + kill mid-flight.
  - Work items:
    - Reach ready, send `/exit\\r`, await `Exited` within 5s.
    - Separate test: send a long prompt, kill mid-stream, verify no orphan claude.exe in process list.
    - *TDD:* `Cl_headed_exit_command_completes_in_5s`, `Cl_headed_kill_midflight_no_orphans`.

- **E01-S18** `[x]` `--resume <session-id>` continuity across runner instances. `Category=HeadedLong`.
  - Work items:
    - Run 1: send "remember the word 'banana'". `/exit`. Capture session-id from stdout (claude prints `claude --resume <id>` on exit).
    - Run 2: spawn with `--resume <id>`. Send "what word should you remember?". Output contains "banana".
    - *TDD:* `Cl_headed_resume_preserves_context_across_runner_instances`.

### Helpers

- **E01-S19** `[x]` `AnsiStripper` — remove CSI/OSC sequences for log-friendly assertions.
  - Work items:
    - Implement minimal regex-based stripper (CSI `\\x1b[...]` final byte, OSC `\\x1b]...\\x07|\\x1b\\\\`).
    - *TDD:* `AnsiStripper_removes_color_codes`, `AnsiStripper_removes_cursor_moves`, `AnsiStripper_removes_OSC_titles`, `AnsiStripper_passthroughs_plain_text`.

- **E01-S20** `[x]` `TempBatch` test helper — disposable `.bat` file generator. Avoids cmd `&` quoting issues seen in initial spike.
  - Work items:
    - `using var bat = new TempBatch("@echo off\\necho hi\\nexit /b 7");` writes to temp, deletes on dispose.
    - `bat.Path` string usable as cmd `/c` arg.
    - *TDD:* `TempBatch_writes_and_cleans_up`, `TempBatch_runs_via_cmd`.

- **E01-S21** `[x]` `ClaudeReadyDetector` + `ClaudeDoneDetector` — wraps `WaitForQuietAsync` with default budgets and centralises tuning. Future upgrade path: parse status line (`Opus 4.7 | in:N out:N`) instead of quiet heuristic.
  - Work items:
    - `await detector.WaitForReady(runner, ct)` with defaults (1.5s quiet / 30s max).
    - `await detector.WaitForDone(runner, ct)` with defaults (3s quiet / 2min max).
    - Configurable via constructor for slow links / large prompts.
    - *TDD:* `ReadyDetector_returns_true_when_runner_settles`, `DoneDetector_returns_true_after_response_completes`.

### Documentation

- **E01-S22** `[x]` Findings doc [`E_01-findings.md`](E_01-findings.md). Captures Windows-specific learnings:
  - winpty vs ConPTY decision, why Porta.Pty wins over Microsoft Pty.Net.
  - ConPTY → conhost.exe sidecar process model, JobObject implications for NFR-05 (deferred to later epic).
  - Porta.Pty quoting quirks (`&&` not safe in `CommandLine` args; use batch files).
  - ANSI noise volume from claude TUI (~50KB+ per prompt cycle); ring buffer sizing implications.
  - Quiet-period heuristic limitations (false-positive if claude pauses mid-stream); upgrade path to status-line parsing.
  - Performance: handle/process leak measurements from S08; spawn latency baseline.
  - Doc-only story; no TDD per `06-test-strategy.md §2` exception.

---

## Acceptance

- All stories' tests green in CI under `Category=Unit|Pty|PtyStress` filter (default).
- Manual run with `ANTIPHON_HEADED_TESTS=1` and `Category=Headed` filter passes on dev machine; permission-menu tests self-skip when Claude Code reports `bypass permissions on`, because no approval prompt is available in that local TUI mode.
- `Antiphon.Agents.Pty` library has zero references back to `Antiphon.Server` (one-way dependency).
- `tests/Antiphon.Agents.Pty.Tests/TEST-STRATEGY.md` reflects implemented surface and matches story coverage.
- Findings doc shipped (S22).

---

## Dependencies + Hand-off

- **Blocks:** E02 (agent abstraction wraps `PtyAgentRunner`), E05 (lifecycle uses `Exited`/`OnData`/`KillAsync`), E08 (xterm.js consumes `OnData` via SignalR), E11 (amux channels feed stdin via `WriteAsync`).
- **Blocked by:** none. This epic is the foundation.
- **Hand-off artefacts:** `PtyAgentRunner` public surface, `RingBuffer<T>`, helper detectors, findings doc, test strategy.

---

## Risks + Mitigations

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| Porta.Pty abandoned mid-build | low | Library is small (~1k LOC); fork acceptable. Fallback: Microsoft `Pty.Net` (still works on .NET 9 despite unlisted). |
| ConPTY behavioural drift across Windows feature updates | low | Stress tests (S08-S10) run on every CI; deviation surfaces fast. |
| Quiet-heuristic false positives on slow claude responses | medium | S21 detector tunable; upgrade path to status-line parsing documented in S22. |
| Headed test cost (API tokens) | low if env-gated | `Category=Headed` excluded from default CI; manual + nightly only. |
| Conhost.exe orphan accumulation under repeated kill | medium | S08 explicitly measures; fix in `DisposeAsync` if found. |
| Porta.Pty quoting wraps multi-arg commands → cmd `&&` breaks | known | Use `TempBatch` (S20) for any multi-step shell commands in tests. |
