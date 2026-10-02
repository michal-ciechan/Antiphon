# CARD-0912: volumes-only cold first Seed

Date: 2026-10-02. Stage: TestDesign complete; next Code after CARD-0905 lands. Branch `feat/card-task-285cc907`, StartRef `46d7852c80237387413ae2e449efa6f31ccbe622`. TestDesign changes only this Markdown file. Builds/tests executed: **Linux 0, Windows 0**. No Docker, SSH, provider, host mutation, Seed, drain or deployment occurred.

## Operator refinements and supersession

Both refinements arrived. **Direction 2**, `task-285cc907-refinement-20261002005528.md`, controls: cache contents are best effort; cold first Seed creates empty, writable volumes and a distinct cold marker. It needs **no package payload, online restore, offline proof, apphost pack, smoke build or build-slot lease**. CARD-0913 is not on this card's critical path.

This revision supersedes the original plan's Outcome and scope; D-1/D-2 saved-source first-seed/import/publication design; D-3 strict-smoke lease/upload design; D-4 saved Fixture; implementation slices; Verification design/roster/positive controls/checkpoints; live-run order; acceptance and cost/handoff. It also supersedes refinement 1's online restore, 9.0.20 dependency, integrity/smoke lease, full-marker/recovery requirement and related tests. Retained obligations are fresh first-seed isolation, unchanged full gates elsewhere, privacy, CARD-0905 collision ordering and a completed temp canary before main drain.

`card.ps1 get CARD-0912 -Board Antiphon` confirmed the original title **CARD-0849 rollout: first-Seed exception without draining server2 + CP-3 Fixture from the saved archive** and scope. The newer operator direction overrides the older title/brief. Existing saved-donor Seed remains unchanged with its full gates; saved-archive Fixture and a saved-donor no-drain exception are dropped from this card. Any later expansion needs a separately commissioned follow-up. Do not read, import, modify or delete the saved tar in this rollout.

## Evidence and implementation design

Read the full original plan/investigation, `docs/project-context.md`, `docs/orchestration-loop.md`, `docs/testing-and-build.md`, `docs/ops-http.md`, `docs/docker-stack.md` and affected scripts/tests. Source facts at StartRef: `c849_seed` prepares volumes before source selection; saved Seed invokes `c849_prune_idle` three times; live-donor Seed has its own donor drain/zero/process/stop/reconnect gates; the rolling wrapper already supports explicit phases. Preserve the actual legacy contract, including the distinction that live-donor Seed does not have a pre-existing main-zero gate.

The old `c849_require_ready` at `scripts/c590-remote.sh:1761-1777` requires recovery directories and a hash of the 9.0.20 package apphost. `c849_seed` writes that marker; require_ready validates it. `case_verify_runner_caches` and retired verification call the offline apphost smoke, and the front door Both/Retired parsers require smoke/hash receipts. **All those cold-mode consumers must change together** or deploy-temp will still fail on an intentionally empty cache. The SDK/apphost investigation is unnecessary for the volumes-only path. Existing full-marker smoke remains unchanged for legacy modes, not a cold-mode gate.

### D-1: explicit cold creation under the existing lock

Add `-Cold` to `verify-card0849-caches.ps1`, valid only with `-Case Seed`, mutually exclusive with SavedDonor. Transport a strictly boolean `coldSeed` through `c590-real.ps1` to a validated host flag. Missing/false means existing behavior. Invalid types/cases or conflicting source selection refuse before any host operation. Never infer creation mode from missing donor, failed lookup or damaged marker. The production deploy wrapper stays read-only: operator explicitly cold-seeds first, then the wrapper's ordinary Seed call can reuse the accepted cold marker without importing.

Before creation/probe, acquire the existing maintenance lock, resolve source and marker state, and prove all of:

- First creation has no marker path, including empty/damaged/symlink; no donor or other temp-project container, including stopped containers. A valid cold marker rerun uses marker validation instead of first-creation emptiness checks.
- Temp status is HTTP 200, retiredAt nonnull, available/dispatchEligible/acceptingNewWork false, draining/retireWhenIdle true, redirectTo=server2; integer sessions/queue zero; runnerSessions explicitly null or integer zero. Missing fields are unknown, not null.
- Exactly one running main container with expected Compose project/service labels. Capture full ID, image and filtered mount identity type/name/source/destination/RW. Main status is readable and internally valid with integer nonnegative counters; main may be busy, accepting and undrained. Its ordinary app-uid builds may continue.
- Each fixed volume is absent or correctly labelled local/options-empty, with a nonsymlink directory root owned 1654:1654 mode 0700, empty including hidden entries. Prove absence with a successful complete name census and inspect facts; inspect failure alone is not absence. No preflight chown/chmod/write probe on existing roots.
- Complete `docker ps -aq` and inspect show no container mounts a target: main, another runner, stopped/unrelated container, bind equal/below/ancestor of canonical mountpoint, or broad Docker-root bind. Main also cannot expose a cache destination through another mount. A helper-looking name grants no exception. Unknown/incomplete census refuses.

The three fixed names are `antiphon-runner-cache-nuget-packages`, `antiphon-runner-cache-nuget-scratch`, `antiphon-runner-cache-npm-content`; labels stay `io.antiphon.owner=server2-runner`, `io.antiphon.cache-schema=1`, and the corresponding cache role. Only absent volumes are initialized, as 1654:1654/0700. Existing correctly owned empty volumes stay intact. Recheck main identity/status, temp predicate, roots and all-container isolation **before and after initialization and each probe, and before marker publication**. End with all three empty and no helper. No copy/import/package step exists.

The maintenance lock serializes the supported lane, not arbitrary root Docker clients. Repeated checks detect observed races; do not claim atomic exclusion of out-of-band attaches between checks. Other host deployment lanes stay quiescent during this short operation, while ordinary main work continues.

### D-2: cheap writability probe, no build lease

For each target use an existing inspected helper image and a short host-Docker `/bin/sh` helper, `--network none`, `--user 1654:1654`, `volume-nocopy`, with no Docker socket or runner private mounts. Create a uniquely run-scoped canary, rename it, then delete it; verify empty afterward and confirm helper removal. Bound each probe to **10 seconds** and stop/remove only that owned helper on expiry. Any failure or residual canary refuses. Never broaden deletion to cache contents or another run's canary.

**No build-slot lease is needed:** the container runs filesystem syscalls only, not dotnet/npm or any build/test driver. Container existence alone is not a build driver. It needs no broker query, strict wrapper, new upload pair, network or SDK. Do not build/pull an image as an implicit probe dependency; missing inspected image refuses `CacheHelperImageMissing`. Code's repo checkpoint builds/tests still take normal host slots. This card does not edit either build-slot script, its shims or its tests.

### D-3: distinct cold marker and exact validator contracts

Write atomically after fresh successful proof/probes a confined nonsymlink marker with exactly one of each required field: `schema=2`, `kind=cold`, `cold=true`, `source-sha=<reviewed full SHA>`, `image=<inspected sha256 identity>`, `packages=antiphon-runner-cache-nuget-packages`, `scratch=antiphon-runner-cache-nuget-scratch`, `npm=antiphon-runner-cache-npm-content`. **No apphost/payload/reference hash and no recovery path.** Receipt `ready=true kind=cold writable=3` records mode honestly; never output smoke=passed for a cold marker.

Change `c849_require_ready` to take a narrow explicit context argument, default **full-required**, with **allow-cold** only at admitted deployment/verification/reuse callers. Reject missing/symlink/empty/damaged/duplicate/unknown/mixed-kind cold markers as `CacheSeedMarkerInvalid` (missing marker remains `CacheSeedRequired`). Recognize old unversioned full markers via the existing recovery+payload validation unchanged; do not interpret missing full fields as cold. A well-formed cold marker in full-required context refuses **`CacheFullSeedRequired`** before payload operations. Cold allow-cold validates exact schema/kind/fields/names and live volume labels/root ownership; it requires neither package content nor a recovery directory. Cache contents may be populated later, so **do not require emptiness or zero consumers on ordinary ready-marker reuse**.

Admitted allow-cold call sites: deployment (`case_deploy_temp_runner`, `case_deploy_parent`), cache verification (`case_verify_runner_caches`, `case_verify_runner_caches_retired`), normal temp retirement (`case_retire_temp_runner`), and the accepted-marker **reuse-only** branch of Seed when no SavedDonor source was explicitly requested. Merely having a temp container on a deployment rerun does not turn marker reuse into donor copying. Explicit saved/live donor population, Prune and Reset remain full-required; cold marker presentation there yields CacheFullSeedRequired without destructive effects. Reset with a legacy full accepted marker still returns CacheSeedAlreadyReady. Absent-marker Reset retains its old full maintenance gates. No cold marker can waive a drain/zero/ownership gate.

Cold verification checks current source SHA/runner status, the three shared mounts, private work/state/tmp/dind mounts, `/tmp` 1777 and cheap uid-1654 writability. Verification probes touch only their own canary and preserve all other entries: after accepted cold creation, normal consumers and populated caches are expected, so creation-only emptiness/no-consumer predicates do not run on verification. It does **not** call c849_smoke, require_ready full-only, package hash, recovery or online/offline restore. Adapt the front-door Seed/Both/Retired receipt parsing by explicit marker kind: cold receipts report `kind=cold`, writable-volume counts and mount/status evidence; full receipts retain the existing smoke/hash/recovery contract. Both must reject mixed marker-kind receipts. Retired still verifies removal of temp private volumes and retains existing rollback-image identity checks; image identity is present in the cold marker. Keep verify-before-clear and all other rolling gates unchanged.

### D-4: CP-3 replacement, cache semantics and recovery

No saved-archive Fixture or operational donor-dependent CP-3 is required for this volumes-only cold rollout. Replace it with deterministic offline cold-path shell/Docker-shim tests below and live volume/probe verification, followed by temp verification and a completed canary Plan. Do not modify or claim success for the old 9-group/26-control Fixture; any broader parent-card Fixture obligation remains distinct. No source-package/hash/supply-chain claim is needed: ordinary builds use their configured network restore and verification policy, filling shared caches opportunistically. Missing/empty package content costs time; **unwritable/wrongly mounted cache volumes remain a hard failure**.

Preflight refusal changes no target content or main admission. After initialization/probe failure, owned newly created empty volumes may remain unaccepted; clean only an exact owned canary/helper, retain diagnostics and refuse marker publication. On an observed consumer or uncertain cleanup, do not clear contents or remove volumes. No automatic Reset/drain. A valid cold marker stays valid as builds populate caches. Explicit maintenance needing a full marker refuses and must be separately planned; never synthesize apphost/recovery facts from cold.

## Exact footprint, slices and collision order

Code's closed **seven-file** footprint: `scripts/c590-remote.sh`, `scripts/c590-real.ps1`, `scripts/verify-card0849-caches.ps1`, `scripts/test-deploy-server2.ps1`, `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`, `docs/docker-stack.md`, and this plan. The bridge is needed for Cold transport; deploy tests are required, production `deploy-server2.ps1` is read-only. No importer/build-slot/SDK/Dockerfile/Compose/broker changes. New helper fakes stay in the existing test file; no new test file/project change.

| Slice | Files | Red-first work |
|---|---|---|
| S1 | RemoteScriptContractTests and test-deploy-server2 | Add CV2, CV3, CV4, CV5, CV6, CV1, CV7 (exact names below); extend existing front-door/privacy assertions and T-17. Commit/push tests, run CP-1/CP-2 declared red/regression round. |
| S2 | c590-remote, c590-real, verify-card0849-caches | Cold transport, predicate/probes, distinct marker/validator contexts, cold verification receipts. Keep legacy paths/gates. Commit/push. |
| S3 | docs/docker-stack and this plan | Document volumes-only semantics, cold/full refusals, explicit phases and optional snapshot. Commit/push; final Linux CP-3/CP-4, separate Windows CP-5/CP-6 at same SHA. |

**Serialize Code after CARD-0905 lands**: same RemoteScriptContractTests AsyncLocal/skip helpers. Caller commissions Code on a base containing that land and this plan; Code recounts its actual source before editing. No fetch/rebase/reset/amend in this task branch. CARD-0788 land-path files LandApproval.cs, AgentTaskLandService.cs, AgentTaskLandSourceResolver.cs, AgentTaskReplyService.cs, AgentTaskWorktreeBaseResolver.cs, AgentTaskDispatcher.cs, TaskCompletionProgressService.cs, GitSettings.cs and land tests have **no overlap**. Supplied CARD-0826 production deploy wrapper footprint is also disjoint. Any actual footprint expansion into shared files defers. This delegate spawned no agents.

## Verification design

### Recount and OS roster

Actual static recount at StartRef: RemoteScriptContractTests **44** `[Test]` methods, **16 C849**, zero argument expansion. Supplied CARD-0905 branch `feat/card-task-322eb329` at `35cfa4619e90bfb96092d68173c0f24722e99014`, independently fetched into bare `/tmp/c912-testdesign-FyLW0h/base.git` and confirmed by ls-remote, has **46**, still **16 C849**. It adds C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block and C905_Linux_shell_finds_pwsh_when_installed. This is review-source evidence, not a claim it landed. Code must recount the landed form; never fetch into a task worktree.

Code source recount at assigned start ref `8d12a61ae7e7ab250327e99ef6e28f2b37fee2b4`: **46** `[Test]` methods, **16 C849**, **2 C905**, zero argument expansion. The supplied CARD-0905 roster is landed. After the original seven C912 additions and four follow-up methods the class is now **57** methods and the selected filters are **27 Linux / 21 Windows**; see the measured checkpoint evidence below. The rolling harness still has 17 groups and 48 child invocations; its assertion count changes from 154 to 155 with the T-17 busy-main assertion.

### Code requirement trace at the Linux slice

The labels below are executable assertions in the seven C912 methods unless a
legacy C849 method or rolling T group is named. `No` is a qualification gap,
not a passing result; the frozen design remains required. This amendment
records why Code did not add another test method or broaden the checkpoint
selection under the seven-method and 120-minute limits.

| Plan obligation | Enforced | Test and assertion label / remaining gap |
|---|---|---|
| Cold is explicit, boolean and Seed-only; no saved-source conflict | Yes | CV5 `cold-transport`, `cold-bridge-boolean-only`, `refusal-CacheDonorSourceConflict` |
| All eight absent/empty combinations keep busy main accepting; fixed empty writable roots, no package/build/network step | Yes | CV1 `cold-ready mask=N`, `only-absent-created mask=N`, `main-unchanged mask=N`, `no-package-network-or-slot-call mask=N` |
| Unmarked content, foreign owner/mode, stopped/other/main mounts and unknown census/inspect/temp status refuse before target write | Yes for listed variants | CV2 `preflight-no-write-*`, `refusal-CacheFirstSeedVolumeInUse`, `refusal-CacheUnmarkedContent-hidden`; every target and every malformed field permutation remain unproven (No) because the fake matrix covers representative targets and types only. |
| Main identity/mount, status, roots and attachments are rechecked before/after creation, probes and publication | Yes for phase calls; No for all race variants | CV3 `phase-proof-recorded-P0..P6`, `recheck-refused-P1`, `refusal-CacheFirstSeedMainMountChanged-id`. Image/mount/content/attachment changes at every boundary need further fault injection. |
| uid-1654 canary creates/renames/deletes, bounded ten-second helper, exact cleanup and no lease | Yes for success and named failure branches; No for real deadline scheduling | CV4 `probe-writable`, `probe-uid-1654`, `probe-owned-helper-stopped`, `probe-refused-*`. The timeout fake returns 124 synchronously; it does not measure wall-clock ten seconds. |
| Cold marker has exact fields; malformed/duplicate/mixed/symlink marker refuses; full-required rejects cold; populated reuse skips payload/recovery | Yes for listed variants; No for all malformed field permutations | CV5 `cold-marker-valid`, `malformed-marker-refused`, `full-context-refused`, `marker-reuse-no-probe-or-restore`; existing C849 full marker tests guard legacy payload/recovery. |
| Deployment, verification, retirement admit cold and keep mounts, status, rollback image, writability; full still smokes; mixed receipts refuse | Yes for caller routing and receipt parser; No for every host mount/status fault | CV6 `cold-verify-empty-cache`, `cold-no-smoke`, `full-smoke-retained`, `mixed-kind-refused`; existing C849 `C849_Deploy_prepares_and_verifies_before_acceptance` covers full host mount gate. |
| Reset, Prune, saved/live donor and redeploy-old retain full drain/zero/process/broker gates | Yes for source gate presence and existing full cases; No for each cold marker presented to each operation | CV7 `full-gate-refused-*`, C849 `C849_Prune_refuses_stale_or_busy_authority`, `C849_Seed_refuses_invalid_donors_and_partial_payloads`, rolling T-12/T-16/T-17. |
| Cold receipts exclude secret/status payload; temp canary precedes main drain | Yes for receipt and rolling ordering; live canary pending | CV6 `cold-toxic-sentinel-absent`, C849 `C849_Cache_receipts_exclude_credentials_and_payloads`, rolling T-1/T-17. The live Plan canary is an operator step after publication. |

StartRef whole-class Linux expectation 44; after CARD-0905 Linux 46, supplied desktop Windows 41 executed+5 capability skips. The five needing pwsh **inside WSL** are C849_Cache_cases_use_only_the_validated_host_lane, C849_Seed_publishes_complete_payloads_before_its_marker, C849_Saved_donor_archive_is_checked_and_imported_without_a_container, C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters, C849_Saved_donor_rejects_declared_size_bomb_before_writing. Preserve its guard. CP-3 Linux qualifies these; CP-5 Windows excludes them, never counts them passed. C905 methods remain outside this narrow selection, and its Windows-returning positive probe is not Linux evidence.

Seven original new remote methods, no parameter expansion; after four follow-up methods, whole class **57=46+7+4**; narrow Linux **27=18+9**, desktop Windows **21=12+9**. All new tests use real bash/jq with Docker/network/process fakes and native Windows pwsh for front-door tests; **none requires real pwsh inside WSL**. No new OS-specific test. The five inherited skips are capability exclusions, not Windows behaviors. Missing WSL/bash/jq blocks a selected Windows row. No BuildSlotScriptTests selection or change remains; its 17-method census is irrelevant to this refined scope.

Existing selected C849 methods: C849_front_door_passes_every_full_case_name_to_the_invoker; C849_Cache_cases_use_only_the_validated_host_lane; C849_Cache_prepare_is_idempotent_and_preserves_payloads; C849_Cache_prepare_refuses_foreign_or_unsafe_roots; C849_Prune_refuses_stale_or_busy_authority; C849_Seed_publishes_complete_payloads_before_its_marker; C849_Seed_refuses_invalid_donors_and_partial_payloads; C849_Saved_donor_archive_is_checked_and_imported_without_a_container; C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters; C849_Saved_donor_rejects_declared_size_bomb_before_writing; C849_Retired_guard_refuses_docker_ps_errors; C849_Deploy_temp_names_saved_import_when_seed_is_missing; C849_Deploy_prepares_and_verifies_before_acceptance; C849_Prune_preview_is_read_only_and_bounded; C849_Prune_and_rollback_retain_roots_and_recovery; C849_Cache_receipts_exclude_credentials_and_payloads.

Independently recounted rolling T-1..T-17 child counts **1,2,1,1,1,0,3,1,2,2,2,19,2,2,3,4,2** = **48**; assertions **8,6,3,2,2,2,9,5,8,8,6,57,6,6,9,12,5** = **154**. Preserve saved T-17 and change its existing retired-no-source marker-reuse invocation to busy accepting main through `faultRunner=server2; faultField=sessions; faultKind=value; faultValue=1; oldDraining=false`. Add one assertion `T-17 cold marker reuse leaves busy main untouched` (no main POST/deploy-parent). Final **17 groups /48 invocations /155 assertions**, zero TUnit. CV5 proves the marker is cold; the rolling fake alone cannot.

### Frozen methods

Each row is exactly one new `[Test]`, labelled internal scenarios rather than parameter expansion. Child-spawning tests carry assembly-local ParallelLimiter<ProcessSpawnLimit>. No real Docker, SSH, dotnet restore/build, provider, host HTTP or live volume. Execute actual extracted shell/jq/marker policies against controlled facts and tiny real directories.

| ID / exact frozen method | Required behavior / labels |
|---|---|
| CV1 `C912_Cold_volumes_seed_accepts_busy_main_without_packages` | All eight absent/empty combinations; main busy/accepting, only absent roots initialized, uid/mode/labels correct, final roots empty, marker cold. `cold-ready`, `main-unchanged`, `no-package-network-or-slot-call`. |
| CV2 `C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | Every target, main/running/stopped/bind alias, hidden content, foreign roots, unknown census/status/inspect. Exact code, no target create/probe/repair/marker. `preflight-no-write-<variant>`. |
| CV3 `C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries` | Scripted main ID/image/mount/attachment/content changes before/after creation/probes and publication; actual read trace, refusal before next write, no unsafe cleanup. `phase-proof-recorded-Pn`, `recheck-refused-Pn`. |
| CV4 `C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit` | Real canary create/rename/delete in private fake volume; uid 1654, network none, no lease; fail each operation, timeout/helper cleanup, residue; outside sentinel retained. `probe-writable`, `probe-refused-<variant>`, `probe-owned-helper-stopped`. |
| CV5 `C912_Cold_marker_has_distinct_validation_and_full_context_refusal` | Actual front door/bridge with fake transport, boolean Cold and source conflict; exact cold fields/no hash/recovery; legacy full validation unchanged; admitted reuse succeeds with populated cache, full-required rejects cold; malformed mixed/duplicate/symlink refuses. `cold-transport`, `cold-marker-valid`, `full-context-refused`, `marker-reuse-no-probe-or-restore`. |
| CV6 `C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads` | Actual deployment/verify/retire caller and receipt paths choose allow-cold; cold Both/Retired check status/shared/private mounts/probe evidence without smoke/hash/recovery; full mode still requires them; mixed receipt kinds refuse. `cold-verify-empty-cache`, `cold-no-smoke`, `full-smoke-retained`, `mixed-kind-refused`. |
| CV7 `C912_Cold_exception_never_weakens_maintenance_or_donor_gates` | Reset/Prune/saved Seed full gates; live donor donor-specific gates; default absent-marker Seed never infers cold; cold marker on maintenance/donor path returns FullSeedRequired. `full-gate-refused-<operation>`, no destructive actions. |

### Refusal-code table: one owning test per code

Each row gives exactly one named method via its roster ID, not a suite. Multiple codes/variants inside a method remain one result. Assert exact code, accepted=false, host exit 2, label `refusal-<code>-<variant>`, no marker and unchanged main. Preflight no-write and post-probe exact-owned-cleanup assertions apply. Bridge rejection happens before transport. Preserve existing codes on legacy branches.

| Code | One detecting test | Frozen stimulus |
|---|---|---|
| `CacheColdModeInvalid` | CV5 | Cold on non-Seed, malformed/nonboolean transport; no upload/host operation. |
| `CacheDonorSourceConflict` | CV5 | Cold+SavedDonor or cold first creation with donor; no fallback. |
| `CacheFirstSeedPreconditionUnknown` | CV2 | Missing/duplicate/wrong main, unreadable/malformed/omitted/type/null/negative/fractional facts, failed/incomplete volume/container census/inspect. Explicit retired runnerSessions null is positive. |
| `CacheFirstSeedMainMountChanged` | CV3 | Change full ID/image or mount type/name/source/destination/RW after initial proof. |
| `CacheFirstSeedVolumeInUse` | CV2 | Any target on main/other/stopped container or bind alias/ancestor; no name-prefix helper exemption. |
| `CacheUnmarkedContent` | CV2 | File/directory/hidden entry in any unmarked target; CV3 adds late content races. |
| `CacheVolumeForeign` | CV2 | Driver/options/owner/schema/role mismatch. |
| `CacheRootInvalid` | CV2 | Symlink/non-directory root, outside sentinel unchanged. |
| `CacheRootOwnershipInvalid` | CV2 | Wrong uid/gid/mode; no silent repair. |
| `CacheFirstSeedTempNotRetired` | CV2 | Well-typed readable temp violates retired/admission/redirect/zero predicate. Missing fields are Unknown. |
| `CacheTempContainerExists` | CV2 | Stopped or unrelated temp-project container despite retired status. |
| `CacheDonorLookupFailed` | CV2 | Failed source/temp lookup, never absent. |
| `CacheHelperImageMissing` | CV4 | No inspected usable existing helper image; no pull/build. |
| `CacheVolumeCreateFailed` | CV4 | Fake volume creation failure; no probe/marker. |
| `CacheVolumeInitFailed` | CV4 | New-root initialization fails; existing roots never altered. |
| `CacheRootNotWritable` | CV4 | Canary create, rename or delete failure, including residual own canary after nominal success. |
| `CacheColdProbeTimeout` | CV4 | Fake 10-second bound reached while probe active, stop only owned helper. |
| `CacheSeedProbeCleanupFailed` | CV4 | Removal fails or helper still in census; no marker, no uncertain broad cleanup. |
| `CacheSeedRequired` | CV5 | Missing marker/default Seed with no source; no implicit cold, actionable explicit Cold command. |
| `CacheSeedMarkerInvalid` | CV5 | Empty/damaged/symlink/duplicate/unknown/mixed-kind marker or malformed cold identity. |
| `CacheFullSeedRequired` | CV5 | Valid cold marker passed to full-required validator for maintenance/donor contexts; no payload operation. |
| `CacheRecoveryInvalid` | CV5 | Legacy full recovery outside confined path; unchanged refusal. |
| `CacheRecoveryMissing` | CV5 | Legacy full recovery missing; cold never checks recovery. |
| `CacheSeedPayloadChanged` | CV5 | Legacy full payload hash mismatch; cold never checks package hash. |
| `CacheConsumersBusy` | CV7 | Reset/Prune/saved Seed non-drained, accepting, nonzero/unknown counters/processes/attachments. |
| `CacheConsumerUnknown` | CV7 | Full-gate process/container lookup failure. |
| `CacheStatusUnavailable` | CV7 | Full-gate status unreadable. |
| `CacheBuildSlotsUnavailable` | CV7 | Full-gate broker missing; cold path makes no broker call. |
| `CacheBuildSlotsBusy` | CV7 | Full-gate broker occupied/nonempty leases. |
| `CacheResetInUse` | CV7 | Stopped consumer at Reset all-container check. |
| `CacheSeedAlreadyReady` | CV7 | Legacy full accepted marker at Reset; no clear. Cold returns FullSeedRequired. |
| `CacheDonorNotIdleDrained` | CV7 | Live donor non-drained/accepting/positive/null/unknown counters, no stop/copy. |
| `CacheDonorConsumerBusy` | CV7 | Live donor app-uid cache process despite zero counters. |
| `CacheDonorConsumerUnknown` | CV7 | Live donor process lookup failure. |

Existing unchanged archive/Fixture/rolling diagnostics retain their existing tests; no global renaming. Rolling RunnerCounterUnknown/RunnerBusy/OldRunnerStillBusy/OldRunnerDrainConflict/RunnerApiFailed stay in the single test-deploy-server2 harness T-3/T-12/T-13/T-14/T-16. New caller-specific cold receipt failures must be labelled within CV6 rather than silently accepted or counted as extra tests.

### Deterministic controls and red-first execution

Stubs replace Docker/curl/transport/process/clock primitives only; real predicate/jq/marker/probe shell executes. Unrecognized network/package/slot calls fail the harness. Fake Docker inspects tiny real throwaway directories and records every effect; actual uid observed by the probe is controlled at its process boundary and asserted in the launch trace. Execute create/rename/delete for real in those directories. Native pwsh runs front-door/bridge with recording invoker stubs. Never stub the guard/validator under test to return success.

CV3 phases: **P0** before mutation; **P1** after initialization; **P2/P3/P4** after packages/scratch/npm probe respectively; **P5** after all helpers removed; **P6** immediately before marker rename. Each probe also has a pre-start proof. Fake operation completion changes facts independent of guards; reads record actual phase proof and subsequent effects. Assert exact refusal before the next effect and phase-proof-recorded for each boundary, so removing an adjacent check cannot hide behind a later refusal. During each probe only its exact ID is allowed; at quiescence none. Main identity/admission and outside sentinels must remain unchanged.

CV4 uses fake monotonic 0 -> 10 seconds and a fake owned process with Running/Exited/StopTree observation plus unrelated sentinel. Synchronously complete one production monitor evaluation, snapshot state, then release the fake probe. Removing stop fails an ordinary `probe-owned-helper-stopped` assertion, never a hang. Events/pipes or atomic numbered request/ack barriers are released in finally; no sleeps/race margins below **2 seconds**. Watchdogs are diagnostics only. Record production cleanup before harness cleanup so the latter cannot mask a mutant. No lease controls survive the direction-2 refinement because there is no cold build/lease producer to test.

S1 intended reds: CV1-CV6 (six named assertion failures), CV7 plus 16 legacy methods green (17); rolling green. Existing entry points must remain callable; undefined future helpers/parser errors/timeouts are not red evidence. First assert observable verdict/trace, then new receipt. Any necessary legacy binding expectation change is declared before its run, not excused afterward. Commit/push every slice. Final rows run only after S2/S3; no routine intermediate green batteries. Selected failures/skips are not dismissed as unrelated flakes; missing Windows coverage remains pending.

### Positive controls

Every row names a detecting method from the roster and concrete production mutations. Execute each separately after land: original green -> intended assertion red -> restored green. Single filter `/*/*/RemoteScriptContractTests*/<exact method>`; no syntax error/zero-test/harness-timeout detection. Tests unchanged. Script mutations use private fakes only.

| PC / variants | Concrete mutation | Detecting test / named assertion red |
|---|---|---|
| PC-1 / 1 | Require main drained/zero for cold | CV1 / cold-ready. |
| PC-2 / 3 | Census main only; running only; ignore hidden content | CV2 / refusal-VolumeInUse-other, refusal-VolumeInUse-stopped, refusal-UnmarkedContent-hidden (full code labels as above). |
| PC-3 / 1 | Treat inspect error as absence | CV2 / preflight-no-write-inspect-error. |
| PC-4 / 9 | Remove P1-P6 individually; ignore ID/image/mount change individually | CV3 / phase-proof-recorded-Pn then recheck-refused-Pn; MainMountChanged-id/image/mount. Fact changes do not wait on removed guards. |
| PC-5 / 2 | Admit nonretired temp; coalesce missing runnerSessions to null | CV2 / refusal-CacheFirstSeedTempNotRetired and refusal-CacheFirstSeedPreconditionUnknown-temp-counter-omitted. |
| PC-6 / 4 | Skip canary create, rename, delete individually; accept cleanup failure | CV4 / probe-writable-create/rename/delete from operation trace, probe-refused-cleanup. |
| PC-7 / 1 | Omit StopTree on probe deadline | CV4 / probe-owned-helper-stopped using finite evaluation snapshot. |
| PC-8 / 3 | Accept cold in full context; accept mixed/duplicate marker; require payload on cold reuse | CV5 / full-context-refused, malformed-marker-refused, marker-reuse-no-probe-or-restore. |
| PC-9 / 3 | Drop Cold transport; call smoke on cold verify; accept mixed receipt kinds | CV5 cold-transport; CV6 cold-no-smoke and mixed-kind-refused. |
| PC-10 / 4 | Apply exception to Reset, Prune, saved Seed, live donor individually | CV7 / full-gate-refused-reset/prune/saved/donor; harmless fake destructive operations. |
| PC-11 / 2 | Remove redeploy-old zero guard; remove drain ownership guard | test-deploy-server2 / T-12 redeploy-old sessions omitted verdict; T-16 main-conflict verdict. Production wrapper edited only in a scratch Mutation task. |
| PC-12 / 1 | Copy raw synthetic status/environment into cold receipt | Existing C849_Cache_receipts_exclude_credentials_and_payloads / cold-toxic-sentinel-absent. |

Total **12 families /34 variants**; Mutation floor **34 x 3 +22 =124 minutes**, before slot waits. No variants executed in TestDesign. No new TUnit method per refusal/variant: seven additions total.

### Checkpoints

Closed list, distinct from former operational CP-3 Fixture. Every TUnit row: one isolated build and one exact literal filter; class trailing `*`. Markdown `\|` escapes a filter pipe, importer removes it; direct quoted filters use plain `|`, parenthesized operands. TUnit uses `dotnet run --project tests/Antiphon.Tests`, never dotnet test or discovery counts. Non-TUnit Build/Min n/a; internal assertions are not Min. Final Linux **27**, Windows **21** = **48** executions; S1 adds 27 diagnostic results, ordinary total **75**. All final selected rows require zero failures/skips. Windows CP-5/CP-6 are **pending a separate Windows task**, not certified by Linux.

CARD-0945 count correction: CARD-0933 Final Review (`a8e5a1b9`/`02a60422`) measured 25 Linux and 20 portable executions at `1f4d12efa3`, all passed with zero skips. Remeasurement at this docs task's base `808677658cc418dc439d906de4526aaea32e231a`, which also contains CARD-0973's two C849 methods, used `scripts/run-checkpoint.ps1` with the literal CP-3 and CP-5 filters: **27/27** and **21/21** passed, zero failures/skips, clean source and verified build binding. The CP-5 filter ran on Linux to measure its selection, not to certify Windows. Receipts: `.antiphon/c969-base-measure/M-Linux-20261002-143111-a01a/source.json` and `.antiphon/c969-base-measure/M-Portable-20261002-143451-d889/source.json`. jq 1.7.1 was installed in scratch only after verifying SHA-256 `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`. CP-2/CP-4 remain unchanged.

Filter-authoring caveat (CARD-0945): a parenthesized pair of exact method names with no wildcard executed **0** tests (exit 3); the cause was not confirmed. Use a prefix filter such as `/*/*/RemoteScriptContractTests*/C912_Cold_seed_*`, which executed the intended two tests, and check executed names and nonzero counts. Do not infer execution from discovery or a syntactically accepted filter.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c912-red-remote/` | red-cold-contracts | `/*/*/RemoteScriptContractTests*/(C849_*)\|(C912_*)` | CV1-CV6 red, CV7/legacy regression | Linux 27 executed; 6 named assertion reds, 21 passed, 0 skipped; diagnostic | 27 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c912-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | n/a | red-rolling | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | T-1..T-17 | Linux 17 groups/48 invocations/155 assertions, 0 failures, 0 TUnit | n/a | 6 | true | n/a |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c912-linux-remote/` | linux-cold-contracts | `/*/*/RemoteScriptContractTests*/(C849_*)\|(C912_*)` | CV1-CV7, all existing C849 | Linux 27 executed, 0 failed/skipped | 27 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c912-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S3 | n/a | linux-rolling | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | T-1..T-17 | Linux 17 groups/48 invocations/155 assertions, 0 failures, 0 TUnit | n/a | 6 | true | n/a |
| CP-5 | S1-S3 | `tests/Antiphon.Tests -> bin-c912-windows-remote/` | windows-cold-contracts | `/*/*/RemoteScriptContractTests*/(C849_front_door_passes_every_full_case_name_to_the_invoker)\|(C849_Cache_prepare_*)\|(C849_Prune_*)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads)\|(C849_Retired_guard_refuses_docker_ps_errors)\|(C849_Deploy_*)\|(C849_Cache_receipts_exclude_credentials_and_payloads)\|(C912_*)` | CV1-CV7, twelve portable C849; pending Windows | Windows 21 executed, 0 failed/skipped; five inherited Linux-pwsh methods excluded | 21 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c912-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1-S3 | n/a | windows-rolling | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | T-1..T-17; pending Windows | Windows 17 groups/48 invocations/155 assertions, 0 failures, 0 TUnit | n/a | 6 | true | n/a |

## Execution, live order and rollback

Use checkpoint tool once per committed slice group, then wait until exit is not 75. At S1 select CP-1,CP-2; final Linux CP-3,CP-4; Windows CP-5,CP-6. Example: `dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c912-tool/ -- run --plan docs/superpowers/plans/2026-10-02-card-0849-first-seed-exception-plan.md --rows CP-3,CP-4 --expected-source-sha <clean-full-SHA>`. Reuse emitted run ID with `wait <id> --max-wait 50s`; never restart an active run. Declare any needed launcher bootstrap and lease it through `scripts/build-slot.ps1`; rows self-lease, no outer slot while waiting. No unlisted builds/tests without stated reason. Preserve each CHECKPOINT line, executed names/counts/TRX, slot/source/dirty/buildSource and reruns. S1 reds do not certify final acceptance.

Run live only after implementation/Review/publication, from trusted desktop checkout matching full reviewed C849_DEPLOY_SHA; use existing token handling. Refresh status, exact container/volume/mount facts. The code task must not recreate its own host. No -Phase all.

| Order | Required action/evidence | Rollback or refusal |
|---|---|---|
| 1 | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed -Cold`; server2 still accepting with work in flight. Three validated empty/writable volumes, repeated proof, cold marker, zero package/network/build-slot calls. | Main untouched; remove only exact owned canary/helper. On consumer/uncertainty retain volumes, no Reset/drain. Refusal never changes source mode. |
| 2 | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $env:C849_DEPLOY_SHA -Phase deploy-temp`; ordinary Seed reuses cold marker; mounts/private state/tmp/dind and uid writes verified before clearing temp. | Failed temp stays held; main accepts. No drain-old. Keep shared volumes; fix precise failure. |
| 3 | Fresh temp SHA/status/acceptance and cold verification receipts, then caller commissions one allowed-kind canary **Plan** task on server2-temp after capacity checks. Require transcript-confirmed delivery, useful completed Plan, normal settlement and correct runner binding. | Failure keeps main accepting; hold temp admission as needed, let work finish, no kill/automatic reroute. Health/registration alone is insufficient. |
| 4 | Explicit `-Phase drain-old` only after step 3; verify redirect=server2-temp, retireWhenIdle=false, sessions/runnerSessions/queue reach nonnull zero naturally. | Timeout retains durable drain. To defer, existing runner-drain clear on server2, verify accepting again; never kill work. |
| 5 | **Optional best-effort snapshot before redeploy-old**: while old container still exists and drained, save only its package/npm cache trees through the trusted existing host lane to a new private run path; no homes/credentials/worktrees/scratch locks. Record exact source container and selected paths, completion/partial status. This is operator work, not a new script feature or Seed input. | Skip/failure is not a rollout gate. Retain a partial save clearly labelled; do not import it automatically or change marker kind. Do not delete old data before the optional attempt completes or is explicitly skipped. |
| 6 | Explicit `-Phase redeploy-old`, fresh zero/drain ownership, then verify new main SHA/mounts/private roots/writability before clear. Empty caches are acceptable; ordinary builds populate them from network. | Failed replacement/verification keeps main held, verified temp continues. Prior image recovery uses normal drain/zero/mount/writability rules; no invented cache recovery requirement. |
| 7 | Optional Both cold verification, normal drain-temp/retire-temp, Retired cold verification: shared volumes retained, temp private volumes gone, main writable/accepting and rollback image identity retained. | Preserve external volumes; do not clear a failed runner or require apphost/hash/recovery on cold mode. Full-marker rollout still uses old full checks. |

Snapshot timing is after drain-old and before any redeploy-old destruction. Cache misses cost restore time/network traffic; the first real builds can be slower, but no cache payload is a prerequisite. Writability/mount correctness and runner lifecycle remain hard gates. Saved archive retained untouched. Cold-to-full maintenance is a separate explicit operation, not automatic rollback. Code rollback is a new forward commit with normal activation, never rewritten pushed history.

## Cost, validation and handoff

Ordinary floor **50 minutes** (S1 16 + final Linux 16 + Windows 18), three isolated builds; final-only floor 34. Mutation floor **124**, combined **174 minutes** before authoring/bootstrap/slot waits. Authoring estimate 1-3 hours; no live Fixture/restore/smoke time budget. Live phases/canary and optional snapshot are separate operator work. No padding with slot/provider/land suites.

Static Node source/manifest checks passed: six-row/eleven-column schema, escaped filter pipes, original seven unique new methods, **34 unique refusal-code rows with one valid owner each**, 12 PC families/34 variants and 50-minute ordinary sum. Current measured selections are 27 Linux/21 portable executions as recorded under Checkpoints; the original TestDesign executed no builds/tests. `git diff --check` passed. Review follows the operator's regression-only rule: failure needs an existing-behavior regression or new reachable fail-open/privacy exposure; missing qualification stays pending, never passed.

Next Code after CARD-0905 lands: recount, implement the seven-file volumes-only scope, run the closed rows, and report Windows/live work pending where applicable. The second refinement controls; all online/offline package/smoke/lease and saved-Fixture additions are superseded. No unresolved operator design decision is required.
