# CARD-0103 — Readiness must prove the TUI reads input: plan

**Date:** 2026-08-20
**Status:** planned (not implemented)
**Card:** CARD-0103 (`5a6205d3-2a7e-4055-be62-ee0c4b034184`) — delegate brief delivery fails on a
TUI that is painted but not yet reading input; Enter withheld, message parked, task watchdog-killed.
**Evidence base:** the card's revision-2 investigation (task `4f0dbaf6`), live-reproduced three
times 2026-08-20 against the real always-on runner: the same 5 829-char paste rendered in **48.8 s**
when sent ~2 s after "ready" and **0.74 s** when sent 45 s after "ready". This plan does not
re-derive any of that; every number below that is not newly verified against the tree is the card's.
**Precedent:** CARD-0047 (silent trust modal read as ready), CARD-0048 (silent DA1 stall read as
ready), CARD-0052 (`c1bd1c8` — quiet cannot count until visible output; **verified landed**, and
`RunnerClaudeAdapter.WaitForReadyAsync` already calls `WaitForQuietAfterVisibleAsync`), CARD-0055
(transcript-confirmed delivery; Enter-only retries after evidence), CARD-0056
(`SendBootPromptWithRetryAsync` re-types before evidence), CARD-0027/0037 (typed-input clip model
and the modern-backend ceilings).

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**The card's proposed probe is the right shape, and it is the fourth rung of a ladder this codebase
has been climbing one live miss at a time: CARD-0052 proved the child produced output, CARD-0047
proved no modal is standing, CARD-0048 proved the console host finished its handshake — and none of
them prove the TUI is *draining stdin*. Only a round trip through the composer proves that.** Three
slices, independently shippable, ordered probe → attempt accounting → re-type safety.

| Decision the report flagged | Answer |
|---|---|
| Is the write-probe-verify-clear proposal the right shape? | **Yes.** It is the only positive signal that distinguishes "painted" from "reading", and both halves already exist proven: the evidence check is `ComposerDeliveryEvidence` (replayed correct against the failing session's own ANSI log), and the clear keystroke (Ctrl+U) is already used against real Claude by `ClaudeHarness.cs:144`. |
| Can the probe itself be lost the same way? | **Not by the measured mechanism.** The card measured that the ConPTY input buffer is *retained and drained on wake* (all three pastes landed, minutes late) — the probe rides the same buffer with a poll budget (90 s) sized to the measured dead zone, unlike the 15 s evidence window that sat entirely inside it. The one true loss window is the pre-WebSocket drop that `ClaudeReadyMinTotalWaitMs` 9 000 exists for (`AgentRegistrySettings.cs:12-18`), so the probe types strictly **after** that floor, and re-types at 30 s intervals as a belt against any other silent drop. |
| Where does it belong? | `RunnerClaudeAdapter.WaitForReadyAsync` and the in-process `ClaudeAdapter.WaitForReadyAsync`, in lockstep (the two are documented as one contract, `ClaudeAdapter.cs:114-116`), as the **final** gate after quiet → trust-clear → MinTotalWait. Not in the queue: at ready time the composer is guaranteed empty and nobody owns the session yet, which is the only moment a junk keystroke is free. |
| Should `NoComposerEvidence` on a pre-first-turn session consume an attempt? | **No — the attempt is refunded** (decrement on the observed verdict, inside a wall-clock grace), and the always-on fresh-composer kill is withheld in the same window. Retries then ride the existing 60 s stranded sweep: ~8 chances inside the dispatcher's 10-minute watchdog instead of 3 chances burned in ~2.5 minutes. |
| What should a pre-evidence re-type do about a composer that may already hold the body? | Look, then clear, then type: if the previous attempt's body is **visible** on screen now, skip the type entirely and go straight to the Enter-plus-transcript-confirm phase; if not, send a measured composer-clear keystroke first. The clear also queues *in order* in the ConPTY buffer, so even fully-deaf retries can never stack two copies. Extends to `SendBootPromptWithRetryAsync`, whose whole-submit re-type has the same stacking exposure. |
| Depend on CARD-0102? | **No.** CARD-0102's leaked pty-hosts are today's load source, not the defect. Any silent stall over 15 s — AV scan, parallel E2E run, another workload — re-arms this. Same reasoning CARD-0048 recorded: fix the signal, not the environment. CARD-0102 still deserves its own fix. |

## 1. The mechanics, as verified against the tree (2026-08-20)

The card's narrative checks out in code, with one update: the plan-era claim that Claude's ready
wait is bare quiet is stale — CARD-0052 landed (`c1bd1c8`), so the current chain in
`RunnerClaudeAdapter.WaitForReadyAsync` (`server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs:123`) is:

1. `WaitForQuietAfterVisibleAsync(ClaudeReadyQuietPeriodMs 5000, ClaudeReadyMaxWaitMs 60000)` —
   visible output, then 5 s of quiet;
2. `ClearBlockingStartupPromptAsync` (CARD-0047's trust-dialog gate);
3. sleep out the remainder of `ClaudeReadyMinTotalWaitMs` 9000 (the WebSocket-connect floor).

CARD-0103's repro defeats all three at once: the banner and composer **are** visible output, the
deaf TUI **is** quiet, no modal is up, and 9 s is long past. Every rung so far proves something
about the *output* side; nothing proves the *input* side.

Downstream, the delegate brief path (all verified):

- `AgentTaskDispatcher` enqueues the brief WhenIdle/Delegation and launches with
  `remoteControlName: null` — so `SendRemoteControlCommandsAsync` returns at once and
  **`VerifiedPromptSubmitter` never runs on this path** (`AgentSessionService.cs:1102`).
- The enqueue path refuses to type into a `Starting` session (`IsAcceptingInputAsync`,
  `SessionMessageQueueService.cs:667`); delivery begins only after `WaitForReadyOrThrowAsync`
  flips the row to `Running` — i.e. **everything the queue ever types is downstream of the ready
  verdict this plan fixes**.
- `DeliverNextLockedAsync` stamps Sent + `DeliveryAttempts++` **before** typing (crash-safe
  direction, `SessionMessageQueueService.cs:757-772`), types, and on `NoComposerEvidence`
  (`DeliverAsync:1141-1148` → 15 s `EvidenceTimeoutSeconds` window) reverts to Pending in
  `HandleDeliveryFailureAsync` — attempts deliberately surviving the revert.
- Retries come from the stranded-queue watchdog: `SessionHealthHostedService` ticks every
  `max(10, RcWatch.ProbeIntervalSeconds 60)` seconds and calls `FlushStrandedQueuesAsync`
  (`StrandedAgeSeconds` 60) — matching the card's observed ~60-84 s attempt cadence. At
  `MaxDeliveryAttempts` 3 the message parks; `FailNeverStartedAsync` fails the task at
  10 minutes.
- Each retry **re-types the whole body** with no look at the composer first. `LateConfirmAttemptedMessagesAsync`
  runs before every re-type but reads the *transcript*, which on a pre-first-turn session is empty
  by construction — so it can never save this case. CARD-0055's Enter-only rule governs the phase
  *after* evidence and is untouched by all of this.

Two structural facts the fix leans on, both from the card's measurements:

- **The input buffer is retained, not dropped.** The deaf TUI processed all three buffered pastes
  on wake, in order. So input written during the dead zone is *late*, not *lost* — which is what
  makes both the probe (wait longer than the dead zone) and the ordered clear-keystroke (queues
  between paste N and paste N+1) sound.
- **Once awake, it stays awake.** The 45-s-delayed control pastes rendered in 0.74 s, three times.
  A ready verdict earned by one round trip is therefore good for the deliveries that follow
  seconds later; the residual risk of a *mid-life* deafness relapse under sustained load is what
  slice 2's refunded attempts cover.

## 2. Slice 1 — the input-responsiveness probe (the real fix)

### 2.1 Shape

New helper in `src/Antiphon.Agents.Pty/` next to `VerifiedPromptSubmitter` (name
`ComposerInputProbe`), delegate-based like `VerifiedPromptSubmitter` (snapshot fn + write fn) so
both Claude adapters — and later Grok, which `IsVerifiedDeliverySessionAsync` documents as also
echoing its composer — can share it:

1. **Type** a probe token: one short single-line write, no CR ever.
2. **Poll** the rendered screen for the token every ~250-500 ms.
   - Token visible → **clear**: send Ctrl+U (`\x15` — the kill-line keystroke `ClaudeHarness.cs:144`
     already uses against real Claude), poll until the token has left the screen (≤ ~10 s), re-send
     Ctrl+U up to 2 more times if it lingers. Cleared → ready `true`. Never cleared → ready
     `false` — a composer we cannot empty must not have a boot prompt appended to it.
   - No token after 30 s → re-type it (a doubled token still substring-matches, and the eventual
     Ctrl+U kills the whole line). At most 3 writes inside the budget.
   - Budget (`ClaudeInputProbeTimeoutMs`, default **90 000**) exhausted → ready `false` →
     `WaitForReadyOrThrowAsync` throws "Agent process did not become ready", the launch catch runs
     `KillAndDisposeAsync` (CARD-0056), and the failure is loud and attributable — strictly better
     than today's silent park inside a session everyone believes is healthy.

Token: letters+digits only, e.g. `zz` + 8 hex of the session id — must not begin with `/`
(slash command), `!` (bash shortcut), or `#` (memory shortcut); session-derived so screen-collision
is negligible and logs are attributable. Matching is a direct contains on the
`ComposerDeliveryEvidence`-normalized screen (the token is far shorter than `FragmentSpan`, so this
is the same arm `IsVisible` would take; no need to thread a before-screen through).

### 2.2 Placement and ordering — and what already depends on the current contract

The probe is the **last** step of `WaitForReadyAsync` in both adapters:

| Step | Why the order is load-bearing |
|---|---|
| 1. visible-then-quiet (CARD-0052) | unchanged — the probe needs a painted composer to type into |
| 2. trust-dialog clear (CARD-0047) | unchanged, and **must precede** the probe: typing into a modal was CARD-0047's named hazard. In the `NotAnswerable`-modal arm (the lenient default that logs and passes), **skip the probe** and keep today's behavior — probing a standing unknown modal is exactly the keystroke CARD-0047 refused to send, and that arm already announces "delivery to it is likely to fail". |
| 3. `MinTotalWait` remainder | unchanged, and **must precede** the probe: before ~9 s the composer *accepts and silently drops* writes (`AgentRegistrySettings.cs:15-17`) — a probe typed inside that window would be genuinely lost and time out falsely. |
| 4. **input probe (new)** | the only step that proves reading, so it goes last and its verdict is final |

Call-site survey (all three `WaitForReadyOrThrowAsync` sites in `AgentSessionService` — card launch
`:175`, relaunch `:361`, interactive `:823`): every one types something immediately after ready
(`/remote-control`, a card work prompt, or unblocks the queue via `Status = Running`), so every one
*wants* this gate. Notably it retroactively covers CARD-0056's live shape — the 15-char
`/remote-control` typed into a resume-rendering composer — because a resume render that is still
chewing is exactly a TUI that fails the probe. CARD-0048's DA1 answer lives inside
`ModernConPtyConnection` at pty creation and is unaffected. No caller reads "ready" as "quiet"; they
all read it as "safe to type", which is what it will finally mean.

Cost on a healthy machine: one type + one poll hit + one Ctrl+U ≈ 1-2 s per Claude launch (the
control measurement bounds the round trip at 0.74 s). That is the price of every launch not being a
coin-flip under load.

### 2.3 Test surface

- **fakeclaude** learns two things (it currently handles neither — verified, no `\x15` handling in
  `src/Antiphon.FakeClaude/`):
  - Ctrl+U kill-line in its composer model (pin the real behavior first with a small headed canary
    arm — `ClaudeHarness` proves single-line kill works; the canary pins it as a contract);
  - `ANTIPHON_FAKE_DEAF_START_MS=N` (opt-in, default off — same discipline as
    `ANTIPHON_FAKE_STDIN_CLIP`): paint banner + composer, then do not read stdin for N ms; buffered
    input is processed on wake, in order — exactly the measured shape.
- `FakeClaudeContractTests`: with deaf-start armed at, say, 8 000 ms and a probe budget above it,
  `WaitForReadyAsync` returns true only *after* the wake (red today: current ready fires inside the
  deafness); with deaf-start beyond the probe budget, ready is `false`, not a silent pass.
- `RunnerClaudeAdapterTrustPromptTests`' scripted fake client learns to echo the probe token and
  honor Ctrl+U so the existing trust-prompt pins keep passing; add an arm pinning **probe skipped**
  on the `NotAnswerable` modal outcome.
- Unit pins on `ComposerInputProbe` itself (delegate fakes, no pty): token appears late → waits;
  never appears → false; appears but never clears → false; re-type at 30 s.

## 3. Slice 2 — a pre-first-turn `NoComposerEvidence` does not consume an attempt

Even with the probe, a session can go deaf again between ready and a delivery (sustained load), and
the probe cannot help deliveries that happen minutes after launch. The budget arithmetic is the
defect here: 3 attempts × ~60 s cadence sits entirely inside a measured 50-200 s dead zone.

**Mechanism** — in `HandleDeliveryFailureAsync`, when **all** of:

- verdict is `NoComposerEvidence` (the Enter was never sent, so nothing can have been submitted —
  the one verdict where "not charged" is provably safe);
- the attempt's stamped baseline had `Observable == false` (zero `TranscriptEntries` at type time —
  the report's "zero transcript entries", read from the already-persisted
  `LastDeliveryBaselineSequence == null`);
- the message is younger than `PreFirstTurnNoEvidenceGraceMinutes` (new
  `DeliveryVerificationSettings` knob, default **8** — deliberately inside the dispatcher's
  10-minute `FailNeverStartedAsync` clock, so a genuinely dead session still fails loudly at 10:00
  with "the brief is still queued Pending" rather than parking silently at 2:30);

then, on the same revert that returns the row to Pending: **decrement `DeliveryAttempts`** (refund
the stamp — the stamp-before-type crash-safety is preserved, because a crash still leaves the
attempt charged; only the *observed* pre-first-turn no-evidence verdict refunds), **withhold the
always-on fresh-composer kill** (killing a booting-but-deaf TUI is the CARD-0047 restart-loop
shape), and demote the incident to a single Warning (first occurrence per message) instead of
today's Error-per-attempt ×3 — six sessions × 3 attempts of Error spam was today's signature.

Everything downstream needs no change: the flush queries' `DeliveryAttempts < maxAttempts`
predicates, parking, and the queue UI all keep their meaning because the counter itself is kept
honest. After the grace expires, attempts charge normally and the message parks as today.

**Tests** — `SessionMessageQueueDeliveryVerificationTests`: refund fires only on the triple
condition (each leg negated in turn: post-first-turn charges; `NoTranscriptRecord` charges; past
grace charges); always-on kill withheld inside grace and restored past it; attempt counter visible
to `FlushStrandedQueuesAsync` keeps the message eligible across > 3 sweeps.

## 4. Slice 3 — a pre-evidence re-type must look at the composer first

The card measured the stakes: after three re-types the composer held the brief **twice** (attempt 2
inline-expanded placeholder #1, attempt 3 added placeholder #2), one Enter away from a duplicated
brief. CARD-0055's never-re-type rule guards the post-evidence phase only; the redelivery re-type,
and CARD-0056's `SendBootPromptWithRetryAsync`, both type with no evidence either way — and
CARD-0056's stated justification ("no composer evidence means the composer does not hold the body")
is exactly what CARD-0103 disproved: no evidence can also mean *not drained yet*.

**Mechanism** — in `DeliverAsync`, when any row in the run has `DeliveryAttempts > 1` at type time
(i.e. this is a re-type; late-confirm has already found nothing in the transcript):

1. **Look**: snapshot the screen and run `ComposerDeliveryEvidence.IsVisible` with an *empty*
   before-screen against the body. Evidence standing → the previous attempt's body is in the
   composer right now: **do not type**; fall through to the existing Enter → transcript-confirm
   phase (`WaitForTranscriptConfirmAsync`), which submits the single standing copy and confirms it
   by text. This converts the card's stacking scenario into one clean submit. (The empty
   before-screen makes the placeholder-index arm inert, deliberately: only head/tail fragments can
   satisfy this look, so a stale placeholder from an *earlier submitted* paste cannot fake it, and
   a false *negative* here merely proceeds to the clear — safe.)
2. **Clear**: no evidence → send the measured composer-clear keystroke sequence, then type as
   today. Because writes queue in order in the retained ConPTY buffer, this is correct even
   against a still-deaf TUI: the stream reads paste₁, clear, paste₂, clear, paste₃ — at most one
   copy ever stands when the TUI wakes.
3. Same look-then-clear goes into `SendBootPromptWithRetryAsync`'s retry loop before each re-type
   (its evidence check is `VerifiedPromptSubmitter`'s; the composer-standing case there means
   "press Enter", which its existing flow already does on evidence).

**Measurement gate (canary first)**: Ctrl+U is proven for a single typed line only. What empties a
composer holding (a) a multi-line typed body, (b) a collapsed `[Pasted text #N]` placeholder,
(c) the exact run-3 shape — expanded body *plus* a placeholder — is unmeasured. A headed
`[Explicit]` canary (alongside `ClaudeSubmitConfirmCanaryTests`) measures candidates (repeated
Ctrl+U; Esc, noting Esc is only safe pre-first-turn because mid-turn it interrupts) and the winner
is pinned into fakeclaude's composer model + `FakeClaudeContractTests` before the production path
uses it. If nothing reliably clears shape (b)/(c), the fallback posture is **refuse to re-type**
(revert, leave Pending for the next sweep, Warning incident) — under slice 2 the message survives
to retry, and "one copy we cannot clear" must never become "two copies we submitted".

**Tests** — `SessionMessageQueuePtyIntegrationTests` through fakeclaude with deaf-start armed:
three delivery attempts against a deaf composer leave exactly **one** copy standing on wake (red
today: two); the standing-evidence arm submits without typing and confirms by text; clear-failure
refuses the re-type. `FakeClaudeContractTests` pins the clear keystroke contract.

## 5. Deliberately not in scope

- **`ComposerDeliveryEvidence.IsVisible`** — replayed correct against the failing session's own
  ANSI log; not touched.
- **Widening `EvidenceTimeoutSeconds` 15, any quiet/max-wait/`MinTotalWait` constant, or
  `MaxDeliveryAttempts`** — CARD-0048's decision and the ADR stand; slice 2 changes what an attempt
  *means* pre-first-turn, not how many there are.
- **CARD-0055's Enter-only post-evidence contract and `WaitForTranscriptConfirmAsync`** — untouched;
  slice 4 of that card's ladder is where slice 3 hands off.
- **CARD-0102** (the pty-host leak) — the load source, fixed on its own card; this fix is designed
  to hold without it (§Verdict, last row).
- **Grok/Codex/OpenCode/Raw ready probes** — `RunnerGrokAdapter` has the same quiet-shaped ready
  and an echoing composer, so it is the obvious second adopter of `ComposerInputProbe`; that is a
  follow-up card, not this one (the measured defect is Claude's, and Grok's clear keystroke is
  unmeasured). Codex/OpenCode/Raw deliver blind and have no composer to probe.
- **The reconciliation observation** (46 runner sessions with no DB row at 09:38:24) — evidence for
  CARD-0102's blast radius, not this card's mechanism.
- **Boot-path transcript confirmation** — CARD-0055's boot scope-out stands; the probe makes the
  boot path safer without pretending a not-yet-created transcript can confirm anything.

## 6. Slices and order

| Slice | Contents | Depends on |
|---|---|---|
| S1 | fakeclaude Ctrl+U + `ANTIPHON_FAKE_DEAF_START_MS`; `ComposerInputProbe`; wire into both Claude adapters; settings; pins | nothing |
| S2 | pre-first-turn attempt refund + kill-withhold + incident demotion; pins | nothing (independent of S1) |
| S3 | composer-clear canary measurement → fakeclaude model → look-then-clear in `DeliverAsync` + `SendBootPromptWithRetryAsync`; pins | S1's fakeclaude knobs; the canary's verdict |

S1 and S2 can land in either order and each alone materially shrinks the failure (S1 removes the
launch-time dead zone; S2 makes the remaining window survivable). S3 goes last because it is gated
on a real-Claude measurement and because S1+S2 already reduce re-types into deaf composers to the
rare case S3 exists to make harmless.

Test runs (alternate output path, forward slash, `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests`
sequentially, never co-scheduled; delete `bin-card0103/` dirs after):

```
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0103/ -- --treenode-filter "/*/*/FakeClaudeContractTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0103/ -- --treenode-filter "/*/*/RunnerClaudeAdapterTrustPromptTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0103/ -- --treenode-filter "/*/*/SessionMessageQueueDeliveryVerificationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0103/ -- --treenode-filter "/*/*/SessionMessageQueuePtyIntegrationTests/*"
```

## 7. Card housekeeping

- CARD-0103 stays open through implementation; this plan is its next revision's pointer. Do not
  re-run the live repro — the three runs and the ANSI-log replay are the evidence base.
- File the follow-up card for the Grok ready probe (`ComposerInputProbe` adoption +
  Grok clear-keystroke measurement) when S1 lands and the helper exists to point at.
- CARD-0102 keeps its own life; add a cross-link from it to this plan's §Verdict last row so its
  fix is not mistaken for this card's.
- CARD-0056's doc-comment claim that a `SendBootPromptWithRetryAsync` re-type "cannot
  double-submit" needs the S3 correction noted in whatever file carries it when S3 lands
  (`SupervisionSettings.cs:222` area) — the claim is true only once the re-type looks first.
