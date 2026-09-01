# CARD-0292 — Resumed session wedges on the /remote-control dialog; queued input silently swallowed

**Date:** 2026-09-01 (Plan pass, task 52a44631 — design only; no code changed)
**Card:** CARD-0292 "A resumed session wedges on the /remote-control dialog and silently queues every message"
**Diagnosis:** done, on the card. Session `70eb4c2d` resumed at 04:05Z with a bridge it has carried since 2026-08-13; the launch preamble replayed `/remote-control`, the already-bridged TUI opened its management menu (Disconnect / Show QR / Continue) instead of no-op'ing, and every subsequent input landed as a `queue-operation` `enqueue` JSONL record that never became a `user` prompt. Every health signal read healthy for five hours.

**Sources (verified this pass):** `server/Application/Services/AgentSessionService.cs`, `SessionHealthService.cs`, `SessionMessageQueueService.cs`, `ProviderContractCatalog.cs`, `AgentSessionRuntime.cs`, `AttentionService.cs`, `server/Infrastructure/Supervision/WindowsRcBridgeProbe.cs`, `server/Application/Interfaces/IRcBridgeProbe.cs`, `IAgentProtocolAdapter.cs`, `server/Domain/Enums/AgentIncidentKind.cs`, `src/Antiphon.SessionRunner/TranscriptNormalizer.cs`, `TranscriptCandidateProbe.cs`, `docs/session-runtime-invariants.md` (Gotchas #48, #50–#52), `docs/investigations/2026-08-23-card-0135-queued-brief-watchdog-investigation.md`, CARD-0240/0212/0132/0137 plans.

---

## Decision

Three layers, deliberately independent — any one of them alone would have prevented or bounded this incident:

1. **Don't send `/remote-control` into a session whose bridge is already live (S1).** On a resume-mode relaunch, Claude re-establishes the bridge itself; the ground truth is `bridgeSessionId` in Claude's own `~/.claude/sessions/<pid>.json`, already read by `WindowsRcBridgeProbe`. Probe briefly after ready; if armed, skip straight to `/rename`.
2. **If the management menu appears anyway, dismiss it (S2, S5).** The menu on screen is *positive evidence the bridge is armed*, not a degradation. Recognize its rendered shape, send one Esc (measured safe for Claude: "Esc is a no-op on an idle empty composer", `ProviderContractCatalog.cs:73-77`), and continue. Today's unarmed path degrades and **returns with the menu still standing** — that return is the wedge.
3. **Detect swallowed input from the transcript, for every input source (S3, S4).** Normalize `queue-operation` records into persisted housekeeping kinds, then sweep: a session whose **latest enqueue** has no subsequent conversion while the session reads idle gets an explicit incident + attention row. This is the missing "explicitly failed to convert" counterpart to Gotcha #50's "transcript-confirmed UserPrompt evidence is the delivery verdict".

No kills, no retypes, no auto-restarts anywhere in this card. The only automatic keystroke is a single screen-verified Esc, the key the menu itself documents as "continue".

---

## Ground truth (checked, not guessed)

### The two senders of `/remote-control`

| Sender | Where | When it fires | Menu risk |
|---|---|---|---|
| Launch preamble | `AgentSessionService.SendRemoteControlCommandsAsync` (`AgentSessionService.cs:1315-1410`), called from the card-spawn path (`:198`, fresh, `resumeMode: null`) and from `LaunchInteractiveProcessAsync` (`:394`) | Every launch with a `remoteControlName`, **including resumes** — `LaunchInteractiveProcessAsync` has `resumeMode` (`:359`) but does not pass it to the preamble | **The incident.** A resumed session with a live bridge gets the menu, and the unarmed branch (`:1358-1373`) raises `RcDegraded` and returns, leaving it standing |
| Health watch re-arm | `SessionHealthService.WatchRcAsync` dead-bridge arm (`SessionHealthService.cs:236-249`): `probe.Armed == true`, zero Anthropic connections for N probes → enqueue `/remote-control` WhenIdle | Always-on agents only | **Latent same bug**: it types `/remote-control` into a TUI that *believes* it is bridged. The never-armed arm (`:186-206`, no `bridgeSessionId`) is safe — nothing to open a menu about |

`SendBootPromptWithRetryAsync` (`:568`) retries the whole verified submit up to `BootPromptAttempts` (3) on missing composer evidence — that is how the incident screen shows `/remote-control` and `/rename` each sent twice. With S1 the duplicate `/remote-control` disappears on the bridged-resume path; the duplicate `/rename` is idempotent and stays out of scope.

### Bridge ground truth available to us

- `IRcBridgeProbe.Probe(pid)` → `Armed` = `bridgeSessionId` present in `%USERPROFILE%\.claude\sessions\<pid>.json`, "a fact, written by the bridge itself" (`IRcBridgeProbe.cs:20-26`, `WindowsRcBridgeProbe.cs:26-44`). Registered singleton (`server/Program.cs:338`). `SessionHealthService` feeds it the **child pid** from the runner's live status (`live.Pid`, `SessionHealthService.cs:125`), not `HostPid`.
- The adapter exposes `Pid`, raw `SendInputAsync` (can carry `"\u001b"`), and `SnapshotRenderedScreen` (`IAgentProtocolAdapter.cs:18,25,43`).
- Menu shape (from the incident's runner snapshot, `lastSequence: 18`): heading `Remote Control`, rows `Disconnect this session` / `Show QR code` / `> Continue`, footer `Enter to select . Esc to continue`.

### Where delivery verification lives today — and why it never saw this

Per-message delivery verdicts exist **only for queue-routed bodies**, all in `SessionMessageQueueService`: confirm = a `UserPrompt`/`QueuedUserPrompt` row past a pre-write baseline (`WaitForTranscriptConfirmAsync :1706`, `TryFindConfirmingRecordAsync :2016-2045`), failure ladder = `NoTranscriptRecord` → Enter-only retries → overlay dismiss (`TryDismissOverlayAsync :2773`, invoked at `:1600`/`:1625`) → park + incident (`HandleDeliveryFailureAsync :2354`), late/grace confirm before any redelivery (`LateConfirmAttemptedMessagesAsync :1140`, Gotchas #49–#51). A queue-routed message typed into this wedge would have failed confirmation, drawn an Esc from CARD-0137's overlay recovery, and likely self-healed.

The incident's two messages never entered that machinery — they reached the TUI directly (the queue showed `pending: null` throughout). Input arriving via the RC bridge, Herdr, or an operator terminal has **no verdict layer at all**. The only place every source converges is the transcript itself: the TUI writes `queue-operation` `enqueue` for input it accepted-but-queued, `dequeue`/`remove` when the queue drains or drops, and a `user` record on real submission (measured, CARD-0064 investigation). Today those records are deliberately invisible server-side: `TranscriptNormalizer` maps `queue-operation` to `[]` (CARD-0132 S2.2 — "enqueue is not proof of submit"; pinned at `TranscriptNormalizerTests.cs:454`); only `TranscriptCandidateProbe.cs:190-237` reads them, for C4 bind harvest. The detector therefore needs S3 (make them visible as inert rows) before S4 (sweep them).

`queue-operation` lines carry no `uuid` (fixture `tests/Antiphon.Tests/Agents/Fixtures/queued-command.jsonl`); persistence already dedupes null-uuid entries by sequence (`AgentSessionRuntime.cs:587,611-613`), so no schema work is needed.

---

## Slices

### S1 — Skip `/remote-control` on a resume whose bridge is already armed

`SendRemoteControlCommandsAsync` gains a `resumeMode` parameter (passed from `:394`; `null` from `:198` — the fresh card-spawn path is untouched). When `resumeMode` is `Resume` or `Continue`:

- After ready, poll `IRcBridgeProbe.Probe(childPid).Armed` every 250 ms (`RemoteControlArmedPollInterval`) for up to a new `RemoteControlResumeProbeTimeoutMs` (default 5000, on the same settings class as `RemoteControlSetupTimeoutMs`), inside the existing setup budget.
- **Armed observed** → log, skip the `/remote-control` submit entirely, go straight to the `rename-submit` stage (title sync works because the bridge is armed — the CARD-0240 ordering rule is satisfied without typing anything).
- **Window expires unarmed** (resume of a never-bridged session, or Claude writing the state file late) → fall through to today's send. S2 catches the late-arm race.
- A probe exception degrades to the send path; nothing here may fail the launch (CARD-0056 posture).

Pid caveat for the implementer: the probe needs the **claude.exe child pid** (the process that writes `~/.claude/sessions/<pid>.json` — the incident recorded `pid 21240` vs `hostPid 56748`). Verify `adapter.Pid` is the child; if it is the host, fetch the child the way `SessionHealthService.cs:125` does. No DB column, no migration — Claude's own state file is the record, keyed by the new process, so it can never go stale the way a persisted Antiphon-side flag could.

### S2 — Recognize the menu; Esc it; treat it as armed

New server-side matcher `RemoteControlMenuScreen.IsPresent(string renderedScreen)` — conservative: requires **both** literals `"Disconnect this session"` and `"Esc to continue"` (the `Remote Control` heading alone is too generic). Lives beside `RemoteControlPolicy` in Application so S5 shares it.

In `SendRemoteControlCommandsAsync`, a shared local `TryDismissRemoteControlMenuAsync(adapter)`:

1. `SnapshotRenderedScreen()`; if no menu → false.
2. `SendInputAsync("\u001b")`, wait ~500 ms, re-snapshot; one retry; still present → false.
3. Dismissed → true. Never Enter (Enter selects the highlighted row; Esc can never select "Disconnect").

Wire it into both failure paths:

- **Unarmed branch (`:1358`)**: menu present + dismissed → this *is* the armed case (the menu renders the session's claude.ai URL); log, skip `RcDegraded`, continue to `rename-submit`. No menu → degrade exactly as today.
- **Generic catch (`:1389`)** — the retry-exhausted shape (attempt 2/3 typed into an open menu produces no composer evidence): attempt the dismiss before `RaiseRemoteControlDegradedAsync`; still degrade (composer state after failed retries is unknown, so `/rename` stays skipped) but with the menu named in the incident message, and — the part that matters — **the wedge cleared** before returning.

Safety: Claude's measured overlay contract ("Esc is a no-op on an idle empty composer", one Esc restores after `/model` — `ProviderContractCatalog.cs:73-77`) means a false-positive match costs nothing.

### S3 — Normalize `queue-operation` into inert, persisted transcript kinds

`TranscriptNormalizer` maps root `type == "queue-operation"` to one `TranscriptPart` with new `TranscriptKinds` constants mirroring the wire operations: `QueueEnqueue`, `QueueDequeue`, `QueueRemove` (text = root `content`, timestamp = the record's own — which is enqueue time; null uuid is fine per the sequence-dedupe arm). CARD-0132 S2.2's *rule* stands unchanged: enqueue is still not proof of submit; these rows merely make the non-proof visible.

Inertness checklist (the S2.4 discipline — every consumer that must NOT see the new kinds, each with a pin test):

| Surface | Where | Behaviour |
|---|---|---|
| Delivery confirmation | `SessionMessageQueueService.TryFindConfirmingRecordAsync :2016` and the unobservable-baseline arm `:1884` | must **not** confirm on the new kinds (negative test) |
| Working/idle, all three lockstep implementations | server `IsWorkingAsync` (`SessionMessageQueueService.cs:2646` region), runner `TranscriptWorkingState.cs:58` region, client `transcriptModel.ts` | excluded from activity (timestamp trap: enqueue time can predate file-order predecessors, same as `QueuedUserPrompt`) |
| Watchdog/settlement span | `TranscriptPromptSpan.cs:52-55` | already `UserPrompt`-only; add a pin that the new kinds stay invisible |
| Channel reply identity | `ChannelReplyDispatcher` (Gotcha #53 window rules) | not an owning prompt, not a window cap |
| C4 bind | `TranscriptCandidateProbe` reads raw JSONL (`:190-237`), unaffected; `RememberPromptText` stays `UserPrompt`-only | no change |

Rewrite `TranscriptNormalizerTests.cs:454` from "excluded entirely" to "present as housekeeping, excluded from activity". Non-retroactive, like CARD-0132 S2: the tailer does not re-read skipped lines; the currently wedged `70eb4c2d` gains no rows (see execution notes).

### S4 — The swallowed-input watchdog

New sweep (working name `QueuedInputWatchdog`), run from `AgentSupervisorHostedService`'s per-minute pass — the `ChannelReplyLost` global-sweep precedent (Gotcha #54). For each live session (the `SessionHealthService` liveness statuses; kind-gating is implicit — only Claude transcripts produce these rows):

- `E` = latest `QueueEnqueue` row.
- **Closed** if any row with `Sequence > E.Sequence` has kind `UserPrompt`, `QueuedUserPrompt`, `QueueDequeue`, or `QueueRemove` — any conversion or drain activity after the last enqueue means the TUI queue is moving. Sequence-window, **no text matching**: the incident's `"Hi"` (2 chars) is far below `MinMatchChars` (12), so `PromptSubmissionMatch` can never be the gate here.
- **Fires** when not closed, `now − E.Timestamp > QueuedInputStuckMinutes` (default 3; null timestamp falls back to ingestion time), and `IsWorkingAsync == false` — a mid-turn session legitimately holds queued input for the length of the turn; the wedge shape reads idle (the preamble's renames are local-command records, excluded from activity).
- **Verdict:** new `AgentIncidentKind.QueuedInputNeverConverted = 43` (int on the existing column, no migration). Warning at the threshold; re-raised Error after `EscalateToErrorAfterMinutes` (default 15, the `TaskProgressStalled` ladder); Critical when the agent is channel-bound (the CARD-0055/0067 severity rule). Deduped per `(session, E.Sequence)` episode; closure resets the episode.
- **Detection only** (CARD-0153's rule, verbatim): never kills, never types, never Escs. The one narrow keystroke this card allows lives in S2/S5, screen-verified; the sweep's evidence (rows) cannot see the screen.
- **Attention:** new `AttentionKind.QueuedInputStuck` projected in `AttentionService` from open kind-43 incidents (the `ProgressStalled` pattern at `AttentionService.cs:636`), so the row appears in the feed at Warning — not only via `RecentCriticalIncident` (`:1009`) when channel-bound. Client gets the kind label.

Settings block `SupervisionSettings.QueuedInputWatch { Enabled, StuckMinutes, EscalateToErrorAfterMinutes }`.

### S5 — Health-watch re-arm: same guard for the second sender

The dead-bridge arm (`SessionHealthService.cs:236-249`) keeps its in-place `/remote-control` repair (restart is already the escalation and stays so), but after the re-arm's settle window (`ReArmSettleUntilUtc`), the next probe pass first calls a new `ISessionHealthActions.TryDismissRemoteControlMenuAsync(sessionId)`: fetch the rendered screen through the runner, apply `RemoteControlMenuScreen.IsPresent`, and if present send Esc via `AgentSessionRuntime.SendInputAsync(sessionId, "\u001b", trackManualTurn: false)` — the exact plumbing `TryDismissOverlayAsync` (`SessionMessageQueueService.cs:2796`) already uses, including the idle-after-pull guard. A dismissal is recorded on the existing `RcReArmed` incident trail (Warning: "re-arm opened the management menu; dismissed"). If the premise is wrong and `/remote-control` on an armed-but-dead bridge reconnects cleanly, the check sees no menu and costs one snapshot.

### S6 — Pins: fakeclaude menu mode + headed canary

- fakeclaude (`src/Antiphon.FakeClaude/Program.cs`): opt-in `ANTIPHON_FAKE_RC_MENU=1` — `/remote-control` renders the menu shape (no `remote-control is active` line), Esc clears it, input submitted while it stands writes a `queue-operation` `enqueue` JSONL line and no `user` record, and closing the menu drains the queue (enqueue → dequeue → user). Mirrors the measured incident shapes; `FakeClaudeContractTests` pin them.
- Headed `[Explicit]` canary `ClaudeRemoteControlMenuCanaryTests`: pins the real menu literals the matcher anchors on and that one Esc dismisses it. Needs a session with a live bridge, so operator-run, like `ClaudeTrustPromptCanaryTests`.

---

## What this card does not do

- **No automatic recovery of the currently wedged session.** S3/S4 are non-retroactive. The live remedy for `70eb4c2d` (if still wedged at execution time) is one operator Esc — or a restart — noted for the execution brief, not coded.
- **The duplicated `/rename`** — idempotent, harmless; its root (boot-retry evidence timing on slash commands) is a separate card if it recurs.
- **Card item 3's "output sequence barely moved since launch" heuristic** — rejected: an idle session legitimately makes no output; the enqueue shape is the precise signal, the quiet-output shape is noise.
- **No change to CARD-0132 S2.2's rule** — enqueue still never confirms a delivery.
- **The card's "separately observed" items** (13 Failed agents from one sweep; watchdog restart exiting 1 while succeeding; `hook_cancelled` SessionStart timeouts) — own cards.
- **CARD-0293's** RC-enabled-by-default modal work — untouched.
- **No new columns, no EF migration** anywhere in this card.

---

## Test matrix

| Layer | Test |
|---|---|
| `Antiphon.Tests` unit | `RemoteControlMenuScreen` matcher: incident screen fixture → true; trust dialog, plain composer, `remote-control is active` scrollback → false |
| `Antiphon.Tests` Application | Preamble resume-skip (fake adapter + fake probe): resume+armed → no `/remote-control`, `/rename` still sent; resume+unarmed → sends as today; fresh (`resumeMode: null`) → never probes; probe throw → degrades, launch survives |
| `Antiphon.Tests` Application | Unarmed branch with menu on screen → Esc sent, rename proceeds, **no** `RcDegraded`; catch path with menu → Esc sent, degrade message names the menu, `/rename` skipped |
| `Antiphon.Tests` Agents | `TranscriptNormalizerTests`: `queue-operation` → `QueueEnqueue`/`QueueDequeue`/`QueueRemove` rows; the `:454` exclusion test rewritten |
| Lockstep inertness | `TranscriptWorkingStateTests`, client `isWorking` tests, `SessionMessageQueueDeliveryVerificationTests`: new kinds are not activity, not confirmation, invisible to `TranscriptPromptSpan` |
| `Antiphon.Tests` Application | `QueuedInputWatchdogTests`: stuck idle enqueue past threshold → incident 43; closed by each closure kind → none; working session → suppressed; episode dedupe; Error escalation; channel-bound → Critical |
| `Antiphon.Tests` Application | `AttentionService` projects `QueuedInputStuck` from an open kind-43 incident; `SessionHealthTests`: dead-bridge re-arm settle pass invokes the dismiss action; menu-dismissed records on the `RcReArmed` trail |
| `Antiphon.Agents.Pty.Tests` | `FakeClaudeContractTests` RC-menu mode: menu shape, Esc clears, enqueue-no-user while open, drain on close |
| Headed `[Explicit]` | `ClaudeRemoteControlMenuCanaryTests` — real menu literals + Esc dismissal (operator-run) |

Run per `docs/testing-and-build.md`: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0292/` (forward slash), chunked by namespace (`--treenode-filter "/*/Antiphon.Tests.Application/*/*"` etc.), `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` sequentially; delete the `bin-card0292` directories afterwards.

---

## Sequencing and risks

**Order: S1+S2 (kills the incident's cause), S3 → S4 (universal detector), S5, S6 alongside.** Each slice lands independently; S4 hard-depends on S3, S5 on S2's matcher.

| Risk | Disposition |
|---|---|
| `adapter.Pid` may be the host pid, not the claude child the probe needs | Verify at implementation; fall back to the runner live status the way `SessionHealthService.cs:125` does |
| Claude writes `bridgeSessionId` later than the 5s probe window on some resumes | Falls through to today's send; S2's menu guard converts the worst case into one Esc. Tune the window from live logs |
| Menu literals drift across Claude versions | Matcher requires two independent literals; canary pins them; a miss degrades to today's behaviour (never worse than current) |
| `/remote-control` on an armed-but-dead bridge might reconnect cleanly (S5's premise wrong) | Dismiss is conditioned on actually seeing the menu; wrong premise costs one snapshot per settle pass |
| False-positive menu match | Esc measured a no-op on an idle composer (CARD-0137); working sessions are excluded by the idle guards |
| Sweep noise from legitimately queued input | Working gate + 3-minute threshold + closure-on-any-drain; `TaskProgressStalled` precedent says detection-only rows are cheap to be wrong about |
