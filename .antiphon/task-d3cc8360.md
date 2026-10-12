Fixed F1/F2: the caller retains the original catch-up exception through held-event abandonment; all 122 ordinary executions passed. Ready for Final Review, not landing.

Code task and new landing owner: d3cc8360-d502-4525-8f0b-b161e1adc4b4.
Original inherited Code task: 54ad209e-df48-4ac1-9fe8-e05fd928c3ba; S1 34df9193c lands with S2.
Branch: feat/card-task-d3cc8360.
Worktree: /work/worktrees/task-d3cc8360.
Assigned base: 5d451ca4c3315744a8cdde3a1f81f0018a4ba382.
Tests-first commit: 563fc930d10d81b78c65e3900fefa09684da9cdd.
Actual ordinary tested SHA: 03ceb17294647acbddf51532c33182c48f5d0070.
Subsequent mutation-binding documentation/test-comment commit: d662556ac1103c90af8fe10bce093186a0c6b312. Production and test bodies are unchanged after the tested SHA. This stored report is a further report-only commit; the final message supplies its published full SHA.

Deliverables (absolute paths):

- /work/worktrees/task-d3cc8360/server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs
- /work/worktrees/task-d3cc8360/tests/Antiphon.Tests/Application/SessionRunnerHeldEventDeliveryTests.cs
- /work/worktrees/task-d3cc8360/tests/Antiphon.Tests/Application/SessionRunnerStreamReadinessTests.cs (PC comments only)
- /work/worktrees/task-d3cc8360/docs/superpowers/plans/2026-10-12-card-1205-turnend-gap-plan.md
- /work/worktrees/task-d3cc8360/docs/superpowers/plans/2026-10-12-card-1205-test-design.md
- /work/worktrees/task-d3cc8360/docs/superpowers/plans/2026-10-12-card-1205-s2-repair5-checkpoints.md (closed executable manifest)
- /work/worktrees/task-d3cc8360/docs/superpowers/mutations/2026-10-12-card-1205-s2-repair5-candidate.md (pending unbound SourceLanding candidate)

F1 implementation: capture the original exception in catch and rethrow it normally. A filtered catch around the common finally's preservation/unwind catches secondary exceptions only while that original is in flight. Best-effort diagnostic logging cannot override it. Normal preservation failure retains the existing cancellation/reconnect behavior. Abandoned callback task/scope custody transfers to the observer before diagnostic logging, so a logger failure cannot strand the callback. There is still one common offer, one copy-suppression path and no uncertain publication replay. No timeout or existing assertion was widened/weakened.

T53 adds three gate-controlled, fake-monotonic-clock cases through the actual HTTP client/runtime/pump. The private handler's actual outer caller logs the propagated exception, which the test captures and checks by exact type and message. The throws/blocked row asserts OperationCanceledException("controlled second-list deadline"), one abandonment warning and iterator disposal before release; the retained callback then faults with InvalidOperationException("controlled late callback fault"), and the test awaits its logged fault and retained scope disposal. The throws/completed control accepts the original output once and propagates the same exception. The normal/blocked control retains clean outer reconnect/unwind with one warning and the same late-fault observation. Cancellation and fake time, never sleeps, drive the transitions; real-time waits are unchanged outer hang guards or new explicit assertion guards. T01, all 48 T51 rows and both T52 controls are unchanged byte-for-byte from the assigned base.

The test-first RED-1 source is 563fc930d10d81b78c65e3900fefa09684da9cdd. `git diff --exit-code 5d451ca4c3315744a8cdde3a1f81f0018a4ba382 563fc930d10d81b78c65e3900fefa09684da9cdd -- server src` exited 0: production is exactly the requested base. Fresh TRX: 3 executed, 2 passed, 1 assertion failure, 0 skipped/errors/timeouts. The failed row is (True, True):

    ShouldAssertException: h.Log.DisconnectException!.GetType()
        should be System.OperationCanceledException
        but was System.TimeoutException

The (True, False) and (False, True) rows passed on base because they are existing-behavior controls; forcing them red would invert correct assertions. The same guarded defect row passes after the fix. This is ordinary test-first defect proof, not a discharged PC cycle.

F2 audit and changes: corrected the PC-118 test comment, T50 design row and G-118 wording to the already-cancelled-host common-offer omission. The PC-118 payload table already described that omission; its wording remains valid. PC-111/112 table and test-comment wording now names the common offer rather than a removed failure-path dispatch. Grepped the plan, full test design, repair notes and relevant test comments; no other stale held-event mutation payload was found. Historical repair3/4 results remain historical. Added current T53/87-case census and PC-172..175. PC-172 removes the captured-exception filter; PC-173 substitutes Task.WhenAny completion for awaiting the late task, retaining scope cleanup; PC-174 omits the exceptional-unwind offer; PC-175 restores unbounded None dispatch. The PC-173 refinement avoids a scope-custody teardown failure obscuring the intended assertion red; no deliberate new-PC mutation was run.

The requested PC-118 scratch diagnostic changed only the finally predicate to:

    if (HasPayload(held) && !stoppingToken.IsCancellationRequested && !await PreserveHeldEventAsync(held, runtime, stoppingToken))

It retained following-copy suppression. One method-scoped T50 result was assertion-red:

    ShouldAssertException: h.Bus.Accepted
        should be 1
        but was 0

The scratch source was intentionally dirty (one file), recorded as diagnostic only, restored before deletion, and never committed/pushed. SourceLanding Mutation still owns all red/restore/green cycles. The initial diagnostic mistakenly ran the unchanged primary checkout: one baseline T50 passed. It is disclosed as an extra isolated build/run, not claimed as mutant evidence or ordinary manifest coverage. The corrected scratch run used the checkpoint tool's row verb. The mistaken baseline used the self-leasing run-checkpoint.ps1 driver, as required by the owner guide's single-slot rule; no wrapper held a second lease.

Ordinary verification at 03ceb17294647acbddf51532c33182c48f5d0070:

| Row | Executed / passed / failed / skipped | Full class census |
|---|---|---|
| CP-1 | 2 / 2 / 0 / 0 | Reproduction 2 |
| CP-2 | 87 / 87 / 0 / 0 | Reproduction 2; readiness 23; pump 5; held delivery 7; exit matrix 50 (48 matrix + 2 iterator controls) |
| ADJ-1 | 33 / 33 / 0 / 0 | HostStats 3; generation wire 9; settings 1; state commit 8; Working query 12 |

All 122 executions have distinct nonempty execution IDs; all three TRX run IDs are fresh. All three T53 argument rows passed. Full class names and counts were checked from TestDefinitions/TestMethod metadata mapped to executed results, not display-name inference. One ordinary isolated build (UseAppHost=false, bin-c1205-r5-green/) completed with 701 warnings, zero errors; CP-2 and ADJ-1 reused it. Manifest report `builds: 2` counts both declared red/green builds; only the selected green build ran. The baseline group likewise ran only its selected red build. No build id was named tests.

The tool completed normally, was observed dead, and wait returned 0; exit-75 waits were continued to completion. Source validator returned `CHECKPOINT SOURCE VALID source=03ceb17294647acbddf51532c33182c48f5d0070 rows=3`. All ordinary receipts have dirty=0, sourceState=clean, buildSource=verified. Source was frozen through each run. Ordinary wall time: 10m23s. There was one ordinary selection per case, no repeats/reruns after green.

Every required V/R outcome:

| ID | Actual outcome |
|---|---|
| V-1 | Passed: unchanged T01, CP-1 and CP-2 |
| V-2 | Passed: grace control, CP-1 and CP-2 |
| V-3 | Passed: connected and five unknown-comment cases, CP-2 |
| V-4 | Passed: exact keepalive readiness once, CP-2 |
| V-5 | Passed: eight first-event cases, catch-up ordering, T47..T53, CP-2 |
| V-6 | Passed: reconnect readiness rearm, CP-2 |
| V-7 | Passed: read/cancellation resource controls and bounded held-event/iterator cases, CP-2 |
| R-1 | Passed: reproduction and subscription ordering, CP-1/CP-2 |
| R-6 | Passed: all five existing pump cases, CP-2 |
| R-7 | Passed only the commissioned adjacent compatibility scope, ADJ-1; does not discharge S3/CP-3 |

The explicit repair brief excludes Unit and limits this Final round to S1/S2. No required manual work exists for S1/S2. No S1/S2 row is deferred to Final. V-8..V-40 and R-2..R-5/R-8/R-9 belong to S3-S5 and were not run; no whole-card producer-to-recipient/restart acceptance is claimed. No full assembly, browser, real provider, deployment, live delivery or production fault injection ran. Existing unbounded ordinary serial callbacks and arbitrary synchronous/non-cooperative operation execution are not redesigned; bounded wait abandonment is not forced termination.

Re-walked exit table:

| Exit | Held disposition |
|---|---|
| Disabled, pre-stream cancellation, first catch-up | No held parsed marker exists |
| Keepalive or payload-free marker | Catch-up only; no offer/suppression |
| Successful empty/nonempty list, completed per-session sync | One bounded offer; normal success suppresses following iterator copy |
| Ordinary non-cancellation list error/return | Swallowed by existing catch-up policy; common offer still runs; responsive offer keeps connection |
| List deadline or cooperative cancellation | Original exception is captured; common offer runs; completion or abandonment retains original exception |
| Non-cooperative list | Existing monotonic list bound ends wait; retained request/scope observed; common held offer and original unwind |
| Host cancellation before/during list, per-session/final pull or final checks | Same initial offer before host linkage; pending callback abandoned; original cancellation survives |
| Other catch-up or completion exception | Same common offer; original type/message/stack survive secondary preservation failures |
| Callback synchronous/asynchronous completion | One accepted offer; normal path suppresses copy; exceptional path propagates original |
| Callback faults/cancels or misses host/deadline bound | One abandonment warning; no replay; pending callback transfers to late observer; normal unwind unchanged, exceptional unwind preserves original |
| Preservation setup, diagnostics or held-scope disposal exception | Filter catches secondary failure only while original is in flight; best-effort warning cannot replace original |
| Cancellation after handler returns or while skipping copy | Offer already completed; no second offer |
| EOF, later MoveNext/read or iterator-disposal fault | Held decision already made; no redelivery (both T52 controls green) |
| Late retained task fault/disposal | Observer awaits and logs the real fault, then disposes its owned scope; no second publication |
| Outer stop, reconnect-delay cancellation or loop exit | No unowned held marker remains |
| Process death/arbitrary synchronous callback obstruction | No managed delivery/termination guarantee; out of ordinary scope |

Unlisted drivers/reasons: slot-gated tool bootstrap was necessary because this worktree had no tool binary (UseAppHost=false, bin-c1205-r5-tool/, one inherited nullable warning, zero errors; slot=granted waited=0s held=6s). The mistaken unchanged T50 diagnostic and corrected scratch T50 diagnostic each ran one isolated build and one exact method selection, requested to bind F2; both slot=granted waited=0s. Their unedited CHECKPOINT lines follow. No unleased/NoSlot run or slot timeout occurred. Ordinary green build waited 45s; test rows waited CP-1=15s, CP-2=15s, ADJ-1=30s. Transient task-owner read timeouts/502s recovered within the tool's unchanged uncertainty budget; no owner-unverified or cancellation exit occurred.

Platform reads: GET /api/runner-defaults and GET /api/session-runners succeeded using the session API/token, with no runner/platform pin or settings change. Card read succeeded on retry after a transport timeout. No stack or runner restart: restart=none.

Fetched origin/master: 02275c3ba8ec6969a6c7bbec8dbe48609367d045. `git merge-tree --write-tree origin/master HEAD` at the implementation tip exited 0, tree 69b2b4ef5126f50bd3c398a58f6eb688602f3df9. No rebase/reset/amend/force-push/landing. Full carried task range d8188da496b2369e9ee688f8977c4888510fbb9f..d662556ac1103c90af8fe10bce093186a0c6b312 passed check-evidence-diff.ps1: 20 commits, 3 evidence entries, zero violations. The final report-only tip is checked again before final reporting.

Cleanup completed after all owned runs ended: checkpoint custody clean removed only this task's red/green outputs; exact-name, realpath-confined cleanup removed 28 accidental baseline F2 output directories; the restored scratch worktree and its outputs were removed via git worktree remove. The bootstrap output is removed last after receipt validation. Generated TRX/JSON/logs remain ignored in the original evidence directories.

Evidence roots:

- /work/worktrees/task-d3cc8360/.antiphon/checkpoints/20261012-232648-d97d/ (baseline)
- /work/worktrees/task-d3cc8360/.antiphon/checkpoints/20261012-233744-f883/ (ordinary green)
- /work/worktrees/task-d3cc8360/.antiphon/c1205-r5-f2-evidence/ (baseline mistake and corrected scratch red)
- /work/worktrees/task-d3cc8360/.antiphon/c1205-r5-bootstrap.log

Rerun after a slot-gated bootstrap of tools/Antiphon.Checkpoints with OutputPath=bin-c1205-r5-tool/ and UseAppHost=false:

    dotnet tools/Antiphon.Checkpoints/bin-c1205-r5-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-12-card-1205-s2-repair5-checkpoints.md --after RepairFix --serial --expected-source-sha <full-current-HEAD> --max-wait 50s

Keep calling wait on that run until exit is not 75. Do not run RED-1 as a remaining ordinary failure on the fixed source.

Post-land Mutation NOT run. PC-1..PC-175 and every variant remain pending, including the explicitly probed PC-118 and new PC-172..175. Final Review is next; caller lands this original Code task d3cc8360 after Review, then commissions SourceLanding Mutation. No blocking human decision is needed.

Unedited checkpoint reports and diagnostic CHECKPOINT lines:

--- checkpoint report ---
run: 20261012-232648-d97d   manifest: /work/worktrees/task-d3cc8360/.antiphon/checkpoints/20261012-232648-d97d/manifest.resolved.yaml
commit: 563fc930d10d81b78c65e3900fefa09684da9cdd  branch: feat/card-task-d3cc8360  worktree: /work/worktrees/task-d3cc8360  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 563fc930d10d81b78c65e3900fefa09684da9cdd state=clean buildSource=verified
CHECKPOINT RED-1 commit=563fc930d10d81b78c65e3900fefa09684da9cdd build=ok filter=/*/*/SessionRunnerHeldEventDeliveryTests*/C1205_Catchup_exception_survives_bounded_held_delivery* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-d3cc8360/.antiphon/checkpoints/20261012-232648-d97d/rows/RED-1/run.trx slot=granted waited=0s dirty=0 source=563fc930d10d81b78c65e3900fefa09684da9cdd sourceState=clean buildSource=verified
PHASES RED-1 slotWait=0s build=113.6596212s startup=5.1655309s testsWall=0.3486055s teardown=0.606306s hostWall=6.1204596s
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventDeliveryTests.C1205_Catchup_exception_survives_bounded_held_delivery (RED-1) ShouldAssertException: h.Log.DisconnectException!.GetType()     should be System.OperationCanceledException     but was System.TimeoutException -> rows/RED-1/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 2m01s  sequential-equivalent: 0m06s  builds: 2  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1205-r5-red/, bin-c1205-r5-green/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261012-232648-d97d
evidence: /work/worktrees/task-d3cc8360/.antiphon/checkpoints/20261012-232648-d97d/report.md
verdict: RED exit=1

--- checkpoint report ---
run: 20261012-233744-f883   manifest: /work/worktrees/task-d3cc8360/.antiphon/checkpoints/20261012-233744-f883/manifest.resolved.yaml
commit: 03ceb17294647acbddf51532c33182c48f5d0070  branch: feat/card-task-d3cc8360  worktree: /work/worktrees/task-d3cc8360  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 03ceb17294647acbddf51532c33182c48f5d0070 state=clean buildSource=verified
CHECKPOINT CP-1 commit=03ceb17294647acbddf51532c33182c48f5d0070 build=ok filter=/*/Antiphon.Tests.Application/TurnEndGapReproductionTests*/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-d3cc8360/.antiphon/checkpoints/20261012-233744-f883/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=03ceb17294647acbddf51532c33182c48f5d0070 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=60s build=219.4699943s startup=101.1953704s testsWall=6.6093637s teardown=10.448437s hostWall=118.2534074s
CHECKPOINT CP-2 commit=03ceb17294647acbddf51532c33182c48f5d0070 build=reused filter=/*/Antiphon.Tests.Application/(TurnEndGapReproductionTests*)|(SessionRunnerStreamReadinessTests*)|(SessionRunnerEventPumpTests*)|(SessionRunnerHeldEventDeliveryTests*)|(SessionRunnerHeldEventExitMatrixTests*)/* executed=87 passed=87 failed=0 skipped=0 trx=/work/worktrees/task-d3cc8360/.antiphon/checkpoints/20261012-233744-f883/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=03ceb17294647acbddf51532c33182c48f5d0070 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=15s build=0s startup=49.5658168s testsWall=46.6547434s teardown=2.6777095s hostWall=98.8982699s
SLOW CLASS Antiphon.Tests.Application.SessionRunnerStreamReadinessTests 84s tests=23 (CP-2)
CHECKPOINT ADJ-1 commit=03ceb17294647acbddf51532c33182c48f5d0070 build=reused filter=/*/*/(SessionRunnerHttpClientHostStatsTests*)|(SessionRunnerGenerationWireTests*)|(SessionRunnerSettingsTests*)|(SessionStateCommitTests*)|(TranscriptWorkingStateQueryTests*)/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-d3cc8360/.antiphon/checkpoints/20261012-233744-f883/rows/ADJ-1/run.trx slot=granted waited=30s dirty=0 source=03ceb17294647acbddf51532c33182c48f5d0070 sourceState=clean buildSource=verified
PHASES ADJ-1 slotWait=30s build=0s startup=66.901462s testsWall=8.655615s teardown=1.220374s hostWall=76.7774501s
SLOW CLASS Antiphon.Tests.Application.SessionStateCommitTests 68s tests=8 (ADJ-1)
unlisted: none (the tool ran no other build or test command)
wall: 10m23s  sequential-equivalent: 5m57s  builds: 2  max-concurrent-builds: 1  rows: 3 green 0 red 0 skipped
outputs: kept bin-c1205-r5-red/, bin-c1205-r5-green/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261012-233744-f883
evidence: /work/worktrees/task-d3cc8360/.antiphon/checkpoints/20261012-233744-f883/report.md
verdict: GREEN exit=0

CHECKPOINT F2-SCRATCH commit=563fc930d10d81b78c65e3900fefa09684da9cdd build=ok filter=/*/*/SessionRunnerHeldEventDeliveryTests*/C1205_Last_transcript_pull_cancellation_preserves_one_output* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d3cc8360/.antiphon/c1205-r5-f2-evidence/F2-SCRATCH-20261012-232916-be34/run.trx slot=granted waited=0s dirty=0 source=563fc930d10d81b78c65e3900fefa09684da9cdd sourceState=clean buildSource=verified
CHECKPOINT F2-SCRATCH EXIT CODE: 0
CHECKPOINT F2-SCRATCH-RED commit=563fc930d10d81b78c65e3900fefa09684da9cdd build=ok filter=/*/*/SessionRunnerHeldEventDeliveryTests*/C1205_Last_transcript_pull_cancellation_preserves_one_output* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-d3cc8360/.antiphon/c1205-r5-f2-evidence/F2-SCRATCH-RED-20261012-233135-cf6c/run.trx slot=granted waited=0s dirty=1 source=563fc930d10d81b78c65e3900fefa09684da9cdd+dirty:da870c6b050f1e8c773e0a602bceabc9aaf74730d2f3153d1d0169e5580dfdd7 sourceState=dirty buildSource=verified
CHECKPOINT F2-SCRATCH-RED EXIT CODE: 1

--- next stage ---
next: review
handoff: Final Review this repair with landing owner d3cc8360-d502-4525-8f0b-b161e1adc4b4; ordinary source 03ceb17294647acbddf51532c33182c48f5d0070 passed 122/122, subsequent edits are mutation-binding docs/comments and this report. Re-run CP-1/CP-2/ADJ-1; all PC-1..175 remain pending for SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-12-card-1205-s2-repair5-checkpoints.md
[antiphon-report:d3cc8360 done]
