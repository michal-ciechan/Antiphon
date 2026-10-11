# CARD-1186: dead-session recovery follow-ups

Plan date: 2026-10-11. Planning baseline and fetched `origin/master`:
`77648b30c8cfbe436187e1bb1c4a2bf0ff061bc5`. CARD-1186 was read with
`scripts/card.ps1 get CARD-1186 -Board Antiphon`; `history` returned no history.
All three premises still apply. Verification design is included for the brief's
code-or-decide handoff. **Next: decide**, solely for D-4's migration mode; the
artifact is complete under the explicitly stated transactional-migration default.

The brief names `docs/plans/`, which does not exist at this baseline. This file
uses the repository's current `docs/superpowers/plans/` convention, also used by
the CARD-1161 plan it references. This Plan dispatch changes only this file.

## Problem and ground truth

A dead-session failure can commit before its caller-note insert fails. The later
recovery now delivers a note, but omits the recoverable git warning. Its candidate
scan also includes an increasingly large Failed population without a matching
partial index. These are follow-ups to working recovery, not a proposal to redo
CARD-1167 or CARD-1182.

Paths below are relative to the repository; line numbers refer to the baseline.

| Card assumption | What this code does | Plan consequence |
|---|---|---|
| The recovered note omits obligation/git information. | `server/Application/Services/AgentTaskDispatcher.cs:4381` (`FailAndNotifyAsync`) appends obligation IDs and settlement digests to `FailureReason`, records `CommitRecoveryAbandoned`, and supplies `warning` and `git` to `BuildCompletionNote`. `:3138` (`RecoverLostDeadSessionFailureNoteAsync`) passes only the saved reason and land facts. | Restore the warning and header bit from durable events; do not append the reason twice. |
| Unresolved obligations could be loaded again. | `server/Application/Services/CommitRecoveryObligations.cs:29` treats a type-36 abandonment referencing a type-34 start as resolved. `Abandon` keeps the start ID followed by the sweep name and reason, truncating `Detail` to 4,000 characters. `Describe` contains the recovery command. | `LoadUnresolvedAsync` cannot recover these facts. Use the start/abandonment pair tied to this saved failure. |
| Failed note recovery already retries safely. | Dispatcher `:2912` selects qualifying Failed rows alongside Dispatched/Working, ordered by Id. `:3109` reuses an existing Delegation note by stamping it. Missing parents are stamped, while other enqueue failures remain retryable. Commits `b3c904bd72d464bee44b453dbf89144929459070` and `a8e007d520bed328daaab415ca5ed498eb297b32` contain these repairs. | Keep those branches, note digest, destination, cancellation, and idempotency behavior. |
| The Failed arm lacks a supporting partial index. | `server/Infrastructure/Data/AppDbContext.cs:2133`, its model snapshot, and `20260925150636_AddCompletionRecoveryIndexes.cs` define `IX_AgentTasks_MissingCompletionNote` with **both** `SourceLandingOperationId IS NOT NULL` and `Result IS NOT NULL`. Ordinary dead-session failures need neither. The broad Status index is not selective within Failed. | Add a separate index for the exact Failed recovery predicate. Do not replace the existing completion index. |
| The OR may prevent the new index from helping. | There is one tracked `AgentTasks` SELECT for both populations. A partial index for Failed cannot alone satisfy the open-task arm. | Preserve one SELECT and its snapshot using disjoint `UNION ALL` arms; prove the actual generated SQL's planner behavior. No performance improvement has been measured in Plan. |
| The CARD-1161 plan is stale. | `docs/superpowers/plans/2026-10-09-card-1161-dead-session-grace-clock-plan.md:244` says the enqueue-failure case is open as CARD-1167. `DelegationDispatchRecoveryBoundaryTests.DeadSessionFailedReceipt.cs` now has eligible/busy/crash arguments and `C1182_Gone_parent_stamps_the_lost_note_once`. | Correct that one historical status sentence and its argument list; preserve the historical checkpoint roster. |
| The grace plan's old statement counts can be copied. | `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.cs:25` now has **14** statement-budget arguments, not three. The live tick and first-death rows are 18; the isolated reconciliation row is 4. | Run the current 14-result method unchanged. A second candidate round trip is not budgeted. |

## Decisions

### D-1. Reconstruct only proven facts for the current saved failure

Extend `CommitRecoveryObligations` with a pure selection rule and a task-scoped
async loader for abandoned obligations belonging to a saved failure. The loader
reads only type-34 starts and type-36 abandonments for this task, with
`AsNoTracking`. Selection requires all of:

- A start and abandonment on the same task; the abandonment references the
  start's full D-format ID followed by a space.
- The current persisted `FailureReason` contains that exact start ID and its
  settlement in the writer's `id (settlement digest)` form.
- The abandonment `Detail` equals the first 4,000 characters (or whole shorter
  string) of `"{startId:D} dead-session reconciler: {FailureReason}"`, matching
  the existing writer. Do not equate event timestamps with `CompletedAt`;
  the writer takes separate clock readings.

Return distinct starts ordered by `StartedAt`, then ID for ties. A missing or
unmatched pair contributes no invented warning; the already-saved reason remains
in the note. A failed event read must not stamp the task as notified. Do not
interpret arbitrary failure text as a commit SHA or perform git operations.

Use one small shared formatting helper for `git=commit-recovery-abandoned:<short
IDs>` and the existing `Describe` warning paragraphs, called by the initial and
recovered paths. Preserve the full D-format obligation IDs, settlement digests,
literal git recovery recipes, and the statement that nothing was pushed. Use the
same deterministic ordering on both paths. The initial path still appends the
reason once and records abandonment in the same transaction as Failed.

Rejected: loading unresolved obligations (the failure resolved them), scraping
only the reason (cannot prove the linked event), including every historical
abandonment (retries can retain old task events), or persisting a second note
payload/adding columns for information already durably available.

### D-2. Keep existing note recovery gates and identity

In `RecoverLostDeadSessionFailureNoteAsync`, do the new read only after the
existing-note and missing-parent early returns, immediately before formatting a
new note. Supply its warning/git alongside the existing land facts.
`DelegationNoteDigest.Compute(task.FailureReason)` remains the digest; body
enrichment does not create a second notification identity. Do not rewrite an
already-persisted note. Repeated sweeps must produce one note and no additional
Failed or Abandoned events. Keep WhenIdle delivery, task/root correlation,
missing-parent stamping, transient retry, and cancellation propagation intact.

Rejected: calling `FailAndNotifyAsync` again from recovery, which repeats terminal
side effects, or widening this card to other failure-reminder paths.

### D-3. Exact additive index and a single two-arm candidate read

Index name and **separate EF model name**:
`IX_AgentTasks_MissingDeadSessionFailureNote`, nonunique B-tree on `Id`:

```sql
"Status" = 5
AND "ReplyTo" = 1
AND "ParentSessionId" IS NOT NULL
AND "CompletionNoteQueuedAt" IS NULL
AND "FailureReason" IS NOT NULL
AND "FailureReason" LIKE 'Session died before the task settled:%'
```

Use `HasIndex(t => t.Id, "IX_AgentTasks_MissingDeadSessionFailureNote")` before
setting the database name and filter. A second unnamed `HasIndex(t => t.Id)`
would reconfigure the existing index on those properties. Keep both indexes and
the primary key; do not add payload/include columns, backfill, or uniqueness.
Freeze the predicate literal inside the generated migration.

Introduce `DeadSessionTaskQuery.Candidates(AppDbContext)` as the single production
query builder used by the sweep: tracked open arm (Dispatched/Working) `Concat`
tracked Failed arm, then outer `OrderBy(t => t.Id)`. The statuses are disjoint,
so `UNION ALL` requires no deduplication. Materialize once in the dispatcher and
retain its recovered-notes-first processing and return-count semantics. Keep the
runner/state/grace enablement early returns before the read.

The failure prefix must remain a SQL literal in the Failed branch, with the same
case-sensitive prefix semantics as today. Inspect the provider's SQL instead of
assuming `StartsWith` translated suitably; use the constant LIKE expression in
the canonical query if needed. No user text goes into SQL literals.

Rejected: adding an index without explaining the production query; retaining OR
and merely forcing index selection; two round trips (changes snapshot and 14
statement-budget witnesses); or a new status/flag column. PostgreSQL must be able
to prove the query implies the partial predicate during planning, including a
prepared generic plan. [PostgreSQL partial-index rules](https://www.postgresql.org/docs/16/indexes-partial.html).

### D-4. Operator decision: transactional migration or concurrent rollout

**Explicit plan default, not operator approval:** CLI-generated, transactional
`CreateIndex`/`DropIndex`, matching the existing AgentTasks completion-index
migration. This keeps failure rollback atomic and makes S3 small. It can block
writes while building; Plan has not measured production AgentTasks size or lock
waits. Do not infer a harmless duration from a small synthetic fixture.

The caller must obtain one decision before commissioning S3: accept a scheduled
write-blocking migration window, or require a concurrent index rollout. The
former executes this plan as written. If concurrent rollout is required, revise
S3 before Code to include `IsCreatedConcurrently`, transaction-suppressed SQL,
invalid-index recovery and interruption tests, using the existing CARD-0698
migration as the repository precedent. Do not silently switch modes in Code.
Concurrent creation has different transaction and failure semantics.
[PostgreSQL CREATE INDEX](https://www.postgresql.org/docs/16/sql-createindex.html),
[Npgsql index configuration](https://www.npgsql.org/efcore/modeling/indexes.html).

For the chosen rollout, measure table/index sizes, current write activity and
long-running transactions; record generated SQL and rehearsal build/lock time.
The window's acceptable duration belongs to the operator. No production DDL or
restart is authorized by this Plan dispatch. S1 and S2 can be commissioned
independently while this decision is pending.

### D-5. Portable placement, bounded scope, no new session lifecycle

Read `GET /api/runner-defaults` and `GET /api/session-runners` on 2026-10-11:
defaults revision 2; the catalogue had eligible Linux and Windows capacity.
Use the live defaults at dispatch, without embedding a fleet host/location.
Omit `-Runner` and `-Platform`; `-Platform Any` clears an inherited OS pin.
Every checkpoint below names its lane through the Group and the lane map.

No Windows-only witness is required: this changes database reads, formatting,
and a historical plan sentence. The queue integration witness uses the existing
in-process `BridgeQueueHarness`, not a native provider or production runner.
It does not claim native terminal delivery qualification.

No session-waiting behavior, release rule, or grace clock changes. Existing busy
caller delivery still waits for queue eligibility/TurnEnd; it does not release
the caller's seat. With parking disabled there is no automatic release deadline
(CARD-1083). No stop, requeue, or parking change is included.

## Landable slices and collision map

Budgets are Code authoring time; checkpoint time is additional. Commit and push
each slice before its checkpoint group. Each slice is independently useful and
safe to land; S4's performance claim depends on S3, but S1/S2 do not depend on it.

### S1. Correct the historical status (30-minute maximum)

Only `docs/superpowers/plans/2026-10-09-card-1161-dead-session-grace-clock-plan.md`:
replace the stale open sentence with CARD-1167's landed commit and the current
eligible/busy/crash witness, and mention CARD-1182's missing-parent witness.
Do not rewrite that plan's historical CP counts or claim new runs occurred.
Manual source/citation review plus CP-1; no new documentation test.

### S2. Recover the obligation warning and git header (60 minutes)

- `server/Application/Services/CommitRecoveryObligations.cs`: D-1 selection,
  loader, and shared warning/git formatting.
- `server/Application/Services/AgentTaskDispatcher.cs`: call the formatter in
  `FailAndNotifyAsync` and load proven facts in `RecoverLostDeadSessionFailureNoteAsync`.
- `tests/Antiphon.Tests/Application/CommitRecoveryObligationsTests.cs`: V-2.
- `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.DeadSessionFailedReceipt.cs`:
  V-3 alongside the existing fault injector and receipt tests.

No new schema or DI registration. Close with CP-2..CP-6. This touches Dispatcher
but does not require edits to `AgentTaskReplyService` or `AgentSessionService`.

### S3. Add the partial index (45 minutes under D-4's default)

- `server/Infrastructure/Data/AppDbContext.cs`: separately named Id index.
- CLI-generated `server/Migrations/<timestamp>_AddDeadSessionFailureNoteIndex.cs`,
  its `.Designer.cs`, and `server/Migrations/AppDbContextModelSnapshot.cs`.
- New `tests/Antiphon.Tests/Application/DeadSessionFailureNoteIndexTests.cs`:
  V-4 migration/model/data-preservation witness.

Use the local EF tool after `dotnet tool restore`; scaffold with
`dotnet ef migrations add AddDeadSessionFailureNoteIndex --project server` through
the host build-slot wrapper. Follow the alternate-output procedure in
`docs/testing-and-build.md`; record this required scaffold/build as a reasoned
non-checkpoint tooling invocation. Review the generated delta: one new index,
no removal of `IX_AgentTasks_MissingCompletionNote`, no unrelated model churn.
No manually authored replacement migration. Close with CP-7..CP-8.

### S4. Isolate the query arms and prove the plan (60 minutes)

- New `server/Application/Services/DeadSessionTaskQuery.cs`: D-3 builder.
- `server/Application/Services/AgentTaskDispatcher.cs`: replace candidate LINQ
  with the canonical builder, preserving all later gates.
- New `tests/Antiphon.Tests/Application/DeadSessionRecoveryQueryTests.cs`:
  V-5/V-6 and local capture/seed helpers. Reuse the existing PostgreSQL isolation
  and JSON-plan traversal patterns; do not edit their shared fixtures.

Close with CP-9..CP-16. Keep the 14 current statement expectations unchanged.
If translation or counts differ, explain the exact command delta and repair the
query; do not quietly relax the budgets or planner assertions.

Collision reservations: S2/S4 own `AgentTaskDispatcher.cs`; S3 owns the DbContext
and migration snapshot, so serialize it against other schema edits and regenerate
against the accepted base when necessary. The brief's concurrent areas
`AgentTaskReplyService`, `AgentSessionService`, `tools/Antiphon.Checkpoints`,
`TaskParkPublicationService`, and `BlockedTaskSyncRecovery` are not touched.
Do not expand `antiphon.areas.json` merely to describe this plan.

## Risks and rollout

- Event matching must tolerate the writer's 4,000-character abandonment detail
  without importing an older failure or another task. D-1's unit negatives and
  the real enqueue-failure witness cover that boundary. Existing oversized
  FailureReason behavior is outside this card.
- The index's benefit depends on selectivity and generated SQL, not its presence
  in the catalog. Use both normal and generic planner modes, real parameters,
  and representative distributions. Never set `enable_seqscan=off` as acceptance.
- D-4 is the sole open operator decision. No live lock duration or production
  performance claim is made. Deploy the additive index before or with the query
  change; the old binary remains schema-compatible. Server startup currently
  migrates (`server/Program.cs:1023`), so coordinate activation with that choice.
- Roll back application code while leaving a successfully created additive index
  in place. Exercise Down only in an isolated clone for verification; production
  index removal is unnecessary for a binary rollback. No runner/client rollout.
- No build, test, migration, or mutation was executed by this Plan task.
  Post-land Mutation was **NOT run** and remains a separately commissioned stage.

## Verification design

### V-1. Accurate historical status

Review S1 against the two landed commits and the current receipt source.
CP-1 is whitespace validation only; the Code report must explicitly confirm
the sentence and witness names. Do not count that command as an executed test.

### V-2. Evidence-linked abandonment facts (Unit)

New `CommitRecoveryObligationsTests.C1186_Abandoned_facts_match_the_saved_failure`,
eight `[Arguments]`: single, multiple, foreign-task, wrong-start-id, missing-start,
different-failure, duplicate-abandonment, truncated-abandonment. Construct event
details independently of the production `Abandon` helper. Assert exact selected
IDs and settlement digests, no duplicates, deterministic order, and empty facts
for each unproven pair. The truncation row has a long persisted reason with the
named obligation before its end and a 4,000-character abandonment detail.

### V-3. Recovered note reaches the intended caller intact (Integration)

New `DelegationDispatchRecoveryBoundaryTests.C1186_Recovered_failure_preserves_obligation_guidance`,
four `[Arguments]`: one, multiple, busy, historical-decoys. Reuse `OpenSweep`,
`FailedNoteInsertFault`, and the real queue plus `BridgeQueueHarness`. Seed expired
starts (older than the 720-minute hold); let production fail and abandon them,
fault the subsequent note insert, and dispose that sweep context. Assert Failed
and abandonment are durable, no note/stamp exists yet, and unresolved is empty.
Recover through a fresh sweep/context, then sweep again.

Assert one queue row, the correct parent, unchanged failure reason/digest,
`git=commit-recovery-abandoned:` with every short ID, full obligation IDs and
settlement digests, and each literal recovery command plus the unpushed warning
in the header/body. Assertions must name expected literals, not compare two
calls to the production formatter. Deliver and require exactly one complete
UserPrompt receipt after the floor; a queue insert or Sent flag is insufficient.
Busy produces no prompt before TurnEnd and exactly one afterward. Decoys include
another task and an older failure on this task; neither recipe may appear.
One Failed event and one abandonment per real obligation remain; no kills occur.

R-1: current `C1161_Failed_caller_note_has_one_complete_user_prompt` (3 results)
and `C1182_Gone_parent_stamps_the_lost_note_once` (1) protect ordinary, busy,
lost-insert, and missing-parent behavior. R-2: the existing expired-obligation
test in `AgentTaskDeadSessionReconciliationTests` protects initial formatting.

### V-4. Generated migration preserves data and both indexes (Integration)

New `DeadSessionFailureNoteIndexTests.C1186_Migration_adds_only_the_recovery_index`:
one test in an isolated cloned database. Locate the generated migration and its
immediate predecessor dynamically. Migrate to the predecessor, seed eligible
and excluded tasks, apply Up, inspect `pg_index`/`pg_get_indexdef`, and assert a
valid, ready, nonunique Id index with the exact predicate. The previous completion
index must still exist unchanged. Snapshot task IDs/status/reasons/stamps before
and after. Down removes only the new index; re-Up succeeds and preserves rows.
Inspect the generated migration script as well as the model. R-3 is the existing
`AppDbContextModelTests.Current_model_matches_migration_snapshot` (1 result).

### V-5. Candidate membership, tracking, and one statement (Integration)

New `DeadSessionRecoveryQueryTests.C1186_Candidates_preserve_membership_and_one_read`:
one result with a hand-listed expected ID set. Include Dispatched, Working,
matching Failed with null Result/SourceLandingOperationId, a stamped failure,
ReplyTo=None, null parent, null/nonmatching/case-mismatched reason, other terminal
statuses, and matching-prefix suffix variants. Assert exact IDs, global Id order,
one captured command, SQL `UNION ALL`, and tracked entities. Invoke the same
builder used by the sweep. Also capture one production sweep to prove it calls
that builder; rerun R-1 after S4 to prove recovered rows are acted on and the
missing-parent row leaves the candidate set.

### V-6. Production SQL uses the partial index (Integration/Slow)

New `DeadSessionRecoveryQueryTests.C1186_Failed_arm_uses_the_partial_index`, four
arguments: normal/generic planning crossed with zero/32 owed Failed rows.
Each isolated clone has 50,000 ordinary Failed rows, 10,000 stamped matching
failures, 10,000 completed rows, 32 live open rows, and the specified owed rows.
Use deterministic seed data and `ANALYZE`. Capture the actual canonical EF query
and parameters; do not explain a hand-written substitute or only the Failed arm.

Run `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` on that SELECT. For generic mode,
prepare the real SELECT with its real parameter types and execute under
`force_generic_plan`; assert the prepared-statement generic counter increments.
A parameterless SELECT uses parameterless PREPARE syntax, not an empty type list.
Reuse the pattern in `TranscriptHotPathFixture.ExplainAsync`, without altering
that shared helper. Require the named new index in the Failed branch, no whole
AgentTasks sequential scan for that branch, and exact candidate cardinality.
Keep measured buffers/rows/time and SQL in ignored evidence. Index Scan and
Bitmap Index Scan are both acceptable; do not pin optimizer costs or wall time.

In a disposable transaction, drop only the new index and explain the identical
query as a negative control, then roll back. The index-use oracle must reject
that plan. Record the before/after buffer and row work rather than claiming a
fixed percentage improvement. Query correctness must be unchanged. If a generic
parameter prevents predicate implication, fix literal generation instead of
weakening the required planner witness.

R-4: `C1149_C1150_Statement_budgets` has 14 results with current source expectations.
R-5: `C1161_Dead_session_grace_is_monotonic` has 13 results. R-6 runs the existing
grace-window and expired-obligation methods after S4. These keep query refactoring
out of timing, holding, and failure semantics.

### Post-land mutation controls (pending)

These are separate SourceLanding, method-scoped red/green obligations. Never
count a zero-test, fixture, build, or SQL setup failure as a red assertion.

| PC | Deliberate production change | Exact detecting method filter | Required red |
|---|---|---|---|
| PC-1 | Omit recovered `warning` | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1186_Recovered_failure_preserves_obligation_guidance` | Missing literal recipe/warning in recovered receipt |
| PC-2 | Omit recovered `git` | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1186_Recovered_failure_preserves_obligation_guidance` | Missing git header bit |
| PC-3 | Ignore task/failure binding, separately | `/*/*/CommitRecoveryObligationsTests/C1186_Abandoned_facts_match_the_saved_failure` | Foreign or historical pair admitted |
| PC-4 | Replace the Failed arm with all Failed rows | `/*/*/DeadSessionRecoveryQueryTests/C1186_Candidates_preserve_membership_and_one_read` | Exact membership differs |
| PC-5 | Remove the new index from an isolated migrated database | `/*/*/DeadSessionRecoveryQueryTests/C1186_Failed_arm_uses_the_partial_index` | Index-use assertion fails on the actual SELECT |
| PC-6 | Reuse the unnamed Id model index and regenerate in isolation | `/*/*/DeadSessionFailureNoteIndexTests/C1186_Migration_adds_only_the_recovery_index` | Existing completion-index preservation fails |

### Execution, lanes, and cost

The brief's narrow scope overrides the generic whole-Unit recipe. No assembly or
namespace sweep. Lane map: CP-1 Docs; CP-2/CP-8 Unit; CP-3..CP-7 and CP-9,
CP-11..CP-16 Integration; CP-10 Integration/Slow planner. **All use the Linux
lane by preference and are portable; Windows-only witnesses: none.**

Use the checkpoint tool `run --plan <this-file> --after S<n>
--expected-source-sha <committed-sha>`, one run per committed slice, and keep
calling `wait` until the exit is not 75. Await all work before settling. Bootstrap
the tool through the host build-slot procedure; its drivers own their slots.
Do not wrap a slot-owning checkpoint driver in another slot. Other required
build/scaffold/test drivers use `scripts/build-slot.ps1`. Exit 4 is not run,
never authorization for an unleased retry. Preserve unedited CHECKPOINT lines,
counts, SHAs and receipts in the stored report; generated evidence stays ignored.
Run the full task-range evidence-diff guard in Code/Review. Clean only the listed
alternate output directories after owned runs finish.

Authoring budgets: S1 30, S2 60, S3 45, S4 60 minutes (195 total maximum).
Ordinary checkpoint floor: **68 minutes**, the sum below; these are estimates,
not measurements from this Plan. Budget each Code dispatch for its slice plus
rows (31/79/55/98 minutes), plus slot waits. Review independently runs the closed
roster at the reviewed SHA. Planner evidence is a gate, not an optional benchmark.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | n/a | docs-status | `git diff --check HEAD^ HEAD` | V-1 | exit 0; manual status/citation review reported | n/a | 1 | true | n/a |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1186-s2/` | unit-obligation-facts | `/*/*/CommitRecoveryObligationsTests/C1186_Abandoned_facts_match_the_saved_failure*` | V-2 | 8 executed, 0 failed/skipped | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | `CP-2` | integration-recovered-guidance | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1186_Recovered_failure_preserves_obligation_guidance*` | V-3 | 4 executed, 0 failed/skipped | 4 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S2 | `CP-2` | integration-failed-receipt | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1161_Failed_caller_note_has_one_complete_user_prompt*` | R-1 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S2 | `CP-2` | integration-missing-parent | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1182_Gone_parent_stamps_the_lost_note_once*` | R-1 | 1 executed, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S2 | `CP-2` | integration-initial-obligation | `/*/*/AgentTaskDeadSessionReconciliationTests/a_dead_session_task_whose_commit_recovery_hold_expired_is_failed_with_the_obligation_abandoned_by_name*` | R-2 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S3 | `tests/Antiphon.Tests -> bin-c1186-s3/` | integration-index-migration | `/*/*/DeadSessionFailureNoteIndexTests/C1186_Migration_adds_only_the_recovery_index*` | V-4 | 1 executed, 0 failed/skipped | 1 | 9 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S3 | `CP-7` | unit-model-snapshot | `/*/*/AppDbContextModelTests/Current_model_matches_migration_snapshot*` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S4 | `tests/Antiphon.Tests -> bin-c1186-s4/` | integration-candidate-membership | `/*/*/DeadSessionRecoveryQueryTests/C1186_Candidates_preserve_membership_and_one_read*` | V-5 | 1 executed, 0 failed/skipped | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S4 | `CP-9` | integration-slow-planner | `/*/*/DeadSessionRecoveryQueryTests/C1186_Failed_arm_uses_the_partial_index*` | V-6 | 4 executed, 0 failed/skipped | 4 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S4 | `CP-9` | integration-statement-budgets | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | R-4 | 14 executed, existing counts unchanged, 0 failed/skipped | 14 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S4 | `CP-9` | integration-grace-clock | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1161_Dead_session_grace_is_monotonic*` | R-5 | 13 executed, 0 failed/skipped | 13 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S4 | `CP-9` | integration-grace-boundary | `/*/*/AgentTaskDeadSessionReconciliationTests/the_grace_has_to_elapse_before_anything_is_failed*` | R-6 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S4 | `CP-9` | integration-obligation-hold | `/*/*/AgentTaskDeadSessionReconciliationTests/a_dead_session_task_whose_commit_recovery_hold_expired_is_failed_with_the_obligation_abandoned_by_name*` | R-6 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S4 | `CP-9` | integration-query-failed-receipt | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1161_Failed_caller_note_has_one_complete_user_prompt*` | R-1 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S4 | `CP-9` | integration-query-missing-parent | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1182_Gone_parent_stamps_the_lost_note_once*` | R-1 | 1 executed, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
