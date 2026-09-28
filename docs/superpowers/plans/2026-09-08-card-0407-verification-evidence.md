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

## S2a in progress — task 96ae83d8 (2026-09-28)

Implementation commits: `eb6bc5e1bb21f5534dbd33252eda63b4c5298f37` adds the
structured decision service, exact grant evaluation, self-worker HTTP route,
typed question table and CLI-generated migration; `2b1026258e0e5a35f72b0d883ead47887f6fdf81`
updates six older fixtures to request Shared explicitly after CARD-0644 made a
fresh non-Git task default to Worktree. Both pushes were verified with
`git ls-remote origin refs/heads/feat/card-task-96ae83d8` immediately after commit.

Red-first evidence: the evaluator's temporary deny stub failed all 12 new Unit
executions; the service stub failed both new integration executions on expected
Continue/idempotency assertions; the mapped HTTP binder answered 400 to the
new malformed-JSON test that requires 422. The first attempted service red run
had a compile error in the test's local temp-workspace helper; it was corrected
before the behavioral red run and is not counted as red evidence.

Checkpoint manifest:
`docs/superpowers/plans/2026-09-28-card-0407-s2a-checkpoints.md`. The committed
run `20260928-083803-cf16` is green at `2b1026258e0e5a35f72b0d883ead47887f6fdf81`:

| CP | Filter | Executed | Passed | Failed | Skipped | TRX |
|---|---|---:|---:|---:|---:|---|
| CP-1 | `/*/*/*/*[Category=Unit]` | 3434 | 3434 | 0 | 34 | `.antiphon/checkpoints/20260928-083803-cf16/rows/CP-1/run.trx` |
| CP-2 | decision service class pair | 14 | 14 | 0 | 0 | `.antiphon/checkpoints/20260928-083803-cf16/rows/CP-2/run.trx` |
| CP-3 | `/*/*/AgentTaskDecisionQuestionApiTests/*` | 2 | 2 | 0 | 0 | `.antiphon/checkpoints/20260928-083803-cf16/rows/CP-3/run.trx` |
| CP-4 | four affected integration classes | 429 | 429 | 0 | 0 | `.antiphon/checkpoints/20260928-083803-cf16/rows/CP-4/run.trx` |

The earlier run `20260928-082155-3502` was red: CP-1 failed two known
`ScaledTimeProviderTests` timing cases under load; both passed in the next full
Unit lane. CP-4 failed six preexisting non-Git fixture creates at
`AgentTaskService.CreateAsync:873` (`workspace_default_not_git`), caused by the
CARD-0644 default; explicit Shared in those test helpers made the same full
class row green. The unchanged source path and the follow-up class result are
the basis for classifying these as fixture drift rather than CARD-0407 runtime
regressions.

Only these S2a subcases are implemented and executed: V-3 exact attribute
target/attribute-name denials; V-6 own task token positive; V-8 malformed JSON
422; V-9 bounded LineEndings Continue, non-None impact denials and sibling-path
denial; V-10 same-scope duplicate and changed-payload conflict; V-12 basic
status/CompletedAt/Result/queue non-effects. Existing S1 V-4/V-5/R-4 classes
were run as compatibility checks. The table's `Covers` cells identify subsets,
not full V/R discharge.

R-5 reinspection at this commit: `AutoContinuedAt` has no Application or
Infrastructure assignment, and `scripts/delegate.ps1` exposes Authority and
Continue but no `-AutoContinue` parameter. CARD-0294 S3 is still absent and
was not implemented as part of this slice.

**S2a remains open.** Required next Code work: complete V-2 link/junction and
question-time repository tests; V-3 two-target and category matrix; V-5
retry/session snapshot checks; V-6 self-token/session-token negative matrix;
V-7 full state/attempt/live/unique-binding matrix; V-8 all schema boundaries;
V-9 three categories and mixed-grant denial; V-10 cross-scope concurrency,
restart and database uniqueness; V-11 state races and atomic publication;
V-12 full non-effect snapshots; R-1/R-2 decision-service and R-4 legacy-field
matrix. Inspect and correct the session-token stale-versus-unrelated HTTP
classification and add an actual bounded history/page contract before claiming
V-16. Keep the endpoint unadvertised until S3. All PC-1, PC-6..16 and PC-18..24
remain pending method-scoped SourceLanding Mutation under this task's Final
verification profile; no Code or Unit green discharges them.

## S2a continuation — task ab6c59b3 (2026-09-28)

Branch `feat/card-task-ab6c59b3` continued from `20805c83`. The endpoint remains
unadvertised. Question-time evaluation now rejects existing directories, broken
links and escaped file/directory links. Admission distinguishes unrelated live
session credentials (403) from prior bound sessions (409), including a retired
session that made no earlier decision check. Cold, warm and standing dispatch
events retain a typed session binding. The additive
`AddAgentTaskDispatchSessionEvidence` migration was generated with the repo-local
EF CLI; its downgrade/upgrade test proves old events gain a null binding without
invented identity.

Question admission takes a short PostgreSQL `SHARE NOWAIT` task-table lock and
`FOR UPDATE NOWAIT` task-row lock. Lock contention retries with a new transaction
for up to one second, then returns 409 rather than approving through ambiguous
binding. This serializes status-only changes to another task sharing the same
session as well as changes to the target task. A blocking-lock attempt deadlocked
with a writer that held the row before its update; the `NOWAIT` repair and
bounded retry have a green focused decision-service row. The database test
pauses at the decision insert, proves a competing status update cannot commit,
then releases the check. Another isolated-schema test forces event insertion to
fail after the typed row insert: both roll back, no publish fires, and a retry
records one complete decision/event pair. A separate-connection bus observes
both rows only after commit.

Added ordinary coverage: exact two-target `.gitattributes` rules, three
category positives and nine impact denials; link retargeting after task
creation; self, session, sibling, capability, unrelated and retired session
token cases; task statuses, wrong attempts, stopped/detached/ambiguous sessions;
required fields, enum/numeric/path/text/raw-document boundaries; cross-scope
concurrent duplicate, property-order/path-normalized duplicate, changed Continue
and NeedsHuman payloads, later-attempt and cross-task request IDs, direct
unique-index violation; cancel, settlement and retry state-first races;
task/report/legacy-field and queue/event non-effects; prior Continue on
cancelled/unmarked turns; six Grok/Codex/ClaudeCode policy/no-policy
design-approval cases. These are executed method rows.

Checkpoint manifest gained CP-5 for migration, pool and standing dispatch
classes. At `72d88c32077263e194f6589515458c3dc5c96e83` run
`20260928-195249-a42e`: CP-2 31/31, CP-3 9/9, CP-4 437/437. CP-5 executed
60, passed 57, failed 3 in older fixture assertions before the new binding
assertions. Focused repeat `20260928-200236-441f` reproduced exactly those
three; baseline comparison `20260928-200516-4aa3` ran the same methods at
`20805c83` and classified all three **INHERITED**: standing T5/T6 expected
Dispatched/Failed but got Blocked, and pool Codex reuse expected a session but
got null. The new migration test and warm/standing binding assertions passed.
CP-1 executed 3440, passed 3439, failed one existing
`ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` deadline
under the concurrent run. Solo CP-1 `20260928-201110-617a` executed 3440,
passed 3438, with that deadline and an unrelated checkpoint-ownership timeout;
CP-1 had passed 3440/3440 at the preceding `a3e3437e` source slice. A final
latest-tip Unit green remains to be recorded. Every listed run used the
checkpoint runner's build-slot gate. The EF tool restore and migration creation
used `scripts/build-slot.ps1` as separate, necessary migration-generation work.

S2a is still open for the plan's full V-5 retry/session snapshot witness and
full V-12 card-revision, delivery/terminal spy and report-watermark non-effect
matrix. R-1/R-2 worker/reply-side cases and visible paged history are later
slices; an actual bounded history/page contract must precede any V-16 claim.
All S2a PCs remain pending method-scoped SourceLanding Mutation, including
PC-1, PC-6..16 and PC-18..24. CARD-0294 S3 remains absent and out of scope.
