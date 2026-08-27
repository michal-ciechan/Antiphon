# CARD-0133 — Codex delegates: the readiness probe the card asked for, and the boot-wedge it actually measured

**Date:** 2026-08-27 · **Card:** CARD-0133 (`c3a34ac6-5476-483b-bae2-1d5b7cb63753`) · **Status:** investigation
complete, design only — nothing implemented. **Verified against:** `master` @ `19d828c` (this worktree). Every
number below was read out of this machine on 2026-08-27: the live dev database (`antiphon-postgres`), the
per-session ANSI logs under `C:\logs\antiphon\session-runner\`, the pty-host logs under
`…\pty-hosts\logs\`, `%TEMP%\antiphon-logs\session-runner-20260821.log`, and `server/logs/antiphon-20260826.log`.
The 2026-08-21 **server** log is gone (5-day retention), so that day's queue verdicts are reconstructed from the
rows the queue left behind plus the identical 2026-08-26 failures, which the server log does still cover.

**Precedent:** CARD-0103 (`ComposerInputProbe`, the Claude readiness gate this card asks to port), CARD-0108
(Codex submit-confirm + rollout-driven done-detection), CARD-0117 (pool-reuse `/compact` + uncorrelated-incident
scope), CARD-0164 (unobservable-baseline transcript-first confirm), CARD-0195 (Codex MCP boot measured; §4 there
left the "Enter produced zero bytes" shape **unowned** — CARD-0190 closed without taking it), CARD-0055/0056.

---

## Verdict up front

| Question the card/brief posed | Answer, measured |
|---|---|
| Is `RunnerCodexAdapter.WaitForReadyAsync` quiet-period-only, as the card claims? | **Yes.** `RunnerCodexAdapter.cs:127-135` is `WaitForQuietAfterVisibleAsync(CodexReadyQuietPeriodMs = 1000, CodexReadyMaxWaitMs = 60000)` with a trust-dialog observer and nothing else — no min-total-wait floor, no positive probe. The in-process `CodexAdapter.cs:147-151` → `CodexReadyDetector` (`CodexDetectors.cs:12-23`) is the same rule. Claude's chain by contrast is quiet → trust → `ClaudeReadyMinTotalWaitMs` 9 000 → **`ComposerInputProbe`** (`RunnerClaudeAdapter.cs:123-172`). |
| Did CARD-0108 touch the ready path? | **No.** Its plan scoped readiness out explicitly ("`CodexReadyDetector` / startup readiness … a Codex `ComposerInputProbe` adoption is the CARD-0103 follow-up ladder, not this card"). It changed `SendPromptAsync` (submit confirm, `RunnerCodexAdapter.cs:72-99`) and `WaitForTurnCompleteAsync` (`:153-195`, rollout `TurnEnd` primary, `CodexTurnScreenTracker` fallback). **Different detector, different phase.** |
| Is CARD-0117 the same mechanism? | **No.** CARD-0117 was a Pending brief queued behind an unmarked `/compact` turn on a *reused* pool delegate plus a once-per-session incident dedup misattributing a stale incident. Every session in this card is a **cold launch** whose brief was typed and stamped `Sent` on attempt 1. |
| Does the 2026-08-21 evidence match CARD-0103's dead-zone shape (painted, not yet reading, quiet-period reads it as ready, wakes minutes later)? | **No — and this is the finding.** In both 08-21 sessions the TUI **read the whole 618-char brief and rendered it in full** into the composer, closed the frame cleanly (`ESC[?2026l`), and then emitted **not one further byte** for the remaining ~9.5 minutes — through the submitting Enter and until the kill. A TUI that renders your body is reading input; a pre-body round-trip probe would have **passed**. The failing step is *after* the body: the Enter is dead and the process never recovers. CARD-0195 §4 saw exactly this on 08-25 (`8be1afc5`) and called it open. |
| How common is it? | **9 of 78 cold Codex delegate launches since 2026-08-20 (11.5 %)** die this way: `1c00d537`, `2ebefa4b`, `6030ef85`, `4cbbc84b`, `8be1afc5`, `a7f2834e`, `66847862`, `b03bed97`, `e13fc0cf`. Signature: 0 `TranscriptEntries`, brief `Sent`/attempts 1/baseline null, ANSI log 6–12 KB ending on a complete frame with the brief standing in the composer, task failed by the delivery watchdog at ~600 s, session `KilledByRequest`. Four of the nine are from **2026-08-26**, i.e. after CARD-0164 (08-24) — today's code does not catch it. |
| Is it load? | **Not measurably.** Delivery-after-launch offsets for the nine failures are 4.1 / 8.3 / 5.4 / 3.8 / 7.5 / 8.4 / 6.0 / 11.7 / 23.8 s; the 69 successes span 2.0–20.2 s. No separation. At 19:58Z on 08-21 the DB shows 8 live sessions (5 Codex, 3 Claude); the 137-pty-host storm CARD-0103 measured is not in evidence for these. Load is not excluded as a contributor, but it is not the discriminator. |
| Is it the MCP bootstrap (CARD-0195's hypothesis)? | **No** for the freeze; **yes** for a different, real cost. `MCP startup interrupted` appears in 16 sessions, 15 of which succeeded, and is absent in 3 of the failures. But on `6030ef85` the brief was typed while `Booting MCP server: codex_apps (0s • esc to interrupt)` was on screen and the very next thing rendered was `⚠ MCP startup interrupted … codex_apps, node_repl` — the bracketed-paste's leading `ESC` is the interrupt key. Typing during the boot line silently strips MCP from that delegate, which is precisely the capability CARD-0195 §2.3 refused to trade for three seconds. Readiness should wait for that line to clear. |
| Is a terminal query stalling the TUI (CARD-0048 genre)? | **No.** Every failed log carries exactly one DA1 (`ESC[c`, OpenConsole's own, answered by `Da1StartupResponder`), zero CPR/kitty/XTVERSION queries, and balanced `?2026h/l` pairs. Nothing was left waiting on us. |
| Why does today's code stamp it `Sent`? | `DeliverAsync` captures `sequenceBeforeSubmit` (`SessionMessageQueueService.cs:1637`) the moment composer evidence appears (`:1601`, `EvidenceTimeoutSeconds` 15 / poll 500 ms) and presses Enter 20 ms later. Codex renders a 600-char body over several synchronized frames, so the body's **own trailing frames** land past that baseline. `WaitForTranscriptConfirmAsync` (`:1690`) then reads `meta.LastSequence > from` as `sawSequenceAdvance`, which on the unobservable path (a) **suppresses every re-Enter** (`:1793-1796`, `observable \|\| !sawSequenceAdvance`) and (b) at the 30 s deadline returns the **degraded screen-only `Delivered`** (`:1756`). The 08-26 server log says it verbatim: *"confirmed by degraded screen-only verdict after 30s with no transcript row … 1 Enter(s) sent"*. The only bytes that ever existed after the baseline were the body's own render. |
| Does `CodexDoneQuietPeriodMs` share the risk? | **Not any more.** Since CARD-0108 S2 the screen fallback requires the `Working (… esc to interrupt)` indicator to appear *and* leave (`CodexTurnScreenTracker`, `CodexDetectors.cs:78-135`); bare quiet never completes a turn. The frozen shape reports `TurnCompleted: false` at `CodexDoneMaxWaitMs`, which is the truth. **Readiness-only scope stands** — with the correction that "readiness" is the wrong word for what fails. |
| Can Codex's composer be probed the CARD-0103 way? | **Yes, mechanically.** It echoes typed text (`CodexComposerCanaryTests` C/F: typed bodies land byte-identical and render), `ComposerDeliveryEvidence.FragmentIsVisible` already matches on its rendered screen (the queue's evidence check uses it for Codex today), and Enter-on-empty is a measured no-op. The **clear keystroke is unmeasured** — Ctrl+U is a Claude fact (`ClaudeHarness`), and Codex's Esc is only measured safe on an *empty* idle composer (`CodexOverlayCanaryTests`). See S0. |

**So the design has two halves, in priority order that is the reverse of the card's framing:**

1. **The measured failure (S1–S3):** a post-submit *positive* signal for Codex, a wedge verdict instead of a
   degraded `Sent`, and a bounded kill-and-relaunch for a cold delegate whose TUI provably stopped reading. This
   is what turns a 10-minute, 11.5 %-of-launches loss into a ~60-second recovery.
2. **The card's literal ask (S4):** `ComposerInputProbe` adoption in both Codex adapters as the final ready gate,
   plus a "boot line has cleared" gate in front of it. Cheap, same helper, closes the CARD-0103 genre for Codex
   — but **none of the nine failures would have been prevented by it**, and the doc says so rather than letting
   the card's title imply otherwise.

Both halves are gated on **S0**, a stub-proxied reproduction canary that spends no model turns.

---

## 1. Facts, as verified against the tree and the box

### 1.1 The Codex ready path today

- `RunnerCodexAdapter.WaitForReadyAsync` (`server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs:127-135`)
  → `RunnerTerminalSession.WaitForQuietAfterVisibleAsync` (`RunnerTerminalSession.cs:137-184`): poll until
  `VisiblePtyOutput.HasVisibleOutput`, then 1 000 ms with no output-sequence change; `AcceptTrustPromptIfVisibleAsync`
  (`:290-302`) observes each tick and answers Codex's "Do you trust the contents of this directory" with `\r` once.
- Settings: `AgentRegistrySettings.cs:59-62` — `CodexReadyQuietPeriodMs` 1000, `CodexReadyMaxWaitMs` 60000,
  `CodexDoneQuietPeriodMs` 3000, `CodexDoneMaxWaitMs` 300000. Validator `AgentRegistrySettingsValidator.cs:84-91`
  pins them positive. No Codex probe settings exist.
- Only the trust dialog is answered in production. The headed harness (`CxSession.WaitForComposerAsync`,
  `tests/Antiphon.Agents.Pty.Tests/CxSession.cs:203-235`) also answers the **"Update available"** modal with `2`
  + Enter (never Enter alone — option 1 upgrades the CLI under the session) and knows the deprecated-model modal
  exists (`CodexComposerCanaryTests` header). In the nine failures "Update available" rendered as a *banner*, not a
  `Press enter to continue` modal, so it was not the blocker — but production has no handler if it ever is.
- Measured launch timeline (CARD-0195 §1.2, nine launches): header at 0.52–1.68 s; MCP boot status visible up to
  3.34 s. Ready therefore fires at ~2–3 s, and the queue types the brief at +3.8 s on the fastest failure.

### 1.2 The Claude gate this card asks to port

`ComposerInputProbe` (`src/Antiphon.Agents.Pty/ComposerInputProbe.cs`): token `"zz" + sessionId[..8]` (letters
and digits only, so never `/`, `!`, `#`), written raw with **no CR**; poll `ComposerDeliveryEvidence.FragmentIsVisible`
every 250 ms up to 90 s, re-typing at 30 s intervals to 3 writes; once visible, **Ctrl+U** (`\x15`) up to 3 times
inside 10 s and require the token gone. `Responsive` / `NeverAppeared` / `NeverCleared`; the last two fail the
launch through `WaitForReadyOrThrowAsync` → `KillAndDisposeAsync` (CARD-0056). It is a *positive* signal because the
only way the token can appear on the rendered screen is the TUI having drained stdin and repainted — an absence
of output can never satisfy it. Call-site ordering is load-bearing (`RunnerClaudeAdapter.cs:141-172`): after quiet,
after the trust gate (never type into a modal), after the 9 s WebSocket floor (before it Claude drops writes), and
skipped on a `NotAnswerable` modal.

### 1.3 The delegate brief path for Codex (what actually typed the brief)

`AgentTaskDispatcher` enqueues the brief `WhenIdle` / `QueuedMessageOrigin.Delegation` and launches with
`remoteControlName: null` (`AgentTaskDispatcher.cs:1661`), so `VerifiedPromptSubmitter` and
`RunnerCodexAdapter.SendPromptAsync`'s CARD-0108 confirm loop **never run on this path**; the queue does.
For Codex the brief is a ~620-char *pointer* body (the 5 KB brief is spilled to `.antiphon/task-<id>-brief.md`,
the conservative Codex spill policy from CARD-0099 S2). `DeliverAsync` (`SessionMessageQueueService.cs:1456`):

1. `IsVerifiedDeliverySessionAsync` (`:2047-2063`) → Codex is `Supported`, so verification is on.
2. Composer evidence (`WaitForComposerEvidenceAsync`, `:1601`; 15 s, poll 500 ms) — passes as soon as the
   head fragment is visible.
3. `sequenceBeforeSubmit = meta.LastSequence` (`:1637`), 20 ms, `\r`.
4. `WaitForTranscriptConfirmAsync` (`:1690`), unobservable branch (zero `TranscriptEntries` — a cold Codex
   session has no rollout until its first submit, `ProviderContractCatalog.cs:163`): poll for a `UserPrompt` row
   past a wall-clock floor, `CatchUpTranscriptAsync` every ≥1 s, re-Enter every 7 s up to 3 **only while
   `!sawSequenceAdvance`** (`:1793-1796`); at 30 s: advance seen → `RecordDeliveryUnverifiedAsync` + degraded
   `Delivered` (`:1750-1760`); no advance → `NoSubmitOutput` (`:1769`).
5. `HandleDeliveryFailureAsync` (`:2278`) on a failure verdict: revert to Pending, attempts survive, the
   always-on kill only for `AlwaysOn` agents (`:2410`), CARD-0103's refund only for `NoComposerEvidence`
   (`:2350-2378`). A delegate session is not AlwaysOn, so nothing is ever killed here; the 60 s stranded sweep
   retries, parks at 3, and `FailNeverStartedAsync` (`AgentTaskDispatcher.cs:429`) fails the task at
   `DeliveryFailTimeoutMinutes` = 10 (`DelegationSettings.cs:333`) and kills the session.

Delivery settings (`SupervisionSettings.cs`): `EvidenceTimeoutSeconds` 15 (`:150`), `PollIntervalMs` 500
(`:152`), `TranscriptConfirmTimeoutSeconds` 30 (`:182`), `ReEnterIntervalSeconds` 7 (`:188`), `SubmitAttempts` 3
(`:196`), `MaxDeliveryAttempts` 3 (`:247`), `PreFirstTurnNoEvidenceGraceMinutes` 8 (`:280`).

### 1.4 The 2026-08-21 sessions, reconstructed

| | task `54164875` / session `6030ef85` | task `c9ae42fd` / session `4cbbc84b` | control: task `911fdfb7` / session `5851adc8` |
|---|---|---|---|
| dispatched (session row) | 19:58:51.5Z | 20:13:31.5Z | 20:27:51.6Z |
| pty-host child launched (`pty-hosts/logs/<id>.log`, `ModernConPty`) | 19:59:09.52Z | 20:13:38.33Z | 20:27:58.99Z |
| brief typed (`LastDeliveryStartedAt` = `SentAt`, stamped before typing) | 19:59:14.92Z (**+5.4 s**) | 20:13:42.14Z (**+3.8 s**) | 20:28:03.76Z (+4.8 s) |
| queue row | Sent, attempts 1, baseline null, 618 chars | Sent, attempts 1, baseline null, 625 chars | Sent, attempts 1, baseline null, 627 chars |
| screen at brief time | `Booting MCP server: codex_apps (0s` → `⚠ MCP startup interrupted` | (boot line not on screen) | boot line already gone |
| last PTY byte | full brief in composer, cursor at `›`, frame closed (`?2026l`); **11 606 B total** | same; **7 119 B total** | Enter → composer empties, ghost hint returns, `•Working(0s • esc to interrupt)` within ~1 s; 3.1 MB total |
| `TranscriptEntries` | 0 | 0 | UserPrompt 20:28:06.2Z (+2.4 s after Sent), 10 AssistantText, TurnEnd |
| `AgentIncidents` | 0 | 0 | 0 |
| runner log | `no cwd-matching Codex rollout … Running WITHOUT a transcript` at +60 s / +360 s; `the child exited without ever producing a Codex rollout … although input was delivered to it` | same | `adopted Codex rollout … (C1-C4)` at 20:28:06Z |
| end | watchdog failed the task 20:08:56Z; `KilledByRequest` | 20:23:42Z; `KilledByRequest` | reported 20:42Z (Done) |

The four 08-26 failures (`a7f2834e`, `66847862`, `b03bed97`, `e13fc0cf`) have the identical DB/ANSI shape and,
because their server log survives, the identical verdict line: *degraded screen-only verdict after 30s with no
transcript row … 1 Enter(s) sent*, then the watchdog at +10 min. `a7f2834e` additionally hit CARD-0195's
foreign-key swallow (`Recording a transcript fault … failed`), fixed 08-25 in `SessionOwnerLookup`.

### 1.5 What the frozen state is, and is not

- **Not a slow TUI.** CARD-0103's dead zone woke after 48–200 s and processed everything buffered. These never
  wake in 600 s. Nine of nine.
- **Not a swallowed Enter of the CARD-0099/0108 kind.** A CR folded into the paste window inserts a newline,
  which repaints the composer — bytes. Zero bytes means the Enter (and everything after it) was never processed
  as input, or the process stopped repainting.
- **Not a terminal-query stall** (§Verdict). **Not the MCP boot** (CARD-0195 §1, and 3 of 9 never showed it).
- **Not distinguishable, post hoc, between "child frozen" and "pty input path stalled."** Both look identical
  from the runner's side (no bytes, alive process, `KilledByRequest`). This is the one question only a live
  reproduction can answer, and it decides whether the remedy is "kill and relaunch" (child bug) or "our pipe"
  (which no relaunch policy should paper over). S0 answers it before S2 is built.

---

## 2. Design

### 2.1 Principles carried over

- **Positive evidence or nothing.** Every verdict this plan adds is "the TUI did X" — a rendered token, an
  emptied composer, a Working indicator, a UserPrompt row — never "it went quiet". Same ladder as CARD-0047 →
  0048 → 0052 → 0103 → 0108.
- **Never weaken** (CARD-0164's rule): nothing here makes an actually-failed delivery easier to mark `Sent`.
  The changes make one currently-`Sent` shape a failure.
- **Unclaimed never implies kill** (CARD-0056). The only kill introduced is on a session this task's own
  dispatch created, before its first turn, with positive proof it stopped reading — and it is bounded.
- **Enter-only, never re-type** (CARD-0055) stays intact; S1 changes *when* Enter is re-pressed, not whether
  the body is re-typed.
- **Do not widen `CodexReadyQuietPeriodMs`, `EvidenceTimeoutSeconds`, `TranscriptConfirmTimeoutSeconds` or
  the 10-minute watchdog.** A frozen TUI is silent forever; no margin reaches it (CARD-0108 plan §7 said the
  same about `CodexDoneQuietPeriodMs`).

### 2.2 S0 — Measure first: a stub-proxied boot-wedge reproduction (no model turns)

Everything after this slice depends on three unmeasured facts. The harness to measure them already exists in
pieces: `RealCliStubEnv.ForCodex` + `CodexHerdrRealCliStubProxyCanaryTests` drive the **real interactive
`codex.cmd` through the production `SessionRunnerRuntime`** against a stub `/v1/responses` with an isolated
`CODEX_HOME` (`ANTIPHON_REAL_CLI_STUB_TESTS=1`, category `RealCliStubProxy`, CARD-0168), and
`CodexMcpBootProbeTests` shows the boot-observation loop. A rollout is created locally at submit, so a
successful submit is observable without spending a turn.

New `tests/Antiphon.Agents.Pty.Tests/CodexBootWedgeProbeTests.cs` (or under `Antiphon.Tests/Agents` if the
runtime path is needed — the stub harness lives there), `[Explicit]`, gated on both `ANTIPHON_CODEX_HEADED_TESTS`
and `ANTIPHON_REAL_CLI_STUB_TESTS`, `[ParallelLimiter<ProcessSpawnLimit>]`:

**P1 — reproduce the wedge, N = 30 launches**, production shape (`--no-alt-screen
--dangerously-bypass-approvals-and-sandbox`, 120×30, `modern`), `RUST_LOG=codex_tui=debug,codex_core=debug` so
Codex's own `CODEX_HOME/log/codex-tui.log` records key events. Each launch: wait ready exactly as production
(`CodexReadyDetector` semantics, trust answered), then deliver a 620-char pointer-shaped body through
`PtyInputEncoding.EncodeBody` + the queue's 20 ms + `\r`, then poll 30 s for: composer emptied / ghost hint back /
`CodexWorkingIndicator.IsVisible` / rollout `UserMessage` row / output-sequence advance **after a 500 ms settle**.
Record the rate. Expected ~10 %; if 0/30, run the incident's exact cwd shape (`C:\src\Antiphon`, two `-c`
overrides) before concluding anything — CARD-0195 measured that shape differing.

**P2 — on every wedge, discriminate child-frozen vs input-stalled**, in this order, each with a 10 s window:
(a) type one printable char — renders? (b) `ResizeAsync` to 121 cols — repaint? (c) codex-tui.log: was the Enter
logged as a key event? (d) child CPU delta over 10 s (`Get-Process`), (e) `Esc`, (f) `Ctrl+C` (last — it may
exit the process). (c) is the decisive one: a logged Enter with no repaint is a Codex-internal freeze; an
unlogged Enter is our pipe or OpenConsole. Attach the ANSI log tail and the last 40 codex-tui.log lines to the
measurement log.

**P3 — the same 30 launches on codex-cli 0.149.1** (`npm install -g @openai/codex@0.149.1 --prefix <scratch>`,
resolved by path — **never** upgrade the global shim; `~/.codex/version.json` already has 0.149.1 dismissed).
If the wedge rate is 0/30 there, the cheapest fix is a CLI pin and S2 becomes a safety net rather than the
remedy. Operator decision D1.

**P4 — the composer-clear keystroke** (for S4), on a healthy launch: type `zz` + 8 hex, then measure which of
**Backspace × len** (the count is known exactly — we wrote it), **Ctrl+U**, **Ctrl+A → Ctrl+K**, and **Esc**
leaves the composer empty *and* leaves it accepting a follow-up token. Also measure typing a bracketed paste
while the boot line is on screen: does it print `MCP startup interrupted`?

**P5 — census script** (`scripts/codex-boot-census.ps1`): the 78-row table in §Verdict, reproducible — joins
`AgentSessions`/`TranscriptEntries`/`SessionQueuedMessages`/`AgentTasks` with the ANSI-log signature (size, last
frame closed, body visible, Working indicator ever seen, MCP interrupted). Dry read-only. It is how S2's success
is verified after deploy and how a regression is noticed before it costs four tasks in a day.

Exit criteria: P1 rate recorded; P2 verdict (frozen vs stalled) recorded for ≥3 wedges; P3 rate recorded; P4
keystroke chosen. **S1 may proceed without S0** (it is safe regardless of the mechanism); **S2 and S4 may not.**

### 2.3 S1 — A Codex submit is proven by a positive post-Enter signal, never by the body's own render

Change `DeliverAsync` / `WaitForTranscriptConfirmAsync` (`SessionMessageQueueService.cs:1637`, `:1690-1800`):

1. **Settle before baselining.** Capture `sequenceBeforeSubmit` only after the output sequence has been
   unchanged for `PostEvidenceSettleMs` (new `DeliveryVerificationSettings` knob, default **500** — Codex
   repaints a paste over several frames ~100 ms apart; a body render that is still going 500 ms after the head
   became visible is the collapsed-chip case, which is also fine to wait out), bounded by 3 s. The Enter then
   follows a *finished* composer. This alone stops the body's trailing frames from being credited to the Enter,
   for every kind — it is strictly stronger evidence and cannot mark a failed delivery Sent.
2. **A kind-aware positive-submit predicate**, `SubmitEvidence.IsPositive(kind, screenBefore, screenNow, body)`
   in `src/Antiphon.Agents.Pty/` next to `ComposerDeliveryEvidence` (delegate-based, shared with the adapters):
   - Codex: `CodexWorkingIndicator.IsVisible(screenNow)` **or** the body's head fragment no longer visible where
     it was (the composer emptied — measured on the control session: composer clears and the ghost hint
     `›Improve documentation in @filename` / `›Write tests for @filename` returns before `•Working(0s` appears).
   - Claude / Grok: today's rule (sequence advance past the settled baseline) — unchanged behaviour, now on a
     settled baseline.
3. On the **unobservable** branch, `sawSequenceAdvance` is replaced by `sawPositiveSubmit` for Codex. Re-Enter
   keeps going every 7 s up to `SubmitAttempts` **until positive evidence** (today it stops after any advance).
   At the deadline: positive evidence → the existing degraded `Delivered` (still `RecordDeliveryUnverifiedAsync`,
   still a Warning); none → **`NoSubmitOutput`** — and for a Codex session with zero transcript rows this is the
   verdict the nine failures should have got at **+30 s, not +600 s**.
4. Observable branch: untouched. It already confirms by `UserPrompt` text (CARD-0055/0024) and the screen is
   only a log detail there.

`ProviderContractCatalog.Codex.DeliveryVerification` reason text gains the sentence: "a submit is proven by the
Working indicator or an emptied composer; sequence advance alone is the body's own render and is not evidence."

Pinned by `SessionMessageQueueDeliveryVerificationTests` (new arms: Codex unobservable, body renders over 3 fake
frames after evidence, Enter produces nothing → `NoSubmitOutput` at deadline with 3 Enters sent — **red today**:
today returns degraded `Delivered` with 1 Enter; Codex with Working indicator after Enter → Confirmed(Screen);
Claude unobservable keeps today's advance verdict on the settled baseline) and `SubmitEvidenceTests` (pure
screen-string cases from the control session's captured frames).

### 2.4 S2 — A cold delegate whose TUI provably stopped reading is killed and relaunched once, not watched die

Where S1 lands `NoSubmitOutput` on a delegate's first delivery, today's follow-through is revert → 60 s sweep →
re-type → park → 10-minute watchdog. Against a frozen TUI every one of those is theatre. New, narrow arm in
`HandleDeliveryFailureAsync` (`:2278`), gated on **all** of:

- verdict `NoSubmitOutput` (S1's), origin `Delegation`, `DeliveryAttempts == 1`, `LastDeliveryBaselineSequence`
  null (zero rows at type time), session kind Codex (extend by measurement only), session `Status == Running`,
  and an `AgentTasks` row with `AgentSessionId == sessionId` and `Status == Dispatched` — i.e. this task's own
  cold session, nobody else's work;
- **a post-failure liveness probe fails**: `ComposerInputProbe.RunAsync` with the Codex clear keystroke from
  S0-P4, a **10 s** budget (`CodexWedgeProbeTimeoutMs`), single write. This is the card's "positive readiness
  signal", applied at the moment it is actually diagnostic: a token that renders means the TUI is reading and the
  verdict was a submit problem, not a wedge — fall through to today's revert/retry. A token that never renders
  is the same proof CARD-0103 built, pointed at the same question ("is it reading?"), asked after the body
  instead of before it.

Then: raise `AgentIncidentKind.BootWedged` (new; Warning, timeline row, Critical never — a delegate is not
channel-bound), record the ANSI tail and probe result in the incident message, `KillAsync` the session through
the runner (same primitive `FailNeverStartedAsync` uses), mark the queue row **Canceled** (not Pending: the
relaunch enqueues a fresh row with the same body against the new session; a Pending row on a dead session is
CARD-0117's shape) and hand the task to a new dispatcher arm:

`AgentTaskDispatcher.RelaunchWedgedAsync` — for a `Dispatched` task whose session died with a `BootWedged`
incident and `RelaunchCount < DelegationSettings.BootWedgeRelaunchLimit` (default **1**): spawn a fresh session
for the same agent, same worktree (already created; `Worktree created at …` is a one-time event), same launch
spec, re-enqueue the brief pointer (the brief file is already on disk), `RelaunchCount++`, `DispatchedAt`
re-stamped so the 10-minute watchdog measures the relaunch. At the limit: fail the task **now** with
`"Boot prompt could not be delivered: the Codex TUI stopped reading input after the brief rendered, twice
(BootWedged incidents …); the task was relaunched once and wedged again"` — 90 s after dispatch instead of 600,
with a reason that names the mechanism. `FailDeadSessionTasksAsync` (`:738`) must skip a task that has a
pending relaunch (a session killed *by us* for relaunch is not a dead-session zombie); add the predicate there.

Why relaunch rather than fail-fast alone: the 08-21 operator did exactly this by hand (third dispatch succeeded
first time), 78 launches show a fresh process succeeds 88 % of the time, and a delegate's cost is dominated by
the orchestrator's wait. Why a limit of 1: two consecutive wedges (as on 08-21) is evidence of something the
relaunch does not fix — the operator should see it. Operator decision D2.

Pinned by `SessionMessageQueueDeliveryVerificationTests` (the arm fires only on the full conjunction; each leg
negated: attempts 2 → no; baseline non-null → no; non-delegation origin → no; probe token renders → no kill,
revert as today; Claude kind → no), `AgentTaskDispatcherRelaunchTests` (relaunch once, same worktree, fresh
session + fresh queue row, `DispatchedAt` re-stamped; second wedge fails with the named reason;
`FailDeadSessionTasksAsync` leaves a relaunch-pending task alone), and `BootWedgeIncidentTests`.

### 2.5 S3 — Readiness waits for the MCP boot line to clear (the cheap, measured part of "readiness")

In both Codex adapters, after quiet and trust and before anything is typed: if the rendered screen contains
`Booting MCP server` / `Starting MCP servers` (the two strings `CodexMcpBootProbeTests` pins), wait until the line
has been absent for 500 ms, bounded by `CodexBootStatusMaxWaitMs` (default **10 000**; CARD-0195's worst case was
3.34 s — a bound, not a cost). A bound expiry logs a Warning and proceeds (the boot line is not a modal; typing
over it costs the MCP servers, not the session). Pinned by a `ScriptedCodexRunnerClient` arm that renders the
boot line for N reads, and by S0-P4's canary result on whether an `ESC`-led paste interrupts it.

This is the one readiness change with measured evidence behind it (`6030ef85`'s `MCP startup interrupted`
following the paste, 16/78 sessions interrupted), and it is independent of S4.

### 2.6 S4 — `ComposerInputProbe` as the final Codex ready gate (the card's literal ask)

Adopt in `RunnerCodexAdapter.WaitForReadyAsync` and `CodexReadyDetector` (lockstep, delegate-based like the
Claude pair), after quiet → trust → S3's boot-line gate, using the keystroke S0-P4 chose. `ComposerInputProbe`
gains a `clear` parameter (a `Func<int writes, string>` so Backspace-count can scale with re-types; Claude keeps
`KillLine`). Settings mirror Claude's: `CodexInputProbeTimeoutMs` (default **30 000** — no measured Codex dead
zone exists to size against; 90 s is Claude's number for Claude's measurement), `…PollIntervalMs` 250,
`…RetypeIntervalMs` 10 000, `…MaxWrites` 3, `…ClearTimeoutMs` 10 000; zero disables. A `NeverCleared` verdict
fails the launch (a composer we could not empty must not have a brief appended). Add the "Update available" /
deprecated-model modal handling `CxSession` already has to `AcceptTrustPromptIfVisibleAsync`'s sibling — typing a
probe into a `Press enter to continue` modal is the CARD-0047 hazard, and only the trust modal is answered today.

Expected effect on the measured failure: **none** — stated plainly. Effect: a Codex TUI that is painted and
deaf at launch (never observed in 78 launches, measured for Claude under load) fails loudly through
`KillAndDisposeAsync` instead of eating a brief; and S2's post-failure probe reuses the same measured keystroke.
Cost: one round trip (~1 s) per Codex launch. Operator decision D3 is whether to ship it default-on.

Pinned by `ComposerInputProbeTests` (clear-parameter arms), `RunnerCodexAdapterReadyTests` (new, on
`ScriptedCodexRunnerClient` taught to echo a token and honour the clear keystroke; deaf-forever → false; modal
standing → probe skipped and logged), and an S0-P4 canary arm that pins the keystroke against the real CLI.

### 2.7 Slices, order, tiers

| Slice | Contents | Depends on | Tier |
|---|---|---|---|
| S0 | boot-wedge reproduction canary (P1–P3), clear-keystroke measurement (P4), census script (P5) | nothing | Frontier/High — it is a measurement whose interpretation decides S2; needs judgement, not volume |
| S1 | settled baseline + Codex positive-submit predicate + re-Enter until positive + `NoSubmitOutput` at deadline; contract prose; pins | nothing (safe regardless of S0) | Codex terra / Grok — mechanical against a precise spec |
| S2 | `BootWedged` incident, post-failure liveness probe, kill, `RelaunchWedgedAsync`, dead-session-reconciler predicate; pins | S0 (P2 verdict + P4 keystroke), S1 | High — touches kill/relaunch; positive-control discipline as CARD-0117 S4 |
| S3 | boot-line gate in both Codex adapters; pins | S0-P4 (interrupt measurement) | Codex terra / luna |
| S4 | `ComposerInputProbe` clear parameter + Codex adoption + modal handling + settings; pins | S0-P4, S3 | Codex terra |
| S5 | live verification: deploy, run P5 census over the next ≥40 cold Codex launches, close only when the 0-rows/`Sent` signature is gone and every wedge became a `BootWedged` row + relaunch | S1–S4 deployed | luna |

S1 can ship the same day as S0 starts. S2 waits for S0's P2 verdict: **if P2 shows the Enter never reached
Codex (our pipe / OpenConsole), S2's relaunch is still correct for the operator but the card gets a sibling for
the pipe, and S2's incident text must say "input did not reach the child" rather than "the TUI froze."**

Test runs (alternate output path, forward slash; delete `bin-card0133/` dirs after — roughly a dozen appear;
`Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` sequentially, never co-scheduled):

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0133/ -- --treenode-filter "/*/*/SessionMessageQueueDeliveryVerificationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0133/ -- --treenode-filter "/*/*/RunnerCodexAdapter*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0133/ -- --treenode-filter "/*/*/AgentTaskDispatcherRelaunchTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0133/ -- --treenode-filter "/*/*/ComposerInputProbeTests/*"
$env:ANTIPHON_CODEX_HEADED_TESTS='1'; $env:ANTIPHON_REAL_CLI_STUB_TESTS='1'; dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0133/ -- --treenode-filter "/*/*/CodexBootWedgeProbeTests/*"
```

Known-red inherited on `master` (CARD-0195 §5, re-confirmed there at base): 2 of 86 in
`SessionMessageQueueDeliveryVerificationTests` (`Verified_delivery_types_body_then_submits_and_leaves_no_incident`,
`Claude_auto_compact_still_enqueues_and_delivers_through_the_normal_path`). Not this card's; do not let S1's
report claim them either way without re-running at base.

---

## 3. Deliberately not in scope

- **Widening any timeout** — `CodexReadyQuietPeriodMs`, `EvidenceTimeoutSeconds`, `TranscriptConfirmTimeoutSeconds`,
  `DeliveryFailTimeoutMinutes`, `CodexDoneQuietPeriodMs`. The frozen shape is silent for ≥600 s; the slow shape
  was never observed for Codex.
- **`CodexDoneQuietPeriodMs` / `WaitForTurnCompleteAsync`** — CARD-0108 S2's indicator-gated fallback already
  refuses bare quiet; no measured defect remains there.
- **`RunnerCodexAdapter.SendPromptAsync`** (card-launch / interactive path) — CARD-0108 S1's confirm loop already
  re-presses Enter and throws `PromptDeliveryException` on a stranded composer; the delegate path never reaches
  it. If S0 shows the wedge also hits that path, its catch already runs `KillAndDisposeAsync` (CARD-0056), which
  is the same remedy S2 builds for the queue path.
- **The observable-baseline branch** of `WaitForTranscriptConfirmAsync` and `PromptSubmissionMatch`
  identity/completeness — untouched (never-weaken).
- **Upgrading the production Codex CLI** — measured in S0-P3, decided in D1, executed on its own card if chosen
  (CARD-0195 item 4 and CARD-0203's leaked `codex.exe` are the neighbours).
- **Suppressing `codex_apps` / `node_repl`** — CARD-0195 §2.3's reasoning stands; S3 protects them instead.
- **CARD-0117's reuse path, CARD-0190's binding, CARD-0142's mirror replies** — all confirmed different
  mechanisms; none of the nine failures involve a reused session, a stale rollout, or an open Terminal panel.
- **Grok / OpenCode / Raw readiness** — Grok has an echoing composer and the same quiet-shaped ready
  (CARD-0103 plan §5 names it the obvious second adopter); its clear keystroke is unmeasured and it has no
  measured failure. Separate card.
- **Fake Codex** (CARD-0099 S5) — still does not exist; S0's measurements (a post-paste freeze, the boot line,
  the clear keystroke) are exactly what it must model when built. `ScriptedCodexRunnerClient` carries CI here.

---

## 4. Operator decisions (with recommendations)

**D1 — If S0-P3 shows codex-cli 0.149.x does not wedge (0/30), pin the delegate launch to it?**
*Recommend: yes, as a per-launch path resolved by `CodexLaunchArgs`/`AgentDefinition` rather than upgrading the
global shim*, so the operator's own `codex` is untouched and the deployment can roll back by config. S2 then
stays as the safety net (11.5 % is too high a rate to leave uncaught even at a lower rate on a newer CLI).
If P3 still wedges, D1 is moot and S2 is the remedy.

**D2 — Auto-relaunch a boot-wedged delegate (S2), and how many times?**
*Recommend: yes, limit 1.* One relaunch recovers the measured 88 % first-launch success rate at ~90 s cost;
a second wedge is a signal, not noise, and should fail with the mechanism named. The alternative — fail-fast at
+30 s with no relaunch — halves the loss but leaves the orchestrator redispatching by hand, which is what
happened on 08-21 and what the card was filed over.

**D3 — Ship S4 (the `ComposerInputProbe` ready gate) default-on for Codex?**
*Recommend: yes, but only after S0-P4 has measured the clear keystroke and S3 is in front of it*, and with the
plain statement in the card's closing note that it addresses the CARD-0103 genre, not the measured failure.
If P4 finds no keystroke that reliably empties the composer, ship S4 **off** (`CodexInputProbeTimeoutMs = 0`)
with the finding recorded — a composer we cannot clear is a launch we must not probe.

**D4 — Card housekeeping.** CARD-0195 §4's open item ("Enter produced zero bytes … recommend folding into
CARD-0190") was never picked up — CARD-0190 closed on its own binding fix. *Recommend: CARD-0133 owns the
boot-wedge from here* (retitle on the next revision: "Codex delegates: brief renders, Enter is dead, TUI never
recovers — 9 of 78 cold launches; plus the readiness gate"), cross-link CARD-0195 §4 and CARD-0203 (the
leaked `codex.exe` — worth checking in S0 whether a wedged child is what leaks).
