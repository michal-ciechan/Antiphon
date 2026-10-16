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
