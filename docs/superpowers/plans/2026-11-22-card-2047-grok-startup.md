# CARD-2047 Grok startup retention and readiness

Source baseline `611df2dbc9606154d9490cdf12d46c55879f0bff`. The dispatch brief supplies the acceptance criteria. The [investigation](../../investigations/2026-11-22-card-2047-desktop-grok-launch-death.md) establishes the launch gate and failure cleanup. No separate landed plan or test design was supplied.

- S1. Prune on capture writes. Use the UTC time embedded in a writer-owned capture name so aging behaves the same after a file is copied or its metadata changes. Expire captures at seven days. Retain the newest captures under the count limit of 200 by default, with a 24-hour grace for all captures under count pressure. The hard 50 MiB cap can evict any age, oldest first. The seven-day age limit governs normal retention and space pressure can shorten it.
- S2. Use a monotonic deadline. Working buys another 30 seconds at each window boundary up to 180 seconds from the original start at the shipped default. A configured initial wait above the ceiling retains its original timeout. Only a settled qualified composer opens the gate. Snapshot faults, cancellation, exit and sign-in still terminate through their existing paths.
- S3. At a Deadline with Unknown and previously visible MCP, continue observing the same process in a single fresh window. Restart the positive streak, preserve the first window's evidence, and issue no input during the second window. The default retry adds a full 60 seconds when the first window ends at 60 seconds. Working extensions and retries share the 180-second ceiling. A failed retry logs both window summaries and captures their bounded screen evidence in one file. Suppress frame bodies after sign-in.

## Verification

- V-1. Capture expiry leaves the recent members of a 12-file fixture. Count pressure removes old captures at the configured cap and preserves recent ones. Byte pressure bounds the total even with recent captures. Only owned files are eligible.
- V-2. Real adapter with fake monotonic time and rendered frames stays gated at 60 seconds on Working, reaches Ready on two positive frames, and reports Deadline with its ceiling on continuously Working frames.
- V-3. Same adapter and clock recover on two Ready frames near 70 seconds, fail immediately without MCP, and emit both window diagnostics and frames at the second Deadline. A second-window trust frame produces no write or process restart.
- V-4. Existing capture bounds, Grok frame parsing, ready, modal, ordering and launch-failure behavior, with only the named classes. The fixed per-task Final guards are one reused-build row.
- R-1. Red-first witnesses for S1-S3 run together from commits with base production code. The row is expected red. Final uses the same cases within the affected class selection.
- R-2. Native Windows confirmation owed under the caller's no-desktop-execution instruction. Run CP-2, CP-3 and CP-6 on a Windows runner from the same reviewed source, then observe the current Grok native launch on a qualified Windows layout with first input held until Ready. No changed OS conditional or native factory.
- R-3. Regenerate the derived impact index after source changes, resolve impacted rows on the full diff, run evidence-diff and land-graph Check, and require conflict-free merge-tree against fetched master.

Unbounded classes: none. Budget approximately 8 minutes per checkpoint build plus the short filtered test rows and slot admission. Each group uses one build per project. SessionRunner's native exit witnesses apply on Linux. Mutation controls remain pending for SourceLanding if commissioned under the caller's waiver.

| Control | Deliberate change | Exact method |
|---|---|---|
| PC-1 | Drop 24-hour count protection | C2047_Count_cap_prunes_oldest_but_preserves_the_first_day |
| PC-2 | Drop the hard byte cap | C2047_Byte_cap_evicts_oldest_even_within_count_grace |
| PC-3 | Drop Working extension | C2047_Working_at_deadline_stays_gated_until_positive_composer |
| PC-4 | Remove the Working ceiling | C2047_Continuous_working_stops_at_the_ceiling |
| PC-5 | Disable the Unknown/MCP retry | C2047_Unknown_retries_only_if_mcp_was_seen |
| PC-6 | Retry with MCP absent | C2047_Unknown_retries_only_if_mcp_was_seen |
| PC-7 | Allow input during retry | C2047_Retry_exhaustion_captures_both_windows_without_input |
| PC-8 | Omit first-window capture evidence | C2047_Retry_exhaustion_captures_both_windows_without_input |
| PC-9 | Drop age-based expiry | C2047_Expiry_keeps_recent_captures_and_ignores_unowned_files |
| PC-10 | Drop the count limit | C2047_Count_cap_prunes_oldest_but_preserves_the_first_day |
| PC-11 | Allow a second Unknown/MCP retry | C2047_Retry_exhaustion_captures_both_windows_without_input |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | red | `tests/Antiphon.Tests -> bin-c2047-red/` | c2047-red | `/*/*/(GrokStartupCaptureStoreTests*)\|(RunnerGrokAdapterReadyTests*)/C2047_*` | R-1 | Eight assertion failures at base production behavior and one passing no-MCP control | 9 | 9 |
| CP-2 | all | `tests/Antiphon.Tests -> bin-c2047-final/` | c2047-adapter | `/*/*/(GrokStartupCaptureStoreTests*)\|(GrokStartupReadinessTests*)\|(RunnerGrokAdapterSignInPromptTests*)\|(RunnerGrokAdapterTrustPromptTests*)\|(GrokLinuxBlockingPromptTests*)/*` | V-1, V-4 | All named classes execute, 0 failed | 7 | 9 |
| CP-3 | all | CP-2 | c2047-launch | `/*/*/(GrokStartupReadyOrderingTests*)\|(Card1619ExitConsumerTests*)/*` | V-4 | Both classes execute on isolated Postgres, 0 failed | 2 | 2 |
| CP-4 | all | CP-2 | c2047-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-4 | All nine classes execute, 0 failed | 78 | 2 |
| CP-5 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c2047-runner/` | c2047-runner | `/*/*/(Card1619RunnerExitTests*)\|(GrokRulesStoreFailureTests*)/*` | V-4 | Both classes execute on Linux, 0 failed | 2 | 9 |
| CP-6 | all | CP-2 | c2047-ready | `/*/*/RunnerGrokAdapterReadyTests/*` | V-2, V-3, V-4 | All named adapter methods execute, 0 failed | 6 | 2 |
