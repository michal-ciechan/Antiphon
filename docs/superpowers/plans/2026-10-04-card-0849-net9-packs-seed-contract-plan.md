# CARD-0849 / CARD-0913: finish the image-pack and Seed contract

Date: 2026-10-04. Stage: Plan; next **TestDesign**. Inspected source:
`bb18064ba647e0ddb03cae4da437ab60ed447d98`. This dispatch changes only this plan.
Verification design is a separate stage; the checkpoint proposal below is not a
Code-ready verification manifest until TestDesign freezes its roster and controls.

## Outcome and scope

Make net9 native apphost readiness depend on the runner image's 9.0.20 host and
reference packs, outside the mounted NuGet cache. Complete the remaining full-Seed,
fixture and recovery changes without adding package work to CARD-0912's volumes-only
cold Seed. Prove the qualified image restores, builds and executes a native net9
program as uid 1654 with an empty mounted package cache and network disabled.

The pack installation is already implemented: commit `d9809c7c9` introduced the
version/hash-pinned archive extraction and image smoke. Do not implement it again.
The premise is partially stale, but the remaining Seed contradiction is reachable
and does not need another investigation before planning its repair.

Starting evidence: [apphost investigation](../../investigations/2026-10-02-apphost-pack-and-dotnet-version.md),
requested reference `9f99584f9d728b9aa150bb228559fae356e1dc54`.
Related authority: [CARD-0912 plan](2026-10-02-card-0849-first-seed-exception-plan.md),
especially **Operator refinements and supersession**, and
[Docker operations](../../docker-stack.md#shared-server2-runner-caches-card-0849).
The investigation describes an earlier checkout, not today's implementation or a
current live-image inventory.

Read-only board inspection found CARD-0849 InProgress and CARD-0912/CARD-0913 Review.
CARD-0913's operator update authorizes baked packs and explicitly leaves cold Seed
volumes-only. The present brief additionally commissions a plan for the remaining
Seed assertions/recovery controls. CARD-0912's older card title still mentions saved
archives; its later plan supersedes that direction. Do not revive saved import as a
cold-rollout prerequisite. No board state or operational setting changes in this task.

## Ground truth

Line references describe the inspected source, before implementation.

| Card/investigation assumption | What the code does now | Consequence |
|---|---|---|
| A recreated runner lacks the net9 packs. | `docker/session-runner-grok/Dockerfile:46-70` extracts three 9.0.20 packs from SDK 9.0.318, checking a fixed SHA-512 before extraction; `session-testing` copies them after SDK 10. | Retain and qualify this installation. Publication of source alone does not prove the deployed image contains it. |
| Changing the SDK major is necessary. | `global.json` requests 10.0.204 with `latestMinor`; Docker's build stage uses floating `sdk:10.0`. The image probe currently demands SDK 10.0.401. | Preserve SDK/TFM policy. Record the actual built SDK; a floating-tag mismatch is a separate observed qualification failure, never a reason to silently update pins. |
| Offline native smoke has not been authored. | `verify-codex-image.sh:197-227` already restores/builds/runs net9 with an empty custom cache/home; `verify-card0660-codex-image.ps1` launches it as uid 1654 with `--network none`. | Strengthen the existing row to cover actual cache mounts, a private scratch pair, and the ASP.NET reference pack. Do not invent a second image verifier. |
| The verifier has eight or ten rows. | Current wrapper has eight Codex rows, Grok, jq, and `net9-offline`: **11** for `session-testing`. | Freeze against today's roster, not the historical report. |
| Seed smoke accepts image packs. | `c849_smoke`, `scripts/c590-remote.sh:1454-1495`, requires cached 9.0.20 apphost metadata/payload and explicitly forbids the installed image host pack. | A full Seed with the new image fails before restore. Replace these assertions together. |
| Only three smoke assertions need changing. | `c849_validate_seed_tree:1602`, full Seed hash creation/publication around 1968-2033, `c849_require_ready:2037`, and Prune around 2380-2430 all depend on the cached host/reference packages. | Change full-marker production, readers, recovery and refill as one contract; do not merely delete the smoke guard. |
| Empty cache should be an expected apphost failure. | `c849_fixture_apphost:2932` copies packs from a live temp donor, forbids image packs, and PC-18 expects `AppHostCacheMiss`. Fixture Seed and Prune also fabricate/hash those packages. | Empty cache becomes a positive case. Missing image packs become the negative cases. Fixture payload must be ordinary synthetic packages. |
| First Seed imports a saved donor and acquires a build lease. | `c849_cold_seed`, explicit `-Cold`, and `schema=2 kind=cold` are implemented. Cold creation/reuse and cold Both/Retired do no package smoke. | Keep CARD-0912's distinction, zero-copy path, busy-main allowance, repeated isolation proofs and writability checks. |
| Full Seed and cold Seed use one interchangeable receipt. | `c849_require_ready` has explicit `allow-cold` versus `full-required`; the front door branches on marker kind. Full Both compares `seed-hash.txt`; Retired retains a recovery image. | Version the new full evidence, preserve cold schema 2, and reject mixed/unknown contracts. |
| Fixture can run before temp exists. | Its apphost group needs a live donor, and `case_runner_cache_inventory` refuses an absent temp container even when its API status is retired. | Remove production package copying; record a proven retired/absent temp inventory explicitly. Lookup errors must still refuse. |
| Older checkpoint counts remain current. | `RemoteScriptContractTests` has 18 C849 and 9 C912 methods; `CodexRunnerImageContractTests` has 11 methods. The rolling harness now requires 24 groups / 66 invocations / 227 assertions with real jq. | Static counts are planning evidence only. TestDesign must freeze actual selected results; do not reuse the old 17/48/155 rolling count. |

## Decisions

**D-1 — Keep the landed pack-only installation.** Install exactly
`Microsoft.NETCore.App.Host.linux-x64`, `Microsoft.NETCore.App.Ref`, and
`Microsoft.AspNetCore.App.Ref` at 9.0.20 under `/usr/share/dotnet/packs`, in
`session-testing` only. Retain SDK 9.0.318's existing SHA-512
`e8685293a3512178e0de1bb3c1663e31fdf9d761af705094bf833cc1ff6b9a18c543aa4141f0216503c98039c719585a6d32905b0bbb3279e93b7e7616e047c3`
and hash-before-extract checks. These are source pins, not a fresh vendor-metadata
verification by this Plan task. Reject replacing SDK 10 with SDK 9 (breaks the repo
SDK policy), installing a second complete SDK (unnecessary footprint/host changes),
and baking packages below the mounted cache (hidden by `volume.nocopy`). A .NET 10
TFM migration remains CARD-0914.

**D-2 — Preserve a real native probe.** Keep `net9.0`, `UseAppHost=true`,
`RuntimeIdentifier=linux-x64`, `RuntimeFrameworkVersion=9.0.20`,
`TargetLatestRuntimePatch=false`, and `SelfContained=false`. Include an explicit
`FrameworkReference` to `Microsoft.AspNetCore.App` and a compiled use of an ASP.NET
type so all three packs matter. Run the produced executable directly and require
its exact success token. Reject DLL-only execution, `UseAppHost=false`, changing
the probe to net10, or an online restore that hides a missing pack.

The image row uses fresh task-owned package and scratch volumes, an empty temporary
CLI/NuGet home, an empty source configuration with sources/fallback folders cleared,
and disabled audit/workload update traffic. Mount the package volume at the actual
cache destination with `volume-nocopy`; initialize only these owned volume roots to
uid 1654. Assert the volumes are empty before the run and no framework packages were
downloaded afterward. Record the three installed pack paths, actual SDK/runtime,
image ID, source revision and restore/build/native-run exits. No shared cache,
provider home, credential, Docker socket or phone-home service enters the child.
The controlling checkpoint (or standalone build-slot wrapper) holds one host build
slot while the child has `--network none`; the child does not try to acquire a second,
unreachable broker lease.

`c849_smoke` retains shared-mount/environment validation and its existing lease,
then uses a fresh private package/scratch/home for the small native project. It
requires image packs and never requires cached framework packages. Its empty-source
run is a deployment smoke; the image row supplies the stronger network-disabled
proof. Do not label a broker-connected container as `network=none`.

**D-3 — Cold Seed remains filesystem-only.** Do not call the native smoke from
`c849_cold_seed`, cold marker reuse, or cold Both/Retired. Do not add a broker call,
saved archive, package download or recovery path to cold schema 2. Image qualification
is an independent rollout obligation. Retain the established canary-before-main-drain
order, shared package/scratch pairing, private `/tmp`, and all ownership/isolation
refusals. Reject using successful image qualification to waive any volume checks.

**D-4 — Separate full-import recovery evidence from framework-pack availability.**
New full Seed validates generic complete NuGet package versions, not two named
framework packages. Preserve rejection of links, unsafe paths, hardlinks, special
entries and malformed package/version layout. Preserve the existing removal of
incomplete versions only in the private staged copy. Require at least one complete
version after filtering for an explicit full import; an empty result returns
`CacheDonorPackagesEmpty`, with `-Cold` as the separate operation for empty caches.
Run npm integrity on the staged cache as today. A donor containing no net9 packages
is valid. Ordinary packages remain subject to normal restore verification; the
snapshot digest proves retained bytes, not package publisher trust.

Introduce **schema 3, kind=full** for newly written full markers. Keep source SHA,
inspected helper/recovery image ID, donor identity/type, timestamp, three fixed volume
identities, sizes, confined recovery path, and `manifest-sha256`. Create a deterministic
versioned manifest of the staged `packages/` and `npm/` trees after filtering/npm
verification. Use relative paths, entry type, regular-file size and SHA-256; preserve
executable-bit evidence separately from ownership/read-write normalization. Use an
unambiguous sorted serialization (NUL-delimited records are appropriate for the Linux
helper), reject duplicate/unsafe records, and exclude the manifest itself and scratch.
Keep it inside the retained recovery tree; the marker binds its digest. Do not put
package names, contents or the manifest into public receipts.

Compare the imported volume trees with this staged manifest before running the
smoke, retaining recovery, reconnecting the live donor, and atomically publishing
the marker. All existing source-specific consumer/drain/attachment gates remain.
Do not add a main-zero gate to live-donor Seed: its existing gates differ from saved
maintenance. A failed step cannot publish ready or mutate the saved source archive.

After acceptance, schema-3 readiness validates the immutable recovery tree/manifest,
image identity and volume contract, not equality of the mutable shared cache with a
historical snapshot. Cache additions/evictions are best effort, as in cold mode.
Fixture retention separately proves ordinary cache bytes survive container recreation.
Reject reusing `payload-sha256` with a new meaning: an apphost digest and a recovery
manifest digest are different evidence contracts.

**D-5 — Preserve explicit compatibility; never guess a marker type.** Parse exactly
three formats: legacy unversioned full, schema-2 cold, and schema-3 full. Unknown,
duplicate, mixed, partial or symlink markers refuse `CacheSeedMarkerInvalid`.
Existing valid legacy full markers keep their old payload/recovery requirements in
an explicitly isolated compatibility branch; no new path writes that format. Do not
auto-upgrade a legacy marker by hashing whatever cache happens to be present.
Cold stays forbidden to Reset/Prune/full-donor maintenance with `CacheFullSeedRequired`.

Expose marker schema and digest type in bounded full Seed/Both/Retired receipts.
Both requires matching schema and digest type/value; it must not equate an old
apphost digest with a new manifest digest. A legacy receipt's hash remains legacy
evidence, not proof of image-pack installation. Add exact parsing to
`verify-card0849-caches.ps1`; never print `smoke=passed` for marker reuse without a
smoke. No automatic cold-to-full conversion or marker deletion is introduced.

**D-6 — Keep recovery gates, remove new-format pack-specific refill.** Schema-3
Prune verifies the confined recovery tree against its manifest before the first
destructive operation; changed/missing recovery refuses with
`CacheRecoveryChanged`/`CacheRecoveryMissing`. Keep preview freshness, volume/root
identity, full drain/zero/process/attachment/broker checks and whole-target preflight.
For schema 3, do not copy net9 NuGet packs back solely to make a native smoke pass.
Existing leased ordinary solution/npm refill, budget checks and image-based smoke
remain required before the operation reports success; admission stays held.
Legacy Prune keeps its old bounded package recovery branch until explicitly reseeded.

Retain the recovery snapshot and rollback image; the snapshot remains available for
explicit ordinary-package recovery. No automatic restore of the entire multi-GB
snapshot into an actively used cache. A rollback image without baked packs cannot
claim schema-3 cold readiness: use a separately qualified compatible rollback path
with its own private cache pair, or stop while the verified runner keeps serving.
Do not discard legacy archives or rewrite historical verification claims.

**D-7 — Make Fixture independent of production package donors.** Use locally
generated ordinary package trees and the existing fixture npm package. Change F-3,
F-5 and F-8 to exercise generic import, recovery and empty-cache native success.
Replace PC-18's empty-cache expected failure with missing-image-pack controls that
mask one pack at a time in disposable containers; never delete an installed pack
from a standing runner. Missing host, core reference and ASP.NET reference must
each fail for the intended reason, not lease/setup failure or zero execution.
Replace host/reference donor metadata controls with generic incomplete-package and
recovery-manifest controls; retain unsafe-entry, authority, privacy and cleanup controls.

Inventory both runner identities read-only. A successful complete container census
plus an explicitly retired temp status may record `container=absent`; failed census,
unknown status or a contradictory live container still refuses. Keep this optional
absence handling narrow to the fixture/inventory context; it grants no deployment
or cleanup authority. Saved-archive Fixture is unnecessary. Freeze updated family
and variant counts in TestDesign and update both receipt producer and front door;
do not preserve the number 26 by silently dropping controls.

**D-8 — Keep routing and activation separate from implementation.** On 2026-10-04,
`GET /api/runner-defaults` returned revision 2 with a resolvable global default;
`GET /api/session-runners` showed eligible Linux and Windows lanes and an unavailable
temporary runner. These are observations, not dispatch pins or deployment readiness.
Refresh both routes when commissioning the next task. Omit `-Runner` unless a
specific operational canary intentionally pins a host; omit `-Platform` for portable
planning. Use `-Platform Linux` for Bash/native Docker work and `-Platform Windows`
only for the Windows front-door checks. `-Platform Any` removes an inherited OS pin.
Do not encode fleet addresses or checkout locations in the plan's test commands.

## Implementation slices

Commit and push each meaningful slice before any long verification. TestDesign may
adjust slice batching to keep one committed source per checkpoint group, but must not
leave a partially migrated marker producer/consumer deployable as a finished change.

| Slice | Files | Change and tests |
|---|---|---|
| S1 — executable image contract | `docker/session-runner-grok/verify-codex-image.sh`; `scripts/verify-card0660-codex-image.ps1`; `tests/Antiphon.Tests/Infrastructure/CodexRunnerImageContractTests.cs` | Strengthen the existing net9 row per D-2, preserve the other ten rows, extend `Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing`. Dockerfile is a read-only dependency unless qualification proves a defect. |
| S2 — full Seed and evidence | `scripts/c590-remote.sh`; `scripts/verify-card0849-caches.ps1`; `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` | Change smoke, generic staged validation, schema-3 producer/reader, typed receipts, recovery and Prune together. Extend existing C849 import/ready/prune tests; add the six focused methods below. Keep schema-2 C912 methods as regressions. |
| S3 — fixture and runbook | same three S2 files; `docs/docker-stack.md`; this plan | Remove fixture live package donor assumptions, revise image-pack/recovery controls, handle proven absent-temp inventory, replace stale runbook cache-only claims/counts, and document legacy/schema-3 distinctions and qualification order. |

Read-only dependencies requiring regression coverage: `scripts/c849-import-saved-donor.ps1`
(archive confinement/size bounds), `scripts/c590-real.ps1` (transport),
`scripts/deploy-server2.ps1`, `scripts/test-deploy-server2.ps1`, compose cache mounts,
and `scripts/lib/build-slot.ps1`. No changes to global.json, project TFMs, broker
policy, API, database, generated `docs/cards/`, or land path are planned. If a new
helper file becomes necessary, TestDesign must name its upload path and tests before
Code changes the transport. Prefer helpers inside the existing shell file.

Serialize any Code touching the shared remote script/test file with concurrent
CARD-0912, cache maintenance or rollout-script work. The caller checks current
occupancy/scope before dispatch; the inspected base already contains the cold Seed
implementation, so do not wait on the obsolete original pre-Code dependency.

## TestDesign handoff

This brief does not fold TestDesign into Plan. Add `## Verification design` in the
next stage, with exact assertions, refusal ownership, executed-result floors,
method-scoped positive controls and separate ordinary/Mutation cost. Freeze the
checkpoint proposal below after binding each item to actual tests. No builds,
tests, Docker operations, SSH, Seed, Prune or deployment ran in this Plan task.

Coverage obligations and proposed six new methods in `RemoteScriptContractTests`:

| ID | Test/method | Required behavior |
|---|---|---|
| V-1 | existing `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing`; real image row | Three exact pinned packs, digest-before-extract, selected SDK unchanged; empty mounted cache, empty home/private scratch, no network, ASP.NET reference use, direct native execution. Static text alone cannot certify the image. |
| V-2 | proposed `C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | Ordinary package-only donor passes; incomplete versions are removed only from staging; empty full payload refuses; links/traversal/size bounds remain refused. Update existing C849 saved-donor tests, including the method whose name currently contains `missing_pack`. |
| V-3 | proposed `C913_Full_marker_binds_verified_recovery_before_publication` | Deterministic manifest, import verification, reconnect/smoke before atomic ready; corrupt file/manifest, invalid path/type and failed copy publish nothing. Saved archive remains byte-identical. |
| V-4 | proposed `C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | Valid formats, corrupt/mixed/duplicate formats, no auto-migration, cache churn allowed only under new semantics, cold remains filesystem-only/full-context refused. |
| V-5 | proposed `C913_Prune_validates_recovery_without_refilling_image_packs` | New-format altered/missing recovery and stale/busy authority refuse before deletion; no net9 package copy; ordinary refill, smoke and budgets still gate success. Exercise legacy recovery separately. |
| V-6 | proposed `C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | No production donor package reads, retired/absent temp accepted only after complete observations, inspect errors refuse, cleanup cannot touch production resources, changed pack controls have distinct actual failures. |
| V-7 | proposed `C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | Schema/digest mismatch, missing/duplicate fields and toxic payloads refuse; legacy/full/cold summaries are truthful. Run receipt parsing on native PowerShell in both OS lanes. |
| R-1 | existing 18 C849 and 9 C912 methods | Cache identity, archive safety, private roots, cold busy-main path and no-package/no-lease contract, full maintenance guards, saved/live source distinctions, privacy and marker consumers. Adapt assertions only where D-4/D-5 deliberately change the new-format contract. |
| R-2 | `scripts/test-deploy-server2.ps1` | All current rolling groups, real jq; source selection, admission/verify ordering, no early main drain and held failures. No provider/land/full assembly suites are justified by these script changes. |

Positive-control design must include a changed archive digest, omitted/wrong pack
copy, each masked image pack with an empty cache, accidentally accepting a warm
cache, deleting an import/recovery hash check, accepting mixed marker versions,
publishing before smoke/reconnect, treating failed census as absence, and accidentally
calling package work from cold Seed. Use real shell predicates/parsers and fake only
external Docker/HTTP/process boundaries in deterministic tests. Zero tests, skipped
capabilities, syntax errors and fixture setup errors are not expected assertion reds.
Full mutation execution belongs after Review and land, on the published source.

### Checkpoints

**Proposal for TestDesign, not permission to bypass that stage.** Lane appears in
Group/Expect because the checkpoint importer rejects an unrecognized Lane column.
All rows below close S1-S3 at one committed source. CP-1 assumes the six proposed
non-parameterized methods: 27 existing + 6 = 33; this is a static count, not a run.
CP-5 selects only portable receipt/cold-verification methods, avoiding inherited
Windows/WSL capability skips. TestDesign must freeze fresh actual rosters and any
required red-first rows before Code. Real host Fixture/rollout gates are separate
from these ordinary rows and must not be represented as completed repo tests.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c849-pack-linux/` | linux-seed-contracts | `/*/*/RemoteScriptContractTests*/(C849_*)\|(C912_*)\|(C913_*)` | V-2..V-7, R-1 | Linux Bash/PowerShell/jq lane; 33 proposed results, 0 failed/skipped | 33 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S3 | CP-1 | linux-image-contract | `/*/*/CodexRunnerImageContractTests*/Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | V-1 | Linux lane; 1 executed, 0 failed/skipped | 1 | 2 | true | n/a |
| CP-3 | S1-S3 | n/a | linux-rolling | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | R-2 | Linux with real jq; 24 groups / 66 invocations / 227 assertions, exit 0 | n/a | 6 | true | n/a |
| CP-4 | S1-S3 | n/a | linux-offline-image | `pwsh -NoProfile -File scripts/verify-card0660-codex-image.ps1 -Target session-testing -Image "${C913_IMAGE:?}" -SourceRevision "${C913_SHA:?}" -ResultsRoot "${C913_RESULTS:?}"` | V-1 | Linux x64 Docker lane; one image build, 11/11 rows ok, empty mounted cache, native run, exit 0 | n/a | 25 | true | n/a |
| CP-5 | S1-S3 | `tests/Antiphon.Tests -> bin-c849-pack-windows/` | windows-receipts | `/*/*/RemoteScriptContractTests*/(C849_front_door_*)\|(C913_Receipts_*)\|(C912_Cold_runner_verification_*)` | V-7, R-1 | Native Windows PowerShell front door; Bash capability for existing method; 3 results, 0 failed/skipped | 3 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1-S3 | n/a | windows-rolling | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | R-2 | Windows with real jq and Bash harness capability; 24 groups / 66 invocations / 227 assertions, exit 0 | n/a | 6 | true | n/a |

Before CP-4 set `C913_SHA` to the full clean implementation HEAD, `C913_IMAGE` to
a unique run-owned tag and `C913_RESULTS` to a fresh ignored results directory.
These are task inputs, never fleet locations or operator credentials. CP-4 is Linux
only; its shell expansions fail on unset/empty values before the verifier launches.
Build and test through one slot-owning layer: `RunScheduler.RunRowAsync` acquires
a lease for command rows too, so CP-4 calls the verifier directly inside the checkpoint
tool. Standalone verifier use must instead run through `scripts/build-slot.ps1`.
The verifier's disconnected children run under that outer lease. A previously built
image may use `-SkipBuild` only with verified matching source/image provenance and a
declared manifest amendment, not an unlisted convenience run.

Ordinary proposed floor: **63 minutes** (12+2+6+25+12+6), excluding authoring, slots
and test-design changes. TestDesign owns the numeric Mutation floor; do not invent
a completed PC count from this inventory. Authoring estimate: 1-2 days because the
new full evidence format touches import, recovery, maintenance and front-door readers.

Use the checkpoint tool's `run --plan` once per committed slice group/OS selection,
then `wait` until exit is not 75. Any tool bootstrap is a declared slot-gated build.
Preserve unedited CHECKPOINT lines, source SHA/dirty/build provenance and expanded
counts; validate receipts at the reviewed SHA. Remove only owned alternate bin
outputs after all children finish. Code/Review run `scripts/check-evidence-diff.ps1`
over their full task range. Generated manifests, logs, image receipts and TRX stay
ignored. Reproduce unexpected failures at the base with the same narrow selection
before attributing them; do not weaken a timeout/assertion to obtain green.

## Rollout and acceptance

After ordinary Code, independent Review and publication, the caller follows the
existing named rolling phases from the trusted operational checkout. This Plan
delegate does not deploy or replace the runner that hosts its task.

1. Bind the built `session-testing` image ID/revision to the reviewed implementation
   and require the network-disabled native qualification receipt. A missing image,
   changed SDK pin, wrong hash or unavailable build lease leaves this gate pending.
2. For the cold rollout, run the explicit `Seed -Cold` front door with fresh
   isolation/retirement evidence; keep standing work accepting. Do not import the
   saved archive. Run the revised parent Fixture in its supported host lane once
   the qualified image is available; record actual group/control/variant counts.
   This does not change CARD-0912's independent volumes-only acceptance contract.
3. Deploy temp with that image, verify exact image/source, mounts and uid writability,
   then complete the sanctioned Plan canary with transcript-confirmed delivery.
   Only then drain old and wait for the established zero-work gates before upgrade.
4. After old upgrade, verify its actual image/source and cache contract, run the
   applicable Both receipt checks, and retire temp through the existing phase/gates.
   Retired verification must retain external caches and the correct rollback evidence.
5. Exercise full import/Prune/new-marker recovery using owned fixture resources for
   ordinary qualification. Live Reset/Prune/import is a separate maintenance action
   under existing authorization and drain rules, not a requirement to disturb the
   cold rollout. Keep old archives/recovery images until their recorded retention
   window and explicit cleanup authority permit removal.

Acceptance is the combination of image-backed net9 capability, cache contract
regressions passing, and honest mode-specific rollout receipts. A cold marker is not
an image qualification receipt; an offline native apphost is not a promise that all
ordinary repository dependencies restore without network. No live outcome is claimed
by this document.

## Plan validation and next stage

Plan validation is static: inspect source/cross-card contracts, check table structure
and arithmetic, verify file/method references, and run `git diff --check`. No code or
test execution is required for this documentation slice. Commit/push the plan on the
assigned branch. Next **TestDesign** must resolve the explicit roster/serialization/
receipt details above into executable verification, keeping D-1 through D-8 and
CARD-0912's cold policy intact. There is no missing operator choice preventing that
stage; implementation refinements belong in the plan before Code is commissioned.

## TestDesign appendix

The [2026-10-04 verification design](2026-10-04-card-0849-net9-packs-seed-contract-test-design.md)
freezes the executable roster, guard/control mappings, evidence formats, red-first
order, fixture counts and costs. Its single Checkpoints table replaces the
provisional table above for Code commissioning; D-1 through D-8 remain unchanged.
