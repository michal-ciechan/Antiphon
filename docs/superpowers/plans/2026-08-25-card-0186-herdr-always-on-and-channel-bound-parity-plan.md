# CARD-0186 — herdr for AlwaysOn and channel-bound agents: PtyHost parity — investigate + design

**Date:** 2026-08-25 · **Card:** CARD-0186 (`2dd90c35-9eec-4dea-bbf5-006c07dcfbca`) ·
**Status:** design (no implementation in this pass) ·
**Verified against:** `master` @ `a073f49`. Every file:line below was re-read out of the code on
that commit. Nothing herdr-side was re-measured in this pass — herdr 0.8.2 is live on this machine
(two `herdr` processes since 2026-08-23 22:06) and the operator's panes are in it, so the two
probes this design needs (§8, P7/P8) require a deliberate herdr restart and are left for the
build slice that depends on them, not run under the operator's feet.

**Established facts, not re-derived here:**
- CARD-0183's closure (2026-08-25, master `92a4bf0`): the launch path into a NEW herdr pane exists
  end to end (`AgentSessionService` → `HerdrLaunchOptions` → `SessionRunnerRuntime.StartHerdrAsync`
  → `HerdrPaneChild.LaunchAsync`); the refusal sites are exactly
  `AgentService.ValidateSessionBackendPairing` (`AgentService.cs:1254`), its four callers (create
  `:292`, kind-change `:346`, PATCH `:404`, launch backstop
  `AgentSessionService.EnsureHerdrLaunchAllowed` `:1118`), and the channel-bind gate
  (`ChatChannelService.cs:60`); `HerdrPaneChild.LaunchAsync` hard-codes `HerdrAgentKinds.Claude`
  (`HerdrPaneChild.cs:104`) — that last one is CARD-0187's and is **not** touched here.
- CARD-0160 §6A (adoption bar), CARD-0161 (ceilings keyed on `SessionBackend`, `blocked` defers),
  CARD-0162 (events are triggers, `HerdrStatusDisagreement` = 34), CARD-0164 (unobservable-baseline
  confirm) — all shipped; `docs/herdr-sessions.md` is current against them.
- CARD-0056's constraint outranks everything below: **unclaimed never implies kill**, and a kill
  needs positive identity of the thing being killed.

**Related:** CARD-0187 (Grok/Codex kinds on herdr — companion, separate scope), CARD-0160–0164,
CARD-0055/0056/0181 (evidence discipline), CARD-0153 (detection-only precedent), CARD-0141
(withhold re-Enter while blocked).

---

## 0. What "restart the agent on herdr" already is — the finding that shapes the design

The card asks what a supervised restart of a dead herdr child means. The answer is that the
mechanism already exists and is the pty one; the gates are what stop it from ever running:

1. A herdr child dies → the runner publishes `SessionExited` with reason `HerdrRestartPresumedDead`
   / `HerdrPaneClosed` / `ProcessVanished` (`SessionRunnerRuntime.cs:518`, `:762`, `:1582`).
2. The server parses that reason with `Enum.TryParse<AgentExitReason>` and gets **`Unknown`**
   (`SessionRunnerHttpClient.cs:539`); `AgentSessionRuntime.CloseSessionOnExitAsync`
   (`AgentSessionRuntime.cs:144`) marks the row **Failed** ("Process exited (Unknown, code
   unknown)") and the agent Failed.
3. `AgentSupervisorService.SuperviseAsync` (`AgentSupervisorService.cs:104`) finds no live
   persistent session, records `Crash` + `RestartScheduled`, and when due calls
   `AgentControlService.StartAsync(agent, Fresh:false)` (`:203`).
4. `StartInteractiveSessionAsync` finds the Failed row resumable
   (`FindResumableSessionAsync`, `AgentControlService.cs:441` — Stopped **or** Failed, same cwd) and
   re-launches **that row** with `--resume <id>`; `BuildRuntimeLaunchSpecAsync`
   (`AgentSessionService.cs:1062`) reads the row's `SessionBackend` snapshot — still `Herdr` — and
   the runner allocates a **new pane** and `agent.start`s into it.

So on herdr, "restart" = **new pane + `claude --resume <same id>`**, conversation intact, exactly
like the pty lane's resume after a host death. There is no re-`agent.start` into the old pane and
no sidecar re-bind: the sidecar was deleted at the exit verdict and a fresh one is written by the
new launch. This design keeps that shape and fixes the six places where it is unsafe or lossy
(§2). It does **not** build pane reuse (§7, deferred).

## 1. Verdict up front — the decisions

1. **Lift exactly two arms of one function.** `ValidateSessionBackendPairing` loses its `alwaysOn`
   and `channelBound` throws; the `Kind != ClaudeCode` throw stays verbatim (CARD-0187 owns it).
   All four callers and the channel-bind gate keep calling it, so the Kind refusal still runs at
   every site; `ChatChannelService.cs:60`'s own inline `SessionBackend == Herdr` throw is deleted
   (it duplicated the pairing check for one arm). `EnsureHerdrLaunchAllowed` keeps its
   `spec.Herdr is null` refusal and its Failed-marking, with the failure text narrowed to
   "non-Claude agent". §3.
2. **No silent remap — including the one the client does today.** `AgentSettingsModal.tsx:272`
   flips `sessionBackend` to `PtyHost` when AlwaysOn is switched on, and `:296` disables the
   control. Both go. The description text at `:286` drops "not available for always-on or
   channel-bound agents". The server-side rule that a resume keeps the **row's** snapshot is
   revisited in decision 3; nothing anywhere maps Herdr→PtyHost without a PATCH that says so. §3.
3. **A resume restamps `SessionBackend` from the agent.** Today `StartInteractiveSessionAsync`'s
   resume branch (`AgentControlService.cs:321`) restamps `DefinitionName`, `TuiProfileRevisionId`,
   `EffectiveModelId` and `ComposedBundleStamp` on the resumed row — "a resume is a LAUNCH" — but
   not `SessionBackend`, so an operator who PATCHes a running AlwaysOn agent onto Herdr would be
   resumed onto PtyHost forever (the supervisor never starts Fresh until
   `FreshAfterResumeFailures`). The PATCH **is** the operator intent decision 2 demands; the row's
   snapshot still governs ceilings for the session that is actually running, because it now
   records what was actually launched. CARD-0160's "resume-after-PATCH keeps the stamp" rule was
   written when no herdr session could ever be resumed by a supervisor; this replaces it. **Caller
   may overrule** — the alternative is "Stop, then Start" as the only way to change lane, which
   is at least explicit. §3.
4. **Exit reasons become first-class on both sides of the wire.** `AgentExitReason` gains
   `HerdrRestartPresumedDead`, `HerdrPaneClosed`, `HerdrChildGone`, `HerdrPaneLeftOpen`; the runner
   emits the same strings (a `HerdrExitReasons` constants class replaces the four string literals).
   All four map to **Failed** in `CloseSessionOnExitAsync` and in reconciliation pass 1
   (`SessionReconciliationService.cs:175`) — never Stopped: Stopped is operator intent and is the
   key of reconciliation's only auto-kill arm. The Crash incident then reads
   "Session died (HerdrRestartPresumedDead)" instead of "(Unknown, code unknown)". §4.
5. **The adoption bar gets a third axis: OS liveness of the sidecar's `ChildPid`.** herdr runs on
   this machine, so `IProcessLivenessProbe.IsAlive(ChildPid, LaunchedAtUtc)` (start-time-checked,
   `ProcessLivenessProbe.cs:22`) is positive local evidence that needs no socket. It is what lets
   the runner reach a verdict while herdr is unreachable (child dead ⇒ `HerdrChildGone`) and what
   makes the R3/R5 orphan rows of the matrix (§5) safe. §5.
6. **"Herdr unreachable" stops being "leave it and never retry."** `AdoptHerdrSessionsAsync`'s
   unreachable arm (`SessionRunnerRuntime.cs:501`) says "liveness/retry will try again" — it does
   not: `SweepVanishedSessions` (`:604`) walks `_sessions` only, and an unadopted sidecar is in
   nothing. Today the server's reconciliation pass 1 then fails the row ("runner does not know this
   session"), the supervisor launches a replacement, and when herdr returns the runner **never
   re-attaches the original** — the classic two-live-children shape. New: the sidecar is registered
   as a `RunnerSession` in status `"Starting"` with `Adopted = true` and a new DTO field
   `Pending = "HerdrUnreachable"`; the liveness sweep re-runs the full bar on every tick until it
   reaches a verdict. The server treats Starting as live (reconciliation `LiveStatuses`, supervisor
   `LiveSessionStatuses`), so nothing is failed and nothing is duplicated. §6.
7. **Input into a herdr session the runner cannot reach is a deferral, never a kill.** The runner's
   `/input` returns `503 herdr_unreachable` on `HerdrBackendUnavailableException` (today it is an
   unhandled 500 → `NoSubmitOutput` → the AlwaysOn kill at `SessionMessageQueueService.cs:2336`,
   whose `pane.close` cannot reach herdr either, so the row lands Failed "did not exit within the
   configured grace period" and the supervisor's relaunch fails until herdr is back). The queue
   maps it to `FlushResult.Nothing` exactly as it maps `blocked` (`:826`) — no attempt charged,
   nothing parked. A `HerdrUnreachable` incident (kind **37**, Warning; **Critical when
   channel-bound**) is raised by the reconciliation sweep when a runner session has reported
   `Pending` for longer than `HerdrPendingAlertMinutes` (default 5), deduped per session. §6.
8. **Reconciliation re-adopts a herdr row only on herdr-specific positive evidence.** Pass 3's
   buffer probe (`SessionReconciliationService.cs:329`) proves a pty-host pipe is serving; for a
   herdr session `GetBuffer` returns the (empty) ansi log and always answers, proving only that
   the runner is up. The runner's `RunnerSessionDto` gains `Backend` ("pty-host" | "herdr") and,
   for herdr, `HerdrVerifiedAtUtc` — stamped by the single-session GET after
   `VerifyHerdrLivenessAsync` passes. Pass 3 re-adopts a herdr row only when the GET's stamp is
   fresher than the sweep's start; otherwise `ReAdoptProbeFailed`, left alone. The no-row arm
   (alert only) and the Stopped arm (re-issue kill) are unchanged. §6.
9. **The event pump is already right and is not touched** — every event re-runs the bar
   (`HerdrEventPumpService.cs:146`), the replay trap is handled, and a verified close on an
   AlwaysOn session simply feeds the server's exit path from §0. The only pump-adjacent change is
   that `VerifyHerdrLivenessAsync` consults the OS-pid axis before declaring `HerdrPaneClosed`
   (decision 5), so a herdr restart that leaves our child alive is treated as the orphan case, not
   as a clean close. §5.
10. **Kill semantics: kill our own child by pid when the pane cannot be closed; never close a
    pane we cannot prove is only ours.** `HerdrPaneChild.KillAsync` (`HerdrPaneChild.cs:185`)
    refuses `pane.close` when a foreground process other than our recorded child/shell is present —
    correct — but then leaves **our** child running while raising Exited, so a Stopped row sits
    over a live process and the Stopped reconciliation arm re-issues the same refused kill every
    15 s. New: on refusal, terminate `ChildPid` by pid **only if** `IsAlive(ChildPid,
    LaunchedAtUtc)` (the pty lane's own `KillPidBestEffort` bar), leave the pane open, exit with
    `HerdrPaneLeftOpen`, and let the server record a Warning incident naming the pane. §4.
11. **Sidecar hygiene: every exit path deletes the sidecar.** `MarkVanishedIfDead` (`:1582`) is
    the one that does not; the stale sidecar then makes `CollectLiveAntiphonPanes` (`:182`) count
    an empty pane as occupied (`AllocatePaneAsync` only drops panes that are *gone*), skewing the
    quad allocator, and costs a spurious `HerdrRestartPresumedDead` on the next runner restart. §4.
12. **The restored-empty pane is left alone, and the pane the operator closed by hand comes
    back.** Both are consequences of always-on that the pty lane already has (a hand-killed
    pty-host is restarted too); the way out is `Stop` / supervision suspend, not a herdr-specific
    exception. The allocator never reuses the restored pane (it has no sidecar), never closes it
    (§7 leaves reuse to a later card gated on probes P5b/P7), and the operator tidies it. This is
    stated in `docs/herdr-sessions.md` §8. §7.
13. **Channel ingress/egress needs no herdr-specific code.** `ChannelBridgeService` has zero
    references to `SessionBackend`; the reply route is the `SessionQueuedMessages` row
    (CARD-0067); the confirm loop is CARD-0055/0164's and already runs on herdr (CARD-0161). Every
    Critical-when-channel-bound path computes "channel-bound" from `ChatChannels.AgentId`
    (`AgentTaskDispatcher.cs:1154`, `ApiErrorRecoveryService.cs:390`, `AttentionService.cs:558`,
    `TranscriptBindingIncidentService`, the parking path), never from backend — they hold the moment
    the gate is lifted. The build pins that with a backend-parametrised test rather than new code.
    `blocked`-forever on a channel-bound agent is already a Critical-eligible
    `HerdrStatusDisagreement` after 10 min (CARD-0162 §5). §3.
14. **Scope is four build slices, not one.** S1 (gates + client + docs) is a half-day and
    independently shippable but **must not ship alone** — with S2–S4 unbuilt, an AlwaysOn herdr
    agent hits decisions 6/7/10/11 within the first herdr restart. Order S1→S2→S3→S4; S4 needs
    S2's DTO fields. §9.

## 2. Where the current code is unsafe for an always-on herdr agent (the six defects)

Each of these is reachable today only because the gate keeps always-on agents off the lane; all
six are fixed by the slices in §9.

| # | Defect | Where | Consequence once the gate lifts |
|---|---|---|---|
| D1 | Herdr exit reasons parse to `AgentExitReason.Unknown` | `SessionRunnerHttpClient.cs:539` | Crash incidents say "(Unknown, code unknown)"; nothing distinguishes a herdr restart from a crash |
| D2 | Unreachable-at-adoption sidecars are never retried and the session is not listed | `SessionRunnerRuntime.cs:501` | Reconciliation fails the row; supervisor launches a duplicate; original child never re-attached |
| D3 | `/input` on an unreachable herdr is an unhandled 500 | runner `Program.cs:174` → `HerdrPaneChild.WriteAsync` | Queue verdict `NoSubmitOutput` → AlwaysOn kill → kill also fails → Failed row → relaunch storm while herdr is down |
| D4 | `MarkVanishedIfDead` leaves the herdr sidecar | `SessionRunnerRuntime.cs:1582` | Allocator counts an empty pane as occupied; spurious `HerdrRestartPresumedDead` next restart |
| D5 | Kill refusal on foreign foreground process leaves OUR child alive under a Stopped row | `HerdrPaneChild.cs:200` | Stopped reconciliation arm re-issues a refused kill every sweep; child runs unclaimed |
| D6 | Reconciliation's re-adopt probe is not evidence on herdr | `SessionReconciliationService.cs:328` | A Failed herdr row could be re-adopted to Running with the pane gone |

Two more are worth naming though they are not defects: the client modal silently remaps
Herdr→PtyHost (decision 2), and a resume never restamps the backend (decision 3).

## 3. The gates, and what replaces them

**Server.** `ValidateSessionBackendPairing(backend, alwaysOn, kind, channelBound)` keeps its
signature (five callers, one test file pins each) and keeps only the Kind arm. The `alwaysOn` /
`channelBound` parameters stay in the signature for one release so every caller still resolves
the request-final state (the PATCH caller at `AgentService.cs:396–404` does real work to compute
them) — deleting the parameters is CARD-0187's call when it rewrites the function anyway.
`ChatChannelService.UpdateAsync` drops its inline herdr throw. `EnsureHerdrLaunchAllowed` keeps
its shape; `FailureReason` becomes "Herdr launch refused: non-Claude agent."

**Client.** `AgentSettingsModal.tsx`: remove the `if (next && sessionBackend === 'Herdr')
setSessionBackend('PtyHost')` remap, remove `disabled={alwaysOn}`, and make the description the
option's own description regardless of AlwaysOn. Herdr's option description
(`SESSION_BACKEND_OPTIONS`) is reworded: "…does not survive a herdr restart; an always-on agent
is resumed into a new pane by supervision."

**Docs.** `docs/herdr-sessions.md` §1 table shrinks to the Kind row; §6 gains the matrix (§5
below); §8 gains rows for `HerdrUnreachable`, `HerdrPaneLeftOpen`, and "supervision brought back
a pane I closed"; §9 drops always-on/channel-bound from the out-of-scope list.
`SessionBackend.cs` and `Agent.cs` doc comments drop the refusal sentence. AGENTS.md's CARD-0160
bullet is amended in place (one sentence: "CARD-0186 lifted the AlwaysOn and channel-bound
refusals; the Kind refusal stands until CARD-0187").

**Tests to flip** (`AgentSessionBackendTests`): `herdr_on_always_on_is_refused_both_directions`,
`herdr_while_channel_bound_is_refused`, `create_herdr_with_always_on_is_refused`,
`patch_always_on_onto_herdr_agent_is_refused`, `patch_herdr_onto_always_on_agent_is_refused`,
`channel_bind_onto_herdr_agent_is_refused`, `herdr_while_a_channel_names_the_agent_is_refused`
become their `_is_allowed` inverses; `herdr_on_non_claude_is_refused` and
`pty_host_accepts_always_on_channel_bound_and_any_kind` stay. New:
`resume_restamps_session_backend_from_the_agent` (decision 3) and
`channel_bound_herdr_session_escalates_transcript_bind_failed_to_critical` (decision 13, in
`TranscriptBindingIncidentTests` parametrised over both backends).

## 4. Runner-side hygiene (decisions 4, 10, 11)

- `HerdrExitReasons` (runner, `Antiphon.SessionRunner.Contracts` so the server shares the
  strings): `RestartPresumedDead`, `PaneClosed`, `ChildGone`, `PaneLeftOpen`. `AgentExitReason`
  gains the four members with the same names; `CloseSessionOnExitAsync` and reconciliation pass 1
  need no branch — non-zero/unknown exit code already maps to Failed — but the two "clean stop"
  tests get a negative case each so a future "treat pane-closed as Stopped" cannot slip in.
- `HerdrPaneChild.KillAsync` on refusal: `probe.IsAlive(ChildPid, LaunchedAtUtc)` → `Process.Kill`
  by pid (the pty lane's `KillPidBestEffort` shape), log Warning naming the pane and the foreign
  pids, `RaiseExited(PaneLeftOpen)`, delete the sidecar, return `true` (our child is gone; the
  pane is not ours to close). `AgentSessionService.KillAsync` (`:775`) therefore lands the row
  **Stopped**, not the misleading "did not exit within the grace period" Failed. The server raises
  a Warning `HerdrPaneLeftOpen` incident (kind **38**) off the exit event so the operator knows a
  pane needs tidying. `HerdrPaneChild` gets an `IProcessLivenessProbe` constructor parameter (the
  runtime already has one injected for the sweep).
- `MarkVanishedIfDead`: in the `_herdrChild is not null` case, `HerdrPaneSidecar.TryDelete` and
  skip `ShutdownHostAsync` (there is no host). Reason stays `ProcessVanished`.
- `CreateAdoptedHerdrExited` / `RegisterHerdrExited`: unchanged except reason vocabulary.

## 5. The restart / adopt matrix (decisions 5, 9)

Evidence columns: **socket** = `ConnectAndValidateAsync`; **pane** = `pane.get` for the sidecar's
`PaneId`; **listed** = `ChildPid` in `pane.process_info.foreground_processes`; **OS** =
`IsAlive(ChildPid, LaunchedAtUtc)`. The bar is evaluated in that order, and the OS column is
consulted **before** any verdict that would otherwise be `PresumedDead`/`PaneClosed`. "—" = not
reached. Every row's verdict is a positive statement about evidence that was actually read.

| Row | Event | socket | pane | listed | OS | Runner verdict | Server / supervisor |
|---|---|---|---|---|---|---|---|
| R1 | runner restart, herdr up | ok | ok | yes | (not needed) | **Adopt** → Running, `Adopted`, re-tail transcript sidecar | `SessionAdopted` → no state change (already) |
| R2 | runner restart; herdr restarted while runner was down, or child died | ok | ok | no | dead | `Exited(RestartPresumedDead)`, sidecar deleted | Failed → Crash + RestartScheduled → resume into a **new** pane |
| R3 | as R2 but our child is still alive outside the pane | ok | ok | no | **alive** | **Orphan**: kill child by pid (positive identity: pid + start time), then `Exited(RestartPresumedDead)`; Warning log `HerdrOrphanedChildKilled` | as R2. Rationale: relaunching `--resume <id>` next to a live process on the same id is the one thing worse than the kill; the kill is of a process **we** launched and can name |
| R4 | runner restart; pane unknown | ok | **err** | — | dead | `Exited(RestartPresumedDead)` | as R2 |
| R5 | runner restart; pane unknown | ok | err | — | alive | as R3 | as R2 |
| R6 | runner restart; herdr unreachable | **fail** | — | — | alive | **Pending**: listed as `Starting`, `Adopted`, `Pending="HerdrUnreachable"`; liveness sweep re-runs the bar each tick; `/input` → 503 | DB Running stays Running (Starting is live to reconciliation and supervisor); queue defers with no attempt charged; `HerdrUnreachable` incident after 5 min (Critical if channel-bound); no restart, no duplicate |
| R7 | runner restart; herdr unreachable | fail | — | — | **dead** | `Exited(ChildGone)`, sidecar deleted — verdict needs no socket | Failed → supervisor restart; launch throws `HerdrBackendUnavailable` while herdr is down → `StartFailure` ladder (5 s → … → hourly Warning → daily Critical); succeeds when herdr returns; **no pane litter** — the launch fails at `ConnectAndValidateAsync` before any `tab.create` |
| R8 | both up; child exits (quit/crash) | ok | ok | no | dead | liveness sweep `ProcessVanished` (sidecar now deleted, §4) or pump `pane_exited` → bar → `PaneClosed` — whichever wins | Failed → resume into a new pane; the old pane (a bare shell) lingers |
| R9 | both up; herdr restarts | drops | — | — | — | pump reconnect backoff (1→30 s); on reconnect the baseline sweep runs the bar per pane → R2 or R3 per session. While down: `/input` → 503, queue defers, **no kill** | as R6 while down, then as R2/R3 |
| R10 | both up; operator closes the pane by hand | ok | err | — | dead (herdr killed it) or alive | bar → `PaneClosed` (or R3's orphan arm if alive) | Failed → supervision **brings it back** in a new pane. Documented; the exit is Stop/suspend |
| R11 | both up; `agent_status == blocked` | ok | ok | yes | alive | nothing (not an exit) | queue `Nothing`; `HerdrStatusDisagreement` at 10 min, alert if channel-bound (CARD-0162) — unchanged |
| R12 | both up; delivery fails (`NoTranscriptRecord`, not working) on an AlwaysOn agent | ok | ok | yes | alive | `KillAsync` → `pane.close` (or §4's own-child kill if the pane has foreign processes) | Stopped/Failed → supervisor restart into a fresh composer — identical to the pty lane |
| R13 | both up; a herdr `pane_closed` **replay** on reconnect for a healthy pane | ok | ok | yes | alive | bar passes → nothing (CARD-0162 E5 guard) — unchanged | nothing |

Two rules the matrix encodes, stated once: **nothing decides "dead" from "I could not ask"**
(R6/R9 wait; only the OS column may end the wait, and only with "dead"), and **the only kills are
of our own named child** (R3/R5/R12 and §4), never of a pane, tab or process we cannot tie to a
sidecar by pid and start time.

**Pty-lane parity check.** R1/R2/R7/R8/R12 are the pty-host rows with `HostPid` replaced by the
socket+pane pair. R3/R5 have no pty analogue (a ConPTY child cannot outlive its host). R6/R9 have
a weaker pty analogue — a host that is alive but whose pipe does not answer is treated as dead
after `AdoptAsync` throws (`SessionRunnerRuntime.cs:428`, then `KillPidBestEffort`) — and this
design deliberately does **not** transpose that: the pty case has a local pid it can kill on
positive identity; the herdr case's local pid is alive and healthy, and the thing that is not
answering is a third-party server.

## 6. Pending adoption and the server's view (decisions 6, 7, 8)

**Runner.** A new `RunnerSession.CreatePendingHerdr(sidecar, …)` registers `Starting` +
`Adopted = true` + `_pendingReason = "HerdrUnreachable"` with the sidecar retained.
`SweepVanishedSessions` gains a herdr arm: for each pending session, run the §5 bar (R1 adopt in
place — the session object upgrades itself to Running and publishes `SessionAdopted`; R2–R5 and R7
publish `SessionExited`); for each Running herdr session, the existing OS-pid check.
`WriteAsync` on a pending session throws `HerdrBackendUnavailableException` immediately rather
than waiting on `_clientReady` (the wait would hold the queue's per-session lock for the HTTP
timeout). `/input`, `/kill`, `/resize`, `/snapshot` map `HerdrBackendUnavailableException` to
**503** with problem-type `herdr_unreachable` (Program.cs endpoint filter). `/kill` on a pending
session: operator intent → delete the sidecar, kill the child by pid if OS-alive (positive
identity), `Exited(PaneLeftOpen)` — the pane, if it still exists when herdr returns, is not ours to
close blind.

**DTO** (`RunnerSessionDto`, additive with defaults so an older server ignores them):
`string? Backend = null` ("pty-host" | "herdr"), `string? Pending = null`,
`DateTime? HerdrVerifiedAtUtc = null`. The single-session GET stamps `HerdrVerifiedAtUtc` after a
passing `VerifyHerdrLivenessAsync`; the list endpoint stays cheap (no herdr calls) and reports the
last stamp.

**Server queue.** `SessionMessageQueueService`: a 503 `herdr_unreachable` from the runner client
is a new `DeliveryVerdict.BackendUnreachable`, handled at the same point as the `blocked` gate
(`:826`) → `FlushResult.Nothing`, no attempt charged, nothing parked; the AlwaysOn kill guard
(`:2336`) adds `verdict is not BackendUnreachable`; Mode:Now returns the same 409 shape as
`blocked` (`:178`) with the herdr-unreachable text. The confirm loop's re-Enter is withheld on the
same condition as `blocked` (`:1733`).

**Server reconciliation.** Pass 3 (`TryReAdoptAsync`): when `runnerSession.Backend == "herdr"`,
the probe is the single-session GET and the evidence is `HerdrVerifiedAtUtc >= sweepStart`;
absent or stale → `ReAdoptProbeFailed` (Error, left alone). Pass 1 is unchanged (a `Pending`
session is `Starting`, not `Exited`, not unknown). New pass 1b: any runner session with
`Pending != null` for longer than `SessionReconciliationSettings.HerdrPendingAlertMinutes` (5)
raises `HerdrUnreachable` (kind 37) — Warning, **Critical when channel-bound** (reuse
`ApiErrorRecoveryService.IsChannelBoundAsync`'s query), dedup key `herdr:pending:{sessionId}`,
never re-raised inside the window, cleared by nothing (the incident is a timeline row; the alert
dedupes). The census arm (`:589`) treats a `Pending` session as neither unclaimed nor surplus.

## 7. What is deliberately not built

- **Restored-pane reuse** (re-`agent.start` into the pane herdr recreated). Needs: a tombstone of
  the last sidecar per session (the sidecar is deleted at the verdict), proof that
  `antiphon-session` metadata tokens survive a herdr restart (P5 left this unverified), and P7's
  answer on what a restored pane's `process_info` looks like. Without those it is a guess about
  which of the operator's panes is "ours". A later card; the allocator keeps refilling gaps, so
  the cost of not building it is one bare-shell pane per herdr restart per agent.
- **Adopting a hand-made pane** as an agent's session — CARD-0183 closed this as not carded.
- **Grok/Codex** — CARD-0187.
- **A herdr-specific supervisor ladder.** The uniform ladder (5 s doubling, hourly Warning, daily
  Critical) applies; herdr-down is a `StartFailure` like any other. A channel-bound agent whose
  herdr is down gets its Critical from `HerdrUnreachable` (R6) or from the daily tier, whichever
  comes first — the former in practice.
- **Closing anything herdr-side we did not open.** No `tab.close` (P3), no `pane.close` on a pane
  with foreign foreground processes (§4), no kill of any pid we cannot name from a sidecar.

## 8. Probes the build needs (run in a dedicated window — they restart the operator's herdr)

| Probe | Question | Decides |
|---|---|---|
| **P7** | Restart herdr with an Antiphon-launched Claude in a pane. Is the Claude process alive afterwards (OS)? Does `pane.get` for the old pane id answer? What does `pane.process_info` list on the restored pane? | Whether R3/R5 (orphan) ever occur in practice, and whether R2's "pane exists, pid not listed" is the only restart shape. If Claude always dies with its pty, R3/R5 stay as defensive arms with a unit test each and no live case |
| **P8** | `pane.close` on a pane whose foreground process is a hand-started `pwsh` — does herdr refuse, succeed, or kill the process? | Whether §4's refusal path is reachable through herdr at all, or only through our own guard |
| **P5b** | Do `workspace`/`pane` metadata tokens survive a herdr restart? | Gates §7's reuse card, not this one |

P7 is run by S2's delegate before writing `HerdrAdoptionSweepTests`' orphan cases, so those tests
pin a measured shape and not a hypothesis. Results go to `.antiphon/card-0186-probe-results.md`
and are inlined into S2's build report.

## 9. Slices, order, and verification

| Slice | Scope | Tests | Band |
|---|---|---|---|
| **S1 — gates, client, docs** | §3 in full; decision 3's resume restamp; doc edits | `AgentSessionBackendTests` flips + 2 new; client `AgentSettingsModal` test that AlwaysOn no longer disables or remaps the control | ½ day |
| **S2 — runner hygiene + adoption bar + DTO** | §4; §5 OS-pid axis in `AdoptHerdrSessionsAsync` and `VerifyHerdrLivenessAsync`; orphan arm; DTO fields; `HerdrExitReasons` + `AgentExitReason` members; P7/P8 | **New `HerdrAdoptionSweepTests`** (runner, `FakeHerdrServer` + a fake `IProcessLivenessProbe`) — one test per matrix row R1–R5, R7, R8, R13 (today there is **no** test of `AdoptHerdrSessionsAsync` at all — `grep AdoptHerdr tests/` is empty); `HerdrPaneChildKillTests` for the foreign-process arm; server `CloseSessionOnExitAsync` negative cases | 1–1½ days |
| **S3 — pending adoption + deferral** | §6 runner (`CreatePendingHerdr`, sweep arm, 503 mapping, `/kill` on pending); §6 queue (`BackendUnreachable`, no-kill guard, Mode:Now 409); `HerdrUnreachable` incident + settings | Runner: R6 and R9 rows (unreachable → pending → reconnect → adopt / exited); `SessionMessageQueueDeliveryVerificationTests` — unreachable defers, charges nothing, never kills an AlwaysOn session; `SessionReconciliationServiceTests` — pending row is not failed, incident after threshold, Critical when channel-bound | 1 day |
| **S4 — reconciliation probe + integration smoke** | §6 pass 3 herdr evidence; census exclusion; one integration test through `DirectSessionRunnerClient` + `FakeHerdrServer`: AlwaysOn herdr agent → runner restart adopts (R1) → fake herdr "restart" empties the pane → no false adopt → supervisor tick resumes into a new pane → channel message delivers and the reply dispatches | `SessionReconciliationServiceTests` herdr re-adopt on/off evidence; `AgentSupervisionTests`-style integration (`[NotInParallel]` no key — it drives a global sweep) | 1 day |

S1 alone is not a deployable state (decision 14). S2 and S3 could be merged into one delegate if
the caller prefers fewer hand-offs; S4 depends on S2's DTO and on S3's `Pending` semantics.
Suggested dispatch: S1 now (Grok, shared tree is fine — it touches nothing another worker is on),
S2 with the probes to a delegate that has the herdr window, S3+S4 after.

## 10. Card acceptance → where it lands

| Card checklist item | Slice |
|---|---|
| Create/PATCH/start an AlwaysOn ClaudeCode agent with `SessionBackend=Herdr` succeeds | S1 |
| Bind a chat channel to a herdr agent; inbound delivers through the queue; reply path works | S1 (gate) + S4 (smoke); no new delivery code (decision 13) |
| Supervisor restarts a dead herdr child into a live pane without a Failed row + leaked pane and without killing unrelated panes | §0 mechanism + S2 (R2/R3/R8 verdicts, own-child-only kills) + S3 (no relaunch storm while herdr is down) |
| After session-runner restart: re-adopt when pane+pid live; after herdr restart: no false Running adopt; AlwaysOn eventually recovers | S2 (R1, R2, R7) + S3 (R6, R9) |
| Existing PtyHost AlwaysOn + channel agents unchanged | every change is under `SessionBackend == Herdr` / `Backend == "herdr"`; S4's parametrised tests run the pty arm too |
| Tests pin the lifted gates and the restart/adopt matrix (unit + ≥1 integration) | S1 (gates), S2/S3 (matrix, unit), S4 (integration) |

## 11. Constraint-by-constraint

1. *Herdr restart ≠ pty-host restart.* Defined: restart = new pane + `--resume`; a restored-empty
   pane is a verdict (R2), never adopted, never reused, never closed. No thrash: the launch fails
   closed while herdr is down (R7), and nothing is killed on "could not ask" (R6/R9).
2. *Channel-bound Critical paths.* Unchanged by construction (decision 13) and pinned; two new
   herdr incidents (`HerdrUnreachable` 37 Critical-when-bound, `HerdrPaneLeftOpen` 38 Warning) are
   the only additions.
3. *No silent remap.* Server never had one; the client's is removed; a resume now follows the
   operator's PATCH (decision 3, overridable).
4. *Reconciliation.* Re-adopt on herdr-specific positive evidence only; pending sessions are
   neither failed nor counted unclaimed; the Stopped arm's re-issued kill goes through §4's
   own-child bar; no arm ever closes a pane or kills a pid it cannot name.
