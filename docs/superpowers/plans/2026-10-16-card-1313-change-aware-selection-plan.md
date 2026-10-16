# CARD-1313: change-aware test selection and durable set timings

Date: 2026-10-16. Stage: Plan. Target: master only. Next: TestDesign.

## Outcome and scope

Build an explainable, conservative selector over two immutable Git commits, execute its exact class/method rosters, and retain comparable timing records for every attempt. Integrate the selection into the existing release authority rather than letting a runner silently shrink its required census. Keep full nightly readiness separate from RC release credit and CARD-1281 train admission.

This dispatch does **not** fold in TestDesign. The checkpoint inventory below is input to that stage, not an executable closed manifest. TestDesign must bind concrete tests, execution counts, red-first rows, regression selections and controls before Code. No build, discovery, provider, browser, Docker or test execution was performed by Plan.

### Evidence and source identity

- Card: Antiphon `99e86094-bbdb-4329-a13f-06eed945951e`, CARD-1313, revision 4, read through `card.ps1 get CARD-1313 -Board Antiphon`. Includes the operator's per-roster/class/category timing addition.
- Assigned branch/base: `feat/card-task-58a7e5b9`, `965c7956cda39f3300a83e10ff3a8bd0240f16b5`. Its parent investigation is [the evidence map](../../investigations/2026-10-16-card-1313-change-aware-selection.md).
- Product source pinned by that investigation and freshly fetched here: `origin/master = 2d2b30364d1793aedd9dae725a68246e127e7dd0`. The inspected release scripts and execution policy are identical between that source and the assigned checkout. Checkpoint cleanup internals differ; implementation must use the target's current custody APIs, not copy old cleanup code from this branch. The stable integration boundaries named below exist in both.
- Owners read: [testing/build](../../testing-and-build.md), [release gates](../../release-gates.md), [orchestration](../../orchestration-loop.md), [HTTP operations](../../ops-http.md), [project conventions](../../project-context.md). CARD-1281's [train design](2026-10-15-card-1281-pipeline-speed-plan.md) remains the authority for train ownership, exact aggregate publication, risk exclusions and separate readiness credit.
- Live `GET /api/runner-defaults` and `GET /api/session-runners` succeeded on 2026-10-16. Defaults revision 2; eligible Linux and Windows lanes exist. This is availability evidence, not test qualification. Checkpoints name capabilities/OS, never a fleet location. Re-read both endpoints at commissioning; omit `-Runner` unless deliberately pinning a host, and omit `-Platform` for portable work (`Any` unpins).
- CARD-1287's Linux 373-class runs (2,534.527s / 2,433.533s) and Windows 361-class runs (233.641s / 218.501s) used different historical binaries and rosters. They motivate measurement; they establish neither current coverage nor a cross-platform speed ratio. Do not import them as current successful receipts.

## Ground truth: card assumptions versus code

| Assumption | What the code does | Consequence |
|---|---|---|
| There is one project per provider TUI | Claude/Codex/Grok implementations coexist in `Antiphon.Agents.Pty`, `Antiphon.SessionRunner` and server adapters/services | Map exact source ownership to class/method rosters; project-only provider selection is unsafe |
| Categories identify expensive resources | Unit/Integration/Slow/OptIn is incomplete resource metadata; OS checks and launch gates also live in bodies and fixtures | Add explicit resource/eligibility metadata, preserving raw categories independently |
| A real CLI pointed at FakeLlmApi is an API-only test | `*RealCliStubProxyCanaryTests` still launch the real vendor TUI; fake processes can still exercise native PTY | Separate executable, interaction, API and OS dimensions |
| Existing class filters are exact | `Get-NightlyNativeExecutionArguments` appends `*`; pinned TUnit has combined-filter extraction quirks | Compile exact leaf selections and reconcile executed expanded UIDs; do not reuse prefix filters as authority |
| Giving fewer classes makes an RC complete | `nightly-tests-impl.ps1` and `nightly-coverage.ps1` require full profile suites and the required UID union | Add an explicit versioned selection scope to runner **and** publisher; old Full accounting remains Full |
| Policy covers all projects | Six projects are declared, while the pinned tree has eleven literal test projects: seven primary assemblies and four child fixtures | Register MarkdownPdf as primary and classify the four child hosts explicitly; never suppress all fixture-looking paths |
| Adding a suite only changes policy JSON | `release-authority.ps1` pins schema v2 and an exact eight-suite RC allowlist | Update supported policy/authority versions and required-suite validation together; old readers must reject new authority |
| The unattended entrypoint already produces publication authority | A source call-site search finds `New-ReleaseGateAuthority` and `New-ReleaseGateExecutionPlan` invoked by `scripts/test-release-gate.ps1`, but no production caller in the inspected tree; the publisher requires their files and a correlated prepublication card readback | The release hook has a producer-wiring prerequisite. A validator unit test or exit-zero nightly summary cannot establish a working publication lane |
| Live tests are already all excluded from unattended RC | Exact rows are missing for `ClaudeHeadedLongTests`, `GrokMidTurnInterruptCanaryTests`, `GrokSizeCaptureTests`, `HerdrPaneDisposalGuardedLiveTests` | Add precise owned dispositions and check the rest of the live inventory; a sibling prefix exclusion does not cover them |
| A class has one resource requirement | `ClaudeVerifiedDeliveryTests` mixes fake/live argument rows; workspace and paste experiments mix methods | Split the mixed parameterized entrypoint and classify remaining mixtures at method granularity |
| Every test-only edit affects one assembly | Linked `Compile Include`, staged native executables, shared fixtures and `client/dist` cross project boundaries | Track explicit runtime/link edges as well as project references |
| Timing infrastructure is absent | Checkpoints already have `PhaseTimings`, `ReportRow.Timings` and TRX timestamps; `SlowClasses.Seconds` sums case durations | Reuse phase capture; add wall envelopes, identity/category joins and durable history without relabeling duration sums |
| A train is already a release-cut option | Cutter accepts `SourceRef`; `release-cut.ps1` does not forward it. CARD-1281 specifies frozen target B, aggregate A and membership identity | Define a B-to-A hook, not a new scheduler or a claim that the train is deployed |
| A checked-in roster is qualification | Release owners still describe deployment as UNQUALIFIED; static evidence did not establish live release behavior | Require fresh current-source receipts and retain unknown deployment status |

## Decisions

These are design decisions within the commissioned scope, not unresolved operator defaults. They do not enable a schedule or authorize live-provider spending. Runtime qualification is an explicit implementation/rollout gate.

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | Use Git object inputs B and A, candidate-pinned policy/catalog bytes and deterministic JSON output. Explain every changed path and every selected set | Working-tree diffs, moving refs, merge-base substitution and an opaque class list |
| D-2 | Unknown impact selects the entire catalog. Unreadable commits/policy/diff refuse execution; known uncertainty can produce a valid full fallback | Treating an error as an empty diff, guessing a baseline, or dropping unknown tests |
| D-3 | Use a small named core plus explicit dependency closure. Narrow provider rules require reviewed file ownership and consumer lists | A whole Unit lane; class-name/provider-prefix heuristics as coverage proof |
| D-4 | Impact and eligibility are independent. Report all selected live/manual/other-OS obligations even when the current profile cannot execute them | Setting opt-in variables, borrowing credentials, or recording gated tests as successful |
| D-5 | Exact project/FQN/method identities define membership; a class entry is allowed only when every method has the same disposition. Split mixed argument rows rather than filtering display text | Silent class-wide exclusions of fake tests or fragile parameter-name matching |
| D-6 | Retain Full as the default. Ship Shadow first; ChangeAware release credit needs new authority validation, fresh qualification and a qualified Full baseline | Changing the meaning of existing `coverageComplete`, Full Review, Interim, or historic RC receipts |
| D-7 | Persist append-only, versioned JSONL timing events outside disposable worktrees/checkpoint output. Keep per-run shards and query them with a CLI | A new server/database service for this first version; one unlocked fleet-wide append file; worktree-only logs |
| D-8 | Measure roster host wall time and class/category wall envelopes separately from summed case durations; unknown stays null | Adding concurrent test durations and calling the result elapsed wall time |
| D-9 | Timing informs ordering and budget estimates, never whether coverage is required. Report attempted cost including failures and retries | Skipping expensive cases or promising savings from incomparable CARD-1287 runs |
| D-10 | Implement selectors, consumers and contract tests on master. Keep release/train activation and real expensive qualification explicitly commissioned | Applying this policy to an old RC repair branch, enabling Interim, changing train cadence or auto-dispatching workers |

## Two-SHA selector contract

New front door: `scripts/select-test-rosters.ps1 -BaselineSha <B> -CandidateSha <A> [-Profile rc] [-OutputPath <ignored-json>] [-Json]`. Two full 40-hex commit SHAs are required; the repository is the current repository unless an explicit `-RepositoryRoot` is supplied. Human output lists sets, lanes, prerequisites, matching rules and fallback reasons; JSON carries the same facts. This command performs bounded Git reads only: no builds, discovery processes, fetch, checkout, test execution or network calls.

1. Validate both inputs as commit objects in the same repository. Use argument arrays, `--end-of-options` where supported and strict SHA validation. Resolve neither an abbreviation nor a mutable ref. Reject malformed/unavailable objects, policy schemas, hashes and unreadable diff output with exit 2 and no runnable manifest.
2. Read policy/catalog from A using Git blobs. Collect paths using `git diff --name-status -z --no-renames B A --`; intentional no-renames makes a move/copy a deletion plus addition, so both sides participate. Include type/mode/submodule changes. Preserve Git's ordinal case-sensitive path identity and slash separators; do not lowercase paths on Windows. Unrepresentable/malformed paths refuse. Case-only renames select both spellings.
3. B need not be an ancestor for a diagnostic diff, but non-ancestry selects Full with `baseline-not-ancestor`. Execution eligible for ChangeAware requires B ancestor of A. Missing baseline is not the same as an unavailable **baseline receipt**: the former is invalid input, the latter makes the release consumer run Full.
4. Add the core even for B=A. A matched empty-impact rule is not supplied in v1: documentation, instructions or configuration without a reviewed mapping select Full. A policy, catalog, build graph or selector change selects Full (`selection-authority-changed`) even if a new rule would otherwise narrow it.
5. Resolve each path through the rule table, then union the referenced sets and their dependency closure. Global full-impact rules always dominate. Provider-specific rules apply only to explicitly enumerated approved paths; generic directory rules apply to the remainder. This partition avoids a generic shared rule defeating every provider rule, while a newly named `Claude*.cs` file remains shared/unknown until classified.
6. Use B's catalog/source entries for deletions and A's entries for additions/modifications; union both sides for changed ownership. An absent old entry falls back to Full. A removed test is recorded as removed, never requested as a nonexistent candidate class; select its surviving consumer set. Catalog changes themselves force Full.
7. Finish with deterministic ordinal sorting and deduplication. Store **all** contributing rule IDs, path roles (old/new), explicit dependency edges and reasons; do not let first-match order hide a broader match. Emit a content digest excluding generated timestamps/absolute checkout paths.

The `antiphon-test-selection-v1` result contains `repositoryIdentity`, B, A, policy and catalog blob/digest identities, selector version/hash, profile, changed-path records, `mode: narrow|full`, fallback reasons, `selectedSets`, `unselectedSets`, prerequisites and unresolved obligations. Each set carries stable id, project/suite, exact class/method selectors, resource tags, permitted platforms, raw category labels where known, eligibility disposition and explanation edges. It is a **selection intent**, with `executionEvidence: false`; it contains no invented expanded UID list or green verdict. A full fallback exits 0 with explicit `mode=full`; consumers must honor it, not silently narrow again.

### Catalog and source mapping

Add `tests/test-selection-policy.json` (versioned, canonical hash) and `scripts/lib/test-selection.ps1`. The execution policy pins this catalog's path and hash. Catalog entries are data, not commands: regexes have anchors and finite match time; arbitrary script blocks and shell snippets are forbidden. An invalid regex is a policy error, never an unmatched-path omission.

Each set has `id`, `suite`, project path, FQN classes/methods, `sourceInputs`, `dependsOn`, `resources`, `platforms`, eligibility/owner/reason and optional prerequisite IDs. Resource dimensions are independent: `vendorExecutable` (none/claude/codex/grok), `interaction` (none/native-probe/tui), `api` (none/stub/live), browser, installed desktop UI, native PTY, PostgreSQL, Docker and broker. Unknown resources cannot be assigned an unattended runnable disposition. Persist provenance as source paths/gates, not line numbers that must stay stable.

The initial catalog is built from sections 2-5 and Appendices A/B of the investigation, then reconciled with **fresh candidate discovery**. Primary automated members not safely assigned to a finer set belong to an explicit owning-suite remainder. Remainders select all applicable discovered UIDs except precise profile exclusions; they prevent an incomplete hand-maintained list silently losing new tests. New projects, unmapped linked sources, ambiguous dependencies and new unclassified resource gates refuse reduced execution. No one has to classify thousands of methods as independent rosters to ship safely.

Use these stable roster IDs; materialize their actual FQNs/methods in catalog data during S1. Membership examples below are mandatory inclusions, not a license to leave other evidence-map consumers unclassified. A roster spanning projects is split into project-qualified execution chunks. Overlapping impact sets are allowed, but the execution UID union runs each case once per declared attempt.

| Roster IDs | Membership contract |
|---|---|
| `core` | The seven exact methods below |
| `provider.<claude/codex/grok>.offline` | Explicit pure detector/parser/replay members, including ClaudeGoldenReplayTests/ClaudeEffortDismissalReplayTests, CodexResponseAnalyzerTests, and GrokSignInPromptDetectorTests/GrokTrustPromptDetectorTests; a provider name alone never admits a class here |
| `provider.<p>.fake-native`, `.tui-stub`, `.tui-live`, `.vendor-offline` | Distinct method-level sets from evidence section 3. Fake-native includes the new fake Claude delivery method and RunnerGrokAdapterReadyTestsPty; tui-stub includes corresponding RealCliStubProxy/Herdr classes; tui-live includes all real interactive canaries for that provider; vendor-offline includes the two workspace-layout oracles. A method with both stub and live variants must split or be conservatively gated as live |
| `provider.<p>.server-runner` | Named adapter/auth/launch/rules/normalizer/tailer consumers. Examples: RunnerCodexAdapterReadyTests, CodexTranscriptTailerTests, GrokRulesFileStoreTests and ClaudeAuthProbeTests. If a consumer is shared, it belongs to the shared dependency closure too |
| `shared.pty`, `shared.pty-host`, `shared.runner`, `main.remainder`, `e2e.remainder`, `messaging.remainder` | Complete owning-project candidate discovery remainder with precise method/profile/platform dispositions; no hand-written prefix omission |
| `client`, `e2e.browser`, `e2e.api`, `e2e.windows-delivery`, `desktop.herdr` | Client entries, ten browser classes, API/CLI classes, four Windows landing-fixture consumers and installed-Herdr methods respectively, as enumerated in evidence sections 3-4 |
| `main.postgres`, `main.outbound-broker`, `messaging.postgres-broker`, `docker.operations` | Explicit evidence section 5/Appendix A consumers; these resource sets can overlap ownership sets. Docker operations names RollingVolumeRecycleDockerTests and RetiredTempContainerDockerTests; fixture-command classes remain separate resource-free script sets |
| `pdf.portable`, `pdf.linux`, `pdf.browser-linux`, `scripts` | Precisely classified MarkdownPdf renderer/command methods, Linux-only methods, Four_documents_produce_readable_combined_pdf, and execution-policy script-census entries. Script entries retain their own names and prerequisites |

Set prerequisites are a separate DAG, not selectable test members: for example browser requires pinned client build, Playwright installation, app/database fixture and isolated runner; broker tests require their disposable broker; native fake tests require staged owned children. Deduplicate prerequisites and detect cycles/missing targets. Shared dependency edges must include linked Compile inputs, build-only ProjectReferences, staged binaries, imported shell helpers and runtime bundle/image/fixture files. An unsupported conditional edge broadens selection instead of disappearing.

| Rule | Anchored path family / exact-map condition | Selected impact, in addition to core |
|---|---|---|
| P-CLAUDE / P-CODEX / P-GROK | Approved exact files under `^server/Infrastructure/Agents/(Pty\|SessionRunner)/`, `^server/Application/Services/`, `^src/Antiphon\.Agents\.Pty/`, `^src/Antiphon\.SessionRunner/`, `^src/Antiphon\.SessionRunner\.Contracts/` with reviewed provider ownership | That provider's offline/parser/replay, fake/native, vendor-offline, real-TUI-stub and real-TUI-live sets; its server/runner adapter and app compositions. Enumerate all consumers, not merely same-prefix tests. A shared consumer makes the path shared |
| P-SHARED | Remaining `^src/Antiphon\.(Agents\.Pty\|SessionRunner\|SessionRunner\.Contracts\|PtyHost(?:\.(?:Client\|Protocol))?)/` and shared server agent/launch/delivery inputs | Main server, Pty, PtyHost, SessionRunner and E2E automated remainders plus **all three** providers' affected live/oracle sets and desktop UI obligations |
| P-SERVER | Remaining `^server/` (including Program/config/migrations) | Full catalog in v1. Shared application/data/runtime coupling is not proved narrow by a directory name |
| P-CLIENT | `^client/` | Client build/lint/Vitest; ten Playwright browser classes listed below; ContractSnapshotTests and all recorded `client/dist` consumers. New or uncertain bundle consumer expands to the E2E remainder |
| P-MSG | `^src/Antiphon\.Messaging(?:\.[^/]+)?/` and `^tools/Antiphon\.Messaging\.SchemaGen/` | Messaging remainder, main ChannelOutbound recovery/unified/composed classes, E2E outbound/channel compositions; PostgreSQL/Redpanda/Docker requirements follow those sets |
| P-PDF | `^tools/Antiphon\.MarkdownPdf/` or `^tests/Antiphon\.MarkdownPdf[^/]*/` | MarkdownPdf fake/CLI/renderer and Linux Docker browser sets, main outbound conversion and E2E PDF composition |
| P-FAKE | `^src/Antiphon\.Fake(?:Claude\|Grok\|LlmApi)/` | All catalog consumers of that fake, including real-vendor CLI plus stub API compositions. Missing linked/staged consumer mapping selects Full |
| P-TEST | Exact mapped `^tests/` source/fixture path, excluding authority/build inputs | Owning class/method set **and all** linked-source, fixture and staged-output consumers. Unmapped test/helper paths select Full; test-only never means zero impact |
| P-DOCKER | `^scripts/(deploy-server2[^/]*\|fixtures/c(?:1008\|994)[^/]*)`, `^docker-compose[^/]*\.ya?ml$`, catalogued Dockerfiles | Real volume-recycle/retired-container sets plus matching command-harness and image consumers. An image with unknown consumers selects Full |
| P-AUTHORITY | Execution/selection policy, selector/timing/roster/release/nightly/checkpoint infrastructure and corresponding owner instructions | Full. Include `scripts/lib/release-authority.ps1`, not only `release-gate.ps1`; control-plane scripts influence the meaning of every omission |
| P-BUILD | `^(Directory\.Build\.(props\|targets)\|Directory\.Packages\.props\|global\.json\|NuGet\.Config\|Antiphon\.sln)$`, any `\.(csproj\|props\|targets)$`, package/lock/runtime graph inputs | Full, with candidate discovery and graph validation; do not narrow on a project name |
| P-UNKNOWN | Any remaining path, ambiguous edge or policy mapping | Entire catalog across lanes, with unavailable/manual obligations preserved |

The literal rule JSON must escape alternation correctly; the Markdown table is not executable regex input. Provider allowlists come from inspected source plus cross-reference review, not a regex accepting every future provider-prefixed file. TestDesign supplies representative positive and negative paths for every rule and a new-file case that must broaden.

The ten browser classes are `AgentE2ETests`, `BoardE2ETests`, `DirectoryAutocompleteE2ETests`, `DispatchConcurrencySettingsE2ETests`, `OrchestratorE2ETests`, `PipelineGlanceE2ETests`, `ProjectDeleteE2ETests`, `SmokeE2ETests`, `WorkflowDeleteTests`, `WorkflowOutputTests` in `Antiphon.E2E`. `CardCliE2ETests`/`ScheduleCliE2ETests` and native landing-delivery classes remain separate sets with their actual app/database/OS prerequisites.

### Narrow always-run core

Use one portable main-project core containing these exact methods (plus their argument-expanded rows, if later parameterized). The two C1313 methods are new contracts to be bound by TestDesign:

- `Antiphon.Tests.Scripts.TestSelectionCatalogTests.C1313_ProjectAndCatalogUniverse`: every test project has a role; catalog/profile/discovery accounting cannot silently lose members.
- `Antiphon.Tests.Scripts.TestSelectionTests.C1313_CoreAndUnknownFallback`: equal SHAs retain core and unknown paths select the complete set universe.
- `Antiphon.Tests.Scripts.NightlyDiscoveryParserTests.C1044_DiscoveryReconciliation`.
- `Antiphon.Tests.Scripts.NightlyExecutionDiagnosticTests.D13_ExecutionGuards`.
- `Antiphon.Tests.Scripts.ReleaseGatePolicyTests.C599_ExpandedCoverage`.
- `Antiphon.Tests.ProcessSpawnLimitTests.Caps_concurrent_process_spawning_tests_at_one` and `Process_spawning_classes_carry_the_limiter`.

These cover catalog identity, fallback, evidence reconciliation and process-test classification. They do not qualify native implementations. They avoid a second assembly or app/DB fixture merely for a smoke test. Measure their fresh cost; if the core is unexpectedly slow, repair or review its composition explicitly rather than dropping it dynamically. Existing `TestClassificationPolicyTests` and `ProviderOracleGateTests` remain change-selected regression sets, not unconditional whole-class additions.

### Class/method and live-gate reconciliation

Update `tests/test-execution-policy.json` to a new supported schema with explicit `projectDispositions`. Seven primary projects include `Antiphon.MarkdownPdf.Tests`. Four exact child fixtures are `scripts/fixtures/nightly/c487-probe/Probe.csproj`, `Antiphon.Checkpoints.FilterHost`, `Antiphon.Checkpoints.LifecycleHost`, and `Antiphon.Checkpoints.RepeatHost`. Each child row names its consuming parent/tests and reason. Every discovered literal test project must have exactly one role; obsolete/duplicate/missing rows fail validation. Non-test support executables remain dependency inputs, not independent suite obligations.

Register the `markdown-pdf` suite and its portable renderer/command tests in the Full RC profile; update the publisher's exact required-suite contract as well as the runner. Classify method-level OS early returns explicitly: for example, the symlink and fresh/stale PDF checks that return immediately on Windows belong to a Linux obligation and cannot earn Windows behavior credit. Its real browser method is a separate Linux/Docker/explicit-flag obligation. Keep nightly's existing scheduled coverage meaning unless explicitly migrated; adding inventory does not silently change readiness credit. All changed policy bytes receive a new canonical hash.

Reconcile exact RC dispositions for the four missing live-gated classes in the ground-truth table, owned by CARD-1313 with the actual prerequisite/reason. Audit every real-provider/installed-Herdr entry in evidence section 3, including Integration-only `ClaudeEffortPromptCanaryTests`; its category is not proof of unattended eligibility. Retain existing exclusions only with their real source gate and profile reason. Check ChannelOutbound opt-in debt against the actual fixture/gates; unavailable required infrastructure stays incomplete, not a fresh exclusion to make red green.

Concrete mixed cases:

| Source | Required representation |
|---|---|
| `ClaudeVerifiedDeliveryTests.Evidence_appears_then_submit_completes_a_turn` | Split its entrypoints into one fake-native method and a real-Claude method with the four existing argument rows. Preserve the shared body, assertions and total five cases. Move eligibility metadata to the correct method; classify the fake method's Windows native requirement independently |
| `OrchestratorWorkspaceLayoutCanaryTests` | Two Claude TUI methods; Codex `Codex_prompt_input_is_bounded_at_the_nested_checkout_git_root` and Grok `Grok_inspect_is_bounded_at_the_nested_checkout_git_root` are **real executable, noninteractive, no model API** oracles. Preserve `ANTIPHON_PTY_PROVIDER_ORACLES=1` and actual executable prerequisites; they are not fake offline tests |
| `PtyPasteMarkerExperiments` | `Real_claude_by_conpty_backend`, `Real_claude_controls_and_size_sweep`, `Real_claude_delivery_envelope` are real TUI. Remaining Node/host experiments retain Windows/native/Node requirements without acquiring provider requirements |
| `*RealCliStubProxyCanaryTests`, including Herdr compositions | Vendor executable + interactive TUI + local stub API; never API-stub-only. Pure `FakeLlmApi` clients with no vendor child belong to the stub set |
| `RunnerGrokAdapterReadyTestsPty`, Claude replay/submit-contract tests | FakeGrok/fakeclaude or recorded output; native requirements where present, no live model claim |
| `HerdrPaneDisposalGuardedLiveTests` | Installed desktop UI plus dummy child, not real Codex; retain its explicit installed-Herdr gate |

Expand candidate discovery before execution and freeze exact `(project, FQN, method, expanded UID)` membership. All selected argument rows must execute. Prefer one exact class/method leaf filter per invocation where the pinned runner supports it; combine only after the filter-host test proves its executed roster. If the runner requires prefix forms for combined operands, pre-discover collisions and split the chunk; never execute unwanted live suffix classes and detect that only afterwards. Use UIDs as reconciliation authority, not display-name parsing. Unresolvable identity or filter mismatch is incomplete.

## Execution and release/train integration

### Modes and authority

Add an explicit `Full|Shadow|ChangeAware` selection mode to the release/roster path. Full is the compatibility default. Shadow computes B-to-A selection and timing estimates but executes Full, producing ordinary full coverage plus a diagnostic proposed-omission list. ChangeAware executes core plus impacted eligible sets only after its admission checks. Ad hoc checkpoint selections remain scoped checkpoint evidence, never automatic release credit.

Version the release authority, discovery partition, execution plan and public manifest together. Preserve strict old-schema readers for historical Full records; unknown versions/scopes refuse. Candidate authority binds repository, B, A, candidate id/ref, profile, policy/catalog/selector hashes, changed-path digest, selection digest, full discovery digest, baseline authority reference and optional train manifest digest. Include selector/catalog scripts in the pinned script-hash chain. Extend `release-authority.ps1`, `release-gate.ps1`, their ledger producers and publisher validation in the same slice.

For each candidate-discovered UID, retain one coverage disposition: `required-selected`, `not-impacted`, or `profile-excluded`. `not-impacted` is a deliberate policy omission, **not executed/pass**. Separately record impact-selected but out-of-profile manual/other-platform obligations. Every `not-impacted` row must be justified by recomputing the selector from pinned Git objects; caller-edited JSON cannot authorize omission. The required execution ledger is the selected UID union, and missing/extra/duplicate/zero/skipped required rows still fail. Full has no `not-impacted` rows. New/unclassified UIDs widen to their suite remainder or Full before launch; they never inherit omission from a missing catalog entry.

ChangeAware RC admission requires all of:

1. Explicit mode, a valid qualification receipt for the deployed selector/policy/catalog/runtime contract, and supported publisher/coordinator versions. No implied activation from code being present.
2. A valid **Full RC baseline** receipt at B, in the same repository/profile/platform contract with the same policy/catalog/selector hashes, and B ancestor of A. A selected run cannot become a rolling baseline that compounds omissions. Historical CARD-1287 Unit rosters, a member Review, master-readiness credit or a green summary do not qualify. Absence/staleness of usable authority falls back to Full before tests; a corrupt/contradictory claimed authority refuses.
3. Fresh candidate build/discovery, stable clean source and exact A binding. Baseline execution is not copied into A's ledger. Dependency/build/policy changes and unknown inputs force Full even with a baseline.
4. Complete selected execution and report delivery at A, all existing candidate/ref/publication/typed-receipt checks, and separately fulfilled profile-required extra-lane obligations. Publish `coverageScope=ChangeAware` and omission counts/digests; do not call it Full or issue a Full Review assertion from it.

Before qualification, ChangeAware can produce diagnostic results with `selectionComplete=true`, `coverageComplete=false`, `creditKind=none`; the existing publisher refuses them. After qualification, the **new** authority validator can issue RC release credit for the explicitly scoped policy-complete ChangeAware result. Keep `testsPassed`, actual executed counts and `allProfileCasesExecuted` separate, with the latter false for any omission. Existing Full credit consumers must inspect scope or reject the new schema. Master scheduled readiness continues to require its Full scheduled contract; this card does not relax it.

The new qualification record is stored in the configured durable release state root, outside candidate worktrees. It binds repository, policy/catalog/selector/coordinator/publisher hashes, platform/runtime contract, exact Full and Shadow run evidence digests, independent Review identity, activation revision and recorded activation authority. Explicit activation selects this immutable record; the selector cannot write or approve its own qualification as a side effect. A changed bound hash/runtime contract or revoked activation invalidates it. Baselines must be genuine Full receipts from that qualified contract; age alone is not the omission test because every intervening change is included in B..A. Historical baseline validation uses its frozen authenticated receipt/publication record, not a fresh requirement that an old release card still have its original revision; the current candidate retains the existing live prepublication readback check. Define this historical validator separately from the current-candidate publisher gate, without relaxing the latter.

Full fallback means all eligible automated suites in the chosen profile **and** a visible list of every other catalog lane selected by impact. It never turns on live/headed/distiller flags. Required but unavailable platform/resources leave that obligation unfulfilled. A profile exclusion is not permission to claim that a real-TUI test passed. Preserve native-run coordination and build slots across every selected chunk.

### Hook points and CARD-1281

- `release-candidate.ps1` continues to pin A once. Freeze B and selection identity in the candidate journal before execution. Normal release B comes from the latest compatible verified Full RC baseline, not a moving branch or last arbitrary task. With no baseline, run Full and seed future selection only after valid publication authority exists.
- Thread mode, B, selection path/digest and timing root through `release-cut.ps1`, `nightly-run.ps1`, `nightly-tests.ps1`, `lib/nightly-run-impl.ps1` (`Invoke-AntiphonNightlyRunCore` and self-reexec) and `lib/nightly-tests-impl.ps1`. There is no separate `nightly-run-core.ps1`. Missing forwarding is covered by composition tests.
- Wire authority/plan production into the actual cut/run path, with one durable candidate intent and release-card identity supplied by the existing release coordinator, full candidate discovery before the execution plan freezes, and actual per-chunk/prerequisite terminal records written to the ledger. Report delivery must supply the existing typed prepublication receipt. This producer wiring is a prerequisite of S6 publication integration; coordinate it with CARD-1281 release qualification rather than inventing receipt fields or a new card/delivery service. If that coordinator contract is still absent when S6 starts, finish selector/timing/Shadow artifacts and report the precise integration dependency; do not claim a publishable ChangeAware lane. Add a composition test through the production entrypoint, not just direct validator calls.
- `nightly-tests-impl.ps1` consumes the frozen selection before prerequisite/build/chunk scheduling. Build/discover candidate assemblies as needed to prove the full census; cache only with exact candidate/source/build binding. Test-execution savings do not imply build/discovery savings.
- The CARD-1281 aggregate hook receives its exact target B, aggregate A and frozen membership digest. Select on **B..A**, including integration edits; never substitute the union of member selections. Target drift, rebased A, changed membership or recomposition invalidates selection and receipts. A failed aggregate/recomposition retains CARD-1281's Full rerun requirement.
- Extend the train manifest with this selection reference, omitted UID digest, unresolved lane obligations and measured/estimated costs. New TrainSelected admission, aggregate Review, exact-target landing and publication links remain CARD-1281's work. This card supplies the hook and tests its input contract; it does not create a train scheduler or member landing shortcut.
- Until the selected publication contract is qualified, the daily train executes Full with Shadow diagnostics. Full Review/Interim semantics and high-risk/manual eligibility rules from CARD-1281 remain intact. An affected live/manual obligation cannot disappear by changing the train's selected set.

No new input-waiting session is introduced. A run awaiting a qualification decision persists its result and settles; the train waits as data. Existing blocked sessions keep their seats while parking is disabled, with no automatic release deadline (CARD-1083).

## Durable timing records and report

### Storage and writers

Use schema `antiphon-test-timing-v1`, UTF-8 JSONL, under `<TimingRoot>/runs/<UTC-date>/<run-id>/<attempt>/events.jsonl`. Resolve `-TimingRoot`, then `ANTIPHON_TEST_TIMING_ROOT`, then `<git-common-dir>/antiphon/test-timings` for local development. Hosted checkpoint/roster/release callers must configure a persistent root outside the checkout, per-run output and cleanup scopes; record its durability class. A repository-local fallback is not advertised as centrally backed up. No hard-coded machine or fleet path belongs in source.

Each coordinator owns its shard and serializes appends; parallel test children do not contend on a global file. Flush complete events to stable storage before acknowledging them. Keep immutable event IDs and never rewrite an earlier attempt. A completion retry with the same event id/payload is idempotent; the same id with different bytes is an integrity error. Queries union roots (e.g. retained Linux and Windows stores) and deduplicate exact events; no network database or service is required. Hosting config must retain/mount the root across container replacement. No automatic age-based deletion in v1.

Validate run/attempt path components and canonicalize the configured root before creating files; refuse traversal or a hosted root inside disposable/cleanup output. The plan changes no generic cleanup authority. A single owner serializes each shard across threads, and restart recovers its durable sequence before appending. Local spool replay reuses the original payload and timestamps. Internal timing records may name the opaque host; the public release manifest contains only allowlisted aggregate counts/digests, never host identity, local evidence paths or raw category data accidentally carrying private information.

Writers:

| Execution boundary | Append location and responsibility |
|---|---|
| Checkpoint tool | `Execution/RowRunner.cs` captures each attempt's boundaries; a new `Timing/TimingJournal.cs` writes start/completion through the coordinator. `RunScheduler.cs` associates producing build, slot and run completion. Extend `Trx/TrxReport.cs`/`Report/PhaseTimings.cs` without changing receipt truth |
| Legacy `run-checkpoint.ps1` | `scripts/lib/test-timings.ps1` emits the identical schema from its existing host/build/slot phases and reconciled TRX. The tool and wrapper must agree on one producer, so a wrapped row is not appended twice |
| Nightly/release roster runner | `scripts/lib/nightly-tests-impl.ps1` appends start/terminal events per chunk attempt and a suite/roster union after reconciliation; run-level completion belongs to `nightly-run-impl.ps1`. Include prerequisites, client and script entries even when they fail |
| Report front door | New `scripts/test-timing-report.ps1 -Roots <paths> -GroupBy roster\|class\|category [-Top 20] [-Json]` reads events; it does not modify results or qualify execution |

Write `run-started` before launching work, per-set completion in `finally` after owned-child join/cleanup, and `run-completed` after durable append. Hard death may leave only a start; a later query reports an unfinished attempt, not zero duration or success. Ignore only a torn final JSONL fragment with an explicit warning; malformed complete records are reported as corrupt. Keep original test outcome separate from persistence status. If durable completion fails, retain the local spool and report `timingPersistence=incomplete`; the rollout/timing acceptance gate is incomplete, while the underlying test assertion result is unchanged. An explicit idempotent import can recover the spool without running tests again.

### Record format

| Field group | Required content |
|---|---|
| Identity | `schemaVersion`, `eventId`, `eventKind`, `runId`, attempt, parent run id, producer/version, repository identity, candidate SHA, nullable baseline SHA, policy/catalog/selection hashes, scope/profile |
| Set | `setKind` (run/roster/chunk/class/category/prerequisite), stable `setId`, suite/project, FQN class or category, selection/membership digest, producing build id, execution filter digest; repeat/diagnostic/qualification purpose |
| Host | Opaque host/runner identity captured at execution, OS/architecture, runtime/TUnit versions, concurrency/slot limits and available CPU/load observations. No credentials, user paths, transcript text or environment dump |
| Time | UTC start/end, nullable monotonic `wallSeconds`, `measurementKind`, build/slot/startup/teardown components and reason for each unavailable measurement. Class/category records use `caseWindowSeconds`; preserve `caseDurationSumSeconds` separately |
| Counts/outcome | Selected/observed/passed/failed/skipped/unknown case counts, outcome, exit/timeout/cancel status, coverage status and persistence status. Incomplete discovery means unknown selected count, not zero |
| Evidence | Source-clean/build-provenance state, source/build/discovery/TRX/terminal-diagnostic hashes, relative artifact references and digest of the validated execution receipt. Timing alone is never execution authority |

Count expanded cases by actual identities, not loop assertions or source methods. Record all attempts, including pre-test failures, canceled/time-out runs and required skips. A build failure has known build wall time and unknown case wall/counts, with an explicit failure phase. Link shared builds once through `producingBuildId`; do not charge the same build to every class.

At roster/chunk level use monotonic elapsed time covering the declared process boundary; report slot wait separately. At class/category level use earliest valid executed-case start to latest end within each invocation, joined to full discovery metadata. This is an **inclusive wall envelope**, not exclusive occupancy or summed durations. Classes/categories can overlap, so their times are non-additive. A category spanning separate chunks reports per-chunk envelopes plus an explicitly labelled run envelope; do not include gaps as active test time. Missing/out-of-range timestamps or clock discontinuity yield null with reason. `SlowClasses.Seconds` retains its old duration-sum meaning for compatibility.

Extend TRX parsing with identity/duration data and join categories by validated discovery identities; do not infer categories from method names. Preserve all raw categories (a case may appear in several category reports) plus catalog resource labels. Without a reliable join, write `category=unavailable` and do not fabricate a category aggregate. Client and script suites get outer wall/count records from their native reports; unsupported class/category data is explicitly unavailable.

For supported TUnit checkpoint runs, category collection is a producing-build prerequisite: reuse a validated discovery document bound to the same assembly hash or produce it once under the build slot, then share it across that build's rows. Normal successful TUnit runs must therefore yield category records, not routinely fall back to unavailable. Record discovery wall time separately and include its command/cost in the checkpoint manifest. It remains metadata, not proof that a selected test executed. Unsupported external command rows can legitimately lack class/category dimensions.

### Query and budget semantics

Default report shows slowest 20 sets by median wall metric, sample count, p90 (nearest rank), latest value, outcomes, total attempted cost and missing/corrupt-record count. Roster rows use host wall; class/category rows clearly say inclusive case window. Provide separate duration-sum ordering when requested. Show the latest five comparable completed runs against the preceding five, absolute/percentage median change and individual run/SHA references. Fewer than ten comparable runs yields `insufficient-history`, not a trend. A zero prior median has no percentage.

Comparability keys include repository, set identity, exact membership digest, measurement kind, platform/architecture, runtime versions, concurrency/resource profile and policy/catalog version. Host grouping is default; cross-host results require explicit grouping and remain stratified. SHA is provenance, **not** a grouping key, so unchanged sets can trend across changes. Membership digest uses canonical logical expanded identities, not an assembly hash; if the framework cannot supply stable data-row identity, mark that series noncomparable across builds. Separate changed membership and incomplete/failing attempts from successful-duration percentiles, while retaining their incurred cost in totals.

Selection output may attach estimates from comparable history: median and p90 per selected roster, sample count, last observation and missing-estimate list. Fewer than five comparable successful observations is provisional. Unknown durations remain unbudgeted. Sum only known serial execution costs, count builds once and report concurrent schedule estimates separately. Do not add category/class windows to roster elapsed time. Budget overruns produce an observation; they never omit tests, extend deadlines automatically or release a run as green.

## Implementation slices

Each slice has a test-only red commit before its fix and a push after each meaningful commit. Preserve the original assertion failure; missing APIs/build failures are not useful red evidence. S1-S3 and S4-S5 may each share one committed verification group, with one isolated build reused only within that group.

| Slice | Files and behavior | Required tests / red trigger |
|---|---|---|
| S1: inventory and eligibility | `tests/test-execution-policy.json`; new selection catalog; `scripts/lib/nightly-policy.ps1`; schema/allowlist compatibility in `release-authority.ps1`; the mixed Pty test entrypoints named above | New `tests/Antiphon.Tests/Scripts/TestSelectionCatalogTests.cs`; extend `ReleaseGatePolicyTests`. Prove real-tree seven-primary/four-fixture census, unknown project refusal, exact four live gaps and preserved five mixed delivery rows. Baseline reports missing inventory rather than passing a reduced fixture |
| S2: two-SHA selector | New `scripts/select-test-rosters.ps1`, `scripts/lib/test-selection.ps1`, `scripts/test-test-selection.ps1`; catalog rules/edges | New `TestSelectionTests.cs` and `TestSelectionDependencyTests.cs` using real temporary Git commits. Invalid SHA, no diff, unknown path, delete/case rename, provider-specific/shared fanout, catalog change and linked fixture cases assert actual selected sets and explanations |
| S3: exact roster execution | `scripts/lib/nightly-tests-impl.ps1`, `nightly-coverage.ps1`, `scripts/test-nightly-tests.ps1`; extend `tests/Antiphon.Checkpoints.FilterHost` with controlled prefix/method/argument cases | New `TestSelectionExecutionTests.cs`; relevant exact methods of `NightlyDiscoveryParserTests`, `NightlyExecutionDiagnosticTests`. Baseline suffix collision, lost argument UID and gated sibling must fail intended assertions; no real provider needed to test filter safety |
| S4: measurement and persistence | New `tools/Antiphon.Checkpoints/Timing/*`; `Execution/RowRunner.cs`, `RunScheduler.cs`, `Trx/TrxReport.cs`, `Report/PhaseTimings.cs`, `ReportModel.cs`; `scripts/lib/test-timings.ps1`, `run-checkpoint.ps1`, nightly producer hooks | New `tests/Antiphon.Tests/Checkpoints/TestSetTimingTests.cs`, `TimingJournalTests.cs`; shared synthetic format fixtures plus actual owned-child narrow timing witness. Overlapping cases must not be summed as wall; parallel append/replay/torn-tail/crash and write-failure preserve truthful outcome |
| S5: report and estimates | New `scripts/test-timing-report.ps1`, bounded timing-report helper/tests; selector estimate attachment | New `TestTimingReportTests.cs`. Known ten-run data produces exact ranks/trend/counts; wrong platform/membership, retries, unknowns and duplicate events must not contaminate successful series or double-count builds |
| S6: release/train consumption | `release-candidate.ps1`, `release-cut.ps1`, nightly wrappers/reexec implementations, `lib/release-authority.ps1`, `release-gate.ps1`, `publish-release.ps1`; exact baseline/selection forwarding and new scope validation | New `ChangeAwareReleaseTests.cs`; narrow affected methods in `ReleaseGateAuthorityContractTests`, `ReleaseGatePublicationAuthorityTests`, `ReleaseGateRunTests`. Real Git objects plus controlled publisher/runner seams prove Shadow executes Full, unqualified selection earns no credit, tampered omission/baseline/A/membership refuses, missing baseline falls back, and nightly credit stays separate |
| S7: rollout contract/docs | `docs/testing-and-build.md`, `docs/release-gates.md`, CARD-1281 plan hook section, `scripts/windmill/README.md` and existing definition parameters where needed | Contract checks in S6 verify documented mode/default/schema and forwarding; no schedule registration or live qualification in the ordinary Code task. Document root retention, reporter examples and exact activation/rollback evidence |

Code must inspect consumers of any changed public timing/report model and any pinned policy schema. Do not edit generated `docs/cards/`, resurrect removed checkpoint cleanup implementations, add generic retries, widen test timeouts or suppress inherited failures.

## TestDesign handoff

### Behavioral acceptance to bind

- V-1: project universe, explicit fixture roles, resource/eligibility gaps and mixed-method classification; unknown entries remain visible.
- V-2: deterministic two-SHA path semantics, all path reasons, fallback, invalid-input refusal and core.
- V-3: provider-specific selection and conservative shared/link/staged/bundle dependency closure.
- V-4: actual filter-host execution equals the frozen expanded UID selection; prefix siblings, mixed methods and all data rows are accounted for before any live launch.
- V-5: wall/envelope/sum semantics, categories, missing time and count provenance.
- V-6: durable append, concurrent producer isolation, interrupted attempts, idempotent recovery and no cleanup of retained timing roots.
- V-7: slowest/trend/budget report comparability, failed-attempt cost and unknown estimates.
- V-8: composed release/roster forwarding, frozen selection authority, baseline requirements and Full/Shadow/ChangeAware credit separation.
- V-9: train B/A/membership binding, target drift/recomposition behavior, native coordination and unchanged nightly readiness.

R-1 preserves discovery/terminal/census negative controls; R-2 preserves checkpoint receipt/build/source validation and old timing fields; R-3 preserves publisher identity, report delivery, typed prepublication receipt and old Full authority behavior. TestDesign names only affected existing methods, not entire script test suites.

### Proposed checkpoints (not yet executable)

One group per behavior, no whole Unit/assembly lane and no redundant broad green run. The columns below intentionally omit invented Min counts: TestDesign must count actual expanded TUnit executions and produce the required `### Checkpoints` manifest under `## Verification design`. It must also bind the separate red-first commands; one internal harness loop is one TUnit execution.

| Candidate CP | After | Lane | Build/reuse | Narrow filter contract | Covers | Estimated minutes including build where named |
|---|---|---|---|---|---|---:|
| CP-1 | S1-S3 | Portable .NET/PowerShell | `tests/Antiphon.Tests -> bin-c1313-selection/` | `/*/Antiphon.Tests.Scripts/TestSelectionCatalogTests/*` plus exact affected policy methods bound by TestDesign | V-1 | 7 |
| CP-2 | S1-S3 | Portable Git/PowerShell | CP-1 | Exact methods in `TestSelectionTests` and `TestSelectionDependencyTests`; only their fixture commits | V-2,V-3 | 3 |
| CP-3 | S1-S3 | Portable owned native test child | CP-1 (stages FilterHost) | `/*/Antiphon.Tests.Scripts/TestSelectionExecutionTests/*` and exact discovery/terminal guards | V-4,R-1 | 4 |
| CP-4 | S4-S5 | Portable .NET timing | `tests/Antiphon.Tests -> bin-c1313-timing/` | `/*/Antiphon.Tests.Checkpoints/TestSetTimingTests/*` and exact changed TRX/phase regressions | V-5,R-2 | 7 |
| CP-5 | S4-S5 | Portable filesystem/owned child | CP-4 | `/*/Antiphon.Tests.Checkpoints/TimingJournalTests/*` | V-6 | 3 |
| CP-6 | S4-S5 | Portable PowerShell | CP-4 | `/*/Antiphon.Tests.Scripts/TestTimingReportTests/*` | V-7 | 2 |
| CP-7 | S6-S7 | Portable Git/release seams | `tests/Antiphon.Tests -> bin-c1313-release/` | Exact `ChangeAwareReleaseTests` methods and affected Full authority/credit methods | V-8,V-9,R-3 | 9 |
| CP-8 | all | Windows filesystem/PowerShell compatibility | `tests/Antiphon.Tests -> bin-c1313-windows/` | Exact path-case/append/argument-forwarding compatibility methods from V-2,V-6,V-8; no entire classes | Cross-OS regressions | 6 |
| CP-9 | S1-S3 | Windows fake native PTY, sequential after main tests | `tests/Antiphon.Agents.Pty.Tests -> bin-c1313-fake/` | Exact new fake-only method in `ClaudeVerifiedDeliveryTests`; TestDesign binds the name. Separately inspect discovery for the four preserved live data rows without launching them | V-1 mixed-entrypoint preservation | 6 |

Planning ordinary floor: 47 minutes, including three portable builds, one Windows main build and one Windows Pty build; this is an estimate, not observed duration. TestDesign must separately budget the baseline-red increment, discovery prerequisite cost and post-land controls; their cost is not included in 47. Broad production Full qualification is a rollout operation with a separately bound roster/cost, not permission to add a whole Unit checkpoint here.

Use the checkpoint tool once per committed group, then wait for its final exit (75 means unfinished). Bootstrap/other build drivers use `scripts/build-slot.ps1`; rows retain actual counts, full SHA, clean-source/build binding and slot provenance. All outputs use isolated `bin-c1313-*/` with forward slashes and are removed through the established owned cleanup after completion. Main and Pty test assemblies run sequentially. No source edits during a run. Confirm unexpected failures at the base using only failing methods. Preserve exact CHECKPOINT lines in ignored/stored evidence, and run `scripts/check-evidence-diff.ps1` over base..HEAD for Code and Review.

TestDesign must give every new guard a compiling, observable red trigger and identify the consumer assertion it protects. Use exact-method controls for missing dependency edges, unknown-path narrowing, dropped/extra UIDs, false timing sums, duplicate journal appends and forged release scope. Post-land mutation remains separately commissioned; Plan has run none. Pure schema snapshots or tests that compute expected output with the selector under test are insufficient.

## Qualification, risks and Code handoff

Land the inventory/selector/timing/consumer implementation under ordinary Code and independent Review before enabling reduced release credit. Qualify the deployed selector and authority using a fresh Full baseline on the corrected policy and paired Shadow selections at exact candidate SHAs. Record actual discovered/selected/executed UIDs, all omissions and gated obligations, full source/build hashes, durable timing append/readback and actual host/OS. Exercise at least provider-only, shared-native, client, linked-fixture and unknown-path cases. An unexplained omission or a failure outside a shadow selection suspends reduced mode and requires a rule repair plus fresh qualification. Qualification demonstrates the specified boundaries; it is not a proof that static impact analysis can predict every behavioral dependency.

Run any real TUI/browser/Docker/Windows qualification only through a separately commissioned exact roster with prerequisites and spend/approval gates satisfied. The release system's current runtime qualification and train implementation are not established by this Plan. Until their required receipts exist, use Full/Shadow and retain incomplete activation status. Rollback is mode Full; keep history and the timing writer/report available. A policy/catalog/selector/runtime-contract change invalidates qualification and forces Full; never grandfather old reduced results.

Main risks are incomplete dependency declarations, new body gates, unstable expanded identities, prefix filter leakage, a changed publisher schema omitted from one consumer, durable roots accidentally inside cleanup scopes, and misleading timing comparisons under concurrency. Conservative remainder/full fallback, method-level inventory, fresh discovery, scoped authority and explicit measurement kinds address these at the cost of lower initial savings. Track fallback frequency and cost; only narrow a new family after evidence and reviewed policy changes. Do not promise a percentage saving before paired measurements.

TestDesign should finish the executable verification manifest and then hand Code the exact approved artifact. Code implements S1-S7, commits/pushes each slice, reports the closed checkpoints and leaves live activation evidence separate. It must carry the inventory and live-gate prerequisites, the operator timing requirement, and the master-only target into its own brief. No unresolved design question prevents TestDesign from proceeding.


## Verification design

TestDesign append, 2026-10-16, task `38202b83`, source `3c73433172b3b1db64e613076cb61e03209b6cd3`. The fix design above is unchanged. This section replaces only the proposed verification inventory as the executable Code contract. Target remains **master only**. Full/Shadow implementation is not qualification or activation of ChangeAware. No builds, tests, discovery, PCs, provider calls or deployment ran in TestDesign.

### Inspection

- Read `ReleaseGatePolicyTests` (all five bodies), `NightlyDiscoveryParserTests`, `NightlyExecutionDiagnosticTests`, `ScriptHarness.RunHarnessCaseAsync/Validate`; `test-release-gate.ps1` profile/census/expanded-coverage bodies, real-Git checkout/authority/ledger helpers, run and publication fixtures, parameter hops and receipt readback | S1-S3 -> V-1..V-4/R-1; S6 -> V-8/R-3. Harness PASS assertions are not TUnit executions.
- Read `RerunFilterHostTests.parenthesized_class_filter_runs_both_methods_on_a_real_host`, its child runner, `FilterHostTests.alpha/beta`, FilterHost project and main-project staging references; `Get-NightlyNativeExecutionArguments` | V-4. Current class operands append `*`; the new child witness must detect a forbidden sibling **before launch**, not merely reject its TRX afterwards.
- Read `ClaudeVerifiedDeliveryTests` and `ClSession` in full; live gate/body witnesses in `ClaudeHeadedLongTests`, `GrokMidTurnInterruptCanaryTests`, `GrokSizeCaptureTests`, `HerdrPaneDisposalGuardedLiveTests.LiveFixture`; MarkdownPdf browser gate and method-level OS returns | V-1. No live method execution is authorized by discovery/classification checks.
- Read `TrxReportTests`, `RowRunnerTests` selected bodies and driver helper, `CheckpointBuildBindingPathTests`, `CheckpointTimingHarnessTests` and harness, `CheckpointTestSupport` fixture/driver bodies and `CheckpointTestBase`; production `TrxReport` and `PhaseTimings.Reduce`, RowRunner/Scheduler integration sites | V-5..V-7/R-2. Existing `SlowClasses.Seconds` is a sum. New tests use these nearest fixtures, not an invented test clock or unowned process helper.
- Read `ReleaseGateAuthorityContractTests`, `ReleaseGatePublicationAuthorityTests`, `ReleaseGateRunTests`, `ReleaseGateReportDeliveryTests` including recipient reads/recovery/process helper, and `AntiphonAppFixture` initialization/restart/isolated runner configuration; `release-cut.ps1`, release authority constants and report entrypoint | V-8..V-10/R-3. The script fixture's `New-C599Authority` fabricates ledger/receipt input through validators; it is **not** the production producer. Existing report recovery proves incident-card delivery, not the missing typed prepublication producer.
- Read testing/build checkpoint schema, runner and delivery contracts; release-gates authority/qualification owner; project conventions; orchestration stage order and D-2 | all rows. Existing process limiter methods read for the exact core roster. Full historical publisher matrices and unrelated checkpoint custody/native tests are excluded from ordinary scope; only named affected methods below run.

Recipes shared by the case inventory:

- **SG (script/Git):** new script cases use the existing ScriptHarness owner and exact named assertions; each C# method dispatches one same-named `Test-C1313_*` PowerShell case. Selector/catalog cases live in `scripts/test-test-selection.ps1`; release cases extend `scripts/test-release-gate.ps1`. Create owned temporary real Git repositories with `core.autocrlf=false`, immutable B/A objects, independent literal expected rosters and a candidate policy/catalog. Create case-only paths through Git index/tree objects on Windows; never depend on two case-distinct files coexisting on that filesystem. Invoke the production front door, parse JSON independently, compare sorted logical identities and reasons, and inspect child-launch logs for zero builds/network/providers. No expected result is computed by the selector being tested.
- **FH (real filter child):** extend the staged FilterHost with safe `C1313Offline`/`C1313OfflineLive` sibling classes, `One`/`OneLive` sibling methods, and `Rows` with exactly three `[Arguments]` values. Forbidden leaves write a sandbox sentinel then fail; all bodies are harmless. Discover the real pinned TUnit assembly and pass its frozen expanded UIDs into the production compiler/runner. Assert exact receipt UIDs and absence of sentinels. Owned children write only into registered scratch, are joined, and carry the process limiter. Child executions do not inflate the parent TUnit Min.
- **TJ (timing):** reuse CheckpointTestBase, FakeDriver and the controlled clock for arithmetic/fault boundaries; use literal timestamped TRX plus discovery metadata. Use real filesystem shards and the production journal/import/report readers for persistence. One owned staged child supplies real host/discovery/TRX timing evidence. FakeDriver proves reducer/scheduling contracts only; it cannot prove durable storage or native process wall measurement.
- **RA (release authority):** extend the real-Git C599 fixture with a small fixed discovery census, a genuine Full baseline-shaped fixture, an explicit qualified-contract fixture and independent remote-store observer. Restore one valid input before each negative case, change one thing, and repair unrelated digests so the intended guard is reached. These are isolated contract fixtures, never real qualification receipts. Only a file-backed Git/GitHub recipient is substituted; no remote release is written.
- **RC (real recipient composition):** use the existing E2E app fixture, disposable PostgreSQL and isolated runner, plus owned Git repositories and FH test children staged by the E2E build. Execute the actual cut/run/report producer and publisher gates. Read the bound card independently through GET by GUID, including the whole description and revision. Do not hand-write authority, frozen plan, terminal ledger or typed receipt. Use tiny actual child executions for the fixture's declared suite/prerequisite roster, not fabricated green outputs or the full repository suite. Preserve seamed/diagnostic provenance and its refusal of real release credit; the real recipient receipt proves this composition, not live qualification. The new coordinator's concrete entrypoint/queue binding is a **binding-required** Code deliverable; see DL-3. No test accepts a canned receipt in its place.

All new methods below are single `[Test]` executions except `C1313_PathIdentity`, which has exactly three arguments (`delete`, `case-rename`, `nul-paths`). Script assertion labels below are the required decisive oracle labels, not a second test count. Every fixture first proves its valid control reaches the intended consumer. New subprocess tests use the assembly-local ProcessSpawnLimit and existing ownership helpers. A failing fixture, unavailable infrastructure, missing API, zero selected tests or compile error is not a useful red.

### Delivery inventory

| ID | Producer -> destination | Durable identity and persistence boundary | Recovery and observable receipt |
|---|---|---|---|
| DL-1 | RowRunner/Scheduler, legacy checkpoint wrapper, nightly chunk/run producers -> timing journal -> report query | `(repository, A, runId, attempt, eventId, setId, producingBuildId)`; start flushed before launch, completion flushed after owned-child join; one selected producer per wrapped row | TJ `C1313_ProducerReceipts` reads the completed event and category join with a **fresh independent reader**, for tool, wrapper and nightly producers. Assert one row event, preserved outcome and attempt, correlated digest, no acknowledgement before flush. A call to Append or a start event alone proves nothing about completion. |
| DL-2 | local timing spool/restart importer -> durable shard -> report query | Same original event identity, payload bytes and timestamps; real on-disk spool is the retry queue, durable shard is the recipient | `C1313_RecoveryHandoffs` runs an already eligible sink and a sink held busy by a barrier/owned file handle; faults before spool persistence, after spool persistence/before append, after durable flush/before local acknowledgement, and completion-write failure. Reopen/replay through production code, then query from another process: exact single terminal event or explicit unfinished/incomplete record. No receipt for failed enqueue; no zero-duration success after hard death. No mocked queue. PC-38..PC-43 protect the separate acknowledgement/replay/recovery guards. |
| DL-3 | release-cut/coordinator -> candidate journal -> discovery/plan -> execution ledger -> prepublication report destination -> publisher | Existing coordinator intent + releaseCardId, candidateId/ref, B/A, runId, selection digest; freeze before launch; terminal chunk/prerequisite records persisted before report; typed receipt binds card revision and whole run correlation | `ReleaseGateReportDeliveryTests.C1313_ProductionReceipt` must drive the real producer through an eligible recipient and a busy/conflicting recipient, with restart/failure before intent persistence, before report enqueue/write, after recipient commit before receipt persistence, and after receipt before publisher readback. Reuse the same durable identity and independently read exactly one logical report at the bound card. If the coordinator has a queue, use **that real durable queue and worker**, asserting enqueue failure leaves recoverable intent, then successful recovery; a queue insert/Sent/ack is not receipt. Current report is synchronous HTTP, so its corresponding cuts are failed write and lost response. Never invent a queue service for this card. |
| DL-4 | publisher -> isolated Git/GitHub recipient | Candidate/ref/A + frozen publication-authority digest + tag/release identity; existing publication journal | RA `C1313_ScopeAuthority` and R-3 independently read stored manifest/release state; negative scope/receipt cases make zero writes. Existing remote timeout/tag/draft/asset recovery is unchanged and excluded; fixture storage does not qualify GitHub deployment. |

DL-3 is a mandatory **producer prerequisite**, not optional follow-up coverage. The inspected source has no production caller of `New-ReleaseGateAuthority` / `New-ReleaseGateExecutionPlan`. Code must bind the coordinator supplied by CARD-1281 (entrypoint, persisted intent, receipt producer and any queue) and make CP-24 pass before claiming S6 complete. If it remains absent, commit/push completed selector/timing/Shadow work, preserve CP-24 and its PCs as **binding required**, report the exact dependency and return `next: plan`; no publishable ChangeAware claim or clean whole-scope verdict. This is an integration dependency already in the fix design, not a human-default question.

No new session-input delivery path is designed. Fake Claude's composer/done witness is a test-preservation check only. If the supplied coordinator delivers session input, DL-3 must bind its real queue and the matching **complete UserPrompt transcript** (recipient/session/run/payload and attempt floor), with busy/already-eligible/crash recovery; card readback, screen redraws, requests and events cannot substitute. Unbound delivery payloads remain binding required after Code and block completeness.

### Proves it works now

“Now” below describes the required post-Code result, not a TestDesign execution claim. Method names are exact contracts. New classes are under `tests/Antiphon.Tests/Scripts/`, except TJ classes under `Checkpoints/`, and the two stated Pty/E2E additions.

| ID | Behaviour / layer | Exact test case(s), recipe and variant | Oracle and reachability / classification |
|---|---|---|---|
| V-1 | Inventory, eligibility and split entrypoints / policy + metadata + fake native | `TestSelectionCatalogTests.C1313_ProjectAndCatalogUniverse`, `.C1313_LiveEligibility`, `.C1313_MixedDeliveryInventory` (SG); `ClaudeVerifiedDeliveryTests.C1313_DeliveryDiscovery` and `.Fake_evidence_appears_then_submit_completes_a_turn` | Real source census is seven primaries + four exact child projects, including MarkdownPdf; add one unknown fixture-looking project and refuse (`unclassified-project`). Exact four missing live classes have owned gate/resource dispositions (`live-obligations-visible`); Integration-only Claude effort and method-level Linux PDF obligations remain visible; unknown resources cannot run unattended. Candidate discovery has exactly one fake delivery UID plus four real rows `short/hugeLine/multiLine/batch` under `Real_evidence_appears_then_submit_completes_a_turn`; metadata test itself is excluded from this five-row census. `fake-required-real-excluded`; Windows fake completes the existing composer/turn assertions. Native real-TUI behaviour remains unqualified. |
| V-2 | Two immutable commits / SG | `TestSelectionTests.C1313_InputRefusal`, `.C1313_CoreAndUnknownFallback`, `.C1313_PinnedPolicy`, `.C1313_PathIdentity` (3 rows), `.C1313_AuthorityChanges`, `.C1313_DeterministicReasons` | Invalid/noncommit/unavailable input or failed Git read => exit 2, no runnable manifest (`invalid-intent-refused`); B=A selects exact seven-method core; unknown path => complete catalog (`unknown-full`). Dirty worktree policy cannot replace A (`candidate-blob`). Delete uses B's owner and surviving A consumers; case rename preserves both spellings; NUL-delimited whitespace/newline/Unicode paths and type/mode/gitlink records preserve identity (`old-new-union`, `ordinal-paths`). Policy/catalog/build graph changes => `selection-authority-changed`; nonancestor => Full. Reordered rules/paths give same digest excluding time/absolute root, with every reason retained. |
| V-3 | Provider/shared/dependency closure / SG | `TestSelectionDependencyTests.C1313_ProviderAndShared`, `.C1313_RuntimeEdges`, `.C1313_InvalidGraph` | Explicit approved provider path selects that provider plus core, unrelated provider offline sets absent; new similarly named file is shared/Full. Shared input includes all three provider obligations (`shared-all-providers`). Four single-edge witnesses: linked Compile, build-only/staged child, imported helper, client bundle; assert literal surviving consumer roster and edge id (`linked-consumer`, `staged-consumer`, `helper-consumer`, `bundle-consumer`). Invalid/missing/cyclic dependency/prerequisite or unsafe/invalid/timed-out regex refuses reduced execution (`invalid-graph`, `invalid-rule`); full-impact rule dominates a narrow overlap. |
| V-4 | Exact execution / production runner + real FH | `TestSelectionExecutionTests.C1313_ExactExpandedExecution`, `.C1313_CoverageRefusal`, `.C1313_NewUidRemainder` | Real executed selected UID multiset is exactly the frozen three argument UIDs plus selected ordinary leaf, each once (`exact-uid-union`); no sibling sentinel (`forbidden-not-launched`). Separately remove one required UID, add one extra UID, duplicate a UID, mark required skip, and supply zero results: each refuses with its own label `missing-uid`, `extra-uid`, `duplicate-uid`, `required-skip`, `zero-required`. Fresh unclassified UID widens before launch (`new-uid-required`). These are five minimal fault witnesses, not a Cartesian schema matrix. |
| V-5 | Wall/category/count truth / TJ reducer + owned child | `TestSetTimingTests.C1313_WallEnvelopes`, `.C1313_UnavailableMeasurements`, `.C1313_DiscoveryJoin`, `.C1313_ProducerReceipts` | Literal cases [1,5] and [2,6] in host [0,8]: host 8, inclusive class/category window 5, duration sum 8, startup 1, teardown 2; legacy sum unchanged (`wall-not-sum`). Missing/reversed/out-of-range/discontinuous timestamps yield null + reason (`unknown-not-zero`). Same-build discovery joins expanded IDs and all raw categories; foreign assembly digest refuses the join (`category-build-bound`). Real child success emits normal category records; wrapper/tool emits once; client/script class/category unavailable is explicit. Build failure, timeout/cancel and failed assertion preserve actual counts/outcome and producingBuildId (`producer-receipt`). |
| V-6 | Durable history / TJ filesystem + real spool queue | `TimingJournalTests.C1313_RootCustody`, `.C1313_ReplayIdentity`, `.C1313_RecoveryHandoffs`, `.C1313_TornTail` | Resolve explicit/env/git-common root; reject traversal and hosted root inside checkout/output/cleanup (`root-outside-custody`). Two shards can append independently; same shard serializes and reopens sequence. Exact event replay is one event (`replay-once`), changed same-id bytes refuse (`event-conflict`). DL-2 cuts yield durable readback, retained spool on failure and unchanged test verdict (`flush-before-ack`, `recoverable-handoff`, `persistence-not-test-outcome`). Delete only owned disposable run output and still read history (`history-survives-output`). Ignore only torn final fragment with warning; complete malformed row remains corrupt (`corrupt-not-truncated`). |
| V-7 | Report/trend/estimates / script reader | `TestTimingReportTests.C1313_ComparableHistory`, `.C1313_AttemptCost`, `.C1313_EstimatesNeverSelect` (TJ + SG) | Literal successful values 1..10 give median 5.5, nearest-rank p90 9, prior-five median 3/latest-five 8/change +5/+166.666…%; different SHA may compare, different membership/platform/runtime/concurrency/host may not (`comparable-series`). Nine observations => insufficient history; zero prior median => percentage null. Failures/retries contribute cost, not success percentiles; duplicate events/build IDs count once (`attempt-cost`). <5 success samples provisional, unknown unbudgeted; change all timing estimates and selected UID digest remains identical (`budget-never-omits`). |
| V-8 | Release modes and authority / RA | `ChangeAwareReleaseTests.C1313_ModeCredit`, `.C1313_BaselineAuthority`, `.C1313_ScopeAuthority`, `.C1313_CandidateBinding`, `.C1313_ParameterHops` | Default Full unchanged, Shadow executes full union (`shadow-full`); unqualified diagnostic ChangeAware earns none; explicit current qualification required. Only compatible qualified Full B admits narrowed A; absent/stale baseline => Full before launch, contradictory claimed authority refuses (`baseline-full-only`, `baseline-absent-full`, `baseline-corrupt-refused`). Historical B uses immutable receipt, not today's old card revision. Fresh clean build/discovery A required. Tampered omission with recalculated JSON hashes refuses on recomputation; selected pass never becomes Full/master credit. Runtime trace carries mode/B/A/selection path+digest/timing root through cut, wrappers, self-reexec and tests; no source-text-only forwarding proof. |
| V-9 | Train and rollout contract / RA + document check | `ChangeAwareReleaseTests.C1313_TrainAggregate`, `.C1313_RolloutContract` | Actual B..A including integration-only edit determines sets; changed target/A/membership invalidates prior authority (`aggregate-bound`); failed aggregate/recomposition runs Full (`recomposition-full`). Readiness/native coordination retained by R-3. Documents and real defaults agree: Full, no enabled schedule/Interim, durable timing root and explicit qualification/rollback required (`rollout-unqualified`). This tests the hook, not a train scheduler. |
| V-10 | Production receipt and public projection / RC + RA | `ReleaseGateReportDeliveryTests.C1313_ProductionReceipt`; `ChangeAwareReleaseTests.C1313_PublicProjection` | DL-3 readback carries exact intent/card/candidate/A/run/selection and all selected obligations (`producer-to-card-receipt`); resume yields one report with same identity. No authority can be synthesized from an exit-zero summary. Public manifest exposes scoped omission counts/digests, never host/private paths/raw categories (`public-allowlist`). Fake GitHub observer proves contract only; deployment stays unqualified. |

### Guards the regression

The SG fixture's fixed rule witnesses below are mandatory cases within V-2/V-3, not additional TUnit executions. Use one positive and one negative boundary per rule, with all other inputs valid. Negative paths are deliberately created in the temporary Git fixture; they need not exist in the checkout. Assert the actual contributing rule ID and the expected roster/Full decision, not just a regex match. Provider paths use the reviewed exact-file catalog; if S1 finds shared consumers, the conservative shared result wins and the catalog/test binding must record that evidence before Code narrows it.

| Rule | Positive path | Negative boundary path | Oracle / exact method |
|---|---|---|---|
| P-CLAUDE | `src/Antiphon.Agents.Pty/ClaudeDetectors.cs` | `src/Antiphon.Agents.Pty/ClaudeNewShared.cs` | Approved provider closure vs shared/Full; `TestSelectionDependencyTests.C1313_ProviderAndShared` |
| P-CODEX | `src/Antiphon.Agents.Pty/CodexDetectors.cs` | `src/Antiphon.Agents.Pty/CodexNewShared.cs` | Approved provider closure vs shared/Full; same exact method |
| P-GROK | `src/Antiphon.Agents.Pty/GrokDetectors.cs` | `src/Antiphon.Agents.Pty/GrokNewShared.cs` | Approved provider closure vs shared/Full; same exact method |
| P-SHARED | `src/Antiphon.Agents.Pty/PtyAgentRunner.cs` | `src/Antiphon.Agents.PtyExtra/Unknown.cs` | All providers/native consumers vs unknown Full; same exact method |
| P-SERVER | `server/Program.cs` | `serverish/Program.cs` | Full with P-SERVER vs P-UNKNOWN reason; `TestSelectionTests.C1313_DeterministicReasons` |
| P-CLIENT | `client/src/App.tsx` | `clientish/src/App.tsx` | Client + exact ten browser classes + recorded bundle consumers vs unknown Full; same exact method |
| P-MSG | `src/Antiphon.Messaging.Service/Program.cs` | `src/Antiphon.MessagingExtra/Program.cs` | Messaging/main outbound/E2E consumers vs unknown Full; same exact method |
| P-PDF | `tools/Antiphon.MarkdownPdf/MarkdownPdfRenderer.cs` | `tools/Antiphon.MarkdownPdfExtra/Renderer.cs` | PDF/Linux-browser/outbound consumers vs unknown Full; same exact method |
| P-FAKE | `src/Antiphon.FakeLlmApi/FakeLlmApiServer.cs` | `src/Antiphon.FakeOther/Program.cs` | All declared stub/vendor/native consumers vs unknown Full; `TestSelectionDependencyTests.C1313_RuntimeEdges` |
| P-TEST | `tests/Antiphon.Messaging.Tests/FakeSlack/FakeSlackServer.cs` | `tests/UnmappedSharedHelper.cs` | Owning and linked E2E consumers vs Full; same exact method |
| P-DOCKER | `scripts/deploy-server2.ps1` | `scripts/deploy-server20-unknown.ps1` | Exact known image/command/real-Docker consumers; similarly prefixed unknown input broadens if consumer mapping is absent; `TestSelectionTests.C1313_DeterministicReasons` |
| P-AUTHORITY | `scripts/lib/release-authority.ps1` | `notes/release-authority.ps1` | Full with authority-changed vs unknown reason; `TestSelectionTests.C1313_AuthorityChanges` |
| P-BUILD | `Directory.Build.props` | `notes/Directory.Build.props.txt` | Full with authority/build reason vs unknown reason; same exact method |
| P-UNKNOWN | `notes/unmapped.txt` | B=A, no changed path | Entire catalog with unresolved lanes vs exact seven-method core; `TestSelectionTests.C1313_CoreAndUnknownFallback` |

- R-1: `ReleaseGatePolicyTests.C599_ProfileSchema`, `C599_ProfileSuites`, `C599_EligibilityCensus`, `C599_ExpandedCoverage`; `NightlyDiscoveryParserTests.C1044_DiscoveryReconciliation`; `NightlyExecutionDiagnosticTests.D13_ExecutionGuards`; both exact core ProcessSpawnLimit methods. Retain historical v1/v2 fixtures and refusals while adding the new shipped schema; update only shipped-schema expectations. Exact live method exclusions leave siblings required; missing expanded/diagnostic UID remains incomplete; orphan/duplicate/version guards remain active. Internal 9/9/15/9/8/4 assertion inventories are **six** TUnit executions, plus two limiter executions.
- R-2: `TrxReportTests.aggregates_seconds_per_class` keeps 0.03 seconds as duration sum; `RowRunnerTests.no_trx_is_exit_2` keeps missing execution evidence invalid even if a timing completion exists; `CheckpointBuildBindingPathTests.Different_directory_or_provenance_is_refused` keeps foreign/dirty build source refused. Three executions. Do not turn timing history into a replacement execution receipt.
- R-3: `ReleaseGateAuthorityContractTests.C599B_PinnedPolicy`, `C599B_AllChunks`; `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedgerReceipt`, `C599A_ReceiptReadback`; `ReleaseGateRunTests.C599_CreditVerdicts`, `C599_MasterStateIsolation`, `C599_SharedLock`. Seven executions. Retain old Full schema fixtures, selected ledger completeness, typed prepublication receipt/current-candidate readback and zero remote writes on refusal; no RC-to-master credit; four master files byte-identical; live native lock owner not stolen. No whole publication-field matrix is selected.

Red-first sequence (separate from ordinary green CP rows): commit/push each slice's test-only change before its fix. Use exact filter `/*/*/<Class>/<Method>` below through the slot-gated checkpoint row driver, isolated `bin-c1313-red-sN/`, Min 1. `S5` characterization reuses the prior committed build only if source/build binding permits; otherwise rebuild and report the cost. Each listed red must be an executed assertion at its named label, followed by its CP green after implementation.

| Slice | Exact red-first method | Reached legacy behavior and expected assertion red |
|---|---|---|
| S1 | `TestSelectionCatalogTests.C1313_ProjectAndCatalogUniverse` | Existing `Test-NightlyPolicyProjectUniverse` on the real tree reports unmatched MarkdownPdf/children; assert complete seven/four roles (`project-universe`). No selector API is needed. |
| S2 | `TestSelectionDependencyTests.C1313_ProviderAndShared` | A **test-only characterization adapter** feeds the real legacy `Resolve-NightlySelectedSuites` result into the fixed provider-impact oracle. Its suite-wide result cannot equal the precise provider/core roster (`provider-exact`). It does not prove the new selector. Once present, the ordinary SG method must invoke only `select-test-rosters.ps1`; remove the adapter from the green path. |
| S3 | `TestSelectionExecutionTests.C1313_ExactExpandedExecution` | Legacy `Get-NightlyNativeExecutionArguments` expands the safe suffix sibling in FH; `forbidden-not-launched`/`exact-uid-union` fails with actual terminal UIDs. No real provider process is involved. |
| S4 | `TestSetTimingTests.C1313_ProducerReceipts` | Existing RowRunner executes its owned child successfully but writes no durable per-set completion; independent reader fails `producer-receipt`. Absence of an event after a completed production run is the missing behavior, not a missing API/build failure. |
| S5 | `TestTimingReportTests.C1313_ComparableHistory` | Test-only legacy characterization uses `TrxReport.Parse(...).SlowClasses` ordering with overlapping durations: class X has sum 8/window 5, class Y sum/window 6. The expected wall ranking Y-before-X fails `wall-ranking`. After S5 the ordinary method invokes only the production report CLI and ten-run data. No invented legacy report command. |
| S6 | `ReleaseGateReportDeliveryTests.C1313_ProductionReceipt` | Run existing production cut/report path with owned fixture leaves; recipient has no bound correlated typed prepublication report/producer ledger (`producer-to-card-receipt`). A missing coordinator remains an integration prerequisite, not a green substitute. Count this one E2E red separately; do not run all E2E. |
| S7 | `ChangeAwareReleaseTests.C1313_RolloutContract` | Existing owner docs omit the new scope/qualification/root-retention contract; explicit `rollout-unqualified` contract assertion red. Documentation-only, not proof of runtime qualification. |

Brand-new CLI/API negative guards without a callable old seam obtain their sensitivity proof from the post-land PCs, **not** a claimed pre-fix API-not-found red. Record characterization limitations and exact red commits; never claim those adapters are production-entrypoint acceptance. Final V/R uses no legacy fallback adapter. Red-first commands are the seven authorized additional runs, not repeated broad CP batteries.

### Guard inventory

Each row is one independently bypassable safety guard in the changed selection/timing/authority path. Existing untouched guards keep R-1..R-3 coverage; this inventory does not commission mutation of unrelated historical safety systems. No guard is justified away. Pure sorting/rounding/presentation checks are ordinary V/R; timing cannot itself confer release authority.

| Guard | Plan reference and invariant | Control |
|---|---|---|
| G-1 | S1/D-2: Every literal test project has exactly one primary/explicit-child role | PC-1 |
| G-2 | S1/D-4: Impact-selected live obligations cannot become unattended passes | PC-2 |
| G-3 | S1/D-5: The fake delivery row stays required and all four real shapes survive | PC-3 |
| G-4 | S1/versioning: Unsupported policy schema refuses | PC-4 |
| G-5 | S1/versioning: Stale canonical policy/catalog hashes refuse | PC-5 |
| G-6 | S2/1: Only full commit object inputs are admitted | PC-6 |
| G-7 | S2/1-2: Git read failure cannot become empty impact | PC-7 |
| G-8 | S2/2: A-pinned policy/catalog owns selection | PC-8 |
| G-9 | S2/4: Core remains for an empty diff | PC-9 |
| G-10 | S2/4: Unknown impact selects Full | PC-10 |
| G-11 | S2/4: Authority/build graph changes dominate narrowing | PC-11 |
| G-12 | S2/3: Nonancestor baseline cannot authorize narrowing | PC-12 |
| G-13 | S2/6: Deleted and changed ownership unions old/new surviving consumers | PC-13 |
| G-14 | S2/2: Ordinal NUL-delimited path identity survives every platform | PC-14 |
| G-15 | S2/5: Explicit provider ownership cannot be inferred from a new filename | PC-15 |
| G-16 | S2/closure: Linked Compile consumer is retained | PC-16 |
| G-17 | S2/closure: Build-only/staged-child runtime consumer is retained | PC-17 |
| G-18 | S2/closure: Imported helper consumer is retained | PC-18 |
| G-19 | S2/closure: Client bundle consumer is retained | PC-19 |
| G-20 | S2/5: Global Full dominates overlapping narrow matches | PC-20 |
| G-21 | S2/DAG: Missing dependency/prerequisite targets cannot authorize reduced work | PC-21 |
| G-22 | S2/catalog: Invalid rule syntax refuses instead of omitting impact | PC-22 |
| G-23 | S3/D-5: Exact compiler prevents forbidden sibling launch | PC-23 |
| G-24 | S3/ledger: Missing required expanded UID blocks completeness | PC-24 |
| G-25 | S3/ledger: Extra executed UID blocks completeness | PC-25 |
| G-26 | S3/ledger: Duplicate executed UID blocks completeness | PC-26 |
| G-27 | S3/ledger: A required skip is not success | PC-27 |
| G-28 | S3/ledger: Zero required executions cannot be green | PC-28 |
| G-29 | S3/remainder: Fresh unclassified candidate UID cannot inherit not-impacted | PC-29 |
| G-30 | S4/D-8: Elapsed wall/envelopes cannot be duration sums | PC-30 |
| G-31 | S4/D-8: Invalid/missing timestamps stay unknown | PC-31 |
| G-32 | S4/discovery: Category metadata must bind the producing assembly | PC-32 |
| G-33 | S4/producers: Wrapped checkpoint has exactly one timing producer | PC-33 |
| G-34 | S4/order: Completion follows owned-child join | PC-34 |
| G-35 | S4/root: Run/attempt components cannot traverse the root | PC-35 |
| G-36 | S4/root: Hosted timing root cannot live under disposable custody | PC-36 |
| G-37 | S4/root: Normal output cleanup cannot erase retained timing history | PC-37 |
| G-38 | S4/DL-2: Append acknowledgement requires completed stable flush | PC-38 |
| G-39 | S4/DL-2: Exact event replay is idempotent | PC-39 |
| G-40 | S4/DL-2: Conflicting same-id bytes are an integrity error | PC-40 |
| G-41 | S4/DL-2: Sequence/ownership is recovered before appending | PC-41 |
| G-42 | S4/DL-2: Failed enqueue/append cannot discard recoverable original spool | PC-42 |
| G-43 | S4/DL-2: Hard death without terminal evidence stays unfinished | PC-43 |
| G-44 | S4/DL-2: Persistence failure is separate from test outcome | PC-44 |
| G-45 | S4/reader: Only a torn final fragment may be ignored | PC-45 |
| G-46 | S5/D-8: Incomparable timing series cannot be pooled | PC-46 |
| G-47 | S5/D-9: Failed/retried attempt cost remains and shared builds count once | PC-47 |
| G-48 | S5/D-9: Timing budgets cannot omit required coverage | PC-48 |
| G-49 | S6/modes: Shadow executes the Full census | PC-49 |
| G-50 | S6/qualification: Explicit current qualification is required for reduced credit | PC-50 |
| G-51 | S6/baseline: Only compatible Full B is a baseline, never selected/readiness/member credit | PC-51 |
| G-52 | S6/baseline: Unavailable usable baseline forces Full before launch | PC-52 |
| G-53 | S6/baseline: Contradictory claimed baseline authority refuses | PC-53 |
| G-54 | S6/baseline: Qualification binds current policy/catalog/selector/runtime hashes | PC-54 |
| G-55 | S6/candidate: Fresh candidate build/discovery belongs to A | PC-55 |
| G-56 | S6/omissions: Every omission is recomputed from pinned Git input | PC-56 |
| G-57 | S6/credit: Scoped selected success cannot claim Full or master readiness | PC-57 |
| G-58 | S6/versioning: Old Full readers reject new scope/schema authority | PC-58 |
| G-59 | S6/hops: Frozen selection identity survives every entrypoint hop | PC-59 |
| G-60 | S6/train: Aggregate B/A/membership drift invalidates prior selection | PC-60 |
| G-61 | S6/train: Failed aggregate/recomposition forces Full rerun | PC-61 |
| G-62 | S6/DL-3: Production creates authority/plan/ledger from durable intent | PC-62 |
| G-63 | S6/DL-3: Failed report enqueue/write retains recoverable intent | PC-63 |
| G-64 | S6/DL-3: Lost acknowledgement recovery reuses report identity | PC-64 |
| G-65 | S6/DL-3: Typed prepublication receipt binds the intended card/run | PC-65 |
| G-66 | S6/DL-3: Current candidate publication requires recipient readback | PC-66 |
| G-67 | S6/DL-3: Receipt revision must equal current recipient revision | PC-67 |
| G-68 | S6/DL-3: Readback body must contain the whole matching run correlation | PC-68 |
| G-69 | S6/public output: Public manifest excludes private timing data | PC-69 |

| G-70 | S1/D-4: Other-platform obligations cannot earn current-platform behaviour credit | PC-70 |
| G-71 | S1/D-4: Unknown resource requirements cannot be unattended runnable | PC-71 |
| G-72 | S2/DAG: Dependency/prerequisite cycles refuse reduced execution | PC-72 |
| G-73 | S2/catalog: Rule evaluation has a finite bound and timeout refuses | PC-73 |
| G-74 | S4/discovery: Category joins use stable expanded identities, never display names | PC-74 |
| G-75 | S6/candidate: Dirty or changed candidate source cannot earn scoped authority | PC-75 |
| G-76 | S6/DL-3: Readback must be the bound release card, not another card | PC-76 |
| G-77 | S4/DL-2: Concurrent appends to one shard serialize complete records | PC-77 |
| G-78 | S4/producers: Pre-test failure still records attempted cost with unknown case counts | PC-78 |
| G-79 | S2/catalog: Catalog data cannot execute arbitrary script or commands | PC-79 |
| G-80 | S6/qualification: Selector cannot create or approve its own qualification | PC-80 |
| G-81 | S4/order: Run-started event is durable before child work begins | PC-81 |

### Positive controls

Every PC below has one compiling defect and one exact method target. Recipe is inherited from that method's V/R row; variant is the stated single defect; oracle is the named assertion. Reachability requires the restored valid companion, original fixture identity and unchanged unrelated guards. Classification is **planned sensitivity control**, never evidence of an introduced product defect. Mutate production/data guard sites, not the expected result/test/fixture, except PC-3 which deliberately mutates production test inventory metadata. Match the specific expected assertion; unrelated refusal, build error, timeout or zero execution is not red.

Mutation runs **break/red/restore/fresh-build/green after land** on its SourceLanding snapshot. Code runs ordinary V/R; Review judges source/evidence before land. Execute all PCs, none sampled. Before Mutation, Code binds each semantic mutation site below to the actual file/symbol and a minimal patch, records the exact test FQN/expanded count and valid fixture payload digest in the bound pack. Newly introduced function/queue names and payload schemas do not exist yet: they stay **binding required**, including after Code if not supplied. Review must not silently promote an unbound control to executable or clean. Mutation must retain per-PC evidence and restoration externally.

For each row use `/*/*/<Class>/<ExactMethod>` (split the qualified name in the table at its final dot), never its whole checkpoint/class filter. `C1313_PathIdentity` runs all three argument rows; every other target is one outer execution. Independent defects may batch only across different production files **and** different test methods, with each exact result retained. No broad mutation matrix. The count reflects one single-defect control per independent guard across seven slices; there is no per-field/type/transport Cartesian expansion. Ordinary V/R stays at 62 executions.

| PC | Break | Exact method expected red | Decisive assertion |
|---|---|---|---|
| PC-1 | G-1: ignore an unknown fixture-looking project in the project-role validator | `TestSelectionCatalogTests.C1313_ProjectAndCatalogUniverse` | `unclassified-project` |
| PC-2 | G-2: admit the exact ClaudeHeadedLongTests row as unattended without its gate | `TestSelectionCatalogTests.C1313_LiveEligibility` | `live-obligations-visible` |
| PC-3 | G-3: drop only the real batch Arguments attribute (compiles) | `ClaudeVerifiedDeliveryTests.C1313_DeliveryDiscovery` | `five-delivery-uids` |
| PC-4 | G-4: add fixture schema 7 to the supported versions | `ReleaseGatePolicyTests.C599_ProfileSchema` | `C599 ProfileSchema unknown version refuses` |
| PC-5 | G-5: bypass the canonical hash inequality check | `ReleaseGatePolicyTests.C599_ProfileSchema` | `C599 ProfileSchema stale hash refuses` |
| PC-6 | G-6: resolve the valid fixture's 12-hex candidate abbreviation instead of refusing it, then continue with that commit | `TestSelectionTests.C1313_InputRefusal` | `invalid-intent-refused` |
| PC-7 | G-7: convert a nonzero diff exit into an empty path list | `TestSelectionTests.C1313_InputRefusal` | `git-read-refused` |
| PC-8 | G-8: read the edited working-tree catalog instead of the candidate blob | `TestSelectionTests.C1313_PinnedPolicy` | `candidate-blob` |
| PC-9 | G-9: return an empty selected set for B=A | `TestSelectionTests.C1313_CoreAndUnknownFallback` | `core-seven` |
| PC-10 | G-10: return core-only for the unknown fixture path | `TestSelectionTests.C1313_CoreAndUnknownFallback` | `unknown-full` |
| PC-11 | G-11: omit the selection-authority-changed full fallback for a changed catalog | `TestSelectionTests.C1313_AuthorityChanges` | `authority-full` |
| PC-12 | G-12: remove the ancestry-to-Full branch | `TestSelectionTests.C1313_AuthorityChanges` | `nonancestor-full` |
| PC-13 | G-13: look up deletion ownership only in A | `TestSelectionTests.C1313_PathIdentity` | `old-new-union` |
| PC-14 | G-14: lowercase parsed Git paths before ownership lookup | `TestSelectionTests.C1313_PathIdentity` | `ordinal-paths` |
| PC-15 | G-15: classify the unlisted ClaudeNewShared fixture by its prefix | `TestSelectionDependencyTests.C1313_ProviderAndShared` | `shared-all-providers` |
| PC-16 | G-16: drop the one fixture linked-Compile edge | `TestSelectionDependencyTests.C1313_RuntimeEdges` | `linked-consumer` |
| PC-17 | G-17: drop the staged-child edge while leaving normal references intact | `TestSelectionDependencyTests.C1313_RuntimeEdges` | `staged-consumer` |
| PC-18 | G-18: drop the helper import edge | `TestSelectionDependencyTests.C1313_RuntimeEdges` | `helper-consumer` |
| PC-19 | G-19: drop the client/dist runtime edge | `TestSelectionDependencyTests.C1313_RuntimeEdges` | `bundle-consumer` |
| PC-20 | G-20: stop matching after the first provider rule | `TestSelectionDependencyTests.C1313_ProviderAndShared` | `full-dominates` |
| PC-21 | G-21: ignore a missing prerequisite target | `TestSelectionDependencyTests.C1313_InvalidGraph` | `invalid-graph` |
| PC-22 | G-22: catch regex validation/timeout and treat it as no match | `TestSelectionDependencyTests.C1313_InvalidGraph` | `invalid-rule` |
| PC-23 | G-23: restore the legacy class-prefix star for the colliding class | `TestSelectionExecutionTests.C1313_ExactExpandedExecution` | `forbidden-not-launched` |
| PC-24 | G-24: ignore the missing UID set | `TestSelectionExecutionTests.C1313_CoverageRefusal` | `missing-uid` |
| PC-25 | G-25: ignore the extra executed UID set | `TestSelectionExecutionTests.C1313_CoverageRefusal` | `extra-uid` |
| PC-26 | G-26: deduplicate duplicate terminal UIDs silently | `TestSelectionExecutionTests.C1313_CoverageRefusal` | `duplicate-uid` |
| PC-27 | G-27: normalize required Skipped to Passed | `TestSelectionExecutionTests.C1313_CoverageRefusal` | `required-skip` |
| PC-28 | G-28: accept a zero-required/zero-executed ledger | `TestSelectionExecutionTests.C1313_CoverageRefusal` | `zero-required` |
| PC-29 | G-29: assign the new fixture UID not-impacted instead of suite remainder | `TestSelectionExecutionTests.C1313_NewUidRemainder` | `new-uid-required` |
| PC-30 | G-30: assign caseDurationSumSeconds to caseWindowSeconds | `TestSetTimingTests.C1313_WallEnvelopes` | `wall-not-sum` |
| PC-31 | G-31: coalesce an unavailable wall measurement to zero | `TestSetTimingTests.C1313_UnavailableMeasurements` | `unknown-not-zero` |
| PC-32 | G-32: accept the fixture foreign assembly digest in the metadata join | `TestSetTimingTests.C1313_DiscoveryJoin` | `category-build-bound` |
| PC-33 | G-33: enable both tool and wrapper row completion writers | `TestSetTimingTests.C1313_ProducerReceipts` | `one-row-producer` |
| PC-34 | G-34: append terminal completion before releasing/joining the held child | `TestSetTimingTests.C1313_ProducerReceipts` | `completion-after-join` |
| PC-35 | G-35: allow ../escape as the run component | `TimingJournalTests.C1313_RootCustody` | `path-component-refused` |
| PC-36 | G-36: accept the output-contained hosted root | `TimingJournalTests.C1313_RootCustody` | `root-outside-custody` |
| PC-37 | G-37: include the external timing shard in the producer cleanup list | `TimingJournalTests.C1313_RootCustody` | `history-survives-output` |
| PC-38 | G-38: return append success before the injected flush barrier completes | `TimingJournalTests.C1313_RecoveryHandoffs` | `flush-before-ack` |
| PC-39 | G-39: append an exact duplicate event instead of recognizing it | `TimingJournalTests.C1313_ReplayIdentity` | `replay-once` |
| PC-40 | G-40: accept the changed payload with the same event id | `TimingJournalTests.C1313_ReplayIdentity` | `event-conflict` |
| PC-41 | G-41: restart sequence at zero for an existing shard | `TimingJournalTests.C1313_ReplayIdentity` | `sequence-resumed` |
| PC-42 | G-42: remove spool after a failed append | `TimingJournalTests.C1313_RecoveryHandoffs` | `recoverable-handoff` |
| PC-43 | G-43: synthesize success for a start-only attempt | `TimingJournalTests.C1313_RecoveryHandoffs` | `unfinished-not-success` |
| PC-44 | G-44: overwrite failed test outcome with pass when the timing writer fails | `TimingJournalTests.C1313_RecoveryHandoffs` | `persistence-not-test-outcome` |
| PC-45 | G-45: ignore a malformed complete middle record | `TimingJournalTests.C1313_TornTail` | `corrupt-not-truncated` |
| PC-46 | G-46: omit membership digest from the comparability key | `TestTimingReportTests.C1313_ComparableHistory` | `comparable-series` |
| PC-47 | G-47: filter failed attempts out of total incurred cost | `TestTimingReportTests.C1313_AttemptCost` | `attempt-cost` |
| PC-48 | G-48: drop an expensive set when its p90 exceeds the budget | `TestTimingReportTests.C1313_EstimatesNeverSelect` | `budget-never-omits` |
| PC-49 | G-49: execute the proposed narrow union in Shadow | `ChangeAwareReleaseTests.C1313_ModeCredit` | `shadow-full` |
| PC-50 | G-50: treat absence/revocation of activation as qualified | `ChangeAwareReleaseTests.C1313_ModeCredit` | `unqualified-no-credit` |
| PC-51 | G-51: accept a ChangeAware receipt as the Full baseline | `ChangeAwareReleaseTests.C1313_BaselineAuthority` | `baseline-full-only` |
| PC-52 | G-52: keep narrow mode when baseline receipt is absent | `ChangeAwareReleaseTests.C1313_BaselineAuthority` | `baseline-absent-full` |
| PC-53 | G-53: treat a foreign baseline digest as missing and fall back silently | `ChangeAwareReleaseTests.C1313_BaselineAuthority` | `baseline-corrupt-refused` |
| PC-54 | G-54: accept a baseline with a changed selector contract hash | `ChangeAwareReleaseTests.C1313_BaselineAuthority` | `contract-hash-bound` |
| PC-55 | G-55: accept discovery built at B for candidate A | `ChangeAwareReleaseTests.C1313_CandidateBinding` | `candidate-build-bound` |
| PC-56 | G-56: trust the edited not-impacted list after its local hashes are repaired | `ChangeAwareReleaseTests.C1313_ScopeAuthority` | `forged-omission-refused` |
| PC-57 | G-57: emit coverageScope=Full for a result with omissions | `ChangeAwareReleaseTests.C1313_ScopeAuthority` | `selected-not-full` |
| PC-58 | G-58: admit new scoped authority through the legacy Full reader | `ChangeAwareReleaseTests.C1313_ScopeAuthority` | `legacy-rejects-scoped` |
| PC-59 | G-59: omit BaselineSha only from the self-reexec argument list | `ChangeAwareReleaseTests.C1313_ParameterHops` | `hop-baseline-bound` |
| PC-60 | G-60: reuse the selection after the aggregate membership digest changes | `ChangeAwareReleaseTests.C1313_TrainAggregate` | `aggregate-bound` |
| PC-61 | G-61: reuse narrow selection after recomposition failure | `ChangeAwareReleaseTests.C1313_TrainAggregate` | `recomposition-full` |
| PC-62 | G-62: skip production authority/plan creation while retaining exit-zero child results | `ReleaseGateReportDeliveryTests.C1313_ProductionReceipt` | `producer-ledger-present` |
| PC-63 | G-63: mark report complete when the actual queue/write fails | `ReleaseGateReportDeliveryTests.C1313_ProductionReceipt` | `report-pending-until-receipt` |
| PC-64 | G-64: allocate a new report identity after recipient commit before receipt persistence | `ReleaseGateReportDeliveryTests.C1313_ProductionReceipt` | `one-logical-report` |
| PC-65 | G-65: accept a final/foreign receipt kind as prepublication | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedgerReceipt` | `C599 G-306: a final or foreign receipt kind where the prepublication receipt is required refuses` |
| PC-66 | G-66: use the local ledger copy when recipient GET fails | `ReleaseGatePublicationAuthorityTests.C599A_ReceiptReadback` | `C599 V-12: an unavailable, failed, unknown or non-object release card readback refuses (fail closed)` |
| PC-67 | G-67: ignore the current-card revision inequality | `ReleaseGatePublicationAuthorityTests.C599A_ReceiptReadback` | `C599 V-12: a receipt revision other than the release card current revision refuses` |
| PC-68 | G-68: ignore a foreign runId in the card body | `ReleaseGatePublicationAuthorityTests.C599A_ReceiptReadback` | `C599 V-12: a release card body without exactly this run correlation refuses` |
| PC-69 | G-69: copy host identity from a timing row into the manifest | `ChangeAwareReleaseTests.C1313_PublicProjection` | `public-allowlist` |

| PC-70 | G-70: mark the Linux-only PDF method runnable on the Windows profile | `TestSelectionCatalogTests.C1313_LiveEligibility` | `other-os-obligation` |
| PC-71 | G-71: default the unknown resource fixture to no prerequisites | `TestSelectionCatalogTests.C1313_LiveEligibility` | `unknown-resource-ineligible` |
| PC-72 | G-72: treat the back edge of the two-node prerequisite cycle as already satisfied | `TestSelectionDependencyTests.C1313_InvalidGraph` | `cycle-refused` |
| PC-73 | G-73: catch the bounded matcher timeout as a successful non-match | `TestSelectionDependencyTests.C1313_InvalidGraph` | `regex-timeout-refused` |
| PC-74 | G-74: join the two colliding display names instead of expanded UID | `TestSetTimingTests.C1313_DiscoveryJoin` | `category-uid-bound` |
| PC-75 | G-75: ignore a dirty candidate observation while preserving its SHA | `ChangeAwareReleaseTests.C1313_CandidateBinding` | `candidate-clean-required` |
| PC-76 | G-76: skip the returned-card id comparison | `ReleaseGatePublicationAuthorityTests.C599A_ReceiptReadback` | `C599 V-12: a readback of any card but the bound release card refuses` |
| PC-77 | G-77: remove the shard writer lock while holding two append calls at the sequence barrier | `TimingJournalTests.C1313_ReplayIdentity` | `serialized-shard` |
| PC-78 | G-78: skip completion emission for the build-failed path | `TestSetTimingTests.C1313_ProducerReceipts` | `failed-attempt-recorded` |
| PC-79 | G-79: accept a script-block rule and evaluate it in the controlled fixture | `TestSelectionDependencyTests.C1313_InvalidGraph` | `data-not-code` |
| PC-80 | G-80: write an active qualification record as a side effect of selection | `ChangeAwareReleaseTests.C1313_ModeCredit` | `qualification-root-unchanged` |
| PC-81 | G-81: launch the child before releasing the start-event flush barrier | `TestSetTimingTests.C1313_ProducerReceipts` | `start-before-launch` |

### Out of scope

- Real provider/headed/API-spend, installed Herdr, real browser PDF and broad Full/Shadow qualification; all remain visible selected obligations. Separate exact-roster commissioning and receipts are required. Fake native/discovery evidence cannot earn these credits.
- Production schedule/Interim activation, deployed train admission, release publication and operational timing-volume retention qualification. RC composition uses a disposable app and fake GitHub destination; it cannot qualify those deployments.
- Unchanged native coordination internals, generic cleanup authority, session queue/reconciliation logic, historical publisher field matrices and whole Unit/assembly runs. Their changed call boundaries receive the named regressions; expanding implementation scope requires an amended manifest before extra runs.
- No claim that discovery executed a test, timing proved a pass, a request delivered a report, or a fake composer check supplied a complete session transcript.

### Checkpoints

Closed ordinary Code scope below; this is the sole executable checkpoint table. All rows require clean committed source, isolated output and verified producing-build binding. Rows sharing output use exactly the same After group. Discovery of each producing assembly (once, same assembly hash) is an explicit prerequisite charged to its build row: record its exact command, hash, expanded roster and wall cost; it is **not** counted in Min. FilterHost discovery/owned-child invocation cost is included in CP-5/CP-9. CP-22 also discovers only the delivery methods before CP-23's five-row assertion; no real TUI runs. This adds no implicit discovery of every repo assembly.

The discovery argument vector is `--list-tests --no-progress --no-ansi --diagnostic --diagnostic-output-directory <owned-discovery-dir>` against the producing assembly (`dotnet <absolute-dll>` where UseAppHost=false). Use the existing owned-process boundary and `ConvertFrom-NightlyDiagnosticLog` / `ConvertTo-NightlyDiscoveryDocument` with the actual assembly hash; the scheduler holds the build slot for this prerequisite. Never write discovery output beside a sealed DLL. CP-22 discovery adds the exact class filter `/*/*/ClaudeVerifiedDeliveryTests/*`; CP-23 independently selects the five delivery UIDs from that metadata. S1-S3 can use the existing discovery producer before S4 adds its timing integration.

Portable rows CP-1..18 run on one eligible .NET/PowerShell/Git host. CP-19..23 require Windows (CP-22 requires staged fakeclaude and native PTY). CP-24 requires the isolated E2E app/PostgreSQL/Docker/runner prerequisites; use Windows if required by the supplied coordinator, otherwise portable. Missing required capability is incomplete, never an early-return pass. Do not count a Linux execution of a Windows method as the Windows row.

Use the checkpoint tool through the host slot gate once per committed group, then await the run to its terminal result (75 means continue waiting). Example: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1313-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-16-card-1313-change-aware-selection-plan.md --rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8`. Restrict each subsequent run to its named rows; do not rebuild the same group for every method. Main/Pty rows run sequentially; native child rows are serial. Preserve exact CHECKPOINT lines, full verified SHA and fresh source/build receipts; Code/Review run the evidence-diff guard. Remove only owned alternate outputs through current cleanup APIs after all consumers finish.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1313-selection/` | catalog | `/*/*/TestSelectionCatalogTests/*` | V-1 | exactly 3 outer executions; named roster; 0 failed/skipped | 3 | 6 | false |
| CP-2 | S1-S3 | `CP-1` | policy | `/*/*/ReleaseGatePolicyTests/(C599_ProfileSchema*)\|(C599_ProfileSuites*)\|(C599_EligibilityCensus*)\|(C599_ExpandedCoverage*)` | V-1,R-1 | exactly 4 outer executions; named roster; 0 failed/skipped | 4 | 1 | false |
| CP-3 | S1-S3 | `CP-1` | two-sha | `/*/*/TestSelectionTests/*` | V-2 | exactly 8 outer executions; named roster; 0 failed/skipped | 8 | 2 | false |
| CP-4 | S1-S3 | `CP-1` | dependencies | `/*/*/TestSelectionDependencyTests/*` | V-3 | exactly 3 outer executions; named roster; 0 failed/skipped | 3 | 1 | false |
| CP-5 | S1-S3 | `CP-1` | exact-execution | `/*/*/TestSelectionExecutionTests/*` | V-4,R-1 | exactly 3 outer executions; named roster; 0 failed/skipped | 3 | 2 | true |
| CP-6 | S1-S3 | `CP-1` | discovery-regression | `/*/*/NightlyDiscoveryParserTests/C1044_DiscoveryReconciliation` | R-1 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 0.5 | false |
| CP-7 | S1-S3 | `CP-1` | terminal-regression | `/*/*/NightlyExecutionDiagnosticTests/D13_ExecutionGuards` | R-1 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 0.5 | false |
| CP-8 | S1-S3 | `CP-1` | core-limiter | `/*/Antiphon.Tests/ProcessSpawnLimitTests/(Caps_concurrent_process_spawning_tests_at_one*)\|(Process_spawning_classes_carry_the_limiter*)` | R-1 | exactly 2 outer executions; named roster; 0 failed/skipped | 2 | 0.25 | false |
| CP-9 | S4-S5 | `tests/Antiphon.Tests -> bin-c1313-timing/` | timing-measurement | `/*/*/TestSetTimingTests/*` | V-5 | exactly 4 outer executions; named roster; 0 failed/skipped | 4 | 6 | true |
| CP-10 | S4-S5 | `CP-9` | timing-journal | `/*/*/TimingJournalTests/*` | V-6 | exactly 4 outer executions; named roster; 0 failed/skipped | 4 | 3 | true |
| CP-11 | S4-S5 | `CP-9` | timing-report | `/*/*/TestTimingReportTests/*` | V-7 | exactly 3 outer executions; named roster; 0 failed/skipped | 3 | 1 | false |
| CP-12 | S4-S5 | `CP-9` | legacy-duration | `/*/*/TrxReportTests/aggregates_seconds_per_class` | R-2 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 0.25 | false |
| CP-13 | S4-S5 | `CP-9` | receipt-required | `/*/*/RowRunnerTests/no_trx_is_exit_2` | R-2 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 0.25 | false |
| CP-14 | S4-S5 | `CP-9` | build-binding | `/*/*/CheckpointBuildBindingPathTests/Different_directory_or_provenance_is_refused` | R-2 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 0.25 | false |
| CP-15 | S6-S7 | `tests/Antiphon.Tests -> bin-c1313-release/` | release-selection | `/*/*/ChangeAwareReleaseTests/*` | V-8,V-9,V-10 | exactly 8 outer executions; named roster; 0 failed/skipped | 8 | 7 | false |
| CP-16 | S6-S7 | `CP-15` | full-authority | `/*/*/ReleaseGateAuthorityContractTests/(C599B_PinnedPolicy*)\|(C599B_AllChunks*)` | R-3 | exactly 2 outer executions; named roster; 0 failed/skipped | 2 | 1 | false |
| CP-17 | S6-S7 | `CP-15` | receipt-authority | `/*/*/ReleaseGatePublicationAuthorityTests/(C599A_ExecutionLedgerReceipt*)\|(C599A_ReceiptReadback*)` | R-3 | exactly 2 outer executions; named roster; 0 failed/skipped | 2 | 1 | false |
| CP-18 | S6-S7 | `CP-15` | credit-coordination | `/*/*/ReleaseGateRunTests/(C599_CreditVerdicts*)\|(C599_MasterStateIsolation*)\|(C599_SharedLock*)` | R-3 | exactly 3 outer executions; named roster; 0 failed/skipped | 3 | 1 | false |
| CP-19 | all | `tests/Antiphon.Tests -> bin-c1313-windows/` | windows-git-paths | `/*/*/TestSelectionTests/C1313_PathIdentity` | V-2 | exactly 3 outer executions; named roster; 0 failed/skipped | 3 | 5 | false |
| CP-20 | all | `CP-19` | windows-journal | `/*/*/TimingJournalTests/C1313_ReplayIdentity` | V-6 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 1 | false |
| CP-21 | all | `CP-19` | windows-forwarding | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | V-8 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 1 | false |
| CP-22 | all | `tests/Antiphon.Agents.Pty.Tests -> bin-c1313-fake/` | fake-delivery | `/*/*/ClaudeVerifiedDeliveryTests/Fake_evidence_appears_then_submit_completes_a_turn` | V-1 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 6 | true |
| CP-23 | all | `CP-22` | delivery-metadata | `/*/*/ClaudeVerifiedDeliveryTests/C1313_DeliveryDiscovery` | V-1 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 0.5 | true |
| CP-24 | all | `tests/Antiphon.E2E -> bin-c1313-recipient/` | producer-recipient | `/*/*/ReleaseGateReportDeliveryTests/C1313_ProductionReceipt` | V-8,V-10,R-3 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 7 | true |

The table totals **62 outer TUnit executions**, including the three argument-expanded path cases twice (portable + Windows). FH argument executions, script PASS lines and five discovered delivery rows are separate receipts, not additions to Min. Combined method operands use the pinned runner's required trailing stars, but their permitted executed names are exactly the listed methods; any suffix collision splits that row before execution and is reported as a manifest correction. New-class `*` filters refer only to the finite method inventories above, never to a category/assembly lane.

### Cost

All numbers are **estimated minutes**, not measured timings or a savings claim. TestDesign execution cost: **0** builds/tests/PCs. Ordinary Code V/R floor = sum of CP-1..24 EstimatedMinutes = **54.5**. This contains six isolated producing builds (estimated **22**) plus six once-per-build category discoveries (**3**) and narrow V/R/fixture execution (**29.5**). Builds/discovery are included, not charged twice.

Seven exact red-first filters above add **15** (isolated incremental setup/build **7**, assertion runs/owned E2E recipient **8**); no whole CP group runs red. Code verification budget before authoring = **69.5**. One preflight/manifest/source-pack setup allowance = **2** outside that floor. CP-24 adds 7 minutes above the original 47-minute proposal because validator fixtures cannot prove the release producer/recipient prerequisite; finer filters and shared builds offset other additions. Full live qualification has no invented cost/receipt here and needs its separately commissioned roster.

Post-land Mutation floor: **81** exact-method PC cycles, broken down below. Each cycle includes its own compiling defect, build, intended assertion red, restore, fresh build and green; no build/green reuse across changed source and no savings assumed from batching.

| PC filters / IDs | Cycles | Build + red + restore + build + green estimate per cycle | Subtotal |
|---|---:|---|---:|
| SG/RA/report exact methods in the PC table; PC IDs 1,2,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,65,66,67,68,69,70,71,72,73,75,76,79,80 | 57 | 0.5 + 0.3 + 0.2 + 0.5 + 0.5 = 2 | 114 |
| TJ exact methods in the PC table; PC IDs 30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,74,77,78,81 | 20 | 0.75 + 0.5 + 0.25 + 0.75 + 0.75 = 3 | 60 |
| ClaudeVerifiedDeliveryTests.C1313_DeliveryDiscovery; PC IDs 3 | 1 | 1 + 0.5 + 0.5 + 1 + 1 = 4 | 4 |
| ReleaseGateReportDeliveryTests.C1313_ProductionReceipt; PC IDs 62,63,64 | 3 | 1 + 1.5 + 0.5 + 1 + 2 = 6 | 18 |

Mutation setup/binding/discovery/report allowance **5**; PC floor **196**; total Mutation verification estimate **201**. Combined ordinary V/R + PC floors = **250.5**. Total planned setup/build/red-first/V/R/each-PC-red-restore-green = **2 + 15 + 54.5 + 5 + 196 = 272.5 minutes**, excluding authoring and separately commissioned rollout. If actual incremental builds exceed the estimate, retain measured cost and update the budget; do not omit controls.

Savings: six producing builds instead of 24 row-local builds saves **18 builds**; at a conservative 2 minutes each, **36 estimated minutes**. No elapsed percentage saving is inferred from CARD-1287's incomparable rosters. Mutation batching savings are **0** until independent file/method bindings prove them.

Handoff audit: **guards=81, mapped=81, missing=0, duplicate PC maps=0**. All 81 PCs are required to run post-land; **executed now=0**, not PC-clean. No unexecuted or unbound control may be reported as passed. Ordinary scope is the union of the 24 CP rows; the seven red-first runs are explicitly budgeted. Code must return the bound mutation pack, actual expanded counts and delivery bindings, run all ordinary rows, then `next: review`. Missing DL-3 coordinator/queue/payload binding instead retains incomplete S6 and returns `next: plan`. No deployment or human decision is implied by this TestDesign handoff.
