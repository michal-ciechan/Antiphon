# CARD-1605 plan re-review, round 2 (task 141e4c6b)

Subject: `docs/superpowers/plans/2026-10-29-card-1605-report-store-budget-plan.md` on
`feat/card-task-e26fb023`, tip `0cd95a876dee9d5bccbb51b1702a5fc1fe0beaed`
(task e26fb023-32d8-43d7-beb8-86fd6608d46c). Read-only, server2/Linux.
`check-evidence-diff.ps1 -BaseRef f8bddb6ef -HeadRef 0cd95a876`: commits=6, violations=0, exit 0.
`merge-tree` against origin/master: clean. No build or test was run. This is a plan-only
branch, so the proposed V-n/CP rows select no tests yet.

**Verdict: two defects, both in the caller-wait analysis. Round-1 D-2..D-4 and all advisories
are closed. Round-1 D-1 is closed only in part.**

## 1. Round-1 closure

| Round-1 item | Status | Where it is closed |
|---|---|---|
| D-1 ceiling and wait sites | **Partly closed** | The aggregate derivation over 7-14 gated commands is in §Preparation ceiling derivation (lines 120-149). The wait inventory has three rows (151-155). Concrete acceptance is at 458-477, including a CP-2/CP-5 rerun on a value change. **Gaps:** the inventory misses the dispatcher-tick wait (new D-1 below), and the scan tradeoff reasoning is unsound (D-2 below). |
| D-2 V-7 empty body | Closed | V-7 (line 215) gives both cases `Result = ""`. Mutant red is required per case. Controls are at 234-236. Today's store has no final check after `IsUsableAsync` (`AgentReportStore.cs:56,69-72`), so both cases go red now. |
| D-3 unrelated OCE | Closed | D-3 (69-78) defines a non-throwing `new(null,"canceled")`. The decision order is at 189-194. V-11 (line 219) is discriminating: today's catch gives `io-budget-expired`. |
| D-4 classification and roster | Closed | D-9, §Classification (255-290), R-4 and CP-6. See A-2 for the roster check. |
| A-1 | Declined, with a reason (183-187) | Acceptable. |
| A-2 | Closed | D-8: V-10 is timer-free and V-12 is opt-in and native Linux. |
| A-3 | Closed | V-3 checks the at-least-1.9 s span and that there is no override. |
| A-4 | Closed | V-1 asserts its outcome first, with Reason. |
| A-5 | Closed | Lines 292-296. |
| A-6 | Closed | Settlement row: no shorter outer timer. |

## 2. The accepted 68-minute scan tradeoff

**Is it reachable?** The 68 min 16 s figure is a theoretical bound. Reaching it needs 128 eligible
profiled completion notes whose reports are all missing in one pass, each hitting the full
30 s preparation ceiling.

**Is the hazard real?** Yes, and the plan does not price it. There are two parts.

- **(a) The full ceiling is reached mainly when Git is saturated for a sustained period.** Each
  store then *fails*: `preparation-budget-expired` → `completion_report_unavailable` → RetryPending.
  - Backoff is `RetrySeconds` = 5..300 s with no attempt cap
    (`AgentTaskLandNotificationService.cs:459`).
  - Once a pass costs 32·m s, the next pass finds every backoff already elapsed (line 61 gate).
    It pays 32·m s again for no progress.
  - That is a livelock of the single serial scan (`AgentTaskLandNotificationHostedService.cs:91-110`).
    Every AwaitingReceipt and land/completion note behind those rows waits, including
    orchestrator completion notes.
- **(b) Many rows need regeneration at once on realistic triggers**, for example loss of the
  report root at a host move or volume recycling. Even m = 10-20 gives 5-11 minutes of stall per
  pass, repeated.

**The plan's reason for rejecting a regeneration cap is unsound.** It says a cap "would make
recovery fail under the same Git contention". But regeneration is the one caller that already has
durable, unlimited retry. A short cap delays only that note. The 30 s cap delays every other note
in the scan.

**Settlement is different.** It is one-shot, with a lossy spill/Result fallback. That is where the
long preparation budget earns its cost. → **D-2.**

## 3. The p95 <= 15 s and max < 30 s threshold

The threshold is measurable:

- The 20-sample nearest-rank figure is defined.
- The worse bucket is used.
- Preparation expiry cannot be dropped.
- `max < 30` is effectively the no-expiry rule.

It is honest that 30 s is policy and not 2x p95. But it does not discriminate. The V-12 fixture
puts 16 stores behind a barrier on a width-8 gate, running sub-second Linux metadata commands. Its
p95 will be far below 15 s, so the rule cannot fail for any plausible implementation, and it says
nothing about the Windows condition behind the card. See A-1.

## 4. Roster and checkpoints

- **Counts are correct.**
  - V-1..V-11 give 1+1+1+2+1+4+2+2+4+1+2 = 21 cases. With R-1's 28 cases, CP-2 is 49.
  - Total: 49+8+1+1+2 = 61.
  - Budget `directTestMethods` is 3 in S1 and 11 in S2. Load is 1.
- **Each CP row has one isolated build or same-slice reuse, and one exact filter** in the working
  `(A*)|(B*)` grammar.
- **CP-6 matches what exists.**
  - The classes resolve to `tests/Shared/TestClassificationGuardTests.cs` (linked into
    Antiphon.Tests, `Antiphon.TestSupport`, one `[Test]`) and
    `tests/Antiphon.Tests/TestHelpers/TestLaneCategoryGuardTests.cs` (one `[Test]`).
  - No other class shares either prefix.
- **Slow and census entries** follow the allowlist pair layout and the `AgentReportStoreTests`
  roster convention (`tests/linux-test-roster.json:2307-2330`). Both are correct.

## Defects

**D-1 (§Preparation ceiling…, settlement row, line 153): the inventory omits the global dispatcher tick and states the opposite.**

- **Where:** the row says "`IsSettleInFlight` lets the dispatcher skip that seat instead of
  blocking its tick" and "the lock is per session, not a fleet lock". But:
  - `AgentTaskDispatcher.TickAsync` (:373-376) runs `RunMaintenanceAsync` before
    `DispatchQueuedAsync`.
  - Maintenance runs `FailDeadSessionTasksAsync` (:435) and `SettleDeferredReportsAsync` (:462).
  - Those await `_replies.OnTurnEndAsync` inline at :3194, :4822, :4846 and :4909.
  - `IsSettleInFlight` (:4807) skips only when *another* path already holds the lock.
- **Failure:** under the card's loaded-Git premise the live observer misses k turn ends, and the
  sweep settles them itself. Each settlement now spends up to 32 s in `StoreAsync`, serially.
  Fleet-wide queued-task admission in that tick waits up to 32·k s, against 2·k s today. This
  repeats on later ticks for further sweep-settled sessions.
- **Fix:**
  - Add a fourth wait-site row for dispatcher maintenance → `OnTurnEndAsync`, with a 32·k s figure.
  - Correct the "not a fleet lock" and "skip" claims.
  - Explicitly accept the delay, or bound it. Do not leave it implied.

**D-2 (§Decisions D-2, scan row line 155, §Risks line 500): the scan tradeoff is accepted for a reason that does not hold, and it can livelock.**

- **Failure:** under sustained Git saturation, each eligible profiled note with a missing report
  costs 32 s per pass and fails. The serial scan stalls for 32·m s on every pass (backoff of
  300 s or less has already elapsed). Every unrelated note is delayed indefinitely, and there is
  no progress. Today the same failure costs 2 s per row.
- **Why:** regeneration already has durable unlimited retry (RetryPending,
  `RetrySeconds` ≤ 300 s), so a short cap there loses no data. The 30 s budget belongs to
  one-shot settlement.
- **Smallest fix:**
  - In `EnsureSnapshotReportAsync` (`AgentTaskLandNotificationService.cs:394-414`), wrap
    `reports.StoreAsync` in a CTS linked to `ct`, with a short stated regeneration allowance
    (about 5 s total, justified in the plan).
  - Catch `OperationCanceledException when (!ct.IsCancellationRequested)` locally and return
    `completion_report_unavailable`.
  - The local catch is required. `ReconcileAsync`'s catch excludes OCE (:372). The hosted loop
    would otherwise only log, without advancing `NextAttemptAt`, and retry the row every pass.
  - Add one witness: a held private gate plus two eligible rows in one scan. The first row reaches
    RetryPending with `NextAttemptAt` set within the allowance, and the second row is reconciled
    in the same pass.
  - Update the scan wait figures and the "no caller changes" statement.
- **Alternative:** a per-pass breaker that defers remaining regenerations after the first
  preparation expiry. It needs more code.

## Advisories

- **A-1:** rename the CP-5 rule "fixture preparation sanity bound". It does not validate 30 s and
  cannot fail on a plausible implementation. Keep it, but do not call it production acceptance.
- **A-2:** R-4 says "source census reconciliation separately checks the two new Linux roster
  rows". No executable consumer of `tests/linux-test-roster.json` exists in this tree outside
  docs. Name the check, for example Review recomputing `sourceSha256` and `directTestMethods`
  against the committed files. Otherwise it is either an unlisted check or none.
- **A-3:** if D-2 is fixed as suggested, add the witness's class to CP-3 or a new CP row and
  update the 61 roster.

## Verification

- Ran: `check-evidence-diff.ps1` (0 violations) and `merge-tree` (clean). Everything else was
  source reading at 0cd95a876.
- ordinaryScopeCompleted: None. No executable ordinary scope exists at this plan-only SHA.
- PCs stay pending.
- Standing authority covered working on server2 without pausing.
