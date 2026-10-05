# CARD-0519 S9 continuation — Code 170e6f0d

Incomplete: added the missing discovery witnesses and repaired six base-reproduced test failures; the latest selected results are **155/157 passed, 2 failed, 0 skipped**. Both permitted repair rounds are used. Two fixture repairs and CP-8/CP-12 verification remain, so next is Code, not Review.

## Ownership and source

- This Code task: 170e6f0d-f076-4365-ad4f-23b8d0d3803d.
- Original Code task / landing owner: **a4290b71-e8e5-4a4f-8fd2-c7bf1f5467f9**. Adopt this continuation into that owner; do not land this task independently.
- Branch: feat/card-task-170e6f0d.
- Worktree: /work/worktrees/task-170e6f0d. Desktop assignment C:\Antiphon\worktrees\card-task-170e6f0d was not accessed.
- Task base: 84f39527b0c0711739a46eef38bfced8821888ec.
- Plan: docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md.
- Evidence report: .antiphon/task-170e6f0d.md (this individually committed Markdown file). Generated TRX/JSON/logs remain ignored.
- Implementation commits, each committed before testing and pushed:
  - 6c3d57b666287f8c57175269aabd26b3e9080f60: discovery witnesses and CP-8 floor 20; initial compile red.
  - db71b343f7980a4679fe688f0520644096587566: repair 1, nullable deadline assertion compile fix; first ten-row run 150/157.
  - d597aee115f75fedbac49562ccef21d11261df0a: repair 2, UTC-preserving page probe, six base-red fixture corrections, expanded policy variants; four-row rerun 49/51.
- The final report/plan-only commit is identified by the pushed full SHA in the caller-facing report. Actual tested SHAs remain those above; documentation commits do not relabel earlier receipts.

The task token was available. Read GET /api/runner-defaults and GET /api/session-runners through the assigned API. No Runner or Platform pin was added. UnifiedRecoveryEnabled remains false by default. **restart: none; owner: none**. No live activation, restart, deployment, or land occurred.

## Remaining work (exact continuation)

1. **CP-8 / ChannelOutboundDiscoveryTests.C519_Each_work_kind_gets_a_bounded_share**, line 469: expected preview `publish this due answer`, got `repair this accepted answer`. Both publications use a frozen clock, so the valid accepted-order GUID tie-break can select the older fixture reply. The first run happened to pass; the final run failed. Advance the controlled clock after the accepted reply and before creating the due reply, retaining all assertions. This is a proposed fixture repair, not a verified resolution. No unchanged repeat was used to hide the failure.
2. **CP-12 / ChannelOutboundUnifiedPathTests.C519_Machine_origins_and_attachments_keep_policy**, World.BundleAsync line 235: `InvalidOperationException: Sequence contains more than one element`. The newly added implied-bundle fixture calls global `db.Projects.SingleAsync()`, while the harness seeds multiple projects. Scope the lookup through the harness agent's board/project, or retain the explicitly created fixture project ID. The implied Delegation-bundle arm is unqualified until fixed and run.

Commit/push the repairs, then run only CP-8 and CP-12 with the new committed expected SHA. They are full affected classes. Use the checkpoint tool's `run --plan docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md --rows CP-8,CP-12 --serial --expected-source-sha <committed HEAD>`; wait until the run exits other than 75, then inspect fresh TRX and validate receipts. Tool bootstrap and any direct build need scripts/build-slot.ps1 and a forward-slash alternate OutputPath. Current alternate outputs were removed after all runs finished, so rebuild the tool if needed. No assertion tolerance or timeout should change. Then commission Review; once ordinary verification is complete and Review accepts, caller lands through original owner a4290b71 and commissions SourceLanding Mutation.

## Changes and guarded observations

Production S9 publication/repair was inherited from the original owner; this continuation changes tests and plan only.

- ChannelOutboundDiscoveryTests adds actual reader-page observations (32 rows per page, ten pages in a cycle, then two remaining candidates), cursor progress past 321 idle roots, independent due publication/metadata repair behind withheld discovery sources, and TTL/capture lock serialization. The TTL test observes an old answer beyond a historical prompt page, independent loss context reaching the destination lock while capture is held, then persisted ownership/settlement/loss state; a loss-first companion asserts atomic incident and alert plus refused capture. These are real database/host/producer observations, not self-comparison or constant stubs. The fairness preview assertion is currently red as above. Terminal-complete transcript closure remains S10.
- ChannelOutboundRetryPolicyTests matches the existing S8 loss contract after definite pre-entry failure: Failed, no publication/attempt, settled source, one failure episode, Critical incident and alert. Cancellation explicitly requires OperationCanceledException while preserving uncertainty/receipt assertions. The terminal refusal fault intercepts the actual INSERT AgentIncidents through EF reader and non-query command paths, replacing an obsolete UPDATE trigger.
- AgentTaskReplyIntegrationTests.ChannelOutboundRuntime adds the harness-native timestamp to its synthetic submitted UserPrompt when the original attempt has no sequence floor. Both shared-runtime entry points now pass.
- ChannelOutboundUnifiedPathTests gives prompts distinct identities; API errors at beginning/middle/end with a positive companion pass. Default/custom origins and explicit/attachments-only cases are exercised; implied Delegation bundle plus NO_REPLY/prose companion was added but stops in fixture setup, so is not claimed covered.
- No timeout widened, assertion loosened, production behavior changed, or deliberate mutant introduced. Red/restore/green control qualification remains Mutation-owned.

## Ordinary verification scope and latest counts

The specific brief and S9 plan selection authorize only **CP-8..CP-12 and CP-53..CP-57**, overriding the generic whole-Unit Final profile. No whole-Unit, whole-assembly, namespace, Pty, native Windows, or live manual activation run was performed. CP-51/CP-52's 15 inherited CARD-1064 failures are excluded, not counted or called passed. Selected rows are full classes except the plan's exact CP-57 runtime witness. No unbounded shared-impact assembly run was attempted.

| CP | Exact filter | Executed | Passed | Failed | Skipped | Actual tested SHA | slot / waited |
|---|---|---:|---:|---:|---:|---|---|
| CP-8 | /*/*/ChannelOutboundDiscoveryTests/* | 20 | 19 | 1 | 0 | d597aee115f75fedbac49562ccef21d11261df0a | granted / 0s |
| CP-9 | /*/*/ChannelOutboundTrailingRecoveryTests/* | 8 | 8 | 0 | 0 | db71b343f7980a4679fe688f0520644096587566 | granted / 0s |
| CP-10 | /*/*/ChannelOutboundRetryPolicyTests/* | 22 | 22 | 0 | 0 | d597aee115f75fedbac49562ccef21d11261df0a | granted / 0s |
| CP-11 | /*/*/ChannelOutboundMetadataRepairTests/* | 6 | 6 | 0 | 0 | db71b343f7980a4679fe688f0520644096587566 | granted / 0s |
| CP-12 | /*/*/ChannelOutboundUnifiedPathTests/* | 8 | 7 | 1 | 0 | d597aee115f75fedbac49562ccef21d11261df0a | granted / 0s |
| CP-53 | /*/*/ChannelOutboundStorageTests/* | 32 | 32 | 0 | 0 | db71b343f7980a4679fe688f0520644096587566 | granted / 0s |
| CP-54 | /*/*/ChannelOutboundDeliveryTests/* | 38 | 38 | 0 | 0 | db71b343f7980a4679fe688f0520644096587566 | granted / 0s |
| CP-55 | /*/*/ChannelOutboundMaterializationTests/* | 12 | 12 | 0 | 0 | db71b343f7980a4679fe688f0520644096587566 | granted / 0s |
| CP-56 | /*/*/ChannelOutboundDispatchIntegrationTests/* | 10 | 10 | 0 | 0 | db71b343f7980a4679fe688f0520644096587566 | granted / 0s |
| CP-57 | /*/*/AgentTaskReplyIntegrationTests/Deferred_is_durable_and_releases_runtime | 1 | 1 | 0 | 0 | d597aee115f75fedbac49562ccef21d11261df0a | granted / 0s |

Latest totals combine the last execution of each row, not one all-green run. Fresh TRX rosters were inspected for all ten intended selections, including parameterized counts (storage 32 results/20 methods, delivery 38/18, dispatcher 10/6) and the exact CP-57 method; every intended row executed nonzero cases.

All selected builds and test rows had **slot=granted**. All build waits were 0s. The first CP-57 test row waited 30s; all other test rows, including all final rerun rows, waited 0s. Every emitted ordinary row has dirty=0, sourceState=clean and buildSource=verified at its actual tested SHA. Green receipts validated:
```text
CHECKPOINT SOURCE VALID source=db71b343f7980a4679fe688f0520644096587566 rows=6
CHECKPOINT SOURCE VALID source=d597aee115f75fedbac49562ccef21d11261df0a rows=2
```
These are respectively CP-9/11/53/54/55/56 and CP-10/57. The two red rows have clean provenance but are not green certificates.

## Run history, failures, and unlisted diagnostics

1. Necessary unlisted checkpoint-tool bootstrap at 6c3d57b666287f8c57175269aabd26b3e9080f60: slot-gated `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c519-s9-tool/ --nologo`; succeeded, one warning, zero errors.
```text
BUILD SLOT granted lease=ae59b6d0-4dea-472a-81fc-c0313ac93796 waited=0s maxcpucount=6
BUILD SLOT released lease=ae59b6d0-4dea-472a-81fc-c0313ac93796 held=5s
```
2. Initial checkpoint run .antiphon/checkpoints/20261005-113801-8cae at 6c3d57b666287f8c57175269aabd26b3e9080f60 failed CP-8 compilation (CS0313, nullable DateTime assertion). Stopped the run and awaited it; the already-started CP-9 build was canceled. No tests/TRX and no completed CHECKPOINT row lines were produced. Both acquired build slots with 0s wait (CP-8 lease 60277944-3c41-4000-8210-1dcd0e3c0ece; CP-9 lease 0a89326c-260a-4f0f-919b-4902a6028473). An expected-SHA transcription mistake also caused a pre-build admission refusal (exit 2, source_mismatch or source_dirty); the correct full SHA was supplied on retry. Neither refusal nor compile failure is test evidence.
3. First complete run at db71b343f7980a4679fe688f0520644096587566: 157 executed, 150 passed, 7 failed, zero skipped; wall 35m01s. Evidence .antiphon/checkpoints/20261005-114036-ce3d/report.json and per-row run.trx. Failures:
   - CP-8 page-bound helper replayed DateTime values with Unspecified Kind (new fixture defect).
   - CP-10 Attempt_commit_precedes_producer expected unsettled source despite S8 terminal loss; Cancellation_is_never_success did not expect the real cancellation exception; Publication_cap_survives_restart's old UPDATE fault never fired.
   - CP-12 API-withholding positive companion reused an identical prompt; shared deferred-runtime scenario did not reach converter.
   - CP-57 same shared deferred-runtime scenario.
4. **Unlisted base diagnostic**, required before attributing inherited red: detached /tmp/c519-170e6f0d-base at exact task base 84f39527b0c0711739a46eef38bfced8821888ec. Selected precisely those six pre-existing retry/unified/runtime cases. First compiled filter omitted wildcard suffixes on OR operands and executed **zero tests**, exit 3; this is not red proof. Corrected suffixes and reused the verified isolated build; all six executed and failed with the same failures at base. Thus they were inherited, not inferred flaky. No entire base class or assembly was run. This diagnostic overlapped the tail of the first run in a separate immutable worktree/output, with schema-isolated fixtures and slot gates; all processes were awaited before edits. Corrected diagnostic was 0/6, slot=granted, waited=0s, clean source/build provenance. Logs/TRX are under .antiphon/s9-baseline/S9-BASE-20261005-121137-1720 and S9-BASE-20261005-121617-7571. The row tool emits a CHECKPOINT line, not a report.json/source.json; do not treat this diagnostic as a green certificate.
5. Repair 2 at d597aee115f75fedbac49562ccef21d11261df0a reran only CP-8/10/12/57: 51 executed, 49 passed, two new fixture failures above, zero skipped; wall 12m36s. Evidence .antiphon/checkpoints/20261005-122016-9dc3/report.json and fresh per-row TRX. All six base-red cases now pass.
6. No repeat proof beyond ordinary reruns after changed source; no loaded repetitions; no green row rerun without a new reason. At most two repair rounds used. Required waits were completed even though total work exceeded the brief's approximate 30–60 minute budget. The two completed checkpoint groups consumed 47m37s, with bootstrap, compile failure and base diagnostics additional/partly overlapping.

## V/R accounting and deferred IDs

“Passed” below is scoped to the selected ordinary S9 class results, not certification of future slice variants or positive controls.

| ID | Actual outcome |
|---|---|
| V-1 | Not rerun: earlier schema slice; no new pass claim. |
| V-2 | Not rerun: earlier capture slice; no new pass claim. |
| V-3 | Selected materialization class passed CP-55, 12/12. |
| V-4 | Incomplete: CP-12 7/8. API matrix and runtime companion now green; new implied-bundle fixture remains red. |
| V-5 | Incomplete: CP-8 19/20. Page/cursor/TTL witnesses green; hosted work-share preview assertion red. Terminal-complete closure explicitly deferred to S10. |
| V-6 | Selected trailing class passed CP-9, 8/8. |
| V-7 | Selected retry class passed CP-10, 22/22. |
| V-8 | S8 failure-recording class not rerun; no new pass claim. CP-51/52 inherited CARD-1064 excluded. |
| V-9 | Selected metadata-repair class passed CP-11, 6/6. |
| V-10 | Deferred to S10 and later complete Final: real retention witnesses not run. |
| V-11 | Deferred to S12 and later complete Final: pre-publication process-death witnesses not run. |
| V-12 | Deferred to S12 and later complete Final: during/after-publication process-death witnesses not run. |
| V-13 | Deferred to S12 and later complete Final: isolated real transport witnesses not run. |
| R-1 | Passed selected dispatcher 10/10 (CP-56) plus exact runtime 1/1 (CP-57). |
| R-2 | Outside this S9 selection; CP-51/52 not rerun, 15 inherited CARD-1064 reds remain excluded. |
| R-3 | Correlation/matching classes outside S9 selection; not run. |
| R-4 | Machine-text/follow-up attachment classes outside S9 selection; not run. |
| R-5 | Partial: delivery class passed CP-54 38/38. Policy (29) and contract (2) outside S9 selection, not rerun. |
| R-6 | Deadline class outside S9 selection; not run. V-3 does not discharge it. |
| R-7 | Passed storage class CP-53, 32/32. |
| R-8 | Recovery selection outside S9; not run. |
| R-9 | Composed transport outside S9; not run. |
| R-10 | Batching outside S9; not run. |
| R-11 | Retention regression deferred/outside S9; not run. |
| R-12 | Windows native parity deferred/outside S9; not run. |

Deferred-to-later-Final coverage includes V-5 terminal-complete, V-10..13, R-11/12 and any plan-wide remaining coverage (V-1/2/8, R-2..6 and R-8..10 are not requalified here). V-4/V-5 remain immediate ordinary Code work, not deferred passes. No manual/live activation acceptance is required for this default-off S9 continuation; later slice manual work remains unperformed.

## Mutation handoff

**Every PC and every named variant remains pending**: PC-1 through PC-100, PC-1059-1 through PC-1059-6, PC-S4-1..4, PC-S5-1..4, PC-S6-1..5, PC-S7-1, PC-S8-1..2, PC-S9-1, and all multi-path/fault/boundary variants attached to these IDs. PC-20's original missing-catalog-loss variant is superseded by the reviewed S4 routing amendment and needs Mutation reconciliation, not silent discharge. S9 discovery and repair controls include PC-34..37/74..78/97/98 and PC-S9-1. Ordinary failures/base reproduction are not deliberate positive-control cycles. After ordinary Code and Review, caller lands through original Code owner and commissions method-scoped SourceLanding Mutation for red/restore/green and missing-control discovery, including any uncovered controls.

## Cleanup and evidence policy

All long-running processes and waits finished. The detached base worktree was removed after its processes exited. Removed 281 task-owned alternate-output directories from tracked-project trees; ignored inventory: .antiphon/s9-diagnostics/cleanup-inventory.json. Checkpoint executor shadows were cleaned by completed waits. No generated evidence was staged, force-added, or relocated to evade policy.

Full task-range evidence-policy check before the report commit:
```text
EVIDENCE range base=84f39527b0c0711739a46eef38bfced8821888ec head=d597aee115f75fedbac49562ccef21d11261df0a
EVIDENCE result commits=3 entries=0 violations=0 base=84f39527b0c0711739a46eef38bfced8821888ec head=d597aee115f75fedbac49562ccef21d11261df0a
```
The final report/plan-only commit must also pass scripts/check-evidence-diff.ps1 over base..HEAD before settlement; its actual result and final pushed SHA are supplied in the caller-facing report.

## Unedited CHECKPOINT lines

First complete group:
```text
CHECKPOINT CP-8 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundDiscoveryTests/* executed=20 passed=19 failed=1 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundTrailingRecoveryTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundRetryPolicyTests/* executed=22 passed=19 failed=3 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundMetadataRepairTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundUnifiedPathTests/* executed=8 passed=6 failed=2 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-53 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundStorageTests/* executed=32 passed=32 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-53/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-54 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundDeliveryTests/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-54/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundMaterializationTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-55/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundDispatchIntegrationTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-56/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-57 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/AgentTaskReplyIntegrationTests/Deferred_is_durable_and_releases_runtime executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-57/run.trx slot=granted waited=30s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
```

Exact base diagnostic (six inherited failures):
```text
CHECKPOINT S9-BASE commit=84f39527b0c0711739a46eef38bfced8821888ec build=reused filter=/*/*/(ChannelOutboundRetryPolicyTests*)|(ChannelOutboundUnifiedPathTests*)|(AgentTaskReplyIntegrationTests*)/(C519_Attempt_commit_precedes_producer*)|(C519_Cancellation_is_never_success*)|(C519_Publication_cap_survives_restart*)|(C519_Api_error_withholds_the_whole_window*)|(C519_Deferred_runtime_releases_without_claiming_publication*)|(Deferred_is_durable_and_releases_runtime*) executed=6 passed=0 failed=6 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/s9-baseline/S9-BASE-20261005-121617-7571/run.trx slot=granted waited=0s dirty=0 source=84f39527b0c0711739a46eef38bfced8821888ec sourceState=clean buildSource=verified
```

Repair 2 group:
```text
CHECKPOINT CP-8 commit=d597aee115f75fedbac49562ccef21d11261df0a build=ok filter=/*/*/ChannelOutboundDiscoveryTests/* executed=20 passed=19 failed=1 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-122016-9dc3/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=d597aee115f75fedbac49562ccef21d11261df0a sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=d597aee115f75fedbac49562ccef21d11261df0a build=ok filter=/*/*/ChannelOutboundRetryPolicyTests/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-122016-9dc3/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=d597aee115f75fedbac49562ccef21d11261df0a sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=d597aee115f75fedbac49562ccef21d11261df0a build=ok filter=/*/*/ChannelOutboundUnifiedPathTests/* executed=8 passed=7 failed=1 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-122016-9dc3/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=d597aee115f75fedbac49562ccef21d11261df0a sourceState=clean buildSource=verified
CHECKPOINT CP-57 commit=d597aee115f75fedbac49562ccef21d11261df0a build=ok filter=/*/*/AgentTaskReplyIntegrationTests/Deferred_is_durable_and_releases_runtime executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-122016-9dc3/rows/CP-57/run.trx slot=granted waited=0s dirty=0 source=d597aee115f75fedbac49562ccef21d11261df0a sourceState=clean buildSource=verified
```

--- next stage ---
next: code
handoff: Fix the S9 discovery fixture clock ordering and implied-bundle project lookup; commit/push and rerun CP-8/CP-12, then Review. Adopt this continuation into original Code owner a4290b71 before landing; all PCs stay pending SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md
