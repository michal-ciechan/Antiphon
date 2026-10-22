# CARD-0167 S4 checkpoint amendment: the affected-classes row

Amends [the landed plan](2026-10-22-card-0167-provider-endpoint-plan.md) for the S4 Code task 16fc75cf. The plan itself is not edited (a branch edit of a plan master already carries conflicts at land); this file carries the S4 rows and is the `--plan` the checkpoint tool runs for S4.

## What changed and why

The plan's CP-5 row is kept byte-for-byte: the new method plus `Runner_catalogue_and_profile_crud_expose_the_public_contract`, floor 2.

S4 adds a required positional member to `AgentTuiRunnerTypeDto`, which every `GET /api/agent-tui/runner-types` response and every `AgentTuiRunnerCatalog.Get` caller now carries. CP-8 runs the three whole classes that read the catalogue's DTOs rather than only constructing the catalogue as a dependency: `AgentTuiApiTests` (Integration, 11 + 1 new, including the secret-canary sweep over the runner-types read), `AgentTuiProfileServiceTests` (Integration, 22, capability rows read through `Get`) and `ProviderContractCatalogTests` (Unit, 37, the catalogue's capability rows against the provider contract). Floor 71 is their real size (no `[Arguments]`, no skips). It reuses CP-5's build. The id is CP-8 because the plan's CP-6 and CP-7 belong to S5 and S6.

No V-n/R-n IDs or PC rows are added. The descriptor test is the plan's S4 witness; PCs stay with SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c0167d/` | runner-types | `/*/*/AgentTuiApiTests/(C0167_*)\|(Runner_catalogue_and_profile_crud*)` | D-12 descriptor | 2 passed, 0 failed, 0 skipped | 2 | 10 |
| CP-8 | S4 | CP-5 | runner-types-affected | `/*/*/(AgentTuiApiTests*)\|(AgentTuiProfileServiceTests*)\|(ProviderContractCatalogTests*)/*` | DTO shape change across the catalogue's readers | all listed, 0 failed, 0 skipped | 71 | 12 |
