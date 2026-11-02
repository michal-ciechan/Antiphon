# Review b922ebf8: CARD-1638 part 1 flake-risk test budgets (round 1)

Subject: feat/card-task-8faaee74 tip 4a0abd68507a5e3105bf7b258849b1abb033890a
(code 5ce77b7ae1529be6860cf9ca8f515691838b6cfa, base e18a1177d9e042ac5986cf8bdebc5b47abb7ca11).
Verdict: one defect (D-1). The rest of the change is sound.

## D-1 JournalCustodySurvivesInterleaving budget is under 2x its first retained sample

- Where: tests/Antiphon.Tests/Scripts/RcFailedFirstExecutionTests.cs:29 (`Timeout(386_000)`) and :45
  (`ScriptHarnessOptions(TimeSpan.FromSeconds(326), ...)`), plus the comment at :27-28.
- Failure: this review's run 20261102-133640-5f80 (CP-3, server2 Linux, load average 32-38 on
  24 cores, source 4a0abd685) passed in TRX duration 240.257 s. That is 74 % of the new 326 s
  execution budget (1.36x headroom). The old 180 s budget would have failed this run.
- Why: R5 in docs/test-performance-standards.md requires about 2x the measured p95. The new value is
  2x a 163 s figure that has no retained receipt. This run is the first retained sample, and the
  test stays in the under-2x flake-risk band that CARD-1638 is meant to close.
- Fix: ExecutionBudget ceil(2 x 240.257) = 481 s and method `[Timeout(541_000)]`, keeping the 60 s
  cleanup margin. Update the comment to cite run 20261102-133640-5f80 (server2, loaded; n=2 with
  the unreceipted 163 s; provisional). The test is test-only, so no other file changes.

## Checks (a)-(e)

(a) Diff scope. All six hunks change only Timeout attributes, the ScriptHarnessOptions execution
budget and comments. No assertion, inner deadline or logic changed. The Journal_intents
close-budget comment is a text edit only, and CloseBudgetSeconds stays 150. Each value equals
ceil(2 x basis): 277.4 -> 555, 355.6 -> 712, 68.9 -> 138, 90.6 -> 182, 163 -> 326 (+60 = 386),
559.732 -> 1120. The four d47848cc figures match docs/investigations/2026-11-01-test-timing-stats-d47848cc.md
section 5 at e7a0b7ac1 exactly.

(b) Enclosing budgets. All values are finite (max 1120 s). The nightly antiphon suite watchdog is
3600 s (tests/test-execution-policy.json watchdogs.antiphon). The measured admission path
(scripts/lib/nightly-watchdog-admission.ps1) floors at 600 s and needs n>=20 calibration, so
unmeasured chunks keep 3600 s. Every new timeout is below the chunk bound, and none is made
ineffective. Existing plan rows that select these tests have importer watchdogs of at least
15 min (max(15, 3*Est)). The CARD-1609 CP-3 row that selects Journal_intents has Est 27, which
gives 81 min. There is no smaller enclosing budget.

(c) Attribute validity. TUnit 1.44.0 `TUnit.Core.TimeoutAttribute` implements `IScopedAttribute`
(read from the package metadata). Scoped attributes keep the nearest scope, so the method's 1120 s
overrides the class's 600 s, and the other VolumeRetirementReceiptTests methods keep 600 s. The
attribute is valid.

(d) Checkpoint run 20261102-133640-5f80 at 4a0abd685 (ad hoc plan .antiphon/review-b922-cp.md, one
build, slot granted):
- CP-1 TimeoutAuditTests: executed=7 passed=7.
- CP-2 C1532 + C508 (row/query): executed=3 passed=3. Durations were 9.8 s, 8.4 s and 90.8 s. The
  C508 query case at 90.8 s on Linux matches the Windows basis, so 182 s is 2.0x.
- CP-3 JournalCustodySurvivesInterleaving: executed=1 passed=1, 240.3 s (D-1).
- Verdict GREEN. validate: CHECKPOINT SOURCE VALID source=4a0abd685 rows=3, sourceState=clean,
  buildSource=verified, unlisted none.
- Not run: C1213, repeat_two and Journal_intents (long, values only, brief budget 30 min). The
  whole Unit lane was not run either.

(e) Gates:
- Land graph -Check: digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE
  count=69, approved, exit 0. No -Update. The evaluator was built through build-slot, and both
  bin-land-audit directories were removed afterwards.
- check-evidence-diff e18a1177d..4a0abd685: commits=2 entries=1 violations=0.
- merge-tree: clean onto origin/master 43ae7371d (master moved past the base) and onto
  origin/rc/2026-11-01 10a826b9d, both for the whole branch and for the code commit with
  --merge-base.

## Advisories

- A-1: CheckpointRepeatBuildIsolationTests' nightly-class-costs row stays at 300 s
  (observed-bound), below the measured 355.6 s. The planner refit is item 2, out of scope.
- A-2: the stale linux-test-roster sourceSha256 for DispatchBaseNotificationTests.Coverage.cs was
  confirmed stale at base (bf23d8d4 vs pinned e81ae803). It was not resealed, which is correct here.
- A-3 (reviewer process): `dotnet run --project tools/Antiphon.Checkpoints -- import` built the
  checkpoint tool once outside a slot, the same as the worker's note. It ran no tests.
