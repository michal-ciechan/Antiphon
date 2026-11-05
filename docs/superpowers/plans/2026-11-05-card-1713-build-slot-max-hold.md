# CARD-1713 absolute build-slot hold bound

Code owner/landing task: `013b9ff7-b813-4986-8c7f-2a5b0fbdef95`.
Base: `388ff482046a62f813a251208bfeabe7c21808ea`. Final verification.

## Decision and evidence

Add positive integer `SessionRunner:BuildSlots:LeaseMaxHoldHours`, default 48.
Absent uses 48; zero/negative refuse options validation. Age starts at grant,
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
V-4: 0 and -1 fail via production options binding.
V-5: real PowerShell wrapper sends its own pid/start over HTTP to real
SystemProcessLivenessProbe; fake clock crosses TTL, stays protected within 48h,
then reaps at bound while process is still alive. Existing wrapper waits remain
bounded; no wall-clock sleep advances lease age.
R-1: all BuildSlot broker, settings, endpoint, metrics and end-to-end classes.
V-6: a healthy renew-mode holder remains leased until one tick before its 1h
bound, then renewal is refused at that bound despite the later 90m TTL.
R-2: MechanicalPcContractTests and TestClassificationPolicy*,
TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*.
R-3: whole Antiphon.Tests Unit lane. Shared regression invariant: lease changes
must preserve ordinary Unit contracts. This is an unbounded class selection,
approximately 3600+ cases, estimated 30m on a saturated host, plus cold build and
admission. No full assembly run is planned. Required by Final profile.

### Checkpoints

totalTimeoutMinutes: 140

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | Red | tests/Antiphon.SessionRunner.Tests -> bin-c1713-red/ | red | `/*/*/BuildSlotMaxHoldTests*/*` | V-1..4 | Historical base witness: 5 failures at cc166db9d3ca78af6606abf2939b8010122aa843; later V-6 is ordinary green scope | 5 | 2 |
| CP-2 | Fix | tests/Antiphon.SessionRunner.Tests -> bin-c1713-final/ | broker | `/*/*/BuildSlot*Tests*/*` | V-1..6, R-1 | Exactly 40 executed/passed; zero failed/skipped | 40 | 3 |
| CP-3 | Fix | tests/Antiphon.Tests -> bin-c1713-unit/ | unit | `/*/*/*/*[Category=Unit]` | R-3 | Whole Unit lane, zero failed | 3500 | 30 |
| CP-4 | Fix | CP-3 | policy | `/*/*/(MechanicalPcContractTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/*` | R-2 | Full named classes, zero failed/skipped | 20 | 2 |

## Pending SourceLanding Mutation candidates

PC-1: remove absolute-bound branch; V1_Default_keeps_live_holder_until_48_hours_then_reaps_once must fail at reap count.
PC-2: lower default to 24; same method must fail at pre-bound protection.
PC-3: use last renewal instead of grant age; V2_Configured_bound_uses_grant_age_and_cannot_be_extended_by_reacquire_or_renew must fail at renewal refusal.
PC-4: remove options validation; V4_Nonpositive_bound_is_rejected_by_production_options (both variants) must fail at expected exception.
PC-5: put bound behind TTL; V3_Bound_applies_before_ttl_and_next_acquire_admits_a_waiter must fail at waiter admission.
PC-6: restrict the bound to pid mode; V6_Healthy_renew_mode_holder_is_reaped_at_bound_before_ttl must fail at final renewal refusal.
All PCs pending binding/execution by SourceLanding Mutation, method scoped.
Unbound handoff: [candidate inventory](../mutations/2026-11-05-card-1713-build-slot-max-hold-candidates.md).

## OS and startup

Bound/settings are portable, no changed OS branch or liveness factory. V-5 uses
the existing production probe with a real Linux process and actual JSON input.
Windows native replay is useful but no new Windows behavior is introduced.
Activation requires restarting the runner/broker that owns leases after review
and rc pick, respecting held leases; server restart is unnecessary. This task
restarts none and changes no live settings.
