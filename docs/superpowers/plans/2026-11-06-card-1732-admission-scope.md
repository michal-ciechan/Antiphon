# CARD-1732 admission scope and release obligations

Code/landing owner: a98c2d1e-7763-48ab-a2a6-b1bc1f30e27e. Base: 1a22e0d2ca7b32e7f6ac346a658e1c7824252a46.
Only A-1 and A-4. A-2/A-3/A-5/A-6/A-7 remain open on CARD-1732.

S1 extracts the existing manual-scope check unchanged and adds an independent regression
with each real manual policy row cloned and its methods property removed. It must return
exactly the offending class without an exception. Commit/run red before S2.
S2 filters absent/null method values before indexing and documents both manual workflows.
No policy, hash input, runtime behavior, timeout or coverage calculation changes.

## Verification design

V-1: the eight admission assertions use real nightly policy/disposition/coverage functions.
R-1: the added class-scope assertion is red before the null fix and green afterward,
covering both LiveLane and load sample variants. Existing exact-scope assertion must also
reject a broadened real row (SourceLanding PC-1 below).
V-2: full affected RcCoverageAdmissionTests class; policy/classification/registry/tripwire,
mechanical instruction contract, platform guidance and all four native policy witnesses.
V-3: whole Unit lane preserves independent classification and policy consumer invariants.
It omits delivery, landing, leases and persistence; no full assembly is selected.
Unit classes are unbounded within the class-letter partitions and Category=Unit, including
method-level tags; partitions A-M and N-Z cover the complete current Unit class roster.
M-1: inspect release doc obligations against policy, unchanged protected hash inputs,
full-range evidence guard, fetched-master merge-tree, OS/startup audit. No OS-selected
production behavior changes; rc startup unaffected; restart none.

## Performance budget

Native incremental Antiphon.Tests compile and checkpoint-tool bootstrap are explicitly
listed preparatory builds, each through build-slot with UseAppHost=false and TEMP=/tmp.
Checkpoint isolated builds shared within each group, at most three selected rows per run.
Admission bounded separately by broker defaults; derive execution timeouts unchanged.
Estimates provisional from Review 8eb266f0: admission 13s, policy sweep 2m24s,
native builds 15-28s, main build 150s; no measured p95. CARD-1404 owns measurements.
Whole Unit has historical >30m cost under CARD-1720; use two letter partitions to retain
method-level tags and budget <=15m each. Report over-30m run to caller for Debug; do not
repeat green rows. Fixtures remain existing shared assembly fixtures. Retain per-test TRX,
row/build/slot timing in ignored .antiphon/checkpoints; unavailable phases unknown.

### Positive controls

All pending method-scoped SourceLanding Mutation; no Code mutation cycles.
PC-1: broaden each real manual row to class scope (2 variants), reseal policy hash and input
pin, require RcCoverageAdmissionTests.Windows_and_linux_obligations_remain_exact to fail
CoverageAdmission separate-method-workflows by assertion, without null-index exception.
Original plan's PC-1..PC-6 variants remain pending under their original Code owner 07ab1e49.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | n/a | admission | `pwsh -NoProfile -File scripts/test-rc-coverage-admission.ps1` | V-1,R-1 | S1 7 pass/1 fail, S2 8 pass | n/a | 1 | true | TEMP=/tmp |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-a98-policy/ | policy | `/*/*/(RcCoverageAdmissionTests*)\|(TaskPlatformGuidanceTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)/*` | V-2 | all pass | 1 | 5 | true | TEMP=/tmp |
| CP-3 | S2 | CP-2 | unit-am | `/*/*/(A*)\|(B*)\|(C*)\|(D*)\|(E*)\|(F*)\|(G*)\|(H*)\|(I*)\|(J*)\|(K*)\|(L*)\|(M*)/*[Category=Unit]` | V-3 | all pass | 1 | 15 | true | TEMP=/tmp |
| CP-4 | S2 | CP-2 | unit-nz | `/*/*/(N*)\|(O*)\|(P*)\|(Q*)\|(R*)\|(S*)\|(T*)\|(U*)\|(V*)\|(W*)\|(X*)\|(Y*)\|(Z*)/*[Category=Unit]` | V-3 | all pass | 1 | 15 | true | TEMP=/tmp |
| CP-5 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-a98-runner/ | runner-native | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuard*)/*` | V-2 | all pass | 4 | 1 | true | TEMP=/tmp |
| CP-6 | S2 | tests/Antiphon.PtyHost.Tests -> bin-a98-host/ | host-native | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuard*)/*` | V-2 | all pass | 4 | 1 | true | TEMP=/tmp |
| CP-7 | S2 | tests/Antiphon.Agents.Pty.Tests -> bin-a98-pty/ | pty-native | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuard*)/*` | V-2 | all pass | 4 | 1 | true | TEMP=/tmp |
