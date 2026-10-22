# CARD-0167 S3 — task d86141ac (Code, Final round) report

Outcome: S3 implemented and green. Both launch resolvers now apply the provider endpoint at merge ranks 3b (project) and 5b (agent) before `$env:` expansion, kind defaults and `{{key:}}` resolution; Codex's five `-c` pairs go after `ExtraArgs`; the warm-pool predicate includes the projection; the one resolver log line is host-only and the record's prose form never prints a path. The S1 Final Review's four carried gaps are closed with witnesses. CP-3 and CP-4 (and CP-1, rerun for the carried gaps) are GREEN through the checkpoint tool on server2 at the tested SHA with a validated clean receipt (second run; the first run is reported below with its roster-miss and retained outputs).

Plan: `docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan.md` (master, not edited). Amendment for this slice: `docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan-s3-amendment.md` (CP-4 floor 42 -> 25, the class's real size; CP-1 rerun).
Branch `feat/card-task-d86141ac`, worktree `/work/worktrees/task-d86141ac` (runner mirror, server2 Linux lane). Task base `d57f4bc7b504b542314c819417774df57a7fbd55`; started from origin/master `a38e1e1bd0f515d6ae7a6682f537a5fe5e4cdd49` (fast-forward, no commits of mine before it). Landing owner: this Code task d86141ac.

## Basing on S2 (say which you did)

S2 (Code task 26316601, tip `4cbb2d304`) was not on origin/master when I began and still is not (its task shows Blocked with a land attempt at 08:29; master tip now `b59706530950ad5fb334c216a6f18a4e9bfd5103`). Its branch forks from `e0080cfc6`, before S1 landed, so my fast-forward-only branch could not be re-pointed at it, and the land replays commits with `git rebase` (`AgentTaskLandingProtocol.cs:257`): carrying S2's three original commits would replay `5180c2324`, whose `ProviderEndpointMode.cs` bytes differ from S1's on master (add/add). I therefore squashed S2's final tree into ONE commit on top of current master, bytes unchanged (`25ae6869`), and edited none of S2's files. When S2 lands first, the landing rebase replays that commit onto identical content and drops it as empty; `git merge-tree --write-tree origin/master HEAD` is clean today. If S2 is landed with different bytes, that commit (only) conflicts, and the fix is to re-cut it; no S3 file overlaps S2's.

Consequence for the brief's "ProviderEndpointSettings.Validate should collapse onto S1's validator": not done in this slice. It would edit an S2-added file (add/add conflict against master once S2 lands, until then invisible) and it changes S2's reviewed 422 codes (S2's API tests pin one code, `provider_endpoint_invalid_url`, where S1 reports per-rule codes), so CP-2 would turn red. The DTO <-> record mapping the collapse needs is delivered (`ProviderEndpointMapping`, used by the sentinel witness); the collapse itself is a one-file follow-up after S2 lands. Flagged for the caller.

## Commits (all pushed to `feat/card-task-d86141ac`)

| SHA | Subject |
|---|---|
| `25ae6869123548f3aeb8de378be9422784e32370` | chore: carry unlanded S2 content (feat/card-task-26316601 @ 4cbb2d304), one squash commit, bytes unchanged |
| `383f3ed1ac43630099d4d0fd62ac557fddd7684f` | test: S3 roster against a skeleton plus the S1 review's carried gaps (red witness SHA) |
| `4135dcb83269fb2cd08ed2fe82803c8f4c30a875` | feat: wire the provider endpoint into both launch resolvers at ranks 3b and 5b (first green run's SHA) |
| `852644a79e9283e6270fe07d30f1da9b828faf9d` | docs: S3 checkpoint amendment, CP-4 floor 25 (tested SHA of the valid run; source identical to 4135dcb83) |

S3 files: `server/Application/Services/ProviderEndpointLayers.cs` (new: `Select`, `Selection`, `HostOf`, `PoolView`), `server/Application/Services/ProviderEndpointMapping.cs` (new: DTO <-> record), `server/Application/Dtos/AgentLaunchSpec.cs` (`ProjectProviderEndpoints`, `AgentProviderEndpointMode`, `AgentProviderEndpoint`), `server/Application/Services/AgentTuiLaunchResolver.cs` (hoist in `AgentLaunchResolution.ResolveForAgentAsync`, fetch in `AttachProjectContextAsync`, ranks and the host-only log line in `ResolveCoreAsync`), `server/Application/Services/AgentRegistry.cs` (ranks, args), `server/Application/Services/ApiKeyEnvResolver.cs` (`GetProjectProviderEndpointsAsync` beside `GetProjectDefaultEnvAsync`), `server/Application/Services/AgentTaskDispatcher.cs` (`BuildLaunchSpec`/`BuildLaunchSpecAsync` inputs; `TryReuseWarmAgentAsync` loads the map once; static `InheritedEnvMatchesPool` seam), `server/Application/Services/CardService.cs` (named-definition card spawn passes the map), `server/Application/Services/ProviderEndpoint.cs` (S1 file: `NoEndpoints`, `_port` code, host-only `ToString`). Tests: the four plan classes plus `ProviderEndpointProjectionTests`. Untouched as the plan requires: `RealCliStubEnv.cs`, `PhoneHomeLaunchPolicy.cs`, `DelegationSettings.cs`, every S2 file, `tests/linux-test-roster.json`.

## Red witness (unlisted build-slot runs, plan D-14)

At `383f3ed1a` (tests plus a skeleton that only adds the named API surface), the five classes' `C0167_*` methods:

```
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0167-red-s3 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c0167red/ -- --treenode-filter "/*/*/(AgentTuiLaunchResolverTests*)|(AgentLaunchEnvTests*)|(PhoneHomeTaskRoutingTests*)|(LaunchEnvLayersIntegrationTests*)|(ProviderEndpointProjectionTests*)/(C0167_*)"
BUILD SLOT granted lease=3a45665e-445a-4a7f-80f1-e8475e9e67ba waited=0s maxcpucount=6
Test run summary: Failed!  total: 45  failed: 14  skipped: 0
BUILD SLOT released lease=3a45665e-445a-4a7f-80f1-e8475e9e67ba held=50s
```

executed=45 failed=14 passed=31. The 14 failed on their guarded assertions: `C0167_project_endpoint_outranks_project_default_env_on_the_same_name`, `C0167_agent_launch_env_outranks_project_endpoint`, `C0167_agent_endpoint_outranks_agent_launch_env_and_override_outranks_all`, `C0167_codex_endpoint_appends_five_args_after_extra_args_and_tripwire_passes` (args should be), `C0167_sentinel_key_value_reaches_env_and_nothing_else` (ANTHROPIC_API_KEY absent), `C0167_unsupported_kind_refuses_at_resolve` (should throw), `C0167_registry_applies_project_and_agent_endpoints_in_rank_order`, `C0167_pool_match_includes_the_agent_projection` (own-endpoint delegate matched), `C0167_legacy_registry_applies_project_and_agent_endpoints_in_rank_order` (from-box won), `C0167_Validate_reports_port_zero_and_out_of_range_by_code` (4 cases, no `_port` code), `C0167_ToString_prints_host_and_key_name_never_the_path` (the path printed). The 31 passing are S1's existing 12 results, the carried-gap pins of behaviour S1 already had (`C0167_Validate_refuses_characters_and_non_http_schemes` 14 cases, `C0167_For_refuses_a_stored_bad_url_without_echoing_it`, `C0167_Lookalike_quotes_cannot_close_the_codex_toml_string`), and the two inversion witnesses against the unmodified line that D-14 allows: `C0167_no_endpoint_leaves_env_and_args_byte_identical` (an exact expected list, not the resolver compared with itself) and `C0167_runner_bound_claude_endpoint_projection_is_phone_home_env_refused` (D-11: S1's projection against the untouched policy).

Other unlisted runs, with reasons: three `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c0167red/` under build-slot (compile checks before the test-first commit and before the implementation commit; one caught my own ` ` literals before any test ran), one green sanity of the same 45-case selection at `4135dcb83` (`c0167-green-sanity-s3`: total 45, failed 0, slot granted waited=0s held=63s), and the checkpoint tool build (`bin-c0167tool/`, slot granted waited=0s held=18s). Repetitions of the `C0167_*` selection: 1 red + 1 green; none after green.

## Checkpoints (closed list: CP-3 and CP-4 are the S3 rows; CP-1 rerun because the carried S1 gaps live in its class)

`import --plan` of the landed plan validated the table ("imported 7 rows"), and of the amendment ("imported 3 rows"), before each start.

### Run 1: `20261022-091757-320d` at `4135dcb83` — CP-1 and CP-3 green, CP-4 roster-miss on an unattainable floor, outputs retained, receipt INVALID

```
commit: 4135dcb83269fb2cd08ed2fe82803c8f4c30a875  branch: feat/card-task-d86141ac  worktree: /work/worktrees/task-d86141ac  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 4135dcb83269fb2cd08ed2fe82803c8f4c30a875 state=clean buildSource=verified
CHECKPOINT CP-1 commit=4135dcb83269fb2cd08ed2fe82803c8f4c30a875 build=ok filter=/*/*/(ProviderEndpointProjectionTests*)|(FakeLlmApiSelfTests*)/(C0167_*)|(ForClaude_sets*)|(ForGrok_requires*)|(ForCodex_emits*) executed=36 passed=36 failed=0 skipped=0 trx=/work/worktrees/task-d86141ac/.antiphon/checkpoints/20261022-091757-320d/rows/CP-1/run.trx slot=granted waited=105s dirty=0 source=4135dcb83269fb2cd08ed2fe82803c8f4c30a875 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=4135dcb83269fb2cd08ed2fe82803c8f4c30a875 build=ok filter=/*/*/(AgentTuiLaunchResolverTests*)|(AgentLaunchEnvTests*)|(PhoneHomeTaskRoutingTests*)/* executed=61 passed=61 failed=0 skipped=0 trx=/work/worktrees/task-d86141ac/.antiphon/checkpoints/20261022-091757-320d/rows/CP-3/run.trx slot=granted waited=60s dirty=0 source=4135dcb83269fb2cd08ed2fe82803c8f4c30a875 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=4135dcb83269fb2cd08ed2fe82803c8f4c30a875 build=reused filter=/*/*/LaunchEnvLayersIntegrationTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-d86141ac/.antiphon/checkpoints/20261022-091757-320d/rows/CP-4/run.trx slot=granted waited=75s dirty=0 source=4135dcb83269fb2cd08ed2fe82803c8f4c30a875 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 15m02s  sequential-equivalent: 6m00s  builds: 5  max-concurrent-builds: 1  rows: 2 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c0167a/, bin-c0167c/
verdict: RED exit=3
```

CP-4's `report.json` row: `state=roster-miss exitCode=3 executed=25 passed=25 failed=0`. The plan's CP-4 floor is 42 ("41 + 1 new"), but `LaunchEnvLayersIntegrationTests` has 24 `[Test]` methods on master (no `[Arguments]`) and 25 with S3's one new method, so no green run can reach 42; the row failed on the floor alone. The tool then kept both build outputs (`still-present` is `OwnedOutputRemains` after a red run, `CheckpointApp.cs` ~line 439), and both validators report `CHECKPOINT SOURCE INVALID reason=cleanup_refused` for this run, even for `-Rows CP-1,CP-3`. I deleted `bin-c0167a/` and `bin-c0167c/` by hand, wrote the amendment (floor 25, otherwise the same row), committed it, and reran all three rows.

### Run 2 (the valid receipt): `20261022-095440-6667` at `852644a79` — GREEN, clean, outputs deleted

`start --plan docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan-s3-amendment.md --rows CP-1,CP-3,CP-4 --expected-source-sha 852644a79…`, then `wait` (75 twice, then 0). The source is the implementation SHA plus the amendment doc only (`git diff 4135dcb83 852644a79 --stat` is that one Markdown file).

```
commit: 852644a79e9283e6270fe07d30f1da9b828faf9d  branch: feat/card-task-d86141ac  worktree: /work/worktrees/task-d86141ac  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 852644a79e9283e6270fe07d30f1da9b828faf9d state=clean buildSource=verified
CHECKPOINT CP-1 commit=852644a79e9283e6270fe07d30f1da9b828faf9d build=ok filter=/*/*/(ProviderEndpointProjectionTests*)|(FakeLlmApiSelfTests*)/(C0167_*)|(ForClaude_sets*)|(ForGrok_requires*)|(ForCodex_emits*) executed=36 passed=36 failed=0 skipped=0 trx=/work/worktrees/task-d86141ac/.antiphon/checkpoints/20261022-095440-6667/rows/CP-1/run.trx slot=granted waited=135s dirty=0 source=852644a79e9283e6270fe07d30f1da9b828faf9d sourceState=clean buildSource=verified
PHASES CP-1 slotWait=135s build=340.8649321s startup=16.613748s testsWall=0.1106545s teardown=0.8450871s hostWall=17.5847762s
CHECKPOINT CP-3 commit=852644a79e9283e6270fe07d30f1da9b828faf9d build=ok filter=/*/*/(AgentTuiLaunchResolverTests*)|(AgentLaunchEnvTests*)|(PhoneHomeTaskRoutingTests*)/* executed=61 passed=61 failed=0 skipped=0 trx=/work/worktrees/task-d86141ac/.antiphon/checkpoints/20261022-095440-6667/rows/CP-3/run.trx slot=granted waited=120s dirty=0 source=852644a79e9283e6270fe07d30f1da9b828faf9d sourceState=clean buildSource=verified
PHASES CP-3 slotWait=120s build=430.0437553s startup=81.0852507s testsWall=6.7491899s teardown=2.5560122s hostWall=90.3904527s
CHECKPOINT CP-4 commit=852644a79e9283e6270fe07d30f1da9b828faf9d build=reused filter=/*/*/LaunchEnvLayersIntegrationTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-d86141ac/.antiphon/checkpoints/20261022-095440-6667/rows/CP-4/run.trx slot=granted waited=225s dirty=0 source=852644a79e9283e6270fe07d30f1da9b828faf9d sourceState=clean buildSource=verified
PHASES CP-4 slotWait=225s build=0s startup=20.590758s testsWall=5.6266472s teardown=1.5295325s hostWall=27.746938s
unlisted: none (the tool ran no other build or test command)
wall: 18m00s  sequential-equivalent: 10m21s  builds: 2  max-concurrent-builds: 1  rows: 3 green 0 red 0 skipped
outputs: deleted bin-c0167a/, bin-c0167c/
verdict: GREEN exit=0
```

Validation: `scripts/validate-checkpoint-receipt.ps1 -Evidence <run>/report.json -ExpectedSourceSha 852644a79… -Rows CP-1,CP-3,CP-4` → `CHECKPOINT SOURCE VALID source=852644a79e9283e6270fe07d30f1da9b828faf9d rows=3` (exit 0); the tool's `validate` verb → the same line, exit 0.
Fresh TRX inspected (all `outcome="Passed"`, no skipped, no `[Arguments]` surprises):
- CP-1 `total=36 executed=36 passed=36`: 33 × `ProviderEndpointProjectionTests` (S1's 7 methods as 12 results; carried: `C0167_Validate_refuses_characters_and_non_http_schemes` ×14, `C0167_Validate_reports_port_zero_and_out_of_range_by_code` ×4, `C0167_For_refuses_a_stored_bad_url_without_echoing_it`, `C0167_Lookalike_quotes_cannot_close_the_codex_toml_string`, `C0167_ToString_prints_host_and_key_name_never_the_path`) + 3 × `FakeLlmApiSelfTests`. Plan floor 15.
- CP-3 `total=61 executed=61 passed=61`: `AgentTuiLaunchResolverTests` 22 (14 + the plan's 8), `AgentLaunchEnvTests` 30 (28 results from 22 methods + `C0167_pool_match_includes_the_agent_projection` + the extra `C0167_registry_applies_project_and_agent_endpoints_in_rank_order`), `PhoneHomeTaskRoutingTests` 9 (8 + `C0167_runner_bound_claude_endpoint_projection_is_phone_home_env_refused`). Plan floor 55.
- CP-4 `total=25 executed=25 passed=25`: `LaunchEnvLayersIntegrationTests` 24 + `C0167_legacy_registry_applies_project_and_agent_endpoints_in_rank_order`. Amended floor 25 (plan said 42, see run 1).
Roster: the plan's 11 new S3 methods are all present (8 resolver + 1 layers + 1 runner policy + 1 pool), plus 1 extra registry rank-order unit test and the 5 carried-gap methods (22 results) in CP-1's class.
Repetitions: CP-1 selection 2 (run 1 roster-pass, run 2 green), CP-3 selection 2, CP-4 selection 2 (run 1 roster-miss on the floor, run 2 green); none after green. No whole-suite or Unit-lane run (brief: minimal testing).
Evidence roots (gitignored, on the mirror): `.antiphon/checkpoints/20261022-091757-320d/` (run 1, retained, receipt invalid) and `.antiphon/checkpoints/20261022-095440-6667/` (run 2, valid). All `bin-c0167*` directories deleted (red/sanity output, the tool build, and both runs' outputs).

## Invariants, PCs, acceptance

- The plan defines no V-n/R-n IDs; its verification design is the CP table plus the decisions. CP-3 covers D-4, D-5, D-11, the redaction witness (`C0167_sentinel_key_value_reaches_env_and_nothing_else`: fake store returns `sentinel-0167-never-printed`; the child env has it; the captured resolver and key-resolver logs, three refusal messages, the DTO JSON, the record's prose form and the Codex argv do not), the byte-identical witness (an exact expected env list and args list for four no-setting shapes) and the pool witness. CP-4 covers D-4 on the legacy registry path through the dispatcher.
- PCs: the plan has no PC table (zero-PC plan); nothing listed is pending. Candidate mutants for SourceLanding Mutation, each with its detecting case: apply 3b after inherited env → `C0167_agent_launch_env_outranks_project_endpoint`, `C0167_registry_applies…`, `C0167_legacy_registry_applies…`; apply 5b before the agent's LaunchEnvJson or after the override → `C0167_agent_endpoint_outranks_agent_launch_env_and_override_outranks_all`; let Endpoint mode overlay 3b instead of replacing it → the `ShouldNotContainKey("ANTHROPIC_API_KEY")` assertions in the three 5b cases; let Direct remove the box value → `C0167_direct_mode_suppresses_the_project_endpoint`; put the Codex pairs before ExtraArgs or after `--model` → the two codex cases; log the full URL or restore the default `ToString` → the sentinel and `C0167_ToString…` cases; drop the projection from either side of the pool predicate → pool cases 1/2/3; skip the map fetch on the dispatcher's registry path → the legacy case; remove the `_port`, characters or scheme checks → the carried cases; project for OpenCode → `C0167_unsupported_kind_refuses_at_resolve`.
- Operator acceptance outside the table (plan): item 2 (set a Claude endpoint on a project with a running key proxy, launch, confirm the first `UserPrompt` row, clear, confirm no `ANTHROPIC_BASE_URL`) is operator-run after land and needs a live proxy; not run here. Item 1 (the B-agent Codex canary) is S6's.
- D-10/D-11 stand: `PhoneHomeLaunchPolicy.cs` is untouched; `C0167_runner_bound_claude_endpoint_projection_is_phone_home_env_refused` shows a runner-bound Claude or Codex projection with a key is 409 `phone_home_env_refused` naming only the variable, a URL-only Claude endpoint and a Grok endpoint pass through, and a non-runner agent is untouched.

## Judgment calls (deviations from the plan's letter; none from its design)

1. **Endpoint mode replaces the project endpoint; it does not overlay it.** The plan's table says only that Direct suppresses 3b. Overlaying would let the project's `{{key:NAME}}` follow a URL-only agent endpoint to a different host and would give Codex two provider tables (ten extra `-c` elements). `ProviderEndpointLayers.Select` therefore yields at most one projection per launch; a key the agent typed into its own box for a name its endpoint does not set still stays (5b removes nothing).
2. **Endpoint mode with no stored endpoint is 409 `provider_endpoint_required` at resolve**, not a fall-through: S2's API refuses the pair, so only a hand-edited row reaches it, and a silent route to the project proxy or to none would be the wrong kind of quiet.
3. **Both sides of the warm-pool predicate carry their projection** (`ProviderEndpointLayers.PoolView`: rank 3b, the stored env, rank 5b). Comparing only the agent side would make a project endpoint defeat warm reuse forever (the task's snapshot never carries the projected names); comparing neither would hand a Direct or own-endpoint process a task that expects the project proxy. The map is read once per `TryReuseWarmAgentAsync` from `_db`; the launch path reads it through `ApiKeyEnvResolver` beside `ProjectDefaultEnv` so both project layers share one identity.
4. **S1 file edits for the carried gaps:** `ProviderEndpoint.ToString()` is host-and-name only (the default record form printed the full URL); `:0` and out-of-range ports are `provider_endpoint_base_url_port` (Uri accepted 0 and reported 65536 as "relative"); `NoEndpoints` is exposed. S1's reviewed behaviour is otherwise unchanged and its 12 results still pass.
5. **The registry path without an agent in hand defaults the mode to Inherit** (CardService's named-definition spawn passes the project map; `ResolveDefaultAsync`'s synthetic agent is Inherit), so a card spawned onto a project with a Claude endpoint gets it, like it gets the project default env.
6. **The CP-4 floor amendment** is a separate committed plan file (`…-s3-amendment.md`), not an edit of the landed plan.
7. **Not extended (outside the plan's S3 file list, flagged for the caller):** `AgentTaskService.ValidateLlmProxyPreview` (the local-proxy marker warning) and `ComputeInheritedLlmEnvAsync` (the inherited-snapshot reconstruction) do not see a projected endpoint; harmless today because a child on the same project gets rank 3b itself, but the preview warning cannot reason about a projected URL. `MergeRegistryGrokProbeEnv` only resolves the Grok home for the credential-store probe, which the projection never sets, so it is unaffected.
8. **`ProviderEndpointSettings.Validate` collapse deferred** (reasons in "Basing on S2").

Secrets custody: no key value anywhere in code, tests, logs or this report. Tests hold key NAMES (`c0167-proxy-key`) and one sentinel VALUE in a fake store (`sentinel-0167-never-printed`), asserted to reach only the child env. `RealCliStubEnv.ForCodex` in the S1 cross-check seeds a throwaway `CODEX_HOME` under the temp directory; no real `auth.json` or Codex home was read. No test inserts raw transcripts (CARD-1464 shape avoided).

## Landability and source checks

- `git fetch origin master` → origin/master `aa7a691ba961f8d4e7eea2562f6dfacfcf1b235e`; `git merge-tree --write-tree origin/master 852644a79` → clean, tree `8860574f0de7a99d164e9281deef713dafe2ad0e`, 0 conflicts (S2 still not on master at that read).
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef d57f4bc7b… -HeadRef 852644a79…` → `EVIDENCE result commits=8 entries=1 violations=0` (the one entry is master's own `.antiphon/task-4a1a5eee.md` inside the range).
- Platform reads: `GET /api/runner-defaults` revision 2, `globalRunnerId` server2, no kind defaults; `GET /api/session-runners`: desktop (windows, eligible), server2 (linux, eligible), server2-temp (not eligible). `-Runner`/`-Platform` omitted.
- RC: master only (plan D-13). Restart: server (the resolvers changed), dormant until an operator writes a setting; the usual post-land activation check suffices.
- Budget: brief said 60 minutes; wall was far longer (two 15-18 minute checkpoint runs on a loaded server2 with slot waits up to 225s, the CP-4 floor rerun, and a usage-limit pause mid-run).
