# CARD-0993 single transcript query

## Decision

Base 227a00a0e159d25984c367cc054153ecd28e20bf still opens two strong-arm queries per session. Use a scalar ordered `Take(1)` necessary-LIKE candidate floor inside the range query. One command materializes the suffix once; the unchanged complete-prompt matcher finds the true floor, then removes false-positive prefixes in memory. The database returns one candidate floor rather than a candidate stream that Npgsql drains on disposal. False candidates can overfetch a prefix; unknown retains the suffix instead of truncating evidence. No complete receipt means no strong items. Database errors and cancellation propagate unchanged.

Weak-arm probes, per-input resolution, original soleInput cardinality and fallback stay unchanged. Mixed sessions trim at the true strong floor before applying existing weak logic. The range suffix remains unbounded, as before; no full assembly run is needed.

## Files and slices

S0: this note first. S1: tests and frozen 227a00a0e builder, run CP-0 with unchanged production expecting guarded assertion failures. S2: merge queries, then ordinary closed rows. Files: `server/Application/Services/AttentionService.TaskInputs.cs`, `tests/Antiphon.Tests/Application/TaskInputAttentionCostTests.cs`. Evidence: ignored `.antiphon/checkpoints/` and `.antiphon/task-a3dcaef9.md`.

## Verification design

V-1: `Strong_arm_uses_one_query_without_draining_candidates`, 1 and 3 independent sessions in one poll with 200 trailing matching candidates each. Commands equal session count; consumed plus drained rows <= 204 per session. Restore old reader pair: both bounds fail on base. The existing interceptor gets opt-in drain measurement.

V-2: `Single_query_boundary_matches_frozen_builder`: 1/2/3 inputs, mixed weak/strong, shared receipt, first/last receipt, false candidate before true receipt, no complete receipt, null/system prompts, and shared sessions across tasks. Full serialized items equal a frozen pre-slice builder and written counts. Exact command bounds include unchanged weak probes; candidate SQL must include LIMIT, so even no-complete-receipt goes red on the base candidate stream. Result controls: omit complete matcher, omit later input/task, or change soleInput.

R-1: full TaskInputAttentionCostTests and TaskInputWeakArmBoundaryTests, retaining every CARD-0992 control. R-2: full AttentionServiceTests, AttentionFeedNoiseTests, TaskInputReadFailureTests, AgentTaskInputSpillTests, AgentTaskInputFallbackTests (212 base cases).

R-3: whole Unit lane required by the binding Final stage contract despite the brief's no-Unit house rule. Estimate 5 minutes. Confirm any inherited red method-scoped on the task base; no known exact exclusions were supplied. Never loosen assertions/timeouts.

Manual V-3: generated SQL scalar LIMIT 1, fresh TRX counts and source receipts, evidence diff base..HEAD, fetched-master merge-tree exit 0, remote task SHA equal HEAD, clean worktree.

### Positive controls (all pending SourceLanding Mutation)

| PC | Exact method | Mutation | Expected red |
|---|---|---|---|
| PC-1 | Strong_arm_uses_one_query_without_draining_candidates | Restore two-query stream/range pair | Commands and drain caps |
| PC-2 | Single_query_boundary_matches_frozen_builder | Accept first non-null prompt in per-input evaluation | no-complete case emits an item |
| PC-3 | Single_query_boundary_matches_frozen_builder | Ignore all but first task sharing a session | shared-session-tasks loses item |
| PC-4 | Single_query_boundary_matches_frozen_builder | Remove closed weak inputs from soleInput count | mixed implicit case differs |
| PC-5 | Single_query_boundary_matches_frozen_builder | Use latest retry baseline as floor | first receipt loses item |

No deliberate mutants run by Code. Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-0 | S1 | `tests/Antiphon.Tests -> bin-c0993-red/` | base-red | `/*/*/TaskInputAttentionCostTests/(Strong_arm_uses_one_query_without_draining_candidates*)\|(Single_query_boundary_matches_frozen_builder*)` | V-1, V-2 red | nonzero guarded assertion failures expected | 12 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c0993/` | single-query-and-weak | `/*/*/(TaskInputAttentionCostTests*)\|(TaskInputWeakArmBoundaryTests*)/*` | V-1, V-2, R-1 | all listed, 0 failed | 51 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `CP-1` | attention-adjacent | `/*/*/(AttentionServiceTests*)\|(AttentionFeedNoiseTests*)\|(TaskInputReadFailureTests*)\|(AgentTaskInputSpillTests*)\|(AgentTaskInputFallbackTests*)/*` | R-2 | all listed, 0 failed | 212 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | `CP-1` | final-unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1900 executed, 0 failed | 1900 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

Ordinary estimate 23 minutes plus authoring; pre-slice red 5 minutes. One isolated build per committed group, UseAppHost=false, forward-slash output. Run checkpoint tool with `--after S1` or `--after S2` and committed `--expected-source-sha`; wait until exit is not 75. Bind if ANTIPHON_TASK_TOKEN is present, otherwise state unbound. No repeats after green.
