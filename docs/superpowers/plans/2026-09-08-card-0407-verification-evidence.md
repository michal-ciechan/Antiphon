# CARD-0407 verification evidence ledger

Design baseline: `33d906a7`. Landed amendment: `8377f6c7350fcc1318e025dcb5135994dd53201a`.
S1 implementation: `2c11351c3a62f15163c6177d03f7dc95d00aaa74`. S1 evidence/text-column: `76cee55a0d3eabc340a4850d7cacbbf916935bba`.
Worktree: `C:\Antiphon\worktrees\card-task-1e81672c`. Original Code task: `1e81672c`.
Build: `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-card0407/`. Isolated producer output only.

Ordinary V/R used `--no-build` against that output. `--list-tests` was not used as evidence.
PCs are pending SourceLanding Mutation (not executed in Code).

## S1 executed ordinary V/R

| Filter | TRX | total | executed | passed | failed | skipped | duration |
|---|---|---:|---:|---:|---:|---:|---|
| `/*/*/InternalDecisionPolicyTests/*` | `.antiphon/card0407-s1-policy3/card0407-InternalDecisionPolicyTests.trx` | 51 | 51 | 51 | 0 | 0 | 5.2s |
| `/*/*/AgentTaskInternalDecisionLifecycleTests/*` | `.antiphon/card0407-s1-lifecycle2/card0407-AgentTaskInternalDecisionLifecycleTests.trx` | 3 | 3 | 3 | 0 | 0 | 35s |
| `/*/*/AgentTaskCallerResolutionTests/*` | `.antiphon/card0407-s1-caller/card0407-AgentTaskCallerResolutionTests.trx` | 7 | 7 | 7 | 0 | 0 | 40s |
| `/*/*/AgentTaskServiceIntegrationTests/*` | `.antiphon/card0407-s1-service/card0407-AgentTaskServiceIntegrationTests.trx` | 108 | 108 | 108 | 0 | 0 | 47s |
| `/*/*/*/*[Category=Unit]` | `.antiphon/card0407-s1-unit/card0407-unit.trx` | 2294 | 2293 | 2292 | 1 | 1 | 2m 35s |

Unit-lane inherited failure, also present at base `2fb81db3`: `ScopedVerificationInstructionTests.C487_G142` still expects `next: mutation when implementation...` while `stage-code` now says `next: review when implementation...`. Not caused by CARD-0407. One skip: symlink privilege. InternalDecisionPolicyTests contributed 51 of the Unit-lane executions.

### V/R ownership

| ID | S1 outcome |
|---|---|
| V-1 create/pure-policy | Done. 16/17 grants, 64/65 paths, 1000/1001 preserve, 20000/20001 JSON, empty-to-absent, duplicate ids, unknown/provenance fields, invalid enums. Service create 422 with no row: `Create_rejects_invalid_policy_before_task_creation`. Mapped HTTP `AgentTaskDecisionQuestionApiTests.Create_rejects_invalid_policy_before_task_creation` pending S2a. |
| V-2 create/pure-path | Done (lexical). Named files, `./` and slash normalize, sibling prefix denied, Ordinal vs ignore-case vectors, host comparer. Drive/UNC/rooted/traversal/wildcard/directory rejected (15 rows). Link/junction/question-time pending S2a. |
| V-3 create/attribute | Done (grant document). Two explicit targets; unlisted/broad/omitted targets and `.gitattributes` without LineEndings rejected. Question-time pending S2a. |
| V-4 | Done. `Internal_policy_create_role_matrix` enumerates all 17 `AgentTaskRole` values: 10 eligible Shared+Worktree accept; ReadOnly and 7 ineligible reject; Coverage Shared/Worktree positive and ReadOnly negative; `(AgentTaskRole)999` rejects. Capability and parent-task grantors stored. Scope does not create a grant. |
| V-5 create/lifecycle | Done. `Policy_snapshot_survives_only_same_task_requeue`: file edit, Refine forged grant, retry/escalation keep JSON+hash; follow-up/new/merge null; AutoContinue fields unchanged. Stage-role and specialist children: `Grant_bearing_task_does_not_leak_the_snapshot_to_stage_or_specialist_children`. Real pre-feature row: `AgentTaskInternalDecisionMigrationTests.Card0407_V05_pre_feature_row_upgrades_to_a_null_policy_with_legacy_fields_intact`. Question-time snapshot use pending S2a. |
| R-4 create/inheritance | Done. Existing authority/auto-continue/merge tests green; `auto_continue_true_with_authority_is_stored_without_a_runtime_fire`; merge copies authority not AutoContinue or policy. |
| R-5 | Inspected at Code start: `AutoContinueOnWait` written only at create; `AutoContinuedAt` has no application writer; `delegate.ps1` has Authority/Continue, no `-AutoContinue`. Runtime S3 absent. Do not implement as CARD-0407. |

## Pending (not S1 ordinary V/R)

Question-time/HTTP/audit/helper/worker/canary rows stay pending for later slices. Mutation owns every PC.

### S1 PC inventory (pending Mutation)

| ID | Variants |
|---|---|
| PC-2 | Bypass version rejection; bypass unsupported-category rejection; bypass required-preserve rejection; bypass unknown-field rejection (4). |
| PC-3 | Raise grant limit 16→17; path limit 64→65; preserve 1000→1001; canonical JSON 20000→20001 (4). |
| PC-4 | Remove ReadOnly rejection; remove ineligible-role rejection; remove Coverage from the eligible set (3). |
| PC-5 | Replace exact equality with prefix match; independently bypass rooted, traversal, wildcard, and directory rejection (5). |
| PC-17 | Copy parent/follow-up policy onto new tasks; clear policy during same-task requeue (2). |

Total S1 Mutation cycles: 18. Not executed here.

## Later slices

S2a–S4b V/R/PC rows remain pending until those Code dispatches execute them.

## Review rejection follow-up (task f90fd539)

Review task `5872ee9c` rejected S1 at `ea49035896f400395706083a4fc4fd5a9bde222e` on two V-5 gaps. Both are closed on `feat/card-task-1e81672c`.

1. **Child routes.** `AgentTaskInternalDecisionLifecycleTests.Grant_bearing_task_does_not_leak_the_snapshot_to_stage_or_specialist_children` adds the two routes the original lifecycle test did not reach. A grant-bearing Orchestrator-kind Code task creates Review, Mutation, TestDesign and Code children plus an explicit `Stage = Review` child; each asserts null policy/hash/baseline, null `StandingAuthority` and false `AutoContinueOnWait` against a real parent link. A `SpecialistTaskRunner.RunAsync` Check run over the same task asserts the same nulls plus `ParentTaskId is null` on the row the runner builds directly.
2. **Migration upgrade.** `AgentTaskInternalDecisionMigrationTests.Card0407_V05_pre_feature_row_upgrades_to_a_null_policy_with_legacy_fields_intact` drives `IMigrator` down past `AddAgentTaskInternalDecisionPolicy` on a cloned schema, proves via `information_schema` that none of the three columns exists, writes to the row while downgraded, then upgrades through both S1 migrations. Asserts null policy/hash/baseline, unchanged legacy fields (title, goal, kind, role, tier, workspace, status, result, `StandingAuthority`, `AutoContinueOnWait`), `text`/`character varying` column types from `StoreInternalDecisionPolicyAsText`, a byte-stable re-grant afterwards, and no pending migrations or model changes.

### Defect found and fixed

`StoreInternalDecisionPolicyAsText.Down()` used the generated `AlterColumn` back to `jsonb`. PostgreSQL has no assignment cast from `text` to `jsonb`, so the statement fails with `42804: column "InternalDecisionPolicyJson" cannot be cast automatically to type jsonb` and S1 could not be rolled back. That also broke every existing test that migrates down past it. Confirmed at base `2fb81db3`: `StandingSpecialistRoutingMigrationTests` 4/4 green; at S1 `ea49035` 3/4 with that exact 42804; green again after the fix. `Down` is now two hand-written `ALTER COLUMN ... TYPE jsonb USING ...::jsonb` statements.

### Re-run V/R (isolated `bin-c407fu/`)

| Filter | total | passed | failed | skipped |
|---|---:|---:|---:|---:|
| `/*/*/AgentTaskInternalDecisionLifecycleTests/*` | 4 | 4 | 0 | 0 |
| `/*/*/AgentTaskInternalDecisionMigrationTests/*` | 1 | 1 | 0 | 0 |
| `/*/*/InternalDecisionPolicyTests/*` | 51 | 51 | 0 | 0 |
| `/*/*/AgentTaskCallerResolutionTests/*` | 7 | 7 | 0 | 0 |
| `/*/*/AgentTaskServiceIntegrationTests/*` | 108 | 108 | 0 | 0 |
| `/*/*/StandingSpecialistRoutingMigrationTests/*` | 4 | 4 | 0 | 0 |
| `/*/*/AgentTuiPersistenceTests/*` | 12 | 12 | 0 | 0 |
| `/*/*/AgentTaskLandApprovalPersistenceTests/*` | 7 | 7 | 0 | 0 |
| `/*/*/AgentTaskLandNotificationPersistenceTests/*` | 7 | 7 | 0 | 0 |
| `/*/*/AgentTaskLandingPersistenceTests/*` | 2 | 2 | 0 | 0 |
| `/*/*/CapacityRecoveryCompatibilityTests/*` | 4 | 4 | 0 | 0 |
| `/*/*/CardFilePrivacyMigrationTests/*` | 1 | 1 | 0 | 0 |
| `/*/*/OutputDistillationMigrationTests/*` | 1 | 1 | 0 | 0 |
| `/*/*/PostLandMutationAdmissionTests/*` | 27 | 27 | 0 | 0 |
| `/*/*/StandingSessionOwnershipTests/*` | 4 | 1 | 3 | 0 |

Every class that downgrades through `IMigrator` was re-run, because the migration fix is on that path.

`StandingSessionOwnershipTests.Upgrade_backfills_only_unambiguous_owners_and_preserves_recovery_state(0|2|500)` is an inherited failure, not CARD-0407's: it seeds `AgentTasks` with the current EF model while downgraded past `_StandingSessionContinuity`, so it fails on `42703: column "CompletionNoteDigest" ... does not exist` (added by `20260912122418_AddCompletionNoteDeliveryStamp`). Reproduced identically at base `2fb81db3` in a detached worktree.

The S1 PC inventory is unchanged; PC-17 still owns the inheritance mutations and now has the stage/specialist assertions to fail against.
