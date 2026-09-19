# CARD-0575: desktop Docker Grok worker v0

Date: 2026-09-19. Stage: Plan. Task: `ba318e38`. Source tree examined: `bb7b75353f76ca940265e5ef77bb64447100b027`.

VD-1 amendment: 2026-09-19, Plan task `0d678e51`, against TestDesign commit `c391749673d7b08bc37d5a5c7d52760543707e77`. D-14..D-17 and S4a..S4c below resolve the refinement persistence seam. The original 140 guard/PC rows and their 174-minute Code / 413.5-minute Mutation floors are retained; eight additional controls and their cost delta are explicit. Return only this amendment and its affected verification cases to TestDesign before Code.

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
| A running refinement's event is a complete recovery record. | `AgentTaskReplyService.RefineAsync` saves the event before spill/enqueue; `NewEvent` clips `Detail` at 4,000 characters and `AppDbContext` also sets its maximum length to 4,000. The full rendered body exists only in memory until a successful file write. | D-14 stores the complete rendered body in a separate durable obligation in the event's transaction. The event remains a bounded timeline entry, never the delivery source. |
| A timestamp makes refinement files unique and reusable after restart. | `FitRefinementForTyping` uses task short ID plus `yyyyMMddHHmmss` and `File.WriteAllText`. Two same-second refinements can overwrite the same path. It persists neither the association nor a checksum. | D-15 uses a persisted full refinement GUID, immutable paths/content identity and create-or-verify materialization. A retry cannot select the newest event or overwrite another message's file. |
| Retrying enqueue or restarting discovers accepted refinements. | Running refinements call `EnqueueAsync` without a source key. `SourceTaskId`/`ContentDigest` dedup is for completion notes; `SourceLandNotificationId` and `RulesRefreshKey` have their own unique keys. `FlushStrandedQueuesAsync` only discovers existing queue rows. | D-14/D-16 add a distinct refinement key and a boot/periodic outbox reconciler, including the acceptance-to-enqueue gap. Do not reuse completion/rules identities. |
| Mapping the first pointer makes all queue transport guest-readable. | `SpillQueueBodyAsync` derives a host cwd inbox file; `TypedBodySpill.Fit` overwrites it, normally renders a relative pointer, and returns the original body on write failure. `DeliverNextLockedAsync` replaces queue `Body` with that pointer. Row-less `Now` uses a second-resolution stem. Only profile-v1 completion rows already freeze a separate rendering snapshot. | D-17 maps the secondary spill explicitly and freezes its original bytes, member identities and final guest wire before typing. Docker spill failure holds delivery; host fallback remains unchanged. |

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
| D-14 | For running Docker-target refinements, add `AgentTaskRefinement` as a durable outbox with complete rendered text, a full GUID and frozen destination. Commit it with the `Refined` event; enqueue by a separate unique `SourceRefinementId`. This covers failure before any queue row exists. | Raising the event cap, using the newest event, relying only on a spill, or stuffing a refinement into the completion outbox: none supplies the required independent immutable delivery identity and recovery contract. |
| D-15 | Bind primary spills to refinement GUID and rendered-byte hash; persist paths and pointer once. Publish files atomically without replacing an existing final path; compare an existing file with the recorded bytes. | Millisecond timestamps still collide; a fresh random name on each retry loses association; overwriting a fixed task/queue filename can change instructions already submitted. |
| D-16 | A concrete refinement reconciler owns boot/periodic recovery; the existing session queue alone owns typing, attempt limits and transcript confirmation. A new session generation or mapping holds old work for an explicit caller decision. | In-memory retry jobs, letting the scanner type directly, or silently rebinding to the current worker/generation risks lost, duplicated or misdirected instructions. |
| D-17 | Freeze a Docker queue spill snapshot before file materialization/attempt, map only its recorded workspace-relative path to the guest and replay the same final wire. Secondary spill failure never types the original oversized body. | Global string replacement, mounting arbitrary host inbox directories, recursively spilling pointers, or applying Docker refusal semantics to host sessions widens scope or hides delivery failure. |

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

### VD-1: durable running refinements and immutable spill transport

This amendment applies to `Dispatched`/`Working` tasks whose accepted session has the Docker binding. The queued-task goal amendment, Blocked/settled refusal, and host-target refinement/fallback contracts stay as they are. A Docker refinement uses ordinary `WhenIdle` delivery even when its recipient is already eligible. No new task status, provider turn or instruction-download API is introduced.

**Acceptance record.** Add `server/Domain/Entities/AgentTaskRefinement.cs` and table `AgentTaskRefinements`. One row is one accepted refinement, not one delivery attempt. Its immutable members are:

| Members | Contract |
|---|---|
| `Id`, `AgentTaskId`, `SourceEventId`, `AgentSessionId`, `AcceptedSequence`, `CreatedAt`, `QueueMessageId` | Full GUID identity; preallocate event and queue GUIDs once. Unique event and queue IDs; unique `(AgentSessionId, AcceptedSequence)` allocated under the session row lock. `QueueMessageId` is a reserved identity, not a required foreign key to a row that does not exist yet. Distinct accepted requests, even identical text in the same second, get distinct refinement IDs. |
| `RenderedBody`, `BodySha256`, `BodyUtf8Bytes` | Required PostgreSQL `text`, with no 4,000-character cap. Store the complete result of `BuildRefinement` once, including both task markers and the continue/report instructions, plus SHA-256 and length of its UTF-8 bytes without BOM. Existing request trimming and formatter LF conversion define the accepted rendering; no later re-render from mutable task/event data. Reject text that cannot be represented losslessly in this storage before acceptance, rather than clipping/replacing bytes. |
| `DestinationBindingJson` | Schema-versioned snapshot of task/session IDs, normalized accepted generation, worker ID/config digest, full container ID, and approved host/guest cwd/workspace mapping. Copy the accepted binding, never the current agent settings. If a launch has not yet established that binding, refuse before accepting a Docker refinement. |
| `PrimarySpillRelativePath`, `PrimarySpillHostPath`, `PrimarySpillGuestPath`, `PrimaryWireText`, `PrimaryWireSha256` | Persist the primary artifact and exact join-safe pointer together with the body. Paths derive only from the frozen binding and identity; the pointer names the guest path. No timestamp or event lookup participates in selection. |

Mutable progress is separate: `State` (`Pending`, `Enqueued`, `Confirmed`, `Held`, `Canceled`), `EnqueuedAt`, `ConfirmedAt`, `ConfirmedPromptSequence`, `NextAttemptAt`, `LastFailureCode` and bounded failure metadata. `Held` retains the obligation; it is not delivery or cancellation. States are observations, not authority to reconstruct or alter immutable members.

`RefineAsync` validates the live task/session relationship under the existing task concurrency discipline and session generation lock, renders once, and inserts the refinement plus its `Refined` event in **one transaction before filesystem I/O or enqueue**. Recheck task status/binding at commit so concurrent settlement cannot create a new accepted refinement for an already-terminal task. The event keeps its 4,000-character display cap and names the full refinement ID; it is explicitly a preview/acceptance record. No committed event without its full-body obligation, and no obligation from a rolled-back request. Caller cancellation after commit does not revoke the obligation.

**Primary artifact.** Use `<accepted-host-cwd>/.antiphon/refinements/<task-guid-N>/<refinement-guid-N>-<body-sha256>.md`; persist its absolute Windows path and `/work/<approved-cwd-relative-path>/.antiphon/refinements/...` counterpart. Grok still spills even a short refinement. A new `DockerInstructionSpillStore` external-I/O implementation writes a uniquely named temporary file in the same directory, flushes/closes it, then publishes to the final name with no overwrite. If the final name already exists, verify exact bytes/hash and reuse it; a concurrent publisher may only win with the identical bytes. Missing final files can be recreated from the recorded body at the **same** path. Partial temporaries are never pointers. A mismatched existing file, mapping escape, inaccessible mount or write failure produces a visible held delivery and no terminal input. Do not overwrite a mismatched final file, mint another refinement, or substitute a newer spill. Revalidate containment/reparse boundaries at materialization and before typing, including retries.

**Enqueue identity and ordering.** Add nullable `SessionQueuedMessage.SourceRefinementId`, a filtered unique index on that field, and a restrictive relation to the obligation. Add an internal refinement enqueue path to `SessionMessageQueueService` which accepts the recorded `QueueMessageId`, source ID, session and pointer. Under the session queue lock and database transaction, find an existing row by source ID or insert the preallocated ID; database uniqueness is the cross-context backstop. On a unique race, reload and compare the existing row's ID, destination and immutable logical-wire hash; mismatch is a conflict, never an upsert. Dedup searches Pending, Sent and Canceled rows. Reconciliation of `Confirmed`/`Canceled` obligations never inserts again even after queue retention. Do not use `SourceTaskId`/completion digests, `SourceLandNotificationId`, or `ExecutionTaskId` as a refinement key: those have other settlement/expiry semantics.

Refinement rows keep `Origin=Delegation`, `ConversationKey=null` and their own marker-bearing pointer; they do not coalesce. The reconciler enqueues accepted refinements in `AcceptedSequence` order, retaining an earlier unresolved enqueue obligation before advancing a later one for that session. Enqueue uses `deliverIfIdle:false`; only after the queue commit may it mark `Enqueued` and request the normal idle flush. A crash after insertion but before the progress update repairs the link by source ID. `EnqueuedAt` is never a completion marker. Both immediate and recovery paths use this same method; no in-memory seen-ID set is correctness-critical.

**Recovery and receipt owner.** Add concrete `AgentTaskRefinementService.ReconcileAsync` and `AgentTaskRefinementHostedService`, registered in `Program.cs`. Scan immediately on server boot, then every five seconds using `TimeProvider`, paged by durable IDs with per-row error isolation and `NextAttemptAt` pacing for I/O failures. Discovery includes Pending, Enqueued and Held obligations even without a queue row and even if their task has since settled. A failed/disabled wakeup cannot strand them. The scanner materializes/verifies files, ensures the keyed queue link, and invokes the existing queue's eligible flush; it never types or fabricates transcript rows itself. Existing `QueueAttention`, the stranded sweep, rules barrier, busy/idle checks, attempt caps and composer hold remain the typing authority.

Recover transcript evidence **before** retrying input. Confirm only a complete immutable final wire `UserPrompt` in the original session/generation above that row's persisted attempt floor; validate the recorded artifact hashes, including both levels for a secondary spill. Store its prompt sequence and `ConfirmedAt` idempotently. `Sent`, screen evidence and an ID/header match cannot confirm the obligation. Qualification additionally observes the actual guest read of every referenced file and compares complete bytes; the runtime receipt flag alone is not proof of model file consumption. A crash between prompt, queue verdict and refinement confirmation uses late-confirm and adds no submit. Retain the original attempt baseline/generation during Enter-only recovery.

Server/runner recreation with the exact adopted generation and binding can continue the obligation. A new generation, changed mapping/container, ambiguous runner state or missing session causes a held delivery with the original bytes retained; never update the original binding to current settings. Late evidence from the original attempt remains eligible for reconciliation. Task settlement does not erase accepted work, but prevents fresh automatic submission of an unconfirmed refinement once the task is terminal; record a held reason for the caller to resolve. Recovery neither reopens the task nor presses Enter into a possibly retained terminal-task composer. An explicit queue cancellation records `Canceled` on its refinement in the same transaction and prevents outbox resurrection. If work must be sent to a replacement generation, the caller makes a new refinement after resolving/canceling the old obligation; no automatic retargeting.

An obligation held before queue insertion must also be resolvable. Add a cancel-only `POST /api/agent-tasks/{id}/refinements/{refinementId}/cancel` in `AgentTaskEndpoints`, using the same task authorization and verifying the refinement belongs to that task. `AgentTaskRefinementService.CancelAsync` atomically cancels a never-attempted obligation and its queue row if present; replay is idempotent. An attempted row refuses this shortcut and requires existing queue controls/late-confirm, preserving composer safety and evidence. This operation neither changes task status nor rebinds/replays instructions; no full-body download or new UI is required.

The immediate path reports materialization/enqueue failure through a bounded `ConflictException` naming the **accepted refinement ID** and that its full body is retained for reconciliation. Persist the held reason and a deduplicated task Warning for recovery failures; include IDs/error categories, not full instruction text. A response lost after acceptance is reconciled by that ID/event. Enqueue retries are idempotent; repeated fresh HTTP refine requests are distinct user instructions, not content-deduplicated requests. This amendment promises no new asynchronous caller notification: a Problem Details response or persisted Warning is visible refusal evidence, not a delivered caller `UserPrompt` (DL-5 stays as separately specified).

**Secondary queue spill.** A primary pointer can exceed the active single-write ceiling, especially with a long permitted cwd. `SpillQueueBodyAsync` must select Docker behavior from the persisted session binding and accept a durable queue owner. Add schema-versioned nullable `SessionQueuedMessage.DockerSpillSnapshotJson` (`jsonb`) containing the immutable destination binding, ordered member queue IDs, each member's original logical body/hash, complete composed body/hash/byte length, workspace-relative plus absolute host/guest file paths, and final join-safe wire text/hash. Store it before writing a secondary file or replacing `Body`; a failed attempt to commit the snapshot produces zero file/input effects. For a batch, persist the same snapshot/link on all member rows in one transaction. For refinements the member set is exactly the one keyed row. Once frozen, no retry may recompose the batch with newer rows or rewrite its source text; new input forms a later run.

Use `<accepted-host-cwd>/.antiphon/inbox/<head-queue-guid-N>-<composed-sha256>.md` and an explicit absolute `/work/.../.antiphon/inbox/...` guest pointer. Freeze the composed bytes **before** replacing `Body`; never regenerate this file from the pointer now stored in `Body`. The same create-or-verify store handles both spill levels. Before the first possible terminal write, commit the snapshot's final wire as queue `Body` with the attempt claim/baseline/generation. A crash after snapshot commit, file publication or attempt claim reconstructs the same member set, path, bytes and wire from durable state. Validate both primary and secondary files before retrying a refinement; a previous successful spill does not authorize typing after a later verification/write failure. Check the final pointer against `SingleWriteMaxBytes`; if even that pointer cannot fit, hold without recursive spilling, truncation, API/timeline fallback or typing the original.

Route `DeliverNextLockedAsync`, `SendNowAsync`, `EnqueueDeliveringNowAsync`, interrupted-attempt/Enter-only recovery and any secondary spill helper through that same frozen-snapshot path. Row-less `EnqueueAsync(..., Now)` must refuse an oversized Docker body before I/O with `docker_spill_requires_queue`; it cannot create a timestamp spill with no durable owner. Under-ceiling Now behavior and host paths stay unchanged, including rules gating. Refuse in-place amendments to refinement rows or frozen spill snapshots; cancellation is explicit and new content gets a new identity. Preserve profile-v1 `CompletionDeliveryJson` as completion's receipt authority: when a Docker destination requires a spill, freeze its final wire/member/path/hash from the same spill snapshot in the attempt transaction. Neither record may independently re-render the other's frozen payload. Add no download endpoint or new bind mount: the existing standalone workspace mount owns both paths.

**Retention and rollout.** Generate the migration through the EF CLI. Existing queues have null new fields; create no fictitious full refinements from clipped historical events. Docker is disabled until this migration and both services are deployed. `DataRetentionService` must protect unresolved refinement rows, their events/tasks, destination sessions/transcripts, linked queue rows and frozen Docker spills; a Sent row is still unresolved until confirmed. Use restrictive relations plus pruning predicates so task/session cascades cannot discard the only full body or attempt evidence. For v0, retain refinement records as dedup tombstones and keep their generated files for the lifetime of the dedicated checkout; no new age-based file sweeper is introduced. Terminal refinement records continue to prevent duplicate enqueue after a queue row is pruned. Existing task/session pruning must respect these retained references.

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

VD-1 expands S4 in three buildable slices; the other slices are unchanged. S4a precedes S4b, then S4c; all three are required before S6 instruction-delivery qualification.

| Slice | Files and concrete change | Tests / exit evidence |
|---|---|---|
| S4a: accept and key the full refinement | New `server/Domain/Entities/AgentTaskRefinement.cs`; `server/Domain/Entities/SessionQueuedMessage.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated `server/Migrations/*_AddDockerRefinementDelivery*` and snapshot; `server/Application/Services/{AgentTaskReplyService,DelegationReportFormatter,SessionMessageQueueService}.cs`; new concrete `server/Application/Services/AgentTaskRefinementService.cs`. Atomic acceptance, full-body text, immutable identity/binding, keyed insertion and cancellation. | Existing `tests/Antiphon.Tests/Application/AgentTaskRefineTests.cs` stays green for host/queued/blocked/terminal semantics. New methods in `DockerGrokDeliveryRecoveryTests.cs`: G-80/G-111 plus G-141/G-142/G-145. Migration round-trip of >4,000 characters/non-ASCII and null legacy fields, rollback and concurrent enqueue cuts. |
| S4b: publish and replay mapped immutable artifacts | New `server/Application/Interfaces/IDockerInstructionSpillStore.cs` (filesystem I/O only), `server/Infrastructure/Agents/DockerInstructionSpillStore.cs`, schema-versioned `server/Application/Dtos/DockerSpillSnapshot.cs`; bounded path mapper from S4; `AgentTaskRefinementService.cs`, `SessionMessageQueueService.cs`, `TypedBodySpill.cs` and `DelegationReportFormatter.cs`. Freeze original/final queue bodies and member identities, map primary/secondary guest paths, create-or-verify files, refuse Docker fallback and row-less oversize. Integrate the existing completion rendering freeze without replacing its identity. | New/expanded methods in `DockerGrokBriefPointerTests.cs` and `DockerGrokDeliveryRecoveryTests.cs`: G-77..G-82, G-109/G-110/G-113/G-139, G-143/G-144/G-146/G-148. Existing R-7/R-8 plus `TypedBodySpillTests.cs`. Guest fixture reads both levels, including long mapped paths and missing/corrupt/reused files after recreation. |
| S4c: recover, expose holds and retain evidence | New `server/Infrastructure/Orchestration/AgentTaskRefinementHostedService.cs`; `server/Program.cs`; `server/Api/Endpoints/AgentTaskEndpoints.cs`; `server/Application/Services/{AgentTaskRefinementService,DataRetentionService,SessionMessageQueueService}.cs`; owner updates in `docs/session-runtime-invariants.md` and `docs/docker-grok-worker.md`; fixture DI in `tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs` and the new Docker test world. Wire boot/periodic recovery, no-row discovery, deduplicated Warning/held evidence, cancel-only resolution and retention protections. | `DockerGrokDeliveryRecoveryTests.cs`: G-111..G-113/G-141..G-147, real scanner/service recreation and attempt floors with busy/eligible recipients. `DataRetentionServiceTests.cs` remains regression coverage. Every accepted row either yields its own complete recipient prompt and matching file bytes or stays visibly held with its complete original body. |

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

The initial D-1..D-13 handoff was completed by TestDesign below. Return this VD-1 amendment (D-14..D-17, S4a..S4c) and only its affected/additional verification cases to TestDesign; retain the original 140 recipes. Code's early image/transport slice must record the pinned official artifact source/checksum, actual Linux cwd encoding, ConPTY -> Docker composer behavior and descendant-stop behavior; those are specific remaining measurements, not assumed investigation successes. If one contradicts this design, return the observation and affected decision to Plan/Investigate.

The Plan artifact is the only repository change from this dispatch. Validation of this dispatch is document/source-reference review plus Git diff/whitespace checks; no tests/builds are claimed.

## Verification design

TestDesign: 2026-09-19, task 318daeb7, plan commit 6f16fb61280b7e94b224a399943c295ac5f0b0bb. This section originally appended to D-1..D-13/S1..S6 without changing them; the later VD-1 amendment expands S4 as recorded above and adds the focused verification delta below. New method names are implementation contracts, not claims that tests already exist or pass.

**Original TestDesign disposition (superseded by the VD-1 amendment): return to Plan for the running-refinement persistence seam.** The running-task branch of AgentTaskReplyService.RefineAsync commits a Refined event, writes a spill, then enqueues without a refinement identity. NewEvent caps Detail at 4,000 characters; FitRefinementForTyping names files using second-resolution timestamps. A crash/write/enqueue failure can lose the tail; two refinements in one second can overwrite the first file. At that inspection, the plan required full-body preservation/recovery without selecting its durable record, stable identity, atomic handoff or recovery owner. D-14..D-17 now select them; the final VD-1 verification amendment identifies the focused TestDesign return.

The requested amendment must name storage/replay of the complete accepted rendered refinement, its unique identity, immutable file association, destination generation, enqueue retry/dedup and startup recovery. It must also cover the secondary queue spill: SessionMessageQueueSpillTests establishes host inbox paths and a type-original-on-write-failure fallback, neither of which proves Docker instruction delivery. This is an implementation seam, not an operator preference. The original tests below specify the observable contract; D-14..D-17 provide the architecture. Return only VD-1 and changed guards to TestDesign; do not send this version directly to Code.

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
- PC observation names (effects, recipient, guestRead, lease, recovered) denote fixture-owned observations of the real path. recovered.FullBody reads the VD-1 `AgentTaskRefinement.RenderedBody` record. No manual Pending->Sent updates or expected-prompt insertion may simulate progress.
- DTO consumers: Agent CRUD/read, session snapshot/migration, task selection/dispatch/reuse, manual/resume control/composition, launch queue, HTTP JSON/client capability, direct runner validation, planner, sidecar/PtyHost adoption, transcript/rules/expiry, stop/recovery, descriptor and registration scripts. Update fake/recording/refusing clients for the descriptor; never silently return an available worker. Null target round-trips unchanged.

### Delivery inventory

Durable join: (TaskId, AgentSessionId, AcceptedStartedAt, DockerWorkerId, ConfigDigest, FullContainerId), plus the individual obligation/queue ID, immutable wire digest and attempt floor. AcceptedStartedAt is the existing normalized equality token. Never rebuild a historical binding from mutable current agent configuration.

| Path | Producer -> destination | Persistence boundary and durable identity | Recovery | Observable receipt |
|---|---|---|---|---|
| DL-1 initial/follow-up brief | Task service/dispatcher -> Linux composer via real launch/session queue | Accepted task/session binding, full rendered brief/spill, queue Id/ExecutionTaskId, LastDeliveryGeneration and baseline | Transaction/enqueue failure, lost wakeup, server/runner recreation, late ACP catch-up | One matching complete UserPrompt of actual wire text in own ACP stream, persisted above attempt floor; exact guest-read spill bytes including role/report suffix. |
| DL-2 running refinement | RefineAsync -> bound delegate through real queue | `AgentTaskRefinement`: full RenderedBody/hash, Id, immutable binding/primary spill, reserved QueueMessageId; queue unique SourceRefinementId and optional frozen DockerSpillSnapshotJson | D-16 boot/periodic reconciler plus queue recovery across acceptance/publication/enqueue/attempt/receipt cuts; same-second identities recover separately | Complete refinement wire UserPrompt plus exact guest file read; tail beyond 4,000 chars survives recreation. Refusal types nothing and retains complete accepted work. |
| DL-3 bootstrap/resume/compact rules | Composer/runner rules store/refresh service -> internal Grok read turn | Atomic host file; receipt generation/hash/count; persisted rules state; unique (session, RulesRefreshKey), queue ID | Replace/receipt/boundary/trigger/queue/attempt/ack/end cuts; same deadline/key after restart | Complete owning UserPrompt, exact guest-read bytes, matching assistant ack and successful TurnEnd before ordinary work. |
| DL-4 report/completion | Grok report -> settlement -> completion scanner -> caller queue | Report/task Result; applicable terminal TaskCompletion outbox, Id/SourceEventId/CompletionSnapshotJson; frozen CompletionDeliveryJson and SourceLandNotificationId | Terminal rollback/commit, notification enqueue/wakeup, render/spill/attempt/prompt cuts; terminal tasks remain recoverable | Caller complete UserPrompt for frozen wire above its own floor, correct identity and pointer hash. Succeeded and completion stamp are insufficient. |
| DL-5 failure outcome | Auth/preflight/spill/delivery failure -> existing failure producer -> caller queue | Failure event/result plus durable notification/destination; delivery-failure outbox where applicable | Before/after failure+obligation commit, queue/attempt/receipt cuts, already-Failed task | Matching complete caller failure UserPrompt. Incident, HTTP refusal or terminal event is not delivered notification evidence. |

DL-4 covers ordinary Shared completion and admitted profile-v1 Code/Review completion. VD-1 promises no new asynchronous DL-2 refusal note; its synchronous refusal/persisted Warning must not be labeled a delivered caller message.

**Required producer-to-recipient matrix, V-5/V-10:** DL-1 through DL-5 each cross busy=false/true with every applicable handoff cut. DL-1 also covers fresh/existing named sessions and Shared/ReadOnly; DL-3 crosses bootstrap/resume/compact. Eligible recipients receive through normal enqueue/flush scheduling without a synthetic business event. Busy recipients have zero writes/attempt charges before their actual TurnEnd, then receive exactly once.

Cuts: before producer commit; after producer commit before enqueue; before queue insert commit; after queue commit before wakeup; after attempt claim before typing; after complete native UserPrompt before verdict/ack persistence; after verdict before receipt-link persistence. DL-2 adds before/after primary spill publication and secondary snapshot/publication (immutable final files are never replaced). DL-3 adds compact transcript commit before unique trigger/coverage commit, and ack before successful TurnEnd. DL-4/DL-5 add terminal+obligation atomicity and frozen-render/spill handoffs. Assert the cut was reached once. Recreate all services/contexts, preserve durable stores and recipient only, then run production startup/periodic recovery and queue. Never manufacture successful task/queue/transcript progress.

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
| V-5 | DL-1/2/3 real producer -> recipient plus cuts | DockerGrokBriefPointerTests; DockerGrokDeliveryRecoveryTests | Complete wire UserPrompt/full guest-read bytes; no failed-spill fallback/duplicate/rules-barrier bypass. Use D-14..D-17 and the focused VD-1 verification delta. |
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

All new classes below reside at tests/{project}/{Application or Agents for Antiphon.Tests}/{class}.cs as listed in the command roster below; runner classes are directly under tests/Antiphon.SessionRunner.Tests. The observation names are the fixture contract in Inspection. PC-80 and the DL-2 variants of PC-111..PC-113 now bind to D-14..D-17; the focused TestDesign review must approve that seam before Code.

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

All figures are **estimated execution floors**, not measured timings or timeout increases. Test authoring, finding repair, ordinary Review reading and operator login/wait time are additional. The following tables preserve the original 140-control baseline; the VD-1 addendum gives the incremental and combined floors. Counts are method contracts; parameterized busy/cut/boundary rows increase executed case counts and must be reported from fresh TRX.

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

**Original TestDesign handoff audit:** inspected bodies/fixtures are listed; guards=140, mapped=140, missing guard-to-PC mappings=0, duplicate PC mappings=0. All 140 mutation recipes have exact methods and decisive assertions; new tests are pending implementation. The original next: plan gate was the absent PC-80 / DL-2 PC-111..PC-113 persistence/recovery contract, with PC-81/PC-82 affected by the file/queue choice. That contract is now D-14..D-17 above. The focused TestDesign review below remains required; this Plan amendment makes no test/build/live success claim.

### VD-1 verification amendment and focused return

Plan task `0d678e51` inspected the running and queued `RefineAsync` branches, `FitRefinementForTyping`, `NewEvent`, formatter refinement/pointer bodies, queue enqueue/dedup/spill/attempt/late-confirm/Now/amend/cancel/discovery paths, `TypedBodySpill`, queue/event EF mappings, notification boot scanner and retention predicates. Existing test bodies used as contract checks: `AgentTaskRefineTests`; TestDesign's already-inspected `SessionMessageQueueSpillTests` and completion/crash fixtures remain the inherited precedents. Source was the specified `c3917496` checkout. No production source or test implementation changed in this amendment.

The 140 existing guard and PC rows remain **verbatim**. Their following fixture bindings/variants now have a concrete source; all other verification sections retain their original scope. `recovered.FullBody` in PC-80 reads `AgentTaskRefinements.RenderedBody`, with independently generated expected UTF-8 bytes, not the event, queue pointer or an in-memory copy.

| Existing case | VD-1 binding / added variant in the same method contract |
|---|---|
| V-5 / DL-2, G-77/G-80 | Real Docker-bound `RefineAsync` producer; >4,000-character body with >1,000 lines, multibyte text and distinctive final marker. Compare full persisted rendering and guest file bytes through context recreation; both short and long Grok refinements spill. Include invalid-text pre-acceptance refusal, accepted cancellation of the HTTP request, and legacy host controls. |
| `G-79` | Primary publication/mapping failure after acceptance keeps the complete row, immutable identity and visible held reason with zero terminal writes. First successful refinement A followed by failed B must never enqueue A's path as B's instructions. |
| `G-81` | Two full refinement IDs accepted under the same frozen clock second; read A again after B materializes and after recreating services. Both paths/bytes remain distinct. Identical text with distinct accepted IDs remains two instructions; an enqueue retry of one ID remains one. |
| `G-82` | Force a primary guest pointer above the queue ceiling using a valid long mapped cwd; observe the secondary guest read of that exact pointer, then the primary read of the exact full refinement. Include inbox/modern ceilings, short/oversize final pointer, allowed cwd descendant, Unicode and guest cwd different from the host. No host-path or relative-cwd coincidence can satisfy the read fixture. |
| G-109/G-110/G-139 | Negative full-wire receipt cases retain the refinement obligation. Check final wire plus both recorded spill hashes and original binding/attempt floor. A later generation's identical prompt cannot confirm the old obligation. |
| `G-111` | Interrupt after event+outbox commit, before queue transaction commit, and after queue commit before `EnqueuedAt`/wakeup. Startup reconciliation uses the same preallocated queue ID and source key; a failed queue insert does not complete or remove the obligation. |
| `G-112` | Boot and periodic scanner tests, with immediate wakeup suppressed and no artificial task/TurnEnd event for the eligible case. Include no queue row, committed Pending, Held retryable I/O and interrupted Sent; exact retained runner generation and unknown/replaced runner variants. |
| `G-113` | Crash after complete native prompt but before verdict/`ConfirmedAt`, and after verdict before receipt link. Late confirmation causes zero second submits and retains the same primary/secondary wire, IDs, paths and hashes. |

Extend the existing busy=false/true x handoff-cut matrix with: acceptance transaction rollback; primary temp-write/publish before enqueue; keyed insert race and insertion before progress-link save; secondary snapshot commit before file write; file publication before attempt transaction; attempt commit before input; full prompt before verdict/confirmation. Each cut is one-shot with an asserted reached count. Recreate all services/contexts, keeping only durable stores and the owned recipient. Compare the exact guest-read bytes, not just generated file names. After every supported recovery require one complete recipient prompt; held generation/terminal/corruption cases require no new input and the complete original obligation. A prior prompt can still late-confirm while a missing file is recreated from the same recorded bytes.

Additional ordinary variants in these same classes cover a canceled refinement surviving scanner restart without requeue; rejection of in-place amendments to keyed/frozen rows; a frozen secondary batch followed by newer queue input; `SendNowAsync`/durable overlay Now/retry sharing the original snapshot; and profile-v1 completion snapshots agreeing with the Docker secondary spill. New rendering content must never replace already-published content under the old identity. Keep the inherited R-1..R-9 selections and add R-10: `AgentTaskRefineTests/*`, `TypedBodySpillTests/*`, `DataRetentionServiceTests/*` in `Antiphon.Tests`, with host fallback unchanged and legacy new columns null. The new retention assertions below run through the production pruning methods; they are not file-age simulations.

Eight new independently critical controls are needed: the original matrix does not mutate acceptance atomicity, refinement enqueue dedup, immutable existing-file validation, missing-file rematerialization, pre-attempt generation binding, Docker secondary failure fallback, refinement retention, or the row-less secondary-spill refusal. Add these rows after G/PC-140; do not renumber or replace any original row.

| Guard | Contract | PC |
|---|---|---|
| G-141 | S4a/D14: Refined acceptance event and complete refinement commit or roll back together. | PC-141 |
| G-142 | S4a/D14: Reconciliation of one accepted ID cannot allocate a second queue message. | PC-142 |
| G-143 | S4b/D15/D17: An existing spill must match its originating message; retries cannot accept another body at that path. | PC-143 |
| G-144 | S4b/D15/D17: A missing primary/secondary file is recoverable from its immutable durable bytes at the original path. | PC-144 |
| G-145 | S4a/S4c/D16: A new generation cannot receive a previously accepted refinement without explicit new acceptance. | PC-145 |
| G-146 | S4b/D17: Secondary file/mapping failure holds the full obligation and sends no fallback body. | PC-146 |
| G-147 | S4c/D16: Retention cannot erase unresolved refinement queue/attempt evidence. | PC-147 |
| G-148 | S4b/D17: Row-less Now cannot publish a Docker spill without a durable message identity. | PC-148 |

| PC / guard | Compiling production mutation | Exact test method | Intended assertion |
|---|---|---|---|
| PC-141 / G-141 | Commit the Refined event separately before inserting the obligation; inject the existing one-shot cut before obligation save. | `DockerGrokDeliveryRecoveryTests.Refinement_acceptance_is_atomic` | `persistedRefinedEvents.Count.ShouldBe(0)` after the failed acceptance; baseline also has zero obligations/input. |
| PC-142 / G-142 | Treat reconciliation replay as a fresh unkeyed enqueue (new queue ID, null SourceRefinementId) instead of the existing-key return. | `DockerGrokDeliveryRecoveryTests.Same_refinement_enqueues_once` | `destinationQueueRows.Count.ShouldBe(1)` after concurrent/restarted reconciliation, plus one complete recipient prompt. |
| PC-143 / G-143 | Trust an existing final spill without comparing it to the recorded hash/bytes. | `DockerGrokBriefPointerTests.Spill_identity_cannot_change_on_retry` | `recipient.SubmittedBodies.ShouldBeEmpty()` with a different valid message's bytes at the original primary/secondary path after restart. |
| PC-144 / G-144 | Return a retained hold on an absent final file instead of recreating it from the durable body/snapshot. | `DockerGrokBriefPointerTests.Missing_spill_recovers_original_bytes` | `recipient.CompletePromptsFor(refinementId).Count.ShouldBe(1)`; guest reads must equal the original bytes at the original paths. |
| PC-145 / G-145 | Skip the refinement's accepted-generation comparison before queue input, retaining other binding checks. | `DockerGrokDeliveryRecoveryTests.Refinement_cannot_retarget_new_generation` | `newGenerationRecipient.SubmittedBodies.ShouldBeEmpty()` for a never-attempted old-generation refinement; full body remains held. |
| PC-146 / G-146 | Return the original oversized wire on a Docker secondary-spill write failure, as the host fallback does. | `DockerGrokBriefPointerTests.Secondary_spill_failure_types_nothing` | `recipient.SubmittedBodies.ShouldBeEmpty()` with the keyed original body still recoverable. |
| PC-147 / G-147 | Omit the unresolved-refinement protection from queue pruning. | `DockerGrokDeliveryRecoveryTests.Unresolved_refinement_survives_retention` | `persistedQueueRow.ShouldNotBeNull()` for an aged Sent/null-verdict refinement after the real retention sweep, before real recovery. |
| PC-148 / G-148 | Allow oversized Docker Mode.Now to take the legacy timestamp-spill path. | `DockerGrokBriefPointerTests.Rowless_oversize_requires_durable_queue` | `recipient.SubmittedBodies.ShouldBeEmpty()`; baseline refuses before creating any spill. |

All eight classes/methods use existing roster aliases B or Q in `tests/Antiphon.Tests/Application/`; no new project or native lane. Mutation uses each exact method filter, for example `/*/*/DockerGrokBriefPointerTests/Spill_identity_cannot_change_on_retry`, with parameterized primary/secondary and busy/cut rows counted in fresh TRX. Use scripted filesystem failure cuts and the local recipient substitute; no Docker daemon receives a SourceLanding snapshot. A mutation that merely causes an unexpected fixture/build/constraint exception is not the stated red. In particular, PC-142 must reach the queue-row count with valid nullable source identity, not a foreign-key error; PC-141's deliberate failure is caught by the test before checking transaction results.

**Incremental estimated floors.** The original 140-PC tables remain unchanged. These additions are all I-group application/queue/file-integration methods at the original rates; no concurrency or caching savings are credited. The extra ordinary allowance covers their fresh cases, expanded VD-1 cuts and R-10. These are commission floors, not measurements or deadlines.

| Added work | Calculation | Minutes |
|---|---|---:|
| Code ordinary additions | 8 new methods x 0.85 + 2.2 for expanded existing cuts/R-10 | 9 |
| Mutation exact-method initial green | 8 x 0.85 | 6.8 |
| Mutation red/restore/fresh-green cycles | 8 x (0.60 + 0.85 + 0.10 + 0.60 + 0.85) | 24 |
| Code floor including VD-1 | unchanged 174 + 9 | **183** |
| Mutation floor including VD-1 | unchanged 413.5 + 6.8 + 24 | **444.3** |
| Total floor including VD-1 | 183 + 444.3 | **627.3 (10 h 27 m 18 s)** |

The total inventory is **148 guards / 148 unique PC mappings** (original P=15, U=45, I=80; amended I=88). TestDesign should audit only D-14..D-17/S4a..S4c, DL-2 and secondary-spill variants, R-10, G/PC-141..148, and the cost delta; the original 140 recipes and baseline floors are final. It must confirm the new persistence/queue/test-fixture seams are executable before handing to Code. No outstanding operator decision, test execution, mutation run or live delivery success is claimed here. **Next: test-design.**

## Verification design

Focused VD-1 review: 2026-09-19, TestDesign task `6b037196`, source `17c414fc84f2cac8511124d4aece748f7dc09477`. This is an append-only review of D-14..D-17/S4a..S4c and their affected verification. The fix design, original 140 recipes, and original 174/413.5-minute floors above are unchanged.

**Disposition: return to Plan; VD-1 is not Code-ready.** The outbox, keyed queue and common file-store decisions provide implementable seams for PC-141..148, with the setup specified below. They do not yet close two affected original controls:

1. **Receipt generation provenance (VD1-TD-1).** `TranscriptEntry`, `SessionRunnerTranscriptEvent` and `SessionRunnerTranscriptDto` carry session/sequence/time but no accepted generation. `LateConfirmAttemptedMessagesAsync` matches body above a sequence or timestamp floor; it does not attribute a transcript row to its producing generation. D-16 requires both accepting late evidence from the original attempt and rejecting the same complete text produced by a replacement generation. Comparing the obligation with the *current* session generation rejects both; labeling a fake transcript with a fixture-only generation proves neither. Plan must name the durable evidence and ingestion/recovery owner that distinguish those histories, including delayed ingestion and null/ambiguous provenance. This affects DL-2, G/PC-109, 110, 113, 139 and the positive companion of PC-145. No new transcript schema is selected by this review.
2. **PC-81's asserted red is masked by D-15 (VD1-TD-2).** With timestamp-only names, publish A then B in the same second. Correct create-or-verify refuses B's different bytes and preserves A; `firstGuestRead.Bytes.ShouldBe(firstBodyBytes)` still passes. Changing only the name therefore need not make the original stated assertion red. Keep the original recipe as historical text. The affected method needs an explicitly approved additional decisive assertion: both accepted identities eventually have their own complete prompt and original file bytes, including `recipient.CompletePromptsFor(secondRefinementId).Count.ShouldBe(1)`. A hold on B is the mutation's intended failure, not a timeout or fixture exception. This does not change the immutable-file design.

The eight-row addition also undercounts independently bypassable VD-1 guards. The inventory below adds 24 controls for requirements already written in D-14..D-17; it adds no product behavior. The 183/444.3-minute arithmetic is correct for eight additions, but is not a complete verification floor for those decisions.

### Inspection

All paths below are repository-relative. A method list denotes bodies inspected, not a claim to have read other methods in the same file. No tests, builds, migrations, Docker operations or live turns ran in this documentation stage.

| Test/fixture bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| `tests/Antiphon.Tests/Application/AgentTaskRefineTests.cs`, all tests, `SeedTaskAsync`, `CreateService`, `ScopeFactory`, `TempWorkspace` | Working/Dispatched, Queued, Blocked, all settled states, empty input, oversized host spill; real queue but no recipient -> V-5/DL-2, R-10. Its Claude/shared-store fixture alone cannot prove Docker delivery. |
| `SessionMessageQueueSpillTests.cs`, all seven tests and helpers | WhenIdle/Now, small/large, batch, file failure; host row-less Now and type-original fallback are deliberate -> V-5, R-7/R-10. |
| `TypedBodySpillTests.cs`, all seven tests and `TempDir` | Inbox/modern ceiling, UTF-8 sizing, quoted Grok pointer, channel envelope, I/O/API fallback -> R-10. |
| `SessionMessageQueueServiceTests.cs`: `Send_now_promotes_a_specific_queued_message`, `Cancel_removes_a_pending_message_without_delivering_it`, `Cancel_keeps_a_non_pending_message_unchanged`, `Cancel_pending_if_untyped_refuses_an_attempted_message`, `CreateHarnessAsync`/transcript helpers | Durable SendNow and cancellation precedents -> V-5 additional cancellation/Now variants. Their manually staged queue status is usable for guard setup, not delivery acceptance. |
| `VerificationRoundDeliveryTests.cs`: `C544_CompletionRecovery`, `C544_PointerReceiptRequiresContent`, `C544_CompletionReceiptWholeWire`, `AssertReceivedOnceAsync` | Real producer/queue, busy/eligible, persistence cuts, pointer hash, complete receipt and floor -> V-5, R-8. Host pointer/hash evidence does not establish guest reads or generation provenance. |
| `DataRetentionServiceTests.cs`: running/stale terminal transcript tests, queue retention/zero-window tests, session-reference/cascade tests, terminal-tree tests, `C544_CompletionObligationRetention`, its seed and service/context/cleanup helpers | Distinct transcript/queue/session/task pruning predicates, restrictive references, confirmed tombstone after queue pruning -> R-10, PC-147/149..152. New cases must use an isolated database, not the class's shared global sweep. |
| `TestHelpers/C544DeliveryRig.cs`, whole; `C544World.BuildServices`, `RestartAsync`, `CreateContext`; `BridgeQueueHarness.HarnessOptions`, `CreateAsync`, submit callback, transcript/working helpers; `TestDbFixture.cs`; `DelegationTestServices.AddGitWorkspaceService`/`AddDelegationWorktreeGraph`; `ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner` | Nearest fixtures for both new B/Q classes. Interceptors, fresh contexts/providers, owned recipient, clone connection, no production runner -> all focused V/PC cases. |
| Production `AgentTaskReplyService.RefineAsync`/`FitRefinementForTyping`; queue spill, keyed enqueue, durable Now, SendNow, cancellation, attempt freeze and late-confirm bodies; `TypedBodySpill`; `DataRetentionService`; relevant `AppDbContext` mappings; `AgentTaskLandNotificationHostedService`; transcript entity/DTOs | Confirms missing implementation and actual seams, including instance-local queue locks, destructive pruning passes, generation-evidence gap and host fallback. This is inspection evidence, not runtime acceptance. |

Owner sections read: project naming/layers/enforcement; testing filters, isolation, output cleanup and post-land Mutation; orchestration delegate/report rules; session delivery, generation, queue recovery and completion-obligation invariants.

**Missing setup assigned to the implementation, after Plan closes VD1-TD-1/2:**

- Extend the already commissioned `DockerGrokTestWorld` using one `CreateIsolatedSchemaAsync` clone per scenario. Wire *every* context, including receipt callbacks and retention, to that connection. Rebuild the whole provider on restart, retaining only the database, owned files, the controlled recipient and its actual submitted/transcript history. Register real reply, refinement, scanner, queue, retention and spill services through the existing delegation helper; do not substitute an in-memory outbox.
- Add a concrete no-op observation/cut object, following `LandDeliveryBoundary`/`C544Boundary`, and an EF save/transaction interceptor following `C544DeliveryFault`. The existing completion interceptor does **not** already recognize refinement entities. Name cuts `refinement-accept-save`, `refinement-accept-committed`, `refinement-primary-temp`, `refinement-primary-published`, `refinement-queue-insert`, `refinement-queue-committed`, `refinement-progress-save`, `docker-spill-snapshot-save`, `docker-spill-snapshot-committed`, `docker-spill-published`, `docker-attempt-committed`, `refinement-verdict-save`, `refinement-receipt-save`. Arm one identity-specific cut per scenario and assert its reached count is exactly one. Post-commit cuts must fire from transaction-committed observation, not merely `SavedChanges` inside an open transaction.
- Add a clock with controllable timers for the new five-second hosted loop. `C544Clock.Advance` changes `GetUtcNow` only; the existing notification host uses wall-clock delay. Neither proves the new periodic scan. Exercise actual host start/stop and timer wakeup, await scan completion, and dispose it before the test ends. Test boot and periodic scans independently with immediate wakeup disabled.
- Use the real `DockerInstructionSpillStore` over owned scratch files; wrap its external I/O for temp-write/publish failures. Observe before input, file publication and pre-delete retention candidate identities. The last observation permits a query-guard mutation to fail at an assertion *before* restrictive FKs mask it with a constraint exception; a separate ordinary variant executes the full real sweep.
- The recipient records only bytes delivered through the actual queue/adapter callback. Its guest-file reader accepts absolute POSIX paths under the fixture's frozen mount table, rejects Windows/relative decoys, reads the referenced file, and follows at most the specified secondary-to-primary link. Expected bytes are generated independently from the test input and compared in full, including the final report instructions. Missing/mismatched content is an observation for assertions, not a thrown setup failure. `CompletePromptsFor(id)` joins persisted source ID -> queue ID -> frozen wire -> matching complete `UserPrompt` above the actual attempt floor; it must not count marker-only matches or fixture-owned expected prompts.
- For PC-142, two independently built providers share the isolated store but have distinct queue lock instances. Pause at absent-key observation, then release both, and also replay sequentially after provider recreation. Never simulate a cross-context uniqueness race with two scopes sharing one queue singleton. For the unique-index control below, use direct independent inserts to reach the database backstop separately from service dedup.
- Seed historical states only for guard isolation; all delivery acceptance paths start with `RefineAsync` or the stated real queue producer. Do not set Pending/Sent/Confirmed, insert an expected successful transcript or attach a synthetic generation label to manufacture progress. A scripted recipient is sufficient for queue PCs; it cannot solve VD1-TD-1.

### Delivery inventory

| Path / producer -> destination | Durable identity and persistence boundary | Recovery and observable receipt |
|---|---|---|
| DL-2 `RefineAsync` -> bound running Grok session | Refinement GUID, SourceEventId, reserved QueueMessageId, accepted session/generation/binding; complete rendered bytes committed with Refined event before I/O | Real boot/periodic reconciler recovers no-row acceptance, primary file, keyed enqueue and link. Require one complete final-wire recipient UserPrompt plus exact guest read of the full rendering. Request accepted/event saved is not receipt. |
| DL-2 keyed insertion -> queue flush | Unique `SourceRefinementId` and reserved row ID; immutable pointer/hash, accepted ordering; queue transaction precedes progress/wakeup | Before/after insert failure, duplicate race, lost progress save and dropped wakeup recover the same row. Busy recipient gets no input until its actual TurnEnd; eligible recipient receives via scanner/normal flush without a fabricated business event. |
| Docker secondary spill -> same recipient, including queue SendNow/durable overlay Now/retry | Head queue GUID, ordered member IDs, original logical bytes, immutable `DockerSpillSnapshotJson`, full binding, file paths/hashes and final wire; snapshot before publication, attempt/final Body before first write | Restart across snapshot/file/attempt boundaries replays recorded members and bytes, excluding newly arrived rows. Receipt requires the complete secondary pointer and exact guest reads of secondary and primary artifacts where applicable. A queue row, Sent flag or successful write is insufficient. |
| Docker destination of profile-v1 completion -> caller | Existing notification/source ID and `CompletionDeliveryJson` remain receipt authority, sharing the frozen Docker wire/member/path/hash in the attempt transaction | Existing completion producer/scanner/queue, with the affected secondary cuts, must reach a complete caller UserPrompt and actual guest-file read. Preserve R-8; no new completion identity is introduced. |
| Row-less `EnqueueAsync(..., Now)` | No durable identity exists; oversized Docker input is refused before any file/write; no delivery obligation is claimed | The positive companion explicitly calls the durable Now/queued SendNow producer and follows its persisted row to receipt. Under-ceiling row-less Now and host fallback remain regression controls. Refusal is not delivered notification evidence. |

**Matrix:** cross busy=false/true with (a) acceptance rollback/commit before primary I/O, (b) primary temp-write/publish, (c) queue insertion failure/commit before progress and wakeup, (d) secondary snapshot rollback/commit before materialization, (e) secondary publication before attempt claim, (f) committed attempt before first input, (g) complete submitted prompt before verdict, and (h) verdict before receipt link. Run primary-only and two-level refinement variants where that boundary exists; there is no secondary snapshot in a primary-only case. A pre-acceptance rollback leaves no event/obligation/input and requires a fresh request; every accepted recoverable case preserves identity and ends with exactly one complete prompt. A terminal task, corrupt artifact, changed/unknown binding or explicit cancellation stays held/canceled with original bytes and no new input; existing genuine receipt is reconciled before any retry. The cross-generation delayed-receipt variant is rejected until VD1-TD-1 is specified.

Boundaries also include 4,000/4,001 display characters versus much larger full rendering; >1,000 lines; multibyte text; short Grok refinements; two distinct/identical messages accepted in one frozen second; cancellation before and after acceptance; failed B after successful A; no queue/Pending/Sent-null-verdict/Held/Confirmed/Canceled; exact/missing/corrupt primary and secondary files; and ceiling-1/ceiling/ceiling+1 **UTF-8 bytes**. Use a valid long mapped cwd for the inbox two-level case and a deliberately small configured modern ceiling for the same transport branch. Also test the real modern ceiling with large ordinary queue bodies. An 86,400-byte filesystem path is not a valid fixture: do not claim a real-modern two-level refinement path that the host/guest path limits reject. Final pointer overflow is an explicit hold with zero recursive spills.

Negative receipt cases retain the obligation: absent prompt, ID/header/prefix only, missing suffix, fragments in separate prompts, old full prompt below the floor, wrong session, wrong/unknown generation, screen-only/Sent-only and matching pointer with wrong file bytes. Test-design/ordinary Review must reject evidence ending before the recipient. Controlled callback transcripts prove queue and receipt logic, not Grok/Docker transport or model consumption; local mount-table reads prove mapping/content logic, not an actual Docker mount. Inherited V-7/V-8/V-9 remain the respective native/live qualification gates; they are not rerun by this document review.

### Proves it works now

These are implementation acceptance commands, not claims that the new methods already exist. B = `DockerGrokBriefPointerTests`, Q = `DockerGrokDeliveryRecoveryTests`, both in `tests/Antiphon.Tests/Application/`. Use the existing `Invoke-C575Selection` helper, project `Antiphon.Tests`, exact class/method below, fresh TRX and nonzero executed case counts. Keep every baseline V/R selection.

| ID | Behavior / layer | Test/command selection | Expected |
|---|---|---|---|
| V-5a | Atomic full rendering / PostgreSQL + real producer | Q `Refinement_acceptance_is_atomic`; `Refinement_full_body_survives_restart` | Rollback leaves neither row nor event; committed bytes/hash/length survive context recreation and recipient read beyond the event cap. |
| V-5b | Idempotent ordered recovery / service + real queue | Q `Same_refinement_enqueues_once`; `Queue_insert_failure_keeps_obligation`; `Committed_queue_recovers_without_wakeup` | Same reserved ID across races/cuts; no duplicate prompt; both busy and eligible recipients actually receive. |
| V-5c | Immutable mapped primary/secondary artifacts / file integration | B `Spill_identity_cannot_change_on_retry`; `Missing_spill_recovers_original_bytes`; `Queue_respill_keeps_guest_path` | Different existing bytes hold without input; missing files reappear at the original paths; full guest-read bytes and one complete wire prompt. |
| V-5d | Changed generation and late receipt / queue + ingestion | Q `Refinement_cannot_retarget_new_generation`; `Receipt_requires_bound_generation`; `Accepted_prompt_is_not_retyped` | No input to replacement generation; exactly one old receipt with no second submit when provenance proves the original attempt. VD1-TD-1 must close before this selection can prove both outcomes. |
| V-5e | Secondary failure / real queue | B `Secondary_spill_failure_types_nothing`; `Rowless_oversize_requires_durable_queue` | Retained original data and zero fallback writes; durable Now/SendNow positive companions reach complete receipt. |
| V-5f | Retention and remaining VD-1 guard variants / isolated database | Q/B exact methods in PC-149..172 below, plus Q `Unresolved_refinement_survives_retention` | Production prune/cancel/amend/freeze paths preserve the stated identities and evidence; recoverable delivery variants end at recipient evidence. |

### Guards the regression

- **R-10:** keep the amendment's `AgentTaskRefineTests/*`, `TypedBodySpillTests/*`, `DataRetentionServiceTests/*` selections. Preserve queued-goal editing, host Working/Dispatched refinement, Blocked/terminal refusal, host file/API/type-original behavior, null new fields on legacy rows, zero retention windows and unrelated stale-row pruning. Read an independently recreated context for every persistence assertion.
- **R-7 affected extension:** include `SessionMessageQueueSpillTests.Now_mode_oversize_types_the_pointer_and_persists_no_queue_row`, `A_small_now_mode_body_is_typed_whole_with_no_spill_file` and `Two_channel_rows_batched_over_the_ceiling_share_one_file_and_one_pointer`. Docker row-less refusal must not silently change these host contracts.
- **R-8 affected extension:** keep the existing completion selections and run their new Docker destination variants through the frozen secondary path; assert the completion receipt's wire/member/path/hash agrees with the Docker snapshot, including restart before receipt link. Host completion success alone is not this assertion.

### Guard inventory

The original G-1..G-140 mappings are inherited unchanged. G-141..G-148 retain their unique PC numbers; **G-147's executable mutation protects queue pruning only**. The other retention passes and other independently bypassable VD-1 decisions require distinct controls. No guard is excluded merely because another guard or FK can mask its mutation.

| Guard | Plan reference / safety-critical invariant | Positive control |
|---|---|---|
| G-141 | S4a acceptance transaction joins event and full obligation | PC-141 |
| G-142 | S4a replay of one refinement produces one queue identity | PC-142 |
| G-143 | S4b common store verifies an existing final artifact's original bytes | PC-143 |
| G-144 | S4b missing artifacts rematerialize from durable bytes at the same paths | PC-144 |
| G-145 | S4a/S4c original accepted generation gates first input | PC-145 |
| G-146 | S4b secondary spill failure cannot type fallback content | PC-146 |
| G-147 | S4c unresolved refinement queue/attempt row survives queue pruning | PC-147 |
| G-148 | S4b oversized row-less Docker Now refuses before I/O | PC-148 |
| G-149 | D-16 retention preserves unresolved destination transcript evidence | PC-149 |
| G-150 | D-16 task-tree pruning excludes retained refinement/task/event references | PC-150 |
| G-151 | D-16 session pruning excludes retained refinement destination references | PC-151 |
| G-152 | D-14/D-16 Confirmed/Canceled tombstones suppress replay after queue pruning | PC-152 |
| G-153 | D-16 terminal task forbids a new submit of accepted unconfirmed work | PC-153 |
| G-154 | D-16 cancellation commits obligation and linked queue cancellation together | PC-154 |
| G-155 | D-16 cancel-only shortcut refuses an attempted refinement | PC-155 |
| G-156 | D-16 cancel-only operation verifies the refinement belongs to the named task | PC-156 |
| G-157 | D-17 secondary snapshot commits before file or input effects | PC-157 |
| G-158 | D-17 replay uses original composed bytes, not Body after pointer replacement | PC-158 |
| G-159 | D-17 replay preserves frozen member IDs and excludes later queue input | PC-159 |
| G-160 | D-17 keyed refinement cannot be amended in place | PC-160 |
| G-161 | D-17 frozen non-refinement spill cannot be amended in place | PC-161 |
| G-162 | D-17 final guest pointer must fit the active byte ceiling | PC-162 |
| G-163 | D-17 completion receipt rendering agrees with the frozen Docker snapshot | PC-163 |
| G-164 | D-14 an earlier unresolved acceptance cannot be overtaken at enqueue | PC-164 |
| G-165 | D-14 acceptance rechecks task status at commit against concurrent settlement | PC-165 |
| G-166 | D-14 an existing source key must name the reserved queue ID | PC-166 |
| G-167 | D-14 an existing source key must name the accepted destination | PC-167 |
| G-168 | D-14 an existing source key must retain the immutable logical-wire hash | PC-168 |
| G-169 | D-14 database uniqueness independently prevents duplicate non-null source keys | PC-169 |
| G-170 | D-15 publication cannot expose a partially written final file | PC-170 |
| G-171 | D-14 unrepresentable input is refused without silently changing accepted bytes | PC-171 |
| G-172 | D-14 no Docker refinement is accepted before its destination binding is established | PC-172 |

### Positive controls

PC-141..148 retain the mutation and decisive assertion above, with the following required arrangements that make those assertions executable once the planned source exists:

| PC | Required arrangement and isolation of the stated red |
|---|---|
| PC-141 | Intercept the added refinement on acceptance save, catch only the armed failure, dispose the failed context, then query events and obligations afresh. Splitting the event commit leaves an orphan preview and makes `persistedRefinedEvents.Count.ShouldBe(0)` red. The new interceptor replaces the amendment's unsupported phrase "existing one-shot cut". |
| PC-142 | Keep other rows valid; mutate the replay branch to insert a fresh queue ID with **null** SourceRefinementId. Count *all destination rows*, including null-key rows, not only rows filtered by refinement ID. Suppress flush until that count is asserted. Then release the real queue and require one complete prompt on restored source. Include one-ID replay versus two fresh accepted IDs with identical text. |
| PC-143 | Corrupt primary and secondary files in separate rows after successful freeze and before any input, then recreate services. Mutate the shared create-or-verify existing-file comparison used on the final pre-input check. Allow the recipient to record wrong-file observations without throwing; `SubmittedBodies.ShouldBeEmpty()` must go red before later receipt-hash refusal can mask the unauthorized input. |
| PC-144 | Delete only the final owned file after its immutable record is committed; retain all durable bytes and a live exact-generation recipient. Primary/secondary rows recreate the same file, flush and scan to receipt. Mutation returns Held for absence; bounded scan completion precedes the exact prompt-count assertion, not a wait-until-delivered timeout. |
| PC-145 | Use a never-attempted row, so LastDeliveryGeneration is null and ordinary attempted-row recovery cannot mask the accepted-generation gate. Change only the live generation, preserving worker/container/mapping. Call actual queue flush as well as scanner. Baseline holds with zero writes; removing the pre-input accepted-generation comparison allows input. The old-generation late-receipt companion still depends on VD1-TD-1. |
| PC-146 | Reach a durable secondary snapshot with a fitting final pointer, then fail only secondary publication. Keep primary file, binding, rules and recipient eligibility valid. Mutation returns the original oversized body; the recorder accepts it so `SubmittedBodies.ShouldBeEmpty()` is red instead of a transport/fixture error. Cover WhenIdle, queued SendNow and durable overlay Now using the same production secondary path. |
| PC-147 | Produce an actual Sent/null-verdict row via the attempt cut, retain its obligation, advance the clock beyond queue retention, then call `PruneQueuedMessagesAsync`. Removing only the refinement predicate makes the fresh row lookup null; linked queue -> obligation Restrict does not prevent deletion of the referencing queue row. Continue unmutated recovery to real receipt. This does not prove the other three prune predicates. |
| PC-148 | Start an eligible Docker-bound recipient with rules ready and no pre-existing rows; submit ceiling+1 bytes via row-less Now. Record file and input effects and catch only the expected refusal. Mutate the single authoritative row-less-owner gate to take the legacy timestamp-spill branch; the intended red is nonempty submitted bodies. If implementation adds another independent null-owner refusal, split that guard/control before accepting this PC; removing one masked refusal is not a successful positive control. |

The following required additions are **verification inventory**, not edits to the selected fix. B/Q aliases resolve to the exact files/classes above; every suffix below is one exact method. Each control breaks only its named guard, keeps other prerequisites valid, and expects the stated assertion red. New method/observation names are implementation contracts, not existing APIs.

| PC / guard | Compiling production defect | Exact method and decisive assertion |
|---|---|---|
| PC-149 / G-149 | Remove only unresolved-refinement exclusion from `PruneTranscriptsAsync`. | Q `Refinement_transcript_survives_retention`: `persistedPrompt.ShouldNotBeNull()` for an aged stopped, non-persistent destination with a genuine previously submitted prompt and no unrelated notification protection. |
| PC-150 / G-150 | Remove only refinement-reference exclusion from the task-tree candidate query. | Q `Refinement_task_tree_is_not_a_prune_candidate`: `observedCandidateRootIds.ShouldNotContain(refinementRootId)` at the production pre-delete observation. Stop at that observation before an FK exception can mask red. Ordinary companion runs a whole sweep and proves an unrelated eligible tree was pruned. |
| PC-151 / G-151 | Remove only refinement-reference exclusion from the session candidate query. | Q `Refinement_session_is_not_a_prune_candidate`: `observedCandidateSessionIds.ShouldNotContain(destinationId)`. Stage a retained historical refinement whose task no longer names the destination; no PersistentSessionId/other protection. Observe the real deletion predicate's selected IDs before delete, as for PC-150; ordinary companion verifies the actual sweep. |
| PC-152 / G-152 | Let reconciliation fall through its terminal-obligation check and allocate a row when the old queue row is absent. | Q `Terminal_refinement_is_not_reenqueued`: `destinationQueueRows.ShouldBeEmpty()` after real confirmation or explicit cancellation, production queue pruning, provider recreation and two scans; zero additional recipient submits. |
| PC-153 / G-153 | Omit the terminal-task check immediately before queue input for a refinement. | Q `Settled_task_holds_unconfirmed_refinement`: `recipient.SubmittedBodies.ShouldBeEmpty()` after acceptance, terminal settlement and recovery, with same generation and valid artifacts; no synthetic receipt. |
| PC-154 / G-154 | Commit queue cancellation separately before obligation cancellation. | Q `Refinement_cancel_is_atomic`: `reloadedQueue.Status.ShouldBe(originalQueueStatus)` after an armed obligation-save failure; a fresh context also sees the original obligation. Successful replay cancels both and never resurrects at boot. |
| PC-155 / G-155 | Remove the attempt-evidence refusal from cancel-only refinement service. | Q `Attempted_refinement_cannot_use_cancel_shortcut`: `cancelAccepted.ShouldBeFalse()` for Pending-attempted and Sent-null-verdict setup with retained composer evidence. |
| PC-156 / G-156 | Omit the refinement-to-requested-task equality check. | Q `Refinement_cancel_checks_task_owner`: `foreignRefinement.State.ShouldBe(originalState)` after invoking cancellation under a different valid task ID; expected refusal is captured, not treated as fixture error. |
| PC-157 / G-157 | Move secondary materialization ahead of its snapshot transaction commit. | B `Secondary_snapshot_precedes_io`: `spillEffects.FinalPublications.ShouldBe(0)` when snapshot save is cut; also zero input. Cross the three durable entry points, using a body that reaches the secondary branch. |
| PC-158 / G-158 | Rematerialize a missing secondary file from current queue Body instead of frozen composed bytes. | B `Secondary_replay_uses_original_body`: `guestRead.Bytes.ShouldBe(originalComposedBytes)` after Body became a pointer and the file was removed; complete receipt still required. |
| PC-159 / G-159 | Recompose a frozen batch from all currently eligible rows. | B `Secondary_replay_keeps_members`: `reloadedSnapshot.MemberQueueIds.ShouldBe(originalMemberIds)` after a later input arrives before restart; receipt/read for the original batch excludes it. |
| PC-160 / G-160 | Omit only the SourceRefinementId arm of in-place amend refusal. | Q `Keyed_refinement_rejects_amendment`: `reloaded.Body.ShouldBe(originalBody)` after actual amend API on an unattempted primary-only refinement. |
| PC-161 / G-161 | Omit only the DockerSpillSnapshotJson arm of in-place amend refusal. | B `Frozen_spill_rejects_amendment`: `reloaded.Body.ShouldBe(originalBody)` for a non-refinement row frozen before its first attempt; no source-key guard masks it. |
| PC-162 / G-162 | Skip the UTF-8 bound on the final guest pointer. | B `Oversized_final_pointer_is_held`: `recipient.SubmittedBodies.ShouldBeEmpty()` for a valid mapped path whose final pointer is ceiling+1; assert no third spill. Ceiling and ceiling-1 companions reach receipt. |
| PC-163 / G-163 | Freeze completion delivery wire from the pre-secondary logical body instead of the Docker snapshot's final wire. | Q `Completion_and_docker_snapshot_agree`: `completionDelivery.WireText.ShouldBe(dockerSnapshot.FinalWireText)` at committed attempt, followed by complete caller prompt and guest-read assertions. |
| PC-164 / G-164 | Advance to the later accepted refinement after the earlier one's enqueue failure. | Q `Refinement_acceptance_order_survives_failure`: `recipient.CompletePromptsFor(secondId).ShouldBeEmpty()` while the first keyed insert is held; after release, complete prompts appear in AcceptedSequence order. |
| PC-165 / G-165 | Use the initial Working/Dispatched read without the commit-time terminal-state recheck. | Q `Settlement_wins_before_refinement_acceptance`: `persistedRefinements.ShouldBeEmpty()` when another context settles at the pre-acceptance barrier; no Refined acceptance event or input. If the task lock serializes settlement, test both real lock orders rather than forcing an impossible interleaving. |
| PC-166 / G-166 | Skip only the existing row's reserved QueueMessageId comparison. | Q `Refinement_existing_key_checks_queue_id`: `enqueueAccepted.ShouldBeFalse()` with a valid existing source row at another queue GUID; all other fields match. |
| PC-167 / G-167 | Skip only the existing row's destination comparison. | Q `Refinement_existing_key_checks_destination`: `enqueueAccepted.ShouldBeFalse()` with a valid foreign session and matching reserved ID/hash; no recipient writes. |
| PC-168 / G-168 | Skip only the existing row's immutable logical-wire hash comparison. | Q `Refinement_existing_key_checks_wire_hash`: `enqueueAccepted.ShouldBeFalse()` with matching source/row/session but another valid rendered pointer/hash. |
| PC-169 / G-169 | Make the CLI-generated migration's non-null SourceRefinementId index non-unique in the mutation snapshot. | Q `Refinement_source_key_is_database_unique`: `secondInsertSucceeded.ShouldBeFalse()` for two otherwise valid queue rows through independent contexts. Use a fresh migrated test template per invocation; catch the expected unique violation on fixed source. A migration/build failure is not red. |
| PC-170 / G-170 | Write directly to the final primary/secondary path before the write/flush completes. | B `Partial_spill_is_never_published`: `File.Exists(recordedFinalPath).ShouldBeFalse()` at an armed mid-write failure; no input, and recovery later publishes exact original bytes. |
| PC-171 / G-171 | Replace invalid text with U+FFFD before acceptance validation instead of refusing it. | Q `Refinement_rejects_lossy_storage_input`: `persistedRefinements.ShouldBeEmpty()` for U+0000 and an unpaired UTF-16 surrogate. The mutant stores valid but changed text, so red is an assertion, not PostgreSQL encoding failure. Valid multibyte companion round-trips exactly. |
| PC-172 / G-172 | Supply the current worker configuration as a binding when the accepted session has none. | Q `Refinement_requires_established_binding`: `persistedRefinements.ShouldBeEmpty()` with valid current manifest but absent accepted binding; zero file/queue/input effects. |

Do not count multiple independent comparisons as one positive control: PC-166..168 are deliberately separate. Primary/secondary are variants of the *same* store guard in PC-143/144/170; each path must actually call that shared guard. The accepted-binding/path checks already inventoried in G-27/G-59..61/G-102/G-136 gain retry variants after file publication without renumbering their recipes. Existing full-body/receipt/busy/scanner controls gain the VD-1 variants above, including a failing row before another session's recoverable row, pagination and NextAttemptAt boundaries. If Code introduces independent bypassable gates instead of those shared guards, its exit inventory must split them; a masked mutant is never credited.

Mutation reports break, exact-method assertion red, restore and fresh green **after land**. Code implements the tests and runs ordinary V/R; ordinary Review judges design and evidence before land. Use one method-scoped filter per cycle, e.g. `/*/*/DockerGrokDeliveryRecoveryTests/Refinement_acceptance_is_atomic`; run every parameter row in that method. No test/fixture edits to manufacture red, no zero-test/build/constraint failures as red, no class-wide PC cycle, no snapshot access from Docker Desktop. Keep per-PC evidence at the assigned external root and restore exact bytes/timestamps.

### Out of scope

- Re-auditing or rewriting the original 140 recipes and baseline floors. Only the VD-1-affected PC-81/139 issues above are returned for resolution; no unrelated Docker launch/auth/lifecycle decision is reopened.
- Implementing the outbox, scanner, migration, file store or tests in this task. These are not present at the inspected SHA; this stage specifies executable contracts and reports the remaining architectural seam.
- Native/live worker qualification, secrets, image builds, deployment and authentication. Existing gates remain; the scripted recipient cannot replace them.
- A standalone primary-pointer fixture beyond actual filesystem path limits. The configured-ceiling variant and ordinary oversized-body modern test explicitly cover the branch without claiming an impossible path.

### Cost

All figures are **estimated execution floors**, not measurements. Test authoring, Plan repair, ordinary Review reading and operator wait are additional. The original Code floor 174 = setup/build 35 + V/R 139, and Mutation floor 413.5 = setup 8 + initial green 83 + cycles 322.5, remain verbatim above.

| Inventory / work | Calculation | Minutes |
|---|---|---:|
| Submitted VD-1 Code delta, arithmetically correct | 8 x 0.85 + 2.2 expanded cases/R-10 | 9 |
| Submitted VD-1 Mutation delta, arithmetically correct | 8 x 0.85 initial green + 8 x 3 red/restore/green | 30.8 |
| Submitted eight-control totals | Code 174 + 9; Mutation 413.5 + 30.8 | **183 / 444.3** |
| Required 24 additional ordinary exact methods | 24 x 0.85 | 20.4 |
| Required 24 additional initial-green selections | 24 x 0.85 | 20.4 |
| Required 24 additional PC cycles | 24 x (0.60 build + 0.85 red + 0.10 restore + 0.60 rebuild + 0.85 green) | 72 |
| Revised Code floor for the inventoried decisions | setup/build 35 + V/R (139 + 9 + 20.4) | **203.4** |
| Revised Mutation floor | setup 8 + initial green (83 + 6.8 + 20.4) + every cycle (322.5 + 24 + 72) | **536.7** |
| Total inventoried verification floor | 203.4 + 536.7 | **740.1 (12 h 20 m 6 s)** |

Ordinary selections are the preserved V/R roster plus B/Q methods and R-10 above; PC selections are the individual exact methods. All added controls use the existing I-group rate. The revised inventory is P=15, U=45, I=112, total 172. Additional cost beyond the submitted VD-1 total is 112.8 minutes. Savings credited: **0 minutes**; no run established caching, batching or concurrency savings. The 0.85-minute method rate is inherited budgeting, not a promise that a parameter matrix finishes in 51 seconds. Code/Mutation must report actual expanded case counts/timings and update the forward estimate if measured costs differ. No second full-suite pass is commissioned here.

**Handoff audit:** inspected bodies and nearest fixtures are listed; inventory guards=172, mapped=172, missing mappings=0, duplicate PC mappings=0. PC-141..172 have concrete mutation, exact method, decisive assertion and specified setup contracts; they have not been implemented or executed. **The all-PC-executable Code gate is not met:** affected original PC-81 has a surviving stated assertion, and affected original PC-139's late-original-versus-replacement receipt distinction lacks a production evidence seam. This is a rejected Code handoff, not an assertion that all 172 PCs are ready. Plan must close VD1-TD-1, approve the PC-81 receipt assertion correction, and reconcile the additional inventory/cost; return that focused change to TestDesign. No human preference is required to identify these gaps. **Next: plan.**
