# 2026-11-03: a server2 dispatch failed during a desktop runner restart

Debug task 6fd10cb5. Read-only diagnosis at master `bcfe77b3d`. Nothing was built or run.

## Incident

| UTC | Event |
|---|---|
| 07:53:17-07:55:04 | Deploy 53c61115 B4 restarts the desktop session runner (17204) softly. The new runner process starts at 07:54:56. |
| 07:54:52.045 | Code task d8d8a562 (CARD-1677 S1, `runnerId=server2`, `runnerSelectionSource=Explicit`) gets its `Dispatched` event: "Dispatched to agent 'task-d8d8a562' (opus)". |
| 07:54:57.774 | The task is `Failed`: "Dispatch failed before a session existed: A task was canceled." |
| 08:08 | Session 6b2f41d1 is still `Running` on server2 (lastSeen 08:08:16). The pool release kept the agent Idle for the janitor, and the supervision FailureDelegate notice went to that session at 08:07:51. |

The failure came 5.7 s after the `Dispatched` event. That gap fits the 5 s
`AgentSession:RunnerLiveReadTimeoutMs` deadline (or the client's own 3 s
`ListTimeoutSeconds` cap) on a desktop runner that was shutting down: it still
accepted connections but did not answer. I could not read the server log
(`C:\src\Antiphon\server\logs\antiphon-20261103.log`) from server2, and the server
has no HTTP log route. The deploy report names `AgentSessionRuntime.ListLocalLiveSessionsAsync`
in the stack trace. The chain below follows from the code and has only one path
that matches every observed fact.

## (1) Why a server2 task lists the desktop runner's sessions

The list comes after the claim and launch have committed. It is not a capacity
check, a duplicate guard or a pool search. It runs in the brief's delivery probe:

1. `AgentTaskDispatcher.DispatchOneAsync` commits the claim (`ConsumeAndCommitLaunchAsync`)
   and enqueues the launch on server2 (`server/Application/Services/AgentTaskDispatcher.cs:6359-6364`).
   This is why the session exists and is still running.
2. It then calls `EnsureLaunchBriefAsync` (`AgentTaskDispatcher.cs:6399`), which calls
   `SessionMessageQueueService.EnsureDispatchBriefAsync`. That method commits the brief row,
   then probes: `if (probe is not null && await _runtime.IsLiveOrUnknownAsync(probe, ct) && ...)`
   (`SessionMessageQueueService.DispatchBrief.cs:169`).
3. `AgentSessionRuntime.IsLiveOrUnknownAsync(AgentSession)` (`AgentSessionRuntime.cs:2091-2098`)
   sees `RunnerId == "server2"` and calls `IsLiveOrUnknownAsync(id, "server2")`. That is
   `RemoteInventoryPending("server2") || ListLiveOrUnknownSessionsAsync().Contains(id)`.
4. `ListLiveOrUnknownSessionsAsync` -> `ListLiveSessionsAsync` -> `ListLocalLiveSessionsAsync`
   (`AgentSessionRuntime.cs:2003-2024`) always makes the local (desktop) runner `ListAsync`
   RPC under `LiveReadDeadline`. Only then does it add the phone-home cache
   (`_directory.LiveRemoteSessionIds()`, no RPC).

The result is used only to decide whether to deliver the brief inline. A session bound
to server2 can never appear in the desktop runner's list. The local RPC adds nothing
to the answer for a remote-bound session, but its failure is still fatal.

## (2) Why one runner's failure is fatal

There are two defects. Either one alone would have prevented the failure.

- **D-1: the post-commit brief guard excludes cancellation.** The guard is
  `catch (Exception ex) when (ex is not OperationCanceledException)` (`AgentTaskDispatcher.cs:6403`).
  Its own comment says a briefly unreachable runner must not fail the task, because the
  brief row is already persisted. But the deadline (`WaitAsync(linked.Token)` on the
  `LiveReadDeadline` CTS) and the client's `CancelAfter` both throw `TaskCanceledException`,
  which is an OCE subclass. So the exception escapes the guard. The dispatcher's
  ct was not canceled, so the tick loop's
  `catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)`
  (`AgentTaskDispatcher.cs:1110`) takes it and calls `FailAndNotifyAsync`. That writes the
  wrong reason, "before a session existed": the claim and session had committed.
  An `HttpRequestException` (connection refused) would have been caught and logged
  harmlessly. Only a timeout or cancellation is fatal, and that is the shape a draining
  runner produces. This breaks `docs/session-runtime-invariants.md:1324` ("An HttpClient timeout is an
  OperationCanceledException — check the token before treating it as shutdown"). The
  reuse path already follows that rule (`TryEnsureReuseBriefAsync`,
  `AgentTaskDispatcher.cs:9108-9123`, CARD-0077); the fresh-launch path does not. The
  boot-wedge relaunch brief at `AgentTaskDispatcher.cs:6838` has the same defect.
- **D-2: liveness for a remote-bound session consults the local runner.** In
  `IsLiveOrUnknownAsync(Guid, runnerId)` with a non-local `runnerId`, the local
  `ListAsync` has no bearing on the answer, but its failure (deadline, cancel, HTTP)
  propagates. CARD-1362 D-3 rightly forbids treating a failed list as "gone". Here the
  failed list belongs to a different runner from the one being asked about.

There is no shared linked token across runners. The deadline is per call. The fault
is the unguarded OCE plus the needless cross-runner RPC.

## (3) Correct behaviour

- Remote-bound session (non-blank `RunnerId`, not `local`): answer from that runner's
  phone-home state only: `RemoteInventoryPending(runnerId)`, then that runner's cached live
  set, then its unknown set. Make no local RPC, so a desktop-runner outage cannot affect
  it. Nothing about "unknown" changes: the phone-home directory already reports a
  disconnected server2 as unknown, never gone.
- Local-bound session: unchanged. A failed list propagates (CARD-1362 D-3). Destructive
  callers keep failing closed.
- Post-commit brief probe, for any target, the target runner included: a failure whose
  token was not canceled is a transient transport failure. Log it and leave the persisted
  brief Pending for queued delivery. Never fail the task. That is what the existing comment
  says. If a session never comes up, the existing CARD-1149 absent-launch sweep and the
  boot-wedge relaunch handle it, not the dispatch tick. Before the commit, the target runner's
  unavailability is already a `RunnerUnavailable` hold from directory status, not from a List.
  Nothing there changes.

## (4) The same pattern elsewhere

Every per-session caller of `IsLiveOrUnknown(Async)` with a remote `RunnerId` makes
the local RPC first (D-2). The D-2 fix covers all of these:

| Path | Site | Effect during a desktop-runner restart |
|---|---|---|
| Fresh-launch brief probe | `SessionMessageQueueService.DispatchBrief.cs:169` via `AgentTaskDispatcher.cs:6399` | **Fatal**: fails the dispatch (this incident). |
| Boot-wedge relaunch brief | `AgentTaskDispatcher.cs:6836-6842` | Same OCE escape: the relaunch tick throws. |
| Pool release on task failure | `AgentTaskService.cs:4122` -> `PoolDelegateRelease.IsSessionTerminalAsync` | Only a Stopped/Failed remote row lists. The throw aborts the release inside the fail path. |
| Pool release sweep (stale delegates) | `AgentTaskDispatcher.cs:9531, 9619` | One Stopped/Failed remote row makes the sweep pass throw. It fails closed with no removal and retries next tick. |
| Release-kill verify / reply release | `AgentTaskDispatcher.cs:9299, 9770`; `AgentTaskReplyService.cs:2925` | Kill is guarded. The verify throws OCE out of the caller. |
| Specialist/agent delete | `AgentService.cs:1050, 1072` | The delete throws (409-shaped retry for the operator). |
| Queued delivery to remote sessions | `SessionMessageQueueService.cs:962, 2346, 5811` | Delivery to server2 sessions stalls until the desktop runner answers. It retries, but it is a cross-runner outage. |
| Resume / orchestrator / schedule | `AgentSessionService.cs:2156`, `OrchestratorService.cs:1084`, `ScheduleService.cs:277, 926` | Throws instead of answering from the server2 inventory. |

Set-wide callers (`ListLiveSessionsAsync`: watchdog `WatchdogService.cs:52`, usage
monitor, compaction, API-error recovery, policy refresh, queue `:1838/:5998`) mix runners
by design and fail closed on any list failure. A desktop restart skips their whole tick,
server2 included. That is a degradation, not a wrong result. Leave it out of this fix and
file it separately if wanted: a per-runner partial set with "local unknown" is a larger
CARD-1362 change.

I found no runtime liveness call in the land/Merge services
(`AgentTaskLandNotificationService`, land v2) or in `StandingSpecialistSeatService`. The
check seat's dispatch uses the same `DispatchOneAsync` and launch-brief path, so D-1 applies
to it. The worktree janitor (`WorktreeJanitorHostedService`) makes no runtime list call.

The OCE filter pattern itself, `when (ex is not OperationCanceledException)`, appears 31
times in `AgentTaskDispatcher.cs` and 276 times under `server/Application`. Most guard no
runner I/O. A sweep for the ones that do is a separate card. This fix changes only the two
brief sites.

## (5) Smallest fix and witnesses

Fix: about 15 lines of production code, two files.

- **D-1** `AgentTaskDispatcher.cs:6396-6413` and `:6830-6842`: change to
  `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } catch (Exception ex) { log }`.
  This copies `TryEnsureReuseBriefAsync`.
- **D-2** `AgentSessionRuntime.IsLiveOrUnknownAsync(Guid sessionId, string? runnerId, ct)`:
  when `runnerId` is non-blank, not `PhoneHomeProtocol.LocalRunnerId`, and `_directory`
  is not null, return
  `RemoteInventoryPending(runnerId) || _directory.LiveRemoteSessionIds().Contains(id) || _directory.UnknownRemoteSessionIds().Contains(id)`
  with no local `ListAsync`. Keep the local path as it is.

Witnesses: commit them red-first. Each uses an existing fixture.

- **W1 (D-2, unit, no DB)**, in `tests/Antiphon.Tests/Application/AgentSessionRuntimeBoundedReadTests.cs`
  (it already builds an `AgentSessionRuntime` with a fake client and a `FakeTimeProvider`):
  `C6fd1_Remote_bound_liveness_ignores_a_failing_local_list(string fault)` with
  `[Arguments("canceled")]` (`ListAsync` throws `TaskCanceledException`), `[Arguments("http")]`
  (`HttpRequestException`) and `[Arguments("deadline")]` (`ListAsync` blocks, then advance the clock
  past `RunnerLiveReadTimeoutMs`). The `ISessionRunnerDirectory` stub has
  `KnownRunnerIds = [local, "server2"]` and `LiveRemoteSessionIds = {S}`. Assert that
  `IsLiveOrUnknownAsync(new AgentSession{Id=S, RunnerId="server2", Status=Running})` is
  true and that the local `ListAsync` call count is 0. An absent remote S returns false and
  does not throw. Red at master, because the fault propagates.
  Control `C6fd1_Local_bound_liveness_still_propagates_a_list_failure` keeps CARD-1362 D-3:
  with `RunnerId` null or `local` the same faults throw. It is green at master and must stay green.
- **W2 (D-1, dispatcher + DB)**, next to `CapacityAdmissionLifecycleTests.Remote.cs`
  (`ReadyDirectory`/`IdleClient` world, `CapacityRecoveryTaskTests.CreateDispatcherProvider`):
  `C6fd1_Brief_probe_cancellation_does_not_fail_a_committed_dispatch(string target)` with
  `[Arguments("remote")]` (task `RunnerId = RemoteRunnerId`) and `[Arguments("local")]` (no
  `RunnerId`). In both, `IdleClient.ListAsync` throws `TaskCanceledException`. Tick once,
  then assert task `Dispatched` (not `Failed`), no `FailureReason`, exactly one Pending delegation
  brief row for the attempt, and one launch enqueued. Red at master for both cases: the
  task ends `Failed` with "Dispatch failed before a session existed: A task was canceled.".
  The `local` case can only go green through D-1, so it pins D-1 independently of D-2.
  Control: cancel the tick's own ct and assert the OCE propagates (shutdown unchanged).

Checkpoint cost on server2: one `Antiphon.Tests` build (about 6-10 min loaded) and one row
`/*/*/(AgentSessionRuntimeBoundedReadTests*)|(CapacityAdmissionLifecycleTests*)/(C6fd1_*)`
(7 cases, under 3 min). Also re-run the `C1362_*` and `C1593_*` neighbours (about 5 min) and
`AgentTaskReuseEnqueueTests` (CARD-0077 reuse guard). That is about 20-25 min wall in total.

Advisory, not part of the fix: the generic tick catch labels every post-commit throw as
"before a session existed". Once D-1 is fixed, this path no longer reaches it.

## (6) restart-apphost.ps1 health budget

- **Where it is defined:** `scripts/restart-apphost.ps1:63`, `[int]$TimeoutSec = 150`, documented at `:29`.
  It is a parameter (`-TimeoutSec`). The deadline starts after the kill and the launch
  (`:284`). The same window has to cover dev-aspire's restore, npm dependencies, Docker
  checks, the build-slot wait, the `dotnet build` (113 s here), DCP start, server startup and the
  `/health` plus `/api/version` probes.
- **What this restart needed:** the script started its wait at about 07:49:37 and the deadline
  was at 07:52:07. The server was verifiably live at 07:52:57-58, so launch to live took about
  201 s. Server-side phases from `GET /api/diagnostics/startup` (run 2aaa4a90, sha 919d8d42f):
  config 0.3 s, host-build 0.2 s, connect 0.2 s, db-create 0.3 s, **db-migrate 9.2 s**
  (5 migrations), seed 0.7 s, setup total 11.9 s, host-start 5.2 s. That is about 18 s
  in-process. The remaining ~70 s after the build is DCP, client and container start.
- **The "last 10 restarts" evidence does not exist on disk.** `restart-apphost.ps1:270`
  truncates `logs/apphost.log` on every restart (`'' | Set-Content $logFile`), so it only ever
  holds the latest launch's `build` LIVE_TIMING line. The script's own `health-wait`
  LIVE_TIMING goes to the caller's stdout, which is not persisted. dev-aspire's
  docker/restore/dependencies lines go to `logs/apphost-launcher-<tag>.out`, one file per
  launch, which are kept. A desktop task (out of scope here: nothing runs on the desktop)
  can collect the last 10 with
  `Get-ChildItem logs\apphost-launcher-*.out | Sort LastWriteTime | Select -Last 10 | Select-String LIVE_TIMING`.
  Only the latest `build` line survives.
- **Proposed budget:** a fixed default of **420 s**, about 2x the one measured cold launch to live
  (201 s), per `docs/test-performance-standards.md` (budget near 2x p95). Better is
  progress-aware waiting: keep 150 s as a *no-progress* window and extend while the launch is
  visibly progressing, up to a hard cap of 600 s. Re-derive the cap once the persisted
  timings below exist.
- **Telling "still starting" from "dead":** every 3 s poll already reads the log verdict
  and the launcher exit. Add these:
  1. *Progress* resets the no-progress window: `apphost.log` length or mtime grew (build
     output, `BUILD SLOT` wait lines, dotnet run output), the dashboard URL file appeared, a
     server PID is listening on 17202, or `/health` answers non-200 (the host is up and
     migrating). Also count the server Serilog file growing (migration and retention lines).
  2. *Dead* exits early, keeping today's exit codes: the launcher process exited (already
     `Get-AppHostLauncherFailure`), or the AppHost process is gone and no server PID exists, or
     `BuildFailed` / `DcpDependencyTimeout` (already handled). Also no progress signal for the
     whole no-progress window.
  3. When it does time out, print the last progress signal and its age. The operator
     then sees "build still running 160 s" rather than a bare timeout.
  4. Stop truncating evidence: rotate `apphost.log` to `apphost.prev.log` (or append a run
     separator) instead of `Set-Content ''`, and append the `health-wait` LIVE_TIMING row to
     `logs/apphost.log`. That gives future budgets a p95.
  Changing the script needs a PowerShell witness: a fake launcher whose log grows past
  150 s must reach exit 0, and a dead launcher must still exit 1 early. That belongs on its
  own card. It is separate from the D-1/D-2 fix.
