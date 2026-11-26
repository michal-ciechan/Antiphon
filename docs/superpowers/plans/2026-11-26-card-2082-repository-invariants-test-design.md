# CARD-2082 · Repository invariant verification

This is the verification appendix to the [landed migration plan](2026-11-26-card-2082-repository-invariants-plan.md). Code imports the Checkpoints table in this appendix. The matching rows in the Plan are the original proposal. S1–S4 and D-1–D-6 below refer to the plan.

The [class timing matrix](2026-11-26-card-2082-class-timing.md) and the measured first-phase and Merge costs below supersede the Plan's preliminary estimates. The matrix records the incomplete groups in both Windows exports and the Linux evidence for each cross-platform failure comparison.

## Verification design

### Inspection

| Bodies read | Production seam and evidence |
|---|---|
| `PlanCheckpointClassSelectionGuardTests`, its `PlanCheckpointClassAudit.Run`, `CheckpointRows`, ledger reader and the miniature checkout in `Audit_reports_a_deleted_class_until_its_row_is_repaired_or_retired` | V-1, V-2. The existing scanner checks names in the first checkpoint table. A split can leave a resolvable parent with some of its previously selected cases missing. |
| `NightlyTestsPlanTests.C1397_RcAntiphonLaunchGroups`, `Native_argv_refuses_ambiguous_and_unsafe_filters`, `NightlyPlanCases.Node`, `RcStoppedRunSeedTests` dispatch steps, `NightlyDispatch.Coordinate`, `RunPrepared`, `WriteSchedule` | V-3. The setup can emit separate discoveries, reorder history and record each actual RunChunk invocation. Prepare alone cannot witness the first launch. |
| `MergeHighRadiusSubsetTests`, its `SelectMergeAsync`, fixed rows and asserted method subset, `ImpactedClasses.Run` and row rendering, `PlanTableImporter.ImportMarkdown` | V-4. Inspect both native-owed and runnable rows and bind the actual imported filter to expanded cases. The top filter represents one project. |
| `RcProjectSelectionTests.Repo` and its baseline Git fixture, `ReleaseCutTests` prior sealed inventory, launch and same-attempt resume, `RcIncrementalCut.Admit/Execute`, `RcChunkReceipt.Match/Reusable`, `RcIncrementalPlan.ExactPartition/PartitionStatus` | V-5. Seed an independently reusable receipt and read the terminal attempt inventory and referenced TRX. Comparing two predicted schedules would miss the carry path in production. |

### Delivery inventory

S1–S4 introduce no session input, broker, outbox or user-message producer. Existing canaries and delivery tests retain their own queue/transcript obligations. Qualification work has a different observable endpoint. Candidate discovery produces per-host case obligations. The Windows coordinator launches the test host, persists the schedule and collects its case results in the attempt directory. The Merge consumer imports and executes project rows. Incremental qualification writes the plan, per-attempt revision and sealed receipt inventory. At rollout match each obligation to its native case result by attempt, SHA, platform/architecture, project, case UID and member digest. Use the existing native-ID/TRX join when the TRX adapter allocates a different testId. Record expected native skips separately. Reject a missing, foreign or interrupted child including after a resume. V-3 through V-5 use controlled child launchers and fixture TRX through the production coordinator/consumer to make ordering, failure and recovery deterministic. They establish wiring. They cannot establish that a Windows child actually passed on the migrated candidate. That requires the first real Full and the per-platform Merge receipts in the rollout section.

### Proves it works now

The five original methods below remain in the checkpoint selection. Keep their stated short class and method names across the move. Use one parameterless TUnit method per row of this table, with independently asserted named subcases inside it. The round 2 historical UID and Merge argument witnesses bring CP-2 to seven results. Round 3 adds the release-cut witness for eight results. Each witness has a method-scoped filter. Keep existing Unit/Integration, platform and process limiter rules.

| ID | Exact class.method | Recipe, variant and decisive oracle |
|---|---|---|
| V-1 | `PlanCheckpointClassSelectionGuardTests.C2082_Repository_readers_belong_to_the_invariant_namespace` | Audit all test projects of the pinned checkout. Walk from declarations through partials, shared includes, fixtures, helpers, data sources and readers to consuming assertions. Compare classified members with source namespace **and** category and the method roster. At S1 require an actual missing tracked-input method, the path and its input chain. After S2 the differences must be empty. Assert nonempty known anchors including the real historical plan and live-lane guards, two different owners of a linked type and both arguments of a parametrized member. Add a new, unrostered reader in the miniature checkout. It must be reported by its method/path even with no lookup entry. In a miniature before/after plan import, resolve a surviving parent, removed argument, exact namespace and retired operand separately. Compare logical case multisets through the migration map, including platform, old row/operand, Min and expected skips. |
| V-2 | `PlanCheckpointClassSelectionGuardTests.C2082_Owned_fixture_reads_remain_in_the_product_lane` | Use two private roots with identical relative policy/plan filenames. Route a helper in the partial declaration to the subject checkout, route its sibling to an owned temp Git tree. Also run a parser on a golden input and a command on fixture files, with a misleading path literal/comment. Feed both to the same audit. Assert the **exact** offender set contains only the subject-checkout comparison. Test namespace-only and category-only variants independently. A missing root or parse failure must produce an error, never an empty clean scan. |
| V-3 | `NightlyTestsPlanTests.C2082_Full_prefix_precedes_ordinary_work_and_stops_on_failure` | Through the production Windows Full planner and coordinator, admit a metadata-bearing listing in two projects with same short type, two argument UIDs, one ordinary test in a mixed suite, another ordinary suite first, and an excluded paid/native case. Cover passthrough and packed plans, no history, an ordinary prior failure and previous-run order. Record actual starts, terminal observations, persisted dispatch ordinals and suite completion. Assert exact disjoint union of candidate IDs once, ordinal zero an invariant, all invariant children successful before the first ordinary start. Inject a first/last invariant failure and an interrupted child. Assert failing case/path in the persisted incomplete result, outstanding obligations, no ordinary start, and clean replay from candidate input. |
| V-4 | `MergeHighRadiusSubsetTests.C2082_Merge_executes_invariants_on_empty_and_unrelated_diffs` | Exercise the normal merge-subset entry, import its rows through the Merge consumer and drive its completion accounting. Use empty, docs-only, unrelated and overlapping subject changes. Use two portable projects with same short class, two argument UIDs, a required native member, a paid exclusion and a behavioral subject control. Compare multiset of admitted logical cases to independently enumerated fixture identities. Portable cases run once per candidate and native requirements remain owed until matching platform evidence arrives. Suppress a child result and assert the Merge cannot complete. Bind the standalone rerun and active Final recipe to the same admitted map and preserve ordinary subject controls. The narrow fixture substitutes discovery/child execution at process boundaries only. |
| V-5 | `RcProjectSelectionTests.C2082_Incremental_requires_fresh_invariant_receipts` | Prepare identical-content prior green rows at B, an unchanged A, an invariant and reusable ordinary neighbor. Exercise shadow selection and production cut Execute, read the sealed results and TRX, then resume an interrupted attempt. Run empty/non-owner diffs, renamed group, old mixed packaging, opaque previous plan and same-SHA/different-attempt variants. Require execute for the invariant, carry for the eligible neighbor, fresh exact case receipts in the current attempt, or Full fallback for opaque membership. Submit a forged carried pass, stale attempt, mismatched digest, platform and missing argument one at a time. Assert the exact invariant obligation remains unsatisfied in each case. |

### Guards the regression

- R-1 (V-1, V-2). A real reader that survives a namespace/category change, or a new reader absent from yesterday's roster, must appear in the offender set. A private-tree read with the same basename must stay out. An empty scan, unknown root or lost selector operand prevents credit.
- R-2 (V-3). Compare the callback trace to the persisted schedule and exact UID partition. An invariant moved behind ordinary work, an early ordinary start, a dropped case or a success after failure fails a named assertion even when planned counts match.
- R-3 (V-4). Compare the expected per-platform ID multiset to results consumed by Merge on each diff. A missing host, duplicate overlapping case, broadened admission or unreceived native member prevents completion.
- R-4 (V-5). A reusable ordinary neighbor establishes that the carry branch is reachable. The same prior pass offered for the invariant never closes its obligation. Inspect the production sealed revision as well as the shadow choice.

### Guard inventory

Each row identifies one independently breakable guard, an exact method via the V table above, and one compiling positive-control change. Bind production file/symbol and exact assertion/variant to the implemented SHA after Code. These are unbound mutation recipes until that binding exists.

| Guard | Plan | PC | Method |
|---|---|---|---|
| G-1 Both namespace and category required | S1, D-1 | PC-1 | V-1 |
| G-2 Detect a previously unknown reader independently of roster | S1, D-2 | PC-2 | V-1 |
| G-3 Private root distinguished at consuming assertion | S1, D-2 | PC-3 | V-2 |
| G-4 Missing/unreadable census cannot pass vacuously | S1 | PC-4 | V-2 |
| G-5 Migrated plan rows retain every surviving logical case and retired operand is exact | S2, D-5 | PC-5 | V-1 |
| G-6 Platform/profile-admitted multiset partition before packing, including passthrough and links | S3, D-3 | PC-6 | V-3 |
| G-7 Streaming first dispatch | S3, D-6 | PC-7 | V-3 |
| G-8 History and previous-order remain within phase | S3, D-6 | PC-8 | V-3 |
| G-9 Failure stops launch and persists outstanding work | S3, D-6 | PC-9 | V-3 |
| G-10 Unconditional project-resolved Merge | S3, D-4 | PC-10 | V-4 |
| G-11 Overlap deduplicated at expanded case granularity | S3, D-4 | PC-11 | V-4 |
| G-12 Owed native child requires native evidence | S3, D-4 | PC-12 | V-4 |
| G-13 Standalone and active Final cover the same invariant members | S3, D-3 | PC-13 | V-4 |
| G-14 Shadow forbids carry for invariant member | S4, D-5 | PC-14 | V-5 |
| G-15 Production and resume require fresh execution | S4, D-5 | PC-15 | V-5 |
| G-16 Sealed receipt matches exact candidate, attempt, platform, digest and members | S4, D-5 | PC-16 | V-5 |
| G-17 Unmappable old mixed group forces Full | S4, D-5 | PC-17 | V-5 |

### Positive controls

The method for each PC is the exact method in the guard inventory. Use a compiling mutation against the implementation or, for a classification defect, the admitted source annotation. Assert the specified red reason. Capture which subcase fired. The PC-1, PC-6, PC-8, PC-9, PC-13, PC-15 and PC-16 variants each require a separate red witness.

| PC | Break | Required failing subcase/assertion |
|---|---|---|
| PC-1 | Move a real standalone fixture to its former namespace while retaining its category. Separately remove only its category. | V-1 live differences include the exact method, tracked input and missing designation in each variant. Keep FQN references compiling. |
| PC-2 | Filter discovered readers to keys already in the roster before computing discrepancies. | V-1 unknown partial-helper reader disappears from the expected offender set. |
| PC-3 | Compare only relative path when resolving the helper's read root. | V-2 exact set gains the owned parser method on the duplicate policy filename. |
| PC-4 | Return successful empty results on a missing scan root. | V-2 missing-root error assertion fails. |
| PC-5 | Resolve a split historical operand by surviving short class alone, omitting its extracted argument. | V-1 before/after mapped row is missing the specified argument while its class and Min still resolve. |
| PC-6 | Collapse the second UID under its class. Separately let an excluded UID enter an unpartitioned owner. | V-3 exact partition loses argument two or gains the excluded UID, respectively. |
| PC-7 | Release the first streamed suite's ordinary child immediately. | V-3 no-history actual first-start assertion names the ordinary child. |
| PC-8 | Sort prior failures globally. Separately replay a saved ordinary-first sequence globally. | V-3 history and previous-order callback traces put the ordinary launch before the final invariant receipt. |
| PC-9 | Continue the ordinary loop after a failed prefix. Separately mark an interrupted prefix complete when persisting it. | V-3 failure trace gains an ordinary launch or incomplete manifest loses the outstanding identity. |
| PC-10 | Gate adding invariant rows on at least one impacted class. | V-4 empty-diff consumer receives fewer than the two portable owner sets. |
| PC-11 | Deduplicate the subject intersection by short class alone. | V-4 overlapping project/argument multiset loses the second qualified identity. |
| PC-12 | Count a required Windows row as fulfilled by the portable host's same-name pass. | V-4 completion succeeds while matching Windows receipt is withheld. |
| PC-13 | Drop one projected linked owner in the standalone renderer, then in the active Final renderer. | V-4 standalone/Final membership comparison misses that project in the corresponding variant. |
| PC-14 | Clear the invariant always-dirty reason in shadow selection for an empty diff. | V-5 shadow decision for the invariant becomes carry. Ordinary neighbor still carries. |
| PC-15 | Permit the prior identical-digest invariant on a new attempt. Separately reuse an older attempt's invariant in the resume path. | V-5 cut or resume trace has no fresh invariant execution for the required attempt. |
| PC-16 | Accept carried disposition, foreign SHA, stale attempt, wrong native platform, mismatched digest, or an incomplete UID set at reconciliation. Mutate one comparison per run with all other fields valid. | V-5 named forged-receipt variant closes the required invariant obligation. |
| PC-17 | Carry an opaque mixed baseline by its old group name/digest. | V-5 opaque-history Full fallback assertion fails. |

Code runs V/R. Ordinary Review judges the proposed PCs against the reviewed code and binds the mutations to reachable assertions. After land Mutation runs **every** listed variant, method-scoped, break/red/restore/green on a bound pack. Record restored SHA, exact filter, per-PC assertion, result and elapsed time. A compilation, fixture or filter error cannot satisfy a PC. Design inventory has 17 guards, 17 distinct PC IDs, zero missing, zero duplicate mappings. No post-land cycles have run in TestDesign.

### Round 3 witnesses

- V-1 and R-1 use the compiled roster checker for all owning assemblies. The class guard exercises a missing owner in each assembly, a constructor-qualified owner, and the retained and extracted methods' compiled execution attributes.
- V-3 and R-2 read the durable missing project-qualified UIDs from the interrupted and missing-argument schedules.
- V-5 and R-4 include `ReleaseCutTests.C2082_Admitted_cut_binds_the_nightly_tests_process`. The test runs the cut, sync and tests commands against an owned Git fixture and stands in for the child processes. It checks the six bindings through the checkout self-update and nightly-tests arguments, refuses a partial invariant receipt, carries the eligible ordinary neighbor, resumes the interrupted attempt from its journal, and exercises an explicit Full cut.

Use `check-roster.cjs --compiled-only <candidate-cases.json> <replay-base-detector.jsonl> <candidate-execution-metadata.jsonl>` to check the current compiled roster independently of the historical source audit.

### Out of scope

Paid providers, live session delivery, rc activation, release publication and a fresh desktop qualification are outside these Code checkpoints. Keep existing per-platform execution obligations in the published run. Qualification scheduling uses the existing child lifecycle. A synthetic child cannot establish historical Windows timing.

### Existing tests affected

In addition to the S2 classes, keep CARD-1782 callers in `AppDbContextModelTests`, `ContractSnapshotTests`, `TestTimingOperationLabelTests`, `RemoteScriptContractTests`, `InboxConversationMigrationTests` and `DelegationDispatchRecoveryBoundaryTests`. These protect migration metadata, public contracts, operation labels, remote script functions and statement budgets. Preserve existing per-change subject coverage when replacing the fixed invariant operands. Inspect `TestClassificationGuardTests`, `TestLaneCategoryGuardTests`, `WindowsUnitPlatformAdmissionTests`, `PlanCoverageBindingTests`, `PlanCheckpointClassSelectionGuardTests`, `LiveLaneRunnerTests`, `DerivedImpactIndexTests`, `RcIncrementalShadowTests` and `ReleaseCutTests` at their existing entry points.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c2082-red/ | red census | `/*/*/PlanCheckpointClassSelectionGuardTests/C2082_Repository_readers_belong_to_the_invariant_namespace` | V-1 R-1 | 1 executed, named missing reader assertion fails | 1 | 11 |
| CP-2 | S4 | tests/Antiphon.Tests -> bin-c2082-green/ | migration controls | `/*/*/(PlanCheckpointClassSelectionGuardTests*)\|(NightlyTestsPlanTests*)\|(MergeHighRadiusSubsetTests*)\|(RcProjectSelectionTests*)\|(HistoricalCheckpointCaseSelectionTests*)\|(ReleaseCutTests*)/C2082_*` | V-1 V-2 V-3 V-4 V-5 R-1 R-2 R-3 R-4 | all eight named methods once, 0 failed, 0 skipped | 7 | 12 |

The importer and CARD-2071 both read the first Checkpoints section. Keep one executable table. Select CP-1 only at the red commit and CP-2 only at the completed implementation. A CP-1 failure remains a red receipt. Its expected result is the named migration defect. CP-2 independently requires green. Inspect the exact eight TRX identities in addition to the historical minimum of seven. Each row has its own build and TRX because its source SHA differs. The estimates are provisional wall costs, based on the larger comparable checkpoint wall excluding admission (634.452 s) and one additional minute for CP-2. The selected total allowances derive to 32 and 34 minutes. Pass explicit `--row-timeout 22m` for CP-1 and `--row-timeout 23m` for CP-2, based on twice the greater observed build/setup residual plus the respective one- and two-minute execution allowances in Cost. This replaces the importer defaults of 33 and 36 minutes without inflating the wall estimates. Report slot wait and inspect actual build and host durations independently. Collect five samples under CARD-2082 and open a Debug investigation if a selection crosses 30 minutes. No total override.

**Landable order.** The documentation and roster may land together through managed plan landing. S1 red is committed and published to the task branch only. S1 + S2 can land as one independently green additive migration only after validating equivalent compiled discovery, the ledger and pins together. Do not split a source move from its selector repair. S3 activating either scheduler depends on S2 having complete membership. Windows Full and Merge planning may be developed independently against the same category resolver. Ship activation of new incremental packaging with S4's never-carry rule or keep incremental admission disabled until S4. Under the authorized two-checkpoint profile, combine S1–S4 for ordinary Review and land to keep CP-2 at its verified SHA. Recompute policyHash first after source/selector and skip/legacy baseline edits, then PinnedInputDigest over the final protected inputs, then regenerate the derived index. A second no-diff regeneration and exact CARD-2071 ledger/operand resolution are S2 acceptance checks.

### Cost

The ordinary Code planning floor is **45 minutes** across two separately committed runs. The isolated B and F timing replays measured 634.452 and 604.650 s from checkpoint start through terminal report. Their displayed slot waits were 0 and 75 s. Host execution took 24.702 and 20.753 s. Subtract those phases to obtain approximately 609.750 and 508.897 s for the isolated build and other checkpoint preparation/closure. Twice the larger residual, rounded up, supplies a provisional **21 minutes per isolated row** under CARD-2082. CP-1 adds one minute of host execution and CP-2 adds two. Keep queue admission and any first-time launcher bootstrap separate. Two historical diagnostic rows support these estimates. Collect the first five migrated measurements to set p95. The 17 post-land PCs specify 28 compiling defects (including six receipt variants). Reserve **2 minutes of execution per red/restore-green pair** on the exact method indicated by its V ID. Mutation uses the same prepared worktree across a defect and restoration. Allow a provisional **3 minutes per incremental build** on red and restored source. Mutation floor is 56 execution minutes, plus 168 build/setup minutes for 56 builds. Combined estimated Code + Mutation cost is **269 minutes** before queue waits. Method filters use `/*/*/<class>/<exact method>` from V-1–V-5, with per-PC evidence and separate clocks. This is distributed work across separately commissioned cycles, not a four-hour Code row. Debug any selection or batch forecast above 30 minutes. Split independent cycles into booked groups below that limit.

The second Code host consolidates seven methods into one build/discovery. At the measured Linux main-host startup of about 20 seconds, four avoided launches save about 80 seconds of startup, before any avoided builds. The new prefix's two-minute first-failure goal starts at first runnable host. Under CARD-2082 collect the first five real warm/cold invocations per applicable platform with case counts, first launch/failure, final receipt time, queue/build/discovery/body/teardown, host load and native waiting. Keep large historical parent totals separate from measured wall. Reprice each row and method at about 2× comparable p95. A >30-minute prefix or Merge needs a Debug task with SHA, host, selection and timing, while retaining all cases.

## Candidate dispositions at the integrated pin

The [JSON roster](2026-11-26-card-2082-candidate-roster.json) covers all 513 previously undecided declarations and all eight newly signalled declarations. The 352 earlier guard/split decisions remain in the first two tables of the [inventory](2026-11-26-card-2082-invariant-inventory.md). Treat both sources as inputs to V-1. The unique identity in this appendix is the project assembly, fully qualified declaring type and source method. A method MOVE moves that method and all its admitted arguments. A method SPLIT extracts the named checkout assertion and leaves the behavioral case running. A fixture is SPLIT if it contains a mixed method or both retained and moved methods.

The roster's `earlierMixed` section adds method decisions for 246 of the 271 original mixed inventory rows. The 25 already mapped rows retain their inventory keys. The section's `source` pins the original declarations and numbered assertion sites. Pass a detector JSONL from that pin as the third argument to the [roster checker](2026-11-26-card-2082-check-roster.cjs), after the inventory-pin and integrated-pin JSONL files. Pass the compiled candidate case JSON as argument four, the detector JSONL from the replay base as argument five, and the candidate assembly metadata JSONL as argument six. Each metadata row has an `owner` (assembly/declaring type/method) and an `attrs` array with effective method and inherited class execution attributes, including their arguments. The checker compares every MOVE and SPLIT with its application status, expanded compiled cases and exact owner. Deferred entries need a phase, a reason and at least one current ordinary case in the owning assembly. Retired entries need a reason and zero current cases at the retired method. SPLIT destinations inherit execution attributes and categories. The E2E host-path extraction explicitly takes Unit admission for its tracked-input assertion and retains the original host test's OptIn admission. The replay-base census verifies the four dedicated live Claude fixtures on their native lane.

| Source census | Original candidates | Additional candidates | Whole fixtures | Split fixtures | Stay fixtures |
|---|---:|---:|---:|---:|---:|
| Integrated `77ba2e55239dcbbf9e14e9b25e8d1b754e2ee994` | 513 / 5,612 methods | 8 / 150 methods | 2 | 26 | 493 |

Across these 521 types, 20 methods MOVE, 39 methods SPLIT and 5,703 methods STAY. The two whole moves are LandDeliveryWaitRoutingTests and BuildOutputNestingTests. They check the shipped E2E wait call sites and the built test host's own output layout respectively. Keep each guard's dedicated negative controls alongside it. LiveLaneFingerprintTests is an example of SPLIT at the *method* level. Its driver runs synthetic Git vectors and two cases against HEAD and the tracked declaration. Only the latter two establish a checkout invariant. PgQpsTests uses shipped SQL as input to command-behavior tests. StagedHelperBuildTests executes the shipped MSBuild target on temporary projects and asserts their build results. Keep those two fixtures in their current lanes.

### How to read and verify a method

The roster declares its column order. Each tuple identifies a file and inclusive source-line range at the census pin. `inputSite` identifies the arrangement, `readSite` identifies a selected read or names the relevant checkout input, and `oracleSites` identifies comparison or assertion-delegate locations. `reasonOrExtraction` either names the STAY reason in the top-level dictionary or describes the extraction. Open the cited bodies to review the decision. For example, a helper can derive a cwd from the checkout, then pass that cwd to a real command whose output is the subject. Treating every reachable File.Read as an invariant would move the wrong cases.

The last four indices dereference `fixture.sourceSets`. A value of −1 denotes the empty set. Reader and oracle entries are zero-based indices into `sourceMembers`, whose column names are in the file. Reader entries retain path hints, syntax signals and read lines, including false-positive basename matches. Route entries are pairs of caller and callee indices. Each pair follows a bound test-source symbol or a fixture/data-source setup entry. Setup entries identify those implicit roots. These are source paths through possible calls. A timed, explicitly gated or OS-ineligible path still requires its original admission. For a helper or command implemented outside the test-source graph, the selected input and extraction note name the boundary to inspect. The representative oracle and the full method body remain available where a library assertion, custom helper or embedded script is outside that graph.

Each method marked SPLIT also has an entry in `fixture.checkoutOracles` naming the specific comparison sites to extract, including assertions reached through PowerShell and Node drivers. For example, RollingProductionMountTests checks eleven bind entries in the checked-in Compose capture before exercising variants in its owned runtime. Only that cardinality comparison moves.

For independent checking, rerun the [detector](2026-11-26-card-2082-invariant-detector.md) on the stated Git tree, select the inventory's final 513 keys plus the newly positive keys and compare exact sorted (project, type, method) multisets. Then check every tuple's file, method name, bounds and disposition, every trace index and the fixture rollup. The [roster checker](2026-11-26-card-2082-check-roster.cjs) takes the inventory-pin JSONL and integrated-pin JSONL as its two arguments. It reads the test and script bodies at the recorded Git pin regardless of which branch is checked out. The comparison passed for all 5,762 methods, 39 split-assertion maps and 7,368 source member references. The compiler-syntax input has 4,916 methods without inline/data-source attributes, 840 with inline arguments and six with method data sources. `sourceCaseLines` resolves each exact attribute expression at this pin. The historical case ranges are zero-based inclusive indices into the committed two-project discovery identified by SHA in the roster. They resolve 6,145 exact compiled nodes to the matching project, type and method. Empty historical ranges mean there is no matching entry in that capture. An attribute count or an older UID cannot establish current native admission. Before S2 takes credit, bind generated/inherited and argument-expanded cases using both platforms' before/after admitted discovery, as required by V-1.

### Change from the inventory

The exact tracked paths, added/retired types, changed method-name sets and changed scanner summaries are in `reconciliation`. The framework-wide comparison is 2,300 → 2,308 declaring types and 1,138 → 1,145 signalled types. Across 57 changed test-tree files, nine classes appear and one disappears. The eight newly signalled classes are the two desktop-slot fixtures, DesktopWinPhase1SecretsTests, the three snapshot/registration fixtures, and the two server2 Postgres fixtures. DesktopWinAclOsTests adds one zero-signal native ACL case against a temporary directory. It stays with the native suite. Sixty-nine retained types have a changed scan summary (paths, source locations or direct method names). A line move alone confers no new obligation.

| Retained declaration with changed method set | Disposition of the delta |
|---|---|
| LinuxRunnerCustodyLifecycleTests | The new host-match case compares constructed process and boot identities. STAY. |
| CheckpointWarmBuildTests | The new exclusive-create probe reads its own temporary seal. STAY. |
| ImpactedClassesSelectorTests | The WindowsPhoneHome path must select the two named witnesses through the shipped impact map. MOVE that assertion under the existing split. |
| AntiphonCliLauncherTests | Extract the four retired operator-file absence checks and AssertSnapshotLauncher's tracked byte/source checks from the argument and command cases. Keep the CLI failure/exit-9 control and argument forwarding with the private checkout. |
| PosixHarnessToolTests | Four cases retired here. Two snapshot cases moved to their own fixtures. The subsequent W1 and W6 cutover cases exercised the one-off PowerShell driver. The surviving parameterized HiddenPath case proves behavior on its own temporary PATH. STAY. Record all four retired identities. |
| RefreshGithubTokenScriptTests | Five methods replaced the retired remote-command declaration. They exercise fake SSH, payload/error handling and redaction. STAY. The original live docs scan in Heredoc_fed_docker_exec_keeps_stdin_open still warrants extraction. The two old PS-text checks now exercise command behavior. Split out the explicit compiled UploadPayload text assertion from the command call/output assertions. |

The refresh from `aa1d0f9f7` to `3c2cb4cd4` retired the W1/W6 case bodies, removed the W6 Windows skip and rc admission row, and adjusted ReleaseGatePlatformTests to the three remaining Linux policy exclusions. The policy and derived-index changes travel with that retirement. Master then advanced to `77ba2e552`. The parking tests now push from test-owned repositories, check the exact remote SHA and enforce refusal when another writer wins. Their shared template and database setup serve behavioral assertions. The method set and all 388 timing identities are unchanged by that park publication change.

The shared historical-migration helper now seeds a reversible schema using the predecessor's compiled model. Follow that data into the assertion. A model used to write compatible seed rows stays fixture setup. A test explicitly comparing the compiled predecessor or snapshot gets a metadata-only assertion. Preserve the populated database roundtrip and the native skip/eligibility rules. Include the revised baseline, skip roster, live-lanes and impact policy/index files from the tracked delta when reconciling the selector pins.

## Qualification timing and cross-platform outcomes

Both copies of the 495,901-byte Windows export matched SHA-256 `509f4227eb2cb474bcbe6095ea4efc1bed5a852d0c4632d3dbbeedd1a6f16a12`. The [per-class matrix](2026-11-26-card-2082-class-timing.md) gives counts, total and maximum seconds, Linux receipt provenance and the cross-platform failure verdict for all **388 assembly/class rows in ten projects**. The 352 original declarations add eight linked compilations. The integrated roster adds 28 whole or split declarations. Of the resulting 91 whole and 297 mixed parents, 352 assembly/class rows belong to Antiphon.Tests.

| Run | Windows qualification at export | Coverage of 388 classes |
|---|---|---|
| B · `b8cbfb24` · `2fd61b643ea4231fa732cd4969b5284d43b33f39` | Terminal `TESTS`, about 14 hours. Schedule complete, coverage incomplete. 1,298 known native groups and 1,298 TRX files. Groups 129 and 142 flagged incomplete. | 370 observed, 18 absent |
| F · `1fe763a9` · `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | **Active, partial**. Schedule complete, no final summary or coverage verdict. 1,320 known groups and 40 TRX files. 1,279 missing-TRX-only groups, one missing with additional flags, one planned-count mismatch. | 53 observed, 335 absent |

Across both Windows exports, 372 distinct rows have results, 51 occur in both and 16 have neither. All observed Windows results are timed. The [native group ledger](2026-11-26-card-2082-class-timing.md#native-groups-with-incomplete-coverage) retains the precise flags. The [Windows join](2026-11-26-card-2082-join-windows-timing.cjs) verifies the digest and uses assembly plus fully qualified class. Run the join from the repository root.

```sh
node docs/superpowers/plans/2026-11-26-card-2082-join-windows-timing.cjs /work/evidence/invariant-qualification/card-2082-windows-class-timing.json
```

### Did any proposed class fail only on Windows?

**Yes. At B, two Windows-failing methods passed in the Linux replay.** A third failure came from a Windows-only native case. The other five Windows failures reproduced on Linux at their corresponding pins. Eight Windows failures occurred in six SPLIT parents. No whole MOVE fixture had an observed failure. A verdict concerns the same case at the same revision. It does not establish that an operating system caused the failure.

| Windows run and parent | Exact case comparison | Effect on the first phase |
|---|---|---|
| B · PlanCoverageBindingTests | Both historical binding assertions failed on both platforms. Windows 48.712 and 84.433 s, Linux 76.878 and 113.077 s. | Both assertions enter. [Plan evidence](2026-11-26-card-2082-repository-invariants-plan.md) |
| B · IncrementalSelectionSoundnessTests | `Recorded_owner_map_status_matches_the_committed_map` failed on Windows in 2.463 s. The B Linux replay passed in 5.6669723 s. | The tracked owner-map comparison enters. [Group 142](../../investigations/2026-11-25-why-missed-group-142.md) |
| B · CheckpointAdmissionContractTests | `C1616_admission_timeout_publishes_readable_nonexecuted_receipt` failed on Windows in 10.1 s. The B Linux replay passed in 3.1687626 s. | The timing-sensitive admission behavior stays in the checkpoint suite. [Group 112](../../investigations/2026-11-25-why-missed-group-112-admission-reason.md) |
| B · NightlyNativeOwnershipTests | `C1039_DescendantExit` failed on Windows in 7.039 s. Its child process uses Windows Job Objects. The source excludes that case on Linux. The F Linux CP-4 TRX records `NotExecuted` for the same name. | The native process lifecycle remains a Windows obligation. [Group 135](../../investigations/2026-11-25-why-missed-group-135-nightly-native-ownership.md) |
| F (partial) · LiveLaneInventoryTests | `EveryLiveGateHasDisposition` and `ExplicitGatesAreNeverFullRequired` both failed on both platforms. The F Linux replay took 11.1240109 s and 11.122693 s, respectively. The F Windows aggregate is 44.9372894 s for the five parent cases. | Both gate comparisons enter. [Group 141](../../investigations/2026-11-26-why-missed-group-141-live-gate-disposition.md) |
| F (partial) · CardCleanupSchedulingSchemaTests | `C1033_AdditiveUpgradePreservesHistory` failed on Windows in 75.2494592 s and on Linux in 42.045 s. | The predecessor seed and populated upgrade remain behavioral. [Group 86](../../investigations/2026-11-26-why-missed-group-86-predecessor-seed.md) |

The focused B replay executed two cases, two passed, zero skipped. The F replay executed two cases, two failed at the named gate assertions, zero skipped. Both clean-source, verified-build [checkpoint lines](2026-11-26-card-2082-class-timing.md#exact-pin-linux-replays) identify the unchanged historical commits, filters and native TRX. The F export can acquire further failures as groups finish.

### Measured first-phase and Merge costs

The table sums recorded native case durations, including recorded skip durations. A class can occur once in each Linux cohort. Each Linux entry uses one qualifying receipt per class and cohort. The filter, commit and available TRX digest are in the [receipt ledger](2026-11-26-card-2082-class-timing.md#linux-receipts). Across the three Linux cohorts, 117 distinct classes have a receipt. Partial filters and differing SHAs preclude treating these cohorts as one run.

| Platform and cohort | Whole parents / case-seconds | Mixed parents / case-seconds | All observed case-seconds | Passed / failed / not executed |
|---|---:|---:|---:|---:|
| Windows B | 89 / 204.7598233 | 281 / 10,013.2560968 | 10,218.0159201 | 5,102 / 5 / 17 |
| Windows F (partial) | 13 / 21.6948885 | 40 / 1,134.3759030 | 1,156.0707915 | 326 / 3 / 0 |
| Linux B | 5 / 279.1507453 | 12 / 548.758207 | 827.9089523 | 201 / 2 / 1 |
| Linux F | 4 / 304.3983764 | 11 / 933.5781438 | 1237.9765202 | 134 / 3 / 11 |
| Linux at other historical SHAs | 28 / 247.1341277 | 79 / 8698.0719912 | 8945.2061189 | 1432 / 0 / 9 |

**Windows first phase.** B observed 501 nodes in whole parents, for 204.760 serial case-seconds. The two binding failures and owner-map case add about 135.608 s of identified invariant work from mixed parents. Together these account for **340.368 s (5.673 min) of historical serial work**. The longest of those identified cases took 84.433 s. Moving all observed B parents wholesale would have charged 170.300 serial minutes including their behavioral work. F has **21.695 s** in 83 observed whole-parent nodes, plus the invariant cases within mixed classes. Its observed parent total is 19.268 serial minutes. Removing just the 75.2494592 s behavioral seed leaves 1,080.8213323 s (18.014 serial min) of observed parent work before the other splits. Of 297 mixed parents only 40 are observed in F. Preserve that incompleteness.

**Every Merge.** Two timed portable checkpoints at `0b07453e48b7eaab1985ec0f7537b056a4b80ed3`, run `20261126-065938-5e6e`, measured 163.4574948 s for CP-1 (105 passed, one skipped) and 219.3259445 s for CP-9 (two passed). Running those selections in sequence costs **382.7834393 s (6.380 min) of test-host wall**. At F, run `20261126-074134-4c4f` measured 247.5882643 s for CP-1 (105 passed, one skipped) and 383.5132033 s for its wider CP-2 (58 passed), **631.1014676 s (10.518 min)** in sequence. At B the older six-class CP-1 alone took 260.6094736 s (102 passed, one skipped). The earlier pair shared a 246.4022752 s build with zero row wait. Its build plus both host intervals took 629.1857145 s (10.486 min). F shared a 691.4273992 s build, giving 1,322.5288668 s (22.042 min) plus 300 s of recorded row waits. These sums cover those phases only. Some selected tests remain outside the new category.

Linux also has directly attributable case-body work. At B the five observed whole parents, both binding failures and the owner-map replay sum to 474.7727176 s. At F the four whole parents contribute 304.3983764 s. The two historical binding cases in CP-2 account for a further 484.6754755 s (342.1187637 and 142.5567118 s). Including the 22.2467039 s live-gate pair gives 811.3205558 s directly attributable at F. Whole-parent coverage differs by pin. These sums are serial case-seconds across receipts.

The export aggregates by parent. Native results from the migrated selection will supply individual split-method timings, end-to-end wall and p95. At rollout use the first invariant launch through the last owed receipt, including the native lane for Merge. Record build/slot wait separately and sample five warm/cold invocations as specified under Cost. Retain the two-minute first-failure target and the 30-minute Debug threshold. The ordinary Code checkpoint budget is listed separately under Cost.
