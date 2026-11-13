# CARD-1836 S5a verification amendment

Code task 08e79898. Base `586be574b88d1957dbf2a96c5dad771e3bcd8b37`.
The owner plan is [incremental activation](2026-11-12-card-1836-incremental-activation-plan.md), S5a / V-3 / D-3.
CP-3 and CP-90 retain its exact filters and floors. The committed diff selects the
`spawn-limit-rosters` and `test-lane-category` impacted rows; CP-90 covers the main
assembly and CP-91/92 cover the other two projects. CP-93 covers the complete
DerivedImpactIndexTests class selected by the regenerated index. No whole lane is selected. No ordinary R-n
is assigned to S5a; R-1 begins at S5b.

## Performance budget

The owner plan's provisional method/IO budgets apply. One 885 MiB streamed corpus
is generated only by LargeSummaryIsBounded, with no database/service fixtures.
CP-91/92/93 estimates are provisional, including cold build and discovery. Each
project builds once; CP-93 reuses CP-3's build. Total estimate 19 minutes plus
slot admission; no repeated green proof is requested. Native Windows path/handle
proof remains owed by the caller under CP-17, not credited by Linux.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-3 | S5a | `tests/Antiphon.Tests -> bin-c1836-s5a/` | linux-stream | `/*/*/RcHistoricalEvidenceTests/*` | V-3 | all 3 named methods, 0 failed/skipped | 3 | 8 |
| CP-90 | S5a | CP-3 | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | policy, categories, spawn census | all listed, 0 failed/skipped | 79 | 4 |
| CP-91 | S5a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s5a-pty-guards/` | linux-pty-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 1 |
| CP-92 | S5a | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s5a-runner-guards/` | linux-runner-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 2 |
| CP-93 | S5a | CP-3 | linux-derived-core | `/*/*/DerivedImpactIndexTests/(committed_index_matches_the_builder*)\|(regenerate_script_reports_changed_then_unchanged*)\|(merge_process_names_the_regenerate_step*)\|(solution_build_produces_the_regenerate_driver*)\|(declaration_diff_selects_the_new_tests_class*)\|(type_edge_on_a_fixture_diff_selects_the_lease_class*)\|(one_hop_path_selects_the_temp_family_class*)\|(dotted_script_name_selects_the_lease_class*)\|(a_path_with_26_classes_is_capped*)\|(solution_file_selects_no_derived_class*)\|(migration_only_diff_keeps_hand_classes_and_the_derive_set*)\|(schema_2_is_still_refused*)\|(merge_subset_on_a_derived_hit_emits_the_fixed_classes*)\|(tool_only_diff_selects_the_budget_method*)\|(index_bytes_stay_lf_when_the_newline_source_is_crlf*)\|(capped_real_paths_keep_the_specific_class*)\|(windows_only_classes_stay_out_of_linux_filters*)\|(reviewer_census_classes_are_windows_only*)\|(undeclared_class_with_an_os_token_stays_on_both_lanes*)\|(linux_only_methods_stay_off_the_windows_filter*)\|(has_windows_specifics_stays_on_both_lanes*)\|(class_level_linux_only_stays_off_the_windows_filter*)\|(override_list_forces_an_undeclared_class_onto_windows*)\|(override_file_names_exist_and_are_undeclared*)\|(parser_agrees_with_platform_taxonomy_derive*)` | 25 core methods of full impacted class | all 25 named methods, 0 failed/skipped | 25 | 5 |
| CP-94 | S5a | CP-3 | linux-derived-history | `/*/*/DerivedImpactIndexTests/s1_replay_matches_declaration_and_index_edges` | remaining historical replay method | exact method, 0 failed/skipped | 1 | 5 |
| CP-95 | S5a | CP-3 | linux-derived-merge | `/*/*/DerivedImpactIndexTests/merge_subset_matches_the_hand_selector` | remaining merge replay method | exact method, 0 failed/skipped | 1 | 5 |

## Measured batching correction

Initial run `20261113-022753-b7b3`, source
`7170a92387a3026bb42492ce05853e2892d5ce2b`, was stopped at 27m25s after
CP-3 passed 3/3 and CP-93 failed its existing 60s rebuild bound at 61.596s.
CP-90/91/92 were not executed and remain required. CP-3's build occupied 334s
plus 150s admission; its row waited 210s and executed 112s. The guard compile
alone took 8m04s; confinement/admission added further cost. These are loaded
observations, not measured p95. No timeout/assertion is widened.

Run the same closed table in three bounded tool invocations at the committed
source: `--rows CP-3,CP-93`, `--rows CP-90`, and `--rows CP-91,CP-92`, with
`--parallel 1 --expected-source-sha <HEAD>`. This retains every required row and
isolated build, avoids a repeat of the combined run, and targets each batch below
30 minutes. Keep all prior red/cancelled evidence. Direct harness runs are
explicit diagnostic/red-first checks for the streaming target and must be reported
separately; they do not discharge a checkpoint receipt.


## R1 method partition after measured budget expiry

Run 20261113-032009-7805 hit its unchanged 34-minute execution budget at 36m03s wall. CP-3 retained a 3-case TRX (2 pass, 1 timing-target failure); CP-93 had no terminal TRX and remains incomplete. Its journal diagnosed 26 completed cases and an unfinished historical replay; those journal records are not green receipts. CARD-1862 revision 2 records this additional R1 evidence under the existing selector-replay Debug scope. The completed journal durations included merge replay 217s, Windows filtering 79s, capped paths 69s and index rebuilding 47s. No inherited-failure attribution or assertion/timeout change is made.

CP-93/94/95 now partition all 27 public Test methods of DerivedImpactIndexTests exactly once: 25 core methods, historical replay, and merge replay. Every original case/assertion remains required. Run CP-3 alone after the streaming repair, then CP-93, CP-94 and CP-95 in separate tool invocations; CP-93 reuses the declared CP-3 build definition but receives its own fresh build when run alone. CP-94/95 likewise use their one declared build definition in separate invocations. Keep CP-90 and CP-91/92 as the other bounded batches. Estimates remain provisional; the measured cold-build plus confinement cost is about 19 minutes, so avoid repeating the prior combined selection. The initial 19-minute total estimate above is historical and superseded by these measured batches; each new batch targets below 30 minutes. Preserve every earlier outcome/cost.


## Shared producer after the measured CP-3 repair

The repaired CP-3 run 20261113-040451-e70f is GREEN at source 960ef1a36abb97aaf16549206c52ae46c8c1238a: 3/3, source clean, build provenance verified, slot=granted waited=0s. Its fresh TRX includes the exact three V-3 methods. The 885 MiB case retained 56.830s body wall, 2.242s fixture generation, 30.312s export, 3.144s parser, 524288-byte buffer high-water and 14884864-byte peak working-set increase. Preserve this proof; no further CP-3 repetition is required for this scheduling-only amendment. Production and test source bytes are unchanged by this amendment.

That one-row checkpoint cost 24m55s including 7m12s compilation, source/confinement work and 249.523s output cleanup. Repeating that producer for every method partition would dominate the remaining work. The S5a-specific CP-90/93/94/95 definitions therefore reuse CP-3's one isolated Antiphon.Tests build and the same S5a After group. This does not change the owner plan for other slices or any filter, floor, assertion or test timeout. CP-93's provisional estimate is 5 minutes, retaining the original derived 15-minute row deadline; replay rows retain their 15-minute floor and existing 600-second method deadlines.

Run only CP-90,CP-93,CP-94,CP-95 in one checkpoint invocation with --parallel 4, committed HEAD as expected SHA. The selected fixtures are independent: guards inspect immutable source/metadata or use GUID-owned temporary probe repositories; index fixture writes target GUID-owned temporary roots; both replay methods read this checkout and use distinct temporary paths. They use no database, mutable shared service, provider session or shared fixture root. Each test host retains its assembly-local ProcessSpawnLimit; it is explicitly not a cross-process lock. Each row takes its own host slot, the tool permits only one build at a time, and this group contains no Pty assembly or FakeClaude execution. All four row result/phase roots are distinct. Source stays frozen until the run exits.

Expected remaining main-group execution is approximately 19 minutes cold producer/confinement plus a 6-minute read-only test critical path and 4 minutes cleanup (provisional, not measured p95); broker admission is separate. CARD-1862 owns the already-triggered R1 replay/cold-build performance follow-up and retains prior over-budget evidence. Keep the tool's derived total allowance; do not widen per-case or per-row deadlines. If this group still exceeds 30 minutes, append its phases to that Debug scope before any repeat. Run CP-91 and CP-92 separately afterward with --parallel 1 so their independent project builds and the Pty assembly remain sequential. Every closed row and all 27 index methods remain required; no earlier missing-TRX journal is promoted into a pass.
