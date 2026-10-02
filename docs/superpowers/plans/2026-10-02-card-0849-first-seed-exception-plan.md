# CARD-0912: CARD-0849 first Seed without draining server2

Date: 2026-10-02. Stage: Plan complete; next TestDesign. Baseline: `origin/master` at **`1cee012fce4444c0512d8057bbd68074297641eb`**, observed with `git ls-remote` and read from an independent throwaway bare repository. Assigned branch `feat/card-task-5ee2c9a3` starts at `64718dff14f659a63122c2fd68ef16ac0cb28102`. No fetch, pull, reset or ref update was performed in the shared Git store; this branch is not rebased. Only this Markdown file is delivered by Plan.

## Outcome and scope

Permit an explicitly selected saved-donor **first** Seed while the existing `server2` container continues accepting and executing work, but only after fresh proof that all three external cache volumes are absent or validated empty and have no consumers. Revalidate isolation around staging, import and publication. Require a real, bounded host build-slot lease for the smoke. Let CARD-0849 operational CP-3 Fixture obtain its private net9 payload from `/home/mc/runner-cache-donor/temp-runner-cache.tar` when the temp runner is retired and no temp container remains. Update `docs/docker-stack.md` with the exact operational sequence and recovery limits.

The exception belongs to the saved/no-container/unmarked Seed branch, not to `c849_prune_idle`. Reset, Prune, live-donor Seed, marker reuse, later `redeploy-old`, and their existing refusal codes retain their existing gates. Do not change runner admission, budgets, broker/server settings, images, Compose, provider state, session ownership or instruction bundles. No main drain is permitted as an automatic fallback when the exception refuses.

The operator's 2026-10-02 ordering is fixed: **Fixture, first Seed with main work in flight, deploy-temp, verify temp and complete a canary Plan task there, then drain-old and redeploy-old**. Do not run `-Phase all`, which crosses the canary checkpoint automatically.

## Evidence and limits

Read CARD-0912 in full with `pwsh -NoProfile -File scripts/card.ps1 get CARD-0912 -Board Antiphon`, then [the supplied investigation](../../investigations/2026-10-02-card-0849-seed-without-drain.md). Owners read include `docs/project-context.md`, the checkpoint/build-slot/filter rules in `docs/testing-and-build.md`, `docs/docker-stack.md`, and the relevant HTTP and orchestration contracts. Plan templates were the CARD-0826 and CARD-0866 plans at the frozen remote baseline; rollout predecessors were the CARD-0727 and CARD-0849 plans. This is a delegate, not an orchestrator; no agents were dispatched.

Measurements are repository reads only: `git status`, `git rev-parse`, `git ls-remote`, frozen `git archive`, `rg`, bounded source reads and a Node source census. The throwaway bare fetch was confined to `/tmp/card0912-plan-KHbCV3/base.git`. No tests/builds, SSH, host Docker calls, provider launches, drains, deployments or live runner probes were performed. Executed tests: **Linux 0, Windows 0**. Card/investigation host facts are historical, not refreshed measurements.

| Frozen source / supplied evidence | Finding and boundary |
|---|---|
| `scripts/c590-remote.sh:1629-1758` | `c849_seed` prepares volumes before deciding its source. The saved branch invokes `c849_prune_idle` before staging, after saved copy and before the marker. Source and gate discovery must move before any first-seed volume creation/probe. |
| `scripts/c590-remote.sh:1925-1985` | Full maintenance gate requires drained/zero runners, no unexpected app-uid processes/attachments and an idle broker. Missing retired temp has a separate strict status predicate. Preserve the whole function. |
| `scripts/c590-remote.sh:1660-1675` | Live-donor Seed separately requires the **donor's** drained zero status and no donor cache process; it stops/copies/restarts that donor. It does not currently call the main-runner maintenance gate. Preserve this actual contract; do not invent a pre-existing main-zero requirement or relax donor checks. |
| `scripts/c590-remote.sh:1435-1471,1716-1733`; `scripts/build-slot.ps1:85-90`; `scripts/lib/build-slot.ps1:100-149` | Smoke runs in a host-Docker helper, and the wrapper can currently return unleased/unlimited. A successful apphost line alone does not prove a lease. Renew failures are not currently propagated to the foreground driver. |
| `scripts/c590-remote.sh:2604-2627,2940-2947,3027-3042` | Fixture F-5 requires a temp donor; F-9 calls inventory which rejects absent temp. Both need a narrowly scoped saved-source path. |
| `scripts/verify-card0849-caches.ps1:18`; `scripts/c590-real.ps1:306-312,362-364` | Front door and bridge admit/upload the saved importer for Seed only. Both must explicitly include Fixture; no arbitrary case admission. |
| `scripts/c590-remote.sh:3618-3635,1727-1728,2657-2658` | Cache cases deliberately bypass checkout refresh, while Seed/F-5 helpers bind build-slot scripts from `$CHECKOUT`. Upload the reviewed strict-smoke pair with the case; otherwise a correct local patch could still execute an older host wrapper. |
| `scripts/deploy-server2.ps1:80-99,133-205`; `scripts/c590-remote.sh:3284-3288` | Temp counters and accepted marker precede temp creation; temp verification precedes its clear. Main zero/drain ownership remains mandatory for replacement. Existing explicit phases already support the required canary pause. No production wrapper edit is needed. |
| Current source census at the baseline SHA | `RemoteScriptContractTests`: 44 `[Test]` methods, **16 C849 methods**, no argument expansion. CARD-0902's front-door method is present. CARD-0905's AsyncLocal repair is absent at this SHA. `BuildSlotScriptTests`: 17 methods; one Linux-only SIGINT method. See exact roster below. |
| Current rolling source, recounted by expanding its loops | 17 groups, 48 child invocations, 154 assertions; these are **not 154 TUnit tests**. No TUnit wrapper for the standalone rolling harness was found in `tests/Antiphon.Tests/Scripts`. |
| Source comparison with assigned StartRef | `c590-remote.sh`, `c590-real.ps1`, `verify-card0849-caches.ps1`, `deploy-server2.ps1`, both build-slot scripts, `RemoteScriptContractTests.cs`, and `test-deploy-server2.ps1` are byte-identical to the frozen baseline. |
| Historical 2026-10-02 preflight | Three external volumes and the cache directory absent; main mounts only its four private named volumes; no container mounts the cache volumes; saved archive 2,931,107,840 bytes; broker had two occupied slots. These facts must be freshly proved by the future host code. Archive size is not extracted size. |
| Historical investigation status | Main accepting, counts 3/3/0; temp retired since 2026-09-30 16:59Z. No current availability, occupancy, archive integrity or Windows qualification is inferred. |

## Design decisions

### D-1: choose the exception once, under the maintenance lock

Refactor only Seed's source selection and guard dispatch. Acquire the existing host maintenance lock first. Resolve the source, marker existence, exact main container and temp state **before** `c849_prepare yes`. Treat any marker path, including a zero-length/damaged marker or symlink, as ineligible for the exception; an accepted marker uses the existing idempotent validation path. A populated unmarked cache must never fall through from a failed exception to another copy mode.

First-seed eligibility requires all of the following:

1. `CASE=runner-cache-seed`, explicit nonempty `C590_SAVED_DONOR`, no live/stopped donor and no accepted or partial marker. Saved+container refuses `CacheDonorSourceConflict`; no saved source retains `CacheSeedRequired` and its actionable command.
2. Temp status is HTTP 200 with `retiredAt != null`, `available=false`, `dispatchEligible=false`, `draining=true`, `acceptingNewWork=false`, `redirectTo=server2`, `retireWhenIdle=true`, integer sessions/queue zero and runnerSessions explicitly null or integer zero. Enumerate **all** temp project containers, including stopped; none may exist. Missing fields are unknown, not the same as explicit null.
3. Exactly one running main session-runner container with the expected Compose project/service labels and a full inspected ID. Snapshot its complete filtered mount identity (type/name/source/destination/RW) and image. Main status must be readable and internally valid, with integer nonnegative counters; counts may be nonzero, draining may be false and accepting may be true. The guard does not demand a main zero window or inspect main app-uid builds as cache consumers. It never changes the main status.
4. Each fixed cache name is absent, or is local/options-empty, correctly owner/schema/role-labelled, a nonsymlink root owned `1654:1654` mode `0700`, and empty including hidden entries. Distinguish a confirmed absent volume from failed/malformed Docker output: an inspect error alone is not absence. Use a successful complete name census plus exact inspect results; unknown refuses. Read-only volume inspection helpers use `volume-nocopy`, no chmod/chown/write probes in preflight.
5. No container in a complete `docker ps -aq` census mounts a target volume, including main, another runner, a stopped container or an unrelated project. Validate exact mount identities rather than trusting names/labels alone. Include bind sources equal to, below, or an ancestor exposing an existing target's canonical Docker mountpoint; a broad host Docker-root bind is not proof of isolation. Main must also omit mounts exposing the three cache destinations. Docker enumeration/inspect failure refuses. No prefix-wide helper exemption is allowed.

Only this proof replaces the saved branch's main drain/zero/process/broker-idle checks. The separate retired-temp predicate, source validation, image identity, volume/root safety, and helper smoke remain required. Do not add a user override flag. Reuse predicates through narrow helpers where needed without changing the full-maintenance predicate's semantics.

Rejected: relaxing `c849_prune_idle`; draining main without waiting for zero (still stops starts); using the earlier survey, zero counters alone, main-only mounts or running-container-only enumeration; retrying a populated unmarked seed; changing `Assert-TempSeedCounters` or `OldRunnerDrainConflict`.

### D-2: phase-aware rechecks, named refusals and honest rollback

Capture proof in a per-invocation local record; never infer proof from a prior receipt. Check identity/isolation before source staging, **after the archive-to-stage copy**, immediately before each target import, after each import helper exits, after the smoke helper is removed and immediately before atomic marker publication. Verify temp state, exact main ID/image/mounts and the all-container census at those boundaries. Require helper removal success before publication.

The empty-volume test applies before any target write and again after staging. Immediately before importing each still-empty target, prove it is empty again. **After import the correct invariant is the validated imported payload, not empty volumes:** verify labels/root identity, retained host/reference hashes and staged/package/npm checks; scratch starts empty but may hold this smoke's locks afterward. A second empty test after import would make success impossible. During a helper's own operation its exact newly captured ID is the sole scoped attachment allowance; at quiescent rechecks there are no allowed consumers at all.

| Refusal code | Meaning / detecting roster |
|---|---|
| `CacheFirstSeedPreconditionUnknown` | Missing/malformed status, identity, volume/census/root/disk facts or incomplete inspect. FS2/FS4. |
| `CacheFirstSeedMainMountChanged` | Main ID, image or filtered mount snapshot changes between boundaries. FS3. |
| `CacheFirstSeedVolumeInUse` | Main or any running/stopped container mounts a target, including bind aliases; observed initial main cache destination also refuses here. FS2/FS3. |
| `CacheUnmarkedContent` | Any target initially contains a file/directory/hidden entry, or a still-empty target gains content before its import. Existing code retained. FS2/FS3. |
| `CacheVolumeForeign`, `CacheRootInvalid`, `CacheRootOwnershipInvalid` | Existing driver/options/labels/root/mode refusals retained; do not silently repair an existing foreign root. Existing C849 prepare tests plus FS2. |
| `CacheFirstSeedTempNotRetired` | Readable temp facts violate the exact retired predicate. FS4. |
| `CacheTempContainerExists`, `CacheDonorLookupFailed`, `CacheDonorSourceConflict` | Existing absent-temp/source lookup/conflict codes retained. FS4/FX2. |
| `CacheFirstSeedDiskLow` | Known free space cannot retain the floor plus remaining staged/import allocations. FS4. |
| `CacheFirstSeedBrokerUnavailable` | Missing/disabled/malformed/unreachable shared broker or wrong/unproved endpoint/liveness mode. Occupied slots alone do not cause this refusal. FS4. |
| `CacheFirstSeedLeaseRequired`, `CacheFirstSeedLeaseTimeout`, `CacheFirstSeedLeaseLost`, `CacheFirstSeedLeaseReleaseFailed`, `CacheFirstSeedSmokeTimeout` | Strict wrapper diagnoses mapped at the Seed boundary, never collapsed into successful smoke. LS1..LS8/FS5. |
| `CacheSeedProbeCleanupFailed` | Smoke/helper container cannot be confirmed removed; no marker. FS5. |
| `CacheSmokeToolsInvalid` | Reviewed run-scoped smoke/importer upload missing, mismatched or outside its fixed root; no stale-checkout fallback. FS5/FX2. |
| Existing archive/tree/import/smoke/recovery diagnoses | Keep unsafe path/entry/duplicate/size, missing net9 pack, npm integrity, `CacheSeedImportFailed`, `CacheSeedSmokeFailed`, recovery and marker-validation failures. Existing C849 tests plus FS5/FX2; no broad renaming. |

Use `write_result false <code> 2` for host refusals. Wrapper slot timeout remains exit 4 and becomes `CacheFirstSeedLeaseTimeout` in Seed's result; never run unleased after it. Other strict wrapper failures are nonzero with a bounded machine-readable reason. Test every new code and preserve existing codes at their existing full-gate call sites.

**Rollback boundary:** a preflight refusal leaves target contents, runner state/admission and donor tar unchanged (only scoped evidence/lock bookkeeping may exist). Once target copy starts, a failure can leave unaccepted partial target contents, as it does today. Do not promise a transaction that Docker volume copies cannot provide. A refused Seed still leaves the standing runner, its mounts, admission and accepted caches unchanged; it writes no accepted marker, stops/removes only its own helper, and retains the original tar. Clean only its verified private stage using existing confinement; retain diagnostic/recovery evidence. Never clear new content after detecting a consumer, and never invoke Reset automatically. Recovery of partial volumes needs the **unchanged full Reset gates** and therefore may require a later operator-approved drain; until then, keep main accepting and defer rollout. Reset refuses an accepted marker with `CacheSeedAlreadyReady`.

The maintenance lock serializes the supported deployment lane, not arbitrary host-root Docker clients. Repeated checks detect observed races, not an atomic global exclusion against an out-of-band attach between checks. Operator deployment through another host lane must remain stopped for this short maintenance operation; ordinary runner work continues. Existing nested Docker cannot attach these host volumes. Do not broaden this patch into a daemon authorization redesign.

Rejected: post-import emptiness; accepting changed mounts because the labels still match; deleting partial contents on uncertain ownership; claiming every mid-copy failure leaves zero disk changes; automatically draining to recover.

### D-3: one strict lease covers the complete seed smoke

Add opt-in `-RequireLease` and a bounded driver lifetime option to `scripts/build-slot.ps1`, implemented with its existing lease library; default callers keep their current behavior. Seed passes strict mode with **five minutes maximum acquisition wait and five minutes maximum smoke-driver lifetime**. One lease covers restore, build and executable run in the single shell driver, through completion and cleanup. Keep explicit `-maxcpucount:1`, `-nodeReuse:false`, empty package sources and `--no-http-cache`. No nested lease for each dotnet command.

The helper is created by **server2's host Docker daemon**, joins `antiphon-build-slots`, and reaches `http://build-slots:8080/build-slots`. It does not start dockerd, use the standing runner's nested daemon, mount a Docker socket, or touch `antiphon-runner_dind-data`. It shares the same broker as the standing runner's builds. Prove that broker is reachable/enabled with a valid budget/occupied/lease/memory response and renew-mode grants; do not start/recreate it or fall back to a different endpoint. Main builds can continue in other granted slots. This prevents bypassing the host build budget; it is **not exclusive CPU/disk reservation**.

For Seed/Fixture, the bridge uploads `build-slot.ps1`, `lib/build-slot.ps1` and the importer from the reviewed source to an owner-only, run-scoped `/home/mc/antiphon-c590/cache-tools-<run>/` tree, preserving their relative script layout. Verify the three file hashes against the source-bound manifest before helper use; reject symlinks or a reused/mismatched tools tree. Bind that pair read-only into strict helpers instead of `$CHECKOUT/scripts/...`, and point saved-copy at this run's importer. Derive the host path from validated RUN, never arbitrary manifest path text. This avoids fetching/updating the host checkout or restarting a broker to install the feature. Record only source/hash identity. Clean only the exact owned tools tree after helper removal; retain it on uncertain helper ownership. Other cache/verification callers retain their current binding behavior. FS5/FX2 cover missing/mismatched tools; FX1 asserts the actual helper binds use the uploaded pair.

Strict mode accepts only a well-formed granted lease with nonempty ID, positive CPU bound, future expiry covering the bounded driver, and valid renewal interval. Reject `-NoSlot` combined with strict mode, unlimited, unleased, malformed/expired grants, unsupported PID-only cross-container grants and definitive HTTP refusal before launching the child. Busy/memory-floor 409 waits in the existing FIFO discipline up to the five-minute bound. Transport/protocol failure refuses without unleased fallback. Carry a typed outcome/diagnosis to the caller; stdout containing the word granted is insufficient.

Maintain the renew-mode lease (baseline broker interval 20 seconds, grace 90 seconds, TTL 90 minutes; do not change them). GET exposes enabled/budget/memory, and the grant exposes expiry and renewal interval; it does not expose grace, so do not invent a wire field. Under strict mode, surface any failed renewal/dead renewal worker to the foreground loop immediately, and stop **only the wrapper-owned smoke child tree** on loss or its five-minute deadline, before reporting refusal. A final renewal/held check precedes success; DELETE/release must return the normal success receipt. Failed release yields nonzero with no seed marker, records the bounded diagnosis and relies on existing broker expiry for recovery; never claim release succeeded. This is bounded test-driver custody, not permission to stop Working sessions or other lease holders.

Preserve grant, wait, renewal and release evidence under the scoped case directory; final Seed success requires matching lease identity and `C849_SMOKE ... stdout=CARD0849_APPHOST_OK`. Do not dump environment, broker headers or other holders' task labels. Ordinary cache verification and maintenance callers keep their prior behavior unless explicitly passing strict mode; Fixture's saved-source apphost smoke uses the same strict option.

Import is serial under the cache lock but is file I/O rather than a dotnet driver. Before staging use a conservative reserve: 20 GiB floor plus two copies of the admitted maximum 10 GiB packages + 2 GiB npm, plus 256 MiB scratch. Check both stage and Docker-root filesystems; if shared, account once for their combined requirement. Before import replace the conservative estimate with measured validated stage bytes plus remaining copy/scratch allowance; recheck the 20 GiB floor before marker publication. Existing importer size/path checks stay intact. This may defer on low headroom even if the particular tar is small; it must not drain main. Host disk latency and the broker's four-seat/16-GiB memory policy remain operational risks; a slot does not reserve disk bandwidth or guarantee future free memory.

Rejected: `occupied < budget` as a lease; waiting for all slots idle; granting unlimited/unleased permission; using the helper's loopback broker; a host Docker smoke on the runner's nested daemon; silent renewal loss; changing default build-slot behavior for unrelated callers.

### D-4: saved Fixture source uses the existing validator and private resources

Admit `-SavedDonor` for **Seed and Fixture only** in the front door and bridge. Keep canonical absolute host path syntax and upload `c849-import-saved-donor.ps1` for both cases. Never copy the archive to the desktop/checkout or infer its path when omitted. Preserve CARD-0902's full case-name routing and all other case restrictions.

In F-5, select either the existing container source or explicit saved source. Saved mode requires the strict retired-temp/no-container proof and refuses ambiguous saved+container input. Use `c849_saved_copy` and `c849_validate_seed_tree` into a private `/tmp/c849-fixture-<run>/` stage; bind the source read-only, retain archive identity/hash before/after, and refuse a changed source (`CacheSavedDonorChanged`). Apply the same identity check to first Seed's saved copy. This is a source hash/metadata receipt, never a copy of package contents. Keep all importer traversal/link/special-file/duplicate/size refusals and net9 host/reference completeness checks. The importer itself needs no edit.

Populate only F-5's recorded `c849<run>...` package/scratch resources from that validated stage; preserve populated-positive and empty-cache-negative controls and the SDK-pack-absent sensitivity check. No dummy temp container is created. The existing live-donor F-5 branch stays available and retains its before/after hash checks.

For F-9's inventory, pass an explicit internal `allow-retired-absent` argument **only from saved-source Fixture**. An absent temp then produces the actual filtered status JSON (including available/retired/admission fields and explicit null runnerSessions), `container=absent`, `mounts=absent`, and no `docker exec`. Reapply the retired predicate and full temp-project census. Do not synthesize a live zero or treat missing main, disconnected-but-not-retired temp, or an inspect error as absent. Default Inventory behavior and Both/Retired acceptance stay unchanged.

Retain the exact Fixture summary: nine F groups, 26 PC expected reds, two inventories, zero production mutations. Extend existing offline tests to validate the new evidence fields and source transport; do not increment live F/PC totals for assertions already inside those groups. F-4 still needs two real broker slots for its private concurrent restore barrier; if slots cannot become available, defer Fixture rather than draining main. Its existing consumer/slot controls remain unchanged by this first-seed exception.

Rejected: synthetic donor containers; direct tar extraction; copying whole homes; source fallback on lookup errors; silently selecting the default archive; allowing absent temp for every inventory caller; declaring CP-3 passed using offline tests alone.

### D-5: preserve rollout, privacy and review boundaries

`deploy-server2.ps1` is read-only for this card. Its Seed marker reuse, temp hold/verify/clear, and later main-zero/drain ownership checks already supply the required phase boundaries. Strengthen only its offline harness to assert busy accepting main remains untouched during saved-source deploy-temp. Use manual explicit phases for the operator's canary pause, not a new orchestration feature.

Evidence is limited to selected status fields, container/image/volume identity, hashes, aggregate bytes, phase/lease outcomes and bounded refusal codes. No archive, metadata contents, provider credentials, full Docker inspect/environment or arbitrary command lines enter receipts. Preserve the existing toxic-sentinel receipt test and extend it to new first-seed/Fixture receipts. No instruction bundle edit is needed; CARD-0884's 30,000-character guard remains irrelevant to this footprint.

Final Review follows the 2026-10-01 regression-only rule: failure requires an existing-behavior regression or a new reachable fail-open/privacy exposure. Missing evidence remains pending and must not be labelled passed. Do not pad the roster with unrelated provider/land/checkpoint suites or style-only requirements.

Rejected: auto-advancing past temp's canary; changing shared broker capacity; editing stage bundles to explain a script feature; claiming publication proves host activation.

## Exact implementation footprint and slices

Only this plan changes now. Future Code's closed footprint is below; new test names are prospective until TestDesign freezes their implementations. No project file change is needed.

| Slice | Exact files | Test-first work |
|---|---|---|
| S1: detecting contracts | `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`, `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs`, `scripts/test-build-slot.ps1`, `scripts/fixtures/c589-slot-shim.ps1`, `scripts/fixtures/c589-command-shim.ps1`, `scripts/test-deploy-server2.ps1` | Add the FS/FX/LS cases below and extend existing front-door/rolling scenarios before production changes. Use real shell functions and jq against scripted facts, isolated child shims and barriers. Commit/push the compiling test slice and record the declared red rounds. Preserve the landed CARD-0905 helper fix. |
| S2: first Seed and strict smoke | `scripts/c590-remote.sh`, `scripts/c590-real.ps1`, `scripts/build-slot.ps1`, `scripts/lib/build-slot.ps1` | Implement D-1..D-3, reviewed tools upload/binds and saved-source identity recheck. Keep strict mode opt-in and shared maintenance code unchanged. Test clocks/lease responses belong in existing offline seams, not production timing margins. Commit/push. |
| S3: Fixture saved-source wiring | `scripts/c590-remote.sh`, `scripts/c590-real.ps1`, `scripts/verify-card0849-caches.ps1` | Implement D-4; preserve all full case names and the 9/26 Fixture census. Commit/push. |
| S4: operator documentation and final qualification | `docs/docker-stack.md`, this plan (actual roster/receipt references only) | Document predicate/codes, strict bounds/daemon, saved Fixture command, canary ordering, rollback boundary, unchanged Reset/Prune/live-donor gates and donor retention. Commit/push, then final closed rows at that clean SHA. |

Read-only dependencies: `scripts/deploy-server2.ps1`, `scripts/verify-docker-stack.ps1`, `scripts/c849-import-saved-donor.ps1`, `scripts/fixtures/c727-fake-http.ps1`, `scripts/fixtures/c727-fake-verify.ps1`, Compose/Dockerfiles, broker production code and checkpoint tool. Existing c727 fault injection can set busy main without a new shim edit. A newly necessary path requires a stated footprint update before editing; it is not implicitly included.

### Collision order

These states come from the brief and frozen source, not fresh dispatch admission. The caller refreshes effective pipeline/runner/default/host limits and scoped tasks before Code; this Plan dispatches nothing.

| Other work | Exact decision |
|---|---|
| CARD-0905 | **Serialize Code after it lands**: same `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`, including helper/AsyncLocal repair. TestDesign must reread/recount its landed form. Start the later Code worktree from a base containing it; do not rebase/amend/reset this pushed Plan branch. If Code was already started, defer and let the caller arrange a new correctly based task rather than overwrite the fix. |
| CARD-0788 | Can run beside it: supplied `LandApproval.cs`, `AgentTaskLandService.cs`, `AgentTaskLandSourceResolver.cs`, `AgentTaskReplyService.cs`, `AgentTaskWorktreeBaseResolver.cs`, `AgentTaskDispatcher.cs`, `TaskCompletionProgressService.cs`, `GitSettings.cs` intersect none of this footprint. Shared build slots still apply. |
| CARD-0826, partial `d2128918` | Can run beside the supplied remaining footprint: HostCleanup, `dev-aspire.ps1`, `Program.cs`, `PhoneHomeCommandDispatcher.cs`, stage bundles and **`deploy-server2.ps1`**. This card deliberately does not edit deploy-server2, so no direct collision there. Defer if either footprint expands into `c590-remote.sh` or either build-slot script, which this card does edit. |
| CARD-0885 / CARD-0886 | No planned checkpoint-tool or driver edit here, so their named tooling paths are disjoint. Their changes can affect how receipts run: use the landed tool, do not repair it in this card. Recheck any expansion into the shared build-slot library/harness; exact overlap serializes. |
| CARD-0881 | Can run beside its orchestrator bundle changes; this card edits no bundle, settings endpoint or instruction index. No bundle growth or area-map change. |

## Verification design

### Current roster, source counts and OS meaning

Frozen baseline **16 C849 single-result methods** in `RemoteScriptContractTests`:

1. `C849_front_door_passes_every_full_case_name_to_the_invoker`
2. `C849_Cache_cases_use_only_the_validated_host_lane`
3. `C849_Cache_prepare_is_idempotent_and_preserves_payloads`
4. `C849_Cache_prepare_refuses_foreign_or_unsafe_roots`
5. `C849_Prune_refuses_stale_or_busy_authority`
6. `C849_Seed_publishes_complete_payloads_before_its_marker`
7. `C849_Seed_refuses_invalid_donors_and_partial_payloads`
8. `C849_Saved_donor_archive_is_checked_and_imported_without_a_container`
9. `C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters`
10. `C849_Saved_donor_rejects_declared_size_bomb_before_writing`
11. `C849_Retired_guard_refuses_docker_ps_errors`
12. `C849_Deploy_temp_names_saved_import_when_seed_is_missing`
13. `C849_Deploy_prepares_and_verifies_before_acceptance`
14. `C849_Prune_preview_is_read_only_and_bounded`
15. `C849_Prune_and_rollback_retain_roots_and_recovery`
16. `C849_Cache_receipts_exclude_credentials_and_payloads`

Linux and Windows-with-WSL each select/execute 16 before additions. Do not use 43 from the old parent plan as today's class count: it is 44 at this baseline, and only 16 are in this selection. WSL must have bash, jq and the PowerShell capability required by the landed CARD-0905 harness. Absence or a skipped shell test is pending Windows coverage, never a pass. Preserve the existing full-gate invocations inside the C849 busy-counter method; the new success branch does not erase those assertions.

`BuildSlotScriptTests` existing census is 7 `C589_*`, 1 `Wrapper_renews_a_renew_mode_grant_while_the_command_runs`, 5 `C800_*`, 4 `C845_*` = **17**. Linux executes all 17; Windows deliberately selects the **16** portable methods, excluding only `C800_WrapperInterruptKillsChildAndReleasesLease` (its source explicitly skips off Linux). Existing fallback, argv, .ps1 binding, renewal, release, failure and ASCII contracts must stay green. Do not count their internal PASS rows as TUnit executions.

The rolling census was independently summed from each group's loop bodies:

| Group | Child invocations | Assertions |
|---|---:|---:|
| T-1 | 1 | 8 |
| T-2 | 2 | 6 |
| T-3 | 1 | 3 |
| T-4 | 1 | 2 |
| T-5 | 1 | 2 |
| T-6 | 0 | 2 |
| T-7 | 3 | 9 |
| T-8 | 1 | 5 |
| T-9 | 2 | 8 |
| T-10 | 2 | 8 |
| T-11 | 2 | 6 |
| T-12 | 19 | 57 |
| T-13 | 2 | 6 |
| T-14 | 2 | 6 |
| T-15 | 3 | 9 |
| T-16 | 4 | 12 |
| T-17 | 2 | 5 |
| Total, current source | **48** | **154** |

Plan changes the existing T-17 saved case to model busy accepting main and adds **one** named assertion, `T-17 busy main admission and deployment untouched`, checking zero main POSTs and no deploy-parent. Expected final harness: **17 groups / 48 invocations / 155 assertions** on each OS; 0 TUnit executions. T-1/T-8/T-9 already check verify-before-clear; do not count duplicate assertions as new tests. No harness was executed in Plan.

### Proposed detecting roster and acceptance coverage

Each row below is one new `[Test]`, with labelled internal scenarios rather than parameter expansion. TestDesign may refine names/attributes only with an explicit recensus and matching checkpoint floors. Total additions: **8 remote + 8 slot = 16 TUnit results per OS**. All child-process tests use assembly-local `ParallelLimiter<ProcessSpawnLimit>`. No provider launch, live HTTP/Docker call or real server Program boot.

| ID / exact proposed method | Production behavior and required assertions |
|---|---|
| FS1 `C912_First_seed_accepts_busy_main_with_empty_unmounted_volumes` | Exercise actual Seed saved branch with main accepting, nonzero sessions/runnerSessions/queue, occupied healthy broker, and each absent/empty target arrangement. Assert main ID/mount/admission unchanged, imports precede strict smoke, helper removal/rechecks precede marker; `first-seed-main-unchanged`, `first-seed-ready`. |
| FS2 `C912_First_seed_refuses_unknown_mounted_or_populated_targets` | Actual preflight, real jq. Each of three names: running/stopped third party, main destination, bind alias, hidden content, foreign labels/options/driver, bad root owner/mode, failed/partial Docker census/inspect. Assert exact D-2 code and zero prepare/import/marker/admission writes (`preflight-no-write-<case>`). |
| FS3 `C912_First_seed_rechecks_each_copy_boundary` | Scripted barrier changes main ID/mount/image, attaches a container or populates a not-yet-imported target after staging/before import/after import/after smoke/before publication. Assert exact code, no subsequent target writes or marker (`recheck-refused-<boundary>`). Retain successful imported-content verification; never assert post-import empty. |
| FS4 `C912_First_seed_refuses_bad_temp_disk_and_broker` | Missing/null/type/retired/admission/counter variants, stopped temp project container, unknown source lookup, insufficient/unknown space, disabled/unreachable/malformed broker; exact D-2 refusal. Busy healthy broker proceeds to lease wait rather than main drain. |
| FS5 `C912_First_seed_failure_never_publishes_or_changes_admission` | Missing/mismatched uploaded tools and import/smoke/lease timeout/loss/release/cleanup/source-change faults; original tar hash stable unless deliberately changed, main unchanged, no accepted marker, own-helper cleanup or named refusal, partial volumes retained safely. Assert mapping of each strict diagnosis without generic success. |
| FX1 `C912_Fixture_uses_saved_archive_and_records_retired_temp` | Run real front-door/bridge argument construction and extracted F-5/F-9 logic with fake host I/O; valid synthetic archive, no donor. Assert importer upload, path transported verbatim, private namespace, real retired/null facts, strict smoke, F-5 populated/empty controls, existing 9/26/2 summary. |
| FX2 `C912_Fixture_saved_source_refuses_invalid_or_ambiguous_evidence` | Omitted saved source, invalid path, saved+donor, changed source hash, unsafe archive entries/duplicates/size, missing host/reference, unknown/nonretired temp, missing main/inventory and missing/mismatched upload failures. Preserve existing archive diagnoses; new changed-source code is `CacheSavedDonorChanged`. No production cache mutation or synthetic inventory. |
| RG1 `C912_First_seed_exception_never_reaches_full_gate_paths` | Invoke unchanged Reset/Prune and live-donor paths with busy, non-drained, accepting, unknown counters/processes/broker as applicable; assert the pre-existing exact refusal codes and zero destructive actions. Marker-present reuse must not import; damaged marker must not enter the exception. |
| LS1 `C912_StrictLeaseRequiresGrantedLease` | Unlimited/unleased/expired/malformed/PID-only/refused replies and NoSlot conflict never launch a command; valid renew grant does. Assert `strict-no-command-<case>`. |
| LS2 `C912_StrictLeaseWaitsForSharedSlot` | Scripted busy and memory-floor responses followed by grant, same endpoint/FIFO request; only one grant covers all smoke commands; `strict-start-after-grant`. No real busy broker. |
| LS3 `C912_StrictLeaseTimeoutRunsNothing` | Zero remaining acquisition budget with scripted busy response yields exit 4/timeout and zero command starts; unknown endpoint refuses without fallback. |
| LS4 `C912_StrictLeaseCoversWholeSmoke` | Recording child marks restore/build/run; matching grant precedes all and release follows all; receipt must be based on that actual lease. Preserve literal argv/CPU cap. |
| LS5 `C912_StrictLeaseLossStopsOwnedChild` | Child/renewer handshake followed by scripted loss or worker death; assert owned child terminated, no next build step/success, exact lease-lost reason. Unrelated child remains alive in fixture. |
| LS6 `C912_StrictLeaseReleasesOnAllExits` | Success, child exit failure and cancellation all attempt the matching release after owned-child exit; preserve child exit when release succeeds. |
| LS7 `C912_StrictLeaseRefusesFailedRelease` | Scripted DELETE failure produces release-failed reason and no success receipt even after apphost output; normal DELETE produces success. |
| LS8 `C912_StrictLeaseBoundsSmokeLifetime` | Controlled monotonic clock reaches the five-minute deadline while a barrier holds the owned driver; exact smoke-timeout and no continuing child/success. Test budget is an injected clock value, not a short real-time margin. |

The Bash tests execute actual functions and real `jq` predicates. Docker/status/slot/clock boundaries return deterministic scripted facts and record every mutating call. No filter-blind jq stub and no replacing the first-seed guard or lease decision with `return 0` in its detecting tests. Existing tests that stub a guard remain regression evidence for their own scope, not proof of the new predicate. For strict renewal/driver deadlines, extend the existing offline seam to expose controlled time and barrier signals; tests must finish by releasing barriers in `finally`. Harness timeout is diagnostic only.

### Positive controls

Each control has a named detecting test and a concrete production mutation. Run later in the Mutation stage with the single method filter `/*/*/<Class>*/<method>`; tests remain unchanged. Baseline green, mutated assertion red, restored/rebuilt green are mandatory. The script driver must run far enough to hit the named assertion; a syntax error, missing tool, harness timeout or zero-test result is not a detection. Alternative scenario variants in one row each get their own recorded assertion red.

| PC | Concrete mutation | Detecting test / expected assertion red |
|---|---|---|
| PC-1 | Route saved first Seed back through main drain/zero gate | FS1 / `first-seed-ready`. |
| PC-2 | Enumerate only main or only running containers; separately bypass empty-content rejection | FS2 / `preflight-no-write-stopped-third-party`, `preflight-no-write-content` and the associated exact refusal. |
| PC-3 | Treat Docker lookup error as absent | FS2 / `preflight-no-write-inspect-error`. |
| PC-4 | Remove one recheck at a time; separately ignore changed main identity | FS3 / `recheck-refused-<boundary>` for that exact injected event. |
| PC-5 | Admit nonretired/null-unknown temp; separately bypass disk and broker validity checks | FS4 / exact `refusal-<case>` assertion. |
| PC-6 | Publish the accepted marker before helper cleanup/strict receipt | FS5 / `failure-no-marker-<case>`. |
| PC-7 | Return unlimited/unleased as granted in strict mode; separately admit PID-only/expired grant | LS1 / `strict-no-command-<case>`. |
| PC-8 | Launch the child on busy/timeout or remove the shared endpoint override | LS2 / `strict-start-after-grant`; LS3 / `timeout-command-count-zero`; FS1 / `smoke-shared-broker-endpoint`. |
| PC-9 | Release the lease between restore and build | LS4 / `whole-smoke-lease-order`. |
| PC-10 | Ignore renewal loss or disable bounded-driver termination | LS5 / `lost-no-next-step`; LS8 / `deadline-child-stopped`. |
| PC-11 | Omit finally-release; separately ignore failed DELETE | LS6 / `release-after-exit`; LS7 / `release-failed-no-success`. |
| PC-12 | Omit Fixture savedDonor transport/importer upload, bind stale checkout scripts, or replace absent-temp inventory with fake live zero | FX1 / `fixture-saved-source-transport`, `fixture-importer-upload`, `fixture-reviewed-tools-bound`, `fixture-retired-null-preserved`; FS5/FX2 also assert `tools-invalid-no-command` for a missing/mismatched upload. |
| PC-13 | Bypass archive validation or source-hash recheck | FX2 / `fixture-refused-unsafe-entry`, `fixture-refused-source-changed`; FS5 / `seed-source-changed-no-marker`. |
| PC-14 | Apply exception to Reset/Prune or bypass donor zero check | RG1 / `full-gate-refused-<operation>`; existing C849 prune/donor exact diagnoses remain. |
| PC-15 | Remove `Assert-ZeroCounters` in redeploy-old or its drain ownership check in a scratch mutation | `scripts/test-deploy-server2.ps1` T-12/T-13/T-14/T-16 exact verdict/no-destructive-host assertions. This is a mutation-only edit to the read-only production wrapper. |
| PC-16 | Copy a synthetic raw status/environment payload into a new receipt | `C849_Cache_receipts_exclude_credentials_and_payloads` extended toxic sentinel assertion. |

### Test-first order, known hazards and Windows pending

Land CARD-0905 before S1 Code. Recount its actual C849 methods and helper semantics and update this plan if the counts change; never simply raise Min to a guessed total. Commit/push S1 before the declared red runs of CP-1/CP-2/CP-3; the new predicate/strict/saved-Fixture assertions should be red against baseline, while untouched full-gate and rolling rows stay green. Implement S2 then S3, commit/push each; S4's clean SHA closes the final Linux rows, followed by Windows rows at the same source SHA. No repeated intermediate full runs are required. Report each justified rerun, its reason and counts.

Known-flake list, supplied/historical rather than remeasured here: CARD-0791/0794 provider readiness/snapshot/broken pipe; CARD-0818 `CheckpointExecutorLogTests.concurrent_callbacks_append_each_line_once_without_overlap`; CARD-0820 temp contention/cleanup and `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget`; CARD-0828 checkpoint ownership/executor races; CARD-0848 `DetachedLauncherTests.executor_survives_its_starter`; CARD-0879 AlwaysOn DetectTimeout/PaneClosed; CARD-0889 Codex submit's loaded two-second budget; CARD-0890 seven Linux `C448_V15_RealWorkerDeathRecoversDurableBoundaries` module-initializer failures; CARD-0900 `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit`; CARD-0742 `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision`; CARD-0757 `ScaledTimeProviderTests.Speed_10`; CARD-0751 `HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines`. All are outside this narrow selection. None excuses a selected failure, new skip, fail-open or privacy exposure. CARD-0905's historical WSL failures remain a prerequisite issue, not five acceptable skips.

Windows rows pending: **CP-4 remote 24, CP-5 slot 24, CP-6 rolling 17/48/155**. Prerequisites are the landed CARD-0905 fix, Windows/WSL tool availability and the same final clean Code SHA. Linux cannot certify these rows. Plan has executed none. Host Fixture and rollout are independently pending operational acceptance; the Windows front door drives Linux host behavior, not native Windows cache volumes.

### Checkpoints

Closed ordinary repo verification list, distinct from **CARD-0849 operational CP-3/4/5** named in the rollout section. One isolated build and one exact filter per TUnit row; rolling rows are the schema's non-TUnit exception (Build/Min `n/a`). Table `\|` is Markdown escaping; the checkpoint importer removes it. When invoking a quoted filter directly, use plain `|`, each operand parenthesized; class names retain trailing `*`. TUnit uses `dotnet run --project`, never `dotnet test` or discovery counts. No full Unit/namespace/provider suite is included.

Final expected TUnit executions: Linux **24 + 25 = 49**, Windows **24 + 24 = 48**, total **97 OS executions**. These are proposed source-derived targets, not measured passes. Windows selects every portable existing slot method plus the eight new C912 methods and excludes the single Linux SIGINT method explicitly. Compare executed names as well as Min. Each OS also runs one standalone 17/48/155 rolling harness.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S4 | `tests/Antiphon.Tests -> bin-c912-linux-remote/` | linux-cache-contracts | `/*/*/RemoteScriptContractTests*/(C849_*)\|(C912_*)` | FS1-FS5, FX1-FX2, RG1; existing cache gates/privacy | Linux 24 executed = 16 existing + 8 new; 0 failed/skipped; Windows 0 selected | 24 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c912-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S4 | `tests/Antiphon.Tests -> bin-c912-linux-slot/` | linux-strict-slot | `/*/*/BuildSlotScriptTests*/*` | LS1-LS8; existing wrapper behavior | Linux 25 executed = 17 existing + 8 new; 0 failed/skipped; Windows 0 selected | 25 | 9 | true | `C804_ORPHAN_SWEEP_ROOT=c912-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S4 | n/a | linux-rolling-gates | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | RG1; T-1..T-17 | Linux 17 groups, 48 child invocations, 155 assertions, 0 failures; 0 TUnit; Windows 0 selected | n/a | 6 | true | n/a |
| CP-4 | S1-S4 | `tests/Antiphon.Tests -> bin-c912-windows-remote/` | windows-cache-contracts | `/*/*/RemoteScriptContractTests*/(C849_*)\|(C912_*)` | FS1-FS5, FX1-FX2, RG1; WSL/front-door parity | Windows 24 executed; 0 failed/skipped; Linux 0 selected | 24 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c912-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1-S4 | `tests/Antiphon.Tests -> bin-c912-windows-slot/` | windows-strict-slot | `/*/*/BuildSlotScriptTests*/(C589_*)\|(Wrapper_renews_*)\|(C800_WrapperPassesWildcardArgvLiterally*)\|(C800_WrapperStartsUnitFilterWithinDeadline*)\|(C800_WrapperForwardsScriptTokens*)\|(C800_WrapperLaunchesNativeExecutableLiterally*)\|(C845_*)\|(C912_*)` | LS1-LS8; portable wrapper regression | Windows 24 executed = 16 existing + 8 new; 0 failed/skipped; Linux 0 selected | 24 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c912-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1-S4 | n/a | windows-rolling-gates | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | RG1; T-1..T-17 Windows | Windows 17 groups, 48 child invocations, 155 assertions, 0 failures; 0 TUnit; Linux 0 selected | n/a | 6 | true | n/a |

## Execution, live-run order and rollback

Code runs the checkpoint tool once per committed slice group and waits until exit is not 75. Explicitly declared S1 red rounds use the same Linux row filters/build outputs and a separate run receipt, before the table's final S1-S4 closure. Never start a second run while the first is active. Use bounded foreground waits/progress updates. Example final commands (PowerShell; full SHA resolved from the clean committed checkout):

```powershell
$plan = 'docs/superpowers/plans/2026-10-02-card-0849-first-seed-exception-plan.md'
$source = (git rev-parse HEAD).Trim()
# Declared launcher bootstrap only when no matching committed tool output exists:
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c912-checkpoint-launcher -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c912-tool/ -nodeReuse:false
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c912-tool/ -- run --plan $plan --rows CP-1,CP-2,CP-3 --expected-source-sha $source
# If exit 75, reuse the emitted run id; repeat wait, never restart the run.
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c912-tool/ -- wait <run-id> --max-wait 50s
# Separately commissioned Windows checkout at the same SHA:
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c912-tool/ -- run --plan $plan --rows CP-4,CP-5,CP-6 --expected-source-sha $source
```

The checkpoint executor self-leases its builds/test drivers. If launcher compilation is needed, explicitly declare that one bootstrap build and acquire a slot with `scripts/build-slot.ps1`; use the resulting launcher with `--no-build` for run/wait so implicit launcher rebuilds are not hidden unleased builds. Do not hold an outer slot while waiting for checkpoint rows to acquire their own. Standalone rolling execution, if explicitly commissioned outside the tool, is `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c912-rolling -- pwsh -NoProfile -File scripts/test-deploy-server2.ps1`. Slot timeout exit 4 is a reported refusal, never bypassed.

Preserve each emitted CHECKPOINT line with source/dirty/buildSource/slot fields, OS, exact executed/passed/failed/skipped counts, TRX/report path and reruns. All final certificates must be clean at the stated SHA. Unlisted builds/tests need a stated reason. No tests/builds are required to publish this Markdown Plan; static validation is reported separately.

### Operator sequence after implementation, Review and publication

Run only from the trusted desktop lane and checkout whose HEAD equals the full reviewed/landed `C849_DEPLOY_SHA`. Use the scripts' approved token handling; never print credentials. Refresh main/temp status, exact mounts/all-container census, archive identity, broker configuration/endpoint, available slots/memory and stage/Docker disk headroom before use. This Plan does not authorize a live operation. The active Code task must not drain/recreate its own host.

| Order / action | Required observed output/evidence | Rollback or refusal response |
|---|---|---|
| 1. CARD-0849 **CP-3 Fixture**: `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -SavedDonor /home/mc/runner-cache-donor/temp-runner-cache.tar` | Exit 0; `C849_FIXTURE groups=9 controls=26 expectedRed=26 inventories=2 failures=0 productionMutations=0`; F-1..F-9 and 26 actual expected-red receipts, immutable image/source, saved hash, real absent/retired temp inventory, strict F-5 grant/release. Main remains accepting. | Remove only exact recorded private fixture resources through existing cleanup. No production drain/cache repair. Full broker or inadequate headroom defers; do not stop work to manufacture capacity. |
| 2. **First Seed with work in flight**: same front door `-Case Seed -SavedDonor /home/mc/runner-cache-donor/temp-runner-cache.tar` | Exit 0; `C849_SEED donor=saved ready=true smoke=passed recovery=retained`, accepted marker and verified host/reference hashes. Add a bounded `first-seed-isolation.txt` receipt with `mode=first-saved`, unchanged main ID/mount hash, before/after admission/counters, completed recheck phases, `slot=granted` and matching release. Main starts/continues work during the operation; capture nonzero work evidence without a synthetic provider launch. | Preflight refusal leaves target data untouched and main unchanged. After-write failure: no marker; preserve tar, report partial unaccepted contents and exact diagnosis, keep main working. Full Reset may be needed later under unchanged gates; never weaken them to meet this rollout. |
| 3. **deploy-temp only**: `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $env:C849_DEPLOY_SHA -Phase deploy-temp -SavedDonor /home/mc/runner-cache-donor/temp-runner-cache.tar` | Exit 0 and printed rolling evidence directory; Seed reuses accepted marker, temp exact SHA/dispatch eligibility, three shared mounts plus private work/state/tmp/dind, `/tmp` 1777, uid-1654 net9 smoke, nested daemon name equals temp hostname, no host socket, verify before temp drain/clear. Main ID/admission unchanged. | Failed verification leaves temp held. Main continues accepting. Do not call drain-old. Inspect/clean only exact failed temp resources under existing lifecycle rules; retain external caches/tar/recovery. Clear a temp drain only when its verification is satisfied. |
| 4. **Verify temp and canary Plan** | Fresh status `buildVersion=<reviewed SHA>`, available/dispatchEligible/accepting true and draining false; retained mount/smoke receipts. Caller commissions one allowed-kind Plan task explicitly on `server2-temp` in its own worktree after reading current caps; require transcript-confirmed prompt delivery, useful complete Plan report, normal settlement and correct runner binding. No provider launch occurs in this Plan. | Failed canary means temp is not verified. Keep main accepting, hold temp's new admission as needed through its existing drain; let any work finish naturally. No kill, no main drain, no automatic reroute. A health check or registration alone is insufficient. |
| 5. **drain-old**: deploy wrapper `-Rolling -Sha $env:C849_DEPLOY_SHA -Phase drain-old` | Temp already verified/canary complete. Main drain has redirectTo=server2-temp, retireWhenIdle=false, accepting false; sessions/runnerSessions/queuedTasks reach explicit non-null zero naturally. Exit 0 only after zero. `OldRunnerDrainConflict` remains a refusal. | Undo this drain with `pwsh -NoProfile -File scripts/runner-drain.ps1 clear -RunnerId server2 -Reason 'CARD-0912 rollout deferred'` (POST `/api/session-runners/server2/drain/clear`). Verify main accepting again. No session kill. A wait timeout does not undo the durable drain automatically. |
| 6. **redeploy-old**: deploy wrapper `-Rolling -Sha $env:C849_DEPLOY_SHA -Phase redeploy-old` | Fresh zero counters and exact drain ownership before deploy-parent. New main exact SHA, caches/private mounts and smoke verified before clear; main accepting afterward. Missing/null/busy/conflict refuses as today. | Keep main held if replacement/verification fails; verified temp continues. Restore the retained prior image only through the normal drain/zero/mount/smoke procedure. Never clear a failed main merely to regain admission. Tar/recovery/marker remain. |
| 7. Existing CARD-0849 CP-4 Both, then drain-temp/retire-temp and CP-5 Retired | `C849_BOTH runners=2 smokes=2 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0`; after normal retirement `C849_RETIRED externalVolumes=3 tempPrivateVolumes=0 mainTmpRetained=true smokes=1 rollback=retained failures=0`. | Existing retirement/rollback gates remain. Keep the donor tar until import and rollout/rollback retention are verified; do not delete it as Seed/Fixture cleanup. External volumes survive temp retirement. |

Reset is explicitly **not** rollback for an accepted Seed: it refuses `CacheSeedAlreadyReady`. For an interrupted unmarked import, later authorized `-Case Reset` still requires both runners drained/zero, every consumer detached including stopped containers, and idle broker. Under the current no-drain-before-temp rule such recovery can remain deferred; that is safer than adding another exception. Code rollback is a new forward commit, with normal reviewed activation; no forced branch history rewrite or automatic cache deletion.

### Acceptance mapped to CARD-0912

| Card requirement | Repo proof | Live proof / outstanding boundary |
|---|---|---|
| First Seed while server2 accepts work; named refusals on mounts/content | FS1-FS5; 16 existing C849 cases retained; strict LS cases | Step 2 isolation/lease/marker receipts with main work in flight. Unknown proof refuses. |
| CP-3 saved archive with no temp donor | FX1/FX2; front-door/bridge contract; unchanged 9/26 roster | Step 1 actual private Fixture run and retired-absent inventory. Offline success alone is insufficient. |
| Every refusal code covered and full gates unchanged | D-2 code mapping, RG1, C849 prune/donor/reset cases, rolling T-1..T-17, positive controls | Existing Reset/Prune/live-donor/redeploy gates remain; no production negative destructive trial needed. |
| Operator order and docs | S4, rolling trace assertion, explicit phase commands above | Temp canary completion gates first main drain; retained archive and correct rollback at every boundary. |

### Cost and handoff

Estimates only: final ordinary repo rows total **53 minutes** (Linux 25, Windows 28), including four isolated builds. Declared Linux S1 red round adds up to 25 minutes. Code authoring estimate 3-5 hours, plus slot/Windows scheduling waits; split committed slices as above. Positive-control variants and live Fixture/rollout are separate commissioned work, not hidden in these counts. No routine repeated green battery or synthetic host load is required.

TestDesign must freeze labelled refusal scenarios, offline clock/renew barriers and the actual post-CARD-0905 census without expanding the production footprint casually. The plan makes no claim of current host readiness or executed green/Windows/Mutation evidence. There is no outstanding operator design decision: implementation and independent verification precede the explicitly ordered live rollout.

Plan validation: static source/filter census confirmed 44 remote methods, 16 selected C849 methods and 17 existing slot methods. The six-row manifest has eleven correctly ordered columns, four distinct isolated TUnit outputs, trailing class wildcards and escaped method unions. Applying those filters to the current plus explicitly proposed roster gives Linux 49 / Windows 48 TUnit results, with 16 unique proposed additions and 53 estimated final-row minutes. The rolling loop census is 17/48/154 before the one proposed new assertion. `git diff --check` passed. These are static checks, not a checkpoint-tool execution or runtime evidence; Linux/Windows builds and tests remain zero in Plan.
