# Why missed: CARD-1934 C519 main queue-to-adapter broker startup

Sweep B027/R005 on baseline `b559bfba2e9a420495122d27171352fe9b7e1ba4` failed
`ChannelOutboundUnifiedTransportTests.C519_Queue_to_adapter(main)` before any
delivery assertion. Current `origin/master` `08cc3dcac230658c9bb7f580d32ad91906665fbd`
ran that same UID alone three times and passed. That Debug pass changed no product or
test source and left the 30-second startup token in place. The follow-up section
records the later alignment of the two startup tokens to the sibling 120 seconds.

## Classification

FIXTURE STARTUP BUDGET, not a product defect, not reproduced on this host today.

The dependency is an owned Testcontainers Redpanda
(`docker.redpanda.com/redpandadata/redpanda:v25.3.4`) plus Testcontainers Ryuk
`0.14.0` (`sha256:7c1a8a9a47c780ed0f983770a662f80deb115d95cce3e2daa3d12115b8cd28f0`).
Both images were already local. There is no host Redpanda. `docker image inspect
testcontainers/ryuk:0.14.0` does not resolve; the digest reference does, and every
create in these runs used `testcontainers/ryuk:0.14.0@sha256:7c1a8a9a…` with no
image-pull event.

`QueueToAdapterAsync` (`tests/Antiphon.Tests/Application/ChannelOutboundUnifiedTransportTests.cs`)
builds that broker and passes one `CancellationTokenSource(TimeSpan.FromSeconds(30))`
to `StartAsync`. That call starts Ryuk (`ResourceReaper.GetAndStartNewAsync`) and
then creates Redpanda and waits until the log contains `Started Kafka API server`.
Testcontainers `WaitStrategy.WaitUntilAsync` waits with `Task.Delay(timeout, ct)`.
When the 30-second token fires, that delay completes and the helper throws
`TimeoutException` ("The operation has timed out") even though the wait strategy's
own timeout is one hour. If the token fires earlier, during Ryuk
`CreateContainerAsync`, the same budget surfaces as `TaskCanceledException`. Those
are the two historical stacks. Both are before `QueueToAdapterAsync` reaches a
delivery scenario.

Sibling methods in the same file (`C519_Partial_prompt_requires_complete_receipt`,
`C519_Enabled_loss_notice_reaches_adapter`, `C519_Size_refusal`) pass a 120-second
token to the same `StartAsync`. Only the queue-to-adapter helper and
`C519_Converter_handoff` use 30 seconds.

## Measurements on 08cc3dc (exact UID, alone)

Docker events versus the TRX method start. "To Redpanda start" is the lower bound
of `StartAsync`. Kafka-ready was not timestamped; a pass means the whole
`StartAsync` finished inside the 30-second token.

| Run | Load at launch (1m) | Slot wait | Method start (UTC) | To Redpanda start | Left of 30s at container start | Result | Method duration |
|---|---:|---:|---|---:|---:|---|---:|
| 1 | 21.63 | 15s | 02:14:08.725 | 12.3s | 17.7s | Passed | 5m 38.8s |
| 2 | 35.46 | 165s | 02:26:37.043 | 16.0s | 14.0s | Passed | 4m 56.1s |
| 3 | 27.30 | 105s | 02:33:58.869 | 13.1s | 16.9s | Passed | 4m 56.3s |

Ryuk create-to-start was about one second once the daemon accepted the create.
The 12–16 seconds are Docker API time before the Redpanda container exists, most
of it before the Ryuk container exists. Build of `tests/Antiphon.Tests` was
6m 48s, slot wait 0s, `maxcpucount=6`, 0 errors, load 24.55 before and 28.91 after.

Three passes are not a p95 of the failing tail (test-performance standard R5: a
small sample is provisional). The historical alone retry was 0/1 at 99.5s wall
with the token already cancelled inside Ryuk create, at load 15.08. Four
`testcontainers-ryuk-*` containers from 2026-11-13 and 2026-11-14 are still
`Created` and never started (session labels `928754f5`, `a077623a`, `9e3ceb44`,
`b095f694`). They were not removed.

## Why the budget was missed

The 30-second token was introduced as a startup watchdog, not as twice a measured
startup. On a warm daemon the container is up in 12–16 seconds and Kafka still
fits. On the sweep the same token expired during Ryuk create or during the
readiness wait. Nothing in the checkpoint timeout audit sees a
`CancellationTokenSource` inside the test. CARD-1906 load makes that Docker
latency the variable; it does not change the delivery assertions.

## Introducing commit/card

introducing commit/card: `729e1c252bd95f5cd1d8e43ba93f43ff1789516e` (CARD-0519),
2026-10-06, "test(channel): add queue and converter transport handoff matrices".
That commit adds the 30-second token at the queue and converter startup sites.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- No `docs/reviews` file names this class. The CARD-0519 plan's landed rows that
  select it are CP-25, CP-62 and CP-70:
  `/*/*/ChannelOutboundUnifiedTransportTests/C519_Queue_to_adapter*`, three
  results, 0 failed/skipped, on the Code host where startup finished inside 30
  seconds. D-S12-6 records measured minutes for the whole method and forbids
  widening that row to fit the 15-minute cap; it does not measure the inner
  broker token. S12c..S12f ordinary scope is CP-61..CP-78 in that plan.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration
and Slow, `tests/Antiphon.Tests/Application/ChannelOutboundUnifiedTransportTests.cs`.
CARD-1892's explicit checkpoint lists did not include the class (sweep note on
`feat/card-task-15406ecc`). The class is in the source Windows roster. The live
qualification `34d41b7e` numeric chunk that would have run it was unmapped, and
this Debug did not read that run. A Linux pass of the class does not prove the
30-second token still has headroom when Docker create is slower than these three
samples.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Application/ChannelOutboundUnifiedTransportTests.cs` ->
`/*/*/ChannelOutboundUnifiedTransportTests/C519_Queue_to_adapter*`

That filter is the method the sweep failed. It passes when Ryuk create plus
Kafka readiness finish inside 30 seconds, which is what happened on
`08cc3dc` above. The check that would have caught the sweep failure is the
headroom of that inner token against Docker create time, which the class filter
does not record.

## Follow-up startup budget

Stated reason, test-performance standard R5. A failing-tail p95 is unavailable.
The three alone passes on `08cc3dc` finished inside 30 seconds, so that sample
has no failing tail. R5 would set a timeout at about twice a measured
failing-tail p95 and would label a sample this small provisional. This change
does not invent that p95.

Measured Docker-time share, same three passes: time before the Redpanda
container existed was 12.3s, 16.0s and 13.1s. That is 40 to 53 percent of the
30-second token, at load 21.63, 35.46 and 27.30, and it is gone before Kafka
readiness. The historical failures are that same token. Group B027's main case
threw `TimeoutException` from `WaitUntilAsync`. The alone retry threw
`TaskCanceledException` from Ryuk `CreateContainerAsync`.

Sibling precedent in `ChannelOutboundUnifiedTransportTests`:
`C519_Partial_prompt_requires_complete_receipt`,
`C519_Enabled_loss_notice_reaches_adapter` and `C519_Size_refusal` pass a
120-second token to the same `StartAsync`. `QueueToAdapterAsync` and
`C519_Converter_handoff` were the only 30-second startup tokens. Both are now
120 seconds. Delivery assertions are unchanged. Every other timeout is
unchanged, including the 120-second scenario tokens and the 30-second
`WaitForIdleAsync` in `C519_Converter_handoff`.

Phase timing uses the existing `TestTiming.BeginPhase` recorder
(`TestPhaseKind.FixtureSetup`) and no new framework. The operation labels are
`c1934-ryuk-create-start`, `c1934-redpanda-container-create` and
`c1934-kafka-api-server`. The test output repeats them as one
`C1934_BROKER_PHASE` line. Ryuk is the default resource reaper until
`MaintainingConnection`. Redpanda container create is the next interval, until
the container `Created` event, and includes the image-present check plus the
Docker create. The Kafka span is `Created` until `Started`: `UnsafeStartAsync`
through `UntilMessageIsLogged("Started Kafka API server")`, including the
container start and the startup-script copy that precede that log line.

## Exemption

same cause as an already written note: none.

The checkpoint child-deadline note
`docs/investigations/2026-11-10-why-missed-g109 CheckpointSourceExecution xml timeout.md`
is the same shape of bug (one 30-second token reported as a timeout under load)
and a different cause.
