# Review 59554ebd: CARD-0167 S3 re-cut (Final, Full)

Subject: Code task 937df27f-aa28-42b4-8aed-67c329d1b6f3, feat/card-task-937df27f at b00b3638eb41993d089ad3591c80be8cabefce73 (base 9cf77dbad). It is a re-cut of 8c2a794e @ a36448ab4, which Review 4520ae2c found clean.

Verdict: clean under the operator verdict policy. There is no regression, no reachable warm-pool env mix-up, no secret, no migration-order problem and no stub test. One test-adequacy gap is disclosed below (the null-map mutation survives), with a Backlog suggestion.

## Receipt (checkpoint tool, server2)

Run 20261022-143025-5e73, plan `docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan-s3-recut.md`, one build (bin-c0167rc, deleted by the tool). Verdict GREEN exit=0; unlisted: none.

| CP | executed | passed | failed | skipped | state |
|---|---:|---:|---:|---:|---|
| CP-3 | 62 | 62 | 0 | 0 | clean/verified |
| CP-4 | 25 | 25 | 0 | 0 | clean/verified |
| CP-5 | 43 | 43 | 0 | 0 | clean/verified |
| CP-7 | 10 | 10 | 0 | 0 | clean/verified |
| CP-8 | 18 | 18 | 0 | 0 | clean/verified |

- `scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261022-143025-5e73/report.json -ExpectedSourceSha b00b3638e...`: `CHECKPOINT SOURCE VALID ... rows=5`, exit 0.
- `scripts/check-evidence-diff.ps1 -BaseRef 9cf77dbad -HeadRef b00b3638e`: commits=18, violations=0.
- The tree is clean.

## Delta checks

1. **Per-file equality.** Both ranges, 25ae68691..a36448ab4 and 9cf77dbad..b00b3638e, touch the same 30 S3 files. Their +/- lines are identical for every one, including AgentTaskDispatcher.cs and the model snapshot. Two files appear only in the new range: the addendum plan and the 937df27f report. The repair2 plan file has one extra line in the new range: the appended "Re-cut addendum" pointer. That is docs only. The Code report's claim that "every file" matches misses this line.
2. **Resolution.**
   - Master at 9cf77dbad, and still at origin/master, had a plain 2-argument `InheritedEnvMatchesPool(agent, task)` that compared only LaunchEnv. The tip deletes it.
   - The only instance callers are dispatcher :8122 (pinned) and :8170 (pool). Both pass `projectEndpoints`, which `ProjectProviderEndpointsAsync` fills and which is never null (`NoEndpoints` for a global-only task).
   - No 2-argument caller exists in server, src or tests. The tests call the static 4-argument predicate. Its null-map calls (AgentLaunchEnvTests :631, :693) are deliberate witnesses that null means "no endpoint".
   - `DeclinesReuseForClaudeEffort`, its stacked summaries and its two call sites match origin/master byte for byte. I diffed the method and the region :8246-8306 against master :8200-8260.
   - The effort decline and the stamp check compose in both orders:
     - Pinned arm: stamp mismatch → SpawnFresh before the idle and effort checks. A matching stamp with an override → SpawnFresh.
     - Pool arm: the effort decline comes first. A matching stamp on an overridden-launch row with a no-override task is still reusable. That is CARD-1481's documented residual, unchanged.
3. **Migration.**
   - origin/master (aa103e96b) ends with 20261022015609, 20261022074212 and 20261022095749. rc/2026-10-20 ends with 20261020142734. 20261022110953_AddPoolProviderProjection sorts after all of them.
   - The snapshot diff against origin/master is only the `PoolProviderProjectionJson` text column.
4. **CP-1/CP-2 not rerun.** None of ProviderEndpointProjectionTests, FakeLlmApiSelfTests, ProviderEndpointApiTests or ApiKeyApiTests references AgentTaskDispatcher (0 hits each). Their S3 lines are identical. The tree differs from a36448ab4 only by master's own commits, and CP-3/4/5 already apply the full migration chain to test schemas. Not needed.
   - Disclosure: the addendum omits the repair2 CP-6 (AgentTaskPoolTests + BootWedgeRelaunchParityTests) without saying so. It carries the 22 inherited reds (see below).
5. **Merge-tree.** `git merge-tree --write-tree origin/master HEAD` against aa103e96b is clean (tree 4c3016a62).

## Throwaway mutation (scratch detached worktree at b00b3638e, restored, worktree removed)

- **Change.** Dispatcher :8170 `.Where(a => InheritedEnvMatchesPool(a, claimed, null))`. I built it under a build slot and ran `(LaunchEnvLayersIntegrationTests*)|(PoolProviderProjectionStampTests*)|(ClaudeEffortByRoleDispatchTests*)|(AgentTaskPoolTests*)` with no filter on methods.
- **Result: 86 total, 64 passed, 22 failed.** The 22 are exactly the inherited AgentTaskPoolTests list that 4520ae2c recorded identical at base and tip:
  - C691 ×7
  - janitor ×4
  - release-sweep ×6
  - the four named cases: busy standing held event, mid-turn unpinned shop, per-directory cap, hold retraces, compacts first
- **No named case turned red. The mutation survives.**
- **What is missing.** No test drives `TryReuseWarmAgentAsync` or the tick's warm reuse with a project provider endpoint set. Only two test files combine the dispatcher with ProviderEndpoint:
  - LaunchEnvLayersIntegrationTests: the endpoint tests stop at spec build.
  - PoolProviderProjectionStampTests: drives TickAsync for the producer only, then calls the static predicate as the "recipient".
- **Impact.** The tip is correct. Under this mutation, a row stamped "no endpoint" would serve a task after the operator set F: a mix-up that nothing catches. The addendum's claim that CP-4/CP-7 cover the resolution is overstated. The policy does not make this a defect, because no test is a stub and nothing is reachable at the tip.
- **Backlog suggestion (one).** Add a dispatcher-level reuse witness. Seed a warm pool row stamped with the no-endpoint fingerprint and set project endpoint F. `TryReuseWarmAgentAsync` must return SpawnFresh on both the pool and the pinned arm. It should be red under a `null` map at :8122 and at :8170.

Cleanup: the tool deleted bin-c0167rc. I deleted the tool build's bin-rv59554 directories (tools/Antiphon.Checkpoints, src/Antiphon.HostCleanup) and the mutation worktree (with its bin-rv59554m).
