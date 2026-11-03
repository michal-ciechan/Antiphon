# task 456cfb77: cut rc/2026-11-04 from master

Outcome: I cut **rc/2026-11-04** at master `f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0` after every precheck and tree check passed. The candidate id is **rc-2026-11-04-f32aed42e58a**. I created no tag and published, promoted and landed nothing. I made no push to rc/2026-11-03 or rc/2026-11-01.

## Master pin

- Before starting: `git ls-remote origin refs/heads/master` gave `f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0`, which matches the brief. I read it again right before the push and it was unchanged. Nothing landed during the task.

## Step 1: the name

No script refuses `rc/2026-11-04`.

- `scripts/lib/release-gate.ps1`, `Test-ReleaseGateCandidateRef`, is the only validator. It is used by `release-cut.ps1` and `release-candidate.ps1`.
  - It checks the pattern `^rc/(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})$` and that `TryParseExact 'yyyy-MM-dd'` parses the date.
  - It has no check against today and no "not in the future" check.
  - The candidate id is `rc-<date>-<first 12 hex>`; the pattern is `^rc-\d{4}-\d{2}-\d{2}-[0-9a-f]{12}$`.
- Tag derivation: in `publish-release.ps1:208`, a long-lived candidate sets `$cutUtc = $attemptUtc`. So the vYYYY.MM.DD.N-rc.K date comes from the **publish attempt time**, not from the line's date.
  - A publish on 2026-11-03 UTC would therefore give a v2026.11.03.N-rc.K tag on the rc/2026-11-04 line.
  - This is cosmetic, but whoever publishes should know it.
- `rc-qualification-status.ps1` has no date logic.
- No C#, deploy or sh file matches an rc/ pattern.
- `docs/release-gates.md` has no same-day convention.

## A-series

- **A1 (name free):** before the push, `git ls-remote origin refs/heads/rc/2026-11-04` was empty.
- **A1 (cherry checks):**
  - `git cherry f32aed42e origin/rc/2026-11-03` (tip 919d8d42f) gave **0 `+` lines**. 919d8d42f is an ancestor of master.
  - `git cherry f32aed42e origin/rc/2026-11-01` (tip 10a826b9d) gave **0 `+` lines**.
- **A4 land graph:** `scripts/audit-land-verification-graph.ps1 -Check -ToolPath /work/cache/land-graph-audit/642837e6415f572aee355c73c95298ec0e2483b5/Antiphon.LandGraphAudit.dll`.
  - The evaluator tree id matches `HEAD:tools/Antiphon.LandGraphAudit` = 642837e6, so nothing was built.
  - Output: `digest=266595CBAA5CA00BE0182F33804AE94AD851DB9BBCF74C3FAAF211D5D5A49CFA count=69`, five `not-built:` lines, `phase=pass2`, `approved`. **Exit 0** in 57 s.
  - I did not run `-Update`. The script removed the canary directory.
- **A4 live-lane pin:** `scripts/test-live-lane-run.ps1 -PrintInputDigest` printed `ed94f736bb81701771a951d3680584580698e87e9ec6dc106562fd5cc5d9e661`, exit 0. That equals `$script:PinnedInputDigest` at line 20.
- **C1323 census:** `scripts/test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy` gave **6 passed, 0 failed**, harness exit 0.

## Step 3: tree verification (cut tree f32aed42e)

- **Solution build:** one lease (waited 0 s, held 201 s, maxcpucount 6). Command: `dotnet build Antiphon.sln -p:UseAppHost=false`.
  - Wall time **3m22s** (MSBuild 3:19.98).
  - **0 errors**, 1735 warnings.
  - **NETSDK1047: none** (0 occurrences).
- **Smoke runs:** each was `--no-build -p:UseAppHost=false`, ran separately under its own lease, and wrote a TRX.

| # | Selection | Result |
|---|---|---|
| 1 | LandGraph real-repo: `C1385_RegistryMatchesNarrowGraph`, `C1483_S4_RealRepositoryApproves`, `C1483_S5_CheckRequiresEvaluationRecord` (with `tests/Antiphon.E2E/TestOutput/Logs/run.log` present) | **3/3 passed**, 1m54s |
| 2 | `ReleaseGatePlatformTests` + `LiveLaneRunnerTests` + `LiveLaneFingerprintTests` | **16/16 passed**, 5m32s |
| 3 | `WindowsUnitPlatformAdmissionTests/C1210_*` | **5 passed, 1 skipped**, 0 failed, 36s |
| 4 | The 7 TestDb* fixture classes | **75 passed, 3 skipped**, 0 failed, 2m31s |

- Run 1: the filter is `(LandNonCodeSkipTests*)|(LandGraphEvaluationTests*)`. The second class is partial. I created run.log for the run and then deleted it and its two directories by literal path.
- Run 2 covered C1323_PlatformExecution, LinuxOnlyWindowsExclusionPolicy, StableCanonicalInputs, DeadlineJoinsOwnedChildren and the rest of the 16.
- Run 3: the skip is `C1210_Windows_linux_only_outcomes_are_exact`, reason "the child skip-roster audit requires Windows placement". It is skipped on Linux by design.
- Run 4:
  - Per-class counts: BakedImageDataDirectory 1, BakedImageLifecycle 7, ClonePool 8, FixtureIsolation 6, FixtureLazyInitialization 17, FixtureLifecycle 33, SchemaCacheLifecycle 6.
  - The 3 skips are the known lazy-init child probes, which run in their parent tests: `Child_db_free_selection_touches_no_database`, `Child_post_start_fault_is_terminal` and `Child_mixed_consumers_initialize_once`.
- Nothing else ran.
- I deleted all bin/obj directories by literal path; the worktree is clean apart from the inbox.

## Step 4: the cut

- `git push origin f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0:refs/heads/rc/2026-11-04` gave `* [new branch]`. It was a plain push, not a force push.
- ls-remote afterwards:
  - `f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0 refs/heads/rc/2026-11-04` (equals the cut SHA exactly)
  - `f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0 refs/heads/master`
  - `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd refs/heads/rc/2026-11-03` (unchanged)
  - `10a826b9d61f2c2cdfa29a43cac59fe360c99ea9 refs/heads/rc/2026-11-01` (unchanged)
- **Candidate id: rc-2026-11-04-f32aed42e58a**

## What the new candidate has that rc/2026-11-03 (919d8d42f) lacked

There are 62 commits, none of them merges. By files touched, the split is about 45 % tests, 13 % scripts, 11 % docs/plans and 30 % product code (41 non-test files, +2258/−62).

### Product-affecting commits

- **CARD-1677 runner worktree retention: new, and on by default with Execute=true.**
  - Commits: 1b6aaf36b, 292195764, bb0947982.
  - New `RunnerWorktreeRetentionService` (541 lines), `RunnerWorkspaceRetention` (578 lines) on the runner, a Hangfire job, and phone-home contracts and commands.
  - `appsettings.json` settings:
    - `RunnerWorktreeRetention`: Enabled=true, Execute=true, Runners=[server2], cron `*/30`.
    - MinTerminalHours 12, MaxRemovalsPerTick 25.
    - Roots `/work/worktrees` and `/work/worktrees-retiring`.
    - TempSweep is off.
    - Runner `RetentionRecentUseMinutes` = 10.
  - `WorkspaceDiskAlarm`: Enabled at 85 %, for server2 `/work`. It shows as a new WorkspaceDiskLow attention row, with a client visual.
  - **Once activated, it deletes finished task worktrees on server2.**
- **CARD-1617 / CARD-1681, Linux pty custody** (`PtyAgentRunner`, `HostSession`):
  - The custody kill is bounded per runner, with capped backoff.
  - EIO after root exit is treated as end of output.
  - RootExited holds the hangup composition.
  - Commits: 859e67b24, 25f5d4976, b2ea37b4d.
- **CARD-1675, Grok probe** (`GrokNativeSessionStore`): a non-directory, or a link or junction to a missing directory, at or above GROK_HOME now reads Unavailable instead of Missing. Commits: ce89fa5d4, a2584ef6e.
- **CARD-1679, dispatch** (`AgentTaskDispatcher`, `AgentSessionRuntime`): a canceled brief probe never fails a launched dispatch, and remote liveness skips the local List. Commit: 88660a5c8.
- **CARD-1674, alarm loop** (`RunnerAlarmHostedService`): ignores a drain expiry at or before its last pass. Commit: 41ae58b9c.
- **CARD-1676** (`ReviewRoundGuard`): reads the colon-less "REVIEW ROUND N OF 3" shapes. Commit: 074e8497d.
- **server2 census VoidTaskResult leak:** stdin writes are voided. Commit: fc3455da3, in the server2 scripts.

### Ops and deploy scripts

- **CARD-1680 `restart-apphost.ps1` / `apphost-common.ps1`:** waits while the launch progresses, up to a hard cap. Only a pid file rewritten after launch names this launch's AppHost. Commits: 62d04f838, 6f886fe7e.
- **Deploy:** in `deploy-server2.ps1`, the deploy-temp hold-lift clear skips the temp volume census and checks its own runner. Commits: f0b4729ce, d9f9f3005.
- **CARD-1590:** the failfirst archive skips .git; the C1313 harness deletes its fixture roots; `New-C487Root` defaults to GetTempPath. Commit: 3494aabcd.

### Test infrastructure and the fixes for the previous run's failures

- **Baked test Postgres (the EINTR fix):** runs its data directory from a volume, not the overlay layer. Commit: 90c986421.
- **Test-DB clone pool:** 10, then 20 ready, 4 at a time, max_connections 500. Commits: bcfe77b3d, a06d64f71.
- **C1323 census roster rows:** commit 0ca1a0ffe.
- **Live-lane digest re-pin:** commit c28a27087.
- **LiveLaneFingerprintTests:** budgets now come from measured p95, and the document cache is shared. Commits: d11315319, 1e49aa044.
- **CARD-0824 S1 slot classes:** Linux only. Commit: f32aed42e.
- **Mutation witnesses:** CARD-1673, 1678, 1683, 1684, 1685.

### Docs

CARD-1617 and CARD-1677 plans and test design.

## Notes for the orchestrator

- Scripts that refuse the new name: **none**.
- Publish-date caveat: the tag date comes from the publish attempt time, not the line name (see Step 1).
- The retention default (Enabled + Execute) is the main behavioural change a host activation would bring.
