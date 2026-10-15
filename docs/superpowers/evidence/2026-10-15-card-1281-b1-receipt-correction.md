# CARD-1281 B-1 receipt correction evidence

Original Code task / landing owner: `f88c7bf2-bae7-499d-9b95-46a565561b26`.
Branch: `feat/card-task-f88c7bf2`. Worktree: `/work/worktrees/task-f88c7bf2`.
Base: `3931a31149acbc63837fcdda13cad60fdd1117cb`.

The authority is the B-1 binding in [the plan](../plans/2026-10-15-card-1281-pipeline-speed-plan.md)
and the component CP-2 in [the checkpoint manifest](../plans/2026-10-15-card-1281-cheap-slices-checkpoints.md).
B-1 is implemented and its acceptance selection passes. **Final remains incomplete:** the
required existing-guard row has two proven inherited bundle-size failures. These are failed
verification guards, not advisory narrative corrections. No Clean/Full or Review-ready claim is
made. Next Code repairs the inherited D-2 bundle-size gate, retaining the pinned instructions and
assertions, before the original B-1 owner receives ordinary Review.

Actual implementation and tested source: `3bdef3882d31df49c292efab66646b3a198c7875`.
This evidence update is a subsequent report-only commit and does not change that tested identity.

## Component boundary

The correction POST, DTO, service, receipt validator and landing consumer are implemented.
No shared registration, entity/model, migration, AgentTaskService, B-2 or B-3 file is changed.
S6 must register `ReviewEvidenceCorrectionService`, `ICheckpointReceiptValidator` to
`CheckpointReceiptValidator`, `ILandingGit`, `LandDeliveryBoundary`, `TimeProvider`, bind
`CheckpointReceiptSettings`, and map `MapReviewEvidenceCorrectionEndpoints`.
Existing operator and authenticated orchestrator credentials supply the stored actor.

The retained Review goal must contain one unquoted/unfenced line `--- correction selection v1 ---`
followed by one JSON line matching `ReviewCorrectionSelection` (PascalCase property names).
The retained result must similarly contain `--- correction assessment v1 ---` followed by
`Behavior`, `SubjectTaskId`, `SourceSha`, `SourceRef`, `RepositoryPath`. Only `Behavior: Satisfied`
permits correction. Unknown/unclassified/substantive behavior requires ordinary Review.
These are additive typed records inside existing retained strings, not a migration or permission
to edit historical reports. Existing legacy reports without the typed assessment remain refused.

`CheckpointReceiptSettings` has operator-owned producer pins (identity, inventory path and SHA-256,
original repository, repository from which committed selections can be read), retained storage root,
and the trusted validator script path. Defaults admit no producer. A request supplies no filesystem
path to execute or import. The inventory pin grants provenance only for those exact retained bytes;
it is not a self-reported clean flag. S6/operator configuration must bind an authentic producer.

The importer checks the exact committed selection/filters/minimums, source/ref/repository/producer,
TRX outcomes/counts, build stamp and manifest digests, retains the original bytes, and invokes
`scripts/validate-checkpoint-receipt.ps1` for the exact rows/SHA. Absent or failed rows return their
exact IDs. The transaction locks Review and subject in existing sorted order, rechecks the retained
report/commission and active predecessor, then appends correction/audit/successor together.
Duplicate requests resolve the same correction. Landing validates the linked record and retained
content. No Review is launched and historical report/outcome/cost bytes are not rewritten.

## Producer fixture

The positive uses the unedited S0 producer inventory from the common Git directory,
`antiphon/code-evidence/04228bff/producer-pack-index.json`, SHA-256
`5afb848873bdd8d653a3587654445d239b88cc31ea59f664dbf23efd78b98661`, with its original
`e39bccc6744f4a94724efd891d4acd360cee8941` source, branch and repository/worktree coordinates.
The fixture Review explicitly commissions CP-14/15/16. This does not reclassify S0's historical
incomplete Final profile as Full. Negative parser fixtures copy the actual pack into owned temp
storage and alter one dimension; they are not proof of a producer execution. The positive deletes
its input copy and restarts before reimporting from retained storage and invoking LandApproval.

## Scope and execution

The brief explicitly waives the whole Unit lane (CARD-1287). CP-1 is omitted, never passed.
CP-2 is the full AdvisoryChangePolicyTests plus StageOutcomeSummaryTests: 12 executions.
The authorized extra guard row CP-25 follows searches for changed identifiers, source references
and approval guard codes. It selects full InstructionBundleTests, ReviewEvidenceParserTests,
AgentTaskLandApprovalRequestTests, AgentTaskLandApprovalRecoveryTests,
AgentTaskLandApprovalPersistenceTests, CheckpointSourceApprovalTests,
InterimVerificationLandGuardTests, ReviewEvidenceRecoveryTests,
ReviewEvidenceRecoveryEndpointTests and PipelineSpeedIntegrationTests. This bounds the changed
approval path, the R-1 text/parser guards, existing recovery, and consumed immutable schema.
No full assembly is required. CP-25 is an eight-minute estimate plus the shared CP-2 build.
Its exact filter is retained in the checkpoint manifest/receipt; no unchanged green proof repetition
is planned. S6's V-27/28/29 authorization/transaction/wiring matrix remains its own obligation.

The extra checkpoint-tool bootstrap used the build-slot gate: 0 build errors, slot=granted,
waited=0s. CP-2's first run at `a80c4ed24fbdb7ef8f06ec57f7161f8f544e250a` executed 12:
10 passed, 2 failed, 0 skipped. Both new methods failed the intended HTTP 404-versus-200 assertions;
the other ten stayed green. The first implementation run at
`7ea3062cfe46fa4efe0d0423010b1ca931465cce` executed 12: 11 passed, 1 failed, 0 skipped.
V-3 passed; V-2 exposed an introduced error classification bug: InvalidDataException escaped the
row's incomplete-result handler. The fix retains the assertion and deadline. The final component
run passes; the separate inherited failures and final gate are detailed below.

## Final results and attribution

| ID | Outcome at the tested source |
|---|---|
| V-1 | Passed advisory disposition and composed policy |
| V-2 | Passed substantive/unknown/legacy assessment refusal; absent run, failed row, absent TRX/build provenance, wrong receipt; exact missing rows and no successor |
| V-3 | Passed real correction POST/validator/storage/LandApproval; replay after input deletion and host restart; one correction/audit, zero new Reviews; independent subject/SHA/ref/repository negatives; historical report/cost unchanged; broken link refused |
| V-4 | Passed separate Docs source/approval identity |
| V-5 | Passed narrative boundary and protected-content refusals |
| V-6 | Passed refusal without supported comment-equivalence proof |
| V-7 | Passed unchanged history and attribution |
| R-1 | **Incomplete gate**: CP-2/qualified 12/12; CP-25 274/276, with two inherited InstructionBundleTests size failures. Parser, summary and approval classes otherwise pass |
| R-5 | Existing foundation five-method class and Review recovery 21-method class passed in CP-25; no new S0 claim |
| R-6 | Existing recovery endpoint full class passed 6/6; S6's new correction integration matrix remains pending |
| V-27/V-28/V-29 | Deferred to the separately owned S6 authorization/transaction/import integration methods; never marked passed by B-1 |
| Other V/R/Q IDs | Other component/shared/live work remains with the original plan owners. No B-1 manual operating gate is specified |

Fresh TRX confirms CP-2 and CP-2-qualified each contain all seven AdvisoryChangePolicyTests and
all five StageOutcomeSummaryTests, with zero failures/skips. CP-25 contains every requested full
class: InstructionBundleTests 78 (76 passed, 2 failed), ReviewEvidenceParserTests 21,
AgentTaskLandApprovalRequestTests 24, AgentTaskLandApprovalRecoveryTests 94,
AgentTaskLandApprovalPersistenceTests 10, CheckpointSourceApprovalTests 4,
InterimVerificationLandGuardTests 13, ReviewEvidenceRecoveryTests 21,
ReviewEvidenceRecoveryEndpointTests 6, PipelineSpeedIntegrationTests 5. All other classes pass;
there are no skipped cases.

The failed method is `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap`:
stage-code is 3,348 characters and stage-review is 5,014, against the unchanged 2,500 cap.
The exact six-argument method was rerun from a temporary detached checkout of task base
`3931a31149acbc63837fcdda13cad60fdd1117cb`: 6 executed, 4 passed, the identical two failed,
zero skipped, clean source and verified build. The bundles and guard file have zero task-range
diff. The detached checkout made no commits or branch changes and is removed after evidence
retention. This extra isolated baseline build/test is required failure attribution, not a broad
baseline suite or an additional feature checkpoint.

The combined final run's source is clean with verified build provenance, but its report refuses
qualification with `cleanup_refused` because it retained outputs after the red guard row. That
historical refusal remains intact. CP-2 was therefore rerun exactly once, without rebuilding,
as CP-2-qualified at the same SHA; both the checkpoint tool and the existing PowerShell validator
return `CHECKPOINT SOURCE VALID ... rows=1`. No edited/relabelled receipt was used. This is
required receipt qualification, not a confidence repetition. CP-25 remains red/unqualified.
Every build/row used a granted host slot, waited=0s. No deadline or assertion changed.

Three grouped runs consumed 342.003, 335.308 and 590.331 seconds of tool-recorded wall time.
Standalone row reports omit useful phase timings (serialized zeros are not zero cost); their
console reports and build log are retained. The baseline and qualification runs are disclosed
additional cost, as is the five-second leased tool bootstrap. No savings are claimed.

## Retained evidence and next action

The 85-file raw inventory is retained outside the disposable checkout at
`/work/repos/antiphon/.git/antiphon/code-evidence/f88c7bf2/producer-pack-index.json`, SHA-256
`64633856c4b839d17e21a6f1727027e708f3c8c4c86ab72f1302a4e919ad8745`.
It records original paths, retained paths, bytes and hashes, including every red attempt, the
unmodified qualified receipt, all TRX, build stamps/logs, guard search, derived manifest and exact
base reproduction. This inventory is not runtime approval. The extra CP-25 is a deterministic
copy of the original first table plus the brief-authorized guard row; input/output digests are
in `.antiphon/c1281-b1-plan-provenance.json` and retained in that inventory.

Run the inherited exact size guard after repairing D-2 policy bundles, then the six existing
instruction guards and required full affected classes without weakening the cap or losing
instruction pins. B-1's original owner remains the landing owner. Commission ordinary Review
only after the failed gate is repaired. S6 still owns registration and V-27..29. All PCs stay
pending for SourceLanding Mutation. Restart remains none.

## Pending controls and other slices

PC-1..PC-121 all remain pending for separately commissioned SourceLanding Mutation; executed=0.
B-1 owns PC-2..7 and contributes the component bindings for PC-84..98, PC-111 and PC-118.
No Mutation or live acceptance was performed. See the companion unbound mutation inventory.
V-22..26 remain S0 history; V-27..37/R-5..8 remain the shared integration contracts except any
explicit unchanged classes observed in CP-25. D-3/D-4/D-5 live and operating Q gates are separate.
Restart: none.

## Unedited checkpoint lines


### 20261015-133255-4fcc

```text
CHECKPOINT CP-2 commit=a80c4ed24fbdb7ef8f06ec57f7161f8f544e250a build=ok filter=/*/*/(AdvisoryChangePolicyTests*)|(StageOutcomeSummaryTests*)/* executed=12 passed=10 failed=2 skipped=0 trx=/work/worktrees/task-f88c7bf2/.antiphon/checkpoints/20261015-133255-4fcc/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=a80c4ed24fbdb7ef8f06ec57f7161f8f544e250a sourceState=clean buildSource=verified
```


### 20261015-134522-f9a8

```text
CHECKPOINT CP-2 commit=7ea3062cfe46fa4efe0d0423010b1ca931465cce build=ok filter=/*/*/(AdvisoryChangePolicyTests*)|(StageOutcomeSummaryTests*)/* executed=12 passed=11 failed=1 skipped=0 trx=/work/worktrees/task-f88c7bf2/.antiphon/checkpoints/20261015-134522-f9a8/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=7ea3062cfe46fa4efe0d0423010b1ca931465cce sourceState=clean buildSource=verified
```


### 20261015-135459-d4bc

```text
CHECKPOINT CP-2 commit=3bdef3882d31df49c292efab66646b3a198c7875 build=ok filter=/*/*/(AdvisoryChangePolicyTests*)|(StageOutcomeSummaryTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-f88c7bf2/.antiphon/checkpoints/20261015-135459-d4bc/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=3bdef3882d31df49c292efab66646b3a198c7875 sourceState=clean buildSource=verified
CHECKPOINT CP-25 commit=3bdef3882d31df49c292efab66646b3a198c7875 build=reused filter=/*/*/(InstructionBundleTests*)|(ReviewEvidenceParserTests*)|(AgentTaskLandApprovalRequestTests*)|(AgentTaskLandApprovalRecoveryTests*)|(AgentTaskLandApprovalPersistenceTests*)|(CheckpointSourceApprovalTests*)|(InterimVerificationLandGuardTests*)|(ReviewEvidenceRecoveryTests*)|(ReviewEvidenceRecoveryEndpointTests*)|(PipelineSpeedIntegrationTests*)/* executed=276 passed=274 failed=2 skipped=0 trx=/work/worktrees/task-f88c7bf2/.antiphon/checkpoints/20261015-135459-d4bc/rows/CP-25/run.trx slot=granted waited=0s dirty=0 source=3bdef3882d31df49c292efab66646b3a198c7875 sourceState=clean buildSource=verified
```


### CP-2-qualified-20261015-140718-4a37

```text
CHECKPOINT CP-2-qualified commit=3bdef3882d31df49c292efab66646b3a198c7875 build=reused filter=/*/*/(AdvisoryChangePolicyTests*)|(StageOutcomeSummaryTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-f88c7bf2/.antiphon/checkpoints/CP-2-qualified-20261015-140718-4a37/run.trx slot=granted waited=0s dirty=0 source=3bdef3882d31df49c292efab66646b3a198c7875 sourceState=clean buildSource=verified
```


### Exact base failure attribution

```text
CHECKPOINT CP-25-base commit=3931a31149acbc63837fcdda13cad60fdd1117cb build=ok filter=/*/*/InstructionBundleTests/each_stage_bundle_is_ascii_and_under_the_size_cap* executed=6 passed=4 failed=2 skipped=0 trx=/tmp/c1281-base-f88c7bf2/.antiphon/checkpoints/CP-25-base-20261015-140501-1cd7/run.trx slot=granted waited=0s dirty=0 source=3931a31149acbc63837fcdda13cad60fdd1117cb sourceState=clean buildSource=verified
```
