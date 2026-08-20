# CARD-0108 — Codex turn completion must come from the rollout, not from quiet: plan

**Date:** 2026-08-20
**Status:** planned (not implemented)
**Card:** CARD-0108 (`4d392e6f-e771-4e02-90ff-7f4904373948`) — the Codex adapter's turn-completion
detection fires on the quiet status bar, not the real answer; `CodexAdapterIntegrationTests`
returned `"gpt-5.6-luna low · ~\appdata\local\temp"` as `ResponseText` in ~5 s.
**Evidence base:** two fresh headed probe runs against the real CLI (2026-08-20, codex-cli 0.147.0,
gpt-5.6-luna @ low, modern ConPTY, production `PtyAgentRunner`/`CodexDoneDetector` — six real model
turns total; measurements in §1), plus CARD-0099 S2's `CodexComposerCanaryTests` measurements and
the S2/S3 build reports' failing integration-test observation.
**Precedent:** CARD-0080 S2 (`RunnerGrokAdapter.WaitForTurnCompleteAsync` — the shipped, pinned
template for "turn verdict from the tailed transcript, screen as fallback"), CARD-0099 S1
(`CodexTranscriptNormalizer`/`CodexTranscriptTailer` — `task_complete` → TurnEnd already ships and
production Codex sessions already run the tailer), CARD-0103 (the "quiet is not done" ladder:
CARD-0047 → 0048 → 0052 → 0103), CARD-0055/0056 (submit is proven by a transcript `UserPrompt`
row; Enter-only re-press is safe on an empty composer).

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**Do not copy `ComposerInputProbe` — Codex already has the positive signal Claude never had.** The
rollout's `event_msg/task_complete` row is an explicit structured turn boundary, S1's tailer already
tails it for every production Codex session (`SessionRunnerHttpClient.TranscriptEnabledFor` says
Supported ⇒ enabled), and `RunnerGrokAdapter.WaitForTurnCompleteAsync` is a shipped, test-pinned
implementation of exactly the right shape one file away. The fix is to give `RunnerCodexAdapter`
the same shape: **primary verdict = a TurnEnd transcript row past a baseline captured at
`SendPromptAsync`; `ResponseText` = the window's AssistantText rows; the screen is a fallback only,
and for Codex the fallback must be gated on the measured working indicator, never bare quiet.**

**But the headed measurement shows the card's framing was only half the defect.** The probe
reproduced the integration-test failure and it is TWO defects stacked:

1. **The turn never starts.** The production submit path (`PtyAgentRunner.SendLineAsync`: body,
   20 ms, separate `\r`) failed to submit **6 times out of 6** across both probe runs — the CR
   lands inside Codex's paste-detection window and folds instead of submitting (S2's phase-B coin
   flip; S2 measured 1-in-3, combined 1-in-9). The body strands in the composer, no rollout file is
   ever created (it is created lazily by the first *turn*), and the TUI is completely silent.
2. **Quiet then certifies the non-turn as done.** Over that stranded, silent composer the
   production `CodexDoneDetector` (3 s quiet) returned `TurnCompleted: true` at **3.15–3.19 s**
   (3/3), and `CodexResponseAnalyzer.ExtractResponse` scraped what was left after echo-stripping:
   the status bar — character-for-character the integration test's failure string.

So the plan has a submit-verification leg (S1 below) and a done-detection leg (S2 below); either
alone leaves the round trip broken (fixing only detection converts corrupt-success into an honest
5-minute timeout, because the turn still never runs).

| Question the card/task posed | Answer, measured |
|---|---|
| What quiet heuristic does the adapter use? | `CodexDoneDetector` → `WaitForQuietAfterVisibleAsync(3 s, 5 min)` on the raw live buffer, in BOTH Codex adapters (`CodexAdapter.cs:109`, `RunnerCodexAdapter.cs:90`). No positive signal of any kind. |
| How long after quiet does the real answer render? | **The stranded-composer case never renders** — that is the failure mode, and it is deterministic here, not a race the right timeout wins. In a turn that actually runs, quiet cannot fire early: the TUI repaints `• Working (Ns …)` at ~1 Hz, so the buffer grows all turn; the answer rendered 1.72/2.72/2.75 s after the submitting Enter and the detector (started at that Enter) fired ~3 s after the answer, correctly. Bare quiet's false fire needs a silent *non-turn*, which the swallowed Enter supplies on nearly every prompt. |
| Is the rollout signal available and timely for the interactive adapter? | Yes, twice over. `RunnerCodexAdapter` (the only Codex adapter production ever constructs — `AgentProtocolAdapterFactory.Create` returns Runner* for every kind) already has `ISessionRunnerClient.GetTranscriptAsync`, and production Codex sessions already run the tailer. Timeliness: `task_complete` was observable by a 200 ms poller **1.85/2.72/2.75 s after the submitting Enter — the same instant the answer rendered** (the row's own timestamp matched the render to ~0.1 s); the tailer polls at 250–300 ms. The transcript verdict is *faster* than the 3 s quiet wait, not slower. |
| Does a `ComposerInputProbe`-style probe transfer? | It answers the wrong question. The probe proves the TUI *reads input*; Codex's TUI reads input fine — it swallows the *Enter semantics*, and it has a structured done signal the probe exists to substitute for. What does transfer is CARD-0055's submit contract: prove the submit by the transcript `UserPrompt` row, retry with Enter only (S2 phase A measured Enter-on-empty submits nothing, five times over). |

## 1. Measured facts (2026-08-20, probe runs A and B)

Probe: scratchpad console app driving the vendored `codex.exe` (npm 0.147.0) through the production
`PtyAgentRunner("modern")` at 120×30 in a fresh temp cwd, trust dialog auto-answered, three trivial
prompts per run ("capital of Japan" etc., markers not present in the prompt text). Run A used the
production submit path exactly; run B added S2-canary-style Enter re-presses so the turns actually
ran. Numbers already stated above; the rest worth recording:

- **The composer echo is the only output a stranded prompt produces.** After the echo redraws
  (~0.1 s apart), the TUI emits *nothing* — no spinner, no repaint, for at least 100 s. Quiet-based
  anything reads that as done/ready/idle.
- **One extra Enter submitted 6/6**, sent ~4 s after the failed CR. (The queue's CARD-0055 confirm
  loop re-presses at 7 s intervals, which is why *queued* deliveries to Codex already survive this;
  the adapter's own `SendPromptAsync` — the boot/card-prompt/integration-test path — is blind
  `SendLineAsync` and strands. `SendBootPromptWithRetryAsync`'s retries never engage because the
  blind path throws no `PromptDeliveryException`; it reports success over a stranded composer.)
- **A live turn is visibly a live turn:** `• Working (Ns • esc to interrupt)` (bullet alternates
  •/◦) renders while the turn runs and leaves the screen when it completes. This is the screen
  fallback's positive signal.
- **OSC-0 title** carries a braille spinner while busy and the plain cwd when idle — but it also
  spins during MCP startup with no turn running, so it is "busy", not "turn running". Weaker than
  the Working line; do not build on it.
- `ExtractResponse` over a real completed turn is still garbage: the raw-buffer scrape picked up
  the composer's ghost hint text ("Summarize recent commits") and spinner fragments alongside the
  answer. Reply text must come from AssistantText rows, same reason Grok's does.
- **TUI-dialect rollout census** for the 3-turn session: `session_meta`, `task_started`×3,
  `item_completed:UserMessage`×3, `item_completed:AgentMessage`×3, `token_count`×3,
  `task_complete`×3, `response_item(message)`×8 — matches what S1's normalizer maps; no surprises.
- Incidental: launching in a fresh cwd with the operator's shared `~/.codex/config.toml` printed
  "⚠ MCP startup interrupted: codex_apps, node_repl" — the delegate-profile isolation note in
  CARD-0099 S3 (`-p codex-delegate`) remains worth doing, separately.

The probe is disposable (session scratchpad); everything load-bearing above must be pinned by the
S3 canary before the constants it justifies are trusted long-term.

## 2. Slice S1 — `SendPromptAsync` must prove the submit (both Codex adapters)

`RunnerCodexAdapter.SendPromptAsync` (and `CodexAdapter`'s, in lockstep) becomes:

1. Capture the transcript baseline (`GetTranscriptAsync().LastSequence`, same discipline as
   `RunnerGrokAdapter.SendPromptAsync:85`) before any keystroke.
2. `SendLineAsync(prompt)` as today (do NOT touch `SendLineAsync`'s 20 ms gap — Claude's contract
   depends on it and a Codex-shaped delay tune would be a guess; the retry loop makes the exact
   settle time irrelevant).
3. Poll for a `UserPrompt` row past the baseline whose text confirms this body
   (`PromptSubmissionMatch.IsConfirmedBy` — the CARD-0055 matcher, head-window). Not there after
   `CodexSubmitReEnterIntervalMs` (default ~4 000, the measured-working interval) → press **Enter
   only, never re-type** (measured safe: Enter on an empty composer submits nothing, and the
   per-adapter call sites hold the session exclusively at boot). Up to `CodexSubmitAttempts`
   (default 3 extra Enters) inside `CodexSubmitConfirmTimeoutMs` (default ~20 000).
4. Confirmed → return. Exhausted → throw `PromptDeliveryException` with the screen tail in the
   message, so `SendBootPromptWithRetryAsync` and the launch catch (`KillAndDisposeAsync`,
   CARD-0056) do their existing jobs.

Two sharp edges, both design constraints and both to be stated in code comments:

- **The outer `SendBootPromptWithRetryAsync` re-types on `PromptDeliveryException`, and its
  justification ("no composer evidence ⇒ composer is empty") does not hold for Codex** — here the
  failure mode is a composer still HOLDING the body. The internal Enter-retry budget makes the
  outer re-type practically unreachable, but S1 must still prevent the stacking case: on the Codex
  arm, the thrown failure follows a **look**: if the screen still shows the body's head fragment,
  the exception message says so and the outer loop must skip the re-type for that attempt (the
  CARD-0103 slice-3 look-then-clear shape, narrowed to this one arm; the full generalization stays
  on CARD-0103).
- **First-turn confirmation races rollout creation and binding**: the file is created by this very
  submit and the tailer then has to discover and bind it (250 ms locate poll; C1–C4). The measured
  end-to-end lag (row observable ≤2.8 s after the Enter that actually submitted) sits comfortably
  inside the re-Enter interval, but the poll must treat "no transcript yet" as *not confirmed*
  rather than as failure, and the timeout must not be trimmed below ~20 s.

Where the transcript never becomes available at all (bind refused/failed), S1 degrades to today's
blind behavior plus a Warning log — a session with no transcript is already a
`TranscriptBindFailed` incident by S1-of-0099's rules, and delivery verification degrading is the
documented CARD-0055 posture.

## 3. Slice S2 — `WaitForTurnCompleteAsync` takes the Grok shape (the card's headline fix)

Port `RunnerGrokAdapter.WaitForTurnCompleteAsync`/`TryBuildTranscriptVerdictAsync`
(`RunnerGrokAdapter.cs:171-258`) to `RunnerCodexAdapter`:

- Poll (250 ms) for `TranscriptKinds.TurnEnd` past the S1 baseline → `TurnCompleted: true`,
  `ResponseText` = the window's AssistantText rows joined (`item_completed{AgentMessage}` maps
  there already), `IsAskingQuestion` from that reply text — not from a screen scrape.
- Screen fallback for transcript-less sessions only, and **for Codex it is NOT bare quiet**: the
  fallback requires the measured turn lifecycle — the Working indicator
  (`Working (` + `esc to interrupt)` on the rendered screen) was seen and has gone — before quiet
  counts. A session where the indicator never appears reaches `CodexDoneMaxWaitMs` and returns
  `TurnCompleted: false` honestly; that is the stranded-composer shape and "false" is the truth.
  This replaces `CodexDoneDetector`'s semantics; keep the class, change its contract, and keep the
  doc-comment naming the measured indicator strings so a TUI redesign is findable.
- Do not add a Grok-style done-line regex: Codex renders no "Worked for Ns" line (measured — the
  completed turn's screen has answer + fresh composer only), and the OSC title spinner is busy-not-
  turn (§1). The indicator-disappears rule is the whole screen signal.

The in-process `CodexAdapter` gets the same fallback change (shared code in
`CodexDetectors.cs` — the detector core is delegate-based over snapshot functions, the
`VerifiedPromptSubmitter`/`ComposerInputProbe` pattern, so the two adapters cannot fork). It does
NOT grow its own rollout reader in this card: production never constructs it
(`AgentProtocolAdapterFactory` returns Runner* only; Grok has no in-process adapter at all), the
server project deliberately references only `SessionRunner.Contracts` (the normalizer lives in
`Antiphon.SessionRunner`), and the one test that needs the full round trip should exercise the
production adapter instead (S3). If the in-process adapter ever becomes a production path again,
the rollout-reader decision reopens — say so in its class comment.

## 4. Slice S3 — proving it: repoint the integration test at the production adapter, and pin the new facts

- **`CodexAdapterIntegrationTests` currently exercises the adapter production never runs.** Repoint
  the round-trip test at `RunnerCodexAdapter` over `DirectSessionRunnerClient` with a new opt-in
  `codexTranscript: true` (the client's comment disables Codex transcript for good reason —
  discovery walks the real `~/.codex/sessions` — but this test is already headed, already spends
  real turns, and C2 becomes exact by giving the session a **unique temp cwd** instead of today's
  shared `Path.GetTempPath()`). The assertion stays exactly as written: `ResponseText` contains
  "pong". This makes the test the first observed full `-Kind Codex` round trip — the thing
  CARD-0108 says nobody has seen.
- **`RunnerCodexAdapterTurnCompleteTests`** mirroring `RunnerGrokAdapterTurnCompleteTests` (fake
  `ISessionRunnerClient` feeding rows): TurnEnd past baseline → completed with AssistantText reply;
  rows at-or-below baseline → keep waiting; no transcript + no Working-indicator-lifecycle → false
  at max wait (the run-A shape, red against today's code); transcript unavailable + lifecycle seen
  → fallback true.
- **Submit-confirm tests**: first CR unconfirmed → Enter re-pressed → confirmed on the row (fake
  client); exhausted → `PromptDeliveryException`; body-still-visible reflected in the failure.
- **A headed canary pinning the §1 facts** (alongside `CodexComposerCanaryTests`, `[Explicit]`,
  same gates): the first-CR swallow (recorded, not asserted — S2 measured it as a coin flip even
  though this probe saw 6/6), the Working-indicator strings, task_complete-observable-≤3 s, and the
  absence of a done line — so a codex-cli update that changes the TUI goes red here first.
- FakeCodex (CARD-0099 S5) still does not exist; when it is built, the S1/S2 behaviors here (CR
  folding in the paste window, Working indicator lifecycle, lazy rollout) are exactly what it must
  model. This card does not wait for it — the fake-client tests above carry CI coverage.

Test runs (alternate output path, forward slash; the two process-spawning projects sequentially,
never co-scheduled; delete `bin-card0108/` dirs after — roughly a dozen appear):

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0108/ -- --treenode-filter "/*/*/RunnerCodexAdapterTurnCompleteTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0108/ -- --treenode-filter "/*/*/CodexAdapterLocalShellTests/*"
$env:ANTIPHON_CODEX_HEADED_TESTS='1'; dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0108/ -- --treenode-filter "/*/*/CodexAdapterIntegrationTests/*"
```

## 5. Deliberately not in scope

- **`PtyAgentRunner.SendLineAsync`'s 20 ms body→CR gap** — load-bearing for Claude (CARD-0027/0037
  contracts); Codex's fix is confirm-and-re-press at the adapter, not a shared-primitive tune.
- **The queue delivery path** (`SessionMessageQueueService`) — already transcript-confirmed with
  Enter re-press (CARD-0055) and already works for Codex by S2's measurements; untouched.
- **`CodexReadyDetector` / startup readiness** — the trust-prompt answer and quiet-ready are a
  different gate; a Codex `ComposerInputProbe` adoption is the CARD-0103 follow-up ladder, not this
  card. (Run A/B saw ready work correctly.)
- **CARD-0103 slice 3 generalization** (look-then-clear before any re-type) — S1 takes only the
  narrow Codex arm of it; the general fix stays on CARD-0103.
- **Delegate-profile isolation** (`-p codex-delegate`, the MCP-startup-interrupted observation) —
  CARD-0099 S3's open note, separate.
- **FakeCodex** — CARD-0099 S5's slice; this card hands it a measured spec, nothing more.
- **`ProviderContractCatalog.Codex` TurnCompletion prose** — updated in S2 to say the adapter
  itself now consumes the structured signal (today it truthfully says quiet-time remains the live
  fallback; after S2 the reason string must describe the indicator-gated fallback).
- **OpenCode/Raw quiet-only detection** — same genre, no measured defect, own cards if ever.

## 6. Slices and order

| Slice | Contents | Depends on |
|---|---|---|
| S1 | submit confirm + Enter re-press in both Codex adapters; settings knobs; fake-client pins | nothing |
| S2 | transcript-primary `WaitForTurnCompleteAsync` in `RunnerCodexAdapter`; indicator-gated fallback in `CodexDetectors`; contract-catalog prose | S1 (baseline capture shared) |
| S3 | integration test repoint (`DirectSessionRunnerClient` opt-in, unique cwd); `RunnerCodexAdapterTurnCompleteTests`; headed canary | S1+S2 |

S1 before S2 because S2's baseline is captured in S1's `SendPromptAsync`, and because S2 without S1
turns corrupt-success into a 5-minute honest timeout — better, but not the round trip the card
demands. All three are small; this is ~a day of work end to end, dominated by the headed canary.

## 7. Card housekeeping

- Record §1's measurements on CARD-0108 (the card asked for the timing number; the answer is "the
  false fire is pre-submit and deterministic-ish, not a tunable margin" — worth stating on the card
  so nobody widens `CodexDoneQuietPeriodMs` as a quick fix, which cannot help: the stranded shape
  is silent forever).
- `CodexAdapterIntegrationTests`' doc comment says the failure is "CARD-0099 S3's to fix" — it is
  CARD-0108's; correct the comment when S3 lands.
- Cross-link CARD-0099 (promotion gate: this card is the named blocker) and CARD-0103 (the narrow
  look-then-clear arm S1 borrows).
- The `DirectSessionRunnerClient` comment explaining why Codex transcript is disabled gains a
  sentence pointing at the headed opt-in, so the reasoning stays honest.
