# Why the dispatch-base Queued flake escaped

Task 198a988b started from current origin/master, fc92658cb88fb282455230b8e04595df78e2a58d, on 2026-11-14 UTC. This is a test synchronization defect. No product behavior, clock, budget, runner setting, timeout, retry policy or assertion is relaxed.

## Root cause

C508_GuardRefMismatchWarnedOnce and C540_ReducedWarningsKeepBaseDiagnostics edit the project's default branch from inside the repository-lease hook. The locked claim correctly rejects its frozen observation. AgentTaskDispatcher then invalidates that result and immediately re-enqueues the previous intent (CARD-1428 D-3).

The tests call TickInspectedAsync again without first joining that newly queued worker. If it has already finished, the fresh cycle works. If it is still running, Peek with the changed default cancels the old intent. SiblingInspectionResults deliberately retains cancelled entries until their owned worker has joined; Begin refuses the new intent during that interval. TickInspectedAsync waits for the old worker, then issues its consumption tick. That tick only enqueues the replacement inspection. The helper returns while the task is correctly still Queued.

The test then asserts Dispatched one worker-completion/consumption phase too early. A later normal dispatch tick can consume the replacement; the task is not permanently stranded. Completed entries count toward Outstanding, so this is not an Outstanding-versus-InFlight counting error.

Relevant code: tests/Antiphon.Tests/TestHelpers/SiblingInspectionDispatch.cs; server/Application/Services/AgentTaskDispatcher.cs (stale-at-claim re-enqueue); server/Application/Services/SiblingInspectionResults.cs (Peek, Begin, Evict, Complete); server/Application/Services/CardSiblingInspectionQueue.cs (worker skip when its intent no longer matches).

Git blame dates both immediate re-observation calls to e85435b9235 (2026-10-19). The stale-claim re-enqueue behavior originated in c00e3d1da66d43c6749310ea26e69451e4a4b1d2 (2026-10-22). The target test file's latest prior change was 3d58e591fa54461b6d676aebdab52f7d5f078094 (2026-11-03). Recent CARD-1892 delivery hold, host-load/clock SQL and CARD-0824 dispatch changes are present at the baseline, but neither a shared runner budget nor those features are needed to explain this interleaving. Each fixture owns an isolated database, scratch repository and inspection queue; its configured task cap is 512.

## Repair

Join the inspection started by the rejected claim before beginning the fresh TickInspectedAsync cycle, at the two explicit re-observation sites. The cycle then starts with no old worker still joining and can enqueue, await and consume the new intent. Preserve the assertion that the first claim stays Queued, the final Dispatched assertion, the base/ref assertions, all warning/delivery assertions and cancellation. This adds synchronization on owned work, with no sleep or dispatch-until-success loop.

## Evidence

Baseline class repetition first failed on run 5: 101 executed, 100 passed, one failed (C540_ReducedWarningsKeepBaseDiagnostics), with exactly the reported Dispatched-versus-Queued assertion. The preceding four runs each passed 101. Source was unchanged and all runs used the gated native Debug output built from fc92658cb88fb282455230b8e04595df78e2a58d. Both methods then passed 20 isolated serial runs each: C508 executed 80 parameter cases and C540 executed 20 cases. Two subsequent serial full-class runs executed precisely the same 101-case order (SHA-256 of the ordered names: 0dbddb307856de3952bc6ee0cc16d01effb3cacf34a2dc0ac0e8ec8fd1cc2abe). The first failed C540 with the same assertion; the second passed. The same immediate predecessor, C540_ClaimRollbackHasNoCollapsedIntent(False, False), preceded the target in both passing and failing runs. A particular predecessor is not a sufficient cause, and concurrent tests are not required. In total, baseline class executions failed 2 of 7 times, both at C540; the 40 isolated invocations were all green.

A one-second, gated deterministic harness exercised the actual baseline SiblingInspectionResults implementation. It held an old intent pending, changed the default, verified cancellation and refusal of its replacement while the old worker was joining, completed the old entry, then verified that the helper's consumption phase could only enqueue the replacement. A subsequent completion made that result consumable. The paired joined-first schedule admitted and completed the replacement within the intended cycle. The unedited output is intent-race.log; the harness is intent-race.ps1 under the ignored evidence root. This proves the queue-lifetime mechanism independently of test order; it does not claim a live dispatcher run.

The repair is committed and pushed as 67a30c6c93b1b92d97b5d13639ee7838e990786c. Post-fix class verification passed 15 of 15 invocations: 1,515 executed, 1,515 passed, zero failures and zero skips. The guard union passed 114 executed tests across all 11 requested classes; one additional test was skipped: WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact, whose child skip-roster audit requires Windows placement. This Linux run does not supply that native Windows receipt.

| Selection | Runs | Executed | Passed | Failed | Skipped | Driver wall |
|---|---:|---:|---:|---:|---:|---|
| Baseline class, default scheduling | 5 | 505 | 504 | 1 | 0 | 6m19s |
| Baseline C508 method alone, serial cases | 20 | 80 | 80 | 0 | 0 | 15m42s |
| Baseline C540 method alone | 20 | 20 | 20 | 0 | 0 | 15m42s |
| Baseline class, identical serial order | 2 | 202 | 201 | 1 | 0 | 4m03s |
| Fixed class | 15 | 1,515 | 1,515 | 0 | 0 | 18m27s |
| Fixed guard union | 1 | 114 | 114 | 0 | 1 | 3m42s |

All post-fix tests used the native Debug output built from the pushed repair commit above, with UseAppHost=false on both build and run. The fixed build passed with zero errors in 2m43.81s. The newly built Antiphon.DerivedImpactIndex driver passed with zero warnings/errors; both subsequent serial generation invocations printed derived-impact-index unchanged. No generated index change or policy/input pin update was needed. Every build/test driver obtained a build slot with a recorded zero-second admission wait. Every individual execution finished below ten minutes. Whole-run nested phase gaps remain unknown rather than zero.

Guard membership from the fresh TRX: TestClassificationPolicyTests 42 passed; TestClassificationGuardTests 1; SlowTestRegistryMergeTests 5; SlowTestTripwireTests 2; NativeLinuxPolicyWitnessTests 3; DerivedImpactIndexTests 27; MechanicalPcContractTests 11; TaskPlatformGuidanceTests 5; C1729LayeringGuardTests 11; TestLaneCategoryGuardTests 1; WindowsUnitPlatformAdmissionTests 6 passed and 1 Windows-only skip.

The caller independently reported identical failures at bare master 92cadbb21 (one of five runs) and the previous combined tip (three of seven). Those historical costs/outcomes are caller evidence, not fresh executions credited here.

## Why earlier landings and qualification missed it

A successful single execution is weak evidence for a race: this investigation itself obtained four complete green class runs immediately before the fifth failed. Which method loses depends on when its own worker finishes. A different host load or test scheduling order changes that window without introducing the defect. Earlier successful merge runs therefore do not establish that a later unrelated change caused it.

The fixture did not force the old-worker/changed-intent boundary, so a qualification execution could only sample the race under its incidental schedule. The class is Integration + Slow; a Unit-only landing check cannot cover it. The live Windows qualification and its logs were explicitly outside this task's access scope and were not inspected. Its exact class inclusion, source and completion cannot be asserted from this investigation. If it ran the class successfully, that is another single probabilistic observation; if it omitted it, it supplies no coverage for this fixture. The caller should reconcile that coverage against the qualification's executed manifest before crediting it.

The cheapest demonstrated pre-land detector here was five repetitions of the named class (6m19s of driver wall time): it exposed the failure that 40 isolated invocations (about 31m24s) missed. For inspection/claim lifecycle changes, retain this focused class check and explicitly review the enqueue/join/consume phases when an intent changes. A future deterministic dispatcher witness should hold the old worker across the intent change; a single isolated green is not a race gate. Repetition can expose the old fixture's race, but a fixed repeat count cannot prove its absence. The repair removes the unjoined phase rather than buying a larger timing window. No full assembly or live qualification rerun is needed to check these two test changes.

## Reproduction and provenance

Generated evidence is ignored under .antiphon/debug-198a988b: per-run TUnit TRX/logs, runs.jsonl with exact filter, source, UTC bounds and /proc/loadavg, and opt-in phase shards for the isolated/fixed runs. TRX records test membership and durations; missing nested phases remain unknown. Every build/test driver uses scripts/build-slot.ps1. Baseline build: 3m40.66s, 0 errors. Initial /proc/loadavg: 2.27 2.81 4.38; later baseline values and slot waits are recorded per run.

At baseline, policyHash verified as bd6689bbc7ea752f13a69f872ea86a171fc8afc94e90ef9ed0c8d53aab00fbaa and PinnedInputDigest verified as 070479b6da9902cc6cbd2b791baf855f01333599f6041cc017c45a8fe0cf9c75. Their inputs are outside the repair.

## Rerun

Build once from the repair source, using the native default output:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label dispatchbase-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj -p:UseAppHost=false
```

Run the following class command 15 times serially, using a fresh results directory and TRX filename each time. Each invocation takes its own slot:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label dispatchbase-class -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/AgentTaskDispatchBaseGuardTests/*' --report-trx --report-trx-filename class-01.trx --results-directory .antiphon/dispatchbase-rerun/class-01
```

For baseline method isolation, replace the filter with `/*/*/AgentTaskDispatchBaseGuardTests/C508_GuardRefMismatchWarnedOnce` or `/*/*/AgentTaskDispatchBaseGuardTests/C540_ReducedWarningsKeepBaseDiagnostics`, and add `--maximum-parallel-tests 1`. For the serial class-order check use the class filter plus the same serial option; compare actual TRX start order.

Guard union, one invocation of the same built output:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label dispatchbase-guards -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/*' --report-trx --report-trx-filename guards.trx --results-directory .antiphon/dispatchbase-rerun/guards
```

Evidence root: /work/worktrees/task-198a988b/.antiphon/debug-198a988b. Failing TRX/log pairs: base-class-05/base-class-05.trx and .log, base-serial-01/base-serial-01.trx and .log. Passing post-fix files: fixed-class-01 through fixed-class-15, and guard-union-01. runs.jsonl records source/filter/time/load; summary.json groups counts and class membership; fixed-build-provenance.json records the built test assembly digest. Source stayed frozen during every build/test run. No production process, live qualification, runner, setting or volume was modified; restart is unnecessary.
