# CARD-1744 blockers G-1, G-2, G-3: desktop-win runner, Windows routing, desktop supervision and build slots (plan, 2026-11-18)

Plan task `a2c6e640`. Read-only planning on server2 runner mirror `/work/worktrees/ws-06`.
Master read: `997f9b8d9ffc4ba870d2fd56caf7c03781591624` (= `origin/master` at fetch time).
Input investigation: `docs/investigations/2026-11-18-server-to-server2-docker-plan.md` at
`ff06bf9354d84725f8c3f3d99a9cf900d8930210` (branch `feat/card-task-b56e40f0`, not on master).
Parent plan: [2026-11-08-card-1744-cutover-plan.md](2026-11-08-card-1744-cutover-plan.md) (D-5 sidecar, D-9 desktop-win).

Live reads: `GET /api/runner-defaults` → revision 2, `globalRunnerId=server2`, no kind defaults.
`GET /api/session-runners` returned:
- `desktop`: windows, available, eligible, capacity 2.
- `server2`: linux, available, eligible, capacity 10.
- `server2-temp`: not available.

What I did not do: no build, no test, no deploy, no restart, no setting change, no card move, no
database connection, no secret read.

**Amendment 1 (2026-11-18, Docs task `2f24f3cc`):** fixes the four blockers from Review `1a877722`
round 1 (`docs/superpowers/reviews/2026-11-18-card-1744-blockers-g1-g3-plan-review.md` at
`caa7271127f2deae8cf417db7ea1b0e0a46bca03`). B-1: D-5, W-1 and W-3 require
`AllowDelegatedTasks: "true"` and no `StandingAgentId`. B-2: S1 and W-2 put the two-key check
before the `-WhatIf` return. B-3: D-3 allows `server2-direct`. B-4: CP-4, CP-6 and CP-9 name real
classes, and the floors are recounted from the `[Test]` and `[Arguments]` attributes at `997f9b8d9`.
Nothing else in the design changed. O-5 to O-9 are unchanged.

## Verdict

- **G-2 is much smaller than the investigation assumed.** Create-time platform placement already
  exists (`AgentTaskService.SelectConstrainedAutomaticAsync` → `SelectPlatformCandidateAsync`,
  `server/Application/Services/AgentTaskService.cs:2129-2230`).
  - It checks the desktop (`local`) first.
  - It then scans every known phone-home runner in ordinal order for a dispatch-eligible runner on
    the required platform.
  - After cutover, `local` is the Linux sidecar. An unpinned `-Platform Windows` task would
    therefore already select `desktop-win`, with no platform-to-runner map and no dispatcher change.
  - `AgentTaskDispatcher.ClassifyRequiredPlatformAsync` (`:6795`) is a **dispatch-time revalidation
    of the frozen host**, not routing. It correctly fails closed with `runner_platform_mismatch`.
- **G-2's real defects are three narrow ones:**
  1. **The fallback scan does not ask `AllowsDelegatedTasks`/`IsRunnerBound`.** Unlike
     `TryPlatformPreferenceAsync`, it can select a runner that does not admit delegated tasks.
  2. **The direct-runner host-load key is hard-wired to `"desktop"`** (`HostLoadCounterCache.Canonical`,
     `AgentTaskPipelineStatusService.cs:287`). After cutover:
     - the sidecar's load shows as the desktop's;
     - **every history ingest for `local` throws a conflict**, because the persisted `"desktop"`
       cursor holds the desktop runner's `RunnerStoreId` (`HostLoadIngestService.cs:39`).
  3. **Queued Windows tasks created before the window with `RunnerId=null` block at dispatch after it.**
     This is a rollout step, not code.
- **G-1 is a compose and deploy-script slice with no server code.**
- **G-3 needs only script changes, and the key one is ordering.**
  - The phone-home runner already serves `/build-slots` on its own port.
  - `build-slot.ps1` already defaults to `localhost:17204`.
  - So at the switch-over, `desktop-win` takes 17204 under the **same** Scheduled Task, executable path
    and pid files as today's direct runner.
  - `restart-session-runner.ps1` and the Windows qualification then need no change.
  - Phase 1 (17214) **must run from a separate checkout**. `restart-session-runner.ps1` kills every
    `Antiphon.SessionRunner` process at the main checkout's Debug executable path, and the
    supervisor's rebuild would hit a locked executable.

## Ground truth (card or investigation assumption vs code at `997f9b8d9`)

| # | Assumption | Code | Consequence |
|---|---|---|---|
| T-1 | A Windows-pinned task with no runner blocks once `local` is Linux (investigation §2.3) | Create-time `SelectPlatformCandidateAsync` (`AgentTaskService.cs:2196-2230`) falls past a non-matching desktop to the first eligible phone-home runner on the platform. The dispatcher only revalidates the frozen host | No map or dispatcher fallback is needed for new tasks. Pin it with a witness (W-4) |
| T-2 | The platform scan is as strict as the preference path | `TryPlatformPreferenceAsync` (`:2167-2194`) checks `IsRunnerBound` and `AllowsDelegatedTasks`. The scan (`:2209-2225`) checks only eligibility, platform and feature | Defect. Red witness W-5 |
| T-3 | `ExclusionFor` shapes can still go to Windows after cutover | `:2147-2160`: an excluded shape is offered the desktop only. After cutover that is Linux, so the result is 409 `RunnerPlatformProblems.Unavailable` | Accepted by design: these shapes cannot run on phone-home (ReadOnly, Shared, existing process, specialists, non-Claude orchestrators, Codex SourceLanding). Decision O-6 |
| T-4 | The host-load label is cosmetic | `HostLoadCounterCache.Canonical` folds `local`/`desktop` to `"desktop"` for cursor, samples and shadow cache. `HostLoadIngestService.cs:38-39` throws a conflict when the cursor's `RunnerStoreId` differs from the page's | After cutover, `local` ingest fails forever (`LastFailure=history_commit_unavailable`), and the old desktop series and the sidecar series would merge. Red witness W-6 |
| T-5 | The pipeline shows the desktop under `local` | `AgentTaskPipelineStatusService.cs:287` reads `Get(h.HostId == "local" ? "desktop" : h.HostId)`. `:296` reads `Get(r.RunnerId)`, and the catalogue row id is `desktop` | Both read the configurable direct key after S3. Red witness W-7 |
| T-6 | The compose has no `desktop-win` | `docker-compose.server2-server.yml` declares only `PhoneHomeRunner__Runners__server2__*`. Deploy requires 6 keys (`deploy-server2-server.ps1:347`) | S1 adds the entry and 2 required keys |
| T-7 | `HostWorkspaceRoot` needs a desktop path | It is used only by `EnsureExactHostRoot` for **pinned named agents** (`PhoneHomeLaunchPolicy.cs:202-208`). Pool delegates never read it; the validator only requires it to be non-blank (`PhoneHomeRunnerSettings.cs:298`) | Reuse `${PHONE_HOME_HOST_WORKSPACE_ROOT}`. No new path |
| T-8 | Children of a phone-home runner lease from it | Nothing injects `ANTIPHON_BUILD_SLOTS_URL` into children (grep: only `HostStatsRoutes.cs:29` reads it). `build-slot.ps1` defaults on Windows to `http://localhost:17204/build-slots` (`scripts/lib/build-slot.ps1:28-29`) | Phase 1: `desktop-win` children share the direct runner's broker, so there is one budget per host. Phase 2: `desktop-win` on 17204 *is* the broker |
| T-9 | The restart script is port-scoped | `session-runner-restart-health.ps1:110-118` stops the pid-file wrapper **and every `Antiphon.SessionRunner` at `<checkout>\src\...\bin\Debug\net9.0\Antiphon.SessionRunner.exe`**. It starts the task `Antiphon Session Runner` and probes `127.0.0.1:17204/health` | Phase 1 `desktop-win` must not run from the main checkout. Phase 2 keeps the same executable path, pid files, task and port, so the script needs no change |
| T-10 | The autostart task can run phone-home | `autostart-session-runner.ps1` runs `run-daemon.ps1` with `--urls http://localhost:17204` and inherited process env. `run-daemon.ps1` launches `UseShellExecute=false`, so env is inherited (`:166`) | Phone-home = set the `PhoneHome__*` env in the autostart script before `run-daemon`. One code path |
| T-11 | The Windows qualification depends on the AppHost or the API | `scripts/qualification/*.ps1`: no `/api`, `ANTIPHON_API`, 17202 or AppHost reference. `launch-rc.ps1:64` wraps the whole `nightly-run.ps1` in **one** `build-slot.ps1` lease (renew-mode thread, `scripts/lib/build-slot.ps1:108-115`) | The switch-over must happen **between** Full runs. Broker leases are in memory, the same as any runner restart today |

## Decisions

- **D-1 (G-2 routing): no platform-to-runner map and no dispatcher fallback.** Keep create-time
  selection, which is already a platform scan, and harden the scan (S2).
  - *Rejected: a `PlatformDefaults` map in the runner defaults.* It needs a migration, a public DTO
    change (`PUT /api/runner-defaults`) and a snapshot change, all to say what the scan already
    finds. It would also be a second source of truth beside the platform each runner reports.
  - *Rejected: a dispatch-time reroute in `ClassifyRequiredPlatformAsync`.* CARD-0659 D-5 says the
    host never changes after create, and every custody, worktree and land path assumes a frozen
    `RunnerId`.
  - Blast radius of S2: one `foreach` filter in `AgentTaskService.SelectPlatformCandidateAsync`.
- **D-2 (G-2 scan filter):** the fallback scan skips a runner unless
  `_phoneHome?.IsRunnerBound(id) == true && _phoneHome.AllowsDelegatedTasks(id)`. These are the same
  two predicates `TryPlatformPreferenceAsync` uses. With no phone-home policy, behaviour is unchanged
  (desktop only).
- **D-3 (G-2 host load): a configurable direct-runner load key.** New setting
  `HostLoadHistory:DirectRunnerKey` (default `"desktop"`, so the desktop topology is byte-for-byte unchanged).
  - **Validation:** `^[a-z0-9][a-z0-9-]{0,63}$`. Refuse `local`. Refuse any configured phone-home
    runner id, ordinal-ignore-case, taken from `PhoneHomeRunnerCatalog.Configured`. That list covers
    the map keys, including disabled entries, and the legacy `AllowedRunnerId`. In production it
    holds `server2` and `desktop-win`. Every other matching value is legal, and the
    default is `desktop`.
  - **Production:** the compose sets `server2-direct`. That is a legal non-default value: it is not
    `local`, and it is not a phone-home runner id.
  - **Mechanics:** one injected singleton `HostLoadRunnerKeys` with `Canonical(string id)` replaces
    the static `HostLoadCounterCache.Canonical` at its 12 call sites. It also replaces the two
    literal folds in `AgentTaskPipelineStatusService.cs:287` and `:296` (the catalogue `desktop` row)
    and the one in `HostLoadCapacityReader.cs:19-20`. The budget host id stays `local`.
  - **Events keep `LoadRunnerId="desktop"`** (`HostLoadModel.cs:73`), which means "the direct runner".
    Readers map it through `Canonical`. This avoids putting settings into `AppDbContext`, and the
    task-window observer stays consistent.
  - **Effect at cutover:**
    - A new cursor row `server2-direct` starts with the sidecar's store id, so there is no conflict.
    - The old `desktop` series stays as truthful desktop history.
    - `desktop-win` has its own series.
  - *Rejected: renaming `"desktop"` to `"local"` everywhere.* It needs a data migration of
    cursors, samples and events in the production database, with no gain.
  - *Rejected: relaxing the store-id conflict.* That check guards against mixing two spools under one id.
  - *Rejected: label-only (display name) changes.* They leave the T-4 ingest conflict in place.
- **D-4 (G-1 compose): `desktop-win` is a fixed, enabled entry with two new required host keys**:
  - `PHONE_HOME_DESKTOP_WIN_SHARED_SECRET`;
  - `PHONE_HOME_DESKTOP_WIN_HOME`: the desktop user's profile in drive-rooted forward-slash form, for
    example `C:/Users/<operator>`, from which the three child homes derive.

  Both use the `:?` form. `deploy-server2-server.ps1` refuses an env file that lacks either key, on
  `-WhatIf` as well as `-Execute` (S1, W-2). An entry with no connected runner is just unavailable,
  which is harmless.
  - *Rejected: an optional entry behind `DESKTOP_WIN_ENABLED`.* Cutover without `desktop-win` loses
    the Windows lane silently. Fail closed at deploy time instead.
  - No user name or fleet location is written into the compose.
- **D-5 (G-1 values):**

  | Key | Value |
  |---|---|
  | `Enabled` | `"true"` |
  | `AllowDelegatedTasks` | `"true"`: required (see below) |
  | `StandingAgentId` | **not set** |
  | `DisplayName` | `desktop-win` |
  | `SharedSecret` | `${PHONE_HOME_DESKTOP_WIN_SHARED_SECRET:?...}` (D-4) |
  | `RunnerWorkspace` | `C:/Antiphon/runner/work` |
  | `RunnerRepository` | `C:/Antiphon/runner/work/repos/antiphon` |
  | `MaxCapacity` | `2` (today's desktop capacity is 2) |
  | `ChildClaudeHome` / `ChildCodexHome` / `ChildGrokHome` | `${PHONE_HOME_DESKTOP_WIN_HOME}/.claude`, `/.codex`, `/.grok` |
  | `RawExeAllowList__0` | `C:/Program Files/PowerShell/7/pwsh.exe` |
  | `CallbackOrigin` | `${ANTIPHON_PUBLIC_ORIGIN}` |
  | `HostWorkspaceRoot` | `${PHONE_HOME_HOST_WORKSPACE_ROOT}` (T-7) |
  | Auth probes | `ClaudeAuthProbeEnabled`/`CodexAuthProbeEnabled` `"true"` |

  These are the same 14 keys as the existing `server2` entry (`docker-compose.server2-server.yml:56-69`),
  plus `RawExeAllowList__0`: 15 `PhoneHomeRunner__Runners__desktop-win__*` lines.
  - **Why `AllowDelegatedTasks` and no `StandingAgentId` (Review B-1).**
    - `PhoneHomeRunnerEntry.AllowDelegatedTasks` defaults to false and `StandingAgentId` to empty
      (`PhoneHomeRunnerSettings.cs:186-189`).
    - `ValidateMapped` refuses that pair (`:386-387`).
    - A `StandingAgentId` would silence the validator, but `AllowsDelegatedTasks` would then be
      false. The S2 scan (D-2) would skip `desktop-win`, and an unpinned Windows task would 409,
      which falsifies D-1 and W-4.
    - So the entry is a pool: the flag is `"true"` and there is no standing agent. Code must not
      add a `StandingAgentId` to make W-3 pass.

  W-1 asserts the bound list: the config binder may keep the default Linux entries after index 0
  (UNOBSERVED). Extra absolute Linux paths are harmless on Windows, but the witness records which happens.
- **D-6 (G-1 tokens):** generate the secret once on the desktop with the existing laptop recipe and
  write it to `C:\Antiphon\runner\secrets\phone-home`.
  - Phase 1 (O-4, 17214): the operator puts the same value into the desktop `antiphon-server`
    user-secrets `PhoneHomeRunner:Runners:desktop-win:SharedSecret`.
  - Phase 2: the same value goes into the server2 0600 env file as
    `PHONE_HOME_DESKTOP_WIN_SHARED_SECRET`.
  - No rotation is needed at the window. Never displayed.
- **D-7 (G-3 phase 1 runs from its own checkout):** `C:\Antiphon\runner\app`, a clone at the rc tip
  that the operator updates. Started as `pwsh -NoProfile -File C:\Antiphon\runner\app\scripts\start-windows-phone-home-runner.ps1 -RunnerId desktop-win -Port 17214 -Capacity 1`
  from a normal terminal, after the live Full run ends (its `-NoBuild`-less start runs a leased build).
  The script resolves its executable from its own repo root, so the main checkout's restart
  (T-9) and rebuilds never touch it. No code change. Phase 1 has no autostart: a reboot needs a
  manual start. That is accepted for a proving phase.
- **D-8 (G-3 phase 2: one supervisor, same identity).** At the switch-over, `desktop-win` becomes the
  process behind the existing task `Antiphon Session Runner`, on 17204, from the main checkout's
  Debug executable, with the same `logs/session-runner.*` pid and state files.
  - **S4 changes:**
    - Extract the env table from `start-windows-phone-home-runner.ps1` into
      `scripts/lib/windows-phone-home-env.ps1`, a pure function
      `New-WindowsPhoneHomeRunnerEnv -RunnerId -Root -Port -Capacity -ServerOrigin -ClaudeHome -CodexHome -GrokHome -PwshPath -Executables`.
    - `autostart-session-runner.ps1` reads an optional ASCII config
      `C:\Antiphon\runner\phone-home.json`: `runnerId`, `serverOrigin`, `capacity`, `root`; no secret.
      When the file is present, it sets that env in-process before `run-daemon.ps1`. Absent means
      today's direct runner, byte-for-byte.
    - `install-autostart.ps1` gains nothing. The task action is unchanged, and the mode lives in the
      config file, so the switch-over and rollback are a file write plus `restart-session-runner.ps1`.
  - **Effect:** reboot survival is the existing logon task (Interactive, `RestartCount 3`). That is
    the same as today: logon-triggered, so the desktop needs a user logon after a reboot.
  - **Who restarts:**
    - `restart-session-runner.ps1` from the main checkout, which orchestrators may already run (AGENTS.md).
    - `-Hard` refreshes the supervisor.
    - `-KillSessions` stays human-only.
  - *Rejected: a second Scheduled Task.* It would mean two supervisors on one executable path, and
    the restart census would kill both (T-9).
  - *Rejected: the AppHost logon task.* It is disabled at cutover (parent D-6).
  - *Rejected: a `-Port 17204` flag on the start script alone.* It gives no supervision.
- **D-9 (G-3 build slots): `localhost:17204` stays the desktop broker address.** No discovery, and no
  `ANTIPHON_BUILD_SLOTS_URL` machine variable.
  - Phase 1: the direct runner brokers for the host. `desktop-win` children default to it (T-8).
  - Phase 2: `desktop-win` brokers, and the host has one broker in both phases.
  - The Windows Full run supervisor needs only that broker and a switch-over **between** runs (T-11).
- **D-10 (rollback ordering):** before any R-2 AppHost restart, delete `phone-home.json` and run
  `restart-session-runner.ps1`. Otherwise the AppHost would adopt a 17204 listener that is also
  registered as `desktop-win`. S5 adds this to the parent runbook R-2.

## Operator decisions (yes/no, with recommendation)

| # | Question | Recommendation |
|---|---|---|
| O-5 | Make `desktop-win` mandatory in the production compose: deploy refuses without its secret and home (D-4)? | **Yes.** Fail closed; the Windows lane is a cutover requirement |
| O-6 | Accept that Windows-only ReadOnly, Shared, existing-process and specialist work has no host after cutover (409 at create, T-3)? | **Yes.** Phone-home cannot carry those shapes. Re-create such work as Worktree tasks, or run it in the rollback window |
| O-7 | Phase 1 `desktop-win` at capacity 1 from `C:\Antiphon\runner\app`, started by hand after the live Full run (D-7)? | **Yes** |
| O-8 | Switch-over by config file under the existing logon task (D-8), not a second task? | **Yes** |
| O-9 | Pre-window drain: every Queued or Blocked task with `RequiredPlatform=Windows` and `RunnerId` null is either dispatched before the window or cancelled and re-created after it (R-3 below)? | **Yes.** It is the only way to avoid post-window `runner_platform_mismatch` blocks without a reroute |

Standing operator defaults O-1 to O-4 apply unchanged. O-4 is implemented by D-7 and D-8.

## Slices

| Slice | Gap | Files | Tests (red first) |
|---|---|---|---|
| S1 | G-1 | `docker-compose.server2-server.yml` (+15 `PhoneHomeRunner__Runners__desktop-win__*` lines, the full D-5 entry with `AllowDelegatedTasks: "true"` and no `StandingAgentId`, + `HostLoadHistory__DirectRunnerKey: server2-direct` for S3); `scripts/deploy-server2-server.ps1`: (a) in the env-file branch (`:307-335`), after the `dev-password` and `hangfire-storage` checks and **before** `if ($planOnly) { return }` (`:341-344`), refuse `env-missing` exit 2 when `PHONE_HOME_DESKTOP_WIN_SHARED_SECRET` or `PHONE_HOME_DESKTOP_WIN_HOME` is blank; (b) add the same two keys to the execute key list (`:347`) so the two lists stay in sync; (c) `Test-ServerCompose` needs list `:120-160`. `-WhatIf` with no `-EnvFile` stays exit 0 (the no-file refusal at `:336-338` stays `-not $planOnly` only); `docs/docker-stack.md` `:2164` key list + D-9 note | New `tests/Antiphon.Tests/Infrastructure/C1744G1DesktopWinComposeTests.cs` (W-1..W-3); existing `Server2ProductionServerContractTests` fixtures gain the two keys |
| S2 | G-2 scan | `server/Application/Services/AgentTaskService.cs` `SelectPlatformCandidateAsync` loop `:2209-2212` only | New `tests/Antiphon.Tests/Application/C1744G2PlacementTests.cs` (W-4 pin, W-5 red) |
| S3 | G-2 load | `server/Application/Settings/HostLoadHistorySettings.cs` (+`DirectRunnerKey`, Validate); new `server/Infrastructure/HostLoad/HostLoadRunnerKeys.cs`; `HostLoadRegistration.cs`; `HostLoadCounterCache.cs`, `HostLoadCapacityReader.cs`, `HostLoadHistoryService.cs`, `HostLoadIngestPump.cs`, `HostLoadIngestService.cs`, `HostLoadTaskWindowObserver.cs` (Canonical call sites); `server/Application/Services/AgentTaskPipelineStatusService.cs:287,296` | New `tests/Antiphon.Tests/Application/C1744G2HostLoadKeyTests.cs` (W-6, W-7 red; W-8 default unchanged) |
| S4 | G-3 | new `scripts/lib/windows-phone-home-env.ps1`; `scripts/start-windows-phone-home-runner.ps1` (use the helper; behaviour unchanged); `scripts/autostart-session-runner.ps1` (optional `phone-home.json`); ASCII-only | New `scripts/test-windows-phone-home-runner.ps1` `-Case` harness (W-9..W-11), runnable on Linux pwsh (pure functions; no Scheduled Task call) |
| S5 | docs/runbook | `docs/docker-stack.md` (Native Windows phone-home runner → desktop-win phase 1/phase 2, D-9 broker), parent plan runbook amendment as a **separate file** `docs/superpowers/plans/2026-11-18-card-1744-runbook-g1-g3-amendment.md` (R-1..R-4 below), `docs/logs.md` desktop-win log paths | `DockerStackDocumentationTests` (existing) |

S1, S2, S3 and S4 touch disjoint files except `docker-compose.server2-server.yml`, which S1 and
S3 both touch. **Put the S3 compose line in S1** so that only one slice edits the compose. S3 then
lands its code with the default and needs no compose edit. S2 and S3 can be one Code task (both
G-2), or two in parallel.

### Witnesses

| W | Slice | Test | Red at base because |
|---|---|---|---|
| W-1 | S1 | `C1744G1DesktopWinComposeTests.G1_compose_declares_desktop_win_with_drive_rooted_paths`: parses the compose; all D-5 keys present, including `Enabled: "true"` and `AllowDelegatedTasks: "true"`; no `PhoneHomeRunner__Runners__desktop-win__StandingAgentId` key; paths drive-rooted forward-slash; secret and home are `:?` | entry absent |
| W-2 | S1 | `G1_deploy_whatif_refuses_without_desktop_win_keys`: `-WhatIf -EnvFile <file>` where the file is otherwise valid (0600, outside the repo, non-dev password, `HANGFIRE_STORAGE=InMemory`, all six existing keys) and omits one new key at a time → `SERVER2-SERVER fail exit=2 reason=env-missing`, no docker call (same seam and fake docker marker as `Execute_refusals_stop_before_docker`). The same file with both keys → `plan` exit 0. `WhatIf_prints_the_plan_and_calls_no_docker` (no `-EnvFile`) stays exit 0 unchanged | the WhatIf path returns at `:341-344` before any key list is read, so it exits 0 `plan` |
| W-3 | S1 | `G1_compose_binds_valid_phone_home_settings`: binds the compose `server` env (with fake values) into `PhoneHomeRunnerSettings` and runs its validator: 0 failures for `desktop-win`; asserts the bound entry has `Enabled == true`, `AllowDelegatedTasks == true` and `StandingAgentId == Guid.Empty`; records the bound `RawExeAllowList` | entry absent |
| W-4 | S2 | `C1744G2PlacementTests.Windows_task_selects_desktop_win_when_local_is_linux`: matrix `desktop`=linux, `desktop-win`=windows, `server2`=linux, global default `server2`; unpinned `RequiredPlatform.Windows` Worktree Code task → `RunnerId=desktop-win`, source Fallback, reason `platform_match` | green at base (pin of T-1; stated, not a red witness) |
| W-5 | S2 | `Fallback_scan_skips_runner_without_delegated_tasks`: `desktop-win` eligible windows but `AllowDelegatedTasks=false` → 409 `RunnerPlatformProblems.Unavailable`, no row | scan ignores the flag (T-2) |
| W-6 | S3 | `C1744G2HostLoadKeyTests.Direct_key_change_starts_a_new_cursor`: seeded cursor `desktop` with store A; `DirectRunnerKey=server2-direct`; batch for `local` with store B → inserted > 0, cursor row `server2-direct` | conflict thrown (T-4) |
| W-7 | S3 | `Pipeline_local_host_reads_the_direct_key`: shadow `desktop`=X, `server2-direct`=Y; pipeline `Hosts[local].Load`==Y and the catalogue `desktop` row Load==Y | reads `"desktop"` (T-5) |
| W-8 | S3 | `Default_direct_key_is_desktop_and_unchanged`: default settings: `local` and `desktop` fold to `desktop`; validator refuses `local` and a phone-home id | green at base (no-behaviour-change guard; red only for a wrong default) |
| W-9 | S4 | `-Case env-table`: helper output equals the start script's current table key-for-key for fixed inputs | helper absent |
| W-10 | S4 | `-Case autostart-direct`: no `phone-home.json` → no `PhoneHome__*` variable set, ExeArgs `--urls http://localhost:17204` | helper/config reader absent |
| W-11 | S4 | `-Case autostart-phone-home`: config present → `PhoneHome__RunnerId=desktop-win`, `PhoneHome__SecretPath` under root, port stays 17204, no secret content read or printed; reserved ids `local`/`desktop` refused | absent |

Mutation targets for later: drop either D-2 predicate (W-5); fold `local` back to the literal
(W-6/W-7); default key other than `desktop` (W-8); the helper drops `SessionRunner__Herdr__Enabled=false` (W-9).

### Checkpoints

Lane: server2 Linux for every row (`-Runner server2`, no `-Platform`). No desktop build: the
operator forbids desktop load during the live Full run, and nothing here needs Windows to compile or test.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-red | tests/Antiphon.Tests -> bin-c1744g1r/ | g1-red | `/*/*/(C1744G1*)/*` | W-1..W-3 | red: 3 failed, 0 passed | 3 | 15 |
| CP-2 | S1 | tests/Antiphon.Tests -> bin-c1744g1/ | g1-green | `/*/*/(C1744G1*)\|(Server2ProductionServerContractTests*)/*` | W-1..W-3, existing contract | 0 failed | 18 | 12 |
| CP-3 | S2-red | tests/Antiphon.Tests -> bin-c1744g2pr/ | g2p-red | `/*/*/(C1744G2PlacementTests*)/*` | W-4, W-5 | 1 failed (W-5), 1 passed (W-4) | 2 | 12 |
| CP-4 | S2 | tests/Antiphon.Tests -> bin-c1744g2p/ | g2p-green | `/*/*/(C1744G2PlacementTests*)\|(TaskPlatformPlacementTests*)\|(RunnerDefaultsWireTests*)\|(OutboundConversionPublicCreateTests*)\|(RunnerDefaultSettingsTests*)\|(RunnerDefaultPlacementTests*)\|(RunnerDefaultMigrationTests*)/*` | W-4, W-5, existing placement, the whole `RunnerDefaultTests.cs` file | 0 failed | 43 | 12 |
| CP-5 | S3-red | tests/Antiphon.Tests -> bin-c1744g2lr/ | g2l-red | `/*/*/(C1744G2HostLoadKeyTests*)/*` | W-6..W-8 | 2 failed (W-6, W-7), 1 passed | 3 | 12 |
| CP-6 | S3 | tests/Antiphon.Tests -> bin-c1744g2l/ | g2l-green | `/*/*/(C1744G2HostLoadKeyTests*)\|(C1514HostLoad*)\|(AgentTaskPipelineStatusTests*)/*` | W-6..W-8, existing host-load + pipeline (including the `HostBudgetPipelineTests.cs` partial) | 0 failed | 121 | 14 |
| CP-7 | S4-red | n/a | g3-red | `pwsh -NoProfile -File scripts/test-windows-phone-home-runner.ps1` | W-9..W-11 | exit 1, output contains `FAIL` | n/a | 2 |
| CP-8 | S4 | n/a | g3-green | `pwsh -NoProfile -File scripts/test-windows-phone-home-runner.ps1` | W-9..W-11 | exit 0, `failed=0` | n/a | 2 |
| CP-9 | S5 | tests/Antiphon.Tests -> bin-c1744doc/ | docs | `/*/*/(DockerStackDocumentationTests*)/*` | S5 | 0 failed | 20 | 12 |

Filter roster check (amendment 1, counted at `997f9b8d9` from `[Test]` and `[Arguments(` attributes;
a `[Test]` with k `Arguments` runs k cases):
- `C1744G1*` and `C1744G2PlacementTests*`/`C1744G2HostLoadKeyTests*` match no existing class (grep:
  no `C1744` class at base). The new classes hold 3, 2 and 3 tests.
- CP-2: `Server2ProductionServerContractTests` has 15 `[Test]`, no `Arguments`. 15 + 3 = **18**.
- CP-4 names the five classes `tests/Antiphon.Tests/Application/RunnerDefaultTests.cs` declares.
  There is no class `RunnerDefaultTests`. `RunnerDefault*` would miss
  `OutboundConversionPublicCreateTests` and would also take `RunnerDefaultGuidanceTests` from
  `TaskPlatformGuidanceTests.cs`, so each class is named exactly.

  | Class | `[Test]` | Cases |
  |---|---|---|
  | `TaskPlatformPlacementTests` | 15 | 15 |
  | `RunnerDefaultsWireTests` | 5 | 5 |
  | `OutboundConversionPublicCreateTests` | 1 | 1 |
  | `RunnerDefaultSettingsTests` | 6 | 6 |
  | `RunnerDefaultPlacementTests` | 9 | 9 |
  | `RunnerDefaultMigrationTests` | 5 | 5 |
  | `C1744G2PlacementTests` (new: W-4, W-5) | 2 | 2 |
  | **CP-4 Min** | | **43** |

- CP-6. `(HostBudgetPipelineTests*)` was dropped: `HostBudgetPipelineTests.cs` declares
  `partial class AgentTaskPipelineStatusTests`, which the filter already names. Nested helper
  classes (`C557Seed`, `TempWorkspace`, `BlockingPushGit` and others) hold no tests.
  `AgentTaskPipelineEndpointTests` (4 tests, in `AgentTaskPipelineStatusTests.cs`) does not match
  and is not part of the row.

  | Class / file | `[Test]` | Cases |
  |---|---|---|
  | `AgentTaskPipelineStatusTests` in `AgentTaskPipelineStatusTests.cs` | 26 | 35 |
  | `AgentTaskPipelineStatusTests` in `AgentTaskPipelineStatusC557Tests.cs` | 19 | 19 |
  | `AgentTaskPipelineStatusTests` in `AgentTaskPipelineStatusC1076Tests.cs` | 4 | 9 |
  | `AgentTaskPipelineStatusTests` in `HostBudgetPipelineTests.cs` | 4 | 4 |
  | `AgentTaskPipelineStatusTests` in `AgentTaskPipelineStatusC100Tests.cs` | 1 | 1 |
  | `AgentTaskPipelineStatusTests` in `AgentTaskPipelineStatusC1329Tests.cs` | 1 | 1 |
  | `C1514HostLoadHistoryTests` | 12 | 22 |
  | `C1514HostLoadMigrationTests` | 4 | 9 |
  | `C1514HostLoadRecommendationTests` | 5 | 5 |
  | `C1514HostLoadApiTests` | 4 | 4 |
  | `C1514HostLoadScriptTests` | 4 | 4 |
  | `C1514HostLoadAggregateTests` | 3 | 3 |
  | `C1514HostLoadScenarioTests` | 2 | 2 |
  | `C1744G2HostLoadKeyTests` (new: W-6..W-8) | 3 | 3 |
  | **CP-6 Min** | 92 | **121** |

  The other `C1514HostLoad*` types (`Fixture`, `World`, `CrashCases`, `SqlProbe`, `Scenario`,
  `ProgressExecutor`) hold no tests. No class in CP-4 or CP-6 has a skip or platform attribute.
- CP-9: `DockerStackDocumentationTests` had 19 `[Test]` and no `Arguments` at `997f9b8d9`. Rechecked for S5a on master `a789c87770d31a77687f75a619c5af646d1ce91f`: 20 `[Test]` methods, no `[Arguments]`. S5a adds no test. Min is **20**. The S5a checkpoint row is CP-14 in `2026-11-19-card-1744-s5a-checkpoints.md`.
- If Code adds a test to a named class, it raises that row's Min by the added cases. It never
  lowers one. A run that executes fewer than Min is red, even with 0 failed.

## Existing tests affected

- Migrations: none. No EF change: `DirectRunnerKey` is configuration, and the cursor row is data.
- Public DTOs and routes: none changed. `HostLimitSummaryDto.Load` and catalogue `Load` keep their
  shape; only the key they read changes.
- Operation labels, remote script functions and statement budgets: none.
- Affected classes, all re-run in CP-2/4/6/9:
  - `Server2ProductionServerContractTests`: env fixtures gain the two keys for `WhatIf_prints_the_plan_and_calls_no_docker`, `Execute_refusals_stop_before_docker` and `Execute_drains_large_compose_output_without_echoing_a_secret`.
  - `TaskPlatformPlacementTests` (`Matrix()` has no phone-home policy, which D-2 keeps).
  - The five classes in `RunnerDefaultTests.cs`: `RunnerDefaultsWireTests`,
    `OutboundConversionPublicCreateTests`, `RunnerDefaultSettingsTests`, `RunnerDefaultPlacementTests`
    and `RunnerDefaultMigrationTests`.
  - `C1514HostLoad*`, and `AgentTaskPipelineStatusTests`, which includes the `HostBudgetPipelineTests.cs` partial.
  - `DockerStackDocumentationTests`.
- Impacted-classes table (CARD-1782, `tests/impacted-classes.json`), checked at base:
  - No kind globs any production file this plan edits.
  - The only host-load kinds are:
    - `timing-labels`, glob `tests/Antiphon.Tests/**/C1514HostLoad*.cs` → `TestTimingOperationLabelTests`;
    - `task-load-window`, glob `C1514HostLoadFixture.cs` → `C1514TaskLoadWindowTests`.
  - The new `C1744G2*` files match neither glob, so no row is needed.
  - Do not edit `C1514HostLoad*` test files. If Code must edit one, add `TestTimingOperationLabelTests`
    to CP-6.

## Collisions with in-flight work

- **CARD-1987 S1** (`RoutingSessionRunnerClient`, `AgentSessionRuntime`): **no line touched.** No
  slice edits `server/Infrastructure/Agents/SessionRunner/RoutingSessionRunnerClient.cs` or any
  `server/Application/Services/AgentSessionRuntime*.cs`.
- **Recently updated branches** (two days, origin/feat) were checked against this plan's files:
  - Branches touching `AgentSessionRuntime*` and one touching `RoutingSessionRunnerClient.cs`
    (`feat/card-task-cd7b1a6e`) overlap nothing here.
  - `feat/card-task-92c62298` and `74b623f9` touch **runner-side** `src/Antiphon.SessionRunner/HostLoad*`.
    S3 is server-side only, so there is no file overlap.
  - No branch touches `AgentTaskService.cs`, `AgentTaskPipelineStatusService.cs`,
    `server/Infrastructure/HostLoad/*`, the production compose or the deploy script.
  - Many old branches show `docs/docker-stack.md`. These are stale merge-bases: run `merge-tree` at land.
- **Parent CARD-1744 plan file:** amend it in a separate file (S5), never in place (add/add hazard).

## Rollout

1. **Land S1 to S5** on master, then port them to the rc line the product hosts track. The rc port
   drops plan docs.
2. **Phase 1 (O-4, 17214).** This runs after the live Windows Full run ends: no desktop load before
   that, and the AppHost restart waits for an idle run.
   - Operator steps:
     - Clone `C:\Antiphon\runner\app`.
     - Write the secret file.
     - Add the desktop user-secrets `PhoneHomeRunner:Runners:desktop-win:*` entry (D-5 values;
       `HostWorkspaceRoot` = `C:\src\Antiphon` pre-cutover).
     - Restart AppHost.
     - Start the runner by hand (D-7).
   - Witness:
     - `GET /api/session-runners` lists `desktop-win` as windows and eligible.
     - Two Windows Debug tasks dispatched **with** `-Runner desktop-win` complete, and one of them lands.
     - Unpinned Windows tasks still go to the direct runner in phase 1, because the scan tries
       `local` first (T-1). The unpinned path is proven only after the window (step 5).
3. **R-3 pre-window drain (O-9).** List Queued and Blocked tasks with `RequiredPlatform=Windows` and
   no runner (`GET /api/agent-tasks?...`, read-only), then dispatch them or cancel and re-create them.
4. **The window** (parent runbook, plus:)
   - The server2 env file gains `PHONE_HOME_DESKTOP_WIN_SHARED_SECRET` and `PHONE_HOME_DESKTOP_WIN_HOME`;
     `-WhatIf -EnvFile <that file>` must pass first; it refuses a missing key (W-2).
   - Stop the phase-1 runner (Ctrl+C).
   - Write `C:\Antiphon\runner\phone-home.json` (`runnerId desktop-win`, `serverOrigin` = public
     origin, `capacity 2`, `root C:\Antiphon\runner`).
   - Run `restart-session-runner.ps1 -Hard`. Its exit 0 means 17204 is healthy. Then
     `GET /api/session-runners/desktop-win/status` must show available and eligible.
   - Check that `build-slot.ps1 -Label probe -- pwsh -NoProfile -Command exit 0` prints a leased
     `BUILD SLOT` line.
   - Only between Full runs (T-11).
5. **Post-window checks:**
   - One unpinned `-Platform Windows` Worktree task lands on `desktop-win` (W-4 live).
   - The pipeline `hosts[local].load` is the sidecar's (S3).
   - Host-load `LastFailure` stays null.
6. **R-2 rollback addition (D-10):** delete `phone-home.json`, run `restart-session-runner.ps1`,
   and only then run the parent R-2 AppHost restart.

Waiting-session checklist: this plan adds or changes no session that waits for input. Not applicable.

## Performance budget

Measured basis: none for the new classes. The figures are estimates, to be replaced from the
first green run (docs/test-performance-standards.md):
- **Build:** `tests/Antiphon.Tests` cold build on loaded server2, about 8-11 min. This dominates
  every C# row, hence the 12-15 min estimates.
- **Tests:**
  - W-1..W-3: under 5 s; file parse and one bounded pwsh child for W-2, the same seam as the existing class.
  - W-4/W-5: one isolated schema each, about 2-4 s, sharing the `TestDbFixture` the class already uses.
  - W-6/W-7: one schema; ingest and pipeline read under 3 s.
  - The pwsh harness (W-9..W-11): under 20 s, no build.
- **Timeouts:** per-test timeouts are the class defaults. For the harness: 60 s per case, about 2x
  a measured p95 to set at Code. Shared setup: one schema per test; no new fixture.
- **No row is near 30 min.** If CP-6's `C1514HostLoad*` family runs past 10 min on server2, Code
  splits it to `(C1514HostLoadHistoryTests*)|(C1514HostLoadApiTests*)` and records why. Each split row
  then takes its Min from the CP-6 roster table, and the split rows together still cover all 121 cases.

## Unobserved

- Live state on the server2 host: env file, ingress, and whether `antiphon-server2-server` was ever started.
- Desktop user-secrets entries.
- Whether `RawExeAllowList` binding appends to the defaults (W-3 records it).
- Mid-run lease behaviour when the broker restarts (T-11 avoids it).
- The same-host hairpin for a desktop phone-home runner (phase 1 observes it).
