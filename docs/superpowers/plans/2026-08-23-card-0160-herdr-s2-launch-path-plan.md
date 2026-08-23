# CARD-0160 — herdr S2 launch path: SessionBackend, workspace/tab mapping, restart reconciliation — plan

**Date:** 2026-08-23 · **Card:** CARD-0160 (`1c8c2b70-c4b4-485f-8511-ff714f42ecda`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `feat/card-task-67765703` @ `a321e26`. Every file:line below was re-read out of
the code on that commit; every herdr API shape below was re-read out of a **live schema dump** taken
2026-08-23 on this machine (`herdr api schema --json`, herdr 0.8.2, protocol 20, schema_version 1 —
matching S1's measured values).

**Established facts, not re-derived here:**
- The Investigate stage (task `a5e97dc7`, Grok, read-only, findings on the card 2026-08-23): the
  three launch funnels converge on `adapter.StartAsync` → `POST /sessions` →
  `SessionRunnerRuntime.StartAsync` → always `PtyHostLauncher`; `ANTIPHON_PTY_BACKEND` is the WRONG
  seam (process-wide by documented invariant, `PtyBackendPolicy.cs:45-51`); `HerdrClient` is
  transport-only; Layer A/B reconciliation shapes and their evidence bars; the
  ReplyStyle/ModelLevel DTO convention as the model to copy.
- The S1 spike (`docs/investigations/2026-08-21-herdr-s1-spike-CARD-0120.md`, GO): named pipe
  round-trips 200/200; a herdr-launched Claude writes the normal cwd-keyed JSONL (C1–C4 unchanged);
  `pane.send_text` + bracketed paste + separate enter delivered 86 400 B intact with the Enter-only
  retry contract holding; herdr's own `agent.prompt` state-wait is NOT a delivery verdict.
- The source plan (`docs/superpowers/plans/2026-08-21-card-0111-herdr-investigation-plan.md`) §3
  mapping, §5 slice boundaries (S3 delivery adapter and S4 state mirror are OUT of this card), §6
  hard constraints (opt-in parallel lane; operator-run herdr; herdr state never a kill authority;
  always-on/channel-bound stay on pty-hosts).

**Related:** CARD-0111 (investigation), CARD-0120 (S1), CARD-0112 (capability-advertising
precedent this plan reuses twice), CARD-0006 (C1–C4 — untouched), CARD-0056 (re-adopt evidence
bar — the model for the herdr arm), CARD-0055 (delivery verdicts — S3, explicitly not here),
CARD-0037 (measured-ceiling precedent — S3), CARD-0115 (PoolProjectId — a cwd edge below).

---

## Verdict up front — the six decisions

1. **Branch point: a new optional field pair on `RunnerLaunchRequest` (`Backend` +
   `HerdrLaunchOptions`), branched inside the runner, capability-gated from the server.** No new
   runner endpoint, no server-side HerdrClient. §2.
2. **Workspace/tab/pane ids persist in a runner-side sidecar (`HerdrPaneSidecar`), NOT in new
   `AgentSession` columns.** The only process that can use those ids is the runner; the server's
   reconciliation evidence keeps flowing through the runner's existing HTTP surface. The DB gains
   exactly one herdr-related column: the `SessionBackend` snapshot. §5.
3. **Workspace cwd = project root; pane cwd = the session's own cwd, always explicitly set at pane
   creation.** They differ on card/delegate launches and both are first-class in herdr's API, so
   neither "wins" — the workspace is the container, the pane is the terminal. §4.
4. **Quad grouping: within the session's workspace, fill the lowest-numbered Antiphon-created tab
   that has a free slot (< 4 live Antiphon panes); none free → `tab.create`. A stopped agent's pane
   is closed, the gap is left (no reflow), and the freed slot is refilled by the next launch.** §4.
5. **Antiphon's own `--resume` path owns re-populating after a herdr restart — confirmed, not
   overridden.** A herdr-restored pane is furniture: fresh shell, stale-or-absent env (per the live
   schema, `agent.start` cannot set env and `ANTIPHON_TASK_TOKEN` is re-minted per launch), no
   `--resume`. The runner marks the session Exited on positive evidence and the existing
   dead-mid-turn machinery (`SessionRestartBoundary` + resume auto-continue,
   `AgentSessionService.cs:373` / `:398`) does the rest on the next launch. §6.
6. **Refusal at THREE points: agent create/PATCH (both directions), channel-bind time, and a final
   launch-time guard.** Also refused: any `AgentKind` other than `ClaudeCode` (S1 proved only
   claude). §7.

Plus one decision the card didn't number but the live schema forces: **the child is launched with
`agent.start {name, kind, pane_id, args}` into a pane we created with the launch's env and cwd** —
`agent.start` has `args` but NO `env` and NO exe parameter (live schema, `AgentStartParams`), so
env must ride on pane creation (`TabCreateParams.env` / `PaneSplitParams.env`, both confirmed) and
the exe is resolved by herdr's own agent manifest. TUI-profile exe pinning therefore does not carry
into the herdr lane; the runner logs a Warning when `spec.Exe` isn't the stock claude launcher, and
operators who need a pinned exe keep the pty-host lane. §4.

---

## 1. The new dimension: `SessionBackend`

A NEW enum, a NEW per-agent column, a NEW per-session snapshot — copying the
`AgentReplyStyle`/`AgentModelLevel` convention exactly, and never touching `PtyBackend`
(`src/Antiphon.Agents.Pty/PtyBackend.cs` stays `InboxConhost | ModernConPty`; its process-wide
invariant at `PtyBackendPolicy.cs:45-51` is the reason this dimension exists at all).

- **Enum** `server/Domain/Enums/SessionBackend.cs`:
  ```csharp
  public enum SessionBackend { PtyHost = 0, Herdr = 1 }
  ```
  `PtyHost = 0` so the int-column default and every pre-existing row mean the pty-host lane. Wire
  JSON is string-enum (`JsonStringEnumConverter`, `allowIntegerValues: false` — `Program.cs`
  registration, same as ReplyStyle).
- **`Agent.SessionBackend`** (`Agent.cs`, next to `ReplyStyle` at `:50`): default `PtyHost`. Doc
  comment states the hard constraint (opt-in visibility lane; AlwaysOn/channel-bound refused; herdr
  restart survival is weaker than pty-hosts, by design).
- **`AgentSession.SessionBackend`**: snapshot stamped at session-row creation from the agent (same
  rationale as `AgentSession.AgentKind` at `AgentSession.cs:16` — reconciliation and relaunch must
  know how THIS session was launched even if the agent setting changes later). Three creation
  sites: `AgentControlService.StartInteractiveSessionAsync`, `AgentTaskDispatcher` (`:1589`
  region), and the card spawn's session creation in `AgentSessionService.StartAsync`.
- **Migration** `AddSessionBackend`: `Agents.SessionBackend int not null default 0`,
  `AgentSessions.SessionBackend int not null default 0`. No backfill needed — 0 is the truth for
  every existing row.
- **DTOs** (`AgentDtos.cs`):
  - `CreateAgentRequest`: `SessionBackend? SessionBackend = null` — null = PtyHost (the
    `ModelLevel? → High` pattern at `:184`).
  - `UpdateAgentRequest`: `SessionBackend? SessionBackend = null` — **null = leave unchanged**
    (the `:219-230` contract; an older caller must not silently reset a chosen backend).
  - `AgentSummaryDto` + `AgentDetailDto`: `SessionBackend SessionBackend = SessionBackend.PtyHost`.
- **Apply sites** (`AgentService.cs`): create mapping next to `:262-264`; update mapping next to
  `:358-361` (`if (request.SessionBackend is { } backend) …` AFTER the validation in §7).
- **UI** (`client/src/features/agents/AgentSettingsModal.tsx`): a `SegmentedControl` mirroring the
  Reply-style one at `:330-343` — label "Session backend", options `Pty host` / `Herdr`, with a
  description line ("Herdr: session runs in a pane of the operator's herdr instance — visible and
  natively attachable, but it does not survive a herdr restart; not available for always-on or
  channel-bound agents"). Disabled with that reason when the modal's AlwaysOn switch is on.
  `client/src/api/agents.ts`: `sessionBackend?: 'PtyHost' | 'Herdr'` on the summary/detail types
  and both request types (`?? 'PtyHost'` on read, the `:114` older-server pattern).

**Deliberately NOT in S2:** a per-launch override (per-agent only — one knob), a delegate.ps1
flag, and any `Project.DefaultLaunchEnvJson` coupling (not shipped — investigation confirmed).

## 2. Wire contract and branch point (decision 1)

**Server → runner.** `RunnerLaunchRequest` (`SessionRunnerContracts.cs:3-17`) gains two additive
fields, following the `TranscriptFormat` precedent (`:15-17`) where null preserves the old meaning:

```csharp
// Which lane hosts the child. Null means pty-host — the only lane that existed before this
// field, so an old server's requests keep their meaning. See SessionBackends.
string? Backend = null,
// Herdr-lane placement context, resolved by the server (the runner has no DB access).
// Required when Backend == SessionBackends.Herdr; ignored otherwise.
HerdrLaunchOptions? Herdr = null);

public static class SessionBackends
{
    public const string PtyHost = "pty-host";
    public const string Herdr = "herdr";
}

public sealed record HerdrLaunchOptions(
    // Stable grouping key for workspace-per-project: "project:<guid>", or "none" when the
    // session resolves to no project (see §4). The runner treats it as an opaque key.
    string WorkspaceKey,
    // workspace.create label — project name (or "Antiphon" for the catch-all).
    string WorkspaceLabel,
    // workspace.create cwd — project.LocalRepositoryPath; null for the catch-all workspace.
    string? WorkspaceCwd,
    // pane.rename label — the agent/definition name the operator should see on the pane.
    string PaneTitle);
```

**Why a field and not a new endpoint or a server-side HerdrClient:** confirmed by investigation —
`HerdrClient` lives only in the runner process (runner `Program.cs:35-38`), the server holds no
herdr transport, and `SessionRunnerRuntime.StartAsync` (`SessionRunnerRuntime.cs:48`) is the single
choke point all three launch funnels already pass through. A second endpoint would fork every
downstream consumer (input, buffer, kill, list) that must keep working uniformly per session id; a
server-side client would put a second process on herdr's pipe and give the server a dependency it
never needs (§5).

**Old-runner hazard and the capability gate.** An old runner deserializing this request would
IGNORE the unknown fields and launch a pty-host silently — the exact stale-capability shape
CARD-0112 exists for. So: `RunnerCapabilitiesDto` (`SessionRunnerContracts.cs:510-519`) gains
`IReadOnlyList<string>? SessionBackends = null` (null = older runner = **no evidence**, same
contract as `TranscriptFormats` at `:515-517`), derived in the runner from its actual dispatch
surface, and `SessionRunnerHttpClient.StartAsync` refuses a herdr launch before POSTing unless the
runner's capabilities contain `"herdr"` — mirroring `GetTranscriptCapabilityMismatchAsync` at
`SessionRunnerHttpClient.cs:50-51` (throw `RunnerCapabilityMismatchException`, loud, no fallback to
pty-host — the CARD-0111 §6 "never silently remap" rule).

**Server-side plumbing.** `AgentLaunchSpec` (`server/Application/Dtos/AgentLaunchSpec.cs:33-43`)
gains `SessionBackend Backend = SessionBackend.PtyHost` and `HerdrLaunchOptions? Herdr = null`.
`AgentSessionService.BuildRuntimeLaunchSpec` (`:1044`) reads the SESSION's snapshot (never the
agent's live value — the snapshot is what makes resume-after-PATCH deterministic) and, for Herdr,
populates `HerdrLaunchOptions` via a small `HerdrLaunchContextResolver`:

- Card session → `Card.Board.ProjectId` → Project.
- Delegate session → the task's project scope (worktree's project / `PoolProjectId` for pool
  delegates, per CARD-0115).
- Interactive standing agent → `Agent.BoardId → Board.ProjectId`.
- Nothing resolvable (e.g. `PoolProjectId == null`) → the catch-all (`WorkspaceKey = "none"`,
  label "Antiphon", cwd null — `WorkspaceCreateParams.cwd` is nullable in the live schema).

**Runner-side branch.** In `SessionRunnerRuntime.StartAsync`, validation adds: unknown `Backend`
value → throw (listing supported, the `UnsupportedTranscriptFormatException` shape);
`Backend == herdr` with `Herdr == null` → throw. The branch itself lives where the pty coupling
already lives — `RunnerSession.StartAsync` (`SessionRunnerRuntime.cs:478`) currently inlines
`launcher.LaunchDetachedAsync` + `PtyHostClient.ConnectAsync` + `LaunchAsync` (`:487-523`). Extract
that block behind an internal seam:

```csharp
internal interface ISessionChild : IAsyncDisposable
{
    Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct);
    Task WriteAsync(string input, CancellationToken ct);   // feeds SessionInputLog in the caller
    Task ResizeAsync(int cols, int rows, CancellationToken ct);
    Task<bool> KillAsync(CancellationToken ct);
    Task<ChildScreen?> ReadScreenAsync(CancellationToken ct); // null => push-driven (pty)
    event Action<ChildExit> Exited;
}
```

`PtyHostChild` is the current code moved verbatim (push output events keep feeding `_liveBuffer` /
`TerminalScreen` / the ansi log exactly as today). `HerdrPaneChild` implements it over the typed
`HerdrClient` wrappers (§8) + the allocator (§4) + the sidecar (§5). Everything ABOVE the seam —
transcript tailer selection and C1–C4 binding, `TranscriptSidecar`, `SessionInputLog`, claim
registry, event hub — is untouched by construction: S1 proved the herdr-launched child writes the
same cwd-keyed JSONL. `ChildStartUtc` for the transcript sidecar's C3 epoch is the runner's own
UTC clock at the moment `agent.start` succeeds (the live schema's `pane.process_info` reports pids
but no process start time; a launch-time stamp is conservative — the transcript can only be created
after it — and C3 only needs "not older than the child").

**Herdr-lane semantics of the request fields (stated, not fudged):**
- `Exe` — advisory only (Warning-logged on mismatch; herdr's agent manifest resolves the command).
- `Args` — passed to `agent.start.args` verbatim (`--session-id`, `--resume`, `--model`,
  `--append-system-prompt` all ride through; S1's spike verified the session id lands).
- `Env` — set on the PANE at creation (tab/split `env`), so the child inherits it.
- `Cwd` — set on the PANE at creation; the workspace keeps the project root (§4).
- `Cols/Rows/MemoryLimitMb` — not applicable (herdr owns layout; the Job-object cap is pty-host
  machinery). Non-default values log a Warning and are ignored; `ResizeAsync` is a logged no-op.
- `ExitCode` — always null for herdr sessions (no API surface for it); `ExitReason` carries a new
  `HerdrPaneClosed` / `HerdrRestartPresumedDead` value instead.

**Input passthrough (S2 scope only).** `WriteAsync` maps a write that is exactly `"\r"` or `"\n"`
to `pane.send_keys ["enter"]` and everything else to `pane.send_text` — the exact pair S1 measured
delivering 86 400 B intact with Enter-only retries. This is a transparent transport; every
CARD-0055/0024 verdict stays where it is (server-side, transcript rows). The delivery ADAPTER —
ceilings, `PtyDeliveryProfile` herdr arm, `agent_blocked` mapping — is S3 and none of it lands
here. Screen reads (`GetBuffer`/`GetSnapshot`) are served on demand from `pane.read` (source
`visible` for the rendered screen, `recent` for buffer; `PaneReadResult.revision` maps onto
`LastSequence`, so sequence-advance-style checks keep meaning something). Consequence, accepted:
**no push output stream in S2** — the web `SessionTerminal` gets polled snapshots, not live
streaming; the operator's live view is herdr itself, which is the entire point of the lane.

## 3. What launches where — funnels unchanged

No funnel forks. Interactive (`AgentControlService.StartAsync`, `AgentControlService.cs:85-145`),
card (`CardService.SpawnAsync`), and delegate (`AgentTaskDispatcher`) all keep converging on
`AgentSessionService` → adapter → `POST /sessions`; the only new inputs are the snapshot column and
the `HerdrLaunchOptions` resolution in §2. Boot prompts (`VerifiedPromptSubmitter`), remote-control
arming, launch notes, and the queue flush at `AgentSessionService.cs:386-406` all run unchanged over
the input passthrough — their evidence sources (screen reads, transcript rows) both exist for herdr
sessions.

## 4. The herdr mapping: workspace, quad tab, cwd (decisions 3 & 4)

All shapes below are live-schema-confirmed (protocol 20): `workspace.create {cwd?, env, label?,
focus}`, `workspace.report_metadata {workspace_id, source, tokens (≤16, keys
`^[A-Za-z0-9_-]{1,32}$`, values string|null), ttl_ms?, seq?}`, `tab.create {workspace_id?, cwd?,
env, label?, focus}`, `pane.split {direction: right|down, ratio?, target_pane_id?, workspace_id?,
cwd?, env, focus}`, `pane.rename {pane_id, label?}`, `pane.report_metadata {pane_id, source,
tokens, title?, …}`, `pane.report_agent_session {pane_id, source, agent, agent_session_id?,
agent_session_path?}`, `agent.start {name, kind, pane_id, args[], timeout_ms?}`,
`pane.process_info {pane_id?} → {shell_pid?, foreground_processes[{pid, name, argv?, cwd?}], tty?}`,
`pane.read {pane_id, source, format, strip_ansi, lines?} → {text, revision, truncated}`,
`pane.close`, `tab.close`, `workspace.list`, `pane.list {workspace_id?}`, `pane.get`,
`session.snapshot`.

**Workspace ensure (per project).** On a herdr launch the runner ensures a workspace for
`WorkspaceKey`: `workspace.list`, match on our own metadata token (`WorkspaceInfo.tokens` carries
reported metadata; token `antiphon-ws = <WorkspaceKey>`), fall back to label+cwd match; none →
`workspace.create {cwd: WorkspaceCwd, label: WorkspaceLabel}` then
`workspace.report_metadata {source: "antiphon", tokens: {antiphon-ws: <WorkspaceKey>}}`.
**Metadata tokens are best-effort identity, never load-bearing:** `ttl_ms` is capped at 24 h in the
schema and token survival across a herdr restart is unverified, so the AUTHORITATIVE record of
"which workspace/tab/pane is session X" is our sidecar (§5); tokens exist so a re-find after loss
has a strong signal and so a human browsing herdr can see whose furniture is whose. The ensure
re-reports tokens on every launch (refreshing any TTL).

**Cwd (decision 3).** `workspace.create` gets `cwd = project.LocalRepositoryPath` — the workspace
is "the top-level project container" (herdr's own docs) and the operator's mental unit, exactly the
investigation-plan §3 mapping. The PANE always gets `cwd = request.Cwd` (the session's real cwd:
worktree for card/delegate launches, `Agent.WorkingDirectory` for standing agents, pool path for
pool delegates) passed explicitly on `tab.create`/`pane.split`. Neither "wins" because herdr never
makes a pane inherit the workspace cwd when the pane's own is given — the workspace cwd is only a
default for panes created WITHOUT one, which Antiphon never does. Same rule for env: the launch's
env dictionary goes on every pane creation call.

**Quad allocation (decision 4).** A pure, deterministic allocator (`HerdrPaneAllocator`, runner):

1. Collect the workspace's live Antiphon panes: every `HerdrPaneSidecar` whose session is still
   live in this runner AND whose `WorkspaceKey` matches, verified against `pane.list` (a sidecar
   whose pane no longer exists doesn't count — and gets cleaned up, below).
2. Group by `TabId`. Pick the **lowest-numbered tab with < 4 live Antiphon panes** (tab number from
   `tab.get`; ties impossible). Tabs not created by Antiphon (no Antiphon pane in them) are never
   split into — the operator's own tabs are not our furniture.
3. No tab with a free slot (0 live tabs, or all full — the "5th agent" case) →
   `tab.create {workspace_id, cwd, env, label: PaneTitle}`; its initial pane is pane #1.
4. Slot free in an existing tab → `pane.split` against a live Antiphon pane in that tab, chosen to
   converge on a 2×2: pane count 1 → split it `right` at 0.5; count 2 → split the FIRST pane
   `down` at 0.5; count 3 → split the remaining un-split pane `down` at 0.5. After a gap-refill the
   same rule applies against whatever panes remain (the grid self-heals toward 2×2 rather than
   reproducing exact positions — positions are cosmetic, the cap of 4 is the contract).
5. `pane.rename {pane_id, label: PaneTitle}` +
   `pane.report_metadata {source: "antiphon", tokens: {antiphon-session: <sessionId>}, title}`.
6. `agent.start {name: PaneTitle, kind: "claude", pane_id, args}` (the kind string is verified at
   build time against `server.agent_manifests` — probe item P4, §9).
7. Once the transcript binds (the existing `RecordTranscriptBinding` hook),
   `pane.report_agent_session {pane_id, source: "antiphon", agent: "claude", agent_session_id,
   agent_session_path}` — pushing OUR authoritative binding into herdr's sidebar. One-way,
   informational; nothing reads it back.

**On stop (kill or exit):** the runner `pane.close`s the session's pane — it is recorded in our
sidecar, i.e. our own furniture, and the guard is that `pane.process_info` must show no foreground
process other than the (dead or shell) remains of our child; anything unexpected → leave the pane,
log, alert. The gap in the 2×2 is **left as a gap** — no reflow: reflowing moves the operator's
visual context underneath them (the lane exists FOR the operator's eyes), and herdr reflows the
split geometry itself when a pane closes. The freed slot is simply the next launch's step-2 answer.
A tab whose last Antiphon pane closes: `tab.close` if the tab is then empty; if herdr auto-removes
empty tabs (probe item P3) the call is skipped.

**What shares a tab, concretely:** live herdr-backed agents of the same project, in launch order,
four to a tab, refilling gaps before opening new tabs. No role/kind-based grouping in S2 — the dumb
rule is predictable, testable with pure unit tests, and matches "tab per 4 agents" without a
scheduler.

## 5. Persistence: `HerdrPaneSidecar`, not DB columns (decision 2)

New runner-side sidecar at `<SessionLogPath>/herdr/<sessionId:N>.json`, exactly the
`TranscriptSidecar` pattern (`TranscriptSidecar.cs:19-99`: atomic temp+rename save, tolerant
`TryLoad`, `LoadAll` sweep):

```csharp
public sealed record HerdrPaneSidecar
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid SessionId { get; init; }
    public required string WorkspaceKey { get; init; }
    public required string WorkspaceId { get; init; }
    public required string TabId { get; init; }
    public required string PaneId { get; init; }
    public int? ChildPid { get; init; }          // claude's pid, from pane.process_info after start
    public int? ShellPid { get; init; }          // the pane's shell, if herdr wraps one
    public DateTime LaunchedAtUtc { get; init; } // C3 epoch and staleness judge
    public string? Cwd { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
```

**Why sidecar and not `AgentSession` columns — the tension, resolved explicitly.** The card asked
this to be justified against how reconciliation reads the data back. Both reconciliation layers
were traced:

- **Layer A (runner adoption after a RUNNER restart)** is the layer that actually dereferences
  pane/workspace ids — and it runs before the runner's HTTP API is even listening (`Program.cs`
  adoption-before-listen), so it structurally cannot read the server's DB. It needs a local file.
  This is the same reason `TranscriptSidecar` and `PtyHostManifest` are files.
- **Layer B (server `SessionReconciliationService`)** never talks to herdr — the server has no
  herdr client (decision 1) — so DB-resident pane ids would have **no reader**. Its evidence bar
  (`TryReAdoptAsync`, `SessionReconciliationService.cs:299-402`: a named process AND a buffer probe
  answering) keeps being served BY THE RUNNER: for a herdr session the runner reports
  `RunnerSessionDto.Pid` = the child pid from `pane.process_info` (HostPid stays null — there is no
  host), and `GetBufferAsync` is answered from `pane.read`. Layer B's code does not change at all;
  its probes just reach different plumbing behind the same runner endpoints. That is the §6 mapping
  of "Pid/HostPid + buffer probe" onto herdr: **child pid via `pane.process_info` + a herdr
  `pane.read` actually answering**, with the runner as the translator.

A second copy in the DB would be a copy with no consumer, maintained across two processes, drifting
from the only authoritative writer. The DB stores the one herdr fact the server genuinely consumes:
`AgentSession.SessionBackend` (refusals, UI, and knowing that a vanished session of this kind means
"herdr restart" rather than "pty-host died").

## 6. Reconciliation and the herdr restart (decision 5)

Three scenarios, three arms — none of which ever uses herdr's `idle|working|blocked` agent state as
evidence for anything (hard constraint; that state is S4 corroboration only).

**A. Runner restarts, herdr keeps running (the common case — `restart-session-runner.ps1`).**
Herdr panes belong to herdr's server, not the runner, so the sessions genuinely survive. The
adoption sweep (`AdoptOrphanedHostsAsync` region, `SessionRunnerRuntime.cs:270-338`) gains a herdr
arm AFTER `RestoreTranscriptClaims()` (C1 first, unchanged): for each `HerdrPaneSidecar.LoadAll`:

- `pane.get(PaneId)` answers AND `pane.process_info(PaneId)` lists a foreground process whose pid
  == `ChildPid` AND `pane.read` answers → **re-adopt**: rebuild the session in Adopted state,
  re-tail the transcript via the existing `TranscriptSidecar` path (untouched). This is the CARD-0056
  bar transposed: a real named process + a probe that answers; sequence/output advancement
  deliberately NOT required (idle sessions are quiet — the exact false positive CARD-0056 exists to
  prevent).
- `pane.get` fails (unknown pane), OR the pane exists but `ChildPid` is absent from its process
  list (**the restored-but-empty pane** — herdr restarted underneath a running runner, restored
  layout, relaunched a fresh shell) → **register the session Exited from the sidecar** (reason
  `HerdrRestartPresumedDead`), never adopt. This is pin (c): a restored-but-empty pane is
  positively DEAD for our session, because the one fact that made it ours — our child pid — is
  gone, and herdr's own docs say pane processes do not survive its restart. It is never
  false-adopted (the pid test fails) and never silently lost (an Exited registration flows to the
  server exactly like a pty-host exit and the row is closed).
- **Herdr unreachable** (`HerdrBackendUnavailableException`) → **no verdict**: the session is
  registered in an Unknown/unadopted state, retried by the liveness sweep, and alerted — matching
  Layer B's "unreachable runner = skip the sweep, not 'nothing is running'". Unreachable is not
  evidence of death; herdr restarting is precisely a window of unreachability followed by
  answerable-and-empty, and the second half is the evidence.

**B. Herdr restarts while the runner is up.** The runner holds one `events.subscribe` stream
(`HerdrClient.SubscribeEventsAsync`, `HerdrClient.cs:114`) in a background pump consuming ONLY
`pane.closed` in S2 (`pane.agent_status_changed` is S4). Rules the pump inherits from the codebase's
scars: the loop catches `OperationCanceledException` only `when (ct.IsCancellationRequested)`, and
a dropped stream logs + backs off + reconnects forever (the CLAUDE.md Telegram-ingress rule). A
`pane.closed` for a tracked pane → that session Exited (`HerdrPaneClosed`). On every RECONNECT the
pump runs the scenario-A verification over all live herdr sessions — the disconnect window is
exactly when a herdr restart happens, and the reconnect sweep is what converts "my panes' pids are
gone" into Exited registrations promptly rather than at the next runner restart.

**C. Who repopulates (decision 5 — confirmed: Antiphon's `--resume`).** Nothing new is built.
Once the session is Exited/Failed, the EXISTING machinery owns recovery, per session class, same as
a pty-host child dying today: an interactive standing agent's next Start resumes by default
(`AgentControlService.StartAsync` → resume mode), writes the `SessionRestartBoundary` if the
transcript reads mid-turn (`AgentSessionService.cs:373`), and queues the auto-continue on a genuine
resume (`:398`); a card/delegate session's death fails the task through the existing paths (stall
row, checkpoint, re-dispatch — CARD-0153's machinery). AlwaysOn auto-restart never applies because
AlwaysOn agents are refused the lane (§7). Herdr's own restored pane is NEVER re-populated in
place, for three independent reasons: `agent.start` cannot set env and the restored pane's env is
stale (the re-minted `ANTIPHON_TASK_TOKEN` alone disqualifies it); the relaunch must carry
`--resume` args the restored shell knows nothing about; and reusing it would couple correctness to
herdr's restore semantics, which §6 of the source plan forbids trusting. The relaunch flows through
the normal launch path, which allocates a fresh pane via §4 — and the allocator's step-1 cleanup
closes the stale restored pane (sidecar-recorded, process-info-guarded) so workspaces don't
accumulate ghosts.

## 7. Refusals: AlwaysOn / channel-bound / non-Claude (decision 6)

The constraint is MUST-stay-on-pty-hosts, so it is enforced at every write that could create the
forbidden state, plus a launch-time backstop — refusal, never silent remap (CARD-0136's rule):

1. **Agent create/PATCH** (`AgentService`, next to the `:358-361` apply block), covering BOTH
   directions and running before any field is applied, over the REQUEST-resolved final state:
   - `SessionBackend = Herdr` requested while the agent is (or the same request makes it)
     `AlwaysOn` → 422/Conflict: "Always-on agents stay on pty-hosts (herdr sessions do not survive
     a herdr restart)."
   - `AlwaysOn = true` requested while the agent is (or becomes) `Herdr` → same refusal, mirrored.
   - `SessionBackend = Herdr` while any `ChatChannels.AgentId` names the agent (the
     `IsChannelBoundAsync` shape, `ApiErrorRecoveryService.cs:461-471`) → refusal naming the
     channel.
   - `SessionBackend = Herdr` while the agent's `Kind != ClaudeCode` (including a Kind change in
     the same request) → refusal: S1 measured claude only; other kinds are un-spiked.
2. **Channel-bind time**: the write path that sets `ChatChannels.AgentId` refuses when the target
   agent's `SessionBackend == Herdr` ("bind refused: this agent runs in herdr; channel-bound agents
   stay on pty-hosts"). Without this gate a bind after a PATCH would create the forbidden pair with
   neither write ever having been refused.
3. **Launch-time backstop** (`AgentSessionService`, before `adapter.StartAsync`): effective backend
   Herdr AND (owning agent AlwaysOn, or channel-bound, or Kind != ClaudeCode) → `ConflictException`,
   session Failed with that reason. Defense in depth for drift and races (a bind landing between
   PATCH and launch), and the only gate the card-spawn and dispatcher funnels need no special
   casing for.

Plus the two runner-level gates that already exist and stay: `SessionRunner:Herdr:Enabled=false` →
`HerdrBackendUnavailableException` (`HerdrClient.cs:255-260`), and protocol pinning at 20
(`ConnectAndValidateAsync`, `:55-79`) — both loud, neither ever falls back to a pty backend.

## 8. Typed `HerdrClient` wrappers (S2 additions)

On top of the existing generic `SendRequestAsync` (`HerdrClient.cs:82`), one thin typed method per
call S2 makes, each deserializing into a record matching the live response schema and throwing the
existing exception taxonomy (`HerdrApiException` codes surface verbatim). Params exactly as
schema-pinned in §4:

| Wrapper | herdr method | Returns |
|---|---|---|
| `WorkspaceListAsync` | `workspace.list` | `IReadOnlyList<HerdrWorkspaceInfo>` (`workspace_id, label, number, tokens, active_tab_id, pane_count, tab_count`) |
| `WorkspaceCreateAsync(cwd, label)` | `workspace.create` | created workspace info (result shape = probe P1) |
| `WorkspaceReportMetadataAsync(workspaceId, tokens)` | `workspace.report_metadata` | ack |
| `TabCreateAsync(workspaceId, cwd, env, label)` | `tab.create` | tab info + initial pane id (probe P2) |
| `PaneSplitAsync(targetPaneId, direction, ratio, cwd, env)` | `pane.split` | new pane info |
| `PaneRenameAsync(paneId, label)` | `pane.rename` | ack |
| `PaneReportMetadataAsync(paneId, tokens, title)` | `pane.report_metadata` | ack |
| `PaneReportAgentSessionAsync(paneId, agent, agentSessionId, agentSessionPath)` | `pane.report_agent_session` | ack |
| `AgentStartAsync(name, kind, paneId, args, timeoutMs)` | `agent.start` | `HerdrAgentInfo` |
| `PaneGetAsync(paneId)` | `pane.get` | `HerdrPaneInfo` (`pane_id, tab_id, workspace_id, cwd, revision, tokens, agent_session`) |
| `PaneListAsync(workspaceId)` | `pane.list` | list of pane info |
| `PaneProcessInfoAsync(paneId)` | `pane.process_info` | `HerdrPaneProcessInfo` (`shell_pid, foreground_processes[{pid,name,argv,cwd}]`) |
| `PaneReadAsync(paneId, source, stripAnsi, lines)` | `pane.read` | `HerdrPaneReadResult` (`text, revision, truncated`) |
| `PaneSendTextAsync(paneId, text)` / `PaneSendKeysAsync(paneId, keys)` | `pane.send_text` / `pane.send_keys` | ack |
| `PaneCloseAsync(paneId)` / `TabCloseAsync(tabId)` | `pane.close` / `tab.close` | ack |

`source: "antiphon"` is stamped inside the client for every report call. Unknown response fields
are ignored (herdr's documented forward-compat contract). `agent.prompt` is deliberately NOT
wrapped — S3 decides delivery, and S1 already proved its state-wait must not be a verdict.

## 9. Out of scope, and probe items the build must verify live

**Out of scope (S3/S4 or forbidden):** queue delivery via herdr (S3 — until then the passthrough in
§2 carries queue bodies exactly as a pty write would, with all existing verdicts unchanged);
`PtyDeliveryProfile` herdr arm and any ceiling change (S3); `pane.agent_status_changed` consumption
and `pane.report_agent` state pushing (S4); herdr worktree API, plugins, Kitty graphics, `--remote`;
Antiphon spawning/supervising herdr; any change to `PtyBackend`/`PtyBackendPolicy`/measured
ceilings; per-launch backend overrides; delegate.ps1 herdr flag.

**Probe items** (small live checks against the operator's herdr before/while building, S1-style —
each is a response-shape or behavior detail the schema dump cannot settle):
- **P1** `workspace.create` result shape (assumed: contains `workspace_id`).
- **P2** whether `tab.create` creates an initial pane and returns its id (assumed yes; else the
  first pane comes from a follow-up `pane.list`).
- **P3** whether herdr auto-removes a tab whose last pane closes (decides if `TabCloseAsync` is
  ever called).
- **P4** the exact agent-manifest kind string for Claude Code (`server.agent_manifests`; assumed
  `"claude"`).
- **P5** metadata-token behavior with `ttl_ms: null` and across a herdr restart (decides nothing
  load-bearing — sidecars are authoritative either way — but calibrates the re-find logging).
- **P6** that `agent.start` into a pane created by `tab.create`/`pane.split` inherits the pane's
  env (the design's env carrier; S1 launched into a workspace-created pane, same mechanism one
  level up).

## 10. Verification / test design

Pinned tests, named per the card's (a)–(d) plus the gates this plan adds. All server tests in
`Antiphon.Tests` (TUnit, shared-Postgres rules apply — every assertion scoped to rows the test
made); runner tests in `Antiphon.SessionRunner.Tests` against the existing fake herdr named-pipe
server from S1's `HerdrClientTests`, extended to serve the §8 methods from a scripted state model.

**(a) `SessionBackend` is a separate, defaulted dimension — no cross-contamination:**
- `AgentSessionBackendTests` (server): create without the field → `PtyHost`; PATCH with null →
  unchanged; PATCH `Herdr` (non-AlwaysOn, unbound, ClaudeCode) → applied; session rows created by
  the interactive path stamp the agent's value; a PATCH after launch does not change the live
  session's snapshot.
- `PtyBackendContractTests` addition: `PtyBackend` still has exactly `{InboxConhost, ModernConPty}`
  and `PtyBackendPolicy.Resolve` never sees or returns a herdr value; a
  `RunnerLaunchRequest{Backend: "herdr"}` under `ANTIPHON_PTY_BACKEND=modern` never touches
  `PtyHostLauncher`, and `Backend: null` under the same env launches a pty-host exactly as today
  (runner-level, fake herdr + existing direct-runtime harness).
- `SessionRunnerHttpClient` gate test: runner capabilities lacking `"herdr"` (and the null/older
  case) → `RunnerCapabilityMismatchException` before any POST, mirroring the existing transcript
  mismatch test.

**(b) refusals:** `AgentSessionBackendTests` refusal cases — Herdr-on-AlwaysOn and
AlwaysOn-on-Herdr (both orders, one-request combined state included); Herdr while a `ChatChannels`
row names the agent; channel bind onto a Herdr agent; Herdr on `Kind != ClaudeCode`; and the
launch-time backstop (`AgentControlServiceTests`: state drifted to the forbidden pair →
`StartAsync` conflicts, no session row left Starting, no runner call made).

**(c) a herdr-restart-killed pane is dead, routed to resume — never re-adopted, never lost:**
`HerdrAdoptionTests` (runner, fake herdr): (i) sidecar present, fake answers `pane.get` unknown →
session registered **Exited**(`HerdrRestartPresumedDead`), no adoption; (ii) pane exists, fresh
shell, recorded `ChildPid` absent from `process_info` → same Exited, no adoption — the
restored-but-empty trap pinned directly; (iii) pane exists AND `ChildPid` present AND `pane.read`
answers → adopted (the only positive arm); (iv) herdr unreachable → neither adopted nor Exited,
alert raised, retried; (v) the reconnect sweep after a dropped event stream converts a
now-missing-pid session to Exited. Server side, the routing half is by construction (an Exited
herdr session closes exactly like an exited pty session), pinned by one integration test asserting
a Herdr-snapshot session whose runner reports Exited ends Failed/closed and its next interactive
start goes down the existing resume path (`SessionRestartBoundary` written when mid-turn — reusing
the existing resume-recovery test pair's fixtures).

**(d) quad grouping:** `HerdrPaneAllocatorTests` (pure): 0 agents → `tab.create` in the project
workspace; 1→2→3→4 → splits (right 0.5; first-pane down 0.5; remaining down 0.5) in the same tab;
5th → second tab; one-of-4 stops → its pane closed, gap kept (no reflow calls), and the NEXT launch
fills that tab, not a third; two projects → two workspaces; unresolvable project → catch-all
workspace; operator's own tabs never split into.

**Sidecar + launch sequence:** `HerdrRunnerSessionTests` (runner, fake herdr): a herdr launch
issues ensure-workspace → allocate → rename/report → `agent.start` with the request's args, pane
created with the request's env and cwd while the workspace holds the project cwd (decision 3 pinned
here); sidecar written atomically with all five ids; `WriteAsync("\r")` → `send_keys ["enter"]`,
body → `send_text`; `GetBuffer` served from `pane.read`; kill → `pane.close` guarded by
`process_info`; `Herdr:Enabled=false` → the launch fails loudly and the session row is torn down
via the existing failed-launch path (`SessionRunnerRuntime.cs:95-103`).

## 11. Build order

1. **B1 — dimension + refusals (server only, shippable dark):** enum, columns, migration, DTOs,
   apply sites, all three refusal gates, UI. Nothing selects herdr yet.
2. **B2 — typed client:** §8 wrappers + fake-herdr server extensions + probe run (P1–P6), recording
   answers in the build commit message.
3. **B3 — runner lane:** `ISessionChild` extraction (pty behavior-preserving — existing runner
   suite green before proceeding), `HerdrPaneChild`, allocator, sidecar, event pump, adoption arm,
   capabilities advertising.
4. **B4 — wire-through:** `RunnerLaunchRequest`/`AgentLaunchSpec` fields, capability gate,
   `HerdrLaunchContextResolver`, snapshot plumbing, launch backstop.
5. **B5 — pins + docs:** the §10 suites not already landed with their slices, a CLAUDE.md gotcha
   entry (herdr lane: opt-in, ClaudeCode-only, no restart survival, sidecar location, the
   never-adopt-empty-pane rule), and a live end-to-end smoke against the operator's herdr.
