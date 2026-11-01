# Review 114aba52 — CARD-1610 S1 (Final, S1 scope), subject 18c32868ac4abbbae3772442b6a828c6f7d4fd44

Outcome: CP-3..CP-9 green and validated at the tip, but the slice has 3 defects. The worst one: the Failed-live hold is keyed to any historical Failed task on a reused pooled session.

Base `98fc3ceb6440a17b68cafe3ecc21c76d190b18ca` (= origin/master tip at review). Branch origin/feat/card-task-36e17e71 = 18c32868a.

## Evidence (fresh, server2/Linux)

Run `.antiphon/checkpoints/20261030-061317-429f` at 18c32868a: GREEN, `validate` -> `CHECKPOINT SOURCE VALID source=18c32868ac4abbbae3772442b6a828c6f7d4fd44 rows=7`.
All rows: sourceState=clean, slot=granted, failed=0, skipped=0.

| CP | executed/passed | body |
|---|---|---|
| CP-3 TaskSupervisionVisibilityTests/C1610_* | 8/8 | 74.8 s |
| CP-4 AgentTaskPoolTests/* | 48/48 | 88.8 s |
| CP-5 AgentTaskOverdueDeadlineTests/* | 36/36 | 39.3 s |
| CP-6 AttentionServiceTests/* | 167/167 | 69.7 s |
| CP-7 SeatOccupancyAttentionTests/* | 10/10 | 34.3 s |
| CP-8 guards | 23/23 | 10.2 s |
| CP-9 TerminalSeatReleaseTests (2 methods) | 2/2 | 7.0 s |

Wall 11m37s. Counts match the author's receipt.

Red-first, rerun at the parent `be042feef295c4c5fa9ac15f12d2b6d1bdd436d3` (scratch worktree, run 20261030-062635-9a30, verdict RED as intended):

- CP-1 executed=8 passed=2 failed=6. Red: failed-fresh, failed-unknown, taskless-ttl, taskless-cap, restart-resolution, failed-dedup. Green: the real-idle and source-custody guards.
- CP-2 executed=1 failed=1 (`should not contain`, the old kill).
- So 7 new witnesses fail on the parent through the real janitor/attention/detail path. They are not stubs.

Mutations on the tip. I made one scratch build with env-toggled mutations; the scratch worktree is now removed.

- M1, janitor releases a Failed-but-live session (drop failedLive/recheckFailed): **RED**. failed-fresh and failed-unknown fail. AgentTaskPoolTests stays 48/48.
- M2, drop the SessionStopStuck de-dupe (AttentionService.cs:299): **RED**. The existing AttentionServiceTests `overlapping_session_leaks_have_one_full_feed_row_until_resolved` fails, (False,False) and (True,False). The new visibility arms stay green.
- M3, treat an unproved-idle session as idle (drop Created/Stopping/StartedAt-default from both uncertain sets): **SURVIVES**. All 8 committed arms and AgentTaskPoolTests 48/48 stay green. Only my scratch probe `rv-stopping` turned red. See D-2.

Other checks:
- check-evidence-diff base..tip: commits=5, violations=0.
- No csproj/props/targets or allowlist changes.
- master: the tip fast-forwards (master == base); merge-tree is clean.
- rc/2026-10-27 (ade542225): a7725ee90, be042feef, 44e0ebc17 and 18c32868a cherry-pick cleanly, without the plan commit. The enum slots 61/62 and 84/85, SeatOccupancyState, RemoteInventoryPending and SessionRestartBoundary all exist there. I did not compile it on rc.
- Land target per plan D-6: master first, then a separately reviewed rc port for S1.

## Defects

### D-1 (High): the Failed-live hold binds to any historical Failed task on a reused pooled session

Where:
- `server/Application/Services/AgentTaskDispatcher.cs:9307` (failedLiveSessionIds) and `:9427` (recheckFailed, plus the loop after it).
- `server/Application/Services/AttentionService.Seats.cs:187` (BuildFailedTaskSessionHeldItemsAsync), plus the SeatIdle/SlotOrphan suppression at `AttentionService.cs:307`.
- `server/Application/Services/AgentTaskService.cs:2844` (detail FailedSession).

The selector is "any task with Status=Failed whose AgentSessionId is this session, and the session is unfinished". It has no newest-task, open-owner or generation check.

Failure:
1. A Shared task settles Failed with its session live.
2. Settlement pools it warm (`AgentTaskReplyService.cs:2854` PoolWarm).
3. A later task reuses the same session (`AgentTaskDispatcher.cs:8561` `claimed.AgentSessionId = session`) and Succeeds.
4. The session goes warm-idle again.

From then on, the janitor never retires that idle session, by TTL or by cap. The feed keeps a FailedTaskSessionHeld row for the old task, and its SeatIdle/SlotOrphan rows stay suppressed. While a newer owner is Working, a "Failed ... seat held" row also appears for a session that task now owns.

Probe at the tip: a scratch arm `rv-reuse` (Failed 2 h ago, a later Succeeded task on the same session, TurnEnd-idle, past TTL) printed `killed=False failedHeldRows=1`.

Why it matters:
- Plan D-10 says "A positively idle warm session retains existing TTL/cap retirement" and "Resolution requires proved idle/end or a new open owner".
- Every warm session that ever had a Shared failure gets unbounded life. That is the leak the janitor exists to bound.
- The row's own text says "held until ... new ownership", yet it survives new ownership.

Fix:
- Treat a session as Failed-live only when the Failed task is the session's current custody: no later task bound to that session or agent, and the task attempt belongs to the current session generation.
- Use one shared predicate in the janitor, the attention projection and task detail.
- Add a witness arm: historical Failed, then a later Succeeded on the same session. The session must retire, with no FailedTaskSessionHeld row. Amend the V-1 count.

### D-2 (Medium): no committed witness for unproved-idle retention

Where: `AgentTaskDispatcher.cs:9296` (uncertainSessionIds) and `:9412` (recheckUnfinished). Nothing in `tests/Antiphon.Tests/Application/TaskSupervisionVisibilityTests.cs` covers this.

Failure: mutation M3 (a Created or Stopping session, or one with no generation, past TTL is treated as idle and killed) keeps CP-3 8/8 and CP-4 48/48 green. My scratch arm `rv-stopping` was green at the tip and red under M3.

Why it matters: the brief and plan D-10 require "uncertain-observation retention" coverage. This is the core new safety branch, and it can regress silently.

Fix: add Stopping, Created and remote-inventory-pending (fake directory) arms. Each asserts no stopper call, the row is kept and the session is unchanged. Amend the V-1 count and the CP rows.

### D-3 (Medium): absent transcript evidence is still treated as proven idle

Where:
- `AgentTaskDispatcher.cs` protectedAgents / recheck (`:9412` onward). A Running session with StartedAt set and no TranscriptEntries reads not-Working, so it reaches `KillPooledSessionAsync` at `:9468`.
- `TaskSupervisionVisibilityTests.cs:214` RealIdleAsync seeds no transcript at all and asserts `Killed.ShouldContain`.

Failure: if transcript ingestion is missing or lagging for a live pooled session, the janitor kills it.

Why it matters: plan D-10 says "uncertainty means absent transcript evidence ... These cannot prove idle and are also retained." The real-idle guard pins the opposite behaviour.

Fix: either
- require positive idle evidence (a TurnEnd after the last activity) before the kill, and seed a TurnEnd in real-idle plus add a no-transcript retention arm; or
- have Plan explicitly amend D-10. That decision belongs to the caller if Code disagrees.

## Advisories (no rerun needed)

- **A-1, 5 s tripwire.** `test-duration-tripwire.ps1` on CP-3's TRX flags failed-fresh at 8.5 s (author: 11.7 s; my scratch run: 10.7 s). TRX startTime shows it always runs first. failed-unknown runs the same path in 0.46 s. So this is one-time first-test warm-up (TestDbFixture/EF/host), not a timeout wait. No Slow row is justified. Optionally warm the fixture in a class hook so the tripwire stays quiet.
- **A-2, attention cost.** BuildFailedTaskSessionHeldItemsAsync runs an unbounded join of all Failed tasks against unfinished sessions on every attention GET. It has one row per task, so N failures on one session make N rows. Consider capping it and deduping per session once D-1 is fixed.
- **A-3, write churn.** RecordPooledWorkingPastTtlAsync rewrites the incident Message (`last=`/`age=`) every tick, so there is one DB write per such session per sweep. The size is bounded (one incident per session generation); it just churns.

## Invariant audit

These checks hold:
- No new path stops, kills or releases. The new incident and attention rows are read-only detection, and no KillSession action is offered.
- The pool-release sweep (`ReleaseUnownedPoolDelegatesAsync`) and the Failed-task settlement release are unchanged.
- TaskDeadlinePolicy (20 min model wait, 90 min tool) and the CARD-1604 token_count logic are untouched.
- The SessionStopStuck single-row rule holds, and the existing C691 test witnesses it.
- PooledSessionWorkingPastTtl dedupes per session+generation across ticks and restarts (restart-resolution arm). FailedTaskSessionHeld is computed, so it survives a restart.
- Row text carries IDs, the agent name and FailureReason, with no secrets or private notes.
- The API is additive: new enum values 62/63 and incident 85, and an optional `failedSession` field. The client types and attentionVisuals were updated.
- No tests were removed. The only changed existing test is the plan-designed janitor rename (red-first at the parent), and the 44e0ebc17 test edit is harness DI wiring only.
- No S2-S4 files are present.

Exceptions: D-1 breaks the "positively idle retires" rule, and D-3 contradicts the plan's uncertainty rule.
