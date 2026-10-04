# CARD-0849 / CARD-0913: Seed contract verification design

Date: 2026-10-04. Inspected base and parent Plan commit:
`598fe19c440bce58a1bd1a54585ddf0c93ffa4a0`.

This is the executable TestDesign appendix to
[the landed fix plan](2026-10-04-card-0849-net9-packs-seed-contract-plan.md).
D-1 through D-8 and S1 through S3 remain the fix design. This file supersedes
**only** that plan's provisional checkpoint proposal and verification estimates.
Commission Code with this file's single Checkpoints table, not the parent table.
This task changes documentation only; no build, test, Docker operation, SSH,
Seed, Prune or deployment is represented as executed.

## Verification design

### Inspection

The following bodies, not just their names, were inspected at the base.

| Bodies read | Boundaries -> coverage |
|---|---|
| `RemoteScriptContractTests.cs`: all 18 C849 methods; all 9 C912 methods; both C905 methods; all 3 C973 methods and their Arguments | Import, volume identity, maintenance, cold reuse, front door, pruned seed-image readers -> V-2..V-7, R-1, R-3, R-4 |
| Same file: `CacheSeedTreeHarness`, `CachePruneHarness`, `CachePrepareHarness`, `CacheStatusHarness`, `ColdSeedHarness`, `C973ReaderHarness`, `Block`, `LinuxShell`, `PrepareLinuxShellScript`, capability guards and front-door process/cleanup bodies | Real-shell extraction, fake boundaries, LF, 60-second process timeout and WSL path conversion -> all shell tests; no arbitrary timeout increase |
| `CodexRunnerImageContractTests.cs`: net9 method, `Run`, `Order`, `Read`; `DockerStackDocuments.Stages/Service/Closure`; image verifier `result`, `need_uid`, `net9-offline` body | Pins, selected image stage, restore/build/native result -> V-1, V-8 |
| `verify-card0660-codex-image.ps1`: setup/provenance, volume creation, init/probe argument construction, row grading and cleanup | Real mounted-cache qualification and wrapper boundary tests -> V-1, V-8 |
| `c590-remote.sh`: c849 volume/prepare/mount/smoke, status/donor, saved-copy/tree validation, complete cold proof/probe/seed, full seed/ready, preview/budget/idle/reset/prune, all F-1..F-9 helpers, inventory/Both/Retired callers | All changed contracts plus cold invariants -> V-2..V-7, R-1, R-3; operational Fixture is Q-1 below |
| `c849-import-saved-donor.ps1`: Resolve/Check/Write entry, tar/directory two-pass reads, budgets, space and Unix mode normalization | Directory/archive mapping, unsafe entries and source immutability -> V-2, R-1; unchanged size/space limits retained |
| `verify-card0849-caches.ps1`: input checks, receipt reader, status checks, dispatch and every case summary | Exact typed receipts, native PowerShell and truthful reuse -> V-7, R-1 |
| `test-deploy-server2.ps1`: jq admission, Run-C727, all T groups and frozen totals; `c727-fake-verify.ps1` marker handoff, `c973-marker-reader.sh` and cold marker fixture; `C1008HostFixture` in `RollingVolumeRecycleScriptTests.cs` | Real reader with fake Docker/HTTP, admission ordering -> R-2/R-3; these do not prove live delivery |
| `c590-real.ps1` cache upload call sites; `scripts/lib/build-slot.ps1` lease entry; checkpoint manifest/driver owners including `RowRunner` command-shell selection | Existing two-file upload contract; one slot-owning layer; POSIX expansion only for Linux image row |

Owners read: `docs/testing-and-build.md` (manifest, source evidence, slots,
mutation, counts), `docs/docker-stack.md` (cache and rollout sections),
`docs/project-context.md`, and `docs/orchestration-loop.md` (stage handoff and
SourceLanding execution). No new backend, queue or session implementation is planned.

**Missing setup and fixture work, assigned to Code rather than hidden skips:**

- The inspected checkout has Bash, PowerShell, dotnet and Docker command paths;
  **jq is absent from PATH**. Docker daemon reachability, SDK availability and host
  lease access were not probed. Provision the documented real jq capability before
  Linux rows; do not substitute a predicate fake for jq. Windows needs native
  PowerShell plus WSL Bash/jq for the rolling marker reader. The Windows checkout
  is not reachable from this delegate. Run its rows on the Windows lane.
- Keep the six proposed new RemoteScript methods as six nonparameterized tests.
  Add the two image methods T8/T9 below to the existing image test file. Add
  `ParallelLimiter<ProcessSpawnLimit>` to each method that launches processes,
  including T7 when its tiny archive execution is added. No new production helper
  file or upload route: any shell helpers remain in `c590-remote.sh`.
- The nearest fixtures currently manufacture framework packages, and
  `CachePruneHarness` overrides `c849_require_ready`. Add a generic full-seed
  harness beside those helpers, using ordinary `c913.probe/1.0.0` and
  `c913.tools/2.0.0` versions, metadata, an executable sentinel, and the existing
  locally generated npm package. Keep a separate complete eight-field legacy
  fixture. Never retain a stub of the guard under test.
- Update existing framework-specific C849 assertions only for the schema-3
  path. Rename the existing `...unsafe_archives_missing_pack_and_busy_counters`
  method to `...unsafe_archives_empty_payload_and_busy_counters`; update the
  direct method reference in C905's guard test. No result-count change.
- `scripts/fixtures/c973-marker-reader.sh` currently builds a three-field
  pseudo-legacy marker. Replace that fixture's `full` branch with a genuine
  eight-field legacy marker and matching synthetic recovery/cache tree. Update
  its extracted-function dependency list if readiness gains helpers. Its
  `full-marker-unchanged` assertion must still succeed through real readiness.
  This is test-helper work in S2, not permission to loosen the new parser.
- C849/C912 existing fake jq status tests remain historical regression evidence.
  T1..T6 use real jq, real filesystem hashing, production shell predicates and
  production PowerShell parsing. They must carry assertion labels from the PC
  table even where an old method needs an added boundary case.
- LinuxShell returns stdout without asserting the shell exit. New harnesses
  must capture and assert every invoked production function's exit and diagnosis,
  plus an independent filesystem/side-effect witness. No trailing echo may hide
  an earlier command failure. Run potentially failing functions in bounded
  subprocesses; inject only the specific Docker/HTTP/process fault.
- CachePrepareHarness currently returns a canned root-type refusal and ignores
  parts of the chown/chmod command body. Execute the actual remapped root tests,
  find, chmod, copy and deletion bodies in its private tree; shim only privileged
  ownership facts. Otherwise PC-150/151 could survive with a green fake. Cold
  and Seed harnesses must shadow dotnet/npm/curl and unexpected pwsh invocations
  with local deny-and-record executables, including calls made through env.
  An injected package/network call must never launch real work from a PC.
- The helpers must materialize the source from the tested checkout on each run,
  keep LF bytes, use run-owned temp roots and retain before/after sentinel bytes.
  New fixture files, if needed for test data, stay under `scripts/fixtures/`;
  no additional production file is uploaded.

**Frozen evidence format used by the tests.** The parent permits a versioned
NUL serialization; bind it here so the tests have an independent oracle. Store
`recovery.manifest` immediately inside the retained recovery root. Its header is
`c849-manifest-v1` followed by NUL. Each record has five NUL-terminated fields:
entry type `D|F`, relative path including `packages/` or `npm/`, byte size,
three-octal-digit executable bits for files, and lowercase SHA-256 for files.
Directories use size `0`, executable field `-` and digest `-`.
Sort records by the complete relative path in C byte order. Include both root
directories, all descendant directories (including empty ones), and every
regular file. Directory paths have no trailing slash; the two root records are
named packages and npm. Exclude scratch and the manifest itself. Reject duplicate paths,
absolute/dot/traversal paths, CR/LF, invalid types, malformed lengths/digests and
unterminated/extra fields. Owner, timestamps and read/write-bit normalization
are deliberately outside the digest; regular-file execute bits are inside.

T2 constructs a golden byte stream independently from a fixed list of records,
hashes it with .NET SHA256, and compares its bytes/digest with the shell result.
It also compares two equal trees created in different orders, with different
mtime/ownership-normalization inputs. Do not use the production manifest writer
to generate the expected result.

New full markers have exactly 14 keys: schema, kind, source-sha, image,
donor-type, donor, time, packages, scratch, npm, package-bytes, npm-bytes,
recovery, manifest-sha256. donor-type is live, saved-tar or saved-directory;
donor is a full 64-hex container ID for live, or saved for the two saved types.
The inspected helper/recovery image is an immutable sha256 ID. Sizes are
nonnegative decimal integers and time is a UTC ISO timestamp.
Legacy markers retain exactly their eight existing fields; they do not acquire
schema or source-sha. Cold remains exactly its existing eight-field schema-2
format. No new full marker writes payload-sha256/reference-sha256.

Keep the legacy `seed-kind.txt`/value files only as compatible projections;
add an exact `seed-contract.txt` with five single LF-terminated lines:
`schema=legacy|2|3`, `kind=full|cold`,
`digest-type=payload-sha256|none|manifest-sha256`,
`digest=<64 lowercase hex>|none`, `smoke=passed|not-run`.
Only tuples legacy/full/payload, 2/cold/none, 3/full/manifest are valid.
A reuse Seed records not-run; a freshly smoked full Seed and full Both/Retired
record passed only with that operation's matching smoke receipt. Cold records
not-run. Check the entire field set and tuple, not substring matches.
Neither the manifest nor package names/content go into exported receipts.

### Delivery inventory

**New or changed asynchronous delivery paths: zero.** Seed is a synchronous
filesystem import; marker publication is an acceptance boundary, not a session
message. No queue, outbox, producer notification, retry worker or session input
is changed, so a new producer-to-recipient queue test and UserPrompt assertion
are inapplicable. There are zero delivery guards needing additional PCs beyond
the filesystem recovery guards below.

The data handoffs that must be observed are nevertheless explicit:

| Producer -> destination | Durable identity / persistence | Failure and recovery | Recipient evidence |
|---|---|---|---|
| Saved archive or stopped owned donor -> private stage -> package/npm volumes | Full source SHA + donor type/ID + inspected image ID + three volume names + manifest SHA-256 | Failed copy/npm validation/import comparison publishes no marker; saved source unchanged; explicit Reset is needed for imported unmarked contents | T1/T2 independently read and hash actual destination bytes/modes; a docker cp/run success is insufficient |
| Verified stage -> confined recovery -> ready marker -> later readiness/Prune | Same manifest SHA-256, recovery path, image ID and typed schema | Fail/interrupt before each move, smoke, donor restart, identity check, reconnect and marker rename; new process refuses incomplete state; no auto-resume promise | T2/T3/T4 read retained files and marker with real parser, then independently verify bytes; post-publication restart accepts only the complete contract |
| Image build -> isolated qualification child -> exported row | Reviewed source SHA + immutable image ID + target + fresh run-owned cache/scratch names | Build/setup failure is exit 2; restore/build/run/row failure is nonzero; retries use fresh owned resources | CP-6 executes the native binary with exact stdout, network none and empty mounted caches; metadata/tag or a reported ok row alone is insufficient |
| Retained cache -> recreated fixture consumer | Exact owned volume identities + sentinel digest | Force-recreate/down -v, then create/read through the other consumer | Q-1 F-7 reads ordinary package/npm/scratch sentinel bytes after recreation |

Substitutes: local Docker/HTTP fakes prove predicates, order, refusal and cleanup
scope, but cannot prove mount implementation, NuGet resolution, actual image
contents, broker state or live admission. Source text asserts installation
instructions, not a built image. CP-6 supplies the native recipient evidence;
Q-1 supplies real recreation evidence. The existing rollout Plan canary stays
a separate operational gate: its task/session identity must join to the matching
**complete UserPrompt transcript** and completed caller receipt. Queue insert,
ack, request, event, Sent flag and healthy status do not prove that canary.
This appendix neither runs it nor replaces it with the offline rolling harness.

### Proves it works now

Method keys below are exact bindings, not extra tests or wildcard families.
All eight new methods are single-result tests. Tests T1..T6 execute success
before their refusal matrices so fixture/setup failures cannot masquerade as reds.

| Key | Exact method |
|---|---|
| T1 | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` |
| T2 | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` |
| T3 | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` |
| T4 | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` |
| T5 | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` |
| T6 | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` |
| T7 | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` |
| T8 | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` |
| T9 | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` |
| A1 | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` |
| A2 | `RemoteScriptContractTests.C849_Cache_prepare_is_idempotent_and_preserves_payloads` |
| A3 | `RemoteScriptContractTests.C849_Seed_refuses_invalid_donors_and_partial_payloads` |
| A4 | `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` |
| A5 | `RemoteScriptContractTests.C849_Saved_donor_rejects_declared_size_bomb_before_writing` |
| A6 | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` |
| A7 | `RemoteScriptContractTests.C849_Prune_preview_is_read_only_and_bounded` |
| A8 | `RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance` |
| A9 | `RemoteScriptContractTests.C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo` |
| A10 | `RemoteScriptContractTests.C849_Cache_cases_use_only_the_validated_host_lane` |
| A11 | `RemoteScriptContractTests.C849_Cache_receipts_exclude_credentials_and_payloads` |
| C1 | `RemoteScriptContractTests.C912_Cold_volumes_seed_accepts_busy_main_without_packages` |
| C2 | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` |
| C3 | `RemoteScriptContractTests.C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries` |
| C4 | `RemoteScriptContractTests.C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit` |
| C5 | `RemoteScriptContractTests.C912_Cold_marker_has_distinct_validation_and_full_context_refusal` |
| C6 | `RemoteScriptContractTests.C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads` |
| C7 | `RemoteScriptContractTests.C912_Cold_seed_refuses_when_created_volume_disappears_before_init` |
| C8 | `RemoteScriptContractTests.C912_Cold_seed_with_unrelated_bind_initializes_only_three_labelled_roots` |
| C9 | `RemoteScriptContractTests.C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers` |
| C10 | `RemoteScriptContractTests.C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune` |

| ID | Behaviour / layer | Test or command | Decisive expected result |
|---|---|---|---|
| V-1 | Installed image can build/run native net9 offline | T7 plus CP-6 real Docker qualification | All three exact 9.0.20 packs visible after cache mounting, SDK 10 policy, uid 1654, restore=0/build=0/native-run=0 and exact token; 11/11 image rows pass |
| V-2 | Generic full import / real shell + saved importer | T1 | Ordinary-only complete versions accepted; incomplete version removed only in stage; all-incomplete/empty -> CacheDonorPackagesEmpty; file-as-version -> CacheDonorVersionInvalid; unsafe path/entry refusals leave sibling and source unchanged |
| V-3 | Full recovery evidence and publication / real shell/filesystem with external boundaries faked | T2 | Golden manifest bytes/digest, all imported bytes and execute bits match before smoke/retain/reconnect/atomic ready. Every handoff fault leaves no accepted marker, except a crash after completed atomic publication, which reopens as valid unchanged evidence |
| V-4 | Typed readiness / real shell parser and filesystem | T3 | Exactly three marker formats accepted, invalid records refused; schema3 recovery mutations fail; schema3 live-cache churn succeeds; legacy cache payload change fails; no format rewrite |
| V-5 | Prune recovery/authority / real shell destructive commands confined to temp directories | T4 | All preflight faults leave every target sentinel intact and no delete trace; schema3 successful prune has ordinary refill, native smoke and final budget receipt, roots/recovery retained, admission held, zero framework-only copy |
| V-6 | Donor-independent Fixture/inventory / real predicates and command trace | T5 | No production payload read; absent temp accepted only after complete successful census + explicit retired status; lookup errors refuse; only run-owned cleanup; intended negative diagnostics required |
| V-7 | Front-door receipt grading / native pwsh on Linux and Windows | T6 | All valid typed tuples accepted; schema/type/value mismatch, duplicate/missing fields, toxic content, false smoke and incomplete Fixture roster rejected |
| V-8 | Qualification guards / local inherited shell/pwsh | T8/T9 | Real probe branches and wrapper argument/receipt logic reject each bad boundary; no Docker/socket/network use by these methods; these tests complement, never replace, V-1 |

**Bounded case matrices (internal cases, not TUnit result counts):**

- T1/T2: live synthetic donor, saved tar and saved directory, each with ordinary
  complete packages and no framework packages; repeat the acceptance path with
  unrelated framework packages present. Exercise one complete plus incomplete,
  all incomplete, zero versions, empty metadata, missing metadata, directory/file
  layout errors, two supported archive-root aliases normalizing to the same path,
  and archive/source write attempts. Complete means the existing generic version
  completion metadata contract, not a new publisher-signature claim.
- T1 unsafe cases: absolute path, leading/interior/trailing dot and dot-dot,
  CR/LF, symlink (including dangling), hardlink and FIFO. Saved archive also has
  duplicate normalized entry, nested traversal, declared over-budget length and
  unsupported tar type. Device/socket entries use the same real forbidden-type
  predicate with test-owned inputs where permitted; no privileged mknod is needed.
- T2 manifest: mutate package file, npm file, size, execute bits, rename, remove
  and add an entry separately; reorder creation and vary mtimes without changing
  expected manifest; add scratch files and leave digest unchanged. Import faults
  occur independently in packages and npm, including correct command exit with
  corrupted destination bytes. Hash equality in a generated receipt is not enough.
- T2 handoffs: before donor stop; after each donor/stage copy; npm verification;
  each import; import comparison; smoke; stage-to-recovery move; restart; donor-ID
  readback; reconnect; temporary-marker write; final atomic rename. For each
  boundary inject a command failure, and terminate the **owned child shell**
  after its trace barrier before the next step. Reopen state in a fresh process
  through real readiness. Before publication there is no ready marker; after
  atomic publication, reopening succeeds without rewriting marker/recovery.
  Partial imported cache is refused until the existing explicit Reset gates hold.
  A crash need not restart the donor automatically; it must not claim ready or
  release admission. Saved source digest is identical after every case.
- T2 admission: live donor with each nonzero/unknown counter refuses before stop;
  unknown process census and one active uid-1654 writer refuse. A busy main with
  an idle drained live donor is accepted. Saved-source maintenance instead
  requires both runners idle and all containers detached; change those facts
  after staged copy and before each import to test the repeated gates.
- T3: valid legacy, cold2 and full3 through readiness/reuse/Both/Retired;
  cold2 through Reset/Prune/saved maintenance refuses CacheFullSeedRequired.
  Cross schema 2/3/unknown with full/cold, duplicate keys, missing each required
  key, extra legacy/full digest fields, malformed SHA/image/time/size, symlink
  markers, symlink recovery, missing recovery/manifest, duplicate/unsafe manifest
  records and a one-byte manifest change. Independently change schema3 recovery
  packages/npm while keeping live cache equal, and live cache while recovery
  remains equal. Legacy missing/changed cached host still refuses; valid legacy
  marker bytes remain unchanged even across source SHA change.
- T4: preview age -1/0/3600/3601 seconds, wrong source/run, modified preview hash,
  changed current facts; first root valid and second root invalid (with sentinel
  in the first), nested mount/unsafe entry, busy/unknown status/process/broker and
  late attachment. Cover selected roles packages/scratch/npm independently and
  together at 79/80/99/100/101 percent; below/at 20 GiB headroom. Schema3 and legacy
  successful paths are separate. Independently fail restore, npm, refill receipt,
  smoke and final budget gate. No success receipt or admission clear on failure.
  Preserve the existing threshold distinction: preview classifies 100 percent
  as OVER, but the admission budget gate refuses strictly above budget; 80
  percent selects a prune candidate. Inject clock/df facts, not real waiting or
  disk filling, to hit the exact age/headroom boundaries.
- T5 inventory: main present required; temp present and nonretired succeeds;
  temp absent and explicitly retired succeeds; absent/nonretired, omitted or
  malformed status, failed/partial census, failed inspect, duplicate identities,
  retired status with a running container all refuse. Read-only census of
  production identities is allowed; any package-payload read is a test failure.
  Occupied fixture names and foreign cleanup ledger entries preserve sentinels.
- T6 full tuple cross-product of legacy/full/payload, 2/cold/none, 3/full/manifest
  in Both (three same-tuple successes, six mixed-pair refusals); equal 64-hex
  values with wrong schema/type still refuse. Missing/duplicate fields and
  unequal values tested independently. Seed new versus reuse, Both and Retired
  test exact smoke claim and evidence. A credential/package-name sentinel is
  appended to input observations and must never appear in accepted summaries.
- T8/T9 run copied source bodies with only fixed absolute filesystem roots
  remapped to temp roots and local executable shims for id/dotnet/git/docker.
  Extract the existing net9 case, c849_smoke heredoc and PowerShell functions by
  exact anchors. Do not mock result, predicates, argument construction or graders.
  Shim dotnet records full environment, project/config bytes and invocation order;
  it produces an owned executable whose exit/stdout can fail independently.
  Mask host/core/ASP.NET pack trees one at a time, then combine a missing pack with
  a warm framework cache. Warm package, scratch and home cases refuse *before*
  restore. Restore/build/non-executable/native-exit/token mismatch and framework
  package appearing afterward all refuse. Wrapper cases use real argument-list
  creation and enforce owner/tag/source, network, uid, mounts, no sensitive binds,
  exact row once, child exit, and cleanup on partial volume-create failure.
  The tiny T7 archive harness executes the Dockerfile's hash/extract chain against
  a local tar: only archive URL/path, output root and test digest are substituted.
  An altered archive must not reach tar. Static assertions separately preserve
  the real vendor version/hash/member list.

Every PC assertion label below is a required executable assertion, including
labels newly added to existing methods. Assert the diagnosis/exit plus the
observable filesystem or trace outcome described here. These additions are
part of S0/S2; they are not permission to add more top-level tests silently.

**Red-first order.** Add T1..T6 and T8/T9 plus T7 extensions and their helpers
in a tests-only S0 commit; keep production at the parent commit. Commit/push,
then run CP-1 and CP-2. Expected red witnesses, one per method:
T1 ordinary-only-accepted; T2 schema3-published; T3 schema3-valid;
T4 schema3-prune-success; T5 absent-temp-accepted; T6 schema3-receipt-accepted;
T7 aspnet-framework-reference; T8 sources-and-fallbacks-cleared;
T9 package-mount-contract. These are assertions about existing executable
branches or emitted data, not missing-function/build errors. Add each success
assertion before later new-helper extraction so the old source fails at that
witness. Record exact assertion reds, 6 and 3 executed respectively. The
checkpoint driver exits 1 intentionally; run these rows separately from green
groups and classify them as regression demonstrations, never acceptance.

Implement S1..S3 in order, committing and pushing each slice; complete the
producer/readers/Prune migration together before ordinary acceptance. CP-3..CP-8
run only on the complete committed source. New tests must then pass without
guard stubs. Red-first proof is not Mutation: do not deliberately damage fixed
production code in Code. Review judges the PC design before land; SourceLanding
Mutation later performs the compiling defects below.

### Guards the regression

- R-1: All 18 C849 and all 9 C912 methods in RemoteScriptContractTests, including
  the renamed archive method. Decisive assertions: no unsafe stop/delete, exact
  volume preservation, legacy recovery retained, source-specific authority,
  cold busy-main allowance, all eight existing-volume masks, no package/network/
  lease call, each repeated proof P0..P6, and cold full-context refusal. Add
  missing assertion cases required by the PC table to these existing methods.
  The guards protect the source behavior, not only a copied implementation.
- R-2: `pwsh -NoProfile -File scripts/test-deploy-server2.ps1 -RequireJq`
  on both lanes. Exactly 24 groups, 66 invocations, 227 assertions, failures=0;
  no C973_SKIPPED. Preserve T-1 verify-before-clear, T-7 busy refusal,
  T-11 held verification failure, T-17/T-24 no implicit saved import, T-20
  cold marker after seed-image prune, T-21 race and T-22 failed census refusal.
  This unchanged deployment state machine is regression scope; its independent
  rollout/authentication guards are owned by its existing plans and are not new
  Seed implementation or delivery claims.
- R-3: All three C973 methods: six reader Arguments + five helper Arguments +
  one full-context method = **12 executed results**. Real cold readiness still
  works when the seed image is gone; fallback refuses foreign/no main/no image;
  retirement retains its independent read-only cache-preservation behavior.
- R-4: Both C905 methods = **2 results**, because a renamed method is referenced
  directly and Linux shell execution must be guarded before unavailable pwsh.
  These capability checks do not turn a skipped selected test into acceptance.

Ordinary Linux script roster: 18 + 9 + 12 + 2 + 6 = **47** results.
Linux image selection: T7/T8/T9 = **3**. Windows receipt selection: existing
C849 front door, existing C912 cold verification, T6 = **3**.
Red-first results (6 + 3) are reported separately from the 53 green results.
No repeat factor is configured. Internal loops/fault variants never raise Min.

**Q-1: post-Review operational Fixture (separate from ordinary Code rows).**
After the qualified image is available in the trusted host lane, the caller
runs `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha $env:C913_SHA`.
Its image ID must match the qualified image; keep read-only inventories of main
and temp, allowing proven retired/absent temp. No saved archive, live package
donor or live Seed/Prune is needed. The Fixture uses its existing build leases
for broker-connected drivers; do not put the whole Fixture under an extra lease.

Freeze **9 groups, 32 control families, 47 executed negative variants**:
families are the fixture's existing `CONTROL PC-01` vocabulary, distinct from
the SourceLanding PC-n IDs in this document. Emit/check exact family and variant
sets in both shell evidence and the front door. Add `variants=47` and
`expected-red-variants=47` to its allowlisted summary. `expected-red=32`
continues to count families. Never count 47 as a TUnit execution floor.

| Fixture families | Frozen cases | Variants |
|---|---|---:|
| 01..11 | Existing compose external/name/env/private/mount, uid/mode/unsafe-root/labels/options, donor identity | 11 |
| 12 | All ordinary versions incomplete -> CacheDonorPackagesEmpty | 1 |
| 13 | Sole version missing metadata; sole version empty metadata -> CacheDonorPackagesEmpty | 2 |
| 14 | Relative traversal; absolute path -> CacheDonorUnsafePath | 2 |
| 15 | Symlink; hardlink; FIFO -> CacheDonorUnsafeEntry | 3 |
| 16..17 | Unmarked attached cache; private scratch lock negative | 2 |
| 18 | Mask host; core reference; ASP.NET reference pack individually in disposable children with empty cache | 3 |
| 19..26 | Existing npm miss, nonexternal loss, busy prune, stale preview, invalid target, budget, missing recovery, toxic evidence | 8 |
| 27 | Warm packages; warm scratch; warm home rejected before restore | 3 |
| 28 | Changed package; changed npm; changed executable bit; altered manifest -> CacheRecoveryChanged | 4 |
| 29 | Missing recovery; missing manifest -> CacheRecoveryMissing | 2 |
| 30 | Cold plus manifest field; schema3 plus legacy payload field -> CacheSeedMarkerInvalid | 2 |
| 31 | Failed census; failed inspect; missing retired status are refused, never absence | 3 |
| 32 | Inject foreign name in owned cleanup ledger; intercept commands before execution and verify it is excluded and outside sentinel remains | 1 |

F-3 uses generated ordinary packages and real full evidence, F-5 positively
executes native net9 from an empty package/scratch pair, F-8 uses schema3
manifest recovery. F-4/F-6/F-7 keep their real lock/npm/retention assertions.
For family 18, use a read-only empty bind over exactly one installed pack
directory in a disposable child. Confirm mount/uid/image setup, require the
specific missing pack diagnosis (or NU1101/NU1102 naming that exact pack at
restore), and verify unmasked siblings succeed. Lease/setup errors are not
negative evidence. Never delete a standing runner's pack. Family 32 uses a
local command interceptor so even a broken cleanup predicate cannot remove a
production resource; its wrapper emits FixtureCleanupScopeHeld only after
observing the production cleanup reject/skip that target.

New Q-1 families deliberately exercise bad inputs and count as Fixture controls,
not SourceLanding mutation completion. Preserve the revised Fixture receipt
and source/image identity with the rollout record. Estimated host execution:
30 minutes; live rollout/canary time is excluded from ordinary V/R and Mutation.

### Guard inventory

The inventory covers the changed Seed/image/receipt contract and the cold,
archive and maintenance safety assertions relied on here. Separate source
sites or independently removable checks get separate rows; enum/value
variants of a single parser guard remain a matrix within its exact method.
Inherited deployment internals outside these source boundaries are R-2 only.
No guard in this inventory is intentionally untested.

| Guard | Plan ref + safety-critical guard/invariant | Positive control |
|---|---|---|
| G-1 | D-1: Pinned SDK archive version/hash pair | PC-1 |
| G-2 | D-1: Archive digest gates extraction | PC-2 |
| G-3 | D-1: Host pack extraction | PC-3 |
| G-4 | D-1: Core reference extraction | PC-4 |
| G-5 | D-1: ASP.NET reference extraction | PC-5 |
| G-6 | D-1: Packs copied into final SDK after SDK 10 copy | PC-6 |
| G-7 | D-1: Default SDK policy stays 10; no full SDK 9 installation | PC-7 |
| G-8 | D-2: Qualification package mount at actual cache destination with nocopy | PC-8 |
| G-9 | D-2: Qualification private scratch mount with nocopy | PC-9 |
| G-10 | D-2: Network disabled in qualification child | PC-10 |
| G-11 | D-2: Native child uses uid/gid 1654 | PC-11 |
| G-12 | D-2: Initialization restricted to this run's owned roots | PC-12 |
| G-13 | D-2: Child has no socket, provider home, credentials or phone-home | PC-13 |
| G-14 | D-2: Package cache starts empty | PC-14 |
| G-15 | D-2: Scratch starts empty | PC-15 |
| G-16 | D-2: CLI/NuGet home starts empty | PC-16 |
| G-17 | D-2: Image host pack required independently of package cache | PC-17 |
| G-18 | D-2: Image core reference pack required | PC-18 |
| G-19 | D-2: Image ASP.NET reference pack required | PC-19 |
| G-20 | D-2: Restore clears sources and fallback folders | PC-20 |
| G-21 | D-2: Audit/workload update traffic disabled | PC-21 |
| G-22 | D-2: net9 RID/runtime/native framework-dependent project contract | PC-22 |
| G-23 | D-2: ASP.NET reference is compiled, not just declared | PC-23 |
| G-24 | D-2: Restore failure cannot pass | PC-24 |
| G-25 | D-2: Build failure cannot pass | PC-25 |
| G-26 | D-2: Native execution and exact stdout are necessary | PC-26 |
| G-27 | D-2: Post-run framework-cache check | PC-27 |
| G-28 | D-2: Image identity/source provenance gates verdict | PC-28 |
| G-29 | D-2: Image receipt requires successful child exit | PC-29 |
| G-30 | D-2: Image receipt requires the matching exact row, once | PC-30 |
| G-31 | D-2: Qualification cleanup confined to owned created volumes | PC-31 |
| G-32 | D-2: Deployment smoke validates inherited cache environment before private probe | PC-32 |
| G-33 | D-2: Deployment smoke retains build lease | PC-33 |
| G-34 | D-2: Offline qualification does not acquire an unreachable second lease | PC-34 |
| G-35 | D-4: Complete ordinary packages accepted without framework packages | PC-35 |
| G-36 | D-4: Incomplete versions removed only from stage | PC-36 |
| G-37 | D-4: At least one complete version remains | PC-37 |
| G-38 | D-4: Package/version layout validated | PC-38 |
| G-39 | D-4: Relative paths confined | PC-39 |
| G-40 | D-4: Symlinks rejected | PC-40 |
| G-41 | D-4: Hardlinks rejected | PC-41 |
| G-42 | D-4: Special entries rejected | PC-42 |
| G-43 | D-4: npm integrity runs before manifest/import | PC-43 |
| G-44 | D-4: Manifest includes packages and npm | PC-44 |
| G-45 | D-4: Manifest binds regular-file bytes and size | PC-45 |
| G-46 | D-4: Manifest binds executable bits separately from owner/write bits | PC-46 |
| G-47 | D-4: Manifest serialization sorted and unambiguous | PC-47 |
| G-48 | D-4: Manifest parser rejects duplicate records | PC-48 |
| G-49 | D-4: Manifest parser rejects unsafe path/type/record shape | PC-49 |
| G-50 | D-4: Imported package bytes compared before ready | PC-50 |
| G-51 | D-4: Imported npm bytes compared before ready | PC-51 |
| G-52 | D-4: Smoke precedes recovery acceptance/publication | PC-52 |
| G-53 | D-4: Recovery retention succeeds before publication | PC-53 |
| G-54 | D-4: Live donor restart succeeds before publication | PC-54 |
| G-55 | D-4: Restarted donor identity remains exact | PC-55 |
| G-56 | D-4: Reconnect status succeeds before publication | PC-56 |
| G-57 | D-4: Ready publication is atomic | PC-57 |
| G-58 | D-4: Saved source remains unchanged | PC-58 |
| G-59 | D-4: Interrupted import cannot become ready by rerun | PC-59 |
| G-60 | D-4: Failure cleanup restricted to owned stage | PC-60 |
| G-61 | D-4: Live donor must be drained with three known zero counters | PC-61 |
| G-62 | D-4: Live donor process census must succeed | PC-62 |
| G-63 | D-4: Live donor has no app-uid package writer | PC-63 |
| G-64 | D-4: Saved import has no temp container | PC-64 |
| G-65 | D-4: Saved import rechecks drain authority after copy | PC-65 |
| G-66 | D-4: Saved import rechecks all attachments at handoffs | PC-66 |
| G-67 | D-5: Marker accepts exactly legacy full / schema2 cold / schema3 full | PC-67 |
| G-68 | D-5: Missing/mixed marker keys refused | PC-68 |
| G-69 | D-5: Duplicate marker keys refused | PC-69 |
| G-70 | D-5: Marker must be regular, nonempty and not a symlink | PC-70 |
| G-71 | D-5: Schema3 marker binds manifest digest | PC-71 |
| G-72 | D-5: Schema3 readiness revalidates immutable recovery bytes | PC-72 |
| G-73 | D-5: Recovery path is canonically confined | PC-73 |
| G-74 | D-5: Schema3 recovery image remains locally available | PC-74 |
| G-75 | D-5: Schema3 validates three fixed volume identities | PC-75 |
| G-76 | D-5: Shared cache churn allowed without changing recovery contract | PC-76 |
| G-77 | D-5: Legacy payload/recovery requirements remain isolated | PC-77 |
| G-78 | D-5: Legacy markers never silently upgraded | PC-78 |
| G-79 | D-5: Cold full-maintenance contexts refused | PC-79 |
| G-80 | D-5: Both requires equal schema | PC-80 |
| G-81 | D-5: Both requires equal digest type | PC-81 |
| G-82 | D-5: Both requires equal digest value | PC-82 |
| G-83 | D-5: Receipt parser rejects missing/duplicate/unknown fields | PC-83 |
| G-84 | D-5: Reuse never claims an unexecuted smoke | PC-84 |
| G-85 | D-5: Full Both/Retired require successful smoke evidence | PC-85 |
| G-86 | D-5: Cold Seed/Both/Retired remain package-free | PC-86 |
| G-87 | D-6: Prune refuses missing recovery before deletion | PC-87 |
| G-88 | D-6: Prune refuses changed recovery before deletion | PC-88 |
| G-89 | D-6: Prune refuses missing retained rollback image | PC-89 |
| G-90 | D-6: Preview source/run identity must match | PC-90 |
| G-91 | D-6: Preview age bounded including future timestamps | PC-91 |
| G-92 | D-6: Preview digest protects saved volume facts | PC-92 |
| G-93 | D-6: Current volume facts must equal preview | PC-93 |
| G-94 | D-6: Whole target set validated before first delete | PC-94 |
| G-95 | D-6: Prune target path/type/mount confinement | PC-95 |
| G-96 | D-6: Prune retains roots/recovery while clearing selected contents | PC-96 |
| G-97 | D-6: Schema3 Prune does not refill framework NuGet packs | PC-97 |
| G-98 | D-6: Legacy Prune retains bounded framework recovery | PC-98 |
| G-99 | D-6: Ordinary refill success required | PC-99 |
| G-100 | D-6: Refill receipt required | PC-100 |
| G-101 | D-6: Prune final smoke required | PC-101 |
| G-102 | D-6: Post-refill budgets gate success | PC-102 |
| G-103 | D-6: Maintenance holds admission | PC-103 |
| G-104 | D-6: Maintenance rejects busy/unknown counters | PC-104 |
| G-105 | D-6: Maintenance rejects app-uid processes | PC-105 |
| G-106 | D-6: Maintenance rejects third-party cache attachments | PC-106 |
| G-107 | D-6: Maintenance requires empty available broker | PC-107 |
| G-108 | D-6: Reset cannot clear any accepted marker | PC-108 |
| G-109 | D-6: Reset rechecks stopped attachments before each clear | PC-109 |
| G-110 | D-7: Fixture never reads package payload from production donor | PC-110 |
| G-111 | D-7: Inventory permits absent temp only after successful census | PC-111 |
| G-112 | D-7: Inventory absent temp requires explicit complete retired status | PC-112 |
| G-113 | D-7: Inventory refuses contradictory retired/live-container observation | PC-113 |
| G-114 | D-7: Inventory inspect failure is not absence | PC-114 |
| G-115 | D-7: Fixture namespace must be fresh | PC-115 |
| G-116 | D-7: Fixture cleanup enforces exact run ownership | PC-116 |
| G-117 | D-7: Fixture summary checks exact group/family/variant roster | PC-117 |
| G-118 | D-7: Fixture controls need intended nonzero diagnosis | PC-118 |
| G-119 | D-7: Public receipts exclude sensitive/payload fields | PC-119 |
| G-120 | D-3: Cold Seed does no package work | PC-120 |
| G-121 | D-3: Cold Seed does not require idle main | PC-121 |
| G-122 | D-3: Cold proof requires complete census | PC-122 |
| G-123 | D-3: Cold proof requires successful inspect | PC-123 |
| G-124 | D-3: Cold proof rejects live/stopped cache attachments | PC-124 |
| G-125 | D-3: Cold proof rejects overlapping bind ancestors/descendants | PC-125 |
| G-126 | D-3: Cold temp retirement must be explicit | PC-126 |
| G-127 | D-3: Cold proof rejects omitted counters | PC-127 |
| G-128 | D-3: Cold preexisting roots must be empty including hidden files | PC-128 |
| G-129 | D-3: Cold rechecks at initialization/probe/publication boundaries | PC-129 |
| G-130 | D-3: Cold main ID stays stable | PC-130 |
| G-131 | D-3: Cold main image stays stable | PC-131 |
| G-132 | D-3: Cold main mount set stays stable | PC-132 |
| G-133 | D-3: Cold missing volume cannot be auto-created by init | PC-133 |
| G-134 | D-3: Cold uid probe must create/rename/delete successfully | PC-134 |
| G-135 | D-3: Cold probe timeout refuses | PC-135 |
| G-136 | D-3: Cold helper cleanup required | PC-136 |
| G-137 | D-3: Cold canary cleanup cannot write outside root | PC-137 |
| G-138 | D-3: Cold schema2 and full receipts stay distinct | PC-138 |
| G-139 | D-3: Cold reader uses available helper after seed image prune | PC-139 |
| G-140 | D-3: Cold helper fallback validates main ownership | PC-140 |
| G-141 | D-3: Cold readiness survives ordinary cache additions | PC-141 |
| G-142 | D-3/D-4: Exact cache names and role pairing | PC-142 |
| G-143 | D-3/D-4: Local volume driver | PC-143 |
| G-144 | D-3/D-4: Empty driver options | PC-144 |
| G-145 | D-3/D-4: Volume owner label | PC-145 |
| G-146 | D-3/D-4: Volume schema label | PC-146 |
| G-147 | D-3/D-4: Volume role label | PC-147 |
| G-148 | D-3/D-4: Root uid/gid ownership | PC-148 |
| G-149 | D-3/D-4: Root mode 0700 | PC-149 |
| G-150 | D-3/D-4: Root symlink/type validation | PC-150 |
| G-151 | D-3/D-4: Marked cache prepare preserves existing bytes/modes | PC-151 |
| G-152 | D-3/D-4: Unmarked attached roots refuse | PC-152 |
| G-153 | D-3/D-4: Mount identity and shared/private destinations checked | PC-153 |
| G-154 | D-3/D-4: Private tmp remains mode1777 | PC-154 |
| G-155 | D-4: Saved archive path traversal rejected | PC-155 |
| G-156 | D-4: Saved archive duplicate normalized paths rejected | PC-156 |
| G-157 | D-4: Saved archive unsupported entry kinds rejected | PC-157 |
| G-158 | D-4: Saved directory special files refused before opening | PC-158 |
| G-159 | D-4: Archive declared size bound checked before writes | PC-159 |
| G-160 | D-4: Saved import never retains setuid/setgid/sticky bits | PC-160 |
| G-161 | D-6: Preview is read-only | PC-161 |
| G-162 | D-6: Budget/headroom thresholds exact | PC-162 |
| G-163 | D-3/D-6: Privileged host root observation retained | PC-163 |
| G-164 | D-4/D-6: Remote host lane required | PC-164 |
| G-165 | D-3/D-6: Deploy prepares before mutation and verifies before admission | PC-165 |

### Positive controls

All rows are **pending executable designs**, not executed evidence. Each edit
is a compiling C#/PowerShell or syntactically valid shell/Dockerfile defect in
the actual production source. Keep the test/oracle unchanged. The binding key
identifies the exact class/method above; the final column is its exact assertion
label, which must appear in the failed assertion, not just a log.
When an existing test has a preceding generic source-shape assertion, order
the decisive labelled outcome first so a PC fails at its specified witness.
T7 keeps separate source-pin assertions and its executable archive-chain check;
the latter must grade bad-archive-no-extract before a missing-pipeline text check.

Use local inherited children only. SourceLanding never gives the snapshot to
Docker, SSH, an external executor or another worktree. T7/T8/T9 intentionally
have local source-execution seams so every PC below can run inside custody.
Their ordinary Docker companion CP-6 is necessary for actual image evidence,
but is not re-run from a sourced Mutation snapshot.

For each row: method-scoped baseline green, apply defect, same method red,
restore exact bytes and refresh timestamps, same method green. Run the
unchanged copied checkpoint driver with
`-Filter '/*/*/ClassName/ExactTestMethod'`, deriving ClassName/ExactTestMethod
literally from the key table. C9 and C10 are parameterized: use only their exact
method plus trailing `*`, MinExecuted 6 and 5 respectively; all others Min=1.
Use unique per-PC/per-phase `bin-c913-pc<N>-<phase>/` output and external evidence
directories. Every phase's build must exit 0; red driver exit=1, correct roster
and the listed assertion failure; baseline/restored driver exit=0, no failures/
skips. Exit 2/3, zero tests, a missing capability, syntax error or setup error
does not count. Code runs V/R; Review judges this design; Mutation runs
break/red/restore/green **after land**. No snapshot commit/push.

| PC | Break guard by this defect | Exact method | Expect red at assertion |
|---|---|---|---|
| PC-1 | Break G-1: Change one hex digit in NET9_SDK_SHA512 | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `pin-pair` |
| PC-2 | Break G-2: Replace the sha512sum pipeline with true while preserving valid shell syntax | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `bad-archive-no-extract` |
| PC-3 | Break G-3: Delete only the host-pack tar member and its post-extract test | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `host-pack-member` |
| PC-4 | Break G-4: Delete only the core-reference tar member and its post-extract test | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `core-ref-member` |
| PC-5 | Break G-5: Delete only the ASP.NET-reference tar member and its post-extract test | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `aspnet-ref-member` |
| PC-6 | Break G-6: Remove the final COPY --from=net9-packs instruction | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `packs-visible-after-sdk-copy` |
| PC-7 | Break G-7: Change the build-stage base from sdk:10.0 to sdk:9.0 | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `sdk10-policy` |
| PC-8 | Break G-8: Delete volume-nocopy from the net9 package mount | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `package-mount-contract` |
| PC-9 | Break G-9: Use the package volume as the scratch source | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `private-scratch-contract` |
| PC-10 | Break G-10: Change net9 child network from none to bridge | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `network-none` |
| PC-11 | Break G-11: Change only net9 child user to 0:0 | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `uid-1654` |
| PC-12 | Break G-12: Append a foreign volume to initialization arguments | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `owned-init-only` |
| PC-13 | Break G-13: Add a host Docker socket bind to the net9 child | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `isolated-child-allowlist` |
| PC-14 | Break G-14: Replace the package emptiness predicate with true | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `warm-packages-refused` |
| PC-15 | Break G-15: Replace the scratch emptiness predicate with true | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `warm-scratch-refused` |
| PC-16 | Break G-16: Replace the home emptiness predicate with true | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `warm-home-refused` |
| PC-17 | Break G-17: Bypass the host-pack existence/executable predicate | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `missing-host-refused` |
| PC-18 | Break G-18: Bypass only the core-reference predicate | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `missing-core-ref-refused` |
| PC-19 | Break G-19: Bypass only the ASP.NET-reference predicate | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `missing-aspnet-ref-refused` |
| PC-20 | Break G-20: Remove fallbackPackageFolders clear from generated NuGet.Config | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `sources-and-fallbacks-cleared` |
| PC-21 | Break G-21: Set NuGetAudit true in the restore invocation | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `audit-and-workload-disabled` |
| PC-22 | Break G-22: Set UseAppHost to false in the generated project | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `native-project-contract` |
| PC-23 | Break G-23: Remove the ASP.NET type use from Program.cs | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `aspnet-compiled` |
| PC-24 | Break G-24: Ignore a nonzero restore exit | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `restore-exit-refused` |
| PC-25 | Break G-25: Ignore a nonzero build exit | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `build-exit-refused` |
| PC-26 | Break G-26: Replace direct executable capture with a constant success token | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `native-run-required` |
| PC-27 | Break G-27: Remove the framework-package absence check after execution | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `downloaded-framework-refused` |
| PC-28 | Break G-28: Ignore image revision mismatch in SkipBuild branch | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `revision-mismatch-refused` |
| PC-29 | Break G-29: Accept ok text when child exit is nonzero | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `child-exit-required` |
| PC-30 | Break G-30: Accept any C660_ROW ok line for net9-offline | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `matching-row-required` |
| PC-31 | Break G-31: Broaden cleanup enumeration to all volume names | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `cleanup-owned-volumes-only` |
| PC-32 | Break G-32: Remove NUGET_SCRATCH inherited environment check in c849_smoke | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `smoke-environment-refused` |
| PC-33 | Break G-33: Run the deployment smoke driver directly instead of through build-slot.ps1 | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `smoke-lease-required` |
| PC-34 | Break G-34: Call build-slot.ps1 from the network-none probe | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `offline-no-inner-lease` |
| PC-35 | Break G-35: Restore the AppHostDonorMissing guard in generic tree validation | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `ordinary-only-accepted` |
| PC-36 | Break G-36: Remove incomplete version cleanup | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `incomplete-stage-only` |
| PC-37 | Break G-37: Return success when filtered complete-version count is zero | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `empty-full-refused` |
| PC-38 | Break G-38: Accept a regular file in place of a version directory | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `invalid-layout-refused` |
| PC-39 | Break G-39: Return success from c849_validate_seed_relative | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `unsafe-relative-refused` |
| PC-40 | Break G-40: Remove type l from staged-tree forbidden-entry search | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `symlink-refused` |
| PC-41 | Break G-41: Remove staged-tree links +1 predicate | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `hardlink-refused` |
| PC-42 | Break G-42: Remove type p from staged-tree forbidden-entry search | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `special-entry-refused` |
| PC-43 | Break G-43: Ignore nonzero staged npm cache verify exit | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `npm-before-manifest` |
| PC-44 | Break G-44: Enumerate only packages when generating the manifest | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `manifest-covers-npm` |
| PC-45 | Break G-45: Emit a constant file digest instead of hashing bytes | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `manifest-binds-bytes` |
| PC-46 | Break G-46: Emit constant 000 executable bits | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `manifest-binds-exec` |
| PC-47 | Break G-47: Remove LC_ALL=C sorted path ordering | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `manifest-order-independent` |
| PC-48 | Break G-48: Ignore duplicate manifest paths | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `duplicate-record-refused` |
| PC-49 | Break G-49: Accept an absolute manifest record path | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `unsafe-record-refused` |
| PC-50 | Break G-50: Skip comparison of imported packages with stage manifest | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `package-import-corruption-no-marker` |
| PC-51 | Break G-51: Skip comparison of imported npm with stage manifest | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `npm-import-corruption-no-marker` |
| PC-52 | Break G-52: Publish final marker immediately before calling c849_smoke | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `smoke-failure-no-marker` |
| PC-53 | Break G-53: Ignore failure moving stage to recovery | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `recovery-save-failure-no-marker` |
| PC-54 | Break G-54: Ignore docker start failure | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `restart-failure-no-marker` |
| PC-55 | Break G-55: Remove post-restart donor ID comparison | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `donor-id-change-no-marker` |
| PC-56 | Break G-56: Bypass final c849_status_zero reconnected gate | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `reconnect-failure-no-marker` |
| PC-57 | Break G-57: Write marker directly to C849_READY instead of temp plus rename | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `publication-failure-no-partial-marker` |
| PC-58 | Break G-58: Point incomplete-version removal at saved source instead of stage | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `saved-source-byte-identical` |
| PC-59 | Break G-59: Accept nonempty unmarked targets as a completed import | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `interrupted-import-held` |
| PC-60 | Break G-60: Replace confined stage condition with nonempty-stage only | `RemoteScriptContractTests.C849_Seed_refuses_invalid_donors_and_partial_payloads` | `cleanup-sibling-retained` |
| PC-61 | Break G-61: Bypass pre-stop c849_status_zero check | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `busy-donor-no-stop` |
| PC-62 | Break G-62: Convert failed donor ps call into empty output | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `unknown-process-no-stop` |
| PC-63 | Break G-63: Bypass active-process zero comparison | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `writer-no-stop` |
| PC-64 | Break G-64: Skip saved-branch c849_no_temp_containers | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `saved-temp-present-no-copy` |
| PC-65 | Break G-65: Remove post-copy c849_prune_idle call only | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `saved-late-busy-no-import` |
| PC-66 | Break G-66: Remove per-target c849_no_cache_attachments call | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `saved-late-attachment-no-import` |
| PC-67 | Break G-67: Treat schema=4 kind=full as schema3 | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `unknown-schema-refused` |
| PC-68 | Break G-68: Permit payload-sha256 in schema3 full | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `mixed-marker-refused` |
| PC-69 | Break G-69: Use first-value-wins for duplicate kind keys | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `duplicate-marker-refused` |
| PC-70 | Break G-70: Remove marker symlink rejection | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `symlink-marker-refused` |
| PC-71 | Break G-71: Skip manifest file SHA-256 comparison with marker | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `manifest-digest-refused` |
| PC-72 | Break G-72: Trust marker/manifest without rehashing recovery | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `recovery-bytes-refused` |
| PC-73 | Break G-73: Allow recovery path through a symlink outside cache root | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `recovery-escape-refused` |
| PC-74 | Break G-74: Ignore docker image inspect failure for retained image | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `recovery-image-refused` |
| PC-75 | Break G-75: Ignore wrong packages identity in schema3 marker | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `marker-volume-refused` |
| PC-76 | Break G-76: Compare mutable packages against recovery manifest on every readiness call | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `cache-churn-accepted` |
| PC-77 | Break G-77: Route legacy marker through schema3 cache-churn acceptance | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `legacy-payload-change-refused` |
| PC-78 | Break G-78: Rewrite a valid legacy marker as schema3 during readiness | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `legacy-marker-byte-identical` |
| PC-79 | Break G-79: Change full-required check to allow cold | `RemoteScriptContractTests.C912_Cold_marker_has_distinct_validation_and_full_context_refusal` | `full-context-refused` |
| PC-80 | Break G-80: Compare only kind and digest, ignoring schema | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `both-schema-refused` |
| PC-81 | Break G-81: Ignore digest-type when comparing full receipts | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `both-digest-type-refused` |
| PC-82 | Break G-82: Ignore digest value when comparing full receipts | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `both-digest-value-refused` |
| PC-83 | Break G-83: Accept duplicate schema fields by taking the first | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `receipt-shape-refused` |
| PC-84 | Break G-84: Print smoke=passed for full marker reuse | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `reuse-smoke-not-run` |
| PC-85 | Break G-85: Accept full Retired with no matching smoke-summary | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `full-smoke-evidence-required` |
| PC-86 | Break G-86: Call c849_smoke unconditionally in case_verify_runner_caches | `RemoteScriptContractTests.C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads` | `cold-no-smoke` |
| PC-87 | Break G-87: Bypass recovery existence guard | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `missing-recovery-no-delete` |
| PC-88 | Break G-88: Bypass recovery-manifest comparison immediately before deletion | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `changed-recovery-no-delete` |
| PC-89 | Break G-89: Bypass recovery image inspect | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `missing-image-no-delete` |
| PC-90 | Break G-90: Bypass preview source-sha comparison | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `stale-source-no-delete` |
| PC-91 | Break G-91: Remove preview age range check | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `stale-time-no-delete` |
| PC-92 | Break G-92: Skip volumes.txt digest comparison | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `preview-digest-no-delete` |
| PC-93 | Break G-93: Skip comparison of volumes-now.txt with preview | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `changed-volume-no-delete` |
| PC-94 | Break G-94: Move validation into per-target destructive loop | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `second-target-invalid-no-delete` |
| PC-95 | Break G-95: Remove nested-mount refusal in c849_prune_validate_tree | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `nested-mount-no-delete` |
| PC-96 | Break G-96: Replace content deletion with removal of volume root | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `roots-and-recovery-retained` |
| PC-97 | Break G-97: Execute legacy framework-copy branch for schema3 | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `schema3-no-framework-copy` |
| PC-98 | Break G-98: Skip legacy refill branch | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `legacy-refill-required` |
| PC-99 | Break G-99: Ignore nonzero ordinary refill exit | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `refill-failure-no-success` |
| PC-100 | Break G-100: Skip exact C849_REFILL receipt check | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `refill-receipt-required` |
| PC-101 | Break G-101: Skip main c849_smoke after refill | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `prune-smoke-required` |
| PC-102 | Break G-102: Skip final c849_budget_gate | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `over-budget-no-success` |
| PC-103 | Break G-103: Insert drain/clear mutation before success | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `admission-held` |
| PC-104 | Break G-104: Bypass live runner counter guard in c849_prune_idle | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `maintenance-busy-no-delete` |
| PC-105 | Break G-105: Bypass active-process guard in c849_prune_idle | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `maintenance-process-no-delete` |
| PC-106 | Break G-106: Permit foreign project/service attached container | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `maintenance-attachment-no-delete` |
| PC-107 | Break G-107: Ignore busy broker response | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `maintenance-broker-no-delete` |
| PC-108 | Break G-108: Remove CacheSeedAlreadyReady refusal | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `reset-marker-no-delete` |
| PC-109 | Break G-109: Remove per-target docker ps -aq recheck | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `reset-late-attachment-no-delete` |
| PC-110 | Break G-110: Restore production-donor docker cp in c849_fixture_apphost | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `no-production-payload-read` |
| PC-111 | Break G-111: Convert failed docker ps into empty successful census | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `failed-census-refused` |
| PC-112 | Break G-112: Accept absent temp with omitted retiredAt | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `unknown-status-refused` |
| PC-113 | Break G-113: Treat retired status as absence despite a found running container | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `contradictory-container-refused` |
| PC-114 | Break G-114: Convert docker inspect failure to container=absent | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `failed-inspect-refused` |
| PC-115 | Break G-115: Ignore existing owned-name volume at fixture setup | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `occupied-fixture-refused` |
| PC-116 | Break G-116: Remove the run-prefix check from volume cleanup | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `foreign-ledger-no-remove` |
| PC-117 | Break G-117: Accept duplicate control ID in place of missing expected ID | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `fixture-roster-refused` |
| PC-118 | Break G-118: Accept any failing command as an expected control red | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `setup-error-not-control-red` |
| PC-119 | Break G-119: Allow credential field through receipt projection | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `toxic-receipt-refused` |
| PC-120 | Break G-120: Insert dotnet restore in c849_cold_seed | `RemoteScriptContractTests.C912_Cold_volumes_seed_accepts_busy_main_without_packages` | `no-package-network-or-slot-call` |
| PC-121 | Break G-121: Add a main sessions==0 requirement | `RemoteScriptContractTests.C912_Cold_volumes_seed_accepts_busy_main_without_packages` | `main-unchanged` |
| PC-122 | Break G-122: Treat failed volume census as empty | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-census-error` |
| PC-123 | Break G-123: Treat failed container inspect as empty mounts | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-inspect-error` |
| PC-124 | Break G-124: Use docker ps -q instead of -aq in all-container census | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-stopped-mount` |
| PC-125 | Break G-125: Skip both canonical bind-overlap checks | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `cold-bind-overlap-refused` |
| PC-126 | Break G-126: Remove retiredAt condition from temp predicate | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-temp-unretired` |
| PC-127 | Break G-127: Remove has(runnerSessions) and its type condition | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-temp-counter-omitted` |
| PC-128 | Break G-128: Use glob-star emptiness test that misses dotfiles | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-hidden` |
| PC-129 | Break G-129: Remove only the P6 proof call | `RemoteScriptContractTests.C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries` | `phase-proof-recorded-P6` |
| PC-130 | Break G-130: Bypass ID equality against C849_COLD_MAIN_ID | `RemoteScriptContractTests.C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries` | `recheck-refused-P1` |
| PC-131 | Break G-131: Bypass image equality against C849_COLD_MAIN_IMAGE | `RemoteScriptContractTests.C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries` | `changed-image-refused` |
| PC-132 | Break G-132: Bypass equality against C849_COLD_MAIN_MOUNTS | `RemoteScriptContractTests.C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries` | `changed-mounts-refused` |
| PC-133 | Break G-133: Remove C849_COLD_PRESENT check before init | `RemoteScriptContractTests.C912_Cold_seed_refuses_when_created_volume_disappears_before_init` | `no-unlabelled-auto-created-volume` |
| PC-134 | Break G-134: Ignore probe return code after a rename failure | `RemoteScriptContractTests.C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit` | `probe-refused-probe-rename-fail` |
| PC-135 | Break G-135: Treat timeout 124 as successful probe | `RemoteScriptContractTests.C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit` | `probe-refused-probe-timeout` |
| PC-136 | Break G-136: Ignore failed docker rm of probe helper | `RemoteScriptContractTests.C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit` | `probe-refused-cleanup-fail` |
| PC-137 | Break G-137: Replace exact canary unlink with recursive parent deletion | `RemoteScriptContractTests.C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit` | `probe-refused-no-marker-or-outside-write` |
| PC-138 | Break G-138: Allow payload-sha256 field in cold marker | `RemoteScriptContractTests.C912_Cold_marker_has_distinct_validation_and_full_context_refusal` | `malformed-marker-refused` |
| PC-139 | Break G-139: Require historical cold image to exist without fallback | `RemoteScriptContractTests.C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune` | `available-main-helper` |
| PC-140 | Break G-140: Skip compose project check on fallback main | `RemoteScriptContractTests.C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune` | `helper-refused-foreign-main` |
| PC-141 | Break G-141: Require empty packages on marker reuse | `RemoteScriptContractTests.C912_Cold_marker_has_distinct_validation_and_full_context_refusal` | `marker-reuse-no-probe-or-restore` |
| PC-142 | Break G-142: Permit foreign-cache as packages volume | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `name exit=2` |
| PC-143 | Break G-143: Drop local driver predicate | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `driver exit=2` |
| PC-144 | Break G-144: Drop options predicate | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `options exit=2` |
| PC-145 | Break G-145: Drop owner label predicate | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `owner exit=2` |
| PC-146 | Break G-146: Drop schema label predicate | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `schema exit=2` |
| PC-147 | Break G-147: Drop actual_role comparison | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `role exit=2` |
| PC-148 | Break G-148: Accept root-owned 0:0:700 in stat comparison | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `uid-owner-refused` |
| PC-149 | Break G-149: Accept 1654:1654:755 in stat comparison | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `mode exit=2` |
| PC-150 | Break G-150: Bypass root test ! -L and test -d | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `symlink exit=2` |
| PC-151 | Break G-151: Recursively chmod payload files during prepare | `RemoteScriptContractTests.C849_Cache_prepare_is_idempotent_and_preserves_payloads` | `preserved antiphon-runner-cache-nuget-packages` |
| PC-152 | Break G-152: Skip CacheUnmarkedInUse guard | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `unmarked-consumer-refused` |
| PC-153 | Break G-153: Accept wrong scratch volume in mount receipt | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `wrong-mount-refused` |
| PC-154 | Break G-154: Ignore actual /tmp mode in c849_assert_mounts | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `tmp-mode-refused` |
| PC-155 | Break G-155: Remove dot-dot segment rejection in Resolve-Entry so packages/../../escape is mapped | `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` | `nested-traversal-refused` |
| PC-156 | Break G-156: Ignore seen.Add returning false | `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` | `duplicate-archive-refused` |
| PC-157 | Break G-157: Permit SymbolicLink in Read-Tar kind admission | `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` | `symlink code=2 diagnosis=CacheDonorUnsafeEntry` |
| PC-158 | Break G-158: Silently continue past FIFO in Read-Directory instead of refusing, avoiding a blocking FIFO open | `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` | `directory-fifo-refused` |
| PC-159 | Break G-159: Remove packages budget comparison in Check-Entry | `RemoteScriptContractTests.C849_Saved_donor_rejects_declared_size_bomb_before_writing` | `size-bomb code=2 diagnosis=CacheBudgetExceeded` |
| PC-160 | Break G-160: Remove the -band 511 mask in Write-Entry | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `unsafe-mode-masked` |
| PC-161 | Break G-161: Call c849_prepare from c849_preview | `RemoteScriptContractTests.C849_Prune_preview_is_read_only_and_bounded` | `preview-read-only` |
| PC-162 | Break G-162: Change budget OVER threshold from >= to > | `RemoteScriptContractTests.C849_Prune_preview_is_read_only_and_bounded` | `100=OVER` |
| PC-163 | Break G-163: Remove sudo -n from observed Docker root realpath | `RemoteScriptContractTests.C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo` | `OBSERVE_EXIT=0` |
| PC-164 | Break G-164: Remove host-lane refusal before cache case dispatch | `RemoteScriptContractTests.C849_Cache_cases_use_only_the_validated_host_lane` | `wrong-lane-no-effect` |
| PC-165 | Break G-165: Move prepare after seed_runner_checkout in case_deploy_temp_runner | `RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance` | `deploy-prepare-before-checkout` |

**PC-3 extraction variants added from real CP-6 evidence:** keep both pending
for SourceLanding Mutation, using the same exact T7 method above. Independently
remove only the host-apphost `chmod 0755` (expect `host-pack-public-executable`
from a real 0744 archive fixture), and remove only `--no-same-owner` (expect
`host-pack-image-owner`). CP-6 at `0cdfb5cd9f9658f7279837924ac5f19f39af762b`
built the exact pinned archive but observed mode 0744 / uid 2001 in the final
image; uid 1654 could not satisfy the required executable image-pack guard.
The pins and narrow three-pack extraction stay unchanged. No deliberate
mutation of either variant has been run in Code.

### Out of scope

- No production Seed/Reset/Prune, fleet restart, board mutation or rollout in
  Code verification. Q-1 and the parent's canary/Both/Retired rollout gates are
  separate operational obligations after Review. A Code green report is not
  permission to discard recovery or legacy archives.
- No SDK/TFM migration, second SDK install, provider login/real credentials,
  database/API/queue/session changes, browser E2E, provider suites or broad
  Antiphon.Tests assembly run. The changed boundaries are bounded by the listed
  script and image tests.
- No literal giant cache allocations or destructive free-disk experiments:
  the existing declared-length bomb exercises archive pre-write budget refusal;
  unchanged importer disk-space arithmetic is inspected, not newly claimed as
  a runtime free-space qualification. Ownership tests use boundary facts in
  local harnesses; Q-1 checks real uid/mode behavior. Device/socket creation
  requiring privilege is excluded; FIFO, symlink and hardlink drive the same
  forbidden-entry validation without privilege.
- No exhaustive Cartesian product of unrelated volume modes, marker syntax
  and donor states. Cross every **interacting** boundary explicitly above:
  source type vs authority, schema vs context/digest, live-cache churn vs
  recovery churn, each handoff vs failure/crash, warm cache vs missing pack,
  absent temp vs census/status, and later invalid target vs earlier valid target.
  Other single-fault checks keep all earlier gates valid so the intended
  predicate is reached.
- SourceLanding PCs do not certify Docker behavior: the custody-safe local seams
  grade real source logic, and CP-6/Q-1 grade actual containers. No PC may be
  marked complete by referring only to a Q-1 expected-red control line.

### Checkpoints

This is the closed ordinary Code manifest. Each row has one isolated build
(or explicit same-After reuse / no-build command) and one exact filter/command.
CP-1/CP-2 are the tests-only S0 assertion-red demonstrations. CP-3..CP-8 are the
complete S1-S3 green acceptance roster. The union covers all V-1..V-8 and
R-1..R-4 ordinary obligations. Q-1 is explicitly post-Review operational scope.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S0 | `tests/Antiphon.Tests -> bin-c913-red-linux/` | linux-seed-red-first | `/*/*/RemoteScriptContractTests/C913_*` | V-2,V-3,V-4,V-5,V-6,V-7 | 6 executed; exactly the six specified assertion reds, 0 skipped; driver exit 1 intentional | 6 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S0 | CP-1 | linux-image-red-first | `/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)\|(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)\|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*)` | V-1,V-8 | 3 executed; specified T7/T8/T9 assertion reds, 0 skipped; driver exit 1 intentional | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c913-green-linux/` | linux-seed-contracts | `/*/*/RemoteScriptContractTests/(C849_*)\|(C905_*)\|(C912_*)\|(C913_*)\|(C973_*)` | V-2,V-3,V-4,V-5,V-6,V-7,R-1,R-3,R-4 | 47 executed with exact roster above, 0 failed/skipped | 47 | 15 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S3 | CP-3 | linux-image-contracts | `/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)\|(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)\|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*)` | V-1,V-8 | T7/T8/T9, 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1-S3 | n/a | linux-rolling | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1 -RequireJq` | R-2,R-3 | 24 groups / 66 invocations / 227 assertions; failures=0, no skips, exit 0 | n/a | 6 | true | n/a |
| CP-6 | S1-S3 | n/a | linux-offline-image | `pwsh -NoProfile -File scripts/verify-card0660-codex-image.ps1 -Target session-testing -Image "${C913_IMAGE:?}" -SourceRevision "${C913_SHA:?}" -ResultsRoot "${C913_RESULTS:?}"` | V-1 | One image build; 11/11 matching rows ok; actual native receipt/empty mounted pair/network none; exit 0 | n/a | 25 | true | n/a |
| CP-7 | S1-S3 | `tests/Antiphon.Tests -> bin-c913-green-windows/` | windows-receipts | `/*/*/RemoteScriptContractTests/(C849_front_door_passes_every_full_case_name_to_the_invoker*)\|(C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims*)\|(C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads*)` | V-7,R-1 | 3 native Windows pwsh results, 0 failed/skipped | 3 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S1-S3 | n/a | windows-rolling | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1 -RequireJq` | R-2,R-3 | Native Windows pwsh + WSL jq; 24 groups / 66 invocations / 227 assertions; failures=0, no skips, exit 0 | n/a | 6 | true | n/a |

Execution contract:

1. Commit/push S0 before CP-1/CP-2, then complete and commit/push S1-S3 before
   green rows. Freeze source while any child runs. Use the checkpoint tool
   `run --plan docs/superpowers/plans/2026-10-04-card-0849-net9-packs-seed-contract-test-design.md --rows CP-1,CP-2 --expected-source-sha "$C913_SHA"`
   for the intentional red group; green Linux uses `--rows CP-3,CP-4,CP-5,CP-6`,
   Windows uses `--rows CP-7,CP-8` with its matching clean implementation SHA.
   Continue `wait` while exit 75, using a foreground wait bounded to 60 seconds
   per call. Do not settle with an executor running. The red group is expected
   to finish nonzero; count it separately, never reuse its outputs across S0
   and S1-S3.
2. If the checkpoint tool needs bootstrap, use one declared host-slot-gated
   `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c913-tool/`
   on each OS. Invoke its built tool with `dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c913-tool/ -- run --plan docs/superpowers/plans/2026-10-04-card-0849-net9-packs-seed-contract-test-design.md --rows CP-3,CP-4,CP-5,CP-6 --expected-source-sha "$C913_SHA"`;
   the scheduler owns row/build leases. Do not wrap the scheduler in another
   lease. Non-checkpoint build/test drivers use `scripts/build-slot.ps1`.
   Exit 4 is not-run/blocked; never retry outside the gate.
3. For CP-6, export C913_SHA as the full clean implementation SHA, C913_IMAGE as
   a unique run-owned tag and C913_RESULTS as a fresh ignored results directory.
   Reject unset values before launch. The Linux command is passed to /bin/sh;
   the POSIX required-value expansions are intentional and this row is Linux
   only. The scheduler holds its outer slot; the disconnected child must not
   ask an unreachable broker for another slot. No implicit SkipBuild waiver.
4. Preserve unedited CHECKPOINT lines, TRX executed names/counts, source/build
   provenance and image receipts in ignored evidence. Check each acceptance
   receipt with `scripts/validate-checkpoint-receipt.ps1` at the tested SHA.
   Intentional assertion-red receipts are diagnostics, not clean acceptance
   certificates. Review's reviewedSourceClean=true requires validated clean
   green receipts; record pending PCs explicitly.
5. Any unexpected inherited failure is reproduced at the parent base with the
   same narrow method after saving current work. Such diagnostic reruns are
   reported with their reason/count and do not silently expand the manifest.
   No retries, assertion weakening or timeout changes to hide red. Code and
   Review run `scripts/check-evidence-diff.ps1` across their complete task range.
   Remove only this run's alternate bin outputs after all owned children end.
   No generated JSON/TRX/logs/manifests are committed.

### Cost

All following times are **estimated**, not measured in TestDesign. Slot waiting,
dependency provisioning, failure diagnosis and authoring are not disguised as
test execution. Allow 1-2 working days for Code authoring; revise estimates with
measured receipts if actual setup or row duration differs.

- Ordinary V/R floor (Code): **80 minutes**, exactly CP-1 12 + CP-2 2 +
  CP-3 15 + CP-4 2 + CP-5 6 + CP-6 25 + CP-7 12 + CP-8 6.
  Filters and lanes are the exact eight rows above. It includes the 14-minute
  tests-only red-first round; green acceptance alone is **66 minutes**.
- Ordinary build allocation already inside those 80 minutes: CP-1 8,
  CP-3 9, CP-6 Docker build 20, CP-7 9 = **46 minutes**. Remaining
  V/R execution = **34 minutes**. Declared checkpoint-tool bootstrap/setup:
  **6 minutes** total (3 per OS), outside the rows. Code verification with
  bootstrap = **86 minutes**; do not add the 46 again.
- PC floor (Mutation): **1072.5 minutes** for 165 separate controls.
  Every PC-n uses its one method filter from the key table; baseline, red and
  restored-green each budget **2 minutes** (1.5 isolated build + 0.5 execution),
  plus **0.5 minute** total edit/restore accounting per PC.
  165 x (2 + 2 + 2 + 0.5) = 1072.5.
  Total 495 method-scoped phase invocations; no class-scoped PC runs.
  C10 expands to five results per phase and has two controls; all other
  PC targets are single-result methods, giving **519 TUnit results**
  across the three phases. Internal fault loops are not results.
- Combined setup/build + ordinary V/R + every PC baseline/red/restore/green:
  **1158.5 minutes** = 6 + 80 + 1072.5.
  Of the PC floor, isolated builds total 742.5 minutes, method execution
  247.5 minutes and edit/restore 82.5 minutes. This makes the
  substantial cost of retaining each independent safety check visible.
- Independent ordinary Review requires the final-source CP-3..CP-8 profile:
  another **66 minutes** if freshly executed, plus 6 if both tool bootstraps
  are needed. Q-1 operational Fixture is an additional **30 minutes**.
  Neither is silently counted as already done. Code + Mutation + fresh Review
  with both bootstraps + Q-1 = **1260.5 minutes**; rollout/canary duration
  remains deployment scope.
- Savings: CP-2/CP-4 reuse already-built same-source outputs, avoiding two
  estimated 8-minute rebuilds = **16 minutes**. Method-scoped PC execution
  replaces 25.5-minute full-assembly execution with the 0.5-minute method
  estimate: 495 x 25 = **12375 minutes** avoided versus that
  inappropriate broad baseline. No mutation batching savings claimed:
  most controls edit the same shell/front-door files and several share methods;
  each source must remain frozen until its phase finishes.

Before handoff: bodies read; **guards=165, mapped=165, missing=0,
duplicate PC maps=0**. Every PC has a concrete syntactically valid defect, exact
method binding, assertion and local inherited execution path. All are executable
once Code lands the specified tests; none is represented as run now. The
checkpoint rows/counts, fixture 32-family/47-variant roster and numeric costs
are frozen. No unresolved human choice or unverifiable production seam prevents
Code; missing jq/Windows/image capabilities are explicit execution prerequisites.

Static validation in this TestDesign task: the on-disk document audit passed
165 guard/PC pairs, zero missing/duplicate maps, eight 11-column checkpoint
rows, all method bindings, the base 18/9/2/3 method census, the 80-minute row
sum and the 32-family/47-variant arithmetic. This is documentation validation,
not TUnit, image or mutation evidence.

### Code execution correction (2026-10-04)

At S0 `514878838d279f7b0cdc2830bc04f23cc8bc559b`, CP-2 produced a fresh TRX
with zero tests. TUnit 1.44 requires trailing wildcard hints for method-segment
OR operands, as documented in `docs/testing-and-build.md` (Combined class filters).
CP-2, CP-4 and CP-7 now include those suffixes. The exact required method roster
and counts are unchanged and must be checked against fresh TRX; the zero-result
run is setup failure, not red-first evidence. CP-1 in that same run had five
Bash extraction failures and the expected T6 receipt assertion red. The test-only
extractor correction matches an entire closing-brace line, preserving embedded
PowerShell/JavaScript heredocs. Production remains unchanged for the rerun.
