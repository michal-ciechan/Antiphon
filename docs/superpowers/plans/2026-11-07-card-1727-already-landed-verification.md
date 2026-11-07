# CARD-1727: verify the already-landed mechanical PC wording repair

Original Code/landing owner: 51804350. Assigned base: fbb2be214a849549cfa72f2e9a17646cc848cfeb.

## Disposition

The stale side was the TestDesign template, not the mechanical contract assertion.
Commit 8747ca113dcef755481ff8e48fc5b0bd03eaded4 already restored
`Unbound payloads: binding required after Code.` before this task's base.
The original assertion is unchanged. Do not introduce a duplicate repair or relax it.
The exact C479_R06 method passed at the assigned base (1 executed, 1 passed, 0 failed/skipped).
The card records historical RED at 388ff482046a62f813a251208bfeabe7c21808ea:
CP-BASE 20261105-190955-fdfc, 1 executed / 1 failed, clean source and verified build.
That historical run is inherited evidence, not newly executed by this task.
No RED at the assigned base is possible without a deliberate mutant; Mutation owns that work.

## Performance budget

One mandated native incremental compile measured 202.41 seconds plus admission 0 seconds;
the exact base-method run measured 12.026 seconds plus admission 0 seconds.
The checkpoint tool was built as a dependency of that compile; no bootstrap build is needed.
Final rows share one isolated build, run serially, and each runs once. Estimates below are
provisional, not p95. Unit performance limitations already belong to CARD-1573/CARD-1720;
no new measured p95 is claimed. Initial row timeout is 45m, unchanged through this task.
The tool derives the total execution allowance from the table; admission has its separate
45m bound. At most three rows in one detached run, observed through foreground wait calls.
Retain per-case durations in TRX, build/slot/run timings in report.json and executor.log;
unavailable fixture/external-call phase attribution remains unknown. No setup is added.
No full-assembly run: Unit selection is partitioned by class initial while retaining the
method-level Category=Unit predicate, so method-only Unit tags are included too.
Any row observed over 30m requires caller-commissioned Debug (delegates cannot subdelegate).

## Verification design

### Inspection

Read MechanicalPcContractTests, stage-test-design.md, its restoring commit, the mechanical
owner doc contract and TaskPlatformGuidanceTests. Read classification policy/guard, native
Linux witness, registry-merge, tripwire and layering classes and testing/performance owners.
No product, test, fixture, policy JSON, pinned digest, helper, OS branch or rc startup changes.
Restart: none. Runtime placement read from /api/runner-defaults and /api/session-runners;
all execution remains local on the assigned Linux mirror, with no runner/platform pin.

### Proves it works now

- V-1: full MechanicalPcContractTests and TaskPlatformGuidanceTests preserve composed instructions and size/placement contracts.
- V-2: full TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests* preserve test-policy guards, including the named Integration class.
- V-3: full C1729LayeringGuardTests preserve the repository layering verdict.
- V-4: the whole Antiphon.Tests Unit lane, including method-level Unit tags, partitions into CP-2 (A-L) and CP-3 (M-Z). Inspect fresh TRX identities and nonzero counts. Native-only skips remain explicit, never passed native evidence.

### Guards the regression

- R-1: exact MechanicalPcContractTests.C479_R06_LowNeverDiscoversAndTierRowsStayLow requires binding after Code and Low-only discovery/tier rules. Base is already GREEN; CP-1 verifies the full containing class at the committed task tip.

### Guard inventory

- G-1: mandatory binding after Code instruction, protected by existing R-1 | PC-1.

### Positive controls

- PC-1 (pending for method-scoped SourceLanding Mutation): replace only `Unbound payloads: binding required after Code.` with the former `Unbound payloads need binding after Code.` in server/Bundles/stage-test-design.md; build must succeed; exact `/*/*/MechanicalPcContractTests/C479_R06_LowNeverDiscoversAndTierRowsStayLow` must fail its composed TestDesign ShouldContain assertion. Restore exact bytes, rebuild and rerun that same method green. No Code mutant is executed.

Guards=1, mapped=1, missing=0, duplicate PC maps=0. Mutation also owns missing-control discovery.

### Out of scope

No new implementation is needed. Do not change tests/test-execution-policy.json, policyHash,
PinnedInputDigest, production behavior, assertion strength or timeouts. Unrelated failures are
reported at their tested source; zero tests, startup/build errors and skips are not successes.
All ordinary IDs below are required for Final, with no deferred-to-final IDs.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1727-final/ | contract-policy-layering | /*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(C1729LayeringGuardTests*)/* | V-1,V-2,V-3,R-1 | all listed, 0 failed | 1 | 10 |
| CP-2 | S1 | CP-1 | unit-a-l | /*/*/(A*)\|(B*)\|(C*)\|(D*)\|(E*)\|(F*)\|(G*)\|(H*)\|(I*)\|(J*)\|(K*)\|(L*)/*[Category=Unit] | V-4 | >= 1 executed, 0 failed | 1 | 15 |
| CP-3 | S1 | CP-1 | unit-m-z | /*/*/(M*)\|(N*)\|(O*)\|(P*)\|(Q*)\|(R*)\|(S*)\|(T*)\|(U*)\|(V*)\|(W*)\|(X*)\|(Y*)\|(Z*)/*[Category=Unit] | V-4 | >= 1 executed, 0 failed | 1 | 15 |

### Cost

Ordinary checkpoint floor 40 estimated minutes, plus the measured native compile/base diagnostic
and authoring. One shared isolated build saves two additional cold builds. PC floor estimated
4 minutes plus builds/admission, owned by Mutation; no deliberate controls discharged here.
The native build and exact-method run are the only unlisted build/test drivers: required by
the brief to establish whether the defect still exists at the exact base before authoring.
