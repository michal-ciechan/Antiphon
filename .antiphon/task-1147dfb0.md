# CARD-1253 F1/F2 repair — Code 1147dfb0

Fixed both findings from Review 3d019d19. The 14 new regression cases failed on unchanged
production; the final commissioned selection passed 158 cases, zero failures/skips.

## Identity and publication

- Task: 1147dfb0-d8a9-4147-a8ea-bdb87126d568.
- Original Code task / landing owner: f3a5b136-0402-405d-88cd-4b3875b5d366.
- Previous repair task: bb848929-c0b9-4862-94fe-b1929ed9c255.
- Branch: feat/card-task-1147dfb0.
- Worktree: /work/worktrees/task-1147dfb0.
- Assigned production baseline: b452f633d6d1e986bfa6c6dd709110fcf006f413.
- Tests-only red SHA: 3189e5f0f774f92e39a1de81777c9a783dd2722c.
- Implementation slice: 39a853d7e4f91c860e97c31dc34e46154b603ea8.
- Merge and actual green tested SHA: 67b9c85724a73752e32411c2d0e385475a031036.
- Fetched/merged origin/master: ba774271a61870261e86fb39a6fc0614fb826d53.
- Plan: docs/superpowers/plans/2026-10-14-card-1253-cascade-foreign-keys.md.
- This report and the plan outcome are the subsequent Markdown-only evidence slice. The
  final caller receipt supplies its pushed SHA; the checkpoint source remains the tested SHA above.

The three source slices were committed and pushed individually. The master merge had no
conflicts. No rebase, amend, force-push, land, deployment or service restart was performed.
No human decision was needed; work continued under the caller's standing authority.

## Repair and its boundary

F1 uses the smaller atomic-delete alternative from the Review: the initial locked nudge
check remains, followed later by DELETE ... RETURNING of the rows actually removed. The same
hold guard checks those returned rows inside the caller transaction. A raced current-generation
hold throws the existing ConflictException and rolls back the deletion. This avoids a second
SELECT-to-DELETE insertion window. PostgreSQL row locks prevent a claim from updating a row
while that row is being deleted; the initial check retains the existing early refusal behavior.

V-7 schedules a separate connection's new comment/nudge insert and committed Attempting claim
after the initial check, before the nudge deletion. It checks the real queue hold before resuming
the cascade, then HTTP 409, restored card ownership, retained audit/nudge and the surviving
queue hold after rollback. Both board and project variants failed at the intended HTTP assertion
on baseline and passed after repair. No timing sleeps or production test hook were introduced.

F2 adds HoldingInCurrentGenerations in ExpectationHoldAudit. It combines the existing shared
holding-state expression with destination existence and SessionGeneration.Equal. The cascade
loads current destination generations; the composer gate and release route supply the session
generation they already read. Their subsequent transcript/screen and audited-release behavior
is retained. The guard remains conservative about transcript/screen evidence, as the API docs
now say; it excludes older-generation and missing-destination attempts. V-8 covers those two
conditions for the three holding states and both delete routes, including the empty/404 release
response that previously left deletion blocked. V-5 still covers current-generation refusal,
the authenticated release audit and successful deletion afterward.

Authored paths:
- server/Application/Services/ProjectCascade.cs
- server/Application/Services/ExpectationHoldAudit.cs
- server/Application/Services/SessionMessageQueueService.Expectations.cs
- tests/Antiphon.Tests/Application/ProjectCascadeForeignKeyTests.HoldRaces.cs
- docs/antiphon-api.md
- docs/superpowers/plans/2026-10-14-card-1253-cascade-foreign-keys.md
- .antiphon/task-1147dfb0.md

## Ordinary verification

Round: Final for the explicitly commissioned selection. The brief excludes the whole Unit
lane; R-3 is NOT RUN / DEFERRED, not passed. Its historical timeout, 19 inherited failures
and 37 unqualified Windows-only skips remain in the inherited plan/report. No full assembly
run was commissioned. The helper's gate callers are covered by the full direct-delivery class,
including Now, SendNow, idle flush, local-command poll and another watchdog nudge.

| Full class | Baseline passed/failed/skipped | Final passed/failed/skipped |
|---|---:|---:|
| ProjectCascadeForeignKeyTests | 27/14/0 | 41/0/0 |
| ProjectDeletionTests | not selected | 20/0/0 |
| BoardProjectArchiveApiTests | not selected | 2/0/0 |
| CardFilePrivacyOwnershipAcceptanceTests | not selected | 16/0/0 |
| SessionStateWarmupTests | not selected | 16/0/0 |
| CardFileBoardLookupTests | not selected | 45/0/0 |
| ExpectationDirectDeliveryTests | not selected | 14/0/0 |
| ExpectationHoldReleaseEndpointTests | not selected | 4/0/0 |

The two V-7 baseline failures expected Conflict but received OK/NoContent. The twelve V-8
baseline failures expected OK/NoContent but received Conflict. These were assertion failures,
not build, fixture or zero-selection errors. The tests-only commit has no server diff from
b452f633d. The final TRX identities were joined to TestDefinitions/TestMethod and inspected:
158 distinct executed identities, matching the eight class counts and their method rosters.

| ID | Actual outcome |
|---|---|
| V-1 | PASS: 2 scoped nudge deletion cases |
| V-2 | PASS: 2 session-keyed question deletion cases |
| V-3 | PASS: 4 rollback cases |
| V-4 | PASS: 2 history/cleanup cases and 1 fifteen-link inventory case |
| V-5 | PASS: 6 current-generation refusal/release/deletion cases |
| V-6 | PASS: 10 non-holding/scoping cases |
| V-7 | RED 2/2 on baseline; PASS 2/2 after repair |
| V-8 | RED 12/12 on baseline; PASS 12/12 after repair |
| R-1 | PASS: ProjectDeletionTests 20 |
| R-2 | PASS: archive API 2 and card-file privacy/ownership 16 |
| R-3 | NOT RUN / DEFERRED by the explicit brief |
| R-4 | PASS: session-state warmup 16 and board lookup 45 |
| R-5 | PASS: direct delivery 14 and release endpoint 4 |
| M-1 | PASS: model snapshot scan found the same 15 Restrict links to the seven named parents; runtime inventory passed |
| M-2 | Pre-report PASS: full-range evidence guard, remote-ref equality, clean tree, merge-tree exit 0; final post-report checks are supplied in the caller receipt |

## Execution, provenance and cleanup

GET /api/runner-defaults, GET /api/session-runners and GET /api/version were read using the
session's configured API endpoint. No Runner or Platform placement override was supplied.

The checkpoint tool executed one build per committed source group, with Linux UseAppHost=false,
isolated bin-1147dfb0/ output, serial rows, expected committed SHA and no keep-outputs option.
The commissioned effective limits were row-timeout 40m and total-timeout 150m. The final
imported manifest also records rowMinutes 40. No assertion was loosened and no timeout was
increased in response to a failure. No proof selection was repeated after green.

- Baseline run 20261014-143535-e009: CP-1, 41 executed, 27 passed, 14 expected failures.
  Build and row slot=granted waited=0s; source clean and build provenance verified.
- Final run 20261014-144305-5cd8: CP-1/CP-2/CP-4/CP-5, respectively 41/38/61/18 passed.
  One build, build and row slot=granted waited=0s. Recorded test/build wall: 10m42s;
  output cleanup completed afterward. Source clean, build provenance verified.
- Both the tool validate command and scripts/validate-checkpoint-receipt.ps1 exited 0:
  CHECKPOINT SOURCE VALID source=67b9c85724a73752e32411c2d0e385475a031036 rows=4.
- One unlisted build was necessary to bootstrap the checkpoint CLI: scripts/build-slot.ps1
  with OutputPath=bin-1147dfb0-tool/ and UseAppHost=false, slot=granted waited=0s, held=6s,
  zero errors (two existing tool warnings). No unlisted test driver ran. The fetched master
  did not change the tool, so a second bootstrap was unnecessary.
- The final executor recorded GREEN/exit 0. A separate wait initially returned 2 because
  its tool shadow could not be deleted under output-memory-authority-missing. After removing
  that literal task-created shadow path, wait returned 0/AlreadyAbsent. Tests were not rerun.
- Red-run output and shadow were removed before the final build; final build output was
  removed by the executor. The final tool shadow and bootstrap output were removed by literal
  paths. Only the ignored build-log/evidence directories named bin-1147dfb0 remain, not compiler
  outputs. No foreign output was removed. No run remains active.
- Full-range pre-report evidence check: 2a64db19bea886657526100ff92a6af4aad2f67f..67b9c8572,
  47 commits, 10 evidence entries, 0 violations. It is rerun over HEAD after this report is committed.
- Pre-report merge-tree --write-tree HEAD origin/master: exit 0,
  tree faa7eb57bc4ac7e4f0090898e708c4c13b2586ad. The final caller receipt reports the final-tip check.

Evidence roots (generated payloads remain ignored):
- /work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-143535-e009/report.json
- /work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-143535-e009/rows/CP-1/run.trx
- /work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-144305-5cd8/report.json
- /work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-144305-5cd8/rows/CP-1/run.trx
- /work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-144305-5cd8/rows/CP-2/run.trx
- /work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-144305-5cd8/rows/CP-4/run.trx
- /work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-144305-5cd8/rows/CP-5/run.trx
- /work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-144305-5cd8/inspected-roster.txt

Rerun ordinary verification by bootstrapping the CLI through build-slot.ps1, then running the
committed plan's CP-1,CP-2,CP-4,CP-5 rows with the new committed expected SHA,
--row-timeout 40m --total-timeout 150m --serial, without --keep-outputs, and waiting to completion.
The report's evidence SHA must remain the actual tested SHA rather than a later report-only tip.

## Pending SourceLanding Mutation

No deliberate mutation ran. Baseline regression failures and ordinary green do not discharge
positive controls. Mutation owns method-scoped deliberate mutants, red/restore/green and missing
control discovery. Candidate artifacts belong under docs/superpowers/mutations/.

| PC | Pending variants | Guard |
|---|---|---|
| PC-1 | omit/misorder nudge deletion; board/project | V-1 |
| PC-2 | broaden nudge scope; sibling/other-project | V-1 |
| PC-3 | omit/misorder question deletion; board/project | V-2 |
| PC-4 | select questions by task ownership; cross-project task | V-2 |
| PC-5 | broaden question scope; sibling/other-project | V-2 |
| PC-6 | dependent deletion outside caller transaction; nudge/question x board/project | V-3 |
| PC-7 | delete surviving task/history; board/project | V-4 |
| PC-8 | omit cleanup leaf ordering; endpoint/target/generation | V-4 |
| PC-9 | remove workflow safeguard; force-delete refusal | R-1 |
| PC-10 | omit refusal or a holding state; board/project x three states | V-5 |
| PC-11 | broaden blocker outside doomed audit comments; board/project | V-6 |
| PC-12 | treat Released as holding; release-followed-by-delete x board/project | V-5 |
| PC-13 | move existing-hold refusal after a destructive statement; board/project | V-5 |
| PC-14 | omit returned-row hold guard; board/project | V-7 |
| PC-15 | omit destination existence or generation matching; board/project x three states x stale/missing destination | V-8 |

Restart: server after landing; no runner restart. Next: Review. After Review, the caller lands
the original Code task f3a5b136-0402-405d-88cd-4b3875b5d366 and commissions SourceLanding Mutation.

## Unedited checkpoint lines

```text
CHECKPOINT CP-1 commit=3189e5f0f774f92e39a1de81777c9a783dd2722c build=ok filter=/*/*/(ProjectCascadeForeignKeyTests*)/* executed=41 passed=27 failed=14 skipped=0 trx=/work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-143535-e009/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=3189e5f0f774f92e39a1de81777c9a783dd2722c sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=186.4746533s startup=39.8654157s testsWall=64.0393718s teardown=4.2906981s hostWall=108.1954957s
CHECKPOINT CP-1 commit=67b9c85724a73752e32411c2d0e385475a031036 build=ok filter=/*/*/(ProjectCascadeForeignKeyTests*)/* executed=41 passed=41 failed=0 skipped=0 trx=/work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-144305-5cd8/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=67b9c85724a73752e32411c2d0e385475a031036 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=267.5062715s startup=44.0262322s testsWall=38.9370413s teardown=3.1233868s hostWall=86.0869316s
CHECKPOINT CP-2 commit=67b9c85724a73752e32411c2d0e385475a031036 build=reused filter=/*/*/(ProjectDeletionTests*)|(BoardProjectArchiveApiTests*)|(CardFilePrivacyOwnershipAcceptanceTests*)/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-144305-5cd8/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=67b9c85724a73752e32411c2d0e385475a031036 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=45.6494224s testsWall=15.774752s teardown=2.7826244s hostWall=64.2067987s
CHECKPOINT CP-4 commit=67b9c85724a73752e32411c2d0e385475a031036 build=reused filter=/*/*/(SessionStateWarmupTests*)|(CardFileBoardLookupTests*)/* executed=61 passed=61 failed=0 skipped=0 trx=/work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-144305-5cd8/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=67b9c85724a73752e32411c2d0e385475a031036 sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=0s startup=47.81815s testsWall=25.8842631s teardown=2.4810214s hostWall=76.1834344s
CHECKPOINT CP-5 commit=67b9c85724a73752e32411c2d0e385475a031036 build=reused filter=/*/*/(ExpectationDirectDeliveryTests*)|(ExpectationHoldReleaseEndpointTests*)/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-1147dfb0/.antiphon/checkpoints/20261014-144305-5cd8/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=67b9c85724a73752e32411c2d0e385475a031036 sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=0s startup=44.5887845s testsWall=79.6904373s teardown=7.1966994s hostWall=131.475921s
```
