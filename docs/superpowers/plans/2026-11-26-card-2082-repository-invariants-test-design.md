# CARD-2082 · Repository invariant verification

This is the verification appendix to the [landed migration plan](2026-11-26-card-2082-repository-invariants-plan.md). Code imports the Checkpoints table in this appendix. The matching rows in the Plan are the original proposal. S1–S4 and D-1–D-6 below refer to the plan.

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

All five methods below are **proposed**. Keep their stated short class and method names across the move. Use one parameterless TUnit method per row of this table, with independently asserted named subcases inside it. This keeps CP-2 at five results and makes each mutation method-selectable. Keep existing Unit/Integration, platform and process limiter rules.

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

### Out of scope

Paid providers, live session delivery, rc activation, release publication and a fresh desktop qualification are outside these Code checkpoints. Keep existing per-platform execution obligations in the published run. Qualification scheduling uses the existing child lifecycle. A synthetic child cannot establish historical Windows timing.

### Existing tests affected

In addition to the S2 classes, keep CARD-1782 callers in `AppDbContextModelTests`, `ContractSnapshotTests`, `TestTimingOperationLabelTests`, `RemoteScriptContractTests`, `InboxConversationMigrationTests` and `DelegationDispatchRecoveryBoundaryTests`. These protect migration metadata, public contracts, operation labels, remote script functions and statement budgets. Preserve existing per-change subject coverage when replacing the fixed invariant operands. Inspect `TestClassificationGuardTests`, `TestLaneCategoryGuardTests`, `WindowsUnitPlatformAdmissionTests`, `PlanCoverageBindingTests`, `PlanCheckpointClassSelectionGuardTests`, `LiveLaneRunnerTests`, `DerivedImpactIndexTests`, `RcIncrementalShadowTests` and `ReleaseCutTests` at their existing entry points.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c2082-red/ | red census | `/*/*/PlanCheckpointClassSelectionGuardTests/C2082_Repository_readers_belong_to_the_invariant_namespace` | V-1 R-1 | 1 executed, named missing reader assertion fails | 1 | 4 |
| CP-2 | S4 | tests/Antiphon.Tests -> bin-c2082-green/ | migration controls | `/*/*/(PlanCheckpointClassSelectionGuardTests*)\|(NightlyTestsPlanTests*)\|(MergeHighRadiusSubsetTests*)\|(RcProjectSelectionTests*)/C2082_*` | V-1 V-2 V-3 V-4 V-5 R-1 R-2 R-3 R-4 | all five named methods once, 0 failed, 0 skipped | 5 | 5 |

The importer and CARD-2071 both read the first Checkpoints section. Keep one executable table. Select CP-1 only at the red commit and CP-2 only at the completed implementation. A CP-1 failure remains a red receipt. Its expected result is the named migration defect. CP-2 independently requires green. Inspect the exact five TRX identities in addition to Min. Each row has its own build and TRX because its source SHA differs. The ordinary derived time allowance is `max(30, 2 × (4 + 5) + 10) = 30` minutes. No override.

**Landable order.** The documentation and roster may land together through managed plan landing. S1 red is committed and published to the task branch only. S1 + S2 can land as one independently green additive migration only after validating equivalent compiled discovery, the ledger and pins together. Do not split a source move from its selector repair. S3 activating either scheduler depends on S2 having complete membership. Windows Full and Merge planning may be developed independently against the same category resolver. Ship activation of new incremental packaging with S4's never-carry rule or keep incremental admission disabled until S4. Under the authorized two-checkpoint profile, combine S1–S4 for ordinary Review and land to keep CP-2 at its verified SHA. Recompute policyHash first after source/selector and skip/legacy baseline edits, then PinnedInputDigest over the final protected inputs, then regenerate the derived index. A second no-diff regeneration and exact CARD-2071 ledger/operand resolution are S2 acceptance checks.

### Cost

The ordinary Code floor is **9 minutes**, including both isolated builds. CP-1 allows 3 minutes of warm build and 1 minute of execution. CP-2 allows 3 minutes of warm build and 2 minutes of execution. Separate queue waits and cold bootstrap in reporting. The 17 post-land PCs specify 28 compiling defects (including six receipt variants). Reserve **2 minutes of execution per red/restore-green pair** on the exact method indicated by its V ID. Allow **3 minutes per incremental build** on red and restored source. Mutation floor is 56 execution minutes, plus 168 build/setup minutes for 56 builds. Combined estimated Code + Mutation cost is **233 minutes** before queue waits. Method filters use `/*/*/<class>/<exact method>` from V-1–V-5, with per-PC evidence and separate clocks. This is distributed work across separately commissioned cycles, not a four-hour Code row. Debug any selection or batch forecast above 30 minutes. Split independent cycles into booked groups below that limit.

The second Code host consolidates five methods into one build/discovery. At the measured Linux main-host startup of about 20 seconds, four avoided launches save about 80 seconds of startup, before any avoided builds. The new prefix's two-minute first-failure goal starts at first runnable host. Under CARD-2082 collect the first five real warm/cold invocations per applicable platform with case counts, first launch/failure, final receipt time, queue/build/discovery/body/teardown, host load and native waiting. Keep large historical parent totals separate from measured wall. Reprice each row and method at about 2× comparable p95. A >30-minute prefix or Merge needs a Debug task with SHA, host, selection and timing, while retaining all cases.

## Candidate dispositions at the integrated pin

The [JSON roster](2026-11-26-card-2082-candidate-roster.json) covers all 513 previously undecided declarations and all eight newly signalled declarations. The 352 earlier guard/split decisions remain in the first two tables of the [inventory](2026-11-26-card-2082-invariant-inventory.md). Treat both sources as inputs to V-1. The unique identity in this appendix is the project assembly, fully qualified declaring type and source method. A method MOVE moves that method and all its admitted arguments. A method SPLIT extracts the named checkout assertion and leaves the behavioral case running. A fixture is SPLIT if it contains a mixed method or both retained and moved methods.

| Source census | Original candidates | Additional candidates | Whole fixtures | Split fixtures | Stay fixtures |
|---|---:|---:|---:|---:|---:|
| Integrated `aa1d0f9f7102abf12535dcb39647179b1259f4cb` | 513 / 5,614 methods | 8 / 150 methods | 2 | 26 | 493 |

Across these 521 types, 20 methods MOVE, 39 methods SPLIT and 5,705 methods STAY. The two whole moves are LandDeliveryWaitRoutingTests and BuildOutputNestingTests. They check the shipped E2E wait call sites and the built test host's own output layout respectively. Keep each guard's dedicated negative controls alongside it. LiveLaneFingerprintTests is an example of SPLIT at the *method* level. Its driver runs synthetic Git vectors and two cases against HEAD and the tracked declaration. Only the latter two establish a checkout invariant. PgQpsTests uses shipped SQL as input to command-behavior tests. StagedHelperBuildTests executes the shipped MSBuild target on temporary projects and asserts their build results. Keep those two fixtures in their current lanes.

### How to read and verify a method

The roster declares its column order. Each tuple identifies a file and inclusive source-line range at the census pin. `inputSite` identifies the arrangement, `readSite` identifies a selected read or names the relevant checkout input, and `oracleSites` identifies comparison or assertion-delegate locations. `reasonOrExtraction` either names the STAY reason in the top-level dictionary or describes the extraction. Open the cited bodies to review the decision. For example, a helper can derive a cwd from the checkout, then pass that cwd to a real command whose output is the subject. Treating every reachable File.Read as an invariant would move the wrong cases.

The last four indices dereference `fixture.sourceSets`. A value of −1 denotes the empty set. Reader and oracle entries are zero-based indices into `sourceMembers`, whose column names are in the file. Reader entries retain path hints, syntax signals and read lines, including false-positive basename matches. Route entries are pairs of caller and callee indices. Each pair follows a bound test-source symbol or a fixture/data-source setup entry. Setup entries identify those implicit roots. These are source paths through possible calls. A timed, explicitly gated or OS-ineligible path still requires its original admission. For a helper or command implemented outside the test-source graph, the selected input and extraction note name the boundary to inspect. The representative oracle and the full method body remain available where a library assertion, custom helper or embedded script is outside that graph.

Each method marked SPLIT also has an entry in `fixture.checkoutOracles` naming the specific comparison sites to extract, including assertions reached through PowerShell and Node drivers. For example, RollingProductionMountTests checks eleven bind entries in the checked-in Compose capture before exercising variants in its owned runtime. Only that cardinality comparison moves.

For independent checking, rerun the [detector](2026-11-26-card-2082-invariant-detector.md) on the stated Git tree, select the inventory's final 513 keys plus the newly positive keys and compare exact sorted (project, type, method) multisets. Then check every tuple's file, method name, bounds and disposition, every trace index and the fixture rollup. The [roster checker](2026-11-26-card-2082-check-roster.cjs) takes the inventory-pin JSONL and integrated-pin JSONL as its two arguments. It reads the test and script bodies at the recorded Git pin regardless of which branch is checked out. The comparison passed for all 5,764 methods, 39 split-assertion maps and 7,372 source member references. The compiler-syntax input has 4,918 methods without inline/data-source attributes, 840 with inline arguments and six with method data sources. `sourceCaseLines` resolves each exact attribute expression at this pin. The historical case ranges are zero-based inclusive indices into the committed two-project discovery identified by SHA in the roster. They resolve 6,147 exact compiled nodes to the matching project, type and method. Empty historical ranges mean there is no matching entry in that capture. An attribute count or an older UID cannot establish current native admission. Before S2 takes credit, bind generated/inherited and argument-expanded cases using both platforms' before/after admitted discovery, as required by V-1.

### Change from the inventory

The exact tracked paths, added/retired types, changed method-name sets and changed scanner summaries are in `reconciliation`. The framework-wide comparison is 2,300 → 2,308 declaring types and 1,138 → 1,145 signalled types. Across 48 changed test-tree files, nine classes appear and one disappears. The eight newly signalled classes are the two desktop-slot fixtures, DesktopWinPhase1SecretsTests, the three snapshot/registration fixtures, and the two server2 Postgres fixtures. DesktopWinAclOsTests adds one zero-signal native ACL case against a temporary directory. It stays with the native suite. Fifty-seven retained types have a changed scan summary (paths, source locations or direct method names). A line move alone confers no new obligation.

| Retained declaration with changed method set | Disposition of the delta |
|---|---|
| LinuxRunnerCustodyLifecycleTests | The new host-match case compares constructed process and boot identities. STAY. |
| CheckpointWarmBuildTests | The new exclusive-create probe reads its own temporary seal. STAY. |
| ImpactedClassesSelectorTests | The WindowsPhoneHome path must select the two named witnesses through the shipped impact map. MOVE that assertion under the existing split. |
| AntiphonCliLauncherTests | Extract the four retired operator-file absence checks and AssertSnapshotLauncher's tracked byte/source checks from the argument and command cases. Keep the CLI failure/exit-9 control and argument forwarding with the private checkout. |
| PosixHarnessToolTests | The two removed snapshot cases no longer exist on this declaration. Preserve tool-free and native ACL behavior through the new snapshot tests and their platform policy. Record the retired identities when reconciling selectors. |
| RefreshGithubTokenScriptTests | Five methods replaced the retired remote-command declaration. They exercise fake SSH, payload/error handling and redaction. STAY. The original live docs scan in Heredoc_fed_docker_exec_keeps_stdin_open still warrants extraction. The two old PS-text checks now exercise command behavior. Split out the explicit compiled UploadPayload text assertion from the command call/output assertions. |

The shared historical-migration helper now seeds a reversible schema using the predecessor's compiled model. Follow that data into the assertion. A model used to write compatible seed rows stays fixture setup. A test explicitly comparing the compiled predecessor or snapshot gets a metadata-only assertion. Preserve the populated database roundtrip and the native skip/eligibility rules. Include the revised baseline, skip roster, live-lanes and impact policy/index files from the tracked delta when reconciling the selector pins.

## Qualification timing and cross-platform outcomes

The timing join has **388 project/class rows in ten projects**, including the 28 newly identified whole or split fixtures. There are 91 whole and 297 mixed parents. The two shared classes account for eight additional compilations. The main Antiphon.Tests assembly owns 352 of the 388 rows. A result joins on the full assembly and class in the corresponding run. A missing class, an untimed result, a native test skipped on Linux and a passing test are four different values.

The supplied location for the desktop export, `/work/evidence/invariant-qualification/card-2082-windows-class-timing.json`, was absent in the assigned runner volume. The complete B/F counts, total and longest case seconds per class, and the resulting observed portion of each run's first-phase cost are pending that input. The [projection](2026-11-26-card-2082-join-windows-timing.cjs) verifies the original 495,901 bytes, SHA-256, both run IDs and both candidate SHAs before producing those rows. It includes raw outcome names, untimed counts, coverage flags, group issues and class absence. F always carries the partial-at-export flag. Existing Linux observations in the inventory reference narrow, historical selections. A Linux pass is comparable only if the corresponding case was admitted and executed on that source. For a class with unpaired failures the comparison remains unknown.

```sh
node docs/superpowers/plans/2026-11-26-card-2082-join-windows-timing.cjs /work/evidence/invariant-qualification/card-2082-windows-class-timing.json
```

### Observations recoverable without the export

These are **individual case observations**. A row with two known results does not describe every result for its class. Times are measured seconds. B ran for approximately 14 hours at `2fd61b643ea4231fa732cd4969b5284d43b33f39`. F was still running at `0cfc4ac1ffb41f5af159a0c1db423fe119950267`.

| Antiphon.Tests class | B | F (partial) | Same-case Linux evidence | Prefix relevance |
|---|---|---|---|---|
| PlanCoverageBindingTests | Two failures, 48.712 and 84.433 s. Known sum 133.145 s, longest known 84.433 s. | Unknown | Both failed at the B pin in 76.878 and 113.077 s. | Both historical-binding assertions enter the prefix. Their B wall contribution under the observed durations lies between 84.433 and 133.145 s depending on overlap. |
| LiveLaneInventoryTests | Both named gate checks passed. | Both failed, group 141. | Both assertions reproduced on Linux at F. | Both enter. The before/after difference concerns the gate catalog. [Investigation](../../investigations/2026-11-26-why-missed-group-141-live-gate-disposition.md) |
| CardCleanupSchedulingSchemaTests | The predecessor-seed case passed. | One known failure, 75.249 s. | The same case failed in 42.045 s on Linux at F. | Preserve the upgrade/seed as behavioral work outside the metadata prefix. [Investigation](../../investigations/2026-11-26-why-missed-group-86-predecessor-seed.md) |
| IncrementalSelectionSoundnessTests | Committed owner-map failure, 2.463 s. | Unknown | Historical Linux pass and a separate adversarial matcher reproduction. The same-case, same-SHA OS comparison is open. | The actual map check enters. [Group 142](../../investigations/2026-11-25-why-missed-group-142.md) |
| CheckpointAdmissionContractTests | Admission timeout failed, 10.1 s. | Unknown | Linux reproduced the assertion with a controlled slow acquisition on a different commit. | Keep the timeout behavior in its owning suite. [Group 112](../../investigations/2026-11-25-why-missed-group-112-admission-reason.md) |
| NightlyNativeOwnershipTests | Descendant-exit failure, 7.039 s. | Unknown | The real Windows child case is excluded on Linux. | Keep the real native lifecycle invocation in its native lane. [Group 135](../../investigations/2026-11-25-why-missed-group-135-nightly-native-ownership.md) |

The B checkpoint binding evidence and source references for all three exact-pin reproductions are recorded in the [plan](2026-11-26-card-2082-repository-invariants-plan.md). The class export can establish how many results of each outcome were observed. It has no per-case duration or failed UID for a mixed parent. If an aggregated failing class has no paired case evidence, resolving its OS differential or isolating the moved assertion's time requires the filtered source TRX rows. An outcome of NotExecuted alone does not establish an expected skip. Join the admitted native UID to its platform skip policy for that verdict.

### First-phase and Merge cost

| Evidence base | Whole parents | Mixed parents | Limits |
|---|---:|---:|---|
| Earlier Windows cost entries explicitly marked measured | 6 unique classes, 85 serial seconds | 70 unique classes, 5,599 serial seconds | Across 719 historical nodes in all. Five additional mixed classes add 736 s to the Plan's 4,948 s. These are rounded historical model inputs. The other 304 declaring types have no measured entry here. [Class costs](../../../scripts/lib/nightly-class-costs.json) |
| Most recent successful provenance-matched Linux class selections in the inventory | 16 assembly/class observations, 374.444 case-seconds | 33, 1,275.395 case-seconds | The receipts total 1,649.838 s and cover the filters recorded per row. Some filters cover only part of a class. [Inventory](2026-11-26-card-2082-invariant-inventory.md#linux-receipt-keys) |

**Windows Full.** As a sensitivity calculation, retain 10% or 30% of the 5,599 mixed-parent seconds from the older Windows data. With the 85 whole-parent seconds, those 76 priced parents contribute 644.9 or 1,764.7 case-seconds. Allow 1 or 3 seconds per remaining project/class row (312 rows), and 20.28 seconds of serial startup for each of ten hosts, borrowing the observed Linux startup. On a warm build with serial bodies, the resulting illustrative first-phase estimates are **19.3 and 48.4 minutes**. At two effective concurrent bodies throughout the body interval, keeping startup serial, those scenarios are **11.4 and 25.9 minutes**. Neither retained proportion, marginal unpriced cost, Windows startup nor effective concurrency has been measured for this selection. The two workloads from B/F must be priced separately. Substitute the joined whole and mixed observed seconds and outstanding counts into the same formula, then replace assumptions with per-case and per-host observations. Full queue, compilation, initial discovery and delayed native admission are additional time. A first failure from an early cheap guard can arrive well ahead of phase completion.

**Every Merge.** Two existing warm Linux selections give **6.4–9.8 minutes** in sequence. The earlier planning allowance of 15–25 minutes includes roughly a minute of graph evaluation and about three minutes for nine extra host startups. Charging each of the 28 additional fixtures 1–3 seconds of *unoverlapped* work adds 28–84 seconds, giving an estimated **15.5–26.4 minutes of warm portable execution**. The increment charges no extra host launch because the ten owners already exist. Total Merge completion also waits for admitted native-only receipts. Where platforms overlap, use the later completion after shared preparation. Where the scheduler serializes them, add their intervals. Its end-to-end p95 and native wait remain unmeasured. Use fresh project-level phase wall and receipt timings at rollout. The 48.4-minute Windows scenario crosses the repository's 30-minute Debug threshold. Investigate the scan, build and host overlap while retaining the required cases.
