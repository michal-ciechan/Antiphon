# CARD-0975: refuse source reset when worktree registration is uncertain

Date: 2026-10-05. Board: Antiphon. Card: `72c53887-9c66-4a8d-bb87-0b9ef9432300`.
Plan baseline: `be16e6c35b5fd9ced3622c2b3e313ae836f416f8`.
Verification design is folded into this plan. Next implementation stage: Code.

## Outcome and scope

Reviewed owner recovery and adoption must observe the current registration before
moving the owner ref and again before resetting its checkout. Corrupting the linked
worktree admin `gitdir` backlink must produce a refusal before the next destructive
step, even if an operation-scoped registration cache was populated earlier. Keep
the checkout, index, unrelated observer checkout and remote refs intact. If the
ref already moved, retain the half-reset state and durable recovery intent.

This is a small production change in two files, with real-Git boundary tests.
It is not a new recovery protocol. Ordinary branch-based schema-3 land stays
branch-based. Existing strict proof still governs interrupted reset recovery.

## Ground truth

Line references below identify the plan baseline, not future implementation lines.

| Card assumption or question | Current code and observed evidence | Consequence |
|---|---|---|
| A lease-scoped cache might hide backlink corruption. | `AgentTaskLandService.cs:477` opens an operation scope. `LandingGit.cs:195-223` caches the worktree listing using only the common worktrees directory's timestamp and entry count. It does not fingerprint each admin `gitdir`. | Editing a nested admin file need not invalidate the listing. The repository lease does not prevent an external filesystem write. |
| Source identity is rechecked before ordinary reviewed adoption. | `AgentTaskLandSourceResolver.cs:450-477` reaches the before-ref boundary, rechecks authority/remotes and calls IdentityAndStatus before CAS. `LandingGit.IdentityAsync`, at line 712, explicitly requests `live: false`. | The identity recheck can accept the earlier registration even though Git's current listing names another path. |
| A refusal means no reset happened. | After CAS, `AgentTaskLandSourceResolver.cs:479-483` reaches the after-ref boundary and runs reset without another inspection. Its subsequent inspection can refuse after reset already changed the checkout. CAS invalidates registrations, so this later read can finally see the corruption. | A refusal alone is an insufficient oracle. Assert zero reset commands and unchanged bytes at the relevant boundary. |
| Existing registration coverage reproduces the card. | `AgentTaskLandHalfResetTests.C883_IdentityChangesBeforeResetRefuse` now uses actual `git worktree move` for its registration argument. The card records the removed admin-file corruption case at historical commit `7ec41e1f18d4d47e867c26a62f4801afccec8399`. | Keep the supported-move regression and add separately named corruption tests; do not replace one with the other. |
| Recovery's strict proof provides a fresh registration independently. | `InspectRecoveryCheckoutAsync` calls IdentityOnly at entry and exit; both use the same cached IdentityAsync path. The resolver repeats strict proof immediately before resumed reset. | Fix the shared identity read so entry, exit and repeated recovery proofs inherit freshness. Do not weaken the byte/index proof. |
| Cleanup needs a new deletion protocol. | `GuardedWorktreeRemoval.cs:101,120` already inspects Full identity/status twice; unregister separately reads the admin backlink. | Fresh identity reads strengthen these existing guards. Add a cleanup regression, without redesigning deletion or publication authority. |
| CARD-0954/0955/0971/0972 are all unfinished proof changes. | Live card reads: 0954 is Done; 0955 concerns redacted diagnostic frames; 0971 documents ignored nested-repository refusal; 0972 tracks earlier Windows/PC obligations. Current code permits unrelated ignored residue. | Preserve 0954 behavior. This card does not implement diagnostic changes, broaden ignored-directory acceptance or discharge earlier cards' historical verification obligations. |

Plan-stage experiment: a disposable Linux Git repository, linked source worktree
and separate observer repository were created below an owned temporary root. Only
the source admin `gitdir` was rewritten to the observer's `.git`. Seven assertions
passed: unchanged parent stamp, unchanged parent entry count, original listing
names source, fresh listing drops source and names observer, unchanged source
HEAD, unchanged index hash, and still-clean source status. Scratch was removed.
This confirms the Git/cache premise; it is not a service-level regression run or
Windows evidence. No product build or TUnit run was performed during Plan.

## Decisions

- **D-1 — Make identity proof use a live registration listing.** Change
  `LandingGit.IdentityAsync` to use `ListRegistrationsAsync(..., live: true, ...)`.
  Both readings of Full/IdentityAndStatus, IdentityOnly, and the strict recovery
  proof therefore read current registration rows. Keep cached `RegistrationsAsync`
  for callers that explicitly request it, and keep the existing live read's cache
  refresh. This is the smallest shared fix and requires no new interface or DTO.
  Update the old inspection-cache test to assert the deliberate new contract:
  two successful Full inspections perform four lists and zero registration hits.
  Reject recursive admin-directory fingerprinting, a larger timestamp heuristic,
  a new cache TTL and globally removing all operation caches: none is needed to
  make a mutation-authorizing identity read fresh.
- **D-2 — Recheck identity after CAS, before reset.** After the existing
  source-adopt-ref-moved-before-reset boundary, call IdentityOnly and require
  acceptance, HEAD and branch SHA equal to reviewed S, and common directory,
  registered path, Git directory and symbolic ref equal to the pre-CAS snapshot.
  Compare identity fields explicitly, with the existing path comparison semantics;
  full record equality would incorrectly compare L with S. Refuse as
  `adopt_local_changed` if any check fails. This read adds no successful-path
  database save between CAS and reset. Do not use IdentityAndStatus here: the
  index and bytes are legitimately still at L. Do not apply the stronger interrupted
  byte proof to ordinary adoption: that would change its ignored-file contract.
- **D-3 — Refuse without repair or rollback on uncertainty.** Before CAS, propagate
  existing inspection reasons (the original redirected-backlink case should be
  `registration_mismatch`). A change during an inspection remains `source_changed`;
  unreadable listing retains existing sanitized I/O diagnostics. After CAS, leave
  ref=S, index/bytes=L and the saved source-adopt-reset intent plus L/S pins intact.
  Never reverse CAS, repair the backlink, force-reset or clean up an uncertain
  checkout. Valid later explicit recovery can use the existing witness contract
  after registration is repaired independently.
- **D-4 — Preserve strict recovery, approval and publication semantics.** No new
  migration, settings, endpoint, journal state or retry. Both owner self-recovery
  and adoption from another reviewed task use these guards. Existing SHA approval,
  remote lease checks, child custody and original owner status remain authoritative.
  A publication already confirmed before cleanup corruption remains confirmed,
  with cleanup refused and residue retained.
- **D-5 — Bound the performance tradeoff and its guarantee.** Identity inspections
  intentionally spend one worktree-list process per identity sample; cached list
  callers still benefit from CARD-0642. Assert counts in the focused cache tests
  and record checkpoint timings. Do not introduce performance thresholds or retries
  to conceal failures. This detects corruption present at the named observations;
  it does not provide atomic protection against arbitrary external metadata/path
  replacement after the final read. A handle-pinned Git/filesystem redesign is
  outside this repair.
- **D-6 — Separate portable and native execution.** GET /api/runner-defaults and
  GET /api/session-runners were read on 2026-10-05: defaults revision 2 and eligible
  Linux and Windows lanes were visible. Re-read both at dispatch. Omit -Runner;
  omit -Platform for portable work and specify -Platform Windows for CP-5/CP-6.
  -Platform Any is only for clearing an inherited constraint. No host or fleet
  filesystem location is part of the plan.

These are implementation decisions within the requested safety repair, not
unresolved operator defaults. No decision-stage approval is needed.

## Implementation slices

Commit and push each slice before its checkpoint group. Each slice is intended
to fit 30-60 minutes of authoring and local review; checkpoint time is additional.
S2 consumes S1, and S3 consumes S2. Re-read the exact methods on the Code baseline
to account for concurrently landed changes; never rebase a pushed runner branch.

| Slice | Authoring budget and lane | Files and concrete change | Tests and completion |
|---|---|---|---|
| S1 | 40-50 minutes; Any | `server/Infrastructure/Git/LandingGit.cs`: D-1 and its contract comment. `tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs`: rename the old inspection-cache test to `C975_InspectAsyncAlwaysReadsCurrentRegistrations` and update its counts. New `tests/Antiphon.Tests/Infrastructure/LandingRegistrationFreshnessTests.cs`: V-1 through V-4. | Keep explicit cached-list tests unchanged. Add real-Git stale-cache, second-sample, strict-proof and failed-live-list controls. Commit/push, then CP-1. |
| S2 | 50-60 minutes; Any | `server/Application/Services/AgentTaskLandSourceResolver.cs`: D-2 guard and comparison, using existing LandDeliveryBoundary. New `tests/Antiphon.Tests/Application/AgentTaskLandRegistrationSafetyTests.cs`: V-5 through V-8. `tests/Antiphon.Tests/TestHelpers/LandHalfResetFixture.cs`: minimal reusable corruption/snapshot scenario helpers for portable and Windows cases. `docs/orchestration-loop.md`: add the live-registration/refusal rule to reviewed recovery guidance. | Implement both owner/adoption variants, late corruption, same/fresh resume and cleanup preservation. Use existing hooks and fixture ownership; no production test-only API. Commit/push, then CP-2 through CP-4. |
| S3 | 30-40 minutes; Windows qualification | New `tests/Antiphon.Tests/Application/AgentTaskLandRegistrationSafetyWindowsTests.cs`: V-9/V-10 wrappers over the S2 scenarios, with a temp root containing spaces and actual Git-for-Windows paths. Update this plan only for implemented method census or evidence obligations that changed. | Wrong-OS selection must fail visibly. Commit/push, then CP-5/CP-6 on Windows. Do not substitute Linux results for these rows. |

No edits to generated docs/cards files. No source cleanup, deployment or live
worktree corruption is part of implementation or verification.

## Verification design

Use `LandingGitFixture` and `LandHalfResetFixture`: all repositories, remotes,
configuration, database schemas and observer checkouts are fixture-owned. New
process-spawning classes carry the assembly-local ParallelLimiter<ProcessSpawnLimit>.
Do not involve a production runner or live repository in fault injection.

The corruption helper derives the admin directory from Git before injection; it
does not guess the worktree registration basename. Save the original backlink and
restore it only in fixture disposal, after outcome assertions, to permit safe
fixture teardown. Write the corrupted backlink through File I/O, not the scoped
Git object. Assert the original checkout remains present and the common worktrees
stamp/count remain unchanged. On a filesystem that cannot provide that witness,
fail the fixture visibly instead of accepting a non-reproduction.

Capture the owner branch, owner index hash, tracked source bytes, owner .git file,
observer HEAD/index/bytes and all relevant remote refs before the measured phase.
The self-recovery owner's remote is already S; adoption's owner remote may still
be L. Compare each to its captured value, not an assumed common SHA. Count commands
only after setup, and distinguish owner branch CAS from recovery-pin writes.
Use selected command-list ShouldBeEmpty or Any(...).ShouldBeFalse assertions;
avoid the historical Shouldly predicate-formatting failure masking the oracle.

### Required ordinary cases

All named C975 methods below are new except the S1 rename. Counts are expected
TUnit argument-expanded results, not internal assertion counts.

| ID | Exact test method and cases | Setup and required observable result |
|---|---|---|
| V-1 | `LandingRegistrationFreshnessTests.C975_CachedBacklinkChangeRefusesIdentity` — 3 scope arguments | Warm a registration cache inside BeginOperationScope; rewrite only admin gitdir to the owned observer .git. Inspect IdentityOnly, IdentityAndStatus or Full. Each refuses registration_mismatch, with source HEAD/index/bytes unchanged. Assert that a fresh list actually occurred after corruption. |
| V-2 | `LandingRegistrationFreshnessTests.C975_SecondIdentityReadSeesBacklinkChange` — 2 scope arguments | Corrupt after the status command in IdentityAndStatus/Full, using the fixture callback. The first identity was valid. Require source_changed at the second sample; no accepted snapshot, no mutation. |
| V-3 | `LandingRegistrationFreshnessTests.C975_RecoveryProofSeesBacklinkChange` — 1 | Seed real HEAD=S, index/bytes=L and warm the cache. Corrupt during the proof after the initial IdentityOnly sample (a one-shot callback after the reviewed ls-tree). Require adopt_local_changed at proof exit and unchanged L bytes/index. This is a direct proof test, so a later resolver guard cannot hide a missing final sample. |
| V-4 | `LandingRegistrationFreshnessTests.C975_RegistrationReadFailureDoesNotReuseCache` — 1 | Warm a valid listing, then make the next list return exit 128 using the Git fixture seam. Inspection refuses identity_io_error with the existing sanitized command diagnostic. No fallback to a cached accepted snapshot. |
| V-5 | `AgentTaskLandRegistrationSafetyTests.C975_CorruptedBacklinkBeforeMoveRefuses` — 2, owner/adoption | At source-adopt-before-ref-move, redirect the admin backlink, keeping source HEAD/index at L. Assert boundary fired once, registration_mismatch, zero owner CAS, zero reset in source or observer, zero owner/target push, no publication/cleanup, and captured bytes/refs preserved. This restores the card's missing service regression. |
| V-6 | `AgentTaskLandRegistrationSafetyTests.C975_CorruptedBacklinkAfterMoveRefuses` — 2, owner/adoption | At source-adopt-ref-moved-before-reset, corrupt once without throwing. Assert one successful owner CAS to S, adopt_local_changed, zero reset/push/cleanup, index/bytes still L, observer unchanged, and durable intent/L/S pins preserved. There must be no compensating CAS. |
| V-7 | `AgentTaskLandRegistrationSafetyTests.C975_CorruptedBacklinkDuringResumeRefuses` — 2, same/fresh request | Use the existing historical interruption to seed HEAD=S and L checkout, then resume normally. Inject a one-shot backlink change during strict proof, after its first identity read. Both request shapes refuse before reset/publication and retain L bytes/index and intent. Verify the existing adopt_local_changed mapping. |
| V-8 | `AgentTaskLandRegistrationSafetyTests.C975_CleanupBacklinkChangePreservesPublicationAndBytes` — 1 | Use normal schema-3 landing. After acknowledged CleanupStarted, corrupt after source status in the final Full inspection; count only cleanup-phase samples. Require source_changed cleanup residue, the original publication identity/SHA unchanged, zero directory/registration/branch removal and preserved source/observer bytes. Assert no set-aside move was attempted using the cleanup result/journal; no synthetic publication refusal. |
| V-9 | `AgentTaskLandRegistrationSafetyWindowsTests.C975_SpacedBacklinkCorruptionBeforeMoveRefuses` — 2, owner/adoption | Run the V-5 scenario on Windows with spaced paths and Git-written backlink spelling. Require the same no-CAS/no-reset and preservation oracles. |
| V-10 | `AgentTaskLandRegistrationSafetyWindowsTests.C975_SpacedBacklinkCorruptionAfterMoveRefuses` — 2, owner/adoption | Run V-6 on Windows with spaced paths. Require the same retained half-reset/intent and no-reset oracles. |
| R-1 | `LandingGitTests.C975_InspectAsyncAlwaysReadsCurrentRegistrations` plus all five `LandingGitTests.C642_*` methods — 6 | Four lists for two Full inspections, zero registration hits there, correct profile accounting; explicit cached reads still cache, own/external registration changes invalidate, ignored listing scope and cheap remote recheck remain unchanged. |
| R-2 | All seven methods in `AgentTaskLandAdoptionConcurrencyTests` — 7 | Existing success, interruption and concurrent-writer cases still publish/recover as specified. In particular C883_MonitorBetweenMoveAndResetDoesNotPreventReset keeps zero successful-path request saves between CAS and reset. |
| R-3 | Six selected methods in `AgentTaskLandHalfResetTests` — 12 | C954_IgnoredResidueAllowsHalfResetPublication (2), C883_IgnoredFileDoesNotHalfResetOrdinaryAdoption (1), C883_IdentityChangesBeforeResetRefuse (3), C883_FreshRequestRepairsPinnedAncestor (2), C883_StagedEditSurvivesFreshAndSameRequest (2), C883_UnstagedEditSurvivesFreshAndSameRequest (2). Preserve valid recovery, ignored residue, real edits and supported registered moves. |
| R-4 | `WorktreeRemovalAuthorityTests.C688_PublicationCleanupDeletesBranchAtLocalSha` — 3 | Existing clean, branch-moved and remote-rewritten cleanup results retain their published-SHA/local-deletion-SHA distinction. |
| R-5 | `AgentTaskLandHalfResetWindowsTests.C883_LinkedWorktreeWithSpacesRecovers` and `AgentTaskLandHalfResetWindowsTests.C883_CaseAliasCannotChangeRegisteredIdentity` — 2 | Successful native spaced-path recovery still preserves ignored bytes; crossed case aliases cannot authorize another checkout. This is current-source regression evidence, not completion of CARD-0972's historical obligations. |

### Positive controls, after ordinary Review and confirmed land

Four controls, one per guarded behavior. Each cycle is one exact method, with all
of that method's argument cases; no class/suite PCs. Use a fresh SourceLanding
snapshot of the published SHA, local inherited execution, external evidence and
the required restoration receipt. No snapshot commits/pushes. Prove green, apply
the compiling defect, prove the named outcome assertion red, restore, rebuild and
prove green. Build/fixture errors or zero cases are not a red. Review verifies this
design before land; deliberate PCs are not ordinary Code checkpoints.

| PC | Deliberate defect | Exact detecting filter | Intended red |
|---|---|---|---|
| PC-1 | In IdentityAsync only, restore live:false registration reads. | `/*/*/AgentTaskLandRegistrationSafetyTests/C975_CorruptedBacklinkBeforeMoveRefuses` | The no-owner-CAS assertion fails (and reset may occur); a late refusal must not count as success. |
| PC-2 | Omit the new post-CAS identity guard while keeping D-1 live reads. | `/*/*/AgentTaskLandRegistrationSafetyTests/C975_CorruptedBacklinkAfterMoveRefuses` | The zero-reset assertion fails; later refusal cannot hide the reset. |
| PC-3 | Bypass InspectAsync's second identity rejection and return its first snapshot after status. | `/*/*/LandingRegistrationFreshnessTests/C975_SecondIdentityReadSeesBacklinkChange` | Expected source_changed/refused result becomes accepted. |
| PC-4 | Bypass only InspectRecoveryCheckoutAsync's final identity comparison after successful byte/index proof. | `/*/*/LandingRegistrationFreshnessTests/C975_RecoveryProofSeesBacklinkChange` | Expected adopt_local_changed proof refusal becomes accepted. |

### Execution and cost

The table is a closed list. CP-1 through CP-4 are Any; CP-5 and CP-6 are Windows
only and are dispatched with -Platform Windows. Group names encode the lane;
the importer has no Platform column. Select rows explicitly so Windows rows never
run in the portable dispatch. A selected wrong-host native row fails rather than
skips. No whole Unit, namespace or assembly run is authorized by this plan.

Use the checkpoint tool once per committed slice group:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c975-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c975-tool/ -- run --plan docs/superpowers/plans/2026-10-05-card-0975-corrupted-backlink-reset-plan.md --rows CP-1 --expected-source-sha <S1-sha>
# S2: same command, --rows CP-2,CP-3,CP-4 and <S2-sha>.
# S3 on Windows: --rows CP-5,CP-6 and <S3-sha>.
```

Use the tool's wait command until exit is not 75; keep the delegate alive until
all owned work exits. Bootstrap/tool compilation also takes a host build slot.
The checkpoint executor leases each driver itself. Never retry an exit-4 slot
timeout unleased. Keep source frozen during each run, and use alternate outputs
with forward slashes. Preserve generated CHECKPOINT lines, exact source SHA,
build binding, lane, counts and failures in the stored report; validate structured
receipts against that SHA. Keep payloads ignored. Remove only the run-owned
bin-c975-* outputs via checkpoint cleanup, and remove the owned tool bootstrap
bin-c975-tool directories after its process exits. Code/Review run
scripts/check-evidence-diff.ps1 over their full task range.

### Cost

Ordinary Code checkpoint floor: 38 minutes (10+10+4+2+9+3), plus 120-150 minutes
authoring across the three slices. Estimates include the named isolated build;
they are budgets, not measured timing claims. Imported per-row timeouts are at
most 30 minutes at these estimates. Review uses the same affected ordinary scope
on the final source SHA and native lane. PCs have four separate method-scoped
cycles after land; budget approximately 30-45 minutes plus investigation of any
survivor. Missing Windows capacity is a pending row, never green qualification.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c975-s1/` | any-registration-proof | `/*/Antiphon.Tests.Infrastructure/(LandingRegistrationFreshnessTests*)\|(LandingGitTests*)/(C975_*)\|(C642_*)` | V-1, V-2, V-3, V-4, R-1 | all 10 named methods, 13 results, 0 failed/skipped | 13 | 10 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c975-s2/` | any-adoption-boundaries | `/*/Antiphon.Tests.Application/(AgentTaskLandRegistrationSafetyTests*)\|(AgentTaskLandAdoptionConcurrencyTests*)/*` | V-5, V-6, V-7, V-8, R-2 | all 11 named methods, 14 results, 0 failed/skipped | 14 | 10 |
| CP-3 | S2 | CP-2 | any-recovery-regressions | `/*/Antiphon.Tests.Application/AgentTaskLandHalfResetTests/(C954_IgnoredResidueAllowsHalfResetPublication*)\|(C883_IgnoredFileDoesNotHalfResetOrdinaryAdoption*)\|(C883_IdentityChangesBeforeResetRefuse*)\|(C883_FreshRequestRepairsPinnedAncestor*)\|(C883_StagedEditSurvivesFreshAndSameRequest*)\|(C883_UnstagedEditSurvivesFreshAndSameRequest*)` | R-3 | all 6 named methods, 12 results, 0 failed/skipped | 12 | 4 |
| CP-4 | S2 | CP-2 | any-cleanup-regressions | `/*/Antiphon.Tests.Infrastructure/WorktreeRemovalAuthorityTests/C688_PublicationCleanupDeletesBranchAtLocalSha` | R-4 | all 3 argument results, 0 failed/skipped | 3 | 2 |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c975-windows/` | windows-backlink-boundaries | `/*/Antiphon.Tests.Application/AgentTaskLandRegistrationSafetyWindowsTests/*` | V-9, V-10 | all 4 argument results on Windows, 0 failed/skipped | 4 | 9 |
| CP-6 | S3 | CP-5 | windows-identity-regressions | `/*/Antiphon.Tests.Application/AgentTaskLandHalfResetWindowsTests/(C883_LinkedWorktreeWithSpacesRecovers*)\|(C883_CaseAliasCannotChangeRegisteredIdentity*)` | R-5 | both named methods on Windows, 0 failed/skipped | 2 | 3 |

## Out of scope and handoff

No automatic metadata repair, broader ignored-path acceptance, diagnostic-frame
change, remote publication algorithm change, general canonical/common-dir cache
redesign, host selection changes or recursive deletion. No claim of protection
against an adversarial writer racing after the last identity sample. No PC results
or native Windows results are claimed by this plan.

Commit/push this document on the assigned task branch only. Landing requires the
task to settle before the caller can order the standard server-managed land;
`LandApproval.RequestStatusEligible` does not admit a still-running Plan task. The caller
should promptly land Plan task `4d1acb84-e0e4-4acb-95f5-ec15870e2fe0` at the pushed
plan SHA, confirm publication on master, then dispatch Code with this checkpoint
manifest pinned to its plan commit. Do not push directly to master or rewrite
the task branch to satisfy that ordering. Code -> ordinary Review -> confirmed
land -> SourceLanding Mutation remains the implementation sequence.
