# CARD-0691: pooled delegate release, agent delete, PtyHost linger and exit, test-host teardown, and a census on the attention feed

Plan date: 2026-09-25. Plan task: `743f9bdd` (Frontier, server2 Linux phone-home runner, worktree
`feat/card-task-743f9bdd`). Code inspected at `e6c94f89464af8ec0cb5efa15d67665e74400042`, which is
`origin/master` and the task branch tip at planning time. Card `f6927a1a-860a-4d55-bf1a-07eb11372059`
(CARD-0691), board `8988ca03-7414-47ad-b0b6-51556c701703`, read in full from the desktop API.
Evidence: the source files named below (line numbers at `e6c94f89`), the CARD-0221 plan
([zombie agent processes](2026-08-28-card-0221-zombie-agent-processes-plan.md)), the CARD-0298 plan
([native Hangfire zombie census](2026-09-01-card-0298-native-hangfire-zombie-census-plan.md)), and the
audit findings recorded on the card (task `4353b48e`, 2026-09-24). No code was changed, no build or
test was run, no database was written.

The deliverable is three bounded Code rounds. The verification design is folded into this plan (the
dispatch says `Next stage: Code`).

## Summary

The card's five asks are five real holes, and each one is structural rather than a missed signal:

1. **Release is a call, not a state.** Settlement releases a pool delegate only when the task
   `Succeeded` (or failed with zero progress). A delegate whose task settled on a **reported
   `failed` verdict** is never released: its agent row stays `Running`, `PoolIdleSince` stays null,
   the session stays alive, and neither the pool janitor (which retires only `Idle` rows) nor the
   reconciler (which sees DB-Running/runner-Running and agrees) will ever touch it. That is the
   "alwaysOn off, status=Running, no PoolIdleSince, alive 6-74 h" shape exactly. Two more terminal
   writers (`FailAndNotifyAsync` from the overdue-deadline and dead-session sweeps) delete the agent
   row without killing the session, producing "agent rows deleted while their sessions lived on".
   And a kill that fails or does not take still removes the row. Nothing in any of this is
   kind-specific; the per-kind skew is explained by which population hits these paths (D-10).
2. **Deleting an agent never looks at its session.** `AgentService.DeleteAsync` removes the row
   (only a standing specialist gets a busy check); the session, linked only by the agent's
   `PersistentSessionId` string, lives on with no owner.
3. **PtyHost's linger clock starts only when the child exits; there is no owner watch; and the exit
   path awaits the child.** A host whose child never exits never expires (correct for production
   re-adoption, fatal for tests). A `Shutdown` ack received while the child is still alive (the
   runner's `ProcessVanished` sweep sends one) logs "Host exiting" and then blocks in
   `DisposeAsync` on the child-exit observer, with its manifest already deleted: the 14.7 h host.
4. **The test sweep cannot see the hosts it leaks.** `PtyHostLeakSweep.RootFromHostPath` matches 7
   hard-coded prefixes while `TestSessionLogRoot.KnownPrefixes` registers 23; `c502-v26*` and
   `c649-prompt` are in the second list and not the first. Two of the leaking tests also kill only
   on the happy path.
5. **The census exists and reports to nobody.** CARD-0298's `ZombieCensusJob` runs daily at 09:30,
   logs its candidates, and (by its own doc comment) "never calls AttentionService".

The design: make release a **state the dispatcher enforces every tick** (a release sweep that pools or
kills any pool delegate with no open task, no matter which writer forgot), verify every kill before a
row goes, give `Stopping` a retry arm and a deadline, make delete stop-or-refuse, give PtyHost an
opt-in owner-pid watch plus a hard-exit watchdog and a kill-on-shutdown-with-live-child rule, derive
the test sweep's regex from the single prefix list, and project three DB-computed rows plus the OS
census summary onto the attention feed.

## Ground truth

| Card / brief assumption | What the code shows (at `e6c94f89`) | Consequence |
|---|---|---|
| "Settlement of a pooled Codex/Grok task must release its session ... Find why these kinds never transition. Claude pool delegates appear to be released correctly." | `AgentTaskReplyService.SettleAsync` computes `shouldRelease = FailureCode == CompletedWithoutProgress \|\| Status == Succeeded` (`AgentTaskReplyService.cs:884-886`) and passes it as `release:` to `PersistDeliverThenReleaseAsync` (`:1841`, release call `:1942`). A task that settles `Failed` from a `[antiphon-report:<id> failed]` verdict, or `Blocked`, never reaches `ReleaseDelegateAsync` (`:2004`). The two *unreported* failure writers (`FailUnreportedTurnAsync` `:1190`, API-error `:1336`) do release. `ReleaseDelegateAsync` itself has no kind branch; `ResolveAgentAsync` stamps `Kind = task.AgentKind` (`AgentTaskDispatcher.cs:5450`) and the janitor groups by kind only for the per-directory cap (`:6306`). | The leak is kind-agnostic (D-2, D-10). The fix is one release rule applied by state (D-1), not a Codex/Grok patch. |
| "Each agent was still status=Running, alwaysOn off, with no PoolIdleSince." | That is the only shape a skipped release leaves: `ResolveAgentAsync` births the row `Idle`; dispatch sets `Running` (`AgentTaskDispatcher.cs:4360`, `:4663`, warm reuse `:5922`); every release arm sets `Idle`+`PoolIdleSince` or removes the row. `RetireIdleWarmAgentsAsync` (`:6280`) acts only on `Status == Idle && PoolIdleSince != null` (TTL/cap) or on rows whose session is *not* live (`stale`, `:6395-6405`). A `Running` row with a live session and no open task is invisible to it. | The janitor needs a third arm: live session, no open task, terminal task older than a grace, `PoolIdleSince == null` (S2). |
| "Two of them had outlived their agent rows (the agents were deleted while the sessions stayed alive)." | `FailAndNotifyAsync` (`AgentTaskDispatcher.cs:3119-3145`) calls `_tasks.RemoveEphemeralAgentAsync` after `FailAsync` with **no kill**; its callers include the overdue-deadline sweep (`:2473`, reason text literally says "The session was NOT killed") and the dead-session reconciler (`:2277`). `RemoveEphemeralAgentAsync` (`AgentTaskService.cs:2814-2851`) removes any pool agent whose task had a session and no other open task, regardless of session liveness. `ReleaseDelegateAsync`'s kill arm swallows a `KillAsync` failure and then `db.Agents.Remove(agent)` (`AgentTaskReplyService.cs:2071-2094`). `AgentSession` has no FK to `Agent`; the only link is `Agent.PersistentSessionId`. | Row removal must be conditional on the session being terminal (D-3); a live-session row is marked `Idle`+`PoolIdleSince` for the janitor instead (S1). |
| "ea3796b0's session row was stuck in Stopping while the runner still reported it Running." | `KillOnAsync` sets `Stopping` and saves (`AgentSessionService.cs:1327-1328`) *before* `_runtime.KillAsync` (`:1330`); a throw (remote runner, phone-home transport loss, runner 5xx) leaves the row `Stopping` with no later write. `KillAndDisposeAsync` has a deferred-kill arm for that transport loss (`:600-620`, CARD-0679 D-7) but `KillOnAsync` does not. `SessionReconciliationService.ReconcileRunnerAliveSessionsAsync` handles runner-Running against DB `Failed` (re-adopt) and `Stopped` (retry kill) and falls through `_ => 0` for `Stopping` (`SessionReconciliationService.cs:432-437`); pass 1 closes a `Stopping` row only when the runner reports it gone. | `Stopping` is an unbounded state today. S3 gives it a retry arm, a deferred-kill record for remote sessions, and an attention row. |
| "Deleting an agent row must stop or kill its live session." | `AgentService.DeleteAsync` (`AgentService.cs:783-840`) unassigns cards, drops workflow runs, preserves pins, removes the row. Only `StandingSpecialistSeatPolicy.IsCheck(agent)` gets a live-session 409. `AgentEndpoints.cs:117` is the route. | S4 adds stop-then-delete with a 409 when the stop does not take (D-5). |
| "PtyHost must honour --linger-hours" | `--linger-hours` parses into `LingerTtl` (`PtyHostOptions.cs:69-72`, default 24 h). `StartLingerExpiry` (`HostSession.cs:443-447`) is armed on child exit (`:400`) and on a custody launch failure (`:174`) only. `docs/testing-and-build.md:30` already states the consequence: "whose 24 h linger clock never starts because the child never exits". | Linger is honoured for the case it covers. The uncovered case (live child, dead owner) needs an owner watch (D-6); the wording of the ask is corrected in the doc (S8). |
| "and exit when its parent dies" | The host is spawned through `--spawn` with a broken parent chain on purpose (`Program.cs`, `Win32ProcessSpawner` `DETACHED_PROCESS \| CREATE_BREAKAWAY_FROM_JOB`, `PosixProcessSpawner` `setsid`); `ParentDeathSpikeTests` pins that surviving the spawner is the point. There is no parent or owner liveness check anywhere in `src/Antiphon.PtyHost`. | An *unconditional* parent-death exit would break runner restarts (ADR-0002, CARD-0308 doc line). The watch must be opt-in per launch (D-6). |
| "the host exit path must actually exit after the shutdown ack" | `Shutdown()` (`HostSession.cs:336-341`) deletes the manifest and `RequestExit`s without checking the child. `PtyHostServer.RunAsync` logs "Host exiting" (`:76`) and returns; `Program.cs` then disposes the session: `HostSession.DisposeAsync` (`:473-489`) awaits `_runner.DisposeAsync()` (which awaits the ConPTY read task) and then `await _exitObserver`, i.e. `_runner.Exited`, which completes only when the child exits. The runner sends `Shutdown` from `HandleExited` (child gone, fine, `SessionRunnerRuntime.cs:3224`) **and** from `MarkVanishedIfDead` (`:3143`, a probe verdict that can be wrong; `RunnerSessionGenerationTests` feeds it `StubProbe(false)`). | A shutdown ack with a live child must kill the child first, and exit must be bounded regardless (D-7). |
| "Test runs leave Antiphon.PtyHost processes behind ... antiphon-c502-v26-*, antiphon-c649-prompt-*" | `TestSessionLogRoot.KnownPrefixes` lists 23 prefixes including `c502-v26`, `c502-v26-manifest`, `c502-v26-herdr`, `c649-prompt` (`PtyHostLeakSweep.cs:15-22`); `RootFromHostPath`'s regex names 7 (`cpu-watchdog-tests\|liveness-tests\|adoption-tests\|bufbounds-tests\|backend-seam\|first-write-race\|0180-dto`). The sweep is `[After(Assembly)]` and Windows-only. `RunnerMultilinePromptDeliveryTests.The_pty_receives_the_marker_line_of_a_multiline_prompt` kills only as its last statement (`:94`); `RunnerSessionGenerationTests.C502_V26_kill_and_relaunch_publish_their_own_generations` ends with `SweepVanishedSessions(new StubProbe(false))` (a shutdown ack to a host whose child is alive) and a best-effort child kill (`:84-87`). | Derive the regex from `KnownPrefixes`, add a round-trip test, wrap teardown in `finally`, and pass the owner pid so the OS does the rest (S6). |
| "One production host (51192) logged 'Host exiting: shutdown ack from runner' but stayed alive 14.7 h." | See the exit-path row: the log line is written *before* `DisposeAsync`, and dispose is unbounded. | D-7 hard-exit watchdog. |
| "Add a periodic census (scripts/reap-zombie-agents.ps1 already detects these) that reports them on the attention feed." | `ZombieCensusJob` (Hangfire `antiphon:zombie-census`, cron `30 9 * * *`, `ZombieCensusSettings.cs`) runs `ZombieCensusService.RunAsync` (WMI via `WindowsZombieProcessCensus`, runner `GET /sessions`, DB rows with `RunnerId == null` only, manifests) through the `ZombieCensusClassifier` port of the script's I1-I5/Z1-Z7 rules, logs counts and up to 20 candidates, and stores nothing. `AttentionService.GetAsync` is computed live from rows on each request (`AttentionService.cs:195-230`); the nearest existing shape is `AgentOutlivedTask` (`:2513`, CARD-0239), which explicitly excludes pool delegates. `AttentionKind` currently ends at `CompactionContinuationStalled = 43`. | Add DB-computed kinds for the three shapes (they need no WMI and work on every host) and one grouped row from the last census result (S7, D-9). |
| "why Claude pool delegates get released or pooled but Codex/Grok don't (trace settlement to pool release per AgentKind)" | No per-kind release code exists (first row). Two facts do differ by population: (a) Codex/Grok delegates are the runner-bound kinds (CARD-0490, CARD-0660), so their kills travel over phone-home and can throw into the unhandled `KillOnAsync` path; (b) the census `LoadDbSnapshotAsync` drops `RunnerId != null` sessions, so runner-bound zombies were never in the daily census. | D-10: fix kind-agnostically; add `AgentKind` to the new attention rows so the next audit measures the skew instead of inferring it. |
| "The alwaysOn 'slides Orchestrator' agent is Failed with no live session." | Listed under "Also found" with no ask; AlwaysOn supervision is `AgentSupervisorService` territory, untouched here. | Follow-up card (see Follow-ups). |

## Decisions

- **D-1 Release is enforced by state, not by the terminal writer.** A new dispatcher sweep
  `ReleaseUnownedPoolDelegatesAsync` runs every tick after `RetireIdleWarmAgentsAsync`: for each
  `IsPoolDelegate` row with `Status != Stopped`, `PoolIdleSince == null`, no open task (Queued,
  Dispatched, Working, Blocked), no task with `SourceLandingOperationId != null`, whose newest task
  `CompletedAt` is older than `Delegation:PoolReleaseGraceSeconds` (new, default 120) and whose
  `PersistentSessionId` session is Starting/Running/Stopping, it applies the same three-state rule
  `ReleaseDelegateAsync` applies at settlement: Shared + pool enabled + session live → warm
  (`Idle`, `PoolIdleSince = now`, `PoolReservedForRootTaskId` = the newest task's root); otherwise
  kill through `IDelegateSessionStopper` and, only on a verified terminal session, remove the row
  (D-3). Mid-turn sessions are deferred with the janitor's existing `IsWorkingBatchAsync` rule.
  *Why:* there are at least eight terminal writers across three services today (SettleAsync's
  reported Failed/Blocked, `FailAsync` callers in the dispatcher, `FailOccupantAsync`,
  `CancelAsync`); fixing each one leaves the next one to be forgotten, and CARD-0221 already had
  to add a `stale` pass for exactly that reason. *Rejected:* per-writer patches only (S1 still fixes
  the two known writers, because the sweep has a grace and the fast path is the ordinary
  behaviour); a Hangfire job (the dispatcher tick already has the clock, the DB and the stopper;
  the census job is Windows-WMI-bound and daily).
- **D-2 A reported `failed` verdict releases like `Succeeded`; `Blocked` still keeps.** `shouldRelease`
  becomes `Status is Succeeded or Failed` (`CompletedWithoutProgress` keeps its `killSession: false`
  arm). *Why:* the judgement is about the agent, not the verdict, which is what the method's own
  doc comment already says for unreported failures ("one response died, the session did not").
  *Rejected:* killing on `Failed` (CARD-0085: a kill on a false Failed is how a live worker dies;
  a Shared delegate that reported failure is as reusable as one that reported success); releasing
  `Blocked` (the session is the conversation the caller continues).
- **D-3 A pool agent row goes only after its session is verified terminal.** In
  `ReleaseDelegateAsync`'s kill arm, `RetireIdleWarmAgentsAsync`'s retire arm, and
  `RemoveEphemeralAgentAsync`: after the kill, re-read the session; remove the row only if it is
  `Stopped`/`Failed`. Otherwise set `Idle` + `PoolIdleSince = now` + reservation cleared and let the
  janitor retry at its next TTL pass; the third failed retry records one
  `AgentIncidentKind.DelegateReleaseUnresolved` (new, Error) on the agent so it lands in
  `RecentCriticalIncident`. `RemoveEphemeralAgentAsync` applies the same rule with no kill of its
  own: a live session keeps its row (marked `Idle`) so every `FailAndNotifyAsync` caller stops
  producing ownerless sessions without changing its "not killed" promise. *Why:* the row is the
  process's only owner; deleting it on an unverified kill is the CARD-0221 zombie by construction.
  *Rejected:* retry loops inside settlement (CARD-0319 ordering: persist and deliver first, never
  block delivery on a kill); a hard `Failed` on the session row (a lie the reconciler would then
  re-adopt).
- **D-4 `Stopping` gets a retry arm and a deadline, and a remote kill that cannot be sent is
  recorded.** `KillOnAsync`: when `_runtime.KillAsync` throws, keep `Stopping` (the stop intent is
  real, CARD-0256), stamp `FailureReason = "kill not delivered: <message>"`, and for a session with
  `RunnerId != null` and an accepted generation record `RunnerSlotService.RecordDeferredKillAsync`
  (the CARD-0679 D-7 ledger, already drained by `RunnerSlotReconcileJob`). Reconciliation pass 3 adds
  `SessionStatus.Stopping => RetryStoppingKillAsync(...)` for rows whose `LastSeenAt` is older than
  `SessionReconciliation:StoppingRetryAfterSeconds` (new, default 60), reusing `RetryFailedKillAsync`
  and its alert. *Why:* a row that says Stopping while the runner runs it is the one state neither
  pass owns today. *Rejected:* reverting to `Running` (loses the operator's intent); `Failed`
  (re-adopt arm).
- **D-5 Delete stops the session first or refuses.** `AgentService.DeleteAsync`: if
  `PersistentSessionId` names a Starting/Running/Stopping session, call
  `AgentSessionService.KillAsync(sessionId, SessionTerminationSource.OperatorRequest)`, re-read, and
  if the session is still live throw `ConflictException("...", "agent_delete_session_live")`. The
  row is removed only after a terminal session. *Why:* the card's ask verbatim, and the only
  way the delete keeps the three-state rule. *Rejected:* a `?force` that deletes anyway (creates the
  ownerless shape on purpose); an FK cascade from Agent to AgentSession (sessions are history and
  have no FK today).
- **D-6 PtyHost gains an opt-in owner watch: `--owner-pid <pid> --owner-started <iso8601>`.** The host
  polls every 2 s; when the pid is gone, or its start time differs from the given one by more than
  5 s (pid reuse), it kills the child tree (`_runner.KillAsync(5 s)`), deletes the manifest and
  `RequestExit("owner pid N died")`. The runner passes its own pid and start time when
  `SessionRunner:PtyHostExitWithOwner` (new, default **false**) is true. Every test runtime sets it
  true (S6). *Why:* production hosts must outlive a runner restart (ADR-0002; CARD-0308's "do not
  shorten linger" line), so parent death cannot be unconditional; a test process is the one owner
  whose death should take its hosts with it. *Rejected:* Windows kill-on-close job objects or Linux
  `PR_SET_PDEATHSIG` on the host (both unconditional, both break re-adoption); inferring the flag
  from `PtyHostLingerHours < 1` (implicit and surprising).
- **D-7 A shutdown ack with a live child kills the child; exit is bounded.** `HostSession.Shutdown()`:
  when `_status == Running`, log "shutdown ack with live child pid N: killing" and
  `_runner.KillAsync(5 s)` before deleting the manifest. `RequestExit` arms a hard-exit timer
  (`--exit-grace-sec`, default 30): after it, log "hard exit: dispose did not finish" and
  `Environment.Exit(0)`. `HostSession.DisposeAsync` bounds `await _exitObserver` and the custody
  observer with `WaitAsync(10 s)`. *Why:* the runner already published the session's fate before
  acking, so a child left alive has no owner (this is an owner's decision, not an inference from
  "unclaimed"); and a process that has logged "Host exiting" and lives 14.7 h is the bug the card
  names. *Rejected:* relying on `EnsureExitedHostGoneAsync` (it runs only when the same session id
  relaunches).
- **D-8 The test sweep's root regex is derived from `KnownPrefixes`, the sweep runs its
  registered-roots arm on Linux too, and every runtime a test builds exits with its owner.** A
  source-inspection guard in `Antiphon.SessionRunner.Tests` fails when any test file sets
  `PtyHostLingerHours = 0.02` (or `--SessionRunner:PtyHostLingerHours`) without also setting
  `PtyHostExitWithOwner`. *Why:* the leak found tonight is the exact gap between the two lists; a
  guard is how the next prefix does not repeat it. *Rejected:* adding the six missing prefixes by
  hand (the same drift in a month).
- **D-9 The census reaches the feed as DB-computed rows first, OS census second.** New
  `AttentionKind` values (appended after shipped 43): `PoolDelegateUnreleased = 44` (Error: the
  D-1 sweep's own predicate, past twice its grace, so a row here means the sweep is failing or
  disabled), `SessionStopStuck = 45` (Error: `Stopping` older than 5 minutes),
  `SessionUnowned = 46` (Warning: a live session no agent's `PersistentSessionId` names, with no
  card, no `StandingAgentId` and no open task), and `ZombieCensusReport = 47` (Warning: one grouped
  row per census class with candidates, from the last `ZombieCensusResult` the job stored in a new
  singleton `ZombieCensusState`; absent until the job has run). Each row carries `ModelKind` =
  the session's `AgentKind`. The client adds the four kinds to `attention.ts`, `attentionVisuals.ts`
  and the visuals test roster; severity drives the bucket as today. *Why:* the feed is computed live
  (CARD-0239 pattern); WMI is Windows-only and daily, while the pool shape is fully decidable from
  rows on every host including server2. *Rejected:* incidents or alerts (need dedup and an
  escalation path, CARD-0298 §6.5); a census table (the DB rows are the durable truth, the OS
  summary is corroboration; a restart re-runs the job on schedule); moving the census cron (daily
  stays the default; `ZombieCensus:Cron` overrides).
- **D-10 No per-kind release code is added.** The release path is kind-agnostic and stays so. The
  skew the audit saw is attributed, from code, to the runner-bound population (remote kills
  through `KillOnAsync`, which has no deferred-kill arm until S3) and to the terminal-status mix;
  the new attention rows carry `AgentKind` so the next audit counts rather than infers. *Rejected:*
  a `Kind`-switch anywhere in release (it would encode a guess as behaviour).
- **D-11 Scope held.** Out: the Failed AlwaysOn "slides Orchestrator" (supervisor restart policy,
  separate card); extending `ZombieCensusClassifier` to `Antiphon.PtyHost` processes
  (`scripts/reap-orphaned-pty-hosts.ps1` stays the operator tool; the reconciler's
  `PtyHostCensusDiverged` alert covers surplus); the census's `RunnerId == null` filter (WMI sees
  only local processes; the DB rows in S7 cover remote sessions).
- **D-12 Windows-bound rounds run on the desktop.** R2 (PtyHost, SessionRunner tests) needs ConPTY and
  `System.Management`; it is dispatched to a Windows Code runner, not server2. R1 and R3 are
  Linux-runnable.

## Design

### S1 — settlement releases on `Failed`; no pool row is removed while its session lives

Files: `server/Application/Services/AgentTaskReplyService.cs`, `server/Application/Services/AgentTaskService.cs`,
`server/Domain/Enums/AgentIncidentKind.cs` (add `DelegateReleaseUnresolved = 76`).

- `SettleAsync`: `shouldRelease = task.Status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed`
  (`killSession` unchanged). Update the comment block above it.
- `ReleaseDelegateAsync` kill arm: after `KillAsync`, load the session with a fresh no-tracking read;
  `Remove(agent)` only when `Status is Stopped or Failed`; otherwise `Idle` + `PoolIdleSince = now`
  + `PoolReservedForRootTaskId = null`, log at Warning with the session id, and record
  `DelegateReleaseUnresolved` once per (agent, session) via `RecordIncidentOnceAsync` **before** the
  row would have gone (incidents cascade). Extract the shared "pool warm / kill / mark idle" body
  into an internal static `PoolDelegateRelease.Apply(...)` so S2 reuses it verbatim.
- `RemoveEphemeralAgentAsync`: after the sole-owner check, read the session; live → mark `Idle` +
  `PoolIdleSince` (log "left for the janitor"), terminal or absent → remove as today.

Tests (red on today's code):

- `AgentTaskReplyIntegrationTests.a_failed_verdict_pools_a_shared_delegate_warm` — seed a Shared
  pool delegate, settle a marked `failed` turn through `OnTurnEndAsync`; assert the agent row is
  `Idle` with `PoolIdleSince` set and the stopper recorded no kill. Red today: row stays `Running`.
- `AgentTaskReplyIntegrationTests.a_failed_verdict_stops_a_worktree_delegate` — same with
  `Workspace = Worktree`; assert the stopper killed the session and the row is gone after a
  terminal session. Red today: neither.
- `AgentTaskReplyIntegrationTests.a_kill_that_leaves_the_session_live_keeps_the_row_idle` — stopper
  seam leaves the session `Running`; assert the row survives as `Idle` with `PoolIdleSince` and one
  `DelegateReleaseUnresolved` incident. Red today: row removed.
- `AgentTaskServiceIntegrationTests.RemoveEphemeralAgentAsync_marks_a_live_session_row_idle_instead_of_deleting_it`
  and `..._removes_the_row_when_the_session_is_terminal`. Red today: first case deletes.
- `AgentTaskOverdueDeadlineTests.an_overdue_pool_delegate_keeps_its_row_idle_while_the_session_lives`
  (extends the existing pool-delegate seed at `:910`). Red today: row deleted.

### S2 — the dispatcher release sweep and verified retirement

Files: `server/Application/Services/AgentTaskDispatcher.cs`, `server/Application/Settings/DelegationSettings.cs`
(`PoolReleaseGraceSeconds = 120`, `PoolReleaseMaxKillRetries = 3`).

- `ReleaseUnownedPoolDelegatesAsync(ct)` per D-1, registered in the tick list directly after the
  warm-agent janitor (`AgentTaskDispatcher.cs:335` region) as `RunSweepAsync("pool release", ...)`.
  It uses `PoolDelegateRelease.Apply` (S1), the janitor's `IsWorkingBatchAsync` deferral, and the
  same `sourcedOwnerIds` exclusion the janitor already computes.
- `RetireIdleWarmAgentsAsync`: `KillPooledSessionAsync` returns whether the session is terminal
  after the kill; `FinishPoolRetire` removes the row only then, otherwise refreshes `PoolIdleSince`
  and counts the retry on the agent (`PoolKillRetries`, new nullable int column, migration
  `AddAgentPoolKillRetries`); on the third failure record `DelegateReleaseUnresolved` and set
  `Stopped` so the row stops churning (it stays visible via the incident).

Tests (red today), all in `AgentTaskPoolTests` unless stated:

- `the_release_sweep_pools_a_running_shared_delegate_whose_task_settled_failed` — seed
  `Running` pool row, live session, one `Failed` task with `CompletedAt` 3 minutes ago; run the sweep;
  assert `Idle` + `PoolIdleSince` + reservation for the task's root.
- `the_release_sweep_kills_a_running_worktree_delegate_with_no_open_task` — stopper kill recorded,
  row removed after the stopper flips the session terminal.
- `the_release_sweep_leaves_a_delegate_inside_the_grace` (CompletedAt 30 s ago) and
  `..._leaves_a_delegate_with_a_blocked_task` and `..._leaves_a_sourced_owner` — no change.
- `the_release_sweep_defers_a_mid_turn_session_and_refreshes_nothing` — `IsWorking` true: untouched,
  one Warning log.
- `the_janitor_keeps_the_row_when_the_kill_did_not_take` — stopper leaves the session live; assert
  row still present, `Idle`, `PoolKillRetries == 1`; after three passes `Stopped` + incident.
- `AgentTaskSettlementRaceTests` unchanged rows stay green (the sweep must not race a settlement in
  flight: it reads `CompletedAt` older than the grace, so a task settled within the last 120 s is
  the fast path's).

### S3 — `Stopping` is retried, bounded, and recorded

Files: `server/Application/Services/AgentSessionService.cs`, `server/Application/Services/SessionReconciliationService.cs`,
`server/Application/Settings/SessionReconciliationSettings.cs` (`StoppingRetryAfterSeconds = 60`).

- `KillOnAsync`: wrap `_runtime.KillAsync` + `GetSessionAsync`; on exception keep `Stopping`, set
  `FailureReason = "kill not delivered: " + message`, `LastSeenAt = now`, save; if
  `session.RunnerId is not null` record `RecordDeferredKillAsync` with the session generation the
  launch path uses (`acceptedGeneration ?? session.StartedAt`, `AgentSessionService.cs:357`, i.e.
  `SessionGeneration.Normalize(session.StartedAt)`) with reason "settlement/operator kill after
  transport loss"; rethrow (callers already catch and log).
- `ReconcileRunnerAliveSessionsAsync`: add
  `SessionStatus.Stopping when now - row.LastSeenAt > StoppingRetryAfter => await RetryFailedKillAsync(row, runnerSession, ct)`
  (the existing method already alerts on a failed retry with dedup key `reconciler:kill:{id}`).

Tests (red today):

- `SessionReconciliationServiceTests.Stopping_session_the_runner_still_serves_past_the_retry_window_gets_the_kill_reissued`
  and `Stopping_session_inside_the_retry_window_is_left_alone`.
- `AgentSessionServiceIntegrationTests.a_kill_whose_runner_call_throws_keeps_Stopping_and_names_the_reason`
  and `..._records_a_deferred_kill_for_a_remote_session` (assert the
  `RunnerSlotReleaseIntent` incident shape `PhoneHomeDeferredKillTests` already asserts).

### S4 — delete stops the session or refuses

Files: `server/Application/Services/AgentService.cs`, `server/Api/Endpoints/AgentEndpoints.cs` (no route
change; document the 409 code), `docs/ops-http.md`.

- `DeleteAsync` per D-5, using the scoped `AgentSessionService` (already resolvable in this
  service's graph; use `IDelegateSessionStopper` if the constructor must stay narrow, and pass
  `SessionTerminationSource.OperatorRequest` through a new overload on the interface).

Tests (red today), `AgentServiceIntegrationTests`:

- `DeleteAsync_kills_a_live_session_before_removing_the_row` (stopper records the kill and flips the
  session; row gone).
- `DeleteAsync_refuses_when_the_session_stays_live` (409 `agent_delete_session_live`, row kept).

### S5 — PtyHost: owner watch, shutdown-with-live-child, hard exit, bounded dispose

Files: `src/Antiphon.PtyHost/PtyHostOptions.cs` (`OwnerPid`, `OwnerStartedUtc`, `ExitGrace`; parse
`--owner-pid`, `--owner-started`, `--exit-grace-sec`), `src/Antiphon.PtyHost/HostSession.cs`
(`StartOwnerWatch`, `Shutdown` kill rule, bounded `DisposeAsync`, hard-exit timer in `RequestExit`),
`src/Antiphon.PtyHost/PtyHostServer.cs` (call `session.StartOwnerWatch()` beside
`StartLaunchTimeout()`), `src/Antiphon.PtyHost/Program.cs` (log the hard-exit arm),
`src/Antiphon.PtyHost.Client/PtyHostLauncher.cs` (`ownerPid`/`ownerStartedUtc` parameters →
`BuildHostArgs`), `src/Antiphon.SessionRunner/SessionRunnerSettings.cs` (`PtyHostExitWithOwner`),
`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs` (`:2243-2262`: pass `Environment.ProcessId` and
`Process.GetCurrentProcess().StartTime.ToUniversalTime()` when the flag is on).

Tests (red today):

- `HostSessionPipeTests.a_shutdown_ack_with_a_live_child_kills_the_child_and_completes_the_run` —
  in-proc `HostHarness`, launch `ping -n 60`, send `Shutdown` without `Kill`; assert `RunTask`
  completes within 10 s and the child pid is gone. Red today: `RunTask` completes but the harness's
  `DisposeAsync` (mirroring `Program.cs`) blocks; assert via a 10 s `WaitAsync` on dispose.
- `HostSessionPipeTests.linger_fires_after_child_exit_and_the_run_completes` — `LingerTtl = 1 s`,
  child `exit 0`, no ack; assert the exit reason is the linger reason. (Guards the honoured case.)
- `HostSessionPipeTests.the_hard_exit_timer_is_armed_on_request_exit` — expose the armed
  `Task`/reason through an internal seam; assert armed with the configured grace. (The real
  `Environment.Exit` is not exercised in-proc.)
- `HostOwnerDeathTests.a_host_exits_when_its_owner_pid_dies` (new class, out-of-proc, Windows +
  Linux): start a throwaway owner (`cmd /c ping -n 60` / `sleep 60`), launch the host with
  `--owner-pid` and `--owner-started` from that process, `Launch` a long child, kill the owner;
  assert host and child pids are gone within 15 s and the manifest is deleted.
- `HostOwnerDeathTests.a_reused_owner_pid_with_a_different_start_time_counts_as_dead` — pass a
  stale `--owner-started`; the host exits at its first poll.
- `PtyHostLauncherTests.owner_arguments_are_passed_only_when_requested` (2 arguments rows).
- `PtyBackendSeamTests` or `HerdrLaunchShapeTests` companion:
  `the_runtime_passes_its_own_pid_as_owner_when_PtyHostExitWithOwner_is_set` via the launcher seam.

### S6 — test-host teardown

Files: `tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs` (regex from `KnownPrefixes`;
registered-roots arm on Linux reading `/proc/<pid>/cmdline`), new
`tests/Antiphon.SessionRunner.Tests/TestRunnerSettings.cs` (`ForPtyHostTests(logRoot)` returning
`SessionRunnerSettings { SessionLogPath, PtyHostLingerHours = 0.02, PtyHostExitWithOwner = true, CpuWatchdogEnabled = false }`),
new `PtyHostTestSettingsGuardTests` (source scan per D-8), `RunnerSessionGenerationTests.cs` and
`RunnerMultilinePromptDeliveryTests.cs` (try/finally `TestSessionTeardown.KillAndAwaitHostExitAsync`),
`LocalHttpRunner.cs` (`--SessionRunner:PtyHostExitWithOwner true`),
`tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs` (flag on), 
`tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs` (`SessionRunner__PtyHostExitWithOwner=true`),
and the remaining runtime-building test files the guard names (28 files build a
`SessionRunnerRuntime` today; the guard's first red run lists them).

Tests (red today):

- `PtyHostLeakSweepTests.every_known_prefix_round_trips_through_RootFromHostPath` — for each
  `KnownPrefixes` entry build `<temp>/antiphon-<prefix>-<32hex>/pty-hosts/bin/x/Antiphon.PtyHost.exe`
  and assert `IsOwnedHost` is true with that root registered. Red today for 16 of 23.
- `PtyHostTestSettingsGuardTests.every_linger_override_in_tests_also_exits_with_owner` — red today
  for every file.

### S7 — census on the attention feed

Files: `server/Application/Dtos/AttentionDtos.cs` (kinds 44-47 with doc comments),
`server/Application/Services/AttentionService.cs` (`BuildPoolDelegateUnreleasedItemsAsync`,
`BuildSessionStopStuckItemsAsync`, `BuildSessionUnownedItemsAsync`, `BuildZombieCensusItems`; add to
the list after `BuildAgentOutlivedTaskItemsAsync`), new `server/Infrastructure/Agents/ZombieCensusState.cs`
(singleton: `Latest` result + `GeneratedAtUtc`), `server/Infrastructure/Agents/ZombieCensusJob.cs`
(store the result), `server/Program.cs` (register the state), `client/src/api/attention.ts`,
`client/src/features/attention/attentionVisuals.ts`, `client/src/features/attention/attentionVisuals.test.ts`.

Predicates:

- `PoolDelegateUnreleased`: the D-1 predicate with age `> 2 × PoolReleaseGraceSeconds`; Error;
  `Actions: [OpenAgent, OpenDrawer]`; Evidence names the newest task, its status, `AgentKind`, and
  the age.
- `SessionStopStuck`: `Status == Stopping && now - LastSeenAt > 5 min`; Error; includes
  `FailureReason` (the S3 "kill not delivered" text) and whether the runner lists it when
  `RunnerConsulted`.
- `SessionUnowned`: live session, `CardId == null`, `StandingAgentId == null`, no agent whose
  `PersistentSessionId == id`, no open task with `AgentSessionId == id`, `StartedAt` older than
  10 min; Warning.
- `ZombieCensusReport`: one row per class in {PoolExpired, EndedButAlive, Unclaimed} with count > 0
  from `ZombieCensusState.Latest`, Warning, Evidence = up to 5 `pid/agent/session` triples and
  `GeneratedAtUtc`; omitted entirely when no result exists.

Tests (red today): `AttentionServiceTests` — `a_running_pool_delegate_with_a_failed_task_older_than_twice_the_grace_is_unreleased`,
`a_pool_delegate_inside_twice_the_grace_is_not_listed`, `a_stopping_session_older_than_five_minutes_is_stop_stuck`,
`a_live_session_no_agent_names_is_unowned`, `a_live_session_an_agent_names_is_not_unowned`,
`the_last_census_result_is_one_row_per_class_with_candidates` (seed `ZombieCensusState`);
`ZombieCensusServiceTests.the_job_stores_its_result_for_the_attention_feed`; client
`attentionVisuals.test.ts` roster includes the four kinds and `homeBucketOf` maps the two Errors to
`broken` and the two Warnings to `review`.

### S8 — docs

- `docs/session-runtime-invariants.md` (three-state bullet at `:207-216`): release is enforced by the
  dispatcher's pool-release sweep; a reported `failed` releases; a row is removed only after a
  verified terminal session; `Stopping` retries after 60 s; delete stops or refuses (409
  `agent_delete_session_live`).
- `docs/testing-and-build.md` (`:30` neighbourhood): test hosts run with `PtyHostExitWithOwner`; the
  sweep's regex is derived from `KnownPrefixes`; the guard test's name; the precise linger
  semantics ("the linger clock starts at child exit; a live child never expires; owner death is
  the test-time bound").
- `docs/adr/0002-modern-conpty-backend.md`: owner watch (opt-in), shutdown-with-live-child, hard exit.
- `docs/ops-http.md` (`:261`): the delete 409 code; the four attention kinds.
- `docs/bootstrap.md` (`:481`): census now feeds `ZombieCensusReport`.
- `AGENTS.md` "Sessions and pty": one clause on the existing three-state bullet — "the dispatcher's
  pool-release sweep enforces it for pool delegates; no terminal writer may rely on being the only
  release".

### Code rounds

| Round | Slices | Runner | Depends on |
|---|---|---|---|
| R1 | S1, S2, S3, S4 | any (Linux-runnable; shared Postgres) | — |
| R2 | S5, S6 | Windows desktop Code runner (ConPTY, `System.Management`) | — (parallel with R1; disjoint areas) |
| R3 | S7, S8 | any | R1 (the `PoolReleaseGraceSeconds` setting and the `DelegateReleaseUnresolved` kind) |

R1 and R2 may run in parallel worktrees; they share no files. R3 follows R1.

## Migration and rollback

- One EF migration (`AddAgentPoolKillRetries`, nullable int on `Agents`). Rolling back the code
  leaves the column unused.
- New settings all default to today's behaviour except `PoolReleaseGraceSeconds` (the sweep is new
  and on; set `Delegation:PoolReleaseGraceSeconds = 0` to disable it) and `PtyHostExitWithOwner`
  (default false; production hosts are unchanged).
- `--owner-pid` and `--owner-started` are additive host arguments. `PtyHostOptions.Parse` walks the
  argument list one token at a time and only consumes a value for a name it knows, so an older host
  binary given the new pair ignores both tokens. In practice no cross-version launch exists anyway:
  the runner shadow-copies the host from its own build (`PtyHostLauncher.CurrentShadowDir`). Stated
  so Review does not have to re-derive it.

## Verification design

Coverage classes: V = new red tests that fail at `e6c94f89` for the reason stated in the slice; R =
existing classes that must stay green because the slice touches their code path.

- V-1 S1 settlement release on `Failed` (3 tests, `AgentTaskReplyIntegrationTests`).
- V-2 S1 live-session row retention (2 tests `AgentTaskServiceIntegrationTests`, 1 test `AgentTaskOverdueDeadlineTests`).
- V-3 S2 release sweep (6 tests) and verified retirement (1 test), `AgentTaskPoolTests`.
- V-4 S3 `Stopping` retry (2 tests `SessionReconciliationServiceTests`) and kill-failure recording (2 tests `AgentSessionServiceIntegrationTests`).
- V-5 S4 delete (2 tests `AgentServiceIntegrationTests`).
- V-6 S5 host exit semantics (3 tests `HostSessionPipeTests`, 2 tests `HostOwnerDeathTests`, 1 test `PtyHostLauncherTests`, 1 runtime arg test).
- V-7 S6 sweep and guard (2 tests).
- V-8 S7 attention rows (6 tests `AttentionServiceTests`, 1 test `ZombieCensusServiceTests`, client roster).
- R-1 settlement, dispatcher, reconciliation, session-service and dead-session/overdue classes named in the table.
- R-2 PtyHost pipe/launcher/spike classes and the SessionRunner host-mediated classes named in CP-7.
- R-3 attention, census and Hangfire startup classes.

Red-first rule: each V test is committed red (with the failing assertion message quoted in the commit)
before the production change in its slice. A test that passes before the change is a stub (CARD-0585
rule 4) and is rewritten.

### Checkpoints

Test projects as stated; isolated outputs `bin-c691a/` (R1, `Antiphon.Tests`), `bin-c691p/` (R2,
`Antiphon.PtyHost.Tests`), `bin-c691r/` (R2, `Antiphon.SessionRunner.Tests`), `bin-c691b/` (R3,
`Antiphon.Tests`), forward slash; one build per project per round, every other row `--no-build`; on
Linux `run-checkpoint.ps1` adds `UseAppHost=false` itself. `Min` is the count of `[Test]` methods in the
named classes at `e6c94f89` (`grep -c '^\s*\[Test'`) plus the new methods, minus a 5 % floor
allowance; a floor, not a census. `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` are never
co-scheduled; CP-6 and CP-7 run sequentially on Windows.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c691a/` | reply-release | `/*/*/AgentTaskReplyIntegrationTests/*` | V-1, R-1 | all listed, 0 failed | 125 | 14 |
| CP-2 | S1 | CP-1 | task-service-fail-paths | `/*/*/(AgentTaskServiceIntegrationTests*)\|(AgentTaskOverdueDeadlineTests*)\|(AgentTaskDeadSessionReconciliationTests*)/*` | V-2, R-1 | all listed, 0 failed | 135 | 12 |
| CP-3 | S2 | CP-1 | pool-sweep | `/*/*/(AgentTaskPoolTests*)\|(AgentTaskSettlementRaceTests*)\|(CodexDelegateDispatchTests*)\|(GrokDelegateDispatchTests*)/*` | V-3, R-1 | all listed, 0 failed | 85 | 10 |
| CP-4 | S3 | CP-1 | stopping-retry | `/*/*/(SessionReconciliationServiceTests*)\|(AgentSessionServiceIntegrationTests*)\|(PhoneHomeDeferredKillTests*)\|(PhoneHomeReconciliationTests*)\|(AgentSessionRuntimeTests*)/*` | V-4, R-1 | all listed, 0 failed | 98 | 9 |
| CP-5 | S4 | CP-1 | agent-delete | `/*/*/AgentServiceIntegrationTests/*` | V-5, R-1 | all listed, 0 failed | 50 | 4 |
| CP-6 | S5 | `tests/Antiphon.PtyHost.Tests -> bin-c691p/` | host-exit | `/*/*/(HostSessionPipeTests*)\|(PtyHostLauncherTests*)\|(ParentDeathSpikeTests*)\|(HostOwnerDeathTests*)/*` | V-6, R-2 | all listed, 0 failed (Windows; `ParentDeathSpikeTests` skips off Windows) | 20 | 7 |
| CP-7 | S5-S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c691r/` | runner-hosts | `/*/*/(PtyHostLeakSweepTests*)\|(PtyHostTestSettingsGuardTests*)\|(RunnerSessionGenerationTests*)\|(RunnerMultilinePromptDeliveryTests*)\|(PtyBackendSeamTests*)\|(PtyHostAdoptionTests*)/*` | V-6 (runtime arg), V-7, R-2 | all listed, 0 failed; the `[After(Assembly)]` sweep line reads `no leaked pty-hosts` | 18 | 9 |
| CP-8 | S6 | CP-1 | direct-client-hosts | `/*/*/(SessionGenerationExitTests*)\|(SessionQueueReceiptPlumbingTests*)/*` | R-2 (`DirectSessionRunnerClient` flag) | all listed, 0 failed | 14 | 6 |
| CP-9 | S7 | `tests/Antiphon.Tests -> bin-c691b/` | attention-census | `/*/*/(AttentionServiceTests*)\|(ZombieCensusServiceTests*)\|(HangfireStartupSafetyTests*)/*` | V-8, R-3 | all listed, 0 failed | 145 | 12 |
| CP-10 | S7 | n/a | client-attention | `pwsh -File scripts/test-client.ps1 attentionVisuals` | V-8 (client) | `CLIENT TESTS EXIT CODE: 0`; roster test names the four kinds | n/a | 3 |
| CP-11 | S8 | n/a | docs-named | `git grep -n -e "PoolReleaseGraceSeconds" -e "PtyHostExitWithOwner" -e "agent_delete_session_live" -e "PoolDelegateUnreleased" -e "StoppingRetryAfterSeconds" -- AGENTS.md docs/session-runtime-invariants.md docs/testing-and-build.md docs/ops-http.md docs/adr/0002-modern-conpty-backend.md docs/bootstrap.md` | S8 | ≥ 8 matching lines across ≥ 5 files, exit 0 | n/a | 1 |

The pipe characters inside `Filter` cells are escaped for the table; the command line uses a plain
`|`, quoted as [docs/testing-and-build.md](../../testing-and-build.md#combined-class-filters-card-0403)
shows. Run each row with `scripts/run-checkpoint.ps1 -Name CP-n -Project <project> -OutputPath
bin-c691x/ -Filter '<filter>' -MinExecuted <Min> -Expect <classes> -ResultsRoot .antiphon/c691-checkpoints`
(`-NoBuild` for reuse rows). CP-6 and CP-7 are Windows rows (D-12); if R2's runner is not Windows the
rows are reported as not run with the reason and Review runs them. The E2E fixture change in S6 is a
one-line environment addition whose effect CP-7's launcher-argument test proves; no E2E row is listed
for that reason.

### Cost

Ordinary Code floor is the sum of `EstimatedMinutes`: R1 = 14 + 12 + 10 + 9 + 4 = **49** minutes of
checkpoints; R2 = 7 + 9 + 6 = **22**; R3 = 12 + 3 + 1 = **16**. Authoring: R1 about 3 h (four services,
one migration, ~17 tests), R2 about 3 h (host + launcher + runtime + 28 test files via the guard),
R3 about 2 h.

## Follow-ups (not in this card)

- AlwaysOn agent `Failed` with no live session and no supervisor relaunch (the "slides
  Orchestrator" finding): file a card against `AgentSupervisorService`.
- `ZombieCensusService.LoadDbSnapshotAsync` excludes `RunnerId != null` sessions; a runner-side
  process census for phone-home runners would close that gap (needs a runner endpoint).
- `PtyHostLeakSweep` could read `/proc` on Linux for the earlier-run arm as well; S6 does only the
  registered-roots arm there.
- `scripts/reap-zombie-agents.ps1` class A (`PoolExpired`) becomes rare once S2 lands; consider
  retiring its `-Execute` path in favour of the server sweep after a burn-in.

## Open defaults (stated, not asked)

- `PoolReleaseGraceSeconds = 120`: long enough that a settlement in flight (persist, deliver,
  release) is never raced; short enough that a forgotten release is fixed within three ticks.
- `StoppingRetryAfterSeconds = 60` and `SessionStopStuck` at 5 minutes: one retry window before
  the feed calls it stuck.
- `--exit-grace-sec = 30`: longer than any observed dispose; a host that needs more is the bug.
- Owner poll 2 s, pid-reuse tolerance 5 s (matches the census's `PidReuseToleranceSeconds`).
