# CARD-0519 S9 fixture repair — Code af2a57eb

Complete within the assigned scope: both fixture-only repairs passed their full
classes, CP-8 **20/20** and CP-12 **8/8**, with zero failures/skips. The plan accepts
the eight retained green rows, giving cumulative S9 coverage **157/157**. This is
combined evidence at three actual tested SHAs, not a single-SHA 157-case run.
Next is Review of this task, followed by adoption/landing through a4290b71.

## Source and ownership

- Code task: af2a57eb-7a1d-4ce5-8dde-722e2a165f2e.
- Original Code task / landing owner: a4290b71-e8e5-4a4f-8fd2-c7bf1f5467f9.
- Branch: feat/card-task-af2a57eb.
- Worktree: /work/worktrees/task-af2a57eb.
- Assigned desktop checkout C:\Antiphon\worktrees\card-task-af2a57eb was not accessed.
- Task base: 6db9855878541f09b71b36200a8183989216ab45.
- Implementation and actual tested SHA: 375f863e0cacc888b23cfc7cc5d0c5e00d215947.
- Plan: docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md,
  `### Checkpoints` and `## S9 implementation selection`.
- Predecessor evidence: .antiphon/task-170e6f0d.md.
- This committed evidence summary: .antiphon/task-af2a57eb.md.
- Fresh generated evidence (ignored):
  .antiphon/checkpoints/20261005-124446-e5f1/report.json, report.md,
  rows/CP-8/run.trx and rows/CP-12/run.trx.

The implementation was committed and pushed before building or testing. The final
report-only commit is identified by full pushed SHA in the caller-facing report;
it does not relabel the actual tested SHA. Branch history remains fast-forward
from the assigned base. No source edits occurred during the checkpoint run.

## Exact repairs and guarded behavior

1. tests/Antiphon.Tests/Application/ChannelOutboundDiscoveryTests.cs:
   C519_Each_work_kind_gets_a_bounded_share advances the controlled clock by one
   second after the first accepted publication and before creating the due reply.
   The later reply therefore has a later acceptance timestamp; the valid GUID
   tie-break cannot choose the earlier reply. The exact catalog-preview assertion,
   publication, settlement, repair, and producer receipt assertions are unchanged.
2. tests/Antiphon.Tests/Application/ChannelOutboundUnifiedPathTests.cs:
   World retains the project ID it explicitly creates and uses it for the implied
   bundle task. This removes the global Projects.SingleAsync assumption from a
   harness containing several projects. The exact NO_REPLY withholding, attachment
   bytes, source settlement, and deliverable timestamp assertions are unchanged.

No production source, timeout, assertion tolerance, or feature default changed.
No new tests or deliberate mutants were introduced. The predecessor's actual red
TRX was inspected: CP-8 failed its exact expected preview; CP-12 threw `Sequence
contains more than one element` in fixture setup. The same two methods now pass
in fresh full-class TRX. These ordinary failures are not Mutation qualification.

## Verification selection, counts and provenance

The specific brief and the S9 plan amendment override the generic whole-Unit Final
profile: only CP-8 and CP-12 are rerun. The plan explicitly says other rows retain
their first clean receipts and assigns this continuation the two red rows. There
is no requirement to repeat the entire selection at one SHA. The brief's “other
nine” wording lists eight actual rows; all eight are accounted for below.
No whole-Unit, assembly, namespace, Windows, or live activation run was performed.
The 15 inherited CARD-1064 failures in CP-51/52 remain excluded, not passed.

| CP | Passed / executed | Failed / skipped | Actual tested SHA | Evidence treatment |
|---|---:|---:|---|---|
| CP-8 | 20/20 | 0/0 | 375f863e0cacc888b23cfc7cc5d0c5e00d215947 | Fresh full discovery class |
| CP-9 | 8/8 | 0/0 | db71b343f7980a4679fe688f0520644096587566 | Retained full trailing class |
| CP-10 | 22/22 | 0/0 | d597aee115f75fedbac49562ccef21d11261df0a | Retained full retry class |
| CP-11 | 6/6 | 0/0 | db71b343f7980a4679fe688f0520644096587566 | Retained full metadata class |
| CP-12 | 8/8 | 0/0 | 375f863e0cacc888b23cfc7cc5d0c5e00d215947 | Fresh full unified-path class |
| CP-53 | 32/32 | 0/0 | db71b343f7980a4679fe688f0520644096587566 | Retained full storage class |
| CP-54 | 38/38 | 0/0 | db71b343f7980a4679fe688f0520644096587566 | Retained full delivery class |
| CP-55 | 12/12 | 0/0 | db71b343f7980a4679fe688f0520644096587566 | Retained full materialization class |
| CP-56 | 10/10 | 0/0 | db71b343f7980a4679fe688f0520644096587566 | Retained full dispatch class |
| CP-57 | 1/1 | 0/0 | d597aee115f75fedbac49562ccef21d11261df0a | Retained exact runtime method |

Fresh TRX confirms all 20 discovery and all 8 unified-path methods ran. The repaired
methods passed in 1.588s and 6.819s respectively. Retained TRX rosters were also
inspected: every intended class and CP-57's exact
Deferred_is_durable_and_releases_runtime method has nonzero passing results.
All ten selected receipts have `slot=granted waited=0s dirty=0 sourceState=clean
buildSource=verified` at their actual tested SHA. Fresh row builds also had
`slot=granted waited=0s`; all leases were released.

All three structured source receipts were independently validated in this task:

```text
CHECKPOINT SOURCE VALID source=db71b343f7980a4679fe688f0520644096587566 rows=6
CHECKPOINT SOURCE VALID source=d597aee115f75fedbac49562ccef21d11261df0a rows=2
CHECKPOINT SOURCE VALID source=375f863e0cacc888b23cfc7cc5d0c5e00d215947 rows=2
```

The retained report.json files remain at:
/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/report.json
(CP-9/11/53/54/55/56) and
/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-122016-9dc3/report.json
(CP-10/57). The predecessor report preserves their essential evidence in Git.

## Commands, cost and run accounting

Necessary unlisted bootstrap, because this fresh checkout had no checkpoint binary:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c519-af2a57eb-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c519-af2a57eb-tool/ --nologo
```

It succeeded: zero errors, one existing CS8602 warning at TaskOwnerGuard.cs:170,
5.92s build time. Gate evidence:

```text
BUILD SLOT granted lease=19596611-c7b5-4d55-a9ea-bbfbf4156c39 waited=0s maxcpucount=6
BUILD SLOT released lease=19596611-c7b5-4d55-a9ea-bbfbf4156c39 held=7s
```

The compiled checkpoint tool was invoked directly; its own slot gate owns every
row build and test driver. No unleased build/test ran:

```sh
dotnet tools/Antiphon.Checkpoints/bin-c519-af2a57eb-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md --rows CP-8,CP-12 --serial --row-timeout 15m --total-timeout 60m --expected-source-sha 375f863e0cacc888b23cfc7cc5d0c5e00d215947 --max-wait 50s
dotnet tools/Antiphon.Checkpoints/bin-c519-af2a57eb-tool/Antiphon.Checkpoints.dll wait --run 20261005-124446-e5f1 --max-wait 50s
```

Repeated foreground waits ended at GREEN exit 0, after 6m14s, below the 12-minute
row estimate and the 30-minute task budget. Row builds took 104.28s and 97.20s;
test hosts 72.74s and 97.06s. The tool's summary says `builds: 57` because it
retains the complete plan build inventory; executor.log shows only the two
selected row builds actually ran. One repair group, one execution per selected
row, no extra normal/loaded repetitions, no post-green reruns. Receipt validation,
TRX inspection, Git/evidence-policy checks and cleanup are not additional tests.

## Every V/R outcome and remaining plan scope

“Passed” is limited to this S9 selection and its accepted retained receipts.

| ID | Actual outcome |
|---|---|
| V-1 | Earlier schema slice; not rerun, no new pass claim. |
| V-2 | Earlier capture slice; not rerun, no new pass claim. |
| V-3 | Selected materialization class passed, retained CP-55 12/12. |
| V-4 | Passed selected full unified-path class, fresh CP-12 8/8, including implied bundle policy. |
| V-5 | S9 discovery witnesses passed, fresh CP-8 20/20; repair fairness retained CP-11. Terminal-complete closure still deferred to S10. |
| V-6 | Selected trailing class passed, retained CP-9 8/8. |
| V-7 | Selected retry class passed, retained CP-10 22/22. |
| V-8 | S8 failure-recording class outside this selection; not rerun. |
| V-9 | Selected metadata-repair class passed, retained CP-11 6/6. |
| V-10 | Retention deferred to S10 / later full Final; not passed here. |
| V-11 | Pre-publication process-death coverage deferred to S12 / later full Final. |
| V-12 | During/after-publication process-death coverage deferred to S12 / later full Final. |
| V-13 | Real transport coverage deferred to S12 / later full Final. |
| R-1 | Selected dispatcher and exact runtime passed, retained CP-56 10/10 and CP-57 1/1. |
| R-2 | Outside selection; CP-51/52 and CARD-1064's 15 failures excluded, not passed. |
| R-3 | Correlation/matching classes outside selection; not rerun. |
| R-4 | Machine-text/follow-up attachment classes outside selection; not rerun. |
| R-5 | Selected delivery class passed, retained CP-54 38/38; policy/contract classes not rerun. |
| R-6 | Deadline class outside selection; not rerun. |
| R-7 | Selected storage class passed, retained CP-53 32/32. |
| R-8 | Recovery selection outside this S9 continuation; not rerun. |
| R-9 | Composed transport outside this S9 continuation; not rerun. |
| R-10 | Batching outside this S9 continuation; not rerun. |
| R-11 | Retention regression deferred / outside S9; not rerun. |
| R-12 | Native Windows parity deferred / outside S9; not rerun. |

Deferred-to-later-Final: V-5 terminal-complete, V-10..13, R-11/12, and remaining
plan-wide coverage. V-1/2/8, R-2..6 and R-8..10 are not requalified beyond the
explicit partial results above. No manual live activation is required for this
default-off fixture continuation. Later-slice manual acceptance remains unperformed.

## Mutation and operational handoff

Every PC and every named variant remains pending method-scoped SourceLanding
Mutation: PC-1..100, PC-1059-1..6, PC-S4-1..4, PC-S5-1..4, PC-S6-1..5, PC-S7-1,
PC-S8-1..2 and PC-S9-1, including every multi-path, boundary and fault variant.
The superseded PC-20 catalog-less-loss variant needs reconciliation with the
reviewed S4 direct-routing amendment. S9 controls include PC-34..37, PC-74..78,
PC-97/98 and PC-S9-1. Ordinary green does not discharge any control. Mutation owns
deliberate mutants, red/restore/green and missing-control discovery.

ANTIPHON_TASK_TOKEN was present. GET /api/runner-defaults and
GET /api/session-runners were read through the assigned API. No Runner/Platform
pin or embedded fleet location was added. UnifiedRecoveryEnabled remains false.
**restart: none; owner: none.** No deployment, activation or landing occurred.
After Review, caller adopts into and lands original Code owner a4290b71, then
commissions SourceLanding Mutation; this task is the Review subject.

All runs and child processes were awaited. The checkpoint tool cleaned its row
outputs and finished shadow; the remaining task-owned bootstrap output directory
was removed. Its ignored inventory is .antiphon/af2a57eb-evidence/cleanup-inventory.json.
Generated receipts/TRX/logs remain ignored; only this individual Markdown report
is committed. Before this report commit, the full task-range check passed:

```text
EVIDENCE range base=6db9855878541f09b71b36200a8183989216ab45 head=375f863e0cacc888b23cfc7cc5d0c5e00d215947
EVIDENCE result commits=1 entries=0 violations=0 base=6db9855878541f09b71b36200a8183989216ab45 head=375f863e0cacc888b23cfc7cc5d0c5e00d215947
```

The report-only commit also requires a fresh base..HEAD evidence-policy check and
remote-ref verification; their outcomes and the final pushed SHA are in the final
caller-facing report.

## Unedited CHECKPOINT lines

The following are the two fresh green rows and eight accepted retained green rows.

```text
CHECKPOINT CP-8 commit=375f863e0cacc888b23cfc7cc5d0c5e00d215947 build=ok filter=/*/*/ChannelOutboundDiscoveryTests/* executed=20 passed=20 failed=0 skipped=0 trx=/work/worktrees/task-af2a57eb/.antiphon/checkpoints/20261005-124446-e5f1/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=375f863e0cacc888b23cfc7cc5d0c5e00d215947 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=375f863e0cacc888b23cfc7cc5d0c5e00d215947 build=ok filter=/*/*/ChannelOutboundUnifiedPathTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-af2a57eb/.antiphon/checkpoints/20261005-124446-e5f1/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=375f863e0cacc888b23cfc7cc5d0c5e00d215947 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundTrailingRecoveryTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundMetadataRepairTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-53 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundStorageTests/* executed=32 passed=32 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-53/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-54 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundDeliveryTests/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-54/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundMaterializationTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-55/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=db71b343f7980a4679fe688f0520644096587566 build=ok filter=/*/*/ChannelOutboundDispatchIntegrationTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-114036-ce3d/rows/CP-56/run.trx slot=granted waited=0s dirty=0 source=db71b343f7980a4679fe688f0520644096587566 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=d597aee115f75fedbac49562ccef21d11261df0a build=ok filter=/*/*/ChannelOutboundRetryPolicyTests/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-122016-9dc3/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=d597aee115f75fedbac49562ccef21d11261df0a sourceState=clean buildSource=verified
CHECKPOINT CP-57 commit=d597aee115f75fedbac49562ccef21d11261df0a build=ok filter=/*/*/AgentTaskReplyIntegrationTests/Deferred_is_durable_and_releases_runtime executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-170e6f0d/.antiphon/checkpoints/20261005-122016-9dc3/rows/CP-57/run.trx slot=granted waited=0s dirty=0 source=d597aee115f75fedbac49562ccef21d11261df0a sourceState=clean buildSource=verified
```
