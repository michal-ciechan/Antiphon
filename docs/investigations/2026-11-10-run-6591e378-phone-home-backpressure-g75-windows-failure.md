# Run 6591e378 group 75: PhoneHomeBackpressureTests Windows failure

Read-only extract of the one failed test in Windows chunk `antiphon-all-group-75`. No process, lock, or qualification file was changed.

## Run

| Field | Value |
|---|---|
| Run | `6591e378` (`C487_INVOCATION=6591e378c04d47bc898cf0927352b01b`) |
| Line | `rc/2026-11-15` = `ac6d00022e940ee3b90584c7d313567b4491c498` |
| Source the stack names | `C:\Antiphon\releases\checkout-4dd2a426` (HEAD is that same SHA) |
| Host | `DESKTOP-KTLKPIF`, Windows 10.0.19045, win-x64, .NET 9.0.22, TUnit 1.44.0.0 |
| Log directory | `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378` |
| Chunk | `antiphon-all-group-75` |
| Assembly | `tests\Antiphon.Tests\bin\Debug\net9.0\Antiphon.Tests.dll` |

Files matching `antiphon-all-group-75*` (directory listing was filtered; the parent log folder was not enumerated):

| Name | Bytes | Role |
|---|---|---|
| `antiphon-all-group-75.trx` | 447397 | Results. One `Failed` result. |
| `antiphon-all-group-75-tests.log` | 4545 | TUnit console summary. No separate `*console*` file shares the group-75 prefix. |
| `antiphon-all-group-75.timing.json` | 2366 | Chunk timing. |
| `antiphon-all-group-75-execution\log_261110154544461.diag` | 869341 | TUnit trace. The failure node repeats the TRX message. |

`tests.log` also names `Antiphon.Tests-windows-net9.0-report.html` in the same log directory. That name is outside the group-75 filter, so it was not opened. The assertion text is in the TRX and `tests.log`.

## Chunk result

TRX `ResultSummary/Counters`: total 241, executed 241, passed 240, failed 1, error 0, timeout 0, aborted 0, inconclusive 0.

`Times`: start `2026-11-10T15:45:44.6912246Z`, finish `2026-11-10T15:52:13.4255609Z`.

`tests.log`: `Failed!`, duration `6m 28s 801ms`, failed 1, succeeded 240, skipped 0.

`timing.json`: `lastPhase=complete`, `queueWaitSeconds=0`, `buildSeconds=655.862`, `buildSlot=not-used`, `runWallSeconds=390.322`, `fixtureSetupSeconds=4.687`, `testCount=241`, `p50Seconds=2.247`, `p95Seconds=12.599`, `maxSeconds=32.297`. The max is this failure.

## The one failure

`Antiphon.Tests.Application.PhoneHomeBackpressureTests.Finite_stall_preserves_connection_order_and_delivery`

| Field | Value |
|---|---|
| Outcome | Failed |
| Duration | `00:00:32.2973072` (32s 297ms) |
| Start | `2026-11-10T15:45:59.3375703+00:00` |
| End | `2026-11-10T15:46:31.6348775+00:00` |
| executionId | `f0123097-5c6b-4a42-8e75-0910b3b2c39b` |
| testId | `63fc8276-bcfc-f50b-9b8c-1dba1be1392c` |
| Category | Integration |
| StdErr | No `StdErr` node on this result |
| StdOut | Only `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.PhoneHomeBackpressureTests.1.1.Finite_stall_preserves_connection_order_and_delivery.1.1.0` |

### Assertion

TRX `ErrorInfo/Message` and `tests.log` (same text):

```text
ShouldAssertException: condition()
    should be
True
    but was
False

Additional Info:
    first byte-window event admitted during catch-up
```

Expected: the `UntilAsync` predicate is true. That predicate is `h.Live.PendingEvents == 1`.

Actual: the predicate is still false when the wait ends. The result does not record the observed `PendingEvents` value. Shouldly reports the boolean, and the additional info is the wait's `because` string, not a compared count.

### Stack, top frames

From the TRX `StackTrace` (runtime frames omitted here; the TRX keeps them):

1. `PhoneHomeHostedRunner.UntilAsync` at `tests\Antiphon.Tests\TestHelpers\PhoneHomeHostedRunner.cs:185` — `condition().ShouldBeTrue(because)` after the wait loop.
2. `PhoneHomeBackpressureTests.ByteWindowDuringCatchupAsync` at `tests\Antiphon.Tests\Application\PhoneHomeBackpressureTests.Bounds.cs:24` — the failing wait.
3. `ByteWindowDuringCatchupAsync` at `Bounds.cs:39` — second async frame of the same method. The message matches only the `because` string on line 24. Line 40's text-sequence `ShouldBe` did not run; a mismatch there would be a collection comparison, not `True`/`False`.
4. `Finite_stall_preserves_connection_order_and_delivery` at `tests\Antiphon.Tests\Application\PhoneHomeBackpressureTests.cs:83` — `await ByteWindowDuringCatchupAsync()` after the method's `try`/`finally`.

The diag node `ErrorTestNodeStateProperty` repeats this message and stack. It adds no stderr and no other exception.

### What line 83 means

`Finite_stall_preserves_connection_order_and_delivery` runs its eight-event stall in a `try`/`finally`, then calls `ByteWindowDuringCatchupAsync`. Reaching line 83 means that `try` returned. StdOut has no `C1523 RED endpoint reason=` line, which is written only when that body takes the closed-socket branch. The finite-stall order, credit, heartbeat, durable prompt, exit, drain, and split-mention checks in that `try` are not the failing assertion.

`ByteWindowDuringCatchupAsync` then builds a second hosted runner and fails on its first poll:

```csharp
const string text = "é😀日本語";
var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new RunnerOutputEvent(Guid.Empty, 1, text)));
await using var h = await PhoneHomeHostedRunner.CreateAsync(count: 8, bytes: 2 * bytes - 1, holdCatchup: true);
h.Live.DispatchEligible.ShouldBeFalse();
h.Output(h.SessionId, 1, text);
await PhoneHomeHostedRunner.UntilAsync(() => h.Live.PendingEvents == 1,
    "first byte-window event admitted during catch-up");
```

`holdCatchup: true` sets `PhoneHomeRecoveryPump.CatchUpHold` to `RecoveryHold`. `CatchUpAsync` awaits that hold before List/transcript, and `StartPump` runs only after catch-up returns. The poll does not release `RecoveryHold`. `PendingEvents` increments in `PhoneHomeLiveConnection.ReceiveLoopCoreAsync` when an event frame is admitted under the count and byte caps. The observer pump is not what the poll waits on.

`UntilAsync` (`PhoneHomeHostedRunner.cs` 180–185):

```csharp
using var timeout = new CancellationTokenSource(LoadTimeoutBounds.PhoneHomeSignalWait);
while (!condition() && !timeout.IsCancellationRequested)
    try { await Task.Delay(10, timeout.Token); } catch (OperationCanceledException) { }
condition().ShouldBeTrue(because);
```

`LoadTimeoutBounds.PhoneHomeSignalWait` is 20 seconds. The comment on that field, at this SHA, says measured whole-test passes were 7–19 seconds, so 20 seconds covers that latency, and a longer cap only slows the known `Finite_stall` red.

The test lasted 32.297 seconds. A full 20 second cap after a body of about 12.3 seconds fits that duration and sits with the passing siblings (9.6–17.8 seconds). The logs have no inner phase timestamp, so the 12.3 second split is inferred from the cap plus the total.

The first hosted runner is an `await using` that is still alive at line 83, so the catch-up runner starts while that runner has not been disposed.

## Classification

Timing/load poll expiry, not an assertion on a returned value.

Evidence:

- The thrown check is `condition().ShouldBeTrue` after `CancellationTokenSource(PhoneHomeSignalWait)`.
- Expected `True`, actual `False`, additional info is the wait reason. There is no expected-versus-actual count, status, or payload in the message.
- The harness counters record `timeout=0`. TUnit did not time the test out. The test failed its own 20 second poll.
- No sharing-violation text, no `StdErr` node, and the diag failure node is this same `ShouldAssertException`.
- `LoadTimeoutBounds` at `ac6d00022e94` already calls this the known `Finite_stall` red and says lengthening the cap only makes that red slower. Debug should not treat a larger `PhoneHomeSignalWait` as the repair.
- This extract is one run. It does not re-execute the test.

## Passing siblings

Same class, same chunk, all passed. They bound the hosted runner, catch-up, credit, overflow, and byte accounting. `ByteWindowDuringCatchupAsync` is called only from the failed test.

| Test | Duration | Start (UTC) | End (UTC) |
|---|---|---|---|
| `End_snapshot_distinguishes_pending_from_received` | 3.383s | 15:45:59.344 | 15:46:02.727 |
| `Slow_observer_is_attributed_without_payload` | 10.820s | 15:45:57.920 | 15:46:08.740 |
| `Sustained_overload_recovers_durable_transcript_once` | 9.602s | 15:45:59.344 | 15:46:08.946 |
| `Exit_after_split_mention_still_queues_the_line_once` | 10.207s | 15:45:59.339 | 15:46:09.546 |
| `Window_limits_epoch_and_legacy_peers` | 17.791s | 15:45:57.765 | 15:46:15.556 |
| `Slow_session_does_not_block_another_session` | 16.611s | 15:45:59.360 | 15:46:15.971 |

`Sustained_overload_recovers_durable_transcript_once` also assigns `CatchUpHold` and passes its reconnect path. `End_snapshot_distinguishes_pending_from_received` reaches `AccountingBoundsAsync`. `Window_limits_epoch_and_legacy_peers` reaches the legacy window and ticket-binding helpers. `Slow_session_does_not_block_another_session` reaches the ownership, worker-bound, and replacement helpers. None of those is the failing poll.

## What ran at the same time

67 other results overlap `15:45:59.338Z`–`15:46:31.635Z`. All 67 passed. The six siblings above overlap the start of `Finite_stall` and finish by `15:46:15.971Z`. If the failing poll is the last 20 seconds (`~15:46:11.6Z`–`15:46:31.6Z`), those siblings are done for most of the wait. The rest of the overlap is other Integration classes.

| Class | Overlapping results | All passed |
|---|---|---|
| `RoutingPinCandidateDispatchTests` | 13 | yes |
| `SessionStateWarmupTests` | 16 | yes |
| `SeatOccupancyAttentionTests` | 9 | yes |
| `AgentTaskLandTargetRaceFakeTests` | 6 | yes |
| `ChannelInboundRecoveryTests` | 6 | yes |
| `PhoneHomeBackpressureTests` | 6 (the siblings) | yes |
| `DispatchConcurrencyAdmissionTests` | 4 | yes |
| `RunnerCompletionProgressTests` | 3 | yes |

Names, start–end UTC, duration:

- `C711_LegacyAdvancedRowsBecomeTerminal(remote)` 15:45:57.765–15:46:18.360 20.595s
- `Window_limits_epoch_and_legacy_peers` 15:45:57.765–15:46:15.556 17.791s
- `C711_LegacyAdvancedRowsBecomeTerminal(push-rejected)` 15:45:57.765–15:46:07.123 9.359s
- `C711_LegacyAdvancedRowsBecomeTerminal(branch)` 15:45:57.765–15:46:18.258 20.494s
- `C711_RetryRestartsTheProgressClock` 15:45:57.765–15:46:18.256 20.491s
- `C711_RaceRetriesInTheFakeHarness` 15:45:57.765–15:46:18.316 20.551s
- `C711_NonRaceRefusalIsNotReplacedBySameRequest` 15:45:57.765–15:46:11.545 13.780s
- `C767_Stop_AwaitsCancelledGroupsAndRestartRecovers` 15:45:57.765–15:46:17.086 19.321s
- `Slow_observer_is_attributed_without_payload` 15:45:57.920–15:46:08.740 10.820s
- `Single_candidate_Required_is_Held_and_never_rerouted` 15:45:59.337–15:46:07.199 7.862s
- `Exit_after_split_mention_still_queues_the_line_once` 15:45:59.338–15:46:09.546 10.207s
- `End_snapshot_distinguishes_pending_from_received` 15:45:59.343–15:46:02.727 3.383s
- `Sustained_overload_recovers_durable_transcript_once` 15:45:59.344–15:46:08.946 9.602s
- `Slow_session_does_not_block_another_session` 15:45:59.360–15:46:15.971 16.611s
- `Three_blocked_Plan_pin_tasks_are_one_attention_row` 15:45:59.360–15:46:07.016 7.656s
- `Wall_rewalk_stays_inside_the_original_explicit_kind` 15:45:59.367–15:46:02.772 3.404s
- `Wall_on_a_walked_pin_task_requeues_on_the_next_candidate` 15:45:59.372–15:46:03.570 4.198s
- `Pin_plus_chain_blocked_tasks_group_under_pin_plus_chain_key` 15:45:59.374–15:46:07.018 7.644s
- `Reroute_nulls_RoutingPinId` 15:45:59.391–15:46:02.730 3.339s
- `Hold_cleared_resumes_a_routing_blocked_pin_task` 15:45:59.397–15:46:09.511 10.114s
- `All_held_blocks_a_queued_walked_task` 15:45:59.398–15:46:07.019 7.621s
- `C527_queued_commit_child_rewalks_to_the_next_pin_candidate_at_dispatch` 15:45:59.401–15:46:09.628 10.227s
- `Queued_walked_task_rewalks_to_the_next_pin_candidate_at_dispatch` 15:45:59.402–15:46:09.559 10.157s
- `Explicit_kind_rewalk_stays_inside_the_original_ask` 15:45:59.402–15:46:06.739 7.338s
- `C1805_UnfetchedGateTreatsARepositoryErrorAsUnverified` 15:45:59.405–15:46:14.031 14.626s
- `Project_and_role_queue_boundaries_refuse_without_insert` 15:45:59.407–15:46:17.690 18.283s
- `Cleared_pin_keeps_the_snapshot_and_Holds` 15:45:59.412–15:46:07.215 7.802s
- `C1079_Disabled_watch_hides_seat_idle_divergence_and_orphan_rows` 15:46:07.019–15:46:11.416 4.397s
- `C1079_Summary_counts_the_three_kinds_open` 15:46:07.023–15:46:11.449 4.427s
- `C1079_No_snapshot_or_unavailable_inventory_yields_no_seat_rows` 15:46:07.023–15:46:14.581 7.558s
- `C1124_Seat_evidence_names_the_park_state` 15:46:07.126–15:46:10.354 3.228s
- `C1079_Slot_orphan_is_immediate_for_occupying_orphans_only` 15:46:07.200–15:46:10.261 3.061s
- `C1079_Seat_idle_clears_when_the_bound_task_is_working_again` 15:46:07.216–15:46:11.554 4.338s
- `C1079_Seat_idle_becomes_error_at_the_error_age` 15:46:08.740–15:46:14.768 6.028s
- `C1079_Seat_idle_warning_names_runner_session_task_status_age_and_pushed` 15:46:08.946–15:46:13.649 4.702s
- `C1079_Seat_idle_is_absent_below_the_warning_age` 15:46:09.512–15:46:13.916 4.404s
- `C1079_Occupancy_divergence_is_one_row_per_host_and_needs_an_idle_seat` 15:46:09.546–15:46:14.302 4.756s
- `Warmup_pins_a_terminal_session_owing_a_channel_reply` 15:46:09.560–15:46:15.212 5.652s
- `Cascade_delete_does_not_resurrect_a_committed_ingest` 15:46:09.630–15:46:16.989 7.359s
- `Ambiguous_project_commit_reloads_instead_of_keeping_working` 15:46:10.261–15:46:17.207 6.945s
- `Rolled_back_board_delete_leaves_the_cached_snapshot` 15:46:10.355–15:46:16.882 6.527s
- `Retention_prune_drops_cached_working_state` 15:46:11.417–15:46:16.556 5.139s
- `Committed_project_delete_evicts_the_cached_working_entry` 15:46:11.450–15:46:17.085 5.635s
- `Committed_board_delete_evicts_the_cached_working_entry` 15:46:11.545–15:46:16.963 5.418s
- `Rollback_switch_restores_SQL_reads_but_keeps_committed_ingestion` 15:46:11.555–15:46:15.863 4.309s
- `Cancelled_waiter_does_not_cancel_or_poison_the_shared_load` 15:46:13.650–15:46:14.888 1.238s
- `Terminal_eviction_preserves_active_and_waiting_gates` 15:46:13.917–15:46:15.715 1.798s
- `Retention_and_session_delete_reseed_before_returning` 15:46:14.032–15:46:16.635 2.603s
- `C1082_PendingEqualTipIsNoPushedProgress` 15:46:14.033–15:46:18.954 4.921s
- `Failed_load_throttles_from_completion_then_retries_without_caching_idle` 15:46:14.302–15:46:15.749 1.447s
- `Delayed_seed_cannot_overwrite_an_arriving_ingest` 15:46:14.581–15:46:16.249 1.668s
- `New_container_rebuilds_committed_state_with_a_new_epoch` 15:46:14.774–15:46:16.250 1.475s
- `Concurrent_cold_readers_share_one_bounded_statement` 15:46:14.889–15:46:16.207 1.318s
- `Capacity_pressure_uses_correct_uncached_reads_without_evicting_active_state` 15:46:15.213–15:46:16.657 1.444s
- `C1716_BusyPass_PublishesHeartbeatAndPreventsPrematureStall` 15:46:17.092–15:46:19.108 2.016s
- `Other_project_and_null_bucket_do_not_interfere` 15:46:17.691–15:46:20.163 2.472s
- `Persisted_remote_evidence_round_trips` 15:46:18.955–15:46:19.003 0.049s
- `C1082_PendingWithLocalObjectsAttributesClaimAgainstObservedTip` 15:46:19.004–15:46:25.568 6.563s
- `C1716_HostedWorker_RetriesBusyAtFiveSecondsThenUsesIdleTick` 15:46:19.109–15:46:22.095 2.985s
- `Unconfigured_create_keeps_legacy_decisions` 15:46:20.164–15:46:24.235 4.071s
- `C1716_HostedService_StartsIndependentStallCheckerAndRearmsAfterRecovery` 15:46:22.095–15:46:24.010 1.914s
- `C1716_HeldQueueLock_OtherSessionQueuedWithinOnePass(enqueue)` 15:46:24.010–15:46:25.873 1.862s
- `Global_and_project_put_change_real_create` 15:46:24.236–15:46:30.394 6.158s
- `C1082_PendingWithoutLocalObjectsIsIndeterminate` 15:46:25.569–15:46:32.692 7.123s
- `C768_HoldIncidentsUseExistingAttentionAndPaging` 15:46:25.874–15:46:30.767 4.893s
- `Override_only_bypasses_legacy_open` 15:46:30.395–15:46:33.700 3.304s
- `C1716_StalledWakeWorker_RaisesOneChannelIncidentPerStall` 15:46:30.768–15:46:32.994 2.226s

## Cause for Debug

`Finite_stall_preserves_connection_order_and_delivery` got through its own stall body, then `ByteWindowDuringCatchupAsync` published one non-ASCII `SessionOutput` while catch-up was held (`DispatchEligible` still false, `RecoveryHold` not completed). `Live.PendingEvents` never became 1 within the 20 second `PhoneHomeSignalWait`, and `UntilAsync` asserted `true`.

Admission of that count happens in the server receive loop, not in the observer pump. The pump does not start until the catch-up hold completes, which this poll never releases. The byte cap for this runner is `2 * UTF-8(Json(RunnerOutputEvent)) - 1`, so one measured payload of that size fits and a second does not. This run does not say whether the frame never arrived, was dropped before the receive loop, or was rejected without incrementing `PendingEvents`. It does say the predicate was still false when the known 20 second cap elapsed.

The six other `PhoneHomeBackpressureTests` methods passed in this chunk, including catch-up hold on reconnect and the byte-accounting helper. The red is confined to this trailing poll.

CARD-1781: the Debug task for this new cause owes the why-missed note (`docs/investigations/<date>-why-missed-<cause>.md` from `docs/investigations/why-missed-template.md`) plus a `WHY-MISSED` section in its done report. This read does not write that note.

## Handoff

- next: debug
- Do not widen `PhoneHomeSignalWait` to hide this red. The bound's own comment says a longer cap only slows it.
- Repair target: why the first event in `ByteWindowDuringCatchupAsync` is not admitted (`PendingEvents == 1`) while `holdCatchup` is set, before `RecoveryHold` is released.
- Class filter: `/*/*/PhoneHomeBackpressureTests/Finite_stall_preserves_connection_order_and_delivery`
- Passing siblings in this chunk already cover the other six methods of the class.
