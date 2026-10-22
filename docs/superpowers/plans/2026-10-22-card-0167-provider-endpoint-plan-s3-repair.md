# CARD-0167 S3 repair: Review e4b591eb D1 (warm-pool projection stamp) and D2 (save/launch validator parity)

Amends [the landed plan](2026-10-22-card-0167-provider-endpoint-plan.md) and [the S3 checkpoint amendment](2026-10-22-card-0167-provider-endpoint-plan-s3-amendment.md) for the repair Code task 73a4fdd8 (descendant of d86141ac at `f8ff1bc57`). Neither earlier file is edited (a branch edit of a plan master already carries conflicts at land); this file carries the repair slices and is the `--plan` the checkpoint tool runs. Target: master only. S2 is landed (`cbb73f50f`), so the carry commit `25ae6869` rebases to empty and `git merge-tree --write-tree origin/master HEAD` is clean.

## D1. The warm-pool predicate compared the current project map on both sides

`AgentTaskDispatcher.InheritedEnvMatchesPool` computed the candidate's projection and the task's projection from the project map AS IT IS NOW, so a pool delegate launched while the project had no endpoint matched a task on that project after an operator set F (the task then ran direct), and a process launched with F (holding the resolved key value in its environment) kept serving tasks after F was cleared or changed. The dispatcher's own cold-launch rule says a warm process retains whatever routing it launched with, so the record must say what it launched with.

### Design

- **New column** `Agents.PoolProviderProjectionJson` (`text`, nullable; migration `20261022110953_AddPoolProviderProjection`, after master's `20261022074212_AddProviderEndpoints` and rc/2026-10-20's `20261020142734_AddStandingAutomaticFreshRecovery`; `Down` drops the column). The type is `text`, not `jsonb`, because the value is compared as an opaque Ordinal string and jsonb would reorder keys on storage. It is NOT folded into `LaunchEnvJson`, which `ComputeInheritedLlmEnvAsync` reads for child tasks.
- **Fingerprint** `ProviderEndpointLayers.PoolFingerprint(kind, projectEndpoints, mode, agentEndpoint)`: `Select` with the same inputs the resolvers use, then the effective projection's env sorted by name (each value is the URL or the `{{key:NAME}}` placeholder) plus its argv tail (Codex's five `-c` pairs), as one canonical JSON document; null when nothing is projected (no setting, Direct mode, or a refusal the launch raises). Taken before placeholder resolution, so it never holds a key value. Including the argv tail means a Codex endpoint that differs only in `base_url` is a mismatch, which the inherit-list env view could not see.
- **Stamp** at the cold-launch block beside `PoolProjectId`/`LaunchEnvJson` (`AgentTaskDispatcher.cs` ~5945): `PoolFingerprint(program.Kind, <the task project's map, or null when no ApiKeyEnvResolver is registered, matching both launch paths>, agent.ProviderEndpointMode, agent's own endpoint)`. It is written inside the claim transaction with the other pool stamps and restamped on every relaunch of the row.
- **Predicate**: CARD-0260's inherit-list comparison of `LaunchEnvJson` against the task's inherited snapshot stays; then the stamp is compared Ordinal with `PoolFingerprint(task.AgentKind, currentMap, Inherit, null)`, what a fresh pool row would get now. The current map is read for the task side only. A row that predates the column (null) reads as launched with nothing and matches only a task that projects nothing. `PoolView` is removed (nothing else used it).
- **Conservative by construction**: a delegate launched with no projection whose inherited env happened to override every projected name is now a mismatch (one extra cold launch) rather than a match. That is the safe direction, and it is exact for Codex, whose argv tail no env can override.

### Test changes, each justified

`AgentLaunchEnvTests.C0167_pool_match_includes_the_agent_projection` pinned the stale behaviour:

| Case | Was | Now | Why |
|---|---|---|---|
| 1 | `Delegate()` (no stamp) + map F: match | `Delegate(launchedWith: Stamp(map))` + map F: match | A delegate with no stamp was launched with no proxy; a match there is defect (a) exactly. The delegate that matches is the one launched under F. |
| 2 | Endpoint-mode delegate, no stamp | Same, stamped with its own endpoint's fingerprint | Still no match; the stamp now says what it was launched with instead of relying on a re-read. |
| 3 | Direct delegate, no stamp | Same, stamped (null) | Still no match (null against F). |
| 4 | `Delegate(launchEnv: elsewhere)` + task inherited elsewhere + map F: match | Same delegate also `launchedWith: Stamp(map)` | Its inherited routing overrode the URL, but the process was launched under F (its key placeholder came from F); a delegate launched under none is now case (a). |
| 5 | unchanged | unchanged | No endpoint anywhere: two nulls match; a Grok-only map fingerprints to null for a Claude task. |
| 6 | `Delegate(kind: Codex)` + Codex map: match | stamped with the Codex fingerprint | Same reason as case 1. The `other` key-name case stays a mismatch on the inherit-list half. |
| 7 | unchanged | unchanged | An unsupported-kind entry fingerprints as null on both sides. |

New `AgentLaunchEnvTests.C0167_pool_match_compares_the_launch_time_projection_not_the_current_map`: (a) launched with none, map now F: no match; (b) launched with F, map now empty or null: no match; (c) launched with F, map now F' or the same host under another key NAME: no match; (d) launched with F, map still F: match; Codex F against F' (argv tail only): no match, F against F: match; the stamp holds the env names and the placeholder, never a value; a null stamp still serves a project with no endpoint. Red at `a0e6a8448` on (a) (`should be False but was True`).

## D2. The save validator stored URLs the launch validator refused

`ProviderEndpointSettings.ValidateBaseUrl` (S2, project and agent routes) refused only `c <= ' '` and `0x7f`, and did not check the port range; `ProviderEndpoint.Validate` (S1, re-run by `ProviderEndpointProjection.For` at every launch) refuses `"`, `\`, any Unicode whitespace or control, and ports outside 1..65535. `http://127.0.0.1:0`, `https://proxy.example.com/v1"x`, `https://proxy.example.com/a\b`, and paths carrying U+00A0 or U+0085 saved with 200 and were refused at every launch.

### Design

- S1's URL rules are extracted unchanged into `ProviderEndpoint.ValidateBaseUrl(value, urlField)`; `Validate` calls it first, then the key-name rules, so the launch path is byte-for-byte the same.
- S2's `ValidateBaseUrl` keeps every rule, order, code (`provider_endpoint_invalid_url`) and message the S2 API tests pin, and then returns `ProviderEndpoint.ValidateBaseUrl(normalised, field)`. Anything S2 refuses keeps S2's code; anything S2 accepts that S1 refuses now fails at save under S1's code (`provider_endpoint_base_url_characters`, `provider_endpoint_base_url_port`). Both routes (`ProjectService` create/update and `AgentService`) go through `ProviderEndpointSettings.Validate`, so both are covered.

### Tests

- `ProviderEndpointApiTests.C0167_save_refuses_the_urls_the_launch_refuses`: the five inputs over the project route are 422 under S1's codes, name `providerEndpoints.ClaudeCode.baseUrl`, never echo the URL, and store nothing. Red at `a0e6a8448` (`response.StatusCode should be UnprocessableEntity but was OK`).
- `ProviderEndpointProjectionTests.C0167_save_path_refuses_every_url_the_launch_path_refuses`: structural parity over the launch roster (the 14 character/scheme cases, the 4 port cases and the 5 named inputs): every URL the launch refuses is refused at save, under S1's code wherever S2 has no rule of its own; what the launch accepts the save accepts, normalised the same way. Red at `a0e6a8448`.

### Checkpoints

One build serves every row (the four rows read one test assembly); all rows are one slice group so the reuse is admitted.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S3R | `tests/Antiphon.Tests -> bin-c0167r/` | projection | `/*/*/(ProviderEndpointProjectionTests*)\|(FakeLlmApiSelfTests*)/(C0167_*)\|(ForClaude_sets*)\|(ForGrok_requires*)\|(ForCodex_emits*)` | D-1, D-3, D-5, D-6, carried S1 review gaps, D2 parity (+1 method) | 37 passed, 0 failed, 0 skipped | 15 | 12 |
| CP-2 | S3R | CP-1 | persistence | `/*/*/(ProviderEndpointApiTests*)\|(ApiKeyApiTests*)/*` | D-2, D-8, D-10 regression, D2's five save-time 422s (+1 method) | 12 passed, 0 failed, 0 skipped | 11 | 14 |
| CP-3 | S3R | CP-1 | resolver-unit | `/*/*/(AgentTuiLaunchResolverTests*)\|(AgentLaunchEnvTests*)\|(PhoneHomeTaskRoutingTests*)/*` | D-4, D-5, D-11, redaction witness, byte-identical witness, pool witnesses old and new (+1 method) | 62 passed, 0 failed, 0 skipped | 55 | 12 |
| CP-4 | S3R | CP-1 | resolver-layers | `/*/*/LaunchEnvLayersIntegrationTests/*` | D-4 on the legacy registry path, rank order across both resolvers | 25 passed, 0 failed, 0 skipped | 25 | 10 |

CP-2's fixture migrates the test database through the new migration. The pending-model-change assertions (`GetPendingMigrationsAsync` empty, `HasPendingModelChanges` false) live in `AgentTaskInternalDecisionMigrationTests`, outside these rows; the migration and snapshot were generated by `dotnet ef migrations add` from the model, so they agree by construction. No V-n/R-n IDs and no PC table exist in the plan; the plan's two operator acceptance items are unchanged and remain operator-run after land.
