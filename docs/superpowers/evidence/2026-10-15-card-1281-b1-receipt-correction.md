# CARD-1281 B-1 receipt correction evidence

Original Code task / landing owner: `f88c7bf2-bae7-499d-9b95-46a565561b26`.
Branch: `feat/card-task-f88c7bf2`. Worktree: `/work/worktrees/task-f88c7bf2`.
Base: `3931a31149acbc63837fcdda13cad60fdd1117cb`.

The authority is the B-1 binding in [the plan](../plans/2026-10-15-card-1281-pipeline-speed-plan.md)
and the component CP-2 in [the checkpoint manifest](../plans/2026-10-15-card-1281-cheap-slices-checkpoints.md).
This report initially records implementation and red evidence; final receipts follow after execution.

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
row's incomplete-result handler. The fix retains the assertion and deadline. Final run pending.

## Pending controls and other slices

PC-1..PC-121 all remain pending for separately commissioned SourceLanding Mutation; executed=0.
B-1 owns PC-2..7 and contributes the component bindings for PC-84..98, PC-111 and PC-118.
No Mutation or live acceptance was performed. See the companion unbound mutation inventory.
V-22..26 remain S0 history; V-27..37/R-5..8 remain the shared integration contracts except any
explicit unchanged classes observed in CP-25. D-3/D-4/D-5 live and operating Q gates are separate.
Restart: none.
