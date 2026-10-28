# Review f1d8b823: load-timeout fix slice 2 (waits and PhoneHome)

Subject: task b41aa7e7-d972-4920-86e4-3c7e72ad13a9, branch feat/card-task-b41aa7e7, tip 65bb292ee8eb4ff521b65a4bacc79f667189b8b9 (base aa6dfd24a51db7c02e8722a05a21817bf4df407a, 4 commits, 11 test files, no production change).
Host: server2 (Linux, 24 CPU, load 7-21 during runs). Build: one build-slot build, UseAppHost=false, OutputPath=bin-f1d8/ (152 s held). Tests ran with `dotnet exec` under build-slot leases 9e4a9064, 4b4fb7a3, 6bb17f7e.
Scope: **Interim**. The whole Unit lane was not run, as the brief instructed. No checkpoint tool receipts.

## Verdict

No regression and no weakened assertion. Every changed site still fails within a bounded time. The by-design short bounds are unchanged. The census cases are green.
The PhoneHome 45 s cap does **not** fix the flake it targets. `Finite_stall_preserves_connection_order_and_delivery` is a co-run-dependent red that is **not caused by timing**. It is inherited from base, and it now fails at the full 45 s cap instead of after 5 s.

## Runs (subject tip 65bb292ee unless noted)

| Selection | Result | Wall |
|---|---|---|
| LoadTimeoutBoundWitnessTests | 3/3 | 3 s |
| CheckpointLaunchCleanupTests | 11/11 | 5 s |
| CheckpointToolCopyCleanupTests | 16/16 | 6 s |
| HostCleanupCheckpointAuthorityTests | 17/17 | 8 s |
| ExpectationObservationAdapterTests | 12/12 | 44 s |
| WorktreeRetirementRaceTests | 54/54 | 23 s |
| C1514LandingSplitBudgetTests (C1404, C1410, C1577, C1584, G2865) | 5/5 | 52 s |
| RcRosterRepairTests | 3/3 | 12 s |
| PhoneHomeBackpressureTests whole class, alone x5 | 3 green, 2 at 5/6 | 22-65 s |
| PhoneHomeBackpressureTests whole class, 8 `yes` burners x5 | 0 green, 5 at 5/6 | 68-70 s |
| Subject, Finite_stall method alone x5 | 5/5 green (7.5-12.4 s) | |
| **Base aa6dfd24**, Finite_stall method alone x6 | 6/6 green | |
| **Base aa6dfd24**, whole class x5 | 4 green, 1 at 5/6 (same assertion, 13.5 s) | |

Finite_stall failed 7 times at the subject tip. Each failure was at line 69, `the split mention reaches the real recipient queue exactly once`, and lasted 51.97-54.10 s, which is the full 45 s cap. The test passes in 7.8-11.1 s. The other five rows took 0.9-10.4 s, and burners changed them by about 1 s.

## Priority answers

1. **PhoneHome**
   - The "stall" is a held fake bus observer (a `TaskCompletionSource`), which the test releases itself. `h.Clock` is a `FakeTimeProvider`. So no wait includes the stall's duration; each cap only covers host scheduling latency.
   - The red is bimodal: a pass at 8-11 s, or a condition that stays false for all 45 s. More time would not help: either the count is 0 (the mention was lost) or it is ≥2 (it was duplicated).
   - It appears only when the class's rows run together (method alone: 11/11 green across subject and base). It is present at base (1/5 class-wide).
   - The red rate was 7/10 at the subject and 1/5 at base. That difference is not significant (about p 0.1). The only change is the constants, and a constant cannot lose a mention, so the mechanism is implausible.
   - A per-class limiter or a larger cap would not fix it. It needs a Debug task on the co-run mention loss: record the actual queue count and body in the assertion message, and wait on an event rather than polling.
   - Advisory A-1: set `PhoneHomeSignalWait` to about 15 s. Measured whole-test passes were 7-19 s at loads up to 21, and per-wait latency is a fraction of that. The current 45 s comes from an estimate ("~22s loaded estimate"), not a measured p95 (test-performance-standards). 45 s per failure only makes this inherited red slower.
2. **Assertion strength**
   - No site became "eventually or skip". Every `WaitAsync(cap)` throws `TimeoutException`, and the `Should.ThrowAsync<PhoneHomeTransportException>` sites fail on a wrong exception type. `UntilAsync` still asserts the condition after its cap. `ProtocolRunner.StopAsync` catches only `OperationCanceledException`, so a hung loop still throws.
   - The 30 ms bound (CheckpointToolCopyCleanupTests:51) and the zero max-wait (HostCleanupCheckpointAuthorityTests:239) are unchanged.
   - Hung-class wall: the first unmet wait throws, so a row stops waiting there; the 26 sites never time out one after another. PhoneHome worst case per row is about 45 + 45 (StopAsync) + 10 (dispose), roughly 100 s. Six rows, even run serially, come to about 600 s.
   - WorktreeRetirementRaceTests: 4 rows × 45 s = 180 s. Checkpoint: 2 × 10 s.
   - All of these are far under the 2400 s lone-class ceiling.
   - The witnesses would go red if a cap reverted: the late hook is checked at 16 s and 6 s, past the old bounds. However, the PhoneHome witness tests a local `WaitHookAsync` copy, not `UntilAsync` or the real sites (minor, advisory A-3).
3. **ExpectationObservationAdapterTests**
   - The new assertions are deterministic and not weaker. `Queries < historySize` catches an N+1 that scales with history. The per-row transcript query for each of the 100 Sent rows is page-bounded by design, and both the old and new versions accept it.
   - A missing `Take` on the queue query fails `Notes.Count == 100`.
   - Gap (advisory A-2): suppose the queue query did its paging in memory (loading every row, then `Take` afterwards). `PageLimits` would still contain 101 from the outbox query, and `foreach limit <= 101` ignores queries that have no LIMIT, so the test would pass. The old 8 s timer would not have caught this either.
   - Fix: assert one limit per note table, for example two entries of 101 when the outbox and queue page queries both run.
4. **Rosters**: C1404, C1410, C1577, C1584 and G2865 are 5/5, and RcRosterRepairTests is 3/3. The new class sits in `Antiphon.Tests.TestHelpers` and changes no census result.

## Other facts

- The author report `.antiphon/task-b41aa7e7.md` is not committed on the subject branch, so the claims were checked against the commit messages and the brief.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef aa6dfd24a5… -HeadRef 65bb292ee8…` gave commits=4, violations=0.
- OS-branch witnesses: not applicable, because no OS-selected production behaviour changed.
- I left 58 `bin-f1d8*` output directories under tests/, src/ and server/ in /work/worktrees/task-f1d8b823 for the caller to remove, because the brief said "delete nothing". Logs and TRX files are in the session scratchpad.

--- review evidence ---
subjectTaskId: b41aa7e7-d972-4920-86e4-3c7e72ad13a9
reviewedSourceSha: 65bb292ee8eb4ff521b65a4bacc79f667189b8b9
reviewedSourceClean: false
ordinaryScopeCompleted: Interim
