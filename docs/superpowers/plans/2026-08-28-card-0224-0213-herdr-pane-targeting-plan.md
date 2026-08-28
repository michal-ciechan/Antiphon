# CARD-0224 + CARD-0213 — Herdr pane targeting: relaunch into our own pane after pid loss (S1), attach to an operator's pane (S2)

**Date:** 2026-08-28
**Status:** planned (design only — nothing here is implemented)
**Cards:** CARD-0224 (`148067e3-839e-48fd-9095-9de088d09803`, "AlwaysOn Herdr relaunch after
grok.exe death opens a new grok-gkp-project tab instead of the existing pane") and CARD-0213
(`dac55591-f4fa-4949-bdc6-30688158ddc0`, "Herdr: attach/adopt an existing operator pane instead of
launching a duplicate"), board Antiphon. Both are GitHub-tracker-sourced (#16 is CARD-0213).
CARD-0224 carries two measured live repros (2026-08-27); **this plan re-verifies the code path it
cites against `master` @ `3a4e940` and does not re-derive the repros.**
**Scope:** two slices that share one new primitive — *a launch or resume goes to a specific
target pane instead of always through `HerdrPaneAllocator`*. **S1 (CARD-0224)** is the narrow bug
fix and lands first; **S2 (CARD-0213)** builds on S1's primitive and adds the explicit attach API.
They are separately buildable and separately verifiable.
**Evidence base:** the two cards; `docs/herdr-sessions.md` §3–§8 in full; the CARD-0187 probe
corpus `.antiphon/herdr-probes-card0187/` (`proc-grok.json`, `get-grok.json`, `fake-gkp.ps1`);
the bundled schema `.antiphon/herdr-schema-card0160.json` (protocol 20); the code cited below.
`restart-gkp.ps1` is **not on this machine** (`~\.local\bin` has no such file; nothing under
`C:\src` or `D:\src` either) — its behaviour is taken from the card: `Stop-Process` the pane's
`grok.exe`, then `herdr pane run gkp …` into the **same** pane.
**Builds on:** CARD-0160 (the lane; sidecar-only pane ids), CARD-0186 (always-on relaunch into a
new pane — the path this bug rides), CARD-0187 (typed launch script; passive detection; never
`agent.start`), CARD-0162 (events are triggers, never evidence).
**Sibling plan, same files:** `2026-08-28-card-0211-0225-herdr-agent-name-and-pane-title-plan.md`
appends `HerdrLaunchOptions.AgentSlug` and adds `TryApplyAgentNameAsync` inside
`HerdrPaneChild.LaunchAsync`. This plan's contract additions go **after** `AgentSlug`, and its
`LaunchAsync` changes are in the pane-resolution step *before* the script is typed — but the two
builds touch the same method and must be built **sequentially, never in parallel worktrees**.
**Model followed:** `docs/superpowers/plans/2026-08-28-card-0217-sub-second-pages-plan.md`.

## Verdict, in one screen

| Finding (verified 2026-08-28 @ `3a4e940`) | Consequence for the design |
|---|---|
| **A supervisor resume REUSES the Failed session row** — `AgentControlService.StartInteractiveSessionAsync` (`server/Application/Services/AgentControlService.cs:218-247`) relaunches `previous.Id` with `--resume <previous.Id>`; the parity test pins it (`HerdrAlwaysOnChannelParityTests.cs:155`, "supervisor resume reuses the Failed row"). So **the Antiphon session id IS the native Claude/Grok session id** (`BuildSessionIdentityArgs`, `AgentSessionService.cs:1192-1224`), on fresh *and* resumed launches. | S1 needs **no server-side pane id at all**: the runner already knows, from the session id alone, which pane that session last lived in — if it keeps that record past the exit. CARD-0160's "pane ids never in the DB" rule is untouched. |
| **Every exit path deletes the sidecar immediately** (`RaiseVerifiedClosed` `HerdrPaneChild.cs:69`, `MarkVanishedIfDead` `SessionRunnerRuntime.cs:1851-1897`, `RegisterHerdrExited` `:619`, `RetryPendingHerdrAsync` `:562`, `KillAsync` `HerdrPaneChild.cs:187`), and `CollectLiveAntiphonPanes` (`:191`) only counts sidecars whose session is live. That is the whole mechanism of the bug: after the pid loss the allocator sees zero Antiphon panes and the tab reads as an operator tab → `CreateTab`. | §2.1: exits that leave the pane standing move the sidecar to a **last-pane record** (`<SessionLogPath>/herdr/last-pane/<sessionId:N>.json`) instead of deleting it. `LoadAll`, adoption and the allocator keep their exact semantics because the record lives in a different directory. |
| **`pane.process_info` returns `argv`, `cwd` and `name` per foreground process** — measured, not assumed: `proc-grok.json` carries `["…\grok.exe","--no-alt-screen","--always-approve","--session-id","6ec1fe4c-…"]` with `cwd`. `HerdrPaneProcess` (`HerdrApiModels.cs:76-82`) already deserialises all of it; only `Pid` is read anywhere today. | This is the **positive identity** both cards need: "the process in this pane is conversation X" is readable without any herdr metadata token. Adopting a live process is gated on `argv` naming **our** session id; a pane whose process names a different id, or none, is never stolen. |
| **A herdr restart with layout restore keeps the pane id and leaves a bare shell in it** (P7, `docs/herdr-sessions.md` §6; `R2_restored_empty_pane…` in `HerdrAdoptionSweepTests`). So "pane exists, our child gone, foreground empty" is the *same observable* for a herdr restart and for `restart-gkp`/Task-Manager pid loss. | They do not need distinguishing: **an empty PowerShell pane that was ours is relaunched in place in both cases** — typing the launch script into a restored empty shell is a strictly better outcome than opening a tab beside it. Only an *unknown* pane goes to the allocator (card's desired behaviour #3). |
| **The card's "allocator prefers the last tab id even with zero sidecars" touch point would split the operator's tab** — by sequence 2 the Orch tab is operator-owned (their live chat in `w2:p12`), and §4's "operator tabs are never split into" is a rule this plan keeps. | Targeting is **per pane**, never per tab; `HerdrPaneAllocator` is not changed. |
| **The server runtime is stateless about runner sessions in production** — `AgentSessionRuntime.SendInputAsync/KillAsync/GetSessionAsync` (`server/Application/Services/AgentSessionRuntime.cs:820-870`) go to `_runnerClient` by session id; nothing needs a registered adapter. | S2's attach needs only a DB row whose `Id` equals the runner session id; queue, screen mirror, supervision and channel bridge then work unchanged. |
| **No `pane.run` exists in protocol 20** (schema method list has `pane.send_text`/`send_keys`/`send_input`, `agent.start`, `pane.release_agent`, `pane.report_agent_session`; no run). | Relaunch-in-place types the same launch script `LaunchAsync` types today. Adopt-in-place types nothing. |

## 1. What exists today (only what the cards did not already record; verified 2026-08-28)

### 1.1 The live sequence, step by step, in code

1. `restart-gkp` kills `grok.exe` while the runner is up. Two paths race to the same verdict:
   the OS-pid liveness sweep `MarkVanishedIfDead` (`SessionRunnerRuntime.cs:1851`) → `Exited(ProcessVanished)` + `HerdrPaneSidecar.TryDelete`; or the event pump's `pane_exited` / `pane.agent_status_changed` → `VerifyHerdrLivenessAsync` (`:906`) → child not in `foreground_processes` → `CloseHerdrAfterBarFailed` (`:944`) → `RaiseVerifiedClosed(PaneClosed)` (or `RestartPresumedDead` if the pid was OS-alive) → sidecar deleted. The card names `HerdrRestartPresumedDead`; which of the three reasons wins is timing, and all three delete the sidecar.
2. The server maps the exit to `Failed` (`AgentSessionRuntime.CloseSessionOnExitAsync`, `:143-170`; never `Stopped` for herdr reasons). `AgentSupervisorService.SuperviseAsync` (`AgentSupervisorService.cs:102-215`) schedules a restart, then `StartAsync(agent, Fresh: false)` → `FindResumableSessionAsync` finds the Failed row (same cwd, same kind) → **same session id**, `--resume <id>`, `EnqueueInteractiveSession(previous.Id, …, resume: true)`.
3. `AgentSessionService.LaunchInteractiveProcessAsync` (`AgentSessionService.cs:349-427`) → `BuildRuntimeLaunchSpecAsync` (`:1065-1112`, `HerdrLaunchOptions` with kind) → `adapter.StartAsync` → `POST :17204/sessions` → `SessionRunnerRuntime.StartAsync` (`:95-188`; an exited session with the same id is replaced) → `RunnerSession.StartHerdrAsync` (`:961`) → `HerdrPaneChild.LaunchAsync` (`HerdrPaneChild.cs:76`) → `AllocatePaneAsync` (`:396`) → `HerdrPaneAllocator.Allocate([])` → `CreateTab`.
4. The new tab is labelled `PaneTitle` (CARD-0225 fixes the value), the old pane keeps the operator's relaunched grok, and the two share one native session.

### 1.2 The adoption bar and its callers (what S1 must not weaken)

`EvaluateHerdrBarAsync` (`SessionRunnerRuntime.cs:516-559`): socket → `pane.get` (unknown pane ⇒ `RestartPresumedDead`, orphan pid killed) → `pane.process_info` lists `ChildPid` (absent ⇒ `RestartPresumedDead`, orphan killed) → `pane.read` answers ⇒ `Adopt`. Unreachable ⇒ `Unreachable` (OS-alive) / `ChildGone`. Called from `AdoptHerdrSessionsAsync` (`:471`, runner start, before the HTTP API listens) and `RetryPendingHerdrAsync` (`:562`, the pending sweep). The same three checks run at runtime as `VerifyHerdrLivenessAsync` (`:906`) and `TryStampHerdrVerifiedAsync` (`:1470`). **None of these change in S1**: a dead child is still an exit, and the row still goes `Failed`. What changes is only what the *next launch of that id* does.

### 1.3 The launch-in-place ingredients already present

- `HerdrPaneChild.AttachExistingAsync` (`:57`) binds a pane id + sidecar without typing (used by `AdoptHerdrAsync`, `:1512`).
- `RequirePowerShellShellAsync` (`:285`), `WaitForExpectedAgentAsync` (`:318`), the script write/type (`:104-111`), the `process_info` pid read (`:116-125`) and the sidecar write (`:129-143`) are all already factored inside `LaunchAsync`; only the pane they run against is decided by `AllocatePaneAsync`.
- `RunnerSession.StartTailerFor(request, childStartUtc)` (`:1027`) selects the Grok deterministic path from `(Env.GROK_HOME, Cwd, sessionId)`, Codex discovery, or Claude discovery with `resumeLaunch` from the args — reusable verbatim for an adopted process once `childStartUtc` is known.
- `IProcessLivenessProbe` (`ProcessLivenessProbe.cs:9-20`) has `IsAlive(pid, startedAt)` (PID-reuse tolerance 2 min) and `TryGetProcessName(pid)`; **no start-time reader** — adopting a process we did not start needs one (§2.1 step A4).
- Server side: `HerdrAgentKindMap` (`server/Application/Services/HerdrAgentKindMap.cs`), `AgentService.ValidateSessionBackendPairing` (`AgentService.cs:1365`, `409 herdr_refused`), `AgentControlService.StopAsync` (`:335-372`, kills the live session then suspends supervision), `ISessionRunnerClient` (`server/Application/Interfaces/ISessionRunnerClient.cs:35-45`) mirrored by `SessionRunnerHttpClient` and the test double `DirectSessionRunnerClient`.

### 1.4 Test seams

- Runner: `FakeHerdrServer` (`tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs`): `SetPaneProcessInfo(paneId, shellPid, params (Pid, Name)[])` emits `argv: [name]` only (`:507-545`) — **no argv scripting**; `ApplyLaunchDetection` (`:585`) sets `pane.Agent` once and **never clears it**, so an emptied pane still reads `agent: claude` — both must grow for S1's tests. `HerdrAdoptionSweepTests` (`[NotInParallel("SessionLiveness")]`) and `HerdrLaunchShapeTests` (`[NotInParallel("HerdrLaunchShape")]`) are the classes the card names; both drive a real `SessionRunnerRuntime` over the fake and assert on `fake.Requests`.
- Server: `HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(Herdr)` asserts at `:162` `CountAgentPanes(fake).ShouldBeGreaterThan(1, "resume allocates a new pane; the emptied one is left standing")` — **that assertion encodes the bug** and flips in S1 (§5).

## 2. S1 — CARD-0224: relaunch or adopt into the pane we already had

### 2.1 Design

**A. Keep the pane after an exit that leaves it standing — the last-pane record.**

New runner-side file `HerdrLastPane` (`src/Antiphon.SessionRunner/HerdrLastPane.cs`), stored at
`<SessionLogPath>/herdr/last-pane/<sessionId:N>.json`, written from the existing sidecar:

```csharp
public sealed record HerdrLastPane
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid SessionId { get; init; }
    public required string WorkspaceKey { get; init; }
    public required string WorkspaceId { get; init; }
    public required string TabId { get; init; }
    public required string PaneId { get; init; }
    public int? LastChildPid { get; init; }       // for the log line only — never trusted as identity
    public string? Cwd { get; init; }
    public string? AgentKind { get; init; }
    public required string Origin { get; init; }   // HerdrPaneOrigins.Launched | Attached (S2)
    public required string ExitReason { get; init; }
    public required DateTime ExitedAtUtc { get; init; }
    // PathFor / SaveAtomic / TryLoad / TryDelete / DeleteOlderThan — same shape as HerdrPaneSidecar
}
```

`HerdrPaneSidecar.TryDelete` is replaced at exactly these sites by `HerdrPaneSidecar.Retire(settings, sessionId, reason)` (move to last-pane, then delete the sidecar):
`RaiseVerifiedClosed` (`HerdrPaneChild.cs:69`), `MarkVanishedIfDead` (`SessionRunnerRuntime.cs:1890`), `RegisterHerdrExited` (`:623`), `RetryPendingHerdrAsync` (`:581`, `:588`). **Not** at `KillAsync`'s `pane.close` success (the pane is gone — plain delete, as today), **not** at `PaneLeftOpen` (`HerdrPaneChild.cs:215`, `KillPendingHerdr` `SessionRunnerRuntime.cs:1842`) — a foreign process owns that pane now and the operator asked us to stop; plain delete. A last-pane record whose pane is unknown to `pane.get` at the next launch is deleted then; records older than `HerdrSettings.LastPaneRetentionDays` (default **7**) are pruned in `AdoptHerdrSessionsAsync`. **Nothing reads the directory except the launch path** — `LoadAll`, adoption, the allocator and the event pump are byte-for-byte unaffected.

**B. Resolve the target pane before allocating — `ResolveTargetPaneAsync`.**

`HerdrPaneChild.LaunchAsync` (`:76`) replaces the unconditional `AllocatePaneAsync` call with:

```
target = await ResolveTargetPaneAsync(workspaceId, opts, request, ct)
   1. candidate = HerdrLastPane.TryLoad(sessionId)
                  ?? (opts.ReusePaneOfSessionId is Guid prev ? HerdrLastPane.TryLoad(prev) : null)
      none → Allocate (today's path, unchanged)
   2. candidate.WorkspaceKey != opts.WorkspaceKey → log, delete record, Allocate
      (the agent moved project; the old pane is in the wrong workspace)
   3. pane.get(candidate.PaneId) throws pane_not_found → delete record, Allocate  [card #3: true restart that dropped the pane]
   4. proc = pane.process_info(candidate.PaneId); pane = pane.get(...)
      4a. foreground empty (or only the shell pid):
            candidate.Origin == Attached → delete record, Allocate            [§3.4: we never TYPE into a pane we did not create]
            shell not PowerShell         → HerdrLaunchException("pane shell … is not PowerShell", code pane_shell)  (same text as today)
            else                         → RelaunchInPlace(candidate)          [card #1]
      4b. pane.Agent == expected kind AND exactly one foreground process whose argv names OUR session id
          (`--session-id <id>` | `--resume <id>` | `-s`/`-r` | `--resume=<id>`, id == request.SessionId, ordinal-insensitive GUID compare)
                                         → AdoptInPlace(candidate, proc)       [card #2]
      4c. anything else — a different id, no id, a different kind, a non-agent foreground process, more than one foreground process
                                         → HerdrLaunchException(code pane_occupied, "pane {PaneId} is occupied by {name} pid {pid} ({native id or 'no --session-id'}); not stolen — run attach (CARD-0213) or free the pane")
                                            record KEPT (the operator may free the pane before the next backoff)
```

`opts.ReusePaneOfSessionId` is one new trailing optional on `HerdrLaunchOptions` (**after** CARD-0211's `AgentSlug`), set by `AgentControlService.StartInteractiveSessionAsync` on the **fresh** arm (`:249-282`) from `agent.PersistentSessionId` before it is overwritten — so a `FreshAfterResumeFailures` fallback (new row id, `--session-id <new>`) still lands in the agent's pane instead of a new tab. Null on the wire means "session id only", so an old server in front of a new runner gets exactly the resume-arm behaviour and nothing else. Never set on card spawns.

**C. `RelaunchInPlace`** = today's `LaunchAsync` from `pane.rename` onwards (`:99-143`) against `candidate.PaneId`/`TabId`/`WorkspaceId`: rename, `report_metadata`, PowerShell check (already passed in 4a), write + type the script, `WaitForExpectedAgentAsync`, `process_info` pid, new sidecar (`LaunchedAtUtc = now`, `Origin = Launched`), delete script, delete the last-pane record. **No `tab.create`, no `pane.split`, no `tab.rename`** — the tab keeps whatever label the operator gave it (card: "keep tab/pane labels"); the pane label is re-set to `PaneTitle` as every launch does. Failure after the script is typed follows today's catch (`StartHerdrAsync` `:1011-1022` kills then disposes) — which for an in-place pane means `KillAsync` → `pane.close`. That closes a pane that was already ours and already empty before we typed; acceptable and stated.

**D. `AdoptInPlace`** types nothing: `AttachExistingAsync`-style bind to `candidate.PaneId`, `pane.rename`/`report_metadata` refreshed, sidecar written with `ChildPid = proc.Pid`, `ShellPid = proc.ShellPid`, **`LaunchedAtUtc = process start time`** (new `IProcessLivenessProbe.TryGetStartTimeUtc(pid)`; the `IsAlive` PID-reuse tolerance and Claude's C3 both key on it), `Origin = Launched` (the pane was ours), returns `ChildStarted(pid, null, startUtc)` so `StartHerdrAsync` publishes `SessionStarted` and calls `StartTailerFor(request, startUtc)` exactly as a fresh launch would. Because the adopted process's native id equals the session id, the Grok tailer path and Claude's exact-named `<id>.jsonl` claim are the same files a normal resume would bind; `IsResumeLaunch(request.Args)` is already true on the supervisor's resume, so C3 is waived as it must be for a process whose history predates us. The last-pane record is deleted. Log at Information: `"Adopted live {kind} pid {pid} in pane {PaneId} for session {SessionId} (operator relaunch; nothing typed)"`.

**E. Codex.** `codex.cmd` carries no session id in argv, so 4b can never prove identity for Codex: an occupied Codex pane is 4c (`pane_occupied`); an empty one relaunches in place. Stated in the doc, pinned by a test.

**F. What S1 does NOT change.** The exit verdicts and their reasons; `HerdrPaneAllocator`; `CollectLiveAntiphonPanes`; the §6 bar; the event pump; `KillAsync`'s foreign-process refusal (`:198-216`); the `Failed → supervisor → resume` cycle (so the "optional" in the card — a healthy adopt with no new row — is already true by construction: the resumed row *is* the old row; the only artefacts are one `Crash` incident and a Failed→Running blip, which is the truth of what happened to the process).

### 2.2 The three questions the brief asked

1. **"Our pid gone" vs "a different process's pid is foreground"** — step 4: foreground **empty** ⇒ ours is gone and nothing replaced it ⇒ relaunch; foreground **non-empty** ⇒ identity by `argv` against the session id, with `pane.get.agent` as the kind check; any doubt ⇒ `pane_occupied`, never a steal, never a split. The existing `KillAsync` refusal is untouched and now has a launch-time twin.
2. **Adopting an operator `restart-gkp` relaunch** — step 4b + D: argv must name our id (the gkp wrapper passes `@args` through to `grok.exe`, `fake-gkp.ps1`; the measured argv shape is `proc-grok.json`). The tailer binds the same deterministic path the row already used. If `restart-gkp` was run with a *different* named session, that is a different conversation and 4c refuses it with the id in the message.
3. **A true herdr restart** — pane id restored with a bare shell ⇒ 4a relaunch in place (better than today, no new tab); pane id not restored ⇒ step 3 allocator (today's behaviour, unchanged). `R2`/`R4` adoption verdicts are unchanged because the *exit* is unchanged; only the subsequent launch differs. The parity test's "resume allocates a new pane" assertion flips to "resume reuses the pane" — that is the deliberate contract change, and `docs/herdr-sessions.md` §6 is rewritten to say so.

## 3. S2 — CARD-0213: attach a standing Herdr agent to a pane Antiphon never launched

Builds on §2's `AdoptInPlace` (the pane inspection, the argv identity read, the process-start-time probe, the sidecar-with-origin) and adds the door the operator opens by hand.

### 3.1 API

`POST /api/agents/{id}/attach-herdr` `{ "paneId": "w2:p3" }` → `200 AgentDetailDto` (the same shape `/start` returns). Server (`AgentControlService.AttachHerdrAsync`, beside `StartAsync`/`StopAsync`; endpoint in `AgentEndpoints.cs` after `/stop`):

| Check | Failure |
|---|---|
| agent exists, locked (`LockAgentAsync`) | 404 |
| `agent.SessionBackend == Herdr` and `HerdrAgentKindMap.TryMap(kind)` (`ValidateSessionBackendPairing`) | `409 herdr_refused` |
| no live session (`HasLiveSessionAsync`) | `409 session_active` |
| runner advertises `herdr` (`GetSessionBackendCapabilityMismatchAsync`) | `409 herdr_refused` with the runner's reason |
| runner `POST /sessions/attach` (below) succeeded | passthrough: `409 herdr_pane_bound` / `herdr_kind_mismatch` / `herdr_pane_unoccupied` / `herdr_pane_foreign` / `herdr_native_id_unknown`; `404 herdr_pane_not_found`; `503 herdr_unreachable` |
| the native id is not already another agent's session (`AgentSessions.Id == nativeId` with `CardId != null`, or an `Agents.PersistentSessionId` that is not this agent) | `409 session_id_taken` — and the server calls runner **detach** so nothing is left half-bound |

On success the server writes the `AgentSession` row **with `Id = <the runner's session id>`** (which is the native id when argv had one), `SessionBackend = Herdr`, `AgentKind`, `DefinitionName` from the composed profile, `Cwd = process cwd`, `Status = Running`, `StartedAt = process start`, then `agent.PersistentSessionId = id`, `agent.Status = Running`, `ClearSupervisionLatchAsync`, `AgentChanged` + `SessionStarted` events. If the row already exists and is this agent's own Stopped/Failed row with the same cwd (the "operator restarted my grok by hand" case), it is reused exactly as a resume reuses it (`:218-247` field restamp). No `/remote-control` (CARD-0212 governs; nothing is typed at attach anyway), no launch note.

Runner: `POST /sessions/attach` with `HerdrAttachRequest(PaneId, ExpectedKind, TranscriptFormat, AgentSlug, FallbackSessionId, WorkspaceKey, PaneTitle)` → `SessionRunnerRuntime.AttachHerdrAsync`:

1. `pane.get(paneId)` (unknown ⇒ `pane_not_found`); `pane.Agent == ExpectedKind` (else `kind_mismatch`, naming both; null ⇒ `pane_unoccupied`).
2. no live session or sidecar references `paneId` (`LiveHerdrPanes()` + `HerdrPaneSidecar.LoadAll`) ⇒ else `pane_bound` naming the session.
3. `pane.process_info`: exactly one foreground process (besides the shell) whose `name` is the kind's executable family (`grok`, `claude`, `codex`/`cmd` for a `.cmd` launcher — pinned list beside `HerdrAgentKinds`) ⇒ else `pane_foreign` naming the pids.
4. native id from argv (§2.1 4b rule). Grok: required (its transcript path is `GROK_HOME/sessions/<enc-cwd>/<id>/updates.jsonl` and nothing else can locate it) ⇒ missing ⇒ `native_id_unknown`. Claude: optional — missing ⇒ session id = `FallbackSessionId`, transcript by the CARD-0006 discovery rules (binds after the first prompt Antiphon sends; a later `--resume <row id>` will not find that conversation and falls back fresh — stated). Codex: never present ⇒ `FallbackSessionId` + discovery.
5. `pane.rename(PaneTitle)` + `pane.report_metadata(antiphon-session)` (best-effort, as launch); optional `agent.rename` via CARD-0211's `TryApplyAgentNameAsync` if that slice has landed.
6. sidecar written with `Origin = Attached`, `ChildPid`, `ShellPid`, `LaunchedAtUtc = process start`, `Cwd = process cwd`; `RunnerSession` registered Running + `Adopted = true`; `SessionStarted` published; `StartTailerFor` with a synthetic request (`Cwd` from the process, `Env` = the runner's own environment for `GROK_HOME`, `Args` containing `--resume <id>` so `ResumeLaunch = true`); `NotifyPaneSetChanged()` so the pump subscribes.

`RunnerSessionDto` gains `HerdrOrigin` (`launched` | `attached` | null) — additive, after `HerdrVerifiedAtUtc`; the server DTO and `AgentSessionLiveDto` mirror it as `herdrOrigin` for the badge and the Stop button label.

### 3.2 Q1 — who owns stop for an adopted pane (recommendation: **detach, never kill**)

The decision lives where the origin lives — in the runner's sidecar — so there is **no new database column**: `RunnerSession.KillAsync` on a session whose sidecar says `Origin = Attached` performs **detach**: clear our metadata tokens/state labels (`pane.report_metadata` with `clear_state_labels`, best-effort), delete the sidecar (no last-pane record — §3.4), raise `Exited(ExitCode: 0, HerdrExitReasons.Detached)`. The server maps a new `AgentExitReason.HerdrDetached = 9` as a **clean stop** (`cleanStop` in `CloseSessionOnExitAsync:163` gains `|| exitReason == HerdrDetached`; `AgentSessionService.KillAsync:775` already lands `Stopped` when `killed` is true). `AgentControlService.StopAsync` is unchanged in code — it still "kills", and for an attached session that means detach; its always-on suspension still applies (a human said stop). The TUI keeps running in the operator's pane, untouched. **A normally launched herdr session's stop is unchanged.** The UI reads `herdrOrigin === 'attached'` to label the button **Detach**.

### 3.3 Q2 — native session id (recommendation: **the pane's id becomes the row id**)

Read from `argv` at attach (§3.1 step 4), never typed, never guessed from the newest transcript directory. Using it *as* the `AgentSession.Id` is what makes everything downstream free: the Grok deterministic path, Claude's exact `<id>.jsonl` claim, a later supervisor `--resume <id>` after the pane dies, and the proxy's named session (which `gkp` maps onto that same `--session-id`). A pane without an id is refused for Grok and discovery-bound for Claude/Codex (stated above). Nothing is sent to the process to "confirm" — a probe prompt into an operator's live conversation is exactly the intrusion this card exists to avoid.

### 3.4 Q3 — identity durability (recommendation: **no new durability; the sidecar is the record, and an attached pane is never typed into**)

`pane.report_metadata` stays best-effort (24 h TTL, restart survival unverified — `docs/herdr-sessions.md` §3). The runner sidecar with `Origin = Attached` is the binding: a **runner** restart re-adopts it through the unchanged §6 bar (pid still listed ⇒ adopt; gone ⇒ `RestartPresumedDead`, as for any session). A **herdr** restart kills it — today's documented contract, kept as-is for attached panes. What S1 adds is deliberately *not* extended to attached panes: an attached session's exit writes **no last-pane record**, and §2.1 step 4a refuses to relaunch into an `Attached`-origin candidate, so **Antiphon never types a launch script into a pane it did not create** — after an attached pane dies, an always-on agent's resume goes through the allocator into Antiphon's own tab (CARD-0186 behaviour), and the operator can re-attach by hand if they rebuilt their pane. The one adopt-only exception: the `AdoptInPlace` arm may still bind a live same-id process in an attached pane (the operator restarted their own grok with the same session) because it types nothing. Documented as a limitation, not a guarantee herdr cannot back.

### 3.5 Q4 — UI surface (recommendation: **API first; one small "Attach to herdr pane…" action in the Agents page header**)

The agent page header (`client/src/features/agents/AgentsPage.tsx:196-243`) is where Start/Stop already live as a `Group` of buttons keyed on `liveSession`; agent *settings* are a form that PATCHes persisted fields, and attach is a one-shot action with a live-session outcome, so it belongs beside Start, not in the settings modal. v1: when `selected.data.sessionBackend === 'Herdr'` and there is no live session, a third button **Attach…** (`TbPlugConnected`) opens a `Modal` with one `TextInput` (`paneId`, placeholder `w2:p3`, helper text pointing at `herdr pane list`) and calls a new `useAttachHerdrPane(agentId)` mutation (`client/src/api/agents.ts`, the `useStartAgent` shape at `:496`; `apiPost` to `/agents/${id}/attach-herdr`, same cache invalidation). Errors surface through `getApiErrorMessage` (the 409 codes above read well as-is). The Stop button reads **Detach** when `liveSession.herdrOrigin === 'attached'`, and `HerdrStatusBadge` gets an `attached` chip. **Deferred and flagged:** a pane picker (the server has no herdr client; a list would need a runner passthrough `GET /herdr/panes` and a server proxy — its own small card), and an "attach" line on the CLI (`scripts/agent.ps1`? none exists; a `curl` example goes in `docs/antiphon-api.md`). If the build must shrink, ship the endpoint + docs and leave the modal for the follow-up card — but say so in the card, do not drop it silently.

### 3.6 Refusals (all `409` unless noted; problem-details `code` as listed)

| Case | Code |
|---|---|
| agent not on Herdr, or kind OpenCode/Raw | `herdr_refused` |
| agent already has a live session | `session_active` |
| pane unknown | `herdr_pane_not_found` (404) |
| pane's `agent` ≠ agent's kind | `herdr_kind_mismatch` |
| pane has no detected agent | `herdr_pane_unoccupied` |
| foreground has a non-agent / more than one process | `herdr_pane_foreign` |
| pane already in a live Antiphon sidecar | `herdr_pane_bound` |
| Grok pane launched without `--session-id` | `herdr_native_id_unknown` |
| native id already another agent's / a card session | `session_id_taken` |
| herdr unreachable | `herdr_unreachable` (503, existing filter) |

## 4. What this costs its neighbours

- **`HerdrAlwaysOnChannelParityTests:162`** flips (S1): the resumed session lands in the **same** pane (`CountAgentPanes == panesBeforeDeath`, pane id equal), and the fake must clear detection when the pane is emptied (new `FakeHerdrServer.ClearDetectedAgent(paneId)`), or the relaunch would read `agent: claude` on an empty pane and take the wrong arm.
- **CARD-0211/0225** (same `LaunchAsync`; `HerdrLaunchOptions` ordering): build sequentially; this plan's `ReusePaneOfSessionId` goes after `AgentSlug`. If 0211 lands first, `AdoptInPlace`/attach call its `TryApplyAgentNameAsync`; if not, they skip naming and 0211 adds the call.
- **CARD-0212** (never `/remote-control` to Grok): attach types nothing, so it neither needs nor conflicts with that gate; the attach path must simply not call `SendRemoteControlCommandsAsync`.
- **Reconciliation** (`SessionReconciliationService`): an attached runner session with no DB row (server write failed after runner attach) is the existing "runner-alive, no row ⇒ alert only, never kill" arm — the server's compensating **detach** call makes that window a few milliseconds, and even if it fails nothing is killed.
- **`HerdrStatusPushService` / event pump**: key on `LiveHerdrPanes()` which reads `HerdrPaneChild.PaneId` — an in-place relaunch or attach registers through the same `NotifyPaneSetChanged()`; no change.
- **Disk**: one small JSON per exited herdr session for ≤ 7 days under `herdr/last-pane/`; pruned by the adoption sweep.
- **Docs to update on build**: `docs/herdr-sessions.md` §3 (origin), §4 (target resolution before the allocator; "relaunch in place"), §6 (a herdr restart with layout restore now relaunches into the restored pane; an unknown pane still allocates), §8 (rows for `pane_occupied`, `herdr_pane_*` codes, `Detach`), new §"Attaching an operator pane"; `docs/antiphon-api.md` agents block (+ `attach-herdr`, codes); `docs/agent-kinds.md:358` still says "(ClaudeCode only)" — fix in passing; one `AGENTS.md` gotcha bullet per slice.

## 5. Tests (all red-before-green; run commands at the end)

### S1 — runner (`tests/Antiphon.SessionRunner.Tests`)

`FakeHerdrServer` additions first: `SetPaneProcessInfo` overload taking `(int Pid, string Name, string[] Argv, string? Cwd)`; `ClearDetectedAgent(paneId)`; `SeedDetectedAgent(paneId, kind)`.

| Class | Test | Pins |
|---|---|---|
| `HerdrAdoptionSweepTests` | `R14_child_dead_pane_alive_relaunch_same_id_reuses_the_pane_and_creates_no_tab` — start; `SweepVanishedSessions(dead probe)` ⇒ `ProcessVanished`, sidecar gone, last-pane record present; `ClearDetectedAgent` + empty `process_info`; `StartAsync` **same session id** ⇒ `Running`, sidecar `PaneId` == original, exactly **one** `tab.create` in `fake.Requests` over both launches, the typed `& '…launch.ps1'` line went to the original pane, last-pane record deleted. | card's named test |
| `HerdrAdoptionSweepTests` | `R15_operator_relaunched_same_native_session_is_adopted_not_retyped` — after the exit, `SetPaneProcessInfo(pane, 1, (777, "grok.exe", [exe, "--session-id", id]))` + `SeedDetectedAgent(grok)`; relaunch ⇒ `Running`, `Pid == 777`, **no** `pane.send_text` after the first launch, sidecar `ChildPid == 777`, transcript sidecar `Format == grok` with the deterministic path. Uses a `StubProbe` that answers `TryGetStartTimeUtc`. | card #2 |
| `HerdrAdoptionSweepTests` | `R16_foreign_occupant_refuses_the_launch_and_keeps_the_record` — argv names a *different* GUID (and a second arm: `claude.exe` in a grok launch; a third: two foreground pids) ⇒ `HerdrLaunchException` with code `pane_occupied` naming pane, pid and the foreign id; no `send_text`, no `tab.create`, **no `pane.close`**; last-pane record still present. | brief Q1 |
| `HerdrAdoptionSweepTests` | `R17_unknown_pane_after_exit_allocates_and_drops_the_record` — remove the pane from the fake ⇒ second launch `tab.create`s; record deleted. | card #3 / herdr restart without restore |
| `HerdrAdoptionSweepTests` | `R18_restored_empty_shell_after_R2_is_relaunched_in_place` — the existing R2 sequence, then a launch with the same id ⇒ same pane, no `tab.create`. | brief Q3 |
| `HerdrAdoptionSweepTests` | `R19_last_pane_records_are_pruned_and_never_adopted` — a 8-day-old record ⇒ pruned by `AdoptOrphanedHostsAsync`; a fresh record ⇒ no session registered, no `SessionExited` published. | §2.1 A |
| `HerdrLaunchShapeTests` | `Resume_with_a_last_pane_must_not_call_tab_create` (card's named test) and `ReusePaneOfSessionId_targets_the_previous_sessions_pane_for_a_fresh_id`; `Codex_occupied_pane_is_refused_even_with_the_right_kind`; `Relaunch_in_place_never_calls_tab_rename_or_pane_split`. | §2.1 B/E |
| `HerdrPaneChildKillTests` | `Kill_after_pane_close_writes_no_last_pane_record`; `PaneLeftOpen_writes_no_last_pane_record`. | §2.1 A exclusions |
| `HerdrPaneSidecarTests` | `HerdrLastPane` round-trip + `Retire` moves atomically. | — |

### S1 — server (`tests/Antiphon.Tests`)

- `HerdrAlwaysOnChannelParityTests` (Herdr arm): the flipped assertion at `:162` — resumed session in the **same** pane; plus `CountAgentPanes(fake).ShouldBe(panesBeforeDeath)`.
- `AgentControlServiceIntegrationTests` (or a new `HerdrLaunchOptionsReusePaneTests`, no DB): fresh arm sets `Herdr.ReusePaneOfSessionId = previous id`; resume arm and card spawns leave it null. `SessionRunnerHttpClientHerdrWireTests`: field on the wire, absent field ⇒ null.

### S2 — runner

`HerdrAttachTests` (new, `[NotInParallel("SessionLiveness")]`): success (sidecar `Origin = attached`, `Running`, `Adopted`, `SessionStarted` published, Grok transcript sidecar path from `process_info.cwd` + argv id, no `send_text`, pump subscribes); each refusal in §3.6 by code; **Claude without `--session-id` binds by discovery with `FallbackSessionId`**; **kill on an attached session detaches** (`Exited(0, HerdrDetached)`, sidecar gone, no `pane.close`, no last-pane record, `pane.report_metadata clear_state_labels` sent); **runner restart re-adopts an attached sidecar with its origin intact**; an attached pane's exit writes no last-pane record and a subsequent launch of that id allocates (§3.4).

### S2 — server

`AgentAttachHerdrTests` (new, `[NotInParallel]`, `DirectSessionRunnerClient` + `FakeHerdrServer`, the parity harness): success path (row `Id == native id`, `Status Running`, `PersistentSessionId`, supervision latch cleared, `herdrOrigin == "attached"` on the DTO); reuse of the agent's own Failed row with the same id; `session_id_taken`; each 409 passthrough; **Stop on an attached agent lands `Stopped` with the TUI still "running" in the fake**; an always-on attached agent whose pane dies is resumed by the supervisor into an **allocated** pane. Client: `AgentAttachHerdr.test.tsx` (msw, the `AgentSessionBackend.test.tsx` pattern) — button visible only for Herdr + no live session; Detach label on `herdrOrigin === 'attached'`.

### Run commands

```powershell
# runner suite (the herdr classes are NotInParallel-grouped; whole assembly is fine)
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c0224/ -- --treenode-filter "/*/Antiphon.SessionRunner.Tests/Herdr*/*"
# server, herdr-related only (chunked; never co-schedule with the runner suite)
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0224/ -- --treenode-filter "/*/Antiphon.Tests.Application/Herdr*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0224/ -- --treenode-filter "/*/Antiphon.Tests.Application/AgentAttachHerdrTests/*"
pwsh -File scripts/test-client.ps1
# afterwards
Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-c0224 | Remove-Item -Recurse -Force
```

**Close-out evidence (in the cards):** S1 — on the live herdr, kill `grok.exe` in `PM-Orchestrator-Grok`'s pane (`2ee02f40`), wait for the supervisor, paste `herdr pane list` showing the same pane id and no new tab, plus the runner log line (`Adopted live grok …` or the typed relaunch). S2 — `POST /api/agents/29829120-…/attach-herdr {"paneId":"w2:p3"}` against `pm-dropcopy-grok`, paste the 200 body (`herdrOrigin: attached`), a queue delivery confirmed by transcript, then Stop and `herdr pane get w2:p3` showing the grok still alive.

## 6. Decisions that are the operator's — each with a recommendation

- **D1 (S1) — a pane occupied by a foreign/unidentifiable process: refuse the launch (recommended) or fall back to the allocator with a Warning?** Refusing means an always-on agent stays down through its backoff ladder until a human frees the pane or attaches — loud, and consistent with "never bind a stranger's conversation" (CARD-0006) and "unclaimed never implies kill" (CARD-0056); after `FreshAfterResumeFailures` the fresh arm still reaches the pane via `ReusePaneOfSessionId`, so it keeps refusing rather than silently duplicating. The alternative keeps availability but re-creates exactly the duplicate the card reports, just with an incident attached. If availability wins, the change is one line in step 4c (Allocate + `HerdrPaneOccupied` Warning incident) and one test arm.
- **D2 (S1) — relaunch into a restored-but-empty pane after a *herdr* restart (recommended: yes)?** The card lists this as "may still be correct as-is". Relaunching in place is strictly less clutter and cannot mis-bind (the pane is empty and was ours). Saying no means step 4a needs a way to tell the two empties apart — there is none in the evidence (§1.2), so "no" would really mean "never relaunch in place", which is the bug.
- **D3 (S1) — `ReusePaneOfSessionId` on the fresh arm (recommended: yes)?** Without it a `Fresh` fallback opens a new tab beside the agent's pane. Cost: one contract field and one server line. Skip only if the sibling contract change (CARD-0211) is not landing.
- **D4 (S2) — stop = detach for attached panes (recommended), or stop = kill with a separate `/detach`?** Detach-on-stop needs no column and no new endpoint and never kills a process Antiphon did not start. A second explicit endpoint is easy to add later if "detach but keep the agent Running" turns out to be wanted (it is not, today: an agent with no session is Stopped by definition).
- **D5 (S2) — Grok pane without `--session-id`: refuse (recommended) or attach screen-only?** Screen-only would attach with no transcript, so CARD-0055 delivery verification could never confirm a send — every queue delivery would park. Refuse, with the message telling the operator to relaunch their grok with `--session-id`.
- **D6 (S2) — v1 UI: header button + one-field modal (recommended), or API-only with a `curl` example?** The button is ~60 client lines on an existing pattern; the pane picker is the part that needs its own card either way.

## 7. Non-goals (named by the cards or the brief)

- The tab/pane title value and `agent.rename` — CARD-0225 / CARD-0211.
- A pane picker UI and a `GET /herdr/panes` runner passthrough — follow-up card.
- Adopting panes Antiphon never launched *after a herdr restart with no sidecar* — the card's own out-of-scope.
- Changing the allocator's "operator tabs are never split into" rule, or any DB column for pane/tab/workspace ids (CARD-0160).
- A `pane.agent_detected` event arm that adopts an operator relaunch *without* the Failed→resume cycle — possible later on the same primitive; not needed for either card's acceptance.
- `pane.release_agent` / `pane.report_agent` — herdr agent authority stays untouched (CARD-0162 §9).
