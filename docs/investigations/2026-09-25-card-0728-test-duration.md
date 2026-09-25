# CARD-0728 — where test and build time goes

Confirmed. On the server2 Linux runner, a Unit lane is about 2.8 minutes of test-host time and a cold `Antiphon.Tests` build is 3 minutes 44 seconds. The 240-minute delegate ceiling is the sum of many checkpoint rows, repeated Postgres and build-slot startup, a few serial classes, and reruns. It is not one slow Unit execution.

No production or test code was changed. Not done, noted: the fixes below are unbuilt.

Filed from these measurements: CARD-0732, CARD-0733, CARD-0734, CARD-0735, CARD-0736. Build-slot deployment stays on CARD-0589. One-process checkpoint execution stays on CARD-0723.

## What was measured

896 `.trx` files under `/work/worktrees/*/.antiphon/` (609 MB), parsed for `UnitTestResult/@duration` joined to `TestMethod/@className`. Duration is the test method. Class totals below are the sum of per-method medians unless a column says the sum inside one run. Wall clock is `Times/@start` to `Times/@finish`.

One cold build and one no-op rebuild of `tests/Antiphon.Tests` into `OutputPath=bin-c728/`, through `scripts/build-slot.ps1`, with `-nodeReuse:false --disable-build-servers -maxcpucount:4 -p:UseAppHost=false -clp:PerformanceSummary`. SDK 10.0.401. Host: 24 cores, `MemAvailable` about 95 GB of 126 GB. Load was roughly 3–15 during the builds.

Two idle timings of one shimmed `scripts/run-checkpoint.ps1 -NoSlot` (dotnet replaced by a shim that exits 0).

No full lane was rerun. The Unit figures are the 95 green checkpoint runs already on disk (executed count at least 2,500, 0 failed).

## 1. Where the time goes

### Unit lane (every Code and Review that follows the default recipe)

95 green runs, about 3,000 tests:

| | p50 | p90 | max |
|---|---:|---:|---:|
| Wall | 170s | 214s | 406s |
| Before the first test | 77s | 104s | 151s |
| After the last test | 1.4s | 2.1s | 7s |
| Sum of test durations | 569s | 675s | 904s |

Sum/wall median is 3.4, so the tests overlap. The 77s lead does not. A sample run (`task-24354988/.antiphon/c679-review/Unit-0cc8db56/Unit-20260925-004451-e2b3/run.trx`, wall 167s, lead 96s) contains `testcontainers.org` output for creating and starting a Postgres container.

CARD-0710 CP-12 on this runner (`task-f32006d5/.antiphon/c710-checkpoints/server2/unit-final/`):

| Attempt | TRX wall | Tests | Failed | Directory stamp to TRX mtime |
|---|---:|---:|---:|---|
| `CP-12-20260925-204559-dd3d` | 152s | 3154 | 7 | 20:45:59Z to 20:59:24Z (13.4 min) |
| `CP-12-20260925-212112-33ab` | 171s | 3154 | 0 | 21:21:12Z to 21:25:55Z (4.7 min) |

The card's "about 43 minutes" matches the span from the first CP-12 stamp through the green finish (40 minutes), which includes the failed attempt, the gap, and `preflight-cap`. One green Unit execution is 2.8 minutes of test host. The rest of a cold row is the build and the build-slot wait below.

### Slowest classes by sum of per-method medians

This is the cost of running the class once if its tests cannot overlap. Where the class is parallel, the number is core-seconds and the wall is closer to the slowest method plus startup.

| # | Sum | Per test | Methods | Samples | Class |
|---:|---:|---:|---:|---:|---|
| 1 | 851s | 40.5s | 21 | 344 | `RunCheckpointScriptTests` |
| 2 | 270s | 2.3s | 119 | 2902 | `SessionMessageQueueDeliveryVerificationTests` |
| 3 | 236s | 5.4s | 44 | 186 | `WorktreeRetirementRaceTests` |
| 4 | 232s | 6.3s | 37 | 540 | `AgentSessionRuntimeTests` |
| 5 | 225s | 11.2s | 20 | 26 | `DelegationCapabilityTests` |
| 6 | 164s | 3.7s | 44 | 700 | `WorktreeGuardedCleanupTests` |
| 7 | 161s | 8.5s | 19 | 21 | `AgentTaskLandApprovalRequestTests` |
| 8 | 139s | 7.0s | 20 | 80 | `VerificationRoundDeliveryTests` |
| 9 | 136s | 3.0s | 46 | 549 | `PostLandMutationDeliveryTests` |
| 10 | 128s | 9.2s | 14 | 14 | `OrchestratorStateProjectionTests` |
| 11 | 112s | 28.0s | 4 | 44 | `TranscriptHotPathQueryTests` |
| 12 | 106s | 10.6s | 10 | 10 | `AgentTaskLandApprovalPersistenceTests` |
| 13 | 100s | 4.2s | 24 | 24 | `LaunchEnvLayersIntegrationTests` |
| 14 | 98s | 5.5s | 18 | 133 | `CheckNoteDeliveryHandoffTests` |
| 15 | 93s | 1.6s | 57 | 122 | `AgentSessionLaunchFailureTests` |
| 16 | 92s | 10.3s | 9 | 9 | `ComplexityDispatcherTests` |
| 17 | 92s | 8.3s | 11 | 228 | `RemoteWorkspacePreparerTests` |
| 18 | 90s | 6.4s | 14 | 14 | `ComplexityCreateTests` |
| 19 | 87s | 2.6s | 34 | 440 | `ChannelBridgeTests` |
| 20 | 85s | 12.1s | 7 | 70 | `PhoneHomeTaskDispatchProjectionTests` |

`WorktreeGuardedCleanupTests` is in this list at 3.7s per test (44 methods, about 16 runs, 700 results). The card's 25s per test is higher than this median. The class is real git plus a cloned database per test (below), serialized by `ParallelLimiter<ProcessSpawnLimit>`, so 164s is close to its wall when it is scheduled alone. A split between fixture setup and the removal under test was not measured, so no card was filed for it.

### Slowest classes by per-test median

Classes with at least two methods, or one method at 5s or more. Same median definition.

| # | Per test | Sum | Methods | Class |
|---:|---:|---:|---:|---|
| 1 | 40.5s | 851s | 21 | `RunCheckpointScriptTests` |
| 2 | 28.0s | 112s | 4 | `TranscriptHotPathQueryTests` |
| 3 | 14.4s | 14s | 1 | `DispatcherRemotePrepStarvationTests` |
| 4 | 12.7s | 13s | 1 | `AgentTaskLandAdmissionControlledTests` |
| 5 | 12.1s | 85s | 7 | `PhoneHomeTaskDispatchProjectionTests` |
| 6 | 11.2s | 225s | 20 | `DelegationCapabilityTests` |
| 7 | 10.6s | 106s | 10 | `AgentTaskLandApprovalPersistenceTests` |
| 8 | 10.3s | 92s | 9 | `ComplexityDispatcherTests` |
| 9 | 9.5s | 66s | 7 | `PhoneHomeLaunchTransportTests` |
| 10 | 9.2s | 128s | 14 | `OrchestratorStateProjectionTests` |
| 11 | 8.5s | 161s | 19 | `AgentTaskLandApprovalRequestTests` |
| 12 | 8.4s | 34s | 4 | `OrchestratorInvestigationSweepTests` |
| 13 | 8.3s | 92s | 11 | `RemoteWorkspacePreparerTests` |
| 14 | 7.9s | 40s | 5 | `ContinuationSettlementTests` |
| 15 | 7.6s | 53s | 7 | `BuildSlotScriptTests` |
| 16 | 7.4s | 7s | 1 | `AgentSessionRuntimeActivityTests` |
| 17 | 7.3s | 15s | 2 | `BuildSlotEndToEndTests` (SessionRunner.Tests) |
| 18 | 7.2s | 36s | 5 | `ClaudeCredentialProbeDispatcherTests` |
| 19 | 7.0s | 139s | 20 | `VerificationRoundDeliveryTests` |
| 20 | 6.9s | 28s | 4 | `ExpectationHoldReleaseEndpointTests` |

Slowest single methods by median include `RunCheckpointScriptTests.C585_QuotedExpect` at 120s (17 samples, max 300s), `C585_RosterMiss` at 99s, and `C589_SlotUnreachable` at 75s. The 300s values sit on `ScriptHarness`'s 300s budget.

### Inside the Unit lane (median sum within a green run)

These are the classes the Category=Unit filter actually runs. Sum is core-seconds inside the run. Only `TestClassificationPolicyTests` in this list also holds `ParallelLimiter<ProcessSpawnLimit>`, so its 66s is serial. The presentation and adapter classes are parallel: their wall contribution is about the slowest method (roughly 5–9s), while they burn a lot of CPU.

| # | Median sum | Max sum | Class |
|---:|---:|---:|---|
| 1 | 160s | 298s | `WorktreeCleanupPresentationTests` |
| 2 | 66s | 140s | `TestClassificationPolicyTests` |
| 3 | 60s | 86s | `AgentTuiSecretProtectorTests` |
| 4 | 33s | 41s | `RunnerClaudeAdapterEffortPromptTests` |
| 5 | 28s | 55s | `SessionDeliveryProfileTests` |
| 6 | 18s | 71s | `WorktreeIgnoredContentClassifierTests` (45 of 115 runs) |
| 7 | 15s | 22s | `RunnerCodexAdapterSubmitConfirmTests` |
| 8 | 12s | 17s | `RunnerCodexAdapterTurnCompleteTests` |
| 9 | 11s | 16s | `RunnerClaudeAdapterTrustPromptTests` |
| 10 | 9s | 21s | `LogRetentionTests` |

`WorktreeCleanupPresentationTests` is pure in-memory JSON (`Capture` builds 65 observations). Argument rows on `C443_D9_DiagnosticsSurviveEitherEnvelopeExtreme` are the bulk of the 160s. That is CPU load during the lane, not 160s of wall.

## 2. Why the slow ones are slow

### Postgres before any Unit test — CARD-0732

`TestDbFixture.InitializeAsync` (`tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs` lines 27–44) is `[Before(Assembly)]`. It awaits `EnsureReadyAsync` when `SharedStoreWarmup.SelectionNeedsSharedStore` is true. Unit classes that reach the store:

- `SessionDeliveryProfileTests` (`[Category("Unit")]`, calls `CreateDbContextOptions`). The testing guide already says this class keeps the Unit lane off a zero-database benchmark.
- `CommitOnSettleMigrationTests` (`[Category("Unit")]`, calls `CreateIsolatedSchemaAsync`).

The 77s lead is that startup. Tail after the last test is 1.4s.

### A real `dotnet build` inside the Unit lane — CARD-0733

`TestClassificationPolicyTests.RunProbeAsync` writes a throwaway TUnit project and runs `dotnet build Probe.csproj`, then the probe (`TestClassificationPolicyTests.cs` around lines 334–379). The class is Category Unit and `[ParallelLimiter<ProcessSpawnLimit>]`. `ProcessSpawnLimit.Limit` is 1, assembly-wide, so these builds are a serial chain on the Unit critical path.

Six compiles per Unit run: `C487_G065`, `C487_G066`, and the two-argument methods `C487_G067` and `C487_G069`, each about 8s median. Class median sum 66s. Six other Unit files also carry the limiter (`GitIndexLockTests`, `MarkdownPdfRendererTests`, `CodexRunnerImageContractTests`, `ProcessSpawnLimitTests`, `RemoteScriptContractTests`, and this one). Their totals are a few seconds. 201 files in the project carry the limiter; almost all of those are Integration and are outside the Unit filter.

### Checkpoint script tests spawn pwsh per assertion — CARD-0734

`RunCheckpointScriptTests` is Integration and on the process limiter. `ScriptHarness.RunHarnessCaseAsync` starts `pwsh -File scripts/test-run-checkpoint.ps1`. `Invoke-C585Runner` starts another `pwsh -File scripts/run-checkpoint.ps1` per assertion. `C585_QuotedExpect` does that six times (`scripts/test-run-checkpoint.ps1` around lines 208–253). The dotnet stand-in is a shim; these tests do not compile the product.

One idle `-NoSlot` invocation with an instant shim took 43s, and a second took 61s. Bare `pwsh -NoProfile -Command` took 0.70s. The class median total of 14.2 minutes matches roughly one slow invocation per method, and the historical rows that reached 46 minutes are methods sitting on the 300s harness budget (`ScriptHarness.cs` line 29).

### MessageQueue is one serial lane — CARD-0735

Forty classes in `tests/Antiphon.Tests` use `[NotInParallel("MessageQueue")]`. `SessionMessageQueueDeliveryVerificationTests` is 119 methods on that key. Each test calls `BridgeQueueHarness.CreateAsync` (service graph, agent insert, session insert on the shared database). The harness uses real time: `EvidenceTimeoutSeconds = 1`, `PollIntervalMs = 50`, `TranscriptConfirmTimeoutSeconds = 3`, `PostFailureConfirmGraceSeconds = 3` (`BridgeQueueHarness.cs` lines 106–118). Median 2.3s, sum 270s. `VerificationRoundDeliveryTests` (139s), `CheckNoteDeliveryHandoffTests` (98s), and `ChannelBridgeTests` (87s) sit on the same key, so a combined filter adds them.

### Transcript hot path reseeds 377,000 rows — CARD-0736

`TranscriptHotPathQueryTests` is `[NotInParallel("TranscriptHotPathQueries")]`. Each of the four methods calls `TranscriptHotPathFixture.CreateAsync(large: true)`, which clones a database and inserts 377,000 rows plus `ANALYZE` (`TranscriptHotPathFixture.cs` lines 46–118, `CommandTimeout` 120). `CreateIsolatedSchemaAsync` takes a process-wide clone lock (`TestDbFixtureLifecycle.CloneAsync`). Median about 28s each, class sum 112s.

### Git fixture per cleanup test

`WorktreeGuardedCleanupTests` is Integration, Slow, and on the process limiter. Every test calls `RemovalHarness.CreateAsync`, which builds a `LandingSafetyHarness`, and that calls `LandingGitFixture.InitializeAsync`: `git init`, bare remote, commit, push, worktree add, push, and a full clone (`LandingGitFixture.cs` lines 35–51), then `CreateIsolatedSchemaAsync`. The 250ms backoff is the only deliberate real delay (`RecordingClock` in the test file). The median test is 3.7s, so the git and clone work fits in that median on this host today.

### Other mechanisms checked

- `TUNIT_MAX_PARALLEL_TESTS` is not set on the Unit lane. Gotcha #26 sets it to 1 only for the CARD-0691 attention/census selection, because that selection exhausted Postgres connections. The Unit sum/wall of 3.4 shows the default parallelism is in effect.
- Headed and pty classes use `[NotInParallel("Headed")]` or `[NotInParallel("Pty")]`. They are outside the Unit filter.
- Fixed sleeps that matter on the common path are the delivery-harness timeouts above and the effort-dialog budgets inside `RunnerClaudeAdapterEffortPromptTests` (method median 7.4s for the dialog that never clears). Those run in parallel with the probe builds, so they do not add their full sum to the Unit wall.
- A real `Program` host is not what the top Unit or MessageQueue classes do. They build a `ServiceCollection`. E2E classes that boot the app are `[NotInParallel]` under `tests/Antiphon.E2E` and were not in these checkpoint TRX totals.

## 3. Build cost

Cold `dotnet build tests/Antiphon.Tests` into `bin-c728/`: **Time Elapsed 00:03:44** (224s), exit 0, 389 warnings, 0 errors. The wrapper's extra minute is the slot wait in section 5.

`-clp:PerformanceSummary`: `Csc` 256.9s summed over 17 compiles, `CoreCompile` 256.9s, `RestoreTask` 0.9s. Project time reported for `Antiphon.Tests` is 224s, which matches the wall, so the test project is the critical path and includes waiting for references. Output timestamps of this run:

| Output | Time (UTC) |
|---|---|
| First project dlls (Messaging and neighbours) | 21:45:17 |
| `Antiphon.SessionRunner.dll` | 21:45:44 |
| `Antiphon.Server.dll` | 21:47:10 |
| `Antiphon.FakeClaude` / last dependency | 21:47:26 |
| `Antiphon.Tests.dll` | 21:48:51 |

The server compile is about 90s. The test assembly after its last dependency is about 85s. That second stretch is the TUnit compile of `tests/Antiphon.Tests`: 966 `.cs` files, 320,477 lines. Restore of the graph was under a second per project once packages were cached. `IncrementalClean` was 31ms. The FileListAbsolute ledger problem from CARD-0222 is not this build.

No-op rebuild, same `OutputPath`: **Time Elapsed 00:00:10.80**, exit 0, 1 warning (MSB3277 in `Antiphon.DockerStack.Fixture`), no `Csc` task. Up-to-date check and copy, not a second compile.

So a later row that reuses `bin-c728/` spends 11s. A row that picks a fresh `bin-<name>/` spends another 3 minutes 44 seconds, because isolated outputs do not share `obj` compile products across output paths in a way that skipped this cold compile. The graph is 17 projects. `Antiphon.Tests` references the server, so every test build compiles the server on a cold output.

`-maxcpucount:4` was the requested cap (and the unleased fallback). This host has 24 cores. A higher count was not measured. `-nodeReuse:false` and `--disable-build-servers` were on for both builds; the no-op still finished in 11s, so those flags are not the 3:44.

## 4. Lane composition

CP-12 is the Category=Unit filter: about 3,150 tests, wall about 170s when green. The default recipe in `docs/testing-and-build.md` is one isolated build, that Unit lane, then the named integration classes. Plans that put `RunCheckpointScriptTests`, `SessionMessageQueueDeliveryVerificationTests`, or `TranscriptHotPathQueryTests` in the table add the serial minutes in section 2 on top.

Narrower lanes are already the rule for a one-class change. The expensive pattern is a checkpoint row per class, each a new `dotnet run`. Each such process pays the 77s Postgres lead when the filter touches the store, and today each row also pays the 60s slot grace in section 5. Six store-touching rows are about 6 × (60s + 77s) = 14 minutes of repeated startup before the test bodies. One process would pay that pair once. That reuse is CARD-0723's job; this investigation did not change the runner script.

A Review that reruns the same table after a clean rebase pays the table again. A Unit row that reuses the build is about 1 minute of grace + 11s of up-to-date build + 2.8 minutes of tests, roughly 4.5 minutes. A cold rebuild makes it about 8 minutes. Skipping an unchanged Unit lane would save that, and it can miss a break the diff does not name. That choice belongs with CARD-0723, as an explicit opt-in, not a silent skip.

## 5. Machine contention and CARD-0589

The live runner on `http://127.0.0.1:8080` returns **404** for `/build-slots`. `/capabilities` and `/sessions` return 200. The binary's commit is `3fdd91abb76a06bef729f4d1e8571713c9f310cb` (the CARD-0589 plan commit, 2026-09-25 06:22 +0100), process start `2026-09-25T06:49:33Z`. `BuildSlotRoutes.cs` was added later in `dd74d8762b` (19:18Z) and is not in that binary.

`scripts/lib/build-slot.ps1` treats HTTP 404 as an unreachable runner: it waits the 60s grace, then runs unleased at `-maxcpucount:4`. Both builds in this investigation printed `BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=http 404` and then built. The wrapper was 285s around the 224s compile, and 72s around the 11s no-op.

CARD-0589's cap is therefore not active on this host. Concurrent delegates still stack compilers, bounded only by each process's `-maxcpucount:4` after the grace. The Unit wall max of 406s against a 170s median, and script-test rows of 25–46 minutes against a 14 minute median, are what that looks like in the TRX. Deploying the runner that contains `/build-slots` removes the 60s tax per row and turns the cap on. Treating a definite 404 as immediate unlease would remove the tax without the cap; the deploy is the fix that does both. That work stays on CARD-0589.

## Ranked fixes

Minutes are wall-clock estimates for a stage that runs the named thing once. Code and Review each pay the row when their checkpoint table includes it. Risk and size are for the fix, not for the investigation.

| Rank | Fix | Saved per Code | Saved per Review | Risk | Size | Where |
|---|---|---:|---:|---|---|---|
| 1 | Run a stage's rows in one test process so Postgres startup and the slot grace are paid once | ~11 min on a 6-row store-touching table (5 × 137s). Less when rows do not touch the store | same | medium | medium | CARD-0723 |
| 2 | Deploy the server2 runner that serves `/build-slots` | 1 min × row count | same | low (runner restart) | deploy | CARD-0589 |
| 3 | Reuse one `OutputPath` inside the stage | 3.5 min for every row that would have been a cold build | same | low | already specified | CARD-0723 and the checkpoint table |
| 4 | Keep Unit off the shared store | 1.3 min | 1.3 min | medium | small | CARD-0732 |
| 5 | Build the classification probe once | 1.1 min | 1.1 min | low | small | CARD-0733 |
| 6 | One pwsh per checkpoint-script case | ~10 min when the class is in the table | same | low | small | CARD-0734 |
| 7 | Shared harness and a fake clock for delivery verification | 4.5 min when that class is named; more if other MessageQueue classes share the row | same | medium | medium | CARD-0735 |
| 8 | Seed the transcript hot path once per class | 1.5–2 min when that class is named | same | low–medium | small | CARD-0736 |

A common Code stage that builds once, runs Unit, and does not name the heavy integration classes is about 1 min grace + 3.7 min cold build + 2.8 min Unit, roughly 8 minutes, plus 1 min grace and 11s for each extra no-build row. Ranks 2, 4, and 5 are the part of that 8 minutes with a measured mechanism. Ranks 6–8 dominate only the plans that list those classes. Rank 1 is the structural saving when a plan has many rows.

`WorktreeGuardedCleanupTests` stays off this list until setup time is split from the removal. Its measured class total is 2.7 minutes, at 3.7s per test.

## Uncertainties

- The 43s and 61s shim timings were not traced inside `run-checkpoint.ps1`. git and TRX parse were timed separately and are under a second. The missing 40s is inside that process.
- Overlap between the 77s Postgres lead and the 66s probe chain was not re-measured by moving either. They currently stack (the lead is before the first test), so the two savings add. The remaining Unit body is on the order of half a minute.
- `-maxcpucount` above 4 was not measured.
- Desktop TRX files were not on this machine. Every figure here is server2.
- `WorktreeRetirementRaceTests` (236s sum, 5.4s per test) and `DelegationCapabilityTests` (225s, 11s per test) are Integration and were not opened far enough to say whether their sums are wall or core-seconds. They are not filed.
