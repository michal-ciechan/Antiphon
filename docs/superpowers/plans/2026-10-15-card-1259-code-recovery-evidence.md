# CARD-1259 continuation d636249e — execution record

Status: incomplete within the 90-minute continuation budget; pushed recovery
implementation and partial routing qualification are green. S2-S4 and Final
remain open. Next: Code, not Review or landing.

Qualified source: 9d3a6e79d7129e0958afa020d2ad2debc3021a99.
Run 20261015-203303-8643 completed exit 0: CP-15 6/6, CP-16 146/146,
CP-18 14/14, CP-19 334/334; total 500 passed, zero failed/skipped. Fresh TRX
class identities, all recovery/routing argument names and the repaired conversion
method were inspected. Report validation returned exit 0 and
`CHECKPOINT SOURCE VALID source=9d3a6e79d7129e0958afa020d2ad2debc3021a99 rows=4`.
All rows: slot=granted, waited=0s, dirty=0, sourceState=clean,
buildSource=verified. One build cost 376.868s; test hosts cost 506.141s in total;
the tool reports wall 15m05s plus its foreground cleanup wait. The executor and
foreground wait both completed; its owned test output and tool copy were removed.
The following commit adds only this narrative evidence; it is not the tested SHA.

Authoritative plan: docs/superpowers/plans/2026-10-15-card-1259-transcript-pump-decoupling-plan.md.
Main V/R and checkpoint binding: docs/superpowers/plans/2026-10-15-card-1259-transcript-pump-verification.md.
Supplement: docs/superpowers/plans/2026-10-15-card-1259-recovery-checkpoints.md.
Published evidence: docs/superpowers/plans/2026-10-15-card-1259-code-recovery-evidence.md.

Original Code / landing owner: eddab1c0-8f84-44bf-b222-fdeabf425004.
Continuation: d636249e-864c-4e97-9ada-f089f887f6d4.
Assigned branch: feat/card-task-d636249e.
Worktree: /work/worktrees/task-d636249e.
Task base: 41289ba905c64a99a12bd42369fbaa74987bfdc3.

The explicit CARD-1287 instruction in this brief overrides the generic Final
template: no whole Unit lane ran. No full assembly ran. Runner defaults and the
session-runner catalogue were read; neither Runner nor Platform was pinned.
Every checkpoint used the authorized `env -u ANTIPHON_TASK_ID` workaround for the
missing task token. Every actual build/test driver used the host slot gate.

## Implemented production changes

- Deferred actions snapshot task ID and attempt with the accepted session
  generation. Task judgement uses the stored turn/frontier. The bound task scope
  rechecks identity under SQL session/task locks at every asynchronous save;
  catch-up failures reach the durable retry owner. Shared settlement fallback
  also checks attempt and session before continuing publication.
- Direct channel publication stores its original route, owning prompt, accepted
  watermark and pending range on the existing source queue rows. Restart can send
  late text to its original recipient. A completed failed send releases only its
  own claim. Acceptance followed by failed acknowledgement remains uncertain and
  is retained without replay; this makes no exactly-once remote-receipt claim.
- Queue retention now uses the transcript mutation lease and protects open direct
  tail evidence. Worker continuation hints are offered after releasing ownership.
- Worker diagnostics record identity, debt count, age, frontier, phase and failure
  type without logging transcript bodies or exception messages.
- EF CLI migration: 20261015195037_RuntimeActionAttemptAndDirectTail; nine nullable
  columns, no legacy identity backfill. Runtime invariant documentation updated.

## Test implementation and attribution

Six recovery cases first reproduced all four defects on unchanged production:
two attempt-fence cases, two direct-tail cases, one actual queue-delete lease
case and one diagnostics case. The first retention oracle relied on latency and
passed incorrectly; it was replaced with a warmed-cache synchronous lease
observation and reproduced the defect. A later diagnostics subcase injects a
private exception through the real UI phase and checks retained debt and safe logs.

Six older channel failures were independently reproduced at exact base 41289ba9
in /tmp/card1259-base-d636249e. They expected routing immediately from ingestion.
The fixture repair drains the durable action worker and preserves every original
receipt/publication assertion. The full adjacent class selection then passed
146/146 (28 lifecycle, 25 durability, 20 late-confirm, 73 retention).

The first eight review routing cases passed 7/8. The incomplete-body fixture used
a 100-character prefix, shorter than the unchanged 200-character identity
window, and got NoTranscriptRecord. Its repair uses 250 characters and explicitly
proves identity plus incompleteness before asserting Truncated. No assertion or
timeout was loosened. The repaired review cases and four initial V-6 eligibility
cases then passed 12/12. V-6 now also implements the task-attempt and held-flush
cases, plus the capped-attempt subcase; the final 14-case routing row passed at
9d3a6e79d7129e0958afa020d2ad2debc3021a99. Overall receipt validation passed.

The full task-reply class expands to 334 cases in TRX, rather than the initial
static estimate of 182. Its single failing conversion fixture was reproduced at
the exact task base by CP-20, reusing the SHA-verified base build. The repair
awaits ingestion and then the durable action worker, preserving the converter
hold, full recipient receipt and unchanged five-second release budget.

## Historical execution outcomes and costs

All listed executed rows had slot=granted, waited=0s and clean source with verified
build provenance unless stated otherwise. A passing row in a red run is recorded
as a passing execution, not a qualified overall receipt. The validator explicitly
rejects the mixed-result CP-15 and CP-16 reports with cleanup_refused.

| Run | SHA | Outcome | Build / host / wall |
|---|---|---|---|
| 20261015-193056-c712 | 79a796697ea38c269d3a86345d5a6049949ae94e | CP-14 compile failure: missing DTO import; zero executions; buildSource unknown, row slot skipped | wall 2m28s |
| 20261015-193419-fb41 | 849a4f36998e52b72f014377346033efe7a3e044 | CP-14 1 passed / 5 intended failures; weak retention oracle not accepted as proof | 214.331s / 70.999s / 4m59s |
| 20261015-194016-5b78 | ae47bb14739c0c21d50b00dc32fbfdfd8af5d04b | CP-14 0 passed / 6 intended assertion failures | 279.208s / 61.304s / 5m55s |
| 20261015-195227-6b3e | 975e1616dbbabbc043c73bc96eba888ea7e0e892 | CP-15 6/6; CP-16 140/146, six inherited failures; overall red/cleanup_refused | 204.144s / 57.978s + 130.330s / 6m55s |
| 20261015-200005-aab6 (base checkout) | 41289ba905c64a99a12bd42369fbaa74987bfdc3 | CP-17 exactly the six older failures, 0/6; overall red/cleanup_refused | 205.406s / 51.172s / 4m30s |
| 20261015-200554-57bb | d5cfc46b98eda815fee53b0c114d4824f635d398 | CP-16 146/146; CP-18 7/8, one fixture failure; overall red/cleanup_refused | 265.517s / 136.036s + 84.900s / 8m32s |
| 20261015-201659-fc1f | edaf7eddb5c3ea890c0af88292144c0320b84a80 | CP-15 6/6; CP-18 12/12; CP-19 333/334, one inherited fixture failure; overall red/cleanup_refused | 225.293s / 79.345s + 75.908s + 228.012s / 10m34s |
| CP-20-20261015-202808-2d28 (base checkout) | 41289ba905c64a99a12bd42369fbaa74987bfdc3 | 0/1, exact inherited conversion assertion; reused verified base build | no build; see row report/TRX |
| 20261015-202929-bf2c | 6ebff7673391caf85774f4317ad782fa7038e77a | stopped intentionally during build to correct the new held-flush fixture's turn-completion sequence; zero test executions, no proof receipt | about 2m50s wall; slot granted/waited=0s |

Additional gated builds were necessary for checkpoint bootstrap and the required
EF migration. Bootstrap: success, 2 warnings/0 errors, slot waited=0s/held=25s.
Initial EF build failed (held=60s); a diagnostic server build identified the
three-value tuple deconstruction error (19 warnings/2 errors, waited=0s,
held=36s). The repaired EF run succeeded (waited=0s/held=57s). EF tools 9.0.20
versus runtime 9.0.21 emitted the existing warning. `dotnet tool restore` restored
the pinned EF tool. These were not hidden ordinary test selections.

CLI refusals before any build/test: unsupported --manifest spelling, unknown
import-plan verb, a base results root outside its checkout, and a shared build
assigned different After values. Corrected invocations retained host-slot and
source checks; no unleased fallback or retry relaxation was used.

The stopped run was terminated through the checkpoint tool and its foreground
wait was joined (exit 6, phase=stopped). Tool-copy cleanup first reported IOException;
the subsequent wait proved executor death and removed the exact owned copy.
The process check found no remaining process for that run/output. The fixture now
completes the second actual turn before demanding acknowledgement of retained idle
debt; the receipt and debt-removal assertions remain intact. Its replacement run
is 20261015-203303-8643 at 9d3a6e79d7129e0958afa020d2ad2debc3021a99.

## Remaining Final gate (not passed)

Production findings from code inspection, not yet reproduced by a new test:
`ChannelReplyDispatcher.DispatchFollowUpAsync` takes the oldest 128 direct roots
and skips uncertain roots inside that page. An uncertain prefix of 128 roots can
therefore starve a later recoverable root on repeated scans. Direct tails also
close only at a later opening prompt; unlike the existing unified outbound path,
the new direct path does not yet apply terminal catch-up closure. A final tail
can consequently keep its session/correlation protected from retention. The next
Code slice must reproduce and resolve these two remaining direct-recovery edges
before treating the four assigned production gaps as completely closed.

V-1, V-2: predecessor seven-case behavior proof; final CP-4 pending here.
V-3: 7/7 named executions pass in CP-16; actual-save cancellation and real
unique-index migration replay remain incomplete.
V-4: 5/5 named executions pass in CP-16; peak simultaneous-writer instrumentation
and the full overlapping-write matrix remain incomplete.
V-8: 5/5 named executions pass in CP-16; more than 128 same-session actions,
execution-page fairness and separately proven page-two yield remain incomplete.
V-9: 6/6 named executions pass in CP-16; full reconnect/scope/seat counters and
separate queue/review/channel effect-before-ack cuts remain incomplete.
V-10: 5/5 named executions pass in CP-16, plus the new real queue-delete lease
case passes in CP-15; every effect/save fault, four-slot poison/recovery and all
separate transcript/session pruning cuts remain incomplete.
V-5: all eight routing variants and parent busy/eligible H0-H4 remain pending.
V-6: all six variants and the capped-attempt subcase implemented and passing in
CP-18 at the current source. The separate bound/catch-up attempt-fence cases also
pass in CP-15. The common Final CP-6 remains pending.
V-7: all five variants and manual recovery busy/eligible H0-H4 remain pending.
V-11: eight variants implemented; legacy real migration, enqueue-failure H0-H4,
post-commit/action-ack cut and explicit fresh redispatch remain pending.
V-12: all six named variants remain pending.
R-1, R-2, R-4, R-5, R-6: final named rows not run here.
R-3: its eight methods executed in the full 20-case late-confirm class; CP-9 at
the common final SHA remains pending.
CP-3 through CP-13 have not been represented as passed. The main manifest order
and 35-case routing minimum remain unchanged. No required manual or operational
acceptance has been claimed by this continuation.

Every PC-1 through PC-113 and every variant remains pending method-scoped
SourceLanding Mutation, including missing-control discovery. No deliberate
mutants ran. The eventual unbound candidate belongs under
docs/superpowers/mutations/; this incomplete continuation does not publish a
completed candidate pack.
Restart after eventual landing: server. No server or runner restart occurred.
Next remains Code until implementation and ordinary V/R are complete, then
ordinary Review; caller subsequently lands the original Code task and commissions
SourceLanding Mutation.

Source-reader audit found only path-string fixtures in ScopeDriftTests and
DelegationUnitTests, plus transcript data, not classes that read the changed
SessionRunnerEventPump/AgentSessionRuntime/SessionMessageQueueService files.

## Essential unedited receipts

The following section is appended directly from the tool-produced reports.

Source report: /work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-193056-c712/report.md
CHECKPOINT CP-14 commit=79a796697ea38c269d3a86345d5a6049949ae94e build=failed filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=79a796697ea38c269d3a86345d5a6049949ae94e sourceState=clean buildSource=unknown

Source report: /work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-193419-fb41/report.md
CHECKPOINT CP-14 commit=849a4f36998e52b72f014377346033efe7a3e044 build=ok filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/* executed=6 passed=1 failed=5 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-193419-fb41/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=849a4f36998e52b72f014377346033efe7a3e044 sourceState=clean buildSource=verified
PHASES CP-14 slotWait=0s build=214.3310521s startup=52.2612548s testsWall=11.3469572s teardown=7.3905358s hostWall=70.998671s

Source report: /work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-194016-5b78/report.md
CHECKPOINT CP-14 commit=ae47bb14739c0c21d50b00dc32fbfdfd8af5d04b build=ok filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/* executed=6 passed=0 failed=6 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-194016-5b78/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=ae47bb14739c0c21d50b00dc32fbfdfd8af5d04b sourceState=clean buildSource=verified
PHASES CP-14 slotWait=0s build=279.2082168s startup=50.5497486s testsWall=8.0901611s teardown=2.6638691s hostWall=61.3038014s

Source report: /work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-195227-6b3e/report.md
CHECKPOINT CP-15 commit=975e1616dbbabbc043c73bc96eba888ea7e0e892 build=ok filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-195227-6b3e/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=975e1616dbbabbc043c73bc96eba888ea7e0e892 sourceState=clean buildSource=verified
PHASES CP-15 slotWait=0s build=204.1441147s startup=45.595187s testsWall=9.2905532s teardown=3.0924519s hostWall=57.9781853s
CHECKPOINT CP-16 commit=975e1616dbbabbc043c73bc96eba888ea7e0e892 build=reused filter=/*/Antiphon.Tests.Application/(SessionRuntimeActionTests*)|(ChannelReplyLateConfirmDispatchTests*)|(ChannelReplyDurabilityTests*)|(DataRetentionServiceTests*)/* executed=146 passed=140 failed=6 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-195227-6b3e/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=975e1616dbbabbc043c73bc96eba888ea7e0e892 sourceState=clean buildSource=verified
PHASES CP-16 slotWait=0s build=0s startup=50.4944113s testsWall=76.4476907s teardown=3.3879771s hostWall=130.3300793s

Source report: /work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-200554-57bb/report.md
CHECKPOINT CP-16 commit=d5cfc46b98eda815fee53b0c114d4824f635d398 build=ok filter=/*/Antiphon.Tests.Application/(SessionRuntimeActionTests*)|(ChannelReplyLateConfirmDispatchTests*)|(ChannelReplyDurabilityTests*)|(DataRetentionServiceTests*)/* executed=146 passed=146 failed=0 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-200554-57bb/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=d5cfc46b98eda815fee53b0c114d4824f635d398 sourceState=clean buildSource=verified
PHASES CP-16 slotWait=0s build=265.5167531s startup=55.47889s testsWall=77.0176654s teardown=3.5394265s hostWall=136.0359797s
CHECKPOINT CP-18 commit=d5cfc46b98eda815fee53b0c114d4824f635d398 build=reused filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/* executed=8 passed=7 failed=1 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-200554-57bb/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=d5cfc46b98eda815fee53b0c114d4824f635d398 sourceState=clean buildSource=verified
PHASES CP-18 slotWait=0s build=0s startup=48.490593s testsWall=33.4001904s teardown=3.0088812s hostWall=84.8996644s

Source report: /tmp/card1259-base-d636249e/.antiphon/checkpoints/20261015-200005-aab6/report.md
CHECKPOINT CP-17 commit=41289ba905c64a99a12bd42369fbaa74987bfdc3 build=ok filter=/*/Antiphon.Tests.Application/(ChannelReplyLateConfirmDispatchTests*)|(ChannelReplyDurabilityTests*)/(The_measured_transport_fixture_dispatches_the_error_through_the_runtime*)|(A_late_confirmed_api_withheld_turn_does_not_warn_as_an_unpublished_reply*)|(A_late_confirmed_no_reply_turn_does_not_warn_as_an_unpublished_reply*)|(A_runtime_batch_redispatches_a_late_confirmed_channel_reply_once*)|(A_failed_late_confirm_publication_is_durably_uncertain_and_leaves_it_owed*)|(A_reply_the_live_path_already_published_is_not_published_again_by_the_grace_confirm*) executed=6 passed=0 failed=6 skipped=0 trx=/tmp/card1259-base-d636249e/.antiphon/checkpoints/20261015-200005-aab6/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=41289ba905c64a99a12bd42369fbaa74987bfdc3 sourceState=clean buildSource=verified
PHASES CP-17 slotWait=0s build=205.405859s startup=43.2930927s testsWall=6.1321894s teardown=1.7470815s hostWall=51.1723693s

Source report: .antiphon/checkpoints/20261015-201659-fc1f/report.md
CHECKPOINT CP-15 commit=edaf7eddb5c3ea890c0af88292144c0320b84a80 build=ok filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-201659-fc1f/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=edaf7eddb5c3ea890c0af88292144c0320b84a80 sourceState=clean buildSource=verified
PHASES CP-15 slotWait=0s build=225.2931375s startup=65.4727306s testsWall=8.6921429s teardown=5.1805055s hostWall=79.3454215s
CHECKPOINT CP-18 commit=edaf7eddb5c3ea890c0af88292144c0320b84a80 build=reused filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-201659-fc1f/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=edaf7eddb5c3ea890c0af88292144c0320b84a80 sourceState=clean buildSource=verified
PHASES CP-18 slotWait=0s build=0s startup=43.7668059s testsWall=29.4945539s teardown=2.6467316s hostWall=75.9080914s
CHECKPOINT CP-19 commit=edaf7eddb5c3ea890c0af88292144c0320b84a80 build=reused filter=/*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/* executed=334 passed=333 failed=1 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-201659-fc1f/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=edaf7eddb5c3ea890c0af88292144c0320b84a80 sourceState=clean buildSource=verified
PHASES CP-19 slotWait=0s build=0s startup=48.0893197s testsWall=177.4856084s teardown=2.4372599s hostWall=228.0121874s

Source report: /tmp/card1259-base-d636249e/.antiphon/checkpoints/CP-20-20261015-202808-2d28/report.md
CHECKPOINT CP-20 commit=41289ba905c64a99a12bd42369fbaa74987bfdc3 build=reused filter=/*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/Deferred_is_durable_and_releases_runtime executed=1 passed=0 failed=1 skipped=0 trx=/tmp/card1259-base-d636249e/.antiphon/checkpoints/CP-20-20261015-202808-2d28/run.trx slot=granted waited=0s dirty=0 source=41289ba905c64a99a12bd42369fbaa74987bfdc3 sourceState=clean buildSource=verified

Source report: .antiphon/checkpoints/20261015-203303-8643/report.md
CHECKPOINT CP-15 commit=9d3a6e79d7129e0958afa020d2ad2debc3021a99 build=ok filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-203303-8643/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=9d3a6e79d7129e0958afa020d2ad2debc3021a99 sourceState=clean buildSource=verified
PHASES CP-15 slotWait=0s build=376.8678244s startup=43.731132s testsWall=8.693882s teardown=3.3181759s hostWall=55.7431796s
CHECKPOINT CP-16 commit=9d3a6e79d7129e0958afa020d2ad2debc3021a99 build=reused filter=/*/Antiphon.Tests.Application/(SessionRuntimeActionTests*)|(ChannelReplyLateConfirmDispatchTests*)|(ChannelReplyDurabilityTests*)|(DataRetentionServiceTests*)/* executed=146 passed=146 failed=0 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-203303-8643/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=9d3a6e79d7129e0958afa020d2ad2debc3021a99 sourceState=clean buildSource=verified
PHASES CP-16 slotWait=0s build=0s startup=42.9101907s testsWall=72.3298703s teardown=4.2615842s hostWall=119.5016448s
CHECKPOINT CP-18 commit=9d3a6e79d7129e0958afa020d2ad2debc3021a99 build=reused filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-203303-8643/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=9d3a6e79d7129e0958afa020d2ad2debc3021a99 sourceState=clean buildSource=verified
PHASES CP-18 slotWait=0s build=0s startup=44.1141648s testsWall=45.4563208s teardown=2.6273875s hostWall=92.1978732s
CHECKPOINT CP-19 commit=9d3a6e79d7129e0958afa020d2ad2debc3021a99 build=reused filter=/*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/* executed=334 passed=334 failed=0 skipped=0 trx=/work/worktrees/task-d636249e/.antiphon/checkpoints/20261015-203303-8643/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=9d3a6e79d7129e0958afa020d2ad2debc3021a99 sourceState=clean buildSource=verified
PHASES CP-19 slotWait=0s build=0s startup=58.7431548s testsWall=175.9349885s teardown=4.0200396s hostWall=238.6981825s
