# CARD-1065 S4 Code evidence — implementation pushed, V-10 fixture repair pending

S4 source-policy is implemented and pushed, but ordinary verification is incomplete.
Latest CP-4: **3 executed, 2 passed, 1 failed, 0 skipped**. V-9 and V-11 passed;
V-10 failed in fixture setup because it changes the immutable SourceLandingOperationId
after the task has been inserted. This is our fixture defect, not inherited red.
The brief's two-repair cap has been reached, conservatively counting the initial
regression assertion-overload correction and the subsequent test-client completion.
One further fixture-only repair and CP-4 rerun needs caller authorization. No further
source edit was made after that failure. This branch is not ready for Review or landing.

## Ownership and source

- Original Code task / eventual landing owner: `101f3188-1d37-42d3-9311-7dd07164279c`.
- Branch: `feat/card-task-101f3188`.
- Worktree: `/work/worktrees/task-101f3188`.
- Desktop companion, not reachable here: `C:\Antiphon\worktrees\card-task-101f3188`.
- Full task base: `9b3f207d78176239adf14f1179ea19dc47589dc8`.
- Actual latest tested source: `109a567b909649b59191d83c1957dd3f878ee710`.
- Initial implementation: `eab5380082c9fdb59b77040e572a829951edad14`.
- Executed red-first regression source: `309050073547edadc4d4b2d5cba7346b015213f1`.
- Earlier pushed commits: `538118bc743828552086b3665e0438367dccec5f`,
  `35d443bdc7fd2017f7d87880f992a299a72d8c9c`, `42838d355ffa5974d02057773eb4a7f8cbcfc7ff`.
- The final caller report supplies the subsequently pushed documentation SHA.
- Plan: `docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md`.
- Predecessor evidence: `docs/investigations/2026-10-05-card-1065-s3-code-evidence.md`.
- Authenticated GET `/api/runner-defaults` and `/api/session-runners` were read again
  on resume. No runner/platform pin, fleet-location constant or host-budget change.

## Implemented footprint

- `TaskParkPublicationService`: dormant preparation, durable source intent before Git/wire
  I/O, ownership and override refusals, short task-before-session transactions, fresh
  source verification, and conditional receipt acceptance. Action identity is the park ID.
  No stop, autosave, settlement, Commit child, or release reservation is performed.
- `LocalTaskParkPublisher`: existing owned Git-child and repository-lease seams;
  exact branch/checkout/common-directory identity, sequencer/index/clean-source checks,
  baseline ancestry, captured endpoint, ordinary non-forced owned-ref push, fresh exact
  remote observation and final source recheck. Shared verifies only, never pushes.
  ReadOnly requires its known base and returns a separate NoSourceChanges proof.
  Failed reads are Unknown; mismatched authority stays held.
- `TaskParkPublicationDtos`: typed publication versus NoSourceChanges evidence. Durable
  receipt digest binds all safety coordinates; replay reconstructs those fields from the
  stored episode. Runner observation time and pre-push remote SHA are telemetry, excluded
  from that durable digest and not authority.
- `BlockedTaskParkingService`: report digest/reference fence; initial source coordinates
  may come from a captured progress baseline for Shared/ReadOnly. Its internal state-only
  CAS remains distinct from publication authority, preserving S1's contract.
- `DelegationReportFormatter`: scoped truthful WIP-before-block instructions; SourceLanding,
  ReadOnly and explicit NoCommit exclusions; suppress conflicting remote-push text.
- `TaskParkPublicationTests` and a fixture service-provider accessor: real migrated isolated
  PostgreSQL, bare Git remotes, owned Git children with isolated Git config, real leases and
  reservations. No successful publication/release receipts were seeded.

Parking remains default-off. No automatic caller or DI activation was added; S10 owns
activation. No Working session, provider, daemon or deployed stack was stopped/restarted.

S5 integration requirements are explicit: obtain the bound runner's authoritative
common-directory identity (the legacy mirror response supplies only Path); never substitute
or guess the desktop identity. The release coordinator must use the persisted publication
action identity, since the runner requires publication.ActionId == release.ActionId.
Its current ordinary reservation path generates an independent action ID and needs the
planned S5 composition. Shared/ReadOnly require a captured, task-owned baseline; unknown
ownership is held. Runtime release, idle waiting, transport routing integration and live
activation are not claimed by S4's source tests.

## Executed verification and remaining repair

The caller explicitly resumed with CP-4 only, narrow filters, no whole-Unit. That scope
wins over the generic Final-profile boilerplate appended to the brief. Only
`/*/*/TaskParkPublicationTests/C1065_*` was executed. No full assembly, namespace,
other checkpoint row, deliberate mutant or unchanged-proof repetition was run.

| ID | Exact method | Latest outcome |
|---|---|---|
| V-9 | TaskParkPublicationTests.C1065_WorkspaceModesPreservePublicationAuthority | Passed, 1: local/remote actual publication, fresh replay, dirty source, unavailable endpoint, Shared already-published/no-push/unpublished/foreign reservation/task owner/unauthorized ref, ReadOnly clean/dirty/advanced base |
| V-10 | TaskParkPublicationTests.C1065_CommitInstructionsAndRefusalsRespectOverrides | Failed, 1: formatter and NoCommit checks execute; fixture fails while changing immutable SourceLandingOperationId; later SourceLanding/recovery/uncertain-owner/no-Commit-child assertions are not established |
| V-11 | TaskParkPublicationTests.C1065_PublicationReceiptCannotAuthorizeChangedAttempt | Passed, 1: changed stored report; independent altered receipt fields before first acceptance; valid real-source acceptance/replay; final intent race; second DB connection takes task row lock during paused Git; no release reservation |

The fresh TRX was inspected for all three exact class/method identities and nonzero counts.
Latest build: 0 errors / 620 warnings, 117.1624322s checkpoint build; row host 67.6401766s;
overall 3m06s. Source stayed frozen at the committed expected SHA throughout every run.
SourceState=clean and buildSource=verified describe provenance, not a passing certificate.
Receipt validation exited 2:

```text
CHECKPOINT SOURCE INVALID reason=row_failed
```

V-10 diagnosis: EF sets SourceLandingOperationId and SourceLandingSha to
PropertySaveBehavior.Throw after save, with a foreign key to AgentTaskLanding.
Do not bypass this guard. Proposed bounded repair: extend the test factory to insert a
separate source owner and a referenced landing operation, then set SourceLandingOperationId
when inserting the excluded task. Use a separate SourceLanding PublicationWorld; keep the
ordinary NoCommit/recovery world ordinary. No production change, timeout increase, retry or
assertion relaxation is needed. The referenced landing row need not claim successful
publication: this tests unconditional custody exclusion, not landing success.

The two completed correction groups were:
1. Initial tests used unsupported Shouldly string-message overloads. Corrected the call
   sites without changing the assertions, then obtained intended three-method defect red.
2. The expanded fake client omitted StreamEventsAsync. Completed the required member;
   also fenced final stored intent and distinguished unavailable endpoint reads, with
   matching ordinary scenarios. The next run passed V-9/V-11 and exposed the V-10 setup bug.

The red-first run at 309050073 failed all three intended assertions: contradictory
SourceLanding Shared commit text (G-73), absent WIP-before-block text (G-76), and acceptance
after report mutation (G-92). This is executed defect evidence, not deliberate Mutation.

## Unedited checkpoint receipts

```text
CHECKPOINT CP-4 commit=35d443bdc7fd2017f7d87880f992a299a72d8c9c build=n/a filter=/*/*/TaskParkPublicationTests/C1065_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-4 commit=42838d355ffa5974d02057773eb4a7f8cbcfc7ff build=failed filter=/*/*/TaskParkPublicationTests/C1065_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=42838d355ffa5974d02057773eb4a7f8cbcfc7ff sourceState=clean buildSource=unknown
CHECKPOINT CP-4 commit=309050073547edadc4d4b2d5cba7346b015213f1 build=ok filter=/*/*/TaskParkPublicationTests/C1065_* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-101f3188/.antiphon/checkpoints/20261006-005234-3919/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=309050073547edadc4d4b2d5cba7346b015213f1 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=eab5380082c9fdb59b77040e572a829951edad14 build=failed filter=/*/*/TaskParkPublicationTests/C1065_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=eab5380082c9fdb59b77040e572a829951edad14 sourceState=clean buildSource=unknown
CHECKPOINT CP-4 commit=109a567b909649b59191d83c1957dd3f878ee710 build=ok filter=/*/*/TaskParkPublicationTests/C1065_* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-101f3188/.antiphon/checkpoints/20261006-010645-6c3c/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=109a567b909649b59191d83c1957dd3f878ee710 sourceState=clean buildSource=verified
```

Generated evidence remains ignored in `.antiphon/checkpoints/`:

| Run | Result and actual work | Build slot |
|---|---|---|
| 20261006-000121-5e62 | Previous turn: 30m deadline, exit 5, zero builds/tests, no TRX | Never granted; occupied=4/budget=4 through elapsed=1785s |
| 20261006-004903-5b86 | Resume: build failed, 5 assertion-overload compile errors, zero tests | granted; waited=0s; build 98.4244332s |
| 20261006-005234-3919 | Intended red-first: 0 pass / 3 fail | granted; waited=0s; build 99.9283217s |
| 20261006-010329-678c | Initial implementation: build failed, missing interface member, zero tests | granted; waited=0s; build 68.3631227s |
| 20261006-010645-6c3c | Current implementation: 2 pass / 1 fixture fail | granted; waited=0s; build 117.1624322s |

For build failures, row slot=skipped means the test row never started; the actual build
slot above was granted. The first attempt's row waited=0s does not describe its preceding
30-minute build admission wait. Manifest summary builds=13 includes twelve unused entries;
only CP-4 was selected. Every checkpoint run/wait was awaited through its terminal exit.

Only unlisted builds were required checkpoint-tool bootstraps through the host slot gate:
- Previous turn: `.antiphon/c1065-s4-driver.log`, 0 errors / 1 warning, MSBuild 4.93s;
  slot=granted waited=330s, lease=1830da34-7ddd-49a9-8071-379d0fc96035, held=6s.
- Resume: `.antiphon/c1065-s4-resume-driver.log`, 0 errors / 1 warning, MSBuild 2.62s;
  slot=granted waited=0s, lease=53cb6442-505b-4456-a39d-92e666df97e4, held=3s.
No unleased driver, timeout widening or other owner's process stop was used.

## Coverage and deferred obligations

Read-only full-plan coverage at eab538008 exited 2 because future selected classes are absent;
this is not clean static coverage. It launched no build/test driver. Complete output:

```text
PLAN-COVERAGE schema=1 mode=static plan="docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md" planSha256=8d780a561c59779df30b528061caea4c6842e64dfeb8d89259b377b0530cb7f3 inputsSha256= files=0
COVERAGE code=INPUT_INVALID planLine=0 planColumn=1 id= test="" name="" testPath="tests/Shared/TestClassificationMetadata.cs" testLine=0 detail="unresolved selected class"
PLAN-COVERAGE-END obligations=0 matched=0 missing=0 unmapped=0 pcIssues=0 pcAdvisories=0 result=invalid reachability=unproven
```

The reported path is the last source enumerated, not a defect in that file. Rerun coverage
on the integrated candidate.

- V-1, V-2, V-3, V-4, V-5, V-6, V-7, V-8: predecessors; not rerun or claimed passed here.
- V-10: unresolved here; V-9/V-11 have the passing results above, CP-4 overall remains red.
- V-12, V-13, V-14: S5; V-15, V-16: S6; V-17, V-18, V-19: S7;
  V-20, V-21, V-22, V-27: S8; V-23, V-24: S9; V-25, V-26: S10 — all deferred.
- R-1, R-2, R-3, R-4, R-5, R-6: S11, none run or passed here.
- CP-1–3 and CP-5–13: outside this slice, not run. Whole Unit explicitly excluded.
- Integrated Linux/Windows, provider/OS and activation acceptance remain S11/rollout work.

Every S4 PC and every plan variant remains pending method-scoped SourceLanding Mutation:
PC-68, PC-69, PC-70, PC-71, PC-72, PC-73, PC-74, PC-75, PC-76, PC-77, PC-78, PC-79,
PC-80, PC-81, PC-82, PC-83, PC-84, PC-85, PC-86, PC-87, PC-88, PC-89, PC-90, PC-91,
PC-92, PC-93. This includes Shared variants, ReadOnly dirty/advanced variants, every brief
exclusion, every receipt coordinate including park/action separately, and lock/I/O variants.
Other slices' PC-1–67 and PC-94–200 remain with their owners. Mutation owns deliberate
mutants, red/restore/green and missing-control discovery after ordinary Review and original
Code task landing; no PC is discharged by these runs.

## Guard, cleanup and next action

Full task-range guard at tested HEAD succeeded:

```text
EVIDENCE range base=9b3f207d78176239adf14f1179ea19dc47589dc8 head=109a567b909649b59191d83c1957dd3f878ee710
EVIDENCE result commits=6 entries=0 violations=0 base=9b3f207d78176239adf14f1179ea19dc47589dc8 head=109a567b909649b59191d83c1957dd3f878ee710
```

Rerun the guard after this documentation commit; the final caller report records its outcome.
Generated JSON/TRX/logs remain ignored; this Markdown preserves essential receipts without
moving generated payloads into source. The checkpoint tool cleaned all 28 producer-owned
CP-4 output directories; its dead executor copies had already been removed by wait.
The isolated bootstrap output was removed as well. No bin-c1065-* directory or owned
build/test/checkpoint run remains active. Final remote-SHA verification is in the caller report.

Next is Code, after authorization for the one additional fixture repair described above.
Rebootstrap the checkpoint tool through scripts/build-slot.ps1 to bin-c1065-s4-driver/.
Then commit/push and run CP-4 with committed HEAD as expected source; inspect the fresh TRX,
rerun the evidence guard, and send to Review only after all three pass. No whole-Unit run.
Restart: **none**. Future activation owner: caller/orchestrator after integrated qualification.
