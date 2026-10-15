# CARD-1281 S0 foundation Code evidence

S0 is implemented and pushed; CP-14..CP-16 and the full Review recovery class pass. **Final verification is incomplete:** whole Unit hit its unchanged 15-minute row deadline without a final TRX. This is not Full/green or Review-ready. Next: Code to finish bounded Unit verification and resolve the baseline documentation failure through its owner; then independent Review.

- Original Code task and landing owner: `04228bff-e4a8-4ecd-bdbb-9315c2e41395`.
- Assigned branch: `feat/card-task-04228bff`; worktree: `/work/worktrees/task-04228bff`.
- Task base: `7b04142e031be8304159dc6bf3981af27f058be5`.
- Actual implementation/tested source: `e39bccc6744f4a94724efd891d4acd360cee8941`. This report and the pending-control inventory are a subsequent documentation-only commit, not a new tested-source claim.
- Plans: `docs/superpowers/plans/2026-10-15-card-1281-pipeline-speed-plan.md` and `2026-10-15-card-1281-cheap-slices-checkpoints.md`; original plan/manifest pin is the task base. Filter correction is committed in `a219e1052611af334c0bc2bc3daf7d43e8f759e7`.
- Restart: none. No deployment, endpoint registration, automatic handoff, close service, qualification, or Mutation was performed.

## Implementation

Five new domain records and an additive CLI-generated migration persist correction identity/provenance, caller action/history, immutable acceptance revisions, and unique close receipts. PostgreSQL enforces the natural keys, including equality of nullable action workflow coordinates. AppDbContext rejects application updates/deletes to correction, observation, acceptance, and receipt rows.

The optional CreateAsync action overload stays inside AgentTaskService; it introduces no DTO or endpoint registration. Admission serializes card then action before existing task/evidence gates. A card-bound transaction exists even without the optional open-task gate. It rereads terminal status and the original card token, preserving unbound creation and explicitly commissioned post-Done Mutation. The handoff verifies scope, canonical request digest, predecessor/event and workflow coordinates; existing linkage returns the same child. Child insertion and action linkage use two saves in one transaction, with events remaining after commit.

Changed production paths: the five named files under server/Domain/Entities; server/Infrastructure/Data/AppDbContext.cs; server/Application/Services/AgentTaskService.cs; migration 20261015113607_AddPipelineSpeedFoundation, its generated designer, and AppDbContextModelSnapshot. New tests and nested interceptors are only in tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.cs. Existing shared fixtures are unchanged. D-2 and D-5 scopes are untouched.

## Ordinary verification and limits

| ID | Actual outcome |
|---|---|
| V-22 | Passed C1281_B1Schema: populated upgrade preserves report/detail/cost; all correction coordinates reload; unique bundle and application immutability; no migration/model drift |
| V-23 | Passed C1281_B2Schema: action tuple variants and nullable key, ordered observations, nullable pending state, immutable observations |
| V-24 | Passed C1281_B3Schema: acceptance/receipt payloads, distinct revisions, duplicate refusal, immutable acceptance and receipt, unset publication coordinates for readiness |
| V-25 | Passed C1281_B2AtomicChild: duplicate race, changed request and stale identities refuse, link-save/precommit rollback, committed response loss and service restart |
| V-26 | Passed C1281_B3AdmissionFence: explicit lock/NOWAIT witness, post-lock Done/token refusal, insertion-first queued work preserved, unbound/Post-Done Mutation admitted |
| R-5 | CP-16 passed 2/2; full CardWorkflowRunFactoryTests is its one selected method. Full ReviewEvidenceRecoveryTests separately passed 21/21 |
| R-7 | S0 admission portion passed through V-25. HTTP/receipt/delivery portions remain with S3/S6, not claimed here |
| R-8 | S0 schema/admission portion passed through V-24/V-26. Close-service/HTTP portions remain with S4/S6, not claimed here |
| Final Unit lane | Attempted; exit 5 at unchanged 15-minute deadline, no TRX or authoritative expanded counts. NOT passed; incomplete |

CP-14/15/16 fresh TRX contain exactly 3/2/2 intended methods, all passed, zero failures/skips. Their complete new class is covered by CP-14 plus CP-15. The full existing recovery class has 21/21, zero skips. Both receipt validations returned CHECKPOINT SOURCE VALID at the tested SHA. All checkpoint rows have slot=granted and waited=0s.

The timed-out Unit console reports Windows-only skips and one failure: OrchestratorInstructionsGuidanceTests.Routing_cross_references_resolve_to_an_existing_owner_heading(ops-http.md, 1), expected pointer count 1, actual 0. No Unit total is inferred from partial console output. A separate, bounded reproduction compiled the unchanged test and DelegateScriptRunner from the exact base and read exact base docs under an exported base root: 2 executions, 1 passed, the same ops-http case failed, 0 skipped. The relevant source/doc blobs also have zero base..HEAD diff. This establishes that documentation assertion as inherited; it does not qualify the rest of the interrupted Unit lane. Reproduction is a minimal project with the pinned TUnit/Shouldly versions, not a full base assembly run.

The explicit brief's no-Unit instruction conflicts with the higher-priority Final Code contract. The added Unit and full recovery rows obey that contract and are disclosed as unlisted additions. The 30-60 minute estimate was exceeded by build/verification and the required Unit attempt; the task does not silently redefine Final as CP-only. Remaining ordinary verification is whole Unit via bounded selections under unchanged deadlines, with platform-only cases explicitly disposed. No Final completion or Review approval is claimed.

V-1..21, V-27..37, R-1..4/R-6 and Q-1..5 belong to other commissioned slices or live gates; none was run or marked passed by S0. There is no separate S0 manual operational gate.

## Red-first evidence and unlisted work

- Initial test commit 3cdd98c34a26133d655c34d7de70caa9d3bf834a had a query-API compilation error, repaired in db69f38c3083273213d91fd4cc52436f5f60e6d5. This is not assertion red.
- The literal method-OR CP-14 filter executed zero tests. The documented TUnit 1.44 suffix-wildcard correction selected exactly the three intended methods; reused certified build at db69f38c3083273213d91fd4cc52436f5f60e6d5 produced three missing-table assertion failures. The scoped manifest's three S0 rows were corrected, not broadened to extra cases. Original/extracted document hashes are retained.
- At a4cba07cc31dd43abcf569e43614e865d7f91161 schema 3/3 and regressions 2/2 passed; duplicate-child admission failed as intended, but the original card-lock witness timed out on the FK lock. That timeout is not acceptance red.
- Corrected observer at 698458b03f04ae5ab77638d6fc28116d9306b4f7 produced both intended admission assertion failures: same-key-child-count and admission-waits-for-card-lock. Production hooks were fixed only afterward.
- No unchanged green proof was repeated for confidence. Reruns followed compilation/filter/test-witness corrections or production implementation. No timeout was widened and no assertion loosened.
- Extra build: leased checkpoint-tool bootstrap (one short-SHA invocation refused before checkpoints); later invocations use --no-build. Extra build/tool invocation: leased isolated EF server build and repository-pinned dotnet-ef 9.0.20 CLI migration generation, required to generate the migration without hand-editing it. EF build: 0 errors; schema test confirms no model drift.
- Extra test row: corrected CP-14 no-build diagnostic, to prove the zero-selection manifest defect and obtain actual schema red. Extra Final rows: Unit and full ReviewEvidenceRecoveryTests, required by the standing Final profile. Extra minimal base build/test: exact inherited documentation failure attribution. All drivers used the host build-slot gate; no unleased test/build was run. EF/bootstrap/base labels also printed granted leases with waited=0s.

## Producer pack and pending controls

Raw evidence is ignored and copied byte-for-byte to the repository common-directory evidence store, surviving deletion of this disposable worktree:

`/work/repos/antiphon/.git/antiphon/code-evidence/04228bff/`

The 141-file inventory is `producer-pack-index.json`, SHA-256 `5afb848873bdd8d653a3587654445d239b88cc31ea59f664dbf23efd78b98661`. It records each original absolute path, retained relative path, byte count and SHA-256. It is a file inventory, not fabricated runtime approval. The original clean S0 producer report is retained at `.antiphon/checkpoints/20261015-115246-09c4/report.json` under that root, together with report.md, manifest/request/state, all TRX, build log and the unchanged checkpoint-build-source.json at its original relative path. Plan provenance/extracted bytes, all red/invalid attempts, the full recovery receipt, interrupted Unit evidence and base reproduction are retained separately in the same inventory. Keep original source/repository identity when importing for B-1/S6; this clean three-row pack is not a Full approval for the incomplete Final profile.

PC-1..PC-121: **all pending**, executed 0. S0's mapped PC-65..70 and PC-74..78, including all update/delete, sync/async, identity, rollback/restart and contender-order variants, are pending method-scoped SourceLanding Mutation. See `docs/superpowers/mutations/2026-10-15-card-1281-s0-unbound.md` for source bindings and extra guard discovery still owed by Mutation. Other component PC IDs remain pending, not waived.

## Exact receipts

### 20261015-112352-05e0

```text
CHECKPOINT CP-14 commit=3cdd98c34a26133d655c34d7de70caa9d3bf834a build=failed filter=/*/*/PipelineSpeedIntegrationTests/(C1281_B1Schema)|(C1281_B2Schema)|(C1281_B3Schema) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=3cdd98c34a26133d655c34d7de70caa9d3bf834a sourceState=clean buildSource=unknown
```

### 20261015-112658-ecc4

```text
CHECKPOINT CP-14 commit=db69f38c3083273213d91fd4cc52436f5f60e6d5 build=ok filter=/*/*/PipelineSpeedIntegrationTests/(C1281_B1Schema)|(C1281_B2Schema)|(C1281_B3Schema) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-04228bff/.antiphon/checkpoints/20261015-112658-ecc4/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=db69f38c3083273213d91fd4cc52436f5f60e6d5 sourceState=clean buildSource=verified
```

### CP-14-20261015-113130-006f

```text
CHECKPOINT CP-14 commit=db69f38c3083273213d91fd4cc52436f5f60e6d5 build=reused filter=/*/*/PipelineSpeedIntegrationTests/(C1281_B1Schema*)|(C1281_B2Schema*)|(C1281_B3Schema*) executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-04228bff/.antiphon/checkpoints/CP-14-20261015-113130-006f/run.trx slot=granted waited=0s dirty=0 source=db69f38c3083273213d91fd4cc52436f5f60e6d5 sourceState=clean buildSource=verified
```

### 20261015-113643-80ac

```text
CHECKPOINT CP-14 commit=a4cba07cc31dd43abcf569e43614e865d7f91161 build=ok filter=/*/*/PipelineSpeedIntegrationTests/(C1281_B1Schema*)|(C1281_B2Schema*)|(C1281_B3Schema*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-04228bff/.antiphon/checkpoints/20261015-113643-80ac/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=a4cba07cc31dd43abcf569e43614e865d7f91161 sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=a4cba07cc31dd43abcf569e43614e865d7f91161 build=reused filter=/*/*/PipelineSpeedIntegrationTests/(C1281_B2AtomicChild*)|(C1281_B3AdmissionFence*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-04228bff/.antiphon/checkpoints/20261015-113643-80ac/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=a4cba07cc31dd43abcf569e43614e865d7f91161 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=a4cba07cc31dd43abcf569e43614e865d7f91161 build=reused filter=/*/*/(CardWorkflowRunFactoryTests*)|(ReviewEvidenceRecoveryTests*)/(CreateFromRevision_snapshots_every_stage_in_order_with_queued_status_and_first_stage_pointer*)|(C1043_LockOrder*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-04228bff/.antiphon/checkpoints/20261015-113643-80ac/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=a4cba07cc31dd43abcf569e43614e865d7f91161 sourceState=clean buildSource=verified
```

### 20261015-114407-d9a0

```text
CHECKPOINT CP-15 commit=698458b03f04ae5ab77638d6fc28116d9306b4f7 build=ok filter=/*/*/PipelineSpeedIntegrationTests/(C1281_B2AtomicChild*)|(C1281_B3AdmissionFence*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-04228bff/.antiphon/checkpoints/20261015-114407-d9a0/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=698458b03f04ae5ab77638d6fc28116d9306b4f7 sourceState=clean buildSource=verified
```

### 20261015-115246-09c4

```text
CHECKPOINT CP-14 commit=e39bccc6744f4a94724efd891d4acd360cee8941 build=ok filter=/*/*/PipelineSpeedIntegrationTests/(C1281_B1Schema*)|(C1281_B2Schema*)|(C1281_B3Schema*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-04228bff/.antiphon/checkpoints/20261015-115246-09c4/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=e39bccc6744f4a94724efd891d4acd360cee8941 sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=e39bccc6744f4a94724efd891d4acd360cee8941 build=reused filter=/*/*/PipelineSpeedIntegrationTests/(C1281_B2AtomicChild*)|(C1281_B3AdmissionFence*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-04228bff/.antiphon/checkpoints/20261015-115246-09c4/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=e39bccc6744f4a94724efd891d4acd360cee8941 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=e39bccc6744f4a94724efd891d4acd360cee8941 build=reused filter=/*/*/(CardWorkflowRunFactoryTests*)|(ReviewEvidenceRecoveryTests*)/(CreateFromRevision_snapshots_every_stage_in_order_with_queued_status_and_first_stage_pointer*)|(C1043_LockOrder*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-04228bff/.antiphon/checkpoints/20261015-115246-09c4/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=e39bccc6744f4a94724efd891d4acd360cee8941 sourceState=clean buildSource=verified
```

### Final-Unit-20261015-120023-7f17

```text
CHECKPOINT Final-Unit commit=e39bccc6744f4a94724efd891d4acd360cee8941 build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=15m slot=granted waited=0s dirty=0 source=e39bccc6744f4a94724efd891d4acd360cee8941 sourceState=clean buildSource=verified
```

### Final-Recovery-20261015-121539-56c9

```text
CHECKPOINT Final-Recovery commit=e39bccc6744f4a94724efd891d4acd360cee8941 build=reused filter=/*/*/ReviewEvidenceRecoveryTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-04228bff/.antiphon/checkpoints/Final-Recovery-20261015-121539-56c9/run.trx slot=granted waited=0s dirty=0 source=e39bccc6744f4a94724efd891d4acd360cee8941 sourceState=clean buildSource=verified
```

## Commits before the reporting slice

```text
3cdd98c34a26133d655c34d7de70caa9d3bf834a test(card-1281): specify foundation schema acceptance before mappings
db69f38c3083273213d91fd4cc52436f5f60e6d5 test(card-1281): use EF interpolated scalar query API
a219e1052611af334c0bc2bc3daf7d43e8f759e7 feat(card-1281): map foundation records and specify admission red witnesses
a4cba07cc31dd43abcf569e43614e865d7f91161 feat(card-1281): generate additive pipeline foundation migration
698458b03f04ae5ab77638d6fc28116d9306b4f7 test(card-1281): distinguish missing card lock from FK insert contention
e39bccc6744f4a94724efd891d4acd360cee8941 feat(card-1281): serialize card admission and atomically bind handoff children
```
