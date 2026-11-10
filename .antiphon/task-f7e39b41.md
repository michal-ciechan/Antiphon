# Review f7e39b41: phone-home byte window during held catch-up

Outcome: CLEAN. Final round 1 of Debug `4b4da169-510d-4574-b1fa-33a9f0a15ee7` at `bc2d6b4751be7b98f965151a01963189f26d7327`. No blocker. Two advisories below do not hold the land. Positive controls stay pending; this repair has no PC rows.

Subject branch `feat/card-task-4b4da169`. Reviewed range `f125755c1648533c473ce46311c307788b4a9b65..bc2d6b4751be7b98f965151a01963189f26d7327` (red `15aa81dc1b1fcc45bb68305b64e4aa24d773fdb1`, fix `71c195e4e4e1fcbbb02fd70d22bd7c1df9dcc02e`, report `bc2d6b4751be7b98f965151a01963189f26d7327`). Card CARD-1514 (`c92555be-65d0-45b6-a759-3d1ba5938bd2`). This repair has no new `### Checkpoints` table. The required selection is the named classes below, evidence-diff, and merge-tree.

## Product gate

`PhoneHomeConnectionService.BeforeEventSubscriptionForTest` is an `internal Task?` set only by `PhoneHomeHostedRunner.CreateAsync`. `InternalsVisibleTo` for `Antiphon.Tests` and `Antiphon.SessionRunner.Tests` is already on `Antiphon.SessionRunner.csproj`. Nothing in production assigns it. It is not a settings property.

`RunConnectedAsync` awaits it only when the property is non-null, and that wait uses the connection token. When it is null the method does not await, takes no new lock, and continues into the same window, epoch, subscribe, heartbeat, and event-loop sequence. Default behavior is unchanged. A held task would delay subscribe and the first heartbeat on that instance until the task completes or the connection token cancels. Production never sets the property, so a forgotten test gate cannot hang a production connect.

The hosted helper's new argument defaults to null. Existing callers do not hold the gate.

## Diagnosis

The Windows failure is the fixture. `holdCatchup: true` returns from `CreateAsync` when the server accepts the socket, which is before `SubscribeBounded`. At `15aa81dc1` `ByteWindowDuringCatchupAsync` called `Output` immediately. `SessionRunnerEventHub.Publish` copies the subscriber list and delivers only to that snapshot, so an empty list drops the event. `PendingEvents` then stays 0 for the whole `PhoneHomeSignalWait`. The red helper has no pressure wait. The fix waits until `ProducerPressure` is set.

`ProducerPressure` is assigned from the first heartbeat payload (`PhoneHomeLiveConnection` receive loop). `HeartbeatLoopAsync` sends that heartbeat as soon as it starts, which is after `SubscribeBounded`. If it never arrives, `PhoneHomeHostedRunner.UntilAsync` fails at 20s with `sender subscribed before the held catch-up publish`. That is a bounded failure, not a hang, and it does not hide a dropped publish: the publish is after the wait.

`Early_publish_is_dropped_and_the_byte_cap_admits_one_windows_shaped_payload` pins the hub drop and the 111-byte measure. A Windows-shaped path in the same text is larger than the Linux twin and still fits a cap of `2 * linux - 1`. The 2N-1 cap is not the miss.

The same hub rule is a pre-existing production window, not what this test was failing on. Session loops publish to the hub whenever they emit. Phone-home subscribes only inside `RunConnectedAsync`, after the socket is already accepted. Catch-up (`PhoneHomeRecoveryPump.CatchUpAsync`) lists sessions and pulls transcripts; it does not replay hub events. `ObserveOutputAsync` (mentions and `AgentTextDelta`) and `ObserveExitAsync` run from the live pump only. A `SessionOutput` or exit published with no subscriber is not reconstructed. Stored transcript entries are recovered by the transcript RPC even if the hub event was dropped. With the gate left null, the accept-to-subscribe gap is the synchronous prefix of `RunConnectedAsync` plus the existing disconnected interval. This change does not widen that gap.

## Seam and duration

`Held_catchup_admits_the_byte_window_when_subscribe_is_late` holds the gate, observes null pressure and `PendingEvents == 0`, then releases it. At `15aa81dc1` the helper published before that release, which is the drop. After `71c195e4e` it waits for pressure first. Removing that wait returns the red.

Fresh durations at `bc2d6b475` (TRX `.antiphon/review-f7e39b41/named.trx`):

| Test | Duration |
|---|---|
| Early_publish_is_dropped_and_the_byte_cap_admits_one_windows_shaped_payload | 0.716s |
| End_snapshot_distinguishes_pending_from_received | 5.937s |
| Slow_observer_is_attributed_without_payload | 21.400s |
| Held_catchup_admits_the_byte_window_when_subscribe_is_late | 21.419s |
| Sustained_overload_recovers_durable_transcript_once | 21.431s |
| Exit_after_split_mention_still_queues_the_line_once | 22.210s |
| Finite_stall_preserves_connection_order_and_delivery | 25.986s |
| Slow_session_does_not_block_another_session | 26.039s |
| Window_limits_epoch_and_legacy_peers | 26.365s |

`Finite_stall` passed at 26.0s (Debug reported 23.7s). `Held_catchup` at 21.4s matches `Slow_observer`, which does not use the new wait. `PhoneHomeSignalWait` is a 20s ceiling on each `UntilAsync`. A ceiling that expires fails the test. This run passed, so the old dropped-event poll is not burning that 20s. The class's other hosted tests sit in the same 21-26s band.

No OS-selected production branch was added. The payload comparison is portable and ran here. A Windows re-run of `Finite_stall` stays with the rc qualification. No startup-impact repair: the null gate does not change connect order.

## Verification

Build lease `8dad29f9-9e8b-4809-80a7-afad0cea7c96` (waited 150s, held 385s). `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`, native output, 0 errors.

Test lease `94e652ed-0fb6-4dc1-af40-116e36f95869` (waited 15s). `--no-build`, same property. Filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(ProcessSpawnLimitTests*)|(PhoneHomeBackpressureTests*)/*`. HEAD `bc2d6b4751be7b98f965151a01963189f26d7327`. 144 passed, 0 failed, 0 skipped, 2m 31s.

| Class | Passed |
|---|---|
| TestClassificationPolicyTests | 41 |
| TestClassificationGuardTests | 1 |
| SlowTestRegistryMergeTests | 5 |
| SlowTestTripwireTests | 2 |
| NativeLinuxPolicyWitnessTests | 3 |
| MechanicalPcContractTests | 11 |
| TaskPlatformGuidanceTests | 5 |
| C1729LayeringGuardTests | 9 |
| TestLaneCategoryGuardTests | 1 |
| ProcessSpawnLimitTests | 57 |
| PhoneHomeBackpressureTests | 9 |

`scripts/impacted-classes.ps1 -Range f125755c1..bc2d6b475` selected only `test-lane-category` / `TestLaneCategoryGuardTests` (included above, 1 passed). `windowsFilters` empty. The migrations row did not match, so the downgrade selector did not run (caller count 0).

`scripts/check-evidence-diff.ps1` on that range: commits=3, entries=1, violations=0.

`git merge-tree --write-tree origin/master bc2d6b475` against `8cae27e99ac1ebfaf5e062c1f8f4c4339e8dfe0c` (descendant of `a8d830d12f0258a8f133364e58b8de4eb25d77bd`; merge-base still `f125755c1648533c473ce46311c307788b4a9b65`) exited 0, tree `7b6b50b74d62921e678d23731838bfab2bf1bf7c`. Not merged.

Worktree clean at the reviewed SHA aside from gitignored TRX. `reviewedSourceClean` true.

Why-missed note `docs/investigations/2026-11-10-why-missed-phone-home-backpressure-g75.md` carries the five CARD-1781 phrases. The proposed `phone-home-byte-window-subscribe` row was not applied. `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`.

## Advisories

1. Live hub publishes with no subscriber are dropped, and catch-up does not replay them. Transcript RPC recovers stored transcript rows. Mentions, text deltas, and exits that exist only as hub events during disconnect or the accept-to-subscribe gap are not reconstructed. Pre-existing. This fixture fix does not close it. File a card if that live gap should be replayed. It does not hold this land.

2. In `Early_publish_is_dropped_and_the_byte_cap_admits_one_windows_shaped_payload`, `admitted <= 2 * admitted - 1` and `2 * admitted > 2 * admitted - 1` (and the same CRLF form) are true for every positive size. The method can still fail: the hub drop, `Admitted == 111`, prepared-versus-admitted, and the Windows path versus the Linux twin are real checks.

## Land

Land Debug owner `4b4da169-510d-4574-b1fa-33a9f0a15ee7` with `-ExpectedSourceSha bc2d6b4751be7b98f965151a01963189f26d7327`. PCs remain pending for a later SourceLanding pass; none are defined on this repair.
