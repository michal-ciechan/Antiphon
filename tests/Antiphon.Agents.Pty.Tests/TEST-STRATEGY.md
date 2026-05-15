# Antiphon.Agents.Pty — Test Strategy

> Scope: epic E01 (PTY substrate + Windows headed-agent proof). Validates that we can spawn `claude` / `codex` via ConPTY (Porta.Pty), stream I/O, detect lifecycle states, and clean up.

---

## 1. Project Layout

| Project | TFM | Purpose |
|---------|-----|---------|
| `src/Antiphon.Agents.Pty` | net9.0 | Library — `PtyAgentRunner`, `RingBuffer`, `TerminalScreen`, ANSI helpers, claude detectors |
| `tests/Antiphon.Agents.Pty.Tests` | net9.0 | Single test project covering Unit, Pty, PtyStress, Headed, HeadedLong tiers |

Rationale:
- One test project; tiers separated by TUnit `[Category(...)]` and a runtime `SkipTestException` gate (`ClSession.SkipIfNotEligible`) for headed scenarios.
- Avoids duplicate csproj plumbing and mirrors the layout of `Antiphon.Tests` / `Antiphon.E2E`.
- Tests reference the library directly; no production code path.

---

## 2. Test Categories

Filter via TUnit `[Category("<x>")]` attribute on test methods/classes.

| Category | Speed | Network/API | Default? |
|----------|-------|-------------|----------|
| `Unit` | <50ms | none | yes |
| `Pty` | <2s | none (cmd.exe / pwsh.exe) | yes |
| `PtyStress` | up to 30s | none | yes (CI) |
| `Headed` | 10s–3min | LLM proxy + tokens | env-gated (skipped at runtime) |
| `HeadedLong` | up to 10min | LLM proxy + tokens | env-gated (skipped at runtime) |

Filter examples (TUnit uses Microsoft Testing Platform — `dotnet run`, not `dotnet test`):
```
dotnet run --project tests/Antiphon.Agents.Pty.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"
dotnet run --project tests/Antiphon.Agents.Pty.Tests -- --treenode-filter "/*/*/*/*[Category=Headed]"
```

Headed tiers always *compile* in the default suite; they self-skip at runtime via `ClSession.SkipIfNotEligible()` unless the env gate is set.

---

## 3. Naming Conventions

- Class: `<Subject>Tests` (e.g. `PtyAgentRunnerTests`, `RingBufferTests`, `ClaudeHeadedTests`).
- Method: `Subject_action_expectedOutcome` (snake-ish per E01 spec).
- One arrange-act-assert per `[Test]`. Use `[Arguments(...)]` for parameterised input.
- Assertions via Shouldly (`x.ShouldBe(y)`, `x.ShouldContain(y)`). One assertion library only.
- Test files mirror SUT layout 1:1.

---

## 4. What We Test (per layer)

### 4.1 `RingBuffer<T>` — Unit

- under-capacity preserves insertion order
- exactly-at-capacity: full snapshot, no overwrite
- over-capacity overwrites oldest, preserves N most recent
- `Capacity` immutable, `Count` clamped to capacity
- thread-safe Add + Snapshot under contention (parallel `Task.Run` × N writers)
- snapshot returns *copy* (mutation of snapshot doesn't affect buffer)

### 4.2 `PtyAgentRunner` — Pty (cmd.exe / pwsh.exe based)

| Scenario | Cmd | Asserts |
|----------|-----|---------|
| spawn + capture exit | `cmd /c <bat with exit /b 42>` | exit=42, stdout contains marker |
| stdin echo round-trip | `cmd` interactive, send `echo X\r exit\r` | output contains `X` |
| kill mid-flight | `cmd /c ping -n 60 127.0.0.1` | killed within 2s |
| multiple sequential prompts in same session | pwsh prompt loop | both replies captured |
| ring buffer fills with chunks | high-output cmd (`dir /s C:\Windows\System32 \| more`) | no exception, last chunks present |
| process exit fires `Exited` task once | exit normally | `Exited.Task` completes once |
| dispose mid-read does not deadlock | spawn long-running, dispose, assert timeout | DisposeAsync returns < 3s |
| large stdin write | send 64 KB in one `WriteAsync` | no truncation, all bytes seen by child (echo) |
| resize after start | spawn `cmd`, call `Resize(80,24)` then `Resize(200,60)` | no throw, child still responsive |
| WaitForOutputAsync predicate match | predicate sees marker | returns true within budget |
| WaitForOutputAsync timeout | predicate never matches | returns false at deadline |
| WaitForQuietAsync detects quiet | bursty output then silence | returns true after quiet period |
| WaitForQuietAsync timeout under continuous output | endless stream | returns false at maxWait |
| StartAsync twice throws | second call without dispose | `InvalidOperationException` |
| WriteAsync before Start throws | new instance | `InvalidOperationException` |
| Pid exposed after Start | spawn, check `runner.Pid` | non-null, > 0 |
| env vars propagate to child | spawn `cmd /c set MY_VAR` with env dict | output contains `MY_VAR=value` |
| cwd honored | spawn `cmd /c cd` with `cwd=temp` | output contains temp path |
| concurrent OnData subscribers | 3 handlers attached | each receives all chunks |

### 4.3 `PtyAgentRunner` — PtyStress

- spawn + dispose 50× sequentially: no handle/process leak (assert via `Process.GetProcesses()` count delta < 5)
- 1MB stdout from child: full capture (use bat that loops echo), ring buffer holds last N, no OOM
- rapid kill (spawn + immediate kill before first read) × 20: all return exit code

### 4.4 Claude headed — Headed

> Requires `cl.bat`/`cl.ps1` or globally-installed `claude` on PATH. Uses `--print` mode where possible to keep cost down.

| Scenario | Asserts |
|----------|---------|
| `cl --version` via PTY | exits 0, output contains version string |
| `cl --print "say PONG"` | exit 0, output contains `PONG` (allow case-insensitive) |
| TUI starts and reaches ready (quiet 1.5s) | `WaitForQuietAsync` returns true within 30s |
| send "hi" → quiet detected, output contains lowercase greeting | done within 60s |
| sequential 2-prompt session: "hi" then "what is 2+2" | both responses captured, "4" appears |
| `/exit` clean shutdown | `Exited` completes within 5s, exit 0 |
| kill mid-response | runner kills within 2s, no orphan claude.exe |
| readiness vs done heuristic stability (flaky guard) | run sequence 3× back-to-back, all succeed |
| long prompt: "list 10 short bullet points about ConPTY" | response captured, contains ≥5 bullet markers |
| ANSI stripping helper produces clean text | `AnsiStripper.Clean(snapshot)` removes `\e[...m` |

### 4.5 Claude headed — HeadedLong (opt-in, costly)

| Scenario | Asserts |
|----------|---------|
| 10-prompt rolling chat | each prompt resolves, no session crash, transcript ordered |
| tool-using prompt: "run `systeminfo` via Bash and tell me OS" | response contains `Windows` |
| concurrency: 2 PTY claudes at once, each gets 1 prompt | both finish, outputs distinct |
| resume via `--resume <session-id>` after exit | second runner picks up context (asks "what was last question" → matches) |

---

## 5. Heuristics & Helpers (built alongside tests)

- `AnsiStripper` — strip CSI/OSC sequences for log-readable assertions
- `ClaudeReadyDetector` — wraps `WaitForQuietAsync` with sensible defaults
- `ClaudeDoneDetector` — quiet-period strategy now, upgrade later to status-line parse (`Opus 4.7 | in:N out:N`) when we have ANSI parser
- `TempBatch` — disposable helper that writes a `.bat` and deletes on dispose (for exit-code tests, avoids cmd `&` quoting issue)
- `ClSession` — central headed-eligibility gate (`SkipIfNotEligible`) + `cl`/`claude` binary resolution

---

## 6. CI Gates

- PR build: `dotnet run --project tests/Antiphon.Agents.Pty.Tests` — `Unit` + `Pty` + `PtyStress` execute; `Headed`/`HeadedLong` self-skip without `ANTIPHON_HEADED_TESTS=1`. Hard gate.
- Nightly: same command with `ANTIPHON_HEADED_TESTS=1` set in environment → `Headed` runs. Soft gate (alert, not block).
- Manual / pre-release: env flag set + `--treenode-filter` selecting `HeadedLong`. Eats tokens; explicit run.

---

## 7. Skip Conditions

Headed tests call `ClSession.SkipIfNotEligible()` at the top of each test. It throws `TUnit.Core.Exceptions.SkipTestException` (reported as **skipped**, not failed) when:

- non-Windows host (ConPTY only)
- `ANTIPHON_HEADED_TESTS != 1`
- neither `cl.bat`/`cl.ps1`/`cl.cmd`/`cl.exe` nor `claude.*` resolvable on PATH

Inline `throw new SkipTestException("...")` is used for ad-hoc per-scenario gates (e.g. API down probe).

---

## 8. Out of Scope (this epic)

- codex agent (separate spike; same harness reused)
- Linux/macOS PTY (Porta.Pty supports it; deferred until Antiphon runs in Linux container)
- Docker isolation tests (epic E03+)
- Memory cap via JobObject (covered in E01-S02 doc, not test)
