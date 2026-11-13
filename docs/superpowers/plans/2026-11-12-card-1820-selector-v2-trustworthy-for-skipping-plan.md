# CARD-1820: Selector v2 trustworthy for release skipping

Plan and verification design, written 2026-11-13 UTC for the 2026-11-12 brief. Assigned source: `cc48c70050a2932eddf4abe18cea8659d478c33e`. This supersedes the release-use assumptions and remaining S2-S5 dispatches in [the original plan](2026-11-09-selector-v2-derived-index.md). Landed S1 remains useful ordinary test selection; its successful exit is not a completeness certificate.

**Deliverable:** a conservative, candidate-bound class selection with reasons and a completeness bit. CARD-1836 may use a complete result as one input to its carry decision. An incomplete, missing, stale, unsupported or incorrectly bound result requires Full. This plan does not activate incremental qualification, admit the old Windows run, or promise the caller's approximately 245/1,163 chunk estimate.

The brief folds verification into this dispatch and requests `next: code`. The verification design and closed checkpoint list below are executable scope for those Code tasks. Mutation execution is waived by the operator on 2026-11-12; ordinary failing regression witnesses remain required.

## Evidence and ground truth

Read CARD-1820, board Antiphon, revision 23, and both investigation reports. Source reports:

- `origin/feat/card-task-62e3aea4`, `docs/investigations/2026-11-12-card-1820-selector-v2-accuracy-on-real-diffs-smallest-path.md`, investigation commit prefix `4e8710dc7`.
- `origin/feat/card-task-b9444a8c`, `docs/investigations/2026-11-12-card-1836-incremental-rc-qualification-result-ledger-boundary-chunks-dirty-marking-release-baseline.md`.
- [CARD-1836 original plan](2026-11-09-card-1836-incremental-rc.md), especially D-3/D-4 and S2-S8. Its original blanket contract-change rule is still the default until a versioned scoped comparator is admitted by CARD-1836.

The following are dependency omissions, not assertions that the historical changes caused those classes to fail. No new tests, mutations, qualification or timing samples were run to produce this plan.

| Card/earlier plan assumption | Code and measured evidence at the assigned source | Required disposition |
|---|---|---|
| S1 is a complete reverse index | `DerivedImpactIndexBuilder.cs` retains 25 classes per path, omits four allowlisted paths, and keeps only eight sample omitted names. Current index has 2,332 path edges and 152 capped/allowlisted records. | Keep ordinary cost controls separate from an uncapped release graph. No cap, byte limit or omitted edge may silently preserve release completeness. |
| A successful empty/partial result can justify skipping | Cut replay has 459 omitted path/class pairs; post-cut 433. `SessionMessageQueueServiceTests` is absent despite registering/resolving the changed queue service. `SeatOccupancyAttentionTests` is absent for the changed AttentionService. | Reconstruct full membership; select every affected project class conservatively. Missing or unbounded inputs make completeness false. |
| One-hop type references close fixtures | `AgentTaskPoolTests -> TestDbFixture -> TestDbFixtureLifecycle` and the assembly hook are missed. The current hop reads strings from a directly referenced file, not a transitive type graph. | Follow dependency closure to a fixed point; assembly hooks and shared lifecycle inputs affect the whole test assembly. |
| All C# and executed scripts are indexed | Source roots are only `src/`, `server/`, `tests/`. `scripts/lib/Antiphon.Scripts/Baseline.cs` selects zero; `ReleaseSelectionCliTests` is absent from both replays. Basename script calls and dot-source imports are not closed. | Include compiled script/tool projects and script invocation/import edges, including the harness that resolves basenames. |
| Exact path strings suffice | `NativeLinuxPolicyMatcherParityTests` embeds a command containing `nightly-coverage.ps1`; whole-string equality misses it. `TestSelectionTests` and release-authority harness callers are also missed. | Parse constant command/path expressions and include transitive script inputs; unresolved expressions broaden or refuse completeness. |
| S2-S5 are implemented | `keywordIds` and `rosterIds` are always empty; the keyword/registry files and planned receipt/ledger tests do not exist. | Execute the revised S2-S5 tasks below; these improve positive selection and accountability, not negative proof by themselves. |
| Range binds the selector to the candidate | Cut-on-cut and current-on-cut each return 193 classes but disagree on `CapacityRecoveryTaskTests` versus `C0747QueueRowExpiryTests`. Missing index is accepted as empty. | Pin baseline, candidate, algorithm, universe and every input; read Git blobs, not arbitrary current files. |
| Short class names and filters identify release coverage | `ProcessSpawnLimitTests` occurs in several projects; raw-string `ProbeTests` enters the regex census. `-MergeSubset` omits the derived payload. | Use project + full type name; actual declarations only. Release mode rejects MergeSubset and method-only approximations. |
| Hash changes inherently invalidate every test | Current policy hash covers the entire policy except `policyHash`; catalog hash covers the entire catalog except `catalogHash`. Current selector hash covers only two scripts; runtime hash is `global.json`, not installed SDK. | Preserve document integrity, add per-consumer semantic projections, and separately close the execution harness. Unknown/global changes still force Full. |
| The example cut can skip under CARD-1836 D-4 | First range adds git-in-process to required suites and changes policy/catalog hashes. Second range leaves the five currently hashed contract files unchanged but changes nightly execution sources. | First range needs scoped compatibility proof; second is not automatically compatible. Do not suppress either reason based on equal test blobs. |
| Linux evidence can admit the old Windows result | CARD-1836 S1 is a library without qualification callers; the old run lacks a proven ledger/bootstrap contract. | Windows provenance, current universe, outcomes, expected skips and failure history remain CARD-1836 obligations. |

### Root-cause closure matrix

`ReleaseImpactGraph` below is a new, uncapped relation, distinct from the existing schema-1 ordinary index. Precise syntactic edges explain positives; evaluated project/assembly closure supplies conservative coverage. A missing precise edge must never remove a conservative edge.

| Edge/root cause | Fix and conservative boundary | Witness / owner |
|---|---|---|
| Cap and allowlist | Materialize all reverse memberships. Global build inputs expand to all dependent test projects. On size/work limits, emit `complete=false` with the reason and Full fallback, never truncate a complete list. | V-2; S1R2 |
| DI registration and factories | A changed implementation, registration extension or composition root selects all test assemblies transitively referencing its owning project, including host/HTTP fixtures declared as runtime consumers. Constant `AddX<I,T>` edges add explanations. Unknown factory/plugin ownership is incomplete. | V-3; S1R3/S1R4 |
| Interface-to-implementation, inheritance, attributes, ambiguous symbols | Evaluate project references and select all classes in downstream test assemblies; include interface/enum/struct/attribute declarations and all candidates for ambiguous source names. Do not infer a negative from failed simple-name lookup. | V-3; S1R3 |
| Reflection | Constant type/assembly names and assembly scanning bind to every possible owning assembly; include all its downstream consumers. Computed loads use a reviewed bounded runtime-input declaration or `complete=false`. Text search is corroboration, never proof of absence. | V-3; S1R3 |
| Configuration/appsettings/environment | Include evaluated Content/None/EmbeddedResource/AdditionalFiles, config variants and settings binding files in each owning project closure. Explicit test/runtime consumers cover externally supplied files/environment names; missing values or unbound variants are unknown. No secret values in receipts; use approved opaque version identities where needed, otherwise do not carry. | V-4; S1R4 |
| Fixtures, test helpers, global/assembly hooks | All compiled helper source belongs to its project. A change in a test project conservatively selects its whole class universe and downstream projects; hooks cannot require explicit references from every test. External fixture/content manifests close runtime reads. | V-4; S1R4 |
| Scripts invoked by tests | Resolve `ScriptHarness` basenames under `scripts/`, constant Join-Path/PSScriptRoot/RepoRoot composition and quoted paths in command strings; PowerShell AST follows dot-source/import/Add-Type/process dependencies. Include `scripts/lib/**/*.cs`, tools and shell/node child inputs. Dynamic invocation or ambiguous base marks incomplete unless all possible targets are bounded. | V-5; S1R5 |
| Generated code | Record evaluated generated Compile inputs, generator/analyzer assemblies, packages and AdditionalFiles plus build properties. Changes dirty the owning project and downstream consumers; missing generated provenance/evaluation makes incomplete. Never treat ignored `obj/` bytes as committed truth. | V-3; S1R3 |
| Project references and conditional imports | Use evaluated Compile/ProjectReference/import/resource items for the requested target framework, RID/configuration/platform; retain conditions and input blobs. Union old and new reverse closures for deleted/moved dependencies. Unresolved imports, conditional Windows-only evaluations or external references make incomplete for that target. | V-3, V-6; S1R3/S1R6 |
| Deleted/renamed paths, candidate binding | Use NUL-delimited old/new diff records, old graph for removed paths, new graph for added paths, both for edits. Match immutable full SHAs and input-set digest before selecting. | V-1, V-6; S1R1/S1R6 |

No reflection-, config- or generated-code-specific historical failure was demonstrated in the reports. Their synthetic witnesses address known unsupported mechanisms, not invented measured regressions.

## Decisions

### D-1. Separate ordinary selection from release evidence

Add opt-in `-ReleaseEvidence -BaselineSha <40hex> -CandidateSha <40hex> -TargetPlatform linux|windows -Json` to `scripts/impacted-classes.ps1`. Require explicit endpoints and reject combinations with `-MergeSubset`, `-Path`, `-FileList` or ambiguous `-Range`. Existing ordinary invocations retain their output/hand-table contracts. A release call with no usable graph returns a structured incomplete envelope and Full disposition; invalid CLI/syntax/JSON refuses. Both paths deny carry.

Rejected: making a successful ordinary `-Json` result implicitly authoritative; or removing the ordinary cap and making every pre-land edit run a monorepo-sized selection. Release and ordinary cost budgets have different purposes.

### D-2. Prefer broad, sound coverage to speculative precision

For a product-project source change, select every class in test projects in its reverse transitive project/assembly dependency closure. For a test-project helper change, select that test project's whole class universe. Add runtime-consumer edges for tests which launch a separate host or read another project's files. This intentionally selects much of `Antiphon.Tests` while the server remains monolithic. CARD-1729's future project extraction may improve savings without relaxing this proof.

Fine type/call edges add reasons; they cannot subtract from project closure in this version. Assembly references alone do not cover reflection, scripts, fixtures or external processes; D-3 closes or refuses those. Rejected: attempting a general sound .NET/PowerShell call graph in one 60-minute task; retaining a one-hop graph; using keywords as a negative dependency certificate.

### D-3. A closed input inventory, not an optimistic completeness flag

Add a versioned graph manifest containing tracked input paths/blobs, project membership/evaluated items, full class declarations, reverse dependency edges and bounded runtime inputs. Add `tests/selector-runtime-inputs.json` for runtime dependencies not derivable statically: consumer project/full class or assembly scope, input paths/globs, kind, rationale and validating witness. A glob is expanded against BOTH trees. Every matching new/deleted file changes the manifest. Entries cannot say `ignore unknown` or confer completeness by assertion alone.

An independent census checks unresolved source/project/runtime constructs, reader coverage and unclassified changed paths. Unknown affected ownership, parse/evaluation failures, missing inputs, submodules without content identity, dynamic external dependencies or resource limits set global `complete=false`. Conservatively expanding a *known bounded* assembly to all its tests is complete; silently assuming a dynamic read is bounded is not. Unknown unrelated paths need a proven no-consumer classification, otherwise Full. Documentation is not automatically harmless: tests read docs.

Cache graph evaluation only by immutable input-set + algorithm + target properties/SDK digests. Generate once per endpoint and reuse within a replay run. `tests/derived-impact-index.json` remains the ordinary schema-1 output; release manifests are generated ignored artifacts, never committed result payloads. Existing builder code may expose a pre-cap relation, but no release consumer may deserialize a capped schema-1 index as complete. Rejected: raising the 2 MB limit or cap and calling the resulting graph complete.

### D-4. Bind evidence without a self-referential SHA

Use Git blob reads at immutable baseline/candidate endpoints. The algorithm has its own full source SHA/digest, separate from the source being analyzed; the new algorithm can analyze an old tree. Hash the complete inventory of input path/blob pairs, including policy/registry/keyword/runtime manifests and evaluated build inputs. Exclude only the graph's own generated output and receipt from that digest, avoiding a self-reference. The envelope records candidate/base SHAs separately, so a digest can be regenerated at the final candidate without embedding that candidate's future commit in a tracked generated file.

Analysis of current working bytes grants no release completeness. Verify tracked cleanliness for any build/evaluation consuming disk, compare its inputs to the pinned blobs, and retain build provenance. Old/new graph omissions, unsupported schemas and source drift fail closed. Record algorithm/schema transitions as global incompatibility until CARD-1836 has reviewed the comparator version; a new graph file alone is not a selector-code change.

### D-5. Class identity and platform are explicit

Identity is `(repository-relative csproj, fully qualified class name)`, with declaration paths and platform membership. Preserve duplicate short names in different projects/namespaces. Derive declarations using Roslyn syntax/compiled metadata rather than raw-string regex. Retain full current test universe and its digest, including parameterized/generic declarations; CARD-1836 joins classes to its frozen discovered method/parameter identities and refuses unmatched/ambiguous members.

A selected class dirties all of its current identities on that platform. No one-method LandGraph cost exception in release mode. Ordinary hand method rows remain unchanged. Filters are only execution conveniences; their suffix expansion is compared with actual identity membership. `windowsFilters` from ordinary selection is not a Windows universe. Linux can evaluate Windows source/metadata conservatively, but cannot credit Windows execution or claim an unresolved platform graph is complete.

### D-6. Hashes retain integrity; scoped compatibility needs a separate proof

Keep existing policy/catalog hash algorithms, pin validation and historical raw hashes. Add a versioned *compatibility projection* per identity/class and suite, not a special case which ignores a changed hash. D-8 defines the limited admitted projection. Global semantic fields and unknown properties stay incompatible. A raw-file reader test is always dirtied when the file changes even if that change is irrelevant to a particular execution projection.

Rejected: dropping policy/catalog from the contract, excluding only a known changed field ad hoc, or treating JSON equality after deleting arbitrary fields as proof. Until CARD-1836 admits this comparator and both endpoint projections exist, its original Full-on-hash-change rule remains.

### D-7. Accuracy bar is zero misses against a named independent proxy

Set **N=50 first-parent master commits**, pinned at verification start and written as full parent/candidate SHA pairs. Include merges as first-parent diffs; never choose commits based on whether the selector understands them. Replay the two full historical pairs below as well. Require zero false negatives for every pair, not just an aggregate average. Mutation executions remain waived; the independent conservative dependency oracle in the verification section is the authorized cheaper proxy. It is not measured behavioral coverage, and zero proxy misses does not prove universal dependency soundness.

Also require a synthetic bounded two-project case in which one unaffected project remains unselected with `complete=true`. Report real selection fraction, fallback frequency and unproven cases. Selecting Full for everything is safe but is not evidence of useful selective operation. No savings threshold is invented for the real cuts.

### D-8. Scope policy/catalog changes only where consumer equivalence is demonstrated

The scoped projection is an additive contract version, owned here as a pure comparator; admission and scheduling belong to CARD-1836. Inputs are validated old/new policies/catalogs plus the complete old/new universe, dependency graph and actual reader inventory. Each projection records the source JSON pointers, resolved values and reader/comparator version; digest equality alone is insufficient without that binding.

| Current hashed input | What it covers today / who reads it | Proposed compatibility scope |
|---|---|---|
| `policyHash` | Canonical whole `tests/test-execution-policy.json` except its self hash. Nightly planner/dispatcher/coverage, release authority and policy witness tests read it. | Preserve raw hash. Resolve applicable profile, suite, project dispositions, safe environment, exclusions, watchdogs and prerequisite/census semantics before projecting. The table below limits which changes may be scoped. |
| Catalog hash and policy's catalog pin | Canonical `tests/test-selection-policy.json` except `catalogHash`; sets, rules, edges, prerequisites, activation and schema. `test-selection.ps1`, compiled TestSelection/SelectionCatalog, project selection and catalog witness tests read it. | Validate pin at each endpoint. Hash effective sets/rules/edge closure and prerequisites for each consumer, evaluated on both trees. Rule additions/removals affect old OR new targets. Global matching/precedence/activation/schema changes are Full. |
| `selectorHash` | LF-normalized digests of `scripts/select-test-rosters.ps1` and `scripts/lib/test-selection.ps1` only. | Keep legacy value; add release graph/comparator and actual script/compiled helper closure. An algorithm/reader change is Full in v1. Do not claim the current two files cover the harness. |
| `runtimeHash` | Blob text of `global.json`. | Continue global invalidation; also compare actual SDK/runtime/toolchain, target/build properties and immutable external fixture/environment identities under CARD-1836. Linux SDK identity is not Windows SDK identity. |

Scoped cases are a closed list; every field is enumerated and classified by the implementation, including existing `projects`, `reducedDispatchPolicy`, `scriptCensus`, `safeEnvironment`, `watchdogs`, `projectDispositions` and every catalog key. No ignored catch-all or schema-unknown success branch:

1. A suite-local command/filter/environment/exclusion or prerequisite changes: dirty that suite's affected identities, all old/new dependent suites, and all tests that read the changed input. A changed whole-suite execution command dirties the whole suite. An exclusion addition/removal changes the expected universe/skip classification; it cannot manufacture a carried pass.
2. A required suite is added: it has no eligible baseline rows and all of it is dirty. Rebuild the global current-universe coverage obligation. Unrelated existing suites can be compatible only if their projected semantics and dependencies are equal. A removal is a recorded obligation change, never credit for executing the removed tests.
3. A catalog set/rule/edge changes with complete old/new ownership: dirty the union of old/new affected sets and transitive prerequisite/dependent consumers. A changed wildcard is evaluated against both complete trees. Unknown target or deleted owner is Full.
4. Any field feeding dispatch concurrency/order/custody, shared setup or coverage/outcome interpretation is global unless a reviewed reader proves a narrower scope. V1 treats schema, activation, safeEnvironment, reducedDispatchPolicy, shared watchdog/census semantics and selector/harness code changes as global. Descriptions/reasons are *not* presumed irrelevant: permit scope only after the reader inventory proves their use, and still dirty document-reader tests.
5. Policy/catalog test classes that read the entire raw document or validate its hash get the full blob in their own inputs. Their chunks run on every document change. They do not by themselves require re-executing every other suite.

Safety argument: for every carried current identity, the old and new versions of **every** execution-affecting reader input must be equal under a versioned, exhaustive projection, the test/product/runtime input closure must be equal, and the current universe/skip obligation must be accounted for. If a global reader consumes a changed value, that premise fails globally. Unknown fields/readers cannot pass the argument. Merely observing identical selector output is insufficient.

For `3db1a43b0..56e96eabf698`, the added git-in-process suite and pin update are candidates for the scoped rule, not an approved compatibility result: enumerate the full JSON diff and harness changes first. For `56e96eabf698..cc48c7005`, changed `nightly-run-impl.ps1`, `nightly-coverage.ps1` and compiled nightly sources remain global in this version unless independently proven otherwise. The old Windows run's missing provenance remains disqualifying regardless of either comparison.

**Operator decision O-1, only if proof fails:** keep Full (the default), or explicitly commission a revised execution-contract proof for the named unclassified field/reader. No slice may substitute a guessed safe scope or accept missing provenance. This plan chooses Full now, so O-1 does not block implementing or reviewing these tasks. A request to carry despite that unresolved proof requires an explicit operator decision recorded on the card; it is not delegated by the seven-seat priority instruction.

### D-9. Retain the remaining original features, with bounded scope

S2 keeps the original three new triggers: generated-text newline assertions, ProtectedData, and TestSessionLogRoot. Existing ParallelLimiter/timing/native-platform triggers stay in the hand table. Add/remove risky syntax needed by release closure is handled by the graph; a token list is not exhaustive proof. Guards use fixtures and parse the relevant assertion/call context rather than flagging arbitrary comments/raw-string fixture text. A guarded Windows call is not executed on Linux.

S3 starts with original D-7 roster owners, plus all currently tracked roster files and embedded enumerations discovered during its census (`expected-duration-roster`, linux roster, policy/catalog, selector tables, StrictClasses and nightly source roster). Resolve additions, deletions, moves and class/method membership changes; also select the guard when the oracle itself changes. Registry completeness is limited to its audited census; runtime unknowns still follow D-3. The solution-shape guard is a structural parser check, not a build-race proof.

S4 extends the old four-field paste to carry the complete bound selection artifact and explicitly distinguish selected, executed, carried and owed. S5 keeps historical note grandfathering, with exact paths fixed at its base SHA, and validates machine-readable proposed rule IDs against the actual hand/keyword/roster rules. A `cannot` reason records debt; it never grants completeness. Stage bundles stay byte-identical.

### D-10. Placement, publication and limits

Read `GET /api/runner-defaults` (revision 2, global runner `server2`) and `GET /api/session-runners` on 2026-11-13. The catalogue then advertised one eligible Linux runner, one eligible Windows runner and a retired temporary Linux runner. Those observations are not routing pins or capacity grants. Dispatch portable tasks using live defaults, omitting `-Runner` and `-Platform`; only W1 requires `-Platform Windows`. Do not embed an address or fleet location. Re-read effective concurrency settings before dispatch; the seven-seat target cannot override stage/host caps.

No slice changes session waiting/parking behavior. No live run, host, process, Docker volume or production checkout is touched. Commit failing witnesses before fixes, commit before checkpoint execution, and push every meaningful slice on its assigned task branch. This plan's requested unsuffixed path is a link document to this canonical `-plan.md` artifact.

## CARD-1836 handoff contract

Proposed format: `antiphon-release-impact-v1`; strict duplicate-key/schema validation, deterministic ordering and canonical digest. The contract is an artifact, not a public HTTP DTO or ledger schema migration.

| Member | Required meaning |
|---|---|
| `repository`, `baselineSha`, `candidateSha`, `algorithmSha`, `algorithmDigest`, `schema` | Exact immutable identities; reject another repo/range/algorithm or unsupported version. |
| `target` | Platform, profile, framework/RID/build properties and available toolchain identities. An unverified component is explicit, never inferred from the analyst's host. |
| `inputManifestDigest`, `baselineGraphDigest`, `candidateGraphDigest`, `universeDigest` | Bind complete path/blob/evaluation inputs and the current project-qualified class universe. Include dependency artifacts and algorithm version. |
| `classes[]` | Project + full class name, declaration paths, platform membership and nonempty reasons. Release entries mean whole classes, not prefix filters. |
| `reasons[]` per class | Kind (`declaration`, `type`, `projectClosure`, `runtimeInput`, `script`, `fixture`, `policy`, `keyword`, `roster`, `global`), changed input, old/new side, dependency chain or conservative scope, and rule version. Retain all root causes without duplicating the class. |
| `complete`, `incompleteReasons[]`, `fallback` | Boolean; false includes at least one stable reason and `fallback=Full`. Missing bit, cap/size truncation, stale/missing graph or unsupported target is not true. Malformed envelope is equivalent to no usable evidence. |
| `compatibility` | Raw policy/catalog/selector/runtime identities plus scoped projections and global incompatibility reasons. `complete=true` does not cancel a forced-Full reason. |
| `legacySelection` | Ordinary kinds/classes/derivedClasses/keywordIds/rosterIds/skippedByCap/owed/filters/windowsFilters for audit only. Never used as the release universe. |

CARD-1836 must validate all bindings against the actual candidate and current discovery before using the envelope. `complete=false` or a validation failure sets forced Full **before any chunk is skipped**. When true, selected classes dirty all their identities; unselected identities are merely candidates for carry. CARD-1836 still checks complete membership, equal complete input digests, same-platform baseline provenance, terminal green/expected-skip, current skip policy, unresolved failure/fix containment, SDK/age rules and historical-run admission. Every current identity must have exactly one executed-or-eligible-carried outcome. A partially supplied identity list is a refusal, not a partially green chunk.

CARD-1820 P2 supplies producer/consumer contract fixtures and a strict adapter callable by CARD-1836; it does not modify `rc-ledger.ps1`, nightly scheduling or the compiled release-selection Full-only branch. CARD-1836 owns integration parity across PowerShell and compiled fast paths, baseline bootstrap, runtime/age policy and coverage credit. Its revised Plan must reference this format and explicitly admit D-8 before replacing D-4's blanket hash rule. Pending CARD-1836 work is not an excuse to emit an optimistic completeness bit.

## Verification design

Ordinary red witnesses precede each behavior change. Reproduce the existing omission with real selector/graph calls on a fixture tree, then fix it; a build failure or missing test API is not an assertion red. New APIs may start with a test-only invocation adapter which exercises the old selector and asserts the missing outcome. Store the expected assertion failure and tested SHA. Do not execute mutation PCs under this brief's waiver.

New classes below live under `tests/Antiphon.Tests/Scripts/`, are Unit-category unless explicitly stated, and have four named test methods each unless the table says otherwise. `ProcessSpawnLimit` is required when they launch Git, PowerShell or dotnet. Shared source/graph scans run once per test host; temporary cases are isolated directories, not repeated database/server boots. Existing tests remain in their projects. Future tests must assert actual output/outcomes, not a duplicate implementation or doc sentence alone.

| ID | New test class / required assertions and red witness | Regression protected |
|---|---|---|
| V-1 | `ReleaseImpactEnvelopeTests`: MissingIndexIsIncomplete, StaleInputIsIncomplete, CandidateMismatchIsIncomplete, OrdinaryModeRemainsAdvisory. Old successful payload lacks/refutes the required completeness evidence. | Missing index, wrong tree, legacy compatibility |
| V-2 | `ReleaseImpactRelationTests`: AllTwentySixConsumersSurvive, GlobalInputExpandsConsumers, QualifiedDuplicateNamesSurvive, RawStringIsNotAClass. The 26th current consumer/raw-string census demonstrates red. | Cap, identity and declaration model |
| V-3 | `ReleaseImpactProjectTests`: DiAndInterfaceConsumersExpand, ReflectionIsBoundOrUnknown, GeneratedInputsExpand, OldAndNewConditionalReferencesUnion. Use two real tiny compiled project fixtures, aliased/ambiguous types and a generated input; dynamically unbounded reflection must refuse. | DI/interface/reflection/generated/build closure |
| V-4 | `ReleaseImpactRuntimeTests`: HelperChainReachesPoolTests, AssemblyHookExpandsAssembly, AppsettingsAndContentExpand, UnboundRuntimeInputIsIncomplete. Reconstruct the queue/fixture chains with fixture classes named as in the report. | Helpers, fixtures, config, hooks |
| V-5 | `ReleaseImpactScriptTests`: BasenameReachesBaselineHelper, DotSourceClosureReachesAuthority, EmbeddedPathReachesParityTest, DynamicScriptTargetIsIncomplete. Invoke the real resolution library, including cycles and a basename collision within the fourth case. | All measured script omissions |
| V-6 | `ReleaseImpactSelectionTests`: DeletedDependencyUsesOldGraph, NewCandidateClassUsesNewGraph, CompleteUnionHasReasons, UnknownNeverYieldsCarry. Check complete class/reason sets, strict bindings and safe filter expansion. | Composition, deletions, provenance |
| V-7 | `KeywordTriggerTests`: NewlineTrigger, ProtectedDataTrigger, SessionLogTrigger, ExistingHandTriggersUnchanged. Each fixture diff calls the selector and asserts its guard output. | Original S2 matching |
| V-8 | `GeneratedTextNewlineGuardTests` (GeneratedAssertionFails, PortableAssertionPasses); `DpapiPlatformGuardTests` (UnguardedCallFails, WindowsGuardPasses). Guard execution must fail on the unsafe fixture and accept the safe fixture; no live DPAPI. | Original S2 guard behavior, four results total |
| V-9 | `RosterRegistryTests`: AddedMemberSelectsGuard, RemovedOrMovedMemberSelectsGuard, OracleEditSelectsGuard, UnknownRosterFails. Include same-file added test method and duplicate owner cases within these scenarios. | Original S3 plus deletion coverage |
| V-10 | `LandGraphAuditProjectShapeTests` (UnclosedProjectFails, DuplicateProjectFails); `RosterRegistryCensusTests` (TrackedOraclesHaveOwners, EmbeddedOraclesHaveOwners). Real registry guard/census, not a fixture-only self-compare. | Original S3 shape and audited roster set |
| V-11 | `WhatRanReceiptContractTests`: ArtifactBindsRange, SelectionIsNotExecution, OwedLanesStayOwed, OwnersRequireStoredReceipt. First three consume real serialized fixtures; last pins the owner sections. | S4 machine evidence and documentation |
| V-12 | `WhyMissedLedgerTests`: UnknownRuleFails, CannotNeedsReason, NewNoteNeedsBlock, GrandfatherIsExact. Check coveredBy dispatch, current IDs, duplicate/malformed blocks, and grandfather edits; preserve `WhyMissedWriteUpContractTests` five-item tests. | S5 ledger debt cannot grant completeness |
| V-13 | `ReleaseImpactPolicyTests`: AddedSuiteDirtiesNewSuite, LocalExclusionChangesUniverse, UnknownFieldForcesFull, GlobalReaderForcesFull. Also assert raw-file reader chunks dirty, old/new wildcard union, catalog pin integrity and equal projection on the unaffected fixture suite. | Scoped compatibility safety |
| V-14 | `ReleaseImpactConsumerTests`: IncompleteForcesFull, CompleteOnlyProposesCarry, TamperedBindingsRefuse, MissingCurrentIdentityRefuses. Invoke strict adapter with a CARD-1836-shaped current universe and prior rows; false/tampered never returns any skippable chunk. | Producer/consumer contract; no scheduling activation |
| V-15 | `ReleaseImpactOracleTests`: IndependentClosureFindsOmittedConsumer, TextSweepFindsScriptConsumer, UnknownOracleIsNotZeroMisses, UnrelatedProjectCanRemainClean. Deliberately deficient selection fixture is rejected by the *independent* oracle. | Noncircular accuracy measurement and usefulness |
| V-16 | Replay harness and `ReleaseImpactReplayTests`: CutRange, PostCutRange, FrozenMasterRoster, SummaryRejectsMissingPair. First two consume pinned historical inputs; summary must reject omitted/unknown pairs and record class-level misses. | Both measured ranges and non-cherry-picked 50 |
| V-17 | `ReleaseImpactWindowsTests`: WindowsUniverseBinding, WindowsPathAndCaseRules, NativeReaderEnvelopeRoundTrip, PortableDoesNotCreditWindows. Windows-only attribute on class. Uses isolated fixtures, no live runner/qualification/process custody. | Native reader/path compatibility, not whole release qualification |

### Independent ground truth and measurable acceptance

Historical endpoints (no abbreviation in replay receipts):

| Pair | Baseline | Candidate |
|---|---|---|
| Cut | `3db1a43b02246706dc28075fce7e7b5e84804ddb` | `56e96eabf698e9c1fcd462836ad32ed1fb259e83` |
| Post-cut | `56e96eabf698e9c1fcd462836ad32ed1fb259e83` | `cc48c70050a2932eddf4abe18cea8659d478c33e` |

The ideal behavioral denominator is every class whose outcome changes when the changed implementation is reverted/mutated with all other inputs fixed. This plan does not claim to collect that denominator: mutation is waived and a full Windows run is unnecessary for the authorized proxy.

Build the proxy independently in `tests/Antiphon.Tests/Scripts/ReleaseImpactOracle.cs` and `scripts/replay-release-impact.ps1`:

1. Freeze `git rev-list --first-parent -n 50 <fetched-master-sha>` and each first parent, then persist their full SHAs and path/blob diffs in an ignored run manifest. Keep the two required pairs even if they overlap the 50. Retain exactly 52 logical results, with reused physical work recorded.
2. Enumerate classes independently from source/metadata. Read compiled assembly references from source-bound Linux build artifacts where available; cross-check against separately evaluated MSBuild project/item/import references at both endpoints. The oracle must not import `ReleaseImpactGraphBuilder`, its selection relation or the candidate selector's class list. Use separate assembly-metadata traversal and a deliberately broad text sweep for type names, script basenames, paths, runtime-reader constructs, fixture/hook/config consumers and whole-file policy/catalog readers.
3. Form a conservative union of downstream test assemblies/classes, changed declarations, text-sweep consumers and runtime input owners. On a Windows-only/unsupported project or missing matching historical binary, include the whole potentially affected project/suite universe from old/new source and project metadata. If that bound itself is unknown, mark the oracle result unknown and require Full; do not report it as a zero-miss pass. No Windows test body is needed to enumerate conservative source dependencies.
4. Build shared fixture/reference artifacts once per run, not once per test or pair. Reuse source-bound artifacts only while their complete compile-input/SDK key is equal. For a changed historical project lacking matching binaries, the source/evaluated-graph broadening above is explicit `oracleBasis=conservative-source`, not fictitious compiled evidence. Historical clean exports are private local inherited work; never switch the assigned branch or execute another session's checkout.
5. Require the effective selected class set to contain the proxy set for **every** pair. Report `oracleCount`, `selectedCount`, full false-negative identities/reasons, selection fraction, fallback reason, completeness and oracle basis. Additionally report misses in the raw positive relation before Full fallback. An unknown oracle or incomplete certificate is `unproven/Full`, never included in a claimed 100% complete-selection recall rate. Zero known false negatives is mandatory even on fallback pairs; a Full materialization does not cure an unexplained raw omission.
6. Pin named report omissions as mandatory members: queue/state warmup/Attention tests, AgentTaskPoolTests, ReleaseSelectionCliTests, TestSelectionTests, ReleaseGatePublicationAuthorityTests, ReleaseGateAuthorityContractTests and NativeLinuxPolicyMatcherParityTests in the applicable historical pairs and individual path probes. Keep project-qualified names and candidate existence checks. Count Windows and Linux universes separately.

Acceptance has two distinct verdicts: **selector implementation accepted** requires all V/R witnesses and no known proxy misses in all 52 pairs, with every unknown explicitly Full; **a particular release skip eligible** additionally requires a complete oracle/evidence graph for that candidate/target, D-8 compatibility, and CARD-1836's independent carry gates. If any real pair remains unproven, publish that fact and its cause. Do not state that this plan proved the next cut can skip it. Report extra selections as over-selection, not proven false positives; the proxy itself is conservative. Preserve original 17-failure replay as historical evidence, not a new denominator or a replacement for the two real ranges.

### Existing tests affected (CARD-1782)

Use [the impacted-classes table owner](../../testing-and-build.md#impacted-classes-card-1782), `tests/impacted-classes.json`. This work changes no migration, public HTTP DTO/route, operation label, remote deploy function or statement budget. Therefore no new functional run of those areas is required. Their **selector behavior** is required regression scope: exact existing methods `migration_only_diff_keeps_hand_classes_and_the_derive_set`, `contract_snapshot_ranges_select_contract_snapshot_tests`, and `label_remote_budget_and_checkpoint_ranges_select_their_rows` cover those five contracts in CP-8/CP-9.

Existing cap/allowlist tests remain valid for ordinary schema 1; new release tests assert uncapped behavior. Existing index drift/regen and platform tests, hand selector malformed-table behavior, mirror project mapping, MergeSubset behavior, catalog/policy readers and five-item why-missed contracts are explicitly covered below. Do not run whole slow legacy classes solely to establish these focused invariants. A changed implementation beyond this listed scope requires a stated reason and amended checkpoint scope, not silent broad testing.

### Checkpoints

Only the selected slice's rows run in its Code task. `Group` names the lane; Linux rows are portable/default placement, W1 rows require native Windows. No task runs the entire table as one batch. Rows sharing a build have the same `After`. These are ordinary red/green rows, not mutation runs. New-class Min values count the named methods above; internal fixture cases do not inflate execution counts. Existing single-method floors are one.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1R1 | `tests/Antiphon.Tests -> bin-c1820-r1/` | linux-envelope | `/*/*/ReleaseImpactEnvelopeTests/*` | V-1 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-2 | S1R2 | `tests/Antiphon.Tests -> bin-c1820-r2/` | linux-relation | `/*/*/ReleaseImpactRelationTests/*` | V-2 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-3 | S1R2 | CP-2 | linux-ordinary-cap | `/*/*/DerivedImpactIndexTests/(a_path_with_26_classes_is_capped*)\|(solution_file_selects_no_derived_class*)` | R-1 | both methods, 0 failed/skipped | 2 | 1 | true |
| CP-4 | S1R3 | `tests/Antiphon.Tests -> bin-c1820-r3/` | linux-project | `/*/*/ReleaseImpactProjectTests/*` | V-3 | all 4 named, 0 failed/skipped | 4 | 12 | true |
| CP-5 | S1R4 | `tests/Antiphon.Tests -> bin-c1820-r4/` | linux-runtime | `/*/*/ReleaseImpactRuntimeTests/*` | V-4 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-6 | S1R5 | `tests/Antiphon.Tests -> bin-c1820-r5/` | linux-script | `/*/*/ReleaseImpactScriptTests/*` | V-5 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-7 | S1R6 | `tests/Antiphon.Tests -> bin-c1820-r6/` | linux-selection | `/*/*/ReleaseImpactSelectionTests/*` | V-6 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-8 | S1R6 | CP-7 | linux-hand-contracts | `/*/*/ImpactedClassesSelectorTests/(contract_snapshot_ranges_select_contract_snapshot_tests*)\|(label_remote_budget_and_checkpoint_ranges_select_their_rows*)\|(malformed_table_is_refused*)\|(C1843_mirror_test_paths_select_their_own_project*)` | R-2 | all named and arguments, 0 failed/skipped | 4 | 3 | true |
| CP-9 | S1R6 | CP-7 | linux-legacy-index | `/*/*/DerivedImpactIndexTests/(migration_only_diff_keeps_hand_classes_and_the_derive_set*)\|(merge_subset_matches_the_hand_selector*)\|(committed_index_matches_the_builder*)\|(windows_only_classes_stay_out_of_linux_filters*)\|(linux_only_methods_stay_off_the_windows_filter*)` | R-3 | all 5 methods, 0 failed/skipped | 5 | 4 | true |
| CP-10 | S2a | `tests/Antiphon.Tests -> bin-c1820-s2a/` | linux-keywords | `/*/*/KeywordTriggerTests/*` | V-7 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-11 | S2b | `tests/Antiphon.Tests -> bin-c1820-s2b/` | linux-source-guards | `/*/*/(GeneratedTextNewlineGuardTests*)\|(DpapiPlatformGuardTests*)/*` | V-8 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-12 | S3a | `tests/Antiphon.Tests -> bin-c1820-s3a/` | linux-registry | `/*/*/RosterRegistryTests/*` | V-9 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-13 | S3b | `tests/Antiphon.Tests -> bin-c1820-s3b/` | linux-roster-census | `/*/*/(LandGraphAuditProjectShapeTests*)\|(RosterRegistryCensusTests*)/*` | V-10 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-14 | S4 | `tests/Antiphon.Tests -> bin-c1820-s4/` | linux-receipt | `/*/*/WhatRanReceiptContractTests/*` | V-11 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-15 | S5 | `tests/Antiphon.Tests -> bin-c1820-s5/` | linux-debt-ledger | `/*/*/(WhyMissedLedgerTests*)\|(WhyMissedWriteUpContractTests*)/*` | V-12 | 4 new plus all existing, 0 failed/skipped | 5 | 10 | true |
| CP-16 | P1 | `tests/Antiphon.Tests -> bin-c1820-p1/` | linux-policy | `/*/*/ReleaseImpactPolicyTests/*` | V-13 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-17 | P1 | CP-16 | linux-policy-regression | `/*/*/ReleaseGatePlatformTests/(LinuxOnlyWindowsExclusionPolicy*)\|(C1686_LinuxOnlyCensus*)` | R-4 | both methods, 0 failed/skipped | 2 | 2 | true |
| CP-18 | P1 | CP-16 | linux-catalog | `/*/*/TestSelectionCatalogTests/C1313_ProjectAndCatalogUniverse` | R-4 | named method, 0 failed/skipped | 1 | 1 | true |
| CP-19 | P2 | `tests/Antiphon.Tests -> bin-c1820-p2/` | linux-consumer | `/*/*/ReleaseImpactConsumerTests/*` | V-14 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-20 | V1 | `tests/Antiphon.Tests -> bin-c1820-v1/` | linux-oracle | `/*/*/ReleaseImpactOracleTests/*` | V-15 | all 4 named, 0 failed/skipped | 4 | 12 | true |
| CP-21 | V2 | `tests/Antiphon.Tests -> bin-c1820-v2/` | linux-real-diffs | `/*/*/ReleaseImpactReplayTests/(CutRange*)\|(PostCutRange*)` | V-16 | 2 results; proxy joins retained; 0 known misses | 2 | 12 | true |
| CP-22 | V3 | `tests/Antiphon.Tests -> bin-c1820-v3/` | linux-replay-manifest | `/*/*/ReleaseImpactReplayTests/(FrozenMasterRoster*)\|(SummaryRejectsMissingPair*)` | V-16 | both methods, 0 failed/skipped | 2 | 10 | true |
| CP-23 | V3 | n/a | linux-master-01-10 | `pwsh -NoProfile -File scripts/replay-release-impact.ps1 -Batch 1 -Count 10 -ResumeManifest .antiphon/selector-replay/master50.json` | V-16 | exit 0; exactly 10 pair reports; 0 known misses | n/a | 4 | true |
| CP-24 | V3 | n/a | linux-master-11-20 | `pwsh -NoProfile -File scripts/replay-release-impact.ps1 -Batch 2 -Count 10 -ResumeManifest .antiphon/selector-replay/master50.json` | V-16 | exit 0; exactly 10 pair reports; 0 known misses | n/a | 4 | true |
| CP-25 | V3 | n/a | linux-master-21-30 | `pwsh -NoProfile -File scripts/replay-release-impact.ps1 -Batch 3 -Count 10 -ResumeManifest .antiphon/selector-replay/master50.json` | V-16 | exit 0; exactly 10 pair reports; 0 known misses | n/a | 4 | true |
| CP-26 | V3 | n/a | linux-master-31-40 | `pwsh -NoProfile -File scripts/replay-release-impact.ps1 -Batch 4 -Count 10 -ResumeManifest .antiphon/selector-replay/master50.json` | V-16 | exit 0; exactly 10 pair reports; 0 known misses | n/a | 4 | true |
| CP-27 | V3 | n/a | linux-master-41-50 | `pwsh -NoProfile -File scripts/replay-release-impact.ps1 -Batch 5 -Count 10 -ResumeManifest .antiphon/selector-replay/master50.json -Finalize` | V-16 | exit 0; 10 pair reports; summary accounts for all 52; 0 known misses | n/a | 4 | true |
| CP-28 | W1 | `tests/Antiphon.Tests -> bin-c1820-w1/` | windows-contract | `/*/*/ReleaseImpactWindowsTests/*` | V-17 | all 4 named, 0 failed/skipped | 4 | 12 | true |
| CP-29 | W1 | CP-28 | windows-legacy-readers | `/*/*/DerivedImpactIndexTests/(windows_only_classes_stay_out_of_linux_filters*)\|(linux_only_methods_stay_off_the_windows_filter*)\|(regenerate_script_reports_changed_then_unchanged*)` | R-5 | all 3 methods, 0 failed/skipped | 3 | 3 | true |
| CP-30 | V2 | CP-21 | linux-final-index | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | R-3 | named method, 0 failed/skipped | 1 | 1 | true |

R-1 ordinary cap semantics; R-2 CARD-1782 hand contracts and malformed schema/project identity; R-3 ordinary index/merge/lane contracts; R-4 policy/catalog legacy integrity; R-5 native reader/regen compatibility. CP-8 Min is a conservative method floor; Code records actual argument-expanded count and raises the floor from fresh discovery/TRX, never uses internal assertions as executions. S4 additionally verifies unchanged stage-bundle blob IDs without running an unrelated bundle suite.

Use `dotnet run --project tools/Antiphon.Checkpoints --no-build -- run --plan <this-plan> --after <slice> --row-timeout 20` after the gated native-output tool bootstrap. Import must have zero warnings. The tool owns isolated checkpoint builds/slots. Wait after every exit 75 until terminal; do not settle with a run alive. For V3, use CP-22 first, then CP-23 through CP-27 as separately joined row groups on the frozen manifest, preserving the same algorithm/source and its cached input artifacts. Command rows own no extra nested build; use the prebuilt graph/oracle driver from CP-22 and validate its source stamp. Record a needed unlisted build with reason and obtain its own build slot.

Suffix collision census: planned class operands are exact new names without prefix siblings. Existing method operands must be checked against source before import and against fresh executed identities after runs; single exact methods need no `*`. If a new sibling appears, split exact-method rows sharing the same build. Never OR full paths, omit operand parentheses, or remove the trailing star from one OR operand. Markdown table pipes are escaped above. See [CARD-0403](../../testing-and-build.md#combined-class-filters-card-0403).

## Slices and dispatch order

Each paragraph below is a Code brief goal. Times are **provisional 30-60 minute task targets including the assigned ordinary green rows**, not historical completion measurements. Red-witness execution, queue waits and reviews can add time; do not hide those costs or compress the graph work into an optimistic single task. A slice exceeding its target is reported/split at the named module boundary, without weakening its tests.

### S1R1 — fail-closed envelope (45-55 minutes)

Add the release-only parameters and strict incomplete envelope to `scripts/impacted-classes.ps1`, with serializer/validation in new `scripts/lib/release-impact.ps1` and `ReleaseImpactEnvelopeTests.cs`. Before any graph support exists, every release request must be incomplete/Full; ordinary behavior is unchanged. Implement the four V-1 red witnesses and run CP-1. This is the first dispatch; it removes accidental interpretation of advisory success as carry authority.

### S1R2 — full relation and class identity (45-60 minutes)

Add `ReleaseImpactGraphBuilder.cs` and `ReleaseImpactRelationTests.cs` beside the existing builder. Expose pre-cap inputs from `DerivedImpactIndexBuilder.cs` only where needed; preserve ordinary cap/allowlist output. Add full project/type identities and Roslyn declarations, no raw-string pseudo-classes, no truncation. Extend `scripts/regenerate-derived-impact-index/Program.cs` with an explicit release-graph artifact mode while leaving ordinary invocation intact. V-2 red; CP-2/CP-3. Depends S1R1's format.

### S1R3 — evaluated project and assembly closure (45-60 minutes)

Add `ReleaseImpactProjectGraph.cs` and `ReleaseImpactProjectTests.cs`: evaluated Compile/project/import/generated/resource inputs, reverse transitive closure, old/new union and bounded reflection/DI explanations under D-2/D-3. Consume the S1R2 identity model; no fine-edge subtraction. Use tiny real compiled fixtures for metadata assertions, unknown for unsupported evaluation. V-3 red; CP-4. If obtaining trustworthy MSBuild evaluation requires more than this slice, land incomplete support and dispatch the remaining evaluator subtask before S1R6; do not fake completeness.

### S1R4 — runtime files, fixtures and hooks (40-55 minutes)

Add `ReleaseImpactRuntimeInputs.cs`, `tests/selector-runtime-inputs.json` and `ReleaseImpactRuntimeTests.cs`. Resolve config/content/fixtures and host-launch consumers; treat test helpers/hooks assembly-wide; validate every explicit bounded runtime declaration with a witness. Preserve unresolved constructs in the graph's incompleteness list. V-4 red; CP-5. Depends S1R2; can run beside S1R3 in disjoint files.

### S1R5 — cross-language script closure (45-60 minutes)

Add `scripts/lib/release-impact-scripts.ps1`, `ReleaseImpactScriptTests.cs` and fixture scripts under `scripts/fixtures/selector-v2/`. Implement PowerShell AST constant/basename/import resolution and integration inputs for compiled script helpers/tools. Close the exact Baseline, authority and embedded nightly chains from the report. Cycles terminate; ambiguous/dynamic targets preserve unknown. V-5 red; CP-6. Depends S1R2; can run beside S1R3/S1R4, with each task owning its module rather than editing the main selector.

### S1R6 — pinned composition and drift refusal (40-60 minutes)

Wire the modules in `scripts/lib/release-impact.ps1`, the graph driver and `scripts/impacted-classes.ps1`; add `ReleaseImpactSelectionTests.cs`. Union positive edges and conservative scopes from both immutable trees, attach class reasons, and compute completeness/bindings. Implement deleted-path handling, drift/missing/size refusal and complete universe export. V-6 red; CP-7/CP-8/CP-9. Depends S1R3-S1R5 and serializes all main-selector integration. Any routine ordinary-index regeneration uses the existing owner script and is committed before tests.

### S2a — keyword trigger engine (35-50 minutes)

Add `tests/keyword-triggers.json`, `scripts/lib/selector-keywords.ps1` and `KeywordTriggerTests.cs`, then wire the ordinary selector once its integration scope is free. Reuse added-line infrastructure; keep original reviewed three triggers and hand rows distinct. V-7 red; CP-10. Can prepare the standalone module beside S1R work; integrate after S1R6. The trigger selects guards but never sets release completeness.

### S2b — generated-text and DPAPI source guards (35-50 minutes)

Add `GeneratedTextNewlineGuardTests.cs`, `DpapiPlatformGuardTests.cs` and shared parser helper `SelectorSourceGuards.cs`. V-8 fixtures execute the actual guards on unsafe/safe source, excluding incidental fixture text. No Windows call is made. CP-11. Depends S2a for end-to-end trigger ownership; different files permit preparation in parallel.

### S3a — roster registry and membership triggers (40-55 minutes)

Add `tests/roster-registry.json`, `scripts/lib/selector-rosters.ps1`, `RosterRegistryTests.cs`; wire changes of oracles and additions/deletions/moves of members to guards. Include class/method changes in existing files. V-9 red; CP-12. Main-selector edit follows S2a; do not overlap it.

### S3b — audited roster census and solution shape (35-50 minutes)

Add `RosterRegistryCensusTests.cs` and `LandGraphAuditProjectShapeTests.cs`; complete the actual tracked/embedded roster inventory, including audited non-roster-named enumerations, with explicit reasons for exclusions. Amend only the registry entries needed by the census. V-10 red; CP-13. Depends S3a; solution validation does not launch the historical concurrent build race.

### S4 — stored selection receipt and owner contract (35-50 minutes)

Add `WhatRanReceiptContractTests.cs`; document the bound artifact and four distinct meanings selected/executed/carried/owed in `docs/testing-and-build.md` and `docs/orchestration-loop.md`. Include legacy four fields plus owed/completeness/reasons/range/algorithm/input provenance. No stage-bundle edit. V-11 red; CP-14. Depends S1R1 format; final serializer fixture follows S1R6. Reserve owner-doc scope against CARD-1836 documentation work.

### S5 — machine-readable why-missed debt (40-55 minutes)

Add `WhyMissedLedgerTests.cs`, a validator helper, `tests/why-missed-ledger-grandfather.txt`; update `docs/investigations/why-missed-template.md`, the why-missed section of `docs/orchestration-loop.md`, `.claude/skills/antiphon-rc-qualification/SKILL.md`, and the existing `WhyMissedWriteUpContractTests.cs` without dropping its five-item contract. Validate hand/keyword/roster IDs and explicit derived-rule reasons; freeze exact grandfather paths at this slice's base. V-12 red; CP-15. Follows S4 for shared docs, S2/S3 for actual IDs. No historical outcome rewrite.

### P1 — scoped contract comparator (45-60 minutes)

Add `scripts/lib/release-impact-policy.ps1`, `ReleaseImpactPolicyTests.cs` and fixtures for the exhaustive D-8 field/reader classification. Output raw hashes, per-consumer semantic projections, dirty suites and global reasons; retain all unknown/global Full fallbacks. V-13 red; CP-16/CP-17/CP-18. Depends S1R1 and the reader/input model from S1R4/S1R5. This pure comparator does not change legacy `Get-ReleaseSelectionContract` or activate CARD-1836. It can run beside keyword/roster tasks.

### P2 — strict CARD-1836 adapter and contract fixtures (35-50 minutes)

Add `scripts/lib/release-impact-admission.ps1`, `ReleaseImpactConsumerTests.cs` and fixture envelopes/current identity sets. Validate binding, complete bit, global reasons and total current membership; expose a fail-closed proposal usable by CARD-1836. V-14 red; CP-19. Depends S1R6/P1. Do not edit the ledger/scheduler/compiled release path: CARD-1836 owns those integrations and must run its own activation witnesses.

### V1 — independent replay oracle (45-60 minutes)

Add `ReleaseImpactOracle.cs`, `ReleaseImpactOracleTests.cs`, the replay driver mode and `scripts/replay-release-impact.ps1`. Implement the independent metadata/project/text sweep, frozen roster, source-bound shared artifact cache, timing/provenance and raw-versus-fallback comparison. V-15 red rejects a deliberately incomplete fixture selection. CP-20. Independent module work can start after S1R2 format stabilizes; schedule shared driver edits after S1R6 settles. The oracle must remain independently implemented, not a call to the candidate graph.

### V2 — two real-diff acceptance replays (35-55 minutes)

Add `ReleaseImpactReplayTests.cs` and run the exact Cut/PostCut pairs with V1 oracle and final producer; retain full counts, named omissions, broadening and unknowns in ignored evidence plus a concise Markdown findings report. V-16 historical miss witnesses; CP-21/CP-30. Depends S1R6/P1/P2/V1 and final S2/S3/S4/S5 inputs. Regenerate the ordinary index against this assembled source before committing and running its drift witness. A missing historical build does not silently shrink the oracle. If closure is unproven, report Full/unproven and the exact remaining work; do not claim a skipping acceptance pass.

### V3 — frozen 50-commit replay (30-60 minutes execution/evidence task)

Complete the remaining replay tests and freeze the first-parent roster at a fetched master full SHA. Prepare the manifest/shared driver once in CP-22, then run CP-23 through CP-27 sequentially, separately joined. Reuse V2's two results only with identical algorithm/input contract; otherwise rerun CP-21 and report why. Produce the 52-pair audit, raw omissions, completeness/fallback rates and timing distributions. No rerun of product test suites. Depends V2; one missed pair, unexplained miss or missing receipt prevents the aggregate claim.

### W1 — native Windows contract qualification (30-55 minutes)

Add/run `ReleaseImpactWindowsTests.cs` and CP-28/CP-29 on an isolated task checkout using native Windows. Verify target universe, path/case/reader behavior and the graph envelope at the same algorithm source as Linux evidence. If the CARD-1836 bridge uses Windows PowerShell 5.1, include real-size 5.1 JSON roundtrip/refusal in NativeReaderEnvelopeRoundTrip; otherwise require pwsh 7 and record 5.1 as unsupported/incomplete. The old 2 MB issue must not be hidden by a tiny fixture. Depends P2/V2; this work grants no Windows test-outcome credit to Linux classes and never reads/stops the live 0b292fa4 run.

Critical integration order: S1R1 -> S1R2 -> {S1R3, S1R4, S1R5} -> S1R6 -> P2 -> V2 -> V3/W1. P1 joins before P2. V1 can proceed beside modules. S2a -> S3a serialize selector edits; S2b/S3b are separate guards; S4 -> S5 serialize owner docs. Final V2/V3 use all completed producer inputs. CARD-1836 boundary mapping can proceed in parallel; digest/admission work consumes the settled contract and must not overlap these policy/doc files. Do not fill seats by dispatching overlapping edits. This is roughly 14-18 authoring/execution hours across the slice targets before queue waits, reviews and any Debug work, not a promise of next-cut readiness.

Ordinary-index maintenance remains CARD-1838's combined-tree regeneration, not hand resolution of generated JSON. Each slice reports any stale ordinary index its added sources induce; regenerate when its assigned drift witness requires it, and the landing owner regenerates the assembled tree. The generator's necessary build is explicitly permitted preparation outside the ordinary test rows: report the command/reason, use `scripts/build-slot.ps1`, native outputs and matching `UseAppHost=false`, then commit/push the generated index before checkpoint execution. Never regenerate while a test run is in flight. Runtime release graphs remain ignored artifacts and do not inherit freshness from this ordinary index.

## Performance budget

Apply [testing/build performance standards](../../testing-and-build.md#performance-standards) and [R1-R9](../../test-performance-standards.md). This Plan task runs no product tests; implementation verification above does. Plan validation may build only the checkpoint importer through the host build-slot gate, in its native output, and must record that as tooling validation rather than selector evidence.

Measured provenance available: investigation 62e3aea4 at pinned cc48c7005/cut trees recorded single Linux selector calls of 8.373-13.830 seconds; original plan recorded n=19, 11.4-24.1 seconds, median 17.0 seconds. Neither supplies comparable **p95** for the proposed release graph. Historical clean S1 Review recorded a 470.8-second cold build and 1,375.457-second combined checkpoint wall. Those costs motivate narrow methods, not a claim that new rows are already timed. No new-code p95 is measured at Plan time.

| Work | Expected/provisional budget | Deadline and collection obligation |
|---|---|---|
| Single fixture test / selector CLI | 1-40 s for pure fixtures; up to 60 s for one real-tree selection | Initially 120 s per external selector process, finite cancellation/output drain; derive approximately 2x observed p95 after five comparable ordinary observations. CARD-1820 tracks provisional status. |
| Shared source/graph evaluation | 30-90 s per immutable endpoint, once per run; tiny compiled fixture setup up to 120 s | Initially 180 s evaluation / 240 s fixture build; record each CLI/IO phase. An unsupported/expired evaluation becomes incomplete, never a partial complete graph. |
| Cold checkpoint build | Historical 470.8 s single sample; budget 8-10 min | Row/build override 20 min is provisional, not measured p95. Native outputs for direct/bootstrap builds; fresh outputs only via checkpoint runner. |
| Most slice groups | 10-17 min including one build; body ordinarily <=2 min, legacy selection rows <=7 min combined | `--row-timeout 20`; no blanket timeout increases. Split/Debug if observed or expected group >30 min. Cost includes every reused row. |
| V2 historical replay | 12 min including build; two graph/selection joins, no product execution | 20 min initial row bound. Record separate compile/evaluate/Git/text-sweep/selection/join costs and oracle basis. |
| V3 ten-pair batch | 4 min estimate, cached immutable graphs; total five batches 20 min plus CP-22 build/setup | 8 min per command row initially (invoke those rows with `--row-timeout 8`); no nested compilation. Freeze/cache setup once; if total run expected/observed >30 min, caller dispatches Debug with evidence before repeating it. |
| Native W1 group | 15 min including build, real-size reader and ordinary compatibility methods | 20 min initial rows; collect Windows separately from Linux, SDK/load retained. |
| Slot/admission | Separate from execution; no measurement available | 45 min existing finite acquisition allowance; exit 4 is not-run/blocked, no unleased retry. Total/hard wall uses runner-derived execution budget plus bounded admission, recorded separately. |

Use the first five comparable ordinary Code/Review/qualification observations to report nearest-rank per-test and per-row p95, sample count, source/host/platform/SDK/load and adjust timeouts to about 2x measured p95 in a reviewed follow-up. Do not repeatedly run a long row merely to populate a sample. Small samples remain provisional under CARD-1820/CARD-1404; automatic importer 15-minute floor/3x estimate is not the policy, hence explicit execution overrides above. Queue wait is never added to a test's p95.

Setup: no AppHost, production runner, PostgreSQL, Docker or migrations. Share Git inventory, parsed source, compiler metadata and tiny fixture compilation once per run/endpoint key; per-test isolation uses separate fixture directories or immutable snapshots. New project extraction was considered and deferred: these regressions exercise the existing script/index assembly, and a new test project would itself change release policy/catalog during the repair. Slow real-tree replays remain separate from fast fixture assertions; no full Antiphon.Tests run is added.

Record under ignored `.antiphon/selector-replay/<run>/` and checkpoint-owned evidence: exact batch/pair/class membership, input/algorithm SHA, platform/category, build and slot wait, Git/object IO, project evaluation/restore/compile, metadata read, text sweep, script parse, selection, join, body and cleanup durations. Capture per-test TRX plus phase journal. Database/container/readiness/host/migration/snapshot phases are **not applicable**, never fabricated zero measurements; unavailable phases are unknown, and wall minus attributed spans is remainder. Bound process exit and stream drain (initial 10 s), Git/IO operations (initial 60 s per batch operation), and teardown (initial 30 s); retain and report timeouts with partial evidence. These are provisional operation bounds to refine from the same samples.

Code cost is the sum of the selected group's table estimates, never the whole table as one run. Any unexpected selection/batch/run over 30 minutes triggers a Debug task with SHA, exact filters, host/load and phase evidence; the implementing delegate reports it to its caller rather than sub-delegating. Preserve every initial red, timeout and failed receipt. Passing a later targeted rerun does not erase them.

## Completion and remaining limits

Build is ready to start S1R1; no new operator approval is needed for these reversible task-branch changes. The plan is complete, but selector correctness and release eligibility remain to be implemented and measured. A complete release certificate is deliberately harder to earn than an ordinary useful selection.

No behavioral false-negative rate, 236-chunk join, old-run provenance, useful real-cut savings or native Windows execution was measured here. All unknowns have explicit Full outcomes. CARD-1836 still owes historical-run admission and complete execution/carry coverage; this artifact cannot replace either. If a complete graph remains unattainable for a candidate, the truthful outcome is Full with the unresolved dependency named, followed by O-1 only if the operator wants to pursue narrower proof.

## Plan validation record

At initial plan commit `0f097f8611526a3e845372c89ec2e19dabbab4ac`, the repository checkpoint importer accepted all 29 original rows with zero import warnings. Its tooling-only build used the host slot, native default output and `UseAppHost=false`: 8.10 seconds, 0 errors, 11 compiler/analyzer warnings. These are importer build results, not product tests or selector timing. The final table adds CP-30 for the assembled ordinary-index drift check and imports **30 rows, zero warnings**, with 223 total estimated minutes distributed across the separate tasks. Reusing the first YAML output path was refused with `unrecorded-existing` (exit 2); importing to fresh ignored `.antiphon/card1820-plan-checkpoints-final.yaml` passed (exit 0). The earlier output was left intact.

The source census checked all 24 suffix operands then present against tracked test class/method declarations and found no prefix siblings. New class declarations and final fresh executed identities must still be checked by Code. Generated importer YAML is ignored. `git diff --check` passed; read-only `git merge-tree --write-tree origin/master HEAD` was conflict-free against `cc48c70050a2932eddf4abe18cea8659d478c33e`. No product test or live qualification ran in this Plan task.
