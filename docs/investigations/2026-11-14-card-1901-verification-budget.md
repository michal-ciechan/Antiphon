# CARD-1901 verification budget

Measurement task `7555b555`, 2026-11-14. Measurement-only work; no CARD-1901
implementation or live retirement operation is authorized by this report.

## Provenance

- Plan: `origin/feat/card-task-2c3ecf9c`, commit `3ef663e98d51e3063614207fcf22343b5b7e3fe9`;
  `docs/superpowers/plans/2026-11-14-card-1901-retirement-audit-plan.md`.
- Current master observed using `git ls-remote origin refs/heads/master`:
  `8c06c7e11f40cc224235ecbc68c2389913cc67ee`.
- Fresh detached measurement worktree: `/tmp/card1901-7555b555-master`, at that
  exact master commit. The assigned report branch starts at the same commit.
- Host: Linux x86_64, kernel `4.15.0-213-generic`, 24 available processors,
  .NET SDK `10.0.404`. The shared host is oversubscribed (CARD-1906).
- Raw evidence and local measurement drivers:
  `/tmp/card1901-7555b555-evidence`. Generated receipts, logs, TRX and phase
  journals are external to Git. Every driver uses `scripts/build-slot.ps1`;
  tests run serially via `dotnet run --project tests/Antiphon.Tests --no-build
  -p:UseAppHost=false`. Native default build output is retained for reuse.
- Admission is measured separately from lease-held execution. Load columns use
  `/proc/loadavg`; one run is not a p95. Uninstrumented phases remain unknown.
- Tracked measurement source was clean after all runs. Binary SHA-256:
  `Antiphon.Tests.dll`: `8adf611cea6147a197059a13e34c1cbb1f531c08dec92a18746f3a06602f7d39`;
  impact driver: `e0fa1b3a3cdf7c94239a1c8e8ae2fe4ff05fe3557d9acf35877a199eb9979b65`.

## Cold build

Fresh checkout outputs; shared NuGet cache retained. Command:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1901-cold-build -SlotWaitMinutes 10 -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj -p:UseAppHost=false -m:1 -nodeReuse:false
```

| Outcome | Admission (s) | Execution (s) | Whole wall (s) | Load start (1/5/15 min) | Load end |
|---|---:|---:|---:|---|---|
| Passed; 0 errors, 2,091 warnings | 211.6 | 232.4 | 444.1 | 19.50 / 23.57 / 22.02 | 23.14 / 22.06 / 21.67 |
| Impact driver; 0 errors/warnings | 213.1* | 11.0 | 224.1* | 50.44 / 40.27 / 32.19 | 49.27 / 40.36 / 32.30 |

\* The driver was first in the finalization lease: its admission is that batch's
launcher-to-grant time; the subsequent oracle/tests reused the lease. Both cold
projects total **243.4 s execution**. Recommend **8 min test build + 1 min driver
build**, or **9 min combined preparation**, with admission separate. Broker waits
were 210 s for each cold admission; launcher overhead explains 211.6/213.1 s.

MSBuild itself reported 231.22 s. The graph includes the checkpoint coordinator
and referenced fixture/probe projects; no separate coordinator bootstrap build
was needed. It does **not** build the standalone derived-impact regeneration
driver. See the prerequisite finding below. Recommend a provisional **8 min
cold-build execution cap**, approximately twice this one observation. Keep the
**10 min slot-admission cap** independent. This is n=1, not a p95, and measures
the native-output Debug build rather than checkpoint cold custom-output builds.

## Budget arithmetic that Code must replace

| Plan accounting | S1 | S2 | S3 | S4 Linux | Windows | Total test minutes |
|---|---:|---:|---:|---:|---:|---:|
| `EstimatedMinutes` checkpoint cells | 7 | 7 | 17 | 15 | 10 | 56 |
| Performance-section approximations | 1 | 2 | 7 | 6 | 3 | 19 |

The performance section's 39–59 min adds five 4–8 min cold builds to the second
line. Applying those same build estimates to the actual checkpoint cells gives
**76–96 min**, before admission. Neither is a measured duration. Mutation's
separate provisional 95–190 min is additional, not included in either total.

## Measurement status

The closed existing checkpoint selections have been measured. The adjacent full-class census remains incomplete within the 90-minute task timebox. Test rows execute alone and serially
inside one foreground, slot-gated measurement driver. Top-level test launches
use `--no-build`; its admission is charged once. Each child row reports zero
additional admission and records reuse of the held lease. Existing policy tests
compile their own temporary probe projects as fixture work; this cost is included
in those test rows. The cold build above has its own independent lease.

## Fresh-checkout prerequisite finding

`DerivedImpactIndexTests` returned **27 executed / 26 passed / 1 failed / 0
skipped** in 204.83 s. The failing method is
`regenerate_script_reports_changed_then_unchanged`. Its assertion expected exit
0 and received exit 1:

```text
The application to execute does not exist:
scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll
```

This occurred on untouched master, before any CARD-1901 code. It is a missing
native preparation build, not evidence of a CARD-1901 behavior regression.
`scripts/regenerate-derived-impact-index.ps1 -NoBuild` deliberately requires the
already-built driver. The initial build inventory omitted this standalone
project. Add declared **CP-0** preparation:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1901-impact-driver -SlotWaitMinutes 10 -- dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -m:1 -nodeReuse:false
```

Board search `card.ps1 search 'DerivedImpactIndex' -Board Antiphon -All` completed
with 13 hits. Full `CARD-1868` text explicitly records this exact prerequisite in
A-2, including the class-wide selector and failing method. Reuse CARD-1868; no
duplicate was filed. The driver cold build took 11.0 s (exit 0); the exact failing method was rechecked after preparation in 17.3 s: 1 executed / 1 passed / 0 failed / 0 skipped. The original class and combined-row reds remain historical red receipts.

## Executed baseline rows

Every row is on master `8c06c7e11f40cc224235ecbc68c2389913cc67ee`. E/P/F/S means TRX executed/passed/failed/notExecuted. Rows have zero additional admission within their foreground batch lease. `serial-suite` waited 75 s (77.0 s launcher-to-grant); the final C1708/recheck rows reused the finalization lease after its cold driver build, whose broker wait was 210 s. These reused leases do not predict zero admission for Code checkpoints.

| Row | E/P/F/S | Execution s | Additional admission s | Load start (1/5/15) | Load end |
|---|---|---:|---:|---|---|
| host-proof | 12/12/0/0 | 69.3 | 0.0 | 19.24/23.06/22.37 | 13.33/20.42/21.50 |
| content-selected | 12/12/0/0 | 248.6 | 0.0 | 13.33/20.42/21.50 | 16.28/15.79/19.16 |
| content-selected-repeat | 12/12/0/0 | 404.8 | 0.0 | 16.28/15.79/19.16 | 30.33/28.38/24.05 |
| git-selected | 30/30/0/0 | 108.2 | 0.0 | 30.33/28.38/24.05 | 20.86/25.80/23.59 |
| remote-impact | 5/5/0/0 | 12.5 | 0.0 | 20.86/25.80/23.59 | 19.48/25.24/23.44 |
| transport-selected | 4/4/0/0 | 251.6 | 0.0 | 19.48/25.24/23.44 | 13.12/18.29/21.00 |
| transport-selected-repeat | 4/4/0/0 | 227.8 | 0.0 | 13.12/18.29/21.00 | 9.71/14.24/18.80 |
| TestClassificationPolicyTests | 42/42/0/0 | 65.0 | 0.0 | 9.71/14.24/18.80 | 9.27/13.21/18.14 |
| TestClassificationGuardTests | 1/1/0/0 | 9.8 | 0.0 | 9.27/13.21/18.14 | 10.87/13.43/18.16 |
| SlowTestRegistryMergeTests | 5/5/0/0 | 18.0 | 0.0 | 10.87/13.43/18.16 | 11.83/13.51/18.11 |
| SlowTestTripwireTests | 2/2/0/0 | 19.5 | 0.0 | 11.83/13.51/18.11 | 16.92/14.62/18.37 |
| NativeLinuxPolicyWitnessTests | 3/3/0/0 | 18.5 | 0.0 | 16.92/14.62/18.37 | 14.97/14.33/18.20 |
| MechanicalPcContractTests | 11/11/0/0 | 7.7 | 0.0 | 14.97/14.33/18.20 | 14.17/14.18/18.13 |
| TaskPlatformGuidanceTests | 5/5/0/0 | 7.6 | 0.0 | 14.17/14.18/18.13 | 12.93/13.91/18.00 |
| C1729LayeringGuardTests | 10/10/0/0 | 18.5 | 0.0 | 12.93/13.91/18.00 | 11.31/13.50/17.80 |
| TestLaneCategoryGuardTests | 1/1/0/0 | 9.2 | 0.0 | 11.31/13.50/17.80 | 11.03/13.37/17.71 |
| ProcessSpawnLimitTests | 57/57/0/0 | 13.6 | 0.0 | 11.03/13.37/17.71 | 10.51/13.14/17.56 |
| TestTimingOperationLabelTests | 2/2/0/0 | 8.2 | 0.0 | 10.51/13.14/17.56 | 10.05/12.95/17.46 |
| DerivedImpactIndexTests | 27/26/1/0 | 204.8 | 0.0 | 10.05/12.95/17.46 | 11.46/12.36/16.35 |
| ReleaseImpactEnvelopeTests | 11/11/0/0 | 59.0 | 0.0 | 11.46/12.36/16.35 | 11.43/12.05/16.00 |
| LiveLaneRunnerTests | 4/4/0/0 | 43.2 | 0.0 | 11.43/12.05/16.00 | 14.16/12.61/16.00 |
| NightlyCoreTests | 1/1/0/0 | 52.8 | 0.0 | 14.16/12.61/16.00 | 13.45/12.80/15.87 |
| native-policy | 2/2/0/0 | 86.7 | 0.0 | 13.45/12.80/15.87 | 25.91/15.95/16.66 |
| native-policy-repeat | 2/2/0/0 | 81.4 | 0.0 | 25.91/15.95/16.66 | 25.34/19.32/17.86 |
| guards-combined | 182/181/1/0 | 493.4 | 0.0 | 25.34/19.32/17.86 | 38.69/31.73/23.94 |
| guards-combined-repeat | 182/181/1/0 | 492.7 | 0.0 | 38.69/31.73/23.94 | 17.63/24.31/24.01 |
| C1708ReflogSalvageTests-full | 30/30/0/0 | 116.8 | 0.0 | 49.49/40.55/32.41 | 24.85/33.74/30.87 |
| derived-driver-prepared | 1/1/0/0 | 17.3 | 0.0 | 24.85/33.74/30.87 | 22.64/32.82/30.61 |

The exact selected filters are retained in `suite-rows.json` and each row receipt; class-only rows use `/*/*/<Class>/*`. The full census came from `rg -l -i 'c590|RecycleGitAudit|WorktreeReflogSalvage|retire-temp|selector_fixture|refs/antiphon/salvage' tests/Antiphon.Tests --glob '*Tests.cs'`, then actual class declarations. Keyword hits include incidental documentation/roster consumers; they are diagnostic scope, not additions to ordinary Code verification.

Adjacent census classes without a full-class receipt at this point: `C1364BoundLandWaitProofTests`, `CacheSeedNpmOwnershipTests`, `ClaudeTokenRefreshOptInTests`, `DockerDeliveryBarrierTests`, `DockerDeliveryDatabaseTests`, `DockerDeliveryRecoveryCommandTests`, `DockerStackContractTests`, `DockerStackDocumentationTests`, `DockerStackSmokeCommandTests`, `DockerTestCommandTests`, `HostJqFixtureClosureTests`, `HostJqPrerequisiteScriptTests`, `LinuxTestRosterTests`, `PlanCoverageGoldenTests`, `RemoteScriptContractTests`, `RetiredTempContainerHostTests`, `RetiredTempContainerScriptTests`, `RetireTempRunnerTimestampTests`, `RollingProductionMountTests`, `RollingVolumeRecycleCensusChurnTests`, `RollingVolumeRecycleScriptTests`, `SeedSmokeTargetingPackContractTests`, `StandingPipelinePolicyDocumentationTests`, `TestAppHostPathTests`, `VolumeRetirementApplyGateTests`, `VolumeRetirementContentTests`, `VolumeRetirementReceiptTests`, `VolumeRetirementWrapperTests`.

## Recommended closed rows for Code

Add CP-0 (the native impact-driver build) to declared preparation. Reuse one
`Antiphon.Tests` build within each committed S1/S2/S3/S4/platform group; every
split row below points at that group's existing build. This is not a build per
row. Preserve the two S1 expected assertion reds, all later expected method/count
rosters, the separate native Windows witness, and all 19 post-land PCs.

S1 keeps CP-1=H1 and CP-2=V7. Split CP-3 into CP-3.H1/H2/H3; split CP-4 into
CP-4.H1/H2/H3/V1–V9; split Windows CP-11 into CP-11.H1/H2/H3. Each is a single
method, exactly one result. H rows get a provisional 2 min cap and V rows 7 min.
The new methods do not exist on this master: these are **n=0 proxy budgets**, not
measurements. The host proxy is the existing 29.8 s ancestry/writer case; the
full-preview proxy is the 163.8 s private-origin bridge case, plus the observed
startup allowance. Collect actual method timing during the already-required
first Code runs and tighten near 2x the measured maximum; neither one nor two
samples is a p95. Windows costs remain unmeasured Linux proxies.

Keep CP-5 and CP-12's full host-proof class at 3 min. Keep the exact original
CP-7/CP-8/CP-9/CP-13 selections at 4/1/9/3 min respectively. Split CP-6 and CP-10
as follows. These group costs are projections from their contained executions,
not newly executed selectors. The manifest uses whole-minute caps; the report's
raw seconds retain the measurement precision.

| Row | Exact filter | Results | Cap min |
|---|---|---:|---:|
| CP-6a | `/*/*/VolumeRetirementContentTests/(Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent*)\|(Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof*)` | 9 | 5 |
| CP-6b | `/*/*/VolumeRetirementContentTests/(Every_ref_recovery_tip_and_tag_object_requires_publication*)\|(Gone_tracking_retains_original_commit_protection*)` | 2 | 5 |
| CP-6c | `/*/*/VolumeRetirementContentTests/Private_origin_advanced_head_uses_real_host_proof` | 1 | 7 |
| CP-10a | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)/*` | all named classes | 4 |
| CP-10b | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | all named classes | 2 |
| CP-10c | `/*/*/(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(TestTimingOperationLabelTests*)\|(DerivedImpactIndexTests*)/*` | all named classes | 9 |
| CP-10d | `/*/*/(ReleaseImpactEnvelopeTests*)\|(LiveLaneRunnerTests*)\|(NightlyCoreTests*)/*` | all named classes | 6 |

Exact new-method filters (one hit each):

| ID | Exact filter | Cap min |
|---|---|---:|
| H1 | `/*/*/VolumeRetirementPublicationTests/Host_origin_salvage_proves_exact_required_objects` | 2 |
| H2 | `/*/*/VolumeRetirementPublicationTests/Host_salvage_namespace_and_types_are_closed` | 2 |
| H3 | `/*/*/VolumeRetirementPublicationTests/Host_salvage_snapshot_prunes_and_renews_exact_objects` | 2 |
| V1 | `/*/*/VolumeRetirementPublicationTests/Origin_salvage_graph_passes_the_full_offline_audit` | 7 |
| V2 | `/*/*/VolumeRetirementPublicationTests/Local_salvage_refs_are_protected_roots` | 7 |
| V3 | `/*/*/VolumeRetirementPublicationTests/Reflog_patch_equivalence_never_proves_publication` | 7 |
| V4 | `/*/*/VolumeRetirementPublicationTests/Other_remote_tracking_refs_do_not_prove_publication` | 7 |
| V5 | `/*/*/VolumeRetirementPublicationTests/Detached_linked_heads_and_logs_require_exact_publication` | 7 |
| V6 | `/*/*/VolumeRetirementPublicationTests/Tags_preserve_object_and_ancestry_requirements` | 7 |
| V7 | `/*/*/VolumeRetirementPublicationTests/V2_invalid_bundle_cannot_use_present_head_fallback` | 7 |
| V8 | `/*/*/VolumeRetirementPublicationTests/Selector_fixture_own_gitdir_file_stays_in_full_audit` | 7 |
| V9 | `/*/*/VolumeRetirementPublicationTests/Selector_fixture_own_gitdir_symlink_stays_in_full_audit` | 7 |

Exact retained selectors:

- `/*/*/RemoteScriptContractTests/(C1105_Git_audit_recovery_tips*)|(C1105_Git_audit_seed_content*)|(C1105_Git_audit_index_content*)|(C1105_Git_audit_object_completeness*)`
- `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(C0995_Deploy_temp_checks_host_absence_before_checkout*)|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)|(C849_Deploy_ordering_contract_is_pinned*)|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)`
- `/*/*/RollingVolumeRecycleScriptTests/(C1008_Host_proof_admits_only_an_authenticated_private_origin*)|(C1008_Host_proof_file_omits_credentials*)|(C1008_Transport_scripts_are_ascii*)|(C1008_Documentation_and_transport_pins_match*)`
- `/*/*/ReleaseGatePlatformTests/(LinuxOnlyWindowsExclusionPolicy*)|(C1686_LinuxOnlyCensus*)`

Checkpoint import's 15 min / 3x defaults are not these measured caps. Pin explicit
per-row `timeoutMinutes` in the ignored manifest after import. The current tool's
build deadline has a 15 min floor; setting `timeouts.rowMinutes: 8` does not remove
it. `--row-timeout` also overrides every test row, so it cannot independently
express the recommended 8 min cold-build cap and all the narrower row caps.
Record the existing build-floor exception rather than claiming an 8 min tool
watchdog. Use the tool's derived total allowance for each After/platform group;
expected wall time is not permission to force a total below that derivation.
Keep the 10 min admission bound separate; exit 4 remains not-run/blocked.

The first Code/Review runs supply the missing comparable samples. Do not run
extra cold builds solely to manufacture a percentile. This report does not
measure the future bound-pack rebuilds or method bodies for the 19 mutation
controls; keep that independent over-30-minute Debug obligation with post-land
Mutation. No live preview, apply, deployment or real registry qualification is
part of this measurement.

## Partial census and remaining work

`C1364BoundLandWaitProofTests` was stopped after 703.5 s including cancellation/join; exit 1, no TRX. Counts are unavailable and this row is not green. The phase journal completed `C1364_Wrapper_redeploy_reports_waited` (29.7 s) and `C1364_Host_recycle_post_removal_reports_waited` (513.2 s), then had the admission method pending. The latter full method alone needs a smaller vector/setup design to support a 10-minute cap near 2x observed cost. All stop targets were proven descendants of the owned measurement driver; `census-stop.json` records zero remaining owned processes.

The full-class keyword census is not complete. Its exact 28 outstanding classes are listed above; none of those full-class rows is credited. The orchestrator accepted this budget report and handoff as sufficient and assigned those measurements to the Code task's own rows. No further Debug measurement timebox is required. The closed Code method selections have timings, and the partition proposal is usable; complete adjacent coverage remains a Code obligation.

## Independent real-Git oracle

The owned fixture has an approved bare origin, separate bare backup, publisher, audited common repository and detached linked checkout. It contains 16 original commits, fourteen unequal-OID/equal-stable-patch-id alternatives, a unique sibling and an annotated tag. Explicit expected accepted commit sets are 15 before and 32 after exact publication. The first v2 audit refuses at `check=rev-list` with 17 unpublished commits (all sixteen originals plus the sibling); exact original-chain/sibling publication then passes with two repositories. No production ref predicate was copied into the oracle.

| Phase | Execution s | Load start (1/5/15) | Load end |
|---|---:|---|---|
| Graph construction, independent assertions and extraction | 2.7 | 49.27/40.36/32.30 | 49.27/40.36/32.30 |
| v2 unpublished audit; exit 2 | 3.4 | 49.27/40.36/32.30 | 49.49/40.55/32.41 |
| v2 published audit; exit 0 | 0.6 | 49.49/40.55/32.41 | 49.49/40.55/32.41 |

Total fixture/oracle wall 6.9 s; 161 explicitly instrumented Git children outside the audit shell consumed 2.9 s. Bundle/manifest preparation cost 0.052 / 0.016 s. Audit-internal Git child attribution remains unknown. Scratch and fixtures were retained as evidence, so teardown deletion is unmeasured. The oracle reused the finalization lease; additional admission was zero.

The committed audit body and content splice ran under Bash. Only mount-root constants were mapped to owned temporary paths, and scratch deletion was suppressed to retain evidence; verdict logic was unchanged. This measures the current head-only v2 path, not the future canonical-salvage implementation. Two earlier harness attempts failed before a valid oracle verdict (literal shell-splice expansion, then temporary `/tmp` mount classification); their logs are retained and excluded from valid oracle timings.

## Serial total and phase attribution

The original existing Linux checkpoint selection, counted once using each row's measured maximum, is **23.8 min execution** (247 result executions, including the known prerequisite red). The separate individual-class census and required over-budget repeats are measurement expense, not extra ordinary Code gates.

The full future plan cannot have an observed total on this master: its 12 new methods and native Windows executions are absent/unrun. A conservative **~85 min serial execution forecast** uses the measured existing rows, the smaller-group projections, ten new H executions at the existing writer-case proxy, ten new V executions at the existing full-preview proxy, five cold test builds at the Linux observation (including an explicitly unmeasured Windows proxy), one driver preparation build, and the Windows existing host-proof proxy. The forecast includes extra launch allowance for CP-6 splitting. Separate coordinator bootstrap cost was not isolated from the test build; allow the plan's provisional 10–60 s in addition. This is not a p95 or a complete executed plan. New H/V and Windows rows remain n=0 until their ordinary Code runs.

Queue time is additional and unforecast: the cold-test broker wait was 210 s; the serial test battery waited 75 s. The actual Code coordinator acquires leases per activity, unlike this batch's reused lease; do not multiply zero additional row waits into a wall-time promise. Keep each admission finite at 10 min and derive total/hard-wall allowances from the real manifest. Mutation and Review expense are additional.

In the slower content sample, the private-origin case spent 163.8 s overall, with 154.3 s exclusive process work, 0.052 s attributed fixture work and the remainder unattributed. Host-proof internals have no equivalent child spans: their uninstrumented time is unknown, not zero. Launch-to-first-test includes discovery; the combined filter observed up to 35.5 s, so the plan's 2–10 s host-start figure is not a measured bound. Keep immutable Git graph setup shared, isolate mutable origin refs, reuse slice builds, and measure setup/process/teardown in the first new-method executions.

## Completion and rerun

TRX-backed rows total 660 executions, 657 passed, 3 failed and 0 skipped, including repeated selections. Three historical failures are the same known driver-precondition assertion. The stopped C1364 row is additional partial evidence and has no credited counts.

Rerun any exact filter from the tables with:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1901-row -SlotWaitMinutes 10 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter "<exact filter above>" --report-trx --report-trx-filename run.trx --results-directory .antiphon/evidence/card-1901/rerun
```

Build both declared prerequisites first. Raw evidence is retained in `.antiphon/evidence/card-1901/budget-7555b555/` (ignored); essential source, outcomes, timings and loads are in this committed report. No CARD-1901 implementation or live host/volume/registry operation was performed.

--- next stage ---
next: code
handoff: Add CP-0; use CP-3/CP-4/CP-11 method rows at H=2 min and V=7 min; CP-6a/b/c=5/5/7 min and CP-10a/b/c/d=4/2/9/6 min; cold preparation 8+1 min, admission 10 min separately. Code measures the 28 named outstanding adjacent classes in its own rows, as accepted by the orchestrator.
artifact: docs/investigations/2026-11-14-card-1901-verification-budget.md
