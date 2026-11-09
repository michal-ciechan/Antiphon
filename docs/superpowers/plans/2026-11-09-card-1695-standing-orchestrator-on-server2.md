# CARD-1695 Standing orchestrator on the phone-home runner

A standing (AlwaysOn) orchestrator can live on the phone-home runner, in its own clone at `/<RunnerWorkspace>/agents/<name>`. The first user is `slides-server2` for the slides board, replacing the desktop slides orchestrator only after the operator stops that desktop session. This plan admits that checkout, lets AlwaysOn start there, and restarts a dead process from the phone-home runner's own liveness. It does not deploy, does not run on the desktop, and does not change product behavior for `/work` or `/work/worktrees/*`.

Re-checked at `e983d6fe263af623a3da186a2260575f60cc4c5f`. The 2026-11-03 investigation's cites still match: named AlwaysOn is `PhoneHomeLaunchPolicy.cs:198-199`, remote control is `:146-147`, and named cwd projection is `:262`. Dispatcher admission is `PhoneHomeCommandDispatcher.cs:966-973` (`IsAdmittedCwd` at `1074-1075`). Line 930 is `ImageCodexPath`. `AgentControlService` writes `RunnerCwd` at `789-790`. Line 782 is `EffectiveModelId`.

`GET /api/runner-defaults` and `GET /api/session-runners` were unreachable from this worktree (connection refused on `127.0.0.1:17202`). This plan embeds no fleet host. Checkpoints omit `-Runner` and `-Platform`. The agent row's `runnerId` is the placement. The string `server2` below is the configured runner id already used in code and docs.

## Decisions

- D-1. Admit exactly one path segment under `<AllowedCwd>/agents/`. The production root is `/work`, so the checkout is `/work/agents/<name>`. The check is lexical and uses the configured root. It does not resolve `..`. Segment rules match worktrees: no slash, not `.` or `..`, and on the Windows flag no colon and no trailing dot or space. `IsAdmitted` becomes exact root, then the existing worktree segment, then this agents segment. A separate `IsStandingCheckout` is the agents segment only.
  Rejected: `/work` itself, because every `/work` session shares Claude memory under `/state/claude` keyed by cwd, and a `CLAUDE.md` at `/work` is loaded by checkouts underneath it. Rejected: `/work/<name>`, because CARD-1778, `docs/docker-stack.md` (standing section, around line 994), and `Server2SidecarStandingAgentsTests.Agents_root_is_created_and_documented` already pin `/work/agents/<name>`. Rejected: `/work/repos/<name>`, because that tree is the mirror inventory (`RunnerWorkspaceService.ResolveRepository`, `RunnerRepositories` in `RunnerWorkspaceService.Retention.cs:10-23`) and the runner mutation lock serializes it. Rejected: `/work/worktrees/<name>`, because those leaves are task mirrors and retention targets. Rejected: nesting deeper than one segment, and resolving `..`.
- D-2. AlwaysOn is allowed only for a runner-bound named, non-pool, non-pinned PtyHost agent whose `WorkingDirectory` is a standing checkout. Kinds stay the named kinds already admitted (Grok, Claude Code, Raw). The orchestrator product is Claude Code only (`docs/agent-kinds.md` lines 86-92). Pinned, task, pool, and named-without-a-standing-checkout stay `phone_home_pool_refused`, including a named agent whose directory is `/work`. Create and PATCH refuse that shape before save with the same code, so a bad row never enters the restart storm. Rows already saved stay refused at start and still charge the backoff. No backfill.
  Rejected: allowing AlwaysOn for every named runner agent. Projection would still start it at `/work`. Rejected: a Claude-only exception. The refusal today covers every named kind, and a Grok row with AlwaysOn would keep climbing the backoff.
- D-3. For that standing agent, reachability is `IPhoneHomeRunnerLiveness.Probe(runnerId)`, not the desktop `ListAsync`. The probe is `(Known, DispatchEligible)`. The implementation reads `PhoneHomeRunnerDirectory.DeclaredCapacity` (`516-524`): an int means dispatch-eligible; null (no live slot, socket closed, lease expired, or unknown id) means skip that agent and do not climb the backoff. `ConflictException` codes `RunnerDraining` and `RunnerRetired` (`AgentControlService.cs:752-756`) are the same skip. A desktop `ListAsync` infrastructure failure (`AgentSupervisorService.cs:91-98`) skips only agents that are not standing-on-phone-home. Restart still goes through `StartAsync`. A live session is not restarted. The supervisor does not kill a Working session or a session waiting for input.
  Rejected: coupling `AgentSupervisorService` to the concrete `PhoneHomeRunnerDirectory`. Rejected: treating a phone-home disconnect as `Exited`. Disconnect drops the live slot. It does not by itself mark the database session exited.
- D-4. The clone is the agent's own `.git` at the standing path, created by the operator inside the runner as uid 1654. Product code does not mkdir it on the desktop server and does not take the desktop `RepositoryMutationLease` or the runner mirror lock. `StartInteractiveSessionAsync` (`AgentControlService.cs:448-450`) skips `Path.GetFullPath` and `Directory.Exists` for a standing checkout. `Project` (`PhoneHomeLaunchPolicy.cs:262`) keeps that path when `runnerCwd` is blank. `RunnerCwd` (`AgentControlService.cs:789-790`) stores it. `AgentSessionService.cs:2440` already launches with `session.RunnerCwd ?? cwd`. `createWorkingDirectory: true` on a standing path is `422 phone_home_cwd_host_refused` and creates nothing.
  Rejected: a server-side `Directory.Exists` of a POSIX path. On the Windows desktop host that call fails closed or, worse, maps onto a local directory. Rejected: a linked worktree of `/work/repos/antiphon`.
- D-5. Herdr and remote control stay refused. Projection keeps `Herdr = null` (`PhoneHomeLaunchPolicy.cs:271`) and `MemoryLimitMb = 0` (`:269`). The dispatcher still refuses Herdr (`PhoneHomeCommandDispatcher.cs:974-978`). Remote control stays `phone_home_remote_control_refused` (`PhoneHomeLaunchPolicy.cs:146-147`, `AgentService.RefuseRunnerClaudeRemoteControl` at `1982-1986`). CARD-0628 D-9 recorded that the outbound WebSocket from the runner was unmeasured. The operator channel is the existing session transcript and screen routes (`docs/ops-http.md`) and `POST /api/sessions/{id}/messages`. `delegate.ps1` is invoked as `pwsh -NoProfile -File /work/repos/antiphon/scripts/delegate.ps1` with the session cwd left on the standing clone. The script reads `ANTIPHON_API` at `scripts/delegate.ps1:414`. `git rev-parse` runs only when `-Directory` is passed (`:668`). Do not copy scripts into the slides clone. `pwsh` is already on the image allow-list.
  Rejected: implementing remote control in this card. Rejected: copying `delegate.ps1` into the slides clone.
- D-6. No migration. The latest migration on this tree is `20261108121426_Card1773TranscriptPromptStampIndex`. `RunnerCwd` already exists. No DTO change.
- D-7. Operator steps and rollback are documentation. Product code does not stop the desktop slides orchestrator (session `b788e4ec`). Killing an AlwaysOn agent stays human-only (`docs/orchestration-loop.md` line 84). A rollout drain can stop sessions the way it already does. This card does not widen autonomy. Rollback is `PATCH` `alwaysOn` false and stop the agent, then revert the admission commit. Deleting the clone is operator-only. Retention does not retire `/work/agents/<name>`. Do not delete the work volume.

The lexical rules live once, in a new public type `src/Antiphon.SessionRunner.Contracts/RunnerWorkspaceAdmission.cs` (no IO). `RunnerWorkspaceCwd` forwards to it. The server already references `Antiphon.SessionRunner.Contracts` and does not gain a reference to `Antiphon.SessionRunner`. `IPhoneHomeRunnerLiveness` lives in `server/Application/Interfaces`. Its implementation wraps `DeclaredCapacity` and is registered beside `server/Program.cs:371-380`. The supervisor constructor gains an optional `IPhoneHomeRunnerLiveness?` with a default of null, after the existing optional parameters (`AgentSupervisorService.cs:49-63`). Null means no phone-home directory: standing runner agents are skipped as unknowable, and desktop AlwaysOn behavior is unchanged. `ActivatorUtilities` hosts that omit the service must still construct. If a test host throws on the new parameter, register a null implementation in that host rather than editing `StandingRestartEscalationTests`.

## Ground truth

| What the card relies on | What the code does at this SHA |
|---|---|
| A named runner agent cannot be AlwaysOn | `RefuseUnsupportedStart` throws `phone_home_pool_refused` at `PhoneHomeLaunchPolicy.cs:198-199`. Pinned AlwaysOn throws at `159-160`. Task AlwaysOn throws at `185-186`. `CreateAsync` stores the flag at `AgentService.cs:579` and does not call `RefuseUnsupportedStart`. |
| Runner Claude remote control is refused | `PhoneHomeLaunchPolicy.cs:146-147` and `AgentService.cs:1982-1986`, code `phone_home_remote_control_refused`. `DockerStackDocumentationTests` line 385 requires that literal in `docs/agent-kinds.md`. |
| A named agent starts in `/work` | `Project` line 262 uses `entry.RunnerWorkspace` when `runnerCwd` is blank. The session stores `RunnerWorkspaceFor` at `AgentControlService.cs:789-790`. |
| The runner admits `/work` or one segment under `/work/worktrees/` | `RunnerWorkspaceCwd.IsAdmitted` (`RunnerWorkspaceCwd.cs:6-30`) returns false unless the path is the exact root or one worktree segment. `/work/agents/<name>` is two segments, so it is refused. The dispatcher message is `PhoneHomeCommandDispatcher.cs:972`. |
| The server demands a directory that exists on the server host | `AgentControlService.cs:448-450` runs `Path.GetFullPath` and `Directory.Exists` before projection. A POSIX standing path fails that check on the Windows host. `AgentService.CreateWorkingDirectory` (`472-473`) mkdirs through `IDirectoryWriter` on the server. |
| Supervision waits on the desktop runner | `TickAsync` returns 0 for the whole tick when `ListAsync` is `RestartFailureKind.Infrastructure` (`AgentSupervisorService.cs:91-98`). Later liveness is the database row (`FindPersistentSessionAsync`, `998`). |
| A phone-home drop is not a dead session | `DeclaredCapacity` (`PhoneHomeRunnerDirectory.cs:516-524`) returns null when the live slot is missing, not dispatch-eligible, or lease-expired. The directory does not mark the database session `Exited`. |
| CARD-1778 already names the checkout | `docs/docker-stack.md` around line 994 says always-on agents run in `/work/agents/<name>` and that CARD-1695 remains the phone-home path this compose does not change. `Server2SidecarStandingAgentsTests.cs:73-95` pins that section, including the literal `CARD-1695`, and forbids `ANTHROPIC_API_KEY` and `CLAUDE_CODE_OAUTH_TOKEN` inside it. |
| Herdr is refused on this runner | `Project` sets `Herdr = null` (`:271`). The dispatcher refuses a non-PtyHost backend and any Herdr payload (`974-978`). |
| Credential names are refused | Claude names at `PhoneHomeLaunchPolicy.cs:226-232` and Codex names at `236-243`, code `phone_home_env_refused`. |
| No schema change is required | Latest migration: `20261108121426_Card1773TranscriptPromptStampIndex`. |

## Intended behaviour

Before. `POST /api/agents` with `runnerId`, `alwaysOn: true`, and `workingDirectory: /work/agents/slides-server2` saves the row. The supervisor tick asks the desktop runner. If that probe is infrastructure, the tick returns 0 and the phone-home agent is not considered. If the desktop runner answers, `StartAsync` throws `phone_home_pool_refused` and `RecordStartFailureInFreshScopeAsync` (`AgentSupervisorService.cs:389-392`) climbs the backoff, up to the existing 30-day cap. A start that got past that refusal would still `GetFullPath` the POSIX directory on the server host, project cwd to `/work`, and be refused by the runner if the cwd were `/work/agents/<name>`.

After. Create and PATCH of a runner-bound AlwaysOn agent whose directory is not a standing checkout fail with `409 phone_home_pool_refused` before save. `createWorkingDirectory: true` on a standing path fails with `422 phone_home_cwd_host_refused` and creates no directory. A valid standing row is PtyHost, named, non-pool, non-pinned, remote control off, Herdr labels empty. On each tick:

1. The desktop `ListAsync` probe still runs. An infrastructure failure no longer returns 0 for the whole tick. It marks desktop agents unknowable.
2. A standing runner agent calls `Probe`. Null capacity, `RunnerDraining`, or `RunnerRetired` skips that agent and leaves `RestartBackoffFailures` unchanged. A dispatch-eligible probe uses the session row.
3. A live session, a session the launch queue owns, a compaction block, a user suspension, a Herdr hold, or a continuity hold does not restart and does not kill.
4. A dead session starts through `StartAsync`. The standing path skips the host existence check. `RunnerCwd` and the projected cwd are the standing path. Herdr is null. Memory limit is 0. Credential env names still throw `phone_home_env_refused`.
5. The runner admits the cwd. `ClaudeLaunchTrustSeed.Apply` (`ClaudeLaunchTrustSeed.cs:37-49`) seeds trust for that cwd when `.claude.json` already exists. It does not create the config file.
6. A later process exit that the runner observes becomes `Exited`, and the next eligible tick starts again. A socket drop alone stays a skip, not a failure.

Files to change:

- `src/Antiphon.SessionRunner.Contracts/RunnerWorkspaceAdmission.cs` (new). `IsAdmitted` and `IsStandingCheckout`.
- `src/Antiphon.SessionRunner/RunnerWorkspaceCwd.cs:6-30`. Forward, and stop returning false before the agents segment is tested.
- `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:972`. Extend the message so it still begins with `cwd must be` and also names `<AllowedCwd>/agents/<segment>`. `IsAdmittedCwd` (`1074-1075`) stays the call.
- `server/Application/Services/PhoneHomeLaunchPolicy.cs:190-199` and `:262`. Standing checkout skips the named AlwaysOn throw. `Project` uses that directory when `runnerCwd` is blank. Pinned (`149-164`) and task (`168-187`) branches stay as they are. Credential loops, `Herdr = null`, and `MemoryLimitMb = 0` stay as they are.
- `server/Application/Services/AgentControlService.cs:441-450` and `:789-790`. Skip host canonicalization for a standing checkout. Store that path as `RunnerCwd`. The drain catch at `752-756` stays; the supervisor learns to treat those codes as a no-charge skip.
- `server/Application/Services/AgentService.cs:472-473`, `:579`, and `:712-713`. Refuse AlwaysOn before save when the runner directory is not standing. Refuse `createWorkingDirectory` on a standing path.
- `server/Application/Services/AgentSupervisorService.cs:82-116` and `:384-392`. Per-agent phone-home probe. Desktop infrastructure skips non-standing agents only. Draining and retired codes return without `RecordStartFailureInFreshScopeAsync`.
- `server/Application/Interfaces/IPhoneHomeRunnerLiveness.cs` (new) and `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerLiveness.cs` (new). Register beside `server/Program.cs:379`.
- Docs listed in S4. No new doc file.

## What stays unchanged

`/work` and `/work/worktrees/<one segment>` stay admitted. Pool delegate paths, card start (`phone_home_card_refused`), OnAgent, custom wrappers, and SourceLanding for named agents stay refused. The pinned CARD-0490 agent stays non-AlwaysOn and still uses `EnsureExactHostRoot` (`PhoneHomeLaunchPolicy.cs:202-208`). Credential env refusals stay. Desktop AlwaysOn supervision stays on `ListAsync`. The desktop Herdr supervision hold stays. `MemoryLimitMb = 0` stays. `RunnerWorkspaceCwdWindowsTests` stays. The Windows lane owes nothing. `PhoneHomeCommandDispatcherTests` line 1237 asserts that some other refusal does not contain `cwd must be`; extending line 972 is safe while that prefix stays on the cwd refusal only.

The enforced write bound is the launch cwd. This card does not add a filesystem jail. The child uid is the uid that owns task worktrees, and a jail denying `/work/worktrees` would break delegate dispatch. The process can still open an absolute path. "No writes outside `/work/<project>`" is the operator rule plus this cwd gate.

Git credentials stay out of the clone. Refused env names stay refused. The image credential helper and `/run/antiphon` mounts stay outside the checkout. `GIT_CONFIG_GLOBAL` remains `/run/antiphon/gitconfig`. The operator clone does not set `core.sshCommand`, `credential.helper`, or `user.email`. `git config --show-origin --get user.email` resolves to `/run/antiphon/gitconfig`. Shared `CLAUDE_CONFIG_DIR=/state/claude` stays one login. Memory is per project path, which is why the standing cwd has to be its own directory. `CLAUDE.md` lives only at `/work/agents/<name>/CLAUDE.md`, never at `/work` or `/work/agents`.

Secondary slides mirrors, when the origin is under `PhoneHome:AllowedCloneSources` (default `https://github.com/michal-ciechan/`), still lazy-clone to `/work/repos/<derived-name>`. That mirror is not the standing cwd. Lands stay on the desktop project path.

## Safety rules

- No credential file in the checkout. No `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR`, or `CCR_OAUTH_TOKEN_FILE` in the projected env. Codex credential names stay refused. A shared secret must not enter the child env.
- Launch cwd is the standing checkout. Product code does not write outside it, and it does not create the directory.
- AlwaysOn restart refusals that stay: pinned, pool, delegated task, named without a standing checkout, card start, OnAgent, custom wrapper, non-PtyHost, SourceLanding, worktree workspace for the named agent, user suspension, Herdr hold, continuity hold, compaction block, launch-queue ownership, desktop-unknowable for a non-standing agent, and phone-home unknowable, draining, or retired. Those last three do not climb the backoff. The others keep today's charge or today's hold. Do not kill a Working session or a session that is waiting for input.

## Sessions that wait for input

The standing orchestrator session waits for input. `BlockedTaskParking:Enabled` defaults to false (CARD-1083). There is no automatic release deadline. Supervision restarts a dead process. It does not release a live session that is waiting for input. The seat stays held.

## Platform

The runner-defaults and session-runners routes were unreachable from this worktree. Checkpoints name the Linux-runner lane by project and filter only. They pass no `-Runner` and no `-Platform`. Placement of `slides-server2` is the agent row's `runnerId` after the operator creates it. Code does not embed a host, an IP, or a fleet location.

## Existing tests affected

`tests/impacted-classes.json` (CARD-1782, CARD-1801) matches none of the files above. Impacted classes: none. Do not edit `server/Application/Services/OrchestratorWorkspaceLayout.cs` (glob `server-git-start-sites`, class `ServerGitProcessTests`). Do not edit `server/**/*Dispatch*.cs` (glob `statement-budgets`). `PhoneHomeCommandDispatcher.cs` is under `src/`, so that glob does not select it. Do not add `[NativeLinuxOnly(]`. Do not edit `server/Migrations/**`.

Tests that change color when admission widens, and where they are covered:

- `PhoneHomeCommandDispatcherTests.Cwd_outside_workspace_is_refused` (`211-235`) admits only `/work` and `/work/worktrees/task-deadbeef`. The class has 45 tests. The checkpoint filter does not include it. `StandingRunnerCwdTests` carries the old pairs and the new agents cases. Line 1237 stays green if the cwd message still contains `cwd must be` and that other refusal does not.
- `PhoneHomeStandingLaunchTests` is `[Category("Integration")]` and has 15 `[Test]` methods. Projection tests that expect cwd `/work` (`173-196`, `Runner_bound_named_claude` at `721-733`) stay green because `Project` changes only for a standing checkout. Remote-control tests (`735-777`) and the Herdr refusal (`796-797`) stay green. The new start method is added here. CP-2 runs the whole class.
- `StandingRestartEscalationTests` is Integration and Slow. Add no method there. The new restart class is Integration and not Slow.
- `Server2SidecarStandingAgentsTests` (5 tests) and `DockerStackDocumentationTests.Agent_kinds_doc_names_the_raw_allow_list` must stay green. CP-4 runs both classes. The agent-kinds edit keeps these literals: `runner-bound`, `/usr/local/bin/pwsh`, `Grok and Claude Code are the agents the image carries`, `Codex is not installed there and stays refused`, `are Grok or Claude Code (CARD-0628)`, `phone_home_env_refused`, `phone_home_remote_control_refused`. It does not reintroduce `Grok is the only agent the image carries`. The docker-stack standing section keeps every literal asserted at `Server2SidecarStandingAgentsTests.cs:73-95`.

## Verification design

Verification is folded into this plan so Code can execute it. At this base SHA the current code is the red mutant: `/work/agents/slides-server2` is not admitted, and a named AlwaysOn start throws `phone_home_pool_refused`. Each witness is written first and seen red for that reason, then the slice turns it green. After the fix, Code puts the listed mutant back, sees that witness fail, and restores it before the commit. This plan does not schedule a SourceLanding mutation pass.

- V-1. `Agents_checkout_is_admitted`. `/work/agents/slides-server2` is admitted for `windows` false and true. The same path with a backslash is admitted only when `windows` is true. Case differs only when `windows` is true. `IsStandingCheckout` is true.
- V-2. `Agents_root_nested_and_traversal_are_refused`. Refused, and not standing: `/work/agents`, `/work/agents/`, `/work/agents/slides-server2/extra`, `/work/agents/..`, `/work/agents/../worktrees`, `/work/repos/antiphon`, `/workspace/agents/x` (prefix lookalike), empty, `.` and `..` segments. Windows flag also refuses a trailing dot, a trailing space, and a colon in the segment. The implementation does not resolve `..`.
- V-3. `Worktree_and_workspace_root_stay_admitted`. `/work` and `/work/worktrees/task-deadbeef` are admitted. Neither is a standing checkout. A named agent's worktree directory therefore cannot take the AlwaysOn exception.
- V-4. `Dispatcher_message_names_the_agents_checkout`. The cwd refusal still contains `cwd must be` and names the agents segment. A Herdr refusal does not contain `cwd must be`.
- V-5. `Named_standing_checkout_may_be_AlwaysOn`. A runner-bound named non-pool non-pinned PtyHost agent with `WorkingDirectory` `/work/agents/slides-server2` passes `RefuseUnsupportedStart` for Claude Code, and for Grok and Raw.
- V-6. `AlwaysOn_without_a_standing_checkout_stays_refused` and `Pinned_and_task_AlwaysOn_stay_refused`. Named AlwaysOn at `/work`, at a Windows path, and at `/work/worktrees/task-x` throws `phone_home_pool_refused`. Pinned and task AlwaysOn throw the same code. Create and PATCH of a non-standing runner AlwaysOn throw before save. `createWorkingDirectory: true` on a standing path throws `phone_home_cwd_host_refused` and does not call `IDirectoryWriter`.
- V-7. `Projection_keeps_the_standing_checkout_and_the_host_root_maps_to_work`. A standing `WorkingDirectory` projects to that path, with `Herdr` null and `MemoryLimitMb` 0. A non-standing named agent still projects to `/work`. A non-blank task `runnerCwd` still wins. Pinned `EnsureExactHostRoot` is unchanged.
- V-8. `Remote_control_and_Herdr_stay_refused`. Runner Claude remote control still throws `phone_home_remote_control_refused`. A Herdr payload and a non-PtyHost backend still throw. Adding `ANTHROPIC_API_KEY` still throws `phone_home_env_refused`.
- V-9. `Standing_checkout_starts_without_a_host_directory` on `PhoneHomeStandingLaunchTests`. Seed a named non-pinned AlwaysOn agent whose directory is `/work/agents/c1695-absent-<guid>` and does not exist on the host. Start does not create it. The session `RunnerCwd` and the launch cwd are that POSIX path. `Path.GetFullPath` is not applied to it.
- V-10. `Desktop_outage_still_restarts_an_eligible_standing_agent`. `ListAsync` throws an exception `RestartFailurePolicy.Classify` already maps to `RestartFailureKind.Infrastructure`. A standing AlwaysOn agent with a dead session and an eligible phone-home probe still attempts start.
- V-11. `Unreachable_or_draining_phone_home_does_not_climb_backoff`. A null probe does not increment `RestartBackoffFailures`. A start that throws `RunnerDraining` or `RunnerRetired` does not either.
- V-12. `Live_session_is_not_restarted`. An eligible probe and a live session do not call start.
- V-13. Four doc tests in `StandingRunnerOrchestratorDocTests`. They pin the new sentences and the literals listed under existing tests. `phone_home_pool_refused` in `docs/agent-kinds.md` still names the pinned and task shapes. `phone_home_remote_control_refused` remains. The delegate sentence names `pwsh -NoProfile -File /work/repos/antiphon/scripts/delegate.ps1`. The checkout sentence forbids credentials in the clone. The Herdr sentence says phone-home standing agents are PtyHost.
- R-1. The existing 15 `PhoneHomeStandingLaunchTests` methods stay green (CP-2).
- R-2. `Server2SidecarStandingAgentsTests` (5) and `DockerStackDocumentationTests` (18) stay green (CP-4).
- R-3. `StandingRestartEscalationTests` is not edited and is not in a filter. Desktop AlwaysOn behavior stays on the existing supervisor path.

Mutants, one per witness, restored before the slice commit:

| Witness | Mutant that must fail it |
|---|---|
| V-1, V-2 | Delete the agents branch, or admit `/work/agents/<name>/extra`, or admit `/work/repos/x`, or resolve `..`. |
| V-3 | Treat a worktree segment as a standing checkout. |
| V-4 | Drop the agents clause from the dispatcher message, or put `cwd must be` on the Herdr refusal. |
| V-5, V-6 | Delete the standing condition at `PhoneHomeLaunchPolicy.cs:198`, or allow AlwaysOn for a pinned or task agent. |
| V-7 | Project every named agent to `/work`, or let a standing path replace a non-blank task `runnerCwd`. |
| V-8 | Copy a credential env into the projection, or stop refusing remote control. |
| V-9 | Put `Directory.Exists` or `GetFullPath` back on the standing path. |
| V-10 | `return 0` on desktop infrastructure before the standing probe. |
| V-11 | Charge the backoff when the probe is null or the code is `RunnerDraining`. |
| V-12 | Restart a live session. |
| V-13 | Delete the `CARD-1695` sentence, or add `ANTHROPIC_API_KEY` to the docker-stack standing section. |

### Checkpoints

totalTimeoutMinutes: 120

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1695-cwd/ | cwd | `/*/*/StandingRunnerCwdTests/*` | V-1, V-2, V-3, V-4 | 4 executed, 0 failed, 0 skipped | 4 | 10 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1695-policy/ | policy | `/*/*/(StandingRunnerLaunchTests*)\|(PhoneHomeStandingLaunchTests*)/*` | V-5, V-6, V-7, V-8, V-9, R-1 | 21 executed, 0 failed, 0 skipped | 21 | 15 |
| CP-3 | S3 | tests/Antiphon.Tests -> bin-c1695-restart/ | restart | `/*/*/StandingRunnerRestartTests/*` | V-10, V-11, V-12 | 3 executed, 0 failed, 0 skipped | 3 | 15 |
| CP-4 | S4 | tests/Antiphon.Tests -> bin-c1695-docs/ | docs | `/*/*/(StandingRunnerOrchestratorDocTests*)\|(Server2SidecarStandingAgentsTests*)\|(DockerStackDocumentationTests*)/*` | V-13, R-2 | 27 executed, 0 failed, 0 skipped | 27 | 15 |

Code runs each row once through `scripts/run-checkpoint.ps1` (the script takes the build slot). The `Build` cell is the cold output for that row. Do not pass `-Runner` or `-Platform`. No `Serial` column. Class-operand stars do not prefix-collide: `StandingRunnerLaunchTests` is not a prefix of `PhoneHomeStandingLaunchTests`, and the three CP-4 names are not prefixes of each other. Do not shorten a filter to `StandingRunner*`. Live kill, supervisor restart, and one delegate dispatch are operator acceptance after land. They are not a checkpoint row and they are not run from the Code task.

## Performance budget

No measured p95 exists for these witnesses. The Unit lane has no measured p95 (`docs/test-performance-standards.md` R5). Every figure below is provisional. The collection plan is the first green TRX for that row: Code replaces `EstimatedMinutes` from that duration in the same commit, then sets `totalTimeoutMinutes` to `2 × new sum + 10`. A sample of one is still provisional. It is not a p95. Do not add a per-test `Timeout` attribute. Do not mark a new test `[Category("Slow")]`. If a slow-test lint fails, add the allowlist row in that same commit with the measured duration.

Expected execution, separate from build-slot queue wait (CARD-1531). Slot wait is not part of `EstimatedMinutes`. Exit 4 is a slot timeout to report, not a reason to run unleased.

| Row | Expected execution | Importer kill deadline | What the time is |
|---|---:|---:|---|
| CP-1 | 10 min | 30 min (`max(15, 3×10)`) | Cold `Antiphon.SessionRunner.Tests` build plus 4 lexical tests. No IO. |
| CP-2 | 15 min | 45 min | Cold `Antiphon.Tests` build plus 5 policy tests and 16 integration tests. |
| CP-3 | 15 min | 45 min | Cold `Antiphon.Tests` build plus 3 supervisor tests on fakes. |
| CP-4 | 15 min | 45 min | Cold `Antiphon.Tests` build plus 27 text tests. |
| Run | 55 min sum | 120 min explicit | `2 × 55 + 10`. Matches `DeriveMinimumTotalMinutes`. |

The importer's 3× kill deadline is not the R5 budget (CARD-1720). R5 wants about 2× a measured p95, and there is no p95 yet. The 15-minute estimates are also the ceiling that keeps `3 × EstimatedMinutes` from crossing the importer's 45-minute advisory. A measured row over 15 minutes is split (same `After`, `Build: CP-n` on the second row) instead of raising one cell past 15. A row whose execution approaches 30 minutes is a Debug obligation under the performance standards. Do not run the full `Antiphon.Tests` suite (about 25.5 minutes, CARD-0110).

Phases. No new `ActivitySource`. CP-1 has no external call. CP-2's new unit tests construct `PhoneHomeLaunchPolicy` with `Options.Create` and share no fixture. V-9 uses the existing per-test isolated schema in `PhoneHomeStandingLaunchTests`, not a new class fixture. CP-3 constructs the supervisor with a fake liveness and an in-memory or existing test database per test, and it does not call a live runner. CP-4 reads repo files only. Recording location is the checkpoint TRX and its sibling timing JSON. There is no external HTTP budget.

Fixture lifetime. Nothing is shared across tests except the production types under test. Do not add a class fixture to get the 15-minute estimate down.

## Migrations

None. Latest on this tree: `20261108121426_Card1773TranscriptPromptStampIndex`. Do not add a migration. `Down` is not in play. Existing AlwaysOn runner rows that are not standing checkouts stay refused at start.

## Slices

Each slice is one commit, then the checkpoint row for that `After`. Budget 60–120 minutes of implementation each.

- S1. Runner cwd admission (about 75 minutes). Add `RunnerWorkspaceAdmission` and forward `RunnerWorkspaceCwd`. Extend the dispatcher message. Add `tests/Antiphon.SessionRunner.Tests/StandingRunnerCwdTests.cs` with the four methods V-1 through V-4. See V-1 red before the forwarder admits the agents segment. CP-1.
- S2. Policy and start wiring (about 110 minutes). `PhoneHomeLaunchPolicy` standing exception and projection. `AgentControlService` host-path skip and `RunnerCwd`. `AgentService` create and PATCH refusals, including `phone_home_cwd_host_refused`. Add `tests/Antiphon.Tests/Application/StandingRunnerLaunchTests.cs` (`[Category("Unit")]`, five methods covering V-5 through V-8). Add `Standing_checkout_starts_without_a_host_directory` to `PhoneHomeStandingLaunchTests`. CP-2. Min 21 is 5 new plus 15 existing plus 1 new.
- S3. Supervisor reachability (about 100 minutes). Optional liveness parameter, directory-backed implementation, DI registration, desktop-outage split, and the draining/retired no-charge catch. Add `tests/Antiphon.Tests/Application/StandingRunnerRestartTests.cs` (`[Category("Integration")]`, not Slow, three methods, V-10 through V-12) using a fake `IPhoneHomeRunnerLiveness`. Do not add methods to `StandingRestartEscalationTests`. CP-3.
- S4. Docs and pins (about 90 minutes). Edit `docs/agent-kinds.md` (phone-home paragraphs around lines 30-47): standing checkout, AlwaysOn only for that shape, create body (`runnerId`, `workingDirectory`, `alwaysOn`, `sessionBackend: PtyHost`, `remoteControlEnabled: false`, empty Herdr labels, `createWorkingDirectory: false`), memory under `CLAUDE_CONFIG_DIR=/state/claude`, instructions file `/work/.antiphon/orchestrator-instructions.md` with the URL fallback, and the `pwsh` delegate command. Keep remote control and credential refusals. Edit the CARD-1695 sentence in `docs/docker-stack.md` (around line 994) to say the phone-home runner admits the same `/work/agents/<name>` checkout and this compose does not implement that gate. Keep the literal `CARD-1695` and do not add `ANTHROPIC_API_KEY` or `CLAUDE_CODE_OAUTH_TOKEN` to that section. Add one paragraph to `docs/orchestration-loop.md` after the human-only list (line 84): the phone-home standing orchestrator is PtyHost in `/work/agents/<name>`, the supervisor restarts a dead process, and killing AlwaysOn agents stays human-only. Add one sentence to `docs/herdr-sessions.md` section 1 (around lines 99-102) or section 6: phone-home standing agents are PtyHost, and Herdr remains the desktop runner. Add `StandingRunnerOrchestratorDocTests` with four `[Test]` methods (V-13). No new doc file. CP-4.

## Risks, operator steps, and Windows

Risks:

- `RecycleUnpublishedWork` versus an extra git clone under `/work/agents` was not fully traced. Code confirms whether `retire-temp` or volume recycle treats that clone as unpublished runner work, and records the result in the S4 doc paragraph. Do not invent a refusal. Do not delete the clone from product code.
- The cwd gate is not a sandbox. Stated under safety rules.
- CARD-1225's orchestrator deny hook is PowerShell-versus-`pwsh` and is not supported on Linux. It stays out of scope. It is not a blocker for admission.
- If `/work/repos/antiphon` is not seeded, `delegate.ps1` is absent. The operator confirms the mirror before the acceptance dispatch.
- A `CLAUDE.md` at `/work` would be inherited by `/work/worktrees/*`. The standing exception refuses `/work` so that file stays inside the named clone.
- Existing bad AlwaysOn rows still charge the backoff until an operator clears them. No migration repairs them.
- `StandingRestartEscalationTests` constructs the supervisor through `ActivatorUtilities`. The new parameter is optional. If that host cannot omit it, give the host a null liveness. Do not weaken those tests.

Operator steps, after land, not executed by Code, not on the desktop:

1. On the phone-home runner as uid 1654, clone the slides repo to `/work/agents/slides-server2`. No credential files. Confirm `git config --show-origin --get user.email` resolves to `/run/antiphon/gitconfig`.
2. Put `CLAUDE.md` only inside that clone.
3. `POST /api/agents` with the phone-home runner's `runnerId`, `workingDirectory` `/work/agents/slides-server2`, `alwaysOn` true, `sessionBackend` `PtyHost`, kind Claude Code, `remoteControlEnabled` false, empty Herdr labels, `createWorkingDirectory` false.
4. Kill the process and let the supervisor restart it. Dispatch one delegate with `pwsh -NoProfile -File /work/repos/antiphon/scripts/delegate.ps1`, cwd remaining the slides clone, and confirm it reports back.
5. Leave the desktop slides orchestrator (session `b788e4ec`) running until the operator stops it.

Rollback. `PATCH` the agent `alwaysOn` false and stop it. Revert the admission commit. The clone remains until the operator deletes it. Do not delete the work volume.

Windows. Nothing is owed natively. The lexical Windows flag is covered inside CP-1 by calling `IsAdmitted` with `windows: true`. No desktop run, no headed test, no `-Platform Windows`.
