# CARD-0340 — an interrupted launch is resumed from its durable state, never left Starting

**Date:** 2026-09-03

**Plan task:** 847c702a (Frontier, plan only; no production code changed)

**Card:** CARD-0340 — "Boot prompt never delivered for 10 minutes, then silently fails - hit twice tonight (Codex and Grok)"

**Evidence base:** the Debug pass cba03b61 (`.antiphon/task-cba03b61.md` in worktree `card-task-cba03b61`), re-verified against this tree at `64751058`: `AgentSessionLaunchQueue`, `AgentSessionService.LaunchInteractiveAsync` / `LaunchInteractiveProcessAsync`, `SessionMessageQueueService` (`EnqueueAsync` Starting guard, `FlushSessionAsync`, `FlushStrandedQueuesAsync`, `IsAcceptingInputAsync`, `DeliverNextLockedAsync`, `LateConfirmAttemptedMessagesAsync`), `AgentTaskDispatcher` (`DispatchOneAsync` tail, `FailNeverStartedAsync`, `FailDeadSessionTasksAsync`, `RelaunchWedgedAsync`), `SessionReconciliationService` (all passes), `SessionRunnerEventPump`, the four runner adapters and `RunnerTerminalSession`, `scripts/restart-apphost.ps1`, the CARD-0103 / CARD-0133 / CARD-0299 / CARD-0331 plans, and the Grok ANSI logs for sessions `4d8712fa` and `2e5c3a22`.

## Decision

Make an interrupted launch resumable from what survives a server death: the runner session (a detached pty-host), the `AgentSessions` row (`Starting`), and the brief's queue row (`Pending`). The exact predicate for "interrupted" is the CARD-0331 liveness argument applied to launches: **a `Starting` row that the runner serves and that no launch in this process owns is orphaned, because the only thing that could ever flip it lived in the process that died.** No timeout is needed and none is added.

Resumption re-runs the tail of `LaunchInteractiveProcessAsync` on the existing runner session: attach the kind's adapter, run the kind's own `WaitForReadyAsync` contract, flip `Running`, then `FlushSessionAsync`. Nothing types before the ready verdict, so the 2026-08-09 ready-probe race the investigation flagged cannot be reintroduced: the `Starting` enqueue guard, the stranded watchdog's `Running` requirement, and the ready probe all stay exactly where they are.

The mechanism lives in three places, each doing what it already owns:

| Piece | Owner | Why there |
|---|---|---|
| Launch ownership registry (in-process, exact) | `AgentSessionLaunchQueue` | It already holds the in-flight launch tasks; the registry is the same set keyed by session id. |
| Orphan detection (periodic) | `SessionReconciliationService`, new pass 1c | It already fetches the runner list each 15 s, already special-cases `Starting`, and already has the alert/incident plumbing and a test harness with a fake runner. |
| Resumption / fail-loudly | `AgentSessionService.ResumeInterruptedLaunchAsync` + `AttachAsync` on the runner adapters | The launch path owns readiness, the `Running` flip, the boot flush, and "a failed launch kills what it started" (CARD-0056). |

Not the dispatcher: the hole is session-level. Every `EnqueueInteractiveSession` caller (pool delegates, boot-wedge relaunch, always-on agents, interactive UI starts) leaves the same `Starting` orphan behind a restart.

## Ground truth, as verified

**The launch is in-memory only.** `DispatchOneAsync` commits the `Starting` row and the brief's `Pending` row, then `EnqueueInteractiveSession` does `Task.Run(LaunchInteractiveSessionAsync)`. `restart-apphost.ps1:136` is `taskkill /T /F` on the AppHost tree; `stoppingToken` never fires, there is no drain, and the session-runner is deliberately preserved. A Codex launch spends up to 70 s in `WaitForReadyOrThrowAsync` (60 s quiet + 10 s MCP line), a Claude launch up to 150 s (60 s + 90 s input probe). Every one of those seconds is a window in which a restart leaves the row `Starting` with a live process behind it.

**Nothing owns `Starting` + runner-`Running`.** After the restart:

- `SessionRunnerEventPump.CatchUpTranscriptsAsync` syncs transcripts ("Catch-up settlement for session …") and changes no state.
- `SessionReconciliationService` pass 1 skips a `Starting` row inside `StartingGraceMs` (90 s) and, after it, only acts when the runner does *not* know the session (→ `Failed`) or reports it Exited. Pass 2 (`ReconcileRunnerAliveSessionsAsync`) is `_ => 0` for `Starting`. Pass 1b is herdr-only.
- `FlushStrandedQueuesAsync` requires `IsAcceptingInputAsync`, which is `Status == Running`. Documented on purpose: "redelivering into a booting TUI would re-create the ready-probe kill".
- `EnqueueAsync` refuses to type into `Starting` for the same reason.
- `FailNeverStartedAsync` fires at `DispatchedAt + 10 min`, kills, and `RemoveEphemeralAgentAsync` cascades the incidents. `KillAsync` then sets `FailureReason = null` on a successful kill, which is why the card saw nothing.

**The two instances are not the same shape.**

- Codex `33c53ffb` (session `0ece8181`): restart 76 s after dispatch, inside `WaitForReady`. Row `Starting`, brief `Pending`/attempts 0 for ten minutes. This is the restart bug, fully explained by the above.
- Grok `76a50222` (session `4d8712fa`): brief `Sent` at +38 s, attempts 1, baseline null. The restart came 61 s after `SentAt`, which is *after* the 30 s `TranscriptConfirmTimeoutSeconds` window closed. The verdict (degraded screen-only `Sent`) had already been reached; nothing retries a `Sent` row with or without a restart. Both the failed session's and the successful retry's ANSI logs contain "Starting session" and "MCP (0/2)" exactly once, so "typed during MCP boot" does not discriminate either. **The restart is coincidental for Grok.** The Grok shape is CARD-0133 §7's open item (the positive-submit predicate that Codex received in `09a6a8ba`/`fd6c8a50` never reached Grok). It is named in S4 below and recommended to be tracked there, not here.

**A third restart-shaped hazard the card did not hit, found while reading:** `DeliverNextLockedAsync` stamps `Sent` + `DeliveryAttempts++` *before* typing (the crash-safe direction for graceful shutdown, which reverts in its `OperationCanceledException` arm). A hard kill between the stamp and the end of the confirm loop leaves a `Sent` row that may never have reached the terminal, and no reader retries `Sent`. The window is ≤ ~80 s per delivery, on every queue delivery including completion notes back to the orchestrator. S3 closes it with a persisted verdict.

## Alternatives considered and rejected

- **Graceful drain in `restart-apphost.ps1`.** A 70–150 s ready wait does not fit a restart, `taskkill /F` exists for stuck DCP trees, and a crash gets no drain anyway. Durable resumption covers both.
- **Persist the launch spec and relaunch from scratch on boot.** The resolved spec carries env from `ApiKeyEnvResolver` (custody problem, `docs/agent-credentials.md`), and a relaunch throws away a healthy warm TUI that CARD-0133 measured as the 88 %-good outcome. Kill-and-relaunch stays as the *failure* arm only.
- **Re-stamp `DispatchedAt` on resume** (the `RelaunchWedgedAsync` precedent). `FailNeverStartedAsync` finds the brief row with `m.CreatedAt >= task.DispatchedAt`; a later `DispatchedAt` hides the surviving row and defeats the CARD-0117 D7/D8 arms. The relaunch path gets away with it because it cancels the row and enqueues a fresh one. Resumption keeps the row (it is the evidence) and puts the clock on the session instead.
- **A startup one-shot instead of a periodic pass.** The runner is often unreachable for the first seconds after boot, and the dispatcher's 5 s tick would beat it. The periodic reconciler plus a watchdog deferral (S2) is order-independent.

## S1 — durable launch ownership and resumption

**Files**

- `server/Application/Services/AgentSessionLaunchQueue.cs`
- `server/Application/Interfaces/ILaunchOwnership.cs` (new), `server/Application/Interfaces/IAttachableProtocolAdapter.cs` (new)
- `server/Application/Services/AgentSessionService.cs`
- `server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs`, `RunnerClaudeAdapter.cs`, `RunnerCodexAdapter.cs`, `RunnerGrokAdapter.cs`, `RunnerRawAdapter.cs`, `RunnerOpenCodeAdapter.cs`
- `server/Application/Services/SessionReconciliationService.cs`, `server/Application/Settings/SessionReconciliationSettings.cs`
- `server/Domain/Entities/AgentSession.cs`, `server/Domain/Enums/AgentIncidentKind.cs`, `server/Migrations/20260903200000_AddAgentSessionLaunchResumedAt.cs` (hand-written + snapshot, per the `AddSessionLaunchBlock` convention)
- `server/Program.cs` (register the ownership interface against the existing singleton)
- Tests: `tests/Antiphon.Tests/Application/AgentSessionLaunchQueueOwnershipTests.cs` (new), `AgentSessionInterruptedLaunchResumeTests.cs` (new), `SessionReconciliationServiceTests.cs`, `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs`

1. **Ownership registry.** `AgentSessionLaunchQueue` keeps a `ConcurrentDictionary<Guid, byte>` of session ids it is launching or resuming. Register in `EnqueueInteractiveSession` and in the new `ResumeInterrupted` **before** `Task.Run`, so a session can never be on the runner while unowned; remove in the completion continuation. Expose `bool Owns(Guid sessionId)` and `void ResumeInterrupted(Guid sessionId, Guid agentId)` through `ILaunchOwnership` (implemented by the queue; the reconciler depends on the interface so its tests pass a recording fake). A `ResumeInterrupted` for an id already owned is a no-op. The card-launch `Enqueue` path registers too, for the same invariant, though nothing resumes it (card sessions are `StartAsync`'s business and out of scope).

2. **Attach.** `RunnerTerminalSession.AttachAsync(Guid sessionId, ct)`: `GET` the runner session; require `Status == "Running"` and `Pending == null`; copy `StartedAt`/`Pid`/`ExitReason`; start the `Exited` poll; mark started. Each runner adapter implements `IAttachableProtocolAdapter.AttachAsync` by delegating to its terminal. The in-process Pty adapters do not implement it; the resume path treats that as not resumable. `StartedAt` from the runner is what makes Claude's and Grok's `MinTotalWait` floors already satisfied on a minutes-old process. Grok's sign-in block reason resolves `GROK_HOME` from the process environment when no launch env is attached; say so in the log line.

3. **`AgentSessionService.ResumeInterruptedLaunchAsync(sessionId, agentId, ct)`**, called by the queue from a fresh scope exactly like `LaunchInteractiveSessionAsync`:
   - Reload the row; return if it is no longer `Starting` (a concurrent path already settled it).
   - Stamp `LaunchResumedAt = now` and save first. This is the durable "we are on it" bit and the watchdog's clock (S2).
   - **Resumable** iff an `AgentTasks` row has `AgentSessionId == session.Id && Status == Dispatched`. Both dispatcher launch sites pass `remoteControlName: null, notes: null` and no `initialPrompt`, so a delegate launch has no non-durable extras. Then: `adapter = _adapterFactory.Create(kind)`; `AttachAsync`; `WaitForReadyOrThrowAsync`; `Running` + `LastSeenAt`; save; `WriteRestartBoundaryIfInterruptedAsync`; publish `SessionStarted` and `AgentChanged`; `FlushSessionAsync` (delivers the `Pending` brief now). Write an `AgentTaskEvent` of type `Warning` on the task: "launch resumed after a server restart: the session sat Starting for N s; ready re-verified". The event survives `RemoveEphemeralAgentAsync`, which is the evidence gap the investigation named.
   - **Not resumable** (always-on agents, interactive/UI starts, anything with a resume flag, notes, remote control or an initial prompt): kill through the runner and dispose, then the launch-failure shape byte for byte: `Failed`, `FailureReason` = "Launch was interrupted by a server restart before the session became ready. Its launch notes, remote-control name and initial prompt are not durable, so the process was stopped for a clean relaunch.", `SessionTermination.Record(SystemRequest)`, the agent `Running → Failed`, `AgentChanged`. The always-on supervisor's existing `RestartScheduled` ladder relaunches with full notes; an interactive user sees `Failed` with a reason instead of an eternal `Starting`. Killing here is CARD-0056's "a failed launch must kill what it started", not the reconciler's forbidden "unclaimed → kill": the process was started for this row and nothing will ever use it.
   - On any exception that is not cancellation: `KillAndDisposeAsync(adapter)`, `Failed` with the reason prefixed "Resumed launch after a server restart failed: …", `LaunchBlock` mapped exactly as `LaunchInteractiveAsync` maps it (sign-in required → `ProviderSignInRequired`), agent `Failed`, rethrow so the queue's continuation raises the launch alert with dedup key `launch:resume:{sessionId}`.
   - Record `AgentIncidentKind.LaunchInterruptedByRestart` (new, next value) on the agent in every arm: severity `Warning` when resumed, `Error` when failed; message names the outcome and the seconds spent `Starting`.

4. **Reconciler pass 1c**, run after pass 1 and before 1b, gated by `SessionReconciliationSettings.LaunchResumeEnabled` (new, default `true`): for each DB `Starting` row whose runner session is `Running` with `Pending == null` and `!ownership.Owns(id)`, call `ownership.ResumeInterrupted(id, agentId)`, where `agentId` comes from `Agents.PersistentSessionId == id` (the dispatcher and control service both stamp it before launching). Log one Information line per session naming how long it has been `Starting`. The reconciler changes no row itself; the resume method owns every write. A row still `Starting` and unowned on a later tick (a second restart mid-resume) is simply resumed again; `LaunchResumedAt` is last-wins. Herdr-backed rows are not measured here: route them to the not-resumable arm until a later card measures attach-through-herdr.

5. **`AgentSession.LaunchResumedAt`** (`timestamp NULL`), migration `20260903200000_AddAgentSessionLaunchResumedAt`. No backfill.

6. **Tests.**
   - `AgentSessionLaunchQueueOwnershipTests`: `Owns` is true from enqueue until the launch task settles (blocking fake adapter), false afterwards; `ResumeInterrupted` registers before running; a second call for an owned id is a no-op; a faulted resume still releases ownership.
   - `AgentSessionInterruptedLaunchResumeTests` (fake adapter gains `Attached`/`AttachResult`): a `Starting` row owned by a Dispatched task attaches, becomes `Running`, has its `Pending` Delegation row delivered by the flush, gets `LaunchResumedAt` and the task event; ready `false` → `Failed`, adapter killed, agent `Failed`, reason names the resumed launch; a sign-in block → `LaunchBlock` set; a non-delegate `Starting` row → no attach, runner kill issued, `Failed` with the restart reason; a row already `Running` → no-op; an adapter without attach → not-resumable arm.
   - `SessionReconciliationServiceTests` (extend the nested fake runner): `Starting` + runner `Running` + unowned → one `ResumeInterrupted`; owned → none; runner `Pending` → none (pass 1b's business, unchanged); runner unknown → existing pass-1 outcome after grace; `LaunchResumeEnabled=false` → none.
   - `RunnerTerminalSession.AttachAsync` against a fake `ISessionRunnerClient`: copies `StartedAt`/`Pid`, polls `Exited`; a non-Running or unknown session throws.

## S2 — the watchdog measures the resumed launch, and the evidence survives

**Files**

- `server/Application/Services/AgentTaskDispatcher.cs`
- `server/Application/Services/SessionReconciliationService.cs` (one reason string)
- `tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs`

1. In `FailNeverStartedAsync`, load `Status` and `LaunchResumedAt` for each suspect's session in one query. **Clock:** the suspect's window is measured from `max(task.DispatchedAt, session.LaunchResumedAt)`; a task whose launch was resumed less than `DeliveryFailTimeoutMinutes` ago is not a suspect this tick. **Deferral:** when the session is still `Starting`, the runner still serves it (`_runnerClient.ListAsync`, `Running`, `Pending == null`), and `LaunchResumeEnabled` is on, log at Information ("delivery watchdog deferring: session {id} is an interrupted launch the reconciler will resume") and `continue`. This is what lets a restart longer than ten minutes recover: the dispatcher's first tick at +5 s must not fail a task the reconciler adopts at +15 s. The deferral is bounded by the reconciler's own outcomes (every resume ends `Running` or `Failed`, both of which leave the predicate); when reconciliation is disabled the deferral is off and today's behaviour stands. A null `_runnerClient` (older harnesses) also disables it.

2. After the watchdog's `KillAsync`, stamp `session.FailureReason = "Killed by the delivery watchdog: " + reason` on the reloaded row and save. `KillOnAsync` writes `null` on a clean kill, and the row is the only thing left once the ephemeral agent is removed; the investigation's "why the card saw no incidents" item 2.

3. Pass-1 reason string for a `Starting` row the runner never got: "Session runner does not know this session (the launch failed, the runner restarted, or the server restarted before the launch reached the runner)."

4. Tests: `Starting` + runner-served + brief `Pending` → left `Dispatched`, log recorded; `LaunchResumedAt` two minutes ago with `DispatchedAt` twenty minutes ago → left alone; `LaunchResumedAt` twelve minutes ago → failed as today; the kill stamps `FailureReason`; reconciliation disabled → failed as today.

## S3 — an interrupted delivery attempt is finished, not trusted

Separable from S1/S2; recommended, because it also protects completion notes back to the orchestrator.

**Files**

- `server/Domain/Entities/SessionQueuedMessage.cs`, `server/Migrations/20260903210000_AddSessionQueuedMessageDeliveryVerdict.cs`
- `server/Application/Services/SessionMessageQueueService.cs`, `server/Application/Settings/SupervisionSettings.cs` (`DeliveryVerificationSettings`)
- `tests/Antiphon.Tests/Application/SessionMessageQueueInterruptedAttemptTests.cs` (new)

1. Columns `DeliveryVerdict` (`int NULL`, the existing private `DeliveryVerdict` enum promoted to `Domain/Enums`) and `DeliveryVerdictAt` (`timestamp NULL`). `DeliverNextLockedAsync` stamps both whenever an outcome is known: `Delivered` (including the degraded screen-only verdict and `LateConfirmed`), and every failure verdict before the revert/park. A row that is `Sent` with a null verdict and a `LastDeliveryStartedAt` older than `TranscriptConfirmTimeoutSeconds + PostFailureConfirmGraceSeconds + UnobservableBaselineConfirmClockToleranceSeconds` (80 s at defaults) is by construction an attempt the process did not live to judge.

2. `FlushStrandedQueuesAsync` gains those rows as candidates, limited to `LastDeliveryStartedAt` within `InterruptedAttemptWindowMinutes` (new, default 60) so rows that predate the migration age out, and to the sweep's existing scope (always-on sessions, Delegation and Supervision origins). Per candidate session, under the per-session lock, on a live `Running` idle session:
   - transcript identity match via the existing `LateConfirmAttemptedMessagesAsync` arms → verdict `LateConfirmed`, row stays `Sent`;
   - no match but the body's head fragment is visible on the current screen (`ComposerDeliveryEvidence.HeadFragmentIsVisible`) → the CARD-0055 Enter-only rule: press Enter and run the existing confirm loop; never re-type a body that is on screen;
   - neither → revert to `Pending` with `DeliveryAttempts` kept, so the same pass re-types it under today's rules.
   Working sessions and sessions outside the window are untouched.

3. Tests: a Delivered outcome stamps the verdict; a verdict-less `Sent` row inside the window with a matching `UserPrompt` is late-confirmed; with the head on screen gets Enter only (the fake adapter records one `\r` and no body write); with nothing on screen is reverted with attempts intact; outside the window is untouched; on a working session is untouched.

## S4 — Grok: the cold first brief must not settle as a degraded `Sent`

Not restart-caused (see Ground truth). Recommended home: CARD-0133 §7's Grok item, or a card of its own; listed here so the orchestrator can decide with the evidence in one place. If taken under CARD-0340, measure first (the CARD-0133 S0 discipline): a census of Grok Delegation rows with `DeliveryAttempts == 1`, `LastDeliveryBaselineSequence == null`, `Status == Sent`, joined to the first `UserPrompt` after `SentAt`, to size the rate before changing the predicate. The change itself: port the `09a6a8ba`/`fd6c8a50` positive-submit predicate (sustained emptied composer for `PostEvidenceSettleMs`, un-latched if the head reappears, body still visible at the unobservable deadline → `NoSubmitOutput`) to Grok's branch of `WaitForTranscriptConfirmAsync`, so the row reverts and the stranded sweep re-types instead of the watchdog failing it at ten minutes. Do not add a Grok boot-wedge kill+relaunch until a wedge is measured.

## S5 — optional: make in-flight launches visible before a restart

`GET /api/diagnostics/launches` returning the ownership registry (session id, agent id, kind, started-at, owned-for seconds), added to the diagnostics map in `Program.cs` and to `docs/antiphon-api.md`. `restart-apphost.ps1` prints it as a pre-flight line ("2 launches in flight; they will be resumed after restart") — informational once S1 ships; ASCII-only for PowerShell 5.1. Also the live-verification tool for S1.

## Order, tiers, estimate

| Slice | Depends on | Tier | Verification floor + authoring |
|---|---|---|---|
| S1 | — | High | 4–6 h |
| S2 | S1 | Medium | 1–2 h |
| S3 | — | High | 3–4 h |
| S4 | — | measure first | separate card |
| S5 | S1 | Low | ~1 h |

S1 + S2 ship together (S2's deferral is what makes S1 safe across a long outage). S3 can follow in its own dispatch.

## Verification

1. Focused TUnit classes, sequentially, isolated output path with a forward slash; delete `bin-card0340/` directories afterwards:

    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0340/ -- --treenode-filter "/*/*/AgentSessionLaunchQueueOwnershipTests/*"
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0340/ -- --treenode-filter "/*/*/AgentSessionInterruptedLaunchResumeTests/*"
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0340/ -- --treenode-filter "/*/*/SessionReconciliationServiceTests/*"
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0340/ -- --treenode-filter "/*/*/AgentTaskDeliveryWatchdogTests/*"
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0340/ -- --treenode-filter "/*/*/AgentSessionLaunchFailureTests/*"
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0340/ -- --treenode-filter "/*/*/AgentStartRecoveryTests/*"

   S3 adds `SessionMessageQueueInterruptedAttemptTests` and `SessionMessageQueueDeliveryVerificationTests`.

2. Live, from the main checkout, deliberately doing what memory currently warns against: dispatch a cheap delegate (`delegate.ps1 -Kind Codex -Level Low`, a trivial goal), then run `scripts/restart-apphost.ps1` within 30 s. Expect, in order: "Antiphon server starting"; within ~20 s a reconciler line naming the session as an interrupted launch; a resume line; the row `Running`; the brief `Sent` with a `UserPrompt` behind it; the task Succeeds. `delegate.ps1 -Status` shows the Warning event; `GET /api/agents/{id}/incidents` shows `LaunchInterruptedByRestart` while the agent exists. Repeat once with an always-on agent restarted mid-launch: its row goes `Failed` with the restart reason and the supervisor relaunches it with its notes.

3. After deploy, retire the memory note "avoid restart near fresh dispatch" and update `docs/bootstrap.md`'s restart section: a restart during a launch is now recovered, not avoided.

## Invariants and non-goals

- Nothing types into a `Starting` session. Resumption flips `Running` only after the kind's own ready verdict, on the same adapter code the launch uses. The stranded watchdog and the enqueue guard are unchanged.
- The reconciler never kills. The resume path kills only what a launch started and that the platform has decided to fail, with the reason on the row.
- `DispatchedAt` is never re-stamped by resumption; the watchdog clock is `LaunchResumedAt` on the session. The brief's queue row is evidence and is kept.
- No launch spec, env, or note is persisted. What is not durable is failed loudly and relaunched by the path that already owns it.
- No timeout is widened: `DeliveryFailTimeoutMinutes`, `StartingGraceMs`, `TranscriptConfirmTimeoutSeconds`, `StrandedAgeSeconds` keep their values.
- CARD-0195's leftover (Grok `TranscriptMissing` raised after the ephemeral agent is deleted, so it becomes a standalone alert) is real and unrelated; leave it on CARD-0195.
- Stopping the eleven nightly server recycles is an operations question, not this card's code path.

## Record in `docs/session-runtime-invariants.md`

New gotcha: **Launch ownership is in-process and exact.** A session's launch (`Task.Run` in `AgentSessionLaunchQueue`) dies with the server; the runner session does not. A `Starting` row that the runner serves and that no launch in this process owns is an interrupted launch. It is resumed on the existing process for delegate sessions (ready re-verified, then flushed) and failed loudly with a kill for sessions whose launch extras are not durable. It is never left `Starting` (CARD-0340; live misses 2026-09-03, sessions `0ece8181` Codex and, by a different mechanism, `4d8712fa` Grok).
