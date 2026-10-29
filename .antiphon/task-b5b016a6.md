# CARD-1603 Final Review round 2 (b5b016a6) - PARTIAL

Status: partial. Adversarial read, D-1 repro and mutations are complete. The Unit shards are still running.

- Subject: `origin/feat/card-task-49ac8ec7` at `c57edb47747e005b0c794b83a1a6027637fff834`. Round-2 Code task: 49ac8ec7-5663-4b0a-b535-9984c39e2f03. Original Code owner: 1d5d34a3-dc37-4882-baa5-df327e9cc63a.
- Base `e65343132`. `origin/master` is `0e7dd3905`.
  - `git merge-tree --write-tree origin/master c57edb477` is clean.
  - Master's changes do not touch `CapacityRecoveryService*`.
- `check-evidence-diff.ps1 -BaseRef e65343132 -HeadRef c57edb477` reported commits=18, entries=6, violations=0.

## Defects

### D-1 (witness, flaky; Code owner 49ac8ec7 / 1d5d34a3): the V-8 busy-arm lock-order assertion reads incidental reader SQL and goes red under load

- **Where:**
  - `tests/Antiphon.Tests/Application/CapacityObsoleteStandingTests.cs:196-204` holds the `session-busy`/`agent-busy` order check.
  - `CommandLog` (`:528-560`) overrides only `ReaderExecuting`, `ReaderExecutingAsync` and `ReaderExecutedAsync`.
  - The production row locks go through `ExecuteSqlInterpolatedAsync`, which is a non-query command (`CapacityRecoveryService.ObsoleteConsumers.cs` `TryLockLiteralNoWaitAsync`). `CommandLog` never records them.
  - So `sessionLock` and `agentLock` are the first reader statements that happen to mention `AgentSessions` and `FROM "Agents"`. Examples are the discovery reads, other reconcile steps, and background queries from the harness after `StartAsync`, which all share the interceptor. `Texts` is a plain `List<string>`.
- **Failure:** the code is unmutated (tip `c57edb477` and the env-gated copy with no mutation active), on server2 with three shard lanes running. The assertion `agentLock should be greater than <sessionLock>` failed in 4 of 7 executions:
  - class `none-ST`: `agent-busy`, 1524 < 2332
  - class `nocas-ST`: `agent-busy`
  - method run m1: `session-busy`, 2142 < 2217
  - class run c1: `agent-busy`, 982 < 2747
  - m2, m3 and c2 were green.
- **Diagnostic:** I added a scratch print of every logged statement that mentions `AgentSessions`, `FROM "Agents"`, `FOR UPDATE` or `pg_try_advisory`, then ran 3 method runs, all green.
  - The log held **0** `FOR UPDATE` statements.
  - The first `AgentSessions` match was an AgentSessions entity select (#0).
  - The first `FROM "Agents"` match was an unrelated `WHERE a."AlwaysOn"` sweep query (#7).
  - The only lock in the log was the provider try-lock (#23).
  - The plan's V-8 oracle ("successful session/agent lock acquisition preceding that command") is not implemented.
- **Why it matters:**
  - CP-2 is a closed checkpoint row, and the method-scoped Mutation/SourceLanding stage will flip on it at random.
  - The check does not witness the session(4) -> agent(5) -> provider(7) order it claims (plan V-8: "successful session/agent lock acquisition preceding that command").
  - The provider-first control is still caught by the busy path's `ShouldNotContain("pg_try_advisory_xact_lock")`. The success-path ordering is noise.
- **Fix:**
  - Record `NonQueryExecuting`/`NonQueryExecutingAsync` too, in a thread-safe log.
  - Assert the index order of the exact lock texts (`FROM "AgentSessions" WHERE "Id" = ... FOR UPDATE NOWAIT`, then `FROM "Agents" WHERE "Id" = ... FOR UPDATE NOWAIT`, then `pg_try_advisory_xact_lock`).
  - Filter those texts to the cleanup's own connection or transaction.
  - Then show red with a mutation that swaps the two row locks.

## Round-1 defects: verification

| Round-1 | Status | Evidence (env-gated copy of c57edb477, one build, CP-1 / CP-2 classes) |
|---|---|---|
| D-1 finished/expired grant held forever | **closed** | Details are below this table. |
| D-2 V-2 exemption arms | **closed** | `noexempt` turns all 10 new dropped-/parked- arms red. |
| D-3 CAS witness | **closed** | `nocas` turns the queue twin `C1603_queue_cleanup_loses_compare_and_set_safely` red, and V-10 red on the standing side. The boundary is now after the reread (`ObsoleteConsumers.cs:157-161`, `:334-338`). |
| D-4 legacy null SessionId | **closed** | `noloop` turns `legacy-null-session-empty` red. The held arm grants the same wait. |

D-1 details:

- **Fix:** `CapacityRecoveryService.cs:158-172` holds the grant only when the wait is unfinished, is a grant candidate and its grant has not expired. Otherwise it calls `ClearGrant` with no re-arm and no re-key. `:185-188` adds an expiry fallback to the demand-loss key mismatch.
- **My scratch repro through the real queue:**
  - The path is non-held Ui head -> real flush selects X -> `ObserveTranscriptAsync` TurnEnd -> real `CancelAsync` -> due competitor.
  - At **+1 s** (expiry is 120 s) the first reconcile grants the competitor: `granted=competitor W=Progressed key=same ver+0 sel=X`.
  - The unfinished arm with the gate held is held at +1 s and +61 s, and the competitor is granted at +121 s. After the gate is released, W is detached to Ready, re-keyed, with selection null.
- **Under `holdforever`** (the round-1 code), the official finished-drop, finished-parked and gate-busy-past-expiry arms, the CAS twin and my repro are red. `noexpiryguard`, `rearm` and `nomismatchexpiry` each turn their own arm red.

## Mutations (C1603_MUT, scratch copy `bin-rb5mut/`, one build)

```
MUT none-Q rc=0 total: 54 failed: 0   (52 + 2 scratch repro arms)
MUT none-ST rc=2 total: 18 failed: 1  (V-8 agent-busy = D-1, unmutated)
MUT holdforever-Q rc=2 total: 54 failed: 5   finished-drop, finished-parked, gate-busy-past-expiry, queue CAS twin, repro finished-drop
MUT noexpiryguard-Q rc=2 failed: 1   gate-busy-past-expiry
MUT nofinishguard-Q rc=2 failed: 1   queue CAS twin only (see A-1)
MUT rearm-Q rc=2 failed: 1           gate-busy-past-expiry
MUT nomismatchexpiry-Q rc=2 failed: 1  demand-loss-key-mismatch-past-expiry
MUT noexempt-Q rc=2 failed: 10       all new V-2 dropped-/parked- arms
MUT nocas-Q rc=2 failed: 1           C1603_queue_cleanup_loses_compare_and_set_safely
MUT nocas-ST rc=2 failed: 2          V-10 + V-8 agent-busy (D-1 flake)
MUT noloop-Q rc=2 failed: 1          legacy-null-session-empty
MUT pre-Q (master product) rc=2 total: 52 failed: 30
MUT pre-ST (master product) rc=2 total: 18 failed: 9
```

Pre-fix means the master/base `CapacityRecoveryService.cs`, with `ObsoleteConsumers.cs` removed. The new tests compile unchanged against it.

- **Red at pre-fix:** V-1 4/4, V-6 4/4, and the gate-busy and key-mismatch arms.
- **Green at pre-fix, as expected:** finished-drop and finished-parked. Base revoked a finished wait's grant on the next tick, so D-1 was a round-1 regression and its red is the `holdforever` run.

PCs stay pending for method-scoped SourceLanding Mutation. These scratch runs discharge none of them.

## Advisories

- **A-1:** the D-1 witness advances 121 s before its first reconcile, so it cannot tell "released at once" from "released at expiry".
  - Under `nofinishguard`, a finished wait holds until expiry: my repro printed `after+1s granted=W`.
  - Only the queue CAS twin's last assertion (`GrantedWaitId.ShouldBeNull()` after one reconcile) catches that.
  - Add a pre-expiry window to the finished arms.
- **A-2:** some arms seed their state directly instead of reaching it through a production path:
  - `finished-parked` stamps `DeliveryAttempts` directly. The author notes that a real parked selection redeems first and clears the grant, so this shape needs something like a lowered `MaxDeliveryAttempts`.
  - The key-mismatch arm rotates `ActionKey` directly.
  - V-2's new arms seed Canceled and receipts directly.
  - Each is a defensive-state seed and is labeled as such.
- **A-3:** the queue detach now reads the wait twice in a row (`ObsoleteConsumers.cs:136` and `:147`). The CAS makes the second read redundant. It is harmless.
- **A-4:** V-6's at-most-five reconcile loop (`CapacityObsoleteStandingTests.cs:74-81`) cannot go falsely green: the assertion stays Superseded, and `nostanding`/pre-fix stay red. It accommodates the busy try-lock by design.

## Unit shard log (live, one build bin-rb5b016a/, dotnet exec under build-slot.ps1)
```
SHARD a-rsc3 start 2026-10-29T21:32:46Z
SHARD b1-RollingVRS start 2026-10-29T21:32:46Z
SHARD b4-Ch1 start 2026-10-29T21:32:46Z
SHARD b4-Ch1 end rc=0 wall=1081s waited=0s  total: 255  failed: 0  succeeded: 255  skipped: 0  duration: 17m 57s 891ms 
SHARD b3-Retired start 2026-10-29T21:50:47Z
SHARD a-rsc3 end rc=0 wall=1691s waited=15s  total: 39  failed: 0  succeeded: 39  skipped: 0  duration: 27m 52s 479ms 
SHARD b5-Ch2 start 2026-10-29T22:00:57Z
SHARD b1-RollingVRS end rc=0 wall=1999s waited=0s  total: 77  failed: 0  succeeded: 77  skipped: 0  duration: 33m 16s 455ms 
SHARD c6-HLM start 2026-10-29T22:06:05Z
SHARD b3-Retired end rc=0 wall=1157s waited=195s  total: 38  failed: 0  succeeded: 38  skipped: 0  duration: 15m 59s 920ms 
SHARD a-rsc2 start 2026-10-29T22:10:04Z
SHARD b5-Ch2 end rc=0 wall=1429s waited=120s  total: 302  failed: 0  succeeded: 301  skipped: 1  duration: 21m 45s 760ms 
SHARD c1-C1 start 2026-10-29T22:24:46Z
SHARD c6-HLM end rc=0 wall=1166s waited=30s  total: 740  failed: 0  succeeded: 738  skipped: 2  duration: 18m 53s 288ms 
SHARD c7-OPQ start 2026-10-29T22:25:31Z
SHARD a-rsc2 end rc=2 wall=960s waited=60s  total: 216  failed: 1  succeeded: 215  skipped: 0  duration: 14m 56s 469ms 
SHARD x-c8-R start 2026-10-29T22:26:04Z
SHARD c1-C1 end rc=0 wall=663s waited=15s  total: 173  failed: 0  succeeded: 170  skipped: 3  duration: 10m 44s 616ms 
SHARD c3-AB start 2026-10-29T22:35:49Z
SHARD x-c8-R end rc=0 wall=703s waited=15s  total: 499  failed: 0  succeeded: 499  skipped: 0  duration: 11m 25s 441ms 
SHARD c5-EFGIJK start 2026-10-29T22:37:47Z
SHARD c7-OPQ end rc=0 wall=937s waited=0s  total: 763  failed: 0  succeeded: 755  skipped: 8  duration: 15m 34s 490ms 
SHARD a-rsc5 start 2026-10-29T22:41:08Z
SHARD a-rsc5 end rc=0 wall=193s waited=0s  total: 121  failed: 0  succeeded: 121  skipped: 0  duration: 3m 11s 603ms 
SHARD a-rsc1 start 2026-10-29T22:44:21Z
SHARD c3-AB end rc=0 wall=710s waited=0s  total: 553  failed: 0  succeeded: 550  skipped: 3  duration: 11m 48s 671ms 
SHARD x-c10-TUV start 2026-10-29T22:47:39Z
SHARD a-rsc1 end rc=0 wall=251s waited=0s  total: 40  failed: 0  succeeded: 40  skipped: 0  duration: 4m 09s 776ms 
SHARD x-c11-W start 2026-10-29T22:48:32Z
SHARD c5-EFGIJK end rc=0 wall=733s waited=0s  total: 492  failed: 0  succeeded: 479  skipped: 13  duration: 12m 09s 822ms 
SHARD c4-D start 2026-10-29T22:50:00Z
SHARD x-c11-W end rc=0 wall=262s waited=0s  total: 299  failed: 0  succeeded: 298  skipped: 1  duration: 4m 19s 826ms 
SHARD c2-C2 start 2026-10-29T22:52:54Z
SHARD c2-C2 end rc=0 wall=251s waited=0s  total: 201  failed: 0  succeeded: 201  skipped: 0  duration: 4m 09s 448ms 
SHARD a-rsc4 start 2026-10-29T22:57:05Z
SHARD c4-D end rc=0 wall=564s waited=0s  total: 580  failed: 0  succeeded: 574  skipped: 6  duration: 9m 22s 562ms 
SHARD x-c9-S start 2026-10-29T22:59:24Z
```

Red comparisons:
```
RED c1105-master  total: 1  failed: 1  succeeded: 0 
RED c1105-tip  total: 1  failed: 1  succeeded: 0 
```
