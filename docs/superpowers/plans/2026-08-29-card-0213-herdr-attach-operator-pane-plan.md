# CARD-0213 — plan: attach a standing Antiphon agent to a Herdr pane it did not launch (2026-08-29)

Plan only; nothing here is built. Three slices (S1 runner inspect + attach + origin-aware kill,
S2 server endpoint + DTO, S3 client button) plus one pre-build probe and six operator decisions.

**Card:** CARD-0213 (`dac55591-f4fa-4949-bdc6-30688158ddc0`, board Antiphon, GitHub #16) —
"Herdr: attach/adopt an existing operator pane instead of launching a duplicate".
**Supersedes:** §3 ("S2") of
[`2026-08-28-card-0224-0213-herdr-pane-targeting-plan.md`](2026-08-28-card-0224-0213-herdr-pane-targeting-plan.md).
That sketch was written before CARD-0224 S1 (`23de792`) and CARD-0211/0225 (`a6e6c01`) landed;
this plan re-verifies every claim against `master` @ `e278584` and keeps the S2 recommendations
where they survived, changing four of them (§0).
**Evidence base:** the card; `docs/herdr-sessions.md` §3–§8; `HerdrPaneChild.cs`,
`HerdrPaneSidecar.cs`, `HerdrLastPane.cs`, `SessionRunnerRuntime.cs` (adoption sweep, pending
retry, `StartHerdrAsync`, `StartTailerFor`, `RestoreTailerFromSidecar`, `KillAsync`),
`GrokTranscriptTailer.ResolveUpdatesPath`, `AgentControlService`, `AgentSessionService`,
`SessionRunnerHttpClient`, `HerdrLaunchContextResolver`, `.antiphon/herdr-schema-card0160.json`,
the CARD-0187 kind-launch probes, and this machine's `~/.grok/sessions` (568 `updates.jsonl`).

## 0. What is different from the 2026-08-28 sketch, and why

| Sketch said | This plan says | Because |
|---|---|---|
| runner attach first, then the server writes the row | **inspect (read-only) → server writes the row `Starting` → runner attach → `Running`** | a runner session with no DB row is the CARD-0056 "alert, never kill" shape; the sketch opened that window on purpose and closed it with a compensating detach. Inspect-first means the row exists before anything is bound, exactly like `/start`, and the native id is known before the row is created (§3.2). |
| `pane.rename(PaneTitle)` + `agent.rename(slug)` on attach | **neither, by default** (D3) | the pane's label and herdr agent name (`pm-dropcopy-grok`) are how the operator addresses it; renaming them is the one visible side-effect the card's "leave the pane where it is" forbids. The metadata token is still reported (invisible, best-effort). |
| Grok without `--session-id` ⇒ refuse | Grok without `--session-id` ⇒ refuse **unless herdr's own `pane.agent_session` names it** (probe P-A1, §2.3) | `HerdrPaneInfo.AgentSession {source, agent, kind, value}` exists on the wire and nobody has looked at what herdr fills it with for a bare `grok` launch. If it carries the session id, the flag is not needed and Claude gets a positive id too. |
| nothing about the readers of sidecars | **two hazards found by reading every sidecar consumer** (§1.3): the allocator would split the operator's tab beside an attached pane, and three arms kill `ChildPid` by pid | both are correct for a pane Antiphon created and wrong for one it did not. They are refusal/guard rows, not follow-ups. |

Everything else in the sketch (detach-not-kill, native id as row id, no new durability, header
button) is kept and restated here with the code that has to change.

## 1. What "adopt" means today — read before designing

### 1.1 The three existing bind paths

| Path | Trigger | Evidence it demands | Sidecar |
|---|---|---|---|
| **Runner-restart adoption** — `SessionRunnerRuntime.AdoptHerdrSessionsAsync:476` → `EvaluateHerdrBarAsync:521` → `RunnerSession.AdoptHerdrAsync:1522` → `HerdrPaneChild.AttachExistingAsync:57` | runner boot, before the HTTP API listens | `pane.get` answers; `pane.process_info` lists the sidecar's `ChildPid`; `pane.read` answers | reads an existing one — **only sidecars Antiphon wrote** |
| **Adopt in place** (CARD-0224 4b) — `HerdrPaneChild.ResolveTargetPaneAsync:137` → `AdoptInPlaceAsync:317` | a launch whose last-pane record points at a live process | `pane.Agent == expectedKind`, exactly one non-shell foreground process, `TryReadNativeSessionId(argv) == request.SessionId` | writes one with `Origin = launched` |
| **Typed launch** — `CompleteTypedLaunchAsync:259` | every other herdr launch | detection of the expected kind after typing the script | writes one with `Origin = launched` |

There is no path that binds a pane whose argv names a session Antiphon has never heard of.
`ResolveTargetPaneAsync:222` already refuses that shape with `pane_occupied` and a message
saying "run attach (CARD-0213)"; `HerdrPaneOrigins.Attached` exists as a constant
(`SessionRunnerContracts.cs:531`) and is never produced. `HerdrPaneSidecar.Retire:82` and
`ResolveTargetPaneAsync:183` already treat `attached` as "never type into this pane again".

### 1.2 The sidecar — the only binding record

`<SessionLogPath>/herdr/<sessionId:N>.json` (`HerdrPaneSidecar`): `SessionId`, `WorkspaceKey`,
`WorkspaceId`, `TabId`, `PaneId`, `ChildPid`, `ShellPid`, `LaunchedAtUtc` (also the C3 epoch and
the `IsAlive(pid, start)` guard), `Cwd`, `AgentKind`, `Origin`, `UpdatedAtUtc`. Transcript
binding is a *separate* sidecar, `<SessionLogPath>/transcripts/<sessionId:N>.json`
(`TranscriptSidecar`: `Cwd`, `ChildStartUtc`, `ResumeLaunch`, `TranscriptPath`, `Format`,
`How`). `RestoreTailerFromSidecar:1573` prefers `TranscriptPath` when set and only derives the
Grok path from `_sessionId` when it is not — that is the seam Q2 uses (§3.4).

The DB holds **no** pane id (CARD-0160, kept). `AgentSession.SessionBackend` is the snapshot the
delivery ceilings key on. Nothing in this plan adds a column.

### 1.3 Every reader of a sidecar, and what an `attached` origin does to it

| Reader | Today | With an attached sidecar | Change |
|---|---|---|---|
| `AdoptHerdrSessionsAsync` / `EvaluateHerdrBarAsync` | re-adopt on the 3-step bar | same bar, same verdicts | none (origin survives the round-trip; `RunnerSessionDto` gains it, §3.3) |
| `TryKillOrphanedChild:607` (R3/R5 arms) | kills `ChildPid` by pid when OS-alive but not listed in the pane | **would kill the operator's process** | skip when `Origin == attached`; log `HerdrOrphanNotOurs`, drop the sidecar |
| `RunnerSession.KillPendingHerdr:1840` | kills `ChildPid` by pid, `Exited(PaneLeftOpen)` | same hazard | skip the pid kill for attached; `Exited(Detached)` |
| `HerdrPaneChild.KillAsync:417` foreign-process arm | kills `ChildPid` by pid, leaves pane open | same hazard | attached ⇒ **detach** (§3.4), never `pane.close`, never pid kill |
| `SessionRunnerRuntime.CollectLiveAntiphonPanes:191` (allocator census) | every live sidecar in the workspace is an "Antiphon pane" | **an attached pane in an operator tab counts as 1 live pane ⇒ the next launch in that workspace `pane.split`s the operator's tab** (`HerdrPaneAllocator`: 1 ⇒ split right) | exclude `Origin == attached` — an attached pane is never a slot |
| `LiveHerdrPanes:79` (event pump + status push) | subscribes / pushes state labels for every live pane | wanted — screen/status mirroring is the point | none |
| `HerdrPaneSidecar.Retire` | last-pane record on exit | already skips attached | none |
| `ResolveTargetPaneAsync` 4a | relaunch into an empty pane we owned | already refuses attached | none |

The two hazards in bold are the reason this card cannot be "write a sidecar with a different
`Origin` and let the machinery run".

### 1.4 The kill-safety check attach must mirror

`HerdrPaneChild.KillAsync:424-431`: `unexpected = foreground − {ChildPid, ShellPid}`; any
survivor ⇒ refuse `pane.close`. Transposed to attach time (before there is a `ChildPid`): the
pane's foreground set minus the shell pid must be **exactly one** process, and its `name` must
belong to the expected kind's executable family. Two processes, zero processes, or a `node.exe`
where `grok.exe` was expected are all the shapes `KillAsync` would later refuse to close — so
they are refused at the door instead (§4).

## 2. Claims verified — and two that the card got slightly wrong

### 2.1 "The Grok transcript path is deterministic from cwd + `--session-id`" — true, with two catches

`GrokTranscriptTailer.ResolveUpdatesPath:86` =
`{GROK_HOME}/sessions/{Uri.EscapeDataString(Path.GetFullPath(cwd))}/{id:D}/updates.jsonl`,
`GROK_HOME` from the launch env, else the runner's environment, else `%USERPROFILE%\.grok`. The
directory exists before the first prompt (CARD-0187 K1, measured). Verified live on this machine:
568 `updates.jsonl` under `~/.grok/sessions`, every session directory a GUID.

1. **The id in the path is the process's own `--session-id`, not Antiphon's session id.** Today
   they coincide because `AgentSessionService.BuildSessionIdentityArgs:1199` passes the row id.
   For an attached pane they coincide only if the row id *is* the native id (Q2, §3.4) — which
   is the recommendation, and why `TranscriptSidecar.TranscriptPath` is written explicitly at
   attach rather than derived.
2. **`GROK_HOME` and cwd casing are the operator's, not the runner's.** The card's own example
   panes (`D:\src\maven.dropcopy`, `D:\src\mav-ref`) have **no** session directory under this
   machine's `~/.grok` — those groks run under another home or another machine. Deriving the
   path and hoping is exactly the CARD-0006 class. Attach therefore **locates by GUID**:
   `Directory.EnumerateDirectories("{GROK_HOME}/sessions", "*")` for one that contains
   `{nativeId:D}/` — the GUID is globally unique, so a hit is positive evidence regardless of
   how grok encoded the cwd; no hit ⇒ refuse `herdr_transcript_not_found` naming the root that
   was searched (D5). `HerdrPaneProcess.Cwd` (the *process's* cwd from `pane.process_info`)
   is still recorded on both sidecars, and a found directory whose decoded cwd differs from it
   is a Warning, not a refusal.

### 2.2 "Same kill-safety as `KillAsync`" — true, see §1.4; **plus the three pid-kill arms** in §1.3 that the card did not list and that would kill an operator's process if attach reused them unchanged.

### 2.3 Herdr's `agent_session` field — unmeasured, and it changes D5 if it is populated

`HerdrPaneInfo.AgentSession` / `HerdrAgentInfo.AgentSession` (`HerdrApiModels.cs:47,68`) carry
`{source, agent, kind, value}`, and the schema has both a client-report method
(`pane.report_agent_session`, which CARD-0163 S2 already calls for our own sessions) and the
field on every `pane.get`. Whether herdr fills it **itself** for a bare `grok` / `claude` launch
— from the OSC title, the process argv, or a transcript watcher — is not in any probe result in
the repo (`card-0160-probe-results.md`, `card-0162-investigation.md`, the CARD-0187 kind probes
all skip it).

**Pre-build probe P-A1 (30 minutes, where herdr runs — not on this machine, §6.3):** launch a
bare `grok` and a bare `claude` in operator panes, `herdr pane get <id> --json`, record
`agent_session` for each, then the same after one prompt. Three outcomes, all designed for:

| P-A1 result | Grok without `--session-id` | Claude without `--session-id` |
|---|---|---|
| `agent_session.kind == "session_id"` (or a path containing the GUID) with `source != antiphon` | **attach with that id** — same positive-evidence weight as argv | **attach with that id**; transcript = `<enc-cwd>/<id>.jsonl`, `how = exact` (CARD-0181 Exact strength) |
| populated only from our own `pane.report_agent_session` (source `antiphon`) or null | refuse `herdr_native_id_unknown` (D5) | `FallbackSessionId` + CARD-0006 discovery, binds at the first prompt **Antiphon** sends (C4); documented |
| present but not a GUID / not verifiable on disk | treat as absent | treat as absent |

Argv stays the first source (it is what CARD-0224 already trusts); `agent_session` is the second;
nothing else is. The probe result goes in `.antiphon/card-0213-probe-results.md` and this table's
winning row is what S1 implements.

## 3. Design

### 3.1 Runner: two endpoints and an origin-aware kill

**`GET /herdr/panes/{paneId}`** → `HerdrPaneInspectDto` (read-only; nothing written, nothing
typed, nothing renamed). Also the primitive a later pane-picker needs.

```
HerdrPaneInspectDto(
  PaneId, WorkspaceId, TabId, Label, Title,
  Agent,                 // pane.get.agent verbatim (claude/grok/codex/null)
  AgentStatus,
  ShellPid, ShellName,
  Foreground[] { Pid, Name, Argv, Cwd, StartTimeUtc? },   // non-shell only
  NativeSessionId?,      // argv (TryReadNativeSessionId) else agent_session (P-A1 row) else null
  NativeSessionSource?,  // "argv" | "agent_session" | null
  BoundToSessionId?,     // a live sidecar / session already claims this pane
  BoundOrigin?)
```

**`POST /sessions/attach`** `HerdrAttachRequest(SessionId, PaneId, ExpectedKind,
TranscriptFormat, ExpectedChildPid, ExpectedNativeSessionId?, WorkspaceKey, PaneTitle?,
AgentSlug?)` → `201 RunnerSessionDto` or problem-details (§4). `SessionId` is the id the
**server** chose from the inspect result (the native id when there was one — §3.4). The runner
**re-runs every inspect check** and additionally refuses `herdr_pane_changed` if the foreground
pid or native id no longer equals what the server was told — the TOCTOU between inspect and
attach is a refusal, never a "close enough".

`SessionRunnerRuntime.AttachHerdrAsync` (new, beside `StartAsync:100`):

1. `_herdrClient.ConnectAndValidateAsync`; `pane.get` (unknown ⇒ `herdr_pane_not_found`).
2. `pane.Agent` must equal `ExpectedKind` (null ⇒ `herdr_pane_unoccupied`; other ⇒
   `herdr_kind_mismatch` naming both).
3. `pane.process_info`: exactly one non-shell foreground process, `Name` in the kind's family
   (`grok`/`grok.exe`; `claude`/`claude.exe`/`node`/`node.exe`; `codex`/`codex.exe`/`cmd.exe`
   for the `.cmd` launcher — a pinned list beside `HerdrAgentKinds`, with the K6 measurement
   that a `pwsh` wrapper is *not* listed) ⇒ else `herdr_pane_foreign` naming every pid.
   `Pid == ExpectedChildPid` ⇒ else `herdr_pane_changed`.
4. Not already bound: no live `RunnerSession` with `HerdrPaneId == PaneId`, no sidecar under
   `herdr/` with that `PaneId`, and no `last-pane/` record for **another** session id pointing
   at it (that record means an Antiphon session expects to relaunch there) ⇒ else
   `herdr_pane_bound` naming the session id and origin.
5. Native id: argv, else `agent_session` per P-A1; must equal `ExpectedNativeSessionId` when the
   server sent one (`herdr_pane_changed` otherwise).
6. Transcript evidence (Grok only, §2.1): locate `{GROK_HOME}/sessions/*/{id:D}/` ⇒ else
   `herdr_transcript_not_found`.
7. `pane.report_metadata(antiphon-session = SessionId)` — best-effort, TTL as today; **no**
   `pane.rename`, **no** `agent.rename` unless the request carries `PaneTitle`/`AgentSlug`
   (the server sends them only when D3 is decided the other way).
8. Write `HerdrPaneSidecar { Origin = attached, ChildPid, ShellPid, LaunchedAtUtc = process
   start (`IProcessLivenessProbe.TryGetStartTimeUtc`, else now), Cwd = process cwd, AgentKind }`.
9. `RunnerSession.AttachHerdrAsync` (new; `AdoptHerdrAsync:1522` with `_adopted = false` and a
   `SessionStarted` publish instead of `SessionAdopted`): register `Running`, wire the `Exited`
   handler exactly as `StartHerdrAsync:983`, `NotifyPaneSetChanged()` so the pump subscribes
   and the status push starts.
10. Transcript: Grok ⇒ `TranscriptSidecar { Format = grok, TranscriptPath = <located>/updates.jsonl,
    How = deterministic, ResumeLaunch = true, ChildStartUtc = process start }` +
    `GrokTranscriptTailer(_sessionId, path, …)` — reads from offset 0, so the operator's
    existing conversation lands in `TranscriptEntries` and `IsWorkingAsync` has history. Claude
    ⇒ exact file when an id is known (`TranscriptTailer` with `knownTranscriptPath`, claim
    strength Exact), else discovery with `ResumeLaunch = true` (C3 waived: the conversation
    predates us). Codex ⇒ discovery, `resumeLaunch: true`, as `StartTailerFor:1068`.

**Origin-aware kill.** `HerdrPaneChild.KillAsync` on `_sidecar.Origin == attached` performs
**detach**: `pane.report_metadata(clear_state_labels: true, antiphon-session: null)` best-effort,
`HerdrPaneSidecar.TryDelete` (no last-pane — `Retire` already skips attached), tailer stopped,
`RaiseExited(HerdrExitReasons.Detached)` with `ExitCode: 0`. No `pane.close`, no pid kill. The
same guard goes into `KillPendingHerdr` and `TryKillOrphanedChild` (§1.3). A normally launched
pane's kill is byte-for-byte unchanged.

`HerdrExitReasons.Detached = "HerdrDetached"` (contracts); `RunnerSessionDto.HerdrOrigin`
(additive, after `HerdrVerifiedAtUtc`; `launched` | `attached` | null); `RunnerCapabilitiesDto`
gains `"herdr-attach"` so an old runner in front of a new server refuses instead of 404-ing.

**Runner endpoint error mapping.** `POST /sessions` today catches only
`UnsupportedTranscriptFormatException` (`Program.cs:180`); a `HerdrLaunchException` is a 500 and
the server sees an `HttpRequestException` with no code. The attach endpoints return RFC 9457
problem details with `type` = the code (the `HerdrProblemTypes` pattern, `Program.cs:100-115`),
status 404 / 409 / 503 as §4 lists — **and `POST /sessions` gets the same mapping for
`HerdrLaunchException` in passing**, so `pane_occupied` reaches the server as a 409 with its code
rather than a bare 500 (today the always-on supervisor sees an opaque transport failure).

### 3.2 Server: `POST /api/agents/{id}/attach-herdr`

`{ "paneId": "w2:p3" }` → `200 AgentDetailDto` (the `/start` shape). `AgentEndpoints.cs` after
`/stop:131`; `AgentControlService.AttachHerdrAsync` beside `StartAsync:89`/`StopAsync:363`:

1. `LockAgentAsync` (404).
2. `agent.SessionBackend == Herdr` and `ValidateSessionBackendPairing(Herdr, agent.Kind)` ⇒
   `409 herdr_refused` (the existing gate; OpenCode/Raw fall out here).
3. `!HasLiveSessionAsync` ⇒ `409 session_active`.
4. `ISessionRunnerClient.GetSessionBackendCapabilityMismatchAsync` and the new `herdr-attach`
   capability ⇒ `409 herdr_refused` with the runner's reason.
5. `ISessionRunnerClient.InspectHerdrPaneAsync(paneId)` — passthrough of the runner's refusals
   (§4). Server-side checks on the result: `Agent` maps to `agent.Kind` via `HerdrAgentKindMap`
   (`herdr_kind_mismatch`), `BoundToSessionId is null` (`herdr_pane_bound`).
6. Session id = `NativeSessionId ?? Guid.NewGuid()`. If a row with that id exists: it must be
   this agent's own (`agent.PersistentSessionId == id`) non-card row in `Stopped`/`Failed` with
   the same cwd — the `FindResumableSessionAsync:340` rule — and it is restamped exactly as the
   resume arm restamps (`StartInteractiveSessionAsync:218-235`); anything else ⇒
   `409 session_id_taken` naming the owner. This is the "operator restarted my grok by hand and
   I want it back" path, and it is what makes attach idempotent across a herdr restart.
7. Write the row **before** the runner binds anything: `Status = Starting`, `SessionBackend =
   Herdr`, `AgentKind = agent.Kind`, `DefinitionName` from `_launchComposer.PeekProfileKindAsync`
   / `ResolveForAgentAsync` (the profile is still what names the kind), `Cwd = inspect
   Foreground[0].Cwd` (fallback pane cwd), `StartedAt = process start`, `Cols/Rows` 120×30,
   `TuiProfileRevisionId`/`EffectiveModelId`/`ComposedBundleStamp = null` (the process's model
   and bundle are the operator's — `IsOutOfDate` reads null as "unknown", not drift; verify in
   `AgentService:116` and add the one-line guard if it does not). `SaveChanges`.
8. `ISessionRunnerClient.AttachHerdrAsync(HerdrAttachRequest …)`. On any exception: row ⇒
   `Failed` with the code in `FailureReason`, rethrow. No compensating detach is needed — the
   runner writes nothing before its own checks pass, and a runner that attached but whose
   response was lost is re-found by `SessionReconciliationService`'s third pass as
   Failed-row/runner-Running ⇒ **re-adopt** (the existing CARD-0056 arm), not a leak.
9. Row ⇒ `Running`, `agent.PersistentSessionId = id`, `agent.Status = Running`,
   `ClearSupervisionLatchAsync`, `AgentChanged` + `SessionStarted` events. **No**
   `SendRemoteControlCommandsAsync` (CARD-0212's gate is moot — nothing is typed), **no** launch
   note / bootstrap prompt (`LaunchNotes` null — a "New session started" ritual into an
   operator's live conversation is the intrusion this card exists to avoid), **no** queue flush.

`SessionRunnerHttpClient` grows `InspectHerdrPaneAsync` / `AttachHerdrAsync` and a problem-details
reader that maps the runner's `type` onto `NotFoundException` (404), `ConflictException(message,
code)` (409 — the two-arg ctor at `ConflictException.cs:12`) and the existing
`ServiceUnavailableException` (503, `HerdrProblemTypes.Unreachable`). `DirectSessionRunnerClient`
(tests) and `RefusingSessionRunnerClient` implement the two new members (the refusing one throws).

`AgentSessionService.KillAsync:755`: `HerdrDetached` lands `Stopped` already (`killed` is true);
`AgentExitReason.HerdrDetached = 9` is added so the unsolicited path (`CloseSessionOnExitAsync`,
`cleanStop = exitCode == 0 …` at `AgentSessionRuntime.cs:163`) reads a detach as a clean stop —
the exit code is 0, so it does today, but the enum value makes the reason legible in the
timeline instead of `Unknown`.

`AgentSessionSummaryDto` (`BoardDtos.cs:95`) gains `HerdrOrigin` beside `HerdrAgentStatus`;
`AgentService.ResolveLiveSession` copies it from the runner DTO.

### 3.3 What the DB needs: nothing new

`AgentSession` already has every field the row needs (`Id`, `SessionBackend`, `AgentKind`,
`Cwd`, `DefinitionName`, `Status`, timestamps). The origin lives on the runner sidecar and is
mirrored to the DTO on read. No migration. This respects CARD-0160's "pane ids are not DB
columns" — the server still never learns a pane id except as an opaque string it forwarded.

### 3.4 The card's four open questions — one recommendation each

**Q1 — who owns stop? Detach, never kill.** `StopAsync:363` is unchanged: it calls
`_agentSessionService.KillAsync`, which calls the runner's `/kill`, which on an attached sidecar
detaches (§3.1). The TUI keeps running in the operator's pane; the DB row lands `Stopped` with
`ExitReason = HerdrDetached`; an always-on agent's supervision is suspended exactly as any
manual stop suspends it. Stop and detach are the same verb because an agent with no session is
Stopped by definition — a separate `/detach` that leaves the agent `Running` would be an agent
with no session that claims to be running. **Cost:** the operator cannot use Antiphon to kill an
attached grok; they close the pane. That is the correct asymmetry: Antiphon never kills what it
did not start (CARD-0056's "unclaimed never implies kill", extended to "attached never implies
kill"). The Stop button reads **Detach** when `herdrOrigin === 'attached'`.

**Q2 — native session id: the pane's id becomes `AgentSession.Id`.** Read from argv (or
`agent_session`, P-A1), never typed, never guessed from the newest directory. Making it the row
id is what makes the rest free: the Grok transcript path (§2.1), Claude's exact-id claim
(CARD-0181 Exact), a later supervisor resume — `FindResumableSessionAsync` finds the row,
`BuildSessionIdentityArgs` types `--resume <id>` (Grok honours it, measured 1.0.5), so after
the operator's pane dies the same conversation continues in an Antiphon-allocated pane — and
the proxy's named session, which `gkp` keys on the same `--session-id`. Nothing is sent to the
process to "confirm" identity; the transcript directory existing on disk (§2.1) is the
confirmation. When there is no id (Claude/Codex bare launches on the P-A1 "absent" row) the row
id is fresh and the transcript binds by CARD-0006 discovery at the first prompt Antiphon sends —
a later `--resume <row id>` will not find that conversation and falls back fresh; stated in
`docs/herdr-sessions.md`, not hidden.

**Q3 — identity durability: no new durability; the sidecar is the record.** `pane.report_metadata`
stays best-effort (24 h TTL, restart survival unverified — nothing in this card measures it,
and nothing depends on it). A **runner** restart re-adopts an attached sidecar through the
unchanged §6 bar with its origin intact. A **herdr** restart kills the process (P7) and the
session is `Exited(HerdrRestartPresumedDead)` like any other; an attached exit writes **no**
last-pane record and `ResolveTargetPaneAsync` 4a refuses an attached candidate, so Antiphon
never types a launch script into a pane it did not create — the always-on resume goes through
the allocator into Antiphon's own tab, carrying `--resume <native id>`, and the operator can
re-attach by hand if they rebuild their pane (step 6 of §3.2 makes that a restamp, not a new
row). The one adopt-only exception stays: `AdoptInPlace` may bind a live same-id process in an
attached pane because it types nothing. The card's alternative — "a durable binding that
survives herdr restart" — is out of reach on the herdr contract as measured (P7: the child dies
with herdr) and would be solving the out-of-scope "adopt after herdr restart with no sidecar"
problem.

**Q4 — UI: API first; one "Attach to Herdr pane…" button in the Agents page header.** Start/Stop
live in the header `Group` (`AgentsPage.tsx:196-243`) keyed on `liveSession`; attach is a
one-shot action with a live-session outcome, so it belongs beside Start, not in the settings
modal (which PATCHes persisted fields). v1: when `selected.data.sessionBackend === 'Herdr'` and
there is no live session, a third button **Attach…** (`TbPlugConnected`) opens a `Modal` with
one `TextInput` (`paneId`, placeholder `w2:p3`, helper text "`herdr pane list` shows ids") and
calls `useAttachHerdrPane(agentId)` (`client/src/api/agents.ts`, the `useStartAgent:496` shape,
`apiPost` to `/agents/${id}/attach-herdr`, same invalidations). Errors surface through
`getApiErrorMessage` — the §4 messages are written to read well there. Stop reads **Detach**
on `liveSession.herdrOrigin === 'attached'`; `HerdrStatusBadge` gets an `attached` chip. A pane
picker needs `GET /herdr/panes` on the runner plus a server proxy — its own card (§8).

## 4. Refusals — precise enough to test

All server responses are RFC 9457 problem details with `code`; runner responses carry the same
string as `type`. Each row names the test that pins it (§6).

| # | Case | Where refused | Status / code | Mirrors | Test |
|---|---|---|---|---|---|
| R1 | agent not on `Herdr`, or kind `OpenCode`/`Raw` | server step 2 | `409 herdr_refused` | `ValidateSessionBackendPairing` | `AgentAttachHerdrTests.Refuses_non_herdr_agent_and_unmapped_kinds` |
| R2 | agent already has a live session | server step 3 | `409 session_active` | `StartAsync` | `…Refuses_when_a_live_session_exists` |
| R3 | runner does not advertise `herdr` / `herdr-attach` | server step 4 | `409 herdr_refused` | `GetSessionBackendCapabilityMismatchAsync` | `…Refuses_when_runner_lacks_attach_capability` |
| R4 | pane unknown to herdr | runner step 1 | `404 herdr_pane_not_found` | `IsPaneNotFound` | `HerdrAttachTests.Unknown_pane_is_404` |
| R5 | pane has no detected agent (`pane.get.agent == null`) | runner step 2 | `409 herdr_pane_unoccupied` | `WaitForExpectedAgentAsync` timeout arm | `…Unoccupied_pane_is_refused` |
| R6 | pane's detected agent ≠ agent's kind (grok pane, ClaudeCode agent) | runner step 2 + server step 5 | `409 herdr_kind_mismatch` (message names both) | `WaitForExpectedAgentAsync` "detected X where Y expected" | `…Kind_mismatch_is_refused_naming_both` |
| R7 | foreground has 0, ≥ 2, or a non-family process besides the shell | runner step 3 | `409 herdr_pane_foreign` (message lists pids and names) | `KillAsync:424-431` unexpected-process arm | `…Two_foreground_processes_are_refused`, `…Wrong_executable_family_is_refused` |
| R8 | pane already claimed by a live session or sidecar, or another id's last-pane | runner step 4 | `409 herdr_pane_bound` (names session + origin) | `ResolveTargetPaneAsync` 4c | `…Bound_pane_is_refused_naming_the_holder`, `…Last_pane_of_another_session_is_refused` |
| R9 | Grok with no native id (argv and `agent_session` both silent) | runner step 5 | `409 herdr_native_id_unknown` (message says relaunch with `--session-id`) | `TryReadNativeSessionId` | `…Grok_without_session_id_is_refused` |
| R10 | Grok id known but no `sessions/*/<id>/` directory under `GROK_HOME` | runner step 6 | `409 herdr_transcript_not_found` (names the root searched) | CARD-0006 "no positive evidence ⇒ no bind" | `…Grok_with_no_session_directory_is_refused` |
| R11 | pid or native id changed between inspect and attach | runner steps 3/5 | `409 herdr_pane_changed` | — (TOCTOU) | `…Pane_that_changed_since_inspect_is_refused` |
| R12 | native id already another agent's session, or any card session | server step 6 | `409 session_id_taken` (names owner) | `FindResumableSessionAsync` | `AgentAttachHerdrTests.Native_id_owned_elsewhere_is_refused` |
| R13 | herdr unreachable | runner, any step | `503 herdr_unreachable` | `HerdrProblemTypes.Unreachable` | `HerdrAttachTests.Unreachable_herdr_is_503_and_writes_nothing` |

Guards that are not refusals but must hold (each a test): an attached pane is never counted by
the allocator (`HerdrLaunchShapeTests.Attached_pane_is_not_an_allocator_slot`); no pid kill in
`TryKillOrphanedChild`, `KillPendingHerdr`, or `KillAsync` for an attached sidecar
(`HerdrAdoptionSweepTests.R20_attached_orphan_is_dropped_not_killed`,
`HerdrPaneChildKillTests.Attached_kill_detaches_without_pane_close_or_pid_kill`); an attached
exit writes no last-pane and the next launch of that id allocates
(`HerdrAdoptionSweepTests.R21_attached_exit_leaves_no_last_pane`).

## 5. Files and methods that change

**Contracts (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`)**
`HerdrExitReasons.Detached`; `RunnerSessionDto.HerdrOrigin`; `HerdrAttachRequest`,
`HerdrPaneInspectDto`, `HerdrForegroundProcessDto`; `HerdrProblemTypes` + the §4 codes;
`RunnerCapabilitiesDto` advertises `herdr-attach`. `HerdrAgentKinds.ExecutableFamily(kind)`.

**Runner (`src/Antiphon.SessionRunner/`)**
`HerdrPaneChild`: `InspectAsync(paneId)`, `AttachAsync(HerdrAttachRequest)` (shares the
process-info / native-id / family checks with `ResolveTargetPaneAsync` — extract
`InspectForegroundAsync`), origin-aware `KillAsync` (detach arm), `Sidecar.Origin` exposed.
`SessionRunnerRuntime`: `InspectHerdrPaneAsync`, `AttachHerdrAsync`,
`CollectLiveAntiphonPanes` excludes attached, `TryKillOrphanedChild` skips attached;
`RunnerSession.AttachHerdrAsync`, `KillPendingHerdr` attached arm, `ToDto` origin,
`StartAttachedTailer` (Grok explicit path / Claude exact-or-discovery / Codex discovery).
`GrokTranscriptTailer.TryLocateSessionDirectory(grokHome, nativeId)`. `Program.cs`: the two
routes, problem-details mapping for `HerdrLaunchException` on `/sessions` and the attach
routes, `/capabilities` advertises `herdr-attach`.

**Server (`server/`)**
`Application/Interfaces/ISessionRunnerClient.cs` (+2 members, default-throwing
`NotSupported` so fakes compile); `Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`
(+2 methods, `ThrowForRunnerProblemAsync`); `Application/Services/AgentControlService.cs`
`AttachHerdrAsync`; `Application/Interfaces/AgentExitReason.cs` `HerdrDetached = 9`;
`Application/Dtos/BoardDtos.cs` `AgentSessionSummaryDto.HerdrOrigin`;
`Application/Dtos/SessionRunnerDtos.cs` mirror; `Application/Services/AgentService.cs`
`ResolveLiveSession` copy + the `IsOutOfDate` null guard; `Api/Endpoints/AgentEndpoints.cs`
route; `Api/Contracts` `AttachHerdrPaneRequest(paneId)`.

**Client (`client/src/`)** `api/agents.ts` (`useAttachHerdrPane`, `herdrOrigin` on the session
summary type); `features/agents/AgentsPage.tsx` (button + modal, Detach label);
`features/agents/HerdrStatusBadge.tsx` (attached chip).

**Docs** `docs/herdr-sessions.md`: §3 (origin `attached` now produced), new §"Attaching an
operator pane" (what is and is not touched: no rename, no launch note, detach-on-stop, the
Grok id requirement, the P-A1 result), §8 rows for every §4 code; `docs/antiphon-api.md` agents
block (+ `attach-herdr`, codes, a `curl` example); `docs/agent-kinds.md:358` "(ClaudeCode only)"
fixed in passing; one `AGENTS.md` gotcha bullet: *"An attached herdr pane is never counted,
never closed and never pid-killed — Stop detaches"*.

## 6. Verification

### 6.1 Runner (`tests/Antiphon.SessionRunner.Tests`)

`FakeHerdrServer` additions: `SetPaneAgentSession(paneId, source, kind, value)`; the argv overload
of `SetPaneProcessInfo` (already there, `:526`) is enough for the rest. New class
`HerdrAttachTests` (`[NotInParallel("SessionLiveness")]`, temp `GROK_HOME` seeded with
`sessions/<enc-cwd>/<id>/updates.jsonl`):

- `Attach_binds_a_live_grok_by_argv_and_writes_an_attached_sidecar` — sidecar `Origin ==
  attached`, `ChildPid` = the fake pid, `RunnerSessionDto.Status == Running`, `Adopted == false`,
  `HerdrOrigin == attached`, `SessionStarted` published, transcript sidecar `Format == grok`
  with the seeded path, **zero** `pane.send_text` / `pane.rename` / `agent.rename` /
  `tab.create` / `pane.split` in `fake.Requests`, one `pane.report_metadata`.
- `Attach_locates_the_grok_directory_by_guid_when_cwd_encoding_differs` — seed the directory
  under a differently-cased cwd; still binds; a Warning is logged.
- Every R4–R11, R13 row in §4 by code, each asserting **no sidecar written and no request
  beyond the reads**.
- `Attached_kill_detaches` — `/kill` ⇒ `Exited(0, HerdrDetached)`, sidecar gone, no last-pane
  record, no `pane.close`, no OS kill (a `StubProbe` that records kill attempts), one
  `pane.report_metadata` with `clear_state_labels`.
- `Runner_restart_readopts_an_attached_sidecar_with_origin_intact` — the R1 sequence from
  `HerdrAdoptionSweepTests` on an attached sidecar; DTO origin `attached` after adoption.
- `Attached_orphan_is_dropped_not_killed` — pane gone, pid OS-alive ⇒ `RestartPresumedDead`,
  sidecar retired, **no** kill recorded.
- `Attached_pane_is_not_an_allocator_slot` (`HerdrLaunchShapeTests`) — one attached sidecar in
  an operator tab, then a normal launch in the same workspace ⇒ `tab.create`, never
  `pane.split` on the operator's tab.
- `Pane_occupied_on_sessions_post_is_a_409_with_its_code` (`Program.cs` mapping, in passing).
- `HerdrClientSurfaceTests`: `GET /herdr/panes/{id}` shape.

### 6.2 Server + client (`tests/Antiphon.Tests`, vitest)

`AgentAttachHerdrTests` (new, `[NotInParallel]`, `DirectSessionRunnerClient` + `FakeHerdrServer`
— the `HerdrAlwaysOnChannelParityTests` harness): success (row `Id == native id`, `Running`,
`PersistentSessionId`, supervision latch cleared, DTO `herdrOrigin == "attached"`, **no**
`/remote-control`, **no** launch note in the queue); restamp of the agent's own Stopped row with
the same id; R1–R3, R12; the runner refusals reaching the API with their codes and the row left
`Failed` with the code in `FailureReason`; **Stop on an attached agent lands `Stopped` with the
fake pane still holding its process**; an always-on attached agent whose process dies is resumed
by the supervisor into an **allocated** pane with `--resume <native id>` in argv (the Q3
contract). `SessionRunnerHttpClientHerdrWireTests`: `HerdrOrigin` on the wire, absent ⇒ null;
problem-details ⇒ typed exceptions. Client `AgentAttachHerdr.test.tsx` (msw, the
`AgentSessionBackend.test.tsx` pattern): button visible only for Herdr + no live session; Detach
label on `herdrOrigin === 'attached'`; the 409 message rendered.

### 6.3 Live smoke — **not runnable on this machine**

Neither `herdr` nor its named pipe exists here today (`which herdr` and `//./pipe/` both empty,
2026-08-29), and the card's panes (`D:\src\maven.dropcopy`, `D:\src\mav-ref`) have no session
directories under this machine's `~/.grok`. The smoke runs wherever herdr and those groks live:

1. P-A1 first (§2.3) — its result decides D5's arm before S1 is written.
2. `herdr pane get w2:p3 --json` → confirm `agent: grok` and `process_info` argv shows
   `--session-id`; `ls $GROK_HOME/sessions/*/<id>/`.
3. `POST /api/agents/29829120-ae08-41e8-9d75-22dafe391ef8/attach-herdr {"paneId":"w2:p3"}` →
   paste the 200 body (`liveSession.herdrOrigin: attached`, `id == <native id>`).
4. One `Mode: WhenIdle` queue delivery → confirmed by a `UserPrompt` transcript row (CARD-0055
   verdict `Sent`), and the operator sees it typed into their pane.
5. `herdr pane list` — the pane is where it was, same label, same agent name; no new tab.
6. Stop → `Stopped`, `exitReason HerdrDetached`; `herdr pane get w2:p3` shows the grok still
   alive; the runner sidecar is gone.
7. Start the agent normally afterwards (always-on or manual) → it resumes `<native id>` in an
   Antiphon tab — the same conversation, continued.

Paste 2–7 into the card as close-out evidence. The two duplicate always-on agents the card
names (`PM-DropCopy-Grok` / `PM-MavRef-Grok`) are stopped before step 3 (R2) and can be deleted
after step 7 if the operator prefers the attached ones.

### 6.4 Run commands

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c0213/ -- --treenode-filter "/*/Antiphon.SessionRunner.Tests/Herdr*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0213/ -- --treenode-filter "/*/Antiphon.Tests.Application/AgentAttachHerdrTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0213/ -- --treenode-filter "/*/Antiphon.Tests.Application/Herdr*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0213/ -- --treenode-filter "/*/Antiphon.Tests.Agents/SessionRunnerHttpClientHerdrWireTests/*"
pwsh -File scripts/test-client.ps1
Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-c0213 | Remove-Item -Recurse -Force
```

Never co-schedule the runner suite and `Antiphon.Tests`. Build order: S1 (runner, contracts)
→ S2 (server) → S3 (client); S1 is independently green against `FakeHerdrServer` and lands
first.

## 7. Decisions that are the operator's — each with a recommendation

- **D1 — Stop on an attached agent = detach (recommended), or kill with an explicit `/detach`?**
  Detach needs no column and no new endpoint and never kills a process Antiphon did not start.
  If you want "Antiphon owns whether it lives" for some panes, that is a per-attach flag
  (`ownsProcess: true`) on the request, one line in the kill arm, and a row in §4 — say so and
  it is added; the default stays detach.
- **D2 — Session row id = the pane's native session id (recommended), or always a fresh id with
  the native id stored beside it?** Fresh-id would need a new column (`NativeSessionId`) and a
  second code path in `BuildSessionIdentityArgs`, `FindResumableSessionAsync` and the Grok
  tailer for every later resume. Using the native id costs one uniqueness check (R12).
- **D3 — Leave the operator's pane label and herdr agent name alone (recommended), or apply
  Antiphon's `Agent.Name` / `Agent.Slug` as a normal launch does (CARD-0211/0225)?** The card
  says "leave the pane where it is"; the names are how the operator already addresses it. If
  you want Antiphon's names, `PaneTitle`/`AgentSlug` go on the attach request and the existing
  `TryApplyAgentNameAsync` runs — ~10 lines, but the operator's `herdr agent get
  pm-dropcopy-grok` stops resolving.
- **D4 — Grok with no `--session-id` and nothing from `agent_session` (P-A1 "absent" row):
  refuse (recommended) or attach screen-only?** Screen-only has no transcript, so CARD-0055 can
  never confirm a delivery and every queued message parks; `IsWorkingAsync` has nothing to
  read. Refuse, with a message that says to relaunch with `--session-id`. Applies equally to a
  known id whose directory is not found under the runner's `GROK_HOME` (R10) — the alternative
  there is a `grokHome` field on the request, which is a bigger promise than it looks (every
  later resume would need it too).
- **D5 — Replay the operator's existing transcript into `TranscriptEntries` (recommended), or
  start the tail at the file's end?** Reading from offset 0 is what every re-tail already does
  and is what gives `IsWorkingAsync`, the channel dispatcher and the UI the conversation's
  history; the cost is one ingest of the file (1–2 MB on this machine's largest sessions).
  Starting at the end would leave the first working/idle verdict blind until the next turn.
- **D6 — v1 UI = header button + one-field modal (recommended), or API-only with a `curl`
  example?** ~60 client lines on the existing pattern. The pane picker is its own card either
  way (§8).

## 8. Non-goals (the card's, plus what this plan deliberately leaves)

- Adopting a pane Antiphon never launched **after a herdr restart with no sidecar** — the
  card's own out-of-scope; nothing here reads herdr state to reconstruct a binding.
- Changing the allocator's "operator tabs are never split into" rule (this plan *tightens* it:
  an attached pane is not a slot).
- Any DB column for pane / tab / workspace ids or origin (CARD-0160).
- A pane picker (`GET /herdr/panes` on the runner + a server proxy + a modal list) and a CLI
  verb — follow-up card; the inspect endpoint here is its primitive.
- `pane.report_agent` / `pane.release_agent` — herdr agent authority stays untouched
  (CARD-0162 §9).
- Moving an attached pane into an Antiphon 2×2, or `pane.close` on detach.
- A durable identity token that survives a herdr restart (Q3): not backed by the measured
  contract (P7); documented as a limitation.
- Card-owned sessions: attach is for standing agents only (`CardId == null`); a card session
  attached to an operator pane would let a settle report land in someone's private conversation.
