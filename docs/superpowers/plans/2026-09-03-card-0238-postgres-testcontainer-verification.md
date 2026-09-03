# CARD-0238 — Postgres testcontainer connection-budget verification

**Date:** 2026-09-03  
**Decision:** make no further fixture change. Do not raise `max_connections` / `max_locks_per_transaction`, and do not add the card's proposed pool-lifetime and semaphore patch.

## Why no new change is warranted

The 2026-08-29 card describes the old isolated-`SearchPath` implementation, where every schema made a long-lived Npgsql pool and concurrent migrations filled both the 100-connection limit and the lock table. That implementation is no longer on master.

Commit `a9112991` (`test(db): CARD-0110 S2 - clone isolated test DBs from a migrate-once template`, 2026-08-30) replaced it with a stronger remedy in `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs`:

- migrate the shared database once, then create a template database;
- create each isolated store as a clone of that template under the one-wide `CloneLock`;
- force-drop the clone at disposal after clearing its exact Npgsql pool; and
- use a non-pooled maintenance connection for clone operations.

This removes concurrent per-isolation migration lock pressure and ensures a clone's distinct pool is cleared when the consumer finishes. Raising the container ceiling would only mask a regression in those lifetime guarantees; adding another semaphore would serialize an operation that is already serialized.

## Full-suite evidence

On the current worktree (clean before the run):

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c238/
pwsh -NoProfile -File scripts/run-tests-watched.ps1 `
  -Exe tests/Antiphon.Tests/bin-c238/Antiphon.Tests.exe -Tag card0238 -Detailed
```

The watched run completed in **25m 27s**:

| Total | Passed | Failed | Skipped |
|---:|---:|---:|---:|
| 3,893 | 3,872 | 16 | 5 |

The watcher sampled a maximum of **42/100** PostgreSQL connections during the early parallel phase (later samples were 7–39, 22–24 in the serial tail, and 31–34 in a later concurrent segment). It recorded **zero** `53300`, `53200`, `too many clients`, or `out of shared memory` errors. This closes the measured gap from the card's 46 of 53 failures, including its observed 101–104 connections against the stock 100 limit.

The remaining failures were:

1. `Probe_cancellation_kills_the_tree_and_redacts_secret_canaries`
2. `Late_process_start_after_the_one_second_deadline_is_reaped`
3. `last_activity_uses_transcript_after_dispatch_and_falls_back_otherwise`
4. `a_git_timeout_fails_one_task_not_the_tick`
5. `a_failed_worktree_add_leaves_no_registration_branch_or_directory`
6. `A_brief_inside_the_ceiling_arrives_whole`
7. `the_final_message_missing_incident_is_raised_once_per_session`
8. `AgentSessionService_kill_force_kills_after_grace_period`
9. `AgentSessionService_kill_marks_active_attempt_canceled_and_disposes_adapter`
10. `arm_0_while_live_on_turn_end_in_progress_enqueues_one_parent_note`
11. `A_fable_hold_skips_fable_and_dispatches_sonnet_on_the_same_tick`
12. `A_manual_fable_hold_skips_queued_fable_and_dispatches_sonnet`
13. `ListAvailable_omits_held_aliases_and_keeps_the_rest`
14. `Require_throws_model_disabled_with_available_list`
15. `Dispatch_records_an_informational_warning_and_never_refuses_on_a_low_reading`
16. `Assembly_guard_and_factory_override_disable_the_Hangfire_worker`

None carries the prior Postgres error signature; they are out of scope for this card.

## Related reports checked

- `GrokDelegateDispatchTests` and `GrokDelegateEndToEndTests` did not fail in this run. The CARD-0230 investigation already established that the latter two test paths do not call `CreateIsolatedSchemaAsync`; its historic harness failure was a swallowed DI exception, not a database-budget failure.
- CARD-0323's reported pre-existing failure is `TranscriptAdoptionSafetyTests.Queue_operation_enqueue_of_delivered_text_binds_via_C4` in `Antiphon.SessionRunner.Tests`, reproduced at that task's base. It is outside this test assembly and database fixture.
- CARD-0098's reported pre-existing failure is `An_edit_snapshots_importance_urgency_and_due_and_maintains_UrgentSince`, a tick-truncation mismatch reproduced at that task's base. It is absent from this run's 16 failures and has no Postgres signature.

## Card disposition

CARD-0238 is fixed by the already-landed `a9112991` fixture redesign and is empirically verified on the current full suite. Close it with that conclusion; do not add container-capacity configuration as a compensating change. The suite is not fully green (16 unrelated failures remain), so its broader test signal still needs separate failure triage.
