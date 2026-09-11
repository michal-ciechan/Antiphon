# CARD-0461: explicit disposal of a leftover Herdr pane

## Accepted execution revision — 2026-09-11

The operator explicitly authorized **best-effort Antiphon check-then-close** on
2026-09-11. This revision supersedes every atomic/backend-guard prerequisite and
backend-fence/operation-receipt assumption below. Other identity, lifecycle,
coordination, retention and redaction requirements remain in force. The original
2026-09-10 design below is retained as design history where it discusses an
unavailable backend primitive; it must not block this revised implementation.

Execution uses the existing protocol-20 `pane.close` with **only `pane_id`**.
Antiphon takes a pane lease, captures intent durably, then reinspects the selected
pane, exact process creation identities, supported/native occupant evidence,
OS shell descendants and runtime/pending/placement/sidecar/last-pane claims
immediately before close. Foreign/unexpected occupants **visible at that check**
refuse without close or PID killing. Antiphon leases serialize Antiphon acquisition,
publication, detach and retirement. They do not fence external Herdr input, move,
close or process creation. External changes **after** the final check may be
terminated by unconditional close: a documented accepted limitation, not a hard
invariant and not a release blocker. An absent optional backend pending-input
observation is unknown, not proof that no external input can arrive.

The daemon's named-pipe server PID and exact creation time identify the observed
backend process. The terminal ID plus placement and exact process identities
detect observed changes; none is represented as an atomic backend comparison.
`guardAvailable` means Antiphon's best-effort check is available; responses expose
`guardMode: antiphon-best-effort` and `atomicClose: false` explicitly. A versioned
best-effort feature/wire opt-in prevents the earlier preview-only server, which
has no standing ownership gates, from accidentally activating a newer runner.

Herdr supplies no operation-ID receipt. The runner persists a sanitized reviewed
snapshot before send. Same-operation retries query that receipt, never replay
destruction. Post-send loss/cancellation is Unknown. Status can establish
AlreadyAbsent only with the same observed backend, absence of the reviewed
terminal in a complete pane census, and positive absence of the original OS
process incarnations. Display-ID reuse is reported separately. Unavailable or
ambiguous evidence stays Unknown. The new runner-only preview lookup permits the
server to inspect the immutable expected IDs before taking its existing standing
delivery/reservation locks; it does not create or refresh a preview.

### Revised verification obligations (all 117 IDs retained)

- V-1..V-8 keep their acceptance goals, using check-then-`pane.close`, durable
  runner receipts and direct read-only reconciliation instead of a guarded RPC
  or backend operation query. Unknown is not Closed. All other refusal and
  preservation cases stay mandatory.
- V-9 uses an owned uniquely named **stock Herdr 0.8.2** server, isolated config/
  log roots, disposable processes and sentinel panes. No custom backend source,
  fork or process-start fence instrumentation is needed. Prove positive closure,
  foreign foreground/background refusal **at the final check**, changed placement
  before that check, and lost-result reconciliation. Schedule changes after the
  first runner observation but before its final inspection using acknowledged
  barriers. No assertion promises survival of arrivals after the final check.
- G/PC-14 (`Backend_feature_gate`): refuse unverifiable/incompatible **selected
  daemon** protocol/OS identity; protocol 20 is supported. Mutant bypasses that
  verification; assertion is zero close on unverifiable backend.
- G/PC-15 (`Distinct_guard_wire`): the reviewed target reaches **existing**
  `pane.close` with exactly `pane_id`; no made-up guard fields or unsupported
  method. Mutant sends the wrong method/shape; captured wire assertion fails.
- G/PC-33 (`No_pending_input`): refuse a **reported** pending-input observation;
  absence does not establish an external input fence.
- G/PC-83 (`Foreign_after_runner_inspection`): a foreign foreground process is
  present before the final Antiphon check. Mutant omits final inspection and uses
  the earlier snapshot; real foreign-process/pane survival assertion fails.
- G/PC-84 (`External_tree_race_at_teardown`): a foreign **background descendant**
  is present before the final check. Mutant omits current OS descendant inspection;
  real survival assertion fails. No backend source mutation is required.
- G/PC-85 (`Backend_rpc_fence`): an external pane move/replacement completes before
  final inspection. Mutant omits final placement/incarnation comparison; target
  survival assertion fails. The historical name does not claim an RPC fence.
- G/PC-93: original absence is based on the conservative direct census/process
  rules above, not a nonexistent backend receipt.
- G/PC-113 (`Queryable_backend_identity`): recovery uses the **persisted reviewed
  backend/terminal/process identity**. Mutant substitutes a new current identity;
  restart/reuse remains Unknown and zero-close assertions detect it.

The same exact class/method IDs below continue to identify all 117 tests and
Mutation controls; these replacements override the obsolete atomic controls in
their rows. Code runs ordinary V/R only. Mutation runs red/restored-green in the
retained Code worktree, then mandatory Review. The historical 479-minute estimate
included custom-backend setup/fence mutations and is superseded for those costs;
record measured time for the stock-backend cases, without padding or skipping.

## Original design and retained non-atomicity requirements

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

## Verification design

TestDesign date: 2026-09-10. Design base: landed plan commit `5f29b65a`,
present in this worktree and local `master`. This section adds verification only;
the implementation design and backend prerequisite above are unchanged.
**Code owns all V/R implementation and execution. Mutation owns every PC below,
including real-backend controls. Review is required after Mutation.** These are
specified tests, not claims of executed tests or of an available guarded backend.

### Inspection

Read the following test bodies and the named fixture/helper implementations before
selecting the new cases. Paths below are relative to `tests/`.

| Bodies/helpers inspected | Boundary consequences |
|---|---|
| `Antiphon.SessionRunner.Tests/HerdrPaneChildKillTests.cs`: foreign-foreground kill and attached kill bodies | R-1/R-2 and B controls preserve real dummy-process survival, Detached metadata/file behavior, and the different ordinary launched-kill contract. |
| `HerdrAttachTests.cs`: attached kill, readoption, orphan, allocator exclusion; `SeedGrokPane`, `SeedGrokHome`, `BuildRuntime`, read-only RPC assertions, dummy/cleanup helpers | V-1/V-3/V-5; attach binding and allocator behavior require runtime tests, not only classifier tests. Existing fake shell PID 1 is not exact OS identity. |
| `HerdrAdoptionSweepTests.cs`: R20 attached orphan and R21 attached exit; production `TryKillOrphanedChild`, `CloseHerdrAfterBarFailed`, `KillPendingHerdr` | R-3 plus four separately mutated attached-origin protections. New pending/failed-bar test setup is required; orphan coverage alone cannot prove either branch. |
| `HerdrNamedTabPlacementTests.cs`: all foreign-occupant vectors, both concurrent named-launch bodies, request/runtime/last-pane helpers | V-5/R-4: deterministic gates and no-mutation deltas; disposal must coordinate with both named acquisition and ordinary reuse/split. |
| `HerdrPaneSidecarTests.cs`: atomic round-trip, retirement, attached-origin exclusion | V-6/R-5: captured bytes/generations and sentinel files; historical PID/launch time are not current destructive identity. |
| `FakeHerdrServer.cs`: `GateMethod`/drop/release, process-info serialization, unconditional close | V-2/V-4/V-6: extend the fake explicitly with complete process identities, immutable incarnations, guarded operation receipts, and after-close/drop faults. Current fake removes a pane without killing an OS process; it cannot establish teardown safety. |
| `HerdrPlacementCheckRouteTests.cs`: read-only check, occupied mapping and runtime helper | Q/W tests require real HTTP route execution in addition to direct mapper assertions. |
| `Antiphon.Tests/Application/AgentAttachHerdrTests.cs`: Stop body, pane/home seeding and `BuildHarness` | V-1/V-3/R-6: use isolated DB and real Application service to direct runner; this fixture's synthetic PID does not prove process survival. |
| `StandingSessionSwitchConcurrencyTests.Stop.cs`: all three launch boundaries and `LaunchBoundaryGate`; `StandingRecoveryFixture.cs` | V-3/R-7: persisted suspension is distinct from queued/active launch ownership. Retain accepted generation; inspect no DB transaction at runner I/O. |
| `Antiphon.Tests/Agents/SessionRunnerHttpClientHerdrWireTests.cs`: placement POST/409 and capturing handler/Problem helpers | V-7/R-8: preserve both route prefixes and typed non-2xx receipt fields across actual HTTP serialization. A mocked handler alone does not prove server routing. |
| `Antiphon.Tests/Scripts/RoutingPinScriptTests.cs`: executed-script request assertions, ASCII test, `RunPinAsync`/`RunProcessAsync`/`StubApi` | V-8: run the real script via pwsh and ArgumentList against an ephemeral loopback API, with a synthetic task token, captured body/output and bounded owned-process lifetime. |
| `HerdrLiveSession.cs` and entire `HerdrNamedTabPlacementLiveTests.cs` | V-9 needs a new isolated fixture. Existing helper uses the default daemon, and existing live cleanup calls tab.close. Neither is a safe fixture for this disposal/race battery. |

The unqualified runner filenames above are all in
`tests/Antiphon.SessionRunner.Tests/`. New classes and exact method addresses are
defined below; Code must implement them, not silently substitute existing
weaker tests.

**Fixture contract.** Add a per-test disposal fixture with unique temporary log,
receipt and dummy native-history roots; explicit named FakeHerdrServer; injected
exact process inspector; mutable process/placement/claim snapshots; instance-owned
placement coordinator; and barriers at resolution, lease acquisition, final runner
inspection, intent persistence, guarded send, backend close, receipt publication
and conditional file deletion. Never use a test-wide frozen clock with the server
queue; use an advancing clock or real-clock offset. A pure classifier clock may
be advanced deterministically to TTL boundaries. Independently count guarded and
unconditional close, PID termination, input, placement calls and session events.
Snapshot locators, labels/tokens, history and sibling process identities before
each operation. Exclude fixture setup/teardown from operation RPC deltas.

Allocator-preservation fixtures must resolve to the same existing workspace as
the attached sentinel, with spare split capacity, so a missing attached-origin
exclusion actually selects that operator pane. Do not let a mismatched workspace
silently allocate elsewhere and mask this regression. Separate token-owned and
unique untagged operator-label cases exercise ownership-token preservation.

For all refusal tests assert the exact refusing layer/code, zero destructive
dispatch from that layer, zero PID kill, unchanged locators, and unchanged sentinel
state. A later guard's refusal does not prove an earlier guard: classifier tests
assert the classifier result directly; server guards assert zero runner execution
calls; runner guards assert zero guarded-close dispatch. Fakes must not turn a
mutated eligible classification into a pass by refusing later. Do not mutate the
test oracle or fixture to make a production mutation detectable.

New OS-process tests use the assembly's own
`ParallelLimiter<ProcessSpawnLimit>`, owned process handles and exact creation
identities, and await/terminate only their own dummy children in finally. Test
hosts booting real Program retain `ProductionRunnerGuard`, disabled interpreter/
diagnose/distiller provisioning and an explicitly injected isolated runner.
Tests do not address ports 17202-17205, the default Herdr socket, historical
production pane IDs, live channels or provider homes.

### Delivery inventory

This change introduces pull-based operation receipts, not a channel/session
notification or a new session-input queue. The durable join key is
`operationId + request fingerprint + backend instance/operation ID + reviewed pane incarnation`.
The following producer-to-reader paths must be exercised through real runner and
server HTTP hosts with file persistence, not only by returning a DTO from a stub.

| Producer -> destination | Persistence boundary | Recovery and observable receipt |
|---|---|---|
| Runner execution acceptance -> backend guarded operation | Atomic runner intent exists before send; backend result is keyed by the same operation identity. | Crash before intent: no close. Crash after intent/before send: query yields unresolved/authoritatively reconciled state; never replay destruction. Backend entry asserts durable intent. V-6, PC for intent/atomicity/replay/query identity. |
| Backend close/refusal -> runner receipt store | Backend commits its queryable verdict; runner publishes complete receipt atomically. | Drop reply, cancel after send, or restart after close/before receipt: status queries the original backend operation. Unknown remains explicit until authoritative evidence arrives. V-6/V-7/V-9. |
| Runner persisted receipt -> server GET -> script/operator | Runner receipt survives runner/server recreation; server adds optional row facts without making rows. | GET during a busy/in-flight execution returns its actual nonterminal/Unknown state; GET after already-terminal execution returns that exact durable result. A dropped POST response and subsequent GET must deliver the same operation identity/outcome to the HTTP client and real script. V-6/V-7/V-8. |
| Confirmed closure -> conditional locator cleanup -> census listener | Receipt retains cleanupPending until captured matching generations are deleted. | Restart/failure retries deletion only. Real event subscriber observes changed census; existing exited-session history is unchanged and no second SessionExited is published. V-6. |

At each handoff inject failure immediately before and after the listed commit/send/
publication; then recreate the relevant service and read via status. Assert
destination JSON and script-parsed result, not just accepted POST, local intent,
transport acknowledgement or a close counter. V-7 includes an in-flight reader
and an already-completed reader; neither can manufacture Closed. Unknown is an
honest observed receipt, not successful-disposal acceptance. Successful acceptance
also needs backend verdict and pane/process evidence in V-9.

There is no new queue enqueue boundary to test here, and no complete UserPrompt
is expected because disposal types nothing. If Code adds asynchronous session/
channel delivery, it must return to TestDesign to enumerate the real queue,
busy/eligible recipient and each enqueue/crash boundary with complete matching
UserPrompt evidence where input is involved. A fake backend proves only Antiphon
protocol/coordination/recovery behavior; a stub HTTP server proves script/wire
handling; neither replaces real-backend process safety.

### Proves it works now

Class aliases resolve to exact new files and classes:

| Alias | Project | File/class |
|---|---|---|
| Q | `tests/Antiphon.Tests` | `Application/HerdrPaneDisposalEndpointTests.cs` / `HerdrPaneDisposalEndpointTests` |
| S | `tests/Antiphon.Tests` | `Application/HerdrPaneDisposalApplicationTests.cs` / `HerdrPaneDisposalApplicationTests` |
| W | `tests/Antiphon.Tests` | `Agents/HerdrPaneDisposalHttpWireTests.cs` / `HerdrPaneDisposalHttpWireTests` |
| P | `tests/Antiphon.Tests` | `Scripts/HerdrPaneScriptTests.cs` / `HerdrPaneScriptTests` |
| I | `tests/Antiphon.SessionRunner.Tests` | `HerdrPaneDisposalIdentityTests.cs` / `HerdrPaneDisposalIdentityTests` |
| D | `tests/Antiphon.SessionRunner.Tests` | `HerdrPaneDisposalServiceTests.cs` / `HerdrPaneDisposalServiceTests` |
| C | `tests/Antiphon.SessionRunner.Tests` | `HerdrPaneDisposalConcurrencyTests.cs` / `HerdrPaneDisposalConcurrencyTests` |
| B | `tests/Antiphon.SessionRunner.Tests` | `HerdrPaneDisposalStopRegressionTests.cs` / `HerdrPaneDisposalStopRegressionTests` |
| L | `tests/Antiphon.SessionRunner.Tests` | `HerdrPaneDisposalGuardedLiveTests.cs` / `HerdrPaneDisposalGuardedLiveTests` |

Every named `C461_Gnnn_...` method in the PC table is also an ordinary V test:
Code runs it green in the corresponding class, with every specified boundary
vector. The following methods supply positive cases and combined boundary tests
beyond the individual guards. Exact method filters expand as
`/*/*/<resolved class>/<method>`; class verification uses
`/*/*/<resolved class>/*`.

| ID | Layer and exact tests | Required behavior and boundary coverage |
|---|---|---|
| V-1 | S.`Disposes_detached_exited_and_rowless_targets`; D.`Disposes_each_supported_evidence_shape` | Stop/detach then **fresh** preview; retained Exited/ProcessVanished runtime; sidecar-only; last-pane-only; no DB or runtime row. Exercise each locator shape with verified idle shell and supported occupied Claude/Grok/Codex, using valid evidence for that shape. Close exact pane; companion pane in same tab and replacement orchestrator sentinel elsewhere survive. Orphan stays rowless. Exited HasExited does not short-circuit disposal. |
| V-2 | I.`Accepts_only_current_positive_process_evidence` plus all I guard methods | Matching token + verified idle shell qualifies; native-only exact process identity qualifies; sidecar or retained detached binding with exact creation identity qualifies; Codex tests both positive proof arms. Companion invalid cases exercise every identity guard below. |
| V-3 | S.`Stopped_current_target_is_eligible_after_launch_release`; B.`Stop_then_fresh_preview_is_the_only_explicit_close` plus all S/B ownership/Stop methods | Real Stop persists suspension and releases acquisition, leaves child/pane alive, clears only Antiphon metadata/sidecar and emits Detached. Only a later explicit disposal closes it. An exited current AlwaysOn target is still refused until Stop and launch release. Exercise before-spawn/start-rpc/saved-running boundaries. |
| V-4 | Q.`Routes_validate_preview_execute_and_status`; D.`Negotiates_guarded_and_legacy_daemons` plus all Q/D protocol/preview methods | Correct server /api/herdr and runner /herdr routes. Missing target preview 404 is not executable. Unknown preview and never-seen pane cannot produce AlreadyAbsent. TTL one tick before/exactly at/one tick after two minutes; capacity eviction; runner restart. Actual selected daemon capability is decisive, including mismatching installed CLI and daemon. |
| V-5 | C.`Different_panes_progress_independently`; C.`Cancellation_releases_every_lease` plus every C method | Gates force both acquisition-wins and disposal-wins for attach, named launch/adoption, last-pane reuse, allocator split, pending adoption, detach/kill and file publication/retirement. All actors are actual runtime paths. Await all tasks; verify one winner, unchanged replacement records, no deadlock and no global serialization across unrelated panes. |
| V-6 | D.`Crash_boundaries_reconcile_to_durable_receipts`; D.`Cleanup_is_conditional_and_census_is_observable` plus all D receipt/cleanup methods | Table-driven faults before/after intent, send, backend commit, receipt publish and each file delete. Restart both sides independently; same-operation sequential/concurrent retries; changed request conflicts; different operations sharing preview; already-absent original vs reused display ID. Retain Unknown/cleanupPending beyond seven days; valid terminal expiry at seven days. Receipt count and physical teardown count agree. |
| V-7 | W.`Server_runner_status_round_trip_survives_lost_post_reply`; W.`All_disposal_outcomes_round_trip` plus W guards | Real loopback server/runner HTTP and runner file store. Round-trip Closed, AlreadyAbsent, each 409, 404, pre-send 503 and Unknown 503. Read status while execution is gated and after completion/restart. Verify JSON nullable paneLeftOpen, replacementPresent, operationId, cleanupPending and typed code, including error responses. |
| V-8 | P.`Inspect_dispose_and_status_use_real_script`; P.`ReasonFile_json_and_ascii_contract` plus all P guards | Real pwsh script inspect/dispose dry-run/dispose -Execute/status; mandatory IDs and bounded reason; exact request; ReasonFile preserves long text without terminal quoting; -Json output parses on success/refusal/Unknown; status after lost POST matches operation. All script bytes ASCII; fake task token never printed. |
| V-9 | L.`Isolated_backend_closes_only_reviewed_incarnation`; L.`Isolated_backend_recovers_lost_close_result` plus all three L controls | Actual guarded backend, owned dummy processes, exact OS identities and isolated server/runner. Prove idle and positively identified occupied disposal, sibling and replacement survival, backend empty-tab removal with no tab/workspace close, drop/restart recovery, and both real race schedules specified below. This is a mandatory release gate. |

**Boundary combinations.** For V-1, construct five locator scenarios named above
against four occupant forms (idle shell/Claude/Grok/Codex): 20 qualifying cases.
For detached and sidecar scenarios also prove legacy-without-creation-time refusal
and recovery through independent native evidence. In V-2, permute conflicting
source order and pair each accepted source with a foreign occupant and each
available valid alternate source; contradictions still refuse. Unknown shell,
invalid PID and partial census remain unsafe even with perfect token/native data.
V-5 tests both winner orders for each acquisition arm. V-6 crosses each crash
boundary with unchanged incarnation and same-ID replacement. Cross guard
availability with idle/occupied, DB present/404, and fresh/stale preview; unsupported
backend never executes. Full Cartesian combinations of unrelated formatting
errors are excluded because they share the same pre-I/O validator; each invalid
shape is still an independently reported argument case. No unsafe process or
ownership branch is excluded for runtime cost.

**Real backend fixture and race recipe (V-9).**

1. Implement `HerdrPaneDisposalIsolatedFixture` in the runner test project. Require
   an explicit uniquely named test daemon, owned process handle/start identity,
   isolated socket/log/config roots and a verified backend instance plus supported
   guarded protocol. Refuse default/shared daemon resolution before creating any
   panes. Record backend repository commit, binary hash, protocol, platform fence
   mechanism and runner implementation SHA. The plan authorizes no edits to a
   separate existing Herdr checkout; the backend owner must supply/authorize an
   isolated build and its source for the backend mutation controls. No guarded
   build or no auditable process-tree fence means V-9/its PCs are **unexecuted
   prerequisites**, never skipped-green or a completed CARD-0461.
2. Create target, same-tab sibling, other-tab sentinel and replacement-orchestrator
   sentinel in another workspace using disposable shells/helpers. Record all
   pane/terminal and OS process identities. Use an owned dummy executable with
   controlled agent-shaped argv for occupied paths; no real provider/auth/network
   dependency. Verify its actual executable-family and native-identity inspection
   path, rather than faking process liveness in the live test.
3. First schedule: take a valid preview of the target idle shell. Pause after the
   runner's final observation and before guarded backend close. A prearmed helper
   in that shell starts a real foreign foreground process in response to an
   external named event/pipe; confirm its OS PID/creation time and heartbeat before
   releasing close. No Herdr input/launch RPC causes this start. Fixed backend
   returns changed/foreign with pane and foreign process alive; all sentinels
   survive. Repeat for foreign background work under the closure-affected tree.
   The unconditional-close PC uses this same successful-start barrier and must
   fail the **real survival** assertion, not merely a method-name check.
4. Second schedule attacks the backend interval itself: provide a test-only
   barrier immediately after its final process validation and before irreversible
   teardown. The external helper attempts process creation/tree attachment during
   that interval, bypassing RPC locks. Instrument attempted/admitted creation,
   fence acquisition and teardown with ordered acknowledgements. A correct
   implementation must either reject close when foreign admission wins, or prove
   creation was prevented for the whole validation/teardown interval. Do not
   count a delayed thread as a prevented start. Force an additional foreign-wins
   schedule before fence acquisition that leaves pane and foreign child alive.
   The backend-fence mutant must admit a foreign child in the vulnerable interval
   and then kill it/remove its pane, failing the survival assertion. If the
   harness cannot force/observe this interleaving, evidence is inadequate; return
   to TestDesign/backend owner, not a probabilistic success claim.
5. Independently race input/launch/move/close RPCs against the guarded boundary to
   prove the RPC fence. This supplements, never substitutes for, step 4. Include
   same-display-ID/new-incarnation and descendant/background process changes.
   Run at least 20 acknowledged trials per foreground/background race schedule
   in fixed code; the mutant and restored code must execute the same schedules.
   No sleep-only race trigger. Close-success companions ensure a backend that
   always refuses cannot pass acceptance.
6. Assert observed foreign-PID liveness using held process handles plus a fresh
   heartbeat, pane census and exact sentinel identities **before fixture cleanup**.
   Preserve sanitized traces/results. Cleanup may terminate only fixture-owned
   dummy processes and the owned isolated daemon after checked identity; never
   enumerate/close production tabs. A hung/unsupported/faulted fixture is not a
   red PC or green verification.

### Guards the regression

Run these existing classes with class filters, retaining their ordinary contracts.
New B controls supplement them where a branch lacks a decisive existing method.

| ID | Existing exact class/method(s) | Decisive assertion |
|---|---|---|
| R-1 | `HerdrPaneChildKillTests.Attached_kill_detaches_without_pane_close_or_pid_kill` | Real child alive; pane present; Detached/0; metadata cleared; sidecar and last-pane absent. |
| R-2 | `HerdrPaneChildKillTests.Foreign_foreground_process_kills_our_child_by_pid_leaves_pane_open_and_returns_true` | Ordinary launched kill kills only its child and keeps PaneLeftOpen semantics. Disposal has no such fallback. |
| R-3 | `HerdrAttachTests.Attached_kill_detaches`, `Attached_orphan_is_dropped_not_killed`, `Attached_pane_is_not_an_allocator_slot`; `HerdrAdoptionSweepTests.R20_attached_orphan_is_dropped_not_killed`, `R21_attached_exit_leaves_no_last_pane` | Stop/adoption/allocator protections stay intact after lease changes. |
| R-4 | `HerdrNamedTabPlacementTests.Named_launch_refuses_every_foreign_occupant_shape_with_zero_side_effects`, `Two_concurrent_named_launches_yield_one_winner_one_refusal_and_one_tab`, `Two_concurrent_named_launches_into_an_absent_tab_create_it_once` | No steal/fallback; one winner/tab. Ordinary placement semantics remain the landed contract. |
| R-5 | `HerdrPaneSidecarTests.last_pane_round_trips_and_retire_moves_atomically`, `retire_of_an_attached_sidecar_writes_no_last_pane_record` | Ordinary launched retirement still works; attached retirement never grants reuse. Run the whole class for legacy serialization/pruning. |
| R-6 | `AgentAttachHerdrTests.Stop_on_an_attached_agent_leaves_the_pane_process_alive` | Server row Stopped; pane survives; no close. B/V-9 supplies actual process proof missing from this fake PID fixture. |
| R-7 | `StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation` | Three boundaries preserve suspension and reject obsolete launches; subsequent owned generation still works. |
| R-8 | `SessionRunnerHttpClientHerdrWireTests.Placement_check_posts_options_without_exe_or_env_and_maps_409_to_conflict` and `HerdrPlacementCheckRouteTests.Check_reports_create_when_tab_or_workspace_is_absent_and_mutates_nothing` | Existing placement inspection and typed conflict mapping remain unchanged. |

### Guard inventory

This inventories the plan's destructive and preservation invariants, including
guards whose implementation does not yet exist. Each row maps to exactly one
distinct PC. References are to sections above; `Identity n` means numbered rule n
in **Identity and occupancy rules**, `Coordination` means **Runner coordination
and cleanup**, and `Results` means **Results, retries and PaneLeftOpen**.
These are individually bypassable checks or operation invariants; additional
guards discovered in Code/Mutation require new rows and controls, not silent
coverage by a nearby test.

| Guard | Plan reference and invariant | Control |
|---|---|---|
| G-1 | **Operator surface:** Only one exact workspace-qualified pane; no label, prefix, wildcard, current-pane or tab/workspace target. | PC-1 |
| G-2 | **Operator surface:** At least one full expected session/native UUID; never prefixes or an empty UUID. | PC-2 |
| G-3 | **Identity 1:** When both IDs are supplied, each must agree with its own identity domain. | PC-3 |
| G-4 | **Operator surface:** Unknown/missing preview cannot authorize execution or AlreadyAbsent. | PC-4 |
| G-5 | **Operator surface:** Two-minute preview TTL is checked at execute; no automatic refresh. | PC-5 |
| G-6 | **Operator surface:** Runner restart invalidates memory previews for new operations. | PC-6 |
| G-7 | **Operator surface:** An ineligible preview grants no executable authorization. | PC-7 |
| G-8 | **Operator surface:** Execute uses the reviewed immutable snapshot, never replacement pane/allowlist input. | PC-8 |
| G-9 | **Operator surface:** Reason is nonblank and bounded. | PC-9 |
| G-10 | **Operator surface:** No force/operator flag can bypass identity or ownership. | PC-10 |
| G-11 | **Operator surface:** The real script sends a disposal POST only with -Execute. | PC-11 |
| G-12 | **Operator surface:** Preview performs only observations; writes no metadata, locator or receipt intent. | PC-12 |
| G-13 | **Safety boundary:** A server refuses an old runner lacking herdr-pane-disposal before mutation. | PC-13 |
| G-14 | **Safety boundary:** The actual selected daemon must explicitly support the distinct guarded method. | PC-14 |
| G-15 | **Safety boundary:** Never disguise guard fields as optional params to unconditional pane.close. | PC-15 |
| G-16 | **Identity 1:** A known pane with no matching locator/token/native evidence is not owned. | PC-16 |
| G-17 | **Identity 1:** Examine all claim sources and refuse contradictions, regardless of enumeration order. | PC-17 |
| G-18 | **Identity 2:** Null/missing foreground list or a partial/error census is unproven, not empty. | PC-18 |
| G-19 | **Identity 2-3:** An idle pane requires a known actual shell identity. | PC-19 |
| G-20 | **Identity 2:** Every affected process PID must be valid; malformed or nonpositive values refuse. | PC-20 |
| G-21 | **Identity 2:** Unavailable exact creation identity refuses, including access failure. | PC-21 |
| G-22 | **Identity 5:** A historical sidecar can prove a child only with exact recorded OS creation identity. | PC-22 |
| G-23 | **Identity 5:** LaunchedAtUtc/UpdatedAtUtc/LastChildPid are never ChildStartedAtUtc authority. | PC-23 |
| G-24 | **Identity 4:** Antiphon-written agent_session is not native process evidence. | PC-24 |
| G-25 | **Identity 4:** Other-source native metadata must be tied to this process incarnation. | PC-25 |
| G-26 | **Identity 4:** Native argv and backend facts cannot contradict one another. | PC-26 |
| G-27 | **Identity 4:** Occupied disposal supports only a positively identified supported agent kind. | PC-27 |
| G-28 | **Identity 4:** Expected kind and executable family must agree. | PC-28 |
| G-29 | **Identity 4:** Native process evidence must match the entire expected native UUID. | PC-29 |
| G-30 | **Identity 5:** Occupied Codex needs process-bound native or exact authoritative recorded identity. | PC-30 |
| G-31 | **Identity 6:** Multiple non-shell foreground roots refuse. | PC-31 |
| G-32 | **Identity 6:** Any foreign affected/background process makes closure unsafe. | PC-32 |
| G-33 | **Identity 3:** An idle shell with pending input is not disposable. | PC-33 |
| G-34 | **Identity 3:** An idle shell with pending backend launch is not disposable. | PC-34 |
| G-35 | **Identity 1,6:** A matching token selects the pane but never authorizes a foreign current occupant. | PC-35 |
| G-36 | **Identity 3; Coordination:** Shell PID and exact creation identity are rechecked against the preview. | PC-36 |
| G-37 | **Identity 4-5; Coordination:** Replacing a child invalidates preview even for the same native conversation. | PC-37 |
| G-38 | **Identity 4; Coordination:** Native identity changes after preview invalidate execution. | PC-38 |
| G-39 | **Safety boundary:** Display pane-ID reuse never redirects disposal to a replacement incarnation. | PC-39 |
| G-40 | **Safety boundary:** A backend-instance change invalidates the observed target. | PC-40 |
| G-41 | **Coordination:** Workspace/tab placement is rechecked after acquiring locks. | PC-41 |
| G-42 | **Requested cases; Coordination:** Do not exclude the requested session from live claims. | PC-42 |
| G-43 | **Requested cases:** Any other live runner binding, including unclaimed/card/pool sessions, refuses. | PC-43 |
| G-44 | **Requested cases:** Pending/unreachable adoption is a binding, not abandoned work. | PC-44 |
| G-45 | **Coordination:** A conflicting readable sidecar claim refuses under the lease. | PC-45 |
| G-46 | **Coordination:** A conflicting last-pane claim refuses under the lease. | PC-46 |
| G-47 | **Requested cases:** A current standing target, including Exited AlwaysOn, requires persisted Stop. | PC-47 |
| G-48 | **Requested cases:** Current target has no active/queued launch generation or reservation. | PC-48 |
| G-49 | **Requested cases:** Standing Start/reservation cannot enter between execution's server check and runner request. | PC-49 |
| G-50 | **Requested cases:** Runner I/O stays outside database transactions. | PC-50 |
| G-51 | **Requested cases:** A current pointer to another session never authorizes stopping/reconfiguring that agent. | PC-51 |
| G-52 | **Requested cases:** Disposal refuses an active target; it never composes Stop/attach/session-kill. | PC-52 |
| G-53 | **Coordination; CARD-0213:** HerdrPaneChild.KillAsync's attached-origin guard still detaches. | PC-53 |
| G-54 | **Coordination; CARD-0213:** TryKillOrphanedChild excludes attached-origin processes. | PC-54 |
| G-55 | **Coordination; CARD-0213:** KillPendingHerdr excludes attached-origin PID killing. | PC-55 |
| G-56 | **Coordination; CARD-0213:** CloseHerdrAfterBarFailed excludes attached-origin PID killing. | PC-56 |
| G-57 | **Coordination; CARD-0213:** Ordinary detach clears Antiphon tokens and state labels only. | PC-57 |
| G-58 | **Coordination; CARD-0213:** Ordinary detach deletes its attached binding sidecar. | PC-58 |
| G-59 | **Coordination; CARD-0213:** Ordinary detach remains Detached/exit-code zero. | PC-59 |
| G-60 | **Coordination; CARD-0213:** Attached retirement never creates a launchable last-pane record. | PC-60 |
| G-61 | **Evidence; Coordination:** Attached operator panes never become allocator split capacity. | PC-61 |
| G-62 | **Evidence; Coordination:** Ordinary launched kill preserves existing foreign refusal and its own-PID fallback. | PC-62 |
| G-63 | **Identity 6:** Disposal refusal never invokes historical/own PID kill. | PC-63 |
| G-64 | **Coordination:** Disposal itself holds the exclusive pane lease through verdict and cleanup. | PC-64 |
| G-65 | **Coordination:** Attach/bind participates in the same pane lease. | PC-65 |
| G-66 | **Coordination:** Named launch/adoption participates through acquisition. | PC-66 |
| G-67 | **Coordination:** Ordinary last-pane reuse holds the same lease. | PC-67 |
| G-68 | **Coordination:** Allocator split selection/acquisition participates. | PC-68 |
| G-69 | **Coordination:** Pending adoption reacquisition participates. | PC-69 |
| G-70 | **Coordination:** Ordinary detach/kill participates where it races disposal. | PC-70 |
| G-71 | **Coordination:** Sidecar publication participates in acquisition/cleanup ordering. | PC-71 |
| G-72 | **Coordination:** Sidecar retirement/last-pane publication participates. | PC-72 |
| G-73 | **Coordination:** Workspace-key then workspace-ID then pane; no inverse acquisition. | PC-73 |
| G-74 | **Coordination:** Revalidate all claims under the acquired lease, not only at preview. | PC-74 |
| G-75 | **Evidence; Operator surface:** Disposal never splits an operator tab. | PC-75 |
| G-76 | **Evidence; Operator surface:** Disposal never ensures/creates a workspace or steals its ownership token. | PC-76 |
| G-77 | **Evidence; Operator surface:** Disposal never creates a replacement tab. | PC-77 |
| G-78 | **Evidence; Operator surface:** Disposal never renames pane/tab/agent. | PC-78 |
| G-79 | **Evidence; Operator surface:** Disposal never moves panes/tabs. | PC-79 |
| G-80 | **Evidence; Operator surface:** Disposal never types, sends keys, agent.start, or replays launch scripts. | PC-80 |
| G-81 | **Safety boundary:** Only backend empty-tab removal; never tab.close. | PC-81 |
| G-82 | **Safety boundary:** Never workspace.close, even with an Antiphon-looking label. | PC-82 |
| G-83 | **Safety boundary:** A real foreign process arriving after Antiphon's final read prevents teardown. | PC-83 |
| G-84 | **Safety boundary:** Backend guard prevents OS process-tree changes slipping between final backend validation and teardown; RPC mutex alone is insufficient. | PC-84 |
| G-85 | **Safety boundary:** Backend fences conflicting input, launch, move and teardown RPC operations. | PC-85 |
| G-86 | **Results:** Atomically persist operation intent before any destructive RPC; persistence failure refuses. | PC-86 |
| G-87 | **Results:** Receipt publication survives interruption without a partially readable terminal success. | PC-87 |
| G-88 | **Results:** Same operation/fingerprint returns or reconciles the existing receipt before preview expiry checks. | PC-88 |
| G-89 | **Results:** An operation ID cannot be repurposed for changed preview/reason content. | PC-89 |
| G-90 | **Results:** Another operation cannot consume an in-flight/uncertain preview. | PC-90 |
| G-91 | **Results:** Timeout/cancel/drop after send is Unknown with paneLeftOpen null until authoritative evidence. | PC-91 |
| G-92 | **Results:** Restart/status reconciles by backend receipt/authoritative absence, never replaying close. | PC-92 |
| G-93 | **Results:** AlreadyAbsent needs authoritative absence of the reviewed incarnation. | PC-93 |
| G-94 | **Results:** Backend unavailability before send cannot prove closure/death. | PC-94 |
| G-95 | **Results:** Pruning retains issued/Unknown and cleanupPending receipts. | PC-95 |
| G-96 | **Coordination:** Refusal/Unknown retain captured locator files. | PC-96 |
| G-97 | **Coordination:** Delete only the captured sidecar session/pane/generation/content. | PC-97 |
| G-98 | **Coordination:** Delete only the captured last-pane session/pane/generation/content. | PC-98 |
| G-99 | **Coordination:** Successful disposal never retires into a replacement last-pane. | PC-99 |
| G-100 | **Coordination:** A cleanup failure reports cleanupPending and retries only conditional deletion. | PC-100 |
| G-101 | **Coordination:** No recursive cleanup, transcript/native-history deletion or unrelated metadata clear. | PC-101 |
| G-102 | **Requested cases; Coordination:** DB-404 disposal stays independent and never creates a synthetic session. | PC-102 |
| G-103 | **Coordination:** Disposal does not emit another SessionExited for an exited representation. | PC-103 |
| G-104 | **Coordination:** Do not rewrite ProcessVanished/Detached history as PaneClosed. | PC-104 |
| G-105 | **Results:** Disposal refusals never claim ordinary kill's own-child-PID termination. | PC-105 |
| G-106 | **Operator surface:** Preview API evidence exposes names and parsed identity, never raw argv/env/home/transcript/credentials. | PC-106 |
| G-107 | **Results:** Persisted receipts contain no raw argv or secret-bearing data. | PC-107 |
| G-108 | **Operator surface:** Script preserves task headers without printing tokens, including errors/dry-run/JSON. | PC-108 |
| G-109 | **Operator surface; Results:** Server HTTP mapping retains operation identity, typed error and nullable paneLeftOpen on non-2xx. | PC-109 |
| G-110 | **Operator surface:** Script uses the server route family and configured API, never production-runner kill. | PC-110 |
| G-111 | **Operator surface; Coordination:** Supervision, reconciliation, adoption and ordinary Stop never initiate disposal automatically. | PC-111 |
| G-112 | **Operator surface; Results:** Preview count is bounded; eviction removes authorization and cannot transfer it. | PC-112 |
| G-113 | **Safety boundary; Results:** Recovery queries the persisted backend instance and operation identity, never a newly generated operation. | PC-113 |
| G-114 | **Coordination:** Changed locator/pane census notifies listeners after targeted cleanup. | PC-114 |
| G-115 | **Results:** Structured disposal logs expose no raw argv, environment, provider-home or credential values. | PC-115 |
| G-116 | **Operator surface; Results:** Status and execution receipt DTOs cannot expose secret-bearing backend evidence. | PC-116 |
| G-117 | **Coordination:** Cancellation/refusal/storage failure releases all held acquisition leases. | PC-117 |

### Positive controls

All mutations below are **temporary compiling production defects**, applied to
Code's committed implementation, with the original test/fixture/oracle unchanged.
The three L controls additionally require the isolated guarded backend's recorded
source/build. Antiphon's unconditional-close substitution and both backend fence
mutations are separate controls. Never introduce a runtime production bypass flag
for this battery.

Exact target is alias + method below; resolve aliases using the class table.
A row with several invalid vectors uses independently reported argument cases.
Run every affected vector and retain its expected assertion failure, not only the
first failure in a foreach loop. Positive companion vectors may remain green.
A redundant later refusal cannot satisfy the required earlier-layer assertion.

| PC / guard | Exact test method | Compiling defect to apply | Expected red assertion |
|---|---|---|---|
| PC-1 / G-1 | Q.`C461_G001_Exact_pane_target` | Replace exact-target validation with nonempty-string acceptance. | Each invalid target is rejected before any backend RPC. |
| PC-2 / G-2 | Q.`C461_G002_Full_expected_identity` | Accept an absent/partial/empty expected identity. | Validation refuses every bad identity and issues no executable preview. |
| PC-3 / G-3 | I.`C461_G003_Both_expected_identities` | Ignore expectedNativeSessionId when expectedSessionId matches. | Mismatched native ID is refused despite matching Antiphon association. |
| PC-4 / G-4 | D.`C461_G004_Preview_required` | Construct an executable default snapshot on preview lookup miss. | Unknown preview sends no close even when the guessed pane is absent. |
| PC-5 / G-5 | D.`C461_G005_Preview_expiry` | Remove the execution expiry predicate. | At expiry and one tick after it execution refuses as preview_expired, with zero close. |
| PC-6 / G-6 | D.`C461_G006_Preview_restart` | Persist/reload an old preview as executable after service recreation. | Old preview with a new operation refuses; new inspection is required. |
| PC-7 / G-7 | D.`C461_G007_Refused_preview` | Store a refused preview in the executable-preview store. | Submitting its ID is refused before guarded-close dispatch. |
| PC-8 / G-8 | D.`C461_G008_Immutable_snapshot` | Overlay a request-supplied replacement pane onto the stored snapshot. | Forged replacement fields are rejected or ignored; only the reviewed target can be dispatched. |
| PC-9 / G-9 | Q.`C461_G009_Reason_required` | Remove reason validation. | Whitespace and over-limit reason requests refuse without execution. |
| PC-10 / G-10 | Q.`C461_G010_No_force_bypass` | Let force=true skip eligibility. | Raw force payload on a bound target cannot execute; no close/Stop. |
| PC-11 / G-11 | P.`C461_G011_Execute_opt_in` | Remove the script's -Execute branch. | dispose without -Execute has zero execution requests, even with valid IDs. |
| PC-12 / G-12 | D.`C461_G012_Preview_read_only` | Report an antiphon token during preview. | Backend RPC delta is read-only and locator/receipt trees are byte-identical. |
| PC-13 / G-13 | Q.`C461_G013_Runner_feature_gate` | Skip the feature test at the server application boundary. | Old-runner fixture receives zero execution calls and the server returns a typed refusal. |
| PC-14 / G-14 | D.`C461_G014_Backend_feature_gate` | Treat protocol 20 or CLI version alone as guarded support. | Selected protocol-20 daemon returns guard_unavailable; zero close of either kind. |
| PC-15 / G-15 | W.`C461_G015_Distinct_guard_wire` | Serialize guarded execution as pane.close with extra fields. | Captured method is the negotiated distinct guarded method, never pane.close. |
| PC-16 / G-16 | I.`C461_G016_Positive_association` | Treat absence of a conflicting claim as association. | Missing/expired-token-only association is identity_unproven. |
| PC-17 / G-17 | I.`C461_G017_Contradictory_claims` | Take the first matching locator and ignore later contradictory claims. | Both source orders refuse and report the conflict. |
| PC-18 / G-18 | I.`C461_G018_Complete_inventory` | Coalesce incomplete inventory to an empty successful snapshot. | All incomplete-snapshot variants are identity_unproven. |
| PC-19 / G-19 | I.`C461_G019_Verified_shell` | Accept a missing/unknown shell as idle. | Unknown-shell variants cannot be eligible. |
| PC-20 / G-20 | I.`C461_G020_Valid_process_ids` | Discard malformed entries before classifying. | Malformed-PID variants refuse instead of disappearing from the ownership set. |
| PC-21 / G-21 | I.`C461_G021_Readable_creation_identity` | Convert process-identity read failure into an alive/owned result. | Denied/unavailable-creation cases are identity_unproven. |
| PC-22 / G-22 | I.`C461_G022_Exact_recorded_child_identity` | Use the existing two-minute liveness tolerance for destructive identity. | Same PID with a different creation identity within two minutes refuses. |
| PC-23 / G-23 | I.`C461_G023_Legacy_sidecar_is_not_exact` | Use LaunchedAtUtc when ChildStartedAtUtc is absent. | Legacy sidecar and last-pane cases without independent native evidence refuse. |
| PC-24 / G-24 | I.`C461_G024_Native_source_antiphon` | Accept source=antiphon metadata as native identity. | Occupied token/Antiphon-metadata-only pane refuses. |
| PC-25 / G-25 | I.`C461_G025_Native_incarnation_binding` | Accept stale non-Antiphon metadata without its process binding. | Matching native metadata from an old incarnation refuses. |
| PC-26 / G-26 | I.`C461_G026_Conflicting_native_facts` | Let matching argv override conflicting native metadata. | Conflicting native facts refuse despite one match. |
| PC-27 / G-27 | I.`C461_G027_Supported_kind` | Accept an unsupported kind when its UUID matches. | Raw/OpenCode/unknown occupied cases refuse. |
| PC-28 / G-28 | I.`C461_G028_Executable_family` | Skip executable-family comparison. | A foreign executable carrying the expected UUID refuses. |
| PC-29 / G-29 | I.`C461_G029_Exact_native_uuid` | Compare only the UUID prefix. | Same-prefix different conversation refuses. |
| PC-30 / G-30 | I.`C461_G030_Codex_positive_identity` | Accept Codex kind/cwd/token as sufficient. | Codex without either proof refuses; both valid proof arms have companion greens. |
| PC-31 / G-31 | I.`C461_G031_Single_agent_root` | Select only the first supported foreground root. | Second-root case is foreign and all processes remain alive. |
| PC-32 / G-32 | I.`C461_G032_Complete_affected_tree` | Classify only foreground roots, ignoring affected background work. | Foreign background child/sibling is foreign even with an empty foreground list. |
| PC-33 / G-33 | I.`C461_G033_No_pending_input` | Ignore the pending-input bit in idle eligibility. | Pending-input shell refuses. |
| PC-34 / G-34 | I.`C461_G034_No_pending_backend_launch` | Ignore a pending backend launch in idle eligibility. | Pending-launch shell refuses. |
| PC-35 / G-35 | I.`C461_G035_Token_is_not_process_authority` | Short-circuit occupancy when antiphon-session matches. | Matching-token plus foreign process refuses, with no disposal PID kill. |
| PC-36 / G-36 | C.`C461_G036_Shell_snapshot_change` | Ignore the shell fingerprint in execution comparison. | Changed shell PID and same-PID/new-start variants refuse as pane_changed. |
| PC-37 / G-37 | C.`C461_G037_Occupant_snapshot_change` | Ignore child PID/creation identity in execution comparison. | Both child PID and creation-time changes refuse, including same-native resume. |
| PC-38 / G-38 | C.`C461_G038_Native_snapshot_change` | Reuse the preview native identity without reinspection. | Changed current native ID refuses with zero close. |
| PC-39 / G-39 | C.`C461_G039_Pane_incarnation_change` | Compare only paneId rather than immutable terminal incarnation. | Same display ID/new terminal refuses and replacement survives. |
| PC-40 / G-40 | C.`C461_G040_Backend_instance_change` | Ignore daemon-instance identity in observation comparison. | Restarted/replaced daemon is refused before teardown. |
| PC-41 / G-41 | C.`C461_G041_Placement_change` | Skip execution placement equality. | Move to another tab and move to another workspace each refuse. |
| PC-42 / G-42 | D.`C461_G042_Own_live_binding` | Pass expectedSessionId as the live-claim exclusion. | A live binding for the requested ID returns pane_bound before close. |
| PC-43 / G-43 | D.`C461_G043_Other_live_binding` | Filter live claims to standing DB-owned agents only. | Each otherwise-unclaimed live binding returns pane_bound. |
| PC-44 / G-44 | D.`C461_G044_Pending_adoption_binding` | Ignore pending bindings when deciding eligibility. | Pending adoption refuses and retains its files. |
| PC-45 / G-45 | D.`C461_G045_Sidecar_claim` | Omit sidecar claims from execution's full claim set. | New conflicting sidecar published after preview blocks execution. |
| PC-46 / G-46 | D.`C461_G046_Last_pane_claim` | Omit last-pane claims from execution's full claim set. | New conflicting last-pane record blocks execution. |
| PC-47 / G-47 | S.`C461_G047_Persisted_stop_intent` | Treat an exited current target as implicitly stopped. | Exited AlwaysOn without persisted suspension/Stop refuses and schedules no launch. |
| PC-48 / G-48 | S.`C461_G048_No_launch_generation` | Check only persisted Stop and omit launch ownership/reservation. | Stopped target with queued/active reservation refuses at each launch boundary. |
| PC-49 / G-49 | S.`C461_G049_Standing_execution_lock` | Release the existing standing synchronization immediately after checking Stop. | Barrier-controlled Start/dispose race has one winner; no close of a newly reserved target. |
| PC-50 / G-50 | S.`C461_G050_No_transaction_over_rpc` | Move disposal RPC into the ownership transaction. | Runner callback observes zero active DB transaction; a second scoped DB operation completes. |
| PC-51 / G-51 | S.`C461_G051_Replacement_owner_untouched` | Resolve the agent by slug/cwd then invoke its Stop. | Replacement agent pointer, suspension, launch generation and process remain unchanged. |
| PC-52 / G-52 | S.`C461_G052_No_implicit_stop` | Invoke Stop to make a bound target eligible. | Stop/attach/session-kill call counts are zero and bound refusal is preserved. |
| PC-53 / G-53 | B.`C461_G053_Ordinary_attached_kill` | Remove only the attached-origin branch in HerdrPaneChild.KillAsync. | Ordinary attached Kill leaves the owned real dummy and pane alive; neither close method called. |
| PC-54 / G-54 | B.`C461_G054_Attached_orphan_guard` | Remove only that method's attached-origin return. | Attached restart orphan's real dummy remains alive. |
| PC-55 / G-55 | B.`C461_G055_Attached_pending_guard` | Remove the !attached predicate in KillPendingHerdr. | Pending attached Stop leaves real dummy alive and reports Detached. |
| PC-56 / G-56 | B.`C461_G056_Attached_failed_bar_guard` | Remove only that method's attached-origin return. | After adoption then a failed verification bar, attached dummy remains alive. |
| PC-57 / G-57 | B.`C461_G057_Detach_metadata_clear` | Skip detach's metadata clear call. | Exactly one clear is observed; unrelated metadata is unchanged. |
| PC-58 / G-58 | B.`C461_G058_Detach_sidecar_remove` | Skip detach's sidecar deletion. | Attached sidecar is absent after Stop; child/pane alive. |
| PC-59 / G-59 | B.`C461_G059_Detach_reason` | Substitute PaneClosed for the detach exit reason. | Runner result/event retain Detached and exit code zero. |
| PC-60 / G-60 | B.`C461_G060_Attached_no_last_pane` | Remove the attached-origin retirement exclusion. | Attached Stop/exit creates no last-pane and subsequent launch types nothing into the old pane. |
| PC-61 / G-61 | B.`C461_G061_Attached_not_allocator_slot` | Remove attached-origin exclusion from live-pane census. | Ordinary unpinned launch cannot split the attached sentinel pane. |
| PC-62 / G-62 | B.`C461_G062_Launched_kill_foreign_guard` | Bypass the foreign-foreground check in ordinary launched KillAsync. | No pane.close; own dummy exits, foreign dummy survives, original PaneLeftOpen reason remains. |
| PC-63 / G-63 | D.`C461_G063_Disposal_no_pid_fallback` | Call KillPidBestEffort on the locator PID on foreign refusal. | Both independently owned real dummies remain alive; PID-kill observer count zero. |
| PC-64 / G-64 | C.`C461_G064_Disposal_lease` | Omit disposal's pane-lease acquisition. | A gated competing acquisition cannot enter while disposal owns the operation. |
| PC-65 / G-65 | C.`C461_G065_Attach_lease` | Omit attach's pane-lease acquisition. | Attach-wins/disposal-wins schedules cannot both acquire the target. |
| PC-66 / G-66 | C.`C461_G066_Named_launch_lease` | Omit named launch's pane lease. | Both winner schedules preserve the winner's pane and binding. |
| PC-67 / G-67 | C.`C461_G067_Last_pane_reuse_lease` | Omit last-pane reuse's pane lease. | Reuse cannot type/adopt during gated disposal; reuse winner forces refusal. |
| PC-68 / G-68 | C.`C461_G068_Allocator_split_lease` | Release the pane lease before split acquisition. | Disposal and split cannot operate concurrently on the selected pane. |
| PC-69 / G-69 | C.`C461_G069_Pending_adoption_lease` | Omit pending adoption's pane lease. | A pending sweep cannot bind during disposal and its winning binding forces refusal. |
| PC-70 / G-70 | C.`C461_G070_Detach_kill_lease` | Release its pane lease before the terminal/locator transition. | Disposal observes either the complete live state or complete detached state, never a mixed eligible state. |
| PC-71 / G-71 | C.`C461_G071_Sidecar_publication_lease` | Publish the sidecar after releasing acquisition's pane lease. | Disposal cannot remove/overlook a binding published after its claim check. |
| PC-72 / G-72 | C.`C461_G072_Retirement_lease` | Retire and publish last-pane outside the pane lease. | Disposal cannot complete and then acquire a newly published stale locator. |
| PC-73 / G-73 | C.`C461_G073_Lock_order` | Acquire the outer workspace lock while holding pane lease. | Lock trace asserts the declared order before any watchdog; reversed two-actor schedule cannot deadlock. |
| PC-74 / G-74 | C.`C461_G074_Claims_rechecked_under_lease` | Use the pre-lock claim snapshot at execute. | Claim inserted between initial resolution and lease acquisition prevents close. |
| PC-75 / G-75 | D.`C461_G075_No_split` | Call pane.split on the reviewed pane during disposal. | Operation RPC delta contains zero pane.split; sibling layout/process unchanged. |
| PC-76 / G-76 | D.`C461_G076_No_workspace_ensure` | Call EnsureWorkspaceAsync with an unmatched placement key/label during disposal, causing workspace.create. | No create/ownership-refresh RPC; operator workspace tokens and root remain byte-identical. |
| PC-77 / G-77 | D.`C461_G077_No_tab_create` | Invoke tab.create on refused disposal. | No tab.create and exact tab census unchanged on refusal. |
| PC-78 / G-78 | D.`C461_G078_No_rename` | Rename the reviewed pane before close. | No rename RPC; target/sibling labels unchanged on refusal. |
| PC-79 / G-79 | D.`C461_G079_No_move` | Invoke a placement move before guarded close. | No move RPC and operator placement unchanged. |
| PC-80 / G-80 | D.`C461_G080_No_input_or_start` | Send a newline to the reviewed pane before closing. | Full mutation allowlist rejects input; sent-input and launch-script execution counts zero. |
| PC-81 / G-81 | D.`C461_G081_No_tab_close` | Call tab.close after successful target close. | No tab.close RPC; same-tab sentinel remains alive. |
| PC-82 / G-82 | D.`C461_G082_No_workspace_close` | Close the workspace after target close. | No workspace.close RPC; other tab and workspace sentinels survive. |
| PC-83 / G-83 | L.`C461_G083_Foreign_after_runner_inspection` | Replace guarded RPC with supported unconditional pane.close at the same barrier. | Foreign process successfully started before teardown, pane and foreign PID remain alive on fixed code; mutant kills/removes them. |
| PC-84 / G-84 | L.`C461_G084_External_tree_race_at_teardown` | Disable only the backend's process-start/tree fence or move validation before acquiring it. | Real external starter wins the vulnerable interval on mutant and survival assertion fails; fixed run proves the fence and refusal schedule below. |
| PC-85 / G-85 | L.`C461_G085_Backend_rpc_fence` | Remove the backend pane-operation fence while retaining process checks. | Conflicting RPC cannot commit between guard validation and close; replacement/sentinel survives. |
| PC-86 / G-86 | D.`C461_G086_Intent_before_rpc` | Send guarded close before the intent write. | At backend entry durable intent exists; denied write issues zero destructive calls. |
| PC-87 / G-87 | D.`C461_G087_Atomic_receipt` | Overwrite the receipt directly instead of atomic replacement. | Reader/crash fault at mid-write sees old valid intent or complete new receipt, never truncated success. |
| PC-88 / G-88 | D.`C461_G088_Same_operation_idempotent` | Require a live preview before looking up an existing operation. | Retry after TTL/restart returns the same result with no second teardown. |
| PC-89 / G-89 | D.`C461_G089_Operation_conflict` | Skip fingerprint comparison on an existing operation. | Changed-payload retry is operation_conflict; stored receipt and close count unchanged. |
| PC-90 / G-90 | D.`C461_G090_Preview_single_consumer` | Remove atomic preview consumption ownership. | Concurrent different operation IDs for one preview dispatch at most one close. |
| PC-91 / G-91 | D.`C461_G091_Unknown_after_send` | Convert post-send cancellation/timeout into Closed. | 503 outcome_unknown carries operationId and null paneLeftOpen, not success. |
| PC-92 / G-92 | D.`C461_G092_No_destructive_replay` | Retry guarded close during unknown-operation recovery. | After each crash boundary status issues no destructive request; backend teardown count stays at most one. |
| PC-93 / G-93 | D.`C461_G093_Incarnation_absence` | Treat display-ID missing/reused or missing backend receipt as old-incarnation absence. | Ambiguous backend evidence remains Unknown; replacementPresent is separate and replacement survives. |
| PC-94 / G-94 | D.`C461_G094_Unavailable_is_not_absent` | Convert backend connection failure into AlreadyAbsent. | 503 unreachable, zero close, locators retained; no terminal closure assertion. |
| PC-95 / G-95 | D.`C461_G095_Unresolved_retention` | Apply seven-day TTL to all receipts irrespective of state. | Old unresolved and cleanup-pending operations remain queryable; only eligible old terminal receipts prune. |
| PC-96 / G-96 | D.`C461_G096_Cleanup_after_confirmation` | Run locator cleanup in the execution finally block. | Each refusal and unknown variant retains exact sidecar/last-pane bytes. |
| PC-97 / G-97 | D.`C461_G097_Sidecar_generation_cleanup` | Delete sidecar by session ID without compare-before-delete. | Same-session newer sidecar, same-pane new generation and other-pane record survive. |
| PC-98 / G-98 | D.`C461_G098_Last_pane_generation_cleanup` | Delete last-pane by session ID without compare-before-delete. | Newer/reassigned last-pane survives while only exact captured generation can disappear. |
| PC-99 / G-99 | D.`C461_G099_No_cleanup_last_pane_write` | Call Retire instead of targeted cleanup after disposal. | Closed/AlreadyAbsent produces no new last-pane for either origin. |
| PC-100 / G-100 | D.`C461_G100_Cleanup_retry_only` | Clear cleanupPending or re-close when cleanup retry fails. | Failure remains visible; restored cleanup succeeds without a second close, even with reused pane ID. |
| PC-101 / G-101 | D.`C461_G101_History_and_unrelated_files` | Recursively delete the target session directory during cleanup. | Captured unrelated files and transcript/native-history sentinel hashes are unchanged. |
| PC-102 / G-102 | S.`C461_G102_No_synthetic_session` | Insert a session row for the orphan before disposal. | Session lookup remains 404 and scoped DB row count unchanged after successful disposal. |
| PC-103 / G-103 | D.`C461_G103_No_duplicate_exit` | Publish SessionExited after confirmed disposal. | Event subscriber sees no additional SessionExited; census change notification still arrives. |
| PC-104 / G-104 | S.`C461_G104_Preserve_exit_history` | Update the historical exit reason after disposal. | Original status/reason/termination-source and transcript rows remain identical. |
| PC-105 / G-105 | S.`C461_G105_No_false_PaneLeftOpen_incident` | Reuse HerdrExitReasons.PaneLeftOpen incident production for disposal refusal. | No killed-our-child/PID-kill incident or new alert; separate disposal receipt/log exists. |
| PC-106 / G-106 | Q.`C461_G106_Preview_response_redaction` | Include raw process argv in preview evidence DTO. | Synthetic secret/home/transcript markers absent from the serialized preview response. |
| PC-107 / G-107 | D.`C461_G107_Receipt_persistence_redaction` | Serialize raw process snapshot into the receipt payload. | Synthetic confidential markers absent from persisted receipt bytes. |
| PC-108 / G-108 | P.`C461_G108_Script_redaction` | Print the task header in the dry-run summary. | Synthetic header-token marker absent from stdout/stderr in all three script modes. |
| PC-109 / G-109 | W.`C461_G109_Typed_status_receipt` | Discard operationId when mapping the runner's 503 problem. | Public response/status round trip retains exact operationId, code, outcome and null. |
| PC-110 / G-110 | P.`C461_G110_Server_route_only` | Post disposal to /sessions/{id}/kill instead of /api/herdr/pane-disposals. | Stub receives exact server disposal route/body and zero session-kill requests. |
| PC-111 / G-111 | S.`C461_G111_Explicit_disposal_only` | Call the disposal executor from a terminal-session supervision path. | Ticks and ordinary Stop leave disposal-dispatch count zero; only explicit POST increments it. |
| PC-112 / G-112 | D.`C461_G112_Bounded_preview_store` | Remove the preview-capacity enforcement branch. | Store count never exceeds configured capacity; an evicted preview cannot execute or target its replacement. |
| PC-113 / G-113 | D.`C461_G113_Queryable_backend_identity` | Generate a new backend operation ID during status reconciliation. | After dropped reply the queried identity equals durable intent and recovers the original result without another close. |
| PC-114 / G-114 | D.`C461_G114_Census_notification` | Remove the disposal census-change notification. | A subscribed real listener observes the changed census exactly for the target; no duplicate SessionExited. |
| PC-115 / G-115 | D.`C461_G115_Structured_log_redaction` | Log the raw observed process snapshot on a refusal. | Captured structured log properties and formatted messages contain none of the synthetic confidential markers. |
| PC-116 / G-116 | Q.`C461_G116_Receipt_response_redaction` | Include raw backend evidence in the status receipt DTO. | Execution/status response bytes contain no synthetic argv/env/home/transcript/credential marker. |
| PC-117 / G-117 | C.`C461_G117_Lease_release_on_failure` | Skip pane-lease disposal on the cancellation branch. | After cancellation a second owned operation acquires the pane and completes within the bounded fixture deadline. |

Mutation must first verify Code's reported SHA equals HEAD, clean tracked source/
index and no running Code command. Record untracked outputs; do not reset them.
For **each of the 117 controls**: run exact method baseline green, apply the
specified defect, build, execute exact method red at the intended assertion,
restore exact fixed bytes, refresh timestamps/force rebuild and execute that same
method restored green. Both red/green must have fresh nonzero execution counts.
A build failure, fixture failure, timeout, skip, stale binary or zero selected
tests is not detection. Record per argument-case outcome, method, mutation diff,
tested SHA (both repos for backend controls), binary identity, assertion and paths
to red/restored-green results. Keep baseline evidence too.

Controls sharing a production file/method or backend/state dependency run
separately. Independent different-file/different-method controls may be batched
only with exact method filters and independent per-PC evidence. No class,
namespace or suite execution for a PC cycle. Local sharding is optional under
the testing guide, but process/backend races remain isolated and serial; this
does not authorize sub-delegation. Restore every mutant and await all owned runs.
Any needed production/test repair returns to Code for V/R and a fresh Mutation
pass. Mutation retains only evidence/design amendments after restoration.

### Out of scope

- No production cleanup, stack restart, deployment, live owner/channel switch or
  historical CARD-0461 pane operation is part of TestDesign or the battery.
- No new UI, sweeper, tracker action, bulk deletion, migration, workspace matching
  policy, provider-native-session deletion or input delivery feature. Explicit-only
  disposal is itself protected; these are not shortcuts around the safety gates.
- Client/Vitest and Antiphon.Agents.Pty.Tests are not part of this unchanged-UI,
  Herdr-specific floor. Expand only for actual code changes; never run the latter
  concurrently with Antiphon.Tests. Existing Herdr event/delivery/launch behavior
  is not redefined; if touched beyond lease participation, add its owner's V/R/PCs.
- A preview/refusal-only increment can be tested with protocol 20 but cannot
  complete V-1/V-9 or CARD-0461. Missing guarded-backend evidence stays outstanding,
  not an exclusion or waived cost.

### Cost

**Mandatory planning floor: 479 minutes (7 h 59 min), estimated, not measured.**
It is a budget floor for the full acceptance battery, not a timer to pad and not
a cap: faster measured runs do not require waiting, and elapsed budget never
authorizes skipping a guard. It excludes writing tests/implementation and any
waiting for the backend owner. All named V/R and every PC are mandatory.

| Owner / component | Required suite/filter scope | Estimated minutes |
|---|---|---:|
| Shared setup/build | Build the two affected test projects into `bin-c461/`; prepare isolated DB/runner and guarded backend build/fence instrumentation; record SHAs and owned resources | 30 |
| Code V-1..V-8 | Eight new classes excluding L, ordinary class-scoped execution; includes all I/D/C/B/S/Q/W/P guard methods and integration/script/crash vectors | 25 |
| Code R-1..R-8 | Nine distinct existing classes listed above, class-scoped, sequential project runs | 15 |
| Code V-9 | Five exact L methods, real success/recovery plus race schedules (at least 20 acknowledged trials per foreground/background schedule) | 35 |
| Mutation: 114 non-live PCs | Per PC: baseline 0.25 + mutation/build/red 1 + restoration/build/green 1 + patch/evidence handling 0.5 = 2.75 | 313.5 |
| Mutation: three live PCs | Per PC: baseline 6 + mutant build/red 6 + restored build/green 6 + isolated-backend/evidence handling 2 = 20 | 60 |
| Final inventory/restoration audit | Confirm 117/117 guard mappings, per-PC evidence and restored source/backend hashes | 0.5 |
| **Total** | **Setup 30 + Code ordinary V/R 75 + Mutation PCs 373.5 + final audit 0.5** | **479** |

Run each ordinary class in its project using this concrete form (substitute only
an exact class from the tables), with a separate fresh result filename:

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c461/ -- --treenode-filter "/*/*/HerdrPaneDisposalIdentityTests/*" --report-trx --report-trx-filename c461-v2.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c461/ -- --treenode-filter "/*/*/HerdrPaneDisposalApplicationTests/*" --report-trx --report-trx-filename c461-v3.trx
```

For a PC, run only its exact method, once per phase with fresh result names, e.g.:

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c461/ -- --treenode-filter "/*/*/HerdrPaneDisposalIdentityTests/C461_G035_Token_is_not_process_authority" --report-trx --report-trx-filename c461-pc35-red.trx
```

Use `pwsh -NoProfile` for script tests; headed opt-in
`ANTIPHON_HEADED_TESTS=1` is necessary but never substitutes for the isolated
fixture's explicit daemon checks. These L methods run with the same exact filter
form and `[NotInParallel("Headed")]`. Mutation carries the original Code task ID,
retained worktree/branch and SHA, backend build/source identity, this artifact,
`review-required: yes`, and restart target `server + runner + guarded Herdr`
as handoff metadata; it does not restart shared services or land its Shared task.

**Savings quantified:** at the guide's approximately 25.5-minute full
Antiphon.Tests duration, baseline/red/restored-green full-suite runs for even the
114 non-live controls alone would cost 8,721 minutes. Method-scoped execution's
313.5-minute allowance saves an estimated 8,407.5 minutes (96.4%) on those cycles.
This is a broad-suite comparison, not a measured runtime guarantee for these new
tests; the mandatory real-backend cost is not removed. Record measured per-PC/
ordinary totals during execution and revise the estimate with evidence.

Design audit: **guards=117, mapped=117, missing=0, duplicate PC mappings=0**.
All controls have a defined class/method, compiling defect, expected assertion,
fixture/setup contract and cost allocation. Test implementation and guarded
backend availability remain Code/backend prerequisites, not completed evidence.
Land this verification document before the next sibling Worktree dispatch.
Code reports ordinary V/R results and PCs pending, then routes the complete
battery to **Mutation** in its retained worktree; Mutation routes to **Review**
after all controls and missing-control discovery succeed.
