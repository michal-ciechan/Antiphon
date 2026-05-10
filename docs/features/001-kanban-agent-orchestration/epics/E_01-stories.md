# E01 — Pty.Net spike + Windows headed-agent proof

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** prove vs-pty.net can spawn `claude` / `codex` headed on Windows, stream stdout/stderr, accept stdin, kill cleanly.

**Covers:** FR-04, NFR-01

---

## Stories

- **E01-S01** `[ ]` Console spike `tools/PtySpike/` launches `claude --version` via Pty.Net, captures output, exits 0.
  - Work items:
    - Add `Pty.Net` NuGet (latest from microsoft/vs-pty.net). *TDD:* xUnit test `PtyAgentRunner_can_spawn_and_capture_known_exit_code` runs `cmd.exe /c exit 42` and asserts exit code + captured stdout.
    - Spike capturing stdout/stderr to in-memory ring buffer. *TDD:* test `RingBuffer_overwrites_oldest_when_full`.
    - Send Ctrl-C / kill. *TDD:* test `PtyAgentRunner_kill_terminates_within_2s`.
- **E01-S02** `[ ]` Document Windows-specific findings (winpty vs conhost path, JobObject for memory cap) in `epics-E01-pty-spike.md`.
  - Work items:
    - Write findings doc. *TDD:* exception — doc-only story; mark per `06-test-strategy.md §2`.
