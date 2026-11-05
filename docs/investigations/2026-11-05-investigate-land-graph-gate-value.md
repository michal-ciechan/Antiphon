# Land-graph digest gate: catches versus churn

Date: 2026-11-05 UTC. Investigate task `a8945a17-c1da-451f-ae2c-1a42f9406656`.
Source examined: assigned base `388ff482046a62f813a251208bfeabe7c21808ea`.

**Finding:** keep the safety boundary behind the documentation-only build skip; the evidence does not justify treating every stale digest as a release-qualification defect or repeatedly blocking unrelated work on it. The digest is a cached build-graph approval identity, not a nightly/rc policy attestation. There is one demonstrated operational approval catch this week (two changed target pins), substantial routine reapproval, and repeated inherited-red reporting. These are different benefits and costs.

**Status: mechanism confirmed**, reconstructed from source, Git objects, and stored task reports/events. Recommendation is an operator decision, not a measured net monetary benefit. No product, test, policy, manifest, evaluation record or pin was changed. No build, test host, `-Update`, desktop command, deployment or target-branch push was run. The only authored file is this report.

## 1. What the mechanism actually protects

There are three distinct hashes in the question's examples:

| Identity | Exact input and consumer | What it establishes |
|---|---|---|
| Land-graph digest | Ordinal-sorted Git tree records, with a trailing NUL after each record, SHA-256 in uppercase. Selected records include mode, object type, object ID and path. `LandVerificationBuildGraph.cs:37–56`. | The candidate's build **definitions** match a previously audited inventory. It does not hash every compiled source or qualification input. |
| Nightly `policyHash` | Canonical JSON of `tests/test-execution-policy.json`, excluding its own `policyHash` member. `scripts/lib/nightly-policy.ps1:52–91`. | Integrity/identity of the execution policy. Release authority validates it and records it with the candidate SHA, suites, exclusions and script hashes (`scripts/lib/release-authority.ps1:198–245,315–333`). |
| Live-lane `PinnedInputDigest` / `-PrintInputDigest` | Newline-normalized text of exactly five paths: `nightly-common.ps1`, `nightly-policy.ps1`, `nightly-coverage.ps1`, `tests/test-execution-policy.json`, and `scripts/fixtures/nightly/c487-probe/discovery.diag`. `scripts/test-live-lane-run.ps1:12–35`. | A separate reviewed census-input pin for the offline paid-wrapper admission contracts. This switch is **not** an option of the land-graph script. |

The current land selector includes every extension ending in `proj`, `.props`, `.targets`, `.sln`, `.slnx`, `.slnf`, `.rsp`; names beginning `Directory.Build.` or ending `proj.user`; and `global.json`, `nuget.config`, `packages.lock.json`, `packages.config`, `directory.packages.props` (case rules in `LandVerificationBuildGraph.cs:37–47`). Selection is repository-wide, not just projects in the solution. At the examined base there are 69 selected records.

Ordinary `.cs`, `.ps1`, `.md`, policy JSON, discovery diagnostics, slow-test registry contents, and the land approval record files themselves are not selected. A byte-only/comment edit to a selected project changes its blob ID and therefore the digest; it need not change behavior. A policy/exclusion change can change qualification behavior **without changing this digest**. Conversely, an `InternalsVisibleTo` or test-helper link can change this digest without changing which qualification tests are required. Therefore, “digest changed” means “selected definition records changed,” not “qualification changed,” “a bug was found,” or “the current qualification is invalid.”

### Runtime: mismatch requires a build; it does not itself refuse publication

`LandVerificationPolicy.DecideAsync` (`server/Application/Services/LandVerificationPolicy.cs:11–31`) permits the skip only for `refs/heads/master`, force-build off, no selected-test filter, a valid pinned diff consisting solely of ordinary documentation files, and a candidate tree matching the embedded digest **and count**. Documentation means regular `docs/**/*.md` or root `README.md` (`:35–69`). A code/project change already requires a build at the diff test. For a later docs-only change, stale graph approval gives `unrecognized-build-graph` and falls back to verification. RC targets always require a build before considering a documentation skip (`:16`).

The running server reads the manifest embedded in **its own assembly**, using a lazy reader (`LandVerificationBuildGraph.cs:14–25,59–66`). It does not accept the candidate's manifest as runtime authority. Updating the committed text does not update an already-running server. The candidate is compared against the loaded server's approval. The skip receipt records `graph=<fingerprint>` (`AgentTaskLandingProtocol.cs:314–322`). Thus there **is a runtime consumer besides Merge**.

This is the failure the boundary prevents: a project begins consuming docs/README as build inputs, then a later docs-only land would skip a build merely because its own diff looks like documentation. Looking only at that later diff misses the earlier dependency change. Keeping the old approved fingerprint makes that later land build. This is why a “definition path changed in this land's diff” test is not equivalent to the candidate-tree comparison. The CARD-1385 plan explicitly reconstructed and rejected that alternative (`docs/superpowers/plans/2026-10-21-card-1385-land-graph-fingerprint-plan.md`, D-1/D-3).

### Author-time approval and qualification tests

The records are:

* `server/Application/Services/LandVerificationBuildGraph.manifest.txt:1`: digest, then count.
* `LandVerificationBuildGraph.evaluation.txt:1`: digest, evaluator SDK version, pins-file Git blob ID.
* `LandVerificationBuildGraph.pins.txt`: separately reviewed exact target/environment-element hashes.

`scripts/audit-land-verification-graph.ps1:191–237` first runs the static precheck. `-Check` requires matching manifest/evaluation identities, selectable SDK provenance, and a successful real evaluator. `-Update` also requires successful evaluation before publishing records; it is not a hash-only setter. The precheck checks for documentation inputs before comparing the manifest (`scripts/audit-land-verification-precheck.ps1:1705–1745`). A stale digest can stop `-Check` before the evaluator gets to its pin/differential checks.

Real evaluation does not restore or execute build targets. It evaluates a solution pass, a canary copy with changed documentation, and a second pass, comparing evaluated properties/imports/items (`tools/Antiphon.LandGraphAudit/SolutionAudit.cs:31–88`). Target execution is outside evaluation, so review approves exact bodies separately (`TargetPins.cs:8–10,19–52`). A changed pin refuses even if the digest refresh would otherwise be routine. The orchestration owner describes this boundary at `docs/orchestration-loop.md:2320–2344,2461–2504`.

`LandNonCodeSkipTests.C1385_RegistryMatchesNarrowGraph` hashes the live Git tree, compares it to the embedded manifest and count, and invokes the full `-Check` (`tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:344–399`). The class is Integration/Slow (`:18–20`). A qualification run that includes this method can fail on stale approval or evaluator defects; it is not merely a Merge-only check. Its name does not mean every failure is a digest mismatch.

The release manifest's allowed fields include candidate SHA, `policyHash`, script/build/bundle hashes, suites and exclusions, but no land-graph digest (`scripts/lib/release-gate.ps1:835–839`). No independent nightly/rc attestation field or comparison for this land digest was found in the release/nightly consumers. The run is SHA-bound and may exercise the registry test; that is different from a digest-bound qualification contract. Searches of `AGENTS.md`, `docs/release-gates.md`, and `docs/nightly-watchdog.md` found no additional land-digest gate. The watchdog's script-hash drift detector is separate (`docs/nightly-watchdog.md:13–22`). No external/untracked CI configuration was audited.

### Why records are not automatically rewritten after each land

The existing production landing path decides skip/build and records that decision; it does not call the author-time updater (`AgentTaskLandingProtocol.cs:314–334`). The updater is an audit approval, including human-reviewed target pins, not an acknowledgement that Git moved. Manifest records are intentionally outside their own input set, so ordinary lands do not require a refresh. Only selected-definition edits do. Blindly moving the approved digest after every land would erase the distinction between “current tree” and “audited tree”; the CARD-1664 incident below shows an update can correctly refuse.

Automatically running the **complete existing audit** is different from automatically copying a hash, but it can still refuse and incurs evaluation cost. Neither operation replaces the running server's embedded approval without a new server build/activation. No existing automatic land-record rewrite was found.

## 2. Origin and original failure evidence

Read-only `card.ps1 search 'land graph' -Board Antiphon -All -Json` completed with five results, `truncated=false`, no continuation. The phrase search misses some hyphenated titles, so the original cards were also fetched by number; it is not used as proof of absence. Relevant stored **Cards** rows, all on board `8988ca03-7414-47ad-b0b6-51556c701703`:

| Card / row ID | Original rationale or stored witness |
|---|---|
| CARD-1380 / `44889223-04f4-4f40-97bf-ceedf5e993df` | Operator sought faster lands while 42 drained at roughly 6–8 minutes each. Cold verification compile was reported around 150 seconds; skip docs-only master builds while rc carries the release gate. This is an optimization guard's origin, not a qualification-policy hash. |
| CARD-1385 / `6f0905d5-047f-4406-b1d9-4377da266d67` | The original 292-input constant went stale on unrelated scripts. Safe fallback builds defeated the speedup and tests failed. Narrow to build definitions and retain fail-closed behavior. |
| CARD-1390 / `e65f05c7-43dd-46cf-be25-a0fb50ee54b1` | A mechanical refresh could approve a new documentation dependency. Added docs-input refusal; malformed manifest must force a build. |
| CARD-1400 / `ff41096c-8417-4308-9454-9a47db1a440c` | Stored temp-repository bypasses: case differences, imported props resolved from the wrong directory, escapes/item types. These are audit implementation witnesses, not production defects stopped by a stale digest. |
| CARD-1409 / `125e083f-0831-4fd4-a0ef-109744911f84` | Follow-up on imports, a silently missing import and inaccurate wording. |
| CARD-1483 / `c222c993-0e43-4183-b8b6-806622f73ec0` | Stored MSBuild metadata witnesses selected `docs/data.json` or root `README.md` while the emulator approved. Operator ended repeated emulator repairs; real MSBuild evaluation and exact target-body review became separate boundaries. |
| CARD-1571 / `e754793f-6a52-47e9-822b-fdcaa4e791e7` | 59 actual definitions versus 58 recorded after adding TestTiming.Probe; exact tests expected zero verifier calls but got one. Production safely built. Windows pin-path/root normalization and evaluator timeout also impeded refresh. |

These support a real safety purpose and real maintenance costs. They do **not** establish that an unsafe docs-only production land has actually been prevented. None was found in the examined record history/reports.

## 3. Every re-record reachable from the assigned base

Method: `git log HEAD --reverse` over manifest and evaluation paths; compare selected-definition differences between successive record-writing trees; independently reconstruct each tree's digest with the production selector, ordinal sorting, UTF-8, NUL terminators and SHA-256. Dates below are author dates in UTC, **not inferred deployment/publication times**. Full Git IDs are the immutable evidence references. Two integrated records do not match their own trees; the initial zero placeholder is separately identified.

Classification: **input change** means an actual build-definition change worth reviewing under the existing contract. It is not evidence a defect was caught. **Routine** identifies changes for which only mechanical reapproval is demonstrated. **Catch** is reserved for a demonstrated refusal exposing a missing approval. A test-only project edit may still change the real build/qualification environment; treating all `tests/` paths as harmless would misclassify CARD-1664 and reflection mode.

| Date | Record-writing commit | Underlying definition change since previous record tree | Catch or churn / observed outcome |
|---|---|---|---|
| 10-21 | `553d9511a6abf18661c88aacdbd25d5fb684ea13` | Initial narrow manifest after server embedded-resource addition. 57 rows. | Initial approval, not an incident catch. Preceding selector commit `0a1c35dcbc58bd623385c7b79059ed7fe59c02ea` created a deliberate zero-digest/0-count placeholder. |
| 10-22 | `d2bf22dc09dfa728bee05be172cfdb797bab1148` | New net10 LandGraphAudit project and staged helper/reference in Antiphon.Tests. 58 rows. | Substantive input change; deliberately recorded red witness. Title records refusal of `_StagedHelperFramework` by the then-current allowlist. Audit-development catch, not unexpected production protection. |
| 10-22 | `5161519241a1f781e78307261460b0e7b87617fc` | Link `BuildDriverEnvironment.cs` into server and checkpoint tool. | Real compiled-input change; routine reapproval, no demonstrated docs-dependency catch. |
| 10-23 | `bd215d7b4f559db69fbf4d7363417fce0f2c2467` | Link PhoneHomeTestWebSocket test helper into Antiphon.Tests. | Routine test-helper reapproval. The `docs:` title hides a record write; no unsafe docs dependency shown. |
| 10-23 | `38567feb5c59b0099d10d5f69f60979b0aed9eaf` | TestTiming.Probe project, staged helper and linked timing code. 59 rows. | Actual test-host/build-input change; CARD-1571 primarily restored a stale optimization/test baseline. |
| 10-24 | `2c4c3b72549de6e46d0f9c00280a7f805e2bffb4` | Observability project/solution/references, HostCleanup and new test/tool projects among 11 changed definitions. | Substantive input changes, but recorded **63** rows versus actual **64** in this integrated tree. Refresh did not establish freshness there; no caught dependency defect proven. |
| 10-24 | `5f7d5376f1cdbb06fa19f53686b126621031c839` | FakeProviderOutput.targets imports/body, HostCleanup define removal, Observability test visibility. 65 rows. | Real target/import/build changes; appropriate audit scope. Fresh record, no demonstrated harmful change stopped. |
| 10-26 | `7eb0187eecbdf61ebd82cccb98921406c6bf00be` | LifecycleHost linked cleanup/timing helpers; Card0418 copied fixture content. | Routine test/fixture reapproval; actual compilation/content inputs, not a plain registry or docs edit. |
| 10-29 | `183fefb9a661cbbfbf8036d71628b28eeeb654b9` | LandGraphAudit.Core project/references and linked DispatchWaitDiagnostic helpers. 66 rows. | Real evaluator/test-build changes; record is already stale in integrated tree (details below). |
| 10-29 | `0dbbbe4902fa50efc17c888ab34012640387d37c` | **No definition change** since preceding record tree. | Pure integration-record correction/churn. Actual 66-row digest unchanged; recorded value corrected. |
| 10-30 | `f81b12b969de3839f972e1d735969cef7d6244ee` | NativeLinuxPolicy.Probe / migration-audit projects; linked policy witnesses/resources and native PtyHost targets across test suites. 68 rows. | Substantive qualification/build-input change, including target execution; appropriate audit scope. No separate unexpected defect proven by this refresh. |
| 10-31 | `78fee7464c58412bcfb2b235d6e26e3794573bbb` | New native GitFixture helper project and test staging. 69 rows. | Real test-executable/staging input change; routine approval. |
| 11-01 | `4c0cdfe3a6a73414f277f17ac42e9ffa3505916f` | Roslyn package references and shared LiveClaude source links in two test projects. | Actual package/compiled-input change; routine approval, not a test-registry-only change. |
| 11-02 | `907edaebbd301af95629c31095f1b530803baea2` | CARD-1664 replaces two in-process restore/build target bodies with child-process Exec builds. | **Operational approval catch:** updater exposed two missing re-pins after a clean Review; re-pin and extra Review/Merge required. It caught an approval gap, not the NETSDK1047 bug itself. |
| 11-03 | `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd` | Four test projects add `Compile Include` for Shared/TestTrace.cs (`de27ce2fd8c66d260e83e1cb0f431aae54035d53`). | Routine instrumentation reapproval. Pins/count unchanged; stored updater run approved in 27 seconds. No docs dependency or changed test roster shown. |
| 11-04 | `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a` | Server adds LibGit2Sharp 0.32.0 (`40c2ab63c10dda3887705e80897b37e0dd119e5e`); only selected row changed. | Legitimate package/build-input review trigger, **not a demonstrated defect catch**. Eleven unrelated Merge batches repeated inherited red before this refresh. |
| 11-05 | `28cdefab326782f2c91ca361768a7252dc875ec7` | OTLP exporter package and Shared/TestTraceOtlp.cs link (`f9f5a0bef590a947e59affd073579ada1081b081`). | Actual test dependency/input change; routine approval. Commit body explicitly reports registry failure and runtime fallback until re-record. |
| 11-05 | `d465bd4e363a3fd3ba334f174a5576852ee7e227` | `EnableTUnitSourceGeneration=false` switches Antiphon.Tests to reflection mode (`e0efde3732ece33ed4d43c2cf90517886760f02f`). | **Substantive test discovery/build mode**, not harmless “test-only” churn. Requires reviewed allowlist admission; no defect detected by digest itself is demonstrated. |

Totals after the narrow manifest: 18 record writes, consisting of initial approval, 16 intervals with actual selected-definition changes, and one no-definition-change correction. Two record-writing trees already contain stale records. Five refreshes have this week's 11-02..11-05 author dates. **Zero narrow-digest changes caused solely by ordinary docs, policy JSON or slow-registry contents were found.** This does not mean the 16 changed-input approvals caught 16 defects.

Reconstruction details for the two stale integrated records:

| Commit | Stored manifest | Recomputed tree |
|---|---|---|
| `2c4c3b72549de6e46d0f9c00280a7f805e2bffb4` | `135D4DE07B485AF081DA77B952718629463D2A922347A2DF497BFB92BA21C820`, 63 | `18C658A3386995DB4703CAC7443205F99B2D100983CCB481B77C152A524ED5C4`, 64 |
| `183fefb9a661cbbfbf8036d71628b28eeeb654b9` | `841DD41B42D127CC768ABEC521683496D2C385ECFE753A4E725FEB5ADD54D052`, 66 | `20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6`, 66 |

These are Git-tree facts. Whether the source branches were correctly audited before integration was not determined; the mismatch is not attributed to an updater bug.

### The three original constant refreshes were genuine unrelated-input churn

Before narrowing, the gate selected 292 paths including scripts and `.gitattributes`. The immutable diffs/comments give the causes:

| 2026-10-20 commit | Change requiring re-record | Classification |
|---|---|---|
| `909591a0eda25c5d75a21290a82384885cff9897` | Five newer-master PowerShell changes for candidate identity, fixture/timing and desktop smoke guard. | Churn for docs-build independence; no new documentation input. |
| `638bcac7df623072636bb88d62d6f606f122f214` | Only validate-checkpoint-receipt.ps1 changed, adding failed-report verdict handling. | Churn for this guard, even though the script change matters elsewhere. |
| `8c70d23802bf989e38ed353adb5116b35ed5158a` | Only `.gitattributes`, for Slow-registry merge rules. | Pure registry-related churn for this guard. |

CARD-1385's narrower selector fixed this particular class of churn. It would be inaccurate to cite these as evidence today's digest hashes all registry/policy changes.

### Every reachable `perf(land):` commit

`git log HEAD --grep='perf(land)'` returns eight commits. Path inspection finds exactly one digest-changing definition edit; seven change only C# files and need no independent digest refresh:

| Commit | Subject / digest effect |
|---|---|
| `ae91aeff1327f485632382d9aca23545bcaa0e71` | Read push endpoint once; C# only, no selected path. |
| `40c2ab63c10dda3887705e80897b37e0dd119e5e` | In-process libgit2 reads; server csproj package added, covered by `5a95ae7a...`. |
| `4a4945b6ca8597c737c871089f68f19ee54d936c` | Per-call libgit2 reads, real paths/differential tests; C# only. |
| `c0deda4877e362668c6156004ddbcf2d8e75f6af` | Push-URL cache / ref validation; C# only. |
| `92264fa896e3ebb0beccda8d288eb61d1a6910e6` | Pin already-local remote observation; C# only. |
| `fceeab72a7824deb29dae73afbbd1ec6a075cef5` | Repository handle once per read / hard-link snapshots; C# only. |
| `cc8845f030d934507e7a0d2db7d939be06d9f2bc` | In-process worktree list; C# only. |
| `d71a4d60fdb1e32ed49e7c6486d2fc49e453a885` | One ls-remote for source/target observation; C# only. |

## 4. This week's blocking and repeated checks

Window: 2026-11-02 00:00 UTC through reads at 2026-11-05 19:02–19:07 UTC. Source is the stored **AgentTasks.Result**, **AgentTasks** timestamps and task event rows, fetched with:

```text
GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&since=2026-11-02T00:00:00Z
GET /api/agent-tasks/<full-task-id>
```

The initial list returned 644 scoped tasks. All 277 Merge/Review candidate details were read successfully (88 Merge, 189 Review; no failed detail read). The API has no list cap here; it retains active rows regardless of age and filters settled rows by CompletedAt (`AgentTaskService.cs:2777–2787`). Counts below use dated weekly evidence, not the age of an old active row. 149 retained reports mention the land graph/C1385/graph approval or digest; this is a **report-mention count, not a number of evaluator runs**. Check-event prose saying “unverified digest” was excluded as unrelated noise.

`card.ps1 history CARD-1514` returned 71 revisions and no matching land-graph/digest reason; task reports provided the useful incident evidence. Committed November investigation searches found the differential incident described below, not a digest-mismatch Merge census.

### Counts with separate meanings

* **One hard-stop Merge episode** tied to land-graph approval: batch 20, ultimately Canceled after a Blocked report. It had a digest mismatch, but the decisive refusal was **two changed target pins**. One re-pin Code task, one additional Review, and one replacement Merge followed.
* **No pure-digest-only blocked settlement was established** in the retained weekly Merge/Review reports. This is not a claim that no transient pause ever occurred or that missing/overwritten report history cannot contain another one.
* **Eleven separate Merge tasks** (batches 54–64) repeated the same inherited LibGit2Sharp mismatch and nevertheless succeeded/published. These were repeated red checks, not eleven stopped lands or eleven different bugs. Ten explicitly describe checking both base and final tree; batch 63 records inherited red unchanged without that detail.
* **Four Review reports explicitly document a stale/mismatched graph**: CARD-1664 round 1; its re-pin Review; CARD-1678 inherited-red Review; CARD-1637 inherited-red Review. None establishes a Review stopped solely for the digest. CARD-1664 round 2 additionally handed its known changed digest to an authorized Merge update without claiming freshness.
* **Two dedicated Merge tasks re-recorded and cut rc** (11-03 TestTrace, 11-04 LibGit2Sharp), in addition to the batch-20 retry. The two 11-05 refreshes are Git commits, not two extra blocked Merge tasks inferred from commit titles.

### Operational catch and remediation timeline (all UTC, 11-02)

Stored task `89cb5516-572b-4e55-abeb-7e39fffea014`, Result and Blocked event at **22:04:21.239727**, contains:

```text
digest=7456743D28E951E9A6FBF53874B166750CD2631588969556A891FCC2CA673C22 count=69
manifest does not match the selected graph
...
refused: tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj Target CopyOwnedPtyHostAppHost (changed pin)
refused: tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj Target CopyOwnedPtyHostAppHostForHerdr (changed pin)
```

No records were written by its failed update; no target was published. The previous clean Review `d092ee10-e8f9-4a86-a111-0e7f58e2b500` had completed at 21:59:44 without re-pinning these targets. This establishes an approval gap actually caught after Review.

The extra re-pin Review `04beb796-149f-452a-a0a0-57389986fa79` completed **22:22:47.917175**. Its stored positive/negative comparison independently recomputed all 14 target pins: the old tree refused the two changed pins; the new tree approved. Its `-Check` still stopped on the stale manifest, so it also called the evaluator directly. This separates the pin gate's value from digest equality.

Replacement Merge `072cf7b1-2eec-4260-b2b5-7b2d998a4a04` dispatched **22:24:54.271930**, completed **22:55:08.082459**, wrote `907edaeb...`, and reported successful update/check. Blocked-to-replacement-dispatch span: **20m33s**; blocked-to-success: **50m47s**, including repair, Review and verification. These are observable remediation spans, **not measured human idle/operator-wait time**. The records do not isolate that time.

### Repeated LibGit2Sharp mismatch (11-04)

The production package commit's committer timestamp is **09:13:14**; the refresh commit is **17:28:05**, an **8h14m51s Git timestamp span**, not a proof of publication/activation times. Review `692b70e4-a874-4dbf-9ac3-7a96b7cde2e6` completed at **10:51:34.823164** with a base-versus-change reconstruction: `1470340559` approved `266595CB...`; `40c2ab63c`, base `5449f8f50` and Review HEAD all refused `6536D70E...`. It deferred re-recording to the operator.

Each row below cites that task's stored Result. All are 2026-11-04, Succeeded, with the same 69-row digest `6536D70E936DB342C7254DF59E08062E1E20D968AD4FBC1829839D29B6E53D20`; none performed `-Update`:

| Batch | AgentTasks row ID | Completed UTC | Observed red |
|---|---|---|---|
| 54 | `4c2d90db-5870-4622-91a4-fdb769b622d2` | 11:57:28 | Base/final identical; existing master manifest refusal. |
| 55 | `f2ed8c32-e319-49bd-97ca-3d8799628ce4` | 12:22:09 | Base/final identical; not re-recorded after package change. |
| 56 | `87232051-9b53-424b-9dba-20ee7ca01d49` | 12:53:09 | Base/final exit 1, manifest does not match. |
| 57 | `a42d888f-ec0e-4c1c-90b9-34c2a2cab720` | 13:19:18 | Base/final identical; existing master red. |
| 58 | `6578802d-12e7-4f58-874b-e9e6732de5b1` | 13:35:42 | Base/final identical; known package change. |
| 59 | `d707a486-2efb-4eef-9f9d-0d4b9b012160` | 14:30:59 | Base/final identical; manifest refusal expected. |
| 60 | `a22bdfed-7ce2-48cb-a694-a72360f00dcd` | 14:47:13 | Base/final identical; red already present. |
| 61 | `821a7c5b-d503-4da6-97d3-c9e92cb73488` | 14:59:46 | Base/final identical; expected inherited red. |
| 62 | `4047a80b-b0b7-43e4-8784-f194f4a04180` | 15:44:01 | Base/final exit 1, expected manifest mismatch. |
| 63 | `23fce9a6-5bb2-494f-8426-866364b616c0` | 16:53:11 | Handoff: land-graph red unchanged, 6536D70E/69. |
| 64 | `b3c01691-be50-4462-b1c7-c1a44c322c89` | 17:05:12 | Base/final identical; pre-existing refusal. |

Dedicated task `0e8a1b50-9eb9-4149-a16b-441bad36a514` was created **17:24:35.481720**, dispatched 17:25:15, completed **18:00:46.125014**, re-recorded `5a95ae7a...` and cut rc/2026-11-06. It reconstructed **one** changed selected row (server csproj's package), updater approval and unchanged pins. The 6h33m interval between the Review completion and this task's creation is visible; how much was operator waiting versus other work is unknown.

Other explicit mismatch Review rows:

| Date | AgentTasks row ID | What it establishes |
|---|---|---|
| 11-02 | `bce1e9ec-0961-4b95-b61b-2b3bb622cd3e` | CARD-1664 r1 reported 35C53D93/69 mismatch. Its actual blockers were cold-build NETSDK1047 and extra runtime-pack downloads; do not credit the digest with finding them. |
| 11-02 | `04beb796-149f-452a-a0a0-57389986fa79` | Re-pin Review still got manifest refusal; direct evaluator proved the pin repair. |
| 11-03 | `c3ef4038-6818-46b0-871d-3e2d59565548` | Two inherited failures, C1385 and S5, on recorded 7456743D versus actual 266595CB; branch touched no definitions. Contemporary master refresh clears them on integration. |
| 11-04 | `692b70e4-a874-4dbf-9ac3-7a96b7cde2e6` | The LibGit2Sharp baseline/change reconstruction above; unrelated Review was otherwise clean. |

The 11-03 refresh task `eb43fc0e-fed1-4382-9c64-dde95abb0d81` records a **27-second** successful updater evaluation, only four new TestTrace compile links, unchanged pins and 3/3 registry/real-repo/record smoke tests. The trace commit and refresh have committer timestamps 06:30:23 and 07:30:35 (1h00m12s apart); again, not measured human wait.

### Churn outside digest mismatch: do not mix it into that count

* `docs/investigations/2026-11-03-c1385-differential-none.md:7–17,32–63` reconstructs a Windows qualification failure with **matching** digest/count. Git-ignored E2E TestOutput files entered SDK default `None` globs in the real checkout but not the canary. A clean checkout passed; adding one ignored run.log reproduced refusal. That is an evaluator false positive/maintenance incident, not an input-digest catch. The report's first placeholder repair was subsequently superseded; current code uses differential row tolerance (`SolutionAudit.cs:83`, `docs/orchestration-loop.md:2516` onward).
* Review `48e15e43-f13f-428a-b540-32384727e5d9` (11-02) records missing evaluator, a 5-second copy-budget refusal at load average about 25, then approval on its third check. No mismatch was involved.
* Merge `1b618bf5-7344-4a82-bf98-fee416a32621` records a cached successful `-Check` in **26 seconds**. Rebuilding the evaluator per task was common enough for caller refinement `5fd6ac3e...`, Refined event **11-02 19:05:20.947782**, to request reuse. This is audit execution/rebuild cost, not proof of human approval cost.
* Merge `52890135-8e78-4289-a35c-e6ad5b440c5a` (11-05) stopped over two contradictory required **policy/live-lane** digest values. Its land-graph precheck matched D5B302A9. It is excluded from land-graph mismatch counts.

## 5. Options: evidence-based tradeoffs, not a fix design

“Remove the gate” is ambiguous: removing a blanket workflow check, removing the registry test, and removing runtime graph equality have different consequences. Existing boundaries are cited below so an operator can commission a precise decision. Implementation design/sizing is outside this Investigate stage.

| Requested option | What is kept | What is lost / demonstrated limitation | Existing boundary implicated |
|---|---|---|---|
| (a) Keep as is | Runtime fail-closed docs skip, static/real audit, exact pins, registry freshness failures. Retains the CARD-1664 approval catch. | Ongoing mechanical reapproval, inherited-red test failures, evaluator operational failures, repeated reporting. Five refreshes this week; eleven repetitive Merge reports for one package change. Does not attest qualification policy. | LandVerificationPolicy; full graph script; registry test. |
| (b) Merge re-records a follow-up commit only for allow-listed test registries/policy files, printing old/new | If the existing full updater remains the authority, its pin/differential refusals can still protect approval. Narrower authorization could reduce human handoffs for covered changes. | **As stated it covers no present land-digest trigger:** ordinary registry/policy files are absent from the selector. Broadening that idea to all test csproj changes would include executable targets and reflection-mode discovery, both substantive. Old/new hashes identify a change but do not prove docs independence. | Selector at LandVerificationBuildGraph.cs:37; updater at audit script:218; separate policy/live-lane hash owners. No current allowlisted re-record implementation found. |
| (c) Re-record after every land; digest is detector/alert, not Merge blocker | Candidate SHA and the independent nightly policy/coverage contracts remain. A detector can still report definition drift. Runtime equality, if retained, still safely falls back on mismatch. | Blind re-record is approval without audit; a full updater can refuse, as CARD-1664 did. Neither updates a loaded server's embedded digest automatically. Current seven C#-only perf lands illustrate why most lands have no record change to make. “Detector-only” does not specify whether runtime skip authority is being weakened. | Production landing never calls updater; runtime reads its own embedded manifest. The distinction must be settled before sizing this option. |
| (d) Remove entirely | SHA-bound release qualification and policyHash do not depend on this particular digest field. | Removing equality while still permitting docs-only skips loses the demonstrated dependency boundary: later doc lands can skip despite changed build definitions. Removing the docs-only optimization as well avoids that risk but gives up the saved verification build. Removing only the author-time test/check leaves runtime safely building on stale approval, but loses freshness monitoring and the observed post-Review pin check at that workflow step. | Runtime skip at LandVerificationPolicy.cs:24–27 versus author-time/registry consumers. No unique smallest change exists until “entirely” is disambiguated. |

**Recommendation / deciding evidence:** preserve the audited runtime boundary; reconsider blanket repetitive Merge/Review enforcement. Of the four choices *without additional definitions*, (a) is the only one that unambiguously preserves the observed safety contract. (b)'s named allowlist does not intersect the land hash, (c) conflates detection with approval, and (d) can remove the premise that makes a docs-only skip safe. The value demonstrated this week is **one missed target approval found after clean Review**, not five harmful changes stopped. The counterweight is **eleven repeated inherited-red Merge tasks** and routine test instrumentation/package reapproval. This evidence supports an operator decision about reducing workflow churn; it does not support deleting the whole mechanism as redundant qualification hashing.

A smaller guard that fails only for “qualification-run inputs” is **not established equivalent**. This digest is neither necessary nor sufficient for qualification-policy changes. Its safety question is whether build definitions can make nominal documentation affect a build. Looking only at the current diff or classifying all test files as harmless loses that question. Whether a more precise guard can preserve it requires a separate decision/plan and evidence; this investigation supplies no proven semantic allowlist.

## 6. Reproduction, limits and handoff

Read-only commands executed successfully in the assigned runner worktree:

```text
pwsh -NoProfile -File scripts/audit-land-verification-precheck.ps1 -Check
digest=D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03 count=69
exit 0

pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest
836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7
exit 0
```

Dot-sourcing nightly-common/nightly-policy and calling Get-NightlyPolicyHash on the committed policy gave the same stated/computed value:
`5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4`.
These are three different values over three different input sets. The **precheck** result is not claimed as a successful full evaluator audit.

History enumeration can be repeated with:

```sh
git log HEAD --reverse --format='%H %aI %s' -- server/Application/Services/LandVerificationBuildGraph.manifest.txt server/Application/Services/LandVerificationBuildGraph.evaluation.txt
git log HEAD --format='%H %aI %s' --grep='chore: land graph digest' --grep='perf(land)'
git show <record-commit>:server/Application/Services/LandVerificationBuildGraph.manifest.txt
git diff <previous-record-commit> <record-commit> -- '*.csproj' '*.props' '*.targets' '*.sln'
```

For each tree reconstruction, select rows using the `Includes` predicate cited above, sort with `StringComparer.Ordinal`, hash UTF-8 of `String.Join(NUL, rows) + NUL`, and compare both digest and count. No working-tree mutation or audit update is necessary. The current selector was used for historical reconstruction; the selected recorded values agree except the deliberate placeholder and the two stated stale integrated records. No selected `proj.user` difference was needed to explain these intervals.

Remaining uncertainties:

1. No observed unsafe documentation land actually prevented was found; latent prevention value cannot be counted from refresh commits. Synthetic bypasses and the approval-gap incident establish mechanism, not incident frequency.
2. Report-level census is bounded to this board and this week. Mutable retained task results may omit earlier transient failures; Check snapshots are not a complete transcript. No claim is made about other boards/repos or untracked CI.
3. Human operator-wait time and total audit CPU/build-slot cost cannot be isolated from the available timestamps. Remediation spans and individual 26/27-second runs are reported without attributing entire task durations/costs to the gate.
4. Runtime loaded manifest/version and actual skip counts on live hosts were not measured. A committed refresh does not establish activation; neither a healthy host nor an rc cut proves its embedded digest.
5. The two stale integrated records' source-branch audit provenance was not reconstructed. They demonstrate integration staleness, not which actor/tool introduced it.
6. No full evaluator/build/test was run in this investigation. Qualification pass/failure counts quoted above belong to identified stored reports or existing investigations, not new runs.

**Not done, noted:** smallest implementation/design for automatic re-recording, narrower enforcement or removal is deferred to Decide/Plan under the evidence-only Investigate contract; no fix or approval record was authored, and no further go-ahead was requested for this already-authorized investigation.

--- next stage ---
next: decide
handoff: Decide separately whether to retain the audited runtime docs-only skip boundary and whether to reduce blanket Merge/Review enforcement; one target-pin approval gap was caught, while eleven unrelated Merge batches repeated one inherited digest red. Policy/live-lane hashes are separate and must not be conflated with this digest.
artifact: docs/investigations/2026-11-05-investigate-land-graph-gate-value.md
