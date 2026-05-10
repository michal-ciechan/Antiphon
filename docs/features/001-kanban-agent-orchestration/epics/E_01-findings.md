# E01 — Findings: PTY substrate on Windows

> Companion to [E_01-stories.md](E_01-stories.md). Captures decisions and surprises that surfaced while building `Antiphon.Agents.Pty`.

---

## 1. Library choice: Porta.Pty over Microsoft Pty.Net

| Library | Status | Verdict |
|---------|--------|---------|
| **Porta.Pty 1.0.7** ([nuget](https://www.nuget.org/packages/Porta.Pty), [tomlm/Porta.Pty](https://github.com/tomlm/Porta.Pty)) | active, MIT | **chosen** |
| Microsoft `Pty.Net` ([microsoft/vs-pty.net](https://github.com/microsoft/vs-pty.net)) | unlisted, last published 2018 | viable fallback |
| `Quick.PtyNet` (+ `Quick.PtyNet.WinPty`) | active | viable, two-package install |
| `DQD.ForkPTY`, `PtySharp` | smaller usage | not evaluated |

**Why Porta.Pty:** active maintenance, single package, Windows ConPTY backend (no winpty), .NET Standard 2.0 → works on .NET 9 with no compatibility shim, clean `IPtyConnection` API (`ReaderStream`, `WriterStream`, `Resize`, `Kill`, `WaitForExit`, `ProcessExited` event).

**Fallback path:** if Porta.Pty becomes unmaintained, Microsoft Pty.Net still works on .NET 9 despite being unlisted on NuGet. Library is small (~1k LOC); a fork is a viable contingency.

---

## 2. ConPTY architecture (Windows 10 1809+)

```
[Antiphon] -> Porta.Pty -> ConPTY (kernel32) -> conhost.exe (sidecar) -> claude.exe / cmd.exe / pwsh.exe
```

- ConPTY is a **Pseudo Console** API: `CreatePseudoConsole`, `ResizePseudoConsole`, `ClosePseudoConsole`.
- Spawns a headless `conhost.exe` per child to translate legacy console API calls into a VT byte stream.
- Replaces the pre-1809 winpty hack (which scraped a hidden conhost screen buffer).
- Linux/macOS equivalent: `forkpty()` / `openpty()` (POSIX). Porta.Pty bridges both.
- Implication for **NFR-05** (memory cap via JobObject): JobObject must be applied to the ConPTY-spawned process tree, not just the immediate child. Deferred to a later epic.

---

## 3. Porta.Pty quirks observed

### 3.1 `CommandLine` arg-array quoting

`PtyOptions.CommandLine` is `string[]`. Porta.Pty re-quotes each element and concatenates before `CreateProcess`. Multi-token shell tricks **break**:

```csharp
// BAD — cmd sees the second arg as a single quoted token "echo X && exit 42"
new[] { "/c", "echo X && exit 42" }
//   → cmd error: '"echo X && exit 42"' is not recognized

// GOOD — single command via batch file
using var bat = new TempBatch("@echo off\r\necho X\r\nexit /b 42\r\n");
new[] { "/d", "/c", bat.Path }
```

`TempBatch` (test helper, `S20`) sidesteps this entirely. Consider it the canonical pattern for any multi-step shell command in tests.

### 3.2 No native stdout/stderr split

ConPTY collapses stdout + stderr into one VT stream (it's a TTY, after all). If we ever need them separated (we don't, for headed agents), would need redirection at the child or a wrapper script.

---

## 4. ANSI noise volume

Headed claude TUI emits **~50KB+ of ANSI per prompt cycle** for cursor moves, color updates, animated spinners, status-line redraws. Implications:

- `RingBuffer<string>` capacity of 4096 chunks (4KB each) is enough for ~16MB of trailing context — sufficient even for long sessions.
- Live `StringBuilder` grows unbounded between `ClearLiveBuffer()` calls — caller must reset between prompts, or memory will balloon over a multi-prompt session.
- Plain-text assertion against captured snapshot **must** strip ANSI first (`AnsiStripper.Clean`); raw text contains escape-laden gibberish.

---

## 5. Readiness + done detection

### 5.1 Current heuristic: quiet period

- **Ready:** `WaitForQuietAsync(quiet=1.5s, max=30s)` — true when stdout stops growing for 1.5s.
- **Done:** `WaitForQuietAsync(quiet=3s, max=2min)` — same mechanism, longer thresholds.

Wrapped in `ClaudeReadyDetector` and `ClaudeDoneDetector` so call sites don't tune timeouts ad-hoc.

### 5.2 Limitations

- **False positive on slow-streaming claude:** if claude pauses >quiet period mid-response (e.g., during tool use), we declare "done" prematurely. Mitigated by the 3s threshold for done; if it bites in practice, raise to 5s.
- **False positive during initial render:** claude TUI animates spinners (`Dilly-dallying.`, `Sublimating.`, etc.) which keep the buffer growing — this actually helps ready detection (we wait until spinners stop).
- **No structured signal:** stream-json mode (`--output-format stream-json --input-format stream-json --verbose`) gives JSON events for ready/done. Future upgrade path. Trade-off: loses TUI rendering, becomes invisible to xterm.js consumers (E08).

### 5.3 Upgrade options (post-MVP)

1. **Status-line parser:** claude renders `Opus 4.7 | in:N out:N cr:N cw:N | $X.XXXX | ctx:NN%` after each response. Parse the in/out token counts to detect "done" deterministically. Requires ANSI parser, not just stripper.
2. **Stream-json side-channel:** spawn second claude in stream-json mode for orchestration signal, headed claude for UI. Doubles cost.
3. **`--print` mode:** non-interactive, exits per prompt → done = exit. Loses interactive session continuity unless paired with `--resume`. Cheap to test (S12).

---

## 6. Process lifecycle observations

### 6.1 Spawn-dispose × 50 (test S08)

Measured on Win11 26100, .NET 9.0.14:
- `conhost.exe` count delta: **0–2** (well under 10 threshold)
- handle count delta: **<200** (under 500 threshold)
- No `claude.exe` orphans (when running with cl.bat parent)

Conclusion: ConPTY teardown is clean. `IPtyConnection.Dispose()` reliably reaps the child + conhost sidecar.

### 6.2 Rapid kill × 20 (test S09)

`KillAsync(timeout=3s)` consistently returns `true` within ~50–300ms even when called immediately after `StartAsync` (before first read). No hangs, no zombie processes.

### 6.3 1MB stdout (test S10)

16384 echo lines (~1MB) captured cleanly:
- No OOM
- Ring buffer caps at 4096 chunks (last ~16MB of stream context)
- Live `StringBuilder` holds full output (~500KB after CR/LF normalisation)
- Last lines preserved (`line-16384` survives in snapshot)

For sessions emitting >100MB cumulative, we'll need disk spill (NFR-06) — not in scope for E01.

### 6.4 `/exit` shutdown latency

claude TUI processes `/exit` and exits within **3–5s** typically. Test S17a budget of 5s is safe.

### 6.5 Mid-flight kill: no orphans

After `runner.KillAsync()` mid-response, `claude.exe` count returns to baseline within 2s (test S17b).

---

## 7. Test strategy outcomes

- 45 default-CI tests (Unit + Pty + PtyStress + ClaudeDetectors): green, ~25s wall time.
- Headed tests env-gated via `ANTIPHON_HEADED_TESTS=1` + `cl.bat` on PATH detection. Default CI never triggers them.
- `Xunit.SkippableFact` works fine for runtime gating but adds a NuGet dep — flagged for removal in E14 (TUnit migration; TUnit has built-in skip).
- FluentAssertions v8 emits a commercial-licensing warning on every test run. Decided in E14 to swap → Shouldly (BSD-3, no nag).

---

## 8. Open questions for E02 (agent abstraction layer)

- Should `IAgentProtocolAdapter` model "ready / sending / waiting / done" as states or events?
- Where does `ClaudeReadyDetector` live — generic in `Antiphon.Agents.Pty` or per-agent in `Antiphon.Agents.Claude`?
- For codex / gemini / aider, do they all settle on quiet-period detection or do some emit deterministic prompt markers we can grep?

These belong in E02 design discussion, not E01 implementation.

---

## 9. References

- Porta.Pty: https://github.com/tomlm/Porta.Pty · https://www.nuget.org/packages/Porta.Pty
- Microsoft Pty.Net (fallback): https://github.com/microsoft/vs-pty.net
- Windows ConPTY API: https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session
- VT escape sequences: https://learn.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences
