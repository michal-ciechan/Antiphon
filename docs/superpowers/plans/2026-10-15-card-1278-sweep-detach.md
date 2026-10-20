# CARD-1278 expiry sweep detaches its failed rows — Code plan

Card: CARD-1278 (follow-up of CARD-1275, found by Final Review 2f23c01d). Base: master
`a92e9ccf2f82545d04db0e7825b3381fa8b26015`. Branch `feat/card-task-2a0c10d9`.

## Design

`ComplexityRoutingService.SweepExpiredAsync` clears expired rows on the request's scoped
`AppDbContext`, which `AgentTaskService` shares. Before this card a lost race detached the failed
rows when the sweep tried again (the `when (attempt < SweepAttempts …)` filter at base `a92e9ccf2`); after the third lost race the rows it had cleared stayed
tracked as modified with their stale token. A default-tier create catches the error and adds the
warning `Routing read failed (DbUpdateConcurrencyException); using today's path.`, then its own
save sent the stale update again and failed (500).

Change: on a lost race the sweep detaches each row it cleared in that attempt (the failed row and
the rows that were rolled back with it), then reads again or, after `SweepAttempts` (3) saves,
rethrows. The bound and the propagated exception are unchanged, so an explicit-complexity create,
the unfiltered list and chain PUT/DELETE still fail as `docs/antiphon-api.md` describes; a
default-tier create is created with the warning. A lost race whose failed entries are not chain
rows is not the sweep's and propagates untouched (the `ex.Entries.All(e => e.Entity is ComplexityChain)` filter, kept).

## Slices

| Slice | Commit | Content |
|---|---|---|
| S1 | test | V-1..V-4 and this plan (red: V-1, V-3, V-4) |
| S2 | fix+docs | detach the cleared rows on each lost race; `docs/antiphon-api.md` sweep table, `docs/orchestration-loop.md` matrix tests, CARD-1275 plan sentence |

## Verification design

| ID | Kind | Test | Asserts |
|---|---|---|---|
| V-1 | new | `MatrixDefaultComplexityCreateTests.A_default_tier_create_whose_expiry_sweep_loses_every_race_is_created_with_the_warning` | a writer rotates the expired Plan/Medium row's token before each save that carries a modified chain row: `SweepAttempts` such saves, the task Queued on the role policy pair with exactly the `Routing read failed (DbUpdateConcurrencyException); using today's path.` warning, Complexity null, no Blocked event, no modified chain row tracked, the row still active |
| V-2 | new (converse) | `MatrixDefaultComplexityCreateTests.An_explicit_complexity_create_whose_expiry_sweep_loses_every_race_still_fails` | the same race with `Complexity: Medium`: `DbUpdateConcurrencyException`, `SweepAttempts` saves, no task row, the row still active |
| V-3 | new | `ComplexityChainRaceTests.A_sweep_of_several_rows_that_loses_every_attempt_leaves_none_of_them_modified` | two expired rows, only one loses each race: `ListLiveCellsAsync` throws after `SweepAttempts` saves; neither row is tracked as modified, the next save writes 0 rows, both rows still active |
| V-4 | changed | `ComplexityChainRaceTests.A_sweep_that_loses_every_attempt_lets_the_concurrency_error_propagate` (CARD-1275 V-23) | adds: after the throw no chain row is tracked as modified and the next save writes 0 rows |
| R-1 | regression | `ComplexityChainRaceTests`, `MatrixDefaultComplexityCreateTests` (whole classes) | the CARD-1275 race and sweep cases and the default-tier create cases |
| R-2 | regression | `ComplexityChainServiceTests`, `ComplexityChainRoleTests`, `ComplexityRoutingWalkTests`, `ComplexityCreateTests`, `MatrixDefaultComplexityRoutingTests`, `ComplexityEffectiveHttpTests` | other callers of `FindActiveAsync` / `LoadChainAsync` |

### Positive controls (pending for SourceLanding Mutation)

| PC | Mutant | Expected red |
|---|---|---|
| PC-1 | the last attempt rethrows without detaching (the pre-CARD-1278 `when (attempt < SweepAttempts …)` filter) | V-1 (`DbUpdateConcurrencyException` from the create), V-3, V-4 |
| PC-2 | detach `ex.Entries` (the failed row) and not the other rows cleared in that attempt | V-3 (the row that did not lose stays modified) |
| PC-3 | swallow the lost race on the last attempt (return the live rows) | V-2 (no exception), CARD-1275 V-23 |
| PC-4 | drop the bound (retry while the race is lost) | All four fixtures arm exactly `SweepAttempts + 1` competing commits (four today), so save five succeeds. V-1 fails its competitor count; V-2/V-3/V-4 fail the expected `DbUpdateConcurrencyException` because none is thrown. Use V-4 alone for the method-scoped readable-red witness; cancellation or timeout is not an accepted red. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1278/` | sweep-create | `/*/*/MatrixDefaultComplexityCreateTests/(A_default_tier_create_whose_expiry_sweep_loses_every_race_is_created_with_the_warning*)\|(An_explicit_complexity_create_whose_expiry_sweep_loses_every_race_still_fails*)` | V-1, V-2 | 2 tests; S1 V-1 failed, V-2 passed; S2 2 passed | 2 | 8 | false | n/a |
| CP-2 | S1-S2 | `CP-1` | sweep-race | `/*/*/ComplexityChainRaceTests/(A_sweep_of_several_rows_that_loses_every_attempt_leaves_none_of_them_modified*)\|(A_sweep_that_loses_every_attempt_lets_the_concurrency_error_propagate*)` | V-3, V-4 | 2 tests; S1 2 failed; S2 2 passed | 2 | 5 | false | n/a |
| CP-3 | S1-S2 | `CP-1` | sweep-classes | `/*/*/(ComplexityChainRaceTests*)\|(MatrixDefaultComplexityCreateTests*)/*` | R-1 | 0 failed, 0 skipped | 30 | 8 | false | n/a |
| CP-4 | S1-S2 | `CP-1` | routing-callers | `/*/*/(ComplexityChainServiceTests*)\|(ComplexityChainRoleTests*)\|(ComplexityRoutingWalkTests*)\|(ComplexityCreateTests*)\|(MatrixDefaultComplexityRoutingTests*)\|(ComplexityEffectiveHttpTests*)/*` | R-2 | 0 failed, 0 skipped | 30 | 12 | false | n/a |

Red-first is by commit order: S1 runs CP-1 and CP-2 (`--rows CP-1,CP-2`); S2 runs CP-1..CP-4.
