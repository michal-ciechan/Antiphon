# E02 Findings — Agent Abstraction

> Captures the design decisions and runtime quirks discovered while standing up `IAgentProtocolAdapter` + `AgentRegistry`. Sibling to [E_02-stories.md](E_02-stories.md).

---

## 1. Single seam at the protocol level (not the transport)

**Decision:** there is exactly one I/O-seam interface for agents — `IAgentProtocolAdapter` — and `PtyAgentRunner` stays concrete inside the adapter implementation.

**Rationale:**

- `Antiphon.Agents.Pty` is already isolated in its own assembly (NFR-01). Pulling Porta.Pty types into `Application/` to mock the runner would defeat that.
- The variation point downstream consumers care about is *protocol behaviour* — when is an agent ready, when is a turn done, what is the response text, did it ask a question — not *PTY transport*. Codex/Claude/Aider all use the same Porta.Pty primitive but differ in protocol parsing.
- Per project Enforcement Rule #2, interfaces exist *only* for external I/O seams. `IPtyAgentRunner` would be a service-pair interface (one impl, mocked for tests), which is exactly what Rule #2 forbids.

**Consequence:** adapter unit tests run against real shells (`cmd.exe`/`pwsh.exe`) — not against a fake runner. This proved cheap (Tier 2 PTY tests run in <2s each) and high-fidelity.

---

## 2. Adapters get protocol-aware unit coverage via synthetic markers

**Problem:** how to cover `ClaudeAdapter` without burning API tokens on every CI run?

**Solution:** spawn a real interactive `cmd.exe`, then send commands that emit literal text matching `ClaudeCrunchedDetector`'s ` for \d+s` regex — e.g. `echo synthetic_resp_marker for 2s`. The adapter sees the same signal it would from a real Claude TUI completing a turn.

**Trade-off:** does not exercise OSC-title detection (`\x1b]0;✳`) — only the regex fallback path. Headed integration test (`Full_round_trip_via_seam_returns_response_text`) covers OSC behaviour against live `cl.bat`. Trade is acceptable: the OSC path is one `WaitForOutputAsync` predicate clause in the existing detector, already covered by `ClaudeDetectorsTests`. Adapter just composes the detector — no new logic.

---

## 3. Registry config stays in `appsettings.json`, not a separate `agents.json`

**Decision:** `Agents` section in `appsettings.json` bound via `IOptions<AgentRegistrySettings>`.

**Rationale:**

- Existing settings (`Git`, `Llm`, `SignalR`, `Audit`, `GitHub`) all live in the same file. A separate `agents.json` would split config without benefit.
- `IOptions` + section binding gives env-var override (`Agents__Definitions__claude__Exe=...`) for free — important for containerised deployment where `cl.bat` path differs.
- Hot-reload is not needed yet (E09 covers WorkflowDefinition reload separately). When/if the agent registry needs hot-reload, switching `IOptions<T>` to `IOptionsMonitor<T>` is mechanical — `AgentRegistry` already takes `IOptionsMonitor<AgentRegistrySettings>` so reloads land for free.

---

## 4. Registry owns resolution; factory only switches on `AgentKind`

**Pattern:**

```
caller → registry.Resolve(name, options) → AgentLaunchSpec
       → factory.Create(spec.Kind) → IAgentProtocolAdapter
       → adapter.StartAsync(spec, ct)
```

**Why:** keeps the factory dependency-free (no `IConfiguration`, no `AgentRegistry` ref) and makes the launch flow grep-able. Adapters never read config — they get a fully-resolved immutable `AgentLaunchSpec` and run with it.

**Subtle:** the factory does take `IOptions<AgentRegistrySettings>` so it can pass tuning to `ClaudeAdapter`'s constructor. That is acceptable — it's *adapter construction* config, not *launch* config. Launch config lives entirely in the resolved spec.

---

## 5. Fail-fast on startup, not first use

`AddOptions<AgentRegistrySettings>().ValidateOnStart()` plus an explicit post-build `GetRequiredService<AgentRegistry>()` + `GetRequiredService<IAgentProtocolAdapterFactory>()` mean a misconfigured `Agents` section kills the host on boot rather than crashing the first kanban card that spawns an agent. Validator catches: empty definitions dict, blank exe, unknown `Kind`, duplicate definition names (case-insensitive), `DefaultDefinition` not in dict, non-positive timing values.

---

## 6. Quirks observed during implementation

- **`PtyAgentRunner.StartAsync` second-call guard** is a hard `InvalidOperationException("Already started")`. Adapters wrap with their own `_started` flag so they get a meaningful exception (`"RawPtyAdapter already started"`) before delegating. Cheap; helps stack traces in callers.
- **`PtyAgentRunner.OnData` is a multicast event** — adapter forwards to `OnTextDelta` via a stored `Action<string>` so we can `_runner.OnData -= ForwardData` cleanly on dispose. Without storing the delegate, the unsub silently no-ops. This bit during initial dev.
- **`ClearLiveBuffer` in `SendPromptAsync` is mandatory for Claude** (per E01 findings — `ClaudeCrunchedDetector` matches on previous-turn signals otherwise). `RawPtyAdapter` clears too, for consistency and so `WaitForTurnCompleteAsync.ResponseText` doesn't include prior-turn output. Tested explicitly in `ClaudeAdapterLocalShellTests.Send_prompt_clears_live_buffer_before_send`.
- **`PtyAgentRunner.WaitForOutputAsync(_ => true, ...)`** is the cheapest "did anything arrive" probe. `RawPtyAdapter.WaitForReadyAsync` uses it with a 500 ms grace and unconditionally returns true — raw shells may have nothing to print before they're ready for input.

---

## 7. Hand-off to E03 / E04 / E05

- `IAgentProtocolAdapter` is the surface E05 will wire to SignalR (`OnTextDelta` → hub method).
- `AgentSession` (E04) entity will hold `DefinitionName` + `AgentLaunchOptions` (cwd from worktree, cols/rows from client) and re-resolve via `AgentRegistry` on resume.
- `AgentLaunchSpec.Cwd` is the worktree path E03 will produce — already plumbed end-to-end, no change needed.
- Future Codex adapter slots into `AgentKind.Codex` + a new factory branch + an `Infrastructure/Agents/Pty/CodexAdapter.cs`. No interface change required for codex's app-server protocol if it can map onto the same `WaitForReady`/`WaitForTurnComplete`/`SendPrompt` lifecycle. If not, add an orthogonal seam later — don't pre-extend now.
