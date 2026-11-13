# CARD-1892 Input uncertainty and runner lock isolation

Code/landing owner: 13c4448d. Base: e4c45f4b4bb58870bb8684087a6d218e1b988cbe.
Debug artifact: 37087a2ea, docs/investigations/2026-11-13-server2-input-timeout-debug.md.
Retention timing in the incident remains hypothetical.

## Implementation

S1 commits red retention witnesses; S2 isolates Input/ConditionalInput from the workspace mutation
gate. The runtime already serializes both under its per-session launch gate. Retention remains
serialized with Mirror/Launch and retains live/recent-use and holder checks. Log thresholded lock
wait/hold timing with fixed lock/operation names and no input, paths or secrets.
S3 commits red ambiguous-delivery launch witnesses; S4 preserves uncertain input attempts and live
sessions for transcript reconciliation. Confirmed spawn/readiness/provider errors retain cleanup.
S5 refreshes the derived index separately if changed, then closes named integration and guard rows.
No CARD-0079 change, timeout widening or assertion weakening. No migrations.

## Verification design

V-1: retention phase A held past a compressed reply deadline cannot delay raw or conditional Input.
R-1: phase B remains outside the gate; recent/live workspace use prevents removal.
V-2: delayed Input reply past its transport deadline leaves launch live, delivery unknown;
cancellation and connection loss preserve the attempt. Late transcript confirms without duplication.
R-2: actual readiness/spawn failure still kills and disposes.
V-3: structured mutation wait/hold timing above threshold; below threshold stays quiet.
R-3: policy guards, mechanical contracts, layering, category census, derived index, impacted classes.
V-4: fresh TRX roster/count inspection, exact-source receipt validation, evidence-diff and merge-tree.
Manual: inspect LF/bracketed-paste/separate-Enter and generation guards. Runner activation requires
caller restart; no running stack is touched. Native Windows proof remains owed by orchestrator.

PC-1 pending Mutation: restore Input under MutateAsync; Phase_a_does_not_block_input must fail
(raw and conditional variants). PC-2 pending Mutation: restore kill-on-ambiguous-input; launch
uncertainty witness must fail. PC-3 pending Mutation: revert preserved uncertain queue attempts;
queue state witness must fail. PC-4 pending Mutation: remove thresholded lock timing; timing witness
must fail. Mutation owns deliberate red/restore/green, missing-control discovery and other variants.

### Cost

Ordinary floor 24 minutes plus authoring. Bounded named classes; no full assembly or Unit lane.
Red rows intentionally fail before fixes, then rerun once green. No repeats after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1892-runner/ | runner-input-retention | `/*/*/(C1677RetentionDispatchTests*)\|(PhoneHomeCommandDispatcherTests*)/*` | V-1,R-1,V-3 | all listed, 0 failed | 2 | 6 |
| CP-2 | S3-S4 | tests/Antiphon.Tests -> bin-c1892-server/ | launch-uncertainty | `/*/*/(AgentSessionLaunchFailureTests*)\|(GrokRulesReadyOrderingTests*)\|(GrokRulesInitializationTests*)\|(PhoneHomeLaunchTransportTests*)\|(SessionMessageQueueInterruptedAttemptTests*)\|(RunnerTerminalSessionInputEncodingTests*)/*` | V-2,R-2 | all listed, 0 failed | 6 | 12 |
| CP-3 | S5 | tests/Antiphon.Tests -> bin-c1892-guards/ | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)/*` | R-3,V-4 | all listed, 0 failed | 10 | 6 |
