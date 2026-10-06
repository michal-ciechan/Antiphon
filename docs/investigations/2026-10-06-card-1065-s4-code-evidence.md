# CARD-1065 S4 Code checkpoint — blocked before implementation

S4 is incomplete. The prescribed red-first CP-4 attempt never acquired a build slot.
Its unchanged 30-minute total-run deadline expired with exit 5, without a build or test
execution. Only three initial regression candidates are committed; they are neither
executed defect evidence nor the complete V-9/V-10/V-11 scenario inventory. No production
implementation was made. This branch is not ready for ordinary Review or landing.

## Ownership and source

- Original Code task / eventual landing owner: `101f3188-1d37-42d3-9311-7dd07164279c`.
- Branch: `feat/card-task-101f3188`.
- Worktree: `/work/worktrees/task-101f3188`.
- Desktop companion, not reachable here: `C:\Antiphon\worktrees\card-task-101f3188`.
- Full task base: `9b3f207d78176239adf14f1179ea19dc47589dc8`.
- Test-only commits, both pushed:
  - `538118bc743828552086b3665e0438367dccec5f`.
  - `35d443bdc7fd2017f7d87880f992a299a72d8c9c`.
- Attempted checkpoint source / expected SHA: `35d443bdc7fd2017f7d87880f992a299a72d8c9c`.
- Actual tested SHA: **none**. No test assembly was built or executed.
- The final response supplies the subsequently pushed documentation commit SHA.
- Plan: `docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md`.
- Predecessor evidence read: `docs/investigations/2026-10-05-card-1065-s3-code-evidence.md`.
- Authenticated GET `/api/runner-defaults` and `/api/session-runners` completed.
  No runner/platform pin, fleet-location constant or host-budget change was introduced.

## Footprint and outstanding implementation

`tests/Antiphon.Tests/Application/TaskParkPublicationTests.cs` contains initial checks for:

- G-73: SourceLanding must not receive a contradictory Shared commit/push instruction.
- G-76: an ordinary writable Worktree brief must require a truthful WIP commit before blocked.
- G-92: a changed stored report must not advance the captured park episode.

The initial state-only publication expectation was corrected before execution: S1 explicitly
separates storage transitions from publication/release authority. Preserve that distinction.
All three current methods remain unexecuted and may need fixture or assertion correction
once actual results are available. No green, red-on-defect, or inherited-red claim is made.

Still required: the TaskParkPublicationService, local strict Git/lease adapter, local/remote
Worktree publication policy, Shared authorized-ref/no-other-writer/no-push policy, ReadOnly
clean-known-base proof, SourceLanding/NoCommit/commit-recovery refusals, scoped formatter
instructions, durable bound receipt acceptance and all real Git/PostgreSQL V-9–V-11 scenarios.
The repair cap of two rounds has not been consumed; implementation has not started.

Useful integration observation: WorkspaceParkBinding requires a repository identity digest,
endpoint fingerprint, full ref, baseline and complete task/session/generation coordinates.
The legacy PhoneHomeWorkspaceMirrorResponse currently returns only Path. The eventual caller
must obtain/capture the runner identity through an authoritative path; do not derive it from a
fleet path or substitute a desktop common-directory identity. Also keep S1's internal storage
CAS distinct from receipt-based release authority. No task transaction may span Git/runner I/O.

## Attempted checkpoint and provenance

The explicit S4 brief and plan restrict this dispatch to CP-4 and say no whole-Unit.
That specific scope takes precedence over the appended generic Final-profile text.
Only CP-4 was selected. No full assembly, namespace, other checkpoint row, deliberate
mutant, repetition, provider launch or manual release was run.

The checkpoint tool was bootstrapped through the required host slot gate, the only extra
build. It succeeded with 0 errors / 1 warning; MSBuild elapsed 4.93 seconds. Bootstrap log:
`.antiphon/c1065-s4-driver.log`. Unedited slot lines:

```text
BUILD SLOT granted lease=1830da34-7ddd-49a9-8071-379d0fc96035 waited=330s maxcpucount=6
BUILD SLOT released lease=1830da34-7ddd-49a9-8071-379d0fc96035 held=6s
```

CP-4 was started with `--after S4 --expected-source-sha 35d443bdc7fd2017f7d87880f992a299a72d8c9c`.
Every foreground wait was awaited, including repeated exit 75, through terminal exit 5.
Source stayed unchanged throughout that checkpoint. Run ID: `20261006-000121-5e62`.
Raw evidence remains ignored under `.antiphon/checkpoints/20261006-000121-5e62/`.

Unedited checkpoint line:

```text
CHECKPOINT CP-4 commit=35d443bdc7fd2017f7d87880f992a299a72d8c9c build=n/a filter=/*/*/TaskParkPublicationTests/C1065_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
```

The row's `slot=skipped waited=0s` describes the test row, which never started. It does not
represent the preceding build-slot wait. The executor repeatedly received HTTP 409
`build_slot_busy`, occupied=4, budget=4, queuePosition=2, through elapsed=1785s; it then
ended at 30m00s overall. Last acquisition and completion lines, unedited:

```text
2026-10-06T00:31:08.6482127+00:00 BUILD SLOT label="build:bin-c1065-cp4" operation=acquire status=409 reason=build_slot_busy elapsed=1785s detail="{\u0022type\u0022:\u0022build_slot_busy\u0022,\u0022title\u0022:\u0022build_slot_busy\u0022,\u0022status\u0022:409,\u0022occupied\u0022:4,\u0022budget\u0022:4,\u0022queuePosition\u0022:2,\u0022retryAfterMs\u0022:15000}"
2026-10-06T00:31:23.8636805+00:00 done exit=5
```

The summary's `builds: 13` counts manifest entries, not executed builds: structured evidence
lists CP-4's build as failed before source observation and the twelve other builds unused.
Actual checkpoint builds=0, test executions=0, assertion failures=0; no TRX exists.
Consequently none of the required three class/method identities has execution evidence.

Receipt validation exited 2, as expected for an unexecuted row:

```text
CHECKPOINT SOURCE INVALID reason=row_failed
```

There is no clean successful checkpoint certificate or verified test build provenance.
No timeout was widened, assertion loosened, unleased retry attempted or other owner's process
stopped. This is a capacity/deadline failure, not a test assertion failure.

## Ordinary and Mutation obligations

| ID | Required method | Actual result |
|---|---|---|
| V-9 | TaskParkPublicationTests.C1065_WorkspaceModesPreservePublicationAuthority | Not run; full scenario implementation pending |
| V-10 | TaskParkPublicationTests.C1065_CommitInstructionsAndRefusalsRespectOverrides | Not run; full scenario implementation pending |
| V-11 | TaskParkPublicationTests.C1065_PublicationReceiptCannotAuthorizeChangedAttempt | Not run; full scenario implementation pending |

V-1, V-2, V-3, V-4, V-5, V-6, V-7 and V-8 belong to predecessors and were not rerun.
V-12, V-13, V-14, V-15, V-16, V-17, V-18, V-19, V-20, V-21, V-22, V-23, V-24,
V-25, V-26 and V-27 remain with later slices. R-1, R-2, R-3, R-4, R-5 and R-6 remain
with S11. None is claimed passed here. CP-1–3 and CP-5–13 were not run. Whole Unit,
integrated Linux/Windows qualification and manual activation acceptance remain outside this
explicit S4 selection; this attempt discharges none of them.

Every S4 control and all its plan variants remain pending method-scoped SourceLanding Mutation:
PC-68, PC-69, PC-70, PC-71, PC-72, PC-73, PC-74, PC-75, PC-76, PC-77, PC-78, PC-79,
PC-80, PC-81, PC-82, PC-83, PC-84, PC-85, PC-86, PC-87, PC-88, PC-89, PC-90, PC-91,
PC-92 and PC-93. Shared unauthorized/unpublished/other-writer variants, ReadOnly dirty/advanced
variants, every brief override and each independent receipt-coordinate variant are all pending.
Other slices' PC-1–67 and PC-94–200 also remain with their SourceLanding owners. Mutation owns
deliberate mutants, red/restore/green and missing-control discovery after implementation,
ordinary Review and original-Code-task landing; no control is discharged by this attempt.

## Continuation, guard and cleanup

Once a host slot is available, continue Code on this original task/branch. Rebuild the checkpoint
tool through `scripts/build-slot.ps1` to an isolated `bin-c1065-s4-driver/`, then run:

```sh
dotnet tools/Antiphon.Checkpoints/bin-c1065-s4-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md --after S4 --expected-source-sha "$(git rev-parse HEAD)"
```

Obtain the red-first results, implement/expand S4's complete ordinary tests, commit/push each
slice, and rerun CP-4 after each necessary repair. Do not send this WIP to Review or land it.
Next becomes Review only when implementation and ordinary verification are complete.

Before the final response, run the full-range evidence guard after this documentation commit:

```sh
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 9b3f207d78176239adf14f1179ea19dc47589dc8 -HeadRef HEAD
```

Its actual outcome and final pushed SHA are supplied in the caller report. Generated evidence
stays ignored. The checkpoint waiter removed its dead executor shadow copy. No CP-4 output
directory was created; the one producer-owned bootstrap directory is removed before settlement.
No owned build/test/executor remains active. Restart: **none**; future activation owner is the
caller/orchestrator after integrated qualification. Parking remains default-off and no Working
session was stopped.
