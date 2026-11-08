# CARD-1778 plan: re-home always-on agents and specialists to the Linux server sidecar (CARD-1744 S4)

Date 2026-11-08. Plan task `c748deca` (Frontier). Base `origin/master`
`c460b0d0a4de591b8e662f8dbc57eeac3f26d249`. Documents only: no product code, nothing deployed,
started or restarted on any host. Parent: [CARD-1744 cutover plan](2026-11-08-card-1744-cutover-plan.md)
(D-5, D-9, slice S4, runbook steps 1.3 and 3.5). Topology B is the accepted target in
[docs/docker-stack.md](../../docker-stack.md#production-deployment-contract-card-1221). Review input:
CARD-1775 round-2 advisory A-9 (trust seed lands in the server home, not the shared directory).

Live readings were taken 2026-11-08 from the server2 phone-home runner (task `c748deca`), read-only:
`GET /api/agents`, `GET /api/projects`, `GET /api/runner-defaults`, `GET /api/session-runners` on the
production origin. No token, secret or key was printed.

## Plain-English summary

- The card says "Code part, only if needed". It is needed, and not where the card guessed. The
  sidecar admits any cwd under `/work` and `Delegation:AllowedRoots` never sees an agent's own
  working directory, so `/work/agents/<name>` is not refused. What refuses every sidecar launch is
  the server itself: before it sends a launch to the direct runner it checks that the agent
  executable exists on the server's own disk (`AgentControlService.cs:527`), and the server image
  has no `claude` or `grok`. On the desktop the server and runner share one disk, so this never
  showed. Slice S1 makes that check topology-aware.
- Second gap: the production compose gives the sidecar no `CLAUDE_CONFIG_DIR`, so Claude in the
  sidecar would keep its login, trust file and memory in the container layer (`/home/app`), lost on
  every recreate. `server-state-init` already creates `/runner-state/claude` for exactly this. Slice
  S2 sets it, and seeds Claude's trust for a working directory where Claude reads it: on the runner,
  by the process that owns the config dir, just before it spawns a Claude session. That resolves A-9
  without giving the server write access to the runner's state volume.
- Third gap: the orchestrator instructions file the server generates lives under the server
  process's data root, which the sidecar cannot read. Slice S2 points it at the shared `work` volume
  so the `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS` path an orchestrator is told about is real on both
  sides. The URL fallback already exists.
- No migration. The eight always-on rows change by `PATCH /api/agents/{id}` in the window, from a
  recorded before table, and rollback restores the before column (D-4). A startup migration cannot
  know which topology it is running in, so it would also rewrite the desktop on rollback.
- The operator does the re-homing (clones, PATCH, start, Claude login on the sidecar, memory
  copy). Nothing in this plan activates, restarts or touches a secret.

## Decisions

| Id | Decision | Why | Rejected |
|---|---|---|---|
| D-1 | New setting `SessionRunner:VerifyExecutablesOnServer` (bool, default `true`). When `false`, `AgentControlService.StartInteractiveSessionAsync` skips `AgentExecutableResolver.Default.EnsureSpawnable(spec.Exe)` for a non-runner-bound launch and the runner's own spawn failure is the refusal. The production compose sets it `false` on `server`. The desktop keeps `true`. | The check is a same-disk optimisation (ConPTY cwd-relative lookup, CARD history in `AgentExecutableResolver.cs:5-15`). In topology B the server image (`Dockerfile`, aspnet base plus git) has no `/usr/local/bin/claude`; the sidecar image does. A rooted `Exe` therefore fails `File.Exists` on the server for every specialist and always-on launch (`AgentExecutableResolver.cs:86-91`). | Hostname heuristic on `SessionRunner:BaseUrl` (loopback = same disk): brittle and undocumented. Runner-side typed refusal (`exe_not_found` over the wire): a contract change across runner and server, more than a 60-120 minute slice. Installing Claude into the server image: doubles the image, puts a TUI binary in the control plane for a check that is not the control plane's. |
| D-2 | Trust is seeded on the runner: `SessionRunnerRuntime.StartCoreAsync` calls a new pure helper `ClaudeLaunchTrustSeed.Apply(request, processEnv)` before `session.StartAsync` (pty-host lane) and before `StartHerdrAsync` (Herdr lane). The helper acts only when `Path.GetFileName(request.Exe)` is `claude`, `claude.exe` or `claude.cmd` (the same file-name test `PhoneHomeLaunchPolicy.ProjectExe` uses), resolves the config path from `request.Env["CLAUDE_CONFIG_DIR"]`, else the runner process's `CLAUDE_CONFIG_DIR`, else `~/.claude.json` (`ClaudeProjectTrust.DefaultConfigPath`), and calls `ClaudeProjectTrust.Seed(request.Cwd, path)`. Never throws, never creates the file, logs one line per outcome. The server-side `StandingSpecialistProvisioner.SeedClaudeTrust` stays. | A-9: the server writes `.claude.json` under its own `CLAUDE_CONFIG_DIR`/home (`ClaudeProjectTrust.cs:35-45`, `StandingSpecialistProvisioner.cs:206-208`); in topology B that is a different container from the Claude that reads it. The runner owns its config dir, so it is the one process that can seed correctly on both topologies. On the desktop the call returns `AlreadyTrusted` (the server seeded the same file) or `Seeded`; both are memoised. `ClaudeStartupReadiness.cs:72` keeps answering the dialog as the fallback. | Server mounts `runner-state` read-write and seeds `/runner-state/claude/.claude.json`: widens the server's write surface onto pty-host manifests and session logs for one JSON key. Shared config dir on `/work`: puts `.credentials.json` under the volume every agent cwd lives in. Setting `CLAUDE_CONFIG_DIR=/runner-state/claude` on the server: the read-only mount makes every provisioning call warn `Failed: atomic replace failed` until the runner has seeded. |
| D-3 | Production compose rows: `server-session-runner` gets `CLAUDE_CONFIG_DIR: /state/claude` (the directory `server-state-init` already creates and chowns, the same value `docker-compose.server2-runner.yml:82` uses); `server` gets `SessionRunner__VerifyExecutablesOnServer: "false"` and `Delegation__OrchestratorInstructions__Path: /work/.antiphon/orchestrator-instructions.md`. `server-state-init` creates `/work/agents` and `/work/.antiphon` (mode 0755, uid 1654). | Login, trust and auto-memory (`<config>/projects/<cwd-key>/memory`) must survive a container recreate, and each `/work/agents/<name>` gets its own memory scope only if the config dir is stable. The instructions file defaults to the server's `LocalApplicationData` (`OrchestratorInstructionsService.cs:464-471`), unreadable from the sidecar; the launch env carries both the path and the URL variable (`OrchestratorInstructionsService.cs:443-444`), and the path must be true on the sidecar. | Leaving the sidecar on `/home/app`: ephemeral; the operator would log in again after every `up -d`. Pointing the instructions path at `runner-state`: the server mounts it read-only. |
| D-4 | No EF migration. Re-homing is a data change made by the operator in the window: `PATCH /api/agents/{id}` with `workingDirectory=/work/agents/<name>`, `sessionBackend=PtyHost`, Herdr labels cleared, from the P-13 before table; rollback restores the before column. | A migration runs wherever the tip starts, including the desktop on R-1/R-3 rollback, and cannot tell topology A from B. `UpdateAgentRequest` already carries every field (`AgentDtos.cs:358-414`), and the before table is already a precondition (CARD-1744 P-13). | Additive `Agent.PreviousWorkingDirectory` column to hold the before value: schema for a one-day record the window log already holds. Startup rewrite of `C:/src/<x>` to `/work/agents/<x>`: silent, topology-blind. |
| D-5 | Always-on agents run in their own clone `/work/agents/<name>` (the card's and CARD-1695's shape), not in the CARD-1776 land mirror `/work/repos/<name>`. Delegation keeps today's default: `delegate.ps1` sends the caller's checkout root as `workingDirectory` (`scripts/delegate.ps1:1369-1370`), which is the parent's own tree and always admitted (`DelegationWorkspaceResolver.cs:70-73`). Worktrees are cut from that clone; land pushes from the task's `RepoPath` (`AgentTaskLandService.cs:216-229`), reachable by the server with the CARD-1776 credential. No `Delegation:AllowedRoots` grant. | The desktop precedent is the markdown-package orchestrator in `C:\src\markdown-package-orchestrator`, a directory that is not its project path. Keeping AllowedRoots empty keeps CARD-0398's boundary and the pinned `DelegationAllowedRootsFileTests`. Project scope comes from the caller's board, not its path (`AgentTaskService.cs:1307-1312`). | `Delegation__AllowedRoots__0: /work/repos` via compose: a tracked silent grant of the land mirrors to every delegating session, and nothing in the default dispatch path needs it. Running the orchestrator inside `/work/repos/<name>`: a Claude session editing the directory the server lands from. |
| D-6 | Fail closed: with the sidecar unreachable the server starts nothing and restarts nothing. `AgentSupervisorService.TickAsync` skips the whole tick when `ListAsync` fails with a transport error (`AgentSupervisorService.cs:86-98`); a launch that reaches the runner and fails charges the per-agent backoff ladder 5 s doubling to the 30-day cap with `BackoffEscalated` incidents (`SupervisionSettings.cs:13-20`, `AgentSupervisorService.cs:852-873`); compose `depends_on: service_healthy` keeps the server from starting before the sidecar answers `/health`. A Check with no live interpreter is refused by the existing `StandingSpecialistSeatPolicy` and `CheckInterpreterUnavailable` incident path. No new timer, no new release deadline. | These paths exist and are tested; the plan only has to prove they are the ones a sidecar outage hits, and must not add a kill. | A "sidecar down" alarm or auto-stop: a stall is a detection state, never an automatic kill (AGENTS.md). |
| D-7 | Rollback to the desktop is CARD-1744 R-1/R-3 plus the agent before column: restore `workingDirectory`, `sessionBackend` and Herdr labels for each agent from the P-13 table, start under the desktop supervisor; sidecar sessions become orphaned seats released with `scripts/runner-slots.ps1 release-orphans`. | Nothing in this plan changes a row the desktop cannot read; a Windows path in `WorkingDirectory` is what the desktop expects. | A dual-path column: more state for the same table. |
| D-8 | CARD-1695 (always-on over phone-home) stays the other path; this plan touches no `PhoneHomeLaunchPolicy` line. The sidecar's catalogue row keeps id `desktop` (`HostStatsAntiphonCounters.cs:41`, `TaskProviderWaitReader.cs:21`); renaming it is a separate card. | Card scope. A rename touches every `desktop` consumer. | Folding CARD-1695 in: it needs a cwd allow-list change on the phone-home dispatcher (`PhoneHomeCommandDispatcher.cs:962`) and a policy change the parent plan excluded. |

## Ground truth

| Card or plan assumes | Code does | Consequence |
|---|---|---|
| "Code part, only if needed: if the sidecar or `Delegation:AllowedRoots` refuses `/work/agents/<name>`" | The direct runner has no cwd allow-list; `PhoneHome__AllowedCwd` is read only by `PhoneHomeCommandDispatcher.cs:179,962,1065` and the park service (`SessionRunnerRuntime.cs:218`). AllowedRoots is read only for delegated task directories (`AgentTaskService.cs:717-724`, `DelegationWorkspaceResolver.cs:70-87`), never for an agent's own cwd. The refusal that does fire is `AgentControlService.cs:527` → `AgentExecutableResolver.EnsureSpawnable` → `File.Exists` on the server. | D-1, S1. The card's premise names the wrong gate; the fix is one setting and one `if`. |
| "Seven always-on agents" | Eight rows have `alwaysOn=true` and are Running: Antiphon-Orchestrator (`C:/src/Antiphon`), ClaudeBot-Antiphon (`C:/src/ClaudeBot`), Gym Stat Orchestrator (`C:/src/gym-stat`), markdown-package Orchestrator (`C:\src\markdown-package-orchestrator`), school-revision (`C:/src/school-revision`), slides Orchestrator (`C:\src\slides`), Torquay Leander (`C:/src/torquay-leander`), antiphon-check-interpreter (`C:\logs\antiphon\check-interpreter`). All `ClaudeCode`, all `PtyHost`, no Herdr labels, seven with remote control on. `antiphon-diagnose` and `antiphon-output-distiller` do not exist on the desktop; the production compose enables both (`docker-compose.server2-server.yml:81-83`), so they are created fresh on the Linux server at first start. | The P-13 table has eight agent rows. Runbook 3.5 lists eight. Remote control stays allowed on the direct runner (`PhoneHomeLaunchPolicy.cs:147` applies to runner-bound only). |
| Specialist trust seed is "pre-existing D-5 shape" to be resolved here (A-9) | `StandingSpecialistProvisioner.cs:206-208` seeds into `ClaudeProjectTrust.DefaultConfigPath()` of the SERVER process. The sidecar's Claude reads its own `CLAUDE_CONFIG_DIR`, which the production compose does not set (only `GROK_HOME`, `:136-144`); `docker-compose.server2-runner.yml:79-83` does set it for the phone-home runner. `ClaudeProjectTrust.Seed` never creates the file (`:155`), so a seed before the first `claude auth login` is `NoConfigFile`. | D-2, D-3, S2. The runner seeds; the operator logs Claude in on the sidecar once (runbook 0.13) so the file exists. |
| "The direct runner shares files with the server" (CARD-1744 D-5) | Shared: `work` rw both sides, `runner-state` rw on the sidecar and ro on the server. Not shared: executables (`Agents__Definitions__*__Exe` name sidecar paths, `:96-106`), the server's data root (instructions file), the sidecar's `/home/app`. | D-1, D-3. |
| Orchestrators read `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS` | The server writes the file under `OrchestratorInstructions.Path` or its `LocalApplicationData` (`OrchestratorInstructionsService.cs:160-161,464-471`) and puts path and URL variables in the launch env (`:443-444`). | D-3: path on `/work/.antiphon`. The URL remains the fallback; the doc paragraph says so. |
| "PATCH working directory, clear Herdr labels, start" | `AgentService.UpdateAsync` applies `WorkingDirectory`, `SessionBackend`, labels (`AgentService.cs:699-702,760-763`); a null label leaves it unchanged and whitespace clears (`NormalizeHerdrLabel`, `:1869-1872`). The start path requires the directory to exist on the server's view of `/work` (`AgentControlService.cs:445-447`) and `POST /api/agents/{id}/ensure-directory` creates only the stored path (`AgentService.cs:206-235`). `AgentWorkspaceProvisioner` leaves a repo's own `CLAUDE.md` alone (`AgentWorkspaceProvisioner.cs:150-153`). | Runbook 3.5 sends `sessionBackend: "PtyHost"` and empty-string labels; clones exist before the PATCH (0.12). |
| Specialists launch on the direct runner (CARD-1744 D-5) | `AgentTaskCheckHostedService.cs:91` provisions at startup; `StandingSpecialistProvisioner.EnsureAsync` starts the row with `automatic: true` and logs a warning when the start fails, leaving supervision to retry (`:106-122`). Supervision skips its tick while the runner is unreachable (`AgentSupervisorService.cs:86-98`). | D-6 as written; no new code. |
| Runner-side spawn of a missing executable "fails the launch" | `UnixPtyArgvGuard.VerifyOrThrow` deliberately leaves absent-executable validation to the launch runtime (`UnixPtyArgvGuard.cs:16`); the pty-host child exits at exec and the session ends. The supervisor records it as a failed start and charges the ladder. | D-1 names this as the refusal when the server check is off. S1's doc line says the symptom (an incident per attempt, not a 409). |
| Capacity | `GET /api/session-runners` shows `desktop` capacity 2 while eight always-on sessions run on it today; `RunnerCapacity` bounds task dispatch, not named starts. The sidecar inherits the same rule. Eleven Claude processes (eight agents, three specialists) will share the sidecar's CPU and RAM with no cap (positive memory caps are unsupported by contract). | Operator post-check 4.x watches `docker stats` for the sidecar in the first hour. No code. |
| Migration ("sorted after master's latest") | Master's latest is `20261107104114_HostLoadRetentionCatchup`. This plan adds none (D-4). | Checkpoint rows run no migration class. |
| Latest tests for the touched classes | `AgentStartRecoveryTests` already witnesses the refusal for an unresolvable exe (`:96-104`) but is Windows-only (cmd.exe). `ClaudeProjectTrustTests` (Pty.Tests) covers `Seed` outcomes. `Server2ProductionServerContractTests` pins the compose; `Specialist_working_directories_are_on_a_mount_both_services_share` (`:411-444`) is the shape S2's pins copy. | S1 adds an OS-neutral class; S2 extends the compose pins in a new class so the CP filter stays narrow. |

## What the server writes, what the sidecar reads

| Path | Volume, mode, owner | Writer | Reader |
|---|---|---|---|
| `/work/specialists/{check-interpreter,diagnose,output-distiller}/.claude/settings.json` (deny-all hook) | `work`, 0755 dirs, uid 1654 (CARD-1775) | server (`StandingSpecialistProvisioner.WriteToolPolicy`) | sidecar Claude (cwd) |
| `/work/specialists/*/CLAUDE.md`, `/work/agents/<name>/CLAUDE.md` | `work` | server (`AgentWorkspaceProvisioner`, only when absent or marked) | sidecar Claude |
| `/work/agents/<name>` (clone) | `work`, uid 1654 | operator, one-shot container of the server image (runbook 0.12) | server (`Directory.Exists`, worktree source, land push), sidecar (cwd) |
| `/work/.antiphon/orchestrator-instructions.md` | `work` | server (`OrchestratorInstructionsService`) | sidecar Claude via `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS` |
| `/runner-state/claude/.claude.json`, `.credentials.json`, `projects/<cwd-key>/` (= sidecar `/state/claude`) | `runner-state`, server mounts it ro | sidecar runner (D-2 seed), sidecar Claude (login, memory, transcripts) | sidecar `TranscriptTailer`; server reads session logs under `/runner-state/session-runner` only |
| `/runner-state/session-runner`, `/runner-state/pty-hosts` | `runner-state` | sidecar runner | server (`AgentSessions:SessionLogPath`, ro) |

## Fail-closed behaviour when the sidecar is unreachable

1. Compose: `server` waits for `server-session-runner` `service_healthy`; `restart: unless-stopped`
   on both. A sidecar that dies later is a transport failure to the server, not a missing service.
2. Supervision: `AgentSupervisorService.TickAsync` probes `ListAsync` first and returns without
   touching any agent while the probe fails with an infrastructure classification. No restart storm,
   no ladder growth from the outage itself. When the sidecar answers again, the next 10-second tick
   (`SupervisionSettings.TickSeconds`) resumes.
3. A launch attempt that reaches the runner and fails (exec failure, refused pipe directory,
   `UnixPtyArgvGuard`) is a failed start: `RestartBackoffFailures` increments, the next attempt is
   scheduled at 5 s · 2ⁿ capped at 30 days, `BackoffEscalated` incidents name the cadence.
4. Checks: a delegate check-in with no live interpreter session is refused through the existing
   `CheckInterpreterUnavailable` incident; Check never admits another agent in its place.
5. Nothing stops a Working session, nothing releases a seat. The park rule stays conditional with
   no deadline while `BlockedTaskParking:Enabled` is false (CARD-1083).

## Rollback to the desktop runner

Follows CARD-1744 section 3.7. The agent part: for each of the eight rows, `PATCH /api/agents/{id}`
with the before column's `workingDirectory` (Windows path), `sessionBackend: "PtyHost"`, labels as
recorded, then the desktop supervisor restarts them once AppHost is back (R-1). Sessions the
sidecar still remembers are orphaned seats: `scripts/runner-slots.ps1 release-orphans` with a
reason. The specialists' rows keep `/work/specialists/*` working directories; on the desktop their
launch refuses `working directory does not exist` and supervision backs off until the operator
PATCHes them back to the recorded desktop paths (the check interpreter) or deletes the two rows
the Linux server created (diagnose, output distiller; recreated on the next Linux start).
`SessionRunner:VerifyExecutablesOnServer` is not set on the desktop, so the default `true` applies.
Nothing at the database is lost; no column changed shape (D-4).

## Migration of existing standing agent rows

None (D-4). The eight rows are rewritten by PATCH in the window from the P-13 before table and
restored from it on rollback. The two specialist rows the Linux server creates are new rows, not
migrations of desktop rows.

## Slices

Each slice is one Grok Code dispatch of 60-120 minutes: test first (committed red), then the
change, then the row. Lane: Unit, Linux, any runner; no `-Runner`, no `-Platform`.

| Slice | Files | Tests (test-first) | Mutants (Review) | Notes |
|---|---|---|---|---|
| S1 (D-1): topology-aware executable check | `server/Application/Settings/SessionRunnerSettings.cs` (new `VerifyExecutablesOnServer`, default true, XML doc naming topology B); `server/Application/Services/AgentControlService.cs:525-527` (inject `IOptions<SessionRunnerSettings>` through the existing ctor's optional parameters; skip `EnsureSpawnable` when false; log one Debug line naming the exe left to the runner); `docs/docker-stack.md` one sentence in the CARD-1778 paragraph | New `tests/Antiphon.Tests/Application/RemoteExecutableLaunchTests.cs` (Unit, `[ParallelLimiter<ProcessSpawnLimit>]` only if it spawns; it does not: the fake adapter never starts a child). Copies `AgentStartRecoveryTests.BuildHarness` (`:166-262`) with a `Raw` definition whose `Exe` is a rooted path that does not exist (`/antiphon-no-such/claude` on Linux, `C:\antiphon-no-such\claude.exe` on Windows). V-1 `Default_true_refuses_a_rooted_exe_missing_on_the_server`: `ConflictException`, message contains the exe, the fake runner saw no start. V-2 `False_hands_the_launch_to_the_runner`: with `VerifyExecutablesOnServer=false` the fake runner's `StartAsync` is called once with `spec.Exe` unchanged. V-3 `Runner_bound_agents_are_unaffected`: a runner-bound agent (phone-home settings with `RunnerId`) never reaches `EnsureSpawnable` whatever the flag (pins today's `else` branch). R-1 `Setting_default_is_true` (one-line settings test, same class). | M1: flag read inverted (V-1 and V-2 both fail). M2: skip applied to the runner-bound branch (V-3 fails). M3: default changed to false (R-1 fails). | Red commit: tests reference the new property, so the first commit fails to compile until the property exists; commit the property with the tests and leave the `if` out so V-2 is an assertion red, not a build red (CARD-0403 rule). |
| S2 (D-2, D-3): runner-side trust seed and compose rows | New `src/Antiphon.SessionRunner/ClaudeLaunchTrustSeed.cs` (static `ConfigPathFor(exe, env, processEnv)` → string?; `Apply(request, processEnv, log)` → `ClaudeProjectTrustResult?`); `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:709-757` one call at the top of the `try` before the lane split; `docker-compose.server2-server.yml` (`server-session-runner`: `CLAUDE_CONFIG_DIR: /state/claude`; `server`: `SessionRunner__VerifyExecutablesOnServer: "false"`, `Delegation__OrchestratorInstructions__Path: /work/.antiphon/orchestrator-instructions.md`; `server-state-init`: add `/work/agents` and `/work/.antiphon` to the `mkdir -p` list and the `chmod 0755` list); `scripts/deploy-server2-server.ps1:133-138` region: add the three new keys to the printed plan; `docs/docker-stack.md`: new `### Standing agents and specialists on the sidecar (CARD-1778)` after the CARD-1776 section (`:926-947`) | New `tests/Antiphon.SessionRunner.Tests/ClaudeLaunchTrustSeedTests.cs` (Unit, pure, temp directory): V-4 `Claude_exe_seeds_the_cwd_key_in_the_request_config_dir` (env `CLAUDE_CONFIG_DIR` → that file gains `projects[key].hasTrustDialogAccepted`); V-5 `Process_env_is_the_fallback_when_the_request_names_none`; V-6 `Non_claude_exe_returns_null_and_writes_nothing` (grok, codex, pwsh); V-7 `Missing_config_file_is_NoConfigFile_and_creates_nothing`; V-8 `Exe_match_is_by_file_name_only` (`/usr/local/bin/claude`, `C:\x\claude.exe`, `claude.cmd`; `claude-helper` does not match). New `tests/Antiphon.Tests/Infrastructure/Server2SidecarStandingAgentsTests.cs` (Unit, text pins using `DockerStackDocuments` helpers): V-9 `Sidecar_claude_config_dir_is_on_runner_state` (sidecar `Env` equals `/state/claude`, `runner-state:/state` mounted rw, init creates `/runner-state/claude`); V-10 `Server_leaves_executable_checks_to_the_sidecar` (`SessionRunner__VerifyExecutablesOnServer` = `false`, staging compose has no such key); V-11 `Orchestrator_instructions_file_is_on_the_shared_work_volume` (path under a rw mount both services declare, init creates `/work/.antiphon`); V-12 `Agents_root_is_created_and_documented` (`/work/agents` in init, doc paragraph names `/work/agents/<name>`, the PATCH fields, the login command shape without a token, the URL fallback, the CARD-1695 pointer); V-13 `Deploy_plan_prints_the_new_keys` (script text contains the three keys). | M4: file-name test accepts any exe (V-6 fails). M5: request env ignored, process env always used (V-4 fails). M6: `Seed` replaced by `IsTrusted` (V-4 and V-5 fail). M7: compose `CLAUDE_CONFIG_DIR` on `server` instead of the sidecar (V-9 fails). M8: init list without `/work/.antiphon` (V-11 fails). | The call-site wiring in `StartCoreAsync` has no spawn-free witness in this lane; Review inspects the diff (one call, before both lanes, exceptions impossible by `Seed`'s contract) and the SourceLanding Mutation names it PC-1 under `RunnerChildClaudeTokenInheritanceTests` (Integration, spawns `sh`). Red commit: the two new test classes before the helper and compose rows exist; V-4..V-8 fail to compile until the helper type exists, so commit the empty helper returning null with the tests (V-4, V-5, V-9..V-13 assertion red). |
| S3 (operator runbook, docs only) | `docs/superpowers/plans/2026-11-08-card-1778-rehome-agents-to-sidecar.md` section "Operator steps" is the runbook; `docs/docker-stack.md` paragraph (S2) links it; no other file | None (docs). `DockerStackDocumentationTests` unaffected (no anchor it pins changes). | n/a | Lands with S2. |

Order: S1, then S2 (S2's V-10 pins the key S1 introduces). S3 is inside S2's commit.

## Operator steps (runbook additions to CARD-1744)

Operator-only. No step here activates a build, restarts a host, or shows a secret. Steps are
numbered to slot into the parent runbook's phases.

| Step | Where | Command | Expected | STOP |
|---|---|---|---|---|
| P-13 (amend) | Desktop | Record for each of the eight `alwaysOn` agents: `id`, `name`, `workingDirectory`, `sessionBackend`, `herdrWorkspaceLabel`, `herdrTabLabel`, `remoteControlEnabled` from `GET /api/agents`; record the origin URL of each desktop checkout (`git -C <wd> remote get-url origin`) | Eight rows plus eight remotes in the window log | A checkout with no `origin` (school-revision, torquay-leander, ClaudeBot have empty `gitRepositoryUrl` on the project): record the remote from the checkout or mark the agent "no clone, directory only" |
| 0.12 | Host | For each agent with a remote: `pwsh -NoProfile -File scripts/server2-repository-mirrors.ps1 -Execute -TableFile <agents table> -ReposRoot /work/agents` from a one-shot container of the server image (as 0.6 does for `/work/repos`); for a "directory only" agent, `mkdir -p /work/agents/<name>` in the same container. `chown 1654:1654 -R /work/agents` | `ls /work/agents` lists eight directories; each clone's `git status` clean | Credential refused: CARD-1776 custody; do not continue |
| 0.13 | Host | Claude login on the sidecar, after the first `up -d` of S1-S2 and before 3.5: `docker exec -it -u 1654:1654 -e HOME=/home/app -e CLAUDE_CONFIG_DIR=/state/claude antiphon-server2-server-server-session-runner-1 claude auth login` (same shape as the runner row in `docs/agent-credentials.md`) | `/state/claude/.credentials.json` exists in the sidecar, owner 1654; `.claude.json` exists | Login fails: specialists cannot launch; do not continue to 3.5 |
| 0.14 (optional) | Desktop → Host | Copy each agent's Claude auto-memory: desktop `%USERPROFILE%\.claude\projects\<old-key>\memory\` to the sidecar's `/state/claude/projects/<new-key>/memory/` where `<new-key>` is `/work/agents/<name>` with `/` replaced by `-` (`-work-agents-<name>`). Plain Markdown only; never copy `.credentials.json` or `.claude.json` | Memory index present under the new key | n/a |
| 1.3 | Desktop | As CARD-1744: stop each always-on session; record session ids | `GET /api/agents` shows none Running | A session that will not stop: leave it and note it |
| 2.4 (amend) | Host | `curl -s http://127.0.0.1:17400/api/session-runners` | Row `desktop` (the sidecar) `platform: linux`, available | Sidecar unavailable: R-2 |
| 2.7 | Host | `docker logs antiphon-server2-server-server-1 --since 5m` | Three "Provisioned the ... in /work/specialists/..." lines (or "Reconciling"), three sessions Running within 2 minutes; no `Agent executable ... does not exist` line | That line present: S1 is not on the tip, or `SessionRunner__VerifyExecutablesOnServer` missing from the env; R-2 |
| 3.5 | Desktop (vhost) | For each of the eight rows: `PATCH /api/agents/{id}` with the full `UpdateAgentRequest` body (name, details, assignment policy and board unchanged), `workingDirectory: "/work/agents/<name>"`, `sessionBackend: "PtyHost"`, `herdrWorkspaceLabel: ""`, `herdrTabLabel: ""`; then `POST /api/agents/{id}/start` with `{"fresh": true}` | `GET /api/agents` shows each Running with the Linux path; `GET /api/agents/{id}/incidents` has no `TrustDialogNotCleared` | Launch refused `working directory does not exist`: 0.12 missed that name; refused `Agent executable ... does not exist`: see 2.7; trust dialog incident: 0.13 not done |
| 3.6 | Desktop (vhost) | `scripts/keep-six.ps1 -ClearHold` per orchestrator once Running | Holds cleared | n/a |
| 4.x | Host | After 30 minutes: `docker stats --no-stream antiphon-server2-server-server-session-runner-1`; `docker exec antiphon-server2-server-server-session-runner-1 ls /state/claude/projects` | Eleven project keys; memory under `-work-agents-*`; RSS recorded in the window log | RSS above the host's free margin: stop the two non-orchestrator agents first (school-revision, Torquay Leander) and file a card |

Rollback for every step is section "Rollback to the desktop runner".

## Performance budget

Selection: three exact class filters below plus the unchanged `Server2ProductionServerContractTests`
in CP-3 (it pins the compose this plan edits). One isolated cold build per committed slice group:
`Antiphon.Tests` (CP-1, CP-3) and `Antiphon.SessionRunner.Tests` (CP-2). Native default output,
`UseAppHost=false` added by the tool off Windows. No namespace or assembly run.

- Measurements (provisional, this runner's history, not a fresh p95): isolated cold `Antiphon.Tests`
  build 2-6 minutes under a build slot (CARD-1775 round 2 measured 354 s at load 9-21);
  `Antiphon.SessionRunner.Tests` cold build 1-3 minutes; `Server2ProductionServerContractTests`
  138 tests in 17-45 s wall including the 24 s `Execute_refusals_stop_before_docker`; a text-pin
  class under 10 s; the new harness class (`RemoteExecutableLaunchTests`) uses the shared
  `TestDbFixture` Postgres and spawns nothing, so 3 tests under 60 s. Row timeouts stay at the
  importer's 15-minute floor, above 2x every figure here; no timeout is widened. Total under
  25 minutes; nothing approaches the 30-minute Debug obligation.
- Phases reported separately by the checkpoint tool: slot wait, build, test body. CP-1 shares the
  assembly-level Postgres fixture once per run (R3); CP-2 and CP-3 use no database, no container,
  no host start, no migration.
- Recording: the tool's `report.json` and TRX under `.antiphon/checkpoints/<run>/`, per-test
  durations in the TRX, phase timings in the report. Slot wait is a separate field.
- Operations budget (not tests): 0.12 clones 5-10 minutes for eight repositories; 0.13 login
  2 minutes; 3.5 eight PATCH and start calls 10 minutes including supervision confirm; adds about
  20 minutes inside CARD-1744's 45-60 minute window estimate. Rollback adds 5 minutes of PATCH.

## Verification design

Lane: Unit, Linux, any runner. Rows run once per committed slice group through the checkpoint tool
(`dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this file>`), red-then-green: each
new class is committed before the production change and must fail with assertion failures, not
build errors (see the per-slice red-commit notes).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1778-s1/ | remote-exe | /*/Antiphon.Tests.Application/RemoteExecutableLaunchTests/* | V-1, V-2, V-3, R-1 | all listed, 0 failed | 4 | 8 |
| CP-2 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1778-s2r/ | trust-seed | /*/Antiphon.SessionRunner.Tests/ClaudeLaunchTrustSeedTests/* | V-4, V-5, V-6, V-7, V-8 | all listed, 0 failed | 5 | 5 |
| CP-3 | S2 | tests/Antiphon.Tests -> bin-c1778-s2/ | compose-sidecar | /*/Antiphon.Tests.Infrastructure/(Server2SidecarStandingAgentsTests*)\|(Server2ProductionServerContractTests*)/* | V-9, V-10, V-11, V-12, V-13 | all listed, 0 failed | 17 | 8 |

Suffix check: `Server2SidecarStandingAgentsTests*` and `Server2ProductionServerContractTests*` share
the prefix `Server2` only; neither is a prefix of the other, and no other `Server2*` class exists
in `Antiphon.Tests.Infrastructure` (`Server2StagingServerContractTests` is not selected because
the operands are full class names with a trailing `*`). `RemoteExecutableLaunchTests` and
`ClaudeLaunchTrustSeedTests` are new names with no sibling. Executed counts: CP-1 4, CP-2 5
(V-8 may use `[Arguments]` and count per case; keep the floor at 5), CP-3 5 + 12 = 17.

### Positive controls (SourceLanding Mutation, after land)

PC-1 `SessionRunnerRuntime.StartCoreAsync` call site: remove the `ClaudeLaunchTrustSeed.Apply`
call; `RunnerChildClaudeTokenInheritanceTests` (Integration, `[ParallelLimiter<ProcessSpawnLimit>]`)
gains one method in S2 that points `CLAUDE_CONFIG_DIR` at a temp dir holding `{"projects":{}}`,
launches `sh -c true` with `Exe` renamed through a `claude` symlink in a temp dir, and asserts the
cwd key appears. Method-scoped filter `/*/*/RunnerChildClaudeTokenInheritanceTests/Claude_launch_seeds_trust_in_the_runner_config_dir`.
PC-2 M1-M3 and PC-3 M4-M8 as the slice table names, each against its exact test method.

### Sessions that wait for input

This plan adds no session that waits for input. The re-homed always-on agents keep today's
semantics: a Working or Blocked seat is released by its owner or by the operator, and with
`BlockedTaskParking:Enabled` false there is no automatic release deadline (CARD-1083). The sidecar
being unreachable releases nothing (D-6).

## Not done, unverified

- No container was started: the `desktop` catalogue row for the sidecar, the 11-process RSS and
  the first-launch trust path are predicted from code, not observed. 2.4, 2.7 and 4.x record them.
- `AgentSupervisorService` behaviour under a sidecar outage is read from `TickAsync`, not exercised
  with a dead runner here; CP rows do not add a test for it (existing `HerdrSupervisionBackoffTests`
  and `CapacityRecoverySupervisionTests` cover the ladder).
- Whether `claude auth login` in 0.13 needs a browser round-trip from the host is the same question
  the phone-home runner's row answers in `docs/agent-credentials.md`; not re-verified.
- The land push from a worktree cut from `/work/agents/<name>` was traced to `task.RepoPath`
  (`AgentTaskLandService.cs:216-229`, `LandingGit`), not run. CARD-1744 post-check "One land"
  proves it on the day; if it proves otherwise, the fallback is a `-Dir /work/repos/<name>`
  dispatch plus a `Delegation__AllowedRoots__0: /work/repos` row, which D-5 rejected as a default.
- The orchestrator instructions file path change (D-3) was not exercised against
  `OrchestratorInstructionsPaths`; S2's V-11 pins the compose, and a startup log line names the
  path the service wrote.

--- next stage ---
next: code
handoff: Code S1 of docs/superpowers/plans/2026-11-08-card-1778-rehome-agents-to-sidecar.md: add SessionRunner:VerifyExecutablesOnServer (default true), skip EnsureSpawnable at AgentControlService.cs:527 when false for non-runner-bound launches, test-first RemoteExecutableLaunchTests, run CP-1; then S2 (runner trust seed, compose rows, Server2SidecarStandingAgentsTests, CP-2, CP-3).
artifact: docs/superpowers/plans/2026-11-08-card-1778-rehome-agents-to-sidecar.md
