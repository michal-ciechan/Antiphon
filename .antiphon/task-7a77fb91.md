# CARD-1605 plan design review (task 7a77fb91)

Subject: plan `docs/superpowers/plans/2026-10-29-card-1605-report-store-budget-plan.md`,
task d8d36ebe-e8f1-4ff1-a64a-035292cc0fb2, branch `feat/card-task-d8d36ebe`, tip
`0d517b525121d7f7c6c2766ace2f509079a4328b` (base `f8bddb6efda7b0e8f50f3b45744087e52188bc66`).
Read-only design review on server2/Linux. Diff is one docs file (+345 lines).
`check-evidence-diff.ps1` reported commits=2, violations=0 (exit 0). `merge-tree` against origin/master: clean.

Verdict: the core design is sound, but four defects must be fixed before TestDesign.

## Answers to the brief

1. **Root cause: confirmed.** In `AgentReportStore.cs:30-31` the 2 s timer starts before
   `ResolveRootAsync` (:39). `GitProcessGate.EnterAsync` (`GitProcessGate.cs:28`) waits only on the
   caller token. Git's own 15 s timeout is created only after the gate lease and `Process.Start`
   (`GitWorkspaceService.cs:1160-1167`), so gate queueing and slow spawns use up the store budget.
   Splitting preparation from I/O is the smallest sound fix:
   - Preparation bounded only by the caller token is unbounded at the gate, because callers pass
     host stopping tokens.
   - A retry does not move the timer and doubles the Git load.
   - Excluding only the gate wait would change the global `GitWorkspaceService`.
   - Optional simplification (A-1): use one CTS and re-arm it with `CancelAfter(IoBudget)` at the
     boundary (`CancelAfter` resets a pending timer). The reason can come from the existing `phase`
     variable.
2. **CARD-1032 is kept.**
   - V-3 runs the production `DefaultIoBudget`. The `write` hook gets the I/O token, the token
     cancels at 2 s, and the 2.5 s delay is cut short, giving `io-budget-expired`. A raised budget or
     a timer started after the hook lets the delay finish and publish, which turns V-3 red.
   - R-1 `Failures_and_cancellation_never_publish_a_bad_pointer(budget)` (IoBudget=0) still yields
     `io-budget-expired`, via the plan's boundary token check or `WriteAsync` on a cancelled token.
   - Gap (A-3): V-3 cannot detect a *lowered* I/O budget. Only R-1's
     `DefaultIoBudget.ShouldBe(2s)` guards the constant.
3. **The 30 s ceiling is arbitrary, and the plan admits it is provisional.** Its justification is
   mis-grounded, and the caller wait sites are not analysed. See D-1.
   - On expiry the store returns `preparation-budget-expired` with the same warning log. Settlement
     then falls back to the legacy spill, or to Result-only for small reports. This is the same
     logged-only loss as today, only rarer. The plan rejects retry and an attention item, which is
     acceptable.
4. **RED witnesses.**
   - V-1 really goes red. On today's code the 2 s token cancels the gate wait, so `Path` is null
     with `io-budget-expired` before the 2.5 s release.
   - V-2 really goes red. `RunCoreAsync` kills the wrapper and rethrows because the caller token
     (the store budget) was cancelled.
   - V-7 `readback` is a stub against its named control. See D-2.
   - Checkpoints: CP-1 and CP-2 each have their own isolated build; CP-3..CP-5 reuse CP-2 (same
     slice). Each row has one exact filter in the working `(A*)|(B*)` grammar.
   - Counts checked against source:
     - R-1 = 1 + 11 + 5 + 2 + 1 + 1 + 7 = 28; CP-2 = 28 + 18 = 46.
     - R-2 = 7 + 1 = 8.
     - R-3 = 1, at `VerificationRoundDeliveryTests.cs:812`.
5. **Callers.** No consumer branches on the reason string; grep finds only the test at
   `AgentReportStoreTests.cs:207`.
   - `ResolveReportFileAsync` (`AgentTaskReplyService.cs:3660-3675`): spill fallback unchanged.
   - `EnsureAsync` (`CardDoneArtifactPreservation.cs:44`): any failure still gives
     `artifact_unpreserved`.
   - `EnsureSnapshotReportAsync` (`AgentTaskLandNotificationService.cs:403`): still
     `completion_report_unavailable` and RetryPending.
   - The semantics are unchanged except for timing (D-1) and the undefined unrelated-cancellation
     outcome (D-3).
6. **Risks.**
   - Shared gate concurrency, the head-of-line effect and lock hold: D-1.
   - Category, registry and platform gate: D-4.
   - Test clock: real-time `CancelAfter` is acceptable, because the CARD-1032 contract is a
     real-time stall.
   - Windows vs Linux: the fix is OS-neutral. V-2 is Linux-only (A-5).
   - rc and master: D-7 covers both. That is correct, given the card's `rc` label.

## Defects

**D-1 (plan §Decisions D-2, §Risks, §Provisional durations): the 30 s preparation ceiling has no analysis of where callers wait.**

- **Where:** D-2 says 30 s "allows queue/start overhead around the existing 15-second Git command
  allowance". But preparation is a sequence of separately gated Git commands:
  - Typical: 3 commands in `ResolveRootAsync` plus 4 in `EnsureIgnoredAsync`.
  - Worst case: about 14 (second candidate, configured root, exclude path plus re-check).
  - Each command re-enters the width-8 gate queue. So 30 s is an aggregate cap of about two Git
    timeouts, not "one command plus overhead".
- **Failure:** `AgentTaskLandNotificationHostedService.cs:100-110` reconciles every pending note
  serially. `ReconcileAsync` calls `EnsureSnapshotReportAsync`, which calls `StoreAsync` (:403).
  Scenario on a loaded host with the gate saturated (the card's own premise):
  - Three completion notes have a missing snapshot report.
  - Each now spends up to about 32 s in the scan instead of up to 2 s.
  - Every other land or completion note waits about 90 s per pass, and this repeats on each
    RetryPending attempt.
  - Settlement also holds the per-session settle lock (`AgentTaskReplyService.cs:90-108`) across
    `ResolveReportFileAsync` (:1278). The completion therefore lands up to about 30 s later.
- **Why:** the plan claims "unchanged caller outcomes", but it changes caller latency by 15x and
  never states this or bounds it. The D-2 acceptance rule ("prefer roughly 2x loaded preparation
  p95 … retaining 30 s requires a stated exception") names no threshold, no decider, and no order
  relative to CP-5.
- **Fix:**
  - Enumerate the three caller wait sites and the lock or serial loop each holds.
  - Either accept the worst case explicitly with a figure, or give the snapshot-regeneration and
    scan path a smaller allowance.
  - Restate the 30 s rationale as an aggregate over N gated commands.
  - Make the D-2 acceptance concrete: the formula, the bucket (loaded, preparation), and that a
    value change after CP-5 reruns CP-2/CP-5.

**D-2 (§RED witnesses V-7): the `readback` case cannot go red for the "skipped final token check" control.**

- **Where:** the plan prescribes empty bytes only for the `existing-read` case.
- **Failure:** in the `readback` case with a non-empty body:
  - The hook cancels the caller and returns normally.
  - `IsUsableAsync` then calls `ReadExactlyAsync(actual, cancelledToken)` (`AgentReportStore.cs:105`).
  - On a non-empty buffer that throws `OperationCanceledException`, so the case passes even when
    the final `ThrowIfCancellationRequested` is removed.
  - A zero-length `ReadExactlyAsync` does not observe the token. That is exactly why the fast-path
    case needs empty bytes.
- **Why:** TestDesign is told to bind a "skipped final token check" control, and one of V-7's two
  cases cannot fail under it.
- **Fix:** both V-7 cases use an empty body (or another seam that makes readback succeed without
  observing the token). State why, and require the cycle to show the mutant red in each case.

**D-3 (§D-3): the outcome of an unrelated `OperationCanceledException` is undefined.**

- **Where:** today `AgentReportStore.cs:74-75` maps *every* non-caller OCE to a result, so the store
  never throws. D-3 says such an OCE "must not masquerade as timeout" but does not say what it
  becomes.
- **Failure:** if Code rethrows it, `ResolveReportFileAsync` (`AgentTaskReplyService.cs:3670`) has
  no OCE handler. Settlement `OnTurnEndAsync` then throws while the host token is live, a new
  failure mode in a path the plan calls unchanged.
- **Fix:** specify a non-throwing reason (for example `canceled`, with `Path == null`). Add a
  witness: a hook throws a fresh-token OCE, and the result is non-null with a non-budget reason and
  no throw.

**D-4 (§Slices S2, §Checkpoints, §Performance budget): classification and roster obligations for the new classes are missing from the closed list.**

- **Where:**
  - V-10 is estimated at 30-60 s, and V-1..V-4 at about 2.5-4 s per case.
  - `scripts/test-duration-tripwire.ps1` flags tests of 5 s or more unless their class is Slow.
  - A class marked Slow must be in `tests/Antiphon.Tests/slow-tests-allowlist.txt` with an adjacent
    reason, or `TestClassificationGuardTests.Registry_matches_compiled_metadata` fails
    (`docs/testing-and-build.md:365`).
  - The plan says only "Integration (R-3 also Slow)". It does not decide Slow for
    `AgentReportStoreLoadTests` and does not name `NativeLinuxGate.Require` for V-2.
  - It leaves `tests/linux-test-roster.json` rows conditional ("if new classes require entries").
  - No CP row runs `Registry_matches_compiled_metadata` or `TestLaneCategoryGuardTests`.
- **Failure:** Code adds the Load class unmarked. CP-2..CP-5 pass, but the tripwire, the next
  guard run or the Docker roster admission goes red after land. Alternatively, Code must run
  unlisted guard rows, which Review flags.
- **Fix:**
  - Fix `[Category]` and the limiter for each new class.
  - Decide whether the Load class is Slow and add its allowlist row with a reason.
  - Name `NativeLinuxGate.Require` for V-2.
  - Add roster rows.
  - Add a CP row reusing the CP-2 build:
    `/*/Antiphon.TestSupport/TestClassificationGuardTests/Registry_matches_compiled_metadata`
    plus the lane-category guard.

## Advisories

- **A-1:** a one-CTS re-arm (`CancelAfter` at the boundary) is a smaller diff than two linked
  sources. It is optional.
- **A-2:** V-10 as a permanent suite test asserts zero refusals under the production 2 s I/O budget
  on shared, loaded runners. That is a flake risk, and the 2 s case is exactly the condition the
  card describes. Consider an opt-in, env-gated measurement plus a correctness-only regression
  case. Also specify how ambient CPU, load and memory are read on Windows, or restrict the test to
  Linux.
- **A-3:** V-3 should also assert that the time from I/O start to cancellation is at least about
  1.9 s, and that the test leaves `IoBudget` at its default.
- **A-4:** order the V-1 assertions so the CP-1 failure message carries `Reason`
  (`io-budget-expired`). An "early completion" assertion that fires first would not show it.
- **A-5:** state that no OS-selected behaviour changes. `SamePath`, `Within` and
  `HasReparseAncestor` are untouched, so the `check-os-witnesses.ps1` declaration has no
  obligations. V-2 is Linux-native only and stays pending on Windows lanes; a skip is not a pass.
- **A-6:** the turn-end callers checked (`AgentSessionRuntime.cs:806-840`,
  `AgentTaskDispatcher.cs:3194/4822/4909`) wrap no outer deadline shorter than 30 s, so a caller
  cancellation will not pre-empt preparation. Re-check this if a caller gains a budget.

## Verification

- No build or test was run. The plan's V-1..V-10 tests do not exist yet: this branch is a plan
  only, and CP rows run against it would select 0 tests. The diff is one docs file.
- ordinaryScopeCompleted: None (plan-design review; no executable ordinary scope at this SHA).
- PCs (the TestDesign mutation controls) remain pending for post-land Mutation.
- Standing authority covered running on server2 without pausing.
