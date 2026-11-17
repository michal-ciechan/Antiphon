# 2026-11-17 server2 phone-home runner stuck "has not completed recovery"

Read-only Debug e5d7e9da, 2026-11-17 03:23-03:50Z. Nothing was restarted, killed or changed.
Server under diagnosis: desktop server `870a93d21f2b18c85a8b0905ab689b28e3099813` (`GET /api/version`,
rc/2026-11-24 activation at ~00:29Z). All citations below are to that SHA (the phone-home files are
identical on master `c3cc0bb9b`). Log: `C:\src\Antiphon\server\logs\antiphon-20261117.log`.

## Verdict

1. **What took server2 out (02:49-02:51Z): a lease expiry on a live socket, during desktop overload.**
   `DispatchEligible` is cleared in exactly two places: `Disconnect` (`PhoneHomeRunnerDirectory.cs:487`, which
   also nulls `slot.Live`, and that did not happen: epoch 1 is still live, `reconnects=1`) and the
   lease check in `ReadSnapshot` (`PhoneHomeRunnerDirectory.cs:984-994`: no heartbeat processed for
   `LeaseSeconds`=90, `PhoneHomeContracts.cs:21`). So by elimination the epoch-1 lease expired at about
   02:50:47Z (`RunnerAlarmCoordinator` "Runner server2 unavailable for 3.0 min" at 02:53:47). The desktop
   server was overloaded at that moment: the desktop runner probe on localhost timed out (02:48:43, 02:50:50),
   DB queries took about 2 s (`LIVE_SLOW_CALL db-query 1947 ms` 02:48:59), the delivery watchdog was cancelled at its
   60 s budget (02:48:57, 02:50:22), the seat sampler's List timed out (02:48:14, 02:49:10, 02:50:10) and
   three recovery-pump inventory refreshes timed out (02:49:03, 02:50:04, 02:51:05). Event throughput from
   server2 fell from about 70-100/s to about 10/s (862,025 events at 02:49:16 to 863,279 at 02:51:15). The
   same desktop-load pattern as CARD-1523, but this time with **no overflow and no disconnect**.
2. **Why it never came back: lease re-recovery has not run a List for 35+ minutes, and it fails silently.**
   Live status now (03:25-03:28Z, `GET /api/session-runners/server2/status`, sampled 15 times): `available=true`,
   heartbeat fresh (about every 15 s), `pendingEvents` 0 (2 once), `disconnectReason` null, `epoch` 1, but
   `dispatchEligible=false` and **`lastCatchUpMs` frozen at 30**. Every successful List sets that value
   (`PhoneHomeRecoveryPump.cs:290`). A failed catch-up List logs a Warning (`:363-366`) and a failed
   cycle logs "recovery pump cycle failed" (`:73`). **Neither line has appeared since 02:51.** With every
   `CanRecoverLease` input currently true, the pump is almost certainly **parked inside an un-timed await in its
   serial loop**, not retrying. Candidates, in order of likelihood:
   - `CatchUpAsync` → `runtime.PersistTranscriptAsync` (`PhoneHomeRecoveryPump.cs:388`) →
     `SessionStateStore.BeginWriteAsync` → `entry.Gate.WaitAsync(ct)` (`SessionStateStore.cs:83-117`), a
     per-session gate whose only token is host shutdown. This fits `lastCatchUpMs=30` if a catch-up
     List did succeed and the per-session transcript step then hung.
   - `RequestAsync` → `_send.WaitAsync(ct)` (`PhoneHomeLiveConnection.cs:410`) is outside the 60 s reply
     timeout (`:434-444`). This is less likely: the credit writer shares the gate (`:694`), and events are
     still flowing.
   - The other explanation is that the loop is alive but `CanRecoverLease` (`PhoneHomeRunnerDirectory.cs:432-442`,
     `PendingEvents == 0` exactly) or `TryRecoverLease` (`:444-461`) keeps refusing with no log line.
     The 0-pending samples make this unlikely as a permanent state, and it would still run Lists, which
     would move `lastCatchUpMs`.
   The two cases cannot be told apart without a managed async dump (see UNOBSERVED). Either way, **nothing
   in the code times it out**. `ExecuteAsync` awaits `RunCycleAsync` serially (`PhoneHomeRecoveryPump.cs:61-77`),
   so a hang there also stops recovery of **every** phone-home connection, including a fresh epoch.
3. **A session output flood is not the cause.** Observer queue is drained: pending SessionOutput
   at the slow-observer lines was 40, 724, 1, 9, and `pendingEvents` 0 now. Lifetime volume for epoch 1 was
   928,997 SessionOutput events, 260.6 MB, 2h38m, an average of about 98 events/s and about 280 B each:
   continuous TUI redraw output from about 6-10 concurrent sessions, mostly Grok. It is a steady load that adds
   to desktop DB pressure (output.activity phase max 3.0 s, output.publish 3.5 s), not a burst.
4. **Side effects observed:** release kills at 03:04:14 (task d7c12587, session 04c4b7d0) and 03:07:03
   (task cad48ede, session 21f15394) got `StopFailed` because `Resolve` threw "has not completed recovery"
   (`PhoneHomeRunnerDirectory.cs:163-164`). Both delegates were "kept Idle for the janitor". The janitor uses
   the same `Resolve`, so those two seats stay occupied on server2 until eligibility returns.
5. **Known card?** It is related to **CARD-1523** (Done: "Phone-home serial event consumer overflows during
   desktop load and flaps server2 eligibility"). That card covers overflow, disconnect and reconnect. This
   incident is a different failure: the **lease-expiry re-recovery path on a live connection** is stuck with
   no timeout and no log. No board hit for "phone-home recovery", "has not completed recovery",
   "SessionOutput flood" or "lease recovery" (`card.ps1 search ... -Board Antiphon -All`, 0 hits each).
   **A new card is warranted.**

## Timeline (UTC, 2026-11-17)

| Time | Event | Source |
|---|---|---|
| 00:25 | server2 epoch 2 (previous server process) ended `request_aborted` (AppHost restart) | log |
| 00:29:13 | server2 epoch 1 accepted, capacity 10, linux (`platformObservedAt`) | log, status |
| 00:32-02:24 | 13 slow-observer warnings, each about 1 s; queue always drains | log |
| 01:27-02:xx | server2 dispatches succeed | caller |
| 02:48:14 | Seat occupancy List to server2 timed out; desktop runner localhost probe slow | log |
| 02:48:43-02:49:00 | Desktop runner capability probe fails 3x (deadline) | log |
| 02:48:57 | Delivery watchdog sweep cancelled at 60 s budget (ERR); DB queries about 2 s | log |
| 02:49:03 | Slow observer w2 session 26dc769a (task 77238f98) output.activity 1169 ms; **refresh List #1 timeout (60 s)** | log |
| 02:49:16 | Slow observer w1 session 04c4b7d0 (task d7c12587) 1217 ms; pending 724 | log |
| ~02:49:17 | Last heartbeat processed before the gap (inferred: lease 90 s before ~02:50:47) | inferred |
| 02:50:04 | Refresh List #2 timeout | log |
| 02:50:22 | Delivery watchdog cancelled again (ERR) | log |
| **~02:50:47** | **Lease expired, so `DispatchEligible=false`** (alarm at 02:53:47 says down 3.0 min) | log + code |
| 02:51:05 | Refresh List #3 timeout; "failed 3 times in a row ... no longer count as live" | log |
| 02:51:15 | Slow observer w3 04c4b7d0 1023 ms; pending 1 (drained). **Last pump List-related line** | log |
| 02:53:47 | RunnerAlarmCoordinator: server2 unavailable 3.0 min | log |
| 03:00:02 | Worktree retention skipped server2: runner_not_eligible | log |
| 03:04:14 | Release kill of d7c12587/04c4b7d0 StopFailed ("has not completed recovery"), kept Idle | log |
| 03:05:30 | Task 03afb6f5 held RunnerUnavailable (first new server2 task) | log |
| 03:07:03 | Release kill of cad48ede/21f15394 StopFailed, kept Idle | log |
| 03:07:08 | Slow observer w0 session 861fbea8 (= task **32f24400**, Debug/opus) transcript.persist 1030 ms; pending 9. Last slow-observer line | log |
| 03:09:01 | Task 3871f961 held RunnerUnavailable | log |
| 03:20:32 / 03:23:59 | Holds aged to ERR (901 s / 903 s) | log |
| 03:25-03:28 | Status: available, heartbeat fresh, pending 0, eligible false, lastCatchUpMs 30 unchanged | live |

Not found anywhere since 02:51:05: "catch-up List ... failed", "recovery pump cycle failed",
"observer teardown", "event pump ended", "succeeded after N failed refreshes", any server2 disconnect or
reconnect, or any new registration (`platformObservedAt` still 00:29:13, so `slot.Epoch` was not bumped).

## Producer ranking (epoch 1, 00:29-03:07Z)

Totals (03:07:08 diagnostics): SessionOutput 928,997 events / 260.6 MB received, 928,988 released;
SessionTranscript 4,085 / 5.9 MB; SessionExited 23. Windowed rates of SessionOutput: 02:24-02:49 about 84/s;
02:49:16-02:51:15 about 10/s (the stall); 02:51-03:07 about 69/s.

The diagnostics `sessions` array holds only the first 16 sessions seen on the connection, not the top 16.
Everything later lands in `otherSessionEvents`: 447,855 events / 130.0 MB, which is all of the growth after 02:51.
The current producers therefore **cannot be ranked from this log**. Among the tracked 16, by bytes:

| Session | Task (role, model) | Events | MB | B/event |
|---|---|---|---|---|
| 23088239 | c6c85b5b (Code, grok-4.7) | 93,938 | 27.8 | 296 |
| 6992459e | e7394f29 (TestDesign, grok-4.7) | 45,351 | 16.0 | 352 |
| 471f79b4 | e2208352 (Code, gpt-6.1-sol) | 98,997 | 14.6 | 148 |
| 6872dd37 | 5f0633e8 (Review, grok-4.7) | 37,359 | 12.8 | 342 |
| e666e71a | c1c28227 (Investigate, grok-4.7) | 34,253 | 11.9 | 349 |
| 16d815b8 | c913e8aa (Review, grok-4.7) | 31,711 | 10.9 | 344 |
| 2f7efa65 | c3619b22 (Review, grok-4.7) | 30,878 | 10.8 | 350 |
| 59cdb40b | d0ae093a (Merge, grok-4.7) | 28,593 | 8.8 | 306 |
| c5ad4fcb | 82a03c61 (Review, grok-4.7) | 25,786 | 8.1 | 315 |

Sessions named in the slow-observer lines are 04c4b7d0 (d7c12587, Code, grok-4.7), 26dc769a (77238f98,
Merge, grok-4.7), 21f15394 (cad48ede, Review, opus) and 861fbea8 (32f24400, Debug, opus). That is a
uniform output.activity cost of about 1 s under desktop load, not one outlier session. Grok TUI sessions
average about 300-350 B per event, at a rate of about 10/s each over a session's life. No single session floods.

## Eligibility rule (citations at 870a93d21)

- Dispatch and every session call go through `Resolve` (`PhoneHomeRunnerDirectory.cs:157-168`), which throws
  "has not completed recovery" while `!live.DispatchEligible`.
- Set false by: `ReadSnapshot` on lease expiry, when `now - LastHeartbeatUtc > LeaseSeconds(90)`
  (`PhoneHomeRunnerDirectory.cs:984-994`, `PhoneHomeLiveConnection.cs:345-348`), or by `Disconnect` (`:480-494`).
  Only Heartbeat frames renew the lease (`PhoneHomeLiveConnection.cs:509-511`). Event frames do not
  (`:540-570`).
- Set true by `MarkRecovered` (first recovery of a connection, `PhoneHomeRecoveryPump.cs:201-206`) or
  `TryRecoverLease` (lease re-recovery, `:196`). For a live connection that has already recovered
  (`recoveringLease`, `:146`) all of these must hold:
  1. `CanRecoverLease` (`PhoneHomeRunnerDirectory.cs:432-442`): same `slot.Live` and `slot.Epoch`, enabled, socket open,
     lease fresh, no recorded disconnect reason, **`PendingEvents == 0`**, not draining or retired;
  2. `CatchUpAsync` returns true: a List **plus** a transcript fetch and DB persist for every owned listed
     session (`PhoneHomeRecoveryPump.cs:349-401`; a List failure retries after `CatchUpRetrySeconds`=5, `:190-193`);
  3. `TryRecoverLease` rechecks (1) under the lock after the catch-up (`:444-461`). On refusal it returns
     false with **no log** and no retry delay (`:196-197`).
- There is no backoff cap and no circuit. A healthy pump would retry within 50 ms to 5 s. There is no deadline on
  `CatchUpAsync` and no watchdog on the serial loop (`PhoneHomeRecoveryPump.cs:61-77`), so one un-timed await
  freezes recovery for all runners until the server process restarts. A new epoch does not help, because the
  new connection's catch-up runs on the same frozen loop.

## Recovery options (none executed), ranked by risk

1. **Wait.** Lowest risk and no blast radius, but **no evidence that it self-heals**: no List in 35+
   min, and nothing in the suspected hang path has a timeout. Self-heal is possible only if a blocked per-session
   gate holder (`SessionStateStore`) eventually releases it, and nothing bounds when. Cost: server2 holds 0 new
   work, and two Idle seats (d7c12587, cad48ede) stay occupied.
2. **Restart only the server2 phone-home connection.** **No operator route exists**. Drain and drain/clear do not
   touch eligibility. `PUT .../capacity` needs an eligible runner. There is no reconnect command on the runner
   HTTP surface. A runner-side reconnect needs ssh into the container. Even then, if the pump loop is hung
   (most likely), the new epoch's catch-up never runs. **Not a reliable fix.**
3. **Restart the server2 session-runner container** (`docker restart <runner>` on server2, via the
   `server2` skill/ssh). Blast radius: every process in the container. pty hosts are adopted across a runner
   *process* restart (adoption sweep, `docs/session-runtime-invariants.md:121-148`), but a *container* restart
   also kills the in-container pty hosts unless they live outside it (**unverified**). Treat the 4 running tasks
   77238f98, 13eaf3b2, 32f24400 and e96c2ae3, plus the 2 Idle seats, as killed. It also does not fix a hung desktop
   pump. **Worst ratio; do not do this.**
4. **AppHost restart** from the main checkout: `pwsh -NoProfile -File scripts/restart-apphost.ps1`
   (check `logs/apphost.restart.lock` / `logs/apphost.launch.lock`; afterwards confirm `GET /api/version` =
   870a93d21). It recycles the server process, so it is the only option that certainly unfreezes the recovery
   pump, and it starts a new epoch. Precedent from tonight: at 00:25 epoch 2 ended `request_aborted`, at 00:29
   epoch 1 was accepted, and server2 dispatch then worked. The server2 sessions keep running in the container;
   the server re-reads them in the new connection's catch-up. Blast radius: about 1-4 min of desktop API downtime,
   the desktop runner, and the **live Windows qualification run 06863906** (known to produce host-load flakes
   during an AppHost restart window, the C1514 host-load test, memory 2026-11-12). That rule makes this an
   orchestrator timing call.

**Recommend option 4, the AppHost restart**, at the first gap in run 06863906 (between chunks, or after
the run), or at once if refilling server2 outweighs one possible host-load flake that can be rerun. It is the
only action that clears both an un-timed hang in `PhoneHomeRecoveryPump` and the stale lease state.
Before restarting, if the operator allows a few seconds' pause of the server, take
`dotnet-dump collect -p <server pid>` and run `dumpasync` to capture the hang site for the durable fix.

**Evidence that it worked:** a new log line "Phone-home connection server2 epoch 1 accepted" after the restart,
then `GET /api/session-runners/server2/status` with `dispatchEligible: true`, a `lastCatchUpMs` that changes
every about 30 s (refresh, `PhoneHomeRecoveryPump.cs:236-244`), `GET /api/session-runners` row `stale:false`,
non-null `capacity`, and tasks 03afb6f5 / 3871f961 logged "released: hold cleared", then dispatched to
server2. The janitor should then reap the 2 Idle seats.

## Durable fix candidates (not implemented), ranked

1. **Bound and observe lease re-recovery** (`PhoneHomeRecoveryPump.cs:146-209`, `:349-401`):
   (a) give `CatchUpAsync` a deadline (linked CTS, for example 2x the List timeout plus a per-transcript budget) so
   one stuck transcript or persist cannot freeze the serial loop; (b) for `recoveringLease`, require only the
   List. The connection's observer group is still running, so transcripts are already flowing, and re-fetching
   and persisting every transcript is what exposes the gate hang; (c) log every lease expiry
   (`PhoneHomeRunnerDirectory.cs:989-994`), every re-recovery attempt and every `CanRecoverLease`/`TryRecoverLease`
   refusal with the failing condition; (d) run each runner's cycle independently with a watchdog that
   logs Error when a cycle exceeds N s (`:61-77`, `:108-117`).
2. **Renew the lease on any frame from the runner**, not only Heartbeat (`PhoneHomeLiveConnection.cs:509-511` vs
   `:540-570`). During 02:49-02:51, events kept arriving, about 10/s, while no heartbeat was processed for 90 s.
   A frame read in order proves the runner is alive. This would likely have prevented tonight's expiry.
3. **Relax `PendingEvents == 0`** in `CanRecoverLeaseUnderLock` (`PhoneHomeRunnerDirectory.cs:440`) to
   "oldest pending < N s" or "below a small threshold". Under continuous output, exactly 0 at both checks
   (before catch-up and in `TryRecoverLease` after a long catch-up) is a race that can lose repeatedly and silently.
4. **Bound the remaining un-timed waits**: `_send.WaitAsync(ct)` inside the request timeout
   (`PhoneHomeLiveConnection.cs:410`), and `SessionStateStore` `entry.Gate.WaitAsync(ct)`
   (`SessionStateStore.cs:117`) for callers on the recovery path.
5. **Output-volume relief** (lower priority; not the cause): coalesce SessionOutput activity writes per
   session (output.activity costs about 1 s under load, phase max 3.0 s) and/or shed the activity phase before
   transcript/exit work. This continues CARD-1523's design. Also make the diagnostics `sessions` list
   top-N by bytes instead of first-16-seen, so the current producers can be ranked.
   Raising the List timeout alone would not help: List was not the stuck call after 02:51.

## UNOBSERVED

- The exact await the recovery pump is parked on. That needs a managed dump of the server process
  (`dotnet-dump collect` + `dumpasync`), which this read-only brief did not take.
- Whether the runner or the network stalled heartbeats at 02:49-02:51, or the desktop receive loop
  did. There is no server2 container log access (no ssh), and the desktop log has no per-frame receive timing.
- Server2 container CPU and memory, and runner-side logs for List handling at 02:49-02:51.
- Whether pty hosts survive a server2 *container* restart (as opposed to a runner process restart).
- `GET /api/session-runners/server2/slots` returned **503** (it resolves through the same gate), so the
  runner's seat list was not readable.
- Per-session output for sessions after the first 16 on the connection (`otherSessionEvents` bucket).
- Exactly when `lastCatchUpMs=30` was recorded: either the last good refresh before 02:48 or a catch-up
  List after 02:51 whose transcript step then hung.
