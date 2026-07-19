# Spec: PTY-Host Split — Sessions Survive Runner Restarts

Date: 2026-07-19
Status: **Draft — awaiting review** (answer the Q-IDs in [Decisions](#decisions-for-review), same format as the 2026-05-19 spec)
Predecessor: [2026-05-19-session-runner-split-questions.md](2026-05-19-session-runner-split-questions.md) — which deliberately deferred this: *"Q23: Should a running session survive runner restart? No, not initially"* and listed "Preserving sessions across runner process restart" as a non-goal. This spec lifts that non-goal.

## Problem

The session-runner owns Claude processes via ConPTY (`PtyAgentRunner` → `Porta.Pty.PtyProvider.SpawnAsync`). ConPTY handles live inside the runner process, so **any runner restart kills every live session**, two ways:

1. `restart-session-runner.ps1` uses `taskkill /T /F` — kills the whole tree (`cmd.exe` wrapper → `dotnet` → `SessionRunner.exe` → `claude`).
2. Even a graceful runner exit closes the pseudo-console; ConPTY terminates attached clients. There is no way to re-attach a ConPTY to a surviving process.

Result: iterating on runner code (transcript tailing, event hub, liveness sweep — the churny parts) means killing live agent sessions. The May split protected sessions from **server/frontend/AppHost** restarts; this spec protects them from **runner** restarts.

## Solution shape (the tmux model)

Introduce a per-session **pty-host**: a tiny, deliberately boring process that owns exactly one ConPTY + child and almost never changes. The runner becomes a stateless-ish coordinator that connects to hosts over named pipes and can restart freely, re-adopting live hosts on startup — the same pattern `DaemonProcessService.InitialiseAsync` already uses to adopt the runner itself ("port already listening — adopting"), pushed one level down.

```
before:  server ──HTTP/SSE──> runner ──ConPTY──> claude          (runner dies => claude dies)

after:   server ──HTTP/SSE──> runner ──named pipe──> pty-host ──ConPTY──> claude
                              (restartable)          (stable, 1 per session)
```

### Responsibility split

| Concern | Today | After |
|---|---|---|
| ConPTY spawn, child lifetime, job object / memory limit | runner (`PtyAgentRunner`) | **pty-host** |
| Raw output capture, monotonic sequence numbers | runner (`RunnerSession.OnData`) | **pty-host** (seq must survive runner restarts) |
| Replay ring buffer + `{sessionId}.ansi.log` append | runner (`RunnerBuffer`) | **pty-host** (must capture output while runner is down) |
| PTY audit chunk recording (`PtySessionAudit`) | runner | **pty-host** |
| Input, resize, kill | runner → PTY | runner → pipe → **pty-host** |
| Rendered screen (`TerminalScreen`), snapshots | runner | **runner** (rebuilt from ansi.log on adopt — interpretation stays in the restartable layer, per Q16 philosophy) |
| Transcript tailing (`TranscriptTailer`), JSONL normalize | runner | **runner** (unchanged — file-based, naturally resumable) |
| HTTP API, SSE event hub, liveness sweep, audit cleanup | runner | **runner** |
| DB, orchestration, turn detection, SignalR | server | **server** (unchanged) |

The host is a byte pump + process babysitter. Everything with product logic in it stays in the runner where it can be iterated without killing sessions.

## New project: `src/Antiphon.PtyHost`

Tiny console exe (references `Antiphon.Agents.Pty` for the spawn primitives; **no** ASP.NET, no DI container — keep startup < 100 ms and memory small). One instance per session.

### Host lifecycle

1. **Spawn (empty)** — runner launches `Antiphon.PtyHost.exe --session <guid> --pipe antiphon-pty-<guid>`. No launch spec on the command line (env vars may contain secrets; quoting is a bug farm). Host opens the pipe server and waits.
2. **Launch** — runner connects and sends `Launch{exe, args, env, cwd, cols, rows, memoryLimitMb}`. Host spawns the ConPTY child, assigns the job object, writes its **manifest** (below), replies `Launched{childPid, childStartTime}`.
   - If no `Launch` arrives within 30 s (runner crashed mid-start), host self-terminates and deletes its manifest.
3. **Streaming** — host reads PTY output, assigns monotonic `seq` per chunk, appends to `{sessionId}.ansi.log`, keeps a bounded in-memory ring of recent chunks, pushes `Output{seq, chunk}` to the attached client.
4. **Detach/reattach** — pipe drop (runner died/restarting) is *normal*: host keeps running, keeps capturing output to log + ring. On reconnect the client sends `Attach{lastSeq}`; host replays from the ring if `lastSeq` is within it, else replies `Resync` and the runner rebuilds from the ansi.log.
5. **Child exit** — host records `{exitCode, exitReason}` in the manifest, publishes `Exited` if attached, then **lingers** serving reads until the runner acks with `Shutdown` (or a linger TTL expires — default 24 h). An exit during runner downtime is therefore never lost.
6. **Kill** — `Kill{timeoutMs}` kills the child (existing `PtyAgentRunner.KillAsync` semantics); host then follows the exit path above.

### Pipe protocol

Named pipe `antiphon-pty-<sessionId>`, ACL'd to the current user, single client at a time (a second connect bumps the first — newest runner wins). Length-prefixed JSON frames. First frame each direction is `Hello{protocolVersion, hostVersion}` / `HelloAck`.

Client→host: `Hello, Launch, Attach{lastSeq}, Input{data}, SendLine{line}, Resize{cols, rows}, Kill{timeoutMs}, ClearLiveBuffer, Status, Shutdown`
Host→client: `HelloAck, Launched, Output{seq, chunk}, Exited{code, reason, lastSeq}, StatusReply{status, childPid, childStartTime, cols, rows, lastSeq, exitCode?, exitReason?}, Resync, Error`

Protocol is versioned and append-only. The host is kept so thin that bumping it should be rare; on version mismatch at adopt the runner adopts in **degraded mode** (streams + kill only) and flags the session `HostOutdated` so the UI can offer "restart session to upgrade".

### Manifest — `logs/pty-hosts/<sessionId>.json`

```json
{
  "schemaVersion": 1,
  "sessionId": "…",
  "pipeName": "antiphon-pty-…",
  "protocolVersion": 1,
  "hostPid": 12345, "hostStartTime": "…",      // pid-reuse guard, same trick as ProcessLivenessProbe
  "childPid": 12399, "childStartTime": "…",
  "exe": "claude.exe", "cwd": "C:\\…", "cols": 120, "rows": 30,
  "transcriptEnabled": true,
  "createdAt": "…",
  "exitCode": null, "exitReason": null, "exitedAt": null
}
```

Written atomically (temp + rename). Env is deliberately **not** persisted (secrets).

### Detachment mechanics (the Windows-specific part)

Three separate mechanisms, all required:

1. **Break the parent chain** — `taskkill /T` walks live parent→child links. The runner never spawns the host directly; it invokes `Antiphon.PtyHost.exe --spawn …` which `Process.Start`s the real host and exits immediately. With the intermediary gone, the tree walk from the runner can't reach the host (double-spawn / "double fork").
2. **Escape job objects** — dev runs can wrap the runner in a job (Aspire DCP does; that's why `DaemonProcessService` uses `UseShellExecute = true` "detach from AppHost job object"). The spawner starts the host with `CREATE_BREAKAWAY_FROM_JOB`. The **child's** memory-limit job is created and owned by the host, as today but one level down.
3. **Shadow-copy the host binary** — hosts must NOT run from `src/Antiphon.PtyHost/bin/…`: on Windows a running exe locks its file and would break the next `dotnet build`, and a rebuild mid-flight would version-skew running hosts. On first launch of a given build, the runner copies the published host to `logs/pty-hosts/bin/<version-dir>/` and launches from there. Old versions keep running old binaries; a cleanup pass removes unreferenced version dirs.
   - **Version-dir naming (Q4 decision):** assembly `InformationalVersion` is NOT unique across local dev builds, so dirs are keyed by **content**: `<yyyyMMdd-HHmmss>-<sha8>` where `sha8` is the first 8 hex chars of a SHA-256 over the host's build output (all files, stable order). Before copying, the runner looks for an existing dir with the same `-<sha8>` suffix and reuses it (same binary → no duplicate copy); otherwise it creates a new dir stamped with the current UTC time. The date prefix makes dirs sort oldest-first, so cleanup is "delete unreferenced dirs from the lowest/oldest up".

## Runner changes (`Antiphon.SessionRunner`)

- **`RunnerSession` becomes a pipe client.** `PtyAgentRunner` usage moves to the host; the session facade keeps the same surface (`ToDto/GetBuffer/GetSnapshot/WriteAsync/Resize/KillAsync`) but talks over the pipe. `TerminalScreen` is fed from `Output` events; on adopt it is rebuilt by replaying the tail of the ansi.log (runner knows cols/rows from `StatusReply`).
- **Adoption sweep on startup** (mirrors `DaemonProcessService.InitialiseAsync`):
  1. Scan `logs/pty-hosts/*.json`.
  2. Host pid alive + start-time matches → connect, `Hello`/`Status`/`Attach`, reconstruct the session (status Running or Exited-pending-ack), restart `TranscriptTailer` (already resumable — sequences are stable per file order), publish `SessionAdopted`.
  3. Manifest has exit recorded, host alive → publish the missed `SessionExited{code, reason}`, then `Shutdown` the host.
  4. Host pid dead (or pid reused) → child is necessarily dead too (ConPTY died with the host): publish `SessionExited(ProcessVanished)`, delete manifest.
  5. **Readiness gating:** the sweep runs to completion before Kestrel starts listening. `/health` and `GET /sessions` are unreachable until adoption is done, so the server's `SessionReconciliationService` can never observe a half-empty session list and mass-fail agents. (Its "runner doesn't know this session → Failed" logic becomes *correct* again: post-adoption, an unknown session really is gone.)
- **Liveness sweep** extends to probe host pid and child pid (via `Status`); a vanished *host* is a vanished session.
- **Sequence continuity:** seq is host-assigned, so `LastSequence` survives runner restarts and the server's existing `(SessionId, Sequence)` dedup keeps working across an adopt with no gap and no replayed duplicates.
- **Exit-code plumbing, audit cleanup** extend to host logs (`logs/pty-hosts/<sessionId>.log`) and stale shadow-copy dirs.

## Contract & server changes (small by design)

- `RunnerSessionDto` + `HostPid` (nullable), + `Adopted` (bool) — additive.
- New SSE event `SessionAdopted` (server treats it as "still running, refresh buffer via existing resync path"; no new state machine states).
- **Verify** (not change): `SessionReconciliationService` must treat *runner unreachable* as "skip this cycle", never as "sessions gone". Readiness gating makes the restart window look like unreachable, so this distinction is what protects agents during a runner bounce. Add a test pinning it.
- No client/UI changes required; optional later: show host pid / "survived N runner restarts" on the agent card.

## Script & ops changes

- `restart-session-runner.ps1`: replace blanket `taskkill /T` on the wrapper tree with: kill `SessionRunner.exe` (and its cmd/dotnet wrappers) **without** descending into pty-hosts (double-spawn already breaks the chain — but stop killing by name patterns that could match hosts). Add `-KillSessions` for the old scorched-earth behavior (walk manifests, `Shutdown`/kill each host, then restart runner).
- `run-daemon.ps1`, Scheduled Task, AppHost supervisor: **no changes** — hosts are not their children.
- New gotcha entries in `CLAUDE.md` (runner restarts are now session-safe; `-KillSessions` exists; hosts run from shadow-copied binaries under `logs/pty-hosts/bin/`).

## Testing plan

Layered like the existing suites (contract/golden/canary, commit a8310df; raw-shell-first per Q43):

1. **Host unit/contract** (new `tests/Antiphon.PtyHost.Tests`): frame codec round-trip; Launch→Output→Exited happy path against `cmd.exe`; attach-replay from mid-stream; `Resync` when ring overflowed; linger + `Shutdown`; launch-timeout self-destruct; manifest atomicity.
2. **Survival integration** (the acceptance test for this whole spec, in `tests/Antiphon.SessionRunner.Tests`): start raw-shell session → hard-kill the runner process (`Process.Kill`, not graceful) → restart runtime → assert: session adopted, **no missing sequence numbers**, screen snapshot equals pre-kill state, input still works, resize works.
3. **Exit-while-down:** start → kill runner → let child exit → restart → assert exactly one `SessionExited` with the host-recorded code (not `ProcessVanished`).
4. **Vanished-host:** start → kill runner *and* host → restart → assert `SessionExited(ProcessVanished)` + manifest cleanup.
5. **Reconciler pin:** server-side test that an unreachable runner marks nothing Failed for N cycles.
6. **Script test (headed/local):** real `restart-session-runner.ps1` while a fake-claude session runs; session survives; `-KillSessions` kills it.
7. **E2E (Playwright, per Q41 pattern):** "session survives *runner* restart" — terminal keeps streaming after a runner bounce.

## Work breakdown (reviewable increments, ~1 commit each)

| # | Slice | Contents | Size |
|---|---|---|---|
| 1 | Host skeleton | `Antiphon.PtyHost` project, pipe protocol + codec, Launch/Output/Input/Exited against `cmd.exe`, manifest, linger/ack, unit tests | M |
| 2 | Detachment | double-spawn intermediary, job breakaway, shadow-copy launcher + version cleanup | S–M |
| 3 | Runner integration | `RunnerSession` → pipe client, seq/buffer/screen relocation, ansi.log ownership move, all existing runner tests green | L |
| 4 | Adoption | startup sweep, readiness gating, exit-while-down + vanished-host paths, `SessionAdopted` event, survival integration tests | M–L |
| 5 | Server & contracts | DTO additions, reconciler pin test, event pump handling of `SessionAdopted` | S |
| 6 | Ops & docs | restart script rework + `-KillSessions`, audit cleanup extension, CLAUDE.md/docs updates, E2E test | S–M |

Rough total: a solid multi-day epic. Slices 1–2 are risk-free to land early (nothing uses the host yet). Slice 3 is the cutover.

## Risks & mitigations

- **ConPTY behavior under detach** (host outliving its spawner, no console attached): spike this first in slice 1 — it's the load-bearing assumption. Porta.Pty already runs ConPTY headless inside a service, so confidence is high, but verify explicitly with a survives-parent-death test before building on it.
- **Windows Defender / exe copies:** shadow-copying exes to `logs/` may trip AV heuristics; if so, fall back to a fixed install dir under `%LOCALAPPDATA%\Antiphon\pty-host\<version>\`.
- **Orphaned hosts** (runner never comes back): linger TTL (24 h) + `rc`-style visibility — hosts are enumerable via manifests, and `-KillSessions` reaps them. The planned discovery service (separate spec) can surface them in the UI.
- **Protocol drift:** mitigated by keeping interpretation out of the host, versioned hello, degraded-mode adopt.

## Non-goals

- Multi-session hosts, host pooling, remote hosts (one session = one host, localhost only).
- Surviving *host* crashes or reboots (a dead host is a dead session — same blast radius as today, but per-session instead of all-sessions).
- Adopting externally-started Claude sessions (that's the discovery-service spec, separate).
- Changing server↔runner transport, SignalR, orchestration, or turn detection.

## User decision overrides (2026-07-19)

- **Q4:** shadow-copy dirs must have valid, unique, auto-incrementing versions: `<yyyyMMdd-HHmmss>-<sha8>` (UTC date stamp + 8-hex content hash of the host build output), reuse by hash suffix, cleanup deletes oldest-first. Recorded in the Detachment section above.
- **Q6:** framework-dependent now; self-contained/AOT host is a tracked follow-up task, not part of this epic.
- **All other questions:** defaults accepted (Q1 one-pass cutover, Q2 named pipes, Q3 24 h linger, Q5 degraded-mode adopt, Q7 kill-all endpoint).

## Decisions for review

Reply by Q-ID (defaults applied if you answer "use defaults").

| ID | Question | Recommended default |
|---|---|---|
| Q1 | Migration: one-pass cutover (no legacy in-proc PTY path in the runner, matching your Q45 "no fallback" preference), or feature-flag `SessionRunner:PtyHost:Enabled` with the old path kept one release? | One-pass cutover; `PtyAgentRunner` stays only as the host's internal engine + test harness |
| Q2 | Transport runner↔host: named pipes (recommended: no port sprawl, user-ACL for free) vs localhost TCP per host? | Named pipes |
| Q3 | Host linger TTL after child exit with no runner ack? | 24 h |
| Q4 | Host binary location: shadow-copy under `logs/pty-hosts/bin/<version>/` vs `%LOCALAPPDATA%\Antiphon\pty-host\<version>\`? | `logs/…` first, fall back to LOCALAPPDATA if AV complains |
| Q5 | Degraded-mode adopt on protocol mismatch (streams+kill only) vs refuse-and-flag? | Degraded-mode |
| Q6 | Publish host as framework-dependent (small on disk, needs dotnet) or self-contained/AOT (instant start, no SDK coupling, bigger copy)? | Framework-dependent now; AOT later if startup/memory ever matters |
| Q7 | Should `-KillSessions` also be exposed as a runner HTTP endpoint (`POST /sessions/kill-all`) for the dashboard? | Yes, trivial |
