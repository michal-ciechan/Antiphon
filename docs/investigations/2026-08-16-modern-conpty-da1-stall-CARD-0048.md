# CARD-0048 — root cause PROVEN: OpenConsole blocks the child until its DA1 query is answered

**Verdict:** the standing hypothesis is **correct and now proven**, with the controls that could have
falsified it run and failed to reproduce the effect.

`OpenConsole.exe` (the console host behind the shipped `conpty.dll`) emits `ESC[c` (DA1,
primary device attributes) at startup and **does not let the console client run at all** until either
a DA1 response arrives on the pty input pipe or a **~3.0 s** timeout expires. Our stack never sends a
DA1 response, so every child on the modern backend is frozen for ~3.0 s before executing its first
instruction. The inbox conhost sends no such query and has no such wait.

The card's framing ("first response to TYPED input") is one symptom, not the mechanism — the stall
happens with **no input at all**. That is why `WaitForQuiet_returns_false_under_continuous_output`,
which never types anything, is a casualty.

---

## 1. The measurement (Antiphon.Agents.Pty.Tests, `PtyAgentRunner(backendOverride)` — no env needed)

Child: `cmd /d /c <bat>` where the bat loops `echo noisy-%random%` forever. Offsets are ms from
`StartAsync`.

| backend | trace |
|---|---|
| inbox  | `+90 ms` `ESC[2J ESC[m ESC[H noisy-22630…` — child output immediately |
| modern | `+30 ms` `ESC[1t`; `+31 ms` `ESC[c ESC[?1004h ESC[?9001h`; **silence**; `+3047 ms` `ESC[?7l ESC[?7h`; `+3054 ms` first `noisy-` |

The stall is a hard ~3.0 s across every run: 3028 / 3034 / 3041 / 3043 / 3044 / 3047 / 3048 / 3054 /
3061 ms. The card's "2–5 s" is this constant plus test-harness jitter.

## 2. The settling experiment — and its controls

Same spawn; a responder writes to the pty the moment the init burst lands.

| backend / reply | first child output |
|---|---|
| modern / **no reply** (control) | **3061 ms**, **3034 ms** |
| modern / **`ESC[?1;0c`** (DA1 response) | **43 ms**, **37 ms** |
| modern / `z` (arbitrary printable byte) | 3050 ms, 3041 ms |
| modern / `ESC[1;1R` (cursor-position report) | 3035 ms, 3044 ms |
| modern / `[?1;0c` **without the ESC** (accidental control) | 3141 ms |
| inbox / no reply | 46 ms, 74 ms |

So it is **not** "any input unblocks it" and **not** "any VT response unblocks it". It is the DA1
response specifically.

## 3. Reply late — proves the wait is *on the reply*, not an unrelated 3 s init timer

| reply written at | first child output | delta |
|---|---|---|
| 538 ms | 554 ms | **16 ms** |
| 1532 ms | 1548 ms | **16 ms** |
| 2520 ms | 2536 ms | **16 ms** |

The child unblocks 16 ms after the reply, whenever it arrives. The 3.0 s is the timeout on that wait.

## 4. The CHILD is blocked, not just its output

Bat does `break > <file>` (a filesystem write, no console I/O) before its first `echo`:

| backend | file touched | first console output | first pty data (OpenConsole's own init) |
|---|---|---|---|
| modern | **3048 ms** | 3049 ms | 30 ms |
| inbox  | 41 ms | 41 ms | 40 ms |

OpenConsole itself starts fine (init burst at 30 ms). It is the **client** that is held. So this is
not buffered output — the agent process genuinely does nothing for 3 s.

## 5. Input written during the stall is NOT lost

Interactive `cmd /d /q /k`, `echo early_marker_42\r` written at ~350 ms (inside the stall):

| backend / reply | echo seen | delta from write |
|---|---|---|
| modern / none | 3141 ms | **2782 ms** |
| modern / DA1 | 381 ms | **63 ms** |
| inbox / none | 412 ms | 61 ms |

The prompt survives the stall and is delivered when the child starts. **This is a latency defect,
not a data-loss defect** — at least for a cooked-mode client (see risks, §9).

Also measured: a **well-formed** DA1 response is consumed by the pty's input state machine and does
**not** reach the child — the rendered screen after `mode=da1` is byte-identical to the control. The
malformed reply (missing ESC) *did* leak and cmd reported `'[?1' is not recognized`, which is how the
leak/no-leak distinction was established.

## 6. Make it disappear / make it reappear — the required demonstration

Local probe: a `ANTIPHON_PTY_DA1_REPLY=1`-gated auto-answer in `PtyAgentRunner.ReadLoopAsync`
(on first `ESC[c`, write `ESC[?1;0c`). Guards commented out in the three pty test assemblies,
`ANTIPHON_PTY_BACKEND=modern` exported. Every row below was run in the foreground.

| suite / filter | modern, DA1 **off** | modern, DA1 **on** |
|---|---|---|
| `RawPtyAdapterTests/*` (10) | **3F** — `Send_prompt_round_trips_via_interactive_shell`, `Send_input_writes_raw_keystrokes_to_session`, `Resize_updates_pty_without_throwing` (rep 1 and rep 2, identical) | **0F/10** (rep 1 and rep 2) |
| `CodexAdapterLocalShellTests/*` (4) | **3F** — `Wait_for_turn_complete_returns_question_state_after_quiet_output`, `Wait_for_ready_accepts_codex_directory_trust_prompt`, `Question_detection_ignores_question_mark_in_prompt_echo` | **0F/4** |
| `PtyAgentRunnerTests/WaitForQuiet_returns_false_under_continuous_output` | **1F** (rep 1 and rep 2) | **0F/1** (rep 1 and rep 2) |
| `ClaudeDetectorsTests/DoneDetector_*` (2) | **1F** — `DoneDetector_returns_false_under_continuous_output` (rep 1 and rep 2) | **0F/2** |
| `PtyHostAdoptionTests/*` (4) — CARD-0049 | **1F** — `Exit_while_runner_down_is_collected_on_adoption_with_the_real_exit_code` (rep 1 and rep 2) | **0F/4** |

No-regression control: `RawPtyAdapterTests/*` on **inbox** with the DA1 flag **on** → 0F/10 (the
inbox conhost never emits `ESC[c`, so the responder never fires).

That is the full casualty list on the card, all 8 + the CARD-0049 test, each removed by answering
DA1 and each restored by not answering it, at least twice.

## 7. `CreatePseudoConsole` flags do NOT avoid the handshake

Probed `flags` = 0, 1, 2, 4, 8 (we pass 0 today). First child output: 3085 / 3066 / 3055 / 3064 /
3063 ms. **No flag removes it.** Answering DA1 is the lever we have from our side; if a cleaner one
exists it is inside `conpty.dll`, not in the create call.

## 8. Why each casualty fails (mechanism, not coincidence)

Everything that infers "ready"/"done"/"idle" from a **quiet period shorter than 3 s** reads the stall
as quiet:

- `RawPtyAdapter` / `RunnerRawAdapter`: `ReadyGrace` 500 ms → "ready" is declared while the child has
  not started; `TurnQuietPeriod` 2 s → `WaitForTurnCompleteAsync` returns `TurnCompleted: true` with
  an **empty snapshot**, because 2 s of the 3 s stall counts as quiet. That is the 3 red tests, and it
  is a *false positive*, not a timeout.
- `CodexDoneDetector` quiet 3 000 ms vs a 3 000 ms stall — a coin flip; the 3 Codex tests.
- `WaitForQuiet(quiet 2 s, max 3 s)` and `DoneDetector(2 s/3 s)` return `true` where the test asserts
  `false`.

The "waits ≤5 s fail, ≥10 s pass" boundary on the card is the same fact seen from the other side.

## 9. User-visible impact on real agent sessions

`src/Antiphon.SessionRunner/appsettings.json` asks for `modern` and the AppHost sets
`ANTIPHON_PTY_BACKEND=modern` on the server, so **this is live**, for server-side adapters and for
every detached pty-host session.

1. **Every session launch pays a fixed ~3 s penalty** before `claude`/`codex` executes anything.
2. **Codex ready detection is unsound today.** `CodexReadyQuietPeriodMs` is **1000** (settings default
   and `server/appsettings.json`), so `WaitForReadyAsync` fires ~1 s into a 3 s window in which Codex
   has not started. The first prompt is then written into a pty whose client is frozen. Measured with
   `cmd`, that input survives and is delivered; **not measured** for a TUI that switches the console
   to raw/win32-input mode after it starts — the input arrives before the mode switch, so a lost or
   garbled first prompt is a real risk. This is the one exposure I would treat as more than latency.
3. **Codex turn completion can fire on the stall**: `CodexDoneQuietPeriodMs` 3000 ≈ the 3000 ms stall.
4. **Claude is safe by luck**: `ClaudeReadyQuietPeriodMs` 5000 + a 9 s `MinTotalWait` both exceed 3 s,
   so the stall costs latency only. Nothing enforces that relationship — anyone lowering the Claude
   quiet period below ~3 s reintroduces the fault on the main agent path.
5. **Raw agents (`AgentKind.Raw`) can report a completed turn with no output** — `TurnCompleted: true`,
   `ResponseText` empty — for any prompt sent within the first ~3 s of a session.

## 10. CARD-0049 — verdict: SAME root cause, not independent

`Exit_while_runner_down_is_collected_on_adoption_with_the_real_exit_code` launches a child that echoes,
`ping -n 3` (~2 s), then `exit /b 7`, disposes the runner, waits **4 s**, and adopts. On modern the
3 s stall pushes the child's exit to ~5 s, so at the 4 s adoption point it is still alive and the test
sees no collected exit. Answering DA1 makes it pass 4/4 (twice), and not answering it makes it fail
again (twice) — i.e. **adoption does collect a genuinely-exited child; there is no separate adoption
defect in evidence here.** CARD-0049 should be closed as a duplicate of CARD-0048 unless someone has
an independent reproduction that does not depend on the launch window.

## 11. Suggested fix direction (not implemented, not committed)

Answer DA1 once per session from the pty layer, modern backend only, before/independently of the
adapters — the reply is consumed by the input state machine and does not reach the child (§5). Two
things a real fix must decide that the probe did not:

- **Where.** The probe answered from `PtyAgentRunner.ReadLoopAsync`, which is shared by both backends
  and re-enters `WriteAsync`. `ModernConPtyConnection` is the honest owner (it is the thing that
  introduced the query), and it would keep the inbox path byte-identical.
- **Which response.** `ESC[?1;0c` was used. Whatever is chosen must be checked against
  `PtyBracketedPasteContractTests` / `PtyBackendContractTests` — claiming the wrong device attributes
  could change how a TUI negotiates features.

Do **not** widen the waits. The 500 ms / 1 s / 2 s / 3 s quiet windows are correct against a pty that
starts its child promptly; widening them hides the live Codex-ready exposure in §9.2.

## 12. Reproduction recipe (as run)

1. Comment out `[Before(Assembly)]` on `PtyBackendEnvGuard.ClearInheritedPtyBackend()` in
   `tests/Antiphon.Agents.Pty.Tests/`, `tests/Antiphon.Tests/TestHelpers/`,
   `tests/Antiphon.SessionRunner.Tests/`.
2. `ANTIPHON_PTY_BACKEND=modern dotnet run --project tests/<X> --property:OutputPath=bin-c48/ -- --treenode-filter "/*/*/<Class>/*"`.
3. The backend-timing probes need neither step — `new PtyAgentRunner("modern")` pins the backend and
   the guard is irrelevant.

**State of the tree: fully reverted.** `git status --porcelain` is empty; the three `[Before(Assembly)]`
attributes are back; the `PtyAgentRunner` DA1 probe and the `ModernConPtyConnection` flags probe are
gone; the probe test file is deleted (kept at
`…/scratchpad/Card0048StallProbe.cs.keep`); all 14 `bin-c48/` directories removed; no
trailing-space build directories exist. Post-revert re-run with `ANTIPHON_PTY_BACKEND=modern`
exported: `PtyBackendEnvGuardTests` 1/1, `RawPtyAdapterTests` 10/10, `PtyHostAdoptionTests` 4/4 — the
guard is back in force.
