# CARD-0167 S1 — task aecb6024 (Code, Final round) report

Outcome: S1 implemented and green. `ProviderEndpoint` (record, `Validate`, `Parse`/`Serialize`, `ParseMap`/`SerializeMap`), `ProviderEndpointMode`, and the pure `ProviderEndpointProjection` (`For` with Claude/Grok/Codex arms, `Describe`, `CodexProviderId`) plus the 7-method / 12-result unit roster. CP-1 ran once through the checkpoint tool on server2 and is GREEN at the tested SHA with a validated clean receipt. No persistence, no resolver wiring, no behaviour change for any launch without a setting (S1 has no callers yet).

Plan: `docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan.md` (landed on master ac9ac4a9a; not edited).
Branch `feat/card-task-aecb6024`, worktree `/work/worktrees/task-aecb6024` (runner mirror, server2 Linux lane). Base `be1e8426eb7910725d6c1bb0c64842ea6260dd59`. Landing owner: this Code task aecb6024.

## Commits (all pushed)

| SHA | Subject |
|---|---|
| `55d0fc0a75a9fc84754b746e664dd331e9f15dc7` | test(CARD-0167): S1 provider endpoint projection roster against a skeleton (test-first, red witness SHA) |
| `1d3ad6f917ee6c4bc56983d0a64e642dc9f1b853` | feat(CARD-0167): S1 ProviderEndpoint record and the per-kind ProviderEndpointProjection (tested SHA, CP-1 green) |

Files: `server/Application/Services/ProviderEndpoint.cs` (new), `server/Application/Services/ProviderEndpointProjection.cs` (new), `server/Domain/Enums/ProviderEndpointMode.cs` (new), `tests/Antiphon.Tests/ApiKeys/ProviderEndpointProjectionTests.cs` (new, `[Category("Unit")]`). No other file touched; `RealCliStubEnv.cs`, `PhoneHomeLaunchPolicy.cs`, `DelegationSettings.cs` untouched as the plan requires.

## Red witness (unlisted build-slot run, plan D-14)

At `55d0fc0a7` (tests against a skeleton: `For`/`Describe` return empty env/args, `Validate` returns its input), CP-1's exact filter:

```
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0167-red-s1 -- dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0167red/ -- --treenode-filter "/*/*/(ProviderEndpointProjectionTests*)|(FakeLlmApiSelfTests*)/(C0167_*)|(ForClaude_sets*)|(ForGrok_requires*)|(ForCodex_emits*)"
BUILD SLOT granted lease=ff76f4b8-e40d-418e-ac97-ccd1f18173e9 waited=0s maxcpucount=6
Test run summary: Failed!  total: 15  failed: 12  skipped: 0
BUILD SLOT released lease=ff76f4b8-e40d-418e-ac97-ccd1f18173e9 held=191s
EXIT=2
```

executed=15 failed=12 passed=3 (the three `FakeLlmApiSelfTests` self-tests). Every one of the 12 new results failed on its guarded assertion: `C0167_Claude_projects_base_url_and_key_placeholder` (should be), `C0167_Grok_projects_both_base_urls_and_chat_var_is_present` (should contain key), `C0167_Codex_projects_openai_key_and_five_args_ending_v1` (should be), `C0167_Codex_requires_a_key_name` (should throw), `C0167_Unsupported_kind_refuses_by_code(OpenCode|Raw)` (should throw, 2), `C0167_Validate_refuses_userinfo_query_fragment_relative_and_placeholder(...)` (should throw, 5), `C0167_Projection_names_match_RealCliStubEnv` (should be). Reason for the unlisted run: the plan's D-14 red-first rule; the table lists green runs only.

## Checkpoint (closed list: CP-1 is the only row After S1)

Run `20261022-073644-1f35` (`start --plan <plan> --rows CP-1 --expected-source-sha 1d3ad6f9…`, then `wait`), tool built once under the slot gate (`dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c0167tool/`, slot granted waited=0s held=8s; the only other unlisted build, needed to run the tool from a worktree).

```
commit: 1d3ad6f917ee6c4bc56983d0a64e642dc9f1b853  branch: feat/card-task-aecb6024  worktree: /work/worktrees/task-aecb6024  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 1d3ad6f917ee6c4bc56983d0a64e642dc9f1b853 state=clean buildSource=verified
CHECKPOINT CP-1 commit=1d3ad6f917ee6c4bc56983d0a64e642dc9f1b853 build=ok filter=/*/*/(ProviderEndpointProjectionTests*)|(FakeLlmApiSelfTests*)/(C0167_*)|(ForClaude_sets*)|(ForGrok_requires*)|(ForCodex_emits*) executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-aecb6024/.antiphon/checkpoints/20261022-073644-1f35/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=1d3ad6f917ee6c4bc56983d0a64e642dc9f1b853 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=15s build=301.751703s startup=12.5165871s testsWall=0.0853395s teardown=0.5239574s hostWall=13.1258758s
unlisted: none (the tool ran no other build or test command)
wall: 5m50s  sequential-equivalent: 0m30s  builds: 5  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
outputs: deleted bin-c0167a/
verdict: GREEN exit=0
```

Validation: `Antiphon.Checkpoints validate --evidence <run>/report.json --expected-source-sha 1d3ad6f9… --rows CP-1` → `CHECKPOINT SOURCE VALID source=1d3ad6f917ee6c4bc56983d0a64e642dc9f1b853 rows=1` (exit 0); `scripts/validate-checkpoint-receipt.ps1 -Evidence … -ExpectedSourceSha … -Rows 'CP-1'` → same line, exit 0.
Fresh TRX inspected: `<Counters total="15" executed="15" passed="15" failed="0" …/>`; className counts 12 × `Antiphon.Tests.ApiKeys.ProviderEndpointProjectionTests`, 3 × `Antiphon.Tests.Agents.FakeLlmApiSelfTests`; all 15 `outcome="Passed"`, method names as the plan lists (the two `[Arguments]` methods expand to 2 and 5). Actual counts equal the plan's 12 + 3 = 15.
Repetitions of the CP-1 selection: 1 red (55d0fc0a7) + 1 green (1d3ad6f91); none after green. No whole-suite or Unit-lane run (brief: minimal testing, one checkpoint run).
Evidence root: `.antiphon/checkpoints/20261022-073644-1f35/` (gitignored, on the mirror). All `bin-c0167*` directories deleted (red run outputs, the tool build, and the run's own `bin-c0167a/`).

## Invariants, PCs, acceptance

- The plan defines no V-n/R-n IDs; its verification design is the CP table plus decisions. CP-1 covers D-1, D-3, D-5, D-6 as the table states. No manual acceptance belongs to S1 (the two operator acceptances are S3/S6 concerns).
- PCs: the plan has no PC table (zero-PC plan); nothing listed is pending. Candidate mutants for SourceLanding Mutation, each with its detecting method: drop `GROK_XAI_API_BASE_URL` or the chat var → `C0167_Grok_projects_both_base_urls_and_chat_var_is_present`; skip the `/v1` append, change `env_key`, or set `CodexProviderId = "stub"` → `C0167_Codex_projects_openai_key_and_five_args_ending_v1` and `C0167_Projection_names_match_RealCliStubEnv`; remove the userinfo/query/fragment/relative/marker checks → the matching `C0167_Validate_refuses_…` case; drop the Codex key requirement on either side → `C0167_Codex_requires_a_key_name`; let `For` project for Raw → `C0167_Unsupported_kind_refuses_by_code(Raw)`.
- Not witnessed in S1 (plan roster fixed at 12): `Parse`/`Serialize`/`ParseMap`/`SerializeMap`. S2's `C0167_project_endpoints_round_trip_per_kind_and_clear_with_empty_map` is the natural witness; flagged for the S2 brief and for Mutation.

## Judgment calls (deviations from the plan's letter, none from its design)

1. `ProviderEndpointMode` lives in `server/Domain/Enums/ProviderEndpointMode.cs`, not inside `ProviderEndpoint.cs`: S2 puts the property on the `Agent` entity and Domain must stay free of Application types.
2. 422 codes are distinct per rule (`provider_endpoint_base_url_{required,too_long,characters,relative,scheme,userinfo,query,fragment}`, `provider_endpoint_placeholder`, `provider_endpoint_key_required`, `provider_endpoint_unsupported_kind`); errors are keyed `<field>.baseUrl` / `<field>.apiKeyName`; the URL is never quoted in a message.
3. One extra refusal: quotes, backslashes, whitespace and control characters in the URL (`provider_endpoint_base_url_characters`), because the Codex arm quotes the URL into a `-c` TOML string.
4. `For` re-runs `Validate` after its own 409 guards, so a row written around the API still cannot project a bad URL into argv. `Describe` returns an empty projection for unsupported kinds (no throw) so S4 can call it per kind; helpers `IsSupportedKind`, `RequiresKeyName`, `SupportedKinds`, `TryParseKind` exist for S2/S4.
5. Env dictionaries use `StringComparer.Ordinal` (the resolver's convention), not the stub's OrdinalIgnoreCase.

Secrets custody: no key value anywhere in code, tests, logs or this report. Tests hold key NAMES (`c0167-proxy-key`) and the stub's synthetic `stub-*` tokens only as `RealCliStubEnv` arguments, asserting they never reach the projection. `RealCliStubEnv.ForCodex` seeds a throwaway `CODEX_HOME` under the temp directory, which the test deletes (same pattern as the existing self-test); no real `auth.json` or Codex home was read. The frozen `tests/linux-test-roster.json` (September snapshot, no October classes in it) was deliberately not edited.

## Landability and source checks

- `git fetch origin master` → origin/master `bb43ec3529b9d8ad2a799f81080b9e34d89fa9c9`; `git merge-tree --write-tree origin/master HEAD` → clean, tree `e74a04a4904f31ccab21f6d5105124f87a634798`.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef be1e8426e… -HeadRef HEAD` → `EVIDENCE result commits=2 entries=0 violations=0`.
- Platform reads: `GET /api/runner-defaults` revision 2, `globalRunnerId` server2, no kind defaults; `GET /api/session-runners`: desktop (windows), server2 (linux), server2-temp (stale). `-Runner`/`-Platform` omitted.

RC question: master only (plan D-13). S1 is additive, has no callers and is dormant until S3 wires it; no rc pick, no restart (server/runner/none → none).
Budget: 45 min authoring + red + CP-1; wall about 40 min.
