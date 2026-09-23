# CARD-0628 — Claude Code on server2's persistent runner: plan

Plan, 2026-09-23, on `feat/card-task-a6db58bc` over the investigation at `d43c1a94`
(`docs/investigations/2026-09-23-card-0628-claude-cli-on-server2-runner.md`). Test-design is folded
into this plan (the brief asked for a full plan with a `### Checkpoints` table and `next: code`).

Operator decisions recorded on the card (2026-09-23) and honoured throughout: **no
`ANTHROPIC_API_KEY` fallback** (subscription `claude auth login` only); **one Max subscription serves
the desktop and server2** (quota contention is watched reactively); **both security findings are in
scope** (`.dockerignore` gap, missing credential probe).

**Next: code.** Every decision below is a stated default (D-13 lists them); none changes money or
custody beyond what the card already decided, so the plan does not stop for `decide`.

## Outcome and scope

When this lands, `delegate.ps1 -Runner server2 -Worktree -Kind ClaudeCode ...` creates a task that
the production server admits, the dispatcher projects onto the runner as a bare `claude` launch with
`CLAUDE_CONFIG_DIR=/state/claude`, the runner admits and starts, and the session works in the mirror
worktree exactly as a Grok remote task is designed to. A runner whose `/state/claude` has no login
fails the task before any worktree is cut, with `AuthenticationRequired` and the login command in
the reason, instead of parking on Claude's sign-in screen. The image carries `claude` 2.1.280 pinned
by SHA-256. The build context can no longer carry a `.claude/` directory or a `.credentials.json`.

Ships:

1. **Image** — `runtime-base` installs the native `linux-x64` binary 2.1.280, digest-verified, with
   a throwaway `HOME`, a `--version` assertion, and the temp tree removed in the same layer;
   `session-testing`'s inventory asserts it after the `node22` `COPY` merge. Compose projects
   `CLAUDE_CONFIG_DIR=/state/claude` and `PhoneHome__ClaudeHome`; `init-state.sh` creates
   `/runner-state/claude`.
2. **Server** — the four Grok-only gates (`AgentTaskService.cs:1044`, `PhoneHomeLaunchPolicy.cs:100`,
   `:113`, `ProjectExe`) admit `ClaudeCode`; `ChildClaudeHome` is projected as `CLAUDE_CONFIG_DIR` on
   every runner-bound launch; a runner-bound Claude launch env carrying an Anthropic API-key name is
   refused; **the task launch path is wired through `Project` at all** (Ground truth row 2 — today it
   is not); named runner-bound Claude agents refuse remote control.
3. **Runner** — the two runner-side gates admit `claude` and the `claude` transcript format
   explicitly; a `ClaudeAuthProbe` runs `claude auth status --json` against `PhoneHome:ClaudeHome`;
   a new `ProviderAuth` phone-home operation answers it on demand; a Claude launch is refused
   `provider_sign_in_required` when the probe says logged out.
4. **Readiness** — the dispatcher's `TryFailClaudeCredentialProbeAsync` (the runner-measured,
   server-decided analogue of `TryFailGrokCredentialProbeAsync`), a provider-neutral sign-in
   incident, and `GET /api/session-runners/{runnerId}/provider-auth/{provider}` for the operator's
   closed loop after login.
5. **Context denial** — `**/.claude` and `**/.credentials.json` in both `.dockerignore` files with
   four `DockerStackContractTests` arms.
6. **Docs and runbook** — `agent-credentials.md` §5 third row and Claude paragraph (names and
   locations only), `agent-kinds.md`, `ops-http.md`, `testing-and-build.md`, `docker-stack.md`, and
   the doc guards that pin those sentences.

Not in scope (unchanged, on purpose): the desktop's own CLI (CARD-0611; note it is already 2.1.280,
row 3); `ANTHROPIC_API_KEY` via `{{key:NAME}}` on the runner (refused, D-4); remote control on
runner-bound Claude (D-9); Codex/OpenCode on the runner (not installed, still refused); CARD-0490's
pinned agent (`PhoneHomeLaunchPolicy.cs:87` stays Grok-only); Cut B custody; the two Grok-specific
remote-task defects in "Not done, noted".

## Ground truth

| Card / investigation assumption | What the code and the hosts say at `d43c1a94` (measured 2026-09-23) | Consequence |
|---|---|---|
| 1. "Five explicit Grok-only refusals; the exe fix alone would leave a 409 on `transcriptFormat: "claude"`." | Four of the five are exact. The fifth's second half is not a wall: `SessionRunnerHttpClient.TranscriptFormatFor(AgentKind.ClaudeCode)` returns **null** on purpose (`:227-234`, "Claude's Format is the pre-Grok runner default; sending it would break old runners"), `RunnerContractMapper.ToLaunchRequest` forwards that null, and `PhoneHomeCommandDispatcher.RejectUnsupportedLaunch` admits a null format (`:203-205`). The runtime then treats null as Claude (`SessionRunnerRuntime.cs:1943-1944`). | D-5 widens the runner's arm anyway, so the rule is explicit (`null`, `grok`, `claude` admitted; anything else refused) and a future mapper that sends `"claude"` cannot regress it. G-9 asserts all three plus a refused `codex`. |
| 2. "`ProjectExe` has no `claude` arm, so the projection itself fails." | True for the **named-agent** path, which is the only caller of `Project`: `AgentControlService.cs:473`. The **task** path never projects. `AgentTaskDispatcher.DispatchOneAsync` calls `RefuseUnsupportedStart` (`:3659`) then `BuildLaunchSpecAsync` (`:3816`), `ResolveSpecAsync` (`:3826`), `EnsureWindowsRulesArgv` (`:3832`) and `EnqueueInteractiveSession` (`:3834`) with the registry spec untouched: `Exe = "grok.exe"` (`appsettings.json:96`), no `GROK_HOME`, no `ANTIPHON_API`. Only the cwd is fixed later (`AgentSessionService.cs:1669`, `session.RunnerCwd ?? cwd`). `PhoneHomeTaskRoutingTests.Task_session_projects_into_the_mirror_not_the_workspace_root` tests the policy method, not the dispatcher. The production board has **zero** runner-bound tasks in any status, and CARD-0604's CP-12 `remote-task-roundtrip` case was never implemented (`c590-remote.sh:1289-1313` has no such case). The S8 commit (`24bd06ed`) added only the `RefuseUnsupportedStart` call. | S3 wires `_phoneHome.Project(spec, agent, remoteCwd)` into the task path after `ResolveSpecAsync` and before the enqueue. It is on this card's critical path, so it is fixed here, with a dispatcher-level guard (G-6) that today's code fails. The orchestrator should know Grok remote tasks share the defect (report). |
| 3. Investigation: desktop CLI is 2.1.266; npm `latest` is 2.1.280. | `claude --version` on this desktop now prints `2.1.280 (Claude Code)`. The pinned digest still comes from the release manifest, not from the desktop binary. | D-2 pins 2.1.280 / `1e08503dbdf3c2cb0d706d32f3408277388d1c76ef108673e8fe42c1b322925b` (233 709 640 bytes, `linux-x64`, glibc). The desktop/container decoupling stands; CARD-0611 is unaffected by this plan. |
| 4. "`claude auth status --json` returns `loggedIn`, `authMethod`, `subscriptionType` and prints no secret." | Measured on 2.1.280. Logged in: exit **0**, `{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","analyticsDisabled":false,"projectsDirectory":…,"configDirectory":…,"email":…,"orgId":…,"orgName":…,"subscriptionType":"max"}`. Empty `CLAUDE_CONFIG_DIR`: exit **1**, `{"loggedIn":false,"authMethod":"none","apiProvider":"firstParty",…}` with no `subscriptionType`, no `email`. No token or key fragment in either. | D-7: the probe keys on the JSON `loggedIn`; the exit code is a cross-check, not the verdict; only `loggedIn`, `authMethod`, `subscriptionType` are surfaced or logged — `email`, `orgId`, `orgName` are dropped at parse time (G-12). |
| 5. "`CLAUDE_CONFIG_DIR` needs projecting the way `GROK_HOME` is." | Two readers, not one. The **child** reads its launch env. The **runner's tailer** reads the runner **process** env: `TranscriptTailer.ResolveProjectsRoot` calls `Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")` (`:1106-1112`). Compose already sets `GROK_HOME` on the service for the same reason (`docker-compose.server2-runner.yml:56`). The runner-side `PhoneHomeSettings.GrokHome` (`:19`) is declared and never read. | D-6: three values must agree — server `ChildClaudeHome` (projected), compose `CLAUDE_CONFIG_DIR` (runner process, for the tailer) and `PhoneHome__ClaudeHome` (the probe's target). G-3 ties the two compose keys together; the server default and the runner default are both `/state/claude` and each is validated POSIX-absolute. |
| 6. "Install in `runtime-base`; no node/npm needed." | `runtime-base` has `curl ca-certificates bash git`; `sha256sum` is coreutils in `aspnet:9.0` (Debian bookworm). `session-testing` then does `COPY --from=node22 /usr/local /usr/local` (`:89`), which merges — `grok` at `/usr/local/bin` survives it today, so `claude` will too — and its inventory `RUN` (`:103-116`) is where every stage-installed tool is asserted. `DockerStackContractTests` has no assertion on the Grok install line at all. | S1 adds the install to `runtime-base` and `claude --version` to the `session-testing` inventory; G-1 asserts the pinned version, the 64-hex digest, `sha256sum -c`, the throwaway `HOME` and the temp-tree removal in `runtime-base`; G-2 asserts the inventory line in `session-testing`. |
| 7. "`.dockerignore` excludes `**/.grok` and `**/auth.json` but not `**/.claude`." | Confirmed in both files (`.dockerignore:6,12`; `docker/tests/Dockerfile.dockerignore:6,12`). The repo root has `.claude/settings.json` and `.claude/skills/` (no credential today). `DockerStackContractTests.Deny` is a one-line arm per sentinel path (`:478-479`), and `DockerStackDocuments.Excluded` implements `**/` semantics. | S2 adds `**/.claude` and `**/.credentials.json` to both; four arms (`Runtime_context_denies_ClaudeHome`, `_ClaudeCredentials`, and the `Tests_` twins). `Test_context_retains_linked_sources` is unaffected. |
| 8. "Add a `claude auth status --json` readiness probe analogous to `TryFailGrokCredentialProbeAsync`." | The Grok probe (`AgentTaskDispatcher.cs:2527-2566`) is a desktop **file stat** run before the worktree is cut, gated by `Agents:GrokCredentialProbeEnabled`, failing the task with `AuthenticationRequired` and one incident per store (`GrokSignInIncident`, episode key `grok-home:<path>`). `MergeRegistryGrokProbeEnv` has no runner awareness, so for a runner-bound Grok task it inspects the **desktop** store — wrong evidence, already. The server cannot stat `/state/claude`; the runner can. The phone-home client already makes on-demand requests (`GetCapabilitiesAsync`, `PhoneHomeRunnerClient.cs:25-29`) and `ISessionRunnerClient` uses default interface members for optional capabilities (`:21,28,36`). | D-7: runner-side measurement, server-side decision. New `PhoneHomeOperation.ProviderAuth = 16`; `ISessionRunnerClient.GetProviderAuthAsync` defaulting to null; the dispatcher pre-flight fails the task before the desktop worktree and the mirror. The runner's own launch-time refusal is the backstop, exactly as its exe allow-list mirrors the server's. |
| 9. Doc guards. | `DockerStackDocumentationTests.Agent_kinds_doc_names_the_raw_allow_list` pins the literal "Grok is the only agent the image carries" (`docs/agent-kinds.md:23`); `Credentials_doc_names_the_deploy_key_custody` refuses **any** 64-hex literal in `docs/agent-credentials.md`. | S6 changes the sentence and its guard in one commit. The SHA-256 digest is written in the Dockerfile and `docs/docker-stack.md` only, never in `agent-credentials.md` (G-17 keeps that guard green). |
| 10. "`DISABLE_AUTOUPDATER=1` arrives as a Claude kind default; re-assert it survives the projection." | Pool delegates take the registry path: `AgentRegistry.Resolve` sets `DISABLE_AUTOUPDATER=1` (`:168-169`) and `CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1` for `ClaudeCode`; `Project` copies `spec.Env` wholesale (`PhoneHomeLaunchPolicy.cs:146`). | G-7 asserts both survive projection alongside `CLAUDE_CONFIG_DIR` and `ANTIPHON_API`. |
| 11. Investigation §7: "Whether Claude's TUI drives correctly over the Linux PtyHost" and first-run dialogs. | Antiphon already knows both halves: an isolated `CLAUDE_CONFIG_DIR` parks on first-run dialogs unless `.claude.json` carries `hasCompletedOnboarding` (`tests/Antiphon.Tests/Agents/RealCliStubClaudeConfig.SeedOnboarding`, `docs/agent-kinds.md:627`); `RunnerClaudeAdapter` auto-answers the trust dialog and names `TrustDialogNotCleared` when it cannot (`:158-195`); the effort dialog is handled the same way. `claude auth login` writes credentials; it is not documented to complete onboarding. | D-8's provisioning step includes one interactive `claude` run in `/work/repos/antiphon` after the login, so the theme/onboarding screens are passed once on the persistent store; the first Antiphon-driven launch then meets only the per-cwd trust dialog the adapter already clears. A stuck launch is a named `LaunchBlock`, never a silent stall. V-12 measures the first real session live. |
| 12. Which kind a runner task gets when `-Kind` is omitted. | `DelegationSettings.Roles[*].Kind` unset means `ClaudeCode` (`:973`); production roles may be promoted to Grok by config. `delegate.ps1 -Kind` is `ValidateSet('ClaudeCode','Grok','Codex')` (`:54`) and has no runner-side kind check. | D-3: the admitted pair is `{Grok, ClaudeCode}`; Codex stays refused at create (`422`) and at the policy with the same code; the refusal texts say "Grok or Claude Code". No script change. |
| 13. Investigation §6.7: "Keep `RemoteControlEnabled=false` on runner-bound Claude agents." | Pool delegates are born `RemoteControlEnabled = false` (`AgentTaskDispatcher.cs:4726`) and every launch applies `ClaudeRemoteControlLaunchArgs.ApplyOff` (`AgentSessionService.cs:1629`). A **named** runner-bound Claude agent (`POST /api/agents` with `runnerId`) is the one shape where the flag would be live: `RemoteControlPolicy.Permits(ClaudeCode)` is true and `AgentControlService.cs:253-261` only drops the flag for kinds that do not permit it. | D-9: `RefuseUnsupportedStart` gains a `remoteControl` input and refuses a runner-bound Claude start with it set (`phone_home_remote_control_refused`); `AgentService` create/PATCH mirrors it. G-8. |
| 14. Env custody on the runner. | `Project` copies the whole spec env; `ApiKeyEnvResolver.ResolveSpecAsync` (`:3826`) resolves `{{key:NAME}}` placeholders **before** the enqueue, and a project default env or a `launchEnvOverride` may carry `ANTHROPIC_API_KEY`. `AgentLaunchEnv.ValidateOverride` refuses only `ANTIPHON_*` names. | D-4: a runner-bound Claude launch whose env names `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR` or `CCR_OAUTH_TOKEN_FILE` is refused `phone_home_env_refused` at projection (after resolution, so a resolved placeholder is caught). Values are never logged (the existing `Secrets_never_cross_child_or_status_boundary` shape). |
| 15. Where Claude's state lands in the container. | `HOME=/home/app` is set by the entrypoint for the runner process (`dind-entrypoint.sh:114`) and lives in the image; `docker exec` does **not** see the entrypoint's exports, only image `ENV` and compose `environment`. With `CLAUDE_CONFIG_DIR` set, Claude writes `.claude.json` and `.credentials.json` under it (the test seed writes both there). `/runner-state` is chowned `1654:1654` by `init-state.sh` before the runner starts. | D-8's operator commands pass `-e HOME=/home/app -e CLAUDE_CONFIG_DIR=/state/claude` explicitly; `init-state.sh` creates `/runner-state/claude` so the login has a writable store; everything persistent is on the `runner-state` volume. |
| 16. `--dangerously-skip-permissions` in the container. | Claude Code refuses that flag as root. The runner and every child run as uid 1654 (`Entrypoint_drops_to_app_uid`); the registry `claude` definition's `ArgsTemplate` is `["--dangerously-skip-permissions"]` (`appsettings.json:79`), and production user-secrets override neither the definition nor its `Exe` (`claude.exe`). | No change. `ProjectExe` maps `claude.exe`/`claude` to `claude` by file name. |
| 17. The live runner today. | `GET /api/session-runners/server2/status` at 19:26Z: `available: true`, `dispatchEligible: true`, `platform: linux`, `buildVersion: 6d90c6fc`, epoch 1. Production `PhoneHomeRunner` user-secrets: `Enabled`, `AllowedRunnerId=server2`, `AllowDelegatedTasks=true`, `HostWorkspaceRoot=C:\src\Antiphon`, `RunnerWorkspace=/work`, `RunnerRepository=/work/repos/antiphon`, `ChildGrokHome=/state/grok`, `CallbackOrigin=https://antiphon.desktop.codeperf.net`. | `ChildClaudeHome` needs no user-secret (default `/state/claude`). The live rows re-deploy the runner from this branch pre-land (`deploy-parent` with `c604Branch`), and the server-side rows wait for land + `restart-apphost.ps1` (CP-8 onward). |
| 18. Harness coverage of the image. | `scripts/verify-card0604-dind-runner.ps1` boots the image on Docker Desktop with no `/state` volume (its state paths are `/tmp/state/...`), probes tools through `docker exec` (steps 4-8) and prints one graded result line. `VerifyDindRunnerScriptTests` (4 methods) pins that script's text. | S1 adds step 7b: create `/tmp/state/claude` as 1654, `claude --version` and `claude auth status --json` as `1654:1654` with `HOME=/home/app CLAUDE_CONFIG_DIR=/tmp/state/claude`; grades `claude=<version>` and `claudeAuth=logged-out`. G-4 pins the step. V-3 executes it. |

## Decisions

### D-1. Auth is the subscription login, provisioned once inside the persistent container

`docker exec -it -u 1654:1654 -e HOME=/home/app -e CLAUDE_CONFIG_DIR=/state/claude
antiphon-runner-session-runner-1 claude auth login --claudeai`. The credential lands in
`/state/claude/.credentials.json` on the `runner-state` volume, full-scope and self-refreshing. It
is never copied from the desktop, never baked, never an env value, never a Compose secret.

Rejected: `ANTHROPIC_API_KEY` (operator decision 1: it moves every server2 turn onto metered Console
billing); `claude setup-token` / `CLAUDE_CODE_OAUTH_TOKEN` (inference-only scope, no self-refresh,
an env value to redeploy on expiry); the CCR file-descriptor / token-file sources (host-injected,
unrefreshable, and the PtyHost launch path passes no inherited fd).

### D-2. The image carries the native binary, pinned by version and digest

In `runtime-base`, beside the Grok install and inside the same `RUN`:

```
ARG CLAUDE_CODE_VERSION=2.1.280
ARG CLAUDE_CODE_SHA256=1e08503dbdf3c2cb0d706d32f3408277388d1c76ef108673e8fe42c1b322925b
 && mkdir -p /tmp/claude-install \
 && curl -fsSL -o /tmp/claude-install/claude "https://downloads.claude.ai/claude-code-releases/${CLAUDE_CODE_VERSION}/linux-x64/claude" \
 && echo "${CLAUDE_CODE_SHA256}  /tmp/claude-install/claude" | sha256sum -c - \
 && install -m 0755 /tmp/claude-install/claude /usr/local/bin/claude \
 && env HOME=/tmp/claude-install CLAUDE_CONFIG_DIR=/tmp/claude-install/cfg DISABLE_AUTOUPDATER=1 /usr/local/bin/claude --version | grep -F "${CLAUDE_CODE_VERSION}" \
 && rm -rf /tmp/claude-install
```

Both `ARG`s are declared after the `runtime-base` `FROM`. The `session-testing` inventory `RUN`
gains `env HOME=/tmp/claude-probe CLAUDE_CONFIG_DIR=/tmp/claude-probe claude --version | grep -F
'2.1.280' && rm -rf /tmp/claude-probe` after the `node22` merge. The standing comment "Do not COPY
OAuth credential files or any GROK_HOME contents (CARD-0575)" is extended to name
`CLAUDE_CONFIG_DIR`.

Rejected: `https://claude.ai/install.sh | bash -s 2.1.280` (downloads the moving `latest` binary as
a bootstrap first, installs a launcher under `$HOME`, no digest check); npm (`node` is not in
`runtime-base`, and it would put a second package manager on the runtime image).

### D-3. Exactly four server gates admit `ClaudeCode`; one shared predicate, not a config list

`PhoneHomeLaunchPolicy.IsAdmittedKind(AgentKind)` — a static predicate over the literal pair
`{Grok, ClaudeCode}` — is used by `AgentTaskService` create validation (`:1044`), the delegated-task
arm (`:100`) and the named-agent arm (`:113`, which becomes "Grok, Claude Code or Raw").
`ProjectExe` gains the `claude.exe`/`claude` → `claude` arm before the pinned check. The pinned
agent's rule at `:87` is untouched (CARD-0490's narrower contract). Messages: "A runner-bound task
must be Grok or Claude Code." / "A runner-bound named agent must be Grok, Claude Code or Raw."

Rejected: `PhoneHomeRunner:AllowedKinds` (the investigation's fix idea). The admitted-kind surface is
the **image's** contract: a config list would let an operator admit Codex on a runner that does not
carry it, and the refusal would move from a 422 at create to a stall at launch. One predicate removes
the scattered literals without making the image's contents configurable from the server.

### D-4. The task path projects, and a runner-bound Claude env carries no Anthropic credential

In `DispatchOneAsync`, after `ResolveSpecAsync` and before `EnsureWindowsRulesArgv` / the enqueue:

```csharp
if (_phoneHome?.IsRunnerBound(agent) == true)
    spec = _phoneHome.Project(spec, agent, remoteCwd);
```

`Project` additionally refuses (`phone_home_env_refused`) when `spec.Kind == ClaudeCode` and the env
names any of `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`,
`CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR`, `CCR_OAUTH_TOKEN_FILE` — names only, values never
logged. The dispatcher's existing `catch` for a `ConflictException` from composition fails the task
with the code in the reason (the `FailClaimedGrokRulesDispatchAsync` shape at `:764-770` is the
model; a projection refusal happens before the session row exists, so the plain `FailAsync` tail
applies).

Rejected: silently stripping the names (the operator asked for a refusal-shaped posture, and a
task that "worked" on metered billing without anyone choosing it is the exact trap the card names);
refusing at create (the override is validated at create already, but the project default env and
`{{key:}}` resolution happen at dispatch, so only the projection sees the final env).

### D-5. The runner keeps its own copy of the rule, widened explicitly

`RejectUnsupportedLaunch`: `isClaude` mirrors `isGrok` (file name `claude`, exact `claude`, or a
path ending `/claude`); the transcript arm admits `null`, `TranscriptFormats.Grok` and
`TranscriptFormats.Claude` and refuses anything else with "Only the Grok and Claude transcript
formats are admitted." The independence from the server's list is the point (CARD-0604 D-2).

### D-6. `CLAUDE_CONFIG_DIR` is one value in three places, each validated

- Server: `PhoneHomeRunnerSettings.ChildClaudeHome` default `/state/claude`, validated POSIX-absolute
  like `ChildGrokHome`; `Project` sets `CLAUDE_CONFIG_DIR = ChildClaudeHome` on **every** runner-bound
  launch, beside `GROK_HOME` (unconditional for the same reason `GROK_HOME` is: a Raw agent that
  runs `claude` by hand gets the right store).
- Runner: `PhoneHomeSettings.ClaudeHome` default `/state/claude`, validated POSIX-absolute; the probe
  targets it.
- Compose: `CLAUDE_CONFIG_DIR: /state/claude` (runner process, for `TranscriptTailer`) and
  `PhoneHome__ClaudeHome: /state/claude`; `init-state.sh` adds `/runner-state/claude` to its
  mkdir/chown list.

### D-7. Readiness: the runner measures, the server decides, the runner backstops

1. **Probe** (`src/Antiphon.SessionRunner/ClaudeAuthProbe.cs`): runs `claude auth status --json`
   with `CLAUDE_CONFIG_DIR=<ClaudeHome>`, `HOME` inherited, `DISABLE_AUTOUPDATER=1`, stdout/stderr
   redirected, 15 s timeout, kill-tree on timeout. Parses `loggedIn` (bool), `authMethod`,
   `subscriptionType`; anything else (non-zero exit without JSON, malformed JSON, timeout, missing
   binary) is `Unknown` with a one-word error class. The raw output is never logged; the three fields
   are. The process runner is a seam (`Func<ProcessStartInfo, CancellationToken,
   Task<(int, string, string)>>`) so parsing and the outcome table are unit-tested without spawning.
2. **Operation**: `PhoneHomeOperation.ProviderAuth = 16`, request `{ "provider": "claude" }`,
   result `RunnerProviderAuthDto(string Provider, bool? LoggedIn, string? AuthMethod,
   string? SubscriptionType, DateTimeOffset CheckedAtUtc, string? Error)`; an unknown provider is
   `UnsupportedTarget` 400. `IPhoneHomeRuntimeSurface` is unchanged; the dispatcher takes an optional
   `IProviderAuthProbe` (null → every answer is `Unknown`, and the backstop admits).
3. **Backstop**: `PhoneHomeCommandDispatcher.LaunchAsync`, for a `claude` exe, runs the probe;
   `LoggedIn == false` → `PhoneHomeProblemTypes.ProviderSignInRequired = "provider_sign_in_required"`
   409 with the login command in the detail; `Unknown` admits (a probe never blocks a launch that
   would have worked — `GrokCredentialStore`'s rule). `PhoneHome:ClaudeAuthProbeEnabled` default
   true gates it.
4. **Server pre-flight**: `ISessionRunnerClient.GetProviderAuthAsync(string provider,
   CancellationToken)` with a default `null` body; `PhoneHomeRunnerClient` implements it. In
   `DispatchOneAsync`, immediately after the Grok probe (`:3552-3560`) and before any worktree or
   mirror: when `claimed.RunnerId` is set, `program.Kind == ClaudeCode` and
   `PhoneHomeRunner:ClaudeAuthProbeEnabled` (default true), `TryFailClaudeCredentialProbeAsync`
   resolves `_runners.Resolve(claimed.RunnerId)`; a `LoggedIn == false` answer fails the task
   through `FailAndNotifyAsync(claimed, reason, "claude-credential-probe", ct,
   AuthenticationRequired)` and records one incident; null (old runner or local client), `Unknown`,
   a transport exception or an unavailable runner all **proceed** (the runner-unavailable case then
   takes `PrepareRemoteWorkspaceAsync`'s existing Queued-with-warning path). The reason: "Claude Code
   is not signed in on runner 'server2' (CLAUDE_CONFIG_DIR=/state/claude). On server2 run `docker
   exec -it -u 1654:1654 -e HOME=/home/app -e CLAUDE_CONFIG_DIR=/state/claude
   antiphon-runner-session-runner-1 claude auth login`, then re-dispatch."
5. **Incident**: `GrokSignInIncident` becomes the thin Grok-keyed face of a new
   `ProviderSignInIncident.RecordAsync(db, supervisor, agentId, sessionId, episodeKey, reason)`;
   the Claude key is `claude-home:<runnerId>:<ClaudeHome>`; the Grok key format is byte-identical
   to today's (`grok-home:<path>`), so `GrokCredentialProbeDispatcherTests` stays green unchanged.
6. **Ops endpoint**: `GET /api/session-runners/{runnerId}/provider-auth/{provider}` → the live
   request → the DTO; a runner that is not connected is 409 `phone_home_unavailable`; a runner that
   answers `UnsupportedOperation` (pre-this-cut binary) is 409 `phone_home_unsupported_operation`.

Rejected: a registration-time snapshot only (stale the moment the operator logs in after boot);
heartbeat-carried state (protocol churn for a value that changes twice a year); a create-time 409
like `RefuseUnauthenticatedGrokAsync` (it would put a synchronous runner round trip inside `POST
/api/agent-tasks`; the dispatch-time failure reaches the caller within one tick with the same
code, before any worktree exists). `allowUnauthenticatedProvider` is a create-request flag that is
not persisted on the task row; the Grok dispatch-time probe does not consult it either, and this one
keeps parity — the kill switch is the setting.

### D-8. Provisioning: what Code proves, what the operator does, in that order (the D-16 pattern)

Code proves, with no login on the store:

- the binary runs standalone in the image and reports 2.1.280 (V-2 locally, V-8 on server2);
- `claude auth status --json` against a fresh store is `loggedIn: false`, exit 1 (V-3, V-9);
- the phone-home probe reports that fact end-to-end through the production server (V-10, after
  land);
- a Claude task dispatched to the un-provisioned runner fails with `AuthenticationRequired` and the
  login command, cuts no worktree and no mirror (G-13 as a unit guard; V-11 live if the operator has
  not yet logged in when CP-9 runs — otherwise V-11 is recorded as "provisioned before measurement"
  and G-13 stands as its evidence).

The operator does, once, on server2, and it is a live interactive action Code cannot perform:

1. `docker exec -it -u 1654:1654 -e HOME=/home/app -e CLAUDE_CONFIG_DIR=/state/claude
   antiphon-runner-session-runner-1 claude auth login --claudeai` → open the printed URL in the
   desktop browser → paste the redirect URL (`?code=...&state=...`) back into the container prompt.
2. Same `docker exec` shape with `claude auth status --json` → `loggedIn: true`,
   `subscriptionType: max`.
3. `docker exec -it -u 1654:1654 -e HOME=/home/app -e CLAUDE_CONFIG_DIR=/state/claude -w
   /work/repos/antiphon antiphon-runner-session-runner-1 claude` → pass the theme/onboarding
   screens and accept trust for `/work/repos/antiphon` (worktrees under `/work/worktrees/` are
   separate cwds and get the per-cwd dialog the adapter already clears) → `/exit`.
4. From the desktop: `curl https://antiphon.desktop.codeperf.net/api/session-runners/server2/provider-auth/claude`
   → `loggedIn: true` (the closed loop; V-10 with the opposite value before step 1).

Recorded in `docs/agent-credentials.md` §5 as a third table row (names and locations only: store
`/state/claude/.credentials.json` on the `runner-state` volume, owner uid 1654, provisioned by the
interactive login, never copied, never baked, never printed) plus a paragraph mirroring the Grok one.
A missing or expired store is the dispatch-time `AuthenticationRequired` and the runner's 409
`provider_sign_in_required`.

### D-9. Remote control is refused for runner-bound Claude in this cut

`RefuseUnsupportedStart` gains `bool remoteControl`; a runner-bound start with it true throws
`phone_home_remote_control_refused`. `AgentService` create/PATCH refuses `remoteControlEnabled:
true` on an agent with `runnerId`. Pool delegates are already off. Reason: the flag is live only for
`ClaudeCode` (`AgentTuiRunnerCatalog.cs:116`), so this is the first runner-bound shape where it
would type into a session, and the outbound-WS dependency from server2 is unmeasured.

### D-10. Context denial closes both files, plus the credential file name

`**/.claude` and `**/.credentials.json` in `.dockerignore` and `docker/tests/Dockerfile.dockerignore`
— the CARD-0575 shape. `.claude/settings.json` is not needed by any image (a session's checkout
brings its own through git).

### D-11. Docs: every sentence a guard pins changes with its guard

`docs/agent-kinds.md:15-23` ("Runner-bound delegated Worktree tasks ... are Grok or Claude Code";
"Grok and Claude Code are the agents the image carries; Codex is not installed there"; remote
control refused on runner-bound Claude); `docs/ops-http.md:62-64` (status row unchanged; new
provider-auth row; task row "Worktree + Grok or Claude Code"); `docs/testing-and-build.md:200`
("routes an ordinary Grok or Claude task there") plus the provisioning pointer;
`docs/docker-stack.md:5` (adds "Claude Code 2.1.280, pinned by SHA-256 `1e08503d…22925b`");
`docs/agent-credentials.md` §5 (D-8). `docs/bootstrap.md:15` ("The image pins Grok 1.0.40") gains
"and Claude Code 2.1.280" since the CARD-0490 canary shares `runtime-base`.

### D-12. Live verification is split at the land boundary

Runner-side rows run pre-land against a runner rebuilt from this branch (`deploy-parent` with the
manifest's `c604Branch` naming the task branch, the same pre-land mechanism CARD-0604 used); the
production **server** at master never calls the new operation and never sees the widened runner
rule, so the branch runner is wire-compatible with it. Server-side and end-to-end rows need the
land and `restart-apphost.ps1` from the main checkout (an operator-visible production change, the
CARD-0604 CP-6a shape) and are marked `After: land`. The orchestrator dispatches those rows as the
post-land Code follow-up; they are listed here so the table stays the closed list.

### D-13. Defaults this plan is written under

- D-1: subscription login only; no API-key opt-in on the runner (card decision 1).
- D-2: version 2.1.280 and its published digest; `runtime-base` placement.
- D-3: the literal admitted pair, not a setting; the pinned agent stays Grok-only.
- D-4: refusal, not stripping, of Anthropic credential names on a runner-bound Claude launch.
- D-6: `/state/claude` in all three places; unconditional projection.
- D-7: probe timeout 15 s; `Unknown` admits; no create-time 409; the pre-flight setting defaults on.
- D-9: remote control refused for runner-bound Claude.
- D-12: pre-land runner redeploy from the branch is acceptable (the runner has no other consumer).

## Target shape

- `runtime-base` installs `claude` 2.1.280 by digest; `session-testing` asserts it; both
  `.dockerignore` files deny `**/.claude` and `**/.credentials.json`.
- `docker-compose.server2-runner.yml`: `CLAUDE_CONFIG_DIR` + `PhoneHome__ClaudeHome` = `/state/claude`;
  `init-state.sh` creates `/runner-state/claude`.
- Server: `PhoneHomeLaunchPolicy.IsAdmittedKind`; `ProjectExe` claude arm; `Project` sets
  `CLAUDE_CONFIG_DIR`, refuses Anthropic credential names, is called from the task path;
  `RefuseUnsupportedStart(remoteControl:)`; `ChildClaudeHome` + `ClaudeAuthProbeEnabled` settings;
  `TryFailClaudeCredentialProbeAsync`; `ProviderSignInIncident`; provider-auth endpoint.
- Runner: `ClaudeHome` + `ClaudeAuthProbeEnabled` settings; `ClaudeAuthProbe`; `ProviderAuth`
  operation; widened `RejectUnsupportedLaunch`; launch-time backstop.
- Contracts: `PhoneHomeOperation.ProviderAuth`, `RunnerProviderAuthDto`,
  `PhoneHomeProblemTypes.ProviderSignInRequired`.
- Docs and guards as D-11.

## Slices

Each slice is one or two commits; commit and push before any checkpoint build. Files named are the
ones to edit; tests named are the ones to write or extend.

### S1. Image, compose, state init, local harness

Files: `docker/session-runner-grok/Dockerfile` (`runtime-base` install; `session-testing`
inventory; comment), `docker-compose.server2-runner.yml`, `docker/stack/init-state.sh`,
`scripts/verify-card0604-dind-runner.ps1` (docker run gains `-e CLAUDE_CONFIG_DIR=/tmp/state/claude
-e PhoneHome__ClaudeHome=/tmp/state/claude`; step 7b creates `/tmp/state/claude` as 1654, runs
`claude --version` and `claude auth status --json` as `1654:1654` with `HOME=/home/app`, writes
`claude-version.txt` / `claude-auth-status.txt`, grades `claude=<version|no>` and
`claudeAuth=<logged-out|unexpected>` on the result line; both must be ok for exit 0).

Tests: `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs` —
`Runner_pins_claude_by_version_and_digest` (G-1), `Testing_stage_asserts_claude_after_node_merge`
(G-2), `Server2_compose_projects_claude_config_dir` (G-3);
`tests/Antiphon.Tests/Infrastructure/DindRunnerContractTests.cs` —
`Init_state_creates_the_claude_home` (G-3a); `tests/Antiphon.Tests/Scripts/VerifyDindRunnerScriptTests.cs`
— `Harness_probes_claude_as_the_app_uid_and_expects_logged_out` (G-4).

### S2. Context denial

Files: `.dockerignore`, `docker/tests/Dockerfile.dockerignore`.
Tests: `DockerStackContractTests` — `Runtime_context_denies_ClaudeHome`,
`Runtime_context_denies_ClaudeCredentials`, `Tests_context_denies_ClaudeHome`,
`Tests_context_denies_ClaudeCredentials` (G-5).

### S3. Server gates, settings, projection, remote control

Files: `server/Application/Settings/PhoneHomeRunnerSettings.cs` (`ChildClaudeHome`,
`ClaudeAuthProbeEnabled`, validation), `server/Application/Services/PhoneHomeLaunchPolicy.cs`
(`IsAdmittedKind`, `:100`, `:113`, `ProjectExe`, `Project` env + refusal, `remoteControl`
parameter), `server/Application/Services/AgentTaskService.cs` (`:1044`),
`server/Application/Services/AgentTaskDispatcher.cs` (the `Project` call after `ResolveSpecAsync`;
the `remoteControl: false` argument at `:3659`), `server/Application/Services/AgentControlService.cs`
(`remoteControl:` at `:464`), `server/Application/Services/AgentService.cs` (create/PATCH refusal for
`runnerId` + `remoteControlEnabled`).

Tests: `tests/Antiphon.Tests/Application/PhoneHomeTaskRoutingTests.cs` — rename
`Only_grok_runs_a_runner_bound_task` → `Only_grok_and_claude_run_a_runner_bound_task` (G-6a: Codex,
OpenCode, Raw refused; Grok and ClaudeCode admitted), `Task_session_projects_claude_exe_and_config_dir`
(G-7), `Runner_bound_claude_launch_refuses_anthropic_credential_names` (G-10);
`tests/Antiphon.Tests/Application/PhoneHomeStandingLaunchTests.cs` —
`Runner_bound_named_claude_agent_is_admitted_and_projects` (G-6b),
`Runner_bound_claude_remote_control_is_refused` (G-8), and the existing pinned-agent methods keep
their outcomes (R-1); `tests/Antiphon.Tests/Application/PhoneHomeRunnerSettingsValidatorTests.cs` —
`Child_claude_home_must_be_posix_absolute` and `Defaults_are_the_documented_server2_shape` extended
(G-11); new `tests/Antiphon.Tests/Application/PhoneHomeTaskDispatchProjectionTests.cs` (modelled on
`GrokCredentialProbeDispatcherTests.CreateDispatcher`: isolated schema, `AgentSessionLaunchQueue`
replaced by a recording fake, an `ISessionRunnerDirectory` fake whose `Resolve("server2")` returns
a client answering the mirror and provider-auth requests) — `Runner_bound_claude_task_reaches_the_queue_projected`
(G-6: Exe `claude`, Cwd the mirror, `CLAUDE_CONFIG_DIR`, `ANTIPHON_API`, `DISABLE_AUTOUPDATER`,
`CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN`; **red today**) and `Runner_bound_grok_task_reaches_the_queue_projected`
(G-6c: Exe `grok`, `GROK_HOME`; **red today**); new `tests/Antiphon.Tests/Application/PhoneHomeTaskCreateTests.cs` (harness borrowed from
`AgentTaskServiceIntegrationTests`, kept separate so CP-2 does not drag its 87 methods) —
`Runner_bound_create_admits_claude_and_refuses_codex` (G-14, the `:1044` guard that has no test
today).

### S4. Runner settings, gates, probe, operation, backstop

Files: `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs` (`ProviderAuth = 16`,
`PhoneHomeProviderAuthRequest`, `RunnerProviderAuthDto`, `ProviderSignInRequired` problem type),
`src/Antiphon.SessionRunner/PhoneHomeSettings.cs` (`ClaudeHome`, `ClaudeAuthProbeEnabled`,
validation), `src/Antiphon.SessionRunner/ClaudeAuthProbe.cs` (new, with `IProviderAuthProbe`),
`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs` (`RejectUnsupportedLaunch`, the
`ProviderAuth` arm, the launch-time backstop, optional probe ctor parameter),
`src/Antiphon.SessionRunner/Program.cs` (wire the probe).

Tests: `tests/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs` —
`Claude_exe_is_image_owned` (G-9a), `Transcript_format_null_grok_and_claude_admitted_codex_refused`
(G-9), `Claude_launch_is_refused_when_the_probe_says_logged_out` (G-15),
`Claude_launch_is_admitted_when_the_probe_is_unknown_or_absent` (G-15a),
`Provider_auth_operation_answers_the_probe_and_refuses_unknown_providers` (G-16); new
`tests/Antiphon.SessionRunner.Tests/ClaudeAuthProbeTests.cs` — `Logged_in_json_is_parsed_to_the_three_fields_only`
(G-12: the fixture carries `email`/`orgId`/`orgName`; the DTO and the log line do not),
`Logged_out_json_with_exit_1_is_logged_out`, `Garbage_nonzero_and_timeout_are_unknown`,
`Probe_passes_claude_home_as_config_dir` (G-12a, the seam records the `ProcessStartInfo`);
`Fresh_config_dir_reports_logged_out_with_the_real_cli` (`[Category("Integration")]`, skips when
`claude` is not on `PATH`; V-1).

### S5. Server pre-flight, incident, client, endpoint

Files: `server/Application/Interfaces/ISessionRunnerClient.cs` (`GetProviderAuthAsync` default
null), `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs`,
`server/Application/Services/ProviderSignInIncident.cs` (new) + `GrokSignInIncident.cs` (delegates),
`server/Application/Services/AgentTaskDispatcher.cs` (`TryFailClaudeCredentialProbeAsync` after
`:3560`), `server/Api/Endpoints/SessionRunnerEndpoints.cs` (provider-auth route),
`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs` (a `RequestProviderAuthAsync`
that routes through the live connection and maps disconnected/unsupported to the two 409s).

Tests: new `tests/Antiphon.Tests/Application/ClaudeCredentialProbeDispatcherTests.cs` (the Grok
probe test's shape) — `Runner_bound_claude_with_a_logged_out_runner_fails_before_any_worktree`
(G-13: `Failed`, `AuthenticationRequired`, reason contains `claude auth login`, `AgentSessionId`
null, `WorktreePath` null, exactly one `ProviderSignInRequired` incident keyed
`claude-home:server2:/state/claude`, the parent's note contains the command),
`Unknown_or_null_answers_proceed` (G-13a), `Probe_disabled_does_not_fail` (G-13b),
`Grok_runner_bound_task_is_not_probed_for_claude` (G-13c); `GrokCredentialProbeDispatcherTests`
unchanged and green (R-2); `tests/Antiphon.Tests/Application/PhoneHomeDirectoryTests.cs` —
`Provider_auth_endpoint_routes_to_the_live_runner_and_409s_when_absent` (G-18, through
`PhoneHomeTestHost`).

### S6. Docs and guards

Files: `docs/agent-credentials.md`, `docs/agent-kinds.md`, `docs/ops-http.md`,
`docs/testing-and-build.md`, `docs/docker-stack.md`, `docs/bootstrap.md`.
Tests: `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs` —
`Agent_kinds_doc_names_the_raw_allow_list` updated to the new sentence (G-17a),
`Credentials_doc_names_the_claude_login` (G-17: `/state/claude`, `claude auth login`,
`provider_sign_in_required`, no 64-hex literal, no `sk-ant-`), `Ops_http_names_provider_auth`
(G-17b), `Docker_stack_doc_names_the_nested_daemon` extended with the digest prefix (G-17c).

### S7. Live: runner redeploy, land-gated acceptance, provisioning

No source; evidence only. Pre-land: `deploy-parent` from the branch, then the two `docker exec`
probes on server2 (V-8, V-9). Post-land (D-12): production restart (V-7), provider-auth endpoint
before login (V-10), the un-provisioned Claude dispatch (V-11, if still measurable), the operator's
D-8 steps, the endpoint after login (V-10b), and the Claude roundtrip (V-12).

## Verification design

Bodies read for this plan: `server/Application/Services/PhoneHomeLaunchPolicy.cs` (whole),
`PhoneHomeRunnerSettings.cs` (whole), `AgentTaskService.cs:1000-1090,3300-3360`,
`AgentTaskDispatcher.cs:2500-2640,3530-3580,3636-3740,3808-3862,4332-4420,4700-4745`,
`AgentControlService.cs:436-500,250-265,1035-1050`, `AgentSessionService.cs:286-330,880-915,1596-1700,1868-1900`,
`AgentRegistry.cs:95-175`, `AgentTuiLaunchResolver.cs:175-200,585-610`, `AgentTuiRunnerCatalog.cs:30-60,110-120`,
`ModelLevelAliases.cs:25-33`, `ClaudeLaunchArgs.cs`, `GrokSignInIncident.cs`, `AgentLaunchBlock.cs`,
`ProviderSignInRequiredException.cs`, `AgentLaunchEnv.cs:128-160`, `RemoteControlPolicy.cs` (grep),
`server/Api/Endpoints/SessionRunnerEndpoints.cs`, `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs:20-75,195-225`,
`PhoneHomeRunnerDirectory.cs:240-300`, `PhoneHomeLiveConnection.cs:50-85`, `RoutingSessionRunnerClient.cs`,
`RunnerContractMapper.cs:1-45`, `SessionRunnerHttpClient.cs:222-240`, `RunnerClaudeAdapter.cs:150-200`,
`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs` (whole), `PhoneHomeSettings.cs`,
`PhoneHomeRuntimeAdapter.cs`, `PhoneHomeConnectionService.cs:70-130,200-210`, `RunnerWorkspaceService.cs:160-215`,
`SessionRunnerRuntime.cs:20-30,270-320,1940-1950`, `TranscriptTailer.cs:1100-1115`,
`src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs:18-135,160-170`, `SessionRunnerContracts.cs:165-180,940-975`,
`src/Antiphon.Agents.Pty/GrokCredentialStore.cs:1-60`, `ClaudeBlockingPrompt.cs:1-70`,
`docker/session-runner-grok/Dockerfile`, `dind-entrypoint.sh`, `docker/stack/init-state.sh`,
`docker-compose.server2-runner.yml`, `.dockerignore`, `docker/tests/Dockerfile.dockerignore`,
`scripts/verify-card0604-dind-runner.ps1:1-215`, `scripts/c590-remote.sh:972-1042,1289-1313`,
`scripts/c590-real.ps1:160-260`, `scripts/verify-docker-stack.ps1:120-175`, `scripts/delegate.ps1:50-60,905-940`,
`tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs:30-75,100-135,160-180,268-300,355-400,478-560`,
`DindRunnerContractTests.cs:74-135,244-265`, `DockerStackDocumentationTests.cs`, `DockerStackDocuments.cs:15-32`,
`tests/Antiphon.Tests/Application/PhoneHomeTaskRoutingTests.cs`, `PhoneHomeStandingLaunchTests.cs:227-258,640-700`,
`PhoneHomeRunnerSettingsValidatorTests.cs:56-85`, `GrokCredentialProbeDispatcherTests.cs:26-57,100-195`,
`tests/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs`,
`tests/Antiphon.Tests/Agents/RealCliStubClaudeConfig.cs`, `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs:24-60`,
`docs/agent-credentials.md` §5, `docs/agent-kinds.md:15-35,345-360`, `docs/ops-http.md:60-66`,
`docs/testing-and-build.md:109-230`, `docs/docker-stack.md:1-17`, CARD-0604 plan (ground truth,
D-16, verification design, checkpoints), the CARD-0628 investigation (whole), card text.

### Inspection

- `DockerStackContractTests`, `DindRunnerContractTests`, `VerifyDindRunnerScriptTests`,
  `DockerStackDocumentationTests` | `[Category("Unit")]`, pure file reads; the new arms stay
  file-only; no OS skip.
- `PhoneHomeTaskRoutingTests`, `PhoneHomeStandingLaunchTests`, `PhoneHomeRunnerSettingsValidatorTests`
  | policy and settings in isolation, `Options.Create`; `PhoneHomeStandingLaunchTests` and
  `PhoneHomeDirectoryTests` also use `PhoneHomeTestHost` (a real `WebApplication` with the
  directory and a recording local client) for the endpoint arm.
- `PhoneHomeTaskDispatchProjectionTests`, `ClaudeCredentialProbeDispatcherTests` |
  `GrokCredentialProbeDispatcherTests`'s harness: `TestDbFixture.CreateIsolatedSchemaAsync`, a
  service collection with `AgentSessionLaunchQueue` replaced by a recording fake and an
  `ISessionRunnerDirectory` fake; `[Category("Integration")]`, Postgres required, no process spawn.
- `PhoneHomeCommandDispatcherTests`, `ClaudeAuthProbeTests` | runner-side, no socket;
  `RejectUnsupportedLaunch` is `internal`; the probe's process runner is a delegate seam; the one
  real-CLI method skips without `claude` on `PATH`.
- Missing setup recorded: Docker Desktop up for CP-5 (`docker-desktop` skill); `mc@server2` SSH and
  the `c604Branch` manifest for CP-6; production user-secrets access and the `restart-apphost.ps1`
  runbook for CP-8; the operator's D-8 login before CP-10.

### Delivery inventory

Dockerfile, compose, `init-state.sh`, two `.dockerignore` files, the local harness script; server:
`PhoneHomeRunnerSettings`, `PhoneHomeLaunchPolicy`, `AgentTaskService`, `AgentTaskDispatcher`,
`AgentControlService`, `AgentService`, `ProviderSignInIncident` (+ `GrokSignInIncident`),
`ISessionRunnerClient`, `PhoneHomeRunnerClient`, `PhoneHomeRunnerDirectory`,
`SessionRunnerEndpoints`; runner: `PhoneHomeSettings`, `ClaudeAuthProbe`,
`PhoneHomeCommandDispatcher`, `Program.cs`; contracts: `PhoneHomeContracts`; docs: six files; tests:
eleven classes touched or created.

### Proves it works now

- V-1 | `claude auth status --json` against an empty `CLAUDE_CONFIG_DIR` parses to `loggedIn:
  false` through `ClaudeAuthProbe` using the real desktop binary | `ClaudeAuthProbeTests.Fresh_config_dir_reports_logged_out_with_the_real_cli` | CP-3.
- V-2 | the pinned binary runs standalone in the built image and prints 2.1.280 as uid 1654 | harness
  `claude=2.1.280` | CP-5.
- V-3 | the fresh in-image store reports logged out (exit 1, `loggedIn: false`) | harness
  `claudeAuth=logged-out` | CP-5.
- V-4 | the image still passes every CARD-0604 harness grade (nested daemon, egress, loopback,
  launch, key, restart, refusals) | `C604 HARNESS EXIT CODE: 0` | CP-5.
- V-5 | the widened contract, dockerignore and doc guards are green on the branch | CP-1, CP-4.
- V-6 | a runner-bound Claude task reaches the launch queue projected (Exe `claude`, mirror cwd,
  `CLAUDE_CONFIG_DIR`, `ANTIPHON_API`, autoupdater off) and a logged-out runner fails it before any
  worktree | CP-2.
- V-7 | production serves the landed sha (`GET /api/version`) and `server2` is `dispatchEligible`
  within 120 s of the restart | CP-8 (post-land).
- V-8 | the server2 runner rebuilt from the branch reports 2.1.280 in-container (`ssh mc@server2
  docker exec -u 1654:1654 -e HOME=/home/app -e CLAUDE_CONFIG_DIR=/state/claude
  antiphon-runner-session-runner-1 claude --version`) and re-registers `dispatchEligible: true`
  with the new `buildVersion` | CP-6.
- V-9 | the same `docker exec` with `claude auth status --json` is `loggedIn: false`, and
  `/state/claude` exists owned by 1654 (`stat -c '%u %a' /state/claude`) | CP-6.
- V-10 | `GET /api/session-runners/server2/provider-auth/claude` through production answers
  `loggedIn: false` before the login and `loggedIn: true`, `subscriptionType: max` after it
  (V-10b) | CP-9, CP-10.
- V-11 | a Claude task dispatched to the un-provisioned runner settles `Failed` /
  `AuthenticationRequired` with the login command in its reason, cut no desktop worktree and no
  mirror | CP-9 (conditional, D-8).
- V-12 | `delegate.ps1 -Runner server2 -Worktree -Kind ClaudeCode -Level High -Role Custom -Title
  "c628 roundtrip" -Goal "Reply with CARD0628_OK_<nonce> and do not use tools. Commit nothing."`
  settles `Succeeded`; the session's transcript on the runner carries the nonce; the mirror is
  removed at retirement; no `LaunchBlock` recorded (or `TrustDialogNotCleared`/onboarding is
  recorded as the named finding) | CP-10.
- V-13 | `RemoteControlEnabled: true` on `POST /api/agents` with `runnerId` and kind ClaudeCode is
  409 `phone_home_remote_control_refused` | CP-2 (G-8) and a one-line live `curl` in CP-10's
  evidence.

### Guards the regression

- R-1 | CARD-0490's pinned agent still refuses a non-Grok kind (`phone_home_kind_refused` at `:87`)
  and every existing `PhoneHomeStandingLaunchTests` outcome holds | CP-2.
- R-2 | `GrokCredentialProbeDispatcherTests` (3) green unchanged: the Grok episode key and failure
  reason are byte-identical | CP-2.
- R-3 | every existing `DockerStackContractTests` / `DindRunnerContractTests` / `VerifyDindRunnerScriptTests`
  method green (94 + 17 + 4 today) | CP-1.
- R-4 | `PhoneHomeCommandDispatcherTests` (6) green unchanged: Raw allow-list, cwd, capacity,
  workspace ops | CP-3.
- R-5 | `PhoneHomeConnectionServiceTests` (3) green: the registration payload is unchanged | CP-3.
- G-1 | `runtime-base` body contains `CLAUDE_CODE_VERSION=2.1.280`, the 64-hex digest,
  `sha256sum -c`, `downloads.claude.ai/claude-code-releases/`, `install -m 0755 ... /usr/local/bin/claude`,
  `HOME=/tmp/claude-install`, `rm -rf /tmp/claude-install`, and the CARD-0575 comment names
  `CLAUDE_CONFIG_DIR` | `Runner_pins_claude_by_version_and_digest`.
- G-2 | `session-testing` body contains `claude --version | grep -F '2.1.280'` **after**
  `COPY --from=node22` (order asserted) | `Testing_stage_asserts_claude_after_node_merge`.
- G-3 | server2 compose `CLAUDE_CONFIG_DIR` == `PhoneHome__ClaudeHome` == `/state/claude` ==
  `new PhoneHomeRunnerSettings().ChildClaudeHome` == `new PhoneHomeSettings().ClaudeHome` |
  `Server2_compose_projects_claude_config_dir`.
- G-3a | `init-state.sh` lists `/runner-state/claude` in its mkdir loop |
  `Init_state_creates_the_claude_home`.
- G-4 | the harness text runs `claude auth status --json` with `-u 1654:1654`, `HOME=/home/app`,
  `CLAUDE_CONFIG_DIR=/tmp/state/claude`, grades `claudeAuth=logged-out`, and never passes a
  credential-looking env name | `Harness_probes_claude_as_the_app_uid_and_expects_logged_out`.
- G-5 | four `Deny` arms: `.claude/settings.json` and `scratch/.credentials.json` in both contexts.
- G-6 | dispatcher-level: a runner-bound Claude task's queued spec is projected (row 2) —
  red today | `Runner_bound_claude_task_reaches_the_queue_projected`.
- G-6a | policy: Codex/OpenCode/Raw refused for tasks; Grok and ClaudeCode admitted; messages name
  "Grok or Claude Code" | `Only_grok_and_claude_run_a_runner_bound_task`.
- G-6b | named runner-bound Claude agent admitted and projected to `claude` at `/work` |
  `Runner_bound_named_claude_agent_is_admitted_and_projects`.
- G-6c | dispatcher-level: a runner-bound Grok task's queued spec is `grok` + `GROK_HOME` — red
  today | `Runner_bound_grok_task_reaches_the_queue_projected`.
- G-7 | `Project` on a Claude spec: `CLAUDE_CONFIG_DIR=/state/claude`, `ANTIPHON_API`, `GROK_HOME`,
  `DISABLE_AUTOUPDATER=1`, `CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1` all present; Exe `claude` from
  `C:\Users\x\.local\bin\claude.exe` | `Task_session_projects_claude_exe_and_config_dir`.
- G-8 | `RefuseUnsupportedStart(remoteControl: true)` on a runner-bound Claude agent throws
  `phone_home_remote_control_refused`; a local Claude agent with the flag is untouched |
  `Runner_bound_claude_remote_control_is_refused`.
- G-9 | runner: transcript formats `null`, `grok`, `claude` admitted; `codex` refused with the new
  text | `Transcript_format_null_grok_and_claude_admitted_codex_refused`.
- G-9a | runner: `claude`, `/usr/local/bin/claude` admitted; `/opt/evil/claude ` (trailing space),
  `claude.exe`, `C:\tools\claude` refused | `Claude_exe_is_image_owned`.
- G-10 | `Project` refuses each of the five Anthropic credential names on a Claude spec with
  `phone_home_env_refused`, admits them on a Grok spec (out of scope for that kind), and the
  exception message names the key, never the value (sentinel value asserted absent) |
  `Runner_bound_claude_launch_refuses_anthropic_credential_names`.
- G-11 | `ChildClaudeHome = "state/claude"` fails validation with the named message; defaults test
  asserts `/state/claude` and `ClaudeAuthProbeEnabled == true` | `Child_claude_home_must_be_posix_absolute`.
- G-12 | probe parse: `email`, `orgId`, `orgName` never appear in the DTO or the logged line;
  `loggedIn`/`authMethod`/`subscriptionType` do | `Logged_in_json_is_parsed_to_the_three_fields_only`.
- G-12a | the probe's `ProcessStartInfo` carries `CLAUDE_CONFIG_DIR=<ClaudeHome>` and
  `DISABLE_AUTOUPDATER=1`, argv `auth status --json` | `Probe_passes_claude_home_as_config_dir`.
- G-13 | dispatcher pre-flight fails a runner-bound Claude task before any worktree on
  `loggedIn: false` (assertions listed in S5) — red today.
- G-13a/b/c | `Unknown`/null proceed; setting off proceeds; a Grok task never asks for `claude`.
- G-14 | `AgentTaskService.CreateAsync` with `runnerId` admits ClaudeCode and refuses Codex with the
  new message (422) | `Runner_bound_create_admits_claude_and_refuses_codex`.
- G-15 | runner backstop: a `claude` launch with a probe answering logged out is
  `provider_sign_in_required` 409 and `StartAsync` never runs; `Unknown` or no probe admits
  (G-15a); a `grok` launch never probes.
- G-16 | `ProviderAuth` operation returns the probe's DTO for `claude`; `codex` is
  `UnsupportedTarget` 400.
- G-17 | `agent-credentials.md` names `/state/claude`, `claude auth login`,
  `provider_sign_in_required`, and contains no 64-hex literal and no `sk-ant-`.
- G-17a/b/c | `agent-kinds.md` new sentence; `ops-http.md` names `provider-auth`; `docker-stack.md`
  names `2.1.280` and the digest prefix `1e08503d`.
- G-18 | endpoint: 409 `phone_home_unavailable` with no live runner; the DTO when the recording
  connection answers.

### Positive controls

Method-scoped `--treenode-filter "/*/*/<Class>/<Method>"` for Mutation; each must go red then
green. Batch only across different files.

| PC | Mutation | Expected red |
|---|---|---|
| PC-1 | `IsAdmittedKind` returns true only for Grok | G-6a, G-14 |
| PC-2 | `ProjectExe` drops the `claude` arm | G-6b, G-7 |
| PC-3 | dispatcher: remove the `Project` call after `ResolveSpecAsync` | G-6, G-6c |
| PC-4 | `Project` skips the credential-name refusal | G-10 |
| PC-5 | runner `RejectUnsupportedLaunch`: `isClaude` always false | G-9a |
| PC-6 | runner transcript arm admits any string | G-9 |
| PC-7 | `ClaudeAuthProbe` maps exit 1 to `LoggedIn = true` | G-12, V-1 |
| PC-8 | `TryFailClaudeCredentialProbeAsync` returns false unconditionally | G-13 |
| PC-9 | backstop admits on `LoggedIn == false` | G-15 |
| PC-10 | `.dockerignore` line `**/.claude` removed | G-5 |
| PC-11 | Dockerfile digest last hex digit changed | G-1 |
| PC-12 | `RefuseUnsupportedStart` ignores `remoteControl` | G-8 |

### Operator provisioning (what Code verifies vs the operator's live action)

See D-8. Code-verifiable without a login: V-2, V-3, V-8, V-9, V-10 (false arm), G-13, G-15.
Operator-only: the interactive `claude auth login`, the one-time onboarding run, and the desktop
browser hop. Code-verifiable after the login: V-10b, V-12.

### Live cases

Pre-land (runner only): `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case deploy-parent
-Manifest <manifest with c604Branch=feat/card-task-a6db58bc, c604Origin=https://antiphon.desktop.codeperf.net>`,
then the two `ssh mc@server2 docker exec ...` probes (V-8, V-9) and
`curl https://antiphon.desktop.codeperf.net/api/session-runners/server2/status` (new `buildVersion`,
`dispatchEligible: true`). Post-land: `git pull --rebase` in `C:\src\Antiphon`, `pwsh -NoProfile
-File scripts/restart-apphost.ps1`, `GET /api/version` = landed sha, then `deploy-parent` again
from master (the runner at the landed sha), then CP-9/CP-10.

### Out of scope

`EnsureWindowsRulesArgv` on a projected Grok remote spec, and the desktop-store Grok probe for
runner-bound Grok tasks (both "Not done, noted"); Cut B custody; Codex on the runner; the CARD-0490
canary compose (`docker-compose.runner-grok.yml`) gains the binary through `runtime-base` but no
Claude wiring.

### Checkpoints

Rows CP-1 to CP-6 are this Code round's closed list. CP-7 to CP-10 are `After: land` (D-12) and
are the closed list of the post-land follow-up; they are listed so the table is complete.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3, S5 | `tests/Antiphon.Tests -> bin-c628/` | contract-guards | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(VerifyDindRunnerScriptTests*)/*` | V-5, R-3, G-1..G-5 | all listed, 0 failed (94 + 17 + 4 today, plus 3 + 1 + 1 + 4 new) | 124 | 12 |
| CP-2 | S1-S3, S5 | CP-1 | phone-home-guards | `/*/*/(PhoneHomeTaskRoutingTests*)\|(PhoneHomeStandingLaunchTests*)\|(PhoneHomeRunnerSettingsValidatorTests*)\|(PhoneHomeDirectoryTests*)\|(PhoneHomeTaskDispatchProjectionTests*)\|(ClaudeCredentialProbeDispatcherTests*)\|(GrokCredentialProbeDispatcherTests*)\|(PhoneHomeTaskCreateTests*)/*` | V-6, V-13, R-1, R-2, G-6..G-8, G-10, G-11, G-13, G-14, G-18 | all listed, 0 failed; the two G-6 methods and G-13 were red before S3/S5 (record the red run's line) | 41 | 14 |
| CP-3 | S4 | `tests/Antiphon.SessionRunner.Tests -> bin-c628/` | runner-guards | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(ClaudeAuthProbeTests*)\|(PhoneHomeConnectionServiceTests*)/*` | V-1, R-4, R-5, G-9, G-12, G-15, G-16 | all listed, 0 failed; V-1 executed (not skipped) on this desktop | 19 | 8 |
| CP-4 | S6 | `tests/Antiphon.Tests -> bin-c628/` | docs-guards | `/*/*/DockerStackDocumentationTests/*` | G-17 | 8 executed, 0 failed | 8 | 4 |
| CP-5 | S1 | Docker Desktop image from HEAD | dind-local | `pwsh -NoProfile -File scripts/verify-card0604-dind-runner.ps1 -Image antiphon-session-testing:c628-<sha12> -Build` | V-2, V-3, V-4 | `C604 HARNESS EXIT CODE: 0`; result line carries `claude=2.1.280 claudeAuth=logged-out` | n/a | 30 |
| CP-6 | S1-S6 pushed | server2 host daemon: `session-testing` from the branch | server2-runner-redeploy | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case deploy-parent -Manifest <c604Branch=feat/card-task-a6db58bc>` then the two `ssh mc@server2 docker exec -u 1654:1654 -e HOME=/home/app -e CLAUDE_CONFIG_DIR=/state/claude antiphon-runner-session-runner-1 claude ...` probes and `stat -c '%u %a' /state/claude` | V-8, V-9 | 1 case accepted; `2.1.280`; `loggedIn: false` exit 1; owner `1654`; status `dispatchEligible: true` with the branch `buildVersion` | n/a | 35 |
| CP-7 | land | `tests/Antiphon.Tests -> bin-c628/` at the landed sha | epoch-and-guards-recheck | `/*/*/(PhoneHomeEpochAgreementTests*)\|(PhoneHomeTaskDispatchProjectionTests*)/*` | R-1 (post-merge) | 4 executed, 0 failed | 4 | 6 |
| CP-8 | land | production at the landed sha (`git pull --rebase` in `C:\src\Antiphon`, `restart-apphost.ps1`), then `deploy-parent` from master | production-enable | `curl .../api/version`; `curl .../api/session-runners/server2/status` | V-7 | sha = landed; `dispatchEligible: true` within 120 s; runner `buildVersion` = landed sha | n/a | 30 |
| CP-9 | CP-8, before the D-8 login | CP-8 | server2-claude-unprovisioned | `curl .../api/session-runners/server2/provider-auth/claude`; optionally `delegate.ps1 -Runner server2 -Worktree -Kind ClaudeCode -Level Low -Role Custom -Title "c628 refusal" -Goal "Reply OK."` and `GET /api/agent-tasks/{id}` | V-10, V-11 | endpoint `loggedIn: false`; the task `Failed`/`AuthenticationRequired` with `claude auth login` in the reason and no worktree; or the row records "provisioned before measurement" with the endpoint's `true` and rests on G-13 | n/a | 10 |
| CP-10 | CP-9, D-8 login done | CP-8 | server2-claude-roundtrip | `curl .../provider-auth/claude`; `delegate.ps1 -Runner server2 -Worktree -Kind ClaudeCode -Level High -Role Custom -Title "c628 roundtrip" -Goal "Reply with CARD0628_OK_<nonce> and do not use tools. Commit nothing."`; await settlement in bounded `GET /api/agent-tasks/{id}` calls at least 60 s apart; then the V-13 `curl` | V-10b, V-12, V-13 | `loggedIn: true`/`max`; task `Succeeded`; nonce in the runner transcript; mirror removed; RC create 409 | n/a | 40 |

### Cost

Ordinary Code floor (CP-1..CP-6): 12 + 14 + 8 + 4 + 30 + 35 = **103 minutes** of checkpoint time.
Authoring: S1 40, S2 10, S3 90, S4 80, S5 80, S6 40 = 340 minutes. `-ExpectAbout` for the Code
dispatch: about 7.5 hours as a band (6-9 h). Post-land follow-up (CP-7..CP-10): 86 minutes plus the
operator's D-8 steps (about 10 minutes of their time, unbounded wait).

## Risks

- **The task-path projection is new production behaviour for Grok too** (row 2). It is the correct
  behaviour and CARD-0604 designed it, but the first Grok remote task after this lands is also its
  first ever real launch; `EnsureWindowsRulesArgv` on a projected spec is the next wall for Grok
  (noted below). Claude is unaffected.
- **First-run dialogs on `/state/claude`** (row 11). Mitigated by D-8 step 3; if the first
  Antiphon-driven launch still parks, `RunnerClaudeAdapter` names it (`TrustDialogNotCleared` /
  `EffortDialogNotCleared`) and CP-10 records the named block rather than a stall; the fix is a
  seeded `.claude.json` (the `RealCliStubClaudeConfig.SeedOnboarding` shape) written by the operator
  step, not a code change.
- **`claude auth status` behaviour on a path that does not exist.** The desktop measurement used an
  existing empty directory. `init-state.sh` and the harness both create the directory first; G-3a
  and G-4 pin that.
- **Digest drift.** If Anthropic re-publishes 2.1.280 the build fails loudly at `sha256sum -c`;
  the remedy is a new pinned pair, never removing the check.
- **Max-subscription contention** (card decision 2): unmeasured; watched reactively.
- **Pre-land runner redeploy** replaces the live server2 runner with a branch build (D-12). The
  runner has no consumer other than this pipeline; `persistent-restart` semantics (same store id)
  hold because the volume is untouched.

## Not done, noted

- **Grok remote tasks**: `GrokLaunchArgs.EnsureWindowsRulesArgv` (`AgentTaskDispatcher.cs:3832`)
  runs on the projected Linux spec, and `TryFailGrokCredentialProbeAsync` inspects the desktop
  `GROK_HOME` for a runner-bound task (row 8). Neither blocks Claude; both need a card
  ("CARD-0604 S8 follow-up: Grok remote task launch path") once the projection here exposes them.
- **`remote-task-roundtrip` harness case** (CARD-0604 CP-12) still does not exist; CP-10 here uses
  `delegate.ps1` directly with bounded polling. A card for the case is worth filing after CP-10
  proves the shape by hand.
- **CARD-0611**: the desktop is already 2.1.280 (row 3); that card can be closed or re-aimed by the
  operator.
- **A create-time 409 for Claude** (`RefuseUnauthenticatedGrokAsync` parity) is deliberately absent
  (D-7); revisit if the dispatch-time failure proves too late for callers in practice.
