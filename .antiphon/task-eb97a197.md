# PhoneHome round 3 — debounce double-route (D-1)

Outcome: the completed-line mention is claimed under the pending-debounce gate before the liveness read. A split line queues once. A failed or not-live read still delivers that one full mention. Pushed on `feat/card-task-eb97a197`.

Base: `1423c937e558bee37bde13434b25b8a404b37d77` (`origin/feat/card-task-c790cd94`).
Review that named D-1: `8b652b4ea` on `origin/feat/card-task-7e117ffd`.

## Commits

- `6bf10da79b8b2ad15b3f774df8b8f6a239115f45` — test(phone-home): hold the liveness read across the pending debounce.
- `261e35c8247ce59cba7cc4ec95b5a29fe534db1d` — fix(phone-home): claim a completed mention before the liveness read.

Files: `server/Application/Services/AgentMentionRouter.cs`, `tests/Antiphon.Tests/Application/AgentChannelServiceIntegrationTests.cs`.

## Fix

`ObserveCompletedAsync` collects the completed-line mentions, then `ClaimCompletedMentions` runs before `await capture`. Under `state.Gate` the claim increments `state.Version` and `RoutedKeys.Add`s each ready target. Only mentions whose add succeeded are enqueued, and they are enqueued after the read with whatever stamp came back.

A null stamp (the read threw) or `WasLive=false` still enqueues that claim. `RouteMentionAsync` then uses the existing Starting/Running status filter. A stopped source that was never stamped live stays refused by that filter. Cancellation of the caller token still skips the enqueue (pre-existing shutdown path).

An in-flight debounce sees the version mismatch on its second lock and does not enqueue the partial. If the debounce already enqueued under the gate, `Add` returns false and the completed line is not enqueued a second time.

The 300 ms debounce uses `Task.Delay(PendingMentionDebounce, _clock, _cts.Token)`. `Clock` is an internal test seam defaulting to `TimeProvider.System`. It is not a constructor or DI parameter, so hosts that register `FakeTimeProvider` as `TimeProvider` do not change production debounce timing.

## Witnesses

Three tests in `AgentChannelServiceIntegrationTests` drive `@helper please inspect` then ` it\n` on a `FakeTimeProvider`, with the liveness read held across the 300 ms advance:

- `Held_liveness_read_routes_the_split_mention_once`
- `Failed_liveness_read_still_delivers_the_split_mention_once`
- `Not_live_stamp_still_delivers_the_split_mention_once`

Each asserts one `CommandEnqueued` and one recipient body containing `please inspect it`.

## Advisories that were in scope

A-2. `Channel_mention_split_across_pty_chunks_routes_once_after_pending_line_settles` waits on `RouteAttemptsSettled` after the first submitted body. The pre-route `await Task.Delay(450)` after `@helper p` remains (line 191). That is the only `Task.Delay(450)` in the file.

A-3. `LoadTimeoutWait` is absent under `tests/` on this base. The new channel witnesses that used `Task.WhenAny` / `WaitAsync` now use the existing `WaitUntilAsync` poll, with the same 5 s and 3 s budgets. The 8 s `WaitUntilAsync` budgets and the 20 s wall assertion were already that helper. `PhoneHomeHostedRunner.UntilAsync` (fixed 5 s, used by the older backpressure tests) was left as it is.

A-1. No failure. `PhoneHomeBackpressureTests` (7 tests) on commit `261e35c8247ce59cba7cc4ec95b5a29fe534db1d`, `dotnet exec --no-build`, filter `/*/*/PhoneHomeBackpressureTests/*`, `--maximum-parallel-tests 4`, 120 s cap each, lease `3917ebe4-20a9-404c-8daf-5d5fa25b588e`:

- 20 runs alone: every run exit 0, `total: 7`, `failed: 0`, `succeeded: 7`. Elapsed 28–54 s.
- 10 runs under 8 CPU burners (pids 6252–6259, tight loops, killed at the end): every run exit 0, same 7/0/7. Elapsed 30–54 s.
- Load average at burner start `29.72 31.20 24.36` (the host was already busy), at stop `46.34 40.61 30.77`.
- 30/30 runs, 0 failures, no failure message. Slot held 1110 s. Nothing to fix.

A-4 through A-7 were out of scope.

## Runs

All through `scripts/build-slot.ps1`, output `bin-eb97a197/`, `UseAppHost=false` on the build. DLL `tests/Antiphon.Tests/bin-eb97a197/Antiphon.Tests.dll`.

| Run | Commit verified | Result |
|---|---|---|
| Red witnesses (3) | pre-fix binary, tests committed as `6bf10da79` | total 3, failed 3, passed 0. TRX `/tmp/eb97a197-red/red.trx` |
| Green (8) | `261e35c82` | total 8, failed 0, passed 8. TRX `/tmp/eb97a197-green/green.trx` |
| Neighbours | `261e35c82` | total 343, failed 6, passed 337. TRX `/tmp/eb97a197-neighbours/neighbours.trx` |
| A-1 matrix | `261e35c82` | 30 runs, each 7 passed, 0 failed |

Red messages:

- `Failed_liveness_read_still_delivers_the_split_mention_once` (508 ms): `CommandEnqueued` should be 1 but was 2.
- `Not_live_stamp_still_delivers_the_split_mention_once` (257 ms): `CommandEnqueued` should be 1 but was 2.
- `Held_liveness_read_routes_the_split_mention_once` (7.3 s): failed in the same double-route window (the unfixed debounce queued the partial while the read was in flight).

Green filter: the three witnesses, `Channel_mention_split_across_pty_chunks_routes_once_after_pending_line_settles`, `Held_flush_of_mention_queue_survives_a_slow_worker`, `Queued_commands_at_dispose_do_not_block_shutdown`, `Route_refuses_a_mention_after_the_source_generation_changes`, `ChannelHub_mention_routes_to_target_session_stdin`.

Neighbour filter: `/*/*/(PhoneHome*)|(AgentChannelServiceIntegrationTests*)|(AgentSessionRuntime*)|(RcRosterRepairTests*)|(C1514LandingSplitBudgetTests*)/*`, `--maximum-parallel-tests 4`. The six failures are only `PhoneHomeMirrorJoinTests` and are the pre-existing Linux reds. Left unfixed.

- `C1032_JoinStaleBinding` (1.3 s), line 269: warnings do not contain `stale_mirror_binding`.
- `C1032_JoinCrashRetry` (2 m), method at line 135: `TimeoutException` after 00:02:00.
- `C1032_JoinBusyRecipient` (2 m), method at line 70: `TimeoutException` after 00:02:00.
- `C1032_JoinIdleRecipient` (526 ms), line 54: `row.BindingId` was `Guid.Empty`. Additional info: the runner could not mirror the task branch (`delegate_branch_identity_mismatch`); the task stays Queued.
- `C1032_JoinStoreMismatch` (401 ms), line 299: warnings do not contain `runner_store_mismatch`.
- `C1032_JoinForeignOwner` (544 ms), line 227: warnings do not contain `foreign_mirror_owner`.

## Merge-tree

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` against `origin/master` `2fb1c5a6541b464de5dcb9ae0821841a9a71ca7c`. Exit 1. Not merged. Conflicts:

- `.antiphon/task-af2b78c8.md` (add/add)
- `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` (add/add)
- `docs/testing-and-build.md`
- `scripts/lib/checkpoint-file-reader.ps1`
- `scripts/lib/nightly-class-costs.json`
- `scripts/test-nightly-tests.ps1`
- `server/Application/Dtos/SessionRunnerDtos.cs`
- `server/Infrastructure/Agents/SessionRunner/RunnerContractMapper.cs`
- `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`
- `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs`
- `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`
- `tests/Antiphon.Tests/Application/GrokSignInRuntimeTests.cs`
- `tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs`
- `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatBuildIsolationTests.cs`
- `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatFixture.cs`

`AgentMentionRouter.cs` and `AgentChannelServiceIntegrationTests.cs` are not in that list. `PhoneHomeBackpressureTests.cs` auto-merged. These conflicts ride in from the carried `c790cd94` history versus current master. This round does not resolve them. Next stage is review, not land.

## Left alone

The six `C1032_Join*` failures. The live Windows rc qualification. `bin-r7e11` directories. No rebase, amend, reset, force-push, tag, or publish.
