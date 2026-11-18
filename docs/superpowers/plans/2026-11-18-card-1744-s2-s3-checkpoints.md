# CARD-1744 S2+S3 checkpoints

Code task dac0151d. Closed rows CP-3 through CP-6 copied from
docs/superpowers/plans/2026-11-18-card-1744-blockers-g1-g3-plan.md. One build and one filter
per row. Red rows run once at their test commit. Export ANTIPHON_LINUX_COMPILER_POLICY=0
before every dotnet command. Lane is the runner-defaults host. Omit -Runner and -Platform.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-3 | S2-red | tests/Antiphon.Tests -> bin-c1744g2pr/ | g2p-red | `/*/*/(C1744G2PlacementTests*)/*` | W-4, W-5 | 1 failed (W-5), 1 passed (W-4) | 2 | 12 |
| CP-4 | S2 | tests/Antiphon.Tests -> bin-c1744g2p/ | g2p-green | `/*/*/(C1744G2PlacementTests*)\|(TaskPlatformPlacementTests*)\|(RunnerDefaultsWireTests*)\|(OutboundConversionPublicCreateTests*)\|(RunnerDefaultSettingsTests*)\|(RunnerDefaultPlacementTests*)\|(RunnerDefaultMigrationTests*)/*` | W-4, W-5, existing placement, RunnerDefaultTests.cs | 0 failed | 43 | 12 |
| CP-5 | S3-red | tests/Antiphon.Tests -> bin-c1744g2lr/ | g2l-red | `/*/*/(C1744G2HostLoadKeyTests*)/*` | W-6, W-7, W-8 | 2 failed (W-6, W-7), 1 passed | 3 | 12 |
| CP-6 | S3 | tests/Antiphon.Tests -> bin-c1744g2l/ | g2l-green | `/*/*/(C1744G2HostLoadKeyTests*)\|(C1514HostLoad*)\|(AgentTaskPipelineStatusTests*)/*` | W-6, W-7, W-8, existing host-load and pipeline | 0 failed | 121 | 14 |
