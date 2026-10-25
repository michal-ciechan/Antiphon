# CARD-1574 queue-fence continuation checkpoint

Queue ownership and readiness fences are implemented and narrowly verified. Whole-plan V-6/V-7 matrix qualification is still incomplete; this is a coherent safe slice under the brief's 70-minute budget, not whole-card Final/Full or deployed activation.

Task: ad9a35c6-092e-4b41-ad6e-fe9156cb7af3 (Debug). Branch: feat/card-task-ad9a35c6. Base: af81145b8fdd157445562314955c4cbe976e580e. Original Code owner: 7c1cf5bb-2f0a-4bc7-983f-d2bab596da72; prior continuation: 109f308b. Preserve the existing operation ownership when adopting these commits.

## Delivered behavior

A standing Claude resume with a nonempty note creates one immutable System WhenIdle queue row keyed by normalized accepted launch ticks. It is committed without inline delivery; acknowledgement re-entry uses the same key. The existing final launch flush is contained for this durable obligation, and the obligation suppresses the unrelated boot probe. Fresh launches keep their direct bootstrap.

The shared queue fence checks the current launch, actual standing owner and current agent pointer through the inherited runtime observation API. It persists a fresh snapshot and reconciles positive complete UserPrompt evidence before Working, input selection, interrupted recovery or further input. Behind, Unknown, legacy-null and observation-query faults defer. Automated Now, durable channel Now, SendNow, ordinary/stranded/action flush, interrupted recovery, confirm-loop Enter, overlay dismissal, local-command opening Esc and watchdog direct sends use the fence. Explicit terminal input keeps its existing contract.

The note's receipt contract is passed from the keyed row, not inferred from text. Only complete UserPrompt evidence above the original stored floor qualifies; zero is observable after successful empty-history admission. Generation checks exclude old attempts, and old launch keys are not retyped into a new launch. An ordinary send repeating a settled note's text retains its original confirmation setting.

After normal confirmation and grace, non-Ready evidence retains the same Pending obligation, count, original attempt time, generation and floor, with the observed absence verdict and a null adjudication timestamp. It does not refund, retype, record a delivery-failure incident or authorize recovery kill. A later pull can late-confirm even while Behind or legacy-unknown. Ready absence retains the existing Working, AlwaysOn and generation-conditional recovery rules. SendNow returns a retained queue for uncertain delivery; automatic action delivery reports a temporary block so the existing action retry remains owed. Interrupted note discovery has no ordinary retry-window expiry and includes capped rows for receipt reconciliation, without bypassing input-attempt caps.

Production files: AgentSessionService.cs, SessionMessageQueueService.cs, its new ResumeReceipt partial, its Expectations partial, and QueueAttention.cs under server/Application/Services. Tests extend the real launch fixture and StandingResumeReceiptReadinessTests; legacy harness runners remain Unknown unless explicitly configured. Session runtime invariants and the exact checkpoint manifest are updated. No migration, setting, new scheduler, timer extension, provider launch change, deployment or branch switch was made.

## Qualified ordinary evidence

The operator brief replaces Unit/broad sweeps with one red witness and narrow green per behavior. CP-3/CP-4 were split into exact methods CP-31..35 and CP-41..45, sharing one build per committed slice. CP-5 shares that build. One execution is one TUnit method; internal scenario variants are not extra test counts.

- At 8bdf2b1f7162dfc72f07de06e34c5c673f96d544: CP-31/32/33 each executed once and failed the intended assertions (input during Behind, screen-only Sent without the receipt, interrupted-claim retyping). CP-34 passed its baseline genuine-failure matrix.
- At 91b57789e978f9958fa20ca829e435bdae8acfef: CP-41..44 each passed once and CP-5 passed all three existing Mode.Now grace tests: 7 passed, 0 failed/skipped. SOURCE VALID independently checked.
- At a8cfc84394b62eb2384c1a2bff7e29d2adc5dfc3: expanded CP-33 failed on an actual opening Esc while Behind; CP-35 failed because settled-note text incorrectly forced transcript confirmation on an ordinary direct send. Both were assertion/behavior failures, not fixture failures. The text-scope regression was introduced by the first queue slice and is repaired by explicit row-context receipt matching.
- At 3dc86c36458b46b39ca764dd0418a9c7ed6c5714: CP-41..45 each passed once; CP-5 passed three: 8 passed, 0 failed/skipped. SOURCE VALID independently checked. V-4 now covers eligible and owed-channel recipients, observation-own-timeout without launch cleanup, old identical history, exact new persisted receipt count, channel-before-note order and real fresh bootstrap. V-5 covers 24 virtual minutes and Behind/Unknown/legacy snapshots, automated Now, SendNow and watchdog admission. V-6 covers a real post-commit callback failure, immutable-key replay, a claimed attempt across graph reconstruction, two-hour stranded discovery, local Esc containment and positive receipt ingestion while Behind. V-7 covers idle genuine failure, Working, non-AlwaysOn, changed generation and final-grace receipt.
- At 3165a54d9e4f1c01a8dc38a42ba941418c708780: only the affected CP-42 was rerun after result-propagation repair: 1 passed, 0 failed/skipped. It now admits actual fake-provider acceptance through both a durable runtime action and SendNow; asserts Deferred/retained-queue outcomes; repeats all three non-Ready states beyond grace; and confirms the original row without more input. SOURCE VALID independently checked. Other methods retain their qualified preceding-slice evidence; they were not rerun solely to relabel a SHA.

S1 reader/transport and S2a runtime persistence proof V-1..V-3 remain the inherited evidence linked by the plan and prior handoffs. They were not repeated here. No PC/Mutation, native provider call, complete assembly, Unit lane, RC roster, Windows native qualification or activation test ran.

## Remaining qualification and handoff limits

The implementation is not a whole-card Final/Full claim. The remaining planned ordinary cases are independent reconstruction at the acknowledgement-only and actual-native-acceptance-before-persistence cuts (V-6 currently combines acknowledgement failure/replay with the later claim reconstruction); a launch-producer-level response/event failure injection; and keyed-note NoComposerEvidence, Truncated/clipped receipt, QueuedUserPrompt and screen-surrogate cases in V-7. The final action/forced outcome variants were first executed green; they do not claim an independently executed counterfactual beyond the historical V-5 red. These need an exact follow-up qualification slice if the caller requires whole-plan completion before landing.

Every PC-1..PC-28 and every variant remains Pending. Existing candidate inventory is docs/superpowers/mutations/2026-10-25-card-1574-runtime-candidates.md. New queue guard/source sites must be bound and independently witnessed by the commissioned post-land Mutation stage; ordinary red/green is not a mutation certificate. Shared/overlapping entry guards need path-specific reachability. No mutation repair or production activation was attempted. Both matching product components and adopted older hosts still require caller-owned activation qualification. The complete commit list must also reach the rc line; this task pushes only its assigned branch.

## Costs and non-proofs retained

Seven checkpoint build attempts ran, one actual build per invocation; unused manifest build definitions in the tool summary are not executions. The only unlisted build was the necessary checkpoint CLI bootstrap through scripts/build-slot.ps1 with OutputPath=bin-c1574-cli/ and UseAppHost=false: granted slot, 0s wait, 7s held, build 6.44s, five warnings and zero errors. All scheduled build/test drivers had granted slots, never an unleased retry.

The first run accidentally supplied --row-timeout 8, interpreted as seconds and rounded to a one-minute build deadline. It ran zero tests and is not a red witness. Correcting the argument to the plan's intended 8m is not a widened test timeout. The next build found missing imports in this task's new test file (nine compiler errors, zero tests); the imports were repaired in 8bdf2b1f7. Invalid shortened expected-SHA and shared-build After-label invocations refused before a driver. Some early wait invocations used a doubled results-relative path and returned 75 while the one real executor continued; absolute run paths then joined it. No duplicate executor or unjoined owned run remains.

All later assertion reds and greens preserve their original reports below. No same-source green repeats or loaded performance proofs ran. Host: Debian Linux x64, 24 advertised cores. Tests use isolated/shared fixture PostgreSQL as explicitly selected, fake adapters and actual launch/queue/ingestion transitions; no provider processes or product runner input. Phase attribution is partial; no measured p95 is claimed from these sparse samples. No run approached the 30-minute debug threshold. All task-created bin-c1574-* outputs were removed after joined runs; generated receipts remain ignored.

## Source publication

The implementation commits are fast-forward-only from the assigned af81145b8 tip and each slice was pushed. Fetched-master merge-tree check at source 3165a54d9e4f1c01a8dc38a42ba941418c708780 is clean against master 3c94a31d4b3f44628d5d0d0122616d29c1de7ca5, resulting tree 210cd9eebe554163ac927d9435f4669163c22673. No target merge, rebase, amend, reset, force-push, rc write, master write or restart. The completion report records the final narrative-only report commit and final remote tip; tested receipts retain their actual source SHAs.

Complete source commit inventory in cherry-pick order (the narrative report commit follows and is included in the completion message):

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
af81145b8fdd157445562314955c4cbe976e580e docs(CARD-1574): preserve runtime evidence and remaining queue scope
a9b65717793eaa0850eb8136d1fe553a9c357f2a test(CARD-1574): witness durable resume queue and readiness fences
8bdf2b1f7162dfc72f07de06e34c5c673f96d544 test(CARD-1574): import queue DTOs and exercise delayed replay
91b57789e978f9958fa20ca829e435bdae8acfef fix(CARD-1574): retain one resume note behind receipt readiness fences
622b832e2aa4ac5e3bf36576634b1e587974e89a test(CARD-1574): expose note-text scope and old-claim recovery gaps
a8cfc84394b62eb2384c1a2bff7e29d2adc5dfc3 docs(CARD-1574): align shared-build checkpoint slice labels
3dc86c36458b46b39ca764dd0418a9c7ed6c5714 fix(CARD-1574): bind receipts to queue ownership and retain recovery debt
3165a54d9e4f1c01a8dc38a42ba941418c708780 fix(CARD-1574): propagate uncertain receipt attempts as queue deferrals
```

## Exact checkpoint reports

The following are unedited checkpoint reports retained for provenance, counts, source stamps, admission/execution costs and original outcomes. Generated JSON/TRX payloads stay in the ignored checkpoint directories.

### 20261025-013553-78c6

--- checkpoint report ---
run: 20261025-013553-78c6   manifest: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-013553-78c6/manifest.resolved.yaml
commit: a9b65717793eaa0850eb8136d1fe553a9c357f2a  branch: feat/card-task-ad9a35c6  worktree: /work/worktrees/task-ad9a35c6  host: Debian GNU/Linux 12 (bookworm) cores=24
source: a9b65717793eaa0850eb8136d1fe553a9c357f2a state=clean buildSource=unknown
CHECKPOINT CP-31 commit=a9b65717793eaa0850eb8136d1fe553a9c357f2a build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Resume_note_waits_then_receipts_once executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-31 waited=0s executed=0.000s wall=0.000s reason=build-deadline-exceeded
CHECKPOINT CP-32 commit=a9b65717793eaa0850eb8136d1fe553a9c357f2a build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Accepted_note_outlives_grace_without_retyping executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-32 waited=0s executed=0.000s wall=0.000s reason=build-deadline-exceeded
CHECKPOINT CP-33 commit=a9b65717793eaa0850eb8136d1fe553a9c357f2a build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Recreated_services_recover_the_original_note executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-33 waited=0s executed=0.000s wall=0.000s reason=build-deadline-exceeded
CHECKPOINT CP-34 commit=a9b65717793eaa0850eb8136d1fe553a9c357f2a build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Ready_absence_keeps_existing_recovery_rules executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-34 waited=0s executed=0.000s wall=0.000s reason=build-deadline-exceeded
BUDGET wall=77.481s execution=77.481s admissionOnly=0.000s
unlisted: none (the tool ran no other build or test command)
wall: 1m17s  sequential-equivalent: 0m00s  builds: 6  max-concurrent-builds: 1  rows: 0 green 4 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1574-queue-red/
evidence: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-013553-78c6/report.md
verdict: RED exit=5 reason=build-deadline-exceeded


### 20261025-013739-50d6

--- checkpoint report ---
run: 20261025-013739-50d6   manifest: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-013739-50d6/manifest.resolved.yaml
commit: a9b65717793eaa0850eb8136d1fe553a9c357f2a  branch: feat/card-task-ad9a35c6  worktree: /work/worktrees/task-ad9a35c6  host: Debian GNU/Linux 12 (bookworm) cores=24
source: a9b65717793eaa0850eb8136d1fe553a9c357f2a state=clean buildSource=unknown
CHECKPOINT CP-31 commit=a9b65717793eaa0850eb8136d1fe553a9c357f2a build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Resume_note_waits_then_receipts_once executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=a9b65717793eaa0850eb8136d1fe553a9c357f2a sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-31 waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-32 commit=a9b65717793eaa0850eb8136d1fe553a9c357f2a build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Accepted_note_outlives_grace_without_retyping executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=a9b65717793eaa0850eb8136d1fe553a9c357f2a sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-32 waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-33 commit=a9b65717793eaa0850eb8136d1fe553a9c357f2a build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Recreated_services_recover_the_original_note executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=a9b65717793eaa0850eb8136d1fe553a9c357f2a sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-33 waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-34 commit=a9b65717793eaa0850eb8136d1fe553a9c357f2a build=failed filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Ready_absence_keeps_existing_recovery_rules executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=a9b65717793eaa0850eb8136d1fe553a9c357f2a sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-34 waited=0s executed=0.000s wall=0.000s
BUDGET wall=246.169s execution=246.169s admissionOnly=0.000s
unlisted: none (the tool ran no other build or test command)
wall: 4m06s  sequential-equivalent: 0m00s  builds: 6  max-concurrent-builds: 1  rows: 0 green 4 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1574-queue-red/
evidence: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-013739-50d6/report.md
verdict: RED exit=2


### 20261025-014207-75ce

--- checkpoint report ---
run: 20261025-014207-75ce   manifest: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-014207-75ce/manifest.resolved.yaml
commit: 8bdf2b1f7162dfc72f07de06e34c5c673f96d544  branch: feat/card-task-ad9a35c6  worktree: /work/worktrees/task-ad9a35c6  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 8bdf2b1f7162dfc72f07de06e34c5c673f96d544 state=clean buildSource=verified
CHECKPOINT CP-31 commit=8bdf2b1f7162dfc72f07de06e34c5c673f96d544 build=ok filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Resume_note_waits_then_receipts_once executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-014207-75ce/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=8bdf2b1f7162dfc72f07de06e34c5c673f96d544 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-31 waited=0s executed=55.855s wall=55.917s
PHASES CP-31 slotWait=0s build=238.2991366s startup=45.8306143s testsWall=7.9235316s teardown=1.1795777s hostWall=54.9336936s
FAILED Antiphon.Tests.Application.StandingResumeReceiptReadinessTests.C1574_Resume_note_waits_then_receipts_once (CP-31) ShouldAssertException: adapter.Inputs     should be empty but had 2     items and was ["[session bac97615] [System note from Antiphon: your session was resumed after a restart. Skim today's memory log before acting; do not re-execute work that already completed. Reply NO_REPLY unless you have something for the user.]", " "]  Additional Info:     launch completes without typing into unread history -> rows/CP-31/failures.md
CHECKPOINT CP-32 commit=8bdf2b1f7162dfc72f07de06e34c5c673f96d544 build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Accepted_note_outlives_grace_without_retyping executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-014207-75ce/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=8bdf2b1f7162dfc72f07de06e34c5c673f96d544 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-32 waited=0s executed=29.301s wall=29.367s
PHASES CP-32 slotWait=0s build=0s startup=17.5925004s testsWall=9.6335134s teardown=1.2803669s hostWall=28.5063806s
FAILED Antiphon.Tests.Application.StandingResumeReceiptReadinessTests.C1574_Accepted_note_outlives_grace_without_retyping (CP-32) ShouldAssertException: original.Status     should be QueuedMessageStatus.Pending     but was QueuedMessageStatus.Sent -> rows/CP-32/failures.md
CHECKPOINT CP-33 commit=8bdf2b1f7162dfc72f07de06e34c5c673f96d544 build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Recreated_services_recover_the_original_note executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-014207-75ce/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=8bdf2b1f7162dfc72f07de06e34c5c673f96d544 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-33 waited=0s executed=27.905s wall=27.974s
PHASES CP-33 slotWait=0s build=0s startup=16.4774261s testsWall=9.4680515s teardown=1.1881129s hostWall=27.1335904s
FAILED Antiphon.Tests.Application.StandingResumeReceiptReadinessTests.C1574_Recreated_services_recover_the_original_note (CP-33) ShouldAssertException: rebuilt.Adapter.Inputs     should be empty but had 2     items and was ["Restart obligation for this launch: resume the standing work and retain its delivery identity.", " "]  Additional Info:     interrupted claims cannot re-Enter or retype while Behind -> rows/CP-33/failures.md
CHECKPOINT CP-34 commit=8bdf2b1f7162dfc72f07de06e34c5c673f96d544 build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Ready_absence_keeps_existing_recovery_rules executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-014207-75ce/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=8bdf2b1f7162dfc72f07de06e34c5c673f96d544 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-34 waited=0s executed=33.104s wall=33.175s
PHASES CP-34 slotWait=0s build=0s startup=15.6714569s testsWall=12.5583575s teardown=3.4409212s hostWall=31.6707359s
BUDGET wall=399.778s execution=399.774s admissionOnly=0.004s
unlisted: none (the tool ran no other build or test command)
wall: 6m39s  sequential-equivalent: 2m26s  builds: 6  max-concurrent-builds: 1  rows: 1 green 3 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1574-queue-red/
evidence: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-014207-75ce/report.md
verdict: RED exit=1


### 20261025-015101-0038

--- checkpoint report ---
run: 20261025-015101-0038   manifest: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015101-0038/manifest.resolved.yaml
commit: 91b57789e978f9958fa20ca829e435bdae8acfef  branch: feat/card-task-ad9a35c6  worktree: /work/worktrees/task-ad9a35c6  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 91b57789e978f9958fa20ca829e435bdae8acfef state=clean buildSource=verified
CHECKPOINT CP-41 commit=91b57789e978f9958fa20ca829e435bdae8acfef build=ok filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Resume_note_waits_then_receipts_once executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015101-0038/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=91b57789e978f9958fa20ca829e435bdae8acfef sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-41 waited=0s executed=53.400s wall=53.479s
PHASES CP-41 slotWait=0s build=198.0209826s startup=42.8061483s testsWall=8.248549s teardown=1.216953s hostWall=52.2716468s
CHECKPOINT CP-42 commit=91b57789e978f9958fa20ca829e435bdae8acfef build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Accepted_note_outlives_grace_without_retyping executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015101-0038/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=91b57789e978f9958fa20ca829e435bdae8acfef sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-42 waited=0s executed=32.208s wall=32.260s
PHASES CP-42 slotWait=0s build=0s startup=16.7031082s testsWall=12.1835637s teardown=1.2264377s hostWall=30.1131096s
CHECKPOINT CP-43 commit=91b57789e978f9958fa20ca829e435bdae8acfef build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Recreated_services_recover_the_original_note executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015101-0038/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=91b57789e978f9958fa20ca829e435bdae8acfef sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-43 waited=0s executed=27.137s wall=27.204s
PHASES CP-43 slotWait=0s build=0s startup=16.7560452s testsWall=8.074384s teardown=1.2448041s hostWall=26.0752333s
CHECKPOINT CP-44 commit=91b57789e978f9958fa20ca829e435bdae8acfef build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Ready_absence_keeps_existing_recovery_rules executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015101-0038/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=91b57789e978f9958fa20ca829e435bdae8acfef sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-44 waited=0s executed=31.487s wall=31.542s
PHASES CP-44 slotWait=0s build=0s startup=16.2194675s testsWall=13.3500225s teardown=0.9900132s hostWall=30.5595033s
CHECKPOINT CP-5 commit=91b57789e978f9958fa20ca829e435bdae8acfef build=reused filter=/*/*/SessionMessageQueueDeliveryVerificationTests/Card0164_ModeNow_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015101-0038/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=91b57789e978f9958fa20ca829e435bdae8acfef sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=29.711s wall=29.787s
PHASES CP-5 slotWait=0s build=0s startup=17.1871708s testsWall=10.2088218s teardown=1.2589851s hostWall=28.6549882s
BUDGET wall=458.663s execution=458.659s admissionOnly=0.004s
unlisted: none (the tool ran no other build or test command)
wall: 7m38s  sequential-equivalent: 2m54s  builds: 6  max-concurrent-builds: 1  rows: 5 green 0 red 0 skipped
outputs: deleted bin-c1574-queue/
evidence: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015101-0038/report.md
verdict: GREEN exit=0


### 20261025-015950-3cd6

--- checkpoint report ---
run: 20261025-015950-3cd6   manifest: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015950-3cd6/manifest.resolved.yaml
commit: a8cfc84394b62eb2384c1a2bff7e29d2adc5dfc3  branch: feat/card-task-ad9a35c6  worktree: /work/worktrees/task-ad9a35c6  host: Debian GNU/Linux 12 (bookworm) cores=24
source: a8cfc84394b62eb2384c1a2bff7e29d2adc5dfc3 state=clean buildSource=verified
CHECKPOINT CP-33 commit=a8cfc84394b62eb2384c1a2bff7e29d2adc5dfc3 build=ok filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Recreated_services_recover_the_original_note executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015950-3cd6/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=a8cfc84394b62eb2384c1a2bff7e29d2adc5dfc3 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-33 waited=0s executed=55.552s wall=55.612s
PHASES CP-33 slotWait=0s build=212.4379551s startup=44.8038412s testsWall=6.7730183s teardown=2.8166484s hostWall=54.3935006s
FAILED Antiphon.Tests.Application.StandingResumeReceiptReadinessTests.C1574_Recreated_services_recover_the_original_note (CP-33) ShouldAssertException: rebuilt.Adapter.Inputs     should be empty but had 1     item and was ["\u001b"]  Additional Info:     local-command Esc is automatic input too -> rows/CP-33/failures.md
CHECKPOINT CP-35 commit=a8cfc84394b62eb2384c1a2bff7e29d2adc5dfc3 build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Ordinary_repeated_note_body_keeps_direct_send_contract executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015950-3cd6/rows/CP-35/run.trx slot=granted waited=0s dirty=0 source=a8cfc84394b62eb2384c1a2bff7e29d2adc5dfc3 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-35 waited=0s executed=29.569s wall=29.637s
PHASES CP-35 slotWait=0s build=0s startup=16.8274155s testsWall=9.7176194s teardown=2.3092083s hostWall=28.8542435s
FAILED Antiphon.Tests.Application.StandingResumeReceiptReadinessTests.C1574_Ordinary_repeated_note_body_keeps_direct_send_contract (CP-35) ConflictException: Message delivery could not be verified — the terminal did not accept it (the submitted prompt never became a transcript record). See the agent's incidents. -> rows/CP-35/failures.md
BUDGET wall=311.878s execution=311.876s admissionOnly=0.002s
unlisted: none (the tool ran no other build or test command)
wall: 5m11s  sequential-equivalent: 1m25s  builds: 6  max-concurrent-builds: 1  rows: 0 green 2 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1574-queue-red/
evidence: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-015950-3cd6/report.md
verdict: RED exit=1


### 20261025-020811-221a

--- checkpoint report ---
run: 20261025-020811-221a   manifest: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-020811-221a/manifest.resolved.yaml
commit: 3dc86c36458b46b39ca764dd0418a9c7ed6c5714  branch: feat/card-task-ad9a35c6  worktree: /work/worktrees/task-ad9a35c6  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 3dc86c36458b46b39ca764dd0418a9c7ed6c5714 state=clean buildSource=verified
CHECKPOINT CP-41 commit=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 build=ok filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Resume_note_waits_then_receipts_once executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-020811-221a/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-41 waited=0s executed=67.808s wall=67.883s
PHASES CP-41 slotWait=0s build=224.8176002s startup=47.6695841s testsWall=16.8615231s teardown=2.008371s hostWall=66.5394851s
CHECKPOINT CP-42 commit=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Accepted_note_outlives_grace_without_retyping executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-020811-221a/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-42 waited=0s executed=33.114s wall=33.190s
PHASES CP-42 slotWait=0s build=0s startup=16.8536138s testsWall=11.6359436s teardown=3.2741075s hostWall=31.7636646s
CHECKPOINT CP-43 commit=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Recreated_services_recover_the_original_note executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-020811-221a/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-43 waited=0s executed=29.689s wall=29.754s
PHASES CP-43 slotWait=0s build=0s startup=18.8082061s testsWall=8.1289956s teardown=1.9287564s hostWall=28.865958s
CHECKPOINT CP-44 commit=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Ready_absence_keeps_existing_recovery_rules executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-020811-221a/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-44 waited=0s executed=29.780s wall=29.849s
PHASES CP-44 slotWait=0s build=0s startup=16.1519916s testsWall=11.6672263s teardown=1.1761377s hostWall=28.9953554s
CHECKPOINT CP-45 commit=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 build=reused filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Ordinary_repeated_note_body_keeps_direct_send_contract executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-020811-221a/rows/CP-45/run.trx slot=granted waited=0s dirty=0 source=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-45 waited=0s executed=26.784s wall=26.823s
PHASES CP-45 slotWait=0s build=0s startup=16.640578s testsWall=7.422101s teardown=1.9263992s hostWall=25.9890785s
CHECKPOINT CP-5 commit=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 build=reused filter=/*/*/SessionMessageQueueDeliveryVerificationTests/Card0164_ModeNow_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-020811-221a/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=3dc86c36458b46b39ca764dd0418a9c7ed6c5714 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=28.700s wall=28.776s
PHASES CP-5 slotWait=0s build=0s startup=16.6652208s testsWall=9.9966469s teardown=1.1493807s hostWall=27.811248s
BUDGET wall=521.738s execution=521.733s admissionOnly=0.005s
unlisted: none (the tool ran no other build or test command)
wall: 8m41s  sequential-equivalent: 3m36s  builds: 6  max-concurrent-builds: 1  rows: 6 green 0 red 0 skipped
outputs: deleted bin-c1574-queue/
evidence: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-020811-221a/report.md
verdict: GREEN exit=0


### 20261025-021828-fc75

--- checkpoint report ---
run: 20261025-021828-fc75   manifest: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-021828-fc75/manifest.resolved.yaml
commit: 3165a54d9e4f1c01a8dc38a42ba941418c708780  branch: feat/card-task-ad9a35c6  worktree: /work/worktrees/task-ad9a35c6  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 3165a54d9e4f1c01a8dc38a42ba941418c708780 state=clean buildSource=verified
CHECKPOINT CP-42 commit=3165a54d9e4f1c01a8dc38a42ba941418c708780 build=ok filter=/*/*/StandingResumeReceiptReadinessTests/C1574_Accepted_note_outlives_grace_without_retyping executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-021828-fc75/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=3165a54d9e4f1c01a8dc38a42ba941418c708780 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-42 waited=0s executed=55.026s wall=55.079s
PHASES CP-42 slotWait=0s build=292.1013621s startup=40.0743099s testsWall=12.0944995s teardown=2.0326001s hostWall=54.2014079s
BUDGET wall=415.658s execution=415.658s admissionOnly=0.000s
unlisted: none (the tool ran no other build or test command)
wall: 6m55s  sequential-equivalent: 0m55s  builds: 6  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
outputs: deleted bin-c1574-queue/
evidence: /work/worktrees/task-ad9a35c6/.antiphon/checkpoints/20261025-021828-fc75/report.md
verdict: GREEN exit=0


--- next stage ---
next: land
handoff: Adopt the coherent queue-fence slice with its qualification limits; preserve the whole commit list for rc, commission the remaining exact matrix cases as required, then ordinary Review/land and separately bound post-land Mutation. This report grants no whole-card Final/Full or activation verdict.
artifact: /work/worktrees/task-ad9a35c6/docs/investigations/2026-10-25-card-1574-queue-fences-checkpoint.md
