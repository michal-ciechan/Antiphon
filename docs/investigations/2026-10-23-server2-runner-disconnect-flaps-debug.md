# server2 phone-home disconnect flaps, 2026-10-23

Both reported disconnects, plus an earlier third one, were **desktop-side
`phone_home_event_overflow`**. The server exhausted its 8,192-event pending limit
while its serial consumer fell behind. Desktop CPU saturation and concurrent
landing verification correlate with the incidents. The runner stayed running;
inventory RPCs failed and dispatch eligibility withdrew, but the inspected task
journal contains no transport-attributed task failure or runner-eligibility hold.
All three concurrent lands published.

This was a read-only operational investigation: no restart, deployment, process
stop, configuration change, build, test, or live load experiment. The only writes
were local evidence, this report, and the two expressly requested Backlog cards.
The report's owner must land it separately; this investigation does not activate
any product change.

## Identity and evidence scope

- Debug task: `e10e4bf9`; branch `feat/card-task-e10e4bf9`; initial clean HEAD
  `c5da1d2387c08adf86e301a3874a5cbcfd4d6a98`.
- Loaded desktop server, read from `/api/version` during collection:
  `0714a9a89e4bff65aae7e0b4b94098694aeb931c` (`land-v2`).
- Runner build in all three connection-accept lines:
  `81cfb267a0af5c0ddb65e8b238af355067bee9cc`.
- Runner store `f519bd08-e53a-47d1-adb1-2ab33475446f`, process boot
  `d9dd4749-8b92-4322-b5ad-946ecc8715c3`. Container
  `antiphon-runner-session-runner-1` started `2026-10-20T06:55:48.614211969Z`,
  restart count 0, image
  `sha256:ca6a8ca50313607c74e5cc46924b32f88b242f69bf010d03a43f888bd5193d6b`.
- External evidence: `C:\Antiphon\evidence\debug-e10e4bf9\`. This contains
  filtered server/runner/proxy logs, CPU and memory series, occupancy audit,
  attention snapshot, task details, qualification progress and SQL/result files.
  Generated evidence is not committed. `manifest.sha256` hashes the retained files.
- Primary server files: `C:\src\Antiphon\server\logs\antiphon-20261022_002.log`
  and `antiphon-20261023.log`. Server timestamps are BST (`+01:00`); runner and
  proxy logs are UTC. **All incident times below are UTC on 2026-10-22.**
- The task-impact journal query covers 22:25:00 through 23:20:00, scoped to
  Antiphon project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`. Attention is a later
  fleet snapshot, not a historical reconstruction.

## Exact disconnects

| UTC end-log time | Epoch / connection | Desktop reason | Pending at end log | Waiters failed | Next desktop accept / gap |
|---|---|---|---|---:|---|
| 22:29:43.969 | 2 / `0HNPGD87AIK0J` | `phone_home_event_overflow` | 8,192 events / 1,236,246 reported bytes | 10 | 22:29:47.962 / 3.993 s |
| 22:55:59.597 | 3 / `0HNPGD87AISDA` | `phone_home_event_overflow` | 8,192 events / 1,454,769 reported bytes | 8 | 22:56:01.340 / 1.743 s |
| 23:16:47.465 | 4 / `0HNPGD87AIT3K` | `phone_home_event_overflow` | 6,502 events / 925,228 reported bytes | 2 | 23:16:50.867 / 3.402 s |

End-to-end intervals were **26m15.628s**, then **20m47.868s**. These are not a
fixed lease or keepalive period. Connection lifetimes were 14,560.7s, 1,571.7s
and 1,246.0s. The accept gaps do not include subsequent catch-up work: the runtime
requires successful recovery before dispatch eligibility returns.

The end log is not an atomic snapshot of the triggering backlog. The pump can
drain between overflow detection and logging. Epoch 3's structured fields already
read 8,191 / 1,454,527 while its rendered text reads 8,192 / 1,454,769. Epoch 4's
6,502 is likewise a later observation, not evidence that the limit was 6,502.
The status snapshot records its disconnect at 23:16:47.2933309, before the end
Warning at .465. All three end lines say socket `Open` at that observation.

Server line references are 248320-248321 and 248479, 293812-293838 in the October
22 file; 5701, 5714, 5856 in the October 23 file. The 90% warnings are:

| UTC | Epoch | Pending / count cap | Reported bytes / size cap | Pump releases over preceding 10 s |
|---|---:|---|---|---:|
| 22:29:43.886 | 2 | 7,373 / 8,192 | 1,112,583 / 16,777,216 | 1.2 events/s |
| 22:55:59.558 | 3 | 7,373 / 8,192 | 1,319,836 / 16,777,216 | 1.2 events/s |
| 23:16:46.758 | 4 | 7,373 / 8,192 | 1,099,846 / 16,777,216 | 87.2 events/s |

The two reported epochs had first crossed 50% at 22:37:12.330 and 23:06:14.085.
Warnings fire once per threshold per connection, so those timestamps do not prove
the backlog stayed above 50% continuously. At roughly these payload sizes the
event-count limit, not the configured size limit, binds. The 23:16 incident's
exact trigger snapshot was not retained, but its surrounding telemetry and the
same code path strongly support the same count-limit explanation.

The runner's matching records corroborate a server-initiated close:

| Runner UTC | Epoch | End observation |
|---|---:|---|
| 22:29:45.378 | 2 | `loop=receive fault=none overflow=false pending=0 pendingBytes=0 inFlight=0 close=NormalClosure:dispose` |
| 22:56:00.426 | 3 | `loop=receive fault=none overflow=false pending=6 pendingBytes=897 inFlight=19 close=NormalClosure:dispose` |
| 23:16:47.686 | 4 | `loop=receive fault=none overflow=false pending=0 pendingBytes=0 inFlight=0 close=NormalClosure:dispose` |

Runner `overflow=false` describes the runner's own queue and does not contradict
the desktop overflow. Both Docker stdout and persisted
`/state/runner-logs/session-runner-20261022.log` confirm the two requested flaps.
The runner next logged connections 3, 4 and 5 at 22:29:47.989, 22:56:01.139 and
23:16:50.889 respectively. Millisecond differences between hosts are not used to
infer network latency.

`C:\src\Antiphon\logs\client-proxy.log` records **target** socket close before
client close for every incident: 22:29:45.662/.721, 22:56:00.644/.649 and
23:16:47.886/23:16:48.341; all `hadError=false`. That corroborates the server's
explicit overflow decision. There is no evidence here for `message_too_large`,
`transport_abort`, `request_aborted`, `receive_fault`, `lease_expired` or
`socket_closed` as the cause. No Tailscale/Cloudflare change is indicated. A
network delay could contribute to delivery bursts, but no tunnel trace proves
one and it is unnecessary to explain the observed termination.

## Load and simultaneous work

The retained desktop 30-minute series starts at 22:52:05; it cannot establish
the first, 22:29 incident's CPU. It does directly confirm the reported 99.98%:

- 22:55:42.119 and 22:55:45.603: CPU 100%; 22:55:58.7707242:
  **99.98492840994724%**; 22:55:59.306: 99.6644%; 22:56:00.563: 100%.
  Physical memory used was 29,120,552,960 bytes at 22:55:58, out of
  34,298,142,720 total (about 5.18 GB available).
- 23:16:24: CPU 100%, memory used 30,489,755,648 bytes. The nominal five-second
  series then has an approximately **29-second gap** to 23:16:53: CPU 97.21%,
  memory used 32,682,700,800. Memory reaches 33,519,788,032 at 23:17:03,
  leaving about **778 MB** physically available. The sample after the gap is
  not an instantaneous CPU measurement of the missing seconds.
- At 23:21:53 the desktop 30-minute CPU mean was 93.99%, maximum 100%.
  Server2's corresponding mean was 64.64%; its samples around the second flap
  continued every five seconds (57.43% at 23:16:43 and 90.71% at 23:16:48).
  Server2 memory was about 37.5-37.8 GB of 135.18 GB there. The clearest memory
  constraint is on the desktop.

Every disconnect overlapped a desktop land verifier:

| Task | Verification interval (UTC) | Relevant detail | Publication |
|---|---|---|---|
| `b94623ea-2250-419e-a1a2-569eff8b379c`, CARD-1500 Windows CP-3 attempt 2 | Slot granted 22:27:01.322; child joined 22:30:20.106 | `maxcpucount=4`, 198s leased | 22:32:34.312, `LandedWithResidue` event; publication `Landed` |
| `85ca6a8c-81d0-4b46-b199-0dfeafcec583`, CARD-1514 slow test profile | Fallback began 22:53:13.243; child joined 22:56:13.837 | After 120s memory-floor wait: `BUILD SLOT unleased reason=memory_floor_fallback available=4891MB floor=6144MB maxcpucount=2` | 22:57:29.651, `Landed` |
| `c6dd91f2-8a94-46d8-bece-bc7d5445dbaf`, CARD-1522 query budget debug | Slot granted 23:13:27.776; child joined 23:17:07.964 | `maxcpucount=4`, 220s leased | 23:19:19.827, `Landed` |

The unleased fallback is an observed existing CARD-1461 option-C path, not a run
started by this investigation. CARD-1470 already tracks its diagnostic/global
bound follow-ups. Other verifier intervals did not cause a disconnect, so
coincidence is not proof that any build necessarily causes a flap.

Windows qualification run `d2777e21ef4c4a3e9b1fb315ff8f5602`, candidate
`rc-2026-10-20-0714a9a89e4b`, was running concurrently. Its retained supervisor
progress has entries at 22:24:30, 22:34:43, 22:55:15 and 23:15:36, with active
descendants and increasing cumulative CPU. Group 17 began around 21:53:41 and
group 18 around 22:53:50. The run state remains phase `tests`; its test reds
are pre-existing qualification evidence, not results of this Debug task.

This establishes **load correlation**, including qualification and lands. It
does not isolate how much CPU each process consumed at the disconnect instant,
prove a particular GC pause, or distinguish CPU scheduling from a slow database
or SignalR await in the consumer. No runtime GC/thread-pool trace was retained
for these moments. No matching pump-crash/restart or event-observer exception
line was found in the inspected incident window.

CARD-1519 was still in Review when inspected. `/api/version` continued to name
0714a9a89; the runner's build and container did not change. There is no evidence
of CARD-1519 activation, an AppHost restart, or server2 deployment causing these
three flaps. Earlier same-day `request_aborted` closes at 17:46 and 18:25 UTC
are different incidents and are not conflated with these overflows.

## Consumer path and misleading backlog telemetry

The loaded SHA and Debug base have identical `PhoneHomeLiveConnection.cs` and
`PhoneHomeRecoveryPump.cs`. The relevant code is:

- `PhoneHomeLiveConnection.ReceiveLoopCoreAsync`, lines 456-468: account events,
  enforce `Limits.MaxPendingEvents` / `Limits.MaxPendingEventBytes`, and queue
  frames. Heartbeats and RPC results use separate branches before event queuing.
- `PhoneHomeRecoveryPump.PumpEventsAsync`, lines 359-424: one sequential consumer
  for all sessions on this connection, owner/epoch checks, then awaited output,
  transcript or lifecycle observation. The `finally` releases pending accounting.
- `AgentSessionRuntime.ObserveOutputAsync`, lines 121-149: awaits SignalR group
  publication and activity recording. `ObserveTranscriptAsync` awaits persistence.
  Owner binding caching and throttled activity writes already exist; repeating
  those CARD-0679 changes would not be a new fix.

No event-type/session histogram or slow-observer timings were logged at high
water. The lifetime received totals imply roughly 245 events/s for epoch 3 and
217/s for epoch 4, but do not identify which session or event type produced the
burst. The 90%-to-close jumps are consistent with a rapid receive burst while the
consumer is slow; only a controlled witness or added timing metadata can establish
the exact slow observer.

**The claimed 1 GB+ `pendingEventBytes` was not corroborated.** Actual captured
pending values were around 1-1.5 MB at overflow and small/zero after recovery.
The log's other value, `live buffer`, is misleading: `_liveBufferEvents` and
`_liveBufferBytes` only increment, while `ReleaseEvent` decrements the separate
pending fields. Consequently epoch 2 reported `live buffer 1927805 events`,
epoch 3 385905 and epoch 4 269906. Those are cumulative received totals, not
millions of retained queue items. This does not explain away the real pending
overflow, and it does not prove a GB memory leak. Public status uses the pending
fields, not those cumulative totals.

## Observed impact and limits

- Connection teardown failed **10, 8 and 2 pending RPC waiters**. Inventory
  refresh `List` failures are explicitly logged for each, with
  `phone_home_connection_closed_in_flight`. Epoch 3 also interrupted the seat
  occupancy sampler. The complete operation mix of all waiters is not logged.
- Durable occupancy records label the sample at 22:55:58.349 as
  `unavailable/error` (6 sessions); the next sample at 22:56:57.366 lists 6.
  Samples at 23:15:57.310 and 23:16:59.327 both list 8 sessions. The broader
  window also has inventory timeouts away from disconnects, consistent with
  general load. These samples support session survival, not perfect transport
  continuity or zero delivery latency.
- The scoped 22:25-23:20 task query returned **45 Held, 2 Blocked, 0 Failed,
  0 LandRefused, 0 HeldAged** events. None of the Held/HeldAged rows names
  `RunnerUnavailable`, `runner_unavailable`, `dispatch-eligible` or `phone_home`.
  Holds instead name repository leases, inspection, or remote preparation.
  This is bounded journal evidence; a brief readiness withdrawal need not be
  sampled by the dispatcher and an unchanged old hold need not append a new row.
- `2a42be76` reported a blocked result at 22:29:46.536, close to the earlier
  flap; the event says delegate verdict, not transport failure. It subsequently
  completed its recipe and published at 23:02:50.360. `d59d844d` became Blocked
  at 22:54:28.446 with `runner_sync_lease_busy`, **before** the 22:55:59 flap.
  Do not relabel those as proven disconnect casualties.
- The 23:21:06.997 attention snapshot has no `DispatchHeld` or
  `OccupancyDivergence`. Its one `SessionDisagreement` is a settlement-sync debt
  for `76fe1072-b65f-46ce-898c-1c927faffc4e` dating to October 14. Its one
  `RunnerUnavailable` is idle `server2-temp`, down since 18:26:57 with zero open
  tasks/sessions. Neither is evidence of new server2 disagreement from these flaps.
- A later saved server2 status has epoch 5, reconnects 4, available and
  dispatchEligible true, pending 0 / 0, and the same last disconnect. Earlier
  recovery observation at 23:20:36 had 63 pending / 10,135 reported bytes,
  seven bound sessions and eight runner sessions. Recovery occurred without an
  intervention from this task.

## Smallest mitigation and follow-up work

Restore desktop CPU/memory headroom during simultaneous qualification and landing
verification as the immediate operational mitigation. An operator can choose
less overlap or lower build parallelism through the existing admission controls;
this investigation changed neither. The memory-floor fallback already observed
here is a relevant trade-off: bypassing the floor may complete lands sooner while
adding pressure. CARD-1470 owns its existing follow-ups.

**Do not increase the phone-home lease, heartbeat timeout, or tunnel timeout for
this incident.** They do not govern the observed overflow. The relevant existing
knob is `PhoneHomeRunner:Limits:MaxPendingEvents` (observed 8192); a larger value
could absorb a finite burst while keeping `MaxPendingEventBytes` bounded, but
would delay rather than fix a persistently slow consumer. The evidence does not
justify a particular replacement value. Measure consumer catch-up first.

The smallest durable code work is bounded handling of transient consumer stalls,
with event-mix and observer-duration metadata. Output batching or separating a
bounded output/UI observer lane from durable ingestion are candidates. Preserve
per-session sequence, mention parsing, owner/generation checks, transcript/exit
delivery and a defined sustained-overload recovery path; do not drop arbitrary
frames or remove the cap. A deterministic delayed-observer/burst witness should
exercise the real pump before choosing the implementation.

Filed on Antiphon board `8988ca03-7414-47ad-b0b6-51556c701703`, both Backlog:

1. **CARD-1523**, `e4fb6da6-83e7-469a-803b-3fc61e521e03`: phone-home serial
   consumer overload and eligibility flaps; High/Soon. Includes incident evidence,
   bounded repair options, attribution telemetry and acceptance expectations.
2. **CARD-1524**, `e8d0a1c5-c4e3-44cc-b4e4-72345aa96538`: cumulative received
   counter mislabeled as live buffer; rename it or maintain a real gauge, and
   snapshot end diagnostics consistently.

Complete `card.ps1 search -Board Antiphon -All` searches used phone-home,
overflow, event overflow, backpressure, LiveBuffer, live buffer, pendingEventBytes
and CPU; matching CARD-0679, CARD-0716, CARD-1461, CARD-1470, CARD-1478 and
CARD-1519 were inspected. The two new findings are distinct from those existing
cards. No task was spawned.

## Reproducing the evidence reads

Use the authentication and source selection in `docs/ops-http.md` and
`docs/logs.md`; do not print tokens. The short host series is retained in memory
and must be captured promptly. The saved series in the evidence root preserves
this incident after it ages out.

```powershell
rg -n 'Phone-home connection' C:\src\Antiphon\server\logs\antiphon-20261022_002.log C:\src\Antiphon\server\logs\antiphon-20261023.log
pwsh -NoProfile -File scripts/logs.ps1 -Source server2-runner -Tail 100
```

Read `/api/session-runners/server2/status`, `/api/hosts/stats`,
`/api/hosts/local/stats/series?metric=cpu&window=30m` (and `metric=memory`),
the corresponding server2 series, and
`/api/hosts/server2/occupancy-samples?from=2026-10-22T22:25:00Z&to=2026-10-22T23:25:00Z&limit=2000`.
Task queries and exact result counts are retained as `task-impact*.sql` and
`task-impact*.txt`; they use read-only SELECT through the documented local
Postgres container. One broad task-list HTTP request timed out, so impact claims
use these scoped journal queries and successful task-detail reads instead.

No tests/builds were run because this deliverable is narrative incident evidence.
No performance repair has been verified or claimed. Remaining uncertainty is
the exact slow observer/burst producer and process/GC attribution, not the
disconnect classification.

--- next stage ---
next: decide
handoff: Land this narrative report through its Docs owner; prioritize CARD-1523 and CARD-1524. Decide operational desktop load mitigation without changing lease/tunnel timeouts. Product repair needs its own Plan/Code/Review work.
artifact: docs/investigations/2026-10-23-server2-runner-disconnect-flaps-debug.md
