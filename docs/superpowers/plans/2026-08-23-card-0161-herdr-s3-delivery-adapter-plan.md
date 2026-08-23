# CARD-0161 — herdr S3 delivery adapter: per-session ceilings, blocked→defer, CARD-0055 unchanged — plan

**Date:** 2026-08-23 · **Card:** CARD-0161 (`3dac1cae-9dec-4768-bf4f-440e40521b92`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `feat/card-task-fdb3f7f4` @ `79df00d` (= master after CARD-0160 S2). Every
file:line below was re-read out of the code on that commit. Every herdr number below that is marked
**measured** was measured LIVE in this pass — herdr 0.8.2, protocol 20, Claude Code v2.1.241 on
Haiku 4.5, this machine, 2026-08-23 — through the same named-pipe framing `HerdrClient` uses
(§ Live measurements). The S1 86 400-byte figure is now corroborated, not inherited.

**Established facts, not re-derived here:**
- The Investigate stage (task `4a27c090`, findings on the card 2026-08-23): the LF/wrap/`\r` →
  `send_text`/`send_keys` pipe is already end-to-end (`SessionMessageQueueService.DeliverAsync` →
  `AgentSessionRuntime.SendInputAsync` → runner `RunnerSession.WriteAsync` →
  `HerdrPaneChild.WriteAsync`, `HerdrPaneChild.cs:126-135`); CARD-0055's verdicts already read
  `TranscriptEntries`, not the pty, so they apply to herdr sessions unchanged; what is missing is
  ceilings resolution, blocked handling, and proof that composer evidence works over polled
  `pane.read`.
- The S1 spike (`docs/investigations/2026-08-21-herdr-s1-spike-CARD-0120.md`): `agent.prompt`'s
  state-wait returned a false `agent_prompt_stalled` on a delivery that had in fact landed whole —
  herdr's own delivery verdict is never trusted, which is why this card routes through
  `pane.send_text` at all.
- The S2 plan (`docs/superpowers/plans/2026-08-23-card-0160-herdr-s2-launch-path-plan.md`) §2:
  input passthrough is a transparent transport; the delivery ADAPTER — ceilings, profile arm,
  `agent_blocked` mapping — is this card. S2 shipped no event pump (`HerdrClient.SubscribeEventsAsync`
  has zero callers on `79df00d`); its §6B reconciliation pump is still owed and is NOT pulled in here.
- CARD-0055/0024/0056 delivery discipline (CLAUDE.md entries): Sent requires a matching, COMPLETE
  `UserPrompt` transcript record; retries are Enter-only; late-confirm before any re-type; a
  working session is never killed on a delivery verdict; parking is for humans.

**Related:** CARD-0160 (S2, shipped `9a140be..79df00d`), CARD-0162 (S4 state mirror — the event
pump's owner), CARD-0055/0024 (verdicts — unchanged by construction here), CARD-0037 (the
measured-ceiling pattern this copies), CARD-0027/0030 (why single-write and the wrap exist),
CARD-0141/0137 (why an Enter near a modal is dangerous), CARD-0136 (refuse, never silently remap).

---

## Verdict up front — the nine decisions

1. **Ceilings axis: a new server-side `DeliveryBackend` enum (`InboxConhost | ModernConPty |
   HerdrPane`); `PtyDeliveryCeilings.Backend` moves onto it.** `PtyBackend` is untouched — herdr
   never becomes a third pseudoconsole value. §2.
2. **Per-session resolution: a new `SessionDeliveryProfile` wrapping `PtyDeliveryProfile`, keyed on
   the `AgentSession.SessionBackend` snapshot, consulted once per delivery inside the queue.** The
   delegation brief/reply call sites stay on the process-wide profile in S3 — safe because herdr's
   envelope is a superset of both pty sets, so the error direction there is only ever an extra
   spill. §3.
3. **Capability evidence for the herdr arm: the session's DB snapshot (`SessionBackend == Herdr`)
   AND the runner's live capabilities containing `"herdr"`.** Disagreement or no answer → the inbox
   conservative set, loudly. The runner's `PtyBackend` is irrelevant to this arm. §3.
4. **Composer evidence under poll-only `pane.read`: works as-is and is now measured** — placeholder
   `[Pasted text #N +M lines]` renders in `pane.read visible` with the per-session `#N` index
   `ComposerDeliveryEvidence` already matches; worst-case render latency 5.4 s against a 15 s
   evidence timeout. Plus ONE runner fix this pass found: herdr `LastSequence` only moves when a
   snapshot is taken, so the single-session GET must refresh it — without that, every
   pre-first-turn delivery to a herdr session fails `NoSubmitOutput` deterministically. §4.
5. **First-Enter timing: keep the production 20 ms gap and CARD-0055's Enter-only re-press;
   no herdr-specific delay.** Measured: with the production ordering (evidence wait → 20 ms →
   Enter), the FIRST Enter submitted an 86 400-byte paste. S1's "500 ms too early" was a
   fixed-delay probe without the evidence wait — not the production shape. §5.
6. **Blocked observation: poll-only in S3.** The runner's single-session GET carries an additive
   `RunnerSessionDto.AgentStatus` (null for pty sessions and old runners); the queue defers —
   `FlushResult.Nothing`, no attempt charged, nothing parked, nothing killed — when a herdr session
   reports the literal `"blocked"`. Every other value (`done`, `idle`, `working`, `unknown`, null,
   unreachable) changes nothing: the vocabulary is open (measured: `done` is the normal post-turn
   state, NOT `idle`) and only `blocked` may gate. No `events.subscribe` consumption in S3. §6.
7. **Blocked-during-confirm: CARD-0055 runs unchanged, plus one narrow herdr-only guard — a
   re-press Enter inside the confirm loop is WITHHELD while herdr reports blocked.** Withholding a
   keystroke is safe even when the heuristic is wrong; pressing one into a permission picker is
   CARD-0141's accident. The verdict, the timeout, grace-confirm and the working-kill guard are
   byte-for-byte today's. §7.
8. **Marker/text integrity: measured exact.** 86 400 UTF-8 bytes through one `pane.send_text` →
   `UserPrompt` record `-ceq`-identical to the sent body: head-200 window intact (identity),
   full containment (completeness), zero ESC bytes (no double-wrap — herdr passes our markers to
   the TUI's paste path, it does not re-wrap or leak them into the record). §8.
9. **S3/S4 boundary: S3 is poll-only delivery; S4 (CARD-0162) owns everything event-shaped** —
   the `events.subscribe` pump, `pane.closed` → Exited, `pane.agent_status_changed` →
   `FlushIfIdleAsync` unblock, reconnect sweeps (including S2's still-unshipped §6B), and any
   status mirroring to the UI. S3 must not open the subscription stream. §9.

---

## Live measurements (this pass, 2026-08-23)

All through the raw named pipe (`%APPDATA%\herdr\herdr.sock`, one NDJSON request per connection —
`HerdrClient`'s framing), against a real Claude Code v2.1.241 started by `agent.start
{kind:"claude", args:["--session-id",…,"--model","haiku"]}` in a pane of the operator's live
herdr 0.8.2. Bodies all-ASCII, LF endings, wrapped `ESC[200~ … ESC[201~` exactly as
`PtyInputEncoding.WrapIfMultiline` emits.

| # | Measurement | Result |
|---|---|---|
| M1 | **86 400 B in ONE `pane.send_text`**, evidence-gated Enter | `UserPrompt` record **exact byte-for-byte** (`-ceq` on the full string): 86 400/86 400 B, head-200 intact, `CARD0161END` tail present, **zero ESC bytes** in the record. `send_text` returned at +1.1 s; placeholder `❯ [Pasted text #1 +891 lines]` visible in `pane.read visible` at **+5.4 s**; **first Enter** (evidence + 20 ms) submitted; record on disk **+1.7 s** after Enter. |
| M2 | **Same 86 400 B PACED**: 85 × 1 024 B `pane.send_text` calls, 25 ms apart, markers only in first/last chunk | **Also intact, exact 86 400 B**, one Enter. Herdr does NOT reproduce the modern-pty paced loss (CARD-0030's paced run read NOTHING). Placeholder index advanced to `#2` (per-session counter, as `ComposerDeliveryEvidence` assumes). |
| M3 | **Small typed body** (76 chars, unwrapped, single line) | Visible in `pane.read` at **+257 ms**; Enter submitted; tool ran. |
| M4 | **`agent_status` on a real tool-permission modal** (`whoami /priv`, not allowlisted) | `done → working (+0.8 s) → blocked (+4.7 s)` — `blocked` fires when the approval UI renders, read from `pane.get`. Esc → `done`. Observed vocabulary this pass: `unknown, idle, working, done, blocked` — note **`done`, not `idle`, is the normal post-turn state**. |
| M5 | **`pane.read` latency** | Idle: 2–5 ms. During heavy paste processing: median ~104 ms, max ~107 ms (n=14). Both far inside the 500 ms `PollIntervalMs` cadence and 15 s `EvidenceTimeoutSeconds`. |
| M6 | Response envelopes | `pane.read` → `{type:"pane_read", read:{text, revision, …}}`; `pane.get` → `{type:"pane_info", pane:{…, agent_status, revision}}`. The shipped S2 wrappers already unwrap these correctly (`HerdrClient.cs:237-281`). |

**What M2 does and does not license.** The single-write rule (one `PaneSendTextAsync` per body, one
`SendInputAsync` server-side) is KEPT: it is the shape both S1 runs and M1 were measured in, it is
what the pty lanes require, and one measured tolerant run is not a contract that herdr coalesces
forever. But the plan must be honest that on herdr the 86 400 figure is *the largest measured*, not
a measured cliff — no loss was observed at any size or pacing tried. The oversize tripwire at
86 400 therefore stays exactly what it is on the modern pty: the edge of the evidence, not of the
transport.

---

## 1. What already works, restated precisely (so the diff stays small)

On `79df00d`, a queued delivery to a herdr-backed session already: LF-normalizes, wraps, sends the
body as ONE write that reaches `pane.send_text` whole, sends `\r` separately as `send_keys
["enter"]`, polls composer evidence via `TryGetLiveSnapshot` → runner `GetSnapshot` → on-demand
`pane.read` (`SessionRunnerRuntime.cs:1243-1270`), and runs `WaitForTranscriptConfirmAsync` /
completeness / grace-confirm / late-confirm entirely against `TranscriptEntries`
(`SessionMessageQueueService.cs:1447+`). Transcript binding is the pty-identical C1–C4 path.

**S3 is therefore NOT a new write path or a new verdict.** It is: (a) the ceilings a herdr session
is sized against, (b) a defer arm for `blocked`, (c) one runner surface (status + revision on the
single-session GET), and (d) pins. Anything in the build that finds itself adding a herdr branch to
`DeliverAsync`'s verdict logic is off the map and should stop.

## 2. Decision 1 — the ceilings axis: `DeliveryBackend`, not a third `PtyBackend`

`PtyBackend` (`src/Antiphon.Agents.Pty/PtyBackend.cs`) stays `InboxConhost | ModernConPty`. Its
process-wide invariant (`PtyBackendPolicy`, XML doc at the class head) is about which
*pseudoconsole binary* serves the ptys THIS deployment spawns — `PtyBackendPolicy.Resolve()` feeds
spawning code, and a value that must never be spawned from does not belong in that enum. S2 created
`SessionBackend` for exactly this reason; the ceilings record follows the same logic.

- **New enum** `server/Application/Settings/DeliveryBackend.cs`:
  ```csharp
  /// <summary>The write path a delivery's ceilings were measured against (CARD-0161).
  /// InboxConhost/ModernConPty mirror PtyBackend; HerdrPane is pane.send_text into the
  /// operator's herdr (SessionBackend.Herdr sessions only). Never fed to spawning code.</summary>
  public enum DeliveryBackend { InboxConhost = 0, ModernConPty = 1, HerdrPane = 2 }
  ```
- **`PtyDeliveryCeilings.Backend`** (`PtyDeliveryCeilings.cs:37`) retypes `PtyBackend →
  DeliveryBackend`. `IsPastePath => Backend != DeliveryBackend.InboxConhost` (herdr is a paste
  path — M1/M2 measured the placeholder collapse and marker survival). `ForAgentKind` and the
  spill plumbing are untouched — they take the record, not the enum.
- **`DelegationSettings`** gains the herdr knobs + arm, next to the modern ones (`:150-214`):
  ```csharp
  public int HerdrPaneBriefInlineMaxBytes  { get; set; } = 43_200;
  public int HerdrPaneReplyInlineMaxChars  { get; set; } = 14_400;
  public int HerdrPaneSingleWriteMaxBytes  { get; set; } = 86_400;
  public PtyDeliveryCeilings HerdrCeilings(string reason) => new(
      DeliveryBackend.HerdrPane, HerdrPaneBriefInlineMaxBytes,
      HerdrPaneReplyInlineMaxChars, HerdrPaneSingleWriteMaxBytes, reason);
  ```
  `CeilingsFor(PtyBackend, reason)` keeps its signature and maps through
  (`InboxConhost→InboxConhost`, `ModernConPty→ModernConPty`) so every existing caller compiles
  unchanged.
- **Why the same three numbers as modern:** the envelope is measured identical (86 400 exact,
  S1 twice + M1 + M2), herdr 0.8.2 ships the modern ConPTY runtime app-local (S1), and the
  brief/reply derivations (2× margin under the envelope; chars = bytes/3) carry over verbatim.
  They are separate KNOBS because they are separate measurements — a herdr upgrade re-measures
  herdr, not the pty.
- Doc comments on all three quote this plan's M1/M2 with the date, per the CARD-0037 convention.

**Rejected:** a third `PtyBackend` value (violates the invariant; leaks into `Resolve()`); a
parallel ceilings record type (duplicates `ForAgentKind`/`ToString`/spill call sites for zero
information gain).

## 3. Decisions 2 & 3 — per-session resolution and its evidence

**The problem:** `SessionMessageQueueService.Ceilings` (`:68-70`) is process-wide from
`PtyDeliveryProfile`, which resolves the PTY backend. On this deployment (modern) a herdr session
gets modern numbers — accidentally correct. On an inbox deployment with herdr enabled, a herdr
session would get the 1 024-byte tripwire and raise a spurious `OversizedTerminalDelivery` on
every multi-KB body that herdr in fact carries whole. And any future divergence of the herdr
numbers would silently not apply. A mixed fleet needs the ceilings resolved per session.

**New service `SessionDeliveryProfile`** (server, singleton, composes `PtyDeliveryProfile`):

```csharp
public async Task<PtyDeliveryCeilings> ForSessionAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
```

- Reads `AgentSessions.SessionBackend` for the id (AsNoTracking PK select). The snapshot is
  immutable after row creation (S2), so a bounded in-memory `sessionId → SessionBackend` cache is
  safe and makes the steady-state cost zero; a miss falls back to the DB, an unknown id returns the
  pty profile's answer (today's behaviour, exactly).
- `PtyHost` → `_ptyProfile.Ceilings` — the existing two-fact modern gate, untouched.
- `Herdr` → the herdr arm, gated on **two facts** mirroring the modern pattern
  (`PtyDeliveryProfile`'s class doc): **fact 1** is the snapshot itself — a session only carries
  `Herdr` because the launch went down the runner's herdr branch, which the CARD-0160 capability
  gate already vetted; **fact 2** is the runner's CURRENT capabilities containing `"herdr"`
  (`RunnerCapabilitiesDto.SessionBackends`, advertised only when `SessionRunner:Herdr:Enabled` —
  runner `Program.cs:129-137`), probed lazily with the same 5-minute TTL / 5-second timeout / no
  answer = no evidence pattern as `PtyDeliveryProfile.ProbeAsync`. Facts disagree (runner answers
  and does NOT list herdr — a swapped or reconfigured runner) → the **inbox conservative set**
  with a Warning naming both facts; a runner that cannot answer → also the conservative set
  (unlike the modern arm there is no "local intent" to stand on for a lane the runner hosts).
  The runner's `PtyBackend`/`PtyBackendReason` are never consulted here — herdr panes are not
  served by the runner's pseudoconsole flag.
- The conservative direction is loss-proof by construction: inbox numbers on a herdr session
  over-spill and over-warn but cannot over-type.

**Call sites that move to per-session (S3):** everything inside the queue —
`DeliverNextLockedAsync` resolves ONCE at its top and threads the record through its own uses
(`PrependWithinCeiling` at `:806`, the batch budget at `:876`, `SpillQueueBodyAsync`) and into
`DeliverAsync` (tripwire at `:1292-1302`), replacing the `Ceilings` property reads on that path.
`SendNowAsync` likewise. The property itself remains as the no-profile test fallback.

**Call sites that deliberately stay process-wide (S3):** `AgentTaskDispatcher:1766` (brief sizing),
`AgentTaskReplyService:354` (reply forwarding), `AgentSessionService:82`. Argument, stated so the
build doesn't "fix" it: herdr sessions are the only per-session deviation from the process-wide pty
answer, and they deviate strictly UPWARD (86 400-byte envelope ≥ either pty set), so sizing a
herdr-bound brief/reply with pty numbers errs only toward spilling to a file — which is always
correct. The reverse error (typing pty-oversized bodies) cannot arise because pty sessions are
process-uniform by the `PtyBackendPolicy` invariant. S4 may unify these onto
`SessionDeliveryProfile` when it also carries the profile into dispatch-time decisions; S3 keeps
the diff on the delivery path.

## 4. Decision 4 — composer evidence over poll-only `pane.read`, and the sequence fix

**The evidence path already fits, now with numbers.** `WaitForComposerEvidenceAsync`
(`SessionMessageQueueService.cs:1621`) polls `TryGetLiveSnapshot` every 500 ms; for a herdr session
each poll is a fresh on-demand `pane.read` (runner `GetSnapshot`, `SessionRunnerRuntime.cs:1243`).
Measured: read latency 2–5 ms idle / ~104 ms under paste load (M5); placeholder render at worst
+5.4 s for the full envelope (M1), typed echo at +257 ms (M3) — all inside the 15 s
`EvidenceTimeoutSeconds` with 2.7× margin. The placeholder carries the per-session `#N` index
(M1 `#1` → M2 `#2`), which is exactly the identity `ComposerDeliveryEvidence` matches
(CARD-0037 step 3), and `RenderedScreen`/`RawOutput` are both the stripped `visible` text for
herdr sessions, which that matcher is happy with. **No change to the evidence code.**

**The fix this pass found — herdr `LastSequence` is frozen between snapshots.** For herdr sessions
`_lastSequence` advances ONLY inside `GetSnapshot` (`:1256`), but `TryGetLiveMetadata` (server,
`AgentSessionRuntime.cs:710`) reads it via the single-session GET → `ToDto()`, which does no
`pane.read`. Consequence: `WaitForSequenceAdvanceAsync` (`:1640`) — the **screen-only fallback
verdict** that governs every delivery to a session with zero transcript rows at baseline (the
launch-note flush on a fresh herdr session is precisely this shape) — polls a number that
structurally cannot move, and every such delivery fails `NoSubmitOutput` after 30 s,
deterministically. The confirm loop's "screen advanced" wedge log-line is similarly blind.

**Fix (runner-side, narrow):** the single-session `GET /sessions/{id}` handler, for a session with
`_herdrChild`, refreshes before answering: one `pane.get` (async, in the handler — never in
`ToDto()`/`ListAsync`, which stay cheap and sync) whose `revision` is folded into `_lastSequence`
exactly as `GetSnapshot` does, and whose `agent_status` populates §6's DTO field. One herdr call
serves both. The build must first confirm `pane.get.revision` and `pane.read.revision` are the same
counter (probe **PR1** — M6 shows both fields exist; identity is asserted, not assumed, by
snapshotting both in one script). If they differ, the handler uses a `lines: 0`/minimal
`pane.read` instead and `pane.get` only for status.

## 5. Decision 5 — first-Enter timing: nothing changes

Production ordering is: send body → **wait for composer evidence** (polling, up to 15 s) → 20 ms →
Enter → transcript confirm with Enter-only re-presses at 7 s intervals. S1's "first enter at 500 ms
was too early" came from a probe that pressed at a FIXED 500 ms with no evidence wait. Measured in
this pass under the production ordering: the placeholder rendered at +5.4 s and the first Enter
submitted, one Enter total, record +1.7 s later (M1). The 20 ms gap, `ReEnterIntervalSeconds` = 7,
`SubmitAttempts` = 3 and `TranscriptConfirmTimeoutSeconds` = 30 all stay; CARD-0055's re-press is
the designed recovery if a first Enter ever does land early on a slower render. No herdr-specific
delay knob is added — a knob with no failing measurement behind it is tuning theatre.

## 6. Decision 6 — where `blocked` is observed, and what it may do

**Surface (runner):** additive `RunnerSessionDto.AgentStatus` — `string?`, default null, populated
ONLY for herdr sessions by §4's single-GET refresh (`pane.get.agent_status` verbatim). Null for
every pty session and from every older runner, which keeps the wire contract additive
(`TranscriptFormats` precedent). No new endpoint.

**Server plumbing:** `AgentSessionLiveMetadata` gains `string? AgentStatus`;
`TryGetLiveMetadata` copies it through. No caching beyond the GET itself.

**The one gate (queue):** at the top of `DeliverNextLockedAsync` — after the pending query, before
late-confirm claims anything — for a session whose `SessionDeliveryProfile` backend is `Herdr`:
read live metadata; if `AgentStatus` is the literal `"blocked"` (ordinal, case-insensitive), log
Debug and return `FlushResult.Nothing`. The message stays `Pending`, `DeliveryAttempts` untouched,
no incident, no park, no kill — byte-identical to what `IsWorkingAsync == true` does to a WhenIdle
flush today. `SendNowAsync` gets the same pre-check (a human's send-now lands when the modal
clears, exactly like send-now onto a working session).

**The vocabulary rule, pinned by measurement:** M4 observed `unknown, idle, working, done, blocked`
and — the trap — **`done` is the normal post-turn state, not `idle`**. So the gate is
`== "blocked"`, never `!= "idle"`; every other value including null and an unreachable runner
changes nothing. Herdr status is corroboration with exactly one permitted effect (defer) in exactly
one direction (withhold). It never overrides `IsWorkingAsync` toward "idle enough to type", never
feeds a kill decision, never produces a verdict. (S1's false `agent_prompt_stalled` is the standing
proof of why it gets no authority; the transcript remains the only truth.)

**Unblock (no event in S3):** deliveries retry through the triggers that already exist — the
turn-end flush when the approved/rejected modal's turn eventually ends (a rejected dialog persists
the interrupt marker, which IS a turn end — `TranscriptKinds.IsInterruptPrompt`), the 60 s
stranded-queue watchdog for Delegation/Supervision-origin rows (`FlushStrandedQueuesAsync:535` —
herdr sessions are never AlwaysOn but delegation briefs to herdr delegates ARE served by it), and
any human flush. Documented residual: a UI-origin WhenIdle message enqueued while blocked, on a
session whose modal is never answered, sits Pending and visible — the same behaviour that session
would show today if it were merely working. S4's `pane.agent_status_changed` → `FlushIfIdleAsync`
closes that promptness gap; nothing here needs to.

## 7. Decision 7 — blocked during the confirm window

Sequence for the hazard: body typed → Enter → Claude takes the turn → tool call → permission modal
→ herdr flips `blocked` — all while `WaitForTranscriptConfirmAsync` may still be polling for our
record. A scheduled re-press Enter at that moment is a keystroke into a picker whose highlighted
option it would select (CARD-0141's founding accident). This hazard is not herdr-specific — the pty
lanes carry it today — but herdr is the first lane that can SEE the modal cheaply, so:

- **One guard, additive:** inside the confirm loop (`:1503-1513`), immediately before a re-press,
  when the session's delivery backend is Herdr: read live metadata; if `"blocked"`, skip THIS
  re-press (log Information, keep polling; `entersSent` not incremented). The deadline, verdicts
  and everything after them are untouched. Failure analysis both ways: heuristic wrong-positive →
  we lose one re-press; if the body truly needed it, the verdict times out into
  `NoTranscriptRecord` → grace-confirm pulls the transcript → the working-kill guard
  (`HandleDeliveryFailureAsync:2014-2020`) sees the open turn (`IsWorkingAsync` true — a modal is
  mid-turn) and withholds the kill; the message goes back to Pending and late-confirm owns it.
  Heuristic wrong-negative (blocked missed) → today's behaviour exactly. Nothing new can kill.
- **Everything else unchanged, stated as a prohibition:** no herdr branch in verdict
  classification, no blocked→Failed, no blocked→park, no blocked-driven Esc (CARD-0137's overlay
  machinery keeps that job with its own working-gated rules), and `GraceConfirmAsync` needs no
  guard — it presses nothing.

## 8. Decision 8 — marker and text integrity through `send_text` (measured)

M1 pins the full CARD-0055/0024 evidence chain on herdr: the stored `UserPrompt` content is
`-ceq`-identical to the sent body — so `PromptSubmissionMatch.IsConfirmedBy`'s 200-char HEAD window
matches (identity), `IsCompleteIn`'s full-containment holds (completeness), LF newlines survive
exactly (no Grok-style join), and the record contains **zero ESC bytes** — herdr neither strips
our `ESC[200~/201~` (the TUI's paste path consumed them: placeholder rendered, paste landed whole)
nor adds its own wrap that would leak markers into the record or double-wrap ours. `send_text` is
byte-transparent for ASCII+ESC payloads. The build re-pins this through the PRODUCTION path (queue
→ runner → `HerdrPaneChild`) in the B5 smoke; the fake-herdr contract tests pin byte-transparency
of `HerdrPaneChild.WriteAsync` → `pane.send_text` params so a future re-encoding regression goes
red without a live herdr.

Non-ASCII note for the build: M1/M2 were ASCII. The pty lanes' UTF-8 handling concerns don't apply
(no ConPTY narrowing on this path — the pipe carries JSON-escaped UTF-16 into herdr's own writer),
but the B5 smoke includes one em-dash/emoji body to close the gap with a measurement rather than
an argument.

## 9. Decision 9 — the S3/S4 line, drawn in API calls

**S3 may call (all request/response):** `pane.get` (status + revision), `pane.read` (already),
`pane.send_text` / `pane.send_keys` (already), capabilities via the runner HTTP surface. **S3 must
not call:** `events.subscribe` (no pump, no `pane.agent_status_changed`, no `pane.closed`
consumption), `agent.prompt` (never — S1), `pane.report_agent` or any state pushing. S4/CARD-0162
owns the pump and with it: prompt unblock flushes, `pane.closed` → Exited promptness, the
reconnect verification sweep S2's plan §6B described (still unshipped on `79df00d` — S3 does not
inherit that debt; a herdr pane closed under a running runner keeps being caught by the existing
liveness machinery at its existing latency), and status badges/UI. Rationale: the pump is a
long-lived stream with its own scar-tissue obligations (OCE-only-when-cancelled, reconnect+resweep,
dedup) and its payoff is reconciliation and promptness — S4's charter. Polling answers S3's only
question ("is it blocked *right now*, at the moment I'm about to type?") at the moment it matters,
which an event mirror cannot do better.

## 10. Out of scope (unchanged from the card, plus findings)

`agent.prompt` in any form; ceilings changes for pty backends; `PtyBackend`/`PtyBackendPolicy`
edits; the event pump (S4); UI status surfacing (S4); unifying the dispatcher/reply-service sizing
onto per-session resolution (noted for S4); AlwaysOn/channel-bound herdr (refused at S2's gates,
so the AlwaysOn kill arm of `HandleDeliveryFailureAsync` is unreachable for herdr sessions — pinned
by a test, not by reasoning alone); fixing S2's unshipped reconciliation pump.

## 11. Verification / test design

Server tests in `Antiphon.Tests` (TUnit; shared-Postgres rules — every assertion scoped to rows
the test made); runner tests in `Antiphon.SessionRunner.Tests` against the fake herdr pipe server
(extended to serve `pane.get` with a scriptable `agent_status`/`revision`). Named per decision:

- **`DeliveryBackendCeilingsTests`** (extends `PtyDeliveryCeilingsTests`): the herdr set carries
  the three configured numbers and `Backend == HerdrPane`; `IsPastePath` true for herdr; the
  inbox/modern mappings unchanged value-for-value; `ForAgentKind` still zeroes the brief ceiling
  for non-Claude on a herdr record (unreachable via refusals, but the record math must not couple
  to that).
- **`SessionDeliveryProfileTests`**: PtyHost snapshot → delegates to `PtyDeliveryProfile` verbatim;
  Herdr snapshot + runner advertising herdr → herdr set; runner answering WITHOUT herdr →
  conservative inbox set + the downgrade reason naming both facts; runner unable to answer →
  conservative set; unknown session → pty profile answer; snapshot cache never re-queries a
  resolved id (TimeProvider-driven TTL for the capability probe only).
- **`SessionMessageQueueDeliveryVerificationTests` — CARD-0161 cases** (the CARD-0055
  unchanged-verdict requirement, pinned positively):
  (i) a Herdr-snapshot session's delivery goes through `WaitForTranscriptConfirmAsync` and is
  marked Sent ONLY on a matching complete `UserPrompt` row — same fixture shape as the existing
  CARD-0055 cases, backend flipped, zero new verdicts observable;
  (ii) the oversize tripwire on a herdr session fires at `HerdrPaneSingleWriteMaxBytes`, not at
  the process-wide pty number, and still delivers;
  (iii) blocked at flush time → `FlushResult.Nothing`, row still Pending, `DeliveryAttempts`
  unchanged, no incident;
  (iv) `done`/`unknown`/null/unreachable statuses do NOT defer (the vocabulary pin — an equality
  gate, not an inequality gate);
  (v) blocked during confirm → the scheduled re-press is withheld while blocked, a record arriving
  late still confirms Sent, and on timeout the failure path runs with the kill withheld because
  working is true;
  (vi) blocked never parks, never cancels, never kills: end state of an exhausted blocked defer is
  Pending-and-visible.
- **`HerdrRunnerSessionTests` additions** (runner, fake herdr): single-session GET on a herdr
  session issues one `pane.get`, folds `revision` into `LastSequence` and reports `AgentStatus`;
  `ListAsync` issues NO herdr calls; a pty session's DTO carries `AgentStatus == null`;
  `GetSnapshot` still the only `pane.read` producer for text. Plus the byte-transparency pin: the
  exact wrapped payload handed to `WriteAsync` appears byte-identical (markers included) in the
  fake's recorded `pane.send_text` params, and `"\r"` alone becomes `send_keys ["enter"]`.
- **`HerdrClientSurfaceTests`** (the agent_prompt_stalled-avoidance requirement, as a compile-time
  fact made test-visible): reflection over `HerdrClient`'s public surface asserts no member sends
  `agent.prompt` — the method-name list of typed wrappers is pinned, so adding one is a
  deliberate, red-first act.
- **Probe PR1** (build-time, live, scripted like CARD-0160's P1–P6): `pane.get.revision` ≡
  `pane.read.revision` on the same pane; result recorded in the build commit. PR2: the B5 smoke —
  one queued delivery through the full production path (server queue → runner → herdr → real
  Claude, ASCII 86 400 B + one small non-ASCII body), transcript-confirmed Sent, results appended
  to this plan's measurement table.

## 12. Build order

1. **B1 — axis + knobs (server, dark):** `DeliveryBackend`, `PtyDeliveryCeilings` retype,
   `DelegationSettings` herdr knobs + `HerdrCeilings`, mapping. Nothing consults it yet.
   `DeliveryBackendCeilingsTests` green; existing ceiling tests updated mechanically.
2. **B2 — `SessionDeliveryProfile` + queue wiring:** per-session resolution threaded through
   `DeliverNextLockedAsync`/`SendNowAsync`/`SpillQueueBodyAsync`/`DeliverAsync`; capability
   corroboration with downgrade logging; dispatcher/reply-service annotated as deliberately
   process-wide. `SessionDeliveryProfileTests` + queue case (ii).
3. **B3 — runner status/revision surface:** PR1 probe first; then the single-GET enrich,
   `RunnerSessionDto.AgentStatus`, `AgentSessionLiveMetadata.AgentStatus`.
   `HerdrRunnerSessionTests` additions.
4. **B4 — blocked arms in the queue:** pre-send defer + confirm-loop re-press withhold. Queue
   cases (i), (iii)–(vi); `HerdrClientSurfaceTests`.
5. **B5 — live smoke + docs:** PR2 through the production path; CLAUDE.md gotcha line under the
   CARD-0160 entry (herdr delivery: same CARD-0055 verdict, per-session ceilings via
   `SessionDeliveryProfile`, `blocked` defers and only defers, `done` ≠ `idle`); card closed with
   the measured numbers.

Slices 1–2 and 3–4 are independently shippable dark; nothing observable changes for pty sessions
at any point (pinned by the existing delivery suites staying green untouched, except the
mechanical ceiling-record retype).
