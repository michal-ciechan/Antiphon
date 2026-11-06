# CARD-1544 Code report

Final verification is incomplete: the required whole Unit row hit its unchanged
45-minute deadline without a TRX. The C1374 closure was already repaired in the
assigned base; its fresh exact check passes. This task also retargets two
baseline-confirmed stale test guards to the formatter's current Core path.

Original Code task / landing owner: `5fb6b1b2-44dd-47c3-8a99-8eea293f1ad6`.
Branch: `feat/card-task-5fb6b1b2`.
Worktree: `/work/worktrees/task-5fb6b1b2`.
Task base: `df1fc145181a3bedf444f9113f822f30d3b136f6`.
Round: Final. Ordinary scope: incomplete. Restart: none.

Commits pushed individually:

- `4034b60bd99619d9f411bddafc360e13ee867581`: verification plan and unbound controls.
- `311888ef27307a25798eeea75de337f8e542fc8d`: two test-path repairs plus plan/control amendments.
- The final report-only commit is identified by the final response and remote progress line.

Plan: `docs/superpowers/plans/2026-11-06-card-1544-source-closure-verification.md`.
Mutation candidate: `docs/superpowers/mutations/2026-11-06-card-1544-source-closure-candidate.md`.

The card and its history were read first. Board search found only CARD-1544 for
the distinctive closure phrase. Git history found the landed repair
`01231c66d570daceba09608edb59343647d4ce82` (CARD-1572), confirmed an ancestor
of this task base. It moved the native Linux gate from C1374CensusProbes.Check
to the host caller's first statement and rostered that caller in Existing.
Accounting is helper=0 independent skip results, host caller=1, wrapper caller=0
native-gate entries. Windows child totals do not change because Existing is
outside the child selection. No gate, roster entry, reason, closure assertion,
production behavior, test attribute or timeout was changed here.

The card's historical red is `77eb7806d35b3b522d46e3942742e4864c688de3`,
one executed/failed, zero passed/skipped, naming C1374CensusProbes.Check. That
is attributed historical evidence from the card, not a fresh run by this task.
The fresh closure method at `4034b60bd99619d9f411bddafc360e13ee867581`
executed once and passed once, with zero skips, in 15.1806075 seconds test time.

The Unit console exposed two stale paths after the existing formatter move:

- RepairSourceDocumentationTests.C499_V31_DocsAndSkillNameTheRepairContract
  read the absent Application/Services/DelegationReportFormatter.cs.
- SpecialistRoleContractTests.no_Check_comparison_survives_outside_the_allowlist
  still allowlisted Services, reporting the same formatter at Core lines 305/938.

Both exact methods were rerun at untouched task base
`df1fc145181a3bedf444f9113f822f30d3b136f6`: each executed once and failed
once with zero skips, matching the original diagnostics. Source was clean and
buildSource verified. Only the two path strings were repaired, preserving the
same class exception and all assertions. The IO failure is ordinary attribution;
it is not claimed as a valid positive-control assertion failure.

Every verification ID and actual outcome:

| ID | Actual outcome |
|---|---|
| V-1 | Passed: repair ancestry and exact current helper/caller/roster accounting inspected. |
| V-2 | Passed: CP-1 exact closure, 1 passed / 0 failed / 0 skipped at 4034b60bd99619d9f411bddafc360e13ee867581. |
| R-1 | Full admission class in CP-2: 5 passed, 0 failed, 1 explicit Windows child-audit skip. Native Windows execution remains unperformed, not passed. |
| R-2 | CP-2 full named guards: classification policy 39 passed; classification guard 1; registry merge Integration 5; tripwire 2; native Linux witness 3; layering 9; mechanical contract 11; TaskPlatformGuidance 5. Total with R-1: 80 executed/passed, 0 failed, 1 skipped. Fresh TRX verified every intended class. |
| R-3 | INCOMPLETE: CP-3 whole Unit, exact Category=Unit filter, timeout=45m, executed=2701.041s, waited=105s, wall=2806.102s, exit 5, no TRX. Counts and full affected-class outcomes from this row are unknown. No Full receipt or passed Unit lane is claimed. |
| R-4 | UNVERIFIED: build passed, zero errors, compiler 12m35.76s, build slot=granted waited=0s. Test slot granted after 150s as the budget stop was issued. Stop returned 0; wait returned 6, executor died without phase=done, no terminal report. No qualified test result or pass is claimed. |
| Manual | Source snapshots validated against exact tested SHAs and verified builds. OS inventory and evidence-history guards passed at the repair SHA. Final-tip checks/cleanup are recorded below. |

No IDs were deferred to a later Final or marked passed without evidence. This
round is Final and R-3 remains unmet. No whole assembly was run. Before the
broad Unit selection, the plan named its invariant (complete Unit classification
coverage), unbounded class impact including process-spawning Scripts/Application
and checkpoint tests, cost and unchanged 45-minute limit. Native Windows skips
do not provide Windows runtime proof; no changed production OS branch exists.

The required native incremental compile bootstrapped the referenced checkpoint
tool through the build-slot gate: compiler elapsed 2m57.76s, zero errors,
slot=granted waited=195s, held=179s, maxcpucount=6. It used native default output,
UseAppHost=false and TEMP=/tmp. This was the disclosed non-table build.
The extra baseline build/two exact rows were required to establish inherited
attribution, not a baseline assembly run. Its compiler elapsed 5m34.04s;
checkpoint wall=1859.120s, execution=418.848s, admission-only=1440.272s.
CP-4 is a separate committed repair group, one build and one combined full-class
filter. No green row was rerun, no deliberate mutant executed, no assertion
loosened and no timeout widened. All build/test drivers acquired granted slots.

The main run exceeded 30 minutes: wall=3591.834s, execution=3276.852s,
admission-only=314.982s. The Unit host itself exceeded 30 minutes. Caller must
commission performance Debug with this source/filter/run identity, host Debian
12 Linux (24 cores), observed broker occupancy 4/4 and maxcpucount=6. This delegate
cannot sub-delegate. The process was observed active (including a newly spawned
pwsh child); no claim of a stall or flake is made. No per-test/fixture-phase
journal or TRX was produced for CP-3; its last executing method and setup/body/
teardown attribution remain unknown. Do not blindly repeat or widen the row.
The missing proof must cover method-level Unit tags as well as class-level tags.

Policy JSON and both pins are unchanged; no refresh was required. Old=new
nightly policyHash: `2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72`.
Old=new live-lane PinnedInputDigest:
`417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939`.
No land-graph Update, deployment, server restart, runner restart or desktop run.

All PCs remain pending for method-scoped SourceLanding Mutation: PC-1 remove
the C1374 caller roster entry; PC-2 restore the old conditional helper gate;
PC-3 remove only the caller gate; PC-4 remove both real formatter progress-marker
prefixes; PC-5 add a compiling Check comparison to unallowlisted SessionExitEvidence.
Each has one listed variant. Missing-control discovery also remains pending.
The candidate records exact method filters; Code performed zero mutant cycles.

Raw retained evidence root: `/work/test-evidence/card1544-5fb6b1b2/`.
`base-checkpoint-20261106-141546-1bb4/` and
`main-checkpoint-20261106-135418-c0f0/` are byte-preserving copies. Original
receipt lines retain their original absolute paths. The temporary base checkout
was removed only after verifier death, source cleanliness and matching copied
report/TRX hashes. The base report SHA-256 is
`ed8022133db4ede9d3027cd86635764b4f3e34b92c6057e46a0cb6340abad8ae`;
base CP-1 TRX `b38cda434cfce0f8ad44bba65ccee04c756da20880ccec48becef0df04949ac1`;
base CP-2 TRX `9016b35672b20d0ed4f4f7968e1e4d64c95aa6550a3f2e5b353ac189e993454c`.
Source-snapshot validation passed for all two base rows and all three main rows;
both reports remain RED and are not promoted to qualified green reports.

Essential unedited checkpoint/provenance lines follow.

```text
CHECKPOINT CP-1 commit=4034b60bd99619d9f411bddafc360e13ee867581 build=ok filter=/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-5fb6b1b2/.antiphon/checkpoints/20261106-135418-c0f0/rows/CP-1/run.trx slot=granted waited=90s dirty=0 source=4034b60bd99619d9f411bddafc360e13ee867581 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=90s executed=31.431s wall=121.681s
PHASES CP-1 slotWait=195s build=413.3744237s startup=13.4269714s testsWall=15.1806075s teardown=0.4000455s hostWall=29.007953s
CHECKPOINT CP-2 commit=4034b60bd99619d9f411bddafc360e13ee867581 build=reused filter=/*/*/(WindowsUnitPlatformAdmissionTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(C1729LayeringGuardTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)/* executed=80 passed=80 failed=0 skipped=1 trx=/work/worktrees/task-5fb6b1b2/.antiphon/checkpoints/20261106-135418-c0f0/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=4034b60bd99619d9f411bddafc360e13ee867581 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=15s executed=102.874s wall=117.914s
PHASES CP-2 slotWait=15s build=0s startup=16.773003s testsWall=84.5036444s teardown=0.7352664s hostWall=102.0119137s
CHECKPOINT CP-3 commit=4034b60bd99619d9f411bddafc360e13ee867581 build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=45m slot=granted waited=105s dirty=0 source=4034b60bd99619d9f411bddafc360e13ee867581 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=105s executed=2701.041s wall=2806.102s reason=row-deadline-exceeded
BUDGET wall=3591.834s execution=3276.852s admissionOnly=314.982s
CHECKPOINT CP-1 commit=df1fc145181a3bedf444f9113f822f30d3b136f6 build=ok filter=/*/*/RepairSourceDocumentationTests/C499_V31_DocsAndSkillNameTheRepairContract executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-5fb6b1b2-base/.antiphon/checkpoints/20261106-141546-1bb4/rows/CP-1/run.trx slot=granted waited=705s dirty=0 source=df1fc145181a3bedf444f9113f822f30d3b136f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=705s executed=7.904s wall=713.069s
PHASES CP-1 slotWait=780s build=380.291555s startup=6.6979993s testsWall=0.0536615s teardown=0.4209455s hostWall=7.1727457s
CHECKPOINT CP-2 commit=df1fc145181a3bedf444f9113f822f30d3b136f6 build=reused filter=/*/*/SpecialistRoleContractTests/no_Check_comparison_survives_outside_the_allowlist executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-5fb6b1b2-base/.antiphon/checkpoints/20261106-141546-1bb4/rows/CP-2/run.trx slot=granted waited=660s dirty=0 source=df1fc145181a3bedf444f9113f822f30d3b136f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=660s executed=11.161s wall=671.367s
PHASES CP-2 slotWait=660s build=0s startup=7.6795702s testsWall=2.0429516s teardown=0.6673407s hostWall=10.3898629s
BUDGET wall=1859.120s execution=418.848s admissionOnly=1440.272s

2026-11-06T14:55:33.3656469+00:00 BUILD SLOT granted lease=c7bb634f-b20f-4500-aaa3-503281b01ef9 label="build:bin-c1544-path-guards" waited=0s maxcpucount=6
2026-11-06T15:09:10.4841437+00:00 BUILD SLOT released lease=c7bb634f-b20f-4500-aaa3-503281b01ef9 label="build:bin-c1544-path-guards"
2026-11-06T15:13:04.8446698+00:00 BUILD SLOT granted lease=15df4490-5e08-4b47-a312-aa98fdfbf160 label="CP-4@bin-c1544-path-guards" waited=150s maxcpucount=6
```

CP-4 has no terminal CHECKPOINT receipt; do not fabricate one from partial state. Retained executor/state/build logs are in repair-checkpoint-20261106-145505-3199/. The verifier was stopped to honor the 90-minute task budget; its test lease raced with stop. No trusted class counts are available. Required next Code work is R-3 instrumented performance Debug and missing whole Unit proof, plus R-4 full two-class verification once. No initial Review or landing is authorized by this incomplete gate. All PC-1 through PC-5 remain pending for post-Review SourceLanding Mutation. Native Windows audit remains unperformed. Restart: none.

Cleanup completed after all owned checkpoint executors were observed dead: the temporary base worktree, native bin/obj roots and both isolated build outputs were removed using literal absolute producer paths. Checkpoint evidence remains ignored and retained externally. Final-tip evidence policy, OS inventory, merge-tree and pushed-ref results are recorded in the final response.
