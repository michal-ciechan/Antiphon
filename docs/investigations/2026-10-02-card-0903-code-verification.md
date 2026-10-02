# CARD-0903 Code verification and requirement trace

**Date:** 2026-10-02. **Task:** `4f067d65`. **Branch:** `feat/card-task-4f067d65`.
This records the operator's Sol + Terra decision and the Final round on the Linux runner.
The existing 2026-10-01 investigation remains historical evidence. No live provider or
authenticated Codex catalog call was made.

## Functional trace

| Requirement | Asserted evidence | Result |
|---|---|---|
| Codex Frontier `gpt-6-astra`, High and Medium `gpt-6.1-sol`, Low `gpt-5.6-luna`, default arm High | `ModelAliasTests.Bare_sol_and_retired_slugs_keep_their_historical_aliases` at `tests/Antiphon.Tests/Application/ModelAliasTests.cs:169-177`; `CodexDelegateDispatchTests.every_tier_pins_a_full_slug_and_names_its_own_reasoning_effort` at `:89-109` | Yes, CP-1 and CP-2. |
| Frontier xhigh, High high, Medium medium, Low low, including launch argv | `CodexDelegateDispatchTests.every_tier_pins_a_full_slug_and_names_its_own_reasoning_effort` at `tests/Antiphon.Tests/Application/CodexDelegateDispatchTests.cs:89-109`; `LaunchModelArgumentAppenderTests.ForLaunch_maps_supported_ladders_and_omits_unsupported_kinds` at `:15-29` | Yes, CP-2 and Unit. The 0.160.0 bundled catalog supports both medium and high for 6.1 Sol (prior investigation section 2.4). |
| 6.1 Sol normalizes from slug/display spelling, while bare Sol and older Sol/Terra ids retain their prior meaning | `ModelAliasTests.Normalize_maps_known_family_text` at `tests/Antiphon.Tests/Application/ModelAliasTests.cs:34-45`; `Bare_sol_and_retired_slugs_keep_their_historical_aliases` at `:154-177` | Yes, CP-1. Bare Astra/Terra/Luna arms are unchanged in `ModelAlias.cs:174-193`. |
| Current hold vocabulary has one 6.1 Sol entry; old `gpt-6-sol` hold retains its exact target | `ModelAliasTests.CanonicalHoldAlias_accepts_known_aliases_and_star` at `:119-130`, `CanonicalHoldAlias_rejects_tui_names_and_unknown_text` at `:133-146`, `Bare_sol_and_retired_slugs_keep_their_historical_aliases` at `:174-177`; `ModelAvailabilityTests.Historical_codex_sol_hold_keeps_its_exact_model_meaning` at `tests/Antiphon.Tests/Application/ModelAvailabilityTests.cs:48-67` | Yes, CP-1/CP-2. |
| New Sol first in curated suggestions, older Sol and Terra still selectable | `AgentTuiProfileServiceTests` curated Codex list assertion at `tests/Antiphon.Tests/AgentTui/AgentTuiProfileServiceTests.cs:823`; `PinnedProfileLaunchSpecTests` exact model assertions at `tests/Antiphon.Tests/Application/PinnedProfileLaunchSpecTests.cs:80-113` | Yes, CP-2. |
| Client tier chips, compact labels, available list and fixtures name 6.1 Sol | `taskVisuals.test.ts:108-114,130`; `pipelineStageModel.test.ts:352-359`; `ModelAvailabilityPanel.test.tsx:12-21` | Yes, 3 files / 69 Vitest tests. The hold dropdown source is `ModelAvailabilityHoldForm.tsx:25-30`; its accepted alias is asserted by `ModelAliasTests.cs:119-130`. |
| Medium→High is same-model fresh context at deeper effort; High→Medium is same model at medium effort; Low→Medium and High→Frontier change models | `CodexDelegateDispatchTests` assertions at `tests/Antiphon.Tests/Application/CodexDelegateDispatchTests.cs:225-280`; `DelegationKindDisplayTests` handoff assertion at `:141` | Yes, CP-2. High→Medium is a tier comparison: `EscalateAsync` admits only upward requests. |
| High and Medium named-agent launch expectations | `NamedCodexAgentLaunchTests.cs:68,86,113` | Source assertions updated. **Windows execution pending**: that full class requires `cmd.exe`; it was accidentally included in initial CP-2 and all six failures were executable-resolution errors, before model assertions. The later Windows task owns its execution. |
| Old Claude/Grok ladders and Frontier/Low pins unchanged | `ModelAliasTests.cs:169-177`; `DelegationKindDisplayTests.cs:31-67`; `CodexDelegateDispatchTests.cs:89-109`; full Unit lane | Yes. No Claude/Grok ladder source arm was edited. |

## Runner and operational trace

| Requirement | Evidence | Result |
|---|---|---|
| Reject or reroute 6.1 tasks on old Codex CLI if per-runner version exists | `RunnerCapabilitiesDto` carries runner build metadata, not installed Codex CLI version; local and phone-home producers at `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:737-754` and `PhoneHomeRuntimeAdapter.cs:31-56`. `AgentTuiProfileService` probes a profile only. | Not applicable to this task's conditional guard. **CARD-0959** filed for version reporting and admission. |
| Before AppHost activation, all Codex-serving runners at least 0.159.1 | `docs/agent-kinds.md` section 3 and CARD-0959. Desktop and server2-temp are 0.160.0; draining standing server2 is 0.156.1 until redeploy-old. | Pending orchestrator rollout. Verify standing runner after redeploy-old, then restart AppHost and inspect `/api/version` SHA. |
| Append operator decision to CARD-0903 without replacing original text | `card.ps1 get -Json` supplied original description and concurrency token; `edit -DescriptionFile` appended `## Operator decision 2026-10-02`. Readback showed revision 2 and the section. | Yes. The script's post-write local card-file status errored on the unavailable `C:` drive; the server revision persisted. |
| Red-first assertion on base, committed slices and remote visibility | CP-1-red at `5be7a310`: 75 executed, 71 passed, four named 6.1 failures, build succeeded. CP-1-green at `93102f32`: 76/76. Commits were pushed after each slice; final remote tip checked in the task report. | Yes. |
| One isolated checkpoint build and exact filter per row; one normal Unit lane; separate test projects sequentially | CP-1 green: 76/76; CP-2 initial: 112/119 (six Windows `cmd.exe`, one malformed test fixture); CP-2 rerun at `f2dbab52`: 113/113; CP-3 Unit: 3,733 passed, 40 skipped; CP-4 Pty Unit: 310 passed, two Linux failures (`CommandLineLengthTests` calls `shell32.dll`; `GrokNativeSessionStoreTests` returned Missing where its assertion expected Unavailable). | Yes. Pty result is red with stated causes; no Pty source changed. Their baseline was not remeasured. |
| Affected client Vitest files once; no real provider launch; no Windows row claim | `scripts/test-client.ps1 taskVisuals.test.ts ModelAvailabilityPanel.test.tsx pipelineStageModel.test.ts`: three files, 69/69. Relevant launch tests use fakes; no live 6.1 canary or authenticated catalog call. | Yes, except the accidental initial CP-2 Windows-class selection above; it established only the platform mismatch. |
| Do not touch rollout scripts, bundles, EF migrations, checkpoint tooling, concurrent card footprints, or main checkout | `git diff --name-only d81ff99c..HEAD` contains only the listed server/client/test/docs paths for CARD-0903. | Yes. |

The orchestrator runs the live 6.1 canary separately. SourceLanding PCs remain pending
method-scoped Mutation; ordinary green tests do not discharge them.
