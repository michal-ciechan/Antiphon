# CARD-0461: explicit disposal of a leftover Herdr pane

Plan date: 2026-09-10. Inspected Antiphon HEAD: `dc6af182`.
Card: CARD-0461, `18e4ed92-d598-4e3d-8b8f-f20ebff955b0`, Antiphon board.
Stage: Plan. Runtime implementation and live cleanup are not part of this change.

Add an explicit, pane-addressed inspect/dispose operation independent of session kill.
It can dispose a detached operator pane, an exited session's remaining pane, or an
identified orphan with no database row. Ordinary Stop/kill continues to detach
attached-origin panes. Disposal refuses live bindings, foreign or unproven occupants,
changed targets, and an unavailable backend. A backend operation that checks the
target and closes it under the same safety boundary is a release prerequisite;
the installed protocol exposes only unconditional `pane.close`.

## Evidence and settled behavior

Read the current board descriptions and outcomes for CARD-0461, CARD-0213,
CARD-0224 and CARD-0323, together with their current landed implementations.

| Evidence | Design consequence |
|---|---|
| CARD-0461 records leftover `w2:p2T`, tab `w2:tY`, native ID `d6cf6ef2-1bff-4034-a266-1d7fe1acf3b2`, in operator workspace PredictionMarkets. Runner reported Exited/ProcessVanished/attached; server GET for that session was 404. The separate live orchestrator was `wS:p1`, session prefix `a1cfa760`. The leftover was subsequently closed manually. | This is a historical reproduction, not a current target to close. A database lookup must not gate orphan inspection/disposal, and no lookup by agent label, cwd or slug may redirect disposal to the current orchestrator. |
| `HerdrPaneChild.KillAsync` (`:1066`) dispatches attached origin to `DetachAsync` (`:1130`). Detach clears Antiphon metadata/state labels, deletes its sidecar, and emits Detached without closing or PID-killing. | Keep CARD-0213's attach contract. Do not add a force flag to ordinary kill, change origin to launched, or make detach retain a launchable last-pane record. |
| Nested `SessionRunnerRuntime.RunnerSession.KillAsync` (`:2387`) returns on `HasExited`. `MarkVanishedIfDead` (`:2450` vicinity) retires a sidecar while retaining the in-memory session. `FindBoundPane` (`:400`) omits exited sessions. | New disposal must work without a live `RunnerSession`; exited history is a locator, not proof of current process ownership. |
| `HerdrPaneSidecar.Retire` (`:124`) excludes attached origin and launch-pending records from last-pane. `HerdrLastPane.LastChildPid` explicitly says it is for logging, never identity. | Read last-pane directly for targeted cleanup; never turn historical PIDs into kill authority. Preserve seven-day pruning and normal relaunch behavior. |
| `HerdrPaneChild.ResolveTargetPaneAsync` reuses an idle launched-origin last-pane or adopts an exact argv session; foreign occupancy refuses without allocator fallback. | Closing an explicitly selected leftover is a distinct operator operation, not a new relaunch/adoption arm. |
| `EnsureWorkspaceAsync` (`:1325`) uses token-first, then one untagged exact label, otherwise create. `CollectLiveAntiphonPanes` (`SessionRunnerRuntime.cs:444`) excludes attached panes. | Workspace placement is settled by CARD-0323. Disposal never calls workspace ensure, allocator, create, split, move, rename, `agent.start`, or any input API. |
| `HerdrPaneChildKillTests` documents P8: Herdr itself closes a pane even with unexpected foreground processes. Normal launched kill refuses that close, may kill only its own identified child, and emits `HerdrPaneLeftOpen`. | Never delegate foreign-process safety to today's `pane.close`. Preserve existing kill behavior and tests; the new disposal refusal performs no PID kill. |
| `TryResolveNativeSessionId` (`HerdrPaneChild.cs:737`) prefers argv and ignores `agent_session` from source `antiphon`. Metadata identity has a 24-hour TTL. | Antiphon tokens select candidates; they cannot establish the identity of a replacement foreground process. |
| `SystemProcessLivenessProbe.IsAlive` allows a two-minute PID-reuse tolerance and returns true on access failure. `HerdrPaneSidecar.LaunchedAtUtc` is not uniformly exact OS creation time. | Neither is a destructive-operation identity check. Use exact process creation identity, and refuse unavailable evidence. |

Local, read-only protocol check performed in this Plan stage:

```text
herdr --version                  -> herdr 0.8.2
herdr api schema --json          -> protocol 20, schema_version 1
request pane.close params        -> #/schemas/request/$defs/PaneTarget
PaneTarget required/properties   -> pane_id / pane_id:string only
```

`HerdrClient.PaneCloseAsync` (`:376`) sends that same unconditional shape. This
establishes the installed CLI schema and current client contract; it does not
establish the version of any running Herdr daemon. No live pane close, agent Stop,
stack restart, or live-session inspection was performed for this plan.

## Operator surface

Use an independent resource family. It deliberately has no `{sessionId}` parent:

| Server route | Runner route | Purpose |
|---|---|---|
| `POST /api/herdr/pane-disposals/preview` | `POST /herdr/pane-disposals/preview` | Inspect an exact target and return eligibility, evidence, blockers and an expiring preview. No external mutations. |
| `POST /api/herdr/pane-disposals` | `POST /herdr/pane-disposals` | Execute exactly the previously reviewed target, with fresh checks. |
| `GET /api/herdr/pane-disposals/{operationId}` | `GET /herdr/pane-disposals/{operationId}` | Read the receipt after success, refusal, interruption or uncertain delivery. |

Preview request:

```json
{
  "paneId": "<exact workspace-qualified pane ID>",
  "expectedSessionId": "<optional full Antiphon session UUID>",
  "expectedNativeSessionId": "<optional full native conversation UUID>"
}
```

Require `paneId` and at least one full expected UUID. Reject prefixes, labels,
wildcards, implicit current-pane targets, multiple panes and workspace/tab-wide
requests. Keep Antiphon session identity and native conversation identity separate;
if both are supplied, require both to agree with their respective evidence.

The runner returns a bounded, immutable preview identified by a random `previewId`,
with a two-minute lifetime measured by injected `TimeProvider`. Its expiry or a
runner restart requires a new preview. This is an operator review/concurrency
mechanism, not authentication. Follow the existing single-user API boundary;
do not claim that an `operator: true` field creates an authenticated principal.

Preview includes exact workspace/tab/pane IDs and labels, pane/terminal incarnation
when available, runner/backend identity, origin and source of each identity fact,
current shell and occupant fingerprints, all matching claims, eligibility/blocker
codes, planned process termination, and whether removing this pane would leave
its tab empty. Expose only executable names and parsed identity fields, never raw
argv, environment, transcript contents, native-home paths or credential values.
The server adds optional current row/standing-owner status without manufacturing
rows for missing sessions. A refused preview still returns useful evidence with
`eligible: false`; it never grants an executable preview.

Execution request:

```json
{
  "operationId": "<new UUID for this attempt>",
  "previewId": "<preview UUID>",
  "reason": "Operator-selected leftover pane"
}
```

The executor uses the stored snapshot, never a caller-supplied replacement pane or
process allowlist. Require a nonblank, bounded reason. No `force`, ownership bypass,
automatic preview refresh, or automatic retry against a changed target.

Add ASCII-only `scripts/herdr-pane.ps1` with `inspect`, `dispose`, and `status` verbs.
Use `ANTIPHON_API` / the normal task header without exposing tokens. `inspect` calls
preview and prints a concise target/evidence/action summary. `dispose` requires the
preview ID, operation ID and `-Execute`; without `-Execute`, it prints the intended
request and does not submit it. Support `-ReasonFile` for long text and `-Json` for automation.
Use the server routes, never direct production-runner kill. No automatic sweeper,
tracker action, supervisor hook, bulk cleanup, or client UI is required here.

## Handling the three requested cases

| Case | Operator flow and result |
|---|---|
| Attached origin with a still-active binding | Inspect identifies the holder and refuses execution with `herdr_pane_bound`. Operator uses existing Stop on that exact standing agent, verifies suspension/detach, then obtains a fresh disposal preview. Stop still leaves the TUI running; explicit disposal closes only the now-unbound, independently verified target. Do not compose an implicit Stop into disposal. |
| Runner Exited/ProcessVanished, or runner representation gone | Inspect the requested pane directly. An exited in-memory locator, sidecar or last-pane can associate the expected session with it. Reinspect its actual occupant; no `HasExited` shortcut in the disposal service. Refuse a foreign replacement, even if the old row said Exited. |
| Server session GET is 404 | Absence is allowed. Use direct runner inspection and expected token/native identity as below. Never call `AgentSessionService.KillAsync`, create a synthetic row, attach for cleanup, or stop a similarly named agent. |

If an agent currently points to the target session, require its existing Stop intent
to be persisted, with no active/queued launch generation, before disposal is eligible.
This also covers an AlwaysOn agent whose current session has already exited. Use
the existing standing launch/reservation synchronization when checking execution;
runner I/O must stay outside a database transaction. A current pointer to a
different session does not authorize stopping that agent. The historical
CARD-0461 replacement orchestrator must remain untouched.

Pending/unreachable adoption, an active card/pool session, or an unclaimed live
runner binding is a refusal. The operator must settle that binding through its
existing control flow first. Disposal does not infer abandoned work from a 404,
silence, a name, or an exit badge.

## Identity and occupancy rules

Target association and current-occupant identity are separate checks.

1. Associate the exact requested pane with `expectedSessionId` using a matching
   runner locator, readable matching sidecar/last-pane, or matching
   `tokens["antiphon-session"]`. Alternatively, independently resolve the current
   native ID to `expectedNativeSessionId`. Sources must not contradict one another.
   Missing/expired tokens are not proof of absence or ownership. Contradictory
   claims refuse; never choose the first result from `FindBoundPane` and discard
   the others. Do not equate a token value with a native UUID without evidence.
2. Read a complete, successful process snapshot. A missing/null foreground list,
   unknown shell identity, malformed PID, unreadable creation identity, or a
   partial inventory means `herdr_pane_identity_unproven`, not an empty pane.
   The current `(ForegroundProcesses ?? [])` attach/relaunch helper cannot be
   reused unchanged for destructive classification.
3. An empty target is eligible only when it is a verified idle shell, with no
   other affected process or pending input/launch. Match the actual shell PID and
   exact OS creation identity in the preview and at execution. Token-only
   association can select this empty target because the operator explicitly chose
   it; the token still grants no authority over any subsequently arriving process.
4. An occupied target requires one positively identified supported agent process
   besides its verified shell. Require expected kind/executable-family agreement,
   exact process creation identity, and a matching native session in argv or a
   backend-native observation provably tied to that same process incarnation.
   A cached `agent_session` value with merely a non-Antiphon source is not enough
   for disposal unless that incarnation binding is established. Reuse the argv
   parser as appropriate, retaining the source distinction and checking conflicting
   native facts rather than silently taking the first one.
5. A surviving process may also be identified by an authoritative sidecar, or its
   retained in-memory binding snapshot after detach, with an exact recorded OS
   creation identity. Introduce an optional, explicitly named
   `ChildStartedAtUtc` field for future bindings if needed; do not reinterpret
   `LaunchedAtUtc`, `UpdatedAtUtc`, or `LastChildPid` as that field. Older sidecars
   without it must use independent native evidence. Occupied Codex without either
   positive process-bound native identity or exact recorded creation identity is
   refused. Kind, cwd, shell PID and a stale token alone cannot prove ownership.
6. Multiple non-shell foreground roots, any other affected/background process
   outside the proven shell/agent ownership tree, wrong kind/native ID, or a
   conflicting claim means leave the pane and all its processes untouched. There
   is no PID-kill fallback in this operation. The backend guard must account for
   the process tree affected by closure; an empty foreground list alone cannot
   certify that background work is safe to terminate.

Replacing a process between preview and execute always invalidates the preview,
even when the new process resumes the same native conversation. A fresh preview
can approve that new incarnation if it independently meets these rules. Loss of
identity after detach may leave some panes refused; preserve the refusal rather
than extending attached-origin ownership or trusting historical PIDs.

## Safety boundary and protocol prerequisite

The new action must not implement `process_info; pane.close` and call it atomic.
An operator shell can start a foreign process in the interval. A second snapshot,
runner semaphore, metadata token, or stable-looking `pane.revision` does not close
that interval; Herdr's revision has also been measured to remain sticky.

Before enabling execution, provide and verify a Herdr-side guarded-close primitive.
The name `pane.close_if_unchanged` below is a **proposed contract**, not a command
in protocol 20. It must:

- Bind a read-only observation to a backend instance, immutable terminal/pane
  incarnation, placement, exact shell/occupant creation identities, and the complete
  process ownership set affected by close.
- Revalidate that observation at the actual teardown boundary. Fence conflicting
  pane input, launch, move and teardown operations while validating/closing. Its
  implementation must also prevent an external process-start/tree change from
  slipping past validation; a mutex over RPC handlers alone is insufficient.
  Establish the platform mechanism with Herdr's owner and prove it in an isolated
  race test. If Herdr cannot provide this guarantee, refuse execution.
- Close precisely that pane when all conditions hold; otherwise return changed,
  foreign/unproven, or already-absent without terminating anything. Resolve the
  observed incarnation, never a recycled pane ID. Use an operation ID for a
  queryable/idempotent result after connection loss.
- Retain Herdr's ordinary empty-tab removal. Never call `tab.close` or
  `workspace.close`, even if a tab label looks Antiphon-owned.

Advertise a new runner feature `herdr-pane-disposal` for this implemented surface;
preview separately reports whether the selected, currently verified Herdr daemon
supports the guard. A new server with an old runner refuses before mutation. A new
runner with protocol-20 Herdr can inspect but returns
`herdr_disposal_guard_unavailable` for execution. Never send made-up optional guard
fields to today's `pane.close`: an older server could ignore them. Use a distinct,
explicitly supported method and tested protocol negotiation.

This is an implementation dependency, not a proposed relaxation of CARD-0213 or
of the foreign-process requirement. The plan is ready for TestDesign, but a
release claiming successful safe disposal is not ready until this dependency is
implemented and measured. An Antiphon-only preview/refusal increment is useful
but does not complete CARD-0461.

## Runner coordination and cleanup

Implement concrete `HerdrPaneDisposalService` and a pure eligibility classifier,
with process/backend I/O behind existing or narrowly extended seams. Wire it from
`SessionRunnerRuntime` without manufacturing a `HerdrPaneChild` and changing its
origin to invoke kill. Keep `ISessionChild.KillAsync`, all four attached-origin
kill protections, the event pump, and automatic recovery policies unchanged.

Extend instance-owned `HerdrPlacementCoordinator` with an exclusive pane operation
lease used by disposal and every Antiphon path that can acquire/replace that pane:
attach, named launch/adoption, last-pane reuse, allocator split selection, and
pending adoption. Ordinary detach/kill and sidecar publication/retirement must
participate where they race this lease, without changing their outcomes. Existing
named-placement claims alone are insufficient: attach and ordinary last-pane
reuse do not currently hold them throughout acquisition.

Use one documented lock order: existing workspace-key lock when relevant,
workspace-ID lock when relevant, then pane lease. No holder of a pane lease may
subsequently acquire either outer lock. Disposal resolves the exact placement
read-only, acquires the applicable locks, and rechecks it. Revalidate all live,
pending, sidecar and last-pane claims under the lease. Do not exclude the requested
session from the *live* claim check. A launch that already acquired the pane wins
and disposal refuses; disposal that won prevents acquisition until its verdict
and targeted cleanup are recorded. Herdr's guard separately covers activity
outside Antiphon.

After confirmed close or positive absence of the reviewed incarnation:

- Remove only captured matching sidecar/last-pane generations. Compare session ID,
  exact pane identity and file generation/content before deletion; never remove a
  newer record for that session on another pane. No recursive directory cleanup,
  unrelated metadata clearing, transcript deletion, native-session deletion, or
  launch-script replay.
- Do not write a replacement last-pane record, including for attached origin.
  Ordinary retirement and seven-day pruning retain their current behavior.
- Notify pane census listeners if their inputs changed. Do not emit a second
  `SessionExited` for an already-exited session or rewrite ProcessVanished/Detached
  history as PaneClosed. No synthetic DB session is needed for an orphan.
- On refusal or unknown outcome retain locator files. If file cleanup fails after
  closure, report `cleanupPending`; retry conditional cleanup only, never close a
  newly created pane to make an old receipt look complete.

## Results, retries and PaneLeftOpen

Persist one runner-local receipt per execution under
`<SessionLogPath>\herdr\disposals\<operationId:N>.json`, separate from live sidecar
and last-pane directories. Record the request fingerprint, exact target snapshot,
reason, clocks, backend operation identity, outcome and cleanup disposition; no
raw argv or secret-bearing data. Write execution intent atomically before the
destructive RPC. Failure to persist intent refuses before close. Preview snapshots
may remain in bounded memory; the execution receipt must survive restart.

| Result | HTTP / receipt semantics |
|---|---|
| Closed | 200, `outcome: "Closed"`, `paneLeftOpen: false`; backend confirms disposal of the reviewed incarnation. |
| Already absent | 200, `outcome: "AlreadyAbsent"`, `paneLeftOpen: false`; authoritative absence of that reviewed incarnation, including idempotent retries. A guessed or malformed pane ID without a valid preview is not success. |
| Foreign occupant | 409 `herdr_pane_foreign`, `outcome: "Refused"`, `paneLeftOpen: true`; no close and no PID kill. |
| Bound / identity-unproven / changed | 409 `herdr_pane_bound` / `herdr_pane_identity_unproven` / `herdr_pane_changed`; no mutation. Record pane existence separately when known. |
| Expired preview / operation-ID conflict | 409 `herdr_disposal_preview_expired` / `herdr_disposal_operation_conflict`; never silently refresh or repurpose the operation. |
| Guard missing | 409 `herdr_disposal_guard_unavailable`; capability/refusal, never fallback to unconditional close. |
| Pane never found at preview | 404 `herdr_pane_not_found`; no executable preview. |
| Backend unavailable before send | 503 `herdr_unreachable`; unchanged target, no death or closure verdict. |
| Close sent but result unknown | 503 `herdr_disposal_outcome_unknown` with operation ID; `outcome: "Unknown"`, `paneLeftOpen: null`. Inspect the backend operation receipt/read state; never infer close from timeout or replay a close against a new incarnation. |

A repeated operation ID with identical request returns/reconciles its existing
receipt before requiring a still-live preview; different content is a conflict.
A second operation cannot consume the
same preview while the first is in flight or uncertain. On restart, reconcile
issued/unknown operations with backend operation results or authoritative
incarnation absence. Do not replay destructive requests automatically. Retain
unresolved/cleanup-pending receipts; bound preview count and prune only old terminal
receipts under an explicit retention policy (default seven days).

`paneLeftOpen` describes this operation's observed outcome for the reviewed
incarnation. If a new incarnation now uses the same display ID, report
`replacementPresent: true` separately and never suggest it was closed. A missing
backend receipt plus a reused ID is Unknown unless the old incarnation's absence
is authoritatively established. Do not reuse
`HerdrExitReasons.PaneLeftOpen` as a session transition here: its existing contract
and incident text say the managed child was killed by PID. This operation has
refused all killing. Existing ordinary-kill tests and its Warning incident stay
accurate. Disposal has a separate typed receipt and structured log; server logging
can correlate an existing session/agent without inventing ownership. No new alert
sink or misleading "killed our child" incident is introduced.

## Implementation slices

1. **Backend prerequisite.** Specify/implement and measure the guard with Herdr's
   owner in its repository; record supported version/protocol and process-tree
   guarantees. This Antiphon worktree does not authorize edits to another checkout.
   Until it exists, implement only truthful preview/refusal, not a close fallback.
2. **Runner contract and classifier.** Extend `SessionRunnerContracts.cs`,
   `HerdrClient`, `HerdrApiModels`, exact process-identity inspection and
   `HerdrProblemMapper`. Add preview/disposal/receipt service, typed outcomes,
   leases, atomic receipts, and compare-before-delete cleanup. Test with
   `FakeHerdrServer` including a guarded backend model; label that model as new
   behavior, not evidence about installed protocol 20.
3. **Server and script.** Add `HerdrPaneDisposalEndpoints`, a concrete Application
   service, `ISessionRunnerClient` methods and `SessionRunnerHttpClient` wire mapping.
   Keep Herdr socket I/O in the runner. Preserve typed Problem Details and return
   receipt identity on non-2xx responses. Add the ASCII script. Use existing
   standing launch synchronization for current-session preconditions, without
   introducing a second Stop implementation or holding DB transactions over RPC.
4. **Documentation and acceptance.** Update `docs/herdr-sessions.md` with explicit
   disposal as the narrow operator exception to the old blanket "never closed"
   phrasing; ordinary Stop/kill/adoption still never close attached origin. Update
   `docs/ops-http.md` and `docs/antiphon-api.md` with both route prefixes, script,
   backend prerequisite, refusal/unknown semantics and the three cases. Complete
   isolated end-to-end acceptance before claiming CARD-0461 fixed.

No database pane-ID columns, agent setting, migration for pane placement, global
cleanup policy, workspace matching change, or live-owner change is required.

## Verification design handoff

This is safety-sensitive work: use separate TestDesign and require Review after
Mutation. TestDesign should turn these obligations into named V/R/PC cases and
exact class/method filters, including a positive control for every destructive
boundary. Do not count a fake backend's guarded-close success as a real Herdr proof.

| Obligation | Required evidence |
|---|---|
| Attached baseline preserved | Ordinary Stop/kill still clears metadata, deletes sidecar, emits Detached, keeps child/pane alive and writes no last-pane. Cover orphan, pending and adoption-failure detach protections. |
| Explicit attached disposal | Stop/detach then fresh preview closes the verified target through the new guard. Another pane in the same tab, and the current replacement orchestrator in another workspace, survive. No split/move/rename/tab close/input calls. |
| Exited / missing runtime / DB 404 | Each path reaches the independent disposal service and succeeds with sufficient current identity. Missing DB rows remain missing; historical exit reasons remain intact. |
| Token and native trust | Matching token plus foreign process refuses; matching token plus known empty shell can qualify. Native-only exact process evidence works. Expired token, source=antiphon native metadata, stale non-Antiphon native observation, ID prefix, conflicting UUIDs, old PID and Codex without identity refuse. |
| Incomplete or unsafe census | Null list, unreadable creation time, missing shell, PID recycling within two minutes, multiple roots and background foreign work refuse with no close/PID kill. |
| Concurrency | Change PID/start time/native ID/terminal incarnation/tab/workspace between preview and execution; race attach, pending adoption, ordinary last-pane launch and named launch against disposal. One acquisition wins, no wrong-pane close or deletion of new records. |
| Actual teardown race | Inject a foreign process after Antiphon's final read but before backend teardown. The real guard leaves it and the pane alive. Test external process-tree changes, not just another RPC call. Unconditional-close substitution must fail this positive control. |
| Protocol refusal | Old runner and unguarded Herdr refuse; zero unconditional `pane.close` calls. Unsupported optional fields cannot masquerade as protection. |
| Uncertain delivery and retry | Backend closes then drops reply, request canceled after send, restart after intent/RPC/before receipt, repeated same/different operation payload, pane ID reuse, and cleanup failure. Accurate Unknown/Closed/AlreadyAbsent; no duplicate teardown or false success. |
| Last-pane and history | Successful cleanup removes only captured records; refusal keeps them; races cannot delete newer sidecar/last-pane. No synthetic session, duplicate exit, transcript/native history deletion, or false PaneLeftOpen incident. |
| Supervision/ownership | A current AlwaysOn target requires persisted Stop/no launch reservation. Old orphan with a different live current session does not stop or reconfigure that live agent. A queued reacquisition cannot bypass the runner lease. |

New test classes should cover classifier/service, route and HTTP wire contracts,
script behavior, and an isolated real guarded-backend canary. Existing regression
classes include `HerdrPaneChildKillTests`, `HerdrAttachTests`,
`HerdrAdoptionSweepTests`, `HerdrNamedTabPlacementTests`, `HerdrPaneSidecarTests`,
`AgentAttachHerdrTests`, `SessionRunnerHttpClientHerdrWireTests`, and the relevant
standing Stop/supervision classes changed by the implementation.

Run TUnit via `dotnet run --project tests/<ProjectName>`, with explicit class or
method `--treenode-filter` values and safe alternate output directories such as
`--property:OutputPath=bin-c461/`. Require executed nonzero counts. Use
`ParallelLimiter<ProcessSpawnLimit>` for new process-spawning tests; never run
Antiphon.Tests and Antiphon.Agents.Pty.Tests concurrently. Real-Program test hosts
must retain `ProductionRunnerGuard` or an isolated random runner. Real Herdr tests
must use an owned, isolated named backend and disposable dummy processes, with
sentinel panes/tabs to prove noninterference; do not use the historical production
pane IDs or send live channel traffic.

## Decisions and stage result

Defaults selected: pane-addressed two-step API; existing Stop first for active
bindings/current supervised sessions; no implicit Stop, force, broad sweep or UI;
strict current identity; zero PID killing on disposal refusal; separate durable
operation receipt; guarded backend closure required. No operator answer is needed
to produce this plan or its tests. A separate Herdr implementation/deployment is
required before the successful-disposal path can be enabled.

Plan-stage validation: board reads, current source inspection, and the local CLI
schema/version check above. No tests/builds run because this change is the plan
artifact only. No production state changed. Next stage: TestDesign, retaining the
backend prerequisite and the real teardown-race acceptance gate.
