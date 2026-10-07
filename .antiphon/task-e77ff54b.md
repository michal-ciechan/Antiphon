# CARD-1108/1124 follow-up S2 Final Review

Found: the new timestamp filter can omit a release confirmed by the current sweep when UTC steps backward. CARD-1145 records the regression. No production or test source was changed. The original Code landing owner remains ec23e008-402d-4d14-9f61-aab1d7e20a74; return this source to Code before landing.

Reviewed source: refs/heads/feat/card-task-ec23e008 at ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f, parent/base 2f751049b860f56d1e1d46aa3b293ec2a7274bcb, one commit/five files. The Review checkout was clean at that SHA throughout the ordinary run. This report is the only Review branch deliverable.

## F1 — current-run release is lost on backward UTC (P2)

Where: server/Application/Services/TerminalRunnerSeatReleaseService.cs:145 and :170; ConfirmAsync at :930-934; server/Program.cs:629; RunnerSlotReconcileJob.cs:47.

Failure: take runStart at 12:00:00. After release eligibility and the physical release, UTC steps back to 11:59:59 before ConfirmAsync stamps its successful ledger transition. The old IsConfirmed-only condition counted this new confirmation. The new condition rejects it because ConfirmedAt < runStart, so a run that actually confirmed a release reports Released=0 and the reconcile job also undercounts. A Blocked event and park sufficiently older than their floors still satisfy all unchanged age checks after a small rollback. The process-wide scheduled overlap gate does not make UTC monotonic.

Why: production uses TimeProvider.System. Reading the same wall clock at both boundaries is not evidence of ordering. runStart is correctly taken before the first release initiated by this sweep, and >= correctly includes equality for a new confirmation; neither property covers a backward step. The C1129 fixture uses a forward-only FakeTimeProvider. Its ordinary passing cases therefore do not exercise the requested backward-clock condition. This is a new regression in an existing observable count, meeting the brief's regression verdict criterion; it is not evidence of an earlier physical release, a skipped safety gate, or a Working-session stop.

Fix: identify confirmation transitions belonging to the invocation through the existing release outcome path, instead of inferring membership solely from wall-clock ordering. Keep the release safety gates and zero-added-statement requirement. Add backward-clock and same-instant prior-confirmation regression arms and update D-2/docs if the precise counter contract changes. Do not replace >= with >: that would lose genuine same-instant confirmations.

Evidence is a read-only source trace, not an executed new regression or mutation. Backlog CARD-1145 (756910ab-e831-4f44-8ee3-471dcd7d0fa6) was created and read back. card.ps1 printed a post-create Windows-drive cleanup error on Linux; GET confirmed the card exists in Backlog, so it was not created again.

Related nonblocking limit: equal/repeated clock instants cannot distinguish a row confirmed before a run from one confirmed inside it. With interval zero, or a restarted/backward clock, a prior ConfirmedAt equal to or greater than the new runStart can still be counted. The new owner sentence's unconditional per-run wording exceeds that timestamp guarantee. This preserves a subset of the pre-existing recount behavior and is on the same card. No release authority depends on Released.

## Production and cursor audit

Only ReclaimLegacyAsync's bookkeeping/loop control changes. The same TryHandleTaskAsync and all publication, reservation, runner identity, transcript-idle, pending-delivery, Working-veto and response-validation checks remain in place. No migration, configuration change, release-time reduction or safety-check removal. Enabled, ReclaimExisting and AutomaticEnabled stay default-off.

runStart is captured after the existing eligible-count read and before NextLegacyPageAsync, RegisterLegacyAsync or TryHandleTaskAsync. The post-visit FindAttemptReleaseAsync read already existed: its identity includes task/attempt, session, agent, runner/store/generation, settlement revision and settlement time, and IsConfirmed still needs Confirmed state, timestamp, action and an exit-confirming outcome. The new comparison adds no database statement. Disabled paths return before any query; an enabled empty set retains only its existing count read and never enters the visit loop.

The per-run set is checked before the callback, registration, release attempt and cursor commit. The repeated id never writes the cursor. NextLegacyPageAsync returns the ascending tail above AfterTaskId, then the ascending head at or below it; CommitLegacyCursorAsync remains after each actual visit, including caught failures. In the six-row example with cursor B and E/F removed after C, visits are C,D,A,B; the next repeated C stops before a write, leaving B. The next run starts again above B with a fresh set. A newly eligible row behind a passed cursor waits for the next wrap, rather than being permanently skipped. The stop never advances past an unvisited row. Existing cursor-restart, poison-row and Working-row arms remain.

Dispatcher and reconcile job both call ReclaimScheduledAsync, sharing the singleton TerminalRunnerSeatDiscoveryState registered in Program. While the default interval is active, the second caller returns None and does not reuse LastLegacyReclaim as a released return. A later sweep excludes an earlier hook confirmation if its UTC timestamp is below the new runStart. The timestamp caveat above still applies. The job also has its pre-existing discovery/reconciliation phases; this patch changes none of them.

## Code report / plan audit

Read the Code report from /work/worktrees/task-ec23e008/.antiphon/task-ec23e008.md and the API task result, plus the requested prior S2/S3/S4/S1 reports from their named refs. All six Code CHECKPOINT lines are byte-identical to raw run 20261007-102329-90df/report.md. Counts CP-6..CP-11 are 1,1,10,15,7,7, all passed, none skipped. Its report.json validates against ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f. No missing or zero-count row.

The complete returned Code transcript has 250 entries; a since=250 read returned no further entries. Its seven terminal-command entries containing build/test drivers all wrap those drivers in scripts/build-slot.ps1. The checkpoint run takes its own slots. Additional runs are explained by red-first implementation, the three local controls, checkpoint-tool bootstrap, and the brief's named regressions/registry guard. The Code report gives build/run leases for the mutations and the extra 54-result regression plus 17-result standalone orphan run. No whole Unit or whole assembly run, unleased driver, unexplained broad selection or observed assertion weakening.

The new C1129 method has production outcome assertions for 4 distinct shrink visits, cursor position, 6 distinct open visits, Released 2 then 0, stable ledger/request counts, a negative arm with one genuinely new release, dirty-seat preservation and Working-seat exclusion. It is not a self-comparison stub. The 119 s arm calls the real scheduled gate, requires None and an unchanged cursor, then the additional second admits the existing 120 s run. The doc pin reads the owner file and asserts the actual sentence. Deleting/changing that sentence would fail the pin; changing production semantics alone need not fail a text pin.

The dated CARD-1141 D-6/V-6/PC-11 amendments match the two publication verifies plus one Advance verify (3 without a boundary, 4 with a no-op boundary; restored unconditional second read would produce 4 against the plain pin 3). The historical S4 report's executed PC-11 red was read, not rerun. RunnerSlotEndpointTests/PhoneHomeDeferredKillTests/RunnerSlotRulesTests roster is 10+3+2=15. The CP-28 prose amendment records 15 but deliberately leaves the historical Min cell at 13 as D-9 directed; a future importer still reads 13. This Review uses the new plan CP-9 with Min 15 and independently checks the full roster. Known-limits wording/pin is assigned to S6 and is not part of S2.

## Producer-to-recipient and recovery evidence

The affected ordinary classes include real producer-to-recipient tests, not just seeded queue rows. BlockedTaskParkReleaseTests.C1065_ReportPublicationPrecedesPhysicalRelease starts from settlement, persists the report/event/notification before the release wire, drives the real SessionMessageQueueService for both busy and eligible parents, and crosses obligation/settlement/note/render/attempt/prompt crash cuts. After restart it checks the same notification/queue identity, one submitted message and destination UserPrompt evidence above the retained delivery floor. Partial text, wrong destination, queued kind, stale floor/time and wrong generation leave confirmation pending.

TerminalRunnerSeatReleaseTests.Settlement_delivery_precedes_release_and_survives_release_fault likewise drives settlement -> durable note -> real queue -> complete native UserPrompt, busy/eligible recipients and a lost release reply. Parent_receipt_rejects_ack_stale_or_partial_prompt preserves the real delivery identity/floor and proves Sent/ack is insufficient. BlockedTaskParkDeliveryTests.C1065_FullAnswerRequiresNewSessionUserPrompt exercises full answer/spill bytes, the real WhenIdle queue, wrong/partial/stale/session/generation/kind/ack negatives, restart and seven save/commit cuts crossed with both busy and eligible recipients. It requires exactly one full matching destination UserPrompt and one queue identity before the accepted answer is consumed. The fixture's recipient adapter records only actual submitted input and routes native-format JSON through TranscriptNormalizer and runtime persistence.

Production RecoverReleasedSeatAnswersAsync binds answer id, attempt, destination session/generation and delivery baseline; clears the obligation under task/session locks only after LandNoteReceipt matches complete expected prompt text. LandNoteReceipt requires UserPrompt for task-completion/answer receipts and checks both identity and completeness. Storage/recovery is separate from release counters. Sync-debt tests retain the source SHA, publication digest and report through lease refusal, restart and parking disable. Review reply evidence also covers busy/eligible caller delivery on the existing inline path; the production-size pointer path remains the explicitly separate S6 work. No new manual/live acceptance is specified in S2; none is claimed here.

## Verification contract and pending work

The brief's explicit S2 closed selection excludes the whole Unit lane and supersedes its generic profile paragraph. This Review ran CP-6..CP-11 plus the full claimed release/park classes, standalone RunnerSeatOrphanSweepTests and registry guard in one serial checkpoint-tool run. No source edits while it ran. PC-4..PC-6, the requested doc mutation, and all other plan controls remain pending for method-scoped SourceLanding Mutation: the stage's read-only instruction forbids executing them in this Review. Existing red evidence was inspected only; neither it nor nightly status discharges a PC.

Platform reads: GET /api/runner-defaults returned revision 2; GET /api/session-runners returned one accepting Windows runner, one draining Linux runner and one accepting Linux runner. No Runner or Platform pin, no host settings or stack restart.

Own unlisted operations: one checkpoint-tool bootstrap build through build-slot.ps1, lease ce133822-271e-4891-a631-4e97dca2cf4f (7 seconds; one existing nullable warning); the test build/rows are all inside the single checkpoint run. An initial run invocation rejected an external results root before creating a run or driver (exit 2); results were moved to the tool-required ignored worktree evidence path. No fallback outside the slot gate, no extra test run, no test-list evidence.

## Merge and sibling coordination

At the initial fresh fetch, origin/master was still 2f751049b860f56d1e1d46aa3b293ec2a7274bcb. merge-tree --write-tree of the reviewed source with that master exited 0, tree e55e04ec18cb8b0e7e05aa9d339c47faef6a28f0; no conflicting hunk and no source rebase performed.

The project-scoped task listing identifies S3b as ce03fa7d-e8f9-4099-a8c8-06205abf992c, tip 5b0501c0b10f3e31b6d473deee4dae87cb646fee, and S6b as a6277b11-d093-4bf7-b20e-0dbe597e10f0, tip 832fb020e1bed815896e70f6603eb782695e6d30. Both have common base 426b700cf6e73616771f127f4533dba1a8d413bd with this source.

S3b pairwise merge-tree exits 0 (tree afd57e0a290e6454ab157f130178693702497604). Its runtime edits are the Reply/attempt sentences around lines 100-102, and its orchestration-loop edit is the corresponding paragraph around line 965. S2 adds its counter sentence around runtime line 115 and does not edit orchestration-loop. There is no same-line S2/S3b collision.

S6b pairwise merge-tree exits 1, only conflicting path docs/session-runtime-invariants.md (conflicted tree 6aa83b6c67e39013eebeaf6c674df7ce38e8fc30). The S2 counter sentence merges outside the conflict. The conflicting hunk is the adjacent inherited S1 addition plus S6b's replacement of Known limits:

- This source: the full CARD-1135 Held re-stamp sentence, followed by `Known limits stay on CARD-1097, CARD-1103, and CARD-1104.`
- S6b: `Known limits stay on CARD-1097 (per-tick park_resume_refused with no dedupe, resume reads the desktop checkout, the inspection lease window, confirm-path bookkeeping, and whole-repository prune), CARD-1103, CARD-1129, CARD-1135, and CARD-1104 for a remote parent with RunnerCwd.`

Preserve the Held sentence and reconcile the limits list and its test pin to what has actually landed. This is a pending sibling merge concern, not a current-origin/master conflict. No conflict was resolved in this read-only Review.

Statement-count qualification: CountingCommandInterceptor records ReaderExecuting/ReaderExecutingAsync, not non-reader ExecuteUpdate/ExecuteDelete calls. The shrink fixture's boundary also contributes its highest-id SELECT. Fresh observed values are therefore the same scoped reader-command measurements as Code's 78/116/46, not a census of all database commands. The zero-added-production-statement conclusion also follows directly from the diff: the post-visit task/session/ledger reads are unchanged and the new set/clock/comparison are in memory.

Waiter correction: after the first run call returned exit 75, a relative `wait --run` argument was mistakenly interpreted under the default results root. That reader launched no build/test and did not control the actual executor. Its exact pid/command was verified and only that owned waiter was terminated (143); the next wait used the absolute run directory. The same executor/run continued throughout, with no interrupted or repeated row.

## Independent Final result

One run, 20261007-105216-aa9d, finished GREEN exit 0 in 21m11s (sequential-equivalent 18m45s). One isolated test build, UseAppHost=false, max concurrent builds/rows 1, TUNIT_MAX_PARALLEL_TESTS=1. All ten rows took granted slots. CP-7 waited 30s; CP-8 waited 15s; all other rows waited 0s. 112 executed results passed, 0 failed, 0 skipped. The source validator printed `CHECKPOINT SOURCE VALID source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f rows=10` and exited 0.

Independent TRX audit found ten fresh run IDs and 112 distinct fresh execution IDs, all starting after this run began. CP-6..CP-11 display rosters exactly match the Code TRX rosters, with different execution IDs. Every class count equals the plan/brief roster. All fourteen TerminalRunnerSeatReleaseTests parameter rows executed (six stopper, two warm-pool, four status, two unsupported-transport), and all three DispatcherSweepLifetimeTests ScopeFault rows executed. No list-tests or exit-code-only evidence.

R-2 is split into CP-101 TerminalRunnerSeatReleaseTests=39 and CP-103 RunnerSeatOrphanSweepTests=17 so the latter runs alone. CP-102 supplies R-3: Release=3, SyncRecovery=2, Resume=3, Delivery=4. CP-104 supplies the registry guard 1+2. CP-9 supplies endpoint/deferred-kill/rules 10+3+2; CP-10 publication/identity/projection 3+2+2; CP-11 lifetime/registration 6+1. CP-6/7 overlap two cases from CP-8 as the source table requires; the 112 executions cover 110 distinct parameterized cases.

Fresh CP-6 measurement output:

```
C1129 visits shrink=True rows=6 visited=4 eligible=6 cap=6 listed=4 distinct=4 statements=78
C1129 visits shrink=False rows=6 visited=6 eligible=6 cap=6 listed=6 distinct=6 statements=116
C1129 releases rows=3 run1Released=2 run2Visited=3 run2Registered=3 run2Released=0 statementsRun2=46
C1129 registerThird released=0
C1129 negative released=1 confirmed=3 version2=3
```

CP-8 separately logged `C1108 verify label=G-11 calls=3` and `C1108 verify label=V-6 calls=4`. Existing happy-path counter and interval claims are reproduced; they do not resolve F1.

The test tool deleted its owned bin-c1108f-cp6 outputs and its dead executor shadow copy. The only remaining producer-owned directory, tools/Antiphon.Checkpoints/bin-review-e77ff54b-tool, was then removed after nonempty-component, resolved-root, exact-target and no-link checks. The complete source candidate guard base 2f751049b860f56d1e1d46aa3b293ec2a7274bcb..ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f returned commits=1 entries=0 violations=0. A final fetch still returned the same master and clean merge-tree e55e04ec18cb8b0e7e05aa9d339c47faef6a28f0.

The Review branch adds only this report. No release source, test, plan, doc sentence, or conflict fix was made. The evidence guard is also run over the full base..Review-HEAD range before publication. Generated TRX/JSON/logs remain ignored, and only this individual Markdown report is committed.

## Reproducible selection

The following table copies source CP-6..CP-11 verbatim and adds the brief's affected-class/standalone/registry rows with the same S2 build. It is the exact selection used, retained here so the scratch selection file need not survive. Build the checkpoint driver through scripts/build-slot.ps1 to an owned alternate output, then run its DLL with `run --plan .antiphon/task-e77ff54b.md --rows CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-101,CP-102,CP-103,CP-104 --serial --expected-source-sha <clean source HEAD>`. Use `TUNIT_MAX_PARALLEL_TESTS=1`, and wait using an absolute run directory until the exit is not 75. This report-only commit changes the source SHA for a new certification; the recorded run below certified ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-6 | S2 | `tests/Antiphon.Tests -> bin-c1108f-cp6/` | counters-s2 | `/*/*/BlockedTaskParkReclaimTests/C1129_*` | V-3, V-4 | exact 1 result, 0 failed/skipped | 1 | 5 | true |
| CP-7 | S2 | CP-6 | gate-s2 | `/*/*/BlockedTaskParkReclaimTests/C1108_ScheduledSweepIsGatedAndBoundedPerRun*` | V-5 | exact 1 result, 0 failed/skipped | 1 | 3 | true |
| CP-8 | S2 | CP-6 | reclaim-s2 | `/*/*/BlockedTaskParkReclaimTests/*` | R-1 | exact 10 results (9 + 1 new), 0 failed/skipped | 10 | 7 | true |
| CP-9 | S2 | CP-6 | job-s2 | `/*/*/(RunnerSlotEndpointTests*)\|(PhoneHomeDeferredKillTests*)\|(RunnerSlotRulesTests*)/*` | R-7 | exact 15 results (10 + 3 + 2), 0 failed/skipped | 15 | 3 | true |
| CP-10 | S2 | CP-6 | publication-s2 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | V-13, R-4, R-5 | exact 7 results, 0 failed/skipped | 7 | 2 | true |
| CP-11 | S2 | CP-6 | lifetime-s2 | `/*/*/(DispatcherSweepLifetimeTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | R-6 | exact 7 results (6 + 1), 0 failed/skipped | 7 | 3 | true |
| CP-101 | S2 | CP-6 | seat-review | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-2 | exact 39 results, 0 failed/skipped | 39 | 6 | true |
| CP-102 | S2 | CP-6 | park-review | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-3 | exact 12 results, 0 failed/skipped | 12 | 5 | true |
| CP-103 | S2 | CP-6 | orphan-review | `/*/*/RunnerSeatOrphanSweepTests/*` | R-2 | exact 17 results, 0 failed/skipped | 17 | 4 | true |
| CP-104 | S2 | CP-6 | registry-review | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | registry | exact 3 results, 0 failed/skipped | 3 | 1 | true |

## Unedited checkpoint-tool report

--- checkpoint report ---
run: 20261007-105216-aa9d   manifest: /work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/manifest.resolved.yaml
commit: ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f  branch: feat/card-task-e77ff54b  worktree: /work/worktrees/task-e77ff54b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f state=clean buildSource=verified
CHECKPOINT CP-6 commit=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f build=ok filter=/*/*/BlockedTaskParkReclaimTests/C1129_* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=143.7737839s startup=53.959325s testsWall=15.9178087s teardown=1.4555712s hostWall=71.3327127s
CHECKPOINT CP-7 commit=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f build=reused filter=/*/*/BlockedTaskParkReclaimTests/C1108_ScheduledSweepIsGatedAndBoundedPerRun* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/rows/CP-7/run.trx slot=granted waited=30s dirty=0 source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f sourceState=clean buildSource=verified
PHASES CP-7 slotWait=30s build=0s startup=43.8111912s testsWall=14.8791835s teardown=2.180544s hostWall=60.8709189s
CHECKPOINT CP-8 commit=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f build=reused filter=/*/*/BlockedTaskParkReclaimTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/rows/CP-8/run.trx slot=granted waited=15s dirty=0 source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f sourceState=clean buildSource=verified
PHASES CP-8 slotWait=15s build=0s startup=50.0604599s testsWall=74.3907751s teardown=1.5707016s hostWall=126.0219365s
SLOW CLASS Antiphon.Tests.Application.BlockedTaskParkReclaimTests 74s tests=10 (CP-8)
CHECKPOINT CP-9 commit=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f build=reused filter=/*/*/(RunnerSlotEndpointTests*)|(PhoneHomeDeferredKillTests*)|(RunnerSlotRulesTests*)/* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f sourceState=clean buildSource=verified
PHASES CP-9 slotWait=0s build=0s startup=43.7759149s testsWall=16.8848085s teardown=1.439161s hostWall=62.0998844s
CHECKPOINT CP-10 commit=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f build=reused filter=/*/*/(TaskParkPublicationTests*)|(TaskParkRunnerIdentityTests*)|(BlockedTaskParkProjectionTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=0s startup=45.8934525s testsWall=26.0910998s teardown=1.3630078s hostWall=73.34756s
CHECKPOINT CP-11 commit=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f build=reused filter=/*/*/(DispatcherSweepLifetimeTests*)|(DispatcherSweepLifetimeRegistrationTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f sourceState=clean buildSource=verified
PHASES CP-11 slotWait=0s build=0s startup=41.7256377s testsWall=9.7070053s teardown=1.2254158s hostWall=52.6580583s
CHECKPOINT CP-101 commit=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f build=reused filter=/*/*/TerminalRunnerSeatReleaseTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/rows/CP-101/run.trx slot=granted waited=0s dirty=0 source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f sourceState=clean buildSource=verified
PHASES CP-101 slotWait=0s build=0s startup=41.4736973s testsWall=138.083566s teardown=2.0786137s hostWall=181.635877s
SLOW CLASS Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests 138s tests=39 (CP-101)
CHECKPOINT CP-102 commit=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f build=reused filter=/*/*/(BlockedTaskParkReleaseTests*)|(BlockedTaskSyncRecoveryTests*)|(BlockedTaskParkResumeTests*)|(BlockedTaskParkDeliveryTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/rows/CP-102/run.trx slot=granted waited=0s dirty=0 source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f sourceState=clean buildSource=verified
PHASES CP-102 slotWait=0s build=0s startup=44.240595s testsWall=262.25001s teardown=1.9536966s hostWall=308.4443017s
SLOW CLASS Antiphon.Tests.Application.BlockedTaskParkReleaseTests 95s tests=3 (CP-102)
SLOW CLASS Antiphon.Tests.Application.BlockedTaskParkDeliveryTests 91s tests=4 (CP-102)
CHECKPOINT CP-103 commit=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f build=reused filter=/*/*/RunnerSeatOrphanSweepTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/rows/CP-103/run.trx slot=granted waited=0s dirty=0 source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f sourceState=clean buildSource=verified
PHASES CP-103 slotWait=0s build=0s startup=41.5927273s testsWall=67.1583439s teardown=1.4965763s hostWall=110.2476474s
SLOW CLASS Antiphon.Tests.Application.RunnerSeatOrphanSweepTests 67s tests=17 (CP-103)
CHECKPOINT CP-104 commit=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/rows/CP-104/run.trx slot=granted waited=0s dirty=0 source=ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f sourceState=clean buildSource=verified
PHASES CP-104 slotWait=0s build=0s startup=4.1996362s testsWall=2.1808884s teardown=0.6400386s hostWall=7.0205632s
unlisted: none (the tool ran no other build or test command)
wall: 21m11s  sequential-equivalent: 18m45s  builds: 1  max-concurrent-builds: 1  rows: 10 green 0 red 0 skipped
outputs: deleted bin-c1108f-cp6/
evidence: /work/worktrees/task-e77ff54b/.antiphon/review-e77ff54b-checkpoints/20261007-105216-aa9d/report.md
verdict: GREEN exit=0

--- review evidence ---
subjectTaskId: ec23e008-402d-4d14-9f61-aab1d7e20a74
reviewedSourceSha: ac1d5c7477d6ec792bd869385b21d9a6d3d06f6f
reviewedSourceClean: true
ordinaryScopeCompleted: Full

--- next stage ---
next: code
handoff: Fix F1/CARD-1145: current-run confirmations are omitted on backward UTC; identify actual per-run confirmation outcomes and cover rollback/equal timestamps without extra statements. Preserve Code owner ec23e008-402d-4d14-9f61-aab1d7e20a74. Coordinate S6b's Held/Known-limits doc conflict. Then rerun Final Review. PCs stay pending.
artifact: docs/superpowers/plans/2026-10-07-card-1108-1124-followups-plan.md
