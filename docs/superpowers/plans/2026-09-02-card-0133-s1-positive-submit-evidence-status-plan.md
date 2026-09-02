# CARD-0133 S1 — positive post-Enter submit evidence for Codex: what shipped, what it proves, what is left

**Date:** 2026-09-02 (Plan pass, task `714fec2c` — design only; no production code changed).
**Card:** CARD-0133 (`c3a34ac6-5476-483b-bae2-1d5b7cb63753`, InProgress, High).
**Relates to:** the 2026-08-27 plan (`2026-08-27-card-0133-codex-readiness-and-boot-wedge-plan.md`, §2.3 is S1's
original spec), the 2026-08-30 harness diagnosis (`docs/investigations/2026-08-30-card-0133-s0-harness-diagnosis.md`),
CARD-0299 (`docs/investigations/2026-09-01-card-0299-codex-plan-unsubmitted.md` and its fix plan).
**Sources verified this pass:** `SessionMessageQueueService.cs` at `d70c557a` (`DeliverAsync` :1676–1897,
`WaitForTranscriptConfirmAsync` :1920–2120, `SettlePostEvidenceAsync` :2347–2385, `HandleDeliveryFailureAsync`
:2620–2760, `TryHandleCodexBootWedgeAsync` :2879–2960); `src/Antiphon.Agents.Pty/SubmitEvidence.cs`,
`ComposerDeliveryEvidence.HeadFragmentIsVisible`, `CodexDetectors.cs` (`CodexWorkingIndicator`, `CodexMcpBoot`);
`RunnerCodexAdapter.cs`; `CodexSubmitConfirmation.cs`; `AgentSessionService.SendBootPromptWithRetryAsync` :583–640;
`AgentTaskDispatcher.FailNeverStartedAsync` :844; `DeliveryVerificationSettings`; `AgentRegistrySettings`
Codex knobs; commits `09a6a8ba`, `f77d0d63`, `fd6c8a50`, `78629298`, `94b29a4e`, `89642ce0`; the live dev
database (census re-run, session/queue/incident rows); `server/logs/antiphon-202608{30,31}.log` and
`antiphon-202609{01,02}.log`; and the pins listed in §6, all run at HEAD in this pass.

---

## Verdict up front

| Question the brief posed | Answer, verified |
|---|---|
| Does the queue still "mark a submit Sent on sequence advance from the body's own render frames" for Codex? | **No — not since 2026-08-27.** S1 shipped as `09a6a8ba` (`fix(queue): require positive Codex submit evidence`), three days *before* the card text that says "Next: dispatch S1" was written. For a Codex session `sawSequenceAdvance` is a log detail only (`:1974–1980`); `sawPositiveSubmit` is set solely by the Working indicator or a *sustained* emptied composer (`:1982–2016`). The brief's premise describes the pre-`09a6a8ba` code, and still describes Claude/Grok (§7). |
| Was `09a6a8ba` complete? | **No — it had a latch hole**, found live by CARD-0299 on 2026-09-01 (session `41959e81`): one transient empty/ghost/MCP-spinner frame latched emptied-composer, suppressed every re-Enter, and certified `Sent` after 1 Enter while the durable last frame still held the brief. Closed by CARD-0299 S1 (`fd6c8a50`): emptied-composer must persist `PostEvidenceSettleMs` (500 ms) of consecutive polls, un-latches if the head reappears, and the unobservable deadline re-reads the current screen — body still visible and no Working → `NoSubmitOutput`. |
| Is the rest of the 08-27 plan's "measured failure" half (S2, S3) shipped? | **Yes, under CARD-0299** (`78629298` S2 `BootWedged` + kill + one relaunch; `94b29a4e` S3 MCP-boot-line gate in both Codex adapters; `89642ce0` S4 docs). Deployed with the 2026-09-01 23:55 +01 restart. Only the 08-27 plan's S4 (`ComposerInputProbe` as the ready gate) and S0-P4 (the composer-clear keystroke) remain unshipped, and both were deliberately deferred by CARD-0299. |
| Does S1 work in production? | **It has caught two real wedges and been fooled twice.** Caught: `5914b66e` (08-30 05:25 +01) and `0bf7fc83` (08-31 15:17 +01) — 3 Enters, `NoSubmitOutput` at 30 s, revert, the 60 s stranded sweep re-typed, both tasks **Succeeded**. Fooled (both pre-`fd6c8a50`): `a3631539` (08-30, 2 Enters, degraded Sent) and `41959e81` (09-01, 1 Enter, degraded Sent), both failed by the 10-minute watchdog. |
| Is the hardened S1 + S2 + S3 verified in production? | **Not yet, and it cannot be from today's data: zero Codex sessions have started since the 2026-09-01 22:55Z deploy.** `AgentIncidents` has no `BootWedged` (44) row and no task has `BootWedgeRelaunchCount > 0`. The census (139 Codex sessions since 08-20, 13 signature rows; 56 / 3 since 08-27) is also blind to a *caught* wedge, because a caught wedge leaves a Canceled queue row, not a `Sent` one (§5, S1b-C). |
| Is `disable_paste_burst` a duplicate of S1? | **No.** The flag removes one *mechanism* (Codex's `PasteBurst` folding an Enter that lands ≤120 ms after a typed burst). S1 is *detection* at delivery time, mechanism-agnostic; S2 is *recovery*. The 09-01 wedge happened with the flag on: the TUI froze during MCP boot and emitted zero bytes for 9.5 minutes — no flag can reach that, only a positive-evidence verdict can (§3.4). |
| What is genuinely left for "S1"? | One real hole on a path the card names and the queue does not cover — the **named/card-launch boot prompt** (`RunnerCodexAdapter.SendPromptAsync` → `CodexSubmitConfirmation`) still returns blind success on a first turn with no rollout, even with the body standing in the composer (§4 R1). Plus the production verification that nobody has been able to run yet (R2), and the card/doc corrections (R3). Everything else is either measured-out-of-scope or needs a measurement first (§7). |

**Recommendation:** treat CARD-0133 S1 as shipped (`09a6a8ba` + `fd6c8a50`), correct the card, and dispatch the
three small residual slices in §5 (≈4–6 h total) as "S1b". Do not re-implement S1.

---

## 1. What shipped, read out of the tree

### 1.1 Delegate-brief path (`SessionMessageQueueService`), the path the nine original wedges took

1. **Settled baseline before Enter** — `SettlePostEvidenceAsync` (`:2354`): after composer evidence, wait until the
   output sequence has been unchanged for `PostEvidenceSettleMs` (500, clamped 0–3000), bounded 3 s, and capture
   `(Sequence, Screen)` at that instant. The Enter follows a *finished* composer, so the body's own trailing
   render frames can no longer land past the baseline. Shared by every verified kind.
2. **Codex predicate** (`:1982–2016`, unobservable branch of `WaitForTranscriptConfirmAsync`):
   - `CodexWorkingIndicator.IsVisible(screenNow)` → `workingLatched = sawPositiveSubmit = true`, never un-latched.
   - else `SubmitEvidence.IsEmptiedComposer(screenBeforeSubmit, screenNow, body)` (head fragment visible in the
     settled pre-Enter screen **and** not visible now) → `emptiedSince ??= now`; positive only once
     `now − emptiedSince ≥ PostEvidenceSettleMs`; any snapshot that shows the head again resets both.
   - `sawSequenceAdvance` is computed (`:1974`) but for Codex feeds only the log line at `:2067`.
3. **Re-press until positive** — `mayReEnter = entersSent < SubmitAttempts && … && (observable || !sawPositiveSubmit)`
   (`:2076`): 3 Enters at 0 / 7 / 14 s unless positive evidence has appeared.
4. **Deadline re-check** (`:2025–2040`): at `TranscriptConfirmTimeoutSeconds` (30) on the unobservable branch, if
   `!workingLatched` and `HeadFragmentIsVisible(deadlineSnapshot, body)` → `NoSubmitOutput` regardless of any
   earlier empty frame. Only then does the existing arm apply: `sawPositiveSubmit` → degraded
   `Confirmed(Screen)` + `RecordDeliveryUnverifiedAsync`; else `NoSubmitOutput`.
5. **Recovery hook** (`:2680`, `TryHandleCodexBootWedgeAsync`): `NoSubmitOutput` + origin Delegation + attempts 1 +
   null baseline + Codex Running + a Dispatched task on that session → `BootWedged` incident, queue row Canceled,
   `KillAsync` regardless of AlwaysOn, `RelaunchWedgedAsync` once (`BootWedgeRelaunchLimit` 1), then
   `FailWedgedAtLimitAsync`.
6. **Ready gate** (`RunnerCodexAdapter.WaitForReadyAsync` :127–147 and `CodexReadyDetector.WaitAsync`): quiet + trust,
   then `CodexMcpBoot.WaitUntilAbsentAsync` (line absent 500 ms, bound `CodexBootStatusMaxWaitMs` 10 s, Warning
   and proceed on expiry).

`ProviderContractCatalog.Codex.DeliveryVerification` (`:171–173`) already carries the contract prose: "A submit is
proven by the Working indicator (immediate) or an emptied composer sustained across consecutive snapshots for
PostEvidenceSettleMs — a single empty/ghost poll is not evidence (CARD-0299). Sequence advance alone is the body's
own render and is not evidence. A body still visible at the unobservable deadline is NoSubmitOutput."

### 1.2 Pins, run at HEAD (`d70c557a`) in this pass — all green

| Suite | Filter | Result |
|---|---|---|
| `Antiphon.Tests` | `SessionMessageQueueDeliveryVerificationTests/Codex_*` | 7 / 7 |
| `Antiphon.Tests` | `SessionMessageQueueBootWedgeTests/*` | 8 / 8 |
| `Antiphon.Tests` | `RunnerCodexAdapterSubmitConfirmTests/*` | 6 / 6 |
| `Antiphon.Agents.Pty.Tests` | `SubmitEvidenceTests/*` | 5 / 5 |

The 08-27 plan's named red-today pin `Codex_unobservable_body_trailing_frames_are_not_submit_evidence_and_re_enter_until_no_submit_output`
exists and passes; CARD-0299's `Codex_unobservable_transient_empty_frame_does_not_latch_emptied_composer` exists and
passes.

### 1.3 Timeline, for the record

| When (+01) | What |
|---|---|
| 08-27 06:26 | `09a6a8ba` S1: settled baseline, `SubmitEvidence`, Codex positive predicate, `PostEvidenceSettleMs`. Task `f12bceb3`. |
| 08-27 05:26 (card) | Card addendum: "S1 … is proceeding separately." |
| 08-30 13:27 | `f77d0d63` `-c disable_paste_burst=true` on every Codex launch. |
| 08-30 13:37 (card) | Card addendum says S1 "was deliberately NOT touched by this fix and should still be prioritized … the queue currently marks a submit Sent on sequence advance" — **already false for Codex at the time of writing** (the author did not see `09a6a8ba`). |
| 09-01 10:30Z | Session `41959e81` wedges through the S1 latch hole (CARD-0299 incident). |
| 09-01 19:13–19:46 | `fd6c8a50` / `94b29a4e` / `78629298` / `89642ce0` (CARD-0299 S1 hole, S3, S2, docs). |
| 09-01 23:55 | Server restart carrying all four. **No Codex session has started since.** |

---

## 2. Live evidence

### 2.1 Census (`scripts/codex-boot-census.ps1`, re-run 2026-09-02)

139 Codex sessions since 2026-08-20; 13 boot-wedge signature rows (0 `TranscriptEntries`, brief `Sent`, attempts 1,
null baseline, ANSI frame closed, no `Working (`). Since 08-27 (post-`09a6a8ba`): 56 sessions, 3 signature rows —
`5ab31a20` (08-27 20:24Z, CARD-0216), `a3631539` (08-30 04:36Z, CARD-0230), `41959e81` (09-01 10:30Z, CARD-0288).
All three are the latch hole (`fd6c8a50` closes it) and all three predate the 09-01 deploy. The last Codex session in
the database started 2026-09-01 19:12Z.

### 2.2 What S1 did on each Codex wedge since it shipped (server logs)

| Session | Enters | Verdict at 30 s | Then | Outcome |
|---|---|---|---|---|
| `5914b66e` 08-30 05:25 | 3 | `NoSubmitOutput` (correct) | revert → stranded sweep re-delivered at +64 s | 1 UserPrompt, task Succeeded |
| `a3631539` 08-30 05:36 | 2 | degraded `Sent` (latch hole) | 10-min watchdog | Failed, 0 rows |
| `0bf7fc83` 08-31 15:17 | 3 | `NoSubmitOutput` (correct) | revert → sweep re-delivered at +83 s | 1 UserPrompt, task Succeeded |
| `41959e81` 09-01 11:30 | 1 | degraded `Sent` (latch hole) | 10-min watchdog | Failed, 0 rows (CARD-0299) |

Two observations that matter for the design: (a) the two correct verdicts recovered **without** a relaunch — the
sweep's re-type + Enter worked ~70 s later, so "TUI stopped reading" is not the only wedge shape; some are
"Enter dead for a while"; (b) both escapes are the single-poll latch, now closed. `5914b66e` and `0bf7fc83` both
ran with `disable_paste_burst` **on** (`0bf7fc83` also after S3's MCP gate did not exist yet), which is the
second piece of evidence that the flag is not sufficient.

### 2.3 The other kinds, for calibration

Of the 18 "degraded screen-only verdict" / "submit Enter produced no output" lines between 08-30 and 09-02, 12 are
Grok (kind 4) and 2 Claude (kind 1); all 14 went on to produce UserPrompt rows and succeed. Those kinds still use
sequence advance past the settled baseline as their positive signal (§7 R6). Not this card's, but it is the shape the
brief's premise sentence still literally describes.

---

## 3. S1 design, stated as it now stands (the brief's three questions)

### 3.1 What counts as positive post-Enter submit evidence for Codex

In strength order; the first that holds ends the wait.

1. **A `UserPrompt` transcript row whose text carries our body** (`PromptSubmissionMatch.IsConfirmedBy` +
   `IsCompleteIn`). Observable branch: past the stored sequence baseline. Unobservable branch (zero rows at type
   time — every cold delegate, because Codex creates the rollout on first submit): any UserPrompt/QueuedUserPrompt
   with `Timestamp ≥ UtcNow − 30 s` captured before the body write (CARD-0164). This is the only verdict that
   produces `Confirmed(Transcript)`; AGENTS.md's "transcript-confirmed UserPrompt evidence is the delivery verdict".
2. **The Working indicator** — a rendered-screen line carrying both `Working (` and `esc to interrupt)`
   (`CodexWorkingIndicator`). Codex paints it within ~1 s of a real submit and repaints at ~1 Hz. Latched on first
   sight. Screen-level, so it yields the *degraded* `Confirmed(Screen)` + `DeliveryUnverified` incident at the
   deadline, never an early Delivered.
3. **The composer emptied, and stayed empty** — the body's head fragment (`ComposerDeliveryEvidence.FragmentSpan`
   chars, whitespace-normalised) was visible in the *settled* pre-Enter screen and has been absent for
   `PostEvidenceSettleMs` of consecutive polls, with no later poll showing it again. Measured on the control
   session: the composer clears and the ghost hint (`› Improve documentation in @filename`) returns *before*
   Working appears, so this covers the sub-second gap between submit and indicator. Same degraded tier as 2.

**Explicitly not evidence:** the output sequence advancing (any redraw does that — the body's own trailing frames,
the MCP spinner, a status-bar tick); a single snapshot without the body (mid-redraw, ghost hint, spinner frame —
the CARD-0299 hole); quiet; `Starting MCP servers (1/2): node_repl (1s  esc to interrupt)` (has the suffix, lacks
the `Working (` prefix — checked); Herdr's `agent_status`; the sidecar.

**And the negative verdict is positive too:** at the unobservable deadline, a screen that still shows the head
fragment with Working never seen is `NoSubmitOutput`, whatever happened in between.

### 3.2 Where it plugs in

Already plugged in; nothing to add on this path:

| Step | Location |
|---|---|
| settle + capture `(Sequence, Screen)` | `DeliverAsync` :1864 → `SettlePostEvidenceAsync` :2354 |
| Enter | `DeliverAsync` :1869 (`\r`, separate write, 20 ms after settle) |
| per-poll predicate, latch/un-latch | `WaitForTranscriptConfirmAsync` :1982–2016 |
| re-press gate | :2076 (`observable \|\| !sawPositiveSubmit`) |
| deadline re-check → `NoSubmitOutput` | :2025–2040 |
| degraded Sent + `DeliveryUnverified` | :2046–2056 |
| `NoSubmitOutput` → `BootWedged` → kill → relaunch once | `HandleDeliveryFailureAsync` :2680 → `TryHandleCodexBootWedgeAsync` :2879 |
| fallback if the conjunction fails (reused session, Ui origin, attempt 2…) | revert to Pending → 60 s stranded sweep → re-type (§2.2 shows this recovering) → park at 3 → 10-min `FailNeverStartedAsync` |

### 3.3 The legacy switch

`DeliveryVerificationSettings.TranscriptConfirmEnabled = false` (default true, no override in any `appsettings*.json`)
skips `WaitForTranscriptConfirmAsync` entirely and takes `WaitForSequenceAdvanceAsync` (`:1886`) — for every kind,
Codex included. That is the CARD-0055 kill switch and it re-opens the exact false positive S1 closes. Documented in
§7 R5; not a slice.

### 3.4 How S1 relates to `disable_paste_burst` (and to S2/S3)

| Layer | What it does | What it cannot do |
|---|---|---|
| `-c disable_paste_burst=true` (launch) | Removes Codex's 120 ms Enter-suppression window after a typed burst — the mechanism the herdr canary reproduced 3/3 at the production 20 ms gap. | Nothing for an Enter that Codex never processed: the 09-01 wedge froze mid-MCP-boot with the flag on; `0bf7fc83` needed a re-type with the flag on. |
| S3 MCP-boot gate (ready) | Refuses to type while `Starting/Booting MCP server` is on screen (typing there is MCP-interrupt / queued-input, CARD-0195). | Nothing once typing has started. |
| **S1 positive evidence (delivery)** | Names the truth at +30 s: `Sent` only on transcript / Working / sustained-empty; else `NoSubmitOutput`. Mechanism-agnostic. | Recover on its own — it only decides. |
| S2 `BootWedged` (recovery) | Turns a cold-delegate `NoSubmitOutput` into kill + one relaunch (~40 s instead of 600 s). | Distinguish "child frozen" from "our pipe stalled" (S0-P2, never measured); relaunch limit 1 is the hedge. |

They are one ladder, not three fixes for one bug. Removing the flag would raise the rate S1 has to catch; removing S1
would put the flag's residual failures back on the 10-minute watchdog. Keep all of them.

---

## 4. Residual gaps, graded by evidence

| # | Gap | Evidence | Verdict |
|---|---|---|---|
| R1 | **Named/card-launch boot prompt is still blind on a first turn.** `CodexSubmitConfirmation.SubmitAsync` :118–127: after `CodexSubmitConfirmTimeoutMs` (20 s) and `CodexSubmitAttempts` (3 extra Enters, 4 s apart), if the transcript never produced *any* row it `return`s success with a Warning that merely *mentions* whether the body is still visible. The card names this path. Today a wedge here is caught only by `WaitForTurnCompleteAsync` at `CodexDoneMaxWaitMs` (300 s) → "Timed out waiting for the agent turn to complete" (`AgentSessionService` :245) — right outcome, five minutes late, wrong reason. 4 of the 56 Codex sessions since 08-27 are task-less (named/human launches), so the path is live. | code + card | **Fix — S1b-A.** Same predicate, same helper family, applied where the transcript is absent. |
| R2 | **The hardened backstop is unverified in production and the census cannot see a caught wedge.** 0 Codex launches since the deploy; `BootWedgeSignature` requires `q.Status == Sent`, so a caught wedge (Canceled row + `BootWedged` incident + relaunch) is invisible to the only regression instrument this card has. | DB + script | **Fix — S1b-C.** |
| R3 | **Card and docs are stale.** Card text says S1 is not started; the 08-27 plan's §2.3/§2.4/§2.6 read as pending; `docs/agent-kinds.md` §6 lists no delivery-verification behaviour for Codex (the invariant lives only in `session-runtime-invariants.md` gotcha #123 and the catalog). | docs | **Fix — S1b-D.** |
| R4 | Working-indicator arm accepts a Working line that was *already* on the settled pre-Enter screen (a session mid-turn with no rollout bound). | logic only; no incident | **Not a slice** — needs the measurement in §7 first (what Codex does with Enter while working). |
| R5 | `TranscriptConfirmEnabled=false` bypasses S1 for Codex. | config | Document; no override exists. |
| R6 | Claude/Grok unobservable branch still certifies on sequence advance past the settled baseline. | 14 degraded verdicts 08-30..09-02, all later confirmed | Sibling card if a Claude/Grok wedge is ever measured; out of CARD-0133. |

---

## 5. Slices ("S1b")

Sequential, Shared workspace, one PR each or one PR for A+D. Nothing here widens a timeout or weakens a verdict.

### S1b-A — Named/card-launch path parity: the blind branch consults the same positive evidence (2–3 h)

**Files:** `server/Infrastructure/Agents/CodexSubmitConfirmation.cs`; `RunnerCodexAdapterSubmitConfirmTests.cs`
(+ `ScriptedCodexRunnerClient` if a knob is missing); one `AgentSessionLaunchFailureTests` arm; `ProviderContractCatalog`
prose; `docs/session-runtime-invariants.md`.

**Change,** confined to the `!anyRowEverSeen` branch of `SubmitAsync` (the "no transcript ever" degrade):

1. Look twice, `CodexMcpBoot.AbsentSettle` (500 ms) apart, at the rendered screen after the last Enter's window.
2. `CodexWorkingIndicator.IsVisible` on either look → `return` (today's degraded success), log "confirmed by Working
   indicator; transcript never bound" at Warning.
3. `ComposerStillShows(screen, body)` on **both** looks and no Working → `throw new PromptDeliveryException(…,
   composerMayHoldBody: true)` with the existing "STILL SHOWS" sentence plus "transcript never produced a row".
4. Neither (body gone, no Working, no rows) → today's blind `return` and Warning, unchanged. This keeps
   `A_session_with_no_observable_transcript_degrades_to_a_blind_send_instead_of_failing` green: `IdleScreen` holds
   no body.

**Why this is safe and what it changes downstream.** `SendBootPromptWithRetryAsync` :607–631 already keys on
`ComposerMayHoldBody`: it skips the re-type (never splices), runs `TryLateConfirmBootPromptAsync` (a fresh boot
has nothing to confirm — CARD-0055's boot scope-out stands) and rethrows; the launch catch runs `KillAndDisposeAsync`
(CARD-0056) and the session is `Failed` with the real reason at ~25 s instead of `Timed out waiting for the agent
turn to complete` at 300 s. No new kill is introduced — the same session is killed today, later. The in-process
`CodexAdapter` (`server/Infrastructure/Agents/Pty/CodexAdapter.cs`) shares the helper, so both adapters move together.

**Not changed:** the transcript-live branch (throws today, keeps throwing); the Enter cadence
(`CodexSubmitReEnterIntervalMs` 4000 / `CodexSubmitAttempts` 3 / `CodexSubmitConfirmTimeoutMs` 20000 — the card's
"recovers via a 4 s retry" is this cadence and it stays, it is the Enter-only re-press contract); `SendLineAsync`
(body, 20 ms, `\r`) — a pre-Enter evidence gate on the runner path is CARD-0128's genre, not this slice.

**Tests (`RunnerCodexAdapterSubmitConfirmTests`):**
- `A_blind_first_turn_with_the_body_still_standing_after_every_Enter_throws_composer_may_hold_body` —
  `ThrowOnTranscript = true`, `ConfirmAfterEnters = 0`, `IndicatorScreenReads = 0`, `QuietScreen` = `IdleScreen`
  with the body's head on the `> ` row → `PromptDeliveryException.ComposerMayHoldBody == true`, `Enters == 4`,
  `BodyWrites == 1`.
- `A_blind_first_turn_that_shows_the_Working_indicator_is_a_degraded_success` — `ThrowOnTranscript = true`,
  `IndicatorScreenReads = 100` → no throw, `BodyWrites == 1`.
- existing `A_session_with_no_observable_transcript_degrades_to_a_blind_send_instead_of_failing` unchanged (neither
  body nor Working on `IdleScreen`).
- `AgentSessionLaunchFailureTests`: `PromptFailure` returning `PromptDeliveryException(composerMayHoldBody: true)` on a
  Codex fake → exactly one body write, session `Failed`, adapter killed (mirrors the existing CARD-0056 pins).

Two consecutive looks rather than one so the reverse of the CARD-0299 transient (a stale frame that *shows* the
body an instant before it clears) cannot fail a launch on its own; `ComposerStillShows` stays a 40-char head look,
whitespace-squashed, as today.

### S1b-C — Production verification of the shipped backstop; census sees caught wedges (1–2 h)

**Files:** `scripts/codex-boot-census.ps1`; card note.

1. Add to the SELECT: `EXISTS(BootWedged incident for the session)` (kind 44), `t."BootWedgeRelaunchCount"`, and the
   brief row's `"CanceledAt"`. Classify each cold Codex delegate as **escaped** (today's `BootWedgeSignature`),
   **caught** (`BootWedged` row, brief Canceled, relaunch count ≥ 1 on the task, or `FailWedgedAtLimit` reason),
   **recovered-by-sweep** (`NoSubmitOutput` then attempts ≥ 2 and ≥ 1 UserPrompt — `5914b66e`'s shape), or clean.
   Print the four counts in the summary line; keep the script read-only and its 14-column split intact.
2. Checkpoint rule for closing S1 on the card: **≥ 40 cold Codex delegate launches after 2026-09-01 22:55Z with
   0 escaped**; every wedge in that window is caught or recovered-by-sweep. Today the count is 0, so the checkpoint
   is open, not failed. This is the 08-27 plan's S5 / the card's "P5", with the instrument fixed.
3. If an escaped row appears post-deploy: pull its ANSI log before anything else (AGENTS.md: a transcript's absence
   is not proof) and attach the last frame to the card — the predicate needs a new shape, not a wider timeout.

### S1b-D — Card and doc corrections (1 h)

- **Card addendum** (via `card.ps1 edit -DescriptionFile` — it *replaces* the description, so prepend the existing
  text): the ready-to-paste block in §9.
- **08-27 plan:** status banner at the top naming what shipped where (`09a6a8ba`, `fd6c8a50`, `78629298`,
  `94b29a4e`) and that S0-P4 / S4 are the only open items — the same blockquote form the 08-27 probe doc received.
  This pass adds that banner (the only file edit besides this document).
- **`docs/agent-kinds.md` §6** "Behaviour worth knowing": one bullet on delivery verification — settle, Working or
  sustained-empty, sequence advance is not evidence, `NoSubmitOutput` at 30 s, cold-delegate relaunch once — pointing
  at the catalog prose as the owner. Today §6 is silent on it.
- **`docs/session-runtime-invariants.md`** gotcha #123 gains one sentence after S1b-A: the boot-prompt path degrades
  to the same Working / body-still-visible look when no transcript ever binds.

---

## 6. Test matrix and commands

| Layer | Pin | State |
|---|---|---|
| `Antiphon.Tests` Application | `Codex_unobservable_body_trailing_frames_are_not_submit_evidence_and_re_enter_until_no_submit_output` | green at HEAD |
| `Antiphon.Tests` Application | `Codex_unobservable_no_post_enter_output_returns_no_submit_output_after_three_enters` | green |
| `Antiphon.Tests` Application | `Codex_unobservable_transient_empty_frame_does_not_latch_emptied_composer` | green |
| `Antiphon.Tests` Application | `Codex_unobservable_working_indicator_confirms_by_screen` | green |
| `Antiphon.Tests` Application | `Claude_unobservable_keeps_advance_based_screen_verdict_after_settled_baseline` | green |
| `Antiphon.Tests` Application | `SessionMessageQueueBootWedgeTests` (8: happy, second-wedge fails, five negations, AlwaysOn kill unchanged) | green |
| `Antiphon.Tests` Agents | `RunnerCodexAdapterSubmitConfirmTests` (6) + the 2 new S1b-A arms | 6 green; 2 to add |
| `Antiphon.Tests` Application | `AgentSessionLaunchFailureTests` + 1 Codex `ComposerMayHoldBody` arm | to add |
| `Antiphon.Agents.Pty.Tests` | `SubmitEvidenceTests` (5) | green |
| Inherited red | the two CARD-0195 known-red names in `SessionMessageQueueDeliveryVerificationTests` — re-run at base before blaming a slice | unchanged |

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0133s1/ -- --treenode-filter "/*/*/RunnerCodexAdapterSubmitConfirmTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0133s1/ -- --treenode-filter "/*/*/AgentSessionLaunchFailureTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0133s1/ -- --treenode-filter "/*/*/SessionMessageQueueDeliveryVerificationTests/Codex_*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0133s1/ -- --treenode-filter "/*/*/SessionMessageQueueBootWedgeTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c0133s1/ -- --treenode-filter "/*/*/SubmitEvidenceTests/*"
pwsh -NoProfile -File scripts/codex-boot-census.ps1
```

Forward slash on `OutputPath`; `Antiphon.Tests` then `Antiphon.Agents.Pty.Tests`, never together; delete every
`bin-c0133s1` directory afterwards (about a dozen).

---

## 7. Deliberately not in scope, and why

- **Re-implementing S1 on the queue path.** Shipped and pinned (§1). A second implementation would be the "third
  shape" this repo refuses.
- **R4, the Working-before-Enter tightening.** Requiring Working *absent* in the settled pre-Enter screen is
  logically stronger, but whether Codex clears its composer when Enter *queues* a message mid-turn is unmeasured.
  If it does not, the emptied arm cannot take over, `NoSubmitOutput` reverts a row that Codex actually queued, and
  the sweep re-types it — a double-send, which is the one thing CARD-0055 forbids. Measure first (a
  `CodexComposerCanaryTests` arm: type + Enter while `Working (` is up; assert composer state and whether a
  `QueuedUserPrompt`-equivalent row lands). Then decide. The delegate path never hits this: a cold delegate is idle
  and unbound at first delivery.
- **A post-Sent "rollout bound within N s" check** after a degraded screen verdict. Rejected: a legitimately
  bind-refused session (`TranscriptBindFailed`, CARD-0064) is working with no rows, and killing it is the false
  positive CARD-0056 was filed over. The 10-minute `FailNeverStartedAsync` already pulls the transcript before it
  judges; keep it.
- **Widening or narrowing** `TranscriptConfirmTimeoutSeconds`, `PostEvidenceSettleMs`, `CodexSubmitConfirmTimeoutMs`,
  `DeliveryFailTimeoutMinutes`, `CodexReadyQuietPeriodMs`. A frozen TUI is silent forever; the caught wedges show the
  present cadence recovers the non-frozen ones.
- **08-27 S4 (`ComposerInputProbe` as the Codex ready gate) and S0-P4 (clear keystroke).** Still unmeasured; CARD-0299
  chose the deadline re-check + relaunch instead of a failure-time probe. Reopen only if the post-deploy census
  shows escaped wedges whose last frame has *no* body (a pre-body dead zone, the CARD-0103 genre), which none of
  the 13 rows do.
- **R6 (Claude/Grok advance-based fallback)** and **R5 (`TranscriptConfirmEnabled=false`)** — documented above; no
  measured failure; separate cards if one appears.
- **The herdr lane's `pane.send_text` dropping bracketed-paste markers** (CARD-0187 territory, noted in the 08-30
  diagnosis).

---

## 8. Decisions for the caller

1. **Accept "S1 shipped" and correct the card** (§9 text), then dispatch S1b-A + S1b-D as one Code task
   (Codex terra / Grok — mechanical against §5), S1b-C as a small script task (luna). Recommended.
2. **Or** keep the card's literal "dispatch S1" and re-implement — not recommended; there is nothing to implement
   on the queue path, and a Code delegate given the stale brief will either discover this and stop or duplicate
   `09a6a8ba`.
3. Whether S1b-A's fail-fast on a card `-Spawn` Codex launch is wanted (25 s `Failed` with the real reason vs today's
   300 s `Timed out waiting for the agent turn`). The plan assumes yes: same kill, earlier, honest reason, and it is
   the CARD-0056 posture.
4. The close checkpoint in S1b-C (≥ 40 post-deploy cold launches, 0 escaped). Today's count is 0 launches, so the
   card stays open on evidence, not on work.

---

## 9. Card addendum, ready to paste (prepend the existing description — `-DescriptionFile` replaces it)

```
## S1 status corrected, 2026-09-02

S1 (positive post-Enter submit evidence in SessionMessageQueueService) SHIPPED on 2026-08-27 as 09a6a8ba — three
days before the 08-30 addendum that says it was untouched. For Codex the queue has not used sequence advance as
submit evidence since then; the 08-30 sentence describes the pre-09a6a8ba code. 09a6a8ba had a single-poll latch
hole (CARD-0299, session 41959e81, 09-01): fixed by fd6c8a50 (emptied-composer must persist PostEvidenceSettleMs,
deadline re-check → NoSubmitOutput). CARD-0299 also shipped this card's S2 (78629298: BootWedged + kill + one
relaunch) and S3 (94b29a4e: MCP boot line must clear before ready). Deployed 2026-09-01 23:55 +01.

Live record since S1: caught 5914b66e (08-30) and 0bf7fc83 (08-31) — NoSubmitOutput at 30 s, sweep re-delivered,
both Succeeded; fooled a3631539 (08-30) and 41959e81 (09-01) through the latch hole. Zero Codex launches since the
09-01 deploy, so the hardened backstop is unverified in production and the census cannot yet say anything.

Remaining, per docs/superpowers/plans/2026-09-02-card-0133-s1-positive-submit-evidence-status-plan.md:
S1b-A named/card-launch boot prompt (CodexSubmitConfirmation blind branch) applies the same Working / body-still-
visible look and throws ComposerMayHoldBody instead of returning blind; S1b-C census counts caught vs escaped
wedges, close S1 at ≥40 post-deploy cold launches with 0 escaped; S1b-D docs. Still open from the 08-27 plan:
S0-P4 (clear keystroke) and S4 (ComposerInputProbe ready gate), both deferred on purpose.
```
