# CARD-1495 warm-pool reuse stamp witness - plan

Test-only. CARD-0167 S3 made warm-pool reuse compare the pool row's launch stamp
(`Agent.PoolProviderProjectionJson`) with what the task would launch with now, via
`InheritedEnvMatchesPool(agent, task, projectEndpoints)` at two `AgentTaskDispatcher` call sites
inside `TryReuseWarmAgentAsync`: the pinned pool delegate (`if (!InheritedEnvMatchesPool(pinned,
claimed, projectEndpoints))`) and the unpinned pool query (`.Where(a => InheritedEnvMatchesPool(a,
claimed, projectEndpoints))`). Review 59554ebd showed that passing `null` at either site turns no
named test red: the existing tests stop at the static predicate or at spec build.

## Change

Add four `[Arguments(ReusePath.Pool)]`/`[Arguments(ReusePath.Pinned)]` methods to
`tests/Antiphon.Tests/ApiKeys/LaunchEnvLayersIntegrationTests.cs` (the class that already drives
`TryReuseWarmAgentAsync` on an isolated schema). Each seeds a project, an idle pool delegate with a
live session, `PoolProjectId` = the project and a given stamp, then calls
`TryReuseWarmAgentAsync` with a queued task on that project: unpinned for `Pool` (reaches only the
pool-query call site), `AgentId` = the row for `Pinned` (reaches only the pinned call site).

| ID | Method | Expect |
|---|---|---|
| V-1 | `C1495_a_no_endpoint_process_is_not_reused_after_its_project_sets_an_endpoint(Pool)` | row stamped with the cold-launch stamp of a project with no endpoint (null); the project then sets an endpoint: `SpawnFresh`; endpoint cleared again: `Reused` |
| V-2 | same method, `(Pinned)` | as V-1 on the pinned path |
| V-3 | `C1495_an_unchanged_no_endpoint_map_reuses_the_process(Pool/Pinned)` | control: `Reused` |
| V-4 | `C1495_a_stamp_matching_the_current_endpoint_reuses_the_process(Pool/Pinned)` | control: row stamped from the project's endpoint: `Reused` |
| V-5 | `C1495_an_unknown_stamp_never_matches_with_or_without_an_endpoint(Pool/Pinned)` | control: `"unknown"` stamp is `SpawnFresh` with no endpoint and with one |

No product code changes.

## Positive controls (pending for SourceLanding Mutation)

| PC | Mutation | Must go red |
|---|---|---|
| PC-1 | pool-query call site: `InheritedEnvMatchesPool(a, claimed, projectEndpoints: null)` | V-1 (`(Pool)`), V-4 `(Pool)` |
| PC-2 | pinned call site: `InheritedEnvMatchesPool(pinned, claimed, projectEndpoints: null)` | V-2 (`(Pinned)`), V-4 `(Pinned)` |

V-3 and V-5 are controls and stay green under both mutations (a null map fingerprints as nothing,
and `"unknown"` equals no fingerprint).

### Checkpoints

One isolated build and one exact filter per row. Lane: server2. `Min` is a floor; confirm
`executed=` in each CHECKPOINT line (25 existing methods + 4 new methods x 2 cases = 33).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1495/` | s1 | `/*/Antiphon.Tests.ApiKeys/LaunchEnvLayersIntegrationTests/*` | V-1..V-5 and the whole affected class | all passed, 0 failed/skipped | 33 | 10 |

Unit lane: not run. The change adds Integration-category methods to one test class and touches no
product code and no Unit-category file, so no Unit test can change outcome; the brief asks for
minimal testing.
