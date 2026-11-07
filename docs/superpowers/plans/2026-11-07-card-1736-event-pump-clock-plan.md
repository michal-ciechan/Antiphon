# CARD-1736: event pump test clock scheduling

Owner/landing task: b61de2b0. Base: fbb2be214a849549cfa72f2e9a17646cc848cfeb.
Scope: test fixture only, Final verification. No product, policy, timeout or assertion changes.

The C1259 fixture advances fake time 100 times with 20 ms real yields, then stops advancing
while awaiting submission. A body write delayed beyond that loop registers its composer
settle/Enter timers after the clock has stopped. This is a fixture scheduling dependency.
Use the existing ControlledTimeProvider registration barrier to advance only actual
pre-submit timers; stop at the 20 ms Enter delay so confirmation time remains frozen.

S1 adds live/subscription-ready delayed-body cases: hold the body until the old advance
loop finishes, then release it. These must fail awaiting submission on the unchanged driver.
S2 replaces that driver with timer-registration synchronization. Delayed-body cases consume
the same five virtual seconds while held, release the body, then use the same driver as normal
cases. Keep complete prompt, ordered other-session persistence, held read, verdict, baseline,
exact input shape and single-attempt assertions.

## Performance budget

Provisional bounds reuse existing guards: streaming/body/submission/read 5 seconds,
persistence 3 seconds, confirmation fake timeout 30 seconds and retry 7 seconds unchanged.
The red injected stall is bounded by the existing five-second submission guard. There is no
new sleep or wider timeout. Measure fresh TRX durations and checkpoint phases. Shared
TestDbFixture owns lazy Postgres setup; isolated schema per integration test. One native
incremental compile precedes checkpoint cold builds, as requested by the brief; compile
the checkpoint tool separately under a build slot. These are explicitly listed bootstrap
builds, not acceptance runs. TEMP=/tmp and UseAppHost=false throughout.

Final whole Unit is explicitly required by the profile. Its invariant is repository Unit
contracts, including all classification, layering, mechanical and platform guidance guards.
The complete class set cannot be bounded for that required lane. Estimated 15 minutes is
provisional; the existing CARD-1573/CARD-1514 Unit timing work records the known >30-minute
risk. The unchanged 45-minute derived row cap is an execution guard, not a per-test allowance.
No full assembly selection. Integration class estimate 3 minutes, registry merge 2 minutes,
cold compile 3 minutes each, bootstrap compile 5 minutes. Slot waits are separate. No repeats
after green. Do not change the execution policy, nightly policyHash or live pinned digest.

## Verification design

V-1: delayed live and subscription-ready submission must complete after release; full C1259
method at S1 records the two intended reds and three existing cases. V-2: same scenarios at
S2 retain all persistence and single-submit assertions with confirmation held.
R-1: complete SessionRunnerEventPumpTests class, including S0 and stale-exit coverage.
R-2: whole Unit lane; inspect fresh identities for TestClassificationPolicyTests,
TestClassificationGuardTests, SlowTestTripwireTests, NativeLinuxPolicyWitnessTests,
C1729LayeringGuardTests, MechanicalPcContractTests and TaskPlatformGuidanceTests.
R-3: complete SlowTestRegistryMergeTests (Integration).
R-4: full-range evidence diff, merge-tree against fetched origin/master, OS witness inventory.
No changed OS branches or startup paths; restart: none. Manual acceptance is the read-barrier
and exact prompt/other-session/queue assertions in V-2, plus fresh TRX roster inspection.

PC-1 live-delayed-body and PC-2 subscription-ready-delayed-body: pending method-scoped
SourceLanding Mutation. Candidate defect: restore the fixed 100-tick driver; do not change
the gated witnesses or any assertions. Mutation owns deliberate red/restore/green execution.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1736-red/ | delayed-body-red | /*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/C1259_confirming_prompt_and_other_session_progress_before_confirmation* | V-1 | five cases, two intended submission timeouts | 5 | 3 | true |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1736-green/ | pump-full | /*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/* | V-2, R-1 | all 10 cases, 0 failed/skipped | 10 | 3 | true |
| CP-3 | S2 | CP-2 | unit-full | /*/*/*/*[Category=Unit] | R-2 | >= 1 executed, 0 failed | 1 | 15 | true |
| CP-4 | S2 | CP-2 | registry-full | /*/Antiphon.Tests.Infrastructure/SlowTestRegistryMergeTests/* | R-3 | all methods, 0 failed/skipped | 1 | 2 | true |
