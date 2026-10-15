# CARD-1287 Q2 refinement: isolate two separate RC defects

Code/landing owner: `7a7b92ba-6819-4460-9776-a07787de775d`.
The caller's 20261015230349 refinement accepts the custody fix unchanged and
commissions only these four executions before reporting. Do not repair the two
unrelated failures in this task. Preserve the earlier verification outcomes;
the unrun whole Unit and Linux selections remain unverified, never passed.

Compare fetched current master `b7b2f112da759fe578b1a48ba16dfb747c55418c`
with morning baseline `cc8798c1fa4f3c2faa834283aef080d8e5fd28f0`.
Each source gets a clean detached diagnostic worktree, one isolated build and
two sequential method-only test hosts. No branch is created or switched.
Pass each worktree's committed HEAD as `--expected-source-sha`; use the existing
checkpoint CLI built from `951cffebd3e8b394e8ee22818a74a37e29220246`, with
`--repo-root` selecting the diagnostic source. Explicitly set `UseAppHost: false`
in the imported ignored manifest. Retain exact errors and fresh TRX counts.

- V-4: migration test alone once at current master and once at morning baseline.
- V-5: Git-object teardown test alone once at each of the same sources.
- R-4: classify each separate RC defect from observed results at both sources.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-5 | Current | `tests/Antiphon.Tests -> bin-q2-current/` | current-migration | `/*/*/HostCleanupOrchestrationTests/AddHostCleanup_upgrades_current_master_without_changing_existing_rows` | V-4 | one case, record actual pass/fail | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | Current | CP-5 | current-output-cleanup | `/*/*/OutputCleanupTests/nested_repository_tracked_output_is_owner_checked_and_kept` | V-5 | one case, record actual pass/fail | 1 | 2 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | Morning | `tests/Antiphon.Tests -> bin-q2-morning/` | morning-migration | `/*/*/HostCleanupOrchestrationTests/AddHostCleanup_upgrades_current_master_without_changing_existing_rows` | V-4 | one case, record actual pass/fail | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | Morning | CP-7 | morning-output-cleanup | `/*/*/OutputCleanupTests/nested_repository_tracked_output_is_owner_checked_and_kept` | V-5 | one case, record actual pass/fail | 1 | 2 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

Estimated additional cost: 16 minutes plus slot waits; no full assembly or
repetitions. Red is diagnostic evidence, never a green release gate. Earlier
R-1 red and R-2/R-3 unverified outcomes stay in the report. All inherited
PC-1..413 and variants remain pending method-scoped SourceLanding Mutation.
Restart: none. After this comparison, ordinary Review assesses the unchanged
custody fix; caller owns landing and separate repair commissions.
