# Phone-home connection replacement blocked the production server

On November 27 the desktop server stopped answering health checks four times after its 00:20 UTC activation. The 03:13 UTC managed stack capture identifies a synchronous wait inside the directory-wide monitor. It also shows the previous connection's completion path waiting to enter that monitor.

## Evidence

The supplied dump is `/work/evidence/server-hang-20261127/server-stacks.txt`, SHA-256 `dac4f7c7aa10ca2d2f9884a306f3480e5ec93e32cfa994f56cbd7c977061e264`. It contains 96 distinct thread headers and 4,285 lines.

| Managed thread or group | Observation |
| --- | --- |
| `0xC398` | The WebSocket connect handler is in `AcceptConnect`, blocked in a task wait. Its sole synchronous task wait is the disposal of the superseded connection while holding `_gate`. |
| `0x1D5F0` | Credit-writer cancellation ran receive-loop and endpoint continuations inline. The endpoint reached `Disconnect`, which needs the same monitor. Disposal can await the credit writer before even starting the three-second WebSocket close handshake. |
| 55 registration requests | Each is waiting in `Register → DrainState`. This is a dictionary lookup and locked read of the mirrored state. Registration does no database query. |
| 13 snapshot readers and 2 runner-ID readers | `SnapshotOf` uses the monitor to check the live lease. The recovery cycle and watchdog both take it to copy configured IDs. Capacity, inventory and dispatch callers consequently wait too. |

Those stacks establish the lock holder and the contention. The dump does not contain the owner objects or runner IDs needed to match a specific peer to the teardown or to attribute the first reconnect. It also cannot establish which awaited disposal continuation was last scheduled. The operating-system thread count and CPU figures in the incident report were separate observations.

## History and coverage

CARD-0490 introduced synchronous disposal inside connection admission in `b6fddd73ee11043cf02809ebd64c3df17ebbdc60` on September 20. CARD-0727 subsequently added the drain-state reader. The event-credit writer seen in the dump was introduced by `c58543c4e26d321d02084ec39cf47bc77953daf2` on October 23. The directory, live connection, and runner reconnect loop are identical between the last healthy build (`0cfc4ac1ffb41f5af159a0c1db423fe119950267`) and the incident build (`5c0883bf203d122fdc7d4556e79087af2a683669`). The endpoint changes in that range added release-intent routes.

CARD-2078 reconciliation and the CARD-2083 two-minute custody retry resolve runners and can add directory readers. Their asynchronous runner calls execute after that lookup has released the directory monitor. Neither job calls registration or connection replacement. Neither appears as the holder in this sample. The timing after activation does not identify a November landing as the trigger.

The existing replacement test checks the epoch retrieved after the WebSocket upgrade, and the multi-runner tests check ownership after replacement. A cooperative peer can finish closing before either assertion runs. The backoff tests cover registration timeouts and the exponential cap. The missing overlap was a directory reader entering while old transport cleanup was still pending.

## Repair and regression coverage

Connection replacement consumes the ticket, records the disconnect, and publishes the new connection under the monitor. The first repair released the monitor before awaiting old transport cleanup. Review dd57df06 found that the wait still held up the eligibility notification and the replacement receive loop. Round 2 starts tracked cleanup outside the monitor and lets the replacement proceed. Server shutdown drains the tracked tasks. A late disconnect checks object identity before touching the live slot.

Registration has one lock acquisition with a 50 ms contention budget. A busy admission returns HTTP 503 with a one-second `Retry-After` before minting a ticket. The runner previously made one attempt at a time with an eight-second HTTP timeout, deterministic 1/2/4/5-second failure delays by default, and an immediate retry after a normally returning connection loop. A client timeout could leave the server request queued. The first repair added jitter and accepted the server's retry hint. Review measured a one-hour hint reaching the timer. Round 2 limits the hint contribution and total delay to 30 seconds. That lets a recovering server shed retries for several normal backoff intervals while preserving the busy response's one-second floor and jitter at the ceiling.

The replacement witness holds the previous socket's close while an open loopback replacement answers List, remains eligible after a late Disconnect, and delivers a queued prompt confirmed by a complete UserPrompt received on the replacement epoch. The busy-admission witness checks the response, retry hint and next issued epoch. Runner timer tests use virtual time and check both jitter bounds, both oversized hint formats, a malformed hint and a normal loopback close.

## Interim operation

Use the server's connection acceptance log (runner ID, epoch, connection ID and peer address) and the runners' timestamped reconnect lines to identify the source. At ingress, temporarily return 503 on new registration POSTs for that source, preserving established WebSocket streams. If attribution is unavailable, hold all new phone-home registrations using the same narrow rule. Verify that each runner's configured origin traverses the rule. A runner already disconnected will have no remote control or dispatch until readmission. Its process and resident sessions can keep running.

If the monitor is already wedged, a registration hold prevents new attempts from reaching the lock but cannot release the current owner. With the hold in place, use the canonical desktop server-only AppHost restart under its ordinary runbook gates. Verify `/api/version` and `/health`. Restore admissions after deploying the reviewed server fix and verify each runner's recovered epoch and inventory. Update the runner separately to pick up jitter. [AppHost restart runbook](../apphost-runbook.md)
