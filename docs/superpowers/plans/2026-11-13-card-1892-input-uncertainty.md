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

## Performance budget

Cold server builds measured 336 s and 528 s (401 s and 613 s including driver audit). The valid red group took 23m58s wall: 969.957 s charged execution and 468.806 s admission-only union; the server build waited 285 s and runner test 450 s. Red rows inspected fresh TRX: CP-1 59/56/3, CP-4 3/0/3, CP-5 3/0/3, CP-6 5/2/3 (executed/passed/failed). The final rows share one server build and bound adjacent integration classes. Runner builds are smaller. Estimates remain provisional, not p95. Each integration host shares its database fixture and each new uncertainty test owns an isolated schema. All fake Input requests/held gates are joined in finally. No selection is expected over 30 minutes; if observed, preserve phase evidence and request caller Debug. Existing checkpoint 15-minute floor remains a documented tooling exception, not a measured per-case timeout.

The first post-fix group was stopped after source inspection found fresh verdict-less Sent attempts needed receipt/Enter-only reconciliation before their ordinary age window. Fresh recovery never authorizes retyping from a clear composer, and holds later input while unresolved. That group also caught the newly named C1722 budget census: its pure-fake-method roster now names the two new non-Git witnesses; no timing or timeout assertion changed. Historical group 20261113-165749-bbce remains stopped, not green.

S6 restores caller cancellation propagation after retaining the session/attempt. The full group at 780ac1aca712906ab984f73936a60edf6fc14b3a had CP-1 157/157, CP-2 95/95, CP-3 106/106, CP-4 5/5, CP-6 5/5; CP-5 was 78/79 because External_cancellation_during_rc_setup_is_not_reported_as_degraded correctly caught a swallowed caller cancellation. Historical group remains red. Rerun only red CP-5 plus affected cancellation/resume classes in CP-7 on S6. The explicit 15-minute row/build override retains the original CP-5 deadline; no timeout is widened. Green unrelated selections are retained at their actual tested SHA, not relabeled as a new-source clean full report. Review must issue its own Final source qualification.

S6 completed green at 5427470e3e4b6dc1d2b22931c0bd604bc09d34ab: CP-5 79/79 and repair-only CP-7 29/29, with zero failed/skipped and a valid exact-source receipt. The repair-only CP-7 selected AgentSessionCancelledTurnTests and AgentSessionInterruptedLaunchResumeTests, both already in CP-2. Its historical manifest/receipt is preserved in the Code evidence report. The current six-row table is the Final Review selection: two builds, no duplicate CP-7, and 29 estimated minutes. All earlier failed/stopped outcomes remain historical outcomes at their actual SHAs.

PC-5 remains pending Mutation: remove caller cancellation propagation after withholding cleanup; AgentSessionLaunchFailureTests/External_cancellation_during_rc_setup_is_not_reported_as_degraded must fail.

### Scope resolution

The task-base range matched the `test-lane-category` glob row in tests/impacted-classes.json; its TestLaneCategoryGuardTests is in CP-3. The selector also prints a wider derived dependency closure (100 source-derived classes, with capped remainder recorded in .antiphon/c1892-impact.json). Per this Code brief/stage scope, ordinary Final executes closed rows, named affected/adjacent classes and matched-glob rows; that wider closure is not represented as passed and is not a whole-lane substitute. Unit remains nightly/rc. No shared invariant requires an unbounded assembly run here: changed behavior is Input lock admission and post-ready uncertain delivery, bounded by the named dispatcher, queue, launch and rules witnesses.

### Cost

Ordinary floor 29 minutes plus authoring. Bounded named classes; no full assembly or Unit lane.
Red rows intentionally fail before fixes, then rerun once green. No repeats after green. Final CP-4/CP-5 expand the red method rows to their full named classes; CP-2 excludes those classes, so cases are not duplicated. The runner budget roster witness remains named and unchanged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.SessionRunner.Tests -> bin-c1892-final2-runner/ | runner-input-retention | `/*/*/(C1677RetentionDispatchTests*)\|(PhoneHomeCommandDispatcherTests*)\|(C1722GitTimeoutBudgetTests*)/*` | V-1,R-1,V-3 | all listed, 0 failed | 3 | 6 |
| CP-2 | all | tests/Antiphon.Tests -> bin-c1892-final2-server/ | adjacent-integration | `/*/*/(GrokRulesInitializationTests*)\|(GrokRulesFailureTests*)\|(PhoneHomeLaunchTransportTests*)\|(SessionMessageQueueInterruptedAttemptTests*)\|(RunnerTerminalSessionInputEncodingTests*)\|(RunnerTerminalSessionGenerationTests*)\|(AgentSessionCancelledTurnTests*)\|(AgentSessionInterruptedLaunchResumeTests*)/*` | V-2,R-2 | all listed, 0 failed | 8 | 12 |
| CP-3 | all | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)/*` | R-3,V-4 | all listed, 0 failed | 10 | 3 |
| CP-4 | all | CP-2 | rules-readiness-uncertainty | `/*/*/GrokRulesReadyOrderingTests/*` | V-2,R-2 | all listed, 0 failed | 5 | 1 |
| CP-5 | all | CP-2 | launch-failure-and-uncertainty | `/*/*/AgentSessionLaunchFailureTests/*` | V-2,R-2 | all listed, 0 failed | 3 | 4 |
| CP-6 | all | CP-2 | queue-transport-recovery | `/*/*/SessionMessageQueuePhoneHomeDropTests/*` | V-2 | all listed, 0 failed | 5 | 3 |
