# CARD-1536: let published cleanup report an unregistered replacement directory

Plan task `e6fd2aa0`, 2026-10-23. Inspected task base and fetched master:
`353e30678c205a00b77d40cb44080e47218c7e85`; fetched rc/2026-10-20:
`5a126f6fc5ae4e16d803b628716b8f0485a37174`. Documentation only. No application
build/test, desktop access, process signal, or live qualification change occurred.
TestDesign is **separate**: the brief requests a verification proposal but does not
fold that stage into Plan. The table below is its bounded starting selection.

After publication, a replaced source directory must survive, and a retry must finish
with `CleanupRetry`, cleanup `Refused`, and `unregistered_directory`. It must not
remain an `INDEX LOCK` hold or publish again. The existing guarded remover already
implements the required refusal; the defect is admission before it.

## Ground truth

Line references below bind to the inspected base; resolve symbols after later changes.

| Card assumption / question | What the code and evidence establish | Implication |
|---|---|---|
| What is the replacement? | `tests/Antiphon.Tests/Application/AgentTaskLandCleanupSafetyTests.cs:104-129` publishes with an ignored report blocking cleanup, deletes that fixture-owned report, runs real `git worktree remove`, creates the same directory, and writes `new.txt` containing `new work\n`. The canonical repository still exists elsewhere. The replacement has no `.git` entry and no worktree registration. | This incident's producer is a deliberate real-filesystem test fixture, not a demonstrated runner-sync or manual-copy incident. It represents independently owned replacement content. |
| Where does it become a lock? | `AgentTaskLandService.cs:597-601` calls admission probes before loading the published operation at 653-656. `ProbeAdmissionIndexLockAsync:915-917` probes the source first; `ProbeCheckoutIndexLockAsync:944-952` exempts only a missing directory. `server/Infrastructure/Git/LandingGit.cs:953-957` maps failed Git path resolution to `index_lock_path_error`; `GitIndexLock.cs:73-74,92-96` maps any probe reason to Held. | The exact early return is service line 601. `AgentTaskLandingProtocol.cs:157-163` never stamps `CleanupRetry`; the prior Fresh mode remains. |
| Is an actual lock or holder demonstrated? | Path resolution fails **before** `LandingGit.cs:958` calls the CARD-1436 census. No lock is required. The qualification assertion is mode, not a captured hold reason. | Capture request state/reason in the new regression. Do not change the census or infer a historical holder. |
| What should happen? | `AgentTaskLandingState.HasPublication` requires durable identity, verification and remote-confirmation facts. The protocol rechecks coordinates/destination before cleanup. `GuardedWorktreeRemoval.cs:381-402` rechecks receipt, pins and fresh remote containment; its lines 57-61 and 101-103 refuse an existing unregistered directory as `unregistered_directory`. | Admit only the proven replacement case far enough to reach these guards; never grant removal authority in the admission helper. |
| Is this Windows-specific? | The exact unregistered/Fresh red is retained in the [September Linux repair](../../investigations/2026-09-25-card-0688-r1-repair-2.md) and both Windows runs in the [October diagnosis](../../investigations/2026-10-23-rc-5a126f6fc-qualification-reds-debug.md), section D. The failure path has no Windows-only branch. | Cross-platform defect. Case folding, short names, reparse points, read-only Git objects and native file locks are unnecessary to reproduce it. |
| Could sync ordinarily produce this shape? | [Session invariants](../../session-runtime-invariants.md) say a missing mirror is recreated with `worktree add`; pruning registrations does not delete checkout contents. CARD-0721 and CARD-0229 describe historical orphan shapes, with different causes. | No evidence assigns this fixture's replacement to sync. Preserve the safety boundary regardless of how foreign content arrived. |
| Does rc have it too? | The inspected rc retains the same source-first probe, missing-directory-only exception, path-error mapping and published cleanup branch. CARD-0543 origin `2339af1c40f4adb350d1923d524862b2fa9879a7` is ancestral to both lines. | This is inherited on master and rc, not introduced by today's rc bundle. A master land alone cannot clear the rc red. |

### Linux primitive observation

At `2026-10-23T09:18:10Z`, Linux Git 2.47.3, a private `/tmp/c1536-plain-*`
repository performed `init`, an empty seed commit, `worktree add`, `worktree remove`,
then `mkdir` and creation of `new.txt`. `worktree list --porcelain` contained only
the canonical checkout; `.git` was absent at the replacement. Running the exact
production command there, `git rev-parse --path-format=absolute --git-path index.lock`,
returned **128**, `fatal: not a git repository`, while `new.txt` retained its bytes.
The private scratch tree was removed. This is fresh evidence for the Git/filesystem
trigger, not a new execution of the .NET landing pipeline. The retained Linux test
red supplies the historical end-to-end evidence; fresh service red/green is owed.

### Duplicate check

Read CARD-1536 in full, searched Antiphon including archived cards for `unregistered`
and `index lock`, and read the relevant hits plus all five brief-named cards.

| Card (state at read) | Boundary / disposition |
|---|---|
| CARD-0308 (Done) | PTY child-tree termination and real directory holders; no path-resolution admission repair. |
| CARD-0820 (Done) | Windows timing/temp-root contention, including a controlled unregister race; no deterministic plain-directory retry repair. |
| CARD-1436 (Done on master and rc) | Exited-during-census Git processes. This failure returns before the census. Preserve its live-unknown-holder behavior. |
| CARD-1515 (Review) | Owned PTY fixture filesystem release after process exit; unrelated native deletion failure. |
| CARD-1539 (Review) | Holder capture diagnostics. Independent work; absence of a repository here is not evidence of a holder. |
| CARD-0543 (Done) | Introduced lock admission. Preserve actual/uncertain checkout lock protections. |
| CARD-0721 / CARD-0229 (Done) | Historical unregistered leftovers: cached fixture registration/restore and old janitor cleanup. Neither owns this published-retry hold. CARD-0229's old recursive-delete behavior is not authority under today's conservative cleanup contract. |

Keep CARD-1536; no duplicate card or dependency on holder diagnostics is needed.

## Decisions

**D-1 — Narrow the source admission exception to a proven published replacement.**
Keep the normal source probe. Only its `index_lock_path_error` result is eligible
for a private helper in `AgentTaskLandService`. Under the existing execution and
repository leases, after the existing writer exclusion, require all of:

- The task's current active operation exists, belongs to this task, is Active, and
  passes the existing `AgentTaskLandingState.HasPublication` predicate. Compare the
  task/operation repository, source path, full source/target refs and repository
  common directory using the existing path/ref conventions. For `CleanupOnly`, its
  required operation ID must also match. Do not trust event prose or mode alone.
- The source is an existing ordinary directory whose canonical path equals the
  recorded source path; an alias, unresolved path or I/O uncertainty cannot qualify.
  Its `.git` entry is **positively absent**: use exception-distinguishing filesystem
  observation, not `File.Exists`/`Path.Exists` false as proof. An empty/malformed
  gitfile is present, and does not qualify.
- A successful **live** registration query from the canonical repository finds no
  source registration, including a matching canonical alias. Query failure or an
  unresolved potential alias remains uncertain. Use `ILandingGit.LiveRegistrationsAsync`,
  not the cached registration read. Keep this extra query off the ordinary path.

If any condition fails, retain the original probe's hold. If all pass, ignore only
that source path-resolution failure and continue the existing land-worktree and
target-checkout lock probes. Do not return success from the whole admission method.
Use one private eligibility helper and the existing Git interface; no new schema,
configuration, generic filesystem framework or lock-classifier semantics.

Rejected: skip all source probes after publication (loses actual-lock controls);
skip whenever `.git` is missing (a damaged registered checkout is unknown); treat
every path error as no lock (masks inaccessible/corrupt repositories); move the whole
protocol before admission (widens mutation/attempt behavior); delete/re-register
foreign content (violates preservation). Passing `HasPublication` is eligibility
for guarded cleanup, not permission to delete or to omit fresh remote proof.

**D-2 — Let the existing protocol own the outcome.** Do not pre-stamp mode or invent
a successful cleanup. It must reload/revalidate the operation, stamp `CleanupRetry`,
and persist the guarded refusal with the same operation ID and publication receipt.
`CompleteRequest` clears the hold and marks the request `Completed`; the subsequent
terminal event is `LandingCleanup`, not another publication. Preserve source bytes,
local task branch and remote source/target identities. Existing receipt/pin/remote
failures take their own named refusal instead of `unregistered_directory`.

**D-3 — Preserve races and uncertainty conservatively.** Admission's observation
is not a deletion snapshot. Leave every protocol/remover recheck, no-follow
confinement, lease and writer gate intact. Cancellation propagates; an I/O or query
failure cannot become proof of absence. A directory that becomes a checkout later
must face the ordinary fresh cleanup identity/content gates. No sleep, timeout
increase, retry policy change, lock removal or process termination is part of this fix.

**D-4 — Keep implementation and verification small and portable.** Production edit:
`server/Application/Services/AgentTaskLandService.cs`; extend existing tests in
`tests/Antiphon.Tests/Application/AgentTaskLandCleanupSafetyTests.cs` and
`AgentTaskLandIndexLockTests.cs`. Update the cleanup/admission paragraph in
`docs/orchestration-loop.md` during Code. Existing Git/removal/state code is the
authority to reuse, not an assigned redesign. Do not add a test class/registry row
or change the legacy C448 argument roster.

**D-5 — Placement comes from live settings.** Read `GET /api/runner-defaults` and
`GET /api/session-runners` again before dispatch. At planning time revision 2 chose
an eligible Linux runner; an eligible Windows runner also existed. This records
capability, not a host pin. Portable Code/TestDesign omit `-Runner` and `-Platform`.
No fleet address belongs in commands. Every ordinary checkpoint below uses the
**portable .NET/PostgreSQL + real Git lane**, with isolated schemas and the existing
assembly-local process limiter. No native Windows-only acceptance row is needed
for the plain-directory repair. Windows rc confirmation remains separately owed
after a reviewed port; use `-Platform Windows` for that lane, without a host pin.

No session input wait is added or changed. This is land-request admission, not
session release. With parking disabled, an existing session waiting for input has
no automatic release deadline (CARD-1083).

## Slices

| Slice / authoring budget | Files and deliverable | Checkpoint boundary |
|---|---|---|
| S1 / 30-45 min | Add the proposed C1536 witnesses to the two existing test files. Strengthen only the unregistered arm of C448 with request/reason observations; preserve its original assertions and 12 arguments. Use existing `LandingSafetyHarness` and `LandingGitFixture`. | Commit/push tests with production unchanged; CP-1 must fail at the intended admission/outcome assertion. No fixture/build error counts as RED. |
| S2 / 30-60 min | Implement D-1 in `AgentTaskLandService.cs`; update its admission comment and the owner paragraph in `docs/orchestration-loop.md`. | Commit/push the fix before CP-2..CP-5; one frozen source SHA and one shared test-project build for these rows. |
| S3 / 30 min | Qualify, inspect request/event/operation evidence, validate receipts, run evidence-diff and OS-witness declaration checks, remove owned alternate outputs and report. No additional production files expected. | Run the table once; failure-driven reruns target the affected row. Fetch master and report conflict-free `git merge-tree --write-tree origin/master HEAD`, never merge it. |

## Verification design

### Inspection

Read the C448 changed-work body, the real Git fixture, the schema/DI/request queue
in `LandingSafetyHarness`, `LandingRemovalControlTests`' one-case wrapper, index-lock
tests, `GitIndexLockTests`, the service/protocol/state and guarded-removal bodies above.
`FixtureGit` inherits production `LandingGit`, uses private real repositories and
its owned-child census; it is not `ControlledLandingGit`. Keep that production path.
The verifier is controlled: these tests prove cleanup admission, not compilation
of a landed payload. No new message-delivery path exists; persisted terminal events
are asserted as request outcomes, not claimed as recipient delivery receipts.

The following names/counts are proposed for TestDesign to bind before Code. All new
methods use `[Arguments]` for the enumerated cases, rather than hidden loops counted
as executions. V-1, V-2 and V-4 go in `AgentTaskLandCleanupSafetyTests`; V-3 goes in
`AgentTaskLandIndexLockTests`. Helper code should stay private to these fixtures.

### Proves it works now

| ID | Exact planned method / executions | Setup, reachability and oracle |
|---|---|---|
| V-1 (RED first) | `C1536_PlainReplacementRecordsCleanupRefusal` / 4 | Real initial publication with refused cleanup; remove the report and registered source; recreate an empty directory or one with `new.txt`. Cross both with ordinary re-POST / `RequestCleanupRetryAsync`. Before retry assert no `.git`, a successful listing with no source row, ordinary directory type, and saved valid publication. Drive queued service execution. Require Complete, fresh request Completed/not pending, null hold reason, same operation/receipt, CleanupRetry, Refused, exact `unregistered_directory`, `DirectoryRemoved=false`, `BranchRemoved=false`, replacement and bytes intact, local branch and remote refs unchanged, unchanged verifier call count, no source removal/rebase/push, exactly one new LandingCleanup event and no new publication event. All four must fail against unchanged admission. |
| V-2 | `C1536_UncertainSourceRetainsAdmissionHold` / 9 | Independently vary only: no active-operation pointer; confirmation time null; verified SHA null; verified SHA empty; crossed operation source path; registered checkout with gitfile removed; unregistered replacement with an empty gitfile; live registration query failure; source directory symlink to a foreign plain directory. Queue a valid request before corrupting evidence so request creation cannot mask admission. Assert the actual DB/filesystem input before RunQueuedAsync. Each retains Held/path-error, zero new request attempts, no cleanup/publication mutation and all content. Keep other gates eligible; the alias case uses real symlink metadata and canonical observation. |
| V-3 | `C1536_PublishedRetryPreservesIndexLockHolds` / 6 | Real lock at source / dedicated land / main target checkout, each fresh / stale. Source cases retain a registered checkout; land/main cases use the proven plain replacement so the new exception is traversed. Assert lock bytes and exact path in HoldDetail, held/stale code, zero request attempts and unchanged publication. Remove only the fixture-owned lock and retry: the plain cases then reach `unregistered_directory`. Use the fixture census, never host-wide timing luck. |
| V-4 | `C1536_PlainReplacementRechecksCleanupAuthority` / 2 | Same plain replacement and valid saved receipt, then independently rewrite the real remote target away from the landed source, or remove the operation's source recovery pin. Reach CleanupRetry/Completed with cleanup Refused and exact `remote_no_longer_contains_source` / `recovery_pin_changed`, not a lock hold. Preserve replacement bytes, source branch and publication receipt; never republish. |

For V-2, hold assertions are the discriminating oracle: preserving bytes alone could
pass even if an admission guard were removed and a later remover happened to refuse.
For V-4, assert the exact authority refusal: `unregistered_directory` alone could
mask a skipped remote/pin recheck. Request and event reads use a fresh DbContext and
the exact new request ID, not `Single` over both historical requests.

### Absent / empty / value inputs (CARD-1525/1526)

| Real input | Absent | Empty | Value / uncertainty |
|---|---|---|---|
| `.git` filesystem entry | V-1 explicitly asserts no entry after real unregister | V-2 creates a zero-byte **file**, asserts it exists and Length=0, expects Held | V-3 source retains the real nonempty gitfile and real lock; V-2 registered-broken distinguishes absent entry from absent registration |
| Replacement contents | R-2 covers a source path that is absent | V-1 empty-directory cases assert zero entries and retain the directory | V-1 populated cases retain literal bytes; no dictionary/anonymous filesystem stand-in |
| `AgentTaskLanding.VerifiedSourceSha : string?` | V-2 persists/readbacks null | V-2 persists/readbacks `""` | V-1 uses the actual full verified OID from publication; no invented default SHA |
| Active operation / confirmation | V-2 readbacks null pointer / nullable confirmation | No empty representation for nullable Guid/DateTime; do not substitute Guid.Empty for null | V-1 actual published entity and timestamp |
| Registration evidence | Successful real listing lacks the source row in V-1 | Zero matching source rows is not a failed query or an empty `.git` file; the canonical row remains present | V-2 registered-broken has the source row; injected command failure is unknown, never an empty success |

There is no new optional configuration input. Native-only ACL denial is not simulated
by an empty value; uncertain Git enumeration uses the existing `BeforeCommand` failure
seam as supplemental fault evidence. The primary success and lock witnesses use real
filesystem/Git types. The real symlink case is portable on the Linux lane; Windows
junction/short-name qualification is not asserted by that Linux execution.

### Guards the regression

| ID | Existing selection / executions | Required invariant |
|---|---|---|
| R-1 | `AgentTaskLandCleanupSafetyTests.C448_V18_CleanupRetryPreservesChangedWork` / 12 | Existing changed-work, registration, remote-change and missing-pin protections retain bytes and publication, stamp retry and do not rebase/remove/push. The original unregistered red must now pass. |
| R-2 | Five executions in CP-4 | `Existing_source_worktree_with_broken_git_holds_as_index_lock` (1), `Reland_after_removed_worktree_is_not_held_as_index_lock` (1), `Removing_the_lock_resumes_the_land` (1), `C688_AdmissionProbesLandWorktreeAndMainCheckoutWithoutListing` (2). Preserve unknown checkout hold, absent-source behavior, real-lock release, and ordinary admission's no-list fast path. |
| R-3 | Three executions in CP-5 | `GitIndexLockTests.Evaluate_path_error_is_held`, `C1436_census_drops_git_that_exits_mid_census`, `C1436_census_keeps_live_holders`. Preserve global error classification and CARD-1436 semantics. |

### Guard inventory and positive controls

Proposal for the separate post-land Mutation stage; no mutation is executed by Plan
or ordinary Code. Each row is an independently bypassable guard. Use the exact named
method, one compiling defect at a time, then restore and run that same method green.
The stated variant must be independently arranged with other gates admitting; a
downstream guard masking the asserted effect requires TestDesign to repair the witness.

| Guard / PC | Deliberate defect | Exact witness and failing observation |
|---|---|---|
| G-1 / PC-1 | Disable the narrow exception | V-1 method: Complete/CleanupRetry becomes Held/Fresh |
| G-2 / PC-2 | Treat absent/invalid publication evidence as eligible | V-2 method, no-operation/unconfirmed/null/empty SHA variants: Held requirement fails |
| G-3 / PC-3 | Omit task/operation coordinate agreement | V-2 method, crossed-path: later named protocol refusal replaces required admission hold |
| G-4 / PC-4 | Convert failed live registration query into absence | V-2 method, query-error: request enters cleanup instead of remaining Held |
| G-5 / PC-5 | Ignore a matching live registration | V-2 method, registered-broken: Held requirement fails |
| G-6 / PC-6 | Accept a present empty gitfile as absent | V-2 method, empty-gitfile: Held requirement fails |
| G-7 / PC-7 | Omit canonical-path agreement | V-2 method, source-alias: admission proceeds to a later confinement refusal |
| G-8 / PC-8 | Skip the registered source lock probe | V-3 method, source: Held/path/attempt oracle fails |
| G-9 / PC-9 | Skip the dedicated land lock probe | V-3 method, land: Held/path oracle fails after the source exception |
| G-10 / PC-10 | Skip the main target lock probe | V-3 method, main: Held/path oracle fails after the source exception |
| G-11 / PC-11 | Skip fresh cleanup remote containment | V-4 method, remote-rewrite: expected remote refusal becomes unregistered-directory refusal |
| G-12 / PC-12 | Skip source recovery-pin validation | V-4 method, pin-missing: expected pin refusal disappears |
| G-13 / PC-13 | Change the unregistered-directory branch to claim clean removal | V-1 method: exact refusal/component and preserved-directory observations disagree |
| G-14 / PC-14 | Remove the persisted CleanupRetry assignment | V-1 method: fresh-context mode assertion fails |
| G-15 / PC-15 | Emit a new publication event on cleanup retry | V-1 method: LandingCleanup/publication event counts fail |

15 proposed guards, 15 distinct PC mappings. TestDesign must split any newly introduced
independent safety guard and bind recipes to final implementation lines. In particular,
do not credit a PC for a failure during fixture setup, build, or zero-test selection.
Existing lease/writer/deletion gates remain unchanged; this proposal does not rerun
their broad matrices. Unregistered-content preservation remains a required V-1 oracle
even where a classification PC reaches a later independent safety guard.

### Out of scope and cost

No holder diagnostics, PTY lifecycle, janitor deletion, runner sync, retry timers,
new API/schema, whole assembly/namespace/Unit runs or live Windows qualification.
No Windows-only CP is proposed: Linux proves the defect and proposed logic. If
TestDesign adds Windows-native behavior, add its exact OWED desktop-lane row before
Code; a skipped native test cannot satisfy it. Current supervisor PID 209072 remains
untouched; any Windows rc confirmation is separately commissioned after that run.

Estimated ordinary checkpoint floor: **23 minutes**, excluding slot waits and any
necessary checkpoint-tool bootstrap (budget 3 minutes, gated and reported). S1 has
one red test-project build; S2 has **one** green test-project build shared by every
green row. Source changes require the second build; there are no per-row rebuilds.
Green total: 41 executions; red total: 4. Proposed mutation floor: 15 cycles at
about 2 minutes each plus 8 minutes setup/build = **38 minutes**, separate from
ordinary Code. These are estimates, not measured receipts. No repeat after green.

Use the checkpoint tool `run --plan <this-plan> --rows <slice-row-ids>
--expected-source-sha <full-sha>` through the build-slot wrapper for its launcher;
row builds acquire their own slots. Wait for the same run until exit is not 75.
Preserve unedited CHECKPOINT lines, actual roster/counts and exact source SHA;
validate CP-2..CP-5 as clean green receipts. Confirm unrelated red at base using
only its failing method. Generated receipts remain ignored. Remove owned `bin-c1536-*`
outputs after retaining evidence. TestDesign finalizes this closed list before Code.

### Checkpoints

All rows: **portable .NET/PostgreSQL + real Git lane**, effective runner defaults.
One exact filter per row; every method OR operand has a trailing wildcard (CARD-0931).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1536-red/` | plain-replacement-red | `/*/*/AgentTaskLandCleanupSafetyTests/C1536_PlainReplacementRecordsCleanupRefusal*` | V-1 | intentional RED: 4 admission/outcome assertion failures, 0 skipped | 4 | 9 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1536-green/` | replacement-and-guards | `/*/*/(AgentTaskLandCleanupSafetyTests*)\|(AgentTaskLandIndexLockTests*)/C1536_*` | V-1..V-4 | all 21 passed, 0 failed/skipped | 21 | 9 |
| CP-3 | S2 | CP-2 | changed-work | `/*/*/AgentTaskLandCleanupSafetyTests/C448_V18_CleanupRetryPreservesChangedWork*` | R-1 | all 12 passed, 0 failed/skipped | 12 | 2 |
| CP-4 | S2 | CP-2 | lock-admission | `/*/*/AgentTaskLandIndexLockTests/(Existing_source_worktree_with_broken_git_holds_as_index_lock*)\|(Reland_after_removed_worktree_is_not_held_as_index_lock*)\|(Removing_the_lock_resumes_the_land*)\|(C688_AdmissionProbesLandWorktreeAndMainCheckoutWithoutListing*)` | R-2 | all 5 passed, 0 failed/skipped | 5 | 2 |
| CP-5 | S2 | CP-2 | lock-classification | `/*/*/GitIndexLockTests/(Evaluate_path_error_is_held*)\|(C1436_census_drops_git_that_exits_mid_census*)\|(C1436_census_keeps_live_holders*)` | R-3 | all 3 passed, 0 failed/skipped | 3 | 1 |

## Rollout, activation and rc status

This plan and subsequent reviewed repair land on master first. No migration or
feature flag is needed. Existing Held requests will be re-evaluated by the ordinary
sweep after activation; qualifying replacement cases should finish as the named
cleanup refusal, with content retained. Do not clear holds or remove locks/content
manually to demonstrate success. Ordinary registered checkout locks still wait.

The current rc `5a126f6fc5ae4e16d803b628716b8f0485a37174` has the defect and the
reported Windows red; it is not made releasable by this documentation. A separately
owned reviewed rc port must adapt the narrow service change and witnesses to that
line, run its exact portable selection and confirm the original unregistered case
on the Windows qualification lane after the protected run. Re-read refs/settings
at dispatch; do not assume today's rc tip remains current. Any native obligation
remains OWED until a fresh receipt names the tested rc SHA.

Master publication is not product activation. The activation owner deploys the
reviewed rc tip through the existing operational procedure, checks `/api/version`
against that source, and observes an eligible retry's request/mode/reason and retained
content. No new live reproduction/deletion or restart is authorized by this Plan.
Binary rollback preserves data but restores the misleading hold; it is not a fix.

--- next stage ---
next: test-design
handoff: Bind the narrow published-plain-source admission exception and proposed V/R/PC witnesses; finalize the 5-row red/green checkpoint list with one test-project build per source group. Preserve real locks, uncertain identity, receipt/remote guards and foreign bytes; keep Windows rc confirmation separately owed.
artifact: docs/superpowers/plans/2026-10-23-card-1536-cleanup-unregistered-directory-plan.md
