# Why Windows group 93 hit the dispatch concurrency cap

Windows Full run `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267` had 301 passes and two failures in group 93. Both `TaskProgressStallSweepTests` witnesses reached the queue assertion after their stall-write assertions passed. The historical TRX recorded `Queued` without the event detail.

The two exact methods passed by themselves on Windows at `93e24eac5050f9e0ed1ef8c439c098140ccb25f5`. A second Windows run at `b4ae8b84327e0d6688f8c132e8c5ea4aa537f255` included one exact predecessor from the original chunk, `AgentTaskScopeOverlapCreateTests.a_shared_task_is_told_it_will_wait_behind_the_running_one`. The predecessor passed. Both stall witnesses failed at the same queue assertion and recorded this event for their queued task.

> Held: concurrency cap reached (2 process-spawning tasks running); waiting for a slot.

The counted rows were `stall sweep test` and `the holder`. The scope-overlap predecessor seeds the latter as a local `Working` task and leaves it in the shared PostgreSQL database. The default concurrency cap is two, and the dispatcher counts those rows before attempting a claim. The original TRX also shows the predecessor finished before either stall witness started. The replay identifies a sufficient producer and the recorded hold. The original TRX cannot identify every row counted in the qualification run.

Each repaired witness opens its own migrated database clone. Seeding, dispatch, the delete-before-insert interceptor, and assertions use that clone. The tests still run the real tick with the default cap and check that the queued task leaves `Queued`. A failure now includes that task's recorded hold details.

## Introducing commit/card

The shared-store witnesses landed in `0812dbdeb0a8c7f86056873e0d821c744239e83b` and `948907e64eb13366b356170e8021b80ef6f0883d`. They exercise the group-86 incident foreign-key repair `52ece03ef17ee4adc8e9b1d6a316408752f05cee` in the CARD-0153 sweep. [Group 86 investigation](2026-11-25-why-missed-group-86-agent-incident-fk.md)

## Approving reviews

- Final Review round 1, Full, `459ebfb9-5c09-4367-b4db-9ed464aeba27` at `289260ba2246e29c8dbdbc48b78e6d539c3ea317`. Source-validated Linux run `20261125-203504-5c75` passed all 15 `TaskProgressStallSweepTests` cases and 10 cases of `GrokDelegateDispatchTests.Warm_grok_invalid_rules_revision_cannot_acquire_or_overwrite_the_live_session`.

## Outside the selection

The review exercised both Integration witnesses in `tests/Antiphon.Tests/Application/TaskProgressStallSweepTests.cs`. Its selection excluded the scope-overlap predecessor and the shared occupancy it leaves behind. The group-93 failure needs combined execution, which the two-method Windows baseline also excluded.

## Cheapest pre-land check

`tests/Antiphon.Tests/Application/TaskProgressStallSweepTests.cs` → `/*/Antiphon.Tests.Application/(AgentTaskScopeOverlapCreateTests)|(TaskProgressStallSweepTests)/(a_shared_task_is_told_it_will_wait_behind_the_running_one)|(A_stalled_task_whose_agent_row_is_gone_records_its_stall_and_does_not_poison_the_tick)|(An_agent_row_deleted_under_the_stall_insert_does_not_poison_the_tick)`

The selection retains one exact predecessor and both exact witnesses in one TUnit process. A TRX records the predecessor's start and end times and the witnesses' queue assertion results.

## Exemption

none
