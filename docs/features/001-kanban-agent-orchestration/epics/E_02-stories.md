# E02 — Agent abstraction (`IAgentProtocolAdapter` + `AgentRegistry`)

> **Status:** `[x]` **Closed 2026-05-15.**
> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked · `[-]` dropped.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md) · **Findings:** [E_02-findings.md](E_02-findings.md)

**Goal:** clean seam between server and PTY/protocol details so Codex app-server, Claude TUI, raw shells all plug in via one interface, while keeping `Antiphon.Agents.Pty` confined to `Infrastructure/` (NFR-01).

**Covers:** FR-04, FR-05, FR-06 (substrate-side; SignalR fan-out is E05/E08), NFR-01.

**Decisions taken** (documented in [E_02-findings.md](E_02-findings.md)):

1. **Single seam at protocol level.** `PtyAgentRunner` stays concrete in its own assembly (already isolated). Variation point is *protocol* (raw vs claude TUI vs codex JSON), not *PTY transport*. No `IPtyAgentRunner` interface.
2. **Two adapters land here:** `RawPtyAdapter` (passthrough, quiet-period turn complete) + `ClaudeAdapter` (wraps existing `ClaudeReadyDetector`/`ClaudeCrunchedDetector`/`ClaudeResponseAnalyzer`). Proves the seam against ≥2 real protocols.
3. **Registry lives in `appsettings.json`** under `Agents` section, bound to `IOptions<AgentRegistrySettings>` per Rule #7. Validated on startup via `AgentRegistrySettingsValidator : IValidateOptions<T>` + `ValidateOnStart()`.
4. **Registry owns resolution:** `AgentRegistry.Resolve(name, AgentLaunchOptions) → AgentLaunchSpec`. Factory just switches on resolved `AgentKind`.

---

## Stories

- **E02-S01** `[x]` Domain `AgentKind` enum (`Raw`, `ClaudeCode`); `AgentLaunchOptions` + `AgentLaunchSpec` DTOs; `AgentRegistrySettings` + `AgentDefinition`; `AgentRegistrySettingsValidator`; `AgentRegistry.Resolve`; `appsettings.json` Agents section + example mirror.
  - Tests: `AgentRegistrySettingsTests` (binds, validator success + 5 failure modes), `AgentRegistryTests` (lookup hits/misses, resolve merges definition + options, defaults, env override, dim validation), `AgentLaunchSpecTests` (record equality, defaults).
- **E02-S02** `[x]` `IAgentProtocolAdapter` + `IAgentProtocolAdapterFactory` interfaces in `Application/Interfaces/`. `AgentTurnResult` record. Compile-only; behaviour proved in S03/S04.
- **E02-S03** `[x]` `RawPtyAdapter` impl in `Infrastructure/Agents/Pty/`.
  - Tests: `RawPtyAdapterTests` against real `cmd.exe` — start+capture, send-prompt round-trip, kill <2s, dispose <3s, snapshot delegation, double-start guard, pre-start guard.
- **E02-S04** `[x]` `ClaudeAdapter` impl wrapping existing detectors; `IOptions<AgentRegistrySettings>` injected for tunable timing.
  - Tests: `ClaudeAdapterLocalShellTests` against real `cmd.exe` emitting synthetic ` for Ns` markers — clear-buffer-before-send proven across two turns, marker detection, max-wait timeout. No fake `PtyAgentRunner` needed.
  - Tests (env-gated, headed): `ClaudeAdapterIntegrationTests.Full_round_trip_via_seam_returns_response_text` exercises real `cl.bat`/`claude` PONG round-trip when `ANTIPHON_HEADED_TESTS=1`.
- **E02-S05** `[x]` `AgentProtocolAdapterFactory` + DI wiring in `Program.cs`: `AddOptions<AgentRegistrySettings>().Bind(...).ValidateOnStart()`, validator + registry + factory registered, post-build `GetRequiredService` proves the graph resolves.
  - Tests: `AgentProtocolAdapterFactoryTests` — Raw/ClaudeCode return correct concrete types, unmapped kind throws.
- **E02-S06** `[x]` `Antiphon.Agents.Pty` ProjectReference added to `server/Antiphon.Server.csproj`. One-way dependency confirmed (`rg "Antiphon\.Server" src/Antiphon.Agents.Pty` → 0 matches).
- **E02-S07** `[x]` Findings doc [`E_02-findings.md`](E_02-findings.md) capturing the four design decisions and any runtime quirks.

---

## Acceptance

- ✅ `dotnet build Antiphon.sln` green, zero warnings on new files.
- ✅ `dotnet run --project tests/Antiphon.Tests` — 135 pass, 1 skipped (headed integration), 0 failed.
- ✅ `dotnet run --project tests/Antiphon.Agents.Pty.Tests` — 74 pass + 22 skipped (no regression).
- ✅ Boundary checks all green: no PTY types in `Domain/` or `Application/Interfaces/`, no `IPtyAgentRunner` interface, no `Antiphon.Server` refs from `Antiphon.Agents.Pty`.
- ✅ Server restarts cleanly; `AgentRegistrySettingsValidator` runs at startup; `IAgentProtocolAdapterFactory` resolves.

---

## Out of Scope

- SignalR fan-out of `OnTextDelta` (E05/E08).
- `AgentSession` domain entity + lifecycle state machine (E04 + E05).
- Worktree creation / cwd resolution (E03).
- Codex `app-server` adapter, Gemini, Aider, Cursor (post-MVP — interface ready).
- Stream-json adapter for Claude (`--output-format=stream-json`) — future, once heuristic limits bite.
- Resize plumbing through interface (no consumer yet).
- Token usage extraction (E13 observability).
- Hot-reload of registry (E09 covers via WorkflowDefinition; agent registry can opt in later).
