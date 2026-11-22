# Why the C467 land fixture reported an unknown SHA

## Introducing commit and card

`b2911c28665fd198f3fa67f74f03c4dfb33cdb5c` (CARD-1514) changed ordinary builds to use `SourceRevisionId=unknown`. The C467 fixture hosts the ordinary server assembly in its E2E test process. `/api/version` still advertised `land-v2`, but `delegate.ps1 -Land` also requires a full hexadecimal SHA. Both Batch1 cases in Windows Full run `c9816b02` at rc `ac8339c75` stopped at this script guard before the land POST. The portable HTTP witness reproduced `version=unknown` with `land-v2` present.

## Approving reviews

- `e4065157` (Final/Full) completed Interim review `a1de8d74` at `48c72753763b490dc962f45b4a9be6d8b8cae65b`. The Final ran `PtyHostDeploymentGuardTests` and evidence-diff. The Interim rows included `SourceRevisionStampContractTests`, `HealthEndpointTests`, the three Docker stack revision tests, `LinuxServerRestartGateTests`, AppHost scripts, the two-HEAD build probe, and guard and index checks. [Deploy-only SHA stamp plan with row filters](../superpowers/plans/2026-11-20-sha-stamp-checkpoints.md).
- `4380f903` (Final/Full) reviewed the CARD-2027 through CARD-2031 follow-ups at `f4d45a524d86258f3d092766acc05aa305135a72`. It ran CP-5 (guards and index), CP-7 (runner guards), CP-8 (pty limiter), and CP-10 (`LandGeneratedArtifactTests` and `ReleaseImpactEnvelopeTests`). It validated the existing CP-9 receipt for impacted script and documentation tests. Those rows ran on Linux. [SHA stamp advisories plan](../superpowers/plans/2026-11-21-sha-stamp-advisories-checkpoints.md).

## Outside the selection

Windows rc `e2e-all-split-1.trx` ran `C467_V29_RealOutcomeProducerMatrix_Batch1` in `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs` (OptIn). Neither review selected an E2E land row. The native matrix requires Windows ConPTY and native `fakegrok.exe`. The selected build and version checks exercised deploy stamp forwarding and ordinary `unknown` metadata without exercising the E2E fixture's real version endpoint as an input to the land CLI.

## Cheapest pre-land check

`Directory.Build.props` → `/*/*/LandDeliveryFixtureProbeTests/*` in `tests/Antiphon.E2E`. The exact regression witness is `/*/*/LandDeliveryFixtureProbeTests/Owned_land_host_reports_checkout_sha_and_real_capabilities`. It uses the real version endpoint and shared Land fixture configuration with an in-memory HTTP host. It checks the checkout SHA and advertised capability without starting the native runner, a database, or a browser.

## Exemption

None.
