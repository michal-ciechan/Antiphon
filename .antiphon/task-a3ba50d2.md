# CARD-1495 task a3ba50d2 (Code) report

Test-only: four `C1495_*` methods (x `[Arguments(ReusePath.Pool)]`/`(ReusePath.Pinned)` = 8 cases) in
`tests/Antiphon.Tests/ApiKeys/LaunchEnvLayersIntegrationTests.cs` drive
`AgentTaskDispatcher.TryReuseWarmAgentAsync` through each `InheritedEnvMatchesPool` call site with the
project's endpoint map read from the database. No product code change.
Plan: `docs/superpowers/plans/2026-10-22-card-1495-reuse-witness-plan.md`.

## Cases

- `C1495_a_no_endpoint_process_is_not_reused_after_its_project_sets_an_endpoint(Pool|Pinned)`: row
  stamped with the cold-launch stamp of a project with no endpoint (asserted null); the project
  sets an endpoint: `SpawnFresh`; cleared again: `Reused`.
- `C1495_an_unchanged_no_endpoint_map_reuses_the_process(Pool|Pinned)`: control, `Reused`.
- `C1495_a_stamp_matching_the_current_endpoint_reuses_the_process(Pool|Pinned)`: control, `Reused`.
- `C1495_an_unknown_stamp_never_matches_with_or_without_an_endpoint(Pool|Pinned)`: control,
  `SpawnFresh` with no endpoint and with one.

A `Pool` case leaves `AgentId` null (reaches only the pool-query site); a `Pinned` case sets
`AgentId` to the row (reaches only the pinned site).

## Mutation evidence (scratch detached worktree at 755308de, never committed)

PC-1, pool-query call site:

```diff
-                .Where(a => InheritedEnvMatchesPool(a, claimed, projectEndpoints))
+                .Where(a => InheritedEnvMatchesPool(a, claimed, projectEndpoints: null))
```

Run `--treenode-filter "/*/*/LaunchEnvLayersIntegrationTests/C1495_*"` (build-slot, waited=211s):
total 8, failed 2, succeeded 6:
- `C1495_a_no_endpoint_process_is_not_reused_after_its_project_sets_an_endpoint(Pool)`: got Reused, expected SpawnFresh
- `C1495_a_stamp_matching_the_current_endpoint_reuses_the_process(Pool)`: got SpawnFresh, expected Reused

PC-2, pinned call site:

```diff
-            if (!InheritedEnvMatchesPool(pinned, claimed, projectEndpoints))
+            if (!InheritedEnvMatchesPool(pinned, claimed, projectEndpoints: null))
```

Same filter (waited=75s): total 8, failed 2, succeeded 6:
- `C1495_a_no_endpoint_process_is_not_reused_after_its_project_sets_an_endpoint(Pinned)`
- `C1495_a_stamp_matching_the_current_endpoint_reuses_the_process(Pinned)`

Both mutations were restored (scratch worktree removed). PC-1 and PC-2 stay pending for
method-scoped SourceLanding Mutation.

## Landing

- Master: `git merge-tree --write-tree origin/master HEAD` clean at origin/master 445df8a80.
- rc/2026-10-20 (0714a9a89): does not apply. rc has no CARD-0167 S3 (no
  `Agent.PoolProviderProjectionJson`, `InheritedEnvMatchesPool(pinned, claimed)` is two-argument),
  so the tests would not compile there. Master-only.

## Verification

CP-1 (whole `LaunchEnvLayersIntegrationTests`, min 33) runs through the checkpoint tool at the
commit that adds this report; its CHECKPOINT lines are in the final message. Unit lane not run:
no product code and no Unit-category file changed; the brief asks for minimal testing.
