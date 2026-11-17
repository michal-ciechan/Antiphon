# CARD-1948 — Client attention counts follow the page

The server census is already on `kindCounts`. `AttentionSummaryDto.From` sums that census and leaves out `RecentFailure` and `LandLegacyUnverified`. The panel header, orchestrator tab, and home glance still count `items` on the loaded page, so a truncated parked page shows a smaller total than the fleet. `SessionMessageQueue` prints `{messages.length} messages` for a page that is not the whole queue.

This card does not change the queue or attention APIs, does not drop a row, and does not hold the CARD-1943 land. No positive controls. Mutation checks are waived by the operator (2026-11-12).

## Verification design

- V-1: The attention panel open badge and the Broken group badge use the parked census. `RecentFailure` and `LandLegacyUnverified` stay out of the open total.
- V-2: The orchestrator Needs attention tab uses that same open total.
- V-3: The home glance keeps a loaded Critical parked row in Blocked and counts the undisplayed parked census as Broken. `RecentFailure` and `LandLegacyUnverified` stay off the glance.
- V-4: A truncated session queue labels the message count as the loaded page. A complete page keeps `{n} messages`.
- R-1: The touched client files stay green.
- R-2: The guard union stays green, including C1729 at 0 violations and 162 listed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | red | n/a | census-red | `pwsh -NoProfile -File scripts/test-client.ps1 AttentionPanel.test OrchestratorPage.test MobileHomePage.test SessionMessageQueue.test -t CARD-1948` | V-1,V-2,V-3,V-4 | 4 intended failures, exit 1 | n/a | 6 | true |
| CP-2 | green | n/a | census-green | `pwsh -NoProfile -File scripts/test-client.ps1 AttentionPanel.test OrchestratorPage.test MobileHomePage.test attentionVisuals.test SessionMessageQueue.test` | V-1,V-2,V-3,V-4,R-1 | touched client files, 0 failed | n/a | 8 | true |
| CP-3 | guards | tests/Antiphon.Tests -> bin-c1948-guards/ | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-2 | all listed, 0 failed | 98 | 15 | true |

### Cost

Ordinary floor is 6 + 8 + 15 = 29 minutes. Client rows include the typecheck and lint gates inside `scripts/test-client.ps1`. CP-3 is one cold `Antiphon.Tests` build plus the guard filter. No repeat proof after green.
