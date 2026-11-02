# Attaching another Windows runner (laptop) for more Windows seats and qualification lanes

Date: 2026-11-02. Base: `0e88f5adba7249c356f3b06e1626a9250525156b`. Read-only investigation on server2;
nothing was run on the desktop or any other host, and no setting was changed.

Operator question (verbatim): *"What would be required to attach another runner? Such as windows
laptop to get some more windows lanes / slots"*

## Short answer

| Part | Works today? | Smallest path to value |
|---|---|---|
| **A. Laptop as an agent-task runner** (more Windows seats) | **No.** The only Windows runner shape is the single desktop HTTP runner at `SessionRunner:BaseUrl`. The second-runner mechanism (phone-home) is validated as Linux-only on both the server and the runner: every path setting must start with `/`. Parts of the runner code already handle Windows. | A Code card: "Windows phone-home runner". It adds platform-aware settings validation and Windows launch projection (`claude.exe`, `codex.cmd`, homes), a laptop install/start script, and an operator-run live acceptance. Rough size: 3–6 Code/Review days in 3 slices. |
| **B. More Windows qualification lanes** | **Partly.** A second Windows host can run its own complete `nightly-run.ps1 -Profile rc` against the same candidate SHA, with its own checkout, state, log and coordination roots, and `-Suites` can pick lanes (for example `e2e,agents-pty`). A partial selection is always `coverageComplete=false`, so it earns **no `rc-release` credit**. Nothing merges two hosts' runs into one credit receipt. | **Now, with no code:** run the long lanes (`e2e`, `agents-pty`, or `antiphon`) on the laptop as a separate rc-profile run, and use the result as operator-accepted evidence, as was done for CARD-1350. **For credit:** a Code card that adds a per-host chunk/suite assignment plus a verified merge of the per-host summaries into one coverage census. Rough size: 1–2 weeks. Sequence it after the in-progress qualification-run-graph plan. |

## A. Attaching a Windows laptop as an agent-task runner

### What exists today

- **One local Windows runner.** The server talks to exactly one HTTP runner:
  `"SessionRunner": { "BaseUrl": "http://localhost:17204" }` (`server/appsettings.json:71-72`).
  The catalogue lists "desktop plus every configured `PhoneHomeRunner:Runners` entry"
  (`docs/ops-http.md:133`). The desktop id is reserved (`server/Application/Settings/PhoneHomeRunnerSettings.cs:342-344`).
  A second HTTP Windows runner (another base URL) was scoped as "Round 4 — desktop, later, separate
  card (about 3–5 days)" and was never built (`docs/investigations/2026-09-25-card-0727-rolling-runner-upgrade.md:133-135`).
- **Remote runners are phone-home runners.** The runner dials the server: an outbound WebSocket
  to `PhoneHome:ServerOrigin`, authenticated by a per-runner `SharedSecret`. The server needs no
  inbound port on the runner (`docker-compose.server2-runner.yml:52-64`; `docs/bootstrap.md:21-27`).
  The server-side map is `PhoneHomeRunner:Runners:<id>` (`PhoneHomeRunnerSettings.cs:145-168`),
  set in the `antiphon-server` user-secrets store. Turning one on is an operator production
  change (`docs/testing-and-build.md:964-990`).
- **Phone-home is Linux-only by validation, on both sides:**
  - Server: `RunnerWorkspace`, `RunnerRepository`, `RawExeAllowList`, `ChildGrokHome`,
    `ChildClaudeHome` and `ChildCodexHome` must be "a POSIX absolute path" (`PhoneHomeRunnerSettings.cs:355-371`).
    Defaults are `/work`, `/work/repos/antiphon`, `/bin/sh`, `/state/claude` and similar (`:157-164`).
  - Runner: `PhoneHomeSettings.Validate()` throws unless `AllowedCwd`, `RunnerRepository`,
    `ClaudeHome`, `CodexHome` and `RawExeAllowList` start with `/` (`src/Antiphon.SessionRunner/PhoneHomeSettings.cs:124-146`).
    Defaults are `/run/secrets/phone-home`, `/state/runner-store-id` and `/state/runner-capacity` (`:10-11`, `:90`).
  - Launch projection rewrites a desktop spec onto the runner. It maps `grok.exe` to `grok`,
    `claude.exe` to `claude`, and `codex.cmd`/`codex.exe` to `codex` (described as "the image's
    native /usr/local/bin/codex"). It sets `GROK_HOME`, `CLAUDE_CONFIG_DIR` and `CODEX_HOME` from
    the entry's POSIX homes and forces `Backend = PtyHost`, `Herdr = null`
    (`server/Application/Services/PhoneHomeLaunchPolicy.cs:245-275`, `:279-307`).
- **Parts of the runner already handle Windows:**
  - Registration reports `linux` or `windows` from the OS (`src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:123`).
  - Cwd admission has a Windows branch with folded separators, folded case, and `:` and trailing-dot
    refusals (`src/Antiphon.SessionRunner/RunnerWorkspaceCwd.cs:6-28`; witness commit `b8188681a`, 2026-10-25).
  - Git null-device handling chooses `NUL` on Windows (`RunnerWorkspaceService.cs:984-991`).
  - Workspace paths are built by string concatenation with `/` (`RunnerWorkspaceService.cs:80-82`,
    `RunnerWorkspaceSlotPool.cs:55`). Windows APIs accept `/`, but this has not been tested on Windows.
- **Placement already handles a Windows remote runner.** For a `-Platform Windows` task with no
  `-Runner`, the order is: kind default, then global default, then the desktop if it is
  dispatch-eligible, then each known remote runner whose platform is `windows` and which
  advertises `required-platform-v1` (`server/Application/Services/AgentTaskService.cs:2040-2140`).
  The desktop always wins while it is eligible. Eligibility is a health test, not free seats
  (`:2108-2117`). So a Windows laptop runner would get Windows work only in three cases: an
  explicit `delegate.ps1 -Runner <laptop-id>`, a per-kind or global runner default naming it, or
  the desktop being ineligible.
- **Runner-bound task limits** apply to any phone-home runner. Only Worktree tasks with Grok,
  Claude Code or Codex are allowed. These are refused: card-backed starts, OnAgent, Shared,
  ReadOnly, pins and SourceLanding (`docs/ops-http.md:146`; `docs/testing-and-build.md:994-1003`).
  Landing, the land verifier and SourceLanding Mutation stay on the desktop.

### What the laptop would need (once the code supports it)

| Need | Detail and source |
|---|---|
| OS and toolchain | Windows 10/11 with modern ConPTY, the same as the desktop runner's `PtyBackend: modern` (`src/Antiphon.SessionRunner/appsettings.json:4`). It also needs the .NET SDK pinned by `global.json` (**10.0.204**, `rollForward: latestMinor`; `global.json:2-4`), Node 20+, pwsh 7 through the version-independent `WindowsApps\pwsh.exe` alias, and git (`docs/bootstrap.md:176-183`). |
| Docker Desktop | Not needed by the runner process. It **is** needed by delegated work that runs tests: `Antiphon.Tests`, `Antiphon.Messaging.Tests`, `Antiphon.Observability.Tests` and `Antiphon.E2E` reference Testcontainers (their `.csproj` files). Without Docker, Code and Review tasks on the laptop cannot run most checkpoints. |
| CLIs and their auth | `claude.exe` (and `grok.exe` / `codex`, if those kinds are wanted) on PATH. Each is logged in **on the laptop**, under the runner's own config home. The server refuses credential env vars on a runner-bound launch (`PhoneHomeLaunchPolicy.cs:225-243`), so desktop credentials are never forwarded. The server2 pattern is to create a `claude setup-token` OAuth token on the desktop and store it in a vault item, with an interactive `claude auth login` into the runner's `CLAUDE_CONFIG_DIR` as fallback (`docs/agent-credentials.md:386-400`). For the laptop, the operator would create its own token or login the same way. Nothing is copied from the desktop's own login. Codex uses `codex login --device-auth` into the runner's `CODEX_HOME` (`docs/testing-and-build.md:1022-1027`). |
| Phone-home secret | A per-runner `SharedSecret` (server2 uses `openssl rand -hex 32`), kept in an owner-only file on the laptop that `PhoneHome:SecretPath` points to. The operator also enters it into the server's `antiphon-server` user-secrets under `PhoneHomeRunner:Runners:<laptop>:SharedSecret` (`docs/agent-credentials.md:316`, `:326-331`). Use a new value; server2's custody file is not reused. |
| Git access | The server pushes the desktop task branch to origin. The runner then mirrors it from GitHub: an anonymous HTTPS fetch of `RunnerCloneSource` into `RunnerRepository`, plus a worktree per task (`docs/docker-stack.md:1152`; `docs/testing-and-build.md:994-1000`). The agent's own pushes need write access. Server2 uses a repo-scoped write deploy key over `ssh.github.com:443` for the Antiphon primary, and a PAT credential helper for other admitted repositories (`docs/agent-credentials.md:312-318`, `:337-351`). The laptop needs its own deploy key (a new GitHub deploy-key entry) or a scoped token in its git credential store. |
| Repo layout on the laptop | No desktop path is needed on the laptop. The desktop worktree under `C:\Antiphon\worktrees` stays canonical. The laptop has its own runner repository and `worktrees/` under its `AllowedCwd`, for example `C:\AntiphonRunner\work\repos\antiphon` and `...\work\worktrees\<task>`, once Windows paths are accepted. On the server entry, `HostWorkspaceRoot` stays `C:\src\Antiphon`, the desktop root, as it is for server2 (`docs/testing-and-build.md:975-977`). The convention directory `C:\logs\antiphon\session-runner` is still needed, because the runner's `SessionLogPath` default is `C:\logs\antiphon\session-runner` (`docs/bootstrap.md:186-200`). |
| Network | The laptop needs outbound access to two addresses: the server origin for phone-home (`PhoneHome:ServerOrigin`), and `CallbackOrigin` (projected as `ANTIPHON_API`) for agent callbacks (`PhoneHomeLaunchPolicy.cs:245-251`). Production uses `CallbackOrigin=https://antiphon.desktop.codeperf.net`. Tailscale `100.79.51.37` / MagicDNS `desktop-ktlkpif` is the in-tailnet alternative (`docs/bootstrap.md:24-27`). No inbound firewall rule is needed on the laptop, and nothing on the desktop changes except user-secrets. |
| Runner process start | No generic Windows phone-home start script exists. On the desktop, the "Antiphon Session Runner" Scheduled Task runs `autostart-session-runner.ps1` and then `run-daemon.ps1`. That path hard-codes `--urls http://localhost:17204` (`scripts/autostart-session-runner.ps1:51`), and `install-autostart.ps1` also registers the AppHost tasks (`docs/bootstrap.md:576-584`). `restart-session-runner.ps1` stops the runner at this checkout's `bin\Debug\net9.0` path (`docs/bootstrap.md:73-74`) and is written for the desktop. A laptop needs a runner-only install/start script: one scheduled task, `PhoneHome__*` env, no AppHost and no watchdog. |
| Registration and visibility | The runner registers with id, platform, capacity and capabilities (`PhoneHomeConnectionService.cs:118-127`). `GET /api/session-runners` then shows it, and `/status` must reach `dispatchEligible: true` (`docs/ops-http.md:144`). `/provider-auth/claude` probes the laptop's login (`docs/ops-http.md:147`). |
| Seats and limits | The laptop declares `PhoneHome:Capacity` (default **1**; `PhoneHomeSettings.cs:87`). The server's `MaxCapacity` bound refuses a higher value (default 10; `PhoneHomeRunnerSettings.cs:160`). The **operator** changes it with `PUT /api/session-runners/<id>/capacity` and caps dispatch with `PUT /api/hosts/<id>/budget`. An orchestrator only reads these (`docs/ops-http.md:135-136`; `docs/orchestration-loop.md:1063-1072`). |
| Build slots | Each host has its own broker. Every runner process serves `/build-slots` (`src/Antiphon.SessionRunner/Program.cs:138`, `:230`). On Windows, `build-slot.ps1` defaults to `http://localhost:17204/build-slots` unless `ANTIPHON_BUILD_SLOTS_URL` is set (`docs/testing-and-build.md:632`, `:1129`). The laptop runner should listen on `localhost:17204` locally, or set that env var for its children. Either way it brokers only the laptop's own budget. |

### What is missing (the code)

1. **Platform-aware phone-home settings.** On Windows, accept and default to fully qualified
   Windows paths: `PhoneHomeSettings.Validate` (runner) and `PhoneHomeRunnerSettingsRules.ValidateMapped`
   (server, with the per-entry platform declared or taken from registration). Keep the Linux rules
   unchanged.
2. **Windows launch projection.** `ProjectExe` returns bare `claude`/`codex`. That is fine for
   `claude.exe` on PATH. It is not fine for the npm `codex.cmd` shim, which CreateProcess will not
   find by bare name. `RawExeAllowList` and the homes need Windows values.
3. **A laptop install/start script** and docs (see above). The script must stay ASCII-only for
   Windows PowerShell 5.1, per AGENTS.md.
4. **Operator live acceptance**: secret, user-secrets entry, AppHost restart, `dispatchEligible`,
   a `-Runner <laptop>` Worker task end to end, with settlement fast-forwarding the desktop worktree.

Not verified, and needs a laptop smoke: the POSIX validators are plain `StartsWith('/')` checks,
and on Windows `/work` is a drive-rooted path (`C:\work`). An all-`/…` configuration might
already pass validation on Windows. That proves nothing about projection, git and auth, so the
Plan should not rely on it.

### Risks: desktop assumptions with two Windows hosts

- `C:\Antiphon\…`, `C:\logs\antiphon\…` and `C:\src\Antiphon` defaults are per-machine. They do not
  collide across hosts, but the server's `HostWorkspaceRoot` must stay the **desktop** root.
- Ports 17202, 17203 and 17205 are only for the AppHost and dashboard; the laptop runs none of them.
  17204 on the laptop is local only. `ProductionRunnerGuard` and the E2E random-port guard key on
  localhost:17204 of the machine running the tests (`docs/release-gates.md:363-366`). On the laptop
  that is the laptop's production runner, so tests there are protected the same way.
- `-Platform Windows` keeps choosing the desktop while it is eligible. Laptop seats stay idle
  unless runner defaults or an explicit `-Runner` send work there. That is an operator routing
  decision (`PUT /api/runner-defaults`; `docs/orchestration-loop.md:1095-1108`).
- Laptop sleep or a network drop makes the runner ineligible. After `Alarms:RunnerGraceSeconds`
  it raises `RunnerUnavailable` (`docs/orchestration-loop.md:419`). Queued work holds; running
  sessions survive only as long as the laptop is up.
- Credential custody: one more host holds a GitHub write credential and a Max-subscription token.
  Quota is shared with the desktop and server2 when it is the same account.

### Order of steps and who does what

1. **Operator decision:** do we want a Windows phone-home runner? It is the only additive path;
   the alternative is the unbuilt CARD-0727 Round 4 second HTTP runner.
2. **Plan, then Code and Review** (Linux-portable unit tests; one Windows-pinned task for the projection witnesses).
3. **Operator:** provision the laptop. That covers the toolchain, Docker Desktop, CLI logins, a
   deploy key or token, the shared secret file, and the server user-secrets entry. Then restart
   the AppHost from the canonical checkout, verify `/api/version`, and run the acceptance.
4. **Operator:** set capacity, host budget and runner defaults. After that the orchestrator places
   `-Platform Windows` work with `-Runner <laptop>` when the desktop is full.

## B. More Windows qualification lanes

### How a qualification run executes today

- One entrypoint chain: `release-cut.ps1` / `nightly-run.ps1`, then `nightly-tests.ps1`. The
  defaults are `CheckoutRoot C:\Antiphon\nightly\checkout`, `StateRoot C:\Antiphon\nightly`,
  `ReleaseRoot C:\Antiphon\releases` and `CoordinationRoot C:\Antiphon\verification`
  (`scripts/nightly-run.ps1:86-90`; `scripts/release-cut.ps1:22-23`; `scripts/lib/release-gate.ps1:18-19`).
  Every root is a parameter, and the documented example uses private scratch roots
  (`scripts/nightly-run.ps1:73-80`).
- **One native lock per host.** `native-run.lock` lives under `CoordinationRoot`. Its owner identity
  is PID, process start, run id and continuation id. A live owner is never stolen, and a busy lock
  gives `deferred-busy` (`docs/release-gates.md:100-112`; `scripts/lib/release-gate.ps1:385-415`).
  The lock is a local file keyed on local PIDs, so it does **not** exclude across machines.
- **Chunks run strictly serially.** The dispatch coordinator prepares each suite, then calls
  `Invoke-NightlyScheduledChunk` once per chunk in a single `foreach`
  (`scripts/lib/nightly-dispatch.ps1:200`, `:243-247`). `dispatch-schedule.json` holds one
  permutation of every `(suite, chunk)` (`:94-153`). The order is failed-first, then unfinished,
  then passed, with a seed carried from a previous run (`:22-86`). `groups-planned.json` is the
  per-phase plan (`scripts/lib/nightly-tests-impl.ps1:1792`). The skill records that "Windows
  chunks run strictly serially under one native lock" (`.claude/skills/antiphon-rc-qualification/SKILL.md:44`).
- **Lanes are suites.** `profiles.rc.requiredSuites` has ten entries: `antiphon`, `session-runner`,
  `pty-host`, `agents-pty`, `messaging`, `client`, `scripts`, `e2e`, `markdown-pdf`, `observability`
  (`tests/test-execution-policy.json:29-40`). `-Suites` selects a subset, and `-SelectionMode` is
  `Full|Shadow|ChangeAware` (`scripts/nightly-run.ps1:83`, `:91`). There is **no `-Lane` parameter**.
- **A subset is never credit.** `Test-NightlyRequiredSuitesPresent` sets `coverageComplete = $false`
  with `partial-selection missing …` whenever a required suite is not selected
  (`scripts/lib/nightly-tests-impl.ps1:1474-1497`). `rc-release` credit requires real-boolean
  `coverageComplete` from one run (`docs/release-gates.md:75-85`). The scoped-lanes plan in
  progress says the same: "Two physical hosts can execute only genuinely independent, separately
  bound qualifications" (`docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md:256`,
  on `feat/card-task-da0479a5` @ `f11f05c0`).

### Shared state if a second host takes part

| State | Shared today? | Second host would need |
|---|---|---|
| Candidate source | Each run owns its checkout at `-ExpectedSha` | Its own clone at the **same full SHA** (`nightly-run.ps1` refuses a HEAD mismatch; `:51-55`) |
| `native-run.lock` / CoordinationRoot | Host-local file | Its own lock on its own disk. That is safe, because the lock serialises only one host's native work |
| State/log/TRX roots, `summary.json`, `last-run.json` | Per run directory | Its own roots. Results arrive as a **second, separate** run |
| Seed / prior failures (`-PriorFailuresPath`, `export-nightly-stopped-run-seed.ps1`) | Per run | A seed filtered to the suites the host owns. The cross-candidate seed needs `-AcceptCrossCandidateSeed` |
| Credit receipt (`complete-green.json`) | One run → one verdict | **No merge exists.** It needs a new multi-run coverage census |

### What would have to be built for split-host credit

1. **A per-host suite/chunk assignment.** Each host runs a declared disjoint subset of
   `(suite, chunk)`, still serial within its host. Assigning by suite is simpler than by chunk,
   because chunk ids come from each host's own discovery and packing.
2. **A merge and verify step.** It takes N run directories at one SHA and checks that each has the
   same policy hash and the same discovery census digest (`Get-NightlyDiscoveryCensus`). The
   per-host executed UIDs must form a disjoint union equal to the required set, with each TRX
   verified. The step then writes one credit verdict through `Get-ReleaseGateCreditVerdict`
   unchanged.
3. **Host identity in receipts.** Each part carries OS, runtime and host enrollment, so a Linux or
   foreign run cannot be counted as Windows.

Rough effort is 1–2 weeks of Plan, Code and Review. It overlaps the in-progress qualification
run-graph plan (`docs/superpowers/plans/2026-10-31-qualification-run-graph-plan.md`, on its
owning Plan branch per the skill: `.claude/skills/antiphon-rc-qualification/SKILL.md:204-207`).
Sequence it after that plan, or fold it in.

### Smaller alternatives, available now with no code

- **An independent laptop lane run.** Run
  `nightly-run.ps1 -Profile rc -Trigger rc -Ref rc/<line> -ExpectedSha <sha> -Suites e2e,agents-pty -NoReport`
  with private `-CheckoutRoot`, `-StateRoot`, `-LogRoot`, `-ReleaseRoot` and `-CoordinationRoot`
  on the laptop, in the form of the script's own example (`scripts/nightly-run.ps1:73-80`).
  `antiphon` alone (about 12,000 methods, serialised by `ProcessSpawnLimit`) is the other
  candidate (`docs/release-gates.md:386-392`). The run is red or incomplete (`coverageComplete=false`)
  and earns no credit. The operator can accept its TRX as evidence, as with CARD-1350's narrower
  standard (`docs/release-gates.md:373-395`).
- **The E2E lane needs Docker, npm and Playwright Chromium on the laptop.** A missing browser or
  Docker is red, never a skip (`docs/release-gates.md:357-361`).
- **Per-fix native reruns on the laptop**, while the desktop's Full run holds its lock. The skill
  today waits for the desktop lock (`SKILL.md:117`, `:195`).

## Not done, noted

- Fix idea (A): add a declared `Platform` on `PhoneHomeRunnerEntry`. Validate and default paths per platform on both sides, and add a Windows `ProjectExe` branch.
- Not run: any live laptop smoke. Nothing confirms whether all-`/…` settings pass on Windows.
- Uncertain: whether Windows `PtyHost`/custody under phone-home needs more than the desktop
  runner already has. The runner advertises its custody backend at registration
  (`PhoneHomeConnectionService.cs:124-127`). A Windows phone-home runner was never registered.

## Draft Plan brief (A)

> Plan: Windows phone-home runner (laptop). Goal: a Windows machine can register as a
> `PhoneHomeRunner:Runners:<id>` delegated-task runner beside server2, so `-Runner <id>`
> Worktree tasks (Claude Code first, then Grok and Codex) run there with Windows paths.
> Scope:
> - S1: platform-aware validation and defaults in `PhoneHomeSettings.Validate` and
>   `PhoneHomeRunnerSettingsRules`, with Linux unchanged and pinned by existing tests.
> - S2: Windows launch projection (`claude.exe`, `codex.cmd`, homes, raw allow list) and
>   workspace path joins, with Windows-pinned witnesses.
> - S3: an ASCII-only runner-only install/start script (scheduled task, env, secret file ACL,
>   `localhost:17204` broker) plus a docs section in `docs/bootstrap.md` and
>   `docs/agent-credentials.md`.
> - S4: operator acceptance checklist. Out of scope: a second HTTP runner (CARD-0727 Round 4),
>   SourceLanding/land on the laptop, and changing `-Platform Windows` placement order.
> Evidence: `docs/investigations/2026-11-02-attaching-another-windows-runner.md`.
