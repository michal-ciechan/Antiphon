# CARD-1943 Code evidence — Final

Implemented the HTTP validation pin, bounded unresolved queue/attention listings,
and explicit attention for an abandoned current Grok rules generation. Portable
ordinary selections passed after replacing the timed-out combined impact row with
bounded whole-class rows. Historical red and refusal records are retained below.

Original Code / landing owner: 73e7dfdd-5bff-4cef-b23f-42dee4f7113c.
Branch: feat/card-task-73e7dfdd. Worktree: /work/worktrees/task-73e7dfdd.
Task base: 64e3d78c487d51570f708492ab67b5db6d0de9a9; this was current origin/master at start.
Main tested sources: ab376ac258b985743ad5dd2ac416be190edac7dd and
58c271b8b07d9e94ce5eb3160fedaa15778ae2b9. Only the plan changed between them;
implementation, client and test source bytes are identical. The exact base check
tested 64e3d78c487d51570f708492ab67b5db6d0de9a9, not the candidate. The final evidence/selection amendment is
published separately; the final caller report names that full pushed SHA.

Plan: docs/superpowers/plans/2026-11-15-card-1943-operator-abandon.md.
Why missed: docs/investigations/2026-11-15-why-missed-card-1943.md.
Unbound PC candidate: docs/superpowers/mutations/2026-11-15-card-1943-operator-abandon-candidate.md.

## Actual V/R outcomes

| ID | Actual outcome |
|---|---|
| V-1 | PASS: two dedicated HTTP cases pin 422, problem media type, validation_failed, errors.reason and the exact 1..1000 validation message. Both already passed before production edits; no artificial red or validator mutant is claimed. |
| V-2 | PASS: independent queue/attention variants traverse all 1003 seeded unresolved Sent rows, including default/max cap, byte budget, deterministic ordering, continuation, no duplicates, and the full attention census. Both failed before the cap existed. Client next/first navigation passed. |
| V-3 | PASS: abandoned rules stay Canceled/null-verdict, keep one charged attempt and one row, send no input, and expose a fresh-start action while that stored generation remains closed. A changed stored generation removes its notice. Existing late-prompt/race assertions remain intact. |
| V-4 | PASS portable: client typecheck/lint, 39 Vitest cases, production bundle, 5 real API contract snapshots, fresh TRX census, manual source checks and OS inventory. Audit details below; final full-range audit follows the evidence commit. |
| R-1 | PASS: whole abandon, queue, standing-switch, attention and eight Grok rules classes. |
| R-2 | PASS portable after CP-11/12: the same 83 impacted cases, 7 + 76, have fresh passing TRX. Contracts and category impact rows also executed. R-2-W1 is pending native Windows, never passed. |
| R-3 | PASS portable: 114 executed guards, no failures. One documented native child-audit skip remains R-3-W1 pending. C1729 inventory: VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3. Derived index changed once and the second regeneration printed unchanged. Nightly pins unchanged. |

Final ordinary rows: CP-2, CP-3, CP-4, CP-6, CP-8, CP-9, CP-10, CP-11, CP-12.
CP-1 and CP-7 are expected pre-fix red proof, CP-5 is historical failed execution
replaced by CP-11/12, and CP-13 is base attribution only. No whole Unit lane,
namespace or full-assembly run was used. No unbounded-class exception was needed.
No ordinary IDs are deferred to another Final round.

Portable subject TRX totals across the completed selections: 568 passed; one
Windows-only NotExecuted result is separate. The base comparison adds 1 passed
and is not candidate qualification. Vitest adds 39 passed separately. Internal
scenario loops, journal outcomes, assertions and elapsed minutes are not TRX counts.

## Failures and their resolution

The initial CP-1 executed 4 cases (2 passed, 2 intended failures). The split red
CP-1 executed 5 cases (2 passed, 3 intended failures): queue returned 1003 rows,
attention returned 1005, and the rules follow-up item was absent. CP-7 produced
2 intended missing-navigation failures with 37 other Vitest cases filtered out.
The new backend cases all passed in CP-2, and both new UI cases passed in CP-8.

The first combined CP-5 reached its unchanged nine-minute deadline without TRX.
Its diagnostic journal has 83 completed cases: 82 passed and one failed. Those
are diagnostics, not native execution counts. C1029_Old_generation_does_not_confirm
was canceled in AgentSessionLaunchQueue.WaitForPendingAsync. The exact method
passed 1/1 at base in CP-13, so no inherited-red attribution is asserted. The
unchanged current whole Codex class passed 7/7 in CP-11, including that method
in 25.4299663 seconds. CP-12 passed the other 76 cases. No timeout, assertion,
retry behavior or production implementation was changed to get those results.
The original failure/timeout is retained and remains relevant to CARD-1906.

An earlier CP-1 build was refused before compilation because npm ci created two
hardlinks to its esbuild binary. After that run ended, the task-owned ignored
link was replaced with identical bytes and mode in a new inode; both link counts
were 1. The refused build executed zero tests and is not defect proof. The
replacement red run succeeded in compiling and produced the intended failures.
One initial launch also refused an incorrectly typed expected SHA before work;
it executed no build or test. It is not credited.

## Source qualification and cost

All intended class/method results were inspected in fresh TRX, with nonzero
counts. The ignored census includes per-case outcomes/durations and SHA256:
/work/worktrees/task-73e7dfdd/.antiphon/c1943-case-census.json.

validate-checkpoint-receipt.ps1 passed the replacement four-row report at
58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 and the base one-row report at
64e3d78c487d51570f708492ab67b5db6d0de9a9; the contract one-row report is validated at the same subject SHA.
The historical grouped report remains RED because CP-5 timed out. It is not
relabeled as a fully qualified green receipt. Its successful native row outcomes,
clean source observations and verified build provenance are preserved verbatim.
No reviewedSourceClean assertion or land approval is made by this Code report.

Every selected build and row held a granted host slot. Row waited=0s throughout;
the contract build waited 15 seconds (PHASES CP-10 slotWait=15s). Its owner reads
also temporarily timed out, recovered before refusal, and were never bypassed.
Peak observed /proc/loadavg first value was 51.05 on the 24-core host; initial
load was 24.24. Runs were serial. The largest complete ordinary server run was
27m54s; the replacement run was 20m44s. Contract wall time includes owner reads,
source/output bookkeeping and cleanup; test execution was 105.544 seconds and
its isolated build phase 275.9626236 seconds. No selection exceeded 30 minutes.
Aggregate parallel per-class spans are not row wall time. Missing external-call
or fixture subphases remain unknown, not zero. No loaded repetitions and no
reassurance repeats after green were run.

Explicit unlisted prerequisites (all through build-slot, native default output):
checkpoint driver bootstrap (0 errors, 7.37s, slot granted/waited 0s), derived-index
driver bootstrap (0 errors, 4.09s, slot granted/waited 0s), then two separately
leased --no-build index regeneration commands (changed, unchanged; 30s and 28s
lease holds). The index is its own commit ab376ac258b985743ad5dd2ac416be190edac7dd.
These are prerequisite tooling, not extra test selections.

## Pending native and Mutation obligations

- R-2-W1: AgentTaskLandDeliveryE2ETests/C488_ReviewDeliveryCrashMatrix_Batch4,
  windows-only matching impacted row; not run on Linux, pending Windows placement.
- R-3-W1: WindowsUnitPlatformAdmissionTests/C1210_Windows_linux_only_outcomes_are_exact,
  native child skip-roster audit; one documented Linux skip, pending Windows placement.
- PC-1: field name, message, over-limit acceptance — pending SourceLanding Mutation.
- PC-2: row cap, byte cap, cursor, truncation, ordering — all pending.
- PC-3: cap, offset, truncation, ordering, full census — all pending.
- PC-4: absent action, stale-generation notice, resend/replacement, false delivery
  verdict — all pending. Mutation also owns missing-control discovery.

The operator waived Code mutation checks (2026-11-12). No deliberate mutant was
applied, no red/restore/green cycle or PC-clean verdict is claimed, and no parent
CARD-1892 control obligation is discharged. Caller commissions SourceLanding Mutation.

## Manual acceptance and audits

Inspected the shared DTO builder: reads, mutation replies and queue-change
notifications all use bounded snapshots. Queue/attention retain their envelopes,
50/100 row limits and explicit continuation/truncation; both panels can reach
later rows and return to the first page. Attention counts retain undisplayed rows.
The new projection sends no input. Existing abandon locking, permanent audit,
null verdict, transcript UserPrompt authority and composer-less no-retype paths
are untouched and their named regression assertions passed. The canceled rules
read stays terminal; fresh launch/new generation is the explicit operator recovery,
and the projection is tied to the generation stored on that session.
No live-host acceptance, restart, deployment or Windows qualification action occurred.

No migrations: zero changed migration paths, zero downgrade/model selections;
migration red/green counts are 0/0, not an executed migration verdict.

OS witness audit at ab376ac258b985743ad5dd2ac416be190edac7dd passed: 14 changed
non-document paths inventoried, no OS candidates/witnesses or changed startup
paths. The final audit updates only the pinned head. Runtime activation needs a
server restart after publication; runner restart is none. None was performed.

Nightly policyHash b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d,
PinnedInputDigest 26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e,
LegacyExclusionDigest 6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a.
Protected inputs were not edited; no re-pin was needed.

At tested tip 58c271b8b07d9e94ce5eb3160fedaa15778ae2b9, evidence-diff over the
full base range passed: commits=8 entries=0 violations=0. Fetched master was
4ce55f36794f3e2fa904d56ba980f4182d89ab3a; merge-tree exited 0 with tree
f7bc4cfdc89122b1a69841b1a4883cf887d14370. The full-range audit and merge-tree
are repeated after this evidence-only commit; their final values are in the caller report.
The owned detached base worktree /tmp/card1943-base-c1029 is retained with its
native evidence. Its build output was reclaimed by the checkpoint owner; no run
is left in flight.

## Exact checkpoint history

### initial red

Source receipt: `/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-044929-634c/report.json`. Unedited tool lines:

```text
CHECKPOINT CP-1 commit=885eb828ef5cd77870e38573441fa8d5ffaa4714 build=ok filter=/*/*/SessionQueueAbandonTests/(Abandon_reason_problem_details*)|(Current_unconfirmed_listings*)|(Abandoned_current_rules*) executed=4 passed=2 failed=2 skipped=0 trx=/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-044929-634c/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=885eb828ef5cd77870e38573441fa8d5ffaa4714 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=65.827s wall=65.864s
PHASES CP-1 slotWait=0s build=247.2096858s startup=7.3373374s testsWall=29.2321791s teardown=28.4036792s hostWall=64.9734942s
BUDGET wall=336.858s execution=336.857s admissionOnly=0.001s
```

### client red and refused build

Source receipt: `/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-045632-3013/report.json`. Unedited tool lines:

```text
CHECKPOINT CP-7 commit=c495f1212b283ecec666183b5627476fbcc4623c build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 SessionMessageQueue AttentionPanel -t pages executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=c495f1212b283ecec666183b5627476fbcc4623c sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-7 waited=0s executed=180.368s wall=180.439s
CHECKPOINT CP-1 commit=c495f1212b283ecec666183b5627476fbcc4623c build=failed filter=/*/*/SessionQueueAbandonTests/(Abandon_reason_problem_details*)|(Current_unconfirmed_listings*)|(Abandoned_current_rules*) executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: unreadable dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-1 waited=0s executed=0.000s wall=0.000s
BUDGET wall=235.090s execution=235.085s admissionOnly=0.005s
```

### split backend red

Source receipt: `/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-050112-ae84/report.json`. Unedited tool lines:

```text
CHECKPOINT CP-1 commit=9bde41c68a9b25dd77cc5cf422893dcb830af864 build=ok filter=/*/*/SessionQueueAbandonTests/(Abandon_reason_problem_details*)|(Current_unconfirmed_listings*)|(Abandoned_current_rules*) executed=5 passed=2 failed=3 skipped=0 trx=/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-050112-ae84/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=9bde41c68a9b25dd77cc5cf422893dcb830af864 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=71.886s wall=71.998s
PHASES CP-1 slotWait=0s build=425.2518136s startup=9.7548364s testsWall=31.4312565s teardown=29.7991155s hostWall=70.9854474s
BUDGET wall=550.562s execution=550.562s admissionOnly=0.001s
```

### first ordinary group

Source receipt: `/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-051639-b45a/report.json`. Unedited tool lines:

```text
CHECKPOINT CP-2 commit=ab376ac258b985743ad5dd2ac416be190edac7dd build=ok filter=/*/*/(SessionQueueAbandonTests*)|(SessionMessageQueueServiceTests*)|(StandingSessionSwitchConcurrencyTests*)|(StandingSessionQueueSwitchTests*)/* executed=88 passed=88 failed=0 skipped=0 trx=/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-051639-b45a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=ab376ac258b985743ad5dd2ac416be190edac7dd sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=184.973s wall=185.085s
PHASES CP-2 slotWait=0s build=276.1706536s startup=104.6253187s testsWall=73.9129672s teardown=5.5493706s hostWall=184.0878994s
CHECKPOINT CP-3 commit=ab376ac258b985743ad5dd2ac416be190edac7dd build=reused filter=/*/*/(AttentionServiceTests*)|(AttentionFeedNoiseTests*)/* executed=182 passed=182 failed=0 skipped=0 trx=/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-051639-b45a/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=ab376ac258b985743ad5dd2ac416be190edac7dd sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=110.479s wall=110.541s
PHASES CP-3 slotWait=0s build=0s startup=24.3749058s testsWall=67.610306s teardown=17.4501366s hostWall=109.4353486s
CHECKPOINT CP-4 commit=ab376ac258b985743ad5dd2ac416be190edac7dd build=reused filter=/*/*/(GrokRulesInitializationTests*)|(GrokRulesFailureTests*)|(GrokRulesCompactionRecoveryTests*)|(GrokRulesReadyOrderingTests*)|(GrokRulesQueueBarrierTests*)|(GrokRulesReceiptTests*)|(GrokRulesTransactionTests*)|(GrokRulesCompactionTests*)/* executed=96 passed=96 failed=0 skipped=0 trx=/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-051639-b45a/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=ab376ac258b985743ad5dd2ac416be190edac7dd sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=130.572s wall=130.628s
PHASES CP-4 slotWait=0s build=0s startup=23.7613882s testsWall=51.3758124s teardown=54.5296293s hostWall=129.6668304s
CHECKPOINT CP-5 commit=ab376ac258b985743ad5dd2ac416be190edac7dd build=reused filter=/*/*/(AgentTaskMidTurnRefineTests*)|(CodexCliObservationGapTests*)|(LandRefusalDeliveryWitnessTests*)|(PhoneHomeOutageMentionTests*)|(SessionMessageQueuePhoneHomeDropTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=9m slot=granted waited=0s dirty=0 source=ab376ac258b985743ad5dd2ac416be190edac7dd sourceState=clean buildSource=verified
JOURNAL-DIAGNOSTIC CP-5 completed=83 pending=0 outcome-passed=82 outcome-failed=1 outcome-skipped=0 in-flight=none
CHECKPOINT-TIMING CP-5 waited=0s executed=541.004s wall=541.059s reason=row-deadline-exceeded
CHECKPOINT CP-6 commit=ab376ac258b985743ad5dd2ac416be190edac7dd build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/* executed=114 passed=114 failed=0 skipped=1 trx=/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-051639-b45a/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=ab376ac258b985743ad5dd2ac416be190edac7dd sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=325.714s wall=345.551s
PHASES CP-6 slotWait=0s build=0s startup=30.0468016s testsWall=293.8370999s teardown=0.9676971s hostWall=324.8515993s
BUDGET wall=1674.437s execution=1674.432s admissionOnly=0.005s
```

### exact base comparison

Source receipt: `/tmp/card1943-base-c1029/.antiphon/checkpoints/20261115-054636-28d4/report.json`. Unedited tool lines:

```text
CHECKPOINT CP-13 commit=64e3d78c487d51570f708492ab67b5db6d0de9a9 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Old_generation_does_not_confirm executed=1 passed=1 failed=0 skipped=0 trx=/tmp/card1943-base-c1029/.antiphon/checkpoints/20261115-054636-28d4/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=64e3d78c487d51570f708492ab67b5db6d0de9a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-13 waited=0s executed=125.081s wall=125.249s
PHASES CP-13 slotWait=0s build=336.4015908s startup=51.2826863s testsWall=52.3408409s teardown=20.3139893s hostWall=123.9375912s
BUDGET wall=573.696s execution=573.695s admissionOnly=0.001s
```

### replacement impact and client

Source receipt: `/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-055658-bda0/report.json`. Unedited tool lines:

```text
CHECKPOINT CP-8 commit=58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 -JsonResultPath .antiphon/c1943-client.json SessionMessageQueue AttentionPanel executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-8 waited=0s executed=67.205s wall=67.392s
CHECKPOINT CP-9 commit=58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 build=n/a filter=npm --prefix client run build executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-9 waited=0s executed=20.391s wall=20.494s
CHECKPOINT CP-11 commit=58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 build=ok filter=/*/*/CodexCliObservationGapTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-055658-bda0/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-11 waited=0s executed=375.823s wall=375.860s
PHASES CP-11 slotWait=0s build=469.3013986s startup=28.3600442s testsWall=328.9942783s teardown=17.6823909s hostWall=375.0370163s
CHECKPOINT CP-12 commit=58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 build=reused filter=/*/*/(AgentTaskMidTurnRefineTests*)|(LandRefusalDeliveryWitnessTests*)|(PhoneHomeOutageMentionTests*)|(SessionMessageQueuePhoneHomeDropTests*)/* executed=76 passed=76 failed=0 skipped=0 trx=/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-055658-bda0/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-12 waited=0s executed=153.738s wall=153.786s
PHASES CP-12 slotWait=0s build=0s startup=30.9428625s testsWall=86.4743053s teardown=35.668262s hostWall=153.0854296s
BUDGET wall=1244.042s execution=1243.957s admissionOnly=0.085s
```

### contracts

Source receipt: `/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-062037-1db1/report.json`. Unedited tool lines:

```text
CHECKPOINT CP-10 commit=58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 build=ok filter=/*/*/ContractSnapshotTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-73e7dfdd/.antiphon/checkpoints/20261115-062037-1db1/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=58c271b8b07d9e94ce5eb3160fedaa15778ae2b9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=0s executed=105.544s wall=105.580s
PHASES CP-10 slotWait=15s build=275.9626236s startup=1.8486857s testsWall=100.9459473s teardown=1.6794268s hostWall=104.4740674s
BUDGET wall=634.643s execution=619.887s admissionOnly=14.752s
```

## Fresh class census

- CP-2 at ab376ac258b985743ad5dd2ac416be190edac7dd: SessionQueueAbandonTests 22 Passed; SessionMessageQueueServiceTests 26 Passed; StandingSessionQueueSwitchTests 9 Passed; StandingSessionSwitchConcurrencyTests 31 Passed.
- CP-3 at ab376ac258b985743ad5dd2ac416be190edac7dd: AttentionServiceTests 168 Passed; AttentionFeedNoiseTests 14 Passed.
- CP-4 at ab376ac258b985743ad5dd2ac416be190edac7dd: GrokRulesInitializationTests 24 Passed; GrokRulesFailureTests 7 Passed; GrokRulesTransactionTests 3 Passed; GrokRulesCompactionTests 2 Passed; GrokRulesQueueBarrierTests 28 Passed; GrokRulesReadyOrderingTests 5 Passed; GrokRulesReceiptTests 16 Passed; GrokRulesCompactionRecoveryTests 11 Passed.
- CP-6 at ab376ac258b985743ad5dd2ac416be190edac7dd: MechanicalPcContractTests 11 Passed; C1729LayeringGuardTests 11 Passed; TaskPlatformGuidanceTests 5 Passed; WindowsUnitPlatformAdmissionTests 6 Passed/1 NotExecuted; SlowTestTripwireTests 2 Passed; NativeLinuxPolicyWitnessTests 3 Passed; TestLaneCategoryGuardTests 1 Passed; TestClassificationGuardTests 1 Passed; DerivedImpactIndexTests 27 Passed; SlowTestRegistryMergeTests 5 Passed; TestClassificationPolicyTests 42 Passed.
- CP-11 at 58c271b8b07d9e94ce5eb3160fedaa15778ae2b9: CodexCliObservationGapTests 7 Passed.
- CP-12 at 58c271b8b07d9e94ce5eb3160fedaa15778ae2b9: AgentTaskMidTurnRefineTests 39 Passed; PhoneHomeOutageMentionTests 8 Passed; LandRefusalDeliveryWitnessTests 24 Passed; SessionMessageQueuePhoneHomeDropTests 5 Passed.
- CP-10 at 58c271b8b07d9e94ce5eb3160fedaa15778ae2b9: ContractSnapshotTests 5 Passed.
