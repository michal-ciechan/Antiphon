# CARD-1593 delta design review 3 (task afa47db3)

Subject: plan round 3, task d94003bc, tip `0c6a9c8845af2016e7f89922cf05862178e0514f`
(plan doc only). Source refs are at rc/2026-10-27 `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`,
which is the tip's source tree. This was a read-only design review: nothing was built and no
tests were run. `check-evidence-diff.ps1` over base..tip: 3 commits, 0 entries, 0 violations.

**Verdict: the round-3 design is sound if Code applies the three amendments below (F1-F3).**
None of them changes the rank table, D-9's classes or D-12. Next: code.

## Priority 1: session-before-agent order. Confirmed, with one gap in the recheck (F1)

No path at base locks a physical agent and then locks or writes a session, and none holds the
agent while it waits on a provider or task. I re-read each site:
- `AgentControlService.cs:546-549` (sessions, then `LockAgentAsync`).
- Stop at `:1276-1283` and delete at `AgentService.cs:942-950`/`:1011-1017`: the session
  `FOR UPDATE`, then the owner.
- `AgentSessionService.cs:410-417`: the session `FOR UPDATE`, then the agent
  `FOR NO KEY UPDATE`.
- The exit observer: the session `FOR UPDATE` at `AgentSessionRuntime.cs:213`, then an agent
  write `:274-279` saved `:331`. It writes no task row.
- `TerminalRunnerSeatReleaseService.cs:872-878` (task, release, session), then the `:956`
  agent `ExecuteUpdate`. The update is filtered on `PersistentSessionId == S` and sets
  `Stopped`, so the warm-reuse `Status == Idle` recheck catches it.
- Grep of every `"Agents" ... FOR` and `LockAgentAsync` site. The agent-only sites take no
  session lock in the same transaction. That includes `AgentPinnedInstructionService.cs:86/:217/:288`,
  which L21 omits; L21 cites `:489/:500`, which are the helper.
- `AgentSessions` has no FK to `Agents` (`AppDbContext.cs:1566-1637`), so deleting X in
  `RemoveEphemeralAgentAsync` `:4004` cascades into no session rows.

**The re-point race is handled.** Suppose a concurrent re-point (the resume path
`AgentControlService.cs:546-552`) moves X from S to S'. The claim's X `FOR UPDATE` waits for it
to commit. The reload then shows `PersistentSessionId == S'`, and the recheck falls back. There
is no cycle, because the re-pointer never waits on the claim's task or previous-owner rows.

### F1 (design): the warm-reuse and standing-placement rechecks omit session liveness

**Where:** D-2 warm reuse step 3, and standing placement in D-2/L1s.
- The warm-reuse recheck after the S lock tests only agent fields
  (`Idle`/`PoolIdleSince`/`PersistentSessionId == S`).
- Standing placement has no recheck after its S `KEY SHARE`.
- S's liveness comes from `LiveSessionIdOfAsync` (`AgentTaskDispatcher.cs:9217-9225`). That is
  an unlocked read taken before the wait.

**Failure:**
1. Pool agent X is `Idle` with live session S, and S exits.
2. The exit observer takes S `FOR UPDATE` (`AgentSessionRuntime.cs:213`), so the claim's S
   `KEY SHARE` waits. It is meant to wait here (`FOR UPDATE` conflicts with `KEY SHARE`).
3. The observer marks S exited. It changes the agent only when `Status == Running`
   (`:274`), so an `Idle` pool X keeps `Idle`, `PoolIdleSince` and `PersistentSessionId == S`.
4. The claim gets S and the recheck passes. T is bound to the dead S, marked `Dispatched`, and
   its admission is consumed with the receipt bound to S.

Standing placement does the same without any recheck. Base has the same behaviour through the
flush's FK `KEY SHARE`, but S1 adds a deliberate wait at exactly this point. The plan's new
recheck is where it belongs, and V-15 only witnesses the stop variant.

**Fix:**
- After the S lock, reread S and require `Status` to be `Starting` or `Running`. Do this in both
  warm reuse and standing placement.
- On failure, return the fallback base gives: `SpawnFresh` for a pool pick; `WaitForAgent` for
  a pin; for standing placement, base's `:8407` choice (`AlwaysOn ? WaitForAgent : SpawnFresh`).
- The lock makes this reread authoritative against every writer that holds S `FOR UPDATE`
  (stop, exit observer, reconciliation `:208`, launch failure).
- Add V-15 arguments `("stop")`, `("pool-session-exit")` and `("standing-session-exit")`. In
  the exit arguments, the test connection takes S `FOR UPDATE`, marks it `Exited`, leaves the
  agent unchanged and commits. Expect T on a new session, or held for `AlwaysOn`. These are red
  at base (Rule F assertion, and bound to the dead S) and red under the round-3 text.
- CP-1 becomes 36 results (20 lifecycle + 16 lock-order) and Min becomes 36.

## Priority 2: D-12 holds, provided the consume reads fresh rows (F3)

There is a single `GrantedWaitId` per provider. Every grant set or clear takes the
transaction-scoped provider key first: `CapacityRecoveryService.cs:138` (grant/revoke),
`:248`, `:347`, `:405`, `:457`, `:647` and `:1181`. The consume compares the id and key under
that key, then clears the grant and sets `NextAdmissionAt` (`:286-288`).
- **c5b:** a grant that has expired but is not revoked has been given to no one else, so
  spending it neither double-admits nor breaks pacing.
- **c5a:** the regrant waits for `NextAdmissionAt`. A wait in `StartAccepted` is never a
  candidate again.

`ConsumePendingReleasesAsync` (`:57-100`) writes wait rows without the provider key, but it
touches only hold-observation fields and moves a wait only out of `WaitingForHold`. That
leaves a cosmetic lost `Version++` race, as at base.

### F3 (amendment): `RedeemOnAsync` must reload the provider state and wait after the lock

**Where:** the new `RedeemOnAsync` runs on the tick's `_db`. On successful commits the claim
context is never cleared. A same-tick committed non-launch release (D-2) leaves
`CapacityRecoveryProviderState` and the wait tracked. A later `FirstOrDefaultAsync` returns the
tracked instance with its stale values.

**Failure:**
- A stale `GrantedWaitId` can refuse a valid grant (a spurious `grant-mismatch`, which costs one
  tick).
- In the worst case it matches a grant the reconciler has since revoked. The consume then
  clears another waiter's grant, and D-12's "matches under the lock" argument breaks.

**Fix:** after `TakeProviderLockAsync`, `ReloadAsync` the state and the wait (or query them
`AsNoTracking` and attach). This is the pattern `SupersedeTaskWaitsOnAsync` already uses at
`:349-356`, with the comment "This context may have read the wait before another consumer
redeemed it".

## Priority 3: the D-9 classification is sound for what can occur. No deterministic failure loops

- **Wrapping:** EF `SaveChanges` gives `DbUpdateException` with an inner `PostgresException`.
  Raw-SQL pre-locks throw `PostgresException` directly. The "chain holds" walk covers both.
- **No retry strategy:** there is no `EnableRetryOnFailure` or `ExecutionStrategy` in
  `server/`, so nothing wraps in `RetryLimitExceededException`.
- **`55P03` is unreachable in the claim:** there is no `lock_timeout` and no `NOWAIT` in the
  claim. That is harmless.
- **Deferred constraints:** there are no deferrable constraints or constraint triggers, so a
  deterministic `PostgresException` from `COMMIT` cannot occur today.
- **Timeouts and connection loss:** a command timeout (default 30 s;
  `NpgsqlException`/`TimeoutException`) or a connection loss in a pre-lock or consume is
  class 4. The task fails and the reservation is released. That matches base, which fails on
  the same fault in the flush, so it is not a misclassification. D-9 should say so explicitly
  (A-4).

## Priority 4: witnesses and counts

**CP-1 = 34 is consistent with the rows:**
- Lifecycle 20: V-2 4, V-3 1, V-4 6, V-5 1, V-6 1, V-7 1, V-11 1, V-12 1, V-13 4.
- Lock-order 14: V-8 8, V-9 3, V-10 2, V-15 1.
- With F1 it becomes 36.

**V-15 is deterministic.**
- At base, the flush's FK `KEY SHARE` on S waits on the test's `FOR UPDATE` while it holds
  the parallel key, so the Rule F assertion is red.
- The PC (X before S) gives a real two-backend cycle, which produces `40P01`.

**V-13 `pre-lock-deadlock-victim` is deterministic.**
- Under S1, the first `Agents` lock or write after the task lock is the raw pre-lock. It
  throws `PostgresException` directly, which is class 2.
- At base, it is the batched flush at `:5706`. That flush has no catch, so the generic catch
  fails the task, which is red.
- Match the interceptor on the command text (`"Agents"` with `FOR UPDATE`, or `UPDATE
  "Agents"`) and fire it once.

**V-8 `claim-agent-row`** is red at base and green after S1. PC (a) is sound.

### F2 (witness setup): the continuation witnesses need a capacity wait on task A

**Where:** V-8 `continuation-agent-row` (red-at-base claim), V-8 `continuation` (guard PC),
V-9 `continuation`, and the V-10 continuation case and PC.

**Failure:** `SupersedeTaskWaitsOnAsync` takes a provider key only for each `ExecutionKind`
among A's existing waits (`CapacityRecoveryService.cs:337-347`). If A owns no wait:
- At base the continuation holds no kind key while it waits on X, so
  `continuation-agent-row` is **green at base**, not red.
- The V-8 and V-9 `continuation` arguments cannot exercise the provider assertion.
- In V-10, the continuation PC needs A's wait to be of the **same kind** as claim task B.
  Otherwise the two paths take different keys and never form the `40P01` cycle the PC
  requires.

**Fix:** in each of these setups, task A owns an unfinished capacity wait whose `ExecutionKind`
equals B's kind. State this the way `routing-resume` states its two-task setup.

## Advisories (no Review rerun needed)

- **A-1. Agent pre-lock strength.** Consider `FOR NO KEY UPDATE` for the X pre-lock. It matches
  the flush's `UPDATE` strength and `AgentSessionService.cs:413-417`'s deliberate choice to admit
  FK `KEY SHARE` inserts on other connections (incidents). `FOR UPDATE` blocks those inserts for
  the whole claim, including `git worktree add`. If Code changes it, the V-15 test lock on X
  stays `FOR UPDATE`, which still conflicts.
- **A-2. Locks kept after a failed recheck.** After a failed recheck, warm reuse returns
  `SpawnFresh` while still holding X `FOR UPDATE` and S `KEY SHARE` through the cold launch. That
  blocks stop, exit and resume on X/S for the length of the worktree add. It is not a cycle (the
  cold launch never waits on X/S holders). Optional fix: a savepoint before the pre-locks, rolled
  back on recheck failure, which releases those row locks.
- **A-3. Class 3 and server errors.** Apply class 3 (the reread) only to a commit failure that is
  **not** a `PostgresException`. A server error at `COMMIT` is a definite rollback, so classify it
  by SQLSTATE as class 2 or class 4. Otherwise a future deferred constraint would loop as
  "Queued reread = class 2".
- **A-4. Release guards.** `ReleaseClaimReservationOnAsync` must cancel only a wait that is still
  `ActionPending` with a null `LaunchSessionId`. If the class-3 reread itself throws, it must not
  release. That keeps a committed `StartAccepted` receipt from ever being cancelled. D-9 should
  also name command timeout and connection loss as class 4, matching base.
- **A-5. PC (b) assertion order.** V-8 `claim-agent-row` PC (b) is "red on the capacity-key
  assertion" only if that assertion runs before the parallel-key assertion. Under PC (b) both keys
  are held. Assert the kind key first, or accept either failure message.
- **A-6. L21 line numbers.** L21 should list `AgentPinnedInstructionService.cs:86/:217/:288`
  (agent only; conforms).

## Is S1 still the smallest sound slice?

Yes. F1 adds one reread to each of two code paths. F2 changes test setup only. F3 adds two
reloads. None of them adds a row to S1 or moves anything from S2 or S3.
