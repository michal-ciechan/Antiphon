# CARD-0824: all eligible roles, reusable runner slots and ref-only desktop halves

Date: 2026-11-14. Plan owner: `8e511694`. Source and freshly fetched master:
`4c5693a3f4dc935f78c074a18b670b0b5fce5b15`. Assigned branch:
`feat/card-task-8e511694`. This is a plan and folded verification design, not an
implementation or a live configuration change.

**Outcome:** activate existing runner slots in a bounded first wave at the next
already-required rc restart, finish the ref-only producer, then widen to Code,
Deploy and Merge as their gates pass. Preserve Mutation/SourceLanding's exact-SHA
verification snapshots. There is no setting that safely makes those snapshots
ordinary slots. The remaining interpretation of “all roles” is D-1 below.

The November 14 operator instruction authorizes enablement; November 5 already
decided ref-only desktop halves. Neither decision needs another go-ahead. This
commission is read-only on live systems: the caller performs activation. The
wave-1 settings below are ready to apply after the listed operational prechecks.

This plan replaces the old role waves and decomposes the remaining S5b-3 work in
the [November 5 ref-only plan](2026-11-05-card-0824-s5b-ref-only-plan.md). Its
durable identity, CAS settlement, consumer and custody contracts remain binding.
The [November 3 amendment](2026-11-03-card-0824-s4-s5-amendment.md) still owns the
S1 acceptance envelope and S4b intent; this plan makes its remaining work smaller.
Desktop pools S5a/S5c and the folded S3 are outside this rollout. S2 remote branch
deletion is independent and must not be enabled as part of a slot flag change.

## Evidence and ground truth

Read in full first: investigation
`docs/investigations/2026-11-14-card-0824-slot-enablement.md`, on
`origin/feat/card-task-9168c199`, commit
`84736ca47c37cbd8da3c7df3b2408694fcb32e9a`. Its call graph and
Git reconstruction were checked against the source above. CARD-1901's card
decision accepts D-1..D-8 of
`docs/superpowers/plans/2026-11-14-card-1901-retirement-audit-plan.md`, on
`origin/feat/card-task-2c3ecf9c`,
`3ef663e98d51e3063614207fcf22343b5b7e3fe9`; the implementation was still in
flight, not credited as active.

Fresh GETs at **08:32 UTC**: `/api/runner-defaults` revision 2 selects server2,
with no kind overrides. `/api/session-runners` has an eligible Linux runner
with capacity 10, occupied 7, retention/branch-ref support and no
`workspaceSlotV1`; an eligible Windows peer has capacity 2, occupied 0; temp is
unavailable. `/api/version` is
`ab7af12c699039270af7e22a5d7fd794035bc325`, not the plan baseline. Refresh both
placement routes at dispatch. These are observations, not compiled placement
constants. Checkpoints name their lane; omit `-Runner`, and omit `-Platform` for
portable work. Use `-Platform Linux` for native runner process tests and
`-Platform Windows` for the desktop producer witness. `-Platform Any` unpins.
Pin `-Runner server2` only for the explicitly chosen operational canary.

| Card assumption | Ground truth at the baseline | Consequence |
|---|---|---|
| Slots just need one flag. | Server `Delegation:WorkspaceSlotsEnabled` and runner `PhoneHome:WorkspaceSlots:Enabled` default false; the runner is observed false, with no capability/ledger. Effective desktop/server overrides cannot be read through the API. | Read only the named owning-host configuration values before activation; do not call an unknown value false. Restart both owners once at the due rc cut. |
| `*` means all fourteen roles. | `WorkspaceSlotRoles.cs` excludes Mutation; literal Mutation fails validation. `RemoteWorkspaceService.IsEligible` also excludes SourceLanding independently. | Use explicit lists. Preserve exclusions; document D-1. |
| A slot directory is under `/work/workspace-slots`. | `RunnerWorkspaceSlotPool` stores the ledger there; `NewSlot` uses `RunnerWorkspaceService.WorktreeRoot`, observed as `/work/worktrees/ws-NN` or `ws-NN-dK`. | Inspect both roots. Do not move checkouts to the ledger directory. Code derives paths from configuration. |
| The desktop producer is ref-only already. | `RefOnlyDesktopGate` and `TaskWorkspaceContextResolver.RecordPlannedAsync` have no production admission caller. `AgentTaskDispatcher.cs:6083-6125` still calls `CreateForTaskAsync`. | Runner-slot wave 1 still creates desktop halves. S5b-3 is required to stop that growth. |
| Wiring the gate alone completes S5b-3. | `CaptureProgressBaselineAsync` still reads WorktreePath/HEAD and writes schema 1. `PrepareRepairSourceAsync` requires a registered checkout. `PushRefOnlyBranchAsync` requires schema 2 and `InspectAsync` requires Published state. | Add a separate initial provisioning state machine; do not try to bootstrap by calling the existing already-Published sync strategy. |
| Existing S5b consumers can be assumed qualified. | Identity, sync, debt, deliverable, continuation and landing classes exist; stored prior reports contain incomplete acceptance. | Reuse exact trustworthy receipts or run the named missing rows before producer activation. No wholesale rewrite of landed consumers. |
| All task workspaces share ordinary settlement. | `DelegationWorktreeService.cs:381-403` creates SourceLanding directly on the runner at confirmed landing SHA; `stage-mutation.md:3-11` forbids snapshot publication. | Mutation uses its single-purpose snapshot and external evidence/restoration record. |
| Equivalent published Merge content makes release safe. | `RunnerWorkspaceSlotPool.UnpushedAsync` examines HEAD, its reflog and slot.Branch using origin tracking refs. Replaced originals remain distinct commits and quarantine. | Exact origin reachability, including approved salvage, must satisfy custody; patch equivalence is diagnostic only. |
| Release removes every Merge ref. | `ResetForFreeAsync` deletes only slot.Branch and its tracking ref, without an auxiliary-ref ownership manifest. Shared repo refs survive the lease. | Track one runner-created auxiliary Merge ref per lease; delete only its proved owned refs with expected-old values. |
| Lease reset repairs a re-cut inside one Merge task. | Initial lease resets to the selected SHA; one lease lasts the whole task. | A re-cut explicitly resets its own cut branch after preserving original commits. It never relies on the next lease. |
| Deploy gains canonical-host authority from a slot. | A slot remains a linked checkout. `restart-apphost.ps1:94-100` refuses it; `deploy-server2.ps1` documents canonical-only privileged provisioning with no override. | Run host-changing commands through the authorized canonical control checkout. No guard relaxation. |
| Seven/eight tasks imply seven/eight workspaces. | Target is capacity + headroom: 10 + 4 = 14 lazily created base slots, plus at most 4 overflow. Blocked and settlement-held leases outlive seats. | Keep defaults, measure occupancy by lease, and hold beyond the bound. |
| A 64 GiB pool budget caps all disk consumption. | It guards overflow creation only. Base growth and subsequent bin/obj growth are not capped by it; quarantines and retained evidence add bytes. | Monitor free space and real size distribution; do not preallocate 18 trees or promise a hard 64 GiB ceiling. |
| Retention and retirement make slots disappear. | CARD-1677 excludes slot ids/leaves/ledger ownership. CARD-1894 is on master but not the observed running SHA. CARD-1901 changes the independent temp/v2 audit, not slot release. | Activate the paid-debt repair at the due cut; keep retirement's fresh proof and refusal gates. |
| Slot inventory/attention is available. | S4b op/route and attention consumer are absent. The ledger has attention entries. | Staff wave-1 ledger reads; land I1-I3 before widening beyond wave 1. |

## Decisions

- **D-1 — Target all ordinary eligible remote Worktree roles.** That is thirteen
  roles, with Mutation and every SourceLanding-bound task retaining verification
  snapshots. Recommend recording this as the meaning of “all roles.” The reason
  is custody, not a temporary role-policy limitation. Reject deleting the
  exclusion or publishing a verification snapshot merely to claim fourteen.
  Local desktop execution stays on its existing workspace until S5c is separately
  commissioned; ref-only removes the desktop half of remote tasks.
- **D-2 — Wave 1 is an explicit ten-role list.**
  `Investigate,Plan,TestDesign,Docs,Coverage,Test,Review,Debug,Custom,Commit`.
  Start with an actual Plan task and observe release/reuse before admitting this
  whole list. Some helpers build or commit; “read-mostly” describes the initial
  task selection, not weaker custody. Reject `*` at startup because it also
  admits Code/Merge/Deploy before their readiness gates.
- **D-3 — Preserve H-2 bounded overflow, then hold.** Keep capacity unchanged,
  Headroom 4, MaxOverflowSlots 4 and the existing disk guard. Do not silently
  create a legacy mirror on slot exhaustion, evict a Blocked lease, or change the
  host/build budgets to achieve seven/eight simultaneous tasks.
- **D-4 — Ref-only is independently gated and ships OFF.** Existing settings
  remain `Enabled=false`, `RunnerIds=[]`, `Roles="none"`. Initial activation is
  server2 only, first Plan, then the ten-role list, after S5b-1/2/3 and Windows
  evidence. Persist mode before Git; no fallback checkout after selection.
  Reject desktop slots, empty directories and sparse checkouts: they retain the
  registration and filesystem churn the operator chose to remove.
- **D-5 — Merge publication is exact and fresh.** Ordinary origin heads plus
  canonical `refs/antiphon/salvage/<lowercase-D-GUID>/<40-lowercase-hex-OID>` may
  prove a commit only with an advertised matching OID, commit type, bound origin
  endpoint and freshly fetched graph. Preserve every original before release.
  Reject patch-id/tree equality, local salvage, stale tracking and blanket reflog
  expiry. No automatic publisher is added to the pool. Publication is an
  explicitly commissioned Merge/recovery action; otherwise preserve and hold.
- **D-6 — Auxiliary Merge refs have durable lease ownership.** Prefer the task
  branch alone where the workflow permits. Where a re-cut needs a separate
  branch, the runner creates exactly one owned ref whose name includes full task
  id and lease generation, journals its creation intent before Git, and records
  it in the lease. Reject arbitrary `merge-*` glob deletion, naming alone as
  proof, and adopting pre-existing refs. Cleanup is local and CAS-protected;
  remote source/salvage refs are never deleted by slot release.
- **D-7 — Observation precedes broader admission.** S4b inventory and attention
  are not required to start a staffed wave 1, but are required before Code,
  Deploy or Merge widening. Preserve the existing 48h/40-release acceptance and
  12-hour observations; do not turn an absent route into an empty-pool signal.
- **D-8 — Deploy remains a host operation.** Its task may occupy a slot, but
  privileged commands use the independently resolved canonical checkout and
  explicit host/source identity. A script that cannot support that arrangement
  stays on a suitable canonical execution lane. No `-AllowWorktree`, new bypass
  or general restart-script change is planned.
- **D-9 — Keep the existing settlement CAS and waiting rules.** Ref-only changes
  representation, not ancestry, endpoint, sync-debt or release authority. A
  Blocked task has no automatic release deadline while parking is disabled.
  Reject timers or cancellation masquerading as disk management.
- **D-10 — Small code units, ordinary Review, then SourceLanding PCs.** Code
  slices below budget 30-60 minutes of authoring each; builds/queues are separate.
  Keep assertion-red evidence before fixes. Unimplemented APIs may be scaffolded
  to compile, but a setup/compile failure is never the behavioral red. No live
  canary or production volume is used as a test fixture.

## Wave 1: no-code enablement at the due rc cut

### Owning settings and one restart per owner

The caller reads provider precedence for only these non-secret keys; never dump
the server secret store or stack environment. There is no generic settings API
for them. The observed control plane still uses desktop coordinates. Verify the
actual owner immediately before applying; do not assume the Linux-server cutover.

| Owner | Exact setting / initial value | Application |
|---|---|---|
| API server | `Delegation:WorkspaceSlotsEnabled=true` | Restart-bound. On the desktop use the `antiphon-server` user-secret key or merge the boolean into server `appsettings.Development.json`; AppHost secrets are a different store. |
| API server | `Delegation:WorkspaceSlotRoles="none"` while rolling; then `"Plan"`; then D-2's exact ten-role string | Put the scalar in reloadable server Development JSON, with no same-key secret/environment override. Never an array. Subsequent role widening/rollback reloads without restart on this owner. |
| Standing runner | `PhoneHome__WorkspaceSlots__Enabled=true` | One exact LF assignment in `secrets/stack.env` under the deployment root, before its already-required `redeploy-old` phase. The container must be recreated; file editing alone does nothing. |
| Temporary runner used by the due rollout | `PhoneHome__WorkspaceSlots__Enabled=true` in its own `secrets/stack.temp.env` | Apply before `deploy-temp` if temp will carry slot work. Keep server roles `none` through the rollout so first acceptance samples do not span a drain. Do not revive a retired temp outside the normal named phases. |
| Ref-only gate | `Delegation:RefOnlyDesktop:Enabled=false`, `RunnerIds=[]`, `Roles="none"` | Keep off until F1-F4 and acceptance prerequisites pass. Wave 1 does not depend on the producer. |

Merge these fields into the existing reloadable file, preserving other values:

```json
{
  "Delegation": {
    "WorkspaceSlotsEnabled": true,
    "WorkspaceSlotRoles": "Investigate,Plan,TestDesign,Docs,Coverage,Test,Review,Debug,Custom,Commit"
  }
}
```

For the observed desktop owner, the explicit flag command is
`dotnet user-secrets set 'Delegation:WorkspaceSlotsEnabled' 'true' --id antiphon-server`.
Do not set the role string there: it would shadow later JSON changes. Resolve any
existing shadow by migrating that one named setting under the caller's enablement
authority, preserving the effective value until the planned reload.

Piggy-back on the next already-required rc activation: prepare these values before
the AppHost/server restart and before the runner's normal rolling recreation.
There is **one due restart per owner**, not one restart total for two processes,
and no additional restart just for widening roles. From the canonical desktop
checkout, use `scripts/restart-apphost.ps1 -ExpectedServerSha <active-rc-full-sha>`
after its lock/HEAD/land checks. If the API has moved to Linux, use
`scripts/restart-linux-server.ps1` instead. The standing server2 runner uses
`scripts/deploy-server2.ps1 -Rolling -Sha <reviewed-rc-full-sha>` with the named
deploy-temp, drain-old, redeploy-old, drain-temp, retire-temp gates. Do not restart
a busy runner directly. Include CARD-1894's landed paid-debt repair in that rc cut.
Confirm API version, runner buildVersion/store/boot identity and advertised
`workspaceSlotV1`, rather than inferring activation from health.

**Reload boundary:** today's Docker API server has no reloadable settings file;
its `Delegation__WorkspaceSlotRoles` environment is restart-bound. If cutover
precedes this rollout, role reload without restart is not available in current
code/deployment. Use that owner's graceful restart for role changes, or separately
commission a reloadable configuration provider. Do not promise JSON hot reload
where no file is loaded. This does not block the observed desktop procedure.

### First task, release and reuse

Before the first task: finish the rolling phase, re-read placement, check at least
84 GiB free, verify the existing S1 custody-critical Mutation disposition from the
amendment and active release/drain fixes, read the effective values above, and
assign a canary observer. Missing historical acceptance is an evidence gate, not
permission to repeat all tests or to declare them green.

1. Reload roles to `Plan`. Dispatch the next already-authorized ordinary Plan
   Worktree task on the chosen runner. Use a fresh ordinary card/base where
   possible to avoid CARD-1349's long same-card sibling walk. No fake work/spend
   is required just to fill the pool.
2. Require the task's persisted `WorkspaceSlotDecision` to say slot, admitted
   role and preparation id. Match task/preparation, runner store, slot id,
   directory/lease generation, exact base SHA and task branch to the ledger.
   Observed paths should be `/work/workspace-slots/ledger.json` and
   `/work/worktrees/ws-*`, derived from the configured root. A session-seat
   `/slots` response is not workspace inventory.
3. On completion, require the session to stop, sync to confirm, and no live
   follow-up before release. The one-minute reconciler is a polling cadence,
   not a guaranteed deadline. Observe released timestamp, cleared remote cwd,
   Free/detached state at the published SHA, removed runner-local task branch
   and tracking ref, and no leftover task inbox/hook/sequencer/source dirt.
   Preserve the origin task branch for ordinary land and late readers.
4. A second eligible task reuses the same directory with incremented generation
   and its own exact base/branch. For warm-output evidence use a later
   authorized Test/Coverage task which actually builds in native bin/obj; record
   hashes/mtime of selected outputs before release and on the next warm lease.
   A Plan task with no build output proves release, not build reuse. Checkpoint
   cold outputs remain separate and are never presented as incremental reuse.
5. After the first release/reuse pair is clean, reload the exact D-2 list. Retain
   at least **48h and 40 released leases**, at least 20 measured warm resets,
   and an explicitly requested cold reset of one Free slot within the first
   24h. Cold reset must not overlap the working lease or an rc fence. Observe
   ledger, retained evidence, disk, latency, release backlog and all retention
   run pages every 12h and at the first two releases. Record counts by role;
   every admitted role needs an actual-task witness before claiming its rollout
   complete, using naturally arriving authorized work.

Success means exact source identity, no leakage/lost work, custody-safe release,
branch cleanup, stable bounded occupancy and warm native output survival. Keep
startup/lock-wait/fetch/reset/verify/release timings separate. Warm reset target
p95 <=15s; >30s, a lease RPC timeout or unavailable hold >10min stops widening.
Also stop for any source leakage, missing reported pushed commit, reset after
failed publication inspection, release-stuck/rehome-failed/pool-degraded, more
than two custody quarantines in 24h, retention touching a slot, Held sync debt,
or Pending sync debt older than one hour. Preserve evidence and commission Debug.

**Soft rollback:** set server `Delegation:WorkspaceSlotRoles="none"`; leave both
support flags true. Confirm a newly decided preparation records `reason=roles_none`.
Already persisted slot decisions may still complete as slots. Running tasks are
not killed and the release reconciler continues. Never clear the ledger or
delete a slot. Disable support flags only after all leasing/leased/releasing and
in-flight preparations are drained, through their owning restart paths. With
support off, the server release loop or runner release RPC is unavailable.

## Implementation slices and order

The authoring estimates exclude checkpoint/queue time. Each slice commits and
pushes its real state; one coherent group is reviewed before land. Keep F1-F4
serial because they touch the same dispatcher/provisioner. I1-I3 and M1-M3 also
share phone-home/pool files: serialize those edits; they may run beside the F
track only with disjoint claims. The operator's Code concurrency limit remains
independent of workspace capacity.

| Slice | Code authoring | Files and concrete result | Tests / exit |
|---|---:|---|---|
| F1, frozen intent | 45-60 min | `DelegationWorktreeService.cs`, `TaskWorkspaceContext.cs`, new `RefOnlyDesktopProvisioner.cs` under `server/Application/Services/`; extract the existing base/branch selection without checkout creation. Save task mode/binding/selected SHA before Git. Register service in `server/Program.cs` and shared `DelegationTestServices`. Gate stays off. | CP-1/CP-16/CP-25; four Provision methods and legacy base/lease callers. |
| F2, initial local/remote publication | 45-60 min | `RefOnlyDesktopProvisioner.cs`, `RemoteWorkspaceService.cs`, existing binding state/revision; implement Planned -> RefCreated -> Published with owned ref CAS, exact origin observation and retry recovery. Keep already-Published settlement strategy separate. | CP-2/CP-26; four Preparation methods and legacy sync callers. |
| F3, production dispatch | 45-60 min | `AgentTaskDispatcher.cs`, `RemoteWorkspacePreparer.cs`, `DelegationReportFormatter.cs`; call gate only for a new ordinary workspace, commit preparation then leave the claim before Git/push, re-enter by owned attempt. Mode-aware schema-2 baseline; remote RunnerCwd/brief only. No missing-binding fallback. | CP-3/CP-4 and impact CP-14/CP-15/CP-16; two additional Provision and two Preparation methods. First dispatcher acceptance must fail on the checkout-creating base before its fix. |
| F4, repair/resume/native closure | 45-60 min | Dispatcher repair-base and resume/boot-wedge paths; `docs/session-runtime-invariants.md`, `docs/testing-and-build.md`; production-caller lifecycle and actual Windows witnesses. Repair source binds owned ref instead of requiring a checkout. Preserve legacy recovery refusal. | CP-5/CP-6/CP-7, native CP-8; finish previous S5b-1/2 acceptance debt before producer activation. |
| I1, slot inventory | 45-60 min | `WorkspaceSlotContracts.cs`, `PhoneHomeContracts.cs`, `PhoneHomeCommandDispatcher.cs`, `RunnerWorkspaceSlotPool.cs`; read-only bounded inventory op and snapshot, including quarantines/attention cursor/measurement time. Introduce a separate inventory capability so old `workspaceSlotV1` peers are not mistaken for inventory-capable peers. | CP-9; I1-I5. No deletion operation. |
| I2, observation and attention | 45-60 min | New `WorkspaceSlotInventoryPollService.cs` beside `HostStatsPollService.cs` in `server/Application/Services/`, runner client plumbing, `AttentionService.Alarms.cs`, `AttentionDtos.cs`, Program registration; poll on existing cadence, retain timestamp/staleness and bounded warning/info state. Expose an optional cached workspace-slot snapshot on the existing runner-status response through `SessionRunnerEndpoints.cs`/its DTO. | CP-10; A1-A6; contract CP-13 before land. |
| I3, operator surface | 30-45 min | `scripts/workspace-slots.ps1` read-only list reads that status snapshot; `client/src/api/attention.ts`, `client/src/features/attention/attentionVisuals.ts` and their existing test files; `docs/ops-http.md`, `docs/docker-stack.md`; one stable attention kind, ASCII script, stale/unknown explicit. | Existing wire test in CP-10, CP-13 contract; parse-only script, CP-11 client cases/type/lint, CP-12 frontend build. No new public inventory HTTP route is needed. |
| M1, Merge lease custody | 45-60 min | Phone-home mirror contracts/strict allowlists, `RunnerWorkspaceSlotLedger.cs`, pool, dispatcher and server `RemoteWorkspaceService.cs`; optional Merge lease mode behind new `workspaceSlotMergeV1`. Journal exact auxiliary-ref ownership and expected-absence creation; old fields default to ordinary. | CP-17/CP-18; MB1-MB4 and MA1-MA3. Merge roles remain closed operationally. |
| M2, exact publication proof | 45-60 min | New `RunnerWorkspaceSlotPublicationProof.cs`, pool release/preflight integration; immutable fresh allowed-origin root set, full owned recovery-root census, bounded graph validation, canonical salvage compatibility with CARD-1901. No shell audit edits or automatic push. | CP-19; MP1-MP5; ordinary custody remains strict. |
| M3, owned-ref cleanup and recovery | 45-60 min | Pool/ledger: persist release intent, recheck proof/ownership, delete only owned local refs using expected-old values, check every result, recover interruption without widening authority; update Merge procedure in `docs/orchestration-loop.md` and `docs/release-gates.md`. | CP-20/CP-21; MR1-MR5 plus existing reset/custody/rehome/overflow guards. |
| D1, Deploy procedure | 30-45 min | `docs/docker-stack.md`, `docs/apphost-runbook.md`: distinguish slot task cwd from independently resolved canonical command cwd; link existing ownership/preflight gates and record verified target SHA. | Documentation/argument review only. No restart/deploy script code unless a concrete failing required workflow demonstrates a gap; such a change needs its own exact remote-function tests. |
| G1, freeze selection/evidence | 30-45 min | Test categories/slow allowlist and derived impact index through their existing generator; finish the prospective/final census and this plan's execution evidence in the Code report. No runtime policy widening. | CP-22/CP-23/CP-24, impact check, evidence-diff and conflict-free merge-tree. |

I1/M1 capability constants belong in
`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`; their transport
client is `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs`.
Keep capability advertisement in `PhoneHomeCommandDispatcher.cs` consistent with
actual handler support. Extend the existing mirror strict-shape witness in
`C0824WorkspaceSlotCustodyTests`, exercised by the runner regression row. The
inventory JSON must be bounded and explicitly report continuation/truncation;
the CLI follows every page or reports an incomplete census, never healthy-empty.

### Ref-only producer protocol (F1-F4)

1. Existing explicit Checkout coordinates stay Checkout; existing RefOnly mode
   bypasses the admission gate and resumes the recorded binding. For a new
   ordinary eligible task, require role AND runner AND enabled. Mutation,
   SourceLanding, Shared and desktop execution remain excluded. Failed selected
   preparation holds; it never calls `CreateForTaskAsync` as recovery.
2. Keep repair/explicit/card/target/default precedence and branch-minting policy.
   Freeze one full base SHA, provenance and full task ref, canonical repository
   and common-directory identity. Reject the eight-character name collision by
   durable owner, even if the colliding tip equals the selected SHA. Identity
   path remains `<configured-worktree-root>/.ref-only/<full-task-guid-N>` and is
   never created. No worktree-list adoption in this branch of provisioning.
3. `RecordPlannedAsync` inside the existing claim transaction does **not** make
   intent durable until the outer transaction commits. Explicitly end that
   claim with the owned preparation queued, then run local Git/publication in
   the existing owned background preparation lifetime. Do not hold a DB
   transaction across network work or detach an unowned background task.
4. Under the existing repository/verification-window lease, re-read owner,
   attempt and binding revision; verify no checkout attaches to the full ref,
   no symbolic ref redirects it, and canonical/common identities still match.
   Create with `update-ref --no-deref <full-ref> <B> <object-format-zero>`.
   Record RefCreated. At a crash boundary, adopt only the ref created by this
   committed intent with its expected state; unexplained/pre-existing refs hold.
5. Initial publication uses recorded B/full ref, never canonical HEAD. Require
   remote absence or the exact matching recorded publication; publish with an
   expected-absence lease, no wildcard/tag push. Different existing remote tips
   hold even if ordinary fast-forward push would be possible. Re-observe exact
   endpoint/tip before recording Published. Retry after a lost reply validates
   the same intent; it cannot repush B over a progressed runner branch.
6. Capture schema-2 baseline from the owned ref and binding after publication,
   with no RegisteredCheckout and the correct frozen B/endpoint/pins. Then use
   the existing committed remote preparation identity and slot-or-mirror choice.
   Actual execution and long-brief spill use RemoteWorktreePath/RunnerCwd. A
   selected task requiring a deny hook must receive the equivalent hook remotely
   or hold `ref_only_remote_hook_unsupported`; no identity-path hook write.
7. Settlement retains the November 5 protocol: observe local L and exact remote
   S, require B ancestor of S and L ancestor of S, pin S, recheck binding/endpoint/
   registration under lease, and `update-ref --no-deref <owned-ref> S L`.
   Re-read exactly S before confirmation. Equal S is idempotent for the same
   binding. CAS loss, ahead/divergent refs or endpoint ambiguity create debt/hold,
   never overwrite. Existing `RefOnlyDesktopWorkspaceService.AdvanceAsync` is the
   primitive, not a reason to remove its callers' guards.
8. Artifacts read B..S and S:path; pending reads follow their existing pinned
   contract. Reply, debt, late land, child auth and cleanup use recorded mode even
   after admission closes. Existing ref-only land-recovery/reset refusal stays;
   use an isolated repair plus ordinary exact-SHA land instead. Source identity
   never grants filesystem authority over the canonical repository.

No new database migration is expected: mode, binding, preparation states and
revision already exist. If implementation cannot express the committed claim
boundary with those fields, return a concrete additive-schema amendment with
the CARD-1782 migration/downgrade selection before expanding the closed scope.

### Merge release protocol and CARD-1901 interaction (M1-M3)

The intended successful case is a clean, holder-free Merge whose final cut is
published and whose replaced originals are also exactly preserved on approved
origin refs. “Published-equivalent” alone must still refuse. This is how to avoid
false quarantines **without deleting the last recoverable copy of a commit**.

- **Admission/ownership:** add optional Merge lease mode to the authenticated,
  preparation-bound mirror request; persist it before creation. The server sends
  it only for Merge and only to `workspaceSlotMergeV1`. An old peer takes the
  legacy Merge preparation, never an unrecognized relaxed slot request. The
  mirror request's optional Merge discriminator and response's optional owned-ref
  field must be added to both strict JSON allowlists; old requests omit them.
  Existing ordinary leases cannot be reinterpreted as Merge leases on upgrade.
  Extend the existing persisted slot-decision event payload compatibly to bind
  mode on the first send; replay reads it, never today's role setting. Validate
  the returned auxiliary ref against the exact task/generation tuple and render
  that owned name in the Merge brief. No task database column is needed for a
  derivable ref name. If durable mode cannot fit that existing event contract,
  amend the schema/verification design before coding an alternative.
  The runner creates one auxiliary ref for a Merge-mode lease, even if the task
  ultimately needs only its task branch. The owned auxiliary ref is exactly
  `refs/heads/antiphon/merge/<task-guid-N>/<lease-generation>/cut`.
  Record an expected-absent intent under repository serialization before creating
  that auxiliary ref. A collision refuses; a name does not authorize adoption.
  Preserve this manifest across restart, release retries and quarantine (including
  its Pushed-disposition recheck). Clear it only after verified release; a rehome
  cannot transfer old-directory ownership to its replacement. No role inferred
  from commit messages or a caller-written file can activate the rule.
- **Re-cut:** the Merge task first records its previous cut OIDs and ensures
  exact publication under an explicitly authorized archive head or canonical
  salvage refs. Verify that publication before resetting its owned cut branch
  to the newly observed target SHA. Produce the new cut there, publish the
  target under the existing reviewed Merge process, and reconcile the task ref
  with that process. Never reset another task branch, the canonical checkout,
  or the whole shared repository. No reflog expiry is the remedy for re-cuts.
- **Protected roots:** HEAD, both old and new OIDs in its complete reflog, task
  and auxiliary tips and their reflogs, plus per-worktree recovery refs/pseudorefs
  that reset would remove. Preserve the oldest old reflog OID, not only `reflog
  show --format=%H`. Reject unreadable/truncated/oversize census rather than
  silently dropping roots. Unknown attached/foreign source branches are never
  deleted. A foreign-current-HEAD or unowned reference involved in the cut holds
  for repair; unrelated shared refs remain unchanged.
- **Publication proof:** read approved-origin endpoint and advertised heads plus
  the closed canonical salvage namespace; fetch exact named refs into
  attempt-private proof refs, verify names/OIDs/types and read advertisement
  again. Require the before/fetched/after snapshot to agree, and at least one
  origin branch head. Traverse protected roots against the explicit accepted
  OIDs, never `--all`, arbitrary `--remotes`, stale cached tracking or local
  salvage exclusions. Tags keep CARD-1901's conservative semantics; a tag alone
  does not become an origin commit root. Bound calls/census and fail closed on
  any missing object or graph/freshness failure. Freshness is re-established on
  every release retry, not recovered from a saved success flag.
- **Release:** after the existing task/session/sync/follow-up gates, prove path,
  registration, generation, process absence, clean index/tree and evidence
  retention. Freeze a release intent containing exact owned refs/tips and the
  proof identity. Immediately before destructive effects, recheck endpoint,
  advertisement, ownership, tips and holders under repository serialization.
  Detach only the owned slot at the exact published SHA. Delete only recorded
  local task/auxiliary refs and their appropriate local tracking refs with
  expected-old OIDs (prefer one `update-ref --stdin` transaction); check errors.
  Verify their absence before Free. Clear this slot's reflog only after every
  recorded original is proved; this is the existing S1 slot-local reset contract,
  not a new pre-audit Merge reflog-expiry operation. There is no repository-wide
  reflog expiry/prune/GC and no change to CARD-1901's complete audit census.
  Free is never recorded after a failed delete or stale ref observation.
- **Crash recovery:** an interrupted release resumes its recorded tuple; already
  absent refs count as this operation's completion only with its durable intent
  and fresh graph proof. A recreated/moved ref is a conflict, not already-free.
  A publication uncertainty leaves custody intact; existing quarantine/rehome is
  available for genuine unsafe state. A fully proved equivalent re-cut does not
  quarantine solely because the old OID differs from the landed OID.

Do not downgrade the runner to a binary unaware of Merge lease manifests while
such leases, interrupted releases or quarantines remain. Closing new Merge
admission is the rollback; drain its recorded custody before binary rollback.

CARD-1901's accepted change only teaches the **temp/v2 retirement audit** to
recognize exact canonical origin salvage. It neither publishes those refs nor
changes the pool. M2 must implement the same closed namespace and fresh-origin
semantics in C#, with its own tests. Do not edit the three in-flight retirement
scripts, add a patch-id exception, or broaden v1/main fallback. Coordinate after
CARD-1901 lands; record both tested SHAs. Until that audit is deployed, exact
origin **archive branch heads** are the compatible preservation route. Never
claim that a pool release receipt alone authorizes `retire-temp`.

### Deploy and Mutation disposition

Deploy requires no slot-specific script change on the current evidence. Use the
slot for preparation/reporting, and run the named host operation from its
authorized canonical control checkout with reviewed source/target identity.
Read-only dry-run/preflight comes first where the script supports it; verify a
linked-checkout refusal without requesting an override. Do not invent a dry-run
option for restart-apphost. Real restart/rollout acceptance belongs to the due
authorized operation, not a destructive automated test. Join every owned helper
before settlement so it cannot keep the slot leased. If an actual workflow needs
slot-built artifacts, design explicit immutable artifact handoff rather than
making a linked checkout canonical by changing a guard.

Mutation and SourceLanding keep their source-bound verification workspace **by
design**. The operator gets one single-purpose snapshot on the runner, already
without an unused desktop half for normal SourceLanding, at the exact confirmed
landing SHA. Its external evidence, red/restore/green and child ownership remain
the cleanup authority; no commits or pushes originate in the snapshot.

Ever pooling that work requires a separate custody design: per-generation
verification identity bound to landing operation/SHA, clean before/after source
and artifact receipts, retained external evidence outside the reused directory,
interruption-safe mutation restoration, complete owned-child joins, held
verification windows, quarantine on unknown restoration, safe late evidence and
cleanup after directory reuse, and permission-compatible no-publication release.
It also needs new server/runner protocol, admission gates and native positive
controls for contamination and stale receipts. Warm outputs can contain mutant
binaries; the saved disk/checkout cost has not been measured to justify that
risk. Do not treat this as the next simple role-list edit.

## Retention, retirement, capacity and disk

Fresh local `df -h /work` during Plan: **705G total, 528G used, 149G available,
79% used**; this task's unbuilt checkout is **247M**. Investigation at 08:19 UTC
measured 120 legacy directories/176G, a representative built checkout **2.3G**
(server bin 173M/obj 53M; Antiphon.Tests bin 918M/obj 106M), no slots, and 148G
free. These are rounded binary-unit samples, not a slot-size p95.

| Resource at observed capacity 10 | Estimate / behavior | Control |
|---|---|---|
| 14 base + 4 overflow, lazy growth | 18 x 2.3 GiB ~=41 GiB; at configured estimate 3 GiB ~=54 GiB, before evidence/quarantine/checkpoints | Keep `Headroom=4`, `MaxOverflowSlots=4`, `SlotBytesEstimateGiB=3`, `PoolDiskBudgetGiB=64`. No capacity change. |
| Seven/eight active tasks | 7-8 leased slots plus 6-7 base headroom; retained leases consume that margin regardless of seat count | Count Leased/Releasing/Blocked/preparing separately. No automatic eviction. |
| Full base pool | If Releasing/ColdResetting exists, wait up to `OverflowWaitSeconds=10`; otherwise grow bounded overflow if disk permits | At overflow/disk/degraded bound return `workspace_slot_unavailable:<bound>` and keep task Queued/Held. No legacy fallback. |
| Overflow disk admission | Free >= max(20 GiB, 10% volume) + 2 x estimate, and active+quarantine measured bytes + estimate <=64 GiB | About 76.5 GiB floor on this volume; canary entry remains the stricter 84 GiB. |
| Full migration overlap | 149 - 54 ~=95 GiB before extra outputs; legacy 176G does not disappear first | Projected usage crosses the existing 85% disk alarm before the maximum pool fills. Measure at every growth; close new admission if entry reserve cannot be maintained. |
| Retained task evidence | Cap 256 MiB/task, 7 days; e.g. 100 tasks/day at cap could retain 175 GiB | A cap is not expected usage. Record actual bytes/day and forecast, not just pool bytes. |
| Quarantine / overflow retirement | >4 open quarantines degrades; rehome cap 3/day/slot; only excess Free slots idle >=72h retire | No hand removal. Fixed Free base slots remain after rollback. |
| Build broker | Investigation observed budget 4/maxCpuCount 6, four holders; independent of 7-8 task seats | Re-read broker at execution; preserve host slot gate, one build at a time per worktree, per-build lease in mutant loops. |

The initial 149 GiB clears the entry gate, but it is not a forecast that eighteen
fully built slots plus retained history fit indefinitely. Before each widening,
record actual slot-size p50/p95/max, retained/quarantine bytes, legacy drain rate,
warm-reset latency, build waits and volume headroom over the canary. Keep the
default bounded pool; if measured growth exhausts reserve, close roles and have
the existing retention owner resolve eligible legacy trees. Do not raise overflow
or disk budgets in response to a refusal without a new resource decision.

CARD-1677 remains the sole ordinary runner legacy remover. Server slot-id, runner
`ws-*` and fail-closed ledger guards must remain; do not add a second sweep. Read
every retention run page, including kept reasons. Preview is a POST that records
run evidence, not a read-only GET; it was not invoked by this Plan.

CARD-1894 treats Ready as paid only with a nonblank SourceSha equal to ConfirmedSha;
Pending/Held still block and a second unpaid debt still blocks. Its master-only
land is not activation. Expect paid legacy trees to become eligible after the
next activated tick and other guards, not all Held/source_changed debt to vanish.
Slot release already checks Pending/Held separately; this plan does not change
that predicate or assume a free session seat proves release eligibility. Ref-only
activation still requires effective S4a `WorktreeLegacyRelease:Enabled=false`
and `Execute=false` under the November 5 safety contract; slot wave 1 itself
does not authorize changing legacy deletion policy.

Before any temp retirement, read ledger/quarantines in addition to the task and
seat census. Keep `RecycleUnpublishedWork`, `RecycleWorktreeDirty`, unknown audit
and registry refusal gates. Use the existing fresh preview/token apply and
renewed proofs for every effect; no old token, local salvage, equivalent patch,
Free ledger state or paid debt supplies publication proof. A quarantined
directory with unique work remains a recycle blocker until exact authorized
preservation is proved. Finish the normal retire-temp phase after the rollout;
missing S4b is not permission to ignore the ledger.

**Waiting-input checklist:** no new wait timer is introduced. A reply resumes
the same mode/binding under existing custody. With parking disabled (default),
Blocked/AwaitingInput retains its workspace lease and normally its seat with
**no automatic release deadline** (CARD-1083). Terminal settlement/cancellation
only makes release eligible after session, publication/debt and follow-up guards;
it does not skip them. Separately enabled receipt-backed parking has its own
conditional release path. A 10-second pool wait, one-minute sweep or 72-hour
overflow idle cutoff is not an input-wait release deadline.

## Verification design

Verification is folded into this Plan. All new Git integration tests use real
temporary local repositories and a local bare origin, task-owned temporary
linked checkouts and fake/loopback transport. Never use a production worktree,
real volume, production Git remote or production runner. Source-only inspections
cannot stand in for the producer caller, CAS races or actual Git cleanup.

Use existing `RefOnlyWorkspaceTestSetup`, `RefOnlySyncFixture`,
`DelegationTestServices` and `C0824SlotScratch` patterns, sharing immutable seed
repos once per test-host run and cloning mutable repos per case. Do not change
the global transport fixture merely for convenience: it has native Windows
obligations. Share the database container/migrations/template and loopback host
once per run; isolate task IDs, schema/clone, refs and fault barriers per test.
Only explicit restart/crash witnesses recreate a host and label that phase.
Process-spawning tests carry their assembly-local ProcessSpawnLimit. Do not
co-schedule Antiphon.Tests with Pty/FakeClaude assemblies.

### Closed new test roster

Each name below is a separate `[Test]` method; looped vectors are not extra Min
counts. Files are `tests/Antiphon.Tests/Application/<Class>.cs` for F/MA/A,
`tests/Antiphon.SessionRunner.Tests/<Class>.cs` for MB/MP/MR/I. Native F methods
require Windows; other lifecycle tests run on Linux first. Add appropriate
Integration/Slow declarations and allowlist entries, not Unit misclassification.

| IDs / class | Exact method names | Required outcome / discriminating negative |
|---|---|---|
| F1-F4 `C0824RefOnlyProvisionTests` | `Intent_precedes_git_and_never_creates_identity`; `Bases_and_short_name_collisions_preserve_ownership`; `Existing_mode_survives_gate_and_attempt_change`; `Selected_failure_never_falls_back_to_checkout` | Persisted ownership before first ref write; exact repair/explicit/card SHA despite moving ref; same-prefix foreign owner refused; no directory/registry delta or local launch on fault. |
| F5-F6 same class, added F3 | `Dispatcher_creates_ref_without_desktop_checkout`; `Dispatcher_commits_preparation_before_network_work` | Real dispatcher path produces RefOnly/zero new registrations; an injected network barrier observes committed queued intent and no held DB transaction, with no premature session. Base checkout behavior must give an assertion red. |
| P1-P4 `C0824RefOnlyPreparationTests` | `Initial_ref_creation_requires_expected_absence`; `Initial_remote_collision_is_not_overwritten`; `Crash_boundaries_resume_only_owned_intent`; `Publication_uses_frozen_ref_and_never_rewinds` | Real local ref CAS; foreign remote descendant refuses; crashes before/after each durable boundary; a moved canonical HEAD cannot select source, replay cannot rewind runner progress. |
| P5-P6 same class, added F3 | `Baseline_and_launch_bind_published_ref_and_runner_cwd`; `Lost_mirror_reply_reuses_preparation_and_slot` | Schema 2/full binding and exact B; remote brief spill, denied local cwd/hook fallback; one preparation/lease after lost reply despite gate reload. |
| L1-L5 `C0824RefOnlyProducerLifecycleTests` | `Dispatch_settle_and_late_land_preserve_other_task`; `Settlement_race_keeps_exact_compare_and_swap`; `Blocked_reply_and_restart_keep_mode_when_gate_closes`; `Repair_from_ref_only_uses_exact_owned_source`; `Artifacts_use_one_settled_sha_and_no_identity_files` | Two real tasks and stale decoy, full production callers; move L at CAS barrier; stop/recreate host with gate closed; repair currency checks; exact blob hashes with origin moving. |
| W1-W3 `C0824RefOnlyNativeTests` | `Windows_dispatch_settle_late_land_has_zero_desktop_checkout`; `Windows_restart_and_gate_rollback_keep_ref_only_custody`; `Windows_two_tasks_and_case_normalization_never_alias` | Real Windows path/registration equality and preserved decoy; no normalized POSIX runner path interpreted as desktop authority. |
| I1-I5 `C0824WorkspaceSlotInventoryTests` | `Inventory_lists_slots_and_quarantines`; `Inventory_does_not_wait_for_mutation_lock`; `Inventory_preserves_ledger_bytes`; `Cursor_and_bounds_are_explicit`; `Unreadable_ledger_is_unknown_not_empty` | Real ledger snapshots, held mutation barrier, byte hash unchanged; bounded/truncated evidence explicit, no false healthy empty result. |
| A1-A6 `C0824SlotAttentionTests` | `Quarantine_warning_tracks_exact_directory`; `Failed_rehome_and_degraded_pool_warn_until_clear`; `Overflow_and_rehome_info_expire_after_day`; `Unsupported_runner_is_not_polled`; `Failed_poll_marks_evidence_stale`; `Attention_wire_value_is_stable` | Exact runner/path keys, stale data never clears prior danger as healthy, explicit 24h information expiry and no unknown-peer RPC. |
| MB1-MB4 `C0824MergeSlotBindingTests` | `Lease_journals_owned_auxiliary_ref_before_create`; `Existing_auxiliary_ref_is_never_adopted`; `Restart_retains_role_task_preparation_and_generation`; `Legacy_request_keeps_strict_ordinary_mode` | Real ref absence check, crash/replay, full tuple, old JSON shape. Missing/contradictory fields refuse. |
| MA1-MA3 `C0824MergeSlotAdmissionTests` | `Merge_requires_capability_and_persisted_decision`; `Role_rollback_does_not_change_existing_merge_lease`; `Mutation_and_source_landing_never_enter_merge_mode` | End-to-end request shape through server/runner transport; capable Merge only, stable retries, independent custody exclusions. |
| MP1-MP5 `C0824MergeSlotPublicationTests` | `Equivalent_recut_with_exact_origin_salvage_releases`; `Equivalent_patch_without_original_publication_is_kept`; `Local_or_stale_salvage_never_proves_origin`; `Malformed_salvage_and_endpoint_or_advertisement_drift_refuse`; `Oldest_reflog_and_auxiliary_history_require_publication` | Unequal original/final OIDs with independently equal patch/tree content; publish original exact chain for the positive; local-only/deleted remote/wrong GUID/OID/noncommit/stale endpoint negatives; protect both reflog sides and earlier auxiliary commits. |
| MR1-MR5 `C0824MergeSlotReleaseTests` | `Release_deletes_only_owned_refs_and_preserves_build_output`; `Foreign_or_attached_auxiliary_branch_is_preserved`; `Ref_move_before_delete_fails_compare_and_swap`; `Crash_during_release_revalidates_before_free`; `Unpublished_dirty_or_live_child_keeps_custody` | Exact local refs gone, origin/sibling untouched, bin/obj byte+mtime retained; expected-old race; all release crash boundaries; original bytes/history/child owner retained. |

M2's positive must fail at the original `UnpushedAsync` quarantine assertion;
MR1 must fail on the original leftover auxiliary ref. Preservation negatives
may already pass at base; do not manufacture red by breaking their fixtures.

### Existing tests affected (CARD-1782)

Use [the impacted-classes owner](../../testing-and-build.md#impacted-classes-card-1782)
and `tests/impacted-classes.json`. Run the final exact-range selector and retain
the result; hand-glob obligations below are already included. Broad derived
reverse-graph suggestions are reported separately with scope reasons, not
silently credited as executed or expanded into a whole assembly.

| Change kind | Required existing coverage |
|---|---|
| Migrations | **None planned.** Existing binding schema is reused. Any additive schema amendment requires the table's migrations row and derived downgrade callers; no empty Down to evade safety. |
| Public DTOs/routes | I2/I3 AttentionDtos/client API and runner-status projection trigger `ContractSnapshotTests` (actual declaring project Antiphon.E2E), plus `AttentionKindWireTests`. There is no new public route. Internal phone-home changes also run `CompletedCardWorkspaceContractTests`, `RunnerCapabilitiesTests`, existing slot transport tests. |
| Operation labels / timing | New spans trigger `TestTimingOperationLabelTests`; preserve stable labels and existing timing fields. |
| Remote script functions | **None changed**: CARD-1901 owns its scripts, Deploy documentation preserves guards. If a later necessary script repair appears, amend with the five `remote-scripts` exact methods before execution. |
| Statement budgets | Dispatcher edits require `DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets`; do not relax its budget to hide extra intent reads. |
| Dispatcher notification blast radius | All thirteen hand-table `land-notification` classes in CP-14/CP-15. Their fixed class lists below intentionally exclude unrelated whole-suite work. |
| Worktree service Git start sites | `ServerGitProcessTests` remains in Antiphon.Tests (CP-1); `GitIndexLockTests` and `ServerGitAssemblyTests` are in Antiphon.Server.Git.Tests (CP-16). |
| New process/timing/category declarations | ProcessSpawnLimitTests in each declaring project; classification/slow/platform/layer/category/mechanical/index guard union CP-22/CP-23/CP-24. If native-policy data changes, add its exact impacted rows before running; do not silently broaden native exclusions. |

The Plan's prospective read-only selector over the dispatcher, worktree service,
remote service, Attention DTO/client API, pool and phone-home contracts returned
four hand kinds (contracts, land-notification, server-git-start-sites and statement
budgets), 133 union classes and 117 derived suggestions. Its Windows suggestion
`GrokDelegateEndToEndTests` and capped broad provider/boot/notification families
remain outside this closed behavioral scope; they do not exercise a changed
provider/OS branch. No green claim is made for them. The ignored prospective
receipt is `.antiphon/evidence/card-0824-plan/prospective-impact.json`; final Code
must repeat the census against its actual full diff, including newly added test
declarations, and record any difference before execution.

### Positive controls, post-land SourceLanding only

These PCs are pending. Bind each cycle to `/*/*/<Class>/<ExactMethod>`; separate
variants mean separate red/restoration/green receipts. Do not run a class-wide
mutation battery. Zero executions, fixture/build error and timeout are not red.
Never weaken fixture assertions to kill a mutant. Retain external evidence and
restore all source before snapshot cleanup.

| PC | Compiling production defect / variants | Exact method |
|---|---|---|
| PC-1 | Create the ordinary checkout after selecting RefOnly. | `C0824RefOnlyProvisionTests.Dispatcher_creates_ref_without_desktop_checkout` |
| PC-2 | Run first Git/push before outer claim commit. | `C0824RefOnlyProvisionTests.Dispatcher_commits_preparation_before_network_work` |
| PC-3 | Drop expected-absent local CAS; separately accept foreign short-id owner. | `C0824RefOnlyPreparationTests.Initial_ref_creation_requires_expected_absence`; `C0824RefOnlyProvisionTests.Bases_and_short_name_collisions_preserve_ownership` |
| PC-4 | Use ordinary initial push accepting a foreign descendant; separately repush B on retry. | `C0824RefOnlyPreparationTests.Initial_remote_collision_is_not_overwritten`; `Publication_uses_frozen_ref_and_never_rewinds` |
| PC-5 | Capture RepoPath HEAD/schema 1; separately use identity as launch/spill cwd. | `C0824RefOnlyPreparationTests.Baseline_and_launch_bind_published_ref_and_runner_cwd` |
| PC-6 | Re-evaluate mode on restart/gate close; separately bypass attempt/store on mirror replay. | `C0824RefOnlyProducerLifecycleTests.Blocked_reply_and_restart_keep_mode_when_gate_closes`; `C0824RefOnlyPreparationTests.Lost_mirror_reply_reuses_preparation_and_slot` |
| PC-7 | Omit settlement expected-old L; separately omit endpoint or binding recheck. | `C0824RefOnlyProducerLifecycleTests.Settlement_race_keeps_exact_compare_and_swap`; existing `C0824RefOnlySyncTests.Endpoint_pin_and_binding_changes_refuse` |
| PC-8 | Read artifact disk or re-resolve moving branch for each file (separate variants). | `C0824RefOnlyProducerLifecycleTests.Artifacts_use_one_settled_sha_and_no_identity_files` |
| PC-9 | Restore checkout-only repair source probe. | `C0824RefOnlyProducerLifecycleTests.Repair_from_ref_only_uses_exact_owned_source` |
| PC-10 | Adopt auxiliary name collision; separately drop generation identity. | `C0824MergeSlotBindingTests.Existing_auxiliary_ref_is_never_adopted`; `Restart_retains_role_task_preparation_and_generation` |
| PC-11 | Ignore Merge capability; separately admit SourceLanding/Mutation. | `C0824MergeSlotAdmissionTests.Merge_requires_capability_and_persisted_decision`; `Mutation_and_source_landing_never_enter_merge_mode` |
| PC-12 | Remove canonical salvage from explicit origin proof roots. | `C0824MergeSlotPublicationTests.Equivalent_recut_with_exact_origin_salvage_releases` |
| PC-13 | Count patch equivalence as publication. | `C0824MergeSlotPublicationTests.Equivalent_patch_without_original_publication_is_kept` |
| PC-14 | Trust local salvage; separately stale tracking after remote deletion. | `C0824MergeSlotPublicationTests.Local_or_stale_salvage_never_proves_origin` |
| PC-15 | Drop namespace/OID/type validation, endpoint binding or second advertisement (separate variants). | `C0824MergeSlotPublicationTests.Malformed_salvage_and_endpoint_or_advertisement_drift_refuse` |
| PC-16 | Omit oldest old reflog OID; separately auxiliary tip/reflog. | `C0824MergeSlotPublicationTests.Oldest_reflog_and_auxiliary_history_require_publication` |
| PC-17 | Keep auxiliary ref; separately cold-clean native output during warm release. | `C0824MergeSlotReleaseTests.Release_deletes_only_owned_refs_and_preserves_build_output` |
| PC-18 | Delete by name without expected-old; separately ignore failed deletion and mark Free. | `C0824MergeSlotReleaseTests.Ref_move_before_delete_fails_compare_and_swap`; `Crash_during_release_revalidates_before_free` |
| PC-19 | Delete foreign/attached ref; separately skip dirty/publication/child guard. | `C0824MergeSlotReleaseTests.Foreign_or_attached_auxiliary_branch_is_preserved`; `Unpublished_dirty_or_live_child_keeps_custody` |
| PC-20 | Mutate inventory ledger; separately collapse unreadable to empty. | `C0824WorkspaceSlotInventoryTests.Inventory_preserves_ledger_bytes`; `Unreadable_ledger_is_unknown_not_empty` |
| PC-21 | Clear danger on failed poll; separately suppress quarantine warning. | `C0824SlotAttentionTests.Failed_poll_marks_evidence_stale`; `Quarantine_warning_tracks_exact_directory` |

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards).
This Plan ran no builds/tests/benchmarks. New row estimates are **provisional**
on CARD-0824, not measured p95. Preserve all inherited failure and cost history.

Measured comparator: `C0824WorkspaceSlotTimeoutBudgetTests` records ten Linux
class runs at `388ff482046a62f813a251208bfeabe7c21808ea`, host `dfcc116be984`,
190/190 results; empirical per-case p95 spans 0.256-3.315s (n=10, p95=max).
Its inherited 60s cross-platform Git floor is an explicit CARD-1722 exception,
not a new-test default. The older server-slot measurement is p95 17.2s at load
37 with a 35s timeout (`C0824WorkspaceSlotServerTests` comment). The amendment
cites loaded Antiphon.Tests build p95 579s, n=117, and runner-test build p95
135s, n=11, from `cf8b6982` receipts; these are historical comparators requiring
full receipt identity before treating them as current. New producer, attention,
Merge-proof row and Windows p95 are unknown.

App batches: server delegation/Git/landing plus artifact consumers; runner
workspace Git/custody and phone-home contracts; attention public contract/client.
Project graph: Antiphon.Tests -> server and SessionRunner; runner tests ->
SessionRunner -> Contracts/Pty; Server.Git.Tests owns extracted Git start sites;
Antiphon.E2E owns API contract snapshots. Keep these consumers in the named
selection. No provider behavior changes or broad Pty runtime suite is planned.
Unknown downstream scope is reported for release qualification, not claimed
covered by ordinary targeted checkpoints.

| Phase / unit | Expected duration, provisional unless cited | Deadline and measurement |
|---|---|---|
| Build admission | 0-10min under load | Separate finite 10min slot wait; exit 4 is not run, never bypass the broker. |
| Fresh main/E2E build | 4-10min | 20min build ceiling from ~2 x historical 579s, provisional CARD-0824 until current receipts. Runner/Git build 2-3min, initial 5min ceiling from ~2 x 135s comparator. |
| Shared DB container/readiness, migration/seed | <=60s + <=30s once per test host | Record separately, preserve existing fixture limits. No per-class containers or repeated schema migrations. E2E uses isolated SharedApp/runner, never production. |
| Shared seed repo / loopback host | <=2s / <=5s | Once per test-host run; per-case clone/schema 1-5s. Restart witness is the named fresh-host exception. |
| Pure gate/wire method | <1s | Initial 2s body budget. No blanket 60s timeout for pure cases. |
| New runner Git method | 1-5s | Initial 10s body budget; existing inherited class timeouts unchanged. Complex interruption method expected <=15s, initial 30s budget. |
| New server lifecycle method | 5-30s | Initial 60s body budget; Windows 30-60s expected/120s initial, explicitly unmeasured. |
| External CLI/DB/loopback calls | Usually <1s | Individually cancelable: initial 10s local Git, 5s loopback/DB, 15s teardown/owned-child join. Preserve production subject budgets; record stdout/stderr drain and proof-network phases. |
| Ordinary row bodies + teardown | Runner 1-3min; server 2-5min; native/E2E 3-6min | Row costs below include isolated build. Calibrate execution to about 2 x actual row p95, with admission separately bounded. |

Use existing TestTiming and fresh TRX; store under ignored
`.antiphon/checkpoints/<run-id>/`. Record exact SHA/build identity, host/platform/
load, per-method and argument membership, p50/p95/max/sample size, slot wait,
build, container/readiness, migration, seed, host, clone, body, teardown, Git/IO/
DB/HTTP spans. Unavailable phases stay unknown; unattributed time is remainder.
Measure first required red/green runs, then normal acceptance; do not repeat a
green battery merely for timing. Round timeouts to about 2 x comparable measured
p95; retain provisional labels until enough comparable data exists.

**Packing:** run only the committed slice group's named rows, serially. The
table is the closed union, not an instruction to run all groups in one batch.
The complete program exceeds 30 minutes; the caller must commission the bounded
CARD-0824 performance Debug/packing obligation before scheduling that combined
selection, as the parent plan already required. Prefer separate <30-minute
slice runs. Any observed/expected >30-minute row/group/run, including queue wait,
requires Debug with timing evidence; do not retry by increasing timeouts.

Importer defaults (15-minute floor/3x estimate) are not p95. Set measured row and
build limits in ignored imported YAML; until calibrated, row limit is twice the
table estimate, with the separate build and body bounds above. Set the finite
charged total from the tool's selected-row derivation, not an arbitrary lower
total; an explicit below-derived override needs a stated reason. Record any
tooling-imposed larger limit as a CARD-0824 provisional exception. Native/contract
rows are separately scheduled; no Windows receipt can be inferred from Linux.

### Checkpoints

Every OR operand has parentheses and its own trailing `*`; pipes are escaped.
Min is a floor on executed test results, not assertions or minutes. Existing
class unions require every named class/method in fresh TRX even where Min is only
the class-count floor. New names refer to the closed roster above. Do not execute
a row while its new tests are absent. CP-14/CP-15 split the dispatcher impact for
bounded runs; CP-16 uses the extracted Git project. CP-22/CP-23/CP-24 are the final
guard/limiter census, not a whole Unit lane.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | F1 | tests/Antiphon.Tests -> bin-c0824-all-f1/ | portable-intent | `/*/*/(C0824RefOnlyProvisionTests*)\|(C0824RefOnlyIdentityTests*)\|(C0824RefOnlyGateTests*)\|(ServerGitProcessTests*)\|(DelegationTestServicesTests*)/*` | F1-F4, recorded mode, Git start sites, DI | all listed, 0 failed | 17 | 8 | true |
| CP-2 | F2 | tests/Antiphon.Tests -> bin-c0824-all-f2/ | portable-publication | `/*/*/C0824RefOnlyPreparationTests/*` | P1-P4 | 4 methods, 0 failed/skipped | 4 | 7 | true |
| CP-3 | F3 | tests/Antiphon.Tests -> bin-c0824-all-f3/ | linux-dispatch | `/*/*/(C0824RefOnlyProvisionTests*)\|(C0824RefOnlyPreparationTests*)\|(RemoteWorkspacePreparerTests*)/*` | F1-F6,P1-P6, legacy prep | all listed, 0 failed | 13 | 10 | true |
| CP-4 | F3 | tests/Antiphon.Tests -> bin-c0824-all-budget/ | portable-statement-budget | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets` | CARD-1782 statement budget | exact method, 0 failed | 1 | 6 | true |
| CP-5 | F4 | tests/Antiphon.Tests -> bin-c0824-all-f4/ | linux-lifecycle | `/*/*/(C0824RefOnlyProducerLifecycleTests*)\|(C0824RefOnlySyncTests*)\|(C0824RefOnlyDebtTests*)\|(C0824RefOnlyContinuationTests*)/*` | L1-L5, CAS/debt/reply | all listed, 0 failed | 8 | 10 | true |
| CP-6 | F4 | tests/Antiphon.Tests -> bin-c0824-all-read/ | portable-reader-regression | `/*/*/(C0824RefOnlyReadTests*)\|(C0824RefOnlyDeliverableTests*)\|(C0824RefOnlyConsumerTests*)\|(C0824RefOnlyReceiptTests*)/*` | bytes, identity, receipt | all listed, 0 failed | 4 | 9 | true |
| CP-7 | F4 | tests/Antiphon.Tests -> bin-c0824-all-land/ | portable-land-regression | `/*/*/(C0824RefOnlyLandingTests*)\|(C0824RefOnlyCleanupTests*)\|(C0824RefOnlyBoundaryTests*)/*` | late land, cleanup, recovery refusal | all listed, 0 failed | 3 | 9 | true |
| CP-8 | F4-Windows | tests/Antiphon.Tests -> bin-c0824-all-win/ | windows-producer | `/*/*/C0824RefOnlyNativeTests/*` | W1-W3 | 3 methods, 0 failed/skipped | 3 | 12 | true |
| CP-9 | I1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-all-i1/ | linux-inventory | `/*/*/(C0824WorkspaceSlotInventoryTests*)\|(CompletedCardWorkspaceContractTests*)\|(RunnerCapabilitiesTests*)/*` | I1-I5, internal wire | all listed, 0 failed | 7 | 5 | true |
| CP-10 | I2-I3 | tests/Antiphon.Tests -> bin-c0824-all-i2/ | portable-attention | `/*/*/(C0824SlotAttentionTests*)\|(AttentionKindWireTests*)\|(HostStatsPollServiceTests*)/*` | A1-A6, poll/wire | all listed, 0 failed | 8 | 8 | true |
| CP-11 | I2-I3 | n/a | portable-client-tests | `pwsh -NoProfile -File scripts/test-client.ps1 attentionVisuals.test attention.contract.test` | attention client mapping/contracts | both files execute, 0 failed, type/lint gates green | n/a | 2 | true |
| CP-12 | I2-I3 | n/a | portable-frontend-build | `pwsh -NoProfile -Command 'npm --prefix client run build; exit $LASTEXITCODE'` | fresh client/dist before contract E2E | exit 0, current bundle produced | n/a | 2 | true |
| CP-13 | I2-I3 | tests/Antiphon.E2E -> bin-c0824-all-contract/ | portable-public-contract | `/*/*/ContractSnapshotTests/*` | CARD-1782 contracts | every class method, isolated app, 0 failed | 1 | 12 | true |
| CP-14 | F3 | tests/Antiphon.Tests -> bin-c0824-all-impact-a/ | portable-dispatch-impact-a | `/*/*/(AgentTaskLandHeldNotificationTests*)\|(AgentTaskLandNotificationParkFollowUpTests*)\|(AgentTaskLandNotificationPersistenceTests*)\|(AgentTaskLandNotificationReconcileLoadTests*)\|(AgentTaskLandNotificationRecoveryTests*)\|(AgentTaskLandNotificationRescanTests*)/*` | CARD-1782 notification callers | every named class, 0 failed | 6 | 10 | true |
| CP-15 | F3 | tests/Antiphon.Tests -> bin-c0824-all-impact-b/ | portable-dispatch-impact-b | `/*/*/(AgentTaskLandReceiptTests*)\|(AgentTaskLandReceiptWatermarkSafetyTests*)\|(AgentTaskLandReceiptWatermarkTests*)\|(ExpectationDebtTests*)\|(LandReceiptScanCacheTests*)\|(PreDispatchFailureDeliveryTests*)\|(ReceiptScanDestinationIntegrationTests*)/*` | CARD-1782 notification callers | every named class, 0 failed | 7 | 10 | true |
| CP-16 | F1 | tests/Antiphon.Server.Git.Tests -> bin-c0824-all-git/ | portable-git-start-sites | `/*/*/(GitIndexLockTests*)\|(ServerGitAssemblyTests*)/*` | CARD-1782 extracted Git start sites | every named class, 0 failed | 2 | 5 | true |
| CP-17 | M1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-all-m1/ | linux-merge-binding | `/*/*/(C0824MergeSlotBindingTests*)\|(CompletedCardWorkspaceContractTests*)\|(RunnerCapabilitiesTests*)/*` | MB1-MB4, wire compatibility | all listed, 0 failed | 6 | 5 | true |
| CP-18 | M1 | tests/Antiphon.Tests -> bin-c0824-all-ma/ | portable-merge-admission | `/*/*/(C0824MergeSlotAdmissionTests*)\|(C0824WorkspaceSlotRoleSettingsTests*)\|(C0824WorkspaceSlotRoleGateTests*)\|(C0824WorkspaceSlotServerTests*)/*` | MA1-MA3, rollback/release | all listed, 0 failed | 6 | 10 | true |
| CP-19 | M2 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-all-m2/ | linux-merge-proof | `/*/*/C0824MergeSlotPublicationTests/*` | MP1-MP5 | 5 methods, 0 failed/skipped | 5 | 5 | true |
| CP-20 | M3 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-all-m3/ | linux-merge-release | `/*/*/(C0824MergeSlotReleaseTests*)\|(C0824MergeSlotPublicationTests*)/*` | MR1-MR5,MP1-MP5 | 10 methods, 0 failed/skipped | 10 | 6 | true |
| CP-21 | M3 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-all-slot-reg/ | linux-slot-regression | `/*/*/(C0824WorkspaceSlotResetTests*)\|(C0824WorkspaceSlotCustodyTests*)\|(C0824WorkspaceSlotRehomeTests*)\|(C0824WorkspaceSlotOverflowTests*)\|(C0824WorkspaceSlotColdResetTests*)\|(C0824WorkspaceSlotProcessScanTests*)\|(C0824WorkspaceSlotTimeoutBudgetTests*)/*` | strict ordinary custody, warm/cold, bounds | all listed, 0 failed | 7 | 6 | true |
| CP-22 | G1 | tests/Antiphon.Tests -> bin-c0824-all-guards/ | portable-final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(TestTimingOperationLabelTests*)\|(DerivedImpactIndexTests*)\|(ReleaseImpactEnvelopeTests*)/*` | policy, labels, index, process roster | every named class, 0 failed | 13 | 8 | true |
| CP-23 | G1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-all-runner-roster/ | portable-runner-roster | `/*/*/ProcessSpawnLimitTests/*` | assembly limiter census | all listed, 0 failed | 1 | 3 | true |
| CP-24 | G1 | tests/Antiphon.Agents.Pty.Tests -> bin-c0824-all-pty-roster/ | portable-pty-roster | `/*/*/ProcessSpawnLimitTests/*` | assembly limiter census | all listed, 0 failed | 1 | 3 | true |
| CP-25 | F1 | tests/Antiphon.Tests -> bin-c0824-all-base/ | portable-legacy-base | `/*/*/DelegationWorktreeTests/(C508_ReuseKeepsRecordedBase*)\|(C508_BaseResolutionRequiresOwnedLease*)` | existing base and repository-lease callers | both exact intended methods, 0 failed | 2 | 6 | true |
| CP-26 | F2 | tests/Antiphon.Tests -> bin-c0824-all-sync/ | portable-legacy-sync | `/*/*/RunnerSettlementSyncTests/(Pushed_tip_fast_forwards_exact_owned_checkout*)\|(Endpoint_change_is_not_followed*)\|(C1082_SettledDebtSyncOnlyFastForwardsToTheRecordedSource*)` | existing Checkout settlement callers | all 3 intended methods, 0 failed | 3 | 6 | true |

For required baseline reds, run the same precise new acceptance method through
the checkpoint tool at the committed test-first SHA, with expected assertion
failure recorded; run its table row green after the fix. This is the explicitly
listed red-first exception to whole-row-only execution, not completed PC credit.
The final group rows are closed; additional builds/test selections require a
stated reason or a committed selection amendment before running.

Use the checkpoint tool `run --plan <this-file> --rows <slice-CPs> --serial
--expected-source-sha <full-committed-sha>`, then wait until exit is not 75.
Bootstrap the coordinator, if absent, through `scripts/build-slot.ps1`; its own
rows acquire slots, so do not wrap the whole coordinator in a second held lease.
Direct builds use native output; only the checkpoint tool owns these cold `bin-*/`
outputs. Commit before long runs and do not edit source during them. Keep unedited
CHECKPOINT lines/counts/source provenance, not merely an exit code.

Code repeats the class/method suffix-collision scan after adding the new roster,
imports this table and verifies fresh executed TRX membership. Plan performs a
static roster/shape check only; it does not build the tool. Source paths include
linked tests/Shared declarations. Same-name ProcessSpawnLimitTests belongs to
multiple projects deliberately, and the table names each project separately.

Plan validation: all 26 rows have the required columns, positive whole-number
costs and safe OR shapes. The static census checked 61 existing class names and
their declaring projects, plus the selected existing method prefixes, with no
suffix collisions. The ten proposed new classes are absent at this baseline as
expected. EstimatedMinutes sums to 183. This is not importer, discovery, build
or test-execution evidence; those remain Code obligations.

Non-test checks: `git diff --check`; PowerShell parse-only on changed CLI; final
`scripts/impacted-classes.ps1 -Range <base>..<tip> -Json`; derived-index generator
under the build-slot gate if needed; `scripts/check-evidence-diff.ps1 -BaseRef
<base> -HeadRef <tip>`; `git fetch origin master` then
`git merge-tree --write-tree origin/master HEAD`. Never merge master into the
task branch. Native policy changes, additional migrations or remote-script fixes
must get their own exact added rows rather than silently bypassing CARD-1782.

### Cost

Ordinary closed-table floor is **183 minutes** across separate source groups
and lanes, plus admission, first behavioral reds and justified fix reruns.
Authoring is approximately 9-12 hours across twelve 30-60 minute units;
the code-unit sizes do not promise that the entire ordinary check set fits into
one hour. Independent Review and SourceLanding PC costs are additional.
Do not schedule the complete table as one long run; apply the performance Debug
and packing rule above. Ref-only 24h and slot 48h live acceptance overlap with
useful authorized work, not continuous paid test execution.

## Later waves, acceptance and rollback

1. **Ref-only wave:** after F1-F4 ordinary Review/land, existing consumer gates,
   native Windows witness and active schema/readers, verify effective S4a off.
   On server2 set `Delegation:RefOnlyDesktop:{Enabled=true,RunnerIds=["server2"],
   Roles="Plan"}`, then D-2's ten-role string after the first lifecycle pair.
   These are operator configuration values, not source hard-coding. Observe
   **20 tasks over >=24h**, restart/retry, blocked reply, report bundle, two
   concurrent tasks and two ordinary lands including late land. Require zero
   new desktop directories/registrations, exact binding/ref/source and no lost
   report. S1 slots and ref-only are independent; either canary can expose a
   defect without disabling the other's existing-mode handling.
2. **Code:** after I1-I3 and both initial acceptance envelopes, append `,Code`
   to each applicable role string. Start with one authorized Code task, then
   observe 48h including a real native-output build, settlement, ordinary Review
   and land. Do not count preserved checkpoint outputs as warm native reuse.
3. **Deploy:** after D1's canonical execution procedure, append `,Deploy` and
   use the next already-authorized operation as the canary. Verify canonical
   source/target, live post-operation version and release with no owned helper
   left in the slot. Slot admission itself does not authorize a deployment.
4. **Merge last:** after M1-M3 reviewed/landed/activated on both sides, capability
   and local Git witnesses, and compatible CARD-1901 audit deployment, append
   `,Merge`. One real authorized cut plus a re-cut when naturally needed proves
   exact original preservation, clean release and auxiliary-ref absence. If no
   real re-cut occurs, local test evidence is valid but live re-cut acceptance
   remains unobserved; never rewrite a production cut solely to exercise it.
5. Final explicit role string is
   `Investigate,Plan,TestDesign,Docs,Coverage,Test,Review,Debug,Custom,Commit,Code,Deploy,Merge`.
   A role on the list is not a claim that every Kind/Shared/local/SourceLanding
   workspace uses slots. Retain per-role task evidence and independent gates.

Ref-only rollback is `Enabled=false` or `Roles="none"` for **new** admissions;
existing bindings/refs/continuations/lands/debt retain their recorded mode. Do not
delete refs or downgrade to an unaware binary until all such obligations drain.
Runner-slot rollback remains `WorkspaceSlotRoles="none"` with support flags on.
If only a late role fails, remove that role first and preserve all current leases.

## Why this is safe

Wave 1 changes only existing gates, leaves releases alive and exercises the least
complex workloads first. Source selection remains exact on every lease. Ref-only
removes desktop filesystem work while preserving durable ownership and the same
publication/ancestry/CAS checks. Merge release can succeed for replaced content
only after its **original objects** are recoverable from freshly proved origin
graphs; scoped expected-old cleanup cannot delete a newly moved or foreign ref.
Mutation snapshots, privileged host guards, retention and retirement retain
their independent authority. Failures consume disk or hold dispatch rather than
erase uncertain work.

## Decisions for the operator

Only **D-1's interpretation** remains a genuine scope question: record “all
ordinary eligible roles; Mutation/SourceLanding keep source-bound snapshots.”
Recommendation: accept that meaning unless there is a concrete measured need to
pool verification snapshots. A literal fourteen-role pool is a separate custody
project, not an enablement switch. This does not require re-approval of the
already-authorized wave 1 or the November 5 ref-only choice.

No answer is needed to finish this artifact. It proceeds under D-1..D-10;
effective host settings, missing historical acceptance, native evidence and
canary observations are execution gates for the caller to resolve, not new
operator preference questions.

--- next stage ---
next: decide
handoff: Record D-1's recommended all-ordinary-roles meaning, keep Mutation snapshots, and apply the ready wave-1 settings at the due rc restarts after prechecks. Then dispatch F1-F4 Code with this folded verification design; serialize inventory/Merge work and preserve the performance packing gate.
artifact: docs/superpowers/plans/2026-11-14-card-0824-all-roles-enablement-plan.md
