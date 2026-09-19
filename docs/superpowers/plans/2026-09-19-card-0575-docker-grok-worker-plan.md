# CARD-0575: desktop Docker Grok worker v0

Date: 2026-09-19. Stage: Plan. Task: `ba318e38`. Source tree examined: `bb7b75353f76ca940265e5ef77bb64447100b027`.

## Outcome and scope

Add an opt-in local execution target for a named Grok worker. The existing Windows session-runner and PtyHost launch `docker.exe exec -i -t` into a dedicated, pre-authenticated Linux container. Antiphon continues to own dispatch, input, transcript ingestion, queued delivery, report settlement and session generation. The container reaches the desktop API at `http://host.docker.internal:17202` and uses the desktop Docker engine through `/var/run/docker.sock`.

This is a dispatchable local worker, not a new remotely registering runner. CARD-0490 owns remote phone-home, remote enrollment and broader lifecycle protocols. CARD-0038 owns an in-container Linux runner and POSIX PTY portability; neither is a prerequisite for this Windows-hosted v0.

The commissioned result is this design and its guards/PC candidates. TestDesign is **separate**, not folded into this dispatch. Next stage is `test-design`; the matrix below is input to that stage, not a claim that verification has been executed or fully commissioned. No implementation, Docker operations, login, stack restart or model turn was performed for this plan.

## Evidence and ground truth

The caller's refinement was received and followed: fetch/read the investigation if useful, do not merge its branch. The full investigation was read at immutable commit `56d5ad47d715ba6e5bfa305163b931362202d6d6` on `feat/card-task-4fe68cac`, without merging it. Its live measurements are inherited evidence, not measurements repeated by this Plan task: [CARD-0575 investigation](https://github.com/michal-ciechan/Antiphon/blob/56d5ad47d715ba6e5bfa305163b931362202d6d6/docs/investigations/2026-09-19-card-0575-docker-grok-worker.md).

| Card assumption / desired behavior | What the investigation or current code actually establishes | Design consequence |
|---|---|---|
| The desktop public URL is the API. | Investigation: Caddy's `antiphon.desktop.codeperf.net` targets client 17203; `/health` is SPA HTML, and `/api/*` relied on the Vite proxy. Container -> `host.docker.internal:17202` returned API health and JSON. | Use direct host API origin, test response shape as well as status. Do not edit Caddy. |
| Setting container `ANTIPHON_API` once will suffice. | `AgentTaskDispatcher.BuildEnv` and `AgentSessionLaunchComposer.ComposeForAgentAsync` inject `DelegationSettings.ApiBaseUrl`, normally localhost. `AgentLaunchEnv` reserves `ANTIPHON_*`; `DelegationReportFormatter.BuildBriefPointer` also embeds the configured host URL or a host spill path. | Resolve a target-specific API origin in the trusted composition layer; map brief/refinement pointers too. Leave the installation's host origin unchanged. |
| A Grok definition can run unchanged in Linux. | `server/appsettings.json` names `grok.exe`. Investigation measured Linux ELF `grok 1.0.34`, and `docker exec -t` provided `/dev/pts/0`; no Docker launcher exists in SessionRunner. | Explicit typed execution target and runner wrapper; retain `AgentKind.Grok` and its adapter. |
| Linux PTY work blocks the local proof. | Investigation confirms a Windows ConPTY child can be Docker CLI; Linux runner portability remains incomplete. | Keep both .NET runner and PtyHost on Windows. Qualify the two PTY hops with real input. |
| Persistent auth is available in any container. | Only an external `GROK_HOME` bind/volume survives replacement; a new unmounted home lacks auth. Windows credential/native-session probes read the local filesystem. | Choose a host bind for v0 so the same bytes are visible to existing probes and tailer. No credential image layer or automatic credential copy. |
| Host cwd and child cwd are interchangeable. | `GrokTranscriptTailer.ResolveUpdatesPath` calls Windows `Path.GetFullPath(cwd)` then URL-encodes it. Linux writes under the encoded Linux cwd. `StartTailerFor` binds this path before the file exists. | Carry separate host and Linux cwd; construct the host transcript path using the exact Linux cwd string. |
| Existing rules files just work across the mount. | `GrokRulesFileStore` writes under runner `SessionLogPath/instructions/grok/<session>/rules.md`; `GrokRulesRefreshService` tells Grok to read `receipt.Path`. `ValidateReceiptPath` already accepts POSIX grammar. | Store on a dedicated host rules root, expose a Linux receipt path, map it back internally for verification/adoption. Preserve read acknowledgement. |
| A managed profile is the worker registry. | `Agent` already represents a named worker. `ResolveDelegateProgramAsync` honors pinned standing agents and profiles; pool delegates follow registry definitions. `delegate.ps1 -Agent` pins a standing worker. | Register a named, profile-less Grok agent plus a typed Docker target ID; no new worker table, `AgentKind`, fleet scheduler or registration endpoint. |
| A wrapper profile has equivalent credential checks. | The dispatch fail-fast credential probe currently runs only for registry-path Grok, not profiles. | Keep v0 on the registry path and bind its host `GROK_HOME` before probing. Do not exploit the profile exclusion to skip auth checks. |
| A profile-less named Grok agent always starts Grok. | Delegated tasks select by kind, but `AgentSessionLaunchComposer.PeekProfileKindAsync` normally falls through to the default profile/definition, and `AgentControlService` uses `AgentLaunchResolution`. | Target-aware manual/resume composition must select Grok explicitly; include these funnels in S2 and test them with a Claude installation default. |
| Existing worktree paths are portable. | Dispatcher creates host Git worktrees and sends their Windows cwd. No Docker path/mount mapping exists. A Windows-linked `.git` pointer must not be assumed readable by Linux Git. | v0 uses one dedicated standalone checkout with a `.git` directory; refuse Worktree/SourceLanding modes. Do not rewrite the canonical repository's Git metadata. |
| Docker client's Windows process ownership covers Grok. | `RunnerSession.StartAsync` owns a Windows PtyHost/child and Windows job; the Linux exec process belongs to the Docker daemon. Current generation/custody metadata does not describe it. | Add durable container binding, exclusive occupancy, restart recovery and container-side stop. Never use Windows job receipts as Linux descendant proof. |
| Docker-in-Docker is necessary. | Investigation: host-socket `docker info` and `hello-world` worked. Publishing 17280 failed because live `antiphon-postgres` already owns it. | Retain host-socket Option A. No DinD daemon, `--privileged`, shared DB changes or fixed port in the worker definition. |
| Version/TTY/health are full acceptance. | Investigation did not perform an authenticated, Antiphon-visible turn or establish ConPTY -> Docker -> Grok delivery and Linux transcript binding. | Those are mandatory qualification gates, not assumed capabilities. |

Owners read for this design: `docs/project-context.md`, `docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, `docs/agent-kinds.md`, `docs/ai-agent-tui-configuration.md`, `docs/agent-credentials.md`, relevant `docs/session-runtime-invariants.md`, `docs/adr/0002-modern-conpty-backend.md`, `docs/testing-and-build.md` and Docker/port sections of `docs/bootstrap.md`.

## Decisions

These are the selected implementation decisions, with no outstanding operator choice blocking TestDesign. Host paths and provisioned IDs below are examples, supplied at installation; they are not hard-coded deployment facts.

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | Keep Windows runner + PtyHost; add `dockerGrokExecV1` capability and typed launch binding. This preserves the existing Grok adapter and delivery pipeline. | Linux runner/ACP migration: different scope and portability/protocol work. Raw kind: loses Grok transcript/settlement semantics. |
| D-2 | One named standing Grok agent, one dedicated container, one occupied session generation. Dispatch through an explicit `-Agent` pin. No anonymous pool adoption. | Change the global `grok` definition to Docker: silently changes all existing workers and mixes warm host/container sessions. |
| D-3 | Add nullable `DockerWorkerId` to `Agent` and immutable target snapshot on `AgentSession`. v0 requires Grok, PtyHost and no TUI profile. Existing null values preserve host behavior. | Infer Docker from an executable string or arbitrary env variable: ambiguous on resume, bypassable through overlays, and hard to validate before side effects. Full TUI-profile transport editor is deferred. |
| D-4 | Use a small Debian bookworm-slim Linux/amd64 image with Grok 1.0.34, Docker CLI + Compose plugin, Git, bash, curl, CA certificates, coreutils and an init process. Pin base/binary inputs and verify Grok version/hash in the build. | Alpine is measured to run `--version` but offers less familiar tooling; the existing root Dockerfile publishes the server, not this worker. An SDK/AppHost image or floating latest installer increases scope/drift. |
| D-5 | Bind a dedicated writable host `GROK_HOME` to `/var/lib/grok`; install executable at `/opt/grok/bin/grok`, outside that mount. Login happens interactively in that home. | Named volume persists but Windows cannot directly probe/tail it. Baking/copying auth or silently falling back to metered API-key auth violates custody/billing expectations. |
| D-6 | Bind a dedicated standalone host checkout to `/work`, and a dedicated rules root read-only to `/antiphon`. Supported dispatch modes are Shared and ReadOnly; both are existing logical modes, not a new filesystem sandbox. | Host-linked Worktree, arbitrary directories and automatic checkout synchronization: not necessary for the local proof and need a separate Git/path design. |
| D-7 | A single operator-owned, non-secret worker manifest is loaded by both server and runner through typed settings; a canonical configuration digest must agree. Manifest supplies exact Docker context, container identity label/name, host/container roots and API origin. | Independent duplicated defaults: can probe one home while launching in another. Sending arbitrary mounts/commands from a task is not a worker registry. |
| D-8 | Set the worker API origin in trusted server composition after user env merge; repeat the binding check at runner launch. Forward orchestration identity as child env, never in argv values or image ENV. | Global `Delegation:ApiBaseUrl` change, client Caddy origin or task `EnvOverride`: affects host sessions or violates reserved identity precedence. |
| D-9 | Generate argv as a token list. The host wrapper is a C# runner launch planner, not `cmd /c`, PowerShell string assembly or `sh -c` around task text. | Shell interpolation of cwd, env and Grok arguments makes quoting and secret handling unnecessarily fragile. |
| D-10 | Keep logical Grok args/env separate from physical Docker process args/env. Rules receipts expose the Linux read path; host persistence derives the physical path from the immutable binding. | Rewrite arbitrary strings globally, encode Windows cwd as Linux cwd, or put the complete rules text on Docker argv: breaks existing contracts. |
| D-11 | Exclusive container ownership allows explicit stop to stop the whole exact container before releasing occupancy. Recovery adopts only a matching generation, container ID and persisted mapping. | Killing only docker.exe, assuming exec disconnect ends descendants, killing by mutable container name, or silently launching a second exec after a crash. |
| D-12 | Keep host-socket Option A, no published worker ports, no extra daemon. It is a trusted local automation target with host-engine authority, not a containment boundary. | DinD to avoid 17280 collision or claiming untrusted-code isolation. Socket-created sibling containers are separate resources, not descendants covered by stopping the worker. |
| D-13 | Qualify behind disabled-by-default config before enabling the registered worker. Keep ordinary Review and post-land SourceLanding PCs separate. Docker-backed SourceLanding execution is explicitly unsupported in v0. | Treating version/health/fake transcripts as live qualification, or issuing native custody receipts for a Docker CLI job. |

## Target shape and registration

Proposed non-secret manifest schema (one file, selected through `DockerGrokWorkers:ManifestPath` on server and runner):

```json
{
  "schemaVersion": 1,
  "workers": {
    "desktop-grok-v0": {
      "enabled": false,
      "dockerContext": "desktop-linux",
      "containerName": "antiphon-grok-worker-v0",
      "ownerLabel": "antiphon.docker-worker=desktop-grok-v0",
      "image": "antiphon/grok-worker:1.0.34-v0",
      "hostGrokHome": "C:\\Antiphon\\docker-workers\\desktop-grok-v0\\grok-home",
      "containerGrokHome": "/var/lib/grok",
      "hostWorkspaceRoot": "C:\\Antiphon\\docker-workers\\desktop-grok-v0\\work",
      "containerWorkspaceRoot": "/work",
      "hostRulesRoot": "C:\\Antiphon\\docker-workers\\desktop-grok-v0\\rules",
      "containerRulesRoot": "/antiphon",
      "apiBaseUrl": "http://host.docker.internal:17202"
    }
  }
}
```

The deployed manifest also records the **resolved image digest**; tag alone is not the identity used by preflight. No auth bytes, tokens or user-specific live paths are committed. A sample file is tracked; the populated file stays outside the checkout. Validate distinct roots, absolute paths, no ambiguous case-equivalent env names, normalized Linux paths, and no path escape/reparse indirection. The worker ID has a bounded slug grammar. Host paths retain Windows backslashes. Changing a mapping invalidates reuse and requires a stopped/fresh session; it must not redirect a live transcript.

`scripts/register-docker-grok-worker.ps1` will use existing agent CRUD with the new optional field. It is idempotent only against the exact matching named agent; refuse name/target collisions. Register `Kind=Grok`, `SessionBackend=PtyHost`, `TuiProfileId=null`, `DockerWorkerId=desktop-grok-v0`, `IsPoolDelegate=false`, `AlwaysOn=false`, `RemoteControlEnabled=false`, `AssignmentPolicy=ManualConfirm`, and the dedicated Windows working directory. Keep it boardless unless explicitly assigned later. v0 task validation requires a named pin; card auto-pick, orchestrator, pool, Herdr, managed-profile, remote-runner and SourceLanding requests refuse rather than fall back.

Add a read-only runner `GET /docker-workers/{id}` descriptor (on the runner, no `/api` prefix) through `ISessionRunnerClient`: target ID, enabled flag, canonical config digest and bounded availability/occupancy state. It exposes no env or credential contents and creates/starts nothing. Registration and server preflight use this to compare manifests. This is local configuration discovery, not remote worker enrollment. Keep the normal runner trust boundary; do not publish another container port.

The operator builds/provisions the container, logs in against its mounted home, completes the isolated checks, enables the manifest entry, and registers the agent. Registration verifies the runner's capability and manifest digest; it does not auto-login or spend a model turn. Example qualification dispatch, from the host's existing authorized caller:

```powershell
$canaryBrief = Get-Content -LiteralPath '<prepared canary brief file>' -Raw
& .\scripts\delegate.ps1 -Agent desktop-grok-v0 -Kind Grok -Role Custom -Shared -Dir 'C:\Antiphon\docker-workers\desktop-grok-v0\work' -Goal $canaryBrief -NoCommit
```

`-NoCommit` is specific to the disposable canary. Real Shared tasks retain existing settlement behavior. The checkout must be a standalone clone with a `.git` directory, no Windows-only `core.worktree`, hooks or credential-helper assumptions needed for the canary. Verify Git can resolve HEAD on both sides. Existing caller/project/working-directory authorization still applies: provisioning a Docker target does not grant a caller access to a new checkout or widen `Delegation:AllowedRoots`. Publishing Git credentials, installing a full build SDK and running Antiphon's Windows-only suite inside Linux are outside this proof.

## Image and container wiring

Add `docker/grok-worker/Dockerfile`, a narrow `.dockerignore`, and a worker-only Compose file in that directory. Do not alter the server Dockerfile or `docker-compose.dev.yml`.

Use a build stage to obtain the official Linux/amd64 Grok artifact. The investigation measured the official `https://x.ai/cli/install.sh` installation, but did not establish an immutable version-download URL. Code must record the actual official artifact URL plus SHA-256, or use an operator-supplied verified artifact as the build input; it must not invent a versioned URL. Commit a checksum/build manifest, require the checksum at build time, and fail if `grok --version` is not the qualified 1.0.34. Pin the Debian and Docker CLI source image/package versions/digests during implementation. Copy only the binary and required tools into the final image, never a real home. Its runtime `GROK_HOME=/var/lib/grok` must not hide the installed executable.

Container configuration:

| Item | v0 setting |
|---|---|
| Main process | Init plus an idle foreground process; Grok is launched by exec, not at container boot. No runner/service inside. |
| Lifecycle | One target-owned container, `restart: "no"`; runner may start the verified existing stopped container at a new admitted launch. Provision/rebuild/replacement is an explicit provisioning operation. |
| Identity | Stable worker label plus unique instance label; record resolved full container ID and image digest on admission. Never adopt another container merely because its name matches. |
| Home | Dedicated host directory -> `/var/lib/grok`, read/write, including OAuth refreshes and sessions. |
| Workspace | Dedicated standalone checkout -> `/work`, read/write. Per-task cwd must be this root or an allowed descendant. |
| Rules | Dedicated host rules root -> `/antiphon`, read-only from the container. |
| Docker | `/var/run/docker.sock` -> same path; Docker CLI and Compose plugin only, no daemon. Root user is the v0 operational choice for socket access, consistent with accepted host-engine authority. |
| Networking | Normal Docker Desktop networking, no `ports`, no privileged mode. API origin is host 17202. |
| Provisioning | Refuse missing bind sources instead of silently creating a new empty auth mount; never remove volumes/homes on down/rebuild. |

Bind mounts are appropriate for host-visible generated files, and Docker Desktop handles sharing native host paths into its Linux VM. A mount can obscure files baked into its destination, which is why executable and home have different paths. [Docker bind-mount documentation](https://docs.docker.com/engine/storage/bind-mounts/)

Login is an operator action (`docker --context <configured-context> exec -it <verified-id> /opt/grok/bin/grok login`) after provisioning, with the same `GROK_HOME`. Do not read/print the auth file, seed it from the user's default home, write it through Antiphon, include it in build context, or hash real auth contents in evidence. Preserve the directory through stop/start, replacement and image rebuild. A laptop has neither this bind path nor this Docker Desktop volume; remote auth transfer is not implied.

Host-socket commands create sibling containers on the desktop engine. Their bind sources are interpreted by that daemon, not relative to `/work` inside the worker. v0's socket canary uses no bind-mounted siblings: `docker info` and a uniquely named/labeled foreground `docker run --rm hello-world`. A task needing sibling bind mounts must supply an explicitly validated daemon-visible path in a later extension. Do not run repository dev Compose unchanged from the worker, reserve or steal 17280, stop the existing Postgres, or use `down -v`/prune. Future test services use a unique Compose project and dynamic ports or no publication; lack of capacity is a refusal, not permission for DinD.

## Launch and data flow

1. **Select and validate.** Task creation resolves the explicitly pinned agent; a concrete `DockerGrokWorkerResolver` validates the kind/backend/profile/workspace restrictions. Resolve the shared manifest, set the host-side `GROK_HOME` from it, and choose its API origin before registry credential checks and brief composition. Conflicting user `GROK_HOME` or target-owned path/env settings refuse; they do not win by precedence. Persist target ID, config digest and the non-secret path/API snapshot on the accepted session generation. Agent target edits are refused while occupied.
2. **Prepare ordinary Grok launch.** Keep registry arguments, model/effort resolution, session identity flags, rules payload and task tokens in their existing funnels. For a Docker target, `AgentSessionLaunchComposer` and `AgentLaunchResolution` explicitly select the Grok registry definition, including manual start/resume; they must not consult the installation's default Claude profile. The logical executable remains the registered Grok definition; only the target-aware physical planner selects `/opt/grok/bin/grok`. An unrecognized kind/custom executable/unsupported profile cannot be reinterpreted as Grok.
3. **Negotiate.** Add optional Docker binding to `AgentLaunchSpec`/`RunnerLaunchRequest` and require `dockerGrokExecV1` in `SessionRunnerHttpClient`. Null follows the old path. New server -> old runner refuses before POSTing a Docker launch. Runner independently validates bindings against its manifest, even for direct HTTP callers. SourceLanding `VerificationBinding` plus Docker target refuses before any child exists.
4. **Reserve and inspect.** Under the worker's exclusive durable reservation, inspect the configured engine/context and container by full ID. Validate Linux mode, ownership labels, resolved image, mounts, expected idle/no prior exec state and restart policy. Inspect only selected non-secret fields, never dump Docker `.Config.Env`. A matching stopped container may start. An unknown/replaced/paused container, digest disagreement, inaccessible mount or unavailable daemon produces a bounded problem and no alternate launch. Record a launch-pending journal before creating the exec child.
5. **Materialize rules and transform.** Write rules atomically on the host, derive the Linux read path and final logical Grok args. Compute the exact Linux cwd from the bounded host-relative path. Plan a physical Docker argv list, exemplified below. Host process cwd remains a valid Windows directory. Measure the final Windows-quoted Docker command against the existing budget; the extra wrapper bytes do not earn a larger budget.
6. **Start and receive.** Existing PtyHost owns docker.exe, input stays LF/bracketed paste/separate Enter under the existing Grok delivery profile, and the Linux TTY hosts Grok. Start the mapped ACP tailer; capture receipt and wait for the internal rules-read acknowledgement before releasing the ordinary brief. Do not mark a task Working/delivered from a screen redraw alone.

Illustrative physical argv, with each displayed item represented as a token, not a shell command string:

```text
docker.exe --context desktop-linux exec -i -t
  --workdir /work
  --env GROK_HOME --env ANTIPHON_API --env ANTIPHON_AGENT_ID
  --env ANTIPHON_SESSION_ID --env ANTIPHON_TASK_ID --env ANTIPHON_TASK_TOKEN
  <full-container-id> /opt/grok/bin/grok
  <existing Grok argument tokens, including session identity and short Linux rules bootstrap>
```

`-i` keeps stdin open, `-t` allocates the Linux PTY, and `--workdir` selects its cwd. Exec requires the container's main process to remain alive and does not resume itself after container restart. [Docker exec documentation](https://docs.docker.com/reference/cli/docker/container/exec/)

Use the Docker child's process environment for merged values; `--env NAME` reads a present host-process env value without placing it in the OS command line. The logical launch retains the host `GROK_HOME` for local probes; the physical Docker child env replaces it with `/var/lib/grok`. Empty overrides use the explicit empty form and are tested; do not accidentally inherit a stale container credential. Only export intended merged application/provider env names, orchestration names, and controlled Linux HOME/PATH/TERM values; never export the whole Windows daemon environment or its PATH. Host Docker context variables are not forwarded into the guest; its Docker CLI uses the mounted Unix socket. Reject case-colliding names before crossing into Linux. Docker's bare-name lookup behavior is in its [ValidateEnv implementation](https://raw.githubusercontent.com/docker/cli/master/opts/env.go). Tokens remain confidential process data; no argv-values, env dump, rules text or credentials in diagnostics/manifests.

### Paths, transcript and rules

Keep three distinct paths in a typed launch context: Windows host cwd, normalized Linux cwd, and physical host transcript path. For a fresh session launched in `/work/sub dir`, the expected host ACP path is:

```text
<HostGrokHome>\sessions\<Uri.EscapeDataString("/work/sub dir")>\<session-guid-D>\updates.jsonl
```

Do not call Windows `Path.GetFullPath` on the Linux string. Preserve Linux casing and slash semantics. The pinned 1.0.34 image must prove this layout with a real transcript; fixture agreement alone is insufficient. Persist both Linux cwd and the exact host transcript path in the Docker binding/sidecar before adoption. Resume uses the exact native session in the mapped home; if GUID lookup is needed for a prior cwd, require exactly one match, never the first/newest unrelated file. Missing or inaccessible resume storage must not turn a standing conversation into a fresh one. Existing host Grok behavior remains unchanged.

For rules, write to `<HostRulesRoot>\instructions\grok\<session-N>\rules.md`, but emit a receipt whose `Path` is `/antiphon/instructions/grok/<session-N>/rules.md`. Keep receipt transport version, generation, hash and byte count unchanged. `GrokRulesFileStore` gains a path context: physical path is used for write/verify/expire, guest path for receipt/bootstrap. Verify both are derivable from the bound target rather than trusting arbitrary receipt paths. The existing POSIX receipt validator still applies. Refresh after resume/compaction must name the same guest path and retain its acknowledgement barrier. No ad hoc copy that becomes stale after the initial launch.

Brief and refinement spill files need the same treatment. Convert only known generated spill paths under the allowed workspace to their `/work/...` equivalents; do not rewrite task prose or persist guest paths into host Git fields. **For this target, a spill write/mapping failure is a visible delivery refusal**, preserving the task/refinement body for recovery and sending no misleading pointer. Do not use the existing fallback to a `goal` JSON field as though it were the complete rendered brief/refinement with its role/reporting contract. This deliberately avoids adding a new instruction-download protocol in v0; host-target fallback behavior stays unchanged. The API callback still uses the target origin and existing task identity/authorization. Linux can use curl against that API; `pwsh` is not assumed to exist. v0 does not promise that every Windows repository script runs in the container.

### Lifecycle and resource ownership

Use a runner-owned, write-through/atomic journal under runner data, separate from the read-only guest rules mount. Key it by worker ID and `(SessionId, AcceptedStartedAt)`, and bind full container ID, image/config digest, host process identity, transcript path and guest cwd. No secrets or complete argv. States cover reservation, launch-pending, attached, stop-pending, stopped and unresolved. A file/process lock alone is not a durable vacancy signal after restart: journal + container observation decide occupancy.

Start permits exactly one live generation per container. An idempotent request for that generation adopts its known host only; it does not start a second `docker exec`. Follow-ups can reuse the same session under the existing named-agent ownership, but a changed target/home/cwd mapping refuses reuse. Normal task settlement may leave a live session **owned by the named agent**; it does not make it an anonymous warm pool worker. SourceLanding is rejected because native Windows job accounting covers only the client, not the Docker execution.

Explicit generation-bound stop, failed-launch compensation and unambiguously observed natural client exit stop the exact dedicated container, wait for its daemon-observed stopped state, drain/finalize host IO/transcript under the existing rules, then terminate/reap the owned host child and release occupancy. Use a bounded Docker stop (default design: 10 seconds grace plus a separately bounded inspect operation); Docker documents TERM then forced KILL after the grace interval. [Docker stop documentation](https://docs.docker.com/reference/cli/docker/container/stop/)

Before each destructive operation, match the journal's accepted generation and full container ID. An old stop must never affect a newly admitted generation. A same-name replacement is not the old resource. Daemon unavailability, journal loss, identity mismatch or uncertain IO drain retains unresolved occupancy and visible failure; no claim of completed cleanup, no automatic second launch, no global process/container kill. A provider stall continues to be a detection/decision state, not an excuse to stop the container.

Runner startup recovery precedes readiness, as today. Recover the Docker journals alongside existing host adoption: exact matching live PtyHost + container is adoptable; launch-pending with a missing host requires exact-container reconciliation/cleanup before reuse; conflicting or unknowable evidence holds the target. Container restart kills the old exec; resumed work requires a new accepted generation with existing native history. A crash after reserve, after spawn, after stop acknowledgement and before release each has a specific recovery test. On service shutdown do not silently remove the named agent's running work: retain durable ownership if host continues, or explicitly stop through the same contract.

Socket-created sibling resources are not covered by these claims. The qualifying canary creates only bounded foreground `--rm` resources with unique names/labels and records their removal. Generic automatic cleanup of arbitrary siblings, full descendant accounting and remote container supervision remain CARD-0490 work.

## Implementation slices

Names marked new are proposed files. Existing files listed are the integration points inspected above; Code should keep each slice buildable, record any necessary extra caller changes, commit/push the real outcome, and never advertise the feature before all required slices qualify.

| Slice | Files and concrete change | Ordinary tests / exit evidence |
|---|---|---|
| S1: image and local fixture | New `docker/grok-worker/{Dockerfile,.dockerignore,compose.yaml,README.md,artifact-lock.json}`; new `scripts/provision-docker-grok-worker.ps1`; sample manifest under `docker/grok-worker/`. Provision/status/recreate target-owned resources with exact IDs and preserved home; no stack changes. | New `scripts/test-docker-grok-worker.ps1` offline cases for arguments/identity/preservation; real dummy-home restart, replace and rebuild. Image digest, ELF/version, no embedded canary auth, callback JSON/health shape and socket canary recorded. |
| S2: target selection and persistence | New `server/Application/Settings/DockerGrokWorkerSettings.cs`, `server/Application/Services/DockerGrokWorkerResolver.cs`; `server/Domain/Entities/{Agent,AgentSession}.cs`, `server/Application/Dtos/{AgentDtos,AgentLaunchSpec}.cs`, `server/Application/Services/{AgentService,AgentTaskService,AgentTaskDispatcher,AgentControlService,AgentTuiLaunchResolver,AgentSessionLaunchComposer,AgentSessionService}.cs`, `server/Infrastructure/Data/AppDbContext.cs`, `server/Program.cs`; CLI-generated migration + snapshot. New shared contract `src/Antiphon.SessionRunner.Contracts/DockerGrokWorkerContracts.cs`. | New `DockerGrokWorkerSelectionTests`, `DockerGrokWorkerDispatchTests`, `DockerGrokWorkerConfigurationTests` in `tests/Antiphon.Tests/Application/`. Existing `PinnedAgentKindTests`, `GrokCredentialProbeDispatcherTests`, `GrokNativeSessionResumeTests`, `AgentServiceIntegrationTests`. Null migration backfill, target mismatch/refusal before worktree/session/child side effects, target manual/resume launch with a Claude default, host Grok launch unchanged. |
| S3: runner transport and launch preflight | `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`, `server/Application/Interfaces/ISessionRunnerClient.cs`, `server/Application/Dtos/SessionRunnerDtos.cs`, `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`, `src/Antiphon.SessionRunner/{Program,SessionRunnerSettings,SessionRunnerRuntime}.cs`; new `DockerGrokLaunchPlanner.cs`, `DockerWorkerProcessClient.cs` with an external-I/O seam. Resolve/validate config, read-only worker descriptor, inspect safe fields, tokenized argv/env, final budget, capability/error mapping; update fake/scripted clients for the new read contract. | New `tests/Antiphon.SessionRunner.Tests/DockerGrokLaunchTests.cs`, `DockerGrokPreflightTests.cs`; new `tests/Antiphon.Tests/Agents/DockerGrokRunnerWireTests.cs`. Existing `SessionRunnerCapabilityGateTests`, `GrokLaunchArgsTests`, `GrokRulesArgvPolicyTests`, `RunnerTerminalSessionInputEncodingTests`. Fake Docker captures actual argv/env without retaining secret values. |
| S4: path and instruction transport | `src/Antiphon.SessionRunner/{GrokTranscriptTailer,GrokRulesFileStore,TranscriptSidecar,SessionRunnerRuntime}.cs`; `src/Antiphon.SessionRunner.Contracts/GrokRulesTransport.cs` only as needed for typed path context; `server/Application/Services/{GrokRulesRefreshService,AgentTaskDispatcher,AgentTaskReplyService,DelegationReportFormatter}.cs`. New bounded path mapper. | New `DockerGrokTranscriptTests`, `DockerGrokRulesTests` in runner tests; `DockerGrokBriefPointerTests` in application tests. Existing `GrokTranscriptTailerTests`, `GrokRulesFileStoreTests`, `GrokRulesAdoptionTests`, `GrokRulesQueueBarrierTests`, `GrokRulesDispatchAcceptanceTests`. Assert complete content reaches the guest read surface and queue stays held until acknowledgement. |
| S5: durable lifecycle | New `src/Antiphon.SessionRunner/DockerWorkerLeaseStore.cs` and lifecycle coordinator; `SessionRunnerRuntime.cs`, `TranscriptSidecar.cs`. Carry non-secret target identity through `src/Antiphon.PtyHost.Protocol/{PtyHostMessages,PtyHostManifest}.cs` and host launch/manifest writer if required for independent recovery. Do not extend Windows custody receipts to claim Docker descendants. | New `DockerGrokLifecycleTests`, `DockerGrokRecoveryTests`; existing `RunnerSessionGenerationTests`, `RunnerStartupReadinessTests`, `RunnerCustodyTests` and affected host manifest/protocol tests. Real disposable container child/grandchild termination, old-generation stop rejection, duplicate start race, and restart recovery evidence. |
| S6: registration and qualification | New `scripts/register-docker-grok-worker.ps1`, `scripts/verify-docker-grok-worker.ps1`, new `docs/docker-grok-worker.md`; update owner links/sections in `docs/{agent-kinds,agent-credentials,ai-agent-tui-configuration,bootstrap,testing-and-build}.md`. No new UI editor required; existing agent read DTO exposes target ID. | New `tests/Antiphon.Tests/Application/DockerGrokDelegateAcceptanceTests.cs` (isolated API/runner) and `tests/Antiphon.SessionRunner.Tests/DockerGrokLiveCanaryTests.cs` (explicit opt-in). Pin -> rules acknowledgement -> complete UserPrompt -> answer/report -> persisted task result. Operator qualification against the deployed feature, with desired/observed server and runner SHAs. |

Do S1's non-secret real transport probe early. If actual ConPTY -> Docker input, Linux transcript layout or auth mount semantics contradict the assumptions, record exact bytes/layout/process observations and return to Plan/Investigate before wiring more production paths. A failed test is not authority to replace Grok delivery confirmation with quiet-time heuristics.

## Test-design handoff: guards and positive controls

TestDesign must turn the following candidate methods into the executable V/R/PC matrix, identify exact mutations and fixtures, cover complete-body spill refusal/recovery, and inventory all callers/consumers of the new DTO fields. Proposed names are contracts to implement, not existing passing methods. Each PC must fail at its named assertion, not compilation, fixture startup or test discovery. Native Docker/control probes and authenticated model qualification have separate evidence; no offline test can establish the latter.

| Guard | Observable contract and proposed test | PC candidate (deliberate production mutation) |
|---|---|---|
| G-1 / PC-1 | `DockerGrokWorkerDispatchTests.Explicit_pin_keeps_container_target_and_host_default_unchanged`: pinned task goes to its named Docker worker; unpinned Grok task stays host registry. | Drop the target binding during dispatch; assert the captured runner request names the expected target. |
| G-2 / PC-2 | `DockerGrokWorkerSelectionTests.Unsupported_workspace_is_refused_before_allocation`: Worktree, SourceLanding, wrong kind/backend/profile or orchestrator refuse before rows/launch effects. | Remove Worktree admission guard; named method must detect a worktree creation/launch that should never happen. Split other variants into explicit methods in TestDesign. |
| G-3 / PC-3 | `DockerGrokLaunchTests.Linux_exec_receives_tty_stdin_and_mapped_cwd`: actual fixture exec observes stdin TTY, receives input, reports exact cwd and argument boundaries. | Remove `-t`; fixture reaches its expected non-TTY assertion rather than failing to build. Add independent `-i` coverage. |
| G-4 / PC-4 | `DockerGrokRunnerWireTests.Old_runner_refuses_docker_target_before_post`: missing capability means zero launch POSTs and bounded incompatibility code. | Bypass feature gate; fake HTTP server records the forbidden launch. |
| G-5 / PC-5 | `DockerGrokLaunchTests.Secrets_reach_child_env_without_argv_or_log_values`: sentinel token survives child env hash comparison; absent from captured argv/log/journal and container-creation env. | Render one sentinel env entry as `NAME=value` in argv; assertion catches exposure without printing sentinel. |
| G-6 / PC-6 | `DockerGrokDelegateAcceptanceTests.Callback_uses_worker_api_origin_and_identity`: matching task identity is observed by isolated API through container, while host configuration remains localhost. | Select installation origin for the worker env; test checks request destination and matching receipt, not just 200. Include a fake SPA-200 endpoint as a negative case. |
| G-7 / PC-7 | `DockerGrokPreflightTests.Mount_mismatch_refuses_even_when_host_auth_is_valid`: valid dummy host store plus wrong guest mount refuses before exec; missing auth retains `provider_sign_in_required`. | Skip source/destination mount comparison; fixture records an unauthorized exec. Keep credential contents out of errors. |
| G-8 / PC-8 | `DockerGrokTranscriptTests.Linux_cwd_selects_own_session_not_host_decoy`: place own Linux-path ACP file and a plausible Windows-path/foreign-session decoy; only own full UserPrompt is ingested. | Feed host cwd into existing `ResolveUpdatesPath`; assert missing own prompt/decoy rejection. |
| G-9 / PC-9 | `DockerGrokRulesTests.Refresh_reads_current_mounted_bytes_before_work`: bootstrap, receipt and queued reread use guest path; host verification checks exact bytes/hash; ordinary work waits for read acknowledgement. | Return physical Windows path in the receipt; actual guest-read fixture cannot acknowledge and work stays held. Separately mutate barrier in its existing queue test. |
| G-10 / PC-10 | `DockerGrokBriefPointerTests.Spilled_refinement_is_complete_and_guest_readable`: oversize multiline/non-ASCII brief and refinement retain full bytes, reporting suffix and correct mapped path. | Leave host spill path in guest pointer; guest read assertion fails. Separately prove failed spill refuses without typing and retains the body for recovery. |
| G-11 / PC-11 | `DockerGrokLifecycleTests.Stop_waits_for_container_descendants`: fixture leaves a long-running grandchild after docker client exits; stop resolves exact container, confirms stopped, drains and only then frees occupancy. | Replace Docker stop with host client kill only; grandchild/container remains running at assertion. No production containers in this test. |
| G-12 / PC-12 | `DockerGrokLifecycleTests.Old_generation_cannot_stop_new_owner`: stale stop after a new generation is accepted sends no Docker stop and new child remains alive. | Remove generation match, leaving ID equality; forbidden stop is observed. |
| G-13 / PC-13 | `DockerGrokRecoveryTests.Pending_launch_survives_restart_without_second_exec`: crash between persisted reservation and attach; recovery reconciles original exact resource or holds unresolved; concurrent start gets no second exec. | Treat journal presence as vacancy after restart; count reaches two execs. Also cover crash after stop and before release. |
| G-14 / PC-14 | `DockerGrokPreflightTests.Replacement_name_never_substitutes_for_bound_id`: replace container with same name but different ID; adoption/stop refuse/retain ownership evidence. | Resolve bound container by name at recovery; fake/isolated new container receives the forbidden operation. |
| G-15 / PC-15 | `DockerGrokWorkerConfigurationTests.Path_escape_and_mapping_drift_refuse`: sibling prefix, `..`, junction/reparse, case collisions and changed digest do not map into an admitted target. | Remove containment/digest check in separately scoped methods; choose the precise branch each test is intended to kill. |

Additional required ordinary guards: dummy home survives restart/replacement/rebuild and auth sentinel is absent from image layers/build logs; unavailable daemon does not release occupancy; duplicate/ambiguous native-session matches do not bind; stopped container never masquerades as running exec; explicit empty env does not inherit container credentials; final Windows command budget is enforced; rules expiration cannot traverse arbitrary receipt paths; a Docker SourceLanding request never produces a native custody receipt. TestDesign assigns additional PCs where these introduce independently critical branches.

For delivery/settlement inventory, use the durable join `(task ID, session ID, accepted generation, queued message ID, target ID/config digest, container ID)`. Producer is the existing dispatcher/refinement/rules producer; destination is the Linux Grok composer; durable transit is the existing queue plus rules state; receipt is the matching **complete UserPrompt** in the own-session ACP transcript. Report token -> persisted task Result -> caller completion follows the existing pipeline. An enqueue, exec start, output redraw or server log is not a delivery receipt. Cover ready and already-busy session timing, server restart with queued work, runner restart before transcript receipt, and normal report settlement without duplicate delivery.

Test infrastructure requirements:

- Use an isolated runner on a random port with owned stores, temp standalone Git repo and per-test DB schema; preserve `ProductionRunnerGuard`. All containers/volumes/networks/files have a unique test prefix and an exact ownership inventory. No live OAuth home in automated tests.
- Real Docker tests are explicit opt-in; absence is reported as unavailable/skipped, never qualified. Use fake CLI/engine seams for deterministic crash/error paths and a small real Linux fixture for TTY/descendants. The runner remains a local inherited child of the test. No external executor or production runner.
- Process-spawning test classes use assembly-local `ParallelLimiter<ProcessSpawnLimit>`. Run the native test projects sequentially; never co-schedule `Antiphon.Tests` with `Antiphon.Agents.Pty.Tests`. Use real/advancing time, not a frozen clock in queue loops.
- Ordinary Code verification runs touched classes and the listed regression classes. TestDesign sets the final cross-project profile; this plan does not require an unrelated full suite. Commands use `dotnet run --project tests/<project> --property:OutputPath=bin-c575/ -- --treenode-filter "/*/*/<Class>/*"`. Commit before substantial runs, keep source frozen during them, record verified SHA and nonzero executed counts, and clean exact owned output directories after path checks.
- Post-land Mutation uses `/*/*/Class/ExactMethod` per PC, red assertion then restore/green. Run only genuinely independent controls together; retain per-PC evidence. A new failure must be checked at the base before calling it inherited; no timeout expansion/retry/assertion weakening to make red green. SourceLanding evidence remains external, not committed into the snapshot.

## Live qualification and acceptance

1. **Non-secret container qualification:** record Windows host/runtime IDs, exact image/binary version/digest, Docker context/engine Linux mode and sanitized mounts. Confirm `test -t 0`, complete multiline input, `/health` plain health plus `/api/version` JSON from the direct API origin, `docker info`, and bounded unique `hello-world` removal. Dummy-home marker survives restart, replace and rebuild without ever being in the image. Port 17280 is inventoried as occupied; do not repeatedly bind against production merely to demonstrate a known collision.
2. **Isolated Antiphon qualification:** real Windows runner + owned Linux container fixture must prove mapped transcript delivery, rules reads, oversize brief/refinement, session resume, old-generation stop, child/grandchild cleanup and crash recovery. Compare observed guest bytes with independent expected content, not only planner-generated paths. Confirm host Grok remains unchanged.
3. **Authenticated local worker canary:** after normal deployment/readiness checks, operator-provisioned home and enabled named target, dispatch a bounded prepared brief through Antiphon. It asks for a unique nonce answer, read-only API callback/version and bounded socket canary. Capture IDs and sanitized transcript excerpts proving full UserPrompt, rules acknowledgement, real assistant answer, report token and persisted task Result. This is the first real model proof; do not substitute a fake transcript or `--version`. Verify the nonce from the destination evidence, not only the model's claim to have called back.
4. **Continuity and release:** idle follow-up/resume reads the same native history and current rules; a container restart does not resurrect an exec invisibly. Explicit stop proves container stopped and owned host drained; unknown Docker state remains visible. A failed canary keeps the target disabled and records which gate failed.

Code/Review must distinguish automated evidence from operator activation evidence. Deploy runner and server contracts together using the canonical runbooks, then verify actual loaded capability and API SHA; branch publication alone is not activation. A live qualification brief may authorize the small model/socket canary, but this Plan dispatch did not execute one.

Accept v0 only when the registered named agent can complete that whole dispatch path and the ordinary guards pass. Do not declare generalized Linux build support, anonymous pool scheduling, remote phone-home, cross-machine credential portability, host-socket isolation or Docker-backed verification custody.

## Handoff and remaining measurements

Proceed to TestDesign with D-1 through D-13. Resolve exact fixture/method/PC selection and the complete-body spill refusal/recovery cases in that stage. Code's early image/transport slice must record the pinned official artifact source/checksum, actual Linux cwd encoding, ConPTY -> Docker composer behavior and descendant-stop behavior; those are specific remaining measurements, not assumed investigation successes. If one contradicts this design, return the observation and affected decision to Plan/Investigate.

The Plan artifact is the only repository change from this dispatch. Validation of this dispatch is document/source-reference review plus Git diff/whitespace checks; no tests/builds are claimed.

## Verification design

TestDesign: 2026-09-19, task 318daeb7, plan commit 6f16fb61280b7e94b224a399943c295ac5f0b0bb. This section appends to the fix design; D-1 through D-13 and S1 through S6 above are unchanged. New method names below are implementation contracts, not claims that tests already exist or pass.

**Disposition: return to Plan for VD-1, the running-refinement persistence seam.** The running-task branch of AgentTaskReplyService.RefineAsync commits a Refined event, writes a spill, then enqueues without a refinement identity. NewEvent caps Detail at 4,000 characters; FitRefinementForTyping names files using second-resolution timestamps. A crash/write/enqueue failure can lose the tail; two refinements in one second can overwrite the first file. The plan requires full-body preservation/recovery but does not select its durable record, stable identity, atomic handoff or recovery owner. A clipped event cannot meet that requirement.

Plan must name storage/replay of the complete accepted rendered refinement, its unique identity, immutable file association, destination generation, enqueue retry/dedup and startup recovery. It must also cover the secondary queue spill: SessionMessageQueueSpillTests establishes host inbox paths and a type-original-on-write-failure fallback, neither of which proves Docker instruction delivery. This is an implementation seam, not an operator preference. These tests specify the observable contract without choosing a new architecture. After the amendment, return only VD-1 and changed guards to TestDesign; do not send this version directly to Code.

### Inspection

Paths are repository-relative. Entries name bodies read; partial-file inspections name the methods actually inspected.

| Test/fixture bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| tests/Antiphon.Tests/Application/PinnedAgentKindTests.cs (T1-T4, seed/create/dispatch helpers); GrokCredentialProbeDispatcherTests.cs (tests and harness) | Pin/default/profile/auth before allocation -> V-2/R-1. Credential fixture stops before launch; cannot prove guest auth. |
| GrokNativeSessionResumeTests.cs (all); GrokDelegateEndToEndTests.cs (a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder, BuildHarness, PumpTranscriptAsync, WaitUntilAsync, GrokLaunchEnv, harness disposal) | Card-only fresh fallback versus strict standing resume; direct runner, ACP, settlement -> V-2/V-5/R-2. Do not copy its shared DB or FinalMessageGraceSeconds=0 escape hatch into native acceptance. |
| tests/Antiphon.Tests/Agents/SessionRunnerCapabilityGateTests.cs and RunnerTerminalSessionInputEncodingTests.cs (all bodies/helpers) | Legacy absent-capability compatibility versus mandatory Docker capability; LF/bracketed paste/separate CR -> V-3/R-3. |
| tests/Antiphon.SessionRunner.Tests/GrokTranscriptTailerTests.cs (Real_turn_rows_normalize_to_UserPrompt_ToolCall_coalesced_text_and_TurnEnd; half-written/re-tail tests; both path tests; append/poll/temp helpers) | Lazy creation, own path, partial line, deterministic replay -> V-4/R-4. Windows Path.GetFullPath(cwd) is not Linux-layout evidence. |
| GrokRulesFileStoreTests.cs and GrokRulesAdoptionTests.cs (all, including StoreFixture, runtime/probe/teardown) | UTF-8, 262144/262145-byte boundary, atomic replace, isolated sessions, generation/hash/path, expiry -> V-4/R-5. |
| tests/Antiphon.Tests/Application/GrokRulesQueueBarrierTests.cs (28 rows); GrokRulesInitializationTests.cs (ack matrix, deadline and Fixture); GrokRulesFailureTests.cs (all); GrokRulesCompactionRecoveryTests.cs (capture helper and native-boundary recovery through TurnEnd); runner native-compaction provenance markdown | Entry-point/origin barrier, ack ownership/id/hash/generation/end, deadline, compact recovery -> V-5/R-6. 1.0.13 capture does not qualify 1.0.34. |
| GrokRulesDispatchAcceptanceTests.cs (whole test/extraction helpers) | Actual CLI read_file output covers every rules line; headed/stub gates and relay limitations -> V-8/V-9. Docker acceptance adds complete UserPrompt and exact spill bytes. |
| SessionMessageQueueSpillTests.cs (harness, large/small WhenIdle, File_write_failure_types_the_original_and_raises_oversize) | Secondary inbox spill and host fallback -> VD-1/V-5/R-7. |
| ReceiptFailureDeliveryTests.cs (receipt/caller cut entry points, VerifyDeliveryAsync, persistence interceptors/offset clock); VerificationRoundDeliveryTests.cs (C544_CompletionReceipt, C544_CompletionRecovery, C544_PointerReceiptRequiresContent, C544_CompletionReceiptWholeWire, C544_StampIsNotReceipt, AssertReceivedOnceAsync) | Real producer/queue, busy/eligible, commit/attempt/prompt cuts, complete wire/hash/floor -> V-5/V-10/R-8. |
| tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs (options/DI, submitted-byte callback, insert helpers); TestDbFixture.cs (lifecycle/isolated store); DelegationTestServices.cs (graph registration); ProductionRunnerGuard.cs; C544DeliveryRig.cs (create/callback/restart/scan/deliver/cuts); C544World.cs (create/attach setup) | Nearest fixtures for new application tests -> V-2/V-5/V-10. CreateIsolatedSchemaAsync clones a database. Fake callbacks must record submitted bytes, never expected bytes. |
| tests/Antiphon.SessionRunner.Tests/RunnerSessionGenerationTests.cs (C502_V25_real_runner_exe_advertises_the_capability_echoes_and_refuses_a_mismatched_kill, exit reader); RunnerStartupReadinessTests.cs (whole); RunnerCustodyTests.cs (Live_host_adoption_keeps_original_binding_and_rejects_changed_replay, CustodyFixture); LocalHttpRunner.cs (all); tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs (owned start/census/stop setup) | Nearest HTTP/lifecycle fixtures -> V-6/V-7/R-9. Runner-child kill alone cannot clean detached hosts or Linux execs. |
| Root Dockerfile, .dockerignore, docker-compose.dev.yml; scripts/test-deploy-am-service.ps1 (fake runner/temp Dockerfile/assertion/provisioning setup) | Nearest image/script fixtures -> V-1. Root server image/dev Compose excluded from edits/mutations. |
| Production AgentTaskReplyService.RefineAsync/FitRefinementForTyping/NewEvent; DelegationReportFormatter pointer contracts; GrokTranscriptTailer.ResolveUpdatesPath/TryLocateSessionDirectory; SessionQueuedMessage identity fields; runner test project references | VD-1, durable joins, filename collision, bounded event, path mapping and fixture dependencies. |

Owner sections read: project conventions/layers, testing fast lane/isolation/mutation, orchestration delegate/SourceLanding rules, credentials custody/precedence, Grok kinds/configuration, session delivery/generation/rules invariants and ConPTY ceilings. No implementation, migration, image, authentication or activation was changed.

**Missing setup assigned to Code after VD-1:**

- A scoped DockerWorkerProcessClient external-I/O seam and journal fault hooks. Scripted engine records tokens, safe inspect fields and ordered reserve/exec/stop/inspect/drain/reap operations. It allows the mutated operation to occur so tests fail at their assertion rather than fixture setup. Every one-shot cut has a reached counter asserted equal to one. A local inherited child records actual Windows argv/env boundaries without logging sentinel values.
- DockerGrokTestWorld: cloned TestDbFixture database; standalone scratch Git repository with .git directory; unique home/rules/journal stores; production task/queue/reply/notification services; new providers/contexts on recreation; real HTTP runner client and event-pump catch-up. Use DelegationTestServices. DirectSessionRunnerClient alone cannot qualify V-8.
- An ordinary-test-only disposable Linux transport fixture through real Windows PtyHost/docker.exe. It reports isatty/cwd/arg boundaries, reads mounted files, can hold a turn busy, swallow one Enter, delay ACP append and leave a foreground child/grandchild after client loss. Unique labels/full IDs and owned resource ledger govern teardown. The Windows fakegrok.exe is not a Linux fixture.
- Pinned Grok 1.0.34 binary/hash/version and base/tool digests; sanitized native ACP fixture with provenance; synthetic provider endpoints redirecting both credential and actual chat traffic. The host stub binds a guest-reachable random port and accepts only synthetic routes. A title request or /api-key hit is not evidence that the real user turn reached the stub.
- Program tests preserve ProductionRunnerGuard, disable unrelated check/diagnose/distiller/Hangfire work and use random isolated ports. New process spawners carry their assembly's ParallelLimiter<ProcessSpawnLimit>. Queue clocks advance in real time; frozen exact-deadline clocks are limited to methods with no queue polling.
- scripts/test-docker-grok-worker.ps1 supports named -Case selections. DockerGrokProvisioningTests exposes one TUnit method per PC, running its exact case in a foreground child; unknown/zero cases fail. Dockerfile/Compose cases parse actual worker files. They do not substitute for native build/layer evidence.
- PC observation names (effects, recipient, guestRead, lease, recovered) denote fixture-owned observations of the real path. recovered.FullBody reads the durable record VD-1 must select. No manual Pending->Sent updates or expected-prompt insertion may simulate progress.
- DTO consumers: Agent CRUD/read, session snapshot/migration, task selection/dispatch/reuse, manual/resume control/composition, launch queue, HTTP JSON/client capability, direct runner validation, planner, sidecar/PtyHost adoption, transcript/rules/expiry, stop/recovery, descriptor and registration scripts. Update fake/recording/refusing clients for the descriptor; never silently return an available worker. Null target round-trips unchanged.

### Delivery inventory

Durable join: (TaskId, AgentSessionId, AcceptedStartedAt, DockerWorkerId, ConfigDigest, FullContainerId), plus the individual obligation/queue ID, immutable wire digest and attempt floor. AcceptedStartedAt is the existing normalized equality token. Never rebuild a historical binding from mutable current agent configuration.

| Path | Producer -> destination | Persistence boundary and durable identity | Recovery | Observable receipt |
|---|---|---|---|---|
| DL-1 initial/follow-up brief | Task service/dispatcher -> Linux composer via real launch/session queue | Accepted task/session binding, full rendered brief/spill, queue Id/ExecutionTaskId, LastDeliveryGeneration and baseline | Transaction/enqueue failure, lost wakeup, server/runner recreation, late ACP catch-up | One matching complete UserPrompt of actual wire text in own ACP stream, persisted above attempt floor; exact guest-read spill bytes including role/report suffix. |
| DL-2 running refinement | RefineAsync -> bound delegate through real queue | VD-1 requires complete accepted rendered body, stable refinement ID, immutable spill and queue link; current clipped event/filename is insufficient | Accepted-write/spill/enqueue/attempt/receipt cuts; same-second refinements recover separately | Complete refinement wire UserPrompt plus exact guest file read; tail beyond 4,000 chars survives recreation. Refusal types nothing and retains complete accepted work. |
| DL-3 bootstrap/resume/compact rules | Composer/runner rules store/refresh service -> internal Grok read turn | Atomic host file; receipt generation/hash/count; persisted rules state; unique (session, RulesRefreshKey), queue ID | Replace/receipt/boundary/trigger/queue/attempt/ack/end cuts; same deadline/key after restart | Complete owning UserPrompt, exact guest-read bytes, matching assistant ack and successful TurnEnd before ordinary work. |
| DL-4 report/completion | Grok report -> settlement -> completion scanner -> caller queue | Report/task Result; applicable terminal TaskCompletion outbox, Id/SourceEventId/CompletionSnapshotJson; frozen CompletionDeliveryJson and SourceLandNotificationId | Terminal rollback/commit, notification enqueue/wakeup, render/spill/attempt/prompt cuts; terminal tasks remain recoverable | Caller complete UserPrompt for frozen wire above its own floor, correct identity and pointer hash. Succeeded and completion stamp are insufficient. |
| DL-5 failure outcome | Auth/preflight/spill/delivery failure -> existing failure producer -> caller queue | Failure event/result plus durable notification/destination; delivery-failure outbox where applicable | Before/after failure+obligation commit, queue/attempt/receipt cuts, already-Failed task | Matching complete caller failure UserPrompt. Incident, HTTP refusal or terminal event is not delivered notification evidence. |

DL-4 covers ordinary Shared completion and admitted profile-v1 Code/Review completion. Plan must identify any newly promised asynchronous DL-2 refusal note; a synchronous refusal must not be labeled a delivered caller message.

**Required producer-to-recipient matrix, V-5/V-10:** DL-1 through DL-5 each cross busy=false/true with every applicable handoff cut. DL-1 also covers fresh/existing named sessions and Shared/ReadOnly; DL-3 crosses bootstrap/resume/compact. Eligible recipients receive through normal enqueue/flush scheduling without a synthetic business event. Busy recipients have zero writes/attempt charges before their actual TurnEnd, then receive exactly once.

Cuts: before producer commit; after producer commit before enqueue; before queue insert commit; after queue commit before wakeup; after attempt claim before typing; after complete native UserPrompt before verdict/ack persistence; after verdict before receipt-link persistence. DL-2 adds before/after spill replacement. DL-3 adds compact transcript commit before unique trigger/coverage commit, and ack before successful TurnEnd. DL-4/DL-5 add terminal+obligation atomicity and frozen-render/spill handoffs. Assert the cut was reached once. Recreate all services/contexts, preserve durable stores and recipient only, then run production startup/periodic recovery and queue. Never manufacture successful task/queue/transcript progress.

Before acceptance, rollback means zero surviving obligation/input; retry the original request. After acceptance, require full recovery or visible retained refusal with original bytes. Post-submit crash late-confirms without another submit. Runner recreation tests both exact retained host/container and unobservable host state. Simultaneous server+runner loss combines durable queue with launch-pending recovery; clearing stores is not recovery.

Negative evidence: no prompt; ID/header-only; missing tail; stale full prompt below floor; wrong session/generation; Windows-path decoy; identical text before attempt; separate fragment prompts; Sent or screen-only. A spill pointer must itself match completely, with exact referenced bytes/hash and guest read evidence. Three marker strings alone are insufficient. Accepted request, queue insert, report token, terminal event, transport acknowledgement and Sent never satisfy delivery acceptance.

Substitutes: scripted Docker I/O proves decisions, not daemon/PTY behavior; local recorder proves Windows argv/env, not Linux env lookup; a controlled terminal producing ACP only from actual submitted bytes proves real queue/receipt logic, not native Grok; Linux fixture proves two-hop transport/file/stop behavior, not model compliance; real Grok+synthetic provider proves native tool wire, not subscription auth/model adherence. V-9 supplies the final evidence. Ordinary Review rejects a design/run that stops before recipient evidence.

### Proves it works now

These are implementation exit requirements, not runs performed by TestDesign. V-1..V-6/V-10 are Code tests; V-7/V-8 use the explicit disposable Docker lane; V-9 needs separately authorized live qualification before enablement.

| ID | Behaviour | Layer / test or command | Expected |
|---|---|---|---|
| V-1 | Image inputs, passive registration, preserved dummy home, no shared resources | DockerGrokProvisioningTests; DockerGrokWorkerConfigurationTests; pwsh -NoProfile -File scripts/test-docker-grok-worker.ps1 -Case All | Refusals before effects; valid registration idempotent; dummy home bytes preserved. Actual image proof is V-7. |
| V-2 | Named dispatch, restrictions, migration, manual/resume, immutable mapping | DockerGrokWorkerSelectionTests; DockerGrokWorkerDispatchTests; DockerGrokWorkerConfigurationTests | Shared/ReadOnly and manual/resume use Grok under Claude default; unsupported cases allocate nothing; legacy target stays null. |
| V-3 | HTTP capability and direct runner checks, argv/env/cwd, command budget | DockerGrokRunnerWireTests; DockerGrokLaunchTests; DockerGrokPreflightTests | Zero old-runner POSTs; independent runner validation; child env hash matches, no secret in argv/logs; final quoted wrapper budget -1/exact/+1. |
| V-4 | Linux path, strict resume, mapped rules receipt/adoption/expiry | DockerGrokTranscriptTests; DockerGrokRulesTests | Own complete prompt, lazy/partial append, deterministic replay; no fresh on missing/unreadable/ambiguous history; corrupt receipt refused. |
| V-5 | DL-1/2/3 real producer -> recipient plus cuts | DockerGrokBriefPointerTests; DockerGrokDeliveryRecoveryTests | Complete wire UserPrompt/full guest-read bytes; no failed-spill fallback/duplicate/rules-barrier bypass. Requires VD-1 amendment. |
| V-6 | Exclusive lifecycle and crash recovery | DockerGrokLifecycleTests; DockerGrokRecoveryTests | Journal before exec; exact generation/ID; daemon stopped -> drain -> host reap -> vacancy. Unknown state visibly retains occupancy. |
| V-7 | Real image/mounts/TTY/stdin/env/path/stop | New runner method DockerGrokNativeTransportTests.Native_transport_and_lifecycle; verifier native mode | Host/guest Git HEAD equal; mounted home cannot hide binary; dummy auth survives restart/replace/rebuild and is absent from layers/history/logs; child/grandchild alive before stop, absent after; bounded uniquely labeled socket canary removed. |
| V-8 | Named real API -> HTTP runner -> PtyHost -> Docker -> Grok 1.0.34 -> ACP -> queue/report | New application method DockerGrokNativeDelegateAcceptanceTests.Native_grok_dispatch_reads_complete_instructions | Rules read+ack, complete brief/refinement, correct callback/task/session identity, persisted report and caller receipt; SPA-200 fails; actual version/hash/cwd encoding recorded. |
| V-9 | Authenticated deployed worker | New runner method DockerGrokLiveCanaryTests.Authenticated_named_worker_completes_and_resumes; scripts/verify-docker-grok-worker.ps1 live mode | Desired/observed server/runner SHA and capability, image digest, native prompt/read/answer/report/caller receipt; strict resume/history and new rules read; one native auto-compact reread under pinned version. Skip/unavailable is not qualified. |
| V-10 | DL-4/5 complete caller outcome across handoffs | DockerGrokDeliveryRecoveryTests; DockerGrokDelegateAcceptanceTests | Busy/eligible caller gets immutable complete note; terminal+obligation atomic; same queue identity after recovery; no prefix/Sent/status confirmation. |

Boundary combinations: run a valid control before refusals, vary one discriminator, then cover valid-auth+wrong-mount, same-name+wrong-ID, same-timestamp+wrong-session, same-ID+old-generation, same-worker+changed-mapping, stopped-container+old-exec and concurrent identical/different generations. Paths cover root/descendant, spaces, Unicode, quotes, sibling prefix, dotdot, mixed separators, Windows case equivalence, POSIX case sensitivity and fixture-owned junction escape. Standalone .git directory succeeds; linked .git file/Windows core.worktree refuses.

Rules: 0/1/262144/262145 UTF-8 bytes, >1000-line tail, CR/LF/CRLF, invalid surrogate/NUL; preserve existing validity decisions without normalizing file bytes. Argv: quotes/backslashes/empty tokens and identity flags; budget includes wrapper quoting. Grok inline ceiling is zero: short rendered briefs/refinements also spill. Test secondary pointer spill at inbox/modern ceiling, two same-second refinements and write failure after an earlier successful spill.

Full Cartesian invalid-admission x mount x crash combinations are excluded: independent PCs and the named cross-boundary combinations cover them. Busy/eligible x handoff-cut is not excluded. Native crash cases use an owned child crash; Task cancellation alone does not prove crash recovery.

### Guards the regression

| ID | Existing tests/filters | Decisive assertion |
|---|---|---|
| R-1 | PinnedAgentKindTests/*; GrokCredentialProbeDispatcherTests/* | Standing kind/pool unchanged; absent registry auth leaves no session/worktree. |
| R-2 | GrokNativeSessionResumeTests/* | Card-only fallback and explicit host-home precedence unchanged; Docker standing strictness tested separately. |
| R-3 | SessionRunnerCapabilityGateTests/*; RunnerTerminalSessionInputEncodingTests/* | Host null capability compatible; multiline bracketed LF then separate CR. |
| R-4 | GrokTranscriptTailerTests: Real_turn_rows_normalize_to_UserPrompt_ToolCall_coalesced_text_and_TurnEnd; A_half_written_trailing_line_is_held_until_its_newline_arrives; A_retail_from_offset_zero_reproduces_identical_sequences_and_uuids; ResolveUpdatesPath_matches_groks_session_store_layout; TryLocateSessionDirectory_finds_the_guid_under_a_foreign_cwd_encoding | Native row shape, no partial row, identical replay and old host path. |
| R-5 | GrokRulesFileStoreTests/*; GrokRulesAdoptionTests/* | Exact bytes/hash, atomic replace, verified receipt only, unrelated file survives expiry. |
| R-6 | GrokRulesQueueBarrierTests/*; GrokRulesInitializationTests: Only_current_refresh_assistant_ack_with_confirmed_prompt_releases_barrier; Deadline_is_persisted_and_recreation_does_not_reset_it; GrokRulesFailureTests/* | 28 barrier cases, owning ack/end, fixed inclusive deadline, retained ordinary body. |
| R-7 | SessionMessageQueueSpillTests: A_large_ui_whenidle_message_spills_and_the_row_stores_the_pointer; A_small_ui_body_is_typed_whole_with_no_spill_file; File_write_failure_types_the_original_and_raises_oversize | Existing host pointer/fallback behavior unchanged. |
| R-8 | VerificationRoundDeliveryTests: C544_CompletionReceipt; C544_CompletionRecovery; C544_PointerReceiptRequiresContent; C544_CompletionReceiptWholeWire; C544_StampIsNotReceipt | Full caller prompt above floor, immutable hash, exactly once across terminal/queue cuts. |
| R-9 | RunnerSessionGenerationTests/C502_V25_real_runner_exe_advertises_the_capability_echoes_and_refuses_a_mismatched_kill; RunnerStartupReadinessTests/*; RunnerCustodyTests/Live_host_adoption_keeps_original_binding_and_rejects_changed_replay | Mismatched kill leaves child alive, adoption precedes readiness, native custody stays strict. |

GrokRulesDispatchAcceptanceTests is fixture precedent, not a second redundant native run; V-8 is its required Docker counterpart. Full unrelated assemblies/client/browser suites are excluded because there is no client change and affected contracts have named coverage. If Code changes host protocol fields, its exit inventory adds the exact affected protocol/manifest methods before ordinary Review.

### Guard inventory

The 15 earlier rows are candidate groups, not the final count. The following expanded inventory supersedes their numbering **within Verification design**. Every independently bypassable guard has its own PC; none is waived. Boundary variants sharing a single guard remain arguments of that guard's one exact method. Split worker/instance labels, session/timestamp, mount source/destination, ack id/hash/generation, and stop/inspect/drain/reap decisions are intentional.

| Guard | Plan reference and invariant | Control |
|---|---|---|
| G-1 | S1/D13: Disabled entry cannot launch. | PC-1 |
| G-2 | S1/D4: Artifact verification fails on absent/wrong SHA before build. | PC-2 |
| G-3 | S1/D4: Qualified version check gates image acceptance. | PC-3 |
| G-4 | S1/D5: Home/auth cannot enter image context or image ENV. | PC-4 |
| G-5 | S1/D5: Recreate/rebuild preserves external home. | PC-5 |
| G-6 | S1/D6: Provisioning cannot silently create missing bind source. | PC-6 |
| G-7 | S1/D11: Replacement/removal verifies owned full ID immediately before action. | PC-7 |
| G-8 | S1/D12: Worker Compose cannot claim shared ports. | PC-8 |
| G-9 | S1/D12: Provisioning cannot enable privileged mode. | PC-9 |
| G-10 | S1/D12: Worker image starts init/idle, not another daemon. | PC-10 |
| G-11 | S6/D2: Existing name with different target/identity is never patched. | PC-11 |
| G-12 | S6/D2: Registration cannot start/spend or join a pool. | PC-12 |
| G-13 | S2/D2: Pinned dispatch retains typed Docker binding. | PC-13 |
| G-14 | S2/D2: Anonymous tasks cannot adopt named Docker worker. | PC-14 |
| G-15 | S2/D3: Docker Worktree task refuses before rows/worktree/launch. | PC-15 |
| G-16 | S2/D13: Server refuses Docker SourceLanding. | PC-16 |
| G-17 | S2/D3: Only Grok is admitted. | PC-17 |
| G-18 | S2/D3: Herdr/direct backends cannot receive Docker binding. | PC-18 |
| G-19 | S2/D3: Docker worker has no managed TUI profile. | PC-19 |
| G-20 | S2/D2: Pool-delegate flag cannot be combined with target. | PC-20 |
| G-21 | S2/D2: Worker target cannot be an orchestrator. | PC-21 |
| G-22 | S2/D2: Card assignment does not auto-select Docker worker. | PC-22 |
| G-23 | S2/D1: Docker v0 is tied to local runner. | PC-23 |
| G-24 | S2/D8: Docker target does not widen caller/project directory authority. | PC-24 |
| G-25 | S2/D3: Manual start explicitly selects Grok with Claude installation default. | PC-25 |
| G-26 | S2/D3: Resume composition explicitly selects Grok. | PC-26 |
| G-27 | S2/D11: Live reuse cannot change home/cwd/target mapping. | PC-27 |
| G-28 | S2/D3: Agent edit cannot redirect occupied generation. | PC-28 |
| G-29 | S2/D7: Server compares canonical descriptor digest before launch. | PC-29 |
| G-30 | S2/D7: Bounded slug schema excludes invalid IDs. | PC-30 |
| G-31 | S2/D7: Host roots must be absolute Windows paths. | PC-31 |
| G-32 | S2/D7: Home/work/rules roots cannot overlap. | PC-32 |
| G-33 | S2/D7: POSIX mapping preserves casing and rejects noncanonical roots. | PC-33 |
| G-34 | S3/D1: Missing/null dockerGrokExecV1 refuses Docker launch. | PC-34 |
| G-35 | S3/D7: Direct runner request cannot bypass manifest binding. | PC-35 |
| G-36 | S3/D13: Runner rejects Docker+VerificationBinding without native custody. | PC-36 |
| G-37 | S3/D9: Arbitrary exe is not reinterpreted as Grok. | PC-37 |
| G-38 | S3/D9: Docker exec must allocate Linux TTY. | PC-38 |
| G-39 | S3/D9: Docker exec must keep stdin open. | PC-39 |
| G-40 | S3/D9: Tokens preserve spaces/quotes/metacharacters without a shell. | PC-40 |
| G-41 | S3/D10: Guest cwd and host process cwd remain distinct. | PC-41 |
| G-42 | S3/D8: Secret values reach env without command-line exposure. | PC-42 |
| G-43 | S3/D8: Logs/journal/sidecar/inspect output contain no secret values or full rules. | PC-43 |
| G-44 | S3/D8: Explicit empty cannot inherit container credential. | PC-44 |
| G-45 | S3/D8: Host daemon PATH/unrelated env/context never leak into guest. | PC-45 |
| G-46 | S3/D8: Case-equivalent Windows env names cannot become ambiguous Linux env. | PC-46 |
| G-47 | S3/D8: Target API origin and orchestration identity override stale agent env. | PC-47 |
| G-48 | S3/D8: Task cannot substitute target-owned GROK_HOME. | PC-48 |
| G-49 | S3/D5: Auth probe inspects bound host home before allocation. | PC-49 |
| G-50 | S3/D7: Correct dummy auth cannot excuse wrong host mount source. | PC-50 |
| G-51 | S3/D7: Correct source cannot excuse wrong guest destination. | PC-51 |
| G-52 | S3/D7: Guest cannot write runner rules. | PC-52 |
| G-53 | S3/D7: Tag equality cannot replace image digest equality. | PC-53 |
| G-54 | S3/D7: Container worker owner label must match. | PC-54 |
| G-55 | S3/D1: Linux target cannot launch into Windows engine. | PC-55 |
| G-56 | S3/D11: Paused/unknown observed state cannot admit exec. | PC-56 |
| G-57 | S3/D11: Automatic restart policy cannot resurrect worker outside journal. | PC-57 |
| G-58 | S3/D9: Budget includes Windows-quoted Docker wrapper bytes. | PC-58 |
| G-59 | S4/D6: Mapping uses path-component containment. | PC-59 |
| G-60 | S4/D6: Traversal cannot escape mapped root. | PC-60 |
| G-61 | S4/D6: Junction/reparse indirection cannot cross owned roots. | PC-61 |
| G-62 | S4/D10: Tail exact encoded Linux cwd/session; host decoy is ignored. | PC-62 |
| G-63 | S4/D10: GUID search requires exactly one match. | PC-63 |
| G-64 | S4/D11: Absent native history never authorizes fresh standing session. | PC-64 |
| G-65 | S4/D11: I/O-unavailable history cannot become fresh. | PC-65 |
| G-66 | S4/D10: Rules receipt/bootstrap/reread names guest path. | PC-66 |
| G-67 | S4/D10: Verify maps guest receipt to exact host file and hash. | PC-67 |
| G-68 | S4/D10: Old rules generation cannot be adopted as current. | PC-68 |
| G-69 | S4/D10: Rules expiry derives owned host path; no receipt-driven delete. | PC-69 |
| G-70 | S4/D10: Failed replacement leaves previous complete rules. | PC-70 |
| G-71 | S4/D10: Ordinary queued/Now/SendNow/expired-hold work waits for rules readiness. | PC-71 |
| G-72 | S4/D10: Assistant ack alone cannot release barrier. | PC-72 |
| G-73 | S4/D10: Wrong SHA in acknowledgement cannot release work. | PC-73 |
| G-74 | S4/D10: Ack before success or after failed TurnEnd cannot release work. | PC-74 |
| G-75 | S4/D10: Restart recovers same compact/resume trigger without duplicate read. | PC-75 |
| G-76 | S4/D10: Spilled brief exposes mapped guest path and full rendered contract. | PC-76 |
| G-77 | S4/D10: Spilled refinement exposes mapped guest path and full contract. | PC-77 |
| G-78 | S4/D10: Brief spill/mapping failure never types API-goal fallback. | PC-78 |
| G-79 | S4/D10: Refinement failure cannot type clipped-event fallback. | PC-79 |
| G-80 | S4/D10: Complete accepted refinement survives event/spill/enqueue cut. | PC-80 |
| G-81 | S4/D10: Different refinement identities cannot share spill file. | PC-81 |
| G-82 | S4/D10: Secondary queue spill is guest-readable and complete. | PC-82 |
| G-83 | S5/D11: No child before durable launch-pending journal commit. | PC-83 |
| G-84 | S5/D11: Two admissions cannot occupy one container. | PC-84 |
| G-85 | S5/D11: Idempotent replay attaches known host, never second exec. | PC-85 |
| G-86 | S5/D11: Old AcceptedStartedAt cannot stop new owner of same session. | PC-86 |
| G-87 | S5/D11: Same-name replacement never substitutes for bound resource. | PC-87 |
| G-88 | S5/D11: Docker ack/client exit is not container-stop proof. | PC-88 |
| G-89 | S5/D11: Uncertain final IO/transcript drain retains occupancy. | PC-89 |
| G-90 | S5/D11: Owned client must be terminated/reaped before release. | PC-90 |
| G-91 | S5/D11: Unavailable/timed-out daemon cannot free container. | PC-91 |
| G-92 | S5/D11: Proven client exit triggers exact-container cleanup. | PC-92 |
| G-93 | S5/D11: Failed launch cleans exact container before vacancy. | PC-93 |
| G-94 | S5/D11: Stall detection cannot kill target. | PC-94 |
| G-95 | S5/D2: Settlement cannot return Docker session to anonymous warm pool. | PC-95 |
| G-96 | S5/D11: Crash after reserve cannot imply vacancy. | PC-96 |
| G-97 | S5/D11: Crash after spawn before attached cannot launch again without cleanup. | PC-97 |
| G-98 | S5/D11: Restart after stop ack must inspect exact stopped state before release. | PC-98 |
| G-99 | S5/D11: Crash after stopped before release preserves identity and completes release once. | PC-99 |
| G-100 | S5/D11: Missing/corrupt journal plus existing owned container is unresolved. | PC-100 |
| G-101 | S5/D11: Adopt only exact PID/start identity with matching binding. | PC-101 |
| G-102 | S5/D7: Restart cannot retarget transcript/home from current manifest. | PC-102 |
| G-103 | S5/D11: HTTP readiness waits for Docker reconciliation. | PC-103 |
| G-104 | S5/D11: Restarted container is not the previous live exec. | PC-104 |
| G-105 | S5/D11: Shutdown cannot abandon live exec without journal or container stop. | PC-105 |
| G-106 | S6/D8: Guest callback uses target origin and same task/session identity. | PC-106 |
| G-107 | S6/D8: HTML 200 or unrelated JSON cannot qualify API reachability. | PC-107 |
| G-108 | S6/D10: Busy delegate/caller receives zero writes until eligible. | PC-108 |
| G-109 | S6/D10: Header/prefix/id/screen/Sent cannot count as delivered. | PC-109 |
| G-110 | S6/D10: Old complete prompt cannot confirm current attempt. | PC-110 |
| G-111 | S6/D10: Enqueue failure retains immutable producer obligation for recovery. | PC-111 |
| G-112 | S6/D10: Dropped wakeup/server crash cannot strand queue. | PC-112 |
| G-113 | S6/D10: Crash after ACP prompt before verdict reconciles without retyping. | PC-113 |
| G-114 | S6/D10: Internal rules ack is not task report/boot response. | PC-114 |
| G-115 | S6/D10: Terminal result and caller obligation persist together. | PC-115 |
| G-116 | S6/D10: Task success/Sent/header cannot confirm caller notification. | PC-116 |
| G-117 | S6/D10: Terminal caller note recovers same identity across queue failure. | PC-117 |
| G-118 | S6/D13: Missing/skip/failure/native-only evidence cannot enable worker. | PC-118 |
| G-119 | S3/D7: Unique instance label is independently required. | PC-119 |
| G-120 | S3/D11: Existing live exec cannot be treated as idle container. | PC-120 |
| G-121 | S4/D10: Ack for another refresh ID cannot release work. | PC-121 |
| G-122 | S4/D10: Ack from prior rules generation cannot release work. | PC-122 |
| G-123 | S5/D11: Equal timestamp on a different session cannot authorize stop. | PC-123 |
| G-124 | S2/D3: Migration cannot turn existing host agents/sessions into Docker workers. | PC-124 |
| G-125 | S1/D4: Worker build rejects unpinned base/tool artifact inputs. | PC-125 |
| G-126 | S3/D5: Missing OAuth cannot silently introduce metered credentials. | PC-126 |
| G-127 | S4/D10: Service recreation cannot extend failed-refresh deadline. | PC-127 |
| G-128 | S4/D10: Repeated refresh-caused compaction cannot produce an endless chain. | PC-128 |
| G-129 | S3/D13: Direct runner callers cannot launch a disabled worker. | PC-129 |
| G-130 | S3/D7: Descriptor is read-only and cannot start/reserve a worker. | PC-130 |
| G-131 | S2/D3: Accepted immutable binding survives server loss before launch. | PC-131 |
| G-132 | S3/D8: Task/session/agent token identity cannot be overwritten by agent env. | PC-132 |
| G-133 | S1/D5: Image configuration cannot bake runtime credentials. | PC-133 |
| G-134 | S3/D3: Docker binding is carried unchanged through launch DTO serialization. | PC-134 |
| G-135 | S4/D10: Active session retains its mapped rules artifact. | PC-135 |
| G-136 | S5/D11: Recovery cannot adopt a same-name replacement container. | PC-136 |
| G-137 | S2/D6: Only standalone Git checkout is admitted. | PC-137 |
| G-138 | S4/D10: Tool/user/quoted text cannot authorize rules readiness. | PC-138 |
| G-139 | S6/D10: Complete prompt on another generation cannot confirm current delivery. | PC-139 |
| G-140 | S6/D13: Healthy stale server/runner cannot qualify new Docker capability. | PC-140 |

### Positive controls

Mutation reports **break, intended red, restore, fresh-build green** after land. Code implements tests and runs V/R; ordinary Review judges them before land. One row means one independently compiling/parse-valid defect in the stated production guard, one exact method, and the decisive assertion shown. Configuration/Dockerfile mutations stay syntactically valid; a parser/compiler/fixture error is not red. Tests must arrange all unrelated prerequisites valid, and observe effects even if a refusal unexpectedly succeeds.

All new classes below reside at tests/{project}/{Application or Agents for Antiphon.Tests}/{class}.cs as listed in the command roster below; runner classes are directly under tests/Antiphon.SessionRunner.Tests. The observation names are the fixture contract in Inspection. PC-80 and the DL-2 variants of PC-111..PC-113 need VD-1's durable seam before Code-ready approval. They are specified here to expose the gap, not silently omitted.

| PC / break guard | Compiling defect | Exact method expected red | Decisive assertion |
|---|---|---|---|
| PC-1 / G-1 | Treat Enabled=false as true in resolver. | `DockerGrokWorkerConfigurationTests.Disabled_target_refuses` | `effects.LaunchCount.ShouldBe(0)` |
| PC-2 / G-2 | Skip provisioning checksum comparison. | `DockerGrokProvisioningTests.Artifact_checksum_is_required` | `effects.BuildCount.ShouldBe(0)` |
| PC-3 / G-3 | Remove the Dockerfile version-test command. | `DockerGrokProvisioningTests.Wrong_grok_version_refuses` | `buildContract.RequiresVersionCheck.ShouldBeTrue()` |
| PC-4 / G-4 | Add COPY of dummy-home to the tracked worker Dockerfile. | `DockerGrokProvisioningTests.Build_context_excludes_auth` | `buildContract.CopiesCredentialRoot.ShouldBeFalse()` |
| PC-5 / G-5 | Add removal of configured home in provisioning cleanup. | `DockerGrokProvisioningTests.Home_survives_recreate` | `File.ReadAllBytes(dummyAuthPath).ShouldBe(originalBytes)` |
| PC-6 / G-6 | Replace missing-source refusal with Directory.CreateDirectory. | `DockerGrokProvisioningTests.Missing_bind_source_refuses` | `effects.CreateCount.ShouldBe(0)` |
| PC-7 / G-7 | Skip instance-ID recheck before provisioning remove. | `DockerGrokProvisioningTests.Provisioning_checks_exact_identity` | `effects.RemoveIds.ShouldBeEmpty()` |
| PC-8 / G-8 | Add a 17280 published port to worker Compose. | `DockerGrokProvisioningTests.Worker_publishes_no_ports` | `compose.PublishedPorts.ShouldBeEmpty()` |
| PC-9 / G-9 | Set privileged=true in worker Compose. | `DockerGrokProvisioningTests.Worker_is_not_privileged` | `compose.Privileged.ShouldBeFalse()` |
| PC-10 / G-10 | Replace worker command with dockerd. | `DockerGrokProvisioningTests.Worker_does_not_start_dind` | `compose.StartsDockerDaemon.ShouldBeFalse()` |
| PC-11 / G-11 | Ignore target mismatch in registration script. | `DockerGrokProvisioningTests.Registration_collision_refuses` | `effects.AgentWrites.ShouldBe(0)` |
| PC-12 / G-12 | Set AlwaysOn=true in registration payload. | `DockerGrokProvisioningTests.Registration_is_passive` | `registered.AlwaysOn.ShouldBeFalse()` |
| PC-13 / G-13 | Set composed Docker binding to null. | `DockerGrokWorkerDispatchTests.Explicit_pin_keeps_target` | `launch.DockerWorkerId.ShouldBe(workerId)` |
| PC-14 / G-14 | Include Docker agent in anonymous selection predicate. | `DockerGrokWorkerDispatchTests.Unpinned_grok_stays_host` | `selected.DockerWorkerId.ShouldBeNull()` |
| PC-15 / G-15 | Remove Worktree admission rejection. | `DockerGrokWorkerSelectionTests.Worktree_refuses_before_allocation` | `effects.WorktreeCreates.ShouldBe(0)` |
| PC-16 / G-16 | Remove Docker+SourceLanding server rejection. | `DockerGrokWorkerSelectionTests.SourceLanding_refuses_before_allocation` | `effects.SessionCreates.ShouldBe(0)` |
| PC-17 / G-17 | Remove target kind check. | `DockerGrokWorkerSelectionTests.Wrong_kind_refuses` | `effects.SessionCreates.ShouldBe(0)` |
| PC-18 / G-18 | Remove PtyHost check. | `DockerGrokWorkerSelectionTests.Wrong_backend_refuses` | `effects.SessionCreates.ShouldBe(0)` |
| PC-19 / G-19 | Remove TuiProfileId check. | `DockerGrokWorkerSelectionTests.Managed_profile_refuses` | `effects.SessionCreates.ShouldBe(0)` |
| PC-20 / G-20 | Remove IsPoolDelegate check in AgentService. | `DockerGrokWorkerSelectionTests.Pool_registration_refuses` | `effects.AgentWrites.ShouldBe(0)` |
| PC-21 / G-21 | Remove Docker orchestrator rejection. | `DockerGrokWorkerSelectionTests.Orchestrator_refuses` | `effects.SessionCreates.ShouldBe(0)` |
| PC-22 / G-22 | Drop Docker exclusion from card candidate query. | `DockerGrokWorkerSelectionTests.Card_autopick_excludes_worker` | `selectedAgentIds.ShouldNotContain(workerAgentId)` |
| PC-23 / G-23 | Remove nonlocal-runner rejection. | `DockerGrokWorkerSelectionTests.Remote_runner_refuses` | `effects.LaunchCount.ShouldBe(0)` |
| PC-24 / G-24 | Bypass allowed-root authorization for Docker target. | `DockerGrokWorkerSelectionTests.Caller_roots_remain_enforced` | `effects.TaskCreates.ShouldBe(0)` |
| PC-25 / G-25 | Let target manual composition fall through to default. | `DockerGrokWorkerDispatchTests.Manual_start_uses_grok` | `launch.Kind.ShouldBe(AgentKind.Grok)` |
| PC-26 / G-26 | Remove target selection in resume resolution. | `DockerGrokWorkerDispatchTests.Resume_uses_grok` | `launch.Kind.ShouldBe(AgentKind.Grok)` |
| PC-27 / G-27 | Skip immutable binding comparison for reuse. | `DockerGrokWorkerDispatchTests.Reuse_requires_unchanged_binding` | `effects.InputCount.ShouldBe(0)` |
| PC-28 / G-28 | Remove occupied-target edit guard. | `DockerGrokWorkerSelectionTests.Occupied_target_cannot_be_edited` | `stored.DockerWorkerId.ShouldBe(originalWorkerId)` |
| PC-29 / G-29 | Ignore descriptor/config digest mismatch. | `DockerGrokWorkerConfigurationTests.Server_digest_mismatch_refuses` | `effects.LaunchCount.ShouldBe(0)` |
| PC-30 / G-30 | Skip worker ID validation. | `DockerGrokWorkerConfigurationTests.Worker_id_grammar_refuses` | `validation.Accepted.ShouldBeFalse()` |
| PC-31 / G-31 | Accept relative host root. | `DockerGrokWorkerConfigurationTests.Host_roots_must_be_absolute` | `validation.Accepted.ShouldBeFalse()` |
| PC-32 / G-32 | Remove root overlap validation. | `DockerGrokWorkerConfigurationTests.Host_roots_are_distinct` | `validation.Accepted.ShouldBeFalse()` |
| PC-33 / G-33 | Skip guest normalization validation. | `DockerGrokWorkerConfigurationTests.Guest_paths_are_normalized` | `validation.Accepted.ShouldBeFalse()` |
| PC-34 / G-34 | Skip Docker capability gate. | `DockerGrokRunnerWireTests.Old_runner_refuses_before_post` | `http.LaunchPosts.ShouldBe(0)` |
| PC-35 / G-35 | Skip runner digest comparison. | `DockerGrokPreflightTests.Direct_request_revalidates_digest` | `effects.ExecCount.ShouldBe(0)` |
| PC-36 / G-36 | Remove runner verification-binding rejection. | `DockerGrokPreflightTests.Verification_binding_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-37 / G-37 | Accept unrecognized logical exe. | `DockerGrokPreflightTests.Custom_executable_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-38 / G-38 | Remove -t from planner tokens. | `DockerGrokLaunchTests.Linux_exec_requests_tty` | `argv.ShouldContain("-t")` |
| PC-39 / G-39 | Remove -i from planner tokens. | `DockerGrokLaunchTests.Linux_exec_keeps_stdin` | `argv.ShouldContain("-i")` |
| PC-40 / G-40 | Join logical argv then split on spaces. | `DockerGrokLaunchTests.Argv_boundaries_survive` | `observedChildArgs.ShouldBe(expectedArgs)` |
| PC-41 / G-41 | Use host cwd for --workdir. | `DockerGrokLaunchTests.Guest_cwd_is_separate` | `observedGuestCwd.ShouldBe("/work/sub dir")` |
| PC-42 / G-42 | Emit --env NAME=value for sentinel. | `DockerGrokLaunchTests.Secret_values_stay_off_argv` | `observed.ArgvContainsSentinel.ShouldBeFalse()` |
| PC-43 / G-43 | Log merged environment in launch planner. | `DockerGrokLaunchTests.Diagnostics_exclude_values` | `observed.DiagnosticsContainSentinel.ShouldBeFalse()` |
| PC-44 / G-44 | Omit empty override from Docker env arguments. | `DockerGrokLaunchTests.Empty_override_clears_container_value` | `observed.GuestVariableIsEmpty.ShouldBeTrue()` |
| PC-45 / G-45 | Forward all process environment names. | `DockerGrokLaunchTests.Export_is_allowlisted` | `observed.GuestNames.ShouldNotContain("C575_HOST_ONLY")` |
| PC-46 / G-46 | Skip case-collision validation. | `DockerGrokLaunchTests.Env_case_collisions_refuse` | `effects.ExecCount.ShouldBe(0)` |
| PC-47 / G-47 | Apply installation origin after target composition. | `DockerGrokWorkerDispatchTests.Target_origin_wins_trusted_merge` | `launch.Env["ANTIPHON_API"].ShouldBe(workerOrigin)` |
| PC-48 / G-48 | Ignore task GROK_HOME conflict. | `DockerGrokWorkerSelectionTests.Conflicting_home_override_refuses` | `effects.SessionCreates.ShouldBe(0)` |
| PC-49 / G-49 | Probe default host home instead of worker home. | `DockerGrokWorkerDispatchTests.Mapped_home_is_probed` | `effects.SessionCreates.ShouldBe(0)` |
| PC-50 / G-50 | Skip mount source comparison. | `DockerGrokPreflightTests.Mount_source_mismatch_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-51 / G-51 | Skip destination comparison. | `DockerGrokPreflightTests.Mount_destination_mismatch_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-52 / G-52 | Ignore RW flag on rules mount. | `DockerGrokPreflightTests.Rules_mount_must_be_readonly` | `effects.ExecCount.ShouldBe(0)` |
| PC-53 / G-53 | Compare image tag only. | `DockerGrokPreflightTests.Image_digest_mismatch_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-54 / G-54 | Ignore worker owner label comparison, retain instance check. | `DockerGrokPreflightTests.Owner_labels_must_match` | `effects.ExecCount.ShouldBe(0)` |
| PC-55 / G-55 | Skip engine OS validation. | `DockerGrokPreflightTests.Nonlinux_engine_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-56 / G-56 | Treat paused/unknown state as idle, retain prior-exec check. | `DockerGrokPreflightTests.Unexpected_state_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-57 / G-57 | Skip restart-policy validation. | `DockerGrokPreflightTests.Restart_policy_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-58 / G-58 | Measure logical Grok argv only. | `DockerGrokLaunchTests.Final_windows_budget_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-59 / G-59 | Use string prefix instead of separator-bounded containment. | `DockerGrokTranscriptTests.Sibling_prefix_cannot_map` | `mapping.Accepted.ShouldBeFalse()` |
| PC-60 / G-60 | Drop resolved containment recheck for dotdot. | `DockerGrokTranscriptTests.Dotdot_cannot_escape` | `mapping.Accepted.ShouldBeFalse()` |
| PC-61 / G-61 | Skip reparse inspection. | `DockerGrokTranscriptTests.Reparse_escape_cannot_map` | `mapping.Accepted.ShouldBeFalse()` |
| PC-62 / G-62 | Compute transcript path from host cwd. | `DockerGrokTranscriptTests.Linux_cwd_selects_own_transcript` | `ownPrompts.ShouldHaveSingleItem().Text.ShouldBe(fullPrompt)` |
| PC-63 / G-63 | Return first native-session match. | `DockerGrokTranscriptTests.Ambiguous_native_id_refuses` | `binding.Accepted.ShouldBeFalse()` |
| PC-64 / G-64 | Convert missing Docker resume to --session-id. | `DockerGrokTranscriptTests.Missing_resume_preserves_history` | `effects.ExecCount.ShouldBe(0)` |
| PC-65 / G-65 | Catch storage error and return native-missing fallback. | `DockerGrokTranscriptTests.Unreadable_resume_preserves_history` | `effects.ExecCount.ShouldBe(0)` |
| PC-66 / G-66 | Return physical Windows receipt path. | `DockerGrokRulesTests.Receipt_names_guest_path` | `receipt.Path.ShouldBe(expectedGuestPath)` |
| PC-67 / G-67 | Skip mapped-file hash verification. | `DockerGrokRulesTests.Receipt_verifies_bound_bytes` | `verified.ShouldBeFalse()` |
| PC-68 / G-68 | Skip generation comparison. | `DockerGrokRulesTests.Receipt_generation_must_match` | `verified.ShouldBeFalse()` |
| PC-69 / G-69 | Delete caller receipt path directly. | `DockerGrokRulesTests.Expiry_cannot_follow_arbitrary_receipt` | `File.Exists(outsideSentinel).ShouldBeTrue()` |
| PC-70 / G-70 | Delete old file before atomic replacement. | `DockerGrokRulesTests.Atomic_replace_retains_previous_rules` | `File.ReadAllBytes(hostRules).ShouldBe(previousBytes)` |
| PC-71 / G-71 | Return false from queue's Grok rules barrier predicate. | `DockerGrokDeliveryRecoveryTests.Rules_barrier_blocks_each_entry` | `recipient.SubmittedOrdinary.ShouldBeEmpty()` |
| PC-72 / G-72 | Ignore missing owning UserPrompt during refresh validation. | `DockerGrokDeliveryRecoveryTests.Rules_ack_needs_own_prompt` | `rules.State.ShouldNotBe(GrokRulesState.Ready)` |
| PC-73 / G-73 | Ignore only acknowledgement SHA comparison. | `DockerGrokDeliveryRecoveryTests.Rules_ack_needs_matching_hash` | `rules.State.ShouldNotBe(GrokRulesState.Ready)` |
| PC-74 / G-74 | Mark ready on ack before successful TurnEnd. | `DockerGrokDeliveryRecoveryTests.Rules_ack_needs_successful_end` | `recipient.SubmittedOrdinary.ShouldBeEmpty()` |
| PC-75 / G-75 | Mint new refresh key during scan. | `DockerGrokDeliveryRecoveryTests.Rules_recovery_is_unique` | `rulesRows.Count.ShouldBe(1)` |
| PC-76 / G-76 | Leave host path in Docker brief pointer. | `DockerGrokBriefPointerTests.Brief_pointer_is_guest_readable` | `guestRead.Bytes.ShouldBe(renderedBriefBytes)` |
| PC-77 / G-77 | Leave host path in Docker refinement pointer. | `DockerGrokBriefPointerTests.Refinement_pointer_is_guest_readable` | `guestRead.Bytes.ShouldBe(renderedRefinementBytes)` |
| PC-78 / G-78 | Enable existing API-goal fallback on Docker spill failure. | `DockerGrokBriefPointerTests.Brief_spill_failure_refuses` | `recipient.SubmittedBodies.ShouldBeEmpty()` |
| PC-79 / G-79 | Enable timeline fallback on Docker spill failure. | `DockerGrokBriefPointerTests.Refinement_spill_failure_refuses` | `recipient.SubmittedBodies.ShouldBeEmpty()` |
| PC-80 / G-80 | Omit durable full-body write while saving Refined event. | `DockerGrokDeliveryRecoveryTests.Refinement_full_body_survives_restart` | `recovered.FullBody.ShouldBe(originalRenderedBody)` |
| PC-81 / G-81 | Use yyyyMMddHHmmss alone for refinement filename. | `DockerGrokBriefPointerTests.Same_second_refinements_do_not_overwrite` | `firstGuestRead.Bytes.ShouldBe(firstBodyBytes)` |
| PC-82 / G-82 | Leave physical inbox path in Docker re-spill pointer. | `DockerGrokBriefPointerTests.Queue_respill_keeps_guest_path` | `guestRead.Bytes.ShouldBe(originalWireBytes)` |
| PC-83 / G-83 | Spawn before journal write succeeds. | `DockerGrokLifecycleTests.Reservation_precedes_exec` | `effects.ExecCount.ShouldBe(0)` |
| PC-84 / G-84 | Remove worker reservation lock. | `DockerGrokLifecycleTests.Concurrent_start_is_exclusive` | `effects.ExecCount.ShouldBe(1)` |
| PC-85 / G-85 | Always spawn on identical live request. | `DockerGrokLifecycleTests.Same_generation_reuses_host` | `effects.ExecCount.ShouldBe(1)` |
| PC-86 / G-86 | Remove AcceptedStartedAt equality, retain session ID check. | `DockerGrokLifecycleTests.Stop_checks_generation` | `effects.StopIds.ShouldBeEmpty()` |
| PC-87 / G-87 | Resolve stop by mutable container name. | `DockerGrokLifecycleTests.Stop_checks_full_container_id` | `effects.StopIds.ShouldBeEmpty()` |
| PC-88 / G-88 | Release occupancy immediately on stop ack. | `DockerGrokLifecycleTests.Stop_requires_daemon_stopped` | `lease.IsVacant.ShouldBeFalse()` |
| PC-89 / G-89 | Skip IO-drain gate. | `DockerGrokLifecycleTests.Stop_requires_io_drain` | `lease.IsVacant.ShouldBeFalse()` |
| PC-90 / G-90 | Skip host-reap gate. | `DockerGrokLifecycleTests.Stop_requires_host_reaped` | `lease.IsVacant.ShouldBeFalse()` |
| PC-91 / G-91 | Mark lease stopped in stop-error handler. | `DockerGrokLifecycleTests.Daemon_failure_keeps_occupancy` | `lease.IsVacant.ShouldBeFalse()` |
| PC-92 / G-92 | Handle natural exit as host-only cleanup. | `DockerGrokLifecycleTests.Natural_exit_stops_container` | `effects.StopIds.ShouldBe([boundContainerId])` |
| PC-93 / G-93 | Skip container compensation after spawn failure. | `DockerGrokLifecycleTests.Failed_launch_compensates` | `effects.StopIds.ShouldBe([boundContainerId])` |
| PC-94 / G-94 | Invoke container stop from stall handling. | `DockerGrokLifecycleTests.Stall_does_not_stop` | `effects.StopIds.ShouldBeEmpty()` |
| PC-95 / G-95 | Set pool eligibility on target settlement. | `DockerGrokWorkerDispatchTests.Settlement_keeps_named_owner` | `agent.IsPoolDelegate.ShouldBeFalse()` |
| PC-96 / G-96 | Treat reserved journal as expired vacancy. | `DockerGrokRecoveryTests.Reserve_crash_reconciles_before_reuse` | `effects.ExecCount.ShouldBe(0)` |
| PC-97 / G-97 | Treat launch-pending with missing host as free. | `DockerGrokRecoveryTests.Spawn_crash_does_not_double_exec` | `effects.ExecCount.ShouldBe(1)` |
| PC-98 / G-98 | Trust persisted stop-pending as stopped. | `DockerGrokRecoveryTests.Stop_ack_crash_rechecks_daemon` | `lease.IsVacant.ShouldBeFalse()` |
| PC-99 / G-99 | Skip recovered stopped-journal release. | `DockerGrokRecoveryTests.Stopped_crash_releases_idempotently` | `lease.IsVacant.ShouldBeTrue()` |
| PC-100 / G-100 | Reset journal on read failure. | `DockerGrokRecoveryTests.Lost_or_corrupt_journal_holds` | `effects.ExecCount.ShouldBe(0)` |
| PC-101 / G-101 | Ignore host process start-time mismatch. | `DockerGrokRecoveryTests.Adoption_checks_host_identity` | `adopted.ShouldBeFalse()` |
| PC-102 / G-102 | Replace persisted mapping with changed manifest. | `DockerGrokRecoveryTests.Adoption_checks_mapping_digest` | `adopted.ShouldBeFalse()` |
| PC-103 / G-103 | Start HTTP before recovery completion. | `DockerGrokRecoveryTests.Recovery_precedes_readiness` | `http.ReadyWhileRecoveryHeld.ShouldBeFalse()` |
| PC-104 / G-104 | Adopt old generation from container Running alone. | `DockerGrokRecoveryTests.Container_restart_requires_new_generation` | `adopted.ShouldBeFalse()` |
| PC-105 / G-105 | Delete attached lease during runtime disposal. | `DockerGrokRecoveryTests.Shutdown_retains_or_stops_owner` | `evidence.HasDurableOwnerOrStoppedContainer.ShouldBeTrue()` |
| PC-106 / G-106 | Use installation loopback origin in Docker child env. | `DockerGrokDelegateAcceptanceTests.Callback_reaches_bound_api` | `api.ReceiptsFor(taskId, sessionId).Count.ShouldBe(1)` |
| PC-107 / G-107 | Treat any 2xx callback as qualification success. | `DockerGrokDelegateAcceptanceTests.Spa_200_is_not_callback_acceptance` | `qualification.CallbackAccepted.ShouldBeFalse()` |
| PC-108 / G-108 | Bypass queue working-state predicate for Docker target. | `DockerGrokDeliveryRecoveryTests.Busy_recipient_waits` | `recipient.WritesBeforeTurnEnd.ShouldBe(0)` |
| PC-109 / G-109 | Use header match instead of complete-body match at acceptance. | `DockerGrokDeliveryRecoveryTests.Complete_prompt_is_required` | `receipt.Accepted.ShouldBeFalse()` |
| PC-110 / G-110 | Ignore LastDeliveryBaselineSequence and attempt timestamp floor. | `DockerGrokDeliveryRecoveryTests.Receipt_floor_is_required` | `receipt.Accepted.ShouldBeFalse()` |
| PC-111 / G-111 | Mark obligation complete before queue insert. | `DockerGrokDeliveryRecoveryTests.Queue_insert_failure_keeps_obligation` | `recipient.CompletePromptsFor(deliveryId).Count.ShouldBe(1)` |
| PC-112 / G-112 | Exclude target pending rows from recovery discovery. | `DockerGrokDeliveryRecoveryTests.Committed_queue_recovers_without_wakeup` | `recipient.CompletePromptsFor(deliveryId).Count.ShouldBe(1)` |
| PC-113 / G-113 | Skip transcript late-confirm in interrupted attempt recovery. | `DockerGrokDeliveryRecoveryTests.Accepted_prompt_is_not_retyped` | `recipient.SubmittedBodies.Count.ShouldBe(1)` |
| PC-114 / G-114 | Remove internal-rules-turn exclusion in settlement. | `DockerGrokDeliveryRecoveryTests.Rules_turn_cannot_settle_task` | `task.Status.ShouldNotBe(AgentTaskStatus.Succeeded)` |
| PC-115 / G-115 | Omit TaskCompletion creation during terminal save. | `DockerGrokDeliveryRecoveryTests.Completion_obligation_is_atomic` | `caller.CompletePromptsFor(notificationId).Count.ShouldBe(1)` |
| PC-116 / G-116 | Confirm notification from queue Sent alone. | `DockerGrokDeliveryRecoveryTests.Caller_receipt_requires_whole_note` | `notification.ConfirmedAt.ShouldBeNull()` |
| PC-117 / G-117 | Skip terminal notifications in recovery scanner. | `DockerGrokDeliveryRecoveryTests.Completion_enqueue_recovers_once` | `caller.CompletePromptsFor(notificationId).Count.ShouldBe(1)` |
| PC-118 / G-118 | Treat unavailable live-canary result as passed. | `DockerGrokProvisioningTests.Unavailable_canary_cannot_qualify` | `qualification.CanEnable.ShouldBeFalse()` |
| PC-119 / G-119 | Ignore only the instance label comparison. | `DockerGrokPreflightTests.Instance_label_must_match` | `effects.ExecCount.ShouldBe(0)` |
| PC-120 / G-120 | Ignore prior exec observation while retaining paused check. | `DockerGrokPreflightTests.Prior_exec_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-121 / G-121 | Ignore only ack ID comparison. | `DockerGrokDeliveryRecoveryTests.Rules_ack_needs_matching_id` | `rules.State.ShouldNotBe(GrokRulesState.Ready)` |
| PC-122 / G-122 | Ignore only ack generation comparison. | `DockerGrokDeliveryRecoveryTests.Rules_ack_needs_matching_generation` | `rules.State.ShouldNotBe(GrokRulesState.Ready)` |
| PC-123 / G-123 | Compare AcceptedStartedAt but omit SessionId. | `DockerGrokLifecycleTests.Stop_checks_session_id` | `effects.StopIds.ShouldBeEmpty()` |
| PC-124 / G-124 | Set new target default to a configured worker ID in migration. | `DockerGrokWorkerConfigurationTests.Migration_leaves_host_rows_null` | `legacy.DockerWorkerId.ShouldBeNull()` |
| PC-125 / G-125 | Remove base digest from worker build contract. | `DockerGrokProvisioningTests.Base_and_tools_are_pinned` | `buildContract.AllInputsPinned.ShouldBeTrue()` |
| PC-126 / G-126 | Inject dummy XAI_API_KEY in worker env on absent store. | `DockerGrokWorkerDispatchTests.No_implicit_api_key_fallback` | `effects.SessionCreates.ShouldBe(0)` |
| PC-127 / G-127 | Reset refresh deadline to now during recovery. | `DockerGrokDeliveryRecoveryTests.Rules_deadline_survives_restart` | `rules.State.ShouldBe(GrokRulesState.Failed)` |
| PC-128 / G-128 | Always enqueue another follow-on refresh. | `DockerGrokDeliveryRecoveryTests.Rules_followon_is_bounded` | `rules.FailureCode.ShouldBe("refresh_loop")` |
| PC-129 / G-129 | Ignore runner manifest Enabled flag. | `DockerGrokPreflightTests.Direct_disabled_target_refuses` | `effects.ExecCount.ShouldBe(0)` |
| PC-130 / G-130 | Start stopped container while serving descriptor. | `DockerGrokPreflightTests.Descriptor_has_no_launch_effects` | `effects.StartCount.ShouldBe(0)` |
| PC-131 / G-131 | Save session without Docker binding snapshot. | `DockerGrokWorkerDispatchTests.Binding_persists_before_launch` | `reloaded.Binding.ShouldBe(acceptedBinding)` |
| PC-132 / G-132 | Merge agent ANTIPHON_TASK_ID after trusted identity. | `DockerGrokWorkerDispatchTests.Task_identity_wins_untrusted_env` | `launch.Env["ANTIPHON_TASK_ID"].ShouldBe(taskId.ToString())` |
| PC-133 / G-133 | Add synthetic XAI_API_KEY ENV to worker Dockerfile. | `DockerGrokProvisioningTests.Image_env_excludes_credentials` | `buildContract.HasCredentialEnv.ShouldBeFalse()` |
| PC-134 / G-134 | Drop binding when creating RunnerLaunchRequest. | `DockerGrokRunnerWireTests.Binding_survives_http_roundtrip` | `received.Binding.ShouldBe(expectedBinding)` |
| PC-135 / G-135 | Remove live-session expiry refusal. | `DockerGrokRulesTests.Active_rules_cannot_expire` | `File.Exists(hostRules).ShouldBeTrue()` |
| PC-136 / G-136 | Resolve recovery container by name and skip ID comparison. | `DockerGrokRecoveryTests.Adoption_checks_full_container_id` | `adopted.ShouldBeFalse()` |
| PC-137 / G-137 | Skip .git-file/core.worktree portability rejection. | `DockerGrokWorkerSelectionTests.Linked_checkout_refuses` | `effects.SessionCreates.ShouldBe(0)` |
| PC-138 / G-138 | Accept ack from ToolResult as assistant text. | `DockerGrokDeliveryRecoveryTests.Rules_ack_must_be_assistant_text` | `rules.State.ShouldNotBe(GrokRulesState.Ready)` |
| PC-139 / G-139 | Ignore persisted delivery-generation comparison. | `DockerGrokDeliveryRecoveryTests.Receipt_requires_bound_generation` | `receipt.Accepted.ShouldBeFalse()` |
| PC-140 / G-140 | Skip desired/observed build and capability comparison. | `DockerGrokProvisioningTests.Loaded_build_identity_is_required` | `qualification.CanEnable.ShouldBeFalse()` |

Post-land PCs use local inherited children and scripted Docker I/O only. No SourceLanding snapshot is mounted into Docker Desktop, sent as a build context, or exposed to any pre-existing daemon/executor. This also keeps post-land failures deterministic. Database-only fixture traffic carries test data, never snapshot access. Real Docker/CLI/image assertions run as ordinary qualification (V-7/V-8) on the Code checkout with dedicated disposable directories; V-9 is separately commissioned. A fake-only green PC cannot certify Linux execution.

The local script/engine substitute is required for lifecycle PCs even when their native companion leaves a real grandchild. For example PC-88 must reach its occupancy assertion with stop acknowledged but inspect still Running; it must not time out during fixture creation. Recovery tests start from independently persisted historical setup so a mutation of current admission cannot prevent the target recovery branch from being reached. Mutation never weakens tests, raises timeouts or edits production repairs in the snapshot. A survivor or unavailable fixture becomes evidence for caller triage.


### Out of scope

- Remote phone-home/enrollment (CARD-0490), Linux runner/POSIX PTY port (CARD-0038), arbitrary sibling-container descendant accounting, anonymous fleet scheduling and Docker-backed SourceLanding remain excluded by D-1/D-2/D-11/D-12/D-13. Socket authority is accepted; these tests do not claim containment.
- General Linux build SDK support, repository-wide Windows script portability, checkout synchronization and credential transfer are not v0 claims. No OAuth home is copied or printed. Only dummy auth bytes may be compared or hashed.
- General Grok model-policy endurance across two compactions remains the earlier rules feature's qualification work. V-9 requires the Docker path's native compact/reread once; if 1.0.34 cannot produce observable compaction, report that gate unavailable and return to Plan rather than infer it from the 1.0.13 fixture.
- No live stack restart, model spend, login, auth inspection or Docker operation is authorized/performed by this TestDesign dispatch. Later Code/live commissions use the existing activation/custody runbooks. Feature remains disabled if required qualification is unavailable.
- SourceLanding PCs cannot execute through a live/pre-existing Docker daemon; deterministic local substitutes are named above. Native ordinary evidence and scripted mutation evidence remain distinct.

### Cost

All figures are **estimated execution floors**, not measured timings or timeout increases. Test authoring, finding repair, ordinary Review reading and operator login/wait time are additional. The unresolved VD-1 seam must be fixed in the plan before commissioning these floors. Counts are method contracts; parameterized busy/cut/boundary rows increase executed case counts and must be reported from fresh TRX.

| Code ordinary verification | Minutes |
|---|---:|
| Setup/build: initial .NET graph 8 + isolated DB/fixture setup 5 + pinned image/artifact/native fixture preparation 22 | 35 |
| Unit lane including configuration/planner/file boundaries | 4 |
| V-1 scripts/provisioning | 4 |
| V-2 selection/dispatch/migration | 11 |
| V-3/V-4 HTTP runner/path/rules additions | 9 |
| V-5/V-10 producer-to-recipient and all busy/cut matrices | 38 |
| V-6 deterministic lifecycle/restart | 11 |
| R-1 through R-9 named regression methods/classes | 30 |
| V-7/V-8 disposable native Docker and real CLI/synthetic provider | 20 |
| V-9 separately commissioned live qualification | 12 |
| **V/R run floor** | **139** |
| **Code setup/build + V/R floor** | **174** |

Mutation groups are exhaustive and disjoint. P=15 script/configuration controls; U=45 file/planner/preflight/configuration controls (C/L/F/T/U aliases in the roster); I=80 application/queue/lifecycle/HTTP controls (remaining aliases). There are 140 total PCs. Each precise method includes all of its declared variants; PC execution never widens to a class.

| Mutation work | Calculation | Minutes |
|---|---|---:|
| Snapshot verification, discovery, initial build and evidence setup | fixed | 8 |
| Exact-method initial green at landed SHA | 15 x 0.25 + 45 x 0.25 + 80 x 0.85 | 83 |
| P red/restore/green cycles | 15 x (0.15 parse/build + 0.25 red + 0.10 restore + 0.25 green + 0.25 evidence) | 15 |
| U red/restore/green cycles | 45 x (0.50 red build + 0.25 red + 0.05 restore + 0.45 fresh green build + 0.25 green) | 67.5 |
| I red/restore/green cycles | 80 x (0.60 red build + 0.85 red + 0.10 restore + 0.60 fresh green build + 0.85 green) | 240 |
| **Every PC red/restore/green floor** | 15 + 67.5 + 240 | **322.5** |
| **Mutation floor** | 8 + 83 + 322.5 | **413.5** |
| **Total verification floor** | Code 174 + Mutation 413.5 | **587.5 (9 h 47 m 30 s)** |

Savings credited: **0 minutes**. No benchmark was run in this document stage, so caching, combined filters or concurrency do not justify a deduction from the serial floor. Method scoping is mandatory; batching may save builds only after Code identifies genuinely separate files/methods. No parallel-shard savings are budgeted for a SourceLanding snapshot. Do not reuse the obsolete 25.5-minute full-suite figure as this task's cost. Re-measure with expanded counts at Code/Mutation and record actuals.

**Executable selection contract.** Use dotnet run, never dotnet test. Build into producer-owned bin-c575/ with a forward slash; commit before substantial runs and keep source frozen. These commands are for implemented tests, not a claim that discovery currently finds them. A new native class belongs to Integration with an explicit native/live opt-in; all other new classes are Unit xor Integration according to the fixture they require.

| Alias | New test file | Project | Ordinary class filter |
|---|---|---|---|
| P | `tests/Antiphon.Tests/Application/DockerGrokProvisioningTests.cs` | Antiphon.Tests | `/*/*/DockerGrokProvisioningTests/*` |
| C | `tests/Antiphon.Tests/Application/DockerGrokWorkerConfigurationTests.cs` | Antiphon.Tests | `/*/*/DockerGrokWorkerConfigurationTests/*` |
| S | `tests/Antiphon.Tests/Application/DockerGrokWorkerSelectionTests.cs` | Antiphon.Tests | `/*/*/DockerGrokWorkerSelectionTests/*` |
| D | `tests/Antiphon.Tests/Application/DockerGrokWorkerDispatchTests.cs` | Antiphon.Tests | `/*/*/DockerGrokWorkerDispatchTests/*` |
| W | `tests/Antiphon.Tests/Agents/DockerGrokRunnerWireTests.cs` | Antiphon.Tests | `/*/*/DockerGrokRunnerWireTests/*` |
| L | `tests/Antiphon.SessionRunner.Tests/DockerGrokLaunchTests.cs` | Antiphon.SessionRunner.Tests | `/*/*/DockerGrokLaunchTests/*` |
| F | `tests/Antiphon.SessionRunner.Tests/DockerGrokPreflightTests.cs` | Antiphon.SessionRunner.Tests | `/*/*/DockerGrokPreflightTests/*` |
| T | `tests/Antiphon.SessionRunner.Tests/DockerGrokTranscriptTests.cs` | Antiphon.SessionRunner.Tests | `/*/*/DockerGrokTranscriptTests/*` |
| U | `tests/Antiphon.SessionRunner.Tests/DockerGrokRulesTests.cs` | Antiphon.SessionRunner.Tests | `/*/*/DockerGrokRulesTests/*` |
| B | `tests/Antiphon.Tests/Application/DockerGrokBriefPointerTests.cs` | Antiphon.Tests | `/*/*/DockerGrokBriefPointerTests/*` |
| Y | `tests/Antiphon.SessionRunner.Tests/DockerGrokLifecycleTests.cs` | Antiphon.SessionRunner.Tests | `/*/*/DockerGrokLifecycleTests/*` |
| Z | `tests/Antiphon.SessionRunner.Tests/DockerGrokRecoveryTests.cs` | Antiphon.SessionRunner.Tests | `/*/*/DockerGrokRecoveryTests/*` |
| E | `tests/Antiphon.Tests/Application/DockerGrokDelegateAcceptanceTests.cs` | Antiphon.Tests | `/*/*/DockerGrokDelegateAcceptanceTests/*` |
| Q | `tests/Antiphon.Tests/Application/DockerGrokDeliveryRecoveryTests.cs` | Antiphon.Tests | `/*/*/DockerGrokDeliveryRecoveryTests/*` |

Native-only additions are tests/Antiphon.SessionRunner.Tests/DockerGrokNativeTransportTests.cs, tests/Antiphon.SessionRunner.Tests/DockerGrokLiveCanaryTests.cs and tests/Antiphon.Tests/Application/DockerGrokNativeDelegateAcceptanceTests.cs. They use the inspected LocalHttpRunner/GrokRulesDispatchAcceptance fixtures as their nearest precedent.

The following foreground helper runs one specified selection with a fresh result directory. Read its TRX and reconcile every intended method/variant; exit zero, --list-tests, skipped cases or zero discovery is not evidence.

```powershell
function Invoke-C575Selection {
    param([string]$Project, [string]$Class, [string]$Method = '*')
    $c575Result = Join-Path '.antiphon' ('c575-' + [guid]::NewGuid().ToString('N'))
    & dotnet run --project ('tests/' + $Project) --property:OutputPath=bin-c575/ -- --treenode-filter ("/*/*/" + $Class + "/" + $Method) --report-trx --report-trx-filename run.trx --results-directory $c575Result
    if ($LASTEXITCODE -ne 0) { throw "C575 selection failed: $Class/$Method; $c575Result" }
}
& dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c575/ --nologo
if ($LASTEXITCODE -ne 0) { throw 'Application test build failed' }
& dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c575/ --nologo
if ($LASTEXITCODE -ne 0) { throw 'Runner test build failed' }
Invoke-C575Selection 'Antiphon.Tests' '*' '*[Category=Unit]'
Invoke-C575Selection 'Antiphon.SessionRunner.Tests' '*' '*[Category=Unit]'
```

Run every roster row and R-1..R-9 selection through that helper, sequentially by project. For semicolon-separated R methods invoke each exact method, or the documented parenthesized method OR syntax and verify each TRX entry. The Unit invocation plus named integration rows is the default floor; do not rerun Unit-only roster classes unnecessarily when the Unit TRX already proves every intended method. Do not co-schedule Antiphon.Tests with Antiphon.Agents.Pty.Tests.

For disposable native selection, the new fixture requires ANTIPHON_DOCKER_GROK_TESTS=1 and ANTIPHON_HEADED_TESTS=1, an owned manifest supplied via ANTIPHON_C575_TEST_MANIFEST, plus both synthetic provider redirects verified by the fixture. Select the exact V-7/V-8 methods from their native-only classes. Live selection additionally requires ANTIPHON_DOCKER_GROK_LIVE=1 and ANTIPHON_C575_LIVE_MANIFEST; a flag is an execution gate, not substitute authorization. The verifier contract is -Mode Offline/Native/Live -ManifestPath -EvidenceRoot, with unavailable prerequisites returning an explicit non-qualified result and nonzero exit for requested qualification.

For each PC derive the project/class/exact method from its roster/table, run only `/*/*/DockerGrokWorkerConfigurationTests/Disabled_target_refuses` (PC-1 example) on initial green, red and restored green. No class wildcard in Mutation. Use a unique producer-owned output path and the caller-assigned external evidence root. Apply the one specified production defect, freshly build, require its exact assertion failure, restore exact bytes and refresh timestamps/rebuild, then require the same method green. Keep diff, tested SHA, nonzero counts, red assertion and restored-green TRX per PC. Do not commit/push sourced snapshot amendments. Retain exact output ownership inventory; remove only verified task-owned bin-c575 directories before finishing Code, and follow sourced restoration/output-record rules for Mutation.

**Handoff audit:** inspected bodies/fixtures are listed; guards=140, mapped=140, missing guard-to-PC mappings=0, duplicate PC mappings=0. All 140 mutation recipes have exact methods and decisive assertions; new tests are pending implementation. **Code-ready executable-seam gate is not satisfied:** PC-80 and DL-2 variants of PC-111..PC-113 depend on the missing VD-1 persistence/recovery contract, with PC-81/PC-82 affected by its file/queue choice. This is the reason for next: plan. Do not report all PCs executable or delivery accepted until that amendment is reviewed. There is no outstanding human choice and no test/build/live success claim from this stage.
