# CARD-1574 Code runtime checkpoint: coherent S2a, overall S2 incomplete

Runtime receipt-readiness and persistence fencing are implemented and narrowly verified. Durable resume-note production, queue input/retry/recovery fences and V-4..V-7 remain unimplemented. This API alone does not fix the running incident. The brief explicitly permits a coherent safe slice under its 60-minute budget.

Original Code/landing owner: 7c1cf5bb-2f0a-4bc7-983f-d2bab596da72. Continuation: 109f308b. Branch: feat/card-task-109f308b. Worktree: /work/worktrees/task-109f308b. Task base: 1cacb12dc6cde604128a12871cb574c20dbc7460. Original card Code base: fda635c6641fccc84c97dd299aeae205f14eec88. Actual tested source: 0166b5f488fcba7a73e98cd0cbc630ecba57f508. A report-only commit follows it; do not relabel receipts with that later SHA. Restart target: none.

Plan: docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md. Pending controls: docs/superpowers/mutations/2026-10-25-card-1574-runtime-candidates.md. Prior S1 evidence: docs/investigations/2026-10-25-card-1574-code-reader-checkpoint.md.

## Delivered

AgentSessionRuntime.ReceiptReadiness.cs adds a five-second linked-budget observation, separate from CatchUpTranscriptAsync's new-row boolean. It accepts same-session/accepted-generation, structurally ordered snapshots; captures actual standing ownership/current pointer and route/store; releases the state lease around HTTP; rejects concurrent store revisions; rechecks ownership under the SQL session row lock before committing; holds the lease through persistence/publication; and rechecks ownership after asynchronous work. Ready returns the stored sequence baseline, including zero and UUID-rebased history. Behind/legacy snapshots can ingest positive evidence without qualifying absence. Persistence faults and NeedsReload fail closed. Caller cancellation propagates while observation timeout returns Unknown. No queue or launch behavior is activated by this slice.

The shared ingestion core receives an optional receipt fence; its existing local Working-recovery policy is preserved. BridgeQueueHarness gains only opt-in transcript scheduling/fault control; legacy fakes remain Unknown. V-3 uses real HTTP deserialization, phone-home mapper, runtime ingestion, isolated PostgreSQL, held commit and owner/route races. The plan adds CP-3a/CP-4a plus one exact adjacent CP-4b sharing the green build. Session runtime invariants describe the partial contract.

## Ordinary outcomes

The operator brief overrides generic Final Unit/full integration rows: one red witness and narrow green per behavior; no Unit lane or broad sweep. This is not Interim and not whole-card Final/Full.

| ID | Actual outcome |
|---|---|
| V-1 | Historical S1 pass at 420c32d40f92814f633afab5ef847f2647885c8c; not rerun here |
| V-2 | Historical S1 pass at that SHA, including truncation/regrowth and invalid UTF-8; not rerun here |
| V-3 | Passed at 0166b5f488fcba7a73e98cd0cbc630ecba57f508; exact method, one execution, 11.2460500s test body; independent clean source/build certificate |
| V-4 | Not implemented/run: real resumed launch and durable note |
| V-5 | Not implemented/run: accepted note beyond grace and all send fences |
| V-6 | Not implemented/run: original durable row across acknowledgement/claim/acceptance reconstruction |
| V-7 | Not implemented/run: genuine absence and Working/non-AlwaysOn/generation/final-receipt matrix |
| R-1 | Historical S1 reader pass only |
| R-2 | V-3 and exact existing C1205 generation-race smoke passed and certified at 0166b5f488fcba7a73e98cd0cbc630ecba57f508 |
| R-3 | Not run; V-4 remains |
| R-4 | Not run; V-5/V-6 remain |
| R-5 | Not run; V-7 and CP-5 remain |

Original CP-3/CP-4/CP-5 remain pending, as do V-4,V-5,V-6,V-7,R-3,R-4,R-5 in the remaining Final work. Unit/broad integration were explicitly excluded by the brief, not passed. No native provider, deployed activation, rollout or rc roster qualification was performed. Both matching product components still need caller-owned activation qualification after the complete fix.

## Checkpoint history and provenance

Every actual run launcher supplied committed HEAD as expected SHA. Fresh TRX rosters were inspected for each actual method and nonzero execution. Final CP-4a and CP-4b source validation returns CHECKPOINT SOURCE VALID for 0166b5f488fcba7a73e98cd0cbc630ecba57f508; dirty=0 and buildSource=verified. Historical reds and compile/refusal costs below remain diagnostic, never green certificates.

The first witness predates the expanded commit-race/rebased-sequence proof. That expanded matrix has three actual executions: two authored fixture errors and its green repair. Compile/preflight failures executed zero tests. No loaded proof or deliberate mutant ran, no qualified green V-3 repeat ran, and the adjacent extra execution was required because its earlier raw pass had an invalid cleanup certificate. No timeout or assertion was loosened.


### 20261025-003722-4974

Expected red: Unknown-only scaffold fails the Ready assertion; one execution, one assertion failure.

Evidence: .antiphon/checkpoints/20261025-003722-4974/report.json

```text
commit: 3b60c1c1f5a5100d9f02b6a032d48628dc824737  branch: feat/card-task-109f308b  worktree: /work/worktrees/task-109f308b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 3b60c1c1f5a5100d9f02b6a032d48628dc824737 state=clean buildSource=verified
CHECKPOINT CP-3a commit=3b60c1c1f5a5100d9f02b6a032d48628dc824737 build=ok filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Transport_and_persistence_fence_readiness executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-109f308b/.antiphon/checkpoints/20261025-003722-4974/rows/CP-3a/run.trx slot=granted waited=0s dirty=0 source=3b60c1c1f5a5100d9f02b6a032d48628dc824737 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3a waited=0s executed=83.478s wall=83.543s
PHASES CP-3a slotWait=0s build=245.0544921s startup=69.5802587s testsWall=11.5853025s teardown=1.38698s hostWall=82.5525675s
BUDGET wall=345.106s execution=345.106s admissionOnly=0.001s
verdict: RED exit=1
```

### 20261025-004514-2f92

Introduced compile error: missing SessionGeneration namespace. Zero tests; no green qualification.

Evidence: .antiphon/checkpoints/20261025-004514-2f92/report.json

```text
commit: ed0823d04ae978f26a93a2e5afe71f1d59ef4317  branch: feat/card-task-109f308b  worktree: /work/worktrees/task-109f308b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: ed0823d04ae978f26a93a2e5afe71f1d59ef4317 state=clean buildSource=unknown
CHECKPOINT CP-4a commit=ed0823d04ae978f26a93a2e5afe71f1d59ef4317 build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Transport_and_persistence_fence_readiness executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=ed0823d04ae978f26a93a2e5afe71f1d59ef4317 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-4a waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-4b commit=ed0823d04ae978f26a93a2e5afe71f1d59ef4317 build=failed filter=/*/*/WorkingTranscriptRecoveryTests/C1205_Generation_changed_after_validation_must_not_append_old_end executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=ed0823d04ae978f26a93a2e5afe71f1d59ef4317 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-4b waited=0s executed=0.000s wall=0.000s
BUDGET wall=129.860s execution=129.860s admissionOnly=0.000s
verdict: RED exit=2
```

### 20261025-004857-ce8e

Pre-existing output refusal after incomplete manual cleanup. Zero tests; not a proof.

Evidence: .antiphon/checkpoints/20261025-004857-ce8e/report.json

```text
commit: fac4d9851ae71ca682247876d7500008338d5f49  branch: feat/card-task-109f308b  worktree: /work/worktrees/task-109f308b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: fac4d9851ae71ca682247876d7500008338d5f49 state=clean buildSource=unknown
CHECKPOINT CP-4a commit=fac4d9851ae71ca682247876d7500008338d5f49 build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Transport_and_persistence_fence_readiness executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-output dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-4a waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-4b commit=fac4d9851ae71ca682247876d7500008338d5f49 build=failed filter=/*/*/WorkingTranscriptRecoveryTests/C1205_Generation_changed_after_validation_must_not_append_old_end executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-output dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-4b waited=0s executed=0.000s wall=0.000s
BUDGET wall=15.011s execution=15.011s admissionOnly=0.000s
verdict: RED exit=2
```

### 20261025-004940-9b75

Introduced compile repair chose wrong namespace. Zero tests; corrected to runner contracts.

Evidence: .antiphon/checkpoints/20261025-004940-9b75/report.json

```text
commit: fac4d9851ae71ca682247876d7500008338d5f49  branch: feat/card-task-109f308b  worktree: /work/worktrees/task-109f308b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: fac4d9851ae71ca682247876d7500008338d5f49 state=clean buildSource=unknown
CHECKPOINT CP-4a commit=fac4d9851ae71ca682247876d7500008338d5f49 build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Transport_and_persistence_fence_readiness executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=fac4d9851ae71ca682247876d7500008338d5f49 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-4a waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-4b commit=fac4d9851ae71ca682247876d7500008338d5f49 build=failed filter=/*/*/WorkingTranscriptRecoveryTests/C1205_Generation_changed_after_validation_must_not_append_old_end executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=fac4d9851ae71ca682247876d7500008338d5f49 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-4b waited=0s executed=0.000s wall=0.000s
BUDGET wall=104.959s execution=104.959s admissionOnly=0.000s
verdict: RED exit=2
```

### 20261025-005211-77d3

V-3 fixture error: partial runner binding violates existing DB constraint. Adjacent raw TRX passed, but receipt validation later refused cleanup_refused; superseded by the clean exact selection below.

Evidence: .antiphon/checkpoints/20261025-005211-77d3/report.json

```text
commit: c091c1e8a781f5b215d9be43a25a3a63407ab878  branch: feat/card-task-109f308b  worktree: /work/worktrees/task-109f308b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: c091c1e8a781f5b215d9be43a25a3a63407ab878 state=clean buildSource=verified
CHECKPOINT CP-4a commit=c091c1e8a781f5b215d9be43a25a3a63407ab878 build=ok filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Transport_and_persistence_fence_readiness executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-109f308b/.antiphon/checkpoints/20261025-005211-77d3/rows/CP-4a/run.trx slot=granted waited=0s dirty=0 source=c091c1e8a781f5b215d9be43a25a3a63407ab878 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4a waited=0s executed=80.593s wall=80.748s
PHASES CP-4a slotWait=0s build=210.5049324s startup=59.5699657s testsWall=14.4432408s teardown=3.564954s hostWall=77.5780887s
CHECKPOINT CP-4b commit=c091c1e8a781f5b215d9be43a25a3a63407ab878 build=reused filter=/*/*/WorkingTranscriptRecoveryTests/C1205_Generation_changed_after_validation_must_not_append_old_end executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-109f308b/.antiphon/checkpoints/20261025-005211-77d3/rows/CP-4b/run.trx slot=granted waited=0s dirty=0 source=c091c1e8a781f5b215d9be43a25a3a63407ab878 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4b waited=0s executed=43.110s wall=43.517s
PHASES CP-4b slotWait=0s build=0s startup=27.3962325s testsWall=11.8569236s teardown=2.5530629s hostWall=41.8062189s
BUDGET wall=358.491s execution=358.490s admissionOnly=0.001s
verdict: RED exit=1
```

### 20261025-005903-ca84

V-3 fixture error in commit-race finally: null store restored beside remote route/cwd. One failed execution; fixed without loosening schema/assertions/timeouts.

Evidence: .antiphon/checkpoints/20261025-005903-ca84/report.json

```text
commit: f30accf2178662fb99bb8172d3540fe836b3614d  branch: feat/card-task-109f308b  worktree: /work/worktrees/task-109f308b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: f30accf2178662fb99bb8172d3540fe836b3614d state=clean buildSource=verified
CHECKPOINT CP-4a commit=f30accf2178662fb99bb8172d3540fe836b3614d build=ok filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Transport_and_persistence_fence_readiness executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-109f308b/.antiphon/checkpoints/20261025-005903-ca84/rows/CP-4a/run.trx slot=granted waited=90s dirty=0 source=f30accf2178662fb99bb8172d3540fe836b3614d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4a waited=90s executed=57.620s wall=147.695s
PHASES CP-4a slotWait=210s build=241.7177438s startup=48.2370137s testsWall=5.6852209s teardown=1.582878s hostWall=55.5051041s
BUDGET wall=547.010s execution=337.079s admissionOnly=209.931s
verdict: RED exit=1
```

### 20261025-011023-832e

V-3 green: one passed, zero failed/skipped. Source receipt independently VALID.

Evidence: .antiphon/checkpoints/20261025-011023-832e/report.json

```text
commit: 0166b5f488fcba7a73e98cd0cbc630ecba57f508  branch: feat/card-task-109f308b  worktree: /work/worktrees/task-109f308b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 0166b5f488fcba7a73e98cd0cbc630ecba57f508 state=clean buildSource=verified
CHECKPOINT CP-4a commit=0166b5f488fcba7a73e98cd0cbc630ecba57f508 build=ok filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Transport_and_persistence_fence_readiness executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-109f308b/.antiphon/checkpoints/20261025-011023-832e/rows/CP-4a/run.trx slot=granted waited=120s dirty=0 source=0166b5f488fcba7a73e98cd0cbc630ecba57f508 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4a waited=120s executed=63.251s wall=183.551s
PHASES CP-4a slotWait=150s build=215.1984257s startup=48.9829507s testsWall=11.24605s teardown=1.9331036s hostWall=62.1621243s
BUDGET wall=532.031s execution=382.019s admissionOnly=150.013s
verdict: GREEN exit=0
```

### 20261025-012002-b363

Exact adjacent recovery smoke green: one passed, zero failed/skipped. Source receipt independently VALID. Required fresh certification after prior cleanup-refused receipt; no V-3 repeat.

Evidence: .antiphon/checkpoints/20261025-012002-b363/report.json

```text
commit: 0166b5f488fcba7a73e98cd0cbc630ecba57f508  branch: feat/card-task-109f308b  worktree: /work/worktrees/task-109f308b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 0166b5f488fcba7a73e98cd0cbc630ecba57f508 state=clean buildSource=verified
CHECKPOINT CP-4b commit=0166b5f488fcba7a73e98cd0cbc630ecba57f508 build=ok filter=/*/*/WorkingTranscriptRecoveryTests/C1205_Generation_changed_after_validation_must_not_append_old_end executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-109f308b/.antiphon/checkpoints/20261025-012002-b363/rows/CP-4b/run.trx slot=granted waited=0s dirty=0 source=0166b5f488fcba7a73e98cd0cbc630ecba57f508 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4b waited=0s executed=24.256s wall=24.323s
PHASES CP-4b slotWait=0s build=323.5438927s startup=16.0396561s testsWall=6.3453623s teardown=0.9768611s hostWall=23.3618851s
BUDGET wall=421.522s execution=421.521s admissionOnly=0.001s
verdict: GREEN exit=0
```

## Cost, bounds and omissions

Only unlisted build: checkpoint CLI bootstrap, required to use the checkpoint tool, through scripts/build-slot.ps1 with OutputPath=bin-c1574-cli/ and UseAppHost=false. Build succeeded: zero errors, five warnings, 6.64s MSBuild; slot=granted waited=0s held=7s. DLL invocations then compiled nothing themselves. One manifest-reference preflight refusal and a mistaken wait-source argument started no test/build and provide no qualification. No unlisted test or full assembly ran. Exact adjacent selection was added because the SQL fence core is shared with Working recovery.

Host: Debian Linux x64, 24 advertised cores; no Runner/Platform pin. GET /api/runner-defaults (revision 2) and GET /api/session-runners succeeded. Build/test drivers used the broker gate; no unleased retry or bypass. Per-run PHASES/BUDGET retain admission separately from execution. No individual run exceeded 30 minutes. Eight-minute importer overrides are provisional plan bounds, not a measured p95; valid test-body samples are singular. Shared DB setup is once per test host, schemas isolated; no provider process/web host/AppHost is started. Per-call DB/HTTP/IO attribution remains incomplete (including TUnit external-span-cap warnings), not zero; CARD-1514/CARD-1404 own instrumentation/calibration. Generated receipts remain ignored.

## Pending Mutation

Every PC-1 through PC-28 and every variant is Pending, explicitly tabulated in docs/superpowers/mutations/2026-10-25-card-1574-runtime-candidates.md. PC-8..PC-13 have runtime candidates; PC-14..PC-25 and launcher containment in PC-26 remain unbound. Reader sites are inherited. Collision-only NeedsReload isolation, actual standing owner/current pointer and store revision/reset/structural variants remain discovery candidates. No control is discharged by ordinary red/green.

## Exact continuation

Review this coherent S2a slice and its scope limitations first, per brief. Then commission remaining Code: create one immutable standing-Claude resume-note key before input without holding boot; reconcile attempted notes before Working; gate flush/automated Now/SendNow and interrupted/re-Enter recovery; preserve attempt metadata under Behind/Unknown; require complete correlated UserPrompt above the stored attempt floor; retain caught-up genuine-failure recovery and old Now grace. Author V-4..V-7 using real launch/queue/ingestion/DB transitions, amend remaining CP-3/CP-4 selection expectations for already-green V-3, then execute their red/green witnesses and CP-5. Do not land this as the complete incident fix. All PCs stay for SourceLanding Mutation after ordinary Review/land.

## Publication and commit inventory

All implementation/fixture slices were committed and pushed on the assigned fast-forward branch; no rebase, target merge, branch switch, land or deployment. Original owner remains 7c1cf5bb-2f0a-4bc7-983f-d2bab596da72; caller must preserve it when adopting this continuation's reviewed source. Caller also needs the complete reviewed list on rc/2026-10-24; this task did not write rc. Fetched-master merge-tree was clean at tested source (master d35b37a01c4f1f3027ee53cc3f2b7d1106fcc322, tree b5aaf9c00b55111f26344e76cc24b177509104d8). Final publication/audit/cleanup receipts and the report commit SHA are appended to .antiphon/task-109f308b.md and the final response.

Full inherited-plus-continuation list through the tested source, in cherry-pick order:

```text
c7dc923efee268f06ea2b7dae779a7c237819bc5 test(CARD-1574): witness missing live transcript readiness
7ce076ab619dbd5f2f57af9af4822e3aa8e990d0 feat(CARD-1574): publish current-file transcript ingestion readiness
8d2a4d5a7c3b31544db5ea1a30c0a5c6287190e7 test(CARD-1574): give fork fixture qualifying ownership evidence
c489fc8e79ce7e0ea07b9f8c1512d65a741e9904 test(CARD-1574): expose regrowth and malformed-byte readiness holes
420c32d40f92814f633afab5ef847f2647885c8c fix(CARD-1574): retain invalidation after truncation and malformed bytes
1cacb12dc6cde604128a12871cb574c20dbc7460 docs(CARD-1574): preserve partial reader evidence and remaining Code scope
3b60c1c1f5a5100d9f02b6a032d48628dc824737 test(CARD-1574): witness committed same-launch receipt readiness
29cb21404e58d9ad00fa82ef0b928ed183e462e7 feat(CARD-1574): qualify receipt readiness after same-launch persistence
ed0823d04ae978f26a93a2e5afe71f1d59ef4317 docs(CARD-1574): use importer-supported shared checkpoint build
fac4d9851ae71ca682247876d7500008338d5f49 fix(CARD-1574): import generation comparison for readiness observer
c091c1e8a781f5b215d9be43a25a3a63407ab878 fix(CARD-1574): resolve generation helper from runner contracts
f30accf2178662fb99bb8172d3540fe836b3614d test(CARD-1574): keep route-race fixture bindings structurally valid
0166b5f488fcba7a73e98cd0cbc630ecba57f508 test(CARD-1574): preserve remote store during commit-race restoration
```
