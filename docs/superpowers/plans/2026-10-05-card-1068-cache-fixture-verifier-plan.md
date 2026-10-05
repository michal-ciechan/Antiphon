# CARD-1068: make the cache Fixture gate test the gated source

Date: 2026-10-05. Stage: Plan. Base: `0e4c26f74c41d6cdd792e01b73f2390d5e726298`.
Branch: `feat/card-task-57c0d543`. Card: Antiphon `CARD-1068`, id
`7ec097e9-c996-472c-b19e-8c759e1f2d21`.

## Outcome and boundary

Repair Fixture image/source selection, failure propagation, owned-resource cleanup,
PC-08 teardown, F-6 hashing, and evidence delivery. Preserve the mandatory full
Fixture gate before Seed and `deploy-temp`: **9 groups, 32 control families,
47 negative variants, two inventories, zero production mutations**.

The safest resolution of the image/deployment circularity is an explicit,
separately commissioned **build, qualify, and import image prerequisite**. Build
the target SHA in an isolated Linux checkout through the existing image
qualification front door and host build-slot gate; import that exact image into
the deployment host's daemon; then run Fixture. Fixture itself refuses a missing
or wrongly stamped target image. It neither builds implicitly nor falls back.
The standing runner remains on its existing image throughout this prerequisite.

This plan is complete under the defaults D-1 through D-9. **Next: Decide on D-1
and D-5, then TestDesign.** The brief did not fold TestDesign into Plan. The
test inventory and checkpoint table below are its bounded input, not permission
to bypass that stage. No production code, live host operation, build, or test
was executed to produce this plan.

Plan and Code may read source and run isolated offline regression harnesses.
They must not execute a rollout phase, live Fixture, Seed, Reset, Prune,
Docker prune, server2 host mutation, image import, or old-residue deletion.
The build/import and live acceptance procedure below belongs to a separate,
post-land operational commission. Implementing script fixes does not authorize
executing them against the host.

## Ground truth

The card was read in full with `scripts/card.ps1 get CARD-1068 -Board Antiphon`.
Its embedded Debug audit is evidence of run `c84983933adda4d94c530`; this Plan
did not repeat its host probes. Source facts below were inspected at the base.

| Card assumption / required behavior | What the code and recorded audit actually establish | Design consequence |
|---|---|---|
| Fixture can verify the target before deployment. | `docs/docker-stack.md` requires Fixture before Seed/deploy-temp. `build_server2_images` in `scripts/c590-remote.sh` builds the target only from deployment callers. Cache dispatch deliberately skips `ensure_checkout`. | Add an explicit image preparation prerequisite, without moving or skipping Fixture. |
| The image selected by `c849_image` represents `SHA`. | It tries the 12-character tag, then permits Fixture to select a donor/marker/main image; it does not verify the full revision label even when the tag exists. The audit found the old standing image `4358939ecd85`, not target `0e4c26f74c41`. | Strict full-SHA and immutable image-ID binding for Fixture only. |
| The native apphost is unusable in current source. | Dockerfile commit `d667f813447c1effc9b00ccee8a0d0ada61c747b` already sets 0755. The audited old image's apphost was 0744, owner 2001; uid 1654 failed `test -x`. | Do not weaken `c849_smoke`, change its uid, or repeat the Dockerfile fix. Test the right image. |
| Sending the new remote script updates all Fixture inputs. | `c590-real.ps1` uploads `c590-remote.sh`. F-1 reads Compose files through `CHECKOUT`; smoke/race/warm helpers bind scripts through that same host checkout. Audit says its HEAD was `4358939ecd85d6e7ff0941f970879499cb930e3d`. | Stage a small, SHA-bound source bundle for Fixture too; do not reset the shared host checkout. |
| Removing Fixture fallback is compatible with every existing test. | `RemoteScriptContractTests.C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune` has a `runner-cache-fixture` argument requiring the fallback. | Replace that one obsolete expectation with strict Fixture tests. Keep its other four contexts and cold Seed semantics. |
| The outer seed failure retains the inner reason. | `c849_fixture_seed` executes `c849_seed` in a subshell, then calls `write_result false FixtureSeedFailed 2`. `WROTE` is process-local; the inner `CacheSeedSmokeFailed` file is overwritten. The repeat call has the same shape. | Preserve a valid diagnosis from this invocation; only undiagnosed failures get the generic fallback. |
| The EXIT trap can delete everything it creates. | Seed uses root npm on a bind mount; F-6 recursively chowns its project to 1654. The trap uses unprivileged `rm -rf`, suppresses several Docker failures, and writes success before cleanup. | Keep deletion narrowly owned, report cleanup independently, and require clean teardown for gate acceptance. |
| PC-08 evidence is safe to copy recursively. | `c849_fixture_prepare` leaves `.fixture-escape` pointing at `CASE_DIR`; `scp -r` follows it. | Unlink the exact test link on every exit path before export; reject remaining linked export entries. |
| A copied result file proves a complete export. | `Invoke-C590LiveCase` checks nonzero scp only for recycle/retirement contexts, so a partially copied successful Fixture result may be accepted. | Check transport and required evidence for Fixture, preserving the remote result as a separate fact. |
| F-6 can hash on the outer host. | It invokes host `pwsh`; the audit found none. PowerShell exists in the image. | Hash fixed tarball bytes in a disposable container of the pinned image. No host PowerShell installation. |
| The previously observed controls constitute a full receipt. | Only F-1/F-2 and PC-01..PC-15 ran. F-3..F-9 and PC-16..PC-32 remain unexecuted. | Neither this plan nor offline regressions satisfy the operational gate. Require the unchanged full roster later. |

Relevant source owners: [Docker operations](../../docker-stack.md),
[testing and builds](../../testing-and-build.md),
[operational autonomy](../../orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout),
[HTTP operations](../../ops-http.md), and [project conventions](../../project-context.md).
Related contracts: the [CARD-0913 plan](2026-10-04-card-0849-net9-packs-seed-contract-plan.md)
and its [TestDesign](2026-10-04-card-0849-net9-packs-seed-contract-test-design.md).

## Decisions

### D-1: explicit build/qualify/import prerequisite (operator decision)

Default: use the already implemented `scripts/verify-card0660-codex-image.ps1`
with `-Target session-testing`, a fresh qualification tag, full `-SourceRevision`,
and a fresh results root, from a clean isolated checkout at the reviewed landed
SHA. Run it under `scripts/build-slot.ps1` on the Linux build host. Its 11/11
qualification rows include the real uid-1654 offline net9 apphost probe. Export
only that qualified image with `docker save`, transfer through the existing
approved deployment transport, verify the archive SHA-256, and `docker load` on
the deployment host. Verify the loaded immutable ID and full OCI revision label
against the qualification receipt **before** adding the target tag
`antiphon-server2/session-testing:<first-12-of-SHA>`.

The operator commissions this preparation after Code/Review/land and authorizes
the host import/storage budget and custody of the generated archive. This plan
does not invent a registry or a fleet address. The builder and transport endpoints
come from the effective runner/deployment configuration at execution time.
Use a new isolated qualification checkout; do not build from the stale shared
host checkout. An occupied target tag with a different ID refuses replacement;
retain it for diagnosis. Reuse requires the exact qualified ID and full SHA.

Complete this path with a narrow image-reuse change in `build_server2_images`:
when the target runner tag already exists and its immutable image has the exact
full revision, retain it instead of rebuilding that runner target. Record the
reused ID. A present tag with wrong/unknown revision refuses. An absent tag keeps
the existing build behavior for other callers; this does not waive the runbook's
mandatory pre-Fixture preparation. State-init building is unchanged. Before
deployment the operational owner compares the tag's ID to O-2's qualified ID;
after deployment it compares the actual runner image to that ID. Thus the normal
rollout consumes the artifact Fixture tested, rather than immediately replacing
it with another build. Code tests this helper only with a Docker shim; Code runs
no rollout phase.

The new runbook prerequisite names all these steps and their receipts. It uses
existing build/qualification tools, not a new implicitly building Fixture mode.
If the operator instead wants host-local building, a separate reviewed build-only
front door, including host slot acquisition without host pwsh, is needed before
that alternative is usable. Do not improvise it during the live gate.

Rejected: testing the previous standing image (cannot prove the 0755 fix or gated
source); deploying temp before Fixture (weakens the documented gate); invoking
`deploy-temp` merely to get its build side effect; calling `ensure_checkout` on
the shared tree (includes checkout/reset); and reusing `build_server2_images`
directly (also builds state-init and is not an isolated qualification front door).
Also reject treating a nested-daemon image as already present on the host daemon.

### D-2: Fixture pins provenance before allocating test resources

Add a Fixture-only resolver near `c849_image`. Require the target tag to exist,
resolve its `sha256:<64 hex>` ID, and read `org.opencontainers.image.revision`
from that ID; it must equal the full requested SHA. Refuse with distinct proposed
codes `FixtureImageMissing`, `FixtureImageRevisionMismatch`, or
`FixtureImageIdentityInvalid`. Emit the receipt in the calling shell, not inside
a lost command substitution. Cache the admitted ID for all Fixture groups,
including F-5/F-6/F-8 and final evidence; a later tag retarget must not change the
image in use. A removed image fails; it never reselects the standing runner.

Bind the image ID to the independently supplied qualification reference in the
operational record. Keep the existing nine-line Fixture summary and full roster;
write new provenance in a separate strictly parsed sidecar. Other cache contexts
retain their current helper fallback, especially CARD-0973 cold-marker recovery.
Reject a global removal of fallback and a tag-only check.

### D-3: stage the actual Fixture source without moving the shared checkout

For `runner-cache-fixture` only, have `c590-real.ps1` export a fixed allowlist from
Git objects at the requested SHA, never arbitrary working-tree bytes. Initial
allowlist: both server2 Compose YAML files, `scripts/c590-remote.sh`,
`scripts/build-slot.ps1`, `scripts/lib/build-slot.ps1`, and
`docker/session-runner-grok/verify-codex-image.sh`. TestDesign must census every
Fixture `$CHECKOUT` read and bind mount and freeze this list; adding an identified
dependency is allowed, exporting the whole working directory is not.

Transfer regular files to a fresh run-owned source directory, with a deterministic
manifest of relative paths, sizes and SHA-256 values plus full source SHA. Verify
the exact roster and hashes on the host before executing the staged remote script
and before any Fixture Docker allocation. Reject symlinks, unexpected/missing
files, failed upload, mismatched bytes, reused paths and an incorrect SHA. Bind
Fixture `CHECKOUT` and any derived Compose/script paths to this directory. The
front door requires clean tracked source and `HEAD=Sha`; no reset, chown of `/work`,
or general `ensure_dirs` call is admitted. Only the two read-only inventories may
inspect production resources.

Keep staged source out of the copied evidence directory; export its allowlisted
provenance sidecar, not the source bundle. Clean only the recorded staging root
after its child processes have joined. Existing non-Fixture cases keep their
checkout behavior. Rejected: stamping `source-sha` without binding the consumed
files, or reading the old checkout just because the image is now correct.

### D-4: preserve seed failure and exit status across subshells

Use a Fixture-local invocation helper for both first Seed and repeated Seed.
Capture child status immediately. Isolate or clear the child receipt slot before
each invocation, so an earlier PC/repeat cannot supply the diagnosis. If this
invocation produced a valid failed receipt with a nonempty diagnosis and matching
nonzero exit, preserve it and propagate that exit. If it did not produce a valid
failure receipt, emit `FixtureSeedFailed` or `FixtureSeedRerunFailed` as today.
Never adopt an earlier success, a malformed receipt, or a receipt from another
invocation. Keep raw `smoke.txt` beside the primary failure.

Scope this fix to Fixture; do not redefine `write_result` globally for all C590
cases. The helper must not hide Bash `set -euo pipefail` failures or turn a
fixture/setup error into an expected red control.

### D-5: primary result and cleanup result are independent (operator decision)

Default: preserve the case's primary status/diagnosis even if teardown fails.
Write a separate `fixture-cleanup.json` containing run/source identity, bounded
owned-resource identities, per-resource outcome, and `complete`. The gate accepts
only when primary success, cleanup complete, provenance valid and evidence
transfer complete all hold. A primary success plus residue is reported as
`FixtureCleanupIncomplete` by the front door, while the remote primary result
remains successful; a primary failure retains its original diagnosis. This
implements the card's instruction that cleanup must not replace the successful
case exit without silently allowing a dirty Fixture gate to count as complete.

Replace the inline EXIT trap with an auditable Fixture finalizer. Capture `$?`
first, disable recursive trapping, await owned children, remove only recorded
run-owned containers/Compose projects, then volumes and network, then host trees.
Verify absence; suppressing stderr does not establish removal. Preserve the
existing PC-32 protection against production cache names. Validate any ledger
identity against the run namespace before issuing a destructive command.

Use narrowly scoped `sudo -n rm -rf -- <exact-owned-root>` for mixed-uid Fixture
scratch only after its containers and volumes are gone. Require the exact
run-derived path, ownership marker and canonical parent; reject empty/root/foreign
paths, symlink ancestors, symlink roots and nested mounts. No glob deletion and no
recursive chown of the shared host root. Missing privilege is recorded residue,
not a fallback to broader deletion. Do not delete copied evidence. Install cleanup
as soon as ownership is established, including setup and interruption exits.

This covers both root npm seed directories and uid-1654 F-6 directories. Keeping
the F-6 consumer at uid 1654 is part of its proof. Reject chmod 0777, ignoring rm
failure, changing all probes to root, and sweeping old `/tmp/c849-fixture-*` trees.
Old audited residue is a separate human cleanup decision, not this finalizer's
authority. TestDesign must exercise real owned filesystem paths plus a privilege
boundary shim; actual cross-uid deletion remains a live acceptance obligation.

### D-6: remove PC-08's link before evidence export

Create the self-link only for its negative probe. Unlink the exact recorded
`.fixture-escape` immediately afterward and in the finalizer on early failure.
Never recurse through it. Before export, check the evidence file roster for
symlinks/special entries and refuse unexpected entries without following them.
The PC must still observe `CacheTargetInvalid` and retain its control receipt.
Do not remove the negative probe or count a setup failure as its expected red.

### D-7: F-6 hashes inside the pinned image

Run a foreground, disposable container of the admitted image with `--network none`,
uid 1654, an overridden pwsh entrypoint and a read-only mount of the exact tarball.
Use a small script/file argument with explicit parameter binding, not ambiguous
PowerShell `-Command` trailing arguments. Require exit zero and exactly one valid
base64 SHA-512 digest. Compare fixed tarball bytes against an independent .NET
oracle in regression tests. No host pwsh call is allowed.

Keep the HTTP fill, server stop, offline B install, exact sentinel and PC-19 empty
cache miss. No `file:` fallback, visible tarball fallback, network during offline
install, or softened integrity check. Reject moving the hash to the old image or
installing PowerShell on the host.

### D-8: evidence copy is part of acceptance

For Fixture in `Invoke-C590LiveCase`, capture upload, SSH and scp statuses
immediately. Nonzero copy-back produces `FixtureEvidenceCopyFailed` even if the
result JSON arrived first. Preserve any remote primary receipt already received;
write transport failure in the local wrapper receipt. Nonzero SSH cannot be
masked by an accepted remote receipt. Missing or malformed required files also
refuse. Keep recycle's existing receipt error behavior unchanged.

Extend the front door to require provenance and cleanup sidecars, with exact
run/SHA/image matching, alongside the unchanged summary/groups/families/variants.
Only the final desktop front door prints the authoritative `C849_FIXTURE` success
line after all these gates. Remove/defer the remote premature success line;
streamed PASS records are observations, not a completed receipt.

### D-9: ordinary regression and operational qualification remain separate

Read `/api/runner-defaults` and `/api/session-runners` again at dispatch time.
This Plan read both on 2026-10-05: defaults revision 2; the default runner reported
Linux and available; a Windows lane was available, and the temp entry was offline.
These are observations, not permanent placement instructions. Omit `-Runner`
unless pinning one host deliberately. The Bash/uid/symlink regressions need Linux;
PowerShell receipt/transport harnesses are portable. Omit `-Platform` unless that
row needs an OS; `-Platform Any` unpins inherited requirements. No fleet address
is part of the plan's checkpoint commands.

Do not use a whole Unit run or assembly run. Mutations remain method-scoped and
post-land under the established SourceLanding process. The operational Fixture's
32 control families are a different roster from this card's regression PCs below.

## Implementation slices

Each slice is 30-60 minutes of authoring; commit and push it before proceeding.
The checkpoint table batches builds after related committed slices. No live
operation belongs to a slice. If a slice expands beyond an hour, split its named
files/behaviors before starting rather than widening test scope.

| Slice | Minutes | Files and concrete change | Tests / checkpoint |
|---|---:|---|---|
| S1 | 45-60 | `scripts/c590-remote.sh`: Fixture-only resolver and immutable ID. `tests/Antiphon.Tests/Scripts/CacheFixtureImageTests.cs` (new); adjust only Fixture argument in `RemoteScriptContractTests.cs`. | T01-T04, four retained C973 arguments; CP-1/CP-2 after S2. |
| S2 | 45-60 | `scripts/c590-real.ps1`, new `scripts/lib/c1068-fixture-source.ps1`, `scripts/c590-remote.sh`: Git-object source staging, early binding and validated provenance. New `scripts/fixtures/c1068-fixture-source.ps1` and `CacheFixtureSourceTests.cs`. | T05-T07; CP-1/CP-2. |
| S3 | 30-45 | `scripts/c590-remote.sh`: scoped first/repeat Seed result propagation. New `CacheFixtureSeedResultTests.cs`; real subshell/helper execution in new `scripts/fixtures/c1068-fixture.sh`. | T08-T10; CP-3 after S4. |
| S4 | 45-60 | `scripts/c590-remote.sh`: finalizer, exact-path cleanup, PC-08 teardown, separate cleanup receipt. New `CacheFixtureCleanupTests.cs`; same shell harness. | T11-T14; CP-3. |
| S5 | 30-45 | `scripts/c590-remote.sh`: containerized F-6 hash. New `CacheFixtureNpmTests.cs`; fixed-byte oracle and blocked host-pwsh boundary in shell harness. | T15-T16; CP-4. |
| S6 | 45-60 | `scripts/c590-real.ps1`, `scripts/verify-card0849-caches.ps1`: fail closed on transfer, cross-check sidecars, final success timing. New `CacheFixtureReceiptTests.cs`, `scripts/fixtures/c1068-fixture-receipts.ps1`; update affected C849/C913 receipt fixture producers. | T17-T20 and bounded receipt/front-door regressions; CP-5/CP-6 after S8. |
| S7 | 30-45 | `docs/docker-stack.md`: explicit build/qualify/import prerequisite, full gate and cleanup/transport interpretation; update runbook source/image provenance and qualification-ID limits. This plan's TestDesign appendix freezes final roster/counts before Code. | CP-5/CP-6 after S8; documentation diff and no weakened gate. |
| S8 | 30-45 | `scripts/c590-remote.sh`: narrowly reuse a fully stamped existing runner image in `build_server2_images`, record its ID, preserve the state-init build and absent-image behavior. New `CacheFixtureImageReuseTests.cs`; offline command trace only. | T21-T22; CP-7, batched with CP-5/CP-6 after S6-S8. |

The S6 fixture-producer updates are specifically
`scripts/fixtures/c913-receipts.ps1` and the embedded C849 front-door harness in
`tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`; keep their older
non-Fixture receipt contracts unchanged. S1's compatibility harness is
`scripts/fixtures/c973-marker-reader.sh`, changed only if its Fixture setup needs
to reflect the removed argument. No other C973 expectation is retired.

Use the existing `RemoteScriptContractTests.LinuxShell`/ScriptHarness facilities
where suitable, with the assembly-local `ParallelLimiter<ProcessSpawnLimit>`.
Own and join subprocesses. Tests must extract/run production functions, not
duplicate their decisions in a fake implementation. A new harness must explicitly
check child exit and stderr because `LinuxShell` alone returns stdout without an
exit assertion. All Docker/SSH/scp/sudo/HTTP boundaries in ordinary Code tests
are fail-closed shims over test-owned paths; no command may fall through to a live
daemon or remote host. Do not add user-writable production bypass switches.

## Test-design handoff

TestDesign must add the executable `## Verification design` before Code. Freeze
the proposed method names, independent oracles, source allowlist, exact assertions,
runtime categories and costs; audit cleanup authority and producer-to-recipient
receipt delivery. The current plan deliberately does not claim those tests exist.
No test/build was run in Plan; the table's counts are proposed unparameterized
method counts, except the explicitly expanded existing C1066/C973 regressions.

### Behavior and positive-control inventory

Each T is one new TUnit method; no new `[Arguments]` expansion is planned.
Each has one causal PC. Several adverse inputs can exercise the same behavior,
but one method's PC changes only its named guard. Prefix the controls `C1068-PC`
to distinguish them from the operational fixture's PC-01..PC-32. All new class
paths are `tests/Antiphon.Tests/Scripts/<Class>.cs`.

| T / owning class.method | Green evidence / adverse inputs | C1068-PC: single production mutation and expected failed assertion |
|---|---|---|
| T01 `CacheFixtureImageTests.Missing_target_refuses_without_donor_lookup` | Missing SHA tag with a valid standing image still refuses before resource allocation; no donor/compose lookup. | 01: restore Fixture fallback; `missing-target-refused` fails. |
| T02 `CacheFixtureImageTests.Full_revision_is_required` | Matching 12-character tag with wrong/missing full label refuses; full exact label accepts. | 02: bypass full revision comparison; `wrong-revision-refused` fails. |
| T03 `CacheFixtureImageTests.All_groups_use_one_immutable_image` | Retarget the tag after admission; actual helper argv and summary still use admitted ID. | 03: re-resolve the tag for a later group; `all-groups-use-admitted-id` fails. |
| T04 `CacheFixtureImageTests.Nonfixture_fallback_is_preserved` | Seed, Inventory, PrunePreview and Reset retain their permitted current-main recovery. | 04: remove one permitted non-Fixture fallback; `cold-helper-preserved` fails. |
| T05 `CacheFixtureSourceTests.Source_files_come_from_the_requested_commit` | A stale host tree and changed worktree bytes cannot supply Fixture inputs; exact Git bytes and manifest match. | 05: export a working-tree file; `git-object-bytes-consumed` fails. |
| T06 `CacheFixtureSourceTests.Invalid_source_bundle_refuses_before_execution` | Missing, extra, linked, corrupt or wrong-SHA source refuses before remote execution/Docker allocation. | 06: bypass payload hash validation; `corrupt-source-no-execution` fails. |
| T07 `CacheFixtureSourceTests.Every_fixture_reader_uses_staged_source` | Trace F-1 and helper bind mounts to admitted paths; stale shared-checkout sentinel is never read. | 07: leave Compose path bound to old checkout; `staged-compose-consumed` fails. |
| T08 `CacheFixtureSeedResultTests.Inner_seed_diagnosis_survives` | Real nested failure writes `CacheSeedSmokeFailed`; parent exit and primary receipt retain it. | 08: restore generic overwrite; `inner-diagnosis-retained` fails. |
| T09 `CacheFixtureSeedResultTests.Undiagnosed_failure_has_fallback` | Child nonzero with no valid receipt yields the first/repeat-specific generic reason. | 09: suppress fallback emission; `undocumented-child-has-receipt` fails. |
| T10 `CacheFixtureSeedResultTests.Repeated_seed_cannot_reuse_old_receipt` | Earlier success/failure cannot replace the current invocation's failed result. | 10: retain the previous child receipt slot; `current-invocation-only` fails. |
| T11 `CacheFixtureCleanupTests.Mixed_owner_scratch_uses_exact_owned_cleanup` | Privilege shim plus real owned filesystem verifies resource-removal order and exact scratch target; green and early-red paths both clean. | 11: use unprivileged final rm; `owned-scratch-removed` fails in the ownership model. Live acceptance supplies actual uid proof. |
| T12 `CacheFixtureCleanupTests.Foreign_or_uncertain_resources_are_retained` | Foreign paths/names, symlink root/ancestor, nested mount, live/unknown child, or removal failure cannot authorize broad deletion; sibling sentinel survives. | 12: bypass exact-root guard; `foreign-root-preserved` fails. |
| T13 `CacheFixtureCleanupTests.Cleanup_failure_preserves_primary_result` | Primary green/red status is unchanged; cleanup failure sidecar records residue. | 13: return cleanup status as primary; `primary-exit-preserved` fails. |
| T14 `CacheFixtureCleanupTests.Pc08_link_is_removed_on_all_exits` | Real link exercises target refusal; ordinary and injected early exits leave no link before export; sibling evidence stays intact. | 14: disable early-exit link unlink; `early-exit-no-export-link` fails. |
| T15 `CacheFixtureNpmTests.Integrity_hash_runs_in_pinned_image` | Host pwsh is a rejecting sentinel; container argv is uid 1654/network none/read-only exact tarball and admitted ID; digest equals independent SHA-512 oracle. | 15: restore host pwsh hash; `host-pwsh-unused` fails. |
| T16 `CacheFixtureNpmTests.Malformed_or_failed_hash_refuses_install` | Failed, empty, extra-line or malformed digest stops before fill/install; valid bytes preserve offline PC-19 behavior. | 16: bypass digest validation; `bad-digest-no-install` fails. |
| T17 `CacheFixtureReceiptTests.Partial_copy_cannot_accept_fixture` | Real PowerShell bridge with shim scp writes accepted result first, then exits nonzero; wrapper refuses and retains remote facts. | 17: ignore Fixture scp failure; `partial-copy-refused` fails. |
| T18 `CacheFixtureReceiptTests.Transport_failure_cannot_accept_stale_success` | Nonzero SSH and stale accepted JSON cannot print success; actual remote failure remains inspectable. | 18: ignore SSH status; `transport-status-required` fails. |
| T19 `CacheFixtureReceiptTests.Success_requires_complete_bound_sidecars` | Missing/failed/mismatched cleanup/provenance or linked evidence refuses; complete matching files accept and print one final success. | 19: omit cleanup completion check; `residue-blocks-gate` fails. |
| T20 `CacheFixtureReceiptTests.Complete_existing_roster_remains_required` | All nine groups/32 families/47 variants/two inventories required; missing late F-6/PC-32, duplicate variants and wrong SHA refuse. | 20: omit variant roster validation; `missing-late-variant-refused` fails. |
| T21 `CacheFixtureImageReuseTests.Existing_target_is_reused_without_runner_build` | Exact full revision and qualified ID survive the real helper; command trace contains state-init build only and records the reused runner ID. | 21: unconditionally rebuild the existing runner target; `qualified-runner-not-rebuilt` fails. |
| T22 `CacheFixtureImageReuseTests.Conflicting_target_refuses_and_absence_still_builds` | Wrong/missing revision on an existing tag refuses without replacing it; absent target retains the existing runner/state-init build sequence. | 22: treat a conflicting target as absent and rebuild; `conflicting-tag-not-replaced` fails. |

T15's shim proves placement/argv and independent digest validation, not that
Docker is operational. T11's privilege model does not prove host sudo policy.
Those gaps are deliberate obligations of O-2 below. TestDesign must avoid
claiming trace/static assertions alone prove actual image or permission behavior.

For every PC run baseline green, apply only the named production mutation, run
`/*/*/<Class>/<ExactMethod>` and require its expected assertion failure, restore,
then run the same method green. Zero tests, setup/build errors and timeouts are
not red. Do not mutate a whole class at once or run whole-Unit PCs. Most controls
touch the same shell file and must be serial. SourceLanding mutations do not
commit or push snapshot changes; keep their evidence externally.

### Delivery and acceptance inventory

| Producer | Durable identity and transport | Consumer / independent verdict |
|---|---|---|
| Image qualification | full SHA + image ID + 11/11 result; archive digest verified after transfer and load | Operator records loaded ID and revision before tagging; Fixture independently inspects the image. |
| Source bundle exporter | full SHA + exact file hashes + run ID, verified at remote stage | Fixture reads staged bytes; copied provenance is matched by front door. |
| Seed child | current invocation receipt and actual child exit | Parent preserves valid inner diagnosis or emits explicit undiagnosed fallback. |
| Fixture groups and controls | run/SHA/image plus exact ordered-independent rosters | Front door rejects missing/duplicate/late omissions; passing prefix is insufficient. |
| Finalizer | separate cleanup receipt, primary result unchanged | Front door requires complete cleanup; residue and primary failure remain distinct. |
| SSH/scp bridge | actual SSH/copy exit plus complete local required files | Front door alone issues final success. A remote stdout line or partial JSON is insufficient. |

### Checkpoints

Proposed ordinary Code closed list, to be frozen by TestDesign after the decisions.
All rows run in the **Linux offline regression lane**: bash/pwsh/jq and local
filesystem harnesses, denied Docker/SSH/HTTP boundaries, no host mutation. Group
names carry the lane because the checkpoint schema has no `Lane` column.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1068-source/` | linux-offline-image-source | `/*/*/(CacheFixtureImageTests*)\|(CacheFixtureSourceTests*)/*` | T01-T07 | all 7 new methods, 0 failed/skipped | 7 | 6 | true |
| CP-2 | S1-S2 | CP-1 | linux-offline-cold-compatibility | `/*/*/RemoteScriptContractTests/C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune` | D-2 compatibility | exactly 4 retained context arguments, 0 failed/skipped | 4 | 1 | true |
| CP-3 | S3-S4 | `tests/Antiphon.Tests -> bin-c1068-lifecycle/` | linux-offline-seed-cleanup | `/*/*/(CacheFixtureSeedResultTests*)\|(CacheFixtureCleanupTests*)/*` | T08-T14 | all 7 new methods, 0 failed/skipped | 7 | 6 | true |
| CP-4 | S5 | `tests/Antiphon.Tests -> bin-c1068-npm/` | linux-offline-npm | `/*/*/CacheFixtureNpmTests/*` | T15-T16 | both new methods, 0 failed/skipped | 2 | 5 | true |
| CP-5 | S6-S8 | `tests/Antiphon.Tests -> bin-c1068-receipts/` | linux-offline-receipts | `/*/*/CacheFixtureReceiptTests/*` | T17-T20 | all 4 new methods, 0 failed/skipped | 4 | 6 | true |
| CP-6 | S6-S8 | CP-5 | linux-offline-fixture-regressions | `/*/*/RemoteScriptContractTests/(C1066_Fixture_tree_fault_reaches_validator_under_nounset*)\|(C1066_Fixture_npm_install_uses_argument_log_under_nounset*)\|(C1066_Fixture_warm_probe_uses_argument_log_under_nounset*)\|(C913_Fixture_uses_owned_payloads_and_proven_absent_temp*)\|(C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims*)\|(C849_front_door_passes_every_full_case_name_to_the_invoker*)\|(C849_Cache_receipts_exclude_credentials_and_payloads*)` | nounset, inventories, receipt shape, full front-door mapping, evidence allowlist | 15 executions: 6+2+3+1+1+1+1; 0 failed/skipped | 15 | 2 | true |
| CP-7 | S6-S8 | CP-5 | linux-offline-image-reuse | `/*/*/CacheFixtureImageReuseTests/*` | T21-T22 | both new methods, 0 failed/skipped | 2 | 1 | true |

The combined expressions use the documented CARD-0403 parenthesized operands
with suffix wildcards required by the pinned discovery hint extractor.
TestDesign must pin the exact executed roster; if necessary use separate
exact-method rows reusing CP-5 output in the same After group. Never widen to the entire large
`RemoteScriptContractTests` class. A changed filter/count requires an explicit
manifest amendment, not a quiet lower floor. One new class contains only its
listed methods, so class-scoped ordinary runs are bounded; PC runs stay methods.

Code uses `tools/Antiphon.Checkpoints` `run --plan <this-plan> --after <group>
--expected-source-sha <committed-sha>` via the documented build-slot launch gate;
await `wait` until exit is not 75. Commit before each build, keep source frozen
through the group's rows, retain the unedited CHECKPOINT lines/counts/source
provenance, and report reruns. Exit 4 is not permission for an unleased retry.
Clean every task-created `bin-c1068-*/` directory after its runs. Evidence stays
ignored. Code/Review run `scripts/check-evidence-diff.ps1` across the full task
range. No other build/test driver is authorized without an explicit reason.

### Cost

Authoring: 300-420 minutes in eight slices. Ordinary checkpoint floor: 27 minutes,
including four isolated builds. No whole Unit or broad suite. TestDesign owns
calibrating these estimates before Code; a demonstrated inherited failure is
checked with its exact filter at the base, not with another whole suite.
Twenty-two method-scoped mutation cycles are a separate post-land budget. Image
qualification/import and live Fixture time/storage are separate operational
costs requiring D-1's commission, not hidden in Code checkpoint estimates.

## Post-land operational acceptance (not Code checkpoints)

These rows are deliberately outside `### Checkpoints`. They do not authorize
operations during Plan, TestDesign, Code or ordinary Review.

| Gate / lane | Required action and evidence | Stop condition |
|---|---|---|
| O-1 / isolated Linux image qualification, then operator deployment transport | Build at clean landed SHA using `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1068-image -- pwsh -NoProfile -File scripts/verify-card0660-codex-image.ps1 -Target session-testing -Image <fresh-tag> -SourceRevision <sha> -ResultsRoot <fresh-root>`. Require 11/11, record immutable ID, save/transfer/load with verified archive digest, inspect loaded ID/full revision, add SHA tag only if absent or already identical. | Any red/unknown row, source mismatch, slot refusal, wrong daemon, transfer/hash/ID mismatch or conflicting tag. No overwrite, prune or fallback. |
| O-2 / canonical desktop front door to Linux deployment-host Fixture | From clean reviewed/landed canonical checkout, after existing host-jq qualification, run `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha <sha>`. Require image/source sidecars, primary exit 0, cleanup complete, transport complete, exact roster and final summary below. Read-only before/after observations show standing container ID/start time unchanged and production cache identities retained. | Any incomplete late group/control, leftover owned root/container/volume/network/link, failed actual cross-uid deletion, changed standing state, missing receipt or copy error. Retain diagnostics; do not Seed/deploy. |
| O-3 / separately commissioned rollout owner | Record acceptance against exact SHA/image ID, compare the target tag to that ID immediately before deployment, then return to the existing Inventory, Seed and staged rollout gates. The S8 helper reuses this runner artifact. Compare the actual deployed runner image to the qualified ID as part of smoke. This plan starts none of those operations. | Changed tag/ID or an unexpected rebuild stops the handoff; requalify the changed artifact and rerun Fixture before further rollout. A source SHA alone is not proof of byte-identical images. |

Required authoritative line, unchanged:

```text
C849_FIXTURE groups=9 controls=32 expectedRed=32 variants=47 expectedRedVariants=47 inventories=2 failures=0 productionMutations=0
```

S8 removes the current unconditional runner rebuild when the admitted target
already exists. The runbook records that reuse and the before/after image-ID
checks. This is a source change verified offline during Code, not authority for
Code to deploy, start, stop or restart a runner. Fixture proves its recorded image,
not an arbitrary later rebuild with the same source label.

The audited old scratch/evidence residue is not in this run's ownership ledger.
Its deletion still needs the separate human decision described on CARD-1068.
No runbook recovery uses Reset, broad rm, Docker prune or volume recycling for it.

## Plan validation and handoff

Source/card inspection confirms the premise; Investigate is not the next stage.
Read-only runner defaults/catalogue requests succeeded. Plan validation consists
of checking the artifact's links, table shape, exact method references and Git
diff, then committing/pushing this document on the assigned branch. No live
receipt or passing test result is claimed.

Decide should accept or replace D-1's explicit isolated build/qualification/import
route and D-5's primary-result/cleanup-gate split. Once settled, TestDesign turns
the bounded inventory into the final verification section and freezes the
checkpoint manifest before Code. Operators retain O-1/O-2 and existing-residue
authority. The full live gate remains outstanding until O-2 actually passes.
