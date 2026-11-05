# Solution split by external dependency: production projects with mirrored test projects

Status: plan, 2026-11-05. Measured at `d465bd4e3`. Appendix A covers the production split and Appendix B the test split. Both are read-only analyses; their numbers come from regex-based scripts, so allow about ±10%.

## Why

- **Any change under `server/` runs the whole suite.** In `tests/test-selection-policy.json`, `P-SERVER` is `full: true`, and every narrowed rule still includes `main.remainder`, which is all of `Antiphon.Tests`. So all 3,000 recent commits ran all 14,415 tests (about 21.9k cases).
- **`Antiphon.Tests` is one 646k-line project**, so any edit recompiles all of it. With TUnit reflection mode that takes about 70 s and peaks at 4.6 GB; before the switch it was 105 s and 6.6 GB. `server/` adds 1.19M lines of generated migrations to every server build.
- **Goal:** once the code is split, "what changed" follows from the `ProjectReference` graph. A Git-only change runs the Git tests plus the tests of whatever references Git, not everything.

## Decisions (operator, 2026-11-05)

1. **Split the production code along logical-architecture and external-dependency lines** (Git, database, runners/PTY, LLM/vendor HTTP, host), and **give each production project a mirrored test project.** This replaces the area-based test-only split recommended in Appendix B §3. On its own, a test split cannot narrow server edits.
2. **Split the database out wherever possible.** Application code reaches Postgres through ports, and tests that only use Postgres incidentally move to fakes. That's about 2,800 tests: 1,390 whose system under test has no EF, plus 1,419 whose system under test uses only simple EF.
3. **Shared test code goes into a `public` support library** (`Antiphon.Tests.Support`, plus a server-free `Antiphon.Tests.Support.Core`). This supersedes Appendix B §4's internal/`InternalsVisibleTo` recommendation. Public helpers must not expose internal server types (CS0050/CS0051); move or widen those types as part of the extraction.
4. **Each test assembly has a small local hook file** (`[Before(Assembly)]`/`[After(Assembly)]`) that calls into the shared support code: ProductionRunnerGuard, PtyBackendEnvGuard, the TestDbFixture lifecycle and CheckpointTempRootSweep. An experiment with TUnit 1.44 established why:
   - a `[Before(Assembly)]` declared in a library never fires for another assembly, in either mode;
   - library `[BeforeEvery(Assembly)]`, `[Before(TestSession)]` and `[BeforeEvery(Test)]` do fire, but in reflection mode only if the library is loaded before test discovery.

   Explicit local hooks avoid both traps. The `TestWorkerModes` self-relaunch registry stays per assembly, because each crash worker must live in the assembly that spawns it.
5. **Namespaces don't change; only assemblies move.** Tree-node filters, `nightly-class-costs.json` and `using` lines stay valid.
6. **Test projects run one after another.** `ProcessSpawnLimit` (Limit=1, on 794 classes), `GitProcessSpawnLimit` and the `NotInParallel` keys (MessageQueue 66, AgentQueue 34) only hold within one test process.
7. **Test code that doesn't touch the server keeps its own lanes:** `Antiphon.Tests.Checkpoints` (25k lines, server-free), `Antiphon.Tests.Scripts` (46k lines, 3 server-touching files) and `Antiphon.Tests.Tui` (provider/PTY lane, 49k lines).

## Target layout

| Production project (`src/`, host stays at `server/`) | Lines | External deps | Mirrored test project | [Test] |
|---|---:|---|---|---:|
| `Antiphon.Server.Core` (Domain, Dtos, Interfaces, Exceptions, Settings, value types, ports) | 29k | none beyond contracts | `Antiphon.Server.Core.Tests` | 520 |
| `Antiphon.Server.Persistence` (AppDbContext, Data, Migrations) | 5.4k + 1.19M generated | EF Core, Npgsql | `Antiphon.Server.Persistence.Tests` | 279 |
| `Antiphon.Server.Git` | 12.4k | LibGit2Sharp, git/dotnet/docker processes | `Antiphon.Server.Git.Tests` | 446 |
| `Antiphon.Server.Runners` | 10.6k | Agents.Pty (native), SessionRunner.Contracts | `Antiphon.Server.Runners.Tests` | 349 |
| `Antiphon.Server.Integrations` (LLM + GitHub/Jira/Linear) | 3.9k | Microsoft.Extensions.AI/OpenAI, HttpClient | `Antiphon.Server.Integrations.Tests` | 20 |
| `Antiphon.Server.Application` | 174k | Core; Persistence until phase 9 | `Antiphon.Server.Application.Tests` | 6,694 |
| `Antiphon.Server` (host: Program, Api, SignalR, Hangfire, DI composition) | 11k | ASP.NET, Hangfire, Serilog, OTel, Kafka client | `Antiphon.Tests` (host/integration) | 4,088 |
| — | | | `Antiphon.Tests.Checkpoints` / `.Scripts` / `.Tui` | 721 / 1,300 / 1,082 |
| — | | | `Antiphon.Tests.Support` (public), `.Support.Core` (server-free) | 0 |

Reference graph (acyclic once the Phase 0 fixes land): the host references everything; Application, Git, Runners, Integrations and Persistence each reference Core; Application references Persistence until phase 9 removes it. Test projects reference Support and their own production project, never each other.

**Expected benefit**, from replaying 3,000 commits (2026-10-09 to 2026-11-05):

| | Value |
|---|---|
| Commits that run only a subset of tests | 87% (today 0%) |
| Median tests run per commit | 46% (today 100%) |
| Test-only commits (67% of all), median | 4,088 tests |
| Commits touching only Git or Runners, median | about 4,500 tests |
| Commits touching Application code, median | still about 75%, because Application stays one 174k-line project |
| Commits still forced to run everything | 13%: they edit shared test helpers or a csproj |

On the test-compile side, the median recompile drops from 646k lines to about 99k (Appendix B).

## Phased plan

Every phase lands as its own reviewed change set, rebased onto master with no merge commits. Each phase has to pass these checks before it lands:
- the solution builds;
- **test-count parity**: TUnit `--list-tests` across all test projects must list exactly the same set of tests as before the phase, with nothing lost or duplicated;
- the layering guard test passes;
- the build-graph digest is re-recorded whenever a csproj or sln changes (`audit-land-verification-graph.ps1 -Update`, then `-Check`; no approval needed);
- the policy files validate (`catalogHash` and `policyHash` updated);
- the land classes affected by the phase pass.

### Phase 0: groundwork in place (no new csproj, no digest change)
Production code (Appendix A §2, §5.0):
- Move the 14 value types that `Application/Interfaces` and `Exceptions` import from `Services` into Core-namespaced folders: ModelAlias, RunnerState, SessionStateSnapshot, DispatchConcurrencyPolicy, SubscriptionQuotaPolicy, ChannelReplyPreparation, AntiphonDataPaths, ClaudeLaunchArgs, AgentTaskLandingState, PipelineStagesJson, PipelineDefinitions, WorkspaceReservationLiveness, WorktreeCleanupPresentation, VerificationReceiptPolicy.
- Fix the 11 Core→Application references: ChannelReplyPreparation, DelegationSettings, DispatchConcurrencyPolicy, SubscriptionQuotaPolicy, PipelineStagesJson.
- Move the pure Runners→Application and Git→Application types into Core:
  - RunnerPlatformProblems, GrokRulesLaunchValidation, RunnerAbsenceEvidenceValidator, ProviderContractCatalog, CodexCliObservation, RunnerRequestIntent, OrchestratorInstructionsSignal;
  - CardFilePreCommitSweep, CardTaskFileRenderer, DelegationReportFormatter, RepositoryLeaseWaiters, LandVerificationWaits.
- Add the ports:
  - `IGitWorkspace` (GitWorkspaceService has 15 concrete users) and move GitFileStatus to Core;
  - `ICardFileBoardLookup`;
  - `IPhoneHomeRunnerDirectory`, or widen ISessionRunnerDirectory (12 concrete users);
  - `IPhoneHomeRunnerClient`, `IOrchestratorWorkspaceProbe` and `IAuditService`;
  - an `ILandGit` port for ServerGitProcess, GitIndexLock, LandWorkspace and LandVerificationArtifacts.
- Invert the remaining back-references with callbacks or ports: LandingVerifier→AgentTaskLandService, LocalTaskParkPublisher→BlockedTaskParkingService, WorktreeHealthHostedService→WorktreeHealthService.
- Move the runner drivers and in-memory state into the Application namespace: SessionRunnerEventPump, PhoneHomeRecoveryPump, RunnerRetireJob, RunnerSlotReconcileJob, SessionRuntimeActionHostedService, HostStatsPollService, SeatOccupancyState, RunnerAlarmState, AgentTuiMetrics.
- Fix the smaller misplacements:
  - WorktreeRemovalEvidence → Application;
  - split ResilienceReadClients;
  - LlmProviderService and GitHubMonitorService → Application;
  - CodexSubmitConfirmation → Runners;
  - DataProtectionAgentTuiSecretProtector → Host.
- Move the non-server blockers:
  - PtyBackend and PtyInputEncoding into `Antiphon.SessionRunner.Contracts`;
  - `IAntiphonMessagingProducer`/`Consumer` into `src/Antiphon.Messaging`, with typed exceptions for QueueFull and MsgSizeTooLarge.
- Split Program.cs's 446 DI registrations into `AddAntiphonCore/Persistence/Application/Git/Runners/Integrations()`.
- **Add a namespace-layering guard test** that fails on any reference pointing the wrong way. It's the executable form of the reference graph above.

Tests (Appendix B §4, §8.0):
- Extract the nested helpers that about 150 test classes host for other areas into top-level helper files.
- Make the one-assembly guards work per assembly: ProcessSpawnLimitTests, TestLaneCategoryGuardTests, and the TestClassificationGuard/slow-tests registry.
- Move the staged-helper targets into `tests/Shared/*.targets`, so the build-graph pin applies once instead of once per project.
- Introduce the local `AssemblyHooks.cs` pattern (decision 4) inside `Antiphon.Tests`.

### Phase 1: support library, Checkpoints carve-out, multi-project verifier
- Create `Antiphon.Tests.Support.Core` (server-free; about 86 files) and `Antiphon.Tests.Support` (server tier), both public. Add `InternalsVisibleTo` for the support assemblies only where a production assembly already grants `Antiphon.Tests`.
- Carve out `Antiphon.Tests.Checkpoints` (server-free; 721 tests). Lower-risk variant: compile `../Antiphon.Tests/Checkpoints/**` from the new csproj with `<Compile Remove>` in the old one, and move the files physically later.
- **Generalise the land verifier:**
  - `AgentTaskLandService.cs:1957` hard-codes `dotnet run --project tests/Antiphon.Tests`, and `AgentTaskLandingProtocol.cs:323` hard-codes the matching message.
  - Run `LandVerifyFilter` across the selected test projects, one after another, and require `executed > 0` in total.
- Generalise the policies and the scripts that assume one test project:
  - test-execution-policy (suites, projects, dispositions);
  - test-selection-policy (`remainderProjects`);
  - rc-project-categories, linux-test-roster, live-lanes and live-lane-manifest.ps1;
  - the nightly groups, checkpoint-usage.ps1 and run-tests-watched.ps1.

  This phase proves the whole pipeline on the smallest piece.

### Phase 2: `Antiphon.Server.Core` + `Antiphon.Server.Core.Tests`
- Copy the `InternalsVisibleTo` entries from `server/Antiphon.Server.csproj:13-16`.
- Put `Aliases="global,server"` on every server reference.
- Update the sln and rc categories, and re-record the digest.

### Phase 3: `Antiphon.Server.Persistence` + `.Persistence.Tests`
- Repoint `MigrationsAssembly` at `server/Program.cs:135`, `AntiphonWebAppFactory.cs:155`, `BridgeQueueHarness.cs:126` and `CheckCompactionCrashWorker.cs:147`.
- Move `Microsoft.EntityFrameworkCore.Design` with it. `dotnet ef` then takes `--project src/Antiphon.Server.Persistence --startup-project server`.
- This removes 1.19M generated lines from the edit loop, the biggest single local-build gain.

### Phase 4: selection policy per project
- Replace `P-SERVER full:true` with one rule per production project. Each rule selects its mirrored test project plus the mirrors of the projects that reference it. Phases 2 and 3 already give it something to select.
- Carve out `Antiphon.Tests.Scripts`, after first moving out the three server-touching files and `MeasureSessionStateCacheScriptTests`.

### Phase 5: `Antiphon.Server.Integrations` + tests
This removes the AI/OpenAI packages from the application layer.

### Phase 6: `Antiphon.Server.Runners` + tests; carve out `Antiphon.Tests.Tui`
- Moving the Hangfire jobs is safe: storage is in-memory and jobs are registered with `AddOrUpdate<T>`.
- PtyBackendEnvGuard hooks go into Tui, Runners and Application tests.

### Phase 7: `Antiphon.Server.Git` + `.Git.Tests`
- Do this after the Phase 0 ports have landed.
- Keep the `LandVerificationBuildGraph.manifest.txt` LogicalName pin with `LandVerificationBuildGraph.cs`, which stays in Application.

### Phase 8: `Antiphon.Server.Application` + `.Application.Tests`
- A mechanical move of what remains.
- Embedded bundles: `InstructionBundles.cs:127` uses `ResourcePrefix = "Antiphon.Server.Bundles."`. Set `RootNamespace=Antiphon.Server` or give the resources explicit LogicalNames; the same applies to AgentPresets and InstructionFragments.
- Move the land crash workers' `TestWorkerModes` registrations with their tests.
- About 2,793 Application tests still reference git, runner or host concretes, so they stay in `Antiphon.Tests` until they use port fakes; for example, swap EventBus for MockEventBus in 56 files.

### Phase 9: database out
- Add repository/unit-of-work ports in Core, implemented by Persistence. Start with the 80 services that use simple EF and touch only 1–2 DbSets, then the remaining 82 simple-EF services.
- Move the 1,390 "system under test has no EF" tests onto fakes, then the 1,419 simple-EF ones.
- Remove `Application → Persistence` once no Application file uses EF directly. The 126 files that rely on Postgres behaviour (transactions, `FOR UPDATE`, `ExecuteUpdate`, advisory locks; 92k lines) keep their database tests until their ports carry those semantics.
- **Expected outcome:** about 2,800 tests no longer need a Postgres container, and commits that only touch Persistence stop running Application.Tests.

### Phase 10 (later, optional): split Application by area
- Application's areas reference each other heavily: Delegation↔Sessions 88/66, misc↔Sessions 105/92.
- Splitting it (Delegation 56k / Sessions 46k / Board 19k / Channels 14k) needs ports between those areas first. It is the only way to bring the 75% median for Application-touching commits down.

## Risks
- **Review volume.** Phase 0 touches about 130 reference sites and about 150 test files.
- **Limiters.** Concurrent test-project runs would break the per-process limiter guarantees, which is why decision 6 requires one project at a time.
- **Shared-helper edits** (13% of commits) still rebuild every test project, so keep Support small and stable.
- **Land builds.** Lands build the whole solution from scratch, so they only gain once the verifier builds just the selected projects, which needs the Phase 1 and Phase 4 policy work.


---

# Appendix A: production split analysis

## Splitting `server/Antiphon.Server.csproj` along external-dependency lines

Repo: `C:\src\antiphon` @ `d465bd4e3` (2026-11-05). This was read-only analysis: nothing in the repo was built or edited.
Tags: **[V]** means measured from the tree or git, **[I]** means inferred.
The scripts that produced every number are in (local analysis scripts, not committed):
- `analyze.py`: inventory and type-reference graph
- `layout.py`: proposed file→project map and layering violations
- `ef.py`: EF coupling
- `tests.py`: test-file DB classification and SUT mapping
- `replay.py`: git replay

Run them with `PYTHONHASHSEED=0`.

**Method caveats.**
- The reference graph is regex-based. It resolves top-level type names through the `using`/namespace scope, strips member-access names and string literals, and drops declarations, giving 2,903 types and 8,195 file→file edges. It is not Roslyn. Expect roughly ±10% noise on edge counts. The named offenders below were spot-checked.
- A test's SUT is chosen from its file name, falling back to the most-referenced non-Core server type.

---

### 1. Inventory of `server/` (lines; external dependencies by using-directive/type)

| Folder | files | lines | External dependencies (files using them) |
|---|---:|---:|---|
| Migrations | 408 | **1,194,334** | EF 408, Npgsql 182. Generated designer files and snapshot. |
| Application/Services (flat) | 567 | 171,331 | **EF 288**, SessionRunner.Contracts 142, Agents.Pty 22, Npgsql 20, Process 9, Hosting 8, Messaging.Client/Kafka 7, git-process 4, YamlDotNet 5, LLM 2, HttpClient 2, docker 1 |
| Infrastructure/Agents (root + Pty/SessionRunner/Tui/Tools) | 78 | 16,802 | SessionRunner.Contracts 23, Agents.Pty 12, Hangfire 11, EF 10, Hosting 8, LLM 4, Process 4, HttpClient 3, native 3 |
| Infrastructure/Git | 38 | 9,704 | Process 9, native 6, git-process 3, LibGit2Sharp 1 (`InProcessGitReads.cs`) |
| Application/Dtos | 121 | 7,917 | SessionRunner.Contracts 10, Messaging.Client 1 |
| Api/Endpoints + Middleware | 58 | 7,089 | ASP.NET 58, EF 17 (endpoints query the DbContext directly) |
| Application/Settings | 78 | 6,394 | Agents.Pty 1 (`DelegationSettings.cs`: `PtyBackend`, `PtyInputEncoding`) |
| Domain (Entities/Enums/StateMachine/VO) | 214 | 9,570 | **none.** Domain is already dependency-free. |
| Infrastructure/Data (+Seeding) | 14 | 5,250 | EF 14, Npgsql 5 |
| Application/Interfaces | 74 | 2,499 | SessionRunner.Contracts 8 |
| Infrastructure/Orchestration | 19 | 1,563 | Hosting 19 (BackgroundService loops over app services), EF 2 |
| Infrastructure/Files, FileSystem | 9 | 1,496 | System.IO only |
| Program.cs | 1 | 1,365 | everything; **446 DI registrations** in one file |
| Infrastructure/Supervision | 15 | 1,188 | Hosting 12, EF 3, Serilog 1, native 1 |
| Infrastructure/IssueTrackers, GitHub | 7 | 1,642 | HttpClient 4 |
| Application/Exceptions | 28 | 868 | — |
| Infrastructure/Security | 6 | 454 | ASP.NET DataProtection 3, Windows ACL 1 |
| Infrastructure/{ExternalChanges, WorkspaceHooks, Verification, WorkflowDefinitions, Resilience, Realtime, Logging, Messaging} | 14 | 1,754 | git/process 3, SignalR 2, EF/Npgsql 3, Kafka 1 |

Packages in today's single csproj: EF Core, Npgsql, LibGit2Sharp, Microsoft.Agents.AI, Microsoft.Extensions.AI(+OpenAI), Confluent.Kafka, Hangfire, OpenTelemetry, Serilog, System.Management, YamlDotNet, System.IO.Abstractions, plus the `Antiphon.Agents.Pty` reference, which brings in the native Porta.Pty/ConPTY packages.

**EF/database coupling in Application [V]:**
- **288 of 567** `Application/Services` files touch EF (133.5k of 171k lines). **255** take `AppDbContext` as a constructor or method parameter. No file uses `IDbContextFactory`.
- **126 files (92k lines) depend on Postgres semantics.** By number of files using each feature:
  - `BeginTransaction`: 81
  - `ExecuteUpdateAsync`: 54
  - `FOR UPDATE` row locks: 54
  - `ChangeTracker`: 49
  - `FromSql`: 31
  - `ExecuteSqlInterpolated`: 30
  - `DbUpdateException`: 27
  - `PostgresException`: 18
  - `pg_advisory`: 16

  The worst are `AgentTaskDispatcher.cs` (9.9k lines, 87 such calls), `AgentTaskLandService.cs` (46), `TerminalRunnerSeatReleaseService.cs` (41), `AgentService.cs` (40), `ExpectationLedger.cs` (39), `CapacityRecoveryService.cs` (36) and `SessionMessageQueueService.cs` (35).
- **162 files (41k lines) are EF-light**: LINQ, `Include` and `SaveChanges` only. 80 of them touch only 1–2 DbSets, 56 touch 3–5, and 18 touch 6 or more; `AttentionService.cs` (3.7k lines) touches 32. These are the candidates to move behind repository ports.
- Only **203 Application files (20k lines)** are dependency-free today, including transitively.

**Test-side Postgres use (`tests/Antiphon.Tests`, 1,673 test files, 14,415 `[Test]`) [V/I].** A file counts as using the database if it uses `TestDbFixture`, `TransactionalTestBase`, `TestDbClonePool` or `AppDbContext`. That covers 682 files and 6,717 tests.

| Class | files | [Test] | Meaning |
|---|---:|---:|---|
| genuine persistence | 201 | 1,978 | The test itself does raw SQL, transactions, interceptors, `DbUpdate*`/`PostgresException`, migrations or `NpgsqlConnection`, or its SUT is in Data/Migrations. Keep on Postgres. |
| SUT relies on Postgres semantics | 186 | 1,930 | The test is plain, but the service under test uses locks, transactions or `ExecuteUpdate`. Stays on Postgres until the port carries those semantics. |
| **incidental, EF-light SUT** | 135 | 1,419 | Fakes become possible once the SUT's DbSets are behind a repository port. |
| **incidental, SUT has no EF** | 160 | 1,390 | The database only seeds collaborators or builds a host. Cheapest to move. |

So about **2,800 tests (42% of the DB tests) use Postgres only incidentally**. The genuine core is about 2,000, and about 1,900 more are bound to Postgres through their SUT.

### 2. Cycles between candidate groups and the interfaces that break them

After placing files by external dependency (§3 layout), **132 type references (78 types) cross the layering the wrong way** [V]. They are listed in full in `split\layout.txt`. The worst, with the fix for each:

**Application → Git concretes (40 refs):**
- `GitWorkspaceService` (git CLI wrapper, 1.26k lines, used by 15 services such as `AgentFilesService`, `AgentTaskService`, `AgentTaskReplyService`, `CardTaskFileService` and `AgentReportStore`). Fix: add `IGitWorkspace` to Core and move `GitFileStatus` to Core.
- `OrchestratorWorkspaceLayout` and its `FactGatherer`/`Cli`/`State` (`ProjectSetupService`, `OrchestratorWorkspaceWarningService`, `AgentTaskService`). Fix: add `IOrchestratorWorkspaceProbe`.
- `ServerGitProcess`, `GitIndexLock`, `LandWorkspace`, `LandVerificationArtifacts` (`AgentTaskLandingProtocol`, `AgentTaskLandService`). Fix: add `ILandGit` next to the existing `ILandingGit` and move the artifact records to Core.
- `RepositoryChildJournalInspector` (`RunnerAlarmCoordinator`, `ExpectationObservationAdapter`), `LocalTaskParkPublisher`, `CheckpointReceiptValidator`.
- Keep `DelegationWorkspaceResolver` (170 lines, used by 171 test files) and `GitProcessGate` **in Application**. Move the resolver's single `git` spawn behind the git port.

**Git → Application (21 refs):**
- `CardFileBoardLookup` (from `GitService`, `LandingGit`, `WorktreeManager`, `GitWorkspaceService`). Fix: add `ICardFileBoardLookup` to Core.
- `CardFilePreCommitSweep`, `CardTaskFileRenderer`, `DelegationReportFormatter`, `RepositoryLeaseWaiters`, `LandVerificationWaits`: pure helpers, move to Core.
- `LandingVerifier` → `AgentTaskLandService`, `LocalTaskParkPublisher` → `BlockedTaskParkingService`, `WorktreeHealthHostedService` → `WorktreeHealthService`: invert with callbacks/ports, or move the hosted service to Application.

**Application → Runners (22 refs):**
- `PhoneHomeRunnerDirectory` is taken as a concrete type by 12 services (`RunnerSlotService`, `RunnerRetireService`, `AgentTaskPipelineStatusService`, `OrchestratorInstructionsSnapshotBuilder`, …), even though it already implements `ISessionRunnerDirectory`. Fix: widen that interface or add `IPhoneHomeRunnerDirectory`.
- `PhoneHomeRunnerClient` (5). Fix: add `IPhoneHomeRunnerClient`.
- Move the inbound drivers (`SessionRunnerEventPump`, `PhoneHomeRecoveryPump`, `RunnerRetireJob`, `RunnerSlotReconcileJob`, `SessionRuntimeActionHostedService`, `HostStatsPollService`) and in-memory state (`SeatOccupancyState`, `RunnerAlarmState`, `AgentTuiMetrics`) **into Application**. They drive application services; they are not adapters.

**Runners → Application (14 refs):** pure types that belong in Core: `RunnerPlatformProblems`, `GrokRulesLaunchValidation`, `RunnerAbsenceEvidenceValidator`, `ProviderContractCatalog`, `CodexCliObservation`, `RunnerRequestIntent` (in `DefaultRunnerRoutingPolicy.cs`), `OrchestratorInstructionsSignal`, and the `ProviderDialogObservationService` exception/types.

**Core → Application (11 refs). These must reach zero before Core can exist:**
- `ChannelReplyPreparation.cs` → 6 channel services
- `DelegationSettings.cs` → `AgentTaskService` and `RunnerRequestIntent`
- `DispatchConcurrencyPolicy` → `DelegationOpenGate`
- `SubscriptionQuotaPolicy` → `SubscriptionUsageSnapshot`
- `PipelineStagesJson` → `PipelineHandoff`

The reverse direction is also wrong today: `Application/Interfaces` and `Exceptions` import `Application.Services` for 14 value types. Move these into Core: `ModelAlias`, `RunnerState`, `SessionStateSnapshot`, `DispatchConcurrencyPolicy`, `SubscriptionQuotaPolicy`, `ChannelReplyPreparation`, `AntiphonDataPaths`, `ClaudeLaunchArgs`, `AgentTaskLandingState`, `PipelineStagesJson`, `PipelineDefinitions`, `WorkspaceReservationLiveness`, `WorktreeCleanupPresentation`, `VerificationReceiptPolicy`.

**Smaller items:**
- `Infrastructure/Data/WorktreeRemovalEvidence.cs` → App/Git/Files. It is application logic in the Data folder, so move it to Application.
- `CardDoneArtifactRoots` → `WorktreeSetAside`.
- `Infrastructure/Resilience/ResilienceReadClients` is HTTP plus DB; split it, putting `DatabaseResilienceExecutor` in Persistence and the read clients in Core as a port.
- `LlmProviderService`, `GitHubMonitorService` and `PhoneHomeRunnerDirectory` take `AppDbContext`. Move the first two to Application, and give the third a port.
- `CodexSubmitConfirmation` moves to Runners. `DataProtectionAgentTuiSecretProtector` moves to Host.
- `AgentExecutor`/`EventEmittingToolWrapper` → `AuditService`: use `IAuditService`.

**Non-server blockers:**
- `DelegationSettings` uses the Agents.Pty enums `PtyBackend` and `PtyInputEncoding`, which would drag the native Porta.Pty package into Core. Move both enums into `Antiphon.SessionRunner.Contracts`.
- `ChannelOutboundDeliveryPump.cs:497,583,591` catches Confluent `ProduceException`/`ErrorCode`. Move `IAntiphonMessagingProducer` and `IAntiphonMessagingConsumer` from `src/Antiphon.Messaging.Client` into `src/Antiphon.Messaging`, with typed exceptions for QueueFull and MsgSizeTooLarge. After that only Host references Messaging.Client/Kafka. Kafka does not need a server project; the one consumer is `Infrastructure/Messaging/InboundUnconsumedEventConsumer.cs`, 97 lines.

Only **19 internal top-level types** cross the new boundaries [V]. Most cross-project access is to public types, plus some internal members. Grant `InternalsVisibleTo` downstream inside the server family as a first step (Persistence→Application/Host, Core→all), then tighten.

### 3. Proposed projects (one layout)

New production projects live under `src/` because the Dockerfile already does `COPY src/ src/`. The host stays at `server/Antiphon.Server.csproj`, so the Dockerfile, `restart-server.ps1` and `rc-project-categories.json` keep working, and its default `**/*.cs` glob shrinks naturally. **Namespaces do not change.** Only assemblies move, so treenode filters, `nightly-class-costs.json` and the `using` lines stay valid.

| Production project | Contents | files | lines | External deps |
|---|---|---:|---:|---|
| `Antiphon.Server.Core` | Domain, Application/{Interfaces,Dtos,Exceptions,Settings}, the 14 value types, the new ports | 529 | 29k | Antiphon.Messaging, SessionRunner.Contracts, M.E.*.Abstractions |
| `Antiphon.Server.Persistence` | AppDbContext, Infrastructure/Data, Migrations, DatabaseResilienceExecutor | 425 | 5.4k + 1.19M generated | EF Core, Npgsql |
| `Antiphon.Server.Application` | all non-adapter services, runner drivers, Orchestration/Supervision hosted services, Files/FileSystem | 611 | 174k | Core, **Persistence (phase 1 only)**, Agents.Pty, Resilience, Hosting.Abstractions |
| `Antiphon.Server.Git` | Infrastructure/Git, GitWorkspaceService, ServerGitProcess, GitIndexLock, OrchestratorWorkspaceLayout, WorkspaceHooks, Verification | 44 | 12.4k | Core, LibGit2Sharp, git/dotnet/docker processes |
| `Antiphon.Server.Runners` | Infrastructure/Agents/{Pty,SessionRunner,Tui} outbound clients and adapters | 32 | 10.6k | Core, Agents.Pty (native), SessionRunner.Contracts |
| `Antiphon.Server.Integrations` | LLM (AgentExecutor, LlmClientFactory, CachingChatClient, Tools) + GitHub/Jira/Linear HTTP | 25 | 3.9k | Core, Microsoft.Extensions.AI/OpenAI/Agents.AI, HttpClient |
| `Antiphon.Server` (host, existing path) | Program.cs, Api, Middleware, Realtime/SignalR EventBus, Security, Hangfire jobs, OS census, DI composition | 84 | 11k | everything + ASP.NET, Hangfire, Serilog, OTel, System.Management, Messaging.Client |

LLM and vendor HTTP are merged into one project because each alone would have about 10 tests.

Reference graph (acyclic once the §2 items are fixed):

```
                     Antiphon.Server (host) ── references all below
        ┌────────────┬──────────┼───────────┬──────────────┐
  Application     Git      Runners    Integrations   Persistence
     │  └──(phase 1)──────────────────────────────────────┘│
     └───────────┴──────────┴───────────┴───────────────── Core
```

**Phase 2** removes `Application → Persistence` by adding repository/unit-of-work ports in Core that Persistence implements. Application then has no EF.

**Mirrored test projects** (sizes from the placement rule: a test file goes to the lowest project that can see every server project it references) [V/I]:

| Test project | [Test] | lines | of which use Postgres |
|---|---:|---:|---:|
| `Antiphon.Server.Application.Tests` | 6,694 | 287k | 4,181 |
| `Antiphon.Tests` (stays; becomes the host/integration tests) | 4,088 | 189k | 2,208 |
| `Antiphon.Tests.Scripts` / `.Checkpoints` (non-server carve-outs from the earlier proposal) | 1,287 / 732 | 49k / 27k | 16 / 0 |
| `Antiphon.Server.Core.Tests` | 520 | 19k | 16 |
| `Antiphon.Server.Git.Tests` | 446 | 14k | 0 |
| `Antiphon.Server.Runners.Tests` | 349 | 16k | 27 |
| `Antiphon.Server.Persistence.Tests` | 279 | 14k | 269 |
| `Antiphon.Server.Integrations.Tests` | 20 | 0.4k | 0 |
| `Antiphon.Tests.Support` (public; TestDbFixture lifecycle, AntiphonWebAppFactory, fakes) | 0 | 35k | — |

Application.Tests is large partly because **256 Application-SUT test files (2,793 tests) also reference adapter or host concretes**:
- Git: 137 files, mostly `LandingGit`, `GitWorkspaceService`, `WorktreeManager`, `RepositoryMutationLease`
- Runners: 83 files (`PhoneHomeRunnerDirectory`, `SessionRunnerHttpClient`)
- Host: 73 files (`EventBus` in 56, `Program` in 20, `ExceptionMiddleware`)

The placement rule sends those that keep doing so to `Antiphon.Tests`. Replacing `EventBus` with the existing `MockEventBus`, and the git/runner concretes with port fakes, moves them back into Application.Tests.

### 4. Benefit: replay of the last 3,000 non-merge commits touching `server/` or `tests/Antiphon.Tests`

The window runs from 2026-10-09 to 2026-11-05. A 6,000-commit replay back to 2026-08-26 gives the same shape.

Today `P-SERVER` (`^server/`, `full: true`) and `main.remainder` (the whole `Antiphon.Tests` csproj) mean **every one of these commits runs all 14,415 tests**.

| Metric (proposed layout, phase 1) | Value |
|---|---|
| Commits that run only a subset | **2,617 / 3,000 = 87%** |
| Commits still forced full | 383 (13%): they edit `TestHelpers` support code or a csproj |
| Tests run per commit | **median 6,694 (46%)**, mean 45%, quartiles 1,287 / 6,694 / 10,782 |
| Test lines in the selected projects | median 287k (44%) |
| Test-only commits (2,007, 67% of all) | median 4,088 |
| Production-touching commits (610 not forced full) | median **10,782 (75%)**. 559 of them touch Application, which brings in Application.Tests + Antiphon.Tests. |
| Commits touching only adapters/host (Git 41, Runners 18, …) | median 4,534 |

Phase 2 (Application off Persistence) does not shrink the selection; the median stays about 46%. What it changes is that the incidental 2,800 Postgres tests stop needing a container, and `Persistence`-only commits (16 in the window) no longer run Application.Tests.

**The ceiling is Application itself.** 174k lines are one project, and an area split inside it (Delegation 56k / Sessions 46k / Board 19k / Channels 14k / misc 40k) is **not viable without more ports**, because the areas reference each other heavily in both directions:
- Delegation↔Sessions: 88/66
- Delegation→misc: 145
- misc↔Sessions: 105/92

### 5. Extraction order (cheapest and least risky first), with what breaks

0. **In place, no csproj change, no build-graph re-record.**
   - Move the 14 value types and the §2 Core→App/Runners→App pure types into Core-namespaced folders.
   - Fix the 11 Core→Application refs.
   - Add the ports: `IGitWorkspace`, `ICardFileBoardLookup`, `IPhoneHomeRunnerDirectory`/`Client`, `IOrchestratorWorkspaceProbe`, `IAuditService`, and the land-git artifacts.
   - Split `Program.cs`'s 446 registrations into `AddAntiphonCore/Persistence/Application/Git/Runners/Integrations()` extension methods.
   - Add a namespace-layering guard test so the graph cannot regress.
   - Breaks: nothing external. Risk is review volume, about 130 reference sites.
1. **`Antiphon.Server.Core` (29k).**
   - Prerequisite: move `PtyBackend`/`PtyInputEncoding` into SessionRunner.Contracts.
   - Breaks: `InternalsVisibleTo` (copy the 4 entries at `server/Antiphon.Server.csproj:13-16`); `Antiphon.sln`; `rc-project-categories.json`.
   - Test projects need `Aliases="global,server"` on **every** server reference. Only 3 test files use `server::`, and an extern alias can span assemblies.
2. **`Antiphon.Server.Persistence`.**
   - Breaks: `MigrationsAssembly("Antiphon.Server")` at `server/Program.cs:135` and in `tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs:155`, `BridgeQueueHarness.cs:126` and `CheckCompactionCrashWorker.cs:147`.
   - `dotnet ef` now needs `--project src/Antiphon.Server.Persistence --startup-project server`. Move `Microsoft.EntityFrameworkCore.Design` along with it.
   - Move `WorktreeRemovalEvidence.cs` to Application first.
   - Biggest local-build win: 1.19M generated lines leave the edit loop.
3. **`Antiphon.Server.Integrations` (3.9k).** Move `LlmProviderService` and `GitHubMonitorService` to Application first. Removes the AI/OpenAI packages from the app layer. Low risk.
4. **`Antiphon.Server.Runners` (10.6k).** First move the 9 driver/state files into Application and the 12 pure types into Core.
   - Hangfire is safe to move: storage is `UseInMemoryStorage` (`server/Program.cs:987`) and jobs are registered with `AddOrUpdate<T>` in `HangfireConfiguration.cs` [V]. No serialized assembly names survive a restart.
   - Breaks: DI for the 6 SessionRunner files that take `AppDbContext` or `PhoneHomeRunnerDirectory` concretely.
5. **`Antiphon.Server.Git` (12.4k).** Do it after the ports land.
   - Breaks: `CardFileBoardLookup` users. `LandVerificationBuildGraph.manifest.txt` is an `EmbeddedResource` whose `LogicalName` is pinned in `server/Antiphon.Server.csproj`; keep the pin wherever `LandVerificationBuildGraph.cs` lives, which is Application.
6. **`Antiphon.Server.Application` (174k): the last, mechanical move of what remains.**
   - Breaks: embedded Bundles. `InstructionBundles.cs:127` uses `ResourcePrefix = "Antiphon.Server.Bundles."`, so set `RootNamespace=Antiphon.Server` or give the resources explicit `LogicalName`s. Same for `AgentPresets`/`InstructionFragments`.
   - `Runtime/claude-remote-control-off.json` content stays in the host.
7. **Tests, in parallel with steps 1–6.**
   - Create `Antiphon.Tests.Support` (public types; extract the nested helpers first).
   - Each test assembly gets a local `AssemblyHooks.cs` with `[Before(Assembly)]`/`[After(Assembly)]` calling `ProductionRunnerGuard`, `PtyBackendEnvGuard`, the `TestDbFixture` lifecycle and `CheckpointTempRootSweep`. These are the 4 hook files today; the `TestWorkerModes` dispatch must stay per assembly.
   - Public Support helpers must not expose internal server types (CS0050/0051).
8. **Land verifier and policies.**
   - `AgentTaskLandService.cs:1957` hard-codes `dotnet run --project tests/Antiphon.Tests`, and `AgentTaskLandingProtocol.cs:323` hard-codes the matching message. Make the verifier run the `LandVerifyFilter` across the selected test projects and require `executed > 0` in total.
   - `test-selection-policy.json`: replace `P-SERVER` with one rule per project. Each rule selects its mirror plus the mirrors of reverse dependents, through `remainderProjects`. Update `catalogHash`.
   - `test-execution-policy.json`: add `suites`/`projects`/`projectDispositions` entries and update `policyHash`.
   - Also update `rc-project-categories.json`; the rows in `linux-test-roster.json` (710) and `live-lanes.json` (61) for moved classes; and `live-lane-manifest.ps1`, which special-cases `Antiphon.Tests`.
   - Each new csproj means re-recording `LandVerificationBuildGraph.{manifest,evaluation}.txt` (expected) and one full `P-BUILD` run.

**Recommendation:** steps 0 → 2 first. They remove 1.19M generated lines from the edit loop, make Core and Persistence real, and make the order of the remaining steps irrelevant. Then do 3–6, then phase 2 (repository ports): start with the 80 EF-light services that touch only 1–2 DbSets, and move the 1,390 "SUT has no EF" tests onto fakes.


---

# Appendix B: test split analysis

## Splitting tests/Antiphon.Tests — proposal

Repo state: `C:\src\antiphon` @ `d465bd4e3`. Read-only research; nothing built or run.
**[V]** = verified from the tree, **[I]** = inferred / estimated, not verified.

---

### 1. What the repo already uses to decide which tests run

The repo has five overlapping classification schemes. None of them is a single "UI / DB / TUI" axis. The closest match to what you described is the `resources` block in the selection policy.

| Scheme | Where | Axis | Relevance to a split |
|---|---|---|---|
| **Selection policy** (CARD-1313) | `tests/test-selection-policy.json`, `scripts/lib/test-selection.ps1` | 111 path rules map changed paths to **sets**. Each set declares `resources`: `postgres`, `docker`, `broker`, `browser`, `desktopUi`, `nativePty`, `interaction` (none/native-probe/tui), `api` (stub/live), `vendorExecutable` (claude/codex/grok) **[V]** | This is the "UI / database / TUI" scheme. Sets that point at Antiphon.Tests: `core` (7 named members), `main.remainder` (whole csproj), **`main.postgres`** (whole csproj, postgres+docker), `main.outbound-broker` (4 classes), `scripts` (15 members), `docker.operations` (2), `release-gate.platform` (1), and `provider.{claude,codex,grok}.{offline,fake-native,tui-stub,tui-live,server-runner}`, which name individual Antiphon.Tests/Agents classes **[V]** |
| Partitions / rules | same file | `P-SERVER` (`^server/`) is **`full: true`**, and so are `P-BUILD` (any csproj/props/sln) and `P-AUTHORITY` (test policy, nightly/release scripts, `tools/Antiphon.Checkpoints`). `P-SHARED` (Agents.Pty/SessionRunner/PtyHost) selects provider sets, `main.remainder` and `e2e.remainder` **[V]** | A server edit runs everything whatever the split. Selection can only narrow for non-server inputs, and today every narrowed rule still includes `main.remainder`, which is all of Antiphon.Tests. |
| Categories | `[Category]` on classes; `TestLaneCategoryGuardTests` requires Unit xor Integration on every class | Integration 1,088 tags, Unit 514, Slow 186, then RealCliStubProxy 10, GitIntegration 6, OptIn 6, Headed 4, Pty 3, E2E 3 **[V]** | Unit lane = `/*/*/*/*[Category=Unit]`. By file: Unit 124k lines, Integration 427k **[V]**. A Unit/Integration split would barely help, because nearly every edit is in Integration code. |
| Execution policy / rc categories | `tests/test-execution-policy.json` (suites `antiphon`, `session-runner`, `pty-host`, `agents-pty`, `messaging`, `client`, `scripts`, `e2e`, `markdown-pdf`, `observability`). `tests/rc-project-categories.json` tags each csproj `claude-runner` / `codex-runner` / `shared-pty-runner` / `git-operations` / `messaging` / `server` / `tooling` / `test-support` **[V]** | One suite per csproj | Each new project becomes a suite, a project disposition and an rc batch. **Precedent:** `Antiphon.Observability.Tests` (CARD-1564) was carved out like this. It links `../Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs` instead of referencing a support library **[V]** |
| Dispatcher areas | `antiphon.areas.json` | Named areas (`delegation`, `session-launch`, `session-lifecycle`, `delivery`, `pty`, `runner`, `herdr`, `schema`, `board`, `tracker`, `channels`, `agents-admin`, `client`, `ops`, `docs`) as path globs over **server** code **[V]** | Good names for an area split of `Application/`. Only `session-launch` names test paths. |
| Parallel limiters | `TestHelpers/ProcessSpawnLimit.cs` (Limit=1), `GitProcessSpawnLimit`, about 9 load limits, `NotInParallel` keys | `ParallelLimiter<ProcessSpawnLimit>` on 794 classes, `GitProcessSpawnLimit` on 119. `NotInParallel("MessageQueue")` 66, `"AgentQueue")` 34, `"Headed")` 16 **[V]** | These are **per-process** guarantees (see §5). |

The land verifier does **not** map changed paths to tests [V]. `LandVerificationPolicy.DecideAsync` only decides between "skip because docs-only" and "build". `AgentTaskLandService.VerifyUnderSlotAsync` then runs `dotnet build --artifacts-path <fresh temp>` (the whole sln, from scratch on every land). If a `LandVerifyFilter` is set, it also runs `dotnet run --project tests/Antiphon.Tests --no-build ... --treenode-filter <filter>`, which must report `executed > 0`. CI (`.github/workflows/ci.yml`) never runs Antiphon.Tests [V].

### 2. Today's shape (measured)

1,929 tracked .cs files, 648.6k lines, 14,405 `[Test]` methods (about 21.9k expanded cases). The `Application/` folder is **flat**: 1,186 files with no subfolders [V].

| Folder | files | k lines | [Test] | uses Postgres fixture | ProcessSpawnLimit | references server/`server::` |
|---|---:|---:|---:|---:|---:|---:|
| Application | 1186 | 455 | 10,043 | 533 | 152 | 1,117 |
| Scripts | 161 | 50 | 1,300 | 1 | 127 | **3** |
| TestHelpers | 224 | 44 | 239 | 67 | 15 | 126 |
| Infrastructure | 110 | 32 | 1,144 | 9 | 30 | 60 |
| Checkpoints | 109 | 27 | 721 | 0 | 46 | **0** |
| Agents + AgentTui | 105 | 31 | 674 | 14 | 26 | 76 |
| ApiKeys/Api/Domain/Migrations/root | 34 | 10 | 284 | — | — | — |

Method: regex scan for `TestDbFixture|DatabaseCollection|TestDbClonePool|PostgreSql`, the `ParallelLimiter<...>` attributes, and `Antiphon.Server`/`server::`. These are approximations [I].

**Cross-file coupling.** I built a type-reference graph from top-level type names declared in each file. BCL-colliding names like `Directory` and `Client` were dropped, so the graph is approximate [I]:
- Application → TestHelpers: 2,382 edges. Application → Agents: 145 (123 of them to `Agents/FakeAgentProtocolAdapter.cs`). Scripts → Application: 83 (72 to `Application/DelegateScriptRunner.cs`). Application → `AgentTui/AgentTuiLaunchResolverTests.cs`: 42.
- **About 150 test files host helpers that other areas use.** Examples [V]: `ApiKeyStoreTests.FakeApiKeyProtector` (used by AgentTui), `AgentControlServiceIntegrationTests.BuildHarness/NewTempRoot/MarkSessionEndedAsync` (used by Agents/ProviderEndpointCanaryHarness and AgentSessionBackendTests), and `ProcessSpawnLimitTests`, whose `typeof(...)` list spans Application, Infrastructure, Scripts, Agents and AgentTui. Every option has to extract these nested helpers first. They are the main migration cost, roughly 150–190 files touched per option.
- **Checkpoints is effectively server-free** [V]. Its closure reaches server-using files only through string literals (`CheckpointImportTests` asserts manifest text "LandingGitTests"). It needs 15 helper files directly: `ProcessSpawnLimit`, `GitFixtureCleanup`, `DirectoryLink`, `NativeHardLink`, `ControlledTimeProvider`, `FixtureFileDiagnostics`, `LoadTimeoutBounds`, `Infrastructure/HostCleanupCheckpointFixture`, `Infrastructure/DockerStackDocuments`, and a few Scripts test types.
- **Scripts is almost server-free.** Only 3 of its files touch server code: `DelegateScriptBindDiagnosisTests`, `LiveLaneEvidenceTests`, `LiveLaneIsolationTests` [V]. Its type closure still reaches 107 external files, 56 of them server-using, mostly through `DelegateScriptRunner`, `TestDbFixture` (only `MeasureSessionStateCacheScriptTests`) and `ProductionRunnerGuard` (constants only) [V].

**Edit churn** (git log since 2026-09-05; 5,095 commits touch Antiphon.Tests .cs files). Under the recommended layout, **79% of commits touch exactly one project**, 16% touch two, and 14% touch the shared support library [V, measured against my file→project map].

---

### 3. Options

Sizes come from the scan and the closure analysis. A helper used by exactly one project goes into that project. A helper used by two or more goes to Support. All sizes are [I] ±10%, because the file assignment is heuristic: `Application/` is split by file-name prefix.

#### Option 1 — Resource lanes (a literal mirror of the selection-policy `resources`)

| Project | Contents | k lines | [Test] | Selection set it serves |
|---|---|---:|---:|---|
| `Antiphon.Tests.Db` | every class using TestDbFixture/clone pool | 270 | 5,127 | `main.postgres` (postgres+docker) |
| `Antiphon.Tests` (core) | Unit and in-process/git/web tests without Postgres | 221 | 6,175 | `core`, `main.remainder` |
| `Antiphon.Tests.Tui` | Agents/, AgentTui/, Application `Grok*/Codex*/Claude*/Pty*/Herdr*/Terminal*/Composer*/Effort*/RealCli*` | 49 | 1,082 | `provider.*`, nativePty/tui |
| `Antiphon.Tests.Scripts` | Scripts/ | 46 | 1,300 | `scripts`, `docker.operations` |
| `Antiphon.Tests.Checkpoints` | Checkpoints/ | 25 | 721 | P-AUTHORITY / tooling |
| `Antiphon.Tests.Support` | shared helpers | 37 | 0 | — |

- Benefit: mean **273k** lines recompiled per commit (42% of today), median 221k. The largest project is 270k.
- Pros: matches the "UI / database / TUI" vocabulary exactly. Only one project needs Postgres, so only one container per run. `main.postgres` / `main.remainder` get their own csproj.
- Cons: Db and core are both still a third to two-fifths of the code. The same feature (for example land) is split across Db and core, with 89 cross-reference files between them. Membership is unstable: a class that starts using `TestDbFixture` has to move projects.

#### Option 2 — Area projects (`antiphon.areas.json` + rc categories)

| Project | Application prefixes (plus folders) | k lines | [Test] |
|---|---|---:|---:|
| `.Delegation` | `AgentTask*, Task*, Land*, Delegat*, Dispatch*, Worktree*, Review*, Verification*, Settlement*, Repair*, Receipt*, Blocked*, Check*, Capacity*, Specialist*, Complexity*, Standing*, C####*` | 203 | 4,247 |
| `.Sessions` | `AgentSession*, Session*, Runner*, Phone*, Herdr*, Grok*, Codex*, Claude*, Remote*, Boot*, Transcript*, Seat*, Orchestrator*, Expectation*, …` + Agents/ + AgentTui/ | 149 | 3,182 |
| `.Board` | `Card*, Board*, Tracker*, Project*, Workflow*, Pipeline*, Schedule*, Output*, Role*` | 43 | 1,008 |
| `.Channels` | `Channel*, Chat*, Post*, Routing*, Attention*` | 40 | 927 |
| `.Infrastructure` | Infrastructure/ | 31 | 1,144 |
| `Antiphon.Tests` (core) | remaining 187 unmatched Application files (56k), ApiKeys, Api, Domain, Migrations, root guards | 76 | 1,876 |
| `.Scripts`, `.Checkpoints`, `.Support` | as Option 1 | 46 / 25 / 36 | |

- Benefit: mean **201k** per commit (31%), median 149k. The largest project is 203k.
- Pros: best edit locality for feature work. Aligns with the dispatcher `-Scope` areas and with the rc categories (`messaging` → Channels, `claude/codex-runner` → Sessions, `git-operations` → Delegation/Infrastructure).
- Cons: does not follow the selection policy's resource axis. TUI/native-PTY tests share a project with in-process session tests. Every DB area project starts its own Postgres container. 187 files need hand classification.

#### Option 3 — Hybrid: resource lanes outside the server, areas inside it (recommended)

A test gets its own project when it already has its own selection set or suite and does not need the server: Checkpoints, Scripts, and the TUI/provider lane. The server-test bulk is split by area. **Postgres stays a fixture in Support, not a project boundary**; `main.postgres` becomes a category/attribute filter run inside each area project.

| Project | Contents | k lines | [Test] | ProcessSpawnLimit classes | Selection hook |
|---|---|---:|---:|---:|---|
| `Antiphon.Tests.Checkpoints` | Checkpoints/ | 25 | 721 | 46 | P-AUTHORITY tooling; `rc: tooling` |
| `Antiphon.Tests.Scripts` | Scripts/ (keep `MeasureSessionStateCacheScriptTests` in a server project) | 46 | 1,300 | 127 | `scripts`, `docker.operations` sets; `scripts` suite |
| `Antiphon.Tests.Tui` | Agents/, AgentTui/, provider-named Application tests | 49 | 1,082 | 40 | `provider.*` sets, P-SHARED; rc `claude/codex/shared-pty-runner` |
| `Antiphon.Tests.Infrastructure` | Infrastructure/ (git, landing infra, host cleanup, resilience) | 31 | 1,144 | 30 (+18 git) | rc `git-operations` |
| `Antiphon.Tests.Delegation` | AgentTask/Land/Dispatch/Worktree/… (Land subset is 51k and could split out later) | 203 | 4,247 | ~73 | areas `delegation` |
| `Antiphon.Tests.Sessions` | session/runner/phone-home/boot/transcript/expectation (non-TUI) | 99 | 2,100 | — | areas `session-*`, `delivery`, `herdr` |
| `Antiphon.Tests.Board` (Board + Channels) | Card/Board/Tracker/Workflow + Channel/Chat/Post/Routing | 83 | 1,935 | — | areas `board`, `tracker`, `channels`; `main.outbound-broker` |
| `Antiphon.Tests` (core, keeps the name) | unmatched Application files, ApiKeys, Api, Domain, Migrations, root guards | 76 | 1,876 | — | `core`, `main.remainder` |
| `Antiphon.Tests.Support` | shared helpers (§4) | 36 | 0 | — | — |

- Benefit: mean **194k** lines per commit (30% of today), **median 99k (15%)**, p75 203k. The 14% of commits that edit Support still rebuild everything. Per-project typical edit [I], assuming csc time is roughly linear in lines (about 80 s / 4.6 GB for 646k) plus a few seconds of fixed overhead per project:
  - Checkpoints: about 25k lines, ~5–8 s
  - Scripts / Tui: about 50k lines, ~8–10 s
  - Sessions / Board: 80–100k lines, ~13–16 s, ~0.7–0.9 GB
  - Delegation: 203k lines, ~28 s, ~1.5 GB
- Server production edits: every server-referencing project rebuilds. Checkpoints never does. Scripts doesn't either if the three server-touching files are moved out and `ProductionRunnerGuard`'s constants are put in a server-free file. About 575k lines.
- Why recommended:
  1. It keeps the selection axis wherever the policy already has a dedicated set (Scripts, Checkpoints, provider/TUI) and uses areas where the policy only has `main.remainder`.
  2. It gives the best median.
  3. Membership is stable: a test does not change project because it starts using Postgres.
  4. Both pure carve-outs (Checkpoints, Scripts) are needed by every option anyway, so the work can start without committing to the rest of the layout.

---

### 4. Shared support library

**What moves** (Option 3): about 173 files / 36k lines.
- 120 non-test files from TestHelpers/. The other 104 TestHelpers files are tests or single-area helpers: 53 go to core, 17 to Delegation, 10 to Sessions, 9 to Tui, 8 to Board.
- About 53 helper files currently in area folders, because more than one project uses them: 15 from Scripts (`ScriptHarnessProcess*`, `C590Harness`, …), 13 from Agents (`FakeAgentProtocolAdapter`, `RealCliStubGate`, `ScriptedClaudeInputGuard`, `ScriptedCodexRunnerClient`, …), 11 from Application (`DelegateScriptRunner`, `ExpectationTestWorld`, `StandingRecoveryFixture`, `RunnerSeatReleaseFixture`, …), 10 from Checkpoints (`CheckpointTestSupport`, `CheckpointFixtureProcess`, …), 4 from Infrastructure (`HostCleanupFixture`, `ResilienceTestSupport`, `HostCleanupCheckpointFixture`, `DockerStackDocuments`).
- Nested helpers hosted inside about 150 test classes are extracted into top-level helper files.
- The links that `Antiphon.Tests.csproj` makes today to `tests/Shared/*.cs` (TestTiming, TestTrace, ProgressAwareWait, TestClassificationMetadata, LiveClaude/*, NativeLinux*) and to sibling test projects (FakeHerdrServer, FakeSlackServer, KestrelLoopback, LocalHttpRunner, PhoneHomeTestWebSocket, …) are made once, in Support.

**Accessibility.** Support has about 246 internal and 41 public top-level types [V, regex count]. Recommendation: **make nothing public.** Instead:
- Give Support `<InternalsVisibleTo Include="Antiphon.Tests.*"/>`, one entry per test project.
- Add `InternalsVisibleTo Antiphon.Tests.Support` (plus each new test project) to every assembly that grants Antiphon.Tests today: `server/Antiphon.Server.csproj:13`, `src/Antiphon.Agents.Pty`, `src/Antiphon.SessionRunner`, `src/Antiphon.Resilience`, `src/Antiphon.NightlyWatchdog`, `tools/Antiphon.LandGraphAudit.Core` [V].

This sidesteps CS0050/CS0051, which would hit any public helper that exposes server internals [I].

**Optional second tier.** About 86 Support files (13k lines) never touch server code [V]. Putting them in `Antiphon.Tests.Support.Core` (server-free) keeps Checkpoints, and later Scripts, off the server reference.

**Area-specific helpers that should not go to Support**: `TestWorkerModes`. It registers 8 self-relaunch workers (`LandQueueRaceWorker`, `LandProtocolCrashWorker`, `LandingRemovalCrashWorker`, `WorktreeRetirementCrashWorker`, `RepositoryJournalCrashWorker`, `PostLandMutationDeliveryWorker`, `CheckCompactionCrashWorker`, `CodexStartupDeliveryWorker`) [V]. The test exe relaunches itself, so each worker must live in the same assembly as the test that spawns it. Make the registry per-project, with the Land/Worktree workers going to Delegation.

### 5. Dependency graph

```
server, Agents.Pty, SessionRunner, Resilience, NightlyWatchdog, LandGraphAudit.Core, Checkpoints tool, …
        │
Antiphon.Tests.Support.Core  (server-free; Shared/*.cs links, ProcessSpawnLimit, GitFixtureCleanup, DirectoryLink…)
        │
Antiphon.Tests.Support  (server alias "global,server", TestDbFixture, AntiphonWebAppFactory, FakeAgentProtocolAdapter…)
        │
 ┌──────┼──────────┬──────────┬──────────┬────────┬────────┐
Core  Delegation  Sessions   Board   Infrastructure  Tui   Scripts*   Checkpoints→Support.Core only
```

\* Scripts can drop to Support.Core once the three server-touching files and one DB test are moved out.

Rules:
- Test projects reference only Support projects, never each other.
- Each test project carries only the production `ProjectReference`s it needs.
- The staged-helper `ReferenceOutputAssembly=false` references (FakeClaude/FakeGrok, Checkpoints.*Host, ScriptHarnessHost, CustodyTestChild, …) go only to the projects that launch them. Inferred owners [I]:
  - Checkpoints: the checkpoint hosts and GitFixture
  - Scripts: ScriptHarnessHost and NativeLinuxPolicy.Probe
  - Tui/Sessions: the fakes and the PtyHost apphost

### 6. What breaks or needs updating

1. **Per-process limiters.**
   - `ProcessSpawnLimit` Limit=1 guarantees one process-spawning test per assembly run, not per machine. Its doc comment already says it "cannot cap a concurrent Antiphon.Agents.Pty.Tests run" [V].
   - After the split, the 794 limited classes are spread over about 7 processes.
   - Runners must execute the test projects **sequentially**, or one at a time while any process-spawning project is running. The nightly native group is already serial per suite [I].
   - The same applies to the `NotInParallel` keys whose classes land in different projects: "MessageQueue" (66) and "AgentQueue" (34) would span Delegation, Sessions and Board.
   - `GitProcessSpawnLimit`'s 2–4 width also becomes per-process.
2. **Guards that assume one assembly.**
   - `ProcessSpawnLimitTests` (its `typeof` lists span all folders) has to be split per project or moved to a reflection census over all test assemblies.
   - `TestLaneCategoryGuardTests` (scans `tests/Antiphon.Tests` on disk) and the `TestClassificationGuardTests` / `slow-tests-allowlist.txt` registry (`AssertRegistryMatches(assembly, path)`, 403 lines) each need one copy per project. Every existing suite already links `Shared/TestClassificationGuardTests.cs`, so there is precedent.
   - 46 files use `typeof(X).Assembly` / `GetTypes()` censuses, and 44 files pin `"tests/Antiphon.Tests/…"` paths (e.g. `LandingProtocolHarnessTests`, `LandVerificationBuildSlotTests`, `CheckpointImportTests`) [V counts].
3. **`[Before(Assembly)]` hooks** [V]: `ProductionRunnerGuard`, `PtyBackendEnvGuard`, `TestDbFixture` (also `[After(Assembly)]` and the worker dispatch), and `CheckpointTempRootSweep` (2 hooks).
   - Assembly hooks declared in a library assembly that has no tests may not run for the referencing test assembly [I — verify against TUnit 1.44 reflection mode].
   - Safer: Support exposes static methods, and each project compiles a tiny linked `tests/Shared/AssemblyHooks.cs` that calls them.
   - Every project needs ProductionRunnerGuard. PtyBackendEnvGuard is needed in Tui, Sessions and Delegation. TestDbFixture is needed wherever there are DB tests.
   - Each DB project lazily starts its own Postgres container [I, CARD-0476 lazy fixture]. That is another reason to run the projects serially.
4. **Land verifier.**
   - `AgentTaskLandService.cs:1957` hard-codes `dotnet run --project tests/Antiphon.Tests`. `AgentTaskLandingProtocol.cs:323` hard-codes the message text.
   - With a split, `LandVerifyFilter` needs a project, or the verifier runs the filter against each project and requires `executed > 0` in total. Otherwise a filter for a moved class yields `no_confirmed_passing_tests`.
   - Treenode filters `/*/Namespace/Class/*` keep working if **namespaces are not changed** (keep `Antiphon.Tests.Application` and so on). `nightly-class-costs.json` is keyed by full class name, so it stays valid [V].
5. **Land build-graph digest.**
   - Every new csproj is a definition file: `LandVerificationBuildGraph.manifest.txt` (count 69, digest `D5B302…`) must be regenerated with `scripts/audit-land-verification-graph.ps1`.
   - `LandVerificationBuildGraph.pins.txt` pins Targets per csproj (`CopyStagedHelpers`, `CopyOwnedPtyHostAppHost`, `CopyOwnedHerdrFakeAppHosts`, `KeepNativeLinuxPolicyFixturesInMainAssembly`). Moving these into a shared `tests/Shared/StagedHelpers.targets` gives one pin instead of N.
   - `scripts/audit-land-verification-precheck.ps1` allow-lists properties. `OutputType`, `IsTestProject`, `EnableTUnitSourceGeneration`, `EnableMicrosoftTestingPlatformRunner` and `InternalsVisibleTo` are already allowed [V].
   - Its `$AllowedDocumentPatterns` names `tests/Antiphon.Tests/Agents/Fixtures/**` and `tests/Antiphon.Tests/Fixtures/NativeLinuxPolicy/*.cs.txt`, which change if fixtures move.
   - Any csproj/sln edit trips `P-BUILD` (full selection) once, which is expected.
6. **Policy and fixture files.**
   - `test-execution-policy.json`: `suites`, `projects`, `projectDispositions`, `defaultSuites`, `profiles.*.requiredSuites`, `policyHash`.
   - `test-selection-policy.json`: 29 member `project` paths, set `remainderProjects`, `catalogHash`.
   - `rc-project-categories.json`: `projects`, `batches`.
   - `linux-test-roster.json`: 710 `"assembly": "Antiphon.Tests"` rows.
   - `tests/live-lanes.json`: 61 `"project": "Antiphon.Tests"` rows. `scripts/lib/live-lane-manifest.ps1` special-cases `$Project -eq 'Antiphon.Tests'`.
   - `scripts/lib/nightly-tests-impl.ps1`: `$NightlyNativeGroups`.
   - Other scripts: `scripts/run-tests-watched.ps1`, `scripts/lib/checkpoint-usage.ps1` (scans `tests/Antiphon.Tests/Checkpoints`, namespace `Antiphon.Tests.Checkpoints`), `test-rc-platform.ps1` (68 refs), `test-nightly-*.ps1`.
   - `tests/fixtures/live-lanes/full-discovery-before.json` has 20,787 project refs. It looks like a frozen "before" snapshot and can probably stay [I].
   - Docs: `TESTING.md`, `docs/testing-and-build.md` (Unit-lane recipe, CARD-1573 shards).
   - `Antiphon.sln`: add every project, because the land runs `dotnet build` on the sln.
7. **Full builds.** Total compile work is unchanged or slightly higher (N csc startups plus reference loading). MSBuild may compile several test projects at once, so cap `-maxcpucount` locally (the land build slot already passes `-maxcpucount:<slot>`). Otherwise peak memory can still reach several GB [I].

### 7. Can land/CI build and run only the affected project?

Not without changes:
- The land does a fresh-artifacts full-sln build on every land, so splitting gives the land roughly zero compile saving.
- To benefit, the verifier would need a path → project map, so it can run `dotnet build tests/Antiphon.Tests.X` plus `run` for the selected projects. The selection policy is the natural place: give each set `remainderProjects: [the new csproj]`.
- Server edits stay `full: true`, so they always build all server-referencing projects.
- Real wins for the land:
  - `scripts/**` / P-DOCKER inputs could build only `.Scripts`.
  - `tools/Antiphon.Checkpoints` (currently P-AUTHORITY full) could build only `.Checkpoints`, if P-AUTHORITY were narrowed.
  - P-SHARED (Agents.Pty/SessionRunner/PtyHost) could drop the area projects that don't reference those assemblies. Most do, because 447 Application files use them [V], so the gain is small.
- The local dev loop (edit, rebuild one project, filter-run) gets the full benefit right away.

### 8. Migration order and risk

| Step | Work | Risk |
|---|---|---|
| 0 | No project changes yet. Extract nested helpers out of about 150 test classes. Make the guards (ProcessSpawnLimitTests, TestLaneCategoryGuard, classification registry) work per assembly. Move the staged-helper targets into `tests/Shared/*.targets`. Add a per-project hook file. | Low. Lands as ordinary commits inside one assembly. |
| 1 | **Carve out `Antiphon.Tests.Checkpoints`.** Server-free, 25k lines, 721 tests. Link its ~15 helpers (Observability.Tests precedent) or create Support.Core. Its rows go into the policies, the digest is regenerated, and the CheckpointTempRootSweep hook moves with it. | Low–medium. Proves the whole policy/digest/IVT/nightly pipeline on the smallest piece. |
| 2 | `Antiphon.Tests.Scripts` (46k). Move the three server-touching tests and `MeasureSessionStateCacheScriptTests` out first. | Medium. Many pwsh harness tests, 127 strict-lane classes, and the serial-run rule from §6.1 starts to matter. |
| 3 | Create `Antiphon.Tests.Support` (server tier), with IVT entries in 6 production csprojs. | Medium. This is where most of the extraction lands. |
| 4 | `Antiphon.Tests.Tui` (49k). Lines up with the provider sets and live-lane rows. | Medium. PTY hooks, fake-provider staging, live-lanes.json. |
| 5 | `Antiphon.Tests.Infrastructure` (31k). | Low–medium. |
| 6 | `Antiphon.Tests.Delegation` (203k, the most-edited area: 1,551 of 5,095 commits). The land verifier and crash-worker registry follow it. Later, split Land (51k) from Dispatch/Task (152k) if needed. | High. Land-verifier command, TestWorkerModes, the MessageQueue/AgentQueue NotInParallel keys. |
| 7 | Split the rest into Sessions (99k), Board+Channels (83k), core (76k). | Medium. |

Lower-risk variant for steps 1–2: leave the files where they are. The new csproj compiles `../Antiphon.Tests/Checkpoints/**/*.cs`, and Antiphon.Tests adds `<Compile Remove="Checkpoints/**"/>`. The 44 path-pinned tests and git history are untouched. Move the files physically later.

### 9. Recommendation

**Option 3 (hybrid), starting with Checkpoints, then Scripts.**
- Median recompile per commit drops from 646k to about 99k lines; mean is about 194k. The largest project is 203k, versus 270k for the literal resource split.
- It keeps the selection-policy lanes where the policy really has them (scripts, checkpoint tooling, provider/TUI).
- It avoids a Postgres project boundary that would churn membership and split features.
- Its first two steps are low-risk and needed by every option, so the decision on how to split `Application/` can wait until step 6.
