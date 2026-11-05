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
R-2: MechanicalPcContractTests and TestClassificationPolicy*,
TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*.
R-3: whole Antiphon.Tests Unit lane. Shared regression invariant: lease changes
must preserve ordinary Unit contracts. This is an unbounded class selection,
approximately 3600+ cases, estimated 30m on a saturated host, plus cold build and
admission. No full assembly run is planned. Required by Final profile.

### Checkpoints

totalTimeoutMinutes: 140

| ID | After | Build | Filter | Min | EstimatedMinutes | Repeat | Kind | Notes |
|---|---|---|---|---:|---:|---:|---|---|
| CP-1 | Red | tests/Antiphon.SessionRunner.Tests -> bin-c1713-red/ | /*/*/BuildSlotMaxHoldTests*/* | 5 | 2 | 1 | test | Base production plus red-first tests V-1..4 |
| CP-2 | Fix | tests/Antiphon.SessionRunner.Tests -> bin-c1713-final/ | /*/*/(BuildSlot*Tests*)/* | 40 | 3 | 1 | test | V-1..5 and R-1 full classes |
| CP-3 | Fix | tests/Antiphon.Tests -> bin-c1713-unit/ | /*/*/*/*[Category=Unit] | 3500 | 30 | 1 | test | R-3 whole Unit lane |
| CP-4 | Fix | CP-3 | /*/*/(MechanicalPcContractTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)/* | 20 | 2 | 1 | test | R-2 explicit required classes |

## Pending SourceLanding Mutation candidates

PC-1: remove absolute-bound branch; V1_Default_keeps_live_holder_until_48_hours_then_reaps_once must fail at reap count.
PC-2: lower default to 24; same method must fail at pre-bound protection.
PC-3: use last renewal instead of grant age; V2_Configured_bound_uses_grant_age_and_cannot_be_extended_by_reacquire_or_renew must fail at renewal refusal.
PC-4: remove options validation; V4_Nonpositive_bound_is_rejected_by_production_options (both variants) must fail at expected exception.
PC-5: put bound behind TTL; V3_Bound_applies_before_ttl_and_next_acquire_admits_a_waiter must fail at waiter admission.
All PCs pending binding/execution by SourceLanding Mutation, method scoped.

## OS and startup

Bound/settings are portable, no changed OS branch or liveness factory. V-5 uses
the existing production probe with a real Linux process and actual JSON input.
Windows native replay is useful but no new Windows behavior is introduced.
Activation requires restarting the runner/broker that owns leases after review
and rc pick, respecting held leases; server restart is unnecessary. This task
restarts none and changes no live settings.
