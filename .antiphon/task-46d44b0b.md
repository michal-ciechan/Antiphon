# CARD-1593 leftover 1 Final Review (Interim scope): bound non-Postgres commit faults

Subject: task 8d6a48c8-2852-4567-a29c-901f0b317989, branch feat/card-task-8d6a48c8,
tip 85dbb322672889cd2b184ca8cc6abdaac627893c (red 5f6a3b1ff120a604c13170f653e65b0fac139195),
base rc/2026-10-27 4563e52529b23052856cb0f1e0d01b383cf7590f. Reviewed read-only at a detached tip in
/work/worktrees/task-46d44b0b (server2/Linux).

Outcome: tests are green, with one defect (D-1). The retry anchor does not do what the claim says.
No fail-open, double spend or reservation leak was found.

## Runs (one slot build, then one slot test run, both through scripts/build-slot.ps1)

- Build: `dotnet build tests/Antiphon.Tests -c Debug --property:OutputPath=bin-r46d/ --property:UseAppHost=false`.
  Exit 0 in 2m23s, lease 36a45d47.
- Tests: `dotnet exec tests/Antiphon.Tests/bin-r46d/Antiphon.Tests.dll --treenode-filter
  "/*/*/(CapacityAdmissionLifecycleTests*)|(CapacityLockOrderTests*)|(WallRerouteDispatchTests*)|(ReceiptFailureDeliveryTests*)|(AgentTaskDispatchBaseGuardTests*)/*"`.
  Exit 0, lease 88c2398c, 3m29s. Total 173, passed 173, failed 0, skipped 0.
- Per-class counts from the TRX:

  | Class | Tests |
  |---|---|
  | CapacityAdmissionLifecycleTests | 24 |
  | CapacityLockOrderTests | 16 |
  | WallRerouteDispatchTests | 15 |
  | ReceiptFailureDeliveryTests | 21 |
  | AgentTaskDispatchBaseGuardTests | 97 |

  All six `C1593_consume_failure_leaves_the_task_queued` cases ran, including `(non-postgres-commit-fault)`.
- `scripts/check-evidence-diff.ps1 -BaseRef 4563e525 -HeadRef 85dbb322`: commits=2, violations=0.
- What is not covered:
  - These runs used build-slot plus dotnet exec, not the checkpoint tool, so there is no SHA-validated receipt.
  - The whole Unit lane was not run, as the brief scoped it.
  - The red commit was not built. Its redness was checked by reading the code (see below).
- The ignored output directory `tests/Antiphon.Tests/bin-r46d` (and its obj outputs) was left in place, because the brief says to delete nothing.

## Red witness

- Red 5f6a3b1f only adds lines to the test. The five existing `[Arguments]` cases are byte-unchanged; the diff has no `-` lines.
- The new case uses a literal marker string, so it compiles on the red commit. The green commit swaps that literal for the constant.
- At red, the dispatcher's second non-Postgres fault still takes `IsTransientCapacity || !IsPostgres` and returns Refused. The task stays Queued, so after `Throws==2`, `stored.Status.ShouldBe(Failed)` fails. The red is genuine.

## Priority answers

### (1) Can it fail a task that should stay Queued?

- **Which exceptions count:**
  - Npgsql socket, IO and timeout failures are NpgsqlException without a PostgresException, so they count as "non-Postgres".
  - An OperationCanceledException that is not from the tick `ct` also counts.
  - A shutdown cancellation of `ct` does not count. The filter rethrows it, and the task's rollback leaves it Queued, as before.
  - A DbUpdateConcurrencyException cannot come from CommitAsync.
- **Failover:** a failover does not "fail every queued task at once".
  - The tick's earlier reads, and the claim's pre-commit SaveChanges, already fail on a broken connection.
  - At base, a single NpgsqlException in the pre-commit claim already fails the task: FailCapacityLaunchAsync treats it as non-transient, then releases and throws.
  - Only a task whose COMMIT straddles the break is counted, and it needs a second straddling fault of its own.
- **Bound of 2:** I would keep it. It matches the rest of the claim, which fails on the first non-transient fault. Each extra allowance costs about one more 120 s re-hold of the provider grant.
- **"Consecutive" is not what the code does:** it counts markers since the latest Dispatched event (D-1).
  - Refusals, transient faults and holds in between do not reset it. Two blips hours apart in a long capacity queue fail the task. I accept this, but the wording should say "since the last dispatch or retry".
  - A manual Retry does not reset it (D-1).
  - A task that was never dispatched gets `lastDispatchAt == null`, so every marker ever written counts.
- **Is the failed task retryable?** Yes. It fails as "Dispatch failed before a session existed: <IOException text>", and `AgentTaskService.RetryAsync` requeues it under the same task id with a Retried event. Because of D-1, though, a retried task gets no grace.

### (2) Does the warning write add risk?

- **Locks (Rule P):** both provider locks are `pg_advisory_xact_lock` (CapacityRecoveryService.cs:852, :1317). AbandonClaimAsync rolls back, which releases them, before the reread. The count query and the marker insert therefore run in autocommit with no lock held, so Rule P is respected.
- **Broken connection:** if the count query or the insert throws, the new exception reaches the caller's `catch` and goes to FailCapacityLaunchAsync. It is non-transient, so the reservation is released (claim-consume-failed) and the task fails on the first fault.
  - Nothing leaks, but the original fault text is masked.
  - The same exposure already exists at base through the preceding reread (`FirstAsync`) and WarnCapacityConsumeRefusedAsync. This is not a new class of risk.

### (3) Double spend, leaked reservation, lost ack, cancellation and transient faults

- **Double spend / leaked reservation:**
  - The bound path calls ReleaseFailedReservationAsync (which swallows its own errors) and then `throw;`, which keeps the original exception.
  - The caller's FailCapacityLaunchAsync abandons and releases again. This is the same double, idempotent release the existing XX000 path makes.
  - The test asserts the wait is Canceled with claim-consume-failed, AdmissionCount is 0 and GrantedWaitId is null. No double spend.
- **Lost ack (A-3):** the reread check for Dispatched with the same session still runs first, so it is unchanged.
- **Transient SQLSTATEs (D-9):** 40P01, 40001 and 55P03 are checked before the non-Postgres branch, so they are unchanged.
- **Cancellation:** unchanged.
- **Advisory A-1:** on the second fault, a commit that lands after the reread would now fail a Dispatched task. The first fault already treats that commit as possibly landing.
  - The window is narrow. The reread runs on a fresh connection after the old backend saw the socket drop, so it matters mainly with synchronous replication.
  - No action is required.

## D-1 (blocking): the fault count ignores Retried, so a retried task fails on its first fault

- **Where:** `AgentTaskDispatcher.NonPostgresCommitFaultsSinceDispatchAsync` (server/Application/Services/AgentTaskDispatcher.cs, around line 8254). It anchors only on `AgentTaskEventType.Dispatched`.
- **Failure:**
  1. A task fails by the bound after markers=1 plus the second fault.
  2. An operator fixes the cause and calls Retry, which requeues the same task id and writes a Retried event, not a Dispatched one.
  3. The marker from before the retry still counts, so the next single non-Postgres commit fault computes 1 + 1 >= 2. The task is released and failed at once, with no "stays queued once".
  4. The same happens to a task that had one blip, then failed pre-dispatch for another reason (for example the Grok or Claude credential probe), and was retried.
- **Why it matters:**
  - It contradicts the claim that the second consecutive fault fails the task and that a retry resets the count.
  - A Retry is the documented recovery for this failure, so a transient blip after recovery fails the task again with no grace.
  - No test pins the anchor at all. Removing the `lastDispatchAt` filter, or the Dispatched anchor, keeps all 24 tests green.
- **Fix:**
  - Anchor on the latest event of type Dispatched **or Retried**. Consider also anchoring on Failed, so any terminal-then-requeue resets the count.
  - Update the comment and summary wording to "since the last dispatch or retry".
  - Add an argument or method to CapacityAdmissionLifecycleTests that seeds one old marker followed by a later Retried event (and, as a control, a later Dispatched event). It should assert that one non-Postgres commit fault leaves the task Queued with the grant held and the marker count at 2.
  - Commit the red test first.
- **Landing owner:** Code task 8d6a48c8-2852-4567-a29c-901f0b317989.
