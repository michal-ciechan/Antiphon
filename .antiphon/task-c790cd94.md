# PhoneHome round 2 — c790cd94

Outcome: fixed. Output observation no longer waits on the mention worker. A completed mention line records whether the source was live at that moment, and the route accepts a source that has since stopped when that stamp matches the same generation, card, and standing agent. Transcript confirmation runs off the worker, so a held confirm cannot block the next observe or the next mention.

Branch `feat/card-task-c790cd94`, fast-forward from `23d77eedacb0464bfe3307e7ec2d88ed0465bc2e`.

## Why the flush moved off the worker

A liveness stamp alone still leaves the second command behind the first mention's transcript confirm. That confirm waits up to `TranscriptConfirmTimeoutSeconds` (default 30) on `AgentMentionRouter`'s single reader. The second mention cannot be inserted while the first transcript never arrives, which is the held-flush witness. Publish stays on the worker. Only `FlushIfIdleAsync` moves to `FlushDetachedAsync`, cancelled by the router token. Public `RouteMentionAsync` still flushes inline (`observed: null`, `deferFlush: false`), so direct callers such as `PhoneHomeOutageHarness` are unchanged.

## Behaviour

- `ObserveOutputAsync` awaits a short status read (`ReadMentionLivenessAsync`) and enqueues before it returns. That read is not a wait on the worker. `ObserveDelta` stays fire-and-forget with a null stamp, so the old Starting/Running filter still applies. A debounced incomplete line is also unstamped.
- Stamp: `MentionSourceLiveness(StartedAt, CardId, StandingAgentId, WasLive)`. `WasLive` is true only for Starting or Running. A capture failure is logged and the command is still enqueued unstamped. Cancellation is rethrown.
- Stamped and `WasLive`: load the source with no status filter. A missing row is `ok=false reason=source-missing`. A generation, card, or standing-agent mismatch is `ok=false reason=other-agent` (`SessionGeneration.Equal` on `StartedAt`). A matching Stopped, Stopping, or Failed source is accepted.
- Unstamped or `WasLive` false: the existing Starting/Running filter. Output replayed after stop does not route.
- Queue: `FullMode.Wait` kept. `TryWrite` refuses the new command when the channel is full (1024) and logs `CommandDropped`. Wait applies only to `WriteAsync`.
- Dispose is idempotent. It cancels, completes the channel, waits up to 2s for the worker, and does not wait on flushes. `OperationCanceledException` and `ObjectDisposedException` during shutdown are swallowed.

## Not fixed

An operator stop or a reconciliation close that does not come through the observer is pre-existing. If that stop commits before the liveness read, the stamp is not live and the route still refuses. If it commits after the stamp, the stamp accepts it. That acceptance is incidental, not a separate fix.

## Commits

| SHA | Subject |
|---|---|
| `687a378a23c57c765c1a49528f4d9a96e52e2685` | test(phone-home): a held transcript confirm blocks the next observe |
| `56f64a2bf49d74246cf5cae3c4c993dc2b3baa67` | fix(phone-home): stamp source liveness at observe time and flush off the worker |
| `34bd3596c766f9fd73055aa74f4e43f6727778bc` | test(phone-home): wait for the committed UserPrompt after a deferred flush |

Files: `server/Application/Services/AgentMentionRouter.cs`, `AgentChannelService.cs`, `AgentSessionRuntime.cs`, `tests/Antiphon.Tests/Application/AgentChannelServiceIntegrationTests.cs`.

The red commit failed for the intended reasons against the round-1 product: the next observe was still blocked after 5s, dispose left the observer waiting, and a generation change after the stamp window still inserted the row. There is no dedicated `AgentMentionRouter` test class. The router is covered by the phone-home and channel tests.

## Witnesses (tip `34bd3596c`, `tests/Antiphon.Tests/bin-c790`, Linux)

Build lease `eb8e879c-b9ea-45a1-a442-c2cead8b805a`, `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c790/ --property:UseAppHost=false`, 0 errors. Later test-only rebuild lease `16332747-ad59-4487-9e29-fd7cee6430d9`, 0 errors.

- `AgentChannelServiceIntegrationTests` lease `abccfd2b-68bf-4b8e-8f31-8d8498f99115`: 9/9. `Held_flush_does_not_block_the_next_observe_or_drop_the_second_mention` 1.890s (pump bound 5s, Delivered bound 8s, total bound 20s). `ChannelHub_mention_routes_to_target_session_stdin` 6.383s. `Queued_commands_at_dispose_do_not_leave_the_observer_blocked` 51ms. `Route_refuses_a_source_whose_generation_changed_after_the_line` 96ms. Split-chunk mention once. The held targets are ClaudeCode with dialog observation and no `OnSubmitted`. The harness registers `SessionStateStore` so the injected UserPrompt persists and the confirm can see it. ChannelHub waits until that UserPrompt row is committed; the exact body assertion is unchanged. Route return now precedes the deferred flush, so waiting only on route-return or on `SubmittedBodies` races `OnSubmitted`'s save.
- `PhoneHomeBackpressureTests` alone, lease `32b877ea-64d2-483c-9ed9-dd59083c0de2`: 7/7 in 24.5s, including `Exit_after_split_mention_still_queues_the_line_once` and `Finite_stall_preserves_connection_order_and_delivery`.
- Same class, 5 runs under 8 bash busy-loops (measured 88–99% CPU, 24 cores), lease `e36aea2d-cf76-4ad5-8e12-afc894c30ba0`: every run 7/7 (37.5s, 32.6s, 23.3s, 20.8s, 28.7s). An earlier attempt used `python3`, which is not installed, so those five passes were unloaded and are not the burner evidence.
- Combined filter `/*/*/(PhoneHome*)|(AgentChannelServiceIntegrationTests*)|(AgentSessionRuntime*)|(RcRosterRepairTests*)|(C1514LandingSplitBudgetTests*)/*`, `--maximum-parallel-tests 4`, lease `56084ea4-79a9-4e9b-8654-10f1e3a1ae4d`: total 340, succeeded 334, failed 6, duration 6m 43s. The census cases C1404, C1577, C1584, and C1410 live in `C1514LandingSplitBudgetTests` and are inside that filter.

## Inherited failures

All 6 failures are `PhoneHomeMirrorJoinTests.C1032_Join*`. They do not use mentions or output. Round 1 reproduced the same six at base.

- `C1032_JoinIdleRecipient` (617ms): `BindingId` empty. Detail: the runner could not mirror the task branch (`delegate_branch_identity_mismatch`); the task stays Queued.
- `C1032_JoinStaleBinding` (631ms): warnings do not contain `stale_mirror_binding`.
- `C1032_JoinForeignOwner` (557ms): warnings do not contain `foreign_mirror_owner`.
- `C1032_JoinStoreMismatch` (686ms): warnings do not contain `runner_store_mismatch`.
- `C1032_JoinBusyRecipient` (2m 00s): timeout.
- `C1032_JoinCrashRetry` (2m 00s): timeout.

## Evidence

`scripts/check-evidence-diff.ps1` against `23d77eedacb0464bfe3307e7ec2d88ed0465bc2e..34bd3596c`: commits=3, entries=0, violations=0. Against the brief base `34a7d0b8f5e1ff2aaab6e1fa178258c4f29c8ba8`: exit 2, `base_not_ancestor`. That commit is not an ancestor of this branch. The branch was not rebased onto it.

`bin-c790` directories from this build were removed. No tags, no publish, no rebase, no force-push. No native tests.
