# CARD-1744 blockers G-1..G-3 plan review, round 1 (Full)

Date: 2026-11-18. Review task `1a877722-568e-4875-b335-cf38d7dbd87c` (Grok). Plan owner
`a2c6e640-c6ae-46fa-8835-5299969eaa0f` (Claude). Read-only. No production code, test, or plan
file was changed.

- Subject: `docs/superpowers/plans/2026-11-18-card-1744-blockers-g1-g3-plan.md` on
  `origin/feat/card-task-a2c6e640`, tip **891ef27d2fc4999e4f519c1f2d5a0527e6f68954**
  (two commits over master `997f9b8d9ffc4ba870d2fd56caf7c03781591624`; the plan file only).
- `reviewedSourceClean` stays false. The import below is a manifest, and this plan review has
  no checkpoint-tool receipt.
- Mutation stays waived (operator 2026-11-12). Positive controls stay pending.

**Verdict: BLOCKERS (4).** B-1 through B-4. The routing, cursor, phase-1, and phase-2 claims
hold. Advisories do not hold the land.

## What holds

- **D-1, new tasks.** `SelectConstrainedAutomaticAsync` tries the kind default, then the global
  default, then `SelectPlatformCandidateAsync` (`AgentTaskService.cs:2195-2230`). Live
  runner-defaults are revision 2, `globalRunnerId=server2`, kind defaults empty. Once local is
  Linux, an unpinned non-excluded Windows task fails the desktop probe and the server2 probe
  (`PlatformMatches`), then the ordinal scan of `KnownRunnerIds` returns the first eligible
  Windows runner that advertises the platform feature. With only `desktop-win`, that is
  `desktop-win`. No platform-to-runner map is required for new tasks. This holds only when
  `desktop-win` has `AllowDelegatedTasks` (B-1). `ExclusionFor`
  (`DefaultRunnerRoutingPolicy.cs:165-190`) still pins existing-process, non-Worktree,
  non-admitted kinds, Codex SourceLanding, non-Claude orchestrators, and specialists to the
  desktop alias. After cutover that alias is Linux, so those shapes 409. O-6 accepts that.
- **Dispatcher.** `ClassifyRequiredPlatformAsync` (`AgentTaskDispatcher.cs:6795-6827`) describes
  the frozen `task.RunnerId` (null means local) and returns `runner_platform_mismatch` on the
  opposite platform. It does not reroute. O-9 (drain Queued/Blocked Windows tasks with
  `RunnerId` null before the window) is the non-code answer for rows already stored against local.
- **D-2 scan gap.** `TryPlatformPreferenceAsync` (`:2182-2184`) requires `IsRunnerBound` and
  `AllowsDelegatedTasks`. The fallback loop (`:2209-2225`) checks eligibility, platform, and
  the feature only. S2's filter matches the preference predicates.
- **D-3 cursor, apart from the validation sentence.** `HostLoadIngestService.IngestAsync` looks
  the cursor up by `HostLoadCounterCache.Canonical(batch.RunnerId)` (`:28`, `:39`) and throws
  `Conflict()` when `cursor.RunnerStoreId` differs (`:40`). The pump sets
  `LastFailure = "history_commit_unavailable"` (`HostLoadIngestPump.cs:83-86`). A new
  `DirectRunnerKey` of `server2-direct` is a different cursor row. `HostLoadIngestCursor.RunnerId`
  is an unconstrained string. `LoadRunnerId` is assigned in memory (`HostLoadModel.cs:73`).
  No EF migration and no DTO shape change.
- **D-4 shape.** Compose `:?` for the secret and home, plus a deploy refusal, is the right
  fail-closed idea. The cited line does not implement the WhatIf witness (B-2). Today's compose
  block (`docker-compose.server2-server.yml:56-69`) is server2 only. The execute key list is six
  keys (`deploy-server2-server.ps1:347-351`).
- **D-7.** `scripts/start-windows-phone-home-runner.ps1` refuses `local`/`desktop` (`:39`),
  refuses a non-Windows host (`:40`), resolves the Debug exe from its own repo root (`:42-44`),
  and defaults to port 17214. `-Capacity 1 -Port 17214` from `C:\Antiphon\runner\app` matches.
  `session-runner-restart-health.ps1:64` and `:115-117` stop only a process whose path equals
  the main checkout's Debug exe. Phase 1 needs no code change.
- **D-8 / D-9 / D-10.** `autostart-session-runner.ps1` hardcodes `--urls http://localhost:17204`
  and the checkout Debug exe, and does not read json. `install-autostart.ps1:83` and `:191-211`
  register task `Antiphon Session Runner` to run that script by path, Interactive, `RestartCount 3`.
  Editing the script is live on the next start; the task action stays the same.
  `scripts/lib/build-slot.ps1:27-30` defaults Windows to `http://localhost:17204/build-slots`.
  `scripts/qualification/launch-rc.ps1:64-65` holds one in-memory build-slot lease around the
  whole nightly run, so the switch belongs between Full runs. Rollback (delete
  `phone-home.json`, restart the runner, then the parent AppHost restart) matches the adopt-17204
  note in the autostart header.
- **Reachability.** The Windows runner registers outbound and dispatch rides that socket. The
  Linux server container does not dial desktop 17214 or 17204. The plan's same-host hairpin
  note is phase 1 on the desktop, and the plan already marks it UNOBSERVED.
- **O-5..O-9 recommendations** match the decisions, except O-5's "deploy refuses" is the WhatIf
  hole in B-2. O-6 matches `ExclusionFor` for the shapes it names. O-7 matches D-7. O-8 matches
  D-8 (a second task would share the T-9 census). O-9 matches the frozen-host recheck.
- **No session input-wait change.** Parking disabled (CARD-1083) is unchanged.
- **Live catalogue, ids only.** Three runners: `desktop` windows, eligible, capacity 2,
  occupied 0; `server2` linux, eligible, capacity 10, occupied 6; `server2-temp` retired,
  unavailable. Matches the plan's recorded revision 2 / server2 / desktop capacity 2.

## Blockers

### B-1. D-5 enables `desktop-win` without `AllowDelegatedTasks`

- **Where:** plan D-5 table and W-1/W-3, against `PhoneHomeRunnerEntry` (`PhoneHomeRunnerSettings.cs:186-188`) and `ValidateMapped` (`:386-387`). `AllowsDelegatedTasks` is `PhoneHomeLaunchPolicy.cs:50-51`.
- **Failure:** `Enabled` defaults true. `AllowDelegatedTasks` defaults false. `StandingAgentId` defaults empty. The D-5 keys bind an enabled entry with no standing agent and no pool flag, so the validator adds `StandingAgentId must be set unless AllowDelegatedTasks is true` and W-3 ("0 failures") stays red. If Code sets a `StandingAgentId` to silence the validator, S2's scan skips the runner (`AllowsDelegatedTasks` is false) and an unpinned Windows task 409s. That falsifies D-1 and W-4.
- **Why it matters:** the routing claim and the validator claim cannot both pass on the stated keys. W-1 only asserts the D-5 list.
- **Fix:** D-5 and W-1 require `Enabled: "true"`, `AllowDelegatedTasks: "true"`, and no `StandingAgentId`. W-3 asserts those bound values. The "+16 lines" count is short of a complete entry (those two flags, `DisplayName`, `SharedSecret`, `RawExeAllowList__0`, plus the D-5 paths). The missing flag is the blocker.

### B-2. W-2 cannot go green from the cited deploy edit

- **Where:** S1 cites `deploy-server2-server.ps1:347`. W-2 (`G1_deploy_whatif_refuses_without_desktop_win_keys`) expects an env file missing either new key to exit 2 `env-missing` with no docker, on the WhatIf path. The rollout says "-WhatIf must pass first (W-2)".
- **Failure:** the key loop is after `if ($planOnly) { return }` (`:341-344`). `-WhatIf` never reads that list. Compose `:?` is not evaluated, because WhatIf prints the command and does not call docker. Adding the keys only at `:347` leaves W-2 red. Requiring the keys on every WhatIf, including no env file, would break `WhatIf_prints_the_plan_and_calls_no_docker` (exit 0 with no `-EnvFile`; the no-file branch at `:336-338` runs only when `-not $planOnly`).
- **Why it matters:** the witness and the stated line disagree about when the check runs.
- **Fix:** when an env file is present, check the two new keys in that branch before the `planOnly` return (same place as the dev-password and hangfire-storage checks), and keep the execute list in sync. W-2 passes an otherwise valid env file that omits one key at a time. WhatIf with no env file stays exit 0.

### B-3. D-3 validation rejects the production key

- **Where:** plan D-3, "It must not equal a configured phone-home runner id, `local`, or any id other than the default `desktop`", against "Production: the compose sets `server2-direct`", W-6, and W-8.
- **Failure:** a literal reading rejects `server2-direct`, so the production setting fails closed at startup and W-6 (a new cursor row for `server2-direct`) cannot pass. W-8 refuses only `local` and a phone-home id, which is the narrower rule. `server2-direct` equals neither `server2` nor `desktop-win`.
- **Why it matters:** the sentence that Code will implement forbids the value the compose is told to set.
- **Fix:** allow the regex, refuse `local`, refuse a configured phone-home runner id, default `desktop`, and state that `server2-direct` is a legal non-default. Delete the clause that forbids every id other than `desktop`.

### B-4. Closed checkpoint floors do not run the named classes

- **Where:** CP-4, CP-6, and CP-9. The only recount instruction is "replace each Min of 3". Import stored these floors as written (`minExecuted` 17, 3, and 1).
- **Failure:**
  - CP-4's filter token `(RunnerDefaultTests*)` matches no class. `tests/Antiphon.Tests/Application/RunnerDefaultTests.cs` declares `RunnerDefaultsWireTests` (5 `[Test]`), `OutboundConversionPublicCreateTests` (1), `RunnerDefaultSettingsTests` (6), `RunnerDefaultPlacementTests` (9), and `RunnerDefaultMigrationTests` (5). None is named `RunnerDefaultTests`. `TaskPlatformPlacementTests` has 15 `[Test]` methods and no `[Arguments]`. Min 17 equals those 15 plus W-4 and W-5. The row goes green when the RunnerDefault file never runs. The affected-class list says `RunnerDefaultTests` is re-run in CP-4.
  - CP-9 Min 1. `DockerStackDocumentationTests` has 19 `[Test]` methods and no `[Arguments]`. One executed test satisfies the floor. This Min is outside the "replace each Min of 3" sentence.
  - CP-6 Min 3 is the placeholder the prose tells Code to replace, and the imported manifest still says 3. A run of the table as imported passes on the three new tests. `(HostBudgetPipelineTests*)` matches no class: `HostBudgetPipelineTests.cs` is `partial class AgentTaskPipelineStatusTests`, which the same filter already names.
  - CP-2 Min 18 equals 15 `Server2ProductionServerContractTests` methods plus W-1..W-3. No `[Arguments]`. Leave it.
- **Why it matters:** Min is the executed-test floor. A floor below the named classes lets a dropped class go green. The importer accepted the table.
- **Fix:** put the real class names in CP-4. If the whole file must run, name all five classes (`RunnerDefault*` misses `OutboundConversionPublicCreateTests`) and set Min to at least 15+26+2 = 43. If only placement must run, name `RunnerDefaultPlacementTests` and set Min to at least 15+9+2 = 26. Set CP-9 Min to at least 19. Set CP-6 Min to the counted `C1514HostLoad*` test classes under `tests/Antiphon.Tests` plus `AgentTaskPipelineStatusTests` plus the 3 new tests, in the table. Argument expansion can only raise a floor.

## Advisories (do not hold the land)

- **A-1. Canonical call-site description.** Qualified `HostLoadCounterCache.Canonical(` calls under `server/`: CapacityReader `:15` and `:20`, HistoryService `:171` and `:177`, IngestPump `:26`, `:41`, `:53`, IngestService `:28`, `:78`, and two on `:97`, TaskWindowObserver two on `:73`. That is 13. The cache also calls `Canonical` internally at `:14` (twice) and `:22`. The plan's "12 call sites" matches the qualified calls once `:20` is counted as the separate CapacityReader fold. `AgentTaskPipelineStatusService.cs:287` is the literal `local` to `"desktop"` fold. `:296` is `_hostLoad?.Get(r.RunnerId)` on `HostLoadShadowCache`, which does not canonicalize. W-7's catalogue `desktop` row stays on the old shadow key unless that Get argument becomes the direct key. Delete the static method so every leftover, including `Observe` and `Nearest`, fails to compile. The conflict throw is `:40`; the cursor query is `:39`.
- **A-2. Old desktop series.** `HostLoadHistoryService.Known` (`:169-173`) and `Binding` (`:175-179`) pass the host through `Canonical` before lookup. After the change, `Canonical("desktop")` is `server2-direct`, so a read of `desktop` does not return rows still stored under `desktop`. The stored rows remain. Events with `LoadRunnerId="desktop"` (`HostLoadModel.cs:73`) mean the direct runner and will be read as the new key. State that, so Code does not migrate events and does not claim the old sample series stays visible through the new key.
- **A-3. Phase 2 checkout.** The logon task runs `autostart-session-runner.ps1` by the path `install-autostart.ps1` recorded (`:94`, `:193`). S4 is live only in the checkout that path points at. The window step should say that checkout contains S4 before the json write and `restart-session-runner.ps1 -Hard`. Otherwise the json is ignored and 17204 stays a direct runner.
- **A-4. O-9 predicate.** The drain is Queued and Blocked. A Dispatched or Working Windows task with `RunnerId` null is outside that predicate and will mismatch on the next revalidation. That remains a parent-window problem.
- **A-5. CARD-1987.** S1+S2 task `cd7b1a6e-6998-4a83-ada6-1be6f3f3c3b2` is Dispatched. `origin/feat/card-task-cd7b1a6e` is `7b698ec948b1dbbad262c9f7f4f1a18b4c9f4975`. Its files against this master are the 1987 checkpoint plan, `AgentSessionRuntime.cs`, `RunnerBindingInvalidation.cs`, `PhoneHomeRunnerDirectory.cs`, `RoutingSessionRunnerClient.cs`, `AppDbContext.cs`, `RunnerBindingWriteInterceptor.cs`, `AgentSessionRuntimeActivityTests.cs`, and `RoutingBindingCacheTests.cs`. None is in this plan's edit set. Repeat the check at land; the branch has grown since the plan's scan.
- **A-6. `HostWorkspaceRoot`.** D-5 reuses `${PHONE_HOME_HOST_WORKSPACE_ROOT}` for `desktop-win`. The validator requires a non-blank absolute path. `EnsureExactHostRoot` applies to pinned named agents. Acceptable for a pool. O-6 already keeps Windows pinned shapes off this runner.
- **A-7. Estimates.** CP rows are 2-15 minutes. None is near 30. `3 * 14 = 42`, which is inside the importer's 45-minute derived-timeout warning. `dotnet run` under the slot gate applies `-maxcpucount` to the program, so the implicit compiler build used the default node count. Same note as the CARD-1987 plan review. Not a plan defect.

## Decisions D-1..D-10 and O-5..O-9

| Decision | Review |
|---|---|
| D-1 creation-time scan, no map | Yes, once B-1 sets the pool flag |
| D-2 same two predicates on the scan | Yes |
| D-3 new direct-runner key, no migration | Mechanics yes; validation sentence is B-3 |
| D-4 mandatory secret and home | Yes; the WhatIf seam is B-2 |
| D-5 compose values | Blocked by B-1 |
| D-6 secret file, no display | Yes |
| D-7 phase 1 from the clone, capacity 1, port 17214 | Yes |
| D-8 one logon task, json switch | Yes, with A-3 |
| D-9 broker stays localhost:17204, switch between Full runs | Yes |
| D-10 delete json, then restart, then parent R-2 | Yes |
| O-5..O-9 recommended Yes | Yes, O-5 via B-2's seam |

## Import

Read-only import of the subject plan. Exit 0. Nine rows, no importer warning. Output
`.antiphon/c1744-g1-g3-import.yaml` (gitignored, not part of this commit).

| CP | minExecuted | timeoutMinutes | note |
|---|---|---|---|
| CP-1 | 3 | 45 | filter `C1744G1*`, expect red 3 |
| CP-2 | 18 | 36 | matches 15 contract tests + 3 new |
| CP-3 | 2 | 36 | W-4 pass, W-5 fail |
| CP-4 | 17 | 36 | B-4 |
| CP-5 | 3 | 36 | W-6 and W-7 fail, W-8 pass |
| CP-6 | 3 | 42 | B-4; `3 * 14` does not warn |
| CP-7 | n/a (command) | 15 | exit 1, output contains FAIL |
| CP-8 | n/a (command) | 15 | exit 0, failed=0 |
| CP-9 | 1 | 36 | B-4 |

Builds are `tests/Antiphon.Tests` to `bin-c1744g1r/`, `bin-c1744g1/`, `bin-c1744g2pr/`, `bin-c1744g2p/`, `bin-c1744g2lr/`, `bin-c1744g2l/`, and `bin-c1744doc/`. Red and green use different `After` groups. Command rows have no Min.

## Mechanics

- `scripts/check-evidence-diff.ps1` base `997f9b8d9ffc4ba870d2fd56caf7c03781591624` head `891ef27d2fc4999e4f519c1f2d5a0527e6f68954`: commits=2 entries=0 violations=0, exit 0.
- `git fetch origin master`, then `git merge-tree --write-tree origin/master 891ef27d2fc4999e4f519c1f2d5a0527e6f68954`: tree `75b583a00f359385e447dfad61e7edc5ebc3bccc`, exit 0. `origin/master` is still `997f9b8d9ffc4ba870d2fd56caf7c03781591624`.
- Impacted-classes JSON for that range: kinds, classes, and files empty. `-MergeSubset`: `migrations=False`, kinds empty. Diff-selected downgrade count 0. The class list that command prints is the always-on merge subset, not rows this diff added.
- No migration. No public DTO or route change in the plan.

## Tests

None run. The subject changes no code and no test, so the touched-class rerun and the guard batch do not apply. The CP rows name tests that do not exist yet. All positive controls stay pending.
