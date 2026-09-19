# CARD-0490 — Phone-home runner (container Grok session)

**Status:** architecture confirmed from the live desktop runner, current tree, and CARD-0575's already-measured container canaries. Not a product bug; feature inventory. Investigate task: `da82967d`. Card: CARD-0490.

**Date:** 2026-09-19. Host API `GET /api/version` SHA `3c7a4057bc363cfcb1afa6dced63f411e9f243ce` (`land-v2`). Live runner `GET :17204/capabilities` `ptyBackend=ModernConPty`, `verificationCustodyBackend=windows-job-v1`, `runnerStoreId=6c459598-54af-4cb9-84f7-380c578fedc1`.

Related: CARD-0575 (superseded exec-wrapper; investigation still valid on `feat/card-task-4fe68cac` at `docs/investigations/2026-09-19-card-0575-docker-grok-worker.md`), CARD-0038 (Linux/PTY portability, still Backlog).

Out of scope here: implementing, privileged DinD, putting the AppHost in Linux, reopening CARD-0575 Option A.

## Verdict

The local 17204 SessionRunner does **not** phone home. Antiphon is the HTTP client of a single configured `SessionRunner:BaseUrl`; the runner never calls the server. There is no register route, no runner fleet table, and no runner-initiated heartbeat. The "heartbeat" that exists is an SSE keepalive the **server** reads while it holds `GET :17204/events`. CARD-0575's close note that 17204 "already" registers and heartbeats is false; Plan must not copy it.

A minimal phone-home Grok worker in a Linux container is blocked by **spawn + dispatch targeting**, not by CARD-0038 slice 1's ceiling bug:

| CARD-0038 item | Minimal in-container Grok session |
|---|---|
| Slice 1 (`PtyBackend` third state) | **Defer for Grok-only.** Grok briefs already spill (`BriefInlineMaxBytes=0`). Wrong InboxConhost ceilings would still lie in `/capabilities` and would clip Claude later. |
| PtyHost spawn (`Antiphon.PtyHost.exe` + `CreateProcessW`) | **Hard blocker.** Production sessions always go through detached PtyHost. linux-x64 publish emits `Antiphon.PtyHost` (no `.exe`) and `libporta_pty.so`; launcher still looks for `.exe` and `--spawn` always P/Invokes `CreateProcessW`. |
| `grok.exe` vs Linux `grok` | **Hard blocker** for a real Grok child. |
| `ANTIPHON_API=http://localhost:17202` | **Hard blocker** for callback from inside the container. CARD-0575 measured the working origin: `http://host.docker.internal:17202`. |
| Host worktree Windows cwd | **Hard blocker for a Worktree task.** Not a blocker for one standing session on a bind-mounted `/work`. |
| Job objects, Herdr, shadow-copy rationale, `.ps1` ops, SkipIfNotWindows density, POSIX paste-envelope measurement | **Defer** for a Grok-only canary. |

Antiphon-side protocol work is also a hard blocker even if Linux spawn were fixed: one `ISessionRunnerClient` bound to `http://localhost:17204`, reconcilers that treat "the" runner's `GET /sessions` as the whole fleet, and no way for an unpublished container (or a remote NAT host) to become a dispatch target.

## 1. CARD-0575 findings reused (not re-measured)

CARD-0575 closed 2026-09-19 as superseded, not shipped. Close reason (revision 12) pivoted off `docker exec` because Plan/TestDesign grew from 140 recipes to G/PC-149..172 with floors **203.4 min Code / 536.7 min Mutation** (plan on `feat/card-task-f7cf0fe7`, D-20). The exec-wrapper had to re-implement generation provenance and delivery receipts on the Windows host for a Linux child the Job Object does not own.

Investigation `4fe68cac` (`docs/investigations/2026-09-19-card-0575-docker-grok-worker.md` on `origin/feat/card-task-4fe68cac`) remains valid:

| Fact | Evidence |
|---|---|
| Linux Grok is ELF `grok` 1.0.34, not host `grok.exe` | 0575 §2; laptop canary on CARD-0575 revision 6 |
| `ANTIPHON_API` from a desktop container is `http://host.docker.internal:17202` (`/health` `Healthy`, `/api/boards` JSON). Caddy `https://antiphon.desktop.codeperf.net` is the SPA on 17203 | 0575 §1 |
| `GROK_HOME` bind-mount or named volume survives restart / replace / rebuild; unmounted container has no `auth.json`; do not bake credentials | 0575 §3 |
| Host-socket docker works; binding host 17280 is refused (`port is already allocated`); live `antiphon-postgres` stays up | 0575 §4 |
| `src/Antiphon.SessionRunner` contains **zero** `docker` references; v0 exec was never in the runner | 0575 §2; re-checked this tree |
| Image has no `pwsh`; HTTP callback is enough | 0575 §5 |

Operator lock retained: Docker-from-container is host socket (Option A), not DinD.

## 2. How the local runner actually talks to Antiphon

Direction is **server → runner**, configured once:

- `server/appsettings.json:62-68` — `SessionRunner:BaseUrl = http://localhost:17204`
- `Antiphon.AppHost/Program.cs:64` — `SessionRunner__BaseUrl=http://localhost:17204`
- `server/Program.cs:262-272` — one typed `HttpClient<ISessionRunnerClient, SessionRunnerHttpClient>` plus a second named client for SSE. No runner list.
- `SessionRunnerHttpClient` ctor sets `BaseAddress` from that single URL (`SessionRunnerHttpClient.cs:48-49`). `StartAsync` **POSTs** `sessions` (`:51-104`). `StreamEventsAsync` **GETs** `events` (`:478-491`).
- `SessionRunnerEventPump` (`server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:27-104`) is a hosted service in the **server**. It calls `ListAsync`, backfills transcripts, then consumes the SSE stream. On disconnect it waits `EventReconnectDelayMs` and retries the same BaseUrl. That is the liveness loop. It is not a runner heartbeat.
- Runner `/events` (`src/Antiphon.SessionRunner/Program.cs:354-392`) writes `: connected` then `: keepalive` every `Events:KeepAliveSeconds` (default 15). The server idle-watchdog is 90 s (`SessionRunnerSettings.EventStreamIdleTimeoutSeconds`). Keepalives exist so the **server's client** does not time out.
- `src/Antiphon.SessionRunner` has **zero** `HttpClient` usages and zero `ANTIPHON_API` references (rg). The runner process does not call 17202.
- `HerdrStatusPushService` heartbeats are runner → Herdr, not runner → Antiphon (`HerdrStatusPushService.cs:36-46`).
- `RunnerStoreId` on `/capabilities` is verification-custody identity (`windows-job-v1` on the live runner), not fleet registration. `AgentSession` has no runner-id column (`server/Domain/Entities/AgentSession.cs`).
- Reconciliation (`SessionReconciliationService.cs:182-216`) lists **the** runner. A DB session the single runner does not know is Failed: *"Session runner does not know this session"*. A second live runner would have its sessions closed as unknown, or hide the first runner's deaths, depending on which BaseUrl was configured.

Launch path the canary would reuse (once targeting exists): server composes `AgentLaunchSpec` (`Exe`, args, env, cwd) → `POST /sessions` → runner `PtyHostLauncher.LaunchDetachedAsync` → named pipe → `PtyAgentRunner` → child. Transcript events flow runner SSE → `SessionRunnerEventPump` → DB. Delivery still uses the existing queue (LF, bracketed paste, separate Enter). That contract is worth keeping; the missing piece is **which process is the runner** and **who opens the TCP connection**.

## 3. CARD-0038 slice 1+2 — hard vs deferred for one Grok session

### Slice 1 — third `PtyBackend` state (small, still wrong, not the Grok gate)

Confirmed in this tree, same mechanism CARD-0038 named:

- `PtyBackend` has two values: `InboxConhost`, `ModernConPty` (`src/Antiphon.Agents.Pty/PtyBackend.cs:4-17`).
- `ConPtyRedistributable.TryLocate` returns false on non-Windows with `"not Windows — there is no pseudoconsole to redirect"` (`ConPtyRedistributable.cs:81-84`).
- `PtyBackendPolicy.Resolve` then falls back to InboxConhost (`PtyBackend.cs:91-94`) with reason *falling back to the inbox conhost; the paste ceilings still apply*.
- Inbox ceilings: brief **900 B**, tripwire **1 024 B** (`DelegationSettings.cs:134`, CARD-0038 text). Modern: **43 200 / 86 400** (`:199`).
- Live Windows runner reports `ModernConPty` and `ptyBackendFellBack: false`. A Linux process with `ANTIPHON_PTY_BACKEND=modern` would advertise InboxConhost fallback.

Why this is **not** a hard blocker for Grok-only: `PtyDeliveryCeilings.RequiresJoinSafeDelivery` is true for every kind except ClaudeCode (`PtyDeliveryCeilings.cs:79`). `ForAgentKind` sets `BriefInlineMaxBytes = 0` so Grok briefs/refinements always spill to a file and only a pointer is typed (`:99-105`, reason "joins typed lines, measured (CARD-0084)"). A 900-byte inline ceiling never sees the brief body.

It **is** still a lying capability bit and a Claude blocker. POSIX paste-envelope numbers remain unmeasured (`PtyPasteMarkerExperiments.Real_claude_delivery_envelope`, CARD-0038). Do not guess "POSIX is lossless."

### Slice 2 — Windows-bound inventory, verdict for a Grok canary

Porta.Pty **does** ship Linux natives. NuGet `porta.pty/1.0.7` contains `runtimes/linux-x64/native/libporta_pty.so` and `linux-arm64`. A `dotnet publish -r linux-x64` of `Antiphon.PtyHost` (this worktree, isolated `OutputPath=bin-card0490-linux/`, then deleted) produced:

- `Antiphon.PtyHost` (75 368 B, **no `.exe`**)
- `Antiphon.PtyHost.dll`
- `libporta_pty.so` (16 560 B)
- still a `conpty/` tree of Windows redistributable files
- `Vanara.PInvoke.Kernel32.dll` (pulled by `ModernConPtyConnection`)

| Item (CARD-0038 list) | Current code | Minimal Grok-in-container |
|---|---|---|
| **`PtyHostLauncher` + `Win32ProcessSpawner`** | `HostExeName = "Antiphon.PtyHost.exe"` (`PtyHostLauncher.cs:13`). Every launch adds `--spawn` (`:167`). `--spawn` calls `Win32ProcessSpawner.StartDetachedWithFallback` → `CreateProcessW` (`PtyHost/Program.cs:6-13`, `Win32ProcessSpawner.cs:24-61`). Named pipe `antiphon-pty-{guid}` (`PtyHostMessages.cs:148`) via `NamedPipeServerStream` (`PtyHostServer.cs:32-37`). | **Hard.** No session starts. `.exe` name misses the linux-x64 file; `--spawn` cannot run. In-process `PtyAgentRunner` exists but production `SessionRunnerRuntime` always uses the detached host (`SessionRunnerRuntime.cs:37, 101, 2037`). POSIX `NamedPipeServerStream` is unmeasured (likely a Unix socket under `/tmp/CoreFxPipe_*`). |
| **`ModernConPtyConnection`** | Win32 ConPTY. Used only when policy resolves `ModernConPty`. | **Defer** if Linux takes Porta's inbox/POSIX spawn. Do not request `modern` on Linux until slice 1 exists. |
| **`WindowsJobObject`** | Throws `PlatformNotSupportedException` on non-Windows (`WindowsJobObject.cs:34-35`). Called only when `memoryLimitMb > 0` (`PtyAgentRunner.cs:182-188`). Default `AgentSessions:MemoryLimitMb = 0` (`AgentSessionSettings.cs:13`). | **Defer.** Keep memory limit 0. Do not send `windows-job-v1` verification bindings (live runner advertises that backend; Linux must not). |
| **Shadow-copy** | `ShadowCopyStore` exists because "a running exe locks its file" (`ShadowCopyStore.cs:8-12`). Harmless extra copy on Linux. | **Defer.** The `.exe` filename is the bug, not the copy. |
| **PowerShell shell-outs** | `SessionRunnerRuntime`: none (rg). `AgentControlService`: operator error strings naming `restart-session-runner.ps1`. `DelegationWorktreeService`: Claude orchestrator PreToolUse deny hook via `powershell.exe` (`:341-353`). `WorkspaceHookRunner`: already `powershell.exe` vs `/bin/sh` (`WorkspaceHookRunner.cs:121`). `WindowsRcBridgeProbe`: Claude `%USERPROFILE%\.claude\sessions\<pid>.json` + `GetExtendedTcpTable` (`WindowsRcBridgeProbe.cs:9-23`). Herdr pane launch requires a PowerShell shell (`HerdrPaneChild.cs:1283-1285`) and Herdr's named pipe is Windows-only (`HerdrClient.cs:514`). | **Defer** for PtyHost Grok. Do not enable Herdr in the worker image. Claude orchestrator deny-hook and RC probe stay on the Windows server. |
| **`.ps1` ops / Scheduled Tasks** | `scripts/run-daemon.ps1`, autostart task. Repo `Dockerfile` publishes **Antiphon.Server**, not the runner (`Dockerfile:23`, `ENTRYPOINT ["dotnet", "Antiphon.Server.dll"]`). `docker-compose.yml` is the old server+postgres stack. | **Defer.** Container entrypoint can be `dotnet Antiphon.SessionRunner.dll`. Not a session-spawn blocker. |
| **`TranscriptTailer` cwd encoding** | Claude exact path: `Uri.EscapeDataString(Path.GetFullPath(cwd))` (`SessionRunnerRuntime.cs:1907-1911`). Grok: `{GROK_HOME}/sessions/{Uri.EscapeDataString(Path.GetFullPath(cwd))}/{id}/updates.jsonl` (`GrokTranscriptTailer.cs:87-104`). CARD-0575: this encoding on **Windows** will not match a Linux grok cwd. | **Soft if runner and grok share the Linux filesystem** (same `Path.GetFullPath`). Still **unmeasured** whether Linux grok 1.0.34 uses that encoding. GUID locate exists (`TryLocateSessionDirectory`) but the bound path is computed up front. Canary must prove `updates.jsonl` bind, not only TUI start. |
| **`GrokRulesFileStore`** | Writes `SessionLogPath/instructions/grok/{session:N}/rules.md` with `Path.Combine` (`GrokRulesFileStore.cs:8-12`). CARD-0575 plan: `ValidateReceiptPath` already accepts POSIX grammar. | **Soft.** Should work if the Grok child is told the Linux path. Unmeasured in a container. |
| **SkipIfNotWindows** | **96** matching lines in **14** test files this tree (was "95" on CARD-0038). Plus other `IsOSPlatform(Windows)` skips (`ClSession`, headed gates). | **Defer** for a product canary. A green Linux test run that skips the pty contract is not evidence, as CARD-0038 said. |
| **Worktrees / AllowedRoots** | `Git:WorktreeBasePath = C:\\Antiphon\\worktrees` (`server/appsettings.json:13`). Dispatcher creates host worktrees and puts that Windows cwd on the launch spec. CARD-0575 plan D-6 refused Worktree/SourceLanding for v0 for this reason. | **Hard for task-shaped work.** **Not hard** for one standing session whose cwd is a bind-mounted Linux path. |
| **Callback env** | `DelegationSettings.ApiBaseUrl` default `http://localhost:17202` (`DelegationSettings.cs:317`). Injected as `ANTIPHON_API` by `AgentTaskDispatcher.BuildEnv` (`:4498`) and `AgentSessionLaunchComposer` (`:47`). `localhost` inside the container is the container. | **Hard** for `card.ps1`/`delegate.ps1`/HTTP callback. Origin must be the worker-reachable API, not Caddy. |

## 4. What the phone-home protocol needs on Antiphon's side

Reuse the **session** protocol (launch DTO, capabilities, SSE event names, generation, transcript formats `claude|grok|codex`, grokRulesFileV1). Do not reuse the **connection** direction.

Must exist before a container is a dispatch target:

1. **Runner identity.** Something the server can store besides a config URL. Today's nearest object is `RunnerStoreId` on capabilities (custody), plus named `Agent` rows (workers). Neither is "a SessionRunner process that just booted in a container."
2. **Registration from the runner.** The container can already open `http://host.docker.internal:17202` (0575 §1). The server cannot open an unpublished container port, and cannot open a remote NAT host. Pull-from-BaseUrl therefore cannot discover the worker. A register POST (capabilities, kinds, capacity, store id, maybe advertised listen URL) is the inversion.
3. **Liveness.** Today: server watches SSE keepalives on a connection **it** opened. Phone-home: the runner must keep a connection the server did not have to dial (SSE/WebSocket from runner → server, or a heartbeat POST). Idle timeout analogue: `EventStreamIdleTimeoutSeconds=90` with 15 s keepalives. Stale runner must stop being a dispatch target; its DB sessions must reconcile against **that** runner, not against 17204.
4. **Dispatch targeting.** `POST /sessions` and input/kill/snapshot have to reach the runner that owns the session. With NAT that means commands travel on the runner-initiated connection (or a reachable published port on the desktop only). `ISessionRunnerClient` is a singleton; `Agent`/`AgentSession` have no runner foreign key. Standing `delegate.ps1 -Agent` can pin a **worker**, not a **runner**.
5. **Per-runner reconciliation.** `SessionReconciliationService` currently fails any session the one runner does not list. Multi-runner without scoping that query is a data-loss bug.
6. **Worker env.** `ANTIPHON_API` (and brief-pointer hosts) must be the origin **that worker** can call. Leave the desktop installation's localhost origin for local Windows sessions.
7. **Kind/exe on that runner.** `Agents:Definitions:grok:Exe = grok.exe` (`server/appsettings.json:91-95`). Linux needs `grok` (or a runner-local rewrite). Do not silently change the Windows definition.
8. **Lifecycle (CARD-0490 hard requirement, still absent).** No prune of stopped containers or superseded images exists in SessionRunner or the AppHost. 0575 parked this on 0490. Not needed to prove register+one session; needed before shipping unbounded `docker run`.

Not required for the first slice: Herdr on the worker, verification custody, Worktree/SourceLanding, Claude/Codex in the image, a second phone-home protocol beside the existing runner DTOs.

## 5. Minimal first slice (scoping)

One desktop Linux container, one standing Grok session, visible on the Antiphon board.

In:

- Runner process **inside** the container (the CARD-0575 pivot), talking **out** to `http://host.docker.internal:17202`.
- Linux PtyHost spawn (filename + POSIX detach; Porta.Pty `.so` is already in the linux-x64 output).
- Linux `grok` 1.0.34, `GROK_HOME` host bind-mount, `ANTIPHON_API` worker origin, host-socket docker already measured.
- Register + heartbeat + one launch of `AgentKind.Grok` with cwd on a bind-mounted workspace (Shared/ReadOnly), transcript `UserPrompt` evidence for one trivial turn.
- Fail clearly when home is unreachable (0575 requirement).

Out:

- CARD-0038 slice 1 as a prerequisite **if** the canary stays Grok-only (still do it before Claude).
- Job objects / cgroups, Herdr, shadow-copy redesign, `.ps1` daemon story, SkipIfNotWindows audit.
- Host worktrees, SourceLanding, `windows-job-v1` custody.
- Remote machine / server2 (same protocol, different `ANTIPHON_API`; 17202 is not the Tailscale path — 0575, not re-measured).
- Image prune policy (follow-on slice on this card, not a different card).
- Re-implementing `docker exec` (superseded; 140→172+ PCs were generation/receipt duplication).

Canary that is **not** this slice: CARD-0575's `docker exec -t` + `wget $ANTIPHON_API/health` + grok `--version`. That proved the image, not a phone-home runner.

## Remaining uncertainties

- Whether Porta.Pty 1.0.7 actually opens a POSIX pty and delivers Grok's composer (mouse/bracketed-paste) on linux-x64 in this tree. `.so` presence is not a delivery envelope.
- Linux grok 1.0.34 `updates.jsonl` directory encoding vs `Uri.EscapeDataString(Path.GetFullPath(cwd))`.
- Whether .NET `NamedPipeServerStream` + `CurrentUserOnly` works in Docker (user namespaces).
- Whether a published container port would let the **existing** pull model reach a *local* worker (desktop only). Remote NAT still cannot; the pivot asked for phone-home, so this was not measured as a substitute.
- Authenticated Grok turn visible as an Antiphon session (0575 listed this as still missing; laptop card text reports a headless PONG with mounted auth, not an Antiphon session row).
- OAuth `auth.json` copied to a second machine (0575, not measured).

## Not done, noted

Plan should invert the connection (runner registers and keeps a server-facing stream) and port PtyHost spawn/exe naming to Linux for Grok-only, rather than wrapping `docker exec` or waiting for the full CARD-0038 audit.
