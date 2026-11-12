# CARD-1820: Selector v2 accuracy on real diffs, smallest path

Date: 2026-11-12 UTC. Investigator task: `62e3aea4-687b-4ea9-86a6-52b35f585017`.

**Confirmed: the landed selector is useful for finding tests, but absence from its output does not establish that a release chunk is unchanged.** Real-diff replays omit classes with demonstrable dependencies. The mechanisms are a deliberate 25-class reverse-edge cap, an exact-string/syntactic type index with limited traversal, and missing script/fixture dependency closure. Completing the remaining original CARD-1820 slices alone has no established proof of release-skipping safety.

This is an evidence report, not a replacement implementation plan. No product, test, configuration, policy, or existing plan was changed. No live run, host, session, or volume was touched. No build, test suite, or mutation control was run. The operator's mutation waiver was respected. All selector processes completed with exit 0; their counts below are **selection counts, not executed tests**.

## Scope and provenance

Pinned source versions:

| Name | Full SHA |
|---|---|
| Previous cut / run ae84478d | `3db1a43b02246706dc28075fce7e7b5e84804ddb` |
| Cut / run 0b292fa4 named by the brief | `56e96eabf698e9c1fcd462836ad32ed1fb259e83` |
| Assigned HEAD and fetched origin/master | `cc48c70050a2932eddf4abe18cea8659d478c33e` |

Reads: `scripts/card.ps1 get/history CARD-1820 -Board Antiphon`; CARD-1836 get; `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703`; individual task GETs for all eleven earlier CARD-1820 tasks; the named plans, selector, generator, builder, tests and roster-related files. The board response contained 8,162 tasks and twelve CARD-1820 rows including this investigation. This was a board-scoped read, not an inference from a capped search. CARD-1820 was **InProgress**, revision 21, when read; the brief's Review-column description is historical. Revision 21 records this Investigate dispatch; revision 18 records the earlier replay Code settlement.

Git census: 6,289 cached origin refs; `git log --all --grep` for CARD-1820/selector-v2 variants found 102 commits, 77 without literal ancestry on origin/master. Sixteen origin branches contain those non-ancestor commits. The branch table below records all sixteen and their remote tips, confirmed by `git ls-remote origin` at investigation time. Most are old replay/review histories; non-ancestry is not proof of unlanded functionality. No commit anywhere in the inspected Git history touched the planned keyword, roster-registry, grandfather-list, or WhatRanReceiptContractTests paths.

Generated local evidence is ignored under `.antiphon/selector-evidence/`. The essential command results, lists, SHAs and limitations are preserved here so this report does not depend on worktree retention. Source citations without another SHA refer to `cc48c70050a2932eddf4abe18cea8659d478c33e`. Historical review citations name their branch and file explicitly.

## What exists, by slice

| Slice / requirement | Observed state | Commit and evidence |
|---|---|---|
| Original five-slice plan | Landed | `f01a030699fdab407ac1c6cee547a74233f29d5b`, `docs/superpowers/plans/2026-11-09-selector-v2-derived-index.md` |
| S1 declaration union and derived index | Landed | Core selector `ceccdaad01225a83fde0661972894f9620057163`; cap/hand-subset repair `f77a407c72cba8e544ee8a938455b35677b8171d`; declaration-based platform classification `af1c08c73807cf57760f81570c65e5aacac88309`; override edge `4dc4ce606abfa6fe9f12f7443f44da699d8c17c7` |
| S1 clean source review | Reviewed-clean historical source; functionality subsequently replayed and landed | Review task `dfefd8e0-ee3d-4095-960b-da6237834b44`, evidence `d6d761f7-4a3c-4c30-9415-843329646b76`, subject `0ddd898d-0458-4810-b936-e1210feabe82`, reviewed source `cb931f0890126ee1327eb2fc9fe29103e3667fe4`, outcome Clean, reviewedSourceClean=true. Full report on `origin/feat/card-task-dfefd8e0:.antiphon/task-dfefd8e0.md:1` |
| S1 replay/index refresh | Landed with new commit IDs | Source `66428403011a078edd4257dbf5fd1f4be8fe7443` became `750cb3c79c125ca8dc88c3c5543144bf70891635`; combined-tree refresh `584a169521a763d682da8313a9bc84f3c24583fb`. Publication mapping is recorded in `origin/feat/card-task-fc265931:.antiphon/task-fc265931.md:1` |
| Regeneration owner step, CARD-1838 | Landed | `4f111d1ed0c616b377057e77d23fceca2aa06711`; `scripts/regenerate-derived-impact-index.ps1:1`, `docs/orchestration-loop.md:736` |
| Regenerator solution-build availability | Landed after native failure | Repair `b5d8930f9d8ba5345144b41f308d344efcf4c2cc`, witness `fcb5831463b755c8b169b00a5fe35db214f28007`. Stored Windows evidence: `docs/investigations/2026-11-10-run-ae84478d-g131 DerivedImpactIndex+ReleaseGateFollowup-windows-failure.md:22` |
| S2 keyword triggers and two new guards | Not started in inspected branches/history | No `tests/keyword-triggers.json`, KeywordTriggerTests, GeneratedTextNewlineGuardTests or DpapiPlatformGuardTests. Selector returns an empty `keywordIds` list unconditionally, `scripts/impacted-classes.ps1:1312` |
| S3 roster registry and solution shape guard | Not started in inspected branches/history | No `tests/roster-registry.json`, RosterRegistryTests or LandGraphAuditProjectShapeTests. Empty `rosterIds` is unconditional, `scripts/impacted-classes.ps1:1313` |
| S4 what-ran contract | Not started in inspected branches/history | No WhatRanReceiptContractTests; original D-9 remains a plan, `docs/superpowers/plans/2026-11-09-selector-v2-derived-index.md:159`. Existing owner docs describe selection, not the planned four-field paste contract, `docs/testing-and-build.md:341`, `docs/orchestration-loop.md:713` |
| S5 machine-readable why-missed ledger | Not started in inspected branches/history | No WhyMissedLedgerTests or `tests/why-missed-ledger-grandfather.txt`; original D-10 and S5 remain plan text |
| New work in flight | Investigation only for CARD-1820 at the board snapshot | Earlier eleven subject tasks terminal; no S2-S5 Code task in the returned subject rows. CARD-1836 has a separate current investigation, `b9444a8c-3762-49cc-b860-d747b6c0a6f6` |

The latest selector edit on pinned master is `e3be96cfd920a8dedf37416c218ca3320f6056c0` (compiled regex/source-walk optimization). The latest index edit is `a379439bb08c688bde9f432f1d62c861e860ed2e` (combined batch regeneration). The index is 833,421 bytes, 2,332 path edges, 152 capped/allowlisted path records, 31 windowsOnly names and one linuxOnly name. Cut index: 832,682 bytes, 2,328 edges, 152 capped/allowlisted records, the same exclusive-lane counts. Git blob IDs respectively `402f36c1a68c7a4dfe2525559389cd25a4cf0787` and `843868c48555abc7bc30ff1bd480e5b0390db3eb`. Schema and allowlist: `tests/derived-impact-index.json:1`.

Existing roster-like things are not the planned registry: `scripts/select-test-rosters.ps1`, `scripts/fixtures/nightly/c1584-e2e-source-roster.txt`, `tests/Shared/expected-duration-roster.json`, `tests/linux-test-roster.json`, execution/selection policy files, and source censuses remain distinct owners. The original D-7 registry table is at plan line 133. For example, `C1514LandingSplitBudgetTests.C1584_E2EExpandedBudget` calls `test-nightly-tests.ps1` (`tests/Antiphon.Tests/Scripts/C1514LandingSplitBudgetTests.cs:10`), whose roster read is `scripts/test-nightly-tests.ps1:1975`. An existing roster file does not establish registry coverage.

## Measurements on the real diffs

Commands, run on Linux with native `pwsh`, no build:

```text
pwsh -NoProfile -File scripts/impacted-classes.ps1 -Range 3db1a43b0..56e96eabf698 -Json
pwsh -NoProfile -File scripts/impacted-classes.ps1 -Range 56e96eabf698..cc48c7005 -Json
```

The first command reads the **current checkout**, even for a historical range. To avoid confusing that with a historical replay, I also exported `git archive 56e96eabf698` into an isolated ignored `cut-tree`, then ran its own selector with `-Repo <cut-tree>` and process-local `GIT_DIR=<assigned worktree git dir>` / `GIT_WORK_TREE=<cut-tree>`. This supplies the original Git objects while binding filesystem inputs, policies, index and script to the cut. No branch was switched and no tracked file was rewritten. `git diff --name-only` still uses the explicit immutable range.

| Replay | Changed paths / C# test-tree paths | Exit | Seconds, one sample | `classes` | `derivedClasses` | Linux filter rows | Windows filter rows / distinct labels |
|---|---:|---:|---:|---:|---:|---:|---:|
| Cut diff, selector/tree at the cut | 86 / 30 | 0 | 13.830 | 193 | 176 | 6 | 2 / 5 |
| Cut diff, selector/tree at master | 86 / 30 | 0 | 11.092 | 193 | 176 | 6 | 2 / 5 |
| Post-cut diff, selector/tree at master | 49 / 17 | 0 | 12.133 | 150 | 150 | 5 | 3 / 6 |

Each primary replay has 198 and 156 distinct nominal class labels respectively when `classes` is unioned with the Windows rows. These are **not chunk counts**, full names, or discovered/executed test counts. The same short name can occur in several projects. Filters use suffix `*` and may admit prefix siblings (`scripts/impacted-classes.ps1:279`). Appendix A preserves every emitted project/class operand and the method-limited row.

The equal counts conceal a version difference: the master-on-cut replay includes `C0747QueueRowExpiryTests` and excludes `CapacityRecoveryTaskTests`; the cut-on-cut replay does the reverse. That is direct evidence that a range alone does not bind selector input versions. The queue-row class did not exist at the earlier cut. Index version/candidate provenance matters independently of the changed-path range.

Both ranges returned `keywordIds=[]`, `rosterIds=[]`. Cut output `skippedByCap=["Antiphon.sln"]`; post-cut `skippedByCap=[]`. A zero-length `skippedByCap` **does not mean no cap omission**: non-allowlisted omissions appear in `owed`, not this field (`scripts/impacted-classes.ps1:1094`).

Output SHA-256, raw stdout as captured:

```text
cut-on-cut: b5fe1b9b267ea3214e9f9d01ef0de9b7dbc10f3948dd50321b5216667d299ce0
post-cut:   a5064dcb6b4b973b2435af5a35d3680e08dd53e0e6ea103977da3b1d65a23fe1
```

A narrow declaration check used file stems, with the partial `.Finish.cs` stem deduplicated: all 20 distinct changed `*Tests` file stems on the cut and all 15 on the post-cut appeared in the combined output. This verifies that simple changed-test declarations are being selected; it is not a dependency recall denominator. Helper/assembly-hook/shared-attribute files are outside that count. Fixture strings also contaminate the selector's regex census: the output includes `ProbeTests`, while `DerivedImpactIndexTests.cs:896` contains that declaration inside a raw string. The selector scans raw file text (`scripts/impacted-classes.ps1:528`, `:552`). The Roslyn builder and this declaration regex have different source models. The related post-land census was repaired separately in `3a8deb98a5b811e74ff6904edcb0ad75e3a266fe`; that commit does not change this selector.

### Cap omissions, including actual dependency misses

Exact cut-on-cut `owed` summary:

| Path | Omitted path/class pairs | Known first names absent from the **entire** cut selection |
|---|---:|---|
| `docs/ops-http.md` | 153 | AgentTaskLandAdmissionControlledTests, AgentTaskLandAdmissionTests, AgentTaskPoolTests, AgentTaskRefineTests |
| `server/Application/Services/AgentTaskLandNotificationService.cs` | 15 | DispatchHoldVisibilityTests, RepositoryMutationLeaseOwnerTests, RolePolicyToolBundleDispatchTests |
| `server/Application/Services/AttentionService.cs` | 20 | DispatchBaseInspectionDiagnosticsTests, DispatchHeldAttentionTests, GhostSeatReleaseTests, PostLandMutationWorkflowTests, RemoteWorkspaceReadinessTests, RoutingExhaustedPlacementTests, RunnerAlarmAttentionTests, SeatOccupancyAttentionTests |
| `server/Application/Services/SessionMessageQueueService.cs` | 125 | ChannelOutboundRecoveryTests, MatrixChildReceiptTests, PhoneHomeTaskDispatchProjectionTests, SessionMessageQueueServiceTests, SessionStateWarmupTests |
| `server/Application/Settings/SupervisionSettings.cs` | 146 | GrokDelegateEndToEndTests, HerdrSupervisionFailureEvidenceTests, ModelAvailabilityTests, SessionHealthTests, SessionMessageQueueServiceTests |

There are 459 omitted path/class pairs, not 459 unique missed tests. After subtracting all selected labels, the stored first-eight samples alone establish **24 distinct omitted labels**. The index does not retain the other omitted names beyond the first eight per path, so this is a lower bound, not an exhaustive list. Doc readers in this table are not automatically product-behavior misses. Concrete product dependencies do exist among the omissions:

* `SessionMessageQueueServiceTests` registers and resolves `SessionMessageQueueService`, `tests/Antiphon.Tests/Application/SessionMessageQueueServiceTests.cs:800`, `:845`.
* `SessionStateWarmupTests` resolves the queue and checks its state, `tests/Antiphon.Tests/Application/SessionStateWarmupTests.cs:170`, `:293`.
* `ChannelOutboundRecoveryTests` registers/resolves it too, `tests/Antiphon.Tests/Application/ChannelOutboundRecoveryTests.cs:1385`, `:1455`. It is absent on the cut; another changed file brings it back on the post-cut.
* `SeatOccupancyAttentionTests` constructs `AttentionService`, `tests/Antiphon.Tests/Application/SeatOccupancyAttentionTests.cs:345`. It is absent on both full ranges; the AttentionService file changed in the cut range.

All four are demonstrated dependency omissions; this investigation does not claim they would fail on these commits. The service and settings really changed in the named Git diffs. Top-25 generation is implemented in `DerivedImpactIndexBuilder.cs:162`, specifically `:182`; the committed queue cap record is `tests/derived-impact-index.json:1189` and its kept edge is `:10698`. The selector logs the omitted sample then selects only kept edges (`scripts/impacted-classes.ps1:1098`, `:1107`).

Post-cut `owed`: `docs/ops-http.md` 154; queue service 125; SupervisionSettings 147; `tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs` 7. That is 433 path/class pairs. The first-eight samples contain **18 distinct labels absent globally**: AgentTaskLandAdmissionControlledTests, AgentTaskLandAdmissionTests, AgentTaskPoolTests, AgentTaskRefineTests, MatrixChildReceiptTests, PhoneHomeTaskDispatchProjectionTests, SessionMessageQueueServiceTests, SessionStateWarmupTests, CapacityRecoveryTaskTests, GrokDelegateEndToEndTests, HerdrSupervisionFailureEvidenceTests, ModelAvailabilityTests, RoutingPinCandidateDispatchTests, SessionHealthTests, RunnerStartupReadinessTests, SessionLivenessTests, C1677TempSweepNativeTests, C1677TempSweepTests. Some are Windows-only or broad/helper-derived associations; this is not a list of eighteen required Windows reruns. A selected `C1677TempFamilyRegistryTests` is also in the path's owed sample: owed must be compared with the full union, never counted directly as missed classes.

### Script and shared-fixture omissions

Four completed, independent path probes on pinned master further isolate the mechanisms:

| `-Path` | Exit / seconds | `classes` / derived | Relevant result |
|---|---|---|---|
| `server/Application/Services/SessionMessageQueueService.cs` | 0 / 9.382 | 22 / 25 | No hand kind; three derived names routed to Windows; owed=125; SessionMessageQueueServiceTests and SessionStateWarmupTests absent |
| `scripts/lib/nightly-coverage.ps1` | 0 / 9.197 | 2 / 0 | Only hand kind nightly-live-lane, selecting LiveLaneRunnerTests and NightlyCoreTests; NativeLinuxPolicyMatcherParityTests absent |
| `scripts/lib/Antiphon.Scripts/Baseline.cs` | 0 / 8.373 | 0 / 0 | Empty successful selection, no owed record |
| `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs` | 0 / 8.641 | 10 / 9 | AgentTaskPoolTests absent; only narrow fixture tests plus the hand lane guard are selected |

Commands are the same selector invocation above with `-Range ...` replaced by the literal `-Path` value. These are selector reads, not test-driver runs.

Reconstructed chains, all using stored source and selection output:

1. **Compiled scripts and basename harnesses:** `ReleaseSelectionCliTests.BaselineMatchesWorker` calls `"test-release-selection-cli.ps1"`, `tests/Antiphon.Tests/Scripts/ReleaseSelectionCliTests.cs:29`. `tests/Antiphon.Tests/Scripts/ScriptHarness.cs:16` builds the full `scripts/` path at runtime. The script imports its production helpers (`scripts/test-release-selection-cli.ps1:8`) and invokes New-ReleaseSelectionAttempt (`:65`). Its compiled ReleaseSelection implementation calls `Baseline.Read`, `scripts/lib/Antiphon.Scripts/ReleaseSelection.cs:71`. The cut changes Baseline.cs; the class is absent from both complete replay selections. The builder's walked roots are only src/server/tests (`DerivedImpactIndexBuilder.cs:468`), and an ordinary script basename is not an exact tracked repo path (`:304`). No C#-to-PowerShell import closure is walked.
2. **Direct changed script:** `TestSelectionTests` calls `test-test-selection.ps1`, `tests/Antiphon.Tests/Scripts/TestSelectionTests.cs:31`; that script changed in the cut. The class is absent from the cut output. `ReleaseGatePublicationAuthorityTests` similarly calls `test-release-gate.ps1` (`tests/Antiphon.Tests/Scripts/ReleaseGatePublicationAuthorityTests.cs:152`); its production dot-source is `scripts/test-release-gate.ps1:21`. Both the test driver and release-authority.ps1 changed in the cut; the class is absent. ReleaseGateAuthorityContractTests is absent too. The basename/closure mechanism explains these without requiring a hypothetical input.
3. **Embedded script string:** `NativeLinuxPolicyMatcherParityTests` constructs a PowerShell command containing a `scripts/lib/nightly-coverage.ps1` path, `tests/Antiphon.Tests/Scripts/NativeLinuxPolicyMatcherParityTests.cs:106`. That file changed in the post-cut. The class is absent from both the single-path probe and the full post-cut selection. The entire C# literal is a command, not an exact tracked path. `CollectStringTargets` tests whole-string equality, then searches dotted production type names; it does not extract the quoted file within a command (`DerivedImpactIndexBuilder.cs:311`).
4. **Fixture/type transitivity and assembly hooks:** AgentTaskPoolTests uses TestDbFixture at `tests/Antiphon.Tests/Application/AgentTaskPoolTests.cs:428`; TestDbFixture owns a TestDbFixtureLifecycle at `TestHelpers/TestDbFixture.cs:22`; the assembly hook calls InitializeAsync at `TestHelpers/TestDbFixtureAssemblyHook.cs:22`. Both lifecycle and assembly-hook files changed in the cut, but AgentTaskPoolTests is absent. The builder's one hop reads path/dotted-name **strings** from referenced types (`DerivedImpactIndexBuilder.cs:135`), not a transitive production/helper type graph. Assembly hooks need no explicit class reference from every affected test.

### Other safety boundaries, with uncertainty distinguished

| Input shape | Evidence of what the current implementation does | What is not established |
|---|---|---|
| Reflection | Fully qualified production type strings are indexed, `DerivedImpactIndexBuilder.cs:325`; dotted-script fixture witness `DerivedImpactIndexTests.cs:363`. LandingGit is also hand-covered. | No general runtime reflection closure, simple-name string lookup, assembly scanning or dynamically built type-name completeness. No new reflection-specific failing test was demonstrated in these cut diffs. |
| Configuration / appsettings | Exact full tracked path literals can generate an edge; concatenated paths and arbitrary configuration-key dependencies are not resolved, builder `:304`. | These ranges contain no appsettings change; there is no measured appsettings recall rate. |
| DB schema / migrations | Hand migrations kind and downgrade scans exist; class declaration and C# direct references remain available. | No migration-file edit occurred in these ranges. Shared fixture/assembly changes do occur and are missed as above; migration callers alone do not prove every data consumer unchanged. |
| Scripts invoked by tests | Basename and dot-source chains above produce confirmed omissions. `scripts/lib/**/*.cs` and tools are outside builder source roots. | Complete script dependency coverage is absent, even with zero owed rows. |
| Generated code / attributes / ambiguous symbols | Builder parses tracked C# without compilation, `:17`, `:499`; type declarations are class-like, `:529`; attribute-name identifiers are excluded, `:625`; multiple distinct full names sharing a simple name remove that name from type lookup, `:600`. | Generated/untracked outputs, source-generator inputs, semantic binding, enums/interfaces/struct-only dependencies, and ambiguous names have no completeness proof. No generated-code regression was executed here. |
| Docker and global build inputs | DockerStackDocumentationTests and GrokRunnerImageContractTests are selected by these actual broader diffs, but there is no general Docker/runtime closure. Four global paths are deliberately allowlisted, builder `:32`. | Neither diff changes a Dockerfile. An empty reverse hit on such an input cannot establish that all runtime-dependent chunks are unchanged. |

Missing index files are accepted as empty (`scripts/impacted-classes.ps1:963`); a valid but stale index has no candidate/version check in the selector. The separate byte witness rebuilds and compares it (`DerivedImpactIndexTests.cs:134`). This report did not execute that witness at cc48c7005. Stored Windows evidence does show it passed in 7.8 seconds on 3db1a43b0, even while the regeneration-driver test failed for a missing executable; those are separate observations, not a current-green claim.

`-MergeSubset` intentionally exits with hand/fixed rows and without the derived/owed/keyword/roster payload (`scripts/impacted-classes.ps1:1242`, `:1256`, `:1278`). It must not be mistaken for the release dependency set. Likewise `windowsFilters` means Windows obligations selected by these rules, not the complete set of portable tests due on Windows. A native expected-set roster and suite identity are still needed to interpret a Windows chunk.

## The unavailable 236-chunk comparison

The brief's seed is `C:\Antiphon\releases\task-25e35cdf\seed-cumulative.json`. The assigned runner contract explicitly says the desktop checkout is unreachable from this mirror. That seed and run 0b292fa4's frozen plan/TRX are not available here. The brief's **236 newly invalidated chunks, approximately 245 impacted chunks, 1,163 total chunks and 17 hours are caller-supplied figures**, not measurements reproduced by this investigation. In particular, 193 classes cannot be compared numerically with 236 chunks.

The concrete missing comparison is:

1. Read the immutable seed and frozen planned groups for run 0b292fa4, checking source/candidate/run IDs and their recorded digests. The seed exporter preserves `suite`, `chunk`, `classes`, membershipSource, completion/start state and unfinishedReason (`scripts/lib/nightly-stopped-run-seed.ps1:319`, `:392`); the source plan also has plannedTestIds (`:368`). If the 236/newly-invalidated designation is stored in the ordering receipt rather than this seed, that receipt is required too; do not invent a seed field.
2. Identify exactly those 236 chunks and expand **their actual suite/class/method members**, with their invalidation reasons and changed source files. Join the Appendix A cut-on-cut project/class operands to those identities, resolving suffix expansion against candidate discovery. Do not collapse duplicate short names across projects, or treat the two ReleaseGatePlatform methods as a whole-class execution.
3. Report matched members and chunks, partially matched chunks, unmatched members/classes and their source paths, and additional selections beyond the seed. Count changed test declarations separately from helper/fixture-induced invalidation. Preserve unknown membership explicitly. A hit on one class in a mixed chunk does not prove every other class unchanged, and a class split across chunks is not one chunk.
4. Compare product/script dependencies independently: test-source-only invalidation cannot be the ground truth for unchanged tests that depend on changed production code or scripts. The omissions above are already counterexamples to using that denominator alone. A complete false-negative/false-positive rate requires an independently justified impacted set, not the selector's own edges.

This missing desktop join limits the numeric accuracy estimate. It does **not** prevent confirmation of the omission mechanism. No live qualification state was read or altered to obtain it.

## What evidence can justify a skipped chunk

The original what-ran plan is a four-field report paste: `kinds`, `classes`, `skippedByCap`, `windowsFilters` (CARD-1820 plan `:159`). These are useful selection facts, but the actual replay demonstrates why those four cannot by themselves justify release carry.

The following are audit questions and gaps against existing contracts, not a new receipt-schema design:

| Audit question | Evidence already required or missing |
|---|---|
| Which exact diff and selector inputs produced the decision? | Candidate/base full SHA, repository, mode/options, changed paths including deleted/renamed inputs, selector/index/table/catalog/keyword/registry/platform-policy versions or digests, and source cleanliness. The current selector payload at `scripts/impacted-classes.ps1:1307` carries no candidate/base/index binding. Equal counts on two differently bound cut replays demonstrably conceal different classes. |
| What did selection include and omit? | Full project-qualified filters, method restrictions, kinds, derived classes, keyword/roster matches, exclusive/owed lanes, skippedByCap **and owed**. Current cap records retain only eight omitted names (`DerivedImpactIndexBuilder.cs:23`); that is insufficient to reconstruct all discarded identities after the fact without the exact generator inputs. Reasons must distinguish a cap omission from an unchanged dependency. |
| What actually ran? | Run/attempt, platform/SDK/runtime, frozen discovered expected identity set, chunk membership/boundary, command/filter, exit, executed/passed/failed/expected-skip/unexpected-skip counts, timing and evidence identity/digests. A selector stdout capture proves selection only. CARD-1836 plan D-1 (`:77`) already names outcomes, durations, contracts and chunk/identity records. |
| Why was this particular chunk carried? | Same-platform prior completed green/expected-skip source row, equal input digest and known boundary, baseline/candidate/run/chunk provenance, no forced Full, and required failure/fix containment proof. Current ledger predicate refuses missing row, missing digest, unknown boundary, cross-platform, bad outcome, unfinished chunk and incomplete provenance (`scripts/lib/rc-ledger.ps1:306`). None of those facts follows from selector absence. |
| Can another operator reproduce the answer? | Retained source/evidence references and digests, the prior row and exact new decision, including dirty/full/unknown reason. CARD-1836's carriedFrom fields already provide the prior candidate/run/SHA/chunk/digest relationship; a human paste alone has no execution or carry authority. |

The complete first-eight cap samples and actual class operands from these measurements remain in this report. No claim is made that a what-ran writer or persistent release carry receipt has been implemented by CARD-1820.

## Existing remaining work and dependency facts for the Plan stage

The smallest remaining **already specified** CARD-1820 slices are S2-S5, not another replay of S1. No existing three-Code-slice path can currently be certified sufficient for release skipping: the original scope does not close the demonstrated dependency gaps. These are inventory facts from the original plan, not approval of that design as a release-skipping proof:

| Existing slice | Original estimate | Evidence for remaining scope / verification | Placement and collision facts |
|---|---:|---|---|
| S2 keywords and two guards | 1.5 h | Plan `:290`, CP-2: KeywordTriggerTests, GeneratedTextNewlineGuardTests, DpapiPlatformGuardTests; currently absent | Linux Codex/Grok work; touches selector shared with S3. No native DPAPI call needed by the planned source guard. |
| S3 roster registry and solution shape guard | 1.5 h | Plan `:294`, CP-3: RosterRegistryTests, LandGraphAuditProjectShapeTests; currently absent | Linux Codex/Grok; selector integration overlaps S2, so these are not independent simultaneous Code tasks on the same source area. |
| S4 what-ran owner-doc contract | 1 h | Plan `:298`, CP-4: WhatRanReceiptContractTests; absent | Linux Codex/Grok; original slice changes docs/testing-and-build.md and docs/orchestration-loop.md, overlapping other documentation work, not the S2/S3 selector code. |
| S5 machine-readable why-missed contract | 1.25 h | Plan `:302`, CP-5: WhyMissedLedgerTests plus existing contract; absent | Linux Codex/Grok; shares docs/orchestration-loop.md with S4. The original plan does not make it the runtime carry mechanism. |
| Release-use reconciliation of the confirmed omissions | No existing implementation estimate | Real cap, script closure, assembly-hook and version-binding gaps above are not resolved by an existing S1 drift pass or promised by S2-S5 | Plan work on Linux can be performed independently of CARD-1836 boundary/digest implementation; subsequent source changes/verification must be scoped by that Plan. No new fix design is supplied here. |
| Exact Windows seed/discovery join | No measured duration; dispatch allowance 0.5-1 h | Needs the named 236-chunk seed/ordering evidence and frozen membership, not a new qualification run | Native Windows **evidence access** needed under this runner contract. No exclusive Windows computation is inherent in the join once artifacts are copied. |

The old S2-S5 estimates sum to **5.25 authoring hours**, excluding review, builds, admission and native qualification. They are unvalidated estimates from the original plan, not an estimate to safe release skipping. Historical S1 evidence warns against treating them as wall-clock promises: the final clean review recorded CP-1 64/64 passing plus 78/78 guards, with 470.8s cold build and 1,375.457s CP-1 wall (`origin/feat/card-task-dfefd8e0:.antiphon/task-dfefd8e0.md:14`). The replay author separately recorded an initial 80-result run with three wrongly filtered platform failures, then lane-correct reruns of 2 and 1 passing, and 135 passing guards (`GET /api/agent-tasks/d6d56b84-7130-4682-82f8-49676290f542`, Result). Those historical failures are retained; this investigation did not rerun or erase them.

CARD-1836's plan explicitly says CARD-1820 is **not a prerequisite** for its initial boundary implementation (`docs/superpowers/plans/2026-11-09-card-1836-incremental-rc.md:102`, `:189`), despite the broader card description naming this index. S1 ledger code exists at `scripts/lib/rc-ledger.ps1`; later boundary/digest/packer/status/launch/baseline/forced-Full/replay steps are distinct S2-S10 at `:248`. The direct dependency in that plan is ledger + boundary/digest/membership + provenance/closure before production skip, not a small selector output alone. It permits a later replacement of boundary assignment by this index; the omission evidence here is relevant to that future decision.

Thus the two cards' independent Plan/evidence work and independent files can proceed concurrently on server2 using Codex/Grok; source-area collisions above still apply. Running Windows-only or Windows-specific test bodies, validating Windows SDK/ConPTY/DPAPI effects, and replaying the inaccessible native evidence require a separately commissioned Windows lane. Linux source-guard passes do not supply those results. The existing ae84478d note gives native evidence for the old index byte witness, not full validation of a future selector/release integration.

**Policy-change constraint on the example savings:** `tests/test-execution-policy.json` changed between the cuts. Its stored policyHash moved from `4ab004cf02e2d8adb80f4744030bd23fae3e2fd9b2cafff94822ba43b7bc5719` to `a1d534f78407944871f438cc0d0d008d6aa0b107db645ee1396796bfa504e99c`; git-in-process was added to required suites. The catalog pin changed from `f4aa926ae5d4ba930718c71b097c04316e1855431c35ba5d22de072749df68ec` to `6f7a46a74b18c1269d9b99f4ab12d9f060f0fc2139f20f5c05ca47c73512c64d`. CARD-1836 D-4 (`:145`) forces Full on policy/catalog/selector/runtime hash changes. Consequently this particular historical cut is not evidence that that plan would legally skip all but approximately 245 chunks. That would require a different baseline/contract or a Plan/operator decision about an existing safety rule; no such decision is assumed here. Current project-selection advisory logic is also conservative: any scripts/shared-input reason expands affected projects (`scripts/lib/rc-project-selection.ps1:233`, `:250`). Current release selection still emits effectiveMode=Full (`scripts/lib/release-selection.ps1:219`, `:229`); no launch behavior was changed.

### First three dispatchable tasks, using existing scope

**1. Plan, CARD-1820 release-use gap reconciliation (allowance 1-2 h, estimate only; server2 Codex/Grok).** Update the planning assessment against pinned cc48c7005 and this report's cut/post-cut observations: inventory the cap/allowlist, script and fixture closure omissions, index/candidate binding, short-name/filter expansion and execution-versus-selection evidence. Reconcile CARD-1820's optional index role with CARD-1836 D-2/D-4 and its policy-change forced Full; identify the evidence needed before a carry can be called justified. The output is a reviewed planning/checkpoint scope with sizes and owners. This investigation supplies no implementation design and does not authorize weakening existing dirty/full rules.

**2. Code, existing CARD-1820 S2 (original 1.5 h; budget build/review separately; server2 Codex/Grok).** Execute only original plan S2/CP-2: the reviewed keyword list, KeywordTriggerTests and the generated-text/newline and DPAPI source guards, preserving the existing hand selector contract and reporting the actual checkpoint counts. The task starts from landed S1 and does not depend on CARD-1836's runtime wiring. Its selector edits must settle before another selector-integrating Code slice starts; the three currently empty/new guards do not claim to solve release dependency completeness.

**3. Code, existing CARD-1820 S4 (original 1 h; budget build/review separately; server2 Codex/Grok).** Execute original S4/CP-4's owner-doc four-field what-ran contract and WhatRanReceiptContractTests, preserving stage bundles as the plan requires. Keep the documented meaning at selection/report evidence; this slice is not the missing CARD-1836 execution/carry writer. Its docs/test files can run alongside S2 under the normal scope rules, provided no concurrent owner-doc task is writing them. S3 is the next existing selector Code slice after S2; S5 follows the shared-doc work. These tasks describe the existing plan rather than inventing a repair in Investigate.

## Remaining uncertainties and verdict

* The 236-chunk desktop join and a complete independent impacted-set denominator remain unavailable. The exact input artifacts and comparison above would resolve that numeric question.
* Only first-eight cap samples are stored; full omitted membership needs reconstruction from the exact generator source/tree. Counts of omitted pairs are not unique affected classes or failing tests.
* The index's present drift witness was not run at cc48c7005. Its historic clean review/native result cannot prove a current clean build. No builds/tests were needed to reproduce these selection omissions.
* No new failing assertion, reflection/config/Docker/generated-code regression, or native Windows behavior was executed; those rows distinguish known implementation limits from observed failures.
* S2-S5 have no current code or fresh cost measurements in the inspected subject branches. No independent clean review of their future implementation is implied by the S1 review.
* The brief's next-cut runtime saving is not established. Current Full/default and forced-Full contracts are separate from selector accuracy, and the example cut changes the contract.

**Root-cause status: confirmed by source reconstruction plus seven completed selector invocations (three range replays, four path probes).** A deliberate incomplete reference set can produce a successful empty or partial selection; its absence signal is insufficient carry evidence. This is a requirement mismatch when promoted from pre-land test discovery into a release-skipping authority. Next stage: Plan, to reconcile the confirmed gap with existing release invariants. No approval was requested; the authorized read-only investigation and report publication proceeded.

## Not done, noted

Fix idea for the Plan owner only: reconcile conservative release dirty/carry evidence with cap, unknown, script/fixture and candidate-binding gaps before treating selector absence as permission to skip.

## Appendix A: exact emitted selections

The following lists are transcribed from the captured JSON, grouped by each emitted project row. A plain class label means that row's `(ClassName*)` operand, not exact discovery membership; method rows preserve their method restrictions. Portable Linux filters and Windows obligations are recorded separately. Fixture labels and duplicate short names are preserved as emitted.

Legend: `D` means present in the JSON `derivedClasses` union, which includes changed-test declarations as well as reverse-index hits. `H` means this label was not in that union and was supplied by hand selection. Neither marker is an execution result.

### Cut-on-cut: 3db1a43b0..56e96eabf698

Linux filters:

`tests/Antiphon.Agents.Pty.Tests`

```text
ProcessSpawnLimitTests H
```

`tests/Antiphon.E2E`

```text
ContractSnapshotTests H
LandDeliveryFixtureProbeTests H
```

`tests/Antiphon.GitInProcess.Tests`

```text
GitInProcessAssemblyTests D
InProcessGitCommandsTests D
LibGit2VsGitDifferentialTests D
LibGit2VsGitJunctionTests D
LibGit2VsGitWorkingTreeVerdictTests D
LibGit2VsGitWorktreeListTests D
```

`tests/Antiphon.SessionRunner.Tests`

```text
C1677TempFamilyRegistryTests D
ProcessSpawnLimitTests H
StandingRunnerCwdTests D
```

`tests/Antiphon.Tests`

```text
AdditiveMigrationRuleWitnessTests D
AdvisoryChangePolicyTests D
AgentAttachHerdrTests D
AgentBundleAttachmentTests D
AgentPinnedInstructionEndpointTests D
AgentSessionCancelledTurnTests D
AgentSessionLaunchFailureTests D
AgentSupervisionTests D
AgentSystemPromptLaunchTests D
AgentTaskCallerResolutionTests D
AgentTaskCheckInterpreterTests D
AgentTaskCheckScheduleTests D
AgentTaskCheckSweepTests D
AgentTaskCommitEndpointTests D
AgentTaskDeadSessionReconciliationTests D
AgentTaskDeliveryWatchdogTests D
AgentTaskDispatchBaseGuardTests D
AgentTaskDispatchFailureTests D
AgentTaskInputSpillTests D
AgentTaskInternalDecisionLifecycleTests D
AgentTaskLandActiveSourceClaimTests D
AgentTaskLandHeldNotificationTests H
AgentTaskLandMonitoringTests D
AgentTaskLandNotificationParkFollowUpTests H
AgentTaskLandNotificationPersistenceTests H
AgentTaskLandNotificationReconcileLoadTests H
AgentTaskLandNotificationRecoveryTests D
AgentTaskLandNotificationRescanTests H
AgentTaskLandPersistenceFailureTests D
AgentTaskLandPublicationTests D
AgentTaskLandQueueAgingTests D
AgentTaskLandQueuedReceiptTests D
AgentTaskLandReceiptTests H
AgentTaskLandReceiptWatermarkSafetyTests H
AgentTaskLandReceiptWatermarkTests H
AgentTaskMidTurnRefineTests D
AgentTaskReplyIntegrationTests D
AgentTaskReuseEnqueueTests D
AgentTaskServiceIntegrationTests D
AgentTaskWorktreeLockOutcomeTests D
AllLinuxOnlyTests D
ApiErrorRecoveryServiceTests D
AttentionServiceTests D
AwayDigestNotifierTests D
AwayDigestProjectionTests D
BlockedReplyReportRetentionTests D
BlockedSeatEscalationNotifierTests D
BlockedTaskNotifierTests D
BlockedTaskParkDeliveryTests D
BlockedTaskParkProjectionTests D
C0747LandNoteExpireEndpointTests D
C0747LandNoteExpireOffSwitchEndpointTests D
C0747LandReceiptReplayTests D
C0824RefOnlyCleanupTests D
C0824RefOnlyLandingTests D
C0824RefOnlyReceiptTests D
C1677DiskAlarmTests D
C1677TempFamilyRegistryTests D
C1729LayeringGuardTests D
CapacityObsoleteQueueTests D
CapacityRecoveryAcceptanceTests D
CapacityRecoveryAttentionTests D
CapacityRecoveryPersistenceTests D
CapacityRecoveryPolicyTests D
CapacityRecoverySupervisionTests D
CapacityRecoveryTaskTests D
Card1619ExitConsumerTests D
CardClosedAttentionTests D
CardDoneWorktreeCleanupTests D
CardThreadEndpointTests D
CardWorktreeCleanupRecoveryTests D
ChannelBridgeTests D
ChannelInboundRecoveryTests D
ChannelIngressIncidentTests D
ChannelInputWithheldIncidentTests D
ChannelOutboundDeliveryTests D
CheckCompactionAttentionTests D
CheckNoteDeliveryHandoffTests D
CheckpointManifestDocumentationTests D
CheckpointToolProjectContractTests D
ClassLinuxOnlyTests D
ClassTeardownTrackerTests D
CodexStartupDeliveryTests D
CompletedCardWorktreeRemovalTests D
CompletionNoteDurableSweepTests D
DataRetentionServiceTests D
DecisionCardNotifierTests D
DelegateCheckProbeTests D
DelegationDispatchRecoveryBoundaryTests D
DerivedImpactIndexTests D
DiagnosticsBundleServiceTests D
DispatchBaseNotificationTests D
DispatchConcurrencyAdmissionTests D
DockerStackDocumentationTests D
DotnetToolManifestContractTests D
EffectiveSettingsPipelineTests D
ExpectationDebtTests H
ExpectedDurationSyncTests D
GitIgnorePreviewTests D
GrokRunnerImageContractTests D
HangfireLeaseDocumentationTests D
HangfirePostgreSqlStorageTests D
HerdrAlwaysOnChannelParityTests D
HerdrPaneDisposalApplicationTests D
ImpactedClassesSelectorTests D
InstructionBundleTests D
InstructionFileStampTests D
LandEvidenceWarningDeliveryTests D
LandGraphAuditProjectInstanceTests D
LandGraphEvaluationTests D
LandGraphLandRefreshTests D
LandNonCodeSkipTests D
LandReceiptScanCacheTests H
LandingGitBatchingTests H
LandingSourceOptionalLockTests D
LegacyCheckNotePublicationTests D
LiveLaneRunnerTests H
MechanicalPcContractTests D
NightlyCoreTests H
OutputDistillationApplyRaceTests D
OutputDistillationDeliveryTests D
OverrideTargetTests D
ParkedMessageSweepServiceTests D
PendingNoteWithholdTests D
PendingSettlementDeliveryTests D
PerTaskFinalUnitLaneWitnessTests D
PhoneHomeOutageMentionTests D
PipelineDefinitionDocumentationTests D
PipelineSpeedIntegrationTests D
PlanCoverageBindingTests D
PostLandEarlyWarningTests D
PostLandMutationDeliveryTests D
PreDispatchFailureDeliveryTests H
ProbeTests D
ProcessSpawnLimitTests H
ProviderEndpointProjectionTests D
RcLedgerDurationTests D
ReceiptFailureDeliveryTests D
ReceiptScanDestinationIntegrationTests H
ReceiptScanDestinationTests D
RepositoryMutationLeaseTests D
ReviewTestQualityChecklistWitnessTests D
RoutingPinCandidateDispatchTests D
RunnerBranchContractDocumentationTests D
RunnerClaudeFinalLaunchArgvTests D
RunnerPushCredentialDocsTests D
ScopeResolverAreaTests D
Server2StagingServerContractTests D
ServerGitProcessTests H
SessionRunnerAbsenceEvidenceDocumentationTests D
SessionRuntimeActionRoutingTests D
SessionRuntimeActionTests D
SessionTerminationSourcePersistenceTests D
SlowTestRegistryMergeTests D
SolutionShapeTests D
StableBuildOutputDocumentationTests D
StagedHelperBuildTests D
StaleWorkingTranscriptWatchdogTests D
StandingPipelinePolicyDocumentationTests D
StandingRestartAccountingTests D
StandingRestartEscalationTests D
TaskInputAttentionCostTests D
TaskInputWeakArmBoundaryTests D
TaskSupervisionReceiptTests D
TestDbBakedImageDataDirectoryTests D
TestDbBakedImageLifecycleTests D
TestDbClonePoolTests D
TestDbFixtureIsolationTests D
TestDbFixtureLifecycleTests D
TestDbFixtureSchemaCacheLifecycleTests D
TestLaneCategoryGuardTests H
TestMigrationFingerprintTests D
TestSchemaSnapshotTests D
TestTimingChokePointTests D
TimeoutAuditTests D
TranscriptBindingIncidentTests D
TranscriptWorkingStateQueryTests D
UndeclaredOsTokenTests D
VerificationRoundDeliveryTests D
WatchQualEtaLineTests D
WindowsSpecificsTests D
WindowsUnitPlatformAdmissionTests D
WorktreeLandingCleanupRetryTests D
```

`tests/Antiphon.Tests`

```text
/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)|(LinuxOnlyWindowsExclusionPolicy*)
```

Windows obligations:

`tests/Antiphon.E2E`

```text
AgentTaskLandDeliveryE2ETests H
```

`tests/Antiphon.Tests`

```text
AgentSessionServiceIntegrationTests D
SessionMessageQueueGrokPtyIntegrationTests D
SessionMessageQueuePtyIntegrationTests D
SessionQueueReceiptPlumbingTests D
```

### Post-cut at master: 56e96eabf698..cc48c7005

Linux filters:

`tests/Antiphon.Agents.Pty.Tests`

```text
ProcessSpawnLimitTests D
```

`tests/Antiphon.E2E`

```text
ContractSnapshotTests H
LandDeliveryFixtureProbeTests D
```

`tests/Antiphon.Server.Git.Tests`

```text
GitIndexLockTests H
ServerGitAssemblyTests H
```

`tests/Antiphon.SessionRunner.Tests`

```text
C1022BackendLaunchTests D
C1677TempFamilyRegistryTests D
C1793SessionPwshCacheTests D
C1818GcOwnerRobustnessTests D
Card1636AdoptionIdentityTests D
Card1653FreshLaunchIdentityTests D
Card1653PeerCheckTests D
Card1653RefusalRetainsHostTests D
Card1653SocketDirTests D
CompactionContinuationStopTests D
FirstWriteRaceTests D
GrokRulesAdoptionTests D
GrokRulesFileLaunchTests D
GrokRulesHttpAcceptanceTests D
GrokRulesRunnerRefusalTests D
GrokRulesStoreFailureTests D
ProcessSpawnLimitTests D
PtyBackendSeamTests D
PtyHostAdoptionTests D
PtyHostLeakSweepTests D
RemoteControlConditionalInputTests D
RunnerChildClaudeTokenInheritanceTests D
RunnerMultilinePromptDeliveryTests D
RunnerPlatformLaunchTests D
RunnerRestartScriptCompatibilityTests D
RunnerSessionGenerationTests D
SessionBufferBoundsTests D
SessionCpuWatchdogTests D
TestSessionTeardownTests D
TranscriptAdoptionSafetyTests D
```

`tests/Antiphon.Tests`

```text
AgentAttachHerdrTests D
AgentBundleAttachmentTests D
AgentSessionCancelledTurnTests D
AgentSessionLaunchFailureTests D
AgentSupervisionTests D
AgentSystemPromptLaunchTests D
AgentTaskCheckInterpreterTests D
AgentTaskCheckScheduleTests D
AgentTaskCheckSweepTests D
AgentTaskCommitEndpointTests D
AgentTaskDeadSessionReconciliationTests D
AgentTaskDeliveryWatchdogTests D
AgentTaskDispatchBaseGuardTests D
AgentTaskDispatchFailureTests D
AgentTaskInputSpillTests D
AgentTaskInternalDecisionLifecycleTests D
AgentTaskLandActiveSourceClaimTests D
AgentTaskLandMonitoringTests D
AgentTaskLandNotificationParkFollowUpTests D
AgentTaskLandNotificationReconcileLoadTests D
AgentTaskLandNotificationRecoveryTests D
AgentTaskLandNotificationRescanTests D
AgentTaskLandPersistenceFailureTests D
AgentTaskMidTurnRefineTests D
AgentTaskReplyIntegrationTests D
AgentTaskReuseEnqueueTests D
AgentTaskWorktreeLockOutcomeTests D
AllLinuxOnlyTests D
ApiErrorRecoveryServiceTests D
BlockedTaskParkProjectionTests D
C0747LandNoteExpireEndpointTests D
C0747LandNoteExpireOffSwitchEndpointTests D
C0747LandReceiptReplayTests D
C0747QueueRowExpiryTests D
C1677TempFamilyRegistryTests D
CapacityObsoleteQueueTests D
CapacityRecoveryAcceptanceTests D
CapacityRecoveryPersistenceTests D
CapacityRecoveryPolicyTests D
CapacityRecoverySupervisionTests D
CardFileGitFailureAcceptanceTests D
CardFileIgnoreAcceptanceTests D
CardFileIgnoreTests D
CardFilePrivacyGitTests D
CardFilePrivacyRecoveryTests D
CardTaskFileServiceTests D
ChannelBridgeTests D
ChannelOutboundRecoveryTests D
ClassLinuxOnlyTests D
ClaudeTokenRefreshOptInTests D
CodexRootExecContractTests D
CodexRunnerImageContractTests D
CodexStartupDeliveryTests D
DelegateCheckProbeTests D
DelegationDispatchRecoveryBoundaryTests D
DeliveryUnverifiedDiagnosticTests D
DeployServer2ApiDefaultTests D
DerivedImpactIndexTests D
DescendantLeakScopeTests D
DispatchBaseNotificationTests D
DockerStackContractTests D
DockerStackDocumentationTests D
ExpectationHoldReleaseEndpointTests D
GrokRunnerImageContractTests D
HangfireLeaseDocumentationTests D
HangfirePostgreSqlStorageTests D
HangfireStorageSelectionTests D
HerdrAlwaysOnChannelParityTests D
HerdrPaneDisposalApplicationTests D
ImpactedClassesSelectorTests D
InstructionBundleTests D
InstructionFileStampTests D
JqRunnerImageContractTests D
LiveLaneRunnerTests H
NightlyCoreTests D
NightlyScriptsTests D
OutputDistillationApplyRaceTests D
OverrideTargetTests D
ParkedMessageSweepServiceTests D
PendingNoteWithholdTests D
PhoneHomeOutageMentionTests D
PhoneHomeQueuedTurnTests D
PipelineDefinitionDocumentationTests D
ProbeTests D
ProcessSpawnLimitTests D
ProjectCascadeForeignKeyTests D
RcBridgeProbePlatformTests D
ReceiptFailureDeliveryTests D
RemoteScriptContractTests D
RepositoryMutationLeaseTests D
RunnerBranchContractDocumentationTests D
RunnerClaudeFinalLaunchArgvTests D
RunnerPushCredentialDocsTests D
Server2ProductionServerContractTests D
Server2SidecarStandingAgentsTests D
Server2StagingServerContractTests D
ServerGitProcessTests D
ServerImagePushCredentialTests D
SessionMessageQueueDeliveredSpillTests D
SessionMessageQueueDeliveryVerificationTests D
SessionRunnerAbsenceEvidenceDocumentationTests D
SessionTerminationSourcePersistenceTests D
SolutionShapeTests D
SpecialistInputTransportTests D
StaleWorkingTranscriptWatchdogTests D
StandingPipelinePolicyDocumentationTests D
StandingRestartAccountingTests D
StandingRestartEscalationTests D
TaskSupervisionReceiptTests D
TestClientFilterTests D
TestLaneCategoryGuardTests H
TranscriptBindingIncidentTests D
TranscriptWorkingStateQueryTests D
UndeclaredOsTokenTests D
VolumeRetirementApplyGateTests D
WindowsSpecificsTests D
WorktreeDefaultCallerTests D
ZombieCensusPlatformTests D
```

Windows obligations:

`tests/Antiphon.E2E`

```text
AgentTaskLandDeliveryE2ETests H
```

`tests/Antiphon.SessionRunner.Tests`

```text
RunnerPlatformContractTests D
```

`tests/Antiphon.Tests`

```text
AgentSessionServiceIntegrationTests D
SessionMessageQueueGrokPtyIntegrationTests D
SessionMessageQueuePtyIntegrationTests D
SessionQueueReceiptPlumbingTests D
```

## Appendix B: origin branches mentioning CARD-1820

Full remote tips confirmed with `git ls-remote origin refs/heads/feat/card-task-<id>`; the assigned task branch is excluded from historical inventory. No source branch was changed. A terminal task status of Succeeded does not mean its Review outcome was Clean.

| Branch under origin/feat/ | Remote tip | Meaning of retained branch |
|---|---|---|
| `card-task-898f2437` | `f40cb3efa144900fcea0a430b2b049412dccd61f` | Original plan; replayed onto master. |
| `card-task-6fde97f6` | `238af3ff3e300f0e13fae828d5286c97cf8e65a8` | Initial S1; round-1 Review found four blockers. |
| `card-task-8ec4caa8` | `ffabc0b9e41291925d330af8a0b0ed971d682e97` | Round-1 review report, .antiphon/task-8ec4caa8.md:1; no clean verdict. |
| `card-task-33c87de9` | `af3e860939b4a7ee7c71a7ead7cab3edec30d703` | Round-1 repair/report; ancestor behavior subsequently replayed. |
| `card-task-f4320f2f` | `af3e860939b4a7ee7c71a7ead7cab3edec30d703` | Old owner ref, same repair tip. |
| `card-task-f4320f2f-rebased` | `682375f5d54c4cf8328ff6810e1046c1ddca7fec` | S1 on later master; round-2 Review found one Windows-census blocker. |
| `card-task-7c6f052c` | `69b299c75e21867b32e40d75f2f1f297b65c35ac` | Round-2 report, .antiphon/task-7c6f052c.md:1; explicitly Interim, one blocker. |
| `card-task-693bdeb9` | `ed6270b6173a42db36bde920abe01fec73f82899` | Token-census repair; round-3 Review found over-classification blocker. |
| `card-task-0ddd898d` | `ed6270b6173a42db36bde920abe01fec73f82899` | Stale nominal owner ref; not the later reviewed source. |
| `card-task-135047fd` | `732648d326266bf487a59dc3f957fed9a63e2a4a` | Round-3 report, .antiphon/task-135047fd.md:1; one blocker, escalated. |
| `card-task-0ddd898d-r4` | `cb931f0890126ee1327eb2fc9fe29103e3667fe4` | New declaration design; exact historical clean-reviewed tip. |
| `card-task-dfefd8e0` | `16df2ab6c550c981e6874687f01ff36a3211597d` | Clean Full review report, .antiphon/task-dfefd8e0.md:1. |
| `card-task-d6d56b84` | `66428403011a078edd4257dbf5fd1f4be8fe7443` | Replay/regeneration; mapped to landed 750cb3c79c125ca8dc88c3c5543144bf70891635. |
| `card-task-fc265931` | `0060bdacffc33476ee1f0fc6f3bbf9c1fe7a41f2` | Publication report .antiphon/task-fc265931.md:1; records actual master mapping. |
| `card-task-118a855f` | `2a5e5a7f9e1749af088975da5714638728b07e37` | Separate CARD-1784 raw-string post-land census repair/report; related only by CARD-1820 fixture evidence. Core repair landed as 3a8deb98a5b811e74ff6904edcb0ad75e3a266fe. |
| `card-task-0c8caad0` | `807575281d09f15355589332cc96ad74c2fa64b1` | Alternative CARD-1784 census repair + stored native report (22 passed, 0 failed); not new selector S2-S5 work. |

The first three historical Reviews are not latent clean approvals: their reports retain the blockers. The clean fourth source Review changed the lane-classification design; its scope was S1 only. All retained subject-code versions and their report paths were inspected; no additional unlanded S2-S5 slice was found.

## Appendix C: unedited primary owed payloads

These JSON arrays are the primary selectors' `owed` values. Counts describe a path's truncated reverse associations before the union with other sources.

cut-on-cut:

```json
[
  {
    "path": "docs/ops-http.md",
    "count": 153,
    "first": [
      "AgentTaskLandAdmissionControlledTests",
      "AgentTaskLandAdmissionTests",
      "AgentTaskLandMonitoringTests",
      "AgentTaskMidTurnRefineTests",
      "AgentTaskPoolTests",
      "AgentTaskRefineTests",
      "AgentTaskReplyIntegrationTests",
      "AgentTaskReuseEnqueueTests"
    ]
  },
  {
    "path": "server/Application/Services/AgentTaskLandNotificationService.cs",
    "count": 15,
    "first": [
      "DispatchHoldVisibilityTests",
      "RepositoryMutationLeaseOwnerTests",
      "RolePolicyToolBundleDispatchTests",
      "ImpactedClassesSelectorTests",
      "DerivedImpactIndexTests",
      "AgentTaskLandNotificationRecoveryTests",
      "AgentTaskLandReceiptTests",
      "AgentTaskLandNotificationPersistenceTests"
    ]
  },
  {
    "path": "server/Application/Services/AttentionService.cs",
    "count": 20,
    "first": [
      "DispatchBaseInspectionDiagnosticsTests",
      "DispatchHeldAttentionTests",
      "GhostSeatReleaseTests",
      "PostLandMutationWorkflowTests",
      "RemoteWorkspaceReadinessTests",
      "RoutingExhaustedPlacementTests",
      "RunnerAlarmAttentionTests",
      "SeatOccupancyAttentionTests"
    ]
  },
  {
    "path": "server/Application/Services/SessionMessageQueueService.cs",
    "count": 125,
    "first": [
      "ChannelOutboundRecoveryTests",
      "MatrixChildReceiptTests",
      "PhoneHomeTaskDispatchProjectionTests",
      "SessionMessageQueueServiceTests",
      "SessionStateWarmupTests",
      "AgentSessionServiceIntegrationTests",
      "AgentTaskCheckInterpreterTests",
      "AgentTaskDispatchBaseGuardTests"
    ]
  },
  {
    "path": "server/Application/Settings/SupervisionSettings.cs",
    "count": 146,
    "first": [
      "CapacityRecoveryTaskTests",
      "ChannelBridgeTests",
      "GrokDelegateEndToEndTests",
      "HerdrSupervisionFailureEvidenceTests",
      "ModelAvailabilityTests",
      "RoutingPinCandidateDispatchTests",
      "SessionHealthTests",
      "SessionMessageQueueServiceTests"
    ]
  }
]
```

post-cut:

```json
[
  {
    "path": "docs/ops-http.md",
    "count": 154,
    "first": [
      "AgentTaskLandAdmissionControlledTests",
      "AgentTaskLandAdmissionTests",
      "AgentTaskLandMonitoringTests",
      "AgentTaskMidTurnRefineTests",
      "AgentTaskPoolTests",
      "AgentTaskRefineTests",
      "AgentTaskReplyIntegrationTests",
      "AgentTaskReuseEnqueueTests"
    ]
  },
  {
    "path": "server/Application/Services/SessionMessageQueueService.cs",
    "count": 125,
    "first": [
      "ChannelOutboundRecoveryTests",
      "MatrixChildReceiptTests",
      "PhoneHomeTaskDispatchProjectionTests",
      "SessionMessageQueueServiceTests",
      "SessionStateWarmupTests",
      "AgentSessionServiceIntegrationTests",
      "AgentTaskCheckInterpreterTests",
      "AgentTaskDispatchBaseGuardTests"
    ]
  },
  {
    "path": "server/Application/Settings/SupervisionSettings.cs",
    "count": 147,
    "first": [
      "CapacityRecoveryTaskTests",
      "ChannelBridgeTests",
      "GrokDelegateEndToEndTests",
      "HerdrSupervisionFailureEvidenceTests",
      "ModelAvailabilityTests",
      "RoutingPinCandidateDispatchTests",
      "SessionHealthTests",
      "SessionMessageQueueServiceTests"
    ]
  },
  {
    "path": "tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs",
    "count": 7,
    "first": [
      "RunnerStartupReadinessTests",
      "SessionLivenessTests",
      "DerivedImpactIndexTests",
      "ImpactedClassesSelectorTests",
      "C1677TempSweepNativeTests",
      "C1677TempSweepTests",
      "C1677TempFamilyRegistryTests"
    ]
  }
]
```

## Reproduction notes

Use the pinned master checkout for the two range commands and four literal path probes shown above. To reproduce the primary cut result without switching branches, export that exact commit into an isolated scratch directory and bind both `-Repo` and the child Git working-tree context to it; retain the assigned repository's object database through `GIT_DIR`. The cut script/index/table/test-tree must all come from the cut, not a mixture with current files. This is precisely the isolation used for the 13.830-second measurement. No test binary, pre-existing process, network executor or native Windows machine supplied those selector results.
