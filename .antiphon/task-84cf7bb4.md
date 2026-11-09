# CARD-1793 Code report (task 84cf7bb4)

Branch feat/card-task-84cf7bb4, runner mirror /work/worktrees/task-84cf7bb4, base master 2bf29ee9dadbd08c3ae78c186e2af00a6deb290e.

## Commits
- red: 1c758374493ca175122c3d1b1cf06fd24b94b7d4 (witnesses), e1d3925a2fcab59dd840579d62a6a4c2269e3b46 (session witnesses reuse the registered c628-token pty test root)
- fix (tested SHA): 6a0baee380ed6363c6eaac0d76e461b7c992539b
- this report: the commit that adds this file

## What changed (card items)
1. Per-session cache: `SessionRunnerRuntime` pty-host lane calls `RunnerSession.WithPwshCache` before `StartAsync`: a launch with no XDG_CACHE_HOME key (case-insensitive) gets `$TMP/antiphon-pwsh-cache-session-<sessionN>-<guid>`, owner.pid = runner pid, then reassigned to the pty-host pid once known; `HandleExited` and the failed-launch catch delete it. A caller-named cache is passed through untouched. All platforms (inert on Windows). The Herdr lane is NOT changed (pane env comes from the herdr server and request.Env; adding a key changes LaunchEnvNames/StaleEnvNames) - residual.
   Checkpoint tool: `tools/Antiphon.Checkpoints.csproj` links `src/Antiphon.Hosting/PwshPrivateCache.cs` (literal Compile item), so its ProcessDriver .ps1 shims get a private cache.
2/3. Stale sweep: `Install()` first runs `SweepStale(Path.GetTempPath())`: each `antiphon-pwsh-cache-*` directory whose owner.pid parses to a pid that is neither this process nor live (`Process.GetProcessById`) is deleted. Absent/garbled marker, reparse points/links, non-family names are kept. Markers are now written temp-file + move so a sweep never reads a truncated pid. Sessions that survive a soft runner restart keep a host-owned directory (item 3); an adopted session's cache is not deleted at its exit by the new runner (it has no path) but is swept once the host pid dies.
4. Playwright: no change; docs/docker-stack.md already requires PLAYWRIGHT_BROWSERS_PATH for server2.
5. Witness for the ScriptHarnessHost linux-owner Release.

## Tests (all Linux server2, build-slot leased, native output, UseAppHost=false)
Red at base source (commit 1c758374/e1d3925a, source = base):
- Antiphon.Tests `PwshPrivateCacheTests/Install_removes_*`, `Checkpoint_tool_*` : 2 failed (stale dir kept); `ScriptHarnessLinuxOwnershipTests/Supervisor_removes_*` passed (pins existing behaviour; red via M7). 3 total.
- SessionRunner.Tests `C1793SessionPwshCacheTests`: `Each_pty_session_*` failed (child saw the runner's cache); `A_launch_that_names_*` passed (pins pass-through; red via M4). 2 total. (First red attempt was a fixture error - unregistered TestSessionLogRoot prefix - fixed in e1d3925a, not counted as red.)
Green at 6a0baee3:
- Touched + impacted-classes (kinds=checkpoint-tool) run, Antiphon.Tests: 53/53 (PwshPrivateCacheTests 5, ScriptHarnessLinuxOwnershipTests 22, CheckpointReceiptSafetyTests 8, CheckpointTerminalReceiptTests 10, HostCleanupCheckpointReceiptTests 8). slot waited=0s.
- Guards, one run, Antiphon.Tests: 77/77 (TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 9). waited=0s.
- SessionRunner.Tests, one run: 14/14 (C1793SessionPwshCacheTests 2, RunnerChildClaudeTokenInheritanceTests 2, Card1653FreshLaunchIdentityTests 3, C1677TempFamilyRegistryTests 3, TestClassificationGuardTests 1, NativeLinuxPolicyWitnessTests 3). waited=0s.
Mutants (each red, source restored, staged dlls restored byte-identical):
- M1 drop WithPwshCache call -> Each_pty red (child saw runner cache)
- M2 drop ReleasePwshCache in HandleExited -> Each_pty red (cache outlived session 30 s)
- M3 drop AssignOwner -> Each_pty red (owner.pid = runner pid, not host pid)
- M4 drop caller-named pass-through -> A_launch_that_names red
- M5 drop isLive in SweepStale -> Install_removes red (live owner's dir deleted)
- M6 drop reparse-point refusal -> Install_removes red (link deleted)
- M7 drop Release() in ScriptHarnessHost supervisor finally -> Supervisor_removes red
- M8 drop the Checkpoints Compile link -> Checkpoint_tool red
check-evidence-diff base..6a0baee3: commits=3 entries=0 violations=0. merge-tree vs origin/master 2bf29ee9 (unchanged): clean, tree b5f1c99edf22b0457bb0dffdb9fc86902442807e.
test-execution-policy.json unchanged: no policyHash / PinnedInputDigest refresh.
OS branch witnesses: no OS-selected branch added or changed (all paths run on every OS); check-os-witnesses not run (no plan for this card).

## Cost (review item)
`Install()` now enumerates the temp root on every module start (test hosts, runner, checkpoint tool, every ScriptHarnessHost helper). Measured on server2's /tmp (25,232 entries): `Directory.GetDirectories("/tmp","antiphon-pwsh-cache-*")` 95-116 ms per call. Proposed if Review wants it bounded: a 10-minute stamp file (`antiphon-pwsh-cache.sweep`) so most starts skip the scan. Not implemented here (budget).

## Owed / residual
- Windows: new tests are cross-platform but were run on Linux only; the Windows run of C1793SessionPwshCacheTests and PwshPrivateCacheTests (link case falls back without symlink privilege) is owed to the Windows lane/rc qualification.
- Herdr lane sessions still inherit whatever XDG_CACHE_HOME the herdr server has (not curated here).
- Not registered in the C1677 temp family registry (self-sweep chosen instead).
- PCs: all pending for SourceLanding Mutation (none bound; M1-M8 above are Code-stage mutants only).
