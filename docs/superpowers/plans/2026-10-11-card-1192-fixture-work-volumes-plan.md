# CARD-1192: disposable fixture working volumes

Date: 2026-10-11. Stage: Plan; TestDesign remains a separate stage.

## Problem and premise

The cache Fixture case still creates `/tmp/c849-fixture-$RUN` on the deployment
host and bind-mounts its working directories into containers. F-3, F-4 and F-6
leave files owned by root or uid 1654 in a tree created by the SSH user. CARD-1191
made normal EXIT cleanup work with sudo; CARD-1192 removes that ownership
dependency by putting disposable payloads in case-owned Docker volumes.

Premise confirmed at `77648b30c8cfbe436187e1bb1c4a2bf0ff061bc5`, also the fetched
`origin/master` during this inspection. CARD-1192's complete description and
history were read through `scripts/card.ps1`; its single revision records this
Plan dispatch. CARD-1189 and CARD-1191 are Done. The gap is **not already fixed**.
The newer CARD-1193, CARD-1194 and CARD-1195 protections are also in this tree.

The plan lives in `docs/superpowers/plans/`, the stage-contract destination and
existing repository convention; `docs/plans/` does not exist in this checkout.
This dispatch creates only this document and changes no production or test code.

## Ground truth

Line references below describe the inspected base, not future offsets.

| Card assumption or implied boundary | Code at the inspected base | Consequence |
|---|---|---|
| Fixture working state is on the host | `scripts/c590-remote.sh:3682`, `c849_fixture`, assigns `/tmp/c849-fixture-$RUN`; its trap removes it with guarded `sudo -n rm -rf` | Migrate storage, then remove this fixture-only host tree. Do not remove the sudo workaround before the last payload consumer migrates. |
| Cache volumes already have disposal | `c849_fixture_prepare` and the group functions append cache names to `.fixture-volumes`; `c849_fixture_cleanup:2785` accepts a run prefix plus a packages/scratch/npm suffix | Add explicit workspace identities and cleanup coverage. Do not widen this into a general volume prune or a permissive new suffix regexp. |
| F-3 is only an npm bind mount | `c849_fixture_seed:2890` also creates donor payloads; `c849_seed:2025` stages stopped-donor copies, verifies npm, imports and retains a manifest/recovery tree on the host | Moving `npm-work` alone is incomplete. The fixture invocation of the shared seed path needs a payload-storage boundary. Real Seed must keep its existing host backend. |
| F-4 just needs a different mount | Restore and lock helpers bind `$root`; `c849_fixture_wait_file`, `c849_fixture_lock_assert`, `touch`, and the generated scripts all address host files or `/fixture` | Move the feed, projects, probe scripts, ready/go/release files and their observations together. Preserve shared/private scratch discrimination. |
| F-5 and F-6 can retain their read-only binds | F-5 binds an empty pack mask and ordinary packages. F-6 binds generated server scripts/tarballs/projects, then reads installed `node_modules` on the host | Read-only payload binds also migrate; a separate empty volume supplies the exact pack mask. Read-only checkout script binds may remain. |
| F-7..F-9 are just summaries | F-7 creates Compose documents; F-8 constructs recovery payloads and modifies previews; F-9 modifies recovery bytes/modes/manifests and creates toxic probe files | Separate small host control documents from payload/probe data. Run recovery mutations and checks inside the work volume. Keep the full control census. |
| Earlier fixes must remain | CARD-1189 keeps zip/hash pwsh inside the admitted image and unlinks PC-08's self-link. CARD-1191 orders probe creation before ownership handoff, suppresses subshell success receipts and preserves `UnhandledExit` | Preserve these behaviors. Literal tests requiring host `sudo chown`/`sudo rm` must be replaced with equivalent volume/ordering assertions when those operations disappear. |
| Only CARD-1189/1191 regressions matter | CARD-1193 passes HOME/scratch to lock probes and safely exports five logs; CARD-1194 validates/scales measured budgets; CARD-1195 restores imported cache-root mode and uses app-uid retention probes | Include these in the migration and checkpoint roster; no blanket deletion of old pins. |
| Evidence can move with the workspace | `scripts/c590-real.ps1:507` recursively copies `CASE_DIR`; `verify-card0849-caches.ps1:138` demands a nine-line summary, 9 groups, 32 control families and 47 variants | Keep the receipt interface and host ownership. Never export the entire work volume or leave a recursive symlink in the copied directory. |
| Linux repo tests establish deployment acceptance | `docs/docker-stack.md` separates ordinary repo tests from the rollout's CP-3 Fixture gate | No live Fixture run during Plan, TestDesign, Code or ordinary Review. Rollout verification remains necessary. |

## Decisions

- **D-1 — Host controller, volume payloads.** Keep the admitted host-lane shell,
  maintenance lock, Docker/Compose calls, HTTP inventories and result publisher
  on the host. Store all fixture package data, seed stages/recovery trees,
  generated probe programs and mutable working files in named volumes. Small
  controller files (Compose documents, markers, previews, ledgers, filtered
  receipts and logs) may remain in a mode-0700 directory owned by the invoking
  host user. This follows the card's explicit small-evidence-directory allowance.
  Reject putting the whole controller in a Docker-socket container: that changes
  lane detection and host-path meaning, and needlessly moves operational authority.
- **D-2 — Two additional volumes per case.** Use `c849${RUN}-work` and
  `c849${RUN}-mask`. Mount the work volume as `/fixture`, with `race/`,
  `npm-work/`, `npm-offline/`, `donor/`, `apphost/`, `cache/` and `probes/`
  subdirectories. The mask volume stays empty and covers only the particular
  image-pack directory under test. Existing package/scratch/npm cache volumes
  remain separate. Every new volume mount uses `volume-nocopy`; read-only
  consumers also use `readonly`. Create work/mask with the local driver, no
  driver options, and labels `io.antiphon.owner=c849-fixture`,
  `io.antiphon.fixture-run=$RUN` and `io.antiphon.fixture-role=work|mask`.
  Helpers use the same owner/run labels and distinct writer/reader roles.
  Reject one volume per file and a new Docker volume-subpath
  dependency: whole-volume mounts with explicit paths suffice on the admitted image.
- **D-3 — No host access to volume internals.** Use named, case-owned helper
  containers from the existing admitted image. A writer mounts work read/write;
  a reader mounts it read-only. Helpers have no Docker socket, credentials,
  host working-directory mount or network. Initialize work as uid/gid 1654,
  mode 0700; use explicit uid for each operation. Root-only setup stays inside
  the helper. Reject reading `.Mountpoint` with sudo, chowning a host payload
  tree, or changing global Docker ownership. Existing production cache admission
  and disk observation logic is outside this storage change.
- **D-4 — Keep one seed/prune algorithm.** Add a narrow payload backend used by
  shared seed/recovery functions: host filesystem by default, work-volume
  operations only when the initialized Fixture context selects them. Do not
  replace the exercised seed/prune algorithms with fixture-only simulations.
  Do not add an environment variable or CLI switch that can redirect a real
  Seed/Prune into a test backend. Ordinary Seed, saved donors, cold seed, preview
  authority, cache labels, budgets and production volume names keep their contracts.
- **D-5 — Exact ownership and ordered cleanup.** Record intended names after
  proving absence, before acquisition; validate run/role labels before acting on
  a created workspace resource. An unknown lookup fails closed. Stop consumers,
  preserve allowed evidence, stop workspace helpers, then remove volumes by
  exact identity. Use the existing cleanup entry point, not `docker volume prune`.
  A workspace creation failure, mid-group failure and normal success all use
  the same finalizer. Do not reuse an occupied case namespace.
- **D-6 — Preserve evidence and the original failure.** Keep the CARD-1189
  unlink and CARD-1191 saved exit/subshell rules. Preserve CARD-1193's regular-file,
  real-directory and destination checks for log export. Result/summary schemas
  stay unchanged. Finalize before publishing success; a cleanup failure on an
  otherwise successful run is a refusal, while a failing run retains its first
  diagnosis and separately records exact cleanup residue in a small receipt.
  Do not let `WROTE=1` hide a failed finalization after an early success receipt.
- **D-7 — Keep existing runtime proofs.** F-1..F-9 and PC-01..PC-32 retain their
  meanings and the 47-variant census. Preserve CARD-1193..1195 as well as the
  cards named in the request. Do not lower timeouts, remove expected-red controls,
  weaken cache-root mode, or permit network access to the offline install.
- **D-8 — Separate TestDesign.** This is a medium-sized storage migration across
  shared seed/recovery seams, not an easy mount-string edit. The dispatch does
  not fold TestDesign into Plan. The checkpoint proposal below supplies concrete
  methods and slice boundaries; TestDesign must add the executable verification
  design and method-scoped mutation inventory before Code.

These are implementation choices within the card's authorization. No operator
product decision is needed to proceed to TestDesign. Do not infer permission to
run the live Fixture, remove earlier failed-run trees, Seed, deploy or restart.
If a later request expands this to migrating real retained Seed recovery storage,
that is a separate operator scope decision, not an implicit part of D-4.

## Intended implementation

### Workspace boundary

Keep helper functions in `scripts/c590-remote.sh` so the existing remote transport
still uploads one script. Introduce fixture workspace initialization, read/write
execution, exact-resource registration and finalization helpers. Pass arguments
as shell arguments/arrays; never construct an `eval` command from payload paths.
For filesystem-only functions, stream an explicit list of `declare -f` definitions
and invoke the selected function with positional arguments, as
`c849_compare_import` already does. Do not source the script's dispatch body
inside a helper. A validator that removes incomplete package versions needs the
writer; manifest comparison and evidence reads use the reader.

The work-volume root is private to this case. Pure payload functions may use
`/fixture/...`; host controller files use a distinct root. A helper result that
denotes a logical payload path must never be passed to host `test`, `find`, `cp`,
`stat`, `realpath`, shell redirection or a `type=bind` mount. Introduce a payload
root/accessor rather than overloading `SERVER2_ROOT` to mean both filesystems.
Host backend calls retain the existing paths and return/error behavior.

For seed transfers between a stopped donor and the volume helper, stream the
`docker cp ... -` archive into the destination container; Docker does not support
container-to-container `docker cp` directly. Check both ends under `pipefail`,
preserve dotfiles and executable bits, and validate the staged tree before import.
Never extract the package archive onto the host. Mount the entire work volume
for npm verification/import/manifest comparison, and pass the actual subtree as
an argument; do not rely on Docker volume subpaths or bind `/fixture` from the host.

Shared seams to inspect/change together are `c849_seed`, `c849_seed_failure`,
`c849_compare_import`, `c849_recovery_check`, and the recovery portions of
`c849_require_ready`, `c849_preview` and `c849_prune`. The host-only default must
retain the legacy and schema-3/cold marker distinctions. Keep the pure
`c849_validate_seed_tree`, `c849_manifest_*` and relative-path rules authoritative
on both backends. Saved-donor transport remains the existing host path and is
covered as a regression; this card adds no saved-donor volume mode.

### Group migration map

| Group | Volume state | Host control/evidence | Required adaptation |
|---|---|---|---|
| F-1/F-2 | Work/mask allocation in addition to existing cache volumes | Render-only Compose inputs/models, resource ledger, cache-root receipt, PC-08 self-link for its existing host-path validator | Keep PC-08 creation/probe/unlink together, with EXIT unlink as backup. It is a control artifact, not package state. |
| F-3 | `donor/`, `npm-work/`, `cache/stage-*`, `cache/recovery-*` | Ready marker and filtered seed/smoke/integrity receipts | Create/validate donor tree in the helper; stream donor copies; use the real seed algorithm with the volume backend; retain both subshell `write_result` overrides. |
| F-4 | `race/` projects, feed, empty source, zip/probe programs and ready/go/release markers | Five existing race/lock logs (plus already generated private-probe logs if retained) | All restore and lock containers share the same work volume and use `/fixture/race`. Create programs before uid handoff; poll/touch markers via helpers. Preserve the 30-second marker wait and shared/private lock witness. |
| F-5 | `apphost/packages/`; empty mask volume | Smoke receipts | Import from the work volume, restore cache root to 0700 after `cp -a`, and preserve an empty mask mount for each pack-negative control. Never populate mask from image content. |
| F-6 | `npm-offline/` package, tarball, hash/server programs, manifests, `node_modules` | Bounded npm command logs | Use named mounts for hash/server/install; inspect sentinel via reader; preserve SHA-512 base64 and network-none `npm ci`. |
| F-7 | Existing external caches and Compose private volumes | Small `shared.yml`/`negative.yml` kept until Compose teardown | Continue real recreate/down-v retention checks as uid 1654. No working payload bind is needed. |
| F-8 | Recovery payload/manifest and refill tarball in work volume | Marker, preview/volume records, consumer-state text, budget observations | Keep preview/authority and budget checks on the controller; route payload hash/mode/existence/copy operations to the backend. |
| F-9 | Recovery corruption backups, toxic probe input/output in work volume | Allowlisted summary and full group/control/variant roster; two inventories | Check mutations against the real recovery validator, restore bytes/modes between variants, and prove toxic content never reaches exported evidence. |

Final controller storage is a fresh, non-symlink, host-owned private subdirectory
of `CASE_DIR`, removed after its documents/logs have served cleanup/export. It
contains no npm cache, package archive, feed, node_modules, obj/bin, or recovery
payload. Success/failure may retain the deliberately exported receipts and logs.
The card's "nothing on the host" means no abandoned working payload tree; it does
not mean deleting the required evidence directory.

### Resource lifetime and failure semantics

Initialize the EXIT finalizer before acquiring the first new resource. Track the
workspace writer/reader as exact container names; use deterministic registered
names for any new temporary helper. Preserve cleanup of existing consumers,
including the masked-pack and seed-smoke helpers on their failure paths.

`c849_fixture_cleanup` must accept the exact current-run cache-name set generated
by prepare/seed/race/apphost/npm/contract-controls plus the two new workspace
names. A foreign run, a same-prefix unregistered suffix, a production cache name,
or a mislabeled pre-existing resource must not become removable via ledger input.
The work ledger must not be truncated later by `c849_fixture_prepare` after
workspace initialization has registered entries. Keep the maintenance lock on
its original host root, acquired before replacing the fixture controller root.

Normal completion explicitly finalizes before `write_result true`. The EXIT
path saves `$?` first, unlinks `.fixture-escape`, and invokes an idempotent
finalizer without allowing one failed cleanup command to skip later resources.
Compose teardown still has its control files available. Stop writers before the
last read/export, release helpers before work/mask volume removal, then remove
the host-only control directory. Preserve the primary nonzero status and
`UnhandledExit <code>` fallback. Report removal failures instead of claiming
resource-free completion; do not add a fleet sweeper or remove unrelated residue.

SIGKILL, host loss and an unavailable Docker daemon cannot guarantee immediate
cleanup. Exact run labels/ledgers make residue attributable for an explicit later
recovery, but this card does not authorize that recovery against old runs.
No agent session/input-wait behavior changes; the CARD-1083 parking checklist is
not applicable.

## Landable slices and collision scope

Execute these slices serially: they overlap the same shell and test class.
Each slice commits, runs its checkpoint group, and pushes before the next one.
The estimates are authoring budgets of 30-60 minutes each, excluding checkpoints.
An intermediate slice improves or prepares a specific boundary and remains
usable with the legacy cleanup for the groups not yet migrated. CARD-1192 is
complete only after S7, not after the first volume mount is introduced.

| Slice | Budget | Landable behavior and tests | Files touched |
|---|---:|---|---|
| S1 | 45 min | Introduce work/mask volumes, helper access, ownership registry and ordered/idempotent cleanup; move F-9 toxic probes first so the helpers have a real consumer. Keep legacy host payload cleanup. Cover namespace collisions, partial acquisition and exact cleanup scope. | `scripts/c590-remote.sh`; `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`; new `scripts/fixtures/c1192-workspace-contract.sh` |
| S2 | 60 min | Migrate F-4's feed/projects/programs and all synchronization paths as one unit. Preserve zip-in-image, script-before-handoff, HOME/scratch, independent private-scratch negative and safe log capture. | Same three files; adapt C1189/C1191/C1193 pins in the existing test class |
| S3 | 45 min | Migrate F-3 npm-work plus F-6 npm-offline and F-8 refill reads. Donor/cache payloads remain host-backed until S5. Preserve hash-in-image, offline miss and sentinel semantics. | Same three files; adapt C1066/C1189 npm harnesses |
| S4 | 45 min | Extract the shared payload boundary with the **host backend still selected for seed/recovery**. Keep the pure validators/manifests and real Seed order. Test local behavior plus volume-backend transfers/validation without enabling it for full fixture seed yet. | Same three files; `scripts/fixtures/c913-seed-contract.sh` only where its Docker/file-operation model needs the new seam |
| S5 | 60 min | Select the volume backend for fixture F-3 donor/stage/recovery. Update F-8/F-9 recovery setup/checks as needed in the same slice so no host consumer is given a volume path. Preserve seed rerun, failure publication and all schema-3 recovery controls. | Same four files; C913/C849 seed/marker/prune regression harness adaptations |
| S6 | 45 min | Move F-5 ordinary import and mask, and finish F-8/F-9 payload backups/refill/observations. Preserve retention probes, 0700 root restoration, budget validation and the full control roster. | Same four files; update C1194/C1195 recipient pins only as necessary |
| S7 | 35 min | Delete the fixture's `/tmp` work-tree creation and sudo cleanup after an exhaustive consumer audit; move remaining control documents to the host-owned control directory. Finalize before success, retain primary failure/evidence, and document the storage/rollout contract. | `scripts/c590-remote.sh`; `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`; `scripts/fixtures/c1192-workspace-contract.sh`; `docs/docker-stack.md` |

S5 must update the F-8/F-9 users of a recovery path at activation time, even though
S6 finishes their remaining independent payloads. Do not land a slice which
hands `/fixture/...` to a host command. If S5 exceeds its 60-minute budget, split
its preparatory adapters into S4 before activation; do not weaken the slice gate.

Collision scope for the orchestrator:
`scripts/c590-remote.sh,tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs,scripts/fixtures/c1192-workspace-contract.sh,scripts/fixtures/c913-seed-contract.sh,docs/docker-stack.md`.
The front door and `c590-real.ps1` are read-only references unless TestDesign
finds a concrete incompatibility requiring an amended plan. No changes are
planned in `AgentTaskReplyService`, `AgentSessionService`, `Antiphon.Checkpoints`,
`TaskParkPublicationService` or `BlockedTaskSyncRecovery`.

## Verification requirements for TestDesign

This section is the Plan's inspection and checkpoint proposal, **not** the final
`## Verification design`. TestDesign must add V/R mappings, guard inventory,
precise positive-control edits/assertions, and any justified roster corrections
before handing the plan to Code. Do not claim an unrun method or a text pin is
live Docker acceptance.

Use the existing `RemoteScriptContractTests`/`LinuxShell` style and a local
`c1192-workspace-contract.sh` fixture that extracts the actual functions. It must
not source dispatch. Model Docker at the process/filesystem boundary, deny any
unexpected real Docker/HTTP/sudo invocation, execute actual filesystem operations
on private test directories, and assert observations rather than fixture-authored
success strings. Shell children retain `ParallelLimiter<ProcessSpawnLimit>` and
the existing bounded wait. Receipt tests must assert exit status and content.

New guard requirements (the V identifiers below bind the proposed checkpoints):

- **V-1:** absent-only exact registration, no later ledger truncation, label/run
  validation, occupied/unknown lookup refusal, and cleanup after partial acquisition.
- **V-2:** cleanup removes consumers/helpers before exact owned volumes, tolerates
  repeated finalization, and refuses foreign, production and same-prefix junk names.
- **V-3:** F-4 mount/path agreement, program creation before ownership handoff,
  volume-backed readiness/go/release, shared exclusion and private-scratch expected red.
- **V-4:** F-3/F-6 npm and refill have no host payload bind/read; positive install
  verifies the real sentinel, empty cache still yields `NpmOfflineCacheMiss`.
- **V-5:** both payload backends preserve bytes/dotfiles/executable modes; copy
  failure, unsafe entry, manifest mismatch and missing recovery refuse publication.
  A non-Fixture caller cannot select the work backend.
- **V-6:** fixture seed/rerun uses the shared algorithm and volume recovery; F-8/F-9
  preserve corruption/staleness/busy/budget refusals and restoration between variants.
- **V-7:** F-5 mask is empty and targets exactly one pack; imported cache roots
  remain 1654:1654:700; F-7 app-uid retention and external/private separation remain.
- **V-8:** no fixture payload remains in a host work tree; only explicit readonly
  checkout-input binds remain. Export preserves the nine-line summary and complete
  census, excludes toxic/payload content, and retains safe diagnostics on failure.
- **V-9:** normal success awaits cleanup; cleanup failure refuses success; primary
  failures and seed-subshell isolation survive secondary export/cleanup failures.

Regression identifiers: R-1 CARD-1189 image tools/unlink; R-2 CARD-1191 ordered
creation and failure receipts; R-3 CARD-1193 lock environment/log safety;
R-4 CARD-1194 measured budget validation; R-5 CARD-1195 root/retention ownership;
R-6 C1066 nounset behavior; R-7 real seed/marker/prune algorithms and saved donors;
R-8 lane/evidence contracts. Where a historical method name describes a removed
host operation, rename it explicitly and retain its behavioral witness. The table
uses the proposed replacement names for the two C1191 host-operation pins.

Every new guard needs an identified one-line/one-branch regression that makes its
own assertion fail. Examples to refine in TestDesign: reintroduce one `type=bind`
payload mount; poll a host ready file; accept a foreign ledger name; drop volume
removal; export after deleting work; allow seed success to publish the outer
receipt; bypass manifest comparison. Mutation remains a separately commissioned
post-land stage. No live Fixture run is a substitute for method-scoped red/green,
and this Plan executes neither.

### Placement and execution

Read `GET /api/runner-defaults` and `GET /api/session-runners` again at dispatch.
Both were read during planning: defaults revision 2, no kind overrides, and an
eligible Linux runner were available. These observations are not capacity
reservations. Use the effective preference, omit `-Runner`, and request
`-Platform Linux` only for these Linux-shell checkpoint slices. Do not embed a
fleet host in dispatch instructions. A later portable docs-only slice can omit
`-Platform`; `-Platform Any` explicitly clears a previous platform pin.

All rows below are the **Linux repo-contract lane**, named in `Group`. They
do not reach production. No Windows-only witness is required for these source
changes. The trusted-desktop front door's eventual rollout invocation is an
operational gate, not a Windows repo-test witness and not an authoring checkpoint.

After TestDesign freezes the table, run one checkpoint-tool invocation per
committed slice group with `--after S<n>` and `--expected-source-sha <commit>`.
Launch any `dotnet run` driver through `scripts/build-slot.ps1`; the checkpoint
executor acquires slots for its own builds/test rows. Await completion, including
all exit-75 continuations. Use isolated forward-slash outputs and keep evidence
ignored. Do not reuse a build across different `After` values. No whole Unit lane,
full assembly run, ad hoc real Docker test or Fixture rollout is in this list.

### Checkpoints

Each row names one behavior or existing regression method, with wildcard tree
segments and a method suffix to include argument expansions. `Min=1` on a new
method means one TUnit result, not its internal fault matrix. TestDesign must
confirm any parameterization before Code. Existing expanded C1066 counts are
read from source. All rows require zero failed/skipped results. Estimates are
budgets, not measured results from this dispatch.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1192-s1/` | linux-registration | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare*` | V-1 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-2 | S1 | `CP-1` | linux-cleanup-scope | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources*` | V-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-3 | S1 | `CP-1` | linux-partial-acquisition | `/*/*/RemoteScriptContractTests/C1192_Partial_workspace_acquisition_is_cleaned*` | V-1, V-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-4 | S1 | `CP-1` | linux-toxic-probe | `/*/*/RemoteScriptContractTests/C1192_Toxic_probe_stays_in_work_volume*` | V-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1192-s2/` | linux-race-mounts | `/*/*/RemoteScriptContractTests/C1192_Race_programs_and_feed_share_the_work_volume*` | V-3 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-6 | S2 | `CP-5` | linux-race-barriers | `/*/*/RemoteScriptContractTests/C1192_Race_barriers_observe_volume_state*` | V-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-7 | S2 | `CP-5` | linux-lock-discrimination | `/*/*/RemoteScriptContractTests/C1192_Lock_probe_distinguishes_shared_and_private_scratch*` | V-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-8 | S2 | `CP-5` | linux-zip-image | `/*/*/RemoteScriptContractTests/C1189_Fixture_package_zip_does_not_call_host_pwsh*` | R-1 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-9 | S2 | `CP-5` | linux-probe-handoff | `/*/*/RemoteScriptContractTests/C1191_Lock_probe_is_created_before_uid_handoff*` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-10 | S2 | `CP-5` | linux-lock-environment | `/*/*/RemoteScriptContractTests/C1193_Lock_probe_passes_home_and_nuget_scratch*` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-11 | S3 | `tests/Antiphon.Tests -> bin-c1192-s3/` | linux-npm-volume | `/*/*/RemoteScriptContractTests/C1192_Npm_seed_install_and_refill_use_work_volume*` | V-4 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-12 | S3 | `CP-11` | linux-npm-recipient | `/*/*/RemoteScriptContractTests/C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed*` | V-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-13 | S3 | `CP-11` | linux-hash-image | `/*/*/RemoteScriptContractTests/C1189_Fixture_npm_integrity_does_not_call_host_pwsh*` | R-1 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-14 | S3 | `CP-11` | linux-npm-nounset | `/*/*/RemoteScriptContractTests/C1066_Fixture_npm_install_uses_argument_log_under_nounset*` | R-6 | 2 executed, 0 failed/skipped | 2 | 1 |
| CP-15 | S4 | `tests/Antiphon.Tests -> bin-c1192-s4/` | linux-backend-selection | `/*/*/RemoteScriptContractTests/C1192_Real_cache_cases_keep_the_host_payload_backend*` | V-5 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-16 | S4 | `CP-15` | linux-transfer | `/*/*/RemoteScriptContractTests/C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy*` | V-5 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-17 | S4 | `CP-15` | linux-seed-validation | `/*/*/RemoteScriptContractTests/C913_Seed_accepts_complete_ordinary_packages_without_framework_packs*` | R-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-18 | S4 | `CP-15` | linux-seed-order | `/*/*/RemoteScriptContractTests/C913_Full_marker_binds_verified_recovery_before_publication*` | R-7 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-19 | S4 | `CP-15` | linux-marker-versions | `/*/*/RemoteScriptContractTests/C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct*` | R-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-20 | S4 | `CP-15` | linux-saved-donor | `/*/*/RemoteScriptContractTests/C849_Saved_donor_archive_is_checked_and_imported_without_a_container*` | R-7 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-21 | S5 | `tests/Antiphon.Tests -> bin-c1192-s5/` | linux-fixture-seed | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume*` | V-5, V-6 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-22 | S5 | `CP-21` | linux-recovery | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest*` | V-6 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-23 | S5 | `CP-21` | linux-seed-failure | `/*/*/RemoteScriptContractTests/C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt*` | R-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-24 | S5 | `CP-21` | linux-tree-nounset | `/*/*/RemoteScriptContractTests/C1066_Fixture_tree_fault_reaches_validator_under_nounset*` | R-6 | 6 executed, 0 failed/skipped | 6 | 1 |
| CP-25 | S5 | `CP-21` | linux-shared-prune | `/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs*` | R-7, V-6 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-26 | S5 | `CP-21` | linux-seed-root | `/*/*/RemoteScriptContractTests/C1195_Seed_import_chmod_restores_volume_root*` | R-5 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-27 | S6 | `tests/Antiphon.Tests -> bin-c1192-s6/` | linux-mask-volume | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount*` | V-7 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-28 | S6 | `CP-27` | linux-apphost-root | `/*/*/RemoteScriptContractTests/C1195_Fixture_import_chmod_restores_volume_root*` | R-5, V-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-29 | S6 | `CP-27` | linux-retention | `/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content*` | R-5, V-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-30 | S6 | `CP-27` | linux-budget-observation | `/*/*/RemoteScriptContractTests/C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution*` | R-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-31 | S6 | `CP-27` | linux-budget-validation | `/*/*/RemoteScriptContractTests/C1194_Fixture_budget_scale_refuses_empty_observation*` | R-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-32 | S6 | `CP-27` | linux-warm-nounset | `/*/*/RemoteScriptContractTests/C1066_Fixture_warm_probe_uses_argument_log_under_nounset*` | R-6 | 3 executed, 0 failed/skipped | 3 | 1 |
| CP-33 | S7 | `tests/Antiphon.Tests -> bin-c1192-s7/` | linux-no-host-payload | `/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind*` | V-8 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-34 | S7 | `CP-33` | linux-finalization | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure*` | V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-35 | S7 | `CP-33` | linux-workspace-disposal | `/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo*` | R-2, V-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-36 | S7 | `CP-33` | linux-evidence-roster | `/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads*` | V-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-37 | S7 | `CP-33` | linux-escape-unlink | `/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit*` | R-1 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-38 | S7 | `CP-33` | linux-log-order | `/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal*` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-39 | S7 | `CP-33` | linux-log-paths | `/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files*` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-40 | S7 | `CP-33` | linux-lane | `/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python*` | R-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-41 | S7 | `CP-33` | linux-receipt-exclusion | `/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads*` | R-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-42 | S7 | `CP-33` | linux-receipt-consumer | `/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims*` | R-8 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-43 | S7 | `CP-33` | linux-fixture-inventory | `/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp*` | R-8 | 1 executed, 0 failed/skipped | 1 | 1 |

### Cost

Proposed ordinary checkpoint floor: **82 minutes** (sum of EstimatedMinutes),
including seven isolated builds. Authoring budgets total **335 minutes** over
seven separately landable slices. These are planning estimates, not a request
for one unbounded Code dispatch. TestDesign should preserve the per-slice shape
and refine counts/costs from its inspection; any scope expansion must amend the
table before execution. No broad suite run is authorized by this proposal.

## Risks and acceptance limits

- Shared seed/recovery changes are the main regression risk. Local-backend
  regression tests must remain meaningful, and fixture tests must execute the
  same validators and publication ordering rather than stubbing them successful.
- Logical volume paths and host paths must not mix. Audit every `$SERVER2_ROOT`,
  `$root`, `$stage`, `$recovery`, shell redirect and payload mount in F-3..F-9 plus
  the shared seams. A mount-only grep is insufficient.
- Moving a bind mount changes paths embedded in NuGet.Config, lock programs and
  npm server scripts. Validate the consumer's actual paths, not just mount type.
- A persistent helper can itself keep a volume busy. Cleanup-order and
  partial-acquisition tests must cover helper creation/start failure and failures
  while restoring, seeding, masking a pack and running the npm server.
- Existing literal guard tests will need surgical adaptation. Retain the old
  defect's behavioral assertion and demonstrate sensitivity to its new location.
- Offline Docker models establish control-flow/path contracts, not real UID,
  Docker copy/archive, volume or NuGet-lock behavior. The rollout gate supplies
  those observations. Do not declare live acceptance from the checkpoint table.

After ordinary Review and confirmed land, the existing rollout owner runs its
documented Fixture gate at the reviewed full SHA through
`scripts/verify-card0849-caches.ps1 -Case Fixture -Sha <landed-sha>` in the trusted
checkout, as part of rollout verification only. Require the unchanged summary:

`C849_FIXTURE groups=9 controls=32 expectedRed=32 variants=47 expectedRedVariants=47 inventories=2 failures=0 productionMutations=0`

Also retain exact work/mask/helper identities, inspect that those resources are
gone after completion, verify no new `/tmp/c849-fixture-*` payload root was
created, and check copied evidence ownership/content. An absent/malformed
receipt, cleanup residue, skipped control or changed production resource keeps
acceptance pending. Prior failed-run trees remain outside this card's authority.

## Handoff

Land this Plan, then dispatch TestDesign against it. Add `## Verification design`
with the required inspection, V/R/guard/PC inventory and finalized checkpoint
table; preserve the seven slice boundaries and the no-live-Fixture restriction.
Return to Plan if a storage boundary cannot be tested without duplicating the
production seed/prune algorithm. No production/test source was changed and no
build, test or live Fixture was run in this Plan dispatch.

Post-land Mutation was **NOT run**.
