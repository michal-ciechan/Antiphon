# GH-59 GitHub review links

A configured public Antiphon URL lets readers open the run from a GitHub review summary, any inline comment, and the two commit status paths. Each destination ends in the run ID. The run page opens on Findings. Reject missing, relative, non-HTTP(S), loopback and credential-bearing URLs, including bases with a query or fragment.

Task base: `b5b349d968b5c5ae28a66c73201f1ab06a540aa5`.
Code and landing owner: `57c44227-4884-42f1-8f50-afb726202c23`.

## Verification design

- V-1. A real GitHub review payload captured by the scripted host has exactly one run link under its summary heading and as the final paragraph of every inline comment. The existing review status uses the same URL.
- V-2. The absent and unsafe settings leave the original summary and inline body intact and omit the status target. The cases include synthetic credentials, a query token and a fragment.
- V-3. A manual re-review of a persisted run preserves the original link, suppresses the previously posted finding and gives the new inline comment and summary the same run URL.
- R-1. Run the affected `ReviewEndToEndApiTests` class for publication, status reporting, approval and merge flow. Inspect the existing /reviews route and the approval-withdrawal call site for the shared URL.
- R-2. Run the policy, test lane, mechanical, platform, layering, fixture cleanup and plan selection guards.
- R-3. Run the two live-gate inventory methods.

S1 commits the new cases before production changes. CP-1 is the expected assertion red against the original publishing behavior. S2 implements the change and updates the configuration guide. CP-2 through CP-4 share one final build. These are bounded filters and no full assembly, native OS or live GitHub run is needed. Verify Markdown placement on the captured outbound payload.

[Pending positive controls](../mutations/2026-11-27-gh-59-review-run-links-candidate.md)

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-gh59-red/` | red-publish-witness | `/*/*/ReviewEndToEndApiTests/Reviews_and_inline_comments_link_once_to_the_same_run_across_publications` | V-1 | Reviews_and_inline_comments_link_once_to_the_same_run_across_publications | 1 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-gh59-final/` | review-publish | `/*/*/ReviewEndToEndApiTests/*` | V-1, V-2, V-3, R-1 | ReviewEndToEndApiTests, Reviews_and_inline_comments_link_once_to_the_same_run_across_publications, Reviews_and_inline_comments_keep_the_original_bodies_without_a_safe_public_base | 14 | 8 |
| CP-3 | S2 | CP-2 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | R-2 | TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestRegistryMergeTests, SlowTestTripwireTests, NativeLinuxPolicyWitnessTests, MechanicalPcContractTests, TaskPlatformGuidanceTests, C1729LayeringGuardTests, TestLaneCategoryGuardTests, TestFixtureCleanupGuardTests, PlanCheckpointClassSelectionGuardTests | 78 | 7 |
| CP-4 | S2 | CP-2 | live-gate-inventory | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | R-3 | EveryLiveGateHasDisposition, ExplicitGatesAreNeverFullRequired | 2 | 3 |
