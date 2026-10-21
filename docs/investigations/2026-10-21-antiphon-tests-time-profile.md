# Antiphon.Tests time profile (Windows rc launch groups)

Operator question: "where does the time go in the 19000 tests, and why does each launch take so long".

**Verdict.** Confirmed from stored TRX/diagnostic evidence plus three one-off measurements on this desktop.
A full Windows rc run of `Antiphon.Tests` is bounded by three things, in this order:

1. **Per-launch fixed cost.** The CARD-1404 launch packer splits the suite into about 220–230 launches
   (cap 120 weighted nodes). Each launch pays about **44 s of harness PowerShell** before the next process
   starts. **30.0 s** of that was measured: one function re-reads and re-censuses all 18,993 discovery nodes
   after every launch. A launch that touches the DB also pays about **27 s of Postgres fixture**, of which
   **~22.8 s is EF `MigrateAsync` over 212 migrations**. In the sampled run this overhead was
   **31.2 of 86.1 minutes (36%)**. Projected over a full run it is roughly **3.5–4 hours**.
2. **A one-wide `ProcessSpawnLimit` lane.** 6,425 of 18,993 nodes (34%) are on it, estimated at 5.4 h of
   serial test time. Inside a launch, effective parallelism is **1.37** (sum of durations / test span),
   or **1.15** counting fixture time.
3. **A small number of Land* matrix classes**, each running real git and DB per test.
   `AgentTaskLandActiveSourceClaimTests` alone is 16 tests, 1,673 s (27.9 min), run serially.

No production or test source was changed. Nothing in this report is a fix.

## Evidence used

| Source | What it gave |
|---|---|
| `C:\Antiphon\releases\candidates\rc-2026-10-20-66e9cfa9fc6b\logs\2026-10-21-1550-ebd2ff29\antiphon-all-group-{1..27}.trx` (run `ebd2ff29`, sha `66e9cfa9fc6b`) | 2,247 results, 164 classes, per-test `duration`/`startTime`/`endTime`, categories |
| same dir, `antiphon-all-group-N-execution\*.diag` (TUnit `--diagnostic`, Trace) | process start, first InProgress, first Passed per launch |
| same dir, `antiphon-profile.discovery.json` (9.5 MB) | full census: **18,993 nodes, 1,355 classes**, categories per node |
| `rc-2026-10-20-cadf547f7d69\logs\2026-10-21-1250-0f43c3e5\*.trx` (8 files, 1,619 results) and `rc-2026-10-20-0b455324d2cb\logs\2026-10-21-1107-883e74b2\*.trx` (32 files, 277 results) | All their (class, test) keys overlap run `ebd2ff29`, so they add no coverage. They give a repeatability check: per-class sum ratio B/A is p10 0.73, p50 1.06, p90 1.92 over 127 common classes. |
| Source at this branch HEAD `47e6916c7590b101c9336fab84398fa93b68878f` | Read for mechanism. The rc ran `66e9cfa9`. The harness and fixture code read here may differ slightly from what ran. |

Scripts used (scratch, not committed): a TRX parser, a diag timeline parser, a stratified estimator, and
a harness-verdict timer. Discovery run on a fresh build of this HEAD found 19,086 tests, against 18,993
in the rc's discovery file.

## (1) Where test time goes

### Distribution, 2,247 sampled tests

| Statistic | Value |
|---|---:|
| Sum of durations | 4,544.2 s (75.7 min) |
| Mean | 2.02 s |
| p50 | 0.149 s |
| p75 | 1.06 s |
| p90 | 3.98 s |
| p95 | 5.59 s |
| p99 | 38.1 s |
| max | 309.4 s |

| Bucket | Tests | Sum | Share of time |
|---|---:|---:|---:|
| < 0.1 s | 1,006 | 25 s | 0.6% |
| 0.1–1 s | 654 | 228 s | 5.0% |
| 1–5 s | 441 | 1,160 s | 25.5% |
| 5–30 s | 119 | 965 s | 21.2% |
| 30–120 s | 26 | 1,857 s | 40.9% |
| ≥ 120 s | 1 | 309 s | 6.8% |

27 tests (1.2%) carry 47.7% of the sampled time. 1,660 tests (74%) carry 5.6%.

### Top 30 classes (sampled)

| # | Sum s | Tests | Max s | Mean s | Launch | Categories | Class |
|---:|---:|---:|---:|---:|---:|---|---|
| 1 | 1672.6 | 16 | 309.4 | 104.5 | 27 | Integration, Slow | Application.AgentTaskLandActiveSourceClaimTests |
| 2 | 467.9 | 92 | 46.5 | 5.09 | 26 | Integration, Slow | Application.AgentTaskDispatchBaseGuardTests |
| 3 | 256.6 | 8 | 39.1 | 32.1 | 27 | Integration, Slow | Application.AgentTaskLandAdmissionTests |
| 4 | 215.0 | 24 | 12.1 | 8.96 | 27 | Integration | Application.AgentTaskLandAdmissionControlledTests |
| 5 | 164.5 | 45 | 6.4 | 3.66 | 20 | Integration, Slow | Application.AgentSessionRuntimeTests |
| 6 | 136.0 | 4 | 57.8 | 34.0 | 23 | Integration | Application.AgentTaskLandHalfResetWindowsTests |
| 7 | 119.7 | 86 | 67.5 | 1.39 | 25 | Integration, Slow | Application.AgentTaskDeliveryWatchdogTests |
| 8 | 94.8 | 71 | 60.2 | 1.34 | 19 | Integration, Slow | Application.AgentSessionLaunchFailureTests |
| 9 | 85.3 | 25 | 6.6 | 3.41 | 23 | Integration | Application.AgentTaskConcurrencyLimitTests |
| 10 | 83.4 | 19 | 6.5 | 4.39 | 22 | Integration | Application.AgentTaskCardBindingTests |
| 11 | 69.5 | 24 | 3.5 | 2.89 | 14 | Integration | ApiKeys.LaunchEnvLayersIntegrationTests |
| 12 | 69.2 | 39 | 10.9 | 1.78 | 23 | Integration | Application.AgentTaskCommitEndpointTests |
| 13 | 59.2 | 18 | 6.3 | 3.29 | 16 | Integration | Application.AgentPinnedInstructionServiceTests |
| 14 | 48.2 | 14 | 4.1 | 3.45 | 9 | Integration, Slow | AgentTui.AgentTuiLaunchResolverTests |
| 15 | 43.1 | 16 | 3.1 | 2.69 | 13 | Integration, Slow | ApiKeys.ApiKeyLaunchPathTests |
| 16 | 37.5 | 11 | 15.0 | 3.41 | 12 | Integration | Api.KeepSixReminderTests |
| 17 | 33.7 | 29 | 8.0 | 1.16 | 18 | Integration | Application.AgentSystemPromptLaunchTests |
| 18 | 32.8 | 65 | 1.4 | 0.50 | 10 | Unit | AgentTui.AgentTuiSecretProtectorTests |
| 19 | 30.1 | 28 | 2.7 | 1.07 | 17 | Integration | Application.AgentReportStoreTests |
| 20 | 28.5 | 12 | 13.9 | 2.37 | 1 | Integration | AgentTuiPersistenceTests |
| 21 | 25.6 | 6 | 7.6 | 4.27 | 3 | Integration | Agents.DirectSessionRunnerClientDisposalTests |
| 22 | 24.9 | 2 | 13.8 | 12.5 | 22 | Integration | Application.AgentTaskInternalDecisionMigrationTests |
| 23 | 23.5 | 7 | 7.0 | 3.35 | 2 | Integration, Pty, Slow | Agents.CodexAdapterLocalShellTests |
| 24 | 22.7 | 10 | 7.3 | 2.27 | 4 | Unit | Agents.RunnerClaudeAdapterEffortPromptTests |
| 25 | 22.6 | 3 | 15.2 | 7.55 | 12 | Integration | Api.ExpectationStatusTests |
| 26 | 21.8 | 4 | 10.0 | 5.45 | 12 | Integration | Api.ExpectationHoldReleaseEndpointTests |
| 27 | 20.9 | 1 | 20.9 | 20.9 | 1 | Integration, Slow | ProgramStartupConcurrencyTests |
| 28 | 20.4 | 10 | 2.1 | 2.04 | 24 | Integration | Application.AgentTaskDetailBlockedContextTests |
| 29 | 19.0 | 2 | 14.9 | 9.51 | 12 | Integration | Api.OrchestratorInstructionsEndpointTests |
| 30 | 18.2 | 9 | 4.1 | 2.02 | 5 | Unit | Agents.RunnerCodexAdapterTurnCompleteTests |

The top 3 classes are 2,397 s, 52.7% of the sample. The top 10 are 3,316 s, 73%.

### Top 30 single tests (sampled)

| # | s | Launch | Test |
|---:|---:|---:|---|
| 1–16 | 309.4, 119.9, 111.4, 110.1, 98.7, 97.3, 95.9, 94.5, 86.0, 85.0, 84.0, 78.1, 77.2, 76.9, 75.2, 72.9 | 27 | `AgentTaskLandActiveSourceClaimTests.C448_V29_ActiveSourceClaimHoldsEveryMode`: all 16 argument rows. Slowest is (ResumePublication, canonical, follow-up). |
| 17 | 67.5 | 25 | `AgentTaskDeliveryWatchdogTests.a_dispatched_task_with_no_transcript_after_the_window_fails_with_the_reason` |
| 18 | 60.2 | 19 | `AgentSessionLaunchFailureTests.Interactive_sign_in_block_persists_LaunchBlock_and_the_named_reason` |
| 19 | 57.8 | 23 | `AgentTaskLandHalfResetWindowsTests.C883_BuiltinCrLfRecoveryPreservesRealEdits` |
| 20 | 48.5 | 23 | `AgentTaskLandHalfResetWindowsTests.C883_LinkedWorktreeWithSpacesRecovers` |
| 21 | 46.5 | 26 | `AgentTaskDispatchBaseGuardTests.C1213_MintUsesCardRoleAndRunnerCapability` |
| 22–29 | 39.1, 38.1, 35.2, 34.6, 33.9, 31.7, 22.3, 21.6 | 27 | `AgentTaskLandAdmissionTests.C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther`: 8 argument rows |
| 30 | 20.9 | 1 | `ProgramStartupConcurrencyTests.Two_entry_point_invocations_racing_on_startup_both_serve` |

### By category (sampled)

| Category | Tests | Sum s | Mean s |
|---|---:|---:|---:|
| Slow | 437 | 2,983.7 | 6.83 |
| not Slow | 1,810 | 1,560.5 | 0.86 |
| Integration (any) | 1,509 | 4,400.0 | 2.92 |
| Unit | 738 | 144.2 | 0.20 |
| Pty | 23 | 40.9 | 1.78 |
| GitIntegration | 21 | 10.7 | 0.51 |

### By namespace (sampled)

| Namespace | Tests | Sum s | Mean s |
|---|---:|---:|---:|
| Application | 1,218 | 3,847.2 | 3.16 |
| Agents | 512 | 232.0 | 0.45 |
| Api | 44 | 151.6 | 3.45 |
| ApiKeys | 136 | 138.0 | 1.01 |
| AgentTui | 264 | 108.9 | 0.41 |
| (root) | 73 | 66.5 | 0.91 |

The 27 sampled launches covered only these six namespaces. Infrastructure, Checkpoints, Scripts
(RemoteScriptContract), TestHelpers, Herdr-bearing classes and the rest of Application were never
reached: launch 28 was cancelled at 381 s. Their figures below are imputed.

### Full-suite estimate (18,993 nodes)

Method: a sampled class uses its own mean × its discovered node count (2,027 nodes, 1.21 h). An unsampled
class (16,966 nodes, 89%) uses the mean of its stratum. Strata are {Slow, Integration, Unit} ×
{Land/Delivery/Check family or not}.

| Imputation rule for unsampled classes | Serial total |
|---|---:|
| median per test (floor) | 3.5 h |
| mean per test, excluding the `ActiveSourceClaim` outlier class | **7.4 h** |
| mean per test, all | 8.9 h |
| mean of class means (ceiling) | 14.8 h |

Central estimate: **7.4–8.9 h of summed test time**. CARD-0856 measured ~270 min wall at ~1.4x, which is
about 6.3 h of summed time. That cross-checks within 20%.

**How good the estimate is: weak.** 89% of nodes are imputed. The heavy "Slow + Land-family" stratum has a
mean of 18.6 s against a median of 0.45 s, so a single class moves the answer by hours. Run-to-run noise
for the same class is 0.73x–1.92x (p10–p90). The mechanism split below has the same weakness. To make it
good, finish one full launch-group run and parse all ~225 TRX files with the same script.

Estimated serial time by mechanism. Classification is per source file: it uses `CreateIsolatedSchemaAsync`
/ `TestDbFixture` / `AntiphonWebAppFactory`, git init/clone/worktree, or `Process.Start` / pty / FakeClaude.

| Mechanism | Nodes | Est. h | of which sampled nodes / h |
|---|---:|---:|---|
| git + DB (Land*, worktree, dispatch guard) | 3,775 | 3.39 | 579 / 0.75 |
| DB-backed integration | 3,823 | 1.98 | 305 / 0.21 |
| other integration (in-memory host, HTTP, no DB/git/proc found) | 3,469 | 1.69 | 200 / 0.10 |
| process / pty spawn | 1,978 | 0.88 | 250 / 0.04 |
| git repo / worktree only | 2,050 | 0.76 | 107 / 0.08 |
| pure unit | 3,898 | 0.20 | 586 / 0.03 |

By category: Slow 3,063 nodes / 4.5 h, Integration-not-Slow 10,017 / 4.1 h, Unit 5,913 / 0.3 h. By
namespace: Application 14,229 nodes / 7.6 h, Infrastructure 1,334 / 0.45 h, Checkpoints 647 / 0.35 h,
Scripts 1,312 / 0.29 h, Agents 564 / 0.08 h. Everything else is under 0.05 h.

Largest imputed classes: `AgentTaskLandPublicationTests` (63 nodes, ~20 min), `AgentTaskLandRemovalMatrixTests` (50,
~16 min), `AgentTaskLandCheckpointMatrixTests` (43, ~13 min), `AgentTaskLandBoundaryTests` (38, ~12 min),
`AgentTaskLandCleanupSafetyTests` (27, ~8 min), `AttentionServiceTests` (167, ~8 min),
`DelegationDispatchRecoveryBoundaryTests` (381, ~7 min), `AgentTaskReplyIntegrationTests` (334, ~6 min),
`SessionMessageQueueDeliveryVerificationTests` (123, ~6 min; CARD-0728 measured 270 s on Linux). These are
projections from stratum means, not measurements.

Real sleeps and polling: no top-10 class has a fixed sleep as its dominant cost (see 2). 244 test files
contain `Task.Delay(`, 6 contain `Thread.Sleep(`, and 576 reference `TimeProvider`/`FakeTimeProvider`.
This sample cannot attribute time to sleeps.

## (2) Why the top classes are slow

| Class | Dominant cost (from code) | Evidence |
|---|---|---|
| `AgentTaskLandActiveSourceClaimTests` (16 × ~100 s) | **Three full git + DB harnesses per test, run one at a time.** Each `LandingSafetyHarness.InitializeAsync` builds a `LandingGitFixture` with 11 real git processes (`init`, `init --bare`, `add`, `commit`, `rev-parse`, `remote add`, `push`×2, `worktree add`, `clone --no-hardlinks`) and a cloned DB. Each test then runs 2–3 real lands (each spawns many git processes), plus `mklink /J` via `cmd.exe` for the alias rows. The class is `[ParallelLimiter<ProcessSpawnLimit>]`, so the 16 rows ran strictly back to back (launch 27 timeline: 17:07:38 → 17:35:31, one at a time). | `tests/Antiphon.Tests/Application/AgentTaskLandActiveSourceClaimTests.cs:22,42-209,236-284`; `TestHelpers/LandingSafetyHarness.cs:41-44`; `TestHelpers/LandingGitFixture.cs:35-52` |
| `AgentTaskDispatchBaseGuardTests` (92 × 5.1 s) | Real git repositories + DB per test, on the spawn lane. Launch 26 parallelism was 1.0. | class at `Application/AgentTaskDispatchBaseGuardTests.cs:27-30` (`Slow`, `ProcessSpawnLimit`) |
| `AgentTaskLandAdmissionTests` (8 × 32 s) | Same land harness with real dispatch admission, on the spawn lane, serial (17:35:31 → 17:39:48). | launch 27 timeline |
| `AgentTaskLandAdmissionControlledTests` (24 × 9 s) | Same harness, but **no spawn limiter**: all 24 started at 17:07:38 and finished by 17:07:54. That is 215 s of test time in 16 s of wall (~13x). | launch 27 timeline. This is the direct witness that the Land harness itself runs concurrently. |
| `AgentSessionRuntimeTests` (45 × 3.7 s) | Session runtime with process spawn; `Slow`. | launch 20, par 25.7: mostly concurrent |
| `AgentTaskLandHalfResetWindowsTests` (4 × 34 s) | Real git worktree half-reset recovery on Windows (CRLF, spaces, case alias, native index). | launch 23 serial run 16:46:36–16:48:52 |
| `AgentTaskDeliveryWatchdogTests` (86 × 1.4 s; one 67.5 s) | `[NotInParallel]` with **no key** (global-sweep suite on the shared `antiphon_test` DB). The watchdog window is seeded (`dispatchedMinutesAgo: 11`), not a wall-clock wait. The 67.5 s test is the 9th test of its launch. | `Application/AgentTaskDeliveryWatchdogTests.cs:28-35,55-68` |
| `AgentSessionLaunchFailureTests` (71 × 1.3 s; one 60.2 s) | `NotInParallel("AgentSessionLaunchFailure")`, launch 19 at par 1.0. The 60 s test is the 10th of its launch. | launch 19 |
| `AgentTaskConcurrencyLimitTests`, `AgentTaskCardBindingTests` (3–4 s each) | DB-backed dispatch with isolated clones. | launch 22/23 |

Specific questions:

- **Wall-clock timeouts that could use an injected clock:** none found among the top-10 dominant costs. The
  outliers (67.5 s, 60.2 s, 46.5 s) were each the 7th–10th test in their launch. Reconstructed hypothesis:
  per-launch cold first use (EF model build, DI graph JIT, first DB clone), paid again in every launch. My
  single DB-test launch took 3.2 s for a test that took 0.25 s inside the rc run, which supports a cold
  cost but does not size a 60 s one. **Unconfirmed.** What would resolve it: one launch of
  `AgentTaskDeliveryWatchdogTests` alone, with the outlier test first and then repeated.
- **Per-test database creation:** yes. 382 files call `TestDbFixture.CreateIsolatedSchemaAsync()`. Each call
  is a `CREATE DATABASE … TEMPLATE antiphon_tmpl`, serialised through one `SemaphoreSlim(1,1)`
  (`TestHelpers/TestDbFixtureLifecycle.cs:159`, `CloneAsync` ~:93-130, with 50·n ms retry on `ObjectInUse`).
  Only 1 file uses `TransactionalTestBase` (transaction rollback). Per-clone cost was **not measured**
  (no timing in the TRX). What would resolve it: a stopwatch log around `CloneAsync`/`DropAsync` in one
  DB-heavy launch.
- **Per-test git repo creation:** yes. `LandingGitFixture.InitializeAsync` runs 11 git processes per fixture
  and every Land* safety test builds 1–3 fixtures. A copied template repository is not used.
- **`ProcessSpawnLimit` serialising tests that may not need it:** see (3). 1,560 spawn-limited nodes
  (93 classes) spawn only git, and 1,365 (123 classes) spawn other non-agent processes. The
  `ControlledTests` class above shows the land harness runs at ~13x when not limited. Whether git-only
  classes can share the lane safely is a design decision; CARD-0050 S5 covers ConPTY and FakeClaude.
- **`NotInParallel` keys that serialise more than needed:** 200 bare `[NotInParallel]` attributes (no key;
  these are mostly global-sweep shared-DB suites by their own comments) cover 2,579 nodes (~0.9 h est.).
  The keyed ones are `MessageQueue` 60, `AgentQueue` 31, `Headed` 14, `C691Census` 11, `RealCliStubProxy` 9,
  `Pty` 7 and smaller groups.

## (3) Parallelism

Limiters, from source at HEAD:

| Limiter | Scope |
|---|---|
| `ParallelLimiter<ProcessSpawnLimit>` with `Limit => 1` (`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:13-16`) | 617 attribute lines in 412 files; 6,425 discovery nodes (34%), est. 5.4 h serial. Of these, 3,500 nodes (190 classes) spawn pty, a fake agent, pwsh, the session runner or a web app; 1,560 (93) spawn only git; 1,365 (123) spawn other processes. 95 limited files are also DB-backed. |
| bare `[NotInParallel]` | 200 attributes, 2,579 nodes |
| keyed `[NotInParallel("…")]` | 193 attributes |
| DB clone lock | one `SemaphoreSlim(1,1)` per process around `CREATE DATABASE … TEMPLATE` |
| Harness | one native launch at a time (`nightly-tests-impl.ps1:944-952` throws `native overlap`) |
| Host | `scripts/build-slot.ps1` lease (`maxcpucount=4`). During this investigation the host sat below its 6,144 MB memory floor (5,433 MB available) and made `--list-tests` wait 15 s. |

Measured parallelism per launch (sum of test durations ÷ first-test-start→last-test-end span):

| Launch | Tests | Classes | Sum s | Span s | Wall s | Par | Note |
|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | 73 | 10 | 66.5 | 44.2 | 75.6 | 1.50 | |
| 4 | 83 | 6 | 60.6 | 15.3 | 15.9 | 3.96 | no DB |
| 9 | 76 | 2 | 48.9 | 4.1 | 35.0 | 11.96 | |
| 10 | 65 | 1 | 32.8 | 1.5 | 18.3 | 22.01 | |
| 11 | 27 | 1 | 16.4 | 16.5 | 17.4 | 1.00 | spawn lane |
| 19 | 71 | 1 | 94.8 | 94.9 | 121.3 | 1.00 | `NotInParallel` key |
| 20 | 45 | 1 | 164.5 | 6.4 | 33.1 | 25.7 | |
| 23 | 120 | 5 | 317.4 | 226.2 | 253.7 | 1.40 | |
| 25 | 86 | 1 | 119.7 | 119.8 | 227.0 | 1.00 | bare `NotInParallel` |
| 26 | 92 | 1 | 467.9 | 468.0 | 497.3 | 1.00 | spawn lane |
| 27 | 48 | 3 | 2,144.2 | 1,929.3 | 1,968.1 | 1.11 | spawn lane + 24-way burst |
| **all 27** | 2,247 | 164 | 4,544.2 | 3,306.5 | 3,965.4 | **1.37** span / **1.15** wall | |

Launch 27 alone is 37% of the run's elapsed time, with 48 of the 2,247 tests. The full 27-row table is
reproducible from the scratch parser. Seven launches ran at parallelism 1.0–1.1, and they hold 65% of all
summed time. Counting the gaps between launches, the run delivered 4,544 s of test time in 5,167 s of
elapsed time: an effective **0.88x**.

## (4) Per-launch overhead

### Inside the test process (from run `ebd2ff29` diagnostics)

| Phase | Typical | Evidence |
|---|---:|---|
| Process start → MTP host built → TUnit invoked | 0.2 s | group 27 diag: 17:07:01.778 `CreateBuilderAsync` → 17:07:02.005 framework invoked |
| In-process discovery and filter → first test InProgress | 0.4–1.5 s (all 28 launches) | diag first InProgress |
| `[Before(Assembly)]` `TestDbFixture.InitializeAsync` (only when the selection touches the store) | **24.5–37.7 s** (20 of 27 launches); non-DB launches 0.7–4.8 s to first pass | diag first Passed; TRX `Times/@start` → first `startTime` = 641 s across 27 launches |
| Teardown: TRX finish → process gone and log rewritten | 0.2–1.3 s (5.3 s for launch 27) | TRX `finish` vs `-tests.log` mtime |

### One-off desktop measurements (this HEAD, `bin-c9743/`, through `build-slot.ps1`, each run once)

| Run | Wall | Detail |
|---|---:|---|
| Build `tests/Antiphon.Tests` | 194 s | 0 errors, 1,111 warnings |
| `Antiphon.Tests.exe --list-tests` | 6 s held (+15 s slot wait on memory floor) | **19,086 tests discovered in 4.9 s** |
| One trivial unit test (`RunnerGrokAdapterReadyTests/Expired_trust_budget_cancels_pending_read`) | **3.1 s** | TUnit duration 1.26 s |
| One trivial DB test (`AgentTaskCheckInterpreterTests/the_board_hides_interpretation_rows_unless_they_are_asked_for`) | **32.8 s** | TRX start → test start **27.6 s**, test 3.2 s, finish +0.6 s |

`docker events` for that DB launch (offsets from TRX start): Ryuk container created at +0.8 s and started at
+1.1 s; `postgres:16-alpine` created at +2.2 s and started at +2.5 s; `pg_isready` passed by +4.8 s; the test
started at +27.6 s; kill and destroy at +30.9 s and +31.3 s. That leaves **~22.8 s between "Postgres ready" and
the first test**. In `TestDbOperations` (`TestHelpers/TestDbFixtureLifecycle.cs:47-73`) that window is
`context.Database.MigrateAsync()` over **212 migrations**, then `CREATE DATABASE antiphon_tmpl TEMPLATE`
and two `ALTER DATABASE` calls. Splitting migrate from template was not measured separately, but the
template copy of an empty schema is small.

### Between launches (harness PowerShell)

Next diag creation minus previous log write: **median 44.1 s, range 32.5–88.2 s, 1,232 s over 26 gaps**.
Test-process teardown is not part of it (≤1.3 s, above). The loop body is
`scripts/lib/nightly-tests-impl.ps1:935-1110`. Its per-launch work:

| Step | Measured | Code |
|---|---:|---|
| `ConvertTo-NightlyNativeSuiteVerdict` with the rc profile (22 exclusion rows) against the **full 18,993-node discovery file**, for one 48-row launch | **30.0 s** (pwsh 7, this desktop, stored group-27 evidence) | `nightly-tests-impl.ps1:1051`; `nightly-coverage.ps1:1046-1211` |
| … of which `ConvertFrom-Json` of the 9.5 MB discovery | 0.8 s | `nightly-coverage.ps1:142-149` |
| … `ConvertFrom-NightlyDiscoveryDocument` (`$nodes += [pscustomobject]` per node, O(n²)) | 3.6 s | `nightly-coverage.ps1:86-130` |
| … TRX read 0.1 s, diag parse 0.2 s, `ConvertTo-NightlyCoverageVerdict` 0.5 s | 0.8 s | |
| … remainder: `Get-NightlyRequiredDiscoveryUids` plus **two** `Get-NightlyDiscoveryCensus` passes over all nodes (`$required += $row`, a per-node disposition call, canonical JSON + SHA-256) | ~24.8 s (by difference) | `nightly-coverage.ps1:766-822,1144-1145,1199-1200` |
| Other loop work: `Get-NightlySafeChildEnvironment`, log stamp rewrite, `Test-NightlyFreshEvidence`, owned-process launch, `Get-Command` | ~14 s, unattributed | `nightly-tests-impl.ps1:940-1007`; `nightly-owned-process.ps1` |

The 30 s depends on the **suite** size (18,993 nodes), not the launch size, so every launch pays it. The
timer ran this HEAD's functions, not `66e9cfa9`'s.

### Per-launch total and the full run

| Component | Per launch | Sampled run (27 launches) |
|---|---:|---:|
| Harness between launches | ~44 s | 1,232 s (20.5 min) |
| DB fixture (DB-touching launches, ~74%) | ~27 s | 641 s (10.7 min) |
| Teardown | <1 s | 16 s |
| **Overhead** | **~64 s average** | **31.2 min of 86.1 min elapsed (36%)** |

A packer simulation over the discovery file (cap 120 nodes and 120 weight; Slow ×4, Land/Delivery/Check ×2;
first-fit per namespace) gives **~221 launches**. The sampled run averaged 83 tests per launch, which
projects to ~229. At ~64 s each, that is **~3.9 h of fixed overhead per full run**, before any test runs.

## (5) Recommendations, ranked by estimated hours saved per full Windows run

Estimates assume ~225 launches, the 7.4–8.9 h central test-time estimate, and parallelism ~1.15. They do not
add up: fewer launches shrinks every per-launch item.

### Quick wins (harness / config)

| # | Change | Where | Est. saved | Effort |
|---:|---|---|---:|:---:|
| Q1 | Stop re-censusing the whole discovery after every launch. Read and census it once per suite, and check each launch only against its own required UIDs. Replace `+=` array growth with `List[T]` in `ConvertFrom-NightlyDiscoveryDocument`, `Get-NightlyDiscoveryCensus` and `Get-NightlyRequiredDiscoveryUids`. | `scripts/lib/nightly-coverage.ps1:86,766,808,1046`; call site `nightly-tests-impl.ps1:1051` | **~1.9–2.4 h** (30 s measured + part of the 14 s, × 225) | S–M |
| Q2 | Fewer, larger launches: raise `NightlyLaunchGroupNodeCap` / weights, or launch per namespace for non-heavy namespaces (Unit/AgentTui/Agents launches are seconds of tests behind ~44 s of harness). Bounded by the 60-minute `antiphon` watchdog. | `scripts/lib/nightly-tests-impl.ps1:127-129` | ~1 h per halving of the launch count after Q1 (~64 s → ~35 s per launch); ~2 h per halving before Q1 | S |
| Q3 | Pay the schema once: make the template from a cached migration snapshot (e.g. a schema dump keyed by migration hash, or a pre-migrated image or volume) instead of `MigrateAsync` over 212 migrations in every process. | `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:47-52` (`TestDbOperations.MigrateAsync`) | **~1.0 h** (~20 s × ~170 DB launches) | M |

### Test refactors

| # | Change | Where | Est. saved | Effort |
|---:|---|---|---:|:---:|
| T1 | Give git-only and other non-agent spawners their own wider limiter (for example 4), keeping the 1-wide lane for pty / FakeClaude / FakeGrok / pwsh / session runner. Update the `ProcessSpawnLimitTests` roster. | `TestHelpers/ProcessSpawnLimit.cs`; 93 git-only + 123 other classes; `ProcessSpawnLimitTests.cs` | ~1.0–1.3 h of wall (1.75 h serial at up to 4x) | M |
| T2 | Shrink the Land* matrices. `ActiveSourceClaim` builds 3 harnesses per row × 16 rows; a pairwise subset or a shared seeded repository copied per test (directory copy instead of 11 git processes per fixture) would cut most of it. The same pattern is in `AgentTaskLandPublicationTests`, `…RemovalMatrixTests`, `…CheckpointMatrixTests`, `…BoundaryTests`, `…CleanupSafetyTests`. | `TestHelpers/LandingGitFixture.cs:35-52`, `TestHelpers/LandingSafetyHarness.cs:41`, the classes named | ~0.4 h (ActiveSourceClaim measured 28 min) to ~1.2 h (with imputed siblings) | M–L |
| T3 | Move bare-`[NotInParallel]` global-sweep suites (`AgentTaskDeliveryWatchdogTests` and others) onto isolated DB clones, then drop or key the attribute. | 200 attribute sites, 2,579 nodes | ≤0.5 h (0.9 h serial, partial overlap possible) | M |
| T4 | Size the per-launch cold-start cost (the 46–67 s outliers in the first ~10 tests of a launch). If it is real, Q2 removes most of it. | diagnosis first | unknown | S to measure |

Not recommended from this evidence: switching `--diagnostic` off. Diag files are 0.18–0.27 MB per launch
and the harness needs them for the census.

## Not done, noted

- Per-clone `CREATE DATABASE … TEMPLATE` cost and the migrate/template split inside the 22.8 s were not timed.
- 89% of nodes were imputed. A completed full launch-group run, parsed with the same script, would replace the estimate.
- The unattributed ~14 s of each harness gap was not profiled. Next step: a `Measure-Command` around each step of `nightly-tests-impl.ps1:935-1110` with one seam run.
- Fix idea: one suite-level census computed before the loop, passed to a per-launch verdict that only checks its own UIDs.

--- next stage ---
next: plan
handoff: Plan cutting Antiphon.Tests Windows rc wall time: Q1 suite-once census in nightly-coverage.ps1 (30 s of every ~44 s launch gap), Q2 fewer launches (cap 120, ~225 launches), Q3 cached-schema template instead of 212-migration MigrateAsync (~22.8 s per DB launch); then T1 split ProcessSpawnLimit for git-only spawners, T2 Land* matrix fixtures.
artifact: docs/investigations/2026-10-21-antiphon-tests-time-profile.md
