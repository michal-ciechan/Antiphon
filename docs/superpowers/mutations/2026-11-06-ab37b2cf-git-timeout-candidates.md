# Unbound candidates: session-runner Git timeout budgets

Original Code / landing owner: ab37b2cf. Plan: docs/superpowers/plans/2026-11-06-ab37b2cf-git-timeout-budgets.md.

All PC-1 variants below are **pending for Mutation**. This is an unbound candidate inventory, not a mechanically eligible execution pack. SourceLanding Mutation binds exact source and patch digests and owns deliberate mutants, red/restore/green and missing-control discovery. Ordinary Code performs no deliberate mutants. No timeout/assertion is changed to rescue a red verification run.

Invariant: every inventoried real-Git test's effective TimeoutAttribute is at least max(60 seconds, 2x its recorded per-case p95); pure in-memory retention tests retain 15 seconds. The new witness obtains the real compiled method/class attribute rather than comparing constants.

For each budget variant, lower the indicated effective Git timeout from 60_000 milliseconds to its pre-repair value; build, execute only `/*/*/C1722GitTimeoutBudgetTests/Git_budget_covers_measured_method`, confirm actual budget assertion failures for the mutated class/method, restore the exact file, rebuild and run that same method green. A build/discovery/fixture failure or zero tests is not red. Run variants serially; do not combine variants that touch the same witness method.

| Variant | File under tests/Antiphon.SessionRunner.Tests/ | Site | Mutated milliseconds |
|---|---|---|---:|
| PC-1/cold-reset | C0824WorkspaceSlotColdResetTests.cs | class Timeout | 10_000 |
| PC-1/native-rehome | C0824WorkspaceSlotNativeRehomeTests.cs | class Timeout | 10_000 |
| PC-1/overflow | C0824WorkspaceSlotOverflowTests.cs | class Timeout | 10_000 |
| PC-1/process-scan | C0824WorkspaceSlotProcessScanTests.cs | class Timeout | 10_000 |
| PC-1/rehome | C0824WorkspaceSlotRehomeTests.cs | class Timeout | 10_000 |
| PC-1/reset | C0824WorkspaceSlotResetTests.cs | class Timeout | 10_000 |
| PC-1/branch-refs | C0824RunnerBranchRefsTests.cs | C0824_deletes_local_and_tracking_refs_at_expected_sha method Timeout | 30_000 |
| PC-1/retention-dispatch | C1677RetentionDispatchTests.cs | Recent_use_by_this_runner_keeps_the_tree method Timeout | 15_000 |
| PC-1/retention-engine | C1677RetentionEngineTests.cs | Clean_pushed_old_mirror_is_removed_without_force_or_prune method Timeout | 15_000 |
| PC-1/retention-inventory | C1677RetentionInventoryTests.cs | class Timeout | 15_000 |
| PC-1/retention-process | C1677RetentionNativeProcessTests.cs | class Timeout | 10_000 |

PC-2/census (pending): remove one recorded method row, run only `/*/*/C1722GitTimeoutBudgetTests/Budget_table_covers_every_git_method`, and require the independent reflected method census to fail. Restore and rerun green.

PC-3/pure-preservation (pending): add a 10_000 method timeout to `C1677RetentionDispatchTests.Capability_is_advertised_only_on_linux` (in-memory), run only `/*/*/C1722GitTimeoutBudgetTests/Pure_retention_methods_keep_their_original_budget`, require the actual 15_000 preservation assertion to fail, restore and rerun green.

No product, OS-selected factory, configuration absence handling or rc startup change is present. Restart: none. Native Windows timing collection remains a separate rc obligation; Linux metadata proof does not establish Windows timing percentiles.
