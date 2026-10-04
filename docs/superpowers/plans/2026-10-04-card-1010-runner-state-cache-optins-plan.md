# CARD-1010: explicit runner-state and cache recycling

Date: 2026-10-04. Plan task: `adbad12b-a8dc-44c8-b312-f2a9a4f6a5f8`.
Inspected source: `7b8e687a73c17167a49b6ce0a3ace1d1ab1f796a`, containing the
CARD-1008 land `004987768`. This dispatch changes this plan only.

## Outcome and stage boundary

Implement this card in three separately reviewable slices. **S1, an observable
store-replacement admission snapshot, comes first.** It adds no recycle flags and
performs no deletion. S2 implements the explicit runner-state reset; S3 implements
shared-cache maintenance and the combined operation. This keeps a new server
contract separate from destructive script integration and cold-seed recovery.

TestDesign is a separate stage. The decisions here are made within the requested
Plan authority, rather than assumptions awaiting product approval. The proposed
S1 checkpoint roster needs TestDesign's fixture, guard/control and importer freeze
before Code. S2/S3 each need a subsequent freeze against the preceding landed
slice; they are not implicitly included in the first Code dispatch. CARD-1010 is
not complete merely because S1 lands.

Read the full live CARD-1010 and its history: the sole revision is the move for this
Plan, not an additional policy decision. Read the CARD-1008 plan, its explicit
31-control transfer, the landed volume policy, deployment wrapper and relevant
host recycle/cache functions. Read project, orchestration, card lifecycle, HTTP,
session-runtime and build/checkpoint owners. Current card/plan observations below
are planning inputs, not receipts proving activation or a clean historical test run.

## Ground truth

| Card assumption | Code at the inspected source | Design consequence |
|---|---|---|
| The flags can be added to an existing option model. | `scripts/deploy-server2.ps1` has neither switch. `Assert-RecycleContext`, `scripts/c590-real.ps1` and the host accept only the schema-1 default context. | Add explicit versioned opt-in transport later; keep schema-1 default behavior and old journal recovery. Unknown options must fail before mutation. |
| Plain main recycle already removes the disposable set. | `c1008_recycle` selects work, runner-tmp and dind-data; temp adds runner-state. `redeploy-old` preserves the old store, requires accepting temp, and skips healthy same-SHA replacement. | Preserve CARD-1008. Maintenance gets a distinct context; its stronger/different counterpart guard must never leak into default recycling. |
| Unavailable means detached. | `PhoneHomeRunnerDirectory.ReadSnapshot` only marks an expired live connection ineligible. It does not null `slot.Live`; `Disconnect` does. | Observe attachment directly from the production admission state. Socket closed, lease expired and available=false are insufficient. |
| A 90-second wait proves state replacement is allowed. | `Register` checks authorization, `slot.Live`, `slot.LeaseUntil`, and `LastDisconnect.AtUtc + configured LeaseSeconds`. `AcceptConnect` also updates LeaseUntil. Heartbeats and disconnects need not coincide with registration. | Publish a coherent predicate using the server clock and both actual deadlines. No hard-coded lease duration or client-clock permission decision. |
| Existing status already contains enough information. | `PhoneHomeRunnerStatusDto` in `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs` has old store, live epoch, heartbeat/disconnect and work counters; no replacement authorization, attachment or lease deadlines. Detached top-level epoch is null. | S1 adds a nested nullable admission snapshot with its own bound store/epoch. No fake-only DTO fields. |
| Drain clear alone permits a new identity. | `ApplyState` authorizes replacement only when a retired row becomes non-retired and non-draining. Any successful registration, even the old store, consumes authorization. A normal drain/clear does not grant it. | Preserve CARD-0953 exactly. Save retirement evidence, remove old containers, clear, immediately establish a non-retiring hold, then observe readiness. |
| Cold Seed can already operate with main absent. | `c849_cold_proof` requires exactly one running main without overlapping cache mounts; `c849_cold_seed` ends through `write_result`, which exits. | Introduce only a journal-bound maintenance proof and a returning seed core. Do not weaken ordinary Cold or let nested seed success terminate deployment early. |
| Cold Seed warms packages. | CARD-0912 creates/validates empty labelled writable roots and atomically publishes a schema-2 cold marker. It does not download a warm payload. | Promise cold, validated caches; subsequent restore can be slow. Full-marker-only contexts remain full-only. |
| Stopped containers release volumes. | Docker references remain after stop; state-init also mounts work/state. The build-slot broker is a separate service. | Remove only inspected owned runner/state-init IDs, prove every reference absent, retain broker and unrelated containers. |
| A live-zero proof can be recovered from any offline main. | CARD-1008 accepts null inventory only against its own matching stop/removal journal; manual stop otherwise refuses. | Save strict live zeros before retirement/stop. Resume is operation/generation-bound, never a generic null-to-zero conversion. |
| jq in the image satisfies host prerequisites. | `c1008_recycle` runs on the host and calls `command -v jq`; failure is `RecycleToolsMissing`, before deletion. CARD-1025 documents this unmet host dependency. | Host jq qualification is an activation prerequisite; fixture jq does not discharge it. |
| Empty/idle workspace means published work. | CARD-1008 closes task/land census across project exclusions and audits real Git as uid 1654 before and after stop. Failed/Blocked owners and unpublished or dirty work refuse. | Reuse these guards for every selected work volume. No task cleanup, salvage or publication side effects in this card. |

Placement reads succeeded at approximately 00:34 UTC: `/api/runner-defaults`
revision 2, no kind overrides; `/api/session-runners` reported eligible Linux and
Windows lanes and an unavailable/draining temporary entry. Re-read both routes
at dispatch. Omit `-Runner` unless deliberately pinning a host; omit `-Platform`
for S1 portable work. Shell/Docker fixture slices require `-Platform Linux`.
`-Platform Any` removes an inherited OS pin. Deployment identifiers below describe
existing resources, not a hard-coded location for future task placement.

## Decisions

### D-1: opt-ins select exact sets and never become defaults

Both switches default false. A true `-RecycleRunnerState` or `-RecycleCaches` is
valid only with an explicitly supplied `-Rolling -Phase redeploy-old`. Reject
`all`, omitted phase, temp phases and unrelated host cases as
`RecycleOptionsInvalid`, including preview. Reject malformed/non-boolean JSON
options independently in the wrapper, bridge and host. Explicit false selects
ordinary behavior. No caller-supplied list of volume names is accepted.

| Invocation | Exact removals in addition to ordinary main work/tmp/dind recycle | Preserved |
|---|---|---|
| Neither flag | None | Main runner-state; all three shared caches and their marker |
| State only | `antiphon-runner_runner-state` | All caches and seed marker |
| Caches only | `antiphon-runner-cache-nuget-packages`, `antiphon-runner-cache-nuget-scratch`, `antiphon-runner-cache-npm-content` | Main runner-state and its identity |
| Both, after S3 ships | Union of the two rows | Every resource outside that exact union |
| Temp retirement, unchanged | All four `antiphon-runner-temp_` private volumes: work, runner-tmp, dind-data, runner-state | Main volumes and all external shared caches |

Plain recycle never retires/clears main for identity replacement and never invokes
cold Seed. S2 ships only the state switch; do not publish a placeholder cache flag
that appears usable before S3. Opt-in apply at an already-running requested SHA
is a refusal (`RecycleOptionsInvalid`, detail `same_sha_requires_new_rollout`),
unless resuming a matching unfinished operation. This prevents a rerun from
silently deleting a successfully recreated generation. Preview may still list the
prospective set and that refusal. Ordinary same-SHA retries remain verification-only.
Opt-in `-DryRun` inherits CARD-1008's read-only contract: no POST, stop, remove,
start, volume creation, seed, marker change or checkout mutation. Evidence writes
are permitted; an absent-runner audit requiring a helper remains auditPending.
Apply always obtains new proofs and cannot consume a preview as authorization.

Rejected: automatic low-disk opt-ins, flags on `all`, broad main `down -v`, prune,
prefix selection, force volume removal, and repeated destructive same-SHA retries.

### D-2: publish server-owned replacement admission, not a guessed wait

Append `StoreReplacementAdmission = null` to `PhoneHomeRunnerStatusDto`, using a
new `RunnerStoreReplacementAdmissionDto` in the same contracts file. Its JSON is
`storeReplacementAdmission` with these fields:

| Field | Meaning |
|---|---|
| `schema` | Integer 1, identifying this observable contract |
| `observedAtUtc` | Server `TimeProvider.GetUtcNow()` sampled for this observation |
| `boundStoreId` | Nullable slot store, not a caller-provided replacement candidate |
| `boundEpoch` | Slot epoch, retained even when the connection is detached |
| `retiredAt` | Mirrored retirement stamp sampled with the admission state |
| `authorized` | Current `StoreReplacementAuthorized` value |
| `connectionAttached` | Exactly `slot.Live != null`, irrespective of socket/heartbeat eligibility |
| `registrationLeaseUntilUtc` | Slot LeaseUntil; null only when no registration has established a binding |
| `disconnectLeaseUntilUtc` | Last recorded disconnect time plus the actual configured lease duration, or null when no disconnect exists |
| `ready` | The predicate below evaluated by the server, not recomputed by PowerShell |

For a known configured slot:

```text
ready = entry.Enabled
     && boundStoreId != null
     && retiredAt == null
     && authorized
     && !connectionAttached
     && registrationLeaseUntilUtc <= observedAtUtc
     && (disconnectLeaseUntilUtc == null || disconnectLeaseUntilUtc <= observedAtUtc)
```

A non-null binding requires a registration deadline. Equality is admitted, matching
Register's existing strict `> now` refusals. The deadline named registration includes
AcceptConnect's update; do not derive it from LastHeartbeatUtc. The disconnect
deadline can be later than the registration deadline. No extra grace period is added.

Capture the whole nested object under `_gate`, using one clock sample. Reuse a
private admission evaluator in `Register` for the **different, already-bound store**
branch and in status; do not run its bound-store requirement on first registration
or same-store reconnect. Preserve the earlier protocol/runner/retirement/capacity
checks and later boot, ticket and epoch behavior, including their refusal ordering.
Do not hold `_gate` across the status database counts or other I/O. Status may keep
its existing eligibility observation, but must not disconnect a connection, renew
a lease, clear retirement, consume authorization, register or mint a ticket.

Known never-connected/disabled slots have a snapshot with ready=false; local/desktop
status has null because this is a phone-home contract. Unknown runner IDs retain
404. Old servers omit the object; old serialized DTOs deserialize to null. Missing,
null, malformed or unsupported-schema admission is unknown to the future state
script and refuses `RecycleStateResetNotAuthorized` before destructive work.
The first slice requires no protocol-version bump, DB migration, UI or runner build.

This is an observation, not an admission reservation. Before starting the replacement,
the script requires ready=true for the journal's old store and expected epoch plus
unchanged maintenance hold and host absence. `Register` still arbitrates atomically.
A reconnect consumes authorization and invalidates the proof. A server restart
losing that in-memory binding/authorization also invalidates it; do not manufacture
the missing old store from a journal or DB edit. Retain the operation for reviewed
recovery. A readiness timeout retains the hold and reports the observed blockers.

Rejected alternatives: exposing just LeaseSeconds (misses the two deadlines and
attachment); fixed 90-second sleeps; treating available=false as detached; fake
leaseExpired fields; probing registration with synthetic identities/tickets; and
using repeated StoreMismatch responses as the wait protocol. The latter would
start replacement before the wait is proven and blur a mandatory stop condition.

### D-3: opt-ins require a deliberate maintenance window

Both opt-ins use a closed-consumer maintenance context. Before beginning it, retire
and remove temp using CARD-1008 while main is still accepting. Then drain main with
no redirect and `retireWhenIdle=false`; a redirect to retired temp is invalid.
The window intentionally suspends service on this pair. It cannot be inserted
unchanged into the usual accepting-temp rolling sequence.

Require main's three counters present, integer and zero while the original runner
is live, `acceptingNewWork=false`, and the expected non-retiring drain. Require temp
retired/offline/absent with sessions=0, queuedTasks=0 and runnerSessions=0 or the
existing narrow retired-absent null exception. Its redirect-to-main retirement
stamp remains recorded, but no work may be routed through either consumer. No
generic offline-main exception is introduced. Cache-only must preserve main store;
state-only and combined require D-4. Temp accepting, ambiguous status or any
foreign/bind-overlap consumer refuses `RecycleCacheMaintenanceRequired` for cache
selection, or `RecycleStateResetNotAuthorized` for state-only selection.

Reuse all default task, routing, land, unpublished-Git, container, volume, disk and
receipt guards. Close the task census for **both** consumer IDs, including pending
lands on succeeded tasks and excluded project scopes. Hold the operator's no-new-
land interval throughout maintenance. A script lock is not a server-wide repository
or scheduling lease; recheck immediately before each destructive boundary and stop
on drift. Never delete unpublished work to make maintenance eligible.

The runner stays up during the user's normal manual in-container cleanup. Scripted
volume recycling instead stops it only after the saved live-zero proof. Keep the
host build-slot broker up. An owned retirement-induced exit may be accepted only
against the same operation's saved identities and retirement receipt; an unrelated
offline/stopped runner does not acquire authority. No force-retire, session kill or
automatic four-hour timeout escalation is part of these flags.

### D-4: state replacement is one-way and journaled

S2 adds these boundaries around the inherited recycle transaction:

1. Validate the admission contract is present, target SHA/options/Compose and all
   preservation proofs. Persist strict live zeros, old store/epoch, exact container
   and volume generations, helper image, task/Git audit and intended state reset
   **before** requesting retirement. A writable durable journal is mandatory.
2. Request ordinary drain with `retireWhenIdle=true` and no redirect. Wait for real
   idle retirement and persist its stamp. Do not synthesize a stamp from drain or
   call forced retirement. Recheck zeros/owned identities against the pre-retire
   receipt even if retirement has already made the runner exit.
3. Gracefully stop the identified runner if still running; remove only it and its
   identified stopped state-init containers without `-v`. Prove absence and zero
   references, including stopped foreign containers and canonical bind overlaps.
   Repeat the quiescent uid-1654 Git audit and remove its helper before final census.
4. Delete the selected exact volumes one at a time, checking each outcome. State
   deletion is irreversible; retained images do not restore its old contents or
   session identity. Preserve every unselected generation.
5. Only after owned removal and selected deletion are recorded, use the existing
   operator-authenticated `/drain/clear`; immediately POST a non-retiring drain
   with no redirect. The old containers are absent throughout this brief clear
   interval. Verify the hold and save the clear/hold acknowledgements. A failed
   hold prevents all recreation; resume must establish it first.
6. Poll D-2 until ready=true for the old binding, both leases elapsed and connection
   detached. Use the server predicate, a bounded wait and cancellation; local
   timestamps are diagnostics only. If either consumer/routing fact changes, stop.
7. Recreate through the existing state-init/checkout/Compose path. Require a
   nonempty **different** runnerStoreId, a newer epoch within the same directory
   lifetime, requested full build SHA and completed recovery while the hold stays
   closed. StoreMismatch is a stop, never success, a permission bypass or a reason
   to edit DB identity. Existing retained sessions are not rebound to the new store.
8. Verify mounts, tmp copy-up/pty-host assets and caches. Only then clear the
   maintenance hold, confirm acceptingNewWork and finish/copy the journal.

Use the existing `WaitIdleMinutes` as one monotonic retirement/admission wait
budget, starting at retirement request (default 480 minutes; existing bounds).
Do not reset it per poll or infer expiry from time spent waiting. Exhaustion is
`RecycleStateResetNotAuthorized` with a wait-timeout detail and retained proof;
a configured lease longer than the budget requires an explicit longer bounded
invocation, never a bypass. Resume records a fresh wait attempt without replaying
completed destructive steps.

Loss includes runner-local manifests, stores, capacity/configuration and provider
state on runner-state (for example volume-backed Grok/Claude login state). Separately
bind-mounted host Codex homes and secret mounts remain protected. Do not inspect,
copy, log or delete their contents as part of this operation. Reauthentication is
a separately authorized human action; a new runner store is not proof of provider
authentication. Preserve the existing sanctioned read-only readiness checks.

### D-5: cache removal and seed form one recoverable maintenance operation

Inspect/retain the exact helper image digest before deleting caches or retiring
superseded images. All three cache volumes must pass exact identity/labels, local
driver with no options, canonical non-symlink Docker root/mountpoints and complete
consumer scans. Validate all targets before the first removal; Docker's own
in-use refusal remains final. Neither main nor temp nor any foreign/stopped/bind
consumer may remain attached. Require no active build work/host build leases that
can use the caches, without stopping or reconfiguring the broker.

Persist old marker identity/content digest and original cache generations. Remove
the **old cache seed marker only after all three original cache removals have
completed**, recording that boundary before recreating roots. This is the single
marker invalidation the flag authorizes. Keep donor tar, recovery directories,
deployment/SHA/rollback markers and operation journals. No donor deletion, Reset
or prune follows implicitly. A partial removal leaves its failure and old marker
recorded, but every seed/deploy/cache-consuming entry point must refuse the open
maintenance journal, so that old marker can never certify partially missing caches.

Extend CARD-0912 with a maintenance-only absent-main proof derived from the matched
journal, not a boolean bypass. Keep ordinary `-Case Seed -Cold`'s running-main
requirement unchanged. Reuse a returning cold-seed core; the public standalone
case may still emit its terminal result. Route an internal
`runner-cache-recycle-seed` host case through the real wrapper/bridge rosters, valid
only for a matching cache-selected maintenance operation at `cacheRemoved`.

Create exactly the three labelled local volumes, then verify uid/gid 1654:1654,
mode 0700, canonical non-symlink roots and emptiness **including hidden entries**.
Require explicit existence before any helper mount, preventing Docker auto-create
of an unlabelled volume. Run bounded uid-1654 create/rename/delete probes with
network disabled and retained image; track helper IDs/canaries and prove cleanup.
Recheck maintenance facts around create, initialization, probe and final marker
publication. Publish schema-2 cold marker atomically only after all proofs succeed.
Then continue actual deployment and `verify-runner-caches`: verify recreated volume
generations, live mounts, writability and the cold-appropriate smoke. A seed result
alone cannot return deployment success. Full-only seed contexts remain full-only.

Cache-only retains and verifies the same main store. Combined selection follows
D-4 retirement/removal/clear/wait, seeds under the journal-bound absent-main proof,
and starts the new identity only after both operations are ready. Ordinary restore
warms cold caches later; no seed-time network warmup promise or fixed duration.

### D-6: maintain lock order, versioned receipts and safe resume

Use the existing rollout lock then cache-maintenance lock, always in that order.
No recursion: the seed core receives only an internal, validated lock-held context;
it does not reacquire a parent's lock. Keep operator credentials in the existing
desktop token reader; do not export them into the host environment or evidence.

State reset necessarily alternates host work and operator HTTP actions. Split the
`deploy-parent` opt-in transport into strict `prepare`, `remove`, `recreate` steps;
each host invocation acquires/releases the ordered locks. Wrapper POSTs use their
existing admission lock. Do not hold the host lock while synchronously invoking a
wrapper POST that tries to acquire it again. The durable open maintenance journal
reserves the operation between invocations: competing rolling/seed/reset/prune
entry points check it under the rollout lock and refuse, including default paths.
Only the exact owning operation and its permitted next step may pass that check;
an owner ID alone does not authorize an out-of-order POST or host case. Competing
paths gain no maintenance authority. External direct API actions remain covered by
fresh checks and the operator no-new-work interval, not a claimed distributed lock.

Opt-in context and journals use schema/version 2, explicit boolean flags, action,
operation ID, full source SHA, project ID and fixed main project. Keep schema-1
default contexts/journals valid only for their original non-opt-in resume. Bind
schema-2 resumes to the exact options, original/recreated generations, retirement
stamp, old/new store, Compose digest and last completed step. No silent journal
upgrade or new generation deletion. Teach incomplete-operation discovery to read
both versions so same-SHA checks cannot hide a schema-2 operation.

Journal boundaries include prepared live proof, retire requested/stamped, owned
stop/removal, each volume outcome, state removed, retirement cleared, verification
hold, observed admission, all caches removed, marker invalidated, roots seeded,
recreated identities, verified, promoted and completed. A pending intent is not a
completed action. Reconcile a lost acknowledgement by current exact identity and
recorded intent; if it cannot be distinguished, refuse `RecycleResumeMismatch`.
In particular, never clear a new retirement, repeat a successful reset or erase a
new marker/volume to make an old operation resume.

Evidence stays outside all recycled volumes. Retain source-bound host journal and
copied receipt, before/after data-root free bytes, signed delta and partial outcomes.
Receipt-copy failure is failure even if the runner starts. On maintenance failure
keep main closed and temp retired/absent; unlike ordinary rolling failure, do not
claim temp is accepting. Reopening temp or changing pins/budgets is separate recovery.

### D-7: preserve user policy and explicit human gates

Manual main cleanup remains **in-container, as uid 1654, with the runner running**,
restricted to the documented audited disposable candidates. Main volume recycle
is **SCRIPTED ONLY** in explicit rolling redeploy-old; never manual stop/remove,
blanket down-v or a host name sweep. Temp remains disposable after its completed
drain: remove all four private volumes and retain shared external caches.

Future use of either state/cache flag requires explicit human authorization naming
the selected loss and maintenance window. Planning, implementing, testing or
landing the flag is not that authorization. Reset/prune, other marker/donor
deletion, secret handling, killing live sessions and changing routing pins/settings
retain their existing human-only boundaries. Ordinary sanctioned staged phases
and running-main cleanup keep their existing operational authority.

## Slices and exact files

| Slice | Owned files | Required tests and finish boundary |
|---|---|---|
| **S1 first: observable admission** | `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`; `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs`; new `tests/Antiphon.Tests/Application/PhoneHomeRunnerReplacementAdmissionTests.cs`; `docs/ops-http.md`; `docs/antiphon-api.md`; this plan | Ten proposed tests below plus existing directory/catalogue/retirement regressions. Read `SessionRunnerEndpoints.cs`, `PhoneHomeTestHost.cs`, `RunnerStateService.cs`, `RunnerRetireService.cs`; no edits needed there. Commit/push coherent implementation before checkpoints; ordinary Review, land and contract activation precede destructive integration. |
| **S2 later: state-only** | `scripts/deploy-server2.ps1`; `scripts/c590-remote.sh`; `scripts/c590-real.ps1`; `scripts/verify-docker-stack.ps1`; new `scripts/fixtures/c1010-recycle-cases.json`, `scripts/fixtures/c1010-maintenance-real-cases.mjs`; `scripts/fixtures/c727-fake-http.ps1`, `c727-fake-verify.ps1`; new `tests/Antiphon.Tests/Scripts/RunnerStateRecycleScriptTests.cs`, `RunnerStateRecycleDockerTests.cs`; affected assertions in `RollingVolumeRecycleScriptTests.cs`, `RemoteScriptContractTests.cs`, `Infrastructure/DockerStackDocumentationTests.cs`; `docs/docker-stack.md`; this plan | Proposed `C1010_State_reset_waits_for_real_admission`, `C1010_State_reset_requires_saved_live_proof`, `C1010_State_reset_resume_preserves_generations`, `C1010_State_options_and_preview_are_strict`, `C1010_State_real_docker_transition`. Freeze exact methods/vectors/counts before Code. Update intentional old unknown-switch pins without weakening plain recycle. |
| **S3 later: caches and combination** | S2 deployment files and shared fixture; `scripts/verify-card0849-caches.ps1`; new `tests/Antiphon.Tests/Scripts/RunnerCacheRecycleScriptTests.cs`, `RunnerCacheRecycleDockerTests.cs`; affected `RemoteScriptContractTests.cs`, `RollingVolumeRecycleScriptTests.cs`, documentation tests; `docs/docker-stack.md`; this plan | Proposed `C1010_Cache_maintenance_requires_both_consumers_closed`, `C1010_Cache_seed_proves_roots_and_cleans_helpers`, `C1010_Cache_partial_removal_preserves_marker_authority`, `C1010_Combined_recycle_resumes_once`, `C1010_Cache_real_docker_maintenance`; retain C849/C912/C973 and CARD-1008 regressions. Freeze separately after S2 lands. |

Paths abbreviated in test cells resolve under `tests/Antiphon.Tests/`; fixture
filenames resolve under `scripts/fixtures/`. No `scripts/**` or `tests/**` scope
grant. Do not change Compose topology, Docker daemon settings, provider homes,
queue semantics, runtime session adoption or workspace deletion services.

## Verification design

This is not a completed TestDesign freeze. TestDesign must name each independent
guard, exact positive-control method/input/assertion, preserve the transfer IDs,
recount current source and validate the manifest using the real importer. Do not
infer executable coverage from method names or textual source pins alone.

S1 uses `PhoneHomeTestHost`, which maps the production session-runner endpoints,
and `FakeTimeProvider` injected into the real directory. No production stack or
provider process is launched. Proposed new methods, one TUnit result each:

| ID | `PhoneHomeRunnerReplacementAdmissionTests` method | Decisive observations |
|---|---|---|
| V-1 | `C1010_Registration_lease_uses_configured_deadline` | Lease 3600, cleared retirement, no connection: ready false at +90 and +3599, true exactly +3600; actual Register rejects/accepts the same boundary. |
| V-2 | `C1010_Disconnect_lease_uses_latest_disconnect` | Registration deadline already past, heartbeat then disconnect with nondefault lease: false until disconnect deadline, true at equality; real Register agrees. |
| V-3 | `C1010_Expired_attached_connection_remains_blocking` | Closed socket and expired heartbeat separately leave connectionAttached=true/ready=false until Directory.Disconnect; status polling cannot detach it. |
| V-4 | `C1010_Only_stamped_retirement_clear_authorizes` | Ordinary drain/clear and retirement stamp alone remain false; explicit stamped clear authorizes, subsequent non-retiring hold preserves authorization and rejects new work. |
| V-5 | `C1010_Same_store_registration_consumes_authorization` | Old-store reconnect consumes the one-shot grant, updates deadline/epoch, and a different store remains refused. |
| V-6 | `C1010_Replacement_registration_consumes_authorization` | Different store accepted once at readiness; subsequent replacement refused; old ticket invalid and boot conflict behavior preserved. |
| V-7 | `C1010_Status_reads_are_coherent_and_non_consuming` | Repeated HTTP reads retain store/epoch/grant/deadlines; snapshots across controlled Register/Disconnect/ApplyState transitions are each internally coherent, never a hybrid ready state. |
| V-8 | `C1010_Unknown_local_disabled_and_unbound_statuses_are_safe` | Unknown 404, local null, disabled/unbound ready=false; recreated directory never invents previous authority or binding. |
| V-9 | `C1010_Status_wire_is_additive_and_server_timed` | Real HTTP camelCase schema/deadlines, JSON types and nullable compatibility; old DTO bytes deserialize with null object. UTC boundary comes from fake server time, not wall time. |
| V-10 | `C1010_Ready_is_not_dispatch_or_a_reservation` | Ready during non-retiring hold still denies ResolveForNewWork; an intervening registration invalidates an earlier ready observation and real Register still refuses. |

R-1 retains full `RunnerCatalogueTests`, `PhoneHomeDirectoryTests` and
`PhoneHomeRunnerRetirementIdentityTests`: source/frozen sibling census is 20
expanded results (including 9 retirement identity results). R-2 runs the real
retirement-cycle test with an isolated database schema and fake time: one result,
two identities through existing state/retire services. Refresh counts after
CARD-0959 lands; count drift needs a documented census, not a lowered floor.

S2 must couple the actual PowerShell wait to status generated by the real S1
directory/test server, then to its real registration decision. A deterministic
test-only clock driver advances that server; canned `ready=true` fixtures cannot
discharge PC-121/122. Assert no recreate/start command before the predicate passes,
and the different new store/SHA after acceptance. Fake only external transport and
container boundaries; never add fields existing only in fixture JSON. No timed
90-second sleep test and no wall-clock multi-hour test is necessary.

### Transferred controls and real outcomes

All **31** moved G/PC pairs remain owned here: **13, 29, 118–144, 155, 156**.
Keep those identifiers as CARD-1008 lineage, without renumbering its remaining
controls. V-11 and RD-9/RD-10 also move. Default no-cold-seed behavior remains
independently covered by CARD-1008 V-1/RD-1; its strict shared-null PC-117 stays there.

| Transferred IDs | New slice/test obligation |
|---|---|
| 29 | S2/S3 wrapper, raw manifest and bridge option/phase/type rejection |
| 118–120 | S2 saved live proof, real retirement stamp, clear-after-removal ordering, attachment |
| 121–122 | S1 V-1/V-2 establish the production predicate; S2 `C1010_State_reset_waits_for_real_admission` detects fixed-default waits and ignored disconnect windows end to end |
| 123–124 | S2 different new store/requested SHA; StoreMismatch remains a stop |
| 13, 125–127 | S3 both consumers closed, complete foreign/bind scan and journal-bound absent-main authority |
| 128–130 | S3 retained helper digest, zero default cold-seed calls, marker invalidation only after completed removals |
| 131–139 | S3 exact labels/driver/options, canonical roots, uid/mode, hidden-entry emptiness, write probes, cleanup, boundary rechecks and no implicit source creation |
| 140–144 | S3 atomic marker, real verification, full-only compatibility, unchanged ordinary Cold, returning seed core |
| 155–156 | S3 no recursive locking and real bridge/host-case routing |

RD-10 retains three changed outcomes: successful new identity after proved
admission; attached/unexpired registration refusal; recent-disconnect lease refusal.
RD-9 retains four changed outcomes: successful cache-only maintenance; accepting or
referenced counterpart refusal; partial removal/marker/seed recovery; combined
state/cache success with identity and root validation. TestDesign must expand the
individual adversarial vectors and assertion labels; seven named outcomes are not
seven TUnit executions automatically and are not claimed executed here.

All real-Docker checks are **fixtures-only on an owned nested daemon**: require
`/.dockerenv` and daemon Name equal to this container's hostname, unique fixture
project/volume labels, random-loopback test HTTP, exact created-object ledger and
checked cleanup. Refuse host/sibling daemon, SSH to the production host, real
standing project names and production API/secret mounts. Relocate logical names
through the fixture's fixed unique map; do not relax production lane checks.
Keep Docker stop/remove/references/volume generations, Git and root permissions
real. Cleanup only recorded fixture objects; prove absence even after assertion
failure. Reuse CARD-1008's retained fixture cleanup pattern, not a blanket prune.
These tests cannot authorize or claim a live rollout.

New process-spawning tests use the assembly-local `ParallelLimiter<ProcessSpawnLimit>`;
do not co-schedule Pty/FakeClaude assemblies. Missing bash/jq/Git/Node/pwsh or nested
Docker is not green or an allowed skip. Generated TRX/JSON/logs remain gitignored.
Ordinary Review gives **regression-only verdicts**: report introduced regressions,
confirm suspected inherited failures at base, and do not block on unrelated cleanup
or style. Never relabel failed/missing required checkpoints as green. Post-land
SourceLanding Mutation is separately commissioned, method-scoped red/restore/green,
with external evidence and no commits from its snapshot; S1 does not discharge S2 PCs.

### Checkpoints

Proposed closed **S1-only** ordinary scope. All rows use the portable lane; CP-3
additionally needs the established isolated Postgres test fixture. No Windows
native dependency or Docker rollout is introduced by S1. S2/S3 have no Code
admission until their separate freezes add their Linux fixture rows and regression
rosters. Each row has its own isolated build and one literal filter.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1010-admission/` | portable-admission | `/*/*/PhoneHomeRunnerReplacementAdmissionTests/C1010_*` | V-1..V-10 | 10 proposed results, 0 failed/skipped | 10 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1010-directory/` | portable-directory-regression | `/*/*/(RunnerCatalogueTests*)\|(PhoneHomeDirectoryTests*)\|(PhoneHomeRunnerRetirementIdentityTests*)/*` | R-1 | 20 results, 0 failed/skipped; recount at freeze | 20 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1010-cycle/` | portable-retirement-cycle | `/*/*/PhoneHomeRunnerRetirementCycleTests/Retired_placeholder_supports_two_container_cycles_in_one_directory_lifetime` | R-2 | 1 result, 0 failed/skipped | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Estimated ordinary floor: 24 minutes including three builds, 31 TUnit executions;
allow four more minutes for isolated checkpoint-tool bootstrap/import. These are
estimates, not run results. TestDesign supplies a separately costed PC roster.
Run every build/test through the host build-slot gate; exit 4 means not run.
Use the checkpoint tool `run --plan <this-plan>` after the committed S1 slice,
then `wait` until terminal, never ending a task at exit 75. Bootstrap only through
`scripts/build-slot.ps1` with a distinct `bin-c1010-tool/` output. Keep forward
slashes, source frozen while runs execute, retain SHA-bound clean receipts, and
remove only owned alternate bin directories after all child processes finish.
Code/Review run `scripts/check-evidence-diff.ps1` over their full task ranges.

## Collision and dependency ledger

Board-scoped active reads around 00:38–00:40 UTC showed CARD-0959 Code
`bd02f8d9`, CARD-1011 Review `d535eeec` (Code `d422c5a9` blocked), CARD-1013
Windows Debug `9efba31e` (Code `2c35a27d` blocked), CARD-1012 Review `a2601af5`,
and CARD-1017 TestDesign `044f2398`. These are observations, not reservations.
Read local remote-tracking branch plans without merging/rebasing this branch.

| Owner / inspected plan or freeze | Boundary and ordering |
|---|---|
| CARD-1008 landed plan and implementation | Required base for S2/S3. Preserve its default recycle, temp absent/null guard, four-volume retirement, audit/resume tests and pending independent Mutation. No retesting claim is inherited from the land's commit title. |
| CARD-0959 active inert-observation freeze on `origin/feat/card-task-bd02f8d9` | Actual branch diff includes both `PhoneHomeRunnerDirectory.cs` and `PhoneHomeContracts.cs`; direct S1 collision. Let that owner land, then rebaseline S1 status construction and regression census. Its latest freeze removes CLI admission gates; the older card/checkout wording about version-floor refusals is superseded. Add no CLI/version gate here. |
| CARD-1011 plan on `origin/feat/card-task-d422c5a9` | Owns Grok qualification, provider docs/bundles and Windows gates. No S1 source overlap. Do not edit orchestrator bundles, pins, sign-in helpers or commission paid canaries here. Respect the relaxed inbox gate and actual pinned Review route. |
| CARD-1013 plan on `origin/feat/card-task-2c35a27d` | Owns coverage-reader LF digests, fixture attributes and native Windows qualification. Read/use current importer only; do not change coverage, `.gitattributes` or claim its Windows gaps passed. |
| CARD-1012 live card, Code `81922318`, Review `a2601af5` | Owns only CARD-0927 CP-2 minimum 100 to 163. Do not copy 163 into this plan's unrelated filters or amend that manifest. |
| CARD-1017 plan on `origin/feat/card-task-044f2398`; freeze in progress | Future S3 owns `PhoneHomeContracts.cs` and remote workspace operations. Serialize actual same-file work with S1/S2; preserve its Done-generation, process/publication custody and separate mirror outcomes. Its disposal policy does not waive the volume-level Git/task audit. |
| CARD-1020 frozen plan `d4203e109`, `2026-10-03-card-1020-test-owned-ptyhost-cleanup-plan.md` | Owns direct test-client teardown and Windows process witnesses; it diagnoses an existing 72-second test linger override. No S1 helper edits required. Do not change production detach/linger or sweep unrelated processes to clean test outputs. |
| CARD-1022 plan `13d7100fe`, `2026-10-03-card-1022-modern-conpty-only-plan.md` | Staged modern-default/deprecation and UnixPty capability work, then later refusal/removal releases. Do not add inbox qualification, alter delivery ceilings, or confuse capability observations with replacement admission. Serialize contracts/status edits if its actual footprint overlaps. |
| CARD-1025 host jq | Before first live recycle, operator/rollout owner supplies host `command -v jq` and version receipt from the host lane. Do not silently install packages or confuse the image's jq with host readiness. Missing jq must still refuse before Docker mutation. |
| Checkpoint census | `scripts/lib/checkpoint-usage.ps1` currently has literal **377**. S1 adds Application tests, not Checkpoints cases: preserve 377 and do not touch the census/tool implementation. Recount only if the responsible sibling actually changes that namespace. |

At Code admission re-read active footprints and landed sources, not just card
status or null scope fields. Actual same-file work defers; do not force-rebase,
reset or overwrite a sibling's changes. This Plan branch remains fast-forward-only.

## Activation and recovery order

1. Land S1 after its separate TestDesign/Code/ordinary regression Review. Activate
   the server from the canonical checkout using the owner runbook, then verify
   `/api/version` against source and actual `storeReplacementAdmission.schema=1`
   on the status route. Health alone is insufficient. No runner upgrade is needed
   for this server-owned observation; do not retire anything to demonstrate it live.
2. Land and qualify S2, later S3, with fixtures only. Preserve any sibling's
   runner-first ordering when their runner contracts change. No active work may
   share deployment scripts during integration. Complete CARD-1025 host jq
   qualification before the first default or opt-in recycle.
3. In a future sanctioned staged rollout, first upgrade/verify through the normal
   named phases and retire temp with all four private volumes once main accepts.
   For a subsequent explicit opt-in redeploy, obtain human authorization for the
   state loss/cache cold start and pair maintenance window. Select a new reviewed
   target SHA; preview exact targets and blockers. Never pass these flags to all.
4. While main is live, close routing/work/land obligations, drain main with no
   redirect, capture strict zeros/publication proof and execute explicit
   redeploy-old with the selected flags. Let the script own retirement, stop,
   deletion, wait, seed and restart. Never pre-stop main by hand. Do not run the
   maintenance controller inside the runner it will replace.
5. Require exact new/same store as selected, requested SHA, completed recovery,
   correct mounts/caches/tmp, copied receipt and final accepting state. A
   separately sanctioned canary uses transcript-confirmed complete UserPrompt and
   report; no extra model spend is authorized by this plan. Temp remains retired.
6. On failure preserve the journal and the closed state; resume only the same
   source/options/operation. State loss cannot be rolled back by an image swap.
   Unexpected identity, lost authorization, provider sign-in or recreated-volume
   ambiguity requires reviewed recovery. Human-only Reset/prune/secret/session/
   routing actions do not become fallback steps.

No build, test, mutation, live rollout, drain, retirement or volume operation was
executed in this Plan task. Static document checks are not an importer receipt.

--- next stage ---
next: test-design
handoff: Freeze S1 only: the server-owned storeReplacementAdmission contract, ten proposed tests and CP-1..3. Rebaseline after CARD-0959's overlapping directory/contracts work lands. Preserve all 31 transferred controls for later state/cache slices, fixtures-only nested Docker, CARD-1025 host jq and explicit human opt-in activation gates.
artifact: docs/superpowers/plans/2026-10-04-card-1010-runner-state-cache-optins-plan.md
