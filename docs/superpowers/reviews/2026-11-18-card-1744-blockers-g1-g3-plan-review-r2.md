# CARD-1744 blockers G-1..G-3 plan review, round 2 (Full, diff only)

Date: 2026-11-18. Review task `3035b2b5-1955-4225-92c4-cae4e39debac` (Grok). Plan amendment
owner `2f24f3cc-d3ed-40bd-93a9-bc3a55697606` (Docs, Claude). Read-only. No production code,
test, or plan file was changed.

- Subject: `docs/superpowers/plans/2026-11-18-card-1744-blockers-g1-g3-plan.md` on
  `origin/feat/card-task-2f24f3cc`, tip **aa83d83209de72b00d61850018a5f9030f91d041**.
- Range: `891ef27d2fc4999e4f519c1f2d5a0527e6f68954..aa83d83209de72b00d61850018a5f9030f91d041`
  (one commit). The only change is that plan file.
- Round 1: `docs/superpowers/reviews/2026-11-18-card-1744-blockers-g1-g3-plan-review.md` at
  `caa7271127f2deae8cf417db7ea1b0e0a46bca03`. This round checks B-1..B-4 only.
- `reviewedSourceClean` stays false. The import below is a manifest. This plan review has
  no checkpoint-tool receipt.
- Mutation stays waived (operator 2026-11-12). Positive controls stay pending.
- Advisories A-1..A-7 stay on the round 1 review. They do not hold this land.

**Verdict: CLEAN.** B-1 through B-4 are fixed in the amended plan. No new blocker in the diff.

## B-1. `AllowDelegatedTasks` true, no `StandingAgentId`, 15 lines

Holds. `PhoneHomeRunnerEntry.AllowDelegatedTasks` defaults false and `StandingAgentId` defaults
`Guid.Empty` (`PhoneHomeRunnerSettings.cs:188-189`). `ValidateMapped` refuses that pair
(`:386-387`). The existing `server2` block (`docker-compose.server2-server.yml:56-69`) is 14
`PhoneHomeRunner__Runners__server2__*` lines. D-5 sets those same 14 keys, plus
`RawExeAllowList__0`, and leaves `StandingAgentId` unset: 15
`PhoneHomeRunner__Runners__desktop-win__*` lines. `AllowDelegatedTasks` is `"true"`. W-1 requires
that flag and the absence of the `StandingAgentId` key. W-3 requires the bound entry
`Enabled == true`, `AllowDelegatedTasks == true`, and `StandingAgentId == Guid.Empty`. S1's
compose edit says the same 15 lines.

## B-2. Two-key check before the `-WhatIf` return, one key at a time

Holds against the script at this tip (the script is unchanged from master
`997f9b8d9ffc4ba870d2fd56caf7c03781591624`). The env-file branch is `:307-335` and ends with the
dev-password and hangfire-storage checks. `if ($planOnly) { return }` is `:341-344`. The no-file
`env-missing` return at `:336-338` is `elseif (-not $planOnly)`. The execute key list at `:347`
is still the six existing keys. S1 puts the two new keys in the env-file branch, after those
checks and before the `planOnly` return, and adds the same two keys to the `:347` list. W-2
passes `-WhatIf -EnvFile` with an otherwise valid file and omits one new key at a time, expects
`SERVER2-SERVER fail exit=2 reason=env-missing` and no docker, expects both keys to `plan` exit
0, and leaves `WhatIf_prints_the_plan_and_calls_no_docker` (no `-EnvFile`) at exit 0. The window
step names `-WhatIf -EnvFile <that file>`.

## B-3. `DirectRunnerKey` allows `server2-direct`

Holds. D-3 keeps `^[a-z0-9][a-z0-9-]{0,63}$`, default `desktop`, refuses `local`, and refuses a
configured phone-home runner id, ordinal-ignore-case, from `PhoneHomeRunnerCatalog.Configured`.
That method (`PhoneHomeRunnerSettings.cs:234-249`) returns every map key, including disabled
entries, and in legacy mode the one `AllowedRunnerId`. `server2-direct` matches the regex, is
not `local`, and is not `server2` or `desktop-win`. W-6 seeds that key. W-8 refuses `local` and
a phone-home id, and keeps the default fold of `local` and `desktop` to `desktop`.

## B-4. Real class names and recounted floors

Holds. Counted at `997f9b8d9ffc4ba870d2fd56caf7c03781591624`. The branch versus that master
changes only the plan file, so these test files are that tree. A `[Test]` with k `[Arguments]`
is k cases; a `[Test]` with none is one case. No `[Skip]`, `MethodDataSource`, or `Matrix` on
these classes.

| Row | Floor in the table | Count at `997f9b8d9` |
|---|---|---|
| CP-2 | 18 | `Server2ProductionServerContractTests` 15 `[Test]`, no `[Arguments]`, plus 3 new |
| CP-4 | 43 | `TaskPlatformPlacementTests` 15; `RunnerDefaultsWireTests` 5; `OutboundConversionPublicCreateTests` 1; `RunnerDefaultSettingsTests` 6; `RunnerDefaultPlacementTests` 9; `RunnerDefaultMigrationTests` 5; plus 2 new. There is no class `RunnerDefaultTests`. `RunnerDefaultGuidanceTests` stays out of the filter |
| CP-6 | 121 | `AgentTaskPipelineStatusTests` in `AgentTaskPipelineStatusTests.cs` 26 `[Test]` / 35 cases (argument groups 6, 3, 3); `AgentTaskPipelineStatusC557Tests.cs` 19/19; `AgentTaskPipelineStatusC1076Tests.cs` 4/9; `HostBudgetPipelineTests.cs` 4/4; `AgentTaskPipelineStatusC100Tests.cs` 1/1; `AgentTaskPipelineStatusC1329Tests.cs` 1/1; `C1514HostLoadHistoryTests` 12/22; `C1514HostLoadMigrationTests` 4/9; `C1514HostLoadRecommendationTests` 5/5; `C1514HostLoadApiTests` 4/4; `C1514HostLoadScriptTests` 4/4; `C1514HostLoadAggregateTests` 3/3; `C1514HostLoadScenarioTests` 2/2; plus 3 new. `AgentTaskPipelineEndpointTests` is 4 `[Test]` in the same file and does not match the filter |
| CP-9 | 19 | `DockerStackDocumentationTests` 19 `[Test]`, no `[Arguments]` |

CP-4 names those seven class prefixes. CP-6 names `C1744G2HostLoadKeyTests*`, `C1514HostLoad*`,
and `AgentTaskPipelineStatusTests*`. `(HostBudgetPipelineTests*)` is gone; that file is
`partial class AgentTaskPipelineStatusTests`. The CP-6 build is `tests/Antiphon.Tests`, so the
`C1514HostLoad*` classes in `Antiphon.SessionRunner.Tests` are outside the row.
`C1514HostLoadFixture`, `World`, `CrashCases`, `SqlProbe`, `Scenario`, and `ProgressExecutor`
have no `[Test]`.

Import of the subject plan with the already-built checkpoint tool
(`dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll import`): exit 0,
9 rows, no `WARNING` line. Output `.antiphon/c1744-g1-g3-import-r2.yaml` (gitignored).

| CP | minExecuted | timeoutMinutes |
|---|---|---|
| CP-1 | 3 | 45 |
| CP-2 | 18 | 36 |
| CP-3 | 2 | 36 |
| CP-4 | 43 | 36 |
| CP-5 | 3 | 36 |
| CP-6 | 121 | 42 |
| CP-7 | command | 15 |
| CP-8 | command | 15 |
| CP-9 | 19 | 36 |

`3 * EstimatedMinutes` stays within the 45-minute advisory (CP-1 derives 45, CP-6 derives 42).
Command rows CP-7 and CP-8 have no Min.

## Mechanics

- `scripts/check-evidence-diff.ps1` base `997f9b8d9ffc4ba870d2fd56caf7c03781591624` head
  `aa83d83209de72b00d61850018a5f9030f91d041`: commits=3 entries=0 violations=0, exit 0.
- `git fetch origin master`, then `git merge-tree --write-tree origin/master aa83d83209de72b00d61850018a5f9030f91d041`:
  tree `e4173beac07fc17447c870f5296c2aa5beecaf99`, exit 0. `origin/master` is still
  `997f9b8d9ffc4ba870d2fd56caf7c03781591624`.
- `scripts/impacted-classes.ps1 -MergeSubset -Range 997f9b8d9..aa83d832 -Json`: `ok=true`,
  `migrations=false`, kinds empty, files 0. Downgrade selector not selected, count 0. The
  printed class list is the fixed merge subset.
- No migration. The diff adds no public DTO or route.

## Tests

None run. The subject changes no code and no test, so the touched-class rerun and the guard
batch do not apply. The new CP classes do not exist yet. All positive controls stay pending.
Load at the import was 21.12; the import used the existing tool binary and started no build.
