# CARD-0667 S4b Code report — incomplete, next Code

Dormant integration is pushed, but S4b is not ready for Review: CP-18 is red, one rewritten endpoint fixture is red, and source review found an uncovered destructive arm. The brief's two repair rounds are exhausted. No timeout or assertion was relaxed, and no activation or S4c work was performed.

Original Code task / landing owner: `8eed758c-eec5-4f96-a7bc-fc97381b05b8`.
Branch: `feat/card-task-8eed758c`.
Worktree: `/work/worktrees/task-8eed758c`.
Assigned desktop mirror: `C:\Antiphon\worktrees\card-task-8eed758c`, not accessed.
Task base: `f329f5d0c0641117abf556b6bee0d9e8966ba9ff`.
Actual final tested code/test source: `f98af19fba6760ce8a38e6541a1eae4a518722c8`.
This report is a subsequent report-only commit; its pushed SHA is supplied in the final message. Earlier receipts are not relabeled.
Plan: `docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`.
Round: Final for the explicitly commissioned S4b scope. The specific brief excludes the whole Unit lane and S4c; this is not card-wide final qualification.

## Changes and remaining work

The existing slot-reconcile job now performs legacy reconciliation, attention recovery and guarded discovery. Dispatcher recovery shares a singleton bounded cursor (32 candidates, four per runner page). Settlement uses a fresh coordinator scope after persistence; completed remote Worktree Blocked reports enter the release path. Cancellation performs its existing requested stop, then invokes conditional reconciliation. The remote Worktree janitor retains conditional holds. The operator orphan sweep uses the conditional coordinator and returns candidates/deferred/dispositions; it cannot invoke the old force-release sweep. Explicit single-seat operator release retains its existing contract. `AutomaticEnabled=false` remains the default. No timer, migration, protocol change or activation was introduced.

Production changes are in `server/Application/Services/{TerminalRunnerSeatReleaseService,AgentTaskReplyService,AgentTaskService,AgentTaskDispatcher,RunnerSlotService}.cs`, `server/Infrastructure/Agents/SessionRunner/RunnerSlotReconcileJob.cs`, `server/Api/Endpoints/SessionRunnerEndpoints.cs`, `server/Application/Dtos/RunnerSlotDtos.cs` and `server/Program.cs`. Tests/fixtures are the four admitted Application files. The three owner docs and plan contain the dormant contract.

The next Code commission must resolve these before Review:

1. `TerminalRunnerSeatReleaseTests.Blocked_report_with_running_runner_frees_the_seat`, line 86: the genuine runner-sync settlement is Blocked but `FailureReason` is null. Production records the sync explanation in `NextHandoff` and a Warning event (`AgentTaskReplyService.cs` around lines 904 and 944). Assert the real Decide handoff and exact sync warning, preserving the existing release, stopped-row, custody and retained-workspace assertions. Do not add a production `FailureReason` write just to satisfy the mistaken fixture. The ordinary marked variant passed before the sync variant stopped this method; the method as a whole failed.
2. `RunnerSlotEndpointTests.Release_orphans_skips_a_seat_claimed_after_the_list`, line 319: its new use of `SeedOpenTaskAsync` creates Working while the retained assertion requires Dispatched. Also, `SeedRunningSessionAsync` stamps runner `grok-linux`, while this real-runtime fixture routes `fixture`; that mismatch can mask the intended ownership guard. Seed a matching runner identity and the intended Dispatched claimant, then assert the ownership disposition as well as zero release/force calls. The unchanged method at the task base passed 1/1, proving the current failure is introduced by this rewrite.
3. D-1 source coverage gap: `TerminalRunnerSeatReleaseService.OwnsAutomaticPath` currently restricts interception to Worktree tasks. An unpooled remote Shared task can still reach the legacy stopper in `ReleaseDelegateAsync` (and the pool-release pass). D-1 protects intentional warm pooling and local compatibility, but requires guarding the runner-bound destructive arm. Extend the guard at that boundary and add a variant that reaches unpooled remote Shared retirement with otherwise valid identity and Working/unsupported evidence. Preserve intentional warm Shared and local behavior. This is a source finding, not an executed mutant or a production incident.

## Ordinary results

All final-source rows below used `f98af19fba6760ce8a38e6541a1eae4a518722c8`, `dirty=0`, `sourceState=clean`, `buildSource=verified`, `slot=granted`, `waited=0s`. Red rows are not valid passing certificates: receipt validation returned `row_failed` for CP-18 and `receipt_failed` for the server class run. The runner receipt validated successfully.

| Check | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-18 exact eight methods | 8 | 7 | 1 | 0 |
| Full affected server classes | 725 | 723 | 2 | 0 |
| Full runner TerminalSeatReleaseTests | 38 | 38 | 0 | 0 |
| Existing claim-race method at base f329f5d0c | 1 | 1 | 0 | 0 |

Fresh TRX class identities and nonzero counts were inspected, not inferred from exit status:

| Full server class | Passed / executed |
|---|---:|
| AgentTaskPoolTests | 48 / 48 |
| AgentTaskReplyIntegrationTests | 334 / 334 |
| AgentTaskServiceIntegrationTests | 120 / 120 |
| AttentionApiTests | 2 / 2 |
| AttentionServiceTests | 164 / 164 |
| RunnerSeatOrphanSweepTests | 17 / 17 |
| RunnerSlotEndpointTests | 7 / 8 |
| RunnerSlotRulesTests | 2 / 2 |
| TerminalRunnerSeatReleaseTests | 29 / 30 |

CP-18 method outcomes: Working_session_keeps_ownership_and_visible_debt PASS; Janitor_cannot_bypass_a_release_hold PASS; Cancellation_reconciles_without_second_stop PASS; Settlement_delivery_precedes_release_and_survives_release_fault PASS; Parent_receipt_rejects_ack_stale_or_partial_prompt PASS; Blocked_report_with_running_runner_frees_the_seat FAIL; Failed_settlement_releases_without_success_branch PASS; Existing_job_discovers_debt_without_settlement_callback PASS.

| Plan ID | Actual ordinary outcome |
|---|---|
| V-1 | Full runner class: 38 passed. No live-provider/native-process qualification claim. |
| V-2 | Full lifecycle/release class: 29 passed, one failed. CP-18's sync-Blocked branch remains incomplete. |
| V-3 | Full orphan/discovery class: 17 passed, including actual job/default-off/legacy reconciliation. |
| R-1 | Classifier/capacity roster not run; deferred to S4c. |
| R-2 | Endpoint/rules roster: nine passed, one failed; claim-race fixture remains incomplete. |
| R-3 | All four named warm Shared/standing/local Blocked compatibility methods passed, individually confirmed in fresh TRX; full reply class also passed. |
| R-4 | Windows qualification not run; deferred to S4c. |

Manual/source checks: default false and conditional no-force behavior reviewed; no migration/model-snapshot diff; no new scheduler; no daemon restart, deployment or live provider launch; retained report/workspace and complete parent receipt exercised by ordinary tests. Manual D-1 review identified remaining item 3 above, so overall acceptance is incomplete. GET `/api/runner-defaults` and `/api/session-runners` both succeeded before implementation (defaults revision 2); no fleet location was embedded or routing setting changed.

Deferred IDs, never claimed passed: remaining CP-18 repair, R-2 claim-race repair, S4c CP-1 through CP-6, R-1 and R-4, separate activation acceptance, and every PC below. Ordinary overlap with a future checkpoint does not discharge that future checkpoint.

## Mutation remains pending

All PC-1 through PC-104 remain pending for method-scoped SourceLanding Mutation. No deliberate mutants or red/restore/green cycles were run; ordinary failures are not positive-control evidence. S4b's pending controls/variants are:

| PC | Pending variants |
|---|---|
| PC-14 | Failed/Succeeded/Blocked Working fast-release interception; also close the unpooled remote Shared gap before claiming complete D-1 coverage. |
| PC-15 | Working and Unknown remote Worktree holds survive TTL/cap retirement. |
| PC-16 | Explicit cancellation requests exactly one ordinary stop. |
| PC-28 | Runner Idle cannot override committed server Working. |
| PC-71 | The actual settlement fast hook reaches the wire only after the terminal result and exact caller obligation commit. The test advances fake time at commit so a later retry cannot mask this order. |
| PC-74 | Real frozen queue attempts marked Sent still require a complete correlated UserPrompt: ack, wrong, stale, partial and QueuedUserPrompt-only evidence remain unconfirmed; late complete receipt finishes without retyping. |
| PC-82 | Busy parent receives no writes until committed TurnEnd; obligation and delivery survive recreated server services and release reply loss. |
| PC-104 | Untouched production-default options cause zero new observations/reservations/conditional commands; legacy intent reconciliation remains active. |

Mutation owns deliberate defects, restoration and missing-control discovery. No PC is discharged by this report.

## Run history and evidence

Initial committed slice: `01750098b5ee98c51f2fe19f1fc9830f37382b5e`. CP-18 build failed on the new fixture's incorrect CompletionNoteFlushQueue namespace; no tests ran. Repair round 1: `fa102c8fd8b95bc708762cc7ad1a628a43f035d7`; CP-18 ran 8, passed 7, failed the new attention assertion's null handling. Repair round 2 started at `5917d0612be43032061cc0e36f21e3a1e1fb59c5`; its build was intentionally stopped before tests while source review found masked receipt/order guards. Stop completed; subsequent wait returned exit 6 and there is no TRX or CHECKPOINT line for that interrupted run. The same repair group was completed and pushed as `f98af19fba6760ce8a38e6541a1eae4a518722c8`; final CP-18 ran 8, passed 7, failed the wrong runner-sync field assertion. The two-round limit prevented further repair. No unchanged green proof was repeated for reassurance and no loaded repetitions ran.

Unlisted commands are accounted for: (1) declared checkpoint-tool bootstrap through build-slot, granted/waited=0s, build succeeded; (2) requested whole runner class; (3) requested affected server classes plus full task service/reply/pool lifecycle classes because the changed hooks affect cancellation/settlement/janitor behavior; (4) exact existing failing endpoint method at base to establish provenance. No full assembly or whole Unit run occurred. The server class run reused verified CP-18 output. No builds overlapped: the separate runner build began after the server build finished; the base build used an independent detached checkout while the already-built server host finished. Every process was awaited. All slots were granted with waited=0s. The final CP-18 executor recovered from two owner-read timeouts without changing any deadline.

Evidence roots under `/work/worktrees/task-8eed758c/`:

- `.antiphon/checkpoints/20261005-204341-cfaa/`: initial compile failure.
- `.antiphon/checkpoints/20261005-204612-a566/`: first test run, 7/8.
- `.antiphon/checkpoints/20261005-205125-bdea/`: intentional stop during build, no test results.
- `.antiphon/checkpoints/20261005-205430-bb9d/`: final CP-18 report.json/report.md and rows/CP-18/run.trx, 7/8.
- `.antiphon/c667-s4b-full/S4b-server-full-20261005-210104-7a1a/`: 723/725, source.json and fresh run.trx.
- `.antiphon/c667-s4b-full/S4b-runner-full-20261005-205937-c78f/`: 38/38, validated source.json and fresh run.trx.
- `.antiphon/c667-base-proof/S4b-base-claim-20261005-210617-a181/`: base 1/1, validated source.json and fresh run.trx.

`scripts/check-evidence-diff.ps1` over base..tested-source passed: four commits, zero violations. The final response records the refreshed check including this report-only commit. Generated TRX/JSON/log payloads remain ignored; only this permitted individual Markdown report is committed. The checkpoint tool removed 28 product alternate-output directories; explicit cleanup removed 38 runner/base/tool alternate-output directories. The clean, task-created detached base checkout was removed after its completed run. The assigned source worktree remains available for repair.

Rerun CP-18 after a committed repair using the plan's bootstrap and `run --rows CP-18 --expected-source-sha <new HEAD> --keep-outputs --max-wait 50s`; wait until exit is not 75. Then rerun the affected red class selections with a matching clean source receipt. Preserve the original Code landing owner. Once implementation and ordinary verification are complete, next is Review; PCs stay pending for post-land SourceLanding Mutation. Do not start S4c or enable automatic release in this repair.

Restart: none performed. Loading the eventual server changes requires a server restart owned by the caller/orchestrator after successful Review and landing; no runner restart is required by S4b.

Unedited CHECKPOINT lines follow.
CHECKPOINT CP-18 commit=01750098b5ee98c51f2fe19f1fc9830f37382b5e build=failed filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/(Working_session_keeps_ownership_and_visible_debt*)|(Janitor_cannot_bypass_a_release_hold*)|(Cancellation_reconciles_without_second_stop*)|(Settlement_delivery_precedes_release_and_survives_release_fault*)|(Parent_receipt_rejects_ack_stale_or_partial_prompt*)|(Blocked_report_with_running_runner_frees_the_seat*)|(Failed_settlement_releases_without_success_branch*)|(Existing_job_discovers_debt_without_settlement_callback*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=01750098b5ee98c51f2fe19f1fc9830f37382b5e sourceState=clean buildSource=unknown

CHECKPOINT CP-18 commit=fa102c8fd8b95bc708762cc7ad1a628a43f035d7 build=ok filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/(Working_session_keeps_ownership_and_visible_debt*)|(Janitor_cannot_bypass_a_release_hold*)|(Cancellation_reconciles_without_second_stop*)|(Settlement_delivery_precedes_release_and_survives_release_fault*)|(Parent_receipt_rejects_ack_stale_or_partial_prompt*)|(Blocked_report_with_running_runner_frees_the_seat*)|(Failed_settlement_releases_without_success_branch*)|(Existing_job_discovers_debt_without_settlement_callback*) executed=8 passed=7 failed=1 skipped=0 trx=/work/worktrees/task-8eed758c/.antiphon/checkpoints/20261005-204612-a566/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=fa102c8fd8b95bc708762cc7ad1a628a43f035d7 sourceState=clean buildSource=verified

CHECKPOINT CP-18 commit=f98af19fba6760ce8a38e6541a1eae4a518722c8 build=ok filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/(Working_session_keeps_ownership_and_visible_debt*)|(Janitor_cannot_bypass_a_release_hold*)|(Cancellation_reconciles_without_second_stop*)|(Settlement_delivery_precedes_release_and_survives_release_fault*)|(Parent_receipt_rejects_ack_stale_or_partial_prompt*)|(Blocked_report_with_running_runner_frees_the_seat*)|(Failed_settlement_releases_without_success_branch*)|(Existing_job_discovers_debt_without_settlement_callback*) executed=8 passed=7 failed=1 skipped=0 trx=/work/worktrees/task-8eed758c/.antiphon/checkpoints/20261005-205430-bb9d/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=f98af19fba6760ce8a38e6541a1eae4a518722c8 sourceState=clean buildSource=verified

CHECKPOINT S4b-server-full commit=f98af19fba6760ce8a38e6541a1eae4a518722c8 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)|(AttentionServiceTests*)|(AttentionApiTests*)|(RunnerSlotEndpointTests*)|(RunnerSlotRulesTests*)|(AgentTaskServiceIntegrationTests*)|(AgentTaskReplyIntegrationTests*)|(AgentTaskPoolTests*)/* executed=725 passed=723 failed=2 skipped=0 trx=/work/worktrees/task-8eed758c/.antiphon/c667-s4b-full/S4b-server-full-20261005-210104-7a1a/run.trx slot=granted waited=0s dirty=0 source=f98af19fba6760ce8a38e6541a1eae4a518722c8 sourceState=clean buildSource=verified

CHECKPOINT S4b-runner-full commit=f98af19fba6760ce8a38e6541a1eae4a518722c8 build=ok filter=/*/*/TerminalSeatReleaseTests/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-8eed758c/.antiphon/c667-s4b-full/S4b-runner-full-20261005-205937-c78f/run.trx slot=granted waited=0s dirty=0 source=f98af19fba6760ce8a38e6541a1eae4a518722c8 sourceState=clean buildSource=verified

CHECKPOINT S4b-base-claim commit=f329f5d0c0641117abf556b6bee0d9e8966ba9ff build=ok filter=/*/*/RunnerSlotEndpointTests*/Release_orphans_skips_a_seat_claimed_after_the_list* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8eed758c/.antiphon/c667-base-proof/S4b-base-claim-20261005-210617-a181/run.trx slot=granted waited=0s dirty=0 source=f329f5d0c0641117abf556b6bee0d9e8966ba9ff sourceState=clean buildSource=verified
