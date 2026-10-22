# CARD-1489 S1: read-only build-slot broker metrics

Source: `docs/investigations/2026-10-22-server2-build-slot-throughput.md` (Debug `17dd54d6`).
Its recommendation is to keep the operator's 4 slots / 6 MSBuild nodes / 16,384 MiB floor, remove
redundant builds, and make the broker measurable before any capacity experiment. The broker keeps
only current leases and waiters, so wait, hold and throughput can today only be reconstructed from
transcripts and executor logs.

No setting changes. FIFO, the memory floor, the lease lifetime, the wrappers, deployment and the
Hosts UI are untouched.

## Slice choice

The investigation's handoff (broker metrics plus build reuse) is larger than one 60-minute slice.
This task is **S1, broker metrics**: it is the investigation's own bounded Code task, it is
additive and read-only, and it is what any later budget or reuse decision is measured with.
Same-group build reuse in this sample is a manifest-authoring practice the existing `CP-n` build
reference already provides; making the checkpoint importer detect equivalent duplicate builds,
a cross-run immutable artifact cache, the Hosts-page local/shared broker mismatch and uncapped
nested/implicit builds are the remainder, filed as one Backlog card.

## Slices

- **S1-test (red, committed first):** new `tests/Antiphon.SessionRunner.Tests/BuildSlotMetricsTests.cs`
  with the loopback endpoint witness V-1 only. It reads JSON through `JsonDocument`, so it compiles
  without the new contract. Red: CP-1 at the S1-test commit, one narrow checkpoint run; expected
  assertion: `GET /build-slots/metrics` answered 404, not 200.
- **S1:** `BuildSlotMetricsRecorder` (bounded lifecycle records + per-minute refusal buckets, fed
  by `BuildSlotBroker` under its existing lock), contract records `BuildSlotMetrics` /
  `BuildSlotDurationStats`, `BuildSlotBroker.Metrics()`, `GET /build-slots/metrics`, V-2..V-5, the
  bare-host route assertion, and the two owner-doc lines (testing-and-build Build slots, ops-http).

Recorder rules: retain at most 3 hours and 4096 lifecycle records (grant, explicit release, reap,
silent-waiter drop); a grant is recorded once, at the new lease, so a retried acquire by the
holder adds nothing; reaps are not releases; refusal polls (busy, memory floor) are counted in
per-minute buckets, never as queued jobs or lifecycle records. Times come from `TimeProvider`.
The snapshot reports broker start, now, window start (the later of start and now-3h, or the oldest
retained record when capacity dropped records inside the window), `partialWindow`, `truncated`,
`droppedRecords`, counts, acquire-wait and held stats (count, mean, nearest-rank p95, max;
null when empty) and releases per hour. A read takes the lock but neither sweeps nor prunes.

## Verification design

- **V-1 `Queued_wait_and_completed_lease_are_counted_once`** (loopback HTTP, fake clock): one
  slot; a holder, a waiter refused twice, release at +45 s, the waiter granted (and re-acquiring
  its own lease), released at +165 s. The route answers 200 with grants 2, releases 2, reaps 0,
  busy refusals 2, wait mean 22.5 s / p95 45 s, held p95 120 s, partial window from T0.
- **V-2 `Retried_acquire_and_double_release_count_once_and_reaps_are_separate`**.
- **V-3 `Records_older_than_three_hours_leave_the_window`**.
- **V-4 `Capacity_drops_oldest_records_and_discloses_truncation`** (2100 grant/release cycles).
- **V-5 `Empty_and_partial_windows_and_reads_do_not_mutate_admission`**: a snapshot of a fresh
  broker is all zero with null stats; repeated reads are equal and leave a dead holder's lease
  unreaped until an explicit sweep.
- **R-1** `BuildSlotBrokerTests` (13) and `BuildSlotEndpointTests` (6, including the bare
  `BuildSlotsOnly` host, now also admitting `/build-slots/metrics` and still refusing
  `/sessions`).

### Checkpoints

Closed list. One checkpoint-tool run per committed group; the tool itself is bootstrapped with one
gated isolated build (`tools/Antiphon.Checkpoints -> bin-c1489tool/`), disclosed in the report.
CP-3 reuses CP-2's output at the same `After`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-test | `tests/Antiphon.SessionRunner.Tests -> bin-c1489-metrics-red/` | metrics-red | `/*/*/BuildSlotMetricsTests/Queued_wait_and_completed_lease_are_counted_once` | V-1 | 1 executed, 1 intended assertion failure (404), build succeeds | 1 | 8 |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1489-metrics-green/` | metrics-green | `/*/*/BuildSlotMetricsTests/*` | V-1..V-5 | 5 executed, 0 failed, 0 skipped | 5 | 8 |
| CP-3 | S1 | `CP-2` | admission-regression | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)/*` | R-1 | 19 executed, 0 failed, 0 skipped | 19 | 4 |

No Unit lane, `Antiphon.Tests` build or assembly run: the change is confined to the session
runner and its contracts, and the brief asks for minimal testing. The `Antiphon.Tests` Unit lane
is a stated residual, not a pass.

### Cost and mutation

Authoring 25-35 minutes, checkpoint rows about 20 minutes plus slot wait. Post-land method-scoped
SourceLanding Mutation candidates, none executed here:

- **PC-1:** record a grant on every successful acquire (including the holder's retry); V-1 and
  V-2 must go red.
- **PC-2:** count a reap as a release; V-2 must go red.
- **PC-3:** drop the 3-hour cutoff from the snapshot filter; V-3 must go red.
- **PC-4:** drop the capacity bound (or stop disclosing truncation); V-4 must go red.
- **PC-5:** sweep inside `Metrics()`; V-5 must go red.
