# CARD-1359: C540 child-host dispatch launch stalls ~60 s before warning materialization

Investigate task 7e43a724, 2026-10-19. Start ref 066f4a98916233c1020bd0dc3bcf03b416b46cae
(CARD-1305 fixtures S0-S3 applied). Subject: `C540_PreEnqueueCrashRecoversWarnings`
(`tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs:83`, via `CrashAsync` at `:112-130`).

## Outcome in one paragraph

The red is reconstructed from the stored Code-task evidence: the test's `before-enqueue` wait
is a 60 s quiet window renewed only by `protocol-git-*.json` observations, and the boundary it
waits for sits behind the whole dispatch launch path (intents materialize only after the launch
spec is composed and the session is enqueued). In the failed run the child host took 53 s from
"worktree created" to "launching", during which the child host emitted no log line of its own
while every one of its runner reads degraded (8 s, 8.5 s and 16 s cancellations, watchdog
`ListAsync` aborted). The dispatcher's own code between those two log lines makes no runner
call. The failed launch-side delay was not reproduced: two isolated runs of the single method at
the same SHA passed with a 1.0-1.1 s worktree-to-launch gap. What slowed the runner reads is
not established; the strongest candidate mechanism for the dispatcher being dragged along is
the server's sync-over-async runner reads (`AgentSessionRuntime.ListLocalLiveSessions`,
`TryGetLiveSnapshot`, ...) blocking thread-pool threads with no deadline on an 8-core host.
Verdict: the test red is a fixture wait-shape defect (dispatch-side progress never renews the
quiet window); the launch-side latency exposes a product hazard that is identified but not
proven to be the trigger.

## Evidence used

| Item | Path |
|---|---|
| Failed run (Code 1f4aaf07, CP-7) | `C:\Antiphon\worktrees\card-task-1f4aaf07\.antiphon\task-1f4aaf07\cp7-dispatch-warnings\` (`run.json`: sourceSha 066f4a98, exit 2, 1 passed / 1 failed, 210.5 s) |
| Failed run fixture root | `C:\Antiphon\worktrees\card-task-1f4aaf07\.antiphon\acceptance\card-0467\9e896927af244985b5c567bc9dc92e09\` (`child-logs\antiphon-20261019.log`, `delivery-evidence.json`, `wait-42a1ff91...json`, `loaded-runner.json`, `identities.json`) |
| Passing no-child run, same CP-7 process | `...\acceptance\card-0467\d2de1ec214c2444ca31aedcc92e7b6b7\server-logs\antiphon-20261019.log` |
| Earlier same-signature red (Investigate 463014fb) | `C:\src\Antiphon\.antiphon\investigations\e2e-land-contract-463014fb\dispatch-preenqueue\fixture\wait-860fcf68...json` ("quiet 60s after 66.4s; progress 46") |
| Reproductions (this task) | `C:\Antiphon\worktrees\card-task-7e43a724\.antiphon\task-7e43a724\preenqueue-1\` and `preenqueue-2\` (`run.json`, `run.trx`, `stdout.log`, `samples.csv`, `runner-mirror\`) |

Log timestamps below are UTC (the Serilog files are written in +01:00; one hour subtracted).

## Timeline of the failed run (child host pid 301488 on :51276, runner pid 128004 on :51126)

| UTC | Source | Event |
|---|---|---|
| 18:36:50.9 | parent server log last line | Parent land host suspended (`UseChildAsync` -> `SuspendLandHostAsync`, `LandDeliveryFixture.cs:264`) |
| 18:37:05.9 | child log | Child host `Now listening` |
| 18:37:11.6 | derived (`wait-*.json` elapsed 83.1 s) | Test enters `WaitForBoundaryAsync("before-enqueue")` (`DispatchBaseWarningDeliveryE2ETests.cs:129`), `protocolGitBefore: 1` |
| 18:37:13.6 / 18:37:22.8 | child log | `Capacity wait registered` / `Capacity recovery grant` for the task (kind Grok). Present in every passing run too (e.g. d2de1ec2: registered 18:36:05.3, dispatched 18:36:07.7, granted 18:36:15.4); not on the critical path |
| 18:37:28.3 - 18:37:34.7 | `protocol-git-*.json` | Base-warning scan `merge-base --is-ancestor` calls (37 observation files; last at 18:37:34.7). This is the last progress the quiet window ever sees |
| 18:37:31.1 | `delivery-evidence.json` | `now` of the dispatch tick: both intents, both notifications and the four task events carry `CreatedAt`/`At` 18:37:31.091569 |
| 18:37:41.4 | child log | `Task 10e99cae: worktree at ... (base master, sha 47850433...)` (`DelegationWorktreeService.cs:459`, last statement of `CreateForTaskAsync`) |
| 18:37:41.4 - 18:38:26.0 | child log | No non-HTTP line from the child host at all (verified: only HTTP client lines in that window) |
| 18:37:44 - 18:37:59 | child log | Runner reads still answer: `/capabilities` 89 ms (18:37:44.4), 2.6 ms (18:37:49.3); `/sessions` 16-33 ms; snapshot 870 ms (18:37:43), buffer/host-stats 150-650 ms |
| 18:37:59.5 -> 18:38:07.6 | child log | `GET /capabilities` cancelled: `Resilience outcome runner.get-capabilities ... canceled reason TaskCanceledException elapsed 8127` |
| 18:38:07.6 -> 18:38:16.1 | child log | `GET /sessions` cancelled after 8483 ms (`runner.list`); the parallel `GET /host-stats` answered after 8485 ms |
| 18:38:11.2 -> 18:38:27.4 | child log | `GET /capabilities` cancelled after 16195 ms |
| 18:38:26.05 | child log | `Watchdog scan failed`: `TaskCanceledException` <- `IOException` <- `SocketException (995) The I/O operation has been aborted because of either a thread exit or an application request`, thrown from `SessionRunnerHttpClient.ListAsync` <- `AgentSessionRuntime.ListLocalLiveSessions` (`AgentSessionRuntime.cs:1690`) <- `WatchdogService.ScanAsync` (`WatchdogService.cs:52`) <- `WatchdogHostedService.ExecuteAsync` (`WatchdogHostedService.cs:39`, logged at `:47`). Error 995 is the client's own cancellation aborting its socket read, not a runner-side reset |
| 18:38:28.8 | child log | Runner reads recover: `/capabilities` 1387 ms, `/sessions` 1255 ms, `/sessions` 2739 ms, then 2-50 ms from 18:38:28.9 on |
| 18:38:34.13 | child log | `Task 10e99cae (Worker/Code): launching with instruction bundles ...` (`AgentTaskDispatcher.cs:7091`) |
| 18:38:34.31 | child log | `Dispatched task 10e99cae ... to session f5573c19` (`AgentTaskDispatcher.cs:6151`); `POST /sessions` at 18:38:34.88 |
| 18:38:34.51 / 18:38:34.62 | `delivery-evidence.json` | Intents `MaterializedAt` (`AgentTaskDispatcher.cs:6165`, after the Dispatched log) |
| ~18:38:34.7 | derived | Quiet window expires: last progress 18:37:34.7 + 60 s. `wait-*.json`: `before-enqueue`, `timeout`, elapsed 83.15 s, protocolGit 1 -> 37, `renewed: true`. No `before-enqueue.barrier.json` exists in that root (the boundary at `AgentTaskLandNotificationService.cs:133` is reached only by the enqueue that follows materialization) |

Gap accounting: intents stamped 18:37:31.1 -> worktree 18:37:41.4 (10.3 s, includes the git
base scan and `git worktree add`) -> launching 18:38:34.1 (52.7 s) -> materialized 18:38:34.6.
Total 63.5 s from the dispatch tick's `now` to materialization. The passing no-child run in
the same CP-7 process took 1.9 s from worktree (18:36:05.8) to launching (18:36:07.7).

## What the dispatcher does between "worktree at" and "launching" (code order)

`server/Application/Services/AgentTaskDispatcher.cs`:

1. `:5766` `_worktrees.CreateForTaskAsync` returns (its last line is the `worktree at` log).
2. `:5778-5779` deny-hook arming (file write, Code role only when configured).
3. `:5797-5812` `CaptureProgressBaselineAsync` (`:7613-7660`): git `rev-parse HEAD` in the
   worktree, `ObserveExactRefAsync` against origin (a local bare repo), two `PinBaselineAsync`
   ref writes. These git calls are not instrumented by `protocol-git` (observations stop at
   18:37:34.7, before the worktree exists), so their duration in the failed run is unknown.
4. `:5836` `ResolveAgentAsync` (DB), `:5838` phone-home refusal gate (not runner-bound here).
5. `:6035-6040` `BuildDispatchWarningDrafts` + `_dispatchWarnings.CaptureAsync` (DB inserts).
6. `:6058-6059` `SaveChangesAsync` + `transaction.CommitAsync` (the claim commit).
7. `:6081` land boundary `dispatch-warning-claim-committed`; `:6087` bundle attachments (DB).
8. `:6088` `FinishDispatchedLaunchSpecAsync` -> `:6924` `BuildLaunchSpecAsync` ->
   `ComposeDelegateArgs` logs `launching with instruction bundles` at `:7091`.
9. `:6096-6155` absence-evidence prep, launch enqueue, brief enqueue, `Dispatched` log.
10. `:6157-6174` **only now** `DispatchBaseWarningIntentService.MaterializeAsync` per captured
    intent. The hosted scan retries it later if this throws.

None of steps 1-8 calls the session runner. `GetCapabilitiesAsync` consumers in the server
(`grep` over `server/`) are the start path (`SessionRunnerHttpClient.StartAsync`, `:154-158`),
delivery profiles, Grok rules refresh, the phone-home directory, diagnostics and the
reconciliation census, none of which sit between steps 1 and 8. The three cancelled
`/capabilities` reads therefore belong to hosted services, not to the dispatch tick.

## Why the quiet window could not survive this

- `LandDeliveryFixture.UntilProtocolAsync` (`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:363-377`)
  passes `ProtocolProgress()` (`:361`, count of `protocol-git-*.json`) as the only progress probe
  to `ProgressAwareWait.UntilAsync` (`tests/Shared/ProgressAwareWait.cs:19-40`): the 60 s deadline
  renews only when that count changes.
- `protocol-git` observations are written by the land-delivery git hook
  (`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs:215`) for the base-warning scan's git
  calls. Worktree creation, baseline capture, the claim commit, launch-spec composition, the
  launch enqueue, materialization and the notification enqueue produce none.
- So once the scan finishes (18:37:34.7) the fixture has exactly 60 s for the entire remainder
  of dispatch plus the hosted notification scan's next enqueue pass, while the
  `before-enqueue` boundary (`server/Application/Services/AgentTaskLandNotificationService.cs:133`,
  cut `pre-enqueue` at `LandDeliveryOptions.cs:164`) is the last step of that chain.
- The same wait shape timed out at 66.4 s in Investigate 463014fb and at 83.1 s here. Both
  passing reproductions satisfied it in 10.1 s and 11.2 s.

## What degraded the runner reads: not established

Facts that bound the candidates:

- Every runner read from the child host between 18:37:59.5 and 18:38:28.8 either exceeded 8 s
  or was cancelled; before and after, the same endpoints answered in 2-90 ms. Recovery was
  gradual (8.1 s, 8.5 s, 16.2 s cancellations, then 1.3-2.7 s, then milliseconds).
- The runner's own log (mirrored in both reproductions at
  `.antiphon/task-7e43a724/preenqueue-*/runner-mirror/run-*/session-runner-20261019.log`)
  records only the startup `/health` request; ASP.NET request logging is filtered, so
  runner-side service time cannot be read even when the run directory survives. The failed
  run's runner directory was deleted at teardown (`AntiphonAppFixture.cs:497`).
- The parent test process made no runner calls after 18:36:50 (its OpenTelemetry activities in
  `stdout.log` end there), so there is no third observer of the runner in the window.
- The census at 18:37:18 reported 14 live `Antiphon.PtyHost` and 14 live `claude` processes on
  the host (the operator's production sessions; still 14/13 at 19:06 today). Reproduction 1's
  build slot waited 90 s on `memory_floor available=4613MB floor=6144MB`; `samples.csv` shows
  total CPU at 50-100 % through both passing runs. Host load is therefore present but did not
  reproduce the stall on two attempts.
- The 8 s / 16 s cancellations are not fixed deadlines in the code path: the admitted-read
  pipeline uses `AttemptTimeoutSeconds` 10 (`src/Antiphon.Resilience/ResilienceSettings.cs:14`)
  and a 100 s request budget (`server/Application/Settings/SessionRunnerSettings.cs:18`), and the
  outcome logged is `canceled` (external token), not `timeout`. No server caller with an 8 s or
  16 s `CancelAfter` on `GET /capabilities` was found (`grep FromSeconds(8|16)`); the only 8 s
  deadline (`SessionRunnerHttpClient.cs:373`) is on a POST. Which callers' tokens fired, and
  why at those durations, is unresolved.

Candidate mechanism for the dispatcher being stalled by runner slowness (identified, not proven):

- `server/Application/Services/AgentSessionRuntime.cs:1689-1696` `ListLocalLiveSessions()` is
  `_runnerClient.ListAsync(CancellationToken.None).GetAwaiter().GetResult()`; `:1792-1793`
  `TryGetLiveSnapshot` blocks the same way on `GetSnapshotAsync` + `GetBufferAsync`; `:1825`
  `GetAsync`; `:1673` and `:1998` `GetBufferAsync`. All block a thread-pool thread for the whole
  runner round trip with no deadline of their own.
- Callers (`grep`): `WatchdogService.cs:52,68,80` every `ScanIntervalMs` (default 10 s,
  `WatchdogSettings.cs:6`), `SessionMessageQueueService.cs` (about 20 sites inside its
  `PollIntervalMs` loops), `BootReplyWatchdogService.cs:424`, `ContextCompactionService.cs:188`,
  `PolicyRefreshService.cs:483`, `ApiErrorRecoveryService.cs:284`,
  `RemoteControlRecoveryService.cs:625,631`, `AgentChannelService.cs:203`.
- The desktop has 8 logical processors and nothing calls `ThreadPool.SetMinThreads` (`grep`
  over `server/`, `src/Antiphon.SessionRunner/`), so the worker pool starts at 8 threads and
  grows at roughly one or two threads per second once all are blocked. A runner that answers
  slowly for a few seconds can pin those 8 threads behind the pollers above; every async
  continuation in the process (EF commits, git `Process` exit callbacks, `HttpClient` response
  headers) then waits for thread injection. That fits the observed shape: a silent dispatcher,
  client-side "received after 8485 ms" on a 20 ms endpoint, cancellations from tokens that
  fired while their continuations were queued, and gradual recovery. It is one mechanism
  consistent with the evidence, not a reconstruction of what happened; the runner's own
  service time in that window is unknown.

## Reproduction (single method, through the build slot)

Built `tests/Antiphon.E2E` to `bin-c1359/` at 066f4a98 (64 s, 0 errors). Each run:
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1359-preenqueue-N -SlotWaitMinutes 5 -- pwsh -NoProfile -File <scratch>\run-c540-preenqueue.ps1 -Case preenqueue-N`,
which executes `dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-c1359/ -- --treenode-filter "/*/*/DispatchBaseWarningDeliveryE2ETests/C540_PreEnqueueCrashRecoversWarnings" --no-progress --no-ansi --report-trx ...`
with the `tests/test-execution-policy.json` safe environment, `PhoneHome__Enabled=false`, a
420 s cap, the runner run directory mirrored every 1.5 s and host/process samples recorded.

| Run | Slot wait | Result | worktree -> launching (UTC) | intents created -> materialized | before-enqueue wait |
|---|---|---|---|---|---|
| preenqueue-1 | 90 s (memory floor) | 1 passed, 0 failed, 1 m 44.6 s | 19:03:14.4 -> 19:03:15.4 (1.0 s) | 19:03:13.24 -> 19:03:15.53 / .57 | satisfied, 10.07 s |
| preenqueue-2 | 0 s | 1 passed, 0 failed, 1 m 29.0 s | 19:07:06.4 -> 19:07:07.5 (1.1 s) | 19:07:05.15 -> 19:07:07.62 / .67 | satisfied, 11.23 s |

No `Resilience outcome` or `Watchdog scan failed` line in either child log; the slowest runner
read in run 1 was one `/sessions` at 553 ms. No other test runs were made. The `bin-c1359`
directories were removed after the runs.

## Verdict

- The red's mechanism is confirmed from stored evidence: the fixture's quiet window is renewed
  only by base-scan git observations, and the awaited boundary is behind the full launch path
  (materialization is sequenced after launch at `AgentTaskDispatcher.cs:6157-6166`), so 60 s of
  launch-side latency with no git activity fails the test even though delivery completes.
  That is a fixture wait-shape defect, not a product regression in warning delivery.
- The launch-side latency itself (53 s) is a product-side effect of degraded runner reads in
  the child host; the degradation's trigger was not reproduced and the runner leaves no
  per-request timing. The sync-over-async runner reads in `AgentSessionRuntime` are a real
  product hazard that can turn runner slowness into a server-wide stall, but they are not
  shown to be what happened on 2026-10-19 18:38Z.
- Widening the 60 s window is not the fix and was not attempted.

Remaining uncertainties: which hosted callers' tokens cancelled the 8.1 s / 16.2 s capability
reads; whether the runner process or the child host's thread pool was the slow party; whether
the other `CrashAsync` cuts that share this wait shape fail under the same conditions (the
card's claim; not measured here).

## Not done, noted

- Fix idea (fixture, smallest): make the dispatch boundary waits renew on dispatch-side
  progress as well as `protocol-git` (for example the task's `AgentTaskEvents` count, its
  session row, or intents' `MaterializedAt`), leaving the 60 s quiet window and the
  `before-enqueue` cut untouched.
- Fix idea (product, separate card): give `AgentSessionRuntime`'s blocking runner reads a
  deadline or make them async, and enable runner request timing so a runner stall can be
  attributed next time.

--- next stage ---
next: plan
handoff: Plan the C540 fixture repair: renew the before-enqueue (and sibling CrashAsync) quiet windows on dispatch-side progress markers instead of protocol-git only, keeping the 60 s window; file a separate product card for AgentSessionRuntime's deadline-less sync-over-async runner reads (ListLocalLiveSessions/TryGetLiveSnapshot) and missing runner request timing.
artifact: docs/investigations/2026-10-19-card-1359-c540-child-launch-stall.md
