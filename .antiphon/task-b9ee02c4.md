# Debug b9ee02c4: C849 red-m6

Start/source: 13565c5a480d8f30a52aa4679a5f1681ef2a8c70, Linux runner mirror.
Assigned scope: reproduce C849 alone 20 times, compare pre-repair fixture, repair the wrong side without weakening custody assertions; run C84x, roster/census selections and land graph Check.

Initial inspection: m6 replaces the INT handler with an early return. red-m6 is 1 only if the group witness is installed and its full release contract fails. A failure before arming cannot count as a detected mutation. No production defect is attributed yet.

Evidence is kept under .antiphon/evidence/b9ee02c4/ (ignored). Native tests and live qualification are outside this task.

## Baseline red

At 13565c5a480d8f30a52aa4679a5f1681ef2a8c70, the gated isolated slot-round2 harness produced driver-ready=1, red-m1=1, red-m5=1, red-m6=0, red-m7=1. The m6 subrun installed the INT trap and returned exit=2 with all release facts=1 in one second: the current contract therefore accepts the no-op handler. Raw baseline: .antiphon/evidence/b9ee02c4/baseline.log (25-second leased execution).

Production scripts/c590-remote.sh SHA-256 is identical at pre-repair 3fb3c113e8b2554ccd5789e00375dbfd69697981, earlier fixture-repair branch origin/feat/card-task-c083bd50, and this rc tip: 180bcbdadb39543ee704276672ff87b7ea56db54f112e20e2c0c5bf0edb51328. The repaired harness bytes are also identical between the earlier repair branch and rc (9e8d3c50685a7ab800b733b1122ba4a61815873a25f91d6345a52f9a6f0769de). No CARD-1614/1618/1619 interaction is present in these inputs.

The first repeat launcher attempted unavailable python3 and ran no tests (exit 1); replaced with a bash/awk driver, retaining the failed invocation evidence. Twenty slot-round2 executions now run sequentially under one real build-slot lease, with no other task-owned build/test in flight. This repeats the exact shell harness and all existing C849 facts, without paying assembly startup for each sample.

## Twenty isolated repetitions

Frozen source 349f88af68ea602581ec078b44094eee1f38efc1 (only report additions since rc source). Twenty complete slot-round2 harness executions, sequential and with no other task-owned build/test: 18 passed, 2 failed. Run 1 failed red-m6=0; run 7 failed signal-TERM-exit=2 instead of 143 (all TERM custody/lease/volume facts remained 1). Other 18 passed every existing C849 assertion. Execution held the real slot for 794 seconds; per-run wall times are in repeat.log and individual stdout/stderr in repeat-1.log through repeat-20.log. No timeout or assertion was widened. The initial independent baseline also failed red-m6=0.

## Historical comparison and deterministic red

Exact pre-repair snapshot 3fb3c113e8b2554ccd5789e00375dbfd69697981 full harness: driver-ready=0, red-m6=1, red-m7=0. Ten targeted pre-repair m6 group runs all detected the mutation by their deadline (15-21 seconds); earlier repaired branch 6d6788def53bd5f08355f79911cf9f643bd5fa90 also detected it in the sampled group run (19 seconds). No historical random m6 miss was observed in those eleven targeted samples. The group witness itself has identical SHA-256 before and after repair: f3ce5a11cf3768b7a023e519112637d3476d0be177ae33b0d6cdb74c2fa46524. The previous Windows driver-ready failure masks the later assertion.

Added a deterministic witness for the precise blind spot: complete cleanup/exit 2 after a signal handler returns must be rejected, while complete cleanup/exit 2 without a returned handler remains accepted. Added the real group witness's no-return assertion. The current predicate ignores the handler-return observation, so the returned-handler case is expected red before the harness repair. Production remains unchanged.

Fourteen residual fake signal-child processes were matched by their literal /tmp/c1192-parent.../signal-child.sh argv and initial PWD=/work/worktrees/task-b9ee02c4, then stopped; cleanup-baseline.log retains only their ownership keys and PIDs. No foreign process or qualification was touched.

## Red-to-green repair

At 9211cc280bc42115e7b9bf5e3ff886225b646904 the exact new method ran 2 cases: 1 passed, 1 failed, 0 skipped. The returned-handler case expected rejected but got accepted; the normal case passed. This is an assertion failure, not build/fixture failure. Build: 0 errors, 1402 warnings, 155 seconds; real slot wait separate. TRX: red/red.trx.

Repair: the trap wrapper appends a run-stamped marker only if c849_fixture_on_signal INT returns. Both runtime acceptance and the group contract reject that marker, and the actual group witness asserts handler-returned=0. All existing foreign-container, CID-only kill, foreign-PID survival, lease deletion, volume disposal, deadline, strict pid-signal exit and m1/m5/m7 assertions remain. The production handler and custody/namespace implementation are untouched. This closes an inherited timing-dependent mutation blind spot; the namespace repair exposed the later assertion but did not introduce the predicate defect.
