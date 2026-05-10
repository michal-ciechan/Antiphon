# E02 — Agent abstraction (`IPtyAgentRunner` + `IAgentProtocolAdapter`)

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** clean seam between domain and PTY/protocol details so Codex app-server, Claude JSON-stream, RawPty all plug in.

**Covers:** FR-04, FR-05, FR-06, NFR-01

---

## Stories

- **E02-S01** `[ ]` `IPtyAgentRunner` interface in `Application/Interfaces/`.
  - Work items:
    - Define interface (`StartAsync`, `WriteInputAsync`, `ResizeAsync`, `KillAsync`, `OutputStream`). *TDD:* compile-time: contract test `IPtyAgentRunner_contract` (mocked impl asserts each method invoked).
- **E02-S02** `[ ]` `IAgentProtocolAdapter` interface + `RawPtyAdapter` impl.
  - Work items:
    - Adapter interface with `OnTextDelta`, `OnTurnComplete`, `OnError`. *TDD:* test `RawPtyAdapter_emits_text_delta_per_chunk` with fake stream.
    - Token usage extraction stub (returns null for raw PTY). *TDD:* test `RawPtyAdapter_token_usage_is_null`.
- **E02-S03** `[ ]` `AgentRegistry` JSON config (`agents.json`): name, exe, args template, auto-prompt rules.
  - Work items:
    - DTO + loader. *TDD:* test `AgentRegistry_loads_known_agents_from_json` parses sample with claude+codex+gemini.
    - Validation: missing exe = error. *TDD:* test `AgentRegistry_rejects_missing_exe_path`.
- **E02-S04** `[ ]` Pty.Net implementation in `Infrastructure/Agents/PtyAgentRunner.cs`.
  - Work items:
    - Wire Pty.Net to interface. *TDD:* integration test `PtyAgentRunner_echo_stdin_round_trips` writes "hello\n" to `cmd.exe`, asserts "hello" appears in output.
    - Resize handling. *TDD:* test `PtyAgentRunner_resize_does_not_crash_running_process`.
