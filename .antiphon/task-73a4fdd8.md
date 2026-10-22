# CARD-0167 S3 repair (Code task 73a4fdd8): Review e4b591eb D1 and D2

Branch `feat/card-task-73a4fdd8`, worktree `/work/worktrees/task-73a4fdd8` (server2 mirror), start ref `f8ff1bc57fa036d2d0de902fc65f36bb2e34d6be` (Code task d86141ac's tip; d86141ac remains the landing owner of the S3 delta, this task lands as its own owner with the whole delta). Target master only. Plan: `docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan-s3-repair.md` (repair slices, every test change justified, closed checkpoint table; `import --plan` validated 4 rows).

## Commits (all pushed)

| SHA | Slice |
|---|---|
| `a0e6a844862313cb5f761bfdc216f3a41cec14ee` | Red witnesses for D1 and D2, plus the scaffolding they compile against: `Agent.PoolProviderProjectionJson`, migration `20261022110953_AddPoolProviderProjection` (ef-generated, `text` nullable, `Down` drops the column; sorts after master's `20261022074212` and rc/2026-10-20's `20261020142734`), `ProviderEndpointLayers.PoolFingerprint`. Predicate, stamp and validators untouched, so the three new tests are red. |
| `a5b307d379f83926c6becdb8ca9bc0b630a7ddb1` | The fixes and the plan. D1: the cold-launch pool stamp writes the fingerprint; `InheritedEnvMatchesPool` compares it (Ordinal) with what a fresh pool row would get from the current map; `PoolView` removed. D2: S1's URL rules extracted unchanged into `ProviderEndpoint.ValidateBaseUrl`; `ProviderEndpointSettings.ValidateBaseUrl` runs them after its own rules. |
| this commit | This report. The final checkpoint run starts at this SHA so the receipt validates against the reviewed tip; its CHECKPOINT lines are in the task completion message and in `.antiphon/checkpoints/<run>/report.md` on the mirror. |

## Red witness (unlisted build-slot runs, stated)

`dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c0167red/` (slot granted waited=180s held=318s, 0 errors) then `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c0167red/ -- --treenode-filter "/*/*/(AgentLaunchEnvTests*)|(ProviderEndpointApiTests*)|(ProviderEndpointProjectionTests*)/(C0167_pool_*)|(C0167_save_*)"` (slot granted waited=46s held=58s) at `a0e6a8448`: total 4, failed 3, skipped 0.

- `C0167_pool_match_compares_the_launch_time_projection_not_the_current_map`: `InheritedEnvMatchesPool(Delegate(launchedWith: null), task, f, names) should be False but was True` (defect (a)).
- `C0167_save_refuses_the_urls_the_launch_refuses`: `response.StatusCode should be UnprocessableEntity but was OK`.
- `C0167_save_path_refuses_every_url_the_launch_path_refuses`: `ProviderEndpointSettings.ValidateBaseUrl(url, ...)` did not throw.
- `C0167_pool_match_includes_the_agent_projection` (re-pinned) passed under the old predicate, as expected: its new stamps are ignored there.

Other unlisted runs, with reasons: `dotnet tool restore` + `dotnet ef migrations add AddPoolProviderProjection --project server` under build-slot (slot granted waited=181s held=94s; the only build of the server with the new column before the red build; its `server/bin` and `src/*/bin` were deleted), the checkpoint tool build (`bin-c0167tool/`, slot granted waited=0s held=8s) for `import --plan` and `wait`/`validate`. No whole-suite or Unit-lane run (brief: minimal testing). Repetitions of the red selection: 1 red; the green is the checkpoint run.

## D1 design (short; the plan has the full text)

The stamp is written in the claim transaction beside `PoolProjectId`/`LaunchEnvJson`, from the task project's map read through the dispatcher's own `ProjectProviderEndpointsAsync` (the same column the resolvers' `ApiKeyEnvResolver` reads; null when no resolver is registered, as both launch paths then project nothing), for `program.Kind`, with the agent's mode and own endpoint. The fingerprint is the projected env sorted by name (URL or `{{key:NAME}}` placeholder) plus the Codex `-c` argv tail, canonical JSON, taken before placeholder resolution: names and placeholders, never a value. The column is `text` because the comparison is an opaque Ordinal string and jsonb would reorder keys. A row that predates the column reads as launched with nothing. The rule is strict on purpose: an inherited env that happens to override every projected name is a mismatch (one extra cold launch), which is exact for Codex, whose argv tail no env overrides.

Witnesses in `AgentLaunchEnvTests`: (a) none then F: no match; (b) F then none/null: no match; (c) F then F', and F then the same host under another key name: no match; (d) F then F: match; Codex F vs F' (argv tail only): no match; null stamp still serves a project with no endpoint. The existing pool test's cases 1, 2, 3, 4 and 6 now carry the stamp a cold launch would have written (justified case by case in the plan); 5 and 7 are unchanged.

## D2 design

`ProviderEndpointSettings.ValidateBaseUrl` keeps every rule, order, code and message S2's API tests pin, then returns `ProviderEndpoint.ValidateBaseUrl(normalised, field)`. Anything S2 refuses keeps `provider_endpoint_invalid_url`; the five inputs the review executed (port 0; quote, backslash, U+00A0, U+0085 in the path) are 422 at save under S1's `provider_endpoint_base_url_port` / `_characters`. Both routes (`ProjectService` create/update, `AgentService` PATCH) go through `ProviderEndpointSettings.Validate`. `ProviderEndpointApiTests.C0167_save_refuses_the_urls_the_launch_refuses` pins the five over the project route (field named, URL never echoed, nothing stored); `ProviderEndpointProjectionTests.C0167_save_path_refuses_every_url_the_launch_path_refuses` pins parity over the whole launch roster (14 character/scheme cases, 4 port cases, the 5) and that accepted URLs normalise the same way.

## Landing: merge-tree versus rebase

`git fetch origin master` (`e5e19c1d3`), then `git merge-tree --write-tree origin/master HEAD` reports two `CONFLICT (add/add)` entries: `server/Application/Services/ProviderEndpointSettings.cs` and `tests/Antiphon.Tests/ApiKeys/ProviderEndpointApiTests.cs`. Both were ADDED by S2 and reached this branch through the carry commit `25ae6869` (bytes identical to master's: `git diff a0e6a8448 origin/master -- <both>` is empty); the merge base `a38e1e1bd` lacks them, so any D2 edit to those files is an add/add for a merge, with this start ref, whatever the edit. Every other file auto-merges. The landing itself rebases (the land service's conflict path is "Landing rebase conflicted"), and a rebase drops the carry: simulated in a scratch detached worktree at `a5b307d37` with `git rebase --onto origin/master a38e1e1bd`: `dropping 25ae6869 ... patch contents already upstream`, 6 commits replayed, exit 0, resulting tree `7896640cb2d8fce7c51419f1ea2b8a7c4985f1ca`, and after it the two S2 files differ from master only by the D2 hunks (7 and 35 lines). The scratch worktree was removed. If the land gate insists on a clean merge-tree rather than the rebase, the alternative is a re-cut of this branch with `-StartRef origin/master` (the brief forbids rebasing the pushed branch), which is the orchestrator's call.

## Checkpoints (closed list from the repair plan, one build, one run)

Final checkpoint run: started at this commit with `start --plan docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan-s3-repair.md --rows CP-1,CP-2,CP-3,CP-4 --expected-source-sha <this commit>`, then `wait` until the exit is not 75, then `scripts/validate-checkpoint-receipt.ps1` and the tool's `validate`. Expected: CP-1 37 (36 + the parity method), CP-2 12 (S2's 11 + the five-URL method), CP-3 62 (61 + the launch-time witness), CP-4 25. The lines are quoted unedited in the task completion message.

## Invariants, PCs, acceptance

The plan has no V-n/R-n IDs and no PC table; nothing is pending for Mutation from a plan row. Candidate controls for a SourceLanding Mutation: the stamp line in the dispatcher (drop it: witness (d) stays green but a live reuse after an endpoint change would match again; the unit witnesses (a)-(c) go red), the Ordinal comparison (compare only nullness: (c) goes red), and the S1 call at the end of `ProviderEndpointSettings.ValidateBaseUrl` (remove it: both D2 tests go red). The plan's two operator acceptance items are unchanged and operator-run after land.

Platform: pure C#, no `-Runner`/`-Platform` pin (runner defaults revision 2, global `server2`; desktop windows and server2 linux both eligible).

Secrets custody: nothing here reads, prints or stores a key value. The fingerprint holds env names, the URL and the `{{key:NAME}}` placeholder; the tests use `sentinel-0167-never-printed` only as a sentinel and assert it absent.
