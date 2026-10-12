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
  stay unchanged. Finalize before publishing success; a detected cleanup failure on an
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
Compose teardown still has its control files available. Workspace/helper
acquisition and consumer/helper/work/mask/cache/network removal require positive
confirmation; unknown outcomes retain named residue and refuse success. The
Compose teardown boundary checks only `docker compose down -v` exit status;
residual Compose-created volumes after exit zero are not detected by this
finalizer (inherited limitation, CARD-1207, outside S7). Stop writers before the
last read/export, release helpers before work/mask volume removal, then remove
the host-only control directory. Preserve the primary nonzero status and
`UnhandledExit <code>` fallback. Report detected removal failures; successful
finalization does not prove absence of Compose-created volumes. Do not add a
fleet sweeper or remove unrelated residue. Live acceptance W-1 remains pending.

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
- **V-9:** normal success awaits finalization; detected cleanup failure refuses success; primary
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

### Checkpoint proposal disposition

The appended Verification design finalizes this proposal. Its single
Checkpoints table is authoritative; all seven slices remain. The 43 method
selections are distinct. Ordinary cost remains 82 estimated minutes, with
post-land Mutation budgeted separately below.

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

## Verification design

TestDesign, 2026-10-11. Inspected source:
`479e129faa5194476b410bc29d6cbf0899c82286`. Fetched origin/master:
`77648b30c8cfbe436187e1bb1c4a2bf0ff061bc5`. Initial
`git merge-tree --write-tree HEAD origin/master` succeeded with tree
`566358d3eb01ae7e3ad423cbcd8d484500569af0`. The host-path premise still exists:
the fixture assigns its /tmp root and binds F-3..F-6 payloads; F-8/F-9 mutate
host recovery. CARD-1192 was read through card.ps1. ANTIPHON_TASK_TOKEN is
present, inherited by this bound task and never printed.

D-1..D-7 and S1..S7 remain unchanged. This amendment changes only the plan.
No build, test, real Docker command or live Fixture ran. Planned methods,
assertion labels and mutation statements are **binding required after Code**:
they are obligations, not passing evidence. Code binds them to implemented
bodies before handoff; unbound payloads remain missing coverage. Review judges
the pending PC inventory before land. Every PC must run after land;
**post-land Mutation was NOT run** here.

### Inspection

- Complete C1066/C1189/C1191/C1193/C1194/C1195 bodies in
  tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs, including
  C1066FixtureHarness, C1193LogCopySnippet, C1195AssertImportRestoresRoot and
  C1195QuotedCommand | nounset, handoff, image tools, logs, budgets, modes -> R-1..R-6.
- Same class: C913 methods and C913Run with independent .NET manifest oracle;
  C849_Saved_donor_archive_is_checked_and_imported_without_a_container,
  C849_Cache_receipts_exclude_credentials_and_payloads,
  Nested_lane_never_uses_sudo_or_python; LinuxShell/PrepareLinuxShellScript,
  Block/Commands/Order/Remote | host backend and recipient contracts -> R-7/R-8.
- Nearest fixture for new c1192-workspace-contract.sh:
  scripts/fixtures/c913-seed-contract.sh, its Docker mapper, run/fail/barrier
  helpers and tree/seed/ready/prune/inventory branches; c913-receipts.ps1
  including front-door shim | model design and independent local recipients
  -> V-1..V-9. Its fake UID and permissive unknown-command defaults are not
  native evidence; new workspace operations must fail closed.
- scripts/c590-remote.sh: fixture/F-1..F-9, cleanup/prepare, restore/lock/wait,
  npm/refill, budgets, roster/evidence, seed/failure/compare_import,
  recovery/ready, tree/manifest validators and prune authority/recovery bodies
  | touched storage and safety seams -> all V/R.
- c590-real.ps1 scp/result reader, verify-card0849-caches.ps1 Fixture
  summary/roster reader, docker-stack.md CP-3 gate, testing-and-build.md
  checkpoint/coverage/restoration and orchestration-loop.md stage contracts
  | destination evidence -> V-8/R-8; live transport is excluded ordinarily.

### Delivery inventory

No task/session/notification/queue delivery path changes. Real-queue
busy/already-eligible/crash-enqueue tests and UserPrompt transcripts are
inapplicable because these slices contain no queue producer. If Code adds one,
return to Plan for real producer-to-recipient queue proof including a matching
complete UserPrompt; request, insert, event, Sent flag or ack cannot prove it.

The changed cross-process file handoffs still require recipient evidence:

| Path / durable identity | Producer -> destination | Persistence boundary | Recovery / observable receipt |
|---|---|---|---|
| Workspace: RUN + exact name + owner/run/role | Host acquisition -> writer/reader | Intent ledger before create; work/mask volumes | CP-1..3 cut before/after each create/start and on lookup failure; independently read census, ledger and recipient files, not create acknowledgements. |
| F-4: RUN + project + generation | Writer -> restore/lock children -> reader | Volume ready/go/entered/release files | CP-5..7 cover delayed/absent ready, failed child, failed release, shared busy waiter and already-eligible private waiter; actual volume-file observation/join required. Modeled scheduling is not real NuGet exclusion. |
| F-3: RUN + donor ID + manifest digest | Donor -> streamed archive -> stage -> imported caches -> recovery | Volume bytes and manifest; host ready marker last | CP-16/18/21/22 fail both copy ends and each import/save/publication handoff; compare recipient bytes/dotfiles/modes independently and reopen with real readiness validator. No marker before completion; interrupted nonempty import stays held. |
| F-6/F-8: RUN + tarball digest + cache | Work writer -> npm fill -> offline install/refill | Tarball/lockfile, cache and node_modules | CP-11/12/14 observe reader's exact sentinel; missing/wrong sentinel after exit zero refuses; ENOTCACHED is distinct from setup/other failure. |
| F-5/F-7: RUN + mask/cache + Compose project | Writer -> apphost/retention recipient | Ordinary cache bytes, empty mask, external volumes | CP-27..29 observe exact mask and root mode, retention bytes and external/private separation; mask/smoke/teardown failures return to finalizer. |
| Evidence: RUN + source SHA + case | Quiesced writers -> safe exporter -> CASE_DIR -> front door | Allowlisted files and complete roster after finalization | CP-34/36/38/39/41/42 inspect destination bytes and refusal on export/cleanup faults, symlinks and failed unlink. Front-door shim does not establish scp receipt. |

**W-1**, the existing operator-run rollout witness in Risks and acceptance
limits, stays outside authoring checkpoints: run the approved Fixture command
at the reviewed landed full SHA during rollout only. Require actual installed
npm/package and retention recipients, shared/private NuGet lock observation,
9 groups/32 families/47 variants/2 inventories/zero production mutations,
copied local receipts matching host receipts, and independently observed
exact-resource absence after finalization, including Compose-created volumes
whose absence the finalizer does not check (CARD-1207). W-1 remains pending.
Verify host evidence ownership and no new host payload tree.
A success exit or complete producer summary without copied recipient evidence
leaves live acceptance pending. Observe cleanup on any naturally failed
rollout attempt; this does not authorize injecting live production failures.
SIGKILL/host loss may leave attributable residue, never guaranteed cleanup.

### Proves it works now

These are executable acceptance obligations for Code, not TestDesign run claims.
The recipes below bind exact methods, variants, cases, oracles and reachability.

- V-1: lifecycle registration | Linux shell/model | CP-1/3 | absent-only
  acquisition, preserved ledger and cleanup after partial acquisition.
- V-2: disposal | Linux shell/model | CP-2/3/35 | exact owned set removed,
  foreign canaries unchanged, consumer/helper ordering and idempotence.
- V-3: F-4 | shell/model plus command contracts | CP-5/6/7 | consistent
  volume paths, real local file barriers and shared/private discrimination.
- V-4: npm | shell/recipient model | CP-11/12 | volume payload paths,
  exact installed sentinel, offline miss and network-none install.
- V-5: backends | shared algorithm with filesystem recipients | CP-15/16/21 |
  host default, bytes/dotfiles/modes retained and failed handoffs held.
- V-6: recovery | actual validators | CP-21/22/25 | seed reuse, corruption
  refusal and restoration between variants, no delete before preflight.
- V-7: apphost/retention | model and filesystem snippet | CP-27/28/29 |
  exact empty mask, 0700 roots, app UID and nocopy contract.
- V-8: payload/evidence separation | model plus real receipt readers |
  CP-4/33/36 | no host payload accesses, complete census and no toxic export.
- V-9: final verdict | actual child-shell EXIT/finalization | CP-23/34 |
  finalize before success, original failure retained and no subshell acceptance.

### Guards the regression

- R-1: CARD-1189 | CP-8/13/37 | admitted-image zip/hash, normal and EXIT
  self-link unlink after PC-08 consumes it.
- R-2: CARD-1191 | CP-9/23/35 | program before uid handoff, both seed
  subshell overrides, UnhandledExit, owned cleanup without host-sudo payload work.
- R-3: CARD-1193 | CP-10/38/39 | HOME/scratch, five logs before source
  disposal, real source/destination guards and outside canaries preserved.
- R-4: CARD-1194 | CP-30/31 | parent observation diagnosis, invalid/oversized
  refusal and decimal measured 5/4 scaling.
- R-5: CARD-1195 | CP-26/28/29 | real 0755-copy widening then 0700 repair;
  exact cache-mode admission, retention uid 1654 and all three nocopy mounts.
- R-6: C1066 | CP-14/24/32 | 2/6/3 argument-expanded results reach intended
  diagnosis under nounset; setup/unbound-variable errors do not count.
- R-7: host seed/ready/prune and saved donor | CP-17..20/25 | existing
  case matrices and independent manifest oracle retained through new seam.
- R-8: lane/evidence | CP-40..43 | no nested sudo/python, toxic receipts
  rejected, schema/digest/smoke/roster consumer refusals, owned-only cleanup.

#### Recipes, variants, cases, oracles and reachability

All methods are in RemoteScriptContractTests. **M** = new local fixture
extracting actual production functions, never dispatch, with closed Docker/
HTTP/sudo shims and separate modeled host/volume/container namespaces.
Unexpected operations refuse. Execute embedded shell and streamed definitions
with positional arguments; never implement a second seed/prune algorithm.
**F** = actual extracted filesystem snippet. **T** = executable-body
Block/Commands/Order contract. **H** = existing c913 shell fixture.
**P** = c913 receipt fixture's front-door process shim, never live Fixture.

Classification: M/H establish model control flow and real local bytes, not
Docker/UID/network/NuGet behavior; F proves local filesystem semantics with
declared process/ownership substitutes; T is static only; P proves recipient
parser behavior, not SSH/scp. W-1 supplies native observations.

Each case must reach its named production boundary before fault injection;
record that witness and final predicate. Use fresh scratch and independent
expected bytes/digests/modes/census. A decoy host file must not make a wrong
logical-volume access pass. Require explicit captured child exit plus all
witnesses and no FAIL: LinuxShell itself returns stdout without asserting exit.
Keep ProcessSpawnLimit, Linux prerequisites and bounded child joins.
New C1192 methods are single TUnit executions with internal variant matrices.

| CP / exact method | Recipe | Case / variants | Oracle and reached boundary |
|---|---|---|---|
| CP-1: `C1192_Workspace_registration_is_absent_only_and_survives_prepare` | M | Absent/occupied/unknown; labels/driver/options; controller fresh/nonlink/private/host-owned; prepare after init | Independently read ledger/census; absence then intent then acquisition; helpers' argv/security and root mode. |
| CP-2: `C1192_Cleanup_releases_only_registered_current_run_resources` | M | Exact set, foreign run/production/same-prefix forged ledger, altered labels; repeat | Foreign canaries intact; acquired set gone; consumer/helper release precedes volumes; second finalization harmless. |
| CP-3: `C1192_Partial_workspace_acquisition_is_cleaned` | M | Before/after each work/mask and writer/reader create/start; delete failure | Cut reached; exact acquired set cleaned or residue named; later resources attempted; EXIT already armed. |
| CP-4: `C1192_Toxic_probe_stays_in_work_volume` | M | Toxic credential input and legal aggregate through volume helper | Actual receipt validator; volume files observed, toxic content absent recursively from CASE_DIR. |
| CP-5: `C1192_Race_programs_and_feed_share_the_work_volume` | M+T | Feed, three projects, generated zip/driver/lock programs and configs | Run writer then open each consumer's actual path; mount/argument agreement, no payload bind. |
| CP-6: `C1192_Race_barriers_observe_volume_state` | M | Delayed/already/never ready; host decoy; child/release failure | Execute wait/read/write helpers; volume ready/go/release observed and children joined; keep 150 x 0.2s contract with synthetic clock. |
| CP-7: `C1192_Lock_probe_distinguishes_shared_and_private_scratch` | M | Shared busy waiter; private eligible waiter; missing post-release entry | Actual lock assertion/orchestration; absence before shared release then presence; private NuGetLockNotShared expected. |
| CP-8: `C1189_Fixture_package_zip_does_not_call_host_pwsh` | T | Zip command and its error branch | Image pwsh -File with updated race paths; no host pwsh; retain failure diagnosis. |
| CP-9: `C1191_Lock_probe_is_created_before_uid_handoff` | M+T | Probe creation then uid handoff, reordered countercase | Complete program exists before ownership event/reader; replace obsolete host-chown literal with equivalent ordering. |
| CP-10: `C1193_Lock_probe_passes_home_and_nuget_scratch` | T | Lock holder/waiter invocation | HOME and NUGET_SCRATCH equal restore-container settings at real command. |
| CP-11: `C1192_Npm_seed_install_and_refill_use_work_volume` | M+T | Seed npm/offline/refill; hash/server generated files | Actual recipient subpaths, work mounts and network-none offline install; no host payload access. |
| CP-12: `C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed` | M | Exit zero with good/missing/wrong sentinel; ENOTCACHED; unrelated failure | Reader verifies exact bytes; intended miss is status 2/NpmOfflineCacheMiss, other failure does not satisfy it. |
| CP-13: `C1189_Fixture_npm_integrity_does_not_call_host_pwsh` | T | Hash invocation/program | Admitted-image -File, readonly volume, SHA512 base64 and tarball parameter. |
| CP-14: `C1066_Fixture_npm_install_uses_argument_log_under_nounset` | F | Existing success/miss Arguments under nounset | Correct argument log, code and diagnosis; adapt sentinel reader without dropping either case. |
| CP-15: `C1192_Real_cache_cases_keep_the_host_payload_backend` | M | Seed/saved/cold/Preview/Prune default and forged backend environment | Actual selector and entry points stay host-backed unless initialized Fixture context owns backend. |
| CP-16: `C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy` | M+F | Complete stream; source failure; sink failure; dotfiles/execute bits/spaced paths | Actual archive streams mapped at docker cp boundary; independent recipient bytes/mode; no import/marker after either failed end. |
| CP-17: `C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | H | Existing tree mode all cases | Real validators; ordinary-only accepted; malformed refused; incomplete pruning affects stage, donor intact. |
| CP-18: `C913_Full_marker_binds_verified_recovery_before_publication` | H | Existing seed mode all success/fault/crash barriers | Recipient bytes, independent .NET manifest oracle; marker absent before publication, completed marker reopens, interrupted import held. |
| CP-19: `C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | H | Existing ready mode legacy/schema3/cold and all corrupt inputs | Actual parser/validators; preserve every current case, cache churn acceptance and recovery refusals. |
| CP-20: `C849_Saved_donor_archive_is_checked_and_imported_without_a_container` | F | Existing saved tar/directory import, smoke failure | Real importer reached; host-owned recovery/payload, unsafe mode masked, failed smoke no marker. |
| CP-21: `C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | M | Actual fixture seed and rerun; unsafe/integrity/import/smoke/save/restart/reconnect/publication faults | Real shared seed; independently check donor/import/recovery bytes; rerun sentinel unchanged; outer receipt unpublished. |
| CP-22: `C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | M+H | F-8/F-9 corrupt package/npm/exec/manifest; missing/linked recovery; malformed marker; busy/stale/budget | Actual volume-backed validators and prune; exact refusals before delete; bytes/modes restored before next variant. |
| CP-23: `C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt` | M+T | Initial and rerun subshell acceptance; injected unexpected exit 9 | Actual child fixture EXIT emits false/UnhandledExit 9 after cleanup; neither seed subshell writes outer JSON. |
| CP-24: `C1066_Fixture_tree_fault_reaches_validator_under_nounset` | F | Existing six host/metadata/reference/symlink/hardlink/special Arguments | Actual volume tree fault reaches validator; intended diagnoses and original metadata preserved, no nounset error. |
| CP-25: `C913_Prune_validates_recovery_without_refilling_image_packs` | H | Existing prune matrix authority/recovery/budgets, all cases retained | Real local recipient sentinels; no delete before complete preflight; schema3 no framework refill, legacy refill survives. |
| CP-26: `C1195_Seed_import_chmod_restores_volume_root` | F+T | Seed import from 0755 tree | Execute real import snippet with chown substitute; first demonstrate widening then exact 700 and payload; UID only W-1. |
| CP-27: `C1192_Apphost_mask_is_an_empty_exact_volume_mount` | M+T | Each of three packs, empty mask, masked-smoke failure | One exact version/pack readonly/nocopy volume mount; empty recipient and failure cleanup. |
| CP-28: `C1195_Fixture_import_chmod_restores_volume_root` | F+T | Apphost import from 0755 tree | Distinct import snippet; actual 700 mode and bytes; observe admission still exactly 1654:1654:700. |
| CP-29: `C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content` | M+T | Recreate/down-v, external cache and private volumes, negative nonexternal | Actual retention orchestration; uid/nocopy argv; read retained sentinels; temp private absent, main present. |
| CP-30: `C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution` | F+T | Three measured role observations and failed observer | Parent-shell capture; original diagnosis before arithmetic, no accepted receipt after observer failure. |
| CP-31: `C1194_Fixture_budget_scale_refuses_empty_observation` | F | Empty/diagnosis/nonnumeric/negative/zero/100/010/08/15-digit/16-digit | Actual scale: exact refusals or 0/125/12/10; no arithmetic syntax error. Add missing numeric boundary cases inside same method. |
| CP-32: `C1066_Fixture_warm_probe_uses_argument_log_under_nounset` | F | Existing packages/scratch/home Arguments | Correct log and WarmCacheRefused diagnosis for each; retain setup/error distinction. |
| CP-33: `C1192_Fixture_has_no_host_payload_tree_or_payload_bind` | M+T | Whole migrated fixture; host decoys and allowed checkout inputs | Audit/intercept host filesystem operations as well as mount argv; no host payload root/read; explicit readonly checkout binds only. |
| CP-34: `C1192_Finalization_precedes_success_and_preserves_primary_failure` | M | Success, primary failure, export/delete failure, WROTE=1, repeat, unavailable Docker | Actual EXIT/finalizer: success last, primary status retained, exact residue, remaining cleanup attempted, Compose inputs available. |
| CP-35: `C1191_Fixture_exit_removes_owned_workspace_without_host_sudo` | M+T | Replacement of host-tree sudo cleanup pin | Actual exit uses workspace finalizer and host-user control-dir cleanup; no host sudo payload operation. |
| CP-36: `C1192_Evidence_export_preserves_fixture_roster_without_payloads` | M+P | 9/32/47/2 census, missing/duplicate group/family/variant, payload canary | Producer roster plus recipient parser; exact destination contents/SHA and recursive toxic exclusion. |
| CP-37: `C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit` | F+T | Self-link normal path and failing exit after PC-08 | Link consumed by probe then removed before next control; EXIT backup before recursive export. |
| CP-38: `C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal` | M+T | Five logs on success and failing group exit | Quiesce/export/helper/volume/control-dir order; destination bytes equal source before source deletion. |
| CP-39: `C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files` | F | Regular/missing/source-link; CASE_DIR link; destination link/directory/nonempty; failed unlink | Run actual exporter; outside bytes/status intact, regular destination; no copy after failed unlink or still-present destination. |
| CP-40: `Nested_lane_never_uses_sudo_or_python` | T | Existing nested-lane scan | No nested sudo/python in moved definitions; saved-import exclusion unchanged. |
| CP-41: `C849_Cache_receipts_exclude_credentials_and_payloads` | F | Aggregate/toxic key/malformed value | Actual receipt function retains aggregate, refuses invalid input without exporting bytes. |
| CP-42: `C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | P | All existing schema/digest/smoke/Fixture roster faults | Actual front-door rejection/privacy with shim receipts; no scp claim. |
| CP-43: `C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | H | Existing inventory/occupied/cleanup/control/apphost cases | Adapt removed host paths only: occupied resource refused, recipient bytes read, foreign preserved, setup error not expected red. |

### Guard inventory

Every row is an independently bypassable safety assertion with a distinct PC.
These plan PC numbers are separate from live Fixture PC-01..PC-32 families.
Shared detecting methods do not merge controls. No safety guard is intentionally
untested; native strength remains limited until W-1. Unchanged internal parser/
importer guards keep all existing R-7 cases; this migration commissions mutation
at the changed seam and the explicitly retained assertions below, not every
historical mutation in transitive dependencies. Any extra safety guard Code
introduces must gain a distinct row before its handoff.

| Guard | Plan ref / independent invariant | Positive control |
|---|---|---|
| G-1 | D-5: Occupied workspace never acquired | PC-1 |
| G-2 | D-5: Unknown acquisition lookup is not absence | PC-2 |
| G-3 | D-5: Intent precedes create | PC-3 |
| G-4 | D-5: Prepare retains workspace registrations | PC-4 |
| G-5 | D-2: Workspace requires local driver | PC-5 |
| G-6 | D-2: Workspace driver options empty | PC-6 |
| G-7 | D-5: Removal requires exact registered name | PC-7 |
| G-8 | D-5: Removal requires current-run name | PC-8 |
| G-9 | D-5: Removal requires owner label | PC-9 |
| G-10 | D-5: Removal requires run label | PC-10 |
| G-11 | D-5: Removal requires role label | PC-11 |
| G-12 | D-5: Unknown removal lookup refuses | PC-12 |
| G-13 | D-5: Production names excluded even in forged ledger | PC-13 |
| G-14 | D-5: Consumers release before volumes | PC-14 |
| G-15 | D-5: Workspace helpers release before volumes | PC-15 |
| G-16 | D-5: Repeated finalization harmless | PC-16 |
| G-17 | D-5: EXIT armed before first acquisition | PC-17 |
| G-18 | D-5: Create-to-start failure retains helper identity | PC-18 |
| G-19 | D-5: Cleanup continues after individual failure | PC-19 |
| G-20 | D-3: Toxic probe payload is volume-only | PC-20 |
| G-21 | D-3: Helpers have no network | PC-21 |
| G-22 | D-3: Helpers have no Docker socket | PC-22 |
| G-23 | D-3: Helpers receive no credentials | PC-23 |
| G-24 | D-3: Helpers have no host working mount | PC-24 |
| G-25 | D-3: Reader mount readonly | PC-25 |
| G-26 | D-2: Work mount nocopy | PC-26 |
| G-27 | D-3: Work initialized 0700 | PC-27 |
| G-28 | D-3: Work initialized uid/gid 1654 | PC-28 |
| G-29 | Workspace boundary: Helper payload arguments positional | PC-29 |
| G-30 | S2: Race mounts share work volume | PC-30 |
| G-31 | S2: Embedded race paths match recipients | PC-31 |
| G-32 | S2: Ready observation uses volume | PC-32 |
| G-33 | S2: Go reaches child volume | PC-33 |
| G-34 | S2: Release reaches holder volume | PC-34 |
| G-35 | D-7: Missing ready refuses within bound | PC-35 |
| G-36 | D-7: Shared waiter blocked before release | PC-36 |
| G-37 | D-7: Private control uses independent scratch | PC-37 |
| G-38 | D-7: Shared waiter enters after release | PC-38 |
| G-39 | R-1: Zip executes in admitted image | PC-39 |
| G-40 | R-2: Probe precedes uid handoff | PC-40 |
| G-41 | R-3: Lock helper gets HOME | PC-41 |
| G-42 | R-3: Lock helper gets NUGET_SCRATCH | PC-42 |
| G-43 | S3: Npm working mounts use volume | PC-43 |
| G-44 | S3: Refill tarball read through volume | PC-44 |
| G-45 | D-7: Offline install network none | PC-45 |
| G-46 | S3: Installed receipt requires sentinel existence | PC-46 |
| G-47 | S3: Installed receipt requires exact sentinel bytes | PC-47 |
| G-48 | D-7: Empty cache remains intended miss | PC-48 |
| G-49 | R-1: Hash runs in image via File | PC-49 |
| G-50 | R-1: Integrity SHA512 base64 | PC-50 |
| G-51 | R-6: Npm argument before dependent log | PC-51 |
| G-52 | D-4: Only initialized Fixture selects volume backend | PC-52 |
| G-53 | S4: Source stream failure propagates | PC-53 |
| G-54 | S4: Sink stream failure propagates | PC-54 |
| G-55 | S4: Transfer preserves dotfiles | PC-55 |
| G-56 | S4: Transfer preserves executable mode | PC-56 |
| G-57 | D-4: Incomplete pruning affects stage only | PC-57 |
| G-58 | D-4: Fixture uses staged tree validator | PC-58 |
| G-59 | D-4: Npm integrity gates import | PC-59 |
| G-60 | D-4: Imported bytes compared before ready | PC-60 |
| G-61 | D-4: Smoke gates publication | PC-61 |
| G-62 | D-4: Recovery save gates publication | PC-62 |
| G-63 | D-4: Donor restart gates publication | PC-63 |
| G-64 | D-4: Donor identity survives restart | PC-64 |
| G-65 | D-4: Reconnect gates publication | PC-65 |
| G-66 | D-4: Ready publishes after durable recovery | PC-66 |
| G-67 | D-4: Seed rerun preserves cache bytes | PC-67 |
| G-68 | S5: Recovery contained in owned payload root | PC-68 |
| G-69 | S5: Linked recovery refused | PC-69 |
| G-70 | S5: Recovery tree required | PC-70 |
| G-71 | S5: Manifest required and regular | PC-71 |
| G-72 | S5: Linked manifest refused | PC-72 |
| G-73 | S5: Manifest digest checked | PC-73 |
| G-74 | S5: Package bytes compared | PC-74 |
| G-75 | S5: Npm bytes compared | PC-75 |
| G-76 | S5: Executable bits compared | PC-76 |
| G-77 | S5: Control restores bytes between variants | PC-77 |
| G-78 | S5: Control restores modes between variants | PC-78 |
| G-79 | D-4: Legacy/full/cold distinct | PC-79 |
| G-80 | D-4: Saved donor stays host-backed | PC-80 |
| G-81 | R-2: Initial seed cannot publish outer receipt | PC-81 |
| G-82 | R-2: Rerun cannot publish outer receipt | PC-82 |
| G-83 | R-6: Tree fault variable initialized first | PC-83 |
| G-84 | D-4: Recovery checked before prune deletes | PC-84 |
| G-85 | D-4: Prune source authority checked | PC-85 |
| G-86 | D-4: Prune busy consumers refused | PC-86 |
| G-87 | D-4: All targets validate before delete | PC-87 |
| G-88 | D-4: Schema3 no framework refill | PC-88 |
| G-89 | D-7: Prune final budget gates success | PC-89 |
| G-90 | R-5: Seed import repairs 0700 root | PC-90 |
| G-91 | D-2: Mask stays empty via nocopy | PC-91 |
| G-92 | S6: Mask readonly | PC-92 |
| G-93 | S6: Mask targets one exact pack/version | PC-93 |
| G-94 | R-5: Apphost import repairs 0700 root | PC-94 |
| G-95 | R-5: Mode admission stays exact | PC-95 |
| G-96 | R-5: Retention app UID | PC-96 |
| G-97 | R-5: Retention all mounts nocopy | PC-97 |
| G-98 | S6: External cache survives down-v | PC-98 |
| G-99 | S6: Temp private gone, main retained | PC-99 |
| G-100 | R-4: Observation preserves parent diagnosis | PC-100 |
| G-101 | R-4: Invalid measurement refused | PC-101 |
| G-102 | R-4: Excessive digit count refused | PC-102 |
| G-103 | R-4: Leading zeros decimal | PC-103 |
| G-104 | R-4: Measured 5/4 scale | PC-104 |
| G-105 | R-6: Warm kind before log path | PC-105 |
| G-106 | S7: No host payload root | PC-106 |
| G-107 | S7: No host read of volume path | PC-107 |
| G-108 | S7: Only readonly checkout binds | PC-108 |
| G-109 | D-6: Success after finalization | PC-109 |
| G-110 | D-6: Cleanup failure refuses even WROTE=1 | PC-110 |
| G-111 | D-6: Primary failure survives cleanup | PC-111 |
| G-112 | D-6: Exact cleanup residue reported | PC-112 |
| G-113 | D-6: Compose inputs survive teardown | PC-113 |
| G-114 | S7: Disposal independent of host ownership | PC-114 |
| G-115 | D-6: Export excludes volume payload | PC-115 |
| G-116 | D-7: Producer group census exact | PC-116 |
| G-117 | D-7: Producer family census exact | PC-117 |
| G-118 | D-7: Producer variant census exact | PC-118 |
| G-119 | R-1: Normal self-link unlink | PC-119 |
| G-120 | R-1: EXIT self-link unlink | PC-120 |
| G-121 | R-3: Writers quiesce before export | PC-121 |
| G-122 | R-3: Export before source removal | PC-122 |
| G-123 | R-3: Source log not a link | PC-123 |
| G-124 | R-3: CASE_DIR real directory | PC-124 |
| G-125 | R-3: Unlink failure prevents copy | PC-125 |
| G-126 | R-3: Destination absent after unlink | PC-126 |
| G-127 | R-3: Copy does not descend into destination directory | PC-127 |
| G-128 | D-6: Receipt key allowlist | PC-128 |
| G-129 | D-6: Receipt value grammar | PC-129 |
| G-130 | D-7: Setup failure not expected red | PC-130 |
| G-131 | R-8: Nested lane no sudo | PC-131 |
| G-132 | R-8: Nested lane no python | PC-132 |
| G-133 | D-1: Controller directory must be fresh and nonlinked | PC-133 |
| G-134 | D-1: Controller directory private mode | PC-134 |
| G-135 | D-1: Controller directory owned by invoking host user | PC-135 |
| G-136 | D-3: Helpers use admitted image | PC-136 |
| G-137 | Resource lifetime: Maintenance lock stays on original host root | PC-137 |
| G-138 | R-3: All five diagnostic logs retained | PC-138 |
| G-139 | R-3: Source logs must be regular files | PC-139 |
| G-140 | S5: Pruning-capable transfer validation uses the writer; reader cannot prune | PC-140 |
| G-141 | S5: F-8 recovery copy validates source cache labels before copying | PC-141 |
| G-142 | S6: Ordinary payload stays on the work volume | PC-142 |
| G-143 | S6: Ordinary import reads work readonly | PC-143 |
| G-144 | S6: Mask mount validates current-run labels | PC-144 |
| G-145 | S6: Apphost requires current-run work labels | PC-145 |
| G-146 | S6: Apphost npm mount validates cache labels | PC-146 |
| G-147 | S6: Failed masked start retains cleanup identity | PC-147 |
| G-148 | S6: Consumers release before workspace helpers | PC-148 |

| G-149 | S7: Replaced controller is neither exported nor removed | PC-149 |
| G-150 | S7: An early outer receipt cannot bypass finalization | PC-150 |
| G-151 | S7: Foreign registered helpers remain visible as named residue | PC-151 |
| G-152 | S7: Legacy consumer cleanup requires its acquisition marker | PC-152 |

### Positive controls

Reachability requirements for the nontrivial controls:
- Scope/label tests keep every unrelated field valid. Controller-owner and helper-UID tests use explicit model facts; native UID enforcement is W-1. Supply same-prefix junk
  with correct labels for membership, and an exact registered identity with
  only one altered label for each label predicate. Use direct production
  ownership/name validators as well as the finalizer when an earlier registry
  check would otherwise mask the specific predicate. For redundant defense
  checks use T-class executable-body assertions and declare that limitation;
  never claim a behavioral refusal was sensitive when another guard caused it.
- CP-25 adds late recovery disappearance at image lookup after the second
  successful require_ready, immediately before the pre-delete recovery call.
  This reaches the destructive-boundary guard without an earlier ready refusal.
- CP-39 adds two controlled destination races: rm reports success while leaving
  a destination (assert copy was never invoked), and a directory/same-named
  outside symlink appears immediately before the real cp call (assert cp -T
  refuses and outside bytes remain intact). These separate unlink, absence and
  direct-file copy guards.
- CP-21 executes the production shared seed under the fixture wrapper. Inject
  failure independently after each handoff; do not stub validation or publication
  successful. Host-backed CP-18 retains its existing complete crash matrix.
- CP-1's security flags/mode/UID are command-contract assertions plus a model
  of initialization. Real ownership/enforcement remains W-1. Assert each
  independent forbidden capability separately.

Mutate production only, never the detecting test, shim, label or oracle. Each
edit must be syntactically valid/compiling. A fixture error, syntax error,
unexpected command, timeout or zero tests is not intended red. Every PC inherits
its detecting CP's recipe, variants, boundary witness and classification.
Expected-red cells name exact assertion labels Code must bind; on old tests
these are additive semantic labels and must retain existing predicates.

Post-land Mutation runs **break / exact-method red / restore / same-method
green for every row**, retaining per-PC patch, source identity, assertion and
TRX. Code runs ordinary V/R; Review judges this pending inventory before land.
Follow SourceLanding external-evidence/restoration custody. Most mutations touch
the same shell file, so do not batch them. Keep all outcomes including survivors.
If an earlier independent guard intercepts the mutation, it is not the planned
red: report the finding and bind a reachable case through Code; never silently
remove another guard to force a result. Assertion text without the actual
predicate is unbound coverage.

| PC | Break guard by compiling production defect | Detecting filter | Expected red assertion |
|---|---|---|---|
| PC-1 | G-1: Replace occupied-name refusal with no-op. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `occupied-name-no-create` fails after CP-1's boundary is reached. |
| PC-2 | G-2: Treat failed census/inspect as empty success. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `unknown-lookup-no-create` fails after CP-1's boundary is reached. |
| PC-3 | G-3: Move work intent append after create. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `intent-before-create` fails after CP-1's boundary is reached. |
| PC-4 | G-4: Restore prepare ledger truncation. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-ledger-retained` fails after CP-1's boundary is reached. |
| PC-5 | G-5: Accept non-local driver. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-local-driver` fails after CP-1's boundary is reached. |
| PC-6 | G-6: Remove Options emptiness predicate. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-empty-options` fails after CP-1's boundary is reached. |
| PC-7 | G-7: Replace exact membership with prefix match. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | `unregistered-name-preserved` fails after CP-2's boundary is reached. |
| PC-8 | G-8: Drop current-run name comparison. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | `foreign-run-preserved` fails after CP-2's boundary is reached. |
| PC-9 | G-9: Drop owner comparison only. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | `foreign-owner-preserved` fails after CP-2's boundary is reached. |
| PC-10 | G-10: Drop fixture-run label comparison only. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | `foreign-label-run-preserved` fails after CP-2's boundary is reached. |
| PC-11 | G-11: Drop role comparison only. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | `wrong-role-preserved` fails after CP-2's boundary is reached. |
| PC-12 | G-12: Treat inspect failure as owned. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | `unknown-resource-preserved` fails after CP-2's boundary is reached. |
| PC-13 | G-13: Bypass namespace guard for registered production name. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | `production-name-preserved` fails after CP-2's boundary is reached. |
| PC-14 | G-14: Move volume removal before consumer removal. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | `consumers-before-volumes` fails after CP-2's boundary is reached. |
| PC-15 | G-15: Omit writer/reader removal. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | `helpers-before-volumes` fails after CP-2's boundary is reached. |
| PC-16 | G-16: Refuse second finalization on confirmed absence. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | `finalize-twice-clean` fails after CP-2's boundary is reached. |
| PC-17 | G-17: Install EXIT after first volume create. | `/*/*/RemoteScriptContractTests/C1192_Partial_workspace_acquisition_is_cleaned` | `first-create-failure-cleaned` fails after CP-3's boundary is reached. |
| PC-18 | G-18: Register writer only after successful start. | `/*/*/RemoteScriptContractTests/C1192_Partial_workspace_acquisition_is_cleaned` | `helper-start-failure-cleaned` fails after CP-3's boundary is reached. |
| PC-19 | G-19: Return after first failed removal. | `/*/*/RemoteScriptContractTests/C1192_Partial_workspace_acquisition_is_cleaned` | `later-resources-attempted` fails after CP-3's boundary is reached. |
| PC-20 | G-20: Redirect toxic input to controller root. | `/*/*/RemoteScriptContractTests/C1192_Toxic_probe_stays_in_work_volume` | `toxic-input-volume-only` fails after CP-4's boundary is reached. |
| PC-21 | G-21: Replace helper network none with bridge. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-network-none` fails after CP-1's boundary is reached. |
| PC-22 | G-22: Add socket bind to helper argv. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-no-socket` fails after CP-1's boundary is reached. |
| PC-23 | G-23: Add credential-file argument to helper argv. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-no-credentials` fails after CP-1's boundary is reached. |
| PC-24 | G-24: Add controller-directory bind. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-no-host-work-bind` fails after CP-1's boundary is reached. |
| PC-25 | G-25: Remove reader readonly. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-reader-readonly` fails after CP-1's boundary is reached. |
| PC-26 | G-26: Remove writer volume-nocopy. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-nocopy` fails after CP-1's boundary is reached. |
| PC-27 | G-27: Initialize mode 0755. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-mode-0700` fails after CP-1's boundary is reached. |
| PC-28 | G-28: Initialize owner 0:0. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-owner-1654` fails after CP-1's boundary is reached. |
| PC-29 | G-29: Join arguments as an unquoted command string. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-positional-arguments` fails after CP-1's boundary is reached. |
| PC-30 | G-30: Restore one payload bind. | `/*/*/RemoteScriptContractTests/C1192_Race_programs_and_feed_share_the_work_volume` | `race-work-volume-mount` fails after CP-5's boundary is reached. |
| PC-31 | G-31: Leave feed path at old /fixture/feed. | `/*/*/RemoteScriptContractTests/C1192_Race_programs_and_feed_share_the_work_volume` | `race-feed-recipient` fails after CP-5's boundary is reached. |
| PC-32 | G-32: Use host test -f for ready. | `/*/*/RemoteScriptContractTests/C1192_Race_barriers_observe_volume_state` | `volume-ready-not-host-decoy` fails after CP-6's boundary is reached. |
| PC-33 | G-33: Touch go on host controller. | `/*/*/RemoteScriptContractTests/C1192_Race_barriers_observe_volume_state` | `volume-go-observed` fails after CP-6's boundary is reached. |
| PC-34 | G-34: Touch release on host controller. | `/*/*/RemoteScriptContractTests/C1192_Race_barriers_observe_volume_state` | `volume-release-observed` fails after CP-6's boundary is reached. |
| PC-35 | G-35: Return success at end of poll loop. | `/*/*/RemoteScriptContractTests/C1192_Race_barriers_observe_volume_state` | `missing-ready-refused` fails after CP-6's boundary is reached. |
| PC-36 | G-36: Remove shared pre-release assertion. | `/*/*/RemoteScriptContractTests/C1192_Lock_probe_distinguishes_shared_and_private_scratch` | `shared-waiter-blocked` fails after CP-7's boundary is reached. |
| PC-37 | G-37: Pass shared scratch to private waiter. | `/*/*/RemoteScriptContractTests/C1192_Lock_probe_distinguishes_shared_and_private_scratch` | `private-waiter-already-entered` fails after CP-7's boundary is reached. |
| PC-38 | G-38: Omit post-release entered-file check. | `/*/*/RemoteScriptContractTests/C1192_Lock_probe_distinguishes_shared_and_private_scratch` | `shared-waiter-after-release` fails after CP-7's boundary is reached. |
| PC-39 | G-39: Replace docker zip call with host pwsh. | `/*/*/RemoteScriptContractTests/C1189_Fixture_package_zip_does_not_call_host_pwsh` | `zip-admitted-image` fails after CP-8's boundary is reached. |
| PC-40 | G-40: Move lock-probe creation after handoff. | `/*/*/RemoteScriptContractTests/C1191_Lock_probe_is_created_before_uid_handoff` | `probe-before-handoff` fails after CP-9's boundary is reached. |
| PC-41 | G-41: Remove HOME argument. | `/*/*/RemoteScriptContractTests/C1193_Lock_probe_passes_home_and_nuget_scratch` | `lock-home` fails after CP-10's boundary is reached. |
| PC-42 | G-42: Remove scratch environment argument. | `/*/*/RemoteScriptContractTests/C1193_Lock_probe_passes_home_and_nuget_scratch` | `lock-scratch-env` fails after CP-10's boundary is reached. |
| PC-43 | G-43: Restore npm-work bind at cache add. | `/*/*/RemoteScriptContractTests/C1192_Npm_seed_install_and_refill_use_work_volume` | `npm-work-volume` fails after CP-11's boundary is reached. |
| PC-44 | G-44: Restore refill host bind. | `/*/*/RemoteScriptContractTests/C1192_Npm_seed_install_and_refill_use_work_volume` | `refill-volume-source` fails after CP-11's boundary is reached. |
| PC-45 | G-45: Use fixture network for install. | `/*/*/RemoteScriptContractTests/C1192_Npm_seed_install_and_refill_use_work_volume` | `offline-network-none` fails after CP-11's boundary is reached. |
| PC-46 | G-46: Skip missing-sentinel refusal. | `/*/*/RemoteScriptContractTests/C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed` | `missing-sentinel-refused` fails after CP-12's boundary is reached. |
| PC-47 | G-47: Accept any nonempty sentinel. | `/*/*/RemoteScriptContractTests/C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed` | `wrong-sentinel-refused` fails after CP-12's boundary is reached. |
| PC-48 | G-48: Return success on ENOTCACHED. | `/*/*/RemoteScriptContractTests/C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed` | `offline-miss-refused` fails after CP-12's boundary is reached. |
| PC-49 | G-49: Use host pwsh -Command. | `/*/*/RemoteScriptContractTests/C1189_Fixture_npm_integrity_does_not_call_host_pwsh` | `hash-admitted-image-file` fails after CP-13's boundary is reached. |
| PC-50 | G-50: Replace SHA512 with SHA256 in program. | `/*/*/RemoteScriptContractTests/C1189_Fixture_npm_integrity_does_not_call_host_pwsh` | `hash-sha512-base64` fails after CP-13's boundary is reached. |
| PC-51 | G-51: Recombine project/output local declaration. | `/*/*/RemoteScriptContractTests/C1066_Fixture_npm_install_uses_argument_log_under_nounset` | `npm-argument-log` fails after CP-14's boundary is reached. |
| PC-52 | G-52: Select from environment without context check. | `/*/*/RemoteScriptContractTests/C1192_Real_cache_cases_keep_the_host_payload_backend` | `real-cases-host-backend` fails after CP-15's boundary is reached. |
| PC-53 | G-53: Disable pipefail around copy pipeline. | `/*/*/RemoteScriptContractTests/C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy` | `source-copy-failure-refused` fails after CP-16's boundary is reached. |
| PC-54 | G-54: Ignore destination copy exit. | `/*/*/RemoteScriptContractTests/C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy` | `sink-copy-failure-refused` fails after CP-16's boundary is reached. |
| PC-55 | G-55: Archive glob instead of dot directory. | `/*/*/RemoteScriptContractTests/C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy` | `copy-dotfile-retained` fails after CP-16's boundary is reached. |
| PC-56 | G-56: Clear executable bits after transfer. | `/*/*/RemoteScriptContractTests/C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy` | `copy-executable-retained` fails after CP-16's boundary is reached. |
| PC-57 | G-57: Pass original donor to validator. | `/*/*/RemoteScriptContractTests/C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `incomplete-stage-only` fails after CP-17's boundary is reached. |
| PC-58 | G-58: Skip validation before npm/import. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `fixture-unsafe-tree-refused` fails after CP-21's boundary is reached. |
| PC-59 | G-59: Ignore npm cache verify failure. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `fixture-integrity-before-import` fails after CP-21's boundary is reached. |
| PC-60 | G-60: Skip compare_import. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `fixture-import-corruption-no-marker` fails after CP-21's boundary is reached. |
| PC-61 | G-61: Continue after smoke failure. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `fixture-smoke-no-marker` fails after CP-21's boundary is reached. |
| PC-62 | G-62: Ignore recovery move failure. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `fixture-recovery-save-no-marker` fails after CP-21's boundary is reached. |
| PC-63 | G-63: Ignore restart failure. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `fixture-restart-no-marker` fails after CP-21's boundary is reached. |
| PC-64 | G-64: Drop post-restart ID comparison. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `fixture-donor-identity-no-marker` fails after CP-21's boundary is reached. |
| PC-65 | G-65: Skip final reconnect check. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `fixture-reconnect-no-marker` fails after CP-21's boundary is reached. |
| PC-66 | G-66: Move publication before recovery save. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `fixture-publication-last` fails after CP-21's boundary is reached. |
| PC-67 | G-67: Bypass reuse branch and reimport. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `fixture-rerun-sentinel-retained` fails after CP-21's boundary is reached. |
| PC-68 | G-68: Drop parent containment predicate. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `recovery-escape-refused` fails after CP-22's boundary is reached. |
| PC-69 | G-69: Remove recovery nonlink/canonical check. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `recovery-link-refused` fails after CP-22's boundary is reached. |
| PC-70 | G-70: Allow missing tree to succeed. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `recovery-tree-required` fails after CP-22's boundary is reached. |
| PC-71 | G-71: Skip regular-manifest check. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `recovery-manifest-required` fails after CP-22's boundary is reached. |
| PC-72 | G-72: Drop manifest nonlink check. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `recovery-manifest-link-refused` fails after CP-22's boundary is reached. |
| PC-73 | G-73: Skip expected digest comparison. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `recovery-manifest-digest-bound` fails after CP-22's boundary is reached. |
| PC-74 | G-74: Exclude packages in comparison. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `recovery-package-bytes-bound` fails after CP-22's boundary is reached. |
| PC-75 | G-75: Exclude npm in comparison. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `recovery-npm-bytes-bound` fails after CP-22's boundary is reached. |
| PC-76 | G-76: Ignore execute bits in comparison. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `recovery-exec-bound` fails after CP-22's boundary is reached. |
| PC-77 | G-77: Omit byte restoration. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `control-bytes-restored` fails after CP-22's boundary is reached. |
| PC-78 | G-78: Restore bytes without original mode. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `control-mode-restored` fails after CP-22's boundary is reached. |
| PC-79 | G-79: Allow legacy digest in schema3 marker. | `/*/*/RemoteScriptContractTests/C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `mixed-marker-refused` fails after CP-19's boundary is reached. |
| PC-80 | G-80: Select work backend for saved donor. | `/*/*/RemoteScriptContractTests/C849_Saved_donor_archive_is_checked_and_imported_without_a_container` | `recovery-host-owned` fails after CP-20's boundary is reached. |
| PC-81 | G-81: Remove first write_result override. | `/*/*/RemoteScriptContractTests/C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt` | `initial-seed-no-outer-receipt` fails after CP-23's boundary is reached. |
| PC-82 | G-82: Remove second write_result override. | `/*/*/RemoteScriptContractTests/C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt` | `rerun-seed-no-outer-receipt` fails after CP-23's boundary is reached. |
| PC-83 | G-83: Recombine fault/tree local declaration. | `/*/*/RemoteScriptContractTests/C1066_Fixture_tree_fault_reaches_validator_under_nounset` | `tree-argument-path` fails after CP-24's boundary is reached. |
| PC-84 | G-84: Omit prune recovery validation call at destructive boundary. | `/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs` | `missing-recovery-no-delete` fails after CP-25's boundary is reached. |
| PC-85 | G-85: Drop source-sha comparison. | `/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs` | `stale-source-no-delete` fails after CP-25's boundary is reached. |
| PC-86 | G-86: Bypass prune_idle invocation. | `/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs` | `maintenance-busy-no-delete` fails after CP-25's boundary is reached. |
| PC-87 | G-87: Move target validation into destructive loop. | `/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs` | `second-target-invalid-no-delete` fails after CP-25's boundary is reached. |
| PC-88 | G-88: Run legacy refill for schema3. | `/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs` | `schema3-no-framework-copy` fails after CP-25's boundary is reached. |
| PC-89 | G-89: Skip final budget gate. | `/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs` | `over-budget-no-success` fails after CP-25's boundary is reached. |
| PC-90 | G-90: Delete final seed-import chmod 0700. | `/*/*/RemoteScriptContractTests/C1195_Seed_import_chmod_restores_volume_root` | `seed-root-restored-700` fails after CP-26's boundary is reached. |
| PC-91 | G-91: Remove mask volume-nocopy. | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | `mask-empty-nocopy` fails after CP-27's boundary is reached. |
| PC-92 | G-92: Remove mask readonly. | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | `mask-readonly` fails after CP-27's boundary is reached. |
| PC-93 | G-93: Broaden mount to all packs. | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | `mask-exact-pack` fails after CP-27's boundary is reached. |
| PC-94 | G-94: Delete apphost final chmod 0700. | `/*/*/RemoteScriptContractTests/C1195_Fixture_import_chmod_restores_volume_root` | `apphost-root-restored-700` fails after CP-28's boundary is reached. |
| PC-95 | G-95: Accept 0755 alongside 0700. | `/*/*/RemoteScriptContractTests/C1195_Fixture_import_chmod_restores_volume_root` | `cache-mode-exact` fails after CP-28's boundary is reached. |
| PC-96 | G-96: Remove Compose user 1654:1654. | `/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content` | `retention-app-uid` fails after CP-29's boundary is reached. |
| PC-97 | G-97: Remove scratch nocopy. | `/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content` | `retention-nocopy-three` fails after CP-29's boundary is reached. |
| PC-98 | G-98: Render packages nonexternal. | `/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content` | `retention-external-survives` fails after CP-29's boundary is reached. |
| PC-99 | G-99: Skip temp down-v. | `/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content` | `retention-private-separation` fails after CP-29's boundary is reached. |
| PC-100 | G-100: Use substitution pipeline instead of parent capture. | `/*/*/RemoteScriptContractTests/C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution` | `budget-parent-diagnosis` fails after CP-30's boundary is reached. |
| PC-101 | G-101: Remove numeric predicate. | `/*/*/RemoteScriptContractTests/C1194_Fixture_budget_scale_refuses_empty_observation` | `budget-invalid-refused` fails after CP-31's boundary is reached. |
| PC-102 | G-102: Remove 15-digit bound. | `/*/*/RemoteScriptContractTests/C1194_Fixture_budget_scale_refuses_empty_observation` | `budget-digits-refused` fails after CP-31's boundary is reached. |
| PC-103 | G-103: Remove 10# prefix. | `/*/*/RemoteScriptContractTests/C1194_Fixture_budget_scale_refuses_empty_observation` | `budget-leading-zero-decimal` fails after CP-31's boundary is reached. |
| PC-104 | G-104: Use fallback instead of measured scaling. | `/*/*/RemoteScriptContractTests/C1194_Fixture_budget_scale_refuses_empty_observation` | `budget-measured-scale` fails after CP-31's boundary is reached. |
| PC-105 | G-105: Recombine kind/output declaration. | `/*/*/RemoteScriptContractTests/C1066_Fixture_warm_probe_uses_argument_log_under_nounset` | `warm-argument-log` fails after CP-32's boundary is reached. |
| PC-106 | G-106: Restore old /tmp initialization. | `/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind` | `no-host-payload-root` fails after CP-33's boundary is reached. |
| PC-107 | G-107: Use host stat for volume recovery. | `/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind` | `no-host-payload-read` fails after CP-33's boundary is reached. |
| PC-108 | G-108: Remove readonly on checkout-script bind. | `/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind` | `checkout-bind-readonly` fails after CP-33's boundary is reached. |
| PC-109 | G-109: Publish true before finalizer. | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | `success-after-finalize` fails after CP-34's boundary is reached. |
| PC-110 | G-110: Ignore finalizer error when WROTE set. | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | `cleanup-failure-refuses-success` fails after CP-34's boundary is reached. |
| PC-111 | G-111: Overwrite saved ec with cleanup result. | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | `primary-failure-retained` fails after CP-34's boundary is reached. |
| PC-112 | G-112: Omit failed work identity from residue. | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | `cleanup-residue-exact` fails after CP-34's boundary is reached. |
| PC-113 | G-113: Remove controller before compose down. | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | `compose-input-until-teardown` fails after CP-34's boundary is reached. |
| PC-114 | G-114: Replace work volume rm with host sudo rm. | `/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo` | `workspace-disposed-without-host-sudo` fails after CP-35's boundary is reached. |
| PC-115 | G-115: Export whole work instead of allowlist. | `/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads` | `evidence-no-payload` fails after CP-36's boundary is reached. |
| PC-116 | G-116: Skip group comparison in roster. | `/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads` | `producer-group-census` fails after CP-36's boundary is reached. |
| PC-117 | G-117: Skip family comparison in roster. | `/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads` | `producer-family-census` fails after CP-36's boundary is reached. |
| PC-118 | G-118: Skip variant comparison in roster. | `/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads` | `producer-variant-census` fails after CP-36's boundary is reached. |
| PC-119 | G-119: Delete normal unlink after PC-08. | `/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit` | `escape-unlinked-normal` fails after CP-37's boundary is reached. |
| PC-120 | G-120: Delete EXIT fallback unlink. | `/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit` | `escape-unlinked-exit` fails after CP-37's boundary is reached. |
| PC-121 | G-121: Export before consumer stop/join. | `/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal` | `writers-before-export` fails after CP-38's boundary is reached. |
| PC-122 | G-122: Delete source before export. | `/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal` | `export-before-source-removal` fails after CP-38's boundary is reached. |
| PC-123 | G-123: Drop source nonlink check. | `/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files` | `symlink-source-not-copied` fails after CP-39's boundary is reached. |
| PC-124 | G-124: Drop CASE_DIR nonlink check. | `/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files` | `case-link-no-write` fails after CP-39's boundary is reached. |
| PC-125 | G-125: Ignore destination rm failure. | `/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files` | `failed-unlink-no-copy` fails after CP-39's boundary is reached. |
| PC-126 | G-126: Drop post-unlink absent checks. | `/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files` | `destination-absent-before-copy` fails after CP-39's boundary is reached. |
| PC-127 | G-127: Remove -T or equivalent. | `/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files` | `directory-destination-no-write-through` fails after CP-39's boundary is reached. |
| PC-128 | G-128: Accept credential key. | `/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads` | `toxic-not-exported` fails after CP-41's boundary is reached. |
| PC-129 | G-129: Accept nonnumeric groups. | `/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads` | `invalid-receipt-value-refused` fails after CP-41's boundary is reached. |
| PC-130 | G-130: Accept any nonzero without expected diagnosis. | `/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `setup-error-not-control-red` fails after CP-43's boundary is reached. |
| PC-131 | G-131: Add sudo to nested branch. | `/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python` | `nested-sudo-refused` fails after CP-40's boundary is reached. |
| PC-132 | G-132: Add python3 to nested branch. | `/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python` | `nested-python-refused` fails after CP-40's boundary is reached. |

| PC-133 | G-133: Allow an occupied/dangling-linked controller path. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `controller-fresh-nonlink` fails after CP-1's boundary is reached. |
| PC-134 | G-134: Create controller directory at 0755. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `controller-mode-0700` fails after CP-1's boundary is reached. |
| PC-135 | G-135: Accept a foreign-owner controller directory. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `controller-host-owner` fails after CP-1's boundary is reached. |
| PC-136 | G-136: Use an arbitrary image instead of admitted image. | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | `workspace-admitted-image` fails after CP-1's boundary is reached. |
| PC-137 | G-137: Replace controller root before taking maintenance lock. | `/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind` | `lock-before-controller-root` fails after CP-33's boundary is reached. |
| PC-138 | G-138: Remove lock-waiter.log from export list. | `/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal` | `five-logs-exported` fails after CP-38's boundary is reached. |
| PC-139 | G-139: Remove source regular-file predicate (T-class body assertion; a directory-copy failure alone is not red). | `/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files` | `nonregular-source-skipped` fails after CP-39's boundary is reached. |
| PC-140 | G-140: Change c849_payload_accept_transfer validation from writer to reader. | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | `writer-prunes-incomplete-stage` fails after reader-cannot-prune proves the incomplete package survives the read-only mount. Pending SourceLanding Mutation. |
| PC-141 | G-141: Bypass c849_fixture_cache_mount_ok in c849_fixture_recovery_copy. | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | `recovery-foreign-cache-refused` fails: the protected recipient is overwritten from the foreign-labeled cache. Pending SourceLanding Mutation. |
| PC-142 | G-142: Restore the apphost host root and payload bind. | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | `apphost-volume-payload` fails after CP-27's boundary is reached. |
| PC-143 | G-143: Remove ordinary import readonly. | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | `apphost-import-readonly` fails after CP-27's boundary is reached. |
| PC-144 | G-144: Skip mask workspace label validation. | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | `mask-foreign-refused` fails after CP-27's boundary is reached. |
| PC-145 | G-145: Skip apphost work mount admission. | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | `apphost-foreign-work-refused` fails after CP-27's boundary is reached. |
| PC-146 | G-146: Skip npm cache mount admission. | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | `apphost-foreign-cache-refused` fails after CP-27's boundary is reached. |
| PC-147 | G-147: Move masked helper registration after start. | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | `mask-partial-start-cleaned` fails after CP-27's boundary is reached. |
| PC-148 | G-148: Delete the early masked/apphost consumer release pass. | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | `mask-consumer-before-workspace` fails after CP-27's boundary is reached. |
| PC-149 | G-149: Bypass the controller identity comparison before export. | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | `foreign-controller-preserved` fails when replacement diagnostic bytes are exported; the removal guard is independent. Pending SourceLanding Mutation. |
| PC-150 | G-150: Delete the EXIT early-receipt rejection. | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | `early-exit-refused` fails: an early true receipt with otherwise clean cleanup stays accepted. Pending SourceLanding Mutation. |
| PC-151 | G-151: Suppress the foreign registered-helper residue. | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | `foreign-helper-residue` fails: the retained writer is omitted from the exact residue and success is accepted. Pending SourceLanding Mutation. |
| PC-152 | G-152: Release donor by deterministic name without its acquisition marker. | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | `unregistered-consumer-preserved` fails: the occupied foreign donor canary is deleted. Pending SourceLanding Mutation. |

S5 Code amendment: CP-21 exercises shared Seed and the actual F-3 wrapper, including rerun, exact fault diagnoses and no outer success publication. Its readonly filesystem model runs as non-root and must preserve an incomplete package through a reader; transfer acceptance prunes through the writer. CP-22 executes the actual F-9 corruption/restoration loop and the shared Prune authority/refusal matrix with volume recovery. Independent warm/inventory/cleanup and npm tool boundaries retain their earlier checkpoint owners. All S5 After cells already equal S5. PC-140 and PC-141 stay pending; ordinary baseline red does not discharge Mutation.

### Out of scope

- Live Fixture/Seed/rollout/restart or removal of previous failed-run trees:
  explicitly prohibited during authoring/ordinary Review. W-1 belongs to rollout.
- Immediate cleanup after SIGKILL, host loss or unavailable Docker: impossible
  to guarantee. Require attributed residue/refusal; no sweeper is added.
- New real saved-donor volume backend, global Docker ownership changes, queue/
  session delivery and parking: unchanged and outside this card.
- Real UID, Docker archive/volume behavior, npm install, Compose retention and
  NuGet locks cannot be established by the substitutes. W-1 remains mandatory.
- No full Unit/assembly, PTY or Windows-only run: relevant Linux methods are
  enumerated below; ordinary coverage does not imply rollout acceptance.

### S6 implementation binding

CP-27 executes F-5 with independent host decoys and volume recipients. It observes imported ordinary bytes, metadata and executable mode; the empty mask's three exact readonly/nocopy mounts; smoke and failed-start cleanup, with consumers released before workspace helpers; and foreign work/mask/npm-label refusal. The model executes the real import and substitutes chown only. It models image pack lookup rather than compiling the image smoke. CP-28 retains the independent real 0755-to-0700 import witness and exact mode admission. CP-29 now executes the real retention orchestration against parsed generated Compose documents; cache sentinel bytes are checked after recreate and down-v, and temp/private separation is observed before main teardown. CP-30 adds the parent-shell diagnosis witness. F-8 refill and F-9 recovery mutation/backup/observation seams already migrated in S3/S5; this slice does not rerun their out-of-scope CPs. W-1 remains rollout-owned. PC-142..148 are additional method-scoped controls, all pending.

### S7 Code binding amendment

CP-33 additionally executes the real volume fixture seed and recovery validator, and scans each individual checkout bind for readonly. CP-34 exercises actual EXIT/finalizer behavior, including primary plus secondary failure, replaced-controller export exclusion, early outer publication and foreign registered-helper residue. CP-35 observes actual resource/control disposal without sudo. CP-36 checks exact nine-line receipt bytes and missing/duplicate group, family and variant censuses. CP-38 binds consumer stop/join, five diagnostic bytes, helper and volume order. CP-39 executes failed unlink, remaining destination and directory-creation-before-copy races. CP-43 retains inventory/occupied/control sensitivity and uses the existing strict F-5 volume recipient model after the host recipient was removed. Its stale unregistered cleanup name is replaced with the exact admitted case name. No rollout or deliberate mutation is executed. PC-149..152 are new method-scoped controls, all pending. Every S7 After cell already equals S7.

### S7 F1 repair binding (round 2)

Round-3 documentation correction (Review 2ec97f2d F1): positive confirmation
applies to workspace/helper acquisition and consumer/helper/work/mask/cache/network
removal. Unknown results retain named residue and refuse success. Compose teardown
checks exit status only; residual Compose-created volumes after successful down -v
are not detected by this finalizer. That inherited limitation is CARD-1207 and
remains outside S7. W-1 live acceptance is pending. Round 3 changes docs/comments
only and reruns CP-33..52 once at committed source, with CP-44 exactly 19 results;
no historical-red rerun, whole Unit lane or full class run is authorized.

The repair brief's closed Final scope is CP-33..52: existing CP-33..43 remain one execution each, CP-44 remains exactly 19 adjacent executions, and CP-45..52 add eight methods. One S7 isolated build is reused within each committed group. No whole Unit lane/full class/live Docker acceptance is authorized. Added cost estimate: 10 row minutes plus slot contention; two source states are needed for historical red then repaired green.

CP-45..51 use the actual strict-shell wrapper, EXIT, finalizer and JSON publisher, including the real evidence/census function and success marker (external inventory/payload producers substituted), with three successful Compose teardowns before injecting a specific boundary. Each internal table covers exit 1 with exact No such resource/name (absent, success), exit 1 daemon unavailable, exit 2, exit 0 object present with failed removal, timeout exit 124, empty exit 1, permission denied, socket no-such-file, unrecognized error, exit-zero removal with object still present, exit 2 carrying missing-resource text, wrong resource name/kind, and mixed daemon-error/missing-resource text. Unknown outcomes require named residue, retained resources, accepted:false/nonzero, and an unknown-cleanup-outcome ledger entry. CP-52 answers every Docker operation with daemon-unavailable exit 1 after the last Compose down; both volumes and helpers must remain independently visible and named. These daemon-loss rows must fail against unchanged production at 276e0f30b4ca410ceb641924758fe201f49e7e50 before the fix. Timeout is modeled at the CLI exit boundary; native time/daemon behavior stays W-1.

All PC-1..172 and every variant remain pending method-scoped SourceLanding Mutation. Added guard census: 20 guards/PCs, total 172; CP-45..51 execute fourteen internal cases each (98) plus the global daemon-loss case. Original cost/census figures below describe the pre-repair TestDesign. Ordinary historical red is not a deliberate PC cycle.

Additional guard inventory:

| Guard | Invariant | Positive control |
|---|---|---|
| G-153 | S7 F1: consumer absence must be positively confirmed | PC-153 |
| G-154 | S7 F1: helper absence must be positively confirmed | PC-154 |
| G-155 | S7 F1: masked absence must be positively confirmed | PC-155 |
| G-156 | S7 F1: work absence must be positively confirmed | PC-156 |
| G-157 | S7 F1: mask absence must be positively confirmed | PC-157 |
| G-158 | S7 F1: cache absence must be positively confirmed | PC-158 |
| G-159 | S7 F1: network absence must be positively confirmed | PC-159 |
| G-160 | S7 F1: unknown outcome retains exact identity and ledger diagnosis | PC-160 |
| G-161 | S7 F1: consumer removal acknowledgement cannot replace positive absence | PC-161 |
| G-162 | S7 F1: helper removal acknowledgement cannot replace positive absence | PC-162 |
| G-163 | S7 F1: masked removal acknowledgement cannot replace positive absence | PC-163 |
| G-164 | S7 F1: work removal acknowledgement cannot replace positive absence | PC-164 |
| G-165 | S7 F1: mask removal acknowledgement cannot replace positive absence | PC-165 |
| G-166 | S7 F1: cache removal acknowledgement cannot replace positive absence | PC-166 |
| G-167 | S7 F1: network removal acknowledgement cannot replace positive absence | PC-167 |
| G-168 | S7 F1: Require exit 1 before admitting missing-resource output | PC-168 |
| G-169 | S7 F1: Missing-resource response identifies the exact resource name | PC-169 |
| G-170 | S7 F1: Missing-resource response identifies the resource kind | PC-170 |
| G-171 | S7 F1: Every nonempty non-JSON response line must be recognized | PC-171 |
| G-172 | S7 F1: Fixture success marker follows successful finalization | PC-172 |

Additional positive controls:

| PC | Break guard | Detecting filter | Expected red assertion |
|---|---|---|---|
| PC-153 | G-153: In the consumer cleanup caller, substitute confirmed absence for unknown probe results. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence` | `daemon-verdict` fails after the boundary is reached; all fourteen variants pending. |
| PC-154 | G-154: In the helper cleanup caller, substitute confirmed absence for unknown probe results. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence` | `daemon-verdict` fails after the boundary is reached; all fourteen variants pending. |
| PC-155 | G-155: In the masked cleanup caller, substitute confirmed absence for unknown probe results. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence` | `daemon-verdict` fails after the boundary is reached; all fourteen variants pending. |
| PC-156 | G-156: In the work cleanup caller, substitute confirmed absence for unknown probe results. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence` | `daemon-verdict` fails after the boundary is reached; all fourteen variants pending. |
| PC-157 | G-157: In the mask cleanup caller, substitute confirmed absence for unknown probe results. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence` | `daemon-verdict` fails after the boundary is reached; all fourteen variants pending. |
| PC-158 | G-158: In the cache cleanup caller, substitute confirmed absence for unknown probe results. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence` | `daemon-verdict` fails after the boundary is reached; all fourteen variants pending. |
| PC-159 | G-159: In the network cleanup caller, substitute confirmed absence for unknown probe results. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence` | `daemon-verdict` fails after the boundary is reached; all fourteen variants pending. |
| PC-160 | G-160: Omit unknown-cleanup-outcome ledger append. | `/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success` | `daemon-ledger` fails; all four work/helper identity variants pending. |
| PC-161 | G-161: Skip the post-removal probe for consumer. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence` | `rm-lie-verdict` fails with exit-zero removal and retained object; pending. |
| PC-162 | G-162: Skip the post-removal probe for helper. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence` | `rm-lie-verdict` fails with exit-zero removal and retained object; pending. |
| PC-163 | G-163: Skip the post-removal probe for masked. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence` | `rm-lie-verdict` fails with exit-zero removal and retained object; pending. |
| PC-164 | G-164: Skip the post-removal probe for work. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence` | `rm-lie-verdict` fails with exit-zero removal and retained object; pending. |
| PC-165 | G-165: Skip the post-removal probe for mask. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence` | `rm-lie-verdict` fails with exit-zero removal and retained object; pending. |
| PC-166 | G-166: Skip the post-removal probe for cache. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence` | `rm-lie-verdict` fails with exit-zero removal and retained object; pending. |
| PC-167 | G-167: Skip the post-removal probe for network. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence` | `rm-lie-verdict` fails with exit-zero removal and retained object; pending. |
| PC-168 | G-168: Delete the exit-1 predicate in c849_fixture_resource_state. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence` | `exit2-absent-verdict` fails at the post-Compose boundary; pending. |
| PC-169 | G-169: Replace the exact identity in the accepted response with a wildcard. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence` | `other-name-verdict` fails at the post-Compose boundary; pending. |
| PC-170 | G-170: Accept any no-such text regardless of kind. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence` | `other-kind-verdict and socket-verdict` fails at the post-Compose boundary; pending. |
| PC-171 | G-171: Ignore unrecognized lines after a matching absent line. | `/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence` | `mixed-verdict` fails at the post-Compose boundary; pending. |
| PC-172 | G-172: Move C849_FIXTURE success marker back into c849_fixture_evidence before finalization. | `/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success` | `daemon-marker` fails; pending. |

### Checkpoints

Authoritative closed list after S7 repair: **53 rows (52 exact methods plus the adjacent OR selection), 79 TUnit executions minimum,
seven isolated builds**, Linux only. Each row has one build output (new or
same-slice reuse) and one exact filter. Wildcard namespace segments are retained;
class/method segments are literal except the explicitly required CP-44 adjacent OR selection. Method filters still select all Arguments.

No identical methods were duplicated in the proposal. CP-2 checks scope,
CP-3 partial acquisition, CP-34 final verdict and CP-35 retired host ownership;
CP-36 is producer census, CP-42 recipient validation; CP-38 orders export and
CP-39 tests unsafe paths. Keep their different decisive assertions, with no
whole-class reruns. All ordinary V/R IDs are covered by the union.

Run checkpoint tool once per committed slice via host build-slot gate, with
--after S<n>, expected full source SHA and forward-slash outputs. Reuse builds
only within the same After slice; preserve clean CHECKPOINT/source receipts.
Await every exit-75 continuation. Slot timeout is not run. Zero/skipped tests
are not green. If synthetic cases exceed LinuxShell's existing 60s limit, bound
the model's waits or split/amend the manifest, never widen timeout silently.
Run the evidence-diff guard over the full Code range. Static coverage must bind
every promised method/label after Code; unbound payloads remain binding required.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1192-s1/` | linux-registration | `/*/*/RemoteScriptContractTests/C1192_Workspace_registration_is_absent_only_and_survives_prepare` | V-1 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-2 | S1 | `CP-1` | linux-cleanup-scope | `/*/*/RemoteScriptContractTests/C1192_Cleanup_releases_only_registered_current_run_resources` | V-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-3 | S1 | `CP-1` | linux-partial-acquisition | `/*/*/RemoteScriptContractTests/C1192_Partial_workspace_acquisition_is_cleaned` | V-1, V-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-4 | S1 | `CP-1` | linux-toxic-probe | `/*/*/RemoteScriptContractTests/C1192_Toxic_probe_stays_in_work_volume` | V-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1192-s2/` | linux-race-mounts | `/*/*/RemoteScriptContractTests/C1192_Race_programs_and_feed_share_the_work_volume` | V-3 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-6 | S2 | `CP-5` | linux-race-barriers | `/*/*/RemoteScriptContractTests/C1192_Race_barriers_observe_volume_state` | V-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-7 | S2 | `CP-5` | linux-lock-discrimination | `/*/*/RemoteScriptContractTests/C1192_Lock_probe_distinguishes_shared_and_private_scratch` | V-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-8 | S2 | `CP-5` | linux-zip-image | `/*/*/RemoteScriptContractTests/C1189_Fixture_package_zip_does_not_call_host_pwsh` | R-1 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-9 | S2 | `CP-5` | linux-probe-handoff | `/*/*/RemoteScriptContractTests/C1191_Lock_probe_is_created_before_uid_handoff` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-10 | S2 | `CP-5` | linux-lock-environment | `/*/*/RemoteScriptContractTests/C1193_Lock_probe_passes_home_and_nuget_scratch` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-11 | S3 | `tests/Antiphon.Tests -> bin-c1192-s3/` | linux-npm-volume | `/*/*/RemoteScriptContractTests/C1192_Npm_seed_install_and_refill_use_work_volume` | V-4 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-12 | S3 | `CP-11` | linux-npm-recipient | `/*/*/RemoteScriptContractTests/C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed` | V-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-13 | S3 | `CP-11` | linux-hash-image | `/*/*/RemoteScriptContractTests/C1189_Fixture_npm_integrity_does_not_call_host_pwsh` | R-1 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-14 | S3 | `CP-11` | linux-npm-nounset | `/*/*/RemoteScriptContractTests/C1066_Fixture_npm_install_uses_argument_log_under_nounset` | R-6 | 2 executed, 0 failed/skipped | 2 | 1 |
| CP-15 | S4 | `tests/Antiphon.Tests -> bin-c1192-s4/` | linux-backend-selection | `/*/*/RemoteScriptContractTests/C1192_Real_cache_cases_keep_the_host_payload_backend` | V-5 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-16 | S4 | `CP-15` | linux-transfer | `/*/*/RemoteScriptContractTests/C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy` | V-5 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-17 | S4 | `CP-15` | linux-seed-validation | `/*/*/RemoteScriptContractTests/C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | R-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-18 | S4 | `CP-15` | linux-seed-order | `/*/*/RemoteScriptContractTests/C913_Full_marker_binds_verified_recovery_before_publication` | R-7 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-19 | S4 | `CP-15` | linux-marker-versions | `/*/*/RemoteScriptContractTests/C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | R-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-20 | S4 | `CP-15` | linux-saved-donor | `/*/*/RemoteScriptContractTests/C849_Saved_donor_archive_is_checked_and_imported_without_a_container` | R-7 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-21 | S5 | `tests/Antiphon.Tests -> bin-c1192-s5/` | linux-fixture-seed | `/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` | V-5, V-6 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-22 | S5 | `CP-21` | linux-recovery | `/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest` | V-6 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-23 | S5 | `CP-21` | linux-seed-failure | `/*/*/RemoteScriptContractTests/C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt` | R-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-24 | S5 | `CP-21` | linux-tree-nounset | `/*/*/RemoteScriptContractTests/C1066_Fixture_tree_fault_reaches_validator_under_nounset` | R-6 | 6 executed, 0 failed/skipped | 6 | 1 |
| CP-25 | S5 | `CP-21` | linux-shared-prune | `/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs` | R-7, V-6 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-26 | S5 | `CP-21` | linux-seed-root | `/*/*/RemoteScriptContractTests/C1195_Seed_import_chmod_restores_volume_root` | R-5 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-27 | S6 | `tests/Antiphon.Tests -> bin-c1192-s6/` | linux-mask-volume | `/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount` | V-7, V-2 (S6 consumers) | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-28 | S6 | `CP-27` | linux-apphost-root | `/*/*/RemoteScriptContractTests/C1195_Fixture_import_chmod_restores_volume_root` | R-5, V-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-29 | S6 | `CP-27` | linux-retention | `/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content` | R-5, V-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-30 | S6 | `CP-27` | linux-budget-observation | `/*/*/RemoteScriptContractTests/C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution` | R-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-31 | S6 | `CP-27` | linux-budget-validation | `/*/*/RemoteScriptContractTests/C1194_Fixture_budget_scale_refuses_empty_observation` | R-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-32 | S6 | `CP-27` | linux-warm-nounset | `/*/*/RemoteScriptContractTests/C1066_Fixture_warm_probe_uses_argument_log_under_nounset` | R-6 | 3 executed, 0 failed/skipped | 3 | 1 |
| CP-33 | S7 | `tests/Antiphon.Tests -> bin-c1192-s7/` | linux-no-host-payload | `/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind` | V-8 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-34 | S7 | `CP-33` | linux-finalization | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-35 | S7 | `CP-33` | linux-workspace-disposal | `/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo` | R-2, V-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-36 | S7 | `CP-33` | linux-evidence-roster | `/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads` | V-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-37 | S7 | `CP-33` | linux-escape-unlink | `/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit` | R-1 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-38 | S7 | `CP-33` | linux-log-order | `/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-39 | S7 | `CP-33` | linux-log-paths | `/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-40 | S7 | `CP-33` | linux-lane | `/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python` | R-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-41 | S7 | `CP-33` | linux-receipt-exclusion | `/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads` | R-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-42 | S7 | `CP-33` | linux-receipt-consumer | `/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | R-8 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-43 | S7 | `CP-33` | linux-fixture-inventory | `/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | R-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-44 | S7 | `CP-33` | linux-adjacent | `/*/*/RemoteScriptContractTests/(C1189_*\|C1191_*\|C1193_*\|C1194_*\|C1195_*\|C1192_Summary_*\|C1192_Foreign_*\|C1192_Harness_*\|C1192_Helper_*)*` | R-1..R-5, R-8 | 19 executed, 0 failed/skipped | 19 | 2 |
| CP-45 | S7 | `CP-33` | linux-cleanup-consumer | `/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence` | V-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-46 | S7 | `CP-33` | linux-cleanup-helper | `/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence` | V-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-47 | S7 | `CP-33` | linux-cleanup-masked | `/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence` | V-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-48 | S7 | `CP-33` | linux-cleanup-work | `/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence` | V-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-49 | S7 | `CP-33` | linux-cleanup-mask | `/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence` | V-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-50 | S7 | `CP-33` | linux-cleanup-cache | `/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence` | V-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-51 | S7 | `CP-33` | linux-cleanup-network | `/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence` | V-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-52 | S7 | `CP-33` | linux-daemon-loss | `/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success` | V-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-53 | S7 | `CP-33` | linux-acquisition-unknown | `/*/*/RemoteScriptContractTests/C1192_Acquisition_unknown_ownership_retains_registration_and_residue` | V-1, V-2, V-9 | 1 executed, 0 failed/skipped | 1 | 1 |

### Cost

All figures are **estimated**, not measured. This TestDesign ran no build/test.
Ordinary V/R floor (Code) = **82 minutes**: S1 CP-1..4 = 9;
S2 CP-5..10 = 11; S3 CP-11..14 = 9; S4 CP-15..20 = 13;
S5 CP-21..26 = 12; S6 CP-27..32 = 11; S7 CP-33..43 = 17.
Exact filters/minutes are in the table. This includes seven 5-minute isolated
builds (**35 minutes**) plus **47 minutes** of filter work, including setup.
C1066 2/6/3 values are executions, not minutes. Slot wait is unknown and additive.

Separate PC floor (Mutation) = **2280 minutes**, all **152 PCs**.
Each exact detecting filter above is budgeted 15 minutes: 1 patch/restore/
boundary inspection + 5 isolated red build + 2 red method + 5 restored build +
2 green method. Both states rebuild conservatively for provenance; no unvalidated
no-build reuse is assumed. All PCs run even when they share a method. Mutation
discovery/reporting and slot contention add time. Commission bounded packs by
slice/guard range, preserving all rows and the original Code landing owner.

Total verification floor = 35 setup/build + 47 ordinary V/R + 2280 PC cycles
= **2362 minutes**. Code authoring remains the separate 335-minute estimate;
authoring plus verification floor = **2697 minutes**. W-1 is separately
scheduled rollout work, outside authoring/Mutation, not zero-cost ordinary proof.

Ordinary build reuse saves **180 minutes** versus 43 independent builds:
(43 - 7) x 5. Duplicate-filter savings = 0 because methods are distinct.
Mutation batching savings = 0: guards mostly share one source file and require
independent red/restored-green cycles. This deliberately exposes the true
control cost rather than hiding an assembly run or omitting guards.

Inventory audit before handoff (S5 adds PC-140/141; S6 adds PC-142..148; S7 adds PC-149..152): **guards=152, mapped=152, missing=0,
duplicate PC maps=0**. All 152 PCs are scheduled and required; **0 executed
during TestDesign**. Code binds implementation/labels, Review judges them before
land, Mutation reports 152/152 red/restore/green outcomes or explicit findings.
**Post-land Mutation was NOT run.**

### Operator decisions

None required for the authorized implementation. The rollout owner decides
when W-1 runs after Review/land. Migrating real retained Seed recovery or
removing old failed-run host trees needs separate operator scope authorization.

TestDesign handoff: **next: code**, first slice **S1 — workspace lifecycle and
F-9 toxic-probe consumer**. Bind its methods and PC targets, commit, run CP-1..4
at that commit, push and hand off to ordinary Review. Later slices remain serial
and each has its own committed checkpoint group.
