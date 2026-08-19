# CARD-0052 — Slow start reads as done: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0052 (`f8a5de64-1cc7-4fb1-8587-5ec4804a516c`) — ready/done detectors accept
silence as evidence; a slow child start reads as a completed empty turn
**Filed from:** CARD-0048 decision 3 (`docs/superpowers/specs/2026-08-16-card-0048-da1-answer.md`)
and investigation §8–9 (`docs/investigations/2026-08-16-modern-conpty-da1-stall-CARD-0048.md`).
The DA1 fix removed the known 3 s instance. This card is the backend-agnostic class: any slow
start (cold AV scan, WSL, loaded box, a future handshake) is still silence, and silence is
still treated as ready/done.
**Precedent:** CARD-0048 required a *positive* DA1 handshake before the modern child was
usable, and deliberately did not widen quiet windows. CARD-0050 S2 measured that stretching
`QuietPeriod` only delays the same false ready, and that an any-byte gate just moves it from
an empty snapshot to a title-only OSC.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**A minimum-wait floor is the wrong lever.** CARD-0048 already rejected widening the
constants (it hides the exposure and taxes every turn). After DA1 there is no stall bound to
derive a floor from — healthy modern start is ~40 ms; the next slow start is unbounded.
Claude's `MinTotalWait` 9 s is a measured WebSocket-connect fact, not a stall floor, and it
does not make Claude ready *sound*: 5 s of silence still counts, then the remainder of 9 s
is slept. A 20 s AV scan still types the first prompt into a child that has not started.

**The structural requirement is the one CARD-0048 named and CARD-0050 refined:** quiet
cannot count until the snapshot has **visible child output** (ANSI-stripped, so host init CSI
and OSC titles do not qualify). An empty or title-only snapshot must never be
`TurnCompleted: true`. Do not change `WaitForQuietAsync` itself — it is a primitive ("has
the buffer been unchanged for T") used by canaries and CARD-0048's own stall pin. Add a
wrapper and wire the quiet-only ready/done paths onto it.

One Code slice.

## 1. Current shape (verified against the files, 2026-08-19)

`PtyAgentRunner.WaitForQuietAsync` (`src/Antiphon.Agents.Pty/PtyAgentRunner.cs:239`) and
`RunnerTerminalSession.WaitForQuietAsync` (`server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs:102`)
both start `lastChange = UtcNow` on entry. Zero output for `quietPeriod` returns `true`.
That is the defect, in one place, consumed by every quiet-only detector.

### 1.1 Who is vulnerable

| Detector | File | Signal | Vulnerable? |
|---|---|---|---|
| `CodexReadyDetector.WaitAsync` | `src/Antiphon.Agents.Pty/CodexDetectors.cs:16` | own loop; `lastChange` starts at t=0; 1 s quiet → ready | **Yes.** Empty+quiet is ready. `MaxWait` never runs. |
| `RunnerCodexAdapter.WaitForReadyAsync` | `…/RunnerCodexAdapter.cs:80` | same loop on sequence | **Yes.** |
| `CodexDoneDetector` / `CodexAdapter.WaitForTurnCompleteAsync` | `CodexDetectors.cs:54` / `CodexAdapter.cs:109` | `WaitForQuietAsync` 3 s | **Yes.** `TurnCompleted: true` on a still-empty snapshot. |
| `RunnerCodexAdapter.WaitForTurnCompleteAsync` | `…/RunnerCodexAdapter.cs:110` | same | **Yes.** |
| `RunnerOpenCodeAdapter` ready + done | `…/RunnerOpenCodeAdapter.cs:79` / `:107` | Codex-shaped quiet | **Yes.** |
| `RawPtyAdapter.WaitForReadyAsync` | `server/Infrastructure/Agents/Pty/RawPtyAdapter.cs:85` | `WaitForOutputAsync(_ => true, ReadyGrace)` then `return true` | **Yes, and worse than the card says.** `_ => true` matches the empty snapshot on the first poll. `ReadyGrace` 500 ms is dead. Ready is **instant**. |
| `RunnerRawAdapter.WaitForReadyAsync` | `…/RunnerRawAdapter.cs:77` | identical `_ => true` | **Yes, instant.** |
| `RawPtyAdapter` / `RunnerRawAdapter` `WaitForTurnCompleteAsync` | `:94` / `:84` | `WaitForQuietAsync` 2 s; `TurnCompleted: quiet` with raw snapshot | **Yes.** This is the card title: completed empty turn. |
| `ClaudeReadyDetector` / `ClaudeAdapter` / `RunnerClaudeAdapter` ready | `ClaudeDetectors.cs:27` / `ClaudeAdapter.cs:108` / `RunnerClaudeAdapter.cs:123` | 5 s quiet, then `MinTotalWait` 9 s from start; trust-dialog check after | **Partially.** Safe against a 3 s stall by luck. A start slower than 9 s still types into a silent child. Trust-dialog `None` on a not-yet-started TUI looks like a healthy idle session (CARD-0047 shape). |
| `ClaudeCrunchedDetector` / both Claude `WaitForTurnCompleteAsync` | `ClaudeDetectors.cs:84` / `ClaudeAdapter.cs:133` / `RunnerClaudeAdapter.cs:196` | OSC idle title `ESC]0;✳` or ` for \d+s` | **No.** Positive evidence. `ClaudeDoneDetector` (quiet 3 s) exists in the library and is **not** wired; the `ClaudeAdapter` header comment that claims a quiet fallback is stale. |
| `RunnerGrokAdapter.WaitForReadyAsync` | `…/RunnerGrokAdapter.cs:142` | 1 s quiet + `MinTotalWait` 2 s | **Yes**, same as Claude-ready without the 9 s luck. |
| `RunnerGrokAdapter.WaitForTurnCompleteAsync` | `:171` | transcript `TurnEnd` primary (CARD-0080 S2); screen done-line / idle title / **quiet fallback** | Primary **no**. Quiet fallback (`:201`) **yes** — empty+quiet sets `screenDone` and, after the transcript grace, returns `TurnCompleted: true`. |

`ProviderContractCatalog` already labels Codex / OpenCode / Raw turn completion
`Degraded` / `QuietTimeOnly`. This card does not promote them to structured; it makes the
degraded signal stop lying.

### 1.2 User-visible path

Card launches (`AgentSessionService`, `:184–216`): `WaitForReadyOrThrowAsync` → boot prompt
→ `WaitForFirstPromptOutputAsync` (any chunk, including a title OSC; default
`FirstDeltaTimeoutMs` 5 s) → `WaitForTurnCompleteAsync`. `TurnCompleted: true` marks the
`RunAttempt` **Succeeded** and runs after-hooks. A slow Codex/Raw start that emits a title
then goes quiet therefore succeeds with an empty body. `TurnCompleted: false` is a timeout
and a kill — so a *post-filter* that flips empty+quiet to false after the wait has already
returned would kill a child whose body is 1 s away (CARD-0050 measured title at 2321 ms,
body still absent at 6549 ms). The gate has to live **inside** the wait.

Always-on working/idle does **not** use these detectors (transcript rules). Do not reopen
CARD-0055.

### 1.3 Why a floor fails, in numbers we already have

- CARD-0048 decision 3: do not move `CodexReadyQuietPeriodMs` 1000,
  `CodexDoneQuietPeriodMs` 3000, `ClaudeReadyQuietPeriodMs` 5000, `ReadyGrace` 500,
  `TurnQuietPeriod` 2 s. ADR `docs/adr/0002-modern-conpty-backend.md` records the same.
- CARD-0050 S2 (`CodexAdapterLocalShellTests.cs:54–69`): empty+quiet fired ready/done in
  1.74–3.06 s with snapshot `""`. An any-byte gate then fired on `ESC]0;cmd.exe - …` while
  the batch body was still 4 s away. Stretching `QuietPeriod` "only delays that same false
  ready."
- Claude `MinTotalWait` is documented as the backend WebSocket connect (`AgentRegistrySettings.cs:12–18`),
  not as a generic stall floor. Do not add a `CodexReadyMinTotalWaitMs`.

## 2. The fix — visible output, then quiet

### 2.1 Shared definition

New small helper next to `AnsiStripper` (`src/Antiphon.Agents.Pty/`, name
`VisiblePtyOutput` or similar):

```
HasVisibleOutput(snapshot) =>
    !string.IsNullOrWhiteSpace(AnsiStripper.Clean(snapshot))
```

`AnsiStripper` already drops CSI (`ESC[1t`, `ESC[c`, `ESC[?1004h`, `ESC[?9001h`) and OSC
(`ESC]0;…BEL` titles). That is the CARD-0048 "init burst" plus the CARD-0050 title-only
case, with no new parser and no "what counts as host init?" debate. Visible text (`HELLO`,
`>`, `❯`, a Codex banner, a response line) is life.

Do **not** treat raw length / sequence advancement as life. A title OSC advances both.

### 2.2 New wait, old primitive stays

Add `WaitForQuietAfterVisibleAsync(quietPeriod, maxWait, ct)` on **both**
`PtyAgentRunner` and `RunnerTerminalSession` (keep them lockstep, same as today's
`WaitForQuietAsync`):

1. Poll until `HasVisibleOutput` is true. Clock does not start.
2. Then wait `quietPeriod` of no further buffer/sequence change.
3. If `maxWait` expires before (1) or (2): return `false`.

`WaitForQuietAsync` is unchanged. `ModernPtyDa1Tests.WaitForQuiet_on_modern_returns_false_from_launch_under_continuous_output`
and `PtyAgentRunnerTests.WaitForQuiet_*` stay on the primitive.

### 2.3 Wire the quiet-only paths onto it

| Call site | Change |
|---|---|
| `CodexReadyDetector.WaitAsync` | replace the t=0 `lastChange` loop with the helper (or the same rule in-place; helper preferred). Trust-prompt observe callback stays. |
| `CodexDoneDetector.WaitAsync` | `WaitForQuietAfterVisibleAsync` |
| `ClaudeReadyDetector.WaitAsync` | quiet half → helper; **keep** `MinTotalWait` after it (WebSocket fact). |
| `ClaudeDoneDetector` | helper, even though production does not call it, so the library type does not remain a footgun. |
| `RunnerCodexAdapter` ready + done | helper |
| `RunnerOpenCodeAdapter` ready + done | helper |
| `RunnerClaudeAdapter.WaitForReadyAsync` | quiet half → helper; keep trust-dialog + `MinTotalWait` |
| `RunnerGrokAdapter.WaitForReadyAsync` | helper + existing `MinTotalWait` 2 s |
| `RunnerGrokAdapter` screen quiet fallback (`:201`) | only set `screenDone` from quiet when `HasVisibleOutput`; transcript primary unchanged |
| `RawPtyAdapter` / `RunnerRawAdapter` `WaitForTurnCompleteAsync` | helper; `TurnCompleted` is the helper's bool |
| `RawPtyAdapter` / `RunnerRawAdapter` `WaitForReadyAsync` | **special case.** Raw shells may be silent. Contract in the class comment is already "first chunk or 500 ms grace, still ready." Implement that for real: wait for `HasVisibleOutput` **or** `ReadyGrace`, then `return true`. Do not leave `_ => true`. Do not require visible output forever. |

Do not post-filter `TurnCompleted = quiet && HasVisibleOutput(snapshot)` after a
`WaitForQuietAsync` that already returned. That converts a late body into a timeout-and-kill
(`AgentSessionService` `:218–225`).

### 2.4 What ready-false / done-false now mean

- Ready returns `false` only if `MaxWait` (60 s Codex/OpenCode/Claude/Grok) expired with no
  visible output. `WaitForReadyOrThrowAsync` already throws "Agent process did not become
  ready." That is the honest failure.
- Done returns `TurnCompleted: false` only if `MaxWait` (60 s raw / 5 min Codex) expired
  without visible-then-quiet. That is a real timeout, not a 2 s empty success.

## 3. Tests — pin the empty-turn, not a log line

New focused tests; do not widen existing local-shell waits.

**No pty (library):**

| Test | Pins |
|---|---|
| `HasVisibleOutput` false on `""`, host-init CSI (`ESC[1t ESC[c ESC[?1004h ESC[?9001h`), and a lone OSC title | CARD-0050 title-only is not life |
| `HasVisibleOutput` true on `> `, `HELLO`, mixed title+body | prompt / body counts |

**Pty, `[Category("Pty")]` `[ParallelLimiter<ProcessSpawnLimit>]`:** a batch that
`ping -n 5` (~4 s) then `echo SLOW_START_BODY`, with quiet 500–800 ms and maxWait 15 s,
started immediately after `StartAsync`:

| Test | Pins | Red today? |
|---|---|---|
| `CodexAdapter` / `RawPtyAdapter` `WaitForReadyAsync` returns true **after** `SLOW_START_BODY` is in the snapshot (or, for raw, after grace if the test uses a silent `/k`) | ready does not fire in the sleep | **Yes** (Codex empty-ready; Raw instant-ready) |
| `WaitForTurnCompleteAsync` after `SendPrompt` on that slow child is **not** `TurnCompleted: true` with a stripped-empty snapshot; when it does complete, snapshot contains `SLOW_START_BODY` | the card title | **Yes** |
| `WaitForQuietAsync` (primitive) on a silent `ping -n 3` still returns true after `quietPeriod` | primitive unchanged | No |
| `WaitForQuietAfterVisibleAsync` on the same silent ping returns false if maxWait < the echo | helper requires life | **Yes** if anyone called the primitive and treated it as done |
| Raw `WaitForReadyAsync` on `cmd /d /q /k` with no output still returns true by `ReadyGrace` | silent shell stays valid | No (already true — the pin is "still true, and not instant" if we can observe elapsed ≥ grace without failing load) |
| `RunnerGrokAdapter` quiet fallback: no transcript, empty snapshot, short maxWait → `TurnCompleted: false` | CARD-0080 fallback must not resurrect the hole | **Yes** (today quiet sets `screenDone`) |

Extend `tests/Antiphon.Agents.Pty.Tests/` for the helper + `HasVisibleOutput`.
Extend `tests/Antiphon.Tests/Agents/RawPtyAdapterTests.cs` and
`CodexAdapterLocalShellTests.cs` (already `[NotInParallel("Pty")]`) for the adapter pins.
Extend `RunnerGrokAdapterTurnCompleteTests.cs` for the fallback (no pty — fake client).

```
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0052/ -- --treenode-filter "/*/*/<NewHelperTests>/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0052/ -- --treenode-filter "/*/*/RawPtyAdapterTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0052/ -- --treenode-filter "/*/*/CodexAdapterLocalShellTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0052/ -- --treenode-filter "/*/*/RunnerGrokAdapterTurnCompleteTests/*"
```

`Antiphon.Tests` classes **one after the other**, never co-scheduled with
`Antiphon.Agents.Pty.Tests`. Delete `bin-card0052/` afterwards.

## 4. Out of scope

- Widening any quiet / max-wait / `MinTotalWait` constant. ADR and CARD-0048 stand.
- Giving Codex / OpenCode / Raw a structured turn-end. Catalog stays `QuietTimeOnly`.
- Changing `ClaudeCrunchedDetector` or wiring `ClaudeDoneDetector` into production
  adapters.
- Changing always-on working/idle (transcript). CARD-0055 stays closed.
- Reopening CARD-0048 / CARD-0049 / CARD-0015 / CARD-0050. Those pins stay; this card
  consumes their measurements.
- Grok transcript path. Only the screen quiet fallback.
- Closing or moving CARD-0052. This plan lands; a Code slice implements.

## 5. Slice

One Code slice, in this order: `HasVisibleOutput` + its unit tests (red on title-only),
`WaitForQuietAfterVisibleAsync` on both runners + the slow-start pin, then wire the table
in §2.3 (done paths first — that is the card title — then ready, then raw grace), then the
remaining adapter pins. Verify with the four `dotnet run` lines in §3.

Not two slices. The helper is the whole fix; splitting ready from done would leave the
frozen-child first prompt in place after "the empty turn" looks fixed, and CARD-0048 filed
them as one class.
