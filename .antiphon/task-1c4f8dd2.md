# Code 1c4f8dd2: CARD-1716 inbound wake worker bounded lock wait + heartbeat

Branch `feat/card-task-1c4f8dd2` (worktree `/work/worktrees/task-1c4f8dd2`, runner mirror server2), base master `e12c6b4d72f93696a168c6e19d23d59d78379c85`. Landing owner: this Code task 1c4f8dd2. No plan file (dispatched from Debug 59ab5d9a + card), so no `### Checkpoints` table: every build/run below was a direct `scripts/build-slot.ps1` lease, one lease per build and per run, native default output, `UseAppHost=false`.

## Commits

| SHA | What |
|---|---|
| `9f291e9263418f243a05c487479ff1f98f142730` | red witnesses (did not compile: missing `using`) |
| `19a56f66cd22588a0e2b99f8ea654e8b915f42bd` | red witnesses compile; **red SHA** |
| `97011c4258942ea64a1c3368aa3fb3df5ddce8f5` | fix + heartbeat + docs; **fix SHA = tested SHA** |

## Change

- `SessionMessageQueueService.FlushSessionAsync` / `FlushSessionReportingDeliveryAsync`: optional `TimeSpan? lockWait` (CARD-1705 shape; null = unchanged unbounded wait); bound expiry throws `SessionQueueBusyException`, nothing done.
- `ChannelBridgeService` wake worker (mirrors CARD-1705/1709): `ScanQueueLockWait` 1 s, per-pass `WakePass` busy set (concurrent; batches run 4-wide). Bounded at: the inline enqueue and its flush in `FlushLaneAsync` (only while the pass is open; a debounced flush after its pass keeps the unbounded wait and holds only its own lane), and the owned-row sweep (`FlushSessionAsync`, the likely production stall site). `SessionQueueBusyException` caught before the general catch: no alert, no incident, no failure; rows stay pending unchanged (`_buffered` released), the agent's remaining rows in the pass are skipped in order, member cursor resets so the next pass starts at the oldest row. `BusyRetryDelay` 5 s replaces the 10 s idle tick after a pass that skipped work.
- Pass summary: `DrainPendingAsync` now returns `ChannelWakeWorkerPass(CompletedAt, RowsSeen, RowsQueued, SessionsSkippedBusy, Elapsed)`, logged at Information when anything was queued or skipped (Debug otherwise), and published on the `ChannelInboundWakeSignal` singleton.
- Heartbeat: new `GET /api/channels/wake-worker` -> `{ lastPass }` (null until a pass completes). `/api/channels/consumer` left alone: it has an allowlisted-fields contract and deploy scripts depend on it.
- Stall alert: independent loop (1 min) in `ExecuteAsync`; when no pass completed for 5 min and a deliverable row accepted >= 5 min ago is unqueued, logs Warning and adds one Critical `AgentIncident` `ChannelReplyLost` / `ChannelWakeWorkerStalled` on that row's agent (the existing incident type of docs/telegram.md aged holds). Once per stall; the next completed pass re-arms. Constants, no setting, so it is ON (no setting was needed; say if it should be gated).
- Docs: docs/telegram.md (paragraph after aged holds), docs/antiphon-api.md (route).
- Not touched: dialog guard / CARD-1591, what is delivered, any csproj/props (land-graph inputs unchanged).

## Callers

| Symbol | Callers | Effect |
|---|---|---|
| `FlushSessionAsync(id, ct, lockWait = null)` | AgentSessionService :693 :1377 :2218, RemoteControlModalWatchService :130, GrokRulesRefreshService :176, tests | unchanged (null) |
| same | ChannelBridgeService sweep :341 | bounded 1 s, busy -> skip |
| same | ChannelBridgeService.FlushLaneAsync :732 | bounded only inside an open pass |
| `FlushSessionReportingDeliveryAsync(..., lockWait = null)` | AgentSessionService :1050 (resumed launch) | unchanged (null) |
| `EnqueueAsync(..., lockWait)` | FlushLaneAsync | bounded only inside an open pass; all other callers unchanged |
| `DrainPendingAsync` Task -> Task<ChannelWakeWorkerPass> | RunWakeWorkerAsync; ~60 test call sites | source-compatible |
| `ProcessAgentGroupAsync(..., pass = null)` | DrainPendingAsync (pass), ProcessInboundAsync/HandleInboundAsync direct (null, unchanged) | |
| debouncer `FlushAllAsync` (shutdown) | stored lane callbacks | bounded only if shutdown lands mid-pass; busy leaves durable rows pending for restart |

## Witnesses (`ChannelInboundRecoveryTests.C1716.cs`, partial of existing class: Integration/Slow lane, no new class)

- `C1716_HeldQueueLock_OtherSessionQueuedWithinOnePass(enqueue|sweep)`: A's lock held by the test, never released in the pass; B's row queued and receipt-confirmed within one pass (8 s bound); A unchanged; no incident/alert.
- `C1716_BusySession_KeepsOrderWhenLockFreesMidPass_AndRetriesNextPass`: A1 < B < A2; A's lock freed at A1's busy report (log hook); A2 not queued in that pass; summary 3 seen / 1 queued / 1 busy; `NextWakeDelay` = 5 s; next pass queues A1 then A2 (queue Sequence order), 2 queued, 0 busy.
- `C1716_StalledWakeWorker_RaisesOneChannelIncidentPerStall`: FakeTimeProvider; 5 min - 1 ms no; at 5 min one incident; again no; completed pass published on `LastPass` and re-arms; a new stall raises a second.

Red at `19a56f66`: 3/3 failed (TimeoutException: the pass hangs), stall test did not exist yet (API absent). Green at `97011c42`: 4/4.

## Mutants (server dll rebuilt and swapped into the tip test output; source restored with `git checkout --` after each)

| Mutant | Result |
|---|---|
| A: busy skip deleted (row loop + sweep), sweep busy catch disabled, stall once-guard deleted | killed: sweep variant (busy exception escapes pass), order test (`firstPass.RowsQueued`, A2 queued), stall test |
| B: FlushLaneAsync busy recorded as failure (catch disabled -> general catch + alert) | killed: enqueue variant (bridge alert count), order test |
| C: bound dropped (lockWait null in lane and sweep) | killed: 3/3 TimeoutException |
| D: retry dropped (busy rows stay in `_buffered`) | killed: order test (`secondPass.RowsQueued`) |
| E: `NextWakeDelay` always idle tick | killed: order test |

(A first attempt at B did not apply its edit and ran unmutated: 4/4 green; discarded, re-run as above.)

Known witness gap for Mutation: the `RunWakeWorkerAsync` use of `NextWakeDelay` (delay variable) and the `RunWakeWorkerStallCheckAsync` loop wiring are not exercised by a test; only the helper/check methods are.

## Verification at `97011c42` (build-slot runs)

One run, filter `/*/*/(ChannelBridge*)|(ChannelInbound*)|(SessionMessageQueue*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(AgentTaskLandNotificationRecoveryTests*)|(TaskSupervisionReceiptTests*)/*`, `--maximum-parallel-tests 4`: **529 total, 516 passed, 0 failed, 13 NotExecuted** (SessionMessageQueuePtyIntegrationTests 8 + SessionMessageQueueGrokPtyIntegrationTests 5, platform-gated). Per class: AgentTaskLandNotificationRecoveryTests 49 (holds C1705_*), ChannelBridgeTests 40, ChannelInboundDebouncerTests 7, ChannelInboundRecoveryTests 45 (incl. 4 C1716), SessionMessageQueue* 290 run, TaskSupervisionReceiptTests 38 (holds C1709_*), guard classes 47 (1+39+5+2). 9m11s. Slot waited=0s on every lease.

Not run (deferred to Review/Final, never passed):
- the 19-class CARD-1705 scanner set (~29 min; list in `.antiphon/task-bdaa665c.md` on origin/feat/card-task-bdaa665c): their source is unchanged; the only shared edit is an optional parameter whose default keeps the unbounded wait. Skipped for the 75-minute budget.
- the whole Unit lane (verification profile Final; ~3 h on server2 per earlier shard plans).

Other: `scripts/check-evidence-diff.ps1 -Repository . -BaseRef e12c6b4d7 -HeadRef HEAD`: commits=3 entries=0 violations=0. `git merge-tree --write-tree origin/master HEAD` clean at origin/master `d95d3f2cdce02fd95bf2145ecee979769cfc4c3c`. No csproj/props/targets changed (land-graph digest unaffected).

## Operational note

This fixes the stall class but does not revive the live server: the running server (919d8d42f) still needs the recovery in Debug 59ab5d9a's report (restart after this lands to rc). Adopted sessions still withhold delivery (CARD-1591).
