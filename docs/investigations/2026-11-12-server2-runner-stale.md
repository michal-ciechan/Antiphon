# 2026-11-12: server2 runner reported `stale`, dispatch held

Read-only investigation (task 7bfaea91, 2026-11-12 05:40-06:05Z). Nothing was restarted or changed.

## Summary

The AppHost server's phone-home lease for `server2` expired at about **04:18:28Z**, during a
desktop-wide starvation episode on the AppHost server (04:15-04:20Z). The runner was healthy, and
its WebSocket stayed connected. Heartbeats resumed and still arrive every 15 s. Lease recovery,
however, never completed: `PhoneHomeRecoveryPump` has not finished a catch-up for that connection
in the 90 minutes since. It has also logged no failure. `dispatchEligible` therefore stays `false`,
and the catalogue reports `unavailableReason: stale`.

The fault is inside the AppHost server process, not in the network, Caddy, TLS, DNS or the secret,
and not in the runner.

## 1. When eligibility was lost, and what changed

`capacityObservedAt` is not a stored timestamp. `SessionRunnerCatalogue.Row` stamps it with
`DateTimeOffset.UtcNow` only when the runner is dispatch-eligible, so it is null whenever the
runner is ineligible. The last eligible moment comes from the server log
(`C:\src\Antiphon\server\logs\antiphon-20261112_001.log`):

| UTC | Event |
|---|---|
| 2026-11-11 17:49:10-12 | The AppHost server restarted, and server2 registered and connected (epoch 1). `platformObservedAt` 17:49:12 is that registration. The same socket has been in use ever since (`reconnects 1`, `epoch 1`). |
| 03:31 / 03:49 | Last server2 launches: 053dff43 (Merge batch 185) and 06aa57e7. |
| 04:15-04:20 | **The desktop server was starved.** DB queries took 1-3 s; localhost:17204 runner calls took 3-4 s; capability probes hit their deadline; the watchdog and session-health ticks timed out; delegation sweeps were cancelled at their 60 s budget; card-file git commands timed out; at 04:18:20 there were Npgsql connection timeouts (`An error occurred using the connection to database 'antiphon'`). |
| 04:17:13 -> 04:18:13 | `Phone-home inventory refresh List for runner server2 epoch 1 failed (phone_home_request_timeout)`. Because a refresh only runs while the runner is eligible, server2 was still eligible at 04:17:13. |
| ~04:18:28 | Lease (90 s) expired, and `ReadSnapshot` flipped `DispatchEligible=false`. At 04:21:28 the runner alarm logged `Runner server2 unavailable for 3.0 min`. |
| 04:44-04:46 | Event ingest still worked: tasks 06aa57e7 and 053dff43 reported done, with transcript rows to 04:46:48. Merge batch 185 settled at 04:46:49. |
| 04:48:18 onward | Merge batch 186 (efe7e6f4) has been held every tick with `Phone-home runner has not completed recovery.` |

The cause of the starvation was not established. It coincides with the live rc qualification load
on the desktop.

## 2. Why the AppHost still says "stale"

**How capacity is obtained.** It is a phone-home push. The AppHost never polls the runner. The
runner POSTs `/api/session-runners/register` (secret header, through `PhoneHome__ServerOrigin`;
it arrives as `::1` via the local proxy hop). It then holds a WebSocket at
`/api/session-runners/server2/connect` and sends heartbeats every 15 s. The lease is 90 s.

Eligibility is `live.DispatchEligible && SocketOpen && !leaseExpired`
(`PhoneHomeRunnerDirectory.DescribeAsync`). A lease expiry clears `DispatchEligible`, and only
`PhoneHomeRecoveryPump.RunOneAsync` can set it again. It does so when `CanRecoverLease` passes, then
`CatchUpAsync` succeeds (an owner-inventory List plus a transcript fetch and persist for every
listed session), then `TryRecoverLease` runs.

**Which leg is broken: the server-side recovery pump.** Evidence, from repeated
`GET /api/session-runners/server2/status` reads at 05:46-05:48Z:

- `available=true`, `lastHeartbeatUtc` advances every 15 s, `disconnectReason null`,
  `pendingEvents 0`, `draining false`, `retiredAt null`, and `epoch 1` (no registration since
  03:53Z, so the slot epoch still matches). Every observable input to `CanRecoverLease` passes.
- `dispatchEligible=false`, and `lastCatchUpMs` is frozen at **54410**. No inventory List has
  completed since the flip. A List to a healthy runner would answer in milliseconds, and
  `lastCatchUpMs` would change.
- No `Phone-home catch-up List ... failed`, no `Phone-home recovery pump cycle failed`, and no
  `event pump ended` line after 04:18:13. The catch-up has neither succeeded nor failed: it is
  **blocked inside `CatchUpAsync`**.
- `RunCycleAsync` awaits every remote runner's `RunOneAsync` through `Task.WhenAll`. A wedged
  server2 cycle therefore stops recovery for every remote runner, including a fresh server2 boot or
  a new `server2-temp`.

There are two unbounded waits on that path. A stack sample is needed to tell which one is blocking:

1. **`PhoneHomeLiveConnection.RequestAsync`.** The 60 s timeout starts only after the frame is
   written. `_send.WaitAsync(ct)` and `WriteFrameAsync` are unbounded, and `ct` is the host
   stopping token. The event-window credit writer shares `_send`. Closing the socket would fault a
   stuck write and unblock it.
2. **`AgentSessionRuntime.PersistTranscriptAsync`**, which `CatchUpAsync` calls with no token.
   `SessionStateStore.BeginWriteAsync` waits on a per-session `entry.Gate.WaitAsync(ct)` with
   `ct = default`. Closing the socket would **not** unblock it; only a server restart would.

The heartbeats arriving with `lastCatchUpMs` frozen fit (1) slightly better. The 54 410 ms is most
likely the refresh that completed at 04:17:13. If so, the post-recovery List never finished. That
is not proven.

**Ruled out:**

- Reachability, Caddy, TLS and DNS: the runner's heartbeats arrive every 15 s on the same socket.
- Token or secret mismatch: no 401 or 403, and no re-registration was needed.
- The runner: the container is `Up 5 days (healthy)`, host load is 0.47, and no runner log lines
  appear after 03:49:15. That silence is expected, because the AppHost sends it no work.
- The database: `pg_stat_activity` showed no long or blocked transaction at 05:58Z.

## 3. The four "in flight" on server2

The pipeline's host `inFlight=4` counts **sessions**, not tasks. Only two tasks are open
fleet-wide: efe7e6f4 (Queued) and this one. All four sessions are real processes in the runner
container (read-only `docker top`):

| Session | DB status | Task (status) | Process | Notes |
|---|---|---|---|---|
| 138281ed | Stopping | 053dff43 Merge batch 185 (Succeeded 04:46:49) | claude, alive | The stop cannot be delivered: `KillGeneration` is refused while ineligible. |
| d41134dd | Stopping | 06aa57e7 rc sr g1 fix (Succeeded 04:44:51) | claude, alive | Same as above. |
| 36be1fa7 | Running | f4320f2f CARD-1820 S1 (Failed 2026-11-10 05:30) | grok, alive since 2026-11-10 | Orphan; warm-delegate retirement is deferred. |
| cd6fcc2a | Running | 0ddd898d CARD-1820 S1 round 4 (Failed 2026-11-10 18:16) | grok, alive since 2026-11-10 | Orphan, as above. |

None of the four carries live work. The container also holds leaked test processes: pty-hosts under
`/tmp/c1614-life-*` (about 2 days old), pty-hosts for sessions 7bc8b3ff and a8afb9da, and a
`channel-outbound-probe` from `task-9d9adb37`.

## 4. Recommended recovery

**Waiting will not fix this.** The connection has been healthy for 90 minutes and recovery has not
completed.

1. **Now, within standing autonomy:** run Merge batch 186 on the desktop. Cancel efe7e6f4 and
   re-dispatch it with no server2 binding. The desktop is eligible, with capacity 2 and 1 occupied.
   Hold all other server2 dispatch.
2. **Confirm the hang point, read-only:** run `dotnet-stack report -p <Antiphon.Server pid>` on the
   desktop (`dotnet-stack` 9.0 is installed). Look for `PhoneHomeRecoveryPump.CatchUpAsync` and
   what it awaits. This attaches EventPipe to the live server, so it is the orchestrator's or
   operator's call; this task did not run it because the brief forbade touching the AppHost.
3. **If the stack shows `RequestAsync`** (`_send.WaitAsync`, `WriteFrameAsync` or the reply
   wait), restart **only** the server2 runner container (compose project `antiphon-runner`). The
   socket close makes the server dispose the connection, which faults the stuck request. The new
   boot then registers after the old lease and gets a fresh catch-up.
   - **Disturbed:** the four sessions above. All belong to Succeeded or Failed tasks, so no live
     work is lost. The leaked test pty-hosts also go.
   - **Risk:** low.
   - **Authority:** this is **not** within orchestrator autonomy. docs/orchestration-loop.md says:
     "Restart the standing server2 runner only through those rolling phases". The operator must
     approve it. A rolling rollout would not help either, because the wedged pump also blocks
     `server2-temp` from becoming eligible.
4. **If the stack shows `SessionStateStore.AcquireAsync` / `BeginWriteAsync`**, only a server
   process restart (`scripts/restart-apphost.ps1`) clears it.
   - That restart is within orchestrator autonomy, but it is blocked while rc run ae84478d needs the
     desktop stack.
   - Keep server2 work on the desktop until the run ends, then restart the AppHost.
   - A runner restart alone would kill the four sessions and **not** restore eligibility.
5. **Without a stack sample**, the lowest-risk order is step 1 now, then step 3 with operator
   approval. If eligibility does not return within about 3 minutes of the new boot connecting, fall
   back to step 4 after the rc run.

## Defects to file

- **Recovery pump.** Lease recovery can wedge silently, and one wedged runner blocks recovery for
  every remote runner. Fix: bound `CatchUpAsync` with a deadline, which also covers the send gate
  and the session-state gate, so that failure logs and retries. Run each runner's cycle
  independently instead of `Task.WhenAll` per cycle. Expose `inFlight`, `waiters` and
  `catchUpStartedAt` on `/status`.
- **Stops while ineligible.** `KillGeneration` is refused while the runner is ineligible, so
  finished sessions stay `Stopping` indefinitely and count as host occupancy.
- **Orphan sessions.** Two Grok sessions of tasks that failed on 2026-11-10 are still `Running` on
  server2.
- **Suspected test isolation leak (unconfirmed).** There were bursts of
  `409 A competing boot cannot replace an unexpired owner` on `/api/session-runners/register` at
  01:16-01:18, 03:23-03:24 and 03:50-03:52Z: 76 attempts that authenticated with **server2's**
  secret through the same proxy hop.
  - The server2 runner itself logged no registration attempts in those windows.
  - The windows line up with Merge and Debug tasks running test suites inside the server2 container
    (Merge 183 ended 01:22, Merge 184 ended 03:29, and 06aa57e7 started 03:49). Leaked test
    pty-hosts are present.
  - Hypothesis: a test that boots the real SessionRunner inherits the container's
    `PhoneHome__RunnerId=server2`, origin, secret path and store-id path, and phones home to
    production.
  - After a lease expiry like the one at 04:18, such a test could take over the server2 identity,
    because its store id would match.
