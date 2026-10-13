# CARD-1208 finalizer deletion audit

Round 2 base: 7ed60feb0d088bf0c7368f90e989ae0d467e31ae. Code/landing owner: 8c713d0c-fe3f-4097-894b-9fc892a709ae. Branch feat/card-task-8c713d0c. Predecessor Code task: b1b731e0-1d07-4589-8c85-94b420b101bd (original base 9f89cc427dca7030cfc93a45ec36e4375d894213). Restart: none. Earlier completion below is historical and superseded by the round 2 evidence.

## Deletion inventory and decision

| Deletion call/site | Object | Base identity / acquisition registration | Applied rule |
|---|---|---|---|
| consumer_release/resource_remove via finalize and cleanup | fixed roster containers | name; no acquisition record | Require acquired record; remove durable ID. |
| helper_release/resource_remove | writer, reader, masked, apphost | .fixture-helpers name; masked/apphost also roster | Require registration and labels; resolve ID. |
| finalize prune-smoke/npm-server/busy/donor | legacy containers | marker name or ID | ID release; name migration with verified current-run labels or refusal. |
| finalize network/resource_remove | npm network | name-only .fixture-network | Record create ID; verify legacy labels or refuse. |
| cleanup/resource_remove volume | work, mask | .fixture-volumes plus owner/run/role labels | Registration and full labels; foreign dropped; unknown residue. |
| cleanup/resource_remove volume | packages/scratch/npm, seed/busy/race/private/smoke/warm variants | .fixture-volumes; name alone | Add run labels at acquisition; verify full cache identity before deletion. |
| shared c849_seed smoke helper run/rm when payload backend=volume | seed helper | base name c849-seed-RUN | Record cidfile and use acquired release; finalizer includes recorded consumers beyond roster. |
| fixture_tree_mutate metadata/reference rm -rf; payload shell rm/mv/chmod probes | payload subtrees/canaries | acquired labeled work-volume helper mounts | Confined to the acquired payload volume; CP-33 validates the payload recipient. |
| immediate busy/masked/apphost/npm-http/prune-smoke docker rm | acquired containers | name or marker | Same durable acquired identity release. |
| finalize docker compose down -v temp/main/negative | Compose resources | project + owned controller file | CARD-1207 excluded; exit-status-only teardown. |
| controller_remove find rm -rf/rmdir | controller content/directory | recorded device/inode/uid | Preserve cwd/path/identity checks and replacement refusal. |
| export_logs rm -rf destination | five diagnostic files | CASE_DIR names | Unlink files/symlinks only; refuse directories. |
| finalize escape unlink; controller identity unlink; prepare/drop temporary ledgers; marker unlink | control files | bounded CASE_DIR paths | Escape symlink gets device/inode/uid acquisition record and revalidation (CP-37, CP-57); remaining temporary control records are unlink-only inside the case namespace; controller identity removed after disposal. |
| wrapper reset_runtime; model volume/container rm; simulated compose down; module setup rm | model objects | fresh private root with nonce | Remain inside acquired private root; model IDs address exact objects. |
| wrapper contained-cleanup find/unlink/rmdir; cleanup_private EXIT | scratch tree | nonce, containment, ancestor/held-directory identity | Preserve named replacement/symlink canaries. |
| fixture EXIT -> fixture_exit -> finalize | traps/receipts | primary exit status + named residue | Preserve primary failure; unknown refuses success; foreign disposition retained. |
| anonymous docker run --rm | ephemeral acquisition helpers | Docker allocated ID | Docker owns automatic disposal; no finalizer ownership inferred. |

Deletion policy, checked by CP-54..72: require current-run labels on the recorded/resolved ID, the recorded container/network name, and any recorded container creation fingerprint; registered volumes require their exact admitted name and complete run/cache labels plus driver/options. A name or roster entry confers no ownership. Drop positively foreign objects immediately and record "foreign: left untouched". Resolve legacy names to IDs only after verifying current-run labels; unknown identity retains named residue and refuses success. Volumes lack durable IDs, so use owner/run/role/driver/options. Concurrent privileged relabeling is outside this Docker identity contract.

## Verification design

Specific brief overrides generic Final: no Unit lane or full class. CP-33..53 preserve S7, including CP-44's 19 earlier adjacent methods once. V-1 foreign cache; V-2 legacy container replacement; V-3 legacy network replacement; V-4 unacquired consumer/helper; V-5 legitimately acquired objects; V-6 recorded ID; V-7 verified legacy migration; V-8 unknown identity. R-1..R-21 map respectively to CP-33..53's named obligations. New stub tests execute production acquisition/finalization and assert actual object survival/removal calls/refusal. CP-54..57 must be red before production fixes against the landed base. W-1 pending rollout owner; Compose teardown CARD-1207 excluded.

### Positive controls (pending Mutation)

PC-1 foreign drop/receipt/removal guard (cache variants); PC-2 legacy container label checks; PC-3 legacy network label checks; PC-4 acquisition requirement (consumer/helper); PC-5 omitted legitimate release (work/mask/cache/helper/consumer/network); PC-6 ID replaced by name; PC-7 migration skipped/name deletion restored; PC-8 unknown treated as owned/absent (container/network/volume). Every PC/variant is pending method-scoped SourceLanding Mutation; Mutation owns deliberate red/restore/green and missing-control discovery. Existing CARD-1192 PC-1..174 remain pending.

| PC | Variants | Detecting filter | Status |
|---|---|---|---|
| PC-1 | cache drop / receipt / removal; packages, scratch, npm (role controls pending) | `/*/*/RemoteScriptContractTests/C1208_Foreign_cache_is_dropped_and_untouched` | PENDING SourceLanding Mutation |
| PC-2 | container owner, run, role | `/*/*/RemoteScriptContractTests/C1208_Legacy_container_replacement_is_untouched` | PENDING SourceLanding Mutation |
| PC-3 | network owner, run, role | `/*/*/RemoteScriptContractTests/C1208_Legacy_network_replacement_is_untouched` | PENDING SourceLanding Mutation |
| PC-4 | consumer, helper, escape symlink acquisition requirement | `/*/*/RemoteScriptContractTests/C1208_Unacquired_roster_is_untouched` | PENDING SourceLanding Mutation |
| PC-5 | work, mask, cache, consumer, writer, reader, network release | `/*/*/RemoteScriptContractTests/C1208_Acquired_objects_are_removed` | PENDING SourceLanding Mutation |
| PC-6 | container ID replaced by name | `/*/*/RemoteScriptContractTests/C1208_Recorded_container_id_does_not_delete_replacement` | PENDING SourceLanding Mutation |
| PC-7 | container and network migration skipped / name deletion restored / network ID replaced by name | `/*/*/RemoteScriptContractTests/C1208_Legacy_owned_markers_are_migrated` | PENDING SourceLanding Mutation |
| PC-8 | outage; empty, malformed, symlinked CID; standalone symlink/directory CID | `/*/*/RemoteScriptContractTests/C1208_Unknown_identity_retains_residue` | PENDING SourceLanding Mutation |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-33 | Fixed | `tests/Antiphon.Tests -> bin-c1208/` | linux-no-host-payload | `/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind` | R-1 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-34 | Fixed | `CP-33` | linux-finalization | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-35 | Fixed | `CP-33` | linux-workspace-disposal | `/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-36 | Fixed | `CP-33` | linux-evidence-roster | `/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads` | R-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-37 | Fixed | `CP-33` | linux-escape-unlink | `/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit` | R-5 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-38 | Fixed | `CP-33` | linux-log-order | `/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal` | R-6 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-39 | Fixed | `CP-33` | linux-log-paths | `/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files` | R-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-40 | Fixed | `CP-33` | linux-lane | `/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python` | R-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-41 | Fixed | `CP-33` | linux-receipt-exclusion | `/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads` | R-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-42 | Fixed | `CP-33` | linux-receipt-consumer | `/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | R-10 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-43 | Fixed | `CP-33` | linux-fixture-inventory | `/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | R-11 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-44 | Fixed | `CP-33` | linux-adjacent | `/*/*/RemoteScriptContractTests/(C1189_*\|C1191_*\|C1193_*\|C1194_*\|C1195_*\|C1192_Summary_*\|C1192_Foreign_*\|C1192_Harness_*\|C1192_Helper_*)*` | R-12 | 19 executed, 0 failed/skipped | 19 | 2 |
| CP-45 | Fixed | `CP-33` | linux-cleanup-consumer | `/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence` | R-13 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-46 | Fixed | `CP-33` | linux-cleanup-helper | `/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence` | R-14 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-47 | Fixed | `CP-33` | linux-cleanup-masked | `/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence` | R-15 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-48 | Fixed | `CP-33` | linux-cleanup-work | `/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence` | R-16 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-49 | Fixed | `CP-33` | linux-cleanup-mask | `/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence` | R-17 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-50 | Fixed | `CP-33` | linux-cleanup-cache | `/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence` | R-18 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-51 | Fixed | `CP-33` | linux-cleanup-network | `/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence` | R-19 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-52 | Fixed | `CP-33` | linux-daemon-loss | `/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success` | R-20 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-53 | Fixed | `CP-33` | linux-acquisition-unknown | `/*/*/RemoteScriptContractTests/C1192_Acquisition_unknown_ownership_retains_registration_and_residue` | R-21 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-54 | Fixed | `CP-33` | audit-54 | `/*/*/RemoteScriptContractTests/C1208_Foreign_cache_is_dropped_and_untouched*` | V-1 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-55 | Fixed | `CP-33` | audit-55 | `/*/*/RemoteScriptContractTests/C1208_Legacy_container_replacement_is_untouched*` | V-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-56 | Fixed | `CP-33` | audit-56 | `/*/*/RemoteScriptContractTests/C1208_Legacy_network_replacement_is_untouched*` | V-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-57 | Fixed | `CP-33` | audit-57 | `/*/*/RemoteScriptContractTests/C1208_Unacquired_roster_is_untouched*` | V-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-58 | Fixed | `CP-33` | audit-58 | `/*/*/RemoteScriptContractTests/C1208_Acquired_objects_are_removed*` | V-5 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-59 | Fixed | `CP-33` | audit-59 | `/*/*/RemoteScriptContractTests/C1208_Recorded_container_id_does_not_delete_replacement*` | V-6 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-60 | Fixed | `CP-33` | audit-60 | `/*/*/RemoteScriptContractTests/C1208_Legacy_owned_markers_are_migrated*` | V-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-61 | Fixed | `CP-33` | audit-61 | `/*/*/RemoteScriptContractTests/C1208_Unknown_identity_retains_residue*` | V-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-62 | Fixed | `CP-33` | repair-62 | `/*/*/RemoteScriptContractTests/C1208_Wrong_cidfile_preserves_foreign_container*` | V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-63 | Fixed | `CP-33` | repair-63 | `/*/*/RemoteScriptContractTests/C1208_Recorded_id_with_changed_labels_is_untouched*` | V-10 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-64 | Fixed | `CP-33` | repair-64 | `/*/*/RemoteScriptContractTests/C1208_Recorded_container_inspect_failure_refuses_cleanup*` | V-11 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-65 | Fixed | `CP-33` | repair-65 | `/*/*/RemoteScriptContractTests/C1208_Wrong_helper_id_preserves_foreign_container*` | V-12 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-66 | Fixed | `CP-33` | repair-66 | `/*/*/RemoteScriptContractTests/C1208_Wrong_network_id_is_untouched*` | V-13 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-67 | Fixed | `CP-33` | repair-67 | `/*/*/RemoteScriptContractTests/C1208_Recorded_network_inspect_failure_refuses_cleanup*` | V-14 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-68 | Fixed | `CP-33` | repair-68 | `/*/*/RemoteScriptContractTests/C1208_Owned_packages_registration_survives_production_controls*` | V-15 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-69 | Fixed | `CP-33` | repair-69 | `/*/*/RemoteScriptContractTests/C1208_Dangling_network_record_retains_residue*` | V-16 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-70 | Fixed | `CP-33` | repair-70 | `/*/*/RemoteScriptContractTests/C1208_Invalid_network_record_retains_text*` | V-17 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-71 | Fixed | `CP-33` | repair-71 | `/*/*/RemoteScriptContractTests/C1208_Confirmed_absent_network_record_is_cleared*` | V-18 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-72 | Fixed | `CP-33` | repair-72 | `/*/*/RemoteScriptContractTests/C1208_Recorded_creation_fingerprint_is_reverified*` | V-19 | 1 executed, 0 failed/skipped | 1 | 1 |

| CP-73 | Fixed | `CP-33` | repair-fingerprint-acquisition | `/*/*/RemoteScriptContractTests/C1208_Fingerprint_acquisition_refuses_unowned_paths*` | V-20 | 6 executed, 0 failed/skipped | 6 | 1 |

### Cost

Round 2 ordinary floor 47 row minutes, plus authoring/slot contention. Round 2 test-only proof groups CP-62..71, CP-63/65/68/72, and CP-64/67/68 each share one isolated build; the repaired full selection CP-33..72 shares one isolated build. Checkpoint-tool bootstrap is an explained unlisted build through build-slot, UseAppHost=false, bin-c1208repair-tool/. No repeats after green. Earlier proof-history descriptions below refer to round 1.

## Implementation binding

Named create/run uses Docker cidfiles at creation, including foreground --rm consumers. Cleanup validates owner/run/role on recorded full IDs and resolved legacy IDs; the recorded container name and any creation fingerprint also match the inspected ID (CP-62..67/72). Registered helpers preserve their label checks; volumes require the recorded roster and complete current-run identity. Empty, malformed and symlinked ID records refuse cleanup. CP-57 also checks an unacquired escape symlink; CP-39 now checks retained diagnostic directories rather than recursively replacing them. SourceLanding PCs remain pending. The isolated scratch-model diagnostic is an explained extra build-slot run, not ordinary TRX evidence.

## Ordinary verification repair

The first full run at e15865e025850492c2dfb7ac97f5678b78a88646 ran all 29 rows: CP-33/43 exposed older c913 seed/inventory Docker models without cidfile/identity support; CP-44 exposed the lock argument test selecting only a literal docker-run command. Update those models and bind the argument test to the acquired launch wrapper without removing its HOME/scratch/path assertions. All production code stays unchanged. Container/network replacement tests now vary owner/run/role independently; unknown identity adds empty/malformed/symlink CID records. Rerun CP-33, CP-43, CP-44, CP-55, CP-56, CP-61 after this committed verification repair. No assertion, deadline or retry is weakened.

Second repair: CP-33 exposed its next wrapper scenario reusing a still-acquired donor; release the predecessor through production ID cleanup before reacquisition. CP-43 exposed the apphost model mounting its planted npm cache without registering acquisition; record that legitimate fixture acquisition. Rerun only CP-33/43. Production code and assertions remain unchanged.

Final audit refinement: the generic cidfile sweep skipped symlink/directory records for acquired consumers outside the fixed roster, suppressing unknown residue. Add those input cases to CP-61 before changing production. A direct, build-slot-bound fixture assertion establishes their red result on the committed pre-fix source; rerun CP-61 after routing them through the existing refusing record validator. CP-33 also needs its recovery model to register acquisition and restore that record only after the foreign-label fault is restored and full identity reverified. Rerun CP-33 alongside CP-61; other green rows are unchanged.

The committed direct fixture proof at 3f40cc432ae2a6581709817190fc3256ebbd5f45 exited 1 on audit=0, standalone-symlink=0 and standalone-directory=0; fixture-seed and volume-recovery assertions passed. Route admitted standalone records through consumer_release regardless of file type, keeping its unknown/refusal validator. Final targeted run: CP-33/61. SourceLanding Mutation was not run.

## Completion

All CP-33..61 now have passing ordinary receipts: 29 rows, 47 executions, 43 distinct methods; CP-44 exactly 19. Independent fresh TRX and per-source receipt audits passed. Final ordinary evidence is cumulative across explicitly recorded tested commits; only affected rows were rerun after repairs. No ordinary IDs are deferred. Final code tip 4c675e64588c5e3f39a875c203cc34447bc98b39; see 2026-10-13-card-1208-finalizer-code-evidence.md for unedited checkpoint lines, actual source bindings and pending Mutation/W-1. Table Covers for inherited guard rows are explicitly CARD-1208 R-1..21, matching the verification design; this final clarification changes documentation only.


## Round 2 repair binding

Repair Code/landing owner: 8c713d0c-fe3f-4097-894b-9fc892a709ae; predecessor b1b731e0-1d07-4589-8c85-94b420b101bd. Base 7ed60feb0d088bf0c7368f90e989ae0d467e31ae. Closed Final scope CP-33..72; no Unit or whole class, per repair brief. V-9..18 map in order to CP-62..71: wrong foreign CID, changed labels on an ID, container inspect outage, wrong helper ID, wrong network ID, network inspect outage, owned packages through actual PC-09/10 controls, dangling network marker, invalid network text, positively absent marker. Each has hand-written survival/deletion/refusal/registration or receipt expectations. First run selects CP-62..71 before production edits; then all 40 rows (58 executions; CP-44 exactly 19).

PC-9 wrong CID/owner/run/role; PC-10 helper wrong ID; PC-11 network wrong ID/owner/run/role; PC-12 container/network inspect outage; PC-13 authoritative packages ledger isolation through PC-09/10 and PC-32; PC-14 dangling/malformed network record and text residue; PC-15 positive-absence clearing/receipt. All variants PENDING method-scoped SourceLanding Mutation, alongside PC-1..8 and CARD-1192 PC-1..174. No Mutation claimed. W-1 and CARD-1207 excluded. Restart: none.

V-19/CP-72 verifies each recorded container name/image/Created fingerprint field against the inspected ID. PC-16 name/image/created compare bypass is PENDING, filter `/*/*/RemoteScriptContractTests/C1208_Recorded_creation_fingerprint_is_reverified*`. CP-68 also runs real PC-32 and requires pre-existing residue to survive its private mock. Legacy acquisitions without fingerprints require recorded name plus owner/run/role; no image/creation fingerprint is inferred for them.

## Round 2 ID deletion authorization

| Finalizer deletion path | Ownership check immediately before Docker delete | Named proof |
|---|---|---|
| Fixed consumer roster, generic cidfile sweep, donor/busy/npm-server/prune-smoke marker, immediate named consumer release and seed helper release | consumer_release -> container_identity -> resource_remove -> container_identity: inspect target ID; owner/run/role; recorded name; recorded name/image/Created fingerprint when present | CP-58/59/62/63/64/72; S7 CP-33..53 |
| Writer/reader/masked/apphost helper roster and immediate helper consumer release | helper registration + labels; container_identity; shared resource_remove repeats ID labels/name/fingerprint checks | CP-58/65/72, CP-46/47 |
| Npm network (full ID or legacy name) | network_identity: resolve full ID then owner/run/npm-network role and recorded name; shared resource_remove repeats these checks | CP-56/60/66/67/71 |
| Work/mask volume names (Docker volumes have no separate durable ID) | exact admitted registered name; current-run owner/run/role; driver local and options null/{} immediately before deletion | CP-48/49/53/58 |
| Registered packages/scratch/npm/cache variants | exact admitted registered name; owner/schema/cache-role/run and local driver/options; config/inspect uncertainty keeps registration; only a confirmed label mismatch drops foreign registration | CP-50/54/58/68 |

Persistent container acquisitions record name/image/Created after Docker cidfile creation. A foreground --rm acquisition already confirmed absent needs no new fingerprint. Legacy records without fingerprints require their recorded name and full labels. Compose teardown (CARD-1207) and automatic Docker --rm disposal are outside manual ID deletion.

Synthetic production controls set a subshell flag that isolates foreign-volume ledger edits; production PC-32 consumes a copied ledger and its own residue files. CP-68 executes the real controls with a modeled Docker boundary, asserting real acquired registration and pre-existing residue survive before finalization removes the volume. Configuration mismatch alone is UNKNOWN for finalizer cache identity, retaining the owned/uncertain registration; the acquisition's existing CacheVolumeForeign diagnosis remains unchanged.

C1208_Wrong_cidfile_preserves_foreign_container and C1208_Recorded_id_with_changed_labels_is_untouched check full-ID owner/run/role validation. C1208_Recorded_creation_fingerprint_is_reverified checks recorded container name/image/Created fields. C1208_Wrong_helper_id_preserves_foreign_container and C1208_Wrong_network_id_is_untouched check foreign helper/network IDs. C1208_Recorded_container_inspect_failure_refuses_cleanup and C1208_Recorded_network_inspect_failure_refuses_cleanup check ownership-probe failure. C1208_Owned_packages_registration_survives_production_controls checks authoritative registration through production PC-09/10 and residue through PC-32. C1208_Dangling_network_record_retains_residue, C1208_Invalid_network_record_retains_text and C1208_Confirmed_absent_network_record_is_cleared check invalid-record refusal/text and positive-absence clearing/receipt. These are modeled contracts; native Docker W-1 remains pending.

| PC | Variants | Method-scoped filters (each separate cycle) | Status |
|---|---|---|---|
| PC-9 | container wrong valid CID; owner/run/role | `/*/*/RemoteScriptContractTests/C1208_Wrong_cidfile_preserves_foreign_container*`, `/*/*/RemoteScriptContractTests/C1208_Recorded_id_with_changed_labels_is_untouched*` | PENDING SourceLanding Mutation |
| PC-10 | helper wrong valid CID | `/*/*/RemoteScriptContractTests/C1208_Wrong_helper_id_preserves_foreign_container*` | PENDING SourceLanding Mutation |
| PC-11 | network wrong valid ID; owner/run/role | `/*/*/RemoteScriptContractTests/C1208_Wrong_network_id_is_untouched*` | PENDING SourceLanding Mutation |
| PC-12 | container/network label-inspect outage | `/*/*/RemoteScriptContractTests/C1208_Recorded_container_inspect_failure_refuses_cleanup*`, `/*/*/RemoteScriptContractTests/C1208_Recorded_network_inspect_failure_refuses_cleanup*` | PENDING SourceLanding Mutation |
| PC-13 | production PC-09 label / PC-10 options ledger isolation; PC-32 residue isolation | `/*/*/RemoteScriptContractTests/C1208_Owned_packages_registration_survives_production_controls*` | PENDING SourceLanding Mutation |
| PC-14 | dangling/malformed network record and record-text residue | `/*/*/RemoteScriptContractTests/C1208_Dangling_network_record_retains_residue*`, `/*/*/RemoteScriptContractTests/C1208_Invalid_network_record_retains_text*` | PENDING SourceLanding Mutation |
| PC-15 | positive-absence marker clearing and receipt | `/*/*/RemoteScriptContractTests/C1208_Confirmed_absent_network_record_is_cleared*` | PENDING SourceLanding Mutation |
| PC-16 | container creation name/image/created comparison | `/*/*/RemoteScriptContractTests/C1208_Recorded_creation_fingerprint_is_reverified*` | PENDING SourceLanding Mutation |

## Round 2 completion

Final ordinary verification at 528839f5e5410a444fa5c9e102ed820152578d72: all 40 CP-33..72 rows passed, 58 executions, 54 distinct methods, 0 failed/skipped; CP-44 exactly 19. R-1..21 and V-1..19 all passed; none deferred. Independent fresh TRX roster/count audit and SHA-bound receipt validation passed. Full result, unedited CHECKPOINT lines, red proofs and source bindings: 2026-10-13-card-1208-finalizer-repair-evidence.md. The first attempted full run was stopped during its build for a known missing create-path model field and gets no verification credit. Source stayed frozen until stop completed; a new committed model fix preceded the full green run. SourceLanding Mutation (all PC-1..16 variants plus inherited CARD-1192 PCs) and rollout W-1 remain pending. Next: Review. Restart: none.

## Round 3 F4 repair binding

Code/landing owner: 15a9381e-0e9d-46a0-a49b-9534d34f3eb4. Branch feat/card-task-15a9381e. Base a2aa93e6cff6f34a1b48c96a89abb41def2cbea6; predecessor owner 8c713d0c-fe3f-4097-894b-9fc892a709ae. Restart: none.

V-20/CP-73 binds `C1208_Fingerprint_acquisition_refuses_unowned_paths`: hand-written table rows file-link (out-of-case victim), directory-link, dangling-link, regular unowned file, fresh acquisition, and a victim link introduced during inspection. Collision rows require exit 2, named FixtureFingerprintAcquisitionRefused, retained record and untouched target; fresh requires exit 0 and exact acquired fingerprint/CID. The same fixture runs through build-slot before production edits, establishing the file-link red on the unchanged base production script without a second test-assembly build. This diagnostic is an explained unlisted script run. Final closed selection CP-33..73: 41 rows, 64 executions, CP-44 exactly 19; no Unit or full class per brief. One isolated test build; explained checkpoint-tool bootstrap uses bin-c1208f4-tool/ and UseAppHost=false. No repeated green proof.

PC-17 variants: remove pre-existing-path refusal (file/directory/dangling links and regular), restore following/truncating redirection (including inspection-time collision), omit fresh fingerprint publication; detecting method `/*/*/RemoteScriptContractTests/C1208_Fingerprint_acquisition_refuses_unowned_paths`. Every variant PENDING method-scoped SourceLanding Mutation. PC-1..16 and inherited CARD-1192 PC-1..174 remain pending; post-land Mutation was NOT run. W-1 native Docker acceptance remains pending with rollout owner. Other cards out of scope.
