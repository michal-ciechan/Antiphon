# CARD-1955: retirement rendering and refused Ready admission

The full audit rendering now precedes the final origin-binding clock. A late
refusal revokes only the current invocation's mutable Ready receipt. The five
second bound, origin observations, proof protocol and receipt admission lifetime
are unchanged. This repair requires a full ordinary Review before land/activation.

Final bounded continuation: **363 passed, 1 base-confirmed inherited failure,
3 expected platform skips** across recorded source revisions. The eight
corpus methods and native C994 part 2 now pass. Required verification remains
incomplete: **37 exact pieces / 281 test methods** below. The caller's next
stage is Full ordinary Review by another company; this is not Clean/Full and does
not authorize land or activation. Earlier settlements below are historical.

## Change

`scripts/c1008-retirement.sh` prepares the Ready envelope and renders the complete
`audit.txt`, `findings.jsonl` and `proof.md` into a private operation-owned staging
directory. It flushes the three files and their directory before the final binding
clock is observed. After fresh approval, advertisement/control and proof-custody
checks, the writer only writes the bounded receipt payloads and renames staged
files. All three Ready payloads are fsynced before canonical publication, and both
source and destination directories are flushed after the renames.

`scripts/c590-remote.sh` supplies the `ready-published` stage to the final fence.
Its result writer revokes the current invocation's installed receipt on a failed
preview, including a writer/directory-flush refusal or a phase-completion refusal.
The revocation is an atomic durable `state=Refused, consumed=true` transition and
checks the operation and installed preview nonce. It never rewrites a previous
invocation's Ready on an earlier failure. Immutable `preview-<nonce>.json`,
`preview.json`, audit/findings/proof and refusal evidence are preserved.
`lifecycle.readyPublished` remains a conservative historical fact that Ready may
have been published; it is not reset to hide that fact. A revocation persistence
failure refuses lifecycle completion rather than acknowledging an inactive phase.

A failed effect window persists a bounded, scrubbed record with only schema,
phase nonce, fixed stage, elapsed milliseconds and the unchanged 5000 ms limit.
Host records are in `publication-diagnostics.jsonl` (at most 8192 bytes); the
current case also contains `c1609-publication-diagnostic.json`. The normal bridge
copies the case evidence after refusal, so this no longer depends on stderr that
the coordinator discards. No URL, control payload, audit text or raw stderr is
stored in this diagnostic.

The final binding start is still taken from the helper clock before fresh origin
URL reads. Rendering does not reset that clock. Every effect still uses its own
fresh host proof, current approval, fresh advertisement/control and custody gates;
no earlier live-origin result is reused. The 900-second Ready admission, fsync
refusals, dirty-work refusal and clock/nonce/header checks remain in force.

## Focused red and green witnesses

The branch started at current `origin/master`
`52a90a3935f1f8ed4198200ce3a5e21ca8dab370`. Production scripts were unchanged for
both red runs at `63686b3d2ec3a3737f25e2e85d4b0c04fb3d2df7`.
The first test commit `08e8bb38d` had an assertion-overload compilation error;
`63686b3d2` corrects that test-only error. It is not counted as assertion red.

`VolumeRetirementReceiptTests.Large_ready_rendering_precedes_the_unchanged_final_fence`
uses the existing real publication fixture, a fixture-owned origin, full proof
renewal/live control, at least 10,417 audit lines / 1,131,573 bytes, findings and
a deterministic six-second loaded-render delay. Red: 1 failed, after successful
live control, expected exit 0 but observed exit 2. Green at
`b0c6ab4e1719aab92a000fab9c728724bb620ae9`: 1 passed. The same workload rendered
exactly once before control, with no Ready receipt installed before that control.
The complete audit, findings and Ready proof were retained. Run durations were
47.202 seconds red and 47.173 seconds green (including discovery, excluding slot
wrapper overhead). This is a controlled rendering-cost witness, not a measured
claim about server2's historic renderer latency.

`VolumeRetirementReceiptTests.Late_ready_fence_refusal_invalidates_apply_admission`
delays six seconds after the real Ready writer returns. The fresh control succeeds
and the enclosing preview refuses `proof-stale` with no successful Ready marker.
Red: 1 failed because the mutable state was Ready instead of Refused. Green at
`b0c6ab4e1719aab92a000fab9c728724bb620ae9`: 1 passed. It checks Refused state,
unchanged immutable Ready evidence, low-level Apply rejection and rejection by
the actual `New-C1609RetirementReadPacket`. The persisted `ready-published`
diagnostic reports elapsed greater than 5000 with limit exactly 5000. No fixture
volume is removed. Run durations: 46.946 seconds red and 48.274 seconds green.

The two method timeouts are narrowed to 100 seconds after these provisional
samples (body about 41-43 seconds, n=2 per method), about twice the observed body
cost rounded up. Existing shared fixture/process timing remains active; no new
TestTiming operation or selector-policy row is introduced. The derived impact
index adds the existing fixture and public-reader dependencies; its driver was
built first, regeneration reported changed, then unchanged, and the index has its
own commit. Nightly policy and protected-input pins are unchanged because their
inputs are not changed.

## Verification and performance

The explicit Debug brief authorizes direct gated build/test rows. They use native
project output, `UseAppHost=false`, TUnit `dotnet run --no-build`, fresh TRX and
phase timing under `.antiphon/c1955/`. Initial load was `0.61 0.79 0.87`; load is
also retained per execution in `executed.jsonl`. Every driver leases a build slot.
Serial test rows have a nine-minute session deadline. The closed selection is the
retirement classes below, the required guard union and the derived selector
contracts; heavy classes are split into exact method selections in `rows.json`.
This avoids a whole-assembly test run. All rows retain source SHA, filter, exit,
wall time and load. Uninstrumented/remainder phases remain unknown.

Classes: VolumeRetirementApplyGateTests, VolumeRetirementContentTests,
VolumeRetirementReceiptTests, VolumeRetirementHostProofTests,
VolumeRetirementPublicationTests, VolumeRetirementWrapperTests,
RollingVolumeRecycleScriptTests, RollingVolumeRecycleCensusChurnTests,
RetiredTempContainerHostTests, RetiredTempContainerScriptTests,
RetireTempRunnerTimestampTests, C1008PlatformContractTests,
C1008HostFixtureSetupInsertionTests, RemoteScriptContractTests,
C0824BranchDeletionRecycleAuditTests and C1364BoundLandWaitProofTests.
Native Docker classes are separate rows so their owned-daemon prerequisites and
cost cannot be mistaken for the shell fixtures.

Guard filter:
`/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(ImpactedClassesSelectorTests*)/*`.

Mutation checks are waived by the operator's 2026-11-12 instruction. Native
Windows/live server2 acceptance is not inferred from these local Linux fixtures.
The assigned brief prohibits touching server2, either live qualification run,
live volumes, deployment/restart or token-bearing retirement commands; none is
part of this repair execution. The initial PowerShell row helper was stopped
before launching any test (unquoted native wildcard expansion); execution uses a
literal-argv Node helper instead. That helper issue is not assertion red.

Final execution counts and unresolved environment failures are recorded in the
stored task report, alongside exact tested SHAs and fresh TRX paths.

## Activation without a new rc

Owner: [Exact origin salvage recovery / Script-only retirement activation while
the desktop stays on rc](../docker-stack.md#script-only-retirement-activation-while-the-desktop-stays-on-rc-card-1955).
`scripts/c590-real.ps1` reads sibling files under its own `$PSScriptRoot` when
preview runs and copies c590-remote, c1008-retirement, c1008-content-audit and
c1008-volume-metadata into a fresh owner-only host operation directory. It
verifies SHA-256 and seals that bundle read-only, retaining `tooling.sha256`.
The credential-capable producer is loaded from the same local script set.
These host shell scripts are not baked into the selected runner image.

With the existing invocation from `C:\src\Antiphon`, source bytes would still
come from that desktop checkout, detached at rc/2026-11-22
`a71c45ab4d5703d480f4eee4c4e30adab45bc4f9`. Publishing the repair to master does
not change it. Hosts continue to track rc, never master.

After full Review and confirmed land, the owner procedure exports the entire
confirmed tooling commit using `git archive` into a fresh ignored desktop
`C:\src\Antiphon\.antiphon\retirement-tooling\<landed-tooling-sha>\` directory.
Invoke that export's `scripts\deploy-server2.ps1` with explicit
`-Rolling -Sha a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 -Phase retire-temp -DryRun`.
The exact commands and publication checks are in the owner section above.
Retain the export SHA, producer hash, operation-owned tooling hashes and preview
exit/result. Its receipts are below the export's `.antiphon/rolling-server2/`.
The `-Sha` remains runtime provenance, while the fixed script bytes are selected
by the invoking script directory. No product image rebuild, rc cut, checkout
switch, restart or live proof reuse is needed. The first action is read-only
preview; only a newly successful full preview can authorize a later Apply under
the existing owner policy, immediately within 900 seconds, using the same export.
A refused operation must never be used as the Apply proof.

Standing caller authority already commissions this repair; no additional go-ahead
was requested. Review and host activation remain separate caller work.

## Historical first settlement: incomplete gate

The final production/test candidate is
`b7bd91d4f73146ca61f53f04b271701e2232c456`; the subsequent commit only records this
settlement. Latest distinct TUnit outcomes across the recorded source revisions
are **297 passed, 1 failed, 1 skipped**, not a single-tip whole-suite run. All
foreground drivers and owned children were joined before this amendment.

| Selection | Latest outcome |
|---|---|
| Required guard union plus ImpactedClassesSelectorTests | 164 passed, 1 Windows-only skip |
| VolumeRetirementApplyGateTests | 13 passed, whole class |
| VolumeRetirementContentTests | 41 passed, whole class |
| VolumeRetirementReceiptTests | 17 passed, whole class, including both new witnesses |
| VolumeRetirementHostProofTests | 14 passed, whole class |
| VolumeRetirementWrapperTests | 7 passed, whole class, including real Preview/Apply renewal |
| VolumeRetirementPublicationTests | 25 passed; eight graph-corpus tests not run |
| VolumeRetirementDockerTests | N1 and N2 passed; N3 failed |
| PTY ProcessSpawnLimitTests | 2 passed |
| SessionRunner ProcessSpawnLimitTests and C1818GcOwnerRobustnessTests | 12 passed |

The guard union originally reported 163 passed, 1 failed, 1 skipped at
`bc205d20edfdac55b81aacaa9d63787b359a99eb`: the introduced failure was missing
source-only admission entries for the two new native tests. Commit `b7bd91d4f`
adds those entries; the affected whole WindowsUnitPlatformAdmissionTests class
then reported 6 passed, 1 Windows-only skip. Preserve the original failure.
The layering receipt is `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.

Native N3 (`Exact_deletion_and_crash_resume_preserve_other_volumes`) failed its
first exact-delete assertion: expected exit 0, observed exit 2 with
`RecycleGitAuditUnknown reason=proof-stale`, removed=0. The unchanged native N3
fixture was rerun against a complete `git archive` of base
`52a90a3935f1f8ed4198200ce3a5e21ca8dab370`, with a local detached provenance HEAD
and shared read-only object lookup. It failed the same assertion/refusal at
`c1609-retirement-real-cases.mjs:455`. This is confirmed inherited red, not a green
acceptance result. Both candidate and baseline reported
`C1609_NATIVE session cleanup=absent`. No assertion, deadline or refusal was
loosened. The baseline fixture driver took 41.13 seconds, plus slot overhead.

The longest test driver was 461.878 seconds; rows remained below ten minutes.
Final load sample was `1.65 1.48 1.47`. Build drivers completed with zero errors;
warnings are not claimed absent. The derived-index driver was built first and
regeneration twice returned changed/unchanged. Protected policy inputs did not
change; the policy, protected-input and legacy-exclusion pins remain those in the
brief. Mutation checks remain explicitly waived.

Required execution remains absent for the eight
`VolumeRetirementPublicationTests.Origin_graph_corpus_seed_190[1-4]` methods
(including each `_second_half`), and these whole classes:
RollingVolumeRecycleScriptTests, RollingVolumeRecycleCensusChurnTests,
RetiredTempContainerHostTests, RetiredTempContainerScriptTests,
RetireTempRunnerTimestampTests, C1008PlatformContractTests,
C1008HostFixtureSetupInsertionTests, RemoteScriptContractTests,
C0824BranchDeletionRecycleAuditTests, C1364BoundLandWaitProofTests,
RollingVolumeRecycleDockerTests and RetiredTempContainerDockerTests.
Additional derived-impact closure support selections also remain unrun; the
retained manifest records them. Source-inventory helper names in that manifest
are not executable tests and must not be credited as coverage or run as zero-test
rows. Use actual discovered test methods for bounded continuation.

There is a concrete row-contract conflict: legacy
`RollingVolumeRecycleDockerTests.C1008_Real_docker_comparison` documents measured
618/709/827-second runs and a 20-minute timeout, while this brief requires every
row under ten minutes. Its fixture has no case selector. Bounded sharding or an
explicitly revised row contract is needed; a nine-minute cancellation is not a
passing run. The caller was asked whether to commission that separately or
extend this task; no extension was received. The 150-minute budget was exhausted
at the end of joined verification; only settlement checks/reporting followed.

Retained evidence is under `/work/worktrees/ws-01/.antiphon/c1955/`: per-row TRX,
`executed.jsonl`, `summary.json`, `deferred.jsonl`, `rows.json`, phase logs and
`base-native.log`/`base-native.json`. These are ignored generated evidence.
The evidence-diff guard over the seven implementation commits reported zero
violations; the final report amendment is checked separately. No live host,
qualification run, deployment, restart or server2 volume was touched.

Product witnesses pass; verification completeness is blocked by inherited N3 red,
missing execution and the legacy row conflict. This is neither Clean/Full nor
Interim. Continue the exact missing execution and inherited fixture repair under
Code, then obtain the requested full ordinary Review before land/activation.

## Reopened Debug: native N3 answer and bounded verification

This section supersedes the earlier settlement's current-status conclusions; the
older outcomes, costs and missing rows above remain historical facts.

N3 is an **expected-success native end-to-end witness**, not a test whose desired
outcome is proof-stale. Its old form exercised real Docker deletion/crash-resume
but neither supplied the required live bidirectional proof exchange nor included
the large rendering workload. Therefore the identical base/candidate refusal was
an inherited fixture defect, not evidence that the production repair had cured
the real path and not an unavailable Docker/host prerequisite. N1/N2 had already
proved those native prerequisites. The original failing run predated diagnostic
instrumentation and persisted no elapsed timing.

The instrumented old N3 failed before the final binding clock even started:
`stage=exchange-required`, `elapsedMs=null`, `limitMs=5000`; stderr explicitly says
`elapsed_ms=unstarted`. No numeric zero or invented elapsed is attributed to that
failure. The bounded scrubbed case/host diagnostic now supports this pre-clock
refusal alongside control, Ready and per-effect timing. No audit, token, URL or
raw control response is persisted there.

N3 now drives the real live protocol: fresh origin proof production and validation,
renewal/control over owned docker exec pipes, current object sets, UID/custody and
fresh approval/advertisement checks. The first successful preview also renders at
least 1,131,573 bytes / 10,417 lines and 105 findings with a deterministic six-second
render delay. Both file flushes and staging-directory fsync finish before binding.
At source `771876d2858903f7646ab3313c606e6ff7b551da`, persisted native observations are:

| Stage | Elapsed ms | Clock/result |
|---|---:|---|
| Full render and staged durability | 6260 | final binding clock unstarted |
| Final live control | 1240 | passed; limit 5000 |
| Ready publication including remaining durable writes | 1360 | passed; limit 5000 |
| Four exact deletion effects | 1560 / 1880 / 3050 / 2300 | each passed; limit 5000 |
| Crash-path effect before intentional stop | 2120 | passed; limit 5000 |
| Three resume effects | 2290 / 1670 / 2180 | each passed; limit 5000 |

`NATIVE_GATE totalMs=19090` includes render/renewal work before the final binding
clock; it is not the five-second elapsed. No remaining audit/file/directory render
work consumed that final window. The separate focused late-refusal witness still
proves canonical Ready revocation and actual public read-packet Apply refusal.

The original combined N3 exceeded its existing 360-second child deadline after
its live fences passed. It is now two exact-method pieces, retaining all four
original scenarios: large preview/exact deletion and after-rm crash/resume in
`Exact_deletion_and_crash_resume_preserve_other_volumes`; replacement and
unattributed absence refusals in `Replacement_and_unattributed_absence_refuse_resume`.
Both passed (1 test each), gated wall times 254.705 and 179.409 seconds, with owned
cleanup checked absent. Intentional crash exit 137 and resume refusal exit 2 are
asserted subcall outcomes, not failed tests. Intermediate fixture integration and
deadline failures remain in their original TRX; they are not the rendering red
witnesses and are not relabelled green.

Legacy native comparison rows are now closed serial shards, not revised or
omitted coverage. RollingVolumeRecycleDockerTests has four pieces: the original
`C1008_Real_docker_comparison` name selects part 1; suffixes `_part_2` through
`_part_4` select the rest. Their exact native case counts are 12/8/7/7, union 34
(base 5, changed 29), with 1 passed TUnit test per piece. Gated wall times were
252.967/189.590/222.994/232.496 seconds. Part 1 source was
`9acb89cc4a81eb791572a9423e4457d8ae5ff67b`; parts 2-4 source was
`135c40fbdcf0f1e86725f6d883a17101b7f6d9d0`. The later first-method rename only
preserves the frozen baseline identity; the executable shard body is unchanged.
Native evidence JSON was copied byte-for-byte into retained ignored evidence and
hash-checked, including source SHA, exact roster and failure=null. Checked cleanup
was absent on every successful piece.

RetiredTempContainerDockerTests similarly retains its original
`C994_Real_retired_temp_lifecycle` name for part 1 (RD-1..4), with `_part_2` for
RD-5..10. Stateful sequences remain together. Children retain an eight-minute
deadline plus at most one minute owned cleanup; TUnit invocations remain below ten
minutes. No production resource-name firewall, identity assertion, crash assertion,
refusal branch or cleanup check was removed. Legacy fixture setup was updated for
current module paths, audit descriptors, actual fresh Preview before Apply and
actual public read-packet handling. Its first attempted part-1 runs found missing
current proof-producer/reader files in the fixture export; those failures remain
recorded.

The required guard union at `b68748d40918fbc9f2c5eed50aaae9c584b185d2` reported
113 passed, 1 failed, 1 Windows-only skip in 214.301 seconds. The introduced failure
was NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute: the first
shards had renamed the two frozen baseline method identities. The fix restores
those original method names for part 1 and keeps the other explicit shard names.
No policy/baseline exclusion or marker is loosened. Only that failed guard needs a
narrow green rerun; the passing union members are not rerun merely to aggregate.

No TestTiming span or selector-policy row was added. The original 50 passing
ImpactedClassesSelectorTests remain credited; they are not redundantly rerun.
Optional unrelated derived-impact support closure rows are excluded from this
Debug task's explicit retirement/guard acceptance scope under the caller's minimal
testing instruction. Actual retirement classes, all eight corpus tests and the
required guard union remain required. Source inventory helpers are not tests.

## Historical second settlement at the added time budget

The +90-minute window began at 03:13:35 UTC. No answer was received to the pending
extension request before its 04:43:35 end. The already-started C994 driver was
joined at 04:44:44; no required row is silently omitted or declared green.
**Product repair and native N3 now pass; verification remains incomplete.**

The latest C994 first piece at source
`7673df1a6d3f879643c3576edfa89a77e6a05270` failed after 150.969 gated seconds
(1 failed TUnit test). RD-1, RD-2 and RD-3 completed and were retained; RD-4's
actual public wrapper deploy-temp admission refused `TempRetirementJournalUnknown`.
This is unresolved: no baseline comparison has established inherited attribution,
and the admission is not bypassed. Native cleanup was checked absent. Its raw
receipt is retained unchanged as
`.antiphon/c1955/native-receipts/Native-C994-first-complete-export.json`, SHA-256
`e127580074933e21fa342ba9e401d06fd4eb369b73bc629bd102f7c19a35ce8c`.
Do not count the three completed subcases as a passing whole shard.

The baseline-identity correction at
`c32b596a451c36e89b8892e6bbe85034ca97f7da` was built through the slot gate in
native output with UseAppHost=false: 0 errors, 2137 warnings, elapsed 4m52.54s
(slot held 296s). A narrow rerun of
`/*/*/NativeLinuxPolicyWitnessTests/Policy_rows_require_pinned_attribute`
then passed (1 test; body/session 6.627s, slot 11s). Together with the previous
union receipt this gives **114 passed, 1 Windows-only skip** for the required
union; the earlier 50 passing selector tests remain additional credited coverage.
The current layering receipt is exactly
`VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.

Latest distinct outcomes across recorded source revisions, with the two renamed
first shards normalized to their original identities, are **303 passed, 1 failed,
1 skipped**. This is neither a single-tip whole-suite run nor a complete required
selection. The current failed behavior is C994 part 1. The original native N3 red
is superseded by its two successful pieces, while its historical red receipts and
costs remain untouched. Required whole-class greens already retained are:
ApplyGate 13, Content 41, Receipt 17, HostProof 14, Wrapper 7; Publication has 25
passed with its eight graph methods still missing. Native N1/N2 each passed, N3
has two passed pieces, and Rolling native has four passed pieces (34 cases).
PTY limiter tests 2 and SessionRunner limiter/robustness tests 12 remain passed.

The derived-index driver was rebuilt first on reopening (5.67s, 0 errors), then
two regenerations both printed unchanged. The first task's changed/unchanged
regeneration and dedicated index commit
`b0762ca069602b41c8ccc553de757ceed246a864` remain the index provenance.
No protected input or policy exclusion changed. A final gated
`scripts/test-live-lane-run.ps1 -PrintInputDigest` printed
`2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`.
Policy hash remains
`c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`;
LegacyExclusionDigest remains
`6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`.
The native policy witness and required policy guards validate those unchanged
inputs; no re-pin is necessary. The longest recorded test driver remains
461.878s, and the reopened cold build was 554s; neither exceeded ten minutes.

Evidence-diff through `7673df1a6d3f879643c3576edfa89a77e6a05270` reported
`commits=24 entries=0 violations=0`. Fetching origin/master then read-only
merge-tree succeeded without conflicts, tree
`c02bc44da17e002dbd2ab9492962dd5f842c05b7`. The final report-only amendment
gets another evidence/merge-tree check at settlement. No live host, qualification
run, deployment/restart, rc/master branch or production Docker volume was touched.
All drivers/owned children were joined. Mutations remain explicitly waived.

Exact missing execution is C994 first-piece repair plus its two-method green
union, the eight corpus methods below, and the 47 remaining retirement
pieces below. These selectors exclude non-test source helpers. Retain all current
passed receipts; do not repeat them solely to aggregate. Use a fresh evidence path
per attempt. Build once through the host slot in native output if this worktree's
compiled provenance cannot be retained, then run each literal filter serially as
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1955-<piece> -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '<filter>' --timeout 9m --report-trx --report-trx-filename result.trx --results-directory <fresh-evidence-path>`.
C994 first piece is `/*/*/RetiredTempContainerDockerTests/C994_Real_retired_temp_lifecycle`;
second is `/*/*/RetiredTempContainerDockerTests/C994_Real_retired_temp_lifecycle_part_2`.

```text
/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1901
/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1902
/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1903
/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1904
/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1901_second_half
/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1902_second_half
/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1903_second_half
/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1904_second_half
/*/*/RollingVolumeRecycleScriptTests/(C1374_Wrapper_census_recovers_transient_reads)|(C1087_Read_failures_name_their_cause)
/*/*/RollingVolumeRecycleScriptTests/(C1087_Project_resolves_by_repository_identity)|(C1693_Retire_temp_census_ignores_standing_runner_churn_between_passes)
/*/*/RollingVolumeRecycleScriptTests/(C1087_Census_filters_open_work_and_pending_lands)|(C1087_Check_census_phase_is_read_only)
/*/*/RollingVolumeRecycleScriptTests/(C1008_Present_or_unknown_temp_keeps_null_refusal)|(C1008_Busy_routed_and_land_in_flight_refuse)
/*/*/RollingVolumeRecycleScriptTests/(C1353_Wrapper_waits_for_a_bound_land_then_proceeds)|(C1008_Same_sha_and_partial_retries_are_safe)
/*/*/RollingVolumeRecycleScriptTests/(C1008_Option_manifest_is_strict)|(C1008_Wrapper_option_manifest_is_strict)
/*/*/RollingVolumeRecycleScriptTests/(C1008_Legacy_rolling_and_jq_rosters_remain_present)|(C1008_Legacy_rolling_and_jq_rosters_remain_absent)
/*/*/RollingVolumeRecycleScriptTests/(C1008_Legacy_rolling_and_jq_rosters_remain_missing_shell)|(C1008_Legacy_rolling_and_jq_rosters_remain_failing_shell)
/*/*/RollingVolumeRecycleScriptTests/(C1008_Documentation_and_transport_pins_match)|(C1008_Transport_scripts_are_ascii)
/*/*/RollingVolumeRecycleScriptTests/(C1008_Refusal_receipts_do_not_leak_secrets)|(C1008_Wrapper_refusal_receipts_do_not_leak_secrets)
/*/*/RollingVolumeRecycleScriptTests/(C1008_Retired_absent_null_is_accepted)|(C1008_Wrapper_retired_absent_null_is_accepted)
/*/*/RollingVolumeRecycleScriptTests/(C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt)|(C1594_Cross_sha_cleanup_image_binds_the_retired_temp)
/*/*/RollingVolumeRecycleScriptTests/(C1594_Older_retirement_receipt_is_ignored)|(C1594_Disagreeing_cross_sha_images_refuse)
/*/*/RollingVolumeRecycleScriptTests/(C1008_Preview_helper_removal_is_owned)
/*/*/RollingVolumeRecycleScriptTests/(C1317_Census_tolerates_a_task_that_settles_without_a_land)|(C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1)
/*/*/RollingVolumeRecycleScriptTests/(C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1)|(C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance)
/*/*/RollingVolumeRecycleScriptTests/(C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands)|(C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail)
/*/*/RollingVolumeRecycleScriptTests/(C1361_Wrapper_census_tolerates_open_status_changes_between_passes)|(C1697_Wrapper_standing_census_compares_only_rows_bound_to_server2)
/*/*/RollingVolumeRecycleScriptTests/(C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts)|(C1371_Post_removal_count_drift_does_not_abort)
/*/*/RollingVolumeRecycleScriptTests/(C1008_Host_proof_admits_only_an_authenticated_private_origin)|(C1008_Host_proof_file_omits_credentials)
/*/*/RollingVolumeRecycleCensusChurnTests/(C1099_Fixture_census_skips_a_vanished_foreign_container)
/*/*/RetiredTempContainerHostTests/(C994_Exited_owned_containers_are_removed_without_volumes)
/*/*/RetiredTempContainerHostTests/(C994_Absent_cleanup_is_idempotent)|(C994_Older_retirement_receipt_does_not_block_cleanup)
/*/*/RetiredTempContainerHostTests/(C994_Host_retirement_proof_is_typed)|(C994_Host_main_admission_is_required)
/*/*/RetiredTempContainerHostTests/(C994_Host_task_and_land_census_is_complete)|(C994_Census_and_inspect_fail_closed)
/*/*/RetiredTempContainerHostTests/(C994_Container_service_and_state_are_restricted)|(C994_Production_mount_topology_is_proven)
/*/*/RetiredTempContainerHostTests/(C994_Whole_candidate_set_precedes_removal)|(C994_Rollout_lock_covers_final_census)
/*/*/RetiredTempContainerHostTests/(C994_Each_removal_rechecks_live_proofs)|(C994_Final_absence_is_observed)
/*/*/RetiredTempContainerHostTests/(C994_Removal_failure_and_crash_retry_preserve_progress)|(C994_Receipt_write_and_copy_fail_closed)
/*/*/RetiredTempContainerHostTests/(C994_Host_context_is_strict)|(C994_Host_preview_never_mutates)
/*/*/RetiredTempContainerHostTests/(C1609_Cleanup_does_not_require_historical_image)|(C1609_Audit_uses_current_image)
/*/*/RetiredTempContainerHostTests/(C994_Cleanup_receipts_redact_unapproved_fields)|(C994_Fixture_custody_is_bounded)
/*/*/RetiredTempContainerHostTests/(C1105_Temp_cleanup_uses_previous_generation)
/*/*/RetiredTempContainerScriptTests/(C994_Drain_waits_for_exit_and_removes_containers)|(C994_Drain_uses_one_deadline)|(C994_Retire_recovers_exited_and_absent_temp)|(C994_Wrapper_retirement_proof_is_typed)|(C994_Wrapper_main_admission_is_required)|(C994_Wrapper_task_and_land_census_is_complete)|(C994_Strict_volume_admission_remains_independent)|(C994_Host_strict_volume_admission_remains_independent)|(C994_Preview_reports_pending_volume_proof)|(C994_Resume_never_runs_container_cleanup)|(C994_Host_resume_never_runs_container_cleanup)|(C994_Cleanup_receipt_is_required_before_recycle)|(C994_Verifier_manifest_is_strict)|(C994_Bridge_transport_preserves_literal_context)|(C994_Host_bridge_transport_preserves_literal_context)|(C994_Separate_invocations_preserve_audit_identity)
/*/*/RetireTempRunnerTimestampTests/(Bridge_exports_a_parsed_manifest_timestamp_as_iso_8601_utc)|(Bridge_still_rejects_a_non_timestamp_before_any_remote_call)
/*/*/C1008PlatformContractTests/(C1050_Guard_matches_native_platform)|(C1050_Remote_entries_guard_before_work)|(C1050_Rolling_entries_guard_before_work)|(C1050_Windows_remote_outcomes_are_exact)|(C1050_Windows_rolling_outcomes_are_exact)|(C1050_Audit_rejects_empty_or_incomplete_outcomes)
/*/*/C1008HostFixtureSetupInsertionTests/(One_column_zero_trap_gets_the_setup_once_and_indented_traps_none)|(No_column_zero_trap_throws_naming_zero)|(Two_column_zero_traps_throw_naming_two)
/*/*/RemoteScriptContractTests/(C1374_Host_census_recovers_transient_reads)|(C1361_Host_census_tolerates_open_status_changes_between_list_and_detail)|(C1361_Host_census_tolerates_open_status_changes_between_passes)|(C1697_Host_standing_census_compares_only_rows_bound_to_server2)|(C1066_Fixture_tree_fault_reaches_validator_under_nounset)|(C1066_Fixture_npm_install_uses_argument_log_under_nounset)|(C1066_Fixture_warm_probe_uses_argument_log_under_nounset)|(C1189_Fixture_package_zip_does_not_call_host_pwsh)|(C1189_Fixture_npm_integrity_does_not_call_host_pwsh)|(C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit)|(C1191_Lock_probe_is_created_before_uid_handoff)|(C1191_Fixture_exit_removes_owned_workspace_without_host_sudo)|(C1192_Fixture_has_no_host_payload_tree_or_payload_bind)|(C1192_Finalization_precedes_success_and_preserves_primary_failure)|(C1192_Evidence_export_preserves_fixture_roster_without_payloads)|(C1192_Cleanup_consumer_requires_positive_absence)|(C1192_Cleanup_helper_requires_positive_absence)|(C1192_Cleanup_masked_requires_positive_absence)|(C1192_Cleanup_work_requires_positive_absence)|(C1192_Cleanup_mask_requires_positive_absence)|(C1192_Cleanup_cache_requires_positive_absence)|(C1192_Cleanup_network_requires_positive_absence)|(C1192_Daemon_loss_after_compose_refuses_success)|(C1192_Acquisition_unknown_ownership_retains_registration_and_residue)|(C1207_Compose_teardown_requires_recorded_positive_absence)|(C1208_Foreign_cache_is_dropped_and_untouched)|(C1208_Legacy_container_replacement_is_untouched)|(C1208_Legacy_network_replacement_is_untouched)|(C1208_Unacquired_roster_is_untouched)|(C1208_Acquired_objects_are_removed)|(C1208_Recorded_container_id_does_not_delete_replacement)|(C1208_Legacy_owned_markers_are_migrated)|(C1208_Unknown_identity_retains_residue)|(C1208_Wrong_cidfile_preserves_foreign_container)|(C1208_Recorded_id_with_changed_labels_is_untouched)|(C1208_Recorded_container_inspect_failure_refuses_cleanup)|(C1208_Wrong_helper_id_preserves_foreign_container)|(C1208_Wrong_network_id_is_untouched)|(C1208_Recorded_network_inspect_failure_refuses_cleanup)|(C1208_Owned_packages_registration_survives_production_controls)|(C1208_Dangling_network_record_retains_residue)|(C1208_Invalid_network_record_retains_text)|(C1208_Confirmed_absent_network_record_is_cleared)|(C1208_Recorded_creation_fingerprint_is_reverified)|(C1208_Fingerprint_acquisition_refuses_unowned_paths)|(C1208_Metadata_writers_refuse_file_type_changes)|(C1208_Update_publication_replaces_link_without_writing_victim)|(C1208_Create_publication_refuses_link_without_writing_victim)|(C1208_Model_inspection_reads_live_fields_without_external_reader_processes)|(C1208_Deadline_fixture_keeps_required_logs_and_omits_unrelated_setup)|(C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt)|(C1192_Workspace_registration_is_absent_only_and_survives_prepare)|(C1192_Cleanup_releases_only_registered_current_run_resources)|(C1192_Partial_workspace_acquisition_is_cleaned)|(C1192_Toxic_probe_stays_in_work_volume)|(C1192_Summary_validator_runs_under_the_production_interpreter)|(C1192_Foreign_volume_is_refused_after_create)|(C1192_Harness_cleanup_stays_inside_its_private_directory)|(C1192_Harness_cleanup_refuses_ancestor_swap)|(C1192_Helper_argv_keeps_discarded_env_file_visible)|(C1192_Race_programs_and_feed_share_the_work_volume)|(C1192_Race_barriers_observe_volume_state)|(C849_Delayed_slot_grant_still_starts_both_restores)|(C849_Build_slot_exit_is_FixtureBuildSlotTimeout)|(C849_Refused_fixture_releases_probes_without_waiting)|(C849_Refused_launch_keeps_a_foreign_container_and_signals_release_proven_cids)|(C849_Signal_during_exit_finishes_custody_cleanup)|(C849_Group_signal_contract_requires_the_handler_to_exit)|(C849_Driver_deadline_witness_does_not_require_mount_namespaces)|(C849_Late_container_id_is_released_and_its_lease_deleted)|(C1192_Lock_probe_distinguishes_shared_and_private_scratch)|(C1192_Npm_seed_install_and_refill_use_work_volume)|(C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed)|(C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed)|(C1192_Real_cache_cases_keep_the_host_payload_backend)|(C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy)|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume)|(C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest)|(C1193_Lock_probe_passes_home_and_nuget_scratch)|(C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal)|(C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files)|(C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution)|(C1194_Fixture_budget_scale_refuses_empty_observation)|(C1192_Apphost_mask_is_an_empty_exact_volume_mount)|(C1195_Fixture_import_chmod_restores_volume_root)|(C1195_Seed_import_chmod_restores_volume_root)|(C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content)|(C1197_Post_removal_compare_accepts_temp_churn_and_refuses_bound_work)|(C913_Seed_accepts_complete_ordinary_packages_without_framework_packs)|(C913_Full_marker_binds_verified_recovery_before_publication)|(C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct)|(C913_Prune_validates_recovery_without_refilling_image_packs)|(C913_Fixture_uses_owned_payloads_and_proven_absent_temp)|(C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims)|(C1087_Host_census_filters_and_names_cause)|(C1008_Recycle_exact_default_volumes)|(C1008_Recycle_refuses_references_and_unknown_census)|(C1008_Recycle_audits_work_as_1654)|(C1008_Recycle_refuses_unpublished_and_dirty_work)|(C1008_Recycle_refuses_uninspectable_git)
/*/*/RemoteScriptContractTests/(C1008_Recycle_reaps_dirty_scratch)|(C1008_Recycle_audits_promisor_checkout)|(C1105_Git_audit_recovery_tips)|(C1105_Git_audit_seed_content)|(C1105_Git_audit_index_content)|(C1105_Git_audit_object_completeness)|(C1105_Git_audit_overall_timeout)|(C1316_Recycle_audit_cap_clears_the_standing_volume)|(C1105_Git_audit_volume_scale)|(C1105_Git_audit_resume_preserves_proof)|(C1105_Resume_extraction_is_closed)|(C1105_Git_audit_hidden_locations)|(C1335_Mirror_owner_stamp_in_linked_admin)|(C1105_Jq_argv_forms_are_absent)|(C1105_Jq_file_forms_are_present)|(C1105_Jq_argv_limit)|(C1008_Recycle_preserves_tmp_copyup)|(C1105_Verify_tmp_accepts_an_absent_pty_host)|(C1008_Recycle_resume_requires_matching_receipt)|(C1008_Recycle_resume_requires_matching_receipt_drift)|(C1008_Recycle_resume_requires_matching_receipt_recreated)|(C1008_Recycle_receipt_records_disk_and_partial_failure)|(C1008_Retire_temp_rechecks_absence_and_retirement)|(C1008_Retire_temp_reclaims_below_cache_disk_gate)|(C1008_Recycle_dry_run_never_mutates)|(C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes)|(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind)|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout)|(C1105_V218_dry_run_reaches_the_generation_line)|(C1105_Relative_dry_run_refuses_before_removal)|(C1105_Generation_identity_refuses_mismatch)|(C1105_Main_state_init_without_session_runner_refuses)|(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle)|(C1105_Compose_cmp_refuses_before_removal)|(C1105_Resume_identity_refusals)|(C1105_Resume_state_init_image_follows_the_journal)|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch)|(C1105_Resume_digest_pins_previous_sha12)|(C1105_Resume_post_removal_census_tracks_open_work)|(C944_All_cache_loop_variables_are_local)|(C951_Cache_paths_refuse_symlinked_mountpoints_and_refusing_sudo)|(C976_Missing_helper_image_preserves_final_receipt_diagnosis)|(C946_Rolling_harness_cleans_only_its_owned_success_root)|(C957_Scratch_mutation_controls_go_red_and_restore)|(C946_Green_harness_removes_root_and_failed_keep_run_retains_evidence)|(C980_Repo_root_assignment_preserves_linux_bytes)|(C980_Repo_root_assignment_converts_windows_paths)|(C980_Linux_shell_rejects_unconverted_repo_root)|(C980_Repo_root_conversion_failure_stops_before_body)|(C983_Required_jq_refuses_unavailable_probe_before_any_group)|(C983_Required_jq_driver_rejects_successful_false_probe)|(C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block)|(C905_Linux_shell_finds_pwsh_when_installed)|(C849_front_door_passes_every_full_case_name_to_the_invoker)|(Nested_lane_never_uses_sudo_or_python)|(C1067_Nested_lane_sudo_guard_is_sensitive)|(Testing_payload_expects_linux_backend_never_windows)|(Custody_containment_case_measures_the_v1_path_as_the_app_uid)|(Custody_residue_counts_subdirectories_only)|(Custody_containment_is_routed_to_the_remote_and_not_left_pending)|(Linux_custody_case_requires_the_linux_backend)|(Scrub_covers_github_token_prefixes)|(Smoke_deletes_its_branch)|(Child_project_is_run_scoped_and_distinct)|(Host_compose_uses_the_deployed_tag_not_this_runs_sha)|(Retirement_is_anchored_on_the_run_scoped_prefix)|(Host_daemon_is_never_pruned)|(Lane_detection_separates_a_bare_host_from_the_runner)|(Every_case_declares_a_lane)|(Deploy_parent_probes_the_phone_home_secret_as_the_app_uid)|(Deploy_parent_phone_home_grep_matches_the_runners_registration_failure_template)|(Deploy_parent_refuses_a_runner_whose_phone_home_keeps_failing)|(Deploy_parent_retires_its_own_superseded_images)|(Temp_deploy_uses_its_own_project_and_the_inspected_grok_store)|(Temp_retire_requires_the_retired_receipt_and_downs_only_temp_volumes)|(Deploy_parent_creates_the_git_identity_file_without_overwriting)|(Deploy_parent_seeds_or_verifies_runner_checkout)|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner)|(Deploy_parent_refuses_a_git_identity_symlink_and_leaves_its_target_mode)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it)|(Deploy_parent_creates_the_github_token_directory_without_reading_it)|(Deploy_parent_codex_home_refuses_a_symlink_or_file_and_keeps_contents)|(Host_lane_ownership_reset_never_enters_the_codex_home)|(C849_Cache_cases_use_only_the_validated_host_lane)|(C849_Cache_prepare_is_idempotent_and_preserves_payloads)|(C849_Cache_prepare_refuses_foreign_or_unsafe_roots)|(C849_Prune_refuses_stale_or_busy_authority)|(C849_Closure_preflight_names_a_missing_dependency)|(C849_Seed_publishes_complete_payloads_before_its_marker)|(C849_Seed_refuses_invalid_donors_and_partial_payloads)|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container)|(C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters)|(C849_Saved_donor_rejects_declared_size_bomb_before_writing)|(C849_Retired_guard_refuses_docker_ps_errors)|(C0995_Deploy_temp_checks_host_absence_before_checkout)
/*/*/RemoteScriptContractTests/(C0995_Cold_ready_seed_refuses_a_temp_container)|(C849_Cold_marker_reuse_writes_gate_receipts)|(C849_Deploy_temp_names_saved_import_when_seed_is_missing)|(C849_Deploy_ordering_contract_is_pinned)|(C849_Deploy_prepares_and_verifies_before_acceptance)|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo)|(C849_Docker_volume_paths_have_no_bare_host_reads)|(C849_Prune_preview_is_read_only_and_bounded)|(C997_Cache_budget_gate_uses_controlled_disk_readings)|(C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance)|(C997_Saved_donor_probe_failure_refuses_before_copy)|(C997_Saved_donor_default_probe_remains_real)|(C849_Prune_and_rollback_retain_roots_and_recovery)|(C849_Cache_receipts_exclude_credentials_and_payloads)|(C912_Cold_seed_with_unrelated_bind_initializes_only_three_labelled_roots)|(C912_Cold_seed_refuses_when_created_volume_disappears_before_init)|(C912_Cold_volumes_seed_accepts_busy_main_without_packages)|(C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets)|(C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries)|(C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit)|(C912_Cold_marker_has_distinct_validation_and_full_context_refusal)|(C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads)|(C912_Cold_exception_never_weakens_maintenance_or_donor_gates)|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers)|(C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune)|(C973_Prune_and_saved_donor_still_require_full_after_seed_image_prune)|(C1168_State_init_tolerates_a_file_deleted_mid_walk)|(C1168_State_init_refuses_a_persistent_chown_error)|(C1168_State_init_owns_a_dangling_symlink)|(C1331_State_init_chowns_a_populated_tree_in_find_batches)|(C1168_Rollout_docs_keep_redeploy_while_temp_holds_the_shared_store)
/*/*/C0824BranchDeletionRecycleAuditTests/(C0824_legacy_mirror_blocks_runner_refs_and_origin_kept)|(C0824_after_runner_refs_and_origin_delete_audit_passes)
/*/*/C0824BranchDeletionRecycleAuditTests/(C0824_origin_delete_without_runner_refs_audit_refuses)
/*/*/C1364BoundLandWaitProofTests/(C1364_Wrapper_redeploy_reports_waited)|(C1364_Wrapper_cleanup_reports_waited)
/*/*/C1364BoundLandWaitProofTests/(C1364_Wrapper_retire_volumes_reports_waited)|(C1364_Host_recycle_admission_reports_waited)
/*/*/C1364BoundLandWaitProofTests/(C1364_Host_recycle_post_removal_reports_waited)|(C1364_Host_recycle_post_removal_reports_waited_resume)
/*/*/C1364BoundLandWaitProofTests/(C1364_Host_recycle_post_removal_reports_waited_main_volumes)|(C1364_Host_recycle_post_removal_reports_waited_temp_volumes)
/*/*/C1364BoundLandWaitProofTests/(C1364_Host_cleanup_reports_waited)
```

A further bounded continuation is needed for that exact execution and the current
C994 fixture/admission failure (request +60 minutes; no technical approval to
weaken admission is requested). Full ordinary Review by another company remains
mandatory after the gate is complete, with no Interim and no land/activation.
Until then retain the D-2 incomplete gate; this is not Clean/Full.

## Final bounded continuation: inherited attribution and Review handoff

The caller granted a further +60 minutes at 04:48 UTC, with priority on the bare-base
C994 comparison, then all eight graph methods and C994 part 2, then the remaining
retirement selections. **No production, fixture, test, policy or index source was
changed in this continuation.** The only new tracked amendment is this report.
Execution-helper metadata changes affected selection order and bounded stopping,
not test bodies or assertions. All rows used the existing gated native build at
`c32b596a451c36e89b8892e6bbe85034ca97f7da` and runtime source
`c5eedeeabf7e59a58a152dd14670eb51bdb80812`; the intervening commits are report-only.
No new build or redundant guard/green selection was needed.

### C994 first shard: confirmed inherited, not repaired here

The **same current shard harness**, SHA-256
`8d86c1dfbbf44dbf593c71dc6c37f283ec2f578e15156c478b1a81ce3cf81091`, was run with `--shard 1` against bare-base production
inputs from `52a90a3935f1f8ed4198200ce3a5e21ca8dab370`. The base was a complete
archive with a local detached provenance HEAD and shared read-only Git object
lookup; no assigned branch was switched or modified. Before execution the driver
byte-compared eleven production/protocol/vector inputs to their exact base blobs,
including the coordinator, all three modules, wrapper, bridge, both proof producers,
public reader and operator-token helper. No candidate production fix was overlaid.
The current selector harness is used because the original base harness has no shard
selector; this is not a claim that its original unsharded method was executed.

The candidate first shard at
`7673df1a6d3f879643c3576edfa89a77e6a05270` completed RD-1, RD-2 and RD-3, then
failed RD-4 with `WrapperFailed:deploy-temp:Rolling deploy stopped: TempRetirementJournalUnknown`.
The base replay completed the same three cases and failed **identically at RD-4**,
with the same diagnosis and checked `cleanup=absent`. It took 144.184s
inside the driver (slot 146s). This establishes inherited failure under the same
harness; it is not a failure of the new late-refusal/render behavior. Per the
caller's explicit instruction, name it and move on rather than expand this repair.
Base direct-native evidence is separate from TUnit totals (exit 1, 3 completed
subcases, 1 failure). Both canonical base and candidate receipts remain unedited.

Base receipt SHA-256:
`3a6e4d7cfe570e01b2c6f0aaf478ed4b9d7ec0660d6f66f29de5cffff35baf19`.
Candidate receipt SHA-256:
`e127580074933e21fa342ba9e401d06fd4eb369b73bc629bd102f7c19a35ce8c`.
Base input hashes and timing are retained in `.antiphon/c1955/base-c994-shard-1.json`;
raw output is `base-c994-shard-1.log` and the retained receipt is
`native-receipts/Base-C994-shard-1.json`.

### Priority greens and preserved timeout

All eight exact `Origin_graph_corpus_seed_190[1-4]` methods and their
`_second_half` counterparts passed individually: **8 tests / 280 graph shapes**.
Their gated wall times were 62.498, 53.002, 51.333, 52.599, 49.907, 38.376,
58.019 and 59.043s. This completes **33 passed** Publication tests, with no missing
method in that class. No graph assertion, seed, family or shape was omitted.

C994 part 2 first failed at RD-5 with `ExpectedSuccess:RD-5:`; it had no completed
subcase. Its Apply trace stopped after 89.816s before any removal, consistent with
the unchanged 90s bounded child deadline. The fixture catches that error, then reads
the prior successful preview's result, obscuring the timeout in its final assertion.
The end-of-row one-minute load sample was 25.59. This is a retained **candidate
transient timeout**, not falsely attributed as inherited just because base passed.

The same part-2 harness passed all six cases at bare base in 181.797s
(slot 183s); its RD-5 Apply trace was 35.205s with four removals. One unchanged
candidate check then passed **1 TUnit test / RD-5..RD-10, six native cases** in
188.282s (slot 187s; body/session 182.792s), with checked cleanup absent. The row's
exact gated wall time in the table below is authoritative. No source edit, retry
implementation, assertion loosening or deadline increase was made between red and
green. This isolated timing check preserves the earlier failure and distinguishes
a transient deadline event from a reproducible behavior regression. Performance
and lost-timeout diagnostics become a follow-up, not code in this task.

Retained part-2 receipt SHA-256 values:
- candidate timeout: `ed72f5dbfc379bff9f26fff55c5ba9f94fd46aeb667361c03be95a65703301ee`;
- bare-base green: `cc91d085f43211bf3e176d5af3a2a1832668e5d40d2417b6671aa700d754ed1c`;
- unchanged candidate green: `3eb7991c141141279d220fd9894dcb2a4f34860a46c48be4360be27c0436808c`.

### Current counts and per-piece receipts

Latest distinct TUnit outcomes, normalized for the two first-shard method renames,
are **363 passed, 1 failed, 3 skipped**. This combines validated recorded
source revisions rather than pretending to be one whole-suite run. The current
failure is the base-confirmed inherited C994 RD-4 failure. The skips are one
Windows admission audit and the two Windows-only C1008 platform-outcome checks.
This continuation's raw TUnit rows contain **60 passed, 1 failed, 2 skipped**;
its one failed row is the preserved C994 part-2 timeout subsequently followed by
the unchanged green. Bare-base direct-native replays are not added to TUnit totals.

All 34 rows below verified the same runtime source
`c5eedeeabf7e59a58a152dd14670eb51bdb80812`. The build/run properties stayed
UseAppHost=false with native default output. Each ID identifies its fresh TRX,
log and phase group under `/work/worktrees/ws-01/.antiphon/c1955/`.

| Piece ID | Passed | Failed | Skipped | Gated wall seconds |
|---|---:|---:|---:|---:|
| executed-VolumeRetirementPublicationTests-10-split2 | 1 | 0 | 0 | 62.498 |
| executed-VolumeRetirementPublicationTests-11-split1 | 1 | 0 | 0 | 53.002 |
| executed-VolumeRetirementPublicationTests-11-split2 | 1 | 0 | 0 | 51.333 |
| executed-VolumeRetirementPublicationTests-12-split1 | 1 | 0 | 0 | 52.599 |
| executed-VolumeRetirementPublicationTests-12-split2 | 1 | 0 | 0 | 49.907 |
| executed-VolumeRetirementPublicationTests-13-split1 | 1 | 0 | 0 | 38.376 |
| executed-VolumeRetirementPublicationTests-13-split2 | 1 | 0 | 0 | 58.019 |
| executed-VolumeRetirementPublicationTests-14-split1 | 1 | 0 | 0 | 59.043 |
| Native-C994-part-2 | 0 | 1 | 0 | 159.952 |
| Native-C994-part-2-timing-check | 1 | 0 | 0 | 188.282 |
| completed-RollingVolumeRecycleScriptTests-1 | 2 | 0 | 0 | 133.275 |
| completed-RollingVolumeRecycleScriptTests-2 | 3 | 0 | 0 | 67.514 |
| completed-RollingVolumeRecycleScriptTests-3 | 2 | 0 | 0 | 59.324 |
| completed-RollingVolumeRecycleScriptTests-4 | 2 | 0 | 0 | 163.878 |
| completed-RollingVolumeRecycleScriptTests-5 | 2 | 0 | 0 | 43.467 |
| completed-RollingVolumeRecycleScriptTests-6 | 2 | 0 | 0 | 42.736 |
| completed-RollingVolumeRecycleScriptTests-7 | 2 | 0 | 0 | 472.501 |
| completed-RollingVolumeRecycleScriptTests-8 | 2 | 0 | 0 | 398.083 |
| completed-RollingVolumeRecycleCensusChurnTests-1 | 1 | 0 | 0 | 11.680 |
| completed-RetiredTempContainerHostTests-1 | 1 | 0 | 0 | 48.952 |
| completed-RetiredTempContainerHostTests-2 | 2 | 0 | 0 | 37.925 |
| completed-RetiredTempContainerHostTests-3 | 2 | 0 | 0 | 19.153 |
| completed-RetiredTempContainerHostTests-4 | 2 | 0 | 0 | 45.635 |
| completed-RetiredTempContainerHostTests-5 | 2 | 0 | 0 | 97.339 |
| completed-RetiredTempContainerHostTests-6 | 2 | 0 | 0 | 32.199 |
| completed-RetiredTempContainerHostTests-7 | 2 | 0 | 0 | 122.380 |
| completed-RetiredTempContainerHostTests-8 | 2 | 0 | 0 | 139.489 |
| completed-RetiredTempContainerHostTests-9 | 2 | 0 | 0 | 55.447 |
| completed-RetireTempRunnerTimestampTests-1 | 4 | 0 | 0 | 18.643 |
| completed-C1008PlatformContractTests-1 | 4 | 0 | 2 | 11.382 |
| completed-C1008HostFixtureSetupInsertionTests-1 | 3 | 0 | 0 | 9.490 |
| completed-C0824BranchDeletionRecycleAuditTests-1 | 2 | 0 | 0 | 45.361 |
| completed-C0824BranchDeletionRecycleAuditTests-2 | 1 | 0 | 0 | 37.376 |
| completed-RetiredTempContainerHostTests-10 | 2 | 0 | 0 | 222.152 |

The legacy Rolling present/absent and missing/failing-shell pieces passed without
rerun: 2 tests each, 472.501s and 398.083s. Each mode preserves its complete
62/66-child roster (252 child invocations across the four modes). The current
maximum recorded test driver is 472.501s, below ten minutes.
The prior cold build was 554s. These heterogeneous one-shot rows are not a stable
per-test p95 sample; unrelated slow-fixture optimization is deferred.

The last test invocation began 05:45:22.108 UTC and finished about 05:49:04 UTC.
The explicit approved-start boundary was 05:48:10. The execution helper refused to
launch the next process after that boundary; its exit 76 is a **metadata stop**, not
a TUnit result, a zero-test green, or a build-slot timeout. Every active driver and
owned child was joined before this report. The final active piece passed both
C1609 image-independence/current-audit-image methods. Remaining fixture/runtime
phase costs not observed by the stored timings stay unknown.

| Required retirement class | Current passed | Current failed | Expected skips | Actual methods still unrun |
|---|---:|---:|---:|---:|
| VolumeRetirementApplyGateTests | 13 | 0 | 0 | 0 |
| VolumeRetirementContentTests | 41 | 0 | 0 | 0 |
| VolumeRetirementReceiptTests | 17 | 0 | 0 | 0 |
| VolumeRetirementHostProofTests | 14 | 0 | 0 | 0 |
| VolumeRetirementPublicationTests | 33 | 0 | 0 | 0 |
| VolumeRetirementWrapperTests | 7 | 0 | 0 | 0 |
| VolumeRetirementDockerTests | 4 | 0 | 0 | 0 |
| RollingVolumeRecycleDockerTests | 4 | 0 | 0 | 0 |
| RetiredTempContainerDockerTests | 1 | 1 | 0 | 0 |
| RollingVolumeRecycleScriptTests | 17 | 0 | 0 | 24 |
| RollingVolumeRecycleCensusChurnTests | 1 | 0 | 0 | 0 |
| RetiredTempContainerHostTests | 19 | 0 | 0 | 3 |
| RetiredTempContainerScriptTests | 0 | 0 | 0 | 16 |
| RetireTempRunnerTimestampTests | 4 | 0 | 0 | 0 |
| C1008PlatformContractTests | 4 | 0 | 2 | 0 |
| C1008HostFixtureSetupInsertionTests | 3 | 0 | 0 | 0 |
| RemoteScriptContractTests | 0 | 0 | 0 | 229 |
| C0824BranchDeletionRecycleAuditTests | 3 | 0 | 0 | 0 |
| C1364BoundLandWaitProofTests | 0 | 0 | 0 | 9 |

The guard union remains 114 passed / 1 expected skip; the original additional
selector class remains 50 passed. PTY/SessionRunner limiter and robustness counts
remain 2 and 12 passed respectively. No passing row was rerun just to aggregate.
The selection audit restored three actual theory methods accidentally omitted by
the old 600-character attribute scan: `C1594_Antiphon_evidence_gitdir` and
`C1105_Resume_post_removal_census_tracks_open_work_binding` / `_landing`.
They remain **unrun**, not mistaken for helpers or credited as coverage.

### Exact missing verification for Full ordinary Review

The source-method/TRX audit finds **37 unrun exact pieces / 281 actual test methods**.
It excludes helper names and unrelated optional closure support. Missing execution
is confined to:

- RollingVolumeRecycleScriptTests: 24 methods.
- RetiredTempContainerHostTests: 3 methods.
- RetiredTempContainerScriptTests: 16 methods.
- RemoteScriptContractTests: 229 methods.
- C1364BoundLandWaitProofTests: 9 methods.

Retain the incomplete gate. These are exact missing selections to execute once;
previous passing receipts stay credited. The three large-class Remote batches
from the older historical inventory were replaced by the actual 17 pieces below,
each at most 15 methods; the selected method union is unchanged. No row contract,
assertion, seed or refusal was removed. Each literal filter below is a separate
invocation through the host gate, in native output, with a fresh evidence path:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1955-<piece> -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '<filter>' --timeout 9m --report-trx --report-trx-filename result.trx --results-directory <fresh-evidence-path>`

```text
completed-RollingVolumeRecycleScriptTests-9
/*/*/RollingVolumeRecycleScriptTests/(C1008_Documentation_and_transport_pins_match)|(C1008_Transport_scripts_are_ascii)

completed-RollingVolumeRecycleScriptTests-10
/*/*/RollingVolumeRecycleScriptTests/(C1008_Refusal_receipts_do_not_leak_secrets)|(C1008_Wrapper_refusal_receipts_do_not_leak_secrets)

completed-RollingVolumeRecycleScriptTests-11
/*/*/RollingVolumeRecycleScriptTests/(C1008_Retired_absent_null_is_accepted)|(C1008_Wrapper_retired_absent_null_is_accepted)

completed-RollingVolumeRecycleScriptTests-12
/*/*/RollingVolumeRecycleScriptTests/(C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt)|(C1594_Cross_sha_cleanup_image_binds_the_retired_temp)

completed-RollingVolumeRecycleScriptTests-13
/*/*/RollingVolumeRecycleScriptTests/(C1594_Older_retirement_receipt_is_ignored)|(C1594_Disagreeing_cross_sha_images_refuse)

completed-RollingVolumeRecycleScriptTests-14
/*/*/RollingVolumeRecycleScriptTests/(C1594_Antiphon_evidence_gitdir)|(C1008_Preview_helper_removal_is_owned)

completed-RollingVolumeRecycleScriptTests-15
/*/*/RollingVolumeRecycleScriptTests/(C1317_Census_tolerates_a_task_that_settles_without_a_land)|(C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1)

completed-RollingVolumeRecycleScriptTests-16
/*/*/RollingVolumeRecycleScriptTests/(C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1)|(C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance)

completed-RollingVolumeRecycleScriptTests-17
/*/*/RollingVolumeRecycleScriptTests/(C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands)|(C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail)

completed-RollingVolumeRecycleScriptTests-18
/*/*/RollingVolumeRecycleScriptTests/(C1361_Wrapper_census_tolerates_open_status_changes_between_passes)|(C1697_Wrapper_standing_census_compares_only_rows_bound_to_server2)

completed-RollingVolumeRecycleScriptTests-19
/*/*/RollingVolumeRecycleScriptTests/(C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts)|(C1371_Post_removal_count_drift_does_not_abort)

completed-RollingVolumeRecycleScriptTests-20
/*/*/RollingVolumeRecycleScriptTests/(C1008_Host_proof_admits_only_an_authenticated_private_origin)|(C1008_Host_proof_file_omits_credentials)

completed-RetiredTempContainerHostTests-11
/*/*/RetiredTempContainerHostTests/(C994_Cleanup_receipts_redact_unapproved_fields)|(C994_Fixture_custody_is_bounded)

completed-RetiredTempContainerHostTests-12
/*/*/RetiredTempContainerHostTests/(C1105_Temp_cleanup_uses_previous_generation)

completed-RetiredTempContainerScriptTests-1
/*/*/RetiredTempContainerScriptTests/(C994_Drain_waits_for_exit_and_removes_containers)|(C994_Drain_uses_one_deadline)|(C994_Retire_recovers_exited_and_absent_temp)|(C994_Wrapper_retirement_proof_is_typed)|(C994_Wrapper_main_admission_is_required)|(C994_Wrapper_task_and_land_census_is_complete)|(C994_Strict_volume_admission_remains_independent)|(C994_Host_strict_volume_admission_remains_independent)|(C994_Preview_reports_pending_volume_proof)|(C994_Resume_never_runs_container_cleanup)|(C994_Host_resume_never_runs_container_cleanup)|(C994_Cleanup_receipt_is_required_before_recycle)|(C994_Verifier_manifest_is_strict)|(C994_Bridge_transport_preserves_literal_context)|(C994_Host_bridge_transport_preserves_literal_context)|(C994_Separate_invocations_preserve_audit_identity)

completed-RemoteScriptContractTests-1-piece-1
/*/*/RemoteScriptContractTests/(C1374_Host_census_recovers_transient_reads)|(C1361_Host_census_tolerates_open_status_changes_between_list_and_detail)|(C1361_Host_census_tolerates_open_status_changes_between_passes)|(C1697_Host_standing_census_compares_only_rows_bound_to_server2)|(C1066_Fixture_tree_fault_reaches_validator_under_nounset)|(C1066_Fixture_npm_install_uses_argument_log_under_nounset)|(C1066_Fixture_warm_probe_uses_argument_log_under_nounset)|(C1189_Fixture_package_zip_does_not_call_host_pwsh)|(C1189_Fixture_npm_integrity_does_not_call_host_pwsh)|(C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit)|(C1191_Lock_probe_is_created_before_uid_handoff)|(C1191_Fixture_exit_removes_owned_workspace_without_host_sudo)|(C1192_Fixture_has_no_host_payload_tree_or_payload_bind)|(C1192_Finalization_precedes_success_and_preserves_primary_failure)|(C1192_Evidence_export_preserves_fixture_roster_without_payloads)

completed-RemoteScriptContractTests-1-piece-2
/*/*/RemoteScriptContractTests/(C1192_Cleanup_consumer_requires_positive_absence)|(C1192_Cleanup_helper_requires_positive_absence)|(C1192_Cleanup_masked_requires_positive_absence)|(C1192_Cleanup_work_requires_positive_absence)|(C1192_Cleanup_mask_requires_positive_absence)|(C1192_Cleanup_cache_requires_positive_absence)|(C1192_Cleanup_network_requires_positive_absence)|(C1192_Daemon_loss_after_compose_refuses_success)|(C1192_Acquisition_unknown_ownership_retains_registration_and_residue)|(C1207_Compose_teardown_requires_recorded_positive_absence)|(C1208_Foreign_cache_is_dropped_and_untouched)|(C1208_Legacy_container_replacement_is_untouched)|(C1208_Legacy_network_replacement_is_untouched)|(C1208_Unacquired_roster_is_untouched)|(C1208_Acquired_objects_are_removed)

completed-RemoteScriptContractTests-1-piece-3
/*/*/RemoteScriptContractTests/(C1208_Recorded_container_id_does_not_delete_replacement)|(C1208_Legacy_owned_markers_are_migrated)|(C1208_Unknown_identity_retains_residue)|(C1208_Wrong_cidfile_preserves_foreign_container)|(C1208_Recorded_id_with_changed_labels_is_untouched)|(C1208_Recorded_container_inspect_failure_refuses_cleanup)|(C1208_Wrong_helper_id_preserves_foreign_container)|(C1208_Wrong_network_id_is_untouched)|(C1208_Recorded_network_inspect_failure_refuses_cleanup)|(C1208_Owned_packages_registration_survives_production_controls)|(C1208_Dangling_network_record_retains_residue)|(C1208_Invalid_network_record_retains_text)|(C1208_Confirmed_absent_network_record_is_cleared)|(C1208_Recorded_creation_fingerprint_is_reverified)|(C1208_Fingerprint_acquisition_refuses_unowned_paths)

completed-RemoteScriptContractTests-1-piece-4
/*/*/RemoteScriptContractTests/(C1208_Metadata_writers_refuse_file_type_changes)|(C1208_Update_publication_replaces_link_without_writing_victim)|(C1208_Create_publication_refuses_link_without_writing_victim)|(C1208_Model_inspection_reads_live_fields_without_external_reader_processes)|(C1208_Deadline_fixture_keeps_required_logs_and_omits_unrelated_setup)|(C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt)|(C1192_Workspace_registration_is_absent_only_and_survives_prepare)|(C1192_Cleanup_releases_only_registered_current_run_resources)|(C1192_Partial_workspace_acquisition_is_cleaned)|(C1192_Toxic_probe_stays_in_work_volume)|(C1192_Summary_validator_runs_under_the_production_interpreter)|(C1192_Foreign_volume_is_refused_after_create)|(C1192_Harness_cleanup_stays_inside_its_private_directory)|(C1192_Harness_cleanup_refuses_ancestor_swap)|(C1192_Helper_argv_keeps_discarded_env_file_visible)

completed-RemoteScriptContractTests-1-piece-5
/*/*/RemoteScriptContractTests/(C1192_Race_programs_and_feed_share_the_work_volume)|(C1192_Race_barriers_observe_volume_state)|(C849_Delayed_slot_grant_still_starts_both_restores)|(C849_Build_slot_exit_is_FixtureBuildSlotTimeout)|(C849_Refused_fixture_releases_probes_without_waiting)|(C849_Refused_launch_keeps_a_foreign_container_and_signals_release_proven_cids)|(C849_Signal_during_exit_finishes_custody_cleanup)|(C849_Group_signal_contract_requires_the_handler_to_exit)|(C849_Driver_deadline_witness_does_not_require_mount_namespaces)|(C849_Late_container_id_is_released_and_its_lease_deleted)|(C1192_Lock_probe_distinguishes_shared_and_private_scratch)|(C1192_Npm_seed_install_and_refill_use_work_volume)|(C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed)|(C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed)|(C1192_Real_cache_cases_keep_the_host_payload_backend)

completed-RemoteScriptContractTests-1-piece-6
/*/*/RemoteScriptContractTests/(C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy)|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume)|(C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest)|(C1193_Lock_probe_passes_home_and_nuget_scratch)|(C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal)|(C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files)|(C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution)|(C1194_Fixture_budget_scale_refuses_empty_observation)|(C1192_Apphost_mask_is_an_empty_exact_volume_mount)|(C1195_Fixture_import_chmod_restores_volume_root)|(C1195_Seed_import_chmod_restores_volume_root)|(C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content)|(C1197_Post_removal_compare_accepts_temp_churn_and_refuses_bound_work)|(C913_Seed_accepts_complete_ordinary_packages_without_framework_packs)|(C913_Full_marker_binds_verified_recovery_before_publication)

completed-RemoteScriptContractTests-1-piece-7
/*/*/RemoteScriptContractTests/(C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct)|(C913_Prune_validates_recovery_without_refilling_image_packs)|(C913_Fixture_uses_owned_payloads_and_proven_absent_temp)|(C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims)|(C1087_Host_census_filters_and_names_cause)|(C1008_Recycle_exact_default_volumes)|(C1008_Recycle_refuses_references_and_unknown_census)|(C1008_Recycle_audits_work_as_1654)|(C1008_Recycle_refuses_unpublished_and_dirty_work)|(C1008_Recycle_refuses_uninspectable_git)

completed-RemoteScriptContractTests-2-piece-1
/*/*/RemoteScriptContractTests/(C1008_Recycle_reaps_dirty_scratch)|(C1008_Recycle_audits_promisor_checkout)|(C1105_Git_audit_recovery_tips)|(C1105_Git_audit_seed_content)|(C1105_Git_audit_index_content)|(C1105_Git_audit_object_completeness)|(C1105_Git_audit_overall_timeout)|(C1316_Recycle_audit_cap_clears_the_standing_volume)|(C1105_Git_audit_volume_scale)|(C1105_Git_audit_resume_preserves_proof)|(C1105_Resume_extraction_is_closed)|(C1105_Git_audit_hidden_locations)|(C1335_Mirror_owner_stamp_in_linked_admin)|(C1105_Jq_argv_forms_are_absent)|(C1105_Jq_file_forms_are_present)

completed-RemoteScriptContractTests-2-piece-2
/*/*/RemoteScriptContractTests/(C1105_Jq_argv_limit)|(C1008_Recycle_preserves_tmp_copyup)|(C1105_Verify_tmp_accepts_an_absent_pty_host)|(C1008_Recycle_resume_requires_matching_receipt)|(C1008_Recycle_resume_requires_matching_receipt_drift)|(C1008_Recycle_resume_requires_matching_receipt_recreated)|(C1008_Recycle_receipt_records_disk_and_partial_failure)|(C1008_Retire_temp_rechecks_absence_and_retirement)|(C1008_Retire_temp_reclaims_below_cache_disk_gate)|(C1008_Recycle_dry_run_never_mutates)|(C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes)|(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind)|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout)|(C1105_V218_dry_run_reaches_the_generation_line)|(C1105_Relative_dry_run_refuses_before_removal)

completed-RemoteScriptContractTests-2-piece-3
/*/*/RemoteScriptContractTests/(C1105_Generation_identity_refuses_mismatch)|(C1105_Main_state_init_without_session_runner_refuses)|(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle)|(C1105_Compose_cmp_refuses_before_removal)|(C1105_Resume_identity_refusals)|(C1105_Resume_state_init_image_follows_the_journal)|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch)|(C1105_Resume_digest_pins_previous_sha12)|(C1105_Resume_post_removal_census_tracks_open_work)|(C1105_Resume_post_removal_census_tracks_open_work_binding)|(C1105_Resume_post_removal_census_tracks_open_work_landing)|(C944_All_cache_loop_variables_are_local)|(C951_Cache_paths_refuse_symlinked_mountpoints_and_refusing_sudo)|(C976_Missing_helper_image_preserves_final_receipt_diagnosis)|(C946_Rolling_harness_cleans_only_its_owned_success_root)

completed-RemoteScriptContractTests-2-piece-4
/*/*/RemoteScriptContractTests/(C957_Scratch_mutation_controls_go_red_and_restore)|(C946_Green_harness_removes_root_and_failed_keep_run_retains_evidence)|(C980_Repo_root_assignment_preserves_linux_bytes)|(C980_Repo_root_assignment_converts_windows_paths)|(C980_Linux_shell_rejects_unconverted_repo_root)|(C980_Repo_root_conversion_failure_stops_before_body)|(C983_Required_jq_refuses_unavailable_probe_before_any_group)|(C983_Required_jq_driver_rejects_successful_false_probe)|(C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block)|(C905_Linux_shell_finds_pwsh_when_installed)|(C849_front_door_passes_every_full_case_name_to_the_invoker)|(Nested_lane_never_uses_sudo_or_python)|(C1067_Nested_lane_sudo_guard_is_sensitive)|(Testing_payload_expects_linux_backend_never_windows)|(Custody_containment_case_measures_the_v1_path_as_the_app_uid)

completed-RemoteScriptContractTests-2-piece-5
/*/*/RemoteScriptContractTests/(Custody_residue_counts_subdirectories_only)|(Custody_containment_is_routed_to_the_remote_and_not_left_pending)|(Linux_custody_case_requires_the_linux_backend)|(Scrub_covers_github_token_prefixes)|(Smoke_deletes_its_branch)|(Child_project_is_run_scoped_and_distinct)|(Host_compose_uses_the_deployed_tag_not_this_runs_sha)|(Retirement_is_anchored_on_the_run_scoped_prefix)|(Host_daemon_is_never_pruned)|(Lane_detection_separates_a_bare_host_from_the_runner)|(Every_case_declares_a_lane)|(Deploy_parent_probes_the_phone_home_secret_as_the_app_uid)|(Deploy_parent_phone_home_grep_matches_the_runners_registration_failure_template)|(Deploy_parent_refuses_a_runner_whose_phone_home_keeps_failing)|(Deploy_parent_retires_its_own_superseded_images)

completed-RemoteScriptContractTests-2-piece-6
/*/*/RemoteScriptContractTests/(Temp_deploy_uses_its_own_project_and_the_inspected_grok_store)|(Temp_retire_requires_the_retired_receipt_and_downs_only_temp_volumes)|(Deploy_parent_creates_the_git_identity_file_without_overwriting)|(Deploy_parent_seeds_or_verifies_runner_checkout)|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner)|(Deploy_parent_refuses_a_git_identity_symlink_and_leaves_its_target_mode)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it)|(Deploy_parent_creates_the_github_token_directory_without_reading_it)|(Deploy_parent_codex_home_refuses_a_symlink_or_file_and_keeps_contents)|(Host_lane_ownership_reset_never_enters_the_codex_home)|(C849_Cache_cases_use_only_the_validated_host_lane)|(C849_Cache_prepare_is_idempotent_and_preserves_payloads)|(C849_Cache_prepare_refuses_foreign_or_unsafe_roots)|(C849_Prune_refuses_stale_or_busy_authority)

completed-RemoteScriptContractTests-2-piece-7
/*/*/RemoteScriptContractTests/(C849_Closure_preflight_names_a_missing_dependency)|(C849_Seed_publishes_complete_payloads_before_its_marker)|(C849_Seed_refuses_invalid_donors_and_partial_payloads)|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container)|(C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters)|(C849_Saved_donor_rejects_declared_size_bomb_before_writing)|(C849_Retired_guard_refuses_docker_ps_errors)|(C0995_Deploy_temp_checks_host_absence_before_checkout)

completed-RemoteScriptContractTests-3-piece-1
/*/*/RemoteScriptContractTests/(C0995_Cold_ready_seed_refuses_a_temp_container)|(C849_Cold_marker_reuse_writes_gate_receipts)|(C849_Deploy_temp_names_saved_import_when_seed_is_missing)|(C849_Deploy_ordering_contract_is_pinned)|(C849_Deploy_prepares_and_verifies_before_acceptance)|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo)|(C849_Docker_volume_paths_have_no_bare_host_reads)|(C849_Prune_preview_is_read_only_and_bounded)|(C997_Cache_budget_gate_uses_controlled_disk_readings)|(C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance)|(C997_Saved_donor_probe_failure_refuses_before_copy)|(C997_Saved_donor_default_probe_remains_real)|(C849_Prune_and_rollback_retain_roots_and_recovery)|(C849_Cache_receipts_exclude_credentials_and_payloads)|(C912_Cold_seed_with_unrelated_bind_initializes_only_three_labelled_roots)

completed-RemoteScriptContractTests-3-piece-2
/*/*/RemoteScriptContractTests/(C912_Cold_seed_refuses_when_created_volume_disappears_before_init)|(C912_Cold_volumes_seed_accepts_busy_main_without_packages)|(C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets)|(C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries)|(C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit)|(C912_Cold_marker_has_distinct_validation_and_full_context_refusal)|(C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads)|(C912_Cold_exception_never_weakens_maintenance_or_donor_gates)|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers)|(C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune)|(C973_Prune_and_saved_donor_still_require_full_after_seed_image_prune)|(C1168_State_init_tolerates_a_file_deleted_mid_walk)|(C1168_State_init_refuses_a_persistent_chown_error)|(C1168_State_init_owns_a_dangling_symlink)|(C1331_State_init_chowns_a_populated_tree_in_find_batches)

completed-RemoteScriptContractTests-3-piece-3
/*/*/RemoteScriptContractTests/(C1168_Rollout_docs_keep_redeploy_while_temp_holds_the_shared_store)

completed-C1364BoundLandWaitProofTests-1
/*/*/C1364BoundLandWaitProofTests/(C1364_Wrapper_redeploy_reports_waited)|(C1364_Wrapper_cleanup_reports_waited)

completed-C1364BoundLandWaitProofTests-2
/*/*/C1364BoundLandWaitProofTests/(C1364_Wrapper_retire_volumes_reports_waited)|(C1364_Host_recycle_admission_reports_waited)

completed-C1364BoundLandWaitProofTests-3
/*/*/C1364BoundLandWaitProofTests/(C1364_Host_recycle_post_removal_reports_waited)|(C1364_Host_recycle_post_removal_reports_waited_resume)

completed-C1364BoundLandWaitProofTests-4
/*/*/C1364BoundLandWaitProofTests/(C1364_Host_recycle_post_removal_reports_waited_main_volumes)|(C1364_Host_recycle_post_removal_reports_waited_temp_volumes)

completed-C1364BoundLandWaitProofTests-5
/*/*/C1364BoundLandWaitProofTests/(C1364_Host_cleanup_reports_waited)
```

### Follow-up card descriptions; no further repair in this task

**C994: completed retirement refuses the next deploy admission at RD-4.**
Reproduce with the exact first native shard and current harness against unmodified
base `52a90a3935f1f8ed4198200ce3a5e21ca8dab370`: RD-1..3 complete, RD-4
`deploy-temp` refuses `TempRetirementJournalUnknown`. It is identical on this
candidate. Investigate the public clearable-census/journal admission without
fabricating an empty census or weakening custody/owner/effect checks. Acceptance:
completed retirement permits the next deploy; unresolved journals still refuse;
retain original ten-case lifecycle roster and checked cleanup. Evidence and both
source pins are above. This inherited defect is separate from the late-fence fix.

**C994: preserve bounded-child timeout cause instead of reading stale preview success.**
`host()` catches a bounded-child error and reads the previous preview's accepted
result, yielding blank-diagnosis `ExpectedSuccess:RD-5:`. Candidate timeout trace
89.816s / zero removals, base green 35.205s / four removals, and unchanged candidate
green show the need for useful timeout evidence under host load. Acceptance: a
controlled child timeout reports its exact stage/deadline and joined cleanup,
cannot consume a stale preview result, and retains all current deadlines and
assertions. This diagnostic/performance debt is not repaired by retries here.

**C1008: reduce repeated setup in the four legacy jq/rolling roster modes.**
The current 252-child matrix consumes 472.501s + 398.083s across two gated pieces.
Debug shared immutable setup and child-start costs separately while preserving all
31 assertions per mode, native shell outcome coverage, process safety and the
per-invocation cap. Budget timeouts from measured per-mode phases/p95 rather than
raising them. This is deferred test-performance work, not an excuse to skip a
retirement guard or broaden this repair.

### Settlement invariants and next stage

The existing red/green witnesses, N3 stage timings and script-only activation owner
procedure above remain current. No new TestTiming span or selector-policy row was
added. Derived-index regeneration remains changed/unchanged in its own original
commit and unchanged/unchanged on reopening after building its driver first.
Protected input, policy and legacy-exclusion pins remain unchanged; the latest
same-runtime gated digest and guard receipts above remain valid. No re-pin, extra
build or repeated broad test sweep is justified by this report-only amendment.

All generated TRX/log/JSON/receipts stay ignored. No live server2/desktop host,
qualification run, production volume, deployment, restart, rc or master branch was
mutated. Standing authority already commissioned the work; no further permission
or budget request is pending. The caller explicitly directs **next: review**, Full
ordinary Review by another company, no Interim and no land. The missing list and
inherited failure remain visible; the handoff is not a Clean/Full assertion.
