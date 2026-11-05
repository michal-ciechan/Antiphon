# CARD-1713 absolute build-slot hold bound

Code owner/landing task: `013b9ff7-b813-4986-8c7f-2a5b0fbdef95`.
Base: `388ff482046a62f813a251208bfeabe7c21808ea`. Final verification.

## Decision and evidence

Add integer `SessionRunner:BuildSlots:LeaseMaxHoldHours`, default 48.
Absent and zero use 48; negative refuses options validation. Age starts at grant,
never at renewal, re-acquisition or TTL extension. At age >= bound the next
sweep/acquire/renew removes the lease and logs a warning once, including lease,
holder, age and `absolute max hold <hours>h exceeded`. No process is killed.
Below the bound preserve existing pid, renewal, FIFO, memory and TTL rules.

Read-only census at 2026-11-05 18:45 UTC: 68 surviving server2 checkpoint
executor logs, 207 completed grant/release pairs. Longest measured lease:
2702.101 seconds (45m 2.101s), lease
`29319c0c-5c47-477a-988b-b2281d69c9cc`,
`/work/worktrees/task-c3ba1792/.antiphon/checkpoints/20261105-044841-7f06/executor.log`
lines 96-97, granted 05:23:07.4483984Z, released 06:08:09.5492323Z.
Runner logs contain no broker lifecycle lines: shared broker is separate.
This is a surviving-history measurement, not an all-time maximum. Default 48h
is >63x that longest observation and above documented 17-30h qualifications.

## Ordinary verification

V-1: default live matching pid stays occupied until one tick before 48h, then
reaps exactly at 48h once, records one warning/reap, cannot be released again.
V-2: configured 72h protects a 60h holder; reacquire/renew cannot restart age.
V-3: configured 1h bound wins even before 90m TTL; waiting acquire is admitted.
V-4: -1 fails via production options binding. The historical red run also
contained a zero-rejection test; that proposed decision was corrected to match
the card. It is not evidence for the final zero-fallback contract.
V-7: zero binds successfully and protects a live holder until exactly 48h, then
reaps it. CP-5 is the red-first zero acceptance witness before normalization.
V-5: real PowerShell wrapper sends its own pid/start over HTTP to real
SystemProcessLivenessProbe; fake clock crosses TTL, stays protected within 48h,
then reaps at bound while process is still alive. Existing wrapper waits remain
bounded; no wall-clock sleep advances lease age.
R-1: all BuildSlot broker, settings, endpoint, metrics and end-to-end classes.
V-6: a healthy renew-mode holder remains leased until one tick before its 1h
bound, then renewal is refused at that bound despite the later 90m TTL.
R-2: MechanicalPcContractTests and TestClassificationPolicy*,
TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*. Also run
the linked TestClassificationGuardTests in the affected runner assembly.
R-3: whole Antiphon.Tests Unit lane. Shared regression invariant: lease changes
must preserve ordinary Unit contracts. This is an unbounded class selection,
approximately 3600+ cases, estimated 30m on a saturated host, plus cold build and
admission. No full assembly run is planned. Required by Final profile.

## Performance budget

Costs are provisional, tracked by CARD-1713; Unit p95 is unknown (CARD-1404 /
CARD-1720), not inferred from a killed run. Collect one complete Final selection
here; do not repeat a green proof for timing. First completed runner selection
at 38e7740b44378375474b6268a5c07291016bdcfa took 134.760s wall,
93.449s build and 19.815s row execution (40 executed, one V-5 input error).
That single failed selection is not a measured p95. Budget runner execution
at 3m, policy at 2m and Unit provisionally at 30m, excluding broker admission.
Retain the importer's finite 15m build/row floor and 90m derived Unit row budget
as explicit provisional exceptions pending this measurement; no timeout is
widened to repair red. Admission is independently bounded at 45m; total charged
execution is 140m. If actual selection time exceeds 30m, caller must commission
Debug with the retained run, SHA, load and timings (delegate cannot sub-delegate).

Fake-clock broker cases own only lightweight service providers, with no web
host, database or wall-clock lease-age waits. V-5 needs one private Kestrel host
for the actual HTTP identity and broker timer lifecycle; its 48h fake clock
cannot be shared with the existing real-clock wrapper cases. All wrapper
processes are joined/killed by the existing fixture; its existing 90s readiness
and exit watchdogs are unchanged. No AppHost or database is started by new tests.
TRX retains exact membership and per-test durations; checkpoint reports retain
admission, build, host startup, tests and teardown phases. Additional unrecorded
HTTP/process/fixture subphases remain unknown, never reported as zero. Evidence
lives under `.antiphon/checkpoints/<run>/`; the final task report retains all
attempts and separates failed/stopped runs from passing receipts.

### Checkpoints

totalTimeoutMinutes: 140

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | Red | tests/Antiphon.SessionRunner.Tests -> bin-c1713-red/ | red | `/*/*/BuildSlotMaxHoldTests*/*` | V-1..4 | Historical base witness: 5 failures at cc166db9d3ca78af6606abf2939b8010122aa843; later V-6 is ordinary green scope | 5 | 2 |
| CP-2 | Fix | tests/Antiphon.SessionRunner.Tests -> bin-c1713-final/ | broker | `/*/*/BuildSlot*Tests*/*` | V-1..7, R-1 | Exactly 40 executed/passed; zero failed/skipped | 40 | 3 |
| CP-3 | Fix | tests/Antiphon.Tests -> bin-c1713-unit/ | unit-without-slow | `/*/*/*/*[(Category=Unit)&(Category!=Slow)]` | R-3 non-Slow partition | All selected Unit cases, zero failed; report native skips | 3500 | 15 |
| CP-4 | Fix | CP-3 | policy | `/*/*/(MechanicalPcContractTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/*` | R-2 | Full named classes, zero failed/skipped | 20 | 2 |
| CP-5 | ZeroRed | tests/Antiphon.SessionRunner.Tests -> bin-c1713-zero-red/ | zero-red | `/*/*/BuildSlotMaxHoldTests*/V7_Zero_uses_the_default_bound` | V-7 | Expected assertion failure before zero normalization | 1 | 1 |
| CP-6 | Fix | CP-2 | runner-policy | `/*/*/TestClassificationGuard*/*` | R-2 runner | Exactly 1 executed/passed; zero failed/skipped | 1 | 1 |
| CP-7 | Fix | CP-3 | unit-slow | `/*/*/*/*[(Category=Unit)&(Category=Slow)]` | R-3 Slow partition | All selected Unit+Slow cases, zero failed; report native skips | 30 | 15 |

## Finish verification packing (Code 1c9ceb47)

The original CP-3 was the whole Unit lane, not a lease-cap class. Its source
census includes hundreds of checkpoint/script methods, and its retained stack
reached CheckpointFixtureProcess.RunAsync. The three Unit+Slow checkpoint
classes perform real Git and dotnet fixture builds: CheckpointOutputConfinementTests,
CheckpointShapeConflictTests and CheckpointTrackedBaselineTests (36 methods
before native skips). This is a concrete execution cost in addition to outer
build/admission; the stopped 35-minute host has no completed timing/roster receipt,
so no single slowest-test or p95 claim is made.

CP-3 now selects Unit without Slow; CP-7 selects Unit with Slow. Their disjoint
union is exactly the former whole Unit predicate, including all lease-adjacent
coverage. The combined executed floor remains at least 3500; the CP-7 floor is
30 to allow its documented native-only skips. Both predicates use the supported
[parenthesized property conjunction](https://tunit.dev/docs/execution/test-filters/).
No PC witness or ordinary class is dropped. Final R-3 requires both receipts.

Run only CP-3, CP-4 and CP-7, serially, with one shared isolated build. Preserve
existing green CP-2/CP-6 receipts at 253a86bc397300936eee02c8b2ba1586d3cffd46:
this finish changes the verification plan only, not implementation/tests.
Historical red CP-1/CP-5 are not rerun as ordinary greens. CP-4 retains every
full named guard class, including the non-Unit SlowTestRegistryMergeTests.

The related admission/cold-build Debug is
origin/feat/card-task-5ae98d31:docs/investigations/2026-11-05-debug-s5b-1-performance.md.
It supports the three-row limit and native compile check; it does not prove that
this card's 35-minute test execution was only build admission. Native compile
on this worktree passed in 543.24s, admission 0s, at the unchanged lease-cap tip.
The 15+15 minute partition estimates are provisional CARD-1713/CARD-1404
measurements to collect once, not p95. Import the table, pin each Unit partition
to 25 minutes and the guard row to 6 minutes (all reductions from prior caps),
build deadline 25 minutes, slot acquisition 12 minutes, UseAppHost=false,
TEMP=/tmp, and hard wall 40 minutes for this budgeted finish. No per-test timeout
or assertion changes. Existing 140-minute charged allowance is retained; the
independent 40-minute wall bound may leave rows incomplete, never green.
Record exact TRX memberships/counts and row/build/admission phases. If the run
exceeds 30 minutes retain evidence for caller-commissioned Debug; no automatic
rerun of green rows. Known inherited reds remain MechanicalPcContractTests.
C479_R06_LowNeverDiscoversAndTierRowsStayLow and NativeLinuxPolicyWitnessTests.
Policy_rows_require_pinned_attribute / Markers_do_not_expand_policy_exclusions;
the separate Code 340f1d0a owns their repair.

## Pending SourceLanding Mutation candidates

PC-1: remove absolute-bound branch; V1_Default_keeps_live_holder_until_48_hours_then_reaps_once must fail at reap count.
PC-2: lower default to 24; same method must fail at pre-bound protection.
PC-3: use last renewal instead of grant age; V2_Configured_bound_uses_grant_age_and_cannot_be_extended_by_reacquire_or_renew must fail at renewal refusal.
PC-4: remove negative-value validation; V4_Nonpositive_bound_is_rejected_by_production_options (-1) must fail at expected exception. The old zero-rejection variant is retired in favor of PC-7.
PC-5: put bound behind TTL; V3_Bound_applies_before_ttl_and_next_acquire_admits_a_waiter must fail at waiter admission.
PC-6: restrict the bound to pid mode; V6_Healthy_renew_mode_holder_is_reaped_at_bound_before_ttl must fail at final renewal refusal.
PC-7: remove zero normalization; V7_Zero_uses_the_default_bound must fail at its no-exception or pre-bound protection assertion.
All PCs pending binding/execution by SourceLanding Mutation, method scoped.
Unbound handoff: [candidate inventory](../mutations/2026-11-05-card-1713-build-slot-max-hold-candidates.md).

## OS and startup

Bound/settings are portable, no changed OS branch or liveness factory. V-5 uses
the existing production probe with a real Linux process and actual JSON input.
Windows native replay is useful but no new Windows behavior is introduced.
Activation requires restarting the runner/broker that owns leases after review
and rc pick, respecting held leases; server restart is unnecessary. This task
restarts none and changes no live settings.
