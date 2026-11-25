# Why missed: rc run b8cbfb24 group-86 AgentIncident FK

Windows Full rc run b8cbfb24 (rc/2026-12-05 pin 2fd61b643ea4231fa732cd4969b5284d43b33f39), chunk `antiphon-all-group-86.trx`: 1 failed row.

- `GrokDelegateDispatchTests.Warm_grok_invalid_rules_revision_cannot_acquire_or_overwrite_the_live_session(expected_hash)`, 1.23 s: `DbUpdateException` from `AppDbContext.SaveChangesAsync` (`AppDbContext.cs:175`) in `AgentTaskDispatcher.FailAsync` (`:8416`), called from `FailAndNotifyAsync`. Inner: `Npgsql.PostgresException 23503: insert or update on table "AgentIncidents" violates foreign key constraint "FK_AgentIncidents_Agents_AgentId"`.
- The other nine cases of the same method (`pending`, `failed`, `legacy`, `malformed`, `hash`, `generation`, `version`, `path`, `count`) are not in the failed-row list, so they passed in that chunk.

## Facts

- The method is not new or renamed. `git log -S Warm_grok_invalid_rules_revision_cannot_acquire` finds it first in `b1d79b3d82c` (`fix(grok): validate retained receipts and cover lifecycle failure matrices (CARD-0395)`). It is in the full-discovery fixture with all ten cases. It was not in the acca11ba chunk's results.
- `GrokDelegateDispatchTests`, `AgentTaskDispatcher.cs`, and `TaskProgressStallSweepTests.cs` are byte-identical at the rc pin `2fd61b643` and at master on 2026-11-25. The fix applies to master and needs no rc-specific change.
- The test seeds a warm Grok delegate whose rules receipt is invalid, seeds a queued Grok task, and runs the real `TickAsync`. `TickAsync` first runs every maintenance sweep and then dispatches every queued task in the shared test database. The harness uses `MaxConcurrentTasks = 512` so that leftover rows do not use up the budget. The sweeps also act on leftover rows from the whole fleet.
- The harness does not register `SweepInFlightState`. `RunSweepAsync` therefore runs each sweep on the dispatcher itself (`RunAwaitedSweepAsync(name, this, …)`), using the same scoped `AppDbContext` that the dispatch half uses afterwards. `TaskProgressStallSweepTests` uses the same harness shape. The production host registers `SweepInFlightState` (`Program.cs:791`) and gives each sweep its own scope.
- `AgentTasks.AgentId` has no foreign key. `AgentIncidents.AgentId` does (`FK_AgentIncidents_Agents_AgentId`, nullable, cascade). A Working task can still name an agent after that agent's row is gone.
- `DetectStalledProgressAsync` (the progress-stall sweep, CARD-0153) is enabled by default (`StallDetection.Enabled = true`, `StallMinutes = 30`). It loads every Dispatched or Working task with `DispatchedAt` and `AgentSessionId` set. For a task with a stall verdict, it did `_db.AgentIncidents.Add(new AgentIncident { AgentId = task.AgentId, … })` and then `SaveChangesAsync`. A failure is caught per task ("the sweep continues"), so the Added entity stayed tracked.

## Cause

A stalled Working task in the shared database named an agent whose row was gone. The stall sweep's insert failed with 23503 and left the incident tracked on the dispatcher's context. On the same tick, the test's queued task hit its first save. That save sent the tracked incident again and failed the same way. The dispatch catch then called `FailAndNotifyAsync` → `FailAsync`, whose `SaveChangesAsync` sent it a third time. That exception escaped `TickAsync`. This is the Windows stack frame for frame.

Linux reproduction, same tree, testcontainer Postgres only: `TaskProgressStallSweepTests.A_stalled_task_whose_agent_row_is_gone_records_its_stall_and_does_not_poison_the_tick`. It seeds the CARD-0153 stalled scenario, deletes its Agent row, seeds one queued task, and calls `TickAsync`. At `c29118a637de3836f04550c42e7a8533cd754e1e` it failed 1/1 with `23503 FK_AgentIncidents_Agents_AgentId` at `AppDbContext.SaveChangesAsync` ← `AgentTaskDispatcher.FailAsync` ← `FailAndNotifyAsync` ← `DispatchCoreAsync:1181` ← `TickAsync:398`.

The failure is in production code, not a gap in the test's setup. The Grok test never touches an agent that it did not insert. Its only fault is that it shares a tick with someone else's row. The production impact is smaller because each sweep owns a scope there. Even so, one stalled task whose agent row is gone made every later stall in that sweep fail on the same poisoned context, on every tick, for as long as the row lived. A delegate whose Agent row is removed while its task is still Working (for example an operator deleting the agent) is enough.

## Fix

`3f285cbb35d28a45782c860fd364319b06349f5f` (`AgentTaskDispatcher.TryRaiseProgressStallAsync`):

- The incident names the agent only while that agent's row exists. Otherwise `AgentId` is null. The episode dedupe is keyed by `SessionId`, so a null `AgentId` does not change it.
- If the incident write fails (for example, the row is deleted between the check and the insert), the incident is detached before the exception is rethrown. A failed write can no longer reach the next `SaveChanges` on that context.

Witnesses:

- The vanished-agent shape: the test named above, committed red at `c29118a63`.
- The race: `An_agent_row_deleted_under_the_stall_insert_does_not_poison_the_tick`, committed at `56cd283cc884c4ce681cc07c6907aae6f4c843ef`. A one-shot command interceptor deletes the agent just before the `INSERT INTO "AgentIncidents"`. The test asserts three things: the tick still decides its queued task, that tick records no stall, and the next pass records the stall with a null `AgentId`. With the fix minus the detach line, it failed 1/1 with the same 23503 out of the tick, while the first witness passed. It is green with the full fix.

No timeout was widened, no retry was added, and no assertion was loosened. The Grok test is unchanged.

## Why Linux and the Merge selection missed it

- **Cross-class shared state, not this test.** The precondition is a row that some other test leaves in the shared assembly database: a Working task that has a session, was dispatched more than 30 minutes ago, has a looping transcript, and names a missing agent. The fleet-wide stall sweep sees that row. The failing test is only the next dispatcher tick to save on that context. On Linux the method passes 10/10 when run alone (all ten cases, 0.35–16 s). On the server2 rc lanes the chunk composition and the concurrent classes differ.
- **Parameterisation is incidental.** `expected_hash` is the tenth and last case. The poisoning row existed only for a window during that Windows chunk, and the last case is the one that overlapped it. No variant-specific code path is involved. The other nine cases ran the same tick and passed.
- **Timing window.** The stall verdict needs `DispatchedAt` at least `StallMinutes` old and enough recent rows in the look-back window. A seeded scenario stops qualifying once its rows age out or its cleanup runs. The producer from the Windows run cannot be identified from server2: that database was not kept, and the TRX records only the victim. The concurrently running classes that seed Working tasks with sessions include several without `[NotInParallel]`. `TaskProgressStallSweepTests` is global `[NotInParallel]` and deletes its Agent row last, so its own scenario cannot be the producer.
- **Merge selection.** Merge verification runs the short set (2026-11-18): narrow witness rows only. `GrokDelegateDispatchTests` and the stall sweep class are `[Category("Integration")]` and are not in that set. Even if it had run, the class run alone has no foreign row to trip on.
- **Not Windows-only ordering.** The witness is red on Linux. Windows only supplied the concurrent producer.

## Same pattern elsewhere

These writers add an incident to a context that a caller keeps using, and they do not detach on failure:

- `AgentTaskDispatcher.RecordReuseEnqueueFailedAsync` (`DeliveryTransportFailed`, `AgentId = task.AgentId`, `_db.Add` + save). It runs on the warm-reuse path while the reused agent is claimed, so its row is present. The risk is low, and it is left unchanged.
- `TryFailGrokCredentialProbeAsync` / `TryFailRunnerGrokCredentialProbeAsync` / `TryFailClaudeCredentialProbeAsync`. With no supervisor, these add a `GrokSignIn` or `ProviderSignIn` incident for `claimed.AgentId` to `_db` without saving. The next save in `FailAndNotifyAsync` writes it. If that agent row were missing, the same 23503 would fail the task's failure write. These paths only run when the credential probe is enabled. Both Grok harnesses turn it off, and production runs with a supervisor. They are not changed here. A follow-up card should apply the same "name the agent only if present" guard to them.

The general hazard is any catch-and-continue around `_db.SaveChangesAsync` on a context that is reused afterwards. The CARD-0153 sweep was the only fleet-wide one found that names an agent taken from a task row.

## Cheapest pre-land check

`server/Application/Services/AgentTaskDispatcher.cs` → `/*/*/TaskProgressStallSweepTests/*` (15 tests), plus the rc scenario `/*/*/GrokDelegateDispatchTests/Warm_grok_invalid_rules_revision_cannot_acquire_or_overwrite_the_live_session` (10 cases).

## Exemption

none

## Verification

Checkpoint run `20261125-200927-8112` (server2, Linux), commit `3f285cbb35d28a45782c860fd364319b06349f5f`, sourceState=clean, buildSource=verified, `validate` → `CHECKPOINT SOURCE VALID rows=2`:

- CP-1 `/*/*/TaskProgressStallSweepTests/*`: 15 executed, 15 passed, 0 failed, 0 skipped.
- CP-2 `/*/*/GrokDelegateDispatchTests/Warm_grok_invalid_rules_revision_cannot_acquire_or_overwrite_the_live_session`: 10 executed, 10 passed, 0 failed, 0 skipped.
