# Debug b9ee02c4: C849 red-m6

Start/source: 13565c5a480d8f30a52aa4679a5f1681ef2a8c70, Linux runner mirror.
Assigned scope: reproduce C849 alone 20 times, compare pre-repair fixture, repair the wrong side without weakening custody assertions; run C84x, roster/census selections and land graph Check.

Initial inspection: m6 replaces the INT handler with an early return. red-m6 is 1 only if the group witness is installed and its full release contract fails. A failure before arming cannot count as a detected mutation. No production defect is attributed yet.

Evidence is kept under .antiphon/evidence/b9ee02c4/ (ignored). Native tests and live qualification are outside this task.

## Baseline red

At 13565c5a480d8f30a52aa4679a5f1681ef2a8c70, the gated isolated slot-round2 harness produced driver-ready=1, red-m1=1, red-m5=1, red-m6=0, red-m7=1. The m6 subrun installed the INT trap and returned exit=2 with all release facts=1 in one second: the current contract therefore accepts the no-op handler. Raw baseline: .antiphon/evidence/b9ee02c4/baseline.log (25-second leased execution).

Production scripts/c590-remote.sh SHA-256 is identical at pre-repair 3fb3c113e8b2554ccd5789e00375dbfd69697981, earlier fixture-repair branch origin/feat/card-task-c083bd50, and this rc tip: 180bcbdadb39543ee704276672ff87b7ea56db54f112e20e2c0c5bf0edb51328. The repaired harness bytes are also identical between the earlier repair branch and rc (9e8d3c50685a7ab800b733b1122ba4a61815873a25f91d6345a52f9a6f0769de). No CARD-1614/1618/1619 interaction is present in these inputs.

The first repeat launcher attempted unavailable python3 and ran no tests (exit 1); replaced with a bash/awk driver, retaining the failed invocation evidence. Twenty slot-round2 executions now run sequentially under one real build-slot lease, with no other task-owned build/test in flight. This repeats the exact shell harness and all existing C849 facts, without paying assembly startup for each sample.
