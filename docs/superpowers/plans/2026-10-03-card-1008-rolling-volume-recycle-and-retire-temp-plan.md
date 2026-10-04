# CARD-1008: rolling volume recycling and retired-temp cleanup

Date: 2026-10-03. Plan baseline: `c6d5d56b5b4c565d36157e21de9c85029cc45b53`.
Original Plan: task `9de0189e`; TestDesign freeze: task `97e2c86f`, commit
`b365b1afaeeba58695040e696ffce5c02659e439`. Scope reconciliation: task
`f500932f`, branch `feat/card-task-f500932f`, fast-forward from that freeze.

## Outcome and authority

Implement acceptance 2-5 of CARD-1008: recycle the standing runner's disposable
volumes during replacement, safely remove all four temp volumes at retirement,
preserve unpublished work, and record disk reclamation. This dispatch writes the
plan only. It does not authorize a live rollout or execute a destructive probe.

The orchestrator's commissioned split is authoritative over the older CARD-1008
acceptance text: CARD-1008 owns default main recycle, the retired-absent temp guard
and temp down -v. Runner-state/cache opt-ins belong to CARD-1010. Full reads of
CARD-1008, CARD-1010, CARD-0994, CARD-0831, CARD-0980 and CARD-0983 confirm that split
and the required shared-file ordering. No new server/HTTP lease contract is chosen.

The corrected policy is already on master by `af6d03f19f9fc8298301187cecebee2c4b4f5d90`;
its volume section was read with `git show`. Main volume recycling is **scripted
only**, inside redeploy-old / deploy-parent with its own controlled stop. Manual
main stop/removal makes the strict counters refuse and is not a workaround.
The task branch predates that land; do not merge, rebase or fetch to import it.
Code must start from a landed base containing that policy. Master was observed at
`d1d04626740023ce23bbc1ac0159393efa1a40d9` by `git ls-remote` during reconciliation.

Owners read: `docs/project-context.md`, `docs/ops-http.md`,
`docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`,
`docs/docker-stack.md`, and the checkpoint/build-slot owner in
`docs/testing-and-build.md`. Format reference: the CARD-1004 Linux composer plan.

The remaining TestDesign content is **already frozen**. This reconciliation moves
opt-in-only rows explicitly without renumbering, preserves the other fixture
vectors, rebinds PC-117 to the retained strict-null regression, and supplies the
remaining controls' input index. All remaining PCs have methods, inputs and first
assertions; next is Code, subject to the ordinary admission receipt checks below.

## Ground truth

| Card assumption / requested behavior | Code at the baseline | Design consequence |
|---|---|---|
| `redeploy-old` recycles disposable storage after drain. | `scripts/deploy-server2.ps1` calls `deploy-parent` only when `buildVersion != Sha`; `case_deploy_parent` in `scripts/c590-remote.sh` builds/prepares caches/seeds the checkout and runs Compose up. It never recycles these volumes. | Add an explicitly commissioned recycle context to this call; ordinary `deploy-parent` must not acquire destructive behavior by default. Same-SHA healthy retries remain verification-only. |
| Stopping a container makes its volumes removable. | Docker retains references from stopped containers. Compose `state-init` also mounts `work` and `runner-state`; the project includes a separate build-slot broker. | Identify, stop and remove the owned runner/state-init containers without deleting volumes, then prove zero references. Do not stop the broker or blindly down the main project. |
| Main has three disposable and four preserved volumes. | Main private names are `antiphon-runner_work`, `_runner-tmp`, `_dind-data`, `_runner-state`; caches have fixed external names independent of the Compose project. | Exact default allowlist; preserve main state/caches. Opt-ins move to CARD-1010. No prefix scan or prune. |
| A retired, absent temp has null live inventory. | `Assert-ZeroCounters` refuses missing/null fields; `case_retire_temp_runner` independently calls strict `c849_status_zero`, which also rejects null. | Both layers need a retirement-specific absent-placeholder predicate. Do not weaken the shared cache/donor predicate. |
| Cleanup should reclaim space when the disk is low. | Remote retirement currently calls `c849_prepare`, `c849_require_ready` and `c849_budget_gate` before `compose_temp down -v`; the budget gate refuses below 20 GiB. | Read-only preservation checks precede deletion; space-consuming deployment/cache checks follow reclamation. Low disk cannot itself prevent safe retirement. |
| Existing CARD-0912 Cold can rebuild deleted caches. | `c849_cold_proof` requires one running main without overlapping cache mounts, plus retired/absent temp. Cold creates labelled empty roots, checks uid-1654 writes and publishes a schema-2 marker; it does not download a warm payload. | Preserve ordinary Cold and default cache payload/marker identity here. The absent-main maintenance context and its proof move to CARD-1010. |
| Recreating main state is just another deletion. | `/state/runner-store-id` belongs to the state volume. CARD-0953 requires stamped retirement, explicit clear, detached connection and lease expiry before a different store can register. | Default preserves state. The identity-transition opt-in and the unobservable lease waits (B-1) move to CARD-1010; no lease wait is added to default recycling. |
| Zero seats means all work can be discarded. | `RunnerWorkspaceService` uses `/work/worktrees`, repository paths under the runner repository policy, and retained failed/blocked mirrors. Task summary has `runnerId`, status and pending-land fields. | Audit every repository/worktree in the work volume and the complete task census. A stopped process is not publication evidence. |
| The manual publication check can run as root. | The incident's `git rev-list --count HEAD --not --remotes` ran as uid 1654; root can fail dubious-ownership checks. | Explicit uid/gid 1654, check exit codes and numeric output. Errors never become zero. |
| `/tmp` may be cleaned by name. | CARD-0827's named mount copies the image `/tmp`, including `/tmp/antiphon-pty-hosts`, on first mount. | Recreate the entire exact volume using normal copy-up. No sweep within `/tmp`, and no `volume-nocopy` on that mount. |
| CARD-0957 supplies a proven harness. | `77615d506` records 24 rolling groups/66 invocations/227 assertions, 52 cache results and 19 real-Docker outcomes; retained fixture is `scripts/fixtures/c973-rolling-host-cases.mjs`. | Reuse its trace/assertion and real-daemon isolation patterns. These are historical receipts, not executions by this Plan task. |
| One script edit transports recycle context and preview. | `deploy-server2.ps1` writes manifests; `verify-docker-stack.ps1` routes cases; `c590-real.ps1` validates/exports manifest fields to the host shell. | Include both transport files and their tests in scope; reject malformed context/preview values at both boundaries. |

Platform observation: GET `/api/runner-defaults` returned revision 2, with an
automatic Linux runner preference, and GET `/api/session-runners` returned a live
Linux runner, live Windows desktop and offline retired temp at 11:13 UTC on 2026-10-03. This is
an observation, not a pinned fleet layout. Resolve these routes again at dispatch.
Omit `-Runner` for ordinary work; select `-Platform Linux` only for the shell/real
Docker rows that require it. Omit `-Platform` for platform-independent work;
`-Platform Any` explicitly removes a previous OS pin. Production runner/project
identifiers below are the existing deployment contract, not placement instructions.

## Decisions

### D-1: exact targets and an explicit deployment context

Default main recycle set, in deterministic order:

1. `antiphon-runner_work`
2. `antiphon-runner_runner-tmp`
3. `antiphon-runner_dind-data`

D-1a: do not declare `-RecycleRunnerState` or `-RecycleCaches` in CARD-1008.
PowerShell's existing advanced-script parameter binder rejects these unknown
switches before the script body, including dry-run. Do not add compatibility flags
that always refuse: they would imply shipped functionality and preserve unnecessary
maintenance branches. `RecycleOptionsInvalid`, `RecycleStateResetNotAuthorized`
and `RecycleCacheMaintenanceRequired` move to CARD-1010 with the flags; they are
not CARD-1008 refusal codes. Default `all` still recycles main and retires temp.

The manifest carries the explicit default-recycle context, operation identifier
and typed `dryRun` boolean (false for apply). No state/cache opt-in fields or
user volume list exist. Retain strict unknown-field/project validation at wrapper
and bridge. G/PC-24 and G/PC-25 retain boolean validation **only for D-7 dryRun**:
string `"false"`, null, numeric and omitted preview values refuse before SSH/host
mutation. This is independent of the moved opt-in booleans. G/PC-87 still binds the
remaining journal options (dryRun=false and the fixed default context) on resume.

Direct/legacy `deploy-parent` without that context preserves its current behavior.
Prefer a small `c1008_*` helper group in the remote script over a second deployment
driver. Transport functions remain ASCII-only and use literal argument arrays.

Temp retirement always ends in `compose_temp down -v` for exactly its private
`work`, `runner-tmp`, `dind-data`, `runner-state` volumes. Validate the rendered
Compose model: the four names are project-private/non-external and the three cache
volumes remain external with their fixed names. A changed model refuses
`RecycleComposeMismatch`, before down. After down inspect all four private names
and the preserved state/cache names; an unremoved target is a failure, not success.

Rejected: blanket main `down -v`, `docker volume prune`, `docker system prune`,
prefix/substring selection, accepting arbitrary volume names, or silently turning
all calls to `deploy-parent` into a cleanup command. Those alternatives either
delete identity/shared caches or widen the destructive entry point.

### D-2: proof before stop, proof before removal, no unknown-as-zero

Use a host rollout lock, then the existing cache-maintenance lock, in that fixed
order. Recycle/retire and their admission-changing rollout paths must respect the
rollout lock; do not nest a second acquisition of the same lock in helper calls.
The lock protects script operations, not the server's repository mutation lease.

Before stopping anything, require integer `sessions=0`, `runnerSessions=0`,
`queuedTasks=0`, explicit `acceptingNewWork=false`, a durable drain with the expected
redirect, and the other runner accepting for a normal rolling main replacement.
Require a complete task/land census and D-3 publication audit. List the exact owned
container IDs, labels, image digest, state and mounts. Reject unknown containers,
foreign consumers or duplicate runner identities before touching availability.

Then stop the identified runner gracefully, verify it is stopped, and remove only
its exact ID and identified stopped `state-init` IDs, without `-v`. Never use
`docker rm -f` to turn live sessions into a zero result. A running/ambiguous
state-init or unexpected service refuses `RecycleContainerStateUnknown`. Preserve
the build-slot broker/network. A stop failure leaves all volumes intact.

Audit the now-quiescent work volume again with a temporary helper using the
inspected/pinned runner image, explicit `--user 1654:1654`, read-only work mount at
its original `/work` path and an overridden entrypoint that cannot start the
runner. Remove the helper and wait for its removal before the final census.
An inspect failure must never create a missing source volume implicitly.

Immediately before the first removal, re-read counters/routing/land/task evidence
and every selected volume's references using `docker ps -aq` (including exited
containers), backed by inspected mount names and canonical bind-mount overlap
checks. Validate all targets before deleting the first one, and recheck each target
at removal. The stopped/removed main's live inventory may turn null: accept that
only against this operation's saved strict-zero observation and exact stop/removal
receipt, with fresh server-side zeros and unchanged drain. A generic offline main
does not acquire the retired-temp exception.

Use one exact `docker volume rm -- <name>` per target. Docker's own in-use refusal
is a final protection against attachment races; preserve it as
`RecycleVolumeInUse`, never retry with force. No collection-wide optimistic delete.
Recreate via the existing state-init/checkout-seed/Compose up path. Verify image
SHA, mount identities, `/tmp` mode/host assets and caches before clearing the drain.

Typed refusal families (exit 2, plus structured receipt):

| Reason | Required detail / refusal boundary |
|---|---|
| `RunnerStatusMissing`, `RunnerCounterUnknown`, `RunnerBusy` | Runner, field, observed type/value; omitted/null/string/bool/fractional/negative counters refuse. Existing stable names remain usable. |
| `RecycleRoutingActive`, `RecycleRoutingUnknown` | Accepting/drain/redirect or routed-task inconsistency; no stop/remove. |
| `RecycleLandInFlight`, `RecycleLandUnknown` | Pending land task/operation IDs, or unavailable/incomplete census; no remove. Queued/held lands also refuse. |
| `RecycleBoundTasks`, `RecycleTaskCensusUnknown` | Target-bound queued/dispatched/working/blocked/failed task IDs or an incomplete/unavailable response. |
| `RecycleUnpublishedWork`, `RecycleWorktreeDirty`, `RecycleGitAuditUnknown` | Repo/worktree relative path, ref/tip/count and stable cause; no Git stderr/credential URLs. |
| `RecycleVolumeInUse`, `RecycleVolumeCensusUnknown`, `RecycleVolumeIdentityMismatch` | Exact selected volume and owning container IDs, or failed enumeration/changed identity. |
| `RecycleStopFailed`, `RecycleRemoveFailed`, `RecycleReceiptUnavailable` | Last completed boundary and exact retained/removal state; leave drain set. |
| `RecycleResumeMismatch`, `RecycleDiskUnknown` | Receipt/source/volume generation mismatch, or unparseable df; no deletion on an unknown before observation. |

Task census implementation uses documented endpoints, not board previews. Start
with the deployment's resolved project scope, GET
`/api/agent-tasks?projectId=<id>&unscoped=include&includeChecks=true` without a
`since` or status window, and follow `excluded.byProject` with explicitly scoped
reads until the census closes. Reconcile IDs/counts and recheck exclusions for
new scopes; unreadable scopes refuse. The current list has no pagination and
must not be given invented `runnerId`, `limit` or page query parameters. Inspect
`runnerId` locally; retain only IDs/status/ref facts in evidence, not goals/results.
Pending `landRequestedAt`/`landStartedAt` and detail `landRequest` are evidence;
the pipeline preview alone cannot establish no land. Freeze DTO/null/legacy
interpretation in TestDesign. Check pending lands across the closed census,
including succeeded tasks, because task status is not landing status.

The no-new-land interval remains an orchestrator rollout precondition. Scripts
recheck immediately before removal and refuse a changed snapshot. No script-only
lock claims to exclude an independently initiated server land atomically. If the
caller needs an atomic fleet maintenance lease, that requires a separate server
contract; this plan does not invent an HTTP lease or claim that stronger guarantee.

### D-3: unpublished-work custody is independent of liveness

Enumerate all repositories/mirrors in the exact work volume, not just the primary
checkout or worktrees whose tasks are Succeeded. Use the runner's repository
policy roots, `/work/repos`, `/work/worktrees`, Git common directories and
`git worktree list --porcelain`; include detached worktrees, standalone clones,
bare mirrors, and local branch/tag refs without a checked-out worktree. Bound
traversal to this volume, reject escaping symlinks/broken gitdir pointers and
in-progress rebase/merge/index-lock state. An unreadable entry is an audit refusal.

Run `git rev-list --count HEAD --not --remotes` **as uid 1654**, checking exit status
and a single nonnegative integer. This reproduces the manual incident check, but
does not let another remote mask an unpublished origin commit: prove all remote
tracking refs are origin's, or use the stronger
`git rev-list --count HEAD --not --remotes=origin` for the verdict. Check each local
branch/tag tip similarly; bare mirrors need explicit tip/ref enumeration, not a
possibly absent HEAD. Record the tip hashes and comparison-ref digest.

Require a current origin ref advertisement to agree with the local origin
comparison set before trusting zero; stale/deleted/missing refs, missing objects,
shallow/partial history that cannot prove ancestry, or failed remote observation
are `RecycleGitAuditUnknown`. Do not turn `git` exit 128, dubious ownership, timeout
or empty stdout into zero. Disable optional locks and lazy object fetching in the
read-only helper; it must refuse if proof needs a write. Do not add `safe.directory=*`.
The baseline/live origin audit can run before stop; repeat the local tip/ref census
after stop against that captured advertisement and refuse drift. An already-absent
temp uses the same read-only uid-1654 helper and explicit noninteractive origin
observation; no production secret/home mount is needed for this repository.

Refuse nonzero unpublished counts, dirty index/worktree or untracked work, and
Blocked/Failed tasks bound to the runner even if their currently inspected HEAD
looks published. List task IDs and paths for caller recovery. Recovery is separate:
publish/salvage using the existing runner WorkspacePublish path, resolve or retire
the task's workspace obligation, refresh refs deliberately as uid 1654, then retry.
The cleanup operation never commits, pushes, resets or deletes a suspicious repo.
Ignored build products are disposable; this is a tip/ref/worktree audit, not a
promise to recover arbitrary unreachable objects from old reflogs.

### D-4: narrow retired-absent exception, in both languages

Keep `Assert-ZeroCounters` strict for ordinary main and cache maintenance calls.
Add a retirement-specific predicate with these jointly required facts:

- Valid `retiredAt`, `draining=true`, `retireWhenIdle=true`, `redirectTo=server2`.
- Present integer `sessions=0` and `queuedTasks=0`; no routed tasks from D-2.
- Present `runnerSessions`: either integer zero, or null only for the exception.
- For null: `available=false`, `dispatchEligible=false`, `acceptingNewWork=false`
  and a successful current host census proving **no project container**, including
  stopped/created/state-init containers. Empty stdout after SSH failure is not absence.
- Main is accepting and the retirement timestamp/redirect still match when the
  host repeats the predicate immediately before cleanup.

The wrapper's SSH census and the remote host's census must agree. Missing fields,
malformed values, wrong redirect, unknown census, or a new container invalidate the
exception. Present-container plus null still returns `RunnerCounterUnknown`;
`deploy-temp` retains `TempContainersRemain`. Do not change `c849_status_zero` to
accept null for donors, Reset, Prune or Seed. An ordinary temp with a proven live
zero inventory may be gracefully stopped/removed under D-2 before final `down -v`.
An already-exited container with null inventory remains a reviewed recovery case,
as explicitly required by this brief; CARD-0994's broader original proposal is
not silently admitted.

Already-absent volumes are recorded as `alreadyAbsent`; still execute the validated
temp Compose down and produce a fresh receipt. Keep the API row retired. No
`/drain/clear`, registration or source checkout is needed just to reclaim temp.
Missing/corrupt Compose inputs refuse; do not guess a replacement volume model.

### D-5: durable receipts, partial failure and disk ordering

Store a schema-versioned JSON journal under the existing host evidence root outside
all recycled volumes, and copy it to the wrapper's `.antiphon/rolling-server2/<id>/`.
Bind source SHA, operation ID, project, flags, retiredAt/old store where applicable,
Compose digest, original container IDs/images, each volume's name/driver/CreatedAt/
labels, audit digests, counters, task/land observations and timestamps. Record
`preflight`, `stopped`, `containersRemoved`, per-volume `removed|alreadyAbsent`,
`recreated`, `verified`, and `completed` using atomic file replacement. Refusal
receipts include the completed subset; a later failure never erases earlier removal.
Evidence-write failure before mutation refuses; failure after mutation reports
partial completion and keeps the drain.

Observe Docker's data-root filesystem with numeric `df -Pk` available blocks,
convert to bytes without locale-dependent parsing, and retain raw before/after
rows. Before is just before removal; after is after deletion and before new builds
or seeding. Print exactly one summary on each attempted recycle, for example:

```text
C1008_RECYCLE project=antiphon-runner operation=<id> removed=3 alreadyAbsent=0 freeBeforeBytes=<n> freeAfterBytes=<n> deltaBytes=<signed-n> outcome=completed receipt=<path>
```

Use `outcome=partial|refused|preview` and unknown fields explicitly when necessary;
never invent zeros or promise a positive delta on a shared filesystem. Final deploy
verification and this deletion receipt are distinct outcomes.

Do not put `c849_budget_gate` before temp deletion, or require a warm cache/helper
image merely to prove that unrelated cache volumes were preserved. Read-only
cache identity/external-mount checks suffice there. For main, recycle only after
all safety preconditions, then run CacheDiskLow **before** image build/seed/up
allocations. If space remains below threshold, stop with the reclaim receipt,
main drained and temp accepting. `deploy-temp`'s existing early disk gate is
unchanged: this feature cannot repair insufficient space before temp exists.

Introduce `-ResumeRecycle <operation-id>` for an incomplete committed journal,
with a strict non-path operation-ID grammar. Revalidate source/flags/project,
retirement, fresh server state and volume generations. Already removed names may
be absent; newly appearing or recreated generations refuse rather than being
deleted again. An interrupted `volume rm` whose journal update did not land is
resolved by inspection. A volume known absent after removal can be skipped, but
an unknown generation cannot. A partial up/seed records its owned new generations;
resume verifies/completes those without recycling them a second time. Never use
`buildVersion == Sha` alone to hide an incomplete journal, and never recycle a
healthy same-SHA runner merely because the command was rerun.

Rejected: swallowing `rm` failures, treating every inspect error as absence,
rerunning the whole deletion loop after a failed up, journaling inside `/work` or
`/tmp`, and accepting an arbitrary evidence path as authority.

### D-6: MOVED to CARD-1010 — runner-state/cache opt-ins

D-6 never modifies D-2 for the default path. The original maintenance exception
replaced accepting temp with both consumers closed **only when an opt-in was
selected**. Default main replacement still requires accepting temp, saved live-zero
proof, this phase's own stop/removal and a fresh final census. Main state and all
three caches remain preserved; no cold seed or identity-reset workflow is added.

CARD-1010 owns both flags, state retire/clear/new-store registration, shared-cache
quiescence, absent-main cold proof/seed/verification, helper/marker recovery and
maintenance-only transport. Rejected here: inventing lease fields, assuming a fixed
90-second sleep, selecting a new registration wait protocol, or weakening the
default accepting-temp guard to bypass B-1. `Register` checks `slot.Live`,
`slot.LeaseUntil` and `LastDisconnect.AtUtc + LeaseSeconds`; status exposes
neither lease deadline nor configured duration (default 90). PC-121/122 are moved
specifically because B-1 makes their original predicates unobservable.

Moved G/PC IDs: **13, 29, 118–144, 155, 156** (31 guard/control pairs).
V-11 and RD-9/RD-10 move with them. Each row remains marked in its original table.
G/PC-129's maintenance-seed mutation moves as requested; V-1/RD-1 still require
zero default cold-seed calls and preserved cache/marker identity. G/PC-117 remains
because D-4 must not weaken the shared donor/reset/prune null guard; V-9 now owns it.
G/PC-24/25 and PC-87 retain only the preview/default-context inputs defined in D-1.
CARD-1010 starts after CARD-1008 lands and re-baselines against that implementation.

### D-7: dry-run is a preview, not an authorization receipt

Implement `-DryRun` for explicit `redeploy-old` and `retire-temp`. It prints ordered
exact removal candidates, preserved names, container owners, current counters,
refusal reasons and available disk. It performs GET/SSH census/inspect/Compose
config reads only, plus owned evidence writes. It makes no POST, drain clear,
container stop/remove/start, Docker volume create/remove, seed-marker write, image
build, checkout mutation or secret provisioning call. Intercept before the generic
host setup path, which currently mutates directories/checkouts.

If an absent-container Git audit needs a helper, preview says `auditPending=true`
rather than creating one or claiming authorization. Apply always obtains fresh
proofs and cannot consume preview as a destructive permit. Invalid flags refuse in
preview too. `-DryRun -Phase all` refuses rather than simulating unperformed drains.

## Slices and owned files

Plan and TestDesign edit this plan; Code owns the following closed footprint.
Read dependencies do not confer permission to change server DTOs, drain services,
runtime leases, Docker daemon settings or runner workspace services.

| Slice | Exact files / scope | Work and exit evidence |
|---|---|---|
| S1: detecting fixtures | `scripts/test-deploy-server2.ps1`; `scripts/fixtures/c727-fake-http.ps1`; `scripts/fixtures/c727-fake-verify.ps1`; new `scripts/fixtures/c1008-recycle-cases.json`; new `scripts/fixtures/c1008-fake-docker.sh`; new `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`; additions to `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` | Freeze state/command traces and introduce the V methods. Commit/push; CP-1 must fail the retired-absent success assertion against unchanged production, not fail from missing dependencies. |
| S2: default recycle and custody guards | `scripts/deploy-server2.ps1`; `scripts/c590-remote.sh`; `scripts/c590-real.ps1`; `scripts/verify-docker-stack.ps1`; S1 tests/fixtures | D-1..D-5 default main + narrow temp exception; task/land/Git proof, exact removal, journals, disk receipt, partial recovery. Commit/push this complete behavior before preview integration. |
| S3: read-only preview | S2 production paths; S1 tests/fixtures | D-7 dry-run with strict default-context/preview transport, independent of opt-ins. Preserve ordinary Cold/Seed/Reset/Prune. Commit/push, then CP-2 on S1-S3. Maintenance work and verify-card0849-caches.ps1 edits move to CARD-1010. |
| S4: regression, real comparison and docs alignment | new `scripts/fixtures/c1008-recycle-real-cases.mjs`; new `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleDockerTests.cs`; `scripts/test-deploy-server2-jq.ps1`; `scripts/fixtures/c973-marker-reader.sh`; S1 fixture count pins; `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`; `docs/docker-stack.md`; `server/Bundles/orchestrator.md` only if a rollout instruction there conflicts with the landed policy | Freeze the real fixture's allowlist/cleanup and final rosters; align examples/pins after the separate Docs task lands. Commit/push and run CP-3..CP-5. Amend this plan only for reviewed fixture/count reconciliation. |

Scope list for dispatch is these literal paths, plus this plan path, not `scripts/**`
or `tests/**`. `RemoteScriptContractTests.cs` and `c590-remote.sh` collide with
CARD-0980/CARD-0983; serialize work touching them. Re-read the current landed file
before integration, preserve their test additions, and update the frozen regression
count if their changes alter it. The policy Docs change has landed; preserve its scripted-only main policy.
CARD-1010 starts only after CARD-1008 lands. No parallel Code dispatch into these paths.

## Verification design

All new script tests execute production functions or the actual wrapper with
controlled HTTP/SSH/Docker boundaries; textual name checks alone cannot discharge
destructive behavior. Fake Docker must model stopped-container references,
exact-name matching, failure exit codes, Compose-created generations and copy-up.
Use real disposable Git repositories with published and unpublished commits for
audit cases; fake only the Docker/SSH transport, not rev-list's answer.

Each V method below is one `[Test]` result, with explicit case vectors inside it;
case/assertion counts are separate. New process-spawning methods carry the existing
assembly-local `ParallelLimiter<ProcessSpawnLimit>`. TUnit wrapper children inherit
the checkpoint's build-slot lease and launch no additional build/test driver. Do
not run the Pty assembly concurrently. Require bash, jq, Git, Node and pwsh for the
Linux lane; unavailable tools are a not-run qualification, not a green skip.

### V-matrix

Class abbreviations in this table: Remote = `RemoteScriptContractTests`, Rolling =
`RollingVolumeRecycleScriptTests`, Real = `RollingVolumeRecycleDockerTests`.
Methods and assertion labels below are the test-design contract, not claims that
these methods already exist. Every fault has an adjacent accepted control.

| ID | Named test | Cases and decisive assertion label |
|---|---|---|
| V-1 | Remote.`C1008_Recycle_exact_default_volumes` | Exact default three removed in order; main state, three caches, all temp names, `antiphon-runner_work-extra`, `schoolrevision-staging` and `openclaw-state` survive byte/identity checks; broker stays running. `recycle-exact-defaults`. |
| V-2 | Remote.`C1008_Recycle_refuses_references_and_unknown_census` | Own runner/state-init stop+rm precedes volume removal; foreign running and stopped consumers, bind overlap, duplicate identity, failed/empty-on-error ps/inspect, concurrent attachment all refuse; no earlier target deleted on preflight failure. `recycle-reference-refusal`. |
| V-3 | Remote.`C1008_Recycle_audits_work_as_1654` | Real Git audit runs as 1654 across linked/detached worktrees, standalone clone and bare mirror, including path spaces; root's dubious ownership is an error control, never zero. `recycle-audit-uid`. |
| V-4 | Remote.`C1008_Recycle_refuses_unpublished_and_dirty_work` | Unpushed HEAD, local branch without checkout, another remote containing HEAD but origin lacking it, dirty/index/untracked work, blocked/failed task; published clean origin tips pass. `recycle-work-preserved`. |
| V-5 | Remote.`C1008_Recycle_refuses_uninspectable_git` | Git exit 128, timeout, nonnumeric/empty count, unreadable/broken gitdir, missing object, stale origin refs, escaping link and half-rebase refuse before rm. `recycle-git-unknown-refuses`. |
| V-6 | Remote.`C1008_Recycle_preserves_tmp_copyup` | Actual intended Compose mount has copy-up, recreated `/tmp` has mode 1777 and image pty-host assets; trace contains no within-volume name sweep. Real Docker corroborates copy-up. `recycle-tmp-assets`. |
| V-7 | Remote.`C1008_Recycle_resume_requires_matching_receipt` | Interrupt after stop, container removal, each volume rm and partial up; resume completes only remaining work; source/flags/project/stamp/volume-generation drift refuses; healthy same-SHA retry deletes nothing. `recycle-resume-generation`. |
| V-8 | Remote.`C1008_Recycle_receipt_records_disk_and_partial_failure` | df before/after on data-root filesystem; signed delta, units, once-only line; disk probe/receipt write failure and partial rm preserve honest boundary data, including already absent target. `recycle-receipt-facts`. |
| V-9 | Remote.`C1008_Retire_temp_rechecks_absence_and_retirement` | Host accepts absent/null and ordinary strict-zero path; separately execute unchanged c849_status_zero with donor/reset/prune null (refuse) and explicit zero (accept); new container between wrapper/host, timestamp change, missing field and census failure refuse. `retire-host-proof`. |
| V-10 | Remote.`C1008_Retire_temp_reclaims_below_cache_disk_gate` | Low df does not block safe temp down; missing warm marker does not create caches; main reclaim precedes build disk gate, remaining-low stops before allocations; deploy-temp still refuses low disk. `recycle-disk-order`. |
| V-11 | **MOVED to CARD-1010**: Remote.`C1008_Recycle_optins_require_maintenance_proofs` | Entire maintenance method retired here; default state/cache/marker preservation and zero cold seed remain V-1/RD-1; strict shared null regression remains V-9 (PC-117). Not a CARD-1008 result. |
| V-12 | Remote.`C1008_Recycle_dry_run_never_mutates` | Both supported phases list exact targets/preserved names and blockers, offline audit pending; trace forbids POST, stop/rm/up/run, volume create/rm, seed/checkout mutations; apply rechecks changed facts. `recycle-preview-readonly`. |
| V-13 | Rolling.`C1008_Retired_absent_null_is_accepted` | Retired/offline/absent/null with integer zeros and no routed work reaches only retire host case, preserves retirement, and reports all four volume outcomes. `retire-absent-null-accepted`. |
| V-14 | Rolling.`C1008_Present_or_unknown_temp_keeps_null_refusal` | Running/exited/state-init container + null, census error, omitted live counter, strings/bools, wrong redirect and nonretired row refuse with exact reason; no host delete and no clear. `retire-null-stays-closed`. |
| V-15 | Rolling.`C1008_Busy_routed_and_land_in_flight_refuse` | Each nonzero counter, accepting row, routed queued/dispatched/working task, Blocked/Failed mirror owner, queued/held/running land on succeeded task, excluded-project/unscoped task and API failure refuse before host mutation. `recycle-work-gates`. |
| V-16 | Rolling.`C1008_Same_sha_and_partial_retries_are_safe` | Healthy same SHA verifies only; offline/partial-operation same SHA resumes from journal; cache/registration failure never clears drain. `recycle-wrapper-resume`. |
| V-17 | Rolling.`C1008_Option_manifest_is_strict` | Preview dryRun arrives as a boolean; source-extracted real wrapper/bridge context validators also receive raw malformed JSON values for PC-24/25 (the script binder is not the test seam); removed opt-in switches fail parameter binding before any script effect, removed manifest fields refuse as unknown; source full SHA/operation ID/stamp survive JSON transport; missing/invalid/misplaced fields and shell metacharacters reject before SSH; direct deploy-parent remains nondestructive. `recycle-manifest-strict`. |
| V-18 | Rolling.`C1008_Legacy_rolling_and_jq_rosters_remain` | Execute existing `test-deploy-server2.ps1` through jq driver modes present/absent/missing-shell/failing-shell, once each; retain T1-T24 expectations except intentional new receipt/order detail. New C1008 targeted groups stay outside the historical all-roster. `rolling-regressions-preserved`. |
| V-19 | Rolling.`C1008_Documentation_and_transport_pins_match` | D-8 claims, strict transport, exact volume allowlists, ASCII bytes and unchanged host/nested lane boundaries agree; real assertions of rendered config as well as sentence pins. `recycle-doc-contract`. |
| V-20 | Rolling.`C1008_Refusal_receipts_do_not_leak_secrets` | Secret-bearing Git stderr/HTTP sentinel and malformed filenames never enter public logs/JSON; receipt retains typed IDs/reasons, original failure not `UnhandledExit`. `recycle-receipt-custody`. |
| V-21 | Real.`C1008_Real_docker_comparison` | Execute the RD matrix below against real isolated Docker/Git; exact result/identity/cleanup census and base/new differences. `recycle-real-comparison`. |
| R-1 | Existing Remote cache methods | Preserve the recounted 54-result C849/C912/C973/C944/C951/C976/C946/C957 selection from 36 methods; refresh at Code admission. No fallback to weakened cache gates. |
| R-2 | Existing Docker/plan contracts | Full `DockerStackContractTests`, `DindRunnerContractTests`, `DockerStackSmokeCommandTests`, `DockerStackDocumentationTests`, `CheckpointImportTests`, `CheckpointManifestTests`; preserve compose isolation, command transport and import schema. |

### Real-Docker comparison, adapted from CARD-0957

New retained fixture `scripts/fixtures/c1008-recycle-real-cases.mjs` follows the
existing c973 fixture: require `/.dockerenv` and daemon Name equal to hostname,
use that isolated nested daemon, unique project labels/names, a random-loopback
status/task endpoint and an exact created-object ledger. Refuse a host/sibling
daemon. Record base SHA, tested SHA, script digests, Docker/Compose versions and
shim inventory. The base is this plan's full baseline SHA, selected with `git show`;
never reset/rebase either worktree. Base failure due to absent new command syntax
is not detecting evidence; drive the common wrapper/host entry points.

Keep Docker stop/rm/volume rm/ps/inspect, Compose down/up private-volume semantics,
filesystem payloads, df and Git history real. Shim only lane/root relocation,
private status endpoints, expensive image build/provider startup and transport to
the isolated daemon. Use a tiny fixture image containing `/tmp/antiphon-pty-hosts`
and a uid-1654 Git helper, not a production provider launch. Archive sanitized
status/command/result JSON outside all fixture volumes. No live fleet identifiers
may be forwarded to a production daemon; logical production names are remapped
only by the fixture's fixed unique-name map.

| RD | Required real outcome |
|---|---|
| RD-1 | Base retains disposable payloads across common redeploy boundary; new script removes/recreates the three, retaining state/cache sentinels and image `/tmp` assets. |
| RD-2 | Present retired temp with null inventory refuses on both versions. Exited state-init alone still counts as present. |
| RD-3 | Retired absent/null temp: base refuses; new path removes all four existing volumes, leaves API retirement and main/cache identities intact. |
| RD-4 | Retired absent/null temp with all four already absent: new path succeeds with four alreadyAbsent entries and no new volumes. |
| RD-5 | Separate running and stopped foreign containers referencing a target cause refusal; no force-removal and no lost sentinel. |
| RD-6 | Prefix neighbours and unrelated project volumes remain identical, including schoolrevision/openclaw logical sentinels. |
| RD-7 | Real uid-1654 linked/detached/unpublished/bare repositories: unpublished or dirty blocks, published clean control removes; root ownership failure is not green. |
| RD-8 | Partial rm injected after a real first deletion: receipt is partial; matching resume deletes only remaining originals; recreated-generation intrusion refuses. |
| RD-9 | **MOVED to CARD-1010**: all cache maintenance outcomes (4 changed). |
| RD-10 | **MOVED to CARD-1010**: all state identity transition outcomes (3 changed); B-1 lease contract remains that card’s decision. |
| RD-11 | Wrapper census says absent, then fixture creates a container before host deletion: host refuses. Census failure cannot look absent. |
| RD-12 | Low-space value is the only df shim: temp cleanup runs, main post-reclaim allocation gate holds. Real df values are still retained separately; no claim of physical low-disk pressure. |

The freeze below expands retained RD-1..RD-8 and RD-11..RD-12 into 32 named
base/branch outcomes, including rejection controls. RD-9/10 retain moved IDs only. Do not copy CARD-0957's 19 as this
card's result count. Finally remove only recorded fixture container IDs, volumes
and images, check exit codes and verify absence; retain the evidence root. This
comparison proves Docker semantics, not a live rollout, cache warmup duration or
production registration lease timing.

### Post-land positive controls

Post-land SourceLanding Mutation only; all controls remain pending after Code.
Each cycle uses `/*/*/<Class>/<ExactMethod>` from the table, one selected method;
all case vectors belonging to that method run, never the entire class. Mutate
production only, commit/restore under the Mutation custody rules, retain first
assertion failure and restored-green receipt. Zero tests, missing jq, build errors,
timeouts and harness failures are not red. A SourceLanding snapshot is never
committed/pushed; repairs require a separate commissioned task.

| PC | One concrete production mutation | Detecting test / first intended label | Frozen input / expected first assertion |
|---|---|---|---|
| PC-1 | Include standing runner-state in the default allowlist. | `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` / `recycle-exact-defaults`. | Default main with all fourteen sentinels: main state identity/payload survives. |
| PC-2 | Replace all-container reference census with running-only. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` / `recycle-reference-refusal`. | Foreign exited container mounts work: work retained and no force removal. |
| PC-3 | Change only the work-audit Docker user from 1654:1654 to 0:0; leave exit handling intact. | `RemoteScriptContractTests.C1008_Recycle_audits_work_as_1654` / `recycle-audit-uid`. | Published Git graph owned by 1654: audit argv selects 1654:1654 before effect checks. |
| PC-4 | Drop origin-only ancestry comparison so another remote masks HEAD. | `RemoteScriptContractTests.C1008_Recycle_refuses_unpublished_and_dirty_work` / `recycle-work-preserved`. | B exists only on a second remote, origin has A: B and work retained. |
| PC-5 | Accept failed Git inspection as empty/zero. | `RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git` / `recycle-git-unknown-refuses`. | Git exits 128 with empty stdout: work retained with GitAuditUnknown. |
| PC-6 | Allow null live inventory regardless of host census. | `RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal` / `retire-null-stays-closed`. | Retired temp has null live count plus exited container: no host deletion. |
| PC-7 | Restore unconditional null refusal in retire-temp. | `RollingVolumeRecycleScriptTests.C1008_Retired_absent_null_is_accepted` / `retire-absent-null-accepted`. | tempRetiredAbsent and zero-byte successful census: four temp outcomes; no refusal. |
| PC-8 | Skip the remote retirement/absence recheck. | `RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` / `retire-host-proof`. | New project container appears between wrapper and host: every temp sentinel retained. |
| PC-9 | Ignore pending land evidence on succeeded tasks. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` / `recycle-work-gates`. | Succeeded task with pending land timestamp: no host mutation. |
| PC-10 | Restore pre-deletion CacheDiskLow on retirement. | `RemoteScriptContractTests.C1008_Retire_temp_reclaims_below_cache_disk_gate` / `recycle-disk-order`. | Retired absent temp below disk threshold: four private volumes removed. |
| PC-11 | Resume deletion without checking volume generation. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` / `recycle-resume-generation`. | Removed work name reappears with recreated timestamp: no remaining original removed. |
| PC-12 | Let DryRun enter the apply branch. | `RemoteScriptContractTests.C1008_Recycle_dry_run_never_mutates` / `recycle-preview-readonly`. | DryRun for both supported phases: forbidden mutation trace is empty. |
| PC-13 | **MOVED to CARD-1010**; Admit cache recreation under the ordinary rolling/temp-active context. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` / `recycle-optin-proof`. | MOVED to CARD-1010; no active input obligation here. |
| PC-14 | Replace after-df with before-df when writing the receipt. | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` / `recycle-receipt-facts`. | Before 1024/after 3072 blocks: receipt after bytes equals 3145728. |

TestDesign must order the labelled substantive assertions before generic exit/count
assertions that could mask a mutant. Pin each concrete replacement to the eventual
production helper and record why the assertion is reachable; do not count an inline
self-mutating text assertion as post-land execution.

### Inspection

TestDesign task `97e2c86f` froze verification at
`b0b9aa34d91da9fbe3b84f5c1f945173a1aa8af6` and pushed `b365b1af`.
Plan task `f500932f` reconciles that freeze against the commissioned scope split.
The historical inspection roster below remains provenance, including moved work;
active obligations are the marked V/R/RD/G/PC tables, not that historical roster.
No implementation, PC execution, Docker mutation or live rollout is claimed.
Counts are derived from source and refreshed at Code admission.

| Bodies read | Boundaries and coverage |
|---|---|
| Entire plan; full CARD-1008/0994/0831/0980/0983 via `card.ps1 get`; CARD-0891 freeze at `7bd76794`; testing manifest/runner/Mutation and orchestration stage owners | Admission, scope, receipt limits and checkpoint schema |
| Entire `RemoteScriptContractTests.cs`, including `LinuxShell`, `Block`, `ColdSeedHarness`, `CacheStatusHarness`, `C973ReaderHarness`, prepare/prune/seed helpers; entire `test-deploy-server2.ps1`, jq driver, both c727 fakes, c973 marker reader and real host fixture | Historical V-1..V-21 (V-11 now moved), R-1; the existing status-token jq fake cannot prove JSON types and is not reusable for C1008 status verdicts |
| Entire `deploy-server2.ps1`; `c590-real.ps1` live-case validation/exports; verifier deploy/retire dispatch; `c590-remote.sh` status-zero, cold facts/proof/probe/seed/ready, deploy and retire bodies; Compose private/external volume declarations | V-1..V-17; transport must execute real wrapper/host guards, not just c727 host-success stub |
| Entire `DockerStackDocumentationTests.cs`, `DockerStackDocuments.cs`, `DelegateScriptRunner.cs`, `ProcessSpawnLimit.cs`; policy branch's new `Main_volume_recycling_is_scripted_only` body | V-19/R-2; literal args, LF fixtures, whitespace-normalized prose, owned processes |
| `AgentTaskSummaryDto`, list envelope/scope/exclusion DTOs, detail and `LandRequestStatusDto`; scoped-list fixture IDs/setup; documented GET query contract | V-4/V-15; complete census includes unscoped/excluded projects and terminal owners with nonterminal lands |
| `PhoneHomeRunnerStatusDto`, directory `Status` and `Register`, all `PhoneHomeRunnerRetirementIdentityTests` bodies | Historical V-11/RD-10/B-1 now MOVED to CARD-1010; V-16 keeps ordinary preserved-store retries; fake clock controls establish server semantics, not an HTTP lease-proof field |
| `PlanTableImporter`, complete Import/Manifest test bodies; source Test/Arguments attributes of DockerStackContract, DindRunnerContract and DockerStackSmokeCommand classes | CP-1..CP-5 schema and counts; those three unchanged classes were counted, not represented as newly inspected behavioral coverage |

The policy correction and added `Main_volume_recycling_is_scripted_only` test are
contained in master at `af6d03f1`, inspected with `git show`. This task's older
base intentionally does not include them. CP-4's post-policy **207** is DERIVED
from that source roster (112+23+35+11+20+6), not an executed receipt on this branch.

### Delivery inventory

No new asynchronous message/session-delivery path is commissioned: no queue
producer, user-session destination or UserPrompt exists in this script change.
Real queue busy/eligible/crash delivery cases are excluded for that reason. If Code
introduces notifications or async work submission, this exclusion expires and
TestDesign must supply producer-to-recipient tests, including a matching complete
UserPrompt for session input. A successful host request never proves delivery.

The changed cross-process operations still require end-to-end effect evidence:

| Producer -> destination | Durable identity and persistence | Recovery and observed outcome |
|---|---|---|
| Rolling wrapper -> verifier -> real bridge -> host recycle | Full source SHA + operation ID + project + options; wrapper manifest and host journal outside selected volumes | V-17 executes validation and transport with intercepted SSH/SCP, then the production host entry; V-7/V-8 restart a fresh child from the persisted journal. Docker inspection and volume payloads, not a host-case trace alone, prove removal/recreation. |
| **MOVED to CARD-1010**: Host -> maintenance cold-seed host case -> deploy continuation | Same operation, pinned helper image, cache generations and schema-2 marker | V-11 interrupts before request, after cache rm/before seed, after each create/probe and after marker publication/before reply. Fresh invocation reconciles the journal; real `c849_require_ready` and `case_verify_runner_caches` observe new roots before any clear. RD-9 corroborates filesystem ownership/mount behavior. |
| Host journal -> wrapper receipt copy | Same operation/source/project, journal phase and per-volume outcomes | V-8 fails SCP after deletion; wrapper cannot report completed or clear admission, and retry retrieves the existing host receipt without another deletion. Both copies and current Docker generations must agree. |
| **MOVED to CARD-1010**: Wrapper retire/clear/hold -> existing server -> replacement registration | Runner ID, old/new store, retirement stamp, requested SHA | V-11 checks POST ordering and final observed new-store/SHA status; a POST 200 or container health is insufficient. B-1 prevents claiming the prerequisite lease observation is available. |

Fakes substitute only HTTP, SSH, Docker and controlled fault boundaries. Their
command logs prove requested operations, not actual Docker copy-up, uid isolation,
server persistence or registration leases. Real Git objects replace canned counts.
RD proves Docker/Git effects on an isolated daemon, not live fleet routing, elapsed
production leases, a provider conversation or package warmup performance.

### Proves it works now

V-1..V-10 and V-12..V-21 retain their methods and primary labels: **20 V methods**.
Only V-11 moves. CP-2 owns **11 Remote + 8 Rolling = 19** methods; CP-5 owns the
single Real method. PC-117 uses V-9’s retained strict-null comparison. Each is one
`[Test]`, with internal vectors and **no Arguments/data-source expansion**. The
following fixture contract is to be implemented in S1, not generated by this task.

**Fixture encoding.** `c1008-recycle-cases.json` stores literal JSON values plus
ordered observations, never PowerShell truthy string substitutions. Omitted keys
are removed, not encoded as null. `c1008-fake-docker.sh` maintains a file-backed
volume/container ledger and records argv arrays, exits and observations. Every
unknown Docker verb/format refuses. All paths resolve below one printed owned
scratch root; fake mount paths are never forwarded to a real daemon. Frozen aliases:
`M=antiphon-runner`, `T=antiphon-runner-temp`,
`P=antiphon-runner-cache-nuget-packages`,
`S=antiphon-runner-cache-nuget-scratch`,
`N=antiphon-runner-cache-npm-content`. Expand these aliases before the production
entry, including in expected argv; they are not prefix selectors.

`volume ls -q` initially exits 0 with one LF-terminated name on each line, in this
order: `M_work`, `M_runner-tmp`, `M_dind-data`, `M_runner-state`, `T_work`,
`T_runner-tmp`, `T_dind-data`, `T_runner-state`, `P`, `S`, `N`,
`antiphon-runner_work-extra`, `schoolrevision-staging`, `openclaw-state`.
Each starts with payload `sentinel:<exact-name>:old\n`. Each volume inspect returns
a one-element array of this shape, replacing the exact name/project/role:

```json
[{"Name":"antiphon-runner_work","Driver":"local","Options":{},"CreatedAt":"2026-10-03T09:00:00Z","Mountpoint":"/fixture/docker/volumes/antiphon-runner_work/_data","Labels":{"com.docker.compose.project":"antiphon-runner","com.docker.compose.volume":"work"}}]
```

Cache labels instead are `io.antiphon.owner=server2-runner`,
`io.antiphon.cache-schema=1`, `io.antiphon.cache-role=<nuget-packages|nuget-scratch|npm-content>`.
Private Compose volumes are non-external; all three caches are external under the
fixed names. Mutate each property independently, including `{}` versus null options,
foreign driver/labels/name, external private target, missing/malformed Compose input,
and canonical bind ancestor/descendant overlap. Missing volume: successful ls omits
it, exact inspect exits 1 with `No such volume`, no object is created. Failed ls,
daemon-unavailable inspect and corrupt inspect JSON are separate unknown cases.

Container IDs use `1` repeated 64 for main, `2` repeated 64 for stopped state-init,
`3` repeated 64 for broker, `4` repeated 64 for temp, and `5` repeated 64 for foreign.
Images are `sha256:` plus `a` repeated 64. Main inspect is a one-element array with
`Id`, `Image`, `State:{Running:true,Status:"running"}`,
`Config.Labels:{com.docker.compose.project:"antiphon-runner",com.docker.compose.service:"session-runner"}`
and seven volume Mounts. Each mount has Type `volume`, exact Name, canonical Source,
RW true, and Destination `/work`, `/tmp`, `/var/lib/docker`, `/state`,
`/home/app/.nuget/packages`, `/var/cache/antiphon/nuget-scratch`, `/home/app/.npm/_cacache`.
State-init has the same project, service `state-init`, State false/`exited`, work/state
mounts. Broker has service `build-slots`, State true/`running`, and no selected mount.
`ps -aq --no-trunc` emits all three IDs; project/service/volume filters select from
the actual ledger, including exited and created containers. Stop changes State;
rm without `-v` removes only an exited exact ID; referenced volume rm exits 1 and
keeps its payload. At each mutation the fixture persists state before returning.

Frozen baseline status documents (SHA `a` repeated 40, old store
`11111111-1111-1111-1111-111111111111`):

```json
{
  "mainDrained": {"runnerId":"server2","runnerStoreId":"11111111-1111-1111-1111-111111111111","epoch":7,"available":true,"dispatchEligible":true,"acceptingNewWork":false,"draining":true,"redirectTo":"server2-temp","retireWhenIdle":false,"retiredAt":null,"sessions":0,"runnerSessions":0,"queuedTasks":0,"buildVersion":"old"},
  "tempAccepting": {"runnerId":"server2-temp","available":true,"dispatchEligible":true,"acceptingNewWork":true,"draining":false,"redirectTo":null,"retireWhenIdle":false,"retiredAt":null,"sessions":0,"runnerSessions":0,"queuedTasks":0,"buildVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
  "tempRetiredAbsent": {"runnerId":"server2-temp","epoch":null,"available":false,"dispatchEligible":false,"acceptingNewWork":false,"draining":true,"redirectTo":"server2","retireWhenIdle":true,"retiredAt":"2026-10-03T09:30:00Z","sessions":0,"runnerSessions":null,"queuedTasks":0,"buildVersion":"old"}
}
```

For temp retirement, main is the accepting counterpart: change main to available,
eligible and accepting true, draining/retireWhenIdle false, redirect/stamp null,
and requested SHA. The temp project census exits 0 with **zero bytes**, distinct
from a failed process with empty stdout. No test uses a live fleet status snapshot.

| Vector family | Exact change and expected boundary | V |
|---|---|---|
| Default controlled stop | mainDrained + tempAccepting; stopped main/state-init still appear in all-container census until exact rm; after own stop main may become unavailable, epoch/live count null with server counters 0 | 1, 2, 7 |
| Already stopped main | Same offline/null main without a matching operation's saved live-zero, stop and removal receipts: `RunnerCounterUnknown` or `RecycleResumeMismatch`; all volumes retained. Never arrange this state by stopping live main manually. | 7, 16 |
| Busy/unknown counters | Independently replace each of sessions/runnerSessions/queuedTasks with 1, -1, 0.5, `"0"`, false, null or omit it. 1 is `RunnerBusy`; malformed/negative values are `RunnerCounterUnknown`; zero control passes. Apply at initial and final observations in wrapper and host. | 2, 14, 15 |
| Routing | Independently change drain false, accepting true, redirect wrong/null/missing, other runner nonaccepting, or fresh routed task; initial and final reads refuse before next destructive operation | 2, 15 |
| Reference/census | Foreign running, exited, created container with target mount; bind exact/ancestor/descendant; duplicate runner, wrong labels, running state-init, ps error, inspect error/empty/malformed, attachment just before rm. Earlier selected target remains intact on whole-set validation failure. | 2 |
| Prefix collision | Keep both M/T names, `M_work-extra` and unrelated sentinels; assert exact ordered default argv `volume rm -- M_work`, `volume rm -- M_runner-tmp`, `volume rm -- M_dind-data`, never a prefix list/prune or rm force | 1 |
| Retired ABSENT | tempRetiredAbsent and successful empty project census in both processes: host runs validated temp down; four volume outcomes and unchanged retirement; explicit runnerSessions 0 also passes | 9, 13 |
| Retired PRESENT | Same row with running/exited/created runner or only exited state-init in temp census: `RunnerCounterUnknown`, no host deletion/clear; error+empty census is unknown, not absent | 9, 14 |
| Exception predicates | Starting from absent/null, omit or corrupt each required field, set each offline boolean true, change stamp/redirect/retireWhenIdle/draining; new container or changed retirement between wrapper and host refuses | 9, 14 |

Do not cross every independent fault with every other fault: one-fault vectors
establish attribution and their accepted neighbor establishes reachability. Required
combinations are null+retired+absence (and each missing fact), stopped+reference,
same SHA+partial journal, nonterminal
land+Succeeded task, and scope exclusion+bound work. These combinations cannot be
replaced by independent single-field tests.

**Git fixtures.** Use local bare origin plus a clone owned by the actual fixture
uid, deterministic empty commit A pushed to origin/master, commit B descending
from A, and a second bare remote holding B. Create a linked worktree with a space
in its name, detached worktree at B, standalone clone, bare mirror with explicit
local branch refs, and a tag-only B. Independently leave B on HEAD, an unchecked-out
branch, or the tag while origin has A. Actual origin-only rev-list returns 1 even
when the second remote holds B; after publishing B and refreshing deliberately,
it returns 0. Do not embed invented SHA results. Test dirty tracked, staged and
untracked files separately; ignored build output alone passes. Worktree enumeration
includes each repo kind; all audited tips and comparison-ref digests survive in the
receipt. Repeat the audit after stop; change a tip between audits to prove refusal.
Fault boundaries cover exit 128/dubious ownership, 124/timeout, empty/nonnumeric
stdout, stale/deleted/missing origin refs, failed ls-remote, missing objects,
shallow/partial history, broken gitdir, escaping symlink and merge/rebase/index lock.
V-3's fake checks `--user 1654:1654`, readonly `/work`, pinned image and overridden
entrypoint; RD-7 is the actual uid/ownership proof. Never grant `safe.directory=*`.

**Task/land fixtures.** Reuse scoped fixture IDs: project X
`aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1`, Y ending `aaa2`, task X1
`11111111-1111-1111-1111-111111111111`, Y1
`22222222-2222-2222-2222-222222222221`, unscoped N1
`33333333-3333-3333-3333-333333333331`. X request includes N1; excluded is
`{"total":1,"unscoped":0,"byProject":[{"projectId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2","projectName":"gym-stat","count":1}]}`.
Read Y explicitly and deduplicate N1 by ID; re-read closure until stable, refusing
changed IDs/counts, unreadable scope, duplicate conflicting rows or perpetual drift.
Items carry real DTO names `id,status,runnerId,projectId,scopeSource,landRequestedAt,landStartedAt`.
Use Queued/Dispatched/Working/Blocked/Failed target-bound rows individually, plus
unscoped and Y variants. Detail has nested `summary` and `landRequest`; do not read
`detail.status` as though it were a summary. Succeeded with either pending timestamp
or landRequest state Queued/Held/Running refuses. NeedsResolution also refuses until
an explicit resolved terminal receipt; unknown states/missing required fields refuse.
Completed/Superseded/Canceled request plus null pending timestamps and consistent
terminal evidence is the accepted control. Explicit `landRequest:null` with both
timestamps null is the supported legacy no-pending shape; omitted key/HTTP failure
is unknown. Task status never substitutes for land state. Goals/results/credential
URLs stay outside evidence. No invented runnerId/limit/page query parameters.

**Partial-resume fixtures.** One operation `c1008` + 32 lowercase hex digits;
reject separators, uppercase, empty, `..`, shell metacharacters and arbitrary paths.
Journal schema 1 binds full source, flags, project, Compose digest, original
container/image, volume driver/name/CreatedAt/labels, audit and census digests.
Freeze original time `2026-10-03T09:00:00Z`, recreated time `2026-10-03T10:00:00Z`.
Inject interruption in a fresh child at each boundary; parent retains journal/root.

| Persisted boundary / actual ledger | Next invocation's required observable result |
|---|---|
| preflight; stop failed | zero volume rm; drain retained; retry re-observes strict zero |
| stopped; owned containers still present | remove only recorded stopped IDs, then audit/census; never force-stop a new ID |
| containersRemoved; 3 original volumes | fresh audits/counters/references then remove exactly 3 |
| work removed; tmp+dind original | remove exactly tmp then dind; removed work stays absent until deliberate recreate |
| work+tmp removed; dind original | remove only dind; preserve completed subset in any later failure |
| volume rm succeeded but per-volume journal replacement failed | successful ls+inspect corroborate original's absence; recover that entry, never interpret daemon error as absence |
| any one/two removed name reappears at new generation | `RecycleResumeMismatch`, no deletion including remaining originals |
| partial seed/up created new owned work/tmp/dind and main container | verify recorded new generations and finish registration/cache checks; zero second rm, including same-SHA retry |
| recreated container ID/image/mount not recorded in operation | refuse even when requested SHA matches |
| completed healthy same SHA | verification only; zero recycle/seed/stop |
| source/flags/project/stamp/Compose/audit or preserved-volume identity differs | refuse resume before new effects; each is a separate vector |

Disk vector: before available 1024-blocks 1024, after 3072 -> bytes 1048576 and
3145728, signed delta 2097152; reversed values -> -2097152. Preserve raw rows and
filesystem identity. Empty/invalid df before refuses; after-failure reports unknown
after/delta plus the real removed subset. Journal prewrite failure stops all effects;
per-volume write/SCP failure leaves drain set and never erases the host journal.

**Default preservation.** V-1 preserves state/cache payload and seed-marker identity
and records zero cold proof/seed, cache create/rm and marker-invalidation calls;
a missing default marker fails verification without seeding. RD-1 corroborates this.
The four opt-in flag combinations, cache-plus-accepting-temp refusal, both-closed
maintenance journal, absent-main cold proof, init/probe/marker interruptions and
maintenance retry/verification vectors are **MOVED to CARD-1010** with V-11 and
RD-9/10. Ordinary Cold, saved donor, Reset and Prune remain unchanged and covered
by R-1. V-9 exercises the real shared `c849_status_zero` predicate for PC-117,
using otherwise-valid status with runnerSessions null then integer zero; assert
null refuses and zero passes before generic exit checks (`retire-host-proof`).

### Guards the regression

R-1's current source census is **36 methods / 54 results**: C849 18/18,
C912 9/9, C973 3/12 (Arguments 6+5+one), C944 1/1, C951 1/8,
C976 1/1, C946 2/2, C957 1/3. Historical 52 is short by the two C912
root-initialization/disappearance methods. Remote overall is **66/84**; adding the
11 C1008 Remote methods yields **77/95**. C905's two results and 28 other results
remain outside CP-3; V-19 explicitly protects touched lane/transport boundaries.

R-2 at this baseline is **206** single-result methods: DockerStackContractTests 112,
DindRunnerContractTests 23, DockerStackSmokeCommandTests 35,
DockerStackDocumentationTests 10, CheckpointImportTests 20, CheckpointManifestTests 6.
The policy-doc land contained at `af6d03f1` adds `Main_volume_recycling_is_scripted_only`.
Post-docs counts are **DERIVED: 207**, documentation class **11**, and must be
recounted at Code admission. This branch still has 206; no merge is authorized. Extend that method's policy
assertions in place; no extra documentation method is commissioned. Preserve its
scripted-only/no-manual-main-rm assertion after implementation. No other argument
or data-source expansion appears in these six source classes.

Intentional R-1 reconciliation: `C849_Deploy_prepares_and_verifies_before_acceptance`
currently requires strict `c849_status_zero`, warm-ready and pre-down disk checks
on retirement. Replace only its retirement portion with the narrow absent predicate,
read-only preserved-cache identity checks and post-reclaim allocation ordering.
`C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads` must stop
requiring allow-cold as an unconditional retirement prerequisite; its other callers
retain it. `C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers`
keeps **six** argument results, but its retirement argument executes the new
read-only preservation gate: missing/malformed marker alone does not block reclaim;
foreign/missing preserved cache identity still refuses. Update c973-marker-reader's
retirement branch to execute that real gate: an empty awk slice is a false positive.
All five other reader arguments retain their nine invalid-marker cases. Preserve
54 results; changing semantic assertions is not permission to drop argument rows.

Manual source expansion of T1..T24, expressed as invocations/assertions:
`1/8,2/6,1/3,1/2,1/2,0/2,3/9,1/5,2/8,2/8,2/6,19/57,2/6,2/6,3/9,4/12,2/6,2/12,5/30,4/9,3/9,2/6,1/3,1/3`.
Sum **24 groups / 66 invocations / 227 assertions**. No-jq omits T20 only:
**23 / 62 / 218**, not CARD-0983's pre-0957 19/55/197. Each jq driver has **31**
outer assertions. V-18's four modes total **93 groups / 252 invocations / 881
harness assertions + 124 driver assertions**, still one TUnit result. Freeze these
totals unchanged after C1008; targeted new vectors stay outside the legacy all
roster. The `present` execution must show available=True and no skip notice even
before CARD-0983 adds its opt-in; a lower green roster cannot qualify CP-2.

### Real-Docker count freeze

RD outcomes are separately named runs, not assertions or TUnit results. Base is
`c6d5d56b5b4c565d36157e21de9c85029cc45b53`; branch is committed Code SHA. Drive common
entry points on base, never new flags that only prove unknown-option refusal.

| RD | Frozen named outcomes (B=base, C=changed) | Count |
|---|---|---:|
| RD-1 | B default payload retained; C default three recreated with state/cache/marker retained and zero cold seed | 2 |
| RD-2 | B/C retired-null running-container refusal; B/C exited-state-init-only refusal | 4 |
| RD-3 | B absent-null refuses; C absent-null removes four with retirement retained | 2 |
| RD-4 | C four-alreadyAbsent succeeds without volume creation | 1 |
| RD-5 | C foreign running reference refuses; C foreign stopped reference refuses | 2 |
| RD-6 | C prefix-neighbor/unrelated payload identities survive | 1 |
| RD-7 | C linked-unpublished; detached-unpublished; unchecked-out-branch-unpublished; bare-unpublished; tag-only-unpublished; dirty; uid-0 ownership error; all-published clean passes | 8 |
| RD-8 | C fail after first removal; resume first; fail after second; resume second; recreated-volume intrusion; recorded partial-up resumes without rm; foreign recreated-container refuses | 7 |
| RD-9 | **MOVED to CARD-1010**: temp consumer refusal; cold seed/verify; probe failure; post-marker retry | 0 (4 moved) |
| RD-10 | **MOVED to CARD-1010**: identity transition; missing clear; attached/unexpired refusal; B-1 | 0 (3 moved) |
| RD-11 | C wrapper-absent/host-new-container refusal; census error+empty stdout refusal | 2 |
| RD-12 | B low-disk retirement refuses; C low-disk retirement reclaims; C main reclaim then low-budget allocation refusal | 3 |
| Total | 5 B + 27 C = 32 active named outcomes, one TUnit result; 7 C outcomes moved | 32 |

The unique-object ledger must verify cleanup exits and final absence of every
fixture container/volume/image while retaining evidence. No inherited c973 helper
that silently ignores cleanup exit codes qualifies. RD-12 shims only df's reported
capacity, recording real df separately. All retained RD counts/vectors are unchanged;
39 minus RD-9’s 4 and RD-10’s 3 gives 32. The moved outcomes discharge nothing here.

### Guard inventory

The inventory is for D-1..D-8's script guards, including independent wrapper and
host checks. Each G has exactly one distinct PC; the original PC-1..PC-14 table
is retained (PC-3 now changes uid only; PC-5 owns error handling). Input variants
for an active guard run inside its named single-result method. MOVED rows are
retained for traceability only and excluded from every active count. No renumbering.

| Guard | Plan reference and safety-critical invariant | Control |
|---|---|---|
| G-1 | D-1: Default state volume excluded | PC-1 |
| G-2 | D-2: Exited references count | PC-2 |
| G-3 | D-3: Audit uid 1654 | PC-3 |
| G-4 | D-3: Origin-only ancestry | PC-4 |
| G-5 | D-3: Failed Git inspection cannot mean zero | PC-5 |
| G-6 | D-4: Wrapper present-container null refusal | PC-6 |
| G-7 | D-4: Wrapper absent-retired null acceptance | PC-7 |
| G-8 | D-4: Host fresh absence proof | PC-8 |
| G-9 | D-2: Succeeded task can have pending land | PC-9 |
| G-10 | D-5: Retirement may reclaim under low disk | PC-10 |
| G-11 | D-5: Resume generation binding | PC-11 |
| G-12 | D-7: Preview exits before mutation | PC-12 |
| G-13 | **MOVED to CARD-1010**; D-6: Cache opt-in cannot use accepting temp | PC-13 |
| G-14 | D-5: Receipt uses real after-df | PC-14 |
| G-15 | D-1: Default shared caches excluded | PC-15 |
| G-16 | D-1: Default target names exact | PC-16 |
| G-17 | D-1: Temp includes all four private volumes | PC-17 |
| G-18 | D-1: Private volumes non-external and project-private | PC-18 |
| G-19 | D-1: Cache volumes external under fixed names | PC-19 |
| G-20 | D-1: No daemon prune | PC-20 |
| G-21 | D-1: Broker survives | PC-21 |
| G-22 | D-1: Post-down selected volumes absent | PC-22 |
| G-23 | D-1: Preserved volume identities unchanged | PC-23 |
| G-24 | D-1/D-7: Wrapper dryRun is a typed boolean | PC-24 |
| G-25 | D-1/D-7: Bridge dryRun is a typed boolean | PC-25 |
| G-26 | D-1: Unknown recycle fields refused | PC-26 |
| G-27 | D-1: Arbitrary volume lists refused | PC-27 |
| G-28 | D-1: Only supported project admitted | PC-28 |
| G-29 | **MOVED to CARD-1010**; D-1: Opt-ins only explicit redeploy-old | PC-29 |
| G-30 | D-1: Legacy deploy-parent remains nondestructive | PC-30 |
| G-31 | D-5: Operation ID is not a path/shell fragment | PC-31 |
| G-32 | D-2: Wrapper counters have present integer types | PC-32 |
| G-33 | D-2: Wrapper nonzero counter refuses | PC-33 |
| G-34 | D-2: Host counters have present integer types | PC-34 |
| G-35 | D-2: Host nonzero counter refuses | PC-35 |
| G-36 | D-2: Main admission closed | PC-36 |
| G-37 | D-2: Main drain established | PC-37 |
| G-38 | D-2: Main redirect expected | PC-38 |
| G-39 | D-2: Counterpart accepting on normal replacement | PC-39 |
| G-40 | D-2: Final status/routing snapshot fresh | PC-40 |
| G-41 | D-2: Main null requires own saved live-zero proof | PC-41 |
| G-42 | D-2: Main null requires own stop/removal receipt | PC-42 |
| G-43 | D-2: Target-bound active/retained tasks block | PC-43 |
| G-44 | D-2: Queued/dispatched/working routed work blocks | PC-44 |
| G-45 | D-2: Excluded project scopes closed | PC-45 |
| G-46 | D-2: Unscoped tasks included | PC-46 |
| G-47 | D-2: Census errors cannot become empty | PC-47 |
| G-48 | D-2: Census stable IDs/counts and duplicate consistency | PC-48 |
| G-49 | D-2: Pending land timestamps checked | PC-49 |
| G-50 | D-2: Land detail state checked | PC-50 |
| G-51 | D-2: Unknown/legacy land evidence fails closed | PC-51 |
| G-52 | D-2: Fresh land/task recheck before deletion | PC-52 |
| G-53 | D-2: Stop is graceful and never forced | PC-53 |
| G-54 | D-2: Stop failure retains all volumes | PC-54 |
| G-55 | D-2: Stopped state inspected before rm | PC-55 |
| G-56 | D-2: Remove only inspected exact owned IDs | PC-56 |
| G-57 | D-2: Running/ambiguous state-init refused | PC-57 |
| G-58 | D-2: Duplicate runner identity refused | PC-58 |
| G-59 | D-2: Failed ps is unknown | PC-59 |
| G-60 | D-2: Failed/malformed inspect is unknown | PC-60 |
| G-61 | D-2: Canonical bind overlap refused | PC-61 |
| G-62 | D-2: Validate entire target set before first delete | PC-62 |
| G-63 | D-2: Recheck each target at removal | PC-63 |
| G-64 | D-2: Docker in-use refusal is final | PC-64 |
| G-65 | D-2: Volume removal failure is not success | PC-65 |
| G-66 | D-2: Inspected volume identity required | PC-66 |
| G-67 | D-3: Enumerate all repositories/worktrees | PC-67 |
| G-68 | D-3: Enumerate unchecked-out local refs | PC-68 |
| G-69 | D-3: Current origin advertisement agrees | PC-69 |
| G-70 | D-3: Dirty work refuses | PC-70 |
| G-71 | D-3: Count must be nonnegative integer | PC-71 |
| G-72 | D-3: History completeness proved | PC-72 |
| G-73 | D-3: Git traversal stays within work volume | PC-73 |
| G-74 | D-3: In-progress Git operations refused | PC-74 |
| G-75 | D-3: Audit is readonly and disables lazy writes | PC-75 |
| G-76 | D-3: Quiescent second audit required | PC-76 |
| G-77 | D-2: Audit helper removed before final census | PC-77 |
| G-78 | D-2: Audit mount source must exist | PC-78 |
| G-79 | D-2: Helper uses inspected pinned image | PC-79 |
| G-80 | D-2: Helper cannot start runner | PC-80 |
| G-81 | D-2: Audit work mount readonly at original path | PC-81 |
| G-82 | D-5: Journal writable before effects | PC-82 |
| G-83 | D-5: Per-volume completion survives later failure | PC-83 |
| G-84 | D-5: Atomic journal replacement | PC-84 |
| G-85 | D-5: Journal outside recycled volumes | PC-85 |
| G-86 | D-5: Resume source binding | PC-86 |
| G-87 | D-5: Resume flags binding | PC-87 |
| G-88 | D-5: Resume project binding | PC-88 |
| G-89 | D-5: Resume retirement/store binding | PC-89 |
| G-90 | D-5: Resume Compose binding | PC-90 |
| G-91 | D-5: Recreated containers must belong to operation | PC-91 |
| G-92 | D-5: Owned new generations not recycled twice | PC-92 |
| G-93 | D-5: Incomplete same-SHA journal not hidden | PC-93 |
| G-94 | D-5: Healthy same-SHA retry does not recycle | PC-94 |
| G-95 | D-5: Before-df parse failure refuses | PC-95 |
| G-96 | D-5: Unknown after-df reported honestly | PC-96 |
| G-97 | D-5: Signed byte delta on same filesystem | PC-97 |
| G-98 | D-5: Exactly one summary per attempt | PC-98 |
| G-99 | D-5: Wrapper receipt-copy failure blocks completion | PC-99 |
| G-100 | D-2: Recreate failure keeps drain | PC-100 |
| G-101 | D-2: Requested SHA observed before clear | PC-101 |
| G-102 | D-2: Mount identities verified before clear | PC-102 |
| G-103 | D-2: Cache verification before clear | PC-103 |
| G-104 | D-4: Wrapper valid retirement stamp | PC-104 |
| G-105 | D-4: Wrapper draining requirement | PC-105 |
| G-106 | D-4: Wrapper retireWhenIdle requirement | PC-106 |
| G-107 | D-4: Wrapper redirect requirement | PC-107 |
| G-108 | D-4: Wrapper unavailable requirement for null | PC-108 |
| G-109 | D-4: Wrapper ineligible requirement for null | PC-109 |
| G-110 | D-4: Wrapper nonaccepting requirement for null | PC-110 |
| G-111 | D-4: Wrapper requires live-counter key even when null | PC-111 |
| G-112 | D-4: Host retirement timestamp freshness | PC-112 |
| G-113 | D-4: Host rechecks all retirement fields | PC-113 |
| G-114 | D-4: Host null exception remains absence-only | PC-114 |
| G-115 | D-4: Main accepting before temp retirement | PC-115 |
| G-116 | D-4: Retirement retained after temp removal | PC-116 |
| G-117 | D-4: Shared donor/reset/prune null guard unchanged | PC-117 |
| G-118 | **MOVED to CARD-1010**; D-6: State reset needs stamped retirement | PC-118 |
| G-119 | **MOVED to CARD-1010**; D-6: State clear follows owned removal | PC-119 |
| G-120 | **MOVED to CARD-1010**; D-6: State replacement waits detached connection | PC-120 |
| G-121 | **MOVED to CARD-1010 — B-1: production lease predicate unavailable**; D-6: State replacement waits registration lease | PC-121 |
| G-122 | **MOVED to CARD-1010 — B-1: production lease predicate unavailable**; D-6: State replacement waits post-disconnect lease | PC-122 |
| G-123 | **MOVED to CARD-1010**; D-6: Registration observes different new store | PC-123 |
| G-124 | **MOVED to CARD-1010**; D-6: StoreMismatch is not bypassed | PC-124 |
| G-125 | **MOVED to CARD-1010**; D-6: Both cache consumers closed and zero | PC-125 |
| G-126 | **MOVED to CARD-1010**; D-6: Cache bind/foreign consumers excluded | PC-126 |
| G-127 | **MOVED to CARD-1010**; D-6: Maintenance context journal required | PC-127 |
| G-128 | **MOVED to CARD-1010**; D-6: Maintenance helper image retained and inspected | PC-128 |
| G-129 | **MOVED to CARD-1010**; D-6: Default must not cold seed | PC-129 |
| G-130 | **MOVED to CARD-1010**; D-6: Old marker invalidated only for completed cache removal | PC-130 |
| G-131 | **MOVED to CARD-1010**; D-6: Cold seed creates exact labelled volumes | PC-131 |
| G-132 | **MOVED to CARD-1010**; D-6: Cold roots have correct driver/options | PC-132 |
| G-133 | **MOVED to CARD-1010**; D-6: Cold root canonical and non-symlink | PC-133 |
| G-134 | **MOVED to CARD-1010**; D-6: Cold ownership 1654 and mode 0700 | PC-134 |
| G-135 | **MOVED to CARD-1010**; D-6: Cold roots empty including hidden entries | PC-135 |
| G-136 | **MOVED to CARD-1010**; D-6: Cold uid-write probe must succeed | PC-136 |
| G-137 | **MOVED to CARD-1010**; D-6: Cold probe helpers/canaries cleaned | PC-137 |
| G-138 | **MOVED to CARD-1010**; D-6: Cold rechecks around create/init/probe/marker | PC-138 |
| G-139 | **MOVED to CARD-1010**; D-6: Cold seed cannot autocreate missing source | PC-139 |
| G-140 | **MOVED to CARD-1010**; D-6: Cold marker validated atomically | PC-140 |
| G-141 | **MOVED to CARD-1010**; D-6: Cold verification really observes recreated roots | PC-141 |
| G-142 | **MOVED to CARD-1010**; D-6: Full-only contexts stay full-only | PC-142 |
| G-143 | **MOVED to CARD-1010**; D-6: Ordinary Cold still requires running main | PC-143 |
| G-144 | **MOVED to CARD-1010**; D-6: Seed host-case success cannot terminate deploy early | PC-144 |
| G-145 | D-7: Preview never provisions secrets/checkouts | PC-145 |
| G-146 | D-7: Preview is not apply authority | PC-146 |
| G-147 | D-7: Absent-helper audit pending in preview | PC-147 |
| G-148 | D-8: Tmp mount retains copy-up | PC-148 |
| G-149 | D-8: Tmp assets and mode verified | PC-149 |
| G-150 | D-5: Receipts omit sensitive content | PC-150 |
| G-151 | D-5: Structured refusal survives EXIT trap | PC-151 |
| G-152 | D-2: Host lane cannot target sibling daemon | PC-152 |
| G-153 | D-2: Rollout lock held before cache lock | PC-153 |
| G-154 | D-2: All admission-changing rollout phases respect lock | PC-154 |
| G-155 | **MOVED to CARD-1010**; D-2: Lock is not reacquired recursively | PC-155 |
| G-156 | **MOVED to CARD-1010**; D-1: Real bridge routes new host case | PC-156 |

### Positive controls

PC-1..PC-14 use the earlier exact methods/labels; PC-15 onward extend that
inventory. Each mutation below is a syntactically valid replacement/removal of
the corresponding production guard, not a test-fixture edit. C# still compiles;
PowerShell/bash must parse. The named method runs the real production decision.
All substantive effects/retained-sentinel assertions carry its listed label and
precede generic exit/count checks. Unrelated setup errors, hangs and zero results
do not count as red. For a lock control, assert an observed forbidden entry using
a held-lock barrier and bounded child termination; do not count a timeout as red.
Code must record production statement coordinates when implementing these named
guards; if the guard cannot be reached by its frozen method, return to TestDesign
before Review rather than silently substituting a source-text assertion.

Each row selects exactly `/*/*/ClassName/ExactTestMethod` using its fully spelled
class/method below, Min=1. Mutation runs break/red/restore/green after land under
SourceLanding custody; Code runs V/R, Review judges this design before land.
All children remain inherited/local; no snapshot access via a Docker executor.
The PC harness is offline. RD is ordinary Code evidence and is not rerun as a
Mutation executor. SourceLanding never commits or pushes its temporary mutations.

| PC | Compiling production defect (break the matching G) | Exact detecting method | First assertion label | Frozen input / expected first assertion |
|---|---|---|---|---|
| PC-15 | Append P to the default removal array. | `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` | `recycle-exact-defaults` | Default main with P/S/N sentinels: cache payload/marker identities unchanged. |
| PC-16 | Replace the exact work name with a volume-ls prefix match. | `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` | `recycle-exact-defaults` | Default main plus work-extra/temp-work sentinels: only exact default ordered names removed. |
| PC-17 | Replace compose_temp down -v with compose_temp down. | `RollingVolumeRecycleScriptTests.C1008_Retired_absent_null_is_accepted` | `retire-absent-null-accepted` | Absent retired temp with four private volumes: all four are absent after down. |
| PC-18 | Replace the rendered private-volume model validation with :. | `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` | `recycle-exact-defaults` | Each private model entry external/wrong-project in turn: all targets retained. |
| PC-19 | Replace the rendered external-cache model validation with :. | `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` | `recycle-exact-defaults` | Each cache model entry nonexternal/wrong-fixed-name in turn: all targets retained. |
| PC-20 | Insert docker volume prune -f after selected removals. | `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` | `recycle-exact-defaults` | Default success with unused unrelated volumes: no prune and unrelated payload survives. |
| PC-21 | Add the broker ID to the owned-container removal list. | `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` | `recycle-exact-defaults` | Running broker with no selected mount: broker remains running and same ID. |
| PC-22 | Remove post-down inspection and report all targets removed. | `RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` | `retire-host-proof` | Fake down succeeds but retains one original temp volume: receipt cannot claim removed. |
| PC-23 | Replace preserved-volume after-inspection comparison with :. | `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` | `recycle-exact-defaults` | Preserved state/cache generation changes after down: identity-mismatch receipt before completion. |
| PC-24 | Replace wrapper dryRun boolean type rejection with boolean coercion. | `RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` | `recycle-manifest-strict` | Invoke real wrapper context validator with dryRun="false", null, 0 or omitted, then false/true controls: reject malformed before transport. |
| PC-25 | Replace bridge dryRun boolean type rejection with boolean coercion. | `RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` | `recycle-manifest-strict` | Invoke real bridge context validator with the same malformed dryRun inputs and boolean controls: reject before host entry. |
| PC-26 | Remove the recycle-object unknown-property rejection. | `RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` | `recycle-manifest-strict` | Add unknown recycle property, including removed state/cache option keys: no SSH mutation. |
| PC-27 | Accept and use manifest recycle.volumes as the removal array. | `RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` | `recycle-manifest-strict` | Inject recycle.volumes containing main state/unrelated name: reject; sentinels retained. |
| PC-28 | Replace project allowlist check with true. | `RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` | `recycle-manifest-strict` | Supply unsupported project in otherwise-valid context: no host mutation. |
| PC-29 | **MOVED to CARD-1010**; Remove the flag/Phase rejection branch. | `RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` | `recycle-manifest-strict` | MOVED to CARD-1010; no active input obligation here. |
| PC-30 | Make absent recycle context take the default recycle apply branch. | `RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` | `recycle-manifest-strict` | Direct deploy-parent without recycle object: default payloads retained. |
| PC-31 | Remove ResumeRecycle grammar rejection before path/SSH construction. | `RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` | `recycle-manifest-strict` | Resume ID contains slash, .., uppercase or shell metacharacters: reject before path/SSH construction. |
| PC-32 | Coerce null or string counter to int before validation. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Each counter null/omitted/string/bool/fractional/negative at wrapper: no host mutation. |
| PC-33 | Replace the nonzero-counter refusal with return. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Each wrapper counter 1, separately: no host mutation; zero neighbor passes. |
| PC-34 | Remove the jq has/type/integer check before zero comparison. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Each host counter null/omitted/string/bool/fractional/negative: first assert RunnerCounterUnknown (not RunnerBusy), then retained volumes. |
| PC-35 | Replace host zero-counter predicate with true. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Each host counter 1, separately: volumes retained; zero neighbor passes. |
| PC-36 | Remove acceptingNewWork=false check from recycle preflight. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Main accepting=true with remaining drain facts valid: no stop. |
| PC-37 | Remove draining=true check from recycle preflight. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Main draining=false with other proof valid: no stop. |
| PC-38 | Remove redirectTo comparison from recycle preflight. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Main redirect wrong/null/omitted: no stop. |
| PC-39 | Remove temp accepting check on default replacement. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Default main drained but counterpart accepting=false: no stop; true control passes. |
| PC-40 | Reuse preflight status instead of the immediate pre-removal GET. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Initially valid status changes to busy/routed at final GET: no next removal. |
| PC-41 | Accept offline/null main when journal has no strict-zero observation. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `recycle-resume-generation` | Offline/null main journal lacks saved strict live-zero: all originals retained. |
| PC-42 | Accept saved zeros without matching stop/removal container receipt. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `recycle-resume-generation` | Saved live-zero exists but exact stop/removal receipt is absent/wrong ID: retain originals. |
| PC-43 | Drop target-bound Blocked and Failed rows from the obligation set. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Target-bound Blocked and Failed owners independently: all work retained. |
| PC-44 | Ignore routed task rows when status counters report zero. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Zero counters but queued/dispatched/working routed task independently: no host mutation. |
| PC-45 | Treat excluded.byProject as informational and return after first response. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | X excludes Y, with target-bound Y1: read Y and refuse removal. |
| PC-46 | Change unscoped=include to unscoped=exclude. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Unscoped N1 bound to target: request includes unscoped and refuses removal. |
| PC-47 | Replace failed task-list request result with an empty items array. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | List API fails or returns malformed envelope: no host mutation, not empty success. |
| PC-48 | Remove repeated-closure/dedup consistency comparison. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Changed exclusion IDs/counts or conflicting duplicate rows on closure: no mutation. |
| PC-49 | Ignore landRequestedAt and landStartedAt when detail landRequest is null. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Succeeded with either pending timestamp and explicit null landRequest: no mutation. |
| PC-50 | Ignore Queued/Held/Running detail when pending timestamps are null. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Null timestamps but detail landRequest Queued/Held/Running: no mutation. |
| PC-51 | Treat omitted landRequest or unknown state as no pending land. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Omitted detail landRequest or unknown state: refuse; explicit-null legacy control passes. |
| PC-52 | Remove the final task/land census and reuse its preflight digest. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Task/land appears after initial audit: final census blocks first deletion. |
| PC-53 | Replace docker stop with docker rm -f for main. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Live identified main in normal controlled-stop vector: exact graceful stop trace, never rm -f. |
| PC-54 | Append \|\| true to the owned-runner stop command. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Owned runner stop returns failure: all three target payloads retained. |
| PC-55 | Remove the post-stop State.Running=false check. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Stop reports success but inspect still Running=true: no container/volume rm. |
| PC-56 | Use an unvalidated project-prefix container list for rm. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Owned main/state-init plus similarly prefixed foreign ID: foreign container survives. |
| PC-57 | Admit state-init regardless of State.Running/Status. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | State-init running or ambiguous Status: no stop/removal; exited neighbor passes. |
| PC-58 | Select the first of multiple runner IDs instead of refusing. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Two labelled runner IDs: no stop, neither volume set touched. |
| PC-59 | Append \|\| true to the final all-container census command substitution. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Final ps exits nonzero with zero-byte stdout: no first deletion. |
| PC-60 | Replace failed container inspection with an empty Mounts array. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Container inspect fails/empty/malformed: retain targets, report unknown census. |
| PC-61 | Remove the bind overlap comparison while retaining named-volume checks. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Foreign bind exact/ancestor/descendant of volume path: retain targets. |
| PC-62 | Move each target validation into its own rm loop only. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Last target has foreign reference: first two targets also retained. |
| PC-63 | Remove the per-target final reference check. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Attach foreign container between whole-set validation and per-target check: no rm invocation for newly referenced target. |
| PC-64 | Retry a failed volume rm using --force. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Docker reports in-use on exact rm: retained payload, no force retry. |
| PC-65 | Append \|\| true to volume rm and journal removed. | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` | `recycle-receipt-facts` | First rm succeeds, second exits error: second retained and receipt lists partial first only. |
| PC-66 | Accept mismatched name/driver/labels from volume inspect. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Volume inspect wrong Name/Driver/Labels: no target removed. |
| PC-67 | Restrict audit roots to the primary checkout only. | `RemoteScriptContractTests.C1008_Recycle_audits_work_as_1654` | `recycle-audit-uid` | Linked/detached/standalone/bare repositories including spaced path: all tips appear in audit. |
| PC-68 | Remove local branch/tag tip enumeration and audit HEAD only. | `RemoteScriptContractTests.C1008_Recycle_refuses_unpublished_and_dirty_work` | `recycle-work-preserved` | B on unchecked-out branch or tag only: refuse and retain work. |
| PC-69 | Skip current origin-advertisement comparison. | `RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git` | `recycle-git-unknown-refuses` | Local origin refs stale/deleted/missing versus current advertisement: retain work. |
| PC-70 | Ignore nonempty status --porcelain output. | `RemoteScriptContractTests.C1008_Recycle_refuses_unpublished_and_dirty_work` | `recycle-work-preserved` | Tracked dirty, staged and untracked variants: retain work; ignored-only control passes. |
| PC-71 | Replace nonnumeric/empty count validation with default zero. | `RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git` | `recycle-git-unknown-refuses` | rev-list stdout empty/nonnumeric/negative: retain work with audit-unknown result. |
| PC-72 | Bypass shallow/partial/missing-object refusal. | `RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git` | `recycle-git-unknown-refuses` | Shallow/partial history or missing object: retain work; complete published neighbor passes. |
| PC-73 | Skip canonical root/gitdir confinement checks. | `RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git` | `recycle-git-unknown-refuses` | Escaping symlink or broken/out-of-root gitdir: no out-of-volume audit, work retained. |
| PC-74 | Ignore merge/rebase/index-lock markers. | `RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git` | `recycle-git-unknown-refuses` | Merge/rebase/index lock present: work retained. |
| PC-75 | Remove GIT_OPTIONAL_LOCKS=0 and no-lazy-fetch setting from audit invocation. | `RemoteScriptContractTests.C1008_Recycle_audits_work_as_1654` | `recycle-audit-uid` | Real audit invocation: trace has GIT_OPTIONAL_LOCKS=0 and disabled lazy fetch before Git runs. |
| PC-76 | Remove the post-stop tip/ref comparison and accept pre-stop audit. | `RemoteScriptContractTests.C1008_Recycle_refuses_unpublished_and_dirty_work` | `recycle-work-preserved` | Change local tip/ref between pre-stop and quiescent audits: work retained. |
| PC-77 | Leave the stopped audit helper after audit and proceed. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Audit helper rm fails/leaves exited reference: no volume rm attempted. |
| PC-78 | Skip explicit source-volume inspection before helper run. | `RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git` | `recycle-git-unknown-refuses` | Source work absent before helper: no helper auto-creates it; audit refuses. |
| PC-79 | Replace pinned image digest with mutable requested tag. | `RemoteScriptContractTests.C1008_Recycle_audits_work_as_1654` | `recycle-audit-uid` | Mutable tag resolves differently from inspected digest: audit argv still uses pinned digest. |
| PC-80 | Remove audit --entrypoint override. | `RemoteScriptContractTests.C1008_Recycle_audits_work_as_1654` | `recycle-audit-uid` | Normal audit helper: argv overrides entrypoint and cannot launch runner. |
| PC-81 | Remove readonly from /work audit mount. | `RemoteScriptContractTests.C1008_Recycle_audits_work_as_1654` | `recycle-audit-uid` | Normal audit helper: argv mounts /work at original path read-only. |
| PC-82 | Ignore failure of the preflight journal write. | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` | `recycle-receipt-facts` | Preflight journal write fails: mutation trace empty. |
| PC-83 | Reset removed outcomes to empty on a subsequent rm failure. | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` | `recycle-receipt-facts` | Second rm fails after work removal journal persisted: receipt retains completed work entry. |
| PC-84 | Write journal destination directly instead of temp plus rename. | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` | `recycle-receipt-facts` | Fault before atomic rename: prior valid journal remains parseable and unchanged. |
| PC-85 | Put host journal under the selected work volume. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `recycle-resume-generation` | Selected work volume is removed: journal remains readable outside all selected roots. |
| PC-86 | Skip journal sourceSha equality. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `recycle-resume-generation` | Resume changes full source SHA: no new effect. |
| PC-87 | Skip journal recycle-options equality. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `recycle-resume-generation` | Resume journal dryRun/default-context options differ: no new effect; matching apply options pass. |
| PC-88 | Skip journal project equality. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `recycle-resume-generation` | Resume changes project: no new effect. |
| PC-89 | Skip journal retiredAt/oldStore equality. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `recycle-resume-generation` | Resume changes saved temp retirement stamp or preserved main store: no new effect. |
| PC-90 | Skip journal Compose digest equality. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `recycle-resume-generation` | Resume rendered Compose digest changes: no new effect. |
| PC-91 | Accept an unrecorded recreated container ID with matching SHA. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `recycle-resume-generation` | Partial-up container ID/image/mount unrecorded despite matching SHA: no deletion/clear. |
| PC-92 | Restart the deletion loop after partial up instead of verification. | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `recycle-resume-generation` | Journal records new generations after partial up: complete verification with zero second rm. |
| PC-93 | Return same-SHA verification branch before inspecting incomplete journal. | `RollingVolumeRecycleScriptTests.C1008_Same_sha_and_partial_retries_are_safe` | `recycle-wrapper-resume` | Same-SHA offline/partial journal: resume required before success; no blind clear. |
| PC-94 | Always invoke recycle for healthy same-SHA runner. | `RollingVolumeRecycleScriptTests.C1008_Same_sha_and_partial_retries_are_safe` | `recycle-wrapper-resume` | Healthy completed same SHA: verification only, zero stop/recycle/seed. |
| PC-95 | Replace invalid before-df with zero and continue. | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` | `recycle-receipt-facts` | Before-df empty/invalid: all payloads retained. |
| PC-96 | Copy before-df into after-df on after-probe error. | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` | `recycle-receipt-facts` | After-df fails following work deletion: after/delta unknown, removed subset retained. |
| PC-97 | Calculate absolute block difference without 1024 conversion. | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` | `recycle-receipt-facts` | 1024->3072 then reversed blocks: byte deltas +2097152 and -2097152. |
| PC-98 | Print completed summary in both rm helper and wrapper continuation. | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` | `recycle-receipt-facts` | One successful operation and one partial operation: exactly one C1008_RECYCLE line per attempt. |
| PC-99 | Ignore failed SCP of host journal and report completed. | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` | `recycle-receipt-facts` | SCP fails after deletion: drain remains, host journal retained, retry copies without rm. |
| PC-100 | Clear main drain in failure cleanup after seed/up failure. | `RollingVolumeRecycleScriptTests.C1008_Same_sha_and_partial_retries_are_safe` | `recycle-wrapper-resume` | Recreate/seed/up failure after reclaim: drain remains set. |
| PC-101 | Remove requested buildVersion equality from verification wait. | `RollingVolumeRecycleScriptTests.C1008_Same_sha_and_partial_retries_are_safe` | `recycle-wrapper-resume` | New container healthy but buildVersion wrong: no drain clear. |
| PC-102 | Treat failed mount verification as success. | `RollingVolumeRecycleScriptTests.C1008_Same_sha_and_partial_retries_are_safe` | `recycle-wrapper-resume` | Requested SHA observed but mount verification fails: no drain clear. |
| PC-103 | Move drain clear before verify-runner-caches. | `RollingVolumeRecycleScriptTests.C1008_Same_sha_and_partial_retries_are_safe` | `recycle-wrapper-resume` | Requested SHA observed but cache verification fails: no drain clear. |
| PC-104 | Accept nonempty non-date retiredAt. | `RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal` | `retire-null-stays-closed` | Absent/null temp with non-date retiredAt: no host deletion. |
| PC-105 | Drop draining=true from absent-temp predicate. | `RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal` | `retire-null-stays-closed` | Absent/null temp with draining=false: no host deletion. |
| PC-106 | Drop retireWhenIdle=true from absent-temp predicate. | `RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal` | `retire-null-stays-closed` | Absent/null temp with retireWhenIdle=false: no host deletion. |
| PC-107 | Drop redirectTo=server2 from absent-temp predicate. | `RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal` | `retire-null-stays-closed` | Absent/null temp wrong/null/missing redirect: no host deletion. |
| PC-108 | Drop available=false from absent-temp predicate. | `RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal` | `retire-null-stays-closed` | Absent/null temp available=true: no host deletion. |
| PC-109 | Drop dispatchEligible=false from absent-temp predicate. | `RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal` | `retire-null-stays-closed` | Absent/null temp dispatchEligible=true: no host deletion. |
| PC-110 | Drop acceptingNewWork=false from absent-temp predicate. | `RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal` | `retire-null-stays-closed` | Absent/null temp acceptingNewWork=true: no host deletion. |
| PC-111 | Treat missing runnerSessions as explicit null. | `RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal` | `retire-null-stays-closed` | Absent retired temp with runnerSessions key omitted: no host deletion; explicit null control passes. |
| PC-112 | Remove host comparison to wrapper retiredAt. | `RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` | `retire-host-proof` | Host retirement stamp differs from wrapper observation: temp volumes retained. |
| PC-113 | Use wrapper supplied retired predicate instead of fresh host status predicate. | `RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` | `retire-host-proof` | Each fresh host retirement predicate independently changes: temp volumes retained. |
| PC-114 | Treat exited temp container as absent in null predicate. | `RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` | `retire-host-proof` | Only exited temp/state-init in host census with null inventory: temp volumes retained. |
| PC-115 | Remove host counterpart accepting check. | `RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` | `retire-host-proof` | Main stops accepting before temp cleanup: temp volumes retained. |
| PC-116 | Add temp drain/clear on successful retirement. | `RollingVolumeRecycleScriptTests.C1008_Retired_absent_null_is_accepted` | `retire-absent-null-accepted` | Successful retired-absent cleanup: no drain/clear POST; original retirement persists. |
| PC-117 | Allow null runnerSessions in c849_status_zero. | `RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` | `retire-host-proof` | Real c849_status_zero with valid donor/reset/prune JSON: null runnerSessions refuses, integer zero passes. |
| PC-118 | **MOVED to CARD-1010**; Allow state reset from ordinary drain without retirement stamp. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-119 | **MOVED to CARD-1010**; Move retirement clear before owned container removal. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-120 | **MOVED to CARD-1010**; Treat unavailable status alone as detached. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-121 | **MOVED to CARD-1010 — B-1: production lease predicate unavailable**; Replace lease-expiry proof with fixed sleep using default 90. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-122 | **MOVED to CARD-1010 — B-1: production lease predicate unavailable**; Ignore recent-disconnect lease window. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-123 | **MOVED to CARD-1010**; Accept old runnerStoreId as successful state reset. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-124 | **MOVED to CARD-1010**; Treat StoreMismatch registration result as success. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-125 | **MOVED to CARD-1010**; Use only main status for maintenance admission. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-126 | **MOVED to CARD-1010**; Exclude foreign containers from maintenance cache reference scan. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-127 | **MOVED to CARD-1010**; Allow absent-main cold proof with flags only and no completed journal. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-128 | **MOVED to CARD-1010**; Accept an uninspectable helper image digest before cache rm. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-129 | **MOVED to CARD-1010**; Invoke runner-cache-recycle-seed unconditionally after default recycle. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-130 | **MOVED to CARD-1010**; Delete seed marker before cache reference/removal proof. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-131 | **MOVED to CARD-1010**; Remove io.antiphon.cache-role label from cache volume create. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-132 | **MOVED to CARD-1010**; Skip driver/options check in c849_cold_volume_facts. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-133 | **MOVED to CARD-1010**; Skip canonical-root/symlink validation. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-134 | **MOVED to CARD-1010**; Skip owner/mode validation after initialization. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-135 | **MOVED to CARD-1010**; Replace find emptiness probe with non-dot glob. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-136 | **MOVED to CARD-1010**; Ignore nonzero c849_cold_probe result. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-137 | **MOVED to CARD-1010**; Ignore helper cleanup failure and publish marker. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-138 | **MOVED to CARD-1010**; Remove the final P6 c849_cold_proof call. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-139 | **MOVED to CARD-1010**; Remove C849_COLD_PRESENT check before init helper. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-140 | **MOVED to CARD-1010**; Write ready marker before final proof using direct destination write. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-141 | **MOVED to CARD-1010**; Return success in case_verify_runner_caches before mounts/writability. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-142 | **MOVED to CARD-1010**; Make c849_require_ready accept cold in full-required context. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-143 | **MOVED to CARD-1010**; Apply maintenance absent-main exception when no recycle context is supplied. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-144 | **MOVED to CARD-1010**; Call exit-producing c849_cold_seed inline in deploy-parent. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-145 | Move dry-run dispatch below ensure_checkout/boot-file preparation. | `RemoteScriptContractTests.C1008_Recycle_dry_run_never_mutates` | `recycle-preview-readonly` | Both DryRun phases: no secret provisioning or checkout setup trace. |
| PC-146 | Reuse preview status/journal without fresh apply preflight. | `RemoteScriptContractTests.C1008_Recycle_dry_run_never_mutates` | `recycle-preview-readonly` | Apply after preview with newly busy status: fresh preflight refuses, no deletion. |
| PC-147 | Start audit Docker helper during offline preview. | `RemoteScriptContractTests.C1008_Recycle_dry_run_never_mutates` | `recycle-preview-readonly` | Absent main/temp audit needs helper during DryRun: auditPending=true and no docker run. |
| PC-148 | Add volume-nocopy to runner-tmp mount rendering. | `RemoteScriptContractTests.C1008_Recycle_preserves_tmp_copyup` | `recycle-tmp-assets` | Rendered runner-tmp mount in V-6: no volume-nocopy; recreated host assets present. |
| PC-149 | Skip recreated /tmp asset/mode validation. | `RemoteScriptContractTests.C1008_Recycle_preserves_tmp_copyup` | `recycle-tmp-assets` | Recreated /tmp has wrong mode or missing pty-host assets: verification fails before clear. |
| PC-150 | Write raw Git stderr or HTTP exception body to public receipt. | `RollingVolumeRecycleScriptTests.C1008_Refusal_receipts_do_not_leak_secrets` | `recycle-receipt-custody` | Secret-bearing stderr/HTTP/filename sentinel on refusal: absent from public logs/JSON. |
| PC-151 | Remove WROTE protection so EXIT overwrites diagnosis with UnhandledExit. | `RollingVolumeRecycleScriptTests.C1008_Refusal_receipts_do_not_leak_secrets` | `recycle-receipt-custody` | Typed refusal reaches EXIT trap: original typed diagnosis survives, never UnhandledExit. |
| PC-152 | Replace host lane refusal at recycle entry with :. | `RollingVolumeRecycleScriptTests.C1008_Documentation_and_transport_pins_match` | `recycle-doc-contract` | Invoke recycle host case in sibling/nested lane: lane refusal before Docker mutation. |
| PC-153 | Remove rollout-lock acquisition while retaining cache lock. | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `recycle-reference-refusal` | Another child holds rollout lock, cache lock free: barrier observes no forbidden recycle entry; terminate owned child after assertion. |
| PC-154 | Remove rollout-lock acquisition on drain/clear path. | `RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse` | `recycle-work-gates` | Another child holds rollout lock while drain/clear requested: no POST entry before release; bounded owned-child cleanup. |
| PC-155 | **MOVED to CARD-1010**; Unconditionally reacquire cache lock inside maintenance seed while parent holds it. | `RemoteScriptContractTests.C1008_Recycle_optins_require_maintenance_proofs` | `recycle-optin-proof` | MOVED to CARD-1010; no active input obligation here. |
| PC-156 | **MOVED to CARD-1010**; Remove runner-cache-recycle-seed from live-case dispatch roster. | `RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` | `recycle-manifest-strict` | MOVED to CARD-1010; no active input obligation here. |

Inventory audit for the **remaining** rows: **guards=125, mapped=125, missing=0,
duplicate PC maps=0**. **31 guards and 31 PCs MOVED to CARD-1010** (13, 29,
118–144, 155, 156); all original IDs 1..156 remain represented once per inventory.
PC-121/122 move specifically for B-1. V-11 moves; PC-117 remains executable in V-9.

**All 125 remaining PCs are executable designs:** each has its concrete production
mutation, exact method and first assertion above, and a frozen input/expected-effect
binding in its row. No remaining PC depends on the missing lease predicate. This means
executable test design, not a claim that S1 methods exist or that Mutation ran.
Code must preserve these first substantive assertions, then Mutation proves actual
reachability at the eventual statement coordinates. The all-PCs-executable handoff
condition is satisfied; the retained TestDesign freeze admits next: code.

### Out of scope

- Runner-state/cache opt-ins: **CARD-1010**, including D-6 maintenance/lease contracts,
  V-11, RD-9/10 and every explicitly MOVED guard/control; starts after CARD-1008 lands.
- Live rollout, production Docker deletion, provider launches, cache warmup duration,
  server lease implementation and new API fields. RD's identity fixture cannot prove
  production lease expiry; existing CARD-0953 server tests are read-only dependencies.
- Recovery of unreachable Git reflog objects, automatic publication/salvage and the
  retained-container/null extension of CARD-0994. Those remain deliberately refused.
- Atomic exclusion of independently initiated server lands. The no-new-land window
  is an operational prerequisite; script locks and a repeated census do not supply
  an atomic server maintenance lease.
- CARD-0980's five Windows path fixes and CARD-0983's require-jq feature. Reserve the
  shared regions below, but do not implement either sibling card in CARD-1008.
- Async session delivery: no changed queue/session producer exists, as inventoried
  above. Command acceptance is never substituted for observed Docker/receipt effects.

### Admission status, shared files and platform qualification

B-1 is **MOVED to CARD-1010** with PC-121/122; it is not a CARD-1008 admission blocker.

**B-2 resolved — actual importer accepted all five rows.**
The original Plan report (`delegate.ps1 -Status 9de0189e`) records five imported
rows and DOCS-1008 36 passed, but that receipt predates this table. This Plan
reconciliation ran the actual unchanged CLI built as Antiphon.Tests' existing
project reference by the self-leasing DOCS-1008 checkpoint at
`f0df65050d1f5e31b86e677b0472d455b62b7b5f`. Import exited **0**, accepted
**CP-1..CP-5**, and emitted minima **1/19/54/207/1** with estimates **6/15/10/8/12**.
It did not launch a checkpoint executor or a separate build. Exact working command:

`dotnet tools/Antiphon.Checkpoints/bin-c1008-plan/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-03-card-1008-rolling-volume-recycle-and-retire-temp-plan.md --out .antiphon/c1008-plan-checkpoints/imported.yml`

The final documentation amendment corrects the tool path (OutputPath has no
net9.0 suffix) and records this receipt; it leaves the imported table byte-identical.
Re-import at the final committed SHA before cleanup and include that receipt in
the final report. B-2 no longer blocks Code; a later table edit requires a fresh
import. Static parsing or the 36 generic documentation/import tests alone do not
establish executability. Do not launch the owner-bound executor without its token.

| Shared path/region | Overlap and required ordering |
|---|---|
| `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` | C1008 new methods and changed retirement/cold reader blocks overlap the class edited by 0980's five C849 LinuxShell/RepoRoot sites (cache lane, seed, saved-donor import/unsafe archive/size bomb). 0983 may add jq qualification contracts here. |
| `scripts/test-deploy-server2.ps1` | C1008 targeted dispatch/fake state and frozen counters share param/probe/roster regions with 0983's require-jq option. |
| `scripts/test-deploy-server2-jq.ps1` | Shared present-probe qualification, expected groups/invocations/assertions and no-jq fallback. |
| `scripts/fixtures/c727-fake-http.ps1`, `scripts/fixtures/c727-fake-verify.ps1`, `scripts/fixtures/c973-marker-reader.sh` | C1008 extends runner/task state, manifest tracing and retire-reader behavior; reserve against sibling harness edits and re-read any later shared helper change. |
| `scripts/c590-remote.sh` | C1008 deploy/retire, retirement cache-reader and host dispatcher regions; reserve as the brief's shared production region. The current 0980 card is test-only and 0983 names jq harnesses, so neither currently establishes a required production edit here. |
| `scripts/deploy-server2.ps1` | C1008 parameters, strict/retirement predicates, manifest and phase dispatch; same conservative serialization reservation from the brief, not a claim that 0980 currently commissions a production fix. |
| `docs/docker-stack.md`, `DockerStackDocumentationTests.cs` | Policy correction and added test landed by af6d03f1; Code base must contain them; C1008 then updates implementation status without reinstating manual main removal. |

**Landing order:** policy Docs (already landed by af6d03f1) -> this reconciled freeze ->
CARD-1008 Code/Review/land **first** -> CARD-0980 and CARD-0983 re-baseline their
shared files and counts. Do not let either sibling dispatch into these regions
while CARD-1008 owns them. CARD-1010 starts after CARD-1008 lands and re-baselines
its maintenance work then. Their historical 57-result/19-group figures are not
current admission floors. `server/Bundles/orchestrator.md` has no matching rolling,
retire-temp, redeploy-old or recycling instruction in this baseline; no bundle edit
is justified by this inspection.

Missing setup to be supplied by S1: file-backed fake Docker, literal status and task
responses, real local Git graph factory, per-operation persistent journal driver,
injected stop/rm/write/SCP boundaries, isolated RD image/object ledger, and process
ownership/cleanup. Existing c727 verifier only marks oldDeployed and cannot prove
recycling; existing CacheStatusHarness fakes jq with word tokens and cannot prove
null/type predicates. Neither is sufficient without those extensions.

The five existing C849 methods named by CARD-0980 require its Windows/WSL row to
prove wslpath conversion; this Linux worktree cannot run that row and the desktop
checkout is not reachable. New C1008 source-byte shell fixtures must materialize
under Linux or explicitly convert each Windows path, following C973ReaderHarness
and c727-fake-verify, not the five defective callers. V-13/14/16/17/18/19 are the
wrapper/manifest/jq portability observations worth including in a later Windows
qualification; CP-5 requires Linux's own isolated nested daemon. The current five
CPs commission Linux evidence only; no Windows green or live-desktop activation is
claimed. CARD-0980's later Windows full-Remote row must recount from 95 expected
post-C1008 results, plus its own additions, and prove zero skips. Do not block the
ordered C1008 land on an uncommissioned Windows repair or silently add a sixth CP.

**Code start condition:** the corrected policy Docs land is contained in the Code
base; C1008 has exclusive shared-file ownership ahead of 0980/0983 and 1010; the
source census is refreshed there; and the revised table retains its actual importer receipt (B-2 resolved here). No further TestDesign freeze is required for the retained scope.
All **20 V methods, 32 RD outcomes and 125 active guard/control mappings** bind Code.

Reconciliation deltas from b365b1af: V **21 -> 20** (only V-11 moves); PCs/guards
**156 -> 125** (31 moved); CP-2 Min **20 -> 19**; CP-3 remains **54 from 36**;
CP-4 remains **DERIVED 207** after the already-landed policy (206 on this branch);
CP-5 keeps Min **1** but RD outcomes **39 -> 32** (7 moved). CP-1 remains **1**.
Five checkpoint rows and **51 estimated minutes** remain; no measured speed claim.
Remote class projection is **77 methods / 95 results** (66/84 + 11/11), to be
recounted when siblings re-baseline. No unchanged fixture count was reduced.

### Checkpoints

Code admission (task `0c0689e5`, 2026-10-03, source
`f0fadc61635e3d977f045876d65e1e09ef06b17a`): the policy commit
`af6d03f19f9fc8298301187cecebee2c4b4f5d90` is an ancestor. Recounted source
rosters: Remote 66 methods / 84 results; cache selection 36 / 54; CP-4
112 + 23 + 35 + 11 + 20 + 6 = 207. These match the retained freeze (delta 0).
The plan has 156 represented guard IDs, 31 moved, 125 retained (delta 0);
20 retained V designs (delta 0), with zero implemented C1008 methods at admission
(implementation deficit 20). The retained RD expansion is 5 base + 27 changed
= 32 designs (delta 0), with zero implemented outcomes (implementation deficit
32). No execution is claimed by this census. Platform routes were reread: defaults
revision 2, available Linux and Windows runners, unavailable retired temp. No host
pin is added. jq is absent from this runner; Code will provide a task-local jq
for qualified C1008/cache fixtures, without changing CARD-0983's qualification
behavior. The explicitly commissioned Final whole Unit lane is an additional
run to this table; the dispatch brief overrides the older exclusion below.
Shared files remain owned by CARD-1008 ahead of CARD-0980/0983 and CARD-1010.

Continuation admission (Code task `b3777342-2f88-4f7c-8266-86fbbc22d1c9`,
2026-10-03): new landing owner; FF-only base `b0d5c552d0dccf9d85e9a7a0e04465a29eeb2b57`.
Read current master `edb96aecd93cbe90fe8887c1a8d1523527bd49d9` through local Git objects,
without fetch/merge/rebase. CARD-0927 adds jq to the image/verifier and tests in other
classes; retained cache census remains 36 methods/54 results, six CP-4 classes remain
112+23+35+11+20+6=207, Remote remains 77/95 with this draft. Retained designs remain
20 V methods, 32 RD outcomes and 125 guard/PC mappings: all deltas zero.
The process image still lacks jq; supplied task-local jq 1.7.1 has SHA-256
`5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, independently
matched to CARD-0927's committed Dockerfile pin. Platform routes were reread:
defaults revision 2, live Linux/Windows and unavailable temp; no placement pin.
CP-2 at this continuation base completed 19/19 passed, 0 failed/skipped, clean
source and verified build provenance; measured build+row wall was 29 minutes,
above the table's 15-minute estimate. This proves the existing implemented vectors,
not the predecessor report's remaining guard/resume/real-Docker obligations.
The Final brief additionally requires whole Unit and every full affected class;
it overrides the older implementation-profile exclusion below.

Closed Code list. Each row owns one isolated build and one literal TUnit filter.
Group names name the lane; no unsupported `Lane` column is added to the importer.
CP-1 is the explicit expected-red preparatory row, not a final green certificate.
All other rows require zero failures/skips at their committed slice SHA. Final
Review runs CP-2..CP-5 at the reviewed tip. Counts are frozen by source inspection above; `Min` counts TUnit results, not harness assertions.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1008-red/` | linux-offline-red | `/*/*/RollingVolumeRecycleScriptTests/C1008_Retired_absent_null_is_accepted` | V-13 | 1 executed, 1 expected assertion failure against unchanged scripts; no setup error | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c1008-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S3 | `tests/Antiphon.Tests -> bin-c1008-scripts/` | linux-offline-contract | `/*/*/(RemoteScriptContractTests*)\|(RollingVolumeRecycleScriptTests*)/C1008_*` | V-1..V-10, V-12..V-20 | 19 named single-result methods, V-11 moved, 0 failed/skipped; internal rolling rosters reported separately | 19 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c1008-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S4 | `tests/Antiphon.Tests -> bin-c1008-cache/` | linux-cache-regression | `/*/*/RemoteScriptContractTests*/(C849_*)\|(C912_*)\|(C973_*)\|(C944_*)\|(C951_*)\|(C976_*)\|(C946_*)\|(C957_*)` | R-1 | 54 expanded results from 36 methods, 0 failed/skipped; retirement reader changes specified in the freeze | 54 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c1008-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1008-docs/` | linux-compose-doc-contract | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(DockerStackSmokeCommandTests*)\|(DockerStackDocumentationTests*)\|(CheckpointImportTests*)\|(CheckpointManifestTests*)/*` | R-2 | DERIVED 207 = 112+23+35+11+20+6 at policy land af6d03f1; recount at Code admission; 0 failed/skipped | 207 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1008-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c1008-real/` | linux-isolated-docker | `/*/*/RollingVolumeRecycleDockerTests/C1008_Real_docker_comparison` | V-21 | 1 TUnit result, 32 named RD outcomes (5 base + 27 changed; 7 moved), 0 failed/skipped, zero fixture residue | 1 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c1008-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

## Test-design freeze and execution hooks

The retained TestDesign freeze supplies status/Git/volume/resume vectors and exact
methods/counts. Code implements that contract; it does not redesign it. Preserve:

1. Literal missing-versus-null status inputs, stopped references, same-SHA partial
   recovery, command ordering, receipt shape and the first labelled assertion.
2. Actual summary/detail DTOs, complete unscoped/excluded-project task census,
   pending lands on succeeded tasks, retained Blocked/Failed work and the stated
   no-new-land operational window. Failed reads never become empty arrays.
3. Default state/cache/marker preservation and zero cold seed; ordinary Cold and
   saved-donor gates remain unchanged. Maintenance-only vectors moved to CARD-1010.
4. Live-zero-before-own-stop evidence and the main null-after-own-stop resume proof,
   without broadening the retired-temp exception or adding state replacement waits.
5. All 20 active V methods, R-1/R-2 expansion, 32 RD outcomes and 125 method-scoped
   PC input/first-assertion bindings. A method omitted from its filter is a defect.
6. The real import of this exact table (B-2 resolved). A static coverage pass is a syntax check,
   never a method-scoped Mutation receipt.

Code commits/pushes every meaningful slice and before a build. Use the checkpoint
tool once per committed slice group with `--expected-source-sha <full-sha>` and
the table rows; continue `wait` while exit is 75. Keep every child owned and awaited;
do not edit source under a run. The bootstrap build, if needed, is the only declared
extra build: isolated `bin-c1008-tool/` through `scripts/build-slot.ps1`. Row drivers
own their slots; do not wrap a self-leasing checkpoint driver in another slot.
Timeout exit 4 is not-run, never a reason for `-NoSlot` or an unleased retry.

Report per-CP executed/passed/failed/skipped counts and source/build/slot receipts,
plus separate harness groups/invocations/assertions and RD outcomes. Confirm any
unexpected red on the assigned base using the exact failing method before calling
it inherited. No whole Unit/full assembly, provider call, real rollout, production
volume deletion or repeated test battery belongs to this implementation profile.
Remove only task-owned `bin-c1008-*/` outputs after awaited runs, with nonempty,
canonical in-root path checks; preserve evidence.

### Plan-stage documentation check

This Plan dispatch runs only the following existing documentation/import contracts,
once after its final committed plan SHA, as required by the brief:

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name DOCS-1008 -Project tests/Antiphon.Tests -OutputPath bin-c1008-plan/ -Filter '/*/*/(DockerStackDocumentationTests*)|(CheckpointImportTests*)|(CheckpointManifestTests*)/*' -MinExecuted 36 -Expect DockerStackDocumentationTests,CheckpointImportTests,CheckpointManifestTests -ExpectedSourceSha <full-plan-sha> -ResultsRoot .antiphon/c1008-plan-checkpoints
```

The baseline has 10 DockerStackDocumentationTests, 20 CheckpointImportTests and 6
CheckpointManifestTests. The latter own table-format fixtures and historical plan
pins; none is a generic assertion that every new plan is executable. Report this
limit. Runtime script implementation and V/R/RD/PC execution remain future work.

## Documentation and bundle pins

D-8: keep these policy sentences true when updating examples from the already-landed
Docs policy. They are quoted from CARD-1008's agreed policy; normalize whitespace in doc
tests rather than pinning wrapping. Add executable volume/phase checks alongside
the sentence tests, so prose alone cannot certify implementation.

| Policy claim to preserve in `docs/docker-stack.md` | Implementation/test obligation |
|---|---|
| "Retiring TEMP: always `compose down -v` for the temp project" | D-1/D-4, all four private volumes removed, retirement retained. |
| "Default for main = recycle work + runner-tmp + dind-data." | D-1 exact names, default state/cache sentinels preserved. |
| "Recycling runner-state and/or the cache volumes is an explicit opt-in" | MOVED to CARD-1010; document as deferred opt-ins, absent in CARD-1008. Here state/caches always remain preserved. |
| "Preconditions for ANY volume removal: drain complete; 0 sessions, 0 queued, nothing routed there; no land in flight; container stopped; volumes unreferenced by any container" | D-2/D-3/D-4 and the no-new-land operational window; receipt names the evidence, not just 'idle'. |
| "remove by EXACT name" and "never prune" | Fixed allowlists; prefix/unrelated-project negative sentinels. |
| "df before/after recorded" | D-5 numeric before/after receipt even when a later deployment step fails. |
| "before removing a work volume, enumerate runner-side mirrors/worktrees for commits not on any origin ref and for Blocked/Failed tasks bound to the runner" | D-3 uid-1654 proof and typed recovery refusal. |
| "a recycled runner-tmp volume copies the image /tmp (including /tmp/antiphon-pty-hosts) on first mount" | V-6/RD-1 normal Docker copy-up, no selective `/tmp` sweep. |

Keep the Docs task's `AGENTS.md` rollout front door and
`docs/orchestration-loop.md` autonomy links; this script task does not duplicate
their policy paragraphs. Search `server/Bundles/orchestrator.md` for conflicting
rollout text at admission; if none exists, no bundle edit is needed. Keep live
host locations out of stage/dispatch examples; existing deployment transport
configuration is a different concern. Remove any now-stale manual-only caveat
only once the corresponding script/tests have landed, not in this Plan commit.

## Risk register

| Risk | Control / residual limitation |
|---|---|
| Destructive selection or prefix collision (`antiphon-runner_` versus `antiphon-runner-temp_`). | Fixed exact arrays, inspected Compose model and per-name rm; no prune. Foreign `schoolrevision-staging`, `openclaw-state` and similarly prefixed volumes are canaries. |
| Stopped containers/state-init still reference work. | All-container census, inspect owners and remove only proven stopped owned IDs; keep broker. Foreign or uncertain owner refuses. |
| Lost unpublished/blocked work, root returning false zeros. | Double uid-1654 audit, checked Git exits, current origin comparison, all local tips and closed task census; no automatic salvage side effects. |
| A land/task or container appears between observations. | Drain, script serialization and immediate rechecks; Docker protects in-use volume removal. Server lands require the operational no-new-land interval; no atomic cross-service lease is claimed. |
| Partial failure and unsafe retries. | Durable external journal, per-volume states, exact identity/CreatedAt binding, explicit resume; no redeletion of recreated data. |
| CARD-0994 present/null dead end. | Intentionally preserve the refusal; fix absent/null only. Caller arranges a reviewed exact-container recovery if needed; deploy-temp never bypasses TempContainersRemain. |
| CacheDiskLow blocks the cleanup intended to solve it. | Read-only safety checks before deletion, allocation gate after reclaim; deploy-temp early gate remains, requiring separate approved reclaim if temp cannot start. |
| `/tmp` loses pty-host assets. | Whole exact volume recreation with image copy-up; inspect assets/mode on real Docker. No pattern cleanup. |
| A misleading df delta or incomplete success receipt. | Same filesystem/units, signed delta and partial outcomes; no guaranteed minimum bytes recovered. Evidence failures stop admission. |
| Dry-run accidentally follows generic mutating host setup. | Early separate preview dispatch, forbidden-operation trace, no preview-as-permit; offline audit marked pending. |
| PowerShell 5.1 encoding fallback / quoting. | All touched scripts remain ASCII-only even though this driver requires PS7; use literal args and typed JSON, preserve Windows backslash config paths. Never interpolate raw user data into SSH shell. |
| Test doubles agree with themselves. | Real Docker comparison, real Git refs, actual wrapper/host execution, post-land mutation; historical counts are not new evidence. |
| Shared-file collisions or outdated policy base. | Policy landed by af6d03f1; require it at Code admission. CARD-1008 first, then CARD-0980/0983 re-baseline and CARD-1010 starts. Preserve sibling changes. |

## Rollout sequencing and recovery

The policy Docs change has landed. Land this reconciled freeze, then reviewed Code.
Run the isolated RD comparison and ordinary checkpoints before any operational use;
post-land Mutation is separately commissioned. No AppHost restart is required for
script-only activation: the rollout caller must run the reviewed landed script SHA,
and host transport must verify that same SHA. State-reset admission belongs to
CARD-1010; ordinary preserved-store registration still uses the existing server.

For the first normal rollout: confirm no lands are active/pending and hold new
lands; run the read-only preview; deploy/verify temp; drain main; require zero
work and publication proof; recycle default main volumes; verify the exact new
runner SHA/mounts/caches before admission; run the existing smoke/canary gates;
return scheduling to main, drain temp, then retire temp and retain both reclaim
receipts. State/cache opt-ins are unavailable in this card.

If main recycle/up fails, keep temp accepting and main drained. A rollback image
can start with newly created disposable volumes, but deleted work/tmp/dind payload
cannot be rolled back. Re-run only via the matching operation's journal. If temp
retirement refuses, keep main accepting and preserve temp's retirement/evidence;
do not clear it to force a future deploy. CARD-1010 separately owns opt-in failure
and maintenance recovery; it adds no recovery prerequisite to the default path.

### Cost

Ordinary Code checkpoints total **51 estimated minutes** (6+15+10+8+12), including
the expected-red row and isolated builds; authoring/TestDesign, queue waits and
post-land controls are additional. CP-2's rolling harness is intentionally bounded
and counted separately from its 19 TUnit results. No performance claim follows
from these estimates. This Plan task is time-boxed to 40 minutes and runs only its
36-result documentation selection.

Reconciled estimates (no PC execution by this Plan task): ordinary Code floor
remains **51 minutes**: CP-1 6 + CP-2 15 + CP-3 10 + CP-4 8 + CP-5 12,
including isolated builds. One-time Code tool bootstrap/setup adds **6 minutes**,
giving **57** before authoring/slot waits. The active PC inventory is **125**.
Mutation has **6 minutes** setup and **772 minutes** of method-scoped cycles:
114 controls at 6 minutes plus 11 V-7 resume controls at 8. The former 30 V-11
controls lose 29 to CARD-1010; PC-117 runs within V-9 at the ordinary six-minute
estimate. PC-29 and PC-156 also move, accounting for all 31 retired controls.
Each remaining cycle uses its literal exact method filter and Min=1.
The six-minute unit includes 0.5 edit/restore, 2 red build, 0.5 red method, 2 green
build, 0.5 restored method and 0.5 evidence; V-7 adds 1 per method execution.
Mutation floor = **778 minutes**. Combined verification estimate =
**6 + 51 + 6 + 772 = 835 minutes** (13 hours 55 minutes), excluding authoring,
repairs and queue waits. Final Review CP-2..CP-5 adds **45**, giving **880 minutes**.
CARD-1010 must budget its moved controls and lease-contract work independently.

Compared with two whole CP-2 builds/runs per active control (125 x 30 = 3,750
minutes), method-scoped cycles estimate **2,978 minutes** saved; measured savings
remain **0**. These are conservative retained checkpoint estimates, not speed evidence.

## FOLLOW-UPS

Board searches `unpublished` and `retire-temp` used `card.ps1 search -Board Antiphon
-All`, then full reads of CARD-0831/CARD-0994. Reuse those cards; do not file duplicate
retirement bugs. CARD-1008 implements CARD-0831's work-preservation gate and the
latest absent-placeholder portion of CARD-0994. Its retained-container/null case
remains explicitly refused. CARD-0935 already tracks the separate canary-before-
promotion issue; this card does not move that gate. CARD-1010 is the existing commissioned follow-up for both opt-ins and B-1; no new
duplicate card is needed. CARD-0980/0983 re-baseline after CARD-1008 lands.

--- next stage ---
next: code
handoff: Implement the retained freeze: default main recycle with its own stop, retired-absent temp acceptance and temp down -v; preview remains. 125 executable PCs, 20 V methods, 32 RD outcomes; CP minima 1/19/54/207-derived/1. B-2 import passed. Require policy land af6d03f1 and admission recount. CARD-1008 lands before 0980/0983 re-baseline and 1010 starts.
artifact: docs/superpowers/plans/2026-10-03-card-1008-rolling-volume-recycle-and-retire-temp-plan.md

### Continuation platform identity correction (b3777342)

A plain local Docker volume reports `Options:null` on this nested daemon. The identity predicate accepts explicit null and an empty object; omitted, array, scalar and nonempty option values remain unknown. The new literal-null V-1 vector failed at its removal assertion on committed 9342aca57d79c4276b3251bbe04605f240f41937 (one executed/failed, successful build). This is ordinary defect qualification, not a deliberate Mutation cycle.
