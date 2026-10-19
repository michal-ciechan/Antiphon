# Gate lanes beside other tasks

Status: **the six Linux roster failures are not shared build-output collisions.** Five fail alone, inside a private temp git repo, and belong to the ledger-nondeterminism investigation. The sixth is the already diagnosed `/proc/<pid>/stat` race, which host load widens and which an isolated rerun does not reproduce. Build provenance is already a file in the per-task output directory. What still couples a gate to other work is the host build-slot lease held for the whole test run, the 16 GB memory floor, the shared git object directory, that `/proc` race, and one roster test that deletes a fixed host directory.

No product or test file was edited. No build or test was started for this note. The stored rosters already separate the six; a new concurrent run would not change that split. Code citations are this checkout, `c8bc95276ad1dfa22fcfcd0d1a2b3b2c6a1d80b0`. The confinement-ledger test blob `e4e39e582ba67b1c6fc80c9c122069ca55e34d1b` and `CheckpointNativeIo.cs` blob `0ed27e4571c20533273ae4f2009aa08a7f317eff` are the same blobs as `42cbbd0417a2611f3a690e6671b13c9ba51fbf34`.

## Evidence read

| Record | Where | What it shows |
|---|---|---|
| Second Linux re-gate, task `ede44ef3` | `/work/worktrees/task-ede44ef3/docs/investigations/2026-10-19-rc-linux-regate2.md`. TRX `/tmp/rc-linux-ede44ef3/roster/run.trx`, SHA-256 `1679f3f74ff32128739760367bf9647600515fb652eeab3d9a8906a19e443dd6` | Candidate `42cbbd04`. 4528 executed, 4522 passed, 6 failed. Slot wait 0s. Roster lease held 2170s |
| First Linux re-gate, task `541c1ad7` | `/work/worktrees/task-541c1ad7/docs/investigations/2026-10-19-rc-linux-regate.md`. TRX `/tmp/rc-linux-541c1ad7/roster/run.trx`, SHA-256 `0275471162ed94a65aaa31cba158453ea91c1997dc090b329f66cf9af074a880` | Same candidate. 4527 passed, 1 failed (`outside`). The five ledger methods passed |
| Linux re-gate, task `ed312042` | `/work/worktrees/task-ed312042/docs/investigations/2026-10-19-rc-linux-regate.md` | Different commit `1f72e201`. 4528 passed, 0 failed. Slot wait 0s. Roster lease held 2143s |
| `/proc` flake | `/work/worktrees/task-75ae5020/docs/investigations/2026-10-19-rc-proc-stat-flake.md` | Mechanism, 2/200 probe, 40/40 isolated passes of `outside` |
| Windows hangs | commit `d3b1426df7b50101d4ea96be2fc1f2a5b53b9500` on `origin/feat/card-task-5b8ad483`, `docs/investigations/2026-10-19-rc-windows-hangs.md` | Task `5b8ad483`, SHA `1f72e201`. Not task `cb5aa16d` |

`docs/investigations/2026-10-19-rc-regate.md` is not in any fetched commit. `refs/heads/feat/card-task-cb5aa16d` is not on origin. The brief's statement that Code `258a65c2`, reviews, and builds were running on server2 during `ede44ef3` is not repeated in that roster record. Both Linux re-gates report slot wait 0s, so those runs were not queued behind another lease.

## 1. The six

All six are in the frozen Linux Unit roster. None of them reads or writes the host checkout's `bin/` or `obj/`, and none of them calls the host build-slot broker.

| Failure | Sensitive to another build or test on the host? | Mechanism |
|---|---|---|
| `CheckpointConfinementLedgerTests.rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch` | No. Failed alone at `42cbbd04` (3.669s) and at `61117dd1` (3.896s), same assertion. Passed in the `541c1ad7` roster at this same SHA (8.378s) | `f0012.js` should be `module.exports = 98;` and was `module.exports = 12;` (`CheckpointConfinementLedgerTests.cs:265`). `98` is the rewrite the fake slot's `onAcquire` applies; `12` is the rewrite the fake build applies only if admission lets the build start (`:259-261`). The file is created by `BeginRepo` (`:458-479`) under `TempDir()`, which is `c723-` plus a new guid under `Path.GetTempPath()` (`CheckpointTestScope.cs:50-54`). Another worktree's `dotnet build` cannot see it |
| `portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch` | No. Same shape, alone at both tips | `f0011.js` should be `99` and was `11` (`:240`). Same private repo and fake `RecordingSlotClient` |
| `file_changed_during_the_slot_wait_and_restored_by_the_build_is_refused_before_launch` | No. `LedgerCaptures` was `[]` alone at both tips | The capture callback never ran (`:159`). That is the in-process scheduler path, not a shared output stamp |
| `scheduler_stamps_non_output_files_before_the_slot_and_verifies_after_release` | No. Same empty `LedgerCaptures` (`:131`) | Same |
| `strict_admission_hash_in_the_manifest_refuses_the_build_before_launch` | No. `BuildLedgers` was `[]` alone at both tips (`:356`) | The fake build never observed a ledger. Still the private repo |
| `CheckpointOwnedOutputTests.changed_inventory_is_refused(directory)` | Load-sensitive, not a shared path. Passed alone at both tips (4 executed, 4 passed). The `541c1ad7` roster failed a different argument of a different method, `disk_ledger_cannot_authorize_user_deletion(outside)`, with the same exception | `DirectoryNotFoundException` on `/proc/19015/stat`. `InitRepo` starts `git init -q` and registers the child before `WaitForExit` (`CheckpointOwnedOutputTests.cs:61-67`). `CheckpointTestScope.Register` calls `Capture` (`CheckpointTestScope.cs:95-98`). `CheckpointNativeIo.StartToken` reads `/proc/<pid>/stat` (`CheckpointNativeIo.cs:37`) after `GetProcessById` has already succeeded (`:26-27`). `ProcessIdentity.Observe` catches `FileNotFoundException` as dead (`ProcessIdentity.cs:48-51`) and lets `DirectoryNotFoundException` fall through to the `IOException` arm as `identity-unknown` (`:53-56`). `Register` has no catch, so the exception fails the test. The proc-flake note's 200-start probe of `/bin/true` got 2 misses, both `DirectoryNotFoundException`. A full roster shares `ParallelLimiter<ProcessSpawnLimit>` (limit 1, in-process only: `tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:13-15`) and delays the thread between `Start` and the stat read. That is CPU scheduling, not `bin/` or the build slot |

The five shorter failures (1.2–3.9s versus 4.8–12.5s when they passed) left the in-slot bytes in place or recorded no capture. That is ledger admission not running the refusal, which is the other investigation. Do not treat a concurrent `dotnet build` as their cause.

## 2. Shared state in the three trees

Cross-process state is what a gate shares with another task. `[NotInParallel]` and `Environment.SetEnvironmentVariable` do not cross a process boundary. `ParallelLimiter<ProcessSpawnLimit>` is documented as assembly-local in both projects (`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:9-11`, `tests/Antiphon.SessionRunner.Tests/ProcessSpawnLimit.cs:9-10`). The checkpoint tool has no `[NotInParallel]` and no `SetEnvironmentVariable`.

### Cross-process

| Shared thing | Where | What collides |
|---|---|---|
| Host build-slot broker | `scripts/lib/build-slot.ps1:26-29` (Linux default `http://127.0.0.1:8080/build-slots`, or `ANTIPHON_BUILD_SLOTS_URL`). Server2 budget in `docker-compose.server2-runner.yml:69-71`: `MaxConcurrent=4`, `MaxCpuCount=6`, `MinAvailableMemoryMb=16384`. Desktop defaults in `BuildSlotSettings.cs:16-22`: 2 concurrent, 4 cpus, 6144 MB, lease TTL 90 min | One lease per holder pid. A second POST from the same pid and process start time is handed the existing lease (`BuildSlotBroker.cs:79-84`). Four leases fill the server2 budget. Available memory under 16 GB refuses a new lease even when a seat is free (`BuildSlotBroker.cs:102-107`) |
| Lease covers the whole command, including a test run | `scripts/build-slot.ps1:89-147` acquires, runs the child, releases in `finally`. The three Linux rosters each held a lease for about 35 minutes with wait 0s. The checkpoint tool also acquires for a no-build row (`tools/Antiphon.Checkpoints/Execution/RunScheduler.cs:483`) and, separately, for the build (`:364`) | A `--no-build` roster occupies one of four seats and keeps the memory floor looking at a machine that is already running the suite. It does not protect the stamp, which is written at the end of the build |
| Provenance file | `CheckpointBuildBinding.PathName` is `OutputPath/checkpoint-build-source.json` (`SourceRunGuard.cs:83`). `Check` compares root, project, output path, properties, commit, fingerprint, and source state (`:95-108`). `Invalidate` refuses a pre-existing stamp or output directory (`:113-118`) | Two tasks with different output directories do not share the stamp. Reusing one output directory in one worktree does |
| Owned intermediates, checkpoint tool only | `BuildStep.AppendSharedLayout` sets `ArtifactsPath` to the owned output plus `/artifacts` and `UseArtifactsOutput=true` (`BuildStep.cs:42-51`). `BuildArguments` adds `-nodeReuse:false` (`:75`) | A raw gate build that sets only `OutputPath=bin-rc1019-<task>/` does not set those. Two such builds of one project directory share `obj/`. Two worktrees do not: each has its own project directory. This checkout's git common dir is `/work/repos/antiphon/.git` |
| Shared git object directory | `BuildConfinement.CaptureLedger` runs `git rev-parse` and `git ls-files` in the worktree (`BuildConfinement.cs:94-110`). `SourceRunGuard.Observe` snapshots the same repository (`SourceRunGuard.cs:23-38`) | A fetch, gc, or repack in any worktree locks the common object dir and can stall the snapshot. It does not edit another worktree's files. The cited rosters did not fail with `source_changed` |
| `/tmp` checkpoint sweep | `CheckpointTempRootSweep.SweepOnce` enumerates `c723-*.json` under the temp index and deletes a root only when `Observe` says the owner is dead (`CheckpointTempRootSweep.cs:128-160`, eligibility at `:212-213`). The coordinator lock is `FileShare.None` (`:302-304`) | Designed sharing. Today `DirectoryNotFoundException` is `Unknown`, so the sweep keeps the root. Classifying that exception as `Dead` (the proc-flake proposal) makes a single raced read eligible to delete. The grace period still applies |
| Fixed host directory deleted by a roster test | `GrokSha256PinTests` is on the frozen roster (`docs/investigations/2026-10-15-card-1287-linux-unit-roster.txt:320`). `Run` deletes `/tmp/grok-download` before and after the extracted Dockerfile snippet (`GrokSha256PinTests.cs:159-163` and `:207-210`). The lock is `private static readonly object Gate` (`:22`), so it serializes only this process. `[ParallelLimiter<ProcessSpawnLimit>]` is also in-process (`:15`) | Two `Antiphon.Tests` processes, or this test beside anything else using that directory, delete each other's tree. Image-contract tests only mention the path in strings (`GrokRunnerImageContractTests.cs:27-32`, `DockerStackContractTests.cs:563-571`) and do not delete it. No other `Directory.Delete("/tmp/...")` exists in these two test projects |
| Checkpoint tool smoke default | `tools/Antiphon.Checkpoints/Program.cs:140`, `Path.Combine(Path.GetTempPath(), "c723-smoke")` when smoke-detach is given no path | Two concurrent smoke-detach invocations with no argument share one path. Gate rosters do not call this |
| `/proc/<pid>/stat` readers besides registration | `tests/Antiphon.Tests/Scripts/LinuxScriptHarnessProcess.cs:199`, `ScriptHarnessLinuxOwnershipTests.cs:563`, `ScriptHarnessProcessFixture.cs:91`, `HostJqPrerequisiteScriptTests.cs:1524`, `tests/Antiphon.Tests/Checkpoints/SafeDeleteProcessBoundaryTests.cs:160`, `CheckpointFixtureProcessTests.cs:137`, `tests/Antiphon.Tests/Agents/DirectSessionRunnerClientDisposalTests.cs:259`, `tests/Antiphon.Tests/TestHelpers/TestOwnedPtyHost.cs:303` | Same read shape as `StartToken`. Not the six failures. A load-widened miss is the same exception if they do not catch `DirectoryNotFoundException` |
| PostgreSQL | `TestDbFixture` starts one Testcontainers container per test process. `SharedDatabaseName` is `antiphon_test` inside that container (`TestDbFixture.cs:11-19`). Isolated consumers get a cloned database, not a schema on a host database (`:65-70`) | Two test processes do not share a server. They collide only if a run is pointed at one external Postgres on purpose |
| Listen ports | `TcpListener` and Kestrel `Listen` in these two projects use `IPAddress.Loopback, 0`. No fixed listen port turned up | No cross-process bind collision in these trees |
| Production runner URL | `ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner` sets `SessionRunner__BaseUrl` to `http://127.0.0.1:1` for the assembly (`ProductionRunnerGuard.cs:41-66`). The checkpoint tool's default slot endpoint on Windows is `http://localhost:17204/build-slots` (`BuildSlotClient.cs:69-73`) | The guard is per process. A test that clears it can still call the real runner. The Linux gates set the dead URL and reported they did not use production sessions |

`HostCleanupFixture.CheckpointPath` (`tests/Antiphon.Tests/Infrastructure/HostCleanupFixture.cs:13`) is a string fed to an in-memory file system, not a directory the test creates. `/tmp/ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md` (`OrchestratorInstructionsDeltaTests.cs:128`) is a string passed into a message body. The many `/tmp/c557` and `/tmp/card-0505` hits are values stored on rows, not directories created at those paths.

### In-process only

`[NotInParallel]` sites: 455. `tests/Antiphon.Tests` 410, `tests/Antiphon.SessionRunner.Tests` 45, checkpoint tool 0. Unkeyed `(none)` is 226 plus `MatrixChildReceiptTests.cs:18` (`[NotInParallel, ParallelLimiter<ProcessSpawnLimit>]`). A key serializes that group inside one test process. An unkeyed attribute serializes against other unkeyed attributes in that same process (`BootReplyWatchdogTests.cs:36-40` says the unkeyed form is the fleet-global one, and it is still one process). A second `dotnet run` of the same assembly does not see those keys.

`SetEnvironmentVariable` sites: 88 lines, 41 in `Antiphon.Tests` and 47 in `SessionRunner.Tests`. The assembly hook is `ProductionRunnerGuard.cs:59-66`. `ClaudeConfigDirEnv` (9 sites) points `CLAUDE_CONFIG_DIR` at `Path.Combine(Path.GetTempPath(), $"antiphon-tailer-test-{Guid...}")` (`TranscriptTailerCompactionTests.cs:17-33`). Two processes do not clobber each other's environment. They would clobber a shared directory only if both chose the same path; these choose a guid.

Wall-clock bounds that a busy host can blow, from the lines read, not a full census:

| Site | Bound | Clock |
|---|---|---|
| `tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs:564-570` | real `Stopwatch` under 15s around a 2s git budget | wall clock |
| `tests/Antiphon.Tests/Infrastructure/GrokSha256PinTests.cs:182` | 15s then kill | wall clock |
| `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs:458`, `:498`, `:692`, `:736-737` | 2s, 2s, 5s, 10s | `elapsed.Elapsed` |
| `tests/Antiphon.Tests/Infrastructure/GitDiffSpikeTests.cs:182` | 5s | `Stopwatch` |
| `tests/Antiphon.Tests/Infrastructure/Resilience/HttpResilienceRegistrationTests.cs:118` | the method an older note called a 15s git flake | `ControlledTimeProvider`, not wall clock. No `gitElapsed` symbol remains in this tree |

The three Linux rosters did not fail these.

### Windows cap, from the record that is on this runner

`2026-10-19-rc-windows-hangs.md` (task `5b8ad483`, not `cb5aa16d`) parsed gate logs `C:\Antiphon\releases\candidates\rc-2026-10-18-1f72e20158b9\logs\2026-10-18-2318-d081f742`. Antiphon watchdog is 3600000 (`tests/test-execution-policy.json` as cited there). Of 2771 antiphon UIDs: 2697 passed, 69 error-with-duration, 4 skipped, 1 in progress at the kill second (`AgentTaskLandBareRepositoryCleanupTests.C1211_NestedTrackedHeadKeepsExistingAdmission` argument 2). Returned antiphon cases maxed at 1m 26s. That suite filled the hour with one just-admitted leftover. It is not a single test stuck on CPU, and it is not evidence that another build corrupted output. The e2e class `AgentTaskLandDeliveryE2ETests` is `[NotInParallel("C467LandDelivery")]` and the rows that returned already summed to 57m 43s, with about 19 more rows still queued. That class exceeds 60 minutes on its own. Load adds slot wait and CPU slowdown on top of a suite that is already at the cap. The hangs note's proposed `e2e` watchdog of 7200000 is a duration fix for that class, not a concurrency fix.

## 3. Slot versus per-task output

Keep the stamp where it is: `checkpoint-build-source.json` inside the task's output directory. A gate does not need a host-wide exclusive lease to make that stamp true. It needs the lease while `dotnet build` is writing that directory, so a second build of the same project directory cannot share `obj/` or the output, and so the confinement ledger's before/after snapshot is not racing writers in that worktree.

`scripts/build-slot.ps1` cannot tell a build from a test run. It leases whatever command it is given (`:89-147`). The rc drivers gave it the 35-minute roster. `RunScheduler.RunRowAsync` leases again for `--no-build` (`:483`). After the build method returns, its `await using` lease is released (`:364`); the row lease is the one that then sits for the whole filter. Read-only work never calls `Enter-AntiphonBuildSlot`, so it is already free of the seat. A test run is not read-only with respect to CPU, `/proc`, `/tmp/grok-download`, or the memory floor. It is read-only with respect to the stamp, provided it passes `--no-build` and does not delete the output directory.

Server2's budget of 4 means one roster does not by itself empty the host. It does spend a seat for half an hour, and four concurrent heavy runs saturate the 24-core box the compose comment describes (`docker-compose.server2-runner.yml:65-68`) and can push available memory under 16 GB so the next build waits. That is why a gate and other tasks disturb each other even when nobody shares `bin/`.

The confinement ledger is the wrong layer to retarget at output directories. It already skips write roots (`BuildConfinement.WriteRoots`, `:185-199`) and stamps everything else in that repository. The five failures are that admission path inside a throwaway repository. Pointing the stamp at another directory would not make `LedgerCaptures` non-empty.

## 4. Ranked changes

Each item is small enough for its own review. None of them is implemented here.

1. **Catch the registration race.** Half a day. This is the only one of the six that load widens. Red first, as `docs/investigations/2026-10-19-rc-proc-stat-flake.md` already specifies: a probe whose `Capture` throws `DirectoryNotFoundException` with the roster message must fail `Register` today and must not throw after `CheckpointTestScope.Register` ignores a reaped child; `Observe` on a matching identity must return `Unknown` / `identity-unknown:DirectoryNotFoundException` today and `Dead` / `identity-dead` after the `ProcessIdentity.cs:48` arm includes that exception. Do not use a live pid as the red test (2 misses in 200). Add a second `Observe` before `CheckpointTempRootSweep` treats the owner as dead, so one raced read cannot delete a live `c723-` root. Leave the five ledger methods out.

2. **Lease the build, not the `--no-build` run.** One to two days, plus the sentence in `docs/testing-and-build.md` that currently calls the slot a build-or-test lease. Red first: a checkpoint row with `NoBuild` true calls `IBuildSlotClient.AcquireAsync` once today (`RunScheduler.cs:483`); after the change it does not. A second witness drives `scripts/build-slot.ps1` around `dotnet run --no-build` and records a granted lease, then the same roster started without the wrapper leaves `GET /build-slots` unoccupied while a concurrent `dotnet build` of another worktree is granted. The stamp check (`SourceRunGuard.cs:95-108`) still runs. Memory floor stays on real builds. Do not pass `-NoSlot` for a build. Do not raise `MaxConcurrent` to hide the overlap.

3. **Stop deleting `/tmp/grok-download`.** Half a day. Red first: create `/tmp/grok-download/marker` and run `Runner_grok_sha_rejects_multiline_pin`; today the marker is gone (`GrokSha256PinTests.cs:161-162`). After, the extracted snippet runs with the download directory rewritten to the guid directory the test already creates at `:149`, and the marker remains. The Dockerfile text assertions stay on the image path. This test is in the frozen roster, so two gates delete that directory today.

4. **Give raw gate builds the owned layout the checkpoint tool already adds.** Half a day in the gate driver, not in `BuildStep`. Red first: `BuildStep.AppendSharedLayout` on a command that only has `OutputPath` yields `UseArtifactsOutput=true` and an `ArtifactsPath` under that output (`BuildStep.cs:42-51`). The rc notes set `OutputPath=bin-rc1019-<task>/` and `UseAppHost=false` and do not set those two. A unit assertion on the driver command is enough; two full builds of one worktree are optional confirmation. Different worktrees already have different `obj/` directories.

5. **Retry a shared-git lock inside the source snapshot.** About a day. Red first: hold the common-dir lock (`/work/repos/antiphon/.git`) during `SourceSnapshot` capture and show `source_unknown` today. After, a lock error retries for a short bound and does not become `source_changed`. The cited rosters did not fail this way, so it stays behind the three items above. Do not weaken `AdmitLedger`.

6. **Leave the Windows 60-minute antiphon cap alone.** The hangs record's e2e watchdog change is a separate card. Do not raise the antiphon 3600000 to absorb load. Re-read `docs/investigations/2026-10-19-rc-regate.md` if it shows up; it is not on this runner.

7. **Do not widen wall-clock assertions until a loaded roster fails them.** `LandingGitTests.cs:570` is the clear one (15s around a 2s budget). The three Linux rosters passed it.

## Uncertainties

- Task `cb5aa16d` and `docs/investigations/2026-10-19-rc-regate.md` were not available. The Windows load reading is the `5b8ad483` hangs note only.
- Other tasks during `ede44ef3` were not confirmed from broker logs here. Slot wait was 0s in the roster record, which rules out "this roster was waiting for a seat" and does not rule out CPU or memory pressure.
- No new concurrent build was run. The five-alone results in the `ede44ef3` record are the separation.
- The `/proc` readers in the script harness were not executed. They are the same read, not demonstrated roster failures.
- A full fake-clock versus wall-clock split of every `ShouldBeLessThan` was not done.

## Not done, noted

No code change. The registration catch, the no-build lease, and the `/tmp/grok-download` delete are the three edits that make a gate and other tasks safe to overlap; the five ledger reds stay with their own investigation.

## Appendix A. Every `[NotInParallel]` in the two test projects

455 sites. Key `(none)` means the attribute has no group. `TurnEndGapRestartDeliveryTests.cs:15` is `"MessageQueue"` on a line shared with `ParallelLimiter`. `MatrixChildReceiptTests.cs:18` is unkeyed, on a line shared with `ParallelLimiter`. The checkpoint tool and `tests/Antiphon.Checkpoints.LifecycleHost` have none.

- `tests/Antiphon.Tests/SmokeTests.cs:22` nameof(SmokeTests)
- `tests/Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs:62` ["Headed", "AgentQueue"]
- `tests/Antiphon.Tests/Application/ChannelInboundRecoveryTests.cs:30` "MessageQueue"
- `tests/Antiphon.Tests/Application/ReviewLoopTests.cs:29` "MessageQueue"
- `tests/Antiphon.Tests/Application/BootReplyWatchdogTests.cs:40` (none)
- `tests/Antiphon.Tests/Application/TurnEndGapRestartDeliveryTests.cs:15` "MessageQueue"
- `tests/Antiphon.Tests/Application/ChannelOutboundMigrationTests.cs:14` (none)
- `tests/Antiphon.Tests/Application/ExpectationReceiptTests.cs:15` "MessageQueue"
- `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/OutboundConversionManifestTestsPump.cs:20` (none)
- `tests/Antiphon.Tests/Application/RunnerAlarmWiringTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/GrokRulesCompactionAcceptanceTests.cs:21` "Headed"
- `tests/Antiphon.Tests/Application/CardDiagnosisApplyTests.cs:22` "DiagnoseLabels"
- `tests/Antiphon.Tests/Application/CapacityRecoveryCompatibilityTests.cs:15` (none)
- `tests/Antiphon.Tests/Application/ContextCompactionSweepTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/ContextCompactionSweepTests.cs:25` (none)
- `tests/Antiphon.Tests/Application/HerdrSupervisionBackoffTests.cs:21` (none)
- `tests/Antiphon.Tests/Application/AgentTaskScopeOverlapCreateTests.cs:25` "AgentQueue"
- `tests/Antiphon.Tests/Application/LandRefusalDeliveryWitnessTests.cs:15` "c1196-land-refusal"
- `tests/Antiphon.Tests/Application/CardRelationshipApiTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/PhoneHomeCompletedCardTransportTests.cs:17` "SessionLiveness"
- `tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs:33` "Headed"
- `tests/Antiphon.Tests/Application/OutputDistillationAdmissionTests.cs:14` (none)
- `tests/Antiphon.Tests/Application/BlockedTaskParkReleaseTests.cs:18` "MessageQueue"
- `tests/Antiphon.Tests/Application/ChannelBatchingTests.cs:22` "MessageQueue"
- `tests/Antiphon.Tests/Application/ChannelOutboundRecoveryTests.cs:33` (none)
- `tests/Antiphon.Tests/Application/RolePolicyInvalidValuesCreateTests.cs:38` "AgentQueue"
- `tests/Antiphon.Tests/Application/DelegationCostBackfillTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/DelegationCostBackfillTests.cs:26` (none)
- `tests/Antiphon.Tests/Application/SubscriptionUsageHttpTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundCaptureTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/OutputDistillationDispatchTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/ProductionRunnerIsolationTests.cs:25` (none)
- `tests/Antiphon.Tests/Application/PhoneHomeSchedulePreviewTests.cs:10` "MessageQueue"
- `tests/Antiphon.Tests/Application/StandingSessionSelectionTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/AgentSessionRuntimeTests.cs:111` "Pty"
- `tests/Antiphon.Tests/Application/ProjectDeletionTests.cs:30` "Board"
- `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs:29` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs:38` (none)
- `tests/Antiphon.Tests/Application/SpecialistToolPolicyLaunchTests.cs:18` "MessageQueue"
- `tests/Antiphon.Tests/Application/OrchestratorTrackerCadenceTests.cs:25` (none)
- `tests/Antiphon.Tests/Application/CodexStartupDeliveryTests.cs:25` "MessageQueue"
- `tests/Antiphon.Tests/Application/WorkflowDefinitionLoaderTests.cs:17` "Board"
- `tests/Antiphon.Tests/Application/CardFileBoardLookupTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/SpecialistTaskRunnerDeadlineTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/TranscriptHotPathQueryTests.cs:11` "TranscriptHotPathQueries"
- `tests/Antiphon.Tests/Application/AgentStartRecoveryTests.cs:28` "AgentStartRecovery"
- `tests/Antiphon.Tests/Application/GrokDelegateDispatchTests.cs:37` "AgentQueue"
- `tests/Antiphon.Tests/Application/OrchestratorStateProjectionTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/AuditArchiveEndpointTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/CardFileSyncEndpointTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/CardFileSyncEndpointTests.cs:135` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundDeliveryTests.cs:29` (none)
- `tests/Antiphon.Tests/Application/BoardCardOrderApiTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/CardListPaginationApiTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/AgentTaskCheckInterpreterTests.cs:40` (none)
- `tests/Antiphon.Tests/Application/AgentTaskCheckInterpreterTests.cs:44` (none)
- `tests/Antiphon.Tests/Application/AgentTaskAutoTitleTests.cs:23` "DiagnoseTitle"
- `tests/Antiphon.Tests/Application/AgentTaskCommitEndpointTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/RepositoryLeaseWaitersWiringTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/CardCorrectionIntegrationTests.cs:32` "CardCorrection"
- `tests/Antiphon.Tests/Application/BootLivenessProbeScopeTests.cs:33` "AgentSessionLaunchFailure"
- `tests/Antiphon.Tests/Application/DelegationScopeHoldTests.cs:28` "AgentQueue"
- `tests/Antiphon.Tests/Application/ScheduleSweepTests.cs:21` (none)
- `tests/Antiphon.Tests/Application/ScheduleSweepTests.cs:25` (none)
- `tests/Antiphon.Tests/Application/AgentCreateRaceTests.cs:30` "AgentQueue"
- `tests/Antiphon.Tests/Application/DispatchConcurrencyPipelineTests.cs:24` "card-0505-advisory-lock"
- `tests/Antiphon.Tests/Application/ProviderQuotaRefusalAcceptanceTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/MutationPipelineTests.cs:25` (none)
- `tests/Antiphon.Tests/Application/MutationPipelineTests.cs:69` (none)
- `tests/Antiphon.Tests/Application/MutationPipelineTests.cs:98` (none)
- `tests/Antiphon.Tests/Application/MutationPipelineTests.cs:124` (none)
- `tests/Antiphon.Tests/Application/AgentAttachHerdrTests.cs:30` (none)
- `tests/Antiphon.Tests/Application/LandEvidenceWarningDeliveryTests.cs:15` "MessageQueue"
- `tests/Antiphon.Tests/Application/GrokRulesLiveMappedDispatchTests.cs:26` "Headed"
- `tests/Antiphon.Tests/Application/PinnedCodexProfileDispatchLaunchTests.cs:27` ["MessageQueue", "AgentQueue"]
- `tests/Antiphon.Tests/Application/CodexCliObservationTests.cs:33` ["MessageQueue", "AgentQueue"]
- `tests/Antiphon.Tests/Application/CapacityRecoveryRefusalTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/CardCommentApiTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/SessionMessageQueueServiceTests.cs:21` "MessageQueue"
- `tests/Antiphon.Tests/Application/RemoteControlModalWatchTests.cs:17` "RemoteControlRecovery"
- `tests/Antiphon.Tests/Application/ReviewEvidenceSettlementTests.cs:17` "MessageQueue"
- `tests/Antiphon.Tests/Application/MatrixChildReceiptTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/PublishedCardCompletionTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/SpecialistExecutionIdentityTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/GrokRulesQueueBarrierTests.cs:13` "MessageQueue"
- `tests/Antiphon.Tests/Application/DispatchConcurrencyAdmissionTests.cs:20` "card-0505-advisory-lock"
- `tests/Antiphon.Tests/Application/CapacityWaitOrphanSweepTests.cs:12` (none)
- `tests/Antiphon.Tests/Application/OutputDistillationMigrationTests.cs:14` (none)
- `tests/Antiphon.Tests/Application/DecisionCardNotifierTests.cs:21` (none)
- `tests/Antiphon.Tests/Application/ChannelReplyDurabilityTests.cs:40` "MessageQueue"
- `tests/Antiphon.Tests/Application/ModelAvailabilityHttpTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/DelegationKindDisplayTests.cs:308` "AgentQueue"
- `tests/Antiphon.Tests/Application/OutputDistillationTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/ExternalTrackerSyncIdentifierTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/PolicyRefreshEndpointTests.cs:15` (none)
- `tests/Antiphon.Tests/Application/PhoneHomeOutageMentionTests.cs:18` "MessageQueue"
- `tests/Antiphon.Tests/Application/PhoneHomeImmediateSendTests.cs:24` "MessageQueue"
- `tests/Antiphon.Tests/Application/TaskInputWeakArmBoundaryTests.cs:26` "AgentQueue"
- `tests/Antiphon.Tests/Application/ChannelOutboundUnifiedTransportTests.cs:28` (none)
- `tests/Antiphon.Tests/Application/ChannelBridgeTests.cs:33` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentTaskStallEscalationTests.cs:27` "AgentQueue"
- `tests/Antiphon.Tests/Application/PostLandMutationWorkflowTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/GrokRulesChannelTests.cs:14` "MessageQueue"
- `tests/Antiphon.Tests/Application/VerificationRoundDeliveryTests.cs:26` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs:22` "AgentQueue"
- `tests/Antiphon.Tests/Application/AgentSessionServiceIntegrationTests.cs:29` "Pty"
- `tests/Antiphon.Tests/Application/AgentTaskListEndpointTests.cs:26` (none)
- `tests/Antiphon.Tests/Application/TaskInputReadFailureTests.cs:23` "AgentQueue"
- `tests/Antiphon.Tests/Application/AgentTaskCheckRemapTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/CardFilePolicyApiTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundPolicyTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/OutputDistillationDeliveryTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/SessionMessageQueuePhoneHomeDropTests.cs:21` "MessageQueue"
- `tests/Antiphon.Tests/Application/SessionMessageQueueBootWedgeTests.cs:28` "MessageQueue"
- `tests/Antiphon.Tests/Application/ComplexityChainRoleHttpTests.cs:14` (none)
- `tests/Antiphon.Tests/Application/GrokRulesReceiptTests.cs:17` "MessageQueue"
- `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.cs:32` "MessageQueue"
- `tests/Antiphon.Tests/Application/TrackerCardStatePushServiceTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/ModelAvailabilityCreateTests.cs:21` (none)
- `tests/Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs:22` "Headed"
- `tests/Antiphon.Tests/Application/DispatcherSweepLifetimeRegistrationTests.cs:14` (none)
- `tests/Antiphon.Tests/Application/DiagnosisEndpointTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/RemoteControlModalAttentionTests.cs:12` "RemoteControlRecovery"
- `tests/Antiphon.Tests/Application/ChannelReplyLateConfirmDispatchTests.cs:31` "MessageQueue"
- `tests/Antiphon.Tests/Application/StandingSessionRecoveryHttpTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/CardFilePrivacyPersistenceTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/AwayDigestNotifierTests.cs:25` (none)
- `tests/Antiphon.Tests/Application/GrokRulesReadyOrderingTests.cs:19` "MessageQueue"
- `tests/Antiphon.Tests/Application/SpecialistInputTransportTests.cs:19` "MessageQueue"
- `tests/Antiphon.Tests/Application/DiagnosticsBundleEndpointTests.cs:11` (none)
- `tests/Antiphon.Tests/Application/AgentTaskInputFallbackTests.cs:38` "AgentQueue"
- `tests/Antiphon.Tests/Application/CardPrivateNotesApiTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/AgentTaskReuseEnqueueTests.cs:25` "AgentQueue"
- `tests/Antiphon.Tests/Application/ChannelOutboundRetryPolicyTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundDeadlineTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/BoardServiceIntegrationTests.cs:24` "Board"
- `tests/Antiphon.Tests/Application/SubscriptionQuotaGateTests.cs:189` (none)
- `tests/Antiphon.Tests/Application/NamedCodexAgentLaunchTests.cs:32` "MessageQueue"
- `tests/Antiphon.Tests/Application/ChannelOutboundDispatchIntegrationTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/AgentTaskCheckSweepTests.cs:38` (none)
- `tests/Antiphon.Tests/Application/AgentTaskCheckSweepTests.cs:42` (none)
- `tests/Antiphon.Tests/Application/AgentTaskStandingAgentDispatchTests.cs:40` "AgentQueue"
- `tests/Antiphon.Tests/Application/AgentTaskStandingAgentDispatchTests.cs:44` "AgentQueue"
- `tests/Antiphon.Tests/Application/ReviewEvidenceConsistencyTests.cs:15` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs:33` (none)
- `tests/Antiphon.Tests/Application/DelegationBriefCeilingPtyTests.cs:41` "Headed"
- `tests/Antiphon.Tests/Application/IncidentPageNotifierTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/CardFilePrivacyHostedServiceTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/AgentTaskRefineTests.cs:28` "AgentQueue"
- `tests/Antiphon.Tests/Application/StandingContinuityAttentionTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/AttentionServiceTests.cs:1114` (none)
- `tests/Antiphon.Tests/Application/AttentionServiceTests.cs:3586` (none)
- `tests/Antiphon.Tests/Application/ExpectationLedgerTests.cs:18` "C650ExpectationLedger"
- `tests/Antiphon.Tests/Application/WorktreeResidueRegistrationTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundComposedTransportTests.cs:26` (none)
- `tests/Antiphon.Tests/Application/StandingSessionSwitchConcurrencyTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/ReadyGateFailureNoticeDeliveryTests.cs:33` (none)
- `tests/Antiphon.Tests/Application/OutputDistillationDeadlineTests.cs:15` (none)
- `tests/Antiphon.Tests/Application/AgentTaskReplyOverlayTests.cs:24` "MessageQueue"
- `tests/Antiphon.Tests/Application/BlockedTaskParkReplyAdmissionTests.cs:22` "MessageQueue"
- `tests/Antiphon.Tests/Application/BlockedTaskNotifierTests.cs:25` (none)
- `tests/Antiphon.Tests/Application/AgentTaskDispatchFailureTests.cs:21` "AgentQueue"
- `tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.cs:45` (none)
- `tests/Antiphon.Tests/Application/SessionRunnerEventPumpTests.cs:25` (none)
- `tests/Antiphon.Tests/Application/AwayDigestProjectionTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/AwayDigestProjectionTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/AgentTaskSettlementRaceTests.cs:27` (none)
- `tests/Antiphon.Tests/Application/RemoteControlRecoveryTests.cs:13` "RemoteControlRecovery"
- `tests/Antiphon.Tests/Application/CardAliasApiTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/StandingRestartAccountingTests.cs:21` (none)
- `tests/Antiphon.Tests/Application/SessionFinishedDuplicateTests.cs:31` "MessageQueue"
- `tests/Antiphon.Tests/Application/ModelAvailabilityDispatcherTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/AgentChannelServiceIntegrationTests.cs:23` "AgentChannel"
- `tests/Antiphon.Tests/Application/MultiRunnerRecoveryTests.cs:190` "MessageQueue"
- `tests/Antiphon.Tests/Application/GrokRulesHerdrAcceptanceTests.cs:23` "Headed"
- `tests/Antiphon.Tests/Application/GrokRulesAutoCompactionCalibrationTests.cs:19` "Headed"
- `tests/Antiphon.Tests/Application/BlockedTaskParkDeliveryTests.cs:27` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentTaskDecisionQuestionApiTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/PinnedAgentKindTests.cs:26` "AgentQueue"
- `tests/Antiphon.Tests/Application/ChannelFollowUpAttachmentTests.cs:31` "MessageQueue"
- `tests/Antiphon.Tests/Application/ModelAvailabilityTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/CodexDelegateDispatchTests.cs:31` "AgentQueue"
- `tests/Antiphon.Tests/Application/StageOutcomeFindingEndpointTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/GrokStartupReadyOrderingTests.cs:22` "MessageQueue"
- `tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/QueuedInputWatchdogTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/QueuedInputWatchdogTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/BlockedTaskSyncRecoveryTests.cs:28` "MessageQueue"
- `tests/Antiphon.Tests/Application/ParkedMessageSweepServiceTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/CapacityRecoveryTaskTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/GrokSignInRuntimeTests.cs:30` ["Headed", "AgentQueue"]
- `tests/Antiphon.Tests/Application/TrackerBidirectionalSyncTests.cs:21` (none)
- `tests/Antiphon.Tests/Application/CapacityRecoveryGrantLivenessTests.cs:13` (none)
- `tests/Antiphon.Tests/Application/OutputDistillationProducerTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/ScheduleCardActionTests.cs:26` (none)
- `tests/Antiphon.Tests/Application/AgentSessionBackendTests.cs:442` "AgentControl"
- `tests/Antiphon.Tests/Application/CapacityRecoveryAcceptanceTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/OrchestratorServiceIntegrationTests.cs:31` (none)
- `tests/Antiphon.Tests/Application/SlashCommandMenuReconciliationTests.cs:27` "Headed"
- `tests/Antiphon.Tests/Application/ExternalTrackerSyncLandingColumnTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/CardReorderIntegrationTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/OutputDistillationApplyRaceTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/PolicyRefreshServiceTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/PolicyRefreshServiceTests.cs:28` (none)
- `tests/Antiphon.Tests/Application/CompletionWarningDeliveryTests.cs:14` "MessageQueue"
- `tests/Antiphon.Tests/Application/ChannelOutboundDiscoveryTests.cs:26` (none)
- `tests/Antiphon.Tests/Application/AgentTaskInputSpillTests.cs:18` "AgentQueue"
- `tests/Antiphon.Tests/Application/ComplexityChainHttpTests.cs:14` (none)
- `tests/Antiphon.Tests/Application/RemoteControlMaintenanceQueueTests.cs:24` "RemoteControlRecovery"
- `tests/Antiphon.Tests/Application/ScheduleEndpointsTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/PolledCompletionNoteShrinkTests.cs:15` "MessageQueue"
- `tests/Antiphon.Tests/Application/WorkflowTrackerActivationTests.cs:19` "Board"
- `tests/Antiphon.Tests/Application/FileSystemBrowseApiTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/CardWorktreeMirrorBindingTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/HealthEndpointTests.cs:13` (none)
- `tests/Antiphon.Tests/Application/GrokRulesLaunchRefusalTests.cs:23` "AgentControl"
- `tests/Antiphon.Tests/Application/SessionMessageQueueWedgedHeadTests.cs:19` "MessageQueue"
- `tests/Antiphon.Tests/Application/OutboundConversionTaskTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/SessionHealthTests.cs:24` "AgentSupervision"
- `tests/Antiphon.Tests/Application/CardSearchApiTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/HerdrPlacementPreflightTests.cs:16` "AgentControl"
- `tests/Antiphon.Tests/Application/BoardProjectArchiveApiTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/StandingSessionQueueSwitchTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/ReviewEvidenceResettlementTests.cs:24` "MessageQueue"
- `tests/Antiphon.Tests/Application/CapacityRecoveryAttentionTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/ModelAvailabilityManualTests.cs:21` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundMaterializationTests.cs:25` (none)
- `tests/Antiphon.Tests/Application/CardWorkTransitionServiceTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/CardWorkTransitionServiceTests.cs:31` (none)
- `tests/Antiphon.Tests/Application/QuotaEvidenceWireTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/ExpectationResponseTests.cs:14` "MessageQueue"
- `tests/Antiphon.Tests/Application/PipelineHandoffPilotTests.cs:27` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentTaskMidTurnRefineTests.cs:27` "AgentQueue"
- `tests/Antiphon.Tests/Application/BoardProjectArchiveTests.cs:22` "Board"
- `tests/Antiphon.Tests/Application/HomeTaskServiceIntegrationTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/ProjectCascadeForeignKeyTests.cs:19` "Board"
- `tests/Antiphon.Tests/Application/AgentPinnedInstructionEndpointTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/BoardCardOrderIntegrationTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/CapacityRecoveryPersistenceTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/CardFilePrivacySyncAcceptanceTests.cs:13` (none)
- `tests/Antiphon.Tests/Application/CodexCliObservationGapTests.cs:20` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentControlServiceIntegrationTests.cs:26` "AgentControl"
- `tests/Antiphon.Tests/Application/TrackerSyncEndpointTests.cs:25` (none)
- `tests/Antiphon.Tests/Application/ProjectReadinessTests.cs:415` (none)
- `tests/Antiphon.Tests/Application/ChannelPreamblePresetEndpointTests.cs:10` (none)
- `tests/Antiphon.Tests/Application/SessionGenerationExitTests.cs:27` "Pty"
- `tests/Antiphon.Tests/Application/ChannelOutboundMetadataRepairTests.cs:26` (none)
- `tests/Antiphon.Tests/Application/TaskProgressStallSweepTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/TaskProgressStallSweepTests.cs:27` (none)
- `tests/Antiphon.Tests/Application/SessionMessageQueueInterruptedAttemptTests.cs:20` "MessageQueue"
- `tests/Antiphon.Tests/Application/PhoneHomeStrandedQueueTests.cs:32` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentTaskAgentKindTests.cs:27` "AgentQueue"
- `tests/Antiphon.Tests/Application/CardTrackerPushApiTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/DistillationEndpointTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/WallRerouteDispatchTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/CompactionRecoveryTests.cs:24` "MessageQueue"
- `tests/Antiphon.Tests/Application/ResponseCompressionIntegrationTests.cs:15` (none)
- `tests/Antiphon.Tests/Application/AgentNamePinTests.cs:25` "AgentQueue"
- `tests/Antiphon.Tests/Application/AgentSystemPromptLaunchTests.cs:29` "MessageQueue"
- `tests/Antiphon.Tests/Application/AppHostWatchdogStateAttentionServiceTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/AgentTaskAnswerTests.cs:18` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentModelLevelBindTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/AgentTaskScopedListEndpointTests.cs:15` (none)
- `tests/Antiphon.Tests/Application/TaskInputAttentionCostTests.cs:24` "AgentQueue"
- `tests/Antiphon.Tests/Application/CardReorderApiTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/WorktreeResidueEndpointTests.cs:9` (none)
- `tests/Antiphon.Tests/Application/PipelineDefinitionEndpointTests.cs:15` (none)
- `tests/Antiphon.Tests/Application/SessionMessageQueueGrokPtyIntegrationTests.cs:26` "Headed"
- `tests/Antiphon.Tests/Application/TranscriptHotPathMigrationTests.cs:17` "TranscriptHotPathMigrations"
- `tests/Antiphon.Tests/Application/AgentTaskOverdueDeadlineTests.cs:40` (none)
- `tests/Antiphon.Tests/Application/AgentTaskOverdueDeadlineTests.cs:1152` (none)
- `tests/Antiphon.Tests/Application/AgentTaskOverdueDeadlineTests.cs:1155` (none)
- `tests/Antiphon.Tests/Application/AgentTaskOverdueDeadlineTests.cs:1162` (none)
- `tests/Antiphon.Tests/Application/ChannelConsumerIdentityEndpointTests.cs:13` (none)
- `tests/Antiphon.Tests/Application/ClosedCardSweepApiTests.cs:11` (none)
- `tests/Antiphon.Tests/Application/CardThreadEndpointTests.cs:26` (none)
- `tests/Antiphon.Tests/Application/TrackerSyncNotifierTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/DispatchConcurrencyDispatchTests.cs:21` "card-0505-advisory-lock"
- `tests/Antiphon.Tests/Application/EffectiveSettingsPipelineTests.cs:24` "card-0505-advisory-lock"
- `tests/Antiphon.Tests/Application/AgentSessionLaunchQueueOwnershipTests.cs:23` "AgentSessionLaunchQueueOwnership"
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:23` "C691Census"
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:76` "C691Census"
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:95` "C691Census"
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:118` "C691Census"
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:177` "C691Census"
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:198` "C691Census"
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:222` "C691Census"
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:240` "C691Census"
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:263` "C691Census"
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:283` "C691Census"
- `tests/Antiphon.Tests/Application/ExternalTrackerSyncImportanceProvenanceTests.cs:15` (none)
- `tests/Antiphon.Tests/Application/AgentTaskLandContractEndpointTests.cs:19` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundDurabilitySchemaTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/GrokRulesResumeMigrationTests.cs:22` "AgentQueue"
- `tests/Antiphon.Tests/Application/ComplexityChainConcurrencyHttpTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/ChannelPromptCorrelationTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/ComplexityEffectiveHttpTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/ReviewReplyDispatcherTests.cs:20` "MessageQueue"
- `tests/Antiphon.Tests/Application/OrchestratorInstructionsRefreshTests.cs:29` "MessageQueue"
- `tests/Antiphon.Tests/Application/DataRetentionServiceTests.cs:35` (none)
- `tests/Antiphon.Tests/Application/AgentTaskServiceIntegrationTests.cs:23` "AgentQueue"
- `tests/Antiphon.Tests/Application/CardCorrectionApiTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/HerdrPaneDisposalApplicationTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/CardReviewServiceIntegrationTests.cs:24` "CardReview"
- `tests/Antiphon.Tests/Application/AgentTaskDecisionQuestionIntegrationTests.cs:19` "AgentQueue"
- `tests/Antiphon.Tests/Application/GrokRulesNativeReadWireTests.cs:19` "Headed"
- `tests/Antiphon.Tests/Application/AgentTaskInternalDecisionLifecycleTests.cs:18` "AgentQueue"
- `tests/Antiphon.Tests/Application/ReviewEvidenceDeliveryTests.cs:15` "MessageQueue"
- `tests/Antiphon.Tests/Application/SessionGenerationDeliveryOverlapTests.cs:17` "SessionGenerationDeliveryOverlap"
- `tests/Antiphon.Tests/Application/ExpectationDirectDeliveryTests.cs:20` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentSupervisionTests.cs:34` (none)
- `tests/Antiphon.Tests/Application/TranscriptBindingIncidentTests.cs:29` "TranscriptBindingIncidents"
- `tests/Antiphon.Tests/Application/CapacityRecoveryQueueTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/CardServiceTrackerPushTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/PhoneHomePendingInventoryTests.cs:18` "MessageQueue"
- `tests/Antiphon.Tests/Application/OutputDistillationCleanupTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/CardFilePrivacyConcurrencyTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/DelegateCheckProbeTests.cs:33` "AgentQueue"
- `tests/Antiphon.Tests/Application/CardDiagnosisSweepTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/WatchdogServiceTests.cs:21` "Watchdog"
- `tests/Antiphon.Tests/Application/GrokRulesDispatchAcceptanceTests.cs:25` "Headed"
- `tests/Antiphon.Tests/Application/AlertRoutingTests.cs:23` "AlertRouting"
- `tests/Antiphon.Tests/Application/SessionMessageQueueSpillTests.cs:23` "MessageQueue"
- `tests/Antiphon.Tests/Application/ChannelOutboundFailureRecordingTests.cs:23` (none)
- `tests/Antiphon.Tests/Application/CheckNoteDeliveryHandoffTests.cs:57` "MessageQueue"
- `tests/Antiphon.Tests/Application/SessionMessageQueueSupervisionTests.cs:21` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs:943` (none)
- `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs:946` (none)
- `tests/Antiphon.Tests/Application/StandingSessionOwnershipTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/PinnedProfileLaunchSpecTests.cs:25` "AgentQueue"
- `tests/Antiphon.Tests/Application/ChannelMachineTurnTextTests.cs:25` "MessageQueue"
- `tests/Antiphon.Tests/Application/PhoneHomeQueuedTurnTests.cs:22` "MessageQueue"
- `tests/Antiphon.Tests/Application/CardFilePrivacyMigrationTests.cs:14` (none)
- `tests/Antiphon.Tests/Application/AgentTaskDeadSessionReconciliationTests.cs:37` (none)
- `tests/Antiphon.Tests/Application/AgentTaskDeadSessionReconciliationTests.cs:44` (none)
- `tests/Antiphon.Tests/Application/StandingContinuityRecoveryTests.cs:15` (none)
- `tests/Antiphon.Tests/Application/FileSystemBrowseMockedTests.cs:17` (none)
- `tests/Antiphon.Tests/Application/AgentTaskRoleBindingTests.cs:22` (none)
- `tests/Antiphon.Tests/Application/AgentTaskRoleBindingTests.cs:43` (none)
- `tests/Antiphon.Tests/Application/AgentTaskRoleBindingTests.cs:58` (none)
- `tests/Antiphon.Tests/Application/ApiErrorRecoveryServiceTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/ApiErrorRecoveryServiceTests.cs:24` (none)
- `tests/Antiphon.Tests/Application/SubscriptionUsageMonitorTests.cs:28` (none)
- `tests/Antiphon.Tests/Application/AgentTaskPoolTests.cs:28` (none)
- `tests/Antiphon.Tests/Application/AgentReplyStyleEndpointTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/RemoteControlRecoveryPtyIntegrationTests.cs:20` "RemoteControlRecovery"
- `tests/Antiphon.Tests/Application/ChannelOutboundEndpointTests.cs:16` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundRetentionTests.cs:26` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundUnifiedPathTests.cs:20` (none)
- `tests/Antiphon.Tests/Application/RolePolicyToolBundleDispatchTests.cs:39` "AgentQueue"
- `tests/Antiphon.Tests/Application/AgentTaskCheckScheduleTests.cs:30` (none)
- `tests/Antiphon.Tests/Application/AgentTaskCheckScheduleTests.cs:34` (none)
- `tests/Antiphon.Tests/Application/AgentSessionInterruptedLaunchResumeTests.cs:26` "AgentSessionInterruptedLaunchResume"
- `tests/Antiphon.Tests/Application/CapacityRecoverySupervisionTests.cs:18` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundUnifiedCrashTests.cs:14` (none)
- `tests/Antiphon.Tests/Application/ChannelOutboundTrailingRecoveryTests.cs:21` (none)
- `tests/Antiphon.Tests/Application/SessionReconciliationServiceTests.cs:32` (none)
- `tests/Antiphon.Tests/Application/StandingBootQueueWitnessTests.cs:33` (none)
- `tests/Antiphon.Tests/Application/CardIdentifierResolutionTests.cs:28` (none)
- `tests/Antiphon.Tests/Application/CardSpawnModelArgumentTests.cs:22` "AgentControl"
- `tests/Antiphon.Tests/Application/HerdrSupervisionAttentionTests.cs:13` (none)
- `tests/Antiphon.Tests/Application/CommitOnSettlePolicyTests.cs:18` "AgentQueue"
- `tests/Antiphon.Tests/Application/BlockedTaskParkResumeTests.cs:20` "MessageQueue"
- `tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs:44` "AgentSessionLaunchFailure"
- `tests/Antiphon.Tests/Checkpoints/TimingJournalTests.cs:9` (none)
- `tests/Antiphon.Tests/Checkpoints/TestSetTimingTests.cs:9` (none)
- `tests/Antiphon.Tests/Agents/CodexCommandLengthSessionTests.cs:21` "RealCliStubProxy"
- `tests/Antiphon.Tests/Agents/SpecialistToolPolicyTests.cs:27` ["Headed", "RealCliStubProxy"]
- `tests/Antiphon.Tests/Agents/CodexBootWedgeProbeTests.cs:24` "RealCliStubProxy"
- `tests/Antiphon.Tests/Agents/ClaudeAdapterIntegrationTests.cs:12` "Headed"
- `tests/Antiphon.Tests/Agents/GrokRealCliStubProxyCanaryTests.cs:24` "RealCliStubProxy"
- `tests/Antiphon.Tests/Agents/RawPtyAdapterTests.cs:14` "Pty"
- `tests/Antiphon.Tests/Agents/Card1277PtyHostFixtureTests.cs:20` nameof(Card1277PtyHostFixtureTests)
- `tests/Antiphon.Tests/Agents/CodexAdapterIntegrationTests.cs:14` "Headed"
- `tests/Antiphon.Tests/Agents/CodexHerdrRealCliStubProxyCanaryTests.cs:31` "RealCliStubProxy"
- `tests/Antiphon.Tests/Agents/GrokHerdrRealCliStubProxyCanaryTests.cs:27` "RealCliStubProxy"
- `tests/Antiphon.Tests/Agents/CodexAdapterLocalShellTests.cs:32` "Pty"
- `tests/Antiphon.Tests/Agents/ClaudeRealCliStubProxyCanaryTests.cs:24` "RealCliStubProxy"
- `tests/Antiphon.Tests/Agents/Card0168InteractiveProbeTests.cs:22` "RealCliStubProxy"
- `tests/Antiphon.Tests/Agents/SessionRunnerRuntimeTests.cs:13` "Pty"
- `tests/Antiphon.Tests/Agents/ClaudeHerdrRealCliStubProxyCanaryTests.cs:27` "RealCliStubProxy"
- `tests/Antiphon.Tests/Agents/CodexRealCliStubProxyCanaryTests.cs:22` "RealCliStubProxy"
- `tests/Antiphon.Tests/Agents/ClaudeAdapterLocalShellTests.cs:28` "Pty"
- `tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs:75` "Headed"
- `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycleTests.cs:112` (none)
- `tests/Antiphon.Tests/Api/KeepSixShortfallTests.cs:23` "KeepSixShortfall"
- `tests/Antiphon.Tests/Api/ExpectationHoldReleaseEndpointTests.cs:34` "MessageQueue"
- `tests/Antiphon.Tests/Api/KeepSixReminderTests.cs:23` "KeepSixReminder"
- `tests/Antiphon.Tests/Api/OrchestratorInstructionsEndpointTests.cs:18` "OrchestratorInstructionsEndpoints"
- `tests/Antiphon.Tests/Api/KeepSixSettingsTests.cs:21` "KeepSixSettings"
- `tests/Antiphon.Tests/Api/OperatorDashboardLoginTests.cs:18` (none)
- `tests/Antiphon.Tests/Api/RolePolicyEndpointTests.cs:23` "RolePolicyEndpoints"
- `tests/Antiphon.Tests/AgentTui/AgentTuiSecretMigratorTests.cs:17` (none)
- `tests/Antiphon.Tests/AgentTui/AgentTuiDiscoveryTests.cs:23` (none)
- `tests/Antiphon.Tests/AgentTui/AgentTuiProfileImporterBackfillTests.cs:16` "AgentTuiProfileImporterBackfill"
- `tests/Antiphon.Tests/AgentTui/AgentTuiProfileConcurrencyTests.cs:24` (none)
- `tests/Antiphon.Tests/AgentTui/AgentTuiProfileServiceTests.cs:27` "AgentTuiProfileServiceSchema"
- `tests/Antiphon.Tests/AgentTui/AgentTuiPersistenceTests.cs:430` (none)
- `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs:19` "RunnerProcessProbe"
- `tests/Antiphon.Tests/AgentTui/AgentTuiApiTests.cs:23` (none)
- `tests/Antiphon.Tests/ApiKeys/DelegationCapabilityApiTests.cs:14` (none)
- `tests/Antiphon.Tests/ApiKeys/ApiKeyApiTests.cs:23` (none)
- `tests/Antiphon.Tests/Infrastructure/HangfireStartupSafetyTests.cs:29` (none)
- `tests/Antiphon.Tests/Infrastructure/ExpectationWatchdogJobTests.cs:27` (none)
- `tests/Antiphon.Tests/Infrastructure/Resilience/ResiliencePredicateTests.cs:15` "antiphon-resilience-meter"
- `tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceBudgetTests.cs:12` "antiphon-resilience-meter"
- `tests/Antiphon.Tests/Infrastructure/Resilience/HttpReadResilienceTests.cs:12` "antiphon-resilience-meter"
- `tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceCircuitTests.cs:15` "antiphon-resilience-meter"
- `tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceOptionsAndTelemetryTests.cs:15` "antiphon-resilience-meter"
- `tests/Antiphon.SessionRunner.Tests/RunnerChildClaudeTokenInheritanceTests.cs:20` (none)
- `tests/Antiphon.SessionRunner.Tests/RunnerChildClaudeTokenInheritanceTests.cs:22` (none)
- `tests/Antiphon.SessionRunner.Tests/HerdrAdoptionSweepTests.cs:16` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/HerdrLabelFollowLiveTests.cs:13` "Headed"
- `tests/Antiphon.SessionRunner.Tests/TerminalSeatReleaseTests.cs:23` "ClaudeConfigDirEnv"
- `tests/Antiphon.SessionRunner.Tests/BlockedParkWireTests.cs:26` "ClaudeConfigDirEnv"
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerObservationTests.cs:9` "ClaudeConfigDirEnv"
- `tests/Antiphon.SessionRunner.Tests/RemoteControlConditionalInputTests.cs:10` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/HerdrAttachTests.cs:17` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalStopRegressionTests.cs:8` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/GrokRulesRunnerRefusalTests.cs:14` "HerdrLaunchShape"
- `tests/Antiphon.SessionRunner.Tests/HerdrGrokNativeSessionLiveTests.cs:17` "Headed"
- `tests/Antiphon.SessionRunner.Tests/CompactionContinuationStopTests.cs:10` "ClaudeConfigDirEnv"
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs:17` "ClaudeConfigDirEnv"
- `tests/Antiphon.SessionRunner.Tests/TranscriptIngestionLatencyProbeTests.cs:14` "ClaudeConfigDirEnv"
- `tests/Antiphon.SessionRunner.Tests/GrokRulesHttpAcceptanceTests.cs:16` "Headed"
- `tests/Antiphon.SessionRunner.Tests/C1022BackendLaunchTests.cs:15` (none)
- `tests/Antiphon.SessionRunner.Tests/SessionCpuWatchdogTests.cs:18` "ClaudeConfigDirEnv"
- `tests/Antiphon.SessionRunner.Tests/HerdrStatusPushTests.cs:18` "HerdrStatusPush"
- `tests/Antiphon.SessionRunner.Tests/SessionLivenessTests.cs:16` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/HerdrLaunchShapeTests.cs:17` "HerdrLaunchShape"
- `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests.cs:9` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/RunnerMultilinePromptDeliveryTests.cs:16` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/FirstWriteRaceTests.cs:16` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/RunnerSessionGenerationTests.cs:14` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/HerdrNamedTabPlacementLiveTests.cs:14` "Headed"
- `tests/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests.cs:9` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/RunnerRestartPreflightCacheTests.cs:8` (none)
- `tests/Antiphon.SessionRunner.Tests/RunnerPlatformLaunchTests.cs:14` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/C1022BackendCapabilitiesTests.cs:14` (none)
- `tests/Antiphon.SessionRunner.Tests/CodexCommandLengthHttpAcceptanceTests.cs:15` "Headed"
- `tests/Antiphon.SessionRunner.Tests/TranscriptAdoptionSafetyTests.cs:25` "ClaudeConfigDirEnv"
- `tests/Antiphon.SessionRunner.Tests/SessionBufferBoundsTests.cs:33` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/PtyBackendSeamTests.cs:32` (none)
- `tests/Antiphon.SessionRunner.Tests/PtyBackendSeamTests.cs:34` (none)
- `tests/Antiphon.SessionRunner.Tests/CodexCliVersionProbeTests.cs:17` (none)
- `tests/Antiphon.SessionRunner.Tests/CodexCliVersionProbeTests.cs:148` (none)
- `tests/Antiphon.SessionRunner.Tests/HerdrPaneChildKillTests.cs:16` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/WorkspaceSourceVerificationWireTests.cs:11` "ClaudeConfigDirEnv"
- `tests/Antiphon.SessionRunner.Tests/PtyHostAdoptionTests.cs:18` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalGuardedLiveTests.cs:13` "Headed"
- `tests/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidenceRuntimeTests.cs:20` "SessionLiveness"
- `tests/Antiphon.SessionRunner.Tests/HerdrNamedTabPlacementTests.cs:12` "HerdrNamedTabPlacement"
- `tests/Antiphon.SessionRunner.Tests/HerdrRunnerSessionTests.cs:14` "HerdrRunnerSession"
- `tests/Antiphon.SessionRunner.Tests/GrokRulesAdoptionTests.cs:13` "SessionLiveness"

## Appendix B. Every `SetEnvironmentVariable` in the two test projects

88 lines. Process-wide, so they matter inside one `dotnet run` and not between a gate and another task, except where the value names a shared directory. The only shared directory these tests delete is `/tmp/grok-download`, and that delete is `Directory.Delete`, not an environment variable.

- `tests/Antiphon.Tests/SmokeTests.cs:44` `Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",`
- `tests/Antiphon.Tests/SmokeTests.cs:46` `Environment.SetEnvironmentVariable("GitHub__Enabled", "false");`
- `tests/Antiphon.Tests/SmokeTests.cs:47` `Environment.SetEnvironmentVariable("Llm__DefaultProvider", "disabled");`
- `tests/Antiphon.Tests/SmokeTests.cs:48` `Environment.SetEnvironmentVariable("Llm__DefaultModel", "disabled-model");`
- `tests/Antiphon.Tests/SmokeTests.cs:49` `Environment.SetEnvironmentVariable("Llm__Providers__anthropic__ApiKey", " ");`
- `tests/Antiphon.Tests/SmokeTests.cs:50` `Environment.SetEnvironmentVariable("Llm__Providers__openai__ApiKey", " ");`
- `tests/Antiphon.Tests/SmokeTests.cs:51` `Environment.SetEnvironmentVariable("Llm__Providers__ollama__ApiKey", " ");`
- `tests/Antiphon.Tests/SmokeTests.cs:63` `Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);`
- `tests/Antiphon.Tests/SmokeTests.cs:64` `Environment.SetEnvironmentVariable("GitHub__Enabled", null);`
- `tests/Antiphon.Tests/SmokeTests.cs:65` `Environment.SetEnvironmentVariable("Llm__DefaultProvider", null);`
- `tests/Antiphon.Tests/SmokeTests.cs:66` `Environment.SetEnvironmentVariable("Llm__DefaultModel", null);`
- `tests/Antiphon.Tests/SmokeTests.cs:67` `Environment.SetEnvironmentVariable("Llm__Providers__anthropic__ApiKey", null);`
- `tests/Antiphon.Tests/SmokeTests.cs:68` `Environment.SetEnvironmentVariable("Llm__Providers__openai__ApiKey", null);`
- `tests/Antiphon.Tests/SmokeTests.cs:69` `Environment.SetEnvironmentVariable("Llm__Providers__ollama__ApiKey", null);`
- `tests/Antiphon.Tests/Application/TrackerTokenResolverTests.cs:71` `Environment.SetEnvironmentVariable(envName, "env-token-value");`
- `tests/Antiphon.Tests/Application/TrackerTokenResolverTests.cs:85` `Environment.SetEnvironmentVariable(envName, null);`
- `tests/Antiphon.Tests/Application/GrokNativeSessionResumeTests.cs:116` `Environment.SetEnvironmentVariable("GROK_HOME", seeded);`
- `tests/Antiphon.Tests/Application/GrokNativeSessionResumeTests.cs:124` `Environment.SetEnvironmentVariable("GROK_HOME", previous);`
- `tests/Antiphon.Tests/Application/AgentAttachHerdrTests.cs:510` `Environment.SetEnvironmentVariable("GROK_HOME", home);`
- `tests/Antiphon.Tests/Application/AgentAttachHerdrTests.cs:519` `Environment.SetEnvironmentVariable("GROK_HOME", home);`
- `tests/Antiphon.Tests/Application/AgentAttachHerdrTests.cs:695` `public void Dispose() => Environment.SetEnvironmentVariable("GROK_HOME", previous);`
- `tests/Antiphon.Tests/Checkpoints/TestSetTimingTests.cs:89` `Environment.SetEnvironmentVariable("ANTIPHON_TEST_TIMING_ROOT", root);`
- `tests/Antiphon.Tests/Checkpoints/TestSetTimingTests.cs:108` `Environment.SetEnvironmentVariable("ANTIPHON_TEST_TIMING_ROOT", previous);`
- `tests/Antiphon.Tests/Agents/SpecialistToolPolicyTests.cs:79` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", config);`
- `tests/Antiphon.Tests/Agents/SpecialistToolPolicyTests.cs:317` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfig);`
- `tests/Antiphon.Tests/Agents/ClaudeAdapterEffortPromptTests.cs:70` `Environment.SetEnvironmentVariable("ANTIPHON_PTY_BACKEND", "modern");`
- `tests/Antiphon.Tests/Agents/ClaudeAdapterEffortPromptTests.cs:106` `Environment.SetEnvironmentVariable("ANTIPHON_PTY_BACKEND", prior);`
- `tests/Antiphon.Tests/Agents/ClaudeRealCliStubProxyCanaryTests.cs:175` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);`
- `tests/Antiphon.Tests/Agents/ClaudeRealCliStubProxyCanaryTests.cs:285` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDir);`
- `tests/Antiphon.Tests/Agents/Card0168InteractiveProbeTests.cs:45` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);`
- `tests/Antiphon.Tests/Agents/Card0168InteractiveProbeTests.cs:98` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDir);`
- `tests/Antiphon.Tests/Agents/ClaudeHerdrRealCliStubProxyCanaryTests.cs:48` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);`
- `tests/Antiphon.Tests/Agents/ClaudeHerdrRealCliStubProxyCanaryTests.cs:172` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDir);`
- `tests/Antiphon.Tests/TestHelpers/PtyBackendEnvGuard.cs:24` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, null);`
- `tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs:62` `Environment.SetEnvironmentVariable(BaseUrlEnvVar, DeadRunnerBaseUrl);`
- `tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs:63` `Environment.SetEnvironmentVariable(CheckInterpreterEnvVar, "false");`
- `tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs:64` `Environment.SetEnvironmentVariable(DiagnoseEnvVar, "false");`
- `tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs:65` `Environment.SetEnvironmentVariable(OutputDistillerEnvVar, "false");`
- `tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs:66` `Environment.SetEnvironmentVariable(HangfireServerEnabledEnvVar, "false");`
- `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs:33` `Environment.SetEnvironmentVariable(hostCanaryName, "synthetic-host-only-value");`
- `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs:45` `Environment.SetEnvironmentVariable(hostCanaryName, previous);`
- `tests/Antiphon.SessionRunner.Tests/HostStatsProbeParseTests.cs:45` `Environment.SetEnvironmentVariable(prefix + key, value);`
- `tests/Antiphon.SessionRunner.Tests/HostStatsProbeParseTests.cs:55` `Environment.SetEnvironmentVariable(prefix + key, null);`
- `tests/Antiphon.SessionRunner.Tests/RunnerChildClaudeTokenInheritanceTests.cs:42` `Environment.SetEnvironmentVariable(TokenVariable, sentinel);`
- `tests/Antiphon.SessionRunner.Tests/RunnerChildClaudeTokenInheritanceTests.cs:73` `Environment.SetEnvironmentVariable(TokenVariable, previous);`
- `tests/Antiphon.SessionRunner.Tests/TerminalSeatReleaseTests.cs:1755` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _root);`
- `tests/Antiphon.SessionRunner.Tests/TerminalSeatReleaseTests.cs:1852` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _oldClaudeConfig);`
- `tests/Antiphon.SessionRunner.Tests/BlockedParkWireTests.cs:1437` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _root);`
- `tests/Antiphon.SessionRunner.Tests/BlockedParkWireTests.cs:1534` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _oldClaudeConfig);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerObservationTests.cs:110` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerObservationTests.cs:123` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _previousConfig);`
- `tests/Antiphon.SessionRunner.Tests/CompactionContinuationStopTests.cs:150` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);`
- `tests/Antiphon.SessionRunner.Tests/CompactionContinuationStopTests.cs:180` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _previousConfig);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs:33` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs:61` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs:86` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs:122` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs:145` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs:178` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs:207` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs:243` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptIngestionLatencyProbeTests.cs:35` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", root);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptIngestionLatencyProbeTests.cs:70` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previous);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptIngestionLatencyProbeTests.cs:110` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", root);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptIngestionLatencyProbeTests.cs:161` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previous);`
- `tests/Antiphon.SessionRunner.Tests/C1022BackendLaunchTests.cs:46` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, ambient);`
- `tests/Antiphon.SessionRunner.Tests/C1022BackendLaunchTests.cs:125` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, previous);`
- `tests/Antiphon.SessionRunner.Tests/SessionCpuWatchdogTests.cs:135` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);`
- `tests/Antiphon.SessionRunner.Tests/SessionCpuWatchdogTests.cs:171` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);`
- `tests/Antiphon.SessionRunner.Tests/SessionCpuWatchdogTests.cs:209` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);`
- `tests/Antiphon.SessionRunner.Tests/PtyBackendEnvGuard.cs:24` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, null);`
- `tests/Antiphon.SessionRunner.Tests/RunnerRestartPreflightCacheTests.cs:287` `Environment.SetEnvironmentVariable("PATH", first + Path.PathSeparator + second);`
- `tests/Antiphon.SessionRunner.Tests/RunnerRestartPreflightCacheTests.cs:306` `Environment.SetEnvironmentVariable("PATH", previous);`
- `tests/Antiphon.SessionRunner.Tests/C1022BackendCapabilitiesTests.cs:49` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar,`
- `tests/Antiphon.SessionRunner.Tests/C1022BackendCapabilitiesTests.cs:72` `finally { Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, previous); }`
- `tests/Antiphon.SessionRunner.Tests/C1022BackendCapabilitiesTests.cs:81` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, "inbox");`
- `tests/Antiphon.SessionRunner.Tests/C1022BackendCapabilitiesTests.cs:84` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, "modern");`
- `tests/Antiphon.SessionRunner.Tests/C1022BackendCapabilitiesTests.cs:90` `finally { Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, previous); }`
- `tests/Antiphon.SessionRunner.Tests/TranscriptAdoptionSafetyTests.cs:2030` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _configDir);`
- `tests/Antiphon.SessionRunner.Tests/TranscriptAdoptionSafetyTests.cs:2045` `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);`
- `tests/Antiphon.SessionRunner.Tests/PtyBackendSeamTests.cs:50` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, "modern");`
- `tests/Antiphon.SessionRunner.Tests/PtyBackendSeamTests.cs:66` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, restore);`
- `tests/Antiphon.SessionRunner.Tests/PtyBackendSeamTests.cs:102` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, "inbox");`
- `tests/Antiphon.SessionRunner.Tests/PtyBackendSeamTests.cs:115` `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, restore);`
- `tests/Antiphon.SessionRunner.Tests/CodexCliVersionProbeTests.cs:530` `Environment.SetEnvironmentVariable(key, key == "CODEX_HOME" ? Path.Combine(root, "parent-home") : "C959-env-canary");`
- `tests/Antiphon.SessionRunner.Tests/CodexCliVersionProbeTests.cs:537` `foreach (var pair in _previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);`
- `tests/Antiphon.SessionRunner.Tests/ClaudeAuthProbeTests.cs:94` `Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sentinel-api-key-value");`
- `tests/Antiphon.SessionRunner.Tests/ClaudeAuthProbeTests.cs:105` `Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousKey);`
