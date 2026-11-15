# CARD-1901 publication fixture leak repair

The producer leak is fixed and its cleanup witness is red then green. This is a
test-only repair handoff to Merge, with explicitly incomplete broader verification:
eight publication corpus methods were not run within the 60-minute task budget.
The earlier round-3 Remote/native-Windows verification debt also remains.

Base and required ancestor: `c4eeff2e35e310698e2fd85e83271f49f5881895`.
Regression commit: `2c4a664fb3750ef000e7e1b7c13a154aae13773f`.
Repaired C# source and every green test row:
`fef9562dc53e4fc477ecb24b4b1d136fad683452`.
The subsequent report commit changes only this investigation. The assigned branch
is `feat/card-task-c6da537d`; no rebase, merge, reset or protected-branch push occurred.

## Defect, repair and ownership

`VolumeRetirementPublicationFixture.Create(preview: false)` created synthetic Git
repositories under `.antiphon/evidence/card-1901/fixtures/<guid>` in the checkout.
Neither it nor its callers deleted them. These real repositories, reflogs and
fixture origins then contaminated the runner-volume publication audit.

The fixture now implements `IDisposable`. Its platform helper chooses `/var/tmp`
on Linux and `Path.GetTempPath()` on Windows. Each created root has a unique name
and ownership marker. Disposal checks its recorded immediate parent, exact name,
marker contents and absence of a root reparse point before deleting that root.
It clears read-only Git files for Windows without walking linked directories.
Setup failures also dispose; every owning publication/host-proof call uses a
`using` scope, whose finally runs on normal completion, assertion failure or
cancellation. `Attach` continues to borrow the caller-owned host and cannot delete
its root. Existing assertions and timeouts remain intact; production is unchanged.

The new single witness is
`VolumeRetirementHostProofTests.Publication_fixture_disposal_leaves_no_checkout_or_temp_residue`.
It creates real repositories and checks successful, assertion-failure and
cancellation exits. After each disposal it checks that the exact root is gone,
its parent was the chosen platform base, and nothing remains under checkout
card-1901 evidence. No new native-gate calls were added, so no roster row was needed;
the source-closure and full admission tests passed.

`df -T /var/tmp /tmp /work/worktrees/task-c6da537d` confirmed `/var/tmp` is on the
container overlay mounted at `/`, whereas `/tmp` and `/work` are mounted ext4
volumes. Thus the Linux root is outside both audited runner volumes. No live host,
Docker volume, deployment, restart or Windows qualification run was touched.

## Red and green evidence

On the unmodified base, the exact cheapest host-proof method
`Fresh_graph_renewal_fetches_again_for_unchanged_advertisement` passed 1/1 in
10.022 seconds but left **50 files** beneath
`.antiphon/evidence/card-1901/fixtures/0566ae376bf048c5a0b50eba4595150d`.
The residue inventory is retained as `base-residue.json` in the evidence root below.

The committed new witness then failed 0/1 in 5.711 seconds on the unchanged leaking
fixture: `Directory.Exists(root)` was true, with the intended assertion
`fixture disposal must remove its exact root after success`. It left 44 files in
the separate recorded root `7a10a8925c9a4a5d9cf33137fe5c918f`. Only those two
literal absolute fixture paths were removed after recording their inventories;
their now-empty parents were removed nonrecursively. No sweep or glob cleanup ran.

After the repair, the witness passed 1/1, including all three exit paths, and the
original host-proof regression passed 1/1. Final inspection found no
`.antiphon/evidence/card-1901` directory. Every green row used the clean, committed
repaired source above and native default build output with `UseAppHost=false`.

| Green selection | Total / passed / failed / skipped | Sum of leased row wall seconds |
|---|---|---|
| New cleanup witness | 1 / 1 / 0 / 0 | 9.85 |
| Original fresh-graph host-proof regression | 1 / 1 / 0 / 0 | 15.72 |
| WindowsUnitPlatformAdmissionTests alone | 7 / 6 / 0 / 1 | 34.23 |
| Required eleven-class guard union | 114 / 113 / 0 / 1 | 213.33 |
| All VolumeRetirementApplyGateTests methods, serial exact filters | 13 / 13 / 0 / 0 | 467.69 |
| Publication methods listed below, serial exact filters | 25 / 25 / 0 / 0 | 1437.52 |

These are 161 results: 159 passed, zero failed and two expected occurrences of
`C1210_Windows_linux_only_outcomes_are_exact` skipped for Windows placement.
The deliberate historical red witness is separate and remains red.
The guard union included TestClassificationPolicyTests, TestClassificationGuardTests,
SlowTestRegistryMergeTests, SlowTestTripwireTests, NativeLinuxPolicyWitnessTests,
DerivedImpactIndexTests, MechanicalPcContractTests, TaskPlatformGuidanceTests,
C1729LayeringGuardTests, TestLaneCategoryGuardTests and
WindowsUnitPlatformAdmissionTests. Layering retained exactly:
`VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.

All rows ran serially, each under ten minutes; the longest was the guard union at
213.33 seconds. No whole-publication-class invocation was attempted: its supplied
estimate of 23 minutes cannot fit a ten-minute leased row. The executed 25 exact
methods alone consumed 23.96 minutes including repeated process startup.

The three test builds passed with zero errors: base 219.19 seconds / 2106 warnings,
red witness 216.60 seconds / 2107 warnings, repaired source 229.40 seconds / 2107
warnings. The added test has the same missing-timeout-token analyzer warning as
the existing methods. The regeneration driver was built first in its own granted
slot: 9.04 seconds, zero warnings/errors. Both subsequent separately leased index
regenerations printed `derived-impact-index unchanged`; no index commit was needed.
No build or test driver ran outside `scripts/build-slot.ps1` or without a granted
lease. All recorded slot waits were zero seconds. Initial `/proc/loadavg` was
`0.49 1.11 2.72`; a later sample reached `23.92 12.19 6.95`. Samples are provisional
contended-host observations, not p95. Existing timeouts were not widened.

The first short continuation wrapper had a quoting/parser error before launching
any driver or test; its log is preserved and carries no test credit. Its corrected
replacement executed the four remaining short publication methods successfully.

Raw evidence is ignored under
`/work/worktrees/task-c6da537d/.antiphon/evidence/task-c6da537d/`: build/regeneration
logs, original red/base TRX, residue inventories, selection JSON, and per-row
`row.json`, `run.trx`, stdout/stderr. Green rows also retain opt-in phase JSONL in
`timing/`, including fixture setup, CLI waits and teardown where instrumented.
Uninstrumented phase attribution is unknown; row wall times are not phase sums.
The wrappers were foreground local children and were awaited to completion.

## Merge handoff and limits

Proceeding under the caller's standing test-only repair authority; no additional
approval or Review was requested. Mutation checks remain operator-waived
(2026-11-12), not executed or credited.

Current fetched master `d100f8fa2c679a5c74b6b235087308776a8f5d50` was verified to
retain policyHash `b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d`,
PinnedInputDigest `26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e`
and LegacyExclusionDigest
`6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`.
This repair changes none of their inputs, so no re-pin was made.

Evidence-history checking over base through repaired source reported two commits,
zero generated evidence entries and zero violations. `git diff --check` passed.
`git merge-tree --write-tree origin/master HEAD` retained the four assigned
conflicts in `scripts/test-live-lane-run.ps1`, `tests/Shared/TestTiming.cs`,
`tests/derived-impact-index.json` and `tests/test-execution-policy.json`.
They were not resolved here and belong to Merge. The required ancestor check passed.

Merge must preserve the incomplete verification gate from
[the round-3 report](2026-11-15-why-missed-card-1901-roster-gap.md): full
RemoteScriptContractTests execution and native Windows placement remain owed.
This task additionally leaves the eight publication corpus methods below unrun.
This is not a Full/Clean or landable claim. Next stage: **merge**.

An exact-method rerun uses the existing built output after an appropriate leased
build: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <row> -- dotnet run
--project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter
'/*/*/VolumeRetirementPublicationTests/<method>' --report-trx
--results-directory <fresh-evidence-directory>`. Keep methods serial and bounded.

## Exact publication method inventory

| Method | Result | Leased row wall seconds |
|---|---|---|
| `Host_origin_salvage_proves_exact_required_objects` | 1 passed / 0 failed | 21.14 |
| `Host_salvage_namespace_and_types_are_closed` | 1 passed / 0 failed | 48.6 |
| `Host_salvage_snapshot_prunes_and_renews_exact_objects` | 1 passed / 0 failed | 53.16 |
| `Origin_salvage_graph_passes_the_full_offline_audit` | 1 passed / 0 failed | 85.8 |
| `Local_salvage_refs_are_protected_roots` | 1 passed / 0 failed | 71.93 |
| `Reflog_patch_equivalence_never_proves_publication` | 1 passed / 0 failed | 132.18 |
| `Other_remote_tracking_refs_do_not_prove_publication` | 1 passed / 0 failed | 68.4 |
| `Detached_linked_heads_and_logs_require_exact_publication` | 1 passed / 0 failed | 71.74 |
| `Tags_preserve_object_and_ancestry_requirements` | 1 passed / 0 failed | 91.31 |
| `V2_invalid_bundle_cannot_use_present_head_fallback` | 1 passed / 0 failed | 90.67 |
| `Selector_fixture_own_gitdir_file_stays_in_full_audit` | 1 passed / 0 failed | 113.37 |
| `Selector_fixture_own_gitdir_symlink_stays_in_full_audit` | 1 passed / 0 failed | 111.02 |
| `Salvage_deleted_after_sealing_refuses_ready` | 1 passed / 0 failed | 35.84 |
| `Head_rolled_back_after_sealing_refuses_ready` | 1 passed / 0 failed | 35.3 |
| `Salvage_replaced_after_sealing_refuses_ready` | 1 passed / 0 failed | 36.04 |
| `Origin_unreachable_at_final_check_refuses_ready` | 1 passed / 0 failed | 35.32 |
| `Origin_rebound_at_final_check_refuses_ready` | 1 passed / 0 failed | 32.38 |
| `Unchanged_objects_renew_at_each_ready_gate` | 1 passed / 0 failed | 74.9 |
| `Offline_scan_requires_live_completion` | 1 passed / 0 failed | 20.37 |
| `Origin_graph_corpus_seed_1901` | NOT RUN: task timebox | unknown |
| `Origin_graph_corpus_seed_1902` | NOT RUN: task timebox | unknown |
| `Origin_graph_corpus_seed_1903` | NOT RUN: task timebox | unknown |
| `Origin_graph_corpus_seed_1904` | NOT RUN: task timebox | unknown |
| `Origin_graph_corpus_seed_1901_second_half` | NOT RUN: task timebox | unknown |
| `Origin_graph_corpus_seed_1902_second_half` | NOT RUN: task timebox | unknown |
| `Origin_graph_corpus_seed_1903_second_half` | NOT RUN: task timebox | unknown |
| `Origin_graph_corpus_seed_1904_second_half` | NOT RUN: task timebox | unknown |
| `Salvage_withdrawn_after_staging_refuses_control` | 1 passed / 0 failed | 44.14 |
| `Head_rolled_back_after_staging_refuses_control` | 1 passed / 0 failed | 45.37 |
| `Origin_unreachable_after_staging_refuses_control` | 1 passed / 0 failed | 45.76 |
| `Final_origin_binding_refresh_is_inside_effect_window` | 1 passed / 0 failed | 51.49 |
| `Binding_clock_precedes_fresh_origin_url_observation` | 1 passed / 0 failed | 12.15 |
| `Binding_clock_record_requires_current_kernel_and_call` | 1 passed / 0 failed | 9.14 |

All 13 apply-gate public methods were executed individually; their exact method/filter identities and durations are retained in the per-row receipts. No apply-gate method was omitted.
