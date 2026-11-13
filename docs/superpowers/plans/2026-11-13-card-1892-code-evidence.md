# CARD-1892 Code evidence

Implemented Input lock isolation and non-destructive ambiguous prompt delivery. Ordinary V/R selections are green across the committed groups below. Initial Grok Final Review, native Windows proof and all SourceLanding Mutation controls remain pending. No activation was performed.

Original Code/landing owner: 13c4448d-6092-41e2-8ea0-4657c3b70bee. Branch: feat/card-task-13c4448d. Worktree: /work/worktrees/task-13c4448d. Assigned desktop mirror is not reachable from this host. Task base: e4c45f4b4bb58870bb8684087a6d218e1b988cbe.

Plan: /work/worktrees/task-13c4448d/docs/superpowers/plans/2026-11-13-card-1892-input-uncertainty.md. Mutation candidate: /work/worktrees/task-13c4448d/docs/superpowers/mutations/2026-11-13-card-1892-input-uncertainty-candidate.md.

## Behavior

Input and ConditionalInput bypass the workspace mutation semaphore and retain SessionRunnerRuntime's existing per-session gate. Retention remains serialized with Launch/Mirror and keeps its live/recent-use and holder checks. Workspace lock diagnostics report fixed lock/operation names, request ID, wait and hold milliseconds at the 250 ms threshold, after release, without input bodies, host paths or credentials.

A post-ready uncertain Input preserves process/runtime ownership and charged attempt metadata (generation, baseline and clock), with no delivery verdict. Fresh unknown attempts reconcile transcript evidence or the still-visible body through Enter-only recovery; a clear composer cannot authorize early retyping, and later input is held. Rules deadline expiry alone cannot convert an unresolved attempt into failed-start cleanup. Genuine spawn/readiness/provider errors keep their cleanup. Caller cancellation propagates after the process is retained. Reply/production timeout values, CARD-0079 and the other stop paths were not changed.

## Ordinary outcomes

| Row | Actual tested SHA | Executed/passed | Failed/skipped | Coverage |
|---|---|---|---|---|
| CP-1 | 780ac1aca712906ab984f73936a60edf6fc14b3a | 157/157 | 0/0 | runner Input/retention, dispatcher, C1722 budget census |
| CP-2 | 780ac1aca712906ab984f73936a60edf6fc14b3a | 95/95 | 0/0 | 8 adjacent integration classes |
| CP-3 | 780ac1aca712906ab984f73936a60edf6fc14b3a | 106/106 | 0/0 | 10-class policy/mechanical/platform/layering/category/index guard union |
| CP-4 | 780ac1aca712906ab984f73936a60edf6fc14b3a | 5/5 | 0/0 | Grok rules readiness plus timeout/cancel/disconnect and late receipt |
| CP-5 | 5427470e3e4b6dc1d2b22931c0bd604bc09d34ab | 79/79 | 0/0 | full launch-failure class, including genuine failure cleanup and caller cancellation |
| CP-6 | 780ac1aca712906ab984f73936a60edf6fc14b3a | 5/5 | 0/0 | phone-home drop, late confirm and Enter-only recovery |
| CP-7 | 5427470e3e4b6dc1d2b22931c0bd604bc09d34ab | 29/29 | 0/0 | affected canceled-turn and interrupted-launch/resume repair smoke; subset of CP-2 |

The six main rows cover 447 executions with current passing outcomes; CP-7 is a 29-case affected-adjacent rerun already represented in CP-2. These are per-committed-group outcomes, not a same-tip full green report. The full report at 780ac1aca712906ab984f73936a60edf6fc14b3a remains red for its one historical failure. Its validator rejects the report with report_failed, including when asked to select green rows; no receipt was rewritten to evade that gate. The S6 repair report at 5427470e3e4b6dc1d2b22931c0bd604bc09d34ab validates GREEN for CP-5/CP-7. Initial Final Review must produce its own full candidate source qualification. The production delta from 780ac1aca712906ab984f73936a60edf6fc14b3a to 5427470e3e4b6dc1d2b22931c0bd604bc09d34ab is four caller-token propagation checks in AgentSessionService; its affected classes were rerun.

Fresh TRX files were inspected through TestDefinitions/TestMethod class identity, with nonzero counts for every intended class/method and all selected results Passed. Roster: CP-1 C1677RetentionDispatchTests 14, PhoneHomeCommandDispatcherTests 45, C1722GitTimeoutBudgetTests 98. CP-2 GrokRulesInitializationTests 24, GrokRulesFailureTests 7, PhoneHomeLaunchTransportTests 7, SessionMessageQueueInterruptedAttemptTests 15, RunnerTerminalSessionInputEncodingTests 5, RunnerTerminalSessionGenerationTests 8, AgentSessionCancelledTurnTests 20, AgentSessionInterruptedLaunchResumeTests 9. CP-3 TestClassificationPolicyTests 42, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 9, TestLaneCategoryGuardTests 1, DerivedImpactIndexTests 27. CP-4 GrokRulesReadyOrderingTests 5; CP-5 AgentSessionLaunchFailureTests 79; CP-6 SessionMessageQueuePhoneHomeDropTests 5; CP-7 canceled-turn 20 and interrupted-launch/resume 9.

| ID | Actual outcome |
|---|---|
| V-1 | Passed CP-1: raw and conditional Input remain responsive while retention phase A is held. Both variants were red before the fix. |
| R-1 | Passed CP-1: phase B independence and real-engine recent/live workspace protection; workspace mutations still serialize. |
| V-2 | Passed CP-4/CP-5/CP-6 plus S6 CP-7: uncertain transport preserves the generation/attempt; late wire ACK alone is not confirmation; matching UserPrompt/ACK confirms without duplicate body input; external caller cancellation still propagates. |
| R-2 | Passed full CP-5 after S6 and adjacent CP-2/CP-7: true launch/readiness failures still clean up; generation, encoding, resume and cancellation contracts retained. |
| V-3 | Passed CP-1: wait/hold timing and quiet fast path. The witness was red with zero timing lines before implementation. |
| R-3 | Passed CP-3: all named guards/index and the matched-glob impacted-class row. |
| V-4 | Fresh TRX roster/count inspection complete; S6 exact-source receipt validation exit 0. Historical full report's rejection is retained above. Evidence-diff, OS inventory and conflict-free merge-tree were checked; final publication checks are reported with the pushed report tip. |

Manual: inspected unchanged LF normalization, bracketed paste and separate Enter in RunnerTerminalSession, with 5 encoding cases passing. Inspected real PhoneHomeRuntimeAdapter delegation and both runtime Input methods' per-session launch gate. No OS-selected production branch/factory/property changed; existing OS guards in changed files and named helpers were inventoried. check-os-witnesses returned 0 for syntax/inventory with native Windows pending, not native execution. The brief explicitly assigns native Windows proof to the orchestrator. No migrations; downgrade/model obligations are not applicable.

The matched impacted-classes kind is test-lane-category, selecting TestLaneCategoryGuardTests (CP-3). The wider derived closure is recorded in ignored .antiphon/c1892-impact-final.json (101 derived classes plus capped remainder) and is not represented as passed. The commissioned scope is closed rows, named affected/adjacent integrations and matched-glob rows; no Unit/full-assembly lane ran. Unit stays with nightly/rc.

## Source, cost and execution provenance

Every checkpoint invocation supplied committed HEAD as expected source SHA. Essential unedited CHECKPOINT lines follow. Source/build fields are preserved even for non-proof runs. For each run, the recorded report.json hash identifies the untouched generated receipt; TRX hashes are listed when present. Generated payloads remain ignored in their original checkpoint directories.

### 20261113-161039-7c53

Stopped supported run to remove an outer build-slot lease around the tool. No credited test execution. Partial output was kept.

Receipt: /work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-161039-7c53/report.json; SHA-256 35da7da8d1b0514a5939520b9438f220b80706e5215314b01b993132269c6ecc.

```text
CHECKPOINT CP-1 commit=05557245bc440c02d2f538dcf33593bd24781417 build=n/a filter=/*/*/(C1677RetentionDispatchTests*)|(PhoneHomeCommandDispatcherTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=05557245bc440c02d2f538dcf33593bd24781417 sourceState=clean buildSource=unknown
BUDGET wall=132.786s execution=27.808s admissionOnly=104.978s
unlisted: none (the tool ran no other build or test command)
wall: 2m38s  sequential-equivalent: 0m00s  builds: 3  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
```

### 20261113-161507-6e69

CP-1 refused pre-existing output from the stopped run. CP-4 ran 3 invalid fixture cases: its receipt path lacked the required grok segment, so no Input occurred. Not red proof.

Receipt: /work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-161507-6e69/report.json; SHA-256 6fcadbeca5c7b84c4e634986e4d848612bb75aa74b250c832947a36ededa447a.

```text
CHECKPOINT CP-1 commit=fb3b880b408166b572e71973991877bcff26df53 build=failed filter=/*/*/(C1677RetentionDispatchTests*)|(PhoneHomeCommandDispatcherTests*)/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=30s slot-reason=refused: pre-existing-output dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-1 waited=30s executed=0.000s wall=0.000s
CHECKPOINT CP-4 commit=fb3b880b408166b572e71973991877bcff26df53 build=ok filter=/*/*/GrokRulesReadyOrderingTests/Ambiguous_input* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-161507-6e69/rows/CP-4/run.trx slot=granted waited=45s dirty=0 source=fb3b880b408166b572e71973991877bcff26df53 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=45s executed=57.245s wall=102.310s
PHASES CP-4 slotWait=75s build=400.9000106s startup=33.7038547s testsWall=17.2644943s teardown=5.4727048s hostWall=56.441281s
BUDGET wall=616.018s execution=511.051s admissionOnly=104.967s
unlisted: none (the tool ran no other build or test command)
wall: 10m16s  sequential-equivalent: 1m42s  builds: 3  max-concurrent-builds: 1  rows: 0 green 2 red 0 skipped
```

CP-4 TRX SHA-256 a5d75cc65d074c95181116192f5b276b177a7fa24740f83b6fcb730211b8ab61.

### 20261113-162425-ead1

Runner build failed because the renamed old method was still named by the C1722 pure-method census. The old method was restored with a held non-Input mutation. No credited test execution.

Receipt: /work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-162425-ead1/report.json; SHA-256 1774dbe05f6be55fd62d1664f893bcfc0c4383a40e895e45dc4829b75a8145d6.

```text
CHECKPOINT CP-1 commit=fb3b880b408166b572e71973991877bcff26df53 build=failed filter=/*/*/(C1677RetentionDispatchTests*)|(PhoneHomeCommandDispatcherTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=fb3b880b408166b572e71973991877bcff26df53 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-1 waited=0s executed=0.000s wall=0.000s
BUDGET wall=167.165s execution=92.169s admissionOnly=74.996s
unlisted: none (the tool ran no other build or test command)
wall: 2m47s  sequential-equivalent: 0m00s  builds: 3  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
```

### 20261113-162854-5c04

Valid red-first proof before production fixes: 70 executed, 58 passed, 12 failed, 0 skipped. CP-1: both Input variants timed out and timing logs were absent. CP-4: all 3 ambiguous wire outcomes reached Input and launch cleanup killed the adapter. CP-5: timeout/transport killed; cancellation escaped without preserving the original attempt. CP-6: 3 cases lost the required unknown Sent state.

Receipt: /work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-162854-5c04/report.json; SHA-256 e6048cb780c769b6dac6d1242c30609693fd05768523fd9cb39c2ac1abfa800f.

```text
CHECKPOINT CP-1 commit=9cb4a47e7d3eccb8eec944fa4037a8da6b4f85de build=ok filter=/*/*/(C1677RetentionDispatchTests*)|(PhoneHomeCommandDispatcherTests*)/* executed=59 passed=56 failed=3 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-162854-5c04/rows/CP-1/run.trx slot=granted waited=450s dirty=0 source=9cb4a47e7d3eccb8eec944fa4037a8da6b4f85de sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=450s executed=10.234s wall=460.368s
PHASES CP-1 slotWait=540s build=98.9949618s startup=5.1293893s testsWall=3.4703898s teardown=0.5849923s hostWall=9.1848335s
CHECKPOINT CP-4 commit=9cb4a47e7d3eccb8eec944fa4037a8da6b4f85de build=ok filter=/*/*/GrokRulesReadyOrderingTests/Ambiguous_input* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-162854-5c04/rows/CP-4/run.trx slot=granted waited=75s dirty=0 source=9cb4a47e7d3eccb8eec944fa4037a8da6b4f85de sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=75s executed=76.138s wall=151.315s
PHASES CP-4 slotWait=360s build=613.3172429s startup=36.5289909s testsWall=32.0924786s teardown=6.0492236s hostWall=74.6706929s
CHECKPOINT CP-5 commit=9cb4a47e7d3eccb8eec944fa4037a8da6b4f85de build=reused filter=/*/*/AgentSessionLaunchFailureTests/Card_input_uncertainty* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-162854-5c04/rows/CP-5/run.trx slot=granted waited=165s dirty=0 source=9cb4a47e7d3eccb8eec944fa4037a8da6b4f85de sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=165s executed=55.572s wall=220.641s
PHASES CP-5 slotWait=165s build=0s startup=21.850658s testsWall=11.9111426s teardown=20.9673765s hostWall=54.7291775s
CHECKPOINT CP-6 commit=9cb4a47e7d3eccb8eec944fa4037a8da6b4f85de build=reused filter=/*/*/SessionMessageQueuePhoneHomeDropTests/* executed=5 passed=2 failed=3 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-162854-5c04/rows/CP-6/run.trx slot=granted waited=75s dirty=0 source=9cb4a47e7d3eccb8eec944fa4037a8da6b4f85de sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=75s executed=57.029s wall=132.118s
PHASES CP-6 slotWait=75s build=0s startup=29.2664529s testsWall=15.6653886s teardown=10.7948958s hostWall=55.7267376s
BUDGET wall=1438.763s execution=969.957s admissionOnly=468.806s
unlisted: none (the tool ran no other build or test command)
wall: 23m58s  sequential-equivalent: 16m04s  builds: 3  max-concurrent-builds: 1  rows: 0 green 4 red 0 skipped
```

CP-1 TRX SHA-256 cfaccfb7dffcf3310d09189fb219a6eeb1de3748ed16d142caaa8614dfb4a9a0.

CP-4 TRX SHA-256 37a536a6a6800151612ebdf907e4b1f4a56129543ce62d4200595a7368f60957.

CP-5 TRX SHA-256 9e90cf60f8c4239b8a193778f710bd0c3f1c86d737fa7f99d6026d5c2c2fb76e.

CP-6 TRX SHA-256 c4f1829ce827651c8fa2b2e470f58f858f34add9a9c21f5e3ce2cac22c9378c7.

### 20261113-165749-bbce

Stopped before the server rows after inspection found fresh unknown attempts needed evidence-first recovery. CP-1 had 156/157: the introduced census omission was fixed by naming the two pure fake/clock methods. No timeout or assertion was loosened.

Receipt: /work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-165749-bbce/report.json; SHA-256 e92509dc3eb43778f7475e6b14b567c9d766a26cf621a8843a5613fc1ef21b6a.

```text
CHECKPOINT CP-1 commit=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 build=ok filter=/*/*/(C1677RetentionDispatchTests*)|(PhoneHomeCommandDispatcherTests*)|(C1722GitTimeoutBudgetTests*)/* executed=157 passed=156 failed=1 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-165749-bbce/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=15s executed=10.989s wall=27.834s
PHASES CP-1 slotWait=105s build=91.0546617s startup=5.8537387s testsWall=3.3845077s teardown=0.6353642s hostWall=9.8736193s
CHECKPOINT CP-2 commit=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 build=n/a filter=/*/*/(GrokRulesInitializationTests*)|(GrokRulesFailureTests*)|(PhoneHomeLaunchTransportTests*)|(SessionMessageQueueInterruptedAttemptTests*)|(RunnerTerminalSessionInputEncodingTests*)|(RunnerTerminalSessionGenerationTests*)|(AgentSessionCancelledTurnTests*)|(AgentSessionInterruptedLaunchResumeTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 sourceState=clean buildSource=unknown
CHECKPOINT CP-3 commit=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 build=n/a filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(DerivedImpactIndexTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 sourceState=clean buildSource=unknown
CHECKPOINT CP-4 commit=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 build=n/a filter=/*/*/GrokRulesReadyOrderingTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 sourceState=clean buildSource=unknown
CHECKPOINT CP-5 commit=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 build=n/a filter=/*/*/AgentSessionLaunchFailureTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 sourceState=clean buildSource=unknown
CHECKPOINT CP-6 commit=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 build=n/a filter=/*/*/SessionMessageQueuePhoneHomeDropTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 sourceState=clean buildSource=unknown
BUDGET wall=269.161s execution=179.181s admissionOnly=89.980s
unlisted: none (the tool ran no other build or test command)
wall: 5m07s  sequential-equivalent: 0m27s  builds: 2  max-concurrent-builds: 1  rows: 0 green 6 red 0 skipped
```

CP-1 TRX SHA-256 fc8d3c79fb909d6ce455d6eda64b4d20c1ae702a756049514a54134eb2a47c66.

### 20261113-170610-bff7

Final ordinary group: five rows green, CP-5 78/79. The introduced external-caller cancellation regression was repaired in S6. The report remains RED and has not been edited or relabeled.

Receipt: /work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-170610-bff7/report.json; SHA-256 74d53fb4abfefd71aca24f3074174ef861df8724957d68cb8446848a57e6059e.

```text
CHECKPOINT CP-1 commit=780ac1aca712906ab984f73936a60edf6fc14b3a build=ok filter=/*/*/(C1677RetentionDispatchTests*)|(PhoneHomeCommandDispatcherTests*)|(C1722GitTimeoutBudgetTests*)/* executed=157 passed=157 failed=0 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-170610-bff7/rows/CP-1/run.trx slot=granted waited=105s dirty=0 source=780ac1aca712906ab984f73936a60edf6fc14b3a sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=105s executed=5.864s wall=110.913s
PHASES CP-1 slotWait=210s build=91.2662683s startup=2.8422669s testsWall=1.6263185s teardown=0.6308614s hostWall=5.0994675s
CHECKPOINT CP-2 commit=780ac1aca712906ab984f73936a60edf6fc14b3a build=ok filter=/*/*/(GrokRulesInitializationTests*)|(GrokRulesFailureTests*)|(PhoneHomeLaunchTransportTests*)|(SessionMessageQueueInterruptedAttemptTests*)|(RunnerTerminalSessionInputEncodingTests*)|(RunnerTerminalSessionGenerationTests*)|(AgentSessionCancelledTurnTests*)|(AgentSessionInterruptedLaunchResumeTests*)/* executed=95 passed=95 failed=0 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-170610-bff7/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=780ac1aca712906ab984f73936a60edf6fc14b3a sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=30s executed=89.496s wall=119.679s
PHASES CP-2 slotWait=135s build=354.1385336s startup=32.7752861s testsWall=39.1353173s teardown=16.2971347s hostWall=88.2077382s
CHECKPOINT CP-4 commit=780ac1aca712906ab984f73936a60edf6fc14b3a build=reused filter=/*/*/GrokRulesReadyOrderingTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-170610-bff7/rows/CP-4/run.trx slot=granted waited=135s dirty=0 source=780ac1aca712906ab984f73936a60edf6fc14b3a sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=135s executed=47.214s wall=182.286s
PHASES CP-4 slotWait=135s build=0s startup=22.8615654s testsWall=19.3435459s teardown=4.0788378s hostWall=46.2839488s
CHECKPOINT CP-3 commit=780ac1aca712906ab984f73936a60edf6fc14b3a build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(DerivedImpactIndexTests*)/* executed=106 passed=106 failed=0 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-170610-bff7/rows/CP-3/run.trx slot=granted waited=120s dirty=0 source=780ac1aca712906ab984f73936a60edf6fc14b3a sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=120s executed=215.322s wall=335.418s
PHASES CP-3 slotWait=120s build=0s startup=17.3540257s testsWall=196.8436299s teardown=0.4953726s hostWall=214.6930281s
CHECKPOINT CP-5 commit=780ac1aca712906ab984f73936a60edf6fc14b3a build=reused filter=/*/*/AgentSessionLaunchFailureTests/* executed=79 passed=78 failed=1 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-170610-bff7/rows/CP-5/run.trx slot=granted waited=60s dirty=0 source=780ac1aca712906ab984f73936a60edf6fc14b3a sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=60s executed=71.692s wall=131.752s
PHASES CP-5 slotWait=60s build=0s startup=20.3952371s testsWall=39.24643s teardown=11.3849011s hostWall=71.0265678s
CHECKPOINT CP-6 commit=780ac1aca712906ab984f73936a60edf6fc14b3a build=reused filter=/*/*/SessionMessageQueuePhoneHomeDropTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-170610-bff7/rows/CP-6/run.trx slot=granted waited=60s dirty=0 source=780ac1aca712906ab984f73936a60edf6fc14b3a sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=60s executed=41.954s wall=102.005s
PHASES CP-6 slotWait=60s build=0s startup=21.5864072s testsWall=16.0002628s teardown=3.5459657s hostWall=41.1326356s
BUDGET wall=1199.033s execution=932.099s admissionOnly=266.935s
unlisted: none (the tool ran no other build or test command)
wall: 19m59s  sequential-equivalent: 16m22s  builds: 2  max-concurrent-builds: 1  rows: 5 green 1 red 0 skipped
```

CP-1 TRX SHA-256 f6e9958dc8207e8deddb14d25b87c764b1ac814384350d5a70897b6b1ae44d88.

CP-2 TRX SHA-256 45a698da5ba1906f39331d0d4afd89cd0ca24aad808ce8e414a97667cb5cc4d0.

CP-4 TRX SHA-256 336401929d0ae9e67b838b7a1976d10e323bc6d690d4d277d9544d88dfd7af3c.

CP-3 TRX SHA-256 310caa964e2ff8a8dabc5be2be7d9e5855ee63d30ea775662c463d4b112aa7d1.

CP-5 TRX SHA-256 c82ea4dd2b15047e82dcb88680a0202302256ec826c635ed3687cad954235fa6.

CP-6 TRX SHA-256 335b81d76f532b830718109810c402d3f3d2c022d68d12dfdaaa3ebfbf70e228.

### 20261113-173153-f799

S6 exact red-row plus affected-adjacent rerun: CP-5 79/79, CP-7 29/29, zero failed/skipped. GREEN. Exact-source validation returned 0.

Receipt: /work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-173153-f799/report.json; SHA-256 dbdde598196ac10d3912f559064824be61583e5d95c91c29a24119680b6ed44f.

```text
CHECKPOINT CP-5 commit=5427470e3e4b6dc1d2b22931c0bd604bc09d34ab build=ok filter=/*/*/AgentSessionLaunchFailureTests/* executed=79 passed=79 failed=0 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-173153-f799/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=5427470e3e4b6dc1d2b22931c0bd604bc09d34ab sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=99.146s wall=99.322s
PHASES CP-5 slotWait=15s build=376.5494887s startup=28.3837308s testsWall=51.8458682s teardown=17.5274463s hostWall=97.7572701s
CHECKPOINT CP-7 commit=5427470e3e4b6dc1d2b22931c0bd604bc09d34ab build=reused filter=/*/*/(AgentSessionCancelledTurnTests*)|(AgentSessionInterruptedLaunchResumeTests*)/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/task-13c4448d/.antiphon/checkpoints/20261113-173153-f799/rows/CP-7/run.trx slot=granted waited=30s dirty=0 source=5427470e3e4b6dc1d2b22931c0bd604bc09d34ab sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=30s executed=73.892s wall=104.060s
PHASES CP-7 slotWait=30s build=0s startup=34.8640337s testsWall=33.6937068s teardown=4.7250994s hostWall=73.2828401s
BUDGET wall=659.085s execution=644.101s admissionOnly=14.984s
unlisted: none (the tool ran no other build or test command)
wall: 10m59s  sequential-equivalent: 3m23s  builds: 3  max-concurrent-builds: 1  rows: 2 green 0 red 0 skipped
```

CP-5 TRX SHA-256 c1df63bab0c5e6272ce43a8ca31884ca92d6ea52938e5e401ca705e20098f6c8.

CP-7 TRX SHA-256 c5ddf7568cfdab8d106bc002f540eb3cc35e6e1aab12faa8396773c44d86f530.

## Accounted runs and remaining work

Unlisted bootstrap/code-generation drivers, all gated: checkpoint tool native build (wait 90 s, held 7 s); DerivedImpactIndex native build (wait 0 s, held 19 s); three already-built index generations (wait/held 1/40 s, 0/42 s, 15/38 s). The first changed the committed index, the later two reported unchanged. No direct custom build output or unleased driver was used. The tool's own run/stop/validate coordinators used its internal row/build leases. The original outer lease was stopped and released. No run exceeded 30 minutes; final ordinary group was 19m59s (932.099 s charged execution, 266.935 s admission-only union), S6 was 10m59s (644.101 s charged, 14.984 s admission-only). Unchanged green selections were not repeated for flake proof. No deliberate mutant or loaded repetition ran.

The nightly protected policy inputs were not changed. Stored master policyHash is 7763a891c3bdde500976e5391b028f3667590ba1186e036abfbf7ce826857fcb and live-lane pinned input digest is 2b9353e8b21fd177508ada0e40db00cdb4d1833933f70af9adc160a8ac709324. No pin refresh was required. Derived index regeneration has its own commit. Evidence-diff over the complete task range returned zero violations before this report and is rerun over the published report tip. Fetched origin/master was e4c45f4b4bb58870bb8684087a6d218e1b988cbe; merge-tree at the last tested source was conflict-free (tree 370e577a63ac8f196b4079148fcd4b922fe8987d); publication repeats the check.

All variants remain pending SourceLanding Mutation: PC-1 raw/conditional Input global-lock restoration; PC-2 Grok timeout/cancellation/disconnect and card timeout/cancellation/transport cleanup; PC-3 Enter, body-to-Enter, re-Enter and expired rules deadline; PC-4 wait/hold/threshold diagnostics; PC-5 external caller cancellation propagation. Mutation owns deliberate red/restore/green, missing-control discovery and binding the unbound candidate. Code's red-first witnesses are not PC receipts.

Native Windows execution is pending with the caller. Activation requires both server and runner on the new build; neither was restarted or reconfigured. The live Windows qualification and production compiler/services were untouched. Proceeded under the caller's standing Code authority without requesting another approval.

## Commits

05557245bc440c02d2f538dcf33593bd24781417 test(card-1892): witness Input starvation during retention phase A
fb3b880b408166b572e71973991877bcff26df53 test(card-1892): pin unknown delivery launch survival and lock diagnostics
36cf78247f7ab42c2fc16634837fafa58e498a98 test(card-1892): correct receipt fixture and cover card launch uncertainty
9cb4a47e7d3eccb8eec944fa4037a8da6b4f85de docs(card-1892): keep red witness checkpoint rows in one table
ae0e64891432b02db63a48829e4771332fffdf43 fix(card-1892): keep Input off the workspace mutation gate
0b2c08ae7149704b2f34b429a8a3880809f4761d fix(card-1892): retain live sessions and attempts after ambiguous Input
debf86bd2fb37578fa5586bfdb8fbf4d2cec7708 docs(card-1892): record uncertainty contract and closed final verification
a74b5e326e22e7d06d16a05bf544a87f0e885c45 chore(card-1892): regenerate derived impact index
adf6e478a22f7b47f1fa651b1ab5bfce7c838fd2 docs(card-1892): bind matched-glob impact to final checkpoint scope
4640d8d2a874f24c86d375e48862a8f70f389559 fix(card-1892): reconcile fresh uncertain attempts before later input
780ac1aca712906ab984f73936a60edf6fc14b3a docs(card-1892): record fresh recovery and verification retry
0dac6b49aef6247ec1a5ee2a33d4393c43da6dfd fix(card-1892): propagate caller cancellation without launch cleanup
5427470e3e4b6dc1d2b22931c0bd604bc09d34ab docs(card-1892): bind cancellation repair to exact rerun rows
9b6a4314352846aba95cf0a46ead04cb748a6c56 docs(card-1892): finalize review selection and pending cancellation control

The publication commit adds this evidence and retains the actual tested SHAs above.
