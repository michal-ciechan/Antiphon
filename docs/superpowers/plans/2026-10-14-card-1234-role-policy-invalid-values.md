# CARD-1234 role policy rejects kinds and levels delegation cannot use

Code task 47b93e01. Base: `42974ae069440f0aee9d7b446b1b11f0f1dbf105` (origin/master at start).

## Slices

- **S1, tests only (red on master).** `RolePolicyConfigurationValidationTests` (Unit),
  `RolePolicyInvalidValuesCreateTests` (Integration, HTTP create path), one DelegateModal case and
  four ProjectSetupModal cases. `RolePolicyEndpointTests`' unsupported-values factory removes the
  `DelegationSettings` validator, so the CARD-0097 reader contract keeps its own host.
- **S2, server.** `DelegationSettingsValidator` rejects `Delegation:RolePolicy:<Role>:Kind` outside
  `AgentTaskService.DelegatableKinds` (Raw, OpenCode, undefined) and an undefined
  `Level`/`EscalateTo`/`Delegation:DefaultLevel`, one literal message each.
  `AgentTaskService.ResolveLevel` refuses an undefined policy or default level with 422
  `role_policy_level_invalid` (field `RolePolicy`) before the orchestrator floor;
  `ResolveAgentKind` gives a policy-sourced unsupported kind the code `role_policy_kind_unsupported`
  (field and message unchanged). `GET /api/role-policy` wire shapes are unchanged.
- **S3, client.** `DelegateModal` reads `effective.modelLevel` for both sources, so a withheld
  target (unsupported kind) falls back to the constant. `ProjectSetupModal` names the default tier
  only when it is a model level, else `default tier unknown.`

## Verification design

- V-1 `RolePolicyConfigurationValidationTests.An_unlaunchable_kind_or_undefined_level_fails_validation_naming_role_and_value`
  — 8 rows (Code Kind Raw/OpenCode, Docs Kind 999, Review Level 999, Mutation Level -1, Test
  EscalateTo 999, Investigate EscalateTo 4, DefaultLevel 999), each exactly one literal failure.
- V-2 `...A_launchable_kind_and_defined_levels_validate` — 12 rows (shipped, null/ClaudeCode/Grok/
  Codex kinds, four levels, EscalateTo null/Frontier, DefaultLevel Low) succeed. Control.
- V-3 `...Binding_unlaunchable_or_undefined_configuration_fails_the_options_read` — in-memory
  configuration bound through `AddOptions().Bind()`; reading `Value` throws with four literals.
- V-4 `...Binding_named_valid_values_and_absent_fields_validates` — control.
- V-5 `RolePolicyInvalidValuesCreateTests.post_agent_tasks_refuses_an_unusable_policy_value_with_a_named_422_and_writes_nothing`
  — 5 rows (policy level 999, DefaultLevel 999 with no Docs entry, kind Raw/OpenCode/999): 422,
  problem+json `code`, error prefix literal, zero tasks and sessions; a Commit create then succeeds
  at Medium.
- V-6 `...an_explicit_level_does_not_consult_an_undefined_policy_level` — control, 201 at Low.
- V-7 `...an_orchestrator_refuses_an_undefined_policy_level_instead_of_clamping_it_to_the_floor`.
- V-8 `DelegateModal.test.tsx` `C1234 unsupported-policy-kind-falls-back-to-constant` — Mutation row
  `level High`, `kind Raw`, effective withheld: fallback source and `tier-Low`, no `tier-High`.
- V-9 `ProjectSetupModal.test.tsx` `C1234 delegation step names the default tier only when it is a
  model level` — 4 rows (Medium named; undefined, null, 999 show `default tier unknown.`).
- R-1 the affected classes stay green: `RolePolicyEndpointTests`, `RolePolicyReadServiceTests`,
  `AgentTaskAgentKindTests`, `RolePolicyToolBundleDispatchTests`, the validator-calling classes, and
  the existing DelegateModal/ProjectSetupModal cases (CARD-0097 V16-V18, V27, V30).

Intended red at S1: V-1 8 rows, V-3, V-5 5 rows (master creates at 999 or refuses with
`validation_failed`), V-7 (master clamps to High), V-8 (master shows the row's High), V-9 3 rows
(master prints `default 999 tier.` or `default High tier.`). Controls V-2, V-4, V-6 and the Medium
row of V-9 stay green.

PCs (pending, SourceLanding Mutation): PC-1 drop the Kind check (V-1 kind rows, V-3); PC-2 drop
the Level check (V-1 level rows); PC-3 drop the EscalateTo check; PC-4 drop the DefaultLevel check;
PC-5 remove the ResolveLevel guard (V-5 level rows, V-7); PC-6 move the guard after the orchestrator
floor (V-7); PC-7 drop the `role_policy_kind_unsupported` code (V-5 kind rows); PC-8 read `row.level`
for source rolePolicy in DelegateModal (V-8); PC-9 restore `?? 'High'` in ProjectSetupModal (V-9).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1234-red/` | server-red | `/*/*/(RolePolicyConfigurationValidationTests*)\|(RolePolicyInvalidValuesCreateTests*)/*` | V-1..V-7 red-first | 29 executed; intended red 15 failed (V-1 8, V-3 1, V-5 5, V-7 1), 14 passed (V-2 12, V-4, V-6), 0 skipped | 29 | 13 | false |
| CP-2 | S1 | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/delegations/DelegateModal.test.tsx src/features/settings/ProjectSetupModal.test.tsx` | V-8, V-9 red-first | 2 files, 35 results; intended red 4 failed (V-8 1, V-9 3) | n/a | 5 | false |
| CP-3 | S2-S3 | `tests/Antiphon.Tests -> bin-c1234-fix/` | server-new | `/*/*/(RolePolicyConfigurationValidationTests*)\|(RolePolicyInvalidValuesCreateTests*)/*` | V-1..V-7 | 29 executed, 0 failed/skipped | 29 | 13 | false |
| CP-4 | S2-S3 | `CP-3` | role-policy-neighbours | `/*/*/(RolePolicyEndpointTests*)\|(RolePolicyReadServiceTests*)\|(AgentTaskAgentKindTests*)\|(RolePolicyToolBundleDispatchTests*)/*` | R-1 | all cases of the four classes, 0 failed/skipped | 40 | 6 | false |
| CP-5 | S2-S3 | `CP-3` | validator-consumers | `/*/*/(DelegationLeaseSettingsTests*)\|(WorkerWorkspaceDefaultStorageTests*)\|(CheckCompactionContinuationSettingsTests*)\|(SpecialistFailurePolicyTests*)\|(AgentTaskServiceIntegrationTests*)/*` | R-1 | all cases, 0 failed/skipped | 100 | 10 | false |
| CP-6 | S2-S3 | `CP-3` | validator-integration | `/*/*/(AgentTaskConcurrencyLimitTests*)\|(DispatchHoldVisibilityTests*)\|(AgentTaskLandMonitoringTests*)\|(AgentTaskLandTargetRaceTests*)\|(RunnerSettlementSyncTests*)/*` | R-1 | all cases, 0 failed/skipped | 85 | 12 | false |
| CP-7 | S2-S3 | n/a | client-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/delegations/DelegateModal.test.tsx src/features/settings/ProjectSetupModal.test.tsx` | V-8, V-9, R-1 | 2 files, 35 results, 0 failed/skipped, `CLIENT TESTS EXIT CODE: 0` | n/a | 5 | false |

Lane: default placement; no `-Runner`, no `-Platform`. Run through the checkpoint tool from a
manifest imported from this table with `timeouts.rowMinutes: 40` (the build deadline):
`--rows CP-1,CP-2` at the S1 commit, `--rows CP-3,CP-4,CP-5,CP-6,CP-7` at the S3 commit. Not run:
the whole Unit lane (excluded by the brief), the E2E callers of `ResolveLevel`
(`DelegationSequencingE2ETests`, `DelegationPipelineE2ETests`; shipped settings, which V-2
`shipped` proves valid).

### Cost

Ordinary floor = 13 + 5 + 13 + 6 + 10 + 12 + 5 = 64 minutes (two isolated `Antiphon.Tests` builds).
