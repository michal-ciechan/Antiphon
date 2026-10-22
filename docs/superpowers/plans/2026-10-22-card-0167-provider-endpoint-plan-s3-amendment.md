# CARD-0167 S3 checkpoint amendment: the CP-4 floor

Amends [the landed plan](2026-10-22-card-0167-provider-endpoint-plan.md) for the S3 Code task d86141ac. The plan itself is not edited (a branch edit of a plan master already carries conflicts at land); this file carries the corrected rows and is the `--plan` the checkpoint tool runs for S3.

## What changed and why

The plan's CP-4 row says `LaunchEnvLayersIntegrationTests` has "41 + 1 new, floor 42". The class has 24 `[Test]` methods on master (none with `[Arguments]`), so with S3's one new method it produces 25 results, and the floor 42 cannot be met by any green run. The first S3 run (`20261022-091757-320d`, source `4135dcb83269fb2cd08ed2fe82803c8f4c30a875`) recorded CP-4 as `executed=25 passed=25 failed=0 skipped=0` and the tool scored it `roster-miss` (exit 3) on the floor alone. The row below sets the floor to the class's real size. Nothing else in the row changes: same build, same filter, same coverage.

CP-1 is rerun in the same run because S3 carried the S1 Final Review's gaps (character and scheme refusals, port codes, a `For()` over a stored bad URL, the lookalike-quote TOML witness, the record's prose form) into `ProviderEndpointProjectionTests`; its floor stays 15 and the actual count rises to 36. CP-3 is unchanged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0167a/` | projection | `/*/*/(ProviderEndpointProjectionTests*)\|(FakeLlmApiSelfTests*)/(C0167_*)\|(ForClaude_sets*)\|(ForGrok_requires*)\|(ForCodex_emits*)` | D-1, D-3, D-5, D-6, carried S1 review gaps | 36 passed, 0 failed, 0 skipped | 15 | 12 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c0167c/` | resolver-unit | `/*/*/(AgentTuiLaunchResolverTests*)\|(AgentLaunchEnvTests*)\|(PhoneHomeTaskRoutingTests*)/*` | D-4, D-5, D-11, redaction witness, byte-identical witness, pool witness | all listed, 0 failed, 0 skipped | 55 | 12 |
| CP-4 | S3 | CP-3 | resolver-layers | `/*/*/LaunchEnvLayersIntegrationTests/*` | D-4 on the legacy registry path, rank order across both resolvers | all listed, 0 failed, 0 skipped | 25 | 10 |
