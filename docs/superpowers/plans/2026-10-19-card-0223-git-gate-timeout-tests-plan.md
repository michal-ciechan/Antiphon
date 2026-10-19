# CARD-0223: dedicated tests for the git-gate kill-on-timeout and the runner list deadline

Card: CARD-0223 (follow-up to CARD-0216 S3/S4; human-rated; master-only; test-coverage).
Code base: `origin/master` 6ec71d427574f3a1b74bfbe2042e0ea9a819d191. The card named
a0f3c4f3e728655933da1782981a88fb2b7b3a56; every file this plan touches or cites is
byte-identical between the two (`git diff --stat` empty), so the ground truth below holds
for both. Task branch `feat/card-task-938fe7a9` is fast-forward from
a60f9f058c88c33c26461c62e119dc4e55b544ef, which is on master.
Prior plan: `docs/superpowers/plans/2026-08-27-card-0216-remote-home-load-and-post-load-spinner-plan.md`
§2.3 and the S3/S4 rows of its slice table (the two tests the build skipped).
Lane: Linux. `GET /api/runner-defaults` names `server2` as the global default; at read
time `server2` is draining and `server2-temp` is accepting. The Code brief omits `-Runner`
and `-Platform`; the checkpoint rows below are Linux rows.

Outcome of the premise check: **the card is not stale, but it is half-covered.** Behaviour 1
(kill the timed-out git child) exists exactly as described and has no test. Behaviour 2 (the
agent list's runner read is bounded by `SessionRunner:ListTimeoutSeconds`) exists, was
reshaped by CARD-1235 on 2026-10-15 into one deadline over per-owner reads, and the
*remote-owner* arm already has a dedicated hung-runner test. The *local-client* arm, the
literal thing the card asks for (`ISessionRunnerClient.ListAsync` never completing), has no
test. Plan: two test methods, no product code. Do not close the card.

## Ground truth

| Card assumes | Code does (origin/master 6ec71d427) | Consequence for the plan |
|---|---|---|
| `GitWorkspaceService.RunAsync` catches `OperationCanceledException`, calls `TryKill`, returns `(-1, "", "timeout")`. | `RunAsync` is a thin `protected internal virtual` over private `RunCoreAsync` (`server/Application/Services/GitWorkspaceService.cs:1123-1197`). The OCE catch calls `TryKill(process)` (`Kill(entireProcessTree: true)`, exceptions swallowed), rethrows when the caller's own token is cancelled, logs a warning, then `WaitForExitBounded(process, 2000)` (added by CARD-0543, 2339af1c4) and returns `(-1, "", "timeout")`. | Behaviour exists. `InternalsVisibleTo("Antiphon.Tests")` (`server/Antiphon.Server.csproj:12`) lets the test call `RunAsync` directly with arbitrary args, so the sleeper's args are the test's, not git's. |
| The timeout and executable are configurable so a test can point the service at a sleeping script with a 1 s deadline. | `GitSettings.ExecutableName` (default `git`) and `GitSettings.TimeoutSeconds` (default 15, floored to 1 by `Math.Max(1, …)`) at `server/Application/Settings/GitSettings.cs:6-7`; constructor takes `IOptions<GitSettings>?` and `GitProcessGate?` (lines 27-39). | The seam is there. 1 s is the smallest deadline the product accepts. |
| No test proves the child OS process is terminated. | No test in `tests/` constructs `GitWorkspaceService` with a non-git `ExecutableName` or a short `TimeoutSeconds`. `WorkspaceInfoGitIntegrationTests` (`tests/Antiphon.Tests/Application/WorkspaceInfoTests.cs:73-215`) covers the gate's `PeakInFlight`/`Started` and in-flight dedupe only. | Test 1 is new coverage. |
| The product uses a test-clock seam the test could drive. | `RunCoreAsync` uses `CancellationTokenSource.CancelAfter(timeout)` on a linked CTS with no `TimeProvider`; the constructor takes none. | The 1 s deadline is a real timer. No product change is allowed, so the test pays ~1 s and polls, it never sleeps (D-3). |
| `AgentService.AttachRunnerLiveStateAsync` wraps `_runnerClient.ListAsync` in a linked CTS with `ListTimeoutSeconds` and swallows the timeout OCE. | `AttachRunnerLiveStateAsync` (`server/Application/Services/AgentService.cs:360-388`) creates one linked CTS with `CancelAfter(Math.Max(1, ListTimeoutSeconds))` (line 368), groups live sessions by `RemoteRunnerId`, and awaits `ReadOwnerAsync` per owner. `ReadOwnerAsync` (lines 390-421) awaits `_runnerClient.ListAsync(timeout)` (line 396) for the local group or `_runnerDirectory.GetInventoryAsync(id, timeout)` for a remote one; `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` (line 412) logs a warning and returns `(owned, null)`, leaving `TranscriptBinding` null. | Behaviour exists; the local arm is the one awaited call the card names. |
| Nothing simulates a runner that never responds. | `AgentServiceIntegrationTests.C1235_hung_remote_runner_leaves_its_session_unknown_and_keeps_the_local_overlay` (`tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs:1783-1812`) hangs the **remote** directory read with `ListTimeoutSeconds = 1` and asserts the remote session's binding is null. The local client in that test answers immediately. | The remote arm is covered. Test 2 covers the local arm with the same harness (`CreateService(..., runnerClient:, runnerSettings:)`, `SeedStartedAgentAsync`, `CleanupSessionsAsync`). |
| The runner list rides the 100 s `HttpClient.Timeout` unless `AgentService` bounds it. | `SessionRunnerHttpClient.ListAsync` (`server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:557-569`) now also bounds itself by `ListTimeoutSeconds` and a resilience budget. | The `AgentService` deadline is the contract the agent list relies on for any `ISessionRunnerClient` (fakes, future transports); it is still worth its own witness. Noted so Review does not call test 2 redundant with the HTTP client. |
| `GetAllAsync` is the only caller. | Both `GetAllAsync` (line 115) and `GetByIdAsync` (line 191) go through `LoadLiveSessionsAsync`, which calls `AttachRunnerLiveStateAsync` (line 349). | Test 2 asserts through `GetAllAsync`, the home-page call the card is about. `GetByIdAsync` shares the code path; no second test. |
| Process-spawning tests need the assembly-local limiter. | `[ParallelLimiter<ProcessSpawnLimit>]` is applied at class level across `Antiphon.Tests` (`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs`). `WorkspaceInfoGitIntegrationTests` does not carry it. | Test 1 goes in a new class that carries the limiter rather than retrofitting it onto the four existing gate tests (D-2). |
| Every test class declares exactly one lane. | `TestLaneCategoryGuardTests` (Unit lane) fails any class with neither or both of `Category("Unit")`/`Category("Integration")`. | The new class is `[Category("Integration")]`; the Unit lane run (CP-3) is what censuses it. |

Out of scope, stated so nobody hunts for it: the same `TryKill` shape also exists in
`GitWorkspaceService.GetContentBytesAtAsync` (lines 223-278) and in
`server/Infrastructure/Git/CardFileRepository.cs:366-385`. The card names `RunAsync`; those
copies are separate methods with their own callers and would need their own witnesses under a
separate card if wanted.

## Decisions

- **D-1 Two test methods, zero product changes.** One per behaviour the card names. Rejected:
  also testing `GetContentBytesAtAsync`/`CardFileRepository` kill paths (not the card's ask;
  a separate card), and a `GetByIdAsync` twin of test 2 (same code path, no new discrimination).
- **D-2 Test 1 lives in a new class** `GitWorkspaceServiceTimeoutTests` in
  `tests/Antiphon.Tests/Application/GitWorkspaceServiceTimeoutTests.cs` with
  `[Category("Integration")]` and `[ParallelLimiter<ProcessSpawnLimit>]`, no `GitIntegration`
  category and no git probe: the executable under test is a shell, not git. Rejected: adding the
  method to `WorkspaceInfoGitIntegrationTests`, which would put the spawn limiter on four
  unrelated tests and make them skip when git is absent for no reason.
- **D-3 The 1 s deadline is a real timer and the test polls, never sleeps.** `RunCoreAsync` has
  no `TimeProvider` seam and the card forbids product code. The repo's clock helpers
  (`ControlledTimeProvider`, `ScaledTimeProvider`) cannot reach a `CancelAfter` on a linked CTS.
  The test's only waits are the product's own 1 s deadline and a bounded liveness poll (50 ms
  steps, 2 s cap) that returns as soon as the pid is gone. `[Timeout(60_000)]` on the method
  bounds a red run at the sleeper's lifetime. Rejected: a wall-clock `Task.Delay(2000)` before
  checking the pid (the brief forbids it and it would hide a kill that takes 1.9 s).
- **D-4 Sleeper shape per OS, grandchild witness on Linux.** Linux: `ExecutableName = "sh"`,
  args `-c`, `sleep 60 & echo $! > "$1"; wait`, `sleeper`, `<pidfile>`; `TimeoutSeconds = 1`.
  The pid file holds the *grandchild* `sleep`, so the assertion witnesses `entireProcessTree`
  as well as the kill (the CARD-0220/0221 orphan class). Windows: `ExecutableName = "pwsh"`,
  args `-NoProfile`, `-NonInteractive`, `-Command`,
  `$PID | Set-Content -LiteralPath '<pidfile>'; Start-Sleep -Seconds 60`; `TimeoutSeconds = 3`
  because pwsh start-up is the slow part and the pid must be on disk before the deadline fires.
  Rejected: pwsh on both platforms (start-up under load can lose the race with a 1 s deadline
  and the test then cannot witness anything); `sh -c 'echo $$ > f; exec sleep 60'` (no tree,
  so PC-2 could not go red). The Windows arm witnesses the direct child only; that is recorded
  in the PC table.
- **D-5 Liveness check tolerates an unreaped zombie on Linux.** After the tree kill, `sleep` is
  reparented to the container's PID 1, which may not reap it. The helper reads
  `/proc/<pid>/stat` and treats state `Z`/`X` or a missing entry as gone; on Windows it uses
  `Process.GetProcessById` + `HasExited`. Rejected: `Process.GetProcessById` alone on Linux,
  which reports a zombie as present and would flake.
- **D-6 Test 2 goes in `AgentServiceIntegrationTests`** next to the CARD-1235 tests, reusing
  `CreateService`, `SeedStartedAgentAsync`, `CleanupSessionsAsync` and the
  `[NotInParallel("AgentQueue")]` group, with a new private `HungRunnerClient` modelled on
  `ListedRunnerClient` and `InventoryDirectory.Hang`. Rejected: a new class (would duplicate the
  seeding helpers), and extending `ListedRunnerClient` with a hang flag (keeps two behaviours
  in one fake).
- **D-7 The hung fake honours the token, and the test proves the token reached it.** A real
  runner that never answers is cancelled by `HttpClient` through the token, so a fake that
  ignores cancellation would model a transport that does not exist and would hang the suite
  under PC-3. The fake counts calls and records whether it observed cancellation; the test
  asserts both, so an overlay that silently skipped the local read, or passed the caller's
  un-deadlined token (PC-5), cannot pass. The elapsed bound (< 10 s) and `[Timeout(20_000)]`
  are the belt to that brace. Rejected: asserting elapsed alone (vacuous if no live session was
  seeded, since `AttachRunnerLiveStateAsync` returns early on an empty map).
- **D-8 Red witnesses run in the Code stage by hand, outside the tool run.** There is no
  pre-fix defect to commit, and committing a product mutation to the task branch is not an
  option, so the two red runs apply uncommitted positive controls, use
  `scripts/run-checkpoint.ps1` **without** `-ExpectedSourceSha` (a dirty run is diagnostic, per
  CARD-0835), into a dedicated `bin-c223-red/` output, then restore. The closed `### Checkpoints`
  table holds only clean-source rows the checkpoint tool runs. Mutation re-executes every PC in
  the SourceLanding snapshot after land (CARD-0451). Rejected: putting the red runs in the
  Checkpoints table (the tool would run the commands green on clean source and record a pass),
  and skipping in-stage red (the brief asks for a red-first witness per behaviour, and CARD-0585
  rule 4 says a test that cannot go red is a stub).
- **D-9 Unit lane stays in the closed list.** Production delta is zero, but the default
  Code/Review recipe runs the Unit lane every Final round and `TestLaneCategoryGuardTests` is
  the only thing that censuses the new class's lane attribute. Rejected: Unit-less table (Review
  would flag the omission).
- **D-10 Linux is the only checkpoint lane.** Human-rated, master-only card; the Linux runners
  are the fleet default. The Windows arm of test 1 is designed and committed but not in the
  closed list; it gets its first run from the nightly backstop (`scripts/nightly-run.ps1`). If
  that run goes red the fix is a Windows-only follow-up, not a reason to pull this card.

Session checklist from the stage brief: this plan adds no session that waits for input, so the
release-deadline question (CARD-1083) does not arise.

## Slices

- **S1: kill-on-timeout witness.** New file
  `tests/Antiphon.Tests/Application/GitWorkspaceServiceTimeoutTests.cs`, one method
  `Timed_out_git_child_is_killed_with_its_tree`. Arrange: temp dir
  (`Directory.CreateTempSubdirectory("antiphon-c223-")`), pid file inside it, service built
  as `new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance, new GitProcessGate(1),
  Options.Create(new GitSettings { ExecutableName = ..., TimeoutSeconds = ... }))` per D-4.
  Act: `var sw = Stopwatch.StartNew(); var result = await service.RunAsync(tempDir,
  CancellationToken.None, args);`. Assert, in order: `result.ShouldBe((-1, "", "timeout"))`;
  pid file exists and parses to a positive int (fail with "sleeper never recorded its pid
  before the deadline" otherwise, never pass vacuously); `await GoneWithinAsync(pid,
  TimeSpan.FromSeconds(2))` is true; `sw.Elapsed < TimeSpan.FromSeconds(TimeoutSeconds + 5)`
  (returned on the deadline, not after the 60 s sleep). `finally`: kill the recorded pid if
  still alive and delete the temp dir. Private helpers: `Sleeper(pidFile)` returning
  `(string Exe, string[] Args, int TimeoutSeconds)` by `OperatingSystem.IsWindows()`,
  `IsAlive(int pid)` per D-5, `GoneWithinAsync`, `TryKill(int pid)` (shape of
  `tests/Antiphon.PtyHost.Tests/LinuxPtyHostLauncherTests.cs:243`). Commit: "test(CARD-0223):
  a timed-out git child is killed with its tree".
- **S2: local runner list deadline witness.** Edit
  `tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs`: add
  `C0223_hung_local_runner_list_returns_within_the_list_deadline_with_runner_state_unknown`
  after the CARD-1235 block, `[Test, Timeout(20_000)]`. Arrange:
  `SeedStartedAgentAsync(SessionStatus.Running)` (local session, no `RunnerId`), `var hung =
  new HungRunnerClient()`, `CreateService(db, new MockEventBus(), runnerClient: hung,
  runnerSettings: new SessionRunnerSettings { ListTimeoutSeconds = 1 })`. Act: `GetAllAsync`
  under a `Stopwatch`. Assert: no exception; `hung.Calls == 1`; `hung.SawCancellation` is
  true; elapsed < 10 s; the seeded agent's `LiveSession` is not null and `TranscriptBinding`
  is null. `finally`: `CleanupSessionsAsync(sessionId)`. Add private `HungRunnerClient :
  ISessionRunnerClient` beside `ListedRunnerClient`: `ListAsync` increments `Calls`, then
  `try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) {
  SawCancellation = true; throw; }`; every other member throws `NotSupportedException`
  exactly as `ListedRunnerClient` does. Commit: "test(CARD-0223): a hung local runner list
  returns within SessionRunner:ListTimeoutSeconds with runner state unknown".

Both slices are test-only. Commit and push each with
`git push origin HEAD:refs/heads/feat/card-task-938fe7a9`; the red witnesses and the tool run
below happen after S2 is pushed, against that exact SHA.

## Verification design

Final round (first Code of the card). Affected integration classes: the new
`GitWorkspaceServiceTimeoutTests` and the edited `AgentServiceIntegrationTests`. No other
class changes. No full assembly, no repetitions after green.

| ID | Ordinary witness | Acceptance |
|---|---|---|
| V-1 | `GitWorkspaceServiceTimeoutTests.Timed_out_git_child_is_killed_with_its_tree` | Returns `(-1, "", "timeout")` on the 1 s deadline (3 s on Windows); the recorded sleeper pid is gone within 2 s of return; the call returns in under deadline + 5 s. |
| V-2 | `AgentServiceIntegrationTests.C0223_hung_local_runner_list_returns_within_the_list_deadline_with_runner_state_unknown` | `GetAllAsync` returns in under 10 s with `ListTimeoutSeconds = 1`; the hung client was called once and observed cancellation; the seeded agent's live session has `TranscriptBinding` null. |
| R-1 | Whole `AgentServiceIntegrationTests` class (57 existing + 1 new) | 58 executed, 0 failed, 0 skipped. Any failure is checked method-scoped at the task base before being reported as inherited; never by rerunning the assembly. |
| R-2 | Whole `Antiphon.Tests` Unit lane | Required Final regression lane; `TestLaneCategoryGuardTests` censuses the new class. Inspect fresh TRX, report every failure/skip. |

### Red witnesses (Code stage, by hand, before the tool run)

Apply PC-1 and PC-5 together (different files, different methods, per git-common batching),
build once into `bin-c223-red/`, run each method-scoped filter, confirm the named failure,
restore both files, confirm `git status --short` is empty. Use
`scripts/run-checkpoint.ps1` without `-ExpectedSourceSha`; the line will say
`sourceState=dirty`, which is the expected diagnostic state for a red witness. Evidence paths
stay under `.antiphon/c223-red/` (ignored). Do not use the checkpoint tool for these rows: it
refuses a dirty tree before building.

| RW | Mutation (uncommitted) | Command | Expected red |
|---|---|---|---|
| RW-1 | PC-1: in `RunCoreAsync`'s `catch (OperationCanceledException)` delete the line `TryKill(process);` (`GitWorkspaceService.cs:1179`). | `pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name RW-1 -Project tests/Antiphon.Tests -OutputPath bin-c223-red/ -Filter '/*/*/GitWorkspaceServiceTimeoutTests/Timed_out_git_child_is_killed_with_its_tree' -MinExecuted 1 -Expect GitWorkspaceServiceTimeoutTests -ResultsRoot .antiphon/c223-red` | `executed=1 failed=1`; the `FAILED` line names the method; the assertion message says the sleeper pid is still alive. The product's `WaitForExitBounded` makes the red run take ~3 s, not 60 s, and the test's `finally` kills the orphan. |
| RW-2 | PC-5: in `ReadOwnerAsync` change `await _runnerClient.ListAsync(timeout)` to `await _runnerClient.ListAsync(ct)` (`AgentService.cs:396`). | `pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name RW-2 -Project tests/Antiphon.Tests -OutputPath bin-c223-red/ -NoBuild -Filter '/*/*/AgentServiceIntegrationTests/C0223_hung_local_runner_list_returns_within_the_list_deadline_with_runner_state_unknown' -MinExecuted 1 -Expect AgentServiceIntegrationTests -ResultsRoot .antiphon/c223-red` | `executed=1 failed=1`; the failure is the 20 s TUnit timeout (the un-deadlined token never cancels the fake). The sibling `C1235_hung_remote_runner_…` would stay green under this mutation, which is why the local arm needs its own test. |

Restore: `git checkout -- server/Application/Services/GitWorkspaceService.cs
server/Application/Services/AgentService.cs`, then `git status --short` must print nothing.
`bin-c223-red/` is a separate output from the green build, so no stale DLL can leak between
the two (CARD-0412 D6).

### Positive controls pending for SourceLanding Mutation

| ID | Variant | Mutation | Detecting filter |
|---|---|---|---|
| PC-1 | kill | Delete `TryKill(process);` in `RunCoreAsync`'s OCE catch. | `/*/*/GitWorkspaceServiceTimeoutTests/Timed_out_git_child_is_killed_with_its_tree` |
| PC-2 | tree (Linux lane only) | In `GitWorkspaceService.TryKill` change `Kill(entireProcessTree: true)` to `Kill()`; the grandchild `sleep` survives. Not detectable on the Windows arm (direct child only, D-4). | same |
| PC-3 | no deadline | Delete `timeoutCts.CancelAfter(...)` in `AttachRunnerLiveStateAsync`; the test hits its 20 s timeout. | `/*/*/AgentServiceIntegrationTests/C0223_hung_local_runner_list_returns_within_the_list_deadline_with_runner_state_unknown` |
| PC-4 | leak the OCE | Delete the `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` block in `ReadOwnerAsync`; `GetAllAsync` throws. | same |
| PC-5 | wrong token | `_runnerClient.ListAsync(ct)` instead of `ListAsync(timeout)` in `ReadOwnerAsync`. | same |

RW-1 and RW-2 are the Code-stage executions of PC-1 and PC-5; Mutation re-runs all five
method-scoped in the snapshot and owns missing-control discovery.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c223/` | c223-kill | `/*/*/GitWorkspaceServiceTimeoutTests/*` | V-1 | all 1 method, 0 failed/skipped; TRX shows `Timed_out_git_child_is_killed_with_its_tree` | 1 | 8 |
| CP-2 | S1-S2 | CP-1 | c223-agent-service | `/*/*/AgentServiceIntegrationTests/*` | V-2, R-1 | >= 58 executed (57 methods plus the new one; two carry `[Arguments]` and expand), 0 failed/skipped; TRX shows `C0223_hung_local_runner_list_returns_within_the_list_deadline_with_runner_state_unknown` | 58 | 10 |
| CP-3 | S1-S2 | CP-1 | unit | `/*/*/*/*[Category=Unit]` | R-2 | >= 1 executed, report every failure/skip; `TestLaneCategoryGuardTests` green | 1 | 30 |

Run as one tool invocation at the pushed S2 SHA:
`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-19-card-0223-git-gate-timeout-tests-plan.md --expected-source-sha <S2 sha>`, then `wait` until the exit is not 75. CP-2 needs the shared Postgres testcontainer the class already uses; its estimate includes the container start. Delete `bin-c223/` and `bin-c223-red/` (one per project) before the final report.

### Cost

Ordinary floor: 48 minutes for the closed table (one isolated build, three filtered runs)
plus about 12 minutes for the two red witnesses (one more isolated build, two method-scoped
runs, restore), so 60 minutes of verification. Authoring the two tests and the hung fake is
about 30-45 minutes. Code dispatch: Standard tier is sufficient, `-ExpectAbout 120`.
No repetitions after green; an inherited failure in CP-2 or CP-3 is confirmed method-scoped
at the task base, never by rerunning the assembly. No live stack, no restart, no Windows
checkpoint lane (D-10).

## Handoff

The brief folded no test-design stage into this dispatch and the verification design is in the
plan, so the next stage is Code against this artifact: `checkpoints:
docs/superpowers/plans/2026-10-19-card-0223-git-gate-timeout-tests-plan.md@<plan commit sha>
section "### Checkpoints"`. After Code: ordinary Review, land to master only, then
method-scoped SourceLanding Mutation for PC-1 to PC-5. Restart: none.
