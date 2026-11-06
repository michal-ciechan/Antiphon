# CARD-1729 Phase 0 verification design

Date: 2026-11-06. TestDesign task: `65e6f8b0`. Source inspected:
`73fe0aa291e35d8bb6703f70d5255381e0ff4b8e` (`origin/feat/card-task-b8eac553`, the Phase 0 plan on
top of master `1527c29fef2737bf3eaa49f2293d82d85402265a`); master at inspection was `0dbd8295a`,
four commits past the plan base, none touching a Phase 0 file.

This appends the verification contract for **Phase 0 of CARD-1729** to
[the unchanged fix design](../plans/2026-11-05-card-1729-phase0-plan.md), following the
[S5b-1](2026-11-05-card-0824-s5b-1-testdesign.md) and [S5b-2](2026-11-05-card-0824-s5b-2-testdesign.md)
format. It does not rewrite D-1..D-10. It designs `C1729LayeringGuardTests` (the resolver, the two
files it reads, the red witnesses and the seeding procedure), the witnesses for P0-B, P0-C5, P0-F2,
P0-H2 and P0-I, the D-9 parity check, the Wave 0 mechanical checklist with its classification-policy
answers, and it freezes the 36-row Checkpoints table. No build, test, product or test-source change
was made here; the only tool run was the read-only importer check named under Binding and import
handoff. Nothing runs on the desktop. No csproj, props, targets or sln edit is admitted in Phase 0
(D-5).

Three plan numbers are corrected below from a fresh roster read, not from the spec: `Min` is a TUnit
execution count and argument-expanded cases change nine rows; `RemoteScriptContractTests` is 227
`[Test]` methods and 499 nightly nodes at pace 4,591 serial seconds, so CP-30 selects its consumers
instead; `CardFileBoardLookupTests` is one partial class across three files (`CardFileBoardLookupTests.cs`,
`CardFileBoardLookupRepairTests.cs`, `CardFileBoardLookupEventTests.cs`; 18 methods, 45 cases), so its
class filter is right and a file-name filter would run zero tests.

## Verification design

### Inspection

`S/` = `server/Application/Services/`; `A/` = `tests/Antiphon.Tests/Application/`;
`H/` = `tests/Antiphon.Tests/TestHelpers/`; `I/` = `tests/Antiphon.Tests/Infrastructure/`.
Locations describe the inspected source at `73fe0aa29`. New files have no line yet; their nearest
anchor is cited and their final Code SHA, symbol, line and diff **remain binding required after
Code**. The absence of a future type is never a compiling mutation or an assertion-red witness.

| Bodies read | Boundaries -> witnesses or exclusion |
|---|---|
| Plan: evidence table, D-1..D-10, layering map, all 20 slices, conflict order, Phase 1-4 sequencing, performance budget, Checkpoints, roster audit; spec Phase 0-4 and the checks every phase must pass (`:57-65`) | Slice scope, map and ratchet -> V-1..V-12; digest-changing items -> exclusions |
| `H/TestLaneCategoryGuardTests.cs` (whole file: `RepoRoot` walk to `Antiphon.sln`, `IsBuildOutput`, line-based census) | Repo-root discovery for the two `Layering/*.txt` files -> V-1; root derivation -> V-11, G-32 |
| `tests/Shared/TestClassificationGuardTests.cs`, `tests/Shared/TestClassificationMetadata.cs:145-230` (`unknown`, `unmarked-registered`, `unregistered-marked`, `lane-xor`), `tests/Antiphon.Tests/slow-tests-allowlist.txt` (410 lines; `AgentTaskLandVerifierTests`, `TaskParkPublicationTests`, `TestDbFixtureLazyInitializationTests` are the Slow classes this design selects) | Classification-policy answers per slice; no new class below is Slow |
| `tests/test-execution-policy.json` (keys `policyHash, defaultSuites, profiles, projects, suites, safeEnvironment, scriptCensus, watchdogs, reducedDispatchPolicy, projectDispositions, selectionCatalog`; no test class name appears in the file), `scripts/lib/nightly-policy.ps1:52-67` (`Get-NightlyPolicyHash` hashes that one file minus `policyHash`), `tests/test-selection-policy.json` (`catalogHash`), `scripts/lib/nightly-tests-impl.ps1:145-165` (unknown class -> node model 0.5 s/node), `tests/linux-test-roster.json` (frozen, `sourceCommit ceff11d83`; no `.ps1`/`.cs` consumer at this base, docs only), `tests/live-lanes.json`, `tests/fixtures/live-lanes/{pre-s1-base.sha,full-discovery-before.json}`, `scripts/lib/live-lane-manifest.ps1:2541-2600` (`Invoke-LiveLaneCensus`), testing-and-build "CARD-1545 CP-30 census scope" | Which policy artefacts a new or renamed test class touches -> Wave 0 checklist item 7 |
| `tests/Antiphon.Tests/Antiphon.Tests.csproj:23` (`Microsoft.CodeAnalysis.CSharp` 5.9.0), `tests/Antiphon.Tests/Scripts/NativeLinuxPolicyWitnessMutationTests.cs`, `.../C1008PlatformContractTests.cs` (existing `CSharpSyntaxTree.ParseText` users) | Nearest fixture for the syntax-level resolver -> V-1 |
| `server/` tree census: 1,349 non-migration `.cs` files, 412 migration files; folders `Api/{Endpoints,Middleware}`, `Application/{Services 572, Dtos 121, Settings 79, Interfaces 74, Exceptions 28}`, `Domain/{Entities,Enums,StateMachine,ValueObjects}`, `Infrastructure/{Agents 78 (root, Pty, SessionRunner, Tools, Tui), Git 38, Orchestration 19, Supervision 15, Data 14, Security 6, Files 5, GitHub 4, FileSystem 4, WorkflowDefinitions 3, Resilience 3, IssueTrackers 3, Realtime 2, Logging 2, WorkspaceHooks 1, Verification 1, Messaging 1, ExternalChanges 1}`, `Program.cs`; `server/{Bundles,Properties,Runtime}` hold no `.cs` | Map coverage oracle -> V-1 (G-1, G-2) |
| `server/Infrastructure/Resilience/{ResilienceReadClients,DatabaseResilienceExecutor,NpgsqlTransientFailureClassifier}.cs`; `S/LlmProviderService.cs:316`, `S/ProjectService.cs:414`, `S/WorkflowTemplateService.cs:3,15-17`; `I/Resilience/ResilienceCircuitTests.cs` (6 Unit methods, namespace `Antiphon.Tests.Infrastructure.Resilience`) | P0-F2 finding and witness -> V-7; map correction (NpgsqlTransientFailureClassifier is Persistence) |
| `S/../Settings/DelegationSettings.cs:1356-1387`; `S/ChannelReplyPreparation.cs:39-40,161,165`; `S/DispatchConcurrencyPolicy.cs:27`; `S/SubscriptionQuotaPolicy.cs:14`; `S/PipelineStagesJson.cs:62-63`; `S/SubscriptionUsageReader.cs:63`; `S/PipelineHandoff.cs`; `S/DelegationOpenGate.cs:19,140`; `S/AgentTaskService.cs:1059,3202-3207`; `A/RolePolicyConfigurationValidationTests.cs:35-39` ("Use ClaudeCode, Grok or Codex"); `A/ChannelOutboundPolicyTests.cs:576-579` (`MatchesMarkdownSources`); `A/DispatchConcurrencyPolicyTests.cs` | P0-B boundary witnesses -> V-4, R-2, R-3 |
| `server/Application/Interfaces/ILandingGit.cs:18-142`; `S/GitIndexLock.cs:15-17,31,195`; `S/ServerGitProcess.cs:16`; `server/Infrastructure/Git/LandWorkspace.cs:16-18,116`; `server/Infrastructure/Git/LandVerificationArtifacts.cs:14,18,216`; member use in `S/{AgentTaskLandService,AgentTaskLandingProtocol,AgentTaskReplyService,DelegationWorkspaceResolver,DelegationWorktreeService}.cs` (`GitIndexLock.{PathsEqual x9,StaleAfter x2,Refusal x2,PathErrorReason,StaleCode,HeldCode}`, `LandWorkspace.{Scan x2,LockReason}`, `LandVerificationArtifacts.{RequireNoLinks x2,CreateResultsDirectory}`, `ServerGitProcess.DisableAutoMaintenance x3`); `A/GitIndexLockTests.cs`, `I/ServerGitProcessTests.cs`, `I/LandWorkspaceTests.cs`; no test references `RequireNoLinks` | P0-C5 port surface -> V-5; the missing link-refusal witness -> V-5 |
| `src/Antiphon.Messaging.Client/{IAntiphonMessagingProducer,IAntiphonMessagingConsumer,KafkaAntiphonMessagingProducer}.cs` (the producer never catches `ProduceException`; it propagates), `src/Antiphon.Messaging.Client.Testing/FakeAntiphonMessagingClient.cs`, `S/ChannelOutboundDeliveryPump.cs:5,497-498,583,591` (`using Confluent.Kafka`), `server/Infrastructure/Messaging/InboundUnconsumedEventConsumer.cs` (also Confluent; Host layer, keeps the package), `server/Antiphon.Server.csproj:72`, `src/Messaging.Pack.props:15-16`, both `PublicAPI.{Shipped,Unshipped}.txt`, `tests/Antiphon.Messaging.Tests` (namespaces `Antiphon.Messaging.Tests[.Client|.Gateway|...]`, links `TestClassificationGuardTests`), `A/ChannelOutboundRetryPolicyTests.cs:26-502` (22 methods; `QueueFull()` at `:502` and `:318` construct `ProduceException`), the three other Antiphon.Tests files constructing `ProduceException` | P0-H2 mapping and pump witnesses -> V-8, delivery inventory |
| `server/Program.cs:71-1016` (52 `AddHostedService` lines, first `:368` `OrchestratorInstructionsHostedService`, last `:982` `SeatOccupancyHostedService`), `H/AntiphonWebAppFactory.cs:35,140-150` (`SharedType.PerTestSession`; `RemoveAll<ISessionRunnerClient>`, `RemoveAll<IZombieProcessCensus>`, one `Remove(descriptor)`), `I/HangfireStartupSafetyTests.cs:30,39,44,83` | P0-I composition witness -> V-9 |
| `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` (9,474 lines; `LinuxShell` `:9269`, `LinuxShellResult` `:9272`, private `C983Fixture` `:4835`); member consumers `I/{Codex,Grok}RunnerImageContractTests.cs`, `Scripts/{CacheFixtureCompletionTests,RollingVolumeRecycleScriptTests,C1361CensusProbes}.cs`; file-name readers `Scripts/ProcessSpawnTimeoutPolicyTests.cs:28`, `Scripts/C1008PlatformContractTests.cs:76`, `Checkpoints/PlanCoveragePcTests.cs:148-155` | P0-T1a extraction set and witness row -> V-10 |
| `H/ProductionRunnerGuard.cs:58`, `H/PtyBackendEnvGuard.cs:17`, `H/TestDbFixture.cs:28,47`, `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweep.cs:10,20`; `H/TestDbFixtureLazyInitializationTests.cs` (10 methods; `A_*` six, `Mixed_*` one, `Child_*` three; `A_worker_child_exits_before_the_shared_store_warmup` carries `[MethodDataSource(nameof(WorkerMarkers))]`) | P0-T3 hook witnesses -> V-11 |
| `server/Infrastructure/Git/LandingVerifier.cs:15-36`; `S/BlockedTaskParkingService.cs:216-217` (`Digest` = upper-case hex SHA-256 of UTF-8); `server/Infrastructure/Git/LocalTaskParkPublisher.cs:161,174`; `A/TaskParkPublicationTests.cs` (4 methods) | P0-D fingerprint identity -> V-6 |
| Roster census (`[Test]` methods / argument-expanded cases) for every class named in the plan table, cross-checked against `scripts/lib/nightly-class-costs.json` node counts where a row exists (`CardFileBoardLookupTests` 45, `AgentTaskLandVerifierTests` 8, `SessionRunnerEventPumpTests` 8, `ProjectSetupServiceTests` 11, all equal to the static count) | Corrected `Min` column |
| `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs` (header, `Serial`, `Environment`, `Repeat`, reuse-within-After, `checkpoint-filter-or-wildcard`, `checkpoint-filter-or-shape`), testing-and-build Checkpoint manifest, Checkpoint runner tool, Combined class filters, Unit lane checkpoint shards; S5b-1 performance Debug (`origin/feat/card-task-5ae98d31:docs/investigations/2026-11-05-debug-s5b-1-performance.md`, entire report); `docs/mechanical-pc-contract.md` (entire) | Table grammar, caps, admission accounting, mechanical eligibility language |
| In-flight branches re-measured at `origin/feat/card-task-{543d8c5b,935ca9be,0c839994,27123eab,d8bdfd3a,07ab1e49,420b5c90}` (tips `f33c79e95 a694ad987 45f1aaee8 88e9fc746 5cb4c4abd 13e9c1c8b e64c5cd80`) against every Wave 0 file and every witness file named here | Zero overlap today; re-check at dispatch (plan "Check at every dispatch") |

### Delivery inventory

Phase 0 adds no dispatch, settlement, report, land or session-input producer and no new async
path. Every move is byte-identical (D-1) and every port is extract-interface over used members
(D-6), so the recipients of existing paths are unchanged. One existing delivery path changes its
exception handling and is inventoried; the rest of Phase 0 stops at compile, parity and the
layering guard by design.

| Path | Producer -> destination | Persistence / recovery | Observable receipt and witness |
|---|---|---|---|
| Channel outbound publication (P0-H2) | `ChannelOutboundDeliveryPump` publication attempt -> `IAntiphonMessagingProducer.SendAsync` -> `KafkaAntiphonMessagingProducer.ProduceAsync` -> outbound topic -> bridge -> channel | `ChannelOutboundDelivery` row: `Publishing` state, `PublicationAttempts`, attempt budget base, due time; a definite queue refusal returns the row to `Ready` with a persisted due time until `PublicationAttemptLimit`, then `Failed`; a size refusal is `Failed` at once; restart re-reads the row (CARD-0519) | Unchanged by H2 except the exception *type* at `:497-498,583,591`: the pump now catches `MessagingQueueFullException` / `MessagingMessageTooLargeException` from `Antiphon.Messaging` instead of `ProduceException` codes. Witness: `ChannelOutboundRetryPolicyTests` (real pump, real DB, fake producer) proves the classification and the persisted retry/terminal states; `C1729MessagingExceptionTests` proves the broker-code-to-type mapping in Messaging.Client on constructed `ProduceException` instances |

Substitutes, declared: the fake producer in `ChannelOutboundRetryPolicyTests` proves what the pump
does with each typed exception, not that a real broker raises `Local_QueueFull` or `MsgSizeTooLarge`
for a given reply; the mapping witness proves the code-to-type translation on constructed
exceptions, not a live Redpanda produce. A real-broker queue-full is not reproducible on demand
and is out of scope; the two halves together cover the only behaviour H2 changes. Receipt by the
channel recipient is unchanged and not re-proven here. P0-D moves `LandingVerifier` and
`WorktreeHealthHostedService` without changing a call; P0-I keeps hosted-service registration order
(D-7), which `C1729ProgramCompositionTests` pins; P0-T3 moves four assembly hooks, which
`C1729AssemblyHooksTests` pins. None of these stops at queue evidence because none produces to a
queue.

### The layering guard (P0-G, P0-G2)

**Files.** `tests/Antiphon.Tests/Infrastructure/C1729LayeringGuardTests.cs` (`[Category("Unit")]`,
namespace `Antiphon.Tests.Infrastructure`), `tests/Antiphon.Tests/Layering/LayeringGuardEngine.cs`
(internal, no tests; namespace `Antiphon.Tests.Layering`), `tests/Antiphon.Tests/Layering/layers.txt`,
`tests/Antiphon.Tests/Layering/allowlist.txt`, one paragraph in `docs/project-context.md`. Both `.txt`
files are read through the repository root found by walking up from `AppContext.BaseDirectory` to
`Antiphon.sln`, exactly as `TestLaneCategoryGuardTests.RepoRoot` does; no csproj `Content` item, so no
digest change. The checkpoint tool's isolated output lives inside the repository tree, so the walk
works there as it does for the existing guard rows.

**`layers.txt` grammar.** UTF-8, LF, `#` comments, two sections. `[edges]` lines are
`From -> To[, To]` and the seven layer names are `Host, Core, Persistence, Git, Runners, Integrations,
Application`; the committed edges are exactly the plan's: `Host -> Core, Persistence, Git, Runners,
Integrations, Application`; `Application -> Core, Persistence`; `Git -> Core`; `Runners -> Core`;
`Integrations -> Core`; `Persistence -> Core`; `Core ->` (nothing). `[rules]` lines are
`<repo-relative glob> <Layer>`, ordered, first match wins, `**` and `*` only, braces expanded by the
engine (`{a,b}`). The rule rows are the plan's layering map table, with one correction: the
Persistence rule reads `server/Infrastructure/Data/**, server/Migrations/**,
server/Infrastructure/Resilience/{DatabaseResilienceExecutor,NpgsqlTransientFailureClassifier}.cs`,
because `DatabaseResilienceExecutor.cs:76-77` calls `NpgsqlTransientFailureClassifier` and a
Persistence-to-Application edge there would be a false row. `server/Migrations/**` is mapped and
never parsed. A file matching no rule is `unmapped-file`; a rule matching no file is `dead-rule`;
both fail in either mode.

**`allowlist.txt` grammar.** Line 1 is `strict: false` or `strict: true`. Rows are
`<fromFile> -> <TypeName> @ <toFile>` with repository-relative `/` paths, grouped under
`# slice <id>` headers and separated by blank lines so that two slices deleting their own rows
rebase without a textual conflict (D-3). Duplicate rows, rows without a group header, unknown paths
and malformed lines are parse errors and fail in either mode. A row suppresses exactly the violation
with the same `(fromFile, TypeName, toFile)` triple, never the whole file.

**Resolver (D-2).** `LayeringGuardEngine.Analyze(map, allowlist, files)` where `files` is the list of
`(path, text)` to parse; the repository method passes every `server/**/*.cs` outside `server/Migrations/`
and `bin/`, `obj/`, `bin-*/`; the synthetic methods pass in-memory texts. Steps:

1. Parse each text with `CSharpSyntaxTree.ParseText` (syntax only, no references). Index every
   type declaration (`class`, `record`, `struct`, `interface`, `enum`, `delegate`, including nested
   and `partial`) by simple name to `(file, layer, namespace)`; a `partial` type declared in two
   files of different layers is `split-type` and fails.
2. For each file collect reference candidates: every `IdentifierNameSyntax` and `GenericNameSyntax`
   identifier token in the tree, excluding trivia, string and character literals, `using` directives
   themselves, the file's own declared type names, and identifiers that are the declared names of
   the file's locals, parameters, members and labels. Attribute names try `X` then `XAttribute`.
   `nameof(X)` counts.
3. Resolve a candidate whose simple name is declared in exactly one indexed file to that file. For a
   name declared in several files prefer a declaration whose namespace equals the referencing
   file's namespace or is named by one of its `using` directives (plus `global using` in `server/`);
   if exactly one remains it resolves; otherwise the candidate is `ambiguous` with its candidate
   list. An ambiguous reference is a violation only when every candidate's layer is a disallowed
   edge from the referencing layer (fail-closed where certain); otherwise it is listed and counted.
4. A resolved reference from layer A to layer B with `A -> B` absent from `[edges]` and A != B is a
   violation `(fromFile, TypeName, toFile)`. Violations present in the allowlist are `listed`; the
   rest are `unlisted`; allowlist rows matching no violation are `stale`.
5. Result: `Mode`, `MapErrors`, `ParseErrors`, `Unlisted`, `Listed`, `Stale`, `Ambiguous`, plus a
   printable inventory `VIOLATIONS <unlisted> LISTED <listed> STALE <stale> AMBIGUOUS <n>` followed by
   one line per unlisted violation (already in row grammar, so it can be pasted) and one per stale
   row and ambiguity.

Verdict: `strict: false` fails only on `MapErrors` or `ParseErrors` and prints the inventory;
`strict: true` additionally fails when `Unlisted` or `Stale` is non-empty. Simple-name resolution is
a known approximation (the spec's regex graph is ±10%; this is better but not a compilation): a
local or property that shares a type's PascalCase name can produce a false row, which the allowlist
absorbs and the printout exposes. Expected runtime: syntax-parsing about 1,350 files, 5-15 s
(provisional, n=0).

**Seeding procedure (Code for P0-G).** Commit the engine, the nine methods below, `layers.txt` and an
`allowlist.txt` holding only `strict: false`. Run CP-1; `Repository_graph_satisfies_the_committed_allowlist`
is green (warn mode) and prints the inventory. Paste the printed unlisted rows into `allowlist.txt`
under `# slice <id>` headers chosen by the **row ownership table** below; rerun CP-1 (counted as a
CP-1 rerun) and require `VIOLATIONS 0 STALE 0`; record `LISTED n AMBIGUOUS a` in the report. The plan's
expectation is 130-200 rows; the count is the run's output, not a claim. Ambiguities are reviewed
and reported, never turned into rows.

| `# slice` header | Rows whose `fromFile` is | Removed by |
|---|---|---|
| `P0-A1` | `Application/{Dtos,Exceptions,Interfaces,Settings}/**` referencing one of the 14 A1 types in `S/` | P0-A1 (the type moves to Core) |
| `P0-B` | `Core/**` (after A1) or `Settings/DelegationSettings.cs` referencing `S/{DelegationOpenGate,AgentTaskService,SubscriptionUsageReader,PipelineHandoff,DeliverableBundleService,ChannelOutboundService,ChatChannelService}.cs` | P0-B |
| `P0-C1` .. `P0-C5` | Application files referencing `GitWorkspaceService`, `CardFileBoardLookup`, `PhoneHomeRunnerDirectory`/`PhoneHomeRunnerClient`, `OrchestratorWorkspaceLayout`/`AuditService`, `ServerGitProcess`/`GitIndexLock`/`LandWorkspace`/`LandVerificationArtifacts` respectively; Integrations/Host files referencing the C2/C4 concretes | the named port slice |
| `P0-D` | `Infrastructure/Git/{LandingVerifier,WorktreeHealthHostedService,LocalTaskParkPublisher}.cs` referencing Application | P0-D |
| `P0-E` | `Infrastructure/Agents/SessionRunner/{the eight drivers}.cs`, `Tui/AgentTuiMetrics.cs` referencing Application | P0-E (the row moves: after E the same files are Application and their Runners references become `P0-C3` rows or `later-phase` rows) |
| `P0-F1` | `Infrastructure/Data/CardDoneArtifactRoots.cs` referencing Git; `Infrastructure/GitHub/GitHubMonitorService.cs` referencing Application; `Infrastructure/Agents/{CodexSubmitConfirmation,Tui/DataProtectionAgentTuiSecretProtector}.cs` | P0-F1 |
| `P0-H2` | `S/ChannelOutboundDeliveryPump.cs` has no `server/` violation (Confluent is a package, not a layer); no row | none |
| `later-phase` | `Infrastructure/Agents/ExpectationObservationAdapter.cs`, Supervision/Orchestration hosted services reaching Git or Runners, `Infrastructure/Agents/*HostedService.cs` reaching Runners, Application services reaching `Infrastructure.Agents.SessionRunner` (16 files) or `Infrastructure.Git` (5) beyond the C-slices' concretes | Phases 2-8; the plan's risk 14 |

The 277 Application files importing `Infrastructure.Data` are Application-to-Persistence, a legal
edge until Phase 9, and produce no rows.

**Frozen methods (9).** Every method has one `[Test]`, no `[Arguments]`; variants are in-body with
fixed assertion labels.

| Case | Exact method | Recipe / variants | Oracle; red at base; classification |
|---|---|---|---|
| L1 | `Map_covers_every_server_file_exactly_once` | Real `layers.txt`, real tree census (`server/**/*.cs` minus `Migrations`, `bin`, `obj`, `bin-*`); assert zero `unmapped-file`, zero `dead-rule`, and the mapped count equals the census count | `unmapped-file=0`, `dead-rule=0`, `mapped=census`. Base has no map, so the method does not exist at base; after Code it is red against a map missing any folder (PC-1). Unit, portable |
| L2 | `Layer_of_pinned_files_follows_first_match` | Real map; a frozen table of 12 path -> layer pins: `server/Program.cs` Host; `server/Infrastructure/Agents/ZombieCensusJob.cs` Host; `server/Infrastructure/Agents/HangfireConfiguration.cs` Host; `server/Infrastructure/Agents/AgentExecutor.cs` Integrations; `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs` Runners; `server/Infrastructure/Resilience/DatabaseResilienceExecutor.cs` Persistence; `server/Infrastructure/Resilience/NpgsqlTransientFailureClassifier.cs` Persistence; `server/Infrastructure/Resilience/ResilienceReadClients.cs` Application; `server/Application/Services/GitWorkspaceService.cs` Git; `server/Application/Services/AgentTaskService.cs` Application; `server/Application/Interfaces/ILandingGit.cs` Core; `server/Domain/Entities/AgentTask.cs` Core | `layer:<path>=<Layer>` per pin. Red when rule order or a glob regresses (PC-2). Unit, portable |
| L3 | `Allowlist_parses_with_grouped_rows_and_a_mode_header` | Real `allowlist.txt`: header present and boolean; every row under a `# slice` header; paths exist in the tree; no duplicate triple; synthetic negative variants (missing header, bare row, duplicate, unknown path) each produce one named parse error | `header-ok`, `rows-grouped`, `no-duplicates`, `paths-exist`; `parse-error:<kind>` per negative. Unit, portable |
| L4 | `Repository_graph_satisfies_the_committed_allowlist` | Real map, allowlist and tree; prints the inventory in both modes; in `strict: false` asserts only `map-errors=0`, `parse-errors=0`; in `strict: true` asserts also `unlisted=0`, `stale=0`, each failure naming the row | Warn: `map-errors=0 parse-errors=0` plus printed `VIOLATIONS n LISTED m STALE s AMBIGUOUS a`; strict: `violation:<row>` and `stale-row:<row>` empty. With an empty allowlist in strict mode this is red at base-equivalent content (the seeding run's first pass with `strict: true` would list every violation); the committed warn-first file keeps it green until P0-G2. Unit, portable; runtime provisional 5-15 s |
| L5 | `Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode` | In-memory map (`core/** Core`, `app/** Application`, plan edges) and texts: `app/Svc.cs` declares `Svc` and `Other`; `core/Value.cs` declares `Value`, references `Svc` in a method body and `Other` in a field type, has no `using`; allowlist `strict: true` with one row for `Svc` only | `violation-count=1` (only `Other` is unlisted), `violation:core/Value.cs -> Other @ app/Svc.cs`, `listed=1`, `verdict=fail`. Red when body identifiers are skipped (PC-3) or a row matches by file (PC-5). Unit, portable |
| L6 | `Allowed_edges_listed_rows_comments_and_strings_do_not_fire` | Synthetic: Host file referencing Git, Runners, Application; Application referencing Core and Persistence; Git referencing Core; a Core file whose only mention of an Application type is inside `// comment`, `/* block */`, `"string"` and `nameof`-free `#region`; a Core file referencing its own nested type; an Application file referencing a listed violation; `strict: true` | `findings=0:allowed-edge`, `findings=0:comment`, `findings=0:string`, `findings=0:own-declaration`, `listed=1 unlisted=0`, `verdict=pass`. Red when the edge table is ignored (PC-4) or trivia is scanned (PC-9). Unit, portable |
| L7 | `Stale_row_and_unmapped_file_fail_in_strict_mode` | Synthetic `strict: true`: (a) allowlist row naming a triple that no violation matches; (b) a text whose path matches no rule; (c) `strict: false` with the same inputs | (a) `stale-row:<row>` and `verdict=fail`; (b) `unmapped-file:<path>` and `verdict=fail` in both modes; (c) stale is printed but `verdict=pass`. Red when stale detection is dropped (PC-6) or unmapped files default to a layer (PC-7). Unit, portable |
| L8 | `Ambiguous_simple_names_resolve_by_using_or_are_listed` | Synthetic: `Reader` declared in `core/A.cs` (namespace `X.Core`) and `app/B.cs` (`X.App`); `core/C.cs` has `using X.App;` and references `Reader`; variant 2: `core/D.cs` has no `using`, references `Reader` -> fully ambiguous with one allowed and one disallowed candidate; variant 3: both candidates disallowed | v1 `violation-count=1` (resolves to App); v2 `ambiguous=1 unlisted=0` (listed, not a violation); v3 `ambiguous=1 unlisted=1` (fail-closed). Red when the first declaration wins (PC-8). Unit, portable |
| L9 | `Warn_mode_prints_the_inventory_and_fails_only_on_map_or_parse_errors` | Synthetic `strict: false`: two unlisted violations and one stale row -> pass with inventory lines; a malformed allowlist row -> fail; a `dead-rule` -> fail | `warn-verdict=pass`, inventory contains both rows and the stale row, `parse-error-fails-in-warn-mode`, `dead-rule-fails-in-warn-mode`. Red when warn mode swallows parse errors (PC-10). Unit, portable |

P0-G2 is the one-line flip of the header to `strict: true`; its verification is CP-1 on a fresh
build (reported as a CP-1 rerun with `reruns=`), which exercises L4's strict arm on the real tree.

### Test-count parity (D-9)

Parity is measured only where tests change. Two shapes, both reported with one `PARITY` line:

1. **Production-only slice** (A1, A2, C1, C2, C3, C4, E, F1, F2 as recommended below): prove
   `git diff --stat <start>..<tip> -- tests/` is empty and report
   `PARITY tests-untouched base=<start sha> tip=<tip sha>`. No run.
2. **Test-touching slice** (G, G2, B, C5, D, H2, I, T1a, T1b, T2, T3, T5): run discovery twice on
   the worktree's native default output, once at the start commit before any edit and once at the
   tip, and compare the node sets. `--list-tests` ignores `--treenode-filter` on this runner
   (CARD-0403) and is discovery only, so one run is complete and costs discovery 1-5 s plus host
   startup. Each `dotnet` invocation takes its own build-slot lease:

```powershell
# once at <start> before editing, once at <tip>; $which is base or tip
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1729-parity-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false
$dir = ".antiphon/c1729-parity/<slice>/$which-$(git rev-parse HEAD)"
New-Item -ItemType Directory -Force $dir | Out-Null
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1729-parity-list -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --list-tests --no-progress --no-ansi --diagnostic --diagnostic-output-directory $dir --results-directory $dir *> "$dir/console.log"
. scripts/lib/nightly-coverage.ps1   # ConvertFrom-NightlyDiagnosticLog :452, ConvertTo-NightlyDiscoveryDocument :536, Get-NightlyLatestDiagnosticLog :585, Get-NightlyAssemblyHash :597
$doc = ConvertTo-NightlyDiscoveryDocument (ConvertFrom-NightlyDiagnosticLog (Get-NightlyLatestDiagnosticLog $dir) discovery).Nodes (Get-NightlyAssemblyHash tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll)
$doc | ConvertTo-Json -Depth 6 | Set-Content "$dir/discovery.json"
$doc.nodes | ForEach-Object { $_.uid } | Sort-Object -Unique | Set-Content "$dir/uids.txt"
```

   Then `diff <base dir>/uids.txt <tip dir>/uids.txt` and report
   `PARITY base=<sha> tip=<sha> base=N tip=N added=k removed=k` followed by every added and removed
   uid verbatim. The acceptance per slice: `removed=0` always; `added` is exactly the frozen new
   methods of that slice (G: the nine L-methods; B: four `C1729CoreBoundaryTests`; C5: four
   `C1729LandGitPortTests`; D: one `C1729ParkFingerprintTests`; H2: zero in `Antiphon.Tests` and three
   in `Antiphon.Messaging.Tests`, measured with the same recipe on `tests/Antiphon.Messaging.Tests`;
   I: one `C1729ProgramCompositionTests`; T1a, T1b, T2, T5: zero; T3: two `C1729AssemblyHooksTests`).
   A nested class with `[Test]` methods that moved shows up as one removed and one added uid and
   fails the slice. The discovery document is the same shape as `tests/fixtures/live-lanes/
   full-discovery-before.json` (`uid`, `namespace`, `type`, `method`, `arguments`), so a reviewer can
   read it; the raw `console.log` is retained beside it. Baselines live only under the ignored
   `.antiphon/c1729-parity/<slice>/`; nothing is committed (D-9 rejects a 21.9k-line roster) and the
   report carries the counts and the added/removed identities. Both discovery invocations are
   **declared unlisted runs** with this reason. The `--list-tests` output is not execution evidence
   and never replaces a checkpoint row.

### Proves it works now

The following are **future Code tests**, not executions claimed by TestDesign. New classes and
their exact methods are frozen here; each new method has one `[Test]`, no `[Arguments]`, in-body
labelled variants, and lives in the named namespace. `TestLaneCategoryGuardTests` requires every
new class to carry exactly one of `[Category("Unit")]` / `[Category("Integration")]`; none is Slow.
Existing classes are selected as they are.

| Case | Exact method | Recipe / variants / production reachability | Oracle; red at base; classification |
|---|---|---|---|
| L1..L9 | `C1729LayeringGuardTests.*` (above) | as designed above | as designed above |
| B1 | `C1729CoreBoundaryTests.Advisory_lock_key_is_one_shared_constant` (`A/`, `Antiphon.Tests.Application`, Unit) | Read `DispatchConcurrencyPolicy.CreateAdvisoryKey`, `DelegationOpenGate.AdvisoryLockKey` and the new Core constant; also scan `S/AgentTaskService.cs` text for `pg_advisory_xact_lock(hashtext('{AdvisoryLockKey}'))` using the symbol, not a literal | `advisory-key-identity`: all three equal `antiphon.delegation.max-open-tasks`; `sql-uses-constant`. Equivalence witness: green when written against the extracted constant; red under PC-13. Unit, portable |
| B2 | `C1729CoreBoundaryTests.Delegatable_kinds_are_one_sequence_for_validation_and_launch` | `AgentTaskService.DelegatableKinds` is `ReferenceEquals` or sequence-equal to the Core constant; `DelegationSettings` validation of an invalid `RolePolicy:Code:Kind` names the kinds in the constant's order | `delegatable-kinds-identity`, `validation-text-order` (`Use ClaudeCode, Grok or Codex`). Red under PC-14. Unit, portable |
| B3 | `C1729CoreBoundaryTests.Channel_reply_inputs_port_matches_markdown_sources_like_the_service` | Construct the production `IChannelReplyInputs` implementation(s) and call the port's markdown-match for the four inputs `ChannelOutboundPolicyTests.cs:576-579` uses; compare with `ChannelOutboundService.MatchesMarkdownSources` on the same inputs | `markdown-match-identity:<input>` four labels. Red under PC-16. Unit, portable |
| B4 | `C1729CoreBoundaryTests.Channel_reply_inputs_port_refuses_prompt_paths_outside_the_workspace` | Port `TryGetPromptPath` with a workspace and a `../outside.md` prompt file -> false; an inside file -> true and the same path `ChatChannelService.TryGetPromptPath` returns | `prompt-path-outside-refused`, `prompt-path-inside-identity`. Red under PC-17. Unit, portable |
| C5-1 | `C1729LandGitPortTests.Port_path_equality_and_staleness_match_GitIndexLock` (`A/`, Unit) | For the `ILandGit` members that replace `GitIndexLock.PathsEqual` and `StaleAfter`: equal paths with different separators and trailing slash, different paths, null; `StaleAfter(null)`, `StaleAfter(0)`, `StaleAfter(45)` compared to `GitIndexLock` | `paths-equal-identity:<case>`, `stale-after-identity:<case>`. Red under PC-18/PC-19. Unit, portable |
| C5-2 | `C1729LandGitPortTests.Port_lock_reason_and_codes_are_the_land_constants` | Port constants equal `LandWorkspace.LockReason` (`antiphon-land`), `GitIndexLock.StaleCode` (`git_index_lock_stale`), `HeldCode` (`git_index_lock_held`), `PathErrorReason` (`index_lock_path_error`) | `lock-reason`, `stale-code`, `held-code`, `path-error-reason`. Red under PC-20. Unit, portable |
| C5-3 | `C1729LandGitPortTests.Results_directory_with_a_link_is_refused` | Temp results directory containing one symbolic link (file or directory) -> the port's `RequireNoLinks` refuses with the existing refusal; a plain directory passes; Linux creates the link with `File.CreateSymbolicLink`, Windows variant skipped only when link creation is denied and the skip is reported | `linked-results-refused`, `plain-results-accepted`. No test covers `RequireNoLinks` at base (grep: zero references under `tests/`), so this is new coverage; red under PC-21. Unit, portable |
| C5-4 | `C1729LandGitPortTests.Port_disables_git_auto_maintenance` | `ProcessStartInfo` through the port's `DisableAutoMaintenance` carries the same environment keys/values `ServerGitProcess.DisableAutoMaintenance` sets | `auto-maintenance-env-identity`. Red under PC-22. Unit, portable |
| D1 | `C1729ParkFingerprintTests.Digest_matches_the_pinned_vector` (`A/`, Unit) | `ParkFingerprint.Digest("antiphon")` and `Digest("")` equal the upper-case hex SHA-256 vectors Code captures from `BlockedTaskParkingService.Digest` at its start commit (binding: paste the two hex strings); `Digest(null)` is null | `park-digest-vector`, `park-digest-empty`, `park-digest-null`. Protects parks persisted before P0-D (reader at `LocalTaskParkPublisher.cs:161,174`). Red under PC-23. Unit, portable |
| H2-1 | `C1729MessagingExceptionTests.Queue_full_maps_to_the_typed_exception` (`tests/Antiphon.Messaging.Tests/Client/`, namespace `Antiphon.Messaging.Tests.Client`, Unit) | Construct `ProduceException<string,string>(new Error(ErrorCode.Local_QueueFull, "m"), new())`; call the internal mapper `KafkaAntiphonMessagingProducer.Map(...)` (internal; visibility through a new `src/Antiphon.Messaging.Client/InternalsVisibleTo.cs` assembly attribute, which is a `.cs` file and digest-safe) | `queue-full-type`, `queue-full-message-retained`, inner exception is the original. Red under PC-24. Unit, portable |
| H2-2 | `C1729MessagingExceptionTests.Message_too_large_maps_to_the_typed_exception` | `ErrorCode.MsgSizeTooLarge` -> `MessagingMessageTooLargeException` | `too-large-type`, inner retained. Red under PC-25 |
| H2-3 | `C1729MessagingExceptionTests.Other_produce_errors_pass_through_unchanged` | `ErrorCode.Local_Transport`, `ErrorCode.Unknown` -> the same `ProduceException` instance is returned/rethrown, not wrapped | `other-code-passthrough:<code>`. Red under PC-26 |
| I1 | `C1729ProgramCompositionTests.Hosted_services_keep_their_registration_order` (`I/`, `Antiphon.Tests.Infrastructure`, Integration, `[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]`) | `_factory.Services.GetServices<IHostedService>()` filtered to types in `Antiphon.Server.*` namespaces, projected to full type names, compared as an ordered list to a frozen constant Code captures by running the method once at its start commit inside the same factory (so factory overrides at `AntiphonWebAppFactory.cs:140-150` cancel out); the frozen list is pasted into the test and its count printed | `hosted-order` (sequence equality; the message names the first differing index and the missing or extra type), `hosted-count`. Red under PC-30/PC-31. Integration, portable; one shared boot |
| T3-1 | `C1729AssemblyHooksTests.Hooks_are_declared_once_in_the_frozen_order` (`H/`, `Antiphon.Tests.TestHelpers`, Unit) | Reflect `AssemblyHooks`: methods carrying TUnit `Before(HookType.Assembly)` ordered by `Order` equal the frozen list `[TestDbFixture.InitializeAsync (worker exit, warm-up), ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner, PtyBackendEnvGuard.ClearInheritedPtyBackend, CheckpointTempSweepAssemblyHook.Sweep, CheckpointTempSweepAssemblyHook.RecordSelectedRoster]`, each hook method's body target resolved by the delegate it calls; `After(Assembly)` holds `TestDbFixture.DisposeAsync` | `hook-roster`, `hook-order`, `after-hook`. Red under PC-33. Unit, portable |
| T3-2 | `C1729AssemblyHooksTests.Original_sites_carry_no_assembly_hook_attributes` | Reflect the four original methods: none carries `Before(Assembly)`/`After(Assembly)` any more | `single-declaration:<method>`. Red under PC-35 |

- V-1: Map, allowlist and resolver behave as D-2/D-3 specify; the real tree is covered and inventoried | Unit | L1..L9, CP-1 | labels above; the real-tree inventory printed and recorded.
- V-2: Strict ratchet refuses a new wrong-way reference and a stale row on the real tree | Unit | L4 after P0-G2, CP-1 rerun | `violation:<row>`/`stale-row:<row>` empty.
- V-3: Byte-identical moves compile, keep namespaces and keep every existing test (A1, A2, E, F1, C4, T1a, T2) | build + existing classes | CP-2..CP-5, CP-16/17, CP-22..CP-24, CP-30, CP-32 and the rename proof `git diff -M100% --stat` | all listed, 0 failed/skipped; `PARITY tests-untouched` or `added=0 removed=0`.
- V-4: Core-to-Application fixes preserve the advisory key, the delegatable-kinds text and the channel reply preparation | Unit + Integration | B1..B4, `RolePolicyConfigurationValidationTests`, `ChannelOutboundPolicyTests`, `PtyDeliveryCeilingsTests`, CP-6..CP-8 | labels above; zero `P0-B` rows left in the allowlist (CP-7 strict).
- V-5: `ILandGit` port preserves lock semantics, constants, link refusal and git maintenance env, and the land path still verifies | Unit + Slow Integration | C5-1..C5-4, `GitIndexLockTests`, `LandWorkspaceTests`, `ServerGitProcessTests`, `AgentTaskLandVerifierTests`, `LandVerificationBuildSlotTests`, CP-18/19 | labels above; 0 failed/skipped.
- V-6: Back-reference inversions keep the park fingerprint and the verifier/health drivers | Unit + Slow Integration | D1, `TaskParkPublicationTests`, `WorktreeHealthServiceTests`, `AgentTaskLandVerifierTests`, CP-20/21 | `park-digest-vector`; 0 failed/skipped.
- V-7: Resilience read-client selection unchanged after F2 | Unit | `ResilienceCircuitTests` (6), CP-25 | 0 failed/skipped; guard row green in the same filter.
- V-8: Typed messaging exceptions map from broker codes and the pump classifies them as before | Unit + Integration | H2-1..H2-3, `ChannelOutboundRetryPolicyTests` (22), CP-26/27 | labels above; `C519_*` retry/terminal states.
- V-9: Program composition is a pure relocation: every hosted service, same order | Integration | I1, `HangfireStartupSafetyTests`, `WorktreeResidueRegistrationTests`, `RunnerAlarmWiringTests`, CP-28/29 | `hosted-order`, `hosted-count`.
- V-10: Nested-helper extraction keeps every consumer compiling and behaving and moves no test | build + consumers + parity | CP-30 (T1a), CP-31 (T1b) | 0 failed/skipped; `added=0 removed=0`.
- V-11: Per-assembly guards and explicit assembly hooks | Unit + Slow Integration | `TestLaneCategoryGuardTests` with the new `lane-guard-root-exists`/`root-has-files` assertions (T2), T3-1, T3-2, `TestDbFixtureLazyInitializationTests (A_*)|(Mixed_*)`, `HangfireStartupSafetyTests.Assembly_guard_and_factory_override_disable_the_Hangfire_worker`, `CheckpointTempRootSweepTests`, CP-32..CP-35 | labels above.
- V-12: One `MigrationsAssembly` helper leaves the DB lifecycle unchanged (T5, if approved) | Slow Integration | `TestDbFixtureLazyInitializationTests (A_*)|(Mixed_*)`, CP-36 | 0 failed/skipped.

### Guards the regression

- R-1: The guard never passes a wrong-way reference by construction error (allowed-edge table, trivia, own declarations) | L6, CP-1 | `findings=0:*` plus L5 `violation-count=1`.
- R-2: Delegation validation text keeps kind order | existing Unit `RolePolicyConfigurationValidationTests.An_unlaunchable_kind_or_undefined_level_fails_validation_naming_role_and_value`, CP-6 | exact message `Use ClaudeCode, Grok or Codex, or leave it unset.`
- R-3: Markdown-source matching and prompt-path authorization unchanged | existing Integration `ChannelOutboundPolicyTests` (`:576-579`), CP-8 | the four `MatchesMarkdownSources` outcomes.
- R-4: Definite queue refusal retries to the cap, size refusal is terminal, cap survives restart | existing `ChannelOutboundRetryPolicyTests.C519_Only_definite_queue_refusal_is_automatic`, `C519_Size_refusal_is_terminal`, `C519_Publication_cap_survives_restart`, `C519_Retry_waits_until_persisted_due_time`, CP-26 | persisted `Ready`/`Failed` states and due times.
- R-5: Production runner guard fires before any Program boot | existing `HangfireStartupSafetyTests.Assembly_guard_and_factory_override_disable_the_Hangfire_worker`, CP-34 | dead runner URL observed; Hangfire worker not started.
- R-6: Worker-mode child exits before the shared store warms up | existing `TestDbFixtureLazyInitializationTests.A_worker_child_exits_before_the_shared_store_warmup`, CP-33/CP-36 | recorded `dbLifecycle` shows no warm-up ahead of the worker.
- R-7: Land verifier and build-slot paths unchanged by C5 and D | existing `AgentTaskLandVerifierTests` (Slow, 105 s), `LandVerificationBuildSlotTests`, CP-18/CP-20 | 0 failed/skipped.
- R-8: Hand-built delegation graph still resolves after each port | existing `DelegationTestServicesTests` (5), CP-10 | 0 failed/skipped.

### Guard inventory

Every safety-critical assertion selected for Phase 0, each mapped 1:1 to its same-numbered PC.
Mechanical slices (A1, A2, E, F1, C1, C2, C3, C4, T1a, T1b, T5) add no guard: a byte-identical move
or an extract-interface over used members has no production decision to protect; its failure modes
are compile errors and DI resolution failures, which the slice's build, boot and consumer rows
catch, and a moved test, which parity catches. The PublicAPI analyzers (RS0016/RS0017) are a build
guard: breaking them is exit 2 (`prerequisite-missing`), never a test red, so they are Review's
diff check, not a PC. Hook order among the four T3 hooks has no observable effect beyond worker
exit, which needs only that no other hook touches the store (none does), so order is pinned
textually by G-33 and has no behavioural PC.

| Guard | Plan reference + safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-2: every non-migration server file matches exactly one rule | PC-1 |
| G-2 | D-2: first match wins and the committed order is the plan's | PC-2 |
| G-3 | D-2: body references are collected, not only `using` directives | PC-3 |
| G-4 | D-2: allowed edges never fire | PC-4 |
| G-5 | D-3: a row suppresses only its own `(from, type, to)` triple | PC-5 |
| G-6 | D-3: a stale row fails in strict mode | PC-6 |
| G-7 | D-2: an unmapped file fails in either mode | PC-7 |
| G-8 | D-2: ambiguity resolves by `using`/namespace or is listed, never by first declaration | PC-8 |
| G-9 | D-2: trivia, strings and own declarations are not references | PC-9 |
| G-10 | D-3: warn mode still fails on parse or map errors | PC-10 |
| G-11 | D-3 (post-G2): an unlisted violation on the real tree fails | PC-11 |
| G-12 | D-3 (post-G2): a stale row on the real tree fails | PC-12 |
| G-13 | P0-B: one advisory lock key for gate, policy and SQL | PC-13 |
| G-14 | P0-B: one delegatable-kinds sequence for validation and launch | PC-14 |
| G-15 | P0-B: validation text keeps the kinds' order | PC-15 |
| G-16 | P0-B: port markdown match equals the service | PC-16 |
| G-17 | P0-B: port refuses a prompt path outside the workspace | PC-17 |
| G-18 | P0-C5: index-lock path equality through the port | PC-18 |
| G-19 | P0-C5: staleness from configured seconds through the port | PC-19 |
| G-20 | P0-C5: land lock reason and lock codes are the constants | PC-20 |
| G-21 | P0-C5: a results directory containing a link is refused | PC-21 |
| G-22 | P0-C5: git auto-maintenance is disabled for owned processes | PC-22 |
| G-23 | D-8/P0-D: park fingerprint is byte-identical to the old `Digest` | PC-23 |
| G-24 | P0-H2: `Local_QueueFull` maps to the queue-full type | PC-24 |
| G-25 | P0-H2: `MsgSizeTooLarge` maps to the too-large type | PC-25 |
| G-26 | P0-H2: other produce errors are not wrapped or swallowed | PC-26 |
| G-27 | P0-H2/R-4: queue-full retries until the publication cap | PC-27 |
| G-28 | P0-H2/R-4: too-large is terminal | PC-28 |
| G-29 | P0-H2/R-4: the cap survives restart with the typed exception | PC-29 |
| G-30 | D-7: hosted services keep registration order | PC-30 |
| G-31 | D-7: no hosted-service registration is dropped | PC-31 |
| G-32 | P0-T2: lane-guard root exists and holds files (fail-closed) | PC-32 |
| G-33 | P0-T3: all four hooks declared once, in the frozen order | PC-33 |
| G-34 | P0-T3/R-5: production runner guard effect precedes boot | PC-34 |
| G-35 | P0-T3: no original site keeps its hook attribute (no double run) | PC-35 |

### Positive controls

Every PC: break its G-n with the listed **compiling defect**, run the exact method with a
method-scoped filter `/*/*/<Class>*/<Method>`, require the listed assertion red, restore only that
mutant, rebuild and run the same method green. Classify `killed-at-intended-assertion`, `survived`,
`invalid-control` or `infrastructure/incomplete`; zero tests, build failure, unrelated red and
timeout never count. New symbols and shifted anchors **remain binding required after Code**; bind
to landed L before Mutation and preserve the one-line patch, digest and before/after evidence. PC-1,
PC-2, PC-11 and PC-12 mutate committed text files the test reads at runtime, so their red and green
legs need **no rebuild**; PC-11 and PC-12 are executable only after P0-G2 and stay
`higher-tier-required: binding-missing` until then. Code runs ordinary V/R; Review judges binding
before land; post-land SourceLanding Mutation runs every cycle at L. No PC is executed by
TestDesign.

| PC | Break G-n at anchor | Compiling defect | Exact case method | Required red assertion / variant |
|---|---|---|---|---|
| PC-1 | G-1; `tests/Antiphon.Tests/Layering/layers.txt` | delete the `server/Infrastructure/WorkspaceHooks/**` glob from the Git rule | `C1729LayeringGuardTests.Map_covers_every_server_file_exactly_once` | `unmapped-file:server/Infrastructure/WorkspaceHooks/<file>` |
| PC-2 | G-2; `layers.txt` rule order | move the Integrations `server/Infrastructure/Agents/*.cs` rule above the Host `server/Infrastructure/Agents/*Job.cs` rule | `C1729LayeringGuardTests.Layer_of_pinned_files_follows_first_match` | `layer:server/Infrastructure/Agents/ZombieCensusJob.cs=Host` |
| PC-3 | G-3; `LayeringGuardEngine` reference collection (new) | collect identifiers only from `UsingDirectiveSyntax` | `C1729LayeringGuardTests.Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode` | `violation-count=1` (observed 0) |
| PC-4 | G-4; engine edge check (new) | treat every cross-layer reference as a violation, ignoring `[edges]` | `C1729LayeringGuardTests.Allowed_edges_listed_rows_comments_and_strings_do_not_fire` | `findings=0:allowed-edge` |
| PC-5 | G-5; engine row matching (new) | match a row by `fromFile` only | `C1729LayeringGuardTests.Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode` | `violation-count=1` (observed 0: `Other` suppressed by the `Svc` row) |
| PC-6 | G-6; engine stale computation (new) | return an empty stale set | `C1729LayeringGuardTests.Stale_row_and_unmapped_file_fail_in_strict_mode` | `stale-row:<row>` |
| PC-7 | G-7; engine rule lookup (new) | default an unmatched path to `Application` | `C1729LayeringGuardTests.Stale_row_and_unmapped_file_fail_in_strict_mode` | `unmapped-file:<path>` |
| PC-8 | G-8; engine resolver (new) | pick the first indexed declaration for a duplicated simple name | `C1729LayeringGuardTests.Ambiguous_simple_names_resolve_by_using_or_are_listed` | `violation-count=1` in variant 1 |
| PC-9 | G-9; engine token walk (new) | include comment trivia text in the identifier scan | `C1729LayeringGuardTests.Allowed_edges_listed_rows_comments_and_strings_do_not_fire` | `findings=0:comment` |
| PC-10 | G-10; engine verdict (new) | in warn mode return pass regardless of parse errors | `C1729LayeringGuardTests.Warn_mode_prints_the_inventory_and_fails_only_on_map_or_parse_errors` | `parse-error-fails-in-warn-mode` |
| PC-11 | G-11; `tests/Antiphon.Tests/Layering/allowlist.txt` (strict) | delete one `later-phase` row | `C1729LayeringGuardTests.Repository_graph_satisfies_the_committed_allowlist` | `violation:<row>` |
| PC-12 | G-12; `allowlist.txt` (strict) | add a row naming a triple with no violation | `C1729LayeringGuardTests.Repository_graph_satisfies_the_committed_allowlist` | `stale-row:<row>` |
| PC-13 | G-13; `S/DelegationOpenGate.cs:19` | replace the shared constant with a private literal `antiphon.delegation.max-open-tasks-x` | `C1729CoreBoundaryTests.Advisory_lock_key_is_one_shared_constant` | `advisory-key-identity` |
| PC-14 | G-14; `S/AgentTaskService.cs` `DelegatableKinds` | keep a private array without `Grok` instead of the Core constant | `C1729CoreBoundaryTests.Delegatable_kinds_are_one_sequence_for_validation_and_launch` | `delegatable-kinds-identity` |
| PC-15 | G-15; `Core/DelegationConstants.cs` (new) | reorder the kinds array `Codex, ClaudeCode, Grok` | `RolePolicyConfigurationValidationTests.An_unlaunchable_kind_or_undefined_level_fails_validation_naming_role_and_value` | the message equality at `:35` |
| PC-16 | G-16; port implementation in `S/ChannelOutboundService.cs` (new) | return `true` from the port's markdown match | `C1729CoreBoundaryTests.Channel_reply_inputs_port_matches_markdown_sources_like_the_service` | `markdown-match-identity:unrelated.zip` |
| PC-17 | G-17; port implementation in `S/ChatChannelService.cs` (new) | return the combined path without the workspace containment check | `C1729CoreBoundaryTests.Channel_reply_inputs_port_refuses_prompt_paths_outside_the_workspace` | `prompt-path-outside-refused` |
| PC-18 | G-18; `ILandGit` implementation (new, beside `S/GitIndexLock.cs:195`) | compare paths with `string.Equals` ordinal, no normalization | `C1729LandGitPortTests.Port_path_equality_and_staleness_match_GitIndexLock` | `paths-equal-identity:separator` |
| PC-19 | G-19; port implementation (new, `S/GitIndexLock.cs:31`) | return `TimeSpan.Zero` for every configured value | `C1729LandGitPortTests.Port_path_equality_and_staleness_match_GitIndexLock` | `stale-after-identity:45` |
| PC-20 | G-20; port constants (new, `server/Infrastructure/Git/LandWorkspace.cs:18`) | `LockReason` = `antiphon-land-x` | `C1729LandGitPortTests.Port_lock_reason_and_codes_are_the_land_constants` | `lock-reason` |
| PC-21 | G-21; port `RequireNoLinks` (new, `LandVerificationArtifacts.cs`) | return without enumerating reparse points | `C1729LandGitPortTests.Results_directory_with_a_link_is_refused` | `linked-results-refused` |
| PC-22 | G-22; port `DisableAutoMaintenance` (new, `S/ServerGitProcess.cs:16`) | no-op | `C1729LandGitPortTests.Port_disables_git_auto_maintenance` | `auto-maintenance-env-identity` |
| PC-23 | G-23; `Core/ParkFingerprint.cs` (new) | lower-case hex | `C1729ParkFingerprintTests.Digest_matches_the_pinned_vector` | `park-digest-vector` |
| PC-24 | G-24; `KafkaAntiphonMessagingProducer.Map` (new) | map `Local_QueueFull` to the too-large type | `C1729MessagingExceptionTests.Queue_full_maps_to_the_typed_exception` | `queue-full-type` |
| PC-25 | G-25; same mapper | map `MsgSizeTooLarge` to the queue-full type | `C1729MessagingExceptionTests.Message_too_large_maps_to_the_typed_exception` | `too-large-type` |
| PC-26 | G-26; same mapper | map every code to the queue-full type | `C1729MessagingExceptionTests.Other_produce_errors_pass_through_unchanged` | `other-code-passthrough:Local_Transport` |
| PC-27 | G-27; `S/ChannelOutboundDeliveryPump.cs:583` (typed catch) | finish the attempt `Failed` on the first queue-full | `ChannelOutboundRetryPolicyTests.C519_Only_definite_queue_refusal_is_automatic` | the `Ready`/due-time assertion (binding required) |
| PC-28 | G-28; `:591` (typed catch) | finish `Ready` with a retry due time on too-large | `ChannelOutboundRetryPolicyTests.C519_Size_refusal_is_terminal` | the `Failed` assertion (binding required) |
| PC-29 | G-29; `:583` | reset `PublicationAttemptBudgetBase` to the current attempts on queue-full | `ChannelOutboundRetryPolicyTests.C519_Publication_cap_survives_restart` | the cap assertion (binding required) |
| PC-30 | G-30; `server/Composition/AntiphonHost.cs` (new) | swap the `RunnerAlarmHostedService` and `HostStatsPollService` lines (`Program.cs:977-978` today) | `C1729ProgramCompositionTests.Hosted_services_keep_their_registration_order` | `hosted-order` |
| PC-31 | G-31; same file | delete the `SeatOccupancyHostedService` line (`Program.cs:982` today) | `C1729ProgramCompositionTests.Hosted_services_keep_their_registration_order` | `hosted-order` naming the missing type |
| PC-32 | G-32; `H/TestLaneCategoryGuardTests.cs:19` (T2 edit) | derive `tests/<AssemblyName>.Tests` | `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration` | `lane-guard-root-exists` |
| PC-33 | G-33; `tests/Antiphon.Tests/AssemblyHooks.cs` (new) | remove the `ProductionRunnerGuard` hook method | `C1729AssemblyHooksTests.Hooks_are_declared_once_in_the_frozen_order` | `hook-roster` |
| PC-34 | G-34; `H/ProductionRunnerGuard.cs:58-62` | keep the hook but do not set `BaseUrlEnvVar` | `HangfireStartupSafetyTests.Assembly_guard_and_factory_override_disable_the_Hangfire_worker` | the dead-runner URL assertion (binding required) |
| PC-35 | G-35; `H/PtyBackendEnvGuard.cs:17` | leave `[Before(Assembly)]` on the original method | `C1729AssemblyHooksTests.Original_sites_carry_no_assembly_hook_attributes` | `single-declaration:ClearInheritedPtyBackend` |

Guard audit: **guards=35, mapped=35, missing=0, duplicate PC maps=0**. All 35 PCs are mandatory
post-land work; executed in this design-only stage: **0**. PC-11/PC-12 bind after P0-G2.

### Wave 0 mechanical slices: what a Code delegate verifies

P0-A1, P0-E, P0-F1, P0-A2, P0-D, P0-C4, P0-T1a, P0-T2 can be dispatched now in parallel worktrees;
zero overlap with the seven in-flight branches was re-measured at inspection. A1, E, F1, T1a and
T2 are `mechanically-eligible` for a Low model in the sense of `docs/mechanical-pc-contract.md`
(a closed file list, a closed command list, no site location, no design); A2 (one file split),
D (one extracted helper and a pinned-vector test) and C4 (two ports) are Medium. Every delegate
runs exactly this list and reports every line:

1. **Move.** `git mv` only, byte-identical (D-1); the only content edits are the ones the slice
   names (A2: the `RunnerRequestIntent` split keeping namespace `Antiphon.Server.Application.Services`;
   D: `Core/ParkFingerprint.cs` and the two callers; C4: two interfaces, two `: IX` clauses,
   constructor parameter types, two `Program.cs` alias lines; T1a: the two helpers into
   `H/RemoteScriptContractSupport.cs` and `RemoteScriptContractTests.X` -> `X` in the five member
   consumers; T2: line 19 plus the two fail-closed assertions). Proof:
   `git diff -M100% --stat <start>..<tip>` shows every move at 100% and the named edits only.
   Any csproj/props/targets/sln edit means the slice has left Phase 0: stop and report (risk 1).
2. **Compile check before the cold build (SHOULD, Debug P4):**
   `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1729-<slice>-compile -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false`,
   reported as unlisted with this reason; skip when the checkpoint build itself is the first build.
3. **Parity (D-9):** shape 1 for A1, A2, E, F1, C4; shape 2 for D (`added=1`, the D1 method), T1a
   and T2 (`added=0 removed=0`).
4. **Allowlist rows (D-3):** delete the slice's `# slice <id>` group; add rows the move newly
   exposes under the owning header from the row ownership table (A1: the four moved files that
   reference Application gain `P0-B` rows; E: the drivers' Runners references gain `P0-C3` or
   `later-phase` rows; A2: an impure "pure" type is reported, its rows go under `P0-A2-residual`
   for the caller). Rows are the pasted printout, never hand-typed.
5. **Checkpoint run:** commit, then
   `dotnet run --project tools/Antiphon.Checkpoints --no-build -- run --plan docs/superpowers/testdesign/2026-11-05-card-1729-phase0-testdesign.md --after <slice> --expected-source-sha <committed sha> --total-timeout <20 + row caps>m --hard-wall-timeout <total + 12 x (rows+1)>m`,
   `wait` until exit is not 75, leave source untouched through the last row, validate the receipt
   against the tested SHA, keep the unedited `CHECKPOINT` lines. The guard row before P0-G2 proves
   `map-errors=0 parse-errors=0` and prints the inventory: paste `VIOLATIONS n LISTED m STALE s
   AMBIGUOUS a` and require `VIOLATIONS 0 STALE 0` after step 4; after P0-G2 the row is simply green.
6. **Evidence:** `scripts/check-evidence-diff.ps1 -BaseRef <start> -HeadRef <tip>` exit 0; report
   the `CHECKPOINT` lines, the `PARITY` line, the rename proof, the admission/execution split and
   the load at build start (Debug P6); push after every commit.
7. **Classification policy:** none of the eight slices adds or renames a test class, so none
   touches `tests/Antiphon.Tests/slow-tests-allowlist.txt`, `tests/test-execution-policy.json`, the
   nightly `policyHash`, `tests/test-selection-policy.json`/`catalogHash`, `tests/rc-project-categories.json`,
   `tests/live-lanes.json` or the live-lane census pin; `scripts/lib/nightly-class-costs.json` is
   keyed by class and unaffected. Exception: P0-D adds `C1729ParkFingerprintTests` (Unit, not
   Slow), which still touches none of them (see the per-slice answers below).

Per-slice classification answers for the whole Phase 0 (the brief's question), from the inspection
of the policy files: `tests/test-execution-policy.json` names suites, projects, scripts and
dispositions and **no test class**, so a new or renamed class never changes it or `policyHash`
(`Get-NightlyPolicyHash` hashes only that file). The live-lane census is commission-scoped
(`census scope=commission not-run-on-later-trees`), pinned to `pre-s1-base.sha` and a fixture blob,
and is **not re-run on later trees** (CARD-1645), so Phase 0 classes never refresh it; only a
`LiveClaude` lane row would touch `tests/live-lanes.json`, and none is added. `slow-tests-allowlist.txt`
changes only for a `[Category("Slow")]` class; every new class here is Unit or plain Integration, so
no registry row and `TestClassificationGuardTests` stays green. `tests/linux-test-roster.json` is a
frozen CARD-0590 roster with no script or test consumer at this base (grep over `.ps1`/`.cs`:
none); Phase 1 re-freezes it per assembly (plan step 3.4). `nightly-class-costs.json` prices an
unknown class with the node model (`nightly-tests-impl.ps1:149-165`). Conclusion: **no Phase 0 slice
requires `test-execution-policy.json`, `policyHash` and the live-lane pin to be refreshed
together**; the only slice that renames a test-bearing type would be a T1a/T1b host whose nested
class carried `[Test]` methods, which parity forbids. Slices adding classes: G (`C1729LayeringGuardTests`,
Unit), B (`C1729CoreBoundaryTests`, Unit), C5 (`C1729LandGitPortTests`, Unit), D (`C1729ParkFingerprintTests`,
Unit), H2 (`C1729MessagingExceptionTests`, Unit, in `Antiphon.Messaging.Tests`, whose own
`slow-tests-allowlist.txt` is untouched), I (`C1729ProgramCompositionTests`, Integration), T3
(`C1729AssemblyHooksTests`, Unit). Each new class is tagged exactly once for
`TestLaneCategoryGuardTests` (Antiphon.Tests only).

### Out of scope

- The two digest-changing spec items (D-5): the staged-helper `.targets` move and the
  `PtyBackend`/`PtyInputEncoding` move ride Phase 1. No row here builds a changed csproj.
- Namespace changes (D-1), the Phase 1 verifier generalisation, support libraries, Checkpoints
  carve-out, policy-hash recomputation, `linux-test-roster.json` re-freeze, Phase 2+ moves.
- Running `RemoteScriptContractTests` whole (227 methods, 499 nodes, pace 4,591 s) for T1a, or the
  16 T1b host classes whole: the extraction leaves their bodies byte-identical except the
  qualification of helper calls, which the build proves; parity proves no test moved; the moved
  helpers' runtime is exercised by the Codex/Grok image-contract and cache-fixture consumers in
  CP-30. `RollingVolumeRecycleScriptTests` (27 nodes, observed-bound 1,740 s) is a consumer and is
  excluded for cost; its compile is the build.
- A real-broker queue-full or size refusal for H2 (declared substitute above); re-proving channel
  recipient receipt; a Messaging package version bump (O-4 is the caller's).
- Full Unit lane, Pty, SessionRunner, E2E and provider suites: no runtime path outside the server
  assembly and `Antiphon.Messaging.Client` changes; direct graph is `Antiphon.Tests -> server` plus
  `Antiphon.Messaging.Tests -> Messaging.Client` for H2 (R7).
- Live Windows execution (nothing runs on the desktop); every row is portable and runs on server2.
- Mutation execution and desktop/activation actions of any kind.

**P0-F2 finding (decision for the caller, not a blocker):** at this base
`Infrastructure/Resilience/ResilienceReadClients.cs` is a 20-line `internal static` HTTP-client
selector (`Select(IHttpClientFactory?, HttpClient, string)`) with no database coupling; the DB side
is the separate `DatabaseResilienceExecutor`, which `WorkflowTemplateService` consumes
(Application-to-Persistence, legal). The map already classes the file Application, so no port is
needed to satisfy the guard; the recommended shape is a Low-tier rename,
`git mv Infrastructure/Resilience/ResilienceReadClients.cs Services/ResilienceReadClients.cs`
(namespace unchanged, the two importers' `using` stays valid), which could join Wave 0. CP-25 is
frozen for either shape: it runs the existing `ResilienceCircuitTests` (6 Unit methods that call
`Select`) with the guard. If the caller keeps the plan's port, Code adds no new class for F2 and
CP-25 is unchanged.

### Checkpoints

The 36 rows below are the **frozen closed ordinary roster** for every Phase 0 slice. `After` is the
slice id; each slice is its own run with one isolated build and **at most three rows**; a `CP-n`
Build cell reuses that row's output within the same `After` only. Filters use the CARD-0403 form
with a trailing `*` on every OR operand and `\|` for the pipe. `Min` is the TUnit **execution**
floor from the roster read above (argument-expanded cases; nested fixture classes are not tests);
Code re-reads the first TRX roster for each row and, if the runner's expansion differs from the
static count, corrects the imported manifest's `Min` before the run and reports the correction,
never widens a filter. All rows `Serial`. `Environment` keeps the CARD-1514 phase recorder on with
the row's id (Debug P6). `EstimatedMinutes` includes the build for a building row (about 7 min at
the p50 cold build) and is a time, never a count.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | P0-G | tests/Antiphon.Tests -> bin-c1729-g/ | layering-guard | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1, V-2, R-1; L1..L9 | exactly the 9 frozen methods, 0 failed/skipped; inventory printed | 9 | 10 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-1 |
| CP-2 | P0-A1 | tests/Antiphon.Tests -> bin-c1729-a1/ | core-value-types | /*/Antiphon.Tests.Application/(ModelAliasTests*)\|(DispatchConcurrencyPolicyTests*)\|(PipelineStagesJsonTests*)/* | V-3 | 19 methods = 76 + 8 + 4 cases, 0 failed/skipped | 88 | 10 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-2 |
| CP-3 | P0-A1 | CP-2 | guard-a1 | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; P0-A1 rows removed, P0-B rows added | 9 methods, 0 failed/skipped; VIOLATIONS 0 STALE 0 | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-3 |
| CP-4 | P0-A2 | tests/Antiphon.Tests -> bin-c1729-a2/ | core-pure-types | /*/Antiphon.Tests.Application/(ProviderContractCatalogTests*)\|(RepositoryLeaseWaitersTests*)\|(CardTaskFileRendererTests*)/* | V-3; RunnerRequestIntent split compiles | 37 + 7 + 12 methods, 0 failed/skipped | 56 | 10 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-4 |
| CP-5 | P0-A2 | CP-4 | guard-a2 | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; A2 residual rows reported | 9 methods, 0 failed/skipped | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-5 |
| CP-6 | P0-B | tests/Antiphon.Tests -> bin-c1729-b/ | core-to-app-fixes | /*/Antiphon.Tests.Application/(DispatchConcurrencyPolicyTests*)\|(PipelineStagesJsonTests*)\|(PtyDeliveryCeilingsTests*)\|(RolePolicyConfigurationValidationTests*)/* | V-4, R-2 | 8 + 4 + 14 + 4 executions (RolePolicy data source re-read), 0 failed/skipped | 30 | 10 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-6 |
| CP-7 | P0-B | CP-6 | guard-b | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; zero P0-B rows remain | 9 methods, 0 failed/skipped | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-7 |
| CP-8 | P0-B | CP-6 | core-boundary-witnesses | /*/Antiphon.Tests.Application/(C1729CoreBoundaryTests*)\|(ChannelOutboundPolicyTests*)/* | V-4, R-3; B1..B4 | 4 frozen + 8 methods (29 cases), 0 failed/skipped | 33 | 4 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-8 |
| CP-9 | P0-C1 | tests/Antiphon.Tests -> bin-c1729-c1/ | git-workspace-port | /*/Antiphon.Tests.Application/(GitWorkspaceServiceTimeoutTests*)\|(CardTaskFileServiceTests*)/* | V-3; IGitWorkspace consumers | 1 + 21 methods, 0 failed/skipped | 22 | 12 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-9 |
| CP-10 | P0-C1 | CP-9 | hand-built-graph | /*/Antiphon.Tests.TestHelpers/DelegationTestServicesTests/* | R-8 | 5 methods, 0 failed/skipped | 5 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-10 |
| CP-11 | P0-C1 | CP-9 | guard-c1 | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; GitWorkspaceService rows removed | 9 methods, 0 failed/skipped | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-11 |
| CP-12 | P0-C2 | tests/Antiphon.Tests -> bin-c1729-c2/ | card-file-board-lookup-port | /*/Antiphon.Tests.Application/CardFileBoardLookupTests/* | V-3; ICardFileBoardLookup consumers | 18 methods (partial class, 3 files) = 45 cases, 0 failed/skipped | 45 | 11 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-12 |
| CP-13 | P0-C2 | CP-12 | guard-c2 | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; CardFileBoardLookup rows removed | 9 methods, 0 failed/skipped | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-13 |
| CP-14 | P0-C3 | tests/Antiphon.Tests -> bin-c1729-c3/ | phone-home-ports | /*/Antiphon.Tests.Application/(MultiRunnerDirectoryTests*)\|(RunnerRetireJobTests*)\|(HostStatsPollServiceTests*)/* | V-3; directory and client ports | 7 + 4 + 10 methods, 0 failed/skipped | 21 | 11 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-14 |
| CP-15 | P0-C3 | CP-14 | guard-c3 | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; PhoneHome rows removed | 9 methods, 0 failed/skipped | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-15 |
| CP-16 | P0-C4 | tests/Antiphon.Tests -> bin-c1729-c4/ | probe-and-audit-ports | /*/Antiphon.Tests.Application/(ProjectSetupServiceTests*)\|(AuditArchiveEndpointTests*)/* | V-3; IOrchestratorWorkspaceProbe and IAuditService consumers | 10 methods (11 cases) + 1, 0 failed/skipped | 12 | 10 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-16 |
| CP-17 | P0-C4 | CP-16 | guard-c4 | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; layout and audit rows removed | 9 methods, 0 failed/skipped | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-17 |
| CP-18 | P0-C5 | tests/Antiphon.Tests -> bin-c1729-c5/ | land-git-port | /*/*/(AgentTaskLandVerifierTests*)\|(LandVerificationBuildSlotTests*)\|(GitIndexLockTests*)\|(LandWorkspaceTests*)\|(ServerGitProcessTests*)\|(C1729LandGitPortTests*)/* | V-5, R-7; C5-1..C5-4 | 8 + 14 + 13 + 8 + 18 cases + 4 frozen methods, 0 failed/skipped (GitIndexLockTests nested class roster re-read) | 65 | 14 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-18 |
| CP-19 | P0-C5 | CP-18 | guard-c5 | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; ServerGitProcess, GitIndexLock, LandWorkspace, LandVerificationArtifacts rows removed | 9 methods, 0 failed/skipped | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-19 |
| CP-20 | P0-D | tests/Antiphon.Tests -> bin-c1729-d/ | back-reference-inversions | /*/Antiphon.Tests.Application/(AgentTaskLandVerifierTests*)\|(TaskParkPublicationTests*)\|(WorktreeHealthServiceTests*)\|(C1729ParkFingerprintTests*)/* | V-6, R-7; D1 | 8 + 4 + 7 cases + 1 frozen method, 0 failed/skipped | 20 | 13 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-20 |
| CP-21 | P0-D | CP-20 | guard-d | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; three Git-to-Application rows removed | 9 methods, 0 failed/skipped | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-21 |
| CP-22 | P0-E | tests/Antiphon.Tests -> bin-c1729-e/ | runner-drivers | /*/Antiphon.Tests.Application/(SessionRunnerEventPumpTests*)\|(HostStatsPollServiceTests*)\|(RunnerRetireJobTests*)/* | V-3; moved drivers register and run | 3 methods (8 cases) + 10 + 4, 0 failed/skipped | 22 | 11 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-22 |
| CP-23 | P0-E | CP-22 | guard-e | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; driver rows moved to P0-C3/later-phase, no other change | 9 methods, 0 failed/skipped | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-23 |
| CP-24 | P0-F1 | tests/Antiphon.Tests -> bin-c1729-f1/ | guard-f1 | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1, V-3; five 100% renames; Persistence-to-Git row removed | 9 methods, 0 failed/skipped | 9 | 10 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-24 |
| CP-25 | P0-F2 | tests/Antiphon.Tests -> bin-c1729-f2/ | resilience-read-clients | /*/*/(ResilienceCircuitTests*)\|(C1729LayeringGuardTests*)/* | V-7, V-1 | 6 + 9 methods, 0 failed/skipped | 15 | 10 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-25 |
| CP-26 | P0-H2 | tests/Antiphon.Tests -> bin-c1729-h2/ | outbound-typed-exceptions | /*/Antiphon.Tests.Application/ChannelOutboundRetryPolicyTests/* | V-8, R-4 | 22 methods, 0 failed/skipped | 22 | 11 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-26 |
| CP-27 | P0-H2 | tests/Antiphon.Messaging.Tests -> bin-c1729-h2m/ | messaging-exception-mapping | /*/Antiphon.Messaging.Tests.Client/C1729MessagingExceptionTests/* | V-8; H2-1..H2-3; PublicAPI analyzers compile | exactly the 3 frozen methods, 0 failed/skipped | 3 | 6 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-27 |
| CP-28 | P0-I | tests/Antiphon.Tests -> bin-c1729-i/ | program-boot | /*/*/(HangfireStartupSafetyTests*)\|(WorktreeResidueRegistrationTests*)\|(RunnerAlarmWiringTests*)\|(C1729ProgramCompositionTests*)/* | V-9; I1 | 13 + 3 + 1 + 1 frozen method, 0 failed/skipped | 18 | 12 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-28 |
| CP-29 | P0-I | CP-28 | guard-i | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1; server/Composition/** is Host | 9 methods, 0 failed/skipped | 9 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-29 |
| CP-30 | P0-T1a | tests/Antiphon.Tests -> bin-c1729-t1a/ | remote-script-host-extraction | /*/*/(CodexRunnerImageContractTests*)\|(GrokRunnerImageContractTests*)\|(CacheFixtureCompletionTests*)\|(C1008PlatformContractTests*)\|(ProcessSpawnTimeoutPolicyTests*)\|(PlanCoveragePcTests*)\|(TestLaneCategoryGuardTests*)/* | V-10, V-11 | 13 + 26 + 19 + 6 + 2 + 8 + 1 cases, 0 failed/skipped | 75 | 12 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-30 |
| CP-31 | P0-T1b | tests/Antiphon.Tests -> bin-c1729-t1b/ | helper-host-extraction | /*/*/(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/* | V-10, V-11; one run per T1b task | 1 + 9 methods (21 cases), 0 failed/skipped | 22 | 10 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-31 |
| CP-32 | P0-T2 | tests/Antiphon.Tests -> bin-c1729-t2/ | lane-guard-root | /*/Antiphon.Tests.TestHelpers/TestLaneCategoryGuardTests/* | V-11; G-32 | 1 method, 0 failed/skipped | 1 | 9 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-32 |
| CP-33 | P0-T3 | tests/Antiphon.Tests -> bin-c1729-t3/ | assembly-hooks-db | /*/Antiphon.Tests.TestHelpers/TestDbFixtureLazyInitializationTests/(A_*)\|(Mixed_*) | V-11, R-6 | 7 executions (6 A_ + 1 Mixed_; WorkerMarkers data source re-read), 0 failed/skipped | 7 | 14 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-33 |
| CP-34 | P0-T3 | CP-33 | assembly-hooks-runner-guard | /*/Antiphon.Tests.Infrastructure/HangfireStartupSafetyTests/Assembly_guard_and_factory_override_disable_the_Hangfire_worker | R-5, G-34 | the 1 exact method, 0 failed/skipped | 1 | 5 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-34 |
| CP-35 | P0-T3 | CP-33 | assembly-hooks-sweep | /*/*/(CheckpointTempRootSweepTests*)\|(C1729AssemblyHooksTests*)/* | V-11; T3-1, T3-2 | 22 methods (28 cases) + 2 frozen methods, 0 failed/skipped | 30 | 3 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-35 |
| CP-36 | P0-T5 | tests/Antiphon.Tests -> bin-c1729-t5/ | migrations-assembly-constant | /*/Antiphon.Tests.TestHelpers/TestDbFixtureLazyInitializationTests/(A_*)\|(Mixed_*) | V-12, R-6 | 7 executions, 0 failed/skipped | 7 | 14 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-36 |

Roster audit at `73fe0aa29`: no class starts with `C1729`; every other class prefix above selects
exactly one class (`grep -rhoE "class <Name>[A-Za-z0-9_]*"` returned one name each);
`CardFileBoardLookupTests` is one partial class in three files and is addressed by class name;
`GitIndexLockTests.cs` also declares a nested `GitIndexLockPathTests`, which the `(GitIndexLockTests*)`
operand selects only if TUnit lists it under the outer class, so CP-18's first TRX fixes that
count; `TestDbFixtureLazyInitializationTests` keeps the `(A_*)|(Mixed_*)` method filter because a
class-wide filter skips its three `Child_*` parent probes and a skip fails validation; CARD-1677's
run of that filter executed 7. Cross-namespace rows use `/*/*/` because their classes sit in
`Antiphon.Tests.Infrastructure`, `.Application`, `.Scripts`, `.Checkpoints`, `.TestHelpers` or
`.Infrastructure.Resilience`.

**Row caps and run budgets (R5).** Manifest `build.properties.UseAppHost: false`,
`build.slotWaitMinutes: 12`, `timeouts.rowMinutes: 20` (the shared build cap, 2 x 579 s rounded;
the measured p95 817 s is inside). Per-row `timeoutMinutes`, each about 2 x a measured figure or
labelled provisional (CARD-1729; bounded collection plan: the first three green rows on server2):
guard rows 2 (provisional, n=0; 10-22 s host + 5-15 s body); CP-2, CP-4 2 (Unit bodies under 10 s);
CP-6 2, CP-8 3 (provisional); CP-9 6 (2 x (91 + 3 + 60) s); CP-10 2; CP-12 5 (2 x (77 + 60) s);
CP-14 5 (provisional; `RunnerRetireJobTests` 32 s measured, the rest unmeasured); CP-16 3
(2 x (21 + 2 + 60) s); CP-18 8 (provisional; 105 s verifier + 60 s container + three unmeasured
classes); CP-20 9 (2 x (105 + 69 + 7 + 60) s); CP-22 5 (provisional; 8 + 32 s measured,
`HostStatsPollServiceTests` unmeasured); CP-25 2; CP-26 5 (provisional, unmeasured); CP-27 2;
CP-28 5 (one Program boot 30-90 s, provisional); CP-30 5 (provisional; 37 + 17 s measured, image
contracts unmeasured); CP-31 4 (provisional); CP-32 2; CP-33 and CP-36 8 (child-spawning, 3-6 min,
provisional); CP-34 5; CP-35 3. `--total-timeout` per run = 20 + the run's row caps (for example
P0-G 22m, P0-A1 24m, P0-B 27m, P0-C5 30m, P0-D 31m, P0-T3 36m, P0-H2 27m); `--hard-wall-timeout`
= total + 12 x (rows + 1). Expected wall per slice run: p50 about 12-15 min (432 s build, 0-47 s
admissions, rows, about 1.5 min tool remainder); an 11b7-like burst about 35 min (817 s build,
3 x 255 s admission). An observed run over 30 min is reported with its admission/execution split
citing the S5b-1 Debug and CARD-1531; admission does not exempt it from R1, and the caller decides
on a new Debug. Never pack more than three rows into one run; never use `-NoSlot`; exit 4 is
reported, not retried unleased. T1b runs CP-31 once per task (three runs at three commits, not
reruns); P0-G2 reruns CP-1 (`reruns=1`).

### Cost

All figures are **estimated planning floors**, not measurements. Comparators: successful cold
isolated `Antiphon.Tests` build n=11, p50 432 s, p95 = max 817 s; admission n=39, p50 0 s, mean
46.5 s, p95 255 s; test-host startup 10-22 s (51-67 s under load); warm native incremental build
143 s (CARD-1677). No build or test was run by TestDesign.

**Ordinary Code V/R floor** = table sum **266 min** (CP-1..CP-36: 10+10+2+10+2+10+2+4+12+2+2+11+2+11+2+10+2+14+2+13+2+11+2+10+10+11+6+12+2+12+10+9+14+5+3+14),
of which 21 isolated builds at about 7 min = 147 min and 119 min of rows. Add the P0-G2 CP-1
rerun (10) and the two extra T1b runs of CP-31 (20): **296 min** across 23 runs. Parity adds, for
the twelve test-touching slices, two native incremental builds (about 3 min each, provisional) and
two discovery hosts (about 1 min each) = 8 min x 12 = **96 min** of declared unlisted runs; the P4
compile check adds about 3 min per slice where used. Independent Review re-uses the same table.
Admission is separate: 12 min finite maximum per acquisition; a 3-row run has 4 acquisitions
(48 min ceiling); the 23 ordinary runs total 78 acquisitions, **936 min admission ceiling**, with
the cited mean 46.5 s giving about 60 min expected.

**PC floor (Mutation).** 35 controls, method-scoped. Four (PC-1, PC-2, PC-11, PC-12) edit committed
text and need no build: 4 x (0.5 + 1 + 1) = 10 min. Thirty-one need one mutated build and one
restored build at about 3 min each (native incremental, provisional) plus 0.5 min accounting and
1-2 min per leg: 24 Unit controls x (6 + 0.5 + 1 + 1) = 204 min; 7 Integration controls (PC-27..29,
PC-30, PC-31, PC-34 and PC-21's link case) x (6 + 0.5 + 2 + 2) = 73.5 min. Cycles subtotal
**287.5 min**. Baseline greens at unchanged L: one native build (3 min) + 35 exact-method runs
(28 x 1 + 7 x 2 = 42 min) = **45 min**. **Mutation floor = 332.5 min (5h33m)**, before admission
(about 141 acquisitions, 1,692 min ceiling, about 110 min expected at the cited mean). No
speculative batching saving is credited: PC-3..PC-10 share one engine file and must cycle one at a
time; only controls on different files and methods may batch under the mutation owner policy.

**Total** = 296 (ordinary) + 96 (parity) + 332.5 (PCs) = **724.5 min**, plus admission. Savings:
reusing one build in each slice's second and third rows saves 15 x 7 = 105 min versus per-row
builds; folding the guard into CP-25 saves one row acquisition; selecting T1a's consumers instead
of the whole host saves at least 76 min of pace time per run; running no whole Unit lane saves the
unmeasured 30+ min lane.

Before handoff: guards=35, mapped=35, missing=0, duplicate PC maps=0; all 35 PCs scheduled as
mandatory post-land work, none run here; Cost is numeric above.

### Binding and import handoff

Code order per slice: commit the source move; for test-adding slices commit the new class first
with its equivalence or pinned-vector assertions written against the extracted symbol (these are
**not red at base**: B1..B4, C5-1..C5-4, D1, H2-1..H2-3, I1, T3-1, T3-2 reference types that do not
exist at base, so the absence of the type is a compile error, never an assertion-red witness; their
load-bearing proof is the PC table after land; L5..L9 are red against the listed engine defects by
construction and L4 is red on the real tree with an empty allowlist in strict mode). The one
genuinely red-at-base existing assertion is R-2's message text, which PC-15 exercises. Record each
new class's final SHA, path, symbol and line before Review.

Code for P0-G also commits the `docs/project-context.md` paragraph pointing at `layers.txt`, and the
printed seed inventory in its report. Code for P0-T2 adds the two fail-closed assertions
(`lane-guard-root-exists`, `root-has-files`) so a wrong derivation cannot pass vacuously. Code for
P0-H2 edits the four Antiphon.Tests files that construct `ProduceException` so their fakes throw the
typed exceptions, drops `using Confluent.Kafka` from the pump, leaves it in
`InboundUnconsumedEventConsumer` (Host) and the server csproj (no digest change), adds the two
exception types to `src/Antiphon.Messaging/PublicAPI.Unshipped.txt`, and adds
`src/Antiphon.Messaging.Client/InternalsVisibleTo.cs` for the mapper. Code for P0-I captures the
hosted-service roster once at its start commit through the same factory and pastes it as the frozen
constant. Code for P0-D pastes the two SHA-256 vectors captured from `BlockedTaskParkingService.Digest`
at its start commit.

Import validation at this design's source: the Checkpoints table above was imported read-only with
a prebuilt `tools/Antiphon.Checkpoints` from a sibling task's scratchpad
(`/tmp/claude-1654/-work-worktrees-task-a2cfcdc1/99fce261-fb7b-4ec8-b78d-86c1d6a3d216/scratchpad/tool-head/Antiphon.Checkpoints.dll`,
built 2026-11-05 06:37, which predates `9c13c6d42`'s `RowTimeout` change but not the importer's
filter and column rules): `import --plan <this file> --repo-root <worktree> --out .antiphon/c1729-td-lint/td.yaml`.
The same binary imported the plan's 36 rows with exit 0 as a control. This is a syntax substitute
with unbound build provenance, not executable verification credit; the generated YAML was deleted
and nothing was built. The result line is recorded in the TestDesign report. Execution uses the
tool's `run`/`wait`/`validate` verbs with `--expected-source-sha`, never `dotnet test`; the Code brief
points at this table as `checkpoints: docs/superpowers/testdesign/2026-11-05-card-1729-phase0-testdesign.md@<this commit> section "### Checkpoints"`.
