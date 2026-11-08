# Run 4e0d1159 group 88: C0824WorkspaceSlotRoleGateTests Windows failure

Read-only extraction from the desktop logs for task e9d0e86e, written 2026-11-08. No build or test was run.

- Run: 4e0d1159 on rc/2026-11-11 (candidate `rc-2026-11-11-3364197bf1cb`, checkout `C:\Antiphon\releases\checkout-0297fa9c` at `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`).
- Sources: `C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb\logs\2026-11-08-0936-4e0d1159\`
  - `antiphon-all-group-88.trx` (745166 bytes)
  - `antiphon-all-group-88-tests.log` (30473 bytes)
  - `antiphon-all-group-88-execution\log_261108094801332.diag` (TUnit Trace diag, 1469455 bytes)
  - `antiphon-all-group-88.timing.json`
  - No hang or dump file exists for group 88.
- Group totals: 353 tests, 337 passed, 15 failed, 1 not executed. Chunk ran 09:48:00Z to 09:55:14Z (runWall 432.6 s, fixtureSetup 4.5 s, processWait 120.1 s, p95 10.1 s).
- Host: TUnit v1.44.0.0, Windows 10.0.19045, win-x64, .NET 9.0.22, MTP v2.2.2.

## One message for all 15 failures

All 15 failures carry the same message:

```
TimeoutException: scratch git exceeded its operation deadline
```

All 15 come from one exception. The first failing case ran the shared scratch build and hit the deadline. `SharedScratchRepository` is a `Lazy<SharedScratch>` with `LazyThreadSafetyMode.ExecutionAndPublication`, which caches a factory exception, so the other 14 cases rethrew the cached exception in 0.5 to 21 ms without starting git. Their stacks show `--- End of stack trace from previous location ---` after `Lazy.ViaFactory`. The first case's stack has `Lazy.ExecutionAndPublication` instead, which shows the factory actually ran in that case.

## Failed cases (start time, duration)

| Start (UTC) | Duration | Test |
|---|---|---|
| 09:48:14.087 | 2.449 s | Retry_keeps_the_first_decision_of_its_preparation(legacy-then-listed) **(first; factory ran here)** |
| 09:48:21.198 | 0.003 s | Decision_is_recorded_before_the_mirror_is_sent |
| 09:48:21.204 | 0.001 s | Retry_keeps_the_first_decision_of_its_preparation(slot-refused-then-none) |
| 09:48:21.208 | 0.001 s | Retry_keeps_the_first_decision_of_its_preparation(busy-then-none) |
| 09:48:21.209 | 0.001 s | Retry_keeps_the_first_decision_of_its_preparation(slot-then-none) |
| 09:48:21.210 | 0.003 s | Unlisted_role_gets_the_legacy_mirror_and_never_holds(none) |
| 09:48:21.211 | 0.003 s | Removed_role_slot_is_released_normally |
| 09:48:21.215 | 0.012 s | None_stops_new_leases_while_releases_continue(hosted-loop) |
| 09:48:21.223 | 0.004 s | None_stops_new_leases_while_releases_continue(reconcile-pass) |
| 09:48:21.283 | 0.005 s | Unlisted_role_gets_the_legacy_mirror_and_never_holds(mutation) |
| 09:48:21.289 | 0.001 s | Unlisted_role_gets_the_legacy_mirror_and_never_holds(invalid) |
| 09:48:21.358 | 0.002 s | Case_reuses_one_scratch_repository_and_spawns_no_git |
| 09:48:21.358 | 0.022 s | Hot_reload_applies_to_the_next_lease_without_restart |
| 09:48:21.359 | 0.003 s | Listed_role_leases_a_slot_on_the_real_runner |
| 09:48:21.361 | 0.0005 s | Unlisted_role_gets_the_legacy_mirror_and_never_holds(role-not-listed) |

The class has `[ParallelLimiter<GitProcessSpawnLimit>]`. The 14 cached failures start 7 s after the first case. That gap matches the limiter releasing the cases that were queued behind the first one.

## Stack of the first case (the factory run)

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TimeoutException: scratch git exceeded its operation deadline
  at Antiphon.Tests.Application.C0824SlotTransportWorld.RunGit(String workingDirectory, String[] args, CancellationToken ct, TimeSpan budget) in C:\Antiphon\releases\checkout-0297fa9c\tests\Antiphon.Tests\Application\C0824SlotTransportWorld.cs:line 412
  at Antiphon.Tests.Application.C0824SlotTransportWorld.Git(String workingDirectory, String[] args) in ...\C0824SlotTransportWorld.cs:line 384
  at Antiphon.Tests.Application.C0824SlotTransportWorld.BuildSharedScratch() in ...\C0824SlotTransportWorld.cs:line 213
  at System.Lazy`1.ViaFactory(LazyThreadSafetyMode mode)
  at System.Lazy`1.ExecutionAndPublication(LazyHelper executionAndPublication, Boolean useDefaultConstructor)
  at System.Lazy`1.CreateValue()
  at Antiphon.Tests.Application.C0824SlotTransportWorld.BuildRepository() in ...\C0824SlotTransportWorld.cs:line 174
  at Antiphon.Tests.Application.C0824SlotTransportWorld.StartAsync(Action`1 tweak, Action`1 configure, Action`1 runnerSettings) in ...\C0824SlotTransportWorld.cs:line 59
  at Antiphon.Tests.Application.C0824WorkspaceSlotRoleGateTests.Retry_keeps_the_first_decision_of_its_preparation(String flip) in ...\C0824WorkspaceSlotRoleGateTests.cs:line 245
  --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
Standard output
  ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.C0824WorkspaceSlotRoleGateTests.1.1.Retry_keeps_the_first_decision_of_its_preparation(System.String).2.1.0
```

## Stack of the cached cases

The other 14 cases differ only in their last frame (the calling test line):

```
  at ...C0824SlotTransportWorld.RunGit(...) in ...\C0824SlotTransportWorld.cs:line 412
  at ...C0824SlotTransportWorld.Git(...) in ...\C0824SlotTransportWorld.cs:line 384
  at ...C0824SlotTransportWorld.BuildSharedScratch() in ...\C0824SlotTransportWorld.cs:line 213
  at System.Lazy`1.ViaFactory(LazyThreadSafetyMode mode)
--- End of stack trace from previous location ---
  at System.Lazy`1.CreateValue()
  at ...C0824SlotTransportWorld.BuildRepository() in ...\C0824SlotTransportWorld.cs:line 174   (absent for Case_reuses_..., which enters via EnsureSharedScratchRepository() line 197)
  at ...C0824SlotTransportWorld.StartAsync(...) in ...\C0824SlotTransportWorld.cs:line 59
  at ...C0824WorkspaceSlotRoleGateTests.<test>() in ...\C0824WorkspaceSlotRoleGateTests.cs:line N
```

Caller lines: Unlisted_role_* line 87; None_stops_* line 123 (the BuildRepository line-174 frame is printed twice); Removed_role_* line 173; Hot_reload_* line 200; Retry_keeps_* line 245; Decision_is_recorded_* line 331; Case_reuses_* line 45 (via EnsureSharedScratchRepository line 197); Listed_role_* line 58.

## Fixture source at the run checkout

`C0824SlotTransportWorld.cs`, read from the run checkout:

```csharp
// line ~182
// Scratch commands share the existing custodied runner's single start/exit/drain deadline.
// Provisional cold-process sample (CARD-1514/1404): 372 commands in CP-6 at 259c2cde,
// max 325.291 ms. Round twice that observed maximum up to one second, including both drains.
internal static readonly TimeSpan GitBudget = TimeSpan.FromSeconds(1);
...
private static readonly Lazy<SharedScratch> SharedScratchRepository = new(BuildSharedScratch, LazyThreadSafetyMode.ExecutionAndPublication);

private static SharedScratch BuildSharedScratch()       // lines 201-217
{
    var root = Path.Combine(Path.GetTempPath(), "c0824-shared-scratch-" + Guid.NewGuid().ToString("N"));
    ...
    Git(origin, "init", "--initial-branch=main");        // 207
    Git(origin, "config", "user.email", "c0824@localhost");
    Git(origin, "config", "user.name", "c0824");
    File.WriteAllText(... "README.md" ...);
    File.WriteAllText(... ".gitignore" ...);
    Git(origin, "add", "-A");                            // 212
    Git(origin, "commit", "-m", "base");                 // 213  <-- timed out
    ...
}

internal static string RunGit(string workingDirectory, string[] args, CancellationToken ct, TimeSpan budget)  // ~388-415
{
    ... FileName = "git" (PATH lookup), GIT_TERMINAL_PROMPT=0, GIT_CONFIG_NOSYSTEM=1 ...
    var result = Antiphon.Checkpoints.CheckpointProcessRunner.Run(psi, checked((int)budget.TotalMilliseconds), git: false, cancellationToken: ct);
    if (result.TimedOut)
        throw new TimeoutException("scratch git exceeded its operation deadline");   // 412
    ...
}
```

`Git(...)` (line 384) always passes `GitBudget`, which is 1 s per command. The command that timed out is the first `git commit` in a fresh temp repository. It is the fifth git process in the factory. The first case took 2.449 s in total: four earlier git commands, the commit hitting its 1 s deadline, and the drain.

## Text searched for and not found

- No `EncodeOriginUrl`, `RewriteOriginUrl` or `shared scratch clone origin url was not unique` text. The failure happens before `CopyTree`/`RewriteOriginUrl` (line 174 never returns), so origin-url encoding never ran.
- No path-length text (`PathTooLong`, `Filename too long`).
- No `safe.directory` or `dubious ownership` text.
- No git-config text, and no `InvalidOperationException: git ... failed:` line. Git never returned a non-zero exit. It only overran the deadline.
- The tests log and diag hold no `git version` line or git path. The run directory has no environment record naming git.

## Git and host environment

Observed from the same desktop now. The run's own logs do not record git.

- `RunGit` resolves `git` through PATH. On this desktop the resolution is `C:\Program Files\Git\cmd\git.exe` (wrapper) -> `C:\Program Files\Git\mingw64\bin\git.exe`.
- Version: `git version 2.50.1.windows.1`.
- Global config: `commit.gpgsign=false`. `core.fsmonitor`, `core.hooksPath` and `init.templateDir` are unset. Nothing in the global config would make `commit` slow.
- `C:\Antiphon\releases\checkout-0297fa9c` exists. Its HEAD is `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e` ("fix: include the workspace-slots reader in extracted deploy harnesses"). It is on drive C:, which has 1.9T total and 286G free (85% used) now.

## The 5 passing cases (Rig-only, no scratch git)

These cases use `Rig.StartAsync` directly (test lines 363/364/383) and never touch `C0824SlotTransportWorld`:

| Duration | Test |
|---|---|
| 3.891 s | Flag_off_hosted_loop_sends_no_release |
| 7.191 s | Flag_off_or_no_feature_ignores_the_list(flag-off:*) |
| 7.121 s | Flag_off_or_no_feature_ignores_the_list(runner-lacks-feature) |
| 4.714 s | Flag_off_or_no_feature_ignores_the_list(flag-off:none) |
| 3.415 s | Flag_off_or_no_feature_ignores_the_list(flag-off:Plan) |

A plain Rig start took 3.4 to 7.2 s at the same moment, 09:48:14 to 09:48:21. The host was heavily loaded during chunk start-up.

## The same failure in earlier Windows runs

| Run (rc) | Class result | Message |
|---|---|---|
| 8e938b6d (rc/2026-11-08, checkout-36d12cfa) | 14 failed, 5 passed | 14 x `scratch git exceeded its operation deadline` (per-case scratch; 1.07 to 2.40 s each) |
| c3f65ddb (rc/2026-11-09, checkout-59e9866b) | 1 failed, 18 passed | 1 x `scratch git exceeded its operation deadline` (busy-then-none, 2.38 s) |
| e737e9fe (rc/2026-11-10, tip 74a8004f405b) | 20 passed | none (Case_reuses_... 0.618 s) |
| 4e0d1159 (rc/2026-11-11, 3364197bf1cb) | 15 failed, 5 passed | 15 x the same, from one cached Lazy exception |

The commit that added the 1 s budget, `e4103bf16`, is in all four of these rc tips. The shared-scratch commits `b34a16d0b`/`e02965b22` are in rc/2026-11-10 and rc/2026-11-11 but not in rc/2026-11-09. The shared `Lazy` turned one slow `git commit` into a whole-class failure. Before that change, each case failed on its own.

## Diagnosis

The cause is a **Windows-only shared-fixture deadline (GitBudget one second)**. Origin-url encoding and the git environment are ruled out.

- `C0824SlotTransportWorld.cs:213` (`git commit -m base`) overran the 1 s `GitBudget` set at about line 185 and threw at line 412. The budget is twice a *Linux/CP-6* cold-process maximum of 325 ms. On the Windows desktop at chunk start-up, plain Rig starts took 3.4 to 7.2 s, so one cold `git.exe` commit (Git for Windows wrapper plus mingw process plus Defender scanning a fresh temp repo) can exceed 1 s.
- Because the `Lazy<SharedScratch>` uses `ExecutionAndPublication`, it caches that one exception. The 14 cases released by `ParallelLimiter<GitProcessSpawnLimit>` then all fail in about 1 ms, so a single slow git process becomes 15 failures.
- The same message appeared in two of the three previous Windows runs (14 and 1 failures), so the failure is intermittent load sensitivity, not a regression introduced by 3364197bf.
- No git error output, no non-zero exit, no safe.directory, path-length or config text appears anywhere, and RewriteOriginUrl/EncodeOriginUrl never executed.
