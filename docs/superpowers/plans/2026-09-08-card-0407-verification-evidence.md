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
| V-5 create/lifecycle | Done. `Policy_snapshot_survives_only_same_task_requeue`: file edit, Refine forged grant, retry/escalation keep JSON+hash; follow-up/new/merge null; pre-feature row null; AutoContinue fields unchanged. Question-time snapshot use pending S2a. |
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
