# CARD-0167 S3 Final Review (task e4b591eb) of Code task d86141ac

Outcome: FOUND, 2 defects. My receipt at the reviewed SHA is green and valid (CP-1 36, CP-3 61, CP-4 25), all three throwaway mutants go red on named cases, and the S2 carry and landing order check out. The defects are a stale warm-pool match and a save/launch validator disagreement. The second is the input class the brief named as a finding.

Subject: `feat/card-task-d86141ac` at `f8ff1bc57fa036d2d0de902fc65f36bb2e34d6be` (checked out itself; tree clean). Landing owner: Code task d86141ac-f05d-41b5-b8c9-6d0a0d8b1a2c.

## Defects

### D1. Warm-pool predicate compares the CURRENT project map on both sides, so a process launched under endpoint E (or none) serves a task that resolves to F (or none)

- **Where:** `server/Application/Services/AgentTaskDispatcher.cs:8218-8240` (`InheritedEnvMatchesPool` static seam) with `ProviderEndpointLayers.PoolView` (`server/Application/Services/ProviderEndpointLayers.cs:99-133`), fed by `ProjectProviderEndpointsAsync` (read at reuse time, `:7994`). The cold-launch stamp at `AgentTaskDispatcher.cs:5945-5957` records `PoolProjectId` and the inherited snapshot only. Nothing records which projection the process got.
- **Failure:** Pool delegate W (Claude, Inherit) launches for project P while P has no endpoint. The process has no `ANTHROPIC_BASE_URL`. The operator then sets P's Claude endpoint F (key K). The next P task with inherited `{}` computes agentSide = PoolView(Claude, {}, {Claude:F}, Inherit) = {F, `{{key:K}}`}, and taskSide is the same, so W is reused. The task runs direct to the provider, not through F. The reverse case is worse for custody. W launches with F (its env holds the resolved key VALUE). The operator clears or changes the map, for example to revoke a proxy. Both sides become {} (or F'), the match holds, and W keeps routing through F with the key for every later task. The branch's own pool witness pins the first shape as intended: `AgentLaunchEnvTests.cs:597-598` case 1 ("a plain delegate and a task on the same project both get the project endpoint: match"). Case 5 (`:624-625`, null map) pins the second. The predicate's inputs cannot represent launch-time state.
- **Why:** The brief's concern (3) and the operator verdict policy ("a warm-pool env mix-up"). The dispatcher's own invariant at `:5945` says "a warm process retains whatever LLM routing it launched with", so its record must say what it launched with. The plan's acceptance step 2 (set an endpoint, launch, then clear and confirm there is no `ANTHROPIC_BASE_URL`) can be defeated by a warm reuse. Note: project DefaultLaunchEnv has the same pre-existing staleness, but this slice introduces it for a typed proxy-and-key setting.
- **Fix:** At cold launch, stamp the pool row with the projection the process actually got. Use a new nullable column, or a stable fingerprint of the Selection's env and args (names and placeholders, never values), beside `PoolProjectId`. Then compare that stamp, not a re-read of the current map, with the task's current PoolView. Add witnesses for (a) launched with none, map now F, no match; (b) launched with F, map now empty, no match; (c) launched with F, map now F', no match; (d) launched with F, map still F, match. Do not fold the projection into `LaunchEnvJson`: `ComputeInheritedLlmEnvAsync` reads that column for children.

### D2. S2's save validator stores base URLs that S1's launch validator then refuses at launch

- **Where:** `server/Application/Services/ProviderEndpointSettings.cs:77-104` (`ValidateBaseUrl`, used by `ProjectService` create/update and the agent API) against `server/Application/Services/ProviderEndpoint.cs` `Validate` (re-run by `ProviderEndpointProjection.For` at launch). S2 refuses only `c <= ' '` and `0x7f`. It does not refuse `"`, `\`, Unicode whitespace or C1 controls, and it does not check port range (Uri accepts `:0`).
- **Failure:** Executed in a throwaway scratch test at f8ff1bc57. The chain was `ProviderEndpointSettings.ValidateMap` + `SerializeMap`, then `ProviderEndpoint.ParseMap`, then `ProviderEndpointLayers.Select(ClaudeCode, Inherit)`. All 5 inputs save with 200 and refuse at launch:
  - `http://127.0.0.1:0` refuses `provider_endpoint_base_url_port`.
  - `https://proxy.example.com/v1"x` refuses `provider_endpoint_base_url_characters`.
  - `https://proxy.example.com/a\b` refuses `provider_endpoint_base_url_characters`.
  - `https://proxy.example.com/a<U+00A0>b` refuses `provider_endpoint_base_url_characters`.
  - `https://proxy.example.com/a<U+0085>b` refuses `provider_endpoint_base_url_characters`.

  The run was 5/5 passed on the assertion "S2 accepts, launch throws ValidationException". Every Claude launch on that project then fails with a 422 from a launch path, after a save that reported success.
- **Why:** The brief, item (5): the deferred collapse "is a finding" if the two validators "DISAGREE in a way that stores a URL the launch path then refuses at launch time". It fails safe (no injection: launch refuses), so this is not a custody leak.
- **Fix:** Before S3 lands, make the save path reject what the launch path rejects. One option is the deferred collapse, calling `ProviderEndpoint.Validate` from `ProviderEndpointSettings.ValidateBaseUrl` and mapping to S2's `provider_endpoint_invalid_url` code if S2's pinned 422 code must stay. The other is copying the character set and port rule into `ValidateBaseUrl`. Add the five inputs above as save-time 422 cases. This edits an S2 file, so sequence it with S2's land: land S2 first, then put it in a re-cut S3 or a follow-up Code task that lands before S3.

## Receipt (Final, Full)

Checkpoint tool on server2. I built the tool under build-slot (`bin-e4b5tool/`, granted waited=0s held=15s). `start --plan docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan-s3-amendment.md --rows CP-1,CP-3,CP-4 --expected-source-sha f8ff1bc57fa036d2d0de902fc65f36bb2e34d6be`, run `20261022-102707-5aca`, then `wait`, exit 0:

```
CHECKPOINT CP-1 commit=f8ff1bc57fa036d2d0de902fc65f36bb2e34d6be build=ok filter=/*/*/(ProviderEndpointProjectionTests*)|(FakeLlmApiSelfTests*)/(C0167_*)|(ForClaude_sets*)|(ForGrok_requires*)|(ForCodex_emits*) executed=36 passed=36 failed=0 skipped=0 trx=/work/worktrees/task-e4b591eb/.antiphon/checkpoints/20261022-102707-5aca/rows/CP-1/run.trx slot=granted waited=120s dirty=0 source=f8ff1bc57fa036d2d0de902fc65f36bb2e34d6be sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=f8ff1bc57fa036d2d0de902fc65f36bb2e34d6be build=reused filter=/*/*/LaunchEnvLayersIntegrationTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-e4b591eb/.antiphon/checkpoints/20261022-102707-5aca/rows/CP-4/run.trx slot=granted waited=105s dirty=0 source=f8ff1bc57fa036d2d0de902fc65f36bb2e34d6be sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=f8ff1bc57fa036d2d0de902fc65f36bb2e34d6be build=ok filter=/*/*/(AgentTuiLaunchResolverTests*)|(AgentLaunchEnvTests*)|(PhoneHomeTaskRoutingTests*)/* executed=61 passed=61 failed=0 skipped=0 trx=/work/worktrees/task-e4b591eb/.antiphon/checkpoints/20261022-102707-5aca/rows/CP-3/run.trx slot=granted waited=105s dirty=0 source=f8ff1bc57fa036d2d0de902fc65f36bb2e34d6be sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 20m17s  sequential-equivalent: 8m21s  builds: 2  max-concurrent-builds: 1  rows: 3 green 0 red 0 skipped
outputs: deleted bin-c0167a/, bin-c0167c/
verdict: GREEN exit=0
```

`scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261022-102707-5aca/report.json -ExpectedSourceSha f8ff1bc57… -Rows CP-1,CP-3,CP-4` printed `CHECKPOINT SOURCE VALID source=f8ff1bc57fa036d2d0de902fc65f36bb2e34d6be rows=3` (exit 0). The CP-4 count is 25 (24 on master + 1), which matches the amendment.

The plan has no V-n/R-n IDs and no PC table. Operator acceptance item 2 (live proxy, UserPrompt confirmation) stays pending and operator-run after land. Item 1 belongs to S6. I ran no producer-to-recipient queue evidence: S3 changes no delivery path.

## Throwaway mutations (scratch detached worktree at f8ff1bc57, now removed)

I made one build (`bin-e4b5mut/`, build-slot granted waited=60s held=290s). Each mutation sat behind a `C0167_MUT` environment switch, so each run had exactly one mutation active. All runs used `dotnet run --no-build` under build-slot:

| Run | Filter | Result |
|---|---|---|
| none (baseline) | `(C0167ReviewScratchTests*)\|(AgentTuiLaunchResolverTests*)\|(AgentLaunchEnvTests*)/(Review_*)\|(C0167_*)` | total 15, failed 0 (5 scratch D2 cases + 10 C0167) |
| `pool`: predicate compares raw LaunchEnvJson vs inherited, no projection | `(AgentTuiLaunchResolverTests*)\|(AgentLaunchEnvTests*)/(C0167_*)` | total 10, failed 1: `C0167_pool_match_includes_the_agent_projection` (case 2, `should be False but was True`) |
| `overlay`: Endpoint mode also applies the project projection at 3b | same | total 10, failed 2: `C0167_agent_endpoint_outranks_agent_launch_env_and_override_outranks_all` (`:599`, `own.Spec.Env should not contain key "ANTHROPIC_API_KEY"`), `C0167_registry_applies_project_and_agent_endpoints_in_rank_order` |
| `log`: resolver log prints `BaseUrl` instead of `HostOf` | `AgentTuiLaunchResolverTests/(C0167_*)` | total 8, failed 1: `C0167_sentinel_key_value_reaches_env_and_nothing_else` (`should contain "host=proxy-0167.local"`, was `host=https://proxy-0167.local:84...`) |

The slot waits were 45s, 60s, 225s and 75s. I restored and removed the scratch worktree and deleted all `bin-e4b5*` directories.

## S2 carry and landing order

- `25ae6869` touches exactly S2's 15 files (`git diff --name-only a38e1e1bd 98b37901b`, the same set), and `git diff 98b37901b 25ae68691 -- <those files>` is empty. The bytes equal S2's reviewed tip `98b37901b` (the subject line names `4cbb2d304`, S2's earlier tip; only S2's re-cut onto a38e1e1bd differs between them, in non-S2 files).
- In a scratch worktree, `rebase --onto 98b37901b a38e1e1bd` from f8ff1bc57 drops the carry ("patch contents already upstream") and replays 4 commits. The resulting tree equals f8ff1bc57's.
- Second order: S2 rebased onto origin/master `e5e19c1d3` (as if landed), then S3 `rebase --onto <that> a38e1e1bd`. The carry is dropped again, 4 commits replay, and the tree equals `git merge-tree --write-tree origin/master f8ff1bc57`.
- `git merge-tree --write-tree origin/master(e5e19c1d34fd74ba488777758e1310205fce014f) f8ff1bc57` is clean (tree `91f3cc9af432ca0b81ddd83e1b34116582ac915f`).

## Custody and semantics (checked, no other findings)

- **Logs:** the one new log line prints `host=` from `Uri.Host` (no userinfo, port, path or query) plus `key=<NAME>`. The key NAME goes beyond the brief's "host only" wording but is not a value; the existing ApiKeyEnvResolver also logs names. This is a disclosure only. The `ToString` override is host plus name.
- **Key value paths:** the value enters only through `{{key:NAME}}` in an env VALUE, resolved last. Codex `env_key` is the variable name, and argv carries no placeholder or value (the tripwire still runs). `ComputeInheritedLlmEnvAsync` reads stored `LaunchEnvJson` (placeholders), and `delegate.ps1` sends only `X_LLM_PROJECT`/`X_LLM_KEY`. So a projected `ANTHROPIC_API_KEY`/`OPENAI_API_KEY` is not snapshotted into a child task row.
- **D-11:** `PhoneHomeLaunchPolicy.Project` (untouched) refuses `ANTHROPIC_API_KEY` for runner-bound Claude and `OPENAI_API_KEY` for runner-bound Codex by name, after resolution, on both launch callers (`AgentTaskDispatcher.cs:6333`, `AgentControlService.cs:493`). That covers every keyed Claude/Codex combination. A URL-only Claude endpoint and Grok pass through, as planned.
- **Codex:** the five `-c` pairs come after ExtraArgs (profile args, then caller args), so a caller `-c model_provider=…`/`base_url=…` is overridden, not honoured. The URL is S1-validated free of `"`, `\`, whitespace and controls before it is quoted into TOML.
- **Ranks:** 3b sits after the project default and before inherited; 5b sits after the agent's LaunchEnvJson and before the override and ExtraEnv, in both resolvers. Endpoint mode replaces the project endpoint (the overlay mutant is red). Endpoint mode with no stored endpoint is 409 `provider_endpoint_required`. Direct projects nothing.
- **No setting:** `Select` returns `Selection.None`, which adds no env and no args, and the log line is gated on `Source`. The migration backfills mode 0 = Inherit and the map `{}`, so behaviour is byte-identical by construction for Claude, Grok and Codex. The exact-list witness is Claude-only; the Grok and Codex no-setting launches are covered by the unchanged pre-existing CP-3/CP-4 cases (disclosure).
- **Carried S1 gaps:** the 14 character/scheme cases, 4 port cases, `For()` refusing a stored bad URL, the lookalike quote and `ToString` are all present and executed in CP-1. The `_port` and `ToString` cases were red at 383f3ed1a.
- **Disclosures:** `ValidateLlmProxyPreview` does not see a projected endpoint, so the local-proxy-marker warning can't reason about it; this is not misleading because it doesn't claim the route. A pinned pool row in Endpoint or Direct mode never warm-reuses while the project has an endpoint (only a hand-edited pool row; costs a relaunch, no mix-up).

## Other checks

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef a38e1e1bd… -HeadRef f8ff1bc57…` reported `commits=5 entries=1 violations=0`. From the task base `d57f4bc7b` it reported `commits=9 entries=2 violations=0`.
- Unlisted runs, with reasons: the tool build; one scratch mutation build; 4 scratch runs (1 baseline + 3 mutants, the brief's required mutations plus the D2 probe). There were no other builds or test runs.
- Platform: no `-Runner`/`-Platform` pin is needed (pure C#).
